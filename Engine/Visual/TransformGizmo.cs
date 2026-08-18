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
        private const float GizmoRadius = 95f;
        private const float AxisLength = 58f;
        private const float ShaftWidth = 6f;      // Thicker shafts so the axis reads clearly
        private const float HeadRadius = 10f;
        private const float HeadLength = 16f;
        private const float HandleRadius = 6f;
        private const float RingThickness = 10f;  // Thicker rings for better visibility
        private const float RingRadius = 70f;
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
                    DrawTranslateGizmo(camera, worldPosition, center, viewportWidth, viewportHeight, colorLoc);
                    break;
                case GizmoMode.Rotate:
                    DrawRotateGizmo(camera, worldPosition, center, viewportWidth, viewportHeight, colorLoc);
                    break;
                case GizmoMode.Scale:
                    DrawScaleGizmo(camera, worldPosition, center, viewportWidth, viewportHeight, colorLoc);
                    break;
            }

            GL.Disable(Const.GL_BLEND);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.UseProgram(0);
        }

        // ════════════════════════════════════════════════════════════
        //  Gizmo Drawing Helpers
        // ════════════════════════════════════════════════════════════

        private void DrawTranslateGizmo(Camera camera, Vector3 worldPosition, Vector2 center,
                                        int viewportWidth, int viewportHeight, int colorLoc)
        {
            float s = Size;
            Vector3 colX = AxisColor(Axis.X, ColorX, ColorHoverX);
            Vector3 colY = AxisColor(Axis.Y, ColorY, ColorHoverY);
            Vector3 colZ = AxisColor(Axis.Z, ColorZ, ColorHoverZ);

            float worldLen = PixelRadiusToWorld(camera, worldPosition, AxisLength * s, viewportHeight);
            if (worldLen < 0.05f) worldLen = 0.05f;

            // Each axis points along its PROJECTED world direction (camera-relative) and
            // shrinks when it points into the camera, so the gizmo reads correctly from any angle.
            DrawProjectedArrow(camera, worldPosition, Vector3.UnitZ, worldLen, center, colorLoc,
                ShaftWidth * 0.7f * s, HeadRadius * 0.7f * s, HeadLength * 0.7f * s, colZ, viewportWidth, viewportHeight, 0.35f);
            DrawProjectedArrow(camera, worldPosition, Vector3.UnitY, worldLen, center, colorLoc,
                ShaftWidth * s, HeadRadius * s, HeadLength * s, colY, viewportWidth, viewportHeight, 0.75f);
            DrawProjectedArrow(camera, worldPosition, Vector3.UnitX, worldLen, center, colorLoc,
                ShaftWidth * s, HeadRadius * s, HeadLength * s, colX, viewportWidth, viewportHeight, 0.75f);

            // Center circle
            Vector3 cc = _dragAxis != Axis.None ? ColorCenterActive : ColorCenter;
            DrawCircle(center, HandleRadius * 0.8f * s, 12, cc, colorLoc);
        }

        /// <summary>Draw an arrow along a world axis, projected to screen. The screen
        /// length is the projected length, clamped to a minimum so axes pointing away from
        /// the camera stay visible (Unreal-style).</summary>
        private void DrawProjectedArrow(Camera camera, Vector3 worldPosition, Vector3 axis,
                                        float worldLen, Vector2 center, int colorLoc,
                                        float shaftWidth, float headRadius, float headLength,
                                        Vector3 color, int viewportWidth, int viewportHeight,
                                        float minScale)
        {
            float projLen = ProjectAxisScreenLength(camera, worldPosition, axis, worldLen,
                                                    viewportWidth, viewportHeight);
            if (projLen < 1.5f)
            {
                // Axis points nearly straight into the camera — draw a small dot so the
                // axis is still visible and grab-able.
                DrawCircle(center, 4f, 10, color, colorLoc);
                return;
            }

            Vector2 dir = ProjectAxisToScreen(camera, worldPosition, axis, worldLen,
                                              viewportWidth, viewportHeight);
            // Keep a minimum drawn length so the handle never collapses to nothing.
            float drawLen = MathF.Max(projLen, AxisLength * 0.45f);
            Vector2 end = center + dir * drawLen;
            DrawArrowShaft(center, end, shaftWidth, headRadius, headLength, color, colorLoc);
        }

        private void DrawRotateGizmo(Camera camera, Vector3 worldPosition, Vector2 center,
                                     int viewportWidth, int viewportHeight, int colorLoc)
        {
            float s = Size;
            Vector3 colX = AxisColor(Axis.X, ColorX, ColorHoverX);
            Vector3 colY = AxisColor(Axis.Y, ColorY, ColorHoverY);
            Vector3 colZ = AxisColor(Axis.Z, ColorZ, ColorHoverZ);

            // World radius that projects to ~RingRadius px at the object's distance.
            float worldR = PixelRadiusToWorld(camera, worldPosition, RingRadius * s, viewportHeight);
            const int segs = 48;

            // True 3D rings (world axes), projected — Unreal style. Z drawn last (topmost).
            DrawProjectedRing(camera, worldPosition, Vector3.UnitX, worldR, segs, viewportWidth, viewportHeight,
                              RingThickness * s, colX, colorLoc);
            DrawProjectedRing(camera, worldPosition, Vector3.UnitY, worldR, segs, viewportWidth, viewportHeight,
                              RingThickness * s, colY, colorLoc);
            DrawProjectedRing(camera, worldPosition, Vector3.UnitZ, worldR, segs, viewportWidth, viewportHeight,
                              RingThickness * s, colZ, colorLoc);

            // Center dot
            Vector3 cc = _dragAxis != Axis.None ? ColorCenterActive : ColorCenter;
            DrawCircle(center, HandleRadius * 0.6f * s, 12, cc, colorLoc);
        }

        /// <summary>Draw a thick ring from projected 3D circle samples (thicker than the
        /// old rings so the axis to rotate is obvious, per bug #5).</summary>
        private void DrawProjectedRing(Camera camera, Vector3 worldPosition, Vector3 axis,
                                       float worldR, int segments, int viewportWidth, int viewportHeight,
                                       float thickness, Vector3 color, int colorLoc)
        {
            var pts = SampleWorldRing(camera, worldPosition, axis, worldR, segments,
                                      viewportWidth, viewportHeight);
            _vertBuffer.Clear();
            float halfT = thickness * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                Vector2 p0 = pts[i];
                Vector2 p1 = pts[(i + 1) % segments];
                Vector2 seg = p1 - p0;
                Vector2 n = seg.LengthSquared() > 1e-6f
                    ? new Vector2(-seg.Y, seg.X) / seg.Length()
                    : new Vector2(1f, 0f);
                AddQuad(p0 - n * halfT, p0 + n * halfT, p1 - n * halfT, p1 + n * halfT);
            }
            FlushBatch(color, colorLoc);
        }

        private void DrawScaleGizmo(Camera camera, Vector3 worldPosition, Vector2 center,
                                    int viewportWidth, int viewportHeight, int colorLoc)
        {
            float s = Size;
            Vector3 colX = AxisColor(Axis.X, ColorX, ColorHoverX);
            Vector3 colY = AxisColor(Axis.Y, ColorY, ColorHoverY);
            Vector3 colZ = AxisColor(Axis.Z, ColorZ, ColorHoverZ);

            float worldLen = PixelRadiusToWorld(camera, worldPosition, AxisLength * s, viewportHeight);
            if (worldLen < 0.05f) worldLen = 0.05f;

            // Same camera-relative projection as translate; cube handles at the tips.
            DrawProjectedLine(camera, worldPosition, Vector3.UnitZ, worldLen, center, colorLoc,
                ShaftWidth * 0.6f * s, CubeHalfSize * 0.7f * s, colZ, viewportWidth, viewportHeight, 0.35f);
            DrawProjectedLine(camera, worldPosition, Vector3.UnitY, worldLen, center, colorLoc,
                ShaftWidth * 0.8f * s, CubeHalfSize * s, colY, viewportWidth, viewportHeight, 0.75f);
            DrawProjectedLine(camera, worldPosition, Vector3.UnitX, worldLen, center, colorLoc,
                ShaftWidth * 0.8f * s, CubeHalfSize * s, colX, viewportWidth, viewportHeight, 0.75f);

            // Center circle
            Vector3 cc = _dragAxis != Axis.None ? ColorCenterActive : ColorCenter;
            DrawCircle(center, HandleRadius * 0.8f * s, 12, cc, colorLoc);
        }

        /// <summary>Draw a shaft + cube handle along a projected world axis.</summary>
        private void DrawProjectedLine(Camera camera, Vector3 worldPosition, Vector3 axis,
                                       float worldLen, Vector2 center, int colorLoc,
                                       float shaftWidth, float cubeHalf, Vector3 color,
                                       int viewportWidth, int viewportHeight, float minScale)
        {
            float projLen = ProjectAxisScreenLength(camera, worldPosition, axis, worldLen,
                                                    viewportWidth, viewportHeight);
            if (projLen < 1.5f)
            {
                DrawCircle(center, 4f, 10, color, colorLoc);
                return;
            }
            Vector2 dir = ProjectAxisToScreen(camera, worldPosition, axis, worldLen,
                                              viewportWidth, viewportHeight);
            float drawLen = MathF.Max(projLen, AxisLength * 0.45f);
            Vector2 end = center + dir * drawLen;
            DrawThickLine(center, end, shaftWidth, color, colorLoc);
            DrawCube(end, cubeHalf, color, colorLoc);
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

        /// <summary>Screen-space direction of a world-space axis as seen from the camera.
        /// Projects two points (worldPos and worldPos + dir·len) and returns the normalized
        /// screen delta — so the gizmo axes are ALWAYS relative to the camera viewport, no
        /// matter how the camera is rotated (bug #2).</summary>
        private static Vector2 ProjectAxisToScreen(Camera camera, Vector3 worldPos, Vector3 dir,
                                                   float len, int viewportWidth, int viewportHeight)
        {
            Vector2 a = ProjectToScreen(camera, worldPos, viewportWidth, viewportHeight);
            Vector2 b = ProjectToScreen(camera, worldPos + dir * len, viewportWidth, viewportHeight);
            Vector2 d = b - a;
            // Degenerate (axis pointing straight into the camera) → fall back to a stable
            // direction so drawing/hit-testing never produces NaN.
            if (d.LengthSquared() < 1e-4f)
                d = new Vector2(0f, 1f);
            return Vector2.Normalize(d);
        }

        /// <summary>Projected screen-space length of a world axis segment of the given
        /// world length, in pixels. Used to shrink axes that point away from the camera.</summary>
        private static float ProjectAxisScreenLength(Camera camera, Vector3 worldPos, Vector3 dir,
                                                     float len, int viewportWidth, int viewportHeight)
        {
            Vector2 a = ProjectToScreen(camera, worldPos, viewportWidth, viewportHeight);
            Vector2 b = ProjectToScreen(camera, worldPos + dir * len, viewportWidth, viewportHeight);
            return Vector2.Distance(a, b);
        }

        /// <summary>Sample a 3D circle (in the plane perpendicular to <paramref name="axis"/>
        /// centered on <paramref name="center"/>) and project every point to screen space.
        /// The projected points form the ellipse seen from the current camera — this is the
        /// Unreal-style rotation ring (bug #5), and the SAME samples are used for hit-testing
        /// so the ring is exactly grabbable where it is drawn (bug #4).</summary>
        private static Vector2[] SampleWorldRing(Camera camera, Vector3 center, Vector3 axis,
                                                 float worldRadius, int segments,
                                                 int viewportWidth, int viewportHeight)
        {
            var pts = new Vector2[segments];
            // Build an orthonormal basis in the ring plane
            Vector3 axisN = Vector3.Normalize(axis);
            Vector3 upRef = MathF.Abs(axisN.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            Vector3 u = Vector3.Normalize(Vector3.Cross(axisN, upRef));
            Vector3 v = Vector3.Normalize(Vector3.Cross(axisN, u));
            for (int i = 0; i < segments; i++)
            {
                float a = i * MathF.PI * 2f / segments;
                Vector3 p = center + (u * MathF.Cos(a) + v * MathF.Sin(a)) * worldRadius;
                pts[i] = ProjectToScreen(camera, p, viewportWidth, viewportHeight);
            }
            return pts;
        }

        /// <summary>World radius that projects to roughly <paramref name="pixelRadius"/> pixels
        /// at the object's distance from the camera. Perspective uses the FOV; orthographic
        /// uses the ortho view height.</summary>
        private static float PixelRadiusToWorld(Camera camera, Vector3 worldPos, float pixelRadius,
                                                int viewportHeight)
        {
            float camDist = Vector3.Distance(camera.Position, worldPos);
            float pxToWorld;
            if (camera.IsOrthographic)
            {
                pxToWorld = (camera.OrthoSize * 2f) / MathF.Max(1f, viewportHeight);
            }
            else
            {
                float fovRad = camera.FoV * MathF.PI / 180f;
                pxToWorld = 2f * MathF.Tan(fovRad * 0.5f) * MathF.Max(0.001f, camDist)
                            / MathF.Max(1f, viewportHeight);
            }
            return pixelRadius * pxToWorld;
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
                float worldLen = PixelRadiusToWorld(camera, worldPosition, AxisLength * s, viewportHeight);
                if (worldLen < 0.05f) worldLen = 0.05f;

                // Hit-test the same projected arrows that are drawn (camera-relative).
                float dx = DistanceToProjectedAxis(mouseScreen, camera, worldPosition, Vector3.UnitX, worldLen, center, viewportWidth, viewportHeight);
                float dy = DistanceToProjectedAxis(mouseScreen, camera, worldPosition, Vector3.UnitY, worldLen, center, viewportWidth, viewportHeight);
                float dz = DistanceToProjectedAxis(mouseScreen, camera, worldPosition, Vector3.UnitZ, worldLen, center, viewportWidth, viewportHeight);

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
                float worldLen = PixelRadiusToWorld(camera, worldPosition, AxisLength * s, viewportHeight);
                if (worldLen < 0.05f) worldLen = 0.05f;

                // Scale handles sit at the projected arrow tips.
                float dx = DistanceToProjectedAxis(mouseScreen, camera, worldPosition, Vector3.UnitX, worldLen, center, viewportWidth, viewportHeight, true);
                float dy = DistanceToProjectedAxis(mouseScreen, camera, worldPosition, Vector3.UnitY, worldLen, center, viewportWidth, viewportHeight, true);
                float dz = DistanceToProjectedAxis(mouseScreen, camera, worldPosition, Vector3.UnitZ, worldLen, center, viewportWidth, viewportHeight, true);

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
                // Hit-test the REAL projected 3D rings (identical samples to DrawProjectedRing),
                // so each ring is grabbable exactly where it is drawn. Z is drawn LAST
                // (visually on top), so it's checked first and wins ties at the crossings.
                float worldR = PixelRadiusToWorld(camera, worldPosition, RingRadius * s, viewportHeight);
                float hitTol = RingThickness * 3.5f * s;
                const int segs = 48;

                Span<(Vector3 axis, Axis gizmoAxis)> rings =
                [
                    (Vector3.UnitZ, Axis.Z),   // Z ring — drawn top-most
                    (Vector3.UnitX, Axis.X),
                    (Vector3.UnitY, Axis.Y),
                ];

                float bestDist = float.MaxValue;
                Axis bestAxis = Axis.None;
                foreach (var (axisVec, gizmoAxis) in rings)
                {
                    var pts = SampleWorldRing(camera, worldPosition, axisVec, worldR, segs,
                                              viewportWidth, viewportHeight);
                    float minD = float.MaxValue;
                    for (int i = 0; i < segs; i++)
                    {
                        float d = Vector2.Distance(mouseScreen, pts[i]);
                        if (d < minD) minD = d;
                    }
                    if (minD < bestDist)
                    {
                        bestDist = minD;
                        bestAxis = gizmoAxis;
                    }
                }

                // Only register a hit when the mouse is actually near a ring curve
                if (bestDist <= hitTol) result = bestAxis;
            }

            HoverAxis = result;
            _hoverWorldPos = worldPosition;
            return result;
        }

        /// <summary>Distance from the mouse to a projected world axis (matching the drawn
        /// arrow/line). When <paramref name="handleOnly"/> is true, measures to the arrow TIP
        /// (scale handles) instead of the whole shaft.</summary>
        private static float DistanceToProjectedAxis(Vector2 mouseScreen, Camera camera,
                                                     Vector3 worldPosition, Vector3 axis, float worldLen,
                                                     Vector2 center, int viewportWidth, int viewportHeight,
                                                     bool handleOnly = false)
        {
            float projLen = ProjectAxisScreenLength(camera, worldPosition, axis, worldLen,
                                                    viewportWidth, viewportHeight);
            if (projLen < 1.5f)
                return Vector2.Distance(mouseScreen, center); // collapsed to a dot at center

            Vector2 dir = ProjectAxisToScreen(camera, worldPosition, axis, worldLen,
                                              viewportWidth, viewportHeight);
            float drawLen = MathF.Max(projLen, AxisLength * 0.45f);
            Vector2 end = center + dir * drawLen;

            if (handleOnly)
                return Vector2.Distance(mouseScreen, end);
            return PointToSegmentDist(mouseScreen, center, end);
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

            // Convert screen pixels to world units based on camera distance. Orthographic
            // cameras use the ortho view height instead of FOV so dragging stays 1:1 (bug #4).
            float sens;
            if (camera.IsOrthographic)
            {
                sens = (camera.OrthoSize * 2f) / MathF.Max(1f, viewportHeight);
            }
            else
            {
                float camDist = Vector3.Distance(camera.Position, _dragObjPos);
                if (camDist < 0.001f) camDist = 1f;
                float fovRad = camera.FoV * MathF.PI / 180f;
                sens = camDist * MathF.Tan(fovRad * 0.5f) * 2f / viewportHeight;
            }

            Vector3 axisVec = _dragAxis switch
            {
                Axis.X => Vector3.UnitX,
                Axis.Y => Vector3.UnitY,
                Axis.Z => Vector3.UnitZ,
                _ => Vector3.Zero,
            };

            // Project the dragged world axis onto the screen (camera-relative) and measure
            // the mouse delta ALONG that screen direction — so the object stays glued to the
            // cursor and the gizmo never drifts from it while dragging (bug #4).
            float proj = 0f;
            if (axisVec != Vector3.Zero)
            {
                if (EffectiveMode == GizmoMode.Rotate)
                {
                    // Rotate: angle-based rotation from the gizmo center.
                    // Much more intuitive than tangent-based — the rotation follows the
                    // angular movement around the gizmo center, similar to Blender/Unity.
                    float ringPx = RingRadius * Size;
                    Vector2 gizmoCenter = ProjectToScreen(camera, _dragObjPos, viewportWidth, viewportHeight);

                    // Angle from gizmo center to the drag-start mouse position
                    Vector2 startDelta = _dragStartMouse - gizmoCenter;
                    float startAngle = MathF.Atan2(startDelta.Y, startDelta.X);

                    // Angle from gizmo center to the current mouse position
                    Vector2 currentDelta = mouseScreen - gizmoCenter;
                    float currentAngle = MathF.Atan2(currentDelta.Y, currentDelta.X);

                    // Delta angle in degrees — direct angular displacement.
                    // 1:1 mapping so a 90° mouse arc around the gizmo = 90° object rotation.
                    float deltaAngle = (currentAngle - startAngle) * (180f / MathF.PI);
                    proj = deltaAngle;
                }
                else
                {
                    float worldLen = PixelRadiusToWorld(camera, _dragObjPos, AxisLength * Size, viewportHeight);
                    if (worldLen < 0.05f) worldLen = 0.05f;
                    Vector2 screenAxis = ProjectAxisToScreen(camera, _dragObjPos, axisVec, worldLen,
                                                             viewportWidth, viewportHeight);
                    proj = Vector2.Dot(deltaScreen, screenAxis) * sens;
                }
            }

            if (EffectiveMode == GizmoMode.Translate)
            {
                Vector3 newPos = _dragStartValue + axisVec * proj;

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
                float angle = proj; // degrees, computed from the ring tangent above
                Vector3 rotDelta = Vector3.Zero;
                switch (_dragAxis)
                {
                    case Axis.X: rotDelta.X = angle; break;
                    case Axis.Y: rotDelta.Y = angle; break;
                    case Axis.Z: rotDelta.Z = angle; break;
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
