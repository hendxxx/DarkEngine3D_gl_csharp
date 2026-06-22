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
        public static void Loop(Texture[] skyTextures, Camera camera, Lights light, TerrainChunk? gameTerrainChunk, Skybox skybox, HUD hud, ObjectManager objectManager)
        {
            uint shaderProgram = Shader.GetShaderProgram();
            int projectionLocation = GL.GetUniformLocation(shaderProgram, "projection");
            int viewLocation = GL.GetUniformLocation(shaderProgram, "view");

            PostProcessStack ppStack = new PostProcessStack(_windowWidth, _windowHeight);
            //var rainOverlayPass = new RainOverlayPass(Shader.GetRainOverlayShaderProgram());

            var invertPass = new InvertPass(Shader.GetInvertPassShaderProgram());

            //ppStack.AddPass(rainOverlayPass);
            //ppStack.AddPass(invertPass);

            // --- CSM INITIALIZATION ---
            CSM csm = new(Config.ShadowConfig.CascadeSizes[0]);

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

            uint shadowStaticAlphaShader = Shader.GetShadowStaticAlphaShaderProgram();
            int shadowStaticAlphaModelLoc = GL.GetUniformLocation(shadowStaticAlphaShader, "model");
            int shadowStaticAlphaLightSpaceLoc = GL.GetUniformLocation(shadowStaticAlphaShader, "lightSpaceMatrix");

            // --- SOFTWARE OCCLUSION CULLING SETUP ---
            OcclusionCulling occlusionCulling = new OcclusionCulling();

            // Register all animated objects untuk occlusion testing
            if (objectManager != null)
            {
                for (int ai = 0; ai < objectManager.GetObjects().Count; ai++)
                    occlusionCulling.RegisterObject();
            }

            // --- SPAWN 4 RANDOM BIG BOXES FOR OC TESTING ---
            Object3D[] testBoxes = null;
            // World-space AABBs dari test boxes (untuk software OC)
            Helpers.ObjectHelpers.AABB[] testBoxAABBs = null;
            if (gameTerrainChunk != null)
            {
                testBoxes = Object3D.SpawnFourRandomBigBoxes(gameTerrainChunk);
                // Compute world-space AABB untuk setiap test box
                testBoxAABBs = new Helpers.ObjectHelpers.AABB[testBoxes.Length];
                for (int bi = 0; bi < testBoxes.Length; bi++)
                    testBoxAABBs[bi] = testBoxes[bi].GetWorldAABB();
                Console.WriteLine($"[Glfw] Spawned {testBoxes.Length} test boxes for OC testing.");
            }

            FramebufferViewer framebufferViewer = new();
            // Game Loop (Zero-GC)
            Console.WriteLine("Engine Running...");
            float time = 0f;
            int occlusionFrameCount = 0;
            while (glfwWindow(window) == 0)
            {
                deltaTime = Glfw.GetDeltaTime();
                time += deltaTime;

                if (camera.CurrentMode == CameraMode.FirstPerson)
                {
                    camera.freeLook = false;
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

                CameraConfig.TargetShoulderOffset = lastTargetShoulderOffset;

                // 2. Third-person keeps ALT free-look.
                if (!camera.freeLook && objectManager != null)
                    objectManager.PlayerAgent.Heading = camera.Yaw;

                // 3. Update keyboard
                Keyboard.Update(window, light, camera, deltaTime, gameTerrainChunk);

                // 3A. Check if camera mode changed and notify player
                if (camera.CurrentMode != _lastCameraMode && objectManager != null)
                {
                    _lastCameraMode = camera.CurrentMode;
                    objectManager.PlayerAgent.OnCameraModeChanged(camera.CurrentMode);
                }

                if (objectManager != null) { 
                    // 4. Update agents
                    objectManager.Update(deltaTime);

                    // 4A. Update player movement dulu
                    objectManager.PlayerAgent.Move(window, camera, deltaTime, gameTerrainChunk, Vector3.Zero, 0f);

                    // 4B. Update NPC AI + movement
                    objectManager.UpdateAgents(window, deltaTime, gameTerrainChunk, camera); 

                    // 5. Set Camera orbital
                    camera.SetCamera(window, objectManager.PlayerAgent.Position, gameTerrainChunk, deltaTime);

                }
                // 6. Update Light (moved up for CSM lightDir calculations)
                light.Update(deltaTime, camera.Position);
                 
                csm.UpdateMatrices(camera, light.ShadowDirStable);

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

                    GL.UseProgram(shadowStaticAlphaShader);
                    unsafe {
                        GL.UniformMatrix4fv(shadowStaticAlphaLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    if (objectManager != null)
                    {
                        objectManager.RenderShadow(camera, csm, i, shadowSkinnedShader, shadowSkinnedModelLoc, shadowSkinnedJointsLoc, shadowStaticAlphaShader, shadowStaticAlphaModelLoc);

                    }

                    if (gameTerrainChunk != null)
                    {
                        gameTerrainChunk.RenderShadow(camera, csm, i, shadowShader, shadowModelLoc);
                    }
                     

                }

                // Restore default viewport and framebuffer
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                GL.Viewport(0, 0, _windowWidth, _windowHeight);

                // --- SOFTWARE OCCLUSION CULLING ---
                if (OcclusionCulling.Enabled && objectManager != null && testBoxes != null && testBoxAABBs != null)
                {
                    occlusionFrameCount++;

                    // Update occluders (re-register setiap frame karena posisi object tetap)
                    occlusionCulling.ClearOccluders();
                    for (int bi = 0; bi < testBoxAABBs.Length; bi++)
                        occlusionCulling.RegisterOccluder(testBoxAABBs[bi]);

                    // Collect AABBs dari animated objects
                    var animObjs = objectManager.GetObjects();
                    var objectAABBs = new Helpers.ObjectHelpers.AABB[animObjs.Count];
                    for (int oi = 0; oi < animObjs.Count; oi++)
                        objectAABBs[oi] = animObjs[oi].WorldAABB;

                    // Cek visibility via CPU ray-AABB test
                    occlusionCulling.CheckVisibility(camera.Position, objectAABBs);

                    // Apply visibility ke objects (skip player)
                    int visCount = 0, occludedCount = 0;
                    for (int oi = 0; oi < animObjs.Count; oi++)
                    {
                        if (animObjs[oi].IsPlayer)
                        {
                            animObjs[oi].IsVisible = true;
                            continue;
                        }
                        bool vis = occlusionCulling.IsVisible(oi);
                        animObjs[oi].IsVisible = vis;
                        if (vis) visCount++; else occludedCount++;
                    }
                    objectManager.PlayerObject.IsVisible = true;

                    if (occlusionFrameCount == 1 || occludedCount > 0)
                        Console.WriteLine($"[SW OC] Frame {occlusionFrameCount}: {visCount} visible, {occludedCount} occluded (player excluded)");
                }

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
                {
                    // Compute cull freeze planes from frozen VP if active
                    Plane[]? cullFreezePlanes = null;
                    if (Keyboard.GetCullFreezeMode())
                    {
                        var freezeVP = Keyboard.GetCullFreezeViewProj();
                        cullFreezePlanes = TerrainChunk.ExtractFrustumPlanes(freezeVP);
                    }

                    renderedTris = gameTerrainChunk.Render(camera, gameTerrainChunk.GetFrozenPlanes(), cullFreezePlanes);
                }
 



                // ---- Draw OC Test Boxes ----
                if (testBoxes != null)
                {
                    GL.UseProgram(Shader.GetShaderProgram());
                    camera.SetViewAndProjection(viewLocation, projectionLocation);
                    for (int bi = 0; bi < testBoxes.Length; bi++)
                        testBoxes[bi].Draw(deltaTime, window, 0f);
                }

                // ---- glTF Object Manager (autonomous wandering agents) ----
                if (objectManager != null)
                {
                    // Sync cull freeze state from Keyboard
                    objectManager.CullFreezeEnabled = Keyboard.GetCullFreezeMode();
                    objectManager.CullFreezeViewProj = Keyboard.GetCullFreezeViewProj();

                    objectManager.Draw(camera, light, csm);
                    objectManager.DrawHealthBars(camera, hud);   // health bars above heads
                }

                //float currentweatherMode = Keyboard.GetCurrentWeather() > 0.5f ? 1 : 0; // 0 = cerah, 1 = badai
                //uint sceneTexture = ppStack.SceneColorTex;
                //rainManager.UpdateAndDraw(camera, currentweatherMode, time, sceneTexture);

                // 2. jalankan semua postprocess pass
                ppStack.RunStack(_windowWidth, _windowHeight, time);

                // Debug bounding boxes (toggled with P key)
                // Uses frozen frustum (same as chunk box coloring)
                if (Keyboard.GetShowDebug() && objectManager != null)
                {
                    GL.Disable(Const.GL_DEPTH_TEST);
                    objectManager.DrawDebugAABBs(camera, gameTerrainChunk?.GetFrozenPlanes());
                    GL.Enable(Const.GL_DEPTH_TEST);
                }

                // ---- OC Debug: Render AABB wireframe (depth test OFF) ----
                if (OcclusionCulling.Enabled && objectManager != null && occlusionFrameCount > 1)
                {
                    GL.Disable(Const.GL_DEPTH_TEST);
                    var animObjs = objectManager.GetObjects();
                    for (int oi = 0; oi < animObjs.Count; oi++)
                    {
                        var aabb = animObjs[oi].WorldAABB;
                        Vector3 color = animObjs[oi].IsPlayer ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 0f, 0f);
                        TerrainChunk.DrawAABBWireframe(aabb, color, camera);
                    }
                    GL.Enable(Const.GL_DEPTH_TEST);
                }

                //int boxW = 350;
                //int boxH = 350;
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
                string freeze = Keyboard.GetCullFreezeMode() ? " [CULL FREEZE]" : "";
                string title3 = $"🎥 MODE: {camera.CurrentMode}{freeze}";
                string title4 = $"📐 TRIS: {renderedTris:N0} / {totalMapTris:N0}";
                string title5 = $" POS: X ={camera.Position.X:N2} Y={camera.Position.Y:N2} Z={camera.Position.Z:N2}";
                string title6 = "";
                if (objectManager!= null)
                {
                    title6 = $" Objects: {objectManager.DrawnObjects:N0} / {objectManager.TotalObjects:N0}";
                }
                string title7 = "";
                if (OcclusionCulling.Enabled && occlusionFrameCount > 1)
                {
                    title7 = $" OC: {occlusionCulling.VisibleCount} visible / {occlusionCulling.OccludedCount} occluded";
                }

                hud.DrawText(title1, 10, 60, new Vector3(1, 0, 0));
                hud.DrawText(title2, 10, 90, new Vector3(1, 0, 0));
                hud.DrawText(title3, 10, 120, new Vector3(1, 1, 0)); // Yellow to stand out
                hud.DrawText(title4, 10, 150, new Vector3(1, 0, 0));
                hud.DrawText(title5, 10, 180, new Vector3(1, 0, 0));
                hud.DrawText(title6, 10, 210, new Vector3(1, 0, 0));
                if (!string.IsNullOrEmpty(title7))
                    hud.DrawText(title7, 10, 240, new Vector3(0, 1, 1)); // Cyan for OC info

                OpenGL.SwapBuffer(window);
                OpenGL.PollEvents();
            }

            csm.Dispose();
            Console.WriteLine("Engine Shutdown.");
        }
         
    }
}
