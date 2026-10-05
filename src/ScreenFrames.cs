using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexQuotaPet
{
    public sealed class ScreenFrame
    {
        public int Index;
        public bool Supported;
        public ScreenSample[] Samples;
        public ScreenRun[] Mask;
        public double TextLeft, TextTop, TextWidth, TextHeight;
        public double Angle;
        public int BackgroundR, BackgroundG, BackgroundB;
    }
    public sealed class ScreenSample { public int X, Y, R, G, B; }
    public sealed class ScreenRun { public int Y, X, Width; }

    // The public source and executable contain no sampled sprite data. Templates
    // are generated from the user's installed asset, then verified against the
    // asset of the particular Codex process whose pet is being observed.
    public static class ScreenFrames
    {
        public const int FrameWidth = 192, FrameHeight = 208;
        private const string GeneratorVersion = "1.0.0";
        private const string SupportedHash = "a816f7488c187ffe8b7f5d58319deb6cfa591f98c219ef06cbaf10d1f9f330db";
        private const int MaxJsonBytes = 4 * 1024 * 1024;
        private static readonly object Gate = new object();
        public static volatile ScreenFrame[] All = new ScreenFrame[0];
        private static volatile string _status = "模板未初始化：请运行 prepare-templates.ps1";
        private static int _lastProcessId;
        private static DateTime _nextCheckUtc;
        private static string _archivePath, _sourceHash;
        private static long _archiveLength, _archiveStamp, _cacheLength, _cacheStamp;

        public static string TemplateStatus { get { return _status; } }
        public static bool IsReady { get { return All.Length != 0; } }
        public static string CacheDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaPetData", "cache"); }
        }

        // Background-thread API. Metadata checks are throttled; image bytes are
        // hashed only when the observed install changes. This never runs on WPF.
        public static bool ReloadForProcess(int processId)
        {
            lock (Gate)
            {
                DateTime now = DateTime.UtcNow;
                if (_lastProcessId == processId && now < _nextCheckUtc) return IsReady;
                _nextCheckUtc = now.AddSeconds(2);
                bool changedProcess = _lastProcessId != processId;
                _lastProcessId = processId;
                if (changedProcess) Clear("正在验证宠物模板");
                try
                {
                    string executable;
                    using (Process process = Process.GetProcessById(processId)) executable = process.MainModule.FileName;
                    string archive = Path.Combine(Path.GetDirectoryName(executable), "resources", "app.asar");
                    FileInfo info = new FileInfo(archive);
                    if (!info.Exists) { Clear("找不到当前 Codex 的宠物素材"); return false; }
                    if (!String.Equals(_archivePath, archive, StringComparison.OrdinalIgnoreCase) ||
                        _archiveLength != info.Length || _archiveStamp != info.LastWriteTimeUtc.Ticks || _sourceHash == null)
                    {
                        Clear("正在验证宠物模板");
                        byte[] content;
                        using (FileStream stream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            content = ReadSprite(stream);
                        _sourceHash = Sha256(content);
                        _archivePath = archive;
                        _archiveLength = info.Length;
                        _archiveStamp = info.LastWriteTimeUtc.Ticks;
                    }
                    if (_sourceHash != SupportedHash) { Clear("当前宠物素材尚未适配，请等待兼容性更新"); return false; }
                    string cache = Path.Combine(CacheDirectory, "templates-" + _sourceHash + ".json");
                    FileInfo cacheInfo = new FileInfo(cache);
                    if (!cacheInfo.Exists) { Clear("模板缺失：请运行 prepare-templates.ps1"); return false; }
                    if (IsReady && _cacheLength == cacheInfo.Length && _cacheStamp == cacheInfo.LastWriteTimeUtc.Ticks) return true;
                    if (cacheInfo.Length <= 0 || cacheInfo.Length > MaxJsonBytes) throw new InvalidDataException("Template size");
                    string json;
                    using (FileStream stream = new FileStream(cache, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                    {
                        if (stream.Length > MaxJsonBytes) throw new InvalidDataException("Template size");
                        json = reader.ReadToEnd();
                    }
                    ScreenFrame[] frames = ParseDocument(json, _sourceHash);
                    int supported = 0;
                    foreach (ScreenFrame frame in frames) if (frame.Supported) supported++;
                    if (frames.Length != 74 || supported != 46) throw new InvalidDataException("Incomplete template");
                    All = frames;
                    _cacheLength = cacheInfo.Length;
                    _cacheStamp = cacheInfo.LastWriteTimeUtc.Ticks;
                    _status = "模板就绪";
                    return true;
                }
                catch (Exception)
                {
                    Clear("模板不可用或损坏：请重新运行 prepare-templates.ps1");
                    return false;
                }
            }
        }

        private static void Clear(string status) { All = new ScreenFrame[0]; _status = status; }
        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 32 };
        }
        private static IDictionary<string, object> Map(object value)
        {
            var result = value as IDictionary<string, object>;
            if (result == null) throw new InvalidDataException("Expected object");
            return result;
        }
        private static object[] ArrayValue(object value)
        {
            var result = value as object[];
            if (result == null) throw new InvalidDataException("Expected array");
            return result;
        }
        private static int Number(object value, int minimum, int maximum)
        {
            if (!(value is int)) throw new InvalidDataException("Expected integer");
            int result = (int)value;
            if (result < minimum || result > maximum) throw new InvalidDataException("Integer outside bounds");
            return result;
        }

        internal static ScreenFrame[] ParseDocument(string json, string expectedHash)
        {
            if (json == null || json.Length > MaxJsonBytes) throw new InvalidDataException("Template size");
            var document = Map(Serializer().DeserializeObject(json));
            if (Number(document["schemaVersion"], 1, 1) != 1 ||
                !Object.Equals(document["generatorVersion"], GeneratorVersion) ||
                !Object.Equals(document["spriteSha256"], expectedHash) ||
                Number(document["frameWidth"], FrameWidth, FrameWidth) != FrameWidth ||
                Number(document["frameHeight"], FrameHeight, FrameHeight) != FrameHeight)
                throw new InvalidDataException("Template version or fingerprint mismatch");
            object[] values = ArrayValue(document["frames"]);
            if (values.Length < 1 || values.Length > 88) throw new InvalidDataException("Frame count");
            var frames = new List<ScreenFrame>();
            var indices = new HashSet<int>();
            foreach (object value in values)
            {
                var raw = Map(value);
                var frame = new ScreenFrame { Index = Number(raw["index"], 0, 87) };
                if (!indices.Add(frame.Index) || !(raw["supported"] is bool)) throw new InvalidDataException("Frame identity");
                frame.Supported = (bool)raw["supported"];
                object[] samples = ArrayValue(raw["samples"]), mask = ArrayValue(raw["mask"]);
                object[] text = ArrayValue(raw["textRectangle"]), background = ArrayValue(raw["background"]);
                if (samples.Length < 40 || samples.Length > 250 || mask.Length > FrameWidth * FrameHeight || text.Length != 4 || background.Length != 3)
                    throw new InvalidDataException("Frame shape");
                frame.Samples = new ScreenSample[samples.Length];
                for (int i = 0; i < samples.Length; i++)
                {
                    object[] item = ArrayValue(samples[i]);
                    if (item.Length != 5) throw new InvalidDataException("Sample shape");
                    frame.Samples[i] = new ScreenSample { X = Number(item[0], 0, FrameWidth - 1), Y = Number(item[1], 0, FrameHeight - 1),
                        R = Number(item[2], 0, 255), G = Number(item[3], 0, 255), B = Number(item[4], 0, 255) };
                }
                frame.Mask = new ScreenRun[mask.Length];
                bool[,] covered = new bool[FrameHeight, FrameWidth];
                for (int i = 0; i < mask.Length; i++)
                {
                    object[] item = ArrayValue(mask[i]);
                    if (item.Length != 3) throw new InvalidDataException("Mask shape");
                    int y = Number(item[0], 0, FrameHeight - 1), x = Number(item[1], 0, FrameWidth - 1), width = Number(item[2], 1, FrameWidth);
                    if (x + width > FrameWidth) throw new InvalidDataException("Mask outside frame");
                    frame.Mask[i] = new ScreenRun { Y = y, X = x, Width = width };
                    for (int column = x; column < x + width; column++)
                    {
                        if (covered[y, column]) throw new InvalidDataException("Overlapping mask runs");
                        covered[y, column] = true;
                    }
                }
                int left = Number(text[0], 0, FrameWidth), top = Number(text[1], 0, FrameHeight);
                int textWidth = Number(text[2], 0, FrameWidth), textHeight = Number(text[3], 0, FrameHeight);
                if (left + textWidth > FrameWidth || top + textHeight > FrameHeight) throw new InvalidDataException("Text outside frame");
                if (frame.Supported)
                {
                    if (textWidth < 28 || textHeight < 14 || mask.Length == 0) throw new InvalidDataException("Unreadable text bounds");
                    for (int y = top; y < top + textHeight; y++)
                        for (int x = left; x < left + textWidth; x++)
                            if (!covered[y, x]) throw new InvalidDataException("Text outside screen mask");
                }
                else if (mask.Length != 0 || left != 0 || top != 0 || textWidth != 0 || textHeight != 0)
                    throw new InvalidDataException("Unsupported frame contains overlay geometry");
                frame.TextLeft = left; frame.TextTop = top; frame.TextWidth = textWidth; frame.TextHeight = textHeight;
                frame.BackgroundR = Number(background[0], 0, 255); frame.BackgroundG = Number(background[1], 0, 255); frame.BackgroundB = Number(background[2], 0, 255);
                frames.Add(frame);
            }
            return frames.ToArray();
        }

        private static byte[] ReadSprite(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                uint sizePayload = reader.ReadUInt32(), headerSize = reader.ReadUInt32(), pickleSize = reader.ReadUInt32(), jsonSize = reader.ReadUInt32();
                if (sizePayload != 4 || headerSize < 8 || headerSize > 16 * 1024 * 1024 || pickleSize + 4 != headerSize || jsonSize == 0 || jsonSize > headerSize - 8)
                    throw new InvalidDataException("ASAR header");
                byte[] bytes = reader.ReadBytes((int)jsonSize);
                if (bytes.Length != jsonSize) throw new InvalidDataException("Truncated ASAR header");
                var header = Map(Serializer().DeserializeObject(Encoding.UTF8.GetString(bytes)));
                var webview = Map(Map(header["files"])["webview"]);
                var assets = Map(Map(webview["files"])["assets"]);
                var files = Map(assets["files"]);
                IDictionary<string, object> asset = null;
                foreach (var pair in files)
                {
                    if (!pair.Key.StartsWith("null-signal-spritesheet-", StringComparison.Ordinal) || !pair.Key.EndsWith(".webp", StringComparison.Ordinal)) continue;
                    if (asset != null) throw new InvalidDataException("Ambiguous sprite");
                    asset = Map(pair.Value);
                }
                if (asset == null || asset.ContainsKey("link") || (asset.ContainsKey("unpacked") && !Object.Equals(asset["unpacked"], false)))
                    throw new InvalidDataException("Packed sprite missing");
                int length = Number(asset["size"], 1, 8 * 1024 * 1024);
                long offset;
                if (!(asset["offset"] is string) || !Int64.TryParse((string)asset["offset"], NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0)
                    throw new InvalidDataException("Sprite offset");
                long absolute = checked(8L + headerSize + offset);
                if (absolute > stream.Length - length) throw new InvalidDataException("Sprite exceeds archive bounds");
                stream.Position = absolute;
                byte[] content = reader.ReadBytes(length);
                if (content.Length != length) throw new InvalidDataException("Truncated sprite");
                return content;
            }
        }

        public static void RunSelfTests()
        {
            // These fixtures are hand-built rectangles and color ramps, independent
            // of the installed app, local cache, account, and third-party artwork.
            var samples = new List<int[]>();
            for (int i = 0; i < 40; i++) samples.Add(new int[] { 10 + i, 20, 40 + i, 50 + i, 60 + i });
            var mask = new List<int[]>();
            for (int y = 80; y < 110; y++) mask.Add(new int[] { y, 60, 65 });
            var frame = new Dictionary<string, object> { {"index", 0}, {"supported", true}, {"samples", samples}, {"mask", mask},
                {"textRectangle", new int[] {65, 85, 50, 20}}, {"background", new int[] {12, 13, 14}} };
            var doc = new Dictionary<string, object> { {"schemaVersion", 1}, {"generatorVersion", GeneratorVersion}, {"spriteSha256", "synthetic"},
                {"frameWidth", FrameWidth}, {"frameHeight", FrameHeight}, {"frames", new object[] {frame}} };
            string good = Serializer().Serialize(doc);
            if (ParseDocument(good, "synthetic").Length != 1) throw new InvalidOperationException("Synthetic template roundtrip");
            ExpectInvalid(delegate { ParseDocument("{", "synthetic"); });
            ExpectInvalid(delegate { ParseDocument(good, "different"); });
            doc["schemaVersion"] = 2;
            ExpectInvalid(delegate { ParseDocument(Serializer().Serialize(doc), "synthetic"); });
            doc["schemaVersion"] = 1; doc["generatorVersion"] = "future";
            ExpectInvalid(delegate { ParseDocument(Serializer().Serialize(doc), "synthetic"); });
            doc["generatorVersion"] = GeneratorVersion; frame["textRectangle"] = new int[] {0, 0, 50, 20};
            ExpectInvalid(delegate { ParseDocument(Serializer().Serialize(doc), "synthetic"); });
            frame["textRectangle"] = new int[] {65, 85, 50, 20}; samples[0][0] = FrameWidth;
            ExpectInvalid(delegate { ParseDocument(Serializer().Serialize(doc), "synthetic"); });
            using (var empty = new MemoryStream(new byte[16])) ExpectInvalid(delegate { ReadSprite(empty); });
        }
        private static void ExpectInvalid(Action action)
        {
            bool failed = false;
            try { action(); } catch (Exception) { failed = true; }
            if (!failed) throw new InvalidOperationException("Invalid template was accepted");
        }
    }
}
