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

public enum TabState
{
    Checking,
    Playing,
    Elsewhere,
}

/// <summary>
/// The signed-in player and which screen is showing. The web version is one page, so a single
/// <see cref="SaveData"/> is shared by every screen (the desktop gives each window its own copy).
/// </summary>
public sealed class GameSession
{
    private readonly IJSInProcessRuntime _js;
    private DotNetObjectReference<GameSession>? _self;

    public GameSession(IJSRuntime js)
    {
        var inProcess = _js = (IJSInProcessRuntime)js;
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

    /// <summary>Set when the player's saved data was damaged and could not be used, so the page can say so.</summary>
    public string? LoadNotice { get; private set; }

    public bool LoadNoticeDismissed { get; set; }

    private void ShowLoadNotice()
    {
        LoadNotice = Save.LoadNotice;
        LoadNoticeDismissed = false;
    }

    private bool _persistAsked;

    // Once per visit, when someone signs in (so it happens when there is something worth keeping): ask the
    // browser not to clear the saved data. A page with an older copy of ms2.js cannot answer; that is fine.
    private async Task AskForPersistentStorage()
    {
        if (_persistAsked || !StorageAvailable) return;
        _persistAsked = true;
        try
        {
            await _js.InvokeAsync<bool>("ms2.storage.persist");
        }
        catch (JSException) { }
    }

    /// <summary>Offers every player in this browser as a backup file download.</summary>
    public bool DownloadBackup()
    {
        if (Profiles.List().Count == 0) return false;
        _js.InvokeVoid("ms2.file.download", $"minesweeper-2-backup-{DateTime.Now:yyyy-MM-dd}.txt", Profiles.ExportBackup());
        return true;
    }

    /// <summary>Raised when the screen, player or save warning changes, so the page re-renders.</summary>
    public event Action? Changed;

    /// <summary>
    /// Whether this tab is the one playing. Only one tab plays at a time (like the desktop's single instance),
    /// because each tab holds its own <see cref="SaveData"/> and the last to save would wipe out the other.
    /// </summary>
    public TabState Tab { get; private set; } = TabState.Checking;

    /// <summary>Raised just before this tab hands over to another, so a screen can record progress it has not saved yet.</summary>
    public event Action? Yielding;

    /// <summary>Claims the playing tab, unless another tab has it. Storage that is blocked is not shared, so needs no guard.</summary>
    public async Task StartAsync()
    {
        if (!StorageAvailable)
        {
            Tab = TabState.Playing;
            return;
        }
        _self = DotNetObjectReference.Create(this);
        try
        {
            Tab = await _js.InvokeAsync<bool>("ms2.tab.start", _self) ? TabState.Playing : TabState.Elsewhere;
        }
        catch (JSException)
        {
            // The guard is a safety net. A page with an older ms2.js (cached from before an update) cannot run it,
            // and that must not stop the game from starting.
            Tab = TabState.Playing;
        }
    }

    /// <summary>"Play here": the other tab saves and stops, then this one reloads the save it left.</summary>
    public async Task TakeOverAsync()
    {
        if (Tab != TabState.Elsewhere) return;
        Tab = TabState.Checking;
        Changed?.Invoke();
        try
        {
            await _js.InvokeVoidAsync("ms2.tab.takeOver");
        }
        catch (JSException) { }
        BecomePlaying();
    }

    /// <summary>The playing tab was closed (or handed over): this tab starts by itself.</summary>
    [JSInvokable]
    public void OnLockFreed()
    {
        if (Tab == TabState.Elsewhere) BecomePlaying();
    }

    // This tab is now the one playing. Its copy of the save is old, so read the current one.
    private void BecomePlaying()
    {
        Tab = TabState.Playing;
        SaveError = null;
        string? player = IsAdmin ? CurrentProfile : Profiles.Find(CurrentProfile);
        if (CurrentProfile.Length > 0 && player != null)
        {
            CurrentProfile = player;
            Save = Profiles.Load(CurrentProfile);
            ShowLoadNotice();
            Save.AdminUnlock = IsAdmin;
            Show(Screen.Start);
        }
        else
        {
            CurrentProfile = "";
            IsAdmin = false;
            Show(Screen.Profiles); // no one was signed in, or the other tab deleted this player
        }
    }

    /// <summary>Another tab asked to play: save now, while this tab still may, then stop.</summary>
    [JSInvokable]
    public void OnYield()
    {
        Yielding?.Invoke();
        Persist();
        Tab = TabState.Elsewhere;
        Changed?.Invoke();
    }

    /// <summary>Another tab took over without waiting for this one: its save may be newer, so stop without saving.</summary>
    [JSInvokable]
    public void OnTakenOver()
    {
        Tab = TabState.Elsewhere;
        Changed?.Invoke();
    }

    public void SignIn(string name, bool isAdmin)
    {
        CurrentProfile = isAdmin ? AdminAccess.UserName : name;
        IsAdmin = isAdmin;
        Save = Profiles.Load(CurrentProfile);
        ShowLoadNotice();
        Save.AdminUnlock = isAdmin;
        if (!isAdmin) Profiles.LastProfile = name;
        _ = AskForPersistentStorage();
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
        // A tab that is not playing may hold an old copy (screens also save as they close when it hands over).
        if (CurrentProfile.Length == 0 || Tab != TabState.Playing) return;
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
