using System.Globalization;
using Microsoft.Extensions.Options;
using PrintServer.Backends.Common;
using PrintServer.Core.Printing;

namespace PrintServer.Backends.Ipp;

public sealed class IppBackend(HttpClient httpClient, IOptions<IppOptions> options, bool disposeHttpClient = false) : IPrinterBackend, IDisposable
{
    private const string Language = "en";
    private const int PrinterStateIdle = 3;
    private const int PrinterStateProcessing = 4;
    private const int JobStatePending = 3;
    private const int JobStatePendingHeld = 4;
    private const int JobStateProcessing = 5;
    private const int JobStateCompleted = 9;

    private readonly IppOptions _options = options.Value;
    private int _requestId;

    public async Task<IReadOnlyList<PrinterInfo>> ListPrintersAsync(CancellationToken ct)
    {
        var printers = new List<PrinterInfo>();
        for (var i = 0; i < _options.Printers.Count; i++)
        {
            var configured = _options.Printers[i];
            printers.Add(new PrinterInfo(configured.Name, i == 0, await ReadAvailabilityAsync(configured, ct).ConfigureAwait(false)));
        }

        return printers;
    }

    public async Task<PrintResult> PrintAsync(PrintRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var printer = FindPrinter(request.PrinterName);
        if (printer is null)
        {
            return PrintResult.Failure($"Unknown printer '{request.PrinterName}'.");
        }

        if (request.Copies < 1)
        {
            return PrintResult.Failure("Copies must be at least 1.");
        }

        var fullPath = Path.GetFullPath(request.FilePath);
        if (!File.Exists(fullPath))
        {
            return PrintResult.Failure($"File '{request.FilePath}' does not exist.");
        }

        try
        {
            var printerUri = IppUri.ToIpp(printer.Uri);
            var jobName = string.IsNullOrWhiteSpace(request.JobName) ? Path.GetFileName(fullPath) : request.JobName;
            var header = IppRequestBuilder.Build(
                IppOperations.PrintJob,
                NextRequestId(),
                [
                    IppAttribute.Charset("attributes-charset", "utf-8"),
                    IppAttribute.NaturalLanguage("attributes-natural-language", Language),
                    IppAttribute.Uri("printer-uri", printerUri),
                    IppAttribute.NameWithoutLanguage("requesting-user-name", _options.UserName),
                    IppAttribute.NameWithoutLanguage("job-name", jobName),
                    IppAttribute.MimeMediaType("document-format", _options.DocumentFormat ?? IppDocumentFormats.ForFile(fullPath)),
                ],
                [IppAttribute.Integer("copies", request.Copies)]);

            var httpUri = IppUri.ToHttp(printer.Uri);
            var response = await SendAsync(httpUri, header, File.OpenRead(fullPath), ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return PrintResult.Failure($"IPP Print-Job failed: {IppStatus.Describe(response.StatusCode)}");
            }

            var jobId = response.FindGroup(IppTags.JobAttributes)?.GetInt32("job-id");
            return jobId is null
                ? PrintResult.Failure("The printer accepted the job but returned no job-id.")
                : PrintResult.Success(BackendJobId.Format(jobId.Value, printer.Name));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException or IOException)
        {
            return PrintResult.Failure($"IPP request to '{printer.Name}' failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return PrintResult.Failure($"IPP request to '{printer.Name}' timed out: {ex.Message}");
        }
    }

    public async Task<JobStatus> GetJobStatusAsync(string backendJobId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendJobId);

        if (!BackendJobId.TryParse(backendJobId, out var jobId, out var printerName))
        {
            throw new ArgumentException($"'{backendJobId}' is not an IPP job id.", nameof(backendJobId));
        }

        var printer = FindPrinter(printerName)
            ?? throw new InvalidOperationException($"Printer '{printerName}' is not configured.");

        var header = IppRequestBuilder.Build(
            IppOperations.GetJobAttributes,
            NextRequestId(),
            [
                IppAttribute.Charset("attributes-charset", "utf-8"),
                IppAttribute.NaturalLanguage("attributes-natural-language", Language),
                IppAttribute.Uri("printer-uri", IppUri.ToIpp(printer.Uri)),
                IppAttribute.Integer("job-id", jobId),
                IppAttribute.NameWithoutLanguage("requesting-user-name", _options.UserName),
                IppAttribute.Keywords("requested-attributes", "job-state"),
            ]);

        var response = await SendOrThrowAsync(printer, header, ct).ConfigureAwait(false);
        if (response.StatusCode == IppStatus.ClientErrorNotFound)
        {
            return JobStatus.Completed;
        }

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"IPP Get-Job-Attributes failed: {IppStatus.Describe(response.StatusCode)}");
        }

        var state = response.FindGroup(IppTags.JobAttributes)?.GetInt32("job-state")
            ?? throw new InvalidOperationException("The printer returned no job-state.");

        return state switch
        {
            JobStatePending or JobStatePendingHeld => JobStatus.Queued,
            JobStateProcessing => JobStatus.Printing,
            JobStateCompleted => JobStatus.Completed,
            _ => JobStatus.Error,
        };
    }

    public void Dispose()
    {
        if (disposeHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private IppPrinterOptions? FindPrinter(string name) =>
        _options.Printers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private int NextRequestId() => Interlocked.Increment(ref _requestId);

    private async Task<PrinterAvailability> ReadAvailabilityAsync(IppPrinterOptions printer, CancellationToken ct)
    {
        try
        {
            var header = IppRequestBuilder.Build(
                IppOperations.GetPrinterAttributes,
                NextRequestId(),
                [
                    IppAttribute.Charset("attributes-charset", "utf-8"),
                    IppAttribute.NaturalLanguage("attributes-natural-language", Language),
                    IppAttribute.Uri("printer-uri", IppUri.ToIpp(printer.Uri)),
                    IppAttribute.NameWithoutLanguage("requesting-user-name", _options.UserName),
                    IppAttribute.Keywords("requested-attributes", "printer-state", "printer-is-accepting-jobs"),
                ]);

            var response = await SendAsync(IppUri.ToHttp(printer.Uri), header, document: null, ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return PrinterAvailability.Offline;
            }

            var group = response.FindGroup(IppTags.PrinterAttributes);
            if (group?.GetBoolean("printer-is-accepting-jobs") == false)
            {
                return PrinterAvailability.Offline;
            }

            return group?.GetInt32("printer-state") switch
            {
                PrinterStateIdle => PrinterAvailability.Ready,
                PrinterStateProcessing => PrinterAvailability.Busy,
                _ => PrinterAvailability.Offline,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException)
        {
            return PrinterAvailability.Offline;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return PrinterAvailability.Offline;
        }
    }

    private async Task<IppResponse> SendOrThrowAsync(IppPrinterOptions printer, byte[] header, CancellationToken ct)
    {
        try
        {
            return await SendAsync(IppUri.ToHttp(printer.Uri), header, document: null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
        {
            throw new InvalidOperationException($"IPP request to '{printer.Name}' failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"IPP request to '{printer.Name}' timed out.", ex);
        }
    }

    private async Task<IppResponse> SendAsync(Uri httpUri, byte[] header, Stream? document, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, httpUri)
        {
            Content = new IppRequestContent(header, document),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.RequestTimeout);

        using var httpResponse = await httpClient.SendAsync(message, timeout.Token).ConfigureAwait(false);
        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)httpResponse.StatusCode} from the printer."));
        }

        var body = await httpResponse.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        return IppResponse.Parse(body);
    }
}
