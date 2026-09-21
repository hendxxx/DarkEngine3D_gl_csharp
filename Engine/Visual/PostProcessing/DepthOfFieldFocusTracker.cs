using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{
    /// <summary>What the Depth-of-Field focus circle tracks each frame.</summary>
    public enum DofFocusTarget
    {
        /// <summary>Manual sliders (Post FX panel) — no tracking.</summary>
        ScreenCenter = 0,
        /// <summary>Follow the Player2D object (capsule mid-height).</summary>
        Player = 1,
        /// <summary>Follow the map tile under the mouse cursor (edit mode).</summary>
        HoveredTile = 2,
        /// <summary>Follow the hovered object in the viewport (edit mode).</summary>
        HoveredObject = 3,
        /// <summary>Follow the current editor selection (single or averaged multi).</summary>
        SelectedObject = 4,
    }

    /// <summary>
    /// Resolves the DoF focus target to a WORLD position every frame and writes the
    /// projected screen point into <see cref="PostFxSettings.DofFocusX"/>/<c>DofFocusY</c>.
    /// Invoked from <see cref="DepthOfFieldComposite.Apply"/> — which runs each frame in
    /// BOTH render paths — so tracking works live in edit mode AND play/preview.
    /// Critically-damped smoothing keeps the focus gliding instead of teleporting.
    /// </summary>
    public static class DepthOfFieldFocusTracker
    {
        /// <summary>The live IDE bridge — set once by IDE.cs at startup.</summary>
        public static IDEBridge? Bridge { get; set; }

        private static bool _hasWorld = false;
        private static Vector3 _smoothWorld;
        private static DofFocusTarget _lastMode = DofFocusTarget.ScreenCenter;

        /// <summary>Reset smoothing (call on mode switch or scene change).</summary>
        public static void Snap() => _hasWorld = false;

        /// <summary>Resolve → smooth → project → write PostFxSettings focus. No-op when
        /// tracking is off, the bridge/camera is missing, or the target doesn't exist.</summary>
        public static void Update()
        {
            var mode = (DofFocusTarget)PostFxSettings.DofFocusTarget;
            if (mode == DofFocusTarget.ScreenCenter)
            {
                _hasWorld = false;
                _lastMode = mode;
                return;
            }

            // Mode switch → drop the old smoothed position so the focus glides to the
            // new target instead of sweeping across the whole screen.
            if (mode != _lastMode) { _hasWorld = false; _lastMode = mode; }

            var bridge = Bridge;
            if (bridge?.Camera == null ||
                bridge.SceneTextureWidth <= 0 || bridge.SceneTextureHeight <= 0)
                return;

            if (!TryResolveWorld(bridge, mode, out Vector3 world))
                return;

            // Critically-damped follow: fast enough to feel locked on, slow enough
            // that the focus edge doesn't flicker when the player lands/jumps.
            // PEEK, never Get: Glfw.GetDeltaTime() is consume-once (resets lastFrame
            // and bumps FrameId) — calling it here stole the real frame's dt, which
            // slowed/jittered every dt-driven animation while the FPS meter stayed
            // high. PeekDeltaTime() is a non-destructive read.
            float speed = Math.Clamp(PostFxSettings.DofFollowSpeed, 1f, 30f);
            float t = 1f - MathF.Exp(-speed * MathF.Max(Glfw.PeekDeltaTime(), 0.0001f));
            _smoothWorld = _hasWorld ? Vector3.Lerp(_smoothWorld, world, t) : world;
            _hasWorld = true;

            var sp = TransformGizmo.ProjectToScreen(
                bridge.Camera, _smoothWorld, bridge.SceneTextureWidth, bridge.SceneTextureHeight);

            // Behind the camera / off-clip → keep last value this frame.
            if (float.IsNaN(sp.X) || float.IsInfinity(sp.X)) return;

            PostFxSettings.DofFocusX = Math.Clamp(sp.X / bridge.SceneTextureWidth, 0f, 1f);
            PostFxSettings.DofFocusY = Math.Clamp(sp.Y / bridge.SceneTextureHeight, 0f, 1f);
        }

        /// <summary>Target type → world position, per mode.</summary>
        private static bool TryResolveWorld(IDEBridge bridge, DofFocusTarget mode, out Vector3 world)
        {
            world = default;
            var mgr = bridge.EditorObjectManager;

            switch (mode)
            {
                case DofFocusTarget.Player:
                {
                    if (mgr != null)
                    {
                        foreach (var o in mgr.Objects)
                        {
                            if (o is not { PrimitiveType: EditorPrimitiveType.Player2D }) continue;
                            // Capsule is feet-anchored — track its mid-height so the
                            // sharp circle sits on the body, not the feet.
                            float h = o.Player2DCapsuleHeight;
                            world = new Vector3(o.Position.X, o.Position.Y + h * 0.5f, o.Position.Z);
                            return true;
                        }
                    }
                    // No Player2D object → fall back to the spawn marker on the map.
                    var map = bridge.ActiveTilemap;
                    if (map is { HasPlayerSpawn: true })
                    {
                        float c = map.TileSize * Tilemap2D.WorldScale;
                        world = new Vector3((map.PlayerSpawn.X + 0.5f) * c,
                                            (map.PlayerSpawn.Y + 0.5f) * c, 0f);
                        return true;
                    }
                    return false;
                }

                case DofFocusTarget.HoveredTile:
                {
                    var w = bridge.HoveredMapTileWorld;
                    if (w == null) return false;
                    world = w.Value;
                    return true;
                }

                case DofFocusTarget.HoveredObject:
                {
                    var h = bridge.HoveredEditorObjectWorld;
                    if (h == null) return false;
                    world = h.Value;
                    return true;
                }

                case DofFocusTarget.SelectedObject:
                {
                    var sel = bridge.SelectedEditorObject;
                    if (sel == null)
                    {
                        // Multi-select → average, mirroring the gizmo's group pivot.
                        int n = bridge.SelectedEditorObjects.Count;
                        if (n == 0) return false;
                        Vector3 sum = Vector3.Zero;
                        foreach (var o in bridge.SelectedEditorObjects) sum += o.Position;
                        world = sum / n;
                        return true;
                    }
                    world = sel.Position;
                    return true;
                }
            }
            return false;
        }
    }
}
