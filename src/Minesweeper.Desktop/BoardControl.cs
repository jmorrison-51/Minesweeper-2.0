using System.Drawing.Drawing2D;
using Minesweeper.Core;

namespace Minesweeper.Desktop;

public sealed class BoardControl : Control
{
    private static readonly Color Face = Color.FromArgb(192, 192, 192);
    private static readonly Color Light = Color.White;
    private static readonly Color Shadow = Color.FromArgb(128, 128, 128);
    private static readonly Color[] NumberColors =
    {
        Color.Empty,
        Color.FromArgb(0, 0, 255),
        Color.FromArgb(0, 128, 0),
        Color.FromArgb(255, 0, 0),
        Color.FromArgb(0, 0, 128),
        Color.FromArgb(128, 0, 0),
        Color.FromArgb(0, 128, 128),
        Color.Black,
        Color.FromArgb(128, 128, 128),
    };

    private Board? _board;
    private bool _leftDown, _rightDown, _middleDown, _chording, _chordDone;
    private int _hoverX = -1, _hoverY = -1;

    public BoardControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.Opaque, true);
        TabStop = false;
    }

    public int CellSize { get; private set; } = 24;

    public Board? Board
    {
        get => _board;
        set
        {
            _board = value;
            ResetInput();
            if (_board != null) Size = new Size(_board.Columns * CellSize, _board.Rows * CellSize);
            Invalidate();
        }
    }

    public bool IsPressing => _board?.Status is GameStatus.Ready or GameStatus.Playing && (_leftDown || _middleDown) && _hoverX >= 0;

    public event EventHandler? Changed;
    public event EventHandler? PressingChanged;

    public void ApplyScale(int cellSize)
    {
        CellSize = cellSize;
        if (_board != null) Size = new Size(_board.Columns * CellSize, _board.Rows * CellSize);
        Invalidate();
    }

    private void ResetInput()
    {
        _leftDown = _rightDown = _middleDown = _chording = _chordDone = false;
        _hoverX = _hoverY = -1;
    }

    private bool CellAt(Point p, out int x, out int y)
    {
        x = p.X / CellSize;
        y = p.Y / CellSize;
        return p.X >= 0 && p.Y >= 0 && _board != null && _board.InBounds(x, y);
    }

    private void UpdateHover(Point p)
    {
        int hx = -1, hy = -1;
        if (CellAt(p, out int x, out int y)) { hx = x; hy = y; }
        if (hx == _hoverX && hy == _hoverY) return;
        _hoverX = hx;
        _hoverY = hy;
        Invalidate();
        PressingChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_board == null) return;

        if (e.Button == MouseButtons.Left) _leftDown = true;
        else if (e.Button == MouseButtons.Right) _rightDown = true;
        else if (e.Button == MouseButtons.Middle) _middleDown = true;
        else return;

        if ((_leftDown && _rightDown) || _middleDown) _chording = true;
        UpdateHover(e.Location);
        Invalidate();
        PressingChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_leftDown || _rightDown || _middleDown) UpdateHover(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_board == null) return;

        bool wasLeft = _leftDown, wasRight = _rightDown;
        if (e.Button == MouseButtons.Left) _leftDown = false;
        else if (e.Button == MouseButtons.Right) _rightDown = false;
        else if (e.Button == MouseButtons.Middle) _middleDown = false;
        else return;

        bool hasCell = CellAt(e.Location, out int x, out int y);

        if (_chording)
        {
            if (!_chordDone && hasCell)
            {
                _chordDone = true;
                _board.Chord(x, y);
            }
            if (!_leftDown && !_rightDown && !_middleDown)
            {
                _chording = false;
                _chordDone = false;
            }
        }
        else if (hasCell)
        {
            if (e.Button == MouseButtons.Left && wasLeft) _board.Reveal(x, y);
            else if (e.Button == MouseButtons.Right && wasRight) _board.ToggleFlag(x, y);
        }

        Invalidate();
        PressingChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverX != -1) UpdateHover(new Point(-1, -1));
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        ResetInput();
        Invalidate();
        PressingChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Face);
        if (_board == null) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        bool pressing = _board.Status is GameStatus.Ready or GameStatus.Playing;
        using var font = new Font("Segoe UI", CellSize * 0.58f, FontStyle.Bold, GraphicsUnit.Pixel);
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (int y = 0; y < _board.Rows; y++)
        {
            for (int x = 0; x < _board.Columns; x++)
            {
                var rect = new Rectangle(x * CellSize, y * CellSize, CellSize, CellSize);
                if (!rect.IntersectsWith(e.ClipRectangle)) continue;
                DrawCell(g, rect, _board[x, y], IsPressed(x, y, pressing), font, format);
            }
        }
    }

    private bool IsPressed(int x, int y, bool pressing)
    {
        if (!pressing || _hoverX < 0) return false;
        if (_chording) return Math.Abs(x - _hoverX) <= 1 && Math.Abs(y - _hoverY) <= 1;
        return _leftDown && x == _hoverX && y == _hoverY;
    }

    private void DrawCell(Graphics g, Rectangle r, Cell cell, bool pressed, Font font, StringFormat format)
    {
        bool lost = _board!.Status == GameStatus.Lost;
        bool raised = cell.State != CellState.Revealed && !pressed || cell.State == CellState.Flagged;

        if (raised)
        {
            using (var b = new SolidBrush(Face)) g.FillRectangle(b, r);
            int bevel = Math.Max(2, CellSize / 10);
            using (var light = new SolidBrush(Light))
            using (var dark = new SolidBrush(Shadow))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.FillPolygon(light, new[] { new Point(r.Left, r.Top), new Point(r.Right, r.Top), new Point(r.Right - bevel, r.Top + bevel), new Point(r.Left + bevel, r.Top + bevel), new Point(r.Left + bevel, r.Bottom - bevel), new Point(r.Left, r.Bottom) });
                g.FillPolygon(dark, new[] { new Point(r.Right, r.Bottom), new Point(r.Left, r.Bottom), new Point(r.Left + bevel, r.Bottom - bevel), new Point(r.Right - bevel, r.Bottom - bevel), new Point(r.Right - bevel, r.Top + bevel), new Point(r.Right, r.Top) });
                g.SmoothingMode = SmoothingMode.AntiAlias;
            }
        }
        else
        {
            Color fill = cell.Exploded ? Color.Red : Face;
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
            using var edge = new Pen(Shadow);
            g.DrawRectangle(edge, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        if (cell.State == CellState.Flagged)
        {
            if (lost && !cell.IsMine)
            {
                DrawMine(g, r);
                using var cross = new Pen(Color.Red, Math.Max(2f, CellSize / 10f));
                g.DrawLine(cross, r.Left + 4, r.Top + 4, r.Right - 4, r.Bottom - 4);
                g.DrawLine(cross, r.Left + 4, r.Bottom - 4, r.Right - 4, r.Top + 4);
            }
            else
            {
                DrawFlag(g, r);
            }
        }
        else if (cell.State == CellState.Revealed)
        {
            if (cell.IsMine) DrawMine(g, r);
            else if (cell.AdjacentMines > 0)
            {
                using var brush = new SolidBrush(NumberColors[cell.AdjacentMines]);
                g.DrawString(cell.AdjacentMines.ToString(), font, brush, r, format);
            }
        }
    }

    private void DrawFlag(Graphics g, Rectangle r)
    {
        float u = CellSize / 24f;
        float cx = r.X + r.Width / 2f;
        using var black = new SolidBrush(Color.Black);
        using var red = new SolidBrush(Color.Red);
        g.FillRectangle(black, cx - 6 * u, r.Bottom - 6 * u, 12 * u, 2.5f * u);
        g.FillRectangle(black, cx - 3.5f * u, r.Bottom - 8.5f * u, 7 * u, 2.5f * u);
        g.FillRectangle(black, cx - 0.5f * u, r.Top + 5 * u, 2 * u, r.Height - 12 * u);
        g.FillPolygon(red, new[]
        {
            new PointF(cx + 1.5f * u, r.Top + 4 * u),
            new PointF(cx + 1.5f * u, r.Top + 12 * u),
            new PointF(cx - 6 * u, r.Top + 8 * u),
        });
    }

    private void DrawMine(Graphics g, Rectangle r)
    {
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        float radius = CellSize * 0.26f;
        using var black = new SolidBrush(Color.Black);
        using var spike = new Pen(Color.Black, Math.Max(1.5f, CellSize / 12f));

        float reach = radius * 1.6f;
        g.DrawLine(spike, cx - reach, cy, cx + reach, cy);
        g.DrawLine(spike, cx, cy - reach, cx, cy + reach);
        float diag = reach * 0.72f;
        g.DrawLine(spike, cx - diag, cy - diag, cx + diag, cy + diag);
        g.DrawLine(spike, cx - diag, cy + diag, cx + diag, cy - diag);
        g.FillEllipse(black, cx - radius, cy - radius, radius * 2, radius * 2);

        using var shine = new SolidBrush(Color.White);
        float s = radius * 0.4f;
        g.FillRectangle(shine, cx - radius * 0.5f, cy - radius * 0.5f, s, s);
    }
}
