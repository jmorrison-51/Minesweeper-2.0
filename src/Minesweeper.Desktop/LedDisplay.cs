using System.Drawing.Drawing2D;

namespace Minesweeper.Desktop;

// Three-digit red-on-black counter, used for the mine count and timer.
public sealed class LedDisplay : Control
{
    private int _value;

    public LedDisplay()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    public int Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Black);

        int v = Math.Clamp(_value, -99, 999);
        string text = v < 0 ? "-" + Math.Abs(v).ToString("00") : v.ToString("000");

        using var font = new Font("Consolas", Height * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var ghost = new SolidBrush(Color.FromArgb(60, 0, 0));
        using var lit = new SolidBrush(Color.FromArgb(255, 30, 30));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var rect = new RectangleF(0, 0, Width, Height);

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.DrawString("888", font, ghost, rect, format);
        g.DrawString(text, font, lit, rect, format);
    }
}
