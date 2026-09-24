using Minesweeper.Core;

namespace Minesweeper.Desktop;

/// <summary>A window holding a player's save, so the error handler can save it before reporting a problem.</summary>
interface ISavesProgress
{
    void SaveProgress();
}

static class Program
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minesweeper2");

    public static readonly ProfileStore Profiles = new(Path.Combine(DataDirectory, "profiles"));

    /// <summary>The signed-in player. Every screen after the picker reads and writes this player's save.</summary>
    public static string CurrentProfile { get; private set; } = "";

    /// <summary>True when signed in through the admin login (Endless Mode unlocked for testing).</summary>
    public static bool IsAdmin { get; private set; }

    public static string SavePath => Profiles.PathFor(IsAdmin ? AdminAccess.UserName : CurrentProfile);

    // Each screen loads the save itself, so the notice is shown once per sign-in, not once per screen.
    private static bool _loadNoticeShown;

    public static SaveData LoadSave()
    {
        var save = SaveData.Load(SavePath);
        save.AdminUnlock = IsAdmin;
        if (save.LoadNotice != null && !_loadNoticeShown)
        {
            _loadNoticeShown = true;
            MessageBox.Show(save.LoadNotice, "Minesweeper 2.0 - Save File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return save;
    }

    private static bool _saveWarningShown;

    /// <summary>
    /// Saves and warns the player if it failed. The warning shows once until a save works again, so a
    /// full disk does not raise a dialog on every click.
    /// </summary>
    public static void Save(SaveData save, string path, IWin32Window? owner = null)
    {
        if (save.TrySave(path, out string error))
        {
            _saveWarningShown = false;
            return;
        }
        if (_saveWarningShown) return;
        _saveWarningShown = true;
        MessageBox.Show(owner,
            $"Your progress could not be saved.\n\n{error}\n\nCheck that the disk is not full and that nothing else is using the file. " +
            "The game will keep trying each time it saves.",
            "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static string CrashLogPath => Path.Combine(DataDirectory, "crash.log");

    // Unexpected errors: save what we can, write the details to crash.log and let the player carry on or quit.
    private static void OnUiException(Exception ex)
    {
        SaveOpenForms();
        LogCrash(ex);
        var answer = MessageBox.Show(
            "Something went wrong, sorry. Your progress has been saved.\n\n" +
            $"{ex.Message}\n\nDetails were written to:\n{CrashLogPath}\n\nKeep playing? (No closes the game.)",
            "Minesweeper 2.0 - Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
        if (answer == DialogResult.No) Environment.Exit(1);
    }

    private static void SaveOpenForms()
    {
        foreach (var form in Application.OpenForms.OfType<ISavesProgress>().ToList())
        {
            try { form.SaveProgress(); }
            catch (Exception) { } // Already handling an error; a failed save must not hide it.
        }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch (Exception) { }
    }

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => OnUiException(e.Exception);
        // Errors off the UI thread end the process and cannot safely touch the windows; just log them.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception ex) return;
            LogCrash(ex);
        };

        // Each window keeps its own copy of the save and writes it back on close, so two copies of the game
        // running at once would overwrite each other's progress.
        using var instance = new Mutex(true, @"Local\Minesweeper2.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Minesweeper 2.0 is already running.", "Minesweeper 2.0",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Upgrade from the old single save file, if there is one.
        Profiles.MigrateLegacy(Path.Combine(DataDirectory, "save.json"));

        bool signedIn = false;
        while (true)
        {
            if (!signedIn)
            {
                var picker = new ProfileForm(Profiles);
                Application.Run(picker);
                if (picker.Result is not { } chosen) return;

                CurrentProfile = chosen.Name;
                IsAdmin = chosen.IsAdmin;
                _loadNoticeShown = false;
                if (!IsAdmin) Profiles.LastProfile = chosen.Name;
                signedIn = true;
            }

            var start = new StartForm();
            Application.Run(start);
            if (start.SwitchPlayer)
            {
                signedIn = false;
                continue;
            }
            if (start.Selected is not { } mode) return;

            switch (mode)
            {
                case GameMode.Endless:
                    var endless = new EndlessForm();
                    Application.Run(endless);
                    if (!endless.ReturnToMenu) return;
                    break;

                default:
                    var game = new MainForm(mode);
                    Application.Run(game);
                    if (!game.ReturnToMenu) return;
                    break;
            }
        }
    }
}
