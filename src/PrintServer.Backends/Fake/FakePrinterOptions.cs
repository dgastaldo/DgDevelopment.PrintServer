using PrintServer.Core.Printing;

namespace PrintServer.Backends.Fake;

public sealed class FakePrinterOptions
{
    public IList<string> Printers { get; } = [];

    public TimeSpan QueueDelay { get; set; } = TimeSpan.Zero;

    public TimeSpan PrintDuration { get; set; } = TimeSpan.Zero;

    public Func<PrintRequest, string?>? FailureRule { get; set; }
}
