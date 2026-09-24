using Minesweeper.Core;

namespace Minesweeper.Desktop;

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

    public static SaveData LoadSave()
    {
        var save = SaveData.Load(SavePath);
        save.AdminUnlock = IsAdmin;
        return save;
    }

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

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
