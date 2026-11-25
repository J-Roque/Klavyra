using System.Drawing.Drawing2D;

namespace Klavyra.Desktop;

/// <summary>
/// The startup screen: the same staff as the home page (a waveform on the left that becomes notes on the right),
/// gently animated, with the logo and the startup step.
/// </summary>
internal sealed class SplashView : Control
{
    public static readonly Color Paper = Color.FromArgb(247, 244, 236);
    private static readonly Color StaffLine = Color.FromArgb(205, 199, 182);
    private static readonly Color Wave = Color.FromArgb(31, 150, 114);
    private static readonly Color Note = Color.FromArgb(16, 97, 75);
    private static readonly Color Title = Color.FromArgb(40, 44, 40);
    private static readonly Color Muted = Color.FromArgb(96, 102, 98);
    private static readonly Color ErrorText = Color.FromArgb(176, 58, 46);

    // The home page's drawing, in its 1200 x 120 viewBox.
    private static readonly (float X, float H, float Opacity)[] Bars = Enumerable.Range(0, 46)
        .Select(i => (12f + i * 8, 8f + MathF.Abs(MathF.Sin(i * 0.55f) * MathF.Cos(i * 0.17f)) * 78, 0.12f + i / 46f * 0.5f))
        .ToArray();
    private static readonly (float X, float Y, float Opacity)[] Notes = new (float, float)[]
        { (800, 70), (846, 60), (892, 50), (938, 60), (984, 70), (1030, 40), (1076, 50), (1122, 30), (1168, 40) }
        .Select((n, i) => (n.Item1, n.Item2, 0.75f - i * 0.06f))
        .ToArray();

    private const float NoteCycle = 4.5f; // seconds for the notes to appear one by one and fade together
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly long _start = Environment.TickCount64;
    private readonly Image? _logo = LoadLogo();
    private string _status = "";
    private bool _isError;

    public SplashView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Paper;
        _timer.Tick += (_, _) => Invalidate();
    }

    public void ShowStatus(string text, bool isError = false)
    {
        _status = text;
        _isError = isError;
        Invalidate();
    }

    private static Image? LoadLogo()
    {
        using var stream = typeof(SplashView).Assembly.GetManifestResourceStream("splash-logo.png");
        return stream is null ? null : Image.FromStream(stream);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _timer.Enabled = Visible; // no animation, and no CPU, once Klavyra is showing
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var t = (Environment.TickCount64 - _start) / 1000f;
        var dpi = DeviceDpi / 96f;

        // The whole staff across the window, from the first bar to the last note.
        var scale = Math.Min(Width / 1200f, 1.6f * dpi);
        var left = (Width - 1200 * scale) / 2;
        var top = Height * 0.56f - 60 * scale;
        PointF At(float x, float y) => new(left + x * scale, top + y * scale);

        using (var line = new Pen(StaffLine, Math.Max(1f, 0.6f * scale)))
        {
            foreach (var y in new[] { 20f, 40f, 60f, 80f, 100f })
                g.DrawLine(line, At(0, y), At(1200, y));
        }

        // Sound: the bars breathe, as if the audio were playing.
        foreach (var (i, (x, h, opacity)) in Bars.Index())
        {
            var height = h * (0.45f + 0.55f * MathF.Abs(MathF.Sin(t * 1.8f + i * 0.32f)));
            using var brush = new SolidBrush(Color.FromArgb((int)(255 * opacity), Wave));
            FillRounded(g, brush, new RectangleF(At(x, 60 - height / 2), new SizeF(4 * scale, height * scale)), 2 * scale);
        }

        // Notation: the notes are written one by one, then start again.
        var phase = t % NoteCycle;
        foreach (var (i, (x, y, opacity)) in Notes.Index())
        {
            var appear = Math.Clamp((phase - i * 0.28f) / 0.35f, 0, 1);
            var fade = Math.Clamp((NoteCycle - phase) / 0.5f, 0, 1);
            var alpha = opacity * appear * fade;
            if (alpha <= 0) continue;
            using var brush = new SolidBrush(Color.FromArgb((int)(255 * alpha), Note));
            var center = At(x, y);
            var state = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(-20);
            g.FillEllipse(brush, -9 * scale, -6.5f * scale, 18 * scale, 13 * scale);
            g.Restore(state);
            g.FillRectangle(brush, new RectangleF(At(x + 7, y - 46), new SizeF(1.8f * scale, 46 * scale)));
        }

        // The logo and name above the staff, the startup step below it.
        var logoSize = 64 * dpi;
        using var titleFont = new Font("Georgia", 26f);
        var titleSize = g.MeasureString("Klavyra", titleFont);
        var blockBottom = top - 18 * dpi;
        var titleTop = blockBottom - titleSize.Height;
        if (_logo is not null)
            g.DrawImage(_logo, new RectangleF((Width - logoSize) / 2, titleTop - logoSize - 6 * dpi, logoSize, logoSize));
        using (var brush = new SolidBrush(Title))
            g.DrawString("Klavyra", titleFont, brush, (Width - titleSize.Width) / 2, titleTop);

        using var statusFont = new Font("Segoe UI", 11.5f);
        using var statusBrush = new SolidBrush(_isError ? ErrorText : Muted);
        var statusArea = new RectangleF(32 * dpi, top + 120 * scale + 22 * dpi, Width - 64 * dpi, Height);
        using var centered = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(_status, statusFont, statusBrush, statusArea, centered);
    }

    private static void FillRounded(Graphics g, Brush brush, RectangleF r, float radius)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius <= 0.5f)
        {
            g.FillRectangle(brush, r);
            return;
        }
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _logo?.Dispose();
        }
        base.Dispose(disposing);
    }
}
