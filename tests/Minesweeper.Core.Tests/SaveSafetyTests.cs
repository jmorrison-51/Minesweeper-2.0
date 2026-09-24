using Minesweeper.Core;

namespace Minesweeper.Core.Tests;

// What happens when a save cannot be read or is damaged: progress must not be lost silently.
public class SaveSafetyTests
{
    private static void SaveAt(string path, int level) => new SaveData { ChallengeUnlocked = level }.Save(path);

    [Fact]
    public void ASaveThatCannotBeReadIsProtectedNotOverwritten()
    {
        using var dir = new TempDir();
        string path = dir.File("p_Kim.dat");
        SaveAt(path, 12);

        SaveData blank;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) // e.g. antivirus holding it
            blank = SaveData.Load(path);

        Assert.Equal(SaveLoadStatus.Unreadable, blank.LoadStatus);
        Assert.NotNull(blank.LoadNotice);
        Assert.True(blank.IsProtected);
        Assert.False(blank.TrySave(path, out string error));
        Assert.NotEmpty(error);

        // The real progress is still there once the file can be read again.
        Assert.Equal(12, SaveData.Load(path).ChallengeUnlocked);
    }

    [Fact]
    public void SavingKeepsThePreviousSaveAsABackupAndLeavesNoTempFile()
    {
        using var dir = new TempDir();
        string path = dir.File("p_Kim.dat");
        SaveAt(path, 3);
        Assert.False(File.Exists(path + ".bak"));

        SaveAt(path, 5);

        Assert.Equal(5, SaveData.Load(path).ChallengeUnlocked);
        Assert.Equal(3, SaveData.Load(path + ".bak").ChallengeUnlocked);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ADamagedSaveIsReplacedByThePreviousOneAndThePlayerIsTold()
    {
        using var dir = new TempDir();
        string path = dir.File("p_Kim.dat");
        SaveAt(path, 3);
        SaveAt(path, 5);
        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]); // a cut-off write

        var loaded = SaveData.Load(path);

        Assert.Equal(SaveLoadStatus.Recovered, loaded.LoadStatus);
        Assert.Equal(3, loaded.ChallengeUnlocked);
        Assert.NotNull(loaded.LoadNotice);
        Assert.False(loaded.IsProtected);
        Assert.True(File.Exists(path + ".invalid"));
    }

    [Fact]
    public void ADamagedSaveWithNoBackupStartsFreshAndSaysSo()
    {
        using var dir = new TempDir();
        string path = dir.File("p_Kim.dat");
        File.WriteAllText(path, "not a save");

        var loaded = SaveData.Load(path);

        Assert.Equal(SaveLoadStatus.Rejected, loaded.LoadStatus);
        Assert.Equal(1, loaded.ChallengeUnlocked);
        Assert.NotNull(loaded.LoadNotice);
        Assert.True(File.Exists(path + ".invalid"));
    }

    [Fact]
    public void AGoodSaveAndAMissingOneCarryNoNotice()
    {
        using var dir = new TempDir();
        string path = dir.File("p_Kim.dat");
        Assert.Null(SaveData.Load(path).LoadNotice);

        SaveAt(path, 4);

        var loaded = SaveData.Load(path);
        Assert.Equal(SaveLoadStatus.Ok, loaded.LoadStatus);
        Assert.Null(loaded.LoadNotice);
    }

    [Fact]
    public void LoadingTheSameDamagedFileAgainDoesNotPileUpCopies_ButADifferentOneIsKept()
    {
        using var dir = new TempDir();
        string path = dir.File("p_Kim.dat");
        File.WriteAllText(path, "first damage");
        for (int i = 0; i < 4; i++) SaveData.Load(path);
        Assert.Equal(new[] { "p_Kim.dat.invalid" }, InvalidCopies(dir));

        File.WriteAllText(path, "second, different damage");
        SaveData.Load(path);

        Assert.Equal(new[] { "p_Kim.dat.invalid", "p_Kim.dat.invalid2" }, InvalidCopies(dir));
        Assert.Equal("first damage", File.ReadAllText(path + ".invalid"));
    }

    [Fact]
    public void DeletingAPlayerRemovesTheirBackupTempAndDamagedCopies_ButNobodyElses()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Create("bob");
        store.Create("bobby");
        foreach (string suffix in new[] { ".tmp", ".bak", ".invalid", ".invalid2" })
        {
            File.WriteAllText(store.PathFor("bob") + suffix, "x");
            File.WriteAllText(store.PathFor("bobby") + suffix, "x");
        }

        store.Delete("bob");

        Assert.Equal(new[] { "bobby" }, store.List());
        Assert.Empty(Directory.GetFiles(dir.Path, "p_bob.dat*"));
        Assert.Equal(5, Directory.GetFiles(dir.Path, "p_bobby.dat*").Length);
    }

    private static string[] InvalidCopies(TempDir dir) =>
        Directory.GetFiles(dir.Path, "*.invalid*").Select(Path.GetFileName).Order().ToArray()!;
}

