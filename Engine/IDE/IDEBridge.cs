using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.IDE.Panels;
using System.Linq;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using System.IO;
using System.Numerics;
  
namespace DarkEngine3D_gl_csharp.Engine.IDE;

/// <summary>
/// Provides engine data to IDE panels without coupling them to engine internals.
/// The engine (GameScene, MainMenuScene, etc.) populates this each frame.
/// </summary>
public class IDEBridge
{
    /// <summary>The most recently constructed bridge (the live IDE's). Engine
    /// classes without a bridge reference (e.g. terrain sculpt baking) can read
    /// the current scene name through <see cref="Current"/>.</summary>
    public static IDEBridge? Current { get; private set; }

    public IDEBridge() => Current = this;

    /// <summary>Host IDE back-reference (set by IDE's constructor) so panels can reach
    /// sibling panels without new constructor wiring — e.g. ViewportPanel pushing the
    /// hovered grid cell to the Map Editor for trigger placement.</summary>
    public IDE? HostIDE { get; set; }
    // ── ImGui Controller (for dynamic font loading) ──
    public ImGuiController? ImGuiCtrl { get; set; }

    // ── Scene info ──
    public string SceneName { get; set; } = "GameScene";
    public int TotalObjects { get; set; }
    public int DrawnObjects { get; set; }
    public int TotalTriangles { get; set; }
    public int RenderedTriangles { get; set; }
    public int AnimatedObjectCount { get; set; }
    public int StaticObjectCount { get; set; }
    /// <summary>Editor objects in the ACTIVE EditorObjectManager (Player2D, Sprite2D,
    /// Map2D tilemap, light markers, primitives — everything the 2D editor draws).
    /// The GLTF counters above stay 0 in pure 2D scenes; these fill the Scene View stats.</summary>
    public int EditorObjectTotal { get; set; }
    /// <summary>Player2D/Sprite2D objects considered for the 2D sprite pass this frame
    /// (frustum-cull step; equals visible ones before culling).</summary>
    public int EditorObjectTotal2D { get; set; }
    /// <summary>Player2D/Sprite2D objects actually DRAWN this frame (not culled by the frustum).</summary>
    public int EditorObjectDrawn2D { get; set; }
    public float Fps { get; set; }
    public float FrameMs { get; set; }
    /// <summary>Objects render time in ms (last frame). Set by GameScene, shown in the in-game overlay.</summary>
    public float RenderObjectsMs { get; set; }
    /// <summary>Post-process pass time in ms (last frame). Set by GameScene, shown in the in-game overlay.</summary>
    public float RenderPostFxMs { get; set; }
    /// <summary>Total frame render time in ms (last frame). Set by GameScene, shown in the in-game overlay.</summary>
    public float RenderTotalMs { get; set; }
    /// <summary>Per-object render timings from the last frame (animated characters + static groups).
    /// Set by GameScene each frame; consumed by the Render Time panel.</summary>
    public IReadOnlyList<RenderTimingSample>? ObjectRenderTimings { get; set; }
    /// <summary>True while the Render Time panel is open. When false, GameScene skips
    /// per-object timing capture so the main render path stays allocation-free.</summary>
    public bool CaptureRenderTimings { get; set; }

    // ── Selected object for inspector ──
    public GltfObject? SelectedObject { get; set; }
    public CharacterAgent? SelectedAgent { get; set; }

    // ── UI element selection (for MainMenu/Loading scenes) ──
    /// <summary>Currently selected UI element in the hierarchy tree (primary/last-clicked).</summary>
    public UIElement? SelectedUIElement { get; set; }
    /// <summary>All currently selected UI elements (for multi-select). Keep in sync with SelectedUIElement.</summary>
    public HashSet<UIElement> SelectedUIElements { get; set; } = [];
    /// <summary>Legacy: flat list of UI buttons (kept for backward compat).</summary>
    public IReadOnlyList<UIButtonData>? SceneUIButtons { get; set; }
    /// <summary>The invisible scene-root container that holds all top-level UI elements.</summary>
    public UIElement? SceneRoot { get; set; }
    /// <summary>True while a visible overlay (first visible root Container) is shown.
    /// Modal behavior: the game world (movement, camera freefly, viewport picking) is
    /// blocked and only elements inside the overlay accept input — the background is
    /// visible but inert until the overlay closes.</summary>
    public bool IsOverlayVisible { get; set; }
    /// <summary>Root-level UI elements in the current scene, for hierarchy display (children of SceneRoot).</summary>
    public IReadOnlyList<UIElement>? SceneRootElements { get; set; }

    // ── All agents (for hierarchy/selection) ──
    public IReadOnlyList<CharacterAgent>? AllAgents { get; set; }

    // ── Camera info ──
    public Camera? Camera { get; set; }
    public Vector3 CameraPosition { get; set; }
    public float CameraYaw { get; set; }
    public float CameraPitch { get; set; }

    // ── Pending freefly-camera restore (set by SceneManagerPanel when a .ing file with a
    // saved camera position is loaded BEFORE the editor camera exists; SceneManager applies
    // it as soon as the editor camera is created, then clears it) ──
    public Vector3? PendingCameraPos { get; set; }
    public float? PendingCameraYaw { get; set; }
    public float? PendingCameraPitch { get; set; }
    public bool? PendingCameraOrtho { get; set; }
    public float? PendingCameraOrthoSize { get; set; }

    // ── Viewport HUD font size (view label, stats, etc.) ──
    public float ViewportFontSize { get; set; } = 16f;

    // ── Object list (for hierarchy) ──
    public IReadOnlyList<GltfObject>? AllObjects { get; set; }

    // ── Scene texture (for Viewport panel) ──
    public uint SceneTextureID { get; set; }
    public int SceneTextureWidth { get; set; }
    public int SceneTextureHeight { get; set; }

    // ── Viewport focus state (set by ViewportPanel each frame) ──
    public bool IsViewportFocused { get; set; }
    /// <summary>True when viewport input should be suppressed (popup/menu was just open).
    /// Set by ViewportPanel after detecting a popup close, cleared after a few frames.</summary>
    public bool SuppressViewportInput { get; set; }

    /// <summary>True when the mouse is over a scrollable Placeholder — camera should NOT consume scroll.</summary>
    public bool ScrollCapturedByUI { get; set; }

    // ── Viewport mouse/click state for click-to-select in IDE mode ──
    /// <summary>Set by ViewportPanel when the scene image is clicked.</summary>
    public bool IsViewportClicked { get; set; }

