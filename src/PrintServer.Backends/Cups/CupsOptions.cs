namespace PrintServer.Backends.Cups;

public sealed class CupsOptions
{
    public string LpPath { get; set; } = "/usr/bin/lp";

    public string LpstatPath { get; set; } = "/usr/bin/lpstat";

    public string CancelPath { get; set; } = "/usr/bin/cancel";
}
