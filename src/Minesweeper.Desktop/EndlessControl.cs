using System.Drawing.Drawing2D;
using Minesweeper.Core;

namespace Minesweeper.Desktop;

/// <summary>Draws and drives an <see cref="EndlessBoard"/>: hex rows sliding down, left click reveals.</summary>
public sealed class EndlessControl : Control
{
    private EndlessBoard? _board;

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
        if (_board == null || e.Button != MouseButtons.Left) return;
        if (!CellAt(e.Location, out int serial, out int x)) return;

        _board.Reveal(serial, x);
        Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BoardControl.Face);
        if (_board == null) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var font = new Font("Segoe UI", CellSize * 0.58f, FontStyle.Bold, GraphicsUnit.Pixel);
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

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

        // The bottom edge is the danger line: a row that reaches it ends the run.
        int bar = Math.Max(4, CellSize / 8);
        using var danger = new SolidBrush(Color.FromArgb(200, 0, 0));
        g.FillRectangle(danger, 0, Height - bar, Width, bar);
    }
}
