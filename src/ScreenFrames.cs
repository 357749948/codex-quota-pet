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
        private static string _failedArchivePath, _archiveFailureStatus;
        private static long _failedArchiveLength, _failedArchiveStamp;
        private static DateTime _archiveRetryUtc;
        private const int MaxSpriteCandidates = 4096;
        private const long MaxSpriteScanBytes = 128L * 1024 * 1024;

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
                    if (!CanRetryArchive(archive, info.Length, info.LastWriteTimeUtc.Ticks, now))
                    { Clear(_archiveFailureStatus); return false; }
                    if (!String.Equals(_archivePath, archive, StringComparison.OrdinalIgnoreCase) ||
                        _archiveLength != info.Length || _archiveStamp != info.LastWriteTimeUtc.Ticks || _sourceHash == null)
                    {
                        Clear("正在验证宠物模板");
                        _sourceHash = null;
                        try
                        {
                            byte[] content;
                            using (FileStream stream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                                content = ReadSprite(stream);
                            string hash = Sha256(content);
                            if (hash != SupportedHash)
                            {
                                RememberArchiveFailure(archive, info.Length, info.LastWriteTimeUtc.Ticks, now,
                                    "当前宠物素材尚未适配，请等待兼容性更新");
                                return false;
                            }
                            _sourceHash = hash;
                        }
                        catch (Exception)
                        {
                            RememberArchiveFailure(archive, info.Length, info.LastWriteTimeUtc.Ticks, now,
                                "宠物素材定位失败：请更新兼容性支持");
                            return false;
                        }
                        _archivePath = archive;
                        _archiveLength = info.Length;
                        _archiveStamp = info.LastWriteTimeUtc.Ticks;
                        _failedArchivePath = null;
                        _archiveFailureStatus = null;
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

        private static bool CanRetryArchive(string path, long length, long stamp, DateTime now)
        {
            return !String.Equals(_failedArchivePath, path, StringComparison.OrdinalIgnoreCase) ||
                _failedArchiveLength != length || _failedArchiveStamp != stamp || now >= _archiveRetryUtc;
        }

        private static void RememberArchiveFailure(string path, long length, long stamp, DateTime now, string status)
        {
            _failedArchivePath = path; _failedArchiveLength = length; _failedArchiveStamp = stamp;
            _archiveRetryUtc = now.AddSeconds(30); _archiveFailureStatus = status;
            Clear(status);
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

        private static byte[] ReadSprite(Stream stream) { return ReadSprite(stream, SupportedHash); }

        // expectedHash permits original synthetic test payloads. Production
        // always supplies the unchanged supported artwork fingerprint above.
        internal static byte[] ReadSprite(Stream stream, string expectedHash)
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
                // Keep the legacy named path strict and unique. Only releases
                // without that name use bounded content-based discovery.
                if (asset != null) return ReadAsset(reader, AssetRange(asset, headerSize, stream.Length));
                var candidates = new List<AsarAssetRange>();
                long totalBytes = 0;
                foreach (var pair in files)
                {
                    if (!pair.Key.EndsWith(".webp", StringComparison.Ordinal)) continue;
                    IDictionary<string, object> candidate = Map(pair.Value);
                    if (candidate.ContainsKey("link") || candidate.ContainsKey("files") ||
                        (candidate.ContainsKey("unpacked") && !Object.Equals(candidate["unpacked"], false))) continue;
                    if (candidates.Count >= MaxSpriteCandidates) throw new InvalidDataException("Too many sprite candidates");
                    AsarAssetRange range = AssetRange(candidate, headerSize, stream.Length);
                    totalBytes += range.Length;
                    if (totalBytes > MaxSpriteScanBytes) throw new InvalidDataException("Sprite scan exceeds byte budget");
                    candidates.Add(range);
                }
                byte[] found = null;
                foreach (AsarAssetRange range in candidates)
                {
                    byte[] content = ReadAsset(reader, range);
                    if (!String.Equals(Sha256(content), expectedHash, StringComparison.Ordinal)) continue;
                    if (found != null) throw new InvalidDataException("Ambiguous sprite fingerprint");
                    found = content;
                }
                if (found == null) throw new InvalidDataException("Supported sprite fingerprint missing");
                return found;
            }
        }

        private sealed class AsarAssetRange { public long Offset; public int Length; }
        private static AsarAssetRange AssetRange(IDictionary<string, object> asset, uint headerSize, long archiveLength)
        {
            if (asset.ContainsKey("link") || asset.ContainsKey("files") ||
                (asset.ContainsKey("unpacked") && !Object.Equals(asset["unpacked"], false)))
                throw new InvalidDataException("Sprite must be a packed regular entry");
            int length = Number(asset["size"], 1, 8 * 1024 * 1024);
            long offset;
            if (!(asset["offset"] is string) || !Int64.TryParse((string)asset["offset"], NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0)
                throw new InvalidDataException("Sprite offset");
            long absolute = checked(8L + headerSize + offset);
            if (absolute > archiveLength - length) throw new InvalidDataException("Sprite exceeds archive bounds");
            return new AsarAssetRange { Offset = absolute, Length = length };
        }
        private static byte[] ReadAsset(BinaryReader reader, AsarAssetRange range)
        {
            reader.BaseStream.Position = range.Offset;
            byte[] content = reader.ReadBytes(range.Length);
            if (content.Length != range.Length) throw new InvalidDataException("Truncated sprite");
            return content;
        }

        public static void RunSelfTests()
        {
            RunAsarSelfTests();
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

        private static void RunAsarSelfTests()
        {
            // Arbitrary original bytes are sufficient to test archive lookup;
            // these are not images or samples from the installed Codex artwork.
            byte[] payload = new byte[] { 13, 29, 47, 71, 101 };
            string hash = Sha256(payload);
            var files = new Dictionary<string, object> {
                { "null-signal-spritesheet-test.webp", SyntheticAsset(payload.Length, "0") }
            };
            using (MemoryStream archive = SyntheticArchive(files, payload))
                AsarAssert(Sha256(ReadSprite(archive, hash)) == hash, "legacy sprite remains readable");
            files["abcdef123456.webp"] = SyntheticAsset(payload.Length, "0");
            using (MemoryStream archive = SyntheticArchive(files, payload))
                AsarAssert(Sha256(ReadSprite(archive, hash)) == hash, "legacy name takes precedence over anonymous entries");
            files["null-signal-spritesheet-second.webp"] = SyntheticAsset(payload.Length, "0");
            ExpectAssetFailure(files, payload, hash, "Ambiguous sprite");
            files.Remove("null-signal-spritesheet-second.webp");
            files.Remove("null-signal-spritesheet-test.webp");
            using (MemoryStream archive = SyntheticArchive(files, payload))
                AsarAssert(Sha256(ReadSprite(archive, hash)) == hash, "anonymous asset matches exact supported content");
            ExpectAssetFailure(files, payload, "unmatched-synthetic-fingerprint", "Supported sprite fingerprint missing");
            files["fedcba654321.webp"] = SyntheticAsset(payload.Length, "0");
            ExpectAssetFailure(files, payload, hash, "Ambiguous sprite fingerprint");
            files.Remove("fedcba654321.webp");
            var range = (Dictionary<string, object>)files["abcdef123456.webp"];
            range["offset"] = "1";
            ExpectAssetFailure(files, payload, hash, "Sprite exceeds archive bounds");
            range["offset"] = "-1";
            ExpectAssetFailure(files, payload, hash, "Sprite offset");
            range["offset"] = "0";
            foreach (string kind in new[] { "unpacked", "link", "files" })
            {
                var skipped = SyntheticAsset(payload.Length, "0");
                skipped[kind] = kind == "unpacked" ? (object)true : kind == "link" ? (object)"another.webp" : new Dictionary<string, object>();
                files["skip-" + kind + ".webp"] = skipped;
                using (MemoryStream archive = SyntheticArchive(files, payload))
                    AsarAssert(Sha256(ReadSprite(archive, hash)) == hash, "anonymous non-regular entries are skipped");
                files["null-signal-spritesheet-test.webp"] = skipped;
                ExpectAssetFailure(files, payload, hash, "Sprite must be a packed regular entry");
                files.Remove("null-signal-spritesheet-test.webp");
            }
            // Malformed regular ranges are rejected, and a broken legacy name
            // cannot silently select a different anonymous entry.
            files["null-signal-spritesheet-test.webp"] = SyntheticAsset(payload.Length, "1");
            ExpectAssetFailure(files, payload, hash, "Sprite exceeds archive bounds");

            files.Clear();
            for (int i = 0; i < MaxSpriteCandidates + 1; i++)
                files[i.ToString(CultureInfo.InvariantCulture) + ".webp"] = SyntheticAsset(1, "0");
            ExpectAssetFailure(files, new byte[] { 7 }, hash, "Too many sprite candidates");
            files.Clear();
            for (int i = 0; i < 17; i++)
                files[i.ToString(CultureInfo.InvariantCulture) + ".webp"] = SyntheticAsset(8 * 1024 * 1024, "0");
            ExpectAssetFailure(files, new byte[8 * 1024 * 1024], hash, "Sprite scan exceeds byte budget");

            string savedPath = _failedArchivePath, savedFailure = _archiveFailureStatus, savedStatus = _status;
            long savedLength = _failedArchiveLength, savedStamp = _failedArchiveStamp;
            DateTime savedRetry = _archiveRetryUtc;
            ScreenFrame[] savedFrames = All;
            try
            {
                DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                RememberArchiveFailure("synthetic-archive", 100, 200, now, "synthetic failure");
                AsarAssert(!CanRetryArchive("synthetic-archive", 100, 200, now.AddSeconds(29)), "unchanged failed scan is throttled");
                AsarAssert(CanRetryArchive("synthetic-archive", 100, 200, now.AddSeconds(30)), "failed scan retries after thirty seconds");
                AsarAssert(CanRetryArchive("changed-archive", 100, 200, now) &&
                    CanRetryArchive("synthetic-archive", 101, 200, now) &&
                    CanRetryArchive("synthetic-archive", 100, 201, now), "changed archive retries without failure delay");
            }
            finally
            {
                _failedArchivePath = savedPath; _archiveFailureStatus = savedFailure;
                _failedArchiveLength = savedLength; _failedArchiveStamp = savedStamp;
                _archiveRetryUtc = savedRetry; _status = savedStatus; All = savedFrames;
            }
        }

        private static Dictionary<string, object> SyntheticAsset(int size, string offset)
        {
            return new Dictionary<string, object> { { "size", size }, { "offset", offset } };
        }
        private static MemoryStream SyntheticArchive(IDictionary<string, object> files, byte[] payload)
        {
            object header = new Dictionary<string, object> { { "files", new Dictionary<string, object> {
                { "webview", new Dictionary<string, object> { { "files", new Dictionary<string, object> {
                    { "assets", new Dictionary<string, object> { { "files", files } } }
                } } } }
            } } };
            byte[] json = Encoding.UTF8.GetBytes(Serializer().Serialize(header));
            int padding = (4 - json.Length % 4) % 4;
            uint headerSize = (uint)(8 + json.Length + padding);
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(4U); writer.Write(headerSize); writer.Write(headerSize - 4); writer.Write((uint)json.Length);
                writer.Write(json); writer.Write(new byte[padding]); writer.Write(payload);
            }
            stream.Position = 0;
            return stream;
        }
        private static void ExpectAssetFailure(IDictionary<string, object> files, byte[] payload, string hash, string expected)
        {
            using (MemoryStream archive = SyntheticArchive(files, payload))
            {
                try { ReadSprite(archive, hash); }
                catch (InvalidDataException error)
                {
                    if (error.Message == expected) return;
                    throw new InvalidOperationException("Unexpected ASAR validation failure: " + error.Message);
                }
            }
            throw new InvalidOperationException("Invalid ASAR accepted: " + expected);
        }
        private static void AsarAssert(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("ASAR self-test: " + name);
        }

        private static void ExpectInvalid(Action action)
        {
            bool failed = false;
            try { action(); } catch (Exception) { failed = true; }
            if (!failed) throw new InvalidOperationException("Invalid template was accepted");
        }
    }
}
