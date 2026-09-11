using Ember.Core.Data;
using Xunit;

namespace Ember.Tests;

/// <summary>§12.4: reading the branch straight from .git/HEAD, no git subprocess.</summary>
public class GitHeadTests : IDisposable
{
    private readonly string _root;

    public GitHeadTests()
    {
        _root = Directory.CreateTempSubdirectory("ember-githead-").FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReadsBranchFromRefsHeads()
    {
        var gitDir = Directory.CreateDirectory(Path.Combine(_root, ".git")).FullName;
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");

        Assert.Equal("main", GitHead.FindBranch(_root));
    }

    [Fact]
    public void ReadsFeatureBranchWithSlashesInName()
    {
        var gitDir = Directory.CreateDirectory(Path.Combine(_root, ".git")).FullName;
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feature/add-widget\n");

        Assert.Equal("feature/add-widget", GitHead.FindBranch(_root));
    }

    [Fact]
    public void DetachedHeadShowsFirstSevenHexCharacters()
    {
        var gitDir = Directory.CreateDirectory(Path.Combine(_root, ".git")).FullName;
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "bf90944fb9d7de61e3446ae713c0986120215ff2\n");

        Assert.Equal("bf90944", GitHead.FindBranch(_root));
    }

    [Fact]
    public void WorktreeGitFileIsFollowed()
    {
        var realGitDir = Directory.CreateDirectory(Path.Combine(_root, "real-git")).FullName;
        File.WriteAllText(Path.Combine(realGitDir, "HEAD"), "ref: refs/heads/worktree-branch\n");

        var worktreeDir = Directory.CreateDirectory(Path.Combine(_root, "worktree")).FullName;
        File.WriteAllText(Path.Combine(worktreeDir, ".git"), $"gitdir: {realGitDir}\n");

        Assert.Equal("worktree-branch", GitHead.FindBranch(worktreeDir));
    }

    [Fact]
    public void WorktreeGitFileWithRelativePathIsResolved()
    {
        var realGitDir = Directory.CreateDirectory(Path.Combine(_root, "real-git")).FullName;
        File.WriteAllText(Path.Combine(realGitDir, "HEAD"), "ref: refs/heads/relative-branch\n");

        var worktreeDir = Directory.CreateDirectory(Path.Combine(_root, "worktree")).FullName;
        File.WriteAllText(Path.Combine(worktreeDir, ".git"), "gitdir: ../real-git\n");

        Assert.Equal("relative-branch", GitHead.FindBranch(worktreeDir));
    }

    [Fact]
    public void WalksUpFromNestedSubdirectory()
    {
        var gitDir = Directory.CreateDirectory(Path.Combine(_root, ".git")).FullName;
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");
        var nested = Directory.CreateDirectory(Path.Combine(_root, "a", "b", "c")).FullName;

        Assert.Equal("main", GitHead.FindBranch(nested));
    }

    [Fact]
    public void NoGitDirectoryAnywhereReturnsNull()
    {
        Assert.Null(GitHead.FindBranch(_root));
    }

    [Fact]
    public void NullOrEmptyStartDirReturnsNull()
    {
        Assert.Null(GitHead.FindBranch(null));
        Assert.Null(GitHead.FindBranch(""));
    }

    [Fact]
    public void GarbageHeadContentsReturnsNull()
    {
        var gitDir = Directory.CreateDirectory(Path.Combine(_root, ".git")).FullName;
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "not a valid HEAD file\n");

        Assert.Null(GitHead.FindBranch(_root));
    }
}
