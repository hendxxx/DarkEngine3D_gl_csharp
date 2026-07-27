using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// 3D transform gizmo for editing objects in the viewport.
    /// Supports translation, rotation, and scale with axis-specific handles.
    /// </summary>
    public class TransformGizmo
    {
        /// <summary>Current gizmo mode.</summary>
        public enum GizmoMode { Translate, Rotate, Scale }

        /// <summary>Which axis is currently active (for highlighting).</summary>
        public enum Axis { None, X, Y, Z, XY, XZ, YZ }

        // ── State ──
        public GizmoMode Mode { get; set; } = GizmoMode.Translate;
        public Axis ActiveAxis { get; private set; } = Axis.None;
        public bool IsDragging { get; private set; } = false;
        public bool IsVisible { get; set; } = true;

        // ── Gizmo size ──
        private const float AxisLength = 2.5f;
        private const float ArrowHeadSize = 0.3f;
        private const float HandleRadius = 0.12f;
        private const float RotationRingRadius = 2.0f;
        private const float ScaleCubeSize = 0.25f;
        private const float HitTestRadius = 0.15f;

        // ── Drag state ──
        private Axis _dragAxis = Axis.None;
        private Vector3 _dragStartPos;
        private Vector3 _dragStartMouseRay;
        private Vector3 _dragStartValue;

        // ── Colors ──
        private static readonly Vector3 ColorX = new(1f, 0.2f, 0.2f);
        private static readonly Vector3 ColorY = new(0.2f, 1f, 0.2f);
        private static readonly Vector3 ColorZ = new(0.2f, 0.4f, 1f);
        private static readonly Vector3 ColorActive = new(1f, 1f, 0.2f);
        private static readonly Vector3 ColorInactive = new(0.4f, 0.4f, 0.4f);

        /// <summary>
        /// Render the gizmo at the given world position.
        /// Uses LineVertex rendering for arrows/rings and colored quads for handles.
        /// </summary>
        public void Render(Camera camera, Vector3 worldPosition, float objectScale = 1f)
        {
            if (!IsVisible) return;

            float gizmoScale = MathF.Max(0.5f, objectScale);
            GL.Disable(Const.GL_DEPTH_TEST);

            switch (Mode)
            {
                case GizmoMode.Translate:
                    DrawTranslateGizmo(camera, worldPosition, gizmoScale);
                    break;
                case GizmoMode.Rotate:
                    DrawRotateGizmo(camera, worldPosition, gizmoScale);
                    break;
                case GizmoMode.Scale:
                    DrawScaleGizmo(camera, worldPosition, gizmoScale);
                    break;
            }

            GL.Enable(Const.GL_DEPTH_TEST);
        }

        private void DrawTranslateGizmo(Camera camera, Vector3 pos, float scale)
        {
            float len = AxisLength * scale;
            float headSize = ArrowHeadSize * scale;

            // X-axis (red)
            DrawArrowLine(camera, pos, pos + new Vector3(len, 0, 0), ColorX, headSize);
            // Y-axis (green)
            DrawArrowLine(camera, pos, pos + new Vector3(0, len, 0), ColorY, headSize);
            // Z-axis (blue)
            DrawArrowLine(camera, pos, pos + new Vector3(0, 0, len), ColorZ, headSize);

            // Draw axis end cubes (for easier clicking)
            DrawAxisHandle(camera, pos + new Vector3(len, 0, 0), HandleRadius * scale, ColorX);
            DrawAxisHandle(camera, pos + new Vector3(0, len, 0), HandleRadius * scale, ColorY);
            DrawAxisHandle(camera, pos + new Vector3(0, 0, len), HandleRadius * scale, ColorZ);

            // Center sphere
            DrawAxisHandle(camera, pos, HandleRadius * scale * 0.6f, ColorInactive);
        }

        private void DrawRotateGizmo(Camera camera, Vector3 pos, float scale)
        {
            float radius = RotationRingRadius * scale;
            int segments = 48;

            // X-axis ring (red) - in YZ plane
            DrawRing(camera, pos, radius, new Vector3(1, 0, 0), ColorX, segments);
            // Y-axis ring (green) - in XZ plane
            DrawRing(camera, pos, radius, new Vector3(0, 1, 0), ColorY, segments);
            // Z-axis ring (blue) - in XY plane
            DrawRing(camera, pos, radius, new Vector3(0, 0, 1), ColorZ, segments);

            // Center sphere
            DrawAxisHandle(camera, pos, HandleRadius * scale * 0.5f, ColorInactive);
        }

        private void DrawScaleGizmo(Camera camera, Vector3 pos, float scale)
        {
            float len = AxisLength * scale;
            float cubeSize = ScaleCubeSize * scale;

            // X-axis (red)
            DrawLine(camera, pos, pos + new Vector3(len, 0, 0), ColorX);
            DrawCubeHandle(camera, pos + new Vector3(len, 0, 0), cubeSize, ColorX);

            // Y-axis (green)
            DrawLine(camera, pos, pos + new Vector3(0, len, 0), ColorY);
            DrawCubeHandle(camera, pos + new Vector3(0, len, 0), cubeSize, ColorY);

            // Z-axis (blue)
            DrawLine(camera, pos, pos + new Vector3(0, 0, len), ColorZ);
            DrawCubeHandle(camera, pos + new Vector3(0, 0, len), cubeSize, ColorZ);

            // Center cube
            DrawCubeHandle(camera, pos, cubeSize * 0.5f, ColorInactive);
        }

        private static void DrawArrowLine(Camera camera, Vector3 from, Vector3 to, Vector3 color, float headSize)
        {
            DrawLine(camera, from, to, color);

            // Draw arrowhead as two diagonal lines
            Vector3 dir = Vector3.Normalize(to - from);
            Vector3 up = Math.Abs(dir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 right = Vector3.Normalize(Vector3.Cross(dir, up));
            Vector3 ortho = Vector3.Normalize(Vector3.Cross(dir, right));

            float headLen = headSize * 0.6f;
            float headWidth = headSize * 0.4f;

            Vector3 tip = to;
            Vector3 base1 = tip - dir * headLen + ortho * headWidth;
            Vector3 base2 = tip - dir * headLen - ortho * headWidth;
            Vector3 base3 = tip - dir * headLen + right * headWidth;
            Vector3 base4 = tip - dir * headLen - right * headWidth;

            DrawLine(camera, tip, base1, color);
            DrawLine(camera, tip, base2, color);
            DrawLine(camera, tip, base3, color);
            DrawLine(camera, tip, base4, color);
            DrawLine(camera, base1, base2, color);
            DrawLine(camera, base3, base4, color);
        }

        private static void DrawRing(Camera camera, Vector3 center, float radius, Vector3 axis, Vector3 color, int segments)
        {
            // Build two perpendicular vectors on the ring plane
            Vector3 up = Math.Abs(Vector3.Dot(axis, Vector3.UnitY)) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 right = Vector3.Normalize(Vector3.Cross(axis, up));
            Vector3 forward = Vector3.Normalize(Vector3.Cross(axis, right));

            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)i / segments * MathF.PI * 2f;
                float a1 = (float)(i + 1) / segments * MathF.PI * 2f;

                Vector3 p0 = center + (right * MathF.Cos(a0) + forward * MathF.Sin(a0)) * radius;
                Vector3 p1 = center + (right * MathF.Cos(a1) + forward * MathF.Sin(a1)) * radius;

                DrawLine(camera, p0, p1, color);
            }
        }

        private static void DrawLine(Camera camera, Vector3 from, Vector3 to, Vector3 color)
        {
            // Use the line rendering from TerrainChunk
            Terrains.TerrainChunk.DrawLineSegments([from, to], color, camera);
        }

        private static void DrawAxisHandle(Camera camera, Vector3 position, float radius, Vector3 color)
        {
            // Draw a small sphere/quad handle
            float size = radius;
            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            var vp = view * proj;

            Vector4 clip = Vector4.Transform(new Vector4(position, 1f), vp);
            if (clip.W <= 0.05f) return;

            float nx = clip.X / clip.W;
            float ny = clip.Y / clip.W;
            float sx = (nx * 0.5f + 0.5f) * Glfw.WindowWidth;
            float sy = (1f - (ny * 0.5f + 0.5f)) * Glfw.WindowHeight;

            // Draw a screen-space circle
            int segments = 16;
            float screenRadius = Math.Max(3f, size * 30f);
            var verts = new List<Vector3>();
            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)i / segments * MathF.PI * 2f;
                float a1 = (float)(i + 1) / segments * MathF.PI * 2f;

                // Project back to world space (approximate screen-space circle)
                float dx0 = MathF.Cos(a0) * screenRadius / Glfw.WindowWidth * 2f;
                float dy0 = MathF.Sin(a0) * screenRadius / Glfw.WindowHeight * 2f;
                float dx1 = MathF.Cos(a1) * screenRadius / Glfw.WindowWidth * 2f;
                float dy1 = MathF.Sin(a1) * screenRadius / Glfw.WindowHeight * 2f;

                // Convert from screen NDC + depth back to approximate world
                var p0 = ScreenToWorld(ndcX: nx + dx0, ndcY: ny + dy0, clipW: clip.W, vp);
                var p1 = ScreenToWorld(ndcX: nx + dx1, ndcY: ny + dy1, clipW: clip.W, vp);

                verts.Add(p0);
                verts.Add(p1);
            }

            if (verts.Count > 0)
                Terrains.TerrainChunk.DrawLineSegments(verts, color, camera);
        }

        private static Vector3 ScreenToWorld(float ndcX, float ndcY, float clipW, Matrix4x4 invVp)
        {
            // Approximate: use the same depth as the center
            Vector4 nearPt = new Vector4(ndcX, ndcY, 0f, 1f);
            nearPt = Vector4.Transform(nearPt, invVp);
            if (nearPt.W != 0) nearPt /= nearPt.W;
            return new Vector3(nearPt.X, nearPt.Y, nearPt.Z);
        }

        private static void DrawCubeHandle(Camera camera, Vector3 position, float size, Vector3 color)
        {
            float hs = size * 0.5f;
            Vector3[] corners = [
                new(-hs, -hs, -hs), new(hs, -hs, -hs), new(hs, hs, -hs), new(-hs, hs, -hs),
                new(-hs, -hs, hs), new(hs, -hs, hs), new(hs, hs, hs), new(-hs, hs, hs)
            ];

            // Transform to world
            for (int i = 0; i < 8; i++)
                corners[i] += position;

            // 12 edges of the cube
            int[] edges = [0,1, 1,2, 2,3, 3,0, 4,5, 5,6, 6,7, 7,4, 0,4, 1,5, 2,6, 3,7];
            var verts = new List<Vector3>();
            for (int i = 0; i < edges.Length; i += 2)
            {
                verts.Add(corners[edges[i]]);
                verts.Add(corners[edges[i + 1]]);
            }

            Terrains.TerrainChunk.DrawLineSegments(verts, color, camera);
        }

        // ─────────────────────────────────────────────────
        //  Mouse Interaction (Hit Testing + Drag)
        // ─────────────────────────────────────────────────

        /// <summary>
        /// Test if the mouse ray intersects any gizmo axis.
        /// Returns the axis that was hit.
        /// </summary>
        public Axis HitTest(Vector3 rayOrigin, Vector3 rayDir, Vector3 gizmoPosition, float objectScale = 1f)
        {
            float scale = MathF.Max(0.5f, objectScale);
            float len = AxisLength * scale;
            float hitRadius = HitTestRadius * scale;

            if (Mode == GizmoMode.Translate || Mode == GizmoMode.Scale)
            {
                // Test each axis
                if (RayVsCylinder(rayOrigin, rayDir, gizmoPosition, new Vector3(len, 0, 0), hitRadius)) return Axis.X;
                if (RayVsCylinder(rayOrigin, rayDir, gizmoPosition, new Vector3(0, len, 0), hitRadius)) return Axis.Y;
                if (RayVsCylinder(rayOrigin, rayDir, gizmoPosition, new Vector3(0, 0, len), hitRadius)) return Axis.Z;

                // Test end handles
                float handleR = HandleRadius * scale * 1.5f;
                if (RayVsSphere(rayOrigin, rayDir, gizmoPosition + new Vector3(len, 0, 0), handleR)) return Axis.X;
                if (RayVsSphere(rayOrigin, rayDir, gizmoPosition + new Vector3(0, len, 0), handleR)) return Axis.Y;
                if (RayVsSphere(rayOrigin, rayDir, gizmoPosition + new Vector3(0, 0, len), handleR)) return Axis.Z;
            }
            else if (Mode == GizmoMode.Rotate)
            {
                float radius = RotationRingRadius * scale;
                float ringThickness = 0.15f * scale;

                // Test each ring
                if (RayVsRing(rayOrigin, rayDir, gizmoPosition, radius, ringThickness, new Vector3(1, 0, 0))) return Axis.X;
                if (RayVsRing(rayOrigin, rayDir, gizmoPosition, radius, ringThickness, new Vector3(0, 1, 0))) return Axis.Y;
                if (RayVsRing(rayOrigin, rayDir, gizmoPosition, radius, ringThickness, new Vector3(0, 0, 1))) return Axis.Z;
            }

            return Axis.None;
        }

        /// <summary>Start dragging the gizmo on the given axis.</summary>
        public void StartDrag(Axis axis, Vector3 worldPos, Vector3 rayOrigin, Vector3 rayDir, object userData)
        {
            if (axis == Axis.None) return;
            _dragAxis = axis;
            IsDragging = true;
            ActiveAxis = axis;
            _dragStartPos = worldPos;

            if (userData is EditorObject editorObject)
            {
                switch (Mode)
                {
                    case GizmoMode.Translate:
                        _dragStartValue = editorObject.Position;
                        break;
                    case GizmoMode.Rotate:
                        _dragStartValue = editorObject.RotationEuler;
                        break;
                    case GizmoMode.Scale:
                        _dragStartValue = editorObject.Scale;
                        break;
                }
            }

            // Compute the ray intersection with the axis plane for delta tracking
            _dragStartMouseRay = GetAxisIntersection(rayOrigin, rayDir, worldPos, _dragAxis);
        }

        /// <summary>Update drag with current mouse ray. Returns the delta for the active axis.</summary>
        public Vector3 UpdateDrag(Vector3 rayOrigin, Vector3 rayDir, EditorObject target)
        {
            if (!IsDragging || _dragAxis == Axis.None) return Vector3.Zero;

            Vector3 currentRay = GetAxisIntersection(rayOrigin, rayDir, _dragStartPos, _dragAxis);
            Vector3 delta = currentRay - _dragStartMouseRay;

            // Project delta onto the active axis
            Vector3 axisDir = GetAxisDirection(_dragAxis);
            float projection = Vector3.Dot(delta, axisDir);
            Vector3 constrainedDelta = axisDir * projection;

            if (Mode == GizmoMode.Translate)
            {
                target.Position = _dragStartValue + constrainedDelta;
                return constrainedDelta;
            }
            else if (Mode == GizmoMode.Scale)
            {
                float scaleFactor = 1f + projection * 0.5f;
                scaleFactor = MathF.Max(0.1f, scaleFactor);

                if (_dragAxis == Axis.X)
                    target.Scale = new Vector3(_dragStartValue.X * scaleFactor, _dragStartValue.Y, _dragStartValue.Z);
                else if (_dragAxis == Axis.Y)
                    target.Scale = new Vector3(_dragStartValue.X, _dragStartValue.Y * scaleFactor, _dragStartValue.Z);
                else if (_dragAxis == Axis.Z)
                    target.Scale = new Vector3(_dragStartValue.X, _dragStartValue.Y, _dragStartValue.Z * scaleFactor);

                return constrainedDelta;
            }
            else if (Mode == GizmoMode.Rotate)
            {
                float angle = projection * 60f; // 1 unit = 60 degrees
                Vector3 euler = _dragStartValue;
                if (_dragAxis == Axis.X) euler.X += angle;
                else if (_dragAxis == Axis.Y) euler.Y += angle;
                else if (_dragAxis == Axis.Z) euler.Z += angle;
                target.RotationEuler = euler;
                return constrainedDelta;
            }

            return Vector3.Zero;
        }

        /// <summary>End the current drag operation.</summary>
        public void EndDrag()
        {
            _dragAxis = Axis.None;
            IsDragging = false;
            ActiveAxis = Axis.None;
        }

        // ─────────────────────────────────────────────────
        //  Ray Intersection Helpers
        // ─────────────────────────────────────────────────

        private static Vector3 GetAxisDirection(Axis axis)
        {
            return axis switch
            {
                Axis.X => Vector3.UnitX,
                Axis.Y => Vector3.UnitY,
                Axis.Z => Vector3.UnitZ,
                _ => Vector3.Zero,
            };
        }

        /// <summary>Get the intersection point of a ray with the plane perpendicular to the axis at the gizmo position.</summary>
        private static Vector3 GetAxisIntersection(Vector3 rayOrigin, Vector3 rayDir, Vector3 gizmoPos, Axis axis)
        {
            Vector3 planeNormal = axis switch
            {
                Axis.X => Vector3.UnitX,
                Axis.Y => Vector3.UnitY,
                Axis.Z => Vector3.UnitZ,
                _ => Vector3.UnitY,
            };

            // Find intersection of ray with plane: planeNormal · (p - gizmoPos) = 0
            float denom = Vector3.Dot(rayDir, planeNormal);
            if (MathF.Abs(denom) < 0.0001f)
                return gizmoPos;

            float t = Vector3.Dot(gizmoPos - rayOrigin, planeNormal) / denom;
            if (t < 0) return gizmoPos;

            return rayOrigin + rayDir * t;
        }

        private static bool RayVsCylinder(Vector3 origin, Vector3 dir, Vector3 basePos, Vector3 axisEnd, float radius)
        {
            Vector3 axisDir = Vector3.Normalize(axisEnd);
            float axisLen = axisEnd.Length();

            // Project ray origin onto the axis line
            Vector3 oc = origin - basePos;
            float proj = Vector3.Dot(oc, axisDir);
            float dirProj = Vector3.Dot(dir, axisDir);

            // Check distance from ray to axis line
            Vector3 perp = oc - axisDir * proj;
            float perpDist = perp.Length();
            float dirPerp = (dir - axisDir * dirProj).Length();

            if (dirPerp < 0.0001f)
                return perpDist < radius;

            // Simplified: check if the ray passes near the cylinder
            float t = -Vector3.Dot(perp, dir - axisDir * dirProj) / (dirPerp * dirPerp);
            if (t < 0) t = 0;

            Vector3 closest = origin + dir * t;
            Vector3 closestOnAxis = basePos + axisDir * Math.Clamp(Vector3.Dot(closest - basePos, axisDir), 0, axisLen);
            float dist = Vector3.Distance(closest, closestOnAxis);

            return dist < radius * 1.5f;
        }

        private static bool RayVsSphere(Vector3 origin, Vector3 dir, Vector3 center, float radius)
        {
            Vector3 oc = origin - center;
            float a = Vector3.Dot(dir, dir);
            float b = 2f * Vector3.Dot(oc, dir);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - 4 * a * c;
            return disc >= 0;
        }

        private static bool RayVsRing(Vector3 origin, Vector3 dir, Vector3 center, float radius, float thickness, Vector3 axis)
        {
            Vector3 up = Math.Abs(Vector3.Dot(axis, Vector3.UnitY)) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 right = Vector3.Normalize(Vector3.Cross(axis, up));
            Vector3 forward = Vector3.Normalize(Vector3.Cross(axis, right));

            // Check distance from ray to ring plane
            float denom = Vector3.Dot(dir, axis);
            if (MathF.Abs(denom) < 0.0001f) return false;

            float t = Vector3.Dot(center - origin, axis) / denom;
            if (t < 0) return false;

            Vector3 hitPoint = origin + dir * t;
            Vector3 hitOffset = hitPoint - center;

            // Check distance from center on the ring plane
            float ringDist = MathF.Sqrt(
                hitOffset.X * hitOffset.X +
                hitOffset.Y * hitOffset.Y +
                hitOffset.Z * hitOffset.Z -
                Vector3.Dot(hitOffset, axis) * Vector3.Dot(hitOffset, axis));

            return MathF.Abs(ringDist - radius) < thickness;
        }
    }
}
