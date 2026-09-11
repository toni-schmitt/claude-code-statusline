using Ember.Core.Render;

namespace Ember.Core;

public enum EmberMode
{
    Render,
    Refresh,
    Install,
}

/// <summary>Flags on the command in settings.json, §15.1. Unknown flags are ignored, not fatal.</summary>
public sealed record Options(IconSet Icons, bool GitDirty, bool WeeklyPerModel, EmberMode Mode)
{
    public static Options Parse(string[] args)
    {
        var icons = IconSet.Nerd;
        var gitDirty = false;
        var weeklyPerModel = false;
        var mode = EmberMode.Render;

        foreach (var arg in args)
        {
            if (arg == "--refresh") { mode = EmberMode.Refresh; continue; }
            if (arg == "--install") { mode = EmberMode.Install; continue; }
            if (arg == "--git-dirty") { gitDirty = true; continue; } // reserved (§14.1); no behaviour wired up yet
            if (arg == "--weekly-per-model") { weeklyPerModel = true; continue; } // reserved (§14.2); ditto

            if (arg.StartsWith("--icons=", StringComparison.Ordinal))
            {
                icons = arg["--icons=".Length..].ToLowerInvariant() switch
                {
                    "nerd" => IconSet.Nerd,
                    "unicode" => IconSet.Unicode,
                    "ascii" => IconSet.Ascii,
                    _ => icons, // unrecognised value: keep the default rather than fail (§15.1)
                };
                continue;
            }

            // anything else: ignored, not fatal (§15.1)
        }

        return new Options(icons, gitDirty, weeklyPerModel, mode);
    }
}
