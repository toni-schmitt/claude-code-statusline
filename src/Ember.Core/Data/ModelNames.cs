namespace Ember.Core.Data;

/// <summary>
/// Best-effort model-ID -&gt; display-name mapping for subagent rows (§11.1
/// hands the row a resolved model ID, not a display name). No ID table is
/// published anywhere in the spec; this parses the observed
/// <c>claude-&lt;family&gt;-&lt;version segments&gt;-&lt;date stamp&gt;</c> shape (with
/// Claude Code's optional bracketed window tag cut off) and
/// falls back to the raw id verbatim when it doesn't recognise the shape,
/// rather than guessing wrong. Never throws.
/// </summary>
public static class ModelNames
{
    public static string Resolve(string modelId)
    {
        // Claude Code appends "[1m]" to the id when the 1M context window is
        // selected (e.g. "claude-opus-4-8[1m]"). It is a window tag, not a
        // version segment, so it is cut off before parsing; left in, "8[1m]"
        // fails the digit check and the row reads "Opus 4". The window itself
        // is not shown: the token count next to the bar already implies it.
        int tag = modelId.IndexOf('[');
        var id = tag < 0 ? modelId : modelId[..tag];

        var parts = id.Split('-', StringSplitOptions.RemoveEmptyEntries);
        string? family = null;
        var version = new List<string>();

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "opus" or "sonnet" or "haiku" or "fable")
            {
                family = char.ToUpperInvariant(lower[0]) + lower[1..];
                continue;
            }
            if (part.Length == 8 && part.All(char.IsAsciiDigit)) continue; // trailing yyyymmdd stamp
            if (part.Equals("claude", StringComparison.OrdinalIgnoreCase)) continue;
            if (part.All(char.IsAsciiDigit)) version.Add(part);
        }

        if (family is null) return id.Length > 0 ? id : modelId; // unrecognised shape: show the id as is, still without the tag
        return version.Count == 0 ? family : $"{family} {string.Join('.', version)}";
    }
}
