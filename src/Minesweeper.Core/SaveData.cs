using System.Text.Json;

namespace Minesweeper.Core;

public sealed class SaveData
{
    public string LastDifficulty { get; set; } = "Beginner";
    public int CustomColumns { get; set; } = 16;
    public int CustomRows { get; set; } = 16;
    public int CustomMines { get; set; } = 40;

    // Best win time in milliseconds, keyed by difficulty name.
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
