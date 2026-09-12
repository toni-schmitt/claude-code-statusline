using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0m")]
    [InlineData(24 * 60_000, "24m")]
    [InlineData(59 * 60_000, "59m")]
    [InlineData(60 * 60_000, "1h00m")]
    [InlineData(84 * 60_000, "1h24m")] // 1h24m, §2's worked example
    [InlineData(26 * 60 * 60_000, "26h00m")] // days never appear, however old the session (§5.6)
    public void DurationFormatsAsSpec(long ms, string expected)
    {
        Assert.Equal(expected, Format.Duration(TimeSpan.FromMilliseconds(ms)));
    }

    [Fact]
    public void DurationClampsNegativeToZero()
    {
        Assert.Equal("0m", Format.Duration(TimeSpan.FromMilliseconds(-1000)));
    }

    [Theory]
    [InlineData(47 * 60, "0h47m")]
    [InlineData((2 * 60 + 11) * 60, "2h11m")]
    [InlineData(4 * 86400 + 6 * 3600, "4d6h")] // days drop minutes entirely (§5.6)
    public void CountdownFormatsAsSpec(long seconds, string expected)
    {
        Assert.Equal(expected, Format.Countdown(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void CountdownHidesNonPositiveRemaining(long seconds)
    {
        Assert.Equal("", Format.Countdown(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(12.0, "12%")]
    [InlineData(12.4, "12%")]
    [InlineData(12.5, "13%")] // half-up, not banker's rounding (§5.4's rule applies to percent too)
    [InlineData(99.5, "100%")]
    [InlineData(0, "0%")]
    public void PercentRoundsHalfUp(double pct, string expected)
    {
        Assert.Equal(expected, Format.Percent(pct));
    }

    [Theory]
    [InlineData("USD", "$")]
    [InlineData("EUR", "€")]
    [InlineData("GBP", "£")]
    [InlineData("JPY", "¥")]
    [InlineData("BRL", "R$")]
    [InlineData("CAD", "CA$")]
    [InlineData("AUD", "A$")]
    [InlineData("NZD", "NZ$")]
    [InlineData("SGD", "S$")]
    public void CurrencyPrefixMatchesTable(string iso, string expected)
    {
        Assert.Equal(expected, Format.CurrencyPrefix(iso));
    }

    [Fact]
    public void UnknownCurrencyFallsBackToUppercaseCodeAndSpace()
    {
        Assert.Equal("CHF ", Format.CurrencyPrefix("chf"));
    }

    [Fact]
    public void MoneyPrefixesEstimatesWithApproxSign()
    {
        Assert.Equal("≈$0.82", Format.Money(0.82, "USD", estimate: true));
        Assert.Equal("$0.82", Format.Money(0.82, "USD", estimate: false));
    }

    [Fact]
    public void MoneyUsesTwoDecimalsForNormalCurrencies()
    {
        Assert.Equal("$1.00", Format.Money(1, "USD", estimate: false));
        Assert.Equal("CHF 12.40", Format.Money(12.4, "CHF", estimate: false));
    }

    [Fact]
    public void MoneyDropsDecimalsForZeroDecimalCurrencies()
    {
        Assert.Equal("¥1234", Format.Money(1234, "JPY", estimate: false));
        Assert.Equal("KRW 5000", Format.Money(5000, "KRW", estimate: false));
        Assert.Equal("≈¥100", Format.Money(100, "JPY", estimate: true));
    }

    [Fact]
    public void AsciiIconSetUsesTildeForEstimates()
    {
        Assert.Equal("~$0.82", Format.Money(0.82, "USD", estimate: true, IconSet.Ascii));
        Assert.Equal("$0.82", Format.Money(0.82, "USD", estimate: false, IconSet.Ascii));
        Assert.Equal("≈$0.82", Format.Money(0.82, "USD", estimate: true, IconSet.Unicode));
    }

    [Theory]
    [InlineData("USD", "$")]       // already ASCII: kept
    [InlineData("BRL", "R$")]      // ditto
    [InlineData("CAD", "CA$")]
    [InlineData("EUR", "EUR ")]    // € is not ASCII: falls back to the code
    [InlineData("GBP", "GBP ")]
    [InlineData("JPY", "JPY ")]
    public void AsciiIconSetFallsBackToCodeForNonAsciiCurrencySymbols(string iso, string expected)
    {
        Assert.Equal(expected, Format.CurrencyPrefix(iso, IconSet.Ascii));
    }

    [Fact]
    public void AsciiMoneyIsEntirelyAscii()
    {
        foreach (var iso in new[] { "USD", "EUR", "GBP", "JPY", "BRL", "CHF" })
        {
            var text = Format.Money(12.4, iso, estimate: true, IconSet.Ascii);
            Assert.True(System.Text.Ascii.IsValid(text), $"{iso} rendered non-ASCII: {text}");
        }
    }

    [Fact]
    public void EstimateAndDotFallBackOnlyForAscii()
    {
        Assert.Equal("~", Format.Estimate(IconSet.Ascii));
        Assert.Equal("≈", Format.Estimate(IconSet.Nerd));
        Assert.Equal("≈", Format.Estimate(IconSet.Unicode));
        Assert.Equal("|", Format.Dot(IconSet.Ascii));
        Assert.Equal("·", Format.Dot(IconSet.Nerd));
        Assert.Equal("·", Format.Dot(IconSet.Unicode));
    }

    [Theory]
    [InlineData("JPY", true)]
    [InlineData("KRW", true)]
    [InlineData("VND", true)]
    [InlineData("USD", false)]
    [InlineData("EUR", false)]
    public void ZeroDecimalCurrenciesMatchSpec(string iso, bool expected)
    {
        Assert.Equal(expected, Format.IsZeroDecimalCurrency(iso));
    }

    [Fact]
    public void MinorUnitsDividedByHundredExceptZeroDecimal()
    {
        Assert.Equal(12.34, Format.MinorUnitsToMajor(1234, "USD"));
        Assert.Equal(1234, Format.MinorUnitsToMajor(1234, "JPY"));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(36_000, "36k")]
    [InlineData(122_000, "122k")]
    [InlineData(1_499, "1k")]
    public void TokenCountIsCompact(long count, string expected)
    {
        Assert.Equal(expected, Format.TokenCount(count));
    }
}
