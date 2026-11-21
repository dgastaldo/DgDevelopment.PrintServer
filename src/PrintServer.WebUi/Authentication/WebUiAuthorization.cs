using Microsoft.AspNetCore.Authorization;

namespace PrintServer.WebUi.Authentication;

internal static class WebUiAuthorization
{
    public static void AddPolicies(AuthorizationOptions authorization, WebUiOptions options)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(options);

        authorization.AddPolicy(WebUiPermissions.AccessPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(WebUiPermissions.ClaimType, options.ViewPermission, options.ManagePermission, options.AddPermission));
        authorization.AddPolicy(WebUiPermissions.ManagePolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(WebUiPermissions.ClaimType, options.ManagePermission));
        authorization.AddPolicy(WebUiPermissions.AddPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(WebUiPermissions.ClaimType, options.AddPermission));
    }
}
