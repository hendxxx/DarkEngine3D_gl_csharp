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
                ImGui.TextDisabled("Pick a character (Player2D) to configure its action effects.");
            }
            else if (obj.Actions.Count == 0)
            {
                ImGui.TextDisabled($"'{obj.Name}' has no actions yet (set them in the Inspector).");
            }
            else
            {
                for (int i = 0; i < obj.Actions.Count; i++)
                {
                    var act = obj.Actions[i];
                    if (obj.PrimitiveType == EditorPrimitiveType.Player2D)
                        obj.InvalidatePlayer2DActionCaches();   // Jump/Fall cache — re-probe on any FX-panel edit
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

            string[] wPresets = ["Clear Day", "Sunset", "Rain", "Storm", "Snow", "Fog", "Mist"];
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
                        "Clear Day" => "Clear sky: all weather layers off (a thin warm tint).",
                        "Sunset" => "Wash jingga senja di seluruh view.",
                        "Rain" => "Hujan + kabut tanah + tint biru-gelap.",
                        "Storm" => "Hujan lebat + kabut tebal + angin kencang + tint badai.",
                        "Snow" => "Salju turun + kabut tipis + tint dingin.",
                        "Fog" => "Kabut TEBAL: gumpalan awan besar dan padat menutupi view, abu-abu lembut.",
                        "Mist" => "Kabut TIPIS: banyak serpihan kecil memanjang dekat tanah.",
                        _ => "" });
                ImGui.SameLine();
            }
            if (ImGui.SmallButton("Clear##wx"))
                Effect2DSystem.ClearWeather();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Turn OFF all weather layers (rain/snow/fog/tint) at once.");
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
                    ImGui.SetTooltip("0.7 = light rain (reference). Raise up to 100 for a super heavy storm.");
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
                    ImGui.SetTooltip("Shift the rain spawn area from the camera center (positive = right/up). Negative Y = rain lands lower.");
                float rs = Effect2DSystem.RainSizeMul;
                if (ImGui.SliderFloat("Rain Size", ref rs, 0.25f, 4f, "%.2f x"))
                    Effect2DSystem.RainSizeMul = Math.Clamp(rs, 0.25f, 4f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Drop size (line length + width). 1 = default.");

                float rsp = Effect2DSystem.RainSplashSize;
                if (ImGui.SliderFloat("Splash Size", ref rsp, 0.25f, 4f, "%.2f x"))
                    Effect2DSystem.RainSplashSize = Math.Clamp(rsp, 0.25f, 4f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Ukuran percikan (lengkung kecil di tanah + titik kecil). 1.2 = default.");

                int rsa = Effect2DSystem.RainSplashAmount;
                if (ImGui.SliderInt("Splash Drops", ref rsa, 0, 6))
                    Effect2DSystem.RainSplashAmount = Math.Clamp(rsa, 0, 6);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Jumlah titik kecil di atas lengkung. 2-4 = referensi, 0 = lengkung saja.");
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
                    ImGui.SetTooltip("0.7 = light snow. Raise up to 100 for a blizzard.");
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
                    ImGui.SetTooltip("Snowflake size. 1 = default.");
                float sr = Effect2DSystem.SnowRestSeconds;
                if (ImGui.SliderFloat("Snow Rest on Ground", ref sr, 0f, 20f, "%.1f s"))
                    Effect2DSystem.SnowRestSeconds = Math.Clamp(sr, 0f, 20f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("How long flakes STAY still on top of the obstacle/collision they hit (ground, platform, box) before fading. 0 = vanish instantly.");
                // "Detect ALL tile layers" toggle REMOVED (user: buat selalu ON) —
                // every drawn tile on any visible layer/map is always a weather surface.
            }

            bool fog = Effect2DSystem.FogEnabled;
            if (ImGui.Checkbox("Fog (thick cloud bank)", ref fog)) Effect2DSystem.FogEnabled = fog;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Kabut TEBAL: gumpalan awan besar (bentuk awan, bukan lingkaran halus) yang bergerak mengikuti angin, plus haze dasar.");
            if (fog)
            {
                float fcvg = Effect2DSystem.FogCoverage;
                if (ImGui.SliderFloat("Fog Coverage", ref fcvg, 0.05f, 1f, "%.2f"))
                    Effect2DSystem.FogCoverage = Math.Clamp(fcvg, 0.05f, 1f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Seberapa tinggi bank kabut menutupi view (0.70 = sebagian besar layar).");

                int fp = Effect2DSystem.FogPuffs;
                if (ImGui.SliderInt("Fog Clouds", ref fp, 1, 32))
                    Effect2DSystem.FogPuffs = fp;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Jumlah gumpalan awan besar. Tiap gumpalan = 1 massa + 3 lobus, jadi 12-16 sudah menutupi layar.");

                float fps = Effect2DSystem.FogPuffSize;
                if (ImGui.SliderFloat("Puff Size", ref fps, 0.1f, 2f, "%.2f x"))
                    Effect2DSystem.FogPuffSize = Math.Clamp(fps, 0.1f, 2f);

                float fo = Effect2DSystem.FogOpacity;
                if (ImGui.SliderFloat("Puff Opacity", ref fo, 0f, 1f, "%.2f"))
                    Effect2DSystem.FogOpacity = Math.Clamp(fo, 0f, 1f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Alpha per puff. Puff saling tumpuk, jadi 0.2-0.4 sudah pekat.");

                float fbh = Effect2DSystem.FogBaseHaze;
                if (ImGui.SliderFloat("Base Haze", ref fbh, 0f, 1f, "%.2f"))
                    Effect2DSystem.FogBaseHaze = Math.Clamp(fbh, 0f, 1f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Haze rata di bawah puff, mengisi celah antar awan. 0 = hanya puff.");

                float fsp = Effect2DSystem.FogSpeed;
                if (ImGui.SliderFloat("Fog Drift Speed", ref fsp, 0f, 4f, "%.2f x"))
                    Effect2DSystem.FogSpeed = Math.Clamp(fsp, 0f, 4f);

                var fcol = Effect2DSystem.FogColor;
                if (ImGui.ColorEdit3("Fog Color", ref fcol))
                    Effect2DSystem.FogColor = fcol;

                bool fut = Effect2DSystem.FogUseTint;
                if (ImGui.Checkbox("Fog follows Tint", ref fut)) Effect2DSystem.FogUseTint = fut;
            }

            bool mist = Effect2DSystem.MistEnabled;
            if (ImGui.Checkbox("Mist (thin low wisps)", ref mist)) Effect2DSystem.MistEnabled = mist;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Kabut TIPIS: serpihan kecil memanjang dekat tanah, bergerak mengikuti angin.");
            if (mist)
            {
                int mw = Effect2DSystem.MistWisps;
                if (ImGui.SliderInt("Mist Wisps", ref mw, 1, 96))
                    Effect2DSystem.MistWisps = mw;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Jumlah serpihan kabut tipis. 50-70 = tebal seperti referensi.");

                float mh = Effect2DSystem.MistHeight;
                if (ImGui.SliderFloat("Mist Height", ref mh, 0.02f, 1f, "%.2f"))
                    Effect2DSystem.MistHeight = Math.Clamp(mh, 0.02f, 1f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Tinggi zona serpihan dari bawah view (0.20 = hanya dekat tanah).");

                float ml = Effect2DSystem.MistLength;
                if (ImGui.SliderFloat("Wisp Length", ref ml, 0.02f, 0.60f, "%.2f x"))
                    Effect2DSystem.MistLength = Math.Clamp(ml, 0.02f, 0.60f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Panjang tiap serpihan (fraksi lebar view). Kecil = potongan pendek; besar = garis panjang.");

                float mt = Effect2DSystem.MistThickness;
                if (ImGui.SliderFloat("Wisp Thickness", ref mt, 0.004f, 0.08f, "%.3f"))
                    Effect2DSystem.MistThickness = Math.Clamp(mt, 0.004f, 0.08f);

                float mo = Effect2DSystem.MistOpacity;
                if (ImGui.SliderFloat("Mist Opacity", ref mo, 0f, 1f, "%.2f"))
                    Effect2DSystem.MistOpacity = Math.Clamp(mo, 0f, 1f);

                float mspd = Effect2DSystem.MistSpeed;
                if (ImGui.SliderFloat("Mist Drift Speed", ref mspd, 0f, 4f, "%.2f x"))
                    Effect2DSystem.MistSpeed = Math.Clamp(mspd, 0f, 4f);

                var mc = Effect2DSystem.MistColor;
                if (ImGui.ColorEdit3("Mist Color", ref mc))
                    Effect2DSystem.MistColor = mc;

                bool mut = Effect2DSystem.MistUseTint;
                if (ImGui.Checkbox("Mist follows Tint", ref mut)) Effect2DSystem.MistUseTint = mut;

                float mwd = Effect2DSystem.MistWindDrift;
                if (ImGui.SliderFloat("Mist Wind Drift", ref mwd, 0f, 1f, "%.2f"))
                    Effect2DSystem.MistWindDrift = Math.Clamp(mwd, 0f, 1f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Seberapa kuat angin mengubah kecepatan/arah serpihan.");
            }

            bool tint = Effect2DSystem.TintEnabled;
            if (ImGui.Checkbox("Tint (ambient color)", ref tint)) Effect2DSystem.TintEnabled = tint;
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
                ImGui.SetTooltip("World wind (world units/s). Slants rain, drifts fog, pushes particles (WindFactor). The Storm preset raises this.");

            // ── Pixel atmosphere FX — all procedural (no sprite assets) ──
            ImGui.Separator();
            if (ImGui.CollapsingHeader("Pixel FX (petir / god rays / heat / frost / wind)"))
            {
                // Lightning
                bool bolt = Effect2DSystem.LightningEnabled;
                if (ImGui.Checkbox("Lightning (petir)", ref bolt)) Effect2DSystem.LightningEnabled = bolt;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Petir prosedural: bolt bercabang dari atas layar ke permukaan tanah + kilat layar.");
                if (bolt)
                {
                    float li = Effect2DSystem.LightningInterval;
                    if (ImGui.DragFloat("Strike Interval (s)", ref li, 0.1f, 0f, 60f, "%.2f"))
                        Effect2DSystem.LightningInterval = Math.Clamp(li, 0f, 60f);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Jeda antar sambaran otomatis. 0 = hanya lewat tombol Strike now.");

                    float lt = Effect2DSystem.LightningThickness;
                    if (ImGui.SliderFloat("Bolt Thickness", ref lt, 0.005f, 0.30f, "%.3f"))
                        Effect2DSystem.LightningThickness = Math.Clamp(lt, 0.005f, 0.30f);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Tebal inti bolt (fraksi tinggi setengah layar).");

                    float lf = Effect2DSystem.LightningFlash;
                    if (ImGui.SliderFloat("Flash Strength", ref lf, 0f, 1f, "%.2f"))
                        Effect2DSystem.LightningFlash = Math.Clamp(lf, 0f, 1f);

                    var lc = Effect2DSystem.LightningColor;
                    if (ImGui.ColorEdit3("Bolt Color", ref lc))
                        Effect2DSystem.LightningColor = lc;

                    if (ImGui.Button("Strike now")) Effect2DSystem.StrikeLightning();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Sambar sekarang (tanpa menunggu interval).");
                }

                // God rays
                bool rays = Effect2DSystem.GodRaysEnabled;
                if (ImGui.Checkbox("God Rays (sun shafts)", ref rays)) Effect2DSystem.GodRaysEnabled = rays;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Berkas cahaya diagonal dari sumber di atas layar, berkedip lembut.");
                if (rays)
                {
                    int rc = Effect2DSystem.GodRaysCount;
                    if (ImGui.SliderInt("Ray Count", ref rc, 1, 24)) Effect2DSystem.GodRaysCount = rc;

                    float rsx = Effect2DSystem.GodRaysStrength;
                    if (ImGui.SliderFloat("Ray Strength", ref rsx, 0f, 1f, "%.2f"))
                        Effect2DSystem.GodRaysStrength = Math.Clamp(rsx, 0f, 1f);

                    float rdir = Effect2DSystem.GodRaysAngle;
                    if (ImGui.SliderFloat("Ray Direction (deg)", ref rdir, 0f, 360f, "%.0f"))
                        Effect2DSystem.GodRaysAngle = rdir;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("0 = kanan, 90 = atas, 236 = kiri-bawah (default referensi).");

                    float rspread = Effect2DSystem.GodRaysSpread;
                    if (ImGui.SliderFloat("Ray Spread (deg)", ref rspread, 0f, 160f, "%.0f"))
                        Effect2DSystem.GodRaysSpread = Math.Clamp(rspread, 0f, 160f);

                    float rlen = Effect2DSystem.GodRaysLength;
                    if (ImGui.SliderFloat("Ray Length", ref rlen, 0.5f, 4f, "%.2f x"))
                        Effect2DSystem.GodRaysLength = Math.Clamp(rlen, 0.5f, 4f);

                    var rcol = Effect2DSystem.GodRaysColor;
                    if (ImGui.ColorEdit3("Ray Color", ref rcol))
                        Effect2DSystem.GodRaysColor = rcol;
                }

                // Wind streaks
                bool ws = Effect2DSystem.WindStreaksEnabled;
                if (ImGui.Checkbox("Wind Streaks", ref ws)) Effect2DSystem.WindStreaksEnabled = ws;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Garis angin horizontal melintasi layar, arahnya ikut Wind X.");
                if (ws)
                {
                    float wa = Effect2DSystem.WindStreakAmount;
                    if (ImGui.SliderFloat("Streak Amount", ref wa, 0.1f, 4f, "%.2f x"))
                        Effect2DSystem.WindStreakAmount = Math.Clamp(wa, 0.1f, 4f);

                    float wsp = Effect2DSystem.WindStreakSpeed;
                    if (ImGui.SliderFloat("Streak Speed", ref wsp, 0f, 5f, "%.2f x"))
                        Effect2DSystem.WindStreakSpeed = Math.Clamp(wsp, 0f, 5f);

                    float wh = Effect2DSystem.WindStreakHeight;
                    if (ImGui.SliderFloat("Wind Height", ref wh, 0.1f, 2f, "%.2f x"))
                        Effect2DSystem.WindStreakHeight = Math.Clamp(wh, 0.1f, 2f);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Tebal pita angin terhadap tinggi layar. 0.1 = pita tipis di tengah, 1 = setengah layar, 2 = penuh.");

                    var wcol = Effect2DSystem.WindStreakColor;
                    if (ImGui.ColorEdit3("Streak Color", ref wcol))
                        Effect2DSystem.WindStreakColor = wcol;

                    int wsm = Effect2DSystem.WindStreakMode;
                    if (ImGui.SliderInt("Streak Style", ref wsm, 0, 2, wsm == 0 ? "0 Lurus (garis ekor)" : wsm == 1 ? "1 Swirl (kumparan slynyrd)" : "2 Campur swirl + lurus"))
                        Effect2DSystem.WindStreakMode = wsm;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("0 = lurus tailed dashes. 1 = swirls: tanda koma mengkait dengan ekor memudar (gaya slynyrd). 2 = campur swirl + garis lurus + debu.");
                }

                // Heat haze
                bool hh = Effect2DSystem.HeatHazeEnabled;
                if (ImGui.Checkbox("Heat Haze", ref hh)) Effect2DSystem.HeatHazeEnabled = hh;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Gelombang udara panas di bagian bawah view (garis shimmer bergoyang).");
                if (hh)
                {
                    float hs = Effect2DSystem.HeatHazeStrength;
                    if (ImGui.SliderFloat("Haze Strength", ref hs, 0f, 1f, "%.2f"))
                        Effect2DSystem.HeatHazeStrength = Math.Clamp(hs, 0f, 1f);

                    float hhgt = Effect2DSystem.HeatHazeHeight;
                    if (ImGui.SliderFloat("Haze Height", ref hhgt, 0.05f, 1f, "%.2f"))
                        Effect2DSystem.HeatHazeHeight = Math.Clamp(hhgt, 0.05f, 1f);

                    float hsp = Effect2DSystem.HeatHazeSpeed;
                    if (ImGui.SliderFloat("Haze Speed", ref hsp, 0f, 4f, "%.2f x"))
                        Effect2DSystem.HeatHazeSpeed = Math.Clamp(hsp, 0f, 4f);

                    var hcol = Effect2DSystem.HeatHazeColor;
                    if (ImGui.ColorEdit3("Haze Color", ref hcol))
                        Effect2DSystem.HeatHazeColor = hcol;
                }

                // Frost
                bool fr = Effect2DSystem.FrostEnabled;
                if (ImGui.Checkbox("Frost", ref fr)) Effect2DSystem.FrostEnabled = fr;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Kristal es prosedural di tepi layar (seperti bunga es di kaca).");
                if (fr)
                {
                    float fs = Effect2DSystem.FrostStrength;
                    if (ImGui.SliderFloat("Frost Strength", ref fs, 0f, 1f, "%.2f"))
                        Effect2DSystem.FrostStrength = Math.Clamp(fs, 0f, 1f);

                    float fd = Effect2DSystem.FrostDensity;
                    if (ImGui.SliderFloat("Frost Density", ref fd, 0.1f, 4f, "%.2f x"))
                        Effect2DSystem.FrostDensity = Math.Clamp(fd, 0.1f, 4f);

                    var fcol = Effect2DSystem.FrostColor;
                    if (ImGui.ColorEdit3("Frost Color", ref fcol))
                        Effect2DSystem.FrostColor = fcol;

                    if (ImGui.Button("New Frost Pattern")) Effect2DSystem.ReshuffleFrost();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Acak ulang pola bunga es.");
                }
            }

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
                    ImGui.SetTooltip("Hit animation size: 1 = the projectile's size (World Height), 0.5 = half, 2 = double.");

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
                    ImGui.SetTooltip("Default (none) = the impact plays ONLY the Hit Clip. Pick a preset if you want extra particles.");
                if (!string.IsNullOrWhiteSpace(act.ProjectileHitFx))
                {
                    float fxs = act.ProjectileHitFxScale <= 0f ? 1f : act.ProjectileHitFxScale;
                    if (ImGui.DragFloat("Hit FX Scale", ref fxs, 0.05f, 0.05f, 8f, "%.2f x"))
                        act.ProjectileHitFxScale = Math.Clamp(fxs, 0.05f, 8f);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Particle burst size (count + size scale together).");
                }
            }
            else
            {
                ImGui.TextDisabled("Projectile OFF — enable it in the Inspector's Projectile section to use Hit Clip/FX.");
            }

            ImGui.Separator();
            ImGui.Text("Action FX (particles from the character):");
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
                    ImGui.SetTooltip("ON = particles stream from the character while the action plays (fireball trail). OFF = a single burst when the action starts.");
                float fs = act.FxScale <= 0f ? 1f : act.FxScale;
                if (ImGui.DragFloat("FX Scale", ref fs, 0.05f, 0.05f, 8f, "%.2f x"))
                    act.FxScale = Math.Clamp(fs, 0.05f, 8f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Size of this action's FX: burst scales count+size, follow scales particle size.");
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
