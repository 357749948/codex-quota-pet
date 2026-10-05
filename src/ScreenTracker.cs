using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexQuotaPet
{
    public sealed class ScreenMatch
    {
        public bool Visible;
        public PetAnchor Anchor;
        public ScreenFrame Frame;
        public double Score;
    }

    // Matches the rendered pose against opaque sprite pixels outside its screen.
    // The caller owns lock/unlock detection. Capture stays disabled until enabled.
    public sealed class ScreenTracker : IDisposable
    {
        private const int FrameWidth = 192;
        private const int FrameHeight = 208;
        private const int IntervalMilliseconds = 50;
        private const double MaximumScore = 12.0;
        private readonly object _gate = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private Thread _worker;
        private bool _enabled;
        private volatile bool _disposed;
        private int _settingsVersion;
        private PetAnchor _anchor;
        private ScreenMatch _lastPublished;

        public event Action<ScreenMatch> Changed;

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("ScreenTracker");
                if (_worker != null) return;
                _worker = new Thread(Work);
                _worker.IsBackground = true;
                _worker.Name = "Codex pet screen pose observer";
                _worker.Start();
            }
        }

        public void SetAnchor(PetAnchor anchor)
        {
            bool hidden;
            lock (_gate)
            {
                if (_disposed || SameAnchor(_anchor, anchor)) return;
                _anchor = CopyAnchor(anchor);
                _settingsVersion++;
                hidden = !UsableAnchor(_anchor);
            }
            if (hidden) Publish(Hidden(anchor), -1);
            _wake.Set();
        }

        public void SetEnabled(bool enabled)
        {
            PetAnchor anchor;
            lock (_gate)
            {
                if (_disposed || _enabled == enabled) return;
                _enabled = enabled;
                _settingsVersion++;
                anchor = CopyAnchor(_anchor);
            }
            if (!enabled) Publish(Hidden(anchor), -1);
            _wake.Set();
        }

        public void Dispose()
        {
            Thread worker;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _settingsVersion++;
                worker = _worker;
            }
            _wake.Set();
            if (worker != null && worker != Thread.CurrentThread) worker.Join(600);
        }

        private void Work()
        {
            IntPtr previousContext = IntPtr.Zero;
            try { previousContext = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            try
            {
                while (!_disposed)
                {
                    Stopwatch cycle = Stopwatch.StartNew();
                    PetAnchor anchor;
                    ScreenFrame previousFrame;
                    PetAnchor previousAnchor;
                    int version;
                    bool enabled;
                    lock (_gate)
                    {
                        anchor = CopyAnchor(_anchor);
                        version = _settingsVersion;
                        enabled = _enabled;
                        previousFrame = _lastPublished != null && _lastPublished.Visible ? _lastPublished.Frame : null;
                        previousAnchor = _lastPublished != null && _lastPublished.Visible ? CopyAnchor(_lastPublished.Anchor) : null;
                    }
                    if (!enabled || !UsableAnchor(anchor))
                    {
                        Publish(Hidden(anchor), version);
                        _wake.WaitOne(1000);
                        continue;
                    }

                    // Resolve the desktop process and validate local templates on
                    // this worker. File reads and hashing must never block WPF.
                    uint processId;
                    GetWindowThreadProcessId(new IntPtr(anchor.WindowHandle), out processId);
                    if (!ScreenFrames.ReloadForProcess((int)processId))
                    {
                        Publish(Hidden(anchor), version);
                        _wake.WaitOne(1000);
                        continue;
                    }

                    ScreenMatch match;
                    try
                    {
                        int width = Math.Max(1, (int)Math.Round(anchor.Width));
                        int height = Math.Max(1, (int)Math.Round(anchor.Height));
                        int left = (int)Math.Round(anchor.Left);
                        int top = (int)Math.Round(anchor.Top);
                        using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics graphics = Graphics.FromImage(bitmap))
                                graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                            BitmapData pixels = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                            try
                            {
                                int stride = Math.Abs(pixels.Stride);
                                byte[] bgra = new byte[stride * height];
                                // Our newly allocated Bitmap is usually top-down. Copy
                                // per row as well for the uncommon negative-stride case.
                                for (int y = 0; y < height; y++)
                                    Marshal.Copy(IntPtr.Add(pixels.Scan0, y * pixels.Stride), bgra, y * stride, stride);
                                match = MatchCore(bgra, width, height, stride, anchor, previousFrame, previousAnchor);
                            }
                            finally { bitmap.UnlockBits(pixels); }
                        }
                    }
                    catch (Exception)
                    {
                        // Locking, desktop switches and protected surfaces can make
                        // capture unavailable. Never reuse an unverified old screen.
                        match = Hidden(anchor);
                    }
                    Publish(match, version);
                    int wait = Math.Max(10, IntervalMilliseconds - (int)cycle.ElapsedMilliseconds);
                    _wake.WaitOne(wait);
                }
            }
            finally
            {
                if (previousContext != IntPtr.Zero)
                    try { SetThreadDpiAwarenessContext(previousContext); }
                    catch (EntryPointNotFoundException) { }
            }
        }

        public static ScreenMatch MatchPixels(byte[] bgra, int width, int height, int stride, PetAnchor anchor, ScreenFrame previousFrame)
        {
            return MatchCore(bgra, width, height, stride, anchor, previousFrame, anchor);
        }

        private static ScreenMatch MatchCore(byte[] bgra, int width, int height, int stride, PetAnchor anchor, ScreenFrame previousFrame, PetAnchor previousAnchor)
        {
            if (!UsableAnchor(anchor) || bgra == null || width <= 0 || height <= 0 || width > 2048 || height > 2048 || stride == Int32.MinValue ||
                Math.Abs(stride) < (long)width * 4 || (long)Math.Abs(stride) * height > bgra.Length)
                return Hidden(anchor);

            bool[] previousMask = BuildPreviousMask(previousFrame, previousAnchor);
            double bestScore = Double.MaxValue;
            ScreenFrame best = null;
            foreach (ScreenFrame frame in ScreenFrames.All)
            {
                if (frame == null || frame.Samples == null || frame.Samples.Length == 0) continue;
                int count = 0, bad = 0;
                double total = 0;
                double expectedSum = 0, observedSum = 0, expectedSquares = 0, observedSquares = 0, productSum = 0;
                foreach (ScreenSample sample in frame.Samples)
                {
                    int x = Math.Min(width - 1, Math.Max(0, (int)Math.Floor((sample.X + .5) * width / FrameWidth)));
                    int y = Math.Min(height - 1, Math.Max(0, (int)Math.Floor((sample.Y + .5) * height / FrameHeight)));
                    if (ExcludedByPreviousMask(previousMask, x, y, width, height, anchor, previousAnchor)) continue;
                    int minimum = Int32.MaxValue;
                    double observedLight = 0;
                    // A one-pixel neighborhood accommodates fractional browser scaling
                    // and antialiasing without requiring a large image library.
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int py = y + dy;
                        if (py < 0 || py >= height) continue;
                        int row = (stride < 0 ? height - 1 - py : py) * Math.Abs(stride);
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int px = x + dx;
                            if (px < 0 || px >= width) continue;
                            int offset = row + px * 4;
                            int difference = Math.Abs(bgra[offset + 2] - sample.R) + Math.Abs(bgra[offset + 1] - sample.G) + Math.Abs(bgra[offset] - sample.B);
                            if (difference < minimum)
                            {
                                minimum = difference;
                                observedLight = (bgra[offset] + bgra[offset + 1] + bgra[offset + 2]) / 3.0;
                            }
                        }
                    }
                    double error = minimum / 3.0;
                    total += error;
                    double expectedLight = (sample.R + sample.G + sample.B) / 3.0;
                    expectedSum += expectedLight; observedSum += observedLight;
                    expectedSquares += expectedLight * expectedLight;
                    observedSquares += observedLight * observedLight;
                    productSum += expectedLight * observedLight;
                    if (error > 38) bad++;
                    count++;
                }
                int required = Math.Max(24, (int)Math.Ceiling(frame.Samples.Length * .45));
                if (count < required) continue;
                double badFraction = (double)bad / count;
                double score = total / count + 20 * badFraction;
                if (badFraction > .20) score = Math.Max(MaximumScore + 1, score);
                double expectedVariance = expectedSquares / count - Math.Pow(expectedSum / count, 2);
                double observedVariance = observedSquares / count - Math.Pow(observedSum / count, 2);
                if (expectedVariance > 4)
                {
                    double covariance = productSum / count - expectedSum * observedSum / count / count;
                    double correlation = observedVariance > 0 ? covariance / Math.Sqrt(expectedVariance * observedVariance) : 0;
                    // A flat dark desktop can resemble individual shell colors but
                    // cannot reproduce the sprite's pattern of light and dark parts.
                    if (observedVariance < expectedVariance * .12 || correlation < .35)
                        score = Math.Max(MaximumScore + 1, score);
                }
                if (score < bestScore) { bestScore = score; best = frame; }
            }
            if (best == null || bestScore > MaximumScore) return Hidden(anchor, bestScore == Double.MaxValue ? 255 : bestScore);
            return new ScreenMatch
            {
                Visible = best.Supported,
                Anchor = CopyAnchor(anchor),
                Frame = best,
                Score = bestScore
            };
        }

        private static bool[] BuildPreviousMask(ScreenFrame previous, PetAnchor previousAnchor)
        {
            if (previous == null || !previous.Supported || previous.Mask == null || previous.Mask.Length == 0) return null;
            int padX = 2, padY = 2;
            if (UsableAnchor(previousAnchor))
            {
                padX = Math.Max(2, (int)Math.Ceiling(1.5 * FrameWidth / previousAnchor.Width));
                padY = Math.Max(2, (int)Math.Ceiling(1.5 * FrameHeight / previousAnchor.Height));
            }
            bool[] mask = new bool[FrameWidth * FrameHeight];
            foreach (ScreenRun run in previous.Mask)
            {
                int fromX = Math.Max(0, run.X - padX);
                int toX = Math.Min(FrameWidth, run.X + run.Width + padX);
                int fromY = Math.Max(0, run.Y - padY);
                int toY = Math.Min(FrameHeight - 1, run.Y + padY);
                for (int y = fromY; y <= toY; y++)
                    for (int x = fromX; x < toX; x++) mask[y * FrameWidth + x] = true;
            }
            return mask;
        }

        private static bool ExcludedByPreviousMask(bool[] mask, int x, int y, int width, int height, PetAnchor current, PetAnchor previous)
        {
            if (mask == null) return false;
            double oldX = (x + .5) * FrameWidth / width;
            double oldY = (y + .5) * FrameHeight / height;
            if (UsableAnchor(current) && UsableAnchor(previous))
            {
                oldX = (current.Left + (x + .5) * current.Width / width - previous.Left) * FrameWidth / previous.Width;
                oldY = (current.Top + (y + .5) * current.Height / height - previous.Top) * FrameHeight / previous.Height;
            }
            int px = (int)Math.Floor(oldX), py = (int)Math.Floor(oldY);
            return px >= 0 && px < FrameWidth && py >= 0 && py < FrameHeight && mask[py * FrameWidth + px];
        }

        private void Publish(ScreenMatch match, int settingsVersion)
        {
            Action<ScreenMatch> changed;
            lock (_gate)
            {
                if (_disposed || (settingsVersion >= 0 && settingsVersion != _settingsVersion)) return;
                if (match.Visible && (!_enabled || !UsableAnchor(_anchor))) return;
                if (SameMatch(_lastPublished, match)) return;
                _lastPublished = match;
                changed = Changed;
            }
            if (changed == null) return;
            foreach (Delegate subscriber in changed.GetInvocationList())
                try { ((Action<ScreenMatch>)subscriber)(match); }
                catch (Exception) { }
        }

        private static bool SameMatch(ScreenMatch left, ScreenMatch right)
        {
            if (left == null || right == null) return left == right;
            return left.Visible == right.Visible &&
                Object.ReferenceEquals(left.Frame, right.Frame) &&
                SameAnchor(left.Anchor, right.Anchor);
        }

        private static bool SameAnchor(PetAnchor left, PetAnchor right)
        {
            if (left == null || right == null) return left == right;
            return left.Visible == right.Visible && left.Left == right.Left && left.Top == right.Top &&
                left.Width == right.Width && left.Height == right.Height && left.Dpi == right.Dpi &&
                left.WindowHandle == right.WindowHandle && left.Name == right.Name;
        }

        private static bool UsableAnchor(PetAnchor anchor)
        {
            return anchor != null && anchor.Visible && Finite(anchor.Left) && Finite(anchor.Top) &&
                Finite(anchor.Width) && Finite(anchor.Height) && anchor.Width >= 24 && anchor.Height >= 24 &&
                anchor.Width <= 2048 && anchor.Height <= 2048 && Math.Abs(anchor.Left) <= 100000 && Math.Abs(anchor.Top) <= 100000;
        }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static PetAnchor CopyAnchor(PetAnchor anchor)
        {
            if (anchor == null) return null;
            return new PetAnchor
            {
                Visible = anchor.Visible, Left = anchor.Left, Top = anchor.Top, Width = anchor.Width, Height = anchor.Height,
                WorkLeft = anchor.WorkLeft, WorkTop = anchor.WorkTop, WorkWidth = anchor.WorkWidth, WorkHeight = anchor.WorkHeight,
                Dpi = anchor.Dpi, WindowHandle = anchor.WindowHandle, Name = anchor.Name
            };
        }
        private static ScreenMatch Hidden(PetAnchor anchor) { return Hidden(anchor, 255); }
        private static ScreenMatch Hidden(PetAnchor anchor, double score)
        {
            return new ScreenMatch { Visible = false, Anchor = CopyAnchor(anchor), Score = score };
        }

        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
