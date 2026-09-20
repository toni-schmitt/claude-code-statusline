using Ember.Core.Render;

namespace Ember.Core;

public enum EmberMode
{
    Render,
    Refresh,
    Install,
}

/// <summary>
/// Flags on the command in settings.json, §15.1. Unknown flags are ignored,
/// not fatal.
/// <para>
/// Only what a person must decide before the binary runs belongs here. Which
/// segments line 2 carries is not that: §15.5 puts it in the settings block
/// <c>--install</c> writes, where it survives a reinstall and can be revisited
/// without editing a command string by hand.
/// </para>
/// </summary>
public sealed record Options(IconSet Icons, EmberMode Mode)
{
    public static Options Parse(string[] args)
    {
        var icons = IconSet.Nerd;
        var mode = EmberMode.Render;

        foreach (var arg in args)
        {
            if (arg == "--refresh") { mode = EmberMode.Refresh; continue; }
            if (arg == "--install") { mode = EmberMode.Install; continue; }

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

        return new Options(icons, mode);
    }
}
