using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PrintServer.UnitTests;

public sealed partial class DeployScriptMessagesTests
{
    private static readonly string[] ShellScripts = ["deploy/service/install-service.sh", "deploy/personal/update.sh"];
    private static readonly string[] PowerShellScripts = ["deploy/service/install-service.ps1", "deploy/personal/update.ps1"];

    [GeneratedRegex(@"^\s*(?<lang>it|\*):(?<key>[a-z_]+)\) echo '(?<text>.*)' ;;\r?$", RegexOptions.Multiline)]
    private static partial Regex ShellMessage();

    [GeneratedRegex(@"^\s{8}(?<key>[A-Za-z]+)\s*= '(?<text>.*)'\s*$", RegexOptions.Multiline)]
    private static partial Regex PowerShellMessage();

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintServer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root(), relative));

    [Theory]
    [InlineData("deploy/service/install-service.sh")]
    [InlineData("deploy/personal/update.sh")]
    public void EveryShellMessageExistsInEnglishAndItalianWithTheSamePlaceholders(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var matches = ShellMessage().Matches(Read(script));

        var english = matches.Where(m => m.Groups["lang"].Value == "*").ToDictionary(m => m.Groups["key"].Value, m => m.Groups["text"].Value);
        var italian = matches.Where(m => m.Groups["lang"].Value == "it").ToDictionary(m => m.Groups["key"].Value, m => m.Groups["text"].Value);

        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), italian.Keys.Order());
        Assert.All(english, pair => Assert.Equal(Placeholders(pair.Value), Placeholders(italian[pair.Key])));
        Assert.DoesNotContain(english, pair => pair.Value == italian[pair.Key] && pair.Value.Length > 12);
    }

    [Theory]
    [InlineData("deploy/service/install-service.ps1")]
    [InlineData("deploy/personal/update.ps1")]
    public void EveryPowerShellMessageExistsInEnglishAndItalianWithTheSamePlaceholders(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var text = Read(script);
        var italianStart = text.IndexOf("    it = @{", StringComparison.Ordinal);
        var englishStart = text.IndexOf("    en = @{", StringComparison.Ordinal);
        Assert.True(englishStart >= 0 && italianStart > englishStart);

        var english = PowerShellMessage().Matches(text[englishStart..italianStart]).ToDictionary(m => m.Groups["key"].Value, m => m.Groups["text"].Value);
        var italian = PowerShellMessage().Matches(text[italianStart..]).ToDictionary(m => m.Groups["key"].Value, m => m.Groups["text"].Value);

        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), italian.Keys.Order());
        Assert.All(english, pair => Assert.Equal(Placeholders(pair.Value), Placeholders(italian[pair.Key])));
    }

    [Fact]
    public void TheScriptListsAreComplete()
    {
        Assert.All([.. ShellScripts, .. PowerShellScripts], script => Assert.True(File.Exists(Path.Combine(Root(), script)), script));
    }

    [Theory]
    [InlineData("--lang en", "", "usage: install-service.sh")]
    [InlineData("--lang it", "", "uso: install-service.sh")]
    [InlineData("--lang=it", "en_US.UTF-8", "uso: install-service.sh")]
    [InlineData("", "it_IT.UTF-8", "uso: install-service.sh")]
    [InlineData("", "en_GB.UTF-8", "usage: install-service.sh")]
    [InlineData("", "de_DE.UTF-8", "usage: install-service.sh")]
    [InlineData("", "", "usage: install-service.sh")]
    public async Task TheInstallerSpeaksTheRequestedLanguage(string arguments, string locale, string expected)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(locale);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var (code, output) = await RunAsync("deploy/service/install-service.sh", $"{arguments} --help".Trim(), locale);

        Assert.Equal(0, code);
        Assert.StartsWith(expected, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePrintserverLanguageVariableWinsOverTheLocale()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var (_, output) = await RunAsync("deploy/service/install-service.sh", "--help", "it_IT.UTF-8", ("PRINTSERVER_LANG", "en"));

        Assert.StartsWith("usage:", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--lang fr", "unsupported language: fr", 2)]
    [InlineData("--lang", "--lang needs a value", 2)]
    [InlineData("--boh", "unknown argument: --boh", 2)]
    [InlineData("--lang it --boh", "argomento sconosciuto: --boh", 2)]
    public async Task TheInstallerRefusesWhatItDoesNotUnderstandInTheRightLanguage(string arguments, string expected, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var (code, output) = await RunAsync("deploy/service/install-service.sh", arguments, string.Empty);

        Assert.Equal(exitCode, code);
        Assert.Contains(expected, output, StringComparison.Ordinal);
    }

    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"%s|\{\d+\}").Select(match => match.Value));

    private static async Task<(int Code, string Output)> RunAsync(string script, string arguments, string locale, params (string Key, string Value)[] variables)
    {
        var start = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(Root(), script));
        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var name in new[] { "LANG", "LC_ALL", "LC_MESSAGES", "PRINTSERVER_LANG" })
        {
            start.Environment.Remove(name);
        }

        if (locale.Length > 0)
        {
            start.Environment["LANG"] = locale;
        }

        foreach (var (key, value) in variables)
        {
            start.Environment[key] = value;
        }

        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        output += await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}
