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
                _editNameBuffer = "";
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
                        LoadScene(i);
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

                // ── Column 3: Load button ──
                ImGui.TableNextColumn();
                if (isActive)
                {
                    ImGui.TextColored(ColDim, "—");
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, ColButton);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColButtonHov);
                    if (ImGui.Button("Load", new Vector2(-1, 0)))
                    {
                        _selectedIdx = i;
                        LoadScene(i);
                    }
                    ImGui.PopStyleColor(2);
                }
            }

            ImGui.EndTable();
        }

        // ── Bottom hint ──
        ImGui.Separator();
        ImGui.TextDisabled("Double-click or click Load to switch scenes");

        // ── Save All Scenes to game.ing ──
        ImGui.Separator();
        {
            int count = SceneAssetSerializer.GetRegisteredSceneNames().Count;
            bool canSave = count > 0;

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.50f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.70f, 0.35f, 1f));
            ImGui.BeginDisabled(!canSave);
            if (ImGui.Button("Save All Scenes to game.ing", new Vector2(-1, 32)))
            {
                SceneAssetSerializer.SaveAllRegisteredScenes();
            }
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);

            if (ImGui.IsItemHovered())
            {
                string tooltip = canSave
                    ? $"Save {count} registered scene(s) to game.ing\n{string.Join(", ", SceneAssetSerializer.GetRegisteredSceneNames())}"
                    : "No scenes have been registered yet (enter a scene first)";
                ImGui.SetTooltip(tooltip);
            }

            ImGui.TextDisabled($"Registered: {count} scene(s)");
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
            ImGui.Combo("##add_type", ref _selectedNewSceneTypeIdx,
                IDEBridge.SceneTypeLabels, IDEBridge.SceneTypeLabels.Length);

            ImGui.Separator();
            bool nameValid = !string.IsNullOrWhiteSpace(_editNameBuffer);

            if (ImGui.Button("Create", new Vector2(120, 0)) && nameValid)
            {
                // Add new scene entry with user-chosen type
                _bridge.AvailableScenes.Add(new IDEBridge.SceneEntry(
                    _editNameBuffer.Trim(),
                    string.IsNullOrWhiteSpace(_editDescBuffer) ? "Custom scene" : _editDescBuffer.Trim(),
                    false,
                    (IDEBridge.SceneType)_selectedNewSceneTypeIdx));
                _selectedIdx = _bridge.AvailableScenes.Count - 1;
                Console.WriteLine($"[SceneManager] Added scene: {_editNameBuffer.Trim()} (type={IDEBridge.SceneTypeLabels[_selectedNewSceneTypeIdx]})");
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
                    _bridge.AvailableScenes[_selectedIdx] = old with
                    {
                        Name = string.IsNullOrWhiteSpace(_editNameBuffer) ? old.Name : _editNameBuffer.Trim(),
                        Description = string.IsNullOrWhiteSpace(_editDescBuffer) ? old.Description : _editDescBuffer.Trim(),
                        Type = (IDEBridge.SceneType)_selectedEditSceneTypeIdx,
                    };
                    Console.WriteLine($"[SceneManager] Updated scene: {_bridge.AvailableScenes[_selectedIdx].Name}");
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
                    _bridge.AvailableScenes.RemoveAt(_selectedIdx);
                    _selectedIdx = -1;
                    Console.WriteLine($"[SceneManager] Deleted scene: {deletedName}");
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
    }

    /// <summary>Switch to the scene at the given index.
    /// Uses the entry's SceneType (set by user) — no auto-detection from name.</summary>
    private void LoadScene(int idx)
    {
        var sm = _bridge.SceneManager;
        if (sm == null) return;

        var entries = _bridge.AvailableScenes;
        if (idx < 0 || idx >= entries.Count) return;

        var entry = entries[idx];
        string currentName = sm.CurrentScene?.Name ?? "";

        // Don't switch to the same scene (use Reload instead)
        if (string.Equals(entry.Name, currentName, StringComparison.OrdinalIgnoreCase))
            return;

        Console.WriteLine($"[SceneManagerPanel] Switching to scene: {entry.Name} (type={entry.Type})");

        IScene? newScene = CreateSceneByType(entry.Type, sm);

        if (newScene != null)
        {
            _bridge.MarkSceneInitialized(entry.Name);
            sm.SwitchScene(newScene);
        }
        else
        {
            Console.WriteLine($"[SceneManagerPanel] Cannot load scene '{entry.Name}' — unsupported type: {entry.Type}");
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
