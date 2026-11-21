namespace PrintServer.WebUi;

public sealed class WebUiOptions
{
    public const string SectionName = "WebUi";

    public bool Enabled { get; set; } = true;

    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    public string PermissionClaim { get; set; } = "permissions";

    public string ViewPermission { get; set; } = "printserver.view";

    public string ManagePermission { get; set; } = "printserver.manage";

    public string AddPermission { get; set; } = "printserver.add";

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Largest file the upload page accepts. Fixed on purpose: it is not configurable.</summary>
    public const long UploadSizeLimit = 8_000_000;

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(3);

    public bool IsOidcConfigured => !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);
}
