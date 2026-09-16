using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using StbImageSharp;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Runtime for the data-driven dialogue system. Owns the ACTIVE conversation and all
/// ambient bubbles; draws both through the HUD primitive queue (DrawBox / DrawText /
/// DrawImage) so it renders identically in-game and in the IDE preview (F8) — both
/// paths flush the same HUD.
///
/// Modes:
///  • Conversation — RPG-style bottom window: portrait, speaker name, typewriter text,
///    branching choices. Triggered by trigger action "Start Dialogue", NPC interaction
///    key, or code. Freezes player movement while open.
///  • Bubbles — short messages floating above a target object (player or NPC). Follow
///    the target every frame so movement/jumps carry the bubble along. One bubble per
///    target; ShowBubble replaces, HideBubble removes.
///
/// Everything is data-driven from <see cref="DialogueLibrary"/> (Assets/Dialogue/
/// dialogues.json): assets, speakers, themes, localization. Dialogue results reuse the
/// trigger-action catalog so Start Quest / Give Item / Change Map … need no new types.
/// </summary>
public static unsafe class DialogueSystem
{
    // ════════════════════════════════════════════
    //  RUNTIME STATE (flags/variables/progress)
    // ════════════════════════════════════════════

    /// <summary>Dialogue asset ids that ran to completion this session.</summary>
    public static HashSet<string> CompletedDialogues { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Named flags (quest_x_active, item_potion, door_1_unlocked…).</summary>
    public static HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Named numeric variables (gold, reputation…).</summary>
    public static Dictionary<string, float> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set/replace a flag (used by actions + choice conditions).</summary>
    public static void SetFlag(string flag) { if (!string.IsNullOrWhiteSpace(flag)) Flags.Add(flag); }
    public static void ClearFlag(string flag) { Flags.Remove(flag); }
    public static bool HasFlag(string flag) => Flags.Contains(flag);

    public static void SetVariable(string name, float value) { if (!string.IsNullOrWhiteSpace(name)) Variables[name] = value; }
    public static void AddVariable(string name, float delta)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Variables.TryGetValue(name, out float v);
        Variables[name] = v + delta;
    }
    public static float GetVariable(string name) => Variables.TryGetValue(name, out float v) ? v : 0f;

    // ════════════════════════════════════════════
    //  CONVERSATION
    // ════════════════════════════════════════════

    /// <summary>Active conversation session (null = none). While set, player movement
    /// input is frozen (Player2DSystem checks IsConversationActive).</summary>
    public static ConversationState? Active { get; private set; }
    public static bool IsConversationActive => Active != null;

    /// <summary>EditorObject the player can currently talk to (in range) — the runtime
    /// shows an "E" prompt above it and starts its dialogue on the interact key.</summary>
    public static EditorObject? InteractableNpc { get; set; }
    /// <summary>NPC interaction range in world units.</summary>
    public static float InteractionRange { get; set; } = 2.2f;

    /// <summary>Fired when a conversation fully ends (asset id) — quest hooks etc.</summary>
    public static Action<string>? OnDialogueEnded { get; set; }

    /// <summary>Set by the scene each frame: true while preview/in-game is active so
    /// the "[E] Talk" prompt only renders during play (never over the edit-mode view).
    /// Editor-panel previews still draw the conversation window itself.</summary>
    public static bool ShowPrompts { get; set; }

    public sealed class ConversationState
    {
        public DialogueAsset Asset = null!;
        public DialogueNode Node = null!;
        /// <summary>Typewriter progress (revealed character count).</summary>
        public float RevealChars;
        /// <summary>True once the full text is visible (next advance moves pages).</summary>
        public bool FullyRevealed;
        public float AutoAdvanceTimer;
        /// <summary>Selected choice index (keyboard navigation).</summary>
        public int ChoiceIndex;
        /// <summary>Object that started the conversation (NPC — bubbles/prompts anchor).</summary>
        public EditorObject? Source;
        /// <summary>Close fade (1 → 0) so the window doesn't pop.</summary>
        public float CloseFade = -1f; // <0 = open
    }

