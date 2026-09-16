using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Dialogue Editor — designer-facing editor for the data-driven dialogue system.
/// Creates/edits/duplicates/deletes dialogue assets, authors nodes (text, speaker,
/// portrait, emotion, choices, auto-advance, start/end actions), manages reusable
/// speakers and themes, sets the active localization language, and previews the
/// selected asset through the live DialogueSystem (drawn in the viewport HUD).
/// No coding required — everything lives in Assets/Dialogue/dialogues.json.
/// </summary>
public class DialogueEditorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible;

    // Selection state
    private int _selectedAsset = -1;
    private int _selectedNode = -1;
    private int _selectedSpeaker = -1;
    private int _selectedTheme = -1;
    private bool _previewMode = true;

    // Localization scratch
    private int _editLanguage;
    private string _locKey = "";
    private string _locValue = "";

    public DialogueEditorPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Dialogue Editor", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;
        ImGui.SetNextWindowSize(new Vector2(860, 620), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Dialogue Editor", ref _visible))
        { ImGui.End(); return; }
        IDE.PanelFocus.Notify("Dialogue Editor");

        DialogueLibrary.EnsureLoaded();

        // ── Toolbar: add / duplicate / delete / save / preview ──
        if (ImGui.Button("+ Asset")) { DialogueLibrary.AddAsset(); _selectedAsset = DialogueLibrary.Assets.Count - 1; _selectedNode = 0; }
        ImGui.SameLine();
        if (ImGui.Button("Duplicate") && _selectedAsset >= 0 && _selectedAsset < DialogueLibrary.Assets.Count)
            DialogueLibrary.DuplicateAsset(DialogueLibrary.Assets[_selectedAsset].Id);
        ImGui.SameLine();
        if (ImGui.Button("Delete") && _selectedAsset >= 0 && _selectedAsset < DialogueLibrary.Assets.Count)
        {
            DialogueLibrary.RemoveAsset(DialogueLibrary.Assets[_selectedAsset].Id);
            _selectedAsset = -1; _selectedNode = -1;
        }
        ImGui.SameLine();
        if (ImGui.Button("Save")) DialogueLibrary.Save();
        ImGui.SameLine();
        ImGui.Checkbox("Preview", ref _previewMode);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Play the selected dialogue in the viewport (needs a play session with a Player2D + map, or just renders the window).");
        ImGui.SameLine();
        if (_previewMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.45f, 0.2f, 1f));
            if (ImGui.Button("▶ Start"))
            {
                if (_selectedAsset >= 0 && _selectedAsset < DialogueLibrary.Assets.Count)
                {
                    DialogueSystem.EndConversationNow();
                    DialogueSystem.StartConversation(DialogueLibrary.Assets[_selectedAsset]);
                }
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Button("■ Stop")) DialogueSystem.EndConversationNow();
        }
        ImGui.Separator();

        // ── Left: asset list / Right: editor ──
        float listW = 210f;
        ImGui.BeginChild("asset_list", new Vector2(listW, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Dialogue Assets");
        ImGui.Separator();
        for (int i = 0; i < DialogueLibrary.Assets.Count; i++)
        {
            var a = DialogueLibrary.Assets[i];
            ImGui.PushID($"asset{i}");
            if (ImGui.Selectable($"{a.Name}##{a.Id}", i == _selectedAsset))
            { _selectedAsset = i; _selectedNode = 0; }
            ImGui.PopID();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginGroup();
        if (_selectedAsset >= 0 && _selectedAsset < DialogueLibrary.Assets.Count)
        {
            RenderAssetEditor(DialogueLibrary.Assets[_selectedAsset]);

            ImGui.Separator();
            RenderSpeakersSection();
            ImGui.Separator();
            RenderThemesSection();
            ImGui.Separator();
            RenderLocalizationSection();
        }
        else
        {
            ImGui.TextDisabled("Select or create a dialogue asset.");
        }
        ImGui.EndGroup();

        ImGui.End();
    }

    // ── Asset editor ───────────────────────────────

    private void RenderAssetEditor(DialogueAsset asset)
    {
        ImGui.PushID($"ed_{asset.Id}");
        string name = asset.Name;
        if (ImGui.InputText("Name", ref name, 64)) asset.Name = name;
        string id = asset.Id;
        if (ImGui.InputText("Id (used by triggers/NPCs)", ref id, 64) && !string.IsNullOrWhiteSpace(id))
            asset.Id = id.Trim();
        string start = asset.StartNodeId;
        if (ImGui.InputText("Start Node", ref start, 64)) asset.StartNodeId = start.Trim();

        // Theme picker
        var themes = DialogueLibrary.GetThemeNames();
        int themeIdx = themes.IndexOf(asset.ThemeName);
        if (themeIdx < 0) themeIdx = 0;
        if (ImGui.BeginCombo("Theme", themes.Count > 0 ? themes[themeIdx] : "Default"))
        {
            for (int i = 0; i < themes.Count; i++)
            {
                bool sel = i == themeIdx;
                if (ImGui.Selectable(themes[i], sel)) asset.ThemeName = themes[i];
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.Separator();
        ImGui.TextDisabled($"Nodes ({asset.Nodes.Count})");

        // Node list (horizontal select)
        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            ImGui.PushID($"node{i}");
            var n = asset.Nodes[i];
            string label = $"{i}: {n.Id}" + (string.IsNullOrEmpty(n.SpeakerId) ? "" : $" [{n.SpeakerId}]") + (n.Choices.Count > 0 ? " +" : "");
            if (ImGui.Selectable(label, i == _selectedNode)) _selectedNode = i;
            if (ImGui.BeginPopupContextItem("node_ctx"))
            {
                if (ImGui.MenuItem("Duplicate") && n != null)
                    asset.Nodes.Insert(i + 1, CloneWithNewId(n, asset));
                if (ImGui.MenuItem("Delete") && asset.Nodes.Count > 1)
                {
                    asset.Nodes.RemoveAt(i);
                    _selectedNode = Math.Min(_selectedNode, asset.Nodes.Count - 1);
                    ImGui.EndPopup();
                    ImGui.PopID();
                    continue;
                }
                ImGui.EndPopup();
            }
            ImGui.PopID();
        }
        if (ImGui.Button("+ Node"))
        {
            asset.Nodes.Add(new DialogueNode { Id = UniqueNodeId(asset, "node") });
            _selectedNode = asset.Nodes.Count - 1;
        }

        // Node editor
        if (_selectedNode >= 0 && _selectedNode < asset.Nodes.Count)
            RenderNodeEditor(asset, asset.Nodes[_selectedNode]);
        ImGui.PopID();
    }

    private static string UniqueNodeId(DialogueAsset asset, string prefix)
    {
        int n = asset.Nodes.Count + 1;
        string id = $"{prefix}{n}";
        while (asset.Nodes.Any(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            id = $"{prefix}{++n}";
        return id;
    }

    private static DialogueNode CloneWithNewId(DialogueNode src, DialogueAsset asset)
    {
        var copy = src.Clone();
        copy.Id = UniqueNodeId(asset, "node");
        return copy;
    }

    private void RenderNodeEditor(DialogueAsset asset, DialogueNode node)
    {
        ImGui.Separator();
        ImGui.Text("Node");
        ImGui.SetNextItemWidth(180f);
        string id = node.Id;
        if (ImGui.InputText("##nodeid", ref id, 64) && !string.IsNullOrWhiteSpace(id))
        {
            string oldId = node.Id;
            node.Id = id.Trim();
            if (asset.StartNodeId == oldId) asset.StartNodeId = node.Id;
        }
        ImGui.SameLine();
        if (ImGui.Button("Set Start")) asset.StartNodeId = node.Id;

        // Speaker picker
        var speakerIds = DialogueLibrary.GetSpeakerIds();
        int spIdx = speakerIds.IndexOf(node.SpeakerId);
        string spLabel = spIdx >= 0 ? speakerIds[spIdx] : (string.IsNullOrEmpty(node.SpeakerId) ? "(narrator)" : node.SpeakerId);
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("Speaker", spLabel))
        {
            if (ImGui.Selectable("(narrator)", string.IsNullOrEmpty(node.SpeakerId)))
                node.SpeakerId = "";
            for (int i = 0; i < speakerIds.Count; i++)
            {
                bool sel = i == spIdx;
                if (ImGui.Selectable(speakerIds[i], sel)) node.SpeakerId = speakerIds[i];
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Emotion picker
        int emoIdx = Array.IndexOf(DialogueEmotions.All, node.Emotion);
        if (emoIdx < 0) emoIdx = 0;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("Emotion", DialogueEmotions.All[emoIdx]))
        {
            for (int i = 0; i < DialogueEmotions.All.Length; i++)
            {
                bool sel = i == emoIdx;
                if (ImGui.Selectable(DialogueEmotions.All[i], sel)) node.Emotion = DialogueEmotions.All[i];
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Portrait path
        string portrait = node.PortraitPath;
        if (ImGui.InputText("Portrait (png, optional)", ref portrait, 256))
            node.PortraitPath = portrait.Trim();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Loaded from project/exe-relative paths. Emotion variants try <name>_Happy.png etc. first.");

        // Text (localized display)
        string text = node.Text;
        if (ImGui.InputTextMultiline("Text", ref text, 600, new Vector2(-1, 64)))
            node.Text = text;
        string preview = DialogueLibrary.Localize(node.Text);
        if (preview != node.Text)
            ImGui.TextDisabled($"[{DialogueLibrary.CurrentLanguage}] {preview}");

        // Next node
        string next = node.NextNodeId;
        if (ImGui.InputText("Next Node (empty = end)", ref next, 64))
            node.NextNodeId = next.Trim();
        ImGui.SameLine();
        if (ImGui.Button("→ ##setnext"))
        {
            // Quick-jump helper: cycle to the next existing node id.
            if (asset.Nodes.Count > 0)
            {
                int idx = asset.Nodes.FindIndex(n => string.Equals(n.Id, node.NextNodeId, StringComparison.OrdinalIgnoreCase));
                node.NextNodeId = asset.Nodes[(idx + 1) % asset.Nodes.Count].Id;
            }
        }

        // Auto advance
        bool auto = node.AutoAdvance;
        if (ImGui.Checkbox("Auto Advance", ref auto)) node.AutoAdvance = auto;
        if (auto)
        {
            float delay = node.AutoAdvanceDelay;
            if (ImGui.DragFloat("Auto Delay (s)", ref delay, 0.1f, 0.1f, 30f))
                node.AutoAdvanceDelay = MathF.Max(0.1f, delay);
        }

        // Choices
        ImGui.Separator();
        ImGui.TextDisabled($"Choices ({node.Choices.Count})");
        for (int i = 0; i < node.Choices.Count; i++)
        {
            var c = node.Choices[i];
            ImGui.PushID($"choice{i}");
            string cText = c.Text;
            ImGui.SetNextItemWidth(160f);
            if (ImGui.InputText("##ctext", ref cText, 128)) c.Text = cText;
            ImGui.SameLine();
            string cNext = c.NextNodeId;
            ImGui.SetNextItemWidth(110f);
            if (ImGui.InputText("##cnext", ref cNext, 64)) c.NextNodeId = cNext.Trim();
            ImGui.SameLine();
            string cond = string.Join(";", c.Conditions);
            ImGui.SetNextItemWidth(140f);
            if (ImGui.InputText("##ccond", ref cond, 256))
                c.Conditions = cond.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Conditions separated by ';' — e.g. level:5;flag:met_elder;item:potion;gold:100");
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
            if (ImGui.SmallButton("x"))
            { node.Choices.RemoveAt(i); ImGui.PopStyleColor(); ImGui.PopID(); continue; }
            ImGui.PopStyleColor();
            // Choice actions (compact)
            if (c.Actions.Count > 0)
                ImGui.TextDisabled($"  → {c.Actions.Count} action(s): {string.Join(", ", c.Actions.Select(a => a.Type))}");
            if (ImGui.Button("+ Action"))
                c.Actions.Add(new TilemapTriggerAction { Type = TriggerActionTypes.GiveItem, Param = "item_1", Param2 = "1" });
            ImGui.SameLine();
            ImGui.SetNextItemWidth(130f);
            int aIdx = 0;
            string[] allActions = TriggerActionTypes.All;
            if (ImGui.BeginCombo("##ctype", c.Actions.Count > 0 ? "edit last…" : "type"))
            {
                for (int i2 = 0; i2 < allActions.Length; i2++)
                {
                    if (ImGui.Selectable(allActions[i2]))
                    {
                        if (c.Actions.Count > 0) c.Actions[^1].Type = allActions[i2];
                        else c.Actions.Add(new TilemapTriggerAction { Type = allActions[i2] });
                    }
                }
                ImGui.EndCombo();
            }
            if (c.Actions.Count > 0)
            {
                ImGui.SameLine();
                string p = c.Actions[^1].Param;
                ImGui.SetNextItemWidth(100f);
                if (ImGui.InputText("##cparam", ref p, 128)) c.Actions[^1].Param = p;
                ImGui.SameLine();
                string p2 = c.Actions[^1].Param2;
                ImGui.SetNextItemWidth(70f);
                if (ImGui.InputText("##cparam2", ref p2, 64)) c.Actions[^1].Param2 = p2;
            }
            ImGui.PopID();
        }
        if (ImGui.Button("+ Choice"))
            node.Choices.Add(new DialogueChoice { Text = $"Choice {node.Choices.Count + 1}" });

        // Node start/end actions
        ImGui.Separator();
        RenderActionList("On Start Actions", node.OnStartActions);
        RenderActionList("On End Actions", node.OnEndActions);
    }

    private static void RenderActionList(string label, List<TilemapTriggerAction> actions)
    {
        ImGui.PushID(label.Replace(" ", ""));
        if (ImGui.TreeNode(label))
        {
            for (int i = 0; i < actions.Count; i++)
            {
                ImGui.PushID($"a{i}");
                var a = actions[i];
                int tIdx = Array.IndexOf(TriggerActionTypes.All, a.Type);
                if (tIdx < 0) tIdx = 0;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.BeginCombo("##t", TriggerActionTypes.All[tIdx]))
                {
                    for (int t = 0; t < TriggerActionTypes.All.Length; t++)
                        if (ImGui.Selectable(TriggerActionTypes.All[t], t == tIdx)) a.Type = TriggerActionTypes.All[t];
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                string p = a.Param;
                ImGui.SetNextItemWidth(110f);
                if (ImGui.InputText("##p", ref p, 128)) a.Param = p;
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
                if (ImGui.SmallButton("x")) { actions.RemoveAt(i); ImGui.PopStyleColor(); ImGui.PopID(); continue; }
                ImGui.PopStyleColor();
                ImGui.PopID();
            }
            if (ImGui.Button("+ Action"))
                actions.Add(new TilemapTriggerAction { Type = TriggerActionTypes.PlaySound, Param = "click.wav" });
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    // ── Speakers ───────────────────────────────────

    private void RenderSpeakersSection()
    {
        if (!ImGui.CollapsingHeader($"Speakers ({DialogueLibrary.Speakers.Count})")) return;
        for (int i = 0; i < DialogueLibrary.Speakers.Count; i++)
        {
            var s = DialogueLibrary.Speakers[i];
            ImGui.PushID($"sp{i}");
            bool open = ImGui.TreeNode($"{s.Name} ({s.Id})");
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
            if (ImGui.SmallButton("x")) { DialogueLibrary.RemoveSpeaker(s); ImGui.PopStyleColor(); ImGui.PopID(); if (open) ImGui.TreePop(); continue; }
            ImGui.PopStyleColor();
            if (open)
            {
                string id = s.Id;
                if (ImGui.InputText("Id", ref id, 64)) s.Id = id.Trim();
                string name = s.Name;
                if (ImGui.InputText("Name", ref name, 64)) s.Name = name;
                string portrait = s.PortraitPath;
                if (ImGui.InputText("Portrait", ref portrait, 256)) s.PortraitPath = portrait.Trim();
                Vector3 col = new(s.ColorR, s.ColorG, s.ColorB);
                if (ImGui.ColorEdit3("Name Color", ref col))
                { s.ColorR = col.X; s.ColorG = col.Y; s.ColorB = col.Z; }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        if (ImGui.Button("+ Speaker"))
            DialogueLibrary.AddSpeaker(new SpeakerData { Id = $"speaker_{DialogueLibrary.Speakers.Count + 1}", Name = $"Speaker {DialogueLibrary.Speakers.Count + 1}" });
    }

    // ── Themes ─────────────────────────────────────

    private void RenderThemesSection()
    {
        if (!ImGui.CollapsingHeader($"Themes ({DialogueLibrary.Themes.Count})")) return;
        for (int i = 0; i < DialogueLibrary.Themes.Count; i++)
        {
            var t = DialogueLibrary.Themes[i];
            ImGui.PushID($"th{i}");
            bool open = ImGui.TreeNode($"{t.Name}{(string.IsNullOrEmpty(t.Parent) ? "" : $" (← {t.Parent})")}");
            ImGui.SameLine();
            if (t.Name == "Default") ImGui.TextDisabled("(built-in)");
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
                if (ImGui.SmallButton("x")) { DialogueLibrary.RemoveTheme(t); ImGui.PopStyleColor(); ImGui.PopID(); if (open) ImGui.TreePop(); continue; }
                ImGui.PopStyleColor();
            }
            if (open)
            {
                string name = t.Name;
                if (ImGui.InputText("Name", ref name, 64) && !string.IsNullOrWhiteSpace(name)) t.Name = name.Trim();
                var parents = DialogueLibrary.GetThemeNames().Where(n => n != t.Name).Prepend("").ToList();
                int pIdx = parents.IndexOf(t.Parent);
                if (pIdx < 0) pIdx = 0;
                if (ImGui.BeginCombo("Parent", parents[pIdx]))
                {
                    for (int p = 0; p < parents.Count; p++)
                        if (ImGui.Selectable(string.IsNullOrEmpty(parents[p]) ? "(none)" : parents[p], p == pIdx))
                            t.Parent = parents[p];
                    ImGui.EndCombo();
                }
                string bg = t.BgImagePath;
                if (ImGui.InputText("Window BG Image", ref bg, 256)) t.BgImagePath = bg.Trim();

                Vector3 win = t.WindowColor;
                if (ImGui.ColorEdit3("Window", ref win))
                { t.WindowColorR = win.X; t.WindowColorG = win.Y; t.WindowColorB = win.Z; }
                Vector3 border = t.BorderColor;
                if (ImGui.ColorEdit3("Border", ref border))
                { t.BorderColorR = border.X; t.BorderColorG = border.Y; t.BorderColorB = border.Z; }
                Vector3 nameC = t.NameColor;
                if (ImGui.ColorEdit3("Name", ref nameC))
                { t.NameColorR = nameC.X; t.NameColorG = nameC.Y; t.NameColorB = nameC.Z; }
                Vector3 textC = t.TextColor;
                if (ImGui.ColorEdit3("Text", ref textC))
                { t.TextColorR = textC.X; t.TextColorG = textC.Y; t.TextColorB = textC.Z; }
                Vector3 choice = t.ChoiceColor;
                if (ImGui.ColorEdit3("Choice", ref choice))
                { t.ChoiceColorR = choice.X; t.ChoiceColorG = choice.Y; t.ChoiceColorB = choice.Z; }
                Vector3 choiceH = t.ChoiceHoverColor;
                if (ImGui.ColorEdit3("Choice Hover", ref choiceH))
                { t.ChoiceHoverColorR = choiceH.X; t.ChoiceHoverColorG = choiceH.Y; t.ChoiceHoverColorB = choiceH.Z; }
                Vector3 bub = t.BubbleColor;
                if (ImGui.ColorEdit3("Bubble BG", ref bub))
                { t.BubbleColorR = bub.X; t.BubbleColorG = bub.Y; t.BubbleColorB = bub.Z; }
                Vector3 bubB = t.BubbleBorderColor;
                if (ImGui.ColorEdit3("Bubble Border", ref bubB))
                { t.BubbleBorderColorR = bubB.X; t.BubbleBorderColorG = bubB.Y; t.BubbleBorderColorB = bubB.Z; }

                string font = t.FontPath;
                if (ImGui.InputText("Font", ref font, 256)) t.FontPath = font;
                float fs = t.WindowFontSize;
                if (ImGui.DragFloat("Window Font", ref fs, 0.5f, 8f, 48f)) t.WindowFontSize = fs;
                float ns = t.NameFontSize;
                if (ImGui.DragFloat("Name Font", ref ns, 0.5f, 8f, 48f)) t.NameFontSize = ns;
                float bs = t.BubbleFontSize;
                if (ImGui.DragFloat("Bubble Font", ref bs, 0.5f, 8f, 48f)) t.BubbleFontSize = bs;
                float tw = t.TypewriterCharsPerSecond;
                if (ImGui.DragFloat("Typewriter (chars/s, 0 = off)", ref tw, 1f, 0f, 200f)) t.TypewriterCharsPerSecond = tw;
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        if (ImGui.Button("+ Theme"))
            DialogueLibrary.AddTheme(new DialogueThemeData { Name = $"Theme{DialogueLibrary.Themes.Count + 1}" });
    }

    // ── Localization ───────────────────────────────

    private void RenderLocalizationSection()
    {
        if (!ImGui.CollapsingHeader($"Localization ({DialogueLibrary.CurrentLanguage})")) return;

        int langIdx = Array.IndexOf(DialogueLibrary.Languages, DialogueLibrary.CurrentLanguage);
        if (langIdx < 0) langIdx = 0;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo("Active Language", DialogueLibrary.Languages[langIdx]))
        {
            for (int i = 0; i < DialogueLibrary.Languages.Length; i++)
            {
                bool sel = i == langIdx;
                if (ImGui.Selectable(DialogueLibrary.Languages[i], sel))
                {
                    DialogueLibrary.CurrentLanguage = DialogueLibrary.Languages[i];
                    _editLanguage = i;
                }
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        _editLanguage = langIdx;

        ImGui.TextDisabled("Overrides for the selected language (original text → translation):");
        string lang = DialogueLibrary.Languages[_editLanguage];
        var table = DialogueLibrary.Localizations.GetValueOrDefault(lang) ?? [];

        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("Original##lockey", ref _locKey, 400);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("Translation##locval", ref _locValue, 400);
        ImGui.SameLine();
        if (ImGui.Button("Set") && !string.IsNullOrWhiteSpace(_locKey))
        {
            if (!DialogueLibrary.Localizations.TryGetValue(lang, out var tbl))
                DialogueLibrary.Localizations[lang] = tbl = new(StringComparer.OrdinalIgnoreCase);
            tbl[_locKey] = _locValue;
        }

        if (table.Count > 0)
        {
            ImGui.TextDisabled($"{table.Count} translation(s)");
            foreach (var kv in table.Take(20))
                ImGui.BulletText($"{kv.Key} → {kv.Value}");
            if (table.Count > 20) ImGui.TextDisabled("…");
        }
    }
}
