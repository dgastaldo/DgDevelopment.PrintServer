using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PrintServer.WebUi;
using PrintServer.WebUi.Authentication;

namespace PrintServer.UnitTests.WebUi;

public sealed class WebUiTests : IDisposable
{
    private readonly FakeIdentityProvider _idp = new();
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public WebUiTests() => _idp.StartAsync().GetAwaiter().GetResult();

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }

        _idp.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, pipeline) =>
            {
                context.Connection.RemoteIpAddress = address;
                return pipeline(context);
            });
            next(app);
        };
    }

    private HttpClient Create(IPAddress remote, bool oidc, params (string Key, string Value)[] settings)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (oidc)
            {
                builder.UseSetting("WebUi:Authority", FakeIdentityProvider.Issuer);
                builder.UseSetting("WebUi:ClientId", FakeIdentityProvider.ClientId);
                builder.UseSetting("WebUi:ClientSecret", FakeIdentityProvider.ClientSecret);
                builder.UseSetting("WebUi:RequireHttpsMetadata", "false");
            }

            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(remote));
                services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
                {
                    options.Backchannel = _idp.Client;
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        FakeIdentityProvider.Issuer + "/.well-known/openid-configuration",
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever(_idp.Client) { RequireHttps = false });
                });
            });
        });
        _factories.Add(factory);
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private async Task<HttpClient> SignedInAsync(params string[] permissions)
    {
        _idp.Permissions = permissions;
        var app = Create(IPAddress.Parse("10.1.2.3"), oidc: true);
        var challenge = await app.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorize = await _idp.Client.GetAsync(challenge.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var callback = await app.GetAsync(new Uri(authorize.Headers.Location!.PathAndQuery, UriKind.Relative));
        Assert.True(callback.StatusCode == HttpStatusCode.Redirect, await callback.Content.ReadAsStringAsync());
        return app;
    }

    [Fact]
    public async Task WithoutAProviderThePageIsReadOnlyFromThisMachine()
    {
        using var client = Create(IPAddress.Loopback, oidc: false);

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PrintServer", html, StringComparison.Ordinal);
        Assert.Contains("solo lettura", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/add\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAProviderOtherMachinesAreRefused()
    {
        using var client = Create(IPAddress.Parse("192.168.1.20"), oidc: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri("/jobs", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task ARequestWithoutAnAddressIsRefused()
    {
        using var client = Create(IPAddress.Loopback, oidc: false);
        using var anonymous = new WebApplicationFactory<Program>().WithWebHostBuilder(_ => { }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task ThePageCanBeSwitchedOff()
    {
        using var client = Create(IPAddress.Loopback, oidc: false, ("WebUi:Enabled", "false"));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task AnonymousVisitorsAreSentToTheProviderWithPkce()
    {
        using var client = Create(IPAddress.Parse("10.1.2.3"), oidc: true);

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.True(response.StatusCode == HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        var location = response.Headers.Location!;
        Assert.StartsWith(FakeIdentityProvider.Issuer + "/connect/authorize", location.AbsoluteUri, StringComparison.Ordinal);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(FakeIdentityProvider.ClientId, query["client_id"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Contains("openid", query["scope"].ToString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
    }

    [Fact]
    public async Task AUserWithPermissionsEntersAndSeesOnlyWhatTheyMayDo()
    {
        using var viewer = await SignedInAsync("printserver.view");

        var html = await (await viewer.GetAsync(new Uri("/", UriKind.Relative))).Content.ReadAsStringAsync();

        Assert.Contains("Mario Rossi", html, StringComparison.Ordinal);
        Assert.Contains("Esci", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/add\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(new Uri("/add", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task AUserWhoMayAddSeesTheAddPage()
    {
        using var adder = await SignedInAsync("printserver.add");

        var home = await (await adder.GetAsync(new Uri("/", UriKind.Relative))).Content.ReadAsStringAsync();
        var add = await adder.GetAsync(new Uri("/add", UriKind.Relative));

        Assert.Contains("href=\"/add\"", home, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
    }

    [Fact]
    public async Task AUserWithoutAnyPermissionIsRefused()
    {
        using var nobody = await SignedInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(new Uri("/jobs", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task ThePermissionNamesAreConfigurable()
    {
        _idp.Permissions = ["print:read"];
        using var app = Create(IPAddress.Parse("10.1.2.3"), oidc: true, ("WebUi:ViewPermission", "print:read"));
        var challenge = await app.GetAsync(new Uri("/", UriKind.Relative));
        var authorize = await _idp.Client.GetAsync(challenge.Headers.Location);
        await app.GetAsync(new Uri(authorize.Headers.Location!.PathAndQuery, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task LoggingOutEndsTheSession()
    {
        using var viewer = await SignedInAsync("printserver.view");

        var logout = await viewer.GetAsync(new Uri(WebUiServiceCollectionExtensions.LogoutPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        var again = await viewer.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
    }

    [Fact]
    public async Task TheLoginEndpointSendsTheVisitorToTheProvider()
    {
        using var client = Create(IPAddress.Parse("10.1.2.3"), oidc: true);

        var response = await client.GetAsync(new Uri(WebUiServiceCollectionExtensions.LoginPath + "?returnUrl=/jobs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(FakeIdentityProvider.Issuer, response.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/jobs", "/jobs")]
    [InlineData("/jobs?state=Failed", "/jobs?state=Failed")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    [InlineData("/", "/")]
    public void OnlyLocalReturnUrlsAreFollowed(string? input, string expected)
    {
        Assert.Equal(expected, WebUiServiceCollectionExtensions.SafeReturnUrl(input));
    }

    [Fact]
    public void PermissionsAreReadFromTheAccessToken()
    {
        var array = new JwtSecurityTokenHandler().CreateEncodedJwt(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("permissions", "a"), new Claim("permissions", "b"), new Claim("permissions", "a"), new Claim("other", "x")]),
        });

        var claims = AccessTokenPermissions.Read(array, "permissions");

        Assert.Equal(["a", "b"], claims.Select(claim => claim.Value));
        Assert.All(claims, claim => Assert.Equal(WebUiPermissions.ClaimType, claim.Type));
        Assert.Empty(AccessTokenPermissions.Read("not a token", "permissions"));
        Assert.Empty(AccessTokenPermissions.Read(null, "permissions"));
        Assert.Empty(AccessTokenPermissions.Read(" ", "permissions"));
        Assert.Empty(AccessTokenPermissions.Read(array, "missing"));
        Assert.Throws<ArgumentException>(() => AccessTokenPermissions.Read(array, " "));
    }

    [Theory]
    [InlineData("WebUi:SessionLifetime", "00:00:00")]
    [InlineData("WebUi:RefreshInterval", "00:00:00")]
    [InlineData("WebUi:Authority", "not a url")]
    [InlineData("WebUi:ViewPermission", " ")]
    [InlineData("WebUi:PermissionClaim", " ")]
    public void InvalidOptionsAreRejected(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        var services = new ServiceCollection();
        services.AddWebUi(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<WebUiOptions>>().Value);
    }

    [Fact]
    public void TheDefaultsAreSafe()
    {
        var options = new WebUiOptions();

        Assert.True(options.Enabled);
        Assert.False(options.IsOidcConfigured);
        Assert.True(options.RequireHttpsMetadata);
        Assert.Equal("printserver.view", options.ViewPermission);
        Assert.Equal("printserver.manage", options.ManagePermission);
        Assert.Equal("printserver.add", options.AddPermission);
        Assert.Equal("permissions", options.PermissionClaim);
        Assert.True(new WebUiOptions { Authority = "https://id", ClientId = "x" }.IsOidcConfigured);
        Assert.False(new WebUiOptions { Authority = "https://id" }.IsOidcConfigured);
    }

    [Fact]
    public void RegistrationChecksItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() => WebUiServiceCollectionExtensions.AddWebUi(null!, new ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddWebUi(null!));
        Assert.Throws<ArgumentNullException>(() => WebUiServiceCollectionExtensions.MapWebUi(null!));
    }
}
