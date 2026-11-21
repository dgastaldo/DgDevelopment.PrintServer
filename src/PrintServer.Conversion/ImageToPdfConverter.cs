using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PrintServer.Core.Printing;

namespace PrintServer.Conversion;

public sealed class ImageToPdfConverter : IDocumentConverter
{
    private const double Margin = 18;

    public bool CanConvert(string sourceFormat) => DocumentFormats.IsImage(sourceFormat);

    public Task<ConversionResult> ConvertToPdfAsync(string sourcePath, string sourceFormat, string outputPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ct.ThrowIfCancellationRequested();

        if (!CanConvert(sourceFormat))
        {
            return Task.FromResult(ConversionResult.Failure($"'{sourceFormat}' is not an image format."));
        }

        try
        {
            Render(sourcePath, outputPath);
            return Task.FromResult(ConversionResult.Success());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotImplementedException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(ConversionResult.Failure($"The image could not be converted: {ex.Message}"));
        }
    }

    private static void Render(string sourcePath, string outputPath)
    {
        using var image = XImage.FromFile(sourcePath);
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Size = PageSize.A4;
        page.Orientation = image.PixelWidth > image.PixelHeight ? PdfSharp.PageOrientation.Landscape : PdfSharp.PageOrientation.Portrait;

        var availableWidth = page.Width.Point - (2 * Margin);
        var availableHeight = page.Height.Point - (2 * Margin);
        var scale = Math.Min(availableWidth / image.PointWidth, availableHeight / image.PointHeight);
        var width = image.PointWidth * scale;
        var height = image.PointHeight * scale;

        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.DrawImage(image, (page.Width.Point - width) / 2, (page.Height.Point - height) / 2, width, height);
        }

        document.Save(outputPath);
    }
}
