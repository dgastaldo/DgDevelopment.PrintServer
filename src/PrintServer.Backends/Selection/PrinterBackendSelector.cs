namespace PrintServer.Backends.Selection;

public static class PrinterBackendSelector
{
    public static PrinterBackendKind Resolve(PrinterBackendKind configured, HostPlatform platform)
    {
        if (configured != PrinterBackendKind.Auto)
        {
            return configured;
        }

        return platform switch
        {
            HostPlatform.Windows => PrinterBackendKind.WindowsSpooler,
            HostPlatform.MacOS or HostPlatform.Linux => PrinterBackendKind.Cups,
            _ => throw new PlatformNotSupportedException(
                "No native printer backend exists for this platform; set Printing:Backend to Ipp or Fake."),
        };
    }
}
