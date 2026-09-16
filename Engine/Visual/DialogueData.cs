using System.Numerics;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>Portrait emotion slots a dialogue node can select. When the node has a
/// portrait path and a non-Neutral emotion, the runtime first tries
/// "&lt;portrait&gt;_&lt;emotion&gt;.png" before falling back to the base portrait.</summary>
public static class DialogueEmotions
{
    public const string Neutral = "Neutral";
    public const string Happy = "Happy";
    public const string Sad = "Sad";
    public const string Angry = "Angry";
    public const string Shock = "Shock";
    public const string Laugh = "Laugh";
    public const string Fear = "Fear";

    public static readonly string[] All = [Neutral, Happy, Sad, Angry, Shock, Laugh, Fear];
}

/// <summary>One selectable branch in a dialogue node. Conditions gate visibility,
/// actions run when the choice is picked, NextNodeId jumps to another node
/// (empty = end the dialogue).</summary>
public class DialogueChoice
{
    /// <summary>Choice label (localized through the DialogueLibrary table).</summary>
    public string Text { get; set; } = "Choice";
    /// <summary>Node to jump to when picked. Empty = end the dialogue.</summary>
    public string NextNodeId { get; set; } = "";
    /// <summary>Gate conditions ("level:5", "flag:met_elder", "quest:active_x",
    /// "item:potion", "gold:100"). All listed must pass for the choice to appear.</summary>
    public List<string> Conditions { get; set; } = new();
    /// <summary>Actions executed when picked. Reuses the trigger-action catalog so
    /// dialogue results (give item, start quest, change map…) need no new types.</summary>
    public List<TilemapTriggerAction> Actions { get; set; } = new();

    public DialogueChoice Clone() => new()
    { Text = Text, NextNodeId = NextNodeId, Conditions = new List<string>(Conditions) };
}

/// <summary>One page ("line") of a conversation. Nodes chain through NextNodeId /
/// choice targets so any branching tree can be authored.</summary>
public class DialogueNode
{
    public string Id { get; set; } = "";
    /// <summary>Speaker id into the asset's speaker list (empty = unnamed narrator).</summary>
    public string SpeakerId { get; set; } = "";
    /// <summary>Portrait emotion (DialogueEmotions) — drives the portrait variant.</summary>
    public string Emotion { get; set; } = DialogueEmotions.Neutral;
    /// <summary>Portrait image path (relative to the project). Empty = colored frame only.</summary>
    public string PortraitPath { get; set; } = "";
    /// <summary>Dialogue text (localized through the DialogueLibrary table).</summary>
    public string Text { get; set; } = "";
    /// <summary>Branching choices. When non-empty the window shows the list instead
    /// of the "next" prompt.</summary>
    public List<DialogueChoice> Choices { get; set; } = new();
    /// <summary>Node to continue to on Next. Empty = end the dialogue.</summary>
    public string NextNodeId { get; set; } = "";
    /// <summary>Auto-advance to NextNodeId after AutoAdvanceDelay seconds (no input).</summary>
    public bool AutoAdvance { get; set; }
    public float AutoAdvanceDelay { get; set; } = 1.5f;
    /// <summary>Actions fired when this node becomes the current page
    /// (play sound, camera shake, give item… — trigger catalog).</summary>
    public List<TilemapTriggerAction> OnStartActions { get; set; } = new();
    /// <summary>Actions fired when this node is left (next page, choice or end).</summary>
    public List<TilemapTriggerAction> OnEndActions { get; set; } = new();

    public DialogueNode Clone() => new()
    {
        Id = Id, SpeakerId = SpeakerId, Emotion = Emotion, PortraitPath = PortraitPath,
        Text = Text, NextNodeId = NextNodeId, AutoAdvance = AutoAdvance,
        AutoAdvanceDelay = AutoAdvanceDelay,
        Choices = Choices.Select(c => c.Clone()).ToList(),
        OnStartActions = OnStartActions.Select(a => a.Clone()).ToList(),
        OnEndActions = OnEndActions.Select(a => a.Clone()).ToList(),
    };
}

/// <summary>A complete reusable conversation — a tree of DialogueNodes. Assets are
/// shared: many NPCs/triggers can reference the same dialogue by Id.</summary>
public class DialogueAsset
{
    /// <summary>Stable reference id (triggers/NPCs bind to this).</summary>
    public string Id { get; set; } = "";
    /// <summary>Editor-facing display name.</summary>
    public string Name { get; set; } = "New Dialogue";
    /// <summary>First node shown when the dialogue starts.</summary>
    public string StartNodeId { get; set; } = "start";
    /// <summary>DialogueTheme name used to draw the conversation window + bubbles.</summary>
    public string ThemeName { get; set; } = "Default";
    public List<DialogueNode> Nodes { get; set; } = new();

