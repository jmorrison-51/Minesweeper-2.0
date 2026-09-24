using System.Drawing.Drawing2D;

namespace Minesweeper.Desktop;

public enum Face
{
    Smile,
    Surprised,
    Dead,
    Cool,
}

public sealed class FaceButton : Control
{
    private Face _face;
    private bool _pressed;

    public FaceButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        TabStop = false;
    }

    public Face Face
    {
        get => _face;
        set
        {
            if (_face == value) return;
            _face = value;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressed) { _pressed = false; Invalidate(); }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(192, 192, 192));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width, Height);
        ControlPaint.DrawBorder3D(g, bounds, _pressed ? Border3DStyle.SunkenInner : Border3DStyle.RaisedInner);

        int pad = Math.Max(4, Width / 6);
        var head = new Rectangle(pad, pad, Width - 2 * pad, Height - 2 * pad);
        if (_pressed) head.Offset(1, 1);

        using (var fill = new SolidBrush(Color.FromArgb(255, 221, 0)))
        using (var outline = new Pen(Color.Black, 1.5f))
        {
            g.FillEllipse(fill, head);
            g.DrawEllipse(outline, head);
        }

        float cx = head.X + head.Width / 2f, cy = head.Y + head.Height / 2f;
        float r = head.Width / 2f;
        using var black = new SolidBrush(Color.Black);
        using var pen = new Pen(Color.Black, Math.Max(1.5f, r / 8f));

        float eyeDx = r * 0.36f, eyeY = cy - r * 0.18f, eyeR = r * 0.12f;
        switch (_face)
        {
            case Face.Dead:
                float s = r * 0.16f;
                foreach (float ex in new[] { cx - eyeDx, cx + eyeDx })
                {
                    g.DrawLine(pen, ex - s, eyeY - s, ex + s, eyeY + s);
                    g.DrawLine(pen, ex - s, eyeY + s, ex + s, eyeY - s);
                }
                g.DrawArc(pen, cx - r * 0.4f, cy + r * 0.28f, r * 0.8f, r * 0.5f, 200, 140);
                break;
            case Face.Cool:
                g.FillRectangle(black, cx - r * 0.7f, eyeY - r * 0.18f, r * 1.4f, r * 0.3f);
                g.DrawArc(pen, cx - r * 0.45f, cy - r * 0.05f, r * 0.9f, r * 0.7f, 20, 140);
                break;
            case Face.Surprised:
                g.FillEllipse(black, cx - eyeDx - eyeR, eyeY - eyeR, eyeR * 2, eyeR * 2);
                g.FillEllipse(black, cx + eyeDx - eyeR, eyeY - eyeR, eyeR * 2, eyeR * 2);
                g.FillEllipse(black, cx - r * 0.14f, cy + r * 0.3f, r * 0.28f, r * 0.34f);
                break;
            default:
                g.FillEllipse(black, cx - eyeDx - eyeR, eyeY - eyeR, eyeR * 2, eyeR * 2);
                g.FillEllipse(black, cx + eyeDx - eyeR, eyeY - eyeR, eyeR * 2, eyeR * 2);
                g.DrawArc(pen, cx - r * 0.45f, cy - r * 0.05f, r * 0.9f, r * 0.7f, 20, 140);
                break;
        }
    }
}