public class BrowserBackupTests
{
    private static KeyValueProfileStore StoreWith(params (string Name, int Level)[] players)
    {
        var store = new KeyValueProfileStore(new MemoryKeyValueStore());
        foreach (var (name, level) in players)
        {
            store.Create(name);
            var save = store.Load(name);
            save.ChallengeUnlocked = level;
            store.TrySave(name, save, out _);
        }
        return store;
    }

    [Fact]
    public void ABackupRestoresEveryPlayerIntoAnEmptyBrowser()
    {
        string backup = StoreWith(("Alice", 7), ("bob", 3)).ExportBackup();

        var fresh = new KeyValueProfileStore(new MemoryKeyValueStore());
        var result = fresh.ImportBackup(backup);

        Assert.True(result.Recognized);
        Assert.Equal(2, result.Added);
        Assert.Equal(new[] { "Alice", "bob" }, fresh.List());
        Assert.Equal(7, fresh.Load("alice").ChallengeUnlocked);
        Assert.Equal(3, fresh.Load("bob").ChallengeUnlocked);
    }

    [Fact]
    public void RestoringNeverOverwritesAPlayerWhoAlreadyExists()
    {
        string backup = StoreWith(("Alice", 2), ("bob", 3)).ExportBackup();
        var current = StoreWith(("alice", 9));

        var result = current.ImportBackup(backup);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.AlreadyThere);
        Assert.Equal(9, current.Load("Alice").ChallengeUnlocked);
        Assert.Equal(3, current.Load("bob").ChallengeUnlocked);
    }

    [Fact]
    public void EditedOrInvalidEntriesAndForeignFilesAreRejected()
    {
        string backup = StoreWith(("Alice", 2)).ExportBackup();
        var fresh = new KeyValueProfileStore(new MemoryKeyValueStore());

        Assert.False(fresh.ImportBackup("hello").Recognized);
        Assert.False(fresh.ImportBackup("").Recognized);

        string edited = backup.Replace("\"ChallengeUnlocked\":2", "\"ChallengeUnlocked\":20");
        var result = fresh.ImportBackup(edited);
        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Damaged);

        // A valid signed save under the reserved admin name, or a name with odd characters, is not accepted either.
        string signed = backup.Split('\n')[1].Split('\t')[1];
        var bad = fresh.ImportBackup("MS2BACKUP1\nadmin\t" + signed + "\n../x\t" + signed + "\nno tab here\n");
        Assert.Equal(0, bad.Added);
        Assert.Equal(3, bad.Damaged);
        Assert.Empty(fresh.List());
    }

    [Fact]
    public void ADamagedSaveIsExcludedFromTheBackupAndReportedOnLoad()
    {
        var kv = new MemoryKeyValueStore();
        var store = new KeyValueProfileStore(kv);
        store.Create("Alice");
        kv.TrySet("ms2.player.alice", "Alice\ngarbage", out _);

        var loaded = store.Load("Alice");

        Assert.Equal(SaveLoadStatus.Rejected, loaded.LoadStatus);
        Assert.NotNull(loaded.LoadNotice);
        Assert.Equal("MS2BACKUP1\n", store.ExportBackup());
    }

    [Fact]
    public void DamagedCopiesAreKeptOnceEachAndDeletedWithTheirPlayerOnly()
    {
        var kv = new MemoryKeyValueStore();
        var store = new KeyValueProfileStore(kv);
        store.Create("bob");
        store.Create("bobby");

        kv.TrySet("ms2.player.bob", "bob\nfirst", out _);
        store.Load("bob");
        store.Load("bob");
        kv.TrySet("ms2.player.bob", "bob\nsecond", out _);
        store.Load("bob");
        kv.TrySet("ms2.player.bobby", "bobby\nbroken", out _);
        store.Load("bobby");

        Assert.Equal("first", kv.Get("ms2.invalid.bob"));
        Assert.Equal("second", kv.Get("ms2.invalid.bob.2"));
        Assert.Null(kv.Get("ms2.invalid.bob.3"));

        store.Delete("bob");

        Assert.Null(kv.Get("ms2.invalid.bob"));
        Assert.Null(kv.Get("ms2.invalid.bob.2"));
        Assert.NotNull(kv.Get("ms2.invalid.bobby"));
    }
}

public class DeleteLeavesNothingBehindTests
{
    [Fact]
    public void DeletingTheLastPlayedOneClearsTheRememberedName_OnDiskAndInTheBrowser()
    {
        using var dir = new TempDir();
        var files = new ProfileStore(dir.Path);
        files.Create("Kim");
        files.LastProfile = "Kim";
        files.Delete("Kim");
        Assert.False(File.Exists(Path.Combine(dir.Path, "last.txt")));

        var kv = new MemoryKeyValueStore();
        var browser = new KeyValueProfileStore(kv);
        browser.Create("Kim");
        browser.LastProfile = "Kim";
        browser.Delete("Kim");
        Assert.Null(kv.Get("ms2.last"));
    }
}
