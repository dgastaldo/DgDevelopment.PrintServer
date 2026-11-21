namespace PrintServer.Core.Printing;

public interface IDocumentConverter
{
    bool CanConvert(string sourceFormat);

    Task<ConversionResult> ConvertToPdfAsync(string sourcePath, string sourceFormat, string outputPath, CancellationToken ct);
}
