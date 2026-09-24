namespace Minesweeper.Desktop;

static class Program
{
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
                case GameMode.Classic:
                    var game = new MainForm();
                    Application.Run(game);
                    if (!game.ReturnToMenu) return;
                    break;

                case GameMode.Hex:
                case GameMode.HexChallenge:
                    MessageBox.Show(
                        mode == GameMode.Hex ? "Hex Minesweeper is coming soon." : "Hex Challenge is coming soon.",
                        "Minesweeper 2.0", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
        }
    }
}
