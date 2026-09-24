using Minesweeper.Core;

namespace Minesweeper.Desktop;

public enum GameMode
{
    Classic,
    Hex,
    HexChallenge,
    Endless,
}

/// <summary>Start screen. Sets <see cref="Selected"/> and closes when a mode is picked; stays null on exit.</summary>
public sealed class StartForm : Form
{
    private static readonly Color Gray = Color.FromArgb(192, 192, 192);

    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly ModeButton[] _buttons;
    private readonly ModeButton _exit;
    private readonly Label _tileLabel = new();
    private readonly ModeButton[] _tileButtons;
    private readonly SaveData _save;

    public GameMode? Selected { get; private set; }

    public StartForm()
    {
        Text = "Minesweeper 2.0";
        BackColor = Gray;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        _title.Text = "MINESWEEPER 2.0";
        _title.TextAlign = ContentAlignment.MiddleCenter;
        _title.Font = new Font("Segoe UI", 22f, FontStyle.Bold);
        _subtitle.Text = "Choose a game";
        _subtitle.TextAlign = ContentAlignment.MiddleCenter;
        _subtitle.Font = new Font("Segoe UI", 10f);

        _save = SaveData.Load(Program.SavePath);
        if (!SaveData.TileSizes.Contains(_save.TileSize)) _save.TileSize = 48;

        // Endless Mode stays locked, and darker, until every Hex Challenge level is cleared without flags.
        bool endlessUnlocked = _save.EndlessUnlocked;
        var endless = new ModeButton(
            endlessUnlocked ? "Endless Mode" : "Beat Hex Challenge without using any flags",
            endlessUnlocked
                ? "Rows keep sliding down. Clear them before they reach the bottom."
                : $"{_save.FlaglessLevelCount} of {ChallengeLevel.Count} levels cleared without flags")
        {
            Locked = !endlessUnlocked,
        };
        endless.Click += (_, _) =>
        {
            if (!endlessUnlocked) return;
            Selected = GameMode.Endless;
            Close();
        };

        _buttons =
        [
            Create("Minesweeper Original", "The classic square grid. Beginner, Intermediate, Expert or Custom.", GameMode.Classic),
            Create("Hex Minesweeper", "Hexagonal tiles, six neighbors each. Beginner, Intermediate, Expert or Custom.", GameMode.Hex),
            Create("Hex Challenge", "20 levels of rising difficulty with mystery tiles and a shrinking flag budget.", GameMode.HexChallenge),
            endless,
        ];
        _exit = new ModeButton("Exit", null);
        _exit.Click += (_, _) => Close();

        _tileLabel.Text = "Tile size";
        _tileLabel.TextAlign = ContentAlignment.MiddleCenter;
        _tileLabel.Font = new Font("Segoe UI", 10f);
        _tileButtons = SaveData.TileSizes.Select(size =>
        {
            var button = new ModeButton(size.ToString(), null) { Checked = size == _save.TileSize };
            button.Click += (_, _) => SetTileSize(size);
            return button;
        }).ToArray();

        Controls.Add(_title);
        Controls.Add(_subtitle);
        Controls.AddRange(_buttons);
        Controls.Add(_tileLabel);
        Controls.AddRange(_tileButtons);
        Controls.Add(_exit);

        LayoutControls();
    }

    private ModeButton Create(string title, string description, GameMode mode)
    {
        var button = new ModeButton(title, description);
        button.Click += (_, _) =>
        {
            Selected = mode;
            Close();
        };
        return button;
    }

    private void SetTileSize(int size)
    {
        _save.TileSize = size;
        _save.Save(Program.SavePath);
        for (int i = 0; i < _tileButtons.Length; i++)
            _tileButtons[i].Checked = SaveData.TileSizes[i] == size;
    }

