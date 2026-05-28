using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  ObjectManager — static mesh glTF, flyweight pattern, frustum culling
    // ===========================================================================
    public unsafe class ObjectManager
    {
        private readonly Dictionary<string, GltfModelGpuData> _modelCache = [];
        private readonly List<GltfObject>                     _objects    = [];
        private readonly List<CharacterAgent>                 _agents     = [];
        private readonly Random                               _agentRng   = new(777);

        // Soft wander boundary for the autonomous agents.
        public Vector3 WanderCenter = Vector3.Zero;
        public float   WanderRadius = 30f;
        private const float RespawnDelay = 10f;   // seconds a fallen character stays down
        private const float FightSpacing = 0.7f;   // how close two fighters may stand (no body/leg clipping)

        private readonly uint _shaderProgram;
        private readonly int  _modelLoc;
        private readonly int  _viewLoc;
        private readonly int  _projLoc;
        private readonly int  _sunDirLoc;
        private readonly int  _lightColorLoc;
        private readonly int  _fogColorLoc;
        private readonly int  _viewPosLoc;
        private readonly int  _useFogLoc;
        private readonly int  _baseColorFactorLoc;
        private readonly int  _useAlbedoLoc;
        private readonly int  _albedoMapLoc;
        private readonly int  _jointLoc;
        private readonly int  _jointsLoc;

        public int  DrawnObjects  { get; private set; }
        public int  CulledObjects { get; private set; }
        public bool DisableFrustumCull = false;

        // -----------------------------------------------------------------------
        public ObjectManager()
        {
            _shaderProgram      = GltfShader.GetShaderProgram();
            _modelLoc           = GL.GetUniformLocation(_shaderProgram, "model");
            _viewLoc            = GL.GetUniformLocation(_shaderProgram, "view");
            _projLoc            = GL.GetUniformLocation(_shaderProgram, "projection");
            _sunDirLoc          = GL.GetUniformLocation(_shaderProgram, "sunDir");
            _lightColorLoc      = GL.GetUniformLocation(_shaderProgram, "lightColor");
            _fogColorLoc        = GL.GetUniformLocation(_shaderProgram, "fogColor");
            _viewPosLoc         = GL.GetUniformLocation(_shaderProgram, "viewPos");
            _useFogLoc          = GL.GetUniformLocation(_shaderProgram, "useFog");
            _baseColorFactorLoc = GL.GetUniformLocation(_shaderProgram, "baseColorFactor");
            _useAlbedoLoc       = GL.GetUniformLocation(_shaderProgram, "useAlbedo");
            _albedoMapLoc       = GL.GetUniformLocation(_shaderProgram, "albedoMap");
            _jointLoc           = GL.GetUniformLocation(_shaderProgram, "joints");
            _jointsLoc          = GL.GetUniformLocation(_shaderProgram, "u_Joints");

            //Console.WriteLine($"[ObjectManager] shader={_shaderProgram} model={_modelLoc} view={_viewLoc} proj={_projLoc}");
        }

        // -----------------------------------------------------------------------
        public GltfModelGpuData LoadModel(string path)
        {
            if (_modelCache.TryGetValue(path, out var cached)) return cached;
            //Console.WriteLine($"[ObjectManager] Loading: {path}");
            var data    = GltfLoader.Load(path);
            var gpuData = new GltfModelGpuData(data);
            _modelCache[path] = gpuData;
            return gpuData;
        }

        // -----------------------------------------------------------------------
        public static GltfData LoadAnimationFile(string path)
        {
            //Console.WriteLine($"[ObjectManager] Loading animation file: {path}");
            try
            {
                return GltfLoader.Load(path);
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[ObjectManager] Failed loading animation: {ex.Message}");
                return new GltfData();
            }
        }

        // -----------------------------------------------------------------------
        public GltfObject AddObject(string modelPath, Vector3 position, float yawDegrees = 0f, float scale = 1f, string? animPath = null, TerrainChunk? terrainForSnap = null)
        {
            var gpuData = LoadModel(modelPath);
            var q       = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f);
            var obj     = new GltfObject(gpuData, position, q, scale);
            _objects.Add(obj);

            if (!string.IsNullOrEmpty(animPath) && File.Exists(animPath))
            {
                var animData = LoadAnimationFile(animPath);
                if (animData != null && animData.Animations.Length > 0)
                {
                    obj.ApplyExternalAnimation(animData);
                    // update once so node globals are correct before snapping
                    obj.Update(0f);
                    if (terrainForSnap != null) obj.AlignToTerrain(terrainForSnap);
                }
            }
            else
            {
                // if no external anim provided, still ensure internal anim is evaluated and object is snapped later by caller
                obj.Update(0f);
            }

            return obj;
        }
        public void Init(TerrainChunk gameTerrainChunk)
        {
            // Stuntman is the rendered model; its idle/walk/run animations are layered on
            // afterwards from Xbot.glb (see ApplyAnimationFileToAll below).
            string xbotPath = "Artifacts\\objects\\Stuntman.glb";
            var rng = new Random();   // time-seeded → different spawn layout each run

            float spawnCX = 0f;
            float spawnCZ = 0f;
            float minDist = 1.6f;    // jarak minimum antar object (meter)
            float spawnRadius = 50f; // area spawn (wider to fit twice as many)

            var spawnedPositions = new List<Vector2>();

            for (int i = 0; i < 10; i++)   // twice as many characters
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

                    // Cek overlap dengan semua posisi yang sudah ada
                    bool overlaps = spawnedPositions.Any(p =>
                        MathF.Sqrt((p.X - px) * (p.X - px) + (p.Y - pz) * (p.Y - pz)) < minDist);
                    if (!overlaps) break;

                    // Jika terlalu banyak percobaan, paksa geser
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
                //Console.WriteLine($"[Spawn] Xbot #{i + 1} pos=({px:F1}, {obj.Position.Y:F1}, {pz:F1}) yaw={yaw:F0}° tries={tries}");
            }


            //Console.WriteLine($"[ObjectManager] {GetObjects().Count} objects spawned.");

            // Load animations from a SEPARATE file and apply them to the already-loaded
            // model (TODO #2). Stuntman ships only one baked clip, so its idle/walk/run
            // come from Xbot.glb — bones are matched by normalized name and rotations are
            // retargeted across the two rigs.
            ApplyAnimationFileToAll("Artifacts\\objects\\Xbot.glb");

            // Fighting animations, retargeted onto Stuntman. Stance + attack variations
            // (fist-fight / punching-bag / hook) plus block, hit-reaction and death clips
            // that the combat AI uses for blocking, taking damage and dying.
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\Fighting-idle.glb", "fightstance");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\fist-fight.glb", "fistfight");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\punching-bag.glb", "punchbag");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\hook.glb", "hook");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\body-block.glb", "block");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\taking-punch.glb", "hurt");
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\dying.glb", "dying", retargetRoot: true);
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\looking-around.glb", "lookaround");  // nervous coward idle
            ApplyAnimationFileToAll("Artifacts\\objects\\anim\\entry.glb", "entry");        // winner's celebration pose

            // Extra fighting clips (jab / hook / cross / …): drop converted Mixamo GLBs
            // into Artifacts\animations\ and they are auto-loaded + retargeted, each named
            // after its file. Optional — the AI uses them as one-shot strikes if present.
            LoadAnimationFolder("Artifacts\\animations");

            // Autonomous behaviour: each character is randomly aggressive or coward, walks
            // /runs in random directions, chases / fights / flees based on what it sees,
            // avoids others on collision, and stays clamped to the terrain.
            WanderCenter = new Vector3(spawnCX, 0f, spawnCZ);
            WanderRadius = 38f;
            InitWanderingAgents();   // after clips are loaded
        }
        // -----------------------------------------------------------------------
        public void Update(float dt)
        {
            foreach (var obj in _objects) obj.Update(dt);
        }

        // -----------------------------------------------------------------------
        //  Create one wandering agent per spawned object. Call this AFTER animation
        //  clips are loaded (so each agent's initial idle/walk/run actually plays).
        public void InitWanderingAgents()
        {
            _agents.Clear();
            foreach (var obj in _objects) _agents.Add(new CharacterAgent(obj, _agentRng));
        }

        // -----------------------------------------------------------------------
        //  Drive the autonomous agents: decide + steer, integrate movement (clamped
        //  to the terrain), resolve agent–agent collisions, then advance animation.
        public void UpdateAgents(float dt, TerrainChunk terrain)
        {
            foreach (var a in _agents) a.UpdateBehavior(dt, _agents);
            foreach (var a in _agents) a.Move(dt, terrain, WanderCenter, WanderRadius);

            ResolveCollisions(terrain);

            // Revive the fallen a few seconds later (at full health, random spot) so
            // the brawl keeps going.
            foreach (var a in _agents)
            {
                if (a.Dead && a.DeadElapsed >= RespawnDelay)
                {
                    float ang  = (float)(_agentRng.NextDouble() * MathF.PI * 2.0);
                    float dist = (float)(_agentRng.NextDouble() * WanderRadius);
                    float x = WanderCenter.X + MathF.Cos(ang) * dist;
                    float z = WanderCenter.Z + MathF.Sin(ang) * dist;
                    a.Respawn(new Vector3(x, terrain.GetHeightAt(x, z), z));
                }
            }

            foreach (var obj in _objects) obj.Update(dt);
        }

        // Separate any overlapping agents and turn each away from the other.
        private void ResolveCollisions(TerrainChunk terrain)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                for (int j = i + 1; j < _agents.Count; j++)
                {
                    var a = _agents[i];
                    var b = _agents[j];
                    if (a.Dead || b.Dead) continue;   // corpses don't collide
                    var pa = a.Position;
                    var pb = b.Position;

                    float dx = pa.X - pb.X;
                    float dz = pa.Z - pb.Z;
                    float distSq = dx * dx + dz * dz;

                    // Fighting partners stand much closer (toe-to-toe) so punches land;
                    // everyone else keeps a full personal-space radius.
                    bool engaged = ((a.Mode == CharacterAgent.Behavior.Fight || a.Mode == CharacterAgent.Behavior.Chase) && ReferenceEquals(a.Target, b))
                                || ((b.Mode == CharacterAgent.Behavior.Fight || b.Mode == CharacterAgent.Behavior.Chase) && ReferenceEquals(b.Target, a));
                    float minDist = engaged ? FightSpacing : (CharacterAgent.CollisionRadius + CharacterAgent.CollisionRadius);

                    if (distSq >= minDist * minDist) continue;

                    float dist = MathF.Sqrt(distSq);
                    float nx, nz;
                    if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                    else
                    {
                        // exactly coincident — pick an arbitrary separation axis
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

        // -----------------------------------------------------------------------
        //  Load an animation-only glTF once and apply (retarget) its clips to every
        //  managed object. This is the "one model file + one animation file" workflow:
        //  the model is loaded normally, then animations from another file are layered
        //  on and become playable by name (idle/walk/run/…).
        public void ApplyAnimationFileToAll(string animPath, string? clipNameOverride = null, bool retargetRoot = false)
        {
            if (string.IsNullOrEmpty(animPath) || !File.Exists(animPath))
            {
                //Console.WriteLine($"[ObjectManager] Animation file not found: {animPath}");
                return;
            }
            var animData = LoadAnimationFile(animPath);
            if (animData == null || animData.Animations.Length == 0) return;
            foreach (var obj in _objects) obj.ApplyExternalAnimation(animData, clipNameOverride, retargetRoot);
        }

        // Load every .glb in a folder as an extra animation source, using each file's
        // name (without extension) as the clip name. This is how fighting clips are
        // added: drop "jab.glb", "hook.glb", "fighting_idle.glb", … and they become
        // playable clips "jab", "hook", "fighting_idle", retargeted onto the model.
        public void LoadAnimationFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*.glb"))
            {
                string clip = Path.GetFileNameWithoutExtension(file);
                //Console.WriteLine($"[ObjectManager] Loading fighting clip '{clip}' from {file}");
                ApplyAnimationFileToAll(file, clip);
            }
        }

        // -----------------------------------------------------------------------
        //  Crossfade every managed object to the named animation clip. Objects
        //  that already play that clip are left untouched, so this is safe to call
        //  every frame from input handling.
        public void PlayAll(string clipName, float blendTime = 0.25f)
        {
            foreach (var obj in _objects) obj.Play(clipName, blendTime);
        }

        // -----------------------------------------------------------------------
        public void Draw(Camera camera, Lights light)
        {
            DrawnObjects  = 0;
            CulledObjects = 0;

            GL.UseProgram(_shaderProgram);
            //// --- MODEL MATRIX ---
            //Matrix4x4 modelMatrix = Matrix4x4.Identity;
            //int locModel = GL.GetUniformLocation(_shaderProgram, "model");
            //GL.UniformMatrix4fv(locModel, 1, false, (float*)Unsafe.AsPointer(ref modelMatrix));

            //// --- NORMAL MATRIX (3x3) ---
            //Matrix4x4.Invert(modelMatrix, out Matrix4x4 inv);
            //Matrix4x4 nm4 = Matrix4x4.Transpose(inv);
            //Helpers.Matrix3x3 normalMatrix = new Helpers.Matrix3x3(
            //    nm4.M11, nm4.M12, nm4.M13,
            //    nm4.M21, nm4.M22, nm4.M23,
            //    nm4.M31, nm4.M32, nm4.M33
            //);

            //int locNormal = GL.GetUniformLocation(_shaderProgram, "normalMatrix");
            //GL.UniformMatrix3fv(locNormal, 1, false, (float*)Unsafe.AsPointer(ref normalMatrix));


            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
            GL.UniformMatrix4fv(_projLoc, 1, false, (float*)Unsafe.AsPointer(ref proj)); 

            GL.Uniform3f(_sunDirLoc,     light.SunDir.X,     light.SunDir.Y,     light.SunDir.Z);
            GL.Uniform3f(_lightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(_fogColorLoc,   light.FogColor.X,   light.FogColor.Y,   light.FogColor.Z);
            GL.Uniform3f(_viewPosLoc,    camera.Position.X,  camera.Position.Y,  camera.Position.Z);
            GL.Uniform1i(_useFogLoc, Inputs.Keyboard.GetIsFogActive() ? 1 : 0); 

            var frustum = ExtractFrustumPlanes(Matrix4x4.Multiply(view, proj));

            foreach (var obj in _objects)
            {
                if (!DisableFrustumCull && !IsAABBInFrustum(frustum, obj.WorldAABB))
                {
                    CulledObjects++;
                    continue;
                }

                // ─── SEND JOINT MATRICES ───
                // Joint matrices are System.Numerics row-vector matrices. Upload with
                // transpose=FALSE (same as the `model`/`view`/`projection` uniforms):
                // OpenGL then reads the row-major storage column-major, which yields
                // the column-vector skinning matrix the GLSL shader expects. Using
                // transpose=TRUE keeps the row-vector form, i.e. the transpose of the
                // correct matrix — invisible at bind pose but it explodes the mesh
                // (spikes/detached blobs) as soon as a joint rotates.
                var joints = obj.GetJointMatrices();
                if (joints != null && joints.Length > 0 && _jointsLoc >= 0)
                {
                    fixed (Matrix4x4* p = &joints[0])
                        GL.UniformMatrix4fv(_jointsLoc, joints.Length, false, (float*)p);
                }

                obj.Draw(_modelLoc, _baseColorFactorLoc, _useAlbedoLoc, _albedoMapLoc);
                DrawnObjects++;
            }
        }

        // -----------------------------------------------------------------------
        //  Draw a small screen-space health bar above each living character.
        public void DrawHealthBars(Camera camera, HUD hud)
        {
            const float headHeight = 2.1f;   // above the feet origin
            var vp = Matrix4x4.Multiply(camera.GetViewMatrix(), camera.GetProjectionMatrix());

            for (int i = 0; i < _objects.Count && i < _agents.Count; i++)
            {
                var ag = _agents[i];
                if (ag.Dead) continue;

                var head = _objects[i].Position + new Vector3(0f, headHeight, 0f);
                if ((head - camera.Position).LengthSquared() > 90f * 90f) continue;   // too far to bother

                var clip = Vector4.Transform(new Vector4(head, 1f), vp);
                if (clip.W <= 0.05f) continue;                                        // behind the camera
                float nx = clip.X / clip.W, ny = clip.Y / clip.W;
                if (nx < -1.1f || nx > 1.1f || ny < -1.1f || ny > 1.1f) continue;     // off screen

                float sx = (nx * 0.5f + 0.5f) * Glfw.WindowWidth;
                float sy = (1f - (ny * 0.5f + 0.5f)) * Glfw.WindowHeight;

                float bw = 46f, bh = 6f;
                float x = sx - bw * 0.5f, y = sy - 6f;
                float hp = Math.Clamp(ag.Health / CharacterAgent.MaxHealth, 0f, 1f);

                hud.DrawBox(x - 1f, y - 1f, bw + 2f, bh + 2f, new Vector3(0f, 0f, 0f));      // border
                hud.DrawBox(x, y, bw, bh, new Vector3(0.18f, 0.18f, 0.18f));                 // background
                var col = hp > 0.5f  ? new Vector3(0.15f, 0.8f, 0.15f)
                        : hp > 0.25f ? new Vector3(0.9f, 0.75f, 0.1f)
                        :              new Vector3(0.9f, 0.15f, 0.15f);
                hud.DrawBox(x, y, bw * hp, bh, col);                                         // fill
            }
        }

        // -----------------------------------------------------------------------
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
        }

        // -----------------------------------------------------------------------
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
