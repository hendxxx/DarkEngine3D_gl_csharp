using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace DarkEngine3D_gl_csharp.Engine.Libs
{

    public unsafe class Glfw
    {
        // Delegasi untuk fungsi GLFW
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte glfwInitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint glfwCreateWindowDelegate(int width, int height, string title, nint monitor, nint share);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void glClearColorDelegate(float r, float g, float b, float a);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void glClearDelegate(uint mask);

        private static delegate* unmanaged[Cdecl]<double> glfwGetTime;

        private static delegate* unmanaged[Cdecl]<IntPtr, byte*, void> glfwSetWindowTitle;

        private static delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void> glfwSetWindowSizeCallback;

        static nint window;
        static nint glfwLib;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> glfwWindow;

        static float timer = 0;
        static int frameCount = 0;
        static int lastFPS = 0;
        public static int GetLastFPS() => lastFPS;

        static float deltaTime = 0.0f;
        static float lastFrame = 0.0f;

        private static int _windowWidth;
        private static int _windowHeight;

        public static int WindowWidth
        {
            get => _windowWidth;
            set => _windowWidth = value;
        }

        public static int WindowHeight
        {
            get => _windowHeight;
            set => _windowHeight = value;
        }

        private static Camera? MainCamera;

        public static void Init(string title, bool fullscreen = true)
        {
            glfwLib = NativeLibrary.Load("glfw3.dll");

            // Ambil alamat fungsi (Get Function Pointers)
            var glfwInit = Marshal.GetDelegateForFunctionPointer<glfwInitDelegate>(NativeLibrary.GetExport(glfwLib, "glfwInit"));
            var glfwCreateWindow = Marshal.GetDelegateForFunctionPointer<glfwCreateWindowDelegate>(NativeLibrary.GetExport(glfwLib, "glfwCreateWindow"));
            var glfwMakeContextCurrent = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(glfwLib, "glfwMakeContextCurrent");
            var glfwWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int>)NativeLibrary.GetExport(glfwLib, "glfwWindowShouldClose");
            var glfwSetWindowPos = (delegate* unmanaged[Cdecl]<IntPtr, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowPos");

            glfwSetWindowSizeCallback = (delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowSizeCallback");

            glfwWindow = glfwWindowShouldClose;
            glfwGetTime = (delegate* unmanaged[Cdecl]<double>)NativeLibrary.GetExport(glfwLib, "glfwGetTime");
            glfwSetWindowTitle = (delegate* unmanaged[Cdecl]<IntPtr, byte*, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowTitle");

            // Tambahkan pointer fungsi untuk monitor utama
            var glfwGetPrimaryMonitor = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(glfwLib, "glfwGetPrimaryMonitor");

            // Init window
            if (glfwInit() == 0) return;

            // Atur rasio di sini jika tidak dalam mode fullscreen
            if (!fullscreen)
            {
                window = glfwCreateWindow(_windowWidth, _windowHeight, title, nint.Zero, nint.Zero);
                if (window == nint.Zero) return;

            }
            else
            {
                // Tentukan monitor jika fullscreen, atau nint.Zero jika windowed
                nint monitor = fullscreen ? glfwGetPrimaryMonitor() : nint.Zero;

                window = glfwCreateWindow(_windowWidth, _windowHeight, title, monitor, nint.Zero);
                if (window == nint.Zero) return;
            }
            glfwSetWindowPos(window, 0, 0);

            glfwMakeContextCurrent(window);
            glfwSetWindowSizeCallback(window, &OnWindowResized);

        }
        public static nint GetglfwLib()
        {
            return glfwLib;
        }
        public static nint GetWindow()
        {
            return window;
        }


        public static void SetMainCamera(Camera camera)
        {
            MainCamera = camera;
            MainCamera.UpdateAspectRatio((float)_windowWidth, (float)_windowHeight);
        }
        public static float GetDeltaTime()
        {

            float currentFrame = (float)glfwGetTime();


            deltaTime = currentFrame - lastFrame;
            lastFrame = currentFrame;

            return deltaTime;
        }
        // Fungsi yang akan dipanggil saat resize
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        public static void OnWindowResized(IntPtr window, int width, int height)
        {
            // 1. Update area gambar GPU
            GL.Viewport(0, 0, width, height);

            // 2. Update aspek rasio kamera global jika sudah diinisialisasi
            MainCamera?.UpdateAspectRatio((float)width, (float)height);

            // 3. Update variabel ukuran window global Anda (opsional)
            _windowWidth = width;
            _windowHeight = height;

        }

        public static void ShowFPS(float deltaTime, int renderedTris, int totalMapTris, string gameTime)
        {
            //FPS
            timer += deltaTime;
            frameCount++;

            if (timer >= 1.0f) // Setiap 1 detik
            {
                lastFPS = frameCount;
                // Format HUD Premium: 🕒 [ 06:05 ]  |  ⚡ FPS: 60  |  📐 TRIS: 1.2M / 4.0M
                string title = $"🕒 [ {gameTime} ]    ⚡ FPS: {lastFPS}    📐 TRIS: {renderedTris:N0} / {totalMapTris:N0}";

                fixed (byte* pTitle = Encoding.UTF8.GetBytes(title + "\0"))
                {
                    glfwSetWindowTitle(window, pTitle);
                }
                frameCount = 0;
                timer = 0;
            }
        }

        private static CameraMode _lastCameraMode = CameraMode.FirstPerson;
        private static float lastTargetShoulderOffset;
        public static void Loop(Texture[] skyTextures, Camera camera, Lights light,  TerrainChunk? gameTerrainChunk, Skybox skybox, HUD hud, RainManager rainManager, ObjectManager objectManager)
        {
            uint shaderProgram = Shader.GetShaderProgram();
            int projectionLocation = GL.GetUniformLocation(shaderProgram, "projection");
            int viewLocation = GL.GetUniformLocation(shaderProgram, "view");

            PostProcessStack ppStack = new PostProcessStack(_windowWidth, _windowHeight);
            var rainOverlayPass = new RainOverlayPass(Shader.GetRainOverlayShaderProgram());
            var invertPass = new InvertPass(Shader.GetInvertPassShaderProgram());

            ppStack.AddPass(rainOverlayPass);
            //ppStack.AddPass(invertPass);

            // --- CSM INITIALIZATION ---
            CSM csm = new CSM(4096);

            uint terrainShader = Shader.GetShaderProgram();
            int terrainShadowMap0Loc = GL.GetUniformLocation(terrainShader, "shadowMap0");
            int terrainShadowMap1Loc = GL.GetUniformLocation(terrainShader, "shadowMap1");
            int terrainShadowMap2Loc = GL.GetUniformLocation(terrainShader, "shadowMap2");
            int terrainLightSpaceLoc0 = GL.GetUniformLocation(terrainShader, "lightSpaceMatrices[0]");
            int terrainLightSpaceLoc1 = GL.GetUniformLocation(terrainShader, "lightSpaceMatrices[1]");
            int terrainLightSpaceLoc2 = GL.GetUniformLocation(terrainShader, "lightSpaceMatrices[2]");
            int terrainCascadeEndsLoc0 = GL.GetUniformLocation(terrainShader, "cascadeEnds[0]");
            int terrainCascadeEndsLoc1 = GL.GetUniformLocation(terrainShader, "cascadeEnds[1]");
            int terrainCascadeEndsLoc2 = GL.GetUniformLocation(terrainShader, "cascadeEnds[2]");

            uint gltfShader = GltfShader.GetShaderProgram();
            int gltfShadowMap0Loc = GL.GetUniformLocation(gltfShader, "shadowMap0");
            int gltfShadowMap1Loc = GL.GetUniformLocation(gltfShader, "shadowMap1");
            int gltfShadowMap2Loc = GL.GetUniformLocation(gltfShader, "shadowMap2");
            int gltfLightSpaceLoc0 = GL.GetUniformLocation(gltfShader, "lightSpaceMatrices[0]");
            int gltfLightSpaceLoc1 = GL.GetUniformLocation(gltfShader, "lightSpaceMatrices[1]");
            int gltfLightSpaceLoc2 = GL.GetUniformLocation(gltfShader, "lightSpaceMatrices[2]");
            int gltfCascadeEndsLoc0 = GL.GetUniformLocation(gltfShader, "cascadeEnds[0]");
            int gltfCascadeEndsLoc1 = GL.GetUniformLocation(gltfShader, "cascadeEnds[1]");
            int gltfCascadeEndsLoc2 = GL.GetUniformLocation(gltfShader, "cascadeEnds[2]");

            uint shadowShader = Shader.GetShadowShaderProgram();
            int shadowModelLoc = GL.GetUniformLocation(shadowShader, "model");
            int shadowLightSpaceLoc = GL.GetUniformLocation(shadowShader, "lightSpaceMatrix");

            uint shadowSkinnedShader = Shader.GetShadowSkinnedShaderProgram();
            int shadowSkinnedModelLoc = GL.GetUniformLocation(shadowSkinnedShader, "model");
            int shadowSkinnedLightSpaceLoc = GL.GetUniformLocation(shadowSkinnedShader, "lightSpaceMatrix");
            int shadowSkinnedJointsLoc = GL.GetUniformLocation(shadowSkinnedShader, "u_Joints");

            FramebufferViewer framebufferViewer = new();
            // Game Loop (Zero-GC)
            Console.WriteLine("Engine Running...");
            float time = 0f;
            while (glfwWindow(window) == 0)
            {
                deltaTime = Glfw.GetDeltaTime();
                time += deltaTime;

                if (camera.CurrentMode == CameraMode.FirstPerson)
                {
                    // First Person selalu free look
                    camera.freeLook = true;
                }
                else
                {
                    // Third Person tetap pakai ALT
                    camera.freeLook =
                        (Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT_ALT) ||
                         Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT_ALT));
                }


                // 1. Mouse → yaw/pitch → vectors
                Mouse.Update(window, camera);
                camera.UpdateVectors();

                if (camera.CurrentMode == CameraMode.FirstPerson)
                {
                    lastTargetShoulderOffset = Config.PlayerConfig.TargetShoulderOffset;
                    Config.PlayerConfig.TargetShoulderOffset = 0;
                    camera.ClampFirstPersonHeadYaw(objectManager.PlayerAgent.Heading);

                }
                else
                {
                    Config.PlayerConfig.TargetShoulderOffset = lastTargetShoulderOffset;
                }

               

                // 2. Third-person keeps ALT free-look.
                if (!camera.freeLook)
                    objectManager.PlayerAgent.Heading = camera.Yaw;

                // 3. Update keyboard
                Keyboard.Update(window, light, camera, deltaTime, gameTerrainChunk);

                // 3A. Check if camera mode changed and notify player
                if (camera.CurrentMode != _lastCameraMode)
                {
                    _lastCameraMode = camera.CurrentMode;
                    objectManager.PlayerAgent.OnCameraModeChanged(camera.CurrentMode);
                }

                // 4. Update agents
                objectManager.Update(deltaTime);
                
                // 4A. Update player movement dulu
                objectManager.PlayerAgent.Move(window, camera, deltaTime, gameTerrainChunk, Vector3.Zero, 0f);
                 
                // 4B. Update NPC AI + movement
                objectManager.UpdateAgents(window, deltaTime, gameTerrainChunk, camera);

                // 5. Set Camera orbital
                camera.SetCamera(window, objectManager.PlayerAgent.Position, gameTerrainChunk, deltaTime);

                // 6. Update Light (moved up for CSM lightDir calculations)
                light.Update(deltaTime, camera.Position);

                // --- CSM SHADOW PASS ---
                csm.UpdateMatrices(camera, light.SunDir);

                for (int i = 0; i < CSM.NumCascades; i++)
                {
                    csm.BindFramebuffer(i);

                    Matrix4x4 lightSpace = csm.LightSpaceMatrices[i];
                    
                    GL.UseProgram(shadowShader);
                    unsafe {
                        GL.UniformMatrix4fv(shadowLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    GL.UseProgram(shadowSkinnedShader);
                    unsafe {
                        GL.UniformMatrix4fv(shadowSkinnedLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    if (objectManager != null)
                    {
                        objectManager.RenderShadow(camera, csm, i, shadowSkinnedShader, shadowSkinnedModelLoc, shadowSkinnedJointsLoc);
                    }

                    if (gameTerrainChunk != null)
                    {
                        gameTerrainChunk.RenderShadow(camera, csm, i, shadowShader, shadowModelLoc);
                    }

                }

                // Restore default viewport and framebuffer
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                GL.Viewport(0, 0, _windowWidth, _windowHeight);

                // --- MAIN RENDER PASS ---
                ppStack.BindSceneFBO(); 
                GL.ClearColor(0.07f, 0.13f, 0.17f, 1.0f);
                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
                 

                // 3. Draw Skybox
                skybox.Draw(camera, light, deltaTime, skyTextures, gameTerrainChunk);

                // --- BIND CSM SHADOW MAPS ---
                GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
                GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[0]);

                GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
                GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[1]);

                GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
                GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[2]);

                // Upload shadow uniforms for Terrain
                GL.UseProgram(terrainShader);
                GL.Uniform1i(terrainShadowMap0Loc, 6);
                GL.Uniform1i(terrainShadowMap1Loc, 7);
                GL.Uniform1i(terrainShadowMap2Loc, 8);
                
                unsafe {
                    fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                        GL.UniformMatrix4fv(terrainLightSpaceLoc0, 1, false, p0);
                    fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                        GL.UniformMatrix4fv(terrainLightSpaceLoc1, 1, false, p1);
                    fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                        GL.UniformMatrix4fv(terrainLightSpaceLoc2, 1, false, p2);
                }
                GL.Uniform1f(terrainCascadeEndsLoc0, csm.CascadeEnds[0]);
                GL.Uniform1f(terrainCascadeEndsLoc1, csm.CascadeEnds[1]);
                GL.Uniform1f(terrainCascadeEndsLoc2, csm.CascadeEnds[2]);

                // Upload shadow uniforms for glTF
                GL.UseProgram(gltfShader);
                GL.Uniform1i(gltfShadowMap0Loc, 6);
                GL.Uniform1i(gltfShadowMap1Loc, 7);
                GL.Uniform1i(gltfShadowMap2Loc, 8);
                
                unsafe {
                    fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                        GL.UniformMatrix4fv(gltfLightSpaceLoc0, 1, false, p0);
                    fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                        GL.UniformMatrix4fv(gltfLightSpaceLoc1, 1, false, p1);
                    fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                        GL.UniformMatrix4fv(gltfLightSpaceLoc2, 1, false, p2);
                }
                GL.Uniform1f(gltfCascadeEndsLoc0, csm.CascadeEnds[0]);
                GL.Uniform1f(gltfCascadeEndsLoc1, csm.CascadeEnds[1]);
                GL.Uniform1f(gltfCascadeEndsLoc2, csm.CascadeEnds[2]);

                //// 4. Ensure terrain shader has current view/projection uniforms bound 
                camera.SetViewAndProjection(viewLocation, projectionLocation);

                int renderedTris = 0;
                if (gameTerrainChunk != null)
                    renderedTris = gameTerrainChunk.Render(camera, gameTerrainChunk.GetFrozenPlanes());
 

                // ---- glTF Object Manager (autonomous wandering agents) ----
                if (objectManager != null)
                {
                    objectManager.DrawHealthBars(camera, hud);   // health bars above heads
                    objectManager.Draw(camera, light);
                }

                float currentweatherMode = Keyboard.GetCurrentWeather() > 0.5f ? 1 : 0; // 0 = cerah, 1 = badai
                //rainOverlayPass.RainAmount = Helpers.ShaderHelpers.SmoothStep(0.6f, 1.0f, currentweatherMode);// Helpers.ShaderHelpers.SmoothStep(0.6f, 1.0f, currentweatherMode);
                uint sceneTexture = ppStack.SceneColorTex;
                rainManager.UpdateAndDraw(camera, currentweatherMode, time, sceneTexture);

                // 2. jalankan semua postprocess pass
                ppStack.RunStack(_windowWidth, _windowHeight, time);

                //int boxW = 250;
                //int boxH = 250;
                //int margin = 10;
                //int spacing = 10;

                //int x = _windowWidth - boxW - margin;
                //int y0 = margin;                          // paling bawah
                //int y1 = y0 + boxH + spacing;             // tengah
                //int y2 = y1 + boxH + spacing;             // paling atas

                //framebufferViewer.RenderDepthTexture(
                //    csm.ShadowTextures[0],
                //    _windowWidth,
                //    _windowHeight,
                //    x,
                //    y0,
                //    boxW,
                //    boxH,
                //    0.1f,
                //    csm.CascadeEnds[0],
                //    false
                //);

                //framebufferViewer.RenderDepthTexture(
                //    csm.ShadowTextures[1],
                //    _windowWidth,
                //    _windowHeight,
                //    x,
                //    y1,
                //    boxW,
                //    boxH,
                //    0.1f,
                //    csm.CascadeEnds[1],
                //    false
                //);

                //framebufferViewer.RenderDepthTexture(
                //    csm.ShadowTextures[2],
                //    _windowWidth,
                //    _windowHeight,
                //    x,
                //    y2,
                //    boxW,
                //    boxH,
                //    0.1f,
                //    csm.CascadeEnds[2],
                //    false
                //);


                // --- HUD SYSTEM ---
                int totalMapTris = TerrainChunk.GetTotalMapTriangles();
                string gTime = light.GetFormattedTime();
                Glfw.ShowFPS(deltaTime, renderedTris, totalMapTris, gTime);

                string title1 = $"🕒 [ {gTime} ]";
                string title2 = $"⚡ FPS: {lastFPS}";
                string title3 = $"🎥 MODE: {camera.CurrentMode}";
                string title4 = $"📐 TRIS: {renderedTris:N0} / {totalMapTris:N0}";
                string title5 = $" POS: X ={camera.Position.X:N2} Y={camera.Position.Y:N2} Z={camera.Position.Z:N2}";
                string title6 = $" Objects: {objectManager.DrawnObjects:N0} / {objectManager.TotalObjects:N0}";

                hud.DrawText(title1, 10, 60, new Vector3(1, 0, 0));
                hud.DrawText(title2, 10, 90, new Vector3(1, 0, 0));
                hud.DrawText(title3, 10, 120, new Vector3(1, 1, 0)); // Yellow to stand out
                hud.DrawText(title4, 10, 150, new Vector3(1, 0, 0));
                hud.DrawText(title5, 10, 180, new Vector3(1, 0, 0));
                hud.DrawText(title6, 10, 210, new Vector3(1, 0, 0));

                OpenGL.SwapBuffer(window);
                OpenGL.PollEvents();
            }

            csm.Dispose();
            Console.WriteLine("Engine Shutdown.");
        }
         
    }
}
