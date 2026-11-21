using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Core.Watching;
using PrintServer.Queue;
using PrintServer.Watching;
using PrintServer.WebUi;
using PrintServer.WebUi.Authentication;

namespace PrintServer.UnitTests.WebUi;

internal sealed class FakeAuthenticationStateProvider : AuthenticationStateProvider
{
    public FakeAuthenticationStateProvider(params string[] permissions)
    {
        var claims = permissions.Select(permission => new Claim(WebUiPermissions.ClaimType, permission)).ToList();
        claims.Add(new Claim(ClaimTypes.Name, "Mario Rossi"));
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    public ClaimsPrincipal User { get; set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
}

internal sealed class NoJavaScript : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
}

internal sealed class FakeNavigationManager : NavigationManager
{
    public FakeNavigationManager() => Initialize("http://localhost/", "http://localhost/");

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
    }
}

internal sealed class WebUiEnvironment : IDisposable
{
    public const string View = "printserver.view";
    public const string Manage = "printserver.manage";
    public const string Add = "printserver.add";

    private readonly string _inbox = Path.Combine(Path.GetTempPath(), $"printserver-webui-{Guid.NewGuid():N}");

    public WebUiEnvironment(bool queueActive = true, params string[] permissions)
    {
        Directory.CreateDirectory(_inbox);
        Queue = new QueueHarness();
        Auth = new FakeAuthenticationStateProvider(permissions);
        Options = new WebUiOptions { RefreshInterval = TimeSpan.FromMilliseconds(20) };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(authorization => WebUiAuthorization.AddPolicies(authorization, Options));
        services.AddSingleton<AuthenticationStateProvider>(Auth);
        services.AddSingleton<NavigationManager>(new FakeNavigationManager());
        services.AddSingleton<IJSRuntime>(new NoJavaScript());
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddSingleton<IPrinterBackend>(Queue.Backend);
        if (queueActive)
        {
            services.AddSingleton(Queue.Queue.Store);
            services.AddSingleton<IJobStore>(Queue.Queue.Store);
            services.AddSingleton(new JobAdministrator(Queue.Queue.Store, Queue.Files, Queue.Backend, NullLogger<JobAdministrator>.Instance));
            services.AddSingleton<IJobSubmitter>(new FolderJobSubmitter(Microsoft.Extensions.Options.Options.Create(new WatcherOptions { Path = _inbox })));
        }

        services.AddScoped<QueueView>();
        services.AddScoped<WebUiActions>();
        Provider = services.BuildServiceProvider();
    }

    public QueueHarness Queue { get; }

    public FakeAuthenticationStateProvider Auth { get; }

    public WebUiOptions Options { get; }

    public ServiceProvider Provider { get; }

    public string Inbox => _inbox;

    public async Task<string> RenderAsync<TComponent>(Dictionary<string, object?>? parameters = null)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(Provider, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = parameters is null ? ParameterView.Empty : ParameterView.FromDictionary(parameters);
            var root = await renderer.RenderComponentAsync<CascadingAuthenticationState>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ChildContent"] = (RenderFragment)(builder =>
                {
                    builder.OpenComponent<TComponent>(0);
                    foreach (var parameter in page)
                    {
                        builder.AddAttribute(1, parameter.Name, parameter.Value);
                    }

                    builder.CloseComponent();
                }),
            }));
            return root.ToHtmlString();
        });
        return html;
    }

    public void Dispose()
    {
        Provider.Dispose();
        Queue.Dispose();
        if (Directory.Exists(_inbox))
        {
            Directory.Delete(_inbox, recursive: true);
        }
    }
}
