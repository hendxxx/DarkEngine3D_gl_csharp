using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Terrain Brush panel — all terrain painting settings in one place (moved out of the
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

    public void Render()
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

        // ── Brush tool info ──
        string[] toolNames = ["⛰ Sculpt", "🎨 Paint", "🌀 Smooth", "⏹ Flatten"];
        int mode = Math.Clamp(_bridge.TerrainBrushMode, 0, toolNames.Length - 1);
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Brush (Viewport)");
        ImGui.TextDisabled($"Active tool: {toolNames[mode]}");
        ImGui.TextDisabled("Left-drag = apply · Ctrl = reverse · Shift = fine control.\nUse the viewport toolbar to switch tools.");

        ImGui.Spacing();
        ImGui.Separator();

        // ── Brush settings (stored per terrain object → persist with the scene) ──
        float bSize = editorObj.TerrainBrushSize;
        if (ImGui.DragFloat("Brush Size", ref bSize, 0.1f, 0.5f, 200f, "%.1f"))
            editorObj.TerrainBrushSize = Math.Clamp(bSize, 0.5f, 200f);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Brush radius in world units (shared by all brush tools). Ctrl+scroll in the viewport resizes it.");

        float bStr = editorObj.TerrainBrushStrength;
        if (ImGui.DragFloat("Brush Strength", ref bStr, 0.005f, 0.01f, 2f, "%.3f"))
            editorObj.TerrainBrushStrength = Math.Clamp(bStr, 0.01f, 2f);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("⛰ Height added/removed per 60fps-frame (world units); 🌀/⏹ blend amount per stamp (0..1).\nHold Shift in the viewport for 15% strength (fine strokes).");

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
            ImGui.SetTooltip("How the brush weight falls off toward its edge (like Unreal's brush falloff presets).\nLinear = cone · Smooth = round center · Sharp = strong center · Spherical = classic · Soft = gentle edges.");

        ImGui.Spacing();
        ImGui.Separator();

        // ── Brush ring highlight (3D ring on the terrain surface) ──
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Ring Highlight");
        var ringCol = new Vector4(editorObj.BrushIndicatorColor.X, editorObj.BrushIndicatorColor.Y, editorObj.BrushIndicatorColor.Z, editorObj.BrushIndicatorAlpha);
        if (ImGui.ColorEdit4("Ring Color", ref ringCol))
        {
            editorObj.BrushIndicatorColor = new Vector3(ringCol.X, ringCol.Y, ringCol.Z);
            editorObj.BrushIndicatorAlpha = Math.Clamp(ringCol.W, 0f, 1f);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Color + transparency of the brush ring shown on the terrain surface while the brush tool is active.\nSaved with the scene.");

        // ── Ring color presets (quick pick) ──
        ImGui.Spacing();
        ImGui.TextDisabled("Presets:");
        if (ImGui.Button("🟢 Hijau"))
            editorObj.BrushIndicatorColor = new Vector3(0.2f, 1f, 0.4f);
        ImGui.SameLine();
        if (ImGui.Button("🔴 Merah"))
            editorObj.BrushIndicatorColor = new Vector3(1f, 0.25f, 0.25f);
        ImGui.SameLine();
        if (ImGui.Button("🟡 Kuning"))
            editorObj.BrushIndicatorColor = new Vector3(1f, 0.85f, 0.1f);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Apply a preset color to the brush ring. Alpha/transparency stays as set in 'Ring Color' above.");

        ImGui.Spacing();
        ImGui.Separator();

        // ── Layer paint (🎨 brush) ──
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Layer Paint (🎨 Brush)");
        string[] layerNames = ["1 · Air", "2 · Tanah", "3 · Rumput", "4 · Salju"];
        int layerIdx = Math.Clamp(editorObj.TerrainPaintLayerIndex, 0, 3);
        if (ImGui.Combo("Paint Layer", ref layerIdx, layerNames, layerNames.Length))
        {
            editorObj.TerrainPaintLayerIndex = layerIdx;
            _bridge.TerrainPaintLayerIndex = layerIdx; // sync the viewport tool
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Layer drawn by the 🎨 Paint brush. Pick the layer in the viewport toolbar too.");

        float pStr = editorObj.TerrainPaintStrength;
        if (ImGui.SliderFloat("Paint Strength", ref pStr, 0.05f, 1f, "%.2f"))
            editorObj.TerrainPaintStrength = pStr;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Weight added to the layer per 🎨 brush stamp (0..1). More stamps = stronger paint.");

        if (ImGui.Button("🧹 Clear Layer Paint", new Vector2(-1, 24)))
        {
            // Record undo (before = painted splat, after = cleared) so Ctrl+Z restores.
            var beforeSplat = editorObj.CaptureTerrainSplat();
            editorObj.ClearTerrainLayerPaint();
            var afterSplat = editorObj.CaptureTerrainSplat();
            if (beforeSplat != null && afterSplat != null && beforeSplat.Length == afterSplat.Length)
                _bridge.OnTerrainLayerPainted?.Invoke(editorObj, beforeSplat, afterSplat);
            Console.WriteLine($"[TerrainBrush] Cleared layer paint on '{editorObj.Name}' (back to auto texturing)");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(editorObj.TerrainSplatIsModified
                ? "Remove ALL manual layer paint — terrain returns to automatic height+slope texturing."
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
