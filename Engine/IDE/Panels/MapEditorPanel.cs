using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
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

    // ── Tile selection (shift+click toggle + left-drag marquee rectangle) ──
    private int _selectedTileId = 0;
    private readonly Dictionary<int, byte> _selectedTileIds = new();
    private Vector2? _paletteDragStart;
    private Vector2 _paletteDragCurrent;
    private bool _paletteDragActive;
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
    private bool _tilesetFlipV = false;

    // ── Undo ──
    private readonly Stack<(int layer, int x, int y, int oldTile, int newTile)> _undoStack = new();
    private readonly Stack<(int layer, int x, int y, int oldTile, int newTile)> _redoStack = new();

    public MapEditorPanel(IDEBridge bridge)
    {
        _bridge = bridge;
        LoadMapEditorPrefs();
    }

    /// <summary>Hook called when the project changes (open / close). Reloads the per-project
    /// grid/palette preferences stored in settings.json.</summary>
    public void OnProjectChanged(string? projectRoot)
    {
        if (!string.IsNullOrEmpty(projectRoot))
            LoadMapEditorPrefs();
    }

    // ── Grid/palette preference persistence (per-project settings.json) ──
    private bool _prefsLoaded = false;
    private double _lastPrefsSaveTime = -10;

    private void LoadMapEditorPrefs()
    {
        try
        {
            var s = DarkEngine3D_gl_csharp.Engine.Config.SettingsSave.Load();
            _showGrid = s.MapEditorShowGrid;
            _showWorldGrid = s.MapEditorShowWorldGrid;
            _worldGridSize = s.MapEditorWorldGridSize;
            _worldGridColor = new Vector4(s.MapEditorWorldGridColorR, s.MapEditorWorldGridColorG,
                s.MapEditorWorldGridColorB, s.MapEditorWorldGridColorA);
            _showPaletteGrid = s.MapEditorShowPaletteGrid;
            _paletteGridColor = new Vector4(s.MapEditorPaletteGridColorR, s.MapEditorPaletteGridColorG,
                s.MapEditorPaletteGridColorB, s.MapEditorPaletteGridColorA);
            _gridColor = new Vector4(s.MapEditorGridColorR, s.MapEditorGridColorG,
                s.MapEditorGridColorB, s.MapEditorGridColorA);
            _paletteCols = Math.Max(1, s.MapEditorPaletteCols);
            _paletteCellSize = Math.Clamp(s.MapEditorPaletteCell, 16f, 64f);
            _prefsLoaded = true;
            Console.WriteLine("[MapEditor] Loaded grid/palette prefs from settings.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapEditor] Failed to load grid/palette prefs: {ex.Message}");
        }
    }

    /// <summary>Write the current grid/palette UI values to settings.json so the panel
    /// comes back the same after restart / project reopen. Called from Render() when a
    /// control changed (throttled, so slider drags don't hammer the disk).</summary>
    private void SaveMapEditorPrefs()
    {
        try
        {
            var s = DarkEngine3D_gl_csharp.Engine.Config.SettingsSave.Load();
            s.MapEditorShowGrid = _showGrid;
            s.MapEditorShowWorldGrid = _showWorldGrid;
            s.MapEditorWorldGridSize = _worldGridSize;
            s.MapEditorWorldGridColorR = _worldGridColor.X;
            s.MapEditorWorldGridColorG = _worldGridColor.Y;
            s.MapEditorWorldGridColorB = _worldGridColor.Z;
            s.MapEditorWorldGridColorA = _worldGridColor.W;
            s.MapEditorShowPaletteGrid = _showPaletteGrid;
            s.MapEditorPaletteGridColorR = _paletteGridColor.X;
            s.MapEditorPaletteGridColorG = _paletteGridColor.Y;
            s.MapEditorPaletteGridColorB = _paletteGridColor.Z;
            s.MapEditorPaletteGridColorA = _paletteGridColor.W;
            s.MapEditorGridColorR = _gridColor.X;
            s.MapEditorGridColorG = _gridColor.Y;
            s.MapEditorGridColorB = _gridColor.Z;
            s.MapEditorGridColorA = _gridColor.W;
            s.MapEditorPaletteCols = _paletteCols;
            s.MapEditorPaletteCell = _paletteCellSize;
            DarkEngine3D_gl_csharp.Engine.Config.SettingsSave.Save(s);
            _lastPrefsSaveTime = ImGui.GetTime();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapEditor] Failed to save grid/palette prefs: {ex.Message}");
        }
    }

    /// <summary>Persist prefs after a user edit, throttled to ~2 Hz so dragging a slider
    /// or color picker doesn't write settings.json on every frame.</summary>
    private void ThrottledPersistPrefs()
    {
        if (!_prefsLoaded) return;
        if (ImGui.GetTime() - _lastPrefsSaveTime < 0.5) return;
        SaveMapEditorPrefs();
    }

    /// <summary>Keep tilemap adoption + parallax render layers fresh for in-game/preview
    /// rendering. IDE.Render() skips panel Render() in in-game mode, so this public hook
    /// is called there instead — without it, Map2D objects never receive their parallax
    /// layers/textures and parallax silently disappears in Play in Preview.</summary>
    public void SyncForGameplay()
    {
        SyncTilemapFromBridge();
        SyncParallaxToEditorObjects();
    }

    public void ShowInMenu() => ImGui.MenuItem("Map Editor", null, ref _visible);

    public void Render()
    {
        // Adopt a level that came from the selected scene's .ing (restored Map2D object):
        // keeps this panel's palette/layers/grid in sync with the active GameScene tilemap.
        // Must run even when the panel is collapsed so the checkbox state matches the
        // actual EditorObject state loaded from the scene file.
        SyncTilemapFromBridge();

        // Keep the viewport parallax render list fresh every frame (even while the panel
        // is hidden) so layers restored from a saved scene render immediately without
        // needing to open the Map Editor first.
        SyncParallaxToEditorObjects();

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

                // Keep the viewport Map2D object's parallax render list in sync so added/
                // removed/re-ordered layers appear in the 3D grid immediately.
                SyncParallaxToEditorObjects();

                // ── Player Spawn ──
                ImGui.Separator();
                ImGui.Text("Player Spawn:");
                if (ImGui.Button("Set at Hover##spawn"))
                {
                    if (_hoveredTileX >= 0 && _hoveredTileY >= 0)
                    {
                        // Center of the hovered tile, in world px (x horizontal,
                        // y height above the map's bottom edge).
                        float ws = ActiveTilemap.TileSize;
                        ActiveTilemap.PlayerSpawn = new Vector2(
                            (_hoveredTileX + 0.5f) * ws,
                            (ActiveTilemap.Height - 1 - _hoveredTileY + 0.5f) * ws);
                        ActiveTilemap.HasPlayerSpawn = true;
                        Console.WriteLine($"[MapEditor] Player spawn set to tile ({_hoveredTileX}, {_hoveredTileY})");
                    }
                    else
                    {
                        Console.WriteLine("[MapEditor] Hover a tile in the viewport first, then click 'Set at Hover'.");
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Reset##spawn") && ActiveTilemap.HasPlayerSpawn)
                {
                    ActiveTilemap.HasPlayerSpawn = false;
                    Console.WriteLine("[MapEditor] Player spawn cleared");
                }
                ImGui.TextDisabled(ActiveTilemap.HasPlayerSpawn
                    ? $"spawn: ({ActiveTilemap.PlayerSpawn.X:F0}, {ActiveTilemap.PlayerSpawn.Y:F0}) px"
                    : "not set — gameplay uses its default spawn");

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

        // Persist grid/palette prefs after user edits (throttled in the helper).
        if (_gridPrefsDirty)
        {
            _gridPrefsDirty = false;
            ThrottledPersistPrefs();
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
            if (ImGui.InputInt("Cols", ref _tilesetCols))
            {
                _tilesetCols = Math.Max(1, _tilesetCols);
                ActiveTilemap.TilesetColumns = _tilesetCols;
                _bridge.TilesetCols = _tilesetCols;
                SyncTilesetToEditorObjects();
            }
            ImGui.SameLine();
            if (ImGui.InputInt("Rows", ref _tilesetRows))
            {
                _tilesetRows = Math.Max(1, _tilesetRows);
                ActiveTilemap.TilesetRows = _tilesetRows;
                _bridge.TilesetRows = _tilesetRows;
                SyncTilesetToEditorObjects();
            }
            ImGui.SameLine();
            if (ImGui.Checkbox("FlipV", ref _tilesetFlipV))
            {
                ActiveTilemap.TilesetFlipV = _tilesetFlipV;
                _bridge.TilesetFlipV = _tilesetFlipV;
                SyncTilesetToEditorObjects();
            }
            ImGui.PopItemWidth();
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

            // Auto-detect grid from the image ONLY when the loaded map predates the
            // TilesetColumns/TilesetRows fields (i.e. still at the default 8x8). If the
            // map already carries a saved non-default grid (e.g. 12x15), trust the saved
            // value — the user may have set a custom grid that doesn't evenly divide the
            // image. The panel vars (_tilesetCols/Rows/FlipV) always follow the tilemap's
            // saved values; the image-derived values are used only to fix stale 8x8 maps.
            int tileSize = ActiveTilemap.TileSize;
            int imgCols = Math.Max(1, _tilesetImgW / tileSize);
            int imgRows = Math.Max(1, _tilesetImgH / tileSize);
            if (ActiveTilemap.TilesetColumns == 8 && ActiveTilemap.TilesetRows == 8)
            {
                ActiveTilemap.TilesetColumns = imgCols;
                ActiveTilemap.TilesetRows = imgRows;
            }
            // The panel vars must reflect what's actually saved on the tilemap, not the
            // image-derived guess. Sync them AFTER the possible backfill above so they're
            // correct in both cases (stale 8x8 fixed, or non-default preserved).
            _tilesetCols = ActiveTilemap.TilesetColumns;
            _tilesetRows = ActiveTilemap.TilesetRows;
            // TilesetFlipV is new — old maps will have it default to false. Keep it false
            // here ONLY if the tilemap still has the default (i.e. was saved before this
            // field existed). If the JSON already carries true, preserve it.
            if (ActiveTilemap.TilesetFlipV == false && ActiveTilemap.TilesetColumns == 8 && ActiveTilemap.TilesetRows == 8)
            {
                // This is a pre-FlipV map; ensure false.
                ActiveTilemap.TilesetFlipV = false;
            }
            _tilesetFlipV = ActiveTilemap.TilesetFlipV;

            // Sync to bridge for viewport rendering
            _bridge.TilesetTextureId = _tilesetTextureId;
            _bridge.TilesetCols = _tilesetCols;
            _bridge.TilesetRows = _tilesetRows;
            _bridge.TilesetFlipV = _tilesetFlipV;
            _bridge.TilesetImgW = _tilesetImgW;
            _bridge.TilesetImgH = _tilesetImgH;

            Console.WriteLine($"[MapEditor] Loaded tileset: {path} ({_tilesetImgW}x{_tilesetImgH}, {_tilesetCols}x{_tilesetRows} tiles)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapEditor] Failed to load tileset: {ex.Message}");
        }
    }

    /// <summary>If the bridge's active tilemap changed externally (scene selection / .ing
    /// restore / another panel), point this panel at it and refresh the tileset preview.
    /// Never clears an existing panel tilemap when the bridge is null — the user may still
    /// be mid-edit on a level while a menu scene is selected.</summary>
    private void SyncTilemapFromBridge()
    {
        var active = _bridge.ActiveTilemap;
        if (active == null || ReferenceEquals(active, ActiveTilemap)) return;

        ActiveTilemap = active;
        _selectedLayerIdx = ActiveTilemap.Layers.Count > 0 ? 0 : -1;
        _selectedTileId = 0;
        _hoveredTileX = -1;
        _hoveredTileY = -1;

        // Refresh tileset texture + grid from the tilemap's stored tileset path.
        if (_tilesetTextureId != 0)
        {
            uint oldTex = _tilesetTextureId;
            unsafe { GL.DeleteTextures(1, &oldTex); }
        }
        _tilesetTextureId = 0;
        // LoadTilesetTexture now reads/writes _tilesetCols/Rows/FlipV from the tilemap's
        // saved values (backfilling only when 8x8), so we don't set them here — doing so
        // would be overwritten by LoadTilesetTexture anyway.
        if (!string.IsNullOrEmpty(ActiveTilemap.TilesetImagePath) && File.Exists(ActiveTilemap.TilesetImagePath))
        {
            LoadTilesetTexture(ActiveTilemap.TilesetImagePath);
        }
        else
        {
            _tilesetImgW = 0;
            _tilesetImgH = 0;
        }

        // Adopt the level's own saved grid state (Show Grid + grid color) from the
        // tilemap itself (Tilemap2D.ShowGrid/GridColor). The grid state now lives on the
        // tilemap so it's carried by both the standalone Assets/Maps/*.tilemap.json and
        // the scene's .ing (via EditorObjectData.Tilemap). Fall back to the EditorObject
        // only for compatibility with maps saved before this field existed.
        if (ActiveTilemap != null)
        {
            _showGrid = ActiveTilemap.ShowGrid;
            _gridColor = ActiveTilemap.GridColor;
        }

        // Adopt the parallax layers carried inside the tilemap payload (restored from
        // the scene .ing or standalone map file) into the panel UI + texture cache.
        ParallaxLayers.Clear();
        if (ActiveTilemap?.ParallaxLayers is { Count: > 0 })
        {
            foreach (var pld in ActiveTilemap.ParallaxLayers)
            {
                var layer = new ParallaxLayer
                {
                    Name = pld.Name,
                    ImagePath = pld.ImagePath,
                    IsVisible = pld.IsVisible,
                    ScrollFactor = pld.ScrollFactor,
                    ZPosition = pld.ZPosition,
                    Alpha = pld.Alpha,
                    TileHorizontal = pld.TileHorizontal,
                    WidthPx = pld.WidthPx,
                    HeightPx = pld.HeightPx,
                    RepeatX = pld.RepeatX,
                    RepeatY = pld.RepeatY,
                    LeftPx = pld.LeftPx,
                    TopPx = pld.TopPx
                };
                ParallaxLayers.Add(layer);
                if (!string.IsNullOrEmpty(layer.ImagePath) && File.Exists(layer.ImagePath))
                    LoadParallaxTexture(layer.ImagePath, layer);
            }
        }
        _bridge.ParallaxLayers = ParallaxLayers.Count > 0 ? ParallaxLayers : null;
        _selectedParallaxIdx = ParallaxLayers.Count > 0 ? 0 : -1;
        if (_bridge.EditorObjectManager != null)
        {
            var mapObj = _bridge.EditorObjectManager.Objects.FirstOrDefault(o =>
                o != null && o.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D
                && ReferenceEquals(o.Map2dTilemap, ActiveTilemap));
            if (mapObj != null)
            {
                // Keep the EditorObject in sync with the tilemap (so Save All serializes
                // the correct state). Only override if the tilemap has an explicit value.
                if (mapObj.Map2dShowGrid != _showGrid || mapObj.Map2dGridColor != _gridColor)
                {
                    mapObj.Map2dShowGrid = _showGrid;
                    mapObj.Map2dGridColor = _gridColor;
                }
            }
        }

        Console.WriteLine($"[MapEditor] Adopted level '{ActiveTilemap.Name}' ({ActiveTilemap.Width}x{ActiveTilemap.Height}, {ActiveTilemap.Layers.Count} layers)");
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
        if (ImGui.Checkbox("Show Grid##toolbar", ref _showGrid))
            _gridPrefsDirty = true;
        ImGui.SameLine();
        ImGui.Checkbox("Show Collision##toolbar", ref _showCollisions);

        // Sync to bridge for viewport painting + world grid
        _bridge.MapPaintTool = (int)_currentTool;
        // Push multi-selection onto the bridge so viewport painting can use the full
        // selected tile set, preserving the dragged block's SHAPE (width x height in
        // palette grid cells, row-major with row 0 = top row). A rectangular drag keeps
        // its rectangle in the map grid; scattered shift-clicks fall back to a 1xN run.
        if (_selectedTileIds.Count == 0)
        {
            _bridge.TilePaletteSelectionCount = 0;
            _bridge.TilePaletteSelectedTiles.Clear();
            _bridge.TilePaletteSelW = 1;
            _bridge.TilePaletteSelH = 1;
        }
        else
        {
            _bridge.TilePaletteSelectionCount = _selectedTileIds.Count;

            // Compute the bounding box of the selection in palette grid coords
            // (col = id % _paletteCols, row = id / _paletteCols).
            int minCol = int.MaxValue, maxCol = int.MinValue;
            int minRow = int.MaxValue, maxRow = int.MinValue;
            foreach (var id in _selectedTileIds.Keys)
            {
                int c = id % _paletteCols;
                int r = id / _paletteCols;
                if (c < minCol) minCol = c;
                if (c > maxCol) maxCol = c;
                if (r < minRow) minRow = r;
                if (r > maxRow) maxRow = r;
            }
            int selW = maxCol - minCol + 1;
            int selH = maxRow - minRow + 1;

            // Build a row-major grid over the bounding box; cells the user did NOT
            // select are stamped as empty (-1) so scattered picks don't fill the gaps.
            _bridge.TilePaletteSelectedTiles.Clear();
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    int id = r * _paletteCols + c;
                    _bridge.TilePaletteSelectedTiles.Add(_selectedTileIds.ContainsKey(id) ? id : -1);
                }
            }
            _bridge.TilePaletteSelW = selW;
            _bridge.TilePaletteSelH = selH;
        }
        _bridge.SelectedTileId = _selectedTileIds.Count == 1 ? _selectedTileIds.Keys.First() : _selectedTileId;
        _bridge.ActiveTileLayer = _selectedLayerIdx;
        _bridge.BrushSize = _brushSize;
        _bridge.ShowWorldGrid = _showWorldGrid;
        _bridge.WorldGridSize = _worldGridSize;
        _bridge.WorldGridColor = _worldGridColor;
        _bridge.ShowPaletteGrid = _showPaletteGrid;
        _bridge.PaletteGridColor = _paletteGridColor;

        // "Show Grid" + "Grid Color" drive the Map2D tile grid in the 3D viewport: push
        // them onto every Map2D EditorObject bound to the active tilemap so both apply
        // instantly (real-time) to the scene, not only to this panel's palette.
        // Also push the ACTIVE layer so the viewport renders ONLY the selected layer's
        // tiles (non-active layers are hidden even when visible).
        if (_bridge.EditorObjectManager != null && ActiveTilemap != null)
        {
            foreach (var o in _bridge.EditorObjectManager.Objects)
            {
                if (o != null && o.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D
                    && ReferenceEquals(o.Map2dTilemap, ActiveTilemap))
                {
                    o.Map2dShowGrid = _showGrid;
                    o.Map2dGridColor = _gridColor;
                    o.Map2dTilesetCols = _tilesetCols;
                    o.Map2dTilesetRows = _tilesetRows;
                    o.Map2dTilesetFlipV = _tilesetFlipV;
                    o.Map2dActiveLayer = _selectedLayerIdx;
                    o.Map2dShowCollision = _showCollisions;
                }
            }
        }
    }

    // Prefs change tracker: set when a grid/palette UI control reports an edit; consumed
    // once per frame by Render() → ThrottledPersistPrefs().
    private bool _gridPrefsDirty = false;

    /// <summary>Push the parallax layer list onto every Map2D EditorObject bound to the
    /// active tilemap so the viewport renders each layer as an upright quad: ZPosition
    /// &gt; 0 draws in FRONT of the grid, &lt; 0 draws BEHIND it. Textures are resolved
    /// through the panel's texture cache (GetParallaxTexture) so images load on demand.
    /// Also writes the layer list back into the tilemap payload (Tilemap2D.ParallaxLayers)
    /// so Save persists the full setup in the standalone map file AND the scene .ing.</summary>
    private void SyncParallaxToEditorObjects()
    {
        // Mirror the live UI list into the tilemap payload for save/load round-trips.
        if (ActiveTilemap != null)
        {
            ActiveTilemap.ParallaxLayers = ParallaxLayers.Select(p => new TilemapParallaxLayerData
            {
                Name = p.Name,
                ImagePath = p.ImagePath,
                IsVisible = p.IsVisible,
                ScrollFactor = p.ScrollFactor,
                ZPosition = p.ZPosition,
                Alpha = p.Alpha,
                TileHorizontal = p.TileHorizontal,
                WidthPx = p.WidthPx,
                HeightPx = p.HeightPx,
                RepeatX = p.RepeatX,
                RepeatY = p.RepeatY,
                LeftPx = p.LeftPx,
                TopPx = p.TopPx
            }).ToList();
        }

        if (_bridge.EditorObjectManager == null) return;

        List<MapParallaxRenderLayer>? renderLayers = null;
        if (ActiveTilemap != null && ParallaxLayers.Count > 0)
        {
            renderLayers = new List<MapParallaxRenderLayer>(ParallaxLayers.Count);
            foreach (var pl in ParallaxLayers)
            {
                if (pl == null) continue;
                uint tex = 0;
                if (!string.IsNullOrEmpty(pl.ImagePath) && File.Exists(pl.ImagePath))
                    tex = GetParallaxTexture(pl.ImagePath);
                renderLayers.Add(new MapParallaxRenderLayer
                {
                    Name = pl.Name,
                    ImagePath = pl.ImagePath,
                    IsVisible = pl.IsVisible,
                    ZPosition = pl.ZPosition,
                    Alpha = pl.Alpha,
                    TileHorizontal = pl.TileHorizontal,
                    ScrollFactor = pl.ScrollFactor,
                    TextureId = tex,
                    ImageWidth = pl.ImageWidth,
                    ImageHeight = pl.ImageHeight,
                    WidthPx = pl.WidthPx,
                    HeightPx = pl.HeightPx,
                    RepeatX = pl.RepeatX,
                    RepeatY = pl.RepeatY,
                    LeftPx = pl.LeftPx,
                    TopPx = pl.TopPx
                });
            }
        }

        foreach (var o in _bridge.EditorObjectManager.Objects)
        {
            if (o != null && o.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D)
            {
                // Only bind to the object rendering the active tilemap (or any Map2D when
                // no tilemap is active yet, so layers added first still show up).
                if (ActiveTilemap == null || ReferenceEquals(o.Map2dTilemap, ActiveTilemap))
                    o.Map2dParallaxLayers = renderLayers;
            }
        }
    }

    /// <summary>Push the current tileset grid (Cols/Rows/FlipV) onto every Map2D EditorObject
    /// bound to the active tilemap, AND onto the tilemap object itself (Map2dTilemap). The
    /// latter is what Save All serializes into the scene's .ing, so this bridges the gap
    /// between the panel's live UI state and the on-disk scene file.</summary>
    private void SyncTilesetToEditorObjects()
    {
        if (ActiveTilemap == null || _bridge.EditorObjectManager == null) return;
        foreach (var o in _bridge.EditorObjectManager.Objects)
        {
            if (o != null && o.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D
                && ReferenceEquals(o.Map2dTilemap, ActiveTilemap))
            {
                if (o.Map2dTilemap != null)
                {
                    o.Map2dTilemap.TilesetColumns = _tilesetCols;
                    o.Map2dTilemap.TilesetRows = _tilesetRows;
                    o.Map2dTilemap.TilesetFlipV = _tilesetFlipV;
                }
                o.Map2dTilesetCols = _tilesetCols;
                o.Map2dTilesetRows = _tilesetRows;
                o.Map2dTilesetFlipV = _tilesetFlipV;
            }
        }
    }

    /// <summary>Push the currently selected layer index onto every Map2D EditorObject bound
    /// to the active tilemap so the viewport re-bakes its mesh to render only that layer.
    /// Called whenever the user selects a different layer in the Layers list.</summary>
    private void SyncActiveLayerToEditorObjects()
    {
        if (ActiveTilemap == null || _bridge.EditorObjectManager == null) return;
        foreach (var o in _bridge.EditorObjectManager.Objects)
        {
            if (o != null && o.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D
                && ReferenceEquals(o.Map2dTilemap, ActiveTilemap))
            {
                o.Map2dActiveLayer = _selectedLayerIdx;
            }
        }
    }

    private void RenderLayers()
    {
        if (ActiveTilemap == null) return;

        for (int i = 0; i < ActiveTilemap.Layers.Count; i++)
        {
            var layer = ActiveTilemap.Layers[i];
            bool isSelected = i == _selectedLayerIdx;

            // "tl" prefix scopes these IDs away from the Parallax Layers list — both
            // use PushID(i) + "##vis" and otherwise collide when both are visible.
            ImGui.PushID($"tl{i}");

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
            {
                _selectedLayerIdx = i;
                // Push the newly selected layer onto the Map2D object right away so the
                // viewport re-bakes its mesh to render ONLY this layer (non-active layers
                // stop rendering even when visible).
                SyncActiveLayerToEditorObjects();
            }

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
            _selectedLayerIdx = ActiveTilemap.Layers.Count - 1;
            // No per-layer scene object is created (the single whole-map Map2D object renders
            // every visible layer). If the map object was deleted from the scene earlier, this
            // is also the natural "add it back" action.
            EnsureMapSceneObject();
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

                // Selection border(s)
                if (isSelected)
                    drawList.AddRect(new Vector2(x, y), new Vector2(x + _paletteCellSize, y + _paletteCellSize),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 1f)), 0f, 0, 2f);
                else if (_selectedTileIds.ContainsKey(tileId))
                    drawList.AddRect(new Vector2(x, y), new Vector2(x + _paletteCellSize, y + _paletteCellSize),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.4f, 0f, 1f)), 0f, 0, 2f);

                // Selection update: plain click starts a marquee drag (rectangle select),
                // shift+click toggles a single tile into the multi-select.
                ImGui.SetCursorScreenPos(new Vector2(x, y));
                ImGui.InvisibleButton($"##tile_{tileId}", new Vector2(_paletteCellSize, _paletteCellSize));
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    if (ImGui.GetIO().KeyShift)
                    {
                        if (_selectedTileIds.ContainsKey(tileId))
                            _selectedTileIds.Remove(tileId);
                        else
                            _selectedTileIds[tileId] = 1;
                        _selectedTileId = tileId;
                    }
                    else
                    {
                        _selectedTileIds.Clear();
                        _selectedTileIds[tileId] = 1;
                        _selectedTileId = tileId;
                        _paletteDragStart = ImGui.GetMousePos();
                        _paletteDragCurrent = _paletteDragStart.Value;
                        _paletteDragActive = false;
                    }
                }
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

        // ── Left-drag marquee rectangle select ──
        if (_paletteDragStart != null)
        {
            _paletteDragCurrent = ImGui.GetMousePos();
            if (Vector2.Distance(_paletteDragCurrent, _paletteDragStart.Value) > 4f)
                _paletteDragActive = true;

            if (_paletteDragActive)
            {
                // Marquee rect in palette space
                var rMin = new Vector2(
                    MathF.Min(_paletteDragStart.Value.X, _paletteDragCurrent.X),
                    MathF.Min(_paletteDragStart.Value.Y, _paletteDragCurrent.Y));
                var rMax = new Vector2(
                    MathF.Max(_paletteDragStart.Value.X, _paletteDragCurrent.X),
                    MathF.Max(_paletteDragStart.Value.Y, _paletteDragCurrent.Y));

                // Live-preview: select every tile intersecting the rect while dragging.
                _selectedTileIds.Clear();
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int tileId = r * cols + c;
                        if (tileId >= totalTiles) break;
                        var tMin = new Vector2(cursorPos.X + c * _paletteCellSize, cursorPos.Y + r * _paletteCellSize);
                        var tMax = tMin + new Vector2(_paletteCellSize, _paletteCellSize);
                        if (tMax.X >= rMin.X && tMin.X <= rMax.X && tMax.Y >= rMin.Y && tMin.Y <= rMax.Y)
                            _selectedTileIds[tileId] = 1;
                    }
                }
                if (_selectedTileIds.Count > 0)
                    _selectedTileId = _selectedTileIds.Keys.Min();

                // Draw the marquee rectangle on top of the palette.
                drawList.AddRectFilled(rMin, rMax, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 0.12f)));
                drawList.AddRect(rMin, rMax, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 0.9f)), 0f, 0, 1.5f);
            }

            // Release: commit the selection and end the drag.
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _paletteDragStart = null;
                _paletteDragActive = false;
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + rows * _paletteCellSize + 4f));
        string selTxt = _selectedTileIds.Count == 0
            ? "Selected: none"
            : _selectedTileIds.Count == 1
                ? $"Selected: {_selectedTileIds.Keys.First()}"
                : $"Selected: {_selectedTileIds.Count} tiles (drag to paint)";
        ImGui.Text(selTxt);
    }

    private void RenderGridSettings()
    {
        // ── Per-map camera start (Play-in-Preview anchor) ──
        ImGui.Text("Camera Start:");
        if (ActiveTilemap != null)
        {
            if (ImGui.Button("Set Current View##camstart"))
            {
                var cam = _bridge.Camera;
                if (cam != null)
                {
                    ActiveTilemap.CameraStartPos = cam.Position;
                    ActiveTilemap.CameraStartYaw = cam.Yaw;
                    ActiveTilemap.CameraStartPitch = cam.Pitch;
                    ActiveTilemap.CameraStartOrthoSize = cam.OrthoSize;
                    ActiveTilemap.HasCameraStart = true;
                    Console.WriteLine($"[MapEditor] Camera start saved for '{ActiveTilemap.Name}'");
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Reset##camstart"))
            {
                ActiveTilemap.HasCameraStart = false;
                Console.WriteLine($"[MapEditor] Camera start cleared — default bottom-left framing returns");
            }
            ImGui.TextDisabled(ActiveTilemap.HasCameraStart
                ? "saved — Play in Preview starts here"
                : "not set — uses default bottom-left framing");
        }

        ImGui.Separator();
        if (ImGui.Checkbox("Show Grid##gridsettings", ref _showGrid))
            _gridPrefsDirty = true;
        if (ImGui.ColorEdit4("Grid Color", ref _gridColor))
            _gridPrefsDirty = true;
        if (ImGui.InputInt("Palette Columns", ref _paletteCols))
        {
            _paletteCols = Math.Max(1, _paletteCols);
            _gridPrefsDirty = true;
        }
        if (ImGui.SliderFloat("Palette Cell", ref _paletteCellSize, 16f, 64f))
            _gridPrefsDirty = true;

        ImGui.Separator();
        ImGui.Text("Viewport Grid:");
        if (ImGui.Checkbox("Show World Grid##vpgrid", ref _showWorldGrid))
            _gridPrefsDirty = true;
        if (ImGui.SliderFloat("Grid Size##vpgrid", ref _worldGridSize, 16f, 128f, "%.0f px"))
            _gridPrefsDirty = true;
        if (ImGui.ColorEdit4("World Grid Color##vpgrid", ref _worldGridColor))
            _gridPrefsDirty = true;
        ImGui.Separator();
        ImGui.TextWrapped("Tile Palette Grid:");
        if (ImGui.Checkbox("Show Palette Grid##palgrid", ref _showPaletteGrid))
            _gridPrefsDirty = true;
        if (ImGui.ColorEdit4("Palette Grid Color##palgrid", ref _paletteGridColor))
            _gridPrefsDirty = true;
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

        // Only one level/map object may exist at a time — drop Map2D objects created for
        // the previous tilemap (e.g. an auto-loaded map) before creating the new one.
        RemoveAllSceneMapObjects();

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

    /// <summary>Remove every Map2D (tilemap) EditorObject from the active manager and all
    /// editor scene managers. Only one map object is ever wanted, so this runs whenever
    /// New Map / Load replaces the active tilemap (prevents stacked duplicate "New Level").</summary>
    private void RemoveAllSceneMapObjects()
    {
        RemoveMapObjectsFrom(_bridge.EditorObjectManager);
        if (_bridge.EditorScenes != null)
        {
            foreach (var kvp in _bridge.EditorScenes)
            {
                if (kvp.Value.ObjectManager != null
                    && !ReferenceEquals(kvp.Value.ObjectManager, _bridge.EditorObjectManager))
                    RemoveMapObjectsFrom(kvp.Value.ObjectManager);
            }
        }
    }

    private static void RemoveMapObjectsFrom(Engine.Objects.EditorObjectManager? mgr)
    {
        if (mgr == null) return;
        var maps = mgr.Objects
            .Where(o => o != null && o.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D)
            .ToList();
        foreach (var o in maps)
            mgr.Remove(o);
    }

    /// <summary>Find an existing Map2D EditorObject bound to ActiveTilemap, or create one
    /// so the loaded map renders (tiles + grid) in the 3D viewport. Map2D objects are
    /// gameplay content and may only be added to a GameScene — never a MainMenu scene.</summary>
    private void EnsureMapSceneObject()
    {
        if (ActiveTilemap == null || _bridge.EditorObjectManager == null) return;
        if (!IsCurrentSceneGameScene())
        {
            Console.WriteLine("[MapEditor] Tilemap loaded — select/open the GameScene to render it (map objects only live in GameScene).");
            return;
        }

        bool exists = _bridge.EditorObjectManager.Objects.Any(o =>
            o != null && o.PrimitiveType == Engine.Objects.EditorPrimitiveType.Map2D
            && ReferenceEquals(o.Map2dTilemap, ActiveTilemap));
        if (exists) return;

        var mapObj = _bridge.EditorObjectManager.AddPrimitive(
            Engine.Objects.EditorPrimitiveType.Map2D, System.Numerics.Vector3.Zero);
        mapObj.Name = ActiveTilemap.Name;
        mapObj.Map2dTilemap = ActiveTilemap;
        mapObj.Map2dTilesetCols = ActiveTilemap.TilesetColumns;
        mapObj.Map2dTilesetRows = ActiveTilemap.TilesetRows;
        mapObj.Map2dLayerIndex = -1;   // whole map (all visible layers)
        // Render only the currently selected layer in the viewport.
        mapObj.Map2dActiveLayer = _selectedLayerIdx;
        // Grid state now lives on the tilemap itself (Tilemap2D.ShowGrid/GridColor) so it's
        // carried by both the standalone Assets/Maps/*.tilemap.json and the scene's .ing.
        // Fall back to the panel var only when the tilemap has no explicit grid state (old
        // maps saved before this field existed).
        mapObj.Map2dShowGrid = ActiveTilemap.ShowGrid;
        mapObj.Map2dGridColor = ActiveTilemap.GridColor;
        _showGrid = ActiveTilemap.ShowGrid;
        _gridColor = ActiveTilemap.GridColor;
        Console.WriteLine($"[MapEditor] Created Map2D scene object for '{ActiveTilemap.Name}'");
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
            // Sync live parallax UI state into the tilemap payload first so the whole
            // setup (tiles + parallax) round-trips through Tilemap2DData.
            SyncParallaxToEditorObjects();
            var data = new MapSaveData { Tilemap = ActiveTilemap.ToData() };
            var opts = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(data, opts);
            File.WriteAllText(path, json);
            Console.WriteLine($"[MapEditor] Saved map to: {path} (parallax layers: {ActiveTilemap.ParallaxLayers.Count})");
        Console.WriteLine($"[MapEditor] Save snapshot -> Cols={ActiveTilemap.TilesetColumns}, Rows={ActiveTilemap.TilesetRows}, FlipV={ActiveTilemap.TilesetFlipV}, ShowGrid={ActiveTilemap.ShowGrid}, GridColor={ActiveTilemap.GridColor}, path='{path}'");

        // Also persist the tileset grid into the scene's .ing so project reload picks up the
        // edited Cols/Rows/FlipV. The standalone Assets/Maps/*.tilemap.json is a convenience
        // export; the canonical storage for the level is the scene file.
        try { _bridge.SaveAllScenes?.Invoke(); } catch { }
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
                SyncParallaxToEditorObjects();
                var data = new MapSaveData { Tilemap = ActiveTilemap.ToData() };
                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, opts);
                File.WriteAllText(_saveDialog.SelectedPath, json);
                Console.WriteLine($"[MapEditor] Saved map to: {_saveDialog.SelectedPath} (parallax layers: {ActiveTilemap.ParallaxLayers.Count})");
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

            // Only one level/map object may exist at a time — drop Map2D objects bound to
            // the previously loaded tilemap before swapping in the newly loaded one.
            RemoveAllSceneMapObjects();

            ActiveTilemap = Tilemap2D.FromData(data.Tilemap);
            _bridge.ActiveTilemap = ActiveTilemap;
            _selectedLayerIdx = Math.Min(1, ActiveTilemap.Layers.Count - 1);

            // Make sure a Map2D scene object renders this tilemap in the 3D viewport
            // (grid included) right after loading — just like "New Map" does.
            EnsureMapSceneObject();

            // Load parallax layers — canonical source is the tilemap payload
            // (Tilemap2DData.ParallaxLayers); fall back to the legacy top-level list.
            ParallaxLayers.Clear();
            var plxSource = data.Tilemap.ParallaxLayers ?? data.ParallaxLayers?.Select(p => new TilemapParallaxLayerData
            {
                Name = p.Name, ImagePath = p.ImagePath, IsVisible = p.IsVisible,
                ScrollFactor = p.ScrollFactor, ZPosition = p.ZPosition, Alpha = p.Alpha,
                TileHorizontal = p.TileHorizontal, WidthPx = p.WidthPx, HeightPx = p.HeightPx,
                RepeatX = p.RepeatX, RepeatY = p.RepeatY
            }).ToList();
            if (plxSource != null)
            {
                foreach (var pld in plxSource)
                {
                    var layer = new ParallaxLayer
                    {
                        Name = pld.Name,
                        ImagePath = pld.ImagePath,
                        IsVisible = pld.IsVisible,
                        ScrollFactor = pld.ScrollFactor,
                        ZPosition = pld.ZPosition,
                        Alpha = pld.Alpha,
                        TileHorizontal = pld.TileHorizontal,
                        WidthPx = pld.WidthPx,
                        HeightPx = pld.HeightPx,
                        RepeatX = pld.RepeatX,
                        RepeatY = pld.RepeatY,
                        LeftPx = pld.LeftPx,
                        TopPx = pld.TopPx
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
            Console.WriteLine($"[MapEditor] Load snapshot <- Cols={ActiveTilemap.TilesetColumns}, Rows={ActiveTilemap.TilesetRows}, FlipV={ActiveTilemap.TilesetFlipV}, ShowGrid={ActiveTilemap.ShowGrid}, GridColor={ActiveTilemap.GridColor}, tileset='{ActiveTilemap.TilesetImagePath}', json_exists={File.Exists(path)}");
            Console.WriteLine($"[MapEditor] Load JSON content (first 300 chars): {json.Substring(0, Math.Min(300, json.Length))}");
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
            Console.WriteLine($"[MapEditor] AutoLoad snapshot <- Cols={ActiveTilemap.TilesetColumns}, Rows={ActiveTilemap.TilesetRows}, FlipV={ActiveTilemap.TilesetFlipV}");
        }
    }

    // ── Parallax Layers ──
    public List<ParallaxLayer> ParallaxLayers = new();
    private int _selectedParallaxIdx = -1;
    private readonly ImGuiFileDialog _parallaxDialog = new();
    private readonly Dictionary<string, uint> _parallaxTextures = new();
    /// <summary>Image dimensions per loaded parallax path — survives texture-cache hits
    /// so layer ImageWidth/Height can always be backfilled (proportional sizing needs
    /// them even when the texture itself was already on the GPU).</summary>
    private readonly Dictionary<string, (int w, int h)> _parallaxImageDims = new();

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
            // "plx" prefix scopes these IDs away from the tile Layers list — both
            // use PushID(i) + "##vis" and otherwise collide when both are visible.
            ImGui.PushID($"plx{i}");

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

            float zPos = sel.ZPosition;
            if (ImGui.SliderFloat("Z Position (depth)##plx", ref zPos, -10f, 10f, "%.1f"))
                sel.ZPosition = zPos;
            ImGui.Text("< 0 = behind the grid, > 0 = in front of the grid");

            float alpha = sel.Alpha;
            if (ImGui.SliderFloat("Alpha##plx", ref alpha, 0f, 1f, "%.1f"))
                sel.Alpha = alpha;

            // ── Size: Width/Height in pixels. 0 = auto (follows the map grid size). ──
            float wPx = sel.WidthPx;
            if (ImGui.DragFloat("Width (px)##plx", ref wPx, 8f, 0f, 100000f, "%.0f"))
                sel.WidthPx = MathF.Max(0f, wPx);
            ImGui.TextDisabled("0 = follow grid width");

            float hPx = sel.HeightPx;
            if (ImGui.DragFloat("Height (px)##plx", ref hPx, 8f, 0f, 100000f, "%.0f"))
                sel.HeightPx = MathF.Max(0f, hPx);
            ImGui.TextDisabled("0 = follow grid height");

            // ── Texture repeats across the quad (0 = auto, natural size). ──
            int repX = sel.RepeatX;
            if (ImGui.InputInt("Repeat X##plx", ref repX))
                sel.RepeatX = Math.Max(0, repX);
            ImGui.SameLine();
            int repY = sel.RepeatY;
            if (ImGui.InputInt("Repeat Y##plx", ref repY))
                sel.RepeatY = Math.Max(0, repY);
            ImGui.TextDisabled("0 = auto (natural size); > 0 tiles the texture");

            // ── Position: Left/Top offsets in pixels from the grid's top-left corner. ──
            float leftPx = sel.LeftPx;
            if (ImGui.DragFloat("Left (px)##plx", ref leftPx, 1f, -100000f, 100000f, "%.0f"))
                sel.LeftPx = leftPx;
            ImGui.TextDisabled("0 = flush with grid left; negative = extend left");

            float topPx = sel.TopPx;
            if (ImGui.DragFloat("Top (px)##plx", ref topPx, 1f, -100000f, 100000f, "%.0f"))
                sel.TopPx = topPx;
            ImGui.TextDisabled("0 = flush with grid top; negative = extend above grid");

            // Load image
            if (ImGui.Button("Load Image##plx"))
                _parallaxDialog.OpenForLoad("*.png;*.jpg;*.bmp", "Select Parallax Image");

            if (!string.IsNullOrEmpty(sel.ImagePath))
            {
                ImGui.SameLine();
                ImGui.TextDisabled(Path.GetFileName(sel.ImagePath));
                ImGui.TextDisabled($"image: {sel.ImageWidth}x{sel.ImageHeight}px");
            }
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
        // Cache hit: the texture exists but the layer may still be missing its image
        // dimensions (early return used to skip the dims — leaving ImageWidth/Height 0,
        // which made the proportional sizing fall back to stretch-to-grid, i.e. the
        // parallax rendered stretched in-game). Backfill dims from the dims cache.
        if (_parallaxTextures.ContainsKey(path))
        {
            if (layer != null && layer.ImageWidth == 0 && _parallaxImageDims.TryGetValue(path, out var dims))
            {
                layer.ImageWidth = dims.w;
                layer.ImageHeight = dims.h;
            }
            return;
        }
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
            _parallaxImageDims[path] = (image.Width, image.Height);
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
    /// <summary>Depth factor along world Z (tile cells). > 0 = in front of the grid, < 0 = behind it.</summary>
    public float ZPosition = 0f;

    /// <summary>Layer opacity (0-1).</summary>
    public float Alpha = 1f;

    /// <summary>Horizontal tile repeat (true = wraps infinitely).</summary>
    public bool TileHorizontal = true;

    /// <summary>Image dimensions (set when loaded).</summary>
    public int ImageWidth;
    public int ImageHeight;

    /// <summary>Quad width in pixels. 0 = auto: follow the map grid width.</summary>
    public float WidthPx;
    /// <summary>Quad height in pixels. 0 = auto: follow the map grid height.</summary>
    public float HeightPx;
    /// <summary>Horizontal texture repeats across the quad. 0 = auto.</summary>
    public int RepeatX;
    /// <summary>Vertical texture repeats across the quad. 0 = auto.</summary>
    public int RepeatY;

    /// <summary>Left offset in pixels from the grid's left edge. 0 = flush with grid left.</summary>
    public float LeftPx;
    /// <summary>Top offset in pixels pushing the quad's top edge DOWN from the grid's
    /// top edge. 0 = flush with grid top, negative = extend above the grid.</summary>
    public float TopPx;
}

/// <summary>Top-level save data for a tilemap + parallax layers.</summary>
public class MapSaveData
{
    public Tilemap2DData? Tilemap { get; set; }
    public List<ParallaxLayerData>? ParallaxLayers { get; set; }
}

/// <summary>Serializable parallax layer data (standalone .tilemap.json export).
/// Note: the canonical storage is TilemapParallaxLayerData inside Tilemap2DData —
/// this DTO is kept for the legacy MapSaveData export shape.</summary>
public class ParallaxLayerData
{
    public string Name { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public bool IsVisible { get; set; } = true;
    public float ScrollFactor { get; set; } = 0.5f;
    public float ZPosition { get; set; }
    public float Alpha { get; set; } = 1f;
    public bool TileHorizontal { get; set; } = true;
    public float WidthPx { get; set; }
    public float HeightPx { get; set; }
    public int RepeatX { get; set; }
    public int RepeatY { get; set; }
    public float LeftPx { get; set; }
    public float TopPx { get; set; }
}
