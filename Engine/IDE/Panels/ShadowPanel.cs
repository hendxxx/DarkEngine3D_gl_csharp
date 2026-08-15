using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels
{
    /// <summary>
    /// Shadow Settings panel — every shadow knob in one place:
    /// <list type="bullet">
    /// <item>Cascade quality preset (Low/Medium/High/Ultra) → live CSM rebuild.</item>
    /// <item>Per-cascade resolutions + split distances (cascade transition placement).</item>
    /// <item>Cascade blend range (transition width, fraction of the split distance).</item>
    /// <item>Bias: constant + slope + minimum for the main/terrain shader, separate
    /// values for the gltf (GLB) shader, plus the normal bias used when casting shadows.</item>
    /// <item>Filter mode (PCF/PCSS, same as the H key) and depth-map linear/nearest.</item>
    /// </list>
    /// All changes apply live — bias values are uploaded to the shaders every frame and
    /// cascade changes bump ShadowSettings.Version so every CSM rebuilds itself.
    /// </summary>
    public class ShadowPanel
    {
        private readonly IDEBridge _bridge;
        private bool _visible = true;

        // Local mirrors so the drags are stable while editing; applied to ShadowSettings on change.
        private int _quality = ShadowSettings.Quality;
        private readonly float[] _layer = (float[])ShadowSettings.CascadeLayer.Clone();
        private float _constantBias = ShadowSettings.ConstantBias;
        private float _slopeBias = ShadowSettings.SlopeBias;
        private float _minBias = ShadowSettings.MinBias;
        private float _gltfConstantBias = ShadowSettings.GltfConstantBias;
        private float _gltfSlopeBias = ShadowSettings.GltfSlopeBias;
        private float _gltfMinBias = ShadowSettings.GltfMinBias;
        private float _blendRange = ShadowSettings.BlendRange;
        private float _normalBias = ShadowSettings.NormalBias;
        private readonly float[] _maxWorldBias = (float[])ShadowSettings.MaxWorldBias.Clone();
        private float _overlayAlpha = ShadowSettings.CascadeOverlayAlpha;
        private int _filterMode = Keyboard.GetIsHardShadow();
        private bool _linear = ShadowSettings.LinearShadowMap;

        // ── Named presets (persisted to shadow_presets.json next to the executable) ──
        private List<ShadowPresetData>? _presets;
        private int _selectedPreset;
        private string _presetName = "";

        // ── Small save-feedback notification (fades out, like the HierarchyPanel bar) ──
        private string _notifText = "";
        private float _notifTimer = 0f;

        private static readonly string[] FilterNames =
        [
            "PCF 16", "Hard Shadow", "PCF 16", "PCF 16 Soft", "PCF 32",
            "PCF 32 Soft", "PCSS 16", "PCSS 16 Soft", "PCSS 32", "PCSS 32 Soft",
        ];

        private static readonly string[] QualityNames = ["Low", "Medium", "High", "Ultra"];

        /// <summary>Constructor kept bridge-shaped for consistency with the other panels;
        /// the bridge is used for the CSM on/off toggle (same ShowShadows flag as the
        /// viewport toolbar "☀ Shadow" button).</summary>
        public ShadowPanel(IDEBridge bridge) => _bridge = bridge;

        public void ShowInMenu() => ImGui.MenuItem("Shadow Settings", null, ref _visible);

        public void Render()
        {
            if (!_visible) return;

            ImGui.Begin("Shadow Settings", ref _visible);

            // ── Save-feedback notification (top of the panel so it stays visible while
            // the user tweaks sliders; fades out over ~2.5 s) ──
            if (_notifTimer > 0f && !string.IsNullOrEmpty(_notifText))
            {
                _notifTimer -= ImGui.GetIO().DeltaTime;
                float alpha = Math.Clamp(_notifTimer, 0f, 1f);
                ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, alpha), $"✓ {_notifText}");
                ImGui.TextDisabled("Saved next to the executable (settings.json / shadow_presets.json).");
                ImGui.Separator();
            }

            // Keep the filter combo in sync if the H key cycles the mode.
            _filterMode = Keyboard.GetIsHardShadow();

            // ════════════════════════════════════════════════════════════════
            //  Cascade quality & split
            // ════════════════════════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Cascades", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // ── CSM on/off toggle — same flag as the viewport toolbar "☀ Shadow"
                // button (skips the shadow pass when off, maps read as fully lit). ──
                bool shadowsOn = _bridge?.ShowShadows ?? true;
                ImGui.PushStyleColor(ImGuiCol.Button, shadowsOn
                    ? new Vector4(0.55f, 0.45f, 0.20f, 1f)
                    : new Vector4(0.25f, 0.25f, 0.30f, 1f));
                if (ImGui.Button(shadowsOn ? "☀ CSM Shadows: On" : "☀ CSM Shadows: Off"))
                {
                    _bridge.ShowShadows = !shadowsOn;
                    PersistShadowToggle();
                    Console.WriteLine($"[Shadow] CSM shadows {(shadowsOn ? "disabled" : "enabled")}");
                    ShowSaveNotification(shadowsOn ? "CSM shadows disabled" : "CSM shadows enabled");
                }
                ImGui.PopStyleColor(1);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Toggle CSM shadows in the viewport\nOff = skip the shadow pass (fully lit, faster)\nSame toggle as the viewport toolbar ☀ Shadow button.");

                ImGui.Spacing();
                ImGui.Text("Quality preset:");
                for (int i = 0; i < QualityNames.Length; i++)
                {
                    if (i > 0) ImGui.SameLine();
                    if (ImGui.RadioButton(QualityNames[i], _quality == i)) ApplyQuality(i);
                }

                var sizes = ShadowSettings.CascadeSizes;
                ImGui.TextColored(new Vector4(0.7f, 0.75f, 0.85f, 1f),
                    $"Resolutions:  {sizes[0]} / {sizes[1]} / {sizes[2]}");

                ImGui.Spacing();
                ImGui.Text("Split distances (m):");
                bool layerChanged = false;
                bool splitReleased = false;
                for (int i = 0; i < 3; i++)
                {
                    ImGui.SetNextItemWidth(240);
                    if (ImGui.DragFloat(
                            $"Cascade {i + 1} split##layer{i}", ref _layer[i], 1f, 5f, 800f, "%.0f m"))
                    {
                        layerChanged = true;
                        // Keep splits strictly ascending so the cascade perspective
                        // projections never invert (prevSplit must be < nextSplit).
                        _layer[i] = i switch
                        {
                            0 => Math.Clamp(_layer[0], 5f, _layer[1] - 1f),
                            2 => Math.Clamp(_layer[2], _layer[1] + 1f, 800f),
                            _ => Math.Clamp(_layer[1], _layer[0] + 1f, _layer[2] - 1f),
                        };
                    }
                    if (ImGui.IsItemDeactivatedAfterEdit()) splitReleased = true;
                }
                if (layerChanged) ShadowSettings.ApplyCascadeLayer(_layer);
                if (splitReleased) PersistAndNotify();

                ImGui.Spacing();
                ImGui.SetNextItemWidth(240);
                if (ImGui.SliderFloat("Blend range##blend", ref _blendRange, 0.0f, 2.0f, "%.2f"))
                    ShadowSettings.BlendRange = _blendRange;
                if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();
                ImGui.TextDisabled("Blend width of the cascade transition, as a fraction of each split distance.");
            }

            // ════════════════════════════════════════════════════════════════
            //  Bias tuning
            // ════════════════════════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Bias", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextDisabled("Terrain / primitives:");
                ImGui.SetNextItemWidth(240);
                if (ImGui.SliderFloat("Constant bias##c", ref _constantBias, 0.0f, 0.01f, "%.5f"))
                    ShadowSettings.ConstantBias = _constantBias;
                if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();
                ImGui.SetNextItemWidth(240);
                if (ImGui.SliderFloat("Slope bias##slope", ref _slopeBias, 0.0f, 0.01f, "%.5f"))
                    ShadowSettings.SlopeBias = _slopeBias;
                if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();
                ImGui.SetNextItemWidth(240);
                if (ImGui.SliderFloat("Min bias##min", ref _minBias, 0.0f, 0.005f, "%.5f"))
                    ShadowSettings.MinBias = _minBias;
                if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();

                ImGui.Spacing();
                ImGui.TextDisabled("GLB / glTF models:");
                ImGui.SetNextItemWidth(240);
                if (ImGui.SliderFloat("Constant bias##gc", ref _gltfConstantBias, 0.0f, 0.01f, "%.5f"))
                    ShadowSettings.GltfConstantBias = _gltfConstantBias;
                if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();
                ImGui.SetNextItemWidth(240);
                if (ImGui.SliderFloat("Slope bias##gslope", ref _gltfSlopeBias, 0.0f, 0.01f, "%.5f"))
                    ShadowSettings.GltfSlopeBias = _gltfSlopeBias;
                if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();
                ImGui.SetNextItemWidth(240);
                if (ImGui.SliderFloat("Min bias##gmin", ref _gltfMinBias, 0.0f, 0.005f, "%.5f"))
                    ShadowSettings.GltfMinBias = _gltfMinBias;
                if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();

                ImGui.Spacing();
                ImGui.SetNextItemWidth(240);
                if (ImGui.SliderFloat("Normal bias (casting)##nb", ref _normalBias, 0.0f, 0.10f, "%.4f"))
                    ShadowSettings.NormalBias = _normalBias;
                if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();
                for (int i = 0; i < 3; i++)
                {
                    ImGui.SetNextItemWidth(240);
                    if (ImGui.SliderFloat($"Max world bias c{i + 1} (m)##wb{i}", ref _maxWorldBias[i], 0.05f, 400.0f, "%.2f m"))
                        ShadowSettings.MaxWorldBias[i] = _maxWorldBias[i];
                    if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();
                }

                ImGui.TextDisabled(
                    "Bias = max(Constant + Slope·(1−N·L), Min). Constant also lifts flat ground; "
                    + "Slope adds on steep faces; too much causes peter-panning.");
                ImGui.TextDisabled("Defaults ≈ 2.5 texels flat / 4 texels steep — kept constant at every distance by the texel-proportional per-cascade scaling.");
                ImGui.TextDisabled("Max world bias caps the bias' world offset (bias × depth range) so far cascades can't detach shadows by meters — cascade 1 tight, cascade 3 loose.");
            }

            // ════════════════════════════════════════════════════════════════
            //  Debug overlay
            // ════════════════════════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Debug overlay", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // ── CSM Level debug toggle — same state as the L key (transparent color
                // code per cascade: cyan / yellow / magenta). Changes apply instantly and
                // stay in sync with the keyboard toggle both ways. ──
                bool csmDebug = Keyboard.GetshowCSMCascadeColor();
                if (ImGui.Checkbox("Debug CSM Level (color code)", ref csmDebug))
                {
                    Keyboard.SetShowCSMCascadeColor(csmDebug);
                    Console.WriteLine($"[Shadow] CSM Level debug {(csmDebug ? "ON" : "OFF")}");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Tint each cascade with a transparent color code\nSame toggle as the L key — synced both ways.");

                // ── Legend: which color = which cascade level ──
                ImGui.Text("Legend:");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 1.0f, 1f), "●");
                ImGui.SameLine();
                ImGui.TextDisabled("cascade 0 (near)");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1f), "●");
                ImGui.SameLine();
                ImGui.TextDisabled("cascade 1 (mid)");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.0f, 1.0f, 1f), "●");
                ImGui.SameLine();
                ImGui.TextDisabled("cascade 2 (far)");

                ImGui.Spacing();
                ImGui.SetNextItemWidth(240);
                if (ImGui.SliderFloat("Cascade overlay alpha##oa", ref _overlayAlpha, 0.0f, 1.0f, "%.2f"))
                    ShadowSettings.CascadeOverlayAlpha = _overlayAlpha;
                if (ImGui.IsItemDeactivatedAfterEdit()) PersistAndNotify();
                ImGui.TextDisabled("Strength of the color-code tint: 0 = invisible, 0.15 = subtle, 0.35+ = strong.");
            }

            // ════════════════════════════════════════════════════════════════
            //  Filtering
            // ════════════════════════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Filtering", ImGuiTreeNodeFlags.DefaultOpen))
            {
                string cur = _filterMode >= 0 && _filterMode < FilterNames.Length
                    ? FilterNames[_filterMode]
                    : "?";
                ImGui.SetNextItemWidth(240);
                if (ImGui.BeginCombo("Shadow filter", cur))
                {
                    for (int i = 0; i < FilterNames.Length; i++)
                    {
                        bool selected = _filterMode == i;
                        if (ImGui.Selectable($"{i}: {FilterNames[i]}", selected))
                        {
                            _filterMode = i;
                            Keyboard.SetShadowFilterMode(i);
                            Console.WriteLine($"[Shadow] Filter mode: {i} ({FilterNames[i]})");
                            PersistAndNotify($"Filter: {FilterNames[i]}");
                        }
                    }
                    ImGui.EndCombo();
                }

                if (ImGui.Checkbox("Linear depth-map filtering", ref _linear))
                {
                    ShadowSettings.LinearShadowMap = _linear;
                    ShadowSettings.FilterVersion++;
                    Console.WriteLine($"[Shadow] Depth-map filter: {(_linear ? "LINEAR" : "NEAREST")}");
                    PersistAndNotify($"Depth-map filter: {(_linear ? "LINEAR" : "NEAREST")}");
                }

                ImGui.TextDisabled("Same modes as the H key. PCF/PCSS sample manually and expect NEAREST.");
            }

            // ════════════════════════════════════════════════════════════════
            //  Depth-range debug — live numbers from the last CSM UpdateMatrices,
            //  for verifying how the NDC bias translates into world-space offsets
            //  ════════════════════════════════════════════════════════════════
            // NOTE: the multiplier formula below must stay in sync with the shaders
            // (fragment_shader.glsl / terrainEditor_fragment.glsl / gltf_fragment.glsl).
            if (ImGui.CollapsingHeader("Depth range debug"))
            {
                var dr = CSM.LastDepthRanges;
                var tw = CSM.LastTexelWorld;
                float r0 = MathF.Max(dr[0], 1e-5f);
                float t0 = MathF.Max(tw[0], 1e-5f);
                // Effective per-cascade bias multipliers the shaders compute:
                // (texel_i / texel_0) × clamped depth-range ratio — keep in sync with the GLSL.
                float[] mult = [1f,
                    MathF.Max(tw[1] / t0, 1f) * Math.Clamp(r0 / MathF.Max(dr[1], 1e-5f), 0.02f, 4f),
                    MathF.Max(tw[2] / t0, 1f) * Math.Clamp(r0 / MathF.Max(dr[2], 1e-5f), 0.02f, 4f)];

                // Fragment-bias world offset for two extremes:
                // worldOffset = max(Const + Slope·(1−N·L), Min) × mult × depthRange,
                // then capped per cascade by ShadowSettings.MaxWorldBias (the peter-panning guard).
                float flatBase = MathF.Max(ShadowSettings.ConstantBias, ShadowSettings.MinBias);
                float steepBase = MathF.Max(ShadowSettings.ConstantBias + ShadowSettings.SlopeBias, ShadowSettings.MinBias);
                var maxWb = ShadowSettings.MaxWorldBias;

                ImGui.TextDisabled("Live from the last CSM update — refreshes every frame.");
                for (int i = 0; i < 3; i++)
                {
                    float range = dr[i];
                    float cap = maxWb[Math.Clamp(i, 0, maxWb.Length - 1)];
                    float flat = MathF.Min(flatBase * mult[i] * range, cap);
                    float steep = MathF.Min(steepBase * mult[i] * range, cap);
                    ImGui.Text($"Cascade {i}: range {range:F0} m · texel {tw[i]:F2} m · bias× {mult[i]:F2} · "
                             + $"offset flat {flat:F2} m / steep {steep:F2} m");
                }
                ImGui.TextDisabled($"Offset = bias_ndc × depth range, capped per cascade ({maxWb[0]:F2} / {maxWb[1]:F2} / {maxWb[2]:F2} m). Flat = N·L 1, steep = N·L 0 (terrain values).");
                ImGui.TextDisabled("World offset is texel-proportional: {flatBase * mult[0] * dr[0] / t0:F1} flat / {steepBase * mult[0] * dr[0] / t0:F1} steep texels in cascade 0 — same across cascades while the range clamp is inactive.");

                // PCF/PCSS softness — radius is in TEXELS, so per-cascade radius now scales
                // by texel_i/texel_0 (in the shaders: rScale_i) and the WORLD penumbra width
                // stays constant at every distance. Show that world width for the current mode.
                int debugMode = Math.Clamp(_filterMode, 0, FilterNames.Length - 1);
                float baseRadius = _filterMode switch
                {
                    3 or 5 => 10f,   // PCF 16/32 Soft
                    6 or 7 => 6f,    // PCSS 16
                    8 or 9 => 12f,   // PCSS 32
                    1 => 0f,         // Hard
                    _ => 5f,         // PCF 16 / PCF 32
                };
                if (baseRadius > 0f)
                {
                    ImGui.TextDisabled($"Filter {FilterNames[debugMode]}: radius {baseRadius:F0} texels → world penumbra ≈ {baseRadius * t0:F2} m across cascades (radius scales with texel size; far cascades floored at 0.25× radius).");
                }
                else
                {
                    ImGui.TextDisabled($"Filter {FilterNames[debugMode]}: hard shadow — no PCF/PCSS radius.");
                }

                ImGui.TextDisabled("NormalBias (casting) also scales per cascade (×texel ratio) — set at cascade 0, applies everywhere.");
                ImGui.TextDisabled("Adjust the bias sliders to taste — every cascade scales together. Range 1 m = CSM not updated yet.");
            }

            // ════════════════════════════════════════════════════════════════
            //  Presets — save/load named shadow configurations
            // ════════════════════════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Presets", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Lazy-load the preset list once per session (file lives next to the exe).
                _presets ??= ShadowPresetStore.Load();
                if (_selectedPreset >= _presets.Count) _selectedPreset = 0;

                // Name + Save current values as a preset.
                ImGui.SetNextItemWidth(240);
                ImGui.InputText("Preset name", ref _presetName, 64);
                if (ImGui.Button("Save preset") && !string.IsNullOrWhiteSpace(_presetName))
                {
                    string name = _presetName.Trim();
                    _presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                    _presets.Add(ShadowSettings.CapturePreset(name));
                    _selectedPreset = _presets.Count - 1;
                    ShadowPresetStore.Save(_presets);
                    ShowSaveNotification($"Preset '{name}' saved");
                    Console.WriteLine($"[Shadow] Preset '{name}' saved ({_presets.Count} total).");
                }

                ImGui.Spacing();
                if (_presets.Count == 0)
                {
                    ImGui.TextDisabled("No presets saved yet.");
                }
                else
                {
                    string[] names = new string[_presets.Count];
                    for (int i = 0; i < _presets.Count; i++) names[i] = _presets[i].Name ?? "(unnamed)";

                    ImGui.SetNextItemWidth(240);
                    if (ImGui.BeginCombo("Preset", names[_selectedPreset]))
                    {
                        for (int i = 0; i < names.Length; i++)
                        {
                            if (ImGui.Selectable(names[i], _selectedPreset == i)) _selectedPreset = i;
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Load"))
                    {
                        var p = _presets[_selectedPreset];
                        ShadowSettings.ApplyPreset(p);
                        RefreshMirrors();
                        PersistAndNotify($"Preset '{p.Name}' loaded");
                        Console.WriteLine($"[Shadow] Preset '{p.Name}' loaded.");
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Delete"))
                    {
                        var p = _presets[_selectedPreset];
                        _presets.RemoveAt(_selectedPreset);
                        if (_selectedPreset >= _presets.Count) _selectedPreset = Math.Max(0, _presets.Count - 1);
                        ShadowPresetStore.Save(_presets);
                        ShowSaveNotification($"Preset '{p.Name}' deleted");
                        Console.WriteLine($"[Shadow] Preset '{p.Name}' deleted ({_presets.Count} remaining).");
                    }

                    ImGui.TextDisabled("Load applies the preset live and persists it as your current settings.");
                }
            }

            ImGui.Separator();
            if (ImGui.Button("Reset to Defaults"))
            {
                ShadowSettings.ResetToDefaults();
                RefreshMirrors();
                Console.WriteLine("[Shadow] Settings reset to defaults.");
                PersistAndNotify("Shadow settings reset to defaults");
            }

            ImGui.End();
        }

        /// <summary>Persist the live shadow values and show the save-feedback bar.
        /// All Shadow panel changes funnel through here so the user always sees a
        /// small confirmation when settings.json was written.</summary>
        private void PersistAndNotify(string text = "Shadow settings saved")
        {
            if (ShadowSettings.Persist())
            {
                ShowSaveNotification(text);
            }
            else
            {
                ShowSaveNotification("Save failed — see console", 3.5f);
            }
        }

        /// <summary>Persist the CSM on/off flag to settings.json so it survives restarts
        /// (same persistence the viewport toolbar uses — kept here so the panel toggle
        /// and the toolbar toggle always agree).</summary>
        private void PersistShadowToggle()
        {
            try
            {
                var settings = SettingsSave.Load();
                settings.ShowShadows = _bridge.ShowShadows;
                SettingsSave.Save(settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Shadow] Failed to persist shadow toggle: {ex.Message}");
            }
        }

        /// <summary>Start the save-feedback bar with the given text and duration (s).</summary>
        private void ShowSaveNotification(string text, float duration = 2.5f)
        {
            _notifText = text;
            _notifTimer = duration;
        }

        /// <summary>Re-sync every local mirror with the live ShadowSettings values.
        /// Used after loading a preset or resetting to defaults.</summary>
        private void RefreshMirrors()
        {
            _quality = ShadowSettings.Quality;
            Array.Copy(ShadowSettings.CascadeLayer, _layer, _layer.Length);
            _constantBias = ShadowSettings.ConstantBias;
            _slopeBias = ShadowSettings.SlopeBias;
            _minBias = ShadowSettings.MinBias;
            _gltfConstantBias = ShadowSettings.GltfConstantBias;
            _gltfSlopeBias = ShadowSettings.GltfSlopeBias;
            _gltfMinBias = ShadowSettings.GltfMinBias;
            _blendRange = ShadowSettings.BlendRange;
            _normalBias = ShadowSettings.NormalBias;
            Array.Copy(ShadowSettings.MaxWorldBias, _maxWorldBias, _maxWorldBias.Length);
            _overlayAlpha = ShadowSettings.CascadeOverlayAlpha;
            _linear = ShadowSettings.LinearShadowMap;
            _filterMode = Keyboard.GetIsHardShadow();
        }

        private void ApplyQuality(int quality)
        {
            _quality = quality;
            ShadowSettings.ApplyQuality(quality);
            Console.WriteLine($"[Shadow] Quality: {QualityNames[quality]} → {string.Join("/", ShadowSettings.CascadeSizes)}");
            PersistAndNotify($"Quality set to {QualityNames[quality]}");
        }
    }
}
