using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Btn = DS4Handler.Core.Input.Buttons;

namespace DS4Handler.App.Controls;

public enum PadKind { DualShock4, DualSense, Xbox360 }

/// <summary>
/// Draws a live top-down view of a DualShock 4, DualSense, or Xbox 360 controller with pressed buttons,
/// stick offsets, and trigger levels lit up in the theme palette.
/// </summary>
public sealed class ControllerView : FrameworkElement
{
    private const double DesignW = 400;
    private const double DesignH = 260;

    private sealed class Palette
    {
        public readonly Brush Accent = ThemeBrushes.Get("AccentBrush", 0x4C, 0x8D, 0xFF);
        public readonly Brush OnAccent = ThemeBrushes.Get("OnAccentBrush", 0xFF, 0xFF, 0xFF);
        public readonly Brush Inset = ThemeBrushes.Get("InsetBrush", 0x0A, 0x10, 0x20);
        public readonly Brush Control = ThemeBrushes.Get("ControlBrush", 0x16, 0x1E, 0x30);
        public readonly Brush TextDim = ThemeBrushes.Get("TextDimBrush", 0x8A, 0x97, 0xAD);
        public readonly Pen InsetPen = ThemeBrushes.Pen(ThemeBrushes.Get("InsetBorderBrush", 0x26, 0x32, 0x4A), 1);
        public readonly Pen ControlPen = ThemeBrushes.Pen(ThemeBrushes.Get("ControlBorderBrush", 0x2C, 0x3A, 0x55), 1.5);
        public readonly Pen GatePen = ThemeBrushes.Pen(ThemeBrushes.Get("GateBrush", 0x2C, 0x3A, 0x55), 1);
        public readonly Pen TextDimPen;
        public readonly Pen OnAccentPen;

        public Palette()
        {
            TextDimPen = ThemeBrushes.Pen(TextDim, 1.6);
            OnAccentPen = ThemeBrushes.Pen(OnAccent, 1.6);
        }
    }

    private static Palette? _palette;
    private static Palette P => _palette ??= new Palette();

    // Static frozen controller silhouettes
    private static readonly Geometry DS4Body = ThemeBrushes.Freeze(Geometry.Parse(
        "M 135,52 L 265,52 C 300,52 346,62 352,100 C 356,130 350,185 336,246 C 330,256 308,256 302,246 " +
        "C 290,225 280,185 260,185 C 240,185 225,198 200,198 C 175,198 160,185 140,185 " +
        "C 120,185 110,225 98,246 C 92,256 70,256 64,246 C 50,185 44,130 48,100 C 54,62 100,52 135,52 Z"));

    private static readonly Geometry DualSenseBody = ThemeBrushes.Freeze(Geometry.Parse(
        "M 140,50 L 260,50 C 295,50 354,58 360,95 C 365,135 358,195 340,248 C 334,258 310,258 304,248 " +
        "C 292,225 280,190 260,190 C 240,190 225,202 200,202 C 175,202 160,190 140,190 " +
        "C 120,190 108,225 96,248 C 90,258 66,258 60,248 C 42,195 35,135 40,95 C 46,58 105,50 140,50 Z"));

    private static readonly Geometry Xbox360Body = ThemeBrushes.Freeze(Geometry.Parse(
        "M 140,54 L 260,54 C 295,54 345,62 355,102 C 360,145 352,195 334,246 C 328,256 304,256 298,246 " +
        "C 275,208 260,212 200,218 C 140,212 125,208 102,246 C 96,256 72,256 66,246 " +
        "C 48,195 40,145 45,102 C 55,62 105,54 140,54 Z"));

