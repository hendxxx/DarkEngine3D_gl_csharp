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
                SelectedObject = null;
            obj.Dispose();
            return _objects.Remove(obj);
        }

        /// <summary>Remove an editor object at index.</summary>
        public void RemoveAt(int index)
        {
            if (index >= 0 && index < _objects.Count)
            {
                if (SelectedObject == _objects[index])
                    SelectedObject = null;
                _objects[index].Dispose();
                _objects.RemoveAt(index);
            }
        }

        /// <summary>Remove all editor objects.</summary>
        public void Clear()
        {
            SelectedObject = null;
            foreach (var obj in _objects)
                obj.Dispose();
            _objects.Clear();
        }

        /// <summary>Create and add a primitive editor object at the given position.</summary>
        public EditorObject AddPrimitive(EditorPrimitiveType type, Vector3 position)
        {
            var obj = new EditorObject(type)
            {
                Position = position,
                Scale = type == EditorPrimitiveType.Sphere ? new Vector3(1f, 1f, 1f)
                       : type == EditorPrimitiveType.Plane ? new Vector3(5f, 0.05f, 5f)
                       : Vector3.One, // Box
                Color = type switch
                {
                    EditorPrimitiveType.Plane => new Vector3(0.3f, 0.7f, 0.3f),
                    EditorPrimitiveType.Box => new Vector3(0.7f, 0.3f, 0.3f),
                    EditorPrimitiveType.Sphere => new Vector3(0.3f, 0.3f, 0.7f),
                    _ => new Vector3(0.8f, 0.8f, 0.8f),
                }
            };
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

        /// <summary>Draw all editor objects (called from the game scene rendering loop).</summary>
        public void Draw(Camera camera, Lights light, CSM? csm = null)
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
            GL.Enable(Const.GL_CULL_FACE);

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

