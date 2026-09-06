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
/// Map Editor panel for 2D sidescroller levels.
/// Tile painting, layer management, collision editing, grid configuration.
/// </summary>
public class MapEditorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;
    private readonly ImGuiFileDialog _tilesetDialog = new();

    // ── Active tilemap ──
    public Tilemap2D? ActiveTilemap;

    // ── Tool modes ──
    private enum PaintTool { Paint, Erase, Fill, Pick, RectSelect }
    private PaintTool _currentTool = PaintTool.Paint;

    // ── Layer management ──
    private int _selectedLayerIdx = -1;
    private string _newLayerName = "New Layer";

    // ── Tile selection ──
    private int _selectedTileId = 0;
    private int _hoveredTileX = -1, _hoveredTileY = -1;

    // ── Grid settings ──
    private bool _showGrid = true;
    private Vector4 _gridColor = new(0.5f, 0.5f, 0.6f, 0.3f);
    private bool _showWorldGrid = true;
    private float _worldGridSize = 128f;
    private Vector4 _worldGridColor = new(1f, 1f, 1f, 0.12f);
    private bool _showPaletteGrid = true;
    private Vector4 _paletteGridColor = new(1f, 1f, 1f, 0.25f);
    private bool _showCollisions;

    // ── Brush settings ──
    private int _brushSize = 1;

    // ── Tile palette ──
    private int _paletteCols = 8;
    private float _paletteCellSize = 32f;

    // ── Scene warning ──
    private bool _showSceneWarning;

    // ── Save/Load ──
    private readonly ImGuiFileDialog _saveDialog = new();
    private readonly ImGuiFileDialog _loadDialog = new();

    // ── Tileset ──
    private uint _tilesetTextureId;
    private int _tilesetImgW, _tilesetImgH;
    private int _tilesetCols = 8;
    private int _tilesetRows = 8;

    // ── Undo ──
    private readonly Stack<(int layer, int x, int y, int oldTile, int newTile)> _undoStack = new();
    private readonly Stack<(int layer, int x, int y, int oldTile, int newTile)> _redoStack = new();

    public MapEditorPanel(IDEBridge bridge)
    {
        _bridge = bridge;
    }

    public void ShowInMenu() => ImGui.MenuItem("Map Editor", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.SetNextWindowSize(new Vector2(350, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Map Editor", ref _visible))
        {
            // ── New Map ──
            if (ImGui.Button("New Map"))
                CreateNewMap();
            ImGui.SameLine();
            if (ActiveTilemap != null && ImGui.Button("Resize"))
                ResizeMap();
            ImGui.SameLine();
            if (ImGui.Button("Save"))
                SaveMap();
            ImGui.SameLine();
            if (ImGui.Button("Load"))
                LoadMapDialog();

            if (ActiveTilemap == null)
            {
                ImGui.TextDisabled("No tilemap loaded. Click 'New Map' to create one.");
            }
            else
            {
                ImGui.Separator();

                // ── Map Info ──
                ImGui.Text($"Map: {ActiveTilemap.Name}");
                ImGui.Text($"Size: {ActiveTilemap.Width} x {ActiveTilemap.Height} tiles ({ActiveTilemap.TileSize}px)");
                ImGui.Text($"World: {ActiveTilemap.Width * ActiveTilemap.TileSize} x {ActiveTilemap.Height * ActiveTilemap.TileSize} px");

                ImGui.Separator();

                // ── Tools ──
                RenderToolbar();

                ImGui.Separator();

                // ── Layers ──
                if (ImGui.CollapsingHeader("Layers", ImGuiTreeNodeFlags.DefaultOpen))
                    RenderLayers();

                ImGui.Separator();

                // ── Tileset ──
                if (ImGui.CollapsingHeader("Tileset", ImGuiTreeNodeFlags.DefaultOpen))
                    RenderTileset();

                // ── Tile Palette ──
                if (ImGui.CollapsingHeader("Tile Palette", ImGuiTreeNodeFlags.DefaultOpen))
                    RenderTilePalette();

                ImGui.Separator();

                // ── Grid Settings ──
                if (ImGui.CollapsingHeader("Grid Settings"))
                    RenderGridSettings();

                ImGui.Separator();

                // ── Collision Tiles ──
                if (ImGui.CollapsingHeader("Collision"))
                    RenderCollisionSettings();

                ImGui.Separator();

                // ── Parallax Layers ──
                if (ImGui.CollapsingHeader("Parallax Layers"))
                    RenderParallaxLayers();

                // ── Info ──
                if (_hoveredTileX >= 0)
                    ImGui.Text($"Hover: ({_hoveredTileX}, {_hoveredTileY}) Tile: {ActiveTilemap.GetTile(_selectedLayerIdx, _hoveredTileX, _hoveredTileY)}");
            }
        }
        ImGui.End();

        // Render file dialog
        _tilesetDialog.Render();
        ProcessTilesetLoadResult();
        _parallaxDialog.Render();
        ProcessParallaxDialogResult();
        _saveDialog.Render();
        ProcessMapSaveResult();
        _loadDialog.Render();
        ProcessMapLoadResult();

        // Scene type warning popup
        if (_showSceneWarning)
        {
            ImGui.OpenPopup("Scene Type Warning");
            _showSceneWarning = false;
        }
        if (ImGui.BeginPopupModal("Scene Type Warning", ref _showSceneWarning, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "⚠ Tilemap requires GameScene");
            ImGui.Separator();
            ImGui.Text("Tilemap can only be created in a GameScene type scene.");
            ImGui.Text("Please switch to a GameScene in the Scene Manager.");
            ImGui.Separator();
            if (ImGui.Button("OK", new Vector2(120, 0)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void RenderTileset()
    {
        if (ActiveTilemap == null) return;

        if (_tilesetTextureId == 0)
        {
            ImGui.TextDisabled("No tileset loaded.");
            ImGui.SameLine();
            if (ImGui.Button("Load Tileset"))
                _tilesetDialog.OpenForLoad("*.png;*.jpg;*.bmp", "Select Tileset Image");
        }
        else
        {
            ImGui.Text($"Tileset: {Path.GetFileName(ActiveTilemap.TilesetImagePath)}");
            ImGui.SameLine();
            if (ImGui.Button("Change"))
                _tilesetDialog.OpenForLoad("*.png;*.jpg;*.bmp", "Select Tileset Image");
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                if (_tilesetTextureId != 0)
                {
                    uint tex = _tilesetTextureId;
                    unsafe { GL.DeleteTextures(1, &tex); }
                }
                _tilesetTextureId = 0;
                ActiveTilemap.TilesetImagePath = "";
            }

            ImGui.PushItemWidth(80);
            ImGui.InputInt("Cols", ref _tilesetCols);
            ImGui.SameLine();
            ImGui.InputInt("Rows", ref _tilesetRows);
            ImGui.PopItemWidth();
            _tilesetCols = Math.Max(1, _tilesetCols);
            _tilesetRows = Math.Max(1, _tilesetRows);
            ActiveTilemap.TilesetColumns = _tilesetCols;
            ActiveTilemap.TilesetRows = _tilesetRows;
        }
    }

    private unsafe void ProcessTilesetLoadResult()
    {
        if (_tilesetDialog.IsConfirmed && _tilesetDialog.SelectedPath != null)
        {
            string path = _tilesetDialog.SelectedPath;
            LoadTilesetTexture(path);
        }
    }

    private unsafe void LoadTilesetTexture(string path)
    {
        if (_tilesetTextureId != 0)
        {
            uint oldTex = _tilesetTextureId;
            GL.DeleteTextures(1, &oldTex);
        }
        _tilesetTextureId = 0;

        try
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            _tilesetImgW = image.Width;
            _tilesetImgH = image.Height;

            uint newTex;
            GL.GenTextures(1, &newTex);
            _tilesetTextureId = newTex;
            GL.BindTexture(Const.GL_TEXTURE_2D, _tilesetTextureId);
            fixed (byte* ptr = image.Data)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                              image.Width, image.Height, 0,
                              Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
            }
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            ActiveTilemap!.TilesetImagePath = path;
            ActiveTilemap.TilesetTextureId = _tilesetTextureId;

            // Auto-detect grid
            int tileSize = ActiveTilemap.TileSize;
            _tilesetCols = Math.Max(1, _tilesetImgW / tileSize);
            _tilesetRows = Math.Max(1, _tilesetImgH / tileSize);
            ActiveTilemap.TilesetColumns = _tilesetCols;
            ActiveTilemap.TilesetRows = _tilesetRows;

            // Sync to bridge for viewport rendering
            _bridge.TilesetTextureId = _tilesetTextureId;
            _bridge.TilesetCols = _tilesetCols;
            _bridge.TilesetRows = _tilesetRows;
            _bridge.TilesetImgW = _tilesetImgW;
            _bridge.TilesetImgH = _tilesetImgH;

            Console.WriteLine($"[MapEditor] Loaded tileset: {path} ({_tilesetImgW}x{_tilesetImgH}, {_tilesetCols}x{_tilesetRows} tiles)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapEditor] Failed to load tileset: {ex.Message}");
        }
    }

    private void RenderToolbar()
    {
        var tools = new[] { ("Paint", PaintTool.Paint), ("Erase", PaintTool.Erase),
                           ("Fill", PaintTool.Fill), ("Pick", PaintTool.Pick) };
        foreach (var (label, tool) in tools)
        {
            bool isActive = _currentTool == tool;
            if (isActive) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.6f, 1f, 1f));
            if (ImGui.Button($"{label}##tool", new Vector2(70, 24)))
                _currentTool = tool;
            if (isActive) ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        ImGui.NewLine();

        ImGui.SliderInt("Brush Size", ref _brushSize, 1, 10);
        ImGui.Checkbox("Show Grid##toolbar", ref _showGrid);
        ImGui.SameLine();
        ImGui.Checkbox("Show Collision##toolbar", ref _showCollisions);

        // Sync to bridge for viewport painting + world grid
        _bridge.MapPaintTool = (int)_currentTool;
        _bridge.SelectedTileId = _selectedTileId;
        _bridge.ActiveTileLayer = _selectedLayerIdx;
        _bridge.BrushSize = _brushSize;
        _bridge.ShowWorldGrid = _showWorldGrid;
        _bridge.WorldGridSize = _worldGridSize;
        _bridge.WorldGridColor = _worldGridColor;
        _bridge.ShowPaletteGrid = _showPaletteGrid;
        _bridge.PaletteGridColor = _paletteGridColor;
    }

    private void RenderLayers()
    {
        if (ActiveTilemap == null) return;

        for (int i = 0; i < ActiveTilemap.Layers.Count; i++)
        {
            var layer = ActiveTilemap.Layers[i];
            bool isSelected = i == _selectedLayerIdx;

            ImGui.PushID(i);

            // Visibility toggle
            bool vis = layer.IsVisible;
            if (ImGui.Checkbox("##vis", ref vis))
                layer.IsVisible = vis;
            ImGui.SameLine();

            // Lock toggle
            bool locked = layer.IsLocked;
            if (ImGui.Checkbox("##lock", ref locked))
                layer.IsLocked = locked;
            ImGui.SameLine();

            // Layer name
            if (ImGui.Selectable($"{layer.Name}##{i}", isSelected, ImGuiSelectableFlags.None, new Vector2(150, 20)))
                _selectedLayerIdx = i;

            ImGui.SameLine();
            ImGui.PushItemWidth(80);
            float opacity = layer.Opacity;
            if (ImGui.SliderFloat("##op", ref opacity, 0f, 1f, "%.1f"))
                layer.Opacity = opacity;
            ImGui.PopItemWidth();

            ImGui.PopID();
        }

        // Add/Remove layer
        ImGui.InputText("##newlayer", ref _newLayerName, 64);
        ImGui.SameLine();
        if (ImGui.Button("+ Add Layer##tile"))
        {
            // Add new layer to tilemap data
            var newLayer = new TileLayer
            {
                Name = _newLayerName,
                Width = ActiveTilemap.Width,
                Height = ActiveTilemap.Height
            };
            ActiveTilemap.Layers.Add(newLayer);
            int newLayerIdx = ActiveTilemap.Layers.Count - 1;
            _selectedLayerIdx = newLayerIdx;

            // Create a Map2D EditorObject for this layer
            if (_bridge.EditorObjectManager != null)
            {
                var mapObj = new Engine.Objects.EditorObject(
                    Engine.Objects.EditorPrimitiveType.Map2D,
                    $"{_newLayerName}");
                mapObj.Map2dTilemap = ActiveTilemap;
                mapObj.Map2dLayerIndex = newLayerIdx;  // Set to render only this layer
                mapObj.Map2dTilesetCols = ActiveTilemap.TilesetColumns;
                mapObj.Map2dTilesetRows = ActiveTilemap.TilesetRows;
                // Position and scale to match tilemap
                float worldW = ActiveTilemap.Width * ActiveTilemap.TileSize;
                float worldH = ActiveTilemap.Height * ActiveTilemap.TileSize;
                mapObj.Position = new System.Numerics.Vector3(-worldW * 0.5f, newLayerIdx * 0.01f, -worldH * 0.5f);
                mapObj.Scale = new System.Numerics.Vector3(worldW, 1f, worldH);
                mapObj.RotationEuler = new System.Numerics.Vector3(-90f, 0, 0);
                _bridge.EditorObjectManager.Add(mapObj);
                Console.WriteLine($"[MapEditor] Created Map2D EditorObject for layer {newLayerIdx}: '{_newLayerName}'");
            }
        }
        if (ActiveTilemap.Layers.Count > 0 && ImGui.Button("- Remove##tile"))
        {
            // Remove layer from tilemap and find + destroy corresponding EditorObject
            if (_bridge.EditorObjectManager != null)
            {
                var layer = ActiveTilemap.Layers[_selectedLayerIdx];
                // Find and remove EditorObject(s) pointing to this layer index
                var toRemove = _bridge.EditorObjectManager.Objects
                    .Where(obj => obj.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D 
                        && obj.Map2dTilemap == ActiveTilemap 
                        && obj.Map2dLayerIndex == _selectedLayerIdx)
                    .ToList();
                foreach (var obj in toRemove)
                {
                    _bridge.EditorObjectManager.Remove(obj);
                    Console.WriteLine($"[MapEditor] Removed Map2D EditorObject for layer {_selectedLayerIdx}");
                }

                // Adjust Map2dLayerIndex for objects pointing to layers after the removed one
                foreach (var obj in _bridge.EditorObjectManager.Objects)
                {
                    if (obj.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D 
                        && obj.Map2dTilemap == ActiveTilemap 
                        && obj.Map2dLayerIndex > _selectedLayerIdx)
                    {
                        obj.Map2dLayerIndex--;
                    }
                }
            }
            ActiveTilemap.Layers.RemoveAt(_selectedLayerIdx);
            _selectedLayerIdx = Math.Min(_selectedLayerIdx, ActiveTilemap.Layers.Count - 1);
        }
        ImGui.SameLine();
        if (ImGui.Button("▲ Up") && _selectedLayerIdx > 0)
        {
            (ActiveTilemap.Layers[_selectedLayerIdx], ActiveTilemap.Layers[_selectedLayerIdx - 1]) =
                (ActiveTilemap.Layers[_selectedLayerIdx - 1], ActiveTilemap.Layers[_selectedLayerIdx]);

            // Swap layer indices in EditorObjects
            if (_bridge.EditorObjectManager != null)
            {
                var obj1 = _bridge.EditorObjectManager.Objects.FirstOrDefault(
                    obj => obj.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D 
                        && obj.Map2dTilemap == ActiveTilemap 
                        && obj.Map2dLayerIndex == _selectedLayerIdx - 1);
                var obj2 = _bridge.EditorObjectManager.Objects.FirstOrDefault(
                    obj => obj.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D 
                        && obj.Map2dTilemap == ActiveTilemap 
                        && obj.Map2dLayerIndex == _selectedLayerIdx);

                if (obj1 != null && obj2 != null)
                {
                    (obj1.Map2dLayerIndex, obj2.Map2dLayerIndex) = (obj2.Map2dLayerIndex, obj1.Map2dLayerIndex);
                }
            }
            _selectedLayerIdx--;
        }
        ImGui.SameLine();
        if (ImGui.Button("▼ Down") && _selectedLayerIdx < ActiveTilemap.Layers.Count - 1)
        {
            (ActiveTilemap.Layers[_selectedLayerIdx], ActiveTilemap.Layers[_selectedLayerIdx + 1]) =
                (ActiveTilemap.Layers[_selectedLayerIdx + 1], ActiveTilemap.Layers[_selectedLayerIdx]);

            // Swap layer indices in EditorObjects
            if (_bridge.EditorObjectManager != null)
            {
                var obj1 = _bridge.EditorObjectManager.Objects.FirstOrDefault(
                    obj => obj.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D 
                        && obj.Map2dTilemap == ActiveTilemap 
                        && obj.Map2dLayerIndex == _selectedLayerIdx + 1);
                var obj2 = _bridge.EditorObjectManager.Objects.FirstOrDefault(
                    obj => obj.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D 
                        && obj.Map2dTilemap == ActiveTilemap 
                        && obj.Map2dLayerIndex == _selectedLayerIdx);

                if (obj1 != null && obj2 != null)
                {
                    (obj1.Map2dLayerIndex, obj2.Map2dLayerIndex) = (obj2.Map2dLayerIndex, obj1.Map2dLayerIndex);
                }
            }
            _selectedLayerIdx++;
        }
    }

    private void RenderTilePalette()
    {
        if (ActiveTilemap == null) return;

        int totalTiles = _tilesetCols * _tilesetRows;
        int cols = _paletteCols;
        int rows = (totalTiles + cols - 1) / cols;

        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Tile size in the tileset image
        float tileUW = _tilesetImgW > 0 ? 1f / _tilesetCols : 0f;
        float tileVH = _tilesetImgH > 0 ? 1f / _tilesetRows : 0f;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int tileId = r * cols + c;
                if (tileId >= totalTiles) break;

                float x = cursorPos.X + c * _paletteCellSize;
                float y = cursorPos.Y + r * _paletteCellSize;

                bool isSelected = tileId == _selectedTileId;

                // Draw tile image from tileset
                if (_tilesetTextureId != 0)
                {
                    int tc = tileId % _tilesetCols;
                    int tr = tileId / _tilesetCols;
                    float u0 = tc * tileUW;
                    float v0 = tr * tileVH;
                    float u1 = u0 + tileUW;
                    float v1 = v0 + tileVH;

                    ImGui.SetCursorScreenPos(new Vector2(x, y));
                    ImGui.Image((nint)_tilesetTextureId, new Vector2(_paletteCellSize, _paletteCellSize),
                        new Vector2(u0, v0), new Vector2(u1, v1));
                }
                else
                {
                    // Fallback: colored rect with ID
                    uint bgColor = isSelected
                        ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.6f, 1f, 0.8f))
                        : ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.3f, 1f));
                    drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + _paletteCellSize - 1, y + _paletteCellSize - 1), bgColor, 2f);
                    string label = $"{tileId}";
                    var textSize = ImGui.CalcTextSize(label);
                    drawList.AddText(
                        new Vector2(x + (_paletteCellSize - textSize.X) * 0.5f, y + (_paletteCellSize - textSize.Y) * 0.5f),
                        ImGui.ColorConvertFloat4ToU32(Vector4.One), label);
                }

                // Selection border
                if (isSelected)
                    drawList.AddRect(new Vector2(x, y), new Vector2(x + _paletteCellSize, y + _paletteCellSize),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 1f)), 0f, 0, 2f);

                // Click
                ImGui.SetCursorScreenPos(new Vector2(x, y));
                ImGui.InvisibleButton($"##tile_{tileId}", new Vector2(_paletteCellSize, _paletteCellSize));
                if (ImGui.IsItemClicked())
                    _selectedTileId = tileId;
            }
        }

        // Palette grid lines (drawn once after all tiles)
        if (_showPaletteGrid)
        {
            uint gridCol = ImGui.ColorConvertFloat4ToU32(_paletteGridColor);
            for (int gc = 1; gc < cols; gc++)
                drawList.AddLine(new Vector2(cursorPos.X + gc * _paletteCellSize, cursorPos.Y),
                                 new Vector2(cursorPos.X + gc * _paletteCellSize, cursorPos.Y + rows * _paletteCellSize),
                                 gridCol, 1f);
            for (int gr = 1; gr < rows; gr++)
                drawList.AddLine(new Vector2(cursorPos.X, cursorPos.Y + gr * _paletteCellSize),
                                 new Vector2(cursorPos.X + cols * _paletteCellSize, cursorPos.Y + gr * _paletteCellSize),
                                 gridCol, 1f);
        }

        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + rows * _paletteCellSize + 4f));
        ImGui.Text($"Selected: {_selectedTileId}");
    }

    private void RenderGridSettings()
    {
        ImGui.Checkbox("Show Grid##gridsettings", ref _showGrid);
        ImGui.ColorEdit4("Grid Color", ref _gridColor);
        ImGui.InputInt("Palette Columns", ref _paletteCols);
        ImGui.SliderFloat("Palette Cell", ref _paletteCellSize, 16f, 64f);

        ImGui.Separator();
        ImGui.Text("Viewport Grid:");
        ImGui.Checkbox("Show World Grid##vpgrid", ref _showWorldGrid);
        ImGui.SliderFloat("Grid Size##vpgrid", ref _worldGridSize, 16f, 128f, "%.0f px");
        ImGui.ColorEdit4("World Grid Color##vpgrid", ref _worldGridColor);
        ImGui.Separator();
        ImGui.TextWrapped("Tile Palette Grid:");
        ImGui.Checkbox("Show Palette Grid##palgrid", ref _showPaletteGrid);
        ImGui.ColorEdit4("Palette Grid Color##palgrid", ref _paletteGridColor);
    }

    private void RenderCollisionSettings()
    {
        if (ActiveTilemap == null) return;

        ImGui.Text("Tiles with Collision:");
        ImGui.TextDisabled("(Click tiles in palette, then toggle collision)");

        bool hasCollision = ActiveTilemap.ActiveLayer.TileHasCollision(_selectedTileId);
        if (ImGui.Checkbox($"Tile {_selectedTileId} has collision", ref hasCollision))
        {
            if (hasCollision)
                ActiveTilemap.ActiveLayer.CollisionTileIds.Add(_selectedTileId);
            else
                ActiveTilemap.ActiveLayer.CollisionTileIds.Remove(_selectedTileId);
        }
    }

    private bool IsCurrentSceneGameScene()
    {
        if (_bridge.SelectedEditorScene == null) return false;
        if (_bridge.EditorScenes.TryGetValue(_bridge.SelectedEditorScene, out var editorScene))
            return editorScene.Type == IDEBridge.SceneType.GameScene;
        return false;
    }

    public void CreateNewMap()
    {
        if (!IsCurrentSceneGameScene())
        {
            _showSceneWarning = true;
            Console.WriteLine("[MapEditor] Cannot create tilemap: requires GameScene type");
            return;
        }

        int width = 50;
        int height = 20;
        int tileSize = 32;

        ActiveTilemap = new Tilemap2D(width, height, tileSize)
        {
            Name = "New Level"
        };
        _bridge.ActiveTilemap = ActiveTilemap;
        _selectedLayerIdx = 0;
        _showSceneWarning = false;

        // Create a Map2D scene object at (0,0) so the tilemap renders in the 3D viewport
        if (_bridge.EditorObjectManager != null)
        {
            var mapObj = _bridge.EditorObjectManager.AddPrimitive(
                Engine.Objects.EditorPrimitiveType.Map2D, System.Numerics.Vector3.Zero);
            mapObj.Name = ActiveTilemap.Name;
            mapObj.Map2dTilemap = ActiveTilemap;
            // Scale the plane to match tilemap size in world units
            float worldW = width * tileSize;
            float worldH = height * tileSize;
            mapObj.Scale = new System.Numerics.Vector3(worldW, 1f, worldH);
            _bridge.SelectEditorObject(mapObj);
            Console.WriteLine($"[MapEditor] Created Map2D scene object: '{mapObj.Name}' at (0,0) [{width}x{height} @ {tileSize}px]");
        }

        Console.WriteLine($"[MapEditor] Created new map: {width}x{height} tiles ({tileSize}px)");
    }

    private void ResizeMap()
    {
        if (ActiveTilemap == null) return;
        Console.WriteLine("[MapEditor] Resize dialog would open here");
    }

    // ── Save/Load ──
    private string GetDefaultSavePath()
    {
        string dir = Engine.Project.ProjectManager.IsProjectLoaded
            ? Path.Combine(Engine.Project.ProjectManager.ProjectRoot!, "Assets", "Maps")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{ActiveTilemap?.Name ?? "map"}.tilemap.json");
    }

    public void SaveMap()
    {
        if (ActiveTilemap == null) return;
        string path = GetDefaultSavePath();
        try
        {
            var data = new MapSaveData
            {
                Tilemap = ActiveTilemap.ToData(),
                ParallaxLayers = ParallaxLayers.Select(p => new ParallaxLayerData
                {
                    Name = p.Name,
                    ImagePath = p.ImagePath,
                    IsVisible = p.IsVisible,
                    ScrollFactor = p.ScrollFactor,
                    YPosition = p.YPosition,
                    Alpha = p.Alpha,
                    TileHorizontal = p.TileHorizontal
                }).ToList()
            };
            var opts = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(data, opts);
            File.WriteAllText(path, json);
            Console.WriteLine($"[MapEditor] Saved map to: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapEditor] Save failed: {ex.Message}");
        }
    }

    public void SaveMapDialog()
    {
        _saveDialog.OpenForSave($"{ActiveTilemap?.Name ?? "map"}.tilemap.json");
    }

    private void ProcessMapSaveResult()
    {
        if (_saveDialog.IsConfirmed && _saveDialog.SelectedPath != null && ActiveTilemap != null)
        {
            try
            {
                var data = new MapSaveData
                {
                    Tilemap = ActiveTilemap.ToData(),
                    ParallaxLayers = ParallaxLayers.Select(p => new ParallaxLayerData
                    {
                        Name = p.Name,
                        ImagePath = p.ImagePath,
                        IsVisible = p.IsVisible,
                        ScrollFactor = p.ScrollFactor,
                        YPosition = p.YPosition,
                        Alpha = p.Alpha,
                        TileHorizontal = p.TileHorizontal
                    }).ToList()
                };
                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, opts);
                File.WriteAllText(_saveDialog.SelectedPath, json);
                Console.WriteLine($"[MapEditor] Saved map to: {_saveDialog.SelectedPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MapEditor] Save failed: {ex.Message}");
            }
        }
    }

    public void LoadMapDialog()
    {
        _loadDialog.OpenForLoad("*.tilemap.json", "Load Tilemap");
    }

    private void ProcessMapLoadResult()
    {
        if (_loadDialog.IsConfirmed && _loadDialog.SelectedPath != null)
        {
            LoadMapFromFile(_loadDialog.SelectedPath);
        }
    }

    public void LoadMapFromFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<MapSaveData>(json);
            if (data?.Tilemap == null)
            {
                Console.WriteLine($"[MapEditor] Invalid map file: {path}");
                return;
            }

            ActiveTilemap = Tilemap2D.FromData(data.Tilemap);
            _bridge.ActiveTilemap = ActiveTilemap;
            _selectedLayerIdx = Math.Min(1, ActiveTilemap.Layers.Count - 1);

            // Load parallax layers
            ParallaxLayers.Clear();
            if (data.ParallaxLayers != null)
            {
                foreach (var pld in data.ParallaxLayers)
                {
                    var layer = new ParallaxLayer
                    {
                        Name = pld.Name,
                        ImagePath = pld.ImagePath,
                        IsVisible = pld.IsVisible,
                        ScrollFactor = pld.ScrollFactor,
                        YPosition = pld.YPosition,
                        Alpha = pld.Alpha,
                        TileHorizontal = pld.TileHorizontal
                    };
                    ParallaxLayers.Add(layer);
                    if (!string.IsNullOrEmpty(layer.ImagePath) && File.Exists(layer.ImagePath))
                        LoadParallaxTexture(layer.ImagePath, layer);
                }
                _bridge.ParallaxLayers = ParallaxLayers;
            }

            // Load tileset texture if available
            if (!string.IsNullOrEmpty(ActiveTilemap.TilesetImagePath) && File.Exists(ActiveTilemap.TilesetImagePath))
                LoadTilesetTexture(ActiveTilemap.TilesetImagePath);

            Console.WriteLine($"[MapEditor] Loaded map from: {path} ({ActiveTilemap.Width}x{ActiveTilemap.Height}, {ActiveTilemap.Layers.Count} layers)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapEditor] Load failed: {ex.Message}");
        }
    }

    /// <summary>Auto-load map when a project is opened.</summary>
    public void AutoLoadMap(string? projectRoot)
    {
        ActiveTilemap = null;
        ParallaxLayers.Clear();
        _bridge.ActiveTilemap = null;
        _bridge.ParallaxLayers = null;

        if (string.IsNullOrEmpty(projectRoot)) return;

        string mapsDir = Path.Combine(projectRoot, "Assets", "Maps");
        if (!Directory.Exists(mapsDir)) return;

        // Load the first .tilemap.json found
        var files = Directory.GetFiles(mapsDir, "*.tilemap.json");
        if (files.Length > 0)
        {
            LoadMapFromFile(files[0]);
            Console.WriteLine($"[MapEditor] Auto-loaded map: {files[0]}");
        }
    }

    // ── Parallax Layers ──
    public List<ParallaxLayer> ParallaxLayers = new();
    private int _selectedParallaxIdx = -1;
    private readonly ImGuiFileDialog _parallaxDialog = new();
    private readonly Dictionary<string, uint> _parallaxTextures = new();

    public void AddParallaxLayer()
    {
        var layer = new ParallaxLayer
        {
            Name = $"Parallax {ParallaxLayers.Count}",
            ScrollFactor = Math.Max(0.1f, 1f - ParallaxLayers.Count * 0.2f)
        };
        ParallaxLayers.Add(layer);
        _bridge.ParallaxLayers = ParallaxLayers;
        _selectedParallaxIdx = ParallaxLayers.Count - 1;
        Console.WriteLine($"[MapEditor] Added parallax layer: {layer.Name} (scroll: {layer.ScrollFactor}x)");
    }

    private void RenderParallaxLayers()
    {
        // Add button
        if (ImGui.Button("+ Add Layer##plx"))
            AddParallaxLayer();
        ImGui.SameLine();
        if (ParallaxLayers.Count > 0 && ImGui.Button("- Remove##plx") && _selectedParallaxIdx >= 0)
        {
            ParallaxLayers.RemoveAt(_selectedParallaxIdx);
            _selectedParallaxIdx = Math.Min(_selectedParallaxIdx, ParallaxLayers.Count - 1);
        }

        if (ParallaxLayers.Count == 0)
        {
            ImGui.TextDisabled("No parallax layers. Click '+ Add Layer' to create one.");
            return;
        }

        for (int i = 0; i < ParallaxLayers.Count; i++)
        {
            var layer = ParallaxLayers[i];
            bool isSelected = i == _selectedParallaxIdx;
            ImGui.PushID(i);

            // Visibility toggle
            bool vis = layer.IsVisible;
            if (ImGui.Checkbox("##vis", ref vis))
                layer.IsVisible = vis;
            ImGui.SameLine();

            // Layer name
            if (ImGui.Selectable($"{layer.Name}##{i}", isSelected, ImGuiSelectableFlags.None, new Vector2(120, 20)))
                _selectedParallaxIdx = i;

            ImGui.SameLine();
            ImGui.Text($"{layer.ScrollFactor:F2}x");

            // Move up/down
            ImGui.SameLine();
            if (ImGui.SmallButton($"▲##plx{i}") && i > 0)
            {
                (ParallaxLayers[i], ParallaxLayers[i - 1]) = (ParallaxLayers[i - 1], ParallaxLayers[i]);
                _selectedParallaxIdx--;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton($"▼##plx{i}") && i < ParallaxLayers.Count - 1)
            {
                (ParallaxLayers[i], ParallaxLayers[i + 1]) = (ParallaxLayers[i + 1], ParallaxLayers[i]);
                _selectedParallaxIdx++;
            }

            ImGui.PopID();
        }

        // Selected layer properties
        if (_selectedParallaxIdx >= 0 && _selectedParallaxIdx < ParallaxLayers.Count)
        {
            var sel = ParallaxLayers[_selectedParallaxIdx];
            ImGui.Separator();
            ImGui.Text($"Editing: {sel.Name}");

            ImGui.InputText("Name##plx", ref sel.Name, 128);

            float scroll = sel.ScrollFactor;
            if (ImGui.SliderFloat("Scroll Factor##plx", ref scroll, 0f, 2f, "%.2fx"))
                sel.ScrollFactor = scroll;

            ImGui.Text("0x = static, 0.5x = half speed, 1x = normal, 2x = double");

            float yPos = sel.YPosition;
            if (ImGui.SliderFloat("Y Position##plx", ref yPos, -1f, 1f))
                sel.YPosition = yPos;

            float alpha = sel.Alpha;
            if (ImGui.SliderFloat("Alpha##plx", ref alpha, 0f, 1f, "%.1f"))
                sel.Alpha = alpha;

            // Load image
            if (ImGui.Button("Load Image##plx"))
                _parallaxDialog.OpenForLoad("*.png;*.jpg;*.bmp", "Select Parallax Image");

            if (!string.IsNullOrEmpty(sel.ImagePath))
                ImGui.TextDisabled(Path.GetFileName(sel.ImagePath));
        }
    }

    private unsafe void ProcessParallaxDialogResult()
    {
        if (_parallaxDialog.IsConfirmed && _parallaxDialog.SelectedPath != null && _selectedParallaxIdx >= 0)
        {
            string path = _parallaxDialog.SelectedPath;
            var layer = ParallaxLayers[_selectedParallaxIdx];
            layer.ImagePath = path;
            LoadParallaxTexture(path, layer);
            Console.WriteLine($"[MapEditor] Loaded parallax image: {path}");
        }
    }

    private unsafe void LoadParallaxTexture(string path, ParallaxLayer? layer = null)
    {
        if (_parallaxTextures.ContainsKey(path)) return;
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
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_REPEAT);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            _parallaxTextures[path] = texId;
            if (layer != null)
            {
                layer.ImageWidth = image.Width;
                layer.ImageHeight = image.Height;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapEditor] Failed to load parallax: {ex.Message}");
        }
    }

    /// <summary>Get parallax texture ID for viewport rendering.</summary>
    public uint GetParallaxTexture(string path)
    {
        if (_parallaxTextures.TryGetValue(path, out uint tex)) return tex;
        LoadParallaxTexture(path);
        return _parallaxTextures.TryGetValue(path, out tex) ? tex : 0;
    }

    /// <summary>
    /// Handle tile painting from viewport click.
    /// Called by ViewportPanel when clicking in the map area.
    /// </summary>
    public void PaintAtWorldPosition(Vector2 worldPos)
    {
        if (ActiveTilemap == null || _selectedLayerIdx < 0) return;
        var layer = ActiveTilemap.Layers[_selectedLayerIdx];
        if (layer.IsLocked) return;

        var (gx, gy) = ActiveTilemap.WorldToGrid(worldPos);
        int tileId = _currentTool == PaintTool.Erase ? -1 : _selectedTileId;

        // Apply brush size
        int halfBrush = _brushSize / 2;
        for (int dy = -halfBrush; dy <= halfBrush; dy++)
        {
            for (int dx = -halfBrush; dx <= halfBrush; dx++)
            {
                int tx = gx + dx, ty = gy + dy;
                int oldTile = ActiveTilemap.GetTile(_selectedLayerIdx, tx, ty);
                if (oldTile != tileId)
                {
                    _undoStack.Push((_selectedLayerIdx, tx, ty, oldTile, tileId));
                    _redoStack.Clear();
                    ActiveTilemap.SetTile(_selectedLayerIdx, tx, ty, tileId);
                }
            }
        }
    }

    /// <summary>
    /// Fill from world position.
    /// </summary>
    public void FillAtWorldPosition(Vector2 worldPos)
    {
        if (ActiveTilemap == null || _selectedLayerIdx < 0) return;
        var (gx, gy) = ActiveTilemap.WorldToGrid(worldPos);
        ActiveTilemap.FloodFill(_selectedLayerIdx, gx, gy, _selectedTileId);
    }

    /// <summary>
    /// Pick tile ID from world position.
    /// </summary>
    public void PickAtWorldPosition(Vector2 worldPos)
    {
        if (ActiveTilemap == null || _selectedLayerIdx < 0) return;
        var (gx, gy) = ActiveTilemap.WorldToGrid(worldPos);
        int tileId = ActiveTilemap.GetTile(_selectedLayerIdx, gx, gy);
        if (tileId >= 0) _selectedTileId = tileId;
    }
}

