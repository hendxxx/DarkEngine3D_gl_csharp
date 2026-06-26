namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>
    /// Base interface for all scenes (Loading, Game, Menu, etc.).
    /// Each scene has its own lifecycle managed by SceneManager.
    /// </summary>
    public interface IScene
    {
        /// <summary>Display name for debug/logging.</summary>
        string Name { get; }

        /// <summary>
        /// Called once when the scene becomes active.
        /// For LoadingScene this is where synchronous loading happens (renders frames internally).
        /// For GameScene this sets up the game state.
        /// </summary>
        void Enter();

        /// <summary>Called every frame before Render(). Update game logic here.</summary>
        void Update(float deltaTime);

        /// <summary>Called every frame after Update(). Render the scene here.</summary>
        void Render();

        /// <summary>Called when leaving the scene. Clean up non-disposable resources.</summary>
        void Exit();

        /// <summary>Called when the scene is being destroyed. Dispose GPU/GL resources here.</summary>
        void Dispose();
    }
}
