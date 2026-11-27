using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Linq;
using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public readonly record struct ViewportCullStatistics(
        int ViewportDrawnObjects, int ViewportFrustumObjects, int ViewportOccludedObjects,
        long ViewportDrawnTriangles, long ViewportFrustumTriangles, long ViewportOccludedTriangles,
        int GlbDrawnObjects, int GlbFrustumObjects, int GlbOccludedObjects,
        long GlbDrawnTriangles, long GlbFrustumTriangles, long GlbOccludedTriangles,
        long TerrainDrawnTriangles, long TerrainFrustumTriangles, long TerrainOccludedTriangles,
        int TerrainPlanes, int TerrainFrustumPlanes, int TerrainOccludedPlanes,
        int TerrainChunkCount, int TerrainChunkTests, int TerrainDrawnChunks,
        int TerrainFrustumChunks, int TerrainOccludedChunks, int TerrainOctreeNodesTested,
        int TerrainOctreeNodesPruned, int EditorMarkerFrustumObjects,
        int OccluderCount, bool OcclusionActive);

    /// <summary>
    /// Manages all editor-placed 3D objects (primitives + glb references).
    /// Handles lifecycle, rendering, shadow pass, and selection.
    /// </summary>
public unsafe class EditorObjectManager
{
    // ── 2D sprite pass scratch (per-frame reuse — zero steady-state alloc) ──
    private static readonly List<EditorObject> _scratchSprites2D = new();
    private static readonly List<EditorObject> _scratchCulledPlayers = new();
    private readonly List<ViewportOcclusion.OccluderData> _viewportOccluders = new();
    private readonly List<(AABB Bounds, int State)> _viewportCullBoxes = new();
    private readonly List<(AABB Bounds, int State)> _frozenViewportCullBoxes = new();
    private ViewportCullStatistics _frozenViewportCullStatistics;
    private bool _captureCullSnapshot;
    private bool _loggedNoOccluders;
    private bool _loggedWireframeHullFallback;

    /// <summary>Viewport occlusion culling master switch — ON by default. Owned by the
    /// Game settings (IDE Settings → Gameplay) and persisted in settings.json; the viewport
    /// toolbar no longer carries an OC button.</summary>
    public static bool ViewportOcclusionEnabled { get; set; } = true;
    public static bool ViewportCullDebug { get; set; }

    /// <summary>Occlusion source (OC button in the viewport toolbar): false = analytic
    /// convex-hull test against objects flagged Occluder (default); true = "OC: HZB", the
    /// max-depth pyramid of the previous viewport frame (any drawn geometry occludes, no
    /// per-object Occluder flag needed). Hull stays the default.</summary>
    public static bool ViewportOcclusionHzb { get; set; }
    public bool ViewportOcclusionActive { get; private set; }
    public int ViewportOccluderCount { get; private set; }
    public bool ViewportCullFreezeEnabled { get; set; }
    public ViewportCullStatistics ViewportCullDisplayStatistics => ViewportCullFreezeEnabled
        ? _frozenViewportCullStatistics
        : CaptureViewportCullStatistics();

    public void RequestViewportCullSnapshot()
    {
        _captureCullSnapshot = true;
        _frozenViewportCullBoxes.Clear();
    }
    public int ViewportDrawnObjects { get; private set; }
    public int ViewportFrustumObjects { get; private set; }
    public int ViewportOccludedObjects { get; private set; }
    public long ViewportDrawnTriangles { get; private set; }
    public long ViewportFrustumTriangles { get; private set; }
    public long ViewportOccludedTriangles { get; private set; }
    public int GlbDrawnObjects { get; private set; }
    public int GlbFrustumObjects { get; private set; }
    public int GlbOccludedObjects { get; private set; }
    public long GlbDrawnTriangles { get; private set; }
    public long GlbFrustumTriangles { get; private set; }
    public long GlbOccludedTriangles { get; private set; }
    public long TerrainDrawnTriangles { get; private set; }
    public long TerrainFrustumTriangles { get; private set; }
    public long TerrainOccludedTriangles { get; private set; }
    public int TerrainPlanes { get; private set; }
    public int TerrainFrustumPlanes { get; private set; }
    public int TerrainOccludedPlanes { get; private set; }
    public int TerrainChunkCount { get; private set; }
    public int TerrainChunkTests { get; private set; }
    public int TerrainDrawnChunks { get; private set; }
    public int TerrainFrustumChunks { get; private set; }
    public int TerrainOccludedChunks { get; private set; }
    public int TerrainOctreeNodesTested { get; private set; }
    public int TerrainOctreeNodesPruned { get; private set; }
    public int EditorMarkerFrustumObjects { get; private set; }

    /// <summary>Maximum occluders submitted to the viewport occlusion pass.</summary>
    internal const int ViewportOccluderLimit = 64;
    /// <summary>Side of the cube inscribed in a unit-diameter sphere (1/√3): an exact,
    /// conservative occluder substitute for a Sphere — a cube inside it can never hide
    /// anything the sphere itself would not hide.</summary>
    private const float InscribedSphereCubeScale = 0.57735026f;

    //  The occlusion test is purely ANALYTIC: it projects the occluder and compares 2D
    //  silhouettes plus depths, so it never reads GL state. The old glGet* gate
    //  (polygon mode, depth mask/func, cull face, stencil, clip planes…) silently
    //  disabled OC for perfectly renderable scenes — e.g. wireframe view, or a scene
    //  that leaves polygon offset / stencil enabled — which looked like "OC does
    //  nothing at all".

    /// <summary>Submit an object's volume as a viewport occluder. Box uses its world
    /// matrix (exact); Sphere uses the cube inscribed in it (conservative); GLB uses its
    /// bind-pose mesh AABB in world space — static AND skinned, so one GLB can hide
    /// another (static prop behind a rigged character). Default ON through
    /// EditorObject.IsOccluder; the box is slightly larger than the mesh, so a
    /// hollow/open model — or a rig whose animation leaves the bind pose — can untick it.
    /// Displaced geometry is skipped — its vertices no longer match the shape.</summary>
    private void AddViewportOccluders(EditorObject obj, Vector3 cameraPosition, Matrix4x4 viewProj)
    {
        if (obj.PbrVertexDisplaceEnabled) return;
        switch (obj.PrimitiveType)
        {
            case EditorPrimitiveType.Box:
            case EditorPrimitiveType.Sphere:
            {
                if (obj.Scale.X <= 0f || obj.Scale.Y <= 0f || obj.Scale.Z <= 0f) return;
                obj.EnsureResources();
                if (obj.PrimitiveTriangles <= 0 ||
                    !EditorObject.IsAABBInFrustum(viewProj, obj.WorldAABB)) return;
                var boxWorld = obj.PrimitiveType == EditorPrimitiveType.Box
                    ? obj.WorldMatrix
                    : obj.WorldMatrix * Matrix4x4.CreateScale(InscribedSphereCubeScale);
                var prepared = ViewportOcclusion.Prepare(boxWorld, cameraPosition, viewProj, obj.OcclusionTag);
                if (prepared.IsValid) _viewportOccluders.Add(prepared);
                return;
            }
            case EditorPrimitiveType.GlbReference:
            {
                // Occluder-side box: bind-pose mesh AABB, which a skinned GLB has too. Only
                // the CULL direction is guarded (TryGetGlbCullData) — a rig may hide things
                // without ever being culled itself.
                if (!obj.TryGetGlbOccluderBounds(out var glbBounds)) return;
                if (!EditorObject.IsAABBInFrustum(viewProj, glbBounds)) return;
                var prepared = ViewportOcclusion.PrepareBounds(glbBounds, cameraPosition, viewProj, obj.OcclusionTag);
                if (prepared.IsValid) _viewportOccluders.Add(prepared);
                return;
            }
        }
    }

    /// <summary>In-place insertion sort of the 2D sprite pass list: layer ascending,
    /// Player2D first on same-layer ties (the exact ordering the old
    /// OrderBy(layer).ThenBy(player-first) produced). Insertion sort is deliberately
    /// chosen over List.Sort: the list is NEARLY SORTED frame-to-frame (same sprites,
    /// same order) → steady-state ≈ O(n) with zero allocation; List.Sort would be
    /// O(n log n) + comparer delegate.
    private static void InsertionSortSprites2D(List<EditorObject> list)
    {
        for (int i = 1; i < list.Count; i++)
        {
            var key = list[i];
            int keyLayer = key.PrimitiveType == EditorPrimitiveType.Player2D ? key.Player2DRenderLayer : key.Sprite2DRenderLayer;
            int keyPlayer = key.PrimitiveType == EditorPrimitiveType.Player2D ? 0 : 1;
            int j = i - 1;
            while (j >= 0)
            {
                var cur = list[j];
                int curLayer = cur.PrimitiveType == EditorPrimitiveType.Player2D ? cur.Player2DRenderLayer : cur.Sprite2DRenderLayer;
                int curPlayer = cur.PrimitiveType == EditorPrimitiveType.Player2D ? 0 : 1;
                if (curLayer < keyLayer || (curLayer == keyLayer && curPlayer <= keyPlayer)) break;
                list[j + 1] = list[j];
                j--;
            }
            list[j + 1] = key;
        }
    }
    // ═══════════ 2D sprite frustum culling (perf: render HANYA yang terlihat kamera) ═══════════

    /// <summary>Cull telemetry passthrough (set on the 2D sprite pass below, on the
    /// shared per-object counters in EditorObject) — read right after the viewport
    /// draw for the toolbar badge.</summary>
    public static int Cull2DCulled => EditorObject.Cull2DCulled;
    public static int Cull2DTotal => EditorObject.Cull2DTotal;

    /// <summary>Frustum-cull check SATU PINTU untuk seluruh 2D sprite pass (Player2D,
    /// Sprite2D, portal — pemanggil lain: clock-advance pre-cull DrawPlayer2D).
    /// Kamera = argumen `camera` (pass ini selalu dipanggil dengan kamera yang sedang
    /// render — active camera aturan user). The padded art AABB is tested conservatively
    /// against the frustum planes, including boxes that cross or enclose the view.</summary>
    public static bool Is2DSpriteCulled(EditorObject obj, Camera camera, Matrix4x4? viewProj = null)
    {
        // PERF memo: the 2D pass culls every sprite ONCE (shared viewProj), then the
        // draw methods re-queried the SAME test with the same camera in the same frame
        // (AABB resolve + frustum test each time). The memo keyed
        // (frame, camera) makes that second query free; a new frame or a DIFFERENT
        // camera (second viewport in the same frame) recomputes. Callers passing an
        // explicit viewProj always compute fresh and refresh the memo.
        if (viewProj == null
            && obj.Cull2DMemoFrame == Glfw.FrameId
            && ReferenceEquals(obj.Cull2DMemoCamera, camera))
            return obj.Cull2DMemoResult;

        var vp = viewProj ?? camera.GetViewMatrix() * camera.GetProjectionMatrix();
        // Use the art bounds rather than the selection capsule; a repeated strip can
        // intersect the frustum even when none of its corners lies inside it.
        var (amin, amax) = obj.Get2DSpriteAABB();
        bool culled = !EditorObject.IsAABBInFrustum(vp, new AABB(amin, amax));
        obj.Cull2DMemoFrame = Glfw.FrameId;
        obj.Cull2DMemoCamera = camera;
        obj.Cull2DMemoResult = culled;
        return culled; // seluruh AABB di luar clip volume → skip draw
    }

