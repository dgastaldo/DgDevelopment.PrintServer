using PrintServer.Core.Printing;

namespace PrintServer.Backends.Common;

internal static class PrinterStateReasons
{
    private const string ErrorSuffix = "-error";
    private const string WarningSuffix = "-warning";
    private const string ReportSuffix = "-report";

    private static readonly Dictionary<string, PrinterIssues> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["media-empty"] = PrinterIssues.PaperOut,
        ["media-needed"] = PrinterIssues.PaperOut,
        ["media-jam"] = PrinterIssues.PaperJam,
        ["toner-empty"] = PrinterIssues.TonerOut,
        ["marker-supply-empty"] = PrinterIssues.TonerOut,
        ["door-open"] = PrinterIssues.DoorOpen,
        ["cover-open"] = PrinterIssues.DoorOpen,
        ["interlock-open"] = PrinterIssues.DoorOpen,
    };

    public static PrinterIssues ToIssues(IEnumerable<string> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);

        var issues = PrinterIssues.None;
        foreach (var reason in reasons)
        {
            issues |= ToIssue(reason.Trim());
        }

        return issues;
    }

    private static PrinterIssues ToIssue(string reason)
    {
        var isError = reason.EndsWith(ErrorSuffix, StringComparison.OrdinalIgnoreCase);
        var isNotice = reason.EndsWith(WarningSuffix, StringComparison.OrdinalIgnoreCase)
            || reason.EndsWith(ReportSuffix, StringComparison.OrdinalIgnoreCase);
        var keyword = Strip(reason);

        if (keyword.Equals("offline", StringComparison.OrdinalIgnoreCase) || keyword.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
        {
            return PrinterIssues.Offline;
        }

        if (isNotice)
        {
            return PrinterIssues.None;
        }

        if (Known.TryGetValue(keyword, out var issue))
        {
            return issue;
        }

        return isError ? PrinterIssues.Error : PrinterIssues.None;
    }

    private static string Strip(string reason)
    {
        foreach (var suffix in new[] { ErrorSuffix, WarningSuffix, ReportSuffix })
        {
            if (reason.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return reason[..^suffix.Length];
            }
        }

        return reason;
    }
}
