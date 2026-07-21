using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// SceneDetail panel — displays the current scene's UI element tree.
/// Shows a tree structure like:
///   📁 MainMenu
///     🔘 START GAME
///     🔘 SETTINGS
///     🔘 EXIT
///     💬 ExitConfirm
///       🔘 CANCEL
///       🔘 YES
/// 
/// Features a toolbar: Undo / Redo / Add / Edit / Del / Save.
/// Full undo/redo support via Ctrl+Z / Ctrl+Y and toolbar buttons.
/// Clicking any element selects it in the Inspector panel for editing.
/// Right-click for context menu: Select, Rename, Add Child, Delete Item.
/// </summary>
public class HierarchyPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    // ── Popup state ──
    private bool _showAddPopup = false;
    private bool _showRenamePopup = false;
    private bool _showReloadConfirm = false;
    private string _addNameBuffer = "";
    private int _addTypeIdx = 0; // 0=Button, 1=Label, 2=Container, 3=Dialog
    private string _renameBuffer = "";
    private string _renamePreviousName = ""; // captured before dialog opens, for undo
    private const int InputBufSize = 256;

    // ── Drag & drop state ──
    private UIElement? _dragSourceElement = null;
    private bool _isDragging = false;

    // ── Pending elements waiting for SceneRoot to be available ──
    private List<UIElement>? _pendingOrphanedElements = null;

    // ── Undo / Redo ──
    private readonly List<UndoRedoAction> _undoStack = [];
    private readonly List<UndoRedoAction> _redoStack = [];
    private const int MaxUndoSteps = 50;
    private bool _undoShortcutWasDown = false; // for Ctrl+Z edge detection
    private bool _redoShortcutWasDown = false; // for Ctrl+Y edge detection

    // ── Save notification state ──
    private string _saveNotificationText = "";
    private float _saveNotificationTimer = 0f;
    private const float SaveNotifDuration = 2.5f;

    // ── Colors ──
    private static readonly Vector4 ColUndoBtn     = new(0.25f, 0.25f, 0.30f, 1f);
    private static readonly Vector4 ColUndoBtnHov  = new(0.35f, 0.35f, 0.45f, 1f);
    private static readonly Vector4 ColAddBtn      = new(0.15f, 0.50f, 0.25f, 1f);
    private static readonly Vector4 ColAddBtnHov   = new(0.25f, 0.70f, 0.35f, 1f);
    private static readonly Vector4 ColEditBtn     = new(0.20f, 0.35f, 0.55f, 1f);
    private static readonly Vector4 ColEditBtnHov  = new(0.30f, 0.50f, 0.75f, 1f);
    private static readonly Vector4 ColDelBtn      = new(0.55f, 0.15f, 0.15f, 1f);
    private static readonly Vector4 ColDelBtnHov   = new(0.75f, 0.25f, 0.25f, 1f);
    private static readonly Vector4 ColSaveBtn     = new(0.10f, 0.55f, 0.30f, 1f);
    private static readonly Vector4 ColSaveBtnHov  = new(0.15f, 0.70f, 0.40f, 1f);
    private static readonly Vector4 ColReloadBtn   = new(0.40f, 0.30f, 0.55f, 1f);
    private static readonly Vector4 ColReloadBtnHov= new(0.55f, 0.40f, 0.75f, 1f);
    private static readonly Vector4 ColDim         = new(0.5f, 0.5f, 0.6f, 1f);
    private static readonly Vector4 ColText        = new(0.9f, 0.9f, 0.95f, 1f);
    private static readonly Vector4 ColGreen       = new(0.3f, 0.85f, 0.4f, 1f);
    private static readonly Vector4 ColWarn        = new(1.0f, 0.6f, 0.2f, 1f);
    private static readonly Vector4 ColWarnDim     = new(0.7f, 0.4f, 0.1f, 1f);

    private static readonly string[] ElementTypeLabels = ["Button", "Label", "Container", "Dialog"];

    /// <summary>Recorded action for undo/redo.</summary>
    private struct UndoRedoAction
    {
        public enum ActionType { Add, Delete, Rename, Move, Transform }
        public ActionType Type;

        // For Add / Delete / Move: the element involved
        public UIElement? Element;
        public UIElement? Parent;       // old parent (for Move: before move)
        public int ChildIndex;           // old index (for Move: before move)

        // For Move: new parent & index
        public UIElement? NewParent;
        public int NewChildIndex;

        // For Rename
        public string OldName;
        public string NewName;

        // For Transform (position/size change from Viewport drag)
        public float OldX, OldY, OldW, OldH;
        public float NewX, NewY, NewW, NewH;
    }

    public HierarchyPanel(IDEBridge bridge)
    {
        _bridge = bridge;

        // Wire up the transform undo delegate so ViewportPanel can record position/size undos
        _bridge.RecordTransformUndo = (elem, oldX, oldY, oldW, oldH, newX, newY, newW, newH) =>
        {
            PushUndo(new UndoRedoAction
            {
                Type = UndoRedoAction.ActionType.Transform,
                Element = elem,
                OldX = oldX, OldY = oldY, OldW = oldW, OldH = oldH,
                NewX = newX, NewY = newY, NewW = newW, NewH = newH,
            });
        };
    }

    public void ShowInMenu() => ImGui.MenuItem("SceneDetail", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        // ── Drag state is NOT reset here intentionally ──
        // ImGui's drag-drop spans multiple frames. On the frame where the mouse is
        // released (drop delivery), BeginDragDropSource returns false, but we still
        // need _dragSourceElement to be set from the previous frame.
        // State is cleared in HandleDropTarget after successful move, or when
        // BeginDragDropSource doesn't fire and no drag is in progress.
        // We only clear stale state if a drag was active but is no longer.
        if (_dragSourceElement != null && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            // Mouse is not dragging and we have stale drag state — clear it
            // But don't clear on the frame where AcceptDragDropPayload delivers the drop
            // because ImGui.IsMouseDragging returns false on the release frame too.
            // Instead, HandleDropTarget will clear after successful drop execution.
            if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                // Not a release frame either — drag was cancelled without delivery
                _dragSourceElement = null;
                _isDragging = false;
            }
        }

        ImGui.Begin("SceneDetail", ref _visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var rootElements = _bridge.SceneRootElements;
        bool hasSelection = _bridge.SelectedUIElement != null;
        int multiCount = _bridge.SelectedUIElements?.Count > 1 ? _bridge.SelectedUIElements.Count : 0;
        bool hasRoots = rootElements != null && rootElements.Count > 0;
        bool canUndo = _undoStack.Count > 0;
        bool canRedo = _redoStack.Count > 0;

        // ── Toolbar Row 1: Undo / Redo / + Add / Edit / Del ──
        {
            float btnWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 4f) / 5f;

            // Undo button (dark)
            ImGui.PushStyleColor(ImGuiCol.Button, ColUndoBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColUndoBtnHov);
            ImGui.BeginDisabled(!canUndo);
            if (ImGui.Button("↩ Undo", new Vector2(btnWidth, 26)))
            {
                ExecuteUndo();
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Undo last action (Ctrl+Z)");

            ImGui.SameLine();

            // Redo button (dark)
            ImGui.PushStyleColor(ImGuiCol.Button, ColUndoBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColUndoBtnHov);
            ImGui.BeginDisabled(!canRedo);
            if (ImGui.Button("↪ Redo", new Vector2(btnWidth, 26)))
            {
                ExecuteRedo();
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Redo last undone action (Ctrl+Y)");

            ImGui.SameLine();

            // Add button (green)
            ImGui.PushStyleColor(ImGuiCol.Button, ColAddBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColAddBtnHov);
            if (ImGui.Button("+ Add", new Vector2(btnWidth, 26)))
            {
                _showAddPopup = true;
                _addNameBuffer = "";
                _addTypeIdx = 0;
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Add a new UI element to the hierarchy");

            ImGui.SameLine();

            // Rename button (blue) — disabled when multi-selected (use Inspector for single)
            string renameLabel = multiCount > 0 ? $"Rename ({multiCount})" : "Rename";
            ImGui.PushStyleColor(ImGuiCol.Button, ColEditBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColEditBtnHov);
            ImGui.BeginDisabled(!hasSelection || multiCount > 0);
            if (ImGui.Button(renameLabel, new Vector2(btnWidth, 26)))
            {
                var sel = _bridge.SelectedUIElement;
                if (sel != null)
                {
                    _renamePreviousName = sel.Name;
                    _renameBuffer = sel.Name;
                    _showRenamePopup = true;
                }
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(multiCount > 0 ? "Cannot rename multiple elements" : "Rename the selected element");

            ImGui.SameLine();

            // Delete button (red) — deletes ALL selected when multi
            string delLabel = multiCount > 0 ? $"Del ({multiCount})" : "Del";
            ImGui.PushStyleColor(ImGuiCol.Button, ColDelBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColDelBtnHov);
            ImGui.BeginDisabled(!hasSelection);
            if (ImGui.Button(delLabel, new Vector2(btnWidth, 26)))
            {
                DeleteSelectedElement();
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(multiCount > 0 ? $"Delete {multiCount + 1} selected elements" : "Delete the selected element");
        }

        // ── Toolbar Row 2: Save / Reload from .ing ──
        {
            float btnWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 1f) / 2f;

            // Save button (green)
            ImGui.PushStyleColor(ImGuiCol.Button, ColSaveBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColSaveBtnHov);
            ImGui.BeginDisabled(!hasRoots);
            if (ImGui.Button("Save", new Vector2(btnWidth, 26)))
            {
                SaveCurrentSceneHierarchy();
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Save current hierarchy to .ing file");

            ImGui.SameLine();

            // Reload button (purple) — always enabled so user can recover from empty state
            ImGui.PushStyleColor(ImGuiCol.Button, ColReloadBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColReloadBtnHov);
            if (ImGui.Button("↻ Reload", new Vector2(btnWidth, 26)))
            {
                if (_undoStack.Count > 0 || _redoStack.Count > 0 || hasSelection)
                    _showReloadConfirm = true;
                else
                    ReloadSceneHierarchy();
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reload hierarchy from .ing file (discard unsaved edits)");
        }

        // ── Keyboard shortcuts (Ctrl+Z / Ctrl+Y) ──
        // Must be checked outside any disabled block so they always work
        {
            bool ctrlHeld = ImGui.GetIO().KeyCtrl;
            bool zDown = ImGui.IsKeyDown(ImGuiKey.Z);
            bool yDown = ImGui.IsKeyDown(ImGuiKey.Y);

            // Ctrl+Z: Undo
            if (ctrlHeld && zDown && !_undoShortcutWasDown && canUndo)
            {
                ExecuteUndo();
            }
            _undoShortcutWasDown = ctrlHeld && zDown;

            // Ctrl+Y: Redo
            if (ctrlHeld && yDown && !_redoShortcutWasDown && canRedo)
            {
                ExecuteRedo();
            }
            _redoShortcutWasDown = ctrlHeld && yDown;
        }

        ImGui.Separator();

        // ── Save notification bar ──
        if (_saveNotificationTimer > 0f && !string.IsNullOrEmpty(_saveNotificationText))
        {
            float alpha = Math.Min(1f, _saveNotificationTimer);
            var notifCol = new Vector4(0.3f, 0.85f, 0.4f, alpha);
            ImGui.TextColored(notifCol, $"✓ {_saveNotificationText}");
        }

        // ── Undo/Redo hint ──
        if (canUndo || canRedo)
        {
            string hint = "";
            if (canUndo) hint += $"{_undoStack.Count} undo";
            if (canUndo && canRedo) hint += " · ";
            if (canRedo) hint += $"{_redoStack.Count} redo";
            ImGui.TextColored(ColDim, hint);
        }

        // ── Auto-select first editor scene if SceneRoot is null but scenes exist ──
        if (_bridge.SceneRoot == null && _bridge.EditorScenes.Count > 0)
        {
            string firstScene = _bridge.EditorScenes.Keys.First();
            var editorScene = _bridge.EditorScenes[firstScene];
            _bridge.SelectedEditorScene = firstScene;
            _bridge.SceneRoot = editorScene.Root;
            _bridge.SceneRootElements = new List<UIElement> { editorScene.Root }.AsReadOnly();
            // Select the first child so wireframe/handles appear in the viewport
            _bridge.SelectedUIElements?.Clear();
            if (editorScene.Root.Children.Count > 0)
                _bridge.SelectedUIElement = editorScene.Root.Children[0];
            else
                _bridge.SelectedUIElement = editorScene.Root;
            if (_bridge.SelectedUIElement != null)
                _bridge.SelectedUIElements?.Add(_bridge.SelectedUIElement);
            _undoStack.Clear();
            _redoStack.Clear();
            rootElements = _bridge.SceneRootElements;
            hasRoots = rootElements != null && rootElements.Count > 0;
            Console.WriteLine($"[SceneDetail] Auto-selected editor scene: {firstScene}");
        }

        // ── Scene selector combo (always shown) ──
        if (_bridge.EditorScenes.Count > 0)
        {
            // Build scene name list for combo
            string[] sceneNames = [.. _bridge.EditorScenes.Keys];
            int currentIdx = _bridge.SelectedEditorScene != null
                ? Array.IndexOf(sceneNames, _bridge.SelectedEditorScene)
                : -1;
            if (currentIdx < 0) currentIdx = 0;

            string comboLabel = $"Scene: {sceneNames[currentIdx]}";
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##scene_selector", comboLabel))
            {
                for (int si = 0; si < sceneNames.Length; si++)
                {
                    bool isSel = si == currentIdx;
                    if (ImGui.Selectable(sceneNames[si], isSel) && si != currentIdx)
                    {
                        // Switch to selected editor scene — select first child so handles appear
                        var es = _bridge.EditorScenes[sceneNames[si]];
                        _bridge.SelectedEditorScene = sceneNames[si];
                        _bridge.SceneRoot = es.Root;
                        _bridge.SceneRootElements = new List<UIElement> { es.Root }.AsReadOnly();
                        _bridge.SelectedUIElements?.Clear();
                        if (es.Root.Children.Count > 0)
                            _bridge.SelectedUIElement = es.Root.Children[0];
                        else
                            _bridge.SelectedUIElement = es.Root;
                        if (_bridge.SelectedUIElement != null)
                            _bridge.SelectedUIElements?.Add(_bridge.SelectedUIElement);
                        _undoStack.Clear();
                        _redoStack.Clear();
                        rootElements = _bridge.SceneRootElements;
                        hasRoots = rootElements != null && rootElements.Count > 0;
                        Console.WriteLine($"[SceneDetail] Switched to editor scene: {sceneNames[si]}");
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.Separator();
        }

        // ── Render hierarchy tree ──
        if (!hasRoots)
        {
            ImGui.TextColored(ColDim, "No UI elements");
            ImGui.TextDisabled("Use + Add to create elements");
            if (_bridge.EditorScenes.Count == 0)
            {
                ImGui.TextColored(ColWarnDim, "No scenes exist. Use Scene Manager panel to create one.");
            }
            // Don't return here — popup rendering below must execute so user can add elements!
        }
        else
        {
            // Root elements (scene containers) always show — only filter children by IsVisible.
            foreach (var root in rootElements)
            {
                RenderTreeNode(root);
            }
        }

        ImGui.End();

        // ── Re-parent pending orphaned elements when SceneRoot becomes available ──
        if (_pendingOrphanedElements != null && _pendingOrphanedElements.Count > 0 && _bridge.SceneRoot != null)
        {
            var sceneRoot = _bridge.SceneRoot;
            for (int i = _pendingOrphanedElements.Count - 1; i >= 0; i--)
            {
                var orphan = _pendingOrphanedElements[i];
                int childIdx = sceneRoot.Children.Count;
                sceneRoot.AddChild(orphan);
                Console.WriteLine($"[SceneDetail] Re-parented pending element '{orphan.Name}' to scene root");
                _bridge.SelectedUIElement = orphan;

                // Update the undo action for this pending element so undo works correctly
                for (int u = 0; u < _undoStack.Count; u++)
                {
                    var ua = _undoStack[u];
                    if (ua.Type == UndoRedoAction.ActionType.Add && ReferenceEquals(ua.Element, orphan))
                    {
                        ua.Parent = sceneRoot;
                        ua.ChildIndex = childIdx;
                        _undoStack[u] = ua; // struct, need to re-assign
                        break;
                    }
                }
            }
            _pendingOrphanedElements.Clear();
        }

        // Update save notification timer (always, even when no roots)
        if (_saveNotificationTimer > 0f)
            _saveNotificationTimer -= ImGui.GetIO().DeltaTime;

        // ── Popups ──

        // ── Add Element popup ──
        if (_showAddPopup)
        {
            ImGui.OpenPopup("Add UI Element");
            _showAddPopup = false;
        }

        bool addPopupOpen = true;
        if (ImGui.BeginPopupModal("Add UI Element", ref addPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Create a new UI element:");
            ImGui.Separator();

            ImGui.Text("Name:");
            ImGui.SetNextItemWidth(260);
            ImGui.InputText("##add_name", ref _addNameBuffer, InputBufSize);

            ImGui.Text("Type:");
            ImGui.SetNextItemWidth(260);
            ImGui.Combo("##add_type", ref _addTypeIdx, ElementTypeLabels, ElementTypeLabels.Length);

            ImGui.Separator();

            bool nameValid = !string.IsNullOrWhiteSpace(_addNameBuffer);

            if (ImGui.Button("Create", new Vector2(120, 0)) && nameValid)
            {
                AddNewElement(_addNameBuffer.Trim(), _addTypeIdx);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        // ── Rename popup ──
        if (_showRenamePopup)
        {
            ImGui.OpenPopup("Rename Element");
            _showRenamePopup = false;
        }

        bool renamePopupOpen = true;
        if (ImGui.BeginPopupModal("Rename Element", ref renamePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Enter new name:");
            ImGui.SetNextItemWidth(260);
            ImGui.InputText("##rename", ref _renameBuffer, InputBufSize);

            ImGui.Separator();

            bool nameValid = !string.IsNullOrWhiteSpace(_renameBuffer);

            if (ImGui.Button("OK", new Vector2(120, 0)) && nameValid)
            {
                var sel = _bridge.SelectedUIElement;
                if (sel != null && sel.Name != _renameBuffer.Trim())
                {
                    string oldName = _renamePreviousName;
                    string newName = _renameBuffer.Trim();

                    // Record undo for rename
                    PushUndo(new UndoRedoAction
                    {
                        Type = UndoRedoAction.ActionType.Rename,
                        Element = sel,
                        OldName = oldName,
                        NewName = newName,
                    });

                    sel.Name = newName;
                    Console.WriteLine($"[SceneDetail] Renamed '{oldName}' → '{newName}'");
                }
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        // ── Reload Confirmation popup ──
        if (_showReloadConfirm)
        {
            ImGui.OpenPopup("Reload from .ing?");
            _showReloadConfirm = false;
        }

        bool reloadPopupOpen = true;
        if (ImGui.BeginPopupModal("Reload from .ing?", ref reloadPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextColored(ColWarn, "⚠ All unsaved changes will be lost!");
            ImGui.Separator();
            ImGui.Text("This will revert the hierarchy to the last");
            ImGui.Text("saved .ing file on disk.");
            ImGui.Separator();

            if (ImGui.Button("Yes, Reload", new Vector2(140, 0)))
            {
                ReloadSceneHierarchy();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
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
        bool isInMulti = _bridge.SelectedUIElements?.Contains(element) == true;

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanFullWidth;
        if (visibleChildCount == 0)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (isSelected)
            flags |= ImGuiTreeNodeFlags.Selected;

        // Use OpenOnArrow so clicking the label selects, clicking the arrow expands
        flags |= ImGuiTreeNodeFlags.OpenOnArrow;

        bool nodeOpen = ImGui.TreeNodeEx(label, flags);

        // ── Click detection: primary IsItemClicked + fallback for ActiveId edge cases ──
        // IsItemClicked() checks g.ActiveId which can be non-zero when another widget
        // (e.g. drag-drop source, popup) holds the active ID. The fallback path avoids
        // this check, ensuring selection always works regardless of global ImGui state.
        bool primaryClick = ImGui.IsItemClicked();
        bool fallbackClick = ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool itemClicked = primaryClick || fallbackClick;

        if (itemClicked)
        {
            bool ctrlHeld = ImGui.GetIO().KeyCtrl;
            bool shiftHeld = ImGui.GetIO().KeyShift;

            if (ctrlHeld)
            {
                // Ctrl+Click: toggle this element in the multi-set
                var multi = _bridge.SelectedUIElements;
                if (multi!.Contains(element))
                {
                    multi.Remove(element);
                    if (multi.Count == 0)
                        _bridge.SelectedUIElement = null;
                    else
                        _bridge.SelectedUIElement = multi.Last(); // keep last remaining as primary
                }
                else
                {
                    multi.Add(element);
                    _bridge.SelectedUIElement = element; // newest = primary
                }
            }
            else if (shiftHeld && _bridge.SelectedUIElement != null)
            {
                // Shift+Click: select range from primary to this element
                // Collect visible elements in order, then select the range
                var ordered = CollectVisibleElements(_bridge.SceneRootElements);
                int fromIdx = ordered.IndexOf(_bridge.SelectedUIElement);
                int toIdx = ordered.IndexOf(element);
                if (fromIdx >= 0 && toIdx >= 0)
                {
                    int start = Math.Min(fromIdx, toIdx);
                    int end = Math.Max(fromIdx, toIdx);
                    var multi = _bridge.SelectedUIElements!;
                    multi.Clear();
                    for (int i = start; i <= end; i++)
                        multi.Add(ordered[i]);
                    _bridge.SelectedUIElement = element;
                }
            }
            else
            {
                // Normal click: set as primary and clear multi (unless already in multi)
                var multi = _bridge.SelectedUIElements!;
                if (multi.Count > 0 && multi.Contains(element))
                {
                    // Already in multi-set — just set as primary
                    _bridge.SelectedUIElement = element;
                }
                else
                {
                    multi.Clear();
                    multi.Add(element);
                    _bridge.SelectedUIElement = element;
                }
            }
        }

        // ── Show visual indicator if this element is in the multi-set but not primary ──
        if (isInMulti && !isSelected)
        {
            // Draw a subtle highlight behind the multi-selected items
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            uint multiColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.5f, 0.8f, 0.15f));
            drawList.AddRectFilled(min, max, multiColor);
        }

        // ── Drag source ──
        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
        {
            _dragSourceElement = element;
            _isDragging = true;
            ImGui.SetDragDropPayload("SCENEDETAIL_NODE", nint.Zero, 0);
            ImGui.Text($"📦 {element.Name}");
            ImGui.EndDragDropSource();
        }

        // Context menu
        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Select"))
            {
                _bridge.SelectedUIElements!.Clear();
                _bridge.SelectedUIElements.Add(element);
                _bridge.SelectedUIElement = element;
            }

            if (ImGui.MenuItem("Rename"))
            {
                _renamePreviousName = element.Name;
                _renameBuffer = element.Name;
                _showRenamePopup = true;
            }

            ImGui.Separator();

            // ── Assign Behavior submenu ──
            if (ImGui.BeginMenu("⚡ Assign Behavior"))
            {
                var behaviors = IDEBridge.AvailableBehaviors;
                string currentLabel = element.ClickBehaviorLabel;

                foreach (var bhv in behaviors)
                {
                    bool bhvSelected = string.Equals(bhv.Value, currentLabel, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.MenuItem(bhv.Label, null, bhvSelected))
                    {
                        element.ClickBehaviorLabel = bhv.Value;
                        element.OnClick = null; // Force re-map on next .ing reload
                        Console.WriteLine($"[SceneDetail] Set behavior '{bhv.Value}' on '{element.Name}'");
                    }
                }

                ImGui.EndMenu();
            }

            ImGui.Separator();

            // ── Delete Item ──
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
            bool deleted = ImGui.MenuItem("Delete Item", "Del");
            ImGui.PopStyleColor();

            if (deleted)
            {
                if (_bridge.SelectedUIElement == element)
                    _bridge.SelectedUIElement = null;
                DeleteUIElement(element);
            }

            ImGui.EndPopup();
        }

        // ── Drop target (reorder / reparent) ──
        HandleDropTarget(element);

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

    /// <summary>Where to drop an element relative to the target.</summary>
    private enum DropPosition { Before, After, AsChild }

    // ──────────────────────────────────────────────
    //  Drag & Drop — Drop Target Handling
    // ──────────────────────────────────────────────

    /// <summary>Handle drop target for a tree node element.</summary>
    private unsafe void HandleDropTarget(UIElement targetElement)
    {
        if (_dragSourceElement == null || !_isDragging)
            return;

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayload* payload = ImGui.AcceptDragDropPayload("SCENEDETAIL_NODE");
            bool hasPayload = payload != null;

            if (hasPayload)
            {
                // Don't allow dropping onto self or own descendants
                bool canDrop = _dragSourceElement != null
                    && _dragSourceElement != targetElement
                    && !IsDescendantOf(targetElement, _dragSourceElement);

                if (canDrop)
                {
                    // Determine drop position based on mouse Y relative to target rect
                    var minY = ImGui.GetItemRectMin().Y;
                    var maxY = ImGui.GetItemRectMax().Y;
                    float height = maxY - minY;
                    float mouseY = ImGui.GetMousePos().Y;

                    DropPosition dropPos;
                    if (mouseY < minY + height * 0.3f)
                        dropPos = DropPosition.Before;
                    else if (mouseY > minY + height * 0.7f)
                        dropPos = DropPosition.After;
                    else
                        dropPos = DropPosition.AsChild;

                    // ── Draw visual indicator ──
                    var drawList = ImGui.GetWindowDrawList();
                    float lineX = ImGui.GetItemRectMin().X;
                    float lineW = ImGui.GetItemRectMax().X - lineX;

                    uint lineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.8f, 1.0f, 0.9f));
                    uint glowColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.8f, 1.0f, 0.3f));

                    if (dropPos == DropPosition.Before)
                    {
                        float lineY = minY;
                        drawList.AddLine(new Vector2(lineX, lineY), new Vector2(lineX + lineW, lineY), lineColor, 2.5f);
                        drawList.AddRectFilled(
                            new Vector2(lineX, lineY - 4),
                            new Vector2(lineX + lineW, lineY + 4),
                            glowColor);
                    }
                    else if (dropPos == DropPosition.After)
                    {
                        float lineY = maxY;
                        drawList.AddLine(new Vector2(lineX, lineY), new Vector2(lineX + lineW, lineY), lineColor, 2.5f);
                        drawList.AddRectFilled(
                            new Vector2(lineX, lineY - 4),
                            new Vector2(lineX + lineW, lineY + 4),
                            glowColor);
                    }
                    else // AsChild
                    {
                        // Draw a bracket indicator for child drop
                        drawList.AddRect(
                            new Vector2(lineX, minY),
                            new Vector2(lineX + lineW, maxY),
                            lineColor, 3f, ImDrawFlags.None, 2.0f);
                    }

                    // ── Execute drop on payload delivery ──
                    // AcceptDragDropPayload returns non-null only on the frame where
                    // the mouse is released over a valid drop target. At this point,
                    // _dragSourceElement is still valid (was set on previous frames).
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && _dragSourceElement != null)
                    {
                        ExecuteMove(_dragSourceElement, targetElement, dropPos);

                        // Clear drag state after successful move
                        _dragSourceElement = null;
                        _isDragging = false;
                    }
                }
            }

            ImGui.EndDragDropTarget();
        }
    }

    /// <summary>
    /// Move an element to a new position relative to a target element.
    /// Handles cross-parent moves and same-parent reordering.
    /// </summary>
    private void ExecuteMove(UIElement source, UIElement target, DropPosition dropPos)
    {
        var rootElements = _bridge.SceneRootElements;
        if (rootElements == null) return;

        // 1. Find source and target positions (before any changes, include scene root for top-level)
        var (sourceParent, sourceIndex) = FindParentAndIndex(rootElements, source, _bridge.SceneRoot);
        if (sourceParent == null || sourceIndex < 0) return;

        var (targetParent, targetIndex) = FindParentAndIndex(rootElements, target, _bridge.SceneRoot);
        if (targetParent == null || targetIndex < 0) return;

        // 2. Remove source from old parent
        sourceParent.Children.RemoveAt(sourceIndex);

        // 3. Compute new parent and index after removal
        UIElement newParent;
        int newIndex;

        bool sameParent = sourceParent == targetParent;
        // If source was before target in the same parent, target's index shifted down by 1
        int adjustedTargetIdx = (sameParent && sourceIndex < targetIndex) ? targetIndex - 1 : targetIndex;

        switch (dropPos)
        {
            case DropPosition.Before:
                newParent = targetParent;
                newIndex = adjustedTargetIdx;
                break;
            case DropPosition.After:
                newParent = targetParent;
                newIndex = adjustedTargetIdx + 1;
                break;
            case DropPosition.AsChild:
                newParent = target;
                newIndex = target.Children.Count; // append at end
                break;
            default:
                return;
        }

        // 4. Clamp and insert
        newIndex = Math.Clamp(newIndex, 0, newParent.Children.Count);
        source.Parent = newParent;
        newParent.Children.Insert(newIndex, source);

        // 5. Record undo for move
        PushUndo(new UndoRedoAction
        {
            Type = UndoRedoAction.ActionType.Move,
            Element = source,
            Parent = sourceParent,
            ChildIndex = sourceIndex,
            NewParent = newParent,
            NewChildIndex = newIndex,
        });

        Console.WriteLine($"[SceneDetail] Moved '{source.Name}' → parent '{newParent.Name}' at index {newIndex}");

        // Select the moved element
        _bridge.SelectedUIElement = source;
    }

    /// <summary>Check if <paramref name="element"/> is a descendant of <paramref name="potentialAncestor"/>.</summary>
    private static bool IsDescendantOf(UIElement element, UIElement potentialAncestor)
    {
        var current = element.Parent;
        while (current != null)
        {
            if (current == potentialAncestor)
                return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>Depth-first collect all visible elements in display order (for Shift+Click range selection).</summary>
    private static List<UIElement> CollectVisibleElements(IReadOnlyList<UIElement>? roots)
    {
        var result = new List<UIElement>();
        if (roots == null) return result;
        foreach (var root in roots)
            CollectVisibleRecursive(root, result);
        return result;
    }

    private static void CollectVisibleRecursive(UIElement elem, List<UIElement> result)
    {
        result.Add(elem);
        foreach (var child in elem.Children)
            if (child.IsVisible)
                CollectVisibleRecursive(child, result);
    }

    // ──────────────────────────────────────────────
    //  Add New Element
    // ──────────────────────────────────────────────

    /// <summary>Add a new element as a child of the selected element, or to the first root if nothing selected.</summary>
    private void AddNewElement(string name, int typeIdx)
    {
        UIElementType elemType = typeIdx switch
        {
            0 => UIElementType.Button,
            1 => UIElementType.Label,
            2 => UIElementType.Container,
            3 => UIElementType.Dialog,
            _ => UIElementType.Button,
        };

        var newElem = new UIElement
        {
            Name = name,
            Text = name,
            Type = elemType,
            X = 100,
            Y = 100,
            Width = 200,
            Height = 50,
            IsVisible = true,
        };

        UIElement? parent;
        int childIndex;

        // Add as SIBLING of the selected element (same parent, after it)
        var selected = _bridge.SelectedUIElement;
        if (selected != null && selected != _bridge.SceneRoot)
        {
            // Sibling: same parent as selected, inserted right after it
            parent = selected.Parent ?? _bridge.SceneRoot;
            if (parent != null)
            {
                childIndex = parent.Children.IndexOf(selected) + 1;
                childIndex = Math.Clamp(childIndex, 0, parent.Children.Count);
                newElem.Parent = parent;
                parent.Children.Insert(childIndex, newElem);
                Console.WriteLine($"[SceneDetail] Added sibling '{name}' after '{selected.Name}' in '{parent.Name}'");
            }
            else
            {
                // Fallback: add to scene root
                parent = _bridge.SceneRoot;
                childIndex = parent?.Children.Count ?? 0;
                parent?.AddChild(newElem);
                Console.WriteLine($"[SceneDetail] Added '{name}' to scene root (fallback)");
            }
        }
        else if (_bridge.SceneRoot != null)
        {
            // Fallback: add directly to the invisible scene root
            parent = _bridge.SceneRoot;
            childIndex = parent.Children.Count;
            parent.AddChild(newElem);
            Console.WriteLine($"[SceneDetail] Added '{name}' to scene root '{parent.Name}'");
        }
        else
        {
            // Last fallback: add to the first root element from SceneRootElements
            var roots = _bridge.SceneRootElements;
            if (roots != null && roots.Count > 0)
            {
                parent = roots[0];
                childIndex = parent.Children.Count;
                parent.AddChild(newElem);
                Console.WriteLine($"[SceneDetail] Added '{name}' to root '{parent.Name}'");
            }
            else
            {
                // No parent at all — scene hasn't set its root yet.
                // Store the new element in a pending list; it will be re-parented
                // on the next frame when the scene's Render() sets SceneRoot.
                Console.WriteLine($"[SceneDetail] No SceneRoot available — queuing '{name}' for re-parent on next frame");
                _pendingOrphanedElements ??= [];
                _pendingOrphanedElements.Add(newElem);
                // Push undo so user can undo this operation if needed
                PushUndo(new UndoRedoAction
                {
                    Type = UndoRedoAction.ActionType.Add,
                    Element = newElem,
                    Parent = null,
                    ChildIndex = -1,
                });
                // Don't set SelectedUIElement since the element isn't in the tree yet
                return;
            }
        }

        // Record undo for add
        PushUndo(new UndoRedoAction
        {
            Type = UndoRedoAction.ActionType.Add,
            Element = newElem,
            Parent = parent,
            ChildIndex = childIndex,
        });

        // Select the new element so its details show up in the Inspector
        _bridge.SelectedUIElement = newElem;
    }

    // ──────────────────────────────────────────────
    //  Delete Element
    // ──────────────────────────────────────────────

    /// <summary>Delete the currently selected element(s). Supports multi-delete.</summary>
    private void DeleteSelectedElement()
    {
        var multi = _bridge.SelectedUIElements;
        if (multi == null || multi.Count == 0) return;

        if (multi.Count == 1)
        {
            var sel = _bridge.SelectedUIElement;
            if (sel == null) return;
            _bridge.SelectedUIElement = null;
            multi.Clear();
            DeleteUIElement(sel);
            Console.WriteLine($"[SceneDetail] Deleted element: {sel.Name}");
        }
        else
        {
            // Multi-delete: copy list, delete each, clear selection
            var toDelete = multi.ToList();
            _bridge.SelectedUIElement = null;
            multi.Clear();
            foreach (var elem in toDelete)
                DeleteUIElement(elem);
            Console.WriteLine($"[SceneDetail] Deleted {toDelete.Count} elements");
        }
    }

    /// <summary>Remove a UIElement from its parent's Children list and record undo.</summary>
    private void DeleteUIElement(UIElement element)
    {
        if (element == null) return;

        var rootElements = _bridge.SceneRootElements;
        if (rootElements != null)
        {
            // Find parent and index BEFORE removing (include scene root for top-level elements)
            var (parent, childIndex) = FindParentAndIndex(rootElements, element, _bridge.SceneRoot);
            if (parent != null && childIndex >= 0)
            {
                // Record undo for delete
                PushUndo(new UndoRedoAction
                {
                    Type = UndoRedoAction.ActionType.Delete,
                    Element = element,
                    Parent = parent,
                    ChildIndex = childIndex,
                });

                // Remove from parent
                parent.Children.RemoveAt(childIndex);
                return;
            }
        }

        Console.WriteLine($"[SceneDetail] Could not find element '{element.Name}' in hierarchy");
    }

    /// <summary>Recursively search for an element and return (parent, index).
    /// Pass sceneRoot to also find elements that are direct children of the scene root (top-level).</summary>
    private static (UIElement? parent, int index) FindParentAndIndex(IEnumerable<UIElement> parents, UIElement target, UIElement? sceneRoot = null)
    {
        // Check if target is a top-level element (direct child of the scene root)
        if (sceneRoot != null)
        {
            int topIdx = sceneRoot.Children.IndexOf(target);
            if (topIdx >= 0)
                return (sceneRoot, topIdx);
        }

        foreach (var parent in parents)
        {
            int idx = parent.Children.IndexOf(target);
            if (idx >= 0)
                return (parent, idx);

            var (grandParent, grandIdx) = FindParentAndIndex(parent.Children, target);
            if (grandParent != null)
                return (grandParent, grandIdx);
        }
        return (null, -1);
    }

    // ──────────────────────────────────────────────
    //  Undo / Redo
    // ──────────────────────────────────────────────

    /// <summary>Push an action onto the undo stack and clear the redo stack.</summary>
    private void PushUndo(UndoRedoAction action)
    {
        _undoStack.Add(action);
        if (_undoStack.Count > MaxUndoSteps)
            _undoStack.RemoveAt(0);

        // Any new action invalidates the redo history
        _redoStack.Clear();
    }

    /// <summary>Undo the most recent action.</summary>
    private void ExecuteUndo()
    {
        if (_undoStack.Count == 0) return;

        var action = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        switch (action.Type)
        {
            case UndoRedoAction.ActionType.Add:
                // Remove the added element from its parent
                if (action.Parent != null && action.Element != null)
                {
                    int idx = action.Parent.Children.IndexOf(action.Element);
                    if (idx >= 0)
                    {
                        action.Parent.Children.RemoveAt(idx);
                        Console.WriteLine($"[SceneDetail] Undo Add: removed '{action.Element.Name}'");
                    }
                    // Select the parent since the added element is gone
                    _bridge.SelectedUIElement = action.Parent;
                }
                else
                {
                    // 🛠️ FIX #11: Guard against null parent — this can happen if the
                    // element was pending re-parent (SceneRoot wasn't available when added).
                    // Just clear selection and skip removal.
                    Console.WriteLine($"[SceneDetail] Undo Add: parent was null — element '{action.Element?.Name ?? "unknown"}' may have been pending");
                    _bridge.SelectedUIElement = null;
                }
                break;

            case UndoRedoAction.ActionType.Delete:
                // Re-insert the deleted element at its original position
                if (action.Parent != null && action.Element != null)
                {
                    // 🛠️ FIX #11: Guard against ChildIndex == -1 (invalid/unset index).
                    // Use Math.Max to ensure we never attempt Insert(-1, elem).
                    int safeIdx = Math.Max(0, action.ChildIndex);
                    int insertIdx = Math.Min(safeIdx, action.Parent.Children.Count);
                    action.Element.Parent = action.Parent; // restore parent reference
                    action.Parent.Children.Insert(insertIdx, action.Element);
                    Console.WriteLine($"[SceneDetail] Undo Delete: restored '{action.Element.Name}' at index {insertIdx}");

                    // Re-select the restored element
                    _bridge.SelectedUIElement = action.Element;
                }
                else
                {
                    // 🛠️ FIX #11: Guard against null parent — this shouldn't happen for delete
                    // since the element was definitely in the tree, but be defensive.
                    Console.WriteLine($"[SceneDetail] Undo Delete: parent was null for '{action.Element?.Name ?? "unknown"}' — cannot restore");
                }
                break;

            case UndoRedoAction.ActionType.Rename:
                // Restore old name
                if (action.Element != null)
                {
                    action.Element.Name = action.OldName;
                    Console.WriteLine($"[SceneDetail] Undo Rename: '{action.NewName}' → '{action.OldName}'");
                }
                break;

            case UndoRedoAction.ActionType.Move:
                // Move element back to its original parent & index
                if (action.Element != null && action.NewParent != null && action.Parent != null)
                {
                    // Remove from new parent
                    int currentIdx = action.NewParent.Children.IndexOf(action.Element);
                    if (currentIdx >= 0)
                        action.NewParent.Children.RemoveAt(currentIdx);

                    // Restore to old parent at original index
                    int restoreIdx = Math.Min(action.ChildIndex, action.Parent.Children.Count);
                    action.Element.Parent = action.Parent;
                    action.Parent.Children.Insert(restoreIdx, action.Element);
                    Console.WriteLine($"[SceneDetail] Undo Move: '{action.Element.Name}' → restored at '{action.Parent.Name}'[{restoreIdx}]");

                    // Select the moved element
                    _bridge.SelectedUIElement = action.Element;
                }
                break;

            case UndoRedoAction.ActionType.Transform:
                // Restore element's old position/size
                if (action.Element != null)
                {
                    action.Element.X = action.OldX;
                    action.Element.Y = action.OldY;
                    action.Element.Width = action.OldW;
                    action.Element.Height = action.OldH;
                    Console.WriteLine($"[SceneDetail] Undo Transform: '{action.Element.Name}' → ({action.OldX:F0},{action.OldY:F0}) [{action.OldW:F0}×{action.OldH:F0}]");
                    _bridge.SelectedUIElement = action.Element;
                }
                break;
        }

        // Push onto redo stack for redo
        _redoStack.Add(action);
    }

    /// <summary>Redo the last undone action.</summary>
    private void ExecuteRedo()
    {
        if (_redoStack.Count == 0) return;

        var action = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        switch (action.Type)
        {
            case UndoRedoAction.ActionType.Add:
                // Re-insert the element that was removed
                if (action.Parent != null && action.Element != null)
                {
                    int insertIdx = Math.Min(action.ChildIndex, action.Parent.Children.Count);
                    action.Element.Parent = action.Parent;
                    action.Parent.Children.Insert(insertIdx, action.Element);
                    Console.WriteLine($"[SceneDetail] Redo Add: restored '{action.Element.Name}'");

                    // Re-select the restored element
                    _bridge.SelectedUIElement = action.Element;
                }
                break;

            case UndoRedoAction.ActionType.Delete:
                // Remove the element again
                if (action.Parent != null && action.Element != null)
                {
                    int idx = action.Parent.Children.IndexOf(action.Element);
                    if (idx >= 0)
                    {
                        action.Parent.Children.RemoveAt(idx);
                        Console.WriteLine($"[SceneDetail] Redo Delete: removed '{action.Element.Name}'");
                    }
                    // Clear selection since the element is gone
                    _bridge.SelectedUIElement = null;
                }
                break;

            case UndoRedoAction.ActionType.Rename:
                // Re-apply new name
                if (action.Element != null)
                {
                    action.Element.Name = action.NewName;
                    Console.WriteLine($"[SceneDetail] Redo Rename: '{action.OldName}' → '{action.NewName}'");
                }
                break;

            case UndoRedoAction.ActionType.Move:
                // Move element to the new parent & index (same as original move)
                if (action.Element != null && action.Parent != null && action.NewParent != null)
                {
                    // Remove from current parent
                    int currentIdx = action.Parent.Children.IndexOf(action.Element);
                    if (currentIdx >= 0)
                        action.Parent.Children.RemoveAt(currentIdx);

                    // Insert at new parent & index
                    int restoreIdx = Math.Min(action.NewChildIndex, action.NewParent.Children.Count);
                    action.Element.Parent = action.NewParent;
                    action.NewParent.Children.Insert(restoreIdx, action.Element);
                    Console.WriteLine($"[SceneDetail] Redo Move: '{action.Element.Name}' → moved to '{action.NewParent.Name}'[{restoreIdx}]");

                    // Select the moved element
                    _bridge.SelectedUIElement = action.Element;
                }
                break;

            case UndoRedoAction.ActionType.Transform:
                // Apply new position/size
                if (action.Element != null)
                {
                    action.Element.X = action.NewX;
                    action.Element.Y = action.NewY;
                    action.Element.Width = action.NewW;
                    action.Element.Height = action.NewH;
                    Console.WriteLine($"[SceneDetail] Redo Transform: '{action.Element.Name}' → ({action.NewX:F0},{action.NewY:F0}) [{action.NewW:F0}×{action.NewH:F0}]");
                    _bridge.SelectedUIElement = action.Element;
                }
                break;
        }

        // Push back onto undo stack
        _undoStack.Add(action);
    }

    // ──────────────────────────────────────────────
    //  Save to .ing
    // ──────────────────────────────────────────────

    /// <summary>
    /// Save the current scene's UI hierarchy.
    /// If game.ing doesn't exist yet, redirect to Save As dialog via SceneManager.
    /// Otherwise, sync data and trigger SceneManager's Save All to persist all scenes.
    /// </summary>
    private void SaveCurrentSceneHierarchy()
    {
        var rootElements = _bridge.SceneRootElements;
        if (rootElements == null || rootElements.Count == 0)
        {
            ShowSaveNotification("Nothing to save");
            return;
        }

        // Get the scene name: from active game scene, or fallback to selected editor scene
        string? sceneName = _bridge.SceneManager?.CurrentScene?.Name
            ?? _bridge.SelectedEditorScene;
        if (string.IsNullOrEmpty(sceneName))
        {
            ShowSaveNotification("No scene selected");
            return;
        }

        // Find the scene root (first Scene-type element, or use _bridge.SceneRoot)
        UIElement? sceneRoot = null;
        foreach (var elem in rootElements)
        {
            if (elem.Type == UIElementType.Scene)
            {
                sceneRoot = elem;
                break;
            }
        }
        sceneRoot ??= rootElements[0];

        // Sync EditorScenes dictionary so SceneManager has latest data before save
        if (_bridge.EditorScenes.ContainsKey(sceneName))
        {
            var existing = _bridge.EditorScenes[sceneName];
            _bridge.EditorScenes[sceneName] = existing with { Root = sceneRoot };
        }
        else
        {
            _bridge.EditorScenes[sceneName] = new IDEBridge.EditorScene(
                sceneName, IDEBridge.SceneType.MainMenu, sceneRoot);
        }

        // Save individual scene file + trigger SceneManager's Save All to persist all scenes
        try
        {
            // Save individual scene file first
            SceneAssetSerializer.EnsureScenesDirectory();
            string scenePath = SceneAssetSerializer.GetScenePath(sceneName);
            SceneAssetSerializer.SaveScene(sceneRoot, scenePath);

            // Then let SceneManager save ALL scenes to game.ing (creates file if not exists)
            _bridge.SaveAllScenes?.Invoke();

            Console.WriteLine($"[SceneDetail] Saved hierarchy '{sceneName}' (triggered SceneManager save)");
            ShowSaveNotification($"Saved '{sceneName}' hierarchy");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneDetail] Failed to save scene: {ex.Message}");
            ShowSaveNotification($"Save failed: {ex.Message}");
        }
    }

    /// <summary>Show a temporary save notification in the panel header.</summary>
    private void ShowSaveNotification(string text)
    {
        _saveNotificationText = text;
        _saveNotificationTimer = SaveNotifDuration;
    }

    // ──────────────────────────────────────────────
    //  Reload from .ing
    // ──────────────────────────────────────────────

    /// <summary>
    /// Reload the UI hierarchy from the .ing file on disk, discarding any in-memory edits.
    /// Searches the individual scene file first (scenes/{SceneName}.ing),
    /// then falls back to game.ing manifest.
    /// Uses either the active game scene or the selected editor scene.
    /// </summary>
    private void ReloadSceneHierarchy()
    {
        string? sceneName = _bridge.SceneManager?.CurrentScene?.Name
            ?? _bridge.SelectedEditorScene;
        if (string.IsNullOrEmpty(sceneName))
        {
            ShowSaveNotification("No active scene or editor scene");
            return;
        }

        try
        {
            // 1. Try loading from individual .ing file first
            string filePath = SceneAssetSerializer.GetScenePath(sceneName);
            SceneAsset? asset = SceneAssetSerializer.LoadScene(filePath);

            // 2. Fallback: search in game.ing manifest
            asset ??= SceneAssetSerializer.FindScene(sceneName);

            if (asset == null || asset.Elements.Count == 0)
            {
                ShowSaveNotification($"No .ing file found for '{sceneName}'");
                return;
            }

            // 3. Convert serialized data back to live UIElement objects
            var loadedRoots = new List<UIElement>();
            foreach (var elementData in asset.Elements)
            {
                var root = SceneAssetSerializer.ToUIElement(elementData);
                loadedRoots.Add(root);
            }

            // 4. Replace the bridge's root elements
            _bridge.SceneRootElements = loadedRoots;

            // 5. If first root is a Scene-type element, use it as SceneRoot; otherwise wrap it.
            UIElement? sceneRoot = null;
            if (loadedRoots.Count > 0)
            {
                if (loadedRoots[0].Type == UIElementType.Scene)
                {
                    sceneRoot = loadedRoots[0];
                }
                else
                {
                    // Wrap loaded root(s) in a Scene-type container
                    sceneRoot = new UIElement
                    {
                        Name = sceneName,
                        Type = UIElementType.Scene,
                        IsVisible = false,
                    };
                    foreach (var root in loadedRoots)
                        sceneRoot.AddChild(root);
                    _bridge.SceneRootElements = new List<UIElement> { sceneRoot }.AsReadOnly();
                }
                _bridge.SceneRoot = sceneRoot;
                SceneAssetSerializer.RegisterSceneRoot(sceneName, sceneRoot);
            }

            // 6. Sync EditorScenes dictionary so SceneManager has the latest data
            if (sceneRoot != null)
            {
                _bridge.EditorScenes[sceneName] = new IDEBridge.EditorScene(
                    sceneName,
                    IDEBridge.SceneType.MainMenu,
                    sceneRoot);
                _bridge.SelectedEditorScene = sceneName;
            }

            // 7. Select first child so wireframe/handles appear in the viewport
            _bridge.SelectedUIElements?.Clear();
            if (sceneRoot != null && sceneRoot.Children.Count > 0)
            {
                _bridge.SelectedUIElement = sceneRoot.Children[0];
            }
            else
            {
                _bridge.SelectedUIElement = sceneRoot;
            }
            if (_bridge.SelectedUIElement != null)
                _bridge.SelectedUIElements?.Add(_bridge.SelectedUIElement);

            _undoStack.Clear();
            _redoStack.Clear();

            Console.WriteLine($"[SceneDetail] Reloaded hierarchy for '{sceneName}' from .ing ({loadedRoots.Count} roots)");
            ShowSaveNotification($"Reloaded '{sceneName}' from .ing");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneDetail] Failed to reload scene: {ex.Message}");
            ShowSaveNotification($"Reload failed: {ex.Message}");
        }
    }
}
