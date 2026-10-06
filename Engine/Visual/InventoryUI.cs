using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// In-game inventory UI — rendered through the HUD BATCH PIPELINE (same as the
/// MainMenu UI: DrawBox/DrawText/DrawImage → Flush), so it lives INSIDE the scene
/// viewport texture, not an ImGui window.
///
/// Visual style follows the cozy pixel-art reference: warm PARCHMENT panels with
/// WOODEN brown frames, an "Inventory" title banner, a 3×5 paperdoll column with a
/// name plate on the left, a 6×6 item grid on the right, and gamepad-style hint
/// chips ([I] Close, right-click hints).
///
/// Input goes through GLFW directly (Keyboard/Mouse) with per-frame edge tracking —
/// NO ImGui dependency, so it works identically in GameScene HUD passes and the
/// editor's no-scene preview path (both end in hud.Flush()).
/// </summary>
public static class InventoryHud
{
    public static bool PanelOpen { get; private set; }

    /// <summary>Shared HUD instance for call sites that have no scene HUD (the
    /// editor's no-scene preview path). Constructed OUTSIDE the frame via Prewarm()
    /// so the font atlas bakes with clean GL state (mid-frame bakes produce the
    /// "boxes but no glyphs" bug the dialogue team already hit here).</summary>
    public static HUD? SharedHud { get; private set; }

    private static int _titleFont;   // 26px — "Inventory" banner
    private static int _labelFont;   // 16px — slot labels, chips
    private static bool _fontsReady;

    /// <summary>Bake the shared HUD (font atlas + big/label font slots) at a
    /// controlled time — call once after the GL context exists (IDE constructor).</summary>
    public static void Prewarm()
    {
        if (SharedHud != null) return;
        try
        {
            SharedHud = new HUD("Artifacts\\fonts\\Worldstar.ttf", 13f);
            _titleFont = SharedHud.GetOrCreateFontSlot("Artifacts\\fonts\\Worldstar.ttf", 26f);
            _labelFont = SharedHud.GetOrCreateFontSlot("Artifacts\\fonts\\Worldstar.ttf", 16f);
            _fontsReady = true;
        }
        catch (Exception ex) { Console.WriteLine($"[InventoryHud] Prewarm failed: {ex.Message}"); }
    }

    // Fancy fonts exist only on the shared HUD (other HUDs would need a mid-frame
    // bake — the empty-glyph bug); they fall back to slot 0.
    private static int TitleFont(HUD hud) => hud == SharedHud && _fontsReady ? _titleFont : 0;
    private static int LabelFont(HUD hud) => hud == SharedHud && _fontsReady ? _labelFont : 0;

    /// <summary>Window-px → scene-px mapper for DOCKED preview (the viewport shows a
    /// letterboxed scene texture; clicks must map through its inverse). Null/absent
    /// = identity (F8 fullscreen and real GameScene runs). Set per-frame by the
    /// ViewportPanel preview.</summary>
    public static Func<float, float, (float X, float Y)>? WindowToScene { get; set; }

    /// <summary>Overlay accept-filter: mirror draws ONLY this shared HUD's strings
    /// (its stb atlas can be GPU-empty → mirror is the real text path) plus untagged
    /// explicit TextOut entries. Auto-mirror entries from OTHER HUD instances
    /// (GameScene dialogue/debug lines — their stb renders fine) are skipped, or the
    /// overlay would draw a second copy on top of their stb text (double text).</summary>
    public static bool AcceptMirrored(HUD.TextOutItem item)
        => item.Source == null || ReferenceEquals(item.Source, SharedHud);

    /// <summary>Compact paperdoll display label — the cell labels are drawn at a
    /// 16px font under narrow cells, so "Head Accessories 1" would overlap its
    /// neighbours. The REAL slot name still drives equip/unequip logic + tooltips.</summary>
    private static string DollLabel(string slotName) => slotName switch
    {
        "Head Accessories 1" => "Head Acc 1",
        "Head Accessories 2" => "Head Acc 2",
        "Body Accessories 1" => "Body Acc 1",
        "Body Accessories 2" => "Body Acc 2",
        "Legs Accessories 1" => "Legs Acc 1",
        "Legs Accessories 2" => "Legs Acc 2",
        _ => slotName
    };

