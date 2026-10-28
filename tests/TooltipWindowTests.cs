using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
            CreditLayout(tip, q, now);
        }
        finally { tip.Close(); face.Close(); }
        Console.WriteLine("PASS quota/credit tooltip layout at 3 scales and actual passive HWND messages/styles");
    }

    private static void CreditLayout(ResetTooltipView tip, QuotaSnapshot snapshot, DateTime now)
    {
        snapshot.State = "ready";
        snapshot.Windows.Add(new QuotaWindow { Label = "5小时", ResetsAtUtc = now.AddHours(3) });
        snapshot.CreditBalance = new CreditBalanceSnapshot { Balance = 12345.6789m, LastSuccessUtc = now };
        snapshot.ResetCredits = new ResetCreditsSnapshot { AvailableCount = 4, LastSuccessUtc = now, DetailsAvailable = true,
            Credits = new List<ResetCredit> {
                new ResetCredit { ExpiresAtUtc = now.AddDays(3) },
                new ResetCredit { ExpiresAtUtc = now.AddYears(1) },
                new ResetCredit(), new ResetCredit { NeverExpires = true }
            } };
        tip.Render(HoverLogic.Content(snapshot, now));
        double detailHeight = tip.Height;
        var border = (Border)tip.Content;
        var rows = (StackPanel)border.Child;
        int dividerIndex = 1 + snapshot.Windows.Count;
        int balanceIndex = dividerIndex + 1;
        int countIndex = balanceIndex + 1;
        Require(rows.Children.Count == countIndex + 1 + snapshot.ResetCredits.Credits.Count, "title, periods, divider, balance, count and every detail are rendered");
        var divider = rows.Children[dividerIndex] as Border;
        Require(divider != null && divider.Height == 1 && divider.Background != null, "real thin line separates period dates from credits");
        Require(((Run)((TextBlock)rows.Children[balanceIndex]).Inlines.FirstInline).Text == "剩余额度：" &&
            ((Run)((TextBlock)rows.Children[balanceIndex]).Inlines.LastInline).Text == "12,345.68", "balance follows divider and precedes reset-credit count");
        Require(border.Padding.Left == border.Padding.Right && border.Padding.Left == 12, "balanced existing padding retained");
        for (int i = balanceIndex; i < rows.Children.Count; i++)
        {
            var text = rows.Children[i] as TextBlock;
            Require(text != null && text.FontSize == 14 && text.TextWrapping == TextWrapping.NoWrap, "count and every expiration use full-size unwrapped text");
            var value = text.Inlines.LastInline as Run;
            Require(value != null && value.FontWeight == FontWeights.Bold, "credit count and date are bold");
        }
        Require(tip.Width < 330 && tip.Height > 170, "cross-year credit date fits natural width and all rows have space");
        foreach (uint dpi in new uint[] { 96, 144, 192 })
        {
            double scale = dpi / 96.0;
            border.Measure(new Size(tip.Width, tip.Height));
            border.Arrange(new Rect(0, 0, tip.Width, tip.Height)); border.UpdateLayout();
            foreach (UIElement child in rows.Children)
            {
                var text = child as TextBlock;
                if (text == null) continue;
                Require(text.ActualWidth + .01 >= text.DesiredSize.Width, "no horizontal text clipping at scale " + scale);
                Require(text.ActualHeight + .01 >= text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom, "no vertical text clipping at scale " + scale);
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(tip.Width * scale), (int)Math.Ceiling(tip.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(border);
            Require(bitmap.PixelHeight >= detailHeight * scale, "all credit details rendered at scale " + scale);
            var anchor = new PetAnchor { Visible = true, Left = -600, Top = 650, Width = 160 * scale, Height = 174 * scale,
                WorkLeft = -1920, WorkTop = -200, WorkWidth = 1920, WorkHeight = 1280, Dpi = dpi };
            OverlayPlacement placement = HoverLogic.Place(anchor, tip.MeasuredWidth, tip.MeasuredHeight);
            Require(placement != null && placement.X >= anchor.WorkLeft && placement.Y >= anchor.WorkTop &&
                placement.X + placement.Width <= 0 && placement.Y + placement.Height <= 1080, "credit tooltip stays inside negative-coordinate monitor");
            Require(placement.Y + placement.Height <= anchor.Top - 8 * scale ||
                placement.X + placement.Width <= anchor.Left - 8 * scale || placement.X >= anchor.Left + anchor.Width + 8 * scale ||
                placement.Y >= anchor.Top + anchor.Height + 8 * scale, "credit tooltip does not obscure pet at scale " + scale);
        }
        snapshot.CreditBalance.Balance = 9876.5m;
        tip.Render(HoverLogic.Content(snapshot, now));
        Require(((Run)((TextBlock)rows.Children[balanceIndex]).Inlines.LastInline).Text == "9,876.5", "balance-only update invalidates render cache");
        Require(rows.Children.Count == countIndex + 1 + snapshot.ResetCredits.Credits.Count, "balance update adds no expiry placeholder");
        snapshot.ResetCredits.AvailableCount = 6;
        tip.Render(HoverLogic.Content(snapshot, now));
        Require(rows.Children.Count == countIndex + 2 + snapshot.ResetCredits.Credits.Count && tip.Height > detailHeight, "partial-detail notice participates in content sizing");
        Require(((TextBlock)rows.Children[rows.Children.Count - 1]).Text == "仅返回 4 / 6 项到期明细", "partial-detail notice is readable text");
        snapshot.ResetCredits.AvailableCount = 0;
        tip.Render(HoverLogic.Content(snapshot, now));
        Require(rows.Children.Count == countIndex + 1 && tip.Height < detailHeight, "zero removes all credit details and notice immediately");
        snapshot.ResetCredits.AvailableCount = 4; snapshot.ResetCredits.LastSuccessUtc = now.AddSeconds(-90);
        tip.Render(HoverLogic.Content(snapshot, now));
        Require(rows.Children.Count == countIndex + 1 && ((Run)((TextBlock)rows.Children[countIndex]).Inlines.LastInline).Text == "待更新", "stale section removes old date rows");
        snapshot.ResetCredits.LastSuccessUtc = now;
        snapshot.ResetCredits.Credits.Clear();
        for (int i = 0; i < 80; i++) snapshot.ResetCredits.Credits.Add(new ResetCredit { ExpiresAtUtc = now.AddDays(i + 1) });
        snapshot.ResetCredits.AvailableCount = 80;
        tip.Render(HoverLogic.Content(snapshot, now));
        Require(rows.Children.Count == countIndex + 1 + snapshot.ResetCredits.Credits.Count && tip.MeasuredHeight > 1080, "full content dimensions preserved even if WPF coerces the HWND size");
        Require(HoverLogic.Place(new PetAnchor { Visible = true, Left = 300, Top = 500, Width = 160, Height = 174,
            WorkWidth = 1920, WorkHeight = 1080, Dpi = 96 }, tip.MeasuredWidth, tip.MeasuredHeight) == null, "tooltip that cannot fit is hidden rather than overlapping or clipping");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("Tooltip window: " + description);
    }
}
