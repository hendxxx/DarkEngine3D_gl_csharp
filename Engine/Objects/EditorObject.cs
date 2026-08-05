using System.IO;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Libs;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects;

/// <summary>
/// Type of editor-placed 3D primitive.
/// </summary>
public enum EditorPrimitiveType
{
    Plane,
    Box,
    Sphere,
    GlbReference,
    Camera,
    Light,
    Sky
}

/// <summary>
/// Represents a user-placed 3D object in the editor scene.
/// Can be a Plane, Box, Sphere, or a reference to a .glb file.
/// Contains all properties needed for rendering, shadow casting, and gizmo interaction.
/// </summary>
public unsafe class EditorObject
{
    // ── Identity ──
    public string Name { get; set; } = "EditorObject";
    public EditorPrimitiveType PrimitiveType { get; set; } = EditorPrimitiveType.Box;

    // ── Transform ──
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 RotationEuler { get; set; } = Vector3.Zero; // degrees
    public Vector3 Scale { get; set; } = Vector3.One;

    // ── Visual ──
    public Vector3 Color { get; set; } = new(0.8f, 0.8f, 0.9f);
    public string? TexturePath { get; set; } = null;
    public bool CastShadow { get; set; } = true;
    public bool IsVisible { get; set; } = true;

    // ── glb reference (only used when PrimitiveType == GlbReference) ──
    public string? GlbFilePath { get; set; } = null;

    // ── Camera (only used when PrimitiveType == Camera) ──
    /// <summary>Vertical FOV in degrees for the placed camera.</summary>
    public float CameraFov { get; set; } = 60f;
    /// <summary>Near clip distance for the placed camera.</summary>
    public float CameraNear { get; set; } = 0.1f;
    /// <summary>Far clip distance for the placed camera.</summary>
    public float CameraFar { get; set; } = 500f;
    /// <summary>Whether the view-frustum wireframe gizmo is drawn in the viewport.</summary>
    public bool ShowFrustum { get; set; } = true;

    // ── Light (only used when PrimitiveType == Light) ──
    /// <summary>Direction the light points toward (world space, not normalized).</summary>
    public Vector3 LightDirection { get; set; } = new(-0.5f, 0.8f, -0.3f);
    /// <summary>Brightness multiplier for the light color.</summary>
    public float LightIntensity { get; set; } = 1f;
    /// <summary>Spotlight cone half-angle in degrees — used by the viewport light gizmo
    /// (and by the editor light sampling) to visualize the light's spread.</summary>
    public float LightConeAngle { get; set; } = 30f;
    /// <summary>Whether the direction-ray + spotlight-cone gizmo is drawn in the viewport.</summary>
    public bool ShowLightGizmo { get; set; } = true;

    // ── Terrain (only used when PrimitiveType == Plane && TerrainEnabled) ──
    /// <summary>When true, this Plane renders as an advanced heightmapped terrain instead
    /// of a flat plane. All terrain settings below are editable in the Inspector.</summary>
    public bool TerrainEnabled { get; set; } = false;
    /// <summary>Heightmap file (.raw 8-bit or any image). Determines the terrain shape.</summary>
    public string TerrainHeightmapPath { get; set; } = "Artifacts/Maps/photoreal_v1.raw";
    /// <summary>Grid resolution per side (4..256). Higher = more detail, more triangles.</summary>
    public int TerrainChunkSize { get; set; } = 32;
    /// <summary>Vertical exaggeration of the heightmap (world units for full white).</summary>
    public float TerrainHeightScale { get; set; } = 30f;
    /// <summary>Slope steepness (1 - normal.y) above which the dirt/rock layer takes over.</summary>
    public float TerrainSlopeThreshold { get; set; } = 0.35f;
    /// <summary>World-space tiling frequency of the layer textures.</summary>
    public float TerrainTexTiling { get; set; } = 0.5f;
    /// <summary>Normalized height where the air layer ends (water level).</summary>
    public float TerrainLayerAirTop { get; set; } = 0.18f;
    /// <summary>Normalized height where the dirt layer ends.</summary>
    public float TerrainLayerDirtTop { get; set; } = 0.45f;
    /// <summary>Normalized height where the grass layer ends (snow starts after).</summary>
    public float TerrainLayerGrassTop { get; set; } = 0.75f;
    /// <summary>Normalized height where the snow layer is fully dominant.</summary>
    public float TerrainLayerSnowTop { get; set; } = 1.0f;
    /// <summary>Texture for layer 1 — air / water.</summary>
    public string TerrainTextureAirPath { get; set; } = "";
    /// <summary>Texture for layer 2 — tanah / dirt.</summary>
    public string TerrainTextureDirtPath { get; set; } = "";
    /// <summary>Texture for layer 3 — rumput / grass.</summary>
    public string TerrainTextureGrassPath { get; set; } = "";
    /// <summary>Texture for layer 4 — salju / snow.</summary>
    public string TerrainTextureSnowPath { get; set; } = "";
    /// <summary>Brush radius in world units (viewport paint tool).</summary>
    public float TerrainBrushSize { get; set; } = 4f;
    /// <summary>Height delta per painted frame, in world units (viewport paint tool).</summary>
    public float TerrainBrushStrength { get; set; } = 0.1f;
    /// <summary>Brush edge falloff 0..1 (0 = hard edge, 1 = very soft).</summary>
    public float TerrainBrushSoftness { get; set; } = 0.6f;
    /// <summary>Layer painted with the 🎨 texture brush: 0=air, 1=tanah, 2=rumput, 3=salju.</summary>
    public int TerrainPaintLayerIndex { get; set; } = 2;
    /// <summary>Weight added to the painted layer per 🎨 brush stamp (0..1).</summary>
    public float TerrainPaintStrength { get; set; } = 0.45f;
    /// <summary>Brush falloff curve used by every brush tool: 0=Linear, 1=Smooth,
    /// 2=Sharp, 3=Spherical, 4=Soft.</summary>
    public int TerrainBrushFalloff { get; set; } = 1;
    /// <summary>Editor-only overlay: colorize the terrain by height (low=blue → high=red)
    /// with contour lines so the relief reads clearly. Transient — not saved to the scene.</summary>
    public bool TerrainShowHeatmap { get; set; } = false;
    /// <summary>Editor-only overlay: draw dark topographic contour lines every 10% height
    /// WITHOUT the heatmap colors — the terrain texture stays fully visible while the
    /// relief reads clearly. Transient — not saved to the scene.</summary>
    public bool TerrainShowContours { get; set; } = false;

    // ── Brush ring indicator (transient — set by ViewportPanel each frame while the
    // brush tool hovers this terrain; NOT serialized) ──
    /// <summary>World-space brush center on this terrain's surface (null = hide ring).</summary>
    public Vector3? BrushIndicatorPos { get; set; }
    /// <summary>Ring color: green = height brush, layer color = 🎨 paint, red = Ctrl (lower/erase).</summary>
    public Vector3 BrushIndicatorColor { get; set; } = new(0.3f, 0.9f, 0.5f);
    /// <summary>Whether the brush ring should be drawn on this terrain.</summary>
    public bool ShowBrushIndicator { get; set; }

    // ── Sky (only used when PrimitiveType == Sky) ──
    /// <summary>Time of day in hours (0..24). 12 = midday.</summary>
    public float SkyTimeOfDay { get; set; } = 12f;
    /// <summary>Sun elevation override in degrees (-90..90). null = follow SkyTimeOfDay.
    /// Setting this lets the user aim the sun independently of the time-of-day cycle.</summary>
    public float? SkySunPitch { get; set; } = null;
    /// <summary>Sun azimuth override in degrees (0..360). null = follow SkyTimeOfDay.</summary>
    public float? SkySunYaw { get; set; } = null;
    /// <summary>Cloud coverage/intensity 0..1 (drives the sky shader's weather mode).</summary>
    public float SkyCloudCoverage { get; set; } = 0.3f;
    /// <summary>Sun brightness multiplier (applied to the sky light color).</summary>
    public float SkySunIntensity { get; set; } = 1f;
    /// <summary>Time-of-day animation speed in hours per second (0 = static). While > 0 and
    /// not paused, SkyTimeOfDay advances automatically and the sun orbits the scene.</summary>
    public float SkyTimeAnimSpeed { get; set; } = 0f;
    /// <summary>Pause the time-of-day animation (keeps the current SkyTimeOfDay).</summary>
    public bool SkyTimeAnimPaused { get; set; } = false;
    /// <summary>Whether the horizon-circle + sun-icon gizmo is drawn in the viewport.</summary>
    public bool ShowSkyGizmo { get; set; } = true;

    // ── Internal rendering resources (lazy-init) ──
    private Object3D? _object3D;
    private uint _textureID = 0;
    private bool _dirty = true;

