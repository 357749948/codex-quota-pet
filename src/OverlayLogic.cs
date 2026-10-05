using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodexQuotaPet
{
    public sealed class OverlayPlacement
    {
        public int X, Y, Width, Height;
    }

    public static class OverlayLogic
    {
        public static OverlayPlacement PlaceOnFace(PetAnchor a)
        {
            // Do not clamp this layer away from its pet; its transparent canvas must
            // share the pet's exact physical bounds, even at monitor edges.
            return new OverlayPlacement { X = (int)Math.Round(a.Left), Y = (int)Math.Round(a.Top), Width = (int)Math.Round(a.Width), Height = (int)Math.Round(a.Height) };
        }
        public static OverlayPlacement Place(PetAnchor a, double widthDip, double heightDip)
        {
            double scale = (a.Dpi == 0 ? 96 : a.Dpi) / 96.0;
            double w = widthDip * scale, h = heightDip * scale, gap = 8 * scale;
            double x = a.Left + (a.Width - w) / 2, y = a.Top - h - gap;
            if (y < a.WorkTop)
            {
                x = a.Left + a.Width + gap;
                y = a.Top + (a.Height - h) / 2;
                if (x + w > a.WorkLeft + a.WorkWidth) x = a.Left - gap - w;
            }
            x = Math.Max(a.WorkLeft, Math.Min(x, a.WorkLeft + a.WorkWidth - w));
            y = Math.Max(a.WorkTop, Math.Min(y, a.WorkTop + a.WorkHeight - h));
            return new OverlayPlacement { X = (int)Math.Round(x), Y = (int)Math.Round(y), Width = (int)Math.Round(w), Height = (int)Math.Round(h) };
        }

        public static string Freshness(QuotaSnapshot q, DateTime now)
        {
            if (q == null) return "正在读取";
            if (q.State == "login_required") return "请登录 Codex";
            if (!q.LastSuccessUtc.HasValue) return q.State == "error" ? "暂时无法读取" : "正在读取";
            double age = (now - q.LastSuccessUtc.Value).TotalSeconds;
            if (age >= 300) return "离线 · " + Beijing(q.LastSuccessUtc.Value).ToString("HH:mm", CultureInfo.InvariantCulture);
            if (age >= 90) return "待更新";
            return "";
        }

        public static DateTime Beijing(DateTime utc)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(8);
        }

        public static string Percentage(double? n)
        {
            return n.HasValue ? Math.Max(0, Math.Min(100, n.Value)).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "--";
        }

        public static string Accent(double? remaining)
        {
            if (!remaining.HasValue) return "#8B95A5";
            return remaining.Value <= 10 ? "#FF737A" : remaining.Value <= 30 ? "#F1B75A" : "#74D5C1";
        }

        public static void RunSelfTests()
        {
            var a = new PetAnchor { Left = 3040, Top = 1730, Width = 160, Height = 174, WorkLeft = 0, WorkTop = 0, WorkWidth = 3200, WorkHeight = 1940, Dpi = 192 };
            var face = PlaceOnFace(a);
            Require(face.X == 3040 && face.Y == 1730 && face.Width == 160 && face.Height == 174, "screen layer shares pet physical coordinates");
            var p = Place(a, 112, 44);
            Require(p.X == 2976 && p.Y == 1626 && p.Width == 224 && p.Height == 88, "200% placement at right edge");
            a.Left = -1800; a.Top = 0; a.WorkLeft = -1920; a.WorkWidth = 1920; a.WorkHeight = 1040; a.Dpi = 96;
            p = Place(a, 112, 44);
            Require(p.X == -1632 && p.Y == 65, "top-edge side placement on negative monitor");
            a.Left = -1800; a.Top = 500; a.Dpi = 144;
            p = Place(a, 112, 74);
            Require(p.Height == 111 && p.Y == 377, "mixed DPI dual rows");
            DateTime now = new DateTime(2026, 10, 5, 1, 0, 0, DateTimeKind.Utc);
            var q = new QuotaSnapshot { State = "ready", LastSuccessUtc = now.AddSeconds(-89) };
            Require(Freshness(q, now) == "", "fresh before 90 seconds");
            q.LastSuccessUtc = now.AddSeconds(-90); Require(Freshness(q, now) == "待更新", "stale at 90 seconds");
            q.LastSuccessUtc = now.AddMinutes(-5); Require(Freshness(q, now) == "离线 · 08:55", "offline threshold and Beijing time");
            q.State = "login_required"; Require(Freshness(q, now) == "请登录 Codex", "login state overrides old data");
            Require(Percentage(null) == "--" && Percentage(0) == "0%", "missing is not zero");
            Require(Accent(30) == "#F1B75A" && Accent(10) == "#FF737A", "color thresholds");
        }

        private static void Require(bool value, string description)
        {
            if (!value) throw new Exception("Overlay: " + description);
        }
    }
}
