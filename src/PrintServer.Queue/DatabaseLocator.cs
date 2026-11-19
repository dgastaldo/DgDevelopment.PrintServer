namespace PrintServer.Queue;

public enum QueuePlatform
{
    Windows,
    MacOS,
    Linux,
}

internal static class DatabaseLocator
{
    public const string FileName = "queue.db";

    public static QueuePlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return QueuePlatform.Windows;
        }

        return OperatingSystem.IsMacOS() ? QueuePlatform.MacOS : QueuePlatform.Linux;
    }

    public static string Resolve(string? configuredPath, QueuePlatform platform, Func<string, bool> isWritable)
    {
        ArgumentNullException.ThrowIfNull(isWritable);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var machineWide = MachineWideDirectory(platform);
        var directory = isWritable(machineWide) ? machineWide : UserDirectory();
        return Path.Combine(directory, FileName);
    }

    public static bool CanWrite(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string MachineWideDirectory(QueuePlatform platform) => platform switch
    {
        QueuePlatform.Windows => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PrintServer"),
        QueuePlatform.MacOS => "/Library/Application Support/PrintServer",
        _ => "/var/lib/printserver",
    };

    internal static string UserDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintServer");
}
