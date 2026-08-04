using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Screen-space transform gizmo rendered at the projected screen position of
    /// the selected object's world position. Draws solid filled shapes (not wireframe)
    /// using the outline shader with an orthographic projection.
    /// All triangles are batched per color for efficiency.
    /// </summary>
    public unsafe class TransformGizmo
    {
        public enum GizmoMode { Translate, Rotate, Scale }
        public enum Axis { None, X, Y, Z }

        // ── Public state ──
        public GizmoMode Mode { get; set; } = GizmoMode.Translate;
        public Axis ActiveAxis { get; private set; } = Axis.None;
        public bool IsDragging { get; private set; } = false;
        public bool IsVisible { get; set; } = true;

        // ── Per-selection mode restrictions (Sky markers have no meaningful rotation
        //    or scale — rotation is fixed, scale is ignored — so their gizmo must stay
        //    in Translate mode). ViewportPanel sets these before each draw. ──
        /// <summary>When false, Rotate mode is never used — the gizmo falls back to Translate.</summary>
        public bool AllowRotate { get; set; } = true;
        /// <summary>When false, Scale mode is never used — the gizmo falls back to Translate.</summary>
        public bool AllowScale { get; set; } = true;

        /// <summary>The mode actually in effect: <see cref="Mode"/> clamped to Translate
        /// when the requested mode is disallowed (e.g. a Sky marker selected).</summary>
        public GizmoMode EffectiveMode => Mode switch
        {
            GizmoMode.Rotate when !AllowRotate => GizmoMode.Translate,
            GizmoMode.Scale when !AllowScale => GizmoMode.Translate,
            _ => Mode,
        };

        /// <summary>Axis currently hovered by the mouse (set by HitTest for visual feedback).</summary>
        public Axis HoverAxis { get; private set; } = Axis.None;
        /// <summary>World position of the gizmo that was hit-tested last (set by HitTest).
        /// With multiple gizmos rendered (multi-select) the shared HoverAxis is only applied
        /// to the gizmo drawn at this exact position, so hovering one object's gizmo doesn't
        /// highlight the same axis on every other selected gizmo.</summary>
        private Vector3? _hoverWorldPos;
        /// <summary>World position of the gizmo currently being rendered (set at the start of
        /// Render), used to scope the hover highlight to the correct gizmo.</summary>
        private Vector3 _renderWorldPos;

        // ── Translate snap (movement via gizmo) ──
        /// <summary>When true, translate drags snap to <see cref="SnapValue"/> world-unit increments.</summary>
        public bool SnapEnabled { get; set; } = true;
        /// <summary>Snap increment in world units for translate mode (1 = matches the 1-unit editor grid).</summary>
        public float SnapValue { get; set; } = 1f;

        // ── Gizmo dimensions (in viewport pixels) ──
        private const float GizmoRadius = 75f;
        private const float AxisLength = 58f;
        private const float ShaftWidth = 4f;
        private const float HeadRadius = 10f;
        private const float HeadLength = 16f;
        private const float HandleRadius = 6f;
        private const float RingThickness = 3f;
        private const float RingRadius = 52f;
        private const float CubeHalfSize = 5f;

        // ── Colors ──
        private static readonly Vector3 ColorX = new(1f, 0.25f, 0.2f);
        private static readonly Vector3 ColorY = new(0.2f, 1f, 0.25f);
        private static readonly Vector3 ColorZ = new(0.2f, 0.5f, 1f);
        private static readonly Vector3 ColorActive = new(1f, 1f, 0.2f);
        private static readonly Vector3 ColorCenter = new(0.55f, 0.55f, 0.65f);
        private static readonly Vector3 ColorCenterActive = new(0.7f, 0.7f, 1.0f);
        // ── Hover highlight: brighter, whitened version of each axis color ──
        private static readonly Vector3 ColorHoverX = new(1f, 0.6f, 0.5f);
        private static readonly Vector3 ColorHoverY = new(0.55f, 1f, 0.5f);
        private static readonly Vector3 ColorHoverZ = new(0.5f, 0.75f, 1f);

        /// <summary>Pick the color for an axis: active while dragging, hover-brightened while
        /// the mouse hovers THIS gizmo (multi-select aware), otherwise its base color.</summary>
        private Vector3 AxisColor(Axis axis, Vector3 baseColor, Vector3 hoverColor)
        {
            if (_dragAxis == axis) return ColorActive;
            // Only the gizmo at the exact hovered world position gets the hover highlight,
            // so hovering one selected object's gizmo doesn't light up the same axis on all
            // the other selected gizmos.
            if (HoverAxis == axis && !IsDragging && _hoverWorldPos == _renderWorldPos)
                return hoverColor;
            return baseColor;
        }

        // ── Shared GL resources (lazy init) ──
        private static uint _vao = 0;
        private static uint _vbo = 0;
        private static bool _resourcesInit = false;

        // ── Drag state ──
        private Axis _dragAxis = Axis.None;
        private Vector2 _dragStartMouse;
        private Vector3 _dragStartValue;
        private Vector3 _dragObjPos;
        private EditorObject? _dragTarget;   // primary (gizmo-handle) target
        private EditorObject[] _dragTargets = [];      // all objects moved by this drag (multi-select)
        private Vector3[] _dragStartValues = [];       // per-target start value (position/rotation/scale)
        private Vector3?[] _dragStartPivots = [];      // per-target start pivot override
        private Vector3[] _dragStartPositions = [];    // per-target start world position
        /// <summary>Center of the dragged selection at drag start (average of target
        /// positions). Scale/rotate operations orbit/scatter around this pivot so the
        /// whole group transforms as one unit.</summary>
        private Vector3 _dragGroupCenter;
        /// <summary>Distance from the object's Position (pivot) down to the bottom of its
        /// world-space AABB, captured at drag start. Y-axis snapping uses this so the
        /// object's BASE lands on the grid (Y=0 = resting on the ground) instead of its center.</summary>
        private float _dragStartBottomOffset;

        // ── Gizmo never clips to bottom; it follows the object's projected screen position ──
        // (BottomMargin constant removed — center is now computed from object world position)

        // ── Temporary vertex buffer (reused each frame to avoid allocations) ──
        private readonly List<float> _vertBuffer = new(4096);

        // ════════════════════════════════════════════════════════════
        //  Initialization
        // ════════════════════════════════════════════════════════════

        private static void EnsureResources()
        {
            if (_resourcesInit) return;
            // Use local variables to avoid CS0212 on static fields
            uint vao = 0, vbo = 0;
            GL.GenVertexArrays(1, &vao);
            _vao = vao;
            GL.GenBuffers(1, &vbo);
            _vbo = vbo;
            GL.BindVertexArray(_vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
            // Allocate enough space for ~8k floats (2666 vertices = ~888 triangles)
            GL.BufferData(Const.GL_ARRAY_BUFFER, 8192 * sizeof(float), (void*)0, Const.GL_STREAM_DRAW);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), null);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
            _resourcesInit = true;
        }

        // ════════════════════════════════════════════════════════════
        //  Render
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Render the gizmo at the projected screen position of the given world position.
        /// Uses the outline shader (flat color) with orthographic projection.
        /// </summary>
        /// <param name="camera">The camera used to project the world position.</param>
        /// <param name="worldPosition">The 3D world position of the selected object.</param>
        /// <param name="viewportWidth">Width of the viewport/framebuffer in pixels.</param>
        /// <param name="viewportHeight">Height of the viewport/framebuffer in pixels.</param>
        /// <summary>Current gizmo size multiplier (reads from Config.CameraConfig.GizmoSize).</summary>
        public float Size => Config.CameraConfig.GizmoSize;

        public void Render(Camera camera, Vector3 worldPosition, int viewportWidth, int viewportHeight)
        {
            if (!IsVisible) return;
            EnsureResources();
            if (_vao == 0) return;

            uint prog = Shader.GetOutlineShaderProgram();
            if (prog == 0) return;

            GL.UseProgram(prog);

            // ── Orthographic projection: (0,0) = bottom-left, Y-up ──
            Matrix4x4 proj = Matrix4x4.CreateOrthographicOffCenter(
                0, viewportWidth, 0, viewportHeight, -1, 1);
            Matrix4x4 ident = Matrix4x4.Identity;

            int projLoc = GL.GetUniformLocation(prog, "projection");
            int viewLoc = GL.GetUniformLocation(prog, "view");
            int modelLoc = GL.GetUniformLocation(prog, "model");
            int colorLoc = GL.GetUniformLocation(prog, "outlineColor");

            GL.UniformMatrix4fv(projLoc, 1, false, (float*)&proj);
            GL.UniformMatrix4fv(viewLoc, 1, false, (float*)&ident);
            GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&ident);

            GL.Disable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_BLEND);
            GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);

            // Remember which gizmo is being drawn so hover highlighting (scoped via
            // _hoverWorldPos) only applies to the gizmo the mouse is actually over.
            _renderWorldPos = worldPosition;

            // Project the object's world position to screen coordinates
            Vector2 center = ProjectToScreen(camera, worldPosition, viewportWidth, viewportHeight);

            // NOTE: no dark halo/backing disc behind the gizmo anymore — it made the
            // gizmo look like it sat on a black background. The axes now draw cleanly
            // over the scene.

            switch (EffectiveMode)
            {
                case GizmoMode.Translate:
                    DrawTranslateGizmo(center, colorLoc);
                    break;
                case GizmoMode.Rotate:
                    DrawRotateGizmo(center, colorLoc);
                    break;
                case GizmoMode.Scale:
                    DrawScaleGizmo(center, colorLoc);
                    break;
            }

            GL.Disable(Const.GL_BLEND);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.UseProgram(0);
        }

        // ════════════════════════════════════════════════════════════
        //  Gizmo Drawing Helpers
        // ════════════════════════════════════════════════════════════

        private void DrawTranslateGizmo(Vector2 center, int colorLoc)
        {
            float s = Size;
            Vector3 colX = AxisColor(Axis.X, ColorX, ColorHoverX);
            Vector3 colY = AxisColor(Axis.Y, ColorY, ColorHoverY);
            Vector3 colZ = AxisColor(Axis.Z, ColorZ, ColorHoverZ);

            // Z axis (into screen) — drawn first so it's behind others
            Vector2 zDir = Vector2.Normalize(new Vector2(-1, -1));
            Vector2 zEnd = center + zDir * AxisLength * 0.6f * s;
            DrawArrowShaft(center, zEnd, ShaftWidth * 0.7f * s, HeadRadius * 0.7f * s, HeadLength * 0.7f * s, colZ, colorLoc);

            // Y axis (up)
            Vector2 yEnd = center + new Vector2(0, AxisLength * s);
            DrawArrowShaft(center, yEnd, ShaftWidth * s, HeadRadius * s, HeadLength * s, colY, colorLoc);

            // X axis (right)
            Vector2 xEnd = center + new Vector2(AxisLength * s, 0);
            DrawArrowShaft(center, xEnd, ShaftWidth * s, HeadRadius * s, HeadLength * s, colX, colorLoc);

            // Center circle
            Vector3 cc = _dragAxis != Axis.None ? ColorCenterActive : ColorCenter;
            DrawCircle(center, HandleRadius * 0.8f * s, 12, cc, colorLoc);
        }

        private void DrawRotateGizmo(Vector2 center, int colorLoc)
        {
            float s = Size;
            Vector3 colX = AxisColor(Axis.X, ColorX, ColorHoverX);
            Vector3 colY = AxisColor(Axis.Y, ColorY, ColorHoverY);
            Vector3 colZ = AxisColor(Axis.Z, ColorZ, ColorHoverZ);

            float r = RingRadius * s;

            // Draw rings with elliptical projection to simulate 3D orientation
            // X ring (red) — vertical ellipse (YZ plane)
            DrawEllipticalRing(center, r, RingThickness * s, 0.3f, 1.0f, 0f, 32, colX, colorLoc);

            // Y ring (green) — horizontal ellipse (XZ plane)
            DrawEllipticalRing(center, r, RingThickness * s, 1.0f, 0.3f, 0f, 32, colY, colorLoc);

            // Z ring (blue) — full circle (XY plane)
            DrawRing(center, r, RingThickness * s, 32, colZ, colorLoc);

            // Center dot
            Vector3 cc = _dragAxis != Axis.None ? ColorCenterActive : ColorCenter;
            DrawCircle(center, HandleRadius * 0.6f * s, 12, cc, colorLoc);
        }

        private void DrawScaleGizmo(Vector2 center, int colorLoc)
        {
            float s = Size;
            Vector3 colX = AxisColor(Axis.X, ColorX, ColorHoverX);
            Vector3 colY = AxisColor(Axis.Y, ColorY, ColorHoverY);
            Vector3 colZ = AxisColor(Axis.Z, ColorZ, ColorHoverZ);

            // Z shaft (into screen)
            Vector2 zDir = Vector2.Normalize(new Vector2(-1, -1));
            Vector2 zEnd = center + zDir * AxisLength * 0.6f * s;
            DrawThickLine(center, zEnd, ShaftWidth * 0.6f * s, colZ, colorLoc);
            DrawCube(zEnd, CubeHalfSize * 0.7f * s, colZ, colorLoc);

            // Y shaft (up)
            Vector2 yEnd = center + new Vector2(0, AxisLength * s);
            DrawThickLine(center, yEnd, ShaftWidth * 0.8f * s, colY, colorLoc);
            DrawCube(yEnd, CubeHalfSize * s, colY, colorLoc);

            // X shaft (right)
            Vector2 xEnd = center + new Vector2(AxisLength * s, 0);
            DrawThickLine(center, xEnd, ShaftWidth * 0.8f * s, colX, colorLoc);
            DrawCube(xEnd, CubeHalfSize * s, colX, colorLoc);

            // Center circle
            Vector3 cc = _dragAxis != Axis.None ? ColorCenterActive : ColorCenter;
            DrawCircle(center, HandleRadius * 0.8f * s, 12, cc, colorLoc);
        }

        // ════════════════════════════════════════════════════════════
        //  Primitive Drawers (fill _vertBuffer, then Flush)
        // ════════════════════════════════════════════════════════════

        private void DrawArrowShaft(Vector2 from, Vector2 to, float shaftWidth,
                                     float headRadius, float headLength,
                                     Vector3 color, int colorLoc)
        {
            Vector2 dir = Vector2.Normalize(to - from);
            Vector2 perp = new(-dir.Y, dir.X);
            float sw2 = shaftWidth * 0.5f;
            Vector2 headBase = to - dir * headLength;

            // Shaft quad (2 triangles)
            _vertBuffer.Clear();
            AddQuad(from - perp * sw2, from + perp * sw2, headBase - perp * sw2, headBase + perp * sw2);
            FlushBatch(color, colorLoc);

            // Arrow head: triangles around the tip
            int segs = 10;
            _vertBuffer.Clear();
            for (int i = 0; i < segs; i++)
            {
                float a0 = (float)i / segs * MathF.PI * 2f;
                float a1 = (float)(i + 1) / segs * MathF.PI * 2f;
                Vector2 s0 = headBase + perp * MathF.Cos(a0) * headRadius
                                   + dir * MathF.Sin(a0) * headRadius * 0.4f;
                Vector2 s1 = headBase + perp * MathF.Cos(a1) * headRadius
                                   + dir * MathF.Sin(a1) * headRadius * 0.4f;
                AddTri(to, s0, s1);
            }
            FlushBatch(color, colorLoc);
        }

        private void DrawThickLine(Vector2 from, Vector2 to, float halfWidth,
                                    Vector3 color, int colorLoc)
        {
            Vector2 dir = Vector2.Normalize(to - from);
            Vector2 perp = new(-dir.Y, dir.X);
            _vertBuffer.Clear();
            AddQuad(from - perp * halfWidth, from + perp * halfWidth,
                     to - perp * halfWidth, to + perp * halfWidth);
            FlushBatch(color, colorLoc);
        }

        private void DrawCircle(Vector2 center, float radius, int segments,
                                 Vector3 color, int colorLoc)
        {
            _vertBuffer.Clear();
            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)i / segments * MathF.PI * 2f;
                float a1 = (float)(i + 1) / segments * MathF.PI * 2f;
                Vector2 p0 = center + new Vector2(MathF.Cos(a0) * radius, MathF.Sin(a0) * radius);
                Vector2 p1 = center + new Vector2(MathF.Cos(a1) * radius, MathF.Sin(a1) * radius);
                AddTri(center, p0, p1);
            }
            FlushBatch(color, colorLoc);
        }

        private void DrawRing(Vector2 center, float radius, float thickness,
                               int segments, Vector3 color, int colorLoc)
        {
            _vertBuffer.Clear();
            float halfT = thickness * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)i / segments * MathF.PI * 2f;
                float a1 = (float)(i + 1) / segments * MathF.PI * 2f;
                float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
                float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                float ri = radius - halfT;
                float ro = radius + halfT;
                Vector2 i0 = center + new Vector2(c0 * ri, s0 * ri);
                Vector2 o0 = center + new Vector2(c0 * ro, s0 * ro);
                Vector2 i1 = center + new Vector2(c1 * ri, s1 * ri);
                Vector2 o1 = center + new Vector2(c1 * ro, s1 * ro);
                AddQuad(i0, o0, i1, o1);
            }
            FlushBatch(color, colorLoc);
        }

        private void DrawEllipticalRing(Vector2 center, float radius, float thickness,
                                         float scaleX, float scaleY, float angle,
                                         int segments, Vector3 color, int colorLoc)
        {
            _vertBuffer.Clear();
            float halfT = thickness * 0.5f;
            float ca = MathF.Cos(angle), sa = MathF.Sin(angle);
            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)i / segments * MathF.PI * 2f;
                float a1 = (float)(i + 1) / segments * MathF.PI * 2f;
                float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
                float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);

                // Local point with elliptical scaling
                float lx0 = c0 * (radius - halfT) * scaleX;
                float ly0 = s0 * (radius - halfT) * scaleY;
                float lx1 = c0 * (radius + halfT) * scaleX;
                float ly1 = s0 * (radius + halfT) * scaleY;
                float lx2 = c1 * (radius - halfT) * scaleX;
                float ly2 = s1 * (radius - halfT) * scaleY;
                float lx3 = c1 * (radius + halfT) * scaleX;
                float ly3 = s1 * (radius + halfT) * scaleY;

                // Rotate
                float rx0 = lx0 * ca - ly0 * sa;
                float ry0 = lx0 * sa + ly0 * ca;
                float rx1 = lx1 * ca - ly1 * sa;
                float ry1 = lx1 * sa + ly1 * ca;
                float rx2 = lx2 * ca - ly2 * sa;
                float ry2 = lx2 * sa + ly2 * ca;
                float rx3 = lx3 * ca - ly3 * sa;
                float ry3 = lx3 * sa + ly3 * ca;

                Vector2 i0 = center + new Vector2(rx0, ry0);
                Vector2 o0 = center + new Vector2(rx1, ry1);
                Vector2 i1 = center + new Vector2(rx2, ry2);
                Vector2 o1 = center + new Vector2(rx3, ry3);
                AddQuad(i0, o0, i1, o1);
            }
            FlushBatch(color, colorLoc);
        }

        private void DrawCube(Vector2 center, float halfSize, Vector3 color, int colorLoc)
        {
            _vertBuffer.Clear();
            float hs = halfSize;
            Vector2 tl = center + new Vector2(-hs, -hs);
            Vector2 tr = center + new Vector2(hs, -hs);
            Vector2 bl = center + new Vector2(-hs, hs);
            Vector2 br = center + new Vector2(hs, hs);
            AddTri(tl, tr, bl);
            AddTri(bl, tr, br);
            FlushBatch(color, colorLoc);
        }

        // ════════════════════════════════════════════════════════════
        //  Vertex Batching
        // ════════════════════════════════════════════════════════════

        private void AddTri(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            _vertBuffer.Add(p0.X); _vertBuffer.Add(p0.Y); _vertBuffer.Add(0f);
            _vertBuffer.Add(p1.X); _vertBuffer.Add(p1.Y); _vertBuffer.Add(0f);
            _vertBuffer.Add(p2.X); _vertBuffer.Add(p2.Y); _vertBuffer.Add(0f);
        }

        private void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            // Two triangles: a-c-b and b-c-d (CCW winding)
            AddTri(a, c, b);
            AddTri(b, c, d);
        }

        private void FlushBatch(Vector3 color, int colorLoc)
        {
            int count = _vertBuffer.Count;
            if (count == 0) return;
            int vertCount = count / 3;

            // Upload to VBO
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
            fixed (float* ptr = CollectionsMarshal.AsSpan(_vertBuffer))
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(count * sizeof(float)), ptr, Const.GL_STREAM_DRAW);
            }

            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, vertCount);
            GL.BindVertexArray(0);
        }

        // ════════════════════════════════════════════════════════════
        //  World → Screen Projection
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Project a 3D world position to 2D screen coordinates (Y=0=bottom).
        /// Uses the camera's view-projection matrix.
        /// </summary>
        public static Vector2 ProjectToScreen(Camera camera, Vector3 worldPos,
                                                 int viewportWidth, int viewportHeight)
        {
            Matrix4x4 viewMatrix = camera.GetViewMatrix();
            Matrix4x4 projMatrix = camera.GetProjectionMatrix();
            // Row-major: v * view * projection = clip space
            Matrix4x4 viewProj = viewMatrix * projMatrix;

            Vector4 clipPos = Vector4.Transform(new Vector4(worldPos, 1f), viewProj);
            if (Math.Abs(clipPos.W) < 0.0001f) clipPos.W = 0.0001f;

            // Perspective divide → NDC [-1, 1]
            float ndcX = clipPos.X / clipPos.W;
            float ndcY = clipPos.Y / clipPos.W;

            // NDC → screen pixels (Y=0=bottom in OpenGL)
            float screenX = (ndcX * 0.5f + 0.5f) * viewportWidth;
            float screenY = (ndcY * 0.5f + 0.5f) * viewportHeight;

            return new Vector2(screenX, screenY);
        }

        // ════════════════════════════════════════════════════════════
        //  Hit Testing (Screen-Space)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Test if the given screen-space point (Y=0=bottom) hits any gizmo axis.
        /// The gizmo center is computed by projecting the object's world position to screen.
        /// Returns the hit axis, or Axis.None if no hit.
        /// </summary>
        public Axis HitTest(Vector2 mouseScreen, Camera camera, Vector3 worldPosition,
                             int viewportWidth, int viewportHeight)
        {
            float s = Size;
            Vector2 center = ProjectToScreen(camera, worldPosition, viewportWidth, viewportHeight);
            float hitRadius = HandleRadius * 2.5f * s;

            Axis result = Axis.None;
            GizmoMode mode = EffectiveMode;

            if (mode == GizmoMode.Translate)
            {
                Vector2 xEnd = center + new Vector2(AxisLength * s, 0);
                Vector2 yEnd = center + new Vector2(0, AxisLength * s);
                Vector2 zEnd = center + Vector2.Normalize(new Vector2(-1, -1)) * AxisLength * 0.6f * s;

                float dx = PointToSegmentDist(mouseScreen, center, xEnd);
                float dy = PointToSegmentDist(mouseScreen, center, yEnd);
                float dz = PointToSegmentDist(mouseScreen, center, zEnd);

                float minDist = MathF.Min(dx, MathF.Min(dy, dz));
                if (minDist <= hitRadius)
                {
                    if (dx <= dy && dx <= dz) result = Axis.X;
                    else if (dy <= dz) result = Axis.Y;
                    else result = Axis.Z;
                }
            }
            else if (mode == GizmoMode.Scale)
            {
                Vector2 xEnd = center + new Vector2(AxisLength * s, 0);
                Vector2 yEnd = center + new Vector2(0, AxisLength * s);
                Vector2 zEnd = center + Vector2.Normalize(new Vector2(-1, -1)) * AxisLength * 0.6f * s;

                float dx = Vector2.Distance(mouseScreen, xEnd);
                float dy = Vector2.Distance(mouseScreen, yEnd);
                float dz = Vector2.Distance(mouseScreen, zEnd);

                float minDist = MathF.Min(dx, MathF.Min(dy, dz));
                if (minDist <= CubeHalfSize * 2.5f * s)
                {
                    if (dx <= dy && dx <= dz) result = Axis.X;
                    else if (dy <= dz) result = Axis.Y;
                    else result = Axis.Z;
                }
            }
            else if (mode == GizmoMode.Rotate)
            {
                // Hit-test each ring against its ACTUAL drawn shape (X = vertical ellipse,
                // Y = horizontal ellipse, Z = full circle). Sampling points along each ring
                // and picking the closest one makes the rings selectable exactly where they
                // are drawn — the old angle-slicing made Y/Z regions tiny and frustrating.
                float r = RingRadius * s;
                float hitTol = RingThickness * 3.5f * s;
                const int segs = 40;

                float bestDist = float.MaxValue;
                Axis bestAxis = Axis.None;

                // (scaleX, scaleY) per ring, matching DrawRotateGizmo. Z is drawn LAST
                // (visually on top), so it's checked first and wins ties at the elbows.
                Span<(float sx, float sy, Axis axis)> rings =
                [
                    (1.0f, 1.0f, Axis.Z),   // Z ring — full circle (top-most)
                    (0.3f, 1.0f, Axis.X),   // X ring — vertical ellipse
                    (1.0f, 0.3f, Axis.Y),   // Y ring — horizontal ellipse
                ];

                foreach (var (sx, sy, axis) in rings)
                {
                    float minD = float.MaxValue;
                    for (int i = 0; i < segs; i++)
                    {
                        float a = i * MathF.PI * 2f / segs;
                        float px = center.X + MathF.Cos(a) * r * sx;
                        float py = center.Y + MathF.Sin(a) * r * sy;
                        float d = Vector2.Distance(mouseScreen, new Vector2(px, py));
                        if (d < minD) minD = d;
                    }
                    if (minD < bestDist)
                    {
                        bestDist = minD;
                        bestAxis = axis;
                    }
                }

                // Only register a hit when the mouse is actually near a ring curve
                if (bestDist <= hitTol) result = bestAxis;
            }

            HoverAxis = result;
            _hoverWorldPos = worldPosition;
            return result;
        }

        /// <summary>Distance from a point to a line segment.</summary>
        private static float PointToSegmentDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            Vector2 ap = p - a;
            float t = Vector2.Dot(ap, ab) / Vector2.Dot(ab, ab);
            t = Math.Clamp(t, 0f, 1f);
            return Vector2.Distance(p, a + ab * t);
        }

        // ════════════════════════════════════════════════════════════
        //  Drag Operations
        // ════════════════════════════════════════════════════════════

        /// <summary>Start a drag operation on the given axis.
        /// The primary <paramref name="target"/> drives the gizmo handle and snapping;
        /// when <paramref name="targets"/> is supplied (multi-select) every object in the
        /// set moves by the SAME delta so they keep their relative layout (no overlap).</summary>
        public void StartDrag(Axis axis, Vector2 mouseScreen, EditorObject target,
                              IReadOnlyList<EditorObject>? targets = null)
        {
            if (axis == Axis.None) return;
            _dragAxis = axis;
            IsDragging = true;
            ActiveAxis = axis;
            _dragStartMouse = mouseScreen;
            _dragTarget = target;
            _dragObjPos = target.Position;

            // Build the drag set: the given multi-selection (if any), else just the primary.
            var set = new List<EditorObject> { target };
            if (targets != null)
            {
                foreach (var t in targets)
                    if (t != null && !set.Contains(t))
                        set.Add(t);
            }
            _dragTargets = set.ToArray();

            // Per-target start values are mode-dependent; pivot overrides are always captured
            // so the translate branch stays safe even if the gizmo mode changes mid-drag.
            _dragStartPivots = _dragTargets.Select(o => o.GizmoPivotOverride).ToArray();
            _dragStartPositions = _dragTargets.Select(o => o.Position).ToArray();

            // Group pivot = average of all target start positions (selection center).
            // For a single object this equals its own position (relative offset = 0),
            // so scale/rotate behave exactly as before.
            _dragGroupCenter = Vector3.Zero;
            for (int i = 0; i < _dragStartPositions.Length; i++)
                _dragGroupCenter += _dragStartPositions[i];
            _dragGroupCenter /= MathF.Max(1, _dragStartPositions.Length);

            switch (EffectiveMode)
            {
                case GizmoMode.Translate:
                    _dragStartValue = target.Position;
                    // Capture how far the AABB bottom sits below the position pivot so Y-axis
                    // snapping can align the object's BASE to the grid instead of its center.
                    _dragStartBottomOffset = target.WorldAABB.Min.Y - target.Position.Y;
                    _dragStartValues = _dragTargets.Select(o => o.Position).ToArray();
                    break;
                case GizmoMode.Rotate:
                    _dragStartValue = target.RotationEuler;
                    _dragStartValues = _dragTargets.Select(o => o.RotationEuler).ToArray();
                    break;
                case GizmoMode.Scale:
                    _dragStartValue = target.Scale;
                    _dragStartValues = _dragTargets.Select(o => o.Scale).ToArray();
                    break;
            }
        }

        /// <summary>Update the drag with current mouse position.
        /// Moves EVERY object captured at drag start (multi-select) by the same delta,
        /// so the whole group follows the gizmo while keeping its relative layout.</summary>
        public void UpdateDrag(Vector2 mouseScreen, Camera camera, int viewportWidth, int viewportHeight)
        {
            if (!IsDragging || _dragAxis == Axis.None || _dragTarget == null)
                return;

            Vector2 deltaScreen = mouseScreen - _dragStartMouse;

            // Convert screen pixels to world units based on camera distance
            float camDist = Vector3.Distance(camera.Position, _dragObjPos);
            if (camDist < 0.001f) camDist = 1f;
            float fovRad = camera.FoV * MathF.PI / 180f;
            float sens = camDist * MathF.Tan(fovRad * 0.5f) * 2f / viewportHeight;

            float proj = 0f;
            switch (_dragAxis)
            {
                // Mouse arrives in screen space with Y-up (same as the gizmo's render space),
                // so dragging along an arrow's screen direction moves the object the same way
                // in world space — exactly like the X axis (drag right = +X, drag up = +Y).
                case Axis.X: proj = deltaScreen.X * sens; break;
                case Axis.Y: proj = deltaScreen.Y * sens; break;
                case Axis.Z: proj = (deltaScreen.X - deltaScreen.Y) * 0.5f * sens; break;
            }

            if (EffectiveMode == GizmoMode.Translate)
            {
                Vector3 axisDir = _dragAxis switch
                {
                    Axis.X => Vector3.UnitX,
                    Axis.Y => Vector3.UnitY,
                    Axis.Z => Vector3.UnitZ,
                    _ => Vector3.Zero,
                };
                Vector3 newPos = _dragStartValue + axisDir * proj;

                // Snap only the dragged axis so movement stays aligned to the grid.
                // The X/Z axes snap the object's center; the Y axis snaps the object's
                // BOTTOM (same 1-unit grid as X) so Y=0 rests the base on the ground
                // instead of putting the pivot in the middle of the object.
                if (SnapEnabled && SnapValue > 0f)
                {
                    switch (_dragAxis)
                    {
                        case Axis.X: newPos.X = SnapAxis(newPos.X); break;
                        case Axis.Y:
                            newPos.Y = SnapAxis(newPos.Y + _dragStartBottomOffset) - _dragStartBottomOffset;
                            break;
                        case Axis.Z: newPos.Z = SnapAxis(newPos.Z); break;
                    }
                }

                // Apply the SAME (snapped) delta to every selected object so the group
                // moves together while each object keeps its own relative position.
                Vector3 delta = newPos - _dragStartValue;
                for (int i = 0; i < _dragTargets.Length; i++)
                {
                    var t = _dragTargets[i];
                    t.Position = _dragStartValues[i] + delta;

                    // Keep each object's own gizmo pivot override attached while translating:
                    // shift it by the same (snapped) delta so gizmos stay where the user placed
                    // them relative to their objects.
                    if (_dragStartPivots[i] is Vector3 startPivot)
                        t.GizmoPivotOverride = startPivot + delta;
                }
            }
            else if (EffectiveMode == GizmoMode.Scale)
            {
                float scaleFactor = 1f + proj * 0.5f;
                scaleFactor = MathF.Max(0.05f, scaleFactor);
                Vector3 factor = Vector3.One;
                switch (_dragAxis)
                {
                    case Axis.X: factor = new Vector3(scaleFactor, 1f, 1f); break;
                    case Axis.Y: factor = new Vector3(1f, scaleFactor, 1f); break;
                    case Axis.Z: factor = new Vector3(1f, 1f, scaleFactor); break;
                }
                for (int i = 0; i < _dragTargets.Length; i++)
                {
                    var t = _dragTargets[i];

                    // Scale the object itself...
                    t.Scale = _dragStartValues[i] * factor;

                    // ...AND its distance from the group center, so a multi-selection
                    // scatters/collects around the shared pivot instead of each object
                    // growing around its own origin.
                    Vector3 rel = _dragStartPositions[i] - _dragGroupCenter;
                    t.Position = _dragGroupCenter + rel * factor;

                    // Pivot overrides follow their object (same relative transform)
                    if (_dragStartPivots[i] is Vector3 startPivot)
                        t.GizmoPivotOverride = _dragGroupCenter + (startPivot - _dragGroupCenter) * factor;
                }
            }
            else if (EffectiveMode == GizmoMode.Rotate)
            {
                float angle = proj * 60f; // rotation sensitivity
                Vector3 rotDelta = Vector3.Zero;
                Vector3 axisVec = Vector3.Zero;
                switch (_dragAxis)
                {
                    case Axis.X: rotDelta.X = angle; axisVec = Vector3.UnitX; break;
                    case Axis.Y: rotDelta.Y = angle; axisVec = Vector3.UnitY; break;
                    case Axis.Z: rotDelta.Z = angle; axisVec = Vector3.UnitZ; break;
                }
                // Orbit each object around the group center (world-axis rotation)
                // AND rotate the object itself by the same angle, so the whole
                // selection spins as a rigid unit around the shared pivot.
                float angleRad = angle * MathF.PI / 180f;
                Quaternion rotQ = Quaternion.CreateFromAxisAngle(axisVec, angleRad);
                for (int i = 0; i < _dragTargets.Length; i++)
                {
                    var t = _dragTargets[i];
                    Vector3 rel = _dragStartPositions[i] - _dragGroupCenter;
                    t.Position = _dragGroupCenter + Vector3.Transform(rel, rotQ);
                    t.RotationEuler = _dragStartValues[i] + rotDelta;
                    if (_dragStartPivots[i] is Vector3 startPivot)
                        t.GizmoPivotOverride = _dragGroupCenter + Vector3.Transform(startPivot - _dragGroupCenter, rotQ);
                }
            }
        }

        /// <summary>Snap a value to the nearest multiple of <see cref="SnapValue"/>.</summary>
        private float SnapAxis(float value) => MathF.Round(value / SnapValue) * SnapValue;

        /// <summary>All objects moved by the current drag (primary first). Empty when not dragging.
        /// Used by ViewportPanel to report every dragged object for undo support.</summary>
        public IReadOnlyList<EditorObject> DragTargets => _dragTargets;

        /// <summary>Center of the selection being dragged (average of target start
        /// positions). Only meaningful while dragging a MULTI selection in scale/rotate
        /// mode — the fixed pivot the group orbits around. Null otherwise (translate
        /// drags move the whole group so there is no fixed pivot; the marker should
        /// follow the objects' live average instead).</summary>
        public Vector3? GroupCenter =>
            IsDragging && _dragTargets.Length > 1 && EffectiveMode != GizmoMode.Translate
                ? _dragGroupCenter : null;

        /// <summary>End the current drag operation.</summary>
        public void EndDrag()
        {
            _dragAxis = Axis.None;
            IsDragging = false;
            ActiveAxis = Axis.None;
            _dragTarget = null;
            _dragStartBottomOffset = 0f;
            _dragTargets = [];
            _dragStartValues = [];
            _dragStartPivots = [];
            _dragStartPositions = [];
            _dragGroupCenter = Vector3.Zero;
        }
    }
}
