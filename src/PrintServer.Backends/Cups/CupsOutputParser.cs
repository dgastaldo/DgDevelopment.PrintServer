using System.Globalization;
using System.Text.RegularExpressions;
using PrintServer.Core.Printing;

namespace PrintServer.Backends.Cups;

internal static partial class CupsOutputParser
{
    private const string NoDestinationsMessage = "No destinations added";

    public static IReadOnlyList<PrinterInfo> ParsePrinters(string lpstatOutput)
    {
        string? defaultPrinter = null;
        var printers = new List<(string Name, PrinterAvailability Availability)>();

        foreach (var rawLine in lpstatOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            var defaultMatch = DefaultDestinationRegex().Match(line);
            if (defaultMatch.Success)
            {
                defaultPrinter = defaultMatch.Groups[1].Value;
                continue;
            }

            var printerMatch = PrinterLineRegex().Match(line);
            if (printerMatch.Success)
            {
                printers.Add((printerMatch.Groups[1].Value, ParseAvailability(printerMatch.Groups[2].Value)));
            }
        }

        return printers
            .Select(p => new PrinterInfo(p.Name, string.Equals(p.Name, defaultPrinter, StringComparison.Ordinal), p.Availability))
            .ToList();
    }

    public static bool IsNoDestinations(string standardError) =>
        standardError.Contains(NoDestinationsMessage, StringComparison.OrdinalIgnoreCase);

    public static string? ParseRequestId(string lpOutput)
    {
        var match = RequestIdRegex().Match(lpOutput);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static IReadOnlySet<string> ParsePendingJobIds(string lpstatOutput)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in lpstatOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = JobLineRegex().Match(rawLine);
            if (match.Success)
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids;
    }

    public static bool IsPrintingJob(string lpstatPrinterOutput, string jobId) =>
        lpstatPrinterOutput.Contains($"now printing {jobId}.", StringComparison.Ordinal);

    public static string PrinterOfJob(string jobId)
    {
        var separator = jobId.LastIndexOf('-');
        return separator > 0 && int.TryParse(jobId.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? jobId[..separator]
            : jobId;
    }

    private static PrinterAvailability ParseAvailability(string state)
    {
        if (state.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return PrinterAvailability.Offline;
        }

        return state.Contains("now printing", StringComparison.OrdinalIgnoreCase)
            ? PrinterAvailability.Busy
            : PrinterAvailability.Ready;
    }

    [GeneratedRegex(@"^system default destination:\s*(\S+)$")]
    private static partial Regex DefaultDestinationRegex();

    [GeneratedRegex(@"^printer\s+(\S+)\s+(.*)$")]
    private static partial Regex PrinterLineRegex();

    [GeneratedRegex(@"^request id is\s+(\S+)\s+\(")]
    private static partial Regex RequestIdRegex();

    [GeneratedRegex(@"^(\S+-\d+)\s+\S+\s+\d+", RegexOptions.Multiline)]
    private static partial Regex JobLineRegex();
}
