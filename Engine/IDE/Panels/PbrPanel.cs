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
    /// </list>
    /// Applies to Box / Sphere. Planes (terrain) get 5 per-texture layer sections — each
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
            "Ambient Occlusion (AO)", "Height / Displacement", "Emission",
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
                ImGui.TextDisabled("Select a Box / Sphere in the scene to edit its PBR material.");
                ImGui.End();
                _dialog.Render();
                return;
            }

            bool isPrimitive = obj.PrimitiveType is EditorPrimitiveType.Box or EditorPrimitiveType.Sphere;

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
                ImGui.TextDisabled("Terrain plane — plain layer textures (air / dirt / grass / snow), PBR removed.");
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
        private void RenderTuning(EditorObject obj)
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
            float b = obj.TerrainPbrAlbedoBrightness; Tune("Brightness##albedo", ref b, 0.01f, 0f, 2f); obj.TerrainPbrAlbedoBrightness = b;
            float sat = obj.TerrainPbrAlbedoSaturation; Tune("Saturation##albedo", ref sat, 0.01f, 0f, 2f); obj.TerrainPbrAlbedoSaturation = sat;
            float ct = obj.TerrainPbrAlbedoContrast; Tune("Contrast##albedo", ref ct, 0.01f, 0f, 2f); obj.TerrainPbrAlbedoContrast = ct;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Normal Map");
            float ns = obj.TerrainPbrNormalStrength; Tune("Strength##normal", ref ns, 0.01f, 0f, 2f); obj.TerrainPbrNormalStrength = ns;
            float nb = obj.TerrainPbrNormalBlur; Tune("Blur (texels)##normal", ref nb, 0.05f, 0f, 8f); obj.TerrainPbrNormalBlur = nb;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.9f, 0.8f, 0.7f, 1f), "Metallic");
            float mt = obj.TerrainPbrMetallicThreshold; Tune("Threshold##metallic", ref mt, 0.01f, 0f, 1f); obj.TerrainPbrMetallicThreshold = mt;
            float ms = obj.TerrainPbrMetallicSoftness; Tune("Softness##metallic", ref ms, 0.01f, 0f, 0.5f); obj.TerrainPbrMetallicSoftness = ms;
            float mst = obj.TerrainPbrMetallicStrength; Tune("Strength##metallic", ref mst, 0.01f, 0f, 1f); obj.TerrainPbrMetallicStrength = mst;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.8f, 0.9f, 0.7f, 1f), "Roughness");
            float rs = obj.TerrainPbrRoughnessStrength; Tune("Strength##roughness", ref rs, 0.01f, 0f, 2f); obj.TerrainPbrRoughnessStrength = rs;
            bool ri = obj.TerrainPbrRoughnessInvert;
            if (ImGui.Checkbox("Invert (smoothness map)##roughness", ref ri)) obj.TerrainPbrRoughnessInvert = ri;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.8f, 0.9f, 1f), "Ambient Occlusion");
            float aos = obj.TerrainPbrAoStrength; Tune("Strength##ao", ref aos, 0.01f, 0f, 2f); obj.TerrainPbrAoStrength = aos;
            float aob = obj.TerrainPbrAoBrightness; Tune("Brightness##ao", ref aob, 0.01f, 0f, 1f); obj.TerrainPbrAoBrightness = aob;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.9f, 1f), "Height / Displacement");
            float hs = obj.TerrainPbrHeightStrength; Tune("Strength##height", ref hs, 0.01f, 0f, 2f); obj.TerrainPbrHeightStrength = hs;
            bool hi = obj.TerrainPbrHeightInvert;
            if (ImGui.Checkbox("Invert (valleys/peaks)##height", ref hi)) obj.TerrainPbrHeightInvert = hi;
            float hb = obj.TerrainPbrHeightBlur; Tune("Blur (texels)##height", ref hb, 0.05f, 0f, 8f); obj.TerrainPbrHeightBlur = hb;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.95f, 0.55f, 1f), "Emission");
            float ei = obj.TerrainPbrEmissionIntensity; Tune("Intensity##emission", ref ei, 0.05f, 0f, 5f); obj.TerrainPbrEmissionIntensity = ei;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.75f, 0.85f, 1f), "Mapping");
            float til = obj.PbrTexTiling; Tune("Map Tiling##object", ref til, 0.05f, 0.1f, 10f); obj.PbrTexTiling = til;
            float parallax = obj.PbrParallaxScale; Tune("Parallax Depth##object", ref parallax, 0.005f, 0f, 0.5f); obj.PbrParallaxScale = parallax;

            ImGui.Spacing();
            if (ImGui.Button("Reset tuning to defaults"))
            {
                obj.TerrainPbrAlbedoBrightness = 1f; obj.TerrainPbrAlbedoSaturation = 1f; obj.TerrainPbrAlbedoContrast = 1f;
                obj.TerrainPbrNormalStrength = 1f; obj.TerrainPbrNormalBlur = 0f;
                obj.TerrainPbrMetallicThreshold = 0.5f; obj.TerrainPbrMetallicSoftness = 0.1f; obj.TerrainPbrMetallicStrength = 1f;
                obj.TerrainPbrRoughnessStrength = 1f; obj.TerrainPbrRoughnessInvert = false;
                obj.TerrainPbrAoStrength = 1f; obj.TerrainPbrAoBrightness = 0f;
                obj.TerrainPbrHeightStrength = 1f; obj.TerrainPbrHeightInvert = false; obj.TerrainPbrHeightBlur = 0f;
                obj.TerrainPbrEmissionIntensity = 1f;
                obj.PbrTexTiling = 1f; obj.PbrParallaxScale = 0.15f;
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
