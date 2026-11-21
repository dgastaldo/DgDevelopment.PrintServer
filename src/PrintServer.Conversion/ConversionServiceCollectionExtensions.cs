using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintServer.Backends.Common;
using PrintServer.Core.Printing;

namespace PrintServer.Conversion;

public static class ConversionServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentConversion(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ConversionOptions>()
            .Bind(configuration.GetSection(ConversionOptions.SectionName))
            .Validate(options => options.Timeout > TimeSpan.Zero, "Conversion options are invalid: Timeout must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IDocumentConverter>(provider => new DocumentConverter(
        [
            new ImageToPdfConverter(),
            ActivatorUtilities.CreateInstance<LibreOfficeConverter>(provider),
        ]));

        return services;
    }
}
