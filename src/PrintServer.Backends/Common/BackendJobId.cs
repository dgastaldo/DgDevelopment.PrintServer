using System.Globalization;

namespace PrintServer.Backends.Common;

internal static class BackendJobId
{
    private const char Separator = '@';

    public static string Format(int jobId, string printerName) =>
        string.Create(CultureInfo.InvariantCulture, $"{jobId}{Separator}{printerName}");

    public static bool TryParse(string value, out int jobId, out string printerName)
    {
        jobId = 0;
        printerName = string.Empty;

        var separator = value.IndexOf(Separator, StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out jobId))
        {
            return false;
        }

        printerName = value[(separator + 1)..];
        return true;
    }
}
