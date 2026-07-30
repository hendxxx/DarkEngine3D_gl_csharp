using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
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
        private uint _sharedFBO = 0;
        private uint _sharedColorTex = 0;
        private uint _sharedDepthRBO = 0;
        private bool _sharedFBOCreated = false;

        // ── Default camera + lights for rendering editor objects when no scene is active ──
        private Camera? _editorCamera;
        private Lights? _editorLights;

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
            if (_sharedFBOCreated) return;

            uint fbo = 0, color = 0, rbo = 0;

            GL.GenFramebuffers(1, &fbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);

            GL.GenTextures(1, &color);
            GL.BindTexture(Const.GL_TEXTURE_2D, color);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8,
                          Glfw.WindowWidth, Glfw.WindowHeight, 0,
                          Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, color, 0);

            GL.GenRenderbuffers(1, &rbo);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, rbo);
            GL.RenderbufferStorage(Const.GL_RENDERBUFFER, Const.GL_DEPTH24_STENCIL8,
                                   Glfw.WindowWidth, Glfw.WindowHeight);
            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_STENCIL_ATTACHMENT,
                                       Const.GL_RENDERBUFFER, rbo);

            uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
            {
                Console.WriteLine($"[SceneManager] Shared FBO incomplete: 0x{status:X} — cleaning up");
                // 🛠️ FIX #7: Clean up resources on incomplete status instead of leaking them
                GL.DeleteFramebuffers(1, &fbo);
                GL.DeleteTextures(1, &color);
                GL.DeleteRenderbuffers(1, &rbo);
                return;
            }

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

            _sharedFBO = fbo;
            _sharedColorTex = color;
            _sharedDepthRBO = rbo;
            _sharedFBOCreated = true;

            Console.WriteLine($"[SceneManager] Shared FBO created ({Glfw.WindowWidth}x{Glfw.WindowHeight})");
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
                            _editorCamera = new Camera(0f, 8f, 10f, 0f, -35f, aspect, 60f, 0.1f, 500f);
                        }
                        if (_editorLights == null)
                        {
                            _editorLights = new Lights(
                                new Vector3(-0.5f, 0.8f, -0.3f),
                                new Vector3(0.9f, 0.9f, 0.85f),
                                _editorCamera.Position);
                        }

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
                        GL.ClearColor(0f, 0f, 0f, 1f);
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
                        var sceneCam = br.Camera;
                        if (sceneCam != null)
                        {
                            var gizmo = br.EditorGizmo;
                            gizmo.Mode = (TransformGizmo.GizmoMode)br.GizmoMode;
                            float objScale = br.SelectedEditorObject.Scale.Length() * 0.5f;
                            gizmo.Render(sceneCam, br.SelectedEditorObject.Position, objScale);
                        }
                    }

                    // ── Render selection highlight (wireframe AABB box) for active scene ──
                    if (br?.SelectedEditorObject != null)
                    {
                        var sceneCam = br.Camera;
                        if (sceneCam != null)
                            DrawSelectionBox(br, sceneCam);
                    }
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
                        _editorCamera = new Camera(0f, 8f, 10f, 0f, -35f, aspect, 60f, 0.1f, 500f);
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

                    // ── Render editor grid (ground plane + axis helpers) ──
                    GL.Clear(Const.GL_DEPTH_BUFFER_BIT);
                    RenderEditorGrid();

                    // ── Render editor 3D objects ──
                    if (bridge?.EditorObjectManager != null && bridge.EditorObjectManager.Count > 0)
                    {
                        bridge.EditorObjectManager.Draw(_editorCamera, _editorLights, null);
                    }

                    // ── Render gizmo on selected editor object ──
                    if (bridge?.SelectedEditorObject != null && bridge.EditorGizmo != null)
                    {
                        var gizmo = bridge.EditorGizmo;
                        gizmo.Mode = (TransformGizmo.GizmoMode)bridge.GizmoMode;
                        float objScale = bridge.SelectedEditorObject.Scale.Length() * 0.5f;
                        gizmo.Render(_editorCamera, bridge.SelectedEditorObject.Position, objScale);
                    }

                    // ── Render selection highlight (wireframe AABB box) ──
                    DrawSelectionBox(bridge, _editorCamera);

                    // Update bridge camera so the IDE panels can show camera info
                    bridge.Camera = _editorCamera;
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
            _sharedFBO = 0;
            _sharedColorTex = 0;
            _sharedDepthRBO = 0;
            _sharedFBOCreated = false;
        }

        // ══════════════════════════════════════════════
        //  Editor Grid (Ground plane + Axis helpers)
        // ══════════════════════════════════════════════

        /// <summary>Create the ground-plane grid and axis helpers as line VAOs.</summary>
        private void CreateEditorGrid()
        {
            CleanupEditorGrid();

            // ── Ground plane grid: 10x10 units centered at origin, spacing 1 ──
            const float halfSize = 5f;
            const int linesPerDir = 11;

            var gridVerts = new List<float>();

            for (int i = 0; i < linesPerDir; i++)
            {
                float pos = -halfSize + i;
                gridVerts.Add(-halfSize); gridVerts.Add(0f); gridVerts.Add(pos);
                gridVerts.Add(halfSize); gridVerts.Add(0f); gridVerts.Add(pos);
                gridVerts.Add(pos); gridVerts.Add(0f); gridVerts.Add(-halfSize);
                gridVerts.Add(pos); gridVerts.Add(0f); gridVerts.Add(halfSize);
            }

            _editorGridVertexCount = gridVerts.Count / 3;

            float[] gridArray = gridVerts.ToArray();
            uint gridVAO = 0, gridVBO = 0;
            GL.GenVertexArrays(1, &gridVAO);
            GL.GenBuffers(1, &gridVBO);
            GL.BindVertexArray(gridVAO);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, gridVBO);
            fixed (float* ptr = gridArray)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(gridArray.Length * sizeof(float)), ptr, Const.GL_STATIC_DRAW);
            }
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), null);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
            _editorGridVAO = gridVAO;
            _editorGridVBO = gridVBO;

            // ── Axis helpers: 3 lines, each rendered separately with different color ──
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

        /// <summary>Render the editor ground-plane grid and axis helpers.</summary>
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

            int viewLoc = GL.GetUniformLocation(prog, "view");
            int projLoc = GL.GetUniformLocation(prog, "projection");
            int modelLoc = GL.GetUniformLocation(prog, "model");
            int colorLoc = GL.GetUniformLocation(prog, "lineColor");

            GL.UniformMatrix4fv(viewLoc, 1, false, &viewMatrix.M11);
            GL.UniformMatrix4fv(projLoc, 1, false, &projMatrix.M11);

            Matrix4x4 ident = Matrix4x4.Identity;
            GL.UniformMatrix4fv(modelLoc, 1, false, &ident.M11);

            // Grid color (blue-gray)
            GL.Uniform3f(colorLoc, 0.35f, 0.45f, 0.65f);

            GL.BindVertexArray(_editorGridVAO);
            GL.DrawArrays(Const.GL_LINES, 0, _editorGridVertexCount);
            GL.BindVertexArray(0);

            // Axes
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

        // ══════════════════════════════════════════════
        //  Selection Highlight (wireframe AABB box)
        // ══════════════════════════════════════════════

        /// <summary>Draw a wireframe bounding box around the selected editor object.</summary>
        private static void DrawSelectionBox(IDEBridge? bridge, Camera? overrideCamera = null)
        {
            if (bridge?.SelectedEditorObject == null) return;

            var obj = bridge.SelectedEditorObject;
            var aabb = obj.GetWorldAABB();

            // Build 12 edge lines of the AABB
            float minX = aabb.Min.X, minY = aabb.Min.Y, minZ = aabb.Min.Z;
            float maxX = aabb.Max.X, maxY = aabb.Max.Y, maxZ = aabb.Max.Z;

            var verts = new List<Vector3>
            {
                // Bottom face (Z ring)
                new(minX, minY, minZ), new(maxX, minY, minZ),
                new(maxX, minY, minZ), new(maxX, minY, maxZ),
                new(maxX, minY, maxZ), new(minX, minY, maxZ),
                new(minX, minY, maxZ), new(minX, minY, minZ),
                // Top face (Z ring)
                new(minX, maxY, minZ), new(maxX, maxY, minZ),
                new(maxX, maxY, minZ), new(maxX, maxY, maxZ),
                new(maxX, maxY, maxZ), new(minX, maxY, maxZ),
                new(minX, maxY, maxZ), new(minX, maxY, minZ),
                // Vertical connectors
                new(minX, minY, minZ), new(minX, maxY, minZ),
                new(maxX, minY, minZ), new(maxX, maxY, minZ),
                new(maxX, minY, maxZ), new(maxX, maxY, maxZ),
                new(minX, minY, maxZ), new(minX, maxY, maxZ),
            };

            // Use bright cyan/yellow color for selection
            Vector3 selectionColor = new(0.2f, 0.9f, 1.0f); // bright cyan

            // Get the appropriate camera
            Camera? cam = overrideCamera ?? bridge.Camera;
            if (cam == null) return;

            GL.Disable(Const.GL_DEPTH_TEST);
            TerrainChunk.DrawLineSegments(verts, selectionColor, cam);
            GL.Enable(Const.GL_DEPTH_TEST);
        }

        /// <summary>Stop the main loop gracefully.</summary>
        public void Stop()
        {
            _running = false;
        }
    }
}
