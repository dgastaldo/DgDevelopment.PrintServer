using System.Globalization;
using Microsoft.Extensions.Localization;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.WebUi;

/// <summary>Everything the page writes in words: looks the text up in the current culture (English by default, Italian available).</summary>
public sealed class UiText(IStringLocalizer<Strings> localizer)
{
    public string this[string key] => localizer[key].Value;

    public string Format(string key, params object[] arguments) => localizer[key, arguments].Value;

    public string StateLabel(JobState state) => state switch
    {
        JobState.Pending => this["State.Pending"],
        JobState.Converting => this["State.Converting"],
        JobState.Printing => this["State.Printing"],
        JobState.Completed => this["State.Completed"],
        JobState.Failed => this["State.Failed"],
        JobState.Stalled => this["State.Stalled"],
        JobState.Cancelled => this["State.Cancelled"],
        _ => state.ToString(),
    };

    public string Printer(string? name) => name ?? this["Add.DefaultPrinter"];

    public string Options(PrintOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var parts = new List<string>();
        if (options.Orientation != PageOrientation.Default)
        {
            parts.Add(options.Orientation == PageOrientation.Landscape ? this["Add.Landscape"] : this["Add.Portrait"]);
        }

        var duplex = options.Duplex switch
        {
            DuplexMode.OneSided => this["Add.OneSided"],
            DuplexMode.TwoSidedLongEdge => this["Add.TwoSidedLongEdge"],
            DuplexMode.TwoSidedShortEdge => this["Add.TwoSidedShortEdge"],
            _ => null,
        };
        if (duplex is not null)
        {
            parts.Add(duplex);
        }

        return parts.Count == 0 ? this["Options.Defaults"] : string.Join(this["Options.Separator"], parts);
    }

    public string Availability(PrinterInfo printer)
    {
        ArgumentNullException.ThrowIfNull(printer);

        return printer.Availability switch
        {
            PrinterAvailability.Ready => this["Availability.Ready"],
            PrinterAvailability.Busy => this["Availability.Busy"],
            _ => this["Availability.Unavailable"],
        };
    }

    public string Issues(PrinterInfo printer)
    {
        ArgumentNullException.ThrowIfNull(printer);

        var parts = new List<string>();
        if (printer.Availability == PrinterAvailability.Offline)
        {
            parts.Add(this["Availability.Unavailable"]);
        }

        AddIssue(parts, printer.Issues, PrinterIssues.PaperOut, "Issue.PaperOut");
        AddIssue(parts, printer.Issues, PrinterIssues.TonerOut, "Issue.TonerOut");
        AddIssue(parts, printer.Issues, PrinterIssues.PaperJam, "Issue.PaperJam");
        AddIssue(parts, printer.Issues, PrinterIssues.DoorOpen, "Issue.DoorOpen");
        AddIssue(parts, printer.Issues, PrinterIssues.Error, "Issue.Error");
        return string.Join(this["Options.Separator"], parts);
    }

    public static string When(DateTimeOffset? value) =>
        value is null ? "-" : value.Value.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0 / 1024.0:0.#} MB"),
    };

    private void AddIssue(List<string> parts, PrinterIssues issues, PrinterIssues flag, string key)
    {
        if (issues.HasFlag(flag))
        {
            parts.Add(this[key]);
        }
    }
}
