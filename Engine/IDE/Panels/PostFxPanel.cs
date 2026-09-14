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

        // Depth of field (slider-driven focus circle).
        private bool _dofEnabled = PostFxSettings.DofEnabled;
        private float _dofFocusX = PostFxSettings.DofFocusX;
        private float _dofFocusY = PostFxSettings.DofFocusY;
        private float _dofRadius = PostFxSettings.DofRadius;
        private float _dofFeather = PostFxSettings.DofFeather;
        private float _dofMaxBlur = PostFxSettings.DofMaxBlur;
        private int _dofFocusTarget = PostFxSettings.DofFocusTarget;
        private float _dofFollowSpeed = PostFxSettings.DofFollowSpeed;
        private int _dofFocusShape = PostFxSettings.DofFocusShape;
        private bool _dofInvertMask = PostFxSettings.DofInvertMask;
        private bool _dofSpriteShapeEnable = PostFxSettings.DofSpriteShapeEnable;
        private int _dofSpriteShapeLayer = PostFxSettings.DofSpriteShapeLayer;
        private float _dofSpriteExpandPx = PostFxSettings.DofSpriteExpandPx;
        private float _dofSpriteAlphaBias = PostFxSettings.DofSpriteAlphaBias;
        private bool _dofSpriteMaskOnly = PostFxSettings.DofSpriteMaskOnly;

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
            _dofEnabled = PostFxSettings.DofEnabled;
            _dofFocusX = PostFxSettings.DofFocusX;
            _dofFocusY = PostFxSettings.DofFocusY;
            _dofRadius = PostFxSettings.DofRadius;
            _dofFeather = PostFxSettings.DofFeather;
            _dofMaxBlur = PostFxSettings.DofMaxBlur;
            _dofFocusTarget = PostFxSettings.DofFocusTarget;
            _dofFollowSpeed = PostFxSettings.DofFollowSpeed;
            _dofFocusShape = PostFxSettings.DofFocusShape;
            _dofInvertMask = PostFxSettings.DofInvertMask;
            _dofSpriteShapeEnable = PostFxSettings.DofSpriteShapeEnable;
            _dofSpriteShapeLayer = PostFxSettings.DofSpriteShapeLayer;
            _dofSpriteExpandPx = PostFxSettings.DofSpriteExpandPx;
            _dofSpriteAlphaBias = PostFxSettings.DofSpriteAlphaBias;
            _dofSpriteMaskOnly = PostFxSettings.DofSpriteMaskOnly;

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

            // ══════════════════════════════════════════════
            //  Depth of Field (slider-driven focus circle)
            // ══════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Depth of Field", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.Checkbox("Enable Depth of Field", ref _dofEnabled))
                {
                    PostFxSettings.DofEnabled = _dofEnabled;
                    PersistAndNotify(_dofEnabled ? "Depth of field enabled" : "Depth of field disabled");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Blur everything outside the focus circle.\nThe focus point is a spot on SCREEN (works for 2D and 3D cameras).");

                if (_dofEnabled)
                {
                    // Live status: distinguishes "not running" (render path / shader
                    // problem — see console) from "running but too subtle".
                    if (Visual.PostProcessing.DepthOfFieldComposite.HasEverRun)
                        ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), "● ACTIVE — compositing every frame");
                    else
                        ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.3f, 1f), "○ NOT RUNNING — check console for [DepthOfField] SKIP/disable");

                    // ── Focus Shape: Geometric Circle vs Player Sprite vs Sprite Layer vs Hybrid ──
                    string[] shapeNames = { "Geometric Circle (Lingkaran)", "Player Sprite (Siluet)", "Sprite2D Layer", "Player + Sprite2D Layer", "Hybrid (Circle + Player)" };
                    int shapeIdx = Math.Clamp(_dofFocusShape, 0, shapeNames.Length - 1);
                    if (ImGui.BeginCombo("Focus Shape", shapeNames[shapeIdx]))
                    {
                        for (int i = 0; i < shapeNames.Length; i++)
                        {
                            if (ImGui.Selectable(shapeNames[i], i == shapeIdx))
                            {
                                _dofFocusShape = i;
                                PostFxSettings.DofFocusShape = i;
                                PostFxSettings.DofSpriteShapeEnable = (i != 0);
                                PersistAndNotify($"DoF shape → {shapeNames[i]}");
                            }
                        }
                        ImGui.EndCombo();
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Bentuk fokus DoF:\nGeometric Circle = Lingkaran fokus manual/target.\nPlayer Sprite = Siluet presisi animasi Player (bukan bentuk geometri).\nSprite2D Layer = Siluet semua sprite di Render Layer terpilih.\nPlayer + Sprite2D Layer = Gabungan Player dan Sprite layer.\nHybrid = Lingkaran fokus + Siluet Player bersamaan.");

                    if (ImGui.SliderFloat("Blur Strength", ref _dofMaxBlur, 0f, 24f, "%.1f"))
                    {
                        PostFxSettings.DofMaxBlur = _dofMaxBlur;
                        PersistAndNotify();
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Maximum blur radius (in pixels) far from the focus region.");

                    bool isGeometric = (_dofFocusShape == 0 || _dofFocusShape == 4);
                    bool isSpriteMask = (_dofFocusShape != 0);

                    // ── Sprite silhouette options (shown for Player Sprite, Sprite Layer, or Hybrid) ──
                    if (isSpriteMask)
                    {
                        ImGui.Separator();
                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Silhouette Options");

                        if (ImGui.Checkbox("Invert: Blur on Player / Sprite", ref _dofInvertMask))
                        {
                            PostFxSettings.DofInvertMask = _dofInvertMask;
                            PersistAndNotify(_dofInvertMask ? "DoF Inverted (Blur on sprite)" : "DoF Normal (Sprite sharp)");
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Normal (OFF): Sprite Player tajam, background sekelilingnya yang blur.\nInvert (ON): Sprite Player yang kena blur, background sekelilingnya tajam.");

                        if (_dofFocusShape == 2 || _dofFocusShape == 3)
                        {
                            if (ImGui.SliderInt("Sprite Layer", ref _dofSpriteShapeLayer, -10, 10))
                            {
                                PostFxSettings.DofSpriteShapeLayer = _dofSpriteShapeLayer;
                                PersistAndNotify();
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Render Layer mana yang siluetnya dipakai.\nHarus sama dengan Render Layer objek Sprite2D di Inspector.");
                        }

                        if (ImGui.SliderFloat("Edge Expand", ref _dofSpriteExpandPx, 0f, 6f, "%.1f"))
                        {
                            PostFxSettings.DofSpriteExpandPx = _dofSpriteExpandPx;
                            PersistAndNotify();
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Seberapa jauh siluet membesar dari alpha asli (mask texel ≈ 2px scene).\n0 = piksel sempurna, 2-3 = tepi sprite tetap tajam walau blur makan tepi.");

                        if (ImGui.SliderFloat("Alpha Bias", ref _dofSpriteAlphaBias, 0f, 0.5f, "%.2f"))
                        {
                            PostFxSettings.DofSpriteAlphaBias = _dofSpriteAlphaBias;
                            PersistAndNotify();
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Menambah coverage alpha: pixel semi-transparan ikut dianggap dalam fokus.");

                        if (ImGui.Checkbox("Debug: Mask Only", ref _dofSpriteMaskOnly))
                        {
                            PostFxSettings.DofSpriteMaskOnly = _dofSpriteMaskOnly;
                            PersistAndNotify();
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Debug: Hanya gunakan mask siluet untuk memverifikasi bentuk mask.");
                    }

                    // ── Geometric Circle controls (shown only when Circle or Hybrid is active) ──
                    if (isGeometric)
                    {
                        ImGui.Separator();
                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Geometric Circle Controls");

                        string[] targetNames = { "Manual (sliders)", "Follow Player", "Hovered Tile", "Hovered Object", "Selected Object" };
                        int targetIdx = Math.Clamp(_dofFocusTarget, 0, targetNames.Length - 1);
                        if (ImGui.BeginCombo("Focus Target", targetNames[targetIdx]))
                        {
                            for (int i = 0; i < targetNames.Length; i++)
                            {
                                if (ImGui.Selectable(targetNames[i], i == targetIdx))
                                {
                                    _dofFocusTarget = i;
                                    PostFxSettings.DofFocusTarget = i;
                                    Visual.PostProcessing.DepthOfFieldFocusTracker.Snap();
                                    PersistAndNotify($"DoF focus → {targetNames[i]}");
                                }
                            }
                            ImGui.EndCombo();
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Apa yang dikejar lingkaran tajam:\nManual = geser slider Focus X/Y sendiri.\nFollow Player = mengikuti Player2D (juga saat main).\nHovered Tile = tile di bawah kursor (edit mode).\nHovered Object = objek di bawah kursor (edit mode).\nSelected Object = objek terpilih (rata-rata kalau multi).");

                        if (_dofFocusTarget != 0)
                        {
                            if (ImGui.SliderFloat("Follow Speed", ref _dofFollowSpeed, 1f, 30f, "%.0f"))
                            {
                                PostFxSettings.DofFollowSpeed = _dofFollowSpeed;
                                PersistAndNotify();
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Seberapa cepat fokus mengejar target yang bergerak.\n1 = santai mengalir, 30 = menempel kencang.");

                            ImGui.TextColored(new Vector4(0.7f, 0.8f, 0.95f, 1f),
                                $"Live focus: {PostFxSettings.DofFocusX:F2}, {PostFxSettings.DofFocusY:F2}");
                            ImGui.TextDisabled("Posisi dikendalikan target — slider manual disembunyikan.");
                        }
                        else
                        {
                            if (ImGui.SliderFloat("Focus X", ref _dofFocusX, 0f, 1f, "%.2f"))
                            {
                                PostFxSettings.DofFocusX = _dofFocusX;
                                PersistAndNotify();
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Focus point, horizontal: 0 = left edge, 0.5 = center, 1 = right edge.");

                            if (ImGui.SliderFloat("Focus Y", ref _dofFocusY, 0f, 1f, "%.2f"))
                            {
                                PostFxSettings.DofFocusY = _dofFocusY;
                                PersistAndNotify();
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Focus point, vertical: 0 = bottom edge, 0.5 = center, 1 = top edge.");
                        }

                        if (ImGui.SliderFloat("Focus Radius", ref _dofRadius, 0.01f, 1f, "%.2f"))
                        {
                            PostFxSettings.DofRadius = _dofRadius;
                            PersistAndNotify();
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Sharp area around the focus point (fraction of screen height).\nEverything inside stays perfectly crisp.");

                        if (ImGui.SliderFloat("Feather", ref _dofFeather, 0.01f, 1f, "%.2f"))
                        {
                            PostFxSettings.DofFeather = _dofFeather;
                            PersistAndNotify();
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Width of the transition band where blur ramps up.\nSmall = harsh focus edge, large = gradual cinematic falloff.");

                        if (ImGui.Button("Center Focus"))
                        {
                            _dofFocusX = 0.5f;
                            _dofFocusY = 0.5f;
                            PostFxSettings.DofFocusX = _dofFocusX;
                            PostFxSettings.DofFocusY = _dofFocusY;
                            PersistAndNotify("Focus moved to screen center");
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Snap the focus point to the middle of the screen.");

                        ImGui.SameLine();
                    }
                    // Sanity test: extreme values the eye CANNOT miss. If the scene is
                    // still not blurry after this, the composite is not reaching the
                    // texture your viewport samples — check the [DepthOfField] console log.
                    if (ImGui.Button("Test: Max Blur"))
                    {
                        _dofFocusX = 0.5f; _dofFocusY = 0.5f;
                        _dofRadius = 0.05f; _dofFeather = 0.10f; _dofMaxBlur = 20f;
                        PostFxSettings.DofFocusX = _dofFocusX;
                        PostFxSettings.DofFocusY = _dofFocusY;
                        PostFxSettings.DofRadius = _dofRadius;
                        PostFxSettings.DofFeather = _dofFeather;
                        PostFxSettings.DofMaxBlur = _dofMaxBlur;
                        PersistAndNotify("DoF test — tiny sharp dot, everything else blurred 20px");
                        Console.WriteLine("[DepthOfField] TEST preset applied: radius=0.05 feather=0.10 blur=20px — the screen must look clearly blurry outside the center dot.");
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Extreme preset to verify the effect: only a tiny center dot stays sharp,\neverything else blurs hard. If you still see no blur, the composite isn't running\non your viewport's texture — check the console [DepthOfField] lines.");

                    ImGui.TextDisabled("Focus circle on screen — outside blurs up to Blur Strength.");
                }
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
            _dofEnabled = PostFxSettings.DofEnabled;
            _dofFocusX = PostFxSettings.DofFocusX;
            _dofFocusY = PostFxSettings.DofFocusY;
            _dofRadius = PostFxSettings.DofRadius;
            _dofFeather = PostFxSettings.DofFeather;
            _dofMaxBlur = PostFxSettings.DofMaxBlur;
            _dofFocusTarget = PostFxSettings.DofFocusTarget;
            _dofFollowSpeed = PostFxSettings.DofFollowSpeed;
            _dofSpriteShapeEnable = PostFxSettings.DofSpriteShapeEnable;
            _dofSpriteShapeLayer = PostFxSettings.DofSpriteShapeLayer;
            _dofSpriteExpandPx = PostFxSettings.DofSpriteExpandPx;
            _dofSpriteAlphaBias = PostFxSettings.DofSpriteAlphaBias;
            _dofSpriteMaskOnly = PostFxSettings.DofSpriteMaskOnly;
        }
    }
}
