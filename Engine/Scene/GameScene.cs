using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>
    /// Main game scene. Contains the full game loop logic (input, physics, AI, rendering, HUD).
    /// Created by LoadingScene after all resources are loaded.
    /// </summary>
    public unsafe class GameScene : IScene
    {
        public string Name => "GameScene";

        // ── Injected resources (set by LoadingScene or Program) ──
        private Camera _camera;
        private Lights _light;
        private Texture[]? _skyTextures;
        private TerrainChunk? _gameTerrainChunk;
        private Skybox? _skybox;
        private HUD? _hud;
        private ObjectManager? _objectManager;

        // ── Render state (initialized in Enter) ──
        private PostProcessStack? _ppStack;
        private CSM? _csm;
        private OcclusionCulling? _occlusionCulling;
        private HiZOcc? _hizOcc;

        // Shader uniform locations (terrain)
        private uint _terrainShader;
        private int _terrainShadowMap0Loc, _terrainShadowMap1Loc, _terrainShadowMap2Loc;
        private int _terrainLightSpaceLoc0, _terrainLightSpaceLoc1, _terrainLightSpaceLoc2;
        private int _terrainCascadeEndsLoc0, _terrainCascadeEndsLoc1, _terrainCascadeEndsLoc2;

        // Shader uniform locations (gltf)
        private uint _gltfShader;
        private int _gltfShadowMap0Loc, _gltfShadowMap1Loc, _gltfShadowMap2Loc;
        private int _gltfLightSpaceLoc0, _gltfLightSpaceLoc1, _gltfLightSpaceLoc2;
        private int _gltfCascadeEndsLoc0, _gltfCascadeEndsLoc1, _gltfCascadeEndsLoc2;

        // Shadow shader uniform locations
        private uint _shadowShader;
        private int _shadowModelLoc, _shadowLightSpaceLoc;
        private uint _shadowSkinnedShader;
        private int _shadowSkinnedModelLoc, _shadowSkinnedLightSpaceLoc, _shadowSkinnedJointsLoc;
        private uint _shadowStaticAlphaShader;
        private int _shadowStaticAlphaModelLoc, _shadowStaticAlphaLightSpaceLoc;

        // Main shader view/projection locations
        private uint _shaderProgram;
        private int _projectionLocation, _viewLocation;

        // ── Per-frame state ──
        private float _time = 0f;
        private float _deltaTime = 0f;
        private int _occlusionFrameCount = 0;
        private CameraMode _lastCameraMode = CameraMode.FirstPerson;
        private float _lastTargetShoulderOffset;

        // ── FPS counter ──
        private int _renderedTris;

        public GameScene(Camera camera, Lights light)
        {
            _camera = camera;
            _light = light;
        }

        /// <summary>
        /// Called by LoadingScene after all assets are loaded to pass resources to GameScene.
        /// </summary>
        public void SetResources(
            Texture[] skyTextures,
            TerrainChunk terrain,
            Skybox skybox,
            HUD hud,
            ObjectManager objectManager)
        {
            _skyTextures = skyTextures;
            _gameTerrainChunk = terrain;
            _skybox = skybox;
            _hud = hud;
            _objectManager = objectManager;
        }

        public void Enter()
        {
            if (_skyTextures == null || _gameTerrainChunk == null || _skybox == null || _hud == null || _objectManager == null)
            {
                Console.Error.WriteLine("[GameScene] Error: Resources not set before Enter()!");
                return;
            }

            _shaderProgram = Shader.GetShaderProgram();
            _projectionLocation = GL.GetUniformLocation(_shaderProgram, "projection");
            _viewLocation = GL.GetUniformLocation(_shaderProgram, "view");

            // ── Post-processing ──
            _ppStack = new PostProcessStack(Glfw.WindowWidth, Glfw.WindowHeight);
            var invertPass = new InvertPass(Shader.GetInvertPassShaderProgram());
            // ppStack.AddPass(invertPass);

            // ── CSM ──
            _csm = new CSM(Config.ShadowConfig.CascadeSizes[0]);

            // Cache terrain shader uniform locations
            _terrainShader = Shader.GetShaderProgram();
            _terrainShadowMap0Loc = GL.GetUniformLocation(_terrainShader, "shadowMap0");
            _terrainShadowMap1Loc = GL.GetUniformLocation(_terrainShader, "shadowMap1");
            _terrainShadowMap2Loc = GL.GetUniformLocation(_terrainShader, "shadowMap2");
            _terrainLightSpaceLoc0 = GL.GetUniformLocation(_terrainShader, "lightSpaceMatrices[0]");
            _terrainLightSpaceLoc1 = GL.GetUniformLocation(_terrainShader, "lightSpaceMatrices[1]");
            _terrainLightSpaceLoc2 = GL.GetUniformLocation(_terrainShader, "lightSpaceMatrices[2]");
            _terrainCascadeEndsLoc0 = GL.GetUniformLocation(_terrainShader, "cascadeEnds[0]");
            _terrainCascadeEndsLoc1 = GL.GetUniformLocation(_terrainShader, "cascadeEnds[1]");
            _terrainCascadeEndsLoc2 = GL.GetUniformLocation(_terrainShader, "cascadeEnds[2]");

            // Cache gltf shader uniform locations
            _gltfShader = GltfShader.GetShaderProgram();
            _gltfShadowMap0Loc = GL.GetUniformLocation(_gltfShader, "shadowMap0");
            _gltfShadowMap1Loc = GL.GetUniformLocation(_gltfShader, "shadowMap1");
            _gltfShadowMap2Loc = GL.GetUniformLocation(_gltfShader, "shadowMap2");
            _gltfLightSpaceLoc0 = GL.GetUniformLocation(_gltfShader, "lightSpaceMatrices[0]");
            _gltfLightSpaceLoc1 = GL.GetUniformLocation(_gltfShader, "lightSpaceMatrices[1]");
            _gltfLightSpaceLoc2 = GL.GetUniformLocation(_gltfShader, "lightSpaceMatrices[2]");
            _gltfCascadeEndsLoc0 = GL.GetUniformLocation(_gltfShader, "cascadeEnds[0]");
            _gltfCascadeEndsLoc1 = GL.GetUniformLocation(_gltfShader, "cascadeEnds[1]");
            _gltfCascadeEndsLoc2 = GL.GetUniformLocation(_gltfShader, "cascadeEnds[2]");

            // Cache shadow shader uniform locations
            _shadowShader = Shader.GetShadowShaderProgram();
            _shadowModelLoc = GL.GetUniformLocation(_shadowShader, "model");
            _shadowLightSpaceLoc = GL.GetUniformLocation(_shadowShader, "lightSpaceMatrix");

            _shadowSkinnedShader = Shader.GetShadowSkinnedShaderProgram();
            _shadowSkinnedModelLoc = GL.GetUniformLocation(_shadowSkinnedShader, "model");
            _shadowSkinnedLightSpaceLoc = GL.GetUniformLocation(_shadowSkinnedShader, "lightSpaceMatrix");
            _shadowSkinnedJointsLoc = GL.GetUniformLocation(_shadowSkinnedShader, "u_Joints");

            _shadowStaticAlphaShader = Shader.GetShadowStaticAlphaShaderProgram();
            _shadowStaticAlphaModelLoc = GL.GetUniformLocation(_shadowStaticAlphaShader, "model");
            _shadowStaticAlphaLightSpaceLoc = GL.GetUniformLocation(_shadowStaticAlphaShader, "lightSpaceMatrix");

            // ── Occlusion Culling ──
            _occlusionCulling = new OcclusionCulling();
            if (Config.OcclusionConfig.Mode == OcclusionMode.HiZ)
            {
                _hizOcc = new HiZOcc();
            }

            // Register all animated objects for occlusion testing
            if (_objectManager != null)
            {
                for (int ai = 0; ai < _objectManager.GetObjects().Count; ai++)
                    _occlusionCulling.RegisterObject();
            }

            // Subscribe to window resize event for camera aspect ratio updates
            Glfw.OnWindowResized += OnWindowResized;

            _lastCameraMode = _camera.CurrentMode;
            _lastTargetShoulderOffset = CameraConfig.TargetShoulderOffset;

            Mouse.ShowMouse(false);

            Console.WriteLine("[GameScene] Engine Running...");
        }

        /// <summary>Handle window resize — update viewport and camera aspect ratio.</summary>
        private void OnWindowResized(int width, int height)
        {
            _camera.UpdateAspectRatio((float)width, (float)height);
        }

        public void Update(float deltaTime)
        {
            nint window = Glfw.GetWindow();
            _deltaTime = deltaTime;
            _time += deltaTime;

            // ── Camera mode / freelook ──
            if (_camera.CurrentMode == CameraMode.FirstPerson)
            {
                _camera.freeLook = false;
            }
            else
            {
                _camera.freeLook =
                    (Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT_ALT) ||
                     Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT_ALT));
            }

            // 1. Mouse → yaw/pitch → vectors
            Mouse.Update(window, _camera);
            _camera.UpdateVectors();

            CameraConfig.TargetShoulderOffset = _lastTargetShoulderOffset;

            // 2. Third-person keeps ALT free-look
            if (!_camera.freeLook && _objectManager != null)
                _objectManager.PlayerAgent.Heading = _camera.Yaw;

            // 3. Update keyboard
            Keyboard.Update(window, _light, _camera, deltaTime, _gameTerrainChunk);

            // 3A. Check if camera mode changed and notify player
            if (_camera.CurrentMode != _lastCameraMode && _objectManager != null)
            {
                _lastCameraMode = _camera.CurrentMode;
                _objectManager.PlayerAgent.OnCameraModeChanged(_camera.CurrentMode);
            }

            if (_objectManager != null)
            {
                // 4. Update agents
                _objectManager.Update(deltaTime);

                // 4A. Update player movement
                _objectManager.PlayerAgent.Move(window, _camera, deltaTime, _gameTerrainChunk, Vector3.Zero, 0f);

                // ── COLLISION: push player out of static objects ──
                var staticMgrs = _objectManager.staticObjectManagers;
                var playerPos = _objectManager.PlayerAgent.Position;
                var pushedPlayer = Helpers.CollisionHelper.PushCharacter(playerPos, staticMgrs);
                if (pushedPlayer != playerPos)
                {
                    pushedPlayer.Y = _gameTerrainChunk.GetHeightAt(pushedPlayer.X, pushedPlayer.Z);
                    _objectManager.PlayerAgent.Position = pushedPlayer;
                }

                // 4B. Update NPC AI + movement
                _objectManager.UpdateAgents(window, deltaTime, _gameTerrainChunk, _camera);

                // ── COLLISION: push NPC out of static objects ──
                var allObjs = _objectManager.GetObjects();
                for (int oi = 0; oi < allObjs.Count; oi++)
                {
                    if (allObjs[oi].IsPlayer) continue;
                    var npcPos = allObjs[oi].Position;
                    var pushedNpc = Helpers.CollisionHelper.PushCharacter(npcPos, staticMgrs);
                    if (pushedNpc != npcPos)
                    {
                        pushedNpc.Y = _gameTerrainChunk.GetHeightAt(pushedNpc.X, pushedNpc.Z);
                        allObjs[oi].Position = pushedNpc;
                    }
                }

                // ── COLLISION: player vs AI characters ──
                {
                    var pPos = _objectManager.PlayerAgent.Position;
                    for (int oi = 0; oi < allObjs.Count; oi++)
                    {
                        if (allObjs[oi].IsPlayer) continue;
                        var aiPos = allObjs[oi].Position;

                        float dx = pPos.X - aiPos.X;
                        float dz = pPos.Z - aiPos.Z;
                        float distSq = dx * dx + dz * dz;
                        float minDist = CharacterAgent.CollisionRadius + CharacterAgent.CollisionRadius;

                        if (distSq >= minDist * minDist) continue;

                        float dist = MathF.Sqrt(distSq);
                        float nx, nz;
                        if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                        else { nx = 1f; nz = 0f; }

                        float push = (minDist - dist) * 0.5f;
                        pPos.X += nx * push;
                        pPos.Z += nz * push;
                        aiPos.X -= nx * push;
                        aiPos.Z -= nz * push;

                        pPos.Y = _gameTerrainChunk.GetHeightAt(pPos.X, pPos.Z);
                        aiPos.Y = _gameTerrainChunk.GetHeightAt(aiPos.X, aiPos.Z);

                        allObjs[oi].Position = aiPos;
                    }
                    _objectManager.PlayerAgent.Position = pPos;
                }

                // ── RE-CHECK: player vs static objects ──
                {
                    var recheckPos = _objectManager.PlayerAgent.Position;
                    var recheckPushed = Helpers.CollisionHelper.PushCharacter(recheckPos, staticMgrs);
                    if (recheckPushed != recheckPos)
                    {
                        recheckPushed.Y = _gameTerrainChunk.GetHeightAt(recheckPushed.X, recheckPushed.Z);
                        _objectManager.PlayerAgent.Position = recheckPushed;
                    }
                }

                // 5. Set Camera orbital (with terrain + wall collision)
                _camera.SetCamera(window, _objectManager.PlayerAgent.Position, _gameTerrainChunk, deltaTime, staticMgrs);

                // Safety net: push Position directly without syncing smoothCamPos
                var camPos = _camera.Position;
                var safeCamPos = Helpers.CollisionHelper.PushCamera(camPos, staticMgrs);
                if (safeCamPos != camPos)
                    _camera.Position = safeCamPos;
            }

            // 6. Update Light
            _light.Update(deltaTime, _camera.Position);

            // ── CSM Shadow Pass ──
            if (_csm != null)
            {
                _csm.UpdateMatrices(_camera, _light.ShadowDirStable);

                for (int i = 0; i < CSM.NumCascades; i++)
                {
                    _csm.BindFramebuffer(i);

                    Matrix4x4 lightSpace = _csm.LightSpaceMatrices[i];

                    GL.UseProgram(_shadowShader);
                    unsafe
                    {
                        GL.UniformMatrix4fv(_shadowLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    GL.UseProgram(_shadowSkinnedShader);
                    unsafe
                    {
                        GL.UniformMatrix4fv(_shadowSkinnedLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    GL.UseProgram(_shadowStaticAlphaShader);
                    unsafe
                    {
                        GL.UniformMatrix4fv(_shadowStaticAlphaLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    if (_objectManager != null)
                    {
                        _objectManager.RenderShadow(_camera, _csm, i,
                            _shadowSkinnedShader, _shadowSkinnedModelLoc, _shadowSkinnedJointsLoc,
                            _shadowStaticAlphaShader, _shadowStaticAlphaModelLoc);
                    }

                    if (_gameTerrainChunk != null)
                    {
                        _gameTerrainChunk.RenderShadow(_camera, _csm, i, _shadowShader, _shadowModelLoc);
                    }
                }

                // Restore default viewport and framebuffer
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
            }

            // ── Occlusion Culling ──
            if (Config.OcclusionConfig.UseOcclusion && _objectManager != null && _gameTerrainChunk != null)
            {
                _occlusionFrameCount++;

                bool useHiZ = (Config.OcclusionConfig.Mode == OcclusionMode.HiZ && _hizOcc != null);

                // Phase 0: Generate Hi-Z depth buffer
                if (useHiZ)
                    _hizOcc!.GenerateTerrainDepth(_camera, _gameTerrainChunk);

                // Phase 1: Register occluders
                _occlusionCulling.ClearOccluders();
                if (useHiZ)
                    _hizOcc!.ClearOccluders();

                if (_objectManager.staticObjectManagers != null)
                {
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Length; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                            sobj.IsVisible = true;
                    }
                }

                var animObjs = _objectManager.GetObjects();
                for (int oi = 0; oi < animObjs.Count; oi++)
                    animObjs[oi].IsVisible = true;

                // 1B. Register static objects as occluders
                if (_objectManager.staticObjectManagers != null)
                {
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Length; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                        {
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                            if (distSq >= _camera.FarDist * _camera.FarDist) continue;

                            if (!useHiZ)
                            {
                                bool terrainOccluded = false;

                                if (mgr.SkipTerrainRayMarch)
                                {
                                    float nearSq = Config.OcclusionConfig.TerrainOcclusionNearDist * Config.OcclusionConfig.TerrainOcclusionNearDist;
                                    if (distSq < nearSq)
                                    {
                                        var aabb = sobj.CachedWorldAABB;
                                        Vector3 bottomCenter = new(
                                            (aabb.Min.X + aabb.Max.X) * 0.5f,
                                            aabb.Min.Y,
                                            (aabb.Min.Z + aabb.Max.Z) * 0.5f
                                        );

                                        float camTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                                        if (_camera.Position.Y >= camTerrainH - 0.5f)
                                        {
                                            Vector3 dir = bottomCenter - _camera.Position;
                                            float totalDist = dir.Length();
                                            if (totalDist >= 0.5f)
                                            {
                                                dir /= totalDist;
                                                const int numSamples = 8;
                                                float stepSize = totalDist / numSamples;
                                                for (int s = 1; s < numSamples; s++)
                                                {
                                                    Vector3 samplePos = _camera.Position + dir * (s * stepSize);
                                                    float terrainH = _gameTerrainChunk.GetHeightAt(samplePos.X, samplePos.Z);
                                                    if (samplePos.Y < terrainH - 0.3f)
                                                    {
                                                        terrainOccluded = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Vector3 midPoint = _camera.Position + (sobj.Position - _camera.Position) * 0.5f;
                                        float midTerrainH = _gameTerrainChunk.GetHeightAt(midPoint.X, midPoint.Z);
                                        float camTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                                        if (_camera.Position.Y >= camTerrainH - 0.5f && midPoint.Y < midTerrainH - 0.5f)
                                            terrainOccluded = true;
                                    }
                                }
                                else
                                {
                                    var aabb = sobj.CachedWorldAABB;
                                    Vector3[] corners = new Vector3[8]
                                    {
                                        new(aabb.Min.X, aabb.Min.Y, aabb.Min.Z),
                                        new(aabb.Max.X, aabb.Min.Y, aabb.Min.Z),
                                        new(aabb.Max.X, aabb.Max.Y, aabb.Min.Z),
                                        new(aabb.Min.X, aabb.Max.Y, aabb.Min.Z),
                                        new(aabb.Min.X, aabb.Min.Y, aabb.Max.Z),
                                        new(aabb.Max.X, aabb.Min.Y, aabb.Max.Z),
                                        new(aabb.Max.X, aabb.Max.Y, aabb.Max.Z),
                                        new(aabb.Min.X, aabb.Max.Y, aabb.Max.Z),
                                    };

                                    float camTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                                    bool allCornersBehindTerrain = true;

                                    for (int ci = 0; ci < 8; ci++)
                                    {
                                        Vector3 cornerPos = corners[ci];
                                        Vector3 dir = cornerPos - _camera.Position;
                                        float totalDist = dir.Length();
                                        if (totalDist < 0.5f) { allCornersBehindTerrain = false; break; }
                                        dir /= totalDist;

                                        if (_camera.Position.Y < camTerrainH - 0.5f) { allCornersBehindTerrain = false; break; }

                                        const int numSamples = 8;
                                        float stepSize = totalDist / numSamples;
                                        bool cornerBehindTerrain = false;
                                        for (int s = 1; s < numSamples; s++)
                                        {
                                            Vector3 samplePos = _camera.Position + dir * (s * stepSize);
                                            float terrainH = _gameTerrainChunk.GetHeightAt(samplePos.X, samplePos.Z);
                                            if (samplePos.Y < terrainH - 0.3f)
                                            {
                                                cornerBehindTerrain = true;
                                                break;
                                            }
                                        }

                                        if (!cornerBehindTerrain)
                                        {
                                            allCornersBehindTerrain = false;
                                            break;
                                        }
                                    }

                                    if (allCornersBehindTerrain)
                                        terrainOccluded = true;
                                }

                                if (terrainOccluded)
                                {
                                    sobj.IsVisible = false;
                                    continue;
                                }
                            }

                            if (sobj.IsOccluder)
                            {
                                _occlusionCulling.RegisterOccluder(sobj.CachedWorldAABB);
                                if (useHiZ)
                                    _hizOcc!.RegisterOccluder(sobj.CachedWorldAABB);
                            }
                        }
                    }
                }

                // Phase 2: Test all objects against occluders
                var objectAABBs = new Helpers.ObjectHelpers.AABB[animObjs.Count];
                for (int oi = 0; oi < animObjs.Count; oi++)
                    objectAABBs[oi] = animObjs[oi].WorldAABB;

                _occlusionCulling.CheckVisibility(_camera.Position, objectAABBs);

                for (int oi = 0; oi < animObjs.Count; oi++)
                    animObjs[oi].IsVisible = _occlusionCulling.IsVisible(oi);

                // 2B. Test static objects against occluders
                if (_objectManager.staticObjectManagers != null)
                {
                    float farSq = _camera.FarDist * _camera.FarDist;

                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Length; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                        {
                            if (!sobj.IsVisible) continue;
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                            if (distSq > farSq) continue;

                            bool occluded;
                            if (useHiZ)
                            {
                                occluded = _hizOcc!.IsTerrainOccluded(_camera.Position, sobj.CachedWorldAABB);
                                if (!occluded)
                                    occluded = _hizOcc!.IsOccluded(_camera.Position, sobj.CachedWorldAABB);
                            }
                            else
                            {
                                occluded = _occlusionCulling.IsOccludedByOccluders(_camera.Position, sobj.CachedWorldAABB);
                            }

                            if (occluded)
                                sobj.IsVisible = false;
                        }
                    }
                }

                // Phase 3: Terrain height ray-marching for animated objects
                for (int oi = 0; oi < animObjs.Count; oi++)
                {
                    if (animObjs[oi].IsPlayer) continue;
                    if (!animObjs[oi].IsVisible) continue;

                    var aabb = animObjs[oi].WorldAABB;
                    Vector3[] corners = new Vector3[8]
                    {
                        new(aabb.Min.X, aabb.Min.Y, aabb.Min.Z),
                        new(aabb.Max.X, aabb.Min.Y, aabb.Min.Z),
                        new(aabb.Max.X, aabb.Max.Y, aabb.Min.Z),
                        new(aabb.Min.X, aabb.Max.Y, aabb.Min.Z),
                        new(aabb.Min.X, aabb.Min.Y, aabb.Max.Z),
                        new(aabb.Max.X, aabb.Min.Y, aabb.Max.Z),
                        new(aabb.Max.X, aabb.Max.Y, aabb.Max.Z),
                        new(aabb.Min.X, aabb.Max.Y, aabb.Max.Z),
                    };

                    float camTerrainH2 = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                    if (_camera.Position.Y < camTerrainH2 - 0.5f) continue;

                    bool allCornersBehindTerrain = true;

                    for (int ci = 0; ci < 8; ci++)
                    {
                        Vector3 cornerPos = corners[ci];
                        Vector3 dir2 = cornerPos - _camera.Position;
                        float totalDist2 = dir2.Length();
                        if (totalDist2 < 0.5f) { allCornersBehindTerrain = false; break; }
                        dir2 /= totalDist2;

                        const int numSamples2 = 16;
                        float stepSize2 = totalDist2 / numSamples2;
                        bool cornerOccluded = false;

                        int startSample = Math.Max(1, numSamples2 / 10);
                        for (int s = startSample; s < numSamples2; s++)
                        {
                            float t = s * stepSize2;
                            Vector3 samplePos = _camera.Position + dir2 * t;
                            float terrainH = _gameTerrainChunk.GetHeightAt(samplePos.X, samplePos.Z);

                            if (samplePos.Y < terrainH - 0.5f)
                            {
                                cornerOccluded = true;
                                break;
                            }
                        }

                        if (!cornerOccluded)
                        {
                            allCornersBehindTerrain = false;
                            break;
                        }
                    }

                    if (allCornersBehindTerrain)
                        animObjs[oi].IsVisible = false;
                }

                // Player always visible
                _objectManager.PlayerObject.IsVisible = true;
            }
        }

        public void Render()
        {
            if (_ppStack == null || _csm == null || _skybox == null || _hud == null) return;

            // ── MAIN RENDER PASS ──
            _ppStack.BindSceneFBO();
            GL.ClearColor(0.07f, 0.13f, 0.17f, 1.0f);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);

            // 3. Draw Skybox
            _skybox.Draw(_camera, _light, 0f, _skyTextures!, _gameTerrainChunk);

            // ── Bind CSM Shadow Maps ──
            GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
            GL.BindTexture(Const.GL_TEXTURE_2D, _csm.ShadowTextures[0]);

            GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
            GL.BindTexture(Const.GL_TEXTURE_2D, _csm.ShadowTextures[1]);

            GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
            GL.BindTexture(Const.GL_TEXTURE_2D, _csm.ShadowTextures[2]);

            // Upload shadow uniforms for Terrain
            GL.UseProgram(_terrainShader);
            GL.Uniform1i(_terrainShadowMap0Loc, 6);
            GL.Uniform1i(_terrainShadowMap1Loc, 7);
            GL.Uniform1i(_terrainShadowMap2Loc, 8);

            unsafe
            {
                fixed (float* p0 = &_csm.LightSpaceMatrices[0].M11)
                    GL.UniformMatrix4fv(_terrainLightSpaceLoc0, 1, false, p0);
                fixed (float* p1 = &_csm.LightSpaceMatrices[1].M11)
                    GL.UniformMatrix4fv(_terrainLightSpaceLoc1, 1, false, p1);
                fixed (float* p2 = &_csm.LightSpaceMatrices[2].M11)
                    GL.UniformMatrix4fv(_terrainLightSpaceLoc2, 1, false, p2);
            }
            GL.Uniform1f(_terrainCascadeEndsLoc0, _csm.CascadeEnds[0]);
            GL.Uniform1f(_terrainCascadeEndsLoc1, _csm.CascadeEnds[1]);
            GL.Uniform1f(_terrainCascadeEndsLoc2, _csm.CascadeEnds[2]);

            // Upload shadow uniforms for glTF
            GL.UseProgram(_gltfShader);
            GL.Uniform1i(_gltfShadowMap0Loc, 6);
            GL.Uniform1i(_gltfShadowMap1Loc, 7);
            GL.Uniform1i(_gltfShadowMap2Loc, 8);

            unsafe
            {
                fixed (float* p0 = &_csm.LightSpaceMatrices[0].M11)
                    GL.UniformMatrix4fv(_gltfLightSpaceLoc0, 1, false, p0);
                fixed (float* p1 = &_csm.LightSpaceMatrices[1].M11)
                    GL.UniformMatrix4fv(_gltfLightSpaceLoc1, 1, false, p1);
                fixed (float* p2 = &_csm.LightSpaceMatrices[2].M11)
                    GL.UniformMatrix4fv(_gltfLightSpaceLoc2, 1, false, p2);
            }
            GL.Uniform1f(_gltfCascadeEndsLoc0, _csm.CascadeEnds[0]);
            GL.Uniform1f(_gltfCascadeEndsLoc1, _csm.CascadeEnds[1]);
            GL.Uniform1f(_gltfCascadeEndsLoc2, _csm.CascadeEnds[2]);

            // 4. Ensure terrain shader has view/projection uniforms
            _camera.SetViewAndProjection(_viewLocation, _projectionLocation);

            _renderedTris = 0;
            if (_gameTerrainChunk != null)
            {
                Plane[]? cullFreezePlanes = null;
                if (Keyboard.GetCullFreezeMode())
                {
                    var freezeVP = Keyboard.GetCullFreezeViewProj();
                    cullFreezePlanes = TerrainChunk.ExtractFrustumPlanes(freezeVP);
                }

                _renderedTris = _gameTerrainChunk.Render(_camera, _gameTerrainChunk.GetFrozenPlanes(), cullFreezePlanes);
            }

            // ── glTF Object Manager ──
            if (_objectManager != null)
            {
                _objectManager.CullFreezeEnabled = Keyboard.GetCullFreezeMode();
                _objectManager.CullFreezeViewProj = Keyboard.GetCullFreezeViewProj();

                _objectManager.Draw(_camera, _light, _csm);
                _objectManager.DrawHealthBars(_camera, _hud);
            }

            // ── Post Process ──
            _ppStack.RunStack(Glfw.WindowWidth, Glfw.WindowHeight, _time);

            // ── Debug BBox Wireframe (toggled with P key) ──
            if (Keyboard.GetShowBBox() && _objectManager != null)
            {
                GL.Disable(Const.GL_DEPTH_TEST);

                _objectManager.DrawDebugAABBs(_camera, null);

                var animObjs = _objectManager.GetObjects();
                for (int oi = 0; oi < animObjs.Count; oi++)
                {
                    var aabb = animObjs[oi].WorldAABB;
                    Vector3 color = animObjs[oi].IsPlayer ? new Vector3(0f, 1f, 0f) : new Vector3(0f, 0.5f, 1f);
                    TerrainChunk.DrawAABBWireframe(aabb, color, _camera);
                }

                if (_objectManager.staticObjectManagers != null)
                {
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Length; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;

                        foreach (var sobj in mgr.GetObjects())
                        {
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                            if (distSq >= _camera.FarDist * _camera.FarDist) continue;

                            bool isCulled = !sobj.IsVisible && Config.OcclusionConfig.UseOcclusion;
                            Vector3 debugColor = isCulled ? new Vector3(1f, 0f, 0f)
                                : (mi == 1 ? new Vector3(1f, 0f, 1f) : new Vector3(1f, 1f, 0f));

                            TerrainChunk.DrawAABBWireframe(sobj.CachedWorldAABB, debugColor, _camera);
                        }
                    }

                    var collisionColor = new Vector3(0.6f, 0f, 1f);
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Length; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                        {
                            if (sobj.CachedCollisionAABB.HasValue)
                                TerrainChunk.DrawAABBWireframe(sobj.CachedCollisionAABB.Value, collisionColor, _camera);
                        }
                    }
                }

                GL.Enable(Const.GL_DEPTH_TEST);
            }

            // ── HUD ──
            int totalMapTris = TerrainChunk.GetTotalMapTriangles();
            string gTime = _light.GetFormattedTime();

            string title1 = $"🕒 [ {gTime} ]";
            string title2 = $"⚡ FPS: {Glfw.GetLastFPS()}";
            string freeze = Keyboard.GetCullFreezeMode() ? " [CULL FREEZE]" : "";
            string title3 = $"🎥 MODE: {_camera.CurrentMode}{freeze}";
            string title4 = $"📐 TRIS: {_renderedTris:N0} / {totalMapTris:N0}";
            string title5 = $" POS: X ={_camera.Position.X:N2} Y={_camera.Position.Y:N2} Z={_camera.Position.Z:N2}";
            string title6 = "";
            if (_objectManager != null)
            {
                int culledTotal = _objectManager.TotalObjects - _objectManager.DrawnObjects;
                title6 = $" Objects: {_objectManager.DrawnObjects:N0} drawn / {culledTotal:N0} culled / {_objectManager.TotalObjects:N0} total";
            }
            string title7 = "";
            string ocMode = Inputs.Keyboard.GetOcclusionModeName();
            bool ocActive = Inputs.Keyboard.GetOcclusionCullingEnabled();
            if (ocActive && _occlusionFrameCount > 1)
            {
                int ocStaticCount = 0;
                if (_objectManager != null && _objectManager.staticObjectManagers != null)
                {
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Length; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                            if (!sobj.IsVisible) ocStaticCount++;
                    }
                }
                title7 = $" OC [{ocMode}]: {_occlusionCulling!.VisibleCount}v / {_occlusionCulling.OccludedCount}o (static {ocStaticCount}o)";
            }
            else if (ocActive)
            {
                title7 = $" OC [{ocMode}]: waiting...";
            }

            _hud.DrawText(title1, 10, 60, new Vector3(1, 0, 0));
            _hud.DrawText(title2, 10, 90, new Vector3(1, 0, 0));
            _hud.DrawText(title3, 10, 120, new Vector3(1, 1, 0));
            _hud.DrawText(title4, 10, 150, new Vector3(1, 0, 0));
            _hud.DrawText(title5, 10, 180, new Vector3(1, 0, 0));
            _hud.DrawText(title6, 10, 210, new Vector3(1, 0, 0));
            if (!string.IsNullOrEmpty(title7))
                _hud.DrawText(title7, 10, 240, new Vector3(0, 1, 1));

            // ── Update window title (FPS, etc.) ──
            Glfw.ShowFPS(_deltaTime, _renderedTris, totalMapTris, gTime);
        }

        public void Exit()
        {
            Glfw.OnWindowResized -= OnWindowResized;
            _hizOcc?.Dispose();
            _csm?.Dispose();
            Console.WriteLine("[GameScene] Exited.");
        }

        public void Dispose()
        {
            _hizOcc?.Dispose();
            _csm?.Dispose();
            _objectManager?.Dispose();
            _gameTerrainChunk?.Dispose();
            Console.WriteLine("[GameScene] Disposed.");
        }
    }
}
