using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintServer.Backends.Common;
using PrintServer.Backends.Cups;
using PrintServer.Backends.Fake;
using PrintServer.Backends.Ipp;
using Microsoft.Extensions.Options;
using PrintServer.Backends.Windows;
using PrintServer.Core.Printing;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
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
            case PrinterBackendKind.Ipp:
                services.Configure<IppOptions>(section.GetSection("Ipp"));
                services.AddSingleton<IPrinterBackend>(provider =>
                {
                    var ippOptions = provider.GetRequiredService<IOptions<IppOptions>>();
                    return new IppBackend(CreateIppHttpClient(ippOptions.Value), ippOptions, disposeHttpClient: true);
                });
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

    [SuppressMessage("Reliability", "CA2000", Justification = "The returned HttpClient owns and disposes the handler.")]
    private static HttpClient CreateIppHttpClient(IppOptions options)
    {
        var ssl = new SslClientAuthenticationOptions();
        var pins = options.TrustedCertificateThumbprints.ToArray();
        if (pins.Length > 0)
        {
            ssl.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                IppCertificateValidator.IsTrusted(certificate, errors, pins);
        }

        return new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), SslOptions = ssl })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
