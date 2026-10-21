using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using System;
using System.IO;
using System.Linq;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels
{
    /// <summary>
    /// Terrain panel — everything about shaping a PLANE into terrain, in ONE place:
    /// <list type="bullet">
    /// <item>Elevation heightmap input (the base SHAPE source, separate from the PBR
    /// height/POM detail map) with drag-drop from the Asset Browser, browse, clear.</item>
    /// <item>Terrain Geometry: world size (X/Z), Base Height (from the heightmap),
    /// Displace Height (vertex-displacement DETAIL from the PBR height map),
    /// Height Offset, Displace Strength, the elevation's own tiling, grid segments
    /// and chunk split — with live mesh/elevation status.</item>
    /// <item>Terrain Sculpt: viewport brush (Raise/Lower/Smooth/Flatten) that paints
    /// the elevation heightmap live; each finished stroke bakes the CPU heightfield
    /// to Artifacts/Terrain/&lt;scene&gt;/&lt;object&gt;.tga which becomes the stored
    /// elevation path, so sculpted terrain persists with the scene.</item>
    /// </list>
    /// Extracted from the PBR panel so terrain authoring has its own dedicated
    /// window (the PBR panel keeps materials: maps, POM tuning, mapping).
    /// </summary>
    public class TerrainPanel
    {
        private readonly IDEBridge _bridge;
        private bool _visible = true;

        private readonly ImGuiFileDialog _dialog = new();
        private bool _dialogTerrain; // the browse dialog targets the elevation slot

        public TerrainPanel(IDEBridge bridge) => _bridge = bridge;

        public void ShowInMenu() => ImGui.MenuItem("Terrain", null, ref _visible);

        public unsafe void Render()
        {
            if (!_visible) return;

            ImGui.Begin("Terrain", ref _visible);
            IDE.PanelFocus.Notify("Terrain");

            var obj = _bridge?.SelectedEditorObject;
            if (obj == null)
            {
                ImGui.TextDisabled("Select a Plane in the scene to shape it into terrain.");
                ImGui.End();
                _dialog.Render();
                return;
            }
            if (obj.PrimitiveType != EditorPrimitiveType.Plane)
            {
                ImGui.TextDisabled($"Terrain applies to PLANES — '{obj.Name}' is a {obj.PrimitiveType}.");
                ImGui.End();
                _dialog.Render();
                return;
            }

            ImGui.TextColored(new Vector4(0.6f, 0.85f, 1f, 1f), $"◉ {obj.Name}");
            ImGui.Separator();

            // Local drag-float helper (same feel as the PBR panel's Tune).
            void Tune(string label, ref float v, float speed, float min, float max)
            {
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat(label, ref v, speed, min, max);
            }

            // ── Elevation heightmap — the BASE SHAPE source (GPU unit 15, RAW). ──
            ImGui.TextColored(new Vector4(0.55f, 0.95f, 0.75f, 1f), "Terrain Heightmap");
            ImGui.Text("Heightmap:");
            ImGui.SameLine();
            string th = obj.TerrainHeightPath ?? "";
            ImGui.SetNextItemWidth(-70);
            if (ImGui.InputText("##terrain_hm_path", ref th, 512))
                obj.TerrainHeightPath = th.Trim();
            // Drag-drop target MUST sit directly after the InputText (ImGui rule:
            // BeginDragDropTarget applies to the LAST submitted item).
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                {
                    obj.TerrainHeightPath = AssetBrowserPanel._dragImagePath;
                    AssetBrowserPanel._dragImagePath = null;
                }
                ImGui.EndDragDropTarget();
            }
            ImGui.SameLine();
            if (ImGui.Button("⋯##terrain_hm_browse"))
            {
                _dialogTerrain = true;
                _dialog.OpenForLoad("*.*", "Select terrain heightmap image");
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Browse for a grayscale heightmap (white = peaks)");
            ImGui.SameLine();
            if (ImGui.Button("X##terrain_hm_clear") && !string.IsNullOrEmpty(obj.TerrainHeightPath))
                obj.TerrainHeightPath = "";
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear terrain elevation (plane becomes flat)");
            if (!string.IsNullOrEmpty(obj.TerrainHeightPath) && File.Exists(PathHelpers.Resolve(obj.TerrainHeightPath)))
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.5f, 1f), $"✓ {Path.GetFileName(PathHelpers.Resolve(obj.TerrainHeightPath))}");
            else if (!string.IsNullOrEmpty(obj.TerrainHeightPath))
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "⚠ file not found");
            else
                ImGui.TextDisabled("Drop image / type path / browse");

            // ── Terrain Geometry ──
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.55f, 0.95f, 0.75f, 1f), "Terrain Geometry");
            // World footprint (Scale X/Z) — the mesh is a unit grid.
            float sizeX = obj.Scale.X; Tune("Terrain Size X##vdisp", ref sizeX, 0.25f, 0.1f, 5000f); obj.Scale = obj.Scale with { X = MathF.Max(sizeX, 0.1f) };
            float sizeZ = obj.Scale.Z; Tune("Terrain Size Z##vdisp", ref sizeZ, 0.25f, 0.1f, 5000f); obj.Scale = obj.Scale with { Z = MathF.Max(sizeZ, 0.1f) };
            // TWO SEPARATE height sliders from TWO SEPARATE images:
            //   Base Height     = the TERRAIN heightmap (unit 15) → base shape.
            //   Displace Height = the PBR Height map (unit 5) → vertex
            //                     displacement DETAIL on top (own tiling +
            //                     POM calibration; needs the PBR slot filled).
            float vbh = obj.TerrainBaseHeight; Tune("Base Height (from heightmap)##vdisp", ref vbh, 0.01f, 0f, 500f); obj.TerrainBaseHeight = vbh;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("How tall the terrain heightmap image itself stands — the base shape (raw pixel value × this height).");
            float vds = obj.TerrainHeightScale; Tune("Displace Height (from PBR)##vdisp", ref vds, 0.01f, 0f, 500f); obj.TerrainHeightScale = vds;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Vertex-displacement DETAIL from the PBR Height map (slot 'Height / Displacement (POM Detail)' in the PBR panel):\ncalibrated + its own tiling. 0 = no PBR detail displacement.");
            if (string.IsNullOrEmpty(obj.PbrHeightPath))
                ImGui.TextDisabled("↳ set the 'Height / Displacement (POM Detail)' map in the PBR panel to use this");
            float vof = obj.TerrainHeightOffset; Tune("Height Offset (world)##vdisp", ref vof, 0.01f, -250f, 250f); obj.TerrainHeightOffset = vof;
            // Relief intensity: reshapes the raw elevation around mid-gray.
            float vst = obj.TerrainHeightStrength; Tune("Displace Strength##vdisp", ref vst, 0.01f, 0f, 3f); obj.TerrainHeightStrength = vst;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reshape the heightmap relief around mid-gray:\n0 = flat, 1 = map as-authored, >1 = steeper peaks & deeper valleys,\nup to 3. Does not change the Displace Height peak.");
            // Live composed result: base (terrain) + PBR detail + offset.
            float hi = Math.Clamp(obj.TerrainBaseHeight, 0f, 500f)
                     + (string.IsNullOrEmpty(obj.PbrHeightPath) ? 0f : Math.Clamp(obj.TerrainHeightScale, 0f, 500f))
                     + Math.Abs(obj.TerrainHeightOffset);
            ImGui.TextDisabled($"Max elevation: +{hi:F2} world (base + PBR detail + offset)");
            // The elevation map's OWN tiling — decoupled from PBR per-map tiling
            // and the global Map Tiling slider (uniform-only, no rebuild).
            float thx = obj.TerrainHeightTilingX; Tune("Terrain Tiling X##vdisp", ref thx, 0.05f, 0.01f, 100f); obj.TerrainHeightTilingX = thx;
            float thy = obj.TerrainHeightTilingY; Tune("Terrain Tiling Y##vdisp", ref thy, 0.05f, 0.01f, 100f); obj.TerrainHeightTilingY = thy;
            // Grid resolution + chunk split (MarkDirty on the property rebuilds the mesh).
            int seg = obj.PbrVertexSegments;
            if (ImGui.SliderInt("Segments##vdisp", ref seg, 16, 512)) obj.PbrVertexSegments = seg;
            int chunk = obj.PbrVertexChunk;
            if (ImGui.SliderInt("Chunks per side##vdisp", ref chunk, 1, 16)) obj.PbrVertexChunk = chunk;
            if (obj.PbrChunkCount > 1)
            {
                int per = obj.PbrPlaneSegmentsBuilt / obj.PbrVertexChunk;
                ImGui.TextDisabled($"Mesh: {obj.PbrChunkCount} chunks ({per}×{per} segs each, {obj.PbrPlaneSegmentsBuilt}×{obj.PbrPlaneSegmentsBuilt} total)");
                ImGui.TextDisabled($"Frustum cull: {obj.PbrChunksCulled}/{obj.PbrChunkCount} chunks skipped last draw");
            }
            else
            {
                ImGui.TextDisabled($"Mesh: {obj.PbrPlaneSegmentsBuilt}×{obj.PbrPlaneSegmentsBuilt} grid (1 draw)");
            }
            if (string.IsNullOrEmpty(obj.TerrainHeightSourcePath))
                ImGui.TextDisabled("Flat plane — no terrain heightmap yet.");
            else
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.6f, 1f), "Vertex displacement ACTIVE (terrain elevation drives geometry).");

            // ── TERRAIN SCULPT — viewport brush that PAINTS the elevation heightmap
            //    (raise/lower/smooth/flatten). The CPU heightfield uploads live (R8)
            //    and each finished stroke bakes to Artifacts/Terrain/<scene>/<object>.tga
            //    which becomes the stored TerrainHeightPath — sculpted terrain
            //    persists with the scene.
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.55f, 0.95f, 0.75f, 1f), "Terrain Sculpt (Viewport Brush)");
            bool sculptOn = _bridge.TerrainSculptObject == obj;
            if (ImGui.Checkbox("Enable brush##terrain_sculpt", ref sculptOn))
            {
                if (sculptOn)
                {
                    // Sessions are mutually exclusive — turn the splat brush off first.
                    if (_bridge.TerrainSplatObject != null)
                    {
                        _bridge.TerrainSplatObject.SplatBrush = null;
                        _bridge.TerrainSplatObject = null;
                    }
                    obj.BeginSculptSession();
                    _bridge.TerrainSculptObject = obj;
                }
                else
                {
                    _bridge.TerrainSculptObject = null;
                    obj.SculptBrush = null;
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Paint the terrain directly in the viewport:\nLMB drag = apply the brush, the ring shows the TRUE world radius.\nWhile ON, left-click paints instead of selecting objects.");
            if (sculptOn)
                ImGui.TextDisabled("↳ brush edits are ADDITIVE — they ride on top of the base heightmap\n(the base image file is never modified). 'Sculpt Add Height' scales the effect.");
            if (sculptOn && obj.SculptBrush is { } br)
            {
                ImGui.Indent();
                int mode = (int)br.Mode;
                if (ImGui.Combo("Brush##terrain_sculpt", ref mode, "Raise\0Lower\0Smooth\0Flatten\0Noise\0Terrace\0"))
                    br.Mode = (TerrainBrushMode)mode;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Raise/Lower push heights, Smooth relaxes, Flatten levels the footprint.\nNoise paints fractal detail between the footprint's elevation range\n(one coherent pattern per stroke), Terrace quantizes into stepped plateaus.\nSHIFT-TAP in the viewport cycles the mode.");
                float rad = br.Radius;
                if (ImGui.SliderFloat("Radius (world)##terrain_sculpt", ref rad, 0.5f, 100f, "%.1f")) br.Radius = rad;
                float str = br.Strength;
                if (ImGui.SliderFloat("Strength##terrain_sculpt", ref str, 0.05f, 6f, "%.2f")) br.Strength = str;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Application speed (fraction of the height range per second at the brush core).\nNoise/Terrace: convergence speed toward the noise field / stepped grid.");
                float hard = br.Hardness;
                if (ImGui.SliderFloat("Hardness##terrain_sculpt", ref hard, 0f, 1f, "%.2f")) br.Hardness = hard;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Falloff shape: 0 = fully soft (smooth dome), 1 = hard-edged disc.");
                if (br.Mode == TerrainBrushMode.Terrace)
                {
                    int steps = br.Steps;
                    if (ImGui.SliderInt("Terrace Steps##terrain_sculpt", ref steps, 2, 64))
                        br.Steps = steps;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Number of discrete height levels (2..64) over the full height range.\n8 = coarse mesas, 32 = fine shelfing. All texels share one grid,\nso terraces continue across stamps.");
                }
                // ADDITIVE sculpt amplitude — how far brush edits push the surface
                // up/down away from the base heightmap (bidirectional, ±).
                float samp = obj.TerrainSculptAmp;
                if (ImGui.SliderFloat("Sculpt Add Height##terrain_sculpt", ref samp, 0f, 100f, "%.1f"))
                    obj.TerrainSculptAmp = samp;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Amplitude of the ADDITIVE sculpt layer (world units):\nbrush edits displace ±this far from the base heightmap.\n0 = sculpt hidden; the base image itself is never modified.");
                if (obj.HasSculptEdits)
                {
                    ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.6f, 1f), "Sculpted — each stroke bakes into the sculpt layer (base image untouched).");
                    if (obj.SculptUndoDepth > 0 || obj.SculptRedoDepth > 0)
                        ImGui.TextDisabled($"Ctrl+Z undo · Ctrl+Y redo — {obj.SculptUndoDepth} stroke(s) back, {obj.SculptRedoDepth} to redo");
                    if (ImGui.SmallButton("Revert sculpt##terrain_sculpt"))
                        obj.RevertSculpt();
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Discard ALL brush edits: the sculpt bake is detached and the terrain\nreturns to its base heightmap (the file stays on disk, just unreferenced).\nThe base heightmap image is never modified by sculpting.");
                }
                ImGui.Unindent();
            }

            // ── TERRAIN PAINT — splat-map painting of texture layers. Layer 0 IS the
            //    base PBR material; layers 1-3 carry their own albedo/PBR textures.
            //    Weights come from the viewport brush AND auto height bands over the
            //    sculpted elevation (valleys → layer 0 … peaks → top layer); the brush
            //    always wins where it painted. Each stroke bakes the weight map to
            //    Artifacts/Terrain/<scene>/<object>_splat.tga (R/G/B/A = layers 0-3)
            //    which persists with the scene.
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.55f, 0.95f, 0.75f, 1f), "Terrain Paint (Texture Layers)");
            bool paintOn = _bridge.TerrainSplatObject == obj;
            if (ImGui.Checkbox("Enable paint##terrain_splat", ref paintOn))
            {
                if (paintOn)
                {
                    if (_bridge.TerrainSculptObject != null)
                    {
                        // Sessions are mutually exclusive — turn the sculpt brush off first.
                        _bridge.TerrainSculptObject.SculptBrush = null;
                        _bridge.TerrainSculptObject = null;
                    }
                    obj.BeginSplatSession();
                    _bridge.TerrainSplatObject = obj;
                }
                else
                {
                    _bridge.TerrainSplatObject = null;
                    obj.SplatBrush = null;
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Paint texture layers on the terrain in the viewport:\nLMB drag = paint the selected layer (weights sum to 1).\nWhile ON, left-click paints instead of selecting objects.");
            if (paintOn && string.IsNullOrEmpty(obj.TerrainHeightSourcePath) && obj.SplatLayerAlbedoPath.Skip(1).All(string.IsNullOrEmpty))
                ImGui.TextDisabled("↳ assign a Terrain Heightmap or a layer texture first.");
            if (paintOn && obj.SplatBrush is { } sp)
            {
                ImGui.Indent();
                int smode = (int)sp.Mode;
                if (ImGui.Combo("Brush##terrain_splat", ref smode, "Paint\0Erase\0Smooth\0"))
                    sp.Mode = (SplatBrushMode)smode;
                int layer = Math.Clamp(sp.Layer, 0, 3);
                if (ImGui.SliderInt("Layer##terrain_splat", ref layer, 0, 3))
                    sp.Layer = layer;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("0 = the base PBR material (PBR panel maps), 1-3 = the layers below.");
                float srad = sp.Radius;
                if (ImGui.SliderFloat("Radius (world)##terrain_splat", ref srad, 0.5f, 100f, "%.1f")) sp.Radius = srad;
                float sstr = sp.Strength;
                if (ImGui.SliderFloat("Strength##terrain_splat", ref sstr, 0.05f, 6f, "%.2f")) sp.Strength = sstr;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Application speed (fraction of full weight per second at the brush core).");
                float shard = sp.Hardness;
                if (ImGui.SliderFloat("Hardness##terrain_splat", ref shard, 0f, 1f, "%.2f")) sp.Hardness = shard;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Falloff shape: 0 = fully soft, 1 = hard-edged disc.");
                ImGui.Unindent();
            }

            // ── ALWAYS-ON layering controls ──
            // The auto layers + layer textures are DATA, not a brush: they used to
            // live inside the paint-session block, so the slope layer (and the
            // textures it blends) could only be configured while the viewport
            // brush was enabled — reading as "slope texturing doesn't work".
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.55f, 0.95f, 0.75f, 1f), "Auto Layers (no painting needed)");
            {
                // ── AUTO height bands (weights from the sculpted elevation) ──
                bool bandsOn = obj.SplatHeightBandsEnabled;
                if (ImGui.Checkbox("Auto layers from height##terrain_splat", ref bandsOn))
                {
                    obj.SplatHeightBandsEnabled = bandsOn;
                    obj.InvalidateSplatBands();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Assign texture layers by ELEVATION automatically:\nvalleys take layer 0 … peaks take the top layer. Manual paint always wins.");
                if (bandsOn)
                {
                    ImGui.Indent();
                    int bandCount = obj.SplatHeightLayerCount;
                    if (ImGui.SliderInt("Bands##terrain_splat", ref bandCount, 1, 4))
                    {
                        obj.SplatHeightLayerCount = bandCount;
                        obj.InvalidateSplatBands();
                    }
                    float feather = obj.SplatHeightLayerFeather;
                    if (ImGui.SliderFloat("Band feather (world)##terrain_splat", ref feather, 0.05f, 20f, "%.2f"))
                    {
                        obj.SplatHeightLayerFeather = feather;
                        obj.InvalidateSplatBands();
                    }
                    for (int l = 0; l < 4; l++)
                    {
                        ImGui.PushID($"splatband{l}");
                        var bandLo = obj.SplatHeightBands[l].X;
                        var bandHi = obj.SplatHeightBands[l].Y;
                        bool inf = bandHi >= 1e5f;
                        ImGui.TextDisabled($"L{l}");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(100);
                        if (ImGui.DragFloat("Low##terrain_splat", ref bandLo, 0.05f, 0f, 500f))
                        {
                            obj.SplatHeightBands[l] = new Vector2(bandLo, obj.SplatHeightBands[l].Y);
                            obj.InvalidateSplatBands();
                        }
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(100);
                        if (!inf && ImGui.DragFloat("High##terrain_splat", ref bandHi, 0.05f, 0f, 500f))
                        {
                            obj.SplatHeightBands[l] = new Vector2(obj.SplatHeightBands[l].X, bandHi);
                            obj.InvalidateSplatBands();
                        }
                        else if (inf)
                        {
                            ImGui.SameLine();
                            ImGui.TextDisabled("+∞");
                        }
                        ImGui.PopID();
                    }
                    ImGui.Unindent();
                }

                // ── SLOPE BASED TEXTURING (Rastertek terrain tutorial 14) — 3 TIERS ──
                // Each enabled tier owns an angle range and blends ONE splat layer in
                // per pixel, so grass (base) → dirt (medium slopes) → rock (cliffs)
                // works without rebaking the weight map. The shader applies the tiers
                // sorted by START angle, so the STEEPEST tier wins where ranges overlap.
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.6f, 0.9f, 1f, 1f), "Slope Based Texturing (3 tiers)");
                bool deg = obj.SplatSlopeUseDegrees;
                if (ImGui.Checkbox("Angle mode (degrees)##terrain_slope", ref deg))
                    obj.SplatSlopeUseDegrees = deg;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("ON = each tier's range is a surface ANGLE in degrees\n(0° = flat, 90° = vertical wall).\nOFF = the legacy abstract 1 − Normal.y units for tier 1 (old scenes).\nTiers 2-3 are always degrees.\n\nWith tiers 2-3 OFF the first tier behaves exactly like before —\nold scenes load unchanged.");
                var slopeTiers = new (bool On, int Layer, float Min, float Max, float Tiling)[]
                {
                    (obj.SplatSlopeEnabled, obj.SplatSlopeLayer, obj.SplatSlopeAngleMin, obj.SplatSlopeAngleMax, obj.SplatSlopeTiling),
                    (obj.SplatSlope2Enabled, obj.SplatSlope2Layer, obj.SplatSlope2AngleMin, obj.SplatSlope2AngleMax, obj.SplatSlope2Tiling),
                    (obj.SplatSlope3Enabled, obj.SplatSlope3Layer, obj.SplatSlope3AngleMin, obj.SplatSlope3AngleMax, obj.SplatSlope3Tiling),
                };
                string[] tierNames =
                [
                    "Tier 1 - gentle slopes##terrain_slope",
                    "Tier 2 - steeper band##terrain_slope",
                    "Tier 3 - cliffs##terrain_slope",
                ];
                for (int t = 0; t < 3; t++)
                {
                    ImGui.PushID($"slopetier{t}");
                    var tier = slopeTiers[t];
                    ImGui.Checkbox(tierNames[t], ref tier.On);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(t == 0
                            ? "Blends ONE splat layer onto ground steeper than the range below.\nThe rastertek terrain tutorial 14 layer (slope = 1 − Normal.y).\nPainted/baked weights stay the base — nothing is rebaked."
                            : "An EXTRA steepness band. Tiers apply from gentle to steep,\nso this one takes over where the gentler tiers fade out.\nClassic setup: base grass -> tier 1 dirt -> tier 2 rock.");
                    if (tier.On)
                    {
                        ImGui.Indent();
                        ImGui.SliderInt("Layer##terrain_slope", ref tier.Layer, 1, 3);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Which splat layer (1-3) this tier uses.\nAssign its albedo/normal/roughness/… in 'Layer textures' below.");

                        if (t == 0 && !deg)
                        {
                            // LEGACY raw units — old scenes never stored an angle range.
                            float slopeTh = obj.SplatSlopeThreshold;
                            if (ImGui.SliderFloat("Threshold##terrain_slope", ref slopeTh, 0f, 0.95f, "%.2f"))
                                obj.SplatSlopeThreshold = slopeTh;
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("LEGACY abstract units: 0 = any non-flat surface,\n0.7 = near-vertical cliffs only (1 − Normal.y).\nSwitch Angle mode on for readable degrees.");
                            float slopeFade = obj.SplatSlopeFade;
                            if (ImGui.SliderFloat("Blend##terrain_slope", ref slopeFade, 0.005f, 0.6f, "%.3f"))
                                obj.SplatSlopeFade = slopeFade;
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Blend width above the threshold —\n0.02 = hard cliff edge, 0.4 = soft gradual transition.");
                            float aLo = MathF.Acos(1f - Math.Clamp(slopeTh, 0f, 0.9999f)) * 180f / MathF.PI;
                            float aHi = MathF.Acos(1f - Math.Clamp(slopeTh + slopeFade, 0f, 0.9999f)) * 180f / MathF.PI;
                            ImGui.TextDisabled($"≈ {aLo:F0}° .. {aHi:F0}° surface angle (1 − Normal.y units)");
                        }
                        else
                        {
                            ImGui.SliderFloat("Start##terrain_slope", ref tier.Min, 0f, 89f, "%.0f°");
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("This tier's alpha is 0 at/below this angle.\n45° = walkable slope, 70° = cliff face.");
                            ImGui.SliderFloat("Full##terrain_slope", ref tier.Max, 1f, 90f, "%.0f°");
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Alpha = 1 at/above this angle (the tier fully replaces the\nground below it). Kept ≥ Start + 1° automatically.");
                            if (tier.Max <= tier.Min) tier.Max = MathF.Min(tier.Min + 1f, 90f);
                        }

                        ImGui.SliderFloat("Tiling##terrain_slope", ref tier.Tiling, 0.25f, 16f, "%.2f", ImGuiSliderFlags.Logarithmic);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Tiling of THIS tier's layer textures across the terrain.\nSlopes stretch — a rock texture usually wants 2-4× the ground's,\nindependent of the painted layer tiling.");
                        if (string.IsNullOrEmpty(obj.GetSplatLayerPath(0, tier.Layer) ?? ""))
                            ImGui.TextDisabled($"-> Layer {tier.Layer} has no albedo yet — only its tint shows.");
                        ImGui.Unindent();
                    }
                    slopeTiers[t] = tier;
                    ImGui.PopID();
                }
                // Push the edited tiers back (setters clamp; AngleMax auto-lifts past Min).
                obj.SplatSlopeEnabled = slopeTiers[0].On;
                obj.SplatSlopeLayer = slopeTiers[0].Layer;
                obj.SplatSlopeAngleMin = slopeTiers[0].Min;
                obj.SplatSlopeAngleMax = slopeTiers[0].Max;
                obj.SplatSlopeTiling = slopeTiers[0].Tiling;
                obj.SplatSlope2Enabled = slopeTiers[1].On;
                obj.SplatSlope2Layer = slopeTiers[1].Layer;
                obj.SplatSlope2AngleMin = slopeTiers[1].Min;
                obj.SplatSlope2AngleMax = slopeTiers[1].Max;
                obj.SplatSlope2Tiling = slopeTiers[1].Tiling;
                obj.SplatSlope3Enabled = slopeTiers[2].On;
                obj.SplatSlope3Layer = slopeTiers[2].Layer;
                obj.SplatSlope3AngleMin = slopeTiers[2].Min;
                obj.SplatSlope3AngleMax = slopeTiers[2].Max;
                obj.SplatSlope3Tiling = slopeTiers[2].Tiling;

                if (obj.SplatSlopeAnyEnabled)
                {
                    // PAINT INTERPLAY — does a slope tier displace manual paint?
                    bool ovPaint = obj.SplatSlopeOverridePaint;
                    if (ImGui.Checkbox("Override painted areas##terrain_slope", ref ovPaint))
                        obj.SplatSlopeOverridePaint = ovPaint;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("OFF = BLEND (the reference lerp): each tier fades in as the surface\nsteepens — at full alpha it covers painted weights there.\nON = OVERRIDE: the tier takes max(paint, floor × alpha) on steep texels,\nso a painted layer always keeps at least the floor share.");
                    if (ovPaint)
                    {
                        float floor = obj.SplatSlopePaintFloor;
                        if (ImGui.SliderFloat("Slope floor##terrain_slope", ref floor, 0f, 0.9f, "%.2f"))
                            obj.SplatSlopePaintFloor = floor;
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Minimum weight a slope tier reaches on any texel its mask\ncovers — 0.25 keeps a hint of the painted layer visible.");
                    }
                }
                bool slopeDbg = EditorObject.ShowSlopeDebug;
                if (ImGui.Checkbox("Slope preview (tier coverage)##terrain_slope", ref slopeDbg))
                    EditorObject.ShowSlopeDebug = slopeDbg;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Session-only tuning view (not saved):\ngray = flat ground (no tier), orange = tier 1, blue = tier 2,\nmagenta = tier 3. Line the angle ranges up with the terrain shape.");
                // ── Layer textures (albedo + full PBR map set per layer) ──
                if (ImGui.TreeNode("Layer textures##terrain_splat"))
                {
                    string[] mapNames = ["Albedo", "Normal", "Metallic", "Roughness", "AO", "Height"];
                    string[] mapTags = ["alb", "nrm", "met", "rgh", "ao", "hgt"];
                    for (int l = 1; l <= 3; l++)
                    {
                        ImGui.PushID($"splatlayer{l}");
                        var col = obj.SplatLayerTint[l];
                        if (ImGui.ColorEdit3("Tint##terrain_splat", ref col,
                                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                            obj.SplatLayerTint[l] = col;
                        ImGui.SameLine();
                        ImGui.Text($"Layer {l}");
                        // Per-layer texture density — independent of the global Map
                        // Tiling (the paint mask always stays 1:1 with the terrain).
                        float lt = obj.SplatLayerTiling[l];
                        if (ImGui.SliderFloat("Tiling##terrain_splat", ref lt, 0.25f, 16f, "%.2f", ImGuiSliderFlags.Logarithmic))
                            obj.SplatLayerTiling[l] = lt;
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("This layer's texture tiling across the whole terrain.\nIndependent from the PBR panel's Map Tiling —\nthe paint mask never tiles.");
                        for (int m = 0; m < 6; m++)
                        {
                            string path = obj.GetSplatLayerPath(m, l) ?? "";
                            ImGui.Text($"{mapNames[m]}:");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-70);
                            if (ImGui.InputText($"##{mapTags[m]}{l}", ref path, 512))
                                obj.SetSplatLayerPath(m, l, path.Trim());
                            if (ImGui.BeginDragDropTarget())
                            {
                                var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                                if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                                {
                                    obj.SetSplatLayerPath(m, l, AssetBrowserPanel._dragImagePath);
                                    AssetBrowserPanel._dragImagePath = null;
                                }
                                ImGui.EndDragDropTarget();
                            }
                            ImGui.SameLine();
                            if (ImGui.Button($"X##clear{mapTags[m]}{l}") && !string.IsNullOrEmpty(obj.GetSplatLayerPath(m, l)))
                                obj.SetSplatLayerPath(m, l, "");
                        }
                        ImGui.PopID();
                        ImGui.Separator();
                    }
                    ImGui.TextDisabled("Layer 0 = the base PBR material (PBR panel).\nEmpty slots keep neutral defaults (no texture, tint shows).\nAlbedo slot empty → the tint colors the band.");
                    ImGui.TreePop();
                }
                float cov = obj.SplatPaintCoverage() * 100f;
                if (cov > 0.01f)
                    ImGui.TextDisabled($"Painted coverage: {cov:F1}% of the terrain");
                if (obj.HasSplatEdits)
                    ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.6f, 1f), "Painted — each stroke bakes into the splat file.");
                if (obj.SplatUndoDepth > 0 || obj.SplatRedoDepth > 0)
                    ImGui.TextDisabled($"Ctrl+Z undo · Ctrl+Y redo — {obj.SplatUndoDepth} stroke(s) back, {obj.SplatRedoDepth} to redo");
                if (ImGui.SmallButton("Revert paint##terrain_splat"))
                    obj.RevertSplat();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Discard ALL paint edits: the baked weight map is detached and every layer\nreturns to layer 0 (the base PBR material).\nLayer textures, height bands and slope tiers are kept.");
                ImGui.Separator();
            }

            ImGui.End();
            _dialog.Render();

            // Apply a confirmed file-dialog selection to the elevation slot.
            bool dialogTerrain = _dialogTerrain;
            _dialogTerrain = false; // always consume — a stale flag must never leak
            if (dialogTerrain && _dialog.IsConfirmed && !string.IsNullOrEmpty(_dialog.SelectedPath))
                obj.TerrainHeightPath = _dialog.SelectedPath;
        }
    }
}
