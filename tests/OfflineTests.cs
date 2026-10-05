using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexQuotaPet;

internal static class OfflineTests
{
    private static int _checks;
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            QuotaService.RunSelfTests(); PetTracker.RunSelfTests(); OverlayLogic.RunSelfTests(); ScreenFrames.RunSelfTests();
            Console.WriteLine("PASS quota parsing, account changes, freshness, placement, template validation");
            SyntheticMatcher();
            FontRendering();
            MockProtocol(args[0], args[1]);
            Console.WriteLine("PASS additional assertions: " + _checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
    }

    private static ScreenFrame SyntheticFrame()
    {
        var samples = new List<ScreenSample>();
        for (int y = 10; y < 200; y += 20)
            for (int x = 10; x < 190; x += 20)
            {
                if (x >= 50 && x <= 140 && y >= 60 && y <= 130) continue;
                samples.Add(new ScreenSample { X = x, Y = y, R = (x * 13 + y * 7) % 220 + 20, G = (x * 3 + y * 17) % 220 + 20, B = (x * 11 + y * 3) % 220 + 20 });
            }
        var runs = new List<ScreenRun>();
        for (int y = 70; y < 120; y++) runs.Add(new ScreenRun { X = 60, Y = y, Width = 72 });
        return new ScreenFrame { Index = 0, Supported = true, Samples = samples.ToArray(), Mask = runs.ToArray(), TextLeft = 64, TextTop = 77, TextWidth = 64, TextHeight = 35, BackgroundR = 15, BackgroundG = 16, BackgroundB = 19 };
    }

