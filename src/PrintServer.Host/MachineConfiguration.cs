using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using PrintServer.Backends.Selection;

namespace PrintServer.Host;

internal static class MachineConfiguration
{
    public const string EnvironmentVariable = "PRINTSERVER_CONFIG";

    private const string FileName = "appsettings.json";

    public static string Resolve(string? overridePath, HostPlatform platform)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(DefaultDirectory(platform), FileName);
    }

    public static void Add(ConfigurationManager configuration, string path)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var source = new JsonConfigurationSource { Path = path, Optional = true, ReloadOnChange = false };
        source.ResolveFileProvider();

        var lastJson = -1;
        for (var index = 0; index < configuration.Sources.Count; index++)
        {
            if (configuration.Sources[index] is JsonConfigurationSource)
            {
                lastJson = index;
            }
        }

        configuration.Sources.Insert(lastJson + 1, source);
    }

    internal static string DefaultDirectory(HostPlatform platform) => platform switch
    {
        HostPlatform.Windows => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PrintServer"),
        HostPlatform.MacOS => "/Library/Application Support/PrintServer",
        _ => "/etc/printserver",
    };
}
