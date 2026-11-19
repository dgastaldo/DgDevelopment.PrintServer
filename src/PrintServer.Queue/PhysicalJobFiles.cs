using System.Security.Cryptography;

namespace PrintServer.Queue;

public sealed class PhysicalJobFiles : IJobFiles
{
    public bool Exists(string path) => File.Exists(path);

    public async Task<string> ComputeHashAsync(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void Move(string source, string destination) => File.Move(source, destination);

    public void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }
}
