using DarkEngine3D_gl_csharp.Engine.Libs;
using ImGuiNET;
using StbImageSharp;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Asset Browser panel — browses the Artifacts directory for .glb, .png/.jpg, .raw files.
/// Supports image thumbnails, filtering, and click-to-select.
/// </summary>
public unsafe class AssetBrowserPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    private string _rootPath;
    private string _currentPath;
    private string _filter = "";
    private Vector2 _thumbnailSize = new(64, 64);
    private int _selectedIndex = -1;
    private FileInfo[] _files = [];
    private DirectoryInfo[] _dirs = [];

    private static readonly HashSet<string> ImageExts = [".png", ".jpg", ".jpeg", ".bmp"];
    private static readonly HashSet<string> ModelExts = [".glb", ".gltf"];

    // ── Thumbnail texture cache (path → OpenGL texture ID) ──
    private readonly Dictionary<string, uint> _thumbnailCache = [];

    public AssetBrowserPanel(IDEBridge bridge)
    {
        _bridge = bridge;
        if (Engine.Project.ProjectManager.IsProjectLoaded)
            _rootPath = Engine.Project.ProjectManager.ProjectRoot!;
        else
        {
            _rootPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Artifacts"));
            if (!Directory.Exists(_rootPath))
                _rootPath = AppDomain.CurrentDomain.BaseDirectory;
        }
        _currentPath = _rootPath;
        Refresh();
    }

    public void ShowInMenu() => ImGui.MenuItem("Asset Browser", null, ref _visible);

    /// <summary>
    /// Update root path when project is loaded/closed.
    /// Navigates to the project's Assets folder if available.
    /// </summary>
    public void SetProjectRoot(string? projectRoot)
    {
        if (!string.IsNullOrEmpty(projectRoot) && Directory.Exists(projectRoot))
        {
            _rootPath = projectRoot;
            // Navigate to Assets folder if it exists, otherwise project root
            string assetsDir = Path.Combine(projectRoot, "Artifacts");
            _currentPath = Directory.Exists(assetsDir) ? assetsDir : projectRoot;
        }
        else
        {
            // No project loaded — fallback to Artifacts or exe directory
            _rootPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Artifacts"));
            if (!Directory.Exists(_rootPath))
                _rootPath = AppDomain.CurrentDomain.BaseDirectory;
            _currentPath = _rootPath;
        }
        ClearThumbnailCache();
        Refresh();
        Console.WriteLine($"[AssetBrowser] Root: {_rootPath}, Current: {_currentPath}");
    }

    /// <summary>Clear thumbnail textures when navigating to a new directory.</summary>
    private void ClearThumbnailCache()
    {
        foreach (var kvp in _thumbnailCache)
        {
            uint tex = kvp.Value;
            if (tex != 0)
                GL.DeleteTextures(1, &tex);
        }
        _thumbnailCache.Clear();
    }

    private void Refresh()
    {
        // Clear thumbnail cache when entering a new directory
        ClearThumbnailCache();

        try
        {
            var di = new DirectoryInfo(_currentPath);
            _dirs = di.GetDirectories().OrderBy(d => d.Name).ToArray();
            _files = di.GetFiles().Where(f =>
            {
                var ext = f.Extension.ToLower();
                return ModelExts.Contains(ext) || ImageExts.Contains(ext) || ext == ".raw" || ext == ".ttf" || ext == ".glsl";
            }).OrderBy(f => f.Name).ToArray();
        }
        catch
        {
            _dirs = [];
            _files = [];
        }
    }

    /// <summary>Load an image file as a cached OpenGL texture for thumbnail display.</summary>
    private uint LoadThumbnailTexture(string filePath)
    {
        if (_thumbnailCache.TryGetValue(filePath, out uint cached))
            return cached;

        if (!File.Exists(filePath))
            return 0;

        try
        {
            uint texID;
            GL.GenTextures(1, &texID);
            GL.BindTexture(Const.GL_TEXTURE_2D, texID);

            using var stream = File.OpenRead(filePath);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            fixed (byte* ptr = image.Data)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                              image.Width, image.Height, 0,
                              Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
            }

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR_MIPMAP_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.GenerateMipmap(Const.GL_TEXTURE_2D);

            _thumbnailCache[filePath] = texID;
            return texID;
        }
        catch
        {
            return 0;
        }
    }

    public void Render()
    {
        // A single click on a clip box is delayed past the double-click window (so a
        // double-click opens the editor instead of ALSO spawning a Sprite2D); flush it
        // even when the panel is closed so a queued placement is never lost.
        FlushPendingPlacement();

        if (!_visible) return;

        ImGui.Begin("Asset Browser", ref _visible);
        IDE.PanelFocus.Notify("Asset Browser");

        // ── Sprite Clips: animation boxes built from the Sprite Editor registry.
        // Shown ONLY while browsing the project's Artifacts/Sprites folder. Each clip is
        // a drag source — drop it on the viewport to place a Sprite2D (decorative
        // animated sprite: no controller, no physics, no camera attachment), or onto
        // a selected Player2D/Sprite2D to assign that clip to it.
        bool isSpritesFolder = _rootPath != null &&
            string.Equals(
                Path.GetFullPath(_currentPath),
                Path.GetFullPath(Path.Combine(_rootPath, "Artifacts", "Sprites")),
                StringComparison.OrdinalIgnoreCase);
        if (isSpritesFolder && IDEBridge.SpriteClipCount > 0)
            RenderSpriteClipBoxes();


        // ── Breadcrumb navigation ──
        if (_currentPath != _rootPath)
        {
            if (ImGui.Button("<- Back"))
            {
                var parentDir = Directory.GetParent(_currentPath);
                if (parentDir != null)
                    _currentPath = parentDir.FullName;
                Refresh();
            }
            ImGui.SameLine();
        }
        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), _currentPath);

        // ── Filter ──
        ImGui.InputText("Filter", ref _filter, 128);

        // ── Thumbnail size slider ──
        int thumbSize = (int)_thumbnailSize.X;
        ImGui.SliderInt("Thumb Size", ref thumbSize, 32, 128);
        _thumbnailSize = new Vector2(thumbSize, thumbSize);

        ImGui.Separator();

        // ── Grid of items ──
        float panelW = ImGui.GetContentRegionAvail().X;
        int cols = Math.Max(1, (int)(panelW / (_thumbnailSize.X + 16)));

        int idx = 0;
        if (ImGui.BeginTable("AssetGrid", cols, ImGuiTableFlags.SizingFixedFit))
        {
            // Directories
            foreach (var dir in _dirs)
            {
                if (!string.IsNullOrEmpty(_filter) && !dir.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                ImGui.TableNextColumn();
                DrawItem(dir.Name, true, idx);
                idx++;
            }

            // Files
            foreach (var file in _files)
            {
                if (!string.IsNullOrEmpty(_filter) && !file.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                ImGui.TableNextColumn();
                DrawItem(file.Name, false, idx);
                idx++;
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }

    /// <summary>Payload type for sprite-clip drags. The static field carries the
    /// "Sheet/Clip" pair (payload data itself is empty — same pattern as ASSET_IMAGE_PATH).</summary>
    public const string SpriteClipPayload = "SPRITE_CLIP_REF";
    public static string? _dragSpriteClip = null;

    /// <summary>Deferred single-click placement on a clip box: (sheet, clip, click time).
    /// ImGui reports a double click as press1 → release1 → press2, and a plain Button
    /// fires on release1, so placing immediately would spawn a Sprite2D on the first half
    /// of an edit gesture. The click is therefore held until the double-click window
    /// expires (press2 cancels it).</summary>
    private static (string Sheet, string Clip, double At)? _pendingPlace;

    private static void FlushPendingPlacement()
    {
        if (_pendingPlace is not { } pending) return;
        double window = ImGui.GetIO().MouseDoubleClickTime;
        if (ImGui.GetTime() - pending.At < window) return;
        _pendingPlace = null;
        IDEBridge.RequestSprite2DPlacement?.Invoke(pending.Sheet, pending.Clip, null);
    }

    /// <summary>Render the Sprite Editor's clips as DRAGGABLE GROUP ROWS in the
    /// Artifacts/Sprites folder. A GROUP is the placeable unit (`Male Skin1`,
    /// `Female Skin1`…): click or drag it and it drops with the group's DEFAULT animation
    /// (idle). The individual action clips (`-walk`, `-run`, `-attack`…) are authoring
    /// detail, so they are NOT listed as separate boxes. A clip without a group has no
    /// parent row — those still appear individually, labelled by their clip name.</summary>
    private void RenderSpriteClipBoxes()
    {
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Animation Groups (click = place, double-click = edit)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click or drag into the viewport = place a Sprite2D (animated, no controller) / assign to the selected Player2D or Sprite2D, using the GROUP's default animation (idle).\nDouble-click = open that default clip in the Animation Editor (timeline, holds, events, bake).\nGrouped like the Animation Editor tree (display only — files untouched).");

        var clips = IDEBridge.GetSpriteClipTriples().ToList();
        float boxW = 132f;
        float boxH = ImGui.GetTextLineHeightWithSpacing() + 8f;
        float avail = ImGui.GetContentRegionAvail().X;
        int cols = Math.Max(1, (int)(avail / (boxW + 6f)));

        // ONE ROW PER GROUP (the placeable unit). Only clips WITHOUT a group — which have no
        // parent row to carry them — keep their own row, labelled by their clip name.
        var rows = new List<(string Label, string Sheet, string Clip, int Count)>();
        foreach (var group in clips
                     .GroupBy(c => string.IsNullOrWhiteSpace(c.Group) ? "" : c.Group)
                     .OrderBy(g => g.Key.Length == 0 ? 1 : 0)
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key.Length > 0)
            {
                string grpSheet = group.First().Sheet;
                // DEFAULT ANIMATION = idle; fall back to the group's first clip.
                string def = IDEBridge.FindGroupClip(grpSheet, group.Key, "idle");
                if (string.IsNullOrEmpty(def)) def = group.First().Clip;
                if (def.Length == 0) continue;
                rows.Add((group.Key, grpSheet, def, group.Count()));
                continue;
            }
            foreach (var (sheetName, clipName, _) in group)
                rows.Add((clipName, sheetName, clipName, 1));
        }

        int n = 0;
        foreach (var row in rows)
        {
            if (n % cols != 0) ImGui.SameLine();
            n++;
            ImGui.PushID($"cliprow_{row.Sheet}|{row.Clip}|{row.Label}");

            // Color-code per sheet so rows from the same sheet share a hue.
            int hash = 0;
            foreach (char ch in row.Sheet) hash = hash * 31 + ch;
            var hue = (hash & 0xFF) / 255f;
            var boxCol = ImGui.ColorConvertU32ToFloat4(ImGui.ColorConvertFloat4ToU32(
                new Vector4(System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.5f + hue, 0.45f, 0.75f - hue)), 0.45f)));

            var boxMin = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("clipbox", new Vector2(boxW, boxH));
            bool boxHovered = ImGui.IsItemHovered();
            // Hand-drawn tile: a Button fires on the first release of a double click,
            // which would place a sprite while opening the editor.
            var boxDraw = ImGui.GetWindowDrawList();
            var boxMax = boxMin + new Vector2(boxW, boxH);
            boxDraw.AddRectFilled(boxMin, boxMax,
                ImGui.ColorConvertFloat4ToU32(boxHovered
                    ? new Vector4(boxCol.X, boxCol.Y, boxCol.Z, 0.7f) : boxCol), 3f);
            boxDraw.AddRect(boxMin, boxMax,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.85f, 0.95f, 0.25f)), 3f);
            var labelSize = ImGui.CalcTextSize(row.Label);
            boxDraw.PushClipRect(boxMin + new Vector2(5, 0), boxMax - new Vector2(5, 0), true);
            boxDraw.AddText(new Vector2(boxMin.X + 6, boxMin.Y + (boxH - labelSize.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.95f, 1f, 1f)), row.Label);
            boxDraw.PopClipRect();

            if (boxHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _pendingPlace = null;   // the first click must not spawn a sprite
                IDEBridge.OpenClipInAnimationEditor(row.Sheet, row.Clip);
            }
            else if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                // Click = same as drop at the camera spawn, deferred (see above).
                _pendingPlace = (row.Sheet, row.Clip, ImGui.GetTime());
            }
            if (boxHovered)
                ImGui.SetTooltip(row.Count > 1
                    ? $"{row.Sheet} / {row.Label}\n{row.Count} clips — places the DEFAULT animation ({row.Clip}).\nClick or drag into the viewport to place.\nDouble-click = edit in the Animation Editor."
                    : $"{row.Sheet} / {row.Clip}\nClick or drag into the viewport to place.\nDouble-click = edit in the Animation Editor.");

            // ── Drag source: the row carries the same (sheet, clip) a click would place ──
            if (ImGui.BeginDragDropSource())
            {
                _pendingPlace = null;   // a drag is not a click
                ImGui.SetDragDropPayload(SpriteClipPayload, nint.Zero, 0);
                _dragSpriteClip = $"{row.Sheet}|{row.Clip}";
                ImGui.Text(row.Label);
                ImGui.TextDisabled(row.Count > 1 ? $"{row.Sheet} · default animation" : row.Sheet);
                ImGui.EndDragDropSource();
            }
            ImGui.PopID();
        }
        ImGui.Separator();
    }

    private void DrawItem(string name, bool isDir, int idx)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var rectMin = cursor;
        var rectMax = cursor + _thumbnailSize;
        var isSelected = (idx == _selectedIndex);

        // ── Background ──
        drawList.AddRectFilled(rectMin, rectMax,
            isSelected ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.3f, 0.6f, 0.5f))
                       : ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.1f, 0.8f)));

        // ── Draw actual image thumbnail for image files ──
        bool drewImage = false;
        if (!isDir && IsImageFile(name))
        {
            string fullPath = Path.Combine(_currentPath, name);
            uint thumbTex = LoadThumbnailTexture(fullPath);
            if (thumbTex != 0)
            {
                // Draw the image, centered and aspect-correct within the thumbnail rect
                float thumbW = _thumbnailSize.X;
                float thumbH = _thumbnailSize.Y;
                float pad = 2f;
                float drawW = thumbW - pad * 2f;
                float drawH = thumbH - pad * 2f;

                drawList.AddImage((nint)thumbTex,
                    new Vector2(cursor.X + pad, cursor.Y + pad),
                    new Vector2(cursor.X + pad + drawW, cursor.Y + pad + drawH));

                drewImage = true;
            }
        }

        // ── Icon text (for non-image files, or as fallback) ──
        if (!drewImage)
        {
            var icon = isDir ? "[DIR]" : GetFileIcon(name);
            var iconSize = ImGui.CalcTextSize(icon);
            drawList.AddText(
                new Vector2(cursor.X + (_thumbnailSize.X - iconSize.X) * 0.5f,
                            cursor.Y + (_thumbnailSize.Y - iconSize.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.9f, 1f)),
                icon);
        }

        // ── Border ──
        drawList.AddRect(rectMin, rectMax,
            isSelected ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.6f, 1f, 1f))
                       : ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.3f, 0.5f)));

        // ── Label below thumbnail (truncate to fit thumbnail width) ──
        ImGui.SetCursorScreenPos(new Vector2(cursor.X, cursor.Y + _thumbnailSize.Y + 4));
        // Estimate max chars that fit: ~7px per char at default font, clamp to thumbnail width
        int maxChars = Math.Max(4, (int)(_thumbnailSize.X / 7f));
        var label = name.Length > maxChars ? name[..Math.Min(maxChars - 3, name.Length)] + "..." : name;
        ImGui.TextUnformatted(label);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);

        // ── Invisible button for click detection ──
        ImGui.SetCursorScreenPos(cursor);
        ImGui.InvisibleButton($"item_{idx}", _thumbnailSize + new Vector2(0, ImGui.GetTextLineHeightWithSpacing()));

        if (ImGui.IsItemClicked())
        {
            _selectedIndex = idx;
            OnItemClicked(name, isDir);
        }

        // ── Drag-drop source for image files ──
        if (!isDir && IsImageFile(name) && ImGui.BeginDragDropSource())
        {
            string fullPath = Path.Combine(_currentPath, name);
            ImGui.SetDragDropPayload("ASSET_IMAGE_PATH", nint.Zero, 0);
            _dragImagePath = fullPath;

            // Preview thumbnail in drag tooltip
            uint dragTex = LoadThumbnailTexture(fullPath);
            if (dragTex != 0)
            {
                ImGui.Text($"Drag: {name}");
                ImGui.Image((nint)dragTex, new Vector2(48, 48));
            }
            else
            {
                ImGui.Text($"IMG: {name}");
            }
            ImGui.EndDragDropSource();
        }

        // ── Tooltip with full name ──
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(name);
    }

    private void OnItemClicked(string name, bool isDir)
    {
        if (isDir)
        {
            _currentPath = Path.Combine(_currentPath, name);
            _selectedIndex = -1;
            Refresh();
        }
    }

    // ── Static drag-drop path (set by source, read by target) ──
    public static string? _dragImagePath = null;

    private static bool IsImageFile(string name)
    {
        var ext = Path.GetExtension(name).ToLower();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
    }

    private static string GetFileIcon(string name)
    {
        var ext = Path.GetExtension(name).ToLower();
        return ext switch
        {
            ".glb" or ".gltf" => "3D",
            ".png" or ".jpg" or ".jpeg" or ".bmp" => "IMG",
            ".raw" => "MAP",
            ".ttf" => "FNT",
            ".glsl" => "SHD",
            _ => "FILE"
        };
    }
}
