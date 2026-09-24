using System.Text;
using System.Text.Json;
using Minesweeper.Core;

namespace Minesweeper.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ms-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

public class EncryptionTests
{
    private static SaveData SampleSave()
    {
        var save = new SaveData { ChallengeUnlocked = 9, TileSize = 36 };
        save.TrySetBestTime("Beginner", 12345);
        save.MarkFlaglessClear(4);
        return save;
    }

    [Fact]
    public void SaveIsEncryptedOnDiskAndRoundTrips()
    {
        using var dir = new TempDir();
        string path = dir.File("save.dat");

        SampleSave().Save(path);

        string raw = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.DoesNotContain("Beginner", raw);
        Assert.DoesNotContain("ChallengeUnlocked", raw);

        var loaded = SaveData.Load(path);
        Assert.Equal(9, loaded.ChallengeUnlocked);
        Assert.Equal(36, loaded.TileSize);
        Assert.Equal(12345, loaded.BestTimesMs["Beginner"]);
        Assert.True(loaded.IsFlaglessCleared(4));
    }

    [Fact]
    public void SavingTwiceGivesDifferentBytesButTheSameData()
    {
        using var dir = new TempDir();
        SampleSave().Save(dir.File("a.dat"));
        SampleSave().Save(dir.File("b.dat"));

        Assert.NotEqual(File.ReadAllBytes(dir.File("a.dat")), File.ReadAllBytes(dir.File("b.dat")));
        Assert.Equal(SaveData.Load(dir.File("a.dat")).BestTimesMs["Beginner"], SaveData.Load(dir.File("b.dat")).BestTimesMs["Beginner"]);
    }

    [Fact]
    public void EditingAByteIsRejectedAndTheOriginalIsKept()
    {
        using var dir = new TempDir();
        string path = dir.File("save.dat");
        SampleSave().Save(path);

        byte[] bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0x5A;
        File.WriteAllBytes(path, bytes);

        var loaded = SaveData.Load(path);

        Assert.Equal(1, loaded.ChallengeUnlocked);
        Assert.Empty(loaded.BestTimesMs);
        Assert.True(File.Exists(path + ".invalid"));
    }

    [Fact]
    public void ReplacingTheFileWithPlainJsonIsRejected()
    {
        using var dir = new TempDir();
        string path = dir.File("save.dat");
        File.WriteAllText(path, JsonSerializer.Serialize(new SaveData { ChallengeUnlocked = 20 }));

        Assert.Equal(1, SaveData.Load(path).ChallengeUnlocked);
    }

    [Fact]
    public void MissingOrEmptyFilesGiveFreshData()
    {
        using var dir = new TempDir();
        Assert.Equal(1, SaveData.Load(dir.File("nope.dat")).ChallengeUnlocked);

        File.WriteAllBytes(dir.File("empty.dat"), Array.Empty<byte>());
        Assert.Equal(1, SaveData.Load(dir.File("empty.dat")).ChallengeUnlocked);
    }

    [Fact]
    public void SaveLeavesNoTempFileBehind()
    {
        using var dir = new TempDir();
        SampleSave().Save(dir.File("save.dat"));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void AFailedSaveReportsTheError()
    {
        using var dir = new TempDir();
        string path = dir.File("save.dat");
        Directory.CreateDirectory(path); // a folder where the file should go cannot be replaced

        Assert.False(SampleSave().TrySave(path, out string error));
        Assert.NotEmpty(error);
        Assert.True(SampleSave().TrySave(dir.File("ok.dat"), out error));
        Assert.Empty(error);
    }

    [Fact]
    public void NullsAndOutOfRangeValuesInAnOldSaveAreRepaired()
    {
        using var dir = new TempDir();
        string path = dir.File("save.json");
        File.WriteAllText(path,
            """{"BestTimesMs":null,"ChallengeFlaglessLevels":[3,3,0,99],"ChallengeUnlocked":500,"ChallengeCurrent":-4,"EndlessBestMs":-1,"LastDifficulty":null}""");

        var save = SaveData.LoadAny(path);

        Assert.Empty(save.BestTimesMs);
        Assert.Equal(new[] { 3 }, save.ChallengeFlaglessLevels);
        Assert.Equal(ChallengeLevel.Count + 1, save.ChallengeUnlocked);
        Assert.Equal(1, save.ChallengeCurrent);
        Assert.Equal(0, save.EndlessBestMs);
        Assert.Equal("Beginner", save.LastDifficulty);
        Assert.NotEmpty(Leaderboard.Challenge(new[] { ("A", save) }));
    }
}

public class ProfileTests
{
    [Fact]
    public void NewPlayersStartFreshAndKeepSeparateScores()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Create("Alice");
        store.Create("Bob");

        var alice = SaveData.Load(store.PathFor("Alice"));
        alice.TrySetBestTime("Beginner", 10_000);
        alice.ChallengeUnlocked = 5;
        alice.Save(store.PathFor("Alice"));

        var bob = SaveData.Load(store.PathFor("Bob"));
        Assert.Empty(bob.BestTimesMs);
        Assert.Equal(1, bob.ChallengeUnlocked);
        Assert.Equal(5, SaveData.Load(store.PathFor("Alice")).ChallengeUnlocked);
    }

