namespace DarkEngine3D_gl_csharp.Engine.Config
{
    public static class LODConfig
    {
        // ============================
        // ANIMATION LOD
        // ============================
        public static float AnimLOD0_Distance = 35f;
        public static float AnimLOD1_Distance = 70f;
        public static float AnimLOD2_Distance = 140f;

        // ============================
        // AI LOD
        // ============================
        public static float AiLOD0_Distance = 30f;
        public static float AiLOD1_Distance = 60f;
        public static float AiLOD2_Distance = 120f;

        // ============================
        // AI Tick Rates
        // ============================
        public static float TickFull = 0f;        // 60 FPS
        public static float TickReduced = 1f / 30f;  // 30 FPS
        public static float TickSimulated = 1f / 8f;   // 8 FPS
        public static float TickFrozen = 0.5f;      // 2 FPS

        // ============================
        // Collision LOD
        // ============================
        public static bool SkipCollisionForLOD2 = true;
        public static bool SkipCollisionForLOD3 = true;

        // ============================
        // Movement LOD
        // ============================
        public static float SimulatedSpeedMultiplier = 0.85f;

        // ============================
        // Frustum LOD
        // ============================
        // seberapa jauh dari forward camera masih dianggap "near frustum"
        public static float FrustumInnerDot = 0.2f;   // > 0.2 = benar2 di depan
        public static float FrustumOuterDot = -0.2f;  // > -0.2 = masih dekat frustum
    }

    // ============================
    // Player Config
    // ============================
    public static class PlayerConfig
    {

        // ============================
        // Initial Heading -> menghadap ke ?
        // ============================
        public static float InitialHeading = 45.0f;  // 45 derajat ke kanan

        // ============================
        // Movement
        // ============================
        public static float Walk = 1.0f;
        public static float Run = 1.25f;
        public static float Sprint = 4.0f;

        private static float _verticalVelocity = 0f;
        private const float gravity = -25f;
        private const float jumpForce = 10f;


        // ============================
        // Camera
        // ============================
        public static float CameraDistance = 10.0f;  
        public static float CameraOffsetHeight = 1.8f;  

        public static float CameraMinDistance = 1.5f;  
        
        public static float TargetCameraDistance = 10.0f;  
        public static float ShoulderOffset = 10.0f;  

        public static float TargetShoulderOffset = 0.6f;  
        public static float ZoomSpeed = 1.5f;  
        public static float CameraFollowSpeed = 0.15f;  
        

        public static float MaxCameraDistance = 15.0f; 
    }

    // ============================
    // AI Config
    // ============================
    public static class AIConfig
    {
 
        // ============================
        // Speed
        // ============================
        //public static float Walk = 1.6f;  
        //public static float Run = 4.6f;  
        //public static float Sprint = 7.4f;

        public static float Walk = 1.0f;
        public static float Run = 2.5f;
        public static float Sprint = 4.0f;

    }
}
