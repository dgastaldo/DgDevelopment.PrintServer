using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.WebUi.Components;
using PrintServer.WebUi.Components.Layout;
using PrintServer.WebUi.Components.Pages;

namespace PrintServer.UnitTests.WebUi;

public sealed class WebUiComponentTests
{
    private static async Task<(WebUiEnvironment Env, Job Failed, Job Pending)> SeedAsync(params string[] permissions)
    {
        var env = new WebUiEnvironment(true, permissions);
        var failed = await env.Queue.IntakeAsync(env.Queue.Drop("Office", "broken.pdf", "x"));
        var pending = await env.Queue.IntakeAsync(env.Queue.Drop("Office", "waiting.pdf", "y"));
        env.Queue.Queue.SetState(failed.JobId!.Value, JobState.Failed);
        return (env, await env.Queue.GetAsync(failed.JobId.Value), await env.Queue.GetAsync(pending.JobId!.Value));
    }

    [Fact]
    public async Task TheDashboardShowsCountsPrintersAndRecentJobs()
    {
        var (env, _, _) = await SeedAsync(WebUiEnvironment.View);
        using (env)
        {
            env.Queue.Backend.Printers.Add(new PrinterInfo("Labels", IsDefault: false, PrinterAvailability.Offline, PrinterIssues.PaperOut | PrinterIssues.TonerOut));

            var html = await env.RenderAsync<Dashboard>();

            Assert.Contains("<h1>Status</h1>", html, StringComparison.Ordinal);
            Assert.Contains("Pending", html, StringComparison.Ordinal);
            Assert.Contains("Office (default)", html, StringComparison.Ordinal);
            Assert.Contains("Labels", html, StringComparison.Ordinal);
            Assert.Contains("Unavailable", html, StringComparison.Ordinal);
            Assert.Contains("Out of paper, Out of toner", html, StringComparison.Ordinal);
            Assert.Contains("broken.pdf", html, StringComparison.Ordinal);
            Assert.Contains("waiting.pdf", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/jobs?state=Failed\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Retry", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheDashboardReportsAnInactiveQueueAndPrinterErrors()
    {
        using var env = new WebUiEnvironment(false, WebUiEnvironment.View);
        env.Queue.Backend.ThrowOnList = new InvalidOperationException("cups is down");

        var html = await env.RenderAsync<Dashboard>();

        Assert.Contains("The queue is not active", html, StringComparison.Ordinal);
        Assert.Contains("cups is down", html, StringComparison.Ordinal);
        Assert.Contains("No jobs", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDashboardSaysWhenThereAreNoPrinters()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);
        env.Queue.Backend.Printers.Clear();

        var html = await env.RenderAsync<Dashboard>();

        Assert.Contains("No printers", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADatabaseFailureIsShownInsteadOfCrashing()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(env.Queue.Queue.DatabasePath, "this is not a database");

        var html = await env.RenderAsync<Dashboard>();

        Assert.Contains("Cannot read the queue", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManagersSeeTheActionsThatFitEachState()
    {
        var (env, failed, pending) = await SeedAsync(WebUiEnvironment.View, WebUiEnvironment.Manage);
        using (env)
        {
            var html = await env.RenderAsync<Jobs>();

            Assert.Contains("Retry", html, StringComparison.Ordinal);
            Assert.Contains("Cancel", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"/jobs/{failed.Id}\"", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"/jobs/{pending.Id}\"", html, StringComparison.Ordinal);
            Assert.Contains("1-2 of 2", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ViewersSeeNoActionButtons()
    {
        var (env, _, _) = await SeedAsync(WebUiEnvironment.View);
        using (env)
        {
            var html = await env.RenderAsync<Jobs>();

            Assert.DoesNotContain("Retry", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Cancel<", html, StringComparison.Ordinal);
            Assert.Contains("broken.pdf", html, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("failed", "Failed")]
    [InlineData("FAILED", "Failed")]
    [InlineData("Pending", "Pending")]
    [InlineData("nonsense", "")]
    [InlineData("99", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TheStateInTheAddressIsReadSafely(string? value, string expected)
    {
        Assert.Equal(expected, Jobs.ParseState(value));
    }

    [Fact]
    public async Task AnEmptyListSaysSo()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);

        var html = await env.RenderAsync<Jobs>();

        Assert.Contains("No jobs", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDetailPageShowsEverythingAboutAJob()
    {
        var (env, failed, _) = await SeedAsync(WebUiEnvironment.View, WebUiEnvironment.Manage);
        using (env)
        {
            env.Queue.Queue.SetState(failed.Id, JobState.Stalled);

            var html = await env.RenderAsync<JobDetail>(new() { ["Id"] = failed.Id });

            Assert.Contains("broken.pdf", html, StringComparison.Ordinal);
            Assert.Contains("Stalled", html, StringComparison.Ordinal);
            Assert.Contains("application/pdf", html, StringComparison.Ordinal);
            Assert.Contains(failed.Id.ToString(), html, StringComparison.Ordinal);
            Assert.Contains("Defaults", html, StringComparison.Ordinal);
            Assert.Contains("Retry", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Cancel<", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheDetailPageOffersCancelForActiveJobsAndShowsTheError()
    {
        var (env, _, pending) = await SeedAsync(WebUiEnvironment.View, WebUiEnvironment.Manage);
        using (env)
        {
            var html = await env.RenderAsync<JobDetail>(new() { ["Id"] = pending.Id });

            Assert.Contains(">Cancel<", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Retry", html, StringComparison.Ordinal);
            Assert.DoesNotContain("banner bad", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheDetailPageForAnUnknownJobSaysSo()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);

        var html = await env.RenderAsync<JobDetail>(new() { ["Id"] = Guid.NewGuid() });

        Assert.Contains("Job not found", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAddPageListsPrintersOptionsAndAcceptedTypes()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.Add);

        var html = await env.RenderAsync<Add>();

        Assert.Contains("Add files", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"Office\">", html, StringComparison.Ordinal);
        Assert.Contains("Two-sided (short edge)", html, StringComparison.Ordinal);
        Assert.Contains("type=\"file\"", html, StringComparison.Ordinal);
        Assert.Contains(".docx", html, StringComparison.Ordinal);
        Assert.Contains("25 MB", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAddPageNeedsAnActiveQueue()
    {
        using var env = new WebUiEnvironment(false, WebUiEnvironment.Add);

        var html = await env.RenderAsync<Add>();

        Assert.Contains("The queue is not active", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"file\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLayoutShowsTheNavigationAccordingToPermissions()
    {
        using var viewer = new WebUiEnvironment(true, WebUiEnvironment.View);
        using var adder = new WebUiEnvironment(true, WebUiEnvironment.Add);

        var plain = await viewer.RenderAsync<MainLayout>(new() { ["Body"] = (Microsoft.AspNetCore.Components.RenderFragment)(_ => { }) });
        var full = await adder.RenderAsync<MainLayout>(new() { ["Body"] = (Microsoft.AspNetCore.Components.RenderFragment)(_ => { }) });

        Assert.Contains("href=\"/jobs\"", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/add\"", plain, StringComparison.Ordinal);
        Assert.Contains("href=\"/add\"", full, StringComparison.Ordinal);
        Assert.Contains("Mario Rossi", full, StringComparison.Ordinal);
        Assert.Contains("read-only", full, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePagesAreAvailableInItalian()
    {
        using var scope = new CultureScope("it");
        using var env = new WebUiEnvironment(true, WebUiEnvironment.Add);
        env.Queue.Backend.Printers.Add(new PrinterInfo("Office", IsDefault: true, PrinterAvailability.Ready));

        var dashboard = await env.RenderAsync<Dashboard>();
        var layout = await env.RenderAsync<MainLayout>(new() { ["Body"] = (Microsoft.AspNetCore.Components.RenderFragment)(_ => { }) });
        var add = await env.RenderAsync<Add>();

        Assert.Contains("<h1>Stato</h1>", dashboard, StringComparison.Ordinal);
        Assert.Contains("Office (predefinita)", dashboard, StringComparison.Ordinal);
        Assert.Contains("Pronta", dashboard, StringComparison.Ordinal);
        Assert.Contains("Lavori", layout, StringComparison.Ordinal);
        Assert.Contains("solo lettura", layout, StringComparison.Ordinal);
        Assert.Contains("Aggiungi file", add, StringComparison.Ordinal);
        Assert.Contains("Fronte e retro (lato corto)", add, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLayoutOffersBothLanguagesAndComesBackToTheSamePage()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);

        var html = await env.RenderAsync<MainLayout>(new() { ["Body"] = (Microsoft.AspNetCore.Components.RenderFragment)(_ => { }) });

        Assert.Contains("href=\"/language?culture=en&amp;returnUrl=%2F", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/language?culture=it&amp;returnUrl=%2F", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAProviderTheLayoutOffersLogout()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);
        env.Options.Authority = "https://identity.example";
        env.Options.ClientId = "client";

        var html = await env.RenderAsync<MainLayout>(new() { ["Body"] = (Microsoft.AspNetCore.Components.RenderFragment)(_ => { }) });

        Assert.Contains("href=\"/logout\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("read-only", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRefreshingComponentReloadsOnItsOwnUntilItIsDisposed()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);
        using var renderer = new TestRenderer(env.Provider);

        var component = await renderer.RenderAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (component.Loads < 3)
        {
            await Task.Delay(20, cts.Token);
        }

        component.Dispose();
        await Task.Delay(100, CancellationToken.None);
        var loads = component.Loads;
        await Task.Delay(150, CancellationToken.None);
        Assert.Equal(loads, component.Loads);
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task ALoadFailureIsKeptAsAnErrorAndClearedOnSuccess()
    {
        using var env = new WebUiEnvironment(true, WebUiEnvironment.View);
        using var renderer = new TestRenderer(env.Provider);
        env.Options.RefreshInterval = TimeSpan.FromHours(1);
        CountingRefreshingComponent.FailNext = true;

        var component = await renderer.RenderAsync();
        Assert.StartsWith("Cannot read the queue", component.Error, StringComparison.Ordinal);

        CountingRefreshingComponent.FailNext = false;
        await component.ReloadNow();
        Assert.Null(component.Error);
        component.Dispose();
    }

#pragma warning disable BL0006 // A hand-made renderer is the only way to run the after-render hook of a component outside a browser.
    private sealed class TestRenderer(IServiceProvider services) : Microsoft.AspNetCore.Components.RenderTree.Renderer(services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
    {
        public override Microsoft.AspNetCore.Components.Dispatcher Dispatcher { get; } = Microsoft.AspNetCore.Components.Dispatcher.CreateDefault();

        public List<Exception> Errors { get; } = [];

        public async Task<CountingRefreshingComponent> RenderAsync()
        {
            var component = (CountingRefreshingComponent)InstantiateComponent(typeof(CountingRefreshingComponent));
            var id = AssignRootComponentId(component);
            await Dispatcher.InvokeAsync(() => RenderRootComponentAsync(id));
            return component;
        }

        protected override void HandleException(Exception exception) => Errors.Add(exception);

        protected override Task UpdateDisplayAsync(in Microsoft.AspNetCore.Components.RenderTree.RenderBatch renderBatch) => Task.CompletedTask;
    }
#pragma warning restore BL0006
}

public sealed class CountingRefreshingComponent : RefreshingComponent
{
    public static bool FailNext { get; set; }

    public int Loads { get; private set; }

    public string? Error => LoadError;

    public Task ReloadNow() => ReloadAsync();

    protected override Task LoadAsync(CancellationToken ct)
    {
        Loads++;
        return FailNext ? throw new InvalidOperationException("database is locked") : Task.CompletedTask;
    }
}
