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
    /// PBR Material panel — edit the selected editor object's PBR material:
    /// <list type="bullet">
    /// <item>7 optional map slots (Albedo / Normal / Metallic / Roughness / AO / Height / Emission)
    /// with drag-drop from the Asset Browser, a browse file dialog, clear, and an
    /// auto-detect that finds sibling maps next to the albedo by filename convention.</item>
    /// <item>Per-map-type tuning (albedo brightness/saturation/contrast, normal strength/blur,
    /// metallic threshold/softness/strength, roughness strength/invert, AO strength/brightness,
    /// height strength/invert/blur, emission intensity) + UV tiling — all live uniforms.</item>
    /// </list>        /// Applies to Box / Sphere / flat Plane — the shared objectPbr shader renders
        /// all three from the same 7 optional maps.
    /// layer has its own 7 map slots and its own tuning, because PBR is per texture; GLB
    /// references use the maps embedded in the .glb. Missing maps keep neutral defaults, so
    /// loading just an albedo already gives a full PBR material.
    /// </summary>
    public class PbrPanel
    {
        private readonly IDEBridge _bridge;
        private bool _visible = true;

        private readonly ImGuiFileDialog _dialog = new();
        private string _dialogSlot = ""; // which map slot the browse dialog targets

        private static readonly string[] SlotKeys = ["albedo", "normal", "metallic", "roughness", "ao", "height", "emission"];
        private static readonly string[] SlotNames =
        [
            "Albedo / Base Color", "Normal Map", "Metallic Map", "Roughness Map",
            "Ambient Occlusion (AO)", "Height / Displacement (POM Detail)", "Emission",
        ];
        private static readonly string[] ImageExts = [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".webp"];

        public PbrPanel(IDEBridge bridge) => _bridge = bridge;

        public void ShowInMenu() => ImGui.MenuItem("PBR Material", null, ref _visible);

        public void Render()
        {
            if (!_visible) return;

            ImGui.Begin("PBR Material", ref _visible);
            IDE.PanelFocus.Notify("PBR Material");

            var obj = _bridge?.SelectedEditorObject;
            if (obj == null)
            {
                ImGui.TextDisabled("Select a Box / Sphere / Plane in the scene to edit its PBR material.");
                ImGui.End();
                _dialog.Render();
                return;
            }

            // Box/Sphere = closed PBR primitives; Plane = flat PBR ground (two-sided).
            bool isPrimitive = obj.PrimitiveType is EditorPrimitiveType.Box
                or EditorPrimitiveType.Sphere or EditorPrimitiveType.Plane;

            ImGui.TextColored(new Vector4(0.6f, 0.85f, 1f, 1f), $"◉ {obj.Name}");
            ImGui.TextDisabled(obj.PrimitiveType.ToString());
            ImGui.Separator();

            if (isPrimitive)
            {
                RenderMapSlots(obj);
                ImGui.Separator();
                if (ImGui.CollapsingHeader("PBR Tuning", ImGuiTreeNodeFlags.DefaultOpen))
                    RenderTuning(obj);
            }
            else if (obj.PrimitiveType == EditorPrimitiveType.GlbReference)
            {
                ImGui.TextDisabled("GLB models use the PBR maps embedded in the .glb file.");
                ImGui.Separator();
            }
            else if (obj.PrimitiveType == EditorPrimitiveType.Plane)
            {
                ImGui.TextDisabled("This object type has no material (marker / gizmo).");
                ImGui.Separator();
            }
            else
            {
                ImGui.TextDisabled("This object type has no material (marker / gizmo).");
                ImGui.Separator();
            }

            ImGui.End();
            _dialog.Render();
            ApplyDialogResult(obj);
        }

        // ── Map slots ────────────────────────────────────────────────────────────────
        // unsafe: ImGui.AcceptDragDropPayload returns a pointer (same as the Inspector).
        private unsafe void RenderMapSlots(EditorObject obj)
        {
            if (!ImGui.CollapsingHeader("Maps (7)", ImGuiTreeNodeFlags.DefaultOpen)) return;

            string[] paths =
            [
                obj.PbrAlbedoPath, obj.PbrNormalPath, obj.PbrMetallicPath, obj.PbrRoughnessPath,
                obj.PbrAoPath, obj.PbrHeightPath, obj.PbrEmissionPath,
            ];

            // ── Auto-detect siblings next to the albedo (by filename tokens) ──
            if (!string.IsNullOrEmpty(obj.PbrAlbedoPath))
            {
                if (ImGui.Button("🔍 Auto-detect maps from Albedo"))
                {
                    var found = PbrMapDiscovery.FindMaps(obj.PbrAlbedoPath);
                    if (found != null)
                    {
                        obj.PbrAlbedoPath = found[0];
                        obj.PbrNormalPath = found[1];
                        obj.PbrMetallicPath = found[2];
                        obj.PbrRoughnessPath = found[3];
                        obj.PbrAoPath = found[4];
                        obj.PbrHeightPath = found[5];
                        obj.PbrEmissionPath = found[6];
                        obj.InvalidatePbrTextures();                            Console.WriteLine("[PBR] Auto-detect on '" + obj.Name + "': " +
                                string.Join(" | ", found.Select((p, i) =>
                                    $"{SlotKeys[i]}={(!string.IsNullOrEmpty(p) ? Path.GetFileName(p) : "-")}")));
                    }
                    else
                    {
                        Console.WriteLine($"[PBR] Auto-detect: albedo not found ({obj.PbrAlbedoPath})");
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear all maps##pbr"))
                {
                    obj.PbrAlbedoPath = obj.PbrNormalPath = obj.PbrMetallicPath = obj.PbrRoughnessPath = "";
                    obj.PbrAoPath = obj.PbrHeightPath = obj.PbrEmissionPath = "";
                    obj.InvalidatePbrTextures();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove every PBR map — the object falls back to its vertex color.");
                ImGui.Spacing();
            }

            for (int i = 0; i < 7; i++)
            {
                string slot = SlotKeys[i];
                string id = $"pbr_{slot}";
                string path = paths[i];
                bool has = !string.IsNullOrEmpty(path) && File.Exists(PathHelpers.Resolve(path));

                ImGui.Text(SlotNames[i] + ":");
                ImGui.SameLine();

                string tmp = path ?? "";
                ImGui.SetNextItemWidth(-70);
                if (ImGui.InputText($"##{id}_path", ref tmp, 512))
                {
                    SetMap(obj, i, tmp.Trim());
                }
                // ── Drag-drop: full-width target on the InputText ──
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    {
                        SetMap(obj, i, AssetBrowserPanel._dragImagePath);
                        AssetBrowserPanel._dragImagePath = null;
                    }
                    ImGui.EndDragDropTarget();
                }

                ImGui.SameLine();
                if (ImGui.Button($"⋯##{id}_browse"))
                {
                    _dialogSlot = slot;
                    _dialog.OpenForLoad("*.*", $"Select {SlotNames[i]} image");
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Browse for an image file");

                ImGui.SameLine();
                if (ImGui.Button($"X##{id}_clear") && has)
                {
                    SetMap(obj, i, "");
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear map");

                if (has)
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.5f, 1f), $"✓ {Path.GetFileName(PathHelpers.Resolve(path))}");
                else
                    ImGui.TextDisabled("Drop image / type path / browse");
            }
        }

        // ── Tuning ───────────────────────────────────────────────────────────────────
        private unsafe void RenderTuning(EditorObject obj)
        {
            void Tune(string label, ref float v, float speed, float min, float max)
            {
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat(label, ref v, speed, min, max);
            }

            // Every widget carries a unique ##id (labels like "Strength" / "Brightness" /
            // "Blur (texels)" repeat across map types — without the suffix ImGui would
            // treat them as the SAME widget and conflict).
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.6f, 1f), "Albedo / Base Color");
            float b = obj.PbrAlbedoBrightness; Tune("Brightness##albedo", ref b, 0.01f, 0f, 2f); obj.PbrAlbedoBrightness = b;
            float sat = obj.PbrAlbedoSaturation; Tune("Saturation##albedo", ref sat, 0.01f, 0f, 2f); obj.PbrAlbedoSaturation = sat;
            float ct = obj.PbrAlbedoContrast; Tune("Contrast##albedo", ref ct, 0.01f, 0f, 2f); obj.PbrAlbedoContrast = ct;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Normal Map");
            float ns = obj.PbrNormalStrength; Tune("Strength##normal", ref ns, 0.01f, 0f, 2f); obj.PbrNormalStrength = ns;
            float nb = obj.PbrNormalBlur; Tune("Blur (texels)##normal", ref nb, 0.05f, 0f, 8f); obj.PbrNormalBlur = nb;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.9f, 0.8f, 0.7f, 1f), "Metallic");
            float mt = obj.PbrMetallicThreshold; Tune("Threshold##metallic", ref mt, 0.01f, 0f, 1f); obj.PbrMetallicThreshold = mt;
            float ms = obj.PbrMetallicSoftness; Tune("Softness##metallic", ref ms, 0.01f, 0f, 0.5f); obj.PbrMetallicSoftness = ms;
            float mst = obj.PbrMetallicStrength; Tune("Strength##metallic", ref mst, 0.01f, 0f, 1f); obj.PbrMetallicStrength = mst;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.8f, 0.9f, 0.7f, 1f), "Roughness");
            float rs = obj.PbrRoughnessStrength; Tune("Strength##roughness", ref rs, 0.01f, 0f, 2f); obj.PbrRoughnessStrength = rs;
            bool ri = obj.PbrRoughnessInvert;
            if (ImGui.Checkbox("Invert (smoothness map)##roughness", ref ri)) obj.PbrRoughnessInvert = ri;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.8f, 0.9f, 1f), "Ambient Occlusion");
            float aos = obj.PbrAoStrength; Tune("Strength##ao", ref aos, 0.01f, 0f, 2f); obj.PbrAoStrength = aos;
            float aob = obj.PbrAoBrightness; Tune("Brightness##ao", ref aob, 0.01f, 0f, 1f); obj.PbrAoBrightness = aob;

            ImGui.Spacing();
            if (obj.PrimitiveType == EditorPrimitiveType.Plane)
            {
                // ── DISPLACED PLANE GRID: the Height / Displacement map drives the
                //    real geometry via the vertex-displacement stage. ──
                ImGui.TextColored(new Vector4(0.55f, 0.95f, 0.75f, 1f), "Terrain Geometry");
                // ── Terrain ELEVATION heightmap — the BASE SHAPE (vertex displacement).
                //    SEPARATE from the PBR Height map (that one stays POM surface detail).
                ImGui.Text("Terrain Heightmap:");
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
                    _dialogSlot = "terrain";
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

                // Terrain size — the plane's world footprint (Scale X/Z). The mesh is a
                // unit grid, so world size comes from the object transform; editing it
                // here keeps terrain authoring in one place.
                float sizeX = obj.Scale.X; Tune("Terrain Size X##vdisp", ref sizeX, 0.25f, 0.1f, 5000f); obj.Scale = obj.Scale with { X = MathF.Max(sizeX, 0.1f) };
                float sizeZ = obj.Scale.Z; Tune("Terrain Size Z##vdisp", ref sizeZ, 0.25f, 0.1f, 5000f); obj.Scale = obj.Scale with { Z = MathF.Max(sizeZ, 0.1f) };
                // Height scale: how tall the highest height-map value stands (world units).
                // Height offset: lifts the WHOLE surface (negative sinks the base) —
                // raise islands above water level or sink valleys below the grid.
                // TWO SEPARATE height sliders from TWO SEPARATE images:
                //   Base Height     = the TERRAIN heightmap (unit 15) → base shape.
                //   Displace Height = the PBR Height map (unit 5) → vertex
                //                     displacement DETAIL on top (own tiling +
                //                     POM calibration; needs the PBR slot filled).
                float vbh = obj.TerrainBaseHeight; Tune("Base Height (from heightmap)##vdisp", ref vbh, 0.01f, 0f, 500f); obj.TerrainBaseHeight = vbh;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("How tall the terrain heightmap image itself stands — the base shape (raw pixel value × this height).");
                float vds = obj.TerrainHeightScale; Tune("Displace Height (from PBR)##vdisp", ref vds, 0.01f, 0f, 500f); obj.TerrainHeightScale = vds;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Vertex-displacement DETAIL from the PBR Height map (slot 'Height / Displacement (POM Detail)'):\ncalibrated + its own tiling. 0 = no PBR detail displacement.");
                if (string.IsNullOrEmpty(obj.PbrHeightPath))
                    ImGui.TextDisabled("↳ set the 'Height / Displacement (POM Detail)' map to use this");
                float vof = obj.TerrainHeightOffset; Tune("Height Offset (world)##vdisp", ref vof, 0.01f, -250f, 250f); obj.TerrainHeightOffset = vof;
                // Relief intensity: reshapes the raw elevation around mid-gray —
                // 0 = flat, 1 = map as-authored, up to 3 = steep peaks/deep valleys.
                // Peak height stays Displace Height; this reshapes the distribution.
                float vst = obj.TerrainHeightStrength; Tune("Displace Strength##vdisp", ref vst, 0.01f, 0f, 3f); obj.TerrainHeightStrength = vst;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reshape the heightmap relief around mid-gray:\n0 = flat, 1 = map as-authored, >1 = steeper peaks & deeper valleys,\nup to 3. Does not change the Displace Height peak.");
                // Live composed result: base (terrain) + PBR detail + offset.
                float hi = Math.Clamp(obj.TerrainBaseHeight, 0f, 500f)
                         + (string.IsNullOrEmpty(obj.PbrHeightPath) ? 0f : Math.Clamp(obj.TerrainHeightScale, 0f, 500f))
                         + Math.Abs(obj.TerrainHeightOffset);
                ImGui.TextDisabled($"Max elevation: +{hi:F2} world (base + PBR detail + offset)");
                // The elevation map's OWN tiling — decoupled from the PBR per-map
                // tiling and the global Map Tiling slider (how many times the
                // heightmap wraps across the terrain; uniform-only, no rebuild).
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
            }
            else
            {
                // Box/Sphere keep the POM height-detail tuning (parallax only — no
                // vertex displacement on closed primitives).
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.9f, 1f), "Height / Displacement (POM detail)");
                float hs = obj.PbrHeightStrength; Tune("Strength##height", ref hs, 0.01f, 0f, 2f); obj.PbrHeightStrength = hs;
                bool hi = obj.PbrHeightInvert;
                if (ImGui.Checkbox("Invert (valleys/peaks)##height", ref hi)) obj.PbrHeightInvert = hi;
                float hb = obj.PbrHeightBlur; Tune("Blur (texels)##height", ref hb, 0.05f, 0f, 8f); obj.PbrHeightBlur = hb;
                ImGui.Separator();
                ImGui.TextDisabled("Calibration (Marmoset-style)");
                float hsc = obj.PbrHeightScaleCenter; Tune("Scale Center##height", ref hsc, 0.005f, 0f, 1f); obj.PbrHeightScaleCenter = hsc;
                float hct = obj.PbrHeightContrast; Tune("Contrast##height", ref hct, 0.01f, 0.1f, 4f); obj.PbrHeightContrast = hct;
                float hcc = obj.PbrHeightContrastCenter; Tune("Contrast Center##height", ref hcc, 0.005f, 0f, 1f); obj.PbrHeightContrastCenter = hcc;
                float hof = obj.PbrHeightOffset; Tune("Offset##height", ref hof, 0.005f, -0.5f, 0.5f); obj.PbrHeightOffset = hof;
            }

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.95f, 0.55f, 1f), "Emission");
            float ei = obj.PbrEmissionIntensity; Tune("Intensity##emission", ref ei, 0.05f, 0f, 5f); obj.PbrEmissionIntensity = ei;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.75f, 0.85f, 1f), "Mapping");
            float til = obj.PbrTexTiling; Tune("Map Tiling##object", ref til, 0.05f, 0.1f, 10f); obj.PbrTexTiling = til;
            float parallax = obj.PbrParallaxScale; Tune("Parallax Depth##object", ref parallax, 0.005f, 0f, 0.5f); obj.PbrParallaxScale = parallax;
            float pomSh = obj.PbrPomShadowStrength; Tune("Relief Shadow##object", ref pomSh, 0.01f, 0f, 1f); obj.PbrPomShadowStrength = pomSh;

            ImGui.Spacing();
            if (ImGui.Button("Reset tuning to defaults"))
            {
                obj.PbrAlbedoBrightness = 1f; obj.PbrAlbedoSaturation = 1f; obj.PbrAlbedoContrast = 1f;
                obj.PbrNormalStrength = 1f; obj.PbrNormalBlur = 0f;
                obj.PbrMetallicThreshold = 0.5f; obj.PbrMetallicSoftness = 0.1f; obj.PbrMetallicStrength = 1f;
                obj.PbrRoughnessStrength = 1f; obj.PbrRoughnessInvert = false;
                obj.PbrAoStrength = 1f; obj.PbrAoBrightness = 0f;
                obj.PbrHeightStrength = 1f; obj.PbrHeightInvert = false; obj.PbrHeightBlur = 0f;
                obj.PbrHeightContrast = 1f; obj.PbrHeightContrastCenter = 0.5f; obj.PbrHeightOffset = 0f; obj.PbrHeightScaleCenter = 0.5f;
                obj.PbrVertexDisplaceScale = 0.15f; obj.PbrVertexOffset = 0f;
                // Terrain base shape back to defaults (base 0.15, displace 0,
                // offset 0, strength 1, tiling 1).
                obj.TerrainBaseHeight = 0.15f; obj.TerrainHeightScale = 0f;
                obj.TerrainHeightOffset = 0f; obj.TerrainHeightStrength = 1f;
                obj.TerrainHeightTilingX = 1f; obj.TerrainHeightTilingY = 1f;
                obj.PbrVertexSegments = EditorObject.PbrDisplaceSegments; obj.PbrVertexChunk = 1; obj.MarkDirty();
                obj.PbrEmissionIntensity = 1f;
                // Mapping defaults: tiling 1×, parallax OFF (0), relief shadow OFF (0).
                obj.PbrTexTiling = 1f; obj.PbrParallaxScale = 0f; obj.PbrPomShadowStrength = 0f;
            }
        }

        // unsafe: ImGui.AcceptDragDropPayload returns a pointer (same as the Inspector).
        /// <summary>Apply a confirmed file-dialog selection to the object PBR map slot that
        /// opened it (slot key like "albedo", "normal", …).</summary>
        private void ApplyDialogResult(EditorObject obj)
        {
            string slot = _dialogSlot;
            _dialogSlot = ""; // always consume — a stale slot must never leak into a later selection
            if (!_dialog.IsConfirmed || string.IsNullOrEmpty(_dialog.SelectedPath)) return;
            string sel = _dialog.SelectedPath;
            if (slot == "terrain") { obj.TerrainHeightPath = sel; return; }
            int idx = Array.IndexOf(SlotKeys, slot);
            if (idx >= 0) SetMap(obj, idx, sel);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────
        private void SetMap(EditorObject obj, int index, string value)
        {
            switch (index)
            {
                case 0: obj.PbrAlbedoPath = value; break;
                case 1: obj.PbrNormalPath = value; break;
                case 2: obj.PbrMetallicPath = value; break;
                case 3: obj.PbrRoughnessPath = value; break;
                case 4: obj.PbrAoPath = value; break;
                case 5: obj.PbrHeightPath = value; break;
                case 6: obj.PbrEmissionPath = value; break;
            }
            obj.InvalidatePbrTextures();
        }
    }
}
