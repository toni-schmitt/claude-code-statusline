namespace Ember.Core.Render;

/// <summary>
/// Six-stop ember gradient across a name, §5.3. Interpolated rather than
/// divided into fixed-width segments so the first character always lands on
/// stop 0 and the last always lands on stop 5, whatever the name's length.
/// </summary>
public static class Gradient
{
    public static readonly int[] Ramp =
    [
        Palette.GradientStop1,
        Palette.GradientStop2,
        Palette.GradientStop3,
        Palette.GradientStop4,
        Palette.GradientStop5,
        Palette.GradientStop6,
    ];

    /// <summary>One colour per character of <paramref name="text"/>.</summary>
    public static int[] ForText(string text)
    {
        int len = text.Length;
        var colors = new int[len];
        if (len == 0) return colors;
        if (len == 1)
        {
            colors[0] = Ramp[0];
            return colors;
        }

        int denom = len - 1;
        for (int i = 0; i < len; i++)
        {
            // round-half-up of (i * 5 / denom), done in integers to avoid
            // floating-point edge cases at exact .5 boundaries (as in §5.4).
            int rampIndex = (2 * i * 5 + denom) / (2 * denom);
            colors[i] = Ramp[rampIndex];
        }
        return colors;
    }
}
