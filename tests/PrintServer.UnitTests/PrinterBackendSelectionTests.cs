using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrintServer.Backends.Cups;
using PrintServer.Backends.Ipp;
using PrintServer.Backends.Windows;
using PrintServer.Backends.Fake;
using PrintServer.Backends.Selection;
using PrintServer.Core.Printing;

namespace PrintServer.UnitTests;

public sealed class PrinterBackendSelectionTests
{
    [Theory]
    [InlineData(HostPlatform.Windows, PrinterBackendKind.WindowsSpooler)]
    [InlineData(HostPlatform.MacOS, PrinterBackendKind.Cups)]
    [InlineData(HostPlatform.Linux, PrinterBackendKind.Cups)]
    public void AutoPicksTheNativeBackendForThePlatform(HostPlatform platform, PrinterBackendKind expected)
    {
        Assert.Equal(expected, PrinterBackendSelector.Resolve(PrinterBackendKind.Auto, platform));
    }

    [Fact]
    public void AutoOnUnsupportedPlatformThrows()
    {
        Assert.Throws<PlatformNotSupportedException>(() => PrinterBackendSelector.Resolve(PrinterBackendKind.Auto, HostPlatform.Other));
    }

    [Theory]
    [InlineData(PrinterBackendKind.Fake)]
    [InlineData(PrinterBackendKind.Ipp)]
    [InlineData(PrinterBackendKind.Cups)]
    [InlineData(PrinterBackendKind.WindowsSpooler)]
    public void ExplicitChoiceWinsOverPlatform(PrinterBackendKind kind)
    {
        Assert.Equal(kind, PrinterBackendSelector.Resolve(kind, HostPlatform.Windows));
    }

    [Fact]
    public void DetectorReturnsAKnownPlatform()
    {
        Assert.NotEqual(HostPlatform.Other, HostPlatformDetector.Current());
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, HostPlatform? platform = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddPrinterBackend(configuration, platform);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task FakeBackendIsRegisteredByDefault()
    {
        using var provider = Build([]);

        var backend = provider.GetRequiredService<IPrinterBackend>();

        Assert.IsType<FakePrinterBackend>(backend);
        Assert.Single(await backend.ListPrintersAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FakePrintersComeFromConfiguration()
    {
        using var provider = Build(new()
        {
            ["Printing:Backend"] = "Fake",
            ["Printing:Fake:Printers:0"] = "Office",
            ["Printing:Fake:Printers:1"] = "Warehouse",
        });

        var printers = await provider.GetRequiredService<IPrinterBackend>().ListPrintersAsync(CancellationToken.None);

        Assert.Equal(["Office", "Warehouse"], printers.Select(p => p.Name));
    }

    [Fact]
    public void BackendIsASingleton()
    {
        using var provider = Build([]);

        Assert.Same(provider.GetRequiredService<IPrinterBackend>(), provider.GetRequiredService<IPrinterBackend>());
    }

    [Fact]
    public void CupsBackendIsRegisteredWhenSelected()
    {
        using var provider = Build(new() { ["Printing:Backend"] = "Cups" });

        Assert.IsType<CupsBackend>(provider.GetRequiredService<IPrinterBackend>());
    }

    [Fact]
    public void AutoOnLinuxRegistersTheCupsBackend()
    {
        using var provider = Build(new() { ["Printing:Backend"] = "Auto" }, HostPlatform.Linux);

        Assert.IsType<CupsBackend>(provider.GetRequiredService<IPrinterBackend>());
    }

    [Fact]
    public void CupsPathsComeFromConfiguration()
    {
        using var provider = Build(new()
        {
            ["Printing:Backend"] = "Cups",
            ["Printing:Cups:LpPath"] = "/opt/cups/bin/lp",
        });

        Assert.Equal("/opt/cups/bin/lp", provider.GetRequiredService<IOptions<CupsOptions>>().Value.LpPath);
    }

    [Fact]
    public void IppBackendIsRegisteredWithItsPrintersFromConfiguration()
    {
        using var provider = Build(new()
        {
            ["Printing:Backend"] = "Ipp",
            ["Printing:Ipp:Printers:0:Name"] = "Office",
            ["Printing:Ipp:Printers:0:Uri"] = "ipp://office.local/ipp/print",
            ["Printing:Ipp:UserName"] = "kiosk",
        });

        var options = provider.GetRequiredService<IOptions<IppOptions>>().Value;

        Assert.IsType<IppBackend>(provider.GetRequiredService<IPrinterBackend>());
        var printer = Assert.Single(options.Printers);
        Assert.Equal("Office", printer.Name);
        Assert.Equal(new Uri("ipp://office.local/ipp/print"), printer.Uri);
        Assert.Equal("kiosk", options.UserName);
    }

    [Fact]
    public async Task IppBackendWithNoPrintersListsNothing()
    {
        using var provider = Build(new() { ["Printing:Backend"] = "Ipp", ["Printing:Ipp:AllowInvalidCertificates"] = "true" });

        Assert.Empty(await provider.GetRequiredService<IPrinterBackend>().ListPrintersAsync(CancellationToken.None));
    }

    [Fact]
    public void WindowsSpoolerBackendOnlyRegistersOnWindows()
    {
        var settings = new Dictionary<string, string?> { ["Printing:Backend"] = "WindowsSpooler" };

        if (OperatingSystem.IsWindows())
        {
            using var provider = Build(settings);
            Assert.IsType<WindowsSpoolerBackend>(provider.GetRequiredService<IPrinterBackend>());
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => Build(settings));
        }
    }

    [Fact]
    public void AutoOnWindowsSelectsTheSpoolerBackend()
    {
        var settings = new Dictionary<string, string?> { ["Printing:Backend"] = "Auto" };

        if (OperatingSystem.IsWindows())
        {
            using var provider = Build(settings, HostPlatform.Windows);
            Assert.IsType<WindowsSpoolerBackend>(provider.GetRequiredService<IPrinterBackend>());
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => Build(settings, HostPlatform.Windows));
        }
    }
}
