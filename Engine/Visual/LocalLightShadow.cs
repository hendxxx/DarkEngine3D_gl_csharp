using DarkEngine3D_gl_csharp.Engine.Libs;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>Per-light shadow maps for the editor's Point / Spotlight Light markers.
    /// Each shadow-casting Point light renders the scene into a 512² cube map (6 × 90°
    /// face passes), each Spotlight into a 1024² perspective depth map — so every light
    /// casts its own shadow instead of only the sun (CSM) doing so.
    ///
    /// The caster renderers (ObjectManager / TerrainChunk / EditorObjectManager) are
    /// reused as-is through the CSM shim: they read <see cref="CSM.LightSpaceMatrices"/>
    /// / <see cref="CSM.OrthoCorners"/> and self-upload the light-space matrix, so this
    /// class only has to bind the right FBO, set the face matrix and invoke the same
    /// per-cascade draw callback the scene already uses for its CSM pass.
    ///
    /// Fragment side: <see cref="UploadShadows"/> binds the depth maps to texture units
    /// 9..15 (free above the 0..8 the renderers use, within the guaranteed 16 fragment
    /// units of GL 3.3) and uploads per-light slot indices + spot light-space matrices,
    /// which the shaders sample in <c>calcLocalLights</c>.</summary>
    public unsafe class LocalLightShadow : CSM
    {
        /// <summary>Maximum number of shadow-casting local lights supported.</summary>
        public const int MaxShadowLights = 4;
        /// <summary>Maximum number of POINT light shadows (limited by free texture units).</summary>
        public const int MaxPointShadows = 3;

        // Texture units for the shadow maps. Renderers use 0..8; these sit above them and
        // below the guaranteed 16 fragment texture units of GL 3.3.
        public const int SpotUnitBase = 9;
        public const int PointUnitBase = 13;

        private const int SpotRes = 1024;
        private const int PointRes = 512;
        private const float PointNear = 0.05f;
        private const float SpotNear = 0.05f;

        // Spot shadows: one 2D depth texture + FBO per slot.
        private uint[] _spotFbos = new uint[MaxShadowLights];
        private uint[] _spotTexs = new uint[MaxShadowLights];
        // Point shadows: one cube depth texture + FBO per slot.
        private uint[] _pointFbos = new uint[MaxShadowLights];
        private uint[] _pointCubes = new uint[MaxShadowLights];

        // Shadow slot index per LOCAL light index (-1 = this light has no shadow map).
        private int[] _spotSlotOfLight = new int[Lights.MaxLocalLights];
        private int[] _pointSlotOfLight = new int[Lights.MaxLocalLights];
        // Local light index per shadow slot.
        private int[] _spotLightOfSlot = new int[MaxShadowLights];
        private int[] _pointLightOfSlot = new int[MaxShadowLights];
        private int _spotCount, _pointCount;

        // Per-local-light data uploaded to the fragment shaders.
        private Matrix4x4[] _spotLightSpace = new Matrix4x4[Lights.MaxLocalLights];
        private float[] _shadowFar = new float[Lights.MaxLocalLights];

        // Shadow vertex programs used by the current pass (uploaded per face).
        private uint _shadowShader, _shadowSkinnedShader, _shadowStaticAlphaShader;

        // Shared 1×1 depth-1.0 textures bound to unused slots so sampling is always valid
        // (returns 1.0 → fully lit, never garbage).
        private uint _emptySpot, _emptyCube;

        // Allocation fingerprint: (light count, type, castShadow) — reallocates GPU
        // resources only when the shadow-casting light set actually changes.
        private long _fingerprint = -1;

        public LocalLightShadow() : base()
        {
            for (int i = 0; i < Lights.MaxLocalLights; i++)
            {
                _spotSlotOfLight[i] = -1;
                _pointSlotOfLight[i] = -1;
                _spotLightSpace[i] = Matrix4x4.Identity;
                _shadowFar[i] = 1f;
            }
            CreateEmptyTextures();
        }

        public int SpotCount => _spotCount;
        public int PointCount => _pointCount;

        // ── Cube-face convention (matches GL cube-map sampling of the same direction) ──
        private static readonly Vector3[] FaceTargets =
        {
            new(1f, 0f, 0f), new(-1f, 0f, 0f), new(0f, 1f, 0f),
            new(0f, -1f, 0f), new(0f, 0f, 1f), new(0f, 0f, -1f),
        };
        private static readonly Vector3[] FaceUps =
        {
            new(0f, -1f, 0f), new(0f, -1f, 0f), new(0f, 0f, 1f),
            new(0f, 0f, -1f), new(0f, -1f, 0f), new(0f, -1f, 0f),
        };

        private static Vector3 SafeNormalize(Vector3 v)
        {
            float len = v.Length();
            return len > 1e-6f ? v / len : Vector3.UnitY;
        }

        private static Vector3 PickUp(Vector3 dir)
        {
            return MathF.Abs(Vector3.Dot(SafeNormalize(dir), Vector3.UnitY)) > 0.99f
                ? Vector3.UnitZ
                : Vector3.UnitY;
        }

        /// <summary>Build the face view-projection for a point-light cube face.</summary>
        private static Matrix4x4 BuildFaceMatrix(Vector3 pos, int face, float far)
        {
            var view = Matrix4x4.CreateLookAt(pos, pos + FaceTargets[face], FaceUps[face]);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, PointNear, far);
            return view * proj;
        }

        /// <summary>Build the frustum view-projection covering a spotlight's cone.</summary>
        private static Matrix4x4 BuildSpotMatrix(Vector3 pos, Vector3 dir, float coneDeg, float far)
        {
            var view = Matrix4x4.CreateLookAt(pos, pos + SafeNormalize(dir), PickUp(dir));
            float fovY = MathF.Min(2f * Math.Clamp(coneDeg, 1f, 89f) * MathF.PI / 180f, 3.1f);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovY, 1f, SpotNear, far);
            return view * proj;
        }

        // ── GPU resource management ──

        private long ComputeFingerprint(IReadOnlyList<Lights.LocalLightData> lights)
        {
            long fp = lights.Count;
            int n = Math.Min(lights.Count, Lights.MaxLocalLights);
            for (int i = 0; i < n; i++)
                fp = fp * 31 + lights[i].Type * 2 + (lights[i].CastShadow ? 1 : 0);
            return fp;
        }

        /// <summary>Allocate / free the shadow textures + FBOs to match the current local
        /// light list. Cheap when the light set is unchanged (no GPU work at all).</summary>
        public void Update(IReadOnlyList<Lights.LocalLightData> lights)
        {
            long fp = ComputeFingerprint(lights);
            if (fp == _fingerprint) return;
            _fingerprint = fp;

            FreeResources();

            for (int i = 0; i < Lights.MaxLocalLights; i++)
            {
                _spotSlotOfLight[i] = -1;
                _pointSlotOfLight[i] = -1;
                _spotLightSpace[i] = Matrix4x4.Identity;
                _shadowFar[i] = 1f;
            }

            _spotCount = 0;
            _pointCount = 0;

            int n = Math.Min(lights.Count, Lights.MaxLocalLights);
            for (int i = 0; i < n; i++)
            {
                var l = lights[i];
                if (!l.CastShadow) continue;

                if (l.Type == 1 && _pointCount < MaxPointShadows)
                {
                    _pointLightOfSlot[_pointCount] = i;
                    _pointSlotOfLight[i] = _pointCount;
                    CreatePointShadow(_pointCount);
                    _pointCount++;
                }
                else if (l.Type == 2 && _spotCount < MaxShadowLights)
                {
                    _spotLightOfSlot[_spotCount] = i;
                    _spotSlotOfLight[i] = _spotCount;
                    CreateSpotShadow(_spotCount);
                    _spotCount++;
                }
            }
        }

        private void FreeResources()
        {
            if (_spotCount > 0)
            {
                fixed (uint* pF = _spotFbos) GL.DeleteFramebuffers(_spotCount, pF);
                fixed (uint* pT = _spotTexs) GL.DeleteTextures(_spotCount, pT);
            }
            if (_pointCount > 0)
            {
                fixed (uint* pF = _pointFbos) GL.DeleteFramebuffers(_pointCount, pF);
                fixed (uint* pT = _pointCubes) GL.DeleteTextures(_pointCount, pT);
            }
            _spotCount = 0;
            _pointCount = 0;
        }

        private void CreateSpotShadow(int slot)
        {
            fixed (uint* pF = _spotFbos) GL.GenFramebuffers(1, pF + slot);
            fixed (uint* pT = _spotTexs) GL.GenTextures(1, pT + slot);

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _spotFbos[slot]);
            GL.BindTexture(Const.GL_TEXTURE_2D, _spotTexs[slot]);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_DEPTH_COMPONENT32F,
                SpotRes, SpotRes, 0, Const.GL_DEPTH_COMPONENT, Const.GL_FLOAT, (void*)0);

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_BORDER);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_BORDER);
            float[] border = { 1f, 1f, 1f, 1f };
            fixed (float* pB = border)
                GL.TexParameterfv(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_BORDER_COLOR, pB);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_COMPARE_MODE, (int)Const.GL_NONE);

            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_ATTACHMENT,
                Const.GL_TEXTURE_2D, _spotTexs[slot], 0);
            uint none = Const.GL_NONE;
            GL.DrawBuffers(0, &none);
            GL.ReadBuffer(Const.GL_NONE);

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
        }

        private void CreatePointShadow(int slot)
        {
            fixed (uint* pF = _pointFbos) GL.GenFramebuffers(1, pF + slot);
            fixed (uint* pT = _pointCubes) GL.GenTextures(1, pT + slot);

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _pointFbos[slot]);
            GL.BindTexture(Const.GL_TEXTURE_CUBE_MAP, _pointCubes[slot]);
            for (int f = 0; f < 6; f++)
            {
                GL.TexImage2D(Const.GL_TEXTURE_CUBE_MAP_POSITIVE_X + (uint)f, 0,
                    (int)Const.GL_DEPTH_COMPONENT32F,
                    PointRes, PointRes, 0, Const.GL_DEPTH_COMPONENT, Const.GL_FLOAT, (void*)0);
            }
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_BORDER);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_BORDER);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_WRAP_R, (int)Const.GL_CLAMP_TO_BORDER);
            float[] border = { 1f, 1f, 1f, 1f };
            fixed (float* pB = border)
                GL.TexParameterfv(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_BORDER_COLOR, pB);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_COMPARE_MODE, (int)Const.GL_NONE);

            uint none = Const.GL_NONE;
            GL.DrawBuffers(0, &none);
            GL.ReadBuffer(Const.GL_NONE);

            GL.BindTexture(Const.GL_TEXTURE_CUBE_MAP, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
        }

        private void CreateEmptyTextures()
        {
            float one = 1f;

            fixed (uint* pE = &_emptySpot)
                GL.GenTextures(1, pE);
            GL.BindTexture(Const.GL_TEXTURE_2D, _emptySpot);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_DEPTH_COMPONENT32F,
                1, 1, 0, Const.GL_DEPTH_COMPONENT, Const.GL_FLOAT, &one);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_BORDER);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_BORDER);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_COMPARE_MODE, (int)Const.GL_NONE);

            fixed (uint* pC = &_emptyCube)
                GL.GenTextures(1, pC);
            GL.BindTexture(Const.GL_TEXTURE_CUBE_MAP, _emptyCube);
            for (int f = 0; f < 6; f++)
            {
                GL.TexImage2D(Const.GL_TEXTURE_CUBE_MAP_POSITIVE_X + (uint)f, 0,
                    (int)Const.GL_DEPTH_COMPONENT32F,
                    1, 1, 0, Const.GL_DEPTH_COMPONENT, Const.GL_FLOAT, &one);
            }
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_BORDER);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_BORDER);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_WRAP_R, (int)Const.GL_CLAMP_TO_BORDER);
            GL.TexParameteri(Const.GL_TEXTURE_CUBE_MAP, Const.GL_TEXTURE_COMPARE_MODE, (int)Const.GL_NONE);

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.BindTexture(Const.GL_TEXTURE_CUBE_MAP, 0);
        }

        // ── Shadow pass ──

        /// <summary>Render the depth maps for all shadow-casting local lights. <paramref name="drawCascade"/>
        /// is the scene's per-cascade draw callback (same renderers as its CSM pass); the caller must
        /// restore its framebuffer + viewport afterwards (or this method does it for them).</summary>
        public void RenderShadowPass(
            Camera camera,
            IReadOnlyList<Lights.LocalLightData> lights,
            uint shadowShader, uint shadowSkinnedShader, uint shadowStaticAlphaShader,
            Action<CSM, int> drawCascade)
        {
            Update(lights);
            if (_spotCount == 0 && _pointCount == 0) return;
            if (drawCascade == null) return;

            _shadowShader = shadowShader;
            _shadowSkinnedShader = shadowSkinnedShader;
            _shadowStaticAlphaShader = shadowStaticAlphaShader;

            // Light-space uniform locations per program.
            int spotLs = GL.GetUniformLocation(shadowShader, "lightSpaceMatrix");
            int skinnedLs = GL.GetUniformLocation(shadowSkinnedShader, "lightSpaceMatrix");
            int staticAlphaLs = GL.GetUniformLocation(shadowStaticAlphaShader, "lightSpaceMatrix");

            // ── Spotlights: one perspective pass each ──
            for (int s = 0; s < _spotCount; s++)
            {
                int li = _spotLightOfSlot[s];
                if (li < 0 || li >= lights.Count) continue;
                var l = lights[li];

                float range = MathF.Max(l.Range, 0.1f);
                var m = BuildSpotMatrix(l.Position, l.Direction, l.ConeAngleDeg, range);
                LightSpaceMatrices[0] = m;
                _spotLightSpace[li] = m;
                _shadowFar[li] = range;

                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _spotFbos[s]);
                GL.Viewport(0, 0, SpotRes, SpotRes);
                GL.Clear(Const.GL_DEPTH_BUFFER_BIT);
                UploadLightSpace(spotLs, skinnedLs, staticAlphaLs);
                drawCascade(this, 0);
            }

            // ── Point lights: six 90° face passes into the cube ──
            for (int p = 0; p < _pointCount; p++)
            {
                int li = _pointLightOfSlot[p];
                if (li < 0 || li >= lights.Count) continue;
                var l = lights[li];

                float range = MathF.Max(l.Range, 0.1f);
                _shadowFar[li] = range;

                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _pointFbos[p]);
                GL.Viewport(0, 0, PointRes, PointRes);
                for (int face = 0; face < 6; face++)
                {
                    GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_ATTACHMENT,
                        Const.GL_TEXTURE_CUBE_MAP_POSITIVE_X + (uint)face, _pointCubes[p], 0);
                    GL.Clear(Const.GL_DEPTH_BUFFER_BIT);
                    LightSpaceMatrices[0] = BuildFaceMatrix(l.Position, face, range);
                    UploadLightSpace(spotLs, skinnedLs, staticAlphaLs);
                    drawCascade(this, 0);
                }
            }

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
        }

        private void UploadLightSpace(int spotLs, int skinnedLs, int staticAlphaLs)
        {
            // Terrain / skinned / static-alpha casters read the light-space uniform from the
            // currently-bound program; the editor-object renderers self-upload from the CSM.
            fixed (float* p = &LightSpaceMatrices[0].M11)
            {
                if (spotLs >= 0 && _shadowShader != 0)
                {
                    GL.UseProgram(_shadowShader);
                    GL.UniformMatrix4fv(spotLs, 1, false, p);
                }
                if (skinnedLs >= 0 && _shadowSkinnedShader != 0)
                {
                    GL.UseProgram(_shadowSkinnedShader);
                    GL.UniformMatrix4fv(skinnedLs, 1, false, p);
                }
                if (staticAlphaLs >= 0 && _shadowStaticAlphaShader != 0)
                {
                    GL.UseProgram(_shadowStaticAlphaShader);
                    GL.UniformMatrix4fv(staticAlphaLs, 1, false, p);
                }
            }
        }

        // ── Fragment-side upload ──

        /// <summary>Bind the shadow maps to their texture units and upload the per-light
        /// slot indices / spot matrices / point far planes. Called from
        /// <see cref="Lights.UploadLocalLights"/> so every render path samples the same set.
        /// When <paramref name="enabled"/> is false all per-light indices are uploaded as
        /// -1, so the shaders skip shadow sampling entirely (viewport "Shadow" toggle).</summary>
        public void UploadShadows(uint program, bool enabled = true)
        {
            if (program == 0) return;

            for (int i = 0; i < MaxShadowLights; i++)
            {
                int loc = GL.GetUniformLocation(program, $"u_localShadowSpot[{i}]");
                if (loc >= 0) GL.Uniform1i(loc, SpotUnitBase + i);
            }
            for (int i = 0; i < MaxPointShadows; i++)
            {
                int loc = GL.GetUniformLocation(program, $"u_localShadowPoint[{i}]");
                if (loc >= 0) GL.Uniform1i(loc, PointUnitBase + i);
            }

            // Biases are in WORLD units (the shaders compare linear depth).
            int biasLoc = GL.GetUniformLocation(program, "u_localShadowBias");
            if (biasLoc >= 0) GL.Uniform1f(biasLoc, 0.02f);
            int pointBiasLoc = GL.GetUniformLocation(program, "u_localShadowPointBias");
            if (pointBiasLoc >= 0) GL.Uniform1f(pointBiasLoc, 0.05f);

            for (int i = 0; i < Lights.MaxLocalLights; i++)
            {
                int slotLoc = GL.GetUniformLocation(program, $"u_localShadowSpotIdx[{i}]");
                if (slotLoc >= 0) GL.Uniform1i(slotLoc, enabled ? _spotSlotOfLight[i] : -1);
                int pointLoc = GL.GetUniformLocation(program, $"u_localShadowPointIdx[{i}]");
                if (pointLoc >= 0) GL.Uniform1i(pointLoc, enabled ? _pointSlotOfLight[i] : -1);

                int lsLoc = GL.GetUniformLocation(program, $"u_localLightSpace[{i}]");
                if (lsLoc >= 0)
                {
                    fixed (float* p = &_spotLightSpace[i].M11)
                        GL.UniformMatrix4fv(lsLoc, 1, false, p);
                }
                int farLoc = GL.GetUniformLocation(program, $"u_localShadowFar[{i}]");
                if (farLoc >= 0) GL.Uniform1f(farLoc, _shadowFar[i]);
            }

            // Bind real (or empty) depth maps to every shadow unit so sampling is always valid.
            for (int i = 0; i < MaxShadowLights; i++)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0 + (uint)(SpotUnitBase + i));
                GL.BindTexture(Const.GL_TEXTURE_2D, i < _spotCount ? _spotTexs[i] : _emptySpot);
            }
            for (int i = 0; i < MaxPointShadows; i++)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0 + (uint)(PointUnitBase + i));
                GL.BindTexture(Const.GL_TEXTURE_CUBE_MAP, i < _pointCount ? _pointCubes[i] : _emptyCube);
            }
            GL.ActiveTexture(Const.GL_TEXTURE0);
        }

        public override void Dispose()
        {
            FreeResources();
            if (_emptySpot != 0)
            {
                fixed (uint* pE = &_emptySpot)
                    GL.DeleteTextures(1, pE);
                _emptySpot = 0;
            }
            if (_emptyCube != 0)
            {
                fixed (uint* pC = &_emptyCube)
                    GL.DeleteTextures(1, pC);
                _emptyCube = 0;
            }
            base.Dispose();
        }
    }
}
