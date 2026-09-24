using Minesweeper.Core;

namespace Minesweeper.Core.Tests;

public class KeyValueProfileTests
{
    [Fact]
    public void SignedTextRoundTripsAndRejectsEdits()
    {
        var save = new SaveData { ChallengeUnlocked = 7 };
        save.TrySetBestTime("Expert", 88_000);
        string text = save.ToSignedText();

        Assert.True(SaveData.TryFromSignedText(text, out var loaded));
        Assert.Equal(7, loaded.ChallengeUnlocked);
        Assert.Equal(88_000, loaded.BestTimesMs["Expert"]);

        Assert.False(SaveData.TryFromSignedText(text.Replace("88000", "1000"), out var edited));
        Assert.Equal(1, edited.ChallengeUnlocked);
        Assert.False(SaveData.TryFromSignedText("{\"ChallengeUnlocked\":21}", out _));
        Assert.False(SaveData.TryFromSignedText(null, out _));
    }

    [Fact]
    public void PlayersKeepSeparateSavesAndNamesAreUniqueIgnoringCase()
    {
        var store = new KeyValueProfileStore(new MemoryKeyValueStore());
        store.Create("Alice");
        store.Create("bob");

        var alice = store.Load("alice");
        alice.ChallengeUnlocked = 5;
        Assert.True(store.TrySave("Alice", alice, out _));

        Assert.Equal(5, store.Load("ALICE").ChallengeUnlocked);
        Assert.Equal(1, store.Load("bob").ChallengeUnlocked);
        Assert.Equal(new[] { "Alice", "bob" }, store.List());
        Assert.Throws<InvalidOperationException>(() => store.Create("ALICE"));
        Assert.Throws<ArgumentException>(() => store.Create("bad/name"));
        Assert.Equal("Alice", store.Find(" alice "));
    }

    [Fact]
    public void AdminIsReservedAndNeverListed()
    {
        var store = new KeyValueProfileStore(new MemoryKeyValueStore());
        Assert.Throws<ArgumentException>(() => store.Create("Admin"));

        var admin = store.Load(AdminAccess.UserName);
        admin.EndlessBestMs = 60_000;
        store.TrySave(AdminAccess.UserName, admin, out _);

        Assert.Empty(store.List());
        Assert.Equal(60_000, store.Load(AdminAccess.UserName).EndlessBestMs);
    }

    [Fact]
    public void DeleteRemovesThePlayerAndClearsLastProfile()
    {
        var store = new KeyValueProfileStore(new MemoryKeyValueStore());
        store.Create("Alice");
        store.LastProfile = "Alice";
        Assert.Equal("Alice", store.LastProfile);

        store.Delete("alice");

        Assert.Empty(store.List());
        Assert.Null(store.LastProfile);
    }

    [Fact]
    public void AnEditedSaveIsSetAsideAndThePlayerStartsFresh()
    {
        var kv = new MemoryKeyValueStore();
        var store = new KeyValueProfileStore(kv);
        store.Create("Alice");
        var save = store.Load("Alice");
        save.ChallengeUnlocked = 4;
        store.TrySave("Alice", save, out _);

        kv.TrySet("ms2.player.alice", kv.Get("ms2.player.alice")!.Replace("\"ChallengeUnlocked\":4", "\"ChallengeUnlocked\":21"), out _);

        Assert.Equal(1, store.Load("Alice").ChallengeUnlocked);
        Assert.NotNull(kv.Get("ms2.invalid.alice"));
        Assert.Equal(new[] { "Alice" }, store.List());
    }

    [Fact]
    public void AFullStoreMakesCreateFail()
    {
        var store = new KeyValueProfileStore(new FullStore());
        Assert.Throws<IOException>(() => store.Create("Alice"));
    }

    private sealed class FullStore : IKeyValueStore
    {
        public IEnumerable<string> Keys => Array.Empty<string>();
        public string? Get(string key) => null;
        public bool TrySet(string key, string value, out string error)
        {
            error = "Storage is full.";
            return false;
        }
        public void Remove(string key) { }
    }
}
