using System;
using System.Collections.Generic;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Item Catalog editor (inventory phase 2): author ItemDefs — icon cropped from any
/// Sprite Editor sheet (visual cell picker), equip slot + stat bonuses, use effects,
/// price/stack. Persists per project to Assets/Items/items.json (Save Catalog button
/// + auto-load on project open, auto-clear on close — wired in IDE.cs).
///
/// Layout: left item-list column + right editor column (SameLine — both children
/// reserve the footer height so they end above the Save row), grouped sections with
/// wide labels and a large icon picker.
/// </summary>
public class ItemEditorPanel
{
    private readonly IDEBridge _bridge;
    private int _selectedIndex = -1;
    private string _newItemId = "";
    private bool _dirty;

    // Icon picker state (shared preview of the selected sheet).
    private string _pickerSheet = "";
    private int _pickerHoverCell = -1;

    public ItemEditorPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu()
    {
        if (ImGui.MenuItem("Item Editor"))
        {
            _show = !_show;
            if (_show && InventorySystem.Items.Count > 0 && _selectedIndex < 0)
                _selectedIndex = 0;
        }
    }
    private bool _show;

    public void Render()
    {
        if (!_show) return;
        // SANE SIZE EVERY OPEN (Appearing — not FirstUseEver): the imgui.ini layout
        // may carry a monster size saved by the earlier auto-sized version, and
        // FirstUseEver would never correct it. Appearing resets to 800×560 each time
        // the panel opens (user can still resize freely while it stays open), and the
        // constraints keep any session resize within a tidy tool window.
        ImGui.SetNextWindowSize(new Vector2(800, 560), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new Vector2(620, 430), new Vector2(1180, 880));
        if (!ImGui.Begin("Item Editor", ref _show))
        {
            ImGui.End();
            return;
        }
        if (!Engine.Project.ProjectManager.IsProjectLoaded)
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "Open a project first — catalog saves to Assets/Items/items.json");
            ImGui.End();
            return;
        }

        // Footer (Save row) height — both columns end above it via negative height.
        float footerH = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y;

        // ── Two columns on ONE row: list | SameLine | editor ──
        DrawItemList(new Vector2(250, -footerH));
        ImGui.SameLine();
        DrawEditor(new Vector2(0, -footerH));

        // ── Footer: save + status ──
        if (ImGui.Button("Save Catalog", new Vector2(130, 0)))
        {
            InventorySystem.SaveCatalog();
            _dirty = false;
        }
        ImGui.SameLine();
        if (_dirty)
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "(unsaved changes)");
        else
            ImGui.TextDisabled($"{InventorySystem.Items.Count} items — Assets/Items/items.json");

        ImGui.End();
    }

    // ═══════════════════════ Item list (left) ═══════════════════════

    private void DrawItemList(Vector2 size)
    {
        ImGui.BeginChild("itemList", size, ImGuiChildFlags.Borders);
        ImGui.TextDisabled($"ITEMS ({InventorySystem.Items.Count})");
        ImGui.Separator();

        for (int i = 0; i < InventorySystem.Items.Count; i++)
        {
            var def = InventorySystem.Items[i];
            ImGui.PushID($"item{i}");

            // 30px icon tile + name on one line (draw-list icon so Selectable keeps
            // full-row hover/selection).
            var rowMin = ImGui.GetCursorScreenPos();
            float rowH = 32f;
            uint tex = InventorySystem.GetIconTexture(def);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(rowMin, rowMin + new Vector2(rowH, rowH),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.10f, 0.15f, 1f)), 4f);
            if (tex != 0)
            {
                // ImGui UVs are top-first → pass GetIconUV's image-space (vTop, vBottom)
                // straight through (same convention as the picker below).
                var (u0, vTop, u1, vBottom) = InventorySystem.GetIconUV(def);
                dl.AddImage((nint)tex, rowMin + new Vector2(3), rowMin + new Vector2(rowH - 3),
                    new Vector2(u0, vTop), new Vector2(u1, vBottom));
            }
            ImGui.Dummy(new Vector2(rowH + 6, rowH));
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X + rowH + 8f, rowMin.Y + (rowH - ImGui.GetTextLineHeight()) * 0.5f));

            if (ImGui.Selectable($"{def.Name}##sel{i}", i == _selectedIndex,
                    ImGuiSelectableFlags.None, new Vector2(0, rowH)))
                _selectedIndex = i;
            ImGui.PopID();
        }

        ImGui.Separator();
        // New-item row (input fills the width).
        ImGui.SetNextItemWidth(-70f);
        ImGui.InputText("##newId", ref _newItemId, 48);
        ImGui.SameLine();
        bool canAdd = !string.IsNullOrWhiteSpace(_newItemId) && InventorySystem.Find(_newItemId.Trim()) == null;
        using (new ImScopeDisabled(!canAdd))
        {
            if (ImGui.Button("+ New", new Vector2(64, 0)) && canAdd)
            {
                InventorySystem.Register(new InventorySystem.ItemDef
                {
                    Id = _newItemId.Trim(),
                    Name = _newItemId.Trim(),
                });
                _selectedIndex = InventorySystem.Items.Count - 1;
                _dirty = true;
                _newItemId = "";
            }
        }
        ImGui.Spacing();
        bool hasSel = _selectedIndex >= 0 && _selectedIndex < InventorySystem.Items.Count;
        using (new ImScopeDisabled(!hasSel))
        {
            if (ImGui.Button("Delete selected", new Vector2(-1, 0)) && hasSel)
            {
                var def = InventorySystem.Items[_selectedIndex];
                InventorySystem.Items.RemoveAt(_selectedIndex);
                InventorySystem.RemoveFromCatalog(def.Id);
                _selectedIndex = -1;
                _dirty = true;
            }
        }
        ImGui.EndChild();
    }

    // ═══════════════════════ Editor (right) ═══════════════════════

    private void DrawEditor(Vector2 size)
    {
        ImGui.BeginChild("itemEdit", size, ImGuiChildFlags.Borders);

        if (_selectedIndex < 0 || _selectedIndex >= InventorySystem.Items.Count)
        {
            ImGui.TextDisabled("Select an item on the left, or create one below it.");
            ImGui.EndChild();
            return;
        }
        var def = InventorySystem.Items[_selectedIndex];

        // Consistent wide labels for all fields in this panel.
        ImGui.PushItemWidth(240);

        // ── Identity ──
        if (ImGui.CollapsingHeader("Identity", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string id = def.Id;
            if (ImGui.InputText("Id", ref id, 64) && !string.IsNullOrWhiteSpace(id))
            { def.Id = id.Trim(); _dirty = true; }

            string name = def.Name;
            if (ImGui.InputText("Name", ref name, 64)) { def.Name = name; _dirty = true; }

            int maxStack = def.MaxStack;
            if (ImGui.InputInt("Max Stack", ref maxStack)) { def.MaxStack = Math.Clamp(maxStack, 1, 9999); _dirty = true; }

            int price = def.Price;
            if (ImGui.DragInt("Price (gold)", ref price, 0.5f, 0, 999999)) { def.Price = Math.Max(0, price); _dirty = true; }

            string notes = def.Notes;
            if (ImGui.InputTextMultiline("Notes", ref notes, 160, new Vector2(500, 54)))
            { def.Notes = notes; _dirty = true; }
        }

        // ── Icon ──
        if (ImGui.CollapsingHeader("Icon (crop from sprite sheet)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawIconSection(def);
        }

        // ── Equipment ──
        if (ImGui.CollapsingHeader("Equipment & Stats", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawEquipSection(def);
        }

        // ── Use effect ──
        if (ImGui.CollapsingHeader("Use Effect", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawUseSection(def);
        }

        ImGui.PopItemWidth();
        ImGui.EndChild();
    }

    private void DrawIconSection(InventorySystem.ItemDef def)
    {
        var sheets = IDEBridge.GetSpriteSheetNames();
        if (sheets.Count == 0)
        {
            ImGui.TextDisabled("(no sprite sheets registered in the Sprite Editor)");
            return;
        }

        int sheetIdx = IndexOf(sheets, def.IconSheet);
        string preview = sheetIdx >= 0 ? sheets[sheetIdx] : "(pick a sheet)";
        if (BeginComboWide("Icon Sheet", preview))
        {
            for (int s = 0; s < sheets.Count; s++)
                if (ImGui.Selectable(sheets[s], s == sheetIdx))
                {
                    def.IconSheet = sheets[s];
                    _pickerSheet = sheets[s];
                    _dirty = true;
                }
            ImGui.EndCombo();
        }

        if (sheetIdx < 0) return;

        int cols = def.IconCols, rows = def.IconRows;
        ImGui.SetNextItemWidth(110);
        if (ImGui.DragInt("Icon Cols", ref cols, 0.1f, 1, 64)) { def.IconCols = Math.Clamp(cols, 1, 64); _dirty = true; }
        ImGui.SetNextItemWidth(110);
        if (ImGui.DragInt("Icon Rows", ref rows, 0.1f, 1, 64)) { def.IconRows = Math.Clamp(rows, 1, 64); _dirty = true; }

        // Live preview of the CURRENT icon (large, 72px) beside the picker.
        DrawIconPicker(def);
    }

    /// <summary>Visual grid over the selected icon sheet: hover highlights the cell,
    /// click assigns (IconCol, IconRow). Mirrors the tileset palette UX.</summary>
    private void DrawIconPicker(InventorySystem.ItemDef def)
    {
        if (!IDEBridge.TryGetSpriteSheetTexture(def.IconSheet, out uint tex, out int _, out int _)
            || tex == 0)
        {
            ImGui.TextDisabled("(sheet texture not loaded — check the Sprite Editor)");
            return;
        }
        if (_pickerSheet != def.IconSheet) _pickerSheet = def.IconSheet;

        const float cell = 34f; // BIG cells — readable at editor zoom
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var mouse = ImGui.GetIO().MousePos;
        _pickerHoverCell = -1;

        for (int r = 0; r < def.IconRows; r++)
        {
            for (int c = 0; c < def.IconCols; c++)
            {
                var p0 = pos + new Vector2(c * cell, r * cell);
                var p1 = p0 + new Vector2(cell, cell);
                float u0 = c / (float)def.IconCols, u1 = (c + 1) / (float)def.IconCols;
                // HUD/GL sheet textures upload top-row-first → v0 = TOP of the cell.
                float v0 = r / (float)def.IconRows;
                float v1 = (r + 1) / (float)def.IconRows;
                dl.AddImage((nint)tex, p0, p1, new Vector2(u0, v0), new Vector2(u1, v1));
                bool hovered = mouse.X >= p0.X && mouse.X < p1.X && mouse.Y >= p0.Y && mouse.Y < p1.Y;
                if (hovered) _pickerHoverCell = r * def.IconCols + c;
                bool selected = c == def.IconCol && r == def.IconRow;
                if (selected || hovered)
                    dl.AddRect(p0, p1, ImGui.ColorConvertFloat4ToU32(
                        selected ? new Vector4(1f, 0.85f, 0.2f, 1f) : new Vector4(1f, 1f, 1f, 0.8f)), 2f, 0f, 2.5f);
            }
        }
        float gridW = def.IconCols * cell, gridH = def.IconRows * cell;
        ImGui.Dummy(new Vector2(gridW, gridH));

        // Click anywhere on the grid → assign the hovered cell.
        if (_pickerHoverCell >= 0 && ImGui.IsItemHovered() && ImGui.IsMouseClicked(0))
        {
            def.IconCol = _pickerHoverCell % def.IconCols;
            def.IconRow = _pickerHoverCell / def.IconCols;
            _dirty = true;
        }
        ImGui.SameLine();
        // Status beside the grid.
        ImGui.BeginGroup();
        ImGui.Text($"Selected cell:  ({def.IconCol}, {def.IconRow})");
        ImGui.TextDisabled("Click a cell above to pick the icon.");
        // Large preview of the chosen icon.
        float pv = 72f;
        var pvPos = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(pvPos, pvPos + new Vector2(pv), ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.10f, 0.15f, 1f)), 6f);
        dl.AddRect(pvPos, pvPos + new Vector2(pv), ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.36f, 0.45f, 1f)), 6f, 0f, 1.5f);
        {
            float u0 = def.IconCol / (float)def.IconCols, u1 = (def.IconCol + 1) / (float)def.IconCols;
            float v0 = def.IconRow / (float)def.IconRows, v1 = (def.IconRow + 1) / (float)def.IconRows;
            dl.AddImage((nint)tex, pvPos + new Vector2(4), pvPos + new Vector2(pv - 4),
                new Vector2(u0, v0), new Vector2(u1, v1));
        }
        ImGui.Dummy(new Vector2(pv, pv));
        ImGui.EndGroup();
    }

    private void DrawEquipSection(InventorySystem.ItemDef def)
    {
        int slotIdx = Array.IndexOf(InventorySystem.EquipSlots, def.EquipSlot);
        string slotPreview = slotIdx >= 0 ? InventorySystem.EquipSlots[slotIdx] : "(not equippable)";
        if (BeginComboWide("Equip Slot", slotPreview))
        {
            if (ImGui.Selectable("(not equippable)", slotIdx < 0))
            { def.EquipSlot = ""; _dirty = true; }
            for (int s = 0; s < InventorySystem.EquipSlots.Length; s++)
                if (ImGui.Selectable(InventorySystem.EquipSlots[s], s == slotIdx))
                { def.EquipSlot = InventorySystem.EquipSlots[s]; _dirty = true; }
            ImGui.EndCombo();
        }

        if (string.IsNullOrEmpty(def.EquipSlot))
        {
            ImGui.TextDisabled("Pick an equip slot to unlock stat bonuses.");
            return;
        }

        float bh = def.BonusHealth, bm = def.BonusMana, bd = def.BonusDefense, bdm = def.BonusDamage;
        if (ImGui.DragFloat("Bonus HP", ref bh, 0.5f)) { def.BonusHealth = bh; _dirty = true; }
        if (ImGui.DragFloat("Bonus MP", ref bm, 0.5f)) { def.BonusMana = bm; _dirty = true; }
        if (ImGui.DragFloat("Bonus DEF", ref bd, 0.5f)) { def.BonusDefense = bd; _dirty = true; }
        if (ImGui.DragFloat("Bonus ATK", ref bdm, 0.5f)) { def.BonusDamage = bdm; _dirty = true; }

        // Equipped art (sprite drawn over the character) — same registry as portals.
        var sheets = IDEBridge.GetSpriteSheetNames();
        int esIdx = IndexOf(sheets, def.EquipSheet);
        string esPreview = esIdx >= 0 ? sheets[esIdx] : "(no equip art)";
        if (BeginComboWide("Equip Art Sheet", esPreview))
        {
            if (ImGui.Selectable("(no equip art)", esIdx < 0))
            { def.EquipSheet = ""; def.EquipClip = ""; _dirty = true; }
            for (int s = 0; s < sheets.Count; s++)
                if (ImGui.Selectable(sheets[s], s == esIdx))
                { def.EquipSheet = sheets[s]; def.EquipClip = ""; _dirty = true; }
            ImGui.EndCombo();
        }
        if (!string.IsNullOrEmpty(def.EquipSheet))
        {
            var clips = IDEBridge.GetClipNames(def.EquipSheet);
            if (clips.Count > 0)
            {
                int cIdx = clips.IndexOf(def.EquipClip);
                string cPreview = cIdx >= 0 ? clips[cIdx] : "(pick clip)";
                if (BeginComboWide("Equip Art Clip", cPreview))
                {
                    for (int c = 0; c < clips.Count; c++)
                        if (ImGui.Selectable(clips[c], c == cIdx))
                        { def.EquipClip = clips[c]; _dirty = true; }
                    ImGui.EndCombo();
                }
            }
            float wh = def.EquipWorldHeight;
            if (ImGui.DragFloat("Equip Art Height", ref wh, 0.05f, 0f, 10f))
            { def.EquipWorldHeight = Math.Max(0f, wh); _dirty = true; }
        }
    }

    private void DrawUseSection(InventorySystem.ItemDef def)
    {
        string[] useEffects = ["", "Heal", "Mana", "GiveGold"];
        int ueIdx = Array.IndexOf(useEffects, def.UseEffect);
        string uePreview = ueIdx >= 0
            ? (useEffects[ueIdx].Length == 0 ? "(not consumable)" : useEffects[ueIdx])
            : def.UseEffect;
        // ##-suffixed label: the section header above is ALSO "Use Effect" — identical
        // visible labels = same ImGui ID = ID-conflict assert.
        if (BeginComboWide("Type##usefx", uePreview))
        {
            for (int u = 0; u < useEffects.Length; u++)
                if (ImGui.Selectable(useEffects[u].Length == 0 ? "(not consumable)" : useEffects[u], u == ueIdx))
                { def.UseEffect = useEffects[u]; _dirty = true; }
            ImGui.EndCombo();
        }
        if (!string.IsNullOrEmpty(def.UseEffect))
        {
            float amt = def.UseAmount;
            if (ImGui.DragFloat("Amount##usefx", ref amt, 0.5f, 0f, 9999f))
            { def.UseAmount = Math.Max(0f, amt); _dirty = true; }
        }

        int fxIdx = Array.IndexOf(Effect2DSystem.Presets, def.UseFxPreset);
        string fxPreview = fxIdx >= 0 ? Effect2DSystem.Presets[fxIdx] : "(no FX)";
        if (BeginComboWide("Particle FX##usefx", fxPreview))
        {
            if (ImGui.Selectable("(no FX)", fxIdx < 0)) { def.UseFxPreset = ""; _dirty = true; }
            for (int p = 0; p < Effect2DSystem.Presets.Length; p++)
                if (ImGui.Selectable(Effect2DSystem.Presets[p], p == fxIdx))
                { def.UseFxPreset = Effect2DSystem.Presets[p]; _dirty = true; }
            ImGui.EndCombo();
        }
    }

    // ═══════════════════════ Small helpers ═══════════════════════

    /// <summary>Combo whose preview fills the label width (240) — no squished text.</summary>
    private static bool BeginComboWide(string label, string preview)
    {
        ImGui.SetNextItemWidth(240);
        return ImGui.BeginCombo(label, preview);
    }

    private static int IndexOf(List<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }

    /// <summary>RAAI-style enable/disable scope — only ends what it began
    /// (EndDisabled without a matching Begin = native assertion crash).</summary>
    private struct ImScopeDisabled : IDisposable
    {
        private bool _active;
        public ImScopeDisabled(bool disabled)
        {
            _active = disabled;
            if (_active) ImGui.BeginDisabled();
        }
        public void Dispose()
        {
            if (_active) { ImGui.EndDisabled(); _active = false; }
        }
    }
}
