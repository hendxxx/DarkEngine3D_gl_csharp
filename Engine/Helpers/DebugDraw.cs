using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;
using System.Numerics;
using System.Collections.Generic;

namespace DarkEngine3D_gl_csharp.Engine.Helpers;

/// <summary>
/// Generic debug drawing utilities (line segments, AABB/capsule/sphere wireframes,
/// grids, gizmo markers) rendered with the shared line shader.
/// </summary>
public static unsafe class DebugDraw
{
    // Cached debug buffers (shared, created on first use)
    private static uint debugVao = 0;
    private static uint debugVbo = 0;
    private static readonly Lock debugBufferLock = new();

    // Cached editor grid geometry (built once per size change)
    private static bool _gridBuilt = false;
    private static readonly List<Vector3> GridMinorX = [];
    private static readonly List<Vector3> GridMinorZ = [];
    private static readonly List<Vector3> GridMajorX = [];
    private static readonly List<Vector3> GridMajorZ = [];
    private static readonly List<Vector3> GridAxisX = [];
    private static readonly List<Vector3> GridAxisZ = [];

        /// <summary>
        /// Draw a collection of 3D line segments using the line shader.
        /// Used for BVH mesh visualization and debug wireframes.
        /// </summary>
        public static unsafe void DrawLineSegments(List<Vector3> vertices, Vector3 color, Camera camera)
            => DrawLineSegments(vertices, color, camera, 1f);

        /// <summary>Same as <see cref="DrawLineSegments(List{Vector3}, Vector3, Camera)"/> but with
        /// a transparency factor (0..1). Enables alpha blending while drawing, so translucent
        /// overlays (e.g. editor highlights) blend over the scene.</summary>
        public static unsafe void DrawLineSegments(List<Vector3> vertices, Vector3 color, Camera camera, float alpha)
        {
            if (vertices == null || vertices.Count == 0)
                return;

            float[] lineData = new float[vertices.Count * 3];
            for (int i = 0; i < vertices.Count; i++)
            {
                lineData[i * 3] = vertices[i].X;
                lineData[i * 3 + 1] = vertices[i].Y;
                lineData[i * 3 + 2] = vertices[i].Z;
            }

            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
            }

            uint lineShader = Shader.GetLineShaderProgram();
            GL.UseProgram(lineShader);

