using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodexQuotaPet
{
    public sealed class HoverRow
    {
        public string Label, Value;
    }

    public sealed class HoverContent
    {
        public string Title = "下次重置（北京时间）";
        public string Status = "";
        public List<HoverRow> Rows = new List<HoverRow>();
        public List<HoverRow> ResetCreditRows = new List<HoverRow>();
        public string ResetCreditNotice = "";
    }

    // All coordinates crossing the native window boundary are physical pixels.
    public static class HoverLogic
    {
        public static bool HitTest(ScreenMatch match, double physicalX, double physicalY)
        {
            if (match == null || !match.Visible || match.Frame == null || !match.Frame.Supported ||
                !UsableAnchor(match.Anchor) || !Finite(physicalX) || !Finite(physicalY)) return false;
            ScreenFrame frame = match.Frame;
            if (!Finite(frame.TextLeft) || !Finite(frame.TextTop) || !Positive(frame.TextWidth) ||
                !Positive(frame.TextHeight) || !Finite(frame.Angle)) return false;
            double x = (physicalX - match.Anchor.Left) * ScreenFrames.FrameWidth / match.Anchor.Width;
            double y = (physicalY - match.Anchor.Top) * ScreenFrames.FrameHeight / match.Anchor.Height;
            double centerX = frame.TextLeft + frame.TextWidth / 2;
            double centerY = frame.TextTop + frame.TextHeight / 2;
            double radians = frame.Angle * Math.PI / 180;
            // Undo the exact text rotation used by ScreenCanvas before testing
            // the safe text rectangle; do not use its larger axis-aligned box.
            double dx = x - centerX, dy = y - centerY;
            double localX = centerX + dx * Math.Cos(radians) + dy * Math.Sin(radians);
            double localY = centerY - dx * Math.Sin(radians) + dy * Math.Cos(radians);
            return localX >= frame.TextLeft && localX < frame.TextLeft + frame.TextWidth &&
                localY >= frame.TextTop && localY < frame.TextTop + frame.TextHeight;
        }

        public static OverlayPlacement Place(PetAnchor anchor, double widthDip, double heightDip)
        {
            if (!UsableAnchor(anchor) || !Positive(widthDip) || !Positive(heightDip) ||
                !Finite(anchor.WorkLeft) || !Finite(anchor.WorkTop) ||
                !Positive(anchor.WorkWidth) || !Positive(anchor.WorkHeight)) return null;
            double scale = (anchor.Dpi == 0 ? 96 : anchor.Dpi) / 96.0;
            double width = Math.Ceiling(widthDip * scale), height = Math.Ceiling(heightDip * scale);
            double gap = 8 * scale;
            double left = Math.Ceiling(anchor.WorkLeft), top = Math.Ceiling(anchor.WorkTop);
            double right = Math.Floor(anchor.WorkLeft + anchor.WorkWidth);
            double bottom = Math.Floor(anchor.WorkTop + anchor.WorkHeight);
            if (!Finite(width) || !Finite(height) || width > right - left || height > bottom - top ||
                left < Int32.MinValue || top < Int32.MinValue || right > Int32.MaxValue || bottom > Int32.MaxValue ||
                width > Int32.MaxValue || height > Int32.MaxValue) return null;
            double centerX = Math.Round(anchor.Left + (anchor.Width - width) / 2);
            double centerY = Math.Round(anchor.Top + (anchor.Height - height) / 2);
            double x = Math.Max(left, Math.Min(centerX, right - width));
            double y = Math.Max(top, Math.Min(centerY, bottom - height));
            // Clamp only along the side, never toward or across the pet image.
            var candidates = new[] {
                new[] { x, Math.Floor(anchor.Top - gap - height) },
                new[] { Math.Ceiling(anchor.Left + anchor.Width + gap), y },
                new[] { Math.Floor(anchor.Left - gap - width), y },
                new[] { x, Math.Ceiling(anchor.Top + anchor.Height + gap) }
            };
            foreach (double[] candidate in candidates)
            {
                double cx = candidate[0], cy = candidate[1];
                if (cx < left || cy < top || cx + width > right || cy + height > bottom) continue;
                if (cx < anchor.Left + anchor.Width && cx + width > anchor.Left &&
                    cy < anchor.Top + anchor.Height && cy + height > anchor.Top) continue;
                return new OverlayPlacement { X = (int)cx, Y = (int)cy, Width = (int)width, Height = (int)height };
            }
            return null;
        }

        public static HoverContent Content(QuotaSnapshot snapshot, DateTime utcNow)
        {
            var content = new HoverContent();
            try { content.Status = OverlayLogic.Freshness(snapshot, utcNow); }
            catch (ArgumentOutOfRangeException) { content.Status = "暂不可用"; }
            DateTime beijingNow;
            bool hasBeijingNow = TryBeijing(utcNow, out beijingNow);
            if (!hasBeijingNow) content.Status = "暂不可用";
            if (content.Status.Length == 0 && snapshot != null && snapshot.Windows != null)
            {
                foreach (QuotaWindow window in snapshot.Windows)
                {
                    if (window == null) continue;
                    string value = "暂不可用";
                    DateTime localReset;
                    if (window.ResetsAtUtc.HasValue && TryBeijing(window.ResetsAtUtc.Value, out localReset))
                    {
                        if (window.ResetsAtUtc.Value <= utcNow) value = "等待更新";
                        else value = FormatBeijing(localReset, beijingNow);
                    }
                    content.Rows.Add(new HoverRow { Label = String.IsNullOrWhiteSpace(window.Label) ? "额度" : window.Label, Value = value });
                }
            }
            if (content.Status.Length == 0 && content.Rows.Count == 0) content.Status = "暂无额度信息";
            ResetCreditContent(content, snapshot, utcNow, beijingNow, hasBeijingNow);
            return content;
        }

        private static void ResetCreditContent(HoverContent content, QuotaSnapshot snapshot, DateTime utcNow, DateTime beijingNow, bool hasBeijingNow)
        {
            ResetCreditsSnapshot credits = snapshot == null ? null : snapshot.ResetCredits;
            string state = "";
            if (snapshot != null && snapshot.State == "login_required") state = "请登录 Codex";
            else if (!hasBeijingNow || credits == null || !credits.AvailableCount.HasValue ||
                credits.AvailableCount.Value < 0 || !credits.LastSuccessUtc.HasValue) state = "暂不可用";
            else
            {
                double age = (utcNow - credits.LastSuccessUtc.Value).TotalSeconds;
                if (age >= 300) state = "离线";
                else if (age >= 90) state = "待更新";
            }
            content.ResetCreditRows.Add(new HoverRow { Label = "可用重置", Value = state.Length == 0 ?
                credits.AvailableCount.Value.ToString(CultureInfo.InvariantCulture) + " 次" : state });
            // Credit freshness is independent of quota notifications. Never
            // render yesterday's expirations merely because a quota event arrived.
            if (state.Length != 0 || credits.AvailableCount.Value == 0) return;
            var entries = new List<ResetCredit>();
            if (credits.DetailsAvailable && credits.Credits != null)
                foreach (ResetCredit credit in credits.Credits)
                    if (credit != null) entries.Add(credit);
            entries.Sort(delegate(ResetCredit a, ResetCredit b)
            {
                int aKind = CreditSortKind(a), bKind = CreditSortKind(b);
                if (aKind != bKind) return aKind.CompareTo(bKind);
                return aKind == 0 ? a.ExpiresAtUtc.Value.CompareTo(b.ExpiresAtUtc.Value) : 0;
            });
            for (int i = 0; i < entries.Count; i++)
            {
                ResetCredit credit = entries[i];
                string value = "到期时间暂不可用";
                DateTime expiration;
                if (credit.NeverExpires) value = "长期有效";
                else if (credit.ExpiresAtUtc.HasValue && TryBeijing(credit.ExpiresAtUtc.Value, out expiration))
                    value = credit.ExpiresAtUtc.Value <= utcNow ? "已到期，等待更新" : FormatBeijing(expiration, beijingNow);
                content.ResetCreditRows.Add(new HoverRow {
                    Label = "第" + (i + 1).ToString(CultureInfo.InvariantCulture) + "次到期", Value = value });
            }
            if (entries.Count == 0) content.ResetCreditNotice = "到期时间暂不可用";
            else if (entries.Count < credits.AvailableCount.Value)
                content.ResetCreditNotice = "仅返回 " + entries.Count.ToString(CultureInfo.InvariantCulture) + " / " +
                    credits.AvailableCount.Value.ToString(CultureInfo.InvariantCulture) + " 项到期明细";
        }

        private static int CreditSortKind(ResetCredit credit)
        {
            DateTime ignored;
            if (credit.NeverExpires) return 2;
            return credit.ExpiresAtUtc.HasValue && TryBeijing(credit.ExpiresAtUtc.Value, out ignored) ? 0 : 1;
        }

        private static string FormatBeijing(DateTime beijingDate, DateTime beijingNow)
        {
            return beijingDate.ToString(beijingDate.Year == beijingNow.Year ? "MM月dd日 HH:mm:ss" : "yyyy年MM月dd日 HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static bool TryBeijing(DateTime utc, out DateTime result)
        {
            try { result = OverlayLogic.Beijing(utc); return true; }
            catch (ArgumentOutOfRangeException) { result = default(DateTime); return false; }
        }

        private static bool UsableAnchor(PetAnchor anchor)
        {
            return anchor != null && anchor.Visible && Finite(anchor.Left) && Finite(anchor.Top) &&
                Positive(anchor.Width) && Positive(anchor.Height) &&
                Finite(anchor.Left + anchor.Width) && Finite(anchor.Top + anchor.Height);
        }

        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static bool Positive(double value) { return Finite(value) && value > 0; }
    }

    // Remember where the user began hovering, rather than treating the pet's
    // own hover animation as mouse movement. A bounded recognition grace bridges
    // transitions, while hiding the pet/session still dismisses immediately.
    public sealed class HoverSession
    {
        private readonly HoverState _dwell = new HoverState();
        private ScreenMatch _hotspot;
        private long _lastRecognized;

        public bool Update(bool available, ScreenMatch match, PetAnchor anchor, double x, double y, bool buttonDown, long now)
        {
            if (!available || anchor == null || !anchor.Visible || buttonDown || now < 0 || anchor.WindowHandle == 0)
            { Reset(); return false; }
            if (_hotspot != null && (anchor.WindowHandle != _hotspot.Anchor.WindowHandle ||
                anchor.Width != _hotspot.Anchor.Width || anchor.Height != _hotspot.Anchor.Height || anchor.Dpi != _hotspot.Anchor.Dpi))
                Reset();
            bool current = match != null && match.Anchor != null && match.Anchor.Visible &&
                match.Anchor.WindowHandle == anchor.WindowHandle && match.Anchor.Left == anchor.Left && match.Anchor.Top == anchor.Top &&
                match.Anchor.Width == anchor.Width && match.Anchor.Height == anchor.Height && match.Anchor.Dpi == anchor.Dpi;
            bool recognized = current && match.Frame != null;
            bool directHit = current && HoverLogic.HitTest(match, x, y);
            if (_hotspot == null)
            {
                if (!directHit) return false;
                // Copy geometry so mutable callers cannot move the remembered
                // region. No pixels or cursor history are retained.
                var frame = match.Frame;
                _hotspot = new ScreenMatch { Visible = true,
                    Frame = new ScreenFrame { Supported = true, TextLeft = frame.TextLeft, TextTop = frame.TextTop,
                        TextWidth = frame.TextWidth, TextHeight = frame.TextHeight, Angle = frame.Angle },
                    Anchor = new PetAnchor { Visible = true, Left = anchor.Left, Top = anchor.Top, Width = anchor.Width,
                        Height = anchor.Height, Dpi = anchor.Dpi, WindowHandle = anchor.WindowHandle } };
                _lastRecognized = now;
            }
            if (recognized) _lastRecognized = now;
            if (now < _lastRecognized || now - _lastRecognized > 750 ||
                (!directHit && !HoverLogic.HitTest(_hotspot, x, y)) ||
                x < anchor.Left || x >= anchor.Left + anchor.Width || y < anchor.Top || y >= anchor.Top + anchor.Height)
            { Reset(); return false; }
            return _dwell.Update(true, true, false, now, anchor.WindowHandle);
        }

        public void Reset() { _hotspot = null; _lastRecognized = 0; _dwell.Reset(); }
    }

    public sealed class HoverState
    {
        private bool _tracking;
        private long _started, _last, _window;

        public bool Update(bool eligible, bool hit, bool buttonDown, long monotonicMilliseconds, long petWindowHandle)
        {
            if (!eligible || !hit || buttonDown || monotonicMilliseconds < 0 || petWindowHandle == 0)
            {
                Reset(); return false;
            }
            if (!_tracking || _window != petWindowHandle || monotonicMilliseconds < _last)
            {
                _tracking = true; _started = monotonicMilliseconds; _window = petWindowHandle;
            }
            _last = monotonicMilliseconds;
            return monotonicMilliseconds - _started >= 300;
        }

        public void Reset() { _tracking = false; _started = _last = _window = 0; }
    }
}
