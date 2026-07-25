using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Scene Manager panel — lists all available scenes with Load/Switch functionality.
/// Supports single-click selection and double-click to immediately switch to a scene.
/// Also shows the currently active scene and allows loading any scene.
/// Features Add/Edit/Delete scene management.
/// User explicitly picks the SceneType for each entry (no auto-detection).
/// </summary>
public class SceneManagerPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    // ── Selection state ──
    private int _selectedIdx = -1;
    private double _lastClickTime = 0;
    private const double DoubleClickInterval = 0.3;

    // ── File dialog for Load/Save As ──
    private readonly ImGuiFileDialog _fileDialog = new();

    // ── Popup state ──
    private bool _showAddPopup = false;
    private bool _showEditPopup = false;
    private bool _showDeleteConfirm = false;
    private string _editNameBuffer = "";
    private string _editDescBuffer = "";
    private int _selectedNewSceneTypeIdx = 0;   // combo box index for Add popup
    private int _selectedEditSceneTypeIdx = 0;  // combo box index for Edit popup
    private const int InputBufSize = 256;

    // ── Colors ──
    private static readonly Vector4 ColActive     = new(0.3f, 0.9f, 0.3f, 1f);
    private static readonly Vector4 ColInactive   = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ColSelected   = new(0.2f, 0.4f, 0.8f, 1f);
    private static readonly Vector4 ColHovered    = new(0.25f, 0.45f, 0.85f, 1f);
    private static readonly Vector4 ColButton     = new(0.12f, 0.30f, 0.50f, 1f);
    private static readonly Vector4 ColButtonHov  = new(0.18f, 0.40f, 0.65f, 1f);
    private static readonly Vector4 ColText       = new(0.9f, 0.9f, 0.95f, 1f);
    private static readonly Vector4 ColDim        = new(0.5f, 0.5f, 0.6f, 1f);
    private static readonly Vector4 ColGreen      = new(0.3f, 0.85f, 0.4f, 1f);
    private static readonly Vector4 ColAddBtn     = new(0.15f, 0.50f, 0.25f, 1f);
    private static readonly Vector4 ColAddBtnHov  = new(0.25f, 0.70f, 0.35f, 1f);
    private static readonly Vector4 ColEditBtn    = new(0.20f, 0.35f, 0.55f, 1f);
    private static readonly Vector4 ColEditBtnHov = new(0.30f, 0.50f, 0.75f, 1f);
    private static readonly Vector4 ColDelBtn     = new(0.55f, 0.15f, 0.15f, 1f);
    private static readonly Vector4 ColDelBtnHov  = new(0.75f, 0.25f, 0.25f, 1f);
    private static readonly Vector4 ColTypeMenu   = new(0.3f, 0.6f, 1.0f, 1f);
    private static readonly Vector4 ColTypeGame   = new(0.3f, 0.9f, 0.3f, 1f);
    private static readonly Vector4 ColTypeLoad   = new(1.0f, 0.7f, 0.3f, 1f);

    public SceneManagerPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Scene Manager", null, ref _visible);

    // ── Public API for main menu bar integration ──
    /// <summary>Open the Add New Scene popup (File > New Scene).</summary>
    public void OpenAddScenePopup()
    {
        _showAddPopup = true;
        _editNameBuffer = _selectedNewSceneTypeIdx switch { 0 => "mn", 1 => "scn", 2 => "load", _ => "scene" };
        _editDescBuffer = "";
        _selectedNewSceneTypeIdx = 0;
    }

    /// <summary>Open the file dialog to load a .ing file (File > Open Scene).</summary>
    public void OpenLoadFileDialog()
    {
        _fileDialog.OpenForLoad();
    }

    /// <summary>Save all editor scenes to game.ing (File > Save).</summary>
    public void SaveAllScenes() => SaveAllEditorScenes();

    /// <summary>Load scenes from a .ing file (called by main menu Recent Files).</summary>
    public void LoadFromFilePath(string path)
    {
        LoadFromIngFile(path);
        DarkEngine3D_gl_csharp.Engine.Config.RecentFilesManager.AddRecentFile(path);
    }

    public void Render()
    {
        if (!_visible) return;

        ImGui.Begin("Scene Manager", ref _visible);

        // ── Header: current scene indicator ──
        var sm = _bridge.SceneManager;
        string currentName = sm?.CurrentScene?.Name ?? "(none)";
        ImGui.TextColored(ColGreen, $"Active: {currentName}");
        ImGui.Separator();

        // ── Toolbar: Add / Edit / Delete ──
        {
            bool hasSelection = _selectedIdx >= 0 && _selectedIdx < _bridge.AvailableScenes.Count;

            float btnWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;

            // Add button (green)
            ImGui.PushStyleColor(ImGuiCol.Button, ColAddBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColAddBtnHov);
            if (ImGui.Button("+ Add", new Vector2(btnWidth, 28)))
            {
                _showAddPopup = true;
                _editNameBuffer = _selectedNewSceneTypeIdx switch { 0 => "mn", 1 => "scn", 2 => "load", _ => "scene" };
                _editDescBuffer = "";
                _selectedNewSceneTypeIdx = 0;
            }
            ImGui.PopStyleColor(2);

            ImGui.SameLine();

            // Edit button (blue)
            ImGui.PushStyleColor(ImGuiCol.Button, ColEditBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColEditBtnHov);
            ImGui.BeginDisabled(!hasSelection);
            if (ImGui.Button("Edit", new Vector2(btnWidth, 28)))
            {
                var entry = _bridge.AvailableScenes[_selectedIdx];
                _editNameBuffer = entry.Name;
                _editDescBuffer = entry.Description;
                _selectedEditSceneTypeIdx = (int)entry.Type;
                _showEditPopup = true;
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);

            ImGui.SameLine();

            // Delete button (red)
            ImGui.PushStyleColor(ImGuiCol.Button, ColDelBtn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColDelBtnHov);
            ImGui.BeginDisabled(!hasSelection);
            if (ImGui.Button("Del", new Vector2(btnWidth, 28)))
            {
                _showDeleteConfirm = true;
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);

            if (ImGui.IsItemHovered() && ImGui.IsItemActive())
                ImGui.SetTooltip("Delete selected scene entry");
        }

        ImGui.Separator();

        // ── Available scenes list ──
        if (ImGui.BeginTable("scene_table", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Scene",     ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Type",      ImGuiTableColumnFlags.WidthFixed,   80f);
            ImGui.TableSetupColumn("State",     ImGuiTableColumnFlags.WidthFixed,   60f);
            ImGui.TableSetupColumn("Action",    ImGuiTableColumnFlags.WidthFixed,   70f);
            ImGui.TableHeadersRow();

            var entries = _bridge.AvailableScenes;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool isActive = string.Equals(entry.Name, currentName, StringComparison.OrdinalIgnoreCase);
                bool isSelected = (i == _selectedIdx);

                ImGui.TableNextRow();

                // ── Column 0: Scene name + description ──
                ImGui.TableNextColumn();
                ImGui.PushID($"scene_{i}");

                // Selectable row covering the full width
                ImGui.Selectable("", isSelected,
                    ImGuiSelectableFlags.SpanAllColumns,
                    new Vector2(0, 32));

                // Handle selection + double-click
                if (ImGui.IsItemClicked())
                {
                    _selectedIdx = i;
                    double now = ImGui.GetTime();
                    if (now - _lastClickTime < DoubleClickInterval)
                    {
                        SelectOrLoadScene(i);
                    }
                    _lastClickTime = now;
                }

                // Draw scene name + description inside the selectable area
                var drawList = ImGui.GetWindowDrawList();
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                float textX = min.X + 8f;

                // Name (top half)
                var nameCol = isActive ? ColGreen : (isSelected ? ColSelected : ColText);
                drawList.AddText(new Vector2(textX, min.Y + 3f), ImGui.ColorConvertFloat4ToU32(nameCol), entry.Name);

                // Description (bottom half)
                drawList.AddText(new Vector2(textX, min.Y + 18f), ImGui.ColorConvertFloat4ToU32(ColDim), entry.Description);

                ImGui.PopID();

                // ── Column 1: Type badge ──
                ImGui.TableNextColumn();
                var (typeLabel, typeCol) = entry.Type switch
                {
                    IDEBridge.SceneType.MainMenu => ("MainMenu", ColTypeMenu),
                    IDEBridge.SceneType.GameScene => ("GameScene", ColTypeGame),
                    IDEBridge.SceneType.Loading => ("Loading", ColTypeLoad),
                    _ => ("?", ColDim),
                };
                ImGui.TextColored(typeCol, typeLabel);

                // ── Column 2: State badge ──
                ImGui.TableNextColumn();
                string state = isActive ? "Active" : (entry.HasInitializedEntry ? "Loaded" : "—");
                var stateCol = isActive ? ColActive : (entry.HasInitializedEntry ? ColInactive : ColDim);
                ImGui.TextColored(stateCol, state);

                // ── Column 3: Select/Edit button ──
                ImGui.TableNextColumn();
                if (isActive)
                {
                    ImGui.TextColored(ColDim, "—");
                }
                else
                {
                    // For editor scenes, show "Select"; for game scenes, show "Load"
                    string actionLabel = _bridge.EditorScenes.ContainsKey(entry.Name) ? "Select" : "Load";
                    ImGui.PushStyleColor(ImGuiCol.Button, ColButton);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColButtonHov);
                    if (ImGui.Button(actionLabel, new Vector2(-1, 0)))
                    {
                        _selectedIdx = i;
                        SelectOrLoadScene(i);
                    }
                    ImGui.PopStyleColor(2);
                }
            }

            ImGui.EndTable();
        }

        // ── Bottom hint ──
        ImGui.Separator();
        ImGui.TextDisabled("Double-click or click Select/Load to switch scenes");

        // ── File operations: Save All / Save As / Load from file ──
        ImGui.Separator();
        {
            bool canSave = _bridge.EditorScenes.Count > 0;
            float btnW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;

            // Save All button (green) — saves to default game.ing
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.50f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.70f, 0.35f, 1f));
            ImGui.BeginDisabled(!canSave);
            if (ImGui.Button("Save All", new Vector2(btnW, 28)))
            {
                SaveAllEditorScenes();
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered() && canSave)
                ImGui.SetTooltip($"Save {_bridge.EditorScenes.Count} scene(s) to {SceneAssetSerializer.GameIngPath}");

            ImGui.SameLine();

            // Save As button (teal) — opens file dialog to choose location
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.10f, 0.45f, 0.50f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.20f, 0.60f, 0.65f, 1f));
            ImGui.BeginDisabled(!canSave);
            if (ImGui.Button("Save As...", new Vector2(btnW, 28)))
            {
                _fileDialog.OpenForSave("game.ing");
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Save scenes to a custom .ing file location");

            ImGui.SameLine();

            // Load from file button (blue) — opens file dialog to pick .ing file
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.30f, 0.50f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.18f, 0.40f, 0.65f, 1f));
            if (ImGui.Button("Load File...", new Vector2(btnW, 28)))
            {
                _fileDialog.OpenForLoad();
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Open a .ing file and load its scenes");

            ImGui.TextDisabled($"Editor scenes: {_bridge.EditorScenes.Count}");
        }

        // ── Reload current scene button ──
        if (sm != null && sm.CurrentScene != null)
        {
            ImGui.Separator();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.15f, 0.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.25f, 0.25f, 1f));
            if (ImGui.Button("Reload Current Scene", new Vector2(-1, 30)))
            {
                ReloadCurrentScene();
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Re-enter the current scene (useful for testing)");
        }

        ImGui.End();

        // ── Popups (rendered after End so they float above) ──

        // ── Add Scene popup ──
        if (_showAddPopup)
        {
            ImGui.OpenPopup("Add New Scene");
            _showAddPopup = false;
        }

        bool addPopupOpen = true;
        if (ImGui.BeginPopupModal("Add New Scene", ref addPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Create a new scene entry:");
            ImGui.Separator();

            ImGui.Text("Name:");
            ImGui.SetNextItemWidth(280);
            ImGui.InputText("##add_name", ref _editNameBuffer, InputBufSize);

            ImGui.Text("Description:");
            ImGui.SetNextItemWidth(280);
            ImGui.InputText("##add_desc", ref _editDescBuffer, InputBufSize);

            ImGui.Text("Scene Type:");
            ImGui.SetNextItemWidth(280);
            int prevSceneTypeIdx = _selectedNewSceneTypeIdx;
            ImGui.Combo("##add_type", ref _selectedNewSceneTypeIdx,
                IDEBridge.SceneTypeLabels, IDEBridge.SceneTypeLabels.Length);
            if (_selectedNewSceneTypeIdx != prevSceneTypeIdx)
            {
                string oldDefault = prevSceneTypeIdx switch { 0 => "mn", 1 => "scn", 2 => "load", _ => "scene" };
                if (_editNameBuffer == oldDefault)
                {
                    _editNameBuffer = _selectedNewSceneTypeIdx switch { 0 => "mn", 1 => "scn", 2 => "load", _ => "scene" };
                }
            }

            ImGui.Separator();
            bool nameValid = !string.IsNullOrWhiteSpace(_editNameBuffer);

            if (ImGui.Button("Create", new Vector2(120, 0)) && nameValid)
            {
                string sceneName = _editNameBuffer.Trim();

                // Add new scene entry with user-chosen type
                _bridge.AvailableScenesInternal.Add(new IDEBridge.SceneEntry(
                    sceneName,
                    string.IsNullOrWhiteSpace(_editDescBuffer) ? "Custom scene" : _editDescBuffer.Trim(),
                    false,
                    (IDEBridge.SceneType)_selectedNewSceneTypeIdx));
                _selectedIdx = _bridge.AvailableScenes.Count - 1;
                Console.WriteLine($"[SceneManager] Added scene: {sceneName} (type={IDEBridge.SceneTypeLabels[_selectedNewSceneTypeIdx]})");

                // Create an EditorScene (UIElement root) for this scene so it can be edited in SceneDetail
                if (!_bridge.EditorScenes.ContainsKey(sceneName))
                {
                    var sceneRoot = new UIElement
                    {
                        Name = sceneName,
                        Type = UIElementType.Scene,
                        IsVisible = false,
                    };
                    _bridge.EditorScenes[sceneName] = new IDEBridge.EditorScene(
                        sceneName,
                        (IDEBridge.SceneType)_selectedNewSceneTypeIdx,
                        sceneRoot);
                    Console.WriteLine($"[SceneManager] Created EditorScene root for '{sceneName}'");
                }

                // Select the new scene in the editor
                SelectEditorScene(sceneName);

                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        // ── Edit Scene popup ──
        if (_showEditPopup)
        {
            ImGui.OpenPopup("Edit Scene");
            _showEditPopup = false;
        }

        bool editPopupOpen = true;
        if (ImGui.BeginPopupModal("Edit Scene", ref editPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Edit scene entry:");
            ImGui.Separator();

            ImGui.Text("Name:");
            ImGui.SetNextItemWidth(280);
            ImGui.InputText("##edit_name", ref _editNameBuffer, InputBufSize);

            ImGui.Text("Description:");
            ImGui.SetNextItemWidth(280);
            ImGui.InputText("##edit_desc", ref _editDescBuffer, InputBufSize);

            ImGui.Text("Scene Type:");
            ImGui.SetNextItemWidth(280);
            ImGui.Combo("##edit_type", ref _selectedEditSceneTypeIdx,
                IDEBridge.SceneTypeLabels, IDEBridge.SceneTypeLabels.Length);

            ImGui.Separator();

            if (ImGui.Button("Save", new Vector2(120, 0)))
            {
                if (_selectedIdx >= 0 && _selectedIdx < _bridge.AvailableScenes.Count)
                {
                    var old = _bridge.AvailableScenes[_selectedIdx];
                    string newName = string.IsNullOrWhiteSpace(_editNameBuffer) ? old.Name : _editNameBuffer.Trim();
                    string oldName = old.Name;

                    _bridge.AvailableScenesInternal[_selectedIdx] = old with
                    {
                        Name = newName,
                        Description = string.IsNullOrWhiteSpace(_editDescBuffer) ? old.Description : _editDescBuffer.Trim(),
                        Type = (IDEBridge.SceneType)_selectedEditSceneTypeIdx,
                    };

                    // If name changed, update EditorScenes dictionary key too
                    if (!string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_bridge.EditorScenes.TryGetValue(oldName, out var oldEditorScene))
                        {
                            _bridge.EditorScenes.Remove(oldName);
                            var renamedRoot = oldEditorScene.Root;
                            renamedRoot.Name = newName;
                            _bridge.EditorScenes[newName] = oldEditorScene with
                            {
                                Name = newName,
                                Root = renamedRoot,
                                Type = (IDEBridge.SceneType)_selectedEditSceneTypeIdx
                            };

                            // If the renamed scene was selected, update selection
                            if (_bridge.SelectedEditorScene == oldName)
                            {
                                _bridge.SelectedEditorScene = newName;
                                _bridge.SceneRoot = renamedRoot;
                            }
                        }
                    }

                    Console.WriteLine($"[SceneManager] Updated scene: '{oldName}' → '{newName}'");
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

        // ── Delete Confirm popup ──
        if (_showDeleteConfirm)
        {
            ImGui.OpenPopup("Delete Scene?");
            _showDeleteConfirm = false;
        }

        bool deletePopupOpen = true;
        if (ImGui.BeginPopupModal("Delete Scene?", ref deletePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (_selectedIdx >= 0 && _selectedIdx < _bridge.AvailableScenes.Count)
            {
                var delEntry = _bridge.AvailableScenes[_selectedIdx];
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f),
                    $"Delete \"{delEntry.Name}\"?");
                ImGui.TextDisabled("This only removes the entry from the list.");
                ImGui.TextDisabled("The .ing file on disk will not be deleted.");
            }
            ImGui.Separator();

            if (ImGui.Button("Delete", new Vector2(120, 0)))
            {
                if (_selectedIdx >= 0 && _selectedIdx < _bridge.AvailableScenes.Count)
                {
                    string deletedName = _bridge.AvailableScenes[_selectedIdx].Name;
                    _bridge.AvailableScenesInternal.RemoveAt(_selectedIdx);

                    // Also remove from editor scenes if present
                    if (_bridge.EditorScenes.ContainsKey(deletedName))
                    {
                        _bridge.EditorScenes.Remove(deletedName);
                        if (_bridge.SelectedEditorScene == deletedName)
                        {
                            _bridge.SelectedEditorScene = null;
                            _bridge.SceneRoot = null;
                            _bridge.SceneRootElements = null;
                            _bridge.SelectedUIElement = null;
                            _bridge.SelectedUIElements?.Clear();
                        }
                    }

                    _selectedIdx = -1;
                    Console.WriteLine($"[SceneManager] Deleted scene: {deletedName}");

                    // Auto-save to game.ing so deletion is permanent (not just in memory)
                    if (_bridge.EditorScenes.Count > 0)
                    {
                        SaveAllEditorScenes();
                        Console.WriteLine($"[SceneManager] Auto-saved after deleting '{deletedName}'");
                    }
                    else
                    {
                        // No scenes left — write empty manifest to clear game.ing
                        // (SaveGameIng() with no args would preserve old data, so write fresh)
                        Console.WriteLine($"[SceneManager] No scenes left, writing empty manifest");
                        var emptyManifest = new SceneManifest();
                        string json = System.Text.Json.JsonSerializer.Serialize(
                            emptyManifest, SceneAssetSerializer.GetJsonOptions());
                        Directory.CreateDirectory(
                            Path.GetDirectoryName(SceneAssetSerializer.GameIngPath)!);
                        File.WriteAllText(SceneAssetSerializer.GameIngPath, json);
                    }
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

        // ── File dialog (Load / Save As) ──
        _fileDialog.Render();
        if (_fileDialog.IsConfirmed && _fileDialog.SelectedPath != null)
        {
            string path = _fileDialog.SelectedPath;

            if (_fileDialog.IsSaveMode)
            {
                // Save As: write editor scenes to chosen path
                SaveToIngFile(path);
            }
            else
            {
                // Load: read from chosen path
                LoadFromIngFile(path);
                DarkEngine3D_gl_csharp.Engine.Config.RecentFilesManager.AddRecentFile(path);
            }
            _fileDialog.Close();
        }
    }

    /// <summary>Public wrapper so IDE can wire it to IDEBridge.SaveAllScenes delegate.</summary>
    public void SaveAllEditorScenesPublic() => SaveAllEditorScenes();

    /// <summary>Public wrapper so IDE can wire it to IDEBridge.RequestSaveAsDialog delegate.</summary>
    public void OpenSaveAsDialog() => _fileDialog.OpenForSave("game.ing");

    /// <summary>Save ALL editor scenes to game.ing file by reusing existing save logic.</summary>
    private void SaveAllEditorScenes()
    {
        if (_bridge.EditorScenes.Count == 0)
        {
            Console.WriteLine("[SceneManagerPanel] No editor scenes to save.");
            return;
        }

        // Convert editor scenes to the format SaveGameIng expects
        var scenes = _bridge.EditorScenes
            .Select(kv => (kv.Key, kv.Value.Root))
            .ToArray();

        SceneAssetSerializer.SaveGameIng(scenes);
        Console.WriteLine($"[SceneManagerPanel] Saved {_bridge.EditorScenes.Count} editor scenes to game.ing");
    }

    /// <summary>Select an editor scene to display in SceneDetail. Does NOT switch game scene.</summary>
    private void SelectEditorScene(string sceneName)
    {
        if (!_bridge.EditorScenes.ContainsKey(sceneName))
        {
            Console.WriteLine($"[SceneManagerPanel] Editor scene '{sceneName}' not found");
            return;
        }

        // Set the selected editor scene on the bridge
        _bridge.SelectedEditorScene = sceneName;
        var editorScene = _bridge.EditorScenes[sceneName];

        // Update bridge SceneRoot/SceneRootElements for HierarchyPanel to display
        // Show the scene root itself in the tree (not its children directly)
        _bridge.SceneRoot = editorScene.Root;
        _bridge.SceneRootElements = new List<UIElement> { editorScene.Root }.AsReadOnly();

        // Select the first visible child so wireframe/handles appear in the viewport
        _bridge.SelectedUIElements?.Clear();
        if (editorScene.Root.Children.Count > 0)
        {
            _bridge.SelectedUIElement = editorScene.Root.Children[0];
        }
        else
        {
            _bridge.SelectedUIElement = editorScene.Root;
        }
        if (_bridge.SelectedUIElement != null)
            _bridge.SelectedUIElements?.Add(_bridge.SelectedUIElement);

        _bridge.MarkSceneInitialized(sceneName);
        Console.WriteLine($"[SceneManagerPanel] Selected editor scene: {sceneName}");
    }

    /// <summary>Handle click on a scene entry: select in editor or load game scene.
    /// For editor scenes → select for editing. For game scenes → load as active scene.</summary>
    private void SelectOrLoadScene(int idx)
    {
        var entries = _bridge.AvailableScenes;
        if (idx < 0 || idx >= entries.Count) return;

        var entry = entries[idx];
        string sceneName = entry.Name;

        // If this scene has an EditorScene entry, select it in the editor (not load from file)
        if (_bridge.EditorScenes.ContainsKey(sceneName))
        {
            SelectEditorScene(sceneName);
            return;
        }

        // Otherwise: fallback to loading a game scene (for existing game scenes)
        var sm = _bridge.SceneManager;
        if (sm == null) return;

        string currentName = sm.CurrentScene?.Name ?? "";
        if (string.Equals(sceneName, currentName, StringComparison.OrdinalIgnoreCase))
            return;

        Console.WriteLine($"[SceneManagerPanel] Loading game scene: {sceneName} (type={entry.Type})");

        IScene? newScene = CreateSceneByType(entry.Type, sm);
        if (newScene != null)
        {
            _bridge.MarkSceneInitialized(entry.Name);
            sm.SwitchScene(newScene);
        }
    }

    /// <summary>Reload the current scene by re-entering it.
    /// Looks up the matching entry in AvailableScenes to get its SceneType.</summary>
    private void ReloadCurrentScene()
    {
        var sm = _bridge.SceneManager;
        if (sm?.CurrentScene == null) return;

        string name = sm.CurrentScene.Name;
        Console.WriteLine($"[SceneManagerPanel] Reloading scene: {name}");

        // Find the matching entry in AvailableScenes
        foreach (var entry in _bridge.AvailableScenes)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                IScene? newScene = CreateSceneByType(entry.Type, sm);
                if (newScene != null)
                    sm.SwitchScene(newScene);
                return;
            }
        }

        Console.WriteLine($"[SceneManagerPanel] No AvailableScenes entry matches '{name}' — cannot reload");
    }

    // ──────────────────────────────────────────────
    //  File Dialog — Load / Save As
    // ──────────────────────────────────────────────

    /// <summary>Load all scenes from a .ing file and populate EditorScenes.
    /// Clears any existing editor scenes and replaces with the loaded data.</summary>
    private void LoadFromIngFile(string filePath)
    {
        try
        {
            var manifest = SceneAssetSerializer.LoadManifestFromPath(filePath);
            if (manifest == null || manifest.Scenes.Count == 0)
            {
                Console.WriteLine($"[SceneManagerPanel] No scenes found in: {filePath}");
                return;
            }

            // Clear existing editor scenes — we're replacing with loaded data
            _bridge.EditorScenes.Clear();

            string? firstLoadedScene = null;
            int sceneCount = 0;

            foreach (var asset in manifest.Scenes)
            {
                string sceneName = asset.SceneName ?? $"Scene_{sceneCount}";

                // Add to AvailableScenes if not already present
                bool exists = _bridge.AvailableScenes.Any(e =>
                    e.Name.Equals(sceneName, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    _bridge.AvailableScenesInternal.Add(new IDEBridge.SceneEntry(
                        sceneName,
                        $"Loaded from {Path.GetFileName(filePath)}",
                        false,
                        IDEBridge.SceneType.MainMenu));
                }

                // Build a tree root from the elements
                UIElement sceneRoot;
                if (asset.Elements.Count == 1)
                {
                    // Single element — use directly as the root
                    sceneRoot = SceneAssetSerializer.ToUIElement(asset.Elements[0]);
                }
                else if (asset.Elements.Count > 1)
                {
                    // Multiple top-level elements — wrap them under a Scene-type root
                    sceneRoot = new UIElement
                    {
                        Name = sceneName,
                        Type = UIElementType.Scene,
                        IsVisible = true,
                    };
                    foreach (var elemData in asset.Elements)
                    {
                        var child = SceneAssetSerializer.ToUIElement(elemData);
                        sceneRoot.AddChild(child);
                    }
                    Console.WriteLine($"[SceneManagerPanel] Wrapped {asset.Elements.Count} elements under root for '{sceneName}'");
                }
                else
                {
                    // No elements — skip this scene
                    Console.WriteLine($"[SceneManagerPanel] Skipping scene '{sceneName}' (0 elements)");
                    continue;
                }

                _bridge.EditorScenes[sceneName] = new IDEBridge.EditorScene(
                    sceneName, IDEBridge.SceneType.MainMenu, sceneRoot);

                // Track the first loaded scene for auto-selection
                firstLoadedScene ??= sceneName;
                sceneCount++;
                Console.WriteLine($"[SceneManagerPanel] Loaded scene '{sceneName}' from {filePath}");
            }

            // Select the first loaded scene
            if (sceneCount > 0 && firstLoadedScene != null)
            {
                SelectEditorScene(firstLoadedScene);
                Console.WriteLine($"[SceneManagerPanel] Loaded {sceneCount} scene(s) from {filePath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneManagerPanel] Failed to load {filePath}: {ex.Message}");
        }
    }

    /// <summary>Save all editor scenes to a specific .ing file path (Save As).</summary>
    private void SaveToIngFile(string filePath)
    {
        if (_bridge.EditorScenes.Count == 0)
        {
            Console.WriteLine("[SceneManagerPanel] No editor scenes to save.");
            return;
        }

        try
        {
            // Build manifest from editor scenes
            var manifest = new SceneManifest();
            foreach (var (name, editorScene) in _bridge.EditorScenes)
            {
                manifest.Scenes.Add(new SceneAsset
                {
                    SceneName = name,
                    Elements = [SceneAssetSerializer.ToData(editorScene.Root)],
                    BackgroundObjects = []
                });
            }

            // Serialize and write
            string json = System.Text.Json.JsonSerializer.Serialize(manifest,
                SceneAssetSerializer.GetJsonOptions());
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, json);

            Console.WriteLine($"[SceneManagerPanel] Saved {_bridge.EditorScenes.Count} scene(s) to {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneManagerPanel] Failed to save to {filePath}: {ex.Message}");
        }
    }

    // ── Scene factory helpers ──

    private static MainMenuScene CreateMainMenuScene(SceneManager sm)
    {
        var camera = new Camera(0f, 40f, 0f, 0f, -30f,
            (float)Glfw.WindowWidth / Glfw.WindowHeight, 60f, 0.1f, 2800f);

        Vector3 sunDir = new(1.0f, 0.5f, 0.0f);
        Vector3 sunColor = new(1.0f, 0.95f, 0.8f);
        Vector3 viewPos = new(camera.Position.X, camera.Position.Y, camera.Position.Z);
        var lights = new Lights(sunDir, sunColor, viewPos, "16:00");

        return new MainMenuScene(sm, camera, lights);
    }

    private static LoadingScene CreateLoadingScene(SceneManager sm)
    {
        var camera = new Camera(0f, 40f, 0f, 0f, -30f,
            (float)Glfw.WindowWidth / Glfw.WindowHeight, 60f, 0.1f, 2800f);

        Vector3 sunDir = new(1.0f, 0.5f, 0.0f);
        Vector3 sunColor = new(1.0f, 0.95f, 0.8f);
        Vector3 viewPos = new(camera.Position.X, camera.Position.Y, camera.Position.Z);
        var lights = new Lights(sunDir, sunColor, viewPos, "16:00");

        return new LoadingScene(sm, camera, lights);
    }

    /// <summary>
    /// Create a scene by its explicit SceneType enum.
    /// Full user control — no name-based auto-detection.
    /// </summary>
    private static IScene? CreateSceneByType(IDEBridge.SceneType type, SceneManager sm)
    {
        return type switch
        {
            IDEBridge.SceneType.MainMenu => CreateMainMenuScene(sm),
            IDEBridge.SceneType.GameScene => CreateLoadingScene(sm), // Loading → GameScene
            IDEBridge.SceneType.Loading => CreateLoadingScene(sm),
            _ => null
        };
    }
}
