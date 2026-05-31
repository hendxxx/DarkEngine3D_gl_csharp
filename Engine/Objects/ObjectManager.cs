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
        private readonly int _lightColorLoc;
        private readonly int _fogColorLoc;
        private readonly int _viewPosLoc;
        private readonly int _useFogLoc;
        private readonly int _baseColorFactorLoc;
        private readonly int _useAlbedoLoc;
        private readonly int _albedoMapLoc;
        private readonly int _jointLoc;
        private readonly int _jointsLoc;

        public int DrawnObjects { get; private set; }
        public int TotalObjects { get; private set; }
        public int CulledObjects { get; private set; }
        public bool DisableFrustumCull = false;


        public CharacterAgent PlayerAgent;
        public GltfObject PlayerObject;


        public ObjectManager()
        {
            _shaderProgram = GltfShader.GetShaderProgram();
            _modelLoc = GL.GetUniformLocation(_shaderProgram, "model");
            _viewLoc = GL.GetUniformLocation(_shaderProgram, "view");
            _projLoc = GL.GetUniformLocation(_shaderProgram, "projection");
            _sunDirLoc = GL.GetUniformLocation(_shaderProgram, "sunDir");
            _lightColorLoc = GL.GetUniformLocation(_shaderProgram, "lightColor");
            _fogColorLoc = GL.GetUniformLocation(_shaderProgram, "fogColor");
            _viewPosLoc = GL.GetUniformLocation(_shaderProgram, "viewPos");
            _useFogLoc = GL.GetUniformLocation(_shaderProgram, "useFog");
            _baseColorFactorLoc = GL.GetUniformLocation(_shaderProgram, "baseColorFactor");
            _useAlbedoLoc = GL.GetUniformLocation(_shaderProgram, "useAlbedo");
            _albedoMapLoc = GL.GetUniformLocation(_shaderProgram, "albedoMap");
            _jointLoc = GL.GetUniformLocation(_shaderProgram, "joints");
            _jointsLoc = GL.GetUniformLocation(_shaderProgram, "u_Joints");
        }

        public void Init(Camera camera,TerrainChunk gameTerrainChunk)
        {
            string xbotPath = "Artifacts\\objects\\Stuntman.glb";
            var rng = new Random();

            float spawnCX = 0f;
            float spawnCZ = 0f;
            float minDist = 1.6f;
            float spawnRadius = 50f;

            var spawnedPositions = new List<Vector2>();

            for (int i = 0; i < 100; i++)
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
                SnapToTerrain(obj, gameTerrainChunk);
            }


            
            WanderCenter = new Vector3(spawnCX, 0f, spawnCZ);
            WanderRadius = 38f;
            InitWanderingAgents();

            // === PLAYER SPAWN ===
            string playerPath = "Artifacts\\objects\\Xbot.glb";

            float playerX = 0f;
            float playerZ = 0f;
            float playerY = gameTerrainChunk.GetHeightAt(playerX, playerZ);

            PlayerObject = AddObject(playerPath, new Vector3(playerX, playerY, playerZ), 0.0f, 1.0f);
            PlayerAgent = new CharacterAgent(PlayerObject, _agentRng)
            {   
                IsPlayer = true
            };

            ApplyAnimationFileToAll("Artifacts\\objects\\Xbot.glb");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\Fighting-idle.glb", "fightstance");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\fist-fight.glb", "fistfight");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\punching-bag.glb", "punchbag");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\hook.glb", "hook");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\body-block.glb", "block");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\taking-punch.glb", "hurt");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\dying.glb", "dying", retargetRoot: true);
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\looking-around.glb", "lookaround");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\entry.glb", "entry");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\walk-strafe-left.glb", "strafeleft");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\walk-strafe-right.glb", "straferight");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\walking-backwards.glb", "backward");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\walking-backwards2.glb", "backward2");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\walk-happy.glb", "walk-happy");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\walk-standard.glb", "walk-standard");
            LoadAnimationFolder("Artifacts\\anim");

            // === INITIAL FACING ===
            float initialHeading = Config.PlayerConfig.InitialHeading;

            // Set heading player
            PlayerAgent.Heading = initialHeading;
            PlayerObject.SetFacing(initialHeading);

            // Sinkronkan kamera
            camera.Yaw = initialHeading;
            camera.Pitch = 10f; // sedikit menunduk biar enak

            // Masukkan player ke list agents paling depan
            _agents.Insert(0, PlayerAgent);
            _objects.Insert(0, PlayerObject);


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

        public GltfObject AddObject(string modelPath, Vector3 position, float yawDegrees = 0f, float scale = 1f, string? animPath = null, TerrainChunk? terrainForSnap = null)
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
                    obj.ApplyExternalAnimation(animData);
                    obj.Update(0f);
                    if (terrainForSnap != null) obj.AlignToTerrain(terrainForSnap);
                }
            }
            else
            {
                obj.Update(0f);
            }

            return obj;
        }

        public void Update(float dt)
        {
            if (_objects.Count == 0) return;
            foreach (var obj in _objects) obj.Update(dt);
        }

        public void InitWanderingAgents()
        {
            _agents.Clear();
            foreach (var obj in _objects) _agents.Add(new CharacterAgent(obj, _agentRng));
        }

        // AAA-style: AI + movement + collisions, tanpa double-update animasi
        public void UpdateAgents(nint window, float dt, TerrainChunk terrain, Camera camera)
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

                // ---------- Anim LOD ----------
                if (!insideFrustum)
                {
                    // di luar frustum → animasi skip total
                    go.AnimLOD = 3;
                }
                else if (dist < LODConfig.AnimLOD0_Distance)
                    go.AnimLOD = 0;
                else if (dist < LODConfig.AnimLOD1_Distance)
                    go.AnimLOD = 1;
                else if (dist < LODConfig.AnimLOD2_Distance)
                    go.AnimLOD = 2;
                else
                    go.AnimLOD = 3;
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

        public void Draw(Camera camera, Lights light)
        {
            DrawnObjects = 0;
            CulledObjects = 0;

            GL.UseProgram(_shaderProgram);

            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
            GL.UniformMatrix4fv(_projLoc, 1, false, (float*)Unsafe.AsPointer(ref proj));

            GL.Uniform3f(_sunDirLoc, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
            GL.Uniform3f(_lightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(_fogColorLoc, light.FogColor.X, light.FogColor.Y, light.FogColor.Z);
            GL.Uniform3f(_viewPosLoc, camera.Position.X, camera.Position.Y, camera.Position.Z);
            GL.Uniform1i(_useFogLoc, Inputs.Keyboard.GetIsFogActive() ? 1 : 0);

            var frustum = ExtractFrustumPlanes(Matrix4x4.Multiply(view, proj));

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];

                float dist = Vector3.Distance(camera.Position, obj.Position);
                if (!obj.IsVisible)
                    obj.AnimLOD = 3;
                else if (dist < 20f)
                    obj.AnimLOD = 0;
                else if (dist < 50f)
                    obj.AnimLOD = 1;
                else if (dist < 120f)
                    obj.AnimLOD = 2;
                else
                    obj.AnimLOD = 3;

                if (!DisableFrustumCull && !IsAABBInFrustum(frustum, obj.WorldAABB))
                {
                    CulledObjects++;
                    continue;
                }

                var joints = obj.GetJointMatrices();
                if (joints != null && joints.Length > 0 && _jointsLoc >= 0)
                {
                    fixed (Matrix4x4* p = &joints[0])
                        GL.UniformMatrix4fv(_jointsLoc, joints.Length, false, (float*)p);
                }

                obj.Draw(_modelLoc, _baseColorFactorLoc, _useAlbedoLoc, _albedoMapLoc);
                DrawnObjects++;
            }

            TotalObjects = _objects.Count;
        }

        public void DrawHealthBars(Camera camera, HUD hud)
        {
            const float headHeight = 2.1f;
            var vp = Matrix4x4.Multiply(camera.GetViewMatrix(), camera.GetProjectionMatrix());

            for (int i = 0; i < _objects.Count && i < _agents.Count; i++)
            {
                var obj = _objects[i];
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
