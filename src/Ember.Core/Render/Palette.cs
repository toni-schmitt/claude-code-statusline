namespace Ember.Core.Render;

/// <summary>
/// ANSI-256 palette, §5.1. Colour carries severity; grey 245 carries every
/// label, unit and non-status icon. Straight indices so the line renders
/// identically on terminals without truecolour.
/// </summary>
public static class Palette
{
    public const int GradientStop1 = 230;
    public const int GradientStop2 = 223;
    public const int GradientStop3 = 216;
    public const int GradientStop4 = 209;
    public const int GradientStop5 = 202; // session share
    public const int GradientStop6 = 166; // effort

    public const int Separator = 175;
    public const int Label = 245;
    public const int Branch = 150;
    public const int Notice = 222;
    public const int Warn = 180;
    public const int Critical = 203;
    public const int UnfilledBar = 240;

    public const int SessionShare = GradientStop5;

    /// <summary>Severity thresholds, §5.5. Never expressed as a word — only colour.</summary>
    public static (int Color, bool Bold) Severity(double usedPercentage) => usedPercentage switch
    {
        < 60 => (GradientStop2, false),
        < 80 => (Notice, false),
        < 95 => (Warn, false),
        _ => (Critical, true),
    };

    public static string Fg(int color) => $"\x1b[38;5;{color}m";
    public const string BoldOn = "\x1b[1m";
    public const string BoldOff = "\x1b[22m";
    public const string ReverseOn = "\x1b[7m";
    public const string ReverseOff = "\x1b[27m";
    public const string Reset = "\x1b[0m";
}