    // ── Terrain brush sculpting (PBR panel ↔ viewport paint) ──
    /// <summary>The plane whose terrain brush-sculpt session is ACTIVE (set by
    /// the PBR panel's "Terrain Sculpt" toggle). Null = brush disabled everywhere.</summary>
    public EditorObject? TerrainSculptObject { get; set; }
    /// <summary>Plane-local X/Z (world units from the plane center) of the cursor
    /// hit on the sculpt target's surface — null when the ray misses the terrain.</summary>
    public Vector2? TerrainBrushLocalXZ { get; set; }
    /// <summary>World-space surface point under the brush cursor (ring gizmo anchor).</summary>
    public Vector3? TerrainBrushWorldHit { get; set; }
    /// <summary>True while a brush stroke is being painted (LMB held on terrain).</summary>
    public bool TerrainBrushPainting { get; set; }
    /// <summary>True when the brush cursor sits on the REAL displaced surface
    /// (paint possible); false = flat-grid fallback position (display only —
    /// the ring renders dimmed so the user sees WHY painting is not happening).</summary>
    public bool TerrainBrushOnSurface { get; set; }

    // ── Terrain splat painting (Terrain panel ↔ viewport paint) ──
    /// <summary>The plane whose splat paint session is ACTIVE (set by the Terrain
    /// panel's paint toggle). Null = no splat painting anywhere. Mutually exclusive
    /// with <see cref="TerrainSculptObject"/> — enabling one disables the other.</summary>
    public EditorObject? TerrainSplatObject { get; set; }
    /// <summary>Scene-space pixel X of the viewport click.</summary>
    public float ViewportClickX { get; set; }
    /// <summary>Scene-space pixel Y of the viewport click.</summary>
    public float ViewportClickY { get; set; }
    /// <summary>Scene-space pixel X of the current mouse position over the viewport (set each frame).</summary>
    public float ViewportMouseX { get; set; }
    /// <summary>Scene-space pixel Y of the current mouse position over the viewport (set each frame).</summary>
    public float ViewportMouseY { get; set; }

    // ── F9 toggle — when false, game input is blocked ──
    public bool InGameActive { get; set; }

    // ── True while a MENU owns the screen (MainMenuScene / LoadingScene). Refreshed
    // every frame by SceneManager from the active scene type. Player2DSystem freezes the
    // 2D side-scroller's gameplay keys (A/D/Shift/Space + bound action keys) while this is
    // true: in 3D there is no player to react, so a menu that is up must leave the world
    // static in 2D too. Physics/animation keep running — only input is frozen.
    public bool MenuOwnsInput { get; set; }

