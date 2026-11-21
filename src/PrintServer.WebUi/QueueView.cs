using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Core.Watching;

namespace PrintServer.WebUi;

public sealed class QueueView(IServiceProvider services)
{
    private readonly IJobStore? _store = services.GetService<IJobStore>();
    private readonly IPrinterBackend? _backend = services.GetService<IPrinterBackend>();
    private readonly IJobSubmitter? _submitter = services.GetService<IJobSubmitter>();

    public bool IsQueueActive => _store is not null;

    public bool CanSubmit => _submitter is not null;

    public IReadOnlyList<string> AcceptedExtensions => _submitter?.AcceptedExtensions ?? [];

    public async Task<IReadOnlyDictionary<JobState, int>> CountsAsync(CancellationToken ct) =>
        _store is null ? new Dictionary<JobState, int>() : await _store.CountByStateAsync(ct).ConfigureAwait(false);

    public async Task<JobPage> JobsAsync(JobQuery query, int skip, int take, CancellationToken ct) =>
        _store is null ? new JobPage([], 0) : await _store.QueryAsync(query, skip, take, ct).ConfigureAwait(false);

    public async Task<Job?> JobAsync(Guid id, CancellationToken ct) =>
        _store is null ? null : await _store.GetAsync(id, ct).ConfigureAwait(false);

    public async Task<PrinterListing> PrintersAsync(CancellationToken ct)
    {
        if (_backend is null)
        {
            return new PrinterListing([], "Nessun backend di stampa configurato.");
        }

        try
        {
            return new PrinterListing(await _backend.ListPrintersAsync(ct).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException or TimeoutException)
        {
            return new PrinterListing([], $"Impossibile leggere le stampanti: {ex.Message}");
        }
    }
}

public sealed record PrinterListing(IReadOnlyList<PrinterInfo> Printers, string? Error);
