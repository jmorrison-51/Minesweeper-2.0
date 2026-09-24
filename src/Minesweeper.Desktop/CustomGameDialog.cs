using Minesweeper.Core;

namespace Minesweeper.Desktop;

public sealed class CustomGameDialog : Form
{
    private readonly NumericUpDown _columns = new() { Minimum = 8, Maximum = 50 };
    private readonly NumericUpDown _rows = new() { Minimum = 8, Maximum = 30 };
    private readonly NumericUpDown _mines = new() { Minimum = 1, Maximum = 1000 };

    public CustomGameDialog(int columns, int rows, int mines)
    {
        Text = "Custom Field";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        _columns.Value = Math.Clamp(columns, 8, 50);
        _rows.Value = Math.Clamp(rows, 8, 30);
        _mines.Value = Math.Clamp(mines, 1, 1000);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(12),
            Dock = DockStyle.Fill,
        };
        AddRow(layout, "Width (8-50):", _columns);
        AddRow(layout, "Height (8-30):", _rows);
        AddRow(layout, "Mines:", _mines);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public Difficulty Result => Difficulty.Custom((int)_columns.Value, (int)_rows.Value, (int)_mines.Value);

    private static void AddRow(TableLayoutPanel layout, string label, NumericUpDown input)
    {
        int row = layout.RowCount++;
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 6) }, 0, row);
        layout.Controls.Add(input, 1, row);
    }
}
