using System.Text;

namespace Ember.Core.Render;

/// <summary>
/// Appends ANSI-coloured text while tracking visible width. Escape codes
/// are free; every character is exactly one column (§5.2 -- every glyph in
/// this design's three icon sets is a single UTF-16 code unit, so
/// string.Length is always the correct visible width).
///
/// Every attribute opened here (bold, reverse) is closed at the end of that
/// same call rather than held open (§5.1), so a later degradation tier
/// (§13) can drop a segment without leaking state into whatever replaces
/// it. A single reset closes the whole line in <see cref="Build"/>.
///
/// Append-only by design: composing a candidate line is cheap enough that
/// §13's degradation ladder rebuilds the whole line per tier and remeasures
/// (§13.3), rather than this type supporting incremental "undo a segment."
/// </summary>
public sealed class AnsiBuilder
{
    private readonly StringBuilder _sb = new();
    public int Width { get; private set; }

    /// <summary>Plain foreground colour, optionally bold, closed at the end of this call.</summary>
    public AnsiBuilder Colored(string text, int color, bool bold = false)
    {
        if (text.Length == 0) return this;
        _sb.Append(Palette.Fg(color));
        if (bold) _sb.Append(Palette.BoldOn);
        _sb.Append(text);
        if (bold) _sb.Append(Palette.BoldOff);
        Width += text.Length;
        return this;
    }

    /// <summary>One colour per character (§5.3's gradient). <paramref name="colors"/> must have one entry per character of <paramref name="text"/>.</summary>
    public AnsiBuilder Gradient(string text, int[] colors)
    {
        for (int i = 0; i < text.Length; i++)
        {
            _sb.Append(Palette.Fg(colors[i])).Append(text[i]);
        }
        Width += text.Length;
        return this;
    }

    /// <summary>A run of pre-coloured bar cells (§5.4).</summary>
    public AnsiBuilder Bar(IReadOnlyList<BarCell> cells)
    {
        foreach (var c in cells)
        {
            _sb.Append(Palette.Fg(c.Color));
            if (c.Bold) _sb.Append(Palette.BoldOn);
            _sb.Append(c.Glyph);
            if (c.Bold) _sb.Append(Palette.BoldOff);
            Width += 1;
        }
        return this;
    }

    /// <summary>Reverse-video banner body: one pad space inside the reversed region at each end (§5.1, §6.2).</summary>
    public AnsiBuilder ReverseBanner(string text, int color)
    {
        _sb.Append(Palette.Fg(color)).Append(Palette.ReverseOn)
           .Append(' ').Append(text).Append(' ')
           .Append(Palette.ReverseOff);
        Width += text.Length + 2;
        return this;
    }

    /// <summary>Uncoloured connective text (e.g. the space between a banner and what follows it).</summary>
    public AnsiBuilder Raw(string text)
    {
        _sb.Append(text);
        Width += text.Length;
        return this;
    }

    /// <summary>Finalises the line with a single reset at the very end (§5.1).</summary>
    public string Build() => _sb.Append(Palette.Reset).ToString();
}
