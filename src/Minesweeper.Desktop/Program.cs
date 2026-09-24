namespace Minesweeper.Desktop;

static class Program
{
    public static readonly string SavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minesweeper2", "save.json");

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        while (true)
        {
            var start = new StartForm();
            Application.Run(start);
            if (start.Selected is not { } mode) return;

            switch (mode)
            {
                default:
                    var game = new MainForm(mode);
                    Application.Run(game);
                    if (!game.ReturnToMenu) return;
                    break;
            }
        }
    }
}
