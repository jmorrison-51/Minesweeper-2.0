using Minesweeper.Core;

namespace Minesweeper.Core.Tests;

public class LeaderboardTests
{
    private static (string, SaveData) Player(string name, Action<SaveData>? setup = null)
    {
        var save = new SaveData();
        setup?.Invoke(save);
        return (name, save);
    }

    [Fact]
    public void FastestTimeRanksFirstAndPlayersWithoutAScoreAreLeftOut()
    {
        var players = new[]
        {
            Player("Slow", s => s.TrySetBestTime("Beginner", 45_000)),
            Player("NoScore"),
            Player("Fast", s => s.TrySetBestTime("Beginner", 9_500)),
            Player("OtherMode", s => s.TrySetBestTime("Expert", 100_000)),
            Player("Mid", s => s.TrySetBestTime("Beginner", 20_250)),
        };

        var board = Leaderboard.BestTimes(players, "Beginner");

        Assert.Equal(new[] { "Fast", "Mid", "Slow" }, board.Select(e => e.Player));
        Assert.Equal(new[] { 1, 2, 3 }, board.Select(e => e.Rank));
        Assert.Equal("9.50 s", board[0].Value);
    }

    [Fact]
    public void HexAndSquareTimesAreRankedSeparately()
    {
        var players = new[]
        {
            Player("A", s => { s.TrySetBestTime("Beginner", 10_000); s.TrySetBestTime("Hex Beginner", 30_000); }),
            Player("B", s => s.TrySetBestTime("Hex Beginner", 20_000)),
        };

        Assert.Equal(new[] { "A" }, Leaderboard.BestTimes(players, "Beginner").Select(e => e.Player));
        Assert.Equal(new[] { "B", "A" }, Leaderboard.BestTimes(players, "Hex Beginner").Select(e => e.Player));
    }

    [Fact]
    public void EqualTimesAreOrderedByName()
    {
        var players = new[]
        {
            Player("zed", s => s.TrySetBestTime("Expert", 50_000)),
            Player("Amy", s => s.TrySetBestTime("Expert", 50_000)),
        };
        Assert.Equal(new[] { "Amy", "zed" }, Leaderboard.BestTimes(players, "Expert").Select(e => e.Player));
    }

    [Theory]
    [InlineData(9_500, "9.50 s")]
    [InlineData(59_994, "59.99 s")]
    [InlineData(60_000, "1:00.00")]
    [InlineData(125_340, "2:05.34")]
    public void SolveTimesAreFormatted(long ms, string expected)
    {
        Assert.Equal(expected, Leaderboard.FormatSolveTime(ms));
    }

    [Fact]
    public void ChallengeRanksByFurthestLevelThenFlaglessClears()
    {
        var players = new[]
        {
            Player("Low", s => s.ChallengeUnlocked = 4),
            Player("New"),
            Player("HighFlagless", s => { s.ChallengeUnlocked = 9; s.MarkFlaglessClear(1); s.MarkFlaglessClear(2); }),
            Player("High", s => s.ChallengeUnlocked = 9),
            Player("Done", s => s.ChallengeUnlocked = 21),
        };

        var board = Leaderboard.Challenge(players);

        Assert.Equal(new[] { "Done", "HighFlagless", "High", "Low" }, board.Select(e => e.Player));
        Assert.Equal("All 20 levels", board[0].Value);
        Assert.Equal("Level 8 (2 no-flag)", board[1].Value);
        Assert.Equal("Level 8", board[2].Value);
        Assert.Equal("Level 3", board[3].Value);
    }

    [Fact]
    public void EndlessRanksLongestRunFirst()
    {
        var players = new[]
        {
            Player("Short", s => s.RecordEndlessRun(60_000, 5)),
            Player("Never"),
            Player("Long", s => s.RecordEndlessRun(250_000, 31)),
        };

        var board = Leaderboard.Endless(players);

        Assert.Equal(new[] { "Long", "Short" }, board.Select(e => e.Player));
        Assert.Equal("4:10 (31 rows)", board[0].Value);
    }

    [Fact]
    public void ScoreboardsAreEmptyWhenNobodyHasPlayed()
    {
        var players = new[] { Player("A"), Player("B") };
        Assert.Empty(Leaderboard.BestTimes(players, "Beginner"));
        Assert.Empty(Leaderboard.Challenge(players));
        Assert.Empty(Leaderboard.Endless(players));
        Assert.Empty(Leaderboard.Endless(Array.Empty<(string, SaveData)>()));
    }

    [Fact]
    public void AdminNeverAppearsBecauseLoadAllSkipsThem()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Create("Alice");
        var alice = SaveData.Load(store.PathFor("Alice"));
        alice.TrySetBestTime("Beginner", 12_000);
        alice.Save(store.PathFor("Alice"));

        // The admin profile has a better time on disk but must not be ranked.
        var admin = new SaveData { AdminUnlock = true };
        admin.TrySetBestTime("Beginner", 1_000);
        admin.RecordEndlessRun(999_000, 99);
        admin.Save(store.PathFor("admin"));

        var all = store.LoadAll();

        Assert.Equal(new[] { "Alice" }, all.Select(p => p.Name));
        Assert.Equal(new[] { "Alice" }, Leaderboard.BestTimes(all, "Beginner").Select(e => e.Player));
        Assert.Empty(Leaderboard.Endless(all));
    }
}
