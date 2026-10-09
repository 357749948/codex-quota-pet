using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("Codex 宠物额度")]
[assembly: AssemblyVersion(CodexQuotaPet.BuildVersion.Assembly)]
[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace CodexQuotaPet
{
    public static class Program
    {
        public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaPetData");
        public static string ExitEventName
        {
            get
            {
                string path = Assembly.GetExecutingAssembly().Location.ToLowerInvariant();
                using (var hash = SHA256.Create())
                    return "Local\\CodexQuotaPet.Exit." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-", "").Substring(0, 24);
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Native.EnableDpi();
            if (Has(args, "--exit"))
            {
                try { using (var e = EventWaitHandle.OpenExisting(ExitEventName)) e.Set(); return 0; }
                catch (WaitHandleCannotBeOpenedException) { return 0; }
            }
            if (Has(args, "--self-test"))
            {
                var result = new Dictionary<string, object>();
                try
                {
                    QuotaService.RunSelfTests(); PetTracker.RunSelfTests(); OverlayLogic.RunSelfTests(); ScreenFrames.RunSelfTests();
                    result["passed"] = true; result["suites"] = new[] { "quota parsing", "pet identification", "DPI placement and stale display", "template validation" };
                }
                catch (Exception e) { result["passed"] = false; result["error"] = e.ToString(); }
                string output = Value(args, "--report");
                if (output != null) File.WriteAllText(output, new JavaScriptSerializer().Serialize(result), Encoding.UTF8);
                return (bool)result["passed"] ? 0 : 1;
            }
            if (Has(args, "--probe"))
            {
                string output = Value(args, "--report");
                try
                {
                    var anchor = PetTracker.ProbeOnce();
                    if (output != null) File.WriteAllText(output, new JavaScriptSerializer().Serialize(anchor), Encoding.UTF8);
                    return anchor != null && anchor.Visible ? 0 : 2;
                }
                catch (Exception e) { if (output != null) File.WriteAllText(output, e.ToString(), Encoding.UTF8); return 1; }
            }

            bool created;
            using (var mutex = new Mutex(true, "Local\\CodexQuotaPet.Singleton", out created))
            {
                if (!created) return 0;
                using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName))
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var host = new OverlayHost(app, exit);
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        host.WriteFailure(e.Exception.GetType().Name);
                        e.Handled = true; app.Shutdown(1);
                    };
                    try { host.Start(); return app.Run(); }
                    catch (Exception e) { host.WriteFailure(e.GetType().Name + ": " + e.Message); return 1; }
                    finally { host.Dispose(); mutex.ReleaseMutex(); }
                }
            }
        }

        private static bool Has(string[] args, string name) { return Array.IndexOf(args, name) >= 0; }
        private static string Value(string[] args, string name)
        {
            int n = Array.IndexOf(args, name); return n >= 0 && n + 1 < args.Length ? args[n + 1] : null;
        }
    }

    public sealed class OverlayHost : IDisposable
    {
        private readonly Application _app;
        private readonly EventWaitHandle _exit;
        private readonly QuotaService _quota = new QuotaService();
        private readonly PetTracker _tracker = new PetTracker();
        private readonly ScreenTracker _screen = new ScreenTracker();
        private readonly QuotaWindowView _window = new QuotaWindowView();
        private readonly ResetTooltipView _tooltip = new ResetTooltipView();
        private readonly HoverSession _hover = new HoverSession();
        private readonly Stopwatch _hoverClock = Stopwatch.StartNew();
        private DispatcherTimer _hoverTimer;
        private OverlayPlacement _tooltipPlacement;
        private long _lastPointerSample = -100;
        private int _pointerX, _pointerY;
        private bool _pointerValid, _pointerButton;
        private Forms.NotifyIcon _tray;
        private Forms.ToolStripMenuItem _details, _status;
        private DispatcherTimer _timer;
        private PetAnchor _anchor;
        private ScreenMatch _screenMatch;
        private bool _sessionUnlocked;
        private QuotaSnapshot _snapshot;
        private OverlayPlacement _placement;
        private bool _disposed;
        private DateTime _lastStateWrite = DateTime.MinValue;
        private RegisteredWaitHandle _exitWait;
        private readonly DateTime _started = DateTime.UtcNow;

        public OverlayHost(Application app, EventWaitHandle exit) { _app = app; _exit = exit; }

        public void Start()
        {
            Directory.CreateDirectory(Program.DataDirectory);
            _snapshot = _quota.Current;
            _quota.Changed += delegate(QuotaSnapshot q) { Dispatch(delegate { _snapshot = q; Update(); }); };
            _screen.Changed += delegate(ScreenMatch match) { Dispatch(delegate { _screenMatch = match; Update(); }); };
            _tracker.Changed += delegate(PetAnchor a)
            {
                Dispatch(delegate
                {
                    bool appeared = a != null && a.Visible && (_anchor == null || !_anchor.Visible);
                    _anchor = a;
                    _screen.SetAnchor(a);
                    _quota.SetPetVisible(_sessionUnlocked && a != null && a.Visible);
                    Update();
                    if (appeared) WriteState(true);
                });
            };
            BuildTray();
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exit, delegate { Dispatch(delegate { _app.Shutdown(); }); }, null, -1, true);
            SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.SessionSwitch += SessionChanged;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { UpdateSession(); Update(); };
            _timer.Start();
            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _hoverTimer.Tick += delegate { UpdateHover(); };
            _hoverTimer.Start();
            _quota.SetPetVisible(false);
            _quota.Start();
            _screen.Start();
            UpdateSession();
            _tracker.Start();
            WriteState(true);
        }

        private void Dispatch(Action a)
        {
            if (!_disposed && !_app.Dispatcher.HasShutdownStarted) _app.Dispatcher.BeginInvoke(a, DispatcherPriority.Background);
        }

        private void BuildTray()
        {
            var menu = new Forms.ContextMenuStrip();
            _status = new Forms.ToolStripMenuItem("正在寻找宠物") { Enabled = false };
            _details = new Forms.ToolStripMenuItem("查看额度与重置时间");
            _details.Click += delegate { ShowDetails(); };
            var refresh = new Forms.ToolStripMenuItem("立即刷新");
            refresh.Click += delegate { _quota.Refresh(); };
            var quit = new Forms.ToolStripMenuItem("退出额度显示层");
            quit.Click += delegate { _app.Shutdown(); };
            menu.Items.Add(_status); menu.Items.Add(_details); menu.Items.Add(refresh);
            menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add(quit);
            _tray = new Forms.NotifyIcon { Text = "Codex 宠物额度", Icon = CreateIcon(), ContextMenuStrip = menu, Visible = true };
            _tray.DoubleClick += delegate { ShowDetails(); };
        }

        private static System.Drawing.Icon CreateIcon()
        {
            using (var bitmap = new System.Drawing.Bitmap(32, 32))
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(28, 32, 40))) g.FillEllipse(brush, 1, 1, 30, 30);
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(116, 213, 193), 3)) g.DrawArc(pen, 7, 7, 18, 18, -90, 265);
                IntPtr h = bitmap.GetHicon();
                try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        private void Update()
        {
            if (_disposed) return;
            bool visible = _sessionUnlocked && ScreenFrames.IsReady && _anchor != null && _anchor.Visible && _screenMatch != null && _screenMatch.Visible;
            if (visible)
            {
                PetAnchor a = _screenMatch.Anchor;
                _window.Render(_snapshot, _screenMatch);
                _placement = OverlayLogic.PlaceOnFace(a);
                if (_window.Handle == IntPtr.Zero) new WindowInteropHelper(_window).EnsureHandle();
                Native.MoveNoActivate(_window.Handle, _placement);
                if (!_window.IsVisible) _window.Show();
                Native.MoveNoActivate(_window.Handle, _placement);
            }
            else if (_window.IsVisible) _window.Hide();
            UpdateHover();
            string quotaText = "Codex 宠物额度";
            if (_snapshot != null && _snapshot.Windows != null && _snapshot.Windows.Count > 0)
                quotaText = _snapshot.Windows[0].Label + "剩余 " + OverlayLogic.Percentage(_snapshot.Windows[0].RemainingPercent);
            string freshness = OverlayLogic.Freshness(_snapshot, DateTime.UtcNow);
            if (freshness.Length > 0) quotaText += " · " + freshness;
            if (_tray != null) _tray.Text = quotaText.Length > 63 ? quotaText.Substring(0, 63) : quotaText;
            _status.Text = visible ? quotaText : !_sessionUnlocked ? "屏幕已锁定 · 显示暂停" : _anchor != null && _anchor.Visible ? (!ScreenFrames.IsReady ? ScreenFrames.TemplateStatus : "等待正面屏幕 · " + quotaText) : "宠物未显示 · 额度层已隐藏";
            WriteState(false);
        }

        private void UpdateHover()
        {
            if (_disposed) return;
            PetAnchor a = _anchor;
            // A remembered hotspot survives the pet's own jumping animation,
            // but only while the same visible pet/session/template remains.
            bool eligible = _sessionUnlocked && ScreenFrames.IsReady && a != null && a.Visible;
            long now = _hoverClock.ElapsedMilliseconds;
            if (eligible && now - _lastPointerSample >= 100)
            {
                _pointerValid = Native.TryPointer(out _pointerX, out _pointerY, out _pointerButton);
                _lastPointerSample = now;
            }
            else if (!eligible) { _pointerValid = false; _lastPointerSample = -100; }
            eligible = eligible && _pointerValid;
            bool show = _hover.Update(eligible, _screenMatch, a, _pointerX, _pointerY, _pointerButton, now);
            if (show)
            {
                _tooltip.Render(HoverLogic.Content(_snapshot, DateTime.UtcNow));
                _tooltipPlacement = HoverLogic.Place(a, _tooltip.MeasuredWidth, _tooltip.MeasuredHeight);
                show = _tooltipPlacement != null;
            }
            bool wasVisible = _tooltip.IsVisible;
            if (show)
            {
                if (_tooltip.Handle == IntPtr.Zero) new WindowInteropHelper(_tooltip).EnsureHandle();
                Native.MoveNoActivate(_tooltip.Handle, _tooltipPlacement);
                if (!_tooltip.IsVisible) _tooltip.Show();
                Native.MoveNoActivate(_tooltip.Handle, _tooltipPlacement);
            }
            else
            {
                _tooltipPlacement = null;
                if (_tooltip.IsVisible) _tooltip.Hide();
            }
            if (wasVisible != _tooltip.IsVisible) WriteState(true);
        }

        private void ShowDetails()
        {
            var text = new StringBuilder();
            if (_snapshot != null && _snapshot.Windows != null)
                foreach (var window in _snapshot.Windows)
                {
                    text.AppendLine(window.Label + "剩余：" + OverlayLogic.Percentage(window.RemainingPercent));
                    text.AppendLine("重置时间：" + (window.ResetsAtUtc.HasValue ? OverlayLogic.Beijing(window.ResetsAtUtc.Value).ToString("MM月dd日 HH:mm:ss") + "（北京时间）" : "暂不可用"));
                    text.AppendLine();
                }
            text.AppendLine(_snapshot != null && _snapshot.LastSuccessUtc.HasValue ? "上次更新：" + OverlayLogic.Beijing(_snapshot.LastSuccessUtc.Value).ToString("HH:mm:ss") + "（北京时间）" : "尚未成功读取额度。");
            string freshness = OverlayLogic.Freshness(_snapshot, DateTime.UtcNow);
            if (freshness.Length > 0) text.AppendLine(freshness);
            if (_snapshot != null && !string.IsNullOrEmpty(_snapshot.Error)) text.AppendLine(_snapshot.Error);
            text.AppendLine("模板状态：" + ScreenFrames.TemplateStatus);
            if (!ScreenFrames.IsReady) text.AppendLine("请在源码目录运行 prepare-templates.ps1；生成后程序会自动重试。");
            text.AppendLine("\n显示时每 " + QuotaService.RefreshIntervalSeconds + " 秒刷新；服务器统计可能存在延迟。");
            MessageBox.Show(text.ToString(), "Codex 额度", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) _quota.Refresh();
        }
        private void SessionChanged(object sender, SessionSwitchEventArgs e)
        {
            Dispatch(delegate { UpdateSession(); Update(); });
        }

        private void UpdateSession()
        {
            bool unlocked = Native.SessionUnlocked();
            if (_sessionUnlocked == unlocked) return;
            _sessionUnlocked = unlocked;
            _screen.SetEnabled(unlocked);
            _quota.SetPetVisible(unlocked && _anchor != null && _anchor.Visible);
            if (unlocked) _quota.Refresh();
            else _screenMatch = null;
        }

        private void WriteState(bool force)
        {
            if (!force && (DateTime.UtcNow - _lastStateWrite).TotalSeconds < 2) return;
            _lastStateWrite = DateTime.UtcNow;
            try
            {
                var state = new Dictionary<string, object>
                {
                    { "app", "CodexQuotaPet" }, { "version", BuildVersion.Value }, { "running", !_disposed },
                    { "pid", Process.GetCurrentProcess().Id }, { "sessionId", Process.GetCurrentProcess().SessionId },
                    { "startedAtUtc", _started.ToString("o") }, { "updatedAtUtc", DateTime.UtcNow.ToString("o") },
                    { "overlayVisible", _window.IsVisible }, { "overlayHwnd", _window.Handle.ToInt64() },
                    { "extendedStyle", Native.GetExStyle(_window.Handle) }, { "overlayBounds", _placement },
                    { "tooltipVisible", _tooltip.IsVisible }, { "tooltipHwnd", _tooltip.Handle.ToInt64() },
                    { "tooltipExtendedStyle", Native.GetExStyle(_tooltip.Handle) }, { "tooltipBounds", _tooltipPlacement },
                    { "pet", _anchor }, { "quota", _snapshot },
                    { "displayMode", "robot-screen" }, { "sessionUnlocked", _sessionUnlocked },
                    { "templateStatus", ScreenFrames.TemplateStatus },
                    { "screenFrame", _screenMatch == null || _screenMatch.Frame == null ? (object)null : _screenMatch.Frame.Index },
                    { "screenScore", _screenMatch == null ? (object)null : _screenMatch.Score },
                    { "freshness", OverlayLogic.Freshness(_snapshot, DateTime.UtcNow) }
                };
                string path = Path.Combine(Program.DataDirectory, "state.json");
                string temp = path + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(state), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void WriteFailure(string reason)
        {
            try { Directory.CreateDirectory(Program.DataDirectory); File.WriteAllText(Path.Combine(Program.DataDirectory, "error.txt"), DateTime.UtcNow.ToString("o") + " " + reason, Encoding.UTF8); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_timer != null) _timer.Stop();
            if (_hoverTimer != null) _hoverTimer.Stop();
            _hover.Reset(); _tooltip.Hide();
            SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.SessionSwitch -= SessionChanged;
            if (_exitWait != null) _exitWait.Unregister(null);
            _screen.Dispose(); _tracker.Dispose(); _quota.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Icon.Dispose(); _tray.Dispose(); }
            _window.Hide(); WriteState(true); _window.Close();
            _tooltip.Close();
        }
    }

    internal static class Native
    {
        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)] private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);
        [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr buffer);
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr n);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out CursorPoint point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

        public static bool TryPointer(out int x, out int y, out bool buttonDown)
        {
            CursorPoint point;
            bool found = GetPhysicalCursorPos(out point);
            x = point.X; y = point.Y;
            buttonDown = (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0 ||
                (GetAsyncKeyState(4) & 0x8000) != 0 || (GetAsyncKeyState(5) & 0x8000) != 0 || (GetAsyncKeyState(6) & 0x8000) != 0;
            return found;
        }

        public static void EnableDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            try { SetProcessDpiAwareness(2); } catch (DllNotFoundException) { }
        }
        public static bool SessionUnlocked()
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, -1, 25, out buffer, out bytes)) return false;
            try
            {
                // WTSINFOEX Level 1, x64: its union is 8-byte aligned.
                return bytes >= 20 && Marshal.ReadInt32(buffer, 0) == 1 && Marshal.ReadInt32(buffer, 12) == 0 && Marshal.ReadInt32(buffer, 16) == 1;
            }
            finally { WTSFreeMemory(buffer); }
        }
        public static long GetExStyle(IntPtr h) { return h == IntPtr.Zero ? 0 : GetWindowLongPtr(h, -20).ToInt64(); }
        public static void MakePassive(IntPtr h) { SetWindowLongPtr(h, -20, new IntPtr(GetExStyle(h) | 0x80000 | 0x20 | 0x80 | 0x08000000)); }
        public static void MoveNoActivate(IntPtr h, OverlayPlacement p) { if (h != IntPtr.Zero) SetWindowPos(h, new IntPtr(-1), p.X, p.Y, p.Width, p.Height, 0x0010); }
    }
}
