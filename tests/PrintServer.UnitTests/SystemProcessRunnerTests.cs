using PrintServer.Backends.Common;

namespace PrintServer.UnitTests;

public sealed class SystemProcessRunnerTests
{
    private readonly SystemProcessRunner _runner = new();

    [Fact]
    public async Task ArgumentsAreNotInterpretedByAShell()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await _runner.RunAsync("/bin/echo", ["a b", "$(id)", ";rm", "`id`"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("a b $(id) ;rm `id`\n", result.StandardOutput);
    }

    [Fact]
    public async Task ExitCodeAndStandardErrorAreCaptured()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await _runner.RunAsync("/bin/sh", ["-c", "echo oops >&2; exit 3"], CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("oops\n", result.StandardError);
    }

    [Fact]
    public async Task MissingExecutableIsReportedClearly()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runner.RunAsync("definitely-not-a-real-binary", [], CancellationToken.None));

        Assert.Contains("definitely-not-a-real-binary", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationStopsTheProcess()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _runner.RunAsync("/bin/sleep", ["30"], cts.Token));
    }
}
