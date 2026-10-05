using System;
using System.Collections.Generic;
using CodexQuotaPet;

internal static class HoverTests
{
    private static int _checks;

    public static void Run()
    {
        ResetContent(); HitAreas(); Placements(); Dwell(); JumpingPet();
        Console.WriteLine("PASS hover/reset logic assertions: " + _checks);
    }

    private static void JumpingPet()
    {
        var anchor = new PetAnchor { Visible = true, Left = 100, Top = 100, Width = 192, Height = 208, Dpi = 96, WindowHandle = 42 };
        var idle = new ScreenMatch { Visible = true, Anchor = anchor,
            Frame = new ScreenFrame { Supported = true, TextLeft = 64, TextTop = 90, TextWidth = 64, TextHeight = 30 } };
        var jumping = new ScreenMatch { Visible = true, Anchor = anchor,
            Frame = new ScreenFrame { Supported = true, TextLeft = 64, TextTop = 40, TextWidth = 64, TextHeight = 30 } };
        var sidePose = new ScreenMatch { Visible = false, Anchor = anchor, Frame = new ScreenFrame { Supported = false } };
        var unknown = new ScreenMatch { Visible = false, Anchor = anchor };
        var session = new HoverSession();
        Require(!session.Update(true, idle, anchor, 196, 205, false, 0), "animation session starts dwell");
        Require(!session.Update(true, jumping, anchor, 196, 205, false, 200), "jump before dwell does not reset hotspot");
        Require(session.Update(true, jumping, anchor, 196, 205, false, 300), "stationary mouse opens tip despite jumping digits");
        Require(session.Update(true, sidePose, anchor, 196, 205, false, 1000), "recognized side pose preserves tip");
        Require(session.Update(true, unknown, anchor, 196, 205, false, 1500), "brief unknown transition preserves tip");
        Require(session.Update(true, idle, anchor, 196, 205, false, 1600), "matching resumes without blinking");
        Require(!session.Update(true, jumping, anchor, 280, 300, false, 1700), "real mouse leave hides immediately");
        Require(!session.Update(true, jumping, anchor, 196, 205, false, 2100), "old hotspot cannot restart after leaving");
        session.Update(true, idle, anchor, 196, 205, false, 2200);
        Require(session.Update(true, idle, anchor, 196, 205, false, 2500), "new recognized entry opens tip");
        Require(!session.Update(true, idle, anchor, 196, 205, true, 2600), "button dismisses animation session");
        Require(!session.Update(true, idle, anchor, 196, 205, false, 2700), "release restarts dwell");
        Require(session.Update(true, idle, anchor, 196, 205, false, 3000), "release dwell completes");
        Require(!session.Update(true, unknown, anchor, 196, 205, false, 3751), "sustained recognition loss dismisses tip");
        session.Update(true, idle, anchor, 196, 205, false, 4000);
        Require(session.Update(true, idle, anchor, 196, 205, false, 4300), "session reenters");
        Require(!session.Update(false, idle, anchor, 196, 205, false, 4301), "pet hidden or session locked dismisses without grace");
        session.Update(true, idle, anchor, 196, 205, false, 5000);
        anchor.WindowHandle++;
        Require(!session.Update(true, idle, anchor, 196, 205, false, 5400), "replacement window restarts dwell");
        Require(session.Update(true, idle, anchor, 196, 205, false, 5700), "replacement window dwell completes");
        anchor.Dpi = 144;
        Require(!session.Update(true, idle, anchor, 196, 205, false, 5800), "DPI change resets old physical hotspot");
    }

