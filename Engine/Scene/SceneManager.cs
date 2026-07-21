using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;

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
        private bool _f2WasDown = false;
        private bool _f9WasDown = false;

        // ── Shared scene FBO (for scenes without their own, e.g. MainMenuScene) ──
        private uint _sharedFBO = 0;
        private uint _sharedColorTex = 0;
        private uint _sharedDepthRBO = 0;
        private bool _sharedFBOCreated = false;

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

                // ── IDE: F2 toggle ──
                if (_ide != null)
                {
                    bool f2Down = Keyboard.IsKeyDown(window, Const.GLFW_KEY_F2);
                    if (f2Down && !_f2WasDown)
                    {
                        _ide.IsActive = !_ide.IsActive;
                        Console.WriteLine($"[SceneManager] Toggle IDE: IsActive={_ide.IsActive}, IsHealthy={_ide.IsHealthy}");
                        Mouse.ShowMouse(_ide.IsActive);
                        if (_ide.IsActive)
                            Mouse.ResetState();
                    }
                    _f2WasDown = f2Down;

                    if (_ide.IsActive)
                    {
                        _ide.Update(dt);
                    }

                    // ── F9: toggle manual input lock for viewport — saved to settings.json ──
                    bool f9Down = Keyboard.IsKeyDown(window, Const.GLFW_KEY_F9);
                    if (f9Down && !_f9WasDown && _ide.IsActive)
                    {
                        _ide.Bridge.InGameActive = !_ide.Bridge.InGameActive;
                        Console.WriteLine($"[SceneManager] Toggle InGameActive: {_ide.Bridge.InGameActive}");

                        // Persist to settings.json
                        var settings = Config.SettingsSave.Load();
                        settings.InGameActive = _ide.Bridge.InGameActive;
                        Config.SettingsSave.Save(settings);
                    }
                    _f9WasDown = f9Down;
                }

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

                _currentScene?.Render();

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
                }

                OpenGL.SwapBuffer(window);
                OpenGL.PollEvents();
            }

            // Cleanup shared FBO
            DestroySharedFBO();

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

        /// <summary>Stop the main loop gracefully.</summary>
        public void Stop()
        {
            _running = false;
        }
    }
}
