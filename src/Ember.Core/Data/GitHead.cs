namespace Ember.Core.Data;

/// <summary>Reads the current branch straight from <c>.git/HEAD</c>, §12.4. No <c>git</c> subprocess.</summary>
public static class GitHead
{
    private const string BranchRefPrefix = "ref: refs/heads/";
    private const string GitDirPrefix = "gitdir: ";

    /// <summary>
    /// Returns the branch name, the first 7 characters of a detached SHA, or
    /// null when no <c>.git</c> is found walking up from <paramref name="startDir"/>.
    /// </summary>
    public static string? FindBranch(string? startDir)
    {
        if (string.IsNullOrEmpty(startDir)) return null;

        var gitDir = FindGitDir(startDir);
        if (gitDir is null) return null;

        var headPath = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headPath)) return null;

        string content;
        try
        {
            content = File.ReadAllText(headPath).Trim();
        }
        catch
        {
            return null;
        }

        if (content.StartsWith(BranchRefPrefix, StringComparison.Ordinal))
            return content[BranchRefPrefix.Length..];

        if (IsHex40(content))
            return content[..7]; // detached HEAD

        return null;
    }

    private static string? FindGitDir(string startDir)
    {
        string? dir;
        try
        {
            dir = Path.GetFullPath(startDir);
        }
        catch
        {
            return null;
        }

        while (dir is not null)
        {
            var gitPath = Path.Combine(dir, ".git");

            if (Directory.Exists(gitPath))
                return gitPath;

            if (File.Exists(gitPath))
                return ResolveGitDirFile(dir, gitPath);

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>A worktree or submodule points at its real gitdir via a <c>gitdir: &lt;path&gt;</c> file.</summary>
    private static string? ResolveGitDirFile(string containingDir, string gitFilePath)
    {
        string content;
        try
        {
            content = File.ReadAllText(gitFilePath).Trim();
        }
        catch
        {
            return null;
        }

        if (!content.StartsWith(GitDirPrefix, StringComparison.Ordinal)) return null;

        var target = content[GitDirPrefix.Length..].Trim();
        return Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(containingDir, target));
    }

    private static bool IsHex40(string s)
    {
        if (s.Length != 40) return false;
        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        return true;
    }
}
