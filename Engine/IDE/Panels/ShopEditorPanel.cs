using System;
using System.Collections.Generic;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Shop editor: author ShopDefs — id/name, sell percent, and the stock list
/// (item id + optional price override + stock cap). Persists per project to
/// Assets/Shops/shops.json (Save button + auto-load on project open, auto-clear
/// on close — wired in IDE.cs like the item catalog). NPCs bind to a shop id via
/// the "Open Shop" trigger/dialogue action.
/// </summary>
public class ShopEditorPanel
{
    private readonly IDEBridge _bridge;
    private int _selectedIndex = -1;
    private string _newShopId = "";
    private bool _show;
    private bool _dirty;

    public ShopEditorPanel(IDEBridge bridge) => _bridge = bridge;

    /// <summary>Re-sync panel state to the freshly loaded/cleared catalog (project open
    /// or close): select the first shop and clear the dirty flag. Stale indices from the
    /// previous project pointed at the wrong shop (or out of range).</summary>
    public void OnProjectChanged()
    {
        _selectedIndex = ShopSystem.Shops.Count > 0 ? 0 : -1;
        _dirty = false;
        _newShopId = "";
    }

    public void ShowInMenu()
    {
        if (ImGui.MenuItem("Shop Editor"))
        {
            _show = !_show;
            if (_show && ShopSystem.Shops.Count > 0 && _selectedIndex < 0)
                _selectedIndex = 0;
        }
    }

    public void Render()
    {
        if (!_show) return;
        ImGui.SetNextWindowSize(new Vector2(760, 520), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new Vector2(560, 380), new Vector2(1100, 860));
        if (!ImGui.Begin("Shop Editor", ref _show))
        {
            ImGui.End();
            return;
        }
        if (!Engine.Project.ProjectManager.IsProjectLoaded)
        {
            ImGui.TextDisabled("Buka project dulu — shop tersimpan di Assets/Shops/shops.json.");
            ImGui.End();
            return;
        }

        // ── Header: new shop row ──
        ImGui.InputText("##newshopid", ref _newShopId, 64);
        ImGui.SameLine();
        if (ImGui.Button("+ New Shop") && _newShopId.Trim().Length > 0)
        {
            string id = _newShopId.Trim();
            if (ShopSystem.GetShop(id) == null)
            {
                ShopSystem.Shops.Add(new ShopSystem.ShopDef { Id = id, Name = id });
                _selectedIndex = ShopSystem.Shops.Count - 1;
                _newShopId = "";
                _dirty = true;
            }
            else ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "ID sudah dipakai!");
        }
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.6f, 0.9f, 0.6f, 1f), _dirty ? "* unsaved" : "");

        if (ShopSystem.Shops.Count == 0)
        {
            ImGui.TextDisabled("Belum ada shop. Buat satu di atas, lalu bind NPC lewat action 'Open Shop'.");
            ImGui.End();
            return;
        }
        _selectedIndex = Math.Clamp(_selectedIndex, 0, ShopSystem.Shops.Count - 1);
        var shop = ShopSystem.Shops[_selectedIndex];

        // ── Shop selector + identity ──
        string[] ids = [.. ShopSystem.Shops.ConvertAll(s => $"{s.Name} ({s.Id})")];
        ImGui.SetNextItemWidth(280);
        if (ImGui.Combo("##shopselect", ref _selectedIndex, ids, ids.Length))
            _selectedIndex = Math.Clamp(_selectedIndex, 0, ShopSystem.Shops.Count - 1);

        string name = shop.Name;
        if (ImGui.InputText("Nama##shopname", ref name, 64)) { shop.Name = name; _dirty = true; }
        string id2 = shop.Id;
        if (ImGui.InputText("Id (dipakai action Open Shop)##shopid", ref id2, 64))
        { shop.Id = id2.Trim(); _dirty = true; }
        int sell = shop.SellPercent;
        if (ImGui.DragInt("Sell Percent (payout jual)", ref sell, 1, 0, 200))
        { shop.SellPercent = Math.Clamp(sell, 0, 200); _dirty = true; }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Payout jual = Price item x SellPercent / 100.\n50 = toko membeli setengah harga (klasik).");

        ImGui.Separator();

        // ── Stock list ──
        ImGui.Text($"Stock ({shop.Stock.Count}) — id item, harga (kosong = Price item), stok (kosong = tak terbatas)");
        for (int i = 0; i < shop.Stock.Count; i++)
        {
            var e = shop.Stock[i];
            ImGui.PushID($"st{i}");
            string iid = e.ItemId;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputText("##itemid", ref iid, 64)) { e.ItemId = iid.Trim(); _dirty = true; }
            ImGui.SameLine();
            var def = InventorySystem.Find(e.ItemId);
            if (def != null)
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), $"✓ {def.Name} (Price {def.Price})");
            else
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.3f, 1f), "ID tidak terdaftar");

            ImGui.SetNextItemWidth(110);
            string price = e.PriceOverride?.ToString() ?? "";
            if (ImGui.InputText("Harga (kosong=Price)##price", ref price, 16))
            {
                e.PriceOverride = string.IsNullOrWhiteSpace(price) ? null : int.TryParse(price.Trim(), out int p) ? Math.Max(0, p) : null;
                _dirty = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            string stock = e.Stock < 0 ? "" : e.Stock.ToString();
            if (ImGui.InputText("Stok (kosong=tak terbatas)##stock", ref stock, 16))
            {
                e.Stock = string.IsNullOrWhiteSpace(stock) ? -1 : int.TryParse(stock.Trim(), out int s) ? s : -1;
                _dirty = true;
            }
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
            if (ImGui.SmallButton("X")) { shop.Stock.RemoveAt(i); _dirty = true; ImGui.PopStyleColor(); ImGui.PopID(); continue; }
            ImGui.PopStyleColor();
            ImGui.PopID();
        }
        if (ImGui.Button("+ Add Stock"))
        {
            shop.Stock.Add(new ShopSystem.ShopEntry { ItemId = "", PriceOverride = null, Stock = -1 });
            _dirty = true;
        }

        ImGui.Separator();
        ImGui.TextDisabled("NPC memakai shop ini lewat action 'Open Shop' (Param = shop Id).\nPanel belanja terbuka di atas dialogue; Esc menutupnya balik ke dialogue.");
        if (ImGui.Button("Save Shops##saves"))
        {
            ShopSystem.Save();
            _dirty = false;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(juga tersimpan oleh Ctrl+S / Close Project / Exit)");
        ImGui.End();
    }
}
