namespace Minesweeper.Core;

/// <summary>Simple string storage, such as browser localStorage. Lets profiles work where there is no file system.</summary>
public interface IKeyValueStore
{
    IEnumerable<string> Keys { get; }

    string? Get(string key);

    /// <summary>False with a reason if the value could not be stored (for example, storage is full).</summary>
    bool TrySet(string key, string value, out string error);

    void Remove(string key);
}

/// <summary>An <see cref="IKeyValueStore"/> kept in memory: for tests, and for browsers that block storage.</summary>
public sealed class MemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, string> _values = new();

    public IEnumerable<string> Keys => _values.Keys.ToList();

    public string? Get(string key) => _values.TryGetValue(key, out string? value) ? value : null;

    public bool TrySet(string key, string value, out string error)
    {
        _values[key] = value;
        error = "";
        return true;
    }

    public void Remove(string key) => _values.Remove(key);
}

/// <summary>
/// Player profiles in an <see cref="IKeyValueStore"/>: the same rules as the desktop <see cref="ProfileStore"/>
/// (valid names, unique ignoring case, a hidden admin login) with each save kept as signed text.
/// </summary>
public sealed class KeyValueProfileStore
{
    private const string PlayerPrefix = "ms2.player.";
    private const string InvalidPrefix = "ms2.invalid.";
    private const string LastKey = "ms2.last";

    private readonly IKeyValueStore _store;

    public KeyValueProfileStore(IKeyValueStore store) => _store = store;

    // One key per player, by lowercase name so names stay unique ignoring case.
    // The value is the name as typed, a newline, then the signed save.
    private static string KeyFor(string name) => PlayerPrefix + name.Trim().ToLowerInvariant();

    private (string Name, string Save)? Entry(string key)
    {
        string? value = _store.Get(key);
        int newline = value?.IndexOf('\n') ?? -1;
        if (value == null || newline <= 0) return null;
        return (value[..newline], value[(newline + 1)..]);
    }

    /// <summary>Player names, alphabetical. The admin login is not included.</summary>
    public IReadOnlyList<string> List() =>
        _store.Keys
            .Where(k => k.StartsWith(PlayerPrefix, StringComparison.Ordinal))
            .Select(Entry)
            .Where(e => e != null && ProfileStore.IsValidName(e.Value.Name, out _))
            .Select(e => e!.Value.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Every listed player with their save, for scoreboards. The admin login is not included.</summary>
    public IReadOnlyList<(string Name, SaveData Save)> LoadAll() => List().Select(n => (n, Load(n))).ToList();

    /// <summary>The stored spelling of a name (matching ignores case), or null if there is no such player.</summary>
    public string? Find(string name) =>
        List().FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Makes a new player with fresh progress and returns the name as stored.</summary>
    public string Create(string name)
    {
        if (!ProfileStore.IsValidName(name, out string error)) throw new ArgumentException(error, nameof(name));
        name = name.Trim();
        if (Find(name) != null) throw new InvalidOperationException($"There is already a player called {name}.");
        if (!TrySave(name, new SaveData(), out string saveError))
            throw new IOException($"Could not create the player: {saveError}");
        return name;
    }

    public void Delete(string name)
    {
        string? stored = Find(name);
        if (stored == null) return;
        bool wasLast = string.Equals(LastProfile, stored, StringComparison.OrdinalIgnoreCase); // before they are gone
        _store.Remove(KeyFor(stored));
        for (int copy = 1; copy <= MaxDamagedCopies; copy++) _store.Remove(DamagedKey(stored, copy));
        if (wasLast) LastProfile = null;
    }

    /// <summary>
    /// A player's save (the admin login works too). A missing player gives fresh data. An altered save is kept
    /// under an "invalid" key and fresh data is returned, as the desktop does with "&lt;name&gt;.invalid".
    /// </summary>
    public SaveData Load(string name)
    {
        string key = KeyFor(name);
        if (Entry(key) is not { } entry) return new SaveData();
        if (SaveData.TryFromSignedText(entry.Save, out SaveData save)) return save;

        KeepDamagedCopy(name, entry.Save);
        return new SaveData { LoadStatus = SaveLoadStatus.Rejected, LoadNotice = SaveData.RejectedNotice };
    }

    private const int MaxDamagedCopies = 5;

    // Names cannot contain a dot, so "alice.2" never collides with another player's key.
    private static string DamagedKey(string name, int copy) =>
        InvalidPrefix + name.Trim().ToLowerInvariant() + (copy == 1 ? "" : "." + copy);

    // Sets a damaged save aside without overwriting an earlier one, and without copying the same text twice
    // (every load of a still-damaged save would otherwise make another).
    private void KeepDamagedCopy(string name, string damaged)
    {
        for (int copy = 1; copy <= MaxDamagedCopies; copy++)
        {
            string? existing = _store.Get(DamagedKey(name, copy));
            if (existing == damaged) return;
            if (existing == null)
            {
                _store.TrySet(DamagedKey(name, copy), damaged, out _);
                return;
            }
        }
    }

    private const string BackupHeader = "MS2BACKUP1";

    /// <summary>
    /// Every player's save as one block of text (a header, then one "name, tab, signed save" line each), to keep
    /// safe in case the browser loses its storage. Saves that are already damaged are left out.
    /// </summary>
    public string ExportBackup()
    {
        var text = new System.Text.StringBuilder(BackupHeader).Append('\n');
        foreach (string name in List())
            if (Entry(KeyFor(name)) is { } entry && SaveData.TryFromSignedText(entry.Save, out _))
                text.Append(entry.Name).Append('\t').Append(entry.Save).Append('\n');
        return text.ToString();
    }

    /// <summary>What <see cref="ImportBackup"/> did. Players who already exist are never overwritten.</summary>
    public sealed record BackupImport(bool Recognized, int Added, int AlreadyThere, int Damaged, bool StorageFull);

    /// <summary>
    /// Adds the players from <see cref="ExportBackup"/> text. Anything edited, misnamed or damaged is skipped,
    /// and a player who already exists is left as they are, so a restore cannot destroy newer progress.
    /// </summary>
    public BackupImport ImportBackup(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        if (lines[0].Trim() != BackupHeader) return new BackupImport(false, 0, 0, 0, false);

        int added = 0, existing = 0, damaged = 0;
        foreach (string line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            int tab = line.IndexOf('\t');
            string name = tab < 0 ? "" : line[..tab];
            string signed = tab < 0 ? "" : line[(tab + 1)..];

            if (name != name.Trim() || !ProfileStore.IsValidName(name, out _) || !SaveData.TryFromSignedText(signed, out _))
            {
                damaged++;
                continue;
            }
            if (Find(name) != null)
            {
                existing++;
                continue;
            }
            if (!_store.TrySet(KeyFor(name), name + "\n" + signed, out _))
                return new BackupImport(true, added, existing, damaged, StorageFull: true);
            added++;
        }
        return new BackupImport(true, added, existing, damaged, false);
    }

    public bool TrySave(string name, SaveData save, out string error)
    {
        name = name.Trim();
        return _store.TrySet(KeyFor(name), name + "\n" + save.ToSignedText(), out error);
    }

    /// <summary>The player chosen last time, if they still exist.</summary>
    public string? LastProfile
    {
        get => _store.Get(LastKey) is { } last ? Find(last) : null;
        set
        {
            if (value == null) _store.Remove(LastKey);
            else _store.TrySet(LastKey, value, out _);
        }
    }
}