    /// <summary>Start a conversation by asset id/name. Returns false when not found
    /// or already talking (a running conversation is never interrupted silently).</summary>
    public static bool StartConversation(string assetIdOrName, EditorObject? source = null)
    {
        var asset = DialogueLibrary.GetAsset(assetIdOrName);
        if (asset == null)
        {
            Console.WriteLine($"[Dialogue] StartConversation FAILED: asset '{assetIdOrName}' not found");
            return false;
        }
        return StartConversation(asset, source);
    }

    public static bool StartConversation(DialogueAsset asset, EditorObject? source = null)
    {
        if (Active != null) return false;
        var start = asset.GetNode(asset.StartNodeId) ?? asset.Nodes.FirstOrDefault();
        if (start == null)
        {
            Console.WriteLine($"[Dialogue] '{asset.Id}' has no nodes — nothing to show");
            return false;
        }
        Active = new ConversationState { Asset = asset, Node = start, Source = source };
        EnterNode(start);
        Console.WriteLine($"[Dialogue] Started '{asset.Id}' (node '{start.Id}')");
        return true;
    }

    /// <summary>Run node-entry side effects (actions) and reset per-node state.</summary>
    private static void EnterNode(DialogueNode node)
    {
        var st = Active!;
        st.Node = node;
        st.RevealChars = 0f;
        st.FullyRevealed = false;
        st.AutoAdvanceTimer = node.AutoAdvance ? MathF.Max(0.1f, node.AutoAdvanceDelay) : 0f;
        st.ChoiceIndex = 0;
        foreach (var a in node.OnStartActions)
            TriggerEventSystem.ExecuteAction(a, $"dialogue:{st.Asset.Id}/{node.Id}");
    }

    /// <summary>Advance: reveal-all on first press, next page/end on second.
    /// No-op when the node has choices (choices must be picked explicitly).</summary>
    public static void Advance()
    {
        var st = Active;
        if (st == null || st.CloseFade >= 0f) return;
        if (!st.FullyRevealed) { st.FullyRevealed = true; return; }
        if (VisibleChoices(st).Count > 0) return; // wait for a choice pick
        GotoNode(st.Node.NextNodeId);
    }

    /// <summary>Pick the currently highlighted choice (or a specific index).</summary>
    public static void SelectChoice(int index)
    {
        var st = Active;
        if (st == null || st.CloseFade >= 0f) return;
        var choices = VisibleChoices(st);
        if (index < 0 || index >= choices.Count) return;
        PickChoice(st, choices[index]);
    }

    public static void MoveChoiceSelection(int delta)
    {
        var st = Active;
        if (st == null) return;
        int n = VisibleChoices(st).Count;
        if (n == 0) return;
        st.ChoiceIndex = ((st.ChoiceIndex + delta) % n + n) % n;
    }

    private static void PickChoice(ConversationState st, DialogueChoice choice)
    {
        foreach (var a in choice.Actions)
            TriggerEventSystem.ExecuteAction(a, $"dialogue:{st.Asset.Id}/{st.Node.Id}");
        GotoNode(choice.NextNodeId);
    }

    /// <summary>Jump to a node id (empty = end). Fires the outgoing node's OnEnd actions.</summary>
    public static void GotoNode(string nodeId)
    {
        var st = Active;
        if (st == null) return;
        foreach (var a in st.Node.OnEndActions)
            TriggerEventSystem.ExecuteAction(a, $"dialogue:{st.Asset.Id}/{st.Node.Id}");

        if (string.IsNullOrWhiteSpace(nodeId) || st.Asset.GetNode(nodeId) == null)
        {
            EndConversation();
            return;
        }
        EnterNode(st.Asset.GetNode(nodeId)!);
    }

    /// <summary>End the active conversation (skip pressed / branch reached the end).</summary>
    public static void EndConversation()
    {
        var st = Active;
        if (st == null) return;
        if (st.CloseFade < 0f)
        {
            st.CloseFade = 0.25f; // brief fade-out, cleared by Tick
            return;
        }
        CompletedDialogues.Add(st.Asset.Id);
        OnDialogueEnded?.Invoke(st.Asset.Id);
        Console.WriteLine($"[Dialogue] Ended '{st.Asset.Id}'");
        Active = null;
    }