    /// <summary>Debug boxes for the 2D cull pass — RED wire = culled (skipped),
    /// GREEN = rendered this pass. Drawn through the shared debug line shader,
    /// depth test OFF (overlay helper, never occluded by world geometry).</summary>
    private static void Draw2DCullDebugBoxes(Camera camera, Matrix4x4 viewProj)
    {
        GL.Disable(Const.GL_DEPTH_TEST);
        var red = new System.Numerics.Vector3(1f, 0.25f, 0.2f);
        var green = new System.Numerics.Vector3(0.2f, 0.95f, 0.35f);
        foreach (var obj in EditorObject.Debug2DCullObjects)
        {
            var (amin, amax) = obj.Get2DSpriteAABB();
            DarkEngine3D_gl_csharp.Engine.Helpers.DebugDraw.DrawAABBWireframe(
                new AABB(amin, amax),
                EditorObject.Debug2DCullCulled.Contains(obj) ? red : green, camera);
        }
        // Camera origin behind the FAR plane (clip W ≤ 0) = everything culled while
        // looking at the level — draw ONE amber box around the camera as the hint.
        float camW = viewProj.M14 * camera.Position.X + viewProj.M24 * camera.Position.Y
                   + viewProj.M34 * camera.Position.Z + viewProj.M44;
        if (camW <= 0f)
        {
            var amber = new System.Numerics.Vector3(1f, 0.75f, 0.1f);
            DarkEngine3D_gl_csharp.Engine.Helpers.DebugDraw.DrawAABBWireframe(
                new AABB(camera.Position - new System.Numerics.Vector3(0.25f),
                         camera.Position + new System.Numerics.Vector3(0.25f)), amber, camera);
        }
        GL.Enable(Const.GL_DEPTH_TEST);
    }

        private readonly List<EditorObject> _objects = [];
        // Per-type counters for incremental default names
        private int _boxCounter = 1;
        private int _sphereCounter = 1;
        private int _planeCounter = 1;
        private int _glbCounter = 1;
        private int _cameraCounter = 1;
        private int _lightCounter = 1;
        private int _skyCounter = 1;
        private int _map2dCounter = 1;
        private int _player2dCounter = 1;
        private int _sprite2dCounter = 1;
        private int _effect2dCounter = 1;
        private int _start2dCounter = 1;
        private int _cameraStart2dCounter = 1;
        private readonly uint _shaderProgram;
        private readonly int _modelLoc, _viewLoc, _projLoc;
        private readonly int _sunDirLoc, _realSunDirLoc, _lightColorLoc, _viewPosLoc;
        private readonly int _useFogLoc, _fogColorLoc;

        // Shadow uniforms for primitive shadow rendering
        private readonly uint _shadowShader;
        private readonly int _shadowModelLoc, _shadowLightSpaceLoc;

        /// <summary>Currently selected editor object (for Inspector + gizmo).</summary>
        public EditorObject? SelectedObject { get; set; }

        /// <summary>All editor objects.</summary>
        public IReadOnlyList<EditorObject> Objects => _objects;

        /// <summary>Number of editor objects.</summary>
        public int Count => _objects.Count;

        /// <summary>Delegate to fire when an object is selected via raycast.</summary>
        public Action<EditorObject?>? OnObjectSelected;

        /// <summary>Delegate fired whenever ANY editor object is removed (any removal path,
        /// not just the primary selection) so the IDE can prune its multi-selection set.</summary>
        public Action<EditorObject>? OnObjectRemoved;

        public EditorObjectManager()
        {
            _shaderProgram = Shader.GetShaderProgram();
            _modelLoc = GL.GetUniformLocation(_shaderProgram, "model");
            _viewLoc = GL.GetUniformLocation(_shaderProgram, "view");
            _projLoc = GL.GetUniformLocation(_shaderProgram, "projection");
            _sunDirLoc = GL.GetUniformLocation(_shaderProgram, "sunDir");
            _realSunDirLoc = GL.GetUniformLocation(_shaderProgram, "realSunDir");
            _lightColorLoc = GL.GetUniformLocation(_shaderProgram, "lightColor");
            _viewPosLoc = GL.GetUniformLocation(_shaderProgram, "viewPos");
            _useFogLoc = GL.GetUniformLocation(_shaderProgram, "useFog");
            _fogColorLoc = GL.GetUniformLocation(_shaderProgram, "fogColor");

            // Shadow shader (static)
            _shadowShader = Shader.GetShadowShaderProgram();
            _shadowModelLoc = GL.GetUniformLocation(_shadowShader, "model");
            _shadowLightSpaceLoc = GL.GetUniformLocation(_shadowShader, "lightSpaceMatrix");
        }

        /// <summary>Add a new editor object.</summary>
        public EditorObject Add(EditorObject obj)
        {
            _objects.Add(obj);
            return obj;
        }

        /// <summary>Remove an editor object.</summary>
        public bool Remove(EditorObject obj)
        {
            if (SelectedObject == obj)
            {
                SelectedObject = null;
                OnObjectSelected?.Invoke(null);
            }
            obj.Dispose();
            bool removed = _objects.Remove(obj);
            if (removed)
                OnObjectRemoved?.Invoke(obj);
            return removed;
        }

        /// <summary>Unlist an object WITHOUT disposing it — the instance (and its GPU
        /// handles) must survive so a later undo can re-insert the SAME object. Paired
        /// with <see cref="InsertAt"/>; regular Delete keeps using <see cref="Remove"/>.</summary>
        public void RemoveKeepAlive(EditorObject obj)
        {
            if (SelectedObject == obj)
            {
                SelectedObject = null;
                OnObjectSelected?.Invoke(null);
            }
            _objects.Remove(obj);
            OnObjectRemoved?.Invoke(obj);
        }

        /// <summary>Re-insert a previously removed object WITHOUT disposing or recreating
        /// it (Delete-undo support: the instance and its GPU handles must survive). Pass
        /// index -1 to append. Caller is responsible for not re-inserting duplicates.</summary>
        public void InsertAt(EditorObject obj, int index)
        {
            index = Math.Clamp(index, 0, _objects.Count);
            _objects.Insert(index, obj);
            // A delete undo brings the object back — it must claim its bake file again
            // (Dispose released the claim when the delete disposed it).
            obj.ReclaimSplatBakePath();
        }

        /// <summary>Index of an object in the manager list (-1 when absent) — captured
        /// before a Delete so the undo can restore it to the same position.</summary>
        public int IndexOf(EditorObject obj) => _objects.IndexOf(obj);

        /// <summary>Move an editor object from one index to another (for drag-drop reorder).</summary>
        public void MoveObject(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _objects.Count) return;
            if (toIndex < 0 || toIndex >= _objects.Count) return;
            if (fromIndex == toIndex) return;