    // ── Palette (cozy parchment + wood, from the reference) ──
    private static readonly Vector3 Parchment = new(0.855f, 0.775f, 0.610f); // panel bg
    private static readonly Vector3 ParchmentDim = new(0.745f, 0.650f, 0.495f); // slot cells
    private static readonly Vector3 ParchmentHover = new(0.910f, 0.845f, 0.700f);
    private static readonly Vector3 WoodDark = new(0.360f, 0.225f, 0.120f);   // frame / banner
    private static readonly Vector3 WoodMid = new(0.520f, 0.345f, 0.195f);    // slot borders
    private static readonly Vector3 Cream = new(0.975f, 0.940f, 0.845f);      // light text
    private static readonly Vector3 InkBrown = new(0.280f, 0.175f, 0.095f);   // dark text
    private static readonly Vector3 InkSoft = new(0.475f, 0.360f, 0.240f);    // dim text
    private static readonly Vector3 HighlightGold = new(0.960f, 0.760f, 0.180f);
    private static readonly Vector3 PlateDark = new(0.185f, 0.120f, 0.070f);  // name plate

    // ── Layout (scene pixel space, scales with window height) ──
    private static float Slot => Math.Clamp(Glfw.WindowHeight * 0.075f, 50f, 72f);
    private static float Gap => Slot * 0.16f;
    private const int GridCols = 6, GridRows = 6;   // 36 slots shown 6×6 (like the ref)
    private const int DollCols = 3, DollRows = 5;   // 13 paperdoll slots in a 3×5 grid (15 cells)

    // ── Input edge state (per physical press) ──
    private static bool _iWasDown, _escWasDown;
    private static readonly bool[] _numWasDown = new bool[9];
    private static bool _leftWasDown, _rightWasDown;

    // Hover state for tooltips (scene-px rect of the last hovered slot this frame).
    private static string _tooltip = "";

    // Hover state for the ALWAYS-visible info summary — which item the cursor is on
    // this frame ("mouse over → kasih informasi desc itemnya"). Falls back to the
    // first grid slot when nothing is hovered.
    private static InventorySystem.ItemDef? _hoverDef;
    private static int _hoverCount;
    private static string? _hoverEquip;

    // Mouse in SCENE-px (mapped through WindowToScene when docked — identity/full-
    // screen otherwise). ALL slot hit-tests must use THIS, not a raw Mouse.GetPosition():
    // the raw cursor is in WINDOW px while slots are drawn in TEXTURE px — in the
    // docked preview those differ (letterbox origin + scale), so the hover highlight
    // landed one slot to the side of the real cursor (fullscreen happened to match). 
    private static float _mx, _my;

    // ── Transient feedback message (bottom-center above the hotbar) ──
    private static string _flash = "";
    private static float _flashTime;
    private static float _dt = 1f / 60f;

    private static void Flash(string msg) { _flash = msg; _flashTime = 2.2f; }

    /// <summary>Show the bottom-center feedback message from OUTSIDE the HUD (loot
    /// vacuum pickups, external systems). Same visual as internal flashes.</summary>
    public static void PushFlash(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        Flash(msg);
    }

    /// <summary>Update input + draw everything. Call ONCE per frame before the HUD
    /// flush (self-gates on the session flag — edit mode draws nothing).
    /// NOTE: renders with the session opacity of a REAL overlay — the parchment style
    /// assumes it draws ON TOP of the scene (like the preview path). The GameScene
    /// docked path must flush with FlushWithBackdrop so the backdrop stays UNDER the
    /// queue (a queued backdrop image would overdraw these panels).</summary>
    public static void Render(HUD hud, float dt)
    {
        if (hud == null || !Player2DStats.SessionActive)
        {
            PanelOpen = false;
            ResetEdges();
            return;
        }
        _dt = MathF.Max(0.0001f, dt);
        bool dialogueOwnsKeys = DialogueSystem.IsConversationActive || ShopHud.IsOpen;

        // ══════════ INPUT (GLFW, physical edge) ══════════
        nint window = Glfw.GetWindow();
        if (!dialogueOwnsKeys)
        {
            bool iDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_I);
            if (iDown && !_iWasDown) PanelOpen = !PanelOpen;
            _iWasDown = iDown;

            if (PanelOpen)
            {
                bool escDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);
                if (escDown && !_escWasDown) PanelOpen = false;
                _escWasDown = escDown;
            }

