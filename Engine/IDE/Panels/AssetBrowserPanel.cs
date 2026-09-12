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
            string assetsDir = Path.Combine(projectRoot, "Assets");
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
        if (!_visible) return;

        ImGui.Begin("Asset Browser", ref _visible);
        IDE.PanelFocus.Notify("Asset Browser");

        // ── Breadcrumb navigation ──
        if (_currentPath != _rootPath)
        {
            if (ImGui.Button("<- Back"))
            {
                _currentPath = Directory.GetParent(_currentPath)?.FullName ?? _rootPath;
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