    // ── Advanced terrain resources (Plane only) ──
    private EditorTerrainMesh? _terrainMesh;
    private string _terrainCacheKey = "";
    /// <summary>Base64-encoded painted heightmap blob (persisted in the scene file so
    /// brush edits survive save/load). Empty = no user edits.</summary>
    private byte[]? _terrainPaintedCache;
    /// <summary>Heightmap path the painted cache belongs to — only restored when the
    /// terrain still points at the same heightmap (switching maps discards old edits).</summary>
    private string? _terrainPaintedSourcePath;
    /// <summary>Heightmap path the CURRENT mesh was actually built from. Used when
    /// stashing the painted cache on rebuild so the cache is keyed to the OLD path,
    /// not the (possibly changed) TerrainHeightmapPath property.</summary>
    private string _terrainLoadedPath = "";
    /// <summary>Serialized manual layer-paint blob (persisted in the scene file). Carried
    /// across mesh rebuilds independently of the heightmap path.</summary>
    private byte[]? _terrainSplatCache;

    // ── Vertex cache for wireframe outline rendering ──
    private Vertex[]? _vertexCache;

    // ── Gizmo state (snapshots for undo) ──
    public Vector3 LastGizmoPosition { get; set; }
    public Vector3 LastGizmoRotation { get; set; }
    public Vector3 LastGizmoScale { get; set; }

    // ── Gizmo pivot override snapshot (so undo/redo also restores the pivot position) ──
    /// <summary>Snapshot of <see cref="GizmoPivotOverride"/> taken at gizmo drag start,
    /// used by undo/redo to prevent the gizmo floating detached from the object.</summary>
    public Vector3? LastGizmoPivot { get; set; }

    // ── Per-object gizmo pivot override (set by middle-click in viewport) ──
    /// <summary>When set, the gizmo renders at this world position instead of the object's Position.
    /// Persists across selection changes — each object remembers its own pivot override.</summary>
    public Vector3? GizmoPivotOverride { get; set; }

    // ── Shader uniform locations (cached for Draw overloads) ──
    private int _modelLoc = -1, _viewLoc = -1, _projLoc = -1;
    private int _sunDirLoc = -1, _lightColorLoc = -1, _viewPosLoc = -1;
    private int _useFogLoc = -1, _fogColorLoc = -1;

    /// <summary>
    /// Create a new EditorObject with the given primitive type and optional name.
    /// </summary>
    public EditorObject(EditorPrimitiveType type, string? name = null)
    {
        PrimitiveType = type;
        if (name != null)
            Name = name;
        // Set reasonable defaults based on type
        Scale = type switch
        {
            EditorPrimitiveType.Plane => new Vector3(25f, 0.05f, 25f),
            EditorPrimitiveType.Camera => new Vector3(0.5f, 0.4f, 0.6f),
            _ => Vector3.One,
        };
        Color = type switch
        {
            EditorPrimitiveType.Plane => new Vector3(0.3f, 0.6f, 0.3f),
            EditorPrimitiveType.Box => new Vector3(0.8f, 0.4f, 0.2f),
            EditorPrimitiveType.Sphere => new Vector3(0.2f, 0.4f, 0.8f),
            EditorPrimitiveType.GlbReference => new Vector3(0.6f, 0.6f, 0.8f),
            EditorPrimitiveType.Camera => new Vector3(0.2f, 0.7f, 0.8f),
            EditorPrimitiveType.Light => new Vector3(1.0f, 0.85f, 0.3f),
            EditorPrimitiveType.Sky => new Vector3(0.5f, 0.7f, 1.0f),
            _ => new Vector3(0.8f, 0.8f, 0.9f),
        };
    }

    public Matrix4x4 WorldMatrix
    {
        get
        {
            return Matrix4x4.CreateScale(Scale)
                 * Matrix4x4.CreateFromYawPitchRoll(
                     RotationEuler.Y * MathF.PI / 180f,
                     RotationEuler.X * MathF.PI / 180f,
                     RotationEuler.Z * MathF.PI / 180f)
                 * Matrix4x4.CreateTranslation(Position);
        }
    }

    /// <summary>
    /// Apply the first placed Light + Sky marker settings to the given Lights/Skybox pair.
    /// Shared by SceneManager (editor viewport) and GameScene (in-game mode) so both render
    /// with the same sun direction, brightness, cloud coverage and time of day.
    /// </summary>
    /// <param name="lightObj">First Light marker (or null). Its direction/color/intensity win over the sky sun.</param>
    /// <param name="skyObj">First Sky marker (or null). Drives time of day, optional sun pitch/yaw override, cloud coverage, sun brightness.</param>
    /// <param name="lights">The Lights instance to mutate.</param>
    /// <param name="skybox">The Skybox to mutate (cloud override), may be null.</param>
    public static void ApplyEnvironmentMarkers(EditorObject? lightObj, EditorObject? skyObj, Lights lights, Skybox? skybox, float deltaTime)
    {
        // ── Light override: the Light marker's direction/color take priority over the sky sun ──
        if (lightObj != null)
        {
            lights.SunDirOverride = lightObj.LightDirection;
            lights.LightColorOverride = lightObj.Color;
            lights.LightIntensity = lightObj.LightIntensity;
        }
        else
        {
            lights.SunDirOverride = null;
            lights.LightColorOverride = null;
            lights.LightIntensity = 1f;
        }

        // ── Sky override: time of day + optional sun pitch/yaw + brightness ──
        if (skyObj != null)
        {
            // Time-of-day animation: while enabled (speed > 0) and not paused, advance the
            // stored SkyTimeOfDay so the sun orbits, the gizmo follows, the Inspector slider
            // moves and the saved scene keeps the current time. Paused/static pins the time.
            if (skyObj.SkyTimeAnimSpeed > 0f && !skyObj.SkyTimeAnimPaused)
            {
                skyObj.SkyTimeOfDay = (skyObj.SkyTimeOfDay + skyObj.SkyTimeAnimSpeed * deltaTime) % 24f;
                if (skyObj.SkyTimeOfDay < 0f) skyObj.SkyTimeOfDay += 24f;
            }

            float hours = Math.Clamp(skyObj.SkyTimeOfDay, 0f, 24f);
            lights.WorldTime = (hours / 24f) * (MathF.PI * 2f);

            // Optional sun pitch/yaw override: aim the sun independently of the time-of-day
            // cycle. Only applied when there is no Light object (the Light marker's direction
            // takes priority) and only when BOTH pitch and yaw are set (null keeps time-of-day).
            if (lightObj == null
                && skyObj.SkySunPitch.HasValue && skyObj.SkySunYaw.HasValue)
            {
                float pitch = skyObj.SkySunPitch.Value * MathF.PI / 180f;
                float yaw = skyObj.SkySunYaw.Value * MathF.PI / 180f;
                // Same convention as the camera: front = (sin yaw cos pitch, sin pitch, cos yaw cos pitch)
                var sunDir = new Vector3(
                    MathF.Sin(yaw) * MathF.Cos(pitch),
                    MathF.Sin(pitch),
                    MathF.Cos(yaw) * MathF.Cos(pitch));
                lights.SunDirOverride = Vector3.Normalize(sunDir);
            }

            // Sun brightness multiplier from the sky object
            lights.SunBrightness = Math.Max(0f, skyObj.SkySunIntensity);
        }
        else
        {
            // No sky object → restore default sun brightness
            lights.SunBrightness = 1f;
        }

        // ── Cloud coverage → skybox weather override ──
        if (skybox != null)
        {
            skybox.WeatherOverride = skyObj != null
                ? Math.Clamp(skyObj.SkyCloudCoverage, 0f, 1f)
                : null;
        }
    }

    /// <summary>World-space forward (look) direction derived from <see cref="RotationEuler"/>
    /// — same convention as <see cref="WorldMatrix"/> and the camera/light gizmos.
    /// Default (no rotation) looks down -Z.</summary>
    public Vector3 Forward
    {
        get
        {
            var rot = Matrix4x4.CreateFromYawPitchRoll(
                RotationEuler.Y * MathF.PI / 180f,
                RotationEuler.X * MathF.PI / 180f,
                RotationEuler.Z * MathF.PI / 180f);
            return Vector3.Transform(-Vector3.UnitZ, rot);
        }
    }

    /// <summary>Model matrix for the advanced terrain mesh: XZ footprint follows
    /// Scale.X/Z (the thin Scale.Y must NOT flatten the terrain), Y keeps world height.
    /// Shared by rendering, AABB and brush raycasting so they all agree.</summary>
    private Matrix4x4 TerrainModelMatrix =>
        Matrix4x4.CreateScale(Scale.X, 1f, Scale.Z)
        * Matrix4x4.CreateFromYawPitchRoll(
            RotationEuler.Y * MathF.PI / 180f,
            RotationEuler.X * MathF.PI / 180f,
            RotationEuler.Z * MathF.PI / 180f)
        * Matrix4x4.CreateTranslation(Position);

