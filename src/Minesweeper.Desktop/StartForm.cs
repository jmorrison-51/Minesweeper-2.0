namespace Minesweeper.Desktop;

public enum GameMode
{
    Classic,
    Hex,
    HexChallenge,
}

/// <summary>Start screen. Sets <see cref="Selected"/> and closes when a mode is picked; stays null on exit.</summary>
public sealed class StartForm : Form
{
    private static readonly Color Gray = Color.FromArgb(192, 192, 192);

    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly ModeButton[] _buttons;
    private readonly ModeButton _exit;

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

        _buttons =
        [
            Create("Minesweeper Original", "The classic square grid. Beginner, Intermediate, Expert or Custom.", GameMode.Classic),
            Create("Hex Minesweeper", "Hexagonal tiles at a fixed difficulty. Play as long as you like.", GameMode.Hex),
            Create("Hex Challenge", "20 levels of rising difficulty with mystery tiles and a shrinking flag budget.", GameMode.HexChallenge),
        ];
        _exit = new ModeButton("Exit", null);
        _exit.Click += (_, _) => Close();

        Controls.Add(_title);
        Controls.Add(_subtitle);
        Controls.AddRange(_buttons);
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

        y += gap;
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

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Focus(); _pressed = true; Invalidate(); }
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
        g.Clear(_hover ? Color.FromArgb(208, 208, 208) : Color.FromArgb(192, 192, 192));

        var bounds = new Rectangle(0, 0, Width, Height);
        ControlPaint.DrawBorder3D(g, bounds, _pressed ? Border3DStyle.SunkenInner : Border3DStyle.Raised);

        int pad = LogicalToDeviceUnits(12);
        var inner = Rectangle.Inflate(bounds, -pad, -LogicalToDeviceUnits(6));
        if (_pressed) inner.Offset(1, 1);

        using var titleFont = new Font("Segoe UI", 13f, FontStyle.Bold);
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

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -4, -4));
    }
}