    private void LayoutControls()
    {
        int pad = LogicalToDeviceUnits(24);
        int width = LogicalToDeviceUnits(420);
        int buttonHeight = LogicalToDeviceUnits(72);
        int gap = LogicalToDeviceUnits(12);

        int y = pad;
        _title.SetBounds(pad, y, width, LogicalToDeviceUnits(44));
        y += _title.Height;
        _subtitle.SetBounds(pad, y, width, LogicalToDeviceUnits(28));
        y += _subtitle.Height + gap;

        foreach (var b in _buttons)
        {
            b.SetBounds(pad, y, width, buttonHeight);
            y += buttonHeight + gap;
        }

        _tileLabel.SetBounds(pad, y, width, LogicalToDeviceUnits(24));
        y += _tileLabel.Height;
        int tileGap = LogicalToDeviceUnits(8);
        int tileWidth = (width - tileGap * (_tileButtons.Length - 1)) / _tileButtons.Length;
        for (int i = 0; i < _tileButtons.Length; i++)
            _tileButtons[i].SetBounds(pad + i * (tileWidth + tileGap), y, tileWidth, LogicalToDeviceUnits(36));
        y += LogicalToDeviceUnits(36) + gap * 2;

        _exit.SetBounds(pad + width / 3, y, width / 3, LogicalToDeviceUnits(34));

        ClientSize = new Size(width + 2 * pad, _exit.Bottom + pad);
    }
}

/// <summary>Raised bevel button with a bold title and an optional description line.</summary>
public sealed class ModeButton : Control
{
    private readonly string _title;
    private readonly string? _description;
    private bool _hover;
    private bool _pressed;
    private bool _checked;
    private bool _locked;

    /// <summary>Drawn slightly darker and without hover or press feedback. The owner decides what a click does.</summary>
    public bool Locked
    {
        get => _locked;
        set
        {
            if (_locked == value) return;
            _locked = value;
            Cursor = value ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    /// <summary>Toggle-style state: drawn sunken, used for the current tile size.</summary>
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
        }
    }

    public ModeButton(string title, string? description)
    {
        _title = title;
        _description = description;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.StandardClick |
                 ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = !_locked; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Focus(); _pressed = !_locked; Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressed) { _pressed = false; Invalidate(); }
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        bool down = _pressed || _checked;
        g.Clear(_checked ? Color.FromArgb(160, 160, 160)
            : _locked ? Color.FromArgb(172, 172, 172)
            : _hover ? Color.FromArgb(208, 208, 208)
            : Color.FromArgb(192, 192, 192));

        var bounds = new Rectangle(0, 0, Width, Height);
        ControlPaint.DrawBorder3D(g, bounds, down ? Border3DStyle.SunkenInner : Border3DStyle.Raised);

        int pad = LogicalToDeviceUnits(12);
        var inner = Rectangle.Inflate(bounds, -pad, -LogicalToDeviceUnits(6));
        if (down) inner.Offset(1, 1);

        // Long titles shrink until they fit on one line.
        float titleSize = 13f;
        var titleFont = new Font("Segoe UI", titleSize, FontStyle.Bold);
        while (titleSize > 9f && TextRenderer.MeasureText(g, _title, titleFont).Width > inner.Width)
        {
            titleFont.Dispose();
            titleSize -= 0.5f;
            titleFont = new Font("Segoe UI", titleSize, FontStyle.Bold);
        }
        using var descFont = new Font("Segoe UI", 9f);
        var titleFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

        if (_description == null)
        {
            TextRenderer.DrawText(g, _title, titleFont, inner, Color.Black, titleFlags | TextFormatFlags.VerticalCenter);
        }
        else
        {
            int titleHeight = TextRenderer.MeasureText(g, _title, titleFont).Height;
            var titleRect = new Rectangle(inner.X, inner.Y, inner.Width, titleHeight);
            var descRect = new Rectangle(inner.X, titleRect.Bottom + LogicalToDeviceUnits(2), inner.Width, inner.Bottom - titleRect.Bottom - LogicalToDeviceUnits(2));
            TextRenderer.DrawText(g, _title, titleFont, titleRect, Color.Black, titleFlags);
            TextRenderer.DrawText(g, _description, descFont, descRect, Color.FromArgb(64, 64, 64),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        titleFont.Dispose();

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -4, -4));
    }
}
