using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using ImGuiNET;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Project;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Simple NPC shop: one shop = a named list of stock entries (item id + optional
/// price override). Persisted per project to Assets/Shops/shops.json (load on
/// project open, clear on close — wired in IDE.cs like the item catalog).
/// The dialogue/trigger action "Open Shop" shows the classic RPG panel
/// (ShopHud: item grid + Buy/Sell buttons) while the conversation continues
/// underneath; closing the panel returns to the dialogue.
/// </summary>
public static class ShopSystem
{
    /// <summary>One stocked item: item id + optional fixed buy price (null = use the
    /// ItemDef's Price). Stock -1 = unlimited (shops restock).</summary>
    public sealed class ShopEntry
    {
        public string ItemId { get; set; } = "";
        /// <summary>Buy price override (Gold). Null = use ItemDef.Price.</summary>
        public int? PriceOverride { get; set; }
        public int Stock { get; set; } = -1;
    }

    /// <summary>A named shop ("Pedagang Keliling"). Referenced by id from the
    /// "Open Shop" action's Param.</summary>
    public sealed class ShopDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "New Shop";
        /// <summary>Sell payout = ItemDef.Price × SellPercent / 100 (vendor markdown).</summary>
        public int SellPercent { get; set; } = 50;
        public List<ShopEntry> Stock { get; set; } = new();
    }

    // ════════════════════════════════════════════
    //  CATALOG (per project, shops.json)
    // ════════════════════════════════════════════

    public static readonly List<ShopDef> Shops = new();

    public static ShopDef? GetShop(string idOrName) =>
        string.IsNullOrWhiteSpace(idOrName) ? null
        : Shops.FirstOrDefault(s => string.Equals(s.Id, idOrName, StringComparison.OrdinalIgnoreCase))
        ?? Shops.FirstOrDefault(s => string.Equals(s.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    public static List<string> GetShopIds() => Shops.Select(s => s.Id).ToList();

    public static void ClearCatalog() => Shops.Clear();

    private static string GetFilePath()
    {
        string dir = ProjectManager.IsProjectLoaded
            ? Path.Combine(ProjectManager.ProjectRoot!, "Assets", "Shops")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Shops");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "shops.json");
    }

    private sealed class ShopFileData
    {
        public List<ShopDef> Shops { get; set; } = [];
    }

    public static void LoadCatalog()
    {
        ClearCatalog();
        if (!ProjectManager.IsProjectLoaded) return;
        try
        {
            string path = GetFilePath();
            if (!File.Exists(path)) return;
            var data = JsonSerializer.Deserialize<ShopFileData>(File.ReadAllText(path));
            if (data?.Shops != null) Shops.AddRange(data.Shops);
            Console.WriteLine($"[Shop] Loaded {Shops.Count} shop(s) → {path}");
        }
        catch (Exception ex) { Console.WriteLine($"[Shop] Load failed: {ex.Message}"); }
    }

    public static void Save()
    {
        try
        {
            if (!ProjectManager.IsProjectLoaded)
            {
                Console.WriteLine("[Shop] Save skipped: no project open");
                return;
            }
            File.WriteAllText(GetFilePath(),
                JsonSerializer.Serialize(new ShopFileData { Shops = Shops }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[Shop] Saved {Shops.Count} shop(s) → {GetFilePath()}");
        }
        catch (Exception ex) { Console.WriteLine($"[Shop] Save failed: {ex.Message}"); }
    }

    // ════════════════════════════════════════════
    //  TRANSACTIONS (shared by the HUD panel; Buy/Sell Item trigger actions keep
    //  their own action-catalog cases so dialogue-only shops stay data-driven)
    // ════════════════════════════════════════════

    /// <summary>Attempt a purchase. priceHint null = ItemDef.Price. All-or-nothing:
    /// gold below total → nothing happens. Returns (ok, flash message).</summary>
    public static (bool ok, string msg) Buy(ShopDef? shop, string itemId, int amount, int? priceHint)
    {
        if (amount <= 0) amount = 1;
        var def = InventorySystem.Find(itemId);
        if (def == null) return (false, $"Item tidak dikenal: {itemId}");
        int unit = priceHint ?? Math.Max(0, def.Price);
        int total = unit * amount;
        if (InventorySystem.Gold < total) return (false, $"Gold kurang! Butuh {total} Gold.");
        int added = InventorySystem.AddItem(itemId, amount);
        if (added <= 0) return (false, "Inventory penuh!");
        InventorySystem.Gold -= unit * added; // partial add → charge proportionally
        string msg = $"-{unit * added} Gold → +{added} {def.Name}";
        InventoryHud.PushFlash(msg);
        Console.WriteLine($"[Shop] Buy '{itemId}' ×{added} @ {unit} → gold {InventorySystem.Gold}");
        return (true, msg);
    }

    /// <summary>Attempt a sale. payoutHint null = ItemDef.Price × shop.SellPercent.
    /// Requires the full amount in stock; otherwise nothing happens.</summary>
    public static (bool ok, string msg) Sell(ShopDef? shop, string itemId, int amount, int? payoutHint)
    {
        if (amount <= 0) amount = 1;
        var def = InventorySystem.Find(itemId);
        if (def == null) return (false, $"Item tidak dikenal: {itemId}");
        if (InventorySystem.Count(itemId) < amount) return (false, $"Kamu tidak punya {amount} {def.Name}.");
        int unit = payoutHint ?? (Math.Max(0, def.Price) * (shop?.SellPercent ?? 50) / 100);
        int sold = InventorySystem.RemoveItem(itemId, amount);
        InventorySystem.Gold += unit * sold;
        string msg = $"+{unit * sold} Gold ← -{sold} {def.Name}";
        InventoryHud.PushFlash(msg);
        Console.WriteLine($"[Shop] Sell '{itemId}' ×{sold} @ {unit} → gold {InventorySystem.Gold}");
        return (true, msg);
    }
}

/// <summary>
/// In-game shop panel (classic RPG): parchment/wood style shared with InventoryHud.
/// Left column = SHOP STOCK grid, right column = YOUR BAG (read-only view of the
/// inventory grid), description box + [Beli (B)] / [Jual (S)] buttons below.
/// Rendered through the HUD batch pipeline (same call sites as InventoryHud.Render),
/// so it lives inside the viewport texture in both the GameScene HUD pass and the
/// editor's no-scene preview path. GLFW input with physical-edge tracking (no ImGui).
/// </summary>
public static class ShopHud
{
    /// <summary>Currently open shop (null = closed). The "Open Shop" action opens it;
    /// Esc / click-outside closes and the still-running conversation shows again.</summary>
    public static ShopSystem.ShopDef? Open { get; private set; }

    public static bool IsOpen => Open != null;

    /// <summary>Open a shop by id/name (the "Open Shop" action). Returns false when unknown.</summary>
    public static bool TryOpen(string idOrName)
    {
        var shop = ShopSystem.GetShop(idOrName);
        if (shop == null)
        {
            Console.WriteLine($"[Shop] Open FAILED: shop '{idOrName}' not found");
            return false;
        }
        Open = shop;
        _selectedShop = -1;
        _selectedBag = -1;
        Console.WriteLine($"[Shop] Opened '{shop.Id}' ({shop.Stock.Count} item)");
        return true;
    }

    public static void Close()
    {
        if (Open == null) return;
        Console.WriteLine($"[Shop] Closed '{Open.Id}'");
        Open = null;
        _selectedShop = -1;
        _selectedBag = -1;
    }

    // ── Fonts on the shared HUD (InventoryHud owns the HUD instance) ──
    private static int _titleFont, _labelFont;

    /// <summary>Bake font slots on InventoryHud.SharedHud (call after its Prewarm —
    /// mid-frame bakes produce the "boxes without glyphs" bug, so only pre-existing
    /// slots are used here).</summary>
    public static void Prewarm()
    {
        if (InventoryHud.SharedHud == null || _labelFont != 0) return;
        _titleFont = InventoryHud.SharedHud.GetOrCreateFontSlot("Artifacts\\fonts\\Worldstar.ttf", 24f);
        _labelFont = InventoryHud.SharedHud.GetOrCreateFontSlot("Artifacts\\fonts\\Worldstar.ttf", 15f);
    }

    private static int TitleFont(HUD hud) => hud == InventoryHud.SharedHud && _titleFont != 0 ? _titleFont : 0;
    private static int LabelFont(HUD hud) => hud == InventoryHud.SharedHud && _labelFont != 0 ? _labelFont : 0;

    /// <summary>Window-px → scene-px mapper (docked preview letterbox). Set by the
    /// ViewportPanel together with InventoryHud.WindowToScene.</summary>
    public static Func<float, float, (float X, float Y)>? WindowToScene { get; set; }

    // ── Palette (InventoryHud's is private — same values duplicated) ──
    private static readonly Vector3 Parchment = new(0.855f, 0.775f, 0.610f);
    private static readonly Vector3 ParchmentDim = new(0.745f, 0.650f, 0.495f);
    private static readonly Vector3 ParchmentHover = new(0.910f, 0.845f, 0.700f);
    private static readonly Vector3 WoodDark = new(0.360f, 0.225f, 0.120f);
    private static readonly Vector3 WoodMid = new(0.520f, 0.345f, 0.195f);
    private static readonly Vector3 Cream = new(0.975f, 0.940f, 0.845f);
    private static readonly Vector3 InkBrown = new(0.280f, 0.175f, 0.095f);
    private static readonly Vector3 InkSoft = new(0.475f, 0.360f, 0.240f);
    private static readonly Vector3 HighlightGold = new(0.960f, 0.760f, 0.180f);
    private static readonly Vector3 PlateDark = new(0.185f, 0.120f, 0.070f);
    private static readonly Vector3 BuyGreen = new(0.30f, 0.62f, 0.28f);
    private static readonly Vector3 SellBlue = new(0.28f, 0.45f, 0.68f);
    private static readonly Vector3 BtnDisabled = new(0.42f, 0.36f, 0.30f);

    // ── Layout ──
    private static float Slot => Math.Clamp(Glfw.WindowHeight * 0.070f, 46f, 64f);
    private static float Gap => Slot * 0.16f;
    private const int ShopCols = 4, ShopRows = 3;   // stock grid 4×3
    private const int BagCols = 6, BagRows = 4;     // mirrors the 6×4 inventory grid

    // ── Input edge state ──
    private static bool _escWasDown, _leftWasDown, _bWasDown, _sWasDown;
    private static int _selectedShop = -1;
    private static int _selectedBag = -1;
    private static string _tooltip = "";

    // Mouse in SCENE-px (mapped through WindowToScene when docked). ALL hover tests
    // must use THIS — the raw cursor is in window px while the panel is drawn in
    // texture px; in the docked preview those differ (letterbox) and the highlight
    // lands beside the real cursor (fullscreen matched by coincidence).
    private static float _mx, _my;

    /// <summary>Per-frame update + draw. Call right after InventoryHud.Render at every
    /// HUD call site (same pipeline); self-gates when no shop is open.</summary>
    public static void Render(HUD hud, float dt)
    {
        if (hud == null || !Player2DStats.SessionActive)
        {
            Open = null; // session ended → panel closes
            ResetEdges();
            return;
        }
        if (Open == null) { ResetEdges(); return; }
        if (InventoryHud.SharedHud != null && _labelFont == 0) Prewarm();

        // Cursor source = ImGui MousePos first (screen-space parity with the
        // ViewportPanel letterbox rect — see InventoryHud.Render for the full comment;
        // GLFW fallback is for paths outside an ImGui frame).
        var imPos = ImGui.GetIO().MousePos;
        float mx, my;
        if (imPos.X >= 0f)
        {
            mx = imPos.X; my = imPos.Y;
        }
        else
        {
            Mouse.GetCursorPosition(out double mxRaw, out double myRaw);
            mx = (float)mxRaw; my = (float)myRaw;
        }
        if (WindowToScene != null) (mx, my) = WindowToScene(mx, my);
        _mx = mx; _my = my; // single source of truth for every hover test this frame

        nint window = Glfw.GetWindow();
        bool escDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);
        bool leftDown = Mouse.IsButtonDown(0);
        bool escPressed = escDown && !_escWasDown;
        bool leftPressed = leftDown && !_leftWasDown;
        _escWasDown = escDown;
        _leftWasDown = leftDown;
        _tooltip = "";

        var shop = Open!;
        float slot = Slot, gap = Gap;
        float pad = slot * 0.4f;
        float bannerH = slot * 0.85f;
        float shopW = ShopCols * slot + (ShopCols - 1) * gap;
        float bagW = BagCols * slot + (BagCols - 1) * gap;
        float gridH = MathF.Max(ShopRows, BagRows) * (slot + gap) - gap;
        float descH = slot * 1.9f;
        float btnH = slot * 0.72f;
        float panelW = pad * 2 + shopW + pad + bagW;
        float panelH = pad + bannerH + pad * 0.6f + gridH + pad * 0.6f + descH + pad * 0.5f + btnH;

        float px = (Glfw.WindowWidth - panelW) * 0.5f;
        float py = (Glfw.WindowHeight - panelH) * 0.5f;

        // ── Panel + frame + banner ──
        FillSolid(hud, px, py, panelW, panelH, Parchment, 3);
        WoodFrame(hud, px, py, panelW, panelH);
        float bx = px + pad, by = py + pad, bw = panelW - pad * 2f;
        FillSolid(hud, bx, by, bw, bannerH, WoodDark, 3);
        string title = shop.Name.Length > 0 ? shop.Name : "Shop";
        var tExt = hud.GetTextExtents(title, TitleFont(hud));
        hud.DrawText(title, bx + pad * 0.7f, by + (bannerH - tExt.Height) * 0.5f, Cream, null, 0f, TitleFont(hud));
        HUD.TextOut(bx + pad * 0.7f, by + (bannerH - tExt.Height) * 0.5f, title, Cream, fontSizePx: 22f);
        // Gold counter + [Esc] chip on the banner's right.
        string gold = $"◈ {InventorySystem.Gold}";
        int lf = LabelFont(hud);
        float goldW = hud.GetTextExtents(gold, lf).Width;
        float chip = bannerH * 0.62f;
        float chipX = bx + bw - chip - pad * 0.6f;
        hud.DrawText(gold, chipX - pad * 0.8f - goldW, by + (bannerH - hud.GetTextExtents(gold, lf).Height) * 0.5f,
            HighlightGold, null, 0f, lf);
        HUD.TextOut(chipX - pad * 0.8f - goldW, by + (bannerH - hud.GetTextExtents(gold, lf).Height) * 0.5f,
            gold, HighlightGold, fontSizePx: 14f);
        HintChip(hud, chipX, by + (bannerH - chip) * 0.5f, chip, "Esc", lf);

        float bodyY = by + bannerH + pad * 0.6f;
        float labelY = bodyY;
        float gridY = bodyY + MathF.Max(12f, slot * 0.3f);

        // ── Left: SHOP STOCK 4×3 ──
        float sx = bx;
        hud.DrawText("BARANG TOKO", sx, labelY, InkBrown, null, 0f, lf);
        for (int i = 0; i < ShopCols * ShopRows; i++)
        {
            int row = i / ShopCols, col = i % ShopCols;
            var entry = i < shop.Stock.Count ? shop.Stock[i] : null;
            var def = entry != null ? InventorySystem.Find(entry.ItemId) : null;
            float x = sx + col * (slot + gap);
            float y = gridY + row * (slot + gap);
            string badge = "";
            if (entry != null)
                badge = (entry.PriceOverride ?? (def?.Price ?? 0)).ToString();
            bool hover = DrawCell(hud, x, y, slot, def, entry?.Stock ?? -1, badge,
                selected: _selectedShop == i, sellMode: false);
            if (hover)
            {
                if (def != null)
                {
                    _tooltip = ShopTooltip(def, entry, buy: true);
                    if (leftPressed) { _selectedShop = i; _selectedBag = -1; }
                }
                else if (leftPressed) { _selectedShop = -1; }
            }
        }

        // ── Right: YOUR BAG 6×4 (read-only inventory grid view) ──
        float gx = bx + shopW + pad;
        hud.DrawText("TAS KAMU", gx, labelY, InkBrown, null, 0f, lf);
        for (int i = 0; i < BagCols * BagRows; i++)
        {
            int row = i / BagCols, col = i % BagCols;
            ref var slotData = ref InventorySystem.Grid[i];
            var def = slotData.IsEmpty ? null : InventorySystem.Find(slotData.ItemId);
            float x = gx + col * (slot + gap);
            float y = gridY + row * (slot + gap);
            bool hover = DrawCell(hud, x, y, slot, def, slotData.IsEmpty ? 0 : slotData.Count, "",
                selected: _selectedBag == i, sellMode: true);
            if (hover)
            {
                if (def != null)
                {
                    _tooltip = ShopTooltip(def, null, buy: false);
                    if (leftPressed) { _selectedBag = i; _selectedShop = -1; }
                }
                else if (leftPressed) { _selectedBag = -1; }
            }
        }

        // ── Description box + buttons ──
        float dY = gridY + BagRows * (slot + gap) + pad * 0.4f;
        FillSolid(hud, bx, dY, bw, descH, ParchmentDim, 2);
        float dTextX = bx + pad * 0.7f, dTextY = dY + pad * 0.4f;
        float lineStep = MathF.Max(14f, slot * 0.3f);

        // Resolve the selection (stock cell first, else bag cell) into a plan.
        string? selItem = null;
        int? priceHint = null;
        bool isBuy = false;
        int selStock = -1;
        if (_selectedShop >= 0 && _selectedShop < shop.Stock.Count)
        {
            var e = shop.Stock[_selectedShop];
            selItem = e.ItemId; priceHint = e.PriceOverride; isBuy = true; selStock = e.Stock;
        }
        else if (_selectedBag >= 0)
        {
            ref var s = ref InventorySystem.Grid[_selectedBag];
            if (!s.IsEmpty) { selItem = s.ItemId; isBuy = false; }
        }

        var selDef = selItem != null ? InventorySystem.Find(selItem) : null;
        int unitBuy = selDef != null ? (priceHint ?? Math.Max(0, selDef.Price)) : 0;
        int unitSell = selDef != null ? Math.Max(0, selDef.Price) * shop.SellPercent / 100 : 0;
        const int qty = 1;

        if (selDef != null)
        {
            string l1 = isBuy ? $"{selDef.Name}  —  {unitBuy} Gold / pcs" : $"{selDef.Name}  —  jual {unitSell} Gold / pcs";
            hud.DrawText(l1, dTextX, dTextY, InkBrown, null, 0f, lf);
            HUD.TextOut(dTextX, dTextY, l1, InkBrown, fontSizePx: 15f);
            string line2 = isBuy
                ? (selStock >= 0 ? $"Stok toko: {selStock}" : "Stok toko: tak terbatas")
                : $"Kamu punya: {InventorySystem.Count(selDef.Id)}";
            hud.DrawText(line2, dTextX, dTextY + lineStep, InkSoft, null, 0f, lf);
            HUD.TextOut(dTextX, dTextY + lineStep, line2, InkSoft, fontSizePx: 14f);
            string hint = isBuy
                ? $"Total {unitBuy * qty} Gold — tekan Beli (B)"
                : $"Dapat {unitSell * qty} Gold — tekan Jual (S)";
            hud.DrawText(hint, dTextX, dTextY + lineStep * 2f, InkBrown, null, 0f, lf);
            HUD.TextOut(dTextX, dTextY + lineStep * 2f, hint, InkBrown, fontSizePx: 14f);
        }
        else
        {
            hud.DrawText("Pilih barang di toko (kiri) untuk membeli,", dTextX, dTextY, InkSoft, null, 0f, lf);
            hud.DrawText("atau barang di tas (kanan) untuk menjual.", dTextX, dTextY + lineStep, InkSoft, null, 0f, lf);
            HUD.TextOut(dTextX, dTextY, "Pilih barang di toko (kiri) untuk membeli,", InkSoft, fontSizePx: 14f);
            HUD.TextOut(dTextX, dTextY + lineStep, "atau barang di tas (kanan) untuk menjual.", InkSoft, fontSizePx: 14f);
        }

        float btnY = dY + descH + pad * 0.4f;
        float btnW = (bw - pad) * 0.5f;
        bool canBuy = isBuy && selDef != null && InventorySystem.Gold >= unitBuy * qty
            && (selStock < 0 || selStock >= qty);
        bool canSell = !isBuy && selDef != null && InventorySystem.Count(selDef.Id) >= qty;
        float buyBx = bx, sellBx = bx + btnW + pad;
        DrawButton(hud, buyBx, btnY, btnW, btnH, "Beli (B)", BuyGreen, canBuy, lf);
        DrawButton(hud, sellBx, btnY, btnW, btnH, "Jual (S)", SellBlue, canSell, lf);

        // ── Transactions (button click or B/S key edge) ──
        bool bDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_B);
        bool sDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
        bool bPressed = bDown && !_bWasDown;
        bool sPressed = sDown && !_sWasDown;
        _bWasDown = bDown;
        _sWasDown = sDown;
        bool buyClicked = leftPressed && Hover(buyBx, btnY, btnW, btnH);
        bool sellClicked = leftPressed && Hover(sellBx, btnY, btnW, btnH);
        if (canBuy && (buyClicked || bPressed) && selItem != null)
            ShopSystem.Buy(shop, selItem, qty, priceHint);
        else if (canSell && (sellClicked || sPressed) && selItem != null)
            ShopSystem.Sell(shop, selItem, qty, null);

        // ── Close: Esc (edge) or click outside the panel ──
        if (escPressed) Close();
        else if (leftPressed && !Hover(px, py, panelW, panelH)) Close();

        DrawTooltip(hud, mx, my);
    }

    // ═══════════════════════ Style helpers (mirror InventoryHud) ═══════════════════════

    private static bool Hover(float x, float y, float w, float h)
    {
        // MAPPED scene-px only (see _mx/_my) — raw window px breaks docked preview.
        return _mx >= x && _mx < x + w && _my >= y && _my < y + h;
    }

    /// <summary>OPAQUE fill via HUD.DrawSolidBox — see InventoryHud.FillSolid. The old
    /// 3× DrawBox stack at 0.6 alpha washed out over bright maps ("layer tembus").
    /// `layers` kept for call-site compat, ignored.</summary>
    private static void FillSolid(HUD hud, float x, float y, float w, float h, Vector3 color, int layers = 3)
    {
        hud.DrawSolidBox(x, y, w, h, color);
    }

    private static void WoodFrame(HUD hud, float x, float y, float w, float h)
    {
        float t = MathF.Max(3f, Slot * 0.075f);
        hud.DrawBox(x, y, w, t, WoodDark);
        hud.DrawBox(x, y + h - t, w, t, WoodDark);
        hud.DrawBox(x, y + t, t, h - t * 2f, WoodDark);
        hud.DrawBox(x + w - t, y + t, t, h - t * 2f, WoodDark);
        float t2 = MathF.Max(1.5f, t * 0.4f);
        float i2 = t * 1.4f;
        hud.DrawBox(x + i2, y + i2, w - i2 * 2f, t2, WoodMid);
        hud.DrawBox(x + i2, y + h - i2 - t2, w - i2 * 2f, t2, WoodMid);
        hud.DrawBox(x + i2, y + i2 + t2, t2, h - i2 * 2f - t2 * 2f, WoodMid);
        hud.DrawBox(x + w - i2 - t2, y + i2 + t2, t2, h - i2 * 2f - t2 * 2f, WoodMid);
    }

    private static void HintChip(HUD hud, float x, float y, float size, string label, int fontSlot)
    {
        FillSolid(hud, x, y, size, size, WoodDark, 2);
        float tw = hud.GetTextExtents(label, fontSlot).Width;
        float th = hud.GetTextExtents(label, fontSlot).Height;
        hud.DrawText(label, x + (size - tw) * 0.5f, y + (size - th) * 0.5f, Cream, null, 0f, fontSlot);
        HUD.TextOut(x + (size - tw) * 0.5f, y + (size - th) * 0.5f, label, Cream, fontSizePx: 13f);
    }

    /// <summary>One shop/bag cell. badge = price text (buy cells), stock −1 = unlimited.
    /// Returns hover.</summary>
    private static bool DrawCell(HUD hud, float x, float y, float size,
        InventorySystem.ItemDef? def, int stock, string badge, bool selected, bool sellMode)
    {
        bool hover = Hover(x, y, size, size);
        hud.DrawBox(x, y, size, size, hover ? ParchmentHover : ParchmentDim);
        var border = selected ? HighlightGold : WoodMid;
        float t = MathF.Max(1.5f, size * 0.035f);
        hud.DrawBox(x, y, size, t, border);
        hud.DrawBox(x, y + size - t, size, t, border);
        hud.DrawBox(x, y + t, t, size - t * 2f, border);
        hud.DrawBox(x + size - t, y + t, t, size - t * 2f, border);

        if (def != null)
        {
            uint tex = InventorySystem.GetIconTexture(def);
            if (tex != 0)
            {
                var (u0, vTop, u1, vBottom) = InventorySystem.GetIconUV(def);
                float pad = size * 0.12f;
                hud.DrawImageUV(x + pad, y + pad, size - pad * 2f, size - pad * 2f, tex, u0, vTop, u1, vBottom);
            }
            else
            {
                hud.DrawBox(x + size * 0.25f, y + size * 0.25f, size * 0.5f, size * 0.5f, new Vector3(0.45f, 0.45f, 0.5f));
            }
            int lf = LabelFont(hud);
            // Price chip (top-left) on buy cells.
            if (!sellMode && badge.Length > 0)
            {
                float tw = hud.GetTextExtents(badge, lf).Width;
                float th = hud.GetTextExtents(badge, lf).Height;
                FillSolid(hud, x + 3f, y + 3f, tw + 8f, th + 4f, PlateDark, 2);
                hud.DrawText(badge, x + 7f, y + 5f, HighlightGold, null, 0f, lf);
            }
            // Count chip (bottom-right) on bag cells.
            if (sellMode && stock > 1)
            {
                string s = stock > 999 ? "999+" : stock.ToString();
                float tw = hud.GetTextExtents(s, lf).Width;
                float th = hud.GetTextExtents(s, lf).Height;
                FillSolid(hud, x + size - tw - 9f, y + size - th - 6f, tw + 8f, th + 4f, PlateDark, 2);
                hud.DrawText(s, x + size - tw - 5f, y + size - th - 4f, Cream, null, 0f, lf);
            }
            // "Sold out" overlay when limited stock hit zero.
            if (!sellMode && stock == 0)
                hud.DrawText("Habis", x + size * 0.5f - hud.GetTextExtents("Habis", lf).Width * 0.5f,
                    y + size - hud.GetTextExtents("Habis", lf).Height - 4f, Cream, null, 0f, lf);
        }
        return hover;
    }

    private static void DrawButton(HUD hud, float x, float y, float w, float h, string label,
        Vector3 color, bool enabled, int fontSlot)
    {
        FillSolid(hud, x, y, w, h, enabled ? color : BtnDisabled, 3);
        float t = MathF.Max(1.5f, h * 0.08f);
        hud.DrawBox(x, y, w, t, WoodDark);
        hud.DrawBox(x, y + h - t, w, t, WoodDark);
        hud.DrawBox(x, y + t, t, h - t * 2f, WoodDark);
        hud.DrawBox(x + w - t, y + t, t, h - t * 2f, WoodDark);
        var ext = hud.GetTextExtents(label, fontSlot);
        hud.DrawText(label, x + (w - ext.Width) * 0.5f, y + (h - ext.Height) * 0.5f, Cream, null, 0f, fontSlot);
    }

    private static string ShopTooltip(InventorySystem.ItemDef def, ShopSystem.ShopEntry? entry, bool buy)
    {
        var lines = new List<string> { def.Name };
        if (!string.IsNullOrEmpty(def.EquipSlot))
        {
            lines.Add($"Equippable → {def.EquipSlot}");
            if (def.BonusHealth != 0) lines.Add($"  +{def.BonusHealth:0} HP");
            if (def.BonusMana != 0) lines.Add($"  +{def.BonusMana:0} MP");
            if (def.BonusDefense != 0) lines.Add($"  +{def.BonusDefense:0} DEF");
            if (def.BonusDamage != 0) lines.Add($"  +{def.BonusDamage:0} ATK");
        }
        if (!string.IsNullOrEmpty(def.UseEffect)) lines.Add($"Use: {def.UseEffect} {def.UseAmount:0}");
        if (buy && entry != null)
        {
            lines.Add($"Harga: {entry.PriceOverride ?? Math.Max(0, def.Price)} gold");
            if (entry.Stock >= 0) lines.Add($"Stok: {entry.Stock}");
        }
        else
        {
            lines.Add("Klik untuk memilih, lalu tekan Jual (S)");
        }
        if (!string.IsNullOrEmpty(def.Notes)) lines.Add(def.Notes);
        lines.Add(buy ? "Klik untuk memilih, lalu tekan Beli (B)" : "Item tas — hanya bisa dijual di toko ini");
        return string.Join('\n', lines);
    }

    /// <summary>Parchment tooltip near the mouse (same style as InventoryHud).</summary>
    private static void DrawTooltip(HUD hud, float mx, float my)
    {
        if (_tooltip.Length == 0) return;
        int labelFont = LabelFont(hud);
        var lines = _tooltip.Split('\n');
        float w = 0f, lineH = 0f;
        foreach (var l in lines)
        {
            w = MathF.Max(w, hud.GetTextExtents(l, labelFont).Width);
            lineH = MathF.Max(lineH, hud.GetTextExtents(l, labelFont).Height);
        }
        float padX = 10f, padY = 7f, lineStep = lineH + 3f;
        float boxW = w + padX * 2f, boxH = lines.Length * lineStep + padY * 2f;
        float bx = MathF.Min(mx + 14f, Glfw.WindowWidth - boxW - 4f);
        float by = MathF.Min(my + 16f, Glfw.WindowHeight - boxH - 4f);
        FillSolid(hud, bx, by, boxW, boxH, Parchment, 3);
        float t = 2f;
        hud.DrawBox(bx, by, boxW, t, WoodDark);
        hud.DrawBox(bx, by + boxH - t, boxW, t, WoodDark);
        hud.DrawBox(bx, by + t, t, boxH - t * 2f, WoodDark);
        hud.DrawBox(bx + boxW - t, by + t, t, boxH - t * 2f, WoodDark);
        for (int i = 0; i < lines.Length; i++)
        {
            var col = i == 0 ? InkBrown : InkSoft;
            hud.DrawText(lines[i], bx + padX, by + padY + i * lineStep, col, null, 0f, labelFont);
            HUD.TextOut(bx + padX, by + padY + i * lineStep, lines[i], col, fontSizePx: 14f);
        }
    }

    private static void ResetEdges()
    {
        _escWasDown = false;
        _leftWasDown = false;
        _bWasDown = false;
        _sWasDown = false;
    }
}