    private static void ResetContent()
    {
        DateTime now = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);
        var weekly = new QuotaWindow { Label = "本周", WindowMinutes = 10080, ResetsAtUtc = now.AddDays(3) };
        var shortWindow = new QuotaWindow { Label = "5小时", WindowMinutes = 300, ResetsAtUtc = now.AddHours(2) };
        var snapshot = new QuotaSnapshot { State = "ready", LastSuccessUtc = now, Windows = new List<QuotaWindow> { weekly } };
        HoverContent content = HoverLogic.Content(snapshot, now);
        Require(content.Title == "下次重置（北京时间）" && content.Status == "" && content.Rows.Count == 1, "single fresh quota");
        Require(content.Rows[0].Label == "本周" && content.Rows[0].Value == "10月08日 23:00:00", "actual label and Beijing date");
        snapshot.Windows.Add(shortWindow);
        content = HoverLogic.Content(snapshot, now);
        Require(content.Rows.Count == 2 && content.Rows[1].Value == "10月06日 01:00:00", "dual quotas and Beijing midnight rollover");
        snapshot.Windows.Reverse();
        Require(HoverLogic.Content(snapshot, now).Rows[0].Label == "5小时", "preserves service ordering");
        shortWindow.ResetsAtUtc = null;
        Require(HoverLogic.Content(snapshot, now).Rows[0].Value == "暂不可用", "missing reset is unknown");
        shortWindow.ResetsAtUtc = now;
        Require(HoverLogic.Content(snapshot, now).Rows[0].Value == "等待更新", "exact reset boundary waits for query");
        shortWindow.ResetsAtUtc = now.AddSeconds(-1);
        Require(HoverLogic.Content(snapshot, now).Rows[0].Value == "等待更新", "past reset is never displayed as next reset");
        shortWindow.ResetsAtUtc = DateTime.MaxValue;
        Require(HoverLogic.Content(snapshot, now).Rows[0].Value == "暂不可用", "out of range Beijing conversion does not crash");
        shortWindow.ResetsAtUtc = new DateTime(2026, 12, 31, 17, 4, 5, DateTimeKind.Utc);
        Require(HoverLogic.Content(snapshot, now).Rows[0].Value == "2027年01月01日 01:04:05", "Beijing year difference includes year");
        DateTime yearBoundary = new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc);
        snapshot.LastSuccessUtc = yearBoundary;
        Require(HoverLogic.Content(snapshot, yearBoundary).Rows[0].Value == "01月01日 01:04:05", "comparison uses Beijing year for both dates");
        shortWindow.Label = "15分钟";
        Require(HoverLogic.Content(snapshot, yearBoundary).Rows[0].Label == "15分钟", "arbitrary actual cycle label retained");
        shortWindow.Label = null;
        Require(HoverLogic.Content(snapshot, yearBoundary).Rows[0].Label == "额度", "missing cycle has neutral label");
        snapshot.LastSuccessUtc = now.AddSeconds(-89);
        Require(HoverLogic.Content(snapshot, now).Rows.Count == 2, "fresh before ninety seconds");
        snapshot.LastSuccessUtc = now.AddSeconds(-90);
        RequireStatus(HoverLogic.Content(snapshot, now), "待更新", "stale has no dates");
        snapshot.LastSuccessUtc = now.AddSeconds(-300);
        RequireStatus(HoverLogic.Content(snapshot, now), "离线 · 22:55", "offline has no dates");
        snapshot.State = "login_required";
        RequireStatus(HoverLogic.Content(snapshot, now), "请登录 Codex", "login overrides old data");
        snapshot.State = "error"; snapshot.LastSuccessUtc = null;
        RequireStatus(HoverLogic.Content(snapshot, now), "暂时无法读取", "initial failure");
        snapshot.State = "loading";
        RequireStatus(HoverLogic.Content(snapshot, now), "正在读取", "loading");
        RequireStatus(HoverLogic.Content(null, now), "正在读取", "null snapshot");
        snapshot.State = "ready"; snapshot.LastSuccessUtc = now; snapshot.Windows = null;
        RequireStatus(HoverLogic.Content(snapshot, now), "暂无额度信息", "no windows");
        snapshot.Windows = new List<QuotaWindow> { null };
        RequireStatus(HoverLogic.Content(snapshot, now), "暂无额度信息", "null window ignored");
        snapshot.Windows = new List<QuotaWindow> { weekly };
        snapshot.LastSuccessUtc = now;
        Require(HoverLogic.Content(snapshot, now).Rows.Count == 1, "recovered fresh data replaces stale status");
    }

    private static void HitAreas()
    {
        foreach (uint dpi in new uint[] { 96, 144, 192 })
        {
            double scale = dpi / 96.0;
            foreach (double angle in new double[] { 0, 27, -27, 90 })
            {
                ScreenMatch match = Match(-1600, -240, 192 * scale, 208 * scale, dpi, angle);
                Require(HitAtLocal(match, 96, 94.5), "rotated center at DPI " + dpi);
                Require(HitAtLocal(match, 65, 78), "inside corner at angle " + angle);
                Require(!HitAtLocal(match, 62, 94.5), "left outside at angle " + angle);
                Require(!HitAtLocal(match, 96, 75), "top outside at angle " + angle);
                Require(!HitAtLocal(match, 130, 94.5), "right outside at angle " + angle);
                Require(!HitAtLocal(match, 96, 114), "bottom outside at angle " + angle);
            }
        }
        var nonuniform = Match(50.25, 20.75, 288, 400, 144, 38);
        Require(HitAtLocal(nonuniform, 65, 78) && !HitAtLocal(nonuniform, 62, 78), "independent physical X/Y scaling");
        var normal = Match(0, 0, 192, 208, 96, 0);
        Require(HoverLogic.HitTest(normal, 64, 77), "rectangle left/top edges included");
        Require(!HoverLogic.HitTest(normal, 128, 94) && !HoverLogic.HitTest(normal, 96, 112), "rectangle right/bottom edges excluded");
        Require(!HoverLogic.HitTest(null, 96, 94), "no match");
        normal.Visible = false; Require(!HoverLogic.HitTest(normal, 96, 94), "hidden match"); normal.Visible = true;
        normal.Anchor.Visible = false; Require(!HoverLogic.HitTest(normal, 96, 94), "hidden pet"); normal.Anchor.Visible = true;
        normal.Frame.Supported = false; Require(!HoverLogic.HitTest(normal, 96, 94), "unsupported pose"); normal.Frame.Supported = true;
        Require(!HoverLogic.HitTest(normal, Double.NaN, 94) && !HoverLogic.HitTest(normal, 96, Double.PositiveInfinity), "invalid cursor coordinates");
        normal.Frame.Angle = Double.NaN; Require(!HoverLogic.HitTest(normal, 96, 94), "invalid angle"); normal.Frame.Angle = 0;
        normal.Frame.TextWidth = -1; Require(!HoverLogic.HitTest(normal, 96, 94), "invalid text width"); normal.Frame.TextWidth = 64;
        normal.Anchor.Width = 0; Require(!HoverLogic.HitTest(normal, 96, 94), "zero pet width"); normal.Anchor.Width = 192;
        normal.Anchor.Height = Double.PositiveInfinity; Require(!HoverLogic.HitTest(normal, 96, 94), "infinite pet height");
    }

    private static ScreenMatch Match(double left, double top, double width, double height, uint dpi, double angle)
    {
        return new ScreenMatch {
            Visible = true,
            Anchor = new PetAnchor { Visible = true, Left = left, Top = top, Width = width, Height = height, Dpi = dpi, WindowHandle = 42 },
            Frame = new ScreenFrame { Supported = true, TextLeft = 64, TextTop = 77, TextWidth = 64, TextHeight = 35, Angle = angle }
        };
    }

    private static bool HitAtLocal(ScreenMatch match, double x, double y)
    {
        double cx = match.Frame.TextLeft + match.Frame.TextWidth / 2, cy = match.Frame.TextTop + match.Frame.TextHeight / 2;
        double radians = match.Frame.Angle * Math.PI / 180;
        double dx = x - cx, dy = y - cy;
        double rotatedX = cx + dx * Math.Cos(radians) - dy * Math.Sin(radians);
        double rotatedY = cy + dx * Math.Sin(radians) + dy * Math.Cos(radians);
        return HoverLogic.HitTest(match, match.Anchor.Left + rotatedX * match.Anchor.Width / 192,
            match.Anchor.Top + rotatedY * match.Anchor.Height / 208);
    }

    private static void Placements()
    {
        var a = new PetAnchor { Visible = true, Left = 300, Top = 250, Width = 192, Height = 208, WorkWidth = 800, WorkHeight = 600, Dpi = 96 };
        OverlayPlacement p = HoverLogic.Place(a, 260, 90);
        Require(p != null && p.X == 266 && p.Y == 152, "above is preferred"); Safe(a, p);
        a.Left = 200; a.Top = 0;
        p = HoverLogic.Place(a, 260, 90); Require(p != null && p.X == 400 && p.Y == 59, "right fallback"); Safe(a, p);
        a.Left = 600;
        p = HoverLogic.Place(a, 260, 90); Require(p != null && p.X == 332 && p.Y == 59, "left fallback"); Safe(a, p);
        a.Left = 54; a.WorkWidth = 300;
        p = HoverLogic.Place(a, 260, 90); Require(p != null && p.X == 20 && p.Y == 216, "below fallback"); Safe(a, p);
        a.WorkHeight = 250;
        Require(HoverLogic.Place(a, 260, 90) == null, "no room outside pet means hidden");
        Require(HoverLogic.Place(a, 301, 90) == null, "oversized tooltip hidden");
        Require(HoverLogic.Place(a, 100, 251) == null, "overheight tooltip hidden");
        Require(HoverLogic.Place(a, Double.NaN, 90) == null && HoverLogic.Place(a, 100, 0) == null, "invalid dimensions hidden");
        Require(HoverLogic.Place(null, 100, 90) == null, "missing anchor hidden");
        foreach (uint dpi in new uint[] { 96, 144, 192 })
        {
            double scale = dpi / 96.0;
            foreach (double horizontal in new double[] { 0, .5, 1 })
                foreach (double vertical in new double[] { 0, .5, 1 })
                {
                    a = new PetAnchor { Visible = true, WorkLeft = -1920, WorkTop = -1080, WorkWidth = 1920, WorkHeight = 1080,
                        Left = -1920 + horizontal * (1920 - 192 * scale), Top = -1080 + vertical * (1080 - 208 * scale),
                        Width = 192 * scale, Height = 208 * scale, Dpi = dpi };
                    p = HoverLogic.Place(a, 260, 90);
                    Require(p != null, "negative monitor edge/corner available at DPI " + dpi); Safe(a, p);
                    Require(p.Width == 260 * scale && p.Height == 90 * scale, "DIP size converted once");
                }
        }
        a = new PetAnchor { Visible = true, Left = -100.25, Top = 100.25, Width = 192.5, Height = 208.5,
            WorkLeft = -400.25, WorkTop = -100.25, WorkWidth = 800.5, WorkHeight = 700.5, Dpi = 120 };
        p = HoverLogic.Place(a, 260.1, 90.1); Require(p != null && p.Width == 326 && p.Height == 113, "fractional pixels rounded outward"); Safe(a, p);
        a.Dpi = 0; p = HoverLogic.Place(a, 260, 90); Require(p != null && p.Width == 260, "unknown DPI uses 96");
        a.Visible = false; Require(HoverLogic.Place(a, 100, 90) == null, "hidden anchor not placed"); a.Visible = true;
        a.WorkLeft = Double.PositiveInfinity; Require(HoverLogic.Place(a, 100, 90) == null, "invalid work area rejected");
    }

    private static void Safe(PetAnchor a, OverlayPlacement p)
    {
        Require(p.X >= a.WorkLeft && p.Y >= a.WorkTop && p.X + p.Width <= a.WorkLeft + a.WorkWidth &&
            p.Y + p.Height <= a.WorkTop + a.WorkHeight, "tooltip fully inside work area");
        double gap = 8 * (a.Dpi == 0 ? 96 : a.Dpi) / 96.0;
        Require(p.X + p.Width <= a.Left - gap || p.X >= a.Left + a.Width + gap ||
            p.Y + p.Height <= a.Top - gap || p.Y >= a.Top + a.Height + gap, "tooltip outside whole pet and gap");
    }

    private static void Dwell()
    {
        var state = new HoverState();
        Require(!state.Update(true, true, false, 1000, 42), "first hover waits");
        Require(!state.Update(true, true, false, 1299, 42), "299 ms still hidden");
        Require(state.Update(true, true, false, 1300, 42), "300 ms visible");
        Require(state.Update(true, true, false, 1400, 42), "animation changes do not reset dwell");
        Require(!state.Update(true, false, false, 1401, 42), "leave hides immediately");
        Require(!state.Update(true, true, false, 1500, 42) && !state.Update(true, true, false, 1799, 42) && state.Update(true, true, false, 1800, 42), "reentry starts new dwell");
        Require(!state.Update(true, true, true, 1801, 42), "mouse press hides immediately");
        Require(!state.Update(true, true, true, 3000, 42), "held drag stays hidden");
        Require(!state.Update(true, true, false, 3100, 42) && state.Update(true, true, false, 3400, 42), "release restarts dwell");
        Require(!state.Update(false, true, false, 3500, 42), "ineligible hides");
        Require(!state.Update(true, true, false, 3600, 42) && state.Update(true, true, false, 3900, 42), "reappearance waits again");
        Require(!state.Update(true, true, false, 4000, 43) && !state.Update(true, true, false, 4299, 43) && state.Update(true, true, false, 4300, 43), "recreated pet window starts new dwell");
        Require(!state.Update(true, true, false, 4000, 43) && state.Update(true, true, false, 4300, 43), "clock reversal restarts safely");
        state.Reset(); Require(!state.Update(true, true, false, 4400, 43), "explicit reset hides");
        Require(!state.Update(true, true, false, 5000, 0), "missing window rejected");
        Require(!state.Update(true, true, false, -1, 43), "invalid monotonic timestamp rejected");
        Require(!state.Update(true, true, false, Int64.MaxValue - 300, 43) && state.Update(true, true, false, Int64.MaxValue, 43), "large monotonic times do not overflow");
    }

    private static void RequireStatus(HoverContent content, string status, string label)
    {
        Require(content.Status == status && content.Rows.Count == 0, label);
    }

    private static void Require(bool condition, string label)
    {
        _checks++;
        if (!condition) throw new Exception("Hover: " + label);
    }
}
