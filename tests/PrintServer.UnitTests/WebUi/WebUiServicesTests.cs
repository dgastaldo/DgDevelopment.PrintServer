using Microsoft.Extensions.DependencyInjection;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Core.Watching;
using PrintServer.WebUi;

namespace PrintServer.UnitTests.WebUi;

public sealed class WebUiServicesTests
{
    private static async Task<(WebUiEnvironment Env, Job Job)> EnvWithJobAsync(bool queue, params string[] permissions)
    {
        var env = new WebUiEnvironment(queue, permissions);
        var result = await env.Queue.IntakeAsync(env.Queue.Drop("Office", "a.pdf", "x"));
        return (env, await env.Queue.GetAsync(result.JobId!.Value));
    }

    [Fact]
    public async Task ManagersCanCancelAndRetry()
    {
        var (env, job) = await EnvWithJobAsync(true, WebUiEnvironment.Manage);
        using (env)
        {
            var actions = env.Provider.GetRequiredService<WebUiActions>();

            Assert.True((await actions.CancelAsync(job.Id, CancellationToken.None)).Succeeded);
            Assert.Equal(JobState.Cancelled, (await env.Queue.GetAsync(job.Id)).State);
            Assert.True((await actions.RetryAsync(job.Id, CancellationToken.None)).Succeeded);
            Assert.Equal(JobState.Pending, (await env.Queue.GetAsync(job.Id)).State);
        }
    }

    [Theory]
    [InlineData(WebUiEnvironment.View)]
    [InlineData(WebUiEnvironment.Add)]
    public async Task OthersCannotCancelOrRetryEvenIfTheyAskDirectly(string permission)
    {
        var (env, job) = await EnvWithJobAsync(true, permission);
        using (env)
        {
            var actions = env.Provider.GetRequiredService<WebUiActions>();

            var cancel = await actions.CancelAsync(job.Id, CancellationToken.None);
            var retry = await actions.RetryAsync(job.Id, CancellationToken.None);

            Assert.False(cancel.Succeeded);
            Assert.False(retry.Succeeded);
            Assert.Contains("permesso", cancel.Message, StringComparison.Ordinal);
            Assert.Equal(JobState.Pending, (await env.Queue.GetAsync(job.Id)).State);
        }
    }

    [Fact]
    public async Task OnlyUsersWhoMayAddCanSubmitFiles()
    {
        using var allowed = new WebUiEnvironment(true, WebUiEnvironment.Add);
        using var denied = new WebUiEnvironment(true, WebUiEnvironment.Manage, WebUiEnvironment.View);
        using var content = new MemoryStream([1, 2, 3]);
        using var more = new MemoryStream([1, 2, 3]);

        var ok = await allowed.Provider.GetRequiredService<WebUiActions>().AddAsync("a.pdf", content, "Office", PrintOptions.Default, CancellationToken.None);
        var refused = await denied.Provider.GetRequiredService<WebUiActions>().AddAsync("a.pdf", more, "Office", PrintOptions.Default, CancellationToken.None);

        Assert.True(ok.Succeeded);
        Assert.True(File.Exists(Path.Combine(allowed.Inbox, "Office", "a.pdf")));
        Assert.False(refused.Succeeded);
        Assert.False(File.Exists(Path.Combine(denied.Inbox, "Office", "a.pdf")));
    }

