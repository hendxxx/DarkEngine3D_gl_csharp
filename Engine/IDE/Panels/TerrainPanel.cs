using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using System;
using System.IO;
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
            if (sculptOn && string.IsNullOrEmpty(obj.TerrainHeightSourcePath))
                ImGui.TextDisabled("↳ assign a Terrain Heightmap first — the brush starts from it.");
            if (sculptOn && obj.SculptBrush is { } br)
            {
                ImGui.Indent();
                int mode = (int)br.Mode;
                if (ImGui.Combo("Brush##terrain_sculpt", ref mode, "Raise\0Lower\0Smooth\0Flatten\0"))
                    br.Mode = (TerrainBrushMode)mode;
                float rad = br.Radius;
                if (ImGui.SliderFloat("Radius (world)##terrain_sculpt", ref rad, 0.5f, 100f, "%.1f")) br.Radius = rad;
                float str = br.Strength;
                if (ImGui.SliderFloat("Strength##terrain_sculpt", ref str, 0.05f, 6f, "%.2f")) br.Strength = str;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Application speed (fraction of the height range per second at the brush core).");
                float hard = br.Hardness;
                if (ImGui.SliderFloat("Hardness##terrain_sculpt", ref hard, 0f, 1f, "%.2f")) br.Hardness = hard;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Falloff shape: 0 = fully soft (smooth dome), 1 = hard-edged disc.");
                if (obj.HasSculptEdits)
                {
                    ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.6f, 1f), "Sculpted — each stroke bakes into the heightmap file.");
                    if (obj.SculptUndoDepth > 0 || obj.SculptRedoDepth > 0)
                        ImGui.TextDisabled($"Ctrl+Z undo · Ctrl+Y redo — {obj.SculptUndoDepth} stroke(s) back, {obj.SculptRedoDepth} to redo");
                    if (ImGui.SmallButton("Revert sculpt##terrain_sculpt"))
                        obj.RevertSculpt();
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Discard ALL brush edits and reload the original heightmap image.");
                }
                ImGui.Unindent();
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
