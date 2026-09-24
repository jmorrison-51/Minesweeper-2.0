using Minesweeper.Core;

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
                case GameMode.Hex:
                    var game = new MainForm(mode == GameMode.Hex ? BoardShape.Hex : BoardShape.Square);
                    Application.Run(game);
                    if (!game.ReturnToMenu) return;
                    break;

                case GameMode.HexChallenge:
                    MessageBox.Show("Hex Challenge is coming soon.",
                        "Minesweeper 2.0", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
        }
    }
}
