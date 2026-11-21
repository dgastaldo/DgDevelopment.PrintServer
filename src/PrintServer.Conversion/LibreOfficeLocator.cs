using PrintServer.Backends.Selection;

namespace PrintServer.Conversion;

internal static class LibreOfficeLocator
{
    public static string? Find(string? configuredPath, HostPlatform platform, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return exists(configuredPath) ? configuredPath : null;
        }

        return Candidates(platform).FirstOrDefault(exists);
    }

    internal static IEnumerable<string> Candidates(HostPlatform platform)
    {
        switch (platform)
        {
            case HostPlatform.Windows:
                foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                {
                    var root = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(root))
                    {
                        yield return Path.Combine(root, "LibreOffice", "program", "soffice.exe");
                    }
                }

                break;
            case HostPlatform.MacOS:
                yield return "/Applications/LibreOffice.app/Contents/MacOS/soffice";
                break;
            default:
                yield return "/usr/bin/soffice";
                yield return "/usr/bin/libreoffice";
                yield return "/usr/local/bin/soffice";
                yield return "/opt/libreoffice/program/soffice";
                break;
        }
    }
}