    public DialogueNode? GetNode(string id) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));

    public DialogueAsset Clone() => new()
    {
        Id = Id, Name = Name, StartNodeId = StartNodeId, ThemeName = ThemeName,
        Nodes = Nodes.Select(n => n.Clone()).ToList(),
    };
}

/// <summary>A named speaker (portrait + name + name color). Speaker ids let one
/// speaker definition be reused across every node/asset.</summary>
public class SpeakerData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Speaker";
    /// <summary>Default portrait (nodes override per-line). Relative to the project.</summary>
    public string PortraitPath { get; set; } = "";
    /// <summary>Name label color 0..1.</summary>
    public float ColorR { get; set; } = 0.55f;
    public float ColorG { get; set; } = 0.75f;
    public float ColorB { get; set; } = 1f;
}

/// <summary>Bubble type — selects the border/accent color and the box style.</summary>
public static class DialogueBubbleTypes
{
    public const string Speech = "Speech";
    public const string Thought = "Thought";
    public const string Quest = "Quest";
    public const string Warning = "Warning";

    public static readonly string[] All = [Speech, Thought, Quest, Warning];
}

/// <summary>Reusable visual theme for the conversation window + bubbles. Fields with
/// Parent inheritance: a child theme's 0/""/null values fall back to its parent chain.</summary>
public class DialogueThemeData
{
    public string Name { get; set; } = "Default";
    /// <summary>Parent theme name (empty = none). Unset fields inherit the parent.</summary>
    public string Parent { get; set; } = "";

    // ── Conversation window ──
    public string BgImagePath { get; set; } = "";   // optional stretch background image
    public float WindowColorR { get; set; } = 0.06f;
    public float WindowColorG { get; set; } = 0.07f;
    public float WindowColorB { get; set; } = 0.12f;
    public float BorderColorR { get; set; } = 0.4f;
    public float BorderColorG { get; set; } = 0.5f;
    public float BorderColorB { get; set; } = 0.9f;
    public float NameColorR { get; set; } = 0.55f;
    public float NameColorG { get; set; } = 0.75f;
    public float NameColorB { get; set; } = 1f;
    public float TextColorR { get; set; } = 0.92f;
    public float TextColorG { get; set; } = 0.92f;
    public float TextColorB { get; set; } = 0.95f;
    public float ChoiceColorR { get; set; } = 0.7f;
    public float ChoiceColorG { get; set; } = 0.8f;
    public float ChoiceColorB { get; set; } = 1f;
    public float ChoiceHoverColorR { get; set; } = 1f;
    public float ChoiceHoverColorG { get; set; } = 0.9f;
    public float ChoiceHoverColorB { get; set; } = 0.4f;

    // ── Bubble ──
    public float BubbleColorR { get; set; } = 0.08f;
    public float BubbleColorG { get; set; } = 0.09f;
    public float BubbleColorB { get; set; } = 0.13f;
    public float BubbleBorderColorR { get; set; } = 0.75f;
    public float BubbleBorderColorG { get; set; } = 0.8f;
    public float BubbleBorderColorB { get; set; } = 0.95f;
    public float BubbleTextColorR { get; set; } = 0.95f;
    public float BubbleTextColorG { get; set; } = 0.95f;
    public float BubbleTextColorB { get; set; } = 1f;

    // ── Typography ──
    public string FontPath { get; set; } = "Artifacts\\fonts\\Worldstar.ttf";
    public float WindowFontSize { get; set; } = 14f;
    public float NameFontSize { get; set; } = 15f;
    public float BubbleFontSize { get; set; } = 11f;
    /// <summary>Typewriter speed (characters per second). 0 = instant.</summary>
    public float TypewriterCharsPerSecond { get; set; } = 45f;

    public Vector3 WindowColor => new(WindowColorR, WindowColorG, WindowColorB);
    public Vector3 BorderColor => new(BorderColorR, BorderColorG, BorderColorB);
    public Vector3 NameColor => new(NameColorR, NameColorG, NameColorB);
    public Vector3 TextColor => new(TextColorR, TextColorG, TextColorB);
    public Vector3 ChoiceColor => new(ChoiceColorR, ChoiceColorG, ChoiceColorB);
    public Vector3 ChoiceHoverColor => new(ChoiceHoverColorR, ChoiceHoverColorG, ChoiceHoverColorB);
    public Vector3 BubbleColor => new(BubbleColorR, BubbleColorG, BubbleColorB);
    public Vector3 BubbleBorderColor => new(BubbleBorderColorR, BubbleBorderColorG, BubbleBorderColorB);
    public Vector3 BubbleTextColor => new(BubbleTextColorR, BubbleTextColorG, BubbleTextColorB);
}

