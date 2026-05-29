using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Xml.Linq;

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

        // state untuk tombol P (edge detection)
        static int prevPState = 0;
        static bool frozenMode = false;

        // ← TAMBAHKAN: Travel time measurement system
        static Stopwatch? travelStopwatch = null;
        static Vector3? travelStartPos = new Vector3();
        static Vector3? travelTargetPos = null;
        static bool isMeasuringTravel = false;
        static int travelMeasureKey = 0;  // Menyimpan key mana yang di-press

        // Tambahkan ini di bagian atas class (Static Fields)
        static float TargetWeather = 0.001f;  // Cuaca tujuan awal (default: sedikit mendung)
        static float CurrentWeather = 0.001f; // Nilai cuaca aktif yang sedang merayap halus

        // Variabel kontrol untuk toggle kabut
        static bool isFogActive = true;
        static bool fPressed = false;

        // Variabel kontrol untuk toggle LOD color
        static bool showLODColor = false;
        static bool lPressed = false;

        // Properti public jika Anda ingin membaca status ini saat pengiriman uniform di render loop
        public static bool IsFogActive
        { 
            get { return isFogActive; }
            set { isFogActive = value; }
        } 
        
            

    public static bool GetShowLODColor()
        {
            return showLODColor;
        }
        public static bool GetIsFogActive()
        {
            return isFogActive;
        }

        public static float GetCurrentWeather()
        {
            return CurrentWeather;
        }

        public static unsafe void Init(nint glfwLib, float _speedCam)
        {
            lineShaderProgram = Shader.GetLineShaderProgram();
            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");
            glfwSetWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowShouldClose");

            isWireframe = false;
            f1Pressed = false;

            speedCam = _speedCam;

            lineVLoc = GL.GetUniformLocation(lineShaderProgram, "view");
            linePLoc = GL.GetUniformLocation(lineShaderProgram, "projection");
        }

        public static bool IsKeyDown(nint window, int key)
        {
            return glfwGetKey(window, key) == Const.GLFW_PRESS;
        }

        public static unsafe void Update(nint window,Lights lights, Camera camera, float deltaTime, TerrainChunk gameTerrainChunk)
        { 
            // Tombol ESC untuk Keluar
            if (glfwGetKey(window, Const.GLFW_KEY_ESCAPE) == Const.GLFW_PRESS)
            {
                // Beritahu GLFW untuk menutup jendela
                glfwSetWindowShouldClose(window, 1);
            }

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
                    fPressed = true;
                    Console.WriteLine(isFogActive ? "Fog: ON" : "Fog: OFF");
                }
            }
            else
            {
                fPressed = false;
            }

            // =========================================================================
            // L TOGGLE LOD COLOR (RISING EDGE)
            // =========================================================================
            int lState = glfwGetKey(window, Const.GLFW_KEY_O);
            if (lState == Const.GLFW_PRESS)
            {
                if (!lPressed)
                {
                    showLODColor = !showLODColor;
                    lPressed = true;
                    Console.WriteLine(showLODColor ? "LOD Color: ON" : "LOD Color: OFF");
                }
            }
            else
            {
                lPressed = false;
            }

            // Movement
            bool shiftPressed = glfwGetKey(window, Const.GLFW_KEY_LEFT_SHIFT) == Const.GLFW_PRESS
                                || glfwGetKey(window, Const.GLFW_KEY_RIGHT_SHIFT) == Const.GLFW_PRESS;

            // ← UBAH: Scale camera speed dengan TerrainScale
            //float scaledSpeed = speedCam * TerrainChunk.TerrainScale * deltaTime * (shiftPressed ? Const.SHIFT_SPEED_MULTIPLIER : 1.0f);
            float scaledSpeed = (speedCam / TerrainChunk.TerrainScale) * deltaTime * (shiftPressed ? Const.SHIFT_SPEED_MULTIPLIER : 1.0f);

            if (glfwGetKey(window, Const.GLFW_KEY_W) == Const.GLFW_PRESS) 
                camera.Position += camera.Front * scaledSpeed;
            if (glfwGetKey(window, Const.GLFW_KEY_S) == Const.GLFW_PRESS) 
                camera.Position -= camera.Front * scaledSpeed;
            if (glfwGetKey(window, Const.GLFW_KEY_A) == Const.GLFW_PRESS) 
                camera.Position -= Vector3.Normalize(Vector3.Cross(camera.Front, camera.Up)) * scaledSpeed;
            if (glfwGetKey(window, Const.GLFW_KEY_D) == Const.GLFW_PRESS) 
                camera.Position += Vector3.Normalize(Vector3.Cross(camera.Front, camera.Up)) * scaledSpeed;
            
            // ← TAMBAHKAN: Travel measurement (tekan T untuk start, atau lagi untuk stop)
            MeasureTravelTime(window, camera);
             
            // P: toggle freeze frustum
            int pState = glfwGetKey(window, Const.GLFW_KEY_P);
            if (pState == Const.GLFW_PRESS && prevPState != Const.GLFW_PRESS)
            {
                frozenMode = !frozenMode;
                if (frozenMode)
                {
                    Matrix4x4 view = camera.GetViewMatrix();
                    Matrix4x4 proj = camera.GetProjectionMatrix();
                    frozenCorners = TerrainChunk.GetFrustumCorners(view, proj);
                    gameTerrainChunk.SetFrozenFrustumCorners(frozenCorners);
                    gameTerrainChunk.SetHighlightFrustumMatches(true);
                }
                else
                {
                    frozenCorners = null;
                    gameTerrainChunk.ClearFrozenFrustumCorners();
                    gameTerrainChunk.SetHighlightFrustumMatches(false);
                }
            }
            prevPState = pState;

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
                TargetWeather = 0.4f; // Tekan 2 -> Sedikit Mendung (Estetik)
            }
            else if (glfwGetKey(window, Const.GLFW_KEY_3) == Const.GLFW_PRESS || glfwGetKey(window, Const.GLFW_KEY_KP_3) == Const.GLFW_PRESS)
            {
                TargetWeather = 0.8f; // Tekan 3 -> Mendung Tebal Sekali
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

        /// <summary>
        /// Measure travel time between two positions
        /// Press T to start, move camera, press T again to stop
        /// </summary>
        private static void MeasureTravelTime(nint window, Camera camera)
        {
            int tState = glfwGetKey(window, Const.GLFW_KEY_T);
            
            if (tState == Const.GLFW_PRESS)
            {
                if (travelMeasureKey != Const.GLFW_PRESS) // Rising edge
                {
                    if (!isMeasuringTravel)
                    {
                        // START measurement
                        travelStartPos = camera.Position;
                        travelStopwatch = Stopwatch.StartNew();  // ← Lebih clean
                        isMeasuringTravel = true;

                        Console.WriteLine("═════════════════════════════════════════");
                        Console.WriteLine("🚀 TRAVEL TIME MEASUREMENT STARTED");
                        Console.WriteLine($"   Start Position: ({travelStartPos?.X:F2}, {travelStartPos?.Y:F2}, {travelStartPos?.Z:F2})");
                        Console.WriteLine($"   Terrain Scale: {TerrainChunk.TerrainScale}");
                        Console.WriteLine($"   Camera Speed: {speedCam} units/sec");
                        Console.WriteLine("   Press T again to STOP measurement");
                        Console.WriteLine("═════════════════════════════════════════");
                    }
                    else
                    {
                        // STOP measurement
                        travelTargetPos = camera.Position;
                        travelStopwatch?.Stop();
                        double elapsedSeconds = travelStopwatch?.Elapsed.TotalSeconds ?? 0;

                        if (travelStartPos.HasValue && travelTargetPos.HasValue)
                        {
                            Vector3 delta = travelTargetPos.Value - travelStartPos.Value;
                            float distance = delta.Length();

                            // Calculate actual speed traveled
                            float actualSpeed = (float)(distance / elapsedSeconds);
                            float expectedSpeed = speedCam;
                            float speedRatio = actualSpeed / expectedSpeed;

                            //PrintTravelMetrics(travelStartPos.Value, travelTargetPos.Value, elapsedSeconds, distance, actualSpeed, speedRatio);
                        }

                        isMeasuringTravel = false;
                        travelStartPos = null;
                        travelTargetPos = null;
                        travelStopwatch = null;
                    }
                }
                travelMeasureKey = Const.GLFW_PRESS;
            }
            else
            {
                travelMeasureKey = Const.GLFW_RELEASE;
            }
        }

        private static void PrintTravelMetrics(Vector3 start, Vector3 end, double elapsedSeconds, float distance, float actualSpeed, float speedRatio)
        {
            Console.WriteLine();
            Console.WriteLine("═════════════════════════════════════════");
            Console.WriteLine("📊 TRAVEL TIME MEASUREMENT RESULTS");
            Console.WriteLine("═════════════════════════════════════════");
            Console.WriteLine($"Start Position:      ({start.X:F2}, {start.Y:F2}, {start.Z:F2})");
            Console.WriteLine($"End Position:        ({end.X:F2}, {end.Y:F2}, {end.Z:F2})");
            Console.WriteLine();
            Console.WriteLine($"Distance Traveled:   {distance:F2} world units");
            Console.WriteLine($"Time Elapsed:        {elapsedSeconds:F2} seconds");
            Console.WriteLine();
            Console.WriteLine($"Expected Speed:      {speedCam:F2} units/sec");
            Console.WriteLine($"Actual Speed:        {actualSpeed:F2} units/sec");
            Console.WriteLine($"Speed Ratio:         {speedRatio:F4} (should be ~1.0)");
            Console.WriteLine();
            Console.WriteLine($"Terrain Scale:       {TerrainChunk.TerrainScale}");
            
            // ← TAMBAHKAN: Calculate expected travel time
            float expectedDistance = distance / TerrainChunk.TerrainScale;
            Console.WriteLine($"Normalized Distance: {expectedDistance:F2} pixels (distance ÷ TerrainScale)");
            Console.WriteLine("═════════════════════════════════════════");
            Console.WriteLine();

            // Save to file
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "travel_metrics.txt");
                var lines = new System.Text.StringBuilder();
                lines.AppendLine($"═════════════════════════════════════════");
                lines.AppendLine($"Travel Measurement - TerrainScale: {TerrainChunk.TerrainScale}");
                lines.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                lines.AppendLine($"═════════════════════════════════════════");
                lines.AppendLine($"Start: ({start.X:F2}, {start.Y:F2}, {start.Z:F2})");
                lines.AppendLine($"End: ({end.X:F2}, {end.Y:F2}, {end.Z:F2})");
                lines.AppendLine($"Distance: {distance:F2} units");
                lines.AppendLine($"Normalized Distance (÷TerrainScale): {expectedDistance:F2} pixels");
                lines.AppendLine($"Time: {elapsedSeconds:F2} seconds");
                lines.AppendLine($"Actual Speed: {actualSpeed:F2} units/sec");
                lines.AppendLine($"Expected Speed: {speedCam:F2} units/sec");
                lines.AppendLine($"Speed Ratio: {speedRatio:F4}");
                lines.AppendLine();

                File.AppendAllText(logPath, lines.ToString());
                Console.WriteLine($"✅ Travel metrics appended to: {logPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to save travel metrics: {ex.Message}");
            }
        }
    } 
}
