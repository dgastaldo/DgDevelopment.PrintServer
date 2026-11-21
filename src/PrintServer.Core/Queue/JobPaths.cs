using PrintServer.Core.Printing;

namespace PrintServer.Core.Queue;

public static class JobPaths
{
    private const string ConvertedSuffix = ".pdf";

    public static string ConvertedFile(string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        return documentPath + ConvertedSuffix;
    }

    public static string PrintFile(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return DocumentFormats.IsPrintable(job.SourceFormat) ? job.DocumentPath : ConvertedFile(job.DocumentPath);
    }
}
