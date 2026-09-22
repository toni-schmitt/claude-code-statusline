using System.Text;

namespace Ember.Core.Render;

/// <summary>
/// Appends ANSI-coloured text while tracking visible width. Escape codes
/// are free; width accounts for East Asian double-width characters so
/// project/branch names with CJK glyphs measure correctly.
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
        Width += DisplayWidth(text);
        return this;
    }

    /// <summary>
    /// One colour per character (§5.3's gradient). <paramref name="colors"/>
    /// must have one entry per UTF-16 code unit of <paramref name="text"/>. A
    /// surrogate pair is written as one unit in its high surrogate's colour:
    /// an escape between the two halves would leave two unpaired surrogates,
    /// which the UTF-8 or JSON encoder on the way out replaces with U+FFFD.
    /// </summary>
    public AnsiBuilder Gradient(string text, int[] colors)
    {
        for (int i = 0; i < text.Length; i++)
        {
            _sb.Append(Palette.Fg(colors[i])).Append(text[i]);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                _sb.Append(text[++i]);
            }
        }
        Width += DisplayWidth(text);
        return this;
    }

    /// <summary>
    /// Appends everything <paramref name="other"/> has composed so far, escapes
    /// included, so a segment can be composed and measured on its own before
    /// the text ahead of it is fitted to the width it leaves over.
    /// <paramref name="other"/> must not have been built: <see cref="Build"/>
    /// appends the reset that belongs at the very end of a line.
    /// </summary>
    public AnsiBuilder Append(AnsiBuilder other)
    {
        _sb.Append(other._sb);
        Width += other.Width;
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
            Width += 1; // bar glyphs are always single-width
        }
        return this;
    }

    /// <summary>Reverse-video banner body: one pad space inside the reversed region at each end (§5.1, §6.2).</summary>
    public AnsiBuilder ReverseBanner(string text, int color)
    {
        _sb.Append(Palette.Fg(color)).Append(Palette.ReverseOn)
           .Append(' ').Append(text).Append(' ')
           .Append(Palette.ReverseOff);
        Width += DisplayWidth(text) + 2;
        return this;
    }

    /// <summary>Uncoloured connective text (e.g. the space between a banner and what follows it).</summary>
    public AnsiBuilder Raw(string text)
    {
        _sb.Append(text);
        Width += DisplayWidth(text);
        return this;
    }

    /// <summary>Finalises the line with a single reset at the very end (§5.1).</summary>
    public string Build() => _sb.Append(Palette.Reset).ToString();

    /// <summary>Terminal display width: 2 for East Asian wide characters, 1 for everything else.</summary>
    public static int DisplayWidth(string text)
    {
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                width += IsWide(char.ConvertToUtf32(c, text[i + 1])) ? 2 : 1;
                i++;
            }
            else
            {
                width += IsWide(c) ? 2 : 1;
            }
        }
        return width;
    }

    /// <summary>
    /// Returns the longest prefix of <paramref name="text"/> whose display
    /// width fits within <paramref name="maxWidth"/> columns, never splitting
    /// a surrogate pair.
    /// </summary>
    public static int TruncateToWidth(string text, int maxWidth)
    {
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            int cw;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cw = IsWide(char.ConvertToUtf32(c, text[i + 1])) ? 2 : 1;
                if (width + cw > maxWidth) return i;
                width += cw;
                i++;
            }
            else
            {
                cw = IsWide(c) ? 2 : 1;
                if (width + cw > maxWidth) return i;
                width += cw;
            }
        }
        return text.Length;
    }

    private static bool IsWide(int codePoint) => codePoint is
        (>= 0x1100 and <= 0x115F) or    // Hangul Jamo
        (>= 0x2E80 and <= 0x303E) or    // CJK Radicals through CJK Symbols
        (>= 0x3041 and <= 0x33BF) or    // Hiragana through CJK Compatibility
        (>= 0x3400 and <= 0x4DBF) or    // CJK Unified Ideographs Extension A
        (>= 0x4E00 and <= 0x9FFF) or    // CJK Unified Ideographs
        (>= 0xA000 and <= 0xA4CF) or    // Yi
        (>= 0xAC00 and <= 0xD7AF) or    // Hangul Syllables
        (>= 0xF900 and <= 0xFAFF) or    // CJK Compatibility Ideographs
        (>= 0xFE30 and <= 0xFE6F) or    // CJK Compatibility Forms
        (>= 0xFF01 and <= 0xFF60) or    // Fullwidth Forms
        (>= 0xFFE0 and <= 0xFFE6) or    // Fullwidth Signs
        (>= 0x20000 and <= 0x2FA1F);   // CJK Supplementary
}
