using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Runtime.CompilerServices;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // =======================================================================
    //  ObjectManager — static mesh glTF, flyweight pattern, frustum culling
    // =======================================================================
    public unsafe class ObjectManager
    {
        private readonly Dictionary<string, GltfModelGpuData> _modelCache = [];
        private readonly List<GltfObject> _objects = [];
        private readonly List<CharacterAgent> _agents = [];
        private readonly Random _agentRng = new(1982);

        public Vector3 WanderCenter = Vector3.Zero;
        public float WanderRadius = 30f;
        private const float RespawnDelay = 10f;
        private const float FightSpacing = 0.7f;

        private readonly uint _shaderProgram;
        private readonly int _modelLoc;
        private readonly int _viewLoc;
        private readonly int _projLoc;
        private readonly int _sunDirLoc;
        private readonly int _realSunDirLoc;
        private readonly int _lightColorLoc;
        private readonly int _fogColorLoc;
        private readonly int _viewPosLoc;
        private readonly int _useFogLoc;
        private readonly int _baseColorFactorLoc;
        private readonly int _useAlbedoLoc;
        private readonly int _albedoMapLoc;
        private readonly int _jointLoc;
        private readonly int _jointsLoc;
        private readonly int _useSkinningLoc;
        
        // Shadow map uniforms
        private readonly int _shadowMap0Loc;
        private readonly int _shadowMap1Loc;
        private readonly int _shadowMap2Loc;
        private readonly int _lightSpaceLoc0;
        private readonly int _lightSpaceLoc1;
        private readonly int _lightSpaceLoc2;
        private readonly int _cascadeEndsLoc0;
        private readonly int _cascadeEndsLoc1;
        private readonly int _cascadeEndsLoc2;
        
        // PBR uniforms
        private readonly int _normalMapLoc;
        private readonly int _metallicRoughnessMapLoc;
        private readonly int _occlusionMapLoc;
        private readonly int _emissiveMapLoc;
        private readonly int _metallicFactorLoc;
        private readonly int _roughnessFactorLoc;
        private readonly int _emissiveFactorLoc;
        private readonly int _normalScaleLoc;
        private readonly int _occlusionStrengthLoc;
        private readonly int _hasNormalTextureLoc;
        private readonly int _hasMetallicRoughnessTextureLoc;
        private readonly int _hasOcclusionTextureLoc;
        private readonly int _hasEmissiveTextureLoc;

        public int DrawnObjects { get; private set; }
        public int TotalObjects { get; private set; }
        public int CulledObjects { get; private set; }
        /// <summary>Total triangles rendered this frame (animated + static objects, excludes terrain).</summary>
        public int RenderedTriangles { get; private set; }
        /// <summary>Total available triangles for ALL objects (animated + static) at full LOD0 detail.</summary>
        public int TotalObjectTriangles { get; private set; }
        public bool DisableFrustumCull = false;

        // Cull freeze mode (Shift+P) — objects outside frozen frustum stay hidden
        public bool CullFreezeEnabled = false;
        public Matrix4x4 CullFreezeViewProj;

        // Event untuk melacak progress loading object (progress 0-1, status message)
        public event Action<float, string>? OnLoadProgress;

        public CharacterAgent PlayerAgent;
        public GltfObject PlayerObject;

        public StaticObjectManager[] staticObjectManagers;

        public ObjectManager()
        {
            _shaderProgram = GltfShader.GetShaderProgram();
            _modelLoc = GL.GetUniformLocation(_shaderProgram, "model");
            _viewLoc = GL.GetUniformLocation(_shaderProgram, "view");
            _projLoc = GL.GetUniformLocation(_shaderProgram, "projection");
            _sunDirLoc = GL.GetUniformLocation(_shaderProgram, "sunDir");
            _realSunDirLoc = GL.GetUniformLocation(_shaderProgram, "realSunDir");
            _lightColorLoc = GL.GetUniformLocation(_shaderProgram, "lightColor");
            _fogColorLoc = GL.GetUniformLocation(_shaderProgram, "fogColor");
            _viewPosLoc = GL.GetUniformLocation(_shaderProgram, "viewPos");
            _useFogLoc = GL.GetUniformLocation(_shaderProgram, "useFog");
            _baseColorFactorLoc = GL.GetUniformLocation(_shaderProgram, "baseColorFactor");
            _useAlbedoLoc = GL.GetUniformLocation(_shaderProgram, "useAlbedo");
            _albedoMapLoc = GL.GetUniformLocation(_shaderProgram, "albedoMap");
            _jointLoc = GL.GetUniformLocation(_shaderProgram, "joints");
            _jointsLoc = GL.GetUniformLocation(_shaderProgram, "u_Joints");
            
            // Cache shadow map uniform locations
            _shadowMap0Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap0");
            _shadowMap1Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap1");
            _shadowMap2Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap2");
            _lightSpaceLoc0 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[0]");
            _lightSpaceLoc1 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[1]");
            _lightSpaceLoc2 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[2]");
            _cascadeEndsLoc0 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[0]");
            _cascadeEndsLoc1 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[1]");
            _cascadeEndsLoc2 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[2]");
            
            // Cache PBR uniform locations
            _normalMapLoc = GL.GetUniformLocation(_shaderProgram, "normalMap");
            _metallicRoughnessMapLoc = GL.GetUniformLocation(_shaderProgram, "metallicRoughnessMap");
            _occlusionMapLoc = GL.GetUniformLocation(_shaderProgram, "occlusionMap");
            _emissiveMapLoc = GL.GetUniformLocation(_shaderProgram, "emissiveMap");
            _metallicFactorLoc = GL.GetUniformLocation(_shaderProgram, "metallicFactor");
            _roughnessFactorLoc = GL.GetUniformLocation(_shaderProgram, "roughnessFactor");
            _emissiveFactorLoc = GL.GetUniformLocation(_shaderProgram, "emissiveFactor");
            _normalScaleLoc = GL.GetUniformLocation(_shaderProgram, "normalScale");
            _occlusionStrengthLoc = GL.GetUniformLocation(_shaderProgram, "occlusionStrength");
            _hasNormalTextureLoc = GL.GetUniformLocation(_shaderProgram, "hasNormalTexture");
            _hasMetallicRoughnessTextureLoc = GL.GetUniformLocation(_shaderProgram, "hasMetallicRoughnessTexture");
            _hasOcclusionTextureLoc = GL.GetUniformLocation(_shaderProgram, "hasOcclusionTexture");
            _hasEmissiveTextureLoc = GL.GetUniformLocation(_shaderProgram, "hasEmissiveTexture");
        }

        public void Init(Camera camera,TerrainChunk gameTerrainChunk)
        {
            string xbotPath = "Artifacts\\objects\\Ybot.glb";
            var rng = new Random();

            float spawnCX = 0f;
            float spawnCZ = 0f;
            float minDist = 1.6f;
            float spawnRadius = 10f;

            var spawnedPositions = new List<Vector2>();

            _objects.Clear();
            _agents.Clear();

            // ─────────────────────────────────────
            // PHASE 1: AI CHARACTERS (0% → 15%)
            // ─────────────────────────────────────
            OnLoadProgress?.Invoke(0f, "AI: spawning characters...");
            
            for (int i = 0; i < 5; i++)
            {
                float px, pz;
                int tries = 0;
                do
                {
                    float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                    float dist = (float)(rng.NextDouble() * spawnRadius);
                    px = spawnCX + MathF.Cos(angle) * dist;
                    pz = spawnCZ + MathF.Sin(angle) * dist;
                    tries++;

                    bool overlaps = spawnedPositions.Any(p =>
                        MathF.Sqrt((p.X - px) * (p.X - px) + (p.Y - pz) * (p.Y - pz)) < minDist);
                    if (!overlaps) break;

                    if (tries > 50)
                    {
                        px += minDist * MathF.Cos(i * 1.1f);
                        pz += minDist * MathF.Sin(i * 1.1f);
                        break;
                    }
                } while (true);

                spawnedPositions.Add(new Vector2(px, pz));
                float yaw = (float)(rng.NextDouble() * 360.0);

                var obj = AddObject(xbotPath, new Vector3(px, 0, pz), yaw, 1.0f);
                obj.CastShadow = true;
                SnapToTerrain(obj, gameTerrainChunk);

                float phase = (i + 1) / 5f;
                OnLoadProgress?.Invoke(phase * 0.10f, $"AI: spawning character {i+1}/5");
            }

            OnLoadProgress?.Invoke(0.10f, "AI: initializing wandering...");
            WanderCenter = new Vector3(spawnCX, 0f, spawnCZ);
            WanderRadius = 38f;
            InitWanderingAgents();

            OnLoadProgress?.Invoke(0.15f, "AI: done");            // ─────────────────────────────────────
            // PHASE 2: STATIC OBJECTS (15% → 85%)
            // ─────────────────────────────────────
            staticObjectManagers =
            [ 
                new StaticObjectManager { RotationCorrection = new Vector3(180, 0, 0) },
                new StaticObjectManager { RotationCorrection = new Vector3(-90, 0, 0) },
                new StaticObjectManager { RotationCorrection = new Vector3(-90, 0, 0) }  // Wall occluder
            ];

            OnLoadProgress?.Invoke(0.15f, "Static: initializing managers...");
             
            // Trees
            string treesName = "trees";
            OnLoadProgress?.Invoke(0.16f, $"Static: loading {treesName}...");
            staticObjectManagers[0].AddRandomObjects("Artifacts/objects/biomes/trees.glb", 1, new Vector3(0, 0, 0), 256f, 1.0f, gameTerrainChunk,
                (p) => OnLoadProgress?.Invoke(0.18f + p * 0.04f, $"Static: loading {treesName}..."),
                collisionPart: "bark",
                overrideCollisionSizeX: 1.2f,
                overrideCollisionSizeZ: 1.2f);
            staticObjectManagers[0].CastShadow = true;
            staticObjectManagers[0].UseAlpha = true;
            staticObjectManagers[0].CullAtMaxLOD = false;

            // Trees — collidable (player/NPC gak bisa tembus pohon)
            foreach (var sobj in staticObjectManagers[0].GetObjects())
                sobj.IsCollidable = true;

            OnLoadProgress?.Invoke(0.20f, $"Static: {treesName} done");

            // Daisies
            string daisiesName = "daises";
            OnLoadProgress?.Invoke(0.20f, $"Static: loading {daisiesName}...");
            staticObjectManagers[1].AddRandomObjects("Artifacts/objects/biomes/daises.glb", 100000, new Vector3(0, 0, 0), 256f, 1.0f, gameTerrainChunk,
                (p) => OnLoadProgress?.Invoke(0.20f + p * 0.65f, $"Static: loading {daisiesName}..."));
            staticObjectManagers[1].CastShadow = false;
            staticObjectManagers[1].UseAlpha = false;
            staticObjectManagers[1].CullAtMaxLOD = true;
            staticObjectManagers[1].SkipTerrainRayMarch = true; // 50rb daisies — skip ray-march, tetap ikut AABB occlusion

            OnLoadProgress?.Invoke(0.82f, "Static: loading wall occluder...");

            // Daisies — collidable (player/NPC gak bisa tembus pohon)
            foreach (var sobj in staticObjectManagers[1].GetObjects())
                sobj.IsCollidable = false;

            // Wall occluder — object besar untuk test occlusion
            // Posisi di antara player start dan area pohon/AI
            string wallPath = "Artifacts/objects/damaged_wall.glb";
            staticObjectManagers[2].AddObject(wallPath, new Vector3(20f, 5f, 10f), 0f, 0.05f, "wall", true, gameTerrainChunk);
            staticObjectManagers[2].CastShadow = true;
            staticObjectManagers[2].UseAlpha = true;
            staticObjectManagers[2].CullAtMaxLOD = false;
            // Set IsOccluder=true agar object ini menjadi penghalang
            foreach (var sobj in staticObjectManagers[2].GetObjects())
                sobj.IsOccluder = true;
            // Wall — collidable (player/NPC/camera gak bisa tembus)
            foreach (var sobj in staticObjectManagers[2].GetObjects())
                sobj.IsCollidable = true;

            OnLoadProgress?.Invoke(0.85f, "Static: all objects done");

            // ─────────────────────────────────────
            // PHASE 3: PLAYER + ANIMATIONS (85% → 100%)
            // ─────────────────────────────────────
            OnLoadProgress?.Invoke(0.85f, "Player: spawning character...");

            string playerPath = "Artifacts\\objects\\Xbot.glb";
            float playerX = 0f;
            float playerZ = 0f;
            float playerY = gameTerrainChunk.GetHeightAt(playerX, playerZ);
            float initialHeading = Config.PlayerConfig.InitialHeading;

            PlayerObject = AddObject(playerPath, new Vector3(playerX, playerY, playerZ), 0.0f, 1.0f);
            PlayerObject.IsPlayer = true;
            PlayerObject.CastShadow = true; 
            PlayerObject.SetFacing(initialHeading);

            PlayerAgent = new CharacterAgent(PlayerObject, _agentRng)
            {
                IsPlayer = true,
                Heading = initialHeading,
                StaticManagers = staticObjectManagers
            };

            OnLoadProgress?.Invoke(0.87f, "Player: loading animations...");

            // Load animation files dan apply ke semua object (AI + Player)
            string[] animFiles =
            [
                "Artifacts\\objects\\Xbot.glb",
                //"Artifacts\\objects\\Ybot.glb",
                "Artifacts\\objects\\anim\\Fighting-idle.glb", "Artifacts\\objects\\anim\\fist-fight.glb",
                "Artifacts\\objects\\anim\\punching-bag.glb", "Artifacts\\objects\\anim\\hook.glb",
                "Artifacts\\objects\\anim\\body-block.glb", "Artifacts\\objects\\anim\\taking-punch.glb",
                "Artifacts\\objects\\anim\\dying.glb", "Artifacts\\objects\\anim\\looking-around.glb",
                "Artifacts\\objects\\anim\\entry.glb",
                "Artifacts\\objects\\anim\\walk-strafe-left.glb", "Artifacts\\objects\\anim\\walk-strafe-right.glb",
                "Artifacts\\objects\\anim\\walking-backwards.glb", "Artifacts\\objects\\anim\\walking-backwards2.glb",
                "Artifacts\\objects\\anim\\walk-happy.glb", "Artifacts\\objects\\anim\\walk-standard.glb",
                "Artifacts\\objects\\anim\\jump-start.glb", "Artifacts\\objects\\anim\\jump-loop.glb", "Artifacts\\objects\\anim\\jump-end.glb",
                "Artifacts\\objects\\anim\\zombie-walk.glb","Artifacts\\objects\\anim\\jumping.glb"
            ];

            string?[] animClipNames =
            [
                null, // Xbot.glb -> base anim
                //null, // UEPerson.glb -> base anim
                "fightstance", "fistfight", "punchbag", "hook", "block", "hurt",
                "dying",
                "lookaround", "entry",
                "strafeleft", "straferight", "backward", "backward2",
                "walk-happy", "walk-standard" ,
                "jump-start", "jump-loop", "jump-end",
                "zombie-walk","jumping"

            ];
            bool[] retargetRoot =
            [
                false,
                //false, 
                false, false, false, false, false, false,false,
                true,   // dying
                false, false, 
                false, false, false, false,
                false, false, 
                false, false, false,
                false, false
            ];

            for (int i = 0; i < animFiles.Length; i++)
            {
                string clipName = animClipNames[i] ?? "base";
                ApplyAnimationFileToAll(animFiles[i], animClipNames[i], retargetRoot[i]);
                float p = 0.87f + ((i + 1) / (float)animFiles.Length) * 0.13f;
                OnLoadProgress?.Invoke(p, $"Player: anim {clipName} ({i+1}/{animFiles.Length})");
            }

            // ── Compute total available object triangles (LOD0) ──
            TotalObjectTriangles = 0;
            foreach (var obj in _objects)
            {
                var meshes = obj.GpuData.Data.Meshes;
                if (meshes != null)
                {
                    for (int mi = 0; mi < meshes.Length; mi++)
                    {
                        if (meshes[mi].Vertices.Length >= 3 && meshes[mi].Indices.Length >= 3)
                            TotalObjectTriangles += meshes[mi].Indices.Length / 3;
                    }
                }
            }
            foreach (var mgr in staticObjectManagers)
                if (mgr != null)
                    TotalObjectTriangles += mgr.TotalTriangles;

            OnLoadProgress?.Invoke(1.0f, "Loading complete!");

        }

        public GltfModelGpuData LoadModel(string path)
        {
            if (_modelCache.TryGetValue(path, out var cached)) return cached;
            var data = GltfLoader.Load(path);
            var gpuData = new GltfModelGpuData(data);
            _modelCache[path] = gpuData;
            return gpuData;
        }

        public static GltfData LoadAnimationFile(string path)
        {
            try
            {
                return GltfLoader.Load(path);
            }
            catch
            {
                return new GltfData();
            }
        }

        public GltfObject AddObject(string modelPath, Vector3 position, float yawDegrees = 0f, float scale = 1f, string? animPath = null, TerrainChunk? terrainForSnap = null, bool snapToTerrain = false)
        {
            var gpuData = LoadModel(modelPath);
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f);
            var obj = new GltfObject(gpuData, position, q, scale);
            _objects.Add(obj);

            if (!string.IsNullOrEmpty(animPath) && File.Exists(animPath))
            {
                var animData = LoadAnimationFile(animPath);
                if (animData != null && animData.Animations.Length > 0)
                {
                    obj.IsPlayer = false;
                    obj.ApplyExternalAnimation(animData);
                    obj.Update(0f);
                    if (terrainForSnap != null) obj.AlignToTerrain(terrainForSnap);
                }
            }
            else
            {
                obj.Update(0f);
            }

            // Snap to terrain height if requested
            if (snapToTerrain && terrainForSnap != null)
                SnapToTerrain(obj, terrainForSnap);

            return obj;
        }

        public void Update(float dt)
        {
            if (_objects.Count == 0) return;
            foreach (var obj in _objects) obj.Update(dt);
        }

        public GltfObject AddStaticObject(string modelPath, Vector3 position, float yawDegrees = 0f, float scale = 1f)
        {
            var obj = AddObject(modelPath, position, yawDegrees, scale);
            obj.IsStatic = true;
            return obj;
        }

        public void AddRandomStaticObjects(string modelPath, int count, Vector3 center, float radius, TerrainChunk terrain)
        {
            var rng = new Random();
            var spawned = new List<Vector3>();
            float minDistance = 2.0f;

            for (int i = 0; i < count; i++)
            {
                for (int attempts = 0; attempts < 50; attempts++)
                {
                    float ang = (float)(rng.NextDouble() * MathF.PI * 2.0);
                    float dist = (float)(rng.NextDouble() * radius);
                    Vector3 pos = center + new Vector3(MathF.Cos(ang) * dist, 0, MathF.Sin(ang) * dist);
                    
                    bool overlap = spawned.Any(s => Vector3.Distance(s, pos) < minDistance);
                    if (!overlap)
                    {
                        var obj = AddStaticObject(modelPath, pos);
                        SnapToTerrain(obj, terrain);
                        spawned.Add(pos);
                        break;
                    }
                }
            }
        }

        public void InitWanderingAgents()
        {
            foreach (var obj in _objects)
            {
                if (!obj.IsPlayer )
                    _agents.Add(new CharacterAgent(obj, _agentRng));
            }
        }

        // AAA-style: AI + movement + collisions, tanpa double-update animasi
        public void UpdateAgents(nint window, float dt, TerrainChunk? terrain, Camera camera)
        {
            if (_agents.Count == 0) return;

            // 1) Tentukan LOD per agent (AI + Anim) berbasis frustum + jarak
            for (int i = 0; i < _agents.Count && i < _objects.Count; i++)
            {
                var a = _agents[i];
                var go = _objects[i];

                var pos = a.Position;
                float dist = Vector3.Distance(camera.Position, pos);

                // arah relatif ke kamera
                var toObj = Vector3.Normalize(pos - camera.Position);
                float dot = Vector3.Dot(camera.Front, toObj);

                bool insideFrustum = go.IsVisible; // kamu sudah punya ini dari culling
                bool nearFrustum = dot > LODConfig.FrustumOuterDot;

                // ---------- AI LOD ----------
                if (!insideFrustum)
                {
                    if (!nearFrustum)
                    {
                        // jauh di luar frustum → AI jarang, tapi TIDAK frozen total
                        a.AiLOD = CharacterAgent.AiLodLevel.Simulated;
                    }
                    else
                    {
                        // dekat frustum tapi tidak kelihatan → AI reduced
                        a.AiLOD = CharacterAgent.AiLodLevel.Reduced;
                    }
                }
                else
                {
                    // di dalam frustum → LOD normal berdasarkan jarak
                    if (dist > LODConfig.AiLOD2_Distance)
                        a.AiLOD = CharacterAgent.AiLodLevel.Simulated;
                    else if (dist > LODConfig.AiLOD1_Distance)
                        a.AiLOD = CharacterAgent.AiLodLevel.Reduced;
                    else
                        a.AiLOD = CharacterAgent.AiLodLevel.Full;
                }

                // ---------- Anim LOD (distance-based, jangan pakai IsVisible) ----------
                {
                    int lod;
                    if (dist < LODConfig.AnimLOD0_Distance) lod = 0;
                    else if (dist < LODConfig.AnimLOD1_Distance) lod = 1;
                    else if (dist < LODConfig.AnimLOD2_Distance) lod = 2;
                    else lod = 3;
                    go.AnimLOD = lod;
                }
            }

            // 2) AI tick + movement (menghormati AiLOD di dalam CharacterAgent)
            foreach (var a in _agents)
                a.UpdateBehavior(dt, _agents);

            foreach (var a in _agents)
                a.Move(window, camera, dt, terrain, WanderCenter, WanderRadius);

            // 3) Collision (LOD-aware)
            ResolveCollisions(terrain);

            // 4) Respawn
            foreach (var a in _agents)
            {
                if (a.Dead && a.DeadElapsed >= RespawnDelay)
                {
                    float ang = (float)(_agentRng.NextDouble() * MathF.PI * 2.0);
                    float dist = (float)(_agentRng.NextDouble() * WanderRadius);
                    float x = WanderCenter.X + MathF.Cos(ang) * dist;
                    float z = WanderCenter.Z + MathF.Sin(ang) * dist;
                    a.Respawn(new Vector3(x, terrain.GetHeightAt(x, z), z));
                }
            }
        }


        // Collision LOD-aware
        private void ResolveCollisions(TerrainChunk terrain)
        {
            int count = _agents.Count;
            if (count <= 1) return;

            for (int i = 0; i < count; i++)
            {
                var a = _agents[i];
                if (a.Dead || a.AiLOD == CharacterAgent.AiLodLevel.Frozen) continue;

                for (int j = i + 1; j < count; j++)
                {
                    var b = _agents[j];
                    if (b.Dead || b.AiLOD == CharacterAgent.AiLodLevel.Frozen) continue;

                    if ((a.AiLOD == CharacterAgent.AiLodLevel.Simulated && LODConfig.SkipCollisionForLOD2) ||
                        (a.AiLOD == CharacterAgent.AiLodLevel.Frozen && LODConfig.SkipCollisionForLOD3) ||
                        (b.AiLOD == CharacterAgent.AiLodLevel.Simulated && LODConfig.SkipCollisionForLOD2) ||
                        (b.AiLOD == CharacterAgent.AiLodLevel.Frozen && LODConfig.SkipCollisionForLOD3))
                    continue;


                    var pa = a.Position;
                    var pb = b.Position;

                    float dx = pa.X - pb.X;
                    float dz = pa.Z - pb.Z;
                    float distSq = dx * dx + dz * dz;

                    bool engaged =
                        ((a.Mode == CharacterAgent.Behavior.Fight || a.Mode == CharacterAgent.Behavior.Chase) && ReferenceEquals(a.Target, b)) ||
                        ((b.Mode == CharacterAgent.Behavior.Fight || b.Mode == CharacterAgent.Behavior.Chase) && ReferenceEquals(b.Target, a));

                    float minDist = engaged ? FightSpacing : (CharacterAgent.CollisionRadius + CharacterAgent.CollisionRadius);
                    if (distSq >= minDist * minDist) continue;

                    float dist = MathF.Sqrt(distSq);
                    float nx, nz;
                    if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                    else
                    {
                        float ang = (float)(_agentRng.NextDouble() * MathF.PI * 2.0);
                        nx = MathF.Sin(ang); nz = MathF.Cos(ang); dist = 0f;
                    }

                    float push = (minDist - dist) * 0.5f;
                    pa.X += nx * push; pa.Z += nz * push;
                    pb.X -= nx * push; pb.Z -= nz * push;
                    pa.Y = terrain.GetHeightAt(pa.X, pa.Z);
                    pb.Y = terrain.GetHeightAt(pb.X, pb.Z);
                    a.Position = pa;
                    b.Position = pb;

                    a.AvoidFrom(pb);
                    b.AvoidFrom(pa);
                }
            }
        }


        public void ApplyAnimationFileToAll(string animPath, string? clipNameOverride = null, bool retargetRoot = false)
        {
            if (string.IsNullOrEmpty(animPath) || !File.Exists(animPath)) return;
            var animData = LoadAnimationFile(animPath);
            if (animData == null || animData.Animations.Length == 0) return;
            foreach (var obj in _objects) obj.ApplyExternalAnimation(animData, clipNameOverride, retargetRoot);
        }

        public void LoadAnimationFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*.glb"))
            {
                string clip = Path.GetFileNameWithoutExtension(file);
                ApplyAnimationFileToAll(file, clip);
            }
        }

        public void PlayAll(string clipName, float blendTime = 0.25f)
        {
            foreach (var obj in _objects) obj.Play(clipName, blendTime);
        }

        public void Draw(Camera camera, Lights light, CSM csm = null)
        {
            DrawnObjects = 0;
            CulledObjects = 0;
            RenderedTriangles = 0;

            GL.UseProgram(_shaderProgram);

            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
            GL.UniformMatrix4fv(_projLoc, 1, false, (float*)Unsafe.AsPointer(ref proj));

            GL.Uniform3f(_sunDirLoc, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
            if (_realSunDirLoc != -1) GL.Uniform3f(_realSunDirLoc, light.RealSunDir.X, light.RealSunDir.Y, light.RealSunDir.Z);
            GL.Uniform3f(_lightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(_fogColorLoc, light.FogColor.X, light.FogColor.Y, light.FogColor.Z);
            GL.Uniform3f(_viewPosLoc, camera.Position.X, camera.Position.Y, camera.Position.Z);
            GL.Uniform1i(_useFogLoc, Inputs.Keyboard.GetIsFogActive() ? 1 : 0);
            
            // Set shadow uniforms if CSM is provided
            if (csm != null)
            {
                GL.Uniform1i(_shadowMap0Loc, 6);
                GL.Uniform1i(_shadowMap1Loc, 7);
                GL.Uniform1i(_shadowMap2Loc, 8);
                
                unsafe {
                    fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                        GL.UniformMatrix4fv(_lightSpaceLoc0, 1, false, p0);
                    fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                        GL.UniformMatrix4fv(_lightSpaceLoc1, 1, false, p1);
                    fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                        GL.UniformMatrix4fv(_lightSpaceLoc2, 1, false, p2);
                }
                GL.Uniform1f(_cascadeEndsLoc0, csm.CascadeEnds[0]);
                GL.Uniform1f(_cascadeEndsLoc1, csm.CascadeEnds[1]);
                GL.Uniform1f(_cascadeEndsLoc2, csm.CascadeEnds[2]);

                // Send shadow filter mode to this shader program
                int shadowFilterLoc = GL.GetUniformLocation(_shaderProgram, "shadowFilterMode");
                if (shadowFilterLoc != -1)
                    GL.Uniform1i(shadowFilterLoc, Inputs.Keyboard.GetIsHardShadow());
            }
            
            // Set default PBR uniforms
            if (_normalMapLoc != -1) GL.Uniform1i(_normalMapLoc, 3);
            if (_metallicRoughnessMapLoc != -1) GL.Uniform1i(_metallicRoughnessMapLoc, 4);
            if (_occlusionMapLoc != -1) GL.Uniform1i(_occlusionMapLoc, 5);
            if (_emissiveMapLoc != -1) GL.Uniform1i(_emissiveMapLoc, 6);
            
            // Set default PBR factor values (will be overridden per-mesh)
            if (_metallicFactorLoc != -1) GL.Uniform1f(_metallicFactorLoc, 1.0f);
            if (_roughnessFactorLoc != -1) GL.Uniform1f(_roughnessFactorLoc, 0.3f);  // Lower for shiny metallic
            if (_normalScaleLoc != -1) GL.Uniform1f(_normalScaleLoc, 1.0f);
            if (_occlusionStrengthLoc != -1) GL.Uniform1f(_occlusionStrengthLoc, 1.0f);
            if (_emissiveFactorLoc != -1) GL.Uniform3f(_emissiveFactorLoc, 0.0f, 0.0f, 0.0f);
            if (_hasNormalTextureLoc != -1) GL.Uniform1i(_hasNormalTextureLoc, 0);
            if (_hasMetallicRoughnessTextureLoc != -1) GL.Uniform1i(_hasMetallicRoughnessTextureLoc, 0);
            if (_hasOcclusionTextureLoc != -1) GL.Uniform1i(_hasOcclusionTextureLoc, 0);
            if (_hasEmissiveTextureLoc != -1) GL.Uniform1i(_hasEmissiveTextureLoc, 0);
            
            // Use frozen frustum for culling when CullFreeze is active
            Vector4[] frustum;
            if (CullFreezeEnabled)
                frustum = ExtractFrustumPlanes(CullFreezeViewProj);
            else
                frustum = ExtractFrustumPlanes(Matrix4x4.Multiply(view, proj));

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];

                // AnimLOD distance-based pake threshold dari Config
                // NOTE: jangan override AnimLOD berdasarkan IsVisible — animasi harus tetap
                // jalan walau di-occlude biar pas visible lagi pose-nya valid (tidak freeze).
                float dist = Vector3.Distance(camera.Position, obj.Position);
                int lod;
                if (dist < LODConfig.AnimLOD0_Distance) lod = 0;
                else if (dist < LODConfig.AnimLOD1_Distance) lod = 1;
                else if (dist < LODConfig.AnimLOD2_Distance) lod = 2;
                else lod = 3;
                obj.AnimLOD = lod;

                if (!DisableFrustumCull && !IsAABBInFrustum(frustum, obj.WorldAABB))
                {
                    CulledObjects++;
                    continue;
                }

                // Occlusion culling: jika IsVisible=false (di-set oleh OcclusionCulling), skip render
                if (!obj.IsVisible && OcclusionCulling.Enabled)
                {
                    CulledObjects++;
                    continue;
                }

                // SKINNED → ENABLE SKINNING
                GL.Uniform1i(_useSkinningLoc, 1);

                var joints = obj.GetJointMatrices();
                if (joints != null && joints.Length > 0 && _jointsLoc >= 0)
                {
                    fixed (Matrix4x4* p = &joints[0])
                        GL.UniformMatrix4fv(_jointsLoc, joints.Length, false, (float*)p);
                }
               

                // Count rendered triangles for this animated object
                var meshes = obj.GpuData.Data.Meshes;
                if (meshes != null)
                {
                    for (int mi = 0; mi < meshes.Length; mi++)
                    {
                        if (meshes[mi].Vertices.Length >= 3 && meshes[mi].Indices.Length >= 3)
                            RenderedTriangles += meshes[mi].Indices.Length / 3;
                    }
                }

                obj.Draw(_modelLoc, _baseColorFactorLoc, _useAlbedoLoc, _albedoMapLoc,
                         _metallicFactorLoc, _roughnessFactorLoc, _normalScaleLoc,
                         _occlusionStrengthLoc, _emissiveFactorLoc,
                         _hasNormalTextureLoc, _hasMetallicRoughnessTextureLoc,
                         _hasOcclusionTextureLoc, _hasEmissiveTextureLoc);
                DrawnObjects++;
            }


            // --- Static Objects (Trees/Rocks) ---
            foreach (var manager in staticObjectManagers)
            {
                if (manager != null)
                {
                    if (CullFreezeEnabled)
                        manager.Draw(camera, light, csm, CullFreezeViewProj, true);
                    else
                        manager.Draw(camera, light, csm);
                    DrawnObjects = DrawnObjects + manager.GetObjectDrawn;
                    RenderedTriangles += manager.RenderedTriangles;
                }
            }


            TotalObjects = _objects.Count + staticObjectManagers.Sum(s=>s.GetTotalObject);
        }

        public void RenderShadow(Camera camera, CSM csm, int cascadeIndex, uint shadowSkinnedShader, int modelLoc, int jointsLoc, uint shadowStaticAlphaShader, int shadowStaticAlphaModelLoc)
        {
            GL.UseProgram(shadowSkinnedShader);

            // Build light-space frustum planes for this cascade to cull objects.
            // Objects outside the light frustum cannot cast shadows into this cascade.
            var planes = csm.OrthoCorners[cascadeIndex] != null
                ? CSM.BuildPlanesFromCorners(csm.OrthoCorners[cascadeIndex])
                : null;

            // Also keep a generous camera-distance cap so we don't shadow objects
            // that are way beyond the last cascade (saves shadow draw calls).
            float maxShadowDist = csm.CascadeEnds[CSM.NumCascades - 1] + 30.0f;

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];

                float dist = Vector3.Distance(camera.Position, obj.Position);
                if (dist > maxShadowDist) continue;

                if (planes != null)
                {
                    const float boundRadius = 1.5f;
                    bool outside = false;
                    foreach (var plane in planes)
                    {
                        float d = Vector3.Dot(plane.Normal, obj.Position) + plane.D;
                        if (d < -boundRadius) { outside = true; break; }
                    }
                    if (outside) continue;
                }

                obj.DrawShadow(modelLoc, jointsLoc);
            }


            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];

                // Skip objects too far from the camera (beyond last cascade)
                float dist = Vector3.Distance(camera.Position, obj.Position);
                if (dist > maxShadowDist) continue;

                // Optional light-frustum cull (skip if planes not available)
                if (planes != null)
                {
                    // Approximate bounding radius for a character (~1.5 units)
                    const float boundRadius = 1.5f;
                    bool outside = false;
                    foreach (var plane in planes)
                    {
                        float d = Vector3.Dot(plane.Normal, obj.Position) + plane.D;
                        if (d < -boundRadius) { outside = true; break; }
                    }
                    if (outside) continue;
                }

                obj.DrawShadow(modelLoc, jointsLoc);
            }


            foreach (var manager in staticObjectManagers)
            {
                if (manager != null)
                {
                    manager.RenderShadow(camera, csm, cascadeIndex, shadowStaticAlphaShader, shadowStaticAlphaModelLoc);
                }
            }

        }

        public void DrawHealthBars(Camera camera, HUD hud)
        {
            const float headHeight = 2.1f;
            var vp = Matrix4x4.Multiply(camera.GetViewMatrix(), camera.GetProjectionMatrix());

            for (int i = 0; i < _objects.Count && i < _agents.Count; i++)
            {
                var obj = _objects[i];

                if (obj.IsPlayer) continue;
                
                if (!obj.IsVisible) continue;

                var ag = _agents[i];
                if (ag.Dead) continue;

                var head = obj.Position + new Vector3(0f, headHeight, 0f);
                if ((head - camera.Position).LengthSquared() > 90f * 90f) continue;

                var clip = Vector4.Transform(new Vector4(head, 1f), vp);
                if (clip.W <= 0.05f) continue;
                float nx = clip.X / clip.W, ny = clip.Y / clip.W;
                if (nx < -1.1f || nx > 1.1f || ny < -1.1f || ny > 1.1f) continue;

                float sx = (nx * 0.5f + 0.5f) * Glfw.WindowWidth;
                float sy = (1f - (ny * 0.5f + 0.5f)) * Glfw.WindowHeight;

                float bw = 46f, bh = 6f;
                float x = sx - bw * 0.5f, y = sy - 6f;
                float hp = Math.Clamp(ag.Health / CharacterAgent.MaxHealth, 0f, 1f);

                hud.DrawBox(x - 1f, y - 1f, bw + 2f, bh + 2f, new Vector3(0f, 0f, 0f));
                hud.DrawBox(x, y, bw, bh, new Vector3(0.18f, 0.18f, 0.18f));
                var col = hp > 0.5f ? new Vector3(0.15f, 0.8f, 0.15f)
                        : hp > 0.25f ? new Vector3(0.9f, 0.75f, 0.1f)
                        : new Vector3(0.9f, 0.15f, 0.15f);
                hud.DrawBox(x, y, bw * hp, bh, col);
            }
        }

        public static void SnapToTerrain(GltfObject obj, TerrainChunk terrain)
        {
            float terrainY = terrain.GetHeightAt(obj.Position.X, obj.Position.Z);
            obj.Position = new Vector3(obj.Position.X, terrainY, obj.Position.Z);
        }

        public void SnapAllToTerrain(TerrainChunk terrain)
        {
            foreach (var obj in _objects) SnapToTerrain(obj, terrain);
        }

        public List<GltfObject> GetObjects() => _objects;

        /// <summary>
        /// Draw wireframe AABBs for all glTF objects and static objects.
        /// Uses the frozen frustum if provided (same as chunk box), otherwise falls
        /// back to the current camera frustum.
        /// Inside frustum → blue; outside frustum → yellow.
        /// </summary>
        public void DrawDebugAABBs(Camera camera, Plane[]? frozenPlanes = null)
        {
            Vector4[] frustum;
            if (frozenPlanes != null)
            {
                // Convert Plane[] to Vector4[] — same format as ExtractFrustumPlanes
                frustum = new Vector4[6];
                for (int i = 0; i < 6; i++)
                    frustum[i] = new Vector4(frozenPlanes[i].Normal.X, frozenPlanes[i].Normal.Y,
                                             frozenPlanes[i].Normal.Z, frozenPlanes[i].D);
            }
            else
            {
                var view = camera.GetViewMatrix();
                var proj = camera.GetProjectionMatrix();
                frustum = ExtractFrustumPlanes(Matrix4x4.Multiply(view, proj));
            }

            var insideColor = new Vector3(0f, 0.5f, 1f);   // blue = inside frustum
            var outsideColor = new Vector3(1f, 1f, 0f);    // yellow = outside frustum

            foreach (var obj in _objects)
            {
                var color = IsAABBInFrustum(frustum, obj.WorldAABB) ? insideColor : outsideColor;
                TerrainChunk.DrawAABBWireframe(obj.WorldAABB, color, camera);
            }

            foreach (var manager in staticObjectManagers)
            {
                if (manager != null)
                {
                    foreach (var sobj in manager.GetObjects())
                    {
                        var color = IsAABBInFrustum(frustum, sobj.CachedWorldAABB) ? insideColor : outsideColor;
                        TerrainChunk.DrawAABBWireframe(sobj.CachedWorldAABB, color, camera);
                    }
                }
            }
        }

        public void Dispose()
        {
            foreach (var (_, gpu) in _modelCache) gpu.Dispose();
            _modelCache.Clear();
            _objects.Clear();
            _agents.Clear();
        }

        private static Vector4[] ExtractFrustumPlanes(Matrix4x4 vp)
        {
            var p = new Vector4[6];
            p[0] = new Vector4(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41);
            p[1] = new Vector4(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41);
            p[2] = new Vector4(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42);
            p[3] = new Vector4(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42);
            p[4] = new Vector4(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43);
            p[5] = new Vector4(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);
            for (int i = 0; i < 6; i++)
            {
                float len = MathF.Sqrt(p[i].X * p[i].X + p[i].Y * p[i].Y + p[i].Z * p[i].Z);
                if (len > 0f) p[i] /= len;
            }
            return p;
        }

        private static bool IsAABBInFrustum(Vector4[] planes, AABB aabb)
        {
            foreach (var pl in planes)
            {
                var pv = new Vector3(
                    pl.X >= 0 ? aabb.Max.X : aabb.Min.X,
                    pl.Y >= 0 ? aabb.Max.Y : aabb.Min.Y,
                    pl.Z >= 0 ? aabb.Max.Z : aabb.Min.Z);
                if (pv.X * pl.X + pv.Y * pl.Y + pv.Z * pl.Z + pl.W < 0f)
                    return false;
            }
            return true;
        }
    }
}
