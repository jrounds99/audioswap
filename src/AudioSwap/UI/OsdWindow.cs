using System.Diagnostics;
using System.Runtime.InteropServices;
using AudioSwap.Config;

namespace AudioSwap.UI;

/// <summary>
/// The switch popup: a layered, click-through, never-focused window that fades and
/// slides in, holds, then fades out. It sits on the monitor you're working on.
/// </summary>
internal sealed class OsdWindow : Form
{
    const double FadeInMs = 160, FadeOutMs = 420, SlideDistance = 10;

    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    readonly Stopwatch _clock = new();
    IntPtr _hBitmap;
    Size _frameSize;
    Point _target;
    int _slideDirection;
    float _scale = 1;
    double _holdMs;
    double _startAlpha;
    byte _alpha;

    public OsdWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        _timer.Tick += (_, _) => Animate();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowContent(OsdContent content, PopupPosition position, double seconds)
    {
        var screen = TargetScreen();
        _scale = DpiOf(screen) / 96f;

        using (var frame = OsdRenderer.Render(content, _scale))
        {
            ReleaseFrame();
            _hBitmap = frame.GetHbitmap(Color.FromArgb(0));
            _frameSize = frame.Size;
        }

        _target = Place(screen.WorkingArea, _frameSize, position, _scale);
        _slideDirection = position is PopupPosition.TopRight or PopupPosition.TopCenter ? -1 : 1;
        _holdMs = seconds * 1000;
        // Already on screen: swap the content in place instead of fading again.
        _startAlpha = Visible ? 255 : 0;
        _clock.Restart();

        if (!IsHandleCreated) CreateHandle();
        Push(_startAlpha == 255 ? (byte)255 : (byte)0, slide: _startAlpha == 255 ? 0 : 1);
        if (!Visible) Show();
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        _timer.Start();
    }

    void Animate()
    {
        double t = _clock.Elapsed.TotalMilliseconds;
        double fadeIn = _startAlpha >= 255 ? 0 : FadeInMs;
        double holdEnd = fadeIn + _holdMs;

        if (t < fadeIn)
        {
            double p = EaseOut(t / fadeIn);
            Push((byte)(255 * p), slide: 1 - p);
        }
        else if (t < holdEnd)
        {
            if (_alpha != 255) Push(255, 0);
        }
        else if (t < holdEnd + FadeOutMs)
        {
            Push((byte)(255 * (1 - EaseIn((t - holdEnd) / FadeOutMs))), 0);
        }
        else
        {
            _timer.Stop();
            Hide();
            _alpha = 0;
            ReleaseFrame();
        }
    }

    void Push(byte alpha, double slide)
    {
        if (_hBitmap == IntPtr.Zero) return;
        _alpha = alpha;
        var dst = new POINT(_target.X, _target.Y + (int)(slide * SlideDistance * _scale * _slideDirection));
        var size = new SIZE(_frameSize.Width, _frameSize.Height);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr old = SelectObject(memDc, _hBitmap);
        try
        {
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    void ReleaseFrame()
    {
        if (_hBitmap == IntPtr.Zero) return;
        DeleteObject(_hBitmap);
        _hBitmap = IntPtr.Zero;
    }

    static Point Place(Rectangle area, Size size, PopupPosition position, float scale)
    {
        // The frame already includes a ~20px shadow margin, so small offsets suffice.
        int edge = (int)(4 * scale);
        int centerX = area.Left + (area.Width - size.Width) / 2;
        return position switch
        {
            PopupPosition.BottomCenter => new(centerX, area.Bottom - size.Height - (int)(40 * scale)),
            PopupPosition.TopRight => new(area.Right - size.Width - edge, area.Top + edge),
            PopupPosition.TopCenter => new(centerX, area.Top + (int)(24 * scale)),
            PopupPosition.Center => new(centerX, area.Top + (area.Height - size.Height) / 2),
            _ => new(area.Right - size.Width - edge, area.Bottom - size.Height - edge),
        };
    }

    static Screen TargetScreen()
    {
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero ? Screen.FromHandle(foreground) : Screen.PrimaryScreen!;
    }

    static int DpiOf(Screen screen)
    {
        var center = new POINT(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
        var monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? (int)dpi : 96;
    }

    static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
    static double EaseIn(double t) => Math.Pow(Math.Clamp(t, 0, 1), 2);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        ReleaseFrame();
        base.Dispose(disposing);
    }

    #region Win32

    const int ULW_ALPHA = 2;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, MONITOR_DEFAULTTONEAREST = 2;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT(int x, int y) { public int X = x, Y = y; }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE(int cx, int cy) { public int Cx = cx, Cy = cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    #endregion
}
