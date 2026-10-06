using System.Collections.Generic;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Objects;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Inventory system (phase 2): item catalog, a 36-slot GRID inventory with stacking
/// and drag-drop moves, gold currency, and the equipment model (paperdoll slots) for
/// Player2D / NPC / enemy characters — plus a simple hotbar (first 9 grid slots).
///
/// Item ICONS are cropped straight from any tileset/sprite-sheet texture (columns ×
/// rows grid + per-item cell) — no dedicated icon assets required. The catalog
/// persists per project as Assets/Items/items.json; the runtime grid/equipment/gold
/// persist through SaveData (save slots).
/// </summary>
public static class InventorySystem
{
    // ═══════════════════════ Equipment slots (paperdoll) ═══════════════════════

    public static readonly string[] EquipSlots =
        ["Head", "Body", "Legs", "Weapon", "Shield", "Hair", "Shoes",
         "Head Accessories 1", "Head Accessories 2",
         "Body Accessories 1", "Body Accessories 2",
         "Legs Accessories 1", "Legs Accessories 2"];

    // NOTE: slot "Accessory" DIHAPUS (permintaan user — diganti slot accessories
    // spesifik di atas). Item lama dengan EquipSlot="Accessory" TETAP berfungsi:
    // Equipment.Equip tidak memvalidasi terhadap array ini (slot = custom key) dan
    // CollectEquipmentLayersForDraw me-render slot di luar paperdoll via union.

    // ═══════════════════════ Item definition ═══════════════════════

    /// <summary>One item type. Icons crop from a tileset grid; equipment items map to
    /// one EquipSlot and (optionally) an equipped sprite drawn OVER the character.</summary>
    public sealed class ItemDef
    {
        public string Id = "";
        public string Name = "";
        /// <summary>Icon source: any registered tileset/sprite-sheet texture name
        /// (Sprite Editor sheets and Map Editor tilesets both resolve).</summary>
        public string IconSheet = "";
        /// <summary>Grid size of the icon sheet (the item crops cell (IconCol,IconRow)).</summary>
        public int IconCols = 8;
        public int IconRows = 8;
        public int IconCol;
        public int IconRow;
        public int MaxStack = 99;
        /// <summary>"" = not equippable; else one of EquipSlots.</summary>
        public string EquipSlot = "";
        /// <summary>Stat bonuses applied while equipped (HP+5, MP-1 style).</summary>
        public float BonusHealth;
        public float BonusMana;
        public float BonusDefense;
        public float BonusDamage;
        /// <summary>Optional sprite-sheet art shown over the character when equipped
        /// (same sheet/clip registry as portals/sprites). Empty = icon-only.</summary>
        public string EquipSheet = "";
        public string EquipClip = "";
        /// <summary>Equipped-art height in world units (0 = full base-sprite height —
        /// full-canvas art aligns 1:1 with the character).</summary>
        public float EquipWorldHeight = 0f;
        /// <summary>Render layer offset for the equipped art (bigger = in front).
        /// (Legacy field — superseded by <see cref="EquipLayer"/>; not consumed.)</summary>
        public float EquipZOffset = 0.09f;
        /// <summary>Equipment LAYER system: when true (and Equip Art Sheet is set) the
        /// item's sprite renders as a layer ON the character while equipped (baju,
        /// celana, shield... follow the base animation). False = stat-only, no art.</summary>
        public bool IsEquipment = true;
        /// <summary>Stacking order among equipped art layers on one character — higher
        /// draws IN FRONT (armor 1 over shirt 0); negative renders BEHIND the base
        /// sprite (capes/back items). Two items on the same layer stack in paperdoll
        /// order (Head→Body→Legs→Weapon→Shield→Hair→Shoes→accessory slots).</summary>
        public int EquipLayer;
        /// <summary>World-unit nudge for the equipped art from the character's
        /// bottom-center (X mirrors with facing).</summary>
        public float EquipOffsetX;
        public float EquipOffsetY;
        /// <summary>Frame SYNC with the base sprite: when true the overlay samples its
        /// sheet at the BASE sprite's current sheet-frame index (clothing authored on
        /// the same grid as the character follows every pose 1:1). False = the overlay
        /// animates on its own clip FPS (independent loops like fire auras).</summary>
        public bool EquipSyncFrame = true;
        /// <summary>Optional particle FX preset fired when the item is used/equipped.</summary>
        public string UseFxPreset = "";
        /// <summary>Use effect: "Heal" / "Mana" / "GiveGold" (UseAmount decides how
        /// much; empty = not consumable).</summary>
        public string UseEffect = "";
        public float UseAmount;
        /// <summary>Shop price (inventory phase 2 UI shows it; vendor logic later).</summary>
        public int Price;
        public string Notes = "";
    }

    /// <summary>All registered item types (Item Editor panel authors this).</summary>
    public static readonly List<ItemDef> Items = new();

    private static readonly Dictionary<string, ItemDef> _byId = new();

    /// <summary>Register one item (or replace an existing definition with the same id).</summary>
    public static ItemDef Register(ItemDef def)
    {
        if (_byId.TryGetValue(def.Id, out var existing))
        {
            int idx = Items.IndexOf(existing);
            if (idx >= 0) Items[idx] = def;
            _byId[def.Id] = def;
            return def;
        }
        Items.Add(def);
        _byId[def.Id] = def;
        return def;
    }

    public static ItemDef? Find(string id) =>
        id != null && _byId.TryGetValue(id, out var d) ? d : null;

    /// <summary>Remove an item from the catalog (Item Editor delete button).</summary>
    public static void RemoveFromCatalog(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (_byId.TryGetValue(id, out var existing))
        {
            Items.Remove(existing);
            _byId.Remove(id);
        }
    }

