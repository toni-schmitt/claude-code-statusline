using System.Globalization;

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

    /// <summary>ISO currency code to display symbol, matching Claude Code's own formatter, §4.2.</summary>
    public static string CurrencyPrefix(string isoCode) =>
        CurrencyPrefixes.TryGetValue(isoCode, out var symbol) ? symbol : $"{isoCode.ToUpperInvariant()} ";

    /// <summary>Two decimals, symbol prefix, estimates prefixed "&#8776;".</summary>
    public static string Money(double amount, string currencyIsoCode, bool estimate)
    {
        var prefix = estimate ? "≈" : "";
        return $"{prefix}{CurrencyPrefix(currencyIsoCode)}{amount.ToString("F2", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Zero-decimal currencies, §4.2: minor-unit figures are not divided by 100.</summary>
    public static bool IsZeroDecimalCurrency(string isoCode) =>
        isoCode.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
        isoCode.Equals("KRW", StringComparison.OrdinalIgnoreCase) ||
        isoCode.Equals("VND", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts a minor-unit integer (e.g. cents) to its major-unit value, §4.2.</summary>
    public static double MinorUnitsToMajor(long minorUnits, string isoCode) =>
        IsZeroDecimalCurrency(isoCode) ? minorUnits : minorUnits / 100.0;
}