    /// <summary>Choices of the current node that pass ALL their conditions.</summary>
    public static List<DialogueChoice> VisibleChoices(ConversationState st)
    {
        var list = new List<DialogueChoice>();
        foreach (var c in st.Node.Choices)
            if (c.Conditions.Count == 0 || c.Conditions.All(ConditionPasses))
                list.Add(c);
        return list;
    }

    /// <summary>Condition mini-language: "level:5", "gold:100", "flag:name",
    /// "item:potion", "quest:id", "var:name:10". Unknown conditions FAIL closed.</summary>
    public static bool ConditionPasses(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        var parts = condition.Trim().Split(':');
        string kind = parts[0].Trim().ToLowerInvariant();
        switch (kind)
        {
            case "level":
                return float.TryParse(parts.ElementAtOrDefault(1), out float lvl) && Player2DStats.Level >= lvl;
            case "gold":
                return float.TryParse(parts.ElementAtOrDefault(1), out float gold) && GetVariable("gold") >= gold;
            case "flag":
                return HasFlag(parts.ElementAtOrDefault(1) ?? "");
            case "item":
                return Variables.GetValueOrDefault($"item_{parts.ElementAtOrDefault(1)}", 0f) > 0f;
            case "quest":
                string q = parts.ElementAtOrDefault(1) ?? "";
                return HasFlag($"quest_{q}_active") || HasFlag($"quest_{q}_done");
            case "questdone":
                return HasFlag($"quest_{parts.ElementAtOrDefault(1)}_done");
            case "var":
            {
                float threshold = float.TryParse(parts.ElementAtOrDefault(2), out float v) ? v : 0f;
                return Variables.GetValueOrDefault(parts.ElementAtOrDefault(1) ?? "", 0f) >= threshold;
            }
            default:
                Console.WriteLine($"[Dialogue] Unknown condition '{condition}' — treating as failed");
                return false;
        }
    }

    // ════════════════════════════════════════════
    //  BUBBLES
    // ════════════════════════════════════════════

    public sealed class ActiveBubble
    {
        public string Text = "";
        public string BubbleType = DialogueBubbleTypes.Speech;
        /// <summary>Follow target (re-projected every frame). Null = fixed world pos.</summary>
        public EditorObject? Target;
        public Vector2 WorldPos;         // fallback / fixed anchor (feet-space)
        public float HeadHeight;         // world units above the anchor (NPCs: sprite height)
        public float OffsetX, OffsetY;   // extra pixel offset from the anchor point
        public float Life;               // <=0 = persistent until HideBubble
        public float Age;
        /// <summary>Fade in/out (0..1) — keeps bubbles from popping.</summary>
        public float Fade = 0f;
    }

    /// <summary>One bubble per target key (object name or "world:N").</summary>
    private static readonly Dictionary<string, ActiveBubble> _bubbles = new();

    /// <summary>Show (or replace) a bubble above an object. duration <=0 = stays until
    /// HideBubble/HideAllBubbles. hideOnExit handled by the caller re-calling Hide.</summary>
    public static void ShowBubble(string text, EditorObject? target, string bubbleType = DialogueBubbleTypes.Speech,
        float duration = 0f, float offsetX = 0f, float offsetY = 0f, float headHeight = -1f)
    {
        if (string.IsNullOrEmpty(text) || target == null) return;
        _bubbles[target.Name] = new ActiveBubble
        {
            Text = text,
            BubbleType = string.IsNullOrEmpty(bubbleType) ? DialogueBubbleTypes.Speech : bubbleType,
            Target = target,
            HeadHeight = headHeight >= 0f ? headHeight : BubbleHeadHeight(target),
            OffsetX = offsetX,
            OffsetY = offsetY,
            Life = duration,
            Fade = 0f,
        };
    }

    /// <summary>Show a bubble at a fixed world position (no follow target).</summary>
    public static void ShowWorldBubble(string text, Vector2 worldPos, string bubbleType = DialogueBubbleTypes.Speech,
        float duration = 3f, float offsetX = 0f, float offsetY = 0f)
    {
        string key = $"world:{worldPos.X:F1},{worldPos.Y:F1}";
        _bubbles[key] = new ActiveBubble
        {
            Text = text,
            BubbleType = bubbleType,
            Target = null,
            WorldPos = worldPos,
            OffsetX = offsetX,
            OffsetY = offsetY,
            Life = duration,
        };
    }