            // Hotbar quick-use 1..9 (only when the panel is closed so number keys
            // don't double-fire with grid right-click in the same frame).
            if (!PanelOpen)
            {
                for (int i = 0; i < 9; i++)
                {
                    bool down = Keyboard.IsKeyDown(window, Const.GLFW_KEY_1 + i);
                    if (down && !_numWasDown[i])
                    {
                        string r = InventorySystem.UseSlot(i);
                        if (r.Length > 0) Flash(r);
                    }
                    _numWasDown[i] = down;
                }
            }
        }
        else
        {
            _iWasDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_I);
            ResetEdges();
        }

        // ══════════ LAYOUT + MOUSE ══════════
        // Cursor source = ImGui's MousePos (screen space — the SAME space the
        // ViewportPanel letterbox rect lives in), valid in EVERY IDE path (docked AND
        // F8 fullscreen — both run inside an ImGui frame). GLFW cursor is only the
        // fallback for HUD paths outside an ImGui frame: raw GLFW coords live in
        // OS screen units while ImGui/letterbox rects live in framebuffer px — on
        // Windows display scaling ≠ 100% they DIVERGE and every hover/click lands
        // offset by the scale factor ("mouse tidak akurat").
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
        // Docked preview / F8 fullscreen: map through the published letterbox inverse.
        if (WindowToScene != null)
            (mx, my) = WindowToScene(mx, my);
        _mx = mx; _my = my; // single source of truth for every hover test this frame
        bool leftDown = Mouse.IsButtonDown(0);
        bool rightDown = Mouse.IsButtonDown(1); // GLFW: 0=left, 1=right
        bool leftPressed = leftDown && !_leftWasDown;
        bool rightPressed = rightDown && !_rightWasDown;
        _leftWasDown = leftDown;
        _rightWasDown = rightDown;
        _tooltip = "";
        _hoverDef = null; _hoverCount = 0; _hoverEquip = null;

        // HIDE DURING CONVERSATION (user: "sembunyikan hotbar saat dialog") —
        // hotbar + gold chip + flash + tooltip render NOTHING (neither the batch
        // queue nor the FrameTextOut mirror) while a conversation is active, so no
        // inventory element can punch through the conversation window on ANY path
        // (preview overlay draws mirror AFTER the dialogue; in-game flushes the
        // shared inventory HUD AFTER the main HUD — both put it on top). Panel
        // state (PanelOpen) is kept, so an open [I] panel reappears after the
        // dialogue ends; inputs are already gated by dialogueOwnsKeys above.
        if (DialogueSystem.IsConversationActive)
            return;

        DrawHotbar(hud, mx, my, leftPressed, rightPressed);
        if (PanelOpen) DrawPanel(hud, mx, my, leftPressed, rightPressed);
        DrawFlash(hud);
        if (PanelOpen) DrawInfoSummary(hud, mx, my);
        // Tooltip LAST (Z paling depan): the overlay draws FrameTextOut in INSERT
        // ORDER, so the tooltip's mirrored plate + lines must be queued after every
        // other HUD entry (summary/labels/badges) to render above them.
        DrawTooltip(hud, mx, my);
    }

    /// <summary>ALWAYS-ON item info summary (user: "kalau teksnya kosong, kasih info
    /// qty + short desc" / "mouse over → kasih informasi desc itemnya") — name (qty)
    /// + wrapped notes line, drawn INSIDE the panel under the name plate (parchment
    /// column — the old grid-bottom spot straddled the panel frame / scene). Shows
    /// the HOVERED item when the cursor is on a slot (grid/hotbar/paperdoll — live
    /// hover desc), otherwise the FIRST non-empty grid slot; the full tooltip on
    /// hover still gives the complete breakdown.</summary>
    private static void DrawInfoSummary(HUD hud, float mx, float my)
    {
        // Item under the cursor first; fall back to the first grid item.
        InventorySystem.ItemDef? def = _hoverDef;
        int count = _hoverCount;
        if (def == null)
        {
            for (int i = 0; i < InventorySystem.GridSize; i++)
            {
                ref var slotData = ref InventorySystem.Grid[i];
                if (slotData.IsEmpty) continue;
                def = InventorySystem.Find(slotData.ItemId);
                if (def == null) continue;
                count = slotData.Count;
                break;
            }
        }
        if (def == null) return;

        // LEFT column under the name plate — INSIDE the parchment panel. The old
        // grid-bottom position landed on the panel's bottom frame and the scene
        // below ("tumpang tindih" with the world); the doll column always ends
        // above the grid bottom, so there is free parchment under the plate.
        float slot = Slot, gap = Gap, pad = slot * 0.45f;
        float bannerH = slot * 0.85f;
        float dollW = DollCols * slot + (DollCols - 1) * gap;
        float gridW = GridCols * slot + (GridCols - 1) * gap;
        float gridH = GridRows * slot + (GridRows - 1) * gap;
        float nameH = slot * 0.55f;
        float labelH = hud.GetTextExtents("Head", LabelFont(hud)).Height + 4f;
        float dollH = DollRows * (slot + gap + labelH) + gap + nameH;
        float bodyH = MathF.Max(gridH, dollH);
        float panelW = pad * 2 + dollW + pad + gridW;
        float panelH = pad + bannerH + pad * 0.6f + bodyH + pad;
        float dx = (Glfw.WindowWidth - panelW) * 0.5f + pad;                 // doll column left
        float bodyY = (Glfw.WindowHeight - panelH) * 0.5f + pad + bannerH + pad * 0.6f;
        float plateY = bodyY + DollRows * (slot + gap + labelH) - gap + 2f; // same math as DrawPanel
        float px = dx;
        float py = plateY + nameH + gap * 0.5f + 6f;
        float maxW = dollW + pad * 0.8f; // may spill into the parchment gap, never onto the grid

        // Equip slot suffix when hovering the paperdoll ("Iron Helm (1) [Head]").
        string title = $"{def.Name} ({count})" + (_hoverEquip != null ? $"  [{_hoverEquip}]" : "");
        float ty = py;
        foreach (var l in hud.WordWrapText(title, maxW, 0))
        {
            HUD.TextOut(px, ty, l, HighlightGold * 0.95f, fontSizePx: 15f);
            ty += 17f;
        }
        string desc = !string.IsNullOrEmpty(def.Notes)
            ? def.Notes
            : !string.IsNullOrEmpty(def.UseEffect) ? $"Use: {def.UseEffect} {def.UseAmount:0}"
            : !string.IsNullOrEmpty(def.EquipSlot) ? $"Equippable -> {def.EquipSlot}"
            : "";
        if (desc.Length > 0)
        {
            foreach (var l in hud.WordWrapText(desc, maxW, 0))
            {
                HUD.TextOut(px, ty, l, InkSoft, fontSizePx: 13f);
                ty += 15f;
            }
        }
        if (def.Price > 0)
            HUD.TextOut(px, ty, $"Price: {def.Price} gold", InkSoft, fontSizePx: 13f);
    }

    private static void ResetEdges()
    {
        _escWasDown = false;
        for (int i = 0; i < 9; i++) _numWasDown[i] = false;
        _leftWasDown = false;
        _rightWasDown = false;
    }

    // ═══════════════════════ Style primitives ═══════════════════════

    /// <summary>OPAQUE fill via HUD.DrawSolidBox — the quad replaces the framebuffer
    /// (blend off), so panels/banner/plates read as solid parchment over ANY scene.
    /// The old 3× DrawBox stack at the shader's 0.6 alpha still washed out over bright
    /// maps and its interleaved groups lost draw order (images overdrawn by later
    /// boxes) — "layer HUD tembus cahaya". `layers` is kept for call-site compat and
    /// ignored.</summary>
    private static void FillSolid(HUD hud, float x, float y, float w, float h, Vector3 color, int layers = 3)
    {
        hud.DrawSolidBox(x, y, w, h, color);
    }

    /// <summary>Wooden frame: thick dark outer border + thin mid-brown inner line
    /// (the double-frame look of the reference panels).</summary>
    private static void WoodFrame(HUD hud, float x, float y, float w, float h)
    {
        float t = MathF.Max(3f, Slot * 0.075f);
        // Outer dark frame.
        hud.DrawBox(x, y, w, t, WoodDark);
        hud.DrawBox(x, y + h - t, w, t, WoodDark);
        hud.DrawBox(x, y + t, t, h - t * 2f, WoodDark);
        hud.DrawBox(x + w - t, y + t, t, h - t * 2f, WoodDark);
        // Inner accent line.
        float t2 = MathF.Max(1.5f, t * 0.4f);
        float i2 = t * 1.4f;
        hud.DrawBox(x + i2, y + i2, w - i2 * 2f, t2, WoodMid);
        hud.DrawBox(x + i2, y + h - i2 - t2, w - i2 * 2f, t2, WoodMid);
        hud.DrawBox(x + i2, y + i2 + t2, t2, h - i2 * 2f - t2 * 2f, WoodMid);
        hud.DrawBox(x + w - i2 - t2, y + i2 + t2, t2, h - i2 * 2f - t2 * 2f, WoodMid);
    }

    /// <summary>Gamepad-style hint chip: dark rounded-ish square + cream letter(s).</summary>
    private static void HintChip(HUD hud, float x, float y, float size, string label, int fontSlot)
    {
        FillSolid(hud, x, y, size, size, WoodDark, 2);
        float tw = hud.GetTextExtents(label, fontSlot).Width;
        float th = hud.GetTextExtents(label, fontSlot).Height;
        hud.DrawText(label, x + (size - tw) * 0.5f, y + (size - th) * 0.5f, Cream, null, 0f, fontSlot);
        HUD.TextOut(x + (size - tw) * 0.5f, y + (size - th) * 0.5f, label, Cream,
            fontSizePx: hud.GetFontSlotSize(fontSlot));
    }

    // ═══════════════════════ Slot drawing ═══════════════════════

    /// <summary>One inventory slot: inset parchment cell + cropped icon + count badge.
    /// Returns true when the mouse hovers it this frame (hit-test in MAPPED scene-px —
    /// raw window px breaks the docked preview, see _mx/_my).</summary>
    private static bool DrawSlot(HUD hud, float x, float y, float size,
        InventorySystem.ItemDef? def, int count, bool highlighted, bool dragging)
    {
        bool hover = _mx >= x && _mx < x + size && _my >= y && _my < y + size;

        hud.DrawBox(x, y, size, size, dragging ? ParchmentHover : hover ? ParchmentHover : ParchmentDim);
        var border = (highlighted || dragging) ? HighlightGold : WoodMid;
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
                // GetIconUV returns IMAGE-space v (vTop < vBottom, row 0 = top);
                // HUD textures share that orientation → pass through directly.
                var (u0, vTop, u1, vBottom) = InventorySystem.GetIconUV(def);
                float pad = size * 0.12f;
                hud.DrawImageUV(x + pad, y + pad, size - pad * 2f, size - pad * 2f,
                    tex, u0, vTop, u1, vBottom);
            }
            else
            {
                // Sheet not registered — grey placeholder block.
                hud.DrawBox(x + size * 0.25f, y + size * 0.25f, size * 0.5f, size * 0.5f,
                    new Vector3(0.45f, 0.45f, 0.5f));
            }
            // Quantity badge (bottom-right, dark chip + cream NUMBER) — ALWAYS visible
            // when the slot holds an item (user: "beri informasi jumlah itemnya").
            // Plain ASCII digits: the mirror font/atlas has NO × glyph (rendered as
            // '?'), and the grid has no slot-index chip so a bare number is
            // unambiguous (the hotbar's index chip is top-left, opposite corner).
            // Chip quad renders via the HUD batch; the number mirrors to the overlay.
            if (count > 0)
            {
                string text = count > 999 ? "999+" : count.ToString();
                var bExt = hud.GetTextExtents(text, LabelFont(hud));
                float cx = x + size - bExt.Width - 6f, cy = y + size - bExt.Height - 4f;
                FillSolid(hud, cx - 5f, cy - 3f, bExt.Width + 10f, bExt.Height + 6f, PlateDark, 2);
                hud.DrawText(text, cx, cy, Cream, null, 0f, LabelFont(hud));
                HUD.TextOut(cx, cy, text, Cream, fontSizePx: hud.GetFontSlotSize(LabelFont(hud)));
            }
        }
        return hover;
    }

    // ═══════════════════════ Panel (Inventory window) ═══════════════════════

    private static void DrawPanel(HUD hud, float mx, float my, bool leftPressed, bool rightPressed)
    {
        float slot = Slot, gap = Gap, pad = slot * 0.45f;
        float bannerH = slot * 0.85f;

        float gridW = GridCols * slot + (GridCols - 1) * gap;
        float dollW = DollCols * slot + (DollCols - 1) * gap;
        float gridH = GridRows * slot + (GridRows - 1) * gap;
        float nameH = slot * 0.55f;
        float labelH = hud.GetTextExtents("Head", LabelFont(hud)).Height + 4f;
        // Each doll row = cell + gap + label UNDER it; the plate sits one gap lower.
        float dollH = DollRows * (slot + gap + labelH) + gap + nameH;
        float bodyH = MathF.Max(gridH, dollH);

        float panelW = pad * 2 + dollW + pad + gridW;
        float panelH = pad + bannerH + pad * 0.6f + bodyH + pad;

        float px = (Glfw.WindowWidth - panelW) * 0.5f;
        float py = (Glfw.WindowHeight - panelH) * 0.5f;

        // Parchment panel + wooden frame.
        FillSolid(hud, px, py, panelW, panelH, Parchment, 3);
        WoodFrame(hud, px, py, panelW, panelH);

        // ── Title banner: dark wood strip with "Inventory" + close chip + gold ──
        float bx = px + pad, by = py + pad, bw = panelW - pad * 2f;
        FillSolid(hud, bx, by, bw, bannerH, WoodDark, 3);
        string title = "Inventory";
        var titleExt = hud.GetTextExtents(title, TitleFont(hud));
        hud.DrawText(title, bx + pad * 0.7f, by + (bannerH - titleExt.Height) * 0.5f, Cream, null, 0f, TitleFont(hud));
        // Title mirrors to the ImGui fallback overlay (26px) — stb-atlas glyphs can be
        // invisible in the editor preview.
        HUD.TextOut(bx + pad * 0.7f, by + (bannerH - titleExt.Height) * 0.5f, title, Cream,
            fontSizePx: hud.GetFontSlotSize(TitleFont(hud)));

        float chip = bannerH * 0.62f;
        HintChip(hud, bx + bw - chip - pad * 0.6f, by + (bannerH - chip) * 0.5f, chip, "I", LabelFont(hud));
        string gold = $"Gold {InventorySystem.Gold}";
        float goldW = hud.GetTextExtents(gold, LabelFont(hud)).Width;
        hud.DrawText(gold, bx + bw - chip - pad * 1.2f - goldW, by + (bannerH - hud.GetTextExtents(gold, LabelFont(hud)).Height) * 0.5f,
            HighlightGold, null, 0f, LabelFont(hud));

        float bodyY = by + bannerH + pad * 0.6f;

        // ── Left: paperdoll (2×3) + name plate ──
        float dx = px + pad;
        float dy = bodyY;
        string[] labels = InventorySystem.EquipSlots;
        for (int r = 0; r < DollRows; r++)
        {
            for (int c = 0; c < DollCols; c++)
            {
                int di = r * DollCols + c;
                if (di >= labels.Length) break;
                string slotName = labels[di];
                string id = InventorySystem.PlayerEquipment.Get(slotName);
                var def = string.IsNullOrEmpty(id) ? null : InventorySystem.Find(id);
                float x = dx + c * (slot + gap);
                // count=0: equipment never stacks — no qty badge on paperdoll cells
                // (the grid/hotbar carry the quantity info).
                bool hover = DrawSlot(hud, x, dy, slot, def, 0,
                    highlighted: false, dragging: false);
                // Label under the cell (compact display label — the real slot name
                // still drives the unequip logic and tooltip below).
                string dollLabel = DollLabel(slotName);
                var labExt = hud.GetTextExtents(dollLabel, LabelFont(hud));
                hud.DrawText(dollLabel, x + (slot - labExt.Width) * 0.5f, dy + slot + 2f, InkSoft, null, 0f, LabelFont(hud));

                if (hover)
                {
                    if (def != null)
                    {
                        _tooltip = BuildTooltip(def, 1, slotName);
                        _hoverDef = def; _hoverCount = 1; _hoverEquip = slotName;
                        if (leftPressed)
                        {
                            if (InventorySystem.UnequipToGrid(slotName)) Flash($"{slotName} unequipped");
                            else Flash("Inventory full!");
                        }
                    }
                    else
                    {
                        _tooltip = $"{slotName}\nRight-click a grid item to equip";
                    }
                }
            }
            dy += slot + gap + labelH;
        }

        // Name plate under the doll (like the reference's dark "Name" strip).
        float plateY = dy - gap + 2f;
        FillSolid(hud, dx, plateY, dollW, nameH, PlateDark, 3);
        string name = "Player";
        var nameExt = hud.GetTextExtents(name, LabelFont(hud));
        hud.DrawText(name, dx + (dollW - nameExt.Width) * 0.5f, plateY + (nameH - nameExt.Height) * 0.5f,
            Cream, null, 0f, LabelFont(hud));
        HUD.TextOut(dx + (dollW - nameExt.Width) * 0.5f, plateY + (nameH - nameExt.Height) * 0.5f,
            name, Cream, fontSizePx: hud.GetFontSlotSize(LabelFont(hud)));

        // ── Right: 6×6 grid ──
        float gx = px + pad + dollW + pad;
        float gy = bodyY;
        int drag = InventorySystem.DragSlot;
        for (int i = 0; i < InventorySystem.GridSize; i++)
        {
            int row = i / GridCols, col = i % GridCols;
            ref var slotData = ref InventorySystem.Grid[i];
            var def = slotData.IsEmpty ? null : InventorySystem.Find(slotData.ItemId);
            float x = gx + col * (slot + gap);
            float y = gy + row * (slot + gap);
            bool hover = DrawSlot(hud, x, y, slot, def, slotData.IsEmpty ? 0 : slotData.Count,
                highlighted: drag >= 0 && i != drag && def != null, dragging: i == drag);

            if (hover)
            {
                if (def != null)
                {
                    _tooltip = BuildTooltip(def, slotData.Count, null);
                    _hoverDef = def; _hoverCount = slotData.Count; _hoverEquip = null;
                }
                if (leftPressed)
                {
                    if (drag < 0)
                    {
                        if (!slotData.IsEmpty)
                            InventorySystem.DragSlot = i; // pick up
                    }
                    else if (drag != i)
                    {
                        InventorySystem.MoveSlot(drag, i); // drop: merge or swap
                        InventorySystem.DragSlot = -1;
                    }
                    else
                        InventorySystem.DragSlot = -1; // click itself = cancel
                }
                if (rightPressed && def != null && drag < 0)
                {
                    string r = InventorySystem.UseSlot(i);
                    if (r.Length > 0) Flash(r);
                }
            }
        }

        // Drag status line under the grid.
        if (drag >= 0)
        {
            string dragText = "Click another slot to drop - itself to cancel";
            var dExt = hud.GetTextExtents(dragText, LabelFont(hud));
            hud.DrawText(dragText, gx + (gridW - dExt.Width) * 0.5f,
                gy + gridH + 4f, InkBrown, null, 0f, LabelFont(hud));
        }
    }

    private static string BuildTooltip(InventorySystem.ItemDef def, int count, string? equipSlot)
    {
        var lines = new List<string>
        {
            def.Name + (count > 0 ? $" ({count})" : "")
        };
        if (!string.IsNullOrEmpty(def.EquipSlot))
        {
            lines.Add($"Equippable -> {def.EquipSlot}");
            if (def.BonusHealth != 0) lines.Add($"  +{def.BonusHealth:0} HP");
            if (def.BonusMana != 0) lines.Add($"  +{def.BonusMana:0} MP");
            if (def.BonusDefense != 0) lines.Add($"  +{def.BonusDefense:0} DEF");
            if (def.BonusDamage != 0) lines.Add($"  +{def.BonusDamage:0} ATK");
        }
        if (!string.IsNullOrEmpty(def.UseEffect)) lines.Add($"Use: {def.UseEffect} {def.UseAmount:0}");
        if (def.Price > 0) lines.Add($"Price: {def.Price} gold");
        if (!string.IsNullOrEmpty(def.Notes)) lines.Add(def.Notes);
        lines.Add(equipSlot != null ? "Click = unequip"
            : !string.IsNullOrEmpty(def.EquipSlot) ? "Right-click = equip"
            : "Right-click = use");
        return string.Join('\n', lines);
    }

    /// <summary>Parchment tooltip near the mouse (multi-line, wooden edge).</summary>
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
        // Thin wooden edge (single 2px lines — frame helper would be too chunky here).
        float t = 2f;
        hud.DrawBox(bx, by, boxW, t, WoodDark);
        hud.DrawBox(bx, by + boxH - t, boxW, t, WoodDark);
        hud.DrawBox(bx, by + t, t, boxH - t * 2f, WoodDark);
        hud.DrawBox(bx + boxW - t, by + t, t, boxH - t * 2f, WoodDark);
        // Z PALING DEPAN: HUD.Flush renders ALL slot images AFTER every box — a
        // batched plate can never cover the hovered slot's icon ("ikon potion
        // menembus kotak tooltip"). Mirror the plate (fill + wooden edge) into the
        // overlay list BEFORE the text lines: the overlay composites ON TOP of the
        // scene texture and draws in insert order. Batch fill/box above stays as
        // fallback for paths without an overlay.
        HUD.RectOut(bx, by, boxW, boxH, Parchment, 1f, hud);
        HUD.RectOut(bx, by, boxW, t, WoodDark, 1f, hud);
        HUD.RectOut(bx, by + boxH - t, boxW, t, WoodDark, 1f, hud);
        HUD.RectOut(bx, by + t, t, boxH - t * 2f, WoodDark, 1f, hud);
        HUD.RectOut(bx + boxW - t, by + t, t, boxH - t * 2f, WoodDark, 1f, hud);
        for (int i = 0; i < lines.Length; i++)
        {
            var col = i == 0 ? InkBrown : InkSoft;
            hud.DrawText(lines[i], bx + padX, by + padY + i * lineStep, col, null, 0f, labelFont);
            HUD.TextOut(bx + padX, by + padY + i * lineStep, lines[i], col,
                fontSizePx: hud.GetFontSlotSize(labelFont));
        }
    }

    // ═══════════════════════ Hotbar ═══════════════════════

    private static void DrawHotbar(HUD hud, float mx, float my, bool leftPressed, bool rightPressed)
    {
        float slot = Slot, gap = Gap;
        float chip = slot * 0.5f;
        float w = 9 * (slot + gap) + chip + 26f;
        float x0 = (Glfw.WindowWidth - w) * 0.5f;
        float y0 = Glfw.WindowHeight - slot - 16f;

        for (int i = 0; i < 9; i++)
        {
            ref var slotData = ref InventorySystem.Grid[i];
            var def = slotData.IsEmpty ? null : InventorySystem.Find(slotData.ItemId);
            float x = x0 + i * (slot + gap);
            bool hover = DrawSlot(hud, x, y0, slot, def, slotData.IsEmpty ? 0 : slotData.Count,
                highlighted: false, dragging: false);

            // Slot number chip (top-left, solid dark square + cream digit). Sized to
            // fit the label font — at 0.30×slot the 16px digit clipped on 50px slots
            // and the chip read as empty ("nomor slot tidak kelihatan").
            float numChip = slot * 0.36f;
            FillSolid(hud, x + 3f, y0 + 3f, numChip, numChip, PlateDark, 2);
            string num = (i + 1).ToString();
            var nExt = hud.GetTextExtents(num, LabelFont(hud));
            hud.DrawText(num, x + 3f + (numChip - nExt.Width) * 0.5f, y0 + 3f + (numChip - nExt.Height) * 0.5f,
                Cream, null, 0f, LabelFont(hud));
            HUD.TextOut(x + 3f + (numChip - nExt.Width) * 0.5f, y0 + 3f + (numChip - nExt.Height) * 0.5f,
                num, Cream, fontSizePx: hud.GetFontSlotSize(LabelFont(hud)));

            if (hover && def != null)
            {
                _tooltip = BuildTooltip(def, slotData.Count, null);
                _hoverDef = def; _hoverCount = slotData.Count; _hoverEquip = null;
                if (leftPressed)
                {
                    string r = InventorySystem.UseSlot(i);
                    if (r.Length > 0) Flash(r);
                }
            }
        }

        // Gold + [I] hint chips at the right end of the strip.
        float hx = x0 + 9 * (slot + gap) + 6f;
        string gold = $"Gold {InventorySystem.Gold}";
        var gExt = hud.GetTextExtents(gold, LabelFont(hud));
        hud.DrawText(gold, hx + (chip - gExt.Width) * 0.5f, y0 + chip * 0.35f, HighlightGold, null, 0f, LabelFont(hud));
        HintChip(hud, hx, y0 + slot - chip - 2f, chip, "I", LabelFont(hud));
    }

    // ═══════════════════════ Flash message ═══════════════════════

    private static void DrawFlash(HUD hud)
    {
        if (_flashTime <= 0f || _flash.Length == 0) return;
        _flashTime -= _dt;
        float a = Math.Clamp(_flashTime / 0.6f, 0f, 1f);
        // HUD text has no per-call alpha — dim the color toward the background instead.
        var col = Vector3.Lerp(new Vector3(0.05f, 0.05f, 0.08f), Cream, a);
        float w = hud.GetTextExtents(_flash).Width;
        hud.DrawText(_flash, (Glfw.WindowWidth - w) * 0.5f, Glfw.WindowHeight - Slot - 44f, col);
        HUD.TextOut((Glfw.WindowWidth - w) * 0.5f, Glfw.WindowHeight - Slot - 44f, _flash, col, a,
            fontSizePx: hud.GetFontSlotSize(0));
    }
}