    private static void SyntheticMatcher()
    {
        ScreenFrame[] saved = ScreenFrames.All;
        var frame = SyntheticFrame();
        ScreenFrames.All = new[] { frame };
        try
        {
            foreach (int width in new[] { 96, 160, 192, 288, 384 })
            {
                int height = (int)Math.Round(width * 208.0 / 192);
                byte[] pixels = new byte[width * height * 4];
                foreach (ScreenSample s in frame.Samples)
                {
                    int x = (int)Math.Floor((s.X + .5) * width / 192), y = (int)Math.Floor((s.Y + .5) * height / 208);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int offset = ((y + dy) * width + x + dx) * 4;
                        pixels[offset] = (byte)s.B; pixels[offset + 1] = (byte)s.G; pixels[offset + 2] = (byte)s.R; pixels[offset + 3] = 255;
                    }
                }
                var anchor = new PetAnchor { Visible = true, Width = width, Height = height, Dpi = 96 };
                Require(ScreenTracker.MatchPixels(pixels, width, height, width * 4, anchor, null).Visible, "synthetic match at " + width);
                Require(ScreenTracker.MatchPixels(pixels, width, height, width * 4, anchor, frame).Visible, "previous overlay does not prevent match");
                Require(!ScreenTracker.MatchPixels(new byte[pixels.Length], width, height, width * 4, anchor, null).Visible, "flat background rejected");
                Require(!ScreenTracker.MatchPixels(pixels, width, height, 1, anchor, null).Visible, "invalid stride rejected");
                frame.Supported = false;
                Require(!ScreenTracker.MatchPixels(pixels, width, height, width * 4, anchor, null).Visible, "unsupported pose hidden");
                frame.Supported = true;
            }
            ScreenFrames.All = new ScreenFrame[0];
            Require(!ScreenTracker.MatchPixels(new byte[192 * 208 * 4], 192, 208, 768, new PetAnchor { Visible = true, Width = 192, Height = 208 }, null).Visible, "no template hidden");
        }
        finally { ScreenFrames.All = saved; }
        Console.WriteLine("PASS synthetic matcher at 5 scales; no installed artwork required");
    }

    private static void FontRendering()
    {
        var frame = SyntheticFrame();
        foreach (string text in new[] { "0%", "63%", "100%", "--" })
        {
            var canvas = new ScreenCanvas();
            canvas.Set(frame, text, "#74D5C1");
            canvas.Measure(new Size(192, 208)); canvas.Arrange(new Rect(0, 0, 192, 208)); canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(192, 208, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            byte[] bytes = new byte[192 * 208 * 4]; bitmap.CopyPixels(bytes, 768, 0);
            int ink = 0, minX = 192, maxX = 0, minY = 208, maxY = 0;
            bool maskContained = true;
            for (int y = 0; y < 208; y++) for (int x = 0; x < 192; x++)
            {
                int i = (y * 192 + x) * 4;
                if (bytes[i + 3] > 0 && !(x >= 60 && x < 132 && y >= 70 && y < 120)) maskContained = false;
                if (bytes[i + 1] > 120 && bytes[i + 2] > 60) { ink++; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            }
            Require(maskContained, text + " mask does not escape");
            Require(ink > 80, text + " has visible glyphs");
            Require(minX >= 63 && maxX <= 129 && minY >= 76 && maxY <= 113, text + " fits safe text rectangle");
            Require(maxX - minX >= 52 || maxY - minY >= 28, text + " uses large readable glyphs");
        }
        var view = new QuotaWindowView();
        Require(!view.ShowActivated && !view.Focusable && !view.ShowInTaskbar, "passive window properties");
        view.Close();
        var reloadCanvas = new ScreenCanvas();
        reloadCanvas.Set(frame, "63%", "#74D5C1");
        reloadCanvas.Measure(new Size(192, 208)); reloadCanvas.Arrange(new Rect(0, 0, 192, 208)); reloadCanvas.UpdateLayout();
        var replacement = SyntheticFrame();
        replacement.Mask = new[] { new ScreenRun { X = 10, Y = 10, Width = 10 } };
        reloadCanvas.Set(replacement, "63%", "#74D5C1"); reloadCanvas.UpdateLayout();
        var afterReload = new RenderTargetBitmap(192, 208, 96, 96, PixelFormats.Pbgra32);
        afterReload.Render(reloadCanvas);
        byte[] reloaded = new byte[192 * 208 * 4]; afterReload.CopyPixels(reloaded, 768, 0);
        Require(reloaded[(10 * 192 + 10) * 4 + 3] > 0 && reloaded[(80 * 192 + 80) * 4 + 3] == 0, "template reload replaces cached mask despite identical frame index");
        Console.WriteLine("PASS 0%, 63%, 100% and unavailable glyph bounds; passive window flags");
    }

    private static void MockProtocol(string executable, string controlPath)
    {
        string oldExe = Environment.GetEnvironmentVariable("CODEX_QUOTA_PET_CODEX_PATH");
        string oldControl = Environment.GetEnvironmentVariable("CQP_TEST_CONTROL");
        Encoding oldInputEncoding = Console.InputEncoding;
        // A redirected UTF-8 console on hosted Windows runners can make the
        // default Process.StandardInput writer emit a BOM. Exercise that exact
        // environment: the production JSONL transport must stay BOM-free.
        Console.InputEncoding = new UTF8Encoding(true);
        Environment.SetEnvironmentVariable("CODEX_QUOTA_PET_CODEX_PATH", executable);
        Environment.SetEnvironmentVariable("CQP_TEST_CONTROL", controlPath);
        Process unrelated = null;
        IntPtr parentInput = GetStdHandle(-10);
        try
        {
            File.WriteAllText(controlPath, "ready");
            unrelated = Process.Start(new ProcessStartInfo(executable, "--sentinel") { UseShellExecute = false, CreateNoWindow = true });
            using (var service = new QuotaService())
            {
                service.SetPetVisible(false); service.Start(); Thread.Sleep(300);
                Require(Child(service) == null, "hidden startup does not spawn app-server");
                service.Refresh();
                WaitFor(delegate { return service.Current.State == "ready"; }, 8000, "explicit hidden refresh with UTF-8 BOM console", service);
                Require(GetStdHandle(-10) == parentInput, "initial connection restores parent input handle");
                Require(service.Current.Windows.Count == 2, "mock dual quota windows");
                bool weekly = false, shortWindow = false;
                foreach (var window in service.Current.Windows) { if (window.WindowMinutes == 10080 && window.RemainingPercent == 63) weekly = true; if (window.WindowMinutes == 300 && window.RemainingPercent == 82) shortWindow = true; }
                Require(weekly && shortWindow, "mock exact remaining values");
                DateTime first = service.Current.LastSuccessUtc.Value;
                service.SetPetVisible(true);
                WaitFor(delegate { return service.Current.LastSuccessUtc > first; }, 8000, "visible immediate refresh", service);
                service.SetPetVisible(false);

                File.WriteAllText(controlPath, "http-error"); service.Refresh();
                WaitFor(delegate { return service.Current.State == "error"; }, 8000, "HTTP failure surfaces", service);
                Require(service.Current.LastSuccessUtc.HasValue, "failure retains old success timestamp");
                File.WriteAllText(controlPath, "ready"); service.Refresh();
                WaitFor(delegate { return service.Current.State == "ready"; }, 8000, "HTTP recovery", service);

                int oldPid = Child(service).Id;
                File.WriteAllText(controlPath, "timeout"); service.Refresh();
                Stopwatch timeout = Stopwatch.StartNew();
                WaitFor(delegate { return service.Current.State == "error"; }, 19000, "request timeout", service);
                Require(timeout.Elapsed.TotalSeconds >= 14 && timeout.Elapsed.TotalSeconds < 19, "15-second timeout boundary");
                File.WriteAllText(controlPath, "ready"); service.Refresh();
                WaitFor(delegate { return service.Current.State == "ready"; }, 8000, "timeout reconnect", service);
                Require(Child(service).Id != oldPid, "timeout recreates owned child");

                File.WriteAllText(controlPath, "login"); service.Refresh();
                WaitFor(delegate { return service.Current.State == "login_required"; }, 8000, "login expiry", service);
                Require(!service.Current.LastSuccessUtc.HasValue, "expired account invalidates cached quota");
                File.WriteAllText(controlPath, "ready"); service.Refresh();
                WaitFor(delegate { return service.Current.State == "ready"; }, 8000, "login recovery", service);

                Process owned = Child(service); owned.Kill();
                WaitFor(delegate { return service.Current.State == "error"; }, 5000, "owned child exit", service);
                service.Refresh();
                WaitFor(delegate { return service.Current.State == "ready"; }, 8000, "owned child restart", service);
                int lastPid = Child(service).Id;
                service.Dispose();
                Require(GetStdHandle(-10) == parentInput, "disposal preserves parent input handle");
                Require(!IsAlive(lastPid), "dispose closes owned child");
                Require(!unrelated.HasExited, "unrelated app-server remains alive");
            }
            // Existing but invalid executable passes discovery and fails inside
            // Process.Start, after the temporary standard-handle swap.
            string invalidExecutable = Path.Combine(Path.GetDirectoryName(controlPath), "InvalidAppServer.exe");
            File.WriteAllText(invalidExecutable, "Synthetic invalid executable for startup-failure testing.");
            Environment.SetEnvironmentVariable("CODEX_QUOTA_PET_CODEX_PATH", invalidExecutable);
            using (var failedStart = new QuotaService())
            {
                failedStart.SetPetVisible(false); failedStart.Start(); failedStart.Refresh();
                WaitFor(delegate { return failedStart.Current.State == "error"; }, 8000, "invalid executable startup fails", failedStart);
                Require(GetStdHandle(-10) == parentInput, "failed startup restores parent input handle");
                Require(typeof(QuotaService).GetField("_input", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(failedStart) == null, "failed startup releases owned input pipe");
            }
        }
        finally
        {
            if (unrelated != null) { if (!unrelated.HasExited) unrelated.Kill(); unrelated.Dispose(); }
            Environment.SetEnvironmentVariable("CODEX_QUOTA_PET_CODEX_PATH", oldExe);
            Environment.SetEnvironmentVariable("CQP_TEST_CONTROL", oldControl);
            Console.InputEncoding = oldInputEncoding;
        }
        Console.WriteLine("PASS mock transport timeout, HTTP failure, reconnect, login expiry, owned-process cleanup");
    }

    private static Process Child(QuotaService service) { return (Process)typeof(QuotaService).GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(service); }
    private static bool IsAlive(int pid) { try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; } catch (ArgumentException) { return false; } }
    private static void WaitFor(Func<bool> predicate, int milliseconds, string name, QuotaService service = null)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate() && timer.ElapsedMilliseconds < milliseconds) Thread.Sleep(25);
        bool passed = predicate();
        if (!passed && service != null)
        {
            // Only safe service state and process liveness: no protocol payloads,
            // environment values, local paths, or account metadata in CI logs.
            QuotaSnapshot state = service.Current;
            bool childAlive = false;
            try { Process child = Child(service); childAlive = child != null && !child.HasExited; } catch (InvalidOperationException) { }
            name += " [state=" + state.State + ", error=" + (state.Error ?? "none") + ", childAlive=" + childAlive + ", elapsedMs=" + timer.ElapsedMilliseconds + "]";
        }
        Require(passed, name);
    }
    private static void Require(bool value, string description) { _checks++; if (!value) throw new InvalidOperationException(description); }
}