    /// <summary>Compute world-space AABB for selection/culling.
    /// Accounts for scale and rotation (transforms 8 corners through WorldMatrix).</summary>
    public AABB WorldAABB
    {
        get
        {
            // Advanced terrain: heightmap mesh spans the full Scale footprint and rises up
            // to TerrainHeightScale — transform with the same model used for rendering.
            if (PrimitiveType == EditorPrimitiveType.Plane && TerrainEnabled && _terrainMesh is { IsReady: true })
            {
                var terrainModel = TerrainModelMatrix;
                float top = Math.Max(1f, TerrainHeightScale);
                return new AABB(new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, top, 0.5f))
                    .Transform(terrainModel);
            }

            // Local-space AABB for each primitive type (before transform)
            AABB localAABB = PrimitiveType switch
            {
                EditorPrimitiveType.Plane => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
                EditorPrimitiveType.Box => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
                EditorPrimitiveType.Sphere => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
                EditorPrimitiveType.GlbReference => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
                _ => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
            };
            return localAABB.Transform(WorldMatrix);
        }
    }

    /// <summary>Compute a cache key from the mesh-affecting terrain settings so the mesh is
    /// only rebuilt when something actually changed (Inspector edits, scale, textures).
    /// Uniform-only settings (slope, tiling, height bands) are deliberately excluded — they
    /// don't change the mesh, so editing them must NOT trigger an expensive rebuild.</summary>
    private string TerrainMeshCacheKey
    {
        get
        {
            return $"{TerrainEnabled}|{TerrainHeightmapPath}|{TerrainChunkSize}|{TerrainHeightScale:F2}|"
                 + $"{Scale.X:F2}|{Scale.Z:F2}|"
                 + $"{TerrainTextureAirPath}|{TerrainTextureDirtPath}|{TerrainTextureGrassPath}|{TerrainTextureSnowPath}";
        }
    }

    /// <summary>Build (or rebuild) the advanced terrain mesh when TerrainEnabled.
    /// Returns true if a valid terrain mesh is available, false if it fell back to flat.</summary>
    private bool EnsureTerrainMesh()
    {
        string key = TerrainMeshCacheKey;
        if (_terrainMesh != null && _terrainCacheKey == key)
            return true;

        // Keep user brush edits across mesh rebuilds (chunk size / scale / texture edits):
        // stash the painted blob before tearing down, restore it after the fresh load
        // as long as the heightmap path didn't change (switching maps discards edits).
        // NOTE: tag the cache with the OLD loaded path — TerrainHeightmapPath may already
        // point at a NEW file (e.g. the user just changed it in the Inspector).
        if (_terrainMesh is { IsModified: true } oldMesh)
        {
            _terrainPaintedCache = oldMesh.GetModifiedRaw();
            _terrainPaintedSourcePath = _terrainLoadedPath;
        }
        // Layer paint (splat) is independent of the heightmap — always carry it over.
        if (_terrainMesh is { SplatModified: true } oldMesh2)
            _terrainSplatCache = oldMesh2.GetModifiedSplatRaw();

        _terrainMesh?.Dispose();
        _terrainMesh = null;
        _terrainCacheKey = key;

        if (!TerrainEnabled) return false;

        var mesh = new EditorTerrainMesh();
        if (!mesh.LoadHeightmap(TerrainHeightmapPath))
        {
            Console.WriteLine($"[EditorObject] '{Name}': heightmap not found — rendering flat plane ({TerrainHeightmapPath})");
            mesh.Dispose();
            _terrainCacheKey = "";
            return false;
        }
        if (_terrainPaintedCache != null && _terrainPaintedSourcePath == TerrainHeightmapPath)
            mesh.RestoreModifiedRaw(_terrainPaintedCache);
        if (_terrainSplatCache != null)
            mesh.RestoreModifiedSplatRaw(_terrainSplatCache);
        _terrainLoadedPath = TerrainHeightmapPath;
        mesh.SetLayerTextures(
            TerrainTextureAirPath,
            TerrainTextureDirtPath,
            TerrainTextureGrassPath,
            TerrainTextureSnowPath);
        mesh.Generate(TerrainChunkSize, TerrainHeightScale, Math.Max(0.1f, Scale.X), Math.Max(0.1f, Scale.Z));
        _terrainMesh = mesh;

        // When switching a plane to terrain mode, release the flat-plane Object3D so it
        // doesn't linger on the GPU until Dispose().
        if (_object3D != null)
        {
            if (_object3D.VAO != 0)
            {
                uint vao = _object3D.VAO;
                GL.DeleteVertexArrays(1, &vao);
            }
            if (_object3D.VBO != 0)
            {
                uint vbo = _object3D.VBO;
                GL.DeleteBuffers(1, &vbo);
            }
            _object3D = null;
        }
        return true;
    }

    /// <summary>Initialize GPU resources (VAO, VBO) if needed.</summary>
    public void EnsureResources()
    {
        // Advanced terrain planes use a dedicated mesh + shader, not Object3D.
        // If the heightmap is missing we fall back to a flat plane (EnsureTerrainMesh
        // returns false), so continue into the normal primitive path below.
        if (PrimitiveType == EditorPrimitiveType.Plane && TerrainEnabled && EnsureTerrainMesh())
            return;

        if (_object3D != null && !_dirty) return;

        // Cleanup old GPU resources (VAO/VBO) so repeated color changes don't leak buffers.
        // EditorObjectManager.Remove() also disposes the Object3D, but here we're re-creating
        // in place, so delete the GL handles ourselves before dropping the reference.
        if (_object3D != null)
        {
            if (_object3D.VAO != 0)
            {
                uint vao = _object3D.VAO;
                GL.DeleteVertexArrays(1, &vao);
            }
            if (_object3D.VBO != 0)
            {
                uint vbo = _object3D.VBO;
                GL.DeleteBuffers(1, &vbo);
            }
            _object3D = null;
        }

        var shader = Shader.GetShaderProgram();

        switch (PrimitiveType)
        {
            case EditorPrimitiveType.Plane:
            {
                // If terrain mode is active AND a valid terrain mesh exists, the mesh is
                // managed by EnsureTerrainMesh() — no flat Object3D is needed. Otherwise
                // (terrain disabled, or heightmap missing) build the classic flat plane.
                if (TerrainEnabled && _terrainMesh is { IsReady: true })
                {
                    _object3D = null;
                    _vertexCache = null;
                    break;
                }
                var verts = Object3D.CreatePlaneVertices(1f, 1f, Color);
                _object3D = new Object3D(0, 0, 0);
                _object3D.Generate(shader, verts);
                _vertexCache = verts;
                break;
            }
            case EditorPrimitiveType.Box:
            {
                var verts = Object3D.CreateBoxVertices(1f, 1f, 1f, Color);
                _object3D = new Object3D(0, 0, 0);
                _object3D.Generate(shader, verts);
                _vertexCache = verts;
                break;
            }
            case EditorPrimitiveType.Sphere:
            {
                var verts = Object3D.CreateSphereVertices(0.5f, Color);
                _object3D = new Object3D(0, 0, 0);
                _object3D.Generate(shader, verts);
                _vertexCache = verts;
                break;
            }
            case EditorPrimitiveType.Camera:
            case EditorPrimitiveType.Light:
            case EditorPrimitiveType.Sky:
            {
                // Camera/Light/Sky markers are 2D billboard icons (drawn via Draw2DMarker) —
                // no solid mesh (the camera shows a wireframe frustum gizmo instead).
                // Keep _object3D null so they don't render as 3D boxes/spheres.
                _vertexCache = null;
                break;
            }
            case EditorPrimitiveType.GlbReference:
                // glb objects are handled by EditorObjectManager externally
                break;
        }

        // Load texture if specified
        if (!string.IsNullOrEmpty(TexturePath) && File.Exists(TexturePath))
        {
            var tex = new Texture(TexturePath);
            _textureID = tex.ID;
        }
        else
        {
            _textureID = 0;
        }

        _dirty = false;
    }

    /// <summary>Alias for EnsureResources() for API compatibility with EditorObjectManager.</summary>
    public void InitGPU()
    {
        EnsureResources();
    }

    // ════════════════════════════════════════════════════════════════════
    //  Terrain brush paint (viewport tool) + painted-data persistence
    // ════════════════════════════════════════════════════════════════════

    /// <summary>True when this terrain has been edited with the paint brush.</summary>
    public bool TerrainIsModified => _terrainMesh is { IsModified: true };

    /// <summary>Base64-encoded painted heightmap blob for scene persistence.
    /// Empty string = no user edits (nothing to save).</summary>
    public string TerrainPaintedData
    {
        get
        {
            // Live-capture from the mesh so edits made this session are always current.
            if (_terrainMesh is { IsModified: true } m)
            {
                _terrainPaintedCache = m.GetModifiedRaw();
                _terrainPaintedSourcePath = TerrainHeightmapPath;
            }
            // Never persist a cache that belongs to a different heightmap file.
            if (_terrainPaintedCache == null || _terrainPaintedSourcePath != TerrainHeightmapPath)
                return "";
            return Convert.ToBase64String(_terrainPaintedCache);
        }
        set
        {
            _terrainPaintedCache = null;
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                _terrainPaintedCache = Convert.FromBase64String(value);
                // Only restored while the terrain points at the same heightmap file.
                _terrainPaintedSourcePath = TerrainHeightmapPath;
            }
            catch
            {
                _terrainPaintedCache = null;
            }
        }
    }

    /// <summary>Raycast this terrain's surface. Returns the world-space surface point
    /// (brush cursor / placement) or null when the ray misses the footprint.</summary>
    /// <summary>Transform the ray into this terrain's local space and intersect the Y=0 base
    /// plane. Returns the local XZ point (in [-0.5, 0.5]²) when the ray hits the footprint.
    /// Shared by every brush operation so ray math stays consistent.</summary>
    private bool TryGetTerrainLocalPoint(Vector3 rayOrigin, Vector3 rayDir, out Vector2 local)
    {
        local = Vector2.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true })
            return false;
        var model = TerrainModelMatrix;
        if (!Matrix4x4.Invert(model, out var invModel))
            return false;
        var localOrigin = Vector3.Transform(rayOrigin, invModel);
        var localDir = Vector3.TransformNormal(rayDir, invModel);
        if (Math.Abs(localDir.Y) < 1e-6f)
            return false;
        float t = -localOrigin.Y / localDir.Y;
        if (t <= 0f)
            return false;
        var localHit = localOrigin + localDir * t;
        if (localHit.X < -0.5f || localHit.X > 0.5f || localHit.Z < -0.5f || localHit.Z > 0.5f)
            return false;
        local = new Vector2(localHit.X, localHit.Z);
        return true;
    }

    /// <summary>Brush radius converted to local units (world brush size ÷ footprint).</summary>
    private float LocalBrushRadius =>
        Math.Max(0.02f, TerrainBrushSize) / MathF.Max(MathF.Max(0.1f, Scale.X), MathF.Max(0.1f, Scale.Z));

    /// <summary>World-space surface point under the ray (brush cursor / placement).</summary>
    public Vector3? RaycastTerrainSurface(Vector3 rayOrigin, Vector3 rayDir)
    {
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return null;
        // Quick reject against the world AABB before the precise intersection.
        if (!AABB.RayIntersectsAABB(rayOrigin, rayDir, GetWorldAABB(), out _, out _))
            return null;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return null;
        float h = m.SampleLocalHeight(local.X, local.Y);
        return Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
    }

    /// <summary>Paint one brush stamp into the terrain at the ray's intersection.
    /// <paramref name="deltaWorld"/> is the height delta in world units (positive =
    /// raise, negative = lower). Returns true + the world hit point when painted.</summary>
    public bool TryPaintTerrainSurface(Vector3 rayOrigin, Vector3 rayDir, float deltaWorld, out Vector3 worldHitPoint)
    {
        worldHitPoint = Vector3.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        // Convert the world delta (sculpt strength in world units) to normalized height.
        float deltaNorm = deltaWorld / Math.Max(1f, TerrainHeightScale);
        m.PaintHeight(local.X, local.Y, LocalBrushRadius, deltaNorm, TerrainBrushSoftness, TerrainBrushFalloff);

        float h = m.SampleLocalHeight(local.X, local.Y);
        worldHitPoint = Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
        return true;
    }

    /// <summary>Smooth one brush stamp at the ray's terrain intersection (averages the
    /// heights in the brush area). <paramref name="strength"/> 0..1 blend per stamp.</summary>
    public bool TrySmoothTerrainSurface(Vector3 rayOrigin, Vector3 rayDir, float strength, out Vector3 worldHitPoint)
    {
        worldHitPoint = Vector3.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        m.SmoothHeight(local.X, local.Y, LocalBrushRadius, strength, TerrainBrushFalloff);

        float h = m.SampleLocalHeight(local.X, local.Y);
        worldHitPoint = Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
        return true;
    }

    /// <summary>Flatten one brush stamp at the ray's terrain intersection toward
    /// <paramref name="targetNorm"/> (normalized 0..1 height). <paramref name="strength"/>
    /// 0..1 blend per stamp.</summary>
    public bool TryFlattenTerrainSurface(Vector3 rayOrigin, Vector3 rayDir, float targetNorm, float strength, out Vector3 worldHitPoint)
    {
        worldHitPoint = Vector3.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        m.FlattenHeight(local.X, local.Y, LocalBrushRadius, targetNorm, strength, TerrainBrushFalloff);

        float h = m.SampleLocalHeight(local.X, local.Y);
        worldHitPoint = Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
        return true;
    }

    /// <summary>Normalized (0..1) terrain height under the ray — the flatten tool captures
    /// this from the first stamp of a stroke as its level target.</summary>
    public bool TryGetTerrainNormalizedHeight(Vector3 rayOrigin, Vector3 rayDir, out float targetNorm)
    {
        targetNorm = 0f;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        targetNorm = m.SampleLocalHeightNorm(local.X, local.Y);
        return true;
    }

    /// <summary>Snapshot the terrain's height array (undo support).</summary>
    public float[]? CaptureTerrainHeights() => _terrainMesh?.GetHeightSnapshot();

    /// <summary>Restore a height snapshot (undo/redo) and rebuild the GPU mesh.</summary>
    public void RestoreTerrainHeights(float[]? heights)
    {
        if (_terrainMesh != null && heights != null)
            _terrainMesh.RestoreHeightSnapshot(heights);
    }

    /// <summary>Write the current painted heights to a .raw file (returns success).</summary>
    public bool SaveTerrainHeightmap(string path)
    {
        if (_terrainMesh == null || string.IsNullOrEmpty(path)) return false;
        bool ok = _terrainMesh.SaveHeightmapFile(path);
        if (ok)
        {
            // Point the terrain at the saved file so future rebuilds read the edits.
            TerrainHeightmapPath = path;
            MarkDirty();
        }
        return ok;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Manual layer paint (splat) — 🎨 brush
    // ════════════════════════════════════════════════════════════════════

    /// <summary>True when this terrain has manual layer paint to show.</summary>
    public bool TerrainSplatIsModified => _terrainMesh is { SplatModified: true };

    /// <summary>Serialized splat blob (base64) for scene persistence. Empty = no paint.</summary>
    public string TerrainSplatData
    {
        get
        {
            if (_terrainMesh is { SplatModified: true } m)
                _terrainSplatCache = m.GetModifiedSplatRaw();
            return _terrainSplatCache == null ? "" : Convert.ToBase64String(_terrainSplatCache);
        }
        set
        {
            _terrainSplatCache = null;
            if (string.IsNullOrEmpty(value)) return;
            try { _terrainSplatCache = Convert.FromBase64String(value); }
            catch { _terrainSplatCache = null; }
        }
    }

    /// <summary>Paint one splat stamp (layer 0..3) at the ray's terrain intersection.
    /// <paramref name="erase"/> decays painted weights back toward automatic texturing.
    /// Returns true + the world hit point when painted.</summary>
    public bool TryPaintLayerSurface(Vector3 rayOrigin, Vector3 rayDir, int layerIndex, float strength, bool erase, out Vector3 worldHitPoint)
    {
        worldHitPoint = Vector3.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        m.PaintLayer(local.X, local.Y, LocalBrushRadius, layerIndex, strength, TerrainBrushSoftness, TerrainBrushFalloff, erase);

        float h = m.SampleLocalHeight(local.X, local.Y);
        worldHitPoint = Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
        return true;
    }

    /// <summary>Snapshot the splat bytes (undo support).</summary>
    public byte[]? CaptureTerrainSplat() => _terrainMesh?.GetSplatSnapshot();

    /// <summary>Restore a splat snapshot (undo/redo).</summary>
    public void RestoreTerrainSplat(byte[]? splat)
    {
        if (_terrainMesh != null && splat != null)
            _terrainMesh.RestoreSplatSnapshot(splat);
    }

    /// <summary>Clear ALL manual layer paint (back to automatic height+slope texturing).</summary>
    public void ClearTerrainLayerPaint()
    {
        _terrainMesh?.ClearSplatPaint();
    }

    /// <summary>
    /// Draw the brush ring ON this terrain's surface — a circle that follows the heightmap
    /// (sampled every segment), so the user sees exactly which area of the plane the brush
    /// will affect. Ring radius = TerrainBrushSize (same footprint mapping as painting).
    /// Depth test is disabled so the ring never z-fights with the terrain mesh.
    /// </summary>
    public void DrawTerrainBrushIndicator(Camera camera)
    {
        if (!ShowBrushIndicator || BrushIndicatorPos is not Vector3 center)
            return;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return;

        var model = TerrainModelMatrix;
        if (!Matrix4x4.Invert(model, out var invModel))
            return;
        var localCenter = Vector3.Transform(center, invModel);

        float footprint = MathF.Max(MathF.Max(0.1f, Scale.X), MathF.Max(0.1f, Scale.Z));
        float rLocal = Math.Max(0.02f, TerrainBrushSize) / footprint;

        const int Segments = 48;
        var ring = new List<Vector3>(Segments * 2);
        var prev = Vector3.Zero;
        for (int i = 0; i <= Segments; i++)
        {
            float a = (float)i / Segments * MathF.PI * 2f;
            float lx = Math.Clamp(localCenter.X + MathF.Cos(a) * rLocal, -0.5f, 0.5f);
            float lz = Math.Clamp(localCenter.Z + MathF.Sin(a) * rLocal, -0.5f, 0.5f);
            float h = m.SampleLocalHeight(lx, lz);
            var p = Vector3.Transform(new Vector3(lx, h, lz), model);
            if (i > 0)
            {
                ring.Add(prev);
                ring.Add(p);
            }
            prev = p;
        }
        // i goes 0..Segments inclusive, so the last pair closes the loop (segment 48 == point 0).

        GL.Disable(Const.GL_DEPTH_TEST);
        Terrains.TerrainChunk.DrawLineSegments(ring, BrushIndicatorColor, camera);
        GL.Enable(Const.GL_DEPTH_TEST);
    }
    /// <summary>Draw using individual uniform locations (matching EditorObjectManager's call pattern).</summary>
        public void Draw(
            int modelLoc, int viewLoc, int projLoc,
            int sunDirLoc, int lightColorLoc, int viewPosLoc,
            int useFogLoc, int fogColorLoc,
            Camera camera, Lights light)
        {
            if (!IsVisible) return;

            // Rebuild GPU resources if MarkDirty() was called (e.g. color changed in the
            // Inspector) — otherwise the old vertices/color would keep rendering forever.
            EnsureResources();

            // ── Advanced terrain plane: render with the dedicated terrain shader. ──
            if (PrimitiveType == EditorPrimitiveType.Plane && TerrainEnabled && _terrainMesh is { IsReady: true })
            {
                // XZ footprint follows Scale.X/Z; Y stays at real world height (the
                // plane's thin Scale.Y must NOT flatten the terrain).
                _terrainMesh.Draw(TerrainModelMatrix, camera, light, this);

                // ── Brush ring indicator ON the terrain surface (while the brush tool
                // hovers this terrain) — drawn after the mesh so it's always on top. ──
                DrawTerrainBrushIndicator(camera);

                // The terrain shader switches the active program — restore the main
                // shader so subsequent editor objects render correctly.
                GL.UseProgram(Shader.GetShaderProgram());
                return;
            }

            // Terrain enabled but no valid mesh (missing heightmap): fall back to the
            // flat plane mesh so the object stays visible and editable.

            if (_object3D == null) return;

            // Set correct model matrix (WorldMatrix includes position, scale, rotation)
            var model = WorldMatrix;
            GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&model);

            // Primitives are CCW-wound, so the scene's culling/winding state (applied via
            // SceneRenderProperties) is respected here — do NOT force culling on/off.

            // Set useTexture=0 so fragment shader uses vertex color instead of textures
            int useTexLoc = GL.GetUniformLocation(Shader.GetShaderProgram(), "useTexture");
            GL.Uniform1i(useTexLoc, 0);

            // Render VAO directly (bypass Object3D.Draw to avoid model matrix override)
            GL.BindVertexArray(_object3D.VAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, _object3D.VertexCount);
            GL.BindVertexArray(0);
        }

    /// <summary>
    /// Draw a real-camera gizmo for this placed camera: a small camera body box plus a
    /// view-frustum wireframe (near plane, far plane, connector lines and a center ray)
    /// built from <see cref="CameraFov"/>, <see cref="CameraNear"/> and <see cref="CameraFar"/>.
    /// The frustum follows the object's rotation (Yaw/Pitch/Roll) so rotating the marker
    /// points the frustum the same way. Depth test is disabled so the wireframe shows
    /// through terrain and other geometry (like other editor helpers).
    /// </summary>
    public unsafe void DrawCameraFrustum(Camera camera)
    {
        if (!IsVisible || PrimitiveType != EditorPrimitiveType.Camera) return;

        float yaw = RotationEuler.Y * MathF.PI / 180f;
        float pitch = RotationEuler.X * MathF.PI / 180f;
        float roll = RotationEuler.Z * MathF.PI / 180f;
        var rot = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);

        // Camera looks down -Z (same convention as the engine Camera + gizmo rotation).
        var forward = Vector3.Transform(-Vector3.UnitZ, rot);
        var up = Vector3.Transform(Vector3.UnitY, rot);
        var right = Vector3.Transform(Vector3.UnitX, rot);

        float fovRad = CameraFov * MathF.PI / 180f;
        float aspect = 16f / 9f; // assumed viewport aspect for the preview frustum

        // Display distances: keep the real near plane exact, but clamp the *displayed*
        // far distance so a 500-unit far clip doesn't produce a giant wireframe that
        // dominates the scene. Near is floored so the near quad stays readable.
        float displayNear = MathF.Max(CameraNear, 0.25f);
        float displayFar = MathF.Min(CameraFar, 100f);

        float nearHalfH = MathF.Tan(fovRad * 0.5f) * displayNear;
        float nearHalfW = nearHalfH * aspect;
        float farHalfH = MathF.Tan(fovRad * 0.5f) * displayFar;
        float farHalfW = farHalfH * aspect;

        Vector3 nearCenter = Position + forward * displayNear;
        Vector3 farCenter = Position + forward * displayFar;

        // 8 frustum corners: near quad then far quad
        Vector3 n0 = nearCenter - right * nearHalfW + up * nearHalfH;
        Vector3 n1 = nearCenter + right * nearHalfW + up * nearHalfH;
        Vector3 n2 = nearCenter + right * nearHalfW - up * nearHalfH;
        Vector3 n3 = nearCenter - right * nearHalfW - up * nearHalfH;
        Vector3 f0 = farCenter - right * farHalfW + up * farHalfH;
        Vector3 f1 = farCenter + right * farHalfW + up * farHalfH;
        Vector3 f2 = farCenter + right * farHalfW - up * farHalfH;
        Vector3 f3 = farCenter - right * farHalfW - up * farHalfH;

        var verts = new List<Vector3>(48);
        void Line(Vector3 a, Vector3 b) { verts.Add(a); verts.Add(b); }

        // Near plane quad
        Line(n0, n1); Line(n1, n2); Line(n2, n3); Line(n3, n0);
        // Far plane quad
        Line(f0, f1); Line(f1, f2); Line(f2, f3); Line(f3, f0);
        // Connectors near → far
        Line(n0, f0); Line(n1, f1); Line(n2, f2); Line(n3, f3);
        // Center ray (from camera origin to far center, with a small crosshair dot at the
        // far center so the look target is obvious)
        Line(Position, farCenter);
        float r = farHalfW * 0.06f;
        Line(farCenter - right * r, farCenter + right * r);
        Line(farCenter - up * r, farCenter + up * r);

        // Small camera body box right at the origin (wireframe, slightly larger than the
        // solid marker so the lens direction reads clearly)
        float b = 0.28f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        Vector3[] body =
        [
            Position - right * b - up * b - forward * b, Position + right * b - up * b - forward * b,
            Position + right * b + up * b - forward * b, Position - right * b + up * b - forward * b,
            Position - right * b - up * b + forward * b, Position + right * b - up * b + forward * b,
            Position + right * b + up * b + forward * b, Position - right * b + up * b + forward * b,
        ];
        int[] boxEdges = [0,1, 1,2, 2,3, 3,0, 4,5, 5,6, 6,7, 7,4, 0,4, 1,5, 2,6, 3,7];
        for (int i = 0; i < boxEdges.Length; i += 2)
            Line(body[boxEdges[i]], body[boxEdges[i + 1]]);

        // Lens: small quad on the +forward face to show which way the camera points
        float l = b * 0.55f;
        Vector3 lensC = Position + forward * b;
        Vector3[] lens =
        [
            lensC - right * l - up * l, lensC + right * l - up * l,
            lensC + right * l + up * l, lensC - right * l + up * l,
        ];
        Line(lens[0], lens[1]); Line(lens[1], lens[2]); Line(lens[2], lens[3]); Line(lens[3], lens[0]);

        DrawEditorLines(verts, camera);
    }

    /// <summary>Render editor wireframe lines (depth-test off so they show through
    /// geometry), shared by the camera frustum and light gizmos.</summary>
    private void DrawEditorLines(List<Vector3> verts, Camera camera)
    {
        DrawEditorLines(verts, camera, Color);
    }

    /// <summary>Render editor wireframe lines in a custom color (depth-test off so they
    /// show through geometry). Used by the light gizmo to color-code its parts.</summary>
    private void DrawEditorLines(List<Vector3> verts, Camera camera, Vector3 lineColor)
    {
        if (verts == null || verts.Count == 0) return;
        GL.Disable(Const.GL_DEPTH_TEST);
        Terrains.TerrainChunk.DrawLineSegments(verts, lineColor, camera);
        GL.Enable(Const.GL_DEPTH_TEST);
    }

    /// <summary>
    /// Draw a real-light gizmo for this placed light: a bright direction axis showing
    /// where <see cref="LightDirection"/> points (the beam the user must "see" first),
    /// a dimmer spotlight cone visualizing <see cref="LightConeAngle"/>, a sun marker
    /// at the anchor, and a small RGB axis tripod so the marker's local orientation is
    /// always readable. Each part is drawn in its own color so the beam never blends
    /// into the cone. Depth test disabled so lines show through geometry.
    /// </summary>
    public unsafe void DrawLightGizmo(Camera camera)
    {
        if (!IsVisible || PrimitiveType != EditorPrimitiveType.Light) return;

        float yaw = RotationEuler.Y * MathF.PI / 180f;
        float pitch = RotationEuler.X * MathF.PI / 180f;
        float roll = RotationEuler.Z * MathF.PI / 180f;
        var rot = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);

        // The light direction is a world-space vector on the object; apply the marker's
        // rotation so rotating the object re-aims the beam (matches WorldMatrix convention).
        var dir = Vector3.Transform(LightDirection, rot);
        float len = dir.Length();
        if (len < 1e-4f) len = 1f;
        var beam = dir / len; // normalized aim direction

        // Display beam length: respect intensity a bit, but keep it bounded so it never
        // dominates the scene (intensity is a multiplier, not distance).
        float displayLen = Math.Clamp(4f + LightIntensity * 3f, 4f, 24f);

        // Build an orthonormal basis around the beam for the cone circle
        var upRef = MathF.Abs(beam.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(beam, upRef));
        var up = Vector3.Normalize(Vector3.Cross(right, beam));

        // Separate vertex lists — one per color — so the axis reads instantly.
        var beamVerts = new List<Vector3>(32); // bright gold: THE direction axis
        var coneVerts = new List<Vector3>(64); // dim amber: spotlight spread
        var sunVerts  = new List<Vector3>(40); // object color: light anchor

        void Line(List<Vector3> list, Vector3 a, Vector3 b) { list.Add(a); list.Add(b); }

        // ── Direction axis (the beam): origin → tip with a prominent arrowhead. ──
        // This is the line the user must spot first, so it gets the brightest color.
        Vector3 tip = Position + beam * displayLen;
        Line(beamVerts, Position, tip);
        float head = displayLen * 0.10f;
        Line(beamVerts, tip, tip - beam * head + right * head * 0.7f);
        Line(beamVerts, tip, tip - beam * head - right * head * 0.7f);
        Line(beamVerts, tip, tip - beam * head + up * head * 0.7f);
        Line(beamVerts, tip, tip - beam * head - up * head * 0.7f);

        // ── Spotlight cone (dim): a circle at the beam tip whose radius grows with
        // distance, plus cone edge lines from the origin to that circle. ──
        float coneHalf = Math.Clamp(LightConeAngle, 1f, 89f) * MathF.PI / 180f;
        float coneRadius = MathF.Tan(coneHalf) * displayLen;
        const int segs = 12;
        var ring = new Vector3[segs];
        for (int i = 0; i < segs; i++)
        {
            float a = i * MathF.PI * 2f / segs;
            ring[i] = tip + right * (MathF.Cos(a) * coneRadius)
                          + up * (MathF.Sin(a) * coneRadius);
        }
        for (int i = 0; i < segs; i++)
        {
            Line(coneVerts, ring[i], ring[(i + 1) % segs]); // ring edge
            Line(coneVerts, Position, ring[i]);             // cone side
        }

        // ── Sun marker around the origin (circle + cross) in the object's color so the
        // light anchor is obvious even when zoomed far out. ──
        float r = 0.35f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        const int sunSegs = 8;
        var sun = new Vector3[sunSegs];
        for (int i = 0; i < sunSegs; i++)
        {
            float a = i * MathF.PI * 2f / sunSegs;
            sun[i] = Position + right * (MathF.Cos(a) * r) + up * (MathF.Sin(a) * r);
        }
        for (int i = 0; i < sunSegs; i++)
            Line(sunVerts, sun[i], sun[(i + 1) % sunSegs]);
        Line(sunVerts, Position - right * r, Position + right * r);
        Line(sunVerts, Position - up * r, Position + up * r);

        // ── RGB local-axis tripod at the anchor (X red, Y green, Z blue, matching the
        // transform gizmo colors) so the marker's rotation state is readable at a glance
        // — Y is always up-ish before you rotate the marker. Each axis is a separate
        // list so it can be colored individually. ──
        float aLen = MathF.Max(0.9f, r * 2.2f);
        var basis = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
        var ax = Vector3.Transform(Vector3.UnitX, basis);
        var ay = Vector3.Transform(Vector3.UnitY, basis);
        var az = Vector3.Transform(Vector3.UnitZ, basis);
        var xVerts = new List<Vector3>(2) { Position, Position + ax * aLen };
        var yVerts = new List<Vector3>(2) { Position, Position + ay * aLen };
        var zVerts = new List<Vector3>(2) { Position, Position + az * aLen };

        // Draw order matters (depth test is off, so later draws overwrite earlier ones):
        // dim cone + sun + tripod first, then the bright beam LAST so the direction axis
        // always wins where its line crosses the cone.
        DrawEditorLines(coneVerts, camera, new Vector3(0.45f, 0.38f, 0.14f));
        DrawEditorLines(sunVerts, camera, Color);
        DrawEditorLines(xVerts, camera, new Vector3(0.85f, 0.25f, 0.2f));  // X = red
        DrawEditorLines(yVerts, camera, new Vector3(0.3f, 0.8f, 0.3f));    // Y = green
        DrawEditorLines(zVerts, camera, new Vector3(0.3f, 0.45f, 0.9f));   // Z = blue
        DrawEditorLines(beamVerts, camera, new Vector3(1.0f, 0.92f, 0.35f)); // beam on top
    }

    // ════════════════════════════════════════════════════════════════════
    //  Sky sun manipulation (gizmo drag handle)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>World-space sun direction this sky object currently renders — honors the
    /// SkySunPitch/SkySunYaw override when set, otherwise follows the time-of-day cycle.
    /// Shared by the gizmo drawing and the sun-handle drag math so they always agree.</summary>
    public Vector3 GetSkySunDirection()
    {
        if (SkySunPitch.HasValue && SkySunYaw.HasValue)
        {
            float p = SkySunPitch.Value * MathF.PI / 180f;
            float y = SkySunYaw.Value * MathF.PI / 180f;
            return Vector3.Normalize(new Vector3(
                MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p), MathF.Cos(y) * MathF.Cos(p)));
        }
        float hours = Math.Clamp(SkyTimeOfDay, 0f, 24f);
        float sunAngle = (hours / 24f) * (MathF.PI * 2f) - (MathF.PI * 0.5f);
        return Vector3.Normalize(new Vector3(MathF.Cos(sunAngle), MathF.Sin(sunAngle), 0.3f));
    }

    /// <summary>World-space center of the sun handle drawn by the sky gizmo (null when this
    /// object is not a Sky). Matches DrawSkyGizmo's sun icon placement exactly.</summary>
    public Vector3? GetSkySunHandleCenter()
    {
        if (PrimitiveType != EditorPrimitiveType.Sky) return null;
        float horizonR = 3.5f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        return Position + GetSkySunDirection() * (horizonR * 0.85f);
    }

    /// <summary>Set the SkySunPitch/SkySunYaw override from a world-space direction (used by
    /// the sun-handle drag). When <paramref name="snap"/> is set, pitch snaps to 5° and yaw
    /// to 15° increments for precise alignment.</summary>
    public void SetSkySunFromDirection(Vector3 worldDir, bool snap = false)
    {
        if (worldDir.LengthSquared() < 1e-6f) return;
        var d = Vector3.Normalize(worldDir);
        float pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f)) * 180f / MathF.PI;
        float yaw = MathF.Atan2(d.X, d.Z) * 180f / MathF.PI;
        if (yaw < 0f) yaw += 360f;
        if (snap)
        {
            pitch = MathF.Round(pitch / 5f) * 5f;
            yaw = MathF.Round(yaw / 15f) * 15f;
            if (yaw >= 360f) yaw -= 360f;
        }
        SkySunPitch = Math.Clamp(pitch, -90f, 90f);
        SkySunYaw = yaw % 360f;
    }

    /// <summary>Aim the sun at the given world ray (dragging the sun handle): intersect the
    /// sun orbit sphere around the marker; on a miss (viewed edge-on) fall back to the
    /// closest point on the ray to the sphere center. Returns false when nothing changed.</summary>
    public bool AimSkySunFromRay(Vector3 rayOrigin, Vector3 rayDir, bool snap = false)
    {
        if (PrimitiveType != EditorPrimitiveType.Sky) return false;
        Vector3 center = Position;
        float horizonR = 3.5f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        float r = MathF.Max(0.5f, horizonR * 0.85f);

        var dir = Vector3.Normalize(rayDir);
        var oc = rayOrigin - center;
        float b = Vector3.Dot(oc, dir);
        float c = Vector3.Dot(oc, oc) - r * r;
        float disc = b * b - c;

        Vector3 hit;
        if (disc >= 0f)
        {
            float t = -b - MathF.Sqrt(disc);
            if (t < 0f) t = -b + MathF.Sqrt(disc);
            if (t < 0f) return false;
            hit = rayOrigin + dir * t;
        }
        else
        {
            // Ray misses the sphere — aim along the closest point of the ray to the center.
            float t = Vector3.Dot(center - rayOrigin, dir);
            if (t < 0f) return false;
            hit = rayOrigin + dir * t;
        }

        SetSkySunFromDirection(hit - center, snap);
        return true;
    }

    /// <summary>
    /// Draw a sky gizmo for this placed sky marker: a horizon circle in the XZ plane
    /// around the marker, a small vertical zenith axis, and a sun icon placed in the
    /// direction of the sun (driven by SkyTimeOfDay, or by the SkySunPitch/SkySunYaw
    /// override when set) whose radius/brightness scales with SkySunIntensity.
    /// </summary>
    public unsafe void DrawSkyGizmo(Camera camera)
    {
        if (!IsVisible || PrimitiveType != EditorPrimitiveType.Sky) return;

        // ── Sun direction: same math as SceneManager/editor lights ──
        Vector3 sunDir = GetSkySunDirection();

        var verts = new List<Vector3>(96);
        void Line(Vector3 a, Vector3 b) { verts.Add(a); verts.Add(b); }

        // ── Horizon circle (XZ plane, radius 3.5, center = marker) ──
        const int segs = 24;
        float horizonR = 3.5f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        var prev = new Vector3(Position.X + horizonR, Position.Y, Position.Z);
        for (int i = 1; i <= segs; i++)
        {
            float a = i * MathF.PI * 2f / segs;
            var cur = new Vector3(Position.X + MathF.Cos(a) * horizonR, Position.Y, Position.Z + MathF.Sin(a) * horizonR);
            Line(prev, cur);
            prev = cur;
        }

        // ── Zenith axis (straight up through the marker) ──
        float zAxis = horizonR * 0.6f;
        Line(Position, Position + Vector3.UnitY * zAxis);
        Line(Position + Vector3.UnitY * (zAxis - 0.25f), Position + Vector3.UnitY * zAxis + Vector3.UnitX * 0.15f);
        Line(Position + Vector3.UnitY * (zAxis - 0.25f), Position + Vector3.UnitY * zAxis - Vector3.UnitX * 0.15f);

        // ── Sun icon: small circle + rays placed along the sun direction, radius scaled
        //    by intensity (brighter/larger sun = more intense) ──
        float sunRadius = 0.35f + 0.25f * Math.Clamp(SkySunIntensity, 0.1f, 3f);
        Vector3 sunCenter = Position + sunDir * (horizonR * 0.85f);

        // Build an orthonormal basis around the sun direction for the circle/rays
        var upRef = MathF.Abs(sunDir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        var sunRight = Vector3.Normalize(Vector3.Cross(sunDir, upRef));
        var sunUp = Vector3.Normalize(Vector3.Cross(sunRight, sunDir));

        const int sunSegs = 10;
        var ring = new Vector3[sunSegs];
        for (int i = 0; i < sunSegs; i++)
        {
            float a = i * MathF.PI * 2f / sunSegs;
            ring[i] = sunCenter + sunRight * (MathF.Cos(a) * sunRadius)
                                + sunUp * (MathF.Sin(a) * sunRadius);
        }
        for (int i = 0; i < sunSegs; i++)
            Line(ring[i], ring[(i + 1) % sunSegs]);

        // Rays around the sun (4 spokes, brighter color separately? — same color for now)
        float rayLen = sunRadius * 0.9f;
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI / 2f;
            var d = sunRight * MathF.Cos(a) + sunUp * MathF.Sin(a);
            Line(sunCenter + d * (sunRadius + 0.05f), sunCenter + d * (sunRadius + rayLen));
        }

        DrawEditorLines(verts, camera);
    }

    /// <summary>
    /// Draw a 2D billboard icon for Camera (camera glyph), Light (sun with rays) and
    /// Sky (sun + cloud) markers. The icon always faces the camera (built on the camera's
    /// right/up vectors), so it reads as a flat 2D badge in the viewport instead of a
    /// solid 3D box/sphere.
    /// </summary>
    public unsafe void Draw2DMarker(Camera camera)
    {
        if (!IsVisible || (PrimitiveType != EditorPrimitiveType.Camera
            && PrimitiveType != EditorPrimitiveType.Light && PrimitiveType != EditorPrimitiveType.Sky)) return;

        // Derive a camera-facing basis from Front (Right/Up fields can be stale in fly
        // mode). Same upRef fallback as the light/sky gizmos so the icon always faces you.
        var front = Vector3.Normalize(camera.Front);
        var upRef = MathF.Abs(front.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(front, upRef));
        var up = Vector3.Normalize(Vector3.Cross(right, front));
        float r = 0.45f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));

        var verts = new List<Vector3>(96);
        void Line(Vector3 a, Vector3 b) { verts.Add(a); verts.Add(b); }
        Vector3 P(float u, float v) => Position + right * (u * r) + up * (v * r);

        if (PrimitiveType == EditorPrimitiveType.Light)
        {
            // Sun icon: circle + 8 rays
            const int segs = 16;
            var prev = P(MathF.Cos(0f), MathF.Sin(0f));
            for (int i = 1; i <= segs; i++)
            {
                float a = i * MathF.PI * 2f / segs;
                var cur = P(MathF.Cos(a), MathF.Sin(a));
                Line(prev, cur);
                prev = cur;
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.PI / 4f;
                float c = MathF.Cos(a), s = MathF.Sin(a);
                Line(P(c * 0.8f, s * 0.8f), P(c * 1.25f, s * 1.25f));
            }
        }
        else if (PrimitiveType == EditorPrimitiveType.Camera)
        {
            // Camera glyph: body rectangle + top viewfinder bump + lens circle.
            // The lens sits toward +right of the icon, echoing the frustum's forward
            // direction (which points down -Z in world space before any rotation).
            Line(P(-0.85f, -0.55f), P(0.85f, -0.55f)); // bottom
            Line(P(0.85f, -0.55f), P(0.85f, 0.35f));   // right
            Line(P(0.85f, 0.35f), P(-0.85f, 0.35f));   // top
            Line(P(-0.85f, 0.35f), P(-0.85f, -0.55f)); // left

            // Viewfinder bump on top
            Line(P(-0.45f, 0.35f), P(-0.45f, 0.6f));
            Line(P(-0.45f, 0.6f), P(0.45f, 0.6f));
            Line(P(0.45f, 0.6f), P(0.45f, 0.35f));

            // Lens circle (centered on the body)
            const int lensSegs = 12;
            const float lcx = 0.28f, lcy = -0.1f, lr = 0.28f;
            var lprev = P(lcx + lr, lcy);
            for (int i = 1; i <= lensSegs; i++)
            {
                float a = i * MathF.PI * 2f / lensSegs;
                var lcur = P(lcx + MathF.Cos(a) * lr, lcy + MathF.Sin(a) * lr);
                Line(lprev, lcur);
                lprev = lcur;
            }
        }
        else
        {
            // Sky icon: small sun circle + cloud arcs
            const int segs = 12;
            var prev = P(MathF.Cos(0f), MathF.Sin(0f));
            for (int i = 1; i <= segs; i++)
            {
                float a = i * MathF.PI * 2f / segs;
                var cur = P(MathF.Cos(a), MathF.Sin(a));
                Line(prev, cur);
                prev = cur;
            }
            // Cloud: three overlapping arcs on the lower half
            for (int arc = 0; arc < 3; arc++)
            {
                float cx = -0.45f + arc * 0.45f;
                float cy = -0.15f;
                Vector3? prevCloud = null;
                for (int i = 0; i <= 4; i++)
                {
                    float a = MathF.PI + i * MathF.PI / 4f; // lower half arc
                    var p = P(cx + MathF.Cos(a) * 0.35f, cy + MathF.Sin(a) * 0.35f);
                    if (prevCloud.HasValue) Line(prevCloud.Value, p);
                    prevCloud = p;
                }
            }
        }

        DrawEditorLines(verts, camera);
    }

    /// <summary>
    /// Draw the object's mesh as a wireframe line outline in the given color.
    /// Walks the triangle edges from the cached vertex data and draws them as
    /// line segments using the line shader.
    /// Deduplicates shared edges by their world-space position to reduce line vertices.
    /// Capped at 100k triangles for safety.
    /// </summary>
    public void DrawWireframe(Camera camera, Vector3 lineColor)
    {
        if (!IsVisible || _vertexCache == null || _vertexCache.Length < 3
            || PrimitiveType == EditorPrimitiveType.GlbReference) return;

        var worldMatrix = WorldMatrix;
        int triCount = _vertexCache.Length / 3;

        // Cap wireframe triangle count for safety
        const int maxTri = 100_000;
        if (triCount > maxTri)
        {
            Console.WriteLine($"[EditorObject] Wireframe skipped: {triCount} triangles exceeds max {maxTri}");
            return;
        }

        // Pack a Vector3 into a long with 2 decimal precision (±1000 range)
        long PackPos(Vector3 v)
        {
            long x = ((long)(v.X * 100 + 100000) & 0x3FFFF);
            long y = ((long)(v.Y * 100 + 100000) & 0x3FFFF);
            long z = ((long)(v.Z * 100 + 100000) & 0x3FFFF);
            return (x << 40) | (y << 20) | z;
        }

        var lineVerts = new List<Vector3>(triCount * 3); // ~50% reduction vs 6 per tri
        var edgeSet = new HashSet<(long, long)>(triCount * 3 / 2);

        for (int t = 0; t < triCount; t++)
        {
            int i = t * 3;
            var p0 = Vector3.Transform(_vertexCache[i].Position, worldMatrix);
            var p1 = Vector3.Transform(_vertexCache[i + 1].Position, worldMatrix);
            var p2 = Vector3.Transform(_vertexCache[i + 2].Position, worldMatrix);

            long h0 = PackPos(p0), h1 = PackPos(p1), h2 = PackPos(p2);

            // Edge (p0, p1) — sorted key so both directions hash the same
            var e1 = h0 < h1 ? (h0, h1) : (h1, h0);
            if (edgeSet.Add(e1)) { lineVerts.Add(p0); lineVerts.Add(p1); }

            // Edge (p1, p2)
            var e2 = h1 < h2 ? (h1, h2) : (h2, h1);
            if (edgeSet.Add(e2)) { lineVerts.Add(p1); lineVerts.Add(p2); }

            // Edge (p2, p0)
            var e3 = h2 < h0 ? (h2, h0) : (h0, h2);
            if (edgeSet.Add(e3)) { lineVerts.Add(p2); lineVerts.Add(p0); }
        }

        if (lineVerts.Count == 0) return;

        GL.Disable(Const.GL_DEPTH_TEST);
        Terrains.TerrainChunk.DrawLineSegments(lineVerts, lineColor, camera);
        GL.Enable(Const.GL_DEPTH_TEST);
    }

    /// <summary>
    /// First pass of the inverted-hull outline technique.
    /// Renders the object to the stencil buffer only (no color output)
    /// with depth test enabled. Stencil is set to 1 where depth passes.
    /// Face culling is intentionally disabled so both front and back faces
    /// write to the stencil (needed for a complete silhouette outline).
    /// </summary>
    public void DrawOutlineStencil(Camera camera)
    {
        if (!IsVisible || _object3D == null
            || PrimitiveType == EditorPrimitiveType.GlbReference) return;

        uint prog = Shader.GetOutlineShaderProgram();
        if (prog == 0) return;

        GL.UseProgram(prog);

        var model = WorldMatrix;
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();

        int modelLoc = GL.GetUniformLocation(prog, "model");
        int viewLoc = GL.GetUniformLocation(prog, "view");
        int projLoc = GL.GetUniformLocation(prog, "projection");

        GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&model);
        GL.UniformMatrix4fv(viewLoc, 1, false, (float*)&view);
        GL.UniformMatrix4fv(projLoc, 1, false, (float*)&proj);

        // Disable face culling so all triangles write to stencil
        // (both front and back faces needed for a complete silhouette)
        OpenGL.EnableFaceCulling(false);

        GL.BindVertexArray(_object3D.VAO);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, _object3D.VertexCount);
        GL.BindVertexArray(0);

        GL.UseProgram(0);
    }

    /// <summary>
    /// Second pass of the inverted-hull outline technique.
    /// Renders the object slightly scaled up (centered on its position)
    /// with face culling disabled, letting the stencil test (NOTEQUAL, 1)
    /// block the original object area so only the expanded border shows.
    /// Should be called after DrawOutlineStencil() with stencil test enabled.
    /// FIXED: Uses Scale * outlineScale directly in the world matrix
    /// instead of scaleAboutPos to keep the outline centered on the object.
    /// </summary>
    public void DrawOutline(Camera camera, Vector3 outlineColor, float outlineScale = 1.05f)
    {
        if (!IsVisible || _object3D == null
            || PrimitiveType == EditorPrimitiveType.GlbReference) return;

        uint prog = Shader.GetOutlineShaderProgram();
        if (prog == 0) return;

        GL.UseProgram(prog);

        // Use Scale * outlineScale directly so the outline stays centered
        // on the object's Position (unlike the old scaleAboutPos approach
        // which caused a positional drift proportional to Position * 0.05).
        float rotY = RotationEuler.Y * MathF.PI / 180f;
        float rotX = RotationEuler.X * MathF.PI / 180f;
        float rotZ = RotationEuler.Z * MathF.PI / 180f;
        var model = Matrix4x4.CreateScale(Scale * outlineScale)
                  * Matrix4x4.CreateFromYawPitchRoll(rotY, rotX, rotZ)
                  * Matrix4x4.CreateTranslation(Position);
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();

        int modelLoc = GL.GetUniformLocation(prog, "model");
        int viewLoc = GL.GetUniformLocation(prog, "view");
        int projLoc = GL.GetUniformLocation(prog, "projection");
        int colorLoc = GL.GetUniformLocation(prog, "outlineColor");

        GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&model);
        GL.UniformMatrix4fv(viewLoc, 1, false, (float*)&view);
        GL.UniformMatrix4fv(projLoc, 1, false, (float*)&proj);
        GL.Uniform3f(colorLoc, outlineColor.X, outlineColor.Y, outlineColor.Z);

        // Disable face culling: all triangles render, stencil test (NOTEQUAL, 1)
        // blocks the original area so only the expanded border is visible.
        OpenGL.EnableFaceCulling(false);

        GL.BindVertexArray(_object3D.VAO);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, _object3D.VertexCount);
        GL.BindVertexArray(0);

        GL.UseProgram(0);
    }

    /// <summary>Get world-space AABB (method version for API compatibility).</summary>
    public AABB GetWorldAABB() => WorldAABB;

    /// <summary>Render shadow using individual uniform locations (matching EditorObjectManager's call pattern).</summary>
    public void RenderShadow(int shadowModelLoc, Camera camera, CSM csm, int cascadeIndex)
    {
        if (!IsVisible || !CastShadow || _object3D == null) return;

        // Use the internally stored shadow shader (provided by manager)
        uint shadowShader = Shader.GetShadowShaderProgram();
        GL.UseProgram(shadowShader);

        var lightSpace = csm.LightSpaceMatrices[cascadeIndex];
        GL.UniformMatrix4fv(shadowModelLoc, 1, false, (float*)&lightSpace);
        GL.UniformMatrix4fv(GL.GetUniformLocation(shadowShader, "lightSpaceMatrix"), 1, false, (float*)&lightSpace);

        var model = WorldMatrix;
        GL.UniformMatrix4fv(shadowModelLoc, 1, false, (float*)&model);

        _object3D.RenderShadow(camera, csm, cascadeIndex, shadowShader, shadowModelLoc);
    }

    /// <summary>Mark object as needing resource refresh (color/texture changed).</summary>
    public void MarkDirty()
    {
        _dirty = true;
        _vertexCache = null; // Invalidate wireframe cache until EnsureResources() rebuilds it
        // Terrain cache key is compared against the live settings, so a rebuild happens
        // automatically next time EnsureResources() runs if anything changed.
    }

    /// <summary>Draw the primitive using Object3D's rendering pipeline.</summary>
    public void Draw(float dt, nint window, float moveSpeed)
    {
        if (!IsVisible) return;

        if (_object3D == null) return;

        // Apply world transform via model matrix
        var model = WorldMatrix;
        _object3D.UpdateModelMatriC(model);

        // Apply color
        // Update vertex colors if changed (simplified - we re-create on color change via MarkDirty)
        if (PrimitiveType != EditorPrimitiveType.GlbReference)
        {
            _object3D.Draw(dt, window, moveSpeed);
        }
    }

    /// <summary>Render shadow for this object.</summary>
    public void RenderShadow(Camera camera, CSM csm, int cascadeIndex, uint shadowShader, int modelLoc)
    {
        if (!IsVisible || !CastShadow || _object3D == null) return;

        _object3D.RenderShadow(camera, csm, cascadeIndex, shadowShader, modelLoc);
    }

    /// <summary>Create a new EditorObject of the given type with default values.</summary>
    public static EditorObject CreateDefault(EditorPrimitiveType type, string name, Vector3 position)
    {
        var obj = new EditorObject(type, name)
        {
            Position = position,
            Scale = type == EditorPrimitiveType.Plane
                ? new Vector3(25f, 0.05f, 25f)
                : Vector3.One,
            CastShadow = true,
            IsVisible = true,
        };
        // Snapshot initial state for undo
        obj.LastGizmoPosition = obj.Position;
        obj.LastGizmoRotation = obj.RotationEuler;
        obj.LastGizmoScale = obj.Scale;
        obj.LastGizmoPivot = obj.GizmoPivotOverride;
        return obj;
    }

    public void Dispose()
    {
        if (_textureID != 0)
        {
            fixed (uint* texPtr = &_textureID)
            {
                GL.DeleteTextures(1, texPtr);
            }
            _textureID = 0;
        }
        _terrainMesh?.Dispose();
        _terrainMesh = null;
        // Object3D cleanup is handled externally
        _object3D = null;
    }
}
