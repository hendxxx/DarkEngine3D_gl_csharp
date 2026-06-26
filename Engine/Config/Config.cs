using System.Collections.Generic;

namespace DarkEngine3D_gl_csharp.Engine.Config
{
    // ============================
    // Gameplay Config
    // ============================
    public static class GameplayConfig
    {
        /// <summary>Whether pressing ESC toggles the pause menu in-game.</summary>
        public static bool PauseOnEsc = false;
    }

    public static class ShadowConfig
    {

        public static float[] CascadeLayer = [ 30.0f, 70.0f, 140.0f ];
        // Semua cascade di 4096 agar tidak ada penurunan kualitas bayangan
        public static int[] CascadeSizes = [8192, 4096, 2048]; 
        //public static int[] CascadeSizes = [4096, 2048, 1024]; 

    }
    // ============================
    // Scale Config (global, configurable)
    // ============================
    public static class ScaleConfig
    {
        public static float ObjScale = 1.0f;

        // Movement
        public static bool ScaleMovement = true;
        public static float MovementBaseScale = 1.0f* ObjScale;
        public static float MovementMinMul = 0.50f * ObjScale;   // karakter kecil = 50%
        public static float MovementMaxMul = 1.00f * ObjScale;   // karakter besar = 100%

        // Camera
        public static bool ScaleCamera = true;
        public static float CameraBaseScale = 1.0f * ObjScale;
        public static float CameraMinMul = 0.75f * ObjScale;
        public static float CameraMaxMul = 1.35f * ObjScale;

        // Combat
        public static bool ScaleCombat = true;
        public static float CombatBaseScale = 1.0f * ObjScale;

        // Animation
        public static bool ScaleAnimationSpeed = true;
        public static float AnimMinMul = 0.9f * ObjScale;
        public static float AnimMaxMul = 1.2f * ObjScale;

        // AI Perception
        public static bool ScaleAI = true;
        public static float AIBaseScale = 1.0f * ObjScale;
    }
    public static class LODConfig
    {
        // ============================
        // OBJECT LOD — distance thresholds untuk static & dynamic object mesh
        // Semua object (pohon, rumput, dll) mengacu ke threshold ini
        // ============================
        public readonly static float ObjectLOD0_Distance = 35f;
        public readonly static float ObjectLOD1_Distance = 50f;
        public readonly static float ObjectLOD2_Distance = 100f;
        public readonly static float ObjectLOD3_Distance = 120f;

        // ============================
        // TERRAIN LOD — distance thresholds untuk terrain chunk
        // (dalam satuan world unit. Gunakan multiplier chunk size atau absolute)
        // ============================
        public readonly static float TerrainLOD0_Distance = 60f;
        public readonly static float TerrainLOD1_Distance = 130f;
        public readonly static float TerrainLOD2_Distance = 250f;
        public readonly static float TerrainLOD3_Distance = 500f;

        // ============================
        // ANIMATION LOD
        // ============================
        public readonly static float AnimLOD0_Distance = 35f;
        public readonly static float AnimLOD1_Distance = 70f;
        public readonly static float AnimLOD2_Distance = 140f;

        // ============================
        // AI LOD
        // ============================
        public readonly static float AiLOD0_Distance = 30f;
        public readonly static float AiLOD1_Distance = 60f;
        public readonly static float AiLOD2_Distance = 120f;

        // ============================
        // AI Tick Rates
        // ============================
        public readonly static float TickFull = 0f;        // 60 FPS
        public readonly static float TickReduced = 1f / 30f;  // 30 FPS
        public readonly static float TickSimulated = 1f / 8f;   // 8 FPS
        public readonly static float TickFrozen = 0.5f;      // 2 FPS

        // ============================
        // Collision LOD
        // ============================
        public readonly static bool SkipCollisionForLOD2 = true;
        public readonly static bool SkipCollisionForLOD3 = true;

        // ============================
        // Movement LOD
        // ============================
        public readonly static float SimulatedSpeedMultiplier = 0.85f;

        // ============================
        // Frustum LOD
        // ============================
        // seberapa jauh dari forward camera masih dianggap "near frustum"
        public readonly static float FrustumInnerDot = 0.2f;   // > 0.2 = benar2 di depan
        public readonly static float FrustumOuterDot = -0.2f;  // > -0.2 = masih dekat frustum
    }

    public enum CameraMode { FirstPerson, OTS, Orbit, Tactical, Follow, Chase }

    public class CameraPreset
    {
        public CameraMode Mode { get; set; }
        public float MinPitch { get; set; }
        public float MaxPitch { get; set; }
        public bool AllowFreeLook { get; set; }
        public float DefaultDistance { get; set; }
        public float MinDistance { get; set; }
        public float MaxDistance { get; set; }
        public bool AllowZoom { get; set; }
        public float ShoulderOffset { get; set; }
        public float HeightOffset { get; set; }
        public bool TrueOTS { get; set; }
        // If true, negate pitch when building the orbit rotation matrix.
        // Needed when camera orbits around the player from any direction (Orbit/Follow/Chase).
        // Not needed for Tactical where pitch is already forced positive (top-down).
        public bool InvertOrbitPitch { get; set; }
    }

