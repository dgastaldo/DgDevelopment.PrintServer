namespace PrintServer.Core.Printing;

public sealed record PrinterInfo(string Name, bool IsDefault, PrinterAvailability Availability);
