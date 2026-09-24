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

    /// <summary>How <see cref="Load"/> came by the data, so a front end can tell the player when something went wrong.</summary>
    [JsonIgnore]
    public SaveLoadStatus LoadStatus { get; internal set; }

    /// <summary>Plain-language explanation for the player when <see cref="LoadStatus"/> is not Ok, otherwise null.</summary>
    [JsonIgnore]
    public string? LoadNotice { get; internal set; }

    /// <summary>
    /// True when the file exists but could not be read, so these are blank stand-in data. Saving them would
    /// destroy the real progress, so <see cref="TrySave"/> refuses.
    /// </summary>
    [JsonIgnore]
    public bool IsProtected { get; private set; }

    internal const string RecoveredNotice =
        "Your latest save file was damaged, so the save before it was restored. A copy of the damaged file was kept beside it.";

    internal const string RejectedNotice =
        "Your save file was damaged and could not be restored, so you are starting fresh. A copy of the damaged file was kept beside it.";

    private const string UnreadableNotice =
        "Your save file could not be read (another program may be using it). To protect your progress, nothing will be saved this time. " +
        "Close anything that might be using the file and start the game again.";

    /// <summary>
    /// Loads an encrypted save. A missing file gives fresh data. A file that is not a valid save (edited,
    /// corrupted, or plain text) is set aside as "&lt;name&gt;.invalid", then the previous save
    /// ("&lt;name&gt;.bak") is used if it is good, else fresh data. A file that exists but cannot be read (locked
    /// by another program) gives protected data that will not overwrite it. <see cref="LoadStatus"/> says which.
    /// </summary>
    public static SaveData Load(string path)
    {
        try
        {
            if (!TryRead(path, out byte[]? bytes))
                return new SaveData { LoadStatus = SaveLoadStatus.Unreadable, LoadNotice = UnreadableNotice, IsProtected = true };
            if (bytes == null) return new SaveData();
            if (TryParse(bytes, out SaveData save)) return save;

            KeepUnreadableCopy(path, bytes);
            if (TryRead(path + ".bak", out byte[]? backup, attempts: 1) && backup != null && TryParse(backup, out SaveData restored))
            {
                restored.LoadStatus = SaveLoadStatus.Recovered;
                restored.LoadNotice = RecoveredNotice;
                return restored;
            }
            return new SaveData { LoadStatus = SaveLoadStatus.Rejected, LoadNotice = RejectedNotice };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SaveData { LoadStatus = SaveLoadStatus.Unreadable, LoadNotice = UnreadableNotice, IsProtected = true };
        }
    }

    // Reads a file, trying again briefly if something else (antivirus, a sync tool) has it open. A missing file
    // is not a failure: it gives null bytes. False means the file is there but could not be read.
    private static bool TryRead(string path, out byte[]? bytes, int attempts = 5)
    {
        bytes = null;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                bytes = File.ReadAllBytes(path);
                return true;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts) return false;
                Thread.Sleep(100);
            }
        }
    }

    private static bool TryParse(byte[] bytes, out SaveData save)
    {
        save = new SaveData();
        if (!SaveCrypto.TryDecrypt(bytes, out byte[] json)) return false;
        try
        {
            save = (JsonSerializer.Deserialize<SaveData>(json) ?? new SaveData()).Normalized();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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

    /// <summary>
    /// The save as signed, readable text for key-value storage (browser localStorage). Browsers have no AES,
    /// so unlike the encrypted desktop files this is not hidden, but an edited value is still rejected.
    /// </summary>
    public string ToSignedText() => SaveCrypto.Sign(JsonSerializer.Serialize(this));

    /// <summary>Reads text made by <see cref="ToSignedText"/>. False (with fresh data) if it was altered or unreadable.</summary>
    public static bool TryFromSignedText(string? text, out SaveData save)
    {
        save = new SaveData();
        if (text == null || !SaveCrypto.TryVerify(text, out string json)) return false;
        try
        {
            save = (JsonSerializer.Deserialize<SaveData>(json) ?? new SaveData()).Normalized();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Save(string path) => TrySave(path, out _);

    /// <summary>Saves, reporting a failure (disk full, file locked, no permission) instead of throwing.</summary>
    public bool TrySave(string path, out string error)
    {
        if (IsProtected)
        {
            error = "Your saved progress could not be read at the start, so the game will not overwrite it. " +
                    "Close anything that might be using the file and start the game again.";
            return false;
        }

        string temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] data = SaveCrypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(this));

            // Write beside the file, force it onto the disk, then swap it in: a crash or power cut mid-write
            // cannot leave a broken save. The swap keeps the save it replaces as "<name>.bak".
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            Swap(temp, path);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception) { }
            error = ex.Message;
            return false;
        }
    }

    // Antivirus and sync tools sometimes hold a file open for a moment, so try a few times.
    private static void Swap(string temp, string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Deletes a save and everything kept beside it: the previous-save backup, an unfinished temp file and any
    /// copies of damaged saves. The save itself goes first, so if it cannot be deleted nothing else is touched.
    /// </summary>
    public static void DeleteWithCompanions(string path)
    {
        File.Delete(path);
        TryDelete(path + ".tmp");
        TryDelete(path + ".bak");

        string? directory = Path.GetDirectoryName(path);
        if (directory == null || !Directory.Exists(directory)) return;
        foreach (string copy in Directory.EnumerateFiles(directory, Path.GetFileName(path) + ".invalid*"))
            TryDelete(copy);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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

    // Sets a damaged file aside as "<name>.invalid" (then ".invalid2", ...) so a later problem cannot overwrite the
    // first evidence. The same content is not copied twice: every load of a still-damaged file would make another.
    private static void KeepUnreadableCopy(string path, byte[] contents)
    {
        try
        {
            string directory = Path.GetDirectoryName(path)!;
            string prefix = Path.GetFileName(path) + ".invalid";
            foreach (string existing in Directory.EnumerateFiles(directory, prefix + "*"))
                if (new FileInfo(existing).Length == contents.Length && File.ReadAllBytes(existing).AsSpan().SequenceEqual(contents))
                    return;

            string target = path + ".invalid";
            for (int n = 2; File.Exists(target); n++)
            {
                if (n > MaxDamagedCopies) return;
                target = path + ".invalid" + n;
            }
            File.WriteAllBytes(target, contents);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private const int MaxDamagedCopies = 5;
}

/// <summary>How a save came to be loaded. Anything but <see cref="Ok"/> comes with a notice for the player.</summary>
public enum SaveLoadStatus
{
    /// <summary>Read normally, or there was no file yet.</summary>
    Ok,

    /// <summary>The newest file was damaged; the save before it was restored.</summary>
    Recovered,

    /// <summary>The file was damaged and there was nothing to restore, so the player starts fresh.</summary>
    Rejected,

    /// <summary>The file exists but could not be read. The data are blank stand-ins that will not be saved.</summary>
    Unreadable,
}