            int colorLoc = GL.GetUniformLocation(lineShader, "lineColor");
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);
            int alphaLoc = GL.GetUniformLocation(lineShader, "lineAlpha");
            if (alphaLoc != -1)
                GL.Uniform1f(alphaLoc, Math.Clamp(alpha, 0f, 1f));

            bool blendingEnabled = GL.IsEnabled(Const.GL_BLEND);
            if (alpha < 0.999f)
            {
                GL.Enable(Const.GL_BLEND);
                GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
            }

            int vLoc = GL.GetUniformLocation(lineShader, "view");
            int pLoc = GL.GetUniformLocation(lineShader, "projection");
            int modelLoc = GL.GetUniformLocation(lineShader, "model");

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix();
            Matrix4x4 ident = Matrix4x4.Identity;
            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&proj);
            if (modelLoc != -1)
                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&ident);

            GL.BindVertexArray(debugVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, debugVbo);
            fixed (void* ptr = lineData)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(lineData.Length * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);
            }
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);
            GL.DrawArrays(Const.GL_LINES, 0, vertices.Count);
            GL.BindVertexArray(0);

            if (alpha < 0.999f && !blendingEnabled)
                GL.Disable(Const.GL_BLEND);
        }

        /// <summary>
        /// Draw a wireframe AABB box using the shared line shader + debug VAO/VBO.
        /// Color is a Vector3 (r,g,b) in 0..1 range.
        /// </summary>
        public static unsafe void DrawAABBWireframe(AABB aabb, Vector3 color, Camera camera)
        {
            var c = new Vector3[8]
            {
                new(aabb.Min.X, aabb.Min.Y, aabb.Min.Z),
                new(aabb.Max.X, aabb.Min.Y, aabb.Min.Z),
                new(aabb.Max.X, aabb.Max.Y, aabb.Min.Z),
                new(aabb.Min.X, aabb.Max.Y, aabb.Min.Z),
                new(aabb.Min.X, aabb.Min.Y, aabb.Max.Z),
                new(aabb.Max.X, aabb.Min.Y, aabb.Max.Z),
                new(aabb.Max.X, aabb.Max.Y, aabb.Max.Z),
                new(aabb.Min.X, aabb.Max.Y, aabb.Max.Z)
            };

            float[] lineData =
            [
                c[0].X, c[0].Y, c[0].Z,  c[1].X, c[1].Y, c[1].Z,
                c[1].X, c[1].Y, c[1].Z,  c[2].X, c[2].Y, c[2].Z,
                c[2].X, c[2].Y, c[2].Z,  c[3].X, c[3].Y, c[3].Z,
                c[3].X, c[3].Y, c[3].Z,  c[0].X, c[0].Y, c[0].Z,

                c[4].X, c[4].Y, c[4].Z,  c[5].X, c[5].Y, c[5].Z,
                c[5].X, c[5].Y, c[5].Z,  c[6].X, c[6].Y, c[6].Z,
                c[6].X, c[6].Y, c[6].Z,  c[7].X, c[7].Y, c[7].Z,
                c[7].X, c[7].Y, c[7].Z,  c[4].X, c[4].Y, c[4].Z,

                c[0].X, c[0].Y, c[0].Z,  c[4].X, c[4].Y, c[4].Z,
                c[1].X, c[1].Y, c[1].Z,  c[5].X, c[5].Y, c[5].Z,
                c[2].X, c[2].Y, c[2].Z,  c[6].X, c[6].Y, c[6].Z,
                c[3].X, c[3].Y, c[3].Z,  c[7].X, c[7].Y, c[7].Z,
            ];

            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
            }

            uint lineShader = Shader.GetLineShaderProgram();
            GL.UseProgram(lineShader);

            int colorLoc = GL.GetUniformLocation(lineShader, "lineColor");
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);

            int vLoc = GL.GetUniformLocation(lineShader, "view");
            int pLoc = GL.GetUniformLocation(lineShader, "projection");
            int modelLoc = GL.GetUniformLocation(lineShader, "model");

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix();
            Matrix4x4 ident = Matrix4x4.Identity;
            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&proj);
            if (modelLoc != -1)
                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&ident);

            GL.BindVertexArray(debugVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, debugVbo);
            fixed (void* ptr = lineData)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(lineData.Length * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);
            }
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);
            GL.DrawArrays(Const.GL_LINES, 0, 24);
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Draw a prominent red X mark on a camera-facing billboard.
        /// Used to indicate impostor regions that are in frustum but culled by distance
        /// (too close or too far for impostor rendering).
        /// The X extends 1.5x beyond the billboard radius for clear visibility.
        /// </summary>
        public static unsafe void DrawXMark(Vector3 center, float radius, Vector3 camRight, Vector3 camUp, Camera camera)
        {
            float xr = radius * 1.5f;
            // Two crossing lines forming an X, bigger than the quad
            Vector3 p0 = center - xr * camRight - xr * camUp;
            Vector3 p1 = center + xr * camRight + xr * camUp;
            Vector3 p2 = center - xr * camRight + xr * camUp;
            Vector3 p3 = center + xr * camRight - xr * camUp;

            float[] lineData =
            [
                p0.X, p0.Y, p0.Z,  p1.X, p1.Y, p1.Z,
                p2.X, p2.Y, p2.Z,  p3.X, p3.Y, p3.Z,
            ];

            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
            }

            uint lineShader = Shader.GetLineShaderProgram();
            GL.UseProgram(lineShader);

            // Bright red
            int colorLoc = GL.GetUniformLocation(lineShader, "lineColor");
            GL.Uniform3f(colorLoc, 1.0f, 0.15f, 0.1f);

            int vLoc = GL.GetUniformLocation(lineShader, "view");
            int pLoc = GL.GetUniformLocation(lineShader, "projection");
            int modelLoc = GL.GetUniformLocation(lineShader, "model");

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix();
            Matrix4x4 ident = Matrix4x4.Identity;
            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&proj);
            if (modelLoc != -1)
                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&ident);

            GL.BindVertexArray(debugVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, debugVbo);
            fixed (void* ptr = lineData)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(lineData.Length * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);
            }
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);
            GL.DrawArrays(Const.GL_LINES, 0, 4);
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Draw a camera-facing billboard quad outline using the shared line shader.
        /// Shows that an impostor region is rendered as a single quad (2 triangles).
        /// </summary>
        public static unsafe void DrawBillboardWireframe(Vector3 center, float radius, Vector3 camRight, Vector3 camUp, Vector3 color, Camera camera)
        {
            // 4 corners of the camera-facing billboard:
            // bl = center - radius*camRight - radius*camUp
            // br = center + radius*camRight - radius*camUp
            // tr = center + radius*camRight + radius*camUp
            // tl = center - radius*camRight + radius*camUp
            Vector3 bl = center - radius * camRight - radius * camUp;
            Vector3 br = center + radius * camRight - radius * camUp;
            Vector3 tr = center + radius * camRight + radius * camUp;
            Vector3 tl = center - radius * camRight + radius * camUp;

            // Draw quad outline (4 edges) + diagonal cross (2 lines) for visibility
            float[] lineData =
            [
                // Quad outline (4 edges)
                bl.X, bl.Y, bl.Z,  br.X, br.Y, br.Z,
                br.X, br.Y, br.Z,  tr.X, tr.Y, tr.Z,
                tr.X, tr.Y, tr.Z,  tl.X, tl.Y, tl.Z,
                tl.X, tl.Y, tl.Z,  bl.X, bl.Y, bl.Z,
                // Diagonal cross (X) to make the quad clearly visible
                bl.X, bl.Y, bl.Z,  tr.X, tr.Y, tr.Z,
                tl.X, tl.Y, tl.Z,  br.X, br.Y, br.Z,
            ];

            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
            }

            uint lineShader = Shader.GetLineShaderProgram();
            GL.UseProgram(lineShader);

            int colorLoc = GL.GetUniformLocation(lineShader, "lineColor");
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);

            int vLoc = GL.GetUniformLocation(lineShader, "view");
            int pLoc = GL.GetUniformLocation(lineShader, "projection");
            int modelLoc = GL.GetUniformLocation(lineShader, "model");

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix();
            Matrix4x4 ident = Matrix4x4.Identity;
            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&proj);
            if (modelLoc != -1)
                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&ident);

            GL.BindVertexArray(debugVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, debugVbo);
            fixed (void* ptr = lineData)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(lineData.Length * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);
            }
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);
            GL.DrawArrays(Const.GL_LINES, 0, 12);
            GL.BindVertexArray(0);
        }

        public static void DrawDebugGrid(Camera camera, int halfExtent = 25, float spacing = 1f)
        {
            if (!_gridBuilt)
            {
                GridMinorX.Clear(); GridMinorZ.Clear(); GridMajorX.Clear(); GridMajorZ.Clear();
                GridAxisX.Clear(); GridAxisZ.Clear();

                float extent = halfExtent * spacing;
                for (int i = -halfExtent; i <= halfExtent; i++)
                {
                    if (i == 0) continue; // axis lines drawn separately below
                    float coord = i * spacing;
                    bool major = (i % 5) == 0;

                    // Line parallel to X (varying X, fixed Z)
                    var xLines = major ? GridMajorX : GridMinorX;
                    xLines.Add(new Vector3(-extent, 0f, coord));
                    xLines.Add(new Vector3(extent, 0f, coord));

                    // Line parallel to Z (varying Z, fixed X)
                    var zLines = major ? GridMajorZ : GridMinorZ;
                    zLines.Add(new Vector3(coord, 0f, -extent));
                    zLines.Add(new Vector3(coord, 0f, extent));
                }

                GridAxisX.Add(new Vector3(-extent, 0f, 0f)); GridAxisX.Add(new Vector3(extent, 0f, 0f));
                GridAxisZ.Add(new Vector3(0f, 0f, -extent)); GridAxisZ.Add(new Vector3(0f, 0f, extent));
                _gridBuilt = true;
            }

            // Depth test off so the grid shows through geometry (like the other editor gizmos).
            GL.Disable(Const.GL_DEPTH_TEST);
            if (GridMinorX.Count > 0) DrawLineSegments(GridMinorX, new Vector3(0.22f, 0.22f, 0.27f), camera);
            if (GridMinorZ.Count > 0) DrawLineSegments(GridMinorZ, new Vector3(0.22f, 0.22f, 0.27f), camera);
            if (GridMajorX.Count > 0) DrawLineSegments(GridMajorX, new Vector3(0.45f, 0.45f, 0.5f), camera);
            if (GridMajorZ.Count > 0) DrawLineSegments(GridMajorZ, new Vector3(0.45f, 0.45f, 0.5f), camera);
            DrawLineSegments(GridAxisX, new Vector3(0.9f, 0.2f, 0.2f), camera);
            DrawLineSegments(GridAxisZ, new Vector3(0.2f, 0.4f, 0.9f), camera);
            GL.Enable(Const.GL_DEPTH_TEST);
        }

        /// <summary>
        /// Draw a wireframe standing capsule using the shared line shader.
        /// Capsule defined by foot position, collider radius, and total height.
        /// Draws: waist ring + vertical lines + top/bottom hemisphere arcs.
        /// </summary>
        public static unsafe void DrawCapsuleWireframe(Vector3 position, float radius, float height, Vector3 color, Camera camera)
        {
            float segStartY = position.Y + radius;
            float segEndY   = position.Y + height - radius;
            if (segEndY <= segStartY)
            {
                // Degenerate: just draw a sphere
                DrawSphereWireframe(position, radius, color, camera);
                return;
            }

            const int segments = 16;
            int totalVerts = segments * 2          // waist ring
                           + segments * 2          // top ring
                           + segments * 2          // bottom ring
                           + 8 * 2                 // vertical lines
                           + segments * 4 * 2      // top hemisphere arcs (16 segs × 4 angles × 2 verts)
                           + segments * 4 * 2;     // bottom hemisphere arcs
            float[] lineData = new float[totalVerts * 3];
            int idx = 0;

            // Helper: write a line segment (x1,y1,z1 → x2,y2,z2)
            void Line(Vector3 a, Vector3 b)
            {
                lineData[idx++] = a.X; lineData[idx++] = a.Y; lineData[idx++] = a.Z;
                lineData[idx++] = b.X; lineData[idx++] = b.Y; lineData[idx++] = b.Z;
            }

            // Helper: point on capsule surface at angle θ (around Y) and height offset t (0=bottom, 1=top)
            Vector3 CapsulePoint(float theta, float t)
            {
                float y = segStartY + t * (segEndY - segStartY);
                return new Vector3(position.X + radius * MathF.Cos(theta), y, position.Z + radius * MathF.Sin(theta));
            }

            // ── Waist ring (center of cylinder section) ──
            float midT = 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i + 0) * MathF.PI * 2f / segments;
                float a1 = (i + 1) * MathF.PI * 2f / segments;
                Line(CapsulePoint(a0, midT), CapsulePoint(a1, midT));
            }

            // ── Top ring (at segEndY) ──
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i + 0) * MathF.PI * 2f / segments;
                float a1 = (i + 1) * MathF.PI * 2f / segments;
                Line(CapsulePoint(a0, 1f), CapsulePoint(a1, 1f));
            }

            // ── Bottom ring (at segStartY) ──
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i + 0) * MathF.PI * 2f / segments;
                float a1 = (i + 1) * MathF.PI * 2f / segments;
                Line(CapsulePoint(a0, 0f), CapsulePoint(a1, 0f));
            }

            // ── 8 vertical lines ──
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.PI * 2f / 8;
                Line(CapsulePoint(a, 0f), CapsulePoint(a, 1f));
            }

            // ── Top hemisphere: half-circle arc above segEndY ──
            for (int i = 0; i < segments; i++)
            {
                float t0 = (i + 0) / (float)segments;
                float t1 = (i + 1) / (float)segments;
                float phi0 = t0 * MathF.PI * 0.5f; // 0 → π/2
                float phi1 = t1 * MathF.PI * 0.5f;
                // Sample at 4 angles around for the arc
                for (int k = 0; k < 4; k++)
                {
                    float theta = k * MathF.PI * 2f / 4;
                    float r0 = radius * MathF.Cos(phi0);
                    float y0 = segEndY + radius * MathF.Sin(phi0);
                    float r1 = radius * MathF.Cos(phi1);
                    float y1 = segEndY + radius * MathF.Sin(phi1);
                    Vector3 p0 = new(position.X + r0 * MathF.Cos(theta), y0, position.Z + r0 * MathF.Sin(theta));
                    Vector3 p1 = new(position.X + r1 * MathF.Cos(theta), y1, position.Z + r1 * MathF.Sin(theta));
                    // Only draw if within index buffer bounds
                    if (idx + 5 < lineData.Length) { Line(p0, p1); }
                }
            }

            // ── Bottom hemisphere: half-circle arc below segStartY ──
            for (int i = 0; i < segments; i++)
            {
                float t0 = (i + 0) / (float)segments;
                float t1 = (i + 1) / (float)segments;
                float phi0 = t0 * MathF.PI * 0.5f;
                float phi1 = t1 * MathF.PI * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    float theta = k * MathF.PI * 2f / 4;
                    float r0 = radius * MathF.Cos(phi0);
                    float y0 = segStartY - radius * MathF.Sin(phi0);
                    float r1 = radius * MathF.Cos(phi1);
                    float y1 = segStartY - radius * MathF.Sin(phi1);
                    Vector3 p0 = new(position.X + r0 * MathF.Cos(theta), y0, position.Z + r0 * MathF.Sin(theta));
                    Vector3 p1 = new(position.X + r1 * MathF.Cos(theta), y1, position.Z + r1 * MathF.Sin(theta));
                    if (idx + 5 < lineData.Length) { Line(p0, p1); }
                }
            }

            int actualVerts = idx / 3;
            if (actualVerts < 2) return;

            uint lineShader = Shader.GetLineShaderProgram();
            GL.UseProgram(lineShader);

            int colorLoc = GL.GetUniformLocation(lineShader, "lineColor");
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);

            int vLoc = GL.GetUniformLocation(lineShader, "view");
            int pLoc = GL.GetUniformLocation(lineShader, "projection");
            int modelLoc = GL.GetUniformLocation(lineShader, "model");

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix();
            Matrix4x4 ident = Matrix4x4.Identity;
            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&proj);
            if (modelLoc != -1)
                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&ident);

            // Use a dynamic VAO/VBO approach similar to DrawLine
            uint dVao, dVbo;
            GL.GenVertexArrays(1, &dVao);
            GL.GenBuffers(1, &dVbo);

            GL.BindVertexArray(dVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, dVbo);
            fixed (void* ptr = lineData)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(actualVerts * 3 * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);
            GL.DrawArrays(Const.GL_LINES, 0, actualVerts);
            GL.BindVertexArray(0);

            // Cleanup temp buffers
            GL.DeleteVertexArrays(1, &dVao);
            GL.DeleteBuffers(1, &dVbo);
        }

        /// <summary>
        /// Draw a small crosshair indicator at the given 3D position using the line shader.
        /// Shows a ring + +-shaped cross for marking gizmo pivot override positions.
        /// Color is bright orange-yellow. Radius ~0.4 units. Uses shared debug VAO/VBO.
        /// </summary>
        public static unsafe void DrawGizmoPivotCrosshair(Vector3 position, Camera camera)
        {
            DrawGizmoPivotCrosshairColored(position, camera, new Vector3(1.0f, 0.6f, 0.1f));
        }

        /// <summary>
        /// Draw a small crosshair indicator with a custom color at the given 3D position.
        /// Same visual as DrawGizmoPivotCrosshair but with a caller-specified color.
        /// </summary>
        public static unsafe void DrawGizmoPivotCrosshairColored(Vector3 position, Camera camera, Vector3 color)
        {
            const int segs = 12;
            float radius = 0.4f;
            // Ring (circle in XZ plane): segs * 2 verts
            // +-cross in XZ: 2 lines = 4 verts  
            // X-cross in XZ: 2 lines = 4 verts
            int totalVerts = segs * 2 + 4 + 4;
            float[] lineData = new float[totalVerts * 3];
            int idx = 0;

            void Line(float x1, float y1, float z1, float x2, float y2, float z2)
            {
                lineData[idx++] = x1; lineData[idx++] = y1; lineData[idx++] = z1;
                lineData[idx++] = x2; lineData[idx++] = y2; lineData[idx++] = z2;
            }

            // Ring
            for (int i = 0; i < segs; i++)
            {
                float a0 = (i + 0) * MathF.PI * 2f / segs;
                float a1 = (i + 1) * MathF.PI * 2f / segs;
                Line(
                    position.X + radius * MathF.Cos(a0), position.Y,
                    position.Z + radius * MathF.Sin(a0),
                    position.X + radius * MathF.Cos(a1), position.Y,
                    position.Z + radius * MathF.Sin(a1));
            }

            // +-cross (axis-aligned in XZ)
            float extent = radius * 0.7f;
            // X-axis line
            Line(position.X - extent, position.Y, position.Z,
                 position.X + extent, position.Y, position.Z);
            // Z-axis line
            Line(position.X, position.Y, position.Z - extent,
                 position.X, position.Y, position.Z + extent);

            // X-cross (diagonal in XZ)
            float diag = extent * 0.5f;
            Line(position.X - diag, position.Y, position.Z - diag,
                 position.X + diag, position.Y, position.Z + diag);
            Line(position.X - diag, position.Y, position.Z + diag,
                 position.X + diag, position.Y, position.Z - diag);

            // Use shared debug VAO/VBO
            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
            }

            uint lineShader = Shader.GetLineShaderProgram();
            GL.UseProgram(lineShader);

            int colorLoc = GL.GetUniformLocation(lineShader, "lineColor");
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);

            int vLoc = GL.GetUniformLocation(lineShader, "view");
            int pLoc = GL.GetUniformLocation(lineShader, "projection");
            int modelLoc = GL.GetUniformLocation(lineShader, "model");

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix();
            Matrix4x4 ident = Matrix4x4.Identity;
            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&proj);
            if (modelLoc != -1)
                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&ident);

            GL.BindVertexArray(debugVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, debugVbo);
            fixed (void* ptr = lineData)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(totalVerts * 3 * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);
            GL.DrawArrays(Const.GL_LINES, 0, totalVerts);
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Draw a subtle small dot indicator at the given 3D position using the line shader.
        /// Shows just a simple ring (+ no cross) to mark that an object has a pivot override
        /// but is not currently selected. Color is a muted orange. Radius ~0.25 units.
        /// </summary>
        public static unsafe void DrawPivotDot(Vector3 position, Camera camera)
        {
            const int segs = 8;
            float radius = 0.25f;
            int totalVerts = segs * 2;
            float[] lineData = new float[totalVerts * 3];
            int idx = 0;

            void Line(float x1, float y1, float z1, float x2, float y2, float z2)
            {
                lineData[idx++] = x1; lineData[idx++] = y1; lineData[idx++] = z1;
                lineData[idx++] = x2; lineData[idx++] = y2; lineData[idx++] = z2;
            }

            // Simple ring
            for (int i = 0; i < segs; i++)
            {
                float a0 = (i + 0) * MathF.PI * 2f / segs;
                float a1 = (i + 1) * MathF.PI * 2f / segs;
                Line(
                    position.X + radius * MathF.Cos(a0), position.Y,
                    position.Z + radius * MathF.Sin(a0),
                    position.X + radius * MathF.Cos(a1), position.Y,
                    position.Z + radius * MathF.Sin(a1));
            }

            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
            }

            uint lineShader = Shader.GetLineShaderProgram();
            GL.UseProgram(lineShader);

            // Muted orange — subtle but visible
            int colorLoc = GL.GetUniformLocation(lineShader, "lineColor");
            GL.Uniform3f(colorLoc, 0.7f, 0.4f, 0.05f);

            int vLoc = GL.GetUniformLocation(lineShader, "view");
            int pLoc = GL.GetUniformLocation(lineShader, "projection");
            int modelLoc = GL.GetUniformLocation(lineShader, "model");

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix();
            Matrix4x4 ident = Matrix4x4.Identity;
            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&proj);
            if (modelLoc != -1)
                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&ident);

            GL.BindVertexArray(debugVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, debugVbo);
            fixed (void* ptr = lineData)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(totalVerts * 3 * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);
            GL.DrawArrays(Const.GL_LINES, 0, totalVerts);
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Draw a wireframe sphere using the shared line shader.
        /// Draws 3 rings (XY, XZ, YZ planes) for a clear sphere silhouette.
        /// </summary>
        public static unsafe void DrawSphereWireframe(Vector3 center, float radius, Vector3 color, Camera camera)
        {
            const int segs = 16;
            int totalVerts = segs * 2 * 3; // 3 rings
            float[] lineData = new float[totalVerts * 3];
            int idx = 0;

            void Line(float x1, float y1, float z1, float x2, float y2, float z2)
            {
                lineData[idx++] = x1; lineData[idx++] = y1; lineData[idx++] = z1;
                lineData[idx++] = x2; lineData[idx++] = y2; lineData[idx++] = z2;
            }

            // Ring in XY plane (around Z)
            for (int i = 0; i < segs; i++)
            {
                float a0 = (i + 0) * MathF.PI * 2f / segs;
                float a1 = (i + 1) * MathF.PI * 2f / segs;
                Line(
                    center.X + radius * MathF.Cos(a0), center.Y + radius * MathF.Sin(a0), center.Z,
                    center.X + radius * MathF.Cos(a1), center.Y + radius * MathF.Sin(a1), center.Z);
            }

            // Ring in XZ plane (around Y) — equator
            for (int i = 0; i < segs; i++)
            {
                float a0 = (i + 0) * MathF.PI * 2f / segs;
                float a1 = (i + 1) * MathF.PI * 2f / segs;
                Line(
                    center.X + radius * MathF.Cos(a0), center.Y, center.Z + radius * MathF.Sin(a0),
                    center.X + radius * MathF.Cos(a1), center.Y, center.Z + radius * MathF.Sin(a1));
            }

            // Ring in YZ plane (around X)
            for (int i = 0; i < segs; i++)
            {
                float a0 = (i + 0) * MathF.PI * 2f / segs;
                float a1 = (i + 1) * MathF.PI * 2f / segs;
                Line(
                    center.X, center.Y + radius * MathF.Cos(a0), center.Z + radius * MathF.Sin(a0),
                    center.X, center.Y + radius * MathF.Cos(a1), center.Z + radius * MathF.Sin(a1));
            }

            uint lineShader = Shader.GetLineShaderProgram();
            GL.UseProgram(lineShader);

            int colorLoc = GL.GetUniformLocation(lineShader, "lineColor");
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);

            int vLoc = GL.GetUniformLocation(lineShader, "view");
            int pLoc = GL.GetUniformLocation(lineShader, "projection");
            int modelLoc = GL.GetUniformLocation(lineShader, "model");

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix();
            Matrix4x4 ident = Matrix4x4.Identity;
            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&proj);
            if (modelLoc != -1)
                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&ident);

            uint dVao, dVbo;
            GL.GenVertexArrays(1, &dVao);
            GL.GenBuffers(1, &dVbo);

            GL.BindVertexArray(dVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, dVbo);
            fixed (void* ptr = lineData)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(totalVerts * 3 * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);
            GL.DrawArrays(Const.GL_LINES, 0, totalVerts);
            GL.BindVertexArray(0);

            GL.DeleteVertexArrays(1, &dVao);
            GL.DeleteBuffers(1, &dVbo);
        }

        public static Vector3[] GetFrustumCorners(Matrix4x4 view, Matrix4x4 proj)
        {
            // Kalikan View lalu Projection (Urutan Row-Major .NET)
            Matrix4x4 viewProj = view * proj;
            if (!Matrix4x4.Invert(viewProj, out Matrix4x4 invVP)) return new Vector3[8];

            Vector3[] corners = new Vector3[8];
            // 8 titik sudut NDC (-1 sampai 1)
            Vector3[] value =
            [
                new(-1, -1, -1),
                new(1, -1, -1),
                new(1, 1, -1),
                new(-1, 1, -1), // Near
                new(-1, -1, 1),
                new(1, -1, 1),
                new(1, 1, 1),
                new(-1, 1, 1)  // Far
            ];
            Vector3[] ndc = value;

            for (int i = 0; i < 8; i++)
            {
                Vector4 worldPos = Vector4.Transform(ndc[i], invVP);
                corners[i] = new Vector3(worldPos.X / worldPos.W, worldPos.Y / worldPos.W, worldPos.Z / worldPos.W);
            }
            return corners;
        }

        public static Plane[] ExtractFrustumPlanes(Matrix4x4 m)
        {
            Plane[] planes = new Plane[6];

            // Left
            planes[0] = new Plane(
                m.M14 + m.M11,
                m.M24 + m.M21,
                m.M34 + m.M31,
                m.M44 + m.M41);

            // Right
            planes[1] = new Plane(
                m.M14 - m.M11,
                m.M24 - m.M21,
                m.M34 - m.M31,
                m.M44 - m.M41);

            // Bottom
            planes[2] = new Plane(
                m.M14 + m.M12,
                m.M24 + m.M22,
                m.M34 + m.M32,
                m.M44 + m.M42);

            // Top
            planes[3] = new Plane(
                m.M14 - m.M12,
                m.M24 - m.M22,
                m.M34 - m.M32,
                m.M44 - m.M42);

            // Near (a3 + a2 untuk row-major VP matrix)
            planes[4] = new Plane(
                m.M14 + m.M13,
                m.M24 + m.M23,
                m.M34 + m.M33,
                m.M44 + m.M43);

            // Far
            planes[5] = new Plane(
                m.M14 - m.M13,
                m.M24 - m.M23,
                m.M34 - m.M33,
                m.M44 - m.M43);

            // normalize
            for (int i = 0; i < 6; i++)
                planes[i] = Plane.Normalize(planes[i]);

            return planes;
        }
    /// <summary>Release the shared debug VAO/VBO (call on engine shutdown).</summary>
    public static void Cleanup()
    {
        lock (debugBufferLock)
        {
            if (debugVao != 0)
            {
                fixed (uint* p = &debugVao) GL.DeleteVertexArrays(1, p);
                debugVao = 0;
            }
            if (debugVbo != 0)
            {
                fixed (uint* p = &debugVbo) GL.DeleteBuffers(1, p);
                debugVbo = 0;
            }
        }
    }
}