    /// <summary>Resolve the GPU texture of an item's icon sheet through the shared
    /// IDEBridge registry (tilesets + sprite sheets). Returns 0 when not found.</summary>
    public static uint GetIconTexture(ItemDef def) =>
        def != null && !string.IsNullOrWhiteSpace(def.IconSheet)
        && DarkEngine3D_gl_csharp.Engine.IDE.IDEBridge.TryGetSpriteSheetTexture(def.IconSheet, out uint tex, out int _, out int _)
            ? tex : 0u;

    /// <summary>UV rect of an item's icon cell in IMAGE space: v grows DOWNWARD
    /// (vTop = row/rows = TOP edge of the cell, vBottom = (row+1)/rows). Sheet
    /// textures upload stb top-row-first at v=0 (confirmed by the icon picker, which
    /// displays correctly with the direct mapping — the old "1 −" flip here made the
    /// saved icon select a DIFFERENT cell than the picker showed).
    /// Consumers: world-batch PushQuad wants (u0, vBOTTOM, u1, vTOP) at its
    /// (bl/br, tl/tr) corners; HUD.DrawImageUV + ImGui.Image want (u0, vTOP, u1,
    /// vBOTTOM).</summary>
    public static (float u0, float vTop, float u1, float vBottom) GetIconUV(ItemDef def)
    {
        int cols = def.IconCols > 0 ? def.IconCols : 8;
        int rows = def.IconRows > 0 ? def.IconRows : 8;
        int col = Math.Clamp(def.IconCol, 0, cols - 1);
        int row = Math.Clamp(def.IconRow, 0, rows - 1);
        float u0 = col / (float)cols, u1 = (col + 1) / (float)cols;
        // Grid row 0 = TOP of the image (stb upload row order → v = row fraction).
        float vTop = row / (float)rows;
        float vBottom = (row + 1) / (float)rows;
        return (u0, vTop, u1, vBottom);
    }

    // ═══════════════════════ Grid inventory (phase 2) ═══════════════════════

    public const int Columns = 9;
    public const int Rows = 4;
    public const int GridSize = Columns * Rows; // 36 slots

    /// <summary>One grid slot: item id ("" = empty) + stack count.</summary>
    public struct Slot
    {
        public string ItemId;
        public int Count;
        public readonly bool IsEmpty => string.IsNullOrEmpty(ItemId) || Count <= 0;
        public static readonly Slot Empty = new() { ItemId = "", Count = 0 };
    }

    /// <summary>The player's grid inventory (index = row * Columns + col).</summary>
    public static readonly Slot[] Grid = new Slot[GridSize];

    /// <summary>Gold counter (auto-consume currency — the GiveGold use-effect and
    /// future vendor/shop logic both read this).</summary>
    public static int Gold;

    /// <summary>Legacy session bag (Give Item trigger before phase 2). Kept in sync:
    /// AddItem deposits grid-first, legacy-bag as fallback when the grid is full.</summary>
    public static readonly Dictionary<string, int> PlayerBag = new();

    public static void ResetSession()
    {
        for (int i = 0; i < GridSize; i++) Grid[i] = Slot.Empty;
        Gold = 0;
        PlayerBag.Clear();
        _dragSlot = -1;
        _selectedSlot = -1;
        ClearDrops(); // fresh session → no leftover world loot
    }

    /// <summary>Current drag source slot for the UI (−1 = none). Lives here so the
    /// drag survives panel close/reopen inside one session.</summary>
    public static int DragSlot { get => _dragSlot; set => _dragSlot = value; }
    private static int _dragSlot = -1;
    /// <summary>UI-selected slot (tooltip/click target highlight).</summary>
    public static int SelectedSlot { get => _selectedSlot; set => _selectedSlot = value; }
    private static int _selectedSlot = -1;

    /// <summary>Add items into the grid (merges stacks first, then empty slots).
    /// Returns the amount actually added. When the grid is full the legacy bag
    /// absorbs the remainder so Give Item triggers never silently lose items.</summary>
    public static int AddItem(string id, int amount)
    {
        if (string.IsNullOrWhiteSpace(id) || amount <= 0) return 0;
        var def = Find(id);
        int maxStack = def?.MaxStack is int m && m > 0 ? m : 99;
        int remaining = amount, added = 0;

        // Pass 1: top up existing stacks.
        for (int i = 0; i < GridSize && remaining > 0; i++)
        {
            if (Grid[i].IsEmpty || Grid[i].ItemId != id) continue;
            int space = maxStack - Grid[i].Count;
            if (space <= 0) continue;
            int put = Math.Min(space, remaining);
            Grid[i].Count += put;
            remaining -= put;
            added += put;
        }
        // Pass 2: fill empty slots.
        for (int i = 0; i < GridSize && remaining > 0; i++)
        {
            if (!Grid[i].IsEmpty) continue;
            int put = Math.Min(maxStack, remaining);
            Grid[i] = new Slot { ItemId = id, Count = put };
            remaining -= put;
            added += put;
        }
        // Legacy bag absorbs the overflow (session-only; survives while playing).
        if (remaining > 0)
        {
            PlayerBag.TryGetValue(id, out int c);
            PlayerBag[id] = c + remaining;
            added += remaining;
        }
        return added;
    }

    public static int Count(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        int n = 0;
        for (int i = 0; i < GridSize; i++)
            if (!Grid[i].IsEmpty && Grid[i].ItemId == id) n += Grid[i].Count;
        PlayerBag.TryGetValue(id, out int c);
        return n + c;
    }