    // Static frozen glyphs
    private static readonly Geometry TriangleGeom = ThemeBrushes.Freeze(Geometry.Parse("M 0,-4.5 L 4.5,3.5 L -4.5,3.5 Z"));
    private static readonly Geometry ArrowUp = ThemeBrushes.Freeze(Geometry.Parse("M 0,-3.5 L 3.5,2 L -3.5,2 Z"));
    private static readonly Geometry ArrowDown = ThemeBrushes.Freeze(Geometry.Parse("M 0,3.5 L 3.5,-2 L -3.5,-2 Z"));
    private static readonly Geometry ArrowLeft = ThemeBrushes.Freeze(Geometry.Parse("M -3.5,0 L 2,3.5 L 2,-3.5 Z"));
    private static readonly Geometry ArrowRight = ThemeBrushes.Freeze(Geometry.Parse("M 3.5,0 L -2,3.5 L -2,-3.5 Z"));

    private static readonly Typeface UiFont = new(new FontFamily("Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Dictionary<(string text, bool isAccent, int sizeKey, int dpiKey), FormattedText> TextCache = new();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(PadKind), typeof(ControllerView),
        new FrameworkPropertyMetadata(PadKind.DualShock4, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ButtonsProperty = DependencyProperty.Register(
        nameof(Buttons), typeof(Btn), typeof(ControllerView),
        new FrameworkPropertyMetadata(Btn.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LeftXProperty = Register(nameof(LeftX));
    public static readonly DependencyProperty LeftYProperty = Register(nameof(LeftY));
    public static readonly DependencyProperty RightXProperty = Register(nameof(RightX));
    public static readonly DependencyProperty RightYProperty = Register(nameof(RightY));
    public static readonly DependencyProperty L2Property = Register(nameof(L2));
    public static readonly DependencyProperty R2Property = Register(nameof(R2));

    public PadKind Kind { get => (PadKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Btn Buttons { get => (Btn)GetValue(ButtonsProperty); set => SetValue(ButtonsProperty, value); }
    public double LeftX { get => (double)GetValue(LeftXProperty); set => SetValue(LeftXProperty, value); }
    public double LeftY { get => (double)GetValue(LeftYProperty); set => SetValue(LeftYProperty, value); }
    public double RightX { get => (double)GetValue(RightXProperty); set => SetValue(RightXProperty, value); }
    public double RightY { get => (double)GetValue(RightYProperty); set => SetValue(RightYProperty, value); }
    public double L2 { get => (double)GetValue(L2Property); set => SetValue(L2Property, value); }
    public double R2 { get => (double)GetValue(R2Property); set => SetValue(R2Property, value); }

    private static DependencyProperty Register(string name) => DependencyProperty.Register(
        name, typeof(double), typeof(ControllerView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width;
        double h = w * (DesignH / DesignW);
        if (!double.IsInfinity(availableSize.Height) && h > availableSize.Height)
        {
            h = availableSize.Height;
            w = h * (DesignW / DesignH);
        }
        return new Size(w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = P;
        double w = ActualWidth, h = ActualHeight;
        if (w <= 20 || h <= 20) return;

        double scale = Math.Min(w / DesignW, h / DesignH);
        double ox = (w - DesignW * scale) / 2;
        double oy = (h - DesignH * scale) / 2;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, ox, oy));
        try
        {
            var kind = Kind;
            bool xbox = kind == PadKind.Xbox360;
            bool dualSense = kind == PadKind.DualSense;

            // 1. Bumpers & triggers along top edge
            DrawTrigger(dc, p, new Rect(76, 14, 58, 18), L2, xbox ? "LT" : "L2", dpi);
            DrawTrigger(dc, p, new Rect(266, 14, 58, 18), R2, xbox ? "RT" : "R2", dpi);
            DrawBumper(dc, p, new Rect(76, 35, 58, 17), Btn.L1, xbox ? "LB" : "L1", dpi);
            DrawBumper(dc, p, new Rect(266, 35, 58, 17), Btn.R1, xbox ? "RB" : "R1", dpi);

            // 2. Controller body silhouette
            var bodyGeom = xbox ? Xbox360Body : (dualSense ? DualSenseBody : DS4Body);
            dc.DrawGeometry(p.Control, p.ControlPen, bodyGeom);

            // 3. Controller controls
            if (xbox)
            {
                DrawGuideButton(dc, p, new Point(200, 108));
                DrawCircleButton(dc, p, new Point(162, 108), 7, Btn.Share, ArrowLeft);
                DrawCircleButton(dc, p, new Point(238, 108), 7, Btn.Options, ArrowRight);

                DrawStick(dc, p, new Point(105, 105), LeftX, LeftY, Btn.L3);
                DrawDpad(dc, p, new Point(146, 168));
                DrawStick(dc, p, new Point(254, 168), RightX, RightY, Btn.R3);

                DrawXboxFace(dc, p, new Point(295, 105), dpi);
            }
            else
            {
                DrawTouchpad(dc, p, dualSense);
                DrawPillButton(dc, p, dualSense ? new Rect(123, 66, 10, 20) : new Rect(129, 66, 10, 20), Btn.Share);
                DrawOptionsButton(dc, p, dualSense ? new Rect(267, 66, 10, 20) : new Rect(261, 66, 10, 20));
                DrawPsButton(dc, p, new Point(200, 138), dpi);
                if (dualSense) DrawMuteButton(dc, p);

                DrawDpad(dc, p, new Point(105, 114));
                DrawPsFace(dc, p, new Point(295, 114));

                DrawStick(dc, p, new Point(146, 172), LeftX, LeftY, Btn.L3);
                DrawStick(dc, p, new Point(254, 172), RightX, RightY, Btn.R3);
            }
        }
        finally
        {
            dc.Pop();
        }
    }

    private void DrawBumper(DrawingContext dc, Palette p, Rect r, Btn flag, string label, double dpi)
    {
        bool pressed = (Buttons & flag) != 0;
        dc.DrawRoundedRectangle(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, r, 4, 4);
        var ft = GetText(label, pressed, 9.5, dpi);
        dc.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
    }

    private void DrawTrigger(DrawingContext dc, Palette p, Rect r, double analogVal, string label, double dpi)
    {
        double val = Math.Clamp(analogVal, 0.0, 1.0); // analog only: the Xbox output never sends the digital bits
        dc.DrawRoundedRectangle(p.Inset, p.InsetPen, r, 4, 4);
        if (val > 0.005)
        {
            dc.PushClip(new RectangleGeometry(r, 4, 4));
            double fillH = (r.Height - 2) * val;
            dc.DrawRectangle(p.Accent, null, new Rect(r.X + 1, r.Bottom - 1 - fillH, r.Width - 2, fillH));
            dc.Pop();
        }
        bool filled = val >= 0.5;
        var ft = GetText(label, filled, 9.5, dpi);
        dc.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
    }

    private void DrawStick(DrawingContext dc, Palette p, Point wellCenter, double xVal, double yVal, Btn clickFlag)
    {
        const double wellR = 24;
        const double thumbR = 15;
        const double travel = wellR * 0.35;

        dc.DrawEllipse(p.Inset, p.InsetPen, wellCenter, wellR, wellR);
        dc.DrawEllipse(null, p.GatePen, wellCenter, 14, 14);

        double sx = Math.Clamp(xVal, -1.0, 1.0) * travel;
        double sy = -Math.Clamp(yVal, -1.0, 1.0) * travel; // Up is positive
        var thumbCenter = new Point(wellCenter.X + sx, wellCenter.Y + sy);

        bool pressed = (Buttons & clickFlag) != 0;
        dc.DrawEllipse(pressed ? p.Accent : p.Control, pressed ? null : p.InsetPen, thumbCenter, thumbR, thumbR);
        dc.DrawEllipse(null, pressed ? p.OnAccentPen : p.TextDimPen, thumbCenter, 7, 7);
    }

    private void DrawDpad(DrawingContext dc, Palette p, Point c)
    {
        dc.DrawEllipse(p.Inset, p.InsetPen, c, 22, 22);
        DrawDpadArm(dc, p, new Rect(c.X - 5.5, c.Y - 20, 11, 13), new Point(c.X, c.Y - 14), Btn.DpadUp, ArrowUp);
        DrawDpadArm(dc, p, new Rect(c.X - 5.5, c.Y + 7, 11, 13), new Point(c.X, c.Y + 14), Btn.DpadDown, ArrowDown);
        DrawDpadArm(dc, p, new Rect(c.X - 20, c.Y - 5.5, 13, 11), new Point(c.X - 14, c.Y), Btn.DpadLeft, ArrowLeft);
        DrawDpadArm(dc, p, new Rect(c.X + 7, c.Y - 5.5, 13, 11), new Point(c.X + 14, c.Y), Btn.DpadRight, ArrowRight);
        dc.DrawEllipse(p.Control, p.InsetPen, c, 5, 5);
    }

    private void DrawDpadArm(DrawingContext dc, Palette p, Rect r, Point arrowPt, Btn flag, Geometry arrow)
    {
        bool pressed = (Buttons & flag) != 0;
        dc.DrawRoundedRectangle(pressed ? p.Accent : p.Control, pressed ? null : p.InsetPen, r, 2, 2);
        dc.PushTransform(new TranslateTransform(arrowPt.X, arrowPt.Y));
        dc.DrawGeometry(pressed ? p.OnAccent : p.TextDim, null, arrow);
        dc.Pop();
    }

    private void DrawPsFace(DrawingContext dc, Palette p, Point c)
    {
        DrawPsFaceBtn(dc, p, new Point(c.X, c.Y - 18), Btn.Triangle, 0);
        DrawPsFaceBtn(dc, p, new Point(c.X + 18, c.Y), Btn.Circle, 1);
        DrawPsFaceBtn(dc, p, new Point(c.X, c.Y + 18), Btn.Cross, 2);
        DrawPsFaceBtn(dc, p, new Point(c.X - 18, c.Y), Btn.Square, 3);
    }

    private void DrawPsFaceBtn(DrawingContext dc, Palette p, Point pt, Btn flag, int shape)
    {
        bool pressed = (Buttons & flag) != 0;
        dc.DrawEllipse(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, pt, 10, 10);
        var pen = pressed ? p.OnAccentPen : p.TextDimPen;
        switch (shape)
        {
            case 0: // Triangle
                dc.PushTransform(new TranslateTransform(pt.X, pt.Y));
                dc.DrawGeometry(null, pen, TriangleGeom);
                dc.Pop();
                break;
            case 1: // Circle
                dc.DrawEllipse(null, pen, pt, 4.2, 4.2);
                break;
            case 2: // Cross
                dc.DrawLine(pen, new Point(pt.X - 4, pt.Y - 4), new Point(pt.X + 4, pt.Y + 4));
                dc.DrawLine(pen, new Point(pt.X - 4, pt.Y + 4), new Point(pt.X + 4, pt.Y - 4));
                break;
            case 3: // Square
                dc.DrawRectangle(null, pen, new Rect(pt.X - 3.8, pt.Y - 3.8, 7.6, 7.6));
                break;
        }
    }

    private void DrawXboxFace(DrawingContext dc, Palette p, Point c, double dpi)
    {
        DrawXboxFaceBtn(dc, p, new Point(c.X, c.Y - 18), Btn.Triangle, "Y", dpi);
        DrawXboxFaceBtn(dc, p, new Point(c.X + 18, c.Y), Btn.Circle, "B", dpi);
        DrawXboxFaceBtn(dc, p, new Point(c.X, c.Y + 18), Btn.Cross, "A", dpi);
        DrawXboxFaceBtn(dc, p, new Point(c.X - 18, c.Y), Btn.Square, "X", dpi);
    }

    private void DrawXboxFaceBtn(DrawingContext dc, Palette p, Point pt, Btn flag, string letter, double dpi)
    {
        bool pressed = (Buttons & flag) != 0;
        dc.DrawEllipse(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, pt, 10, 10);
        var ft = GetText(letter, pressed, 11, dpi);
        dc.DrawText(ft, new Point(pt.X - ft.Width / 2, pt.Y - ft.Height / 2));
    }

    private void DrawTouchpad(DrawingContext dc, Palette p, bool dualSense)
    {
        var r = dualSense ? new Rect(142, 52, 116, 52) : new Rect(150, 54, 100, 48);
        bool pressed = (Buttons & Btn.TouchpadClick) != 0;
        dc.DrawRoundedRectangle(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, r, dualSense ? 6 : 4, dualSense ? 6 : 4);
        dc.DrawEllipse(pressed ? p.OnAccent : p.TextDim, null, new Point(200, r.Y + r.Height / 2), 2, 2);
    }

    private void DrawPillButton(DrawingContext dc, Palette p, Rect r, Btn flag)
    {
        bool pressed = (Buttons & flag) != 0;
        dc.DrawRoundedRectangle(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, r, 4, 4);
    }

    private void DrawOptionsButton(DrawingContext dc, Palette p, Rect r)
    {
        bool pressed = (Buttons & Btn.Options) != 0;
        dc.DrawRoundedRectangle(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, r, 4, 4);
        var pen = pressed ? p.OnAccentPen : p.TextDimPen;
        double mx = r.X + r.Width / 2, my = r.Y + r.Height / 2;
        dc.DrawLine(pen, new Point(mx - 2.5, my - 3), new Point(mx + 2.5, my - 3));
        dc.DrawLine(pen, new Point(mx - 2.5, my), new Point(mx + 2.5, my));
        dc.DrawLine(pen, new Point(mx - 2.5, my + 3), new Point(mx + 2.5, my + 3));
    }

    private void DrawPsButton(DrawingContext dc, Palette p, Point c, double dpi)
    {
        bool pressed = (Buttons & Btn.PS) != 0;
        dc.DrawEllipse(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, c, 10, 10);
        var ft = GetText("PS", pressed, 8.5, dpi);
        dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
    }

    private void DrawMuteButton(DrawingContext dc, Palette p)
    {
        var r = new Rect(192, 154, 16, 7);
        bool pressed = (Buttons & Btn.Mute) != 0;
        dc.DrawRoundedRectangle(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, r, 3, 3);
        dc.DrawEllipse(pressed ? p.OnAccent : p.TextDim, null, new Point(200, 157.5), 1.5, 1.5);
    }

    private void DrawGuideButton(DrawingContext dc, Palette p, Point c)
    {
        bool pressed = (Buttons & Btn.PS) != 0;
        dc.DrawEllipse(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, c, 13, 13);
        var pen = pressed ? p.OnAccentPen : p.TextDimPen;
        dc.DrawLine(pen, new Point(c.X - 5, c.Y - 5), new Point(c.X + 5, c.Y + 5));
        dc.DrawLine(pen, new Point(c.X - 5, c.Y + 5), new Point(c.X + 5, c.Y - 5));
    }

    private void DrawCircleButton(DrawingContext dc, Palette p, Point c, double r, Btn flag, Geometry arrow)
    {
        bool pressed = (Buttons & flag) != 0;
        dc.DrawEllipse(pressed ? p.Accent : p.Inset, pressed ? null : p.InsetPen, c, r, r);
        dc.PushTransform(new TranslateTransform(c.X, c.Y));
        dc.DrawGeometry(pressed ? p.OnAccent : p.TextDim, null, arrow);
        dc.Pop();
    }

    private static FormattedText GetText(string text, bool isAccent, double size, double dpi)
    {
        int dpiKey = (int)Math.Round(dpi * 100);
        int sizeKey = (int)Math.Round(size * 10);
        var key = (text, isAccent, sizeKey, dpiKey);
        if (!TextCache.TryGetValue(key, out var ft))
        {
            ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                UiFont,
                size,
                isAccent ? P.OnAccent : P.TextDim,
                dpi);
            TextCache[key] = ft;
        }
        return ft;
    }
}
