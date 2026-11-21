using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace PrintServer.WebUi.Components;

public abstract class RefreshingComponent : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;

    protected RefreshingComponent() => _token = _lifetime.Token;

    [Inject]
    protected IOptions<WebUiOptions> Settings { get; set; } = default!;

    // Copied once: a CancellationToken stays valid after its source is disposed, the source itself does not.
    protected CancellationToken Token => _token;

    protected string? LoadError { get; private set; }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }

    protected virtual Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

    protected override async Task OnInitializedAsync() => await ReloadAsync().ConfigureAwait(true);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RefreshLoopAsync().ConfigureAwait(true);
        }
    }

    protected async Task ReloadAsync()
    {
        try
        {
            await LoadAsync(Token).ConfigureAwait(true);
            LoadError = null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or SqliteException or DbUpdateException)
        {
            LoadError = $"Impossibile leggere la coda: {ex.Message}";
        }
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(Settings.Value.RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(Token).ConfigureAwait(true))
            {
                await ReloadAsync().ConfigureAwait(true);
                StateHasChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // The page was left: the refresh simply stops.
        }
    }
}