    /// <summary>Remove up to <paramref name="amount"/> from the grid (then the legacy
    /// bag); returns how many were removed.</summary>
    public static int RemoveItem(string id, int amount)
    {
        if (id == null || amount <= 0) return 0;
        int remaining = amount, removed = 0;
        for (int i = GridSize - 1; i >= 0 && remaining > 0; i--) // consume from the tail
        {
            if (Grid[i].IsEmpty || Grid[i].ItemId != id) continue;
            int take = Math.Min(Grid[i].Count, remaining);
            Grid[i].Count -= take;
            remaining -= take;
            removed += take;
            if (Grid[i].Count <= 0) Grid[i] = Slot.Empty;
        }
        if (remaining > 0 && PlayerBag.TryGetValue(id, out int c))
        {
            int take = Math.Min(c, remaining);
            if (c - take <= 0) PlayerBag.Remove(id); else PlayerBag[id] = c - take;
            remaining -= take;
            removed += take;
        }
        return removed;
    }

    /// <summary>Move/merge one grid slot onto another (drag & drop). Same item +
    /// stack room = merge; otherwise SWAP. Returns true when the layout changed.</summary>
    public static bool MoveSlot(int from, int to)
    {
        if (from < 0 || to < 0 || from >= GridSize || to >= GridSize || from == to)
            return false;
        if (Grid[from].IsEmpty) return false;
        var src = Grid[from];

        if (Grid[to].IsEmpty)
        {
            Grid[to] = src;
            Grid[from] = Slot.Empty;
            return true;
        }
        var def = Find(src.ItemId);
        int maxStack = def?.MaxStack is int m && m > 0 ? m : 99;
        if (Grid[to].ItemId == src.ItemId && Grid[to].Count < maxStack)
        {
            int space = maxStack - Grid[to].Count;
            int put = Math.Min(space, src.Count);
            Grid[to].Count += put;
            src.Count -= put;
            Grid[from] = src.Count > 0 ? src : Slot.Empty;
            return true;
        }
        // Swap.
        Grid[from] = Grid[to];
        Grid[to] = src;
        return true;
    }

    // ═══════════════════════ Per-character equipment ═══════════════════════

    /// <summary>Equipped state for one character (player, NPC, enemy). Items are
    /// referenced by id (resolved through the catalog) — survives serialization.</summary>
    public sealed class Equipment
    {
        /// <summary>Slot name → item id ("" = empty).</summary>
        public readonly Dictionary<string, string> Slots = new();

        public string Get(string slot) =>
            slot != null && Slots.TryGetValue(slot, out var id) ? id : "";

        /// <summary>Equip an item; returns the previously equipped id from that slot
        /// ("" when empty). Returns null when the item/slot is invalid.</summary>
        public string? Equip(string itemId)
        {
            var def = Find(itemId);
            if (def == null || string.IsNullOrEmpty(def.EquipSlot)) return null;
            string old = Get(def.EquipSlot);
            Slots[def.EquipSlot] = itemId;
            return old;
        }

        /// <summary>Unequip a slot; returns the removed id ("" when it was empty).</summary>
        public string Unequip(string slot)
        {
            string old = Get(slot);
            if (slot != null) Slots.Remove(slot);
            return old;
        }

        /// <summary>Summed stat bonuses across every equipped item.</summary>
        public (float hp, float mp, float def, float dmg) Totals()
        {
            float hp = 0, mp = 0, df = 0, dm = 0;
            foreach (var id in Slots.Values)
            {
                var d = Find(id);
                if (d == null) continue;
                hp += d.BonusHealth; mp += d.BonusMana; df += d.BonusDefense; dm += d.BonusDamage;
            }
            return (hp, mp, df, dm);
        }
    }

    /// <summary>The single-player session equipment (paperdoll). UI + save paths
    /// target this; per-NPC equipment stays on <see cref="EditorObject.Equipment"/>.</summary>
    public static readonly Equipment PlayerEquipment = new();

    /// <summary>Equip from a grid slot: old equipped item returns to the inventory
    /// (auto-stack), stat bonuses apply immediately to Player2DStats maxima + current
    /// (so the HUD bars grow with the bonus). Returns false when the slot is empty
    /// or the item is not equippable.</summary>
    public static bool EquipFromGrid(int gridIndex, Equipment? equip = null)
    {
        var eq = equip ?? PlayerEquipment;
        if (gridIndex < 0 || gridIndex >= GridSize) return false;
        var slot = Grid[gridIndex];
        if (slot.IsEmpty) return false;
        var def = Find(slot.ItemId);
        if (def == null || string.IsNullOrEmpty(def.EquipSlot)) return false;

        string? previous = eq.Equip(def.Id);
        // Consume exactly ONE from the stack; the replaced item goes back.
        RemoveItem(def.Id, 1);
        if (!string.IsNullOrEmpty(previous))
            AddItem(previous, 1);

        ApplyEquipmentBonuses(eq);
        if (!string.IsNullOrEmpty(def.UseFxPreset))
            Effect2DSystem.SpawnBurst(def.UseFxPreset, new Vector3(PlayerFeetX, PlayerFeetY + 1f, 0f), 0.7f);
        Console.WriteLine($"[Inventory] Equipped '{def.Name}' → {def.EquipSlot}" +
            (previous.Length > 0 ? $" (swapped out '{previous}')" : ""));
        return true;
    }

