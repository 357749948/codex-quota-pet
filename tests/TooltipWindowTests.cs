using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexQuotaPet;

internal static class TooltipWindowTests
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int message, IntPtr w, IntPtr l);

    public static void Run()
    {
        var face = new QuotaWindowView();
        var tip = new ResetTooltipView();
        try
        {
            foreach (Window window in new Window[] { face, tip })
            {
                IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
                long required = 0x80000 | 0x20 | 0x80 | 0x08000000;
                Require((Native.GetExStyle(handle) & required) == required, "actual HWND is layered, transparent, toolwindow and nonactivating");
                Require(SendMessage(handle, 0x84, IntPtr.Zero, IntPtr.Zero).ToInt64() == -1, "hit test passes through");
                Require(SendMessage(handle, 0x21, IntPtr.Zero, IntPtr.Zero).ToInt64() == 3, "mouse activation denied");
                Require(!window.ShowActivated && !window.Focusable && !window.ShowInTaskbar, "passive WPF properties");
            }
            var now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var q = new QuotaSnapshot { State = "ready", LastSuccessUtc = now,
                Windows = new System.Collections.Generic.List<QuotaWindow> {
                    new QuotaWindow { Label = "本周", ResetsAtUtc = now.AddDays(4) },
                    new QuotaWindow { Label = "5小时", ResetsAtUtc = now.AddHours(3) }
                } };
            tip.Render(HoverLogic.Content(q, now));
            Require(tip.Width >= 160 && tip.Width < 240 && tip.Height > 65, "two readable rows fit content without excess minimum width");
            double dualHeight = tip.Height;
            var visual = (FrameworkElement)tip.Content;
            foreach (double scale in new double[] { 1, 1.5, 2 })
            {
                visual.Measure(new Size(tip.Width, tip.Height));
                visual.Arrange(new Rect(0, 0, tip.Width, tip.Height)); visual.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(tip.Width * scale), (int)Math.Ceiling(tip.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                Require(bitmap.PixelWidth >= 160 * scale && bitmap.PixelHeight > 65 * scale, "tooltip render at DPI scale " + scale);
            }
            q.Windows.RemoveAt(1); tip.Render(HoverLogic.Content(q, now));
            Require(tip.Height < dualHeight, "one period removes the second row");
            q.State = "login_required"; tip.Render(HoverLogic.Content(q, now));
            Require(tip.Height < dualHeight, "login state removes old reset rows");
        }
        finally { tip.Close(); face.Close(); }
        Console.WriteLine("PASS tooltip layout at 3 scales and actual passive HWND messages/styles");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("Tooltip window: " + description);
    }
}
