using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintServer.Backends.Common;
using PrintServer.Backends.Cups;
using PrintServer.Backends.Fake;
using PrintServer.Backends.Windows;
using PrintServer.Core.Printing;
using System.Runtime.Versioning;

namespace PrintServer.Backends.Selection;

public static class PrinterBackendServiceCollectionExtensions
{
    public static IServiceCollection AddPrinterBackend(
        this IServiceCollection services,
        IConfiguration configuration,
        HostPlatform? platform = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(PrinterBackendOptions.SectionName);
        var options = new PrinterBackendOptions();
        section.Bind(options);

        var kind = PrinterBackendSelector.Resolve(options.Backend, platform ?? HostPlatformDetector.Current());

        services.TryAddSingleton(TimeProvider.System);

        switch (kind)
        {
            case PrinterBackendKind.Fake:
                services.Configure<FakePrinterOptions>(section.GetSection("Fake"));
                services.AddSingleton<IPrinterBackend, FakePrinterBackend>();
                break;
            case PrinterBackendKind.Cups:
                services.Configure<CupsOptions>(section.GetSection("Cups"));
                services.TryAddSingleton<IProcessRunner, SystemProcessRunner>();
                services.AddSingleton<IPrinterBackend, CupsBackend>();
                break;
            case PrinterBackendKind.WindowsSpooler:
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("The WindowsSpooler backend only runs on Windows.");
                }

                AddWindowsSpooler(services);
                break;
            default:
                throw new NotSupportedException($"The {kind} printer backend is not implemented yet.");
        }

        return services;
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsSpooler(IServiceCollection services)
    {
        services.AddSingleton<ISpoolerApi, NativeSpoolerApi>();
        services.AddSingleton<IPrinterBackend, WindowsSpoolerBackend>();
    }
}
