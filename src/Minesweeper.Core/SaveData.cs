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

    public int CustomColumns { get; set; } = 16;
    public int CustomRows { get; set; } = 16;
    public int CustomMines { get; set; } = 40;

    // Best win time in milliseconds, keyed by Difficulty.Key ("Beginner", "Hex Beginner", ...).
    public Dictionary<string, long> BestTimesMs { get; set; } = new();

    public bool TrySetBestTime(string difficultyName, long elapsedMs)
    {
        if (BestTimesMs.TryGetValue(difficultyName, out long best) && best <= elapsedMs) return false;
        BestTimesMs[difficultyName] = elapsedMs;
        return true;
    }

    public static SaveData Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(path)) ?? new SaveData();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new SaveData();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
