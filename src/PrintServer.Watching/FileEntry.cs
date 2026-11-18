namespace PrintServer.Watching;

public sealed record FileEntry(string Path, long Length, DateTimeOffset LastWriteUtc);