/// <summary>
/// A single parallax background layer.
/// Images scroll at different speeds to create depth illusion.
/// </summary>
public class ParallaxLayer
{
    public string Name = "Parallax";
    public string ImagePath = "";
    public bool IsVisible = true;

    /// <summary>Scroll speed multiplier. 0 = static, 0.5 = half speed, 1 = normal, 2 = double.</summary>
    public float ScrollFactor = 0.5f;

    /// <summary>Vertical position (-1 to 1, 0 = center).</summary>
    public float YPosition = 0f;

    /// <summary>Layer opacity (0-1).</summary>
    public float Alpha = 1f;

    /// <summary>Horizontal tile repeat (true = wraps infinitely).</summary>
    public bool TileHorizontal = true;

    /// <summary>Image dimensions (set when loaded).</summary>
    public int ImageWidth;
    public int ImageHeight;
}

/// <summary>Top-level save data for a tilemap + parallax layers.</summary>
public class MapSaveData
{
    public Tilemap2DData? Tilemap { get; set; }
    public List<ParallaxLayerData>? ParallaxLayers { get; set; }
}

/// <summary>Serializable parallax layer data.</summary>
public class ParallaxLayerData
{
    public string Name { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public bool IsVisible { get; set; } = true;
    public float ScrollFactor { get; set; } = 0.5f;
    public float YPosition { get; set; }
    public float Alpha { get; set; } = 1f;
    public bool TileHorizontal { get; set; } = true;
}
