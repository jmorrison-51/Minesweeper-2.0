using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minesweeper.Core;

public sealed class SaveData
{
    public string LastDifficulty { get; set; } = "Beginner";
    public string LastHexDifficulty { get; set; } = "Beginner";

    public static readonly int[] TileSizes = [24, 36, 48];

    // Tile edge length in logical pixels, chosen on the start screen.
    public int TileSize { get; set; } = 48;
    // Hex Challenge progress: highest level the player may play, and the level they were last on.
    public int ChallengeUnlocked { get; set; } = 1;
    public int ChallengeCurrent { get; set; } = 1;

    [JsonIgnore]
    public bool ChallengeCompleted => ChallengeUnlocked > ChallengeLevel.Count;

    /// <summary>Highest level that can be played right now.</summary>
    [JsonIgnore]
    public int ChallengePlayable => Math.Clamp(ChallengeUnlocked, 1, ChallengeLevel.Count);

    /// <summary>Records a cleared level and unlocks the next one. Returns the level to play next, or null after the last.</summary>
    public int? CompleteChallengeLevel(int level)
    {
        int next = level + 1;
        if (next > ChallengeUnlocked) ChallengeUnlocked = Math.Min(next, ChallengeLevel.Count + 1);
        return next <= ChallengeLevel.Count ? next : null;
    }

    public static string ChallengeKey(int level) => ChallengeLevel.Get(level).Difficulty.Key;

    /// <summary>
    /// Games in a row on a level without a safe start (9-20) whose opening guess failed (see
    /// <see cref="Board.OpeningGuessFailed"/>). At <see cref="BadStartsForOpener"/> the next such game gets a
    /// guaranteed opening; that game opens an area, so it resets the count.
    /// </summary>
    public int ChallengeBadStarts { get; set; }

    public const int BadStartsForOpener = 2;

    public bool NeedsGuaranteedOpener(int level) =>
        !ChallengeLevel.Get(level).Rules.SafeStart && ChallengeBadStarts >= BadStartsForOpener;

    /// <summary>Records how a challenge game started, once it is decided (second reveal or game over).</summary>
    public void RecordChallengeStart(int level, bool openingGuessFailed)
    {
        if (ChallengeLevel.Get(level).Rules.SafeStart) return;
        ChallengeBadStarts = openingGuessFailed ? ChallengeBadStarts + 1 : 0;
    }

    /// <summary>The rules to play a level with, adding a safe start if the player has earned one.</summary>
    public BoardRules ChallengeRules(int level)
    {
        var rules = ChallengeLevel.Get(level).Rules;
        return NeedsGuaranteedOpener(level) ? rules with { SafeStart = true } : rules;
    }

    // Levels cleared without the player ever placing a flag. Clearing all 20 unlocks Endless Mode.
    public List<int> ChallengeFlaglessLevels { get; set; } = new();

    public bool IsFlaglessCleared(int level) => ChallengeFlaglessLevels.Contains(level);

    public void MarkFlaglessClear(int level)
    {
        if (level is >= 1 and <= ChallengeLevel.Count && !ChallengeFlaglessLevels.Contains(level))
            ChallengeFlaglessLevels.Add(level);
    }

    [JsonIgnore]
    public int FlaglessLevelCount => ChallengeFlaglessLevels.Where(l => l is >= 1 and <= ChallengeLevel.Count).Distinct().Count();

    /// <summary>Set at runtime (never saved) when signed in as admin, which unlocks Endless Mode for testing.</summary>
    [JsonIgnore]
    public bool AdminUnlock { get; set; }

    [JsonIgnore]
    public bool EndlessUnlocked => AdminUnlock || FlaglessLevelCount == ChallengeLevel.Count;

    // Endless Mode records: longest run in milliseconds, and the most rows cleared in a run.
    public long EndlessBestMs { get; set; }
    public int EndlessBestRows { get; set; }

    /// <summary>Records a finished Endless run. Returns true if it is a new longest run.</summary>
    public bool RecordEndlessRun(long survivedMs, int rowsCleared)
    {
        EndlessBestRows = Math.Max(EndlessBestRows, rowsCleared);
        if (survivedMs <= EndlessBestMs) return false;
        EndlessBestMs = survivedMs;
        return true;
    }

    public int CustomColumns { get; set; } = 16;
    public int CustomRows { get; set; } = 16;
    public int CustomMines { get; set; } = 40;

    // Best win time in milliseconds, keyed by Difficulty.Key ("Beginner", "Hex Beginner", ...).
    public Dictionary<string, long> BestTimesMs { get; set; } = new();

    /// <summary>Shortest time a win can record, so a lucky win on the first click cannot set an unbeatable 0.</summary>
    public const long MinSolveMs = 1000;

    public static long SolveTime(long elapsedMs) => Math.Max(MinSolveMs, elapsedMs);

    public bool TrySetBestTime(string difficultyName, long elapsedMs)
    {
        elapsedMs = SolveTime(elapsedMs);
        if (BestTimesMs.TryGetValue(difficultyName, out long best) && best <= elapsedMs) return false;
        BestTimesMs[difficultyName] = elapsedMs;
        return true;
    }

    /// <summary>
    /// Loads an encrypted save. A missing file gives fresh data. A file that is not a valid save (edited,
    /// corrupted, or plain text) is copied to "&lt;name&gt;.invalid" and fresh data is returned.
    /// </summary>
    public static SaveData Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                if (SaveCrypto.TryDecrypt(File.ReadAllBytes(path), out byte[] json))
                    return (JsonSerializer.Deserialize<SaveData>(json) ?? new SaveData()).Normalized();
                KeepUnreadableCopy(path);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new SaveData();
    }

    /// <summary>Like <see cref="Load"/> but also accepts the old plain-text format. Only for the one-time upgrade.</summary>
    public static SaveData LoadAny(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (SaveCrypto.TryDecrypt(bytes, out byte[] json)) bytes = json;
            return (JsonSerializer.Deserialize<SaveData>(bytes) ?? new SaveData()).Normalized();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SaveData();
        }
    }

    public void Save(string path) => TrySave(path, out _);

    /// <summary>Saves, reporting a failure (disk full, file locked, no permission) instead of throwing.</summary>
    public bool TrySave(string path, out string error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] data = SaveCrypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(this));

            // Write beside the file and swap it in, so a crash mid-write cannot leave a broken save.
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, path, overwrite: true);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    // A save can come from an old version or plain JSON (LoadAny), so repair anything that would break the
    // game or the scoreboards: nulls where collections are expected, and values outside their range.
    private SaveData Normalized()
    {
        LastDifficulty ??= "Beginner";
        LastHexDifficulty ??= "Beginner";
        ChallengeFlaglessLevels = (ChallengeFlaglessLevels ?? new())
            .Where(l => l is >= 1 and <= ChallengeLevel.Count).Distinct().ToList();
        BestTimesMs = (BestTimesMs ?? new())
            .Where(p => p.Key != null && p.Value >= 0)
            .ToDictionary(p => p.Key, p => p.Value);
        ChallengeUnlocked = Math.Clamp(ChallengeUnlocked, 1, ChallengeLevel.Count + 1);
        ChallengeCurrent = Math.Clamp(ChallengeCurrent, 1, ChallengeLevel.Count);
        EndlessBestMs = Math.Max(0, EndlessBestMs);
        EndlessBestRows = Math.Max(0, EndlessBestRows);
        ChallengeBadStarts = Math.Max(0, ChallengeBadStarts);
        return this;
    }

    private static void KeepUnreadableCopy(string path)
    {
        try
        {
            File.Copy(path, path + ".invalid", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
