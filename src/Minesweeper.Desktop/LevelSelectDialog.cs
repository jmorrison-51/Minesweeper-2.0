using Minesweeper.Core;

namespace Minesweeper.Desktop;

/// <summary>Grid of the 20 Hex Challenge levels. Levels not yet unlocked are disabled.</summary>
public sealed class LevelSelectDialog : Form
{
    private const int Columns = 5;

    public int SelectedLevel { get; private set; }

    public LevelSelectDialog(SaveData save, int current)
    {
        Text = "Choose Level";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var tips = new ToolTip();
        const int buttonWidth = 78, buttonHeight = 52, margin = 4, padding = 12, footerHeight = 44;
        int rows = (ChallengeLevel.Count + Columns - 1) / Columns;
        var grid = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(padding - margin),
            Height = rows * (buttonHeight + 2 * margin) + 2 * (padding - margin),
        };
        ClientSize = new Size(Columns * (buttonWidth + 2 * margin) + 2 * (padding - margin),
            grid.Height + footerHeight);

        for (int level = 1; level <= ChallengeLevel.Count; level++)
        {
            int n = level;
            string best = save.BestTimesMs.TryGetValue(SaveData.ChallengeKey(n), out long ms) ? $"\n{ms / 1000.0:0.0}s" : "";
            bool flagless = save.IsFlaglessCleared(n);
            var button = new Button
            {
                Text = n + (flagless ? " ★" : "") + best,
                Size = new Size(buttonWidth, buttonHeight),
                Margin = new Padding(margin),
                Enabled = n <= save.ChallengePlayable,
                Font = new Font(Font, n == current ? FontStyle.Bold : FontStyle.Regular),
            };
            tips.SetToolTip(button, n <= save.ChallengePlayable
                ? $"Level {n}: {ChallengeLevel.Get(n).Summary}" + (flagless ? "\nCleared without flags" : "")
                : $"Level {n}: locked. Clear level {n - 1} first.");
            button.Click += (_, _) =>
            {
                SelectedLevel = n;
                DialogResult = DialogResult.OK;
            };
            grid.Controls.Add(button);
        }

        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = footerHeight,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(padding - margin, 6, padding - margin, 0),
        };
        footer.Controls.Add(cancel);
        footer.Controls.Add(new Label
        {
            Text = $"★ cleared without flags: {save.FlaglessLevelCount} of {ChallengeLevel.Count}",
            AutoSize = true,
            Margin = new Padding(8, 8, 0, 0),
        });

        Controls.Add(grid);
        Controls.Add(footer);
        CancelButton = cancel;
    }
}
