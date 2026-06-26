using DarkEngine3D_gl_csharp.Engine.Libs;

namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>
    /// Manages the scene lifecycle: switching, main game loop, and rendering.
    /// Owns the top-level while-loop that drives the entire application.
    /// </summary>
    public class SceneManager
    {
        private IScene? _currentScene;
        private IScene? _nextScene;
        private bool _running;

        /// <summary>The currently active scene.</summary>
        public IScene? CurrentScene => _currentScene;

        /// <summary>
        /// Start the main loop with the given initial scene.
        /// The initial scene's Enter() is called first (may contain synchronous loading),
        /// then the render loop runs until the window closes or Stop() is called.
        /// </summary>
        public void Run(IScene startScene)
        {
            _running = true;
            SwitchToScene(startScene, true);

            nint window = Glfw.GetWindow();

            while (_running && Glfw.GetWindowShouldClose(window) == 0)
            {
                float dt = Glfw.GetDeltaTime();

                // Handle deferred scene switch (from the previous frame's Update)
                if (_nextScene != null && _nextScene != _currentScene)
                {
                    SwitchToScene(_nextScene, false);
                }

                _currentScene?.Update(dt);
                _currentScene?.Render();

                OpenGL.SwapBuffer(window);
                OpenGL.PollEvents();
            }

            // Cleanup current scene
            _currentScene?.Exit();
            _currentScene?.Dispose();
            _currentScene = null;
            _nextScene = null;

            Console.WriteLine("Engine Shutdown.");
        }

        /// <summary>
        /// Request a scene switch. The switch happens at the start of the next frame.
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

        /// <summary>Stop the main loop gracefully.</summary>
        public void Stop()
        {
            _running = false;
        }
    }
}
