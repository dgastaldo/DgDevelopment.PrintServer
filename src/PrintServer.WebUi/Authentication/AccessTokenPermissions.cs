using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PrintServer.WebUi.Authentication;

internal static class AccessTokenPermissions
{
    public static IReadOnlyList<Claim> Read(string? accessToken, string claimName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimName);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return [];
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(accessToken))
        {
            return [];
        }

        return [.. handler.ReadJwtToken(accessToken).Claims
            .Where(claim => string.Equals(claim.Type, claimName, StringComparison.Ordinal))
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Select(value => new Claim(WebUiPermissions.ClaimType, value))];
    }
}
