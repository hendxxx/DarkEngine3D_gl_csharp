using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Inputs
{
    public unsafe class Keyboard
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetKey;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, void> glfwSetWindowShouldClose;
        static bool isWireframe = false;
        static bool f1Pressed = false;
        static float speedCam = 1.0f;
        static int lineVLoc = 0;
        static int linePLoc = 0;
        static uint lineShaderProgram;
        static Vector3[]? frozenCorners = null;

        // Debug visualization mode (cycled with N key)
        // 0=Off, 1=AABB, 2=All
        static int debugMode = 0;
        static bool nPressed = false;
        public static int GetDebugMode() => debugMode;
        public static bool GetShowBBox() => debugMode >= 1;
        public static bool GetIsWireframe() => isWireframe;

        // P: toggle frozen frustum debug visualization (freeze 8 corners at capture time)
        static bool _showFrustumDebug = false;
        static bool pPressed = false;
        public static bool GetShowFrustumDebug() => _showFrustumDebug;
        public static Vector3[]? GetFrozenCorners() => frozenCorners;

        // Shift+P: freeze culling — objects outside the frozen frustum stay hidden while camera moves
        static int prevShiftPState = 0;
        static bool cullFreezeMode = false;
        static Matrix4x4 cullFreezeViewProj;
        public static bool GetCullFreezeMode() => cullFreezeMode;
        public static Matrix4x4 GetCullFreezeViewProj() => cullFreezeViewProj;

        // ← TAMBAHKAN: Travel time measurement system
        static Stopwatch? travelStopwatch = null;
        static Vector3? travelStartPos = new Vector3();
        static Vector3? travelTargetPos = null;
        static bool isMeasuringTravel = false;
        static int travelMeasureKey = 0;  // Menyimpan key mana yang di-press

        // Tambahkan ini di bagian atas class (Static Fields)
        static float TargetWeather = 0.001f;  // Cuaca tujuan awal (default: sedikit mendung)
        static float CurrentWeather = 0.001f; // Nilai cuaca aktif yang sedang merayap halus

        // Variabel kontrol untuk toggle HLOD visualization
        static bool showHLOD = false;
        static bool kPressed = false;
        public static bool GetShowHLOD() => showHLOD;

        // M toggle: billboard atlas debug overlay
        static bool showBillboardAtlas = false;
        static bool mPressed = false;
        public static bool GetShowBillboardAtlas() => showBillboardAtlas;

        // Variabel kontrol untuk toggle impostor visualization (0=off, 1=billboard, 2=aabb)
        static int showImpostorMode = 0;
        static bool jPressed = false;
        public static bool GetShowImpostor() => showImpostorMode != 0;
        public static int GetImpostorDebugMode() => showImpostorMode;

        // Variabel kontrol untuk toggle kabut
        static bool isFogActive = true;
        static int shadowFilterMode = 0;
        // 0=PCF 16, 1=Hard, 2=PCF 16, 3=PCF 16 soft, 4=PCF 32, 5=PCF 32 Soft,
        // 6=PCSS 16, 7=PCSS 16 Soft, 8=PCSS 32, 9=PCSS 32 Soft
        static bool fPressed = false;

        // Variabel kontrol untuk toggle LOD color
        static bool showLODColor = false;
        static bool showCSMCascadeColor = false;
        static bool lPressed = false;
        static bool oPressed = false;

        // Variabel kontrol untuk toggle camera mode 
        static bool commaPressed = false;
        static bool periodPressed = false;



        private static Dictionary<int, bool> lastKeyState = new Dictionary<int, bool>();

        // Properti public jika Anda ingin membaca status ini saat pengiriman uniform di render loop
        public static bool IsFogActive
        { 
            get { return isFogActive; }
            set { isFogActive = value; }
        }
        public static bool GetshowCSMCascadeColor()
        {
            return showCSMCascadeColor;
        }

        /// <summary>Set the CSM cascade debug overlay (L key) directly. The IDE Shadow
        /// panel uses this so its checkbox stays in sync with the keyboard toggle.</summary>
        public static void SetShowCSMCascadeColor(bool value)
        {
            showCSMCascadeColor = value;
        }


        public static bool GetShowLODColor()
        {
            return showLODColor;
        }
        public static bool GetIsFogActive()
        {
            // The master switch lives in Config.FogSettings (edited from the Inspector
            // "Fog" section); the F-key quick-toggle below flips it. The static flag is
            // kept only as a mirror for the keyboard toggle history.
            return Config.FogSettings.Enabled;
        }

        public static float GetCurrentWeather()
        {
            return CurrentWeather;
        }

        public static int GetIsHardShadow()
        {
            return shadowFilterMode;
        }

        /// <summary>Set the shadow filter mode directly (0-9). The IDE Shadow Settings panel
        /// uses this instead of cycling with the H key.</summary>
        public static void SetShadowFilterMode(int mode)
        {
            shadowFilterMode = Math.Clamp(mode, 0, 9);
        }

        public static unsafe void Init(nint glfwLib)
        {
            lineShaderProgram = Shader.GetLineShaderProgram();
            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");
            glfwSetWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowShouldClose");

            isWireframe = false;
            f1Pressed = false;
             
            lineVLoc = GL.GetUniformLocation(lineShaderProgram, "view");
            linePLoc = GL.GetUniformLocation(lineShaderProgram, "projection");
        }
        public static bool IsKeyPressed(nint window, int key)
        {
            bool isDown = IsKeyDown(window, key);

            if (!lastKeyState.ContainsKey(key))
                lastKeyState[key] = false;

            bool wasDown = lastKeyState[key];

            lastKeyState[key] = isDown;

            return isDown && !wasDown; // true hanya 1 frame
        }

        public static bool IsKeyDown(nint window, int key)
        {
            return glfwGetKey(window, key) == Const.GLFW_PRESS;
        }

        public static unsafe void Update(nint window,Lights lights, Camera camera, float deltaTime, TerrainChunk? gameTerrainChunk)
        { 
            //// Tombol ESC untuk Keluar
            //if (glfwGetKey(window, Const.GLFW_KEY_ESCAPE) == Const.GLFW_PRESS)
            //{
            //    // Beritahu GLFW untuk menutup jendela
            //    glfwSetWindowShouldClose(window, 1);
            //}

            // F1 toggle wireframe (rising edge)
            int f1State = glfwGetKey(window, Const.GLFW_KEY_F1);
            if (f1State == Const.GLFW_PRESS)
            {
                if (!f1Pressed)
                {
                    isWireframe = !isWireframe;
                    GL.PolygonMode(Const.GL_FRONT_AND_BACK, isWireframe ? Const.GL_LINE : Const.GL_FILL);
                    f1Pressed = true;
                    Console.WriteLine(isWireframe ? "Wireframe Mode: ON" : "Wireframe Mode: OFF");
                }
            }
            else
            {
                f1Pressed = false;
            }

            // =========================================================================
            // F TOGGLE FOG (RISING EDGE)
            // =========================================================================
            int fState = glfwGetKey(window, Const.GLFW_KEY_F);
            if (fState == Const.GLFW_PRESS)
            {
                if (!fPressed)
                {
                    isFogActive = !isFogActive;
                    Config.FogSettings.Enabled = isFogActive;
                    fPressed = true;
                    Console.WriteLine(isFogActive ? "Fog: ON" : "Fog: OFF");
                }
            }
            else
            {
                fPressed = false;
            }

             
            // H = toggle shadow filter mode (0 → 1 → 2 → 0 ...)
            if (IsKeyPressed(window, Const.GLFW_KEY_H))
            {
                shadowFilterMode++;
                if (shadowFilterMode > 9)
                    shadowFilterMode = 0;

                string modeName = shadowFilterMode switch
                {
                    0 => "PCF 16",
                    1 => "Hard Shadow",
                    2 => "PCF 16",
                    3 => "PCF 16 soft",
                    4 => "PCF 32",
                    5 => "PCF 32 Soft",
                    6 => "PCSS 16",
                    7 => "PCSS 16 Soft",
                    8 => "PCSS 32",
                    9 => "PCSS 32 Soft",
                    _ => "Unknown"
                };

                Console.WriteLine($"Shadow Filter Mode: {shadowFilterMode} ({modeName})");
            }

            // =========================================================================
            // O TOGGLE LOD COLOR (RISING EDGE)
            // =========================================================================
            int oState = glfwGetKey(window, Const.GLFW_KEY_O);
            if (oState == Const.GLFW_PRESS)
            {
                if (!oPressed)
                {
                    showLODColor = !showLODColor;
                    oPressed = true;
                    Console.WriteLine(showLODColor ? "LOD Color: ON" : "LOD Color: OFF");
                }
            }
            else
            {
                oPressed = false;
            }

            // =========================================================================
            // J TOGGLE IMPOSTOR VISUALIZATION (3-state: OFF → Billboard → AABB → OFF)
            // =========================================================================
            int jState = glfwGetKey(window, Const.GLFW_KEY_J);
            if (jState == Const.GLFW_PRESS)
            {
                if (!jPressed)
                {
                    showImpostorMode = (showImpostorMode + 1) % 3;
                    jPressed = true;
                    string modeName = showImpostorMode switch
                    {
                        1 => "Impostor Billboard: ON",
                        2 => "Impostor AABB: ON",
                        _ => "Impostor Visualization: OFF"
                    };
                    Console.WriteLine(modeName);
                }
            }
            else
            {
                jPressed = false;
            }

            // =========================================================================
            // K TOGGLE HLOD VISUALIZATION (RISING EDGE)
            // =========================================================================
            int kState = glfwGetKey(window, Const.GLFW_KEY_K);
            if (kState == Const.GLFW_PRESS)
            {
                if (!kPressed)
                {
                    showHLOD = !showHLOD;
                    kPressed = true;
                    Console.WriteLine(showHLOD ? "HLOD Visualization: ON" : "HLOD Visualization: OFF");
                }
            }
            else
            {
                kPressed = false;
            }

            // =========================================================================
            // L TOGGLE CSM LOD COLOR (RISING EDGE)
            // =========================================================================
            int lState = glfwGetKey(window, Const.GLFW_KEY_L);
            if (lState == Const.GLFW_PRESS)
            {
                if (!lPressed)
                {
                    showCSMCascadeColor = !showCSMCascadeColor;
                    lPressed = true;
                    Console.WriteLine(showCSMCascadeColor ? "CSM LOD Color: ON" : "CSM LOD Color: OFF");
                }
            }
            else
            {
                lPressed = false;
            }



            // =========================================================================
            // M TOGGLE BILLBOARD ATLAS DEBUG (RISING EDGE)
            // =========================================================================
            int mState = glfwGetKey(window, Const.GLFW_KEY_M);
            if (mState == Const.GLFW_PRESS)
            {
                if (!mPressed)
                {
                    showBillboardAtlas = !showBillboardAtlas;
                    mPressed = true;
                    Console.WriteLine(showBillboardAtlas ? "Billboard Atlas Debug: ON" : "Billboard Atlas Debug: OFF");
                }
            }
            else
            {
                mPressed = false;
            }

            // =========================================================================
            // < AND > TOGGLE CAMERA MODE
            // =========================================================================
            int commaState = glfwGetKey(window, Const.GLFW_KEY_COMMA); // COMMA <
            if (commaState == Const.GLFW_PRESS)
            {
                if (!commaPressed)
                {
                    camera.ToggleCameraMode(-1);
                    commaPressed = true;
                }
            }
            else commaPressed = false;

            int periodState = glfwGetKey(window, Const.GLFW_KEY_PERIOD); // PERIOD >
            if (periodState == Const.GLFW_PRESS)
            {
                if (!periodPressed)
                {
                    camera.ToggleCameraMode(1);
                    periodPressed = true;
                }
            }
            else periodPressed = false;

            //// Movement
            //bool shiftPressed = glfwGetKey(window, Const.GLFW_KEY_LEFT_SHIFT) == Const.GLFW_PRESS
            //                    || glfwGetKey(window, Const.GLFW_KEY_RIGHT_SHIFT) == Const.GLFW_PRESS;

            //// ← UBAH: Scale camera speed dengan TerrainScale
            ////float scaledSpeed = speedCam * TerrainChunk.TerrainScale * deltaTime * (shiftPressed ? Const.SHIFT_SPEED_MULTIPLIER : 1.0f);
            //float scaledSpeed = (speedCam / TerrainChunk.TerrainScale) * deltaTime * (shiftPressed ? Const.SHIFT_SPEED_MULTIPLIER : 1.0f);
            
            //Vector3 flatForward = Vector3.Normalize(new Vector3(camera.Front.X, 0f, camera.Front.Z));

            //if (glfwGetKey(window, Const.GLFW_KEY_W) == Const.GLFW_PRESS)
            //    camera.Position += flatForward * scaledSpeed;

            //if (glfwGetKey(window, Const.GLFW_KEY_S) == Const.GLFW_PRESS)
            //    camera.Position -= flatForward * scaledSpeed;

            //Vector3 flatRight = Vector3.Normalize(Vector3.Cross(flatForward, Vector3.UnitY));

            //if (glfwGetKey(window, Const.GLFW_KEY_A) == Const.GLFW_PRESS)
            //    camera.Position -= flatRight * scaledSpeed;

            //if (glfwGetKey(window, Const.GLFW_KEY_D) == Const.GLFW_PRESS)
            //    camera.Position += flatRight * scaledSpeed;

            //camera.Pitch = Math.Clamp(camera.Pitch, -85f, 85f);


            // ← TAMBAHKAN: Travel measurement (tekan T untuk start, atau lagi untuk stop)
            //MeasureTravelTime(window, camera);
             
            // N: cycle debug visualization mode
            // 0=Off → 1=AABB → 2=All → 0
            int nState = glfwGetKey(window, Const.GLFW_KEY_N);
            if (nState == Const.GLFW_PRESS)
            {
                if (!nPressed)
                {
                    debugMode = (debugMode + 1) % 3;
                    nPressed = true;
                    string modeName = debugMode switch
                    {
                        1 => "Debug: AABB",
                        2 => "Debug: All",
                        _ => "Debug: OFF"
                    };
                    Console.WriteLine(modeName);
                }
            }
            else
            {
                nPressed = false;
            }

            // Shift+P: freeze culling — also freeze frustum corners for visualization
            int pState = glfwGetKey(window, Const.GLFW_KEY_P);
            bool shiftHeld = glfwGetKey(window, Const.GLFW_KEY_LEFT_SHIFT) == Const.GLFW_PRESS ||
                             glfwGetKey(window, Const.GLFW_KEY_RIGHT_SHIFT) == Const.GLFW_PRESS;
            if (pState == Const.GLFW_PRESS && prevShiftPState != Const.GLFW_PRESS && shiftHeld)
            {
                cullFreezeMode = !cullFreezeMode;
                if (cullFreezeMode)
                {
                    Matrix4x4 view = camera.GetViewMatrix();
                    Matrix4x4 proj = camera.GetProjectionMatrix();
                    cullFreezeViewProj = view * proj;

                    // Also freeze frustum corners for visualization
                    if (Matrix4x4.Invert(cullFreezeViewProj, out Matrix4x4 invVp))
                    {
                        frozenCorners = new Vector3[8];
                        for (int i = 0; i < 8; i++)
                        {
                            float x = (i & 1) != 0 ? 1f : -1f;
                            float y = (i & 2) != 0 ? 1f : -1f;
                            float z = (i & 4) != 0 ? 1f : -1f;
                            Vector4 ndc = new(x, y, z, 1f);
                            Vector4 world = Vector4.Transform(ndc, invVp);
                            world /= world.W;
                            frozenCorners[i] = new Vector3(world.X, world.Y, world.Z);
                        }
                    }

                    camera.FlyMode = true; // auto freefly
                    Console.WriteLine("Cull Freeze: ON — objects outside frozen frustum will stay hidden");
                }
                else
                {
                    frozenCorners = null;
                    Console.WriteLine("Cull Freeze: OFF");
                }
            }
            prevShiftPState = pState;

            // P: toggle frozen frustum debug visualization (only when shift is NOT held)
            if (pState == Const.GLFW_PRESS && !pPressed && !shiftHeld)
            {
                _showFrustumDebug = !_showFrustumDebug;
                if (_showFrustumDebug)
                {
                    // Compute & freeze 8 frustum corners at current camera position
                    Matrix4x4 view = camera.GetViewMatrix();
                    Matrix4x4 proj = camera.GetProjectionMatrix();
                    Matrix4x4 vp = view * proj;
                    if (Matrix4x4.Invert(vp, out Matrix4x4 invVp))
                    {
                        frozenCorners = new Vector3[8];
                        for (int i = 0; i < 8; i++)
                        {
                            float x = (i & 1) != 0 ? 1f : -1f;
                            float y = (i & 2) != 0 ? 1f : -1f;
                            float z = (i & 4) != 0 ? 1f : -1f;
                            Vector4 ndc = new(x, y, z, 1f);
                            Vector4 world = Vector4.Transform(ndc, invVp);
                            world /= world.W;
                            frozenCorners[i] = new Vector3(world.X, world.Y, world.Z);
                        }
                    }
                    camera.FlyMode = true; // auto freefly
                    Console.WriteLine("Frustum Frozen: ON — fly around to see the frozen frustum");
                }
                else
                {
                    frozenCorners = null;
                    Console.WriteLine("Frustum Frozen: OFF");
                }
                pPressed = true;
            }
            else if (pState != Const.GLFW_PRESS)
            {
                pPressed = false;
            }



            // =========================================================================
            // SISTEM INPUT & TRANSISI HALUS INTERAKTIF CUACA 1, 2, 3 (GLFW CORE PROFILE)
            // =========================================================================
            // 1. Deteksi input angka 1, 2, 3 dari Keyboard atas maupun Numpad kanan
            if (glfwGetKey(window, Const.GLFW_KEY_1) == Const.GLFW_PRESS || glfwGetKey(window, Const.GLFW_KEY_KP_1) == Const.GLFW_PRESS)
            {
                TargetWeather = 0.0f; // Tekan 1 -> Awan Cerah Sedikit Sekali
            }
            else if (glfwGetKey(window, Const.GLFW_KEY_2) == Const.GLFW_PRESS || glfwGetKey(window, Const.GLFW_KEY_KP_2) == Const.GLFW_PRESS)
            {
                TargetWeather = 0.35f; // Tekan 2 -> Sedikit Mendung (Estetik)
            }
            else if (glfwGetKey(window, Const.GLFW_KEY_3) == Const.GLFW_PRESS || glfwGetKey(window, Const.GLFW_KEY_KP_3) == Const.GLFW_PRESS)
            {
                TargetWeather = 0.7f; // Tekan 3 -> Mendung Tebal Sekali
            }

            // 2. Kalkulasi Pergerakan Lerp (Awan membesar/menyusut pelan, tidak kaku melompat)
            // Angka 1.5f mengontrol kecepatan transisi gumpalan awan (Bisa dinaikkan jika ingin lebih kilat)
            // =========================================================================
            // SINKRONISASI LOGIKA INTERPOLASI LINIER (LERP MANUAL CORES PROFILE)
            // =========================================================================
            // Rumus Lerp Manual: A + (B - A) * t
            // Ini menggantikan MathHelper.Lerp secara total agar bebas dari error namespace
            float transitionSpeed = 1.5f;
            CurrentWeather += (TargetWeather - CurrentWeather) * (transitionSpeed * deltaTime);

            // Batasi nilai agar tetap berada di range 0.0f sampai 1.0f murni
            CurrentWeather = System.Math.Clamp(CurrentWeather, 0.0f, 1.0f);


            // Manual Override / Fast Forward
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_EQUAL))
                lights.WorldTime += deltaTime * lights.baseSpeed * lights.manualMultiplier;
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_MINUS))
                lights.WorldTime -= deltaTime * lights.baseSpeed * lights.manualMultiplier;
             
        } 

        ///// <summary>
        ///// Measure travel time between two positions
        ///// Press T to start, move camera, press T again to stop
        ///// </summary>
        //private static void MeasureTravelTime(nint window, Camera camera)
        //{
        //    int tState = glfwGetKey(window, Const.GLFW_KEY_T);
            
        //    if (tState == Const.GLFW_PRESS)
        //    {
        //        if (travelMeasureKey != Const.GLFW_PRESS) // Rising edge
        //        {
        //            if (!isMeasuringTravel)
        //            {
        //                // START measurement
        //                travelStartPos = camera.Position;
        //                travelStopwatch = Stopwatch.StartNew();  // ← Lebih clean
        //                isMeasuringTravel = true;

        //                Console.WriteLine("═════════════════════════════════════════");
        //                Console.WriteLine("🚀 TRAVEL TIME MEASUREMENT STARTED");
        //                Console.WriteLine($"   Start Position: ({travelStartPos?.X:F2}, {travelStartPos?.Y:F2}, {travelStartPos?.Z:F2})");
        //                Console.WriteLine($"   Terrain Scale: {TerrainChunk.TerrainScale}");
        //                Console.WriteLine($"   Camera Speed: {speedCam} units/sec");
        //                Console.WriteLine("   Press T again to STOP measurement");
        //                Console.WriteLine("═════════════════════════════════════════");
        //            }
        //            else
        //            {
        //                // STOP measurement
        //                travelTargetPos = camera.Position;
        //                travelStopwatch?.Stop();
        //                double elapsedSeconds = travelStopwatch?.Elapsed.TotalSeconds ?? 0;

        //                if (travelStartPos.HasValue && travelTargetPos.HasValue)
        //                {
        //                    Vector3 delta = travelTargetPos.Value - travelStartPos.Value;
        //                    float distance = delta.Length();

        //                    // Calculate actual speed traveled
        //                    float actualSpeed = (float)(distance / elapsedSeconds);
        //                    float expectedSpeed = speedCam;
        //                    float speedRatio = actualSpeed / expectedSpeed;

        //                    //PrintTravelMetrics(travelStartPos.Value, travelTargetPos.Value, elapsedSeconds, distance, actualSpeed, speedRatio);
        //                }

        //                isMeasuringTravel = false;
        //                travelStartPos = null;
        //                travelTargetPos = null;
        //                travelStopwatch = null;
        //            }
        //        }
        //        travelMeasureKey = Const.GLFW_PRESS;
        //    }
        //    else
        //    {
        //        travelMeasureKey = Const.GLFW_RELEASE;
        //    }
        //}

        //private static void PrintTravelMetrics(Vector3 start, Vector3 end, double elapsedSeconds, float distance, float actualSpeed, float speedRatio)
        //{
        //    Console.WriteLine();
        //    Console.WriteLine("═════════════════════════════════════════");
        //    Console.WriteLine("📊 TRAVEL TIME MEASUREMENT RESULTS");
        //    Console.WriteLine("═════════════════════════════════════════");
        //    Console.WriteLine($"Start Position:      ({start.X:F2}, {start.Y:F2}, {start.Z:F2})");
        //    Console.WriteLine($"End Position:        ({end.X:F2}, {end.Y:F2}, {end.Z:F2})");
        //    Console.WriteLine();
        //    Console.WriteLine($"Distance Traveled:   {distance:F2} world units");
        //    Console.WriteLine($"Time Elapsed:        {elapsedSeconds:F2} seconds");
        //    Console.WriteLine();
        //    Console.WriteLine($"Expected Speed:      {speedCam:F2} units/sec");
        //    Console.WriteLine($"Actual Speed:        {actualSpeed:F2} units/sec");
        //    Console.WriteLine($"Speed Ratio:         {speedRatio:F4} (should be ~1.0)");
        //    Console.WriteLine();
        //    Console.WriteLine($"Terrain Scale:       {TerrainChunk.TerrainScale}");
            
        //    // ← TAMBAHKAN: Calculate expected travel time
        //    float expectedDistance = distance / TerrainChunk.TerrainScale;
        //    Console.WriteLine($"Normalized Distance: {expectedDistance:F2} pixels (distance ÷ TerrainScale)");
        //    Console.WriteLine("═════════════════════════════════════════");
        //    Console.WriteLine();

        //    // Save to file
        //    try
        //    {
        //        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "travel_metrics.txt");
        //        var lines = new System.Text.StringBuilder();
        //        lines.AppendLine($"═════════════════════════════════════════");
        //        lines.AppendLine($"Travel Measurement - TerrainScale: {TerrainChunk.TerrainScale}");
        //        lines.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        //        lines.AppendLine($"═════════════════════════════════════════");
        //        lines.AppendLine($"Start: ({start.X:F2}, {start.Y:F2}, {start.Z:F2})");
        //        lines.AppendLine($"End: ({end.X:F2}, {end.Y:F2}, {end.Z:F2})");
        //        lines.AppendLine($"Distance: {distance:F2} units");
        //        lines.AppendLine($"Normalized Distance (÷TerrainScale): {expectedDistance:F2} pixels");
        //        lines.AppendLine($"Time: {elapsedSeconds:F2} seconds");
        //        lines.AppendLine($"Actual Speed: {actualSpeed:F2} units/sec");
        //        lines.AppendLine($"Expected Speed: {speedCam:F2} units/sec");
        //        lines.AppendLine($"Speed Ratio: {speedRatio:F4}");
        //        lines.AppendLine();

        //        File.AppendAllText(logPath, lines.ToString());
        //        Console.WriteLine($"✅ Travel metrics appended to: {logPath}");
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"❌ Failed to save travel metrics: {ex.Message}");
        //    }
        //}
    } 
}
