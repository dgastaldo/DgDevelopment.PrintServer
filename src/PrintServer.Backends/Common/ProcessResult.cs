namespace PrintServer.Backends.Common;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
