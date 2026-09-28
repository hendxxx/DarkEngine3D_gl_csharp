using System.Collections.Generic;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Inventory system phase 1: item catalog, per-character inventory grids, and the
/// equipment model (paperdoll slots) for Player2D / NPC / enemy characters.
///
/// Item ICONS are cropped straight from any tileset/sprite-sheet texture (columns ×
/// rows grid + per-item frame index) — no dedicated icon assets required, matching
/// the "render from tilemap" request. The HUD hotbar draws through the shared
/// Effect2DSystem batch pass as world-space quads over the player's head.
/// </summary>
public static class InventorySystem
{
    // ═══════════════════════ Equipment slots (paperdoll) ═══════════════════════

    public static readonly string[] EquipSlots =
        ["Head", "Body", "Legs", "Weapon", "Shield", "Accessory"];

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
        /// <summary>Equipped-art size in world units (0 = player height × 0.9).</summary>
        public float EquipWorldHeight = 0f;
        /// <summary>Render layer offset for the equipped art (bigger = in front).</summary>
        public float EquipZOffset = 0.09f;
        /// <summary>Optional particle FX preset fired when the item is used/equipped.</summary>
        public string UseFxPreset = "";
    }

    /// <summary>All registered item types (Inspector / future editors author here).</summary>
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

    /// <summary>Resolve the GPU texture of an item's icon sheet through the shared
    /// IDEBridge registry (tilesets + sprite sheets). Returns 0 when not found.</summary>
    public static uint GetIconTexture(ItemDef def) =>
        def != null && !string.IsNullOrWhiteSpace(def.IconSheet)
        && DarkEngine3D_gl_csharp.Engine.IDE.IDEBridge.TryGetSpriteSheetTexture(def.IconSheet, out uint tex, out int _, out int _)
            ? tex : 0u;

    /// <summary>UV rect of an item's icon cell (top-left origin), prepped for the
    /// top-row-first upload convention (v flipped like every sprite path here).</summary>
    public static (float u0, float v0, float u1, float v1) GetIconUV(ItemDef def)
    {
        int cols = def.IconCols > 0 ? def.IconCols : 8;
        int rows = def.IconRows > 0 ? def.IconRows : 8;
        int col = Math.Clamp(def.IconCol, 0, cols - 1);
        int row = Math.Clamp(def.IconRow, 0, rows - 1);
        float u0 = col / (float)cols, u1 = (col + 1) / (float)cols;
        // Grid row 0 = TOP of the image; GL v grows upward → flip.
        float vTop = 1f - row / (float)rows;
        float vBot = 1f - (row + 1) / (float)rows;
        return (u0, vBot, u1, vTop);
    }

    // ═══════════════════════ Session inventory (trigger Give Item) ═══════════════════════

    /// <summary>Simple id → count bag for the current session (no grid limits yet —
    /// the phase-2 UI will present this as pages). Cleared on session start.</summary>
    public static readonly Dictionary<string, int> PlayerBag = new();

    public static void ResetSession() => PlayerBag.Clear();

    /// <summary>Add items; returns the amount actually added (respects MaxStack totals).</summary>
    public static int AddItem(string id, int amount)
    {
        if (string.IsNullOrWhiteSpace(id) || amount <= 0) return 0;
        var def = Find(id);
        int current = PlayerBag.TryGetValue(id, out int c) ? c : 0;
        int max = def?.MaxStack is int m && m > 0 ? m * 999 : int.MaxValue; // stacks are soft-capped in phase 1
        int added = Math.Clamp(amount, 0, Math.Max(0, max - current));
        if (added > 0) PlayerBag[id] = current + added;
        return added;
    }

    public static int Count(string id) =>
        id != null && PlayerBag.TryGetValue(id, out int c) ? c : 0;

    /// <summary>Remove up to <paramref name="amount"/>; returns how many were removed.</summary>
    public static int RemoveItem(string id, int amount)
    {
        if (id == null || amount <= 0 || !PlayerBag.TryGetValue(id, out int c)) return 0;
        int removed = Math.Min(amount, c);
        if (c - removed <= 0) PlayerBag.Remove(id); else PlayerBag[id] = c - removed;
        return removed;
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

    // ═══════════════════════ HUD hotbar rendering ═══════════════════════

    /// <summary>Render the player's equipped items as floating world-space icons above
    /// the character (the phase-1 "equipment visible on screen" path). The hotbar sits
    /// above the player's head; each icon is an item tile cropped from its sheet.
    /// Drawn through the shared Effect2DSystem batch (call INSIDE a Begin/EndBatch).
    /// <paramref name="feetX"/>/<paramref name="feetY"/>: player feet world position.</summary>
    public static void RenderEquippedHotbar(Camera? camera, Equipment? equip,
        float feetX, float feetY, float playerHeight, float dt)
    {
        if (camera == null || equip == null) return;
        var cam = camera;
        float halfH = MathF.Max(0.5f, cam.OrthoSize);
        if (feetY > cam.Position.Y + halfH + 2f || feetY < cam.Position.Y - halfH - 2f) return;

        float iconSize = MathF.Max(0.28f, playerHeight * 0.22f);
        float y = feetY + playerHeight + iconSize * 0.65f;
        int shown = 0;
        foreach (var slot in EquipSlots)
        {
            string id = equip.Get(slot);
            if (string.IsNullOrEmpty(id)) continue;
            var def = Find(id);
            if (def == null) continue;
            uint tex = GetIconTexture(def);
            if (tex == 0) continue;
            var (u0, v0, u1, v1) = GetIconUV(def);
            float x = feetX + (shown - 2.5f) * (iconSize * 1.15f) - iconSize * 0.5f;
            // Slot-colored backing tile (slightly larger) + the icon over it.
            Effect2DSystem.PushQuad(Effect2DSystem.GetProceduralTexture(Effect2DSystem.TexSquare),
                x - iconSize * 0.08f, y - iconSize * 0.08f, iconSize * 1.16f, iconSize * 1.16f,
                0f, 0f, 1f, 1f, new Vector4(0.08f, 0.08f, 0.12f, 0.85f), 0.088f);
            Effect2DSystem.PushQuad(tex, x, y, iconSize, iconSize, u0, v0, u1, v1,
                new Vector4(1f, 1f, 1f, 1f), 0.09f);
            shown++;
            if (shown >= 6) break;
        }
    }
}