/// <summary>Persisted container for every dialogue asset, speaker and theme
/// (Assets/Dialogue/dialogues.json inside the project). Loaded lazily per project;
/// everything is designer-editable — no hardcoded dialogue content.</summary>
public static class DialogueLibrary
{
    private static readonly List<DialogueAsset> _assets = [];
    private static readonly List<SpeakerData> _speakers = [];
    private static readonly List<DialogueThemeData> _themes = [];

    /// <summary>Active localization language ("English" = original text).</summary>
    public static string CurrentLanguage { get; set; } = "English";
    /// <summary>Per-language text overrides: [language][original text or key] = translated.</summary>
    public static Dictionary<string, Dictionary<string, string>> Localizations { get; set; } = new();
    public static readonly string[] Languages =
        ["English", "Indonesia", "Japanese", "Chinese", "Korean", "Thai", "Vietnamese"];

    private static string? _loadedProjectRoot;
    private static bool _defaultsSeeded;

    // ════════════════════════════════════════════
    //  ACCESS
    // ════════════════════════════════════════════

    public static IReadOnlyList<DialogueAsset> Assets { get { EnsureLoaded(); return _assets; } }
    public static IReadOnlyList<SpeakerData> Speakers { get { EnsureLoaded(); return _speakers; } }
    public static IReadOnlyList<DialogueThemeData> Themes { get { EnsureLoaded(); return _themes; } }

