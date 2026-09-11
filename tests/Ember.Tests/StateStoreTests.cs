using Ember.Core.Data;
using Xunit;

namespace Ember.Tests;

/// <summary>§8: the state file's load/save/atomic-write and account/machine keying.</summary>
public class StateStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public StateStoreTests()
    {
        _dir = Directory.CreateTempSubdirectory("ember-state-").FullName;
        _path = Path.Combine(_dir, "statusline-state.json");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LoadOnMissingFileReturnsFreshState()
    {
        var state = StateStore.Load(_path);
        Assert.Equal(StateFile.CurrentVersion, state.Version);
        Assert.Empty(state.Accounts);
    }

    [Fact]
    public void SaveThenLoadRoundTripsData()
    {
        var state = new StateFile();
        state.Accounts["acct/machine"] = new AccountState
        {
            Day = "2026-09-11",
            DayStartCredits = 100,
            TodayEstimateUsd = 2.41,
            Sessions =
            {
                ["s1"] = new SessionState
                {
                    FiveHourWatermark = 63,
                    FiveHourResetsAt = 1789142400,
                    SessionStartCredits = 50,
                    LastCostUsd = 0.82,
                    SeenAt = 1789126800,
                },
            },
        };

        StateStore.Save(_path, state);
        var loaded = StateStore.Load(_path);

        Assert.Equal(2, loaded.Version);
        Assert.True(loaded.Accounts.TryGetValue("acct/machine", out var account));
        Assert.Equal("2026-09-11", account!.Day);
        Assert.Equal(100, account.DayStartCredits);
        Assert.Equal(2.41, account.TodayEstimateUsd);
        Assert.True(account.Sessions.TryGetValue("s1", out var session));
        Assert.Equal(63, session!.FiveHourWatermark);
        Assert.Equal(1789142400, session.FiveHourResetsAt);
        Assert.Equal(50, session.SessionStartCredits);
        Assert.Equal(0.82, session.LastCostUsd);
        Assert.Equal(1789126800, session.SeenAt);
    }

    [Fact]
    public void SaveWritesAtomicallyViaTempFileAndRename()
    {
        StateStore.Save(_path, new StateFile());
        Assert.True(File.Exists(_path));
        // No leftover temp files after a successful save.
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.Contains(".tmp-"));
    }

    [Fact]
    public void CorruptFileIsDiscardedAndRebuilt()
    {
        File.WriteAllText(_path, "{ this is not valid json");
        var state = StateStore.Load(_path);
        Assert.Empty(state.Accounts);
    }

    [Fact]
    public void UnrecognisedVersionIsDiscardedAndRebuilt()
    {
        File.WriteAllText(_path, """{"version":99,"accounts":{"x":{"day":"2020-01-01","day_start_credits":0,"today_estimate_usd":0,"sessions":{}}}}""");
        var state = StateStore.Load(_path);
        Assert.Empty(state.Accounts);
    }

    [Fact]
    public void AccountKeyCombinesUuidAndMachine()
    {
        Assert.Equal("uuid-1/abc12345", StateStore.AccountKey("uuid-1", "abc12345"));
    }

    [Fact]
    public void MachineIdIsEightHexCharsAndDeterministicWithinAProcess()
    {
        var id1 = StateStore.ComputeMachineId();
        var id2 = StateStore.ComputeMachineId();
        Assert.Equal(id1, id2);
        Assert.Equal(8, id1.Length);
        Assert.Matches("^[0-9a-f]{8}$", id1);
    }
}
