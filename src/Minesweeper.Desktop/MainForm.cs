using System.Diagnostics;
using Minesweeper.Core;

namespace Minesweeper.Desktop;

public sealed class MainForm : Form
{
    private static readonly Color Gray = Color.FromArgb(192, 192, 192);

    private readonly string _savePath = Program.SavePath;

    private readonly SaveData _save;
    private readonly MenuStrip _menu = new();
    private readonly LedDisplay _mineCounter = new();
    private readonly LedDisplay _timerDisplay = new();
    private readonly FaceButton _faceButton = new();
    private readonly BoardControl _boardControl = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 100 };
    private readonly Stopwatch _stopwatch = new();
    private readonly Dictionary<string, ToolStripMenuItem> _difficultyItems = new();

    private Difficulty _difficulty;
    private Board _board = null!;
    private bool _bestTimeRecorded;

    /// <summary>True when the form was closed to go back to the start screen rather than to quit.</summary>
    public bool ReturnToMenu { get; private set; }

    private readonly BoardShape _shape;
    private readonly bool _challenge;
    private readonly ToolStripMenuItem _nextLevelItem = new("&Next Level");
    private int _level = 1;

    public MainForm(GameMode mode = GameMode.Classic)
    {
        _shape = mode == GameMode.Classic ? BoardShape.Square : BoardShape.Hex;
        _challenge = mode == GameMode.HexChallenge;
        Text = mode switch
        {
            GameMode.Hex => "Hex Minesweeper",
            GameMode.HexChallenge => "Hex Challenge",
            _ => "Minesweeper",
        };
        BackColor = Gray;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        Icon = SystemIcons.Application;

        _save = Program.LoadSave();
        _difficulty = (_shape == BoardShape.Hex ? _save.LastHexDifficulty : _save.LastDifficulty) switch
        {
            "Intermediate" => Difficulty.Intermediate,
            "Expert" => Difficulty.Expert,
            "Custom" => Difficulty.Custom(_save.CustomColumns, _save.CustomRows, _save.CustomMines),
            _ => Difficulty.Beginner,
        };
        _difficulty = _difficulty.WithShape(_shape);
        if (_challenge)
        {
            _level = Math.Clamp(_save.ChallengeCurrent, 1, _save.ChallengePlayable);
            _difficulty = ChallengeLevel.Get(_level).Difficulty;
        }

        BuildMenu();
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        Controls.Add(_mineCounter);
        Controls.Add(_timerDisplay);
        Controls.Add(_faceButton);
        Controls.Add(_boardControl);

        _boardControl.Changed += (_, _) => OnBoardChanged();
        _boardControl.PressingChanged += (_, _) => UpdateFace();
        _faceButton.Click += (_, _) => NewGame();
        _clock.Tick += (_, _) => UpdateTimer();

        FormClosing += (_, _) => _save.Save(_savePath);
        DpiChanged += (_, _) => NewGame();

        NewGame();
    }

    private void BuildMenu()
    {
        var game = new ToolStripMenuItem("&Game");
        if (_challenge)
        {
            game.DropDownItems.Add(MenuItem("&Retry Level", Keys.F2, NewGame));
            _nextLevelItem.ShortcutKeys = Keys.F3;
            _nextLevelItem.Click += (_, _) => GoToLevel(_level + 1);
            game.DropDownItems.Add(_nextLevelItem);
            game.DropDownItems.Add(MenuItem("Choose &Level...", Keys.F4, ShowLevelSelect));
        }
        else
        {
            game.DropDownItems.Add(MenuItem("&New", Keys.F2, NewGame));
            game.DropDownItems.Add(new ToolStripSeparator());
            foreach (var d in new[] { Difficulty.Beginner, Difficulty.Intermediate, Difficulty.Expert })
            {
                var item = MenuItem($"&{d.Name}", Keys.None, () => SetDifficulty(d.WithShape(_shape)));
                _difficultyItems[d.Name] = item;
                game.DropDownItems.Add(item);
            }
            var custom = MenuItem("&Custom...", Keys.None, ShowCustomDialog);
            _difficultyItems["Custom"] = custom;
            game.DropDownItems.Add(custom);
            game.DropDownItems.Add(new ToolStripSeparator());
            game.DropDownItems.Add(MenuItem("Best &Times...", Keys.None, ShowBestTimes));
        }
        game.DropDownItems.Add(new ToolStripSeparator());
        game.DropDownItems.Add(MenuItem("Main &Menu", Keys.None, () =>
        {
            ReturnToMenu = true;
            Close();
        }));
        game.DropDownItems.Add(MenuItem("E&xit", Keys.None, Close));
        _menu.Items.Add(game);
    }

    private static ToolStripMenuItem MenuItem(string text, Keys shortcut, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void SetDifficulty(Difficulty difficulty)
    {
        _difficulty = difficulty;
        NewGame();
    }

    private void ShowCustomDialog()
    {
        using var dialog = new CustomGameDialog(_save.CustomColumns, _save.CustomRows, _save.CustomMines);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var result = dialog.Result;
        _save.CustomColumns = result.Columns;
        _save.CustomRows = result.Rows;
        _save.CustomMines = result.Mines;
        SetDifficulty(result.WithShape(_shape));
    }

    private void GoToLevel(int level)
    {
        _level = Math.Clamp(level, 1, _save.ChallengePlayable);
        NewGame();
    }

    private void ShowLevelSelect()
    {
        using var dialog = new LevelSelectDialog(_save, _level);
        if (dialog.ShowDialog(this) == DialogResult.OK) GoToLevel(dialog.SelectedLevel);
    }

    private void ShowBestTimes()
    {
        string Line(Difficulty d) =>
            _save.BestTimesMs.TryGetValue(d.WithShape(_shape).Key, out long ms) ? $"{ms / 1000.0:0.00} seconds" : "--";

        MessageBox.Show(this,
            $"Beginner:\t\t{Line(Difficulty.Beginner)}\n" +
            $"Intermediate:\t{Line(Difficulty.Intermediate)}\n" +
            $"Expert:\t\t{Line(Difficulty.Expert)}",
            "Best Times", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void NewGame()
    {
        _clock.Stop();
        _stopwatch.Reset();
        _bestTimeRecorded = false;

        if (_challenge)
        {
            var level = ChallengeLevel.Get(_level);
            _difficulty = level.Difficulty;
            _board = new Board(level.Difficulty, null, level.Rules);
            _save.ChallengeCurrent = _level;
            Text = $"Hex Challenge - Level {_level} of {ChallengeLevel.Count}";
            _nextLevelItem.Enabled = _level < _save.ChallengePlayable;
        }
        else
        {
            _board = new Board(_difficulty);
        }
        _boardControl.Board = _board;

        // Tiles start at the chosen size and shrink only if the window would not fit the screen.
        var area = Screen.FromControl(this).WorkingArea;
        int tileSize = SaveData.TileSizes.Contains(_save.TileSize) ? _save.TileSize : 48;
        int cell = LogicalToDeviceUnits(tileSize);
        while (true)
        {
            _boardControl.ApplyScale(cell);
            LayoutControls();
            if ((Width <= area.Width && Height <= area.Height) || cell <= LogicalToDeviceUnits(16)) break;
            cell -= 2;
        }
        Location = new Point(
            Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
            Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));

        if (!_challenge)
        {
            if (_shape == BoardShape.Hex) _save.LastHexDifficulty = _difficulty.Name;
            else _save.LastDifficulty = _difficulty.Name;
        }
        foreach (var (name, item) in _difficultyItems) item.Checked = name == _difficulty.Name;

        LayoutControls();
        UpdateHeader();
        UpdateFace();
    }

    private void LayoutControls()
    {
        int pad = LogicalToDeviceUnits(10);
        int inset = LogicalToDeviceUnits(6);
        int headerHeight = LogicalToDeviceUnits(40);
        int ledWidth = LogicalToDeviceUnits(62);
        int face = LogicalToDeviceUnits(32);
        int menuHeight = _menu.Height;

        int boardW = _boardControl.Width;
        int headerTop = menuHeight + pad;
        int headerBoxHeight = headerHeight + 2 * inset;

        _mineCounter.SetBounds(pad + inset, headerTop + inset + (headerHeight - LogicalToDeviceUnits(30)) / 2, ledWidth, LogicalToDeviceUnits(30));
        _timerDisplay.SetBounds(pad + boardW - inset - ledWidth, _mineCounter.Top, ledWidth, LogicalToDeviceUnits(30));
        _faceButton.SetBounds(pad + (boardW - face) / 2, headerTop + inset + (headerHeight - face) / 2, face, face);

        _boardControl.Location = new Point(pad, headerTop + headerBoxHeight + pad);
        ClientSize = new Size(boardW + 2 * pad, _boardControl.Bottom + pad);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_boardControl.Board == null) return;

        int pad = LogicalToDeviceUnits(10);
        int inset = LogicalToDeviceUnits(6);
        int headerBoxHeight = LogicalToDeviceUnits(40) + 2 * inset;

        var header = new Rectangle(pad, _menu.Height + pad, _boardControl.Width, headerBoxHeight);
        var board = new Rectangle(_boardControl.Left, _boardControl.Top, _boardControl.Width, _boardControl.Height);
        int b = LogicalToDeviceUnits(3);
        ControlPaint.DrawBorder3D(e.Graphics, Rectangle.Inflate(header, b, b), Border3DStyle.SunkenOuter);
        ControlPaint.DrawBorder3D(e.Graphics, Rectangle.Inflate(board, b, b), Border3DStyle.Sunken);
    }

    private void OnBoardChanged()
    {
        if (_board.Status == GameStatus.Playing && !_stopwatch.IsRunning)
        {
            _stopwatch.Start();
            _clock.Start();
        }

        if (_board.Status is GameStatus.Won or GameStatus.Lost)
        {
            _stopwatch.Stop();
            _clock.Stop();
            if (_board.Status == GameStatus.Won) RecordWin();
        }

        UpdateHeader();
        UpdateFace();
    }

    private void RecordChallengeWin()
    {
        if (_bestTimeRecorded) return;
        _bestTimeRecorded = true;

        long ms = _stopwatch.ElapsedMilliseconds;
        int cleared = _level;
        bool newBest = _save.TrySetBestTime(_difficulty.Key, ms);
        int? next = _save.CompleteChallengeLevel(cleared);

        // Only flags the player placed count; the ones the game adds to every mine on a win do not.
        bool flagless = _board.FlagsPlaced == 0;
        bool endlessWasUnlocked = _save.EndlessUnlocked;
        if (flagless) _save.MarkFlaglessClear(cleared);
        bool endlessJustUnlocked = !endlessWasUnlocked && _save.EndlessUnlocked;

        _save.Save(_savePath);
        _nextLevelItem.Enabled = _level < _save.ChallengePlayable;

        string message = $"Level {cleared} cleared in {ms / 1000.0:0.00} seconds" + (newBest ? " - a new best!" : ".");
        if (flagless) message += $"\nNo flags used! ({_save.FlaglessLevelCount} of {ChallengeLevel.Count} levels flagless)";
        if (endlessJustUnlocked) message += "\n\nEndless Mode is unlocked. Find it on the main menu!";
        BeginInvoke(() =>
        {
            if (next is int n)
            {
                var answer = MessageBox.Show(this, $"{message}\n\nPlay level {n}?", "Level Cleared",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes) GoToLevel(n);
            }
            else
            {
                MessageBox.Show(this, $"{message}\n\nYou have cleared all {ChallengeLevel.Count} levels of the Hex Challenge!",
                    "Challenge Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        });
    }

    private void RecordWin()
    {
        if (_challenge)
        {
            RecordChallengeWin();
            return;
        }
        if (_bestTimeRecorded || _difficulty.Name == "Custom") return;
        _bestTimeRecorded = true;

        long ms = _stopwatch.ElapsedMilliseconds;
        if (_save.TrySetBestTime(_difficulty.Key, ms))
        {
            _save.Save(_savePath);
            BeginInvoke(() => MessageBox.Show(this,
                $"New best time for {_difficulty.Name}: {ms / 1000.0:0.00} seconds!",
                "Fastest Time", MessageBoxButtons.OK, MessageBoxIcon.Information));
        }
    }

    private void UpdateTimer() => _timerDisplay.Value = (int)_stopwatch.Elapsed.TotalSeconds;

    private void UpdateHeader()
    {
        _mineCounter.Value = _board.FlagsRemaining;
        _timerDisplay.Value = (int)_stopwatch.Elapsed.TotalSeconds;
    }

    private void UpdateFace()
    {
        _faceButton.Face = _board.Status switch
        {
            GameStatus.Won => Face.Cool,
            GameStatus.Lost => Face.Dead,
            _ => _boardControl.IsPressing ? Face.Surprised : Face.Smile,
        };
    }
}
