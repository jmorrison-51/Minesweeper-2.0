using System.Diagnostics;
using Minesweeper.Core;

namespace Minesweeper.Desktop;

/// <summary>Window for Endless Mode. Counters show rows cleared (left) and seconds survived (right).</summary>
public sealed class EndlessForm : Form, ISavesProgress
{
    private static readonly Color Gray = Color.FromArgb(192, 192, 192);

    private readonly string _savePath = Program.SavePath;
    private readonly SaveData _save;
    private readonly MenuStrip _menu = new();
    private readonly LedDisplay _rowsDisplay = new();
    private readonly LedDisplay _timerDisplay = new();
    private readonly FaceButton _faceButton = new();
    private readonly EndlessControl _field = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 30 };

    private EndlessBoard _board = null!;
    private long _lastTick;
    private bool _recorded;

    /// <summary>True when the form was closed to go back to the start screen rather than to quit.</summary>
    public bool ReturnToMenu { get; private set; }

    public EndlessForm()
    {
        Text = "Endless Mode";
        BackColor = Gray;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        Icon = SystemIcons.Application;

        _save = Program.LoadSave();

        var game = new ToolStripMenuItem("&Game");
        game.DropDownItems.Add(MenuItem("&New", Keys.F2, NewGame));
        // Menu shortcuts need a modifier, so P and Esc are handled in ProcessCmdKey and only shown here.
        game.DropDownItems.Add(new ToolStripMenuItem("&Pause", null, (_, _) => TogglePause()) { ShortcutKeyDisplayString = "P" });
        game.DropDownItems.Add(new ToolStripSeparator());
        game.DropDownItems.Add(MenuItem("Best &Run...", Keys.None, ShowBestRun));
        game.DropDownItems.Add(new ToolStripSeparator());
        game.DropDownItems.Add(MenuItem("Main &Menu", Keys.None, () =>
        {
            ReturnToMenu = true;
            Close();
        }));
        game.DropDownItems.Add(MenuItem("E&xit", Keys.None, Close));
        _menu.Items.Add(game);

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(MenuItem("&Controls...", Keys.F1, () => ControlsHelp.Show(this, endless: true)));
        _menu.Items.Add(help);

        Controls.Add(_menu);
        MainMenuStrip = _menu;
        Controls.Add(_rowsDisplay);
        Controls.Add(_timerDisplay);
        Controls.Add(_faceButton);
        Controls.Add(_field);

        _field.Changed += (_, _) => OnFieldChanged();
        _faceButton.Click += (_, _) => NewGame();
        _clock.Tick += (_, _) => Tick();

        // Leaving mid-run still counts the run so far.
        FormClosing += (_, _) =>
        {
            RecordRunSoFar();
            SaveProgress();
        };
        // Switching to another window (or a dialog) pauses the run; the tiles are hidden while paused.
        Deactivate += (_, _) =>
        {
            if (_board.Status == GameStatus.Playing && !_field.Paused) TogglePause();
        };
        // The timer is not owned by the form, so it would keep running the abandoned board in the next screen.
        FormClosed += (_, _) => _clock.Dispose();
        DpiChanged += (_, _) => Fit();

        NewGame();
        _clock.Start();
    }

    private static ToolStripMenuItem MenuItem(string text, Keys shortcut, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void ShowBestRun()
    {
        string text = _save.EndlessBestMs > 0
            ? $"Longest run: {FormatTime(_save.EndlessBestMs)}\nMost rows cleared: {_save.EndlessBestRows}"
            : "No runs yet.";
        MessageBox.Show(this, text, "Best Run", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string FormatTime(long ms) => $"{ms / 60000}:{ms / 1000 % 60:00}";

    public void SaveProgress() => Program.Save(_save, _savePath, this);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (base.ProcessCmdKey(ref msg, keyData)) return true;
        if (keyData is Keys.P or Keys.Escape)
        {
            TogglePause();
            return true;
        }
        return _field.HandleKey(keyData);
    }

    private void TogglePause()
    {
        if (_board.Status != GameStatus.Playing) return;
        _field.Paused = !_field.Paused;
        _lastTick = Stopwatch.GetTimestamp();
        Text = _field.Paused ? "Endless Mode - Paused" : "Endless Mode";
    }

    /// <summary>Records a run that is being abandoned (new game, closing). Returns true if it was a new longest run.</summary>
    private bool RecordRunSoFar()
    {
        if (_recorded || _board.Status != GameStatus.Playing) return false;
        _recorded = true;
        return _save.RecordEndlessRun((long)(_board.Elapsed * 1000), _board.RowsCleared);
    }

    private void NewGame()
    {
        if (_board != null && RecordRunSoFar())
        {
            SaveProgress();
            MessageBox.Show(this, $"The run you left lasted {FormatTime(_save.EndlessBestMs)} - your longest yet!",
                "Best Run", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        _board = new EndlessBoard();
        Text = "Endless Mode";
        _recorded = false;
        _lastTick = Stopwatch.GetTimestamp();
        _field.Board = _board;
        Fit();
        UpdateHeader();
        UpdateFace();
    }

    // Tiles start at the chosen size and shrink only if the window would not fit the screen.
    private void Fit()
    {
        var area = Screen.FromControl(this).WorkingArea;
        int tileSize = SaveData.TileSizes.Contains(_save.TileSize) ? _save.TileSize : 48;
        int cell = LogicalToDeviceUnits(tileSize);
        while (true)
        {
            _field.ApplyScale(cell);
            LayoutControls();
            if ((Width <= area.Width && Height <= area.Height) || cell <= LogicalToDeviceUnits(16)) break;
            cell -= 2;
        }
        Location = new Point(
            Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
            Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
    }

    private void LayoutControls()
    {
        int pad = LogicalToDeviceUnits(10);
        int inset = LogicalToDeviceUnits(6);
        int headerHeight = LogicalToDeviceUnits(40);
        int ledWidth = LogicalToDeviceUnits(62);
        int face = LogicalToDeviceUnits(32);

        int fieldW = _field.Width;
        int headerTop = _menu.Height + pad;
        int headerBoxHeight = headerHeight + 2 * inset;

        _rowsDisplay.SetBounds(pad + inset, headerTop + inset + (headerHeight - LogicalToDeviceUnits(30)) / 2, ledWidth, LogicalToDeviceUnits(30));
        _timerDisplay.SetBounds(pad + fieldW - inset - ledWidth, _rowsDisplay.Top, ledWidth, LogicalToDeviceUnits(30));
        _faceButton.SetBounds(pad + (fieldW - face) / 2, headerTop + inset + (headerHeight - face) / 2, face, face);

        _field.Location = new Point(pad, headerTop + headerBoxHeight + pad);
        ClientSize = new Size(fieldW + 2 * pad, _field.Bottom + pad);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_field.Board == null) return;

        int pad = LogicalToDeviceUnits(10);
        int inset = LogicalToDeviceUnits(6);
        int headerBoxHeight = LogicalToDeviceUnits(40) + 2 * inset;

        var header = new Rectangle(pad, _menu.Height + pad, _field.Width, headerBoxHeight);
        var field = new Rectangle(_field.Left, _field.Top, _field.Width, _field.Height);
        int b = LogicalToDeviceUnits(3);
        ControlPaint.DrawBorder3D(e.Graphics, Rectangle.Inflate(header, b, b), Border3DStyle.SunkenOuter);
        ControlPaint.DrawBorder3D(e.Graphics, Rectangle.Inflate(field, b, b), Border3DStyle.Sunken);
    }

    private void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Min(0.25, (now - _lastTick) / (double)Stopwatch.Frequency);
        _lastTick = now;

        // A minimized window pauses the run: the player cannot see the rows, so nothing is gained.
        if (_board.Status == GameStatus.Playing && !_field.Paused && WindowState != FormWindowState.Minimized)
        {
            _board.Advance(dt);
            UpdateHeader();
            _field.Invalidate();
            if (_board.Status == GameStatus.Lost) OnLost();
        }
    }

    private void OnFieldChanged()
    {
        UpdateHeader();
        UpdateFace();
        if (_board.Status == GameStatus.Lost) OnLost();
    }

    private void OnLost()
    {
        UpdateFace();
        _field.Invalidate();
        if (_recorded) return;
        _recorded = true;

        long ms = (long)(_board.Elapsed * 1000);
        int rows = _board.RowsCleared;
        bool newBest = _save.RecordEndlessRun(ms, rows);
        SaveProgress();

        string why = _board.LossReason == LossReason.Mine ? "You hit a mine!" : "The rows reached the bottom!";
        string message = $"{why}\n\nYou lasted {FormatTime(ms)} and cleared {rows} row{(rows == 1 ? "" : "s")}." +
                         (newBest ? "\nThat is your longest run yet!" : "");
        BeginInvoke(() => MessageBox.Show(this, message, "Game Over", MessageBoxButtons.OK, MessageBoxIcon.Information));
    }

    private void UpdateHeader()
    {
        _rowsDisplay.Value = _board.RowsCleared;
        _timerDisplay.Value = (int)_board.Elapsed;
    }

    private void UpdateFace()
    {
        _faceButton.Face = _board.Status == GameStatus.Lost ? Face.Dead : Face.Smile;
    }
}
