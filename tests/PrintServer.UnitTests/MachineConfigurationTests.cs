using Microsoft.Extensions.Configuration;
using PrintServer.Backends.Selection;
using PrintServer.Host;

namespace PrintServer.UnitTests;

public sealed class MachineConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"printserver-machine-{Guid.NewGuid():N}");

    public MachineConfigurationTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string name, string json)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ThePathFromTheEnvironmentWins()
    {
        var path = Path.Combine(_directory, "custom.json");

        Assert.Equal(path, MachineConfiguration.Resolve(path, HostPlatform.Linux));
    }

    [Theory]
    [InlineData(HostPlatform.Linux, "/etc/printserver")]
    [InlineData(HostPlatform.Other, "/etc/printserver")]
    [InlineData(HostPlatform.MacOS, "/Library/Application Support/PrintServer")]
    public void TheDefaultPathDependsOnThePlatform(HostPlatform platform, string directory)
    {
        Assert.Equal(Path.Combine(directory, "appsettings.json"), MachineConfiguration.Resolve(null, platform));
        Assert.Equal(Path.Combine(directory, "appsettings.json"), MachineConfiguration.Resolve("  ", platform));
    }

    [Fact]
    public void OnWindowsTheDefaultPathIsInProgramData()
    {
        var path = MachineConfiguration.Resolve(null, HostPlatform.Windows);

        Assert.EndsWith(Path.Combine("PrintServer", "appsettings.json"), path, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMachineFileOverridesTheBundledOneButNotTheEnvironment()
    {
        var bundled = Write("bundled.json", """{ "Watcher": { "Path": "bundled" }, "Queue": { "StallAfter": "01:00:00" }, "Printing": { "Backend": "Fake" } }""");
        var machine = Write("machine.json", """{ "Watcher": { "Path": "machine" }, "Queue": { "StallAfter": "02:00:00" } }""");
        var configuration = new ConfigurationManager();
        configuration.AddJsonFile(bundled, optional: false);
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Queue:StallAfter"] = "03:00:00" });

        MachineConfiguration.Add(configuration, machine);

        Assert.Equal("machine", configuration["Watcher:Path"]);
        Assert.Equal("Fake", configuration["Printing:Backend"]);
        Assert.Equal("03:00:00", configuration["Queue:StallAfter"]);
    }

    [Fact]
    public void AMissingMachineFileIsIgnored()
    {
        var bundled = Write("bundled.json", """{ "Watcher": { "Path": "bundled" } }""");
        var configuration = new ConfigurationManager();
        configuration.AddJsonFile(bundled, optional: false);

        MachineConfiguration.Add(configuration, Path.Combine(_directory, "missing", "appsettings.json"));

        Assert.Equal("bundled", configuration["Watcher:Path"]);
    }

    [Fact]
    public void WithoutAnyJsonSourceTheFileIsStillAdded()
    {
        var machine = Write("machine.json", """{ "Watcher": { "Path": "machine" } }""");
        var configuration = new ConfigurationManager();

        MachineConfiguration.Add(configuration, machine);

        Assert.Equal("machine", configuration["Watcher:Path"]);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => MachineConfiguration.Add(null!, "x"));
        Assert.Throws<ArgumentException>(() => MachineConfiguration.Add(new ConfigurationManager(), " "));
    }
}
