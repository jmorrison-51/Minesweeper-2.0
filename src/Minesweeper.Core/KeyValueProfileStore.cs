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
        _store.Remove(KeyFor(stored));
        if (string.Equals(LastProfile, stored, StringComparison.OrdinalIgnoreCase)) LastProfile = null;
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

        _store.TrySet(InvalidPrefix + name.Trim().ToLowerInvariant(), entry.Save, out _);
        return new SaveData();
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