    // ── Static sprite-clip registry: lets engine-side objects (Player2D EditorObject)
    // resolve the Sprite Editor's sheets/clips by name without a panel reference.
    // Refreshed every frame by SpriteEditorPanel.Render (cheap dictionary fill).
    // Both registries are CASE-INSENSITIVE: trigger/dialogue action params are free-text
    // typed by the designer ("Cooking Area" vs the registered "Cooking area") — an exact-
    // case dictionary made those swaps resolve to nothing and silently fall back to the
    // base art (log said success, sprite never changed).
    private static readonly Dictionary<string, (SpriteSheet sheet, uint texId, int imgW, int imgH)> _spriteSheets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string sheet, string clip), AnimationClip2D> _spriteClips = new();

    /// <summary>Bumped on every SyncSpriteRegistry — sprite-data caches (e.g. the
    /// equipment auto-clip match) invalidate themselves by comparing this.</summary>
    public static int SpriteRegistryVersion { get; private set; }

    /// <summary>Bumped whenever a preview/atlas texture is RELOADED IN PLACE (a rebake
    /// rewrites the SAME PNG path). Part of the sync signature: without it a rebake
    /// whose new GL texture ID recycles the OLD id number produces a signature
    /// (name+path+id) identical to the previous bake — the registry keeps the stale
    /// entry and every viewport consumer keeps drawing the old pixels. Bump this and
    /// the signature MUST change, forcing a rebuild with the new texture.</summary>
    public static int SpriteTextureGeneration { get; private set; }

    /// <summary>Call after reloading a sprite texture under an existing path/atlas name
    /// (SpriteEditorPanel.ReloadPreviewTexture). Invalidates the signature gate and
    /// every registry consumer so the next sync rebuilds with the new texture.</summary>
    public static void NotifySpriteTextureReloaded()
    {
        SpriteTextureGeneration++;
        // A re-bake rewrites `<sheet>Normal.png` too; the 2D lighting cache must re-read it.
        Visual.SpriteLighting2D.InvalidateNormalMaps();
    }

    /// <summary>Auto-clip-match cache: (overlay sheet, base animation name) → the clip
    /// on that sheet which follows the base animation (null = none). The equipment
    /// renderer asks per layer per FRAME, so the scan must not re-run every frame.</summary>
    private static readonly Dictionary<(string sheet, string baseName), (AnimationClip2D? clip, int ver)> _autoClipMatchCache = new();

    /// <summary>Replace the static registry contents (called by SpriteEditorPanel every
    /// frame with its live sheet/clip lists + preview textures).</summary>
    public static void SyncSpriteRegistry(
        IEnumerable<SpriteSheet> sheets, Dictionary<string, uint> textures,
        IEnumerable<AnimationClip2D> clips)
    {
        // ── CHANGE-DTECTION GATE (per-frame sync fix) ──
        // SyncRegistry() runs EVERY frame (even with the Sprite Editor hidden), and the
        // old code unconditionally cleared + rebuilt the dictionaries, invalidated the
        // auto-match cache and bumped SpriteRegistryVersion. That made EVERY cached
        // clip match die after one frame → every animated sprite re-ran GetClipNames
        // (LINQ + sort + List alloc) per frame per layer — FPS dropped in proportion to
        // the number of sprite animations. Now we compute an allocation-light signature
        // (names + texture ids) and only rebuild when it actually changed.
        int sig = 17;
        foreach (var s in sheets)
        {
            sig = sig * 31 + s.Name.GetHashCode(StringComparison.OrdinalIgnoreCase);
            sig = sig * 31 + (s.ImagePath?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0);
        }
        foreach (var c in clips)
        {
            sig = sig * 31 + c.Name.GetHashCode(StringComparison.OrdinalIgnoreCase);
            sig = sig * 31 + (c.SpriteSheetName?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0);
        }
        // Texture ids are part of the signature: a re-imported image gets a NEW texId
        // under the same path — the registry must refresh so Player2D picks it up.
        foreach (var kv in textures)
            sig = sig * 31 + kv.Key.GetHashCode(StringComparison.OrdinalIgnoreCase)
                + (int)kv.Value;
        // Texture-generation stamp beats the texId-recycling trap: a rebake can recycle its
        // previous texture ID number, which would make the signature above unchanged
        // while the pixels changed. The generation bumps on EVERY in-place reload.
        sig = sig * 31 + SpriteTextureGeneration;

        if (sig == _lastRegistrySig)
            return; // nothing changed — keep dictionaries + caches warm
        _lastRegistrySig = sig;

        _spriteSheets.Clear();
        foreach (var s in sheets)
        {
            // The preview cache keys on the RESOLVED absolute path; sheets whose image
            // path is stored project-relative need the same resolution here or the
            // in-game (Player2D) lookup would miss the texture (tex = 0 → no art).
            uint tex = 0;
            if (!string.IsNullOrEmpty(s.ImagePath))
            {
                if (!textures.TryGetValue(s.ImagePath, out tex))
                    textures.TryGetValue(DarkEngine3D_gl_csharp.Engine.Helpers.PathHelpers.ResolveProjectAsset(s.ImagePath), out tex);
            }
            _spriteSheets[s.Name] = (s, tex, s.ImageWidth, s.ImageHeight);
        }
        _spriteClips.Clear();
        foreach (var c in clips)
            _spriteClips[(c.SpriteSheetName, c.Name)] = c;
        _autoClipMatchCache.Clear(); // sheet/clip lists changed — cached matches are stale
        _clipNamesCache.Clear();     // per-sheet name lists are stale too
        SpriteRegistryVersion++;
    }

    /// <summary>Signature of the last SyncSpriteRegistry content (11 = initial empty
    /// state) — the per-frame sync early-exits when the content is unchanged.</summary>
    private static int _lastRegistrySig = 11;

    /// <summary>Find the clip on <paramref name="sheetName"/> that follows the base
    /// animation <paramref name="baseName"/>: an EXACT name match first (case-insensitive),
    /// then the ACTION-TOKEN match — the '-'-suffix segment, so 'baju-run' matches both a
    /// base clip 'run' and a pattern-style base 'hero-run' (tokens: run == run). Used by
    /// the equipment sprite layers so an outfit authored as &lt;sheet&gt;-idle/-walk/-run
    /// (Save As Pattern naming) auto-follows every base animation without hand-assigning
    /// one clip per action. Cached per (sheet, baseName) + registry version.</summary>
    public static AnimationClip2D? FindAnimationClipForBase(string sheetName, string baseName)
    {
        if (string.IsNullOrEmpty(sheetName) || string.IsNullOrEmpty(baseName)) return null;
        var key = (sheetName, baseName);
        if (_autoClipMatchCache.TryGetValue(key, out var cached) && cached.ver == SpriteRegistryVersion)
            return cached.clip;

        AnimationClip2D? found = null;
        var names = GetClipNames(sheetName); // sorted, allocations acceptable: cached below
        // Pass 1: exact name.
        foreach (var n in names)
            if (string.Equals(n, baseName, StringComparison.OrdinalIgnoreCase))
            {
                TryGetSpriteClip(sheetName, n, out _, out found);
                break;
            }
        // Pass 2: action-token match ('<something>-<action>' vs '<something else>-<action>').
        if (found == null)
        {
            string baseToken = ActionToken(baseName);
            foreach (var n in names)
                if (string.Equals(ActionToken(n), baseToken, StringComparison.OrdinalIgnoreCase))
                {
                    TryGetSpriteClip(sheetName, n, out _, out found);
                    break;
                }
        }
        if (found != null && found.FrameIndices.Count == 0) found = null; // empty clip = no match

        _autoClipMatchCache[key] = (found, SpriteRegistryVersion);
        return found;
    }

    /// <summary>Find the selected overlay family clip for the active base action.
    /// For example, selected "Male Hair1-idle" plus base "Player2D-run" resolves to
    /// "Male Hair1-run" on the same sheet. Returns null when that action clip is absent.</summary>
    public static AnimationClip2D? FindAnimationClipForAction(string sheetName, string selectedClipName, string baseName)
    {
        if (string.IsNullOrEmpty(sheetName) || string.IsNullOrEmpty(selectedClipName)
            || string.IsNullOrEmpty(baseName)) return null;

        int selectedDash = selectedClipName.LastIndexOf('-');
        if (selectedDash <= 0 || selectedDash == selectedClipName.Length - 1) return null;

        string action = ActionToken(baseName);
        if (string.IsNullOrEmpty(action)) return null;

        string matchingName = $"{selectedClipName[..selectedDash]}-{action}";
        return TryGetSpriteClip(sheetName, matchingName, out _, out var matchingClip)
            && matchingClip != null && matchingClip.FrameIndices.Count > 0
                ? matchingClip
                : null;
    }

    /// <summary>The action token of a clip name: everything after the LAST '-' (a name
    /// without '-' is its own token). 'baju-run' → 'run', 'hero-walk-cycle' → 'cycle'.</summary>
    private static string ActionToken(string clipName)
    {
        int dash = clipName.LastIndexOf('-');
        return dash >= 0 && dash < clipName.Length - 1 ? clipName[(dash + 1)..] : clipName;
    }

    // Backing sprite registry accessor used by the runtime/editor to map sheet names to known texture ids.
            // The SpriteEditor updates this registry when sheets change; callers in draw code should use this
            // fast-path when the editor has already loaded the texture into the runtime entry.
            public static bool TryGetSpriteSheetTexture(string sheetName, out uint texId, out int imgW, out int imgH)
            {
                texId = 0; imgW = 0; imgH = 0;
                if (sheetName != null && _spriteSheets.TryGetValue(sheetName, out var entry))
                {
                    texId = entry.texId; imgW = entry.imgW; imgH = entry.imgH;
                    return true;
                }
                return false;
            }

    public static List<string> GetSpriteSheetNames()
        => _spriteSheets.Keys.OrderBy(k => k).ToList();

    /// <summary>Number of animation clips currently registered by the Sprite Editor
    /// (used by the Asset Browser to decide whether to show the clip boxes).</summary>
    public static int SpriteClipCount => _spriteClips.Count;

    /// <summary>Enumerate all registered clips with their sheet names (for the Asset
    /// Browser's clip boxes). Ordered by sheet then clip name for stable UI.</summary>
    public static IEnumerable<(string Sheet, string Clip)> GetSpriteClipPairs()
        => _spriteClips.Keys.Select(k => (k.sheet, k.clip)).OrderBy(p => p.sheet).ThenBy(p => p.clip);

    /// <summary>Request a Sprite2D placement from the Asset Browser (clip box click or
    /// viewport drop). Handler lives in IDE: (sheetName, clipName, dropWorldPos — null
    /// when the caller has no world position, e.g. a plain click → spawn at camera).</summary>
    public static Action<string, string, Vector3?>? RequestSprite2DPlacement { get; set; }

    /// <summary>Request the Animation Editor to open AT a clip (sheetName, clipName) —
    /// the Asset Browser's clip boxes, the Inspector's clip combo and action rows call
    /// this instead of knowing the panel. The handler lives in IDE; <paramref name="sheetName"/>
    /// may be "" when the caller only knows the clip name. One navigation entry point
    /// ends the "edit a clip = hunt across panels" loop.</summary>
    public static Action<string, string>? RequestOpenAnimationClip { get; set; }

    /// <summary>Safe wrapper for <see cref="RequestOpenAnimationClip"/>: no-op before the
    /// IDE wires the handler, and an empty clip name never opens an arbitrary clip.</summary>
    public static void OpenClipInAnimationEditor(string? sheetName, string? clipName)
    {
        if (string.IsNullOrWhiteSpace(clipName)) return;
        RequestOpenAnimationClip?.Invoke(sheetName ?? "", clipName);
    }

    /// <summary>Per-sheet clip-name list cache. GetClipNames is called from UI code
    /// and from the per-frame sprite/equipment resolve paths; with many animations the
    /// old per-call LINQ + OrderBy + ToList turned into per-frame GC churn. Cached per
    /// sheet and versioned by SpriteRegistryVersion — invalidated automatically when
    /// the registry rebuilds.</summary>
    private static readonly Dictionary<string, (List<string> names, int ver)> _clipNamesCache = new(StringComparer.OrdinalIgnoreCase);

    public static List<string> GetClipNames(string sheetName)
    {
        if (sheetName == null) return [];
        if (_clipNamesCache.TryGetValue(sheetName, out var hit) && hit.ver == SpriteRegistryVersion)
            return hit.names;

        var names = _spriteClips.Where(kv => string.Equals(kv.Key.sheet, sheetName, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value.Name).OrderBy(n => n).ToList();
        _clipNamesCache[sheetName] = (names, SpriteRegistryVersion);
        return names;
    }

    public static bool TryGetSpriteClip(string sheetName, string clipName, out SpriteSheet? sheet, out AnimationClip2D? clip)
    {
        sheet = null; clip = null;
        if (_spriteClips.TryGetValue((sheetName, clipName), out var c))
        {
            clip = c;
            if (_spriteSheets.TryGetValue(sheetName, out var entry))
                sheet = entry.sheet;
            return true;
        }
        // Case-insensitive fallback (the clip dict itself keeps exact keys): free-text
        // action params must resolve to the registered spelling — 'Cooking Area|Alat
        // Masak 1' finds the registered 'Cooking area|Alat Masak 1'.
        foreach (var kv in _spriteClips)
        {
            if (string.Equals(kv.Key.sheet, sheetName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(kv.Key.clip, clipName, StringComparison.OrdinalIgnoreCase))
            {
                clip = kv.Value;
                if (_spriteSheets.TryGetValue(kv.Key.sheet, out var entry))
                    sheet = entry.sheet;
                return true;
            }
        }
        return false;
    }

    /// <summary>Resolve a sheet object by name without needing a clip (used by the
    /// portal renderer's full-sheet fallback for sheets with no authored clips).</summary>
    public static bool TryGetSpriteSheet(string sheetName, out SpriteSheet? sheet)
    {
        sheet = null;
        if (sheetName != null && _spriteSheets.TryGetValue(sheetName, out var entry))
        {
            sheet = entry.sheet;
            return true;
        }
        return false;
    }

    /// <summary>First registered clip name on a sheet (registry order). Used by the
    /// portal display-state fallback when the portal's sheet has clips but none were
    /// assigned to the Active/NotActive states.</summary>
    public static bool TryGetFirstSpriteClipName(string sheetName, out string clipName)
    {
        clipName = "";
        foreach (var kv in _spriteClips)
        {
            if (string.Equals(kv.Key.sheet, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                clipName = kv.Key.clip;
                return true;
            }
        }
        return false;
    }

    /// <summary>Get the player spawn point for the active map, if one was placed in the
    /// Map Editor. Returns false when no map is active or no spawn was set — gameplay
    /// should fall back to its default spawn behavior in that case. The returned world
    /// position is upright (x horizontal, y height above the map's bottom edge, z = 0),
    /// matching the 2D level plane orientation.</summary>
    public bool TryGetPlayerSpawn(out System.Numerics.Vector3 worldPos)
    {
        worldPos = default;
        if (ActiveTilemap == null || !ActiveTilemap.HasPlayerSpawn) return false;
        worldPos = new System.Numerics.Vector3(
            ActiveTilemap.PlayerSpawn.X, ActiveTilemap.PlayerSpawn.Y, 0f);
        return true;
    }

    /// <summary>Get the player spawn point for the active map, if one was placed in the
    /// Map Editor. Returns false when no map is active or no spawn was set — gameplay
    /// should fall back to its default spawn behavior in that case.</summary>
    /// <param name=
    // ── Preview mode (F5) — hides editor gizmos/helpers without changing camera behavior ──
    public bool IsPreviewMode { get; set; }
    // ── In-game option: when true, mouse cursor stays visible during in-game mode ──
    public bool ShowCursorInGame { get; set; } = false;

    // ── Editor debug grid toggle (shown in the viewport while editing) ──
    public bool ShowDebugGrid { get; set; } = true;

    // ── Editor selection outline for UI elements (cyan wireframe + brackets). Global
    // editor preference (IDE Settings → Interface); resize handles are unaffected. ──
    public bool ShowSelectionBorder { get; set; } = true;

    // ── In-game stats overlay toggle (FPS/TRIS/Objects panel while playing) ──
    public bool ShowInGameStats { get; set; } = true;

    // ── Player2D 1 Hz collision telemetry toggle (Console panel [P2D-Tel] lines) ──
    public bool Player2DTelemetry { get; set; } = false;

    // ── Editor viewport shadows toggle (CSM on/off for editor objects) ──
    public bool ShowShadows { get; set; } = true;

    // ── Action to select object ──
    public Action<int>? SelectObjectByIndex { get; set; }
    public Action? FocusCameraOnSelected { get; set; }

    // ── Undo/Redo integration ──
    // Called by ViewportPanel when a drag operation ends to record undo for position/size change.
    // Parameters: (element, oldX, oldY, oldW, oldH, newX, newY, newW, newH)
    public Action<UIElement, float, float, float, float, float, float, float, float>? RecordTransformUndo { get; set; }
    /// <summary>Called by InspectorPanel when a color property changes.
    /// Parameters: (element, propertyName, oldColor, newColor)</summary>
    public Action<UIElement, string, Vector4, Vector4>? RecordColorUndo { get; set; }

    // ── Save integration (HierarchyPanel → SceneManagerPanel) ──
    /// <summary>Called by HierarchyPanel to save ALL editor scenes (triggers SceneManager's Save All).</summary>
    public Action? SaveAllScenes { get; set; }
    /// <summary>Force-save all editor scenes to game.ing specifically (for in-game mode entry).</summary>
    public Action? SaveToGameIng { get; set; }
    /// <summary>Called by HierarchyPanel to open the Save As file dialog (first save or redirect).</summary>
    public Action? RequestSaveAsDialog { get; set; }
    /// <summary>The currently active .ing file path. Set by SceneManagerPanel, read by all panels.</summary>
    public string? ActiveSaveFile { get; set; }

    // ── Scene Manager (for SceneManagerPanel to switch scenes) ──
    public SceneManager? SceneManager { get; set; }

    // ── Active Tilemap (for 2D map editor) ──
    public Tilemap2D? ActiveTilemap { get; set; }
    /// <summary>Multi-tilemap: adopted tilemaps WITHOUT a Map2D object of their own
    /// (their Map2D was created outside the current scene). Kept so the Map Editor
    /// selector can still list/switch/delete them. May be null.</summary>
    public List<Tilemap2D>? Tilemaps { get; set; }
    public int ActiveTileLayer { get; set; } = 1;
    public int SelectedTileId { get; set; } = 0;
    public int TilePaletteSelectionCount { get; set; } = 0;
    public List<int> TilePaletteSelectedTiles { get; set; } = new();
    // Width/height of the selected palette REGION (row-major grid in TilePaletteSelectedTiles,
    // row 0 = top row). Keeps a dragged block's shape when stamped into the grid.
    public int TilePaletteSelW { get; set; } = 1;
    public int TilePaletteSelH { get; set; } = 1;
    public int MapPaintTool { get; set; } = 0; // 0=Paint, 1=Erase, 2=Fill, 3=Pick, 4=Trigger
    public float MapPaintHeight { get; set; } = 0f;

    // ── Trigger Area editing (viewport ↔ Map Editor panel) ──
    /// <summary>Trigger currently selected in the Map Editor's Triggers list. The
    /// viewport highlights it and routes drags/resize/Delete/Ctrl+D to it.</summary>
    public Visual.TilemapTriggerArea? SelectedTrigger { get; set; }
    /// <summary>World position of the map tile under the mouse (0 = none). Updated by
    /// ViewportPanel every frame while a visible map is hovered — consumed by the DoF
    /// focus tracker's "Hovered Tile" mode.</summary>
    public System.Numerics.Vector3? HoveredMapTileWorld { get; set; }
    /// <summary>Hit point on the object under the mouse (0 = none). Updated by
    /// ViewportPanel — consumed by the DoF focus tracker's "Hovered Object" mode.</summary>
    public System.Numerics.Vector3? HoveredEditorObjectWorld { get; set; }
    // Tilemap painting hooks (wired by IDE.cs to MapEditorPanel).
    public Action<Vector2>? MapPaintAt { get; set; }
    public Action<Vector2>? MapFillAt { get; set; }
    public Action<Vector2>? MapPickAt { get; set; }
    /// <summary>Tile paint/erase undo-redo (wired to MapEditorPanel.UndoTilePaint/Redo).</summary>
    public Action? MapUndo { get; set; }
    public Action? MapRedo { get; set; }
    /// <summary>Undo-history plumbing for 3D/2D editor-object Add / Delete / Duplicate.
    /// HierarchyPanel owns the shared history; the panels that mutate the object list
    /// call Record* through these (kept null-safe at every call site).</summary>
    public Action<EditorObject>? RecordEditorObjectAdded { get; set; }
    public Action<EditorObject>? RecordEditorObjectDeleted { get; set; }
    public Action<EditorObject, EditorObject>? RecordEditorObjectDuplicated { get; set; }
    /// <summary>Trigger-area undo (create = Index -1 / delete = captured index) —
    /// HierarchyPanel owns the shared history; MapEditor/Viewport record through these.</summary>
    public Action<TilemapTriggerArea, int>? RecordTriggerAdded { get; set; }
    public Action<TilemapTriggerArea>? RecordTriggerDeleted { get; set; }

    // ── Map Editor grid settings (synced to ViewportPanel) ──

    // ── 2D Map scene ownership ──

    /// <summary>
    /// Map2D (tilemap) renderer objects are gameplay content, so they may only live in a
    /// GameScene editor scene — never in a MainMenu/Loading scene. Call this whenever the
    /// active editor scene changes (it is cheap, so running it every editor frame is fine):
    /// any stray Map2D object in a non-GameScene is removed. A level is NEVER auto-created
    /// here — it only appears when the scene actually contains a Map2D object (restored
    /// from the .ing file or created by the Map Editor via New Map / Load / Add Layer).
    /// </summary>
    public void EnforceMapObjectSceneRule()
    {
        if (EditorObjectManager == null) return;

        bool isGameScene = SelectedEditorScene != null
            && EditorScenes.TryGetValue(SelectedEditorScene, out var editorScene)
            && editorScene.Type == SceneType.GameScene;

        if (isGameScene) return;

        // Tilemaps belong to gameplay only — drop leftovers from menu/loading scenes.
        var stray = new List<EditorObject>();
        foreach (var o in EditorObjectManager.Objects)
            if (o != null && o.PrimitiveType == EditorPrimitiveType.Map2D)
                stray.Add(o);
        foreach (var o in stray)
            EditorObjectManager.Remove(o);
    }

    /// <summary>Teleport every Player2D object back to the first visible Start2D marker
    /// (feet anchor), zeroing velocity + animation clock. Called when returning to edit
    /// mode from preview/in-game so the editor shows players at their spawn point, not
    /// wherever physics left them. No-op when no Start2D marker exists.</summary>
    public void ResetPlayersToStart2D()
    {
        var mgr = EditorObjectManager;
        if (mgr == null) return;

        EditorObject? start2d = null;
        foreach (var o in mgr.Objects)
        {
            if (o is { IsVisible: true, PrimitiveType: EditorPrimitiveType.Start2D }) { start2d = o; break; }
        }
        if (start2d == null) return;

        foreach (var o in mgr.Objects)
        {
            if (o is not { PrimitiveType: EditorPrimitiveType.Player2D }) continue;
            // Capsule-aware: Start2D marks where the COLLIDER stands (physics feet =
            // Position.Y + capsule offset), so place the object so the capsule — not
            // the sprite anchor — lands exactly on the marker.
            o.Position = new System.Numerics.Vector3(
                start2d.Position.X - o.Player2DCapsuleOffsetX,
                start2d.Position.Y - o.Player2DCapsuleOffsetY,
                start2d.Position.Z);
            o.Player2DVelocityY = 0f;
            o.Player2DVelocityX = 0f;
            o.Player2DAnimTime = 0f;
            // Clear the runtime animation state too — edit mode doesn't run
            // Player2DSystem.Update, so a stale Moving flag would keep the walk clip
            // playing in the editor if the user exited in-game while holding A/D.
            o.Player2DMoving = false;
            o.Player2DFacingRight = true;
            // Clear any in-progress action as well, so locomotion restarts cleanly
            // from the idle state after returning to edit mode. Grounded=true too —
            // edit mode has no physics, so the resolver must treat the player as
            // standing (otherwise it picks Jump and idle never plays in the viewport).
            o.Player2DActionTime = 0f;
            o.Player2DCurrentAction = "";
            o.Player2DGrounded = true;
            Console.WriteLine($"[Bridge] Player2D reset to Start ({o.Position.X:F1}, {o.Position.Y:F1})");
        }
    }

    // ── Editor Object Manager ──
    private EditorObjectManager? _editorObjectManager;
    /// <summary>Manages editor-placed 3D primitives (Plane, Box, Sphere, glb references).
    /// Auto-wires OnObjectSelected to keep SelectedEditorObject in sync.</summary>
    public EditorObjectManager? EditorObjectManager
    {
        get => _editorObjectManager;
        set
        {
            // Unwire old manager
            if (_editorObjectManager != null)
            {
                _editorObjectManager.OnObjectSelected -= OnManagerSelectionChanged;
                _editorObjectManager.OnObjectRemoved -= OnManagerObjectRemoved;
            }

            // If the manager instance changes (e.g. scene switch), the previous selection
            // belongs to the old manager's objects — clear it so no stale refs linger.
            if (_editorObjectManager != value)
            {
                _selectedEditorObject = null;
                SelectedEditorObjects.Clear();
            }

            _editorObjectManager = value;

            // Wire new manager
            if (_editorObjectManager != null)
            {
                _editorObjectManager.OnObjectSelected += OnManagerSelectionChanged;
                _editorObjectManager.OnObjectRemoved += OnManagerObjectRemoved;
            }
        }
    }

    /// <summary>Prune the multi-selection set when any object is removed from the manager
    /// (prevents stale references to disposed objects from lingering in the set).</summary>
    private void OnManagerObjectRemoved(EditorObject obj)
    {
        DeselectEditorObject(obj);
    }

    private void OnManagerSelectionChanged(EditorObject? obj)
    {
        _selectedEditorObject = obj;
        // Keep the multi-selection set in sync with the manager's primary selection
        if (obj == null)
            SelectedEditorObjects.Clear();
        else if (!SelectedEditorObjects.Contains(obj))
            SelectedEditorObjects.Add(obj);
    }

    private EditorObject? _selectedEditorObject;
    /// <summary>All currently selected editor-placed 3D objects (multi-select).
    /// The primary (last-clicked / gizmo-driven) selection is <see cref="SelectedEditorObject"/>.</summary>
    public HashSet<EditorObject> SelectedEditorObjects { get; set; } = [];

    /// <summary>True when any selected editor object is a Sky marker. Sky objects have no
    /// meaningful rotation/scale, so the gizmo is locked to Translate while one is selected.
    /// Used by the viewport gizmo setup, the gizmo-mode toolbar/menu, and SceneManager.</summary>
    public bool SelectionHasSky
    {
        get
        {
            foreach (var o in SelectedEditorObjects)
                if (o != null && o.PrimitiveType == EditorPrimitiveType.Sky)
                    return true;
            return false;
        }
    }

    /// <summary>Currently selected editor-placed 3D object (primary — drives the Inspector + gizmo).
    /// Setting this also syncs to EditorObjectManager.SelectedObject and keeps the multi-set in sync.
    /// Use <see cref="SelectEditorObject"/> for ctrl-additive selection instead of this setter.</summary>
    public EditorObject? SelectedEditorObject
    {
        get => _selectedEditorObject;
        set
        {
            _selectedEditorObject = value;
            // Keep the multi-set in sync: primary is always a member
            if (value == null)
                SelectedEditorObjects.Clear();
            else if (!SelectedEditorObjects.Contains(value))
                SelectedEditorObjects.Add(value);
            // Keep EditorObjectManager.SelectedObject in sync
            if (_editorObjectManager != null && _editorObjectManager.SelectedObject != value)
                _editorObjectManager.SelectedObject = value;
        }
    }

    /// <summary>True when Ctrl is held over the viewport (set each frame by ViewportPanel).
    /// Used by GameScene's click-to-select so ctrl-click adds to the multi-selection.</summary>
    public bool ViewportCtrlHeld { get; set; }
    /// <summary>True when Shift is held over the viewport (set each frame by ViewportPanel).
    /// Used by GameScene's click-to-select so shift-click also adds to the multi-selection.</summary>
    public bool ViewportShiftHeld { get; set; }

    /// <summary>Compute the single gizmo anchor position for the current selection.
    /// One object → its pivot override (or position). Multi-select → the average of all
    /// selected positions (group center), so ONE gizmo drives the whole group.
    /// Returns null when nothing is selected.</summary>
    public Vector3? GetEditorGizmoCenter()
    {
        if (SelectedEditorObjects.Count == 0) return null;
        if (SelectedEditorObjects.Count == 1)
        {
            var sole = SelectedEditorObjects.First();
            if (sole == null) return null;
            return sole.GizmoPivotOverride ?? sole.Position;
        }
        Vector3 sum = Vector3.Zero;
        int n = 0;
        foreach (var s in SelectedEditorObjects)
        {
            if (s == null) continue;
            sum += s.Position;
            n++;
        }
        return n > 0 ? sum / n : null;
    }

    /// <summary>Set the primary editor object selection. When <paramref name="additive"/> is true
    /// (Ctrl+Click) the object is added to the multi-selection set and becomes primary;
    /// otherwise the set is replaced with just this object.</summary>
    public void SelectEditorObject(EditorObject? obj, bool additive = false)
    {
        if (obj == null)
        {
            SelectedEditorObject = null; // clears set + manager sync
            return;
        }
        if (!additive)
        {
            SelectedEditorObjects.Clear();
            _selectedEditorObject = obj;
            SelectedEditorObjects.Add(obj);
            if (_editorObjectManager != null) _editorObjectManager.SelectedObject = obj;
        }
        else
        {
            if (!SelectedEditorObjects.Contains(obj)) SelectedEditorObjects.Add(obj);
            _selectedEditorObject = obj;
            if (_editorObjectManager != null) _editorObjectManager.SelectedObject = obj;
        }
    }


    /// <summary>Toggle an object in the multi-selection set (Ctrl+Click). If the removed object
    /// was the primary, the last remaining member becomes the new primary.</summary>
    public void ToggleEditorObjectSelection(EditorObject obj)
    {
        if (SelectedEditorObjects.Contains(obj))
        {
            SelectedEditorObjects.Remove(obj);
            if (_selectedEditorObject == obj)
            {
                _selectedEditorObject = SelectedEditorObjects.Count > 0 ? SelectedEditorObjects.Last() : null;
                if (_editorObjectManager != null) _editorObjectManager.SelectedObject = _selectedEditorObject;
            }
        }
        else
        {
            SelectedEditorObjects.Add(obj);
            _selectedEditorObject = obj;
            if (_editorObjectManager != null) _editorObjectManager.SelectedObject = obj;
        }
    }

    /// <summary>Remove a (deleted) object from the multi-selection set, updating the primary.
    /// Safe to call on objects that were never in the set.</summary>
    public void DeselectEditorObject(EditorObject obj)
    {
        if (!SelectedEditorObjects.Remove(obj)) return;
        if (_selectedEditorObject == obj)
        {
            _selectedEditorObject = SelectedEditorObjects.Count > 0 ? SelectedEditorObjects.Last() : null;
            if (_editorObjectManager != null) _editorObjectManager.SelectedObject = _selectedEditorObject;
        }
    }

/// <summary>Gizmo interaction mode: 0=Translate, 1=Rotate, 2=Scale.</summary>
    public int GizmoMode { get; set; } = 0;

    /// <summary>Shared TransformGizmo instance for viewport interaction.</summary>
    public TransformGizmo? EditorGizmo { get; set; }

    /// <summary>Compute a spawn position aligned to the editor grid for newly created objects.
    /// Z is forced to 0 and X is snapped to the nearest 1-unit grid column so primitives
    /// line up with the editor grid squares. Y is set so the object rests on the ground
    /// (0 for a plane, 0.5 for box/sphere so they sit on the grid plane, 1.5 for cameras at
    /// eye height, 3 for lights so they float above, 0 for sky markers).</summary>
    public static Vector3 GetGridSpawnPosition(Camera? cam, EditorPrimitiveType type)
    {
        // Spawn at the camera's freefly position so new objects appear exactly
        // where the user is looking (except UI which has its own placement logic).
        if (cam == null) return new Vector3(0f, 1f, 0f);

        // USER RULE: in 2D mode (orthographic camera) every newly added object lands on
        // the Z = 0 world plane. The ortho camera parks far on +Z looking back at the
        // level, so spawning "at the camera" would put new objects at Z ≈ 60 — in front
        // of the tilemap and off the 2D plane. X/Y still follow the camera so the object
        // appears where the user is looking.
        Vector3 spawn = cam.Position;
        if (cam.IsOrthographic) spawn.Z = 0f;
        return spawn;
    }

    /// <summary>Called by ViewportPanel when a gizmo drag ends (for undo support).
    /// Passes ALL EditorObjects that were actually dragged (multi-select aware,
    /// primary first) — not whatever is currently selected — so undo stays correct
    /// even if selection changed mid-drag.</summary>
    public Action<IReadOnlyList<EditorObject>>? OnGizmoDragEnded { get; set; }

    /// <summary>Called by ViewportPanel when a gizmo pivot is placed via middle-click
    /// (for undo support). Passes the object, the previous pivot override (may be null),
    /// and the new pivot override — consistent with OnGizmoDragEnded.
    /// HierarchyPanel records this as an undo/redo action so Ctrl+Z can revert pivot placement.</summary>
    public Action<EditorObject, Vector3?, Vector3?>? OnGizmoPivotChanged { get; set; }

    /// <summary>Called by ViewportPanel when a sky-sun gizmo drag ends (for undo support).
    /// Passes the sky object, the pitch/yaw BEFORE the drag (null = followed time of day),
    /// and the pitch/yaw AFTER. When a Light marker was placed it overrides the sky sun for
    /// actual lighting, so the drag keeps its direction in sync — the light object and its
    /// old/new world direction ride along so Ctrl+Z can revert both. HierarchyPanel records
    /// this as an undo/redo action.</summary>
    public Action<EditorObject, float?, float?, float?, float?, EditorObject?, Vector3?, Vector3?>? OnSkySunChanged { get; set; }

    // ── Viewport mouse state (tracked per frame for 3D gizmo interaction) ──
    /// <summary>True when the left mouse button is held down over the viewport.</summary>
    public bool IsViewportMouseDown { get; set; }
    /// <summary>True on the frame the left mouse button is released over the viewport.</summary>
    public bool IsViewportMouseReleased { get; set; }
    /// <summary>Mouse NDC X coordinate (-1 to 1) for ray casting.</summary>
    public float ViewportMouseNDCX { get; set; }
    /// <summary>Mouse NDC Y coordinate (-1 to 1) for ray casting.</summary>
    public float ViewportMouseNDCY { get; set; }

    // ── Selection highlight colors (configurable via Inspector) ──
    /// <summary>Contains the two configurable wireframe colors: GltfObject (gold) and EditorObject (cyan).</summary>
    public SelectionHighlightColors SelectionHighlights = SelectionHighlightColors.Default;
    /// <summary>Scene type — user picks this explicitly (no auto-detection).</summary>
    public enum SceneType
    {
        MainMenu,
        GameScene,
        Loading,
    }

    /// <summary>How a Game Scene plays — picked per scene entry in the Add/Edit Scene
    /// dialog (Scene Type = Game Scene only). Persisted in the .ing as a stable key
    /// (see <see cref="GameTypeToKey"/>) and shown in the Scene Manager "Game" column.
    /// A NEW 2D scene is created with the ortho front view; 2.5D/3D start perspective.</summary>
    public enum GameType
    {
        SideScroller2D,
        TopDown2D,
        Isometric25D,
        ThreeD,
    }

    /// <summary>Available scene descriptors for the SceneManagerPanel.
    /// DefaultTransition is an optional editor-configured transition spec (e.g. "fade:0.6" or "slide:left:0.5").
    /// GameType only applies to GameScene entries (menus/loading keep the default).</summary>
    public record SceneEntry(string Name, string Description, bool HasInitializedEntry, SceneType Type,
        TransitionType TransitionType, float TransitionDuration, float[] TransitionColor, string TransitionEasing, bool TransitionBlockInput,
        GameType GameType = GameType.ThreeD);

    /// <summary>Human-readable labels for the SceneType enum (for combo box).</summary>
    public static readonly string[] SceneTypeLabels =
        ["Main Menu", "Game Scene", "Loading Screen"];

    /// <summary>Human-readable labels for the GameType enum (Add/Edit Scene combo).</summary>
    public static readonly string[] GameTypeLabels =
        ["2D SideScroller", "2D Top Down", "2.5D", "3D"];

    /// <summary>Short badge for the Scene Manager "Game" column.</summary>
    public static string GameTypeShortLabel(GameType type) => type switch
    {
        GameType.SideScroller2D => "2D Side",
        GameType.TopDown2D => "2D Top",
        GameType.Isometric25D => "2.5D",
        _ => "3D",
    };

    /// <summary>True for the two flat/orthographic game types (2D SideScroller + 2D Top Down).</summary>
    public static bool Is2DGameType(GameType type)
        => type is GameType.SideScroller2D or GameType.TopDown2D;

    /// <summary>Stable key written to the .ing — do NOT localize or rename these.</summary>
    public static string GameTypeToKey(GameType type) => type switch
    {
        GameType.SideScroller2D => "2d_sidescroller",
        GameType.TopDown2D => "2d_topdown",
        GameType.Isometric25D => "2.5d",
        _ => "3d",
    };

    /// <summary>Parse a saved key. A missing/unknown key (old .ing files) falls back to the
    /// scene's saved camera: orthographic = 2D SideScroller, otherwise 3D — so legacy
    /// files keep their meaning without being re-saved.</summary>
    public static GameType ParseGameType(string? key, bool? editorCameraOrtho = null)
    {
        switch ((key ?? "").Trim().ToLowerInvariant()
                    .Replace("_", "").Replace("-", "").Replace(" ", ""))
        {
            case "2dsidescroller":
            case "2dside":
            case "sidescroller":
                return GameType.SideScroller2D;
            case "2dtopdown":
            case "topdown":
                return GameType.TopDown2D;
            case "2.5d":
            case "25d":
            case "isometric":
                return GameType.Isometric25D;
            case "3d":
                return GameType.ThreeD;
            default:
                return editorCameraOrtho == true ? GameType.SideScroller2D : GameType.ThreeD;
        }
    }

    /// <summary>List of all registered scenes and whether they have been initialized (current or next).
    /// Starts empty — user adds scenes via the Scene Manager panel.</summary>
    public IReadOnlyList<SceneEntry> AvailableScenes => _availableScenes;
    internal List<SceneEntry> _availableScenes = [];
    /// <summary>Internal mutable list for adding/removing scenes (SceneManagerPanel uses this).</summary>
    public List<SceneEntry> AvailableScenesInternal => _availableScenes;

    // ── UI Editor scene data (managed by IDE, not by game scenes) ──

    /// <summary>An editor scene with its own UIElement tree root and optional 3D objects.</summary>
    public record EditorScene(string Name, SceneType Type, UIElement Root)
    {
        /// <summary>3D editor objects specific to this scene (Plane, Box, Sphere, etc.).</summary>
        public EditorObjectManager? ObjectManager { get; set; }

        /// <summary>Per-scene render properties (background color, VSync, face culling, wireframe, etc.).</summary>
        public SceneRenderProperties? RenderProperties { get; set; }

        // ── Per-scene freefly camera (each scene remembers its own view) ──
        /// <summary>Saved freefly camera position (null = use default view).</summary>
        public Vector3? CameraPos { get; set; }
        /// <summary>Saved freefly camera yaw (degrees).</summary>
        public float? CameraYaw { get; set; }
        /// <summary>Saved freefly camera pitch (degrees).</summary>
        public float? CameraPitch { get; set; }
        /// <summary>Saved projection mode (null = keep current). 2D level scenes are
        /// viewed orthographic — the per-scene restore must re-apply that too.</summary>
        public bool? CameraOrtho { get; set; }
        /// <summary>Saved ortho half-height (zoom). Null = keep current.</summary>
        public float? CameraOrthoSize { get; set; }
    }

    /// <summary>All scenes created/managed by the UI Editor. Keyed by scene name.</summary>
    public Dictionary<string, EditorScene> EditorScenes { get; } = [];

    /// <summary>Global default transition settings used when no per-scene default is set.</summary>
    public TransitionType DefaultTransitionType { get; set; } = TransitionType.Fade;
    public float DefaultTransitionDuration { get; set; } = 0.6f;
    public float[] DefaultTransitionColor { get; set; } = [0f, 0f, 0f];
    public string DefaultTransitionEasing { get; set; } = "linear";
    public bool DefaultTransitionBlockInput { get; set; } = false;

    private string? _selectedEditorScene;

    /// <summary>Name of the currently selected editor scene (displayed in SceneDetail).
    /// Setting this stashes the live freefly camera into the outgoing scene and restores
    /// the incoming scene's saved view, so every scene keeps its own camera.</summary>
    public string? SelectedEditorScene
    {
        get => _selectedEditorScene;
        set
        {
            if (string.Equals(value, _selectedEditorScene, StringComparison.Ordinal))
                return;

            // Stash the live editor camera into the outgoing scene before switching
            if (_selectedEditorScene != null
                && EditorScenes.TryGetValue(_selectedEditorScene, out var prev)
                && Camera != null)
            {
                prev.CameraPos = Camera.Position;
                prev.CameraYaw = Camera.Yaw;
                prev.CameraPitch = Camera.Pitch;
                prev.CameraOrtho = Camera.IsOrthographic;
                prev.CameraOrthoSize = Camera.OrthoSize;
            }

            _selectedEditorScene = value;

            // Restore the incoming scene's saved camera (if any)
            if (value != null
                && EditorScenes.TryGetValue(value, out var next)
                && Camera != null
                && next.CameraPos is Vector3 p)
            {
                Camera.Position = p;
                Camera.Yaw = next.CameraYaw ?? Camera.Yaw;
                Camera.Pitch = next.CameraPitch ?? Camera.Pitch;
                if (next.CameraOrtho is bool savedOrtho) Camera.IsOrthographic = savedOrtho;
                if (next.CameraOrthoSize is float savedSize) Camera.OrthoSize = savedSize;
                Camera.UpdateVectors();
                Camera.SyncSmoothVectors();
            }
        }
    }

    /// <summary>True when the ACTIVE editor scene is a GameScene type. Gates the
    /// gameplay HUD (inventory/quick slots, shop, quest) so MainMenu/Loading scenes
    /// never show gameplay UI in preview or in-game (user: "quick slot and
    /// inventory only in its GameScene type").</summary>
    public bool IsGameSceneSelected
    {
        get
        {
            return SelectedEditorScene != null
                && EditorScenes.TryGetValue(SelectedEditorScene, out var editorScene)
                && editorScene.Type == SceneType.GameScene;
        }
    }

    /// <summary>Get the currently selected editor scene's root element, or null.</summary>
    public UIElement? GetSelectedEditorRoot()
    {
        if (SelectedEditorScene == null) return null;
        if (EditorScenes.TryGetValue(SelectedEditorScene, out var editorScene))
            return editorScene.Root;
        return null;
    }

    /// <summary>Get overlay (Dialog/Container) names from the current scene root.</summary>
    public static string[] GetOverlayNamesFromScene(UIElement? sceneRoot)
    {
        if (sceneRoot == null) return [];
        var names = new List<string>();
        foreach (var child in sceneRoot.Children)
        {
            if (child.Type == UIElementType.Container)
                names.Add(child.Name);
        }
        return [.. names];
    }

    // ── Available Behaviors (for Inspector panel combo box) ──

    /// <summary>A behavior option with display label and stored value.</summary>
    public record BehaviorOption(string Label, string Value);

    /// <summary>Available behavior types for UI elements.</summary>
    public static readonly BehaviorOption[] AvailableBehaviors =
    [
        new BehaviorOption("(none)", ""),
        new BehaviorOption("Open / Close Overlay", "overlay"),
        new BehaviorOption("Close Current Overlay", "closeoverlay"),
        new BehaviorOption("Go to Scene", "scene"),
        new BehaviorOption("Exit", "exit"),
    ];

    /// <summary>Available scene names for scene action type.</summary>
    public string[] AvailableSceneNames =>
        _availableScenes.Select(s => s.Name).ToArray();

    /// <summary>Parse a ClickBehaviorLabel into (typeValue, param).
    /// e.g. "overlay:ExitConfirm" → ("overlay", "ExitConfirm")
    ///      "scene:MainMenu" → ("scene", "MainMenu")
    ///      "exit" → ("exit", "")
    ///      "" → ("", "")</summary>
    public static (string type, string param) ParseBehavior(string clickBehaviorLabel)
    {
        if (string.IsNullOrEmpty(clickBehaviorLabel))
            return ("", "");

        int colon = clickBehaviorLabel.IndexOf(':');
        if (colon >= 0)
        {
            string type = clickBehaviorLabel[..colon];
            string param = clickBehaviorLabel[(colon + 1)..];
            return (type.ToLowerInvariant(), param);
        }
        return (clickBehaviorLabel.ToLowerInvariant(), "");
    }

    /// <summary>Build a ClickBehaviorLabel from type and param.</summary>
    public static string BuildBehavior(string type, string param)
    {
        if (string.IsNullOrEmpty(type))
            return "";
        return string.IsNullOrEmpty(param) ? type : $"{type}:{param}";
    }

    /// <summary>Mark a scene as initialized (has been entered at least once).</summary>
    public void MarkSceneInitialized(string name)
    {
        for (int i = 0; i < _availableScenes.Count; i++)
        {
            if (_availableScenes[i].Name == name)
            {
                _availableScenes[i] = _availableScenes[i] with { HasInitializedEntry = true };
                return;
            }
        }
    }
}
