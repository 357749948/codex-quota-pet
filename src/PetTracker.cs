using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace CodexQuotaPet
{
    public sealed class PetAnchor
    {
        public bool Visible;
        public double Left, Top, Width, Height;
        public double WorkLeft, WorkTop, WorkWidth, WorkHeight;
        public uint Dpi;
        public long WindowHandle;
        public string Name;
    }

    // Observes the existing pet. It never changes the Codex window or its contents.
    public sealed class PetTracker : IDisposable
    {
        public event Action<PetAnchor> Changed;

        private const int SampleMilliseconds = 150;
        private const int HiddenMilliseconds = 1000;
        private const int RediscoveryMilliseconds = 2000;
        private readonly object gate = new object();
        private readonly ManualResetEvent stopping = new ManualResetEvent(false);
        private Thread worker;
        private bool disposed;
        private PetAnchor lastPublished;

        private sealed class Target
        {
            public IntPtr Window;
            public uint ProcessId;
            public AutomationElement Image;
        }

        public void Start()
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("PetTracker");
                if (worker != null) return;
                worker = new Thread(Track);
                worker.Name = "Codex pet position observer";
                worker.IsBackground = true;
                worker.SetApartmentState(ApartmentState.MTA);
                worker.Start();
            }
        }

        public void Dispose()
        {
            Thread thread;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                stopping.Set();
                thread = worker;
            }
            // A misbehaving external accessibility provider must not block app exit.
            if (thread != null && thread != Thread.CurrentThread) thread.Join(500);
            // The worker can still be in a provider call. Do not dispose its wait handle.
        }

        private void Track()
        {
            IntPtr previousDpiContext = EnterPhysicalCoordinates();
            Target target = null;
            long nextDiscovery = 0;
            Stopwatch clock = Stopwatch.StartNew();
            int initialRetries = 3;
            try
            {
                while (!stopping.WaitOne(0))
                {
                    PetAnchor anchor = null;
                    try
                    {
                        if (target == null || clock.ElapsedMilliseconds >= nextDiscovery)
                        {
                            target = FindUniqueTarget();
                            nextDiscovery = clock.ElapsedMilliseconds + RediscoveryMilliseconds;
                        }
                        if (target != null) anchor = ReadAnchor(target);
                        if (anchor == null)
                        {
                            target = null;
                            anchor = HiddenAnchor();
                        }
                        else initialRetries = 0;
                    }
                    catch (Exception exception)
                    {
                        Trace.WriteLine("Pet position observation: " + exception.GetType().Name);
                        target = null;
                        anchor = HiddenAnchor();
                    }
                    Publish(anchor);
                    // Chromium sometimes exposes only its accessibility shell on first access.
                    int delay = anchor.Visible || initialRetries-- > 0
                        ? SampleMilliseconds : HiddenMilliseconds;
                    if (stopping.WaitOne(delay)) break;
                }
            }
            finally { RestoreDpiContext(previousDpiContext); }
        }

        private void Publish(PetAnchor anchor)
        {
            if (stopping.WaitOne(0) || SameAnchor(lastPublished, anchor)) return;
            lastPublished = anchor;
            Action<PetAnchor> callback = Changed;
            if (callback == null) return;
            try { callback(anchor); }
            catch (Exception exception)
            {
                Trace.WriteLine("Pet position subscriber: " + exception.GetType().Name);
            }
        }

        public static PetAnchor ProbeOnce()
        {
            IntPtr previousDpiContext = EnterPhysicalCoordinates();
            try
            {
                // Requesting the subtree can enable Chromium accessibility asynchronously.
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    Target target = FindUniqueTarget();
                    PetAnchor anchor = target == null ? null : ReadAnchor(target);
                    if (anchor != null) return anchor;
                    if (attempt < 3) Thread.Sleep(SampleMilliseconds);
                }
                return HiddenAnchor();
            }
            catch (Exception exception)
            {
                Trace.WriteLine("Pet position probe: " + exception.GetType().Name);
                return HiddenAnchor();
            }
            finally { RestoreDpiContext(previousDpiContext); }
        }

        private static Target FindUniqueTarget()
        {
            HashSet<uint> processIds = AppProcessIds();
            if (processIds.Count == 0) return null;
            List<Target> candidates = new List<Target>();
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                if (!processIds.Contains(processId) || !IsCandidateWindow(window)) return true;
                candidates.Add(new Target { Window = window, ProcessId = processId });
                return true;
            }, IntPtr.Zero);

            Target unique = null;
            foreach (Target candidate in candidates)
            {
                try
                {
                    AutomationElement root = AutomationElement.FromHandle(candidate.Window);
                    if (root == null) continue;
                    AutomationElementCollection images = root.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image));
                    foreach (AutomationElement image in images)
                    {
                        Target match = new Target
                        {
                            Window = candidate.Window,
                            ProcessId = candidate.ProcessId,
                            Image = image
                        };
                        if (ReadAnchor(match) == null) continue;
                        // No guessing when several visible pet images are present.
                        if (unique != null) return null;
                        unique = match;
                    }
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }
            return unique;
        }

        private static HashSet<uint> AppProcessIds()
        {
            HashSet<uint> result = new HashSet<uint>();
            foreach (string name in new string[] { "ChatGPT", "Codex" })
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        try { result.Add((uint)process.Id); }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            return result;
        }

        private static bool IsCandidateWindow(IntPtr window)
        {
            if (!IsVisibleWindow(window)) return false;
            long extendedStyle = GetExtendedStyle(window);
            const long required = 0x00000008L | 0x00000080L | 0x00080000L;
            // TOPMOST + TOOLWINDOW + LAYERED. Mouse pass-through can change on hover.
            if ((extendedStyle & required) != required) return false;
            StringBuilder className = new StringBuilder(128);
            if (GetClassName(window, className, className.Capacity) == 0) return false;
            return className.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);
        }

        private static bool IsVisibleWindow(IntPtr window)
        {
            if (!IsWindow(window) || !IsWindowVisible(window) || IsIconic(window)) return false;
            int cloaked;
            try
            {
                if (DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return false;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return true;
        }

        private static PetAnchor ReadAnchor(Target target)
        {
            if (target.Image == null || !IsVisibleWindow(target.Window)) return null;
            uint processId;
            GetWindowThreadProcessId(target.Window, out processId);
            if (processId != target.ProcessId) return null;

            AutomationElement.AutomationElementInformation info = target.Image.Current;
            if (info.IsOffscreen || info.ControlType != ControlType.Image || !IsPetName(info.Name))
                return null;
            System.Windows.Rect rectangle = info.BoundingRectangle;
            if (!ValidRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height)) return null;
            NativeRect windowRect;
            if (!GetWindowRect(target.Window, out windowRect)) return null;
            // UIA and Win32 are both physical pixels after entering per-monitor awareness.
            if (rectangle.Right <= windowRect.Left || rectangle.Left >= windowRect.Right ||
                rectangle.Bottom <= windowRect.Top || rectangle.Top >= windowRect.Bottom) return null;

            NativePoint center = new NativePoint
            {
                X = (int)Math.Round(rectangle.X + rectangle.Width / 2),
                Y = (int)Math.Round(rectangle.Y + rectangle.Height / 2)
            };
            IntPtr monitor = MonitorFromPoint(center, 2);
            MonitorInfo monitorInfo = new MonitorInfo();
            monitorInfo.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo)) return null;
            uint dpi = 96;
            try
            {
                uint currentDpi = GetDpiForWindow(target.Window);
                if (currentDpi != 0) dpi = currentDpi;
            }
            catch (EntryPointNotFoundException) { }
            return new PetAnchor
            {
                Visible = true,
                Left = rectangle.X, Top = rectangle.Y,
                Width = rectangle.Width, Height = rectangle.Height,
                WorkLeft = monitorInfo.Work.Left, WorkTop = monitorInfo.Work.Top,
                WorkWidth = monitorInfo.Work.Right - monitorInfo.Work.Left,
                WorkHeight = monitorInfo.Work.Bottom - monitorInfo.Work.Top,
                Dpi = dpi, WindowHandle = target.Window.ToInt64(), Name = info.Name
            };
        }

        private static bool IsPetName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            return name.IndexOf("宠物", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("寵物", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("mascot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.Equals("pet", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("pet ", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf(" pet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ValidRectangle(double left, double top, double width, double height)
        {
            return IsFinite(left) && IsFinite(top) && IsFinite(width) && IsFinite(height) &&
                width > 0 && height > 0 && left >= Int32.MinValue && top >= Int32.MinValue &&
                left + width <= Int32.MaxValue && top + height <= Int32.MaxValue;
        }

        private static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static PetAnchor HiddenAnchor() { return new PetAnchor { Visible = false, Name = "" }; }

        private static bool SameAnchor(PetAnchor first, PetAnchor second)
        {
            if (first == null || second == null || first.Visible != second.Visible) return false;
            if (!first.Visible) return true;
            return first.Left == second.Left && first.Top == second.Top &&
                first.Width == second.Width && first.Height == second.Height &&
                first.WorkLeft == second.WorkLeft && first.WorkTop == second.WorkTop &&
                first.WorkWidth == second.WorkWidth && first.WorkHeight == second.WorkHeight &&
                first.Dpi == second.Dpi && first.WindowHandle == second.WindowHandle && first.Name == second.Name;
        }

        public static void RunSelfTests()
        {
            // These are safety boundaries, including off-monitor positions and unusable UIA rectangles.
            if (!ValidRectangle(-1920, 20, 160, 174) || ValidRectangle(0, 0, 0, 174) ||
                ValidRectangle(Double.NaN, 0, 160, 174) || ValidRectangle(0, 0, Double.PositiveInfinity, 174))
                throw new InvalidOperationException("Pet rectangle validation failed.");
            if (!IsPetName("Null Signal 宠物") || !IsPetName("Null Signal pet") ||
                IsPetName("profile picture") || IsPetName("competitor"))
                throw new InvalidOperationException("Pet identity validation failed.");
        }

        private static IntPtr EnterPhysicalCoordinates()
        {
            try { return SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { return IntPtr.Zero; }
        }

        private static void RestoreDpiContext(IntPtr previous)
        {
            if (previous == IntPtr.Zero) return;
            try { SetThreadDpiAwarenessContext(previous); }
            catch (EntryPointNotFoundException) { }
        }

        private static long GetExtendedStyle(IntPtr window)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr(window, -20).ToInt64() : GetWindowLong(window, -20);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    }
}
