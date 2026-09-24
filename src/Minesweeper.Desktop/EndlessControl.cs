using System.Drawing.Drawing2D;
using Minesweeper.Core;

namespace Minesweeper.Desktop;

/// <summary>Draws and drives an <see cref="EndlessBoard"/>: hex rows sliding down, left click reveals.</summary>
public sealed class EndlessControl : Control
{
    private EndlessBoard? _board;
    private bool _paused;

    // Keyboard cursor: a row serial and column, so it rides along with its row. Hidden (-1) until a key is used.
    private int _cursorRow = -1, _cursorCol = -1;

    /// <summary>While paused the tiles are hidden (so pausing gives no free thinking time) and clicks are ignored.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            Invalidate();
        }
    }

    public EndlessControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.Opaque, true);
        TabStop = false;
    }

    public int CellSize { get; private set; } = 24;

    public EndlessBoard? Board
    {
        get => _board;
        set
        {
            _board = value;
            _paused = false;
            _cursorRow = _cursorCol = -1;
            Size = FieldSize();
            Invalidate();
        }
    }

    /// <summary>Raised after a click has been applied to the board.</summary>
    public event EventHandler? Changed;

    public void ApplyScale(int cellSize)
    {
        CellSize = cellSize;
        Size = FieldSize();
        Invalidate();
    }

    // Same geometry as the hex board: pointy-top cells, HexWidth flat to flat, HexRadius center to corner.
    private float HexWidth => CellSize * 1.1f;
    private float HexRadius => HexWidth / MathF.Sqrt(3f);

    private Size FieldSize() => new(
        (int)MathF.Ceiling(HexWidth * (EndlessBoard.Columns + 0.5f)),
        (int)MathF.Ceiling(HexRadius * (1.5f * (EndlessBoard.Capacity - 1) + 2f)));

    private PointF CellCenter(int serial, int x) => new(
        HexWidth * (x + 0.5f + ((serial & 1) == 1 ? 0.5f : 0f)),
        HexRadius * (1f + 1.5f * (float)_board!.SlotPosition(serial)));

    private bool CellAt(Point p, out int serial, out int x)
    {
        serial = x = -1;
        if (_board == null) return false;

        double slot = (p.Y / HexRadius - 1f) / 1.5;
        int estimate = (int)Math.Round(_board.NewestRow + _board.Offset - slot);
        int column = (int)(p.X / HexWidth);

        float best = float.MaxValue;
        for (int s = estimate - 1; s <= estimate + 1; s++)
        {
            if (!_board.IsVisibleRow(s)) continue;
            for (int c = column - 1; c <= column + 1; c++)
            {
                if (c < 0 || c >= EndlessBoard.Columns) continue;
                var center = CellCenter(s, c);
                float d = (center.X - p.X) * (center.X - p.X) + (center.Y - p.Y) * (center.Y - p.Y);
                if (d < best) { best = d; serial = s; x = c; }
            }
        }
        return serial >= 0 && best <= HexRadius * HexRadius;
    }

    // Rows keep moving while the button is down, so a click acts on press, not release.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_board == null || _paused || e.Button != MouseButtons.Left) return;
        _cursorRow = _cursorCol = -1;
        if (!CellAt(e.Location, out int serial, out int x)) return;

        _board.Reveal(serial, x);
        Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keyboard play: arrows (or WASD) move the cursor, Space/Enter reveals. Returns true if the key was used.</summary>
    public bool HandleKey(Keys key)
    {
        if (_board == null || _paused) return false;

        bool reveal = key is Keys.Space or Keys.Enter;
        int dx = key switch { Keys.Left or Keys.A => -1, Keys.Right or Keys.D => 1, _ => 0 };
        // Up is toward newer rows (higher serials) at the top of the field.
        int dy = key switch { Keys.Up or Keys.W => 1, Keys.Down or Keys.S => -1, _ => 0 };
        if (!reveal && dx == 0 && dy == 0) return false;

        var rows = _board.VisibleRows.ToList();
        if (rows.Count == 0) return true;

        if (!_board.IsVisibleRow(_cursorRow))
        {
            // First use, or the cursor's row was cleared: go to the nearest row still on the field.
            int target = _cursorRow < 0 ? rows[rows.Count / 2] : _cursorRow;
            _cursorRow = rows.OrderBy(s => Math.Abs(s - target)).First();
            if (_cursorCol < 0) _cursorCol = EndlessBoard.Columns / 2;
        }
        else if (dy != 0)
        {
            int index = rows.IndexOf(_cursorRow) + dy;
            if (index >= 0 && index < rows.Count) _cursorRow = rows[index];
        }
        _cursorCol = Math.Clamp(_cursorCol + dx, 0, EndlessBoard.Columns - 1);

        if (reveal)
        {
            _board.Reveal(_cursorRow, _cursorCol);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        Invalidate();
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BoardControl.Face);
        if (_board == null) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var font = new Font("Segoe UI", CellSize * 0.58f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        if (_paused)
        {
            using var big = new Font("Segoe UI", CellSize * 1.2f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", CellSize * 0.5f, GraphicsUnit.Pixel);
            g.DrawString("PAUSED", big, Brushes.Black, new RectangleF(0, 0, Width, Height - CellSize * 1.5f), format);
            g.DrawString("Press P or Esc to carry on", small, Brushes.DimGray, new RectangleF(0, CellSize * 1.5f, Width, Height - CellSize * 1.5f), format);
            return;
        }

        foreach (int serial in _board.VisibleRows)
        {
            for (int x = 0; x < EndlessBoard.Columns; x++)
            {
                var center = CellCenter(serial, x);
                if (center.Y + HexRadius < 0 || center.Y - HexRadius > Height) continue;

                var cell = _board[serial, x];
                var rect = new Rectangle((int)MathF.Round(center.X - CellSize / 2f), (int)MathF.Round(center.Y - CellSize / 2f), CellSize, CellSize);
                bool revealed = cell.State == CellState.Revealed;

                BoardControl.DrawHexBackground(g, center, HexRadius, CellSize, !revealed, cell.Exploded);
                if (!revealed) continue;

                if (cell.IsMine) BoardControl.DrawMine(g, rect, CellSize);
                else if (cell.AdjacentMines > 0)
                {
                    using var brush = new SolidBrush(BoardControl.NumberColors[cell.AdjacentMines]);
                    g.DrawString(cell.AdjacentMines.ToString(), font, brush, rect, format);
                }
            }
        }

        if (_board.IsVisibleRow(_cursorRow) && _cursorCol >= 0)
            BoardControl.DrawHexCursor(g, CellCenter(_cursorRow, _cursorCol), HexRadius, CellSize);

        // The bottom edge is the danger line: a row that reaches it ends the run.
        int bar = Math.Max(4, CellSize / 8);
        using var danger = new SolidBrush(Color.FromArgb(200, 0, 0));
        g.FillRectangle(danger, 0, Height - bar, Width, bar);
    }
}
