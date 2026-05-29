using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
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
        private  static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }

        private float exposureState = 1.0f;
        private float targetExposure = 1.0f;
        private int locExposureState; 
        private int sunBlockedLoc; 
        private int outHaloRadiusLoc; 
        
        private float totalTime = 0.0f;
        private float SmoothStep(float edge0, float edge1, float x)
        {
            x = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
            return x * x * (3 - 2 * x);
        }

        private float ComputeTargetExposure(Vector3 viewDir, Vector3 sunDir, float tMalam)
        {
            float sunFacing = MathF.Max(Vector3.Dot(viewDir, sunDir), 0.0f);

            float a = Lerp(1.6f, 0.55f, sunFacing);
            float b = Lerp(1.0f, 1.8f, tMalam);
            return a * b;
        }


        public void Draw(Camera camera, Lights lights, float deltaTime, Texture[] skyTextures, TerrainChunk terrain)
        {    
            // === PAKAI SHADER LANGIT ===
            GL.UseProgram(skyShader);

            int weatherModeLoc = GL.GetUniformLocation(skyShader, "weatherMode");
            locExposureState = GL.GetUniformLocation(skyShader, "exposureState");
            sunBlockedLoc = GL.GetUniformLocation(skyShader, "sunBlocked");
            outHaloRadiusLoc = GL.GetUniformLocation(skyShader, "outHaloRadius");

            int aspectLoc = GL.GetUniformLocation(skyShader, "u_aspectRatio");
            float currentAspect = camera.GetAspect();

            // === AUTO EXPOSURE ===
            Vector3 viewDir = camera.Front;
            Vector3 sunDir = lights.SunDir;

            float sunY = sunDir.Y;
            float tMalam = 1.0f - SmoothStep(-0.3f, 0.1f, sunY);


            float targetExposure = ComputeTargetExposure(viewDir, sunDir, tMalam);

            float speed = 2.5f;
            exposureState = Lerp(exposureState, targetExposure, deltaTime * speed);
            exposureState = Math.Clamp(exposureState, 0.6f, 2.0f);

            // === WEATHER ===
            float currentWeatherVal = Keyboard.GetCurrentWeather();
            GL.Uniform1f(weatherModeLoc, currentWeatherVal);

            // === TIME ===
            totalTime += deltaTime;
            GL.Uniform3f(timeLoc, totalTime, 0, 0);

            // === CAMERA ===
            Matrix4x4 view = camera.GetViewMatrix();
            view.M41 = view.M42 = view.M43 = 0;
            view.M14 = view.M24 = view.M34 = 0; view.M44 = 1;

            Matrix4x4 projection = camera.GetProjectionMatrix();

            GL.UniformMatrix4fv(viewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(projLoc, 1, false, (float*)&projection);

            // === LIGHTS ===
            GL.Uniform3f(fogColorLoc, lights.FogColor.X, lights.FogColor.Y, lights.FogColor.Z);
            GL.Uniform3f(sunDirLoc, lights.SunDir.X, lights.SunDir.Y, lights.SunDir.Z);

            // === MOON TEXTURE ===
            GL.ActiveTexture(Const.GL_TEXTURE4);
            GL.BindTexture(Const.GL_TEXTURE_2D, skyTextures[0].ID);
            GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, Const.GL_CLAMP_TO_EDGE);
            GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, Const.GL_CLAMP_TO_EDGE);
            GL.Uniform1i(GL.GetUniformLocation(skyShader, "moonTex"), 4);

            // === ASPECT ===
            GL.Uniform1f(aspectLoc, currentAspect); 
            bool sunBlocked = RaycastSun(0.0948683298f, camera, lights, terrain); 
            GL.Uniform1f(sunBlockedLoc, sunBlocked ? 1.0f : 0.0f);

            // Kirim exposureState
            GL.Uniform1f(locExposureState, exposureState);
 

            // === DRAW SKY ===
            GL.Disable(Const.GL_DEPTH_TEST);
            OpenGL.EnableFaceCulling(true);

            GL.BindVertexArray(vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            OpenGL.EnableFaceCulling(false);
            GL.Enable(Const.GL_DEPTH_TEST);
        }
         

        bool RaycastSun(float haloRadius,Camera cam, Lights lights, TerrainChunk terrain)
        {
            Vector3 origin = cam.Position;
            Vector3 sunDir = Vector3.Normalize(lights.SunDir);

            // radius sudut matahari (harus sama dengan shader)
            float angularRadius = haloRadius;

            // buat basis koordinat untuk offset
            Vector3 up = Math.Abs(sunDir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, sunDir));
            Vector3 sunUp = Vector3.Cross(sunDir, right);

            // 5 arah raycast
            Vector3[] dirs = new Vector3[]
            {
                sunDir, // pusat
                Vector3.Normalize(sunDir + sunUp * angularRadius),     // atas
                Vector3.Normalize(sunDir - sunUp * angularRadius),     // bawah
                Vector3.Normalize(sunDir + right * angularRadius),     // kanan
                Vector3.Normalize(sunDir - right * angularRadius),     // kiri
            };

            // cek semua titik
            foreach (var dir in dirs)
            {
                if (!RaycastSingle(origin, dir, terrain))
                    return false; // masih ada bagian matahari yang terlihat
            }

            return true; // seluruh matahari tertutup terrain
        }

        bool RaycastSingle(Vector3 origin, Vector3 dir, TerrainChunk terrain)
        {
            float maxDistance = 30000f;
            float step = 5f;

            float prevDiff = float.MaxValue;

            for (float d = 0; d < maxDistance; d += step)
            {
                Vector3 p = origin + dir * d;
                float terrainHeight = terrain.GetHeightAt(p.X, p.Z);
                float diff = p.Y - terrainHeight;

                if (diff < 0)
                    return true; // ray menabrak terrain

                if (diff < 0 && prevDiff > 0)
                {
                    float t = prevDiff / (prevDiff - diff);
                    float hitDist = (d - step) + t * step;
                    if (hitDist > 0)
                        return true;
                }

                prevDiff = diff;

                step = Lerp(5f, 50f, d / maxDistance);
            }

            return false;
        }


    }
}
