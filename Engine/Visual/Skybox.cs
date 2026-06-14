using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
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

        //private float exposureState = 1.0f;
        //private float targetExposure = 1.0f;
        //private int locExposureState; 
        //private int sunBlockedLoc; 
        //private int outHaloRadiusLoc; 
        
        private float totalTime = 0.0f;
        private float exposureState = 0.0f;


        public void Draw(Camera camera, Lights lights, float deltaTime, Texture[] skyTextures, TerrainChunk? terrain)
        {    
            // === PAKAI SHADER LANGIT ===
            GL.UseProgram(skyShader);

            int weatherModeLoc = GL.GetUniformLocation(skyShader, "weatherMode"); 
            int timeLoc = GL.GetUniformLocation(skyShader, "time");


            int aspectLoc = GL.GetUniformLocation(skyShader, "u_aspectRatio");
            float currentAspect = camera.GetAspect();

            //// === AUTO EXPOSURE ===
            //Vector3 viewDir = camera.Front;
            //Vector3 sunDir = lights.SunDir;

            //float sunY = sunDir.Y;
            //float tMalam = 1.0f - Helpers.ShaderHelpers.SmoothStep(-0.3f, 0.1f, sunY);


            //float targetExposure = Helpers.ShaderHelpers.ComputeTargetExposure(viewDir, sunDir, tMalam);

            //float speed = 2.5f;
            //exposureState = Helpers.ShaderHelpers.Lerp(exposureState, targetExposure, deltaTime * speed);
            //exposureState = Math.Clamp(exposureState, 0.6f, 2.0f);

            // === WEATHER ===
            float currentWeatherVal = Keyboard.GetCurrentWeather();
            GL.Uniform1f(weatherModeLoc, currentWeatherVal);

            // === TIME ===
            totalTime += deltaTime;
            GL.Uniform3f(timeLoc, totalTime, 0, 0);

            //float sunY = lights.SunDir.Y;
            //float tMalam = 1.0f - Helpers.ShaderHelpers.SmoothStep(-0.3f, 0.1f, sunY);

            //float targetExposure = Helpers.ShaderHelpers.ComputeTargetExposure(camera.Front, lights.SunDir, tMalam);

            //float speed = 2.5f;
            //exposureState = Helpers.ShaderHelpers.Lerp(exposureState, targetExposure, deltaTime * speed);
            //exposureState = Math.Clamp(exposureState, 0.6f, 2.0f);

            //GL.Uniform1f(exposureLoc, exposureState);

            // === CAMERA ===
            Matrix4x4 view = camera.GetViewMatrix();
            view.M41 = view.M42 = view.M43 = 0;
            view.M14 = view.M24 = view.M34 = 0; view.M44 = 1;

            Matrix4x4 projection = camera.GetProjectionMatrix();

            GL.UniformMatrix4fv(viewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(projLoc, 1, false, (float*)&projection);

            // === LIGHTS ===
            GL.Uniform3f(fogColorLoc, lights.FogColor.X, lights.FogColor.Y, lights.FogColor.Z);
            GL.Uniform3f(sunDirLoc, lights.RealSunDir.X, lights.RealSunDir.Y, lights.RealSunDir.Z);


            // === MOON TEXTURE ===
            GL.ActiveTexture(Const.GL_TEXTURE4);
            GL.BindTexture(Const.GL_TEXTURE_2D, skyTextures[0].ID);
            GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, Const.GL_CLAMP_TO_EDGE);
            GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, Const.GL_CLAMP_TO_EDGE);
            GL.Uniform1i(GL.GetUniformLocation(skyShader, "moonTex"), 4);

            // === ASPECT ===
            GL.Uniform1f(aspectLoc, currentAspect);

            //Ever Exposure
            //bool sunBlocked = Helpers.ShaderHelpers.RaycastSun(0.0948683298f, camera, lights, terrain); 
            //GL.Uniform1f(sunBlockedLoc, sunBlocked ? 1.0f : 0.0f);

            //// Kirim exposureState
            //GL.Uniform1f(locExposureState, exposureState);


            // === DRAW SKY ===
            // sebelum draw sky
            GL.DepthMask(false);                    // jangan tulis depth
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthFunc(Const.GL_LEQUAL);          // sky di belakang semua

            GL.BindVertexArray(vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            GL.DepthMask(true);
            GL.DepthFunc(Const.GL_LESS);
        }
         


    }
}
