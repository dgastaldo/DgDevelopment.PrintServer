using System.Text.RegularExpressions;

namespace PrintServer.UnitTests;

/// <summary>Finds the placeholders (<c>%s</c> in shell messages, <c>{0}</c> in resources and PowerShell) of a message.</summary>
internal static partial class Placeholders
{
    [GeneratedRegex(@"%s|\{\d+\}")]
    private static partial Regex Pattern();

    public static string Of(string text) =>
        string.Join(",", Pattern().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal));
}
