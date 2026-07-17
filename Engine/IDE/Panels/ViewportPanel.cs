using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Viewport panel — displays the game's rendered scene texture inside an ImGui panel.
/// Supports aspect-ratio-correct scaling and right-click to focus.
/// This panel replaces the full-screen game view when the IDE is active.
/// </summary>
public class ViewportPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    public ViewportPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Viewport", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.Begin("Viewport", ref _visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        // Track whether the viewport is focused — used by GameScene to gate input processing
        // Only when the viewport has keyboard focus (user clicked on it) will game input be processed.
        _bridge.IsViewportFocused = ImGui.IsWindowFocused();

        var avail = ImGui.GetContentRegionAvail();
        if (avail.X > 0 && avail.Y > 0 && _bridge.SceneTextureID != 0)
        {
            // Calculate aspect-ratio-correct image size to fill the panel
            float panelAspect = avail.X / avail.Y;
            float sceneAspect = _bridge.SceneTextureWidth / (float)_bridge.SceneTextureHeight;

            Vector2 imageSize;
            if (panelAspect > sceneAspect)
            {
                // Panel is wider — fit to height
                imageSize = new Vector2(avail.Y * sceneAspect, avail.Y);
            }
            else
            {
                // Panel is taller — fit to width
                imageSize = new Vector2(avail.X, avail.X / sceneAspect);
            }

            // Center the image in the panel
            float offsetX = (avail.X - imageSize.X) * 0.5f;
            float offsetY = (avail.Y - imageSize.Y) * 0.5f;
            ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(offsetX, offsetY));

            // Display the scene texture as an ImGui image
            // Flip UV vertically because OpenGL textures have origin at bottom-left,
            // while ImGui expects top-left origin.
            var uv0 = new Vector2(0, 1); // top-left of ImGui = bottom-left of OpenGL
            var uv1 = new Vector2(1, 0); // bottom-right of ImGui = top-right of OpenGL
            ImGui.Image((nint)(nint)_bridge.SceneTextureID, imageSize, uv0, uv1);
        }
        else
        {
            // No texture yet - show placeholder
            var center = ImGui.GetCursorScreenPos() + avail * 0.5f;
            var textSize = ImGui.CalcTextSize("No Scene");
            ImGui.GetWindowDrawList().AddText(
                center - textSize * 0.5f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.4f, 1f)),
                "No Scene");
        }

        ImGui.End();
    }
}
