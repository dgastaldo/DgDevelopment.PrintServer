namespace PrintServer.Core.Queue;

public sealed record JobQuery(JobState? State = null, string? Search = null);
