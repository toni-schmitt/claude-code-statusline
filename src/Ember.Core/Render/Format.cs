using System.Globalization;
using System.Text;

namespace Ember.Core.Render;

/// <summary>Duration, countdown, percent and money formatting, §5.6.</summary>
public static class Format
{
    /// <summary>Session clock. &lt;1h -&gt; "24m"; otherwise "1h24m". Days never appear.</summary>
    public static string Duration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        int hours = (int)elapsed.TotalHours;
        int minutes = elapsed.Minutes;
        return hours == 0 ? $"{minutes}m" : $"{hours}h{minutes:D2}m";
    }

    /// <summary>
    /// Countdown to a reset. "0h47m", "2h11m" under a day; "4d6h" (minutes
    /// dropped) at a day or more. Never negative -- the caller hides the
    /// segment instead when <paramref name="remaining"/> is non-positive.
    /// </summary>
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return string.Empty;
        if (remaining.TotalDays >= 1)
        {
            int days = (int)remaining.TotalDays;
            int hours = remaining.Hours;
            return $"{days}d{hours}h";
        }
        int h = remaining.Hours;
        int m = remaining.Minutes;
        return $"{h}h{m:D2}m";
    }

    /// <summary>Integer percent, no decimal, half-up rounded.</summary>
    public static string Percent(double pct)
    {
        int rounded = (int)Math.Floor(pct + 0.5 + 1e-9);
        return $"{rounded}%";
    }

    private static readonly Dictionary<string, string> CurrencyPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = "$",
        ["EUR"] = "€",
        ["GBP"] = "£",
        ["JPY"] = "¥",
        ["BRL"] = "R$",
        ["CAD"] = "CA$",
        ["AUD"] = "A$",
        ["NZD"] = "NZ$",
        ["SGD"] = "S$",
    };

    /// <summary>
    /// ISO currency code to display symbol, matching Claude Code's own
    /// formatter, §4.2. Under <see cref="IconSet.Ascii"/> a symbol that isn't
    /// ASCII (&#8364;, &#163;, &#165;) falls back to the ISO code, the same
    /// shape an unrecognised code already gets -- a set chosen because the
    /// terminal can't render anything better shouldn't be handed a glyph it
    /// can't draw.
    /// </summary>
    public static string CurrencyPrefix(string isoCode, IconSet icons = IconSet.Nerd)
    {
        if (CurrencyPrefixes.TryGetValue(isoCode, out var symbol)
            && (icons != IconSet.Ascii || Ascii.IsValid(symbol)))
        {
            return symbol;
        }
        return $"{isoCode.ToUpperInvariant()} ";
    }

    /// <summary>Symbol prefix, estimates prefixed "&#8776;" ("~" under <see cref="IconSet.Ascii"/>). Zero-decimal currencies (JPY/KRW/VND) drop the fractional part.</summary>
    public static string Money(double amount, string currencyIsoCode, bool estimate, IconSet icons = IconSet.Nerd)
    {
        var prefix = estimate ? Estimate(icons) : "";
        var format = IsZeroDecimalCurrency(currencyIsoCode) ? "F0" : "F2";
        return $"{prefix}{CurrencyPrefix(currencyIsoCode, icons)}{amount.ToString(format, CultureInfo.InvariantCulture)}";
    }

    /// <summary>The "this is our own estimate, not a billed figure" marker, §5.6.</summary>
    public static string Estimate(IconSet icons) => icons == IconSet.Ascii ? "~" : "≈";

    /// <summary>Separates the session figure from the daily one in the spend slot.</summary>
    public static string Dot(IconSet icons) => icons == IconSet.Ascii ? "|" : "·";

    /// <summary>Zero-decimal currencies, §4.2: minor-unit figures are not divided by 100.</summary>
    public static bool IsZeroDecimalCurrency(string isoCode) =>
        isoCode.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
        isoCode.Equals("KRW", StringComparison.OrdinalIgnoreCase) ||
        isoCode.Equals("VND", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts a minor-unit integer (e.g. cents) to its major-unit value, §4.2.</summary>
    public static double MinorUnitsToMajor(long minorUnits, string isoCode) =>
        IsZeroDecimalCurrency(isoCode) ? minorUnits : minorUnits / 100.0;

    /// <summary>Compact token count, shared by Line 1's ctx segment (§2.1) and subagent rows (§11.3): "36.0k", "175.1k", "0". Below 1000, the plain integer rather than "0.4k".</summary>
    public static string TokenCount(long count) =>
        count < 1000
            ? count.ToString(CultureInfo.InvariantCulture)
            : $"{(count / 1000.0).ToString("F1", CultureInfo.InvariantCulture)}k";
}
