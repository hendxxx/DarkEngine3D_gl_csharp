using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Libs;
using StbImageSharp;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    // Lightweight LRU for sprite atlas textures. Keeps a map of resolved image path -> AtlasEntry.
    // All GL texture creation MUST happen on the render thread; ProcessReloadQueue should be
    // called every frame from the render loop to perform pending uploads.
    public static class SpriteAtlasManager
    {
        public class AtlasEntry
        {
            public string Path = "";
            public uint TextureId = 0;
            public DateTime LastUsed = DateTime.UtcNow;
            public int RefCount = 0;
            public bool CpuLoaded = false;
            public byte[]? PixelData = null; // RGBA8
            public int Width = 0; public int Height = 0; public int Channels = 4;
        }

        private static readonly Dictionary<string, AtlasEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> _lru = new();
        private static readonly ConcurrentQueue<string> _reloadQueue = new();

        // Config
        public static int MaxAtlases = 128;
        public static float UnloadAgeSeconds = 10f; // when to evict unused atlas
        public static float IdlePauseSeconds = 2f;  // pause animation updates after this

        // Telemetry
        public static int SkippedSpriteUpdates = 0;
        public static int EvictedAtlases = 0;
        public static int ReloadsQueued = 0;

        // Request a texture; if loaded returns TextureId, otherwise enqueue load and return 0.
        public static uint GetOrQueueTexture(string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath)) return 0;
            lock (_entries)
            {
                if (_entries.TryGetValue(resolvedPath, out var e))
                {
                    e.LastUsed = DateTime.UtcNow;
                    // move to front
                    _lru.Remove(resolvedPath);
                    _lru.AddFirst(resolvedPath);
                    return e.TextureId;
                }
                // Not present -> create entry and enqueue reload
                var ne = new AtlasEntry { Path = resolvedPath };
                _entries[resolvedPath] = ne;
                _lru.AddFirst(resolvedPath);
            }
            _reloadQueue.Enqueue(resolvedPath);
            System.Threading.Interlocked.Increment(ref ReloadsQueued);
            return 0;
        }

        public static void Retain(string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath)) return;
            lock (_entries)
            {
                if (_entries.TryGetValue(resolvedPath, out var e)) { e.RefCount++; e.LastUsed = DateTime.UtcNow; _lru.Remove(resolvedPath); _lru.AddFirst(resolvedPath); }
                else { var ne = new AtlasEntry { Path = resolvedPath, RefCount = 1 }; _entries[resolvedPath] = ne; _lru.AddFirst(resolvedPath); _reloadQueue.Enqueue(resolvedPath); System.Threading.Interlocked.Increment(ref ReloadsQueued); }
            }
        }

        public static void Release(string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath)) return;
            lock (_entries)
            {
                if (_entries.TryGetValue(resolvedPath, out var e))
                {
                    e.RefCount = Math.Max(0, e.RefCount - 1);
                    e.LastUsed = DateTime.UtcNow;
                }
            }
        }

        // Called on render thread to process loads (GL uploads) and evictions
        public static unsafe void ProcessReloadQueue()
        {
            // First handle queued reloads
            while (_reloadQueue.TryDequeue(out var path))
            {
                try
                {
                    AtlasEntry? e = null;
                    lock (_entries) if (_entries.TryGetValue(path, out e)) { }
                    if (e == null) continue;

                    // If already has texture, skip
                    if (e.TextureId != 0) continue;

                    // Try load image bytes using StbImageSharp
                    if (!File.Exists(path)) continue;
                    using var fs = File.OpenRead(path);
                    var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
                    e.Width = img.Width; e.Height = img.Height; e.Channels = 4;
                    e.PixelData = img.Data;
                    e.CpuLoaded = true;

                    // Create GL texture (must be on render thread)
                    uint tex = 0;
                    try
                    {
                        // Use existing GL pattern: GenTextures with pointer, then upload with a fixed pointer
                        GL.GenTextures(1, &tex);
                        GL.BindTexture(Const.GL_TEXTURE_2D, tex);
                        fixed (byte* p = e.PixelData)
                        {
                            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, e.Width, e.Height, 0, (int)Const.GL_RGBA, (int)Const.GL_UNSIGNED_BYTE, p);
                        }
                        GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
                        GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
                        GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                        GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
                        GL.BindTexture(Const.GL_TEXTURE_2D, 0);
                        e.TextureId = tex;
                        e.LastUsed = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SpriteAtlasManager] GL upload failed for {path}: {ex.Message}");
                        try { if (tex != 0) { uint id = tex; GL.DeleteTextures(1, &id); } } catch { }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[SpriteAtlasManager] ProcessReloadQueue: {ex.Message}"); }
            }

            // Evict least-recently-used if above capacity or aged
            List<string> toEvict = new();
            lock (_entries)
            {
                while (_entries.Count > MaxAtlases && _lru.Count > 0)
                {
                    var last = _lru.Last!.Value;
                    if (string.IsNullOrEmpty(last)) break;
                    if (_entries.TryGetValue(last, out var e))
                    {
                        if (e.RefCount == 0)
                        {
                            toEvict.Add(last);
                            _lru.RemoveLast();
                        }
                        else break; // stop if last is still referenced
                    }
                    else _lru.RemoveLast();
                }

                // Time-based eviction
                var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(UnloadAgeSeconds);
                foreach (var kvp in _entries)
                {
                    var p = kvp.Key; var e = kvp.Value;
                    if (e.RefCount == 0 && e.LastUsed < cutoff)
                        toEvict.Add(p);
                }
            }

            foreach (var p in toEvict)
            {
                lock (_entries)
                {
                    if (!_entries.TryGetValue(p, out var e)) continue;
                    // delete GL texture
                    try { if (e.TextureId != 0) { uint id = e.TextureId; GL.DeleteTextures(1, &id); } } catch { }
                    e.TextureId = 0; e.PixelData = null; e.CpuLoaded = false; e.LastUsed = DateTime.UtcNow;
                    _entries.Remove(p);
                    _lru.Remove(p);
                    System.Threading.Interlocked.Increment(ref EvictedAtlases);
                }
            }
        }

        /// <summary>Drop GPU textures AND CPU pixel caches for every entry: used after
        /// an in-place sprite rebake rewrites PNGs under the same paths. The next
        /// Acquire/GetOrQueue enqueues a fresh async load of the NEW file content and
        /// ProcessReloadQueue re-uploads on the render thread. Call this from the
        /// UI/render thread right after a bake commits — GL deletion must stay on
        /// that thread.</summary>
        public static unsafe void InvalidateAll()
        {
            lock (_entries)
            {
                foreach (var e in _entries.Values)
                {
                    if (e.TextureId != 0)
                    {
                        try { uint id = e.TextureId; GL.DeleteTextures(1, &id); } catch { }
                        e.TextureId = 0;
                    }
                    e.PixelData = null;
                    e.CpuLoaded = false;
                    // Re-enqueue for a fresh async upload of the NEW file content.
                    _reloadQueue.Enqueue(e.Path);
                    System.Threading.Interlocked.Increment(ref ReloadsQueued);
                }
            }
        }

        // Helper: resolve and acquire texture id (returns 0 if not loaded yet)
        public static uint AcquireTextureIfLoaded(string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath)) return 0;
            lock (_entries)
            {
                if (_entries.TryGetValue(resolvedPath, out var e))
                {
                    e.LastUsed = DateTime.UtcNow;
                    _lru.Remove(resolvedPath); _lru.AddFirst(resolvedPath);
                    return e.TextureId;
                }
            }
            return 0;
        }
    }
}
