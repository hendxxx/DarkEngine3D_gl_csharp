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

        // ── Shared GL resources (lazy init) ──
        private static uint _vao = 0;
        private static uint _vbo = 0;
        private static bool _resourcesInit = false;

        // ── Drag state ──
        private Axis _dragAxis = Axis.None;
        private Vector2 _dragStartMouse;
        private Vector3 _dragStartValue;
        private Vector3 _dragObjPos;
        private EditorObject? _dragTarget;

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

            // Project the object's world position to screen coordinates
            Vector2 center = ProjectToScreen(camera, worldPosition, viewportWidth, viewportHeight);

            switch (Mode)
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
            Vector3 colX = _dragAxis == Axis.X ? ColorActive : ColorX;
            Vector3 colY = _dragAxis == Axis.Y ? ColorActive : ColorY;
            Vector3 colZ = _dragAxis == Axis.Z ? ColorActive : ColorZ;

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
            Vector3 colX = _dragAxis == Axis.X ? ColorActive : ColorX;
            Vector3 colY = _dragAxis == Axis.Y ? ColorActive : ColorY;
            Vector3 colZ = _dragAxis == Axis.Z ? ColorActive : ColorZ;

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
            Vector3 colX = _dragAxis == Axis.X ? ColorActive : ColorX;
            Vector3 colY = _dragAxis == Axis.Y ? ColorActive : ColorY;
            Vector3 colZ = _dragAxis == Axis.Z ? ColorActive : ColorZ;

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
        private static Vector2 ProjectToScreen(Camera camera, Vector3 worldPos,
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

            if (Mode == GizmoMode.Translate)
            {
                Vector2 xEnd = center + new Vector2(AxisLength * s, 0);
                Vector2 yEnd = center + new Vector2(0, AxisLength * s);
                Vector2 zEnd = center + Vector2.Normalize(new Vector2(-1, -1)) * AxisLength * 0.6f * s;

                float dx = PointToSegmentDist(mouseScreen, center, xEnd);
                float dy = PointToSegmentDist(mouseScreen, center, yEnd);
                float dz = PointToSegmentDist(mouseScreen, center, zEnd);

                float minDist = MathF.Min(dx, MathF.Min(dy, dz));
                if (minDist > hitRadius) return Axis.None;

                if (dx <= dy && dx <= dz) return Axis.X;
                if (dy <= dz) return Axis.Y;
                return Axis.Z;
            }
            else if (Mode == GizmoMode.Scale)
            {
                Vector2 xEnd = center + new Vector2(AxisLength * s, 0);
                Vector2 yEnd = center + new Vector2(0, AxisLength * s);
                Vector2 zEnd = center + Vector2.Normalize(new Vector2(-1, -1)) * AxisLength * 0.6f * s;

                float dx = Vector2.Distance(mouseScreen, xEnd);
                float dy = Vector2.Distance(mouseScreen, yEnd);
                float dz = Vector2.Distance(mouseScreen, zEnd);

                float minDist = MathF.Min(dx, MathF.Min(dy, dz));
                if (minDist > CubeHalfSize * 2.5f * s) return Axis.None;

                if (dx <= dy && dx <= dz) return Axis.X;
                if (dy <= dz) return Axis.Y;
                return Axis.Z;
            }
            else if (Mode == GizmoMode.Rotate)
            {
                float dist = Vector2.Distance(mouseScreen, center);
                float ringDist = MathF.Abs(dist - RingRadius * s);
                if (ringDist > RingThickness * 3f * s) return Axis.None;

                // Determine which ring by angle
                Vector2 offset = mouseScreen - center;
                float angle = MathF.Atan2(offset.Y, offset.X);
                float normAngle = (angle / MathF.PI + 1f) % 2f;

                // Segment the circle into 3 regions
                if (normAngle < 0.33f || normAngle >= 1.67f) return Axis.X;
                if (normAngle >= 0.67f && normAngle < 1.33f) return Axis.Y;
                return Axis.Z;
            }

            return Axis.None;
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

        /// <summary>Start a drag operation on the given axis.</summary>
        public void StartDrag(Axis axis, Vector2 mouseScreen, EditorObject target)
        {
            if (axis == Axis.None) return;
            _dragAxis = axis;
            IsDragging = true;
            ActiveAxis = axis;
            _dragStartMouse = mouseScreen;
            _dragTarget = target;
            _dragObjPos = target.Position;

            switch (Mode)
            {
                case GizmoMode.Translate:
                    _dragStartValue = target.Position;
                    break;
                case GizmoMode.Rotate:
                    _dragStartValue = target.RotationEuler;
                    break;
                case GizmoMode.Scale:
                    _dragStartValue = target.Scale;
                    break;
            }
        }

        /// <summary>Update the drag with current mouse position.</summary>
        public void UpdateDrag(Vector2 mouseScreen, EditorObject target,
                                Camera camera, int viewportWidth, int viewportHeight)
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
                case Axis.X: proj = deltaScreen.X * sens; break;
                case Axis.Y: proj = -deltaScreen.Y * sens; break; // Y inverted in screen vs world
                case Axis.Z: proj = (deltaScreen.X - deltaScreen.Y) * 0.5f * sens; break;
            }

            if (Mode == GizmoMode.Translate)
            {
                Vector3 axisDir = _dragAxis switch
                {
                    Axis.X => Vector3.UnitX,
                    Axis.Y => Vector3.UnitY,
                    Axis.Z => Vector3.UnitZ,
                    _ => Vector3.Zero,
                };
                target.Position = _dragStartValue + axisDir * proj;
            }
            else if (Mode == GizmoMode.Scale)
            {
                float scaleFactor = 1f + proj * 0.5f;
                scaleFactor = MathF.Max(0.05f, scaleFactor);
                Vector3 sv = _dragStartValue;
                switch (_dragAxis)
                {
                    case Axis.X: target.Scale = new Vector3(sv.X * scaleFactor, sv.Y, sv.Z); break;
                    case Axis.Y: target.Scale = new Vector3(sv.X, sv.Y * scaleFactor, sv.Z); break;
                    case Axis.Z: target.Scale = new Vector3(sv.X, sv.Y, sv.Z * scaleFactor); break;
                }
            }
            else if (Mode == GizmoMode.Rotate)
            {
                float angle = proj * 60f; // rotation sensitivity
                Vector3 ev = _dragStartValue;
                switch (_dragAxis)
                {
                    case Axis.X: ev.X += angle; break;
                    case Axis.Y: ev.Y += angle; break;
                    case Axis.Z: ev.Z += angle; break;
                }
                target.RotationEuler = ev;
            }
        }

        /// <summary>End the current drag operation.</summary>
        public void EndDrag()
        {
            _dragAxis = Axis.None;
            IsDragging = false;
            ActiveAxis = Axis.None;
            _dragTarget = null;
        }
    }
}
