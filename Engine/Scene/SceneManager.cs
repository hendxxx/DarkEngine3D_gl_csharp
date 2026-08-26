using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>
    /// Manages the scene lifecycle: switching, main game loop, and rendering.
    /// Owns the top-level while-loop that drives the entire application.
    /// </summary>
    public unsafe class SceneManager
    {
        private IScene? _currentScene;
        private IScene? _nextScene;
        private bool _running;
        private bool _altEnterWasDown = false;

        // ── IDE integration ──
        private IDE.IDE? _ide;

        // ── Shared scene FBO (for scenes without their own, e.g. MainMenuScene) ──
        // MSAA: the scene renders into _sharedFBO (multisampled); ResolveSharedFBO()
        // blits it into the single-sample _sharedColorTex the Viewport panel samples.
        private uint _sharedFBO = 0;
        private uint _sharedColorTex = 0;
        private uint _sharedDepthRBO = 0;
        private uint _sharedMsaaColorRBO = 0;
        private uint _sharedResolveFBO = 0;
        private int _sharedMsaaSamples = 4;
        private bool _sharedFBOCreated = false;

        // ── AAA post-FX chain applied to the shared viewport texture (same chain as
        // GameScene) so the IDE Post FX panel sliders are live in the editor viewport ──
        private PostFxProcessor? _postFx;

        // ── Default camera + lights for rendering editor objects when no scene is active ──
        private Camera? _editorCamera;
        private Lights? _editorLights;
        // ── CSM shadow maps for the bare-editor viewport (mirrors GameScene's shadow pass
        // so editor objects cast shadows matching the sky/light sun in this view too) ──
        private CSM? _editorCsm;

        // ── Editor skybox (rendered when a Sky editor object exists in the active scene) ──
        private Skybox? _editorSkybox;
        private Texture[]? _editorSkyTextures;
        private bool _editorSkyboxInitFailed = false;

        // ── Editor grid (ground plane + axis helpers for viewport when no scene is active) ──
        private uint _editorGridVAO = 0;
        private uint _editorGridVBO = 0;
        private int _editorGridVertexCount = 0;
        private uint _editorAxisVAO = 0;
        private uint _editorAxisVBO = 0;
        private bool _editorGridCreated = false;

        // ── VSync state tracking (for per-scene VSync switching) ──
        private bool _currentVSync = true;

        private void EnsureSharedFBO()
        {
            // Follow the unified quality preset: when the requested MSAA sample count
            // changes (quality preset switched), destroy the FBO so it is rebuilt with
            // the new sample count on the next EnsureSharedFBO call.
            if (_sharedFBOCreated && _sharedMsaaSamples != Math.Max(1, Config.QualitySettings.MsaaSamples))
                DestroySharedFBO();

            if (_sharedFBOCreated) return;

            // Requested MSAA from the quality preset, clamped to the driver's max.
            _sharedMsaaSamples = Math.Max(1, Config.QualitySettings.MsaaSamples);
            int maxSamples = 0;
            GL.GetIntegerv(Const.GL_MAX_SAMPLES, &maxSamples);
            if (maxSamples > 0) _sharedMsaaSamples = Math.Min(_sharedMsaaSamples, maxSamples);

            uint fbo = 0, color = 0, rbo = 0, msaaColorRbo = 0, resolveFbo = 0;

            GL.GenFramebuffers(1, &fbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);

            // Multisampled color + depth-stencil renderbuffers.
            GL.GenRenderbuffers(1, &msaaColorRbo);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, msaaColorRbo);
            bool msaaOk = GL.RenderbufferStorageMultisample(Const.GL_RENDERBUFFER, _sharedMsaaSamples, Const.GL_RGBA8,
                                                            Glfw.WindowWidth, Glfw.WindowHeight);
            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                       Const.GL_RENDERBUFFER, msaaColorRbo);

            GL.GenRenderbuffers(1, &rbo);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, rbo);
            GL.RenderbufferStorageMultisample(Const.GL_RENDERBUFFER, _sharedMsaaSamples, Const.GL_DEPTH24_STENCIL8,
                                              Glfw.WindowWidth, Glfw.WindowHeight);
            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_STENCIL_ATTACHMENT,
                                       Const.GL_RENDERBUFFER, rbo);

            uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"[SceneManager] Shared MSAA FBO incomplete: 0x{status:X}");

            bool msaaUsable = msaaOk && _sharedMsaaSamples > 1 && status == Const.GL_FRAMEBUFFER_COMPLETE;

            // Single-sample resolve target — the texture the Viewport panel samples.
            GL.GenTextures(1, &color);
            GL.BindTexture(Const.GL_TEXTURE_2D, color);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8,
                          Glfw.WindowWidth, Glfw.WindowHeight, 0,
                          Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);

            GL.GenFramebuffers(1, &resolveFbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, resolveFbo);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, color, 0);

            if (!msaaUsable)
            {
                // Fall back to single-sample: reuse the depth renderbuffer, render straight
                // into the resolve FBO. Depth renderbuffer storage becomes plain.
                GL.BindRenderbuffer(Const.GL_RENDERBUFFER, rbo);
                GL.RenderbufferStorage(Const.GL_RENDERBUFFER, Const.GL_DEPTH24_STENCIL8,
                                       Glfw.WindowWidth, Glfw.WindowHeight);
                GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_STENCIL_ATTACHMENT,
                                           Const.GL_RENDERBUFFER, rbo);
            }

            uint resolveStatus = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (resolveStatus != Const.GL_FRAMEBUFFER_COMPLETE)
            {
                Console.WriteLine($"[SceneManager] Shared resolve FBO incomplete: 0x{resolveStatus:X} — cleaning up");
                GL.DeleteFramebuffers(1, &fbo);
                GL.DeleteTextures(1, &color);
                GL.DeleteRenderbuffers(1, &rbo);
                GL.DeleteRenderbuffers(1, &msaaColorRbo);
                GL.DeleteFramebuffers(1, &resolveFbo);
                return;
            }

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

            // When MSAA is off, render directly to the resolve FBO (scene writes to the texture)
            // so there's no unnecessary blit. When MSAA is on, scene renders to the MSAA FBO
            // and blits to the resolve FBO. In both cases _sharedResolveFBO always has _sharedColorTex.
            _sharedFBO = msaaUsable ? fbo : resolveFbo;
            _sharedColorTex = color;
            _sharedDepthRBO = rbo;
            _sharedMsaaColorRBO = msaaColorRbo;
            _sharedResolveFBO = resolveFbo;
            _sharedFBOCreated = true;

            Console.WriteLine($"[SceneManager] Shared FBO created ({Glfw.WindowWidth}x{Glfw.WindowHeight}, MSAA {(_sharedMsaaSamples > 1 && msaaUsable ? _sharedMsaaSamples + "x" : "off")})");
        }

        /// <summary>Resolve the shared MSAA FBO into the single-sample _sharedColorTex the
        /// Viewport panel samples, then apply the AAA post-FX chain (bloom + tonemap +
        /// gamma) so the IDE Post FX panel is live in the editor viewport too. Call after
        /// all scene rendering for the frame.</summary>
        private void ResolveSharedFBO()
        {
            if (!_sharedFBOCreated || _sharedResolveFBO == 0) return;

            // When MSAA is active, resolve the multisampled FBO into the texture.
            if (_sharedFBO != _sharedResolveFBO)
            {
                GL.BindFramebuffer(Const.GL_READ_FRAMEBUFFER, _sharedFBO);
                GL.BindFramebuffer(Const.GL_DRAW_FRAMEBUFFER, _sharedResolveFBO);
                GL.BlitFramebuffer(0, 0, Glfw.WindowWidth, Glfw.WindowHeight,
                                   0, 0, Glfw.WindowWidth, Glfw.WindowHeight,
                                   Const.GL_COLOR_BUFFER_BIT, Const.GL_NEAREST);
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            }

            // Grade the viewport texture with the AAA post-FX chain (bloom + tonemap + gamma).
            // PostFxProcessor has its own output texture to avoid feedback loops.
            Console.WriteLine($"[SceneManager] ResolveSharedFBO: FBO={_sharedFBO} resolve={_sharedResolveFBO} tex={_sharedColorTex} postFx={Config.PostFxSettings.Enabled}");
            if (Config.PostFxSettings.Enabled)
            {
                _postFx ??= new PostFxProcessor(Glfw.WindowWidth, Glfw.WindowHeight);
                _postFx.Run(_sharedColorTex, _sharedResolveFBO, Glfw.WindowWidth, Glfw.WindowHeight);
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            }
        }

        /// <summary>Attach the IDE to this scene manager. Must be called before Run().</summary>
        public void AttachIde(IDE.IDE ide)
        {
            _ide = ide;
            // Give IDE panels access to the SceneManager for scene switching
            ide.Bridge.SceneManager = this;
            // Recreate shared FBO when window is resized
            Glfw.OnWindowResized += OnSharedFboResized;
        }

        private void OnSharedFboResized(int width, int height)
        {
            DestroySharedFBO();
        }

        /// <summary>The currently active scene.</summary>
        public IScene? CurrentScene => _currentScene;

        /// <summary>Expose the IDE bridge for game scenes to populate with per-frame data.</summary>
        public IDEBridge? Bridge => _ide?.Bridge;

        /// <summary>Whether the IDE is currently active (F2 toggled on).</summary>
        public bool IsIdeActive => _ide?.IsActive ?? false;

        /// <summary>Shared FBO handles (for scenes that need to render into the viewport during IDE mode).</summary>
        public uint SharedFBO => _sharedFBO;
        public uint SharedColorTex => _sharedColorTex;

        /// <summary>Ensure the shared FBO exists. Safe to call multiple times.</summary>
        public void EnsureSharedFBOExists() => EnsureSharedFBO();

        /// <summary>
        /// Start the main loop with the given initial scene (can be null for UI Editor mode).
        /// The initial scene's Enter() is called first (may contain synchronous loading),
        /// then the render loop runs until the window closes or Stop() is called.
        /// </summary>
        public void Run(IScene? startScene = null)
        {
            _running = true;
            if (startScene != null)
                SwitchToScene(startScene, true);

            nint window = Glfw.GetWindow();

            while (_running && Glfw.GetWindowShouldClose(window) == 0)
            {
                float dt = Glfw.GetDeltaTime();

                // ── Rolling FPS counter — driven from the CENTRAL loop so the FPS
                // readout stays live in every scene (GameScene, MainMenu, Loading)
                // and in bare editor mode. Previously only GameScene called
                // Glfw.ShowFPS/UpdateFPS, so FPS froze at 0 everywhere else.
                Glfw.UpdateFPS(dt);

                // ═══════════════════════════════════════════════════════
                // FRAME-START VALIDATION: detect OpenGL state corruption
                // ═══════════════════════════════════════════════════════
                OpenGL.CheckFrontFaceState();

                // ═══════════════════════════════════════════════════════
                // PHASE 1: UPDATE (scene logic runs)
                // ═══════════════════════════════════════════════════════
                // 🛠️ FIX #1: Update runs BEFORE scene switch so that
                // scene.Exit()/Dispose() do NOT leave a stale _currentScene
                // that could be accessed by Render() below.
                // The deferred scene switch (_nextScene) is applied AFTER render.
                _currentScene?.Update(dt);

                // ── If no scene is active, provide free-fly camera (WASD + mouse look) ──
                // This lets the user navigate the viewport to inspect editor objects from any angle,
                // matching the same behavior as MainMenuScene when IDE is in edit mode.
                if (_currentScene == null && _ide != null && _ide.IsActive)
                {
                    var bridge = _ide.Bridge;
                    if (bridge != null && !bridge.InGameActive && bridge.IsViewportFocused)
                    {
                        // Ensure editor camera exists
                        if (_editorCamera == null)
                        {
                            float aspect = (float)Glfw.WindowWidth / Math.Max(1, Glfw.WindowHeight);
                            _editorCamera = new Camera(0f, 10f, 15f, 180f, -33.7f, aspect, 60f, 0.1f, 500f);
                            ApplyPendingEditorCamera(_editorCamera, bridge);
                        }
                        if (_editorLights == null)
                        {
                            _editorLights = new Lights(
                                new Vector3(-0.5f, 0.8f, -0.3f),
                                new Vector3(0.9f, 0.9f, 0.85f),
                                _editorCamera.Position);
                        }

                        // Mouse.Update must be called to refresh delta values
                        // before SetCameraFlyMode reads them. Without this, stale
                        // Mouse.DeltaX/Y from window creation/ImGui would rotate
                        // the camera away from its initial orientation each frame.
                        Mouse.Update(window, _editorCamera);
                        _editorCamera.SetCameraFlyMode(window, dt, true);
                    }
                }

                // ── IDE: F2 toggle ── [DISABLED — replaced by Viewport preview mode]
                // if (_ide != null)
                // {
                //     bool f2Down = Keyboard.IsKeyDown(window, Const.GLFW_KEY_F2);
                //     if (f2Down && !_f2WasDown)
                //     {
                //         _ide.IsActive = !_ide.IsActive;
                //         Console.WriteLine($"[SceneManager] Toggle IDE: IsActive={_ide.IsActive}, IsHealthy={_ide.IsHealthy}");
                //         if (_ide.IsActive)
                //         {
                //             Mouse.ShowMouse(true);
                //             Mouse.ResetState();
                //         }
                //         else
                //         {
                //             _ide.Bridge.InGameActive = true;
                //             UpdateInGameCursor();
                //         }
                //     }
                //     _f2WasDown = f2Down;

                //     if (_ide.IsActive)
                //     {
                //         _ide.Update(dt);
                //     }
                // }

                // IDE always active — keep update running
                if (_ide != null)
                    _ide.Update(dt);

                // ── Keep the IDE FPS/FrameMs readouts fresh EVERY frame regardless of
                // which scene is active (SceneViewPanel + in-game overlay read these).
                if (_ide != null)
                {
                    var bridgeStats = _ide.Bridge;
                    if (bridgeStats != null)
                    {
                        bridgeStats.Fps = Glfw.GetLastFPS();
                        bridgeStats.FrameMs = dt * 1000f;
                    }
                }

                // ── F9: toggle manual input lock [DISABLED]
                    // bool f9Down = Keyboard.IsKeyDown(window, Const.GLFW_KEY_F9);
                    // if (f9Down && !_f9WasDown && _ide.IsActive)
                    // {
                    //     _ide.Bridge.InGameActive = !_ide.Bridge.InGameActive;
                    //     Console.WriteLine($"[SceneManager] Toggle InGameActive: {_ide.Bridge.InGameActive}");
                    //     var settings = Config.SettingsSave.Load();
                    //     settings.InGameActive = _ide.Bridge.InGameActive;
                    //     Config.SettingsSave.Save(settings);
                    // }
                    // _f9WasDown = f9Down;

                // ── Alt+Enter: toggle fullscreen globally ──
                bool altHeld = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT_ALT) ||
                               Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT_ALT);
                bool enterHeld = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER);
                bool altEnterNow = altHeld && enterHeld;
                if (altEnterNow && !_altEnterWasDown)
                {
                    Glfw.ToggleFullscreen();
                }
                _altEnterWasDown = altEnterNow;

                // ═══════════════════════════════════════════════════════
                // PHASE 2: RENDER
                // ═══════════════════════════════════════════════════════
                // When IDE is active, render scene to shared FBO (fallback for scenes without their own FBO)
                // so the Viewport panel can display it. GameScene overrides this with its own SceneFBO.
                if (_ide != null && _ide.IsActive)
                {
                    EnsureSharedFBO();
                    if (_sharedFBOCreated)
                    {
                        GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _sharedFBO);
                        GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);

                        // Use the active editor scene's BackgroundColor for the viewport clear color
                        Vector3 bgColor = GetEditorSceneBackgroundColor();
                        GL.ClearColor(bgColor.X, bgColor.Y, bgColor.Z, 1f);
                        GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
                    }

                    // Reset bridge texture ID before render so the post-render code
                    // always assigns the correct frame's texture (handles FBO recreation
                    // and scene switches where the new scene uses the shared FBO).
                    var bridge = _ide.Bridge;
                    if (bridge != null)
                        bridge.SceneTextureID = 0;
                }

                // ── Apply per-scene render properties before rendering ──
                if (_currentScene != null)
                {
                    OpenGL.ApplySceneProperties(_currentScene.RenderProperties, ref _currentVSync);
                }

                _currentScene?.Render();

                // ── Render gizmo when a scene is active and an editor object is selected ──
                if (_currentScene != null && _ide != null && _ide.IsActive)
                {
                    var br = _ide.Bridge;
                    if (br?.SelectedEditorObject != null && br.EditorGizmo != null)
                    {
                        var gizmo = br.EditorGizmo;
                        // Sky markers can't be rotated/scaled — lock the gizmo to Translate
                        gizmo.AllowRotate = !br.SelectionHasSky;
                        gizmo.AllowScale = !br.SelectionHasSky;
                        gizmo.Mode = (TransformGizmo.GizmoMode)br.GizmoMode;
                        var sceneCam = br.Camera;
                        if (sceneCam != null)
                        {
                            int vpW = br.SceneTextureWidth > 0 ? br.SceneTextureWidth : Glfw.WindowWidth;
                            int vpH = br.SceneTextureHeight > 0 ? br.SceneTextureHeight : Glfw.WindowHeight;

                            // ── Render ONE gizmo at the selection center (group average for
                            // multi-select) — a single gizmo drives the whole selection.
                            // While a multi scale/rotate drag is active, stick to the FROZEN
                            // pivot captured at drag start (TransformGizmo.GroupCenter) so the
                            // visible gizmo matches the pivot the group actually orbits around.
                            Vector3 gizmoCenter = gizmo.GroupCenter ??
                                (br.GetEditorGizmoCenter() ?? Vector3.Zero);
                            if (br.SelectedEditorObjects.Count > 0)
                            {
                                gizmo.Render(sceneCam, gizmoCenter, vpW, vpH);
                            }

                            // Draw crosshair indicators on every SELECTED object that has a
                            // per-object gizmo pivot override (so overrides stay visible).
                            foreach (var sel in br.SelectedEditorObjects)
                            {
                                if (sel == null || sel.GizmoPivotOverride == null) continue;
                                GL.Disable(Const.GL_DEPTH_TEST);
                                TerrainChunk.DrawGizmoPivotCrosshair(sel.GizmoPivotOverride.Value, sceneCam);
                                GL.Enable(Const.GL_DEPTH_TEST);
                            }

                            // Draw pivot dot indicators for ALL NON-selected objects that have pivot override
                            var editorMgr = br.EditorObjectManager;
                            if (editorMgr != null)
                            {
                                foreach (var obj in editorMgr.Objects)
                                {
                                    if (br.SelectedEditorObjects.Contains(obj)) continue; // already has full gizmo/crosshair
                                    if (obj.GizmoPivotOverride != null)
                                    {
                                        GL.Disable(Const.GL_DEPTH_TEST);
                                        TerrainChunk.DrawPivotDot(obj.GizmoPivotOverride.Value, sceneCam);
                                        GL.Enable(Const.GL_DEPTH_TEST);
                                    }
                                }
                            }
                        }
                    }

                    // Editor object mesh wireframe is now drawn inside EditorObjectManager.Draw()
                    // (replaces the old AABB bounding-box wireframe approach)
                }

                // ── If no scene is active, render editor grid + 3D objects into the SharedFBO ──
                // This ensures the viewport shows something even when the user hasn't loaded a scene yet.
                // Previously a spinning triangle was drawn via ImGui in ViewportPanel; now we use the
                // scene-based rendering so objects appear consistently.
                if (_currentScene == null && _ide != null && _ide.IsActive && _sharedFBOCreated)
                {
                    var bridge = _ide.Bridge;

                    // Lazy-init editor camera with a good default view
                    if (_editorCamera == null)
                    {
                        float aspect = (float)Glfw.WindowWidth / Math.Max(1, Glfw.WindowHeight);
                        _editorCamera = new Camera(0f, 10f, 15f, 180f, -33.7f, aspect, 60f, 0.1f, 500f);
                        ApplyPendingEditorCamera(_editorCamera, bridge);
                    }
                    if (_editorLights == null)
                    {
                        _editorLights = new Lights(
                            new Vector3(-0.5f, 0.8f, -0.3f),
                            new Vector3(0.9f, 0.9f, 0.85f),
                            _editorCamera.Position);
                    }

                    _editorCamera.UpdateAspectRatio(Glfw.WindowWidth, Glfw.WindowHeight);

                    GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _sharedFBO);
                    GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);

                    // ── Apply the active editor scene's full render properties (viewport only) ──
                    // This includes background color, face culling (None/Back/Front/Front&Back),
                    // winding order (CCW/CW), wireframe mode, depth test and blending, so the
                    // Inspector's render-property changes take effect in real-time.
                    var editorProps = GetEditorSceneRenderProperties();
                    if (editorProps != null)
                    {
                        editorProps.Apply();
                    }
                    else
                    {
                        // Fallback: ensure a sane default if no editor scene is configured.
                        GL.PolygonMode(Const.GL_FRONT_AND_BACK, Const.GL_FILL);
                        GL.Enable(Const.GL_CULL_FACE);
                        GL.CullFace(Const.GL_BACK);
                        GL.FrontFace(Const.GL_CCW);
                    }

                    // ── Render editor grid (ground plane + axis helpers) ──
                    GL.Clear(Const.GL_DEPTH_BUFFER_BIT);

                    // ── Sky object: render the procedural skybox in the editor viewport ──
                    // Also apply a Light object's direction/color to the editor lights so the
                    // user can see their lighting setup live.
                    if (bridge?.EditorObjectManager != null)
                    {
                        EditorObject? skyObj = null;
                        foreach (var obj in bridge.EditorObjectManager.Objects)
                            if (skyObj == null && obj.PrimitiveType == EditorPrimitiveType.Sky) skyObj = obj;
                        // Sky drives a DIRECT light — prefer it over other light types.
                        EditorObject? lightObj = EditorObject.PickSunLight(bridge.EditorObjectManager.Objects);

                        // ── Light + Sky override: apply the first Light/Sky marker settings to
                        //    the editor lights (shared helper — same code drives in-game mode).
                        //    Ensure the editor skybox exists FIRST so the helper can apply the
                        //    cloud-coverage WeatherOverride on the very first sky frame too.
                        if (skyObj != null)
                            EnsureEditorSkybox();
                        if (_editorLights != null)
                        {
                            EditorObject.ApplyEnvironmentMarkers(lightObj, skyObj, _editorLights, _editorSkybox, dt);

                            // ── Collect Point/Spot Light markers as local lights so they
                            //    illuminate every object in the viewport ──
                            _editorLights.CollectLocalLights(bridge.EditorObjectManager.Objects);

                            // Ensure lighting uniforms are computed for the editor lights
                            _editorLights.Update(dt, _editorCamera.Position);
                        }

                        // ── Skybox render (behind the grid, drawn first). The skybox was
                        //    already ensured and the cloud-coverage WeatherOverride applied by
                        //    ApplyEnvironmentMarkers above. ──
                        if (skyObj != null)
                        {
                            if (_editorSkybox != null && _editorSkyTextures != null && _editorLights != null)
                            {
                                _editorSkybox.Draw(_editorCamera, _editorLights, dt, _editorSkyTextures, null);
                            }
                        }
                    }

                    // Debug grid toggle (shared with the GameScene DrawDebugGrid path) — the
                    // camera-centered editor grid hides when the viewport toolbar toggle is off.
                    // Also hidden in preview mode.
                    if ((bridge?.ShowDebugGrid ?? true) && !(bridge?.IsPreviewMode ?? false))
                        RenderEditorGrid();

                    // ── CSM shadow pass + render editor 3D objects (only when the editor
                    // actually has objects — EditorObjectManager may be null when no editor
                    // scene with an object manager is loaded yet) ──
                    if (bridge?.EditorObjectManager is { Count: > 0 } editorObjMgr)
                    {
                        // The CSM self-rebuilds when the Shadow Settings panel changes
                        // cascade sizes/splits (CSM.EnsureCurrent runs inside UpdateMatrices,
                        // called from RenderShadowPass below).
                        _editorCsm ??= new CSM(Config.ShadowSettings.CascadeSizes[0]);
                        _editorLights.LocalShadowsEnabled = bridge.ShowShadows;
                        if (bridge.ShowShadows)
                        {
                            editorObjMgr.RenderShadowPass(_editorCamera, _editorLights, _editorCsm);

                            // Local light (Point/Spot) shadow pass — each light casts its
                            // own shadow map (editor objects are the only casters here).
                            _editorLights.LocalShadow ??= new LocalLightShadow();
                            _editorLights.LocalShadow.RenderShadowPass(
                                _editorCamera, _editorLights.LocalLights,
                                Shader.GetShadowShaderProgram(), Shader.GetShadowSkinnedShaderProgram(),
                                Shader.GetShadowStaticAlphaShaderProgram(),
                                (CSM csm, int ci) => editorObjMgr.RenderShadow(_editorCamera, csm, ci));
                        }
                        else
                        {
                            _editorCsm.ClearShadowMaps();
                        }

                        // Restore the shared FBO + scene render state after the depth-only shadow pass
                        GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _sharedFBO);
                        GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
                        if (editorProps != null)
                            editorProps.Apply();
                        GL.Enable(Const.GL_DEPTH_TEST);

                        // Bind the cascade shadow maps (units 6..8 — matches the main shader)
                        GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
                        GL.BindTexture(Const.GL_TEXTURE_2D, _editorCsm.ShadowTextures[0]);
                        GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
                        GL.BindTexture(Const.GL_TEXTURE_2D, _editorCsm.ShadowTextures[1]);
                        GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
                        GL.BindTexture(Const.GL_TEXTURE_2D, _editorCsm.ShadowTextures[2]);

                        // Pass selection highlight color so selected objects get a mesh wireframe outline
                        Vector3? wireCol = bridge is { SelectedEditorObjects.Count: > 0 }
                            ? bridge.SelectionHighlights.EditorObject : null;
                        bool showGizmos = !(bridge?.IsPreviewMode ?? false);
                        editorObjMgr.Draw(_editorCamera, _editorLights, _editorCsm, wireCol, bridge.SelectedEditorObjects, showEditorGizmos: showGizmos);
                    }

                    // ── Render ONE gizmo at the selection center (group average for
                    // multi-select) — a single gizmo drives the whole selection.
                    if (bridge?.SelectedEditorObject != null && bridge.EditorGizmo != null && _editorCamera != null)
                    {
                        var gizmo = bridge.EditorGizmo;
                        // Sky markers can't be rotated/scaled — lock the gizmo to Translate
                        gizmo.AllowRotate = !bridge.SelectionHasSky;
                        gizmo.AllowScale = !bridge.SelectionHasSky;
                        gizmo.Mode = (TransformGizmo.GizmoMode)bridge.GizmoMode;
                        int vpW = bridge.SceneTextureWidth > 0 ? bridge.SceneTextureWidth : Glfw.WindowWidth;
                        int vpH = bridge.SceneTextureHeight > 0 ? bridge.SceneTextureHeight : Glfw.WindowHeight;

                        // While a multi scale/rotate drag is active, stick to the FROZEN
                        // pivot captured at drag start (TransformGizmo.GroupCenter) so the
                        // visible gizmo matches the pivot the group actually orbits around.
                        Vector3 gizmoCenter = gizmo.GroupCenter ??
                            (bridge.GetEditorGizmoCenter() ?? Vector3.Zero);
                        if (bridge.SelectedEditorObjects.Count > 0)
                        {
                            gizmo.Render(_editorCamera, gizmoCenter, vpW, vpH);
                        }

                        // Draw crosshair indicators on every SELECTED object that has a
                        // per-object gizmo pivot override (so overrides stay visible).
                        foreach (var sel in bridge.SelectedEditorObjects)
                        {
                            if (sel == null || sel.GizmoPivotOverride == null) continue;
                            GL.Disable(Const.GL_DEPTH_TEST);
                            TerrainChunk.DrawGizmoPivotCrosshair(sel.GizmoPivotOverride.Value, _editorCamera);
                            GL.Enable(Const.GL_DEPTH_TEST);
                        }

                        // Draw pivot dot indicators for ALL NON-selected objects that have pivot override
                        if (bridge.EditorObjectManager != null)
                        {
                            foreach (var obj in bridge.EditorObjectManager.Objects)
                            {
                                if (bridge.SelectedEditorObjects.Contains(obj)) continue; // already has full gizmo/crosshair
                                if (obj.GizmoPivotOverride != null)
                                {
                                    GL.Disable(Const.GL_DEPTH_TEST);
                                    TerrainChunk.DrawPivotDot(obj.GizmoPivotOverride.Value, _editorCamera);
                                    GL.Enable(Const.GL_DEPTH_TEST);
                                }
                            }
                        }
                    }

                    // ── Reset PolygonMode to GL_FILL after viewport rendering ──
                    // This ensures wireframe mode from the editor scene does NOT
                    // leak into any subsequent game scene rendering.
                    GL.PolygonMode(Const.GL_FRONT_AND_BACK, Const.GL_FILL);

                    // Update bridge camera so the IDE panels can show camera info
                    bridge.Camera = _editorCamera;
                    ApplyPendingEditorCamera(_editorCamera, bridge); // restore saved freefly pos when available
                    bridge.CameraPosition = _editorCamera.Position;

                    // Mark texture as rendered so the fallback below doesn't override with a cleared FBO
                    bridge.SceneTextureID = _sharedColorTex;
                    bridge.SceneTextureWidth = Glfw.WindowWidth;
                    bridge.SceneTextureHeight = Glfw.WindowHeight;
                }

                // ── After scene render: ensure viewport texture is set & bind fb 0 for ImGui ──
                if (_ide != null && _ide.IsActive)
                {
                    var bridge = _ide.Bridge;

                    // Resolve the shared MSAA FBO into its single-sample texture so the
                    // Viewport panel (and any scene that rendered into the shared FBO)
                    // samples an antialiased, up-to-date frame.
                    ResolveSharedFBO();

                    if (bridge != null && bridge.SceneTextureID == 0)
                    {
                        // Scene didn't set its own texture (MainMenuScene, etc.) — use shared FBO
                        bridge.SceneTextureID = _sharedColorTex;
                        bridge.SceneTextureWidth = Glfw.WindowWidth;
                        bridge.SceneTextureHeight = Glfw.WindowHeight;
                    }

                    // Always bind framebuffer 0 so ImGui renders to screen, not to any FBO
                    GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                    GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
                    GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
                }

                // ── Render IDE overlay on top of scene ──
                if (_ide != null && _ide.IsActive)
                    _ide.Render();

                // ═══════════════════════════════════════════════════════
                // PHASE 3: DEFERRED SCENE SWITCH (applied AFTER render)
                // ═══════════════════════════════════════════════════════
                // 🛠️ FIX #1: Scene switch happens AFTER this frame's Render
                // so the old scene isn't destroyed before its render pass.
                // If SwitchScene() was called during Update(), _nextScene
                // is set here and applied before the next Update().
                if (_nextScene != null && _nextScene != _currentScene)
                {
                    SwitchToScene(_nextScene, false);

                    // When InGame mode (IDE off), update cursor for the new scene
                    if (_ide != null && !_ide.IsActive)
                        UpdateInGameCursor();
                }

                OpenGL.SwapBuffer(window);
                OpenGL.PollEvents();
            }

            // Cleanup shared FBO + editor grid
            DestroySharedFBO();
            CleanupEditorGrid();
            _editorCsm?.Dispose();
            _editorCsm = null;
            _editorLights?.DisposeLocalShadow();

            // Cleanup IDE
            Glfw.OnWindowResized -= OnSharedFboResized;
            _ide?.Dispose();
            _ide = null;

            // Cleanup current scene
            _currentScene?.Exit();
            _currentScene?.Dispose();
            _currentScene = null;
            _nextScene = null;

            Console.WriteLine("Engine Shutdown.");
        }

        /// <summary>
        /// Request a scene switch. The switch happens at the end of the current frame (after Render).
        /// </summary>
        public void SwitchScene(IScene scene)
        {
            _nextScene = scene;
        }

        /// <summary>
        /// Immediately switch to a scene (used for initial setup or from within Enter()).
        /// </summary>
        private void SwitchToScene(IScene scene, bool isInitial)
        {
            _currentScene?.Exit();
            _currentScene?.Dispose();

            _currentScene = scene;
            _nextScene = null;

            if (!isInitial)
            {
                Console.WriteLine($"[Scene] Switched to: {scene.Name}");
            }

            scene.Enter();
        }

        private void DestroySharedFBO()
        {
            if (!_sharedFBOCreated) return;
            fixed (uint* p = &_sharedFBO) GL.DeleteFramebuffers(1, p);
            fixed (uint* p = &_sharedColorTex) GL.DeleteTextures(1, p);
            fixed (uint* p = &_sharedDepthRBO) GL.DeleteRenderbuffers(1, p);
            fixed (uint* p = &_sharedMsaaColorRBO) GL.DeleteRenderbuffers(1, p);
            fixed (uint* p = &_sharedResolveFBO) GL.DeleteFramebuffers(1, p);
            _sharedFBO = 0;
            _sharedColorTex = 0;
            _sharedDepthRBO = 0;
            _sharedMsaaColorRBO = 0;
            _sharedResolveFBO = 0;
            _sharedFBOCreated = false;
        }

        // ══════════════════════════════════════════════
        //  Editor Grid (Ground plane + Axis helpers)
        // ══════════════════════════════════════════════

        /// <summary>Create VAO/VBO for the dynamic editor grid and static axis helpers.</summary>
        private void CreateEditorGrid()
        {
            CleanupEditorGrid();

            // ── Grid VAO/VBO: allocated once, updated each frame via BufferSubData ──
            uint gridVAO = 0, gridVBO = 0;
            GL.GenVertexArrays(1, &gridVAO);
            GL.GenBuffers(1, &gridVBO);
            GL.BindVertexArray(gridVAO);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, gridVBO);

            // Pre-allocate max vertex buffer (501 lines × 4 verts × 3 floats)
            int maxFloats = 501 * 4 * 3;
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(maxFloats * sizeof(float)), (void*)0, Const.GL_DYNAMIC_DRAW);

            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), null);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
            _editorGridVAO = gridVAO;
            _editorGridVBO = gridVBO;

            // ── Axis helpers: 3 lines at world origin ──
            float[] axisVerts =
            [
                0f, 0f, 0f,   2f, 0f, 0f,
                0f, 0f, 0f,   0f, 2f, 0f,
                0f, 0f, 0f,   0f, 0f, 2f,
            ];
            uint axisVAO = 0, axisVBO = 0;
            GL.GenVertexArrays(1, &axisVAO);
            GL.GenBuffers(1, &axisVBO);
            GL.BindVertexArray(axisVAO);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, axisVBO);
            fixed (float* ptr = axisVerts)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(axisVerts.Length * sizeof(float)), ptr, Const.GL_STATIC_DRAW);
            }
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), null);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
            _editorAxisVAO = axisVAO;
            _editorAxisVBO = axisVBO;

            _editorGridCreated = true;
            Console.WriteLine("[SceneManager] Editor grid created.");
        }

        /// <summary>Apply a pending saved freefly-camera restore (set by SceneManagerPanel
        /// when a .ing file with a saved camera is loaded) to the editor camera, then clear
        /// it so it only fires once. Safe to call every frame — no-op when nothing pending.</summary>
        private static void ApplyPendingEditorCamera(Camera cam, IDEBridge bridge)
        {
            if (bridge?.PendingCameraPos is not Vector3 pos) return;
            cam.Position = pos;
            cam.Yaw = bridge.PendingCameraYaw ?? cam.Yaw;
            cam.Pitch = bridge.PendingCameraPitch ?? cam.Pitch;
            bridge.PendingCameraPos = null;
            bridge.PendingCameraYaw = null;
            bridge.PendingCameraPitch = null;
        }

        /// Render the editor ground-plane grid centered on the camera's XZ position.
        /// Vertices are regenerated each frame so the grid always extends equally
        /// in all directions from the camera (truly "infinite" feel).
        /// Lines are offset by 0.5 so they sit on half-integer coordinates: any NxN plane
        /// centered at an integer position spans -N/2..N/2 and covers exactly NxN grid squares.
        /// Axes stay at world origin.
        /// </summary>
        private void RenderEditorGrid()
        {
            if (!_editorGridCreated) CreateEditorGrid();
            if (_editorGridVAO == 0 || _editorAxisVAO == 0) return;
            if (_editorCamera == null) return;

            uint prog = Shader.GetLineShaderProgram();
            if (prog == 0) return;

            GL.UseProgram(prog);

            Matrix4x4 viewMatrix = _editorCamera.GetViewMatrix();
            Matrix4x4 projMatrix = _editorCamera.GetProjectionMatrix();
            Matrix4x4 ident = Matrix4x4.Identity;

            int viewLoc = GL.GetUniformLocation(prog, "view");
            int projLoc = GL.GetUniformLocation(prog, "projection");
            int modelLoc = GL.GetUniformLocation(prog, "model");
            int colorLoc = GL.GetUniformLocation(prog, "lineColor");

            GL.UniformMatrix4fv(viewLoc, 1, false, &viewMatrix.M11);
            GL.UniformMatrix4fv(projLoc, 1, false, &projMatrix.M11);
            GL.UniformMatrix4fv(modelLoc, 1, false, &ident.M11);

            // ── Generate grid vertices centered on camera XZ, snapped to half-integer ──
            // Offsetting by 0.5 makes each 1-unit grid square centered on an integer position,
            // so an NxN plane placed at an integer coordinate covers exactly NxN squares.
            const int halfLines = 250;        // 250 lines on each side = 501 total
            float centerX = MathF.Round(_editorCamera.Position.X - 0.5f) + 0.5f;
            float centerZ = MathF.Round(_editorCamera.Position.Z - 0.5f) + 0.5f;

            var verts = new List<float>((halfLines * 2 + 1) * 4 * 3);
            for (int i = -halfLines; i <= halfLines; i++)
            {
                // X-aligned line at Z = centerZ + i
                float z = centerZ + i;
                verts.Add(centerX - halfLines); verts.Add(0); verts.Add(z);
                verts.Add(centerX + halfLines); verts.Add(0); verts.Add(z);

                // Z-aligned line at X = centerX + i
                float x = centerX + i;
                verts.Add(x); verts.Add(0); verts.Add(centerZ - halfLines);
                verts.Add(x); verts.Add(0); verts.Add(centerZ + halfLines);
            }

            _editorGridVertexCount = verts.Count / 3;
            float[] gridArray = verts.ToArray();

            // Upload new vertex data via BufferSubData
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _editorGridVBO);
            fixed (float* ptr = gridArray)
            {
                GL.BufferSubData(Const.GL_ARRAY_BUFFER, (nuint)0,
                    (nuint)(gridArray.Length * sizeof(float)), ptr);
            }

            // ── Draw grid in blue-gray ──
            GL.Uniform3f(colorLoc, 0.35f, 0.45f, 0.65f);
            GL.BindVertexArray(_editorGridVAO);
            GL.DrawArrays(Const.GL_LINES, 0, _editorGridVertexCount);
            GL.BindVertexArray(0);

            // ── Axes at world origin ──
            GL.BindVertexArray(_editorAxisVAO);
            GL.Uniform3f(colorLoc, 1f, 0.2f, 0.2f); // X red
            GL.DrawArrays(Const.GL_LINES, 0, 2);
            GL.Uniform3f(colorLoc, 0.2f, 1f, 0.2f); // Y green
            GL.DrawArrays(Const.GL_LINES, 2, 2);
            GL.Uniform3f(colorLoc, 0.2f, 0.3f, 1f); // Z blue
            GL.DrawArrays(Const.GL_LINES, 4, 2);
            GL.BindVertexArray(0);

            GL.UseProgram(0);
        }

        /// <summary>Delete GPU resources for editor grid.</summary>
        private void CleanupEditorGrid()
        {
            if (_editorGridVAO != 0)
            {
                uint vao = _editorGridVAO;
                GL.DeleteVertexArrays(1, &vao);
                _editorGridVAO = 0;
            }
            if (_editorGridVBO != 0)
            {
                uint vbo = _editorGridVBO;
                GL.DeleteBuffers(1, &vbo);
                _editorGridVBO = 0;
            }
            if (_editorAxisVAO != 0)
            {
                uint vao = _editorAxisVAO;
                GL.DeleteVertexArrays(1, &vao);
                _editorAxisVAO = 0;
            }
            if (_editorAxisVBO != 0)
            {
                uint vbo = _editorAxisVBO;
                GL.DeleteBuffers(1, &vbo);
                _editorAxisVBO = 0;
            }
            _editorGridVertexCount = 0;
            _editorGridCreated = false;
        }

        /// <summary>Update cursor visibility based on current scene when in InGame mode (IDE off).
        /// MainMenu → show cursor, GameScene → hide cursor.</summary>
        private void UpdateInGameCursor()
        {
            string? sceneName = _currentScene?.Name;
            bool showMouse = sceneName == "MainMenuScene" || sceneName == null;
            Mouse.ShowMouse(showMouse);
            if (showMouse)
                Mouse.ResetState();
        }

        /// <summary>Lazily create the editor skybox + moon texture so the viewport can preview
        /// the procedural sky when the active scene contains a Sky editor object.
        /// Texture path matches LoadingScene's moon texture (Artifacts/Textures/moon.png).</summary>
        private void EnsureEditorSkybox()
        {
            if (_editorSkybox != null || _editorSkyboxInitFailed) return;
            try
            {
                _editorSkybox = new Skybox();
                _editorSkyTextures =
                [
                    new Texture("Artifacts/Textures/moon.png"),
                ];
                Console.WriteLine("[SceneManager] Editor skybox created.");
            }
            catch (Exception ex)
            {
                _editorSkybox = null;
                _editorSkyTextures = null;
                _editorSkyboxInitFailed = true;
                Console.WriteLine($"[SceneManager] Editor skybox init failed: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════
        //  Selection Highlight (inverted-hull outline)
        // ══════════════════════════════════════════════
        // Now handled inside EditorObjectManager.Draw() with DrawOutlineStencil()/DrawOutline().
        // The old edge-wireframe approach has been removed.

        /// <summary>Get the active editor scene's background color, or black if none is available.</summary>
        private Vector3 GetEditorSceneBackgroundColor()
        {
            if (_ide == null) return Vector3.Zero;
            var bridge = _ide.Bridge;
            if (bridge == null) return Vector3.Zero;

            // Try selected editor scene first
            if (bridge.SelectedEditorScene != null &&
                bridge.EditorScenes.TryGetValue(bridge.SelectedEditorScene, out var editorScene) &&
                editorScene.RenderProperties != null)
            {
                return editorScene.RenderProperties.BackgroundColor;
            }

            // Fallback: first editor scene with render properties
            foreach (var (_, es) in bridge.EditorScenes)
            {
                if (es.RenderProperties != null)
                    return es.RenderProperties.BackgroundColor;
            }

            return Vector3.Zero;
        }

        /// <summary>Get the active editor scene's render properties, or null if none is available.</summary>
        private SceneRenderProperties? GetEditorSceneRenderProperties()
        {
            if (_ide == null) return null;
            var bridge = _ide.Bridge;
            if (bridge == null) return null;

            // Try selected editor scene first
            if (bridge.SelectedEditorScene != null &&
                bridge.EditorScenes.TryGetValue(bridge.SelectedEditorScene, out var editorScene) &&
                editorScene.RenderProperties != null)
            {
                return editorScene.RenderProperties;
            }

            // Fallback: first editor scene with render properties
            foreach (var (_, es) in bridge.EditorScenes)
            {
                if (es.RenderProperties != null)
                    return es.RenderProperties;
            }

            return null;
        }

        /// <summary>Stop the main loop gracefully.</summary>
        public void Stop()
        {
            _running = false;
        }
    }
}