    public static void HideBubble(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName)) return;
        _bubbles.Remove(targetName);
    }

    public static void HideAllBubbles() => _bubbles.Clear();

    /// <summary>Bubbles hide automatically when the player walks away by this many
    /// world units (Hide On Distance) — 0 = unlimited.</summary>
    public static float BubbleHideDistance { get; set; } = 0f;

    private static float BubbleHeadHeight(EditorObject obj)
    {
        // Head anchor: Player2D uses capsule top; Sprite2D/others use render height.
        if (obj.PrimitiveType == EditorPrimitiveType.Player2D)
            return obj.Player2DCapsuleOffsetY + obj.Player2DCapsuleHeight + 0.15f;
        return obj.Player2DHeight + 0.25f;
    }

    // ════════════════════════════════════════════
    //  INPUT + NPC INTERACTION (call from update)
    // ════════════════════════════════════════════

    private static bool _interactWasDown;

    /// <summary>Per-frame gameplay-side update: NPC proximity → interaction prompt,
    /// interact key → start dialogue, conversation keyboard. Call from the scene
    /// update (runs in preview + in-game; harmless in edit mode).</summary>
    public static void UpdateInteraction(EditorObjectManager? manager, float dt)
    {
        // ── NPC proximity (only when not already talking) ──
        InteractableNpc = null;
        if (manager != null && Active == null)
        {
            var player = manager.Objects.FirstOrDefault(o =>
                o is { IsVisible: true, PrimitiveType: EditorPrimitiveType.Player2D });
            if (player != null)
            {
                float best = InteractionRange;
                foreach (var obj in manager.Objects)
                {
                    if (obj == player || !obj.IsVisible || string.IsNullOrEmpty(obj.NpcDialogueId)) continue;
                    float dx = obj.Position.X - player.Position.X;
                    float dy = obj.Position.Y - player.Position.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist < best) { best = dist; InteractableNpc = obj; }
                }
            }
        }

        // ── Interact key starts the NPC's dialogue ──
        bool interactDown = ImGui.IsKeyDown(ImGuiKey.E);
        bool pressed = interactDown && !_interactWasDown;
        _interactWasDown = interactDown;
        if (pressed && Active == null && InteractableNpc != null)
            StartConversation(InteractableNpc.NpcDialogueId, InteractableNpc);

        // ── Conversation keyboard (outside the pause/save gates — dialogue owns input) ──
        var conv = Active;
        if (conv != null && conv.CloseFade < 0f)
        {
            var st = conv;
            int choiceCount = VisibleChoices(st).Count;

            if (ImGui.IsKeyPressed(ImGuiKey.Space) || ImGui.IsKeyPressed(ImGuiKey.Enter))
                Advance();
            if (choiceCount > 0)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) MoveChoiceSelection(+1);
                if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) MoveChoiceSelection(-1);
                if (ImGui.IsKeyPressed(ImGuiKey.E))
                {
                    if (!st.FullyRevealed) st.FullyRevealed = true;
                    else SelectChoice(st.ChoiceIndex);
                }
                for (ImGuiKey k = ImGuiKey._1; k <= ImGuiKey._9; k++)
                {
                    int idx = k - ImGuiKey._1;
                    if (idx < choiceCount && ImGui.IsKeyPressed(k))
                    {
                        if (!st.FullyRevealed) st.FullyRevealed = true;
                        else { st.ChoiceIndex = idx; SelectChoice(idx); }
                        break;
                    }
                }
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                EndConversation();
        }
    }

    // ════════════════════════════════════════════
    //  DRAW + TICK (call from the HUD render path)
    // ════════════════════════════════════════════

    private static readonly Dictionary<string, uint> _textureCache = [];
    private static readonly Dictionary<string, int> _fontSlotCache = [];
    private static EditorObjectManager? _lastManager;
    private static Camera? _lastCamera;

    /// <summary>Update timers + queue all dialogue drawing. Call once per frame from
    /// the scene's HUD section (before hud.Flush()). manager/camera refresh the
    /// bubble follow targets and world→screen projection.</summary>
    public static void Tick(float dt, HUD hud, Camera? camera, EditorObjectManager? manager)
    {
        _lastManager = manager;
        _lastCamera = camera;
        _time += dt;
        if (hud == null) return;

        int w = Glfw.WindowWidth;
        int h = Glfw.WindowHeight;

        // ── Bubbles ──
        var expired = new List<string>();
        foreach (var (key, b) in _bubbles)
        {
            b.Age += dt;
            if (b.Life > 0f && b.Age >= b.Life) { expired.Add(key); continue; }

            Vector3 anchorWorld;
            if (b.Target != null)
            {
                if (!b.Target.IsVisible) { expired.Add(key); continue; }
                anchorWorld = new Vector3(b.Target.Position.X, b.Target.Position.Y + b.HeadHeight, b.Target.Position.Z);
                // Hide On Distance: player walked away → bubble goes with a fade.
                if (BubbleHideDistance > 0f && manager != null)
                {
                    var player = manager.Objects.FirstOrDefault(o =>
                        o is { IsVisible: true, PrimitiveType: EditorPrimitiveType.Player2D });
                    if (player != null)
                    {
                        float dx = b.Target.Position.X - player.Position.X;
                        float dy = b.Target.Position.Y - player.Position.Y;
                        if (dx * dx + dy * dy > BubbleHideDistance * BubbleHideDistance) { expired.Add(key); continue; }
                    }
                }
            }
            else
                anchorWorld = new Vector3(b.WorldPos.X, b.WorldPos.Y, 0f);

            var theme = Active != null ? DialogueLibrary.GetTheme(Active.Asset.ThemeName) : DialogueLibrary.GetTheme("Default");
            DrawBubble(hud, b, anchorWorld, theme, w, h);
        }
        foreach (var key in expired) _bubbles.Remove(key);

        // ── Interaction prompt ("E — Talk") above the nearby NPC ──
        if (ShowPrompts && Active == null && InteractableNpc != null && camera != null)
            DrawInteractPrompt(hud, InteractableNpc, w, h);

        // ── Conversation window ──
        if (Active != null)
        {
            var st = Active;
            if (st.CloseFade >= 0f)
            {
                st.CloseFade -= dt;
                if (st.CloseFade <= 0f) { EndConversation(); return; }
            }
            else
            {
                // Typewriter reveal + auto-advance.
                var theme = DialogueLibrary.GetTheme(st.Asset.ThemeName);
                string fullText = DialogueLibrary.Localize(st.Node.Text);
                if (!st.FullyRevealed)
                {
                    float cps = MathF.Max(0f, theme.TypewriterCharsPerSecond);
                    if (cps <= 0f) st.FullyRevealed = true;
                    else
                    {
                        st.RevealChars += cps * dt;
                        if (st.RevealChars >= fullText.Length) { st.RevealChars = fullText.Length; st.FullyRevealed = true; }
                    }
                }
                else if (st.Node.AutoAdvance && VisibleChoices(st).Count == 0)
                {
                    st.AutoAdvanceTimer -= dt;
                    if (st.AutoAdvanceTimer <= 0f) GotoNode(st.Node.NextNodeId);
                }
            }

            DrawConversation(hud, st, w, h);
        }
    }

    /// <summary>Screen position (HUD coords, Y=0 top) of a world point.</summary>
    private static Vector2 Project(Vector3 worldPos, int w, int h)
    {
        if (_lastCamera == null) return new Vector2(w * 0.5f, h * 0.5f);
        var p = TransformGizmo.ProjectToScreen(_lastCamera, worldPos, w, h);
        return new Vector2(p.X, h - p.Y); // GL Y=0 bottom → HUD Y=0 top
    }

    // ── Bubble drawing ─────────────────────────────

    private static void DrawBubble(HUD hud, ActiveBubble b, Vector3 anchorWorld, DialogueThemeData theme, int w, int h)
    {
        b.Fade = MathF.Min(1f, b.Fade + 0.12f);
        float fade = b.Fade * (b.Life > 0f ? Math.Clamp((b.Life - b.Age) / 0.3f, 0f, 1f) : 1f);

        var sp = Project(anchorWorld, w, h);
        float ax = sp.X + b.OffsetX;
        float ay = sp.Y + b.OffsetY;

        string text = DialogueLibrary.Localize(b.Text);
        int fontSlot = GetFontSlot(hud, theme, theme.BubbleFontSize);
        float maxW = MathF.Min(300f, w * 0.32f);

        var lines = hud.WordWrapText(text, maxW, fontSlot);
        float lineH = hud.MeasureTextHeight(text) + 2f;
        float textW = 0f;
        foreach (var line in lines) textW = MathF.Max(textW, hud.GetTextExtents(line).Width);

        float padX = 9f, padY = 6f, arrowH = 7f;
        float boxW = textW + padX * 2f;
        float boxH = lines.Count * lineH + padY * 2f;
        // Keep on screen.
        float bx = Math.Clamp(ax - boxW * 0.5f, 4f, w - boxW - 4f);
        float by = ay - boxH - arrowH - 4f;

        var (bg, border, txt) = BubbleColors(b.BubbleType, theme);

        hud.DrawBox(bx - 1f, by - 1f, boxW + 2f, boxH + 2f, border * fade * 0.9f); // border
        hud.DrawBox(bx, by, boxW, boxH, bg * fade);
        // Arrow (two stacked shrinking bars — cheap triangle from rects).
        hud.DrawBox(ax - 4f, by + boxH, 8f, 3f, bg * fade);
        hud.DrawBox(ax - 2f, by + boxH + 3f, 4f, 3f, bg * fade);

        float ty = by + padY;
        foreach (var line in lines)
        {
            float lx = bx + padX;
            hud.DrawText(line, lx, ty, txt * fade, new Vector3(0f, 0f, 0f), 1f);
            ty += lineH;
        }
    }

    /// <summary>Bubble type → (background, border, text) from the theme.</summary>
    private static (Vector3 bg, Vector3 border, Vector3 text) BubbleColors(string type, DialogueThemeData theme)
    {
        return type switch
        {
            DialogueBubbleTypes.Quest => (theme.BubbleColor, new Vector3(1f, 0.8f, 0.2f), theme.BubbleTextColor),
            DialogueBubbleTypes.Warning => (new Vector3(0.18f, 0.05f, 0.04f), new Vector3(1f, 0.35f, 0.25f), theme.BubbleTextColor),
            DialogueBubbleTypes.Thought => (theme.BubbleColor, new Vector3(0.55f, 0.6f, 0.75f), theme.BubbleTextColor * 0.9f),
            _ => (theme.BubbleColor, theme.BubbleBorderColor, theme.BubbleTextColor),
        };
    }

    private static void DrawInteractPrompt(HUD hud, EditorObject npc, int w, int h)
    {
        var theme = DialogueLibrary.GetTheme("Default");
        var sp = Project(new Vector3(npc.Position.X,
            npc.Position.Y + BubbleHeadHeight(npc) + 0.5f, npc.Position.Z), w, h);
        string label = "[E] Talk";
        int slot = GetFontSlot(hud, theme, theme.BubbleFontSize);
        var ext = hud.GetTextExtents(label);
        hud.DrawText(label, sp.X - ext.Width * 0.5f, sp.Y, new Vector3(1f, 0.95f, 0.6f),
            new Vector3(0f, 0f, 0f), 1.5f);
    }

    // ── Conversation window ────────────────────────

    private static void DrawConversation(HUD hud, ConversationState st, int w, int h)
    {
        var theme = DialogueLibrary.GetTheme(st.Asset.ThemeName);
        float fade = st.CloseFade >= 0f ? MathF.Max(0f, st.CloseFade / 0.25f) : 1f;

        string fullText = DialogueLibrary.Localize(st.Node.Text);
        string shown = st.FullyRevealed ? fullText : fullText[..(int)st.RevealChars];

        var speaker = DialogueLibrary.GetSpeaker(st.Node.SpeakerId);
        string speakerName = speaker?.Name ?? st.Node.SpeakerId;
        bool hasPortrait = !string.IsNullOrEmpty(st.Node.PortraitPath) ||
                           !string.IsNullOrEmpty(speaker?.PortraitPath);

        // ── Layout: bottom-center window ──
        float winW = MathF.Min(w * 0.72f, 860f);
        float winX = (w - winW) * 0.5f;
        int fontSlot = GetFontSlot(hud, theme, theme.WindowFontSize);
        float lineH = hud.MeasureTextHeight(shown) + 4f;

        var choices = st.FullyRevealed ? VisibleChoices(st) : new List<DialogueChoice>();
        float textMaxW = winW - 40f - (hasPortrait ? 130f : 0f);
        var lines = hud.WordWrapText(shown, textMaxW, fontSlot);

        float choicesH = 0f;
        if (choices.Count > 0)
        {
            int cSlot = GetFontSlot(hud, theme, theme.WindowFontSize);
            foreach (var c in choices)
                choicesH += hud.MeasureTextHeight(DialogueLibrary.Localize(c.Text)) + 10f;
            choicesH += 8f;
        }

        float winH = 22f /*name*/ + lines.Count * lineH + MathF.Max(choicesH, 26f) + 24f;
        float winY = h - winH - 18f;

        // ── Window background + border ──
        hud.DrawBox(winX + 2f, winY + 2f, winW, winH, theme.BorderColor * fade); // border
        hud.DrawBox(winX, winY, winW, winH, theme.WindowColor * fade);
        if (!string.IsNullOrEmpty(theme.BgImagePath))
        {
            uint tex = GetTexture(theme.BgImagePath);
            if (tex != 0) hud.DrawImage(winX, winY, winW, winH, tex);
        }

        float contentX = winX + 20f;
        float contentY = winY + 14f;

        // ── Portrait (left) ──
        if (hasPortrait)
        {
            float pSize = winH - 40f;
            float pX = winX + 16f;
            float pY = winY + 20f;
            hud.DrawBox(pX + 2f, pY + 2f, pSize, pSize, theme.BorderColor * fade);
            hud.DrawBox(pX, pY, pSize, pSize, theme.WindowColor * fade * 1.6f);

            string portraitPath = !string.IsNullOrEmpty(st.Node.PortraitPath) ? st.Node.PortraitPath : speaker!.PortraitPath;
            uint tex = 0;
            // Emotion variant first ("<path>_<emotion>.png"), then the base portrait.
            if (!string.Equals(st.Node.Emotion, DialogueEmotions.Neutral, StringComparison.OrdinalIgnoreCase))
                tex = GetTexture(EmotionVariant(portraitPath, st.Node.Emotion));
            if (tex == 0) tex = GetTexture(portraitPath);
            if (tex != 0) hud.DrawImage(pX + 3f, pY + 3f, pSize - 6f, pSize - 6f, tex);
        }

        // ── Speaker name ──
        float textX = contentX + (hasPortrait ? 124f : 0f);
        if (!string.IsNullOrEmpty(speakerName))
        {
            var nameCol = speaker != null
                ? new Vector3(speaker.ColorR, speaker.ColorG, speaker.ColorB) * fade
                : theme.NameColor * fade;
            int nameSlot = GetFontSlot(hud, theme, theme.NameFontSize);
            hud.DrawText(speakerName, textX, contentY, nameCol, new Vector3(0f, 0f, 0f), 1f, nameSlot);
            contentY += hud.MeasureTextHeight(speakerName) + 8f;
        }

        // ── Dialogue text (typewriter) ──
        float ty = contentY;
        foreach (var line in lines)
        {
            hud.DrawText(line, textX, ty, theme.TextColor * fade, new Vector3(0f, 0f, 0f), 1f, fontSlot);
            ty += lineH;
        }

        // ── Choices or continue hint ──
        if (choices.Count > 0)
        {
            float cy = winY + winH - choicesH - 10f;
            int cSlot = GetFontSlot(hud, theme, theme.WindowFontSize);
            for (int i = 0; i < choices.Count; i++)
            {
                string label = $"{i + 1}. {DialogueLibrary.Localize(choices[i].Text)}";
                bool selected = i == st.ChoiceIndex;
                var col = (selected ? theme.ChoiceHoverColor : theme.ChoiceColor) * fade;
                if (selected)
                    hud.DrawBox(textX - 6f, cy - 2f, winW - (textX - winX) - 26f,
                        hud.MeasureTextHeight(label) + 6f, theme.WindowColor * 1.8f * fade);
                hud.DrawText(label, textX, cy, col, new Vector3(0f, 0f, 0f), 1f, cSlot);
                cy += hud.MeasureTextHeight(label) + 10f;
            }
        }
        else if (st.FullyRevealed && !st.Node.AutoAdvance)
        {
            // Continue indicator (blinks).
            float blink = (MathF.Sin(_time * 5f) + 1f) * 0.5f;
            string hint = "▼ [Space]";
            var hintExt = hud.GetTextExtents(hint);
            hud.DrawText(hint, winX + winW - hintExt.Width - 18f, winY + winH - 22f,
                new Vector3(0.7f, 0.75f, 0.95f) * fade * (0.4f + 0.6f * blink));
        }
    }

    private static float _time;

    private static string EmotionVariant(string portraitPath, string emotion)
    {
        int dot = portraitPath.LastIndexOf('.');
        string stem = dot > 0 ? portraitPath[..dot] : portraitPath;
        string ext = dot > 0 ? portraitPath[dot..] : ".png";
        return $"{stem}_{emotion}{ext}";
    }

    // ── Resource caches ────────────────────────────

    private static int GetFontSlot(HUD hud, DialogueThemeData theme, float fontSize)
    {
        string key = $"{theme.FontPath}@{fontSize}";
        if (_fontSlotCache.TryGetValue(key, out int slot) && slot < hud.FontSlotCount) return slot;
        slot = hud.GetOrCreateFontSlot(theme.FontPath, fontSize);
        _fontSlotCache[key] = slot;
        return slot;
    }

    /// <summary>Load a portrait/image texture (cached; relative to project or exe).</summary>
    private static uint GetTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        if (_textureCache.TryGetValue(path, out uint tex)) return tex;

        tex = 0;
        try
        {
            string resolved = PathHelpers.Resolve(path);
            if (File.Exists(resolved))
            {
                GL.GenTextures(1, &tex);
                GL.BindTexture(Const.GL_TEXTURE_2D, tex);
                using var stream = File.OpenRead(resolved);
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                fixed (byte* ptr = image.Data)
                {
                    GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                        image.Width, image.Height, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
                }
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dialogue] Portrait load failed '{path}': {ex.Message}");
            tex = 0;
        }
        _textureCache[path] = tex;
        return tex;
    }

    // ════════════════════════════════════════════
    //  SAVE / RESTORE
    // ════════════════════════════════════════════

    /// <summary>Snapshot dialogue progress for a save slot.</summary>
    public static (List<string> completed, List<string> flags, List<string> vars) CaptureState()
    {
        var vars = Variables.Select(kv => $"{kv.Key}={kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}").ToList();
        return ([.. CompletedDialogues], [.. Flags], vars);
    }

    /// <summary>Restore dialogue progress from a save slot (replaces current state).</summary>
    public static void RestoreState(List<string>? completed, List<string>? flags, List<string>? vars)
    {
        CompletedDialogues.Clear();
        if (completed != null) foreach (var c in completed) CompletedDialogues.Add(c);
        Flags.Clear();
        if (flags != null) foreach (var f in flags) Flags.Add(f);
        Variables.Clear();
        if (vars != null)
            foreach (var v in vars)
            {
                int eq = v.IndexOf('=');
                if (eq > 0 && float.TryParse(v[(eq + 1)..], System.Globalization.CultureInfo.InvariantCulture, out float val))
                    Variables[v[..eq]] = val;
            }
        EndConversationNow();
        HideAllBubbles();
    }

    /// <summary>Immediately drop the conversation (no fade) — session reset / load.</summary>
    public static void EndConversationNow() => Active = null;

    /// <summary>Fresh play session: clear runtime state (progress survives saves only).</summary>
    public static void ResetSession()
    {
        EndConversationNow();
        HideAllBubbles();
        CompletedDialogues.Clear();
        Flags.Clear();
        Variables.Clear();
        InteractableNpc = null;
    }
}
