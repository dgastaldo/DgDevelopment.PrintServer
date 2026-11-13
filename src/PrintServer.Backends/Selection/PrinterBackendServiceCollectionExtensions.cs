using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintServer.Backends.Fake;
using PrintServer.Core.Printing;

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
            default:
                throw new NotSupportedException($"The {kind} printer backend is not implemented yet.");
        }

        return services;
    }
}
