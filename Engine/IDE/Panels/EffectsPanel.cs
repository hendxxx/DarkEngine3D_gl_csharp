using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System;
using System.Linq;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels
{
    /// <summary>
    /// Effects panel — every 2D effect knob in ONE place. User decision behind it:
    /// impacts play the HIT CLIP animation (the Hit FX particles became opt-in and
    /// default to none), and every effect has a SIZE control (scale) instead of a
    /// hard-coded burst amount.
    /// <list type="bullet">
    /// <item>Selected character's actions: Hit Sheet/Clip + Hit Scale (impact sprite
    /// size), optional Hit FX preset + FX Scale (moved here from the Inspector).</item>
    /// <item>Per-action FX (burst / follow stream) + Fx Scale.</item>
    /// <item>Effect2D emitter objects: preset, rate, wind, offset, enable.</item>
    /// <item>Global weather: rain toggle/intensity/wind slant + world wind.</item>
    /// </list>
    /// </summary>
    public class EffectsPanel
    {
        private readonly IDEBridge _bridge;
        private bool _visible = true;

        public EffectsPanel(IDEBridge bridge) => _bridge = bridge;

        public void ShowInMenu() => ImGui.MenuItem("Effects", null, ref _visible);

        public void Render()
        {
            if (!_visible) return;
            ImGui.Begin("Effects", ref _visible);
            IDE.PanelFocus.Notify("Effects");

            var obj = _bridge?.SelectedEditorObject;
            ImGui.TextDisabled("Impact = Hit CLIP (atur Hit Scale). Partikel = FX opsional.");
            ImGui.Separator();

            // ── Per-action effects for the selected character ──
            if (obj == null)
            {
                ImGui.TextDisabled("Pilih karakter (Player2D) untuk mengatur effect aksinya.");
            }
            else if (obj.Actions.Count == 0)
            {
                ImGui.TextDisabled($"'{obj.Name}' belum punya action (atur di Inspector).");
            }
            else
            {
                for (int i = 0; i < obj.Actions.Count; i++)
                {
                    var act = obj.Actions[i];
                    ImGui.PushID(i);
                    bool open = ImGui.TreeNodeEx($"{act.Name}", ImGuiTreeNodeFlags.Framed);
                    if (open)
                    {
                        RenderActionEffects(act);
                        ImGui.TreePop();
                    }
                    ImGui.PopID();
                }
            }

            // ── Effect2D emitter objects ──
            if (obj is { PrimitiveType: EditorPrimitiveType.Effect2D })
            {
                ImGui.Separator();
                RenderEmitterInspector(obj);
            }

            // ── Global weather: presets + per-layer manual controls ──
            ImGui.Separator();
            ImGui.Text("Global Weather");

            string[] wPresets = ["Clear Day", "Sunset", "Rain", "Storm", "Snow", "Fog"];
            foreach (var wp in wPresets)
            {
                // "##wx" suffix: hidden from the label but makes the ID unique — the
                // bare preset name collided with the Rain/Snow checkboxes below (ImGui
                // derives widget IDs from labels → "conflicting ID" error popup).
                if (ImGui.SmallButton(wp + "##wx"))
                    Effect2DSystem.ApplyWeatherPreset(wp);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(wp switch
                    {
                        "Clear Day" => "Langit cerah: semua lapisan cuaca mati (tint hangat tipis).",
                        "Sunset" => "Wash jingga senja di seluruh view.",
                        "Rain" => "Hujan + kabut tanah + tint biru-gelap.",
                        "Storm" => "Hujan lebat + kabut tebal + angin kencang + tint badai.",
                        "Snow" => "Salju turun + kabut tipis + tint dingin.",
                        "Fog" => "Kabut tanah pekat tanpa presipitasi.",
                        _ => "" });
                ImGui.SameLine();
            }
            if (ImGui.SmallButton("Clear##wx"))
                Effect2DSystem.ClearWeather();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Matikan SEMUA lapisan cuaca (rain/snow/fog/tint) sekaligus.");
            ImGui.TextDisabled($"Partikel aktif: {Effect2DSystem.ParticleCount}");

            bool rain = Effect2DSystem.RainEnabled;
            if (ImGui.Checkbox("Rain", ref rain)) Effect2DSystem.RainEnabled = rain;
            if (rain)
            {
                float ri = Effect2DSystem.RainIntensity;
                // 0..100 (user: "intensitasnya dibuat gede-an sampai 100"): 0.7 = sparse
                // reference look, 100 = storm wall (capped by the 4000-particle pool).
                if (ImGui.SliderFloat("Rain Intensity", ref ri, 0f, 100f, "%.1f"))
                    Effect2DSystem.RainIntensity = Math.Clamp(ri, 0f, 100f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("0.7 = hujan jarang (referensi). Naikkan sampai 100 untuk badai super lebat.");
                float rw = Effect2DSystem.RainWindFactor;
                if (ImGui.SliderFloat("Rain Wind Slant", ref rw, -2f, 2f, "%.2f"))
                    Effect2DSystem.RainWindFactor = rw;
                // Position + size of the rain band (user: "bisa ubah2 posisinya dan
                // ukuran nya"). Drag di luar slider = perubahan cepat.
                float rx = Effect2DSystem.RainOffsetX;
                if (ImGui.DragFloat("Rain Offset X", ref rx, 0.25f))
                    Effect2DSystem.RainOffsetX = rx;
                float ry = Effect2DSystem.RainOffsetY;
                if (ImGui.DragFloat("Rain Offset Y", ref ry, 0.25f))
                    Effect2DSystem.RainOffsetY = ry;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Geser area spawn hujan dari tengah kamera (positif = kanan/atas). Negatif Y = hujan turun lebih rendah.");
                float rs = Effect2DSystem.RainSizeMul;
                if (ImGui.SliderFloat("Rain Size", ref rs, 0.25f, 4f, "%.2f x"))
                    Effect2DSystem.RainSizeMul = Math.Clamp(rs, 0.25f, 4f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Besar-kecil tetesan (panjang + lebar garis). 1 = default.");
            }

            bool snow = Effect2DSystem.SnowEnabled;
            if (ImGui.Checkbox("Snow", ref snow)) Effect2DSystem.SnowEnabled = snow;
            if (snow)
            {
                float si = Effect2DSystem.SnowIntensity;
                // 0..100 — same headroom as rain (user request).
                if (ImGui.SliderFloat("Snow Intensity", ref si, 0f, 100f, "%.1f"))
                    Effect2DSystem.SnowIntensity = Math.Clamp(si, 0f, 100f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("0.7 = salju ringan. Naikkan sampai 100 untuk badai salju.");
                float sx = Effect2DSystem.SnowOffsetX;
                if (ImGui.DragFloat("Snow Offset X", ref sx, 0.25f))
                    Effect2DSystem.SnowOffsetX = sx;
                float sy = Effect2DSystem.SnowOffsetY;
                if (ImGui.DragFloat("Snow Offset Y", ref sy, 0.25f))
                    Effect2DSystem.SnowOffsetY = sy;
                float ss = Effect2DSystem.SnowSizeMul;
                if (ImGui.SliderFloat("Snow Size", ref ss, 0.25f, 4f, "%.2f x"))
                    Effect2DSystem.SnowSizeMul = Math.Clamp(ss, 0.25f, 4f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Besar-kecil kepingan salju. 1 = default.");
                float sr = Effect2DSystem.SnowRestSeconds;
                if (ImGui.SliderFloat("Snow Rest on Ground", ref sr, 0f, 20f, "%.1f s"))
                    Effect2DSystem.SnowRestSeconds = Math.Clamp(sr, 0f, 20f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Berapa lama kepingan DIAM di atas obstacle/collision yang kena (tanah, platform, kotak) sebelum memudar. 0 = langsung hilang.");
                // "Detect ALL tile layers" toggle REMOVED (user: buat selalu ON) —
                // every drawn tile on any visible layer/map is always a weather surface.
            }

            bool fog = Effect2DSystem.FogEnabled;
            if (ImGui.Checkbox("Fog (kabut tanah)", ref fog)) Effect2DSystem.FogEnabled = fog;
            if (fog)
            {
                float fh = Effect2DSystem.FogHeight;
                if (ImGui.DragFloat("Fog Height", ref fh, 0.05f, 0.1f, 8f, "%.2f"))
                    Effect2DSystem.FogHeight = Math.Clamp(fh, 0.1f, 8f);
                float fo = Effect2DSystem.FogOpacity;
                if (ImGui.SliderFloat("Fog Opacity", ref fo, 0f, 1f, "%.2f"))
                    Effect2DSystem.FogOpacity = Math.Clamp(fo, 0f, 1f);
            }

            bool tint = Effect2DSystem.TintEnabled;
            if (ImGui.Checkbox("Tint (warna ambien)", ref tint)) Effect2DSystem.TintEnabled = tint;
            if (tint)
            {
                var tc = Effect2DSystem.TintColor;
                if (ImGui.ColorEdit3("Tint Color", ref tc))
                    Effect2DSystem.TintColor = tc;
                float ts = Effect2DSystem.TintStrength;
                if (ImGui.SliderFloat("Tint Strength", ref ts, 0f, 1f, "%.2f"))
                    Effect2DSystem.TintStrength = Math.Clamp(ts, 0f, 1f);
            }

            float wind = Effect2DSystem.WindX;
            if (ImGui.DragFloat("Wind X (angin global)", ref wind, 0.05f, -10f, 10f, "%.2f"))
                Effect2DSystem.WindX = wind;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Angin dunia (world units/s). Hujan miring, kabut drift, partikel tergeser (WindFactor). Preset Storm menaikkan ini.");

            ImGui.End();
        }

        /// <summary>Hit Clip + Hit Scale + optional Hit FX + FX Scale + action FX for one action.</summary>
        private void RenderActionEffects(Player2DAction act)
        {
            if (act.ProjectileEnabled)
            {
                ImGui.Text("Hit Clip (animasi impact):");
                string sheet = act.ProjectileHitSheet, clip = act.ProjectileHitClip;
                if (ComboSheetClip("Hit Sheet", "Hit Clip", ref sheet, ref clip))
                {
                    act.ProjectileHitSheet = sheet;
                    act.ProjectileHitClip = clip;
                }

                float hs = act.ProjectileHitScale <= 0f ? 1f : act.ProjectileHitScale;
                if (ImGui.DragFloat("Hit Scale", ref hs, 0.05f, 0.05f, 10f, "%.2f x"))
                    act.ProjectileHitScale = Math.Clamp(hs, 0.05f, 10f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Besar-kecil animasi hit: 1 = sebesar projectile (World Height), 0.5 = setengah, 2 = dobel.");

                ImGui.Separator();
                ImGui.Text("Hit FX (partikel, opsional):");
                string[] fxCfg = ["", .. Effect2DSystem.Presets];
                string[] fxLabel = ["(none)", .. Effect2DSystem.Presets];
                int fxIdx = Array.IndexOf(fxCfg, act.ProjectileHitFx);
                if (fxIdx < 0) fxIdx = 0;
                if (ImGui.BeginCombo("Hit FX", fxLabel[fxIdx]))
                {
                    for (int fx = 0; fx < fxCfg.Length; fx++)
                    {
                        bool sel = fx == fxIdx;
                        if (ImGui.Selectable(fxLabel[fx], sel))
                            act.ProjectileHitFx = fxCfg[fx];
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Default (none) = impact HANYA Hit Clip. Pilih preset bila mau partikel tambahan.");
                if (!string.IsNullOrWhiteSpace(act.ProjectileHitFx))
                {
                    float fxs = act.ProjectileHitFxScale <= 0f ? 1f : act.ProjectileHitFxScale;
                    if (ImGui.DragFloat("Hit FX Scale", ref fxs, 0.05f, 0.05f, 8f, "%.2f x"))
                        act.ProjectileHitFxScale = Math.Clamp(fxs, 0.05f, 8f);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Besar-kecil burst partikel (jumlah + ukuran ikut skala).");
                }
            }
            else
            {
                ImGui.TextDisabled("Projectile OFF — aktifkan di Inspector bagian Projectile untuk pakai Hit Clip/FX.");
            }

            ImGui.Separator();
            ImGui.Text("Action FX (partikel dari karakter):");
            var presets = Effect2DSystem.Presets;
            string[] aCfg = ["", .. presets];
            string[] aLabel = ["(none)", .. presets];
            int aIdx = Array.IndexOf(aCfg, act.FxPreset);
            if (aIdx < 0) aIdx = 0;
            if (ImGui.BeginCombo("FX Preset", aLabel[aIdx]))
            {
                for (int f = 0; f < aCfg.Length; f++)
                {
                    bool sel = f == aIdx;
                    if (ImGui.Selectable(aLabel[f], sel))
                        act.FxPreset = aCfg[f];
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            if (!string.IsNullOrWhiteSpace(act.FxPreset))
            {
                bool follow = act.FxFollow;
                if (ImGui.Checkbox("Follow (aliran terus)", ref follow)) act.FxFollow = follow;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("ON = partikel mengalir dari karakter selama aksi main (jejak fireball). OFF = sekali burst saat aksi mulai.");
                float fs = act.FxScale <= 0f ? 1f : act.FxScale;
                if (ImGui.DragFloat("FX Scale", ref fs, 0.05f, 0.05f, 8f, "%.2f x"))
                    act.FxScale = Math.Clamp(fs, 0.05f, 8f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Besar-kecil FX aksi ini: burst mengatur jumlah+ukuran, follow mengatur ukuran partikel.");
            }
        }

        /// <summary>Effect2D emitter overrides for the selected emitter object.</summary>
        private void RenderEmitterInspector(EditorObject obj)
        {
            ImGui.Text($"Emitter: {obj.Name}");
            string[] presets = Effect2DSystem.Presets;
            int presetIdx = Array.IndexOf(presets, obj.Effect2DPreset);
            if (presetIdx < 0) presetIdx = 0;
            if (ImGui.BeginCombo("Preset", presets[presetIdx]))
            {
                for (int p = 0; p < presets.Length; p++)
                {
                    bool sel = p == presetIdx;
                    if (ImGui.Selectable(presets[p], sel)) obj.Effect2DPreset = presets[p];
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            float rate = obj.Effect2DEmitRate < 0f
                ? Effect2DSystem.PresetConfig(obj.Effect2DPreset).Rate
                : obj.Effect2DEmitRate;
            if (ImGui.DragFloat("Rate (partikel/detik)", ref rate, 0.5f, 0f, 500f, "%.1f"))
                obj.Effect2DEmitRate = Math.Max(0f, rate);
            if (ImGui.SmallButton("Pakai default preset"))
                obj.Effect2DEmitRate = -1f;
            float wind = obj.Effect2DWindFactor < 0f
                ? Effect2DSystem.PresetConfig(obj.Effect2DPreset).WindFactor
                : obj.Effect2DWindFactor;
            if (ImGui.SliderFloat("Wind Factor", ref wind, 0f, 2f, "%.2f"))
                obj.Effect2DWindFactor = Math.Clamp(wind, 0f, 2f);
            float offY = obj.Effect2DOffsetY;
            if (ImGui.DragFloat("Offset Y", ref offY, 0.05f))
                obj.Effect2DOffsetY = offY;
            bool enabled = obj.Effect2DEnabled;
            if (ImGui.Checkbox("Emitter aktif", ref enabled)) obj.Effect2DEnabled = enabled;
        }

        /// <summary>Shared sheet + clip dropdown pair (returns true when either changed;
        /// a sheet change clears the clip so the clip combo re-picks on the new sheet).</summary>
        private static bool ComboSheetClip(string idSheet, string idClip, ref string sheet, ref string clip)
        {
            bool changed = false;
            var sheets = IDEBridge.GetSpriteSheetNames();
            string[] sheetArr = sheets.Count > 0 ? sheets.ToArray() : [""];
            int sIdx = Array.IndexOf(sheetArr, sheet);
            if (sIdx < 0) sIdx = 0;
            if (ImGui.BeginCombo(idSheet, sheets.Count == 0 ? "(no sheets — import in Sprite Editor)" : sheetArr[sIdx]))
            {
                for (int s = 0; s < sheetArr.Length; s++)
                {
                    bool sel = s == sIdx;
                    if (ImGui.Selectable(sheetArr[s], sel) && !sheetArr[s].Equals(sheet, StringComparison.Ordinal))
                    {
                        sheet = sheetArr[s];
                        clip = "";
                        changed = true;
                    }
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            if (!string.IsNullOrEmpty(sheet) && sheets.Count > 0)
            {
                var clips = IDEBridge.GetClipNames(sheet);
                string[] clipArr = clips.Count > 0 ? clips.ToArray() : [""];
                int cIdx = Array.IndexOf(clipArr, clip);
                if (cIdx < 0) cIdx = 0;
                if (ImGui.BeginCombo(idClip, clipArr[cIdx]))
                {
                    for (int c = 0; c < clipArr.Length; c++)
                    {
                        bool sel = c == cIdx;
                        if (ImGui.Selectable(clipArr[c], sel))
                        {
                            clip = clipArr[c];
                            changed = true;
                        }
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            return changed;
        }
    }
}
