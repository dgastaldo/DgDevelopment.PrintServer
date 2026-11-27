using System.Text.RegularExpressions;

namespace PrintServer.UnitTests;

/// <summary>Keeps the English pages of the docs site in step with the Italian ones.</summary>
public sealed partial class DocsTranslationTests
{
    [GeneratedRegex(@"^#{1,6} ", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\]\((?<target>[^)]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^```", RegexOptions.Multiline)]
    private static partial Regex Fence();

    private static string Docs()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintServer.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "docs");
    }

    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Docs(), "*.md").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryItalianPageHasAnEnglishTwinWithTheSameShape(string page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var italian = File.ReadAllText(Path.Combine(Docs(), page));
        var englishPath = Path.Combine(Docs(), "en", page);
        Assert.True(File.Exists(englishPath), $"docs/en/{page} is missing");
        var english = File.ReadAllText(englishPath);
        var name = Path.GetFileNameWithoutExtension(page);

        Assert.StartsWith($"**Italiano** · [English](en/{name}.html)", italian, StringComparison.Ordinal);
        Assert.StartsWith($"[Italiano](../{name}.html) · **English**", english, StringComparison.Ordinal);
        Assert.Equal(Heading().Count(italian), Heading().Count(english));
        Assert.Equal(Fence().Count(italian), Fence().Count(english));
        Assert.Equal(Targets(italian, withLanguageLink: false), Targets(english, withLanguageLink: false));
    }

    [Fact]
    public void ThereAreNoEnglishPagesWithoutAnItalianOne()
    {
        var italian = Directory.GetFiles(Docs(), "*.md").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);

        Assert.All(Directory.GetFiles(Path.Combine(Docs(), "en"), "*.md"), file => Assert.Contains(Path.GetFileName(file), italian));
    }

    private static string[] Targets(string text, bool withLanguageLink) =>
        [.. Link().Matches(text)
            .Select(match => match.Groups["target"].Value.Split('#')[0])
            .Where(target => withLanguageLink || !(target.StartsWith("en/", StringComparison.Ordinal) || target.StartsWith("../", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)];
}
