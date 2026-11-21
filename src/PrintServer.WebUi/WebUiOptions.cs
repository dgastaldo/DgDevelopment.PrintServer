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

    /// <summary>Hard ceiling for <see cref="MaxUploadBytes"/>: uploads through the page are never larger than this.</summary>
    public const long UploadSizeLimit = 8 * 1024 * 1024;

    public long MaxUploadBytes { get; set; } = UploadSizeLimit;

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(3);

    public bool IsOidcConfigured => !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);
}
