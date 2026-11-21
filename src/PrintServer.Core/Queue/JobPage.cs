namespace PrintServer.Core.Queue;

public sealed record JobPage(IReadOnlyList<Job> Items, int Total);