    [Fact]
    public async Task WithoutAQueueEveryActionExplainsWhy()
    {
        using var env = new WebUiEnvironment(false, WebUiEnvironment.Manage, WebUiEnvironment.Add);
        var actions = env.Provider.GetRequiredService<WebUiActions>();
        using var content = new MemoryStream();

        var cancel = await actions.CancelAsync(Guid.NewGuid(), CancellationToken.None);
        var retry = await actions.RetryAsync(Guid.NewGuid(), CancellationToken.None);
        var add = await actions.AddAsync("a.pdf", content, null, PrintOptions.Default, CancellationToken.None);

        Assert.Contains("Watcher:Path", cancel.Message, StringComparison.Ordinal);
        Assert.Contains("Watcher:Path", retry.Message, StringComparison.Ordinal);
        Assert.Contains("Watcher:Path", add.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheViewReadsTheQueue()
    {
        var (env, job) = await EnvWithJobAsync(true, WebUiEnvironment.View);
        using (env)
        {
            var view = env.Provider.GetRequiredService<QueueView>();

            Assert.True(view.IsQueueActive);
            Assert.True(view.CanSubmit);
            Assert.Contains(".pdf", view.AcceptedExtensions);
            Assert.Equal(1, (await view.CountsAsync(CancellationToken.None))[JobState.Pending]);
            Assert.Equal(job.Id, Assert.Single((await view.JobsAsync(new JobQuery(), 0, 10, CancellationToken.None)).Items).Id);
            Assert.Equal(job.Id, (await view.JobAsync(job.Id, CancellationToken.None))!.Id);
            Assert.Equal("Office", Assert.Single((await view.PrintersAsync(CancellationToken.None)).Printers).Name);
        }
    }

    [Fact]
    public async Task TheViewIsEmptyWithoutAQueueOrABackend()
    {
        using var env = new WebUiEnvironment(false, WebUiEnvironment.View);
        var view = env.Provider.GetRequiredService<QueueView>();

        Assert.False(view.IsQueueActive);
        Assert.False(view.CanSubmit);
        Assert.Empty(view.AcceptedExtensions);
        Assert.Empty(await view.CountsAsync(CancellationToken.None));
        Assert.Equal(0, (await view.JobsAsync(new JobQuery(), 0, 10, CancellationToken.None)).Total);
        Assert.Null(await view.JobAsync(Guid.NewGuid(), CancellationToken.None));

        var services = new ServiceCollection().BuildServiceProvider();
        var bare = new QueueView(services);
        Assert.Contains("Nessun backend", (await bare.PrintersAsync(CancellationToken.None)).Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    public async Task APrinterBackendFailureBecomesAMessage(Type exceptionType)
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);
        env.Queue.Backend.ThrowOnList = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var listing = await env.Provider.GetRequiredService<QueueView>().PrintersAsync(CancellationToken.None);

        Assert.Empty(listing.Printers);
        Assert.Contains("boom", listing.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JobState.Pending, "In attesa", "info", false, true)]
    [InlineData(JobState.Converting, "In conversione", "info", false, true)]
    [InlineData(JobState.Printing, "In stampa", "info", false, true)]
    [InlineData(JobState.Completed, "Completato", "ok", false, false)]
    [InlineData(JobState.Failed, "Fallito", "bad", true, false)]
    [InlineData(JobState.Stalled, "In stallo", "bad", true, false)]
    [InlineData(JobState.Cancelled, "Annullato", "muted", true, false)]
    public void StatesAreDescribedForThePage(JobState state, string label, string css, bool retry, bool cancel)
    {
        Assert.Equal(label, Display.StateLabel(state));
        Assert.Equal(css, Display.StateClass(state));
        Assert.Equal(retry, Display.CanRetry(state));
        Assert.Equal(cancel, Display.CanCancel(state));
        Assert.Contains(state, Display.AllStates);
        Assert.Equal("99", Display.StateLabel((JobState)99));
    }

    [Fact]
    public void ValuesAreFormattedForReading()
    {
        Assert.Equal("Predefinita", Display.Printer(null));
        Assert.Equal("Office", Display.Printer("Office"));
        Assert.Equal("Predefinite", Display.Options(PrintOptions.Default));
        Assert.Equal("Orizzontale, Fronte e retro (lato lungo)", Display.Options(new PrintOptions(PageOrientation.Landscape, DuplexMode.TwoSidedLongEdge)));
        Assert.Equal("Verticale, Solo fronte", Display.Options(new PrintOptions(PageOrientation.Portrait, DuplexMode.OneSided)));
        Assert.Equal("Fronte e retro (lato corto)", Display.Options(new PrintOptions(PageOrientation.Default, DuplexMode.TwoSidedShortEdge)));
        Assert.Equal("-", Display.When(null));
        Assert.Equal("21/11/2026", Display.When(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero))[..10]);
        Assert.Equal("10 B", Display.Size(10));
        Assert.Equal("1.5 KB", Display.Size(1536));
        Assert.Equal("2 MB", Display.Size(2 * 1024 * 1024));
    }

    [Fact]
    public void PrinterProblemsAreListedInWords()
    {
        var all = new PrinterInfo("P", false, PrinterAvailability.Offline, PrinterIssues.PaperOut | PrinterIssues.TonerOut | PrinterIssues.PaperJam | PrinterIssues.DoorOpen | PrinterIssues.Error);

        Assert.Equal("Non disponibile, Carta esaurita, Toner esaurito, Carta inceppata, Sportello aperto, Errore", Display.Issues(all));
        Assert.Equal(string.Empty, Display.Issues(new PrinterInfo("P", false, PrinterAvailability.Ready)));
        Assert.Throws<ArgumentNullException>(() => Display.Issues(null!));
        Assert.Throws<ArgumentNullException>(() => Display.Options(null!));
    }

    [Fact]
    public void TheSubmitterAndStoreInterfacesAreWired()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);

        Assert.NotNull(env.Provider.GetService<IJobSubmitter>());
        Assert.NotNull(env.Provider.GetService<IJobStore>());
    }
}
