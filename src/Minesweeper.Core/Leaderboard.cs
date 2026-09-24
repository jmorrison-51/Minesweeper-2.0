using System.Globalization;

namespace Minesweeper.Core;

/// <summary>One line of a scoreboard. <see cref="Value"/> is ready to show.</summary>
public sealed record ScoreEntry(int Rank, string Player, string Value);

/// <summary>
/// Ranks players for each game mode, best first. Only players with a score in that mode are listed, and the
/// hidden admin login never appears because <see cref="ProfileStore"/> does not list it.
/// </summary>
public static class Leaderboard
{
    /// <summary>Fastest win on a difficulty, by <see cref="Difficulty.Key"/> ("Expert", "Hex Beginner", ...). Lower is better.</summary>
    public static IReadOnlyList<ScoreEntry> BestTimes(IEnumerable<(string Name, SaveData Save)> players, string difficultyKey) =>
        Rank(players
            .Where(p => p.Save.BestTimesMs.ContainsKey(difficultyKey))
            .Select(p => (p.Name, Ms: p.Save.BestTimesMs[difficultyKey]))
            .OrderBy(p => p.Ms)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => (p.Name, FormatSolveTime(p.Ms))));

    /// <summary>Hex Challenge: furthest level cleared, then most levels cleared without flags.</summary>
    public static IReadOnlyList<ScoreEntry> Challenge(IEnumerable<(string Name, SaveData Save)> players) =>
        Rank(players
            .Select(p => (p.Name, Cleared: LevelsCleared(p.Save), Flagless: p.Save.FlaglessLevelCount))
            .Where(p => p.Cleared > 0)
            .OrderByDescending(p => p.Cleared)
            .ThenByDescending(p => p.Flagless)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => (p.Name,
                (p.Cleared == ChallengeLevel.Count ? "All 20 levels" : $"Level {p.Cleared}") +
                (p.Flagless > 0 ? $" ({p.Flagless} no-flag)" : ""))));

    /// <summary>Endless Mode: longest run, then most rows cleared.</summary>
    public static IReadOnlyList<ScoreEntry> Endless(IEnumerable<(string Name, SaveData Save)> players) =>
        Rank(players
            .Where(p => p.Save.EndlessBestMs > 0)
            .OrderByDescending(p => p.Save.EndlessBestMs)
            .ThenByDescending(p => p.Save.EndlessBestRows)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => (p.Name, $"{FormatDuration(p.Save.EndlessBestMs)} ({p.Save.EndlessBestRows} rows)")));

    /// <summary>Highest Hex Challenge level a player has cleared, 0 if none. Levels unlock in order.</summary>
    public static int LevelsCleared(SaveData save) => Math.Clamp(save.ChallengeUnlocked - 1, 0, ChallengeLevel.Count);

    public static string FormatSolveTime(long ms) =>
        ms < 60_000
            ? (ms / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " s"
            : $"{ms / 60_000}:{ms / 1000 % 60:00}.{ms % 1000 / 10:00}";

    public static string FormatDuration(long ms) => $"{ms / 60_000}:{ms / 1000 % 60:00}";

    private static IReadOnlyList<ScoreEntry> Rank(IEnumerable<(string Name, string Value)> ordered) =>
        ordered.Select((p, i) => new ScoreEntry(i + 1, p.Name, p.Value)).ToList();
}
