using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// Manages all editor-placed 3D objects (primitives + glb references).
    /// Handles lifecycle, rendering, shadow pass, and selection.
    /// </summary>
    public unsafe class EditorObjectManager
    {
        private readonly List<EditorObject> _objects = [];
        // Per-type counters for incremental default names
        private int _boxCounter = 1;
        private int _sphereCounter = 1;
        private int _planeCounter = 1;
        private int _glbCounter = 1;
        private int _cameraCounter = 1;
        private int _lightCounter = 1;
        private int _skyCounter = 1;
        private readonly uint _shaderProgram;
        private readonly int _modelLoc, _viewLoc, _projLoc;
        private readonly int _sunDirLoc, _realSunDirLoc, _lightColorLoc, _viewPosLoc;
        private readonly int _useFogLoc, _fogColorLoc;

        // Shadow uniforms for primitive shadow rendering
        private readonly uint _shadowShader;
        private readonly int _shadowModelLoc, _shadowLightSpaceLoc;

        /// <summary>Currently selected editor object (for Inspector + gizmo).</summary>
        public EditorObject? SelectedObject { get; set; }

        /// <summary>All editor objects.</summary>
        public IReadOnlyList<EditorObject> Objects => _objects;

        /// <summary>Number of editor objects.</summary>
        public int Count => _objects.Count;

        /// <summary>Delegate to fire when an object is selected via raycast.</summary>
        public Action<EditorObject?>? OnObjectSelected;

        /// <summary>Delegate fired whenever ANY editor object is removed (any removal path,
        /// not just the primary selection) so the IDE can prune its multi-selection set.</summary>
        public Action<EditorObject>? OnObjectRemoved;

        public EditorObjectManager()
        {
            _shaderProgram = Shader.GetShaderProgram();
            _modelLoc = GL.GetUniformLocation(_shaderProgram, "model");
            _viewLoc = GL.GetUniformLocation(_shaderProgram, "view");
            _projLoc = GL.GetUniformLocation(_shaderProgram, "projection");
            _sunDirLoc = GL.GetUniformLocation(_shaderProgram, "sunDir");
            _realSunDirLoc = GL.GetUniformLocation(_shaderProgram, "realSunDir");
            _lightColorLoc = GL.GetUniformLocation(_shaderProgram, "lightColor");
            _viewPosLoc = GL.GetUniformLocation(_shaderProgram, "viewPos");
            _useFogLoc = GL.GetUniformLocation(_shaderProgram, "useFog");
            _fogColorLoc = GL.GetUniformLocation(_shaderProgram, "fogColor");

            // Shadow shader (static)
            _shadowShader = Shader.GetShadowShaderProgram();
            _shadowModelLoc = GL.GetUniformLocation(_shadowShader, "model");
            _shadowLightSpaceLoc = GL.GetUniformLocation(_shadowShader, "lightSpaceMatrix");
        }

        /// <summary>Add a new editor object.</summary>
        public EditorObject Add(EditorObject obj)
        {
            _objects.Add(obj);
            return obj;
        }

        /// <summary>Remove an editor object.</summary>
        public bool Remove(EditorObject obj)
        {
            if (SelectedObject == obj)
            {
                SelectedObject = null;
                OnObjectSelected?.Invoke(null);
            }
            obj.Dispose();
            bool removed = _objects.Remove(obj);
            if (removed)
                OnObjectRemoved?.Invoke(obj);
            return removed;
        }

        /// <summary>Move an editor object from one index to another (for drag-drop reorder).</summary>
        public void MoveObject(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _objects.Count) return;
            if (toIndex < 0 || toIndex >= _objects.Count) return;
            if (fromIndex == toIndex) return;

            var obj = _objects[fromIndex];
            _objects.RemoveAt(fromIndex);
            int insertIdx = toIndex;
            if (fromIndex < toIndex) insertIdx--;
            insertIdx = Math.Clamp(insertIdx, 0, _objects.Count);
            _objects.Insert(insertIdx, obj);
        }

        /// <summary>Remove an editor object at index.</summary>
        public void RemoveAt(int index)
        {
            if (index >= 0 && index < _objects.Count)
            {
                var obj = _objects[index];
                if (SelectedObject == obj)
                {
                    SelectedObject = null;
                    OnObjectSelected?.Invoke(null);
                }
                obj.Dispose();
                _objects.RemoveAt(index);
                OnObjectRemoved?.Invoke(obj);
            }
        }

        /// <summary>Remove all editor objects.</summary>
        public void Clear()
        {
            SelectedObject = null;
            OnObjectSelected?.Invoke(null);
            foreach (var obj in _objects)
                obj.Dispose();
            _objects.Clear();
        }

        /// <summary>Get next incremental default name for a primitive type (per-type counter).</summary>
        public string GetNextName(EditorPrimitiveType type)
        {
            return type switch
            {
                EditorPrimitiveType.Plane => $"plane{_planeCounter++}",
                EditorPrimitiveType.Box => $"box{_boxCounter++}",
                EditorPrimitiveType.Sphere => $"sphere{_sphereCounter++}",
                EditorPrimitiveType.GlbReference => $"glb{_glbCounter++}",
                EditorPrimitiveType.Camera => $"camera{_cameraCounter++}",
                EditorPrimitiveType.Light => $"light{_lightCounter++}",
                EditorPrimitiveType.Sky => $"sky{_skyCounter++}",
                _ => $"object{_boxCounter++}",
            };
        }

        /// <summary>Create and add a primitive editor object at the given position.</summary>
        public EditorObject AddPrimitive(EditorPrimitiveType type, Vector3 position)
        {
            var obj = new EditorObject(type)
            {
                Position = position,
                Scale = type == EditorPrimitiveType.Sphere ? new Vector3(1f, 1f, 1f)
                       : type == EditorPrimitiveType.Plane ? new Vector3(25f, 0.05f, 25f)
                       : type == EditorPrimitiveType.Camera ? new Vector3(0.5f, 0.4f, 0.6f)
                       : Vector3.One, // Box / Light / Sky
                Color = type switch
                {
                    EditorPrimitiveType.Plane => new Vector3(0.3f, 0.7f, 0.3f),
                    EditorPrimitiveType.Box => new Vector3(0.7f, 0.3f, 0.3f),
                    EditorPrimitiveType.Sphere => new Vector3(0.3f, 0.3f, 0.7f),
                    EditorPrimitiveType.Camera => new Vector3(0.2f, 0.7f, 0.8f),
                    EditorPrimitiveType.Light => new Vector3(1.0f, 0.85f, 0.3f),
                    EditorPrimitiveType.Sky => new Vector3(0.5f, 0.7f, 1.0f),
                    _ => new Vector3(0.8f, 0.8f, 0.8f),
                }
            };
            // New planes spawn as advanced terrain by default (the whole point of the
            // feature) — every setting stays editable in the Inspector, and terrain can be
            // toggled off to get back a plain flat plane.
            if (type == EditorPrimitiveType.Plane)
            {
                obj.TerrainEnabled = true;
                obj.TerrainHeightmapPath = "Artifacts/Maps/photoreal_v1.raw";
                obj.TerrainTextureDirtPath = "Artifacts/Textures/aerial rock/aerial_rocks_04_diff_4k.jpg";
                obj.TerrainTextureGrassPath = "Artifacts/Textures/aerial grass/aerial_grass_rock_diff_4k.jpg";
                obj.TerrainTextureSnowPath = "Artifacts/Textures/snow/snow_01_diff_4k.jpg";
            }

            if (type != EditorPrimitiveType.GlbReference)
                obj.InitGPU();
            _objects.Add(obj);
            return obj;
        }

        /// <summary>Add a glb reference object.</summary>
        public EditorObject AddGlbReference(string glbPath, Vector3 position)
        {
            var obj = new EditorObject(EditorPrimitiveType.GlbReference)
            {
                Position = position,
                GlbFilePath = glbPath,
                Scale = Vector3.One,
                Color = new Vector3(1f, 1f, 1f),
            };
            _objects.Add(obj);
            return obj;
        }

        /// <summary>Duplicate an editor object (all properties copied, offset slightly in X) and add it to the scene.</summary>
        public EditorObject Duplicate(EditorObject source)
        {
            var clone = new EditorObject(source.PrimitiveType)
            {
                Name = GetNextName(source.PrimitiveType),
                Position = source.Position + new Vector3(1f, 0f, 0f),
                RotationEuler = source.RotationEuler,
                Scale = source.Scale,
                Color = source.Color,
                TexturePath = source.TexturePath,
                CastShadow = source.CastShadow,
                IsVisible = source.IsVisible,
                GlbFilePath = source.GlbFilePath,
                CameraFov = source.CameraFov,
                CameraNear = source.CameraNear,
                CameraFar = source.CameraFar,
                LightDirection = source.LightDirection,
                LightIntensity = source.LightIntensity,
                SkyTimeOfDay = source.SkyTimeOfDay,
                SkySunPitch = source.SkySunPitch,
                SkySunYaw = source.SkySunYaw,
                SkyCloudCoverage = source.SkyCloudCoverage,
                SkySunIntensity = source.SkySunIntensity,
                SkyTimeAnimSpeed = source.SkyTimeAnimSpeed,
                SkyTimeAnimPaused = source.SkyTimeAnimPaused,
                ShowFrustum = source.ShowFrustum,
                ShowLightGizmo = source.ShowLightGizmo,
                ShowSkyGizmo = source.ShowSkyGizmo,
                GizmoPivotOverride = null,
                // ── Terrain (Plane) ──
                TerrainEnabled = source.TerrainEnabled,
                TerrainHeightmapPath = source.TerrainHeightmapPath,
                TerrainChunkSize = source.TerrainChunkSize,
                TerrainHeightScale = source.TerrainHeightScale,
                TerrainSlopeThreshold = source.TerrainSlopeThreshold,
                TerrainTexTiling = source.TerrainTexTiling,
                TerrainLayerAirTop = source.TerrainLayerAirTop,
                TerrainLayerDirtTop = source.TerrainLayerDirtTop,
                TerrainLayerGrassTop = source.TerrainLayerGrassTop,
                TerrainLayerSnowTop = source.TerrainLayerSnowTop,
                TerrainTextureAirPath = source.TerrainTextureAirPath,
                TerrainTextureDirtPath = source.TerrainTextureDirtPath,
                TerrainTextureGrassPath = source.TerrainTextureGrassPath,
                TerrainTextureSnowPath = source.TerrainTextureSnowPath,
                TerrainBrushSize = source.TerrainBrushSize,
                TerrainBrushStrength = source.TerrainBrushStrength,
                TerrainBrushSoftness = source.TerrainBrushSoftness,
                TerrainBrushFalloff = source.TerrainBrushFalloff,
                TerrainPaintedData = source.TerrainPaintedData,
                TerrainPaintLayerIndex = source.TerrainPaintLayerIndex,
                TerrainPaintStrength = source.TerrainPaintStrength,
                TerrainSplatData = source.TerrainSplatData,
            };
            if (clone.PrimitiveType != EditorPrimitiveType.GlbReference)
                clone.InitGPU();
            _objects.Add(clone);
            return clone;
        }

        /// <summary>Draw all editor objects (called from the game scene rendering loop).
        /// If <paramref name="wireframeColor"/> is set and objects are selected,
        /// every selected object is also rendered as a wireframe line outline in that color
        /// (multi-select supported via <paramref name="selectedObjects"/>).
        /// </summary>
        public void Draw(Camera camera, Lights light, CSM? csm = null, Vector3? wireframeColor = null,
            IReadOnlyCollection<EditorObject>? selectedObjects = null)
        {
            if (_objects.Count == 0) return;

            GL.UseProgram(_shaderProgram);

            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(_projLoc, 1, false, (float*)&proj);

            GL.Uniform3f(_sunDirLoc, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
            if (_realSunDirLoc >= 0) GL.Uniform3f(_realSunDirLoc, light.RealSunDir.X, light.RealSunDir.Y, light.RealSunDir.Z);
            GL.Uniform3f(_lightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(_viewPosLoc, camera.Position.X, camera.Position.Y, camera.Position.Z);
            GL.Uniform1i(_useFogLoc, Inputs.Keyboard.GetIsFogActive() ? 1 : 0);
            GL.Uniform3f(_fogColorLoc, light.FogColor.X, light.FogColor.Y, light.FogColor.Z);

            // ── Send shadow uniforms if CSM provided ──
            if (csm != null)
            {
                int shadowMap0Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap0");
                int shadowMap1Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap1");
                int shadowMap2Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap2");
                int lightSpaceLoc0 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[0]");
                int lightSpaceLoc1 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[1]");
                int lightSpaceLoc2 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[2]");
                int cascadeEndsLoc0 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[0]");
                int cascadeEndsLoc1 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[1]");
                int cascadeEndsLoc2 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[2]");

                GL.Uniform1i(shadowMap0Loc, 6);
                GL.Uniform1i(shadowMap1Loc, 7);
                GL.Uniform1i(shadowMap2Loc, 8);
                unsafe
                {
                    fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                        GL.UniformMatrix4fv(lightSpaceLoc0, 1, false, p0);
                    fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                        GL.UniformMatrix4fv(lightSpaceLoc1, 1, false, p1);
                    fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                        GL.UniformMatrix4fv(lightSpaceLoc2, 1, false, p2);
                }
                GL.Uniform1f(cascadeEndsLoc0, csm.CascadeEnds[0]);
                GL.Uniform1f(cascadeEndsLoc1, csm.CascadeEnds[1]);
                GL.Uniform1f(cascadeEndsLoc2, csm.CascadeEnds[2]);
            }

            GL.Enable(Const.GL_DEPTH_TEST);
            // Face culling/winding state is applied by the scene's RenderProperties
            // (see OpenGL.ApplySceneProperties) — do NOT force-enable culling here.

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                obj.Draw(
                    _modelLoc, _viewLoc, _projLoc,
                    _sunDirLoc, _lightColorLoc, _viewPosLoc,
                    _useFogLoc, _fogColorLoc,
                    camera, light);
            }

            GL.BindVertexArray(0);

            // ── Editor gizmos for special marker types (drawn after the solid objects so
            // the wireframe lines always render on top; depth test is disabled internally
            // so they show through terrain): real-camera frustum for cameras, a direction
            // ray + spotlight cone for lights, and a horizon + sun icon for skies. ──
            foreach (var obj in _objects)
            {
                if (obj == null) continue;

                // Camera/Light/Sky markers are 2D billboard icons (always face the camera)
                if (obj.PrimitiveType == EditorPrimitiveType.Camera ||
                    obj.PrimitiveType == EditorPrimitiveType.Light ||
                    obj.PrimitiveType == EditorPrimitiveType.Sky)
                {
                    obj.Draw2DMarker(camera);
                }

                if (obj.PrimitiveType == EditorPrimitiveType.Camera)
                {
                    if (obj.ShowFrustum)
                        obj.DrawCameraFrustum(camera);
                }
                else if (obj.PrimitiveType == EditorPrimitiveType.Light)
                {
                    if (obj.ShowLightGizmo)
                        obj.DrawLightGizmo(camera);
                }
                else if (obj.PrimitiveType == EditorPrimitiveType.Sky)
                {
                    if (obj.ShowSkyGizmo)
                        obj.DrawSkyGizmo(camera);
                }
            }

            // ── Stencil-based inverted-hull outline on ALL selected objects (with pulsing effect).
            // Planes are intentionally excluded — their large ground tiles would fill the whole
            // screen with a blinking outline, so a selected plane shows no highlight (the gizmo
            // still marks it as selected). ──
            var outlineSet = selectedObjects is { Count: > 0 }
                ? selectedObjects.Where(o => o.PrimitiveType != EditorPrimitiveType.Plane).ToArray()
                : (SelectedObject != null && SelectedObject.PrimitiveType != EditorPrimitiveType.Plane ? [SelectedObject] : null);
            if (outlineSet != null && outlineSet.Length > 0 && wireframeColor.HasValue)
            {
                // Pulsing effect: oscillates between 0.6 and 1.0 brightness
                float t = Environment.TickCount / 1000f;
                float pulse = 0.6f + 0.4f * MathF.Sin(t * 4f);
                Vector3 outlineCol = wireframeColor.Value * pulse;

                // ── Pass 1: Write stencil mask (set stencil to 1 where objects' depth passes) ──
                GL.Enable(Const.GL_STENCIL_TEST);
                GL.StencilMask(0xFF);
                GL.Clear(Const.GL_STENCIL_BUFFER_BIT);
                GL.StencilFunc(Const.GL_ALWAYS, 1, 0xFF);
                GL.StencilOp(Const.GL_KEEP, Const.GL_KEEP, Const.GL_REPLACE);
                GL.ColorMask(false, false, false, false);

                foreach (var sel in outlineSet)
                    sel.DrawOutlineStencil(camera);

                GL.ColorMask(true, true, true, true);

                // ── Pass 2: Draw expanded back faces where stencil != 1 (yellow outline) ──
                GL.StencilFunc(Const.GL_NOTEQUAL, 1, 0xFF);
                GL.StencilOp(Const.GL_KEEP, Const.GL_KEEP, Const.GL_KEEP);

                foreach (var sel in outlineSet)
                    sel.DrawOutline(camera, outlineCol);

                GL.Disable(Const.GL_STENCIL_TEST);
                // NOTE: DrawOutline() leaves GL_CULL_FACE disabled (needed for the inverted-hull
                // stencil technique). The scene's RenderProperties.Apply() re-applies the correct
                // culling/winding state at the start of the next frame's render pass.
            }
        }

        /// <summary>Render the full CSM shadow pass for all editor objects (all cascades).
        /// Caller must restore its framebuffer afterwards and bind the cascade shadow maps
        /// to texture units 6/7/8 before calling <see cref="Draw"/> with the same CSM.
        /// Shared by GameScene (via RenderShadow per cascade), MainMenuScene and the
        /// SceneManager bare-editor viewport so shadows match the sun in every view.</summary>
        public void RenderShadowPass(Camera camera, Lights light, CSM csm)
        {
            csm.UpdateMatrices(camera, light.ShadowDirStable);

            for (int i = 0; i < CSM.NumCascades; i++)
            {
                csm.BindFramebuffer(i);
                RenderShadow(camera, csm, i);
            }
        }

        /// <summary>Render shadow for all editor objects (called from shadow pass).</summary>
        public void RenderShadow(Camera camera, CSM csm, int cascadeIndex)
        {
            if (_objects.Count == 0) return;

            GL.UseProgram(_shadowShader);

            var planes = CSM.BuildPlanesFromCorners(csm.OrthoCorners[cascadeIndex]);

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                if (!obj.CastShadow || !obj.IsVisible) continue;
                if (obj.PrimitiveType == EditorPrimitiveType.GlbReference) continue;

                // Simple frustum culling for shadow
                if (planes != null)
                {
                    const float boundRadius = 3f;
                    bool outside = false;
                    foreach (var plane in planes)
                    {
                        float d = Vector3.Dot(plane.Normal, obj.Position) + plane.D;
                        if (d < -boundRadius) { outside = true; break; }
                    }
                    if (outside) continue;
                }

                // Camera/Light/Sky markers don't cast shadows
                if (obj.PrimitiveType == EditorPrimitiveType.Camera
                    || obj.PrimitiveType == EditorPrimitiveType.Light
                    || obj.PrimitiveType == EditorPrimitiveType.Sky) continue;

                obj.RenderShadow(_shadowModelLoc, camera, csm, cascadeIndex);
            }
        }

        /// <summary>
        /// Raycast against all editor objects to find the closest hit.
        /// Returns the hit object, hit point, and hit distance.
        /// </summary>
        public EditorObject? Raycast(Vector3 rayOrigin, Vector3 rayDir, out float hitDist, out Vector3 hitPoint)
        {
            hitDist = float.MaxValue;
            hitPoint = Vector3.Zero;
            EditorObject? closest = null;

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                if (!obj.IsVisible) continue;

                if (obj.PrimitiveType == EditorPrimitiveType.GlbReference)
                {
                    // Use approximate sphere AABB for glb references
                    float radius = 1.5f * obj.Scale.Length() * 0.5f;
                    if (RaySphereIntersect(rayOrigin, rayDir, obj.Position, radius, out float t))
                    {
                        if (t > 0 && t < hitDist)
                        {
                            hitDist = t;
                            hitPoint = rayOrigin + rayDir * t;
                            closest = obj;
                        }
                    }
                }
                else
                {
                    // Use AABB intersection for primitives
                    var aabb = obj.GetWorldAABB();
                    if (AABB.RayIntersectsAABB(rayOrigin, rayDir, aabb, out float tMin, out float _))
                    {
                        if (tMin > 0 && tMin < hitDist)
                        {
                            hitDist = tMin;
                            hitPoint = rayOrigin + rayDir * tMin;
                            closest = obj;
                        }
                    }
                }
            }

            return closest;
        }

        private static bool RaySphereIntersect(Vector3 origin, Vector3 dir, Vector3 center, float radius, out float t)
        {
            t = 0;
            Vector3 oc = origin - center;
            float a = Vector3.Dot(dir, dir);
            float b = 2.0f * Vector3.Dot(oc, dir);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float discriminant = b * b - 4 * a * c;
            if (discriminant < 0) return false;

            float sqrtD = MathF.Sqrt(discriminant);
            float t1 = (-b - sqrtD) / (2.0f * a);
            float t2 = (-b + sqrtD) / (2.0f * a);

            if (t1 > 0) { t = t1; return true; }
            if (t2 > 0) { t = t2; return true; }
            return false;
        }

        public void Dispose()
        {
            Clear();
        }
    }
}

