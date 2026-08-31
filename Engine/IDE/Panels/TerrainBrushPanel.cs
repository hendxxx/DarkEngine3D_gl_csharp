using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Terrain Brush panel  all terrain painting settings in one place (moved out of the
/// Inspector): brush size / strength / softness / falloff curve, layer paint, and the
/// brush ring color + transparency. Editing works on the selected terrain plane; when no
/// terrain is selected the panel shows a hint. All values live on the EditorObject so they
/// persist with the scene (and are copied on duplicate).
/// </summary>
public class TerrainBrushPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    public TerrainBrushPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Terrain Brush", null, ref _visible);

    public unsafe void Render()
    {
        if (!_visible) return;

        ImGui.Begin("Terrain Brush", ref _visible);

        var editorObj = _bridge.SelectedEditorObject;
        if (editorObj == null || editorObj.PrimitiveType != EditorPrimitiveType.Plane)
        {
            ImGui.TextDisabled("Select a terrain plane to edit its brush settings.");
            ImGui.End();
            return;
        }

        //  Brush tool info 
        string[] toolNames = ["▲ Sculpt", "▲ Paint", "▲ Smooth", "▲ Flatten"];
        int mode = Math.Clamp(_bridge.TerrainBrushMode, 0, toolNames.Length - 1);
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Brush (Viewport)");
        ImGui.TextDisabled($"Active tool: {toolNames[mode]}");
        ImGui.TextDisabled("Left-drag = apply  Ctrl = reverse  Shift = fine control.\nUse the viewport toolbar to switch tools.");

        ImGui.Spacing();
        ImGui.Separator();

        //  Brush settings (stored per terrain object → persist with the scene) 
        float bSize = editorObj.TerrainBrushSize;
        if (ImGui.DragFloat("Brush Size", ref bSize, 0.1f, 0.5f, 200f, "%.1f"))
            editorObj.TerrainBrushSize = Math.Clamp(bSize, 0.5f, 200f);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Brush radius in world units (shared by all brush tools). Ctrl+scroll in the viewport resizes it.");

        float bStr = editorObj.TerrainBrushStrength;
        if (ImGui.DragFloat("Brush Strength", ref bStr, 0.005f, 0.01f, 2f, "%.3f"))
            editorObj.TerrainBrushStrength = Math.Clamp(bStr, 0.01f, 2f);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(" Height added/removed per 60fps-frame (world units); / blend amount per stamp (0..1).\nHold Shift in the viewport for 15% strength (fine strokes).");

        float bSoft = editorObj.TerrainBrushSoftness;
        if (ImGui.SliderFloat("Brush Softness", ref bSoft, 0f, 1f, "%.2f"))
            editorObj.TerrainBrushSoftness = bSoft;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Falloff amount: 0 = hard edge, 1 = the full falloff curve below.");

        // Falloff curve presets (Unreal-style brush falloff selection)
        string[] falloffNames = ["Linear", "Smooth", "Sharp", "Spherical", "Soft"];
        int falloffIdx = Math.Clamp(editorObj.TerrainBrushFalloff, 0, falloffNames.Length - 1);
        if (ImGui.Combo("Falloff Curve", ref falloffIdx, falloffNames, falloffNames.Length))
            editorObj.TerrainBrushFalloff = falloffIdx;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How the brush weight falls off toward its edge (like Unreal's brush falloff presets).\nLinear = cone  Smooth = round center  Sharp = strong center  Spherical = classic  Soft = gentle edges.");

        ImGui.Spacing();
        ImGui.Separator();

        //  Brush ring highlight (3D ring on the terrain surface) 
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Ring Highlight");
        var ringCol = new Vector4(editorObj.BrushIndicatorColor.X, editorObj.BrushIndicatorColor.Y, editorObj.BrushIndicatorColor.Z, editorObj.BrushIndicatorAlpha);
        if (ImGui.ColorEdit4("Ring Color", ref ringCol))
        {
            editorObj.BrushIndicatorColor = new Vector3(ringCol.X, ringCol.Y, ringCol.Z);
            editorObj.BrushIndicatorAlpha = Math.Clamp(ringCol.W, 0f, 1f);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Color + transparency of the brush ring shown on the terrain surface while the brush tool is active.\nSaved with the scene.");

        //  Ring color presets (quick pick) 
        ImGui.Spacing();
        ImGui.TextDisabled("Presets:");
        if (ImGui.Button("☀ Hijau"))
            editorObj.BrushIndicatorColor = new Vector3(0.2f, 1f, 0.4f);
        ImGui.SameLine();
        if (ImGui.Button("☀ Merah"))
            editorObj.BrushIndicatorColor = new Vector3(1f, 0.25f, 0.25f);
        ImGui.SameLine();
        if (ImGui.Button("☀ Kuning"))
            editorObj.BrushIndicatorColor = new Vector3(1f, 0.85f, 0.1f);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Apply a preset color to the brush ring. Alpha/transparency stays as set in 'Ring Color' above.");

        ImGui.Spacing();
        ImGui.Separator();

        //  Paint Layers (independent textures per layer, like terrain layers) 
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Paint Layers");
        ImGui.TextDisabled("Each layer has its own texture + tiling. Click a layer to paint it.");

        int layerCount = Math.Clamp(editorObj.PaintLayerCount, 1, 4);
        bool canAdd = layerCount < 4;
        bool canRemove = layerCount > 1;
        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button("+ Add Layer", new Vector2(ImGui.GetContentRegionAvail().X * 0.5f, 22)))
        {
            editorObj.PaintLayerCount = Math.Min(layerCount + 1, 4);
            editorObj.MarkDirty();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!canRemove);
        if (ImGui.Button("- Remove Last", new Vector2(-1, 22)) && canRemove)
        {
            int lastIdx = layerCount - 1;
            editorObj.SetPaintLayerTexture(lastIdx, "");
            editorObj.PaintLayerCount = layerCount - 1;
            if (editorObj.TerrainPaintLayerIndex >= editorObj.PaintLayerCount)
                editorObj.TerrainPaintLayerIndex = editorObj.PaintLayerCount - 1;
            editorObj.MarkDirty();
        }
        ImGui.EndDisabled();

        Vector4[] layerColors = [
            new(0.20f, 0.50f, 0.85f, 1f), // blue
            new(0.60f, 0.45f, 0.28f, 1f), // brown
            new(0.30f, 0.65f, 0.30f, 1f), // green
            new(0.90f, 0.93f, 0.98f, 1f), // white
        ];
        int activeIdx = Math.Clamp(editorObj.TerrainPaintLayerIndex, 0, layerCount - 1);

        for (int i = 0; i < layerCount; i++)
        {
            ImGui.PushID($"paint_layer_{i}");
            bool isActive = (i == activeIdx);

            if (ImGui.Selectable($"##sel_{i}", isActive, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 22)))
            {
                activeIdx = i;
                editorObj.TerrainPaintLayerIndex = i;
                _bridge.TerrainPaintLayerIndex = i;
            }
            ImGui.SameLine();
            ImGui.TextColored(layerColors[i], $"> Layer {i + 1}");
            ImGui.SameLine();
            string tex = editorObj.GetPaintLayerTexture(i);
            ImGui.TextDisabled(string.IsNullOrEmpty(tex) ? "(no texture)" : System.IO.Path.GetFileName(tex));
            if (editorObj.PaintLayerStochastic[i])
            {
                ImGui.SameLine();
                ImGui.TextDisabled("[R]");
            }

            ImGui.PopID();
        }

        ImGui.Spacing();
        ImGui.Separator();

        //  Active layer editing 
        if (activeIdx >= 0 && activeIdx < 4)
        {
            ImGui.PushID($"paint_edit_{activeIdx}");
            ImGui.TextColored(layerColors[activeIdx], $"Editing Layer {activeIdx + 1}");

            // Texture path with drag-and-drop
            string texPath = editorObj.GetPaintLayerTexture(activeIdx);
            ImGui.Text("Texture:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##paint_tex", ref texPath, 512))
            {
                editorObj.SetPaintLayerTexture(activeIdx, texPath);
                editorObj.MarkDirty();
            }
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                {
                    editorObj.SetPaintLayerTexture(activeIdx, AssetBrowserPanel._dragImagePath);
                    editorObj.MarkDirty();
                    AssetBrowserPanel._dragImagePath = null;
                }
                ImGui.EndDragDropTarget();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag texture from Asset Browser to assign");

            // Clear button for this texture
            ImGui.SameLine();
            if (ImGui.Button("X", new Vector2(24, 0)))
            {
                editorObj.SetPaintLayerTexture(activeIdx, "");
                editorObj.MarkDirty();
            }

            // Tiling
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Tiling");
            var tiling = editorObj.PaintLayerTiling[activeIdx];
            float tx = tiling.X;
            float ty = tiling.Y;
            if (ImGui.DragFloat("Tiling X", ref tx, 0.01f, 0.01f, 10f, "%.2f"))
            {
                editorObj.PaintLayerTiling[activeIdx] = new System.Numerics.Vector2(Math.Max(0.01f, tx), editorObj.PaintLayerTiling[activeIdx].Y);
                editorObj.MarkDirty();
            }
            if (ImGui.DragFloat("Tiling Y", ref ty, 0.01f, 0.01f, 10f, "%.2f"))
            {
                editorObj.PaintLayerTiling[activeIdx] = new System.Numerics.Vector2(editorObj.PaintLayerTiling[activeIdx].X, Math.Max(0.01f, ty));
                editorObj.MarkDirty();
            }
            bool linked = Math.Abs(tx - ty) < 0.001f;
            if (ImGui.Checkbox("Link X/Y", ref linked))
            {
                if (linked) editorObj.PaintLayerTiling[activeIdx] = new System.Numerics.Vector2(tx, tx);
                editorObj.MarkDirty();
            }
            bool stochastic = editorObj.PaintLayerStochastic[activeIdx];
            if (ImGui.Checkbox("Random Tile", ref stochastic))
            {
                editorObj.PaintLayerStochastic[activeIdx] = stochastic;
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Randomize sampling per tile to break up the repeating pattern.");

            ImGui.PopID(); // paint_edit_
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Paint strength
        float pStr = editorObj.TerrainPaintStrength;
        if (ImGui.SliderFloat("Paint Strength", ref pStr, 0.05f, 1f, "%.2f"))
            editorObj.TerrainPaintStrength = pStr;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Weight added per brush stamp (0..1). More stamps = stronger paint.");

        if (ImGui.Button("Clear Layer Paint", new Vector2(-1, 24)))
        {
            var beforeSplat = editorObj.CaptureTerrainSplat();
            editorObj.ClearTerrainLayerPaint();
            var afterSplat = editorObj.CaptureTerrainSplat();
            if (beforeSplat != null && afterSplat != null && beforeSplat.Length == afterSplat.Length)
                _bridge.OnTerrainLayerPainted?.Invoke(editorObj, beforeSplat, afterSplat);
            Console.WriteLine($"[TerrainBrush] Cleared layer paint on '{editorObj.Name}'");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(editorObj.TerrainSplatIsModified
                ? "Remove ALL manual layer paint - terrain returns to automatic texturing."
                : "No manual layer paint on this terrain yet.");

        ImGui.Spacing();
        ImGui.Separator();

        // ── Quick action buttons ──
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Quick Actions");
        if (ImGui.Button("🌀 Smooth All Terrain", new Vector2(-1, 28)))
        {
            editorObj.SmoothAllTerrain(2, 0.4f);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Smooth the ENTIRE terrain heightmap in one pass (2 passes, 40%% strength).\nGood for removing rough spots after sculpting.");

        ImGui.Spacing();
        ImGui.TextDisabled($"Brush settings + ring color are saved with '{editorObj.Name}'.");
        ImGui.End();
    }
}
