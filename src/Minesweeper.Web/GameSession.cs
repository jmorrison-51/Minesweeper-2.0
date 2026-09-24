using Microsoft.JSInterop;
using Minesweeper.Core;

namespace Minesweeper.Web;

public enum Screen
{
    Profiles,
    Start,
    Board,
    Endless,
}

public enum GameMode
{
    Classic,
    Hex,
    HexChallenge,
    Endless,
}

/// <summary>
/// The signed-in player and which screen is showing. The web version is one page, so a single
/// <see cref="SaveData"/> is shared by every screen (the desktop gives each window its own copy).
/// </summary>
public sealed class GameSession
{
    public GameSession(IJSRuntime js)
    {
        var inProcess = (IJSInProcessRuntime)js;
        StorageAvailable = BrowserStorage.IsAvailable(inProcess);
        IKeyValueStore store = StorageAvailable ? new BrowserStorage(inProcess) : new MemoryKeyValueStore();
        Profiles = new KeyValueProfileStore(store);
    }

    public KeyValueProfileStore Profiles { get; }

    /// <summary>False when the browser blocks storage: players and scores last only until the page closes.</summary>
    public bool StorageAvailable { get; }

    public string CurrentProfile { get; private set; } = "";
    public bool IsAdmin { get; private set; }
    public SaveData Save { get; private set; } = new();

    public Screen Screen { get; private set; } = Screen.Profiles;
    public GameMode Mode { get; private set; }

    /// <summary>Set when a save failed, for the warning banner. Cleared by the next save that works.</summary>
    public string? SaveError { get; private set; }

    public bool SaveWarningDismissed { get; set; }

    /// <summary>Raised when the screen, player or save warning changes, so the page re-renders.</summary>
    public event Action? Changed;

    public void SignIn(string name, bool isAdmin)
    {
        CurrentProfile = isAdmin ? AdminAccess.UserName : name;
        IsAdmin = isAdmin;
        Save = Profiles.Load(CurrentProfile);
        Save.AdminUnlock = isAdmin;
        if (!isAdmin) Profiles.LastProfile = name;
        Show(Screen.Start);
    }

    public void SwitchPlayer() => Show(Screen.Profiles);

    public void Play(GameMode mode)
    {
        Mode = mode;
        Show(mode == GameMode.Endless ? Screen.Endless : Screen.Board);
    }

    public void ToMenu() => Show(Screen.Start);

    private void Show(Screen screen)
    {
        Screen = screen;
        Changed?.Invoke();
    }

    /// <summary>Writes the current player's save. A failure shows a warning banner until a save works again.</summary>
    public void Persist()
    {
        if (CurrentProfile.Length == 0) return;
        if (Profiles.TrySave(CurrentProfile, Save, out string error))
        {
            if (SaveError == null) return;
            SaveError = null;
        }
        else
        {
            if (SaveError != null) return;
            SaveError = error;
            SaveWarningDismissed = false;
        }
        Changed?.Invoke();
    }
}
