using Ember.Core;
using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

/// <summary>§15.1: flag parsing. Unknown flags are ignored, never fatal.</summary>
public class OptionsTests
{
    [Fact]
    public void DefaultsToNerdIconsAndRenderMode()
    {
        var options = Options.Parse([]);
        Assert.Equal(IconSet.Nerd, options.Icons);
        Assert.Equal(EmberMode.Render, options.Mode);
    }

    [Theory]
    [InlineData("--icons=nerd", IconSet.Nerd)]
    [InlineData("--icons=unicode", IconSet.Unicode)]
    [InlineData("--icons=ascii", IconSet.Ascii)]
    public void ParsesIconSet(string flag, IconSet expected)
    {
        Assert.Equal(expected, Options.Parse([flag]).Icons);
    }

    [Fact]
    public void UnrecognisedIconValueKeepsDefault()
    {
        Assert.Equal(IconSet.Nerd, Options.Parse(["--icons=nonsense"]).Icons);
    }

    [Fact]
    public void RefreshFlagSetsRefreshMode()
    {
        Assert.Equal(EmberMode.Refresh, Options.Parse(["--refresh"]).Mode);
    }

    [Fact]
    public void InstallFlagSetsInstallMode()
    {
        Assert.Equal(EmberMode.Install, Options.Parse(["--install"]).Mode);
    }

    [Fact]
    public void UnknownFlagsAreIgnoredNotFatal()
    {
        var options = Options.Parse(["--bogus-flag", "--another=thing", "not-a-flag-at-all"]);
        Assert.Equal(IconSet.Nerd, options.Icons);
        Assert.Equal(EmberMode.Render, options.Mode);
    }

    [Fact]
    public void FlagsCombineFreely()
    {
        var options = Options.Parse(["--icons=ascii", "--refresh", "--unknown"]);
        Assert.Equal(IconSet.Ascii, options.Icons);
        Assert.Equal(EmberMode.Refresh, options.Mode);
    }
}