    /// <summary>Unequip a paperdoll slot back into the grid (finds room; fails only
    /// when the grid is completely full) and re-apply stat bonuses.</summary>
    public static bool UnequipToGrid(string slotName, Equipment? equip = null)
    {
        var eq = equip ?? PlayerEquipment;
        string id = eq.Unequip(slotName);
        if (string.IsNullOrEmpty(id)) return false;
        int added = AddItem(id, 1);
        if (added == 0)
        {
            eq.Equip(id); // grid full → put it back on, nothing lost
            return false;
        }
        ApplyEquipmentBonuses(eq);
        return true;
    }

    /// <summary>Recompute the equipment bonus totals and push them into
    /// Player2DStats (both the maxima AND current values, so bars visibly grow).
    /// Editor stat edits survive: bonuses re-derive from the catalog each call.</summary>
    public static void ApplyEquipmentBonuses(Equipment? equip = null)
    {
        var eq = equip ?? PlayerEquipment;
        var (hp, mp, df, dm) = eq.Totals();
        // First call before any SnapshotBaseStats (session start missed) → capture
        // the current maxima as base so the maxima never collapse to just the bonus.
        if (_baseHpMax < 0f) SnapshotBaseStats();
        // Store raw bonus totals for UI display; keep stats consistent by adjusting
        // the maxima relative to the BASE values (base = max − previous bonus).
        BonusHp = hp; BonusMp = mp; BonusDef = df; BonusDmg = dm;
        Player2DStats.HealthMax = MathF.Max(1f, _baseHpMax + hp);
        Player2DStats.ManaMax = MathF.Max(1f, _baseMpMax + mp);
        Player2DStats.Health = Math.Clamp(Player2DStats.Health, 0f, Player2DStats.HealthMax);
        Player2DStats.Mana = Math.Clamp(Player2DStats.Mana, 0f, Player2DStats.ManaMax);
        Player2DStats.ClampAll();
        DefenseBonus = df; DamageBonus = dm;
    }

    private static float _baseHpMax = -1f, _baseMpMax = -1f;
    /// <summary>Current summed equipment bonuses (HUD/UI readouts).</summary>
    public static float BonusHp { get; private set; }
    public static float BonusMp { get; private set; }
    public static float BonusDef { get; private set; }
    public static float BonusDmg { get; private set; }
    /// <summary>Defense/damage bonuses as gameplay modifiers (projectile system and
    /// future combat read these).</summary>
    public static float DefenseBonus { get; private set; }
    public static float DamageBonus { get; private set; }

    /// <summary>Snapshot the stats' current maxima as the "base" before the first
    /// bonus application (call at session start, before any equipment exists).</summary>
    public static void SnapshotBaseStats()
    {
        _baseHpMax = Player2DStats.HealthMax;
        _baseMpMax = Player2DStats.ManaMax;
    }

    // ═══════════════════════ Use item ═══════════════════════

    /// <summary>Player feet position (refreshed by the runtime each frame; used to
    /// anchor use/equip FX bursts). Editor mode: camera center fallback.</summary>
    public static float PlayerFeetX { get; set; }
    public static float PlayerFeetY { get; set; }

    /// <summary>Consume/use one item from a grid slot (Heal / Mana / GiveGold
    /// effects + optional FX burst). Returns a user-facing result string ("" when
    /// the slot is empty or the item is not usable).</summary>
    public static string UseSlot(int gridIndex)
    {
        if (gridIndex < 0 || gridIndex >= GridSize) return "";
        var slot = Grid[gridIndex];
        if (slot.IsEmpty) return "";
        var def = Find(slot.ItemId);
        if (def == null) return "";

        if (string.IsNullOrEmpty(def.UseEffect))
        {
            // Not consumable — equippable items equip instead (hotbar convenience).
            if (!string.IsNullOrEmpty(def.EquipSlot))
                return EquipFromGrid(gridIndex) ? $"Equipped {def.Name}" : "Cannot equip";
            return "";
        }

        string result;
        switch (def.UseEffect)
        {
            case "Heal":
                Player2DStats.Health = MathF.Min(Player2DStats.HealthMax, Player2DStats.Health + def.UseAmount);
                result = $"+{def.UseAmount:0} HP";
                break;
            case "Mana":
                Player2DStats.Mana = MathF.Min(Player2DStats.ManaMax, Player2DStats.Mana + def.UseAmount);
                result = $"+{def.UseAmount:0} MP";
                break;
            case "GiveGold":
                Gold += (int)def.UseAmount;
                result = $"+{def.UseAmount:0} gold";
                break;
            default:
                return "";
        }
        RemoveItem(def.Id, 1);
        if (!string.IsNullOrEmpty(def.UseFxPreset))
            Effect2DSystem.SpawnBurst(def.UseFxPreset,
                new Vector3(PlayerFeetX, PlayerFeetY + 1.2f, 0f), 0.8f);
        Console.WriteLine($"[Inventory] Used '{def.Name}' → {result}");
        return $"{def.Name}: {result}";
    }

    // ═══════════════════════ Save / restore (SaveData slots) ═══════════════════════

    /// <summary>Snapshot the grid + paperdoll + gold for a save slot. Format:
    /// "index|itemId|count" per grid entry, "slot|itemId" per equipment entry.</summary>
    public static (List<string> grid, List<string> equip, int gold) CaptureState()
    {
        var grid = new List<string>();
        for (int i = 0; i < GridSize; i++)
            if (!Grid[i].IsEmpty)
                grid.Add($"{i}|{Grid[i].ItemId}|{Grid[i].Count}");
        var equip = new List<string>();
        foreach (var kv in PlayerEquipment.Slots)
            equip.Add($"{kv.Key}|{kv.Value}");
        return (grid, equip, Gold);
    }

