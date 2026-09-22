using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using System;
using System.IO;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels
{
    /// <summary>
    /// Terrain panel — the plane-terrain workflow in ONE dedicated place (never in the
    /// PBR Material panel): 4-layer splat painting with height bands / slope auto-rock /
    /// triplanar, the viewport sculpt/paint brush, and the per-chunk LOD + occlusion
    /// optimizations. Applies to <see cref="EditorPrimitiveType.Plane"/> only. The height
    /// map input AND the vertex-displacement grid live in the PBR panel (next to the
    /// Height / Displacement map slot they consume) — deliberately not duplicated here.
    /// </summary>
    public class TerrainPanel
    {
        private readonly IDEBridge _bridge;
        private bool _visible = true;

        public TerrainPanel(IDEBridge bridge) => _bridge = bridge;

        public void ShowInMenu() => ImGui.MenuItem("Terrain", null, ref _visible);

        public void Render()
        {
            if (!_visible) return;

            ImGui.Begin("Terrain", ref _visible);
            IDE.PanelFocus.Notify("Terrain");

            var obj = _bridge?.SelectedEditorObject;
            if (obj == null)
            {
                ImGui.TextDisabled("Select a Plane in the scene to edit it as terrain.");
                ImGui.End();
                return;
            }

            if (obj.PrimitiveType != EditorPrimitiveType.Plane)
            {
                ImGui.TextDisabled($"{obj.PrimitiveType}: terrain tools apply to a Plane primitive only.");
                ImGui.End();
                return;
            }

            ImGui.TextColored(new Vector4(0.6f, 0.85f, 1f, 1f), $"◉ {obj.Name}");
            ImGui.TextDisabled(obj.PrimitiveType.ToString());
            ImGui.Separator();

            RenderSplat(obj);
            ImGui.Separator();
            RenderBrush(obj);

            ImGui.End();
        }

        // unsafe: drag-drop payload pointer.
        // ── Splat painting ───────────────────────────────────────────────────────────
        private unsafe void RenderSplat(EditorObject obj)
        {
            ImGui.TextColored(new Vector4(0.55f, 0.95f, 0.75f, 1f), "Terrain Paint (Splat)");
            ImGui.TextDisabled("Paint up to 4 albedo layers + sculpt the terrain height in the viewport brush.");

            // ── Layer slots: albedo path (drag-drop / clear) + tint ──
            for (int i = 0; i < EditorObject.MaxSplatLayers; i++)
            {
                var layer = obj.EnsureSplatLayer(i);
                ImGui.PushID($"splat_{i}");
                string tmp = layer.AlbedoPath ?? "";
                ImGui.Text($"Layer {i + 1}:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-70);
                if (ImGui.InputText("##albedo", ref tmp, 512))
                {
                    layer.AlbedoPath = tmp.Trim();
                    obj.InvalidatePbrSplatTextures();
                }
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    {
                        layer.AlbedoPath = PathHelpers.MakeRelative(AssetBrowserPanel._dragImagePath);
                        AssetBrowserPanel._dragImagePath = null;
                        obj.InvalidatePbrSplatTextures();
                    }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.Button("X"))
                {
                    layer.AlbedoPath = "";
                    obj.InvalidatePbrSplatTextures();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear this layer's albedo.");

                Vector3 tint = layer.Tint;
                if (ImGui.ColorEdit3("Tint", ref tint, ImGuiColorEditFlags.NoInputs))
                {
                    layer.TintR = tint.X; layer.TintG = tint.Y; layer.TintB = tint.Z;
                }
                ImGui.PopID();
            }

            // ── Height layers: auto-terrain bands by ELEVATION (valley→peak) ──
            ImGui.Separator();
            bool hAuto = obj.SplatHeightLayersEnabled;
            if (ImGui.Checkbox("Height Layers (auto by elevation)", ref hAuto))
                obj.SplatHeightLayersEnabled = hAuto;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Layers auto-assigned by the TERRAIN height map elevation:\nlayer 1 = valleys ... layer N = peaks. Brush paint still overrides locally.");
            if (hAuto)
            {
                int hc = obj.SplatHeightLayerCount;
                if (ImGui.SliderInt("Active bands##hlayer", ref hc, 1, 4)) obj.SplatHeightLayerCount = hc;
                float hf = obj.SplatHeightLayerFeather;
                if (ImGui.SliderFloat("Band feather##hlayer", ref hf, 0.01f, 0.5f)) obj.SplatHeightLayerFeather = hf;
                if (string.IsNullOrEmpty(obj.TerrainHeightSourcePath) && obj._sculptHeights == null)
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1f), "Assign a Height / Displacement map (PBR panel) — bands need elevation data.");
                else
                    ImGui.TextDisabled($"Bands: layer 1 = low ... layer {Math.Clamp(obj.SplatHeightLayerCount, 1, 4)} = high elevation");
            }

            // ── Per-layer ELEVATION BANDS (legacy-terrain parity): HeightMin/HeightMax per
            //    layer — a layer fades in inside its [min,max] elevation range; overlapping
            //    adjacent ranges cross-fade. -1/-1 = band off for that layer.
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.7f, 0.85f, 1f, 1f), "Layer Height Bands (min/max)");
            int bedit = Math.Clamp(obj.SplatBandEditLayer, 0, 3);
            if (ImGui.BeginCombo("Layer##bandedit", $"Layer {bedit + 1}"))
            {
                for (int i = 0; i < 4; i++)
                    if (ImGui.Selectable($"Layer {i + 1}", bedit == i)) obj.SplatBandEditLayer = i;
                ImGui.EndCombo();
            }
            {
                int bi = bedit * 2;
                float bmin = obj.SplatHeightBands[bi], bmax = obj.SplatHeightBands[bi + 1];
                bool bandOn = bmax > bmin;
                bool on = bandOn;
                if (ImGui.Checkbox("Band enabled##bandon", ref on))
                {
                    if (on && !bandOn) { obj.SplatHeightBands[bi] = 0f; obj.SplatHeightBands[bi + 1] = 1f; }
                    if (!on) { obj.SplatHeightBands[bi] = -1f; obj.SplatHeightBands[bi + 1] = -1f; }
                }
                if (on)
                {
                    float lo = bandOn ? bmin : 0f, hiB = bandOn ? bmax : 1f;
                    if (ImGui.SliderFloat("Height min##band", ref lo, 0f, 1f)) obj.SplatHeightBands[bi] = lo;
                    if (ImGui.SliderFloat("Height max##band", ref hiB, 0f, 1f)) obj.SplatHeightBands[bi + 1] = hiB;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Elevation range where this layer shows (normalized terrain height).\nOverlapping neighboring bands blend smoothly.");
                    if (obj.SplatHeightBands[bi + 1] <= obj.SplatHeightBands[bi])
                        ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1f), "Max must be above min (band disabled otherwise).");
                }
            }
            if (ImGui.Button("Equal ranges (auto N bands)##bands"))
            {
                // Fill the active bands 0..N-1 with adjacent equal elevation slices
                // (legacy auto-band preset) and clear the rest.
                int n = Math.Clamp(obj.SplatHeightLayerCount, 1, 4);
                for (int i = 0; i < 4; i++)
                {
                    if (i < n)
                    {
                        obj.SplatHeightBands[i * 2] = i / (float)n;
                        obj.SplatHeightBands[i * 2 + 1] = (i + 1) / (float)n;
                    }
                    else
                    {
                        obj.SplatHeightBands[i * 2] = -1f;
                        obj.SplatHeightBands[i * 2 + 1] = -1f;
                    }
                }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Preset: N equal elevation slices for layers 1..N\n(uses the Active bands value above); other layers off.");
            bool lheat = obj.SplatShowLayerHeatmap;
            if (ImGui.Checkbox("Show layer heatmap", ref lheat))
                obj.SplatShowLayerHeatmap = lheat;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Viewport overlay of the band weights: hue = dominant layer\n(blue/green/yellow/red = 1-4), brightness = weight. Not saved.");
            if (lheat)
                ImGui.TextDisabled("1=blue 2=green 3=yellow 4=red · dark = no band");

            // ── Slope auto-paint: rock/cliff takes over steep terrain (no brush) ──
            ImGui.Separator();
            bool slopeAuto = obj.SplatSlopeEnabled;
            if (ImGui.Checkbox("Slope Auto-Paint (rock on steep)", ref slopeAuto))
                obj.SplatSlopeEnabled = slopeAuto;
            if (slopeAuto)
            {
                int sl = obj.SplatSlopeLayer;
                if (ImGui.SliderInt("Rock layer##slope", ref sl, 0, 3)) obj.SplatSlopeLayer = sl;
                float sth = obj.SplatSlopeThreshold;
                if (ImGui.SliderFloat("Slope threshold##slope", ref sth, 0f, 1f)) obj.SplatSlopeThreshold = sth;
                float sfe = obj.SplatSlopeFeather;
                if (ImGui.SliderFloat("Slope feather##slope", ref sfe, 0.01f, 0.5f)) obj.SplatSlopeFeather = sfe;
                float stilS = obj.SplatSlopeTiling;
                if (ImGui.DragFloat("Slope tiling##slope", ref stilS, 0.05f, 0.05f, 16f)) obj.SplatSlopeTiling = stilS;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("World tiling for the rock texture — separate from Splat Tiling\n(cliffs usually need a different density than the ground layers).");
                if (string.IsNullOrEmpty(obj.EnsureSplatLayer(Math.Clamp(obj.SplatSlopeLayer, 0, 3)).AlbedoPath))
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1f), "Assign an albedo texture to the rock layer (above).");

                bool showMask = obj.SplatShowSlopeMask;
                if (ImGui.Checkbox("Show slope mask (heatmap)", ref showMask))
                    obj.SplatShowSlopeMask = showMask;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Viewport overlay: blue = flat, green = approaching the threshold,\ngreen→red = feather zone, red = full rock. Not saved.");
            }

            // ── Triplanar + tiling + strength ──
            ImGui.Separator();
            bool triplanar = obj.SplatTriplanar;
            if (ImGui.Checkbox("Triplanar sampling (world-space)", ref triplanar))
                obj.SplatTriplanar = triplanar;
            float stiling = obj.SplatTiling;
            if (ImGui.DragFloat("Splat Tiling##splat", ref stiling, 0.05f, 0.05f, 16f)) obj.SplatTiling = stiling;
            float sstrength = obj.SplatPaintStrength;
            if (ImGui.DragFloat("Paint Strength##splat", ref sstrength, 0.05f, 0f, 1f)) obj.SplatPaintStrength = sstrength;

            if (ImGui.Button("Clear splat paint"))
                obj.ClearSplat(-1);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Erase ALL painted layer weights (albedo paths are kept).");
        }

        // ── Viewport brush + optimization ────────────────────────────────────────────
        private void RenderBrush(EditorObject obj)
        {
            ImGui.TextColored(new Vector4(0.55f, 0.95f, 0.75f, 1f), "Viewport Brush");
            bool brushOn = _bridge.PbrSplatBrushActive;
            if (ImGui.Checkbox("Enable brush (click-drag in viewport)", ref brushOn))
                _bridge.PbrSplatBrushActive = brushOn;
            int bmode = _bridge.PbrSplatBrushMode;
            if (ImGui.BeginCombo("Brush mode##splatbrush", bmode switch
            {
                1 => "Paint layer",
                2 => "Smooth",
                3 => "Flatten",
                _ => "Sculpt",
            }))
            {
                for (int m = 0; m < 4; m++)
                {
                    string name = m switch { 1 => "Paint layer", 2 => "Smooth", 3 => "Flatten", _ => "Sculpt" };
                    if (ImGui.Selectable(name, bmode == m)) _bridge.PbrSplatBrushMode = m;
                }
                ImGui.EndCombo();
            }
            int bfalloff = obj.TerrainBrushFalloff;
            if (ImGui.BeginCombo("Falloff##splatbrush", bfalloff switch { 0 => "Linear", 2 => "Sharp", _ => "Smooth" }))
            {
                if (ImGui.Selectable("Linear", bfalloff == 0)) obj.TerrainBrushFalloff = 0;
                if (ImGui.Selectable("Smooth", bfalloff == 1)) obj.TerrainBrushFalloff = 1;
                if (ImGui.Selectable("Sharp", bfalloff == 2)) obj.TerrainBrushFalloff = 2;
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Brush edge profile: Linear = even cone,\nSmooth = soft smoothstep (default), Sharp = weight concentrated at the center.");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Sculpt: drag raises, Ctrl lowers.\nPaint layer: paints the layer below, Ctrl erases.\nSmooth/Flatten: edit the terrain height.\nShift = fine, Ctrl+scroll = brush size.");
            int player = _bridge.PbrSplatPaintLayerIndex;
            if (ImGui.SliderInt("Paint layer##brush", ref player, 1, 4))
            {
                _bridge.PbrSplatPaintLayerIndex = player - 1; // bridge is 0-based
                var selectedObj = _bridge.SelectedEditorObject;
                if (selectedObj != null) selectedObj.SplatPaintLayerIndex = player - 1;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Which layer the Paint layer brush applies to.");
            float bsize = obj.TerrainBrushSize;
            if (ImGui.DragFloat("Brush size##brush", ref bsize, 0.05f, 0.1f, 64f)) obj.TerrainBrushSize = bsize;
            float bsoft = obj.TerrainBrushSoftness;
            if (ImGui.SliderFloat("Brush softness##brush", ref bsoft, 0.05f, 1f)) obj.TerrainBrushSoftness = bsoft;
            float bstr = obj.TerrainBrushStrength;
            if (ImGui.SliderFloat("Brush strength##brush", ref bstr, 0.02f, 2f)) obj.TerrainBrushStrength = bstr;

            // ── Dynamic terrain optimization ──
            ImGui.Spacing();
            ImGui.TextDisabled("Dynamic terrain (optimization):");
            bool lod = obj.PbrLodEnabled;
            if (ImGui.Checkbox("Per-chunk LOD (dynamic terrain)", ref lod)) obj.PbrLodEnabled = lod;
            if (lod)
            {
                float d1 = obj.PbrLodDistance;
                if (ImGui.DragFloat("LOD distance##lod", ref d1, 1f, 2f, 400f)) obj.PbrLodDistance = d1;
                float d2 = obj.PbrLodDistance2;
                if (ImGui.DragFloat("LOD distance 2##lod", ref d2, 1f, 4f, 800f)) obj.PbrLodDistance2 = d2;
            }
            bool occ = obj.PbrOcclusionEnabled;
            if (ImGui.Checkbox("Occlusion culling (GPU queries)", ref occ)) obj.PbrOcclusionEnabled = occ;
        }
    }
}
