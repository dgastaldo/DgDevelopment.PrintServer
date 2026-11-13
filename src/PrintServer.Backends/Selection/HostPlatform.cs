namespace PrintServer.Backends.Selection;

public enum HostPlatform
{
    Windows,
    MacOS,
    Linux,
    Other,
}

public static class HostPlatformDetector
{
    public static HostPlatform Current()
    {
        if (OperatingSystem.IsWindows())
        {
            return HostPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return HostPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return HostPlatform.Linux;
        }

        return HostPlatform.Other;
    }
}
