using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexQuotaPet
{
    public sealed class QuotaWindowView : Window
    {
        private readonly ScreenCanvas _canvas = new ScreenCanvas();
        public IntPtr Handle { get; private set; }

        public QuotaWindowView()
        {
            Title = "Codex 机器人屏幕额度";
            Width = 80; Height = 87;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
            Content = _canvas;
            SourceInitialized += delegate
            {
                Handle = new WindowInteropHelper(this).Handle;
                Native.MakePassive(Handle);
                HwndSource.FromHwnd(Handle).AddHook(Hook);
            };
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x84) { handled = true; return new IntPtr(-1); }
            if (msg == 0x21) { handled = true; return new IntPtr(3); }
            return IntPtr.Zero;
        }

        public void Render(QuotaSnapshot quota, ScreenMatch match)
        {
            if (match == null || match.Frame == null || match.Anchor == null) return;
            double scale = (match.Anchor.Dpi == 0 ? 96 : match.Anchor.Dpi) / 96.0;
            Width = match.Anchor.Width / scale;
            Height = match.Anchor.Height / scale;
            QuotaWindow main = null;
            if (quota != null && quota.Windows != null)
            {
                foreach (QuotaWindow window in quota.Windows)
                {
                    if (main == null || window.WindowMinutes == 10080) main = window;
                    if (window.WindowMinutes == 10080) break;
                }
            }
            bool fresh = quota != null && quota.State != "login_required" && OverlayLogic.Freshness(quota, DateTime.UtcNow).Length == 0;
            double? remaining = fresh && main != null ? main.RemainingPercent : null;
            _canvas.Set(match.Frame, OverlayLogic.Percentage(remaining), OverlayLogic.Accent(remaining));
        }
    }

    public sealed class ScreenCanvas : FrameworkElement
    {
        private ScreenFrame _frame;
        private string _text, _color;
        private Geometry _mask;
        private readonly Dictionary<ScreenFrame, Geometry> _masks = new Dictionary<ScreenFrame, Geometry>();

        public ScreenCanvas()
        {
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            IsHitTestVisible = false;
        }

        public void Set(ScreenFrame frame, string text, string color)
        {
            if (_frame == frame && _text == text && _color == color) return;
            _frame = frame; _text = text; _color = color;
            if (!_masks.TryGetValue(frame, out _mask))
            {
                var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
                using (var c = geometry.Open())
                {
                    foreach (ScreenRun run in frame.Mask)
                    {
                        c.BeginFigure(new Point(run.X, run.Y), true, true);
                        c.LineTo(new Point(run.X + run.Width, run.Y), true, false);
                        c.LineTo(new Point(run.X + run.Width, run.Y + 1), true, false);
                        c.LineTo(new Point(run.X, run.Y + 1), true, false);
                    }
                }
                // A reloaded template may reuse an index with different geometry.
                if (_masks.Count >= 88) _masks.Clear();
                geometry.Freeze(); _mask = geometry; _masks.Add(frame, geometry);
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (_frame == null || !_frame.Supported || _mask == null || ActualWidth <= 0 || ActualHeight <= 0) return;
            dc.PushTransform(new ScaleTransform(ActualWidth / 192.0, ActualHeight / 208.0));
            var background = new SolidColorBrush(Color.FromRgb((byte)_frame.BackgroundR, (byte)_frame.BackgroundG, (byte)_frame.BackgroundB));
            background.Freeze();
            dc.DrawGeometry(background, null, _mask);
            dc.PushClip(_mask);
            var foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_color));
            foreground.Freeze();
            var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var formatted = new FormattedText(_text ?? "--", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 48, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            Geometry glyphs = formatted.BuildGeometry(new Point(0, 0));
            var stroke = new Pen(foreground, .6);
            Rect ink = glyphs.GetRenderBounds(stroke);
            // Fit the visible strokes, not the font's line box and unused leading.
            // This fills the safe screen area while still fitting four-character 100%.
            double fit = Math.Min(_frame.TextWidth * .98 / Math.Max(1, ink.Width), _frame.TextHeight * .98 / Math.Max(1, ink.Height));
            double centerX = _frame.TextLeft + _frame.TextWidth / 2;
            double centerY = _frame.TextTop + _frame.TextHeight / 2;
            dc.PushTransform(new RotateTransform(_frame.Angle, centerX, centerY));
            dc.PushTransform(new TranslateTransform(centerX, centerY));
            dc.PushTransform(new ScaleTransform(fit, fit));
            dc.PushTransform(new TranslateTransform(-ink.X - ink.Width / 2, -ink.Y - ink.Height / 2));
            dc.DrawGeometry(foreground, stroke, glyphs);
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
}