    public static DialogueAsset? GetAsset(string idOrName)
    {
        EnsureLoaded();
        return _assets.FirstOrDefault(a => string.Equals(a.Id, idOrName, StringComparison.OrdinalIgnoreCase))
            ?? _assets.FirstOrDefault(a => string.Equals(a.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public static SpeakerData? GetSpeaker(string id) =>
        string.IsNullOrWhiteSpace(id) ? null
        : Speakers.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public static List<string> GetAssetIds() => Assets.Select(a => a.Id).ToList();
    public static List<string> GetSpeakerIds() => Speakers.Select(s => s.Id).ToList();
    public static List<string> GetThemeNames() => Themes.Select(t => t.Name).ToList();

    /// <summary>Resolve a theme by name with fallback to "Default".</summary>
    public static DialogueThemeData GetTheme(string name)
    {
        EnsureLoaded();
        var t = _themes.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (t != null) return t;
        return _themes.FirstOrDefault(x => x.Name == "Default") ?? _themes[0];
    }

    /// <summary>Translate a dialogue string for the active language. Untranslated
    /// strings pass through unchanged — new languages need no code changes.</summary>
    public static string Localize(string text)
    {
        if (string.IsNullOrEmpty(text) || CurrentLanguage == "English") return text;
        if (Localizations.TryGetValue(CurrentLanguage, out var table) &&
            table.TryGetValue(text, out var translated) && !string.IsNullOrEmpty(translated))
            return translated;
        return text;
    }

    // ════════════════════════════════════════════
    //  MUTATION (editor)
    // ════════════════════════════════════════════

    public static DialogueAsset AddAsset(string? name = null)
    {
        EnsureLoaded();
        int n = 1;
        string id = $"dialogue_{n}";
        while (_assets.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)))
            id = $"dialogue_{++n}";
        var asset = new DialogueAsset
        {
            Id = id,
            Name = name ?? $"Dialogue {n}",
            Nodes = [new DialogueNode { Id = "start", Text = "Hello, traveler!" }],
        };
        _assets.Add(asset);
        return asset;
    }

    public static void DuplicateAsset(string id)
    {
        EnsureLoaded();
        var src = GetAsset(id);
        if (src == null) return;
        var copy = src.Clone();
        copy.Id = src.Id + "_copy";
        copy.Name = src.Name + " Copy";
        _assets.Add(copy);
    }

    public static bool RemoveAsset(string id)
    {
        EnsureLoaded();
        var a = GetAsset(id);
        return a != null && _assets.Remove(a);
    }

    public static void AddSpeaker(SpeakerData speaker)
    {
        EnsureLoaded();
        _speakers.Add(speaker);
    }

    public static void RemoveSpeaker(SpeakerData speaker)
    {
        EnsureLoaded();
        _speakers.Remove(speaker);
    }

    public static void AddTheme(DialogueThemeData theme)
    {
        EnsureLoaded();
        _themes.Add(theme);
    }

    public static void RemoveTheme(DialogueThemeData theme)
    {
        EnsureLoaded();
        _themes.Remove(theme);
    }

    // ════════════════════════════════════════════
    //  PERSISTENCE (Assets/Dialogue/dialogues.json)
    // ════════════════════════════════════════════

    private sealed class DialogueFileData
    {
        public List<DialogueAsset> Assets { get; set; } = [];
        public List<SpeakerData> Speakers { get; set; } = [];
        public List<DialogueThemeData> Themes { get; set; } = [];
        public string CurrentLanguage { get; set; } = "English";
        public Dictionary<string, Dictionary<string, string>> Localizations { get; set; } = new();
    }

    public static string GetFilePath()
    {
        string dir = DarkEngine3D_gl_csharp.Engine.Project.ProjectManager.IsProjectLoaded
            ? Path.Combine(DarkEngine3D_gl_csharp.Engine.Project.ProjectManager.ProjectRoot!, "Assets", "Dialogue")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Dialogue");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "dialogues.json");
    }

    /// <summary>Load once per project (re-loads when the project root changes). Old
    /// files without new fields deserialize to defaults — no migration needed.</summary>
    public static void EnsureLoaded()
    {
        string? root = DarkEngine3D_gl_csharp.Engine.Project.ProjectManager.IsProjectLoaded
            ? DarkEngine3D_gl_csharp.Engine.Project.ProjectManager.ProjectRoot : null;
        if (_loadedProjectRoot == root && _defaultsSeeded) return;

        _loadedProjectRoot = root;
        _assets.Clear();
        _speakers.Clear();
        _themes.Clear();
        Localizations = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        CurrentLanguage = "English";

        SeedDefaults();

        try
        {
            string path = GetFilePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<DialogueFileData>(json);
                if (data != null)
                {
                    _assets.AddRange(data.Assets);
                    _speakers.AddRange(data.Speakers);
                    _themes.AddRange(data.Themes.Where(t => _themes.All(x => x.Name != t.Name)));
                    CurrentLanguage = string.IsNullOrEmpty(data.CurrentLanguage) ? "English" : data.CurrentLanguage;
                    foreach (var (lang, table) in data.Localizations)
                        Localizations[lang] = new Dictionary<string, string>(table, StringComparer.OrdinalIgnoreCase);
                }
                Console.WriteLine($"[Dialogue] Loaded {Assets.Count} asset(s), {Speakers.Count} speaker(s), {Themes.Count} theme(s)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dialogue] Load failed: {ex.Message}");
        }
    }

    public static void Save()
    {
        try
        {
            EnsureLoaded();
            var data = new DialogueFileData
            {
                Assets = _assets,
                Speakers = _speakers,
                Themes = _themes,
                CurrentLanguage = CurrentLanguage,
                Localizations = Localizations,
            };
            File.WriteAllText(GetFilePath(),
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[Dialogue] Saved {_assets.Count} asset(s) → {GetFilePath()}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dialogue] Save failed: {ex.Message}");
        }
    }

    /// <summary>Seed the built-in "Default"/"Medieval" themes + a sample speaker/asset
    /// so a fresh project has something usable immediately.</summary>
    private static void SeedDefaults()
    {
        _defaultsSeeded = true;
        if (_themes.Count == 0)
            _themes.Add(new DialogueThemeData { Name = "Default" });
        if (_themes.All(t => t.Name != "Medieval"))
            _themes.Add(new DialogueThemeData
            {
                Name = "Medieval",
                Parent = "Default",
                WindowColorR = 0.12f, WindowColorG = 0.08f, WindowColorB = 0.04f,
                BorderColorR = 0.72f, BorderColorG = 0.55f, BorderColorB = 0.2f,
                NameColorR = 1f, NameColorG = 0.82f, NameColorB = 0.35f,
                BubbleColorR = 0.11f, BubbleColorG = 0.08f, BubbleColorB = 0.05f,
            });
        if (_speakers.Count == 0)
            _speakers.Add(new SpeakerData { Id = "village_chief", Name = "Village Chief" });
        if (_assets.Count == 0)
        {
            _assets.Add(new DialogueAsset
            {
                Id = "village_intro",
                Name = "Village Intro",
                ThemeName = "Default",
                Nodes =
                [
                    new DialogueNode
                    {
                        Id = "start",
                        SpeakerId = "village_chief",
                        Text = "Selamat datang di desa kami! Apakah kamu ingin masuk ke dungeon ini?",
                        NextNodeId = "",
                        Choices =
                        [
                            new DialogueChoice { Text = "Ya", NextNodeId = "accept" },
                            new DialogueChoice { Text = "Tidak", NextNodeId = "decline" },
                        ],
                    },
                    new DialogueNode
                    {
                        Id = "accept",
                        SpeakerId = "village_chief",
                        Text = "Hati-hati di dalam sana, penjelajah muda.",
                    },
                    new DialogueNode
                    {
                        Id = "decline",
                        SpeakerId = "village_chief",
                        Text = "Baik. Kembali lagi kapan pun kamu siap.",
                    },
                ],
            });
        }
    }
}
