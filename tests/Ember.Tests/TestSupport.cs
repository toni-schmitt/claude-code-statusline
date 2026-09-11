using System.Text.RegularExpressions;

namespace Ember.Tests;

/// <summary>Shared helpers for asserting on rendered ANSI output without hardcoding escape sequences everywhere.</summary>
internal static partial class TestSupport
{
    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex AnsiRegex();

    public static string StripAnsi(string s) => AnsiRegex().Replace(s, "");

    public static int VisibleWidth(string s) => StripAnsi(s).Length;
}
