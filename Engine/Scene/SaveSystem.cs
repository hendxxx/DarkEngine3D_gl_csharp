using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using StbImageSharp;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>Serializable game state data.</summary>
    public class SaveData
    {
        // Player
        public float PlayerX { get; set; }
        public float PlayerY { get; set; }
        public float PlayerZ { get; set; }
        public float PlayerHeading { get; set; }
        public float PlayerHealth { get; set; }

        // Camera
        public int CameraMode { get; set; }
        public float CameraDistance { get; set; }
        public float CameraYaw { get; set; }
        public float CameraPitch { get; set; }

        // World
        public float WorldTime { get; set; }

        // Metadata
        public string SaveTime { get; set; } = "";
    }

    public struct SaveSlotInfo
    {
        public int SlotIndex;
        public bool HasData;
        public bool IsCorrupted; // true when CRC check fails
        public string SaveTime;
        public uint ThumbnailTexture;
        public bool HasThumbnail;
        public bool ThumbnailLoaded;
    }

    public static unsafe class SaveManager
    {
        public const int NumSlots = 5;
        private static readonly string SavesDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "saves");

        private static string SlotDir(int index) => Path.Combine(SavesDir, $"slot_{index}");
        private static string MetaPath(int index) => Path.Combine(SlotDir(index), "save.json");
        private static string ScreenshotPath(int index) => Path.Combine(SlotDir(index), "screenshot.png");

        /// <summary>Ensure save directories exist.</summary>
        public static void Init()
        {
            Directory.CreateDirectory(SavesDir);
            for (int i = 0; i < NumSlots; i++)
                Directory.CreateDirectory(SlotDir(i));
        }

        public static SaveSlotInfo[] GetAllSlots()
        {
            var slots = new SaveSlotInfo[NumSlots];
            for (int i = 0; i < NumSlots; i++)
                slots[i] = GetSlotInfo(i);
            return slots;
        }

        public static SaveSlotInfo GetSlotInfo(int index)
        {
            var info = new SaveSlotInfo { SlotIndex = index };
            var metaPath = MetaPath(index);
            if (File.Exists(metaPath))
            {
                try
                {
                    string json = File.ReadAllText(metaPath);
                    var data = JsonSerializer.Deserialize<SaveData>(json);
                    if (data != null)
                    {
                        // Validate CRC checksum
                        if (IsChecksumValid(metaPath, json))
                        {
                            info.HasData = true;
                            info.SaveTime = data.SaveTime;
                        }
                        else
                        {
                            info.HasData = true; // File exists but corrupted
                            info.IsCorrupted = true;
                            info.SaveTime = data.SaveTime;
                        }
                    }
                }
                catch
                {
                    // Malformed JSON = corrupted
                    info.HasData = true;
                    info.IsCorrupted = true;
                }
            }
            if (File.Exists(ScreenshotPath(index)))
                info.HasThumbnail = true;

            return info;
        }

        /// <summary>Load thumbnail texture from PNG for a slot (cached after first load).</summary>
        public static uint GetOrLoadThumbnail(ref SaveSlotInfo info, int thumbW, int thumbH)
        {
            if (info.ThumbnailLoaded) return info.ThumbnailTexture;

            var shotPath = ScreenshotPath(info.SlotIndex);
            if (!File.Exists(shotPath))
            {
                info.ThumbnailLoaded = true;
                info.ThumbnailTexture = 0;
                return 0;
            }

            try
            {
                // Load PNG via StbImageSharp (already a project dependency)
                StbImageSharp.ImageResult image;
                using (var stream = File.OpenRead(shotPath))
                {
                    image = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlue);
                }

                if (image.Data == null || image.Width <= 0 || image.Height <= 0)
                    return 0;

                int srcW = image.Width;
                int srcH = image.Height;
                byte[] srcPixels = image.Data; // RGB

                // Downsample to thumbnail size
                byte[] thumb = new byte[thumbW * thumbH * 4];
                float scaleX = (float)srcW / thumbW;
                float scaleY = (float)srcH / thumbH;

                for (int ty = 0; ty < thumbH; ty++)
                {
                    int sy = (int)(ty * scaleY);
                    if (sy >= srcH) sy = srcH - 1;

                    for (int tx = 0; tx < thumbW; tx++)
                    {
                        int sx = (int)(tx * scaleX);
                        if (sx >= srcW) sx = srcW - 1;
                        int srcIdx = (sy * srcW + sx) * 3;
                        int dstIdx = (ty * thumbW + tx) * 4;
                        thumb[dstIdx + 0] = srcPixels[srcIdx + 0];
                        thumb[dstIdx + 1] = srcPixels[srcIdx + 1];
                        thumb[dstIdx + 2] = srcPixels[srcIdx + 2];
                        thumb[dstIdx + 3] = 255;
                    }
                }

                uint tex;
                GL.GenTextures(1, &tex);
                GL.BindTexture(Const.GL_TEXTURE_2D, tex);
                fixed (byte* p = thumb)
                {
                    GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, thumbW, thumbH, 0,
                        Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, p);
                }
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);

                info.ThumbnailTexture = tex;
                info.ThumbnailLoaded = true;
                return tex;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SaveManager] Thumbnail load failed: {ex.Message}");
                info.ThumbnailLoaded = true;
                info.ThumbnailTexture = 0;
                return 0;
            }
        }

        public static SaveData? Load(int index)
        {
            var metaPath = MetaPath(index);
            if (!File.Exists(metaPath)) return null;
            try
            {
                string json = File.ReadAllText(metaPath);

                // Validate CRC checksum before deserializing
                if (!IsChecksumValid(metaPath, json))
                {
                    Console.Error.WriteLine($"[SaveManager] Slot {index}: save file is corrupted (CRC mismatch)!");
                    return null;
                }

                return JsonSerializer.Deserialize<SaveData>(json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SaveManager] Slot {index}: failed to load: {ex.Message}");
                return null;
            }
        }

        /// <summary>Check if the CRC file matches the JSON content. Missing CRC file = assumed valid (backward compat).</summary>
        private static bool IsChecksumValid(string metaPath, string json)
        {
            // CRC file is stored alongside save.json: e.g. save.json → save.json.crc
            string crcPath = metaPath + ".crc";
            if (!File.Exists(crcPath))
                return true; // No CRC file = old save format, assume valid

            try
            {
                byte[] storedCrcBytes = File.ReadAllBytes(crcPath);
                if (storedCrcBytes.Length != 4) return false;

                uint storedCrc = ((uint)storedCrcBytes[0] << 24) |
                                 ((uint)storedCrcBytes[1] << 16) |
                                 ((uint)storedCrcBytes[2] << 8) |
                                 storedCrcBytes[3];

                byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
                uint computedCrc = Crc32(jsonBytes);

                return storedCrc == computedCrc;
            }
            catch
            {
                return false;
            }
        }

        public static void Save(int index, SaveData data)
        {
            var dir = SlotDir(index);
            Directory.CreateDirectory(dir);
            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllBytes(MetaPath(index), jsonBytes);

            // Compute and store CRC32 checksum of the JSON bytes
            uint crc = Crc32(jsonBytes);
            byte[] crcBytes = [
                (byte)((crc >> 24) & 0xFF),
                (byte)((crc >> 16) & 0xFF),
                (byte)((crc >> 8) & 0xFF),
                (byte)(crc & 0xFF)
            ];
            // Atomic write: write to temp file, then rename to prevent partial CRC files
            string crcPath = MetaPath(index) + ".crc";
            string crcTemp = crcPath + ".tmp";
            File.WriteAllBytes(crcTemp, crcBytes);
            File.Move(crcTemp, crcPath, overwrite: true);

            Console.WriteLine($"[SaveManager] Saved slot {index}: {data.SaveTime} (CRC: 0x{crc:X8})");
        }

        /// <summary>Capture current framebuffer as PNG screenshot for a slot.
        /// Reads the full framebuffer at full resolution, then downscales to a
        /// thumbnail size (~320x240) to produce a proper full-scene thumbnail.</summary>
        public static void CaptureScreenshot(int index)
        {
            try
            {
                int w = Glfw.WindowWidth;
                int h = Glfw.WindowHeight;
                if (w <= 0 || h <= 0) return;

                // Target thumbnail dimensions (max 320 wide, 240 tall, keep aspect ratio)
                int thumbW = Math.Min(w, 320);
                int thumbH = h * thumbW / w;
                if (thumbH > 240) { thumbH = 240; thumbW = w * thumbH / h; }

                // Read the FULL framebuffer into a buffer
                int srcRowBytes = w * 3;
                byte[] fullFrame = new byte[w * h * 3];
                fixed (byte* p = fullFrame)
                {
                    GL.ReadPixels(0, 0, w, h, Const.GL_RGB, Const.GL_UNSIGNED_BYTE, (float*)p);
                }

                // Downscale to thumbnail size using simple point sampling
                byte[] thumb = new byte[thumbW * thumbH * 3];
                float scaleX = (float)w / thumbW;
                float scaleY = (float)h / thumbH;
                int dstRowBytes = thumbW * 3;

                for (int ty = 0; ty < thumbH; ty++)
                {
                    // Flip Y: OpenGL framebuffer is bottom-up → PNG expects top-down
                    int sy = (int)((thumbH - 1 - ty) * scaleY);
                    if (sy >= h) sy = h - 1;

                    for (int tx = 0; tx < thumbW; tx++)
                    {
                        int sx = (int)(tx * scaleX);
                        if (sx >= w) sx = w - 1;

                        int srcIdx = sy * srcRowBytes + sx * 3;
                        int dstIdx = ty * dstRowBytes + tx * 3;
                        thumb[dstIdx + 0] = fullFrame[srcIdx + 0];
                        thumb[dstIdx + 1] = fullFrame[srcIdx + 1];
                        thumb[dstIdx + 2] = fullFrame[srcIdx + 2];
                    }
                }

                // Save as PNG
                SaveAsPng(ScreenshotPath(index), thumb, thumbW, thumbH);
                Console.WriteLine($"[SaveManager] PNG screenshot saved for slot {index} ({thumbW}x{thumbH})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SaveManager] Screenshot failed: {ex.Message}");
            }
        }

        /// <summary>Save raw RGB24 or RGBA32 pixels as a PNG file using built-in DeflateStream.</summary>
        private static void SaveAsPng(string path, byte[] pixels, int width, int height, bool hasAlpha = false)
        {
            int channels = hasAlpha ? 4 : 3;
            int colorType = hasAlpha ? 6 : 2; // PNG color type: 2=RGB, 6=RGBA
            int rowSize = width * channels;

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // PNG signature
            bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            // IHDR chunk
            WriteChunk(bw, "IHDR", delegate (BinaryWriter writer)
            {
                writer.Write(BigEndian(width));
                writer.Write(BigEndian(height));
                writer.Write((byte)8);  // bit depth
                writer.Write((byte)colorType);
                writer.Write((byte)0);  // compression
                writer.Write((byte)0);  // filter
                writer.Write((byte)0);  // interlace
            });

            // IDAT chunk: filter rows (filter byte 0 = None per row) + deflate compress
            byte[] filtered = new byte[(rowSize + 1) * height];
            for (int y = 0; y < height; y++)
            {
                filtered[y * (rowSize + 1)] = 0; // filter byte = None
                Buffer.BlockCopy(pixels, y * rowSize, filtered, y * (rowSize + 1) + 1, rowSize);
            }

            byte[] compressed = DeflateCompress(filtered);
            WriteChunk(bw, "IDAT", delegate (BinaryWriter writer) { writer.Write(compressed); });

            // IEND chunk
            WriteChunk(bw, "IEND", delegate { });
        }

        /// <summary>
        /// Read an OpenGL RGBA texture and save it as a PNG file.
        /// Creates a temporary FBO to read back GPU pixels to CPU memory.
        /// </summary>
        public static void SaveTextureAsPng(string path, uint textureId, int width, int height)
        {
            if (textureId == 0 || width <= 0 || height <= 0) return;

            byte[] rgba;
            unsafe
            {
                // Create temporary FBO, attach the texture, read pixels
                uint fbo;
                GL.GenFramebuffers(1, &fbo);
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);
                GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                    Const.GL_TEXTURE_2D, textureId, 0);

                // Explicitly set read buffer to color attachment 0
                GL.ReadBuffer(Const.GL_COLOR_ATTACHMENT0);

                int status = GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
                if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                {
                    Console.WriteLine($"[SaveManager] WARN: FBO incomplete (status={status}) for texture {textureId}");
                    GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                    GL.DeleteFramebuffers(1, &fbo);
                    return;
                }

                // Save current viewport
                int origVpX, origVpY, origVpW, origVpH;
                origVpX = 0; origVpY = 0;
                origVpW = Glfw.WindowWidth;
                origVpH = Glfw.WindowHeight;

                GL.Viewport(0, 0, width, height);

                rgba = new byte[width * height * 4];
                fixed (byte* p = rgba)
                {
                    GL.ReadPixels(0, 0, width, height, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (float*)p);
                }

                // Restore viewport
                GL.Viewport(origVpX, origVpY, origVpW, origVpH);
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                GL.DeleteFramebuffers(1, &fbo);
            }

            // Flip Y: OpenGL bottom-up → PNG top-down
            int rowBytes = width * 4;
            byte[] flipped = new byte[rgba.Length];
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y) * rowBytes;
                int dstRow = y * rowBytes;
                Buffer.BlockCopy(rgba, srcRow, flipped, dstRow, rowBytes);
            }

            // Save as RGBA PNG
            SaveAsPng(path, flipped, width, height, hasAlpha: true);
            Console.WriteLine($"[SaveManager] Saved texture {textureId} as PNG: {path} ({width}x{height})");
        }

        /// <summary>Write a PNG chunk: length (big-endian) + type + data + CRC32.</summary>
        private static void WriteChunk(BinaryWriter bw, string type, Action<BinaryWriter> writeData)
        {
            // Write to a memory stream first to compute length and CRC
            using var ms = new MemoryStream();
            using var dataWriter = new BinaryWriter(ms);
            writeData(dataWriter);
            byte[] data = ms.ToArray();

            // Combine type + data for CRC
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            byte[] crcInput = new byte[typeBytes.Length + data.Length];
            Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
            Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);

            uint crc = Crc32(crcInput);

            bw.Write(BigEndian(data.Length));
            bw.Write(typeBytes);
            bw.Write(data);
            bw.Write(BigEndian(crc));
        }

        /// <summary>Compress data using Deflate (without zlib wrapper).</summary>
        private static byte[] DeflateCompress(byte[] raw)
        {
            using var ms = new MemoryStream();
            // Write zlib header: CMF=0x78 (deflate, window=32K), FLG=0x01 (no dict, check bits)
            ms.WriteByte(0x78);
            ms.WriteByte(0x01);

            using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            // Append Adler-32 checksum
            uint adler = Adler32(raw);
            ms.WriteByte((byte)((adler >> 24) & 0xFF));
            ms.WriteByte((byte)((adler >> 16) & 0xFF));
            ms.WriteByte((byte)((adler >> 8) & 0xFF));
            ms.WriteByte((byte)(adler & 0xFF));

            return ms.ToArray();
        }

        /// <summary>Compute Adler-32 checksum.</summary>
        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte byteVal in data)
            {
                a = (a + byteVal) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        /// <summary>Compute CRC-32 (used in PNG chunks).</summary>
        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            return crc ^ 0xFFFFFFFF;
        }

        /// <summary>Convert int to big-endian bytes.</summary>
        private static byte[] BigEndian(int value)
        {
            return [(byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
                    (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)];
        }

        /// <summary>Convert uint to big-endian bytes.</summary>
        private static byte[] BigEndian(uint value)
        {
            return [(byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
                    (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)];
        }

        public static bool HasAnySave()
        {
            for (int i = 0; i < NumSlots; i++)
                if (File.Exists(MetaPath(i))) return true;
            return false;
        }

        public static int GetLatestSlot()
        {
            int latest = -1;
            DateTime latestTime = DateTime.MinValue;
            for (int i = 0; i < NumSlots; i++)
            {
                var path = MetaPath(i);
                if (File.Exists(path))
                {
                    var wt = File.GetLastWriteTime(path);
                    if (wt > latestTime) { latestTime = wt; latest = i; }
                }
            }
            return latest;
        }

        public static int GetNextEmptySlot()
        {
            for (int i = 0; i < NumSlots; i++)
                if (!File.Exists(MetaPath(i))) return i;
            return 0;
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Save/load slot UI rendering helpers
    // ──────────────────────────────────────────────────────────────

    public static class SaveSlotUI
    {
        /// <summary>Cache for GetTextExtents — title text is static while panel is shown, avoids recomputation every frame.</summary>
        private static readonly Dictionary<string, HUD.TextExtents> _textExtentsCache = [];

        private static HUD.TextExtents GetCachedExtents(HUD hud, string text)
        {
            if (string.IsNullOrEmpty(text)) return new HUD.TextExtents(0f, 0f, 0f);
            if (_textExtentsCache.TryGetValue(text, out var cached))
                return cached;
            var ext = hud.GetTextExtents(text);
            _textExtentsCache[text] = ext;
            return ext;
        }
        public const int PanelColStart = 2;
        public const int PanelColEnd = 10;
        public const int SlotRowH = 60;
        public const int SlotGap = 5;
        public const int ThumbW = 90;
        public const int ThumbH = 50;

        /// <summary>Compute panel dimensions using the 12-column grid.</summary>
        public static void GetPanelRect(int w, int h, out float panelX, out float panelW, out float panelY, out float panelH, out float startY)
        {
            var grid = new GridLayout(w, h);
            panelX = grid.ColX(PanelColStart);
            panelW = grid.SpanW(PanelColStart, PanelColEnd);
            panelH = SlotRowH * SaveManager.NumSlots + SlotGap * (SaveManager.NumSlots - 1) + 100f;
            panelY = (h - panelH) * 0.5f;
            startY = panelY + 60f;
        }

        /// <summary>Render a list of save slots. Returns the hovered slot index or -1.</summary>
        public static int RenderSlots(HUD hud, int w, int h, SaveSlotInfo[] slots, int selection,
            float panelX, float panelW, float startY, string mode)
        {
            int hovered = -1;

            // Get mouse position once outside the loop
            Mouse.GetCursorPosition(out double mouseX, out double mouseY);

            for (int i = 0; i < slots.Length; i++)
            {
                float sy = startY + i * (SlotRowH + SlotGap);
                bool sel = i == selection;
            float bx = panelX + GridLayout.Gutter * 0.5f;
            float bw = panelW - GridLayout.Gutter;

                // Slot background
                Vector3 bg = sel
                    ? new Vector3(0.22f, 0.28f, 0.45f)
                    : (slots[i].HasData ? new Vector3(0.12f, 0.14f, 0.20f) : new Vector3(0.07f, 0.08f, 0.12f));
                hud.DrawBox(bx, sy, bw, SlotRowH, bg);

                // Border
                Vector3 border = sel ? new Vector3(0.5f, 0.6f, 1.0f) : new Vector3(0.15f, 0.18f, 0.25f);
                hud.DrawBox(bx, sy, bw, 1f, border);
                hud.DrawBox(bx, sy + SlotRowH - 1f, bw, 1f, border);

                // Selection side bar
                if (sel)
                    hud.DrawBox(bx - 3f, sy + 4f, 3f, SlotRowH - 8f, new Vector3(0.4f, 0.5f, 0.9f));

                // Thumbnail area (left side, fixed 140x80)
                float thumbX = bx + 10f;
                float thumbY = sy + (SlotRowH - ThumbH) * 0.5f;

                if (slots[i].HasData && slots[i].ThumbnailLoaded && slots[i].ThumbnailTexture != 0)
                {
                    hud.DrawImage(thumbX, thumbY, ThumbW, ThumbH, slots[i].ThumbnailTexture);
                }
                else if (slots[i].HasData)
                {
                    hud.DrawBox(thumbX, thumbY, ThumbW, ThumbH, new Vector3(0.10f, 0.10f, 0.15f));
                }
                else
                {
                    hud.DrawBox(thumbX, thumbY, ThumbW, ThumbH, new Vector3(0.06f, 0.06f, 0.10f));
                }

                // Slot number + status
                string slotLabel = slots[i].HasData
                    ? $"SLOT {i + 1}"
                    : $"SLOT {i + 1}  —  EMPTY";
                float labelX = thumbX + ThumbW + 14f;
                float labelY = sy + 25f;
                hud.DrawText(slotLabel, labelX, labelY,
                    slots[i].HasData ? new Vector3(0.9f, 0.9f, 1.0f) : new Vector3(0.4f, 0.4f, 0.5f));

                // Date/time
                if (slots[i].HasData && !string.IsNullOrEmpty(slots[i].SaveTime))
                {
                    float timeY = labelY + 24f;
                    hud.DrawText(slots[i].SaveTime, labelX, timeY, new Vector3(0.6f, 0.6f, 0.8f));
                }

                // Corrupted indicator
                if (slots[i].IsCorrupted)
                {
                    string warn = "⚠ CORRUPTED";
                    float warnY = labelY + 46f;
                    hud.DrawText(warn, labelX, warnY, new Vector3(1.0f, 0.3f, 0.3f));
                }

                // Hover detection
                if (mouseX >= bx && mouseX <= bx + bw && mouseY >= sy && mouseY <= sy + SlotRowH)
                    hovered = i;
            }

            return hovered;
        }

        /// <summary>Title + hint text for the save/load panel, grid-aligned.</summary>
        public static void RenderPanelFrame(HUD hud, int w, int h, string title, SaveSlotInfo[] slots,
            float panelX, float panelW, float panelY, float panelH)
        {
            var grid = new GridLayout(w, h);

            // Dim overlay
            hud.DrawBox(0, 0, w, h, new Vector3(0f, 0f, 0f) * 0.55f);

            // Panel background
            hud.DrawBox(panelX, panelY, panelW, panelH, new Vector3(0.08f, 0.09f, 0.14f));
            hud.DrawBox(panelX, panelY, panelW, 2f, new Vector3(0.4f, 0.5f, 0.9f));
            hud.DrawBox(panelX, panelY + panelH - 2f, panelW, 2f, new Vector3(0.4f, 0.5f, 0.9f) * 0.5f);

            // Title — centered using grid, matching main menu style
            float titleCenterX = grid.CenterX(PanelColStart, PanelColEnd);
            var titleExt = GetCachedExtents(hud, title);
            float titleX = titleCenterX - titleExt.Width * 0.5f;
            float titleY = panelY + 40f;
            hud.DrawText(title, titleX, titleY, new Vector3(0.9f, 0.9f, 1.0f));

            // Divider — centered under title
            float titleH = titleExt.Height;
            float divY = titleY + titleH ;
            float divW = panelW * 0.6f;
            float divX = titleCenterX - divW * 0.5f;
            hud.DrawBox(divX, divY, divW, 1f, new Vector3(0.2f, 0.22f, 0.3f));
        }
    }
}