    [Fact]
    public void ListIsAlphabeticalAndIgnoresCase()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        Assert.Empty(store.List());

        store.Create("zoe");
        store.Create("Adam");
        store.Create("Mia");

        Assert.Equal(new[] { "Adam", "Mia", "zoe" }, store.List());
        Assert.Equal("Adam", store.Find("aDAM"));
        Assert.Null(store.Find("Nobody"));
    }

    [Fact]
    public void DuplicateNamesAreRefusedEvenWithDifferentCase()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Create("Sam");
        Assert.Throws<InvalidOperationException>(() => store.Create("SAM"));
        Assert.Single(store.List());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("A", true)]
    [InlineData("Mary Jane", true)]
    [InlineData("kid_1-b", true)]
    [InlineData("bad/name", false)]
    [InlineData("..", false)]
    [InlineData("CON", true)]
    [InlineData("this name is way too long for us", false)]
    [InlineData("admin", false)]
    [InlineData("ADMIN", false)]
    [InlineData("  admin ", false)]
    public void NameValidation(string name, bool valid)
    {
        Assert.Equal(valid, ProfileStore.IsValidName(name, out _));
    }

    [Fact]
    public void ReservedWindowsNamesWorkBecauseOfTheFilePrefix()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Create("CON");
        Assert.Equal(new[] { "CON" }, store.List());
    }

    [Fact]
    public void DeleteRemovesOnlyThatPlayer()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Create("A");
        store.Create("B");
        store.LastProfile = "A";

        store.Delete("a");

        Assert.Equal(new[] { "B" }, store.List());
        Assert.Null(store.LastProfile);
    }

    [Fact]
    public void LastProfileIsRememberedOnlyWhileTheyExist()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        Assert.Null(store.LastProfile);

        store.Create("Kim");
        store.LastProfile = "Kim";
        Assert.Equal("Kim", new ProfileStore(dir.Path).LastProfile);

        store.Delete("Kim");
        Assert.Null(store.LastProfile);
    }

    [Fact]
    public void OldPlainSaveBecomesTheFirstPlayer()
    {
        using var dir = new TempDir();
        string legacy = dir.File("save.json");
        var old = new SaveData { ChallengeUnlocked = 7 };
        old.TrySetBestTime("Expert", 99_000);
        File.WriteAllText(legacy, JsonSerializer.Serialize(old));

        var store = new ProfileStore(dir.File("profiles"));
        Assert.True(store.MigrateLegacy(legacy));

        Assert.Equal(new[] { "Player" }, store.List());
        var migrated = SaveData.Load(store.PathFor("Player"));
        Assert.Equal(7, migrated.ChallengeUnlocked);
        Assert.Equal(99_000, migrated.BestTimesMs["Expert"]);
        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(legacy + ".migrated"));
        Assert.Equal("Player", store.LastProfile);
    }

    [Fact]
    public void MigrationDoesNothingWhenProfilesAlreadyExistOrThereIsNoOldFile()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.File("profiles"));
        Assert.False(store.MigrateLegacy(dir.File("save.json")));

        store.Create("Existing");
        File.WriteAllText(dir.File("save.json"), "{}");
        Assert.False(store.MigrateLegacy(dir.File("save.json")));
        Assert.Equal(new[] { "Existing" }, store.List());
    }
}

public class AdminTests
{
    [Theory]
    [InlineData("admin", true)]
    [InlineData("Admin", true)]
    [InlineData(" ADMIN ", true)]
    [InlineData("administrator", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AdminNameIgnoresCaseAndSpaces(string? name, bool expected)
    {
        Assert.Equal(expected, AdminAccess.IsAdminName(name));
    }

    [Fact]
    public void OnlyTheExactPasswordWorks()
    {
        Assert.True(AdminAccess.PasswordMatches("ClaudeIsGreat"));
        Assert.False(AdminAccess.PasswordMatches("claudeisgreat"));
        Assert.False(AdminAccess.PasswordMatches("ClaudeIsGreat "));
        Assert.False(AdminAccess.PasswordMatches(""));
        Assert.False(AdminAccess.PasswordMatches(null));
    }

    [Fact]
    public void AdminUnlockOpensEndlessModeAndIsNeverSaved()
    {
        using var dir = new TempDir();
        var save = new SaveData { AdminUnlock = true };
        Assert.True(save.EndlessUnlocked);
        Assert.Equal(0, save.FlaglessLevelCount);

        save.Save(dir.File("admin.dat"));
        Assert.False(SaveData.Load(dir.File("admin.dat")).EndlessUnlocked);
    }

    [Fact]
    public void AdminIsNotListedAndCannotBeCreatedAsAPlayer()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        new SaveData().Save(store.PathFor("admin"));

        Assert.Empty(store.List());
        Assert.Throws<ArgumentException>(() => store.Create("Admin"));
    }

    [Fact]
    public void StrayFilesInTheProfileFolderAreNotListed()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Create("Alice");
        File.WriteAllText(dir.File("p_.dat"), "");
        File.WriteAllText(dir.File("p_a.b.dat"), "");
        File.WriteAllText(dir.File("p_ spaced.dat"), "");

        Assert.Equal(new[] { "Alice" }, store.List());
    }
}
