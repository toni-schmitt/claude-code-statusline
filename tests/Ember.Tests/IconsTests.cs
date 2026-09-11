using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

/// <summary>
/// Byte-exact codepoint checks, §5.2. This is a regression test for a real
/// mistake made while authoring Icons.cs: a Write of the Nerd Font glyphs
/// silently produced empty strings for every PUA codepoint, and it wasn't
/// caught until a hexdump. These assertions pin every codepoint by its
/// integer value so a future edit can't reintroduce that silently.
/// </summary>
public class IconsTests
{
    [Fact]
    public void NerdGlyphsMatchPrivateUseAreaCodepoints()
    {
        var icons = Icons.For(IconSet.Nerd);
        Assert.Equal('', Single(icons.Model));
        Assert.Equal('', Single(icons.Effort));
        Assert.Equal('', Single(icons.Project));
        Assert.Equal('', Single(icons.Branch));
        Assert.Equal('', Single(icons.Clock));
        Assert.Equal('', Single(icons.Share));
        Assert.Equal('', Single(icons.Spend));
        Assert.Equal('', Single(icons.Credits));
        Assert.Equal('', Single(icons.Blocked));
        Assert.Equal('', Single(icons.Reset));
        Assert.Equal('', Single(icons.Done));
    }

    [Fact]
    public void SeparatorIsHeavyRightPointingAngleQuoteInNerdAndUnicodeSets()
    {
        Assert.Equal('❯', Single(Icons.For(IconSet.Nerd).Separator));
        Assert.Equal('❯', Single(Icons.For(IconSet.Unicode).Separator));
    }

    [Fact]
    public void BarGlyphsAreFullHalfEmptyBlocks()
    {
        foreach (var set in new[] { IconSet.Nerd, IconSet.Unicode })
        {
            var bar = Icons.For(set).Bar;
            Assert.Equal('█', bar.Full);  // █
            Assert.Equal('▌', bar.Half);  // ▌ -- LEFT half block, not U+2590 RIGHT half block
            Assert.Equal('░', bar.Empty); // ░
        }
    }

    [Fact]
    public void UnicodeGlyphsAreMenloSafe()
    {
        var icons = Icons.For(IconSet.Unicode);
        Assert.Equal('◆', Single(icons.Model));  // ◆
        Assert.Equal('▲', Single(icons.Effort)); // ▲
        Assert.Equal('▸', Single(icons.Project)); // ▸
        Assert.Equal('┣', Single(icons.Branch)); // ┣
        Assert.Equal('◷', Single(icons.Clock));  // ◷
        Assert.Equal('⊕', Single(icons.Share));  // ⊕
        Assert.Equal('¤', Single(icons.Spend));  // ¤
        Assert.Equal('⚡', Single(icons.Credits)); // ⚡
        Assert.Equal('⊘', Single(icons.Blocked)); // ⊘
        Assert.Equal('↺', Single(icons.Reset));  // ↺ -- NOT in JetBrainsMono, hence a separate nerd codepoint
        Assert.Equal('✔', Single(icons.Done));   // ✔
    }

    [Fact]
    public void AsciiSetIsPureAscii()
    {
        var icons = Icons.For(IconSet.Ascii);
        foreach (var glyph in new[]
                 {
                     icons.Model, icons.Effort, icons.Project, icons.Branch, icons.Clock, icons.Share,
                     icons.Spend, icons.Credits, icons.Blocked, icons.Reset, icons.Done, icons.Separator,
                 })
        {
            Assert.All(glyph, c => Assert.True(c < 128, $"'{c}' (U+{(int)c:X4}) is not ASCII"));
        }
        Assert.True(icons.Bar.Full < 128 && icons.Bar.Half < 128 && icons.Bar.Empty < 128);
    }

    [Fact]
    public void EveryGlyphIsExactlyOneUtf16CodeUnit()
    {
        // AnsiBuilder's width accounting (§13) assumes every icon costs exactly one column.
        foreach (var set in Enum.GetValues<IconSet>())
        {
            var icons = Icons.For(set);
            foreach (var glyph in new[]
                     {
                         icons.Model, icons.Effort, icons.Project, icons.Branch, icons.Clock, icons.Share,
                         icons.Spend, icons.Credits, icons.Blocked, icons.Reset, icons.Done, icons.Separator,
                     })
            {
                Assert.Equal(1, glyph.Length);
            }
        }
    }

    private static char Single(string s)
    {
        Assert.Equal(1, s.Length);
        return s[0];
    }
}
