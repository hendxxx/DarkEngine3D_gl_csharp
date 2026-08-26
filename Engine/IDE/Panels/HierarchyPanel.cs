using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// SceneDetail panel — displays the current scene's UI element tree.
/// Shows a tree structure like:
///   [Scene] MainMenu
///     [Btn] START GAME
///     [Btn] SETTINGS
///     [Btn] EXIT
///     [Chat] ExitConfirm
///       [Btn] CANCEL
///       [Btn] YES
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
    private int _addTypeIdx = 0; // 0=Button, 1=Label, 2=Container, 3=SliderNumber, 4=SliderText, 5=Checkbox, 6=Dropdown, 7=TextBox
    private string _renameBuffer = "";
    private string _renamePreviousName = ""; // captured before dialog opens, for undo
    private const int InputBufSize = 256;        // ── Drag & drop state ──
    private UIElement? _dragSourceElement = null;
    private bool _isDragging = false;

    // ── 3D object drag & drop state ──
    private int _dragSourceObjectIndex = -1;
    private bool _isDraggingObject = false;

    // ── 3D object selection state ──
    /// <summary>Index of the last PLAIN-clicked 3D object row — the anchor for
    /// Shift+Click range selection (selects everything between anchor and click).</summary>
    private int _last3DClickIndex = -1;

    // ── Undo / Redo ──
    private readonly List<UndoRedoAction> _undoStack = [];
    private readonly List<UndoRedoAction> _redoStack = [];
    private const int MaxUndoSteps = 50;

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

    private static readonly string[] ElementTypeLabels = ["Button", "Label", "Container", "SliderNumber", "SliderText", "Checkbox", "Dropdown", "TextBox", "—— 3D ——", "Plane", "Box", "Sphere", "Camera", "Light", "Sky"];
    private const int First3DTypeIdx = 9; // Index in ElementTypeLabels where 3D types start

    /// <summary>Recorded action for undo/redo.</summary>
    private struct UndoRedoAction
    {
        public enum ActionType { Add, Delete, Rename, Move, Transform, ColorChange, EditorTransform, EditorTransformGroup, EditorPivotChange, SkySunChange, TerrainPaint, TerrainLayerPaint }
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

        // For ColorChange (color property change from Inspector)
        public string ColorPropertyName;
        public Vector3 OldColor;
        public Vector3 NewColor;

        // For EditorTransform (3D gizmo drag on an EditorObject)
        public EditorObject? EditorObj;
        public Vector3 OldPos, OldRot, OldScale;
        public Vector3 NewPos, NewRot, NewScale;
        public Vector3? OldPivot, NewPivot;   // gizmo pivot override snapshots

        // For EditorTransformGroup (single undo covering a MULTI-select gizmo drag).
        // Parallel arrays — one entry per object that was actually moved.
        public EditorObject[]? EditorObjs;
        public Vector3[]? OldPositions, OldRotations, OldScales;
        public Vector3[]? NewPositions, NewRotations, NewScales;
        public Vector3?[]? OldPivots, NewPivots;

        // For TerrainPaint (height brush stroke on an advanced terrain plane).
        public EditorObject? TerrainObj;
        public float[]? OldHeights, NewHeights; // height snapshots before / after the stroke
        // For TerrainLayerPaint (🎨 layer brush stroke) — splat snapshots.
        public byte[]? OldSplat, NewSplat;
        // For SkySunChange (sun drag on the sky gizmo) — pitch/yaw before/after (null = time-of-day),
        // plus the Light marker whose direction follows the sun drag (null = none).
        public float? OldPitch, NewPitch, OldYaw, NewYaw;
        public EditorObject? LightObj;
        public Vector3? OldLightDir, NewLightDir;
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

        // Wire up the 3D gizmo drag-end delegate so ViewportPanel can record
        // transform undos for editor objects (uses each object's LastGizmo* snapshot
        // captured at drag start as the "old" state, current values as "new").
        // Multi-select drags record ONE grouped undo covering ALL moved objects,
        // so a single Ctrl+Z restores the entire multi-drag in one step.
        _bridge.OnGizmoDragEnded = (objs) =>
        {
            if (objs == null) return;

            // Collect only the objects that actually moved (skip interrupted/no-op drags)
            var moved = new List<EditorObject>();
            foreach (var obj in objs)
            {
                if (obj == null) continue;
                if (obj.Position == obj.LastGizmoPosition
                    && obj.RotationEuler == obj.LastGizmoRotation
                    && obj.Scale == obj.LastGizmoScale
                    && obj.GizmoPivotOverride == obj.LastGizmoPivot)
                    continue;
                moved.Add(obj);
            }
            if (moved.Count == 0) return;

            if (moved.Count == 1)
            {
                // Single-object drag — keep the classic per-object action
                var obj = moved[0];
                PushUndo(new UndoRedoAction
                {
                    Type = UndoRedoAction.ActionType.EditorTransform,
                    EditorObj = obj,
                    OldPos = obj.LastGizmoPosition,
                    OldRot = obj.LastGizmoRotation,
                    OldScale = obj.LastGizmoScale,
                    OldPivot = obj.LastGizmoPivot,
                    NewPos = obj.Position,
                    NewRot = obj.RotationEuler,
                    NewScale = obj.Scale,
                    NewPivot = obj.GizmoPivotOverride,
                });
                Console.WriteLine($"[SceneDetail] Recorded gizmo undo for '{obj.Name}'");
                return;
            }

            // Multi-select drag — ONE grouped undo action with parallel per-object
            // arrays, so a single Ctrl+Z restores the whole group in one step.
            PushUndo(new UndoRedoAction
            {
                Type = UndoRedoAction.ActionType.EditorTransformGroup,
                EditorObjs = moved.ToArray(),
                OldPositions = moved.Select(o => o.LastGizmoPosition).ToArray(),
                OldRotations = moved.Select(o => o.LastGizmoRotation).ToArray(),
                OldScales = moved.Select(o => o.LastGizmoScale).ToArray(),
                OldPivots = moved.Select(o => o.LastGizmoPivot).ToArray(),
                NewPositions = moved.Select(o => o.Position).ToArray(),
                NewRotations = moved.Select(o => o.RotationEuler).ToArray(),
                NewScales = moved.Select(o => o.Scale).ToArray(),
                NewPivots = moved.Select(o => o.GizmoPivotOverride).ToArray(),
            });
            Console.WriteLine($"[SceneDetail] Recorded grouped gizmo undo for {moved.Count} object(s) in one step");
        };

        // Wire up the gizmo pivot-placement delegate so ViewportPanel middle-click pivot
        // placement records an undo/redo (consistent with gizmo transform drags).
        // Params: (obj, oldPivotOverride, newPivotOverride) — either may be null.
        _bridge.OnGizmoPivotChanged = (obj, oldPivot, newPivot) =>
        {
            if (obj == null) return;

            // Skip if the pivot didn't actually change (e.g. clicked the same spot)
            if (oldPivot == newPivot) return;

            PushUndo(new UndoRedoAction
            {
                Type = UndoRedoAction.ActionType.EditorPivotChange,
                EditorObj = obj,
                OldPivot = oldPivot,
                NewPivot = newPivot,
            });
            Console.WriteLine($"[SceneDetail] Recorded gizmo pivot undo for '{obj.Name}'");
        };

        // Wire up the sky-sun drag delegate so ViewportPanel sun-handle drags on the sky
        // gizmo record an undo/redo (restores the pitch/yaw override, or back to time-of-day).
        // When a Light marker overrides the sky sun, its direction follows the drag — the
        // old/new light direction rides along so Ctrl+Z restores the lighting too.
        // Params: (skyObj, oldPitch, oldYaw, newPitch, newYaw, lightObj, oldLightDir, newLightDir).
        _bridge.OnSkySunChanged = (obj, oldPitch, oldYaw, newPitch, newYaw, lightObj, oldLightDir, newLightDir) =>
        {
            if (obj == null) return;
            // Skip no-op drags (grabbed and released without moving the sun).
            if (oldPitch == newPitch && oldYaw == newYaw) return;

            PushUndo(new UndoRedoAction
            {
                Type = UndoRedoAction.ActionType.SkySunChange,
                EditorObj = obj,
                OldPitch = oldPitch,
                NewPitch = newPitch,
                OldYaw = oldYaw,
                NewYaw = newYaw,
                LightObj = lightObj,
                OldLightDir = oldLightDir,
                NewLightDir = newLightDir,
            });
            Console.WriteLine($"[SceneDetail] Recorded sky sun undo for '{obj.Name}'"
                + (lightObj != null ? $" + light '{lightObj.Name}'" : ""));
        };

        // Wire up the color undo delegate so InspectorPanel can record color undos
        _bridge.RecordColorUndo = (elem, propName, oldColor, newColor) =>
        {
            PushUndo(new UndoRedoAction
            {
                Type = UndoRedoAction.ActionType.ColorChange,
                Element = elem,
                ColorPropertyName = propName,
                OldColor = oldColor,
                NewColor = newColor,
            });
        };

        // Wire up the terrain brush delegate so ViewportPanel paint strokes record an
        // undo/redo (restores the full height snapshots taken before/after the stroke).
        _bridge.OnTerrainPainted = (obj, before, after) =>
        {
            if (obj == null || before == null || after == null) return;
            if (before.Length != after.Length) return;

            // Skip no-op strokes (mouse moved but no height actually changed).
            bool same = true;
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] != after[i]) { same = false; break; }
            }
            if (same) return;

            PushUndo(new UndoRedoAction
            {
                Type = UndoRedoAction.ActionType.TerrainPaint,
                TerrainObj = obj,
                OldHeights = before,
                NewHeights = after,
            });
            Console.WriteLine($"[SceneDetail] Recorded terrain paint undo for '{obj.Name}' ({before.Length} heights)");
        };

        // Wire up the terrain LAYER paint delegate so 🎨 brush strokes record an undo/redo
        // (restores the full splat snapshots taken before/after the stroke).
        _bridge.OnTerrainLayerPainted = (obj, before, after) =>
        {
            if (obj == null || before == null || after == null) return;
            if (before.Length != after.Length) return;

            // Skip no-op strokes (painted with the eraser on a clean area, etc.).
            bool same = true;
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] != after[i]) { same = false; break; }
            }
            if (same) return;

            PushUndo(new UndoRedoAction
            {
                Type = UndoRedoAction.ActionType.TerrainLayerPaint,
                TerrainObj = obj,
                OldSplat = before,
                NewSplat = after,
            });
            Console.WriteLine($"[SceneDetail] Recorded terrain layer-paint undo for '{obj.Name}' ({before.Length} splat bytes)");
        };
    }

    public void ShowInMenu() => ImGui.MenuItem("SceneDetail", null, ref _visible);

    // ── Public API for main menu bar integration ──
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public bool HasSelection => _bridge.SelectedUIElement != null || _bridge.SelectedEditorObjects.Count > 0;
    public void Undo() => ExecuteUndo();
    public void Redo() => ExecuteRedo();
    public void Duplicate() => DuplicateAllSelected();
    public void DeleteSelection() => DeleteSelectedElement();

    // ── Clipboard for Copy/Paste ──
    private UIElement? _clipboardElement = null;

    /// <summary>Copy the primary selected element (deep clone) into clipboard.</summary>
    public void CopySelection()
    {
        var sel = _bridge.SelectedUIElement;
        if (sel == null) return;
        _clipboardElement = sel.DeepClone();
        Console.WriteLine($"[SceneDetail] Copied '{sel.Name}' to clipboard");
    }

    /// <summary>Cut the primary selected element: copy to clipboard then delete it (Ctrl+X).</summary>
    public void CutSelection()
    {
        CopySelection();
        DeleteSelection();
    }

    /// <summary>Paste the clipboard element as a sibling after the primary selection.
    /// If nothing is selected, append to the scene root.
    /// If clipboard is empty or pasted element name collides, generates unique name.</summary>
    public void PasteClipboard()
    {
        if (_clipboardElement == null) return;

        var rootElements = _bridge.SceneRootElements;
        if (rootElements == null) return;

        var clone = _clipboardElement.DeepClone();

        UIElement? parent;
        int insertIdx;

        var sel = _bridge.SelectedUIElement;
        if (sel != null && sel != _bridge.SceneRoot)
        {
            var (foundParent, _) = FindParentAndIndex(rootElements, sel, _bridge.SceneRoot);
            parent = foundParent ?? _bridge.SceneRoot;
            if (parent != null)
            {
                insertIdx = parent.Children.IndexOf(sel) + 1;
                insertIdx = Math.Clamp(insertIdx, 0, parent.Children.Count);
                // Generate unique name among siblings
                clone.Name = GetDuplicateName(clone, parent);
                clone.Parent = parent;
                parent.Children.Insert(insertIdx, clone);
            }
            else
            {
                parent = _bridge.SceneRoot;
                insertIdx = parent?.Children.Count ?? 0;
                parent?.AddChild(clone);
            }
        }
        else if (_bridge.SceneRoot != null)
        {
            parent = _bridge.SceneRoot;
            clone.Name = GetDuplicateName(clone, parent);
            insertIdx = parent.Children.Count;
            parent.AddChild(clone);
        }
        else
        {
            Console.WriteLine("[SceneDetail] Paste: no scene root available");
            return;
        }

        // Record undo
        PushUndo(new UndoRedoAction
        {
            Type = UndoRedoAction.ActionType.Add,
            Element = clone,
            Parent = parent,
            ChildIndex = insertIdx,
        });

        // Select the pasted element
        _bridge.SelectedUIElement = clone;
        _bridge.SelectedUIElements?.Clear();
        _bridge.SelectedUIElements?.Add(clone);

        Console.WriteLine($"[SceneDetail] Pasted '{clone.Name}' at '{parent.Name}'[{insertIdx}]");
    }

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
                _dragSourceObjectIndex = -1;
                _isDraggingObject = false;
            }
        }

        ImGui.Begin("SceneDetail", ref _visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var rootElements = _bridge.SceneRootElements;
        bool hasUIElementSelection = _bridge.SelectedUIElement != null;
        bool has3DSelection = _bridge.SelectedEditorObjects.Count > 0;
        bool hasSelection = hasUIElementSelection || has3DSelection;
        int multiCount = _bridge.SelectedUIElements?.Count > 1 ? _bridge.SelectedUIElements.Count : 0;
        int multi3DCount = _bridge.SelectedEditorObjects.Count > 1 ? _bridge.SelectedEditorObjects.Count : 0;
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
                _addNameBuffer = _addTypeIdx switch
                {
                0 => "btn", 1 => "lb", 2 => "cont", 3 => "sld", 4 => "sldtxt",
                5 => "chk", 6 => "drp", 7 => "txt",
                9 => "plane", 10 => "box", 11 => "sphere", 12 => "camera", 13 => "light", 14 => "sky",
                _ => "element",
            };
                _addTypeIdx = 0;
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Add a new UI element or 3D object to the scene");

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
            string delLabel = multiCount > 0 ? $"Del ({multiCount})"
                : multi3DCount > 0 ? $"Del 3D ({multi3DCount})"
                : (has3DSelection ? "Del 3D" : "Del");
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
                ImGui.SetTooltip(multiCount > 0 ? $"Delete {multiCount + 1} selected elements"
                    : multi3DCount > 0 ? $"Delete {multi3DCount} selected 3D objects"
                    : (has3DSelection ? "Delete selected 3D object" : "Delete the selected element"));
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

        // ── Toolbar Row 3: Quick-add 3D primitives (Box, Sphere, Plane) ──
        if (_bridge.EditorObjectManager != null)
        {
            float btnWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;

            // Box button (reddish)
            var colBox = new Vector4(0.55f, 0.25f, 0.25f, 1f);
            var colBoxHov = new Vector4(0.75f, 0.35f, 0.35f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Button, colBox);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colBoxHov);
            if (ImGui.Button("▣ Box", new Vector2(btnWidth, 24)))
            {
                QuickAdd3D(EditorPrimitiveType.Box);
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a Box primitive");

            ImGui.SameLine();

            // Sphere button (bluish)
            var colSphere = new Vector4(0.25f, 0.30f, 0.65f, 1f);
            var colSphereHov = new Vector4(0.35f, 0.45f, 0.85f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Button, colSphere);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colSphereHov);
            if (ImGui.Button("◉ Sphere", new Vector2(btnWidth, 24)))
            {
                QuickAdd3D(EditorPrimitiveType.Sphere);
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a Sphere primitive");

            ImGui.SameLine();

            // Plane button (greenish)
            var colPlane = new Vector4(0.25f, 0.55f, 0.25f, 1f);
            var colPlaneHov = new Vector4(0.35f, 0.75f, 0.35f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Button, colPlane);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colPlaneHov);
            if (ImGui.Button("▭ Plane", new Vector2(btnWidth, 24)))
            {
                QuickAdd3D(EditorPrimitiveType.Plane);
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a Plane primitive");

            ImGui.Separator();

            // ── Toolbar Row 4: Camera / Light / Sky scene elements ──
            {
                float btnWidth2 = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;

                // Camera button (teal)
                var colCam = new Vector4(0.20f, 0.55f, 0.60f, 1f);
                var colCamHov = new Vector4(0.30f, 0.70f, 0.75f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, colCam);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colCamHov);
                if (ImGui.Button("📷 Camera", new Vector2(btnWidth2, 24)))
                {
                    QuickAdd3D(EditorPrimitiveType.Camera);
                }
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a Camera marker (eye height)");

                ImGui.SameLine();

                // Light button (amber)
                var colLgt = new Vector4(0.60f, 0.50f, 0.20f, 1f);
                var colLgtHov = new Vector4(0.75f, 0.62f, 0.28f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, colLgt);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colLgtHov);
                if (ImGui.Button("☀ Light", new Vector2(btnWidth2, 24)))
                {
                    QuickAdd3D(EditorPrimitiveType.Light);
                }
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a Light marker (overrides sun color/direction)");

                ImGui.SameLine();

                // Sky button (blue)
                var colSky = new Vector4(0.25f, 0.40f, 0.65f, 1f);
                var colSkyHov = new Vector4(0.35f, 0.55f, 0.80f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, colSky);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colSkyHov);
                if (ImGui.Button("☁ Sky", new Vector2(btnWidth2, 24)))
                {
                    QuickAdd3D(EditorPrimitiveType.Sky);
                }
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a Sky marker (renders procedural skybox)");

                ImGui.Separator();
            }
        }

        // ── Keyboard shortcuts (Ctrl+Z / Ctrl+Y / Ctrl+D) ──
        // Uses IsKeyReleased to prevent repeated triggering when key is held down.
        // Must be checked outside any disabled block so they always work.
        {
            bool ctrlHeld = ImGui.GetIO().KeyCtrl;

            // Ctrl+Z: Undo (on key release — prevents looping)
            if (ctrlHeld && ImGui.IsKeyReleased(ImGuiKey.Z) && canUndo)
            {
                ExecuteUndo();
            }

            // Ctrl+Y: Redo (on key release)
            if (ctrlHeld && ImGui.IsKeyReleased(ImGuiKey.Y) && canRedo)
            {
                ExecuteRedo();
            }

            // Ctrl+D: Duplicate selected element(s) — multi-select (on key release)
            if (ctrlHeld && ImGui.IsKeyReleased(ImGuiKey.D) && hasSelection)
            {
                DuplicateAllSelected();
            }
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

        // ── Render hierarchy tree (Scene root node wraps both UI elements and 3D objects) ──
        var editorMgr = _bridge.EditorObjectManager;
        bool hasEditorObjects = editorMgr != null && editorMgr.Count > 0;

        if (!hasRoots && !hasEditorObjects)
        {
            ImGui.TextColored(ColDim, "No UI elements");
            ImGui.TextDisabled("Use + Add to create elements");
            if (_bridge.EditorScenes.Count == 0)
            {
                ImGui.TextColored(ColWarnDim, "No scenes exist. Use Scene Manager panel to create one.");
            }
        }
        else if (_bridge.SceneRoot != null)
        {
            // ── Render the Scene node as a tree root ──
            string sceneName = _bridge.SceneRoot.Name;
            ImGuiTreeNodeFlags sceneFlags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.DefaultOpen;
            bool sceneNodeOpen = ImGui.TreeNodeEx(sceneName, sceneFlags);

            if (ImGui.IsItemClicked())
            {
                // Select the scene root element and clear any 3D selection
                _bridge.SelectedUIElement = _bridge.SceneRoot;
                _bridge.SelectedEditorObject = null;
                _bridge.SelectedObject = null;
                _bridge.SelectedAgent = null;
            }

            if (sceneNodeOpen)
            {
                // ── Render UI children (actual children of SceneRoot) ──
                if (_bridge.SceneRoot.Children.Count > 0)
                {
                    foreach (var child in _bridge.SceneRoot.Children.ToArray())
                    {
                        RenderTreeNode(child);
                    }
                }

                // ── Render 3D editor objects as children of Scene node ──
                if (hasEditorObjects)
                {
                    var objects = editorMgr!.Objects;

                    for (int i = 0; i < objects.Count; i++)
                    {
                        var obj = objects[i];
                        bool isSelected = _bridge.SelectedEditorObjects.Contains(obj);
                        bool isPrimary = _bridge.SelectedEditorObject == obj;

                        string icon = obj.PrimitiveType switch
                        {
                            EditorPrimitiveType.Plane => "▭",
                            EditorPrimitiveType.Box => "▣",
                            EditorPrimitiveType.Sphere => "◉",
                            EditorPrimitiveType.GlbReference => "◈",
                            EditorPrimitiveType.Camera => "📷",
                            EditorPrimitiveType.Light => "☀",
                            EditorPrimitiveType.Sky => "☁",
                            _ => "◇",
                        };

                        string label = $"{icon} {obj.Name}";

                        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth;
                        if (isSelected)
                            flags |= ImGuiTreeNodeFlags.Selected;

                        ImGui.TreeNodeEx(label, flags);

                        // ── Multi-select highlight for non-primary members ──
                        if (isSelected && !isPrimary)
                        {
                            var dl = ImGui.GetWindowDrawList();
                            var min = ImGui.GetItemRectMin();
                            var max = ImGui.GetItemRectMax();
                            dl.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.5f, 0.8f, 0.18f)));
                        }

                        if (ImGui.IsItemClicked())
                        {
                            Handle3DObjectClick(i, obj);
                        }

                        // ── Drag source for 3D object ──
                        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
                        {
                            _dragSourceObjectIndex = i;
                            _isDraggingObject = true;
                            ImGui.SetDragDropPayload("SCENEDETAIL_3DOBJ", nint.Zero, 0);
                            ImGui.Text($"{icon} {obj.Name}");
                            ImGui.EndDragDropSource();
                        }

                        // Context menu
                        if (ImGui.BeginPopupContextItem())
                        {
                            if (ImGui.MenuItem("Rename"))
                            {
                                _renamePreviousName = obj.Name;
                                _renameBuffer = obj.Name;
                                _showRenamePopup = true;
                            }
                            ImGui.Separator();
                            if (ImGui.MenuItem("Delete"))
                            {
                                // If right-clicked object is part of a multi-selection, delete ALL selected;
                                // otherwise delete just this object.
                                if (_bridge.SelectedEditorObjects.Contains(obj))
                                {
                                    DeleteSelectedElement();
                                }
                                else
                                {
                                    _bridge.DeselectEditorObject(obj);
                                    editorMgr.Remove(obj);
                                    Console.WriteLine($"[SceneDetail] Deleted editor object: {obj.Name}");
                                }
                            }
                            ImGui.EndPopup();
                        }

                        // ── Drop target for 3D objects (reorder) ──
                        Handle3DDropTarget(i);
                    }
                }

                ImGui.TreePop();
            }
        }
        else
        {
            // Fallback: no SceneRoot, render root elements directly
            if (hasRoots)
            {
                foreach (var root in rootElements)
                {
                    RenderTreeNode(root);
                }
            }

            // Render 3D objects at root level as fallback
            if (hasEditorObjects)
            {
                var objects = editorMgr!.Objects;
                for (int i = 0; i < objects.Count; i++)
                {
                    var obj = objects[i];
                    bool isSelected = _bridge.SelectedEditorObjects.Contains(obj);
                    bool isPrimary = _bridge.SelectedEditorObject == obj;
                    string icon = obj.PrimitiveType switch
                    {
                        EditorPrimitiveType.Plane => "▭",
                        EditorPrimitiveType.Box => "▣",
                        EditorPrimitiveType.Sphere => "◉",
                        EditorPrimitiveType.GlbReference => "◈",
                        EditorPrimitiveType.Camera => "📷",
                        EditorPrimitiveType.Light => "☀",
                        EditorPrimitiveType.Sky => "☁",
                        _ => "◇",
                    };
                    ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth;
                    if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;
                    ImGui.TreeNodeEx($"{icon} {obj.Name}", flags);

                    // ── Multi-select highlight for non-primary members ──
                    if (isSelected && !isPrimary)
                    {
                        var dl = ImGui.GetWindowDrawList();
                        var min = ImGui.GetItemRectMin();
                        var max = ImGui.GetItemRectMax();
                        dl.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.5f, 0.8f, 0.18f)));
                    }

                    if (ImGui.IsItemClicked())
                    {
                        Handle3DObjectClick(i, obj);
                    }
                    if (ImGui.BeginPopupContextItem())
                    {
                        if (ImGui.MenuItem("Delete"))
                        {
                            if (_bridge.SelectedEditorObjects.Contains(obj))
                            {
                                DeleteSelectedElement();
                            }
                            else
                            {
                                _bridge.DeselectEditorObject(obj);
                                editorMgr.Remove(obj);
                            }
                        }
                        ImGui.EndPopup();
                    }
                    Handle3DDropTarget(i);
                }
            }
        }

        ImGui.End();


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
            // Use a custom combo that handles the separator
            if (ImGui.BeginCombo("##add_type", _addTypeIdx >= First3DTypeIdx ? ElementTypeLabels[_addTypeIdx] : ElementTypeLabels[_addTypeIdx]))
            {
                for (int ti = 0; ti < ElementTypeLabels.Length; ti++)
                {
                    string label = ElementTypeLabels[ti];
                    bool isSeparator = label.StartsWith("—");
                    
                    if (isSeparator)
                    {
                        ImGui.Separator();
                        ImGui.TextColored(ColDim, label);
                        continue;
                    }
                    
                    bool isSel = (ti == _addTypeIdx);
                    if (ImGui.Selectable(label, isSel))
                    {
                        int prevTypeIdx = _addTypeIdx;
                        _addTypeIdx = ti;
                        
                        // If name still matches the old default prefix, update it to new type's prefix
                        string oldDefault = prevTypeIdx switch
                        {
                            0 => "btn", 1 => "lb", 2 => "cont", 3 => "sld", 4 => "sldtxt",
                            5 => "chk", 6 => "drp", 7 => "txt",
                            9 => "plane", 10 => "box", 11 => "sphere",
                            _ => "element",
                        };
                        if (_addNameBuffer == oldDefault)
                        {
                            _addNameBuffer = ti switch
                            {
                                0 => "btn", 1 => "lb", 2 => "cont", 3 => "sld", 4 => "sldtxt",
                                5 => "chk", 6 => "drp", 7 => "txt",
                                9 => "plane", 10 => "box", 11 => "sphere", 12 => "camera", 13 => "light", 14 => "sky",
                                _ => "element",
                            };
                        }
                    }
                    if (isSel)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

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

// Show ALL children in the tree regardless of IsVisible, so hidden elements
        // (like ExitConfirm dialog children) can still be selected and edited.
        bool hasChildren = element.Children.Count > 0;

        // For hidden elements, dim the icon to indicate they won't render in viewport
        string icon = element.IsVisible ? element.GetIcon() : "○";
        string label = $"{icon} {element.Name}";

        bool isSelected = _bridge.SelectedUIElement == element;
        bool isInMulti = _bridge.SelectedUIElements?.Contains(element) == true;

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanFullWidth;
        if (!hasChildren)
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

            // After any click on a UI element, clear 3D selection so the Inspector
            // shows UI element properties (or scene properties for Scene-type elements).
            // This covers Ctrl+Click, Shift+Click, and Normal Click cases.
            if (_bridge.SelectedUIElement != null)
            {
                _bridge.SelectedEditorObject = null;
                _bridge.SelectedObject = null;
                _bridge.SelectedAgent = null;
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
            ImGui.Text($"{element.GetIcon()} {element.Name}");
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
                var (currentType, _) = IDEBridge.ParseBehavior(element.ClickBehaviorLabel);

                foreach (var bhv in behaviors)
                {
                    bool bhvSelected = string.Equals(bhv.Value, currentType, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.MenuItem(bhv.Label, null, bhvSelected))
                    {
                        element.ClickBehaviorLabel = bhv.Value; // Just set the type, no sub-params
                        element.OnClick = null;
                        Console.WriteLine($"[SceneDetail] Set behavior '{bhv.Value}' on '{element.Name}'");
                    }
                }

                ImGui.EndMenu();
            }

            ImGui.Separator();

            // ── Duplicate Item ──
            if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
            {
                DuplicateSelectedElement(element);
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

        // Recursively render ALL children — including hidden ones (like ExitConfirm dialog buttons)
        // so they appear in the hierarchy tree for selection and editing.
        // IMPORTANT: snapshot to array before iterating — drag-drop reorder (ExecuteMove)
        // can modify element.Children during recursive RenderTreeNode calls, which would
        // throw "Collection was modified; enumeration operation may not execute."
        if (hasChildren && nodeOpen)
        {
            foreach (var child in element.Children.ToArray())
                RenderTreeNode(child);
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

        // 1. Find source and target positions (before any changes)
        var (sourceParent, sourceIndex) = FindParentAndIndex(rootElements, source, _bridge.SceneRoot);
        if (sourceParent == null || sourceIndex < 0 || sourceIndex >= sourceParent.Children.Count)
        {
            Console.WriteLine($"[SceneDetail] ExecuteMove FAIL: source '{source.Name}' not found (parent={sourceParent?.Name}, idx={sourceIndex}, count={sourceParent?.Children.Count})");
            return;
        }

        var (targetParent, targetIndex) = FindParentAndIndex(rootElements, target, _bridge.SceneRoot);

        // For Before/After placement, we need the target's parent.
        // For AsChild placement, the target itself becomes the parent (root elements are valid).
        if (dropPos != DropPosition.AsChild && (targetParent == null || targetIndex < 0))
        {
            Console.WriteLine($"[SceneDetail] ExecuteMove FAIL: target '{target.Name}' parent not found");
            return;
        }

        // 2. Remove source from old parent
        sourceParent.Children.RemoveAt(sourceIndex);

        UIElement newParent;
        int newIndex;

        try
        {
            // 3. Compute new parent and index after removal
            bool sameParent = sourceParent == targetParent;
            int adjustedTargetIdx = (sameParent && sourceIndex < targetIndex) ? targetIndex - 1 : targetIndex;

            switch (dropPos)
            {
                case DropPosition.Before:
                    newParent = targetParent!;
                    newIndex = adjustedTargetIdx;
                    break;
                case DropPosition.After:
                    newParent = targetParent!;
                    newIndex = adjustedTargetIdx + 1;
                    break;
                case DropPosition.AsChild:
                    newParent = target;
                    newIndex = target.Children.Count;
                    break;
                default:
                    // Restore source before returning
                    source.Parent = sourceParent;
                    sourceParent.Children.Insert(Math.Min(sourceIndex, sourceParent.Children.Count), source);
                    return;
            }

            // 4. Clamp and insert
            newIndex = Math.Clamp(newIndex, 0, newParent.Children.Count);
            source.Parent = newParent;
            newParent.Children.Insert(newIndex, source);
        }
        catch (Exception ex)
        {
            // Restore source to original position to avoid orphaned element
            Console.WriteLine($"[SceneDetail] ExecuteMove CRASH: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[SceneDetail]   source='{source?.Name}' target='{target?.Name}' dropPos={dropPos}");
            source.Parent = sourceParent;
            sourceParent.Children.Insert(Math.Min(sourceIndex, sourceParent.Children.Count), source);
            return;
        }

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

    /// <summary>Set a color property on a UIElement by property name.</summary>
    private static void SetColorProperty(UIElement elem, string propName, Vector3 color)
    {
        switch (propName)
        {
            case "TextColor": elem.TextColor = color; break;
            case "BgColor": elem.BgColor = color; break;
            case "BorderColor": elem.BorderColor = color; break;
            case "HoverTextColor": elem.HoverTextColor = color; break;
            case "HoverBgColor": elem.HoverBgColor = color; break;
            case "HoverBorderColor": elem.HoverBorderColor = color; break;
        }
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
    //  Quick-Add 3D Primitive
    // ──────────────────────────────────────────────

    /// <summary>Quick-add a 3D primitive with incremental naming, placed at camera position.</summary>
    private void QuickAdd3D(EditorPrimitiveType primType)
    {
        var editorMgr = _bridge.EditorObjectManager;
        if (editorMgr == null)
        {
            Console.WriteLine("[SceneDetail] Cannot add 3D object: EditorObjectManager not available");
            ShowSaveNotification("No EditorObjectManager");
            return;
        }

        Vector3 spawnPos = IDEBridge.GetGridSpawnPosition(_bridge.Camera, primType);

        var obj = editorMgr.AddPrimitive(primType, spawnPos);

        // Sky automatically drives a DIRECT light — reuse or create one (bug #7).
        if (primType == EditorPrimitiveType.Sky)
            editorMgr.EnsureDirectLightForSky(obj);

        _bridge.SelectEditorObject(obj);
        _bridge.SelectedUIElement = null;
        _bridge.SelectedUIElements.Clear();
        _bridge.SelectedObject = null;
        _bridge.SelectedAgent = null;
        Console.WriteLine($"[SceneDetail] Quick-added 3D {primType}: '{obj.Name}' at {spawnPos}");
    }

    // ──────────────────────────────────────────────
    //  3D Object Drag & Drop — Drop Target Handling
    // ──────────────────────────────────────────────

    /// <summary>Handle drop target for reordering 3D objects in the tree.</summary>
    private unsafe void Handle3DDropTarget(int targetIndex)
    {
        if (_dragSourceObjectIndex < 0 || !_isDraggingObject)
            return;

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayload* payload = ImGui.AcceptDragDropPayload("SCENEDETAIL_3DOBJ");
            bool hasPayload = payload != null;

            if (hasPayload)
            {
                var editorMgr = _bridge.EditorObjectManager;
                if (editorMgr == null) { ImGui.EndDragDropTarget(); return; }

                bool canDrop = _dragSourceObjectIndex >= 0
                    && _dragSourceObjectIndex < editorMgr.Count
                    && targetIndex >= 0 && targetIndex < editorMgr.Count
                    && _dragSourceObjectIndex != targetIndex;

                if (canDrop)
                {
                    // Draw visual indicator
                    var drawList = ImGui.GetWindowDrawList();
                    float lineX = ImGui.GetItemRectMin().X;
                    float lineW = ImGui.GetItemRectMax().X - lineX;
                    float lineY = ImGui.GetItemRectMax().Y;
                    uint lineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.8f, 1.0f, 0.9f));
                    drawList.AddLine(new Vector2(lineX, lineY), new Vector2(lineX + lineW, lineY), lineColor, 2.5f);

                    // Execute reorder on drop
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        int srcIdx = _dragSourceObjectIndex;
                        editorMgr.MoveObject(srcIdx, targetIndex);
                        Console.WriteLine($"[SceneDetail] Reordered 3D object from index {srcIdx} to {targetIndex}");

                        // Clear drag state
                        _dragSourceObjectIndex = -1;
                        _isDraggingObject = false;
                    }
                }
            }

            ImGui.EndDragDropTarget();
        }
    }

    // ──────────────────────────────────────────────
    //  Add New Element
    // ──────────────────────────────────────────────

    /// <summary>Get a unique name by appending an incrementing number suffix if needed.</summary>
    private string GetUniqueName(string baseName)
    {
        // Check 3D objects
        var mgr = _bridge.EditorObjectManager;
        if (mgr != null)
        {
            foreach (var obj in mgr.Objects)
            {
                if (obj != null && obj.Name == baseName)
                {
                    // Found duplicate — try baseName1, baseName2, etc.
                    for (int i = 1; ; i++)
                    {
                        string candidate = baseName + i;
                        bool exists = false;
                        foreach (var o2 in mgr.Objects)
                        {
                            if (o2 != null && o2.Name == candidate) { exists = true; break; }
                        }
                        if (!exists) return candidate;
                    }
                }
            }
        }
        // Check UI elements (recursive)
        if (_bridge.SceneRoot != null && HasNameConflict(_bridge.SceneRoot, baseName))
        {
            for (int i = 1; ; i++)
            {
                string candidate = baseName + i;
                if (!HasNameConflict(_bridge.SceneRoot, candidate)) return candidate;
            }
        }
        return baseName;
    }

    private static bool HasNameConflict(UIElement root, string name)
    {
        foreach (var child in root.Children)
        {
            if (child.Name == name) return true;
            if (child.Children.Count > 0 && HasNameConflict(child, name)) return true;
        }
        return false;
    }

    /// <summary>Add a new element or 3D object. UI elements are added to the hierarchy;
    /// 3D objects (Plane, Box, Sphere) are created via EditorObjectManager.</summary>
    private void AddNewElement(string name, int typeIdx)
    {
        // ── Handle 3D object creation ──
        if (typeIdx >= First3DTypeIdx)
        {
            var editorMgr = _bridge.EditorObjectManager;
            if (editorMgr == null)
            {
                Console.WriteLine("[SceneDetail] Cannot add 3D object: EditorObjectManager not available");
                ShowSaveNotification("No EditorObjectManager");
                return;
            }

            EditorPrimitiveType primType = typeIdx switch
            {
                9 => EditorPrimitiveType.Plane,
                10 => EditorPrimitiveType.Box,
                11 => EditorPrimitiveType.Sphere,
                12 => EditorPrimitiveType.Camera,
                13 => EditorPrimitiveType.Light,
                14 => EditorPrimitiveType.Sky,
                _ => EditorPrimitiveType.Box,
            };

            Vector3 spawnPos = IDEBridge.GetGridSpawnPosition(_bridge.Camera, primType);

            var obj = editorMgr.AddPrimitive(primType, spawnPos);
            obj.Name = GetUniqueName(name);
            _bridge.SelectEditorObject(obj);
            _bridge.SelectedUIElement = null;
            _bridge.SelectedUIElements.Clear();
            _bridge.SelectedObject = null;
            _bridge.SelectedAgent = null;
            Console.WriteLine($"[SceneDetail] Created 3D {primType}: '{name}' at {spawnPos}");
            return;
        }

        UIElementType elemType = typeIdx switch
        {
            0 => UIElementType.Button,
            1 => UIElementType.Label,
            2 => UIElementType.Container,
            3 => UIElementType.SliderNumber,
            4 => UIElementType.SliderText,
            5 => UIElementType.Checkbox,
            6 => UIElementType.Dropdown,
            7 => UIElementType.TextBox,
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

        // ── Defaults per type ──
        // Label: transparent background, no hover
        // Container: no hover
        // Button: default (hover on)
        // SliderNumber, SliderText, Checkbox, Dropdown, TextBox: no hover, sensible sizes
        if (elemType == UIElementType.Label)
        {
            newElem.UseHover = false;
            newElem.BgColor = new Vector3(0f, 0f, 0f) * 0f; // fully transparent
            newElem.BorderColor = new Vector3(0f, 0f, 0f) * 0f;
            newElem.HoverBgColor = newElem.BgColor;
            newElem.HoverBorderColor = newElem.BorderColor;
            newElem.HoverTextColor = newElem.TextColor;
        }
        else if (elemType == UIElementType.Container)
        {
            newElem.UseHover = false;
            newElem.Opacity = 1.0f; // fully opaque (0% transparent)
            newElem.BorderColor = newElem.BgColor; // border matches background (invisible)
        }
        else if (elemType == UIElementType.SliderNumber)
        {
            newElem.UseHover = false;
            newElem.Width = 300;
            newElem.Height = 40;
            newElem.MinValue = 0;
            newElem.MaxValue = 100;
            newElem.Step = 1;
            newElem.CurrentValue = 50;
        }
        else if (elemType == UIElementType.SliderText)
        {
            newElem.UseHover = false;
            newElem.Width = 300;
            newElem.Height = 40;
            newElem.TextOptions = ["Option A", "Option B", "Option C"];
            newElem.SelectedTextIndex = 0;
        }
        else if (elemType == UIElementType.Checkbox)
        {
            newElem.UseHover = false;
            newElem.Width = 200;
            newElem.Height = 36;
            newElem.IsChecked = false;
        }
        else if (elemType == UIElementType.Dropdown)
        {
            newElem.UseHover = false;
            newElem.Width = 250;
            newElem.Height = 40;
            newElem.Options = ["Option 1", "Option 2", "Option 3"];
            newElem.SelectedIndex = 0;
        }
        else if (elemType == UIElementType.TextBox)
        {
            newElem.UseHover = false;
            newElem.Width = 300;
            newElem.Height = 40;
            newElem.Placeholder = "Enter text...";
            newElem.InputText = "";
            newElem.MaxLength = 0;
        }

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
                // Cannot add element without a valid parent. Log and skip.
                Console.WriteLine($"[SceneDetail] No SceneRoot available — cannot add '{name}'");
                ShowSaveNotification("No scene root available");
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
    //  3D Object Click Selection (plain / Ctrl / Shift)
    // ──────────────────────────────────────────────

    /// <summary>Handle a click on a 3D object row in the hierarchy tree:
    /// - Plain click → select just this object (and set it as the Shift anchor).
    /// - Ctrl+Click  → toggle this object in the multi-selection (anchor unchanged).
    /// - Shift+Click → range-select from the last plain-clicked anchor to this row
    ///   (standard file-explorer behaviour). Shift+Ctrl+Click keeps the existing
    ///   selection and ADD-adds the whole range to it.</summary>
    private void Handle3DObjectClick(int index, EditorObject obj)
    {
        bool ctrlHeld = ImGui.GetIO().KeyCtrl;
        bool shiftHeld = ImGui.GetIO().KeyShift;
        var objects = _bridge.EditorObjectManager?.Objects;

        if (shiftHeld && _last3DClickIndex >= 0 && objects != null)
        {
            // Range select from anchor to clicked index (inclusive), ordered
            int a = Math.Min(_last3DClickIndex, index);
            int b = Math.Max(_last3DClickIndex, index);
            a = Math.Clamp(a, 0, objects.Count - 1);
            b = Math.Clamp(b, 0, objects.Count - 1);

            if (!ctrlHeld)
                _bridge.SelectEditorObject(null); // replace selection with the range
            for (int i = a; i <= b; i++)
                _bridge.SelectEditorObject(objects[i], additive: true);
            // The clicked object becomes the primary (drives the gizmo + Inspector)
            _bridge.SelectEditorObject(obj, additive: true);
            _last3DClickIndex = index;
        }
        else if (ctrlHeld)
        {
            _bridge.ToggleEditorObjectSelection(obj);
            // Ctrl+click without shift does not move the range anchor
        }
        else
        {
            _bridge.SelectEditorObject(obj);
            _last3DClickIndex = index;
        }

        _bridge.SelectedUIElement = null;
        _bridge.SelectedUIElements.Clear();
        _bridge.SelectedObject = null;
        _bridge.SelectedAgent = null;
    }

    // ──────────────────────────────────────────────
    //  Delete Element
    // ──────────────────────────────────────────────

    /// <summary>Delete the currently selected element(s). Supports multi-delete (UI + 3D objects).</summary>
    private void DeleteSelectedElement()
    {
        // ── Handle 3D object deletion first (deletes ALL selected) ──
        var editorObjs = _bridge.SelectedEditorObjects;
        if (editorObjs != null && editorObjs.Count > 0)
        {
            var toDelete = editorObjs.ToArray();
            foreach (var obj in toDelete)
                _bridge.EditorObjectManager?.Remove(obj);
            _bridge.SelectEditorObject(null);
            Console.WriteLine($"[SceneDetail] Deleted {toDelete.Length} 3D object(s)");
            return;
        }

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

    // ──────────────────────────────────────────────
    //  Duplicate Element
    // ──────────────────────────────────────────────

    /// <summary>Generate a unique duplicate name by appending "Copy N" suffix.
    /// Checks existing siblings to avoid duplicates like "Copy 1, Copy 2".</summary>
    private static string GetDuplicateName(UIElement original, UIElement parent)
    {
        string baseName = original.Name;
        // If original already ends with "Copy N", strip it and use the base name
        // Pattern: "something Copy 1", "something Copy 2", etc.
        var match = System.Text.RegularExpressions.Regex.Match(baseName, @"^(.*?)\s+Copy\s+(\d+)$");
        if (match.Success)
            baseName = match.Groups[1].Value.Trim();

        // Find the highest existing Copy N number among siblings
        int maxCopy = 0;
        foreach (var sibling in parent.Children)
        {
            if (sibling == original) continue;
            var m = System.Text.RegularExpressions.Regex.Match(sibling.Name, @$"^{System.Text.RegularExpressions.Regex.Escape(baseName)}\s+Copy\s+(\d+)$");
            if (m.Success)
            {
                int num = int.Parse(m.Groups[1].Value);
                if (num > maxCopy) maxCopy = num;
            }
        }

        return $"{baseName} Copy {maxCopy + 1}";
    }

    /// <summary>Duplicate the selected element (deep clone) and insert it as a sibling after the original.
    /// Supports single-element mode only (used from right-click context menu).
    /// The new element gets name suffix "Copy 1", "Copy 2", etc.</summary>
    private void DuplicateSelectedElement(UIElement source)
    {
        if (source == null) return;

        // Find parent and index of the source element
        var rootElements = _bridge.SceneRootElements;
        if (rootElements == null) return;

        var (parent, sourceIndex) = FindParentAndIndex(rootElements, source, _bridge.SceneRoot);
        if (parent == null || sourceIndex < 0)
        {
            Console.WriteLine($"[SceneDetail] Duplicate FAIL: source '{source.Name}' parent not found");
            return;
        }

        // Deep clone the element
        var clone = source.DeepClone();
        clone.Name = GetDuplicateName(source, parent);

        // Insert right after the original
        int insertIdx = Math.Min(sourceIndex + 1, parent.Children.Count);
        clone.Parent = parent;
        parent.Children.Insert(insertIdx, clone);

        Console.WriteLine($"[SceneDetail] Duplicated '{source.Name}' → '{clone.Name}' at index {insertIdx}");

        // Record undo for add
        PushUndo(new UndoRedoAction
        {
            Type = UndoRedoAction.ActionType.Add,
            Element = clone,
            Parent = parent,
            ChildIndex = insertIdx,
        });

        // Select the cloned element
        _bridge.SelectedUIElement = clone;
        _bridge.SelectedUIElements?.Clear();
        _bridge.SelectedUIElements?.Add(clone);
    }

    /// <summary>Duplicate ALL currently selected elements. Each clone is inserted as a sibling
    /// right after its original, with "Copy N" name suffix. Supports both single and multi-select.
    /// Processes elements from last to first to preserve insert indices. Selects all clones afterwards.</summary>
    private void DuplicateAllSelected()
    {
        // ── 3D object duplication (Ctrl+D) — duplicates ALL selected ──
        var editorObjs = _bridge.SelectedEditorObjects;
        if (editorObjs != null && editorObjs.Count > 0)
        {
            var dups = new List<EditorObject>();
            int dupIdx = 0;
            foreach (var obj in editorObjs.ToArray())
            {
                var dup = _bridge.EditorObjectManager?.Duplicate(obj);
                if (dup != null)
                {
                    // Spread clones out so they don't stack on top of each other
                    dup.Position += new Vector3(dupIdx, 0f, 0f);
                    dupIdx++;
                    dups.Add(dup);
                    Console.WriteLine($"[SceneDetail] Duplicated 3D object: '{obj.Name}' → '{dup.Name}'");
                }
            }
            if (dups.Count > 0)
            {
                // Select all duplicated objects (last becomes primary)
                _bridge.SelectEditorObject(null);
                foreach (var d in dups)
                    _bridge.SelectEditorObject(d, additive: true);
                Console.WriteLine($"[SceneDetail] Duplicated {dups.Count} 3D object(s)");
            }
            return;
        }

        var multi = _bridge.SelectedUIElements;
        if (multi == null || multi.Count == 0) return;

        var rootElements = _bridge.SceneRootElements;
        if (rootElements == null) return;

        // Collect (element, parent, index) tuples sorted from LAST to FIRST by tree position.
        // Processing in reverse order ensures insert indices are correct when adding clones.
        var items = new List<(UIElement elem, UIElement parent, int index)>();
        foreach (var elem in multi)
        {
            var (parent, idx) = FindParentAndIndex(rootElements, elem, _bridge.SceneRoot);
            if (parent != null && idx >= 0)
                items.Add((elem, parent, idx));
        }

        // Sort by parent then index descending (last child first)
        items.Sort((a, b) =>
        {
            int cmp = a.parent.GetHashCode().CompareTo(b.parent.GetHashCode());
            return cmp != 0 ? cmp : b.index.CompareTo(a.index);
        });

        // Clone all elements
        var clones = new List<UIElement>();
        foreach (var (elem, parent, idx) in items)
        {
            var clone = elem.DeepClone();
            clone.Name = GetDuplicateName(elem, parent);

            int insertIdx = Math.Min(idx + 1, parent.Children.Count);
            clone.Parent = parent;
            parent.Children.Insert(insertIdx, clone);
            clones.Add(clone);

            PushUndo(new UndoRedoAction
            {
                Type = UndoRedoAction.ActionType.Add,
                Element = clone,
                Parent = parent,
                ChildIndex = insertIdx,
            });

            Console.WriteLine($"[SceneDetail] Duplicated '{elem.Name}' → '{clone.Name}' at index {insertIdx}");
        }

        // Select all cloned elements
        if (clones.Count > 0)
        {
            _bridge.SelectedUIElements?.Clear();
            foreach (var c in clones)
                _bridge.SelectedUIElements?.Add(c);
            _bridge.SelectedUIElement = clones[^1]; // last clone as primary
        }
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

            case UndoRedoAction.ActionType.ColorChange:
                // Restore element's old color value
                if (action.Element != null)
                {
                    SetColorProperty(action.Element, action.ColorPropertyName, action.OldColor);
                    Console.WriteLine($"[SceneDetail] Undo Color: '{action.Element.Name}'.{action.ColorPropertyName} → restored");
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

            case UndoRedoAction.ActionType.EditorTransform:
                // Restore the 3D editor object's old transform + pivot override
                if (action.EditorObj != null)
                {
                    action.EditorObj.Position = action.OldPos;
                    action.EditorObj.RotationEuler = action.OldRot;
                    action.EditorObj.Scale = action.OldScale;
                    action.EditorObj.GizmoPivotOverride = action.OldPivot;
                    Console.WriteLine($"[SceneDetail] Undo Gizmo: '{action.EditorObj.Name}' → pos {action.OldPos:F2}");
                    _bridge.SelectEditorObject(action.EditorObj); // non-additive: replaces the multi-set
                }
                break;

            case UndoRedoAction.ActionType.EditorTransformGroup:
                // Restore ALL 3D editor objects from the multi-drag in ONE undo step
                if (action.EditorObjs != null)
                {
                    int n = action.EditorObjs.Length;
                    if (action.OldPositions != null) n = Math.Min(n, action.OldPositions.Length);
                    if (action.OldRotations != null) n = Math.Min(n, action.OldRotations.Length);
                    if (action.OldScales != null) n = Math.Min(n, action.OldScales.Length);
                    if (action.OldPivots != null) n = Math.Min(n, action.OldPivots.Length);
                    for (int i = 0; i < n; i++)
                    {
                        var obj = action.EditorObjs[i];
                        if (obj == null) continue;
                        obj.Position = action.OldPositions?[i] ?? obj.Position;
                        obj.RotationEuler = action.OldRotations?[i] ?? obj.RotationEuler;
                        obj.Scale = action.OldScales?[i] ?? obj.Scale;
                        // Assign directly (may be null) so a pivot can be cleared back too
                        obj.GizmoPivotOverride = action.OldPivots?[i];
                        Console.WriteLine($"[SceneDetail] Undo Gizmo Group: '{obj.Name}' → pos {obj.Position:F2}");
                    }
                    // Restore the multi-selection set (last becomes primary)
                    _bridge.SelectEditorObject(null);
                    foreach (var obj in action.EditorObjs)
                        if (obj != null)
                            _bridge.SelectEditorObject(obj, additive: true);
                }
                break;

            case UndoRedoAction.ActionType.EditorPivotChange:
                // Restore the 3D editor object's previous gizmo pivot override (or clear it)
                if (action.EditorObj != null)
                {
                    action.EditorObj.GizmoPivotOverride = action.OldPivot;
                    Console.WriteLine($"[SceneDetail] Undo Gizmo Pivot: '{action.EditorObj.Name}' → {(action.OldPivot?.ToString() ?? "none")}");
                    _bridge.SelectEditorObject(action.EditorObj); // non-additive: replaces the multi-set
                }
                break;

            case UndoRedoAction.ActionType.SkySunChange:
                // Restore the sky's previous sun pitch/yaw override (null = back to time-of-day).
                if (action.EditorObj != null)
                {
                    action.EditorObj.SkySunPitch = action.OldPitch;
                    action.EditorObj.SkySunYaw = action.OldYaw;
                    Console.WriteLine($"[SceneDetail] Undo Sky Sun: '{action.EditorObj.Name}' → pitch={action.OldPitch?.ToString() ?? "time-of-day"}, yaw={action.OldYaw?.ToString() ?? "time-of-day"}");
                    _bridge.SelectEditorObject(action.EditorObj); // non-additive: replaces the multi-set
                }
                // Restore the synced Light marker's direction to its pre-drag value.
                if (action.LightObj != null && action.OldLightDir.HasValue)
                {
                    action.LightObj.LightDirection = action.OldLightDir.Value;
                    Console.WriteLine($"[SceneDetail] Undo Sky Sun light: '{action.LightObj.Name}' direction restored");
                }
                break;

            case UndoRedoAction.ActionType.TerrainPaint:
                // Restore the terrain's heightmap to its pre-stroke state.
                if (action.TerrainObj != null && action.OldHeights != null)
                {
                    action.TerrainObj.RestoreTerrainHeights(action.OldHeights);
                    Console.WriteLine($"[SceneDetail] Undo Terrain Paint: '{action.TerrainObj.Name}' heights restored");
                    _bridge.SelectEditorObject(action.TerrainObj);
                }
                break;

            case UndoRedoAction.ActionType.TerrainLayerPaint:
                // Restore the terrain's splat map to its pre-stroke state.
                if (action.TerrainObj != null && action.OldSplat != null)
                {
                    action.TerrainObj.RestoreTerrainSplat(action.OldSplat);
                    Console.WriteLine($"[SceneDetail] Undo Terrain Layer Paint: '{action.TerrainObj.Name}' splat restored");
                    _bridge.SelectEditorObject(action.TerrainObj);
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

            case UndoRedoAction.ActionType.ColorChange:
                // Apply new color value
                if (action.Element != null)
                {
                    SetColorProperty(action.Element, action.ColorPropertyName, action.NewColor);
                    Console.WriteLine($"[SceneDetail] Redo Color: '{action.Element.Name}'.{action.ColorPropertyName} → restored");
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

            case UndoRedoAction.ActionType.EditorTransform:
                // Re-apply the 3D editor object's new transform + pivot override
                if (action.EditorObj != null)
                {
                    action.EditorObj.Position = action.NewPos;
                    action.EditorObj.RotationEuler = action.NewRot;
                    action.EditorObj.Scale = action.NewScale;
                    action.EditorObj.GizmoPivotOverride = action.NewPivot;
                    Console.WriteLine($"[SceneDetail] Redo Gizmo: '{action.EditorObj.Name}' → pos {action.NewPos:F2}");
                    _bridge.SelectEditorObject(action.EditorObj); // non-additive: replaces the multi-set
                }
                break;

            case UndoRedoAction.ActionType.EditorTransformGroup:
                // Re-apply ALL 3D editor objects' new transforms in ONE redo step
                if (action.EditorObjs != null)
                {
                    int n = action.EditorObjs.Length;
                    if (action.NewPositions != null) n = Math.Min(n, action.NewPositions.Length);
                    if (action.NewRotations != null) n = Math.Min(n, action.NewRotations.Length);
                    if (action.NewScales != null) n = Math.Min(n, action.NewScales.Length);
                    if (action.NewPivots != null) n = Math.Min(n, action.NewPivots.Length);
                    for (int i = 0; i < n; i++)
                    {
                        var obj = action.EditorObjs[i];
                        if (obj == null) continue;
                        obj.Position = action.NewPositions?[i] ?? obj.Position;
                        obj.RotationEuler = action.NewRotations?[i] ?? obj.RotationEuler;
                        obj.Scale = action.NewScales?[i] ?? obj.Scale;
                        // Assign directly (may be null) so a pivot can be cleared back too
                        obj.GizmoPivotOverride = action.NewPivots?[i];
                        Console.WriteLine($"[SceneDetail] Redo Gizmo Group: '{obj.Name}' → pos {obj.Position:F2}");
                    }
                    // Restore the multi-selection set (last becomes primary)
                    _bridge.SelectEditorObject(null);
                    foreach (var obj in action.EditorObjs)
                        if (obj != null)
                            _bridge.SelectEditorObject(obj, additive: true);
                }
                break;

            case UndoRedoAction.ActionType.EditorPivotChange:
                // Re-apply the 3D editor object's new gizmo pivot override (or clear it)
                if (action.EditorObj != null)
                {
                    action.EditorObj.GizmoPivotOverride = action.NewPivot;
                    Console.WriteLine($"[SceneDetail] Redo Gizmo Pivot: '{action.EditorObj.Name}' → {(action.NewPivot?.ToString() ?? "none")}");
                    _bridge.SelectEditorObject(action.EditorObj); // non-additive: replaces the multi-set
                }
                break;

            case UndoRedoAction.ActionType.SkySunChange:
                // Re-apply the sky's post-drag sun pitch/yaw override.
                if (action.EditorObj != null)
                {
                    action.EditorObj.SkySunPitch = action.NewPitch;
                    action.EditorObj.SkySunYaw = action.NewYaw;
                    Console.WriteLine($"[SceneDetail] Redo Sky Sun: '{action.EditorObj.Name}' → pitch={action.NewPitch?.ToString() ?? "time-of-day"}, yaw={action.NewYaw?.ToString() ?? "time-of-day"}");
                    _bridge.SelectEditorObject(action.EditorObj); // non-additive: replaces the multi-set
                }
                // Re-apply the synced Light marker's post-drag direction.
                if (action.LightObj != null && action.NewLightDir.HasValue)
                {
                    action.LightObj.LightDirection = action.NewLightDir.Value;
                    Console.WriteLine($"[SceneDetail] Redo Sky Sun light: '{action.LightObj.Name}' direction re-applied");
                }
                break;

            case UndoRedoAction.ActionType.TerrainPaint:
                // Re-apply the terrain's post-stroke heights.
                if (action.TerrainObj != null && action.NewHeights != null)
                {
                    action.TerrainObj.RestoreTerrainHeights(action.NewHeights);
                    Console.WriteLine($"[SceneDetail] Redo Terrain Paint: '{action.TerrainObj.Name}' heights re-applied");
                    _bridge.SelectEditorObject(action.TerrainObj);
                }
                break;

            case UndoRedoAction.ActionType.TerrainLayerPaint:
                // Re-apply the terrain's post-stroke splat map.
                if (action.TerrainObj != null && action.NewSplat != null)
                {
                    action.TerrainObj.RestoreTerrainSplat(action.NewSplat);
                    Console.WriteLine($"[SceneDetail] Redo Terrain Layer Paint: '{action.TerrainObj.Name}' splat re-applied");
                    _bridge.SelectEditorObject(action.TerrainObj);
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
    /// Reloads INTO the existing scene root (if available) instead of creating a new
    /// root object, preserving the reference chain with the game scene (_sceneRoot).
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

            // 2. Fallback: search in the active save file manifest
            asset ??= SceneAssetSerializer.FindScene(sceneName, _bridge.ActiveSaveFile);

            if (asset == null || asset.Elements.Count == 0)
            {
                ShowSaveNotification($"No .ing file found for '{sceneName}'");
                return;
            }

            // 3. Convert serialized data back to live UIElement objects
            //    Keep the existing scene root REFERENCE (if any) so that the game scene
            //    (_sceneRoot field) continues pointing to the same object. This prevents
            //    the loaded data from being lost on the next frame's bridge.SceneRoot overwrite.
            UIElement? sceneRoot = _bridge.SceneRoot;
            bool reuseExisting = sceneRoot != null;

            if (!reuseExisting)
            {
                // No existing root — create a new one and load into it
                sceneRoot = new UIElement
                {
                    Name = sceneName,
                    Type = UIElementType.Scene,
                    IsVisible = false,
                };
            }

            // Clear existing children and load from asset
            sceneRoot.ClearChildren();
            foreach (var elemData in asset.Elements)
            {
                var child = SceneAssetSerializer.ToUIElement(elemData);
                sceneRoot.AddChild(child);
            }

            // 4. Set bridge references
            _bridge.SceneRoot = sceneRoot;
            _bridge.SceneRootElements = new List<UIElement> { sceneRoot }.AsReadOnly();
            SceneAssetSerializer.RegisterSceneRoot(sceneName, sceneRoot);

            // 5. Sync EditorScenes dictionary so SceneManager has the latest data
            _bridge.EditorScenes[sceneName] = new IDEBridge.EditorScene(
                sceneName,
                IDEBridge.SceneType.MainMenu,
                sceneRoot);
            _bridge.SelectedEditorScene = sceneName;

            // 6. Select first child so wireframe/handles appear in the viewport
            _bridge.SelectedUIElements?.Clear();
            if (sceneRoot.Children.Count > 0)
                _bridge.SelectedUIElement = sceneRoot.Children[0];
            else
                _bridge.SelectedUIElement = sceneRoot;
            if (_bridge.SelectedUIElement != null)
                _bridge.SelectedUIElements?.Add(_bridge.SelectedUIElement);

            _undoStack.Clear();
            _redoStack.Clear();

            int loadedCount = sceneRoot.Children.Count;
            Console.WriteLine($"[SceneDetail] Reloaded hierarchy for '{sceneName}' from .ing ({loadedCount} top-level elements) — reused existing root: {reuseExisting}");
            ShowSaveNotification($"Reloaded '{sceneName}' from .ing");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneDetail] Failed to reload scene: {ex.Message}");
            ShowSaveNotification($"Reload failed: {ex.Message}");
        }
    }
}
