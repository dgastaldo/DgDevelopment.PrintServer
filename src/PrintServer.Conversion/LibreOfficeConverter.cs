using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrintServer.Backends.Common;
using PrintServer.Backends.Selection;
using PrintServer.Core.Printing;

namespace PrintServer.Conversion;

public sealed partial class LibreOfficeConverter : IDocumentConverter
{
    private const int MaxErrorLength = 300;

    private static readonly HashSet<string> Formats =
    [
        DocumentFormats.Doc,
        DocumentFormats.Docx,
        DocumentFormats.Odt,
        DocumentFormats.Rtf,
        DocumentFormats.PlainText,
        DocumentFormats.Xls,
        DocumentFormats.Xlsx,
        DocumentFormats.Ods,
        DocumentFormats.Ppt,
        DocumentFormats.Pptx,
        DocumentFormats.Odp,
    ];

    private readonly IProcessRunner _runner;
    private readonly ConversionOptions _options;
    private readonly Func<string, bool> _exists;
    private readonly HostPlatform _platform;
    private readonly ILogger<LibreOfficeConverter> _logger;

    public LibreOfficeConverter(
        IProcessRunner runner,
        IOptions<ConversionOptions> options,
        ILogger<LibreOfficeConverter> logger)
        : this(runner, options, logger, File.Exists, HostPlatformDetector.Current())
    {
    }

    internal LibreOfficeConverter(
        IProcessRunner runner,
        IOptions<ConversionOptions> options,
        ILogger<LibreOfficeConverter> logger,
        Func<string, bool> exists,
        HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exists);

        _runner = runner;
        _options = options.Value;
        _logger = logger;
        _exists = exists;
        _platform = platform;
    }

    public bool CanConvert(string sourceFormat) => Formats.Contains(sourceFormat);

    public async Task<ConversionResult> ConvertToPdfAsync(string sourcePath, string sourceFormat, string outputPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (!CanConvert(sourceFormat))
        {
            return ConversionResult.Failure($"LibreOffice cannot convert '{sourceFormat}'.");
        }

        var executable = LibreOfficeLocator.Find(_options.LibreOfficePath, _platform, _exists);
        if (executable is null)
        {
            return ConversionResult.Failure("LibreOffice was not found: install it or set Conversion:LibreOfficePath.");
        }

        var fullSource = Path.GetFullPath(sourcePath);
        var fullOutput = Path.GetFullPath(outputPath);
        var workDirectory = Path.Combine(Path.GetDirectoryName(fullOutput)!, $".convert-{Guid.NewGuid():N}");
        var profileDirectory = Path.Combine(workDirectory, "profile");
        var outputDirectory = Path.Combine(workDirectory, "out");
        Directory.CreateDirectory(profileDirectory);
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var result = await RunAsync(executable, fullSource, profileDirectory, outputDirectory, ct).ConfigureAwait(false);
            if (result.Failure is not null)
            {
                return result.Failure;
            }

            var produced = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(fullSource) + ".pdf");
            if (!File.Exists(produced))
            {
                return ConversionResult.Failure($"LibreOffice produced no PDF. {Trim(result.Process!.StandardError)}".Trim());
            }

            File.Move(produced, fullOutput, overwrite: true);
            return ConversionResult.Success();
        }
        finally
        {
            DeleteWorkDirectory(workDirectory);
        }
    }

    private async Task<(ProcessResult? Process, ConversionResult? Failure)> RunAsync(
        string executable,
        string source,
        string profileDirectory,
        string outputDirectory,
        CancellationToken ct)
    {
        string[] arguments =
        [
            "--headless",
            "--norestore",
            "--nolockcheck",
            "--nodefault",
            "--nologo",
            $"-env:UserInstallation={new Uri(profileDirectory + Path.DirectorySeparatorChar).AbsoluteUri}",
            "--convert-to",
            "pdf",
            "--outdir",
            outputDirectory,
            source,
        ];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.Timeout);
        ProcessResult process;
        try
        {
            process = await _runner.RunAsync(executable, arguments, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, ConversionResult.Failure($"LibreOffice did not finish within {_options.Timeout}."));
        }
        catch (InvalidOperationException ex)
        {
            return (null, ConversionResult.Failure(ex.Message));
        }

        return process.ExitCode == 0
            ? (process, null)
            : (null, ConversionResult.Failure($"LibreOffice failed with exit code {process.ExitCode}. {Trim(process.StandardError)}".Trim()));
    }

    private static string Trim(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= MaxErrorLength ? trimmed : trimmed[..MaxErrorLength];
    }

    private void DeleteWorkDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCleanupFailed(ex, directory);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the temporary conversion folder '{Directory}'.")]
    private partial void LogCleanupFailed(Exception exception, string directory);
}
