using System.Text.RegularExpressions;

namespace Minesweeper.Core;

/// <summary>
/// One save file per player, in a folder. Each file is a normal (encrypted) <see cref="SaveData"/>, so every
/// player has their own best times, level progress and settings. The reserved "admin" login is never listed.
/// </summary>
public sealed class ProfileStore
{
    public const int MaxNameLength = 20;

    // Names become file names, so keep them plain. The prefix also avoids reserved Windows names like CON.
    private const string Prefix = "p_";
    private const string Extension = ".dat";
    private const string LastFileName = "last.txt";

    private static readonly Regex NamePattern = new(@"^[A-Za-z0-9][A-Za-z0-9 _-]*$", RegexOptions.Compiled);

    private readonly string _directory;

    public ProfileStore(string directory) => _directory = directory;

    public string PathFor(string name) => Path.Combine(_directory, Prefix + name + Extension);

    public static bool IsValidName(string? name, out string error)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0) error = "Enter a name.";
        else if (name.Length > MaxNameLength) error = $"Names can be at most {MaxNameLength} characters.";
        else if (!NamePattern.IsMatch(name)) error = "Use letters, numbers, spaces, - and _ only.";
        else if (AdminAccess.IsAdminName(name)) error = "That name is reserved.";
        else error = "";
        return error.Length == 0;
    }

    /// <summary>Player names, alphabetical. The admin login is not included.</summary>
    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<string>();
        return Directory.EnumerateFiles(_directory, Prefix + "*" + Extension)
            .Select(f => Path.GetFileNameWithoutExtension(f)[Prefix.Length..])
            // Skip stray files ("p_.dat", "p_a.b.dat") that could not have come from Create.
            .Where(n => n.Length <= MaxNameLength && NamePattern.IsMatch(n) && n == n.Trim())
            .Where(n => !AdminAccess.IsAdminName(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Every listed player with their save, for scoreboards. The admin login is not included.</summary>
    public IReadOnlyList<(string Name, SaveData Save)> LoadAll() =>
        List().Select(name => (name, SaveData.Load(PathFor(name)))).ToList();

    /// <summary>The stored spelling of a name (matching ignores case), or null if there is no such player.</summary>
    public string? Find(string name) =>
        List().FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Makes a new player with fresh progress and returns the name as stored.</summary>
    public string Create(string name)
    {
        if (!IsValidName(name, out string error)) throw new ArgumentException(error, nameof(name));
        name = name.Trim();
        if (Find(name) != null) throw new InvalidOperationException($"There is already a player called {name}.");

        if (!new SaveData().TrySave(PathFor(name), out string saveError))
            throw new IOException($"Could not create the player file: {saveError}");
        return name;
    }

    public void Delete(string name)
    {
        string? stored = Find(name);
        if (stored == null) return;
        bool wasLast = string.Equals(LastProfile, stored, StringComparison.OrdinalIgnoreCase); // before they are gone
        SaveData.DeleteWithCompanions(PathFor(stored));
        if (wasLast) LastProfile = null;
    }

    /// <summary>The player chosen last time, if they still exist.</summary>
    public string? LastProfile
    {
        get
        {
            try
            {
                string file = Path.Combine(_directory, LastFileName);
                return File.Exists(file) ? Find(File.ReadAllText(file)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        set
        {
            try
            {
                string file = Path.Combine(_directory, LastFileName);
                if (value == null) File.Delete(file);
                else
                {
                    Directory.CreateDirectory(_directory);
                    File.WriteAllText(file, value);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// One-time upgrade from the old single save file: if it exists and nobody has a profile yet, it becomes a
    /// player called <paramref name="name"/> and the old file is renamed out of the way. Returns true if it moved.
    /// </summary>
    public bool MigrateLegacy(string legacyPath, string name = "Player")
    {
        try
        {
            if (!File.Exists(legacyPath) || List().Count > 0) return false;

            SaveData data = SaveData.LoadAny(legacyPath);
            Directory.CreateDirectory(_directory);
            data.Save(PathFor(name));
            File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
            LastProfile = name;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
