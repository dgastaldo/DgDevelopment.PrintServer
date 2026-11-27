using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PrintServer.WebUi.Authentication;

namespace PrintServer.WebUi;

public static class WebUiServiceCollectionExtensions
{
    public const string LoginPath = "/login";

    public const string LogoutPath = "/logout";

    public const string LanguagePath = "/language";

    /// <summary>Languages the page is available in; the first one is the default.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["en", "it"];

    public static IServiceCollection AddWebUi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new WebUiOptions();
        configuration.GetSection(WebUiOptions.SectionName).Bind(options);

        services.AddOptions<WebUiOptions>()
            .Bind(configuration.GetSection(WebUiOptions.SectionName))
            .Validate(IsValid, "WebUi options are invalid: Authority must be an absolute URL, SessionLifetime and RefreshInterval must be positive and the three permissions must differ from nothing.")
            .ValidateOnStart();

        if (!options.Enabled)
        {
            return services;
        }

        services.AddLocalization(localization => localization.ResourcesPath = Strings.ResourcesPath);
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddCascadingAuthenticationState();
        services.AddAuthorization(authorization => WebUiAuthorization.AddPolicies(authorization, options));
        services.AddScoped<UiText>();
        services.AddScoped<QueueView>();
        services.AddScoped<WebUiActions>();

        var authentication = services.AddAuthentication(scheme =>
        {
            scheme.DefaultScheme = options.IsOidcConfigured ? CookieAuthenticationDefaults.AuthenticationScheme : LoopbackAuthenticationHandler.SchemeName;
            scheme.DefaultChallengeScheme = options.IsOidcConfigured ? OpenIdConnectDefaults.AuthenticationScheme : LoopbackAuthenticationHandler.SchemeName;
        });

        if (options.IsOidcConfigured)
        {
            authentication
                .AddCookie(cookie =>
                {
                    cookie.ExpireTimeSpan = options.SessionLifetime;
                    cookie.SlidingExpiration = false;
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                    cookie.Cookie.Name = "printserver.session";
                    cookie.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                })
                .AddOpenIdConnect(oidc => ConfigureOpenIdConnect(oidc, options));
        }
        else
        {
            authentication.AddScheme<AuthenticationSchemeOptions, LoopbackAuthenticationHandler>(LoopbackAuthenticationHandler.SchemeName, _ => { });
        }

        return services;
    }

    public static IEndpointRouteBuilder MapWebUi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<IOptions<WebUiOptions>>().Value;
        if (!options.Enabled)
        {
            return app;
        }

        app.UseRequestLocalization(localization => localization
            .SetDefaultCulture(Languages[0])
            .AddSupportedCultures([.. Languages])
            .AddSupportedUICultures([.. Languages]));
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        if (options.IsOidcConfigured)
        {
            app.MapGet(LoginPath, (string? returnUrl) => Results.Challenge(
                new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
                [OpenIdConnectDefaults.AuthenticationScheme]));
            app.MapGet(LogoutPath, () => Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
        }

        app.MapGet(LanguagePath, (HttpContext context, string? culture, string? returnUrl) =>
        {
            var language = Languages.FirstOrDefault(candidate => string.Equals(candidate, culture, StringComparison.OrdinalIgnoreCase));
            if (language is not null)
            {
                // Secure only over HTTPS: the page also works over plain HTTP on a LAN, where a Secure cookie would be dropped
                // by the browser and the language choice would silently not stick. The cookie holds no secret.
                context.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(language)),
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, HttpOnly = true, Secure = context.Request.IsHttps });
            }

            return Results.LocalRedirect(SafeReturnUrl(returnUrl));
        }).AllowAnonymous();

        app.MapStaticAssets();
        app.MapRazorComponents<Components.App>()
            .AddInteractiveServerRenderMode()
            .RequireAuthorization(WebUiPermissions.AccessPolicy);
        return app;
    }

    internal static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
            ? returnUrl
            : "/";

    private static bool IsValid(WebUiOptions options) =>
        options.SessionLifetime > TimeSpan.Zero
        && options.RefreshInterval > TimeSpan.Zero
        && !string.IsNullOrWhiteSpace(options.PermissionClaim)
        && !string.IsNullOrWhiteSpace(options.ViewPermission)
        && !string.IsNullOrWhiteSpace(options.ManagePermission)
        && !string.IsNullOrWhiteSpace(options.AddPermission)
        && (string.IsNullOrWhiteSpace(options.Authority) || Uri.TryCreate(options.Authority, UriKind.Absolute, out _));

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions oidc, WebUiOptions options)
    {
        oidc.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        oidc.Authority = options.Authority;
        oidc.ClientId = options.ClientId;
        oidc.ClientSecret = options.ClientSecret;
        oidc.RequireHttpsMetadata = options.RequireHttpsMetadata;
        oidc.ResponseType = "code";
        oidc.ResponseMode = OpenIdConnectResponseMode.Query;
        oidc.CorrelationCookie.SameSite = SameSiteMode.Lax;
        oidc.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        oidc.NonceCookie.SameSite = SameSiteMode.Lax;
        oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        oidc.UsePkce = true;
        oidc.SaveTokens = false;
        oidc.GetClaimsFromUserInfoEndpoint = false;
        oidc.MapInboundClaims = false;
        oidc.Scope.Clear();
        oidc.Scope.Add("openid");
        oidc.Scope.Add("profile");
        oidc.Scope.Add("email");
        oidc.TokenValidationParameters.NameClaimType = "name";
        oidc.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = context =>
            {
                var permissions = AccessTokenPermissions.Read(context.TokenEndpointResponse?.AccessToken, options.PermissionClaim);
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    identity.AddClaims(permissions);
                }

                return Task.CompletedTask;
            },
        };
    }
}
