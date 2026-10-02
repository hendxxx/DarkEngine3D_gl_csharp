using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Project;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Quest bookkeeping for the HUD: a per-project catalog (Assets/Quests/quests.json)
/// of QuestDefs — display name, description/objective lines, reward text. Quest
/// STATE stays where it already lives (DialogueSystem flags quest_&lt;id&gt;_active /
/// quest_&lt;id&gt;_done, set by the Activate Quest / Complete Quest trigger actions) —
/// the catalog only supplies DISPLAY data, so saves/debugging/conditions keep working
/// unchanged. QuestHud draws the active-quest tracker + a [Q] quest log panel.
/// </summary>
public static class QuestSystem
{
    /// <summary>Display data for one quest. Objectives are plain lines (the engine
    /// does not auto-track item counts — write them player-readable, e.g.
    /// "Kumpulkan 3 Ember (dari chest dungeon)").</summary>
    public sealed class QuestDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "New Quest";
        public string Description { get; set; } = "";
        /// <summary>Objective lines shown in the tracker/log (one per line).</summary>
        public List<string> Objectives { get; set; } = new();
        /// <summary>Reward summary line ("20 Gold + 1 Potion") shown in the log.</summary>
        public string RewardText { get; set; } = "";
        /// <summary>Hide from the tracker/log until activated (hidden quests never
        /// show as "done" either — spoiler control).</summary>
        public bool Hidden { get; set; }
    }

    // ════════════════════════════════════════════
    //  CATALOG (per project, quests.json)
    // ════════════════════════════════════════════

    public static readonly List<QuestDef> Quests = new();

    public static QuestDef? GetQuest(string id) =>
        string.IsNullOrWhiteSpace(id) ? null
        : Quests.FirstOrDefault(q => string.Equals(q.Id, id, StringComparison.OrdinalIgnoreCase));

    public static void ClearCatalog() => Quests.Clear();

    private static string GetFilePath()
    {
        string dir = ProjectManager.IsProjectLoaded
            ? Path.Combine(ProjectManager.ProjectRoot!, "Assets", "Quests")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Quests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "quests.json");
    }

    private sealed class QuestFileData
    {
        public List<QuestDef> Quests { get; set; } = [];
    }

    public static void LoadCatalog()
    {
        Quests.Clear();
        if (!ProjectManager.IsProjectLoaded) return;
        try
        {
            string path = GetFilePath();
            if (!File.Exists(path)) return;
            var data = JsonSerializer.Deserialize<QuestFileData>(File.ReadAllText(path));
            if (data?.Quests != null) Quests.AddRange(data.Quests);
            Console.WriteLine($"[Quest] Loaded {Quests.Count} quest(s) → {path}");
        }
        catch (Exception ex) { Console.WriteLine($"[Quest] Load failed: {ex.Message}"); }
    }

    public static void Save()
    {
        try
        {
            if (!ProjectManager.IsProjectLoaded)
            {
                Console.WriteLine("[Quest] Save skipped: no project open");
                return;
            }
            File.WriteAllText(GetFilePath(),
                JsonSerializer.Serialize(new QuestFileData { Quests = Quests }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[Quest] Saved {Quests.Count} quest(s) → {GetFilePath()}");
        }
        catch (Exception ex) { Console.WriteLine($"[Quest] Save failed: {ex.Message}"); }
    }

    // ════════════════════════════════════════════
    //  STATE (reads the DialogueSystem quest flags)
    // ════════════════════════════════════════════

    public static bool IsActive(string id) => DialogueSystem.HasFlag($"quest_{id}_active");
    public static bool IsDone(string id) => DialogueSystem.HasFlag($"quest_{id}_done");

    /// <summary>Catalog quests that are currently ACTIVE (tracker order).</summary>
    public static List<QuestDef> ActiveQuests() =>
        Quests.Where(q => !q.Hidden && IsActive(q.Id) && !IsDone(q.Id)).ToList();

    /// <summary>Catalog quests already turned in (log's SELESAI section).</summary>
    public static List<QuestDef> DoneQuests() =>
        Quests.Where(q => !q.Hidden && IsDone(q.Id)).ToList();

    // ════════════════════════════════════════════
    //  HOOKS (called by the Activate/Complete Quest trigger actions)
    // ════════════════════════════════════════════

    /// <summary>Called after quest_&lt;id&gt;_active is set. Unknown ids auto-register a
    /// minimal placeholder so the tracker still shows something (author later in the
    /// quests.json / Quest Editor).</summary>
    public static void OnActivated(string questId)
    {
        var def = GetQuest(questId);
        if (def == null)
        {
            def = new QuestDef { Id = questId, Name = questId, Description = "(belum diatur - tambahkan di Assets/Quests/quests.json)" };
            Quests.Add(def);
            Console.WriteLine($"[Quest] Auto-registered placeholder for '{questId}'");
        }
        InventoryHud.PushFlash($"Quest baru: {def.Name}");
        Console.WriteLine($"[Quest] Tracker → '{def.Name}' active");
    }

    /// <summary>Called after quest_&lt;id&gt;_done is set — flashes the completion.</summary>
    public static void OnCompleted(string questId)
    {
        var def = GetQuest(questId);
        string name = def?.Name ?? questId;
        InventoryHud.PushFlash($"[OK] Quest selesai: {name}");
        Console.WriteLine($"[Quest] Tracker → '{name}' done");
    }
}

/// <summary>
/// Quest HUD: an always-on TRACKER (top-left, under the HP/MP bars — active quests
/// with their objective lines) and a [Q] QUEST LOG panel (parchment/wood style
/// shared with the inventory — AKTIF and SELESAI sections with objectives and
/// rewards). HUD batch pipeline on the SHARED HUD (font parity with the preview);
/// GLFW edge input; read-only so no mouse mapping is needed.
/// </summary>
public static class QuestHud
{
    public static bool LogOpen { get; private set; }

    /// <summary>Toggle the tracker entirely (some games prefer a key to show it).</summary>
    public static bool TrackerVisible { get; set; } = true;

    private static bool _qWasDown, _escWasDown;

    // Fonts on the shared HUD (InventoryHud owns the instance).
    private static int _titleFont, _labelFont;

    /// <summary>Bake font slots on InventoryHud.SharedHud (call AFTER its Prewarm —
    /// mid-frame bakes produce the empty-glyph atlas bug).</summary>
    public static void Prewarm()
    {
        if (InventoryHud.SharedHud == null || _labelFont != 0) return;
        _titleFont = InventoryHud.SharedHud.GetOrCreateFontSlot("Artifacts\\fonts\\Worldstar.ttf", 22f);
        _labelFont = InventoryHud.SharedHud.GetOrCreateFontSlot("Artifacts\\fonts\\Worldstar.ttf", 15f);
    }

    private static int TitleFont(HUD hud) => hud == InventoryHud.SharedHud && _titleFont != 0 ? _titleFont : 0;
    private static int LabelFont(HUD hud) => hud == InventoryHud.SharedHud && _labelFont != 0 ? _labelFont : 0;

    // ── Palette (same parchment set as InventoryHud/ShopHud) ──
    private static readonly Vector3 Parchment = new(0.855f, 0.775f, 0.610f);
    private static readonly Vector3 ParchmentDim = new(0.745f, 0.650f, 0.495f);
    private static readonly Vector3 WoodDark = new(0.360f, 0.225f, 0.120f);
    private static readonly Vector3 WoodMid = new(0.520f, 0.345f, 0.195f);
    private static readonly Vector3 Cream = new(0.975f, 0.940f, 0.845f);
    private static readonly Vector3 InkBrown = new(0.280f, 0.175f, 0.095f);
    private static readonly Vector3 InkSoft = new(0.475f, 0.360f, 0.240f);
    private static readonly Vector3 HighlightGold = new(0.960f, 0.760f, 0.180f);
    private static readonly Vector3 PlateDark = new(0.185f, 0.120f, 0.070f);
    private static readonly Vector3 DoneGreen = new(0.35f, 0.62f, 0.30f);

    private static float Slot => Math.Clamp(Glfw.WindowHeight * 0.070f, 46f, 64f);

    /// <summary>Per-frame input + draw. Call right after ShopHud.Render at the shared
    /// HUD call sites; self-gates on the session flag (edit mode draws nothing).</summary>
    public static void Render(HUD hud, float dt)
    {
        if (hud == null || !Player2DStats.SessionActive)
        {
            LogOpen = false;
            _qWasDown = false;
            return;
        }
        if (InventoryHud.SharedHud != null && _labelFont == 0) Prewarm();

        nint window = Glfw.GetWindow();
        bool dialogueOrShop = DialogueSystem.IsConversationActive || ShopHud.IsOpen;

        // [Q] toggles the log; Esc closes it. Suppressed while a conversation/shop
        // owns input (same rule as the inventory panel).
        bool qDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_Q);
        bool escDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);
        if (!dialogueOrShop)
        {
            if (qDown && !_qWasDown) LogOpen = !LogOpen;
            if (LogOpen && escDown && !_escWasDown) LogOpen = false;
        }
        _qWasDown = qDown;
        _escWasDown = escDown;

        var active = QuestSystem.ActiveQuests();
        var done = QuestSystem.DoneQuests();

        if (LogOpen)
            DrawLog(hud, active, done);
        else if (TrackerVisible && !dialogueOrShop && active.Count > 0)
            DrawTracker(hud, active);
    }

    // ── Tracker (top-left, compact) ────────────────

    private static void DrawTracker(HUD hud, List<QuestSystem.QuestDef> active)
    {
        int lf = LabelFont(hud);
        float pad = 10f, lineStep = MathF.Max(15f, Slot * 0.26f);
        float w = 0f;
        foreach (var q in active)
        {
            w = MathF.Max(w, hud.GetTextExtents(q.Name, lf).Width);
            foreach (var o in q.Objectives)
                w = MathF.Max(w, hud.GetTextExtents("- " + o, lf).Width);
        }
        w = MathF.Min(MathF.Max(w + pad * 2f, 190f), Glfw.WindowWidth * 0.42f);
        int lines = active.Sum(q => 1 + q.Objectives.Count);
        float h = pad * 2f + lines * lineStep + (active.Count - 1) * 4f;

        float x = 16f, y = 118f; // under the HP/MP bars
        FillSolid(hud, x, y, w, h, PlateDark, 3);
        float t = 2f;
        hud.DrawBox(x, y, w, t, HighlightGold * 0.8f);
        hud.DrawBox(x, y + h - t, w, t, HighlightGold * 0.8f);
        hud.DrawBox(x, y + t, t, h - t * 2f, HighlightGold * 0.8f);
        hud.DrawBox(x + w - t, y + t, t, h - t * 2f, HighlightGold * 0.8f);

        float ty = y + pad;
        for (int qi = 0; qi < active.Count; qi++)
        {
            var q = active[qi];
            hud.DrawText(q.Name, x + pad, ty, HighlightGold, null, 0f, lf);
            HUD.TextOut(x + pad, ty, q.Name, HighlightGold, fontSizePx: 14f);
            ty += lineStep;
            foreach (var o in q.Objectives)
            {
                hud.DrawText("- " + o, x + pad + 6f, ty, Cream, null, 0f, lf);
                HUD.TextOut(x + pad + 6f, ty, "- " + o, Cream, fontSizePx: 13f);
                ty += lineStep;
            }
            if (qi < active.Count - 1) ty += 4f;
        }
        // Log hint (part of the plate when there's room; below it otherwise).
        string hint = "[Q] Quest Log";
        hud.DrawText(hint, x, y + h + 4f, Cream * 0.75f, null, 0f, lf);
        HUD.TextOut(x, y + h + 4f, hint, Cream * 0.75f, fontSizePx: 13f);
    }

    // ── Quest Log panel ([Q]) ──────────────────────

    private static void DrawLog(HUD hud, List<QuestSystem.QuestDef> active, List<QuestSystem.QuestDef> done)
    {
        int tf = TitleFont(hud), lf = LabelFont(hud);
        float pad = Slot * 0.4f;
        float bannerH = Slot * 0.8f;
        float lineStep = MathF.Max(16f, Slot * 0.28f);

        // Measure content height (active section + done section + headers).
        float contentH = 0f;
        contentH += lineStep; // "AKTIF" header
        if (active.Count == 0) contentH += lineStep;
        else foreach (var q in active)
                contentH += lineStep * (1 + q.Objectives.Count) + (q.RewardText.Length > 0 ? lineStep : 0) + 4f;
        if (done.Count > 0)
        {
            contentH += lineStep * 1.4f; // separator + "SELESAI"
            foreach (var q in done) contentH += lineStep + 4f;
        }
        contentH += lineStep * 0.6f; // footer hint

        float panelW = MathF.Min(620f, Glfw.WindowWidth * 0.6f);
        float panelH = pad * 2f + bannerH + pad * 0.6f + contentH + pad;
        float px = (Glfw.WindowWidth - panelW) * 0.5f;
        float py = (Glfw.WindowHeight - panelH) * 0.5f;

        FillSolid(hud, px, py, panelW, panelH, Parchment, 3);
        WoodFrame(hud, px, py, panelW, panelH);

        // Banner: title + close chip.
        float bx = px + pad, by = py + pad, bw = panelW - pad * 2f;
        FillSolid(hud, bx, by, bw, bannerH, WoodDark, 3);
        string title = "Quest Log";
        var tExt = hud.GetTextExtents(title, tf);
        hud.DrawText(title, bx + pad * 0.7f, by + (bannerH - tExt.Height) * 0.5f, Cream, null, 0f, tf);
        HUD.TextOut(bx + pad * 0.7f, by + (bannerH - tExt.Height) * 0.5f, title, Cream, fontSizePx: 22f);
        float chip = bannerH * 0.62f;
        HintChip(hud, bx + bw - chip - pad * 0.6f, by + (bannerH - chip) * 0.5f, chip, "Q", lf);
        string gold = $"Gold {InventorySystem.Gold}";
        float goldW = hud.GetTextExtents(gold, lf).Width;
        hud.DrawText(gold, bx + bw - chip - pad * 1.2f - goldW, by + (bannerH - hud.GetTextExtents(gold, lf).Height) * 0.5f,
            HighlightGold, null, 0f, lf);

        float tx = bx + pad * 0.6f;
        float ty = by + bannerH + pad * 0.6f;

        // ── AKTIF ──
        hud.DrawText("== AKTIF ==", tx, ty, InkBrown, null, 0f, lf);
        ty += lineStep;
        if (active.Count == 0)
        {
            hud.DrawText("Belum ada quest aktif. Bicara dengan NPC bertanda \"!\".", tx, ty, InkSoft, null, 0f, lf);
            HUD.TextOut(tx, ty, "Belum ada quest aktif. Bicara dengan NPC bertanda \"!\".", InkSoft, fontSizePx: 14f);
            ty += lineStep;
        }
        foreach (var q in active)
        {
            hud.DrawText(q.Name, tx + 6f, ty, HighlightGold, null, 0f, lf);
            HUD.TextOut(tx + 6f, ty, q.Name, HighlightGold, fontSizePx: 15f);
            ty += lineStep;
            if (q.Description.Length > 0)
            {
                hud.DrawText(q.Description, tx + 18f, ty, InkSoft, null, 0f, lf);
                HUD.TextOut(tx + 18f, ty, q.Description, InkSoft, fontSizePx: 13f);
                ty += lineStep;
            }
            foreach (var o in q.Objectives)
            {
                hud.DrawText("- " + o, tx + 18f, ty, InkBrown, null, 0f, lf);
                HUD.TextOut(tx + 18f, ty, "- " + o, InkBrown, fontSizePx: 13f);
                ty += lineStep;
            }
            if (q.RewardText.Length > 0)
            {
                hud.DrawText("Hadiah: " + q.RewardText, tx + 18f, ty, DoneGreen, null, 0f, lf);
                HUD.TextOut(tx + 18f, ty, "Hadiah: " + q.RewardText, DoneGreen, fontSizePx: 13f);
                ty += lineStep;
            }
            ty += 4f;
        }

        // ── SELESAI ──
        if (done.Count > 0)
        {
            ty += lineStep * 0.4f;
            hud.DrawBox(tx, ty, bw - pad * 1.2f, 1.5f, WoodMid);
            ty += lineStep * 0.6f;
            hud.DrawText("== SELESAI ==", tx, ty, InkSoft, null, 0f, lf);
            HUD.TextOut(tx, ty, "== SELESAI ==", InkSoft, fontSizePx: 14f);
            ty += lineStep;
            foreach (var q in done)
            {
                // ✓ marker = stb-only decoration (mirror font has no checkmark glyph
                // → it would render '?' next to a doubled name).
                hud.DrawText("✓", tx + 6f, ty, DoneGreen, null, 0f, lf, mirror: false);
                hud.DrawText(q.Name, tx + 26f, ty, InkSoft, null, 0f, lf);
                HUD.TextOut(tx + 26f, ty, q.Name, InkSoft, fontSizePx: 13f);
                ty += lineStep + 4f;
            }
        }

        hud.DrawText("[Q] / [Esc] tutup", tx, ty + lineStep * 0.2f, InkBrown * 0.8f, null, 0f, lf);
        HUD.TextOut(tx, ty + lineStep * 0.2f, "[Q] / [Esc] tutup", InkBrown * 0.8f, fontSizePx: 13f);
    }

    // ── Style helpers (mirror InventoryHud/ShopHud) ──

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
}