            var obj = _objects[fromIndex];
            _objects.RemoveAt(fromIndex);
            int insertIdx = toIndex;
            if (fromIndex < toIndex) insertIdx--;
            insertIdx = Math.Clamp(insertIdx, 0, _objects.Count);
            _objects.Insert(insertIdx, obj);
        }

        /// <summary>Remove an editor object at index.</summary>
        public void RemoveAt(int index)
        {
            if (index >= 0 && index < _objects.Count)
            {
                var obj = _objects[index];
                if (SelectedObject == obj)
                {
                    SelectedObject = null;
                    OnObjectSelected?.Invoke(null);
                }
                obj.Dispose();
                _objects.RemoveAt(index);
                OnObjectRemoved?.Invoke(obj);
            }
        }

        /// <summary>Remove all editor objects.</summary>
        public void Clear()
        {
            SelectedObject = null;
            OnObjectSelected?.Invoke(null);
            foreach (var obj in _objects)
                obj.Dispose();
            _objects.Clear();
        }

        /// <summary>Get next incremental default name for a primitive type (per-type counter).</summary>
        public string GetNextName(EditorPrimitiveType type)
        {
            return type switch
            {
                EditorPrimitiveType.Plane => $"plane{_planeCounter++}",
                EditorPrimitiveType.Box => $"box{_boxCounter++}",
                EditorPrimitiveType.Sphere => $"sphere{_sphereCounter++}",
                EditorPrimitiveType.GlbReference => $"glb{_glbCounter++}",
                EditorPrimitiveType.Camera => $"camera{_cameraCounter++}",
                EditorPrimitiveType.Light => $"light{_lightCounter++}",
                EditorPrimitiveType.Sky => $"sky{_skyCounter++}",
                EditorPrimitiveType.Map2D => $"map2d{_map2dCounter++}",
                EditorPrimitiveType.Player2D => $"player{_player2dCounter++}",
                EditorPrimitiveType.Sprite2D => $"sprite{_sprite2dCounter++}",
                EditorPrimitiveType.Effect2D => $"effect{_effect2dCounter++}",
                EditorPrimitiveType.Start2D => $"start{_start2dCounter++}",
                EditorPrimitiveType.CameraStart2D => $"camstart{_cameraStart2dCounter++}",
                _ => $"object{_boxCounter++}",
            };
        }

        /// <summary>Scan existing objects and advance counters past any loaded names
        /// so the next GetNextName() never produces a duplicate.</summary>
        public void SyncCounters()
        {
            foreach (var obj in _objects)
            {
                if (obj == null) continue;
                if (int.TryParse(ExtractNumberSuffix(obj.Name), out int num))
                {
                    switch (obj.PrimitiveType)
                    {
                        case EditorPrimitiveType.Box:       if (num >= _boxCounter) _boxCounter = num + 1; break;
                        case EditorPrimitiveType.Sphere:    if (num >= _sphereCounter) _sphereCounter = num + 1; break;
                        case EditorPrimitiveType.Plane:     if (num >= _planeCounter) _planeCounter = num + 1; break;
                        case EditorPrimitiveType.GlbReference: if (num >= _glbCounter) _glbCounter = num + 1; break;
                        case EditorPrimitiveType.Camera:    if (num >= _cameraCounter) _cameraCounter = num + 1; break;
                        case EditorPrimitiveType.Light:     if (num >= _lightCounter) _lightCounter = num + 1; break;
                        case EditorPrimitiveType.Sky:       if (num >= _skyCounter) _skyCounter = num + 1; break;
                        case EditorPrimitiveType.Map2D:     if (num >= _map2dCounter) _map2dCounter = num + 1; break;
                        case EditorPrimitiveType.Player2D:  if (num >= _player2dCounter) _player2dCounter = num + 1; break;
                        case EditorPrimitiveType.Sprite2D:  if (num >= _sprite2dCounter) _sprite2dCounter = num + 1; break;
                        case EditorPrimitiveType.Effect2D:  if (num >= _effect2dCounter) _effect2dCounter = num + 1; break;
                        case EditorPrimitiveType.Start2D:   if (num >= _start2dCounter) _start2dCounter = num + 1; break;
                        case EditorPrimitiveType.CameraStart2D: if (num >= _cameraStart2dCounter) _cameraStart2dCounter = num + 1; break;
                    }
                }
            }
        }

        private static string ExtractNumberSuffix(string name)
        {
            // "box3" → "3", "box" → ""
            int i = name.Length - 1;
            while (i >= 0 && char.IsDigit(name[i])) i--;
            return name.Substring(i + 1);
        }

        /// <summary>Create and add a primitive editor object at the given position.</summary>
        public EditorObject AddPrimitive(EditorPrimitiveType type, Vector3 position)
        {
            var obj = new EditorObject(type, GetNextName(type))
            {
                Position = position,
                Scale = type == EditorPrimitiveType.Sphere ? new Vector3(1f, 1f, 1f)
                       : type == EditorPrimitiveType.Plane ? new Vector3(100f, 0.05f, 100f)
                       : type == EditorPrimitiveType.Camera ? new Vector3(0.5f, 0.4f, 0.6f)
                       : type == EditorPrimitiveType.Map2D ? new Vector3(1600f, 1f, 640f)
                       : Vector3.One, // Box / Light / Sky / Player2D / Start2D
                Color = type switch
                {
                    EditorPrimitiveType.Plane => new Vector3(0.3f, 0.7f, 0.3f),
                    EditorPrimitiveType.Box => new Vector3(0.7f, 0.3f, 0.3f),
                    EditorPrimitiveType.Sphere => new Vector3(0.3f, 0.3f, 0.7f),
                    EditorPrimitiveType.Camera => new Vector3(0.2f, 0.7f, 0.8f),
                    EditorPrimitiveType.Light => new Vector3(1.0f, 0.85f, 0.3f),
                    EditorPrimitiveType.Sky => new Vector3(0.5f, 0.7f, 1.0f),
                    _ => new Vector3(0.8f, 0.8f, 0.8f),
                }
            };
            if (type != EditorPrimitiveType.GlbReference)
                obj.InitGPU();
            _objects.Add(obj);
            return obj;
        }

        /// <summary>Sky automatically drives a Direct light (bug #7): when a Sky is placed
        /// (or a scene containing a Sky is loaded) this ensures a DIRECT light exists —
        /// reusing an existing one if present, otherwise creating a new marker next to the
        /// sky aimed at the sky's current sun direction.</summary>
        public void EnsureDirectLightForSky(EditorObject skyObj)
        {
            foreach (var o in _objects)
            {
                if (o != null && o.PrimitiveType == EditorPrimitiveType.Light
                    && o.LightTypeEnum == LightType.Direct)
                {
                    Console.WriteLine($"[EditorObjectManager] Sky '{skyObj.Name}' reuses existing Direct light '{o.Name}'");
                    return;
                }
            }

            var light = new EditorObject(EditorPrimitiveType.Light, GetNextName(EditorPrimitiveType.Light))
            {
                Position = skyObj.Position + new Vector3(0f, 6f, 0f),
                Scale = Vector3.One,
                Color = new Vector3(1.0f, 0.95f, 0.85f),
                LightTypeEnum = LightType.Direct,
                LightDirection = skyObj.GetSkySunDirection(),
                LightIntensity = 1f,
                ShowLightGizmo = true,
            };
            light.InitGPU();
            _objects.Add(light);
            Console.WriteLine($"[EditorObjectManager] Sky '{skyObj.Name}' created Direct light '{light.Name}'");
        }

        /// <summary>After a scene is loaded/edited, make sure every Sky has a Direct light
        /// (bug #7). Runs as a single pass AFTER all objects are in place so it never
        /// creates a duplicate when the scene file already contains its own Direct light.</summary>
        public void EnsureDirectLightForAnySky()
        {
            EditorObject? sky = null;
            bool hasDirect = false;
            foreach (var o in _objects)
            {
                if (o == null) continue;
                if (sky == null && o.PrimitiveType == EditorPrimitiveType.Sky) sky = o;
                if (o.PrimitiveType == EditorPrimitiveType.Light && o.LightTypeEnum == LightType.Direct)
                    hasDirect = true;
            }
            if (sky != null && !hasDirect)
                EnsureDirectLightForSky(sky);
        }

        /// <summary>Lowest "&lt;baseName&gt;&lt;n&gt;" (n from 1) that no other object uses;
        /// <paramref name="self"/> is skipped because a restored object is already in the list.</summary>
        private string NextFreeName(string baseName, EditorObject? self = null)
        {
            int n = 1;
            while (_objects.Any(o => !ReferenceEquals(o, self) &&
                                     string.Equals(o?.Name, $"{baseName}{n}", StringComparison.OrdinalIgnoreCase)))
                n++;
            return $"{baseName}{n}";
        }

        /// <summary>Name for an object restored from a scene file: its saved name, numbered only
        /// when another object already holds it (scenes saved before GLB references were numbered
        /// contain several "EditorObject" rows), or this type's incremental name when the file has
        /// none. An already-numbered duplicate continues its stem ("box3" → "box4", never
        /// "box31").</summary>
        public string RestoredName(EditorObject self, string? savedName)
        {
            if (string.IsNullOrWhiteSpace(savedName)) return GetNextName(self.PrimitiveType);
            string name = savedName.Trim();
            bool taken = _objects.Any(o => !ReferenceEquals(o, self) &&
                                           string.Equals(o?.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!taken) return name;
            string stem = name[..(name.Length - ExtractNumberSuffix(name).Length)];
            return NextFreeName(stem.Length > 0 ? stem : name, self);
        }

        /// <summary>Add a glb reference object (renders the GLB model in the viewport,
        /// casts shadows, and supports the gizmo/outline like other primitives).</summary>
        public EditorObject AddGlbReference(string glbPath, Vector3 position)
        {
            var obj = new EditorObject(EditorPrimitiveType.GlbReference)
            {
                // Numbered name: the class default left every reference as "EditorObject",
                // so the outliner showed a stack of identically named rows.
                Name = NextFreeName("EditorObject"),
                Position = position,
                GlbFilePath = glbPath,
                Scale = Vector3.One,
                Color = new Vector3(1f, 1f, 1f),
                CastShadow = true,
                IsVisible = true,
            };
            // Snapshot initial state for undo
            obj.LastGizmoPosition = obj.Position;
            obj.LastGizmoRotation = obj.RotationEuler;
            obj.LastGizmoScale = obj.Scale;
            obj.LastGizmoPivot = obj.GizmoPivotOverride;
            _objects.Add(obj);
            return obj;
        }

        /// <summary>Duplicate an editor object (all properties copied, offset slightly in X) and add it to the scene.</summary>
        public EditorObject Duplicate(EditorObject source)
        {
            var clone = new EditorObject(source.PrimitiveType)
            {
                Name = GetNextName(source.PrimitiveType),
                Position = source.Position + new Vector3(1f, 0f, 0f),
                RotationEuler = source.RotationEuler,
                Scale = source.Scale,
                Color = source.Color,
                TexturePath = source.TexturePath,
                CastShadow = source.CastShadow,
                IsVisible = source.IsVisible,
                IsOccluderOverride = source.IsOccluderOverride,
                IsOccludee = source.IsOccludee,
                GlbFilePath = source.GlbFilePath,
                CameraFov = source.CameraFov,
                CameraNear = source.CameraNear,
                CameraFar = source.CameraFar,
                IsDefaultCamera = false,
                LightDirection = source.LightDirection,
                LightIntensity = source.LightIntensity,
                LightTypeEnum = source.LightTypeEnum,
                LightConeAngle = source.LightConeAngle,
                LightPointRadius = source.LightPointRadius,
                SkyTimeOfDay = source.SkyTimeOfDay,
                SkySunPitch = source.SkySunPitch,
                SkySunYaw = source.SkySunYaw,
                SkyCloudCoverage = source.SkyCloudCoverage,
                SkySunIntensity = source.SkySunIntensity,
                SkyTimeAnimSpeed = source.SkyTimeAnimSpeed,
                SkyTimeAnimPaused = source.SkyTimeAnimPaused,
                ShowFrustum = source.ShowFrustum,
                ShowLightGizmo = source.ShowLightGizmo,
                ShowSkyGizmo = source.ShowSkyGizmo,
                SkySettings = source.SkySettings.Clone(),
                GizmoPivotOverride = null,
                TexSettings = source.TexSettings.Clone(),
                PbrTexSettings = source.PbrTexSettings.Select(s => s.Clone()).ToArray(),
                PbrRandomTiling = source.PbrRandomTiling,   // bool — copies directly
                PbrVertexDisplaceEnabled = source.PbrVertexDisplaceEnabled,
                PbrTexTiling = source.PbrTexTiling,
                // Terrain displacement tuning (TerrainPanel) — was missing from the
                // duplicate, resetting the Inspector values on the clone.
                TerrainHeightScale = source.TerrainHeightScale,
                TerrainHeightOffset = source.TerrainHeightOffset,
                TerrainHeightStrength = source.TerrainHeightStrength,
                PbrPrimTessellation = source.PbrPrimTessellation,
                SphereType = source.SphereType,
                // Terrain LOD travels with the duplicate too (the mesh rebuilds from the
                // same segments/chunks, so the levels come out identical).
                TerrainLodEnabled = source.TerrainLodEnabled,
                TerrainLodDistance0 = source.TerrainLodDistance0,
                TerrainLodDistance1 = source.TerrainLodDistance1,
                TerrainLodDistance2 = source.TerrainLodDistance2,
                TerrainStitchEnabled = source.TerrainStitchEnabled,
                TerrainStitchHeight = source.TerrainStitchHeight,
                TerrainStitchCull = source.TerrainStitchCull,
                // Terrain elevation + splat state travels with the duplicate (the
                // weight map path is SHARED — the file is scene-persistent; the clone
                // drops its live GPU buffers and re-decodes/recomputes lazily).
                TerrainHeightPath = source.TerrainHeightPath,
                TerrainBaseHeight = source.TerrainBaseHeight,
                TerrainHeightTilingX = source.TerrainHeightTilingX,
                TerrainHeightTilingY = source.TerrainHeightTilingY,
                SculptDeltaPath = source.SculptDeltaPath,
                TerrainSculptAmp = source.TerrainSculptAmp,
                SplatMapPath = source.SplatMapPath,
                SplatHeightBandsEnabled = source.SplatHeightBandsEnabled,
                SplatHeightLayerCount = source.SplatHeightLayerCount,
                SplatHeightLayerFeather = source.SplatHeightLayerFeather,
                SplatSlopeEnabled = source.SplatSlopeEnabled,
                SplatSlopeLayer = source.SplatSlopeLayer,
                SplatSlopeThreshold = source.SplatSlopeThreshold,
                SplatSlopeFade = source.SplatSlopeFade,
                SplatSlopeUseDegrees = source.SplatSlopeUseDegrees,
                SplatSlopeAngleMin = source.SplatSlopeAngleMin,
                SplatSlopeAngleMax = source.SplatSlopeAngleMax,
                SplatSlopeOverridePaint = source.SplatSlopeOverridePaint,
                SplatSlopePaintFloor = source.SplatSlopePaintFloor,
                SplatSlopeTiling = source.SplatSlopeTiling,
                SplatSlope2Enabled = source.SplatSlope2Enabled,
                SplatSlope2Layer = source.SplatSlope2Layer,
                SplatSlope2AngleMin = source.SplatSlope2AngleMin,
                SplatSlope2AngleMax = source.SplatSlope2AngleMax,
                SplatSlope2Tiling = source.SplatSlope2Tiling,
                SplatSlope3Enabled = source.SplatSlope3Enabled,
                SplatSlope3Layer = source.SplatSlope3Layer,
                SplatSlope3AngleMin = source.SplatSlope3AngleMin,
                SplatSlope3AngleMax = source.SplatSlope3AngleMax,
                SplatSlope3Tiling = source.SplatSlope3Tiling,
                // ── Everything the .ing save persists MUST travel with the duplicate:
                // a clone used to lose the whole 2D/NPC block (sprite art, actions,
                // camera follow, render layers…) and only healed after a save+reload.
                Player2DSpriteSheet = source.Player2DSpriteSheet,
                Player2DAnimationClip = source.Player2DAnimationClip,
                Player2DHeight = source.Player2DHeight,
                Player2DCapsuleRadius = source.Player2DCapsuleRadius,
                Player2DCapsuleHeight = source.Player2DCapsuleHeight,
                Player2DCapsuleOffsetX = source.Player2DCapsuleOffsetX,
                Player2DCapsuleOffsetY = source.Player2DCapsuleOffsetY,
                Player2DShowCapsule = source.Player2DShowCapsule,
                Player2DGravity = source.Player2DGravity,
                Player2DMoveSpeed = source.Player2DMoveSpeed,
                Player2DRunSpeed = source.Player2DRunSpeed,
                Player2DJumpForce = source.Player2DJumpForce,
                Player2DGravityScale = source.Player2DGravityScale,
                Player2DAcceleration = source.Player2DAcceleration,
                Player2DDeceleration = source.Player2DDeceleration,
                Player2DAirControl = source.Player2DAirControl,
                Player2DCoyoteTime = source.Player2DCoyoteTime,
                Player2DStepHeight = source.Player2DStepHeight,
                Player2DJumpBuffer = source.Player2DJumpBuffer,
                Player2DJumpCutMultiplier = source.Player2DJumpCutMultiplier,
                Player2DFacingRight = source.Player2DFacingRight,
                Player2DFlipFacing = source.Player2DFlipFacing,
                Player2DRenderLayer = source.Player2DRenderLayer,
                NpcDialogueId = source.NpcDialogueId,
                NpcAlertImagePath = source.NpcAlertImagePath,
                NpcDisplayName = source.NpcDisplayName,
                NpcBadgeSize = source.NpcBadgeSize,
                NpcBadgeLift = source.NpcBadgeLift,
                NpcPromptPos = source.NpcPromptPos,
                Sprite2DLoop = source.Sprite2DLoop,
                Sprite2DSpeed = source.Sprite2DSpeed,
                Sprite2DStartOffset = source.Sprite2DStartOffset,
                Sprite2DFacingRight = source.Sprite2DFacingRight,
                Sprite2DRenderLayer = source.Sprite2DRenderLayer,
                Sprite2DWorldWidth = source.Sprite2DWorldWidth,
                Sprite2DGlow = source.Sprite2DGlow,
                Player2DGlow = source.Player2DGlow,
                Sprite2DGlowColor = source.Sprite2DGlowColor,
                Player2DGlowColor = source.Player2DGlowColor,
                Sprite2DGlowFlicker = source.Sprite2DGlowFlicker,
                Player2DGlowFlicker = source.Player2DGlowFlicker,
                CameraFollowSpeed = source.CameraFollowSpeed,
                CameraDeadZoneWidth = source.CameraDeadZoneWidth,
                CameraDeadZoneHeight = source.CameraDeadZoneHeight,
                CameraVerticalThreshold = source.CameraVerticalThreshold,
                CameraReturnSpeed = source.CameraReturnSpeed,
                CameraLookAhead = source.CameraLookAhead,
                CameraViewOffset = source.CameraViewOffset,
                Effect2DPreset = source.Effect2DPreset,
                Effect2DEmitRate = source.Effect2DEmitRate,
                Effect2DWindFactor = source.Effect2DWindFactor,
                Effect2DOffsetY = source.Effect2DOffsetY,
                Effect2DEnabled = source.Effect2DEnabled,
                Map2dShowGrid = source.Map2dShowGrid,
                Map2dShowTriggers = source.Map2dShowTriggers,
                Map2dGridColor = source.Map2dGridColor,
            };
            // Animation actions (deep-copied — shared instances would mirror edits),
            // equipment bindings and the tilemap payload (deep data clone; the GPU
            // texture rebuilds lazily on first draw).
            foreach (var a in source.Actions) clone.Actions.Add(a.Clone());
            clone.Equipment.Slots.Clear();
            foreach (var kv in source.Equipment.Slots) clone.Equipment.Slots[kv.Key] = kv.Value;
            foreach (var dl in source.DirectEquipment) clone.DirectEquipment.Add(dl.Clone());
            if (source.Map2dTilemap != null)
                clone.Map2dTilemap = Tilemap2D.FromData(source.Map2dTilemap.ToData());
            clone.Map2dParallaxLayers = source.Map2dParallaxLayers == null ? null
                : source.Map2dParallaxLayers.Select(p => p.Clone()).ToList();
            for (int l = 1; l <= 3; l++)
            {
                for (int m = 0; m < 6; m++)
                    clone.SetSplatLayerPath(m, l, source.GetSplatLayerPath(m, l));
                clone.SplatLayerTint[l] = source.SplatLayerTint[l];
                clone.SplatLayerTiling[l] = source.SplatLayerTiling[l];
            }
            for (int l = 0; l < 4; l++)
                clone.SplatHeightBands[l] = source.SplatHeightBands[l];
            if (clone.PrimitiveType != EditorPrimitiveType.GlbReference)
                clone.InitGPU();
            _objects.Add(clone);
            return clone;
        }

        /// <summary>Draw all editor objects (called from the game scene rendering loop).
        /// If <paramref name="wireframeColor"/> is set and objects are selected,
        /// every selected object is also rendered as a wireframe line outline in that color
        /// (multi-select supported via <paramref name="selectedObjects"/>).
        /// </summary>
        /// <summary>Make sure every GLB reference has its model loaded for this frame.
        /// Cheap after the first frame (one resolved-path + file-stamp check per object).
        /// Also called when the editor renders a scene without a shadow pass.</summary>
        public void PrepareGlbReferences()
        {
            for (int i = 0; i < _objects.Count; i++)
            {
                var o = _objects[i];
                if (o.PrimitiveType == EditorPrimitiveType.GlbReference && o.IsVisible)
                    o.PrepareGlb();
            }
        }

        public void Draw(Camera camera, Lights light, CSM? csm = null, Vector3? wireframeColor = null,
            IReadOnlyCollection<EditorObject>? selectedObjects = null, bool showSkyGizmo = true, bool showEditorGizmos = true,
            bool captureViewportCullSnapshot = true, Camera? viewportCullCamera = null,
            bool recordViewportPick = true, bool depthOcclusionAllowed = true)
        {
            ViewportDrawnObjects = ViewportFrustumObjects = ViewportOccludedObjects = 0;
            ViewportDrawnTriangles = ViewportFrustumTriangles = ViewportOccludedTriangles = 0;
            GlbDrawnObjects = GlbFrustumObjects = GlbOccludedObjects = 0;
            GlbDrawnTriangles = GlbFrustumTriangles = GlbOccludedTriangles = 0;
            TerrainDrawnTriangles = TerrainFrustumTriangles = TerrainOccludedTriangles = 0;
            TerrainPlanes = TerrainFrustumPlanes = TerrainOccludedPlanes = 0;
            TerrainChunkCount = TerrainChunkTests = TerrainDrawnChunks = 0;
            TerrainFrustumChunks = TerrainOccludedChunks = 0;
            TerrainOctreeNodesTested = TerrainOctreeNodesPruned = 0;
            EditorMarkerFrustumObjects = 0;
            ViewportOcclusionActive = false;
            ViewportOccluderCount = 0;
            _viewportOccluders.Clear();
            _viewportCullBoxes.Clear();
            if (_objects.Count == 0) return;

            // Models are loaded up front so nothing loads inside the draw loops below
            // (see RenderShadowPass for why).
            PrepareGlbReferences();

            GL.UseProgram(_shaderProgram);

            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            var cullCamera = viewportCullCamera ?? camera;
            var viewportCullViewProj = cullCamera.GetViewMatrix() * cullCamera.GetProjectionMatrix();

            // OC: HZB — usable only when the captured depth belongs to THIS pass's camera
            // (the pyramid is copied from the viewport render) and never in the camera
            // preview inset, whose depth buffer is the viewport's rather than its own.
            //
            // WIREFRAME (F2) is NOT a valid HZB source: with glPolygonMode(GL_LINE) only the
            // triangle EDGES write depth, so every pyramid tile that should hold a solid
            // wall keeps its cleared value (max-depth pyramid takes the MAX, so a single
            // untouched pixel pins the whole tile to the far plane). Result: the farther the
            // camera / the coarser the terrain LOD, the fewer depth lines land in a tile and
            // the less OC culls — near geometry appeared to "work", distant geometry never
            // culled. Wireframe therefore always uses the analytic hull test (pure geometry,
            // independent of polygon mode); the manager also gathers hull occluders again.
            bool hzbUsable = !Keyboard.GetIsWireframe() && ViewportOcclusionHzb && depthOcclusionAllowed
                && ReferenceEquals(cullCamera, camera) && ViewportHZB.Valid;
            if (ViewportOcclusionHzb && !hzbUsable && !_loggedWireframeHullFallback && Keyboard.GetIsWireframe())
            {
                _loggedWireframeHullFallback = true;
                Console.WriteLine("[OC] Wireframe is on — HZB cannot read a line-only depth buffer, using the hull test instead.");
            }
            var hzbViewProj = view * proj;
            EditorObject.UseViewportHZB = hzbUsable;
            GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(_projLoc, 1, false, (float*)&proj);

            GL.Uniform3f(_sunDirLoc, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
            if (_realSunDirLoc >= 0) GL.Uniform3f(_realSunDirLoc, light.RealSunDir.X, light.RealSunDir.Y, light.RealSunDir.Z);
            GL.Uniform3f(_lightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(_viewPosLoc, camera.Position.X, camera.Position.Y, camera.Position.Z);

            // ── Fog (enable, mode, color, density, start/end, height — Config.FogSettings) ──
            Visual.FogUniforms.UploadMain(_shaderProgram, light);

            // ── Local point/spot lights (from editor Light markers) ──
            light.UploadLocalLights(_shaderProgram);

            // ── 2D lighting: snapshot this frame's sun + Point/Spot markers for the
            //    sprite / tilemap / particle passes (they share SpriteLighting2D). ──
            SpriteLighting2D.Capture(light);

            // ── Send shadow uniforms if CSM provided ──
            if (csm != null)
            {
                int shadowMap0Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap0");
                int shadowMap1Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap1");
                int shadowMap2Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap2");
                int lightSpaceLoc0 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[0]");
                int lightSpaceLoc1 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[1]");
                int lightSpaceLoc2 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[2]");
                int cascadeEndsLoc0 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[0]");
                int cascadeEndsLoc1 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[1]");
                int cascadeEndsLoc2 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[2]");

                GL.Uniform1i(shadowMap0Loc, 6);
                GL.Uniform1i(shadowMap1Loc, 7);
                GL.Uniform1i(shadowMap2Loc, 8);
                unsafe
                {
                    fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                        GL.UniformMatrix4fv(lightSpaceLoc0, 1, false, p0);
                    fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                        GL.UniformMatrix4fv(lightSpaceLoc1, 1, false, p1);
                    fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                        GL.UniformMatrix4fv(lightSpaceLoc2, 1, false, p2);
                }
                GL.Uniform1f(cascadeEndsLoc0, csm.CascadeEnds[0]);
                GL.Uniform1f(cascadeEndsLoc1, csm.CascadeEnds[1]);
                GL.Uniform1f(cascadeEndsLoc2, csm.CascadeEnds[2]);
            }

            GL.Enable(Const.GL_DEPTH_TEST);
            // Face culling/winding state is applied by the scene's RenderProperties
            // (see OpenGL.ApplySceneProperties) — do NOT force-enable culling here.

            var primitiveViewProj = viewportCullViewProj;
            bool useOcclusion = ViewportOcclusionEnabled && _shaderProgram != 0;
            // OC: HZB needs no occluder gather — the depth pyramid is the occluder.
            if (useOcclusion && !hzbUsable)
            {
                // Build the current-frame occluder set BEFORE visiting targets, otherwise
                // a wall later in Hierarchy order could never hide an earlier object.
                // Only objects marked "Occluder" contribute (EditorObject.IsOccluder).
                for (int i = 0; i < _objects.Count && _viewportOccluders.Count < ViewportOccluderLimit; i++)
                {
                    var candidate = _objects[i];
                    if (!candidate.IsVisible || !candidate.IsOccluder) continue;
                    AddViewportOccluders(candidate, cullCamera.Position, primitiveViewProj);
                }
            }
            ViewportOcclusionActive = useOcclusion;
            ViewportOccluderCount = _viewportOccluders.Count;
            if (useOcclusion && ViewportOccluderCount == 0 && !_loggedNoOccluders)
            {
                _loggedNoOccluders = true;
                Console.WriteLine("[OC] Enabled, but no visible Box/Sphere/GLB is marked as an occluder — nothing can be hidden yet. Tick 'Occluder' in Inspector → Flags.");
            }
            bool captureCullDebug = ViewportCullDebug && (!ViewportCullFreezeEnabled || _captureCullSnapshot);
            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                //  Picking follows THIS frame's draw decision (see IsViewportPickable): an
                //  object the cull pass skips must not be clickable, one that is drawn must
                //  stay clickable. Reset here, then the cull branches below mark it.
                if (recordViewportPick)
                {
                    obj.ViewportCullState = 0;
                    obj.ViewportCullStateFrame = Glfw.FrameId;
                }
                //  "Occludee" OFF = never skipped by the occlusion test (frustum culling
                //  still applies): the escape hatch for geometry that must not pop.
                bool occludee = useOcclusion && obj.IsOccludee;
                obj.ViewportOccluders = occludee ? _viewportOccluders : null;
                obj.ViewportCullDebugCapture = captureCullDebug;
                obj.ViewportCullViewProjection = viewportCullViewProj;
                obj.ViewportCullPosition = cullCamera.Position;
                bool terrainPlane = obj.IsVisible && obj.PrimitiveType == EditorPrimitiveType.Plane;
                if (terrainPlane) TerrainPlanes++;
                bool measured = obj.IsVisible
                    && obj.PrimitiveType is EditorPrimitiveType.Box or EditorPrimitiveType.Sphere;
                // Chunked planes must reach their draw path even when the aggregate
                // bounds miss the view, so each chunk gets its own cull/debug result.
                bool wholePlaneCull = terrainPlane && obj.PbrVertexChunk <= 1;
                if (measured || wholePlaneCull)
                {
                    obj.EnsureResources();
                    var bounds = obj.ViewportCullBounds;
                    int triangles = obj.PrimitiveTriangles;
                    //  A Sphere is culled as a SPHERE: its projected disk (centre + radius),
                    //  not the 8 corners of the box that encloses it. The AABB test only
                    //  culled it from far away — up close the box corners projected past the
                    //  occluder's silhouette, so a fully hidden sphere stayed visible.
                    bool sphereOccludee = obj.PrimitiveType == EditorPrimitiveType.Sphere;
                    Vector3 sphereCenter = (bounds.Min + bounds.Max) * 0.5f;
                    Vector3 sphereSize = bounds.Max - bounds.Min;
                    float sphereRadius = 0.5f * MathF.Max(sphereSize.X, MathF.Max(sphereSize.Y, sphereSize.Z));
                    int state = 0;
                    if (!EditorObject.IsAABBInFrustum(primitiveViewProj, bounds))
                    {
                        state = 1;
                        if (measured)
                        {
                            ViewportFrustumObjects++;
                            ViewportFrustumTriangles += triangles;
                        }
                        else { TerrainFrustumPlanes++; TerrainFrustumTriangles += triangles; }
                    }
                    else if (hzbUsable)
                    {
                        // OC: HZB — the previous frame's max-depth pyramid is the occluder.
                        if (sphereOccludee
                            ? ViewportHZB.IsOccludedSphere(sphereCenter, sphereRadius, hzbViewProj)
                            : ViewportHZB.IsOccluded(bounds, hzbViewProj))
                        {
                            state = 2;
                            if (measured)
                            {
                                ViewportOccludedObjects++;
                                ViewportOccludedTriangles += triangles;
                            }
                            else { TerrainOccludedPlanes++; TerrainOccludedTriangles += triangles; }
                        }
                    }
                    else if (occludee)
                    {
                        foreach (var occluder in _viewportOccluders)
                            if (sphereOccludee
                                ? ViewportOcclusion.IsOccludedSphere(sphereCenter, sphereRadius, occluder, primitiveViewProj, obj.OcclusionTag)
                                : ViewportOcclusion.IsOccludedPrepared(bounds, occluder, primitiveViewProj, obj.OcclusionTag))
                            {
                                state = 2;
                                if (measured)
                                {
                                    ViewportOccludedObjects++;
                                    ViewportOccludedTriangles += triangles;
                                }
                                else { TerrainOccludedPlanes++; TerrainOccludedTriangles += triangles; }
                                break;
                            }
                    }
                    if (captureCullDebug) _viewportCullBoxes.Add((bounds, state));
                    if (state != 0)
                    {
                        if (recordViewportPick) obj.ViewportCullState = state;
                        continue;
                    }
                }
                obj.Draw(
                    _modelLoc, _viewLoc, _projLoc,
                    _sunDirLoc, _lightColorLoc, _viewPosLoc,
                    _useFogLoc, _fogColorLoc,
                    camera, light, csm);
                if (measured && obj.ViewportSubmittedTriangles > 0)
                {
                    ViewportDrawnObjects++;
                    ViewportDrawnTriangles += obj.ViewportSubmittedTriangles;
                }
                if (terrainPlane)
                {
                    TerrainDrawnTriangles += obj.ViewportSubmittedTriangles;
                    TerrainFrustumTriangles += obj.ViewportFrustumSkippedTriangles;
                    TerrainOccludedTriangles += obj.ViewportOcclusionSkippedTriangles;
                    if (obj.PbrChunkCount > 1)
                    {
                        TerrainChunkCount += obj.PbrChunkCount;
                        TerrainChunkTests += obj.PbrChunksTested;
                        TerrainDrawnChunks += obj.PbrChunksDrawn;
                        TerrainOctreeNodesTested += obj.PbrOctreeNodesTested;
                        TerrainOctreeNodesPruned += obj.PbrOctreeNodesPruned;
                    }
                    TerrainFrustumChunks += obj.PbrChunksCulled;
                    TerrainOccludedChunks += obj.PbrChunksOccluded;
                    if (captureCullDebug) _viewportCullBoxes.AddRange(obj.ViewportTerrainCullBoxes);
                }
            }

            GL.BindVertexArray(0);

            // ── GLB reference models (gltf shader — PBR + CSM shadow reception) ──
            DrawGlbReferences(camera, cullCamera, viewportCullViewProj, light, csm, captureCullDebug,
                recordViewportPick, hzbUsable, hzbViewProj);

            // ── 2D sprites: Player2D + Sprite2D in ONE layer-sorted pass ──
            // Digabung dari dua pass terpisah (semua player dulu, lalu semua sprite):
            // urutan painter ANTAR-PASS mengabaikan layer — chest layer -1 yang harusnya
            // di belakang player (layer 0) ikut pass sprite dan MENUTUPI player. Satu
            // pass terurut RenderLayer membuat layer -1 benar-benar di belakang.
            // Tie di layer sama: Player2D dulu, sprite dekor di atasnya (perilaku lama).
            //
            // FRUSTUM CULLING (2D): hanya sprite yang menyentuh frustum kamera aktif
            // yang digambar — yang di luar DIHAPUS dari draw call (perf: objek level
            // besar tidak lagi digambar tak terlihat). Kamera = yang dipakai pass ini
            // (viewport editor / kamera gameplay aktif). Jam animasi tetap dikelola
            // DI DALAM DrawPlayer2D/DrawSprite2D masing-masing (player terus maju
            // walau di luar layar; dekoratif *timed* sengaja membeku off-screen —
            // animasinya diukur dalam waktu TAYANG, bukan waktu dunia). Portal
            // digambar terpisah dari player yang ter-cull (host-nya DrawPlayer2D).
            EditorObject.Cull2DCulled = 0;
            EditorObject.Cull2DTotal = 0;
            var viewProj = view * proj;
            bool cullDebug = EditorObject.Show2DCullDebug;
            EditorObject.Debug2DCullObjects.Clear();
            EditorObject.Debug2DCullCulled.Clear();
            // PERF: the 2D sprite pass used `.Where(...).OrderBy(...).ThenBy(...)`
            // EVERY frame — that allocates 3 delegates + 2 iterators + sort buffers
            // 60×/s (GC stutter = the 22 FPS dip). Fix: fill a SCRATCH list in one
            // plain pass, then INSERTION-SORT it in place. The scratch stays mostly
            // sorted frame-to-frame (same sprites, same order) → insertion sort runs
            // ~O(n) on the steady state (each frame costs a few swaps, not a sort).
            var culledPlayers = _scratchCulledPlayers; // host portal yang dibuang culling (scratch — nol alokasi/frame)
            culledPlayers.Clear();
            var sprites2D = _scratchSprites2D;
            sprites2D.Clear();
            foreach (var o in _objects)
            {
                if (o is not { IsVisible: true, PrimitiveType: EditorPrimitiveType.Player2D or EditorPrimitiveType.Sprite2D }) continue;
                sprites2D.Add(o);
            }
            InsertionSortSprites2D(sprites2D);

            foreach (var obj in sprites2D)
            {
                bool culled = Is2DSpriteCulled(obj, cullCamera, viewportCullViewProj);
                if (culled) EditorObject.Cull2DCulled++;
                EditorObject.Cull2DTotal++;
                if (cullDebug)
                {
                    EditorObject.Debug2DCullObjects.Add(obj);
                    if (culled) EditorObject.Debug2DCullCulled.Add(obj);
                }

                if (culled)
                {
                    // Portal ikut DrawPlayer2D — simpan hostnya agar portal 4-state
                    // TETAP digambar walau player-nya off-screen (portal yang hilang
                    // saat player keluar frame = bug, ini WAJIB di semua mode).
                    if (obj.PrimitiveType == EditorPrimitiveType.Player2D)
                        culledPlayers.Add(obj);
                    continue; // draw skip — debug box digambar pada tahap khusus di bawah
                }

                if (obj.PrimitiveType == EditorPrimitiveType.Player2D)
                    obj.DrawPlayer2D(camera);
                else
                    obj.DrawSprite2D(camera);
            }

            foreach (var p in culledPlayers)
                p.DrawPortalSpritesPublic(camera);

            if (cullDebug) Draw2DCullDebugBoxes(camera, viewProj);
            // ── Effect2D particles (world-space quads, one draw call per texture run).
            // Rendered AFTER all sprite layers so the FX sit in front of sprites/portals.
            Effect2DSystem.Render(camera);

            // ── Projectile + impact sprites (animated sheet quads, fireballs/hits).
            Projectile2DSystem.Render(camera);

            // ── Global weather overlay (ground fog band + ambient tint wash) —
            // on top of every world element so fog/sunset tint the whole scene.
            Effect2DSystem.RenderWeatherOverlay(camera);

            // ── Equipped-item hotbar above each character's head (DISABLED) ──
            // The hotbar (RenderEquippedHotbar) shows equipped items as icons above the
            // player's head. This is currently disabled per user request — equipment art
            // should only render ON the player body via DrawEquipmentSpriteOverlays, not
            // as HUD icons above the player.
            // Re-enable by removing this comment block and uncommenting the code below.
            {
                var br = IDEBridge.Current;
                bool inLiveSession = br is { InGameActive: true } or { IsPreviewMode: true };
                if (!inLiveSession) goto skipHotbar;
                // Hotbar rendering disabled - skip directly
                goto skipHotbar;
                /*Disabled:
                bool batchOpen = false;
                foreach (var obj in _objects)
                {
                    if (obj is not { IsVisible: true, PrimitiveType: EditorPrimitiveType.Player2D }) continue;
                    if (obj.Equipment.Slots.Count == 0) continue;
                    if (!batchOpen) { Effect2DSystem.BeginBatch(camera); batchOpen = true; }
                    InventorySystem.RenderEquippedHotbar(camera, obj.Equipment,
                        obj.Position.X, obj.Position.Y, obj.Player2DHeight, 0f);
                }
                if (batchOpen) Effect2DSystem.EndBatch();
                */
                skipHotbar:;
            }

            // ── World loot drops (magnet vacuum pickups): animated item-icon quads
            // through the same shared batch. No icons → no batch, zero cost.
            if (InventorySystem.Drops.Count > 0)
            {
                Effect2DSystem.BeginBatch(camera);
                InventorySystem.RenderLootDrops(camera);
                Effect2DSystem.EndBatch();
            }

            // ── Weather fog FRONT layer (user: fog paling depan) — translucent cloud
            // atmosphere over the world, drawn after every world sprite/particle so
            // tiles/player/particles sit BEHIND it (still visible through the alpha).
            // Editor gizmos below stay on top of the fog (design tools must stay crisp).
            Effect2DSystem.RenderFogFront(camera);

            // ── Mist veil (reference #2): ONE broad low haze slab, drawn AFTER the fog
            // bands so a thick mist also mutes them. Editor gizmos stay frontmost.
            Effect2DSystem.RenderMistFront(camera);

            // ── Pixel atmosphere FX (god rays / wind streaks / heat haze / lightning /
            // frost): the final overlay pass before the editor gizmos.
            Effect2DSystem.RenderPixelFxFront(camera);

            // ── Editor gizmos for special marker types (drawn after the solid objects so
            // the wireframe lines always render on top; depth test is disabled internally
            // so they show through geometry): real-camera frustum for cameras, a direction
            // ray + spotlight cone for lights, and a horizon + sun icon for skies. ──
            foreach (var obj in _objects)
            {
                if (obj == null || !obj.IsVisible) continue;

                bool marker = obj.PrimitiveType is EditorPrimitiveType.Camera or EditorPrimitiveType.Light
                    or EditorPrimitiveType.Sky or EditorPrimitiveType.Player2D or EditorPrimitiveType.Start2D
                    or EditorPrimitiveType.Sprite2D or EditorPrimitiveType.Effect2D or EditorPrimitiveType.CameraStart2D;
                if (marker)
                {
                    var bounds = GetEditorMarkerCullBounds(obj, cullCamera);
                    bool culled = !EditorObject.IsAABBInFrustum(viewportCullViewProj, bounds);
                    if (captureCullDebug) _viewportCullBoxes.Add((bounds, culled ? 1 : 0));
                    if (culled)
                    {
                        EditorMarkerFrustumObjects++;
                        continue;
                    }
                }

                // Camera/Light/Sky markers are 2D billboard icons (always face the camera)
                // Hidden in preview/in-game mode via showEditorGizmos flag.
                // Player2D capsule + Start2D spawn arrow stay visible in-game too (they
                // are gameplay aids, and Draw2DMarker internally skips when hidden).
                if (obj.PrimitiveType == EditorPrimitiveType.Player2D ||
                    obj.PrimitiveType == EditorPrimitiveType.Start2D ||
                    obj.PrimitiveType == EditorPrimitiveType.Sprite2D ||
                    obj.PrimitiveType == EditorPrimitiveType.Effect2D ||
                    obj.PrimitiveType == EditorPrimitiveType.CameraStart2D)
                {
                    if (showEditorGizmos || !EditorObject.Editor2DAidsHidden)
                        obj.Draw2DMarker(camera);
                }
                else if (showEditorGizmos && (obj.PrimitiveType == EditorPrimitiveType.Camera ||
                    obj.PrimitiveType == EditorPrimitiveType.Light ||
                    obj.PrimitiveType == EditorPrimitiveType.Sky))
                {
                    obj.Draw2DMarker(camera);
                }

                if (showEditorGizmos && obj.PrimitiveType == EditorPrimitiveType.Camera)
                {
                    if (obj.ShowFrustum)
                        obj.DrawCameraFrustum(camera);
                }
                else if (showEditorGizmos && obj.PrimitiveType == EditorPrimitiveType.Light)
                {
                    if (obj.ShowLightGizmo)
                        obj.DrawLightGizmo(camera);
                }
                else if (obj.PrimitiveType == EditorPrimitiveType.Sky)
                {
                    // Sky gizmo is an editor tool — hidden when the scene renders in game
                    // mode (MainMenuScene passes showSkyGizmo: false).
                    if (showEditorGizmos && showSkyGizmo && obj.ShowSkyGizmo)
                        obj.DrawSkyGizmo(camera);
                }

            }

            if (captureViewportCullSnapshot && ViewportCullFreezeEnabled && _captureCullSnapshot)
            {
                _frozenViewportCullBoxes.AddRange(_viewportCullBoxes);
                _frozenViewportCullStatistics = CaptureViewportCullStatistics();
                _captureCullSnapshot = false;
            }
            if (ViewportCullDebug)
            {
                bool depthWasEnabled = GL.IsEnabled(Const.GL_DEPTH_TEST);
                GL.Disable(Const.GL_DEPTH_TEST);
                var boxes = ViewportCullFreezeEnabled ? _frozenViewportCullBoxes : _viewportCullBoxes;
                foreach (var (bounds, state) in boxes)
                {
                    var color = state == 1 ? new Vector3(1f, 0.2f, 0.2f)
                        : state == 2 ? new Vector3(1f, 0.7f, 0.1f) : new Vector3(0.2f, 0.9f, 0.3f);
                    Helpers.DebugDraw.DrawAABBWireframe(bounds, color, camera);
                }
                if (depthWasEnabled) GL.Enable(Const.GL_DEPTH_TEST);
            }

            // ── Stencil-based inverted-hull outline on ALL selected objects (with pulsing effect).
            // Planes are intentionally excluded — their large ground tiles would fill the whole
            // screen with a blinking outline, so a selected plane shows no highlight (the gizmo
            // still marks it as selected). ──
            var outlineSet = selectedObjects is { Count: > 0 }
                ? selectedObjects.Where(o => o.PrimitiveType != EditorPrimitiveType.Plane).ToArray()
                : (SelectedObject != null && SelectedObject.PrimitiveType != EditorPrimitiveType.Plane ? [SelectedObject] : null);
            if (outlineSet != null && outlineSet.Length > 0 && wireframeColor.HasValue)
            {
                // Pulsing effect: oscillates between 0.6 and 1.0 brightness
                float t = Environment.TickCount / 1000f;
                float pulse = 0.6f + 0.4f * MathF.Sin(t * 4f);
                Vector3 outlineCol = wireframeColor.Value * pulse;

                // ── Pass 1: Write stencil mask (set stencil to 1 where objects' depth passes) ──
                GL.Enable(Const.GL_STENCIL_TEST);
                GL.StencilMask(0xFF);
                GL.Clear(Const.GL_STENCIL_BUFFER_BIT);
                GL.StencilFunc(Const.GL_ALWAYS, 1, 0xFF);
                GL.StencilOp(Const.GL_KEEP, Const.GL_KEEP, Const.GL_REPLACE);
                GL.ColorMask(false, false, false, false);

                foreach (var sel in outlineSet)
                    sel.DrawOutlineStencil(camera);

                GL.ColorMask(true, true, true, true);

                // ── Pass 2: Draw expanded back faces where stencil != 1 (yellow outline) ──
                GL.StencilFunc(Const.GL_NOTEQUAL, 1, 0xFF);
                GL.StencilOp(Const.GL_KEEP, Const.GL_KEEP, Const.GL_KEEP);

                foreach (var sel in outlineSet)
                    sel.DrawOutline(camera, outlineCol);

                GL.Disable(Const.GL_STENCIL_TEST);
                // NOTE: DrawOutline() culls the FRONT faces of the hull and restores GL_BACK
                // afterwards; polygon offset is turned off there as well. The scene's
                // RenderProperties.Apply() re-applies the full culling state next frame anyway.
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  GLB reference rendering (gltf shader — mirrors ObjectManager.Draw)
        // ────────────────────────────────────────────────────────────────

        /// <summary>Cached uniform locations for the gltf shader used to draw GLB reference
        /// editor objects (mirrors ObjectManager's setup for game objects).</summary>
        private static class GlbUniforms
        {
            public static bool Ready;
            public static uint Program;
            public static int View, Proj, SunDir, LightColor, FogColor, ViewPos, UseFog;
            public static int ShadowMap0, ShadowMap1, ShadowMap2;
            public static int LightSpace0, LightSpace1, LightSpace2;
            public static int CascadeEnds0, CascadeEnds1, CascadeEnds2, ShadowFilter;
            public static int Model, BaseColor, UseAlbedo, AlbedoMap;
            public static int MetallicFactor, RoughnessFactor, NormalScale, OcclusionStrength, EmissiveFactor;
            public static int HasNormalTexture, HasMetallicRoughnessTexture, HasOcclusionTexture, HasEmissiveTexture;
            public static int NormalMap, MetallicRoughnessMap, OcclusionMap, EmissiveMap, Joints, SkinFlag;
            public static int ShowCSMCascadeColor;

            private static bool _noProgramLogged;

            public static void Ensure()
            {
                if (Ready) return;
                Program = GltfShader.GetShaderProgram();
                if (Program == 0)
                {
                    // Do NOT latch Ready: the first GLB can be drawn before the gltf program
                    // exists, and the old code then kept Program 0 forever — every GLB
                    // reference stayed invisible until the editor was restarted.
                    if (!_noProgramLogged)
                    {
                        _noProgramLogged = true;
                        Console.WriteLine("[GLB] gltf shader program not ready — GLB references will retry rendering.");
                    }
                    return;
                }
                View = GL.GetUniformLocation(Program, "view");
                Proj = GL.GetUniformLocation(Program, "projection");
                SunDir = GL.GetUniformLocation(Program, "sunDir");
                LightColor = GL.GetUniformLocation(Program, "lightColor");
                FogColor = GL.GetUniformLocation(Program, "fogColor");
                ViewPos = GL.GetUniformLocation(Program, "viewPos");
                UseFog = GL.GetUniformLocation(Program, "useFog");
                ShadowMap0 = GL.GetUniformLocation(Program, "shadowMap0");
                ShadowMap1 = GL.GetUniformLocation(Program, "shadowMap1");
                ShadowMap2 = GL.GetUniformLocation(Program, "shadowMap2");
                LightSpace0 = GL.GetUniformLocation(Program, "lightSpaceMatrices[0]");
                LightSpace1 = GL.GetUniformLocation(Program, "lightSpaceMatrices[1]");
                LightSpace2 = GL.GetUniformLocation(Program, "lightSpaceMatrices[2]");
                CascadeEnds0 = GL.GetUniformLocation(Program, "cascadeEnds[0]");
                CascadeEnds1 = GL.GetUniformLocation(Program, "cascadeEnds[1]");
                CascadeEnds2 = GL.GetUniformLocation(Program, "cascadeEnds[2]");
                ShadowFilter = GL.GetUniformLocation(Program, "shadowFilterMode");
                ShowCSMCascadeColor = GL.GetUniformLocation(Program, "showCSMCascadeColor");
                Model = GL.GetUniformLocation(Program, "model");
                BaseColor = GL.GetUniformLocation(Program, "baseColorFactor");
                UseAlbedo = GL.GetUniformLocation(Program, "useAlbedo");
                AlbedoMap = GL.GetUniformLocation(Program, "albedoMap");
                MetallicFactor = GL.GetUniformLocation(Program, "metallicFactor");
                RoughnessFactor = GL.GetUniformLocation(Program, "roughnessFactor");
                NormalScale = GL.GetUniformLocation(Program, "normalScale");
                OcclusionStrength = GL.GetUniformLocation(Program, "occlusionStrength");
                EmissiveFactor = GL.GetUniformLocation(Program, "emissiveFactor");
                HasNormalTexture = GL.GetUniformLocation(Program, "hasNormalTexture");
                HasMetallicRoughnessTexture = GL.GetUniformLocation(Program, "hasMetallicRoughnessTexture");
                HasOcclusionTexture = GL.GetUniformLocation(Program, "hasOcclusionTexture");
                HasEmissiveTexture = GL.GetUniformLocation(Program, "hasEmissiveTexture");
                NormalMap = GL.GetUniformLocation(Program, "normalMap");
                MetallicRoughnessMap = GL.GetUniformLocation(Program, "metallicRoughnessMap");
                OcclusionMap = GL.GetUniformLocation(Program, "occlusionMap");
                EmissiveMap = GL.GetUniformLocation(Program, "emissiveMap");
                Joints = GL.GetUniformLocation(Program, "u_Joints");
                SkinFlag = GltfObject.SkinFlagLocation(Program);
                Ready = true;
            }
        }

        /// <summary>Render all visible GLB reference editor objects with the gltf shader
        /// (PBR materials + CSM shadow reception), mirroring ObjectManager.Draw's uniform
        /// setup. Called once per frame from <see cref="Draw"/> so all GLB refs share a
        /// single shader bind. Restores the main shader afterwards.</summary>
        private ViewportCullStatistics CaptureViewportCullStatistics() => new(
            ViewportDrawnObjects, ViewportFrustumObjects, ViewportOccludedObjects,
            ViewportDrawnTriangles, ViewportFrustumTriangles, ViewportOccludedTriangles,
            GlbDrawnObjects, GlbFrustumObjects, GlbOccludedObjects,
            GlbDrawnTriangles, GlbFrustumTriangles, GlbOccludedTriangles,
            TerrainDrawnTriangles, TerrainFrustumTriangles, TerrainOccludedTriangles,
            TerrainPlanes, TerrainFrustumPlanes, TerrainOccludedPlanes,
            TerrainChunkCount, TerrainChunkTests, TerrainDrawnChunks,
            TerrainFrustumChunks, TerrainOccludedChunks, TerrainOctreeNodesTested,
            TerrainOctreeNodesPruned, EditorMarkerFrustumObjects,
            ViewportOccluderCount, ViewportOcclusionActive);

        private static AABB GetEditorMarkerCullBounds(EditorObject obj, Camera camera)
        {
            if (obj.PrimitiveType is EditorPrimitiveType.Player2D or EditorPrimitiveType.Sprite2D)
            {
                var (min, max) = obj.Get2DSpriteAABB();
                return new AABB(min, max);
            }

            if (obj.PrimitiveType is EditorPrimitiveType.Start2D or EditorPrimitiveType.Effect2D
                or EditorPrimitiveType.CameraStart2D)
            {
                float billboardRadius = 0.45f * MathF.Max(MathF.Abs(obj.Scale.X),
                    MathF.Max(MathF.Abs(obj.Scale.Y), MathF.Abs(obj.Scale.Z)));
                var extent = new Vector3(billboardRadius);
                return new AABB(obj.Position - extent, obj.Position + extent);
            }

            var bounds = obj.WorldAABB;
            float markerRadius = 0.45f * MathF.Max(MathF.Abs(obj.Scale.X),
                MathF.Max(MathF.Abs(obj.Scale.Y), MathF.Abs(obj.Scale.Z)));
            float radius = obj.PrimitiveType switch
            {
                EditorPrimitiveType.Camera => MathF.Max(CameraGizmoRadius(obj, camera), markerRadius),
                EditorPrimitiveType.Light => LightGizmoRadius(obj),
                EditorPrimitiveType.Sky => 3.5f * MathF.Max(MathF.Abs(obj.Scale.X),
                    MathF.Max(MathF.Abs(obj.Scale.Y), MathF.Abs(obj.Scale.Z)))
                    + obj.SkySunDiscRadius,
                _ => 0f,
            };
            if (radius <= 0f) return bounds;
            var pad = new Vector3(radius);
            return new AABB(bounds.Min - pad, bounds.Max + pad);
        }

        private static float CameraGizmoRadius(EditorObject obj, Camera camera)
        {
            float far = MathF.Min(obj.CameraFar, 100f);
            float tanHalfFov = MathF.Tan(Math.Clamp(obj.CameraFov, 1f, 179f) * MathF.PI / 360f);
            float aspect = MathF.Max(0.01f, camera.GetAspect());
            return far * MathF.Sqrt(1f + tanHalfFov * tanHalfFov * (1f + aspect * aspect));
        }

        private static float LightGizmoRadius(EditorObject obj)
        {
            if (obj.LightTypeEnum == LightType.Point)
                return MathF.Max(0.5f, obj.LightPointRadius > 0f ? obj.LightPointRadius : 50f);
            float length = Math.Clamp(4f + obj.LightIntensity * 3f, 4f, 24f);
            if (obj.LightTypeEnum != LightType.Spotlight) return MathF.Max(length, 0.45f * MathF.Max(
                MathF.Abs(obj.Scale.X), MathF.Max(MathF.Abs(obj.Scale.Y), MathF.Abs(obj.Scale.Z))));
            float coneRadius = MathF.Tan(Math.Clamp(obj.LightConeAngle, 1f, 89f) * MathF.PI / 180f) * length;
            float coneExtent = MathF.Sqrt(length * length + coneRadius * coneRadius);
            float markerRadius = 0.45f * MathF.Max(MathF.Abs(obj.Scale.X),
                MathF.Max(MathF.Abs(obj.Scale.Y), MathF.Abs(obj.Scale.Z)));
            return MathF.Max(coneExtent, markerRadius);
        }

        private void DrawGlbReferences(Camera camera, Camera cullCamera, Matrix4x4 cullViewProj,
            Lights light, CSM? csm, bool captureCullDebug, bool recordViewportPick = true,
            bool hzbUsable = false, Matrix4x4 hzbViewProj = default)
        {
            bool any = false;
            for (int i = 0; i < _objects.Count; i++)
            {
                var o = _objects[i];
                if (o.PrimitiveType == EditorPrimitiveType.GlbReference && o.IsVisible) { any = true; break; }
            }
            if (!any) return;

            GlbUniforms.Ensure();
            if (GlbUniforms.Program == 0) return;

            GL.UseProgram(GlbUniforms.Program);
            GL.Enable(Const.GL_DEPTH_TEST);

            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            GL.UniformMatrix4fv(GlbUniforms.View, 1, false, (float*)&view);
            GL.UniformMatrix4fv(GlbUniforms.Proj, 1, false, (float*)&proj);
            GL.Uniform3f(GlbUniforms.SunDir, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
            GL.Uniform3f(GlbUniforms.LightColor, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(GlbUniforms.ViewPos, camera.Position.X, camera.Position.Y, camera.Position.Z);

            // ── Fog (enable, mode, color, density, start/end, height — Config.FogSettings) ──
            Visual.FogUniforms.UploadMain(GlbUniforms.Program, light);

            // ── Local point/spot lights (from editor Light markers) ──
            light.UploadLocalLights(GlbUniforms.Program);

            // Live shadow bias / blend tuning (Shadow Settings panel) — gltf shader values.
            ShadowUniforms.UploadGltf(GlbUniforms.Program);
            if (GlbUniforms.ShowCSMCascadeColor >= 0)
                GL.Uniform1i(GlbUniforms.ShowCSMCascadeColor, Inputs.Keyboard.GetshowCSMCascadeColor() ? 1 : 0);

            if (csm != null)
            {
                GL.Uniform1i(GlbUniforms.ShadowMap0, 6);
                GL.Uniform1i(GlbUniforms.ShadowMap1, 7);
                GL.Uniform1i(GlbUniforms.ShadowMap2, 8);
                fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                    GL.UniformMatrix4fv(GlbUniforms.LightSpace0, 1, false, p0);
                fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                    GL.UniformMatrix4fv(GlbUniforms.LightSpace1, 1, false, p1);
                fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                    GL.UniformMatrix4fv(GlbUniforms.LightSpace2, 1, false, p2);
                GL.Uniform1f(GlbUniforms.CascadeEnds0, csm.CascadeEnds[0]);
                GL.Uniform1f(GlbUniforms.CascadeEnds1, csm.CascadeEnds[1]);
                GL.Uniform1f(GlbUniforms.CascadeEnds2, csm.CascadeEnds[2]);
                GL.Uniform1i(GlbUniforms.ShadowFilter, Inputs.Keyboard.GetIsHardShadow());
            }

            // Sampler units + default PBR values (overridden per-mesh by GltfObject.Draw)
            GL.Uniform1i(GlbUniforms.NormalMap, 3);
            GL.Uniform1i(GlbUniforms.MetallicRoughnessMap, 4);
            GL.Uniform1i(GlbUniforms.OcclusionMap, 5);
            GL.Uniform1i(GlbUniforms.EmissiveMap, 6);
            GL.Uniform1f(GlbUniforms.MetallicFactor, 1.0f);
            GL.Uniform1f(GlbUniforms.RoughnessFactor, 0.3f);
            GL.Uniform1f(GlbUniforms.NormalScale, 1.0f);
            GL.Uniform1f(GlbUniforms.OcclusionStrength, 1.0f);
            GL.Uniform3f(GlbUniforms.EmissiveFactor, 0f, 0f, 0f);
            GL.Uniform1i(GlbUniforms.HasNormalTexture, 0);
            GL.Uniform1i(GlbUniforms.HasMetallicRoughnessTexture, 0);
            GL.Uniform1i(GlbUniforms.HasOcclusionTexture, 0);
            GL.Uniform1i(GlbUniforms.HasEmissiveTexture, 0);

            //  GLB references are skipped by the occluders gathered in the primitive pass
            //  above; the analytic test needs no GL state.
            bool useOcclusion = ViewportOcclusionEnabled && (_viewportOccluders.Count > 0 || hzbUsable);
            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                if (obj.PrimitiveType != EditorPrimitiveType.GlbReference || !obj.IsVisible) continue;
                if (!obj.TryGetGlbCullData(out var bounds, out long triangles)) continue;

                int state = 0;
                // Skinning can move vertices outside bind-pose bounds; a skinned GLB keeps
                // bounds == null and is drawn without culling (guard restored on purpose:
                // an animated character must never disappear). Rigs still occlude others —
                // see AddViewportOccluders.
                if (bounds is { } aabb)
                {
                    if (!EditorObject.IsAABBInFrustum(cullViewProj, aabb)) state = 1;
                    else if (hzbUsable && ViewportHZB.IsOccluded(aabb, hzbViewProj)) state = 2;
                    else if (useOcclusion && obj.IsOccludee)
                        foreach (var occluder in _viewportOccluders)
                            if (ViewportOcclusion.IsOccludedPrepared(aabb, occluder, cullViewProj, obj.OcclusionTag))
                            {
                                state = 2;
                                break;
                            }
                    if (captureCullDebug) _viewportCullBoxes.Add((aabb, state));
                }
                if (recordViewportPick)
                {
                    obj.ViewportCullState = state;
                    obj.ViewportCullStateFrame = Glfw.FrameId;
                }
                if (state == 1) { GlbFrustumObjects++; GlbFrustumTriangles += triangles; continue; }
                if (state == 2) { GlbOccludedObjects++; GlbOccludedTriangles += triangles; continue; }
                GlbDrawnObjects++;
                GlbDrawnTriangles += triangles;

                // A GLB mesh with an emissive texture binds it to unit 6 (GltfObject.Draw,
                // same as the game) which would clobber shadowMap0 — re-bind the cascade maps
                // before every object so cascade-0 shadows stay correct for all of them.
                if (csm != null)
                {
                    GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
                    GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[0]);
                    GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
                    GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[1]);
                    GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
                    GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[2]);
                    GL.ActiveTexture(Const.GL_TEXTURE0);
                }

                // Skinning state per model (mirrors ObjectManager): an unskinned GLB turns
                // u_UseSkinning off instead of inheriting another model's joints.
                obj.UploadGlbSkinning(GlbUniforms.SkinFlag, GlbUniforms.Joints);

                obj.DrawGlb(
                    GlbUniforms.Model, GlbUniforms.BaseColor, GlbUniforms.UseAlbedo, GlbUniforms.AlbedoMap,
                    GlbUniforms.MetallicFactor, GlbUniforms.RoughnessFactor, GlbUniforms.NormalScale,
                    GlbUniforms.OcclusionStrength, GlbUniforms.EmissiveFactor,
                    GlbUniforms.HasNormalTexture, GlbUniforms.HasMetallicRoughnessTexture,
                    GlbUniforms.HasOcclusionTexture, GlbUniforms.HasEmissiveTexture);
            }

            // Restore the main shader for subsequent editor content (gizmos/outlines).
            GL.UseProgram(_shaderProgram);
        }

        /// <summary>Render the full CSM shadow pass for all editor objects (all cascades).
        /// Caller must restore its framebuffer afterwards and bind the cascade shadow maps
        /// to texture units 6/7/8 before calling <see cref="Draw"/> with the same CSM.
        /// Shared by GameScene (via RenderShadow per cascade), MainMenuScene and the
        /// SceneManager bare-editor viewport so shadows match the sun in every view.</summary>
        public void RenderShadowPass(Camera camera, Lights light, CSM csm)
        {
            // Load/refresh GLB models BEFORE any GL pass runs. Loading used to be triggered
            // lazily by the first cull/draw/shadow call, i.e. in the middle of a pass: it
            // created VAOs/VBOs/textures (binding texture units the pass was using) and a
            // model that parsed while the exporter still held the file stayed invisible until
            // something else forced a reload. One explicit pass makes it deterministic.
            PrepareGlbReferences();
            csm.UpdateMatrices(camera, light.ShadowDirStable);

            for (int i = 0; i < CSM.NumCascades; i++)
            {
                csm.BindFramebuffer(i);
                RenderShadow(camera, csm, i);
            }
        }

        /// <summary>Render shadow for all editor objects (called from shadow pass).</summary>
        public void RenderShadow(Camera camera, CSM csm, int cascadeIndex)
        {
            if (_objects.Count == 0) return;

            GL.UseProgram(_shadowShader);

            // Live normal-bias tuning (Shadow Settings panel) — static shadow shader.
            ShadowUniforms.UploadNormalBias(_shadowShader);

            var planes = CSM.BuildPlanesFromCorners(csm.OrthoCorners[cascadeIndex]);

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                if (!obj.CastShadow || !obj.IsVisible) continue;

                // Simple frustum culling for shadow passes. GLB references use a
                // radius derived from their actual mesh AABB; primitives use a fixed
                // conservative radius.
                if (planes != null)
                {
                    float boundRadius = obj.PrimitiveType == EditorPrimitiveType.GlbReference
                        ? obj.GetGlbBoundRadius()
                        : 3f;
                    bool outside = false;
                    foreach (var plane in planes)
                    {
                        float d = Vector3.Dot(plane.Normal, obj.Position) + plane.D;
                        if (d < -boundRadius) { outside = true; break; }
                    }
                    if (outside) continue;
                }

                // Camera/Light/Sky markers don't cast shadows
                if (obj.PrimitiveType == EditorPrimitiveType.Camera
                    || obj.PrimitiveType == EditorPrimitiveType.Light
                    || obj.PrimitiveType == EditorPrimitiveType.Sky) continue;

                obj.RenderShadow(_shadowModelLoc, camera, csm, cascadeIndex);
            }
        }

        /// <summary>
        /// Raycast against all editor objects to find the closest hit.
        /// Returns the hit object, hit point, and hit distance.
        /// </summary>
        /// <summary>True when the object was actually DRAWN in the current viewport frame.
        /// A frame id mismatch (no draw pass yet this frame: hidden tab, scene still loading)
        /// counts as pickable so selecting never breaks; likewise an object with no recorded
        /// state. Culled objects (frustum or occlusion) are not pickable — a click through the
        /// editor camera must never select something the viewport is not showing.</summary>
        public static bool IsViewportPickable(EditorObject obj) =>
            obj.ViewportCullState == 0 || obj.ViewportCullStateFrame != Glfw.FrameId;

        public EditorObject? Raycast(Vector3 rayOrigin, Vector3 rayDir, out float hitDist, out Vector3 hitPoint)
        {
            hitDist = float.MaxValue;
            hitPoint = Vector3.Zero;
            EditorObject? closest = null;

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                if (!obj.IsVisible) continue;
                if (!IsViewportPickable(obj)) continue;

                if (obj.PrimitiveType == EditorPrimitiveType.GlbReference)
                {
                    // Use approximate sphere AABB for glb references
                    float radius = 1.5f * obj.Scale.Length() * 0.5f;
                    if (RaySphereIntersect(rayOrigin, rayDir, obj.Position, radius, out float t))
                    {
                        if (t > 0 && t < hitDist)
                        {
                            hitDist = t;
                            hitPoint = rayOrigin + rayDir * t;
                            closest = obj;
                        }
                    }
                }
                else
                {
                    // Use AABB intersection for primitives
                    var aabb = obj.GetWorldAABB();
                    if (AABB.RayIntersectsAABB(rayOrigin, rayDir, aabb, out float tMin, out float _))
                    {
                        if (tMin > 0 && tMin < hitDist)
                        {
                            hitDist = tMin;
                            hitPoint = rayOrigin + rayDir * tMin;
                            closest = obj;
                        }
                    }
                }
            }

            return closest;
        }

        private static bool RaySphereIntersect(Vector3 origin, Vector3 dir, Vector3 center, float radius, out float t)
        {
            t = 0;
            Vector3 oc = origin - center;
            float a = Vector3.Dot(dir, dir);
            float b = 2.0f * Vector3.Dot(oc, dir);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float discriminant = b * b - 4 * a * c;
            if (discriminant < 0) return false;

            float sqrtD = MathF.Sqrt(discriminant);
            float t1 = (-b - sqrtD) / (2.0f * a);
            float t2 = (-b + sqrtD) / (2.0f * a);

            if (t1 > 0) { t = t1; return true; }
            if (t2 > 0) { t = t2; return true; }
            return false;
        }

        public void Dispose()
        {
            Clear();
        }
    }
}