    public static class CameraConfig
    {
        // ============================
        // Camera
        // ============================
        public static float CameraDistance = 10.0f;
        public static float CameraOffsetHeight = 1.8f;
        public static float CameraFlySpeed = 50.0f;

        public static float CameraMinDistance = 1.5f;

        public static float TargetCameraDistance = 10.0f;
        public static float ShoulderOffset = 10.0f;

        public static float TargetShoulderOffset = 0.6f;
        public static float ZoomSpeed = 1.5f;
        public static float CameraFollowSpeed = 0.15f;
        public static float FirstPersonHeadYawLimit = 85.0f;
        public static float FirstPersonCameraForwardOffset = 0.35f;


        public static float MaxCameraDistance = 15.0f;
        public readonly static Dictionary<CameraMode, CameraPreset> Presets = new()
        {
            { CameraMode.OTS, new CameraPreset {
                Mode = CameraMode.OTS,
                MinPitch = -75f, MaxPitch = 75f,
                AllowFreeLook = false,
                DefaultDistance = 2.5f, MinDistance = 1.5f, MaxDistance = 4.0f, AllowZoom = true,
                ShoulderOffset = -0.6f, HeightOffset = 1.5f,
                TrueOTS = true, InvertOrbitPitch = true
            }},
            { CameraMode.Orbit, new CameraPreset {
                Mode = CameraMode.Orbit,
                MinPitch = -85f, MaxPitch = 85f,
                AllowFreeLook = true,
                DefaultDistance = 4.0f, MinDistance = 2.0f, MaxDistance = 8.0f, AllowZoom = true,
                ShoulderOffset = 0.0f, HeightOffset = 1.5f,
                TrueOTS = false, InvertOrbitPitch = true  // Orbit can go above/below
            }},
            { CameraMode.Tactical, new CameraPreset {
                Mode = CameraMode.Tactical,
                MinPitch = 40f, MaxPitch = 85f,
                AllowFreeLook = true,
                DefaultDistance = 12.0f, MinDistance = 8.0f, MaxDistance = 20.0f, AllowZoom = true,
                ShoulderOffset = 0.0f, HeightOffset = 2.0f,
                TrueOTS = false, InvertOrbitPitch = false  // Already forced top-down, no inversion needed
            }},
            { CameraMode.Follow, new CameraPreset {
                Mode = CameraMode.Follow,
                MinPitch = -60f, MaxPitch = 60f,
                AllowFreeLook = false,
                DefaultDistance = 5.0f, MinDistance = 3.0f, MaxDistance = 10.0f, AllowZoom = true,
                ShoulderOffset = 0.0f, HeightOffset = 1.7f,
                TrueOTS = false, InvertOrbitPitch = true
            }},
            { CameraMode.Chase, new CameraPreset {
                Mode = CameraMode.Chase,
                MinPitch = -30f, MaxPitch = 50f,
                AllowFreeLook = false,
                DefaultDistance = 6.0f, MinDistance = 4.0f, MaxDistance = 12.0f, AllowZoom = false,
                ShoulderOffset = 0.0f, HeightOffset = 1.5f,
                TrueOTS = false, InvertOrbitPitch = true
            }}
        };
    }

    // ============================
    // Player Config
    // ============================
    public static class PlayerConfig
    {

        // ============================
        // Initial Heading -> menghadap ke ?
        // ============================
        public readonly static float InitialHeading = 45.0f;  // 45 derajat ke kanan

        // ============================
        // Movement
        // ============================
        public readonly static float Walk = 1.0f;
        public readonly static float Run = 1.25f;
        public readonly static float Sprint = 4.0f;

        private readonly static float _verticalVelocity = 0f;
        private const float gravity = -25f;
        private const float jumpForce = 10f;


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

    // ============================
    // Occlusion Config
    // ============================
    public enum OcclusionMode { Software, HiZ }

    public static class OcclusionConfig
    {
        // Pilih mode occlusion culling: Software (CPU ray-march) atau HiZ (GPU depth prepass)
        public static OcclusionMode Mode = OcclusionMode.HiZ;

        // Master toggle for occlusion culling (applies to both modes)
        public static bool UseOcclusion = true;

        // Distance threshold untuk terrain occlusion quality (Software mode only):
        // Object < TerrainOcclusionNearDist → pakai 1-corner ray-march (akurat)
        // Object >= TerrainOcclusionNearDist → pakai quick height check (cepat)
        // Default 50f = ObjectLOD1_Distance
        public static float TerrainOcclusionNearDist = 50f;

        // Hi-Z resolution scale (0-1). 0.25f = 32×18, 0.5f = 64×36, 1.0f = 128×72
        // Lebih besar = lebih akurat, tapi lebih lambat (GetHeightAt calls)
        public static float HiZResolutionScale = 0.25f;

        // Hi-Z depth buffer step size (meters). 5f = step 5m × 60 steps = 300m range
        // Lebih kecil = lebih akurat untuk ridge tipis, tapi 2× GetHeightAt tiap halving
        public static float HiZStepSize = 5f;
    }
}
