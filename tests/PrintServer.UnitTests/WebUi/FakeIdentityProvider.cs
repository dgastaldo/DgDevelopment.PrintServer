using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace PrintServer.UnitTests.WebUi;

internal sealed class FakeIdentityProvider : IAsyncDisposable
{
    public const string Issuer = "http://idp.test";

    public const string ClientId = "printserver-client";

    public const string ClientSecret = "s3cret";

    private static readonly string[] CodeFlow = ["code"];
    private static readonly string[] Public = ["public"];
    private static readonly string[] Rs256 = ["RS256"];
    private static readonly string[] Pkce = ["S256"];
    private static readonly string[] SecretPost = ["client_secret_post"];

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly Dictionary<string, (string Nonce, string Challenge)> _codes = [];
    private WebApplication? _app;

    public FakeIdentityProvider() => _key = new RsaSecurityKey(_rsa) { KeyId = "test-key" };

    public string[] Permissions { get; set; } = [];

    public string UserName { get; set; } = "Mario Rossi";

    public HttpClient Client { get; private set; } = null!;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = builder.Build();

        _app.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = Issuer,
            ["authorization_endpoint"] = Issuer + "/connect/authorize",
            ["token_endpoint"] = Issuer + "/connect/token",
            ["jwks_uri"] = Issuer + "/connect/jwks",
            ["userinfo_endpoint"] = Issuer + "/connect/userinfo",
            ["end_session_endpoint"] = Issuer + "/connect/endsession",
            ["response_types_supported"] = CodeFlow,
            ["subject_types_supported"] = Public,
            ["id_token_signing_alg_values_supported"] = Rs256,
            ["code_challenge_methods_supported"] = Pkce,
            ["token_endpoint_auth_methods_supported"] = SecretPost,
        }));
        _app.MapGet("/connect/jwks", () =>
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = "test-key" });
            return Results.Json(new { keys = new[] { new { kty = jwk.Kty, use = "sig", alg = "RS256", kid = jwk.Kid, n = jwk.N, e = jwk.E } } });
        });
        _app.MapGet("/connect/authorize", (HttpRequest request) =>
        {
            var code = Guid.NewGuid().ToString("N");
            _codes[code] = (request.Query["nonce"].ToString(), request.Query["code_challenge"].ToString());
            return Results.Redirect($"{request.Query["redirect_uri"]}?code={code}&state={Uri.EscapeDataString(request.Query["state"].ToString())}");
        });
        _app.MapPost("/connect/token", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync().ConfigureAwait(false);
            if (form["client_secret"] != ClientSecret || form["client_id"] != ClientId || !_codes.Remove(form["code"].ToString(), out var stored))
            {
                return Results.BadRequest(new { error = "invalid_client" });
            }

            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
            if (challenge != stored.Challenge)
            {
                return Results.BadRequest(new { error = "invalid_grant" });
            }

            return Results.Json(new
            {
                access_token = Token(Permissions.Select(permission => new Claim("permissions", permission)), null),
                id_token = Token([new Claim("name", UserName)], stored.Nonce),
                token_type = "Bearer",
                expires_in = 3600,
            });
        });
        _app.MapGet("/connect/endsession", () => Results.Ok());

        await _app.StartAsync().ConfigureAwait(false);
        Client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync().ConfigureAwait(false);
        }

        _rsa.Dispose();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string Token(IEnumerable<Claim> extra, string? nonce)
    {
        var claims = new List<Claim> { new("sub", "user-1") };
        claims.AddRange(extra);
        if (nonce is not null)
        {
            claims.Add(new Claim("nonce", nonce));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
