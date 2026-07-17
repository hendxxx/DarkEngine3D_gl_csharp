using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// SceneDetail panel — displays the current scene's UI element tree.
/// Only shows elements with IsVisible = true.
/// Shows a tree structure like:
///   Scene: MainMenu
///     🔘 START GAME
///     🔘 SETTINGS
///     🔘 EXIT
///     💬 ExitConfirm
///       🔘 CANCEL
///       🔘 YES
/// Clicking any element selects it in the Inspector panel for editing.
/// Right-click for context menu: Select, Rename, Delete Item.
/// </summary>
public class HierarchyPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    public HierarchyPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("SceneDetail", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.Begin("SceneDetail", ref _visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var rootElements = _bridge.SceneRootElements;

        if (rootElements == null || rootElements.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No UI elements");
            ImGui.TextDisabled("Scene has not populated the hierarchy");
            ImGui.End();
            return;
        }

        // ── Render each root node ──
        // Root elements (scene containers) always show — only filter children by IsVisible.
        foreach (var root in rootElements)
        {
            RenderTreeNode(root);
        }

        ImGui.End();
    }

    /// <summary>Recursively render a UIElement tree node (skips invisible children).</summary>
    private void RenderTreeNode(UIElement element)
    {
        if (element == null) return;

        string label = $"{element.GetIcon()} {element.Name}";

        // Only count visible children
        int visibleChildCount = 0;
        foreach (var c in element.Children)
            if (c.IsVisible) visibleChildCount++;

        bool isSelected = _bridge.SelectedUIElement == element;

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanFullWidth;
        if (visibleChildCount == 0)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (isSelected)
            flags |= ImGuiTreeNodeFlags.Selected;

        // Use OpenOnArrow so clicking the label selects, clicking the arrow expands
        flags |= ImGuiTreeNodeFlags.OpenOnArrow;

        bool nodeOpen = ImGui.TreeNodeEx(label, flags);

        // Click on the node label → select it
        if (ImGui.IsItemClicked())
        {
            _bridge.SelectedUIElement = element;
        }

        // Drag source (for future drag-reorder)
        if (ImGui.BeginDragDropSource())
        {
            ImGui.SetDragDropPayload("SCENEDETAIL_NODE", nint.Zero, 0);
            ImGui.Text(label);
            ImGui.EndDragDropSource();
        }

        // Context menu
        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Select"))
                _bridge.SelectedUIElement = element;

            if (ImGui.MenuItem("Rename"))
                ImGui.OpenPopup("Rename##" + element.Name);

            ImGui.Separator();

            // ── Delete Item ──
            // Push red color for the Delete menu item
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
            bool deleted = ImGui.MenuItem("Delete Item", "Del");
            ImGui.PopStyleColor();

            if (deleted)
            {
                DeleteUIElement(element);
            }

            ImGui.EndPopup();
        }

        // Recursively render only visible children
        if (visibleChildCount > 0 && nodeOpen)
        {
            foreach (var child in element.Children)
            {
                if (child.IsVisible)
                    RenderTreeNode(child);
            }
            ImGui.TreePop();
        }
    }

    /// <summary>Remove a UIElement from its parent's Children list and clear selection.</summary>
    private void DeleteUIElement(UIElement element)
    {
        if (element == null) return;

        // Clear selection if this element was selected
        if (_bridge.SelectedUIElement == element)
        {
            _bridge.SelectedUIElement = null;
        }

        // Try to find and remove from parent scene root or parent element
        var rootElements = _bridge.SceneRootElements;
        if (rootElements != null)
        {
            // Search recursively from root
            if (RemoveFromParent(rootElements, element))
            {
                Console.WriteLine($"[SceneDetail] Deleted element: {element.Name}");
                return;
            }
        }

        Console.WriteLine($"[SceneDetail] Could not find element '{element.Name}' in hierarchy");
    }

    /// <summary>Recursively search for an element and remove it from its parent's Children.</summary>
    private static bool RemoveFromParent(IEnumerable<UIElement> parents, UIElement target)
    {
        foreach (var parent in parents)
        {
            // Check direct children
            if (parent.Children.Remove(target))
                return true;

            // Recurse into grandchildren
            if (RemoveFromParent(parent.Children, target))
                return true;
        }
        return false;
    }
}
