using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Collections.ObjectModel;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public unsafe class Skybox
    {
        private readonly uint vao, vbo;
        public uint skyShader;
        private readonly int viewLoc, projLoc, fogColorLoc, sunDirLoc, timeLoc;

        private readonly float[] vertices = [
            -1.0f,  1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f, -1.0f, -1.0f,
             1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f, -1.0f,  1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f, -1.0f,  1.0f,
             1.0f, -1.0f, -1.0f,  1.0f, -1.0f,  1.0f,  1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,  1.0f,  1.0f, -1.0f,  1.0f, -1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f, -1.0f,  1.0f,  1.0f,  1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,  1.0f, -1.0f,  1.0f, -1.0f, -1.0f,  1.0f,
            -1.0f,  1.0f, -1.0f,  1.0f,  1.0f, -1.0f,  1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f, -1.0f,  1.0f,  1.0f, -1.0f,  1.0f, -1.0f,
            -1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f, -1.0f,
             1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f,  1.0f
        ];

        public Skybox()
        {
            skyShader = Shader.GetSkyShaderProgram();

            // VAO/VBO Setup
            fixed (uint* pVao = &vao) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &vbo) GL.GenBuffers(1, pVbo);
            
            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            fixed (float* p = vertices) {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (uint)(vertices.Length * sizeof(float)), (void*)p, Const.GL_STATIC_DRAW);
            }
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);

            viewLoc = GL.GetUniformLocation(skyShader, "view");
            projLoc = GL.GetUniformLocation(skyShader, "projection");
            fogColorLoc = GL.GetUniformLocation(skyShader, "fogColor");
            sunDirLoc = GL.GetUniformLocation(skyShader, "sunDir");
            timeLoc = GL.GetUniformLocation(skyShader, "time");


        }

        private float totalTime = 0.0f;
        public void Draw(Camera camera, Lights lights, float deltaTime, Texture[] skyTextures)
        {
            int weatherModeLoc = GL.GetUniformLocation(skyShader, "weatherMode");
            float currentWeatherVal = Keyboard.GetCurrentWeather();
            
            totalTime += deltaTime;
            GL.Disable(Const.GL_DEPTH_TEST); // Draw without depth testing
            GL.Disable(Const.GL_CULL_FACE); 
            GL.UseProgram(skyShader);

            Matrix4x4 view = camera.GetViewMatrix();
            // Remove translation: only rotation remains
            view.M41 = 0; view.M42 = 0; view.M43 = 0;
            view.M14 = 0; view.M24 = 0; view.M34 = 0; view.M44 = 1;

            Matrix4x4 projection = Camera.GetProjectionMatrix(camera.aspect, camera.foV, camera.nearDist, camera.farDist);

            GL.UniformMatrix4fv(viewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(projLoc, 1, false, (float*)&projection);
            GL.Uniform3f(fogColorLoc, lights.FogColor.X, lights.FogColor.Y, lights.FogColor.Z);
            GL.Uniform3f(sunDirLoc, lights.SunDir.X, lights.SunDir.Y, lights.SunDir.Z);
            GL.Uniform3f(timeLoc, totalTime, 0, 0);

            GL.BindVertexArray(vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            // Di loop render langit C# Anda:
            GL.ActiveTexture(Const.GL_TEXTURE4); // Gunakan slot tekstur kosong, misal slot 4
            GL.BindTexture(Const.GL_TEXTURE_2D, skyTextures[0].ID);
            
            // WAJIB: Kunci tekstur agar tidak mengulang (tiling) jika UV jebol
            GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, Const.GL_CLAMP_TO_EDGE);
            GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, Const.GL_CLAMP_TO_EDGE);
             
            GL.Uniform1i(GL.GetUniformLocation(skyShader, "moonTex"), 4);
             
            // Kirimkan nilai berjalan halus (CurrentWeather) dari kalkulasi input class C#
            GL.Uniform1f(weatherModeLoc, currentWeatherVal);
             
            GL.Enable(Const.GL_CULL_FACE);
            GL.Enable(Const.GL_DEPTH_TEST); // Re-enable for terrain

        }
    }
}
