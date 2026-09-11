using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

/// <summary>§5.5's severity thresholds -- boundaries matter, so every edge is tested explicitly.</summary>
public class PaletteTests
{
    [Theory]
    [InlineData(0, 223, false)]
    [InlineData(59.9, 223, false)]
    [InlineData(60, 222, false)]
    [InlineData(79.9, 222, false)]
    [InlineData(80, 180, false)]
    [InlineData(94.9, 180, false)]
    [InlineData(95, 203, true)]
    [InlineData(100, 203, true)]
    [InlineData(150, 203, true)] // above 100 stays critical
    public void SeverityThresholds(double pct, int expectedColor, bool expectedBold)
    {
        var (color, bold) = Palette.Severity(pct);
        Assert.Equal(expectedColor, color);
        Assert.Equal(expectedBold, bold);
    }

    [Fact]
    public void SessionShareIsGradientStopFive()
    {
        Assert.Equal(Palette.GradientStop5, Palette.SessionShare);
    }
}
