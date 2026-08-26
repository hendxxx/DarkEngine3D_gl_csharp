using DarkEngine3D_gl_csharp.Engine.Config;
using ImGuiNET;
using System;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels
{
    /// <summary>
    /// Post-Processing panel — live sliders for the AAA post-FX chain:
    /// <list type="bullet">
    /// <item>Master on/off for the whole bloom + tonemap + gamma chain.</item>
    /// <item>Bloom: intensity, threshold and soft-knee (bright-pass tuning).</item>
    /// <item>Tonemap: exposure (ACES filmic input gain).</item>
    /// <item>Gamma correction (2.2 = standard sRGB).</item>
    /// </list>
    /// Every slider writes <see cref="PostFxSettings"/> directly — the values are read
    /// each frame by PostProcessStack.RunPostFx, so changes apply live with no rebuild.
    /// Changes are persisted to settings.json so the look survives restarts.
    /// </summary>
    public class PostFxPanel
    {
        private bool _visible = true;

        // Local mirrors so drags stay stable while editing; applied to PostFxSettings on change.
        private bool _enabled = PostFxSettings.Enabled;
        private float _bloomIntensity = PostFxSettings.BloomIntensity;
        private float _bloomThreshold = PostFxSettings.BloomThreshold;
        private float _bloomSoftKnee = PostFxSettings.BloomSoftKnee;
        private float _exposure = PostFxSettings.Exposure;
        private float _gamma = PostFxSettings.Gamma;
        private bool _autoExposure = PostFxSettings.AutoExposure;
        private float _aeMin = PostFxSettings.AutoExposureMinExposure;
        private float _aeMax = PostFxSettings.AutoExposureMaxExposure;
        private float _aeTarget = PostFxSettings.AutoExposureTargetLuminance;
        private float _aeSpeed = PostFxSettings.AutoExposureSpeed;

        private string _notifText = "";
        private float _notifTimer = 0f;

        public PostFxPanel(IDEBridge bridge) { }

        public void ShowInMenu() => ImGui.MenuItem("Post FX (Bloom/Tonemap/Gamma)", null, ref _visible);

        public void Render()
        {
            if (!_visible) return;

            // Re-sync mirrors from PostFxSettings so sliders always reflect current values.
            _enabled = PostFxSettings.Enabled;
            _bloomIntensity = PostFxSettings.BloomIntensity;
            _bloomThreshold = PostFxSettings.BloomThreshold;
            _bloomSoftKnee = PostFxSettings.BloomSoftKnee;
            _exposure = PostFxSettings.Exposure;
            _gamma = PostFxSettings.Gamma;
            _autoExposure = PostFxSettings.AutoExposure;
            _aeMin = PostFxSettings.AutoExposureMinExposure;
            _aeMax = PostFxSettings.AutoExposureMaxExposure;
            _aeTarget = PostFxSettings.AutoExposureTargetLuminance;
            _aeSpeed = PostFxSettings.AutoExposureSpeed;

            ImGui.Begin("Post FX", ref _visible);

            // ── Save-feedback notification ──
            if (_notifTimer > 0f && !string.IsNullOrEmpty(_notifText))
            {
                _notifTimer -= ImGui.GetIO().DeltaTime;
                float alpha = Math.Clamp(_notifTimer, 0f, 1f);
                ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, alpha), $"✓ {_notifText}");
                ImGui.TextDisabled("Saved to settings.json next to the executable.");
                ImGui.Separator();
            }

            // ── Master switch ──
            if (ImGui.Checkbox("Enable Post FX (Bloom + Tonemap + Gamma)", ref _enabled))
            {
                PostFxSettings.Enabled = _enabled;
                PersistAndNotify(_enabled ? "Post FX enabled" : "Post FX disabled");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Master switch for the whole chain.\nOff = raw scene rendered as-is (no bloom, no tonemap, no gamma).");

            ImGui.Spacing();

            // ════════════════════════════════════════════════
            //  Bloom
            // ════════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Bloom", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.SliderFloat("Intensity", ref _bloomIntensity, 0f, 2f, "%.2f"))
                {
                    PostFxSettings.BloomIntensity = _bloomIntensity;
                    PersistAndNotify();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("How strongly the blurred bright areas glow back onto the scene.\n0 = bloom off.");

                if (ImGui.SliderFloat("Threshold", ref _bloomThreshold, 0f, 2f, "%.2f"))
                {
                    PostFxSettings.BloomThreshold = _bloomThreshold;
                    PersistAndNotify();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Luminance above which pixels start to bloom.\nLower = more of the scene glows (sky, lights, highlights).");

                if (ImGui.SliderFloat("Soft Knee", ref _bloomSoftKnee, 0f, 0.5f, "%.2f"))
                {
                    PostFxSettings.BloomSoftKnee = _bloomSoftKnee;
                    PersistAndNotify();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Width of the soft transition below the threshold.\nHigher = smoother falloff, no hard bloom edges.");

                ImGui.TextDisabled("Bright pass is extracted at half resolution, blurred 2× (separable Gaussian).");
            }

            // ════════════════════════════════════════════════
            //  Tonemapping & Gamma
            // ════════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Tonemapping & Gamma", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.Checkbox("Auto Exposure", ref _autoExposure))
                {
                    PostFxSettings.AutoExposure = _autoExposure;
                    PersistAndNotify(_autoExposure ? "Auto exposure enabled" : "Auto exposure disabled");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Measure the scene's average brightness every frame and adapt the exposure\nso bright scenes don't blow out and dark scenes stay visible.");

                if (_autoExposure)
                {
                    if (ImGui.SliderFloat("AE Min Exposure", ref _aeMin, 0.05f, 4f, "%.2f"))
                    {
                        PostFxSettings.AutoExposureMinExposure = _aeMin;
                        PersistAndNotify();
                    }
                    if (ImGui.SliderFloat("AE Max Exposure", ref _aeMax, 0.1f, 8f, "%.2f"))
                    {
                        PostFxSettings.AutoExposureMaxExposure = _aeMax;
                        PersistAndNotify();
                    }
                    if (ImGui.SliderFloat("Target Brightness", ref _aeTarget, 0.01f, 0.6f, "%.2f"))
                    {
                        PostFxSettings.AutoExposureTargetLuminance = _aeTarget;
                        PersistAndNotify();
                    }
                    if (ImGui.SliderFloat("Adapt Speed", ref _aeSpeed, 0.01f, 4f, "%.2f"))
                    {
                        PostFxSettings.AutoExposureSpeed = _aeSpeed;
                        PersistAndNotify();
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("How fast the exposure follows brightness changes.\nLow = smooth, cinematic transitions. High = instant.");

                    // Read-only live value (written by the post-FX processor each frame).
                    ImGui.TextColored(new Vector4(0.7f, 0.75f, 0.85f, 1f),
                        $"Current exposure: {PostFxSettings.CurrentAutoExposure:F2}");
                    ImGui.Spacing();
                }
                else
                {
                    if (ImGui.SliderFloat("Exposure", ref _exposure, 0.1f, 4f, "%.2f"))
                    {
                        PostFxSettings.Exposure = _exposure;
                        PersistAndNotify();
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Input gain applied before the ACES filmic curve.\n>1 brightens (esp. shadows/mids), <1 darkens.\nIgnored while Auto Exposure is on.");
                }

                if (ImGui.SliderFloat("Gamma", ref _gamma, 0.4f, 4f, "%.2f"))
                {
                    PostFxSettings.Gamma = _gamma;
                    PersistAndNotify();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Gamma correction applied after tonemapping.\n2.2 = standard sRGB. Higher = brighter midtones.");

                ImGui.TextDisabled("ACES filmic tonemap: lifts midtones, rolls off highlights, keeps shadows deep.");
            }

            ImGui.Spacing();

            // ════════════════════════════════════════════════
            //  Reset
            // ════════════════════════════════════════════════
            if (ImGui.Button("Reset to Defaults"))
            {
                PostFxSettings.ResetToDefaults();
                RefreshMirrors();
                PersistAndNotify("Post FX reset to defaults");
                Console.WriteLine("[PostFX] Settings reset to defaults.");
            }

            ImGui.End();
        }

        /// <summary>Persist the live post-FX values and show the save-feedback bar.</summary>
        private void PersistAndNotify(string text = "Post FX settings saved")
        {
            if (PostFxSettings.Persist())
                ShowSaveNotification(text);
            else
                ShowSaveNotification("Save failed — see console", 3.5f);
        }

        private void ShowSaveNotification(string text, float duration = 2.5f)
        {
            _notifText = text;
            _notifTimer = duration;
        }

        private void RefreshMirrors()
        {
            _enabled = PostFxSettings.Enabled;
            _bloomIntensity = PostFxSettings.BloomIntensity;
            _bloomThreshold = PostFxSettings.BloomThreshold;
            _bloomSoftKnee = PostFxSettings.BloomSoftKnee;
            _exposure = PostFxSettings.Exposure;
            _gamma = PostFxSettings.Gamma;
            _autoExposure = PostFxSettings.AutoExposure;
            _aeMin = PostFxSettings.AutoExposureMinExposure;
            _aeMax = PostFxSettings.AutoExposureMaxExposure;
            _aeTarget = PostFxSettings.AutoExposureTargetLuminance;
            _aeSpeed = PostFxSettings.AutoExposureSpeed;
        }
    }
}
