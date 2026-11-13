using PrintServer.Backends.Common;

namespace PrintServer.UnitTests;

internal sealed class ScriptedProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results = new();

    public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    public ScriptedProcessRunner Returns(int exitCode, string standardOutput = "", string standardError = "")
    {
        _results.Enqueue(new ProcessResult(exitCode, standardOutput, standardError));
        return this;
    }

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(_results.Dequeue());
    }
}
