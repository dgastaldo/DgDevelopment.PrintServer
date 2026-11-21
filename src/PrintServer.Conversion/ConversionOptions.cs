namespace PrintServer.Conversion;

public sealed class ConversionOptions
{
    public const string SectionName = "Conversion";

    public string? LibreOfficePath { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}
