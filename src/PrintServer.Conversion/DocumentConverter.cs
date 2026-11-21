using PrintServer.Core.Printing;

namespace PrintServer.Conversion;

public sealed class DocumentConverter(IEnumerable<IDocumentConverter> converters) : IDocumentConverter
{
    private readonly IReadOnlyList<IDocumentConverter> _converters = [.. converters];

    public bool CanConvert(string sourceFormat) => _converters.Any(converter => converter.CanConvert(sourceFormat));

    public Task<ConversionResult> ConvertToPdfAsync(string sourcePath, string sourceFormat, string outputPath, CancellationToken ct)
    {
        var converter = _converters.FirstOrDefault(candidate => candidate.CanConvert(sourceFormat));
        return converter is null
            ? Task.FromResult(ConversionResult.Failure($"No converter is available for '{sourceFormat}'."))
            : converter.ConvertToPdfAsync(sourcePath, sourceFormat, outputPath, ct);
    }
}
