using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using StbImageSharp;
using System.Numerics;
using System.IO;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Sprite Editor panel for 2D sidescroller game.
/// Handles sprite sheet slicing, frame preview, animation editing, and sprite properties.
/// </summary>
public class SpriteEditorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;
    private readonly ImGuiFileDialog _importDialog = new();
    private readonly ImGuiFileDialog _saveDialog = new();
    private readonly ImGuiFileDialog _loadSheetsDialog = new();
    private string _lastSavePath = "";
    private string _lastLoadPath = "";

    // ── Sprite sheets ──
    public List<SpriteSheet> SpriteSheets = new();
    private int _selectedSheetIdx = -1;
    private SpriteSheet? SelectedSheet => _selectedSheetIdx >= 0 && _selectedSheetIdx < SpriteSheets.Count
        ? SpriteSheets[_selectedSheetIdx] : null;

    // ── Edit state ──
    private string _newSheetName = "NewSheet";
    private int _editCols = 8;
    private int _editRows = 8;
    private int _editFrameW = 64;
    private int _editFrameH = 64;
    private int _editPadX, _editPadY;
    private int _editOffsetX, _editOffsetY;

    // ── Frame selection ──
    private int _selectedFrameIdx = -1;
    private SpriteFrame? SelectedFrame => _selectedFrameIdx >= 0 && SelectedSheet?.CustomFrames != null && _selectedFrameIdx < SelectedSheet.CustomFrames.Count
        ? SelectedSheet.CustomFrames[_selectedFrameIdx] : null;

    // ── Animation preview ──
    private bool _animPlaying;
    private float _animTime;
    private float _animFPS = 12f;
    private int _animStartFrame;
    private int _animEndFrame;
    private bool _animLoop = true;
    private int _currentPreviewFrame;
    private float _animPreviewZoom = 1f;

    // ── Clip editing preview ──
    private AnimationClip2D? _editingClip;
    private bool _clipPlaying;
    private float _clipTime;
    private int _clipCurrentFrame; // local index into FrameIndices
    private float _clipZoom = 2f;

    // ── Frame drag state ──
    private enum FrameDragMode { None, MoveHitbox, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight, Anchor }
    private FrameDragMode _frameDragMode = FrameDragMode.None;
    private bool _frameDragActive;
    private Vector2 _frameDragStartMouse;
    private Vector4 _frameDragStartHitbox; // X, Y, W, H
    private Vector2 _frameDragStartAnchor;

    // ── Animation clips ──
    public List<AnimationClip2D> AnimationClips = new();
    private int _selectedClipIdx = -1;
    private AnimationClip2D? SelectedClip => _selectedClipIdx >= 0 && _selectedClipIdx < AnimationClips.Count
        ? AnimationClips[_selectedClipIdx] : null;
    private string _newClipName = "New Clip";

    // ── Preview ──
    private float _previewZoom = 1f;
    private readonly Dictionary<string, uint> _previewTextures = new();
    private bool _showPreview = true;
    private float _previewMaxWidth = 600f;

    // ── Tool modes ──
    private enum ToolMode { Select, Slice, Anchor, Hitbox }
    private ToolMode _toolMode = ToolMode.Select;

    public SpriteEditorPanel(IDEBridge bridge)
    {
        _bridge = bridge;
    }

    public void ShowInMenu() => ImGui.MenuItem("Sprite Editor", null, ref _visible);

    /// <summary>Public one-liner for IDE in-game mode (panel Render() is skipped there):
    /// refresh the static sheet/clip registry so Player2D keeps animating in-game.</summary>
    public void SyncRegistry()
        => IDEBridge.SyncSpriteRegistry(SpriteSheets, _previewTextures, AnimationClips);

    public void Render()
    {
        // Keep the static sprite registry fresh even when the panel is hidden, so
        // Player2D objects keep resolving their sheet/clip + live texture in-game.
        SyncRegistry();

        if (!_visible) return;

        ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Sprite Editor", ref _visible))
        {
            // ── Window-level drag-drop target ──
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                if (AssetBrowserPanel._dragImagePath != null)
                {
                    string imgPath = AssetBrowserPanel._dragImagePath;
                    AssetBrowserPanel._dragImagePath = null;
                    ImportSpriteFromPath(imgPath);
                    Console.WriteLine($"[SpriteEditor] Drag-drop imported: {imgPath}");
                }
                ImGui.EndDragDropTarget();
            }

            // ── Toolbar ──
            if (ImGui.Button("Import Sheet"))
                ImportNewSheet();
            ImGui.SameLine();
            if (SelectedSheet != null && ImGui.Button("Delete Sheet"))
            {
                SpriteSheets.RemoveAt(_selectedSheetIdx);
                _selectedSheetIdx = Math.Min(_selectedFrameIdx, SpriteSheets.Count - 1);
            }
            ImGui.SameLine();
            if (ImGui.Button("Save"))
                SaveAllSheets();
            ImGui.SameLine();
            if (ImGui.Button("Load"))
                LoadAllSheetsDialog();
            ImGui.SameLine();
            ImGui.TextDisabled($"({SpriteSheets.Count} sheets, {AnimationClips.Count} clips)");

            ImGui.Separator();

            // ── Sheet list ──
            if (ImGui.CollapsingHeader("Sprite Sheets", ImGuiTreeNodeFlags.DefaultOpen))
            {
                for (int i = 0; i < SpriteSheets.Count; i++)
                {
                    bool isSelected = i == _selectedSheetIdx;
                    if (ImGui.Selectable($"{SpriteSheets[i].Name}##{i}", isSelected))
                    {
                        _selectedSheetIdx = i;
                        _selectedFrameIdx = -1;
                        LoadSheetSettings();
                    }
                }
            }

            if (SelectedSheet != null)
            {
                ImGui.Separator();

                // ── Sheet Settings ──
                if (ImGui.CollapsingHeader("Sheet Settings", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.InputText("Sheet Name##sheet", ref _newSheetName, 256);

                    bool gridChanged = false;
                    gridChanged |= ImGui.InputInt("Columns", ref _editCols);
                    gridChanged |= ImGui.InputInt("Rows", ref _editRows);
                    gridChanged |= ImGui.InputInt("Frame Width", ref _editFrameW);
                    gridChanged |= ImGui.InputInt("Frame Height", ref _editFrameH);
                    gridChanged |= ImGui.InputInt("Padding X", ref _editPadX);
                    gridChanged |= ImGui.InputInt("Padding Y", ref _editPadY);
                    gridChanged |= ImGui.InputInt("Offset X", ref _editOffsetX);
                    gridChanged |= ImGui.InputInt("Offset Y", ref _editOffsetY);

                    if (gridChanged)
                    {
                        SelectedSheet.Name = _newSheetName;
                        SelectedSheet.Columns = Math.Max(1, _editCols);
                        SelectedSheet.Rows = Math.Max(1, _editRows);
                        SelectedSheet.FrameWidth = Math.Max(1, _editFrameW);
                        SelectedSheet.FrameHeight = Math.Max(1, _editFrameH);
                        SelectedSheet.PaddingX = Math.Max(0, _editPadX);
                        SelectedSheet.PaddingY = Math.Max(0, _editPadY);
                        SelectedSheet.OffsetX = Math.Max(0, _editOffsetX);
                        SelectedSheet.OffsetY = Math.Max(0, _editOffsetY);
                        SelectedSheet.BakeUniformFrames();
                    }
                    ImGui.Text($"Frames: {SelectedSheet.FrameCount}");
                }

                ImGui.Separator();

                // ── Sprite Sheet Preview ──
                if (ImGui.CollapsingHeader("Preview", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    RenderSheetPreview();
                }

                // ── Frame Grid Preview ──
                if (ImGui.CollapsingHeader("Frame Grid", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    RenderFrameGrid();
                }

                ImGui.Separator();

                // ── Selected Frame ──
                if (SelectedFrame != null)
                {
                    if (ImGui.CollapsingHeader("Frame Properties", ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        // ── Frame thumbnail preview ──
                        RenderFrameThumbnail();

                        ImGui.InputText("Frame Name##frame", ref SelectedFrame.Name, 256);
                        ImGui.SliderFloat("Anchor X", ref SelectedFrame.AnchorX, 0f, 1f);
                        ImGui.SliderFloat("Anchor Y", ref SelectedFrame.AnchorY, 0f, 1f);
                        ImGui.Separator();
                        ImGui.Text("Hitbox (pixels):");
                        ImGui.InputInt("X", ref SelectedFrame.HitboxX);
                        ImGui.InputInt("Y", ref SelectedFrame.HitboxY);
                        ImGui.InputInt("W", ref SelectedFrame.HitboxW);
                        ImGui.InputInt("H", ref SelectedFrame.HitboxH);
                    }
                }

                ImGui.Separator();

                // ── Animation Preview ──
                if (ImGui.CollapsingHeader("Animation Preview", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    RenderAnimationPreview();
                }

                ImGui.Separator();

                // ── Animation Clips ──
                if (ImGui.CollapsingHeader("Animation Clips", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    RenderAnimationClips();
                }
            }

            // ── Drop zone indicator ──
            if (SelectedSheet == null)
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.5f, 0.7f, 1f, 0.8f), "Drop sprite sheet image here");
            }

            // ── Bottom drop target (fallback) ──
            var dropCursor = ImGui.GetCursorScreenPos();
            var availSize = ImGui.GetContentRegionAvail();
            if (availSize.Y > 0)
            {
                ImGui.InvisibleButton("##drop_target", new Vector2(Math.Max(availSize.X, 100f), Math.Max(availSize.Y, 20f)));
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (AssetBrowserPanel._dragImagePath != null)
                    {
                        string imgPath = AssetBrowserPanel._dragImagePath;
                        AssetBrowserPanel._dragImagePath = null;
                        ImportSpriteFromPath(imgPath);
                        Console.WriteLine($"[SpriteEditor] Drag-drop imported: {imgPath}");
                    }
                    ImGui.EndDragDropTarget();
                }
            }
        }
        ImGui.End();

        // Render file dialogs outside the panel window
        _importDialog.Render();
        ProcessImportResult();
        _saveDialog.Render();
        ProcessSaveResult();
        _loadSheetsDialog.Render();
        ProcessLoadSheetsResult();
    }

    private void RenderSheetPreview()
    {
        if (SelectedSheet == null) return;

        ImGui.Checkbox("Show Preview", ref _showPreview);
        ImGui.SameLine();
        ImGui.PushItemWidth(150);
        ImGui.SliderFloat("Zoom##preview", ref _previewZoom, 1f, 8f, "%.1fx");
        ImGui.PopItemWidth();

        if (!_showPreview) return;

        if (string.IsNullOrEmpty(SelectedSheet.ImagePath) || !System.IO.File.Exists(SelectedSheet.ImagePath))
        {
            ImGui.TextDisabled("No image loaded");
            return;
        }

        // Load texture if not cached
        LoadPreviewTexture(SelectedSheet.ImagePath);

        if (!_previewTextures.TryGetValue(SelectedSheet.ImagePath, out uint texId) || texId == 0)
        {
            ImGui.TextDisabled("Failed to load image");
            return;
        }

        // Calculate preview size with zoom
        float imgW = SelectedSheet.ImageWidth;
        float imgH = SelectedSheet.ImageHeight;
        float maxW = ImGui.GetContentRegionAvail().X;
        float baseScale = Math.Min(maxW / imgW, 400f / imgH);
        float scale = baseScale * _previewZoom;
        float dispW = imgW * scale;
        float dispH = imgH * scale;

        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.Image((nint)texId, new Vector2(dispW, dispH));

        // Draw frame grid overlay
        var drawList = ImGui.GetWindowDrawList();
        uint gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 0f, 0.5f));
        uint selectedColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 0.8f));

        for (int r = 0; r < SelectedSheet.Rows; r++)
        {
            for (int c = 0; c < SelectedSheet.Columns; c++)
            {
                float fx = cursorPos.X + (SelectedSheet.OffsetX + c * (SelectedSheet.FrameWidth + SelectedSheet.PaddingX)) * scale;
                float fy = cursorPos.Y + (SelectedSheet.OffsetY + r * (SelectedSheet.FrameHeight + SelectedSheet.PaddingY)) * scale;
                float fw = SelectedSheet.FrameWidth * scale;
                float fh = SelectedSheet.FrameHeight * scale;

                int frameIdx = r * SelectedSheet.Columns + c;
                uint color = frameIdx == _selectedFrameIdx ? selectedColor : gridColor;

                drawList.AddRect(new Vector2(fx, fy), new Vector2(fx + fw, fy + fh), color, 1f);

                // Frame number label
                string label = $"{frameIdx}";
                var textSize = ImGui.CalcTextSize(label);
                if (textSize.X < fw && textSize.Y < fh)
                {
                    drawList.AddText(
                        new Vector2(fx + (fw - textSize.X) * 0.5f, fy + (fh - textSize.Y) * 0.5f),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.8f)), label);
                }
            }
        }

        ImGui.Dummy(new Vector2(dispW, dispH));
    }

    private void RenderFrameGrid()
    {
        if (SelectedSheet == null) return;

        float cellSize = 48f;
        int cols = SelectedSheet.Columns;
        int rows = SelectedSheet.Rows;
        float gridW = cols * cellSize;
        float gridH = rows * cellSize;

        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Draw grid
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int frameIdx = r * cols + c;
                float x = cursorPos.X + c * cellSize;
                float y = cursorPos.Y + r * cellSize;

                bool isSelected = frameIdx == _selectedFrameIdx;
                uint bgColor = isSelected
                    ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.6f, 1f, 0.8f))
                    : ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.25f, 0.9f));
                uint borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.5f, 1f));

                drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + cellSize - 1, y + cellSize - 1), bgColor, 2f);
                drawList.AddRect(new Vector2(x, y), new Vector2(x + cellSize - 1, y + cellSize - 1), borderColor, 2f);

                // Frame number
                string label = $"{frameIdx}";
                var textSize = ImGui.CalcTextSize(label);
                drawList.AddText(
                    new Vector2(x + (cellSize - textSize.X) * 0.5f, y + (cellSize - textSize.Y) * 0.5f),
                    ImGui.ColorConvertFloat4ToU32(Vector4.One), label);

                // Click detection
                ImGui.SetCursorScreenPos(new Vector2(x, y));
                ImGui.InvisibleButton($"##frame_{frameIdx}", new Vector2(cellSize, cellSize));
                if (ImGui.IsItemClicked())
                    _selectedFrameIdx = frameIdx;
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + gridH + 4f));
    }

    private void RenderFrameThumbnail()
    {
        if (SelectedSheet == null || SelectedFrame == null) return;
        if (string.IsNullOrEmpty(SelectedSheet.ImagePath) || !File.Exists(SelectedSheet.ImagePath)) return;
        if (SelectedSheet.ImageWidth <= 0 || SelectedSheet.ImageHeight <= 0) return;

        LoadPreviewTexture(SelectedSheet.ImagePath);
        if (!_previewTextures.TryGetValue(SelectedSheet.ImagePath, out uint texId) || texId == 0) return;

        // Calculate UV for this specific frame
        float u0 = (float)SelectedFrame.X / SelectedSheet.ImageWidth;
        float v0 = (float)SelectedFrame.Y / SelectedSheet.ImageHeight;
        float u1 = (float)(SelectedFrame.X + SelectedFrame.Width) / SelectedSheet.ImageWidth;
        float v1 = (float)(SelectedFrame.Y + SelectedFrame.Height) / SelectedSheet.ImageHeight;

        // Display size: scale up to a nice preview, max 128px
        float maxSize = 128f;
        float frameW = SelectedFrame.Width;
        float frameH = SelectedFrame.Height;
        float scale = Math.Min(maxSize / Math.Max(frameW, frameH), 4f);
        float dispW = frameW * scale;
        float dispH = frameH * scale;

        // Checkerboard background to show transparency
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        uint checkerA = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.35f, 0.35f, 1f));
        uint checkerB = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.25f, 1f));
        int checkerSize = (int)(8f * scale);
        if (checkerSize < 4) checkerSize = 4;
        for (int cy = 0; cy < dispH; cy += checkerSize)
        {
            for (int cx = 0; cx < dispW; cx += checkerSize)
            {
                bool isA = ((cx / checkerSize) + (cy / checkerSize)) % 2 == 0;
                drawList.AddRectFilled(
                    new Vector2(cursorPos.X + cx, cursorPos.Y + cy),
                    new Vector2(Math.Min(cursorPos.X + cx + checkerSize, cursorPos.X + dispW),
                                Math.Min(cursorPos.Y + cy + checkerSize, cursorPos.Y + dispH)),
                    isA ? checkerA : checkerB);
            }
        }

        // Draw the frame image
        ImGui.Image((nint)texId, new Vector2(dispW, dispH),
            new Vector2(u0, v0), new Vector2(u1, v1));

        // ── Interactive overlay ──
        var mousePos = ImGui.GetIO().MousePos;
        bool mouseInFrame = mousePos.X >= cursorPos.X && mousePos.X <= cursorPos.X + dispW &&
                            mousePos.Y >= cursorPos.Y && mousePos.Y <= cursorPos.Y + dispH;

        // Draw hitbox overlay
        float hx = cursorPos.X + SelectedFrame.HitboxX * scale;
        float hy = cursorPos.Y + SelectedFrame.HitboxY * scale;
        float hw = Math.Max(SelectedFrame.HitboxW * scale, 4f);
        float hh = Math.Max(SelectedFrame.HitboxH * scale, 4f);
        bool hasHitbox = SelectedFrame.HitboxW > 0 && SelectedFrame.HitboxH > 0;

        if (hasHitbox)
        {
            drawList.AddRect(
                new Vector2(hx, hy), new Vector2(hx + hw, hy + hh),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.2f, 0.2f, 0.9f)), 0f, 0, 1.5f);

            // Resize handles (corners + edges)
            float handleSize = 5f;
            uint handleColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 1f));
            // Corners
            drawList.AddRectFilled(new Vector2(hx - handleSize, hy - handleSize), new Vector2(hx + handleSize, hy + handleSize), handleColor);
            drawList.AddRectFilled(new Vector2(hx + hw - handleSize, hy - handleSize), new Vector2(hx + hw + handleSize, hy + handleSize), handleColor);
            drawList.AddRectFilled(new Vector2(hx - handleSize, hy + hh - handleSize), new Vector2(hx + handleSize, hy + hh + handleSize), handleColor);
            drawList.AddRectFilled(new Vector2(hx + hw - handleSize, hy + hh - handleSize), new Vector2(hx + hw + handleSize, hy + hh + handleSize), handleColor);

            // Hitbox label
            string hbLabel = $"Hitbox {SelectedFrame.HitboxW}x{SelectedFrame.HitboxH}";
            var hbSize = ImGui.CalcTextSize(hbLabel);
            drawList.AddRectFilled(new Vector2(hx, hy - hbSize.Y - 4), new Vector2(hx + hbSize.X + 6, hy - 1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.7f)));
            drawList.AddText(new Vector2(hx + 3, hy - hbSize.Y - 3),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.3f, 0.3f, 1f)), hbLabel);
        }

        // Draw anchor point
        {
            float ax = cursorPos.X + SelectedFrame.AnchorX * dispW;
            float ay = cursorPos.Y + SelectedFrame.AnchorY * dispH;
            drawList.AddCircleFilled(new Vector2(ax, ay), 4f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 0f, 1f)));
            drawList.AddCircle(new Vector2(ax, ay), 6f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0.5f, 0f, 1f)));
        }

        // Frame info
        ImGui.SameLine();
        ImGui.Text($"{SelectedFrame.Width}x{SelectedFrame.Height}px @ ({SelectedFrame.X},{SelectedFrame.Y})");

        // ── Interactive hitbox editing ──
        if (mouseInFrame && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.GetIO().KeyCtrl)
        {
            float mx = mousePos.X - cursorPos.X;
            float my = mousePos.Y - cursorPos.Y;

            // Check if clicking near anchor (drag anchor)
            float ax = SelectedFrame.AnchorX * dispW;
            float ay = SelectedFrame.AnchorY * dispH;
            float anchorDist = MathF.Sqrt((mx - ax) * (mx - ax) + (my - ay) * (my - ay));
            if (anchorDist < 10f)
            {
                _frameDragMode = FrameDragMode.Anchor;
                _frameDragActive = true;
            }
            // Check if clicking on hitbox resize handles
            else if (hasHitbox)
            {
                float hbx = SelectedFrame.HitboxX * scale;
                float hby = SelectedFrame.HitboxY * scale;
                float hbw = SelectedFrame.HitboxW * scale;
                float hbh = SelectedFrame.HitboxH * scale;

                // Check corners for resize
                if (MathF.Abs(mx - hbx) < 8f && MathF.Abs(my - hby) < 8f)
                    { _frameDragMode = FrameDragMode.ResizeTopLeft; _frameDragActive = true; }
                else if (MathF.Abs(mx - (hbx + hbw)) < 8f && MathF.Abs(my - hby) < 8f)
                    { _frameDragMode = FrameDragMode.ResizeTopRight; _frameDragActive = true; }
                else if (MathF.Abs(mx - hbx) < 8f && MathF.Abs(my - (hby + hbh)) < 8f)
                    { _frameDragMode = FrameDragMode.ResizeBottomLeft; _frameDragActive = true; }
                else if (MathF.Abs(mx - (hbx + hbw)) < 8f && MathF.Abs(my - (hby + hbh)) < 8f)
                    { _frameDragMode = FrameDragMode.ResizeBottomRight; _frameDragActive = true; }
                // Check inside hitbox body (move)
                else if (mx >= hbx && mx <= hbx + hbw && my >= hby && my <= hby + hbh)
                    { _frameDragMode = FrameDragMode.MoveHitbox; _frameDragActive = true; }
                // Click outside hitbox — create new hitbox
                else
                {
                    SelectedFrame.HitboxX = (int)(mx / scale);
                    SelectedFrame.HitboxY = (int)(my / scale);
                    SelectedFrame.HitboxW = 16;
                    SelectedFrame.HitboxH = 16;
                    _frameDragMode = FrameDragMode.ResizeBottomRight;
                    _frameDragActive = true;
                }
            }
            else
            {
                // No hitbox — create one at click position
                SelectedFrame.HitboxX = (int)(mx / scale);
                SelectedFrame.HitboxY = (int)(my / scale);
                SelectedFrame.HitboxW = 16;
                SelectedFrame.HitboxH = 16;
                _frameDragMode = FrameDragMode.ResizeBottomRight;
                _frameDragActive = true;
            }
            _frameDragStartMouse = mousePos;
            _frameDragStartHitbox = new Vector4(SelectedFrame.HitboxX, SelectedFrame.HitboxY,
                SelectedFrame.HitboxW, SelectedFrame.HitboxH);
            _frameDragStartAnchor = new Vector2(SelectedFrame.AnchorX, SelectedFrame.AnchorY);
        }

        // ── Drag update ──
        if (_frameDragActive && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            float dmX = (mousePos.X - _frameDragStartMouse.X) / scale;
            float dmY = (mousePos.Y - _frameDragStartMouse.Y) / scale;

            switch (_frameDragMode)
            {
                case FrameDragMode.MoveHitbox:
                    SelectedFrame.HitboxX = Math.Clamp((int)(_frameDragStartHitbox.X + dmX), 0, SelectedFrame.Width - SelectedFrame.HitboxW);
                    SelectedFrame.HitboxY = Math.Clamp((int)(_frameDragStartHitbox.Y + dmY), 0, SelectedFrame.Height - SelectedFrame.HitboxH);
                    break;

                case FrameDragMode.ResizeTopLeft:
                    SelectedFrame.HitboxX = Math.Clamp((int)(_frameDragStartHitbox.X + dmX), 0, (int)(_frameDragStartHitbox.X + _frameDragStartHitbox.Z - 4));
                    SelectedFrame.HitboxY = Math.Clamp((int)(_frameDragStartHitbox.Y + dmY), 0, (int)(_frameDragStartHitbox.Y + _frameDragStartHitbox.W - 4));
                    SelectedFrame.HitboxW = (int)(_frameDragStartHitbox.Z - (SelectedFrame.HitboxX - _frameDragStartHitbox.X));
                    SelectedFrame.HitboxH = (int)(_frameDragStartHitbox.W - (SelectedFrame.HitboxY - _frameDragStartHitbox.Y));
                    break;

                case FrameDragMode.ResizeTopRight:
                    SelectedFrame.HitboxY = Math.Clamp((int)(_frameDragStartHitbox.Y + dmY), 0, (int)(_frameDragStartHitbox.Y + _frameDragStartHitbox.W - 4));
                    SelectedFrame.HitboxW = Math.Clamp((int)(_frameDragStartHitbox.Z + dmX), 4, (int)(SelectedFrame.Width - _frameDragStartHitbox.X));
                    SelectedFrame.HitboxH = (int)(_frameDragStartHitbox.W - (SelectedFrame.HitboxY - _frameDragStartHitbox.Y));
                    break;

                case FrameDragMode.ResizeBottomLeft:
                    SelectedFrame.HitboxX = Math.Clamp((int)(_frameDragStartHitbox.X + dmX), 0, (int)(_frameDragStartHitbox.X + _frameDragStartHitbox.Z - 4));
                    SelectedFrame.HitboxW = (int)(_frameDragStartHitbox.Z - (SelectedFrame.HitboxX - _frameDragStartHitbox.X));
                    SelectedFrame.HitboxH = Math.Clamp((int)(_frameDragStartHitbox.W + dmY), 4, (int)(SelectedFrame.Height - _frameDragStartHitbox.Y));
                    break;

                case FrameDragMode.ResizeBottomRight:
                    SelectedFrame.HitboxW = Math.Clamp((int)(_frameDragStartHitbox.Z + dmX), 4, (int)(SelectedFrame.Width - _frameDragStartHitbox.X));
                    SelectedFrame.HitboxH = Math.Clamp((int)(_frameDragStartHitbox.W + dmY), 4, (int)(SelectedFrame.Height - _frameDragStartHitbox.Y));
                    break;

                case FrameDragMode.Anchor:
                    float newAX = Math.Clamp(_frameDragStartAnchor.X + dmX / (SelectedFrame.Width), 0f, 1f);
                    float newAY = Math.Clamp(_frameDragStartAnchor.Y + dmY / (SelectedFrame.Height), 0f, 1f);
                    SelectedFrame.AnchorX = (float)Math.Round(newAX, 2);
                    SelectedFrame.AnchorY = (float)Math.Round(newAY, 2);
                    break;
            }
        }
        else if (_frameDragActive && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _frameDragActive = false;
        }

        ImGui.Separator();
    }

    private void RenderAnimationPreview()
    {
        if (SelectedSheet == null || SelectedSheet.FrameCount == 0) return;

        // Controls
        ImGui.Checkbox("Loop", ref _animLoop);
        ImGui.SameLine();
        int fpsInt = (int)_animFPS;
        ImGui.PushItemWidth(60);
        ImGui.InputInt("FPS", ref fpsInt);
        ImGui.PopItemWidth();
        _animFPS = Math.Clamp(fpsInt, 1, 120);
        ImGui.SameLine();
        ImGui.PushItemWidth(80);
        ImGui.SliderFloat("Zoom##anim", ref _animPreviewZoom, 1f, 8f, "%.1fx");
        ImGui.PopItemWidth();

        ImGui.PushItemWidth(80);
        ImGui.InputInt("Start", ref _animStartFrame);
        ImGui.SameLine();
        ImGui.InputInt("End", ref _animEndFrame);
        ImGui.PopItemWidth();

        _animStartFrame = Math.Clamp(_animStartFrame, 0, SelectedSheet.FrameCount - 1);
        _animEndFrame = Math.Clamp(_animEndFrame, _animStartFrame, SelectedSheet.FrameCount - 1);

        int totalFrames = _animEndFrame - _animStartFrame + 1;
        float duration = totalFrames / _animFPS;
        ImGui.Text($"{_animFPS} FPS | {totalFrames} frames | {duration:F2}s | 1/{_animFPS:F0}s per frame");

        // Play controls
        if (ImGui.Button(_animPlaying ? "Stop" : "Play"))
            _animPlaying = !_animPlaying;

        if (_animPlaying)
        {
            float dt = ImGui.GetIO().DeltaTime;
            _animTime += dt;
            float frameDuration = 1f / _animFPS;
            int frameCount = _animEndFrame - _animStartFrame + 1;
            int frame = (int)(_animTime / frameDuration);
            if (_animLoop)
                frame = ((frame % frameCount) + frameCount) % frameCount;
            else
                frame = Math.Min(frame, frameCount - 1);
            _currentPreviewFrame = _animStartFrame + frame;

            if (!_animLoop && frame >= frameCount - 1)
                _animPlaying = false;
        }

        // Draw preview frame
        ImGui.Separator();
        ImGui.Text($"Frame: {_currentPreviewFrame} / {SelectedSheet.FrameCount - 1}");

        float previewSize = 128f * _animPreviewZoom;
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Background
        drawList.AddRectFilled(cursorPos, new Vector2(cursorPos.X + previewSize, cursorPos.Y + previewSize),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.18f, 1f)), 4f);

        // Render actual frame image using UV coordinates
        if (!string.IsNullOrEmpty(SelectedSheet.ImagePath) && SelectedSheet.ImageWidth > 0 && SelectedSheet.ImageHeight > 0)
        {
            LoadPreviewTexture(SelectedSheet.ImagePath);
            if (_previewTextures.TryGetValue(SelectedSheet.ImagePath, out uint texId) && texId != 0)
            {
                int cols = Math.Max(1, SelectedSheet.Columns);
                int frameIdx = Math.Clamp(_currentPreviewFrame, 0, SelectedSheet.FrameCount - 1);
                int row = frameIdx / cols;
                int col = frameIdx % cols;

                // Calculate UV coordinates for this specific frame
                float u0 = (SelectedSheet.OffsetX + col * (SelectedSheet.FrameWidth + SelectedSheet.PaddingX)) / (float)SelectedSheet.ImageWidth;
                float v0 = (SelectedSheet.OffsetY + row * (SelectedSheet.FrameHeight + SelectedSheet.PaddingY)) / (float)SelectedSheet.ImageHeight;
                float u1 = u0 + SelectedSheet.FrameWidth / (float)SelectedSheet.ImageWidth;
                float v1 = v0 + SelectedSheet.FrameHeight / (float)SelectedSheet.ImageHeight;

                // Maintain aspect ratio
                float aspect = (float)SelectedSheet.FrameWidth / SelectedSheet.FrameHeight;
                float dispW = previewSize;
                float dispH = previewSize;
                if (aspect > 1f)
                    dispH = previewSize / aspect;
                else
                    dispW = previewSize * aspect;

                ImGui.Image((nint)texId, new Vector2(dispW, dispH),
                    new Vector2(u0, v0), new Vector2(u1, v1));

                // If image is smaller than preview box, position the label correctly
                float offsetX = (previewSize - dispW) * 0.5f;
                float offsetY = (previewSize - dispH) * 0.5f;
                string frameLabel = $"Frame {_currentPreviewFrame}";
                var textSize = ImGui.CalcTextSize(frameLabel);
                drawList.AddText(
                    new Vector2(cursorPos.X + offsetX + 4, cursorPos.Y + offsetY + dispH - textSize.Y - 4),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.8f, 1f)), frameLabel);

                ImGui.Dummy(new Vector2(previewSize, previewSize));
                return;
            }
        }

        // Fallback: gray box
        string fallbackLabel = $"Frame {_currentPreviewFrame}";
        var fallbackSize = ImGui.CalcTextSize(fallbackLabel);
        drawList.AddText(
            new Vector2(cursorPos.X + 4, cursorPos.Y + previewSize - fallbackSize.Y - 4),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.8f, 1f)), fallbackLabel);
        ImGui.Dummy(new Vector2(previewSize, previewSize));
    }

    private void RenderAnimationClips()
    {
        if (SelectedSheet == null) return;

        // ── Create clip from current animation preview range ──
        ImGui.Text("Create from preview:");
        ImGui.SameLine();
        ImGui.PushItemWidth(120);
        ImGui.InputText("##clipname", ref _newClipName, 128);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button("Create Clip"))
        {
            int start = Math.Clamp(_animStartFrame, 0, SelectedSheet.FrameCount - 1);
            int end = Math.Clamp(_animEndFrame, start, SelectedSheet.FrameCount - 1);
            int count = end - start + 1;

            var clip = new AnimationClip2D
            {
                Name = _newClipName,
                SpriteSheetName = SelectedSheet.Name,
                FrameIndices = Enumerable.Range(start, count).ToList(),
                FPS = _animFPS,
                Loop = _animLoop
            };
            AnimationClips.Add(clip);
            _selectedClipIdx = AnimationClips.Count - 1;
            Console.WriteLine($"[SpriteEditor] Created clip '{clip.Name}': frames {start}-{end} ({count} frames, {clip.FPS} FPS)");
        }

        ImGui.Separator();

        // ── Clip list ──
        if (AnimationClips.Count == 0)
        {
            ImGui.TextDisabled("No animation clips. Set Start/End frames above, then click 'Create Clip'.");
            return;
        }

        int deleteIdx = -1;
        for (int i = 0; i < AnimationClips.Count; i++)
        {
            var clip = AnimationClips[i];
            bool isSelected = i == _selectedClipIdx;
            string loopTag = clip.Loop ? " [loop]" : "";
            int start = clip.FrameIndices.Count > 0 ? clip.FrameIndices[0] : 0;
            int end = clip.FrameIndices.Count > 0 ? clip.FrameIndices[^1] : 0;
            string label = $"{clip.Name} ({start}-{end}, {clip.FrameIndices.Count}f @ {clip.FPS:F0}fps){loopTag}##{i}";

            ImGui.PushID(i);
            if (ImGui.SmallButton("X"))
            {
                deleteIdx = i;
                ImGui.PopID();
                continue;
            }
            ImGui.SameLine();
            if (ImGui.Selectable(label, isSelected))
                _selectedClipIdx = i;
            ImGui.PopID();
        }
        if (deleteIdx >= 0)
        {
            AnimationClips.RemoveAt(deleteIdx);
            _selectedClipIdx = Math.Min(_selectedClipIdx, AnimationClips.Count - 1);
        }

        // ── Selected clip properties ──
        if (SelectedClip != null)
        {
            ImGui.Separator();
            ImGui.Text($"Editing: {SelectedClip.Name}");

            ImGui.InputText("Name##clipname", ref SelectedClip.Name, 128);

            float fps = SelectedClip.FPS;
            if (ImGui.SliderFloat("FPS##clip", ref fps, 1f, 60f))
                SelectedClip.FPS = fps;

            bool loop = SelectedClip.Loop;
            if (ImGui.Checkbox("Loop##clip", ref loop))
                SelectedClip.Loop = loop;

            bool reverse = SelectedClip.Reverse;
            if (ImGui.Checkbox("Reverse##clip", ref reverse))
                SelectedClip.Reverse = reverse;

            float speed = SelectedClip.SpeedMultiplier;
            if (ImGui.SliderFloat("Speed##clip", ref speed, 0.1f, 4f, "%.1fx"))
                SelectedClip.SpeedMultiplier = speed;

            // Frame range info
            int clipStart = SelectedClip.FrameIndices.Count > 0 ? SelectedClip.FrameIndices[0] : 0;
            int clipEnd = SelectedClip.FrameIndices.Count > 0 ? SelectedClip.FrameIndices[^1] : 0;
            ImGui.Text($"Range: frame {clipStart}-{clipEnd} | {SelectedClip.FrameIndices.Count} frames | {SelectedClip.Duration:F2}s");

            // Frame indices display
            ImGui.Text("Frames:");
            ImGui.SameLine();
            string frameList = string.Join(", ", SelectedClip.FrameIndices.Take(20));
            if (SelectedClip.FrameIndices.Count > 20) frameList += $"... (+{SelectedClip.FrameIndices.Count - 20})";
            ImGui.TextDisabled(frameList);

            // Play this clip in the editing preview
            ImGui.Separator();
            if (ImGui.Button("▶ Play in Preview"))
            {
                _editingClip = SelectedClip;
                _clipPlaying = true;
                _clipTime = 0f;
                _clipCurrentFrame = 0;

                // Switch to the clip's sprite sheet if different
                if (!string.IsNullOrEmpty(SelectedClip.SpriteSheetName))
                {
                    int sheetIdx = SpriteSheets.FindIndex(s => s.Name == SelectedClip.SpriteSheetName);
                    if (sheetIdx >= 0 && sheetIdx != _selectedSheetIdx)
                    {
                        _selectedSheetIdx = sheetIdx;
                        _selectedFrameIdx = -1;
                        LoadSheetSettings();
                    }
                }
            }
        }

        // ── Clip Editing Preview (shows when a clip is playing) ──
        if (_editingClip != null && _editingClip.FrameIndices.Count > 0)
        {
            ImGui.Separator();
            RenderClipEditingPreview();
        }
    }

    private void RenderClipEditingPreview()
    {
        if (_editingClip == null || SelectedSheet == null) return;
        if (string.IsNullOrEmpty(SelectedSheet.ImagePath) || !File.Exists(SelectedSheet.ImagePath)) return;

        var clip = _editingClip;
        ImGui.Text($"  Clip: {clip.Name}  ");
        ImGui.SameLine();
        if (ImGui.SmallButton("X##closeclip"))
        {
            _editingClip = null;
            _clipPlaying = false;
            return;
        }

        // Playback controls
        if (ImGui.Button(_clipPlaying ? "|| Pause" : "> Play"))
            _clipPlaying = !_clipPlaying;
        ImGui.SameLine();
        if (ImGui.Button("|<"))
        {
            _clipCurrentFrame = 0;
            _clipTime = 0f;
        }
        ImGui.SameLine();
        if (ImGui.Button("<"))
        {
            _clipCurrentFrame = Math.Max(0, _clipCurrentFrame - 1);
            _clipTime = _clipCurrentFrame / (clip.FPS * clip.SpeedMultiplier);
        }
        ImGui.SameLine();
        if (ImGui.Button(">"))
        {
            _clipCurrentFrame = Math.Min(clip.FrameIndices.Count - 1, _clipCurrentFrame + 1);
            _clipTime = _clipCurrentFrame / (clip.FPS * clip.SpeedMultiplier);
        }
        ImGui.SameLine();
        if (ImGui.Button(">|"))
        {
            _clipCurrentFrame = clip.FrameIndices.Count - 1;
            _clipTime = _clipCurrentFrame / (clip.FPS * clip.SpeedMultiplier);
        }

        ImGui.SameLine();
        ImGui.PushItemWidth(80);
        ImGui.SliderFloat("Zoom##clippreview", ref _clipZoom, 1f, 8f, "%.1fx");
        ImGui.PopItemWidth();

        // Update playback
        if (_clipPlaying)
        {
            float dt = ImGui.GetIO().DeltaTime;
            _clipTime += dt * clip.SpeedMultiplier * (clip.Reverse ? -1f : 1f);
            float frameDuration = 1f / clip.FPS;
            int newFrame = (int)(_clipTime / frameDuration);
            if (clip.Loop)
                newFrame = ((newFrame % clip.FrameIndices.Count) + clip.FrameIndices.Count) % clip.FrameIndices.Count;
            else
                newFrame = Math.Clamp(newFrame, 0, clip.FrameIndices.Count - 1);
            _clipCurrentFrame = newFrame;
            if (!clip.Loop && _clipTime >= clip.Duration)
                _clipPlaying = false;
        }

        // Frame info
        int spriteFrameIdx = clip.FrameIndices[_clipCurrentFrame];
        ImGui.Text($"Frame {_clipCurrentFrame + 1}/{clip.FrameIndices.Count} | sprite #{spriteFrameIdx} | {clip.FPS:F0} FPS | {clip.Duration:F2}s");

        // Draw the frame
        LoadPreviewTexture(SelectedSheet.ImagePath);
        if (!_previewTextures.TryGetValue(SelectedSheet.ImagePath, out uint texId) || texId == 0) return;

        int cols = Math.Max(1, SelectedSheet.Columns);
        int row = spriteFrameIdx / cols;
        int col = spriteFrameIdx % cols;
        float u0 = (SelectedSheet.OffsetX + col * (SelectedSheet.FrameWidth + SelectedSheet.PaddingX)) / (float)SelectedSheet.ImageWidth;
        float v0 = (SelectedSheet.OffsetY + row * (SelectedSheet.FrameHeight + SelectedSheet.PaddingY)) / (float)SelectedSheet.ImageHeight;
        float u1 = u0 + SelectedSheet.FrameWidth / (float)SelectedSheet.ImageWidth;
        float v1 = v0 + SelectedSheet.FrameHeight / (float)SelectedSheet.ImageHeight;

        float previewSize = 128f * _clipZoom;
        float aspect = (float)SelectedSheet.FrameWidth / SelectedSheet.FrameHeight;
        float dispW = previewSize;
        float dispH = previewSize;
        if (aspect > 1f) dispH = previewSize / aspect;
        else dispW = previewSize * aspect;

        // Checkerboard background
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        uint ckA = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.35f, 0.35f, 1f));
        uint ckB = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.25f, 1f));
        int ck = 8;
        for (int cy = 0; cy < dispH; cy += ck)
            for (int cx = 0; cx < dispW; cx += ck)
            {
                bool isA = ((cx / ck) + (cy / ck)) % 2 == 0;
                drawList.AddRectFilled(
                    new Vector2(cursorPos.X + cx, cursorPos.Y + cy),
                    new Vector2(Math.Min(cursorPos.X + cx + ck, cursorPos.X + dispW),
                                Math.Min(cursorPos.Y + cy + ck, cursorPos.Y + dispH)),
                    isA ? ckA : ckB);
            }

        ImGui.Image((nint)texId, new Vector2(dispW, dispH), new Vector2(u0, v0), new Vector2(u1, v1));

        // Frame timeline bar
        ImGui.Separator();
        float timelineW = ImGui.GetContentRegionAvail().X;
        float timelineH = 20f;
        var tlCursor = ImGui.GetCursorScreenPos();
        uint tlBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.2f, 1f));
        drawList.AddRectFilled(tlCursor, new Vector2(tlCursor.X + timelineW, tlCursor.Y + timelineH), tlBg, 3f);

        // Draw frame markers
        float frameW = timelineW / clip.FrameIndices.Count;
        for (int i = 0; i < clip.FrameIndices.Count; i++)
        {
            float fx = tlCursor.X + i * frameW;
            bool isActive = i == _clipCurrentFrame;
            uint markerColor = isActive
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.7f, 1f, 0.9f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.4f, 0.6f));
            drawList.AddRectFilled(
                new Vector2(fx + 1, tlCursor.Y + 2),
                new Vector2(fx + frameW - 1, tlCursor.Y + timelineH - 2),
                markerColor, 2f);

            if (frameW > 12f)
            {
                string num = $"{i}";
                var numSize = ImGui.CalcTextSize(num);
                if (numSize.X < frameW - 2)
                    drawList.AddText(new Vector2(fx + (frameW - numSize.X) * 0.5f, tlCursor.Y + 3),
                        ImGui.ColorConvertFloat4ToU32(Vector4.One), num);
            }
        }

        // Click on timeline to jump
        ImGui.InvisibleButton("##timeline", new Vector2(timelineW, timelineH));
        if (ImGui.IsItemClicked())
        {
            float clickX = ImGui.GetIO().MousePos.X - tlCursor.X;
            int clickFrame = Math.Clamp((int)(clickX / frameW), 0, clip.FrameIndices.Count - 1);
            _clipCurrentFrame = clickFrame;
            _clipTime = clickFrame / (clip.FPS * clip.SpeedMultiplier);
        }

        ImGui.Dummy(new Vector2(0, 4));
    }

    private void ImportNewSheet()
    {
        _importDialog.OpenForLoad("*.png;*.jpg", "Import Sprite Sheet");
    }

    // ── Save/Load ──

    /// <summary>Get the default save path for sprite sheets in the current project.</summary>
    private string GetDefaultSavePath()
    {
        string dir = Engine.Project.ProjectManager.IsProjectLoaded
            ? Path.Combine(Engine.Project.ProjectManager.ProjectRoot!, "Assets", "Sprites")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sprites");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "sprites.sheets.json");
    }

    /// <summary>Snapshot all editor state (sheets + clips + Animation Preview settings).
    /// Used by every save path so Save / Save As... never drop data.</summary>
    private SpriteSheetsSaveData CaptureSaveData()
    {
        var data = new SpriteSheetsSaveData
        {
            Sheets = SpriteSheets.Select(s => s.ToData()).ToList(),
            AnimationClips = AnimationClips.Select(c => c.ToData()).ToList()
        };
        // Persist the Animation Preview range (Start/End), FPS and Loop so they come
        // back exactly as the user left them after a reload.
        if (SelectedSheet != null && SelectedSheet.FrameCount > 0)
        {
            data.PreviewStartFrame = Math.Clamp(_animStartFrame, 0, SelectedSheet.FrameCount - 1);
            data.PreviewEndFrame = Math.Clamp(_animEndFrame, data.PreviewStartFrame, SelectedSheet.FrameCount - 1);
            data.PreviewFps = Math.Clamp(_animFPS, 1f, 120f);
            data.PreviewLoop = _animLoop;
        }
        return data;
    }

    /// <summary>Save all sprite sheets to the default project location.</summary>
    public void SaveAllSheets()
    {
        string path = GetDefaultSavePath();
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(CaptureSaveData(), opts);
            File.WriteAllText(path, json);
            _lastSavePath = path;
            Console.WriteLine($"[SpriteEditor] Saved {SpriteSheets.Count} sheets + {AnimationClips.Count} clips to: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Save failed: {ex.Message}");
        }
    }

    /// <summary>Open save dialog to choose a custom save path.</summary>
    public void SaveAllSheetsDialog()
    {
        _saveDialog.OpenForSave("sprites.sheets.json");
    }

    private void ProcessSaveResult()
    {
        if (_saveDialog.IsConfirmed && _saveDialog.SelectedPath != null)
        {
            string path = _saveDialog.SelectedPath;
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(CaptureSaveData(), opts);
                File.WriteAllText(path, json);
                _lastSavePath = path;
                Console.WriteLine($"[SpriteEditor] Saved {SpriteSheets.Count} sheets + {AnimationClips.Count} clips to: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpriteEditor] Save failed: {ex.Message}");
            }
        }
    }

    private void ProcessLoadSheetsResult()
    {
        if (_loadSheetsDialog.IsConfirmed && _loadSheetsDialog.SelectedPath != null)
        {
            LoadSheetsFromFile(_loadSheetsDialog.SelectedPath);
        }
    }

    /// <summary>Load all sprite sheets from the default project location.</summary>
    public void LoadAllSheets()
    {
        string path = GetDefaultSavePath();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[SpriteEditor] No save file found at: {path}");
            return;
        }
        LoadSheetsFromFile(path);
    }

    /// <summary>Open load dialog to choose a custom file.</summary>
    public void LoadAllSheetsDialog()
    {
        _loadSheetsDialog.OpenForLoad("*.sheets.json", "Load Sprite Sheets");
    }

    private void LoadSheetsFromFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<SpriteSheetsSaveData>(json);
            if (data?.Sheets == null)
            {
                Console.WriteLine($"[SpriteEditor] Invalid save file: {path}");
                return;
            }

            SpriteSheets.Clear();
            foreach (var sheetData in data.Sheets)
            {
                var sheet = SpriteSheet.FromData(sheetData);
                SpriteSheets.Add(sheet);
                // Load preview texture if image exists
                if (!string.IsNullOrEmpty(sheet.ImagePath) && File.Exists(sheet.ImagePath))
                    LoadPreviewTexture(sheet.ImagePath);
            }
            // Load animation clips
            AnimationClips.Clear();
            if (data.AnimationClips != null)
            {
                foreach (var clipData in data.AnimationClips)
                    AnimationClips.Add(AnimationClip2D.FromData(clipData));
            }
            _selectedSheetIdx = SpriteSheets.Count > 0 ? 0 : -1;
            _selectedFrameIdx = -1;
            _selectedClipIdx = -1;
            if (SelectedSheet != null) LoadSheetSettings();

            // Restore the last Animation Preview settings (Start/End/FPS/Loop).
            if (data.PreviewStartFrame >= 0 && SelectedSheet != null)
            {
                _animStartFrame = Math.Clamp(data.PreviewStartFrame, 0, Math.Max(0, SelectedSheet.FrameCount - 1));
                _animEndFrame = Math.Clamp(Math.Max(data.PreviewEndFrame, _animStartFrame),
                    _animStartFrame, Math.Max(0, SelectedSheet.FrameCount - 1));
                _animFPS = Math.Clamp(data.PreviewFps, 1f, 120f);
                _animLoop = data.PreviewLoop;
            }
            _lastLoadPath = path;
            Console.WriteLine($"[SpriteEditor] Loaded {SpriteSheets.Count} sheets + {AnimationClips.Count} clips from: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Load failed: {ex.Message}");
        }
    }

    /// <summary>Auto-load sprite sheets when a project is opened.</summary>
    public void OnProjectChanged(string? projectRoot)
    {
        SpriteSheets.Clear();
        AnimationClips.Clear();
        _selectedSheetIdx = -1;
        _selectedFrameIdx = -1;
        _selectedClipIdx = -1;
        _previewTextures.Clear();

        if (!string.IsNullOrEmpty(projectRoot))
        {
            string path = Path.Combine(projectRoot, "Assets", "Sprites", "sprites.sheets.json");
            if (File.Exists(path))
                LoadSheetsFromFile(path);
        }
    }

    /// <summary>Import a sprite sheet from a direct file path (used by drag-and-drop).</summary>
    private void ImportSpriteFromPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var sheet = new SpriteSheet
        {
            Name = Path.GetFileNameWithoutExtension(path),
            ImagePath = path,
            Columns = _editCols,
            Rows = _editRows,
            FrameWidth = _editFrameW,
            FrameHeight = _editFrameH,
            PaddingX = _editPadX,
            PaddingY = _editPadY,
            OffsetX = _editOffsetX,
            OffsetY = _editOffsetY
        };

        // Auto-detect image dimensions and grid
        try
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            sheet.ImageWidth = image.Width;
            sheet.ImageHeight = image.Height;

            // Auto-detect frame size
            if (_editFrameW == 64 && _editFrameH == 64)
            {
                for (int frameSize = 128; frameSize >= 16; frameSize /= 2)
                {
                    if (image.Width % frameSize == 0 && image.Height % frameSize == 0)
                    {
                        sheet.FrameWidth = frameSize;
                        sheet.FrameHeight = frameSize;
                        sheet.AutoDetectGrid();
                        _editCols = sheet.Columns;
                        _editRows = sheet.Rows;
                        _editFrameW = sheet.FrameWidth;
                        _editFrameH = sheet.FrameHeight;
                        Console.WriteLine($"[SpriteEditor] Auto-detected: {sheet.Columns}x{sheet.Rows} frames @ {frameSize}px");
                        break;
                    }
                }
            }
            else
            {
                sheet.AutoDetectGrid();
                _editCols = sheet.Columns;
                _editRows = sheet.Rows;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Could not read image: {ex.Message}");
        }

        sheet.BakeUniformFrames();
        SpriteSheets.Add(sheet);
        _selectedSheetIdx = SpriteSheets.Count - 1;
        _newSheetName = sheet.Name;
        LoadPreviewTexture(path);

        Console.WriteLine($"[SpriteEditor] Imported via drag: {sheet.Name} ({sheet.Columns}x{sheet.Rows} = {sheet.FrameCount} frames, {sheet.ImageWidth}x{sheet.ImageHeight}px)");
    }

    /// <summary>Process file dialog result after rendering.</summary>
    private unsafe void ProcessImportResult()
    {
        if (_importDialog.IsConfirmed && _importDialog.SelectedPath != null)
        {
            string path = _importDialog.SelectedPath;
            var sheet = new SpriteSheet
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                ImagePath = path,
                Columns = _editCols,
                Rows = _editRows,
                FrameWidth = _editFrameW,
                FrameHeight = _editFrameH,
                PaddingX = _editPadX,
                PaddingY = _editPadY,
                OffsetX = _editOffsetX,
                OffsetY = _editOffsetY
            };

            // Auto-detect image dimensions and grid
            try
            {
                using var stream = File.OpenRead(path);
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                sheet.ImageWidth = image.Width;
                sheet.ImageHeight = image.Height;

                // Auto-detect: if frame size is default, try to detect
                if (_editFrameW == 64 && _editFrameH == 64)
                {
                    // Try common sizes
                    for (int frameSize = 128; frameSize >= 16; frameSize /= 2)
                    {
                        if (image.Width % frameSize == 0 && image.Height % frameSize == 0)
                        {
                            sheet.FrameWidth = frameSize;
                            sheet.FrameHeight = frameSize;
                            sheet.AutoDetectGrid();
                            _editCols = sheet.Columns;
                            _editRows = sheet.Rows;
                            _editFrameW = sheet.FrameWidth;
                            _editFrameH = sheet.FrameHeight;
                            Console.WriteLine($"[SpriteEditor] Auto-detected: {sheet.Columns}x{sheet.Rows} frames @ {frameSize}px");
                            break;
                        }
                    }
                }
                else
                {
                    sheet.AutoDetectGrid();
                    _editCols = sheet.Columns;
                    _editRows = sheet.Rows;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpriteEditor] Could not read image: {ex.Message}");
            }

            sheet.BakeUniformFrames();
            SpriteSheets.Add(sheet);
            _selectedSheetIdx = SpriteSheets.Count - 1;
            _newSheetName = sheet.Name;

            // Load preview texture
            LoadPreviewTexture(path);

            Console.WriteLine($"[SpriteEditor] Imported: {sheet.Name} ({sheet.Columns}x{sheet.Rows} = {sheet.FrameCount} frames, {sheet.ImageWidth}x{sheet.ImageHeight}px)");
        }
    }

    private unsafe void LoadPreviewTexture(string path)
    {
        if (_previewTextures.ContainsKey(path)) return;
        try
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            uint texId;
            GL.GenTextures(1, &texId);
            GL.BindTexture(Const.GL_TEXTURE_2D, texId);
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
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            _previewTextures[path] = texId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Failed to load preview texture: {ex.Message}");
        }
    }

    private void LoadSheetSettings()
    {
        if (SelectedSheet == null) return;
        _newSheetName = SelectedSheet.Name;
        _editCols = SelectedSheet.Columns;
        _editRows = SelectedSheet.Rows;
        _editFrameW = SelectedSheet.FrameWidth;
        _editFrameH = SelectedSheet.FrameHeight;
        _editPadX = SelectedSheet.PaddingX;
        _editPadY = SelectedSheet.PaddingY;
        _editOffsetX = SelectedSheet.OffsetX;
        _editOffsetY = SelectedSheet.OffsetY;
    }
}

/// <summary>Top-level save data for all sprite sheets.</summary>
public class SpriteSheetsSaveData
{
    public List<SpriteSheetData> Sheets { get; set; } = new();
    public List<AnimationClip2DData> AnimationClips { get; set; } = new();

    // ── Last-used Animation Preview settings (Start/End/FPS/Loop) ──
    /// <summary>Saved so the preview range the user left is restored after a reload.
    /// -1 = never saved (keep defaults).</summary>
    public int PreviewStartFrame { get; set; } = -1;
    public int PreviewEndFrame { get; set; } = -1;
    public float PreviewFps { get; set; } = 12f;
    public bool PreviewLoop { get; set; } = true;
}
