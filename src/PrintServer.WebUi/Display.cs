using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.WebUi;

public static class Display
{
    public static IReadOnlyList<JobState> AllStates { get; } =
        [JobState.Pending, JobState.Converting, JobState.Printing, JobState.Completed, JobState.Failed, JobState.Stalled, JobState.Cancelled];

    public static string StateLabel(JobState state) => state switch
    {
        JobState.Pending => "Pending",
        JobState.Converting => "Converting",
        JobState.Printing => "Printing",
        JobState.Completed => "Completed",
        JobState.Failed => "Failed",
        JobState.Stalled => "Stalled",
        JobState.Cancelled => "Cancelled",
        _ => state.ToString(),
    };

    public static string StateClass(JobState state) => state switch
    {
        JobState.Completed => "ok",
        JobState.Failed or JobState.Stalled => "bad",
        JobState.Cancelled => "muted",
        _ => "info",
    };

    public static bool CanRetry(JobState state) => state is JobState.Failed or JobState.Stalled or JobState.Cancelled;

    public static bool CanCancel(JobState state) => JobStates.IsActive(state);

    public static string Printer(string? name) => name ?? "Predefinita";

    public static string Options(PrintOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var parts = new List<string>();
        if (options.Orientation != PageOrientation.Default)
        {
            parts.Add(options.Orientation == PageOrientation.Landscape ? "Orizzontale" : "Verticale");
        }

        var duplex = options.Duplex switch
        {
            DuplexMode.OneSided => "Solo fronte",
            DuplexMode.TwoSidedLongEdge => "Fronte e retro (lato lungo)",
            DuplexMode.TwoSidedShortEdge => "Fronte e retro (lato corto)",
            _ => null,
        };
        if (duplex is not null)
        {
            parts.Add(duplex);
        }

        return parts.Count == 0 ? "Predefinite" : string.Join(", ", parts);
    }

    public static string Issues(PrinterInfo printer)
    {
        ArgumentNullException.ThrowIfNull(printer);

        var parts = new List<string>();
        if (printer.Availability == PrinterAvailability.Offline)
        {
            parts.Add("Non disponibile");
        }

        AddIssue(parts, printer.Issues, PrinterIssues.PaperOut, "Carta esaurita");
        AddIssue(parts, printer.Issues, PrinterIssues.TonerOut, "Toner esaurito");
        AddIssue(parts, printer.Issues, PrinterIssues.PaperJam, "Carta inceppata");
        AddIssue(parts, printer.Issues, PrinterIssues.DoorOpen, "Sportello aperto");
        AddIssue(parts, printer.Issues, PrinterIssues.Error, "Errore");
        return string.Join(", ", parts);
    }

    public static string When(DateTimeOffset? value) =>
        value is null ? "-" : value.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };

    private static void AddIssue(List<string> parts, PrinterIssues issues, PrinterIssues flag, string text)
    {
        if (issues.HasFlag(flag))
        {
            parts.Add(text);
        }
    }
}
