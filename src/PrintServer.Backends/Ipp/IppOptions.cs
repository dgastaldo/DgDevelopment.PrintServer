namespace PrintServer.Backends.Ipp;

public sealed class IppPrinterOptions
{
    public string Name { get; set; } = string.Empty;

    public Uri? Uri { get; set; }
}

public sealed class IppOptions
{
    public IList<IppPrinterOptions> Printers { get; } = [];

    public string UserName { get; set; } = "printserver";

    public string? DocumentFormat { get; set; }

    public bool AllowInvalidCertificates { get; set; }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
