using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using PrintServer.WebUi;

namespace PrintServer.UnitTests.WebUi;

internal static class TestText
{
    public static UiText Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = Strings.ResourcesPath);
        services.AddScoped<UiText>();
        return services.BuildServiceProvider().GetRequiredService<UiText>();
    }
}

/// <summary>Switches the culture of the current test and restores it afterwards.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        var culture = new CultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
