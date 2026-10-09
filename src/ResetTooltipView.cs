using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexQuotaPet
{
    // An independent passive HWND: the face must remain click-through, and the
    // tooltip must never cover pixels used by the pet's screen matcher.
    public sealed class ResetTooltipView : Window
    {
        private readonly StackPanel _rows = new StackPanel();
        private readonly Border _border;
        private string _contentKey;
        public IntPtr Handle { get; private set; }
        public double MeasuredWidth { get; private set; }
        public double MeasuredHeight { get; private set; }

        public ResetTooltipView()
        {
            Title = "Codex 额度重置时间";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
            Foreground = Brush("#EDF1F7");
            _border = new Border
            {
                Background = Brush("#1C2028"), BorderBrush = Brush("#46505F"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 10), Child = _rows,
                IsHitTestVisible = false
            };
            Content = _border;
            SourceInitialized += delegate
            {
                Handle = new WindowInteropHelper(this).Handle;
                Native.MakePassive(Handle);
                HwndSource.FromHwnd(Handle).AddHook(Hook);
            };
        }

        private static Brush Brush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze(); return brush;
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x84) { handled = true; return new IntPtr(-1); }
            if (msg == 0x21) { handled = true; return new IntPtr(3); }
            return IntPtr.Zero;
        }

        public void Render(HoverContent content)
        {
            string key = content.Title + "\n" + content.Status;
            foreach (HoverRow row in content.Rows) key += "\n" + row.Label + "\t" + row.Value;
            key += "\n--credits--";
            foreach (HoverRow row in content.ResetCreditRows) key += "\n" + row.Label + "\t" + row.Value;
            key += "\n" + content.ResetCreditNotice;
            if (key == _contentKey) return;
            _contentKey = key;
            _rows.Children.Clear();
            _rows.Children.Add(new TextBlock
            {
                Text = content.Title, Foreground = Brush("#B9C5D6"),
                TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 0, 5)
            });
            if (!String.IsNullOrEmpty(content.Status))
                _rows.Children.Add(new TextBlock { Text = content.Status, TextWrapping = TextWrapping.NoWrap });
            AddRows(content.Rows);
            _rows.Children.Add(new Border { Height = 1, Background = Brush("#46505F"), Margin = new Thickness(0, 8, 0, 6) });
            AddRows(content.ResetCreditRows);
            if (!String.IsNullOrEmpty(content.ResetCreditNotice))
                _rows.Children.Add(new TextBlock { Text = content.ResetCreditNotice, Foreground = Brush("#B9C5D6"),
                    TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 4, 0, 0) });
            // Measure in logical pixels. The host places the HWND in physical
            // pixels using the pet monitor's DPI, just like the face overlay.
            // Natural sizing leaves placement to hide an oversized tooltip,
            // rather than wrapping or clipping its expiry information.
            _border.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            MeasuredWidth = Math.Ceiling(_border.DesiredSize.Width);
            _border.Measure(new Size(MeasuredWidth, Double.PositiveInfinity));
            MeasuredHeight = Math.Ceiling(_border.DesiredSize.Height);
            // WPF can coerce Window.Height to the monitor's maximum tracking
            // size. Keep the full dimensions for the placement fit check.
            Width = MeasuredWidth;
            Height = MeasuredHeight;
        }

        private void AddRows(System.Collections.Generic.List<HoverRow> rows)
        {
            foreach (HoverRow row in rows)
            {
                var text = new TextBlock { TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 2, 0, 0) };
                text.Inlines.Add(new Run(row.Label + "："));
                text.Inlines.Add(new Run(row.Value) { FontWeight = FontWeights.Bold });
                _rows.Children.Add(text);
            }
        }
    }
}
