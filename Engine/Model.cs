using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine
{
    // A 3D model loaded from an OBJ + (optional) MTL.
    // Each material in the OBJ becomes a sub-mesh with its own VAO/VBO and (optionally) a texture.
    public unsafe class Model : IDisposable
    {
        // One renderable group inside the model.
        private class Group
        {
            public uint Vao, Vbo;
            public int VertexCount;
            public Vector3 Color = new(0.8f, 0.8f, 0.8f); // fallback when no texture
            public Texture? DiffuseTex;                   // null → render with Color via vertex-color path
        }

        public Vector3 Position;
        public float YawDegrees;
        public float Scale;

        private uint _shaderProgram;
        private int _modelLocation;
        private int _useTextureLocation;
        private int _diffuseTexLocation;
        private int _alphaCutoffLocation;

        private readonly List<Group> _groups = new();
        // Cache textures by path so multiple groups sharing the same map_Kd reuse one GL texture.
        private readonly Dictionary<string, Texture> _textureCache = new(StringComparer.OrdinalIgnoreCase);

        public Model(string objPath, Vector3 fallbackColor, Vector3 position, float yawDegrees = 0f, float scale = 1f)
        {
            Position = position;
            YawDegrees = yawDegrees;
            Scale = scale;

            _shaderProgram = Shader.GetShaderProgram();
            _modelLocation       = GL.GetUniformLocation(_shaderProgram, "model");
            _useTextureLocation  = GL.GetUniformLocation(_shaderProgram, "useTexture");
            _diffuseTexLocation  = GL.GetUniformLocation(_shaderProgram, "diffuseTex");
            _alphaCutoffLocation = GL.GetUniformLocation(_shaderProgram, "alphaCutoff");

            ObjMesh mesh = ObjLoader.Load(objPath);

            foreach (MeshGroup mg in mesh.Groups)
            {
                if (mg.Vertices.Count == 0) continue;

                Group g = new() { VertexCount = mg.Vertices.Count };

                // Resolve material → color/texture.
                if (mesh.Materials.TryGetValue(mg.MaterialName, out MaterialDef? mat) && mat != null)
                {
                    g.Color = mat.DiffuseColor;
                    if (!string.IsNullOrEmpty(mat.DiffuseMapPath) && File.Exists(mat.DiffuseMapPath))
                    {
                        if (!_textureCache.TryGetValue(mat.DiffuseMapPath, out Texture? tex))
                        {
                            try { tex = new Texture(mat.DiffuseMapPath); _textureCache[mat.DiffuseMapPath] = tex; }
                            catch (Exception ex) { Console.WriteLine($"  [tex fail] {mat.DiffuseMapPath}: {ex.Message}"); tex = null; }
                        }
                        g.DiffuseTex = tex;
                    }
                }
                else
                {
                    g.Color = fallbackColor;
                }

                // Stamp the group's color into the per-vertex Color so the no-texture path lights correctly.
                Vertex[] verts = new Vertex[mg.Vertices.Count];
                for (int i = 0; i < verts.Length; i++)
                {
                    Vertex v = mg.Vertices[i];
                    v.Color = g.Color;
                    verts[i] = v;
                }

                fixed (uint* p = &g.Vao) GL.GenVertexArrays(1, p);
                fixed (uint* p = &g.Vbo) GL.GenBuffers(1, p);

                GL.BindVertexArray(g.Vao);
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, g.Vbo);
                fixed (void* ptr = verts)
                {
                    GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(verts.Length * sizeof(Vertex)), ptr, Const.GL_STATIC_DRAW);
                }
                int stride = sizeof(Vertex);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)sizeof(Vector3));
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribPointer(2, 3, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 2));
                GL.EnableVertexAttribArray(3);
                GL.VertexAttribPointer(3, 2, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 3));
                GL.BindVertexArray(0);

                _groups.Add(g);
            }

            int textured = 0;
            foreach (var g in _groups) if (g.DiffuseTex != null) textured++;
            Console.WriteLine($"  Model has {_groups.Count} group(s), {textured} textured.");
        }

        public void Draw()
        {
            Matrix4x4 model =
                Matrix4x4.CreateScale(Scale) *
                Matrix4x4.CreateRotationY(YawDegrees * MathF.PI / 180f) *
                Matrix4x4.CreateTranslation(Position);

            GL.UseProgram(_shaderProgram);
            OpenGL.EnableFaceCulling(false);
            GL.UniformMatrix4fv(_modelLocation, 1, false, (float*)&model);
            // Sampler always reads from texture unit 0.
            if (_diffuseTexLocation  >= 0) GL.Uniform1i(_diffuseTexLocation, 0);
            // Alpha cutout threshold (0.5 — kills any pixel whose texture alpha is below half).
            if (_alphaCutoffLocation >= 0) GL.Uniform1f(_alphaCutoffLocation, 0.5f);

            foreach (Group g in _groups)
            {
                bool hasTex = g.DiffuseTex != null;
                if (_useTextureLocation >= 0) GL.Uniform1i(_useTextureLocation, hasTex ? 1 : 0);
                if (hasTex) g.DiffuseTex!.Bind(0);

                GL.BindVertexArray(g.Vao);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, g.VertexCount);
                GL.BindVertexArray(0);
            }

            // Reset useTexture so other renderers using the same shader (terrain, cubes) stay on the vertex-color path.
            if (_useTextureLocation >= 0) GL.Uniform1i(_useTextureLocation, 0);
            // Unbind any texture we left bound.
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            OpenGL.EnableFaceCulling(true);
        }

        public void Dispose()
        {
            foreach (Group g in _groups)
            {
                if (g.Vao != 0) { fixed (uint* p = &g.Vao) GL.DeleteVertexArrays(1, p); g.Vao = 0; }
                if (g.Vbo != 0) { fixed (uint* p = &g.Vbo) GL.DeleteBuffers(1, p); g.Vbo = 0; }
            }
            foreach (var t in _textureCache.Values) t.Dispose();
            _textureCache.Clear();
            _groups.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