    /// <summary>Restore grid + paperdoll + gold from a save slot (replaces state).</summary>
    public static void RestoreState(List<string>? grid, List<string>? equip, int gold)
    {
        ResetSession();
        Gold = Math.Max(0, gold);
        if (grid != null)
            foreach (var entry in grid)
            {
                var parts = entry.Split('|');
                if (parts.Length < 3) continue;
                if (!int.TryParse(parts[0], out int idx) || idx < 0 || idx >= GridSize) continue;
                if (!int.TryParse(parts[2], out int cnt) || cnt <= 0) continue;
                Grid[idx] = new Slot { ItemId = parts[1], Count = cnt };
            }
        if (equip != null)
            foreach (var entry in equip)
            {
                var parts = entry.Split('|');
                if (parts.Length < 2) continue;
                PlayerEquipment.Slots[parts[0]] = parts[1];
            }
        ApplyEquipmentBonuses();
    }

    // ═══════════════════════ Catalog persistence (items.json) ═══════════════════════

    /// <summary>items.json inside the active project (Assets/Items/).</summary>
    public static string CatalogPath =>
        System.IO.Path.Combine(
            DarkEngine3D_gl_csharp.Engine.Project.ProjectManager.AssetsDir, "Items", "items.json");

    private sealed class CatalogFile
    {
        public List<CatalogItem> Items { get; set; } = new();
    }

