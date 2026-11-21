using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

internal sealed class JobFileMover(IJobFiles files, IJobStore store)
{
    public async Task<bool> MoveDocumentAsync(Job job, string folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!files.Exists(job.DocumentPath))
        {
            return false;
        }

        try
        {
            var destination = MoveToFolder(job.DocumentPath, job.OriginDirectory, folder, job.FileName);
            DeleteConvertedFile(job);
            files.DeleteDirectoryIfEmpty(Path.GetDirectoryName(job.DocumentPath)!);
            await store.UpdateDocumentPathAsync(job.Id, destination, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void DeleteConvertedFile(Job job)
    {
        var converted = JobPaths.ConvertedFile(job.DocumentPath);
        if (!DocumentFormats.IsPrintable(job.SourceFormat) && files.Exists(converted))
        {
            files.Delete(converted);
        }
    }

    public string MoveToFolder(string source, string originDirectory, string folder, string fileName)
    {
        var directory = Path.Combine(originDirectory, folder);
        files.CreateDirectory(directory);
        var destination = UniquePath(directory, fileName);
        files.Move(source, destination);
        return destination;
    }

    private string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var index = 1;
        while (files.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            index++;
        }

        return candidate;
    }
}
