namespace Ember.Core.Data;

/// <summary>
/// Best-effort model-ID -&gt; display-name mapping for subagent rows (§11.1
/// hands the row a resolved model ID, not a display name). No ID table is
/// published anywhere in the spec; this parses the observed
/// <c>claude-&lt;family&gt;-&lt;version segments&gt;-&lt;date stamp&gt;</c> shape and
/// falls back to the raw id verbatim when it doesn't recognise the shape,
/// rather than guessing wrong. Never throws.
/// </summary>
public static class ModelNames
{
    public static string Resolve(string modelId)
    {
        var parts = modelId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        string? family = null;
        var version = new List<string>();

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "opus" or "sonnet" or "haiku")
            {
                family = char.ToUpperInvariant(lower[0]) + lower[1..];
                continue;
            }
            if (part.Length == 8 && part.All(char.IsAsciiDigit)) continue; // trailing yyyymmdd stamp
            if (part.Equals("claude", StringComparison.OrdinalIgnoreCase)) continue;
            if (part.All(char.IsAsciiDigit)) version.Add(part);
        }

        if (family is null) return modelId; // unrecognised shape: show the raw id
        return version.Count == 0 ? family : $"{family} {string.Join('.', version)}";
    }
}