    private sealed class CatalogItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string IconSheet { get; set; } = "";
        public int IconCols { get; set; } = 8;
        public int IconRows { get; set; } = 8;
        public int IconCol { get; set; }
        public int IconRow { get; set; }
        public int MaxStack { get; set; } = 99;
        public string EquipSlot { get; set; } = "";
        public float BonusHealth { get; set; }
        public float BonusMana { get; set; }
        public float BonusDefense { get; set; }
        public float BonusDamage { get; set; }
        public string EquipSheet { get; set; } = "";
        public string EquipClip { get; set; } = "";
        public float EquipWorldHeight { get; set; }
        public float EquipZOffset { get; set; } = 0.09f;
        public bool IsEquipment { get; set; } = true;
        public int EquipLayer { get; set; }
        public float EquipOffsetX { get; set; }
        public float EquipOffsetY { get; set; }
        public bool EquipSyncFrame { get; set; } = true;
        public string UseFxPreset { get; set; } = "";
        public string UseEffect { get; set; } = "";
        public float UseAmount { get; set; }
        public int Price { get; set; }
        public string Notes { get; set; } = "";
    }

    private static CatalogItem ToFile(ItemDef d) => new()
    {
        Id = d.Id, Name = d.Name, IconSheet = d.IconSheet,
        IconCols = d.IconCols, IconRows = d.IconRows, IconCol = d.IconCol, IconRow = d.IconRow,
        MaxStack = d.MaxStack, EquipSlot = d.EquipSlot,
        BonusHealth = d.BonusHealth, BonusMana = d.BonusMana,
        BonusDefense = d.BonusDefense, BonusDamage = d.BonusDamage,
        EquipSheet = d.EquipSheet, EquipClip = d.EquipClip,
        EquipWorldHeight = d.EquipWorldHeight, EquipZOffset = d.EquipZOffset,
        IsEquipment = d.IsEquipment, EquipLayer = d.EquipLayer,
        EquipOffsetX = d.EquipOffsetX, EquipOffsetY = d.EquipOffsetY,
        EquipSyncFrame = d.EquipSyncFrame,
        UseFxPreset = d.UseFxPreset, UseEffect = d.UseEffect, UseAmount = d.UseAmount,
        Price = d.Price, Notes = d.Notes,
    };

    private static ItemDef FromFile(CatalogItem c) => new()
    {
        Id = c.Id, Name = c.Name, IconSheet = c.IconSheet,
        IconCols = c.IconCols, IconRows = c.IconRows, IconCol = c.IconCol, IconRow = c.IconRow,
        MaxStack = c.MaxStack, EquipSlot = c.EquipSlot,
        BonusHealth = c.BonusHealth, BonusMana = c.BonusMana,
        BonusDefense = c.BonusDefense, BonusDamage = c.BonusDamage,
        EquipSheet = c.EquipSheet, EquipClip = c.EquipClip,
        EquipWorldHeight = c.EquipWorldHeight, EquipZOffset = c.EquipZOffset,
        IsEquipment = c.IsEquipment, EquipLayer = c.EquipLayer,
        EquipOffsetX = c.EquipOffsetX, EquipOffsetY = c.EquipOffsetY,
        EquipSyncFrame = c.EquipSyncFrame,
        UseFxPreset = c.UseFxPreset, UseEffect = c.UseEffect, UseAmount = c.UseAmount,
        Price = c.Price, Notes = c.Notes,
    };

    /// <summary>Write the catalog to the project (Assets/Items/items.json).</summary>
    public static void SaveCatalog()
    {
        try
        {
            string path = CatalogPath;
            string? dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            var file = new CatalogFile();
            foreach (var d in Items) file.Items.Add(ToFile(d));
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(file, opts));
            Helpers.BinaryObjectCache.TryWrite(path, file); // binary sidecar → next load skips the JSON parse
            Console.WriteLine($"[Inventory] Catalog saved: {Items.Count} items → {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Inventory] Catalog save FAILED: {ex.Message}");
        }
    }

    /// <summary>Load the project's catalog (REPLACES the in-memory list — call on
    /// project open). Creates nothing when the file is missing (fresh project).
    /// After the main file, EVERY other *.json catalog in Assets/Items/ is
    /// auto-merged (see MergeExtraCatalogs).</summary>
    public static void LoadCatalog()
    {
        Items.Clear();
        _byId.Clear();
        try
        {
            string path = CatalogPath;
            if (System.IO.File.Exists(path))
            {
                // Binary sidecar fast path — falls back to the JSON parse on any
                // miss/staleness/corruption (JSON remains the source of truth).
                var file = Helpers.BinaryObjectCache.TryLoad<CatalogFile>(path)
                    ?? System.Text.Json.JsonSerializer.Deserialize<CatalogFile>(
                        System.IO.File.ReadAllText(path));
                if (file?.Items != null)
                    foreach (var c in file.Items)
                    {
                        if (string.IsNullOrWhiteSpace(c.Id)) continue;
                        Register(FromFile(c));
                    }
                Console.WriteLine($"[Inventory] Catalog loaded: {Items.Count} items.");
            }
            else
            {
                Console.WriteLine("[Inventory] No items.json in project — empty catalog.");
            }

            MergeExtraCatalogs(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Inventory] Catalog load FAILED: {ex.Message}");
        }
    }

    /// <summary>Auto-load: merge EVERY other *.json file in Assets/Items/ (top-level,
    /// items.json excluded) as an extra item catalog — same idea as the sprite-anim
    /// auto-load. Files whose Items array is missing/empty (or invalid JSON) are
    /// skipped; ids already registered are KEPT as-is (the main items.json wins).
    /// Saving the catalog writes the merged list back to items.json.</summary>
    private static void MergeExtraCatalogs(string mainPath)
    {
        string dir;
        try { dir = System.IO.Path.GetDirectoryName(mainPath) ?? ""; }
        catch { return; }
        if (dir.Length == 0 || !System.IO.Directory.Exists(dir)) return;

        int files = 0, added = 0;
        foreach (string f in System.IO.Directory.GetFiles(dir, "*.json",
                     System.IO.SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (string.Equals(System.IO.Path.GetFullPath(f),
                        System.IO.Path.GetFullPath(mainPath), StringComparison.OrdinalIgnoreCase))
                    continue; // main catalog already loaded above

                var file = Helpers.BinaryObjectCache.TryLoad<CatalogFile>(f)
                    ?? System.Text.Json.JsonSerializer.Deserialize<CatalogFile>(
                        System.IO.File.ReadAllText(f));
                if (file?.Items == null || file.Items.Count == 0) continue; // not a catalog
                files++;
                foreach (var c in file.Items)
                {
                    if (string.IsNullOrWhiteSpace(c.Id)) continue;
                    if (Find(c.Id) != null) continue; // already present — keep existing
                    Register(FromFile(c));
                    added++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Inventory] Auto-load skip {System.IO.Path.GetFileName(f)}: {ex.Message}");
            }
        }
        if (files > 0 || added > 0)
            Console.WriteLine($"[Inventory] Auto-loaded {added} items from {files} extra catalogs in Items folder.");
    }

    /// <summary>Drop the catalog (project closed).</summary>
    public static void ClearCatalog()
    {
        Items.Clear();
        _byId.Clear();
    }

    // ═══════════════════════ World loot drops (magnet vacuum) ═══════════════════════

    /// <summary>One physical item drop in the world: sits where it spawned (with a
    /// small pop bounce), then accelerates toward the player once inside the magnet
    /// radius and auto-collects on contact. Rendered as an animated world-space icon
    /// quad by RenderLootDrops (shared Effect2D batch).</summary>
    public sealed class LootDrop
    {
        public string ItemId = "";
        public int Count = 1;
        public float X, Y;
        /// <summary>Horizontal drift after the spawn pop (mirrors the drop's facing).</summary>
        public float VelX;
        /// <summary>Vertical velocity of the spawn pop (positive = up).</summary>
        public float VelY;
        public bool Magnetized;
        public float Age;
        /// <summary>Squash-and-stretch phase for the spawn bounce (0 = settled).</summary>
        public float BounceT = 1f;
        /// <summary>Set true by the magnet tick the frame it gets collected (FX +
        /// flash fire here, then the drop is removed).</summary>
        public bool Collected;
    }

    private static readonly List<LootDrop> _drops = new();
    /// <summary>How far (world units) a drop starts chasing the player.</summary>
    public const float MagnetRadius = 2.6f;
    /// <summary>Distance at which a magnetized drop is collected into the inventory.</summary>
    public const float CollectRadius = 0.45f;
    /// <summary>Acceleration (world units/s²) of a magnetized drop toward the player —
    /// starts slow and ramps up, which reads as the "vacuum" pull.</summary>
    public const float MagnetAccel = 22f;
    /// <summary>Velocity cap so drops never outrun the player at high FPS.</summary>
    public const float MagnetMaxSpeed = 11f;

    /// <summary>Live drop list (count for HUD/debug overlays).</summary>
    public static IReadOnlyList<LootDrop> Drops => _drops;

    /// <summary>Spawn a world drop at a world position. The item is NOT added to the
    /// inventory here — only when the magnet vacuum actually delivers it (so walking
    /// away mid-pull leaves the item on the ground).</summary>
    public static void SpawnDrop(string itemId, int count, float x, float y)
    {
        if (string.IsNullOrWhiteSpace(itemId) || count <= 0 || _drops.Count >= 128) return;
        _drops.Add(new LootDrop
        {
            ItemId = itemId,
            Count = count,
            X = x,
            Y = y,
            // Pop: a small hop with a random sideways flick so a pile of loot
            // doesn't spawn as one overlapping stack.
            VelX = (Random.Shared.NextSingle() - 0.5f) * 1.6f,
            VelY = 1.4f + Random.Shared.NextSingle() * 0.6f,
            BounceT = 0f,
        });
    }

    /// <summary>Per-frame magnet simulation (preview/in-game only). Drops pop, settle
    /// on the ground, get pulled into the player inside MagnetRadius, and auto-collect
    /// inside CollectRadius (FX + flash + AddItem). Call AFTER PlayerFeetX/Y refresh.
    /// Ground snap uses the collision tiles so loot rests ON the floor, not mid-air.</summary>
    public static void TickDrops(EditorObjectManager? mgr, Tilemap2D? activeMap, float dt)
    {
        if (dt <= 0f) return;
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            d.Age += dt;
            d.BounceT = MathF.Min(1f, d.BounceT + dt * 3.5f);

            if (!d.Magnetized)
            {
                // Spawn pop physics: light gravity + drift, snap to the floor when
                // the hop lands (collision probe at the drop's position).
                d.VelY -= 9f * dt;
                d.X += d.VelX * dt;
                d.Y += d.VelY * dt;
                d.VelX *= 1f - MathF.Min(1f, 3f * dt); // ground friction
                float floor = FindDropFloor(mgr, activeMap, d.X, d.Y);
                if (d.VelY <= 0f && d.Y <= floor)
                {
                    d.Y = floor;
                    d.VelY = 0f;
                    d.VelX = 0f;
                }
                // Inside the player's pull? Start chasing (feet-centered).
                float dxp = PlayerFeetX - d.X, dyp = PlayerFeetY + 0.5f - d.Y;
                if (dxp * dxp + dyp * dyp <= MagnetRadius * MagnetRadius)
                    d.Magnetized = true;
            }
            else
            {
                // Vacuum: accelerate toward the player, capped speed.
                float dx = PlayerFeetX - d.X, dy = PlayerFeetY + 0.5f - d.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= CollectRadius)
                {
                    CollectDrop(d);
                    _drops.RemoveAt(i);
                    continue;
                }
                float sp = MathF.Min(MagnetMaxSpeed, MagnetAccel * (0.4f + d.Age * 2f));
                d.X += dx / dist * sp * dt;
                d.Y += dy / dist * sp * dt;
            }
        }
    }

    /// <summary>Deliver a drop into the inventory (stack-safe: overflow stays as a
    /// world drop so nothing is silently destroyed).</summary>
    private static void CollectDrop(LootDrop d)
    {
        int added = AddItem(d.ItemId, d.Count);
        if (added < d.Count)
        {
            // Inventory full: keep the remainder on the ground, stop pulling for a
            // moment (drop pops back down) so it doesn't spam the failure every frame.
            d.Count -= Math.Max(0, added);
            d.Magnetized = false;
            d.VelY = 1.2f;
            d.BounceT = 0f;
            if (d.Count <= 0) return;
            FlashPickup(d.ItemId, d.Count, full: true);
            return;
        }
        var def = Find(d.ItemId);
        Effect2DSystem.SpawnBurst("Coin",
            new System.Numerics.Vector3(PlayerFeetX, PlayerFeetY + 0.9f, 0f), 0.5f);
        Effect2DSystem.SpawnBurst("Sparks",
            new System.Numerics.Vector3(PlayerFeetX, PlayerFeetY + 0.9f, 0f), 0.4f);
        FlashPickup(d.ItemId, added, full: false);
        Console.WriteLine($"[Inventory] Loot vacuum: +{added}× {(def?.Name ?? d.ItemId)}");
    }

    private static void FlashPickup(string itemId, int amount, bool full)
    {
        var def = Find(itemId);
        InventoryHud.PushFlash(full ? "Inventory full!" : $"+{amount} {(def?.Name ?? itemId)}");
    }

    /// <summary>Ground height for a drop: walk DOWN from its current Y through the
    /// collision tiles of every visible map (same flip convention as physics —
    /// grid row ty spans world [(mapH-1-ty)*cell, (mapH-ty)*cell]). Falls back to
    /// the map bottom when nothing is below (loot over a pit just falls away).</summary>
    private static float FindDropFloor(EditorObjectManager? mgr, Tilemap2D? activeMap, float x, float y)
    {
        float best = float.NegativeInfinity;
        bool any = false;
        foreach (var m in CollectCollisionMapsSafe(mgr, activeMap))
        {
            float cell = m.TileSize * Tilemap2D.WorldScale;
            if (cell <= 0f) continue;
            int col = (int)MathF.Floor(x / cell);
            if (col < 0 || col >= m.Width) continue;
            int hitRow = -1;
            for (int li = 0; li < m.Layers.Count && hitRow < 0; li++)
            {
                var layer = m.Layers[li];
                if (layer == null || !layer.IsVisible || layer.CollisionTileIds.Count == 0) continue;
                // From the drop's row upward? No — search DOWNWARD from the top: the
                // first solid row AT or BELOW the drop is its floor.
                for (int ty = (int)MathF.Floor((m.Height * cell - y) / cell); ty < m.Height; ty++)
                {
                    if (ty < 0) continue;
                    int tile = m.GetTile(li, col, ty);
                    if (tile > 0 && layer.CollisionTileIds.Contains(tile)) { hitRow = ty; break; }
                }
            }
            if (hitRow >= 0)
            {
                float floorY = (m.Height - 1 - hitRow) * cell; // top of the solid tile
                if (!any || floorY > best) { best = floorY; any = true; }
            }
        }
        return any ? best : -20f; // nothing below: fall out of the world like projectiles
    }

    private static IEnumerable<Tilemap2D> CollectCollisionMapsSafe(EditorObjectManager? mgr, Tilemap2D? activeMap)
    {
        if (activeMap != null) yield return activeMap;
        if (mgr == null) yield break;
        foreach (var o in mgr.Objects)
            if (o is { IsVisible: true, PrimitiveType: DarkEngine3D_gl_csharp.Engine.Objects.EditorPrimitiveType.Map2D, Map2dTilemap: { } m } && m != activeMap)
                yield return m;
    }

    /// <summary>Clear every world drop (fresh session / project close).</summary>
    public static void ClearDrops() => _drops.Clear();

    /// <summary>Render every loot drop as an animated world-space icon quad. Call
    /// INSIDE an Effect2DSystem Begin/EndBatch (EditorObjectManager 2D pass, after the
    /// equipped hotbar). Bobbing idle art + spin/scale-in while magnetized.</summary>
    public static void RenderLootDrops(Camera? camera)
    {
        if (camera == null || _drops.Count == 0) return;
        var cam = camera;
        float halfH = MathF.Max(0.5f, cam.OrthoSize);
        float halfW = halfH * cam.GetAspect();
        float size = 0.42f;
        foreach (var d in _drops)
        {
            // Off-screen cull (same margin as the equipped hotbar).
            if (d.X < cam.Position.X - halfW - 2f || d.X > cam.Position.X + halfW + 2f
                || d.Y < cam.Position.Y - halfH - 2f || d.Y > cam.Position.Y + halfH + 2f) continue;
            var def = Find(d.ItemId);
            if (def == null) continue;
            uint tex = GetIconTexture(def);
            if (tex == 0) continue;
            var (u0, vTop, u1, vBottom) = GetIconUV(def);

            // Animasi: bob pelan saat idle; spin (UV mirror) + squash saat tersedot.
            float bob = d.Magnetized ? 0f : MathF.Sin(d.Age * 2.4f) * 0.05f;
            float squash = 1f + (1f - d.BounceT) * 0.25f * MathF.Sin(d.BounceT * MathF.PI);
            float s = size * squash;
            float su0 = u0, su1 = u1;
            if (d.Magnetized && MathF.Sin(d.Age * 18f) < 0f)
                (su0, su1) = (su1, su0); // flip halfway through the vacuum spin
            // Dark backing tile (readable on any background) + the icon over it.
            Effect2DSystem.PushQuad(Effect2DSystem.GetProceduralTexture(Effect2DSystem.TexSquare),
                d.X - s * 0.56f, d.Y + bob - s * 0.56f, s * 1.12f, s * 1.12f,
                0f, 0f, 1f, 1f, new Vector4(0.08f, 0.08f, 0.12f, 0.8f), 0.084f);
            // World batch corners: BL/BR take the BOTTOM v, TL/TR the TOP v.
            Effect2DSystem.PushQuad(tex, d.X - s * 0.5f, d.Y + bob - s * 0.5f, s, s,
                su0, vBottom, su1, vTop, new Vector4(1f, 1f, 1f, 1f), 0.086f);
        }
    }

    // ═══════════════════════ HUD hotbar rendering ═══════════════════════

    /// <summary>Render the player's equipped items as floating world-space icons above
    /// the character. Drawn through the shared Effect2DSystem batch (call INSIDE a
    /// Begin/EndBatch). <paramref name="feetX"/>/<paramref name="feetY"/>: player feet.</summary>
    public static void RenderEquippedHotbar(Camera? camera, Equipment? equip,
        float feetX, float feetY, float playerHeight, float dt)
    {
        if (camera == null || equip == null) return;
        var cam = camera;
        float halfH = MathF.Max(0.5f, cam.OrthoSize);
        if (feetY > cam.Position.Y + halfH + 2f || feetY < cam.Position.Y - halfH - 2f) return;

        float iconSize = MathF.Max(0.28f, playerHeight * 0.22f);
        float y = feetY + playerHeight + iconSize * 0.65f;
        // Collect first, then center on the ACTUAL equipped count — the strip used
        // to hardcode a 6-slot center AND a 6-icon cap, both wrong for the
        // 13-slot paperdoll (slot 7+ would silently disappear).
        _hotbarIcons.Clear();
        foreach (var slotName in EquipSlots)
        {
            string id = equip.Get(slotName);
            if (string.IsNullOrEmpty(id)) continue;
            var def = Find(id);
            if (def == null) continue;
            uint tex = GetIconTexture(def);
            if (tex == 0) continue;
            var (u0, vTop, u1, vBottom) = GetIconUV(def);
            _hotbarIcons.Add((tex, u0, vTop, u1, vBottom));
        }
        float centerOff = (_hotbarIcons.Count - 1) * 0.5f;
        for (int i = 0; i < _hotbarIcons.Count; i++)
        {
            var (tex, u0, vTop, u1, vBottom) = _hotbarIcons[i];
            float x = feetX + (i - centerOff) * (iconSize * 1.15f) - iconSize * 0.5f;
            // Slot-colored backing tile (slightly larger) + the icon over it.
            Effect2DSystem.PushQuad(Effect2DSystem.GetProceduralTexture(Effect2DSystem.TexSquare),
                x - iconSize * 0.08f, y - iconSize * 0.08f, iconSize * 1.16f, iconSize * 1.16f,
                0f, 0f, 1f, 1f, new Vector4(0.08f, 0.08f, 0.12f, 0.85f), 0.088f);
            // World batch corners: BL/BR take the BOTTOM v, TL/TR the TOP v.
            Effect2DSystem.PushQuad(tex, x, y, iconSize, iconSize, u0, vBottom, u1, vTop,
                new Vector4(1f, 1f, 1f, 1f), 0.09f);
        }
    }

    // Scratch strip for RenderEquippedHotbar (no per-frame allocation).
    private static readonly List<(uint Tex, float U0, float VTop, float U1, float VBottom)> _hotbarIcons = new();
}
