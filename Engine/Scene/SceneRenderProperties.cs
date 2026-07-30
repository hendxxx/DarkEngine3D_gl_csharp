using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>
    /// Defines per-scene OpenGL render state properties.
    /// Each scene can customize its own background color, face culling,
    /// wireframe mode, depth test, blending, and VSync.
    /// Applied automatically when the scene becomes active via SceneManager.
    /// </summary>
    public class SceneRenderProperties
    {
        // ── Background ──
        /// <summary>GL.ClearColor value. Default: black (0,0,0).</summary>
        public Vector3 BackgroundColor { get; set; } = new Vector3(0f, 0f, 0f);

        // ── VSync ──
        /// <summary>Enable/disable vertical sync. Default: true.</summary>
        public bool VSync { get; set; } = true;

        // ── Face Culling ──
        /// <summary>Which faces to cull. Default: Back.</summary>
        public CullMode FaceCulling { get; set; } = CullMode.Back;

        /// <summary>Winding order for front faces. Default: CCW.</summary>
        public WindingOrder FrontFaceWinding { get; set; } = WindingOrder.CCW;

        // ── Wireframe ──
        /// <summary>When true, renders polygons as lines (wireframe). Default: false.</summary>
        public bool WireframeMode { get; set; } = false;

        // ── Depth Test ──
        /// <summary>Enable/disable depth testing. Default: true.</summary>
        public bool DepthTest { get; set; } = true;

        // ── Blending ──
        /// <summary>Enable/disable alpha blending. Default: false.</summary>
        public bool Blending { get; set; } = false;

        /// <summary>
        /// Apply all render properties to the current OpenGL state.
        /// Call this at the start of a scene's Render() method.
        /// </summary>
        public unsafe void Apply()
        {
            // ── Background color ──
            GL.ClearColor(BackgroundColor.X, BackgroundColor.Y, BackgroundColor.Z, 1f);

            // ── Face culling ──
            if (FaceCulling == CullMode.None)
            {
                GL.Disable(Const.GL_CULL_FACE);
            }
            else
            {
                GL.Enable(Const.GL_CULL_FACE);
                GL.FrontFace(FrontFaceWinding == WindingOrder.CCW ? Const.GL_CCW : Const.GL_CW);
                switch (FaceCulling)
                {
                    case CullMode.Back:
                        GL.CullFace(Const.GL_BACK);
                        break;
                    case CullMode.Front:
                        GL.CullFace(Const.GL_FRONT);
                        break;
                    case CullMode.FrontAndBack:
                        GL.CullFace(Const.GL_FRONT_AND_BACK);
                        break;
                }
            }

            // ── Wireframe mode ──
            GL.PolygonMode(Const.GL_FRONT_AND_BACK, WireframeMode ? Const.GL_LINE : Const.GL_FILL);

            // ── Depth test ──
            if (DepthTest)
            {
                GL.Enable(Const.GL_DEPTH_TEST);
                GL.DepthFunc(Const.GL_LEQUAL);
                GL.DepthMask(true);
            }
            else
            {
                GL.Disable(Const.GL_DEPTH_TEST);
            }

            // ── Blending ──
            if (Blending)
            {
                GL.Enable(Const.GL_BLEND);
                GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
            }
            else
            {
                GL.Disable(Const.GL_BLEND);
            }
        }
    }

    /// <summary>Face culling mode.</summary>
    public enum CullMode
    {
        None,           // No face culling
        Back,           // Cull back faces (default)
        Front,          // Cull front faces
        FrontAndBack    // Cull both (useful for wireframe debugging)
    }

    /// <summary>Winding order for front face determination.</summary>
    public enum WindingOrder
    {
        CCW,  // Counter-clockwise (default, OpenGL standard)
        CW    // Clockwise
    }
}
