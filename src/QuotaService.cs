using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexQuotaPet
{
    public sealed class QuotaWindow
    {
        public double? RemainingPercent;
        public int? WindowMinutes;
        public DateTime? ResetsAtUtc;
        public string Label;
    }

    public sealed class QuotaSnapshot
    {
        public List<QuotaWindow> Windows;
        public DateTime? LastSuccessUtc;
        public string State;
        public string Error;
        public string PlanType;
    }

    // The one worker owns the protocol, its child process, and every refresh.
    // Changed is raised on that worker; UI subscribers must dispatch to the UI.
    public sealed class QuotaService : IDisposable
    {
        private const int RequestTimeoutSeconds = 15;
        private static readonly object ProcessStartHandleGate = new object();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetStdHandle(int handle, IntPtr value);
        private readonly object _gate = new object();
        private readonly Queue<string> _incoming = new Queue<string>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly HashSet<string> _triggeredResets = new HashSet<string>();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private Thread _worker;
        private Process _process;
        private Stream _input;
        private bool _started;
        private volatile bool _disposed;
        private bool _petVisible = true;
        private bool _refreshRequested = true;
        private bool _manualRefreshRequested;
        private bool _acceptQuotaNotifications;
        private bool _connectionInitialized;
        private int _accountEpoch;
        private int _generation;
        private int _nextId;
        private int _failures;
        private DateTime _nextAttemptUtc = DateTime.MinValue;
        private string _planType;
        private string _accountFingerprint;
        private string _selectedLimitId;
        private Dictionary<string, object> _lastBucket;
        private QuotaSnapshot _current = new QuotaSnapshot
        {
            Windows = new List<QuotaWindow>(),
            State = "loading"
        };

        public event Action<QuotaSnapshot> Changed;

        public QuotaSnapshot Current
        {
            get { lock (_gate) { return CopySnapshot(_current); } }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("QuotaService");
                if (_started) return;
                _started = true;
                _worker = new Thread(Work);
                _worker.IsBackground = true;
                _worker.Name = "Codex quota reader";
                _worker.Start();
            }
        }

        public void SetPetVisible(bool visible)
        {
            lock (_gate)
            {
                if (_disposed || _petVisible == visible) return;
                _petVisible = visible;
                if (visible) _refreshRequested = true;
            }
            _wake.Set();
        }

        public void Refresh()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _manualRefreshRequested = true;
            }
            _wake.Set();
        }

        public void Dispose()
        {
            Thread worker;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                worker = _worker;
            }
            _wake.Set();
            if (worker != null && worker != Thread.CurrentThread) worker.Join(5000);
            // Do not dispose the wake handle while an asynchronous stdout callback
            // could still be unwinding. It is reclaimed with this service object.
        }

        private void Work()
        {
            try
            {
                while (!_disposed)
                {
                    DrainNotifications();
                    if (_process != null && !ProcessAlive())
                    {
                        StopChild();
                        RecordFailure("error", "额度连接已断开");
                    }

                    bool visible;
                    bool requested;
                    bool manual;
                    lock (_gate)
                    {
                        visible = _petVisible;
                        requested = _refreshRequested;
                        manual = _manualRefreshRequested;
                    }
                    DateTime now = DateTime.UtcNow;
                    bool resetDue = visible && _failures == 0 && ConsumeDueReset(now);
                    if (manual || (visible && (requested || resetDue || now >= _nextAttemptUtc)))
                    {
                        lock (_gate)
                        {
                            _refreshRequested = false;
                            _manualRefreshRequested = false;
                        }
                        try
                        {
                            EnsureConnection();
                            // A private app-server might not receive another client's
                            // account-change event. Recheck its public account view.
                            CheckAccount();
                            int accountEpoch = _accountEpoch;
                            Dictionary<string, object> result = Request("account/rateLimits/read", null);
                            if (accountEpoch != _accountEpoch) throw new AccountChangedException();
                            string limitId;
                            Dictionary<string, object> bucket = ChooseBucket(result, out limitId);
                            if (bucket == null) throw new SourceFailure("error", "暂无额度信息");
                            _selectedLimitId = limitId;
                            _lastBucket = new Dictionary<string, object>(bucket);
                            _acceptQuotaNotifications = true;
                            QuotaSnapshot snapshot = SnapshotFromBucket(bucket, _planType, DateTime.UtcNow);
                            Publish(snapshot);
                            _failures = 0;
                            _nextAttemptUtc = DateTime.UtcNow.AddSeconds(30);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (AccountChangedException)
                        {
                            // Discard a response crossing an authentication change.
                            lock (_gate)
                            {
                                _refreshRequested = true;
                                if (manual) _manualRefreshRequested = true;
                            }
                        }
                        catch (SourceFailure ex)
                        {
                            // Recreate only our private child after transport failure.
                            // Authentication/HTTP failures can recover on the same child.
                            if (ex.TransportFailure || ex.State == "login_required") StopChild();
                            RecordFailure(ex.State, ex.Message);
                        }
                        catch (Exception)
                        {
                            StopChild();
                            RecordFailure("error", "额度读取暂时失败");
                        }
                        continue;
                    }
                    _wake.WaitOne(1000);
                }
            }
            finally { StopChild(); }
        }

        private void EnsureConnection()
        {
            if (_disposed) throw new OperationCanceledException();
            if (_process != null && ProcessAlive() && _connectionInitialized) return;
            StopChild();
            string executable = FindCodexExecutable();
            if (executable == null) throw new SourceFailure("error", "未找到 Codex；请设置 CODEX_QUOTA_PET_CODEX_PATH 为原生 codex.exe 的绝对路径");

            int generation;
            lock (_gate)
            {
                generation = ++_generation;
                _incoming.Clear();
            }
            Process child = new Process();
            child.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--no-daemon app-server --stdio",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            child.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
            {
                if (args.Data == null || args.Data.Length == 0) return;
                lock (_gate)
                {
                    if (_disposed || generation != _generation) return;
                    _incoming.Enqueue(args.Data);
                }
                _wake.Set();
            };
            // Drain stderr without retaining authentication, account, or debug data.
            child.ErrorDataReceived += delegate { };
            _process = child;
            try
            {
                // Framework's redirected stdin writer may emit a console-derived
                // BOM during Start, before our first write. Supply an owned byte
                // pipe instead. The child inherits its read handle; the parent's
                // original handle is restored immediately, including on failure.
                var input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
                _input = input;
                lock (ProcessStartHandleGate)
                {
                    IntPtr original = GetStdHandle(-10);
                    bool changed = false;
                    try
                    {
                        if (!SetStdHandle(-10, input.ClientSafePipeHandle.DangerousGetHandle())) throw new IOException("Cannot provide child input pipe.");
                        changed = true;
                        if (!child.Start()) throw new InvalidOperationException();
                    }
                    finally
                    {
                        try { if (changed && !SetStdHandle(-10, original)) throw new IOException("Cannot restore parent input handle."); }
                        finally { input.DisposeLocalCopyOfClientHandle(); }
                    }
                }
                child.BeginOutputReadLine();
                child.BeginErrorReadLine();
            }
            catch (Exception)
            {
                StopChild();
                throw new SourceFailure("error", "无法启动额度连接", true);
            }

            Dictionary<string, object> client = new Dictionary<string, object>();
            client["name"] = "codex_quota_pet";
            client["title"] = "Codex Quota Pet";
            client["version"] = BuildVersion.Value;
            Dictionary<string, object> initialize = new Dictionary<string, object>();
            initialize["clientInfo"] = client;
            Request("initialize", initialize);
            Send(new Dictionary<string, object>
            {
                { "method", "initialized" },
                { "params", new Dictionary<string, object>() }
            });
            _connectionInitialized = true;
            _acceptQuotaNotifications = false;
        }

        private void CheckAccount()
        {
            int accountEpoch = _accountEpoch;
            Dictionary<string, object> result = Request("account/read", new Dictionary<string, object>
            {
                { "refreshToken", false }
            });
            if (accountEpoch != _accountEpoch) throw new AccountChangedException();
            ApplyAccount(result);
        }

        private void ApplyAccount(Dictionary<string, object> result)
        {
            Dictionary<string, object> account = AsObject(Get(result, "account"));
            string type = GetString(account, "type");
            if (account == null || String.IsNullOrEmpty(type))
                throw new SourceFailure("login_required", "请先登录 Codex");
            if (!String.Equals(type, "chatgpt", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(type, "chatgptAuthTokens", StringComparison.OrdinalIgnoreCase))
                throw new SourceFailure("login_required", "请使用 ChatGPT 账户登录");
            string fingerprint;
            // Keep only an in-memory digest of public account metadata. Never
            // persist or expose an email, account ID, token, or credential.
            using (SHA256 digest = SHA256.Create())
                fingerprint = Convert.ToBase64String(digest.ComputeHash(Encoding.UTF8.GetBytes(_json.Serialize(account))));
            if (_accountFingerprint != null && !String.Equals(_accountFingerprint, fingerprint, StringComparison.Ordinal))
                InvalidateAccount("loading", null);
            _accountFingerprint = fingerprint;
            _planType = GetString(account, "planType");
        }

        private Dictionary<string, object> Request(string method, Dictionary<string, object> parameters)
        {
            if (_disposed) throw new OperationCanceledException();
            int id = ++_nextId;
            Dictionary<string, object> request = new Dictionary<string, object>();
            request["id"] = id;
            request["method"] = method;
            if (parameters != null) request["params"] = parameters;
            Send(request);
            Stopwatch elapsed = Stopwatch.StartNew();
            while (!_disposed && elapsed.Elapsed.TotalSeconds < RequestTimeoutSeconds)
            {
                Dictionary<string, object> message;
                while ((message = TakeMessage()) != null)
                {
                    double? responseId = Number(Get(message, "id"));
                    if (responseId.HasValue && responseId.Value == id)
                    {
                        Dictionary<string, object> error = AsObject(Get(message, "error"));
                        if (error != null) throw ErrorFromResponse(error);
                        Dictionary<string, object> result = AsObject(Get(message, "result"));
                        if (result == null) throw new SourceFailure("error", "额度响应暂不可用");
                        return result;
                    }
                    HandleNotification(message);
                }
                if (!ProcessAlive()) throw new SourceFailure("error", "额度连接已断开", true);
                _wake.WaitOne(250);
            }
            if (_disposed) throw new OperationCanceledException();
            throw new SourceFailure("error", "额度读取超时", true);
        }

        private void Send(Dictionary<string, object> message)
        {
            try
            {
                // JSON-RPC stdio is UTF-8 without a BOM, regardless of locale.
                byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(message) + "\n");
                _input.Write(bytes, 0, bytes.Length);
                _input.Flush();
            }
            catch (Exception) { throw new SourceFailure("error", "额度连接已断开", true); }
        }

        private Dictionary<string, object> TakeMessage()
        {
            while (true)
            {
                string line;
                lock (_gate)
                {
                    if (_incoming.Count == 0) return null;
                    line = _incoming.Dequeue();
                }
                try
                {
                    Dictionary<string, object> message = AsObject(_json.DeserializeObject(line));
                    if (message != null) return message;
                }
                catch (Exception) { /* Ignore diagnostics that are not protocol JSON. */ }
            }
        }

        private void DrainNotifications()
        {
            Dictionary<string, object> message;
            while ((message = TakeMessage()) != null) HandleNotification(message);
        }

        private void HandleNotification(Dictionary<string, object> message)
        {
            string method = GetString(message, "method");
            Dictionary<string, object> parameters = AsObject(Get(message, "params"));
            if (method == "account/updated")
            {
                string authMode = GetString(parameters, "authMode");
                bool unavailable = parameters != null && parameters.ContainsKey("authMode") &&
                    authMode != "chatgpt" && authMode != "chatgptAuthTokens";
                string error = unavailable ? (authMode == null ? "请先登录 Codex" : "请使用 ChatGPT 账户登录") : null;
                InvalidateAccount(unavailable ? "login_required" : "loading", error);
                lock (_gate) { _refreshRequested = true; }
                return;
            }
            if (method != "account/rateLimits/updated" || parameters == null || !_acceptQuotaNotifications) return;
            string limitId;
            Dictionary<string, object> incoming = ChooseBucket(parameters, out limitId);
            if (incoming == null) return;
            if (!String.IsNullOrEmpty(_selectedLimitId) && !String.IsNullOrEmpty(limitId) &&
                !String.Equals(_selectedLimitId, limitId, StringComparison.Ordinal)) return;
            Dictionary<string, object> merged = _lastBucket == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(_lastBucket);
            foreach (KeyValuePair<string, object> item in incoming)
            {
                Dictionary<string, object> incomingWindow = AsObject(item.Value);
                Dictionary<string, object> previousWindow = AsObject(Get(merged, item.Key));
                if ((item.Key == "primary" || item.Key == "secondary") && incomingWindow != null && previousWindow != null)
                {
                    Dictionary<string, object> window = new Dictionary<string, object>(previousWindow);
                    foreach (KeyValuePair<string, object> field in incomingWindow) window[field.Key] = field.Value;
                    merged[item.Key] = window;
                }
                else merged[item.Key] = item.Value;
            }
            _lastBucket = merged;
            _selectedLimitId = limitId ?? _selectedLimitId;
            Publish(SnapshotFromBucket(merged, _planType, DateTime.UtcNow));
            _failures = 0;
            _nextAttemptUtc = DateTime.UtcNow.AddSeconds(30);
        }

        private void RecordFailure(string state, string error)
        {
            _failures++;
            int seconds = _failures == 1 ? 30 : _failures == 2 ? 60 : _failures == 3 ? 120 : 300;
            _nextAttemptUtc = DateTime.UtcNow.AddSeconds(seconds);
            if (state == "login_required")
            {
                InvalidateAccount(state, error);
                return;
            }
            QuotaSnapshot snapshot = Current;
            snapshot.State = state;
            snapshot.Error = error;
            Publish(snapshot);
        }

        private void InvalidateAccount(string state, string error)
        {
            _accountEpoch++;
            _accountFingerprint = null;
            _planType = null;
            _selectedLimitId = null;
            _lastBucket = null;
            _acceptQuotaNotifications = false;
            _triggeredResets.Clear();
            Publish(new QuotaSnapshot
            {
                Windows = new List<QuotaWindow>(),
                State = state,
                Error = error
            });
        }

        private bool ConsumeDueReset(DateTime now)
        {
            bool found = false;
            QuotaSnapshot snapshot = Current;
            foreach (QuotaWindow window in snapshot.Windows)
            {
                if (!window.ResetsAtUtc.HasValue || now < window.ResetsAtUtc.Value) continue;
                string key = (_selectedLimitId ?? "codex") + ":" + window.ResetsAtUtc.Value.Ticks.ToString(CultureInfo.InvariantCulture);
                if (_triggeredResets.Add(key)) found = true;
            }
            return found;
        }

        private bool ProcessAlive()
        {
            try { return _process != null && !_process.HasExited; }
            catch (Exception) { return false; }
        }

        private void StopChild()
        {
            Process child = _process;
            _process = null;
            Stream input = _input;
            _input = null;
            try { if (input != null) input.Dispose(); }
            catch (Exception) { }
            _connectionInitialized = false;
            _acceptQuotaNotifications = false;
            lock (_gate)
            {
                _generation++;
                _incoming.Clear();
            }
            if (child == null) return;
            try
            {
                if (!child.WaitForExit(2000))
                {
                    child.Kill();
                    child.WaitForExit(1000);
                }
            }
            catch (Exception) { }
            child.Dispose();
        }

        private void Publish(QuotaSnapshot snapshot)
        {
            Action<QuotaSnapshot> changed;
            lock (_gate)
            {
                if (_disposed) return;
                _current = CopySnapshot(snapshot);
                changed = Changed;
            }
            if (changed == null) return;
            foreach (Delegate subscriber in changed.GetInvocationList())
            {
                try { ((Action<QuotaSnapshot>)subscriber)(CopySnapshot(snapshot)); }
                catch (Exception) { /* A closing UI must not stop the quota reader. */ }
            }
        }

        private static string FindCodexExecutable()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_QUOTA_PET_CODEX_PATH");
            if (!String.IsNullOrWhiteSpace(configured))
            {
                if (!FullyQualifiedPath(configured) || !String.Equals(Path.GetExtension(configured), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(configured))
                    throw new SourceFailure("error", "CODEX_QUOTA_PET_CODEX_PATH 必须指向已存在的原生 codex.exe 绝对路径");
                return Path.GetFullPath(configured);
            }
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    string directory = entry.Trim().Trim('"');
                    if (!FullyQualifiedPath(directory)) continue;
                    string candidate = Path.Combine(directory, "codex.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string package = Path.Combine(roaming, @"npm\node_modules\@openai\codex");
            string[] candidates = new string[]
            {
                Path.Combine(package, @"node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe"),
                Path.Combine(roaming, @"npm\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe"),
                Path.Combine(package, @"vendor\x86_64-pc-windows-msvc\codex\codex.exe")
            };
            foreach (string candidate in candidates) if (File.Exists(candidate)) return candidate;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string desktopBin = Path.Combine(local, @"OpenAI\Codex\bin");
            if (Directory.Exists(desktopBin))
            {
                try
                {
                    string best = null;
                    DateTime newest = DateTime.MinValue;
                    foreach (string directory in Directory.GetDirectories(desktopBin))
                    {
                        string candidate = Path.Combine(directory, "codex.exe");
                        if (!File.Exists(candidate)) continue;
                        DateTime modified = File.GetLastWriteTimeUtc(candidate);
                        if (best == null || modified > newest) { best = candidate; newest = modified; }
                    }
                    if (best != null) return best;
                }
                catch (Exception) { }
            }
            return null;
        }

        private static bool FullyQualifiedPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            bool drive = path.Length >= 3 && Char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
            bool unc = path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?", StringComparison.Ordinal) && !path.StartsWith(@"\\.", StringComparison.Ordinal);
            if (!drive && !unc) return false;
            try { return !String.IsNullOrEmpty(Path.GetFullPath(path)); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        private static Dictionary<string, object> ChooseBucket(Dictionary<string, object> result, out string limitId)
        {
            limitId = null;
            Dictionary<string, object> buckets = AsObject(Get(result, "rateLimitsByLimitId"));
            Dictionary<string, object> chosen = AsObject(Get(buckets, "codex"));
            if (chosen != null) limitId = "codex";
            if (chosen == null) chosen = AsObject(Get(result, "rateLimits"));
            if (chosen == null) return null;
            limitId = GetString(chosen, "limitId") ?? limitId;
            return chosen;
        }

        private static QuotaSnapshot SnapshotFromBucket(Dictionary<string, object> bucket, string planType, DateTime updatedAt)
        {
            List<QuotaWindow> windows = new List<QuotaWindow>();
            QuotaWindow first = WindowFromObject(AsObject(Get(bucket, "primary")));
            QuotaWindow second = WindowFromObject(AsObject(Get(bucket, "secondary")));
            if (first != null) windows.Add(first);
            if (second != null) windows.Add(second);
            if (windows.Count == 0) windows.Add(new QuotaWindow { Label = "额度" });
            // The longer period stays first, so one/two-window UIs never swap at random.
            if (windows.Count == 2 && (windows[1].WindowMinutes ?? -1) > (windows[0].WindowMinutes ?? -1))
            {
                QuotaWindow temporary = windows[0];
                windows[0] = windows[1];
                windows[1] = temporary;
            }
            return new QuotaSnapshot
            {
                Windows = windows,
                LastSuccessUtc = updatedAt,
                State = "ready",
                PlanType = GetString(bucket, "planType") ?? planType
            };
        }

        private static QuotaWindow WindowFromObject(Dictionary<string, object> window)
        {
            if (window == null) return null;
            double? used = Number(Get(window, "usedPercent"));
            double? duration = Number(Get(window, "windowDurationMins"));
            int? minutes = null;
            if (duration.HasValue && duration.Value > 0 && duration.Value <= Int32.MaxValue && duration.Value == Math.Truncate(duration.Value))
                minutes = (int)duration.Value;
            DateTime? resetsAt = null;
            double? seconds = Number(Get(window, "resetsAt"));
            if (seconds.HasValue)
            {
                try { resetsAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds.Value); }
                catch (ArgumentOutOfRangeException) { }
            }
            return new QuotaWindow
            {
                RemainingPercent = used.HasValue ? (double?)Math.Max(0, Math.Min(100, 100 - used.Value)) : null,
                WindowMinutes = minutes,
                ResetsAtUtc = resetsAt,
                Label = WindowLabel(minutes)
            };
        }

        private static string WindowLabel(int? minutes)
        {
            if (!minutes.HasValue) return "额度";
            int value = minutes.Value;
            if (value == 10080) return "本周";
            if (value >= 1440 && value % 1440 == 0) return (value / 1440).ToString(CultureInfo.InvariantCulture) + "天";
            if (value % 60 == 0) return (value / 60).ToString(CultureInfo.InvariantCulture) + "小时";
            return value.ToString(CultureInfo.InvariantCulture) + "分钟";
        }

        private static QuotaSnapshot CopySnapshot(QuotaSnapshot snapshot)
        {
            List<QuotaWindow> windows = new List<QuotaWindow>();
            if (snapshot.Windows != null)
                foreach (QuotaWindow window in snapshot.Windows)
                    windows.Add(new QuotaWindow
                    {
                        RemainingPercent = window.RemainingPercent,
                        WindowMinutes = window.WindowMinutes,
                        ResetsAtUtc = window.ResetsAtUtc,
                        Label = window.Label
                    });
            return new QuotaSnapshot
            {
                Windows = windows,
                LastSuccessUtc = snapshot.LastSuccessUtc,
                State = snapshot.State,
                Error = snapshot.Error,
                PlanType = snapshot.PlanType
            };
        }

        private static Dictionary<string, object> AsObject(object value) { return value as Dictionary<string, object>; }
        private static object Get(Dictionary<string, object> value, string key)
        {
            object result;
            return value != null && value.TryGetValue(key, out result) ? result : null;
        }
        private static string GetString(Dictionary<string, object> value, string key) { return Get(value, key) as string; }
        private static double? Number(object value)
        {
            if (value == null || value is bool || value is string) return null;
            try
            {
                double result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return Double.IsNaN(result) || Double.IsInfinity(result) ? (double?)null : result;
            }
            catch (Exception) { return null; }
        }

        private SourceFailure ErrorFromResponse(Dictionary<string, object> error)
        {
            string message = (GetString(error, "message") ?? "").ToLowerInvariant();
            if (Number(Get(error, "code")) == 401 || message.Contains("401") || message.Contains("unauthorized") || message.Contains("not logged") ||
                message.Contains("not signed") || message.Contains("not authenticated") || message.Contains("authentication required") ||
                message.Contains("login required") || message.Contains("token expired") || message.Contains("token has expired") ||
                message.Contains("invalid token") || message.Contains("requires chatgpt"))
            {
                return new SourceFailure("login_required", "请重新登录 Codex");
            }
            // Never forward upstream error text, since it can contain account data.
            return new SourceFailure("error", "额度读取暂时失败");
        }

        private sealed class SourceFailure : Exception
        {
            public readonly string State;
            public readonly bool TransportFailure;
            public SourceFailure(string state, string message) : this(state, message, false) { }
            public SourceFailure(string state, string message, bool transportFailure) : base(message)
            {
                State = state;
                TransportFailure = transportFailure;
            }
        }

        private sealed class AccountChangedException : Exception { }

        public static void RunSelfTests()
        {
            Assert(!FullyQualifiedPath("C:codex.exe") && !FullyQualifiedPath(@"\codex.exe") && !FullyQualifiedPath("codex.exe"), "relative executable paths rejected");
            Assert(FullyQualifiedPath(@"C:\tools\codex.exe") && FullyQualifiedPath(@"\\server\tools\codex.exe"), "qualified executable paths accepted");
            JavaScriptSerializer json = new JavaScriptSerializer();
            Dictionary<string, object> result = AsObject(json.DeserializeObject(
                "{\"rateLimits\":{\"primary\":{\"usedPercent\":99}},\"rateLimitsByLimitId\":{\"other\":{\"primary\":{\"usedPercent\":95}},\"codex\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":20,\"windowDurationMins\":300,\"resetsAt\":1735689600},\"secondary\":{\"usedPercent\":33,\"windowDurationMins\":10080}}}}"));
            string limitId;
            Dictionary<string, object> bucket = ChooseBucket(result, out limitId);
            QuotaSnapshot snapshot = SnapshotFromBucket(bucket, "pro", DateTime.UtcNow);
            Assert(limitId == "codex" && snapshot.Windows.Count == 2, "multi-bucket selection");
            Assert(snapshot.Windows[0].Label == "本周" && snapshot.Windows[0].RemainingPercent == 67, "weekly ordering");
            Assert(snapshot.Windows[1].Label == "5小时" && snapshot.Windows[1].RemainingPercent == 80, "short-window label");
            Assert(snapshot.Windows[1].ResetsAtUtc == new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Unix seconds");

            QuotaWindow empty = WindowFromObject(new Dictionary<string, object>());
            Assert(!empty.RemainingPercent.HasValue && !empty.WindowMinutes.HasValue && !empty.ResetsAtUtc.HasValue, "missing is unknown");
            Assert(SnapshotFromBucket(new Dictionary<string, object>(), null, DateTime.UtcNow).Windows.Count == 1, "unknown placeholder");
            Assert(WindowFromObject(new Dictionary<string, object> { { "usedPercent", -15 } }).RemainingPercent == 100, "negative clamp");
            Assert(WindowFromObject(new Dictionary<string, object> { { "usedPercent", 130 } }).RemainingPercent == 0, "over-limit clamp");
            Assert(!WindowFromObject(new Dictionary<string, object> { { "usedPercent", Double.NaN } }).RemainingPercent.HasValue, "invalid percentage");
            Assert(!WindowFromObject(new Dictionary<string, object> { { "resetsAt", Double.MaxValue } }).ResetsAtUtc.HasValue, "invalid timestamp");
            Assert(WindowLabel(60) == "1小时" && WindowLabel(15) == "15分钟" && WindowLabel(2880) == "2天", "generic labels");

            using (QuotaService service = new QuotaService())
            {
                service._selectedLimitId = "codex";
                service._lastBucket = bucket;
                service._acceptQuotaNotifications = true;
                service.Publish(snapshot);
                QuotaSnapshot previous = service.Current;
                int events = 0;
                service.Changed += delegate(QuotaSnapshot updated)
                {
                    events++;
                    Assert(updated.Windows[0].RemainingPercent == 60, "notification snapshot");
                    updated.Windows[0].RemainingPercent = 1;
                };
                service.HandleNotification(AsObject(json.DeserializeObject(
                    "{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"secondary\":{\"usedPercent\":40,\"windowDurationMins\":10080}}}}")));
                Assert(events == 1 && service.Current.Windows[0].RemainingPercent == 60, "subscriber isolation");
                Assert(previous.Windows[0].RemainingPercent == 67 && service.Current.Windows[1].RemainingPercent == 80, "new snapshot partial merge");
                service.HandleNotification(AsObject(json.DeserializeObject(
                    "{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"limitId\":\"other\",\"primary\":{\"usedPercent\":90}}}}")));
                Assert(events == 1, "unrelated bucket notification");
                DateTime resetAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                Assert(!service.ConsumeDueReset(resetAt.AddTicks(-1)), "no early reset");
                Assert(service.ConsumeDueReset(resetAt) && !service.ConsumeDueReset(resetAt), "exact reset once per timestamp");
            }

            using (QuotaService service = new QuotaService())
            {
                service._selectedLimitId = "codex";
                service._lastBucket = bucket;
                service._acceptQuotaNotifications = true;
                service.Publish(snapshot);
                service.HandleNotification(AsObject(json.DeserializeObject(
                    "{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":25}}}}")));
                Assert(service.Current.Windows[1].RemainingPercent == 75 && service.Current.Windows[1].WindowMinutes == 300 &&
                    service.Current.Windows[1].ResetsAtUtc == snapshot.Windows[1].ResetsAtUtc, "nested partial window merge");
                service.HandleNotification(AsObject(json.DeserializeObject(
                    "{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"secondary\":null}}}")));
                Assert(service.Current.Windows.Count == 1 && service.Current.Windows[0].RemainingPercent == 75, "explicit null removes window");

                DateTime? successAt = service.Current.LastSuccessUtc;
                service.RecordFailure("error", "额度读取超时");
                Assert(service.Current.State == "error" && service.Current.LastSuccessUtc == successAt &&
                    service.Current.Windows[0].RemainingPercent == 75, "network failure preserves last good data");
                double seconds = (service._nextAttemptUtc - DateTime.UtcNow).TotalSeconds;
                Assert(seconds > 28 && seconds <= 30, "first failure backoff");
                service.RecordFailure("error", "额度读取暂时失败");
                seconds = (service._nextAttemptUtc - DateTime.UtcNow).TotalSeconds;
                Assert(seconds > 58 && seconds <= 60, "second failure backoff");
                service.RecordFailure("error", "额度读取暂时失败");
                seconds = (service._nextAttemptUtc - DateTime.UtcNow).TotalSeconds;
                Assert(seconds > 118 && seconds <= 120, "third failure backoff");
                service.RecordFailure("error", "额度读取暂时失败");
                seconds = (service._nextAttemptUtc - DateTime.UtcNow).TotalSeconds;
                Assert(seconds > 298 && seconds <= 300, "capped failure backoff");

                SourceFailure expired = service.ErrorFromResponse(new Dictionary<string, object>
                    { { "code", 401 }, { "message", "token has expired: example-private-value" } });
                Assert(expired.State == "login_required" && expired.Message.IndexOf("private", StringComparison.Ordinal) < 0, "auth error is classified and sanitized");
                service.RecordFailure(expired.State, expired.Message);
                Assert(service.Current.State == "login_required" && service.Current.Windows.Count == 0 &&
                    !service.Current.LastSuccessUtc.HasValue && service._lastBucket == null, "auth failure clears old quota");
                service.HandleNotification(AsObject(json.DeserializeObject(
                    "{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":1}}}}")));
                Assert(service.Current.State == "login_required" && service.Current.Windows.Count == 0, "ignore notifications while unauthenticated");
            }

            using (QuotaService service = new QuotaService())
            {
                Dictionary<string, object> accountA = AsObject(json.DeserializeObject(
                    "{\"account\":{\"type\":\"chatgpt\",\"email\":\"a@example.invalid\",\"planType\":\"pro\"}}"));
                Dictionary<string, object> accountB = AsObject(json.DeserializeObject(
                    "{\"account\":{\"type\":\"chatgpt\",\"email\":\"b@example.invalid\",\"planType\":\"pro\"}}"));
                service.ApplyAccount(accountA);
                service._selectedLimitId = "codex";
                service._lastBucket = bucket;
                service._acceptQuotaNotifications = true;
                service.Publish(snapshot);
                service.ApplyAccount(accountA);
                Assert(service.Current.Windows.Count == 2, "unchanged account preserves cached quota");
                service.ApplyAccount(accountB);
                Assert(service.Current.State == "loading" && service.Current.Windows.Count == 0 && service._lastBucket == null &&
                    !service._acceptQuotaNotifications, "account switch clears every old bucket");
                service.HandleNotification(AsObject(json.DeserializeObject(
                    "{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":1}}}}")));
                Assert(service.Current.Windows.Count == 0, "new account needs a complete snapshot");
                int oldEpoch = service._accountEpoch;
                service.HandleNotification(AsObject(json.DeserializeObject(
                    "{\"method\":\"account/updated\",\"params\":{\"authMode\":null,\"planType\":null}}")));
                Assert(service._accountEpoch != oldEpoch && service.Current.State == "login_required" &&
                    service.Current.Windows.Count == 0, "logout invalidates in-flight account epoch immediately");
                service.HandleNotification(AsObject(json.DeserializeObject(
                    "{\"method\":\"account/updated\",\"params\":{\"authMode\":\"apikey\"}}")));
                Assert(service.Current.State == "login_required" && service.Current.Error == "请使用 ChatGPT 账户登录", "unsupported login mode is explicit");
            }
        }

        private static void Assert(bool condition, string test)
        {
            if (!condition) throw new InvalidOperationException("QuotaService self-test failed: " + test);
        }
    }
}
