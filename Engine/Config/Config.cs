namespace DarkEngine3D_gl_csharp.Engine.Config
{
    public static class LODConfig
    {
        // ============================
        // ANIMATION LOD
        // ============================
        public static float AnimLOD0_Distance = 35f;   // full anim sampai 35m
        public static float AnimLOD1_Distance = 70f;   // update tiap 3 frame sampai 70m
        public static float AnimLOD2_Distance = 140f;  // freeze pose setelah 140m


        // ============================
        // AI LOD
        // ============================
        public static float AiLOD0_Distance = 30f;   // full AI
        public static float AiLOD1_Distance = 60f;   // 15 FPS AI
        public static float AiLOD2_Distance = 120f;  // 3 FPS AI


        // ============================
        // AI Tick Rates
        // ============================
        public static float TickFull = 0f;        // 60 FPS
        public static float TickReduced = 1f / 30f;  // 30 FPS (dari 15 FPS → jauh lebih halus)
        public static float TickSimulated = 1f / 8f;   // 8 FPS (dari 3 FPS → lebih smooth)
        public static float TickFrozen = 0.5f;      // tetap 2 FPS


        // ============================
        // Collision LOD
        // ============================
        public static bool SkipCollisionForLOD2 = true;
        public static bool SkipCollisionForLOD3 = true;

        // ============================
        // Movement LOD
        // ============================
        public static float SimulatedSpeedMultiplier = 0.85f;
    }
}
