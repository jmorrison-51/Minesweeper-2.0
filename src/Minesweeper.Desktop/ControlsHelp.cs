namespace Minesweeper.Desktop;

/// <summary>The Help > Controls box: mouse and keyboard controls for the board windows.</summary>
internal static class ControlsHelp
{
    public static void Show(IWin32Window owner, bool endless)
    {
        string text = endless
            ? "Mouse\n" +
              "  Left click\tReveal a tile (acts as soon as you press)\n\n" +
              "Keyboard\n" +
              "  Arrows or WASD\tMove the cursor (it rides along with its row)\n" +
              "  Space or Enter\tReveal the tile under the cursor\n" +
              "  P or Esc\tPause / carry on (the field is hidden while paused)\n" +
              "  F2\t\tNew run (the current run still counts)\n\n" +
              "The run also pauses when you switch to another window."
            : "Mouse\n" +
              "  Left click\tReveal a tile\n" +
              "  Right click\tPlace or remove a flag\n" +
              "  Both buttons or middle click on a number\n" +
              "\t\tReveal its neighbors when enough flags are placed\n\n" +
              "Keyboard\n" +
              "  Arrows or WASD\tMove the cursor\n" +
              "  Space or Enter\tReveal the tile, or reveal the neighbors of a number\n" +
              "  F\t\tPlace or remove a flag\n" +
              "  F2\t\tNew game / retry level";

        MessageBox.Show(owner, text, "Controls", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
