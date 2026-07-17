using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Asset Browser panel — browses the Artifacts directory for .glb, .png/.jpg, .raw files.
/// Supports thumbnails, filtering, and click-to-select.
/// </summary>
public class AssetBrowserPanel
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

    public AssetBrowserPanel(IDEBridge bridge)
    {
        _bridge = bridge;
        _rootPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Artifacts"));
        if (!Directory.Exists(_rootPath))
            _rootPath = AppDomain.CurrentDomain.BaseDirectory;
        _currentPath = _rootPath;
        Refresh();
    }

    public void ShowInMenu() => ImGui.MenuItem("Asset Browser", null, ref _visible);

    private void Refresh()
    {
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

    public void Render()
    {
        if (!_visible) return;

        ImGui.Begin("Asset Browser", ref _visible);

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
        var style = ImGui.GetStyle();
        var cursor = ImGui.GetCursorScreenPos();
        var icon = isDir ? "[DIR]" : GetFileIcon(name);

        // Draw thumbnail or icon
        var drawList = ImGui.GetWindowDrawList();
        var rectMin = cursor;
        var rectMax = cursor + _thumbnailSize;
        var isSelected = (idx == _selectedIndex);

        // Background
        drawList.AddRectFilled(rectMin, rectMax,
            isSelected ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.3f, 0.6f, 0.5f))
                       : ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.1f, 0.8f)));

        // Border
        drawList.AddRect(rectMin, rectMax,
            isSelected ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.6f, 1f, 1f))
                       : ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.3f, 0.5f)));

        // Icon text centered in thumbnail
        var iconSize = ImGui.CalcTextSize(icon);
        drawList.AddText(
            new Vector2(cursor.X + (_thumbnailSize.X - iconSize.X) * 0.5f,
                        cursor.Y + (_thumbnailSize.Y - iconSize.Y) * 0.5f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.9f, 1f)),
            icon);

        // Label below thumbnail
        ImGui.SetCursorScreenPos(new Vector2(cursor.X, cursor.Y + _thumbnailSize.Y + 4));
        var label = name.Length > 16 ? name[..13] + "..." : name;
        ImGui.TextUnformatted(label);

        // Invisible button for click detection
        ImGui.SetCursorScreenPos(cursor);
        ImGui.InvisibleButton($"item_{idx}", _thumbnailSize + new Vector2(0, ImGui.GetTextLineHeightWithSpacing()));

        if (ImGui.IsItemClicked())
        {
            _selectedIndex = idx;
            OnItemClicked(name, isDir);
        }

        // Tooltip with full name
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
        // Could open model preview, etc.
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
