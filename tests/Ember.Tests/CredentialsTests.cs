using Ember.Core.Data;
using Xunit;

namespace Ember.Tests;

public class CredentialsTests
{
    [Fact]
    public void ParseAccessTokenExtractsFromValidJson()
    {
        var token = Credentials.ParseAccessToken("""{"claudeAiOauth":{"accessToken":"tok-123"}}""");
        Assert.Equal("tok-123", token);
    }

    [Fact]
    public void ParseAccessTokenReturnsNullWhenOauthBlockMissing()
    {
        Assert.Null(Credentials.ParseAccessToken("""{"someOtherKey":"value"}"""));
    }

    [Fact]
    public void ParseAccessTokenReturnsNullWhenAccessTokenFieldMissing()
    {
        Assert.Null(Credentials.ParseAccessToken("""{"claudeAiOauth":{"refreshToken":"only-refresh"}}"""));
    }

    [Fact]
    public void ParseAccessTokenReturnsNullForCorruptJson()
    {
        Assert.Null(Credentials.ParseAccessToken("not json at all"));
    }

    [Fact]
    public void ParseAccessTokenReturnsNullForEmptyObject()
    {
        Assert.Null(Credentials.ParseAccessToken("{}"));
    }

    [Fact]
    public void ParseAccessTokenReturnsNullWhenAccessTokenIsNotString()
    {
        Assert.Null(Credentials.ParseAccessToken("""{"claudeAiOauth":{"accessToken":42}}"""));
    }
}

[Collection("EnvironmentVariables")]
public class CredentialsFileTests : IDisposable
{
    private readonly string? _origConfigDir;
    private readonly string _tempConfigDir;

    public CredentialsFileTests()
    {
        _origConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        _tempConfigDir = Directory.CreateTempSubdirectory("ember-creds-").FullName;
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _tempConfigDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _origConfigDir);
        Directory.Delete(_tempConfigDir, recursive: true);
    }

    [Fact]
    public void ConfigDirRespectsEnvironmentVariable()
    {
        Assert.Equal(_tempConfigDir, Credentials.ConfigDir());
    }
}
