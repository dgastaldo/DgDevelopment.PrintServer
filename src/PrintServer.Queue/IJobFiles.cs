namespace PrintServer.Queue;

public interface IJobFiles
{
    bool Exists(string path);

    Task<string> ComputeHashAsync(string path, CancellationToken ct);

    void CreateDirectory(string path);

    void Move(string source, string destination);

    void Delete(string path);

    void DeleteDirectoryIfEmpty(string path);
}
