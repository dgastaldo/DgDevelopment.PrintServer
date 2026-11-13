using Microsoft.Extensions.Options;
using PrintServer.Backends.Cups;
using PrintServer.Core.Printing;

namespace PrintServer.UnitTests;

public sealed class CupsBackendTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"printserver cups {Guid.NewGuid():N}.pdf");
    private readonly ScriptedProcessRunner _runner = new();

    public CupsBackendTests() => File.WriteAllText(_file, "content");

    public void Dispose() => File.Delete(_file);

    private CupsBackend Create() => new(_runner, Options.Create(new CupsOptions { LpPath = "lp-bin", LpstatPath = "lpstat-bin" }));

    [Fact]
    public async Task ListPrintersRunsLpstatAndParsesTheOutput()
    {
        _runner.Returns(0, "printer A is idle.  enabled since x\nsystem default destination: A\n");

        var printers = await Create().ListPrintersAsync(CancellationToken.None);

        var printer = Assert.Single(printers);
        Assert.Equal("A", printer.Name);
        Assert.True(printer.IsDefault);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("lpstat-bin", call.FileName);
        Assert.Equal(["-p", "-d"], call.Arguments);
    }

    [Fact]
    public async Task ListPrintersReturnsEmptyWhenCupsHasNoDestinations()
    {
        _runner.Returns(1, standardError: "lpstat: No destinations added.");

        Assert.Empty(await Create().ListPrintersAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListPrintersThrowsOnOtherFailures()
    {
        _runner.Returns(1, standardError: "lpstat: Forbidden");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Create().ListPrintersAsync(CancellationToken.None));

        Assert.Contains("Forbidden", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintPassesEveryValueAsASeparateArgument()
    {
        _runner.Returns(0, "request id is Office-42 (1 file(s))\n");

        var result = await Create().PrintAsync(new PrintRequest(_file, "Office", Copies: 3, JobName: "Invoice; rm -rf /"), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("Office-42", result.BackendJobId);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("lp-bin", call.FileName);
        Assert.Equal(["-d", "Office", "-n", "3", "-t", "Invoice; rm -rf /", Path.GetFullPath(_file)], call.Arguments);
    }

    [Fact]
    public async Task PrintUsesTheFileNameWhenNoJobNameIsGiven()
    {
        _runner.Returns(0, "request id is Office-1 (1 file(s))\n");

        await Create().PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        Assert.Equal(Path.GetFileName(_file), _runner.Calls[0].Arguments[5]);
    }

    [Fact]
    public async Task PrintFailsWhenLpFails()
    {
        _runner.Returns(1, standardError: "lp: The printer or class does not exist.");

        var result = await Create().PrintAsync(new PrintRequest(_file, "Nope"), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintFailsOnUnexpectedLpOutput()
    {
        _runner.Returns(0, "something else");

        var result = await Create().PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task PrintDoesNotStartAProcessForInvalidRequests()
    {
        var backend = Create();

        Assert.False((await backend.PrintAsync(new PrintRequest(_file + ".missing", "Office"), CancellationToken.None)).Accepted);
        Assert.False((await backend.PrintAsync(new PrintRequest(_file, "Office", Copies: 0), CancellationToken.None)).Accepted);
        Assert.False((await backend.PrintAsync(new PrintRequest(_file, " "), CancellationToken.None)).Accepted);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task JobMissingFromThePendingListIsCompleted()
    {
        _runner.Returns(0, "Other-1   user   10   Fri 13 Nov 2026 10:00:00 CET\n");

        Assert.Equal(JobStatus.Completed, await Create().GetJobStatusAsync("Office-42", CancellationToken.None));
        Assert.Equal(["-W", "not-completed", "-o"], _runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task PendingJobOnAnIdlePrinterIsQueued()
    {
        _runner
            .Returns(0, "Office-42   user   10   Fri 13 Nov 2026 10:00:00 CET\n")
            .Returns(0, "printer Office is idle.  enabled since x\n");

        Assert.Equal(JobStatus.Queued, await Create().GetJobStatusAsync("Office-42", CancellationToken.None));
        Assert.Equal(["-p", "Office"], _runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task PendingJobBeingPrintedIsPrinting()
    {
        _runner
            .Returns(0, "Office-42   user   10   Fri 13 Nov 2026 10:00:00 CET\n")
            .Returns(0, "printer Office now printing Office-42.  enabled since x\n");

        Assert.Equal(JobStatus.Printing, await Create().GetJobStatusAsync("Office-42", CancellationToken.None));
    }

    [Fact]
    public async Task StatusThrowsWhenLpstatFails()
    {
        _runner.Returns(1, standardError: "lpstat: Service unavailable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().GetJobStatusAsync("Office-42", CancellationToken.None));
    }
}
