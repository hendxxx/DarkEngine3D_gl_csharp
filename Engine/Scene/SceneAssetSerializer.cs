using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.IO;

namespace DarkEngine3D_gl_csharp.Engine.Scene;

/// <summary>
/// Serializes/deserializes UIElement trees to/from .ing scene files (JSON).
/// Also manages default scene files saved alongside the executable.
/// </summary>
public static class SceneAssetSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new Helpers.Vector3JsonConverter() },
    };

    /// <summary>Expose JsonOptions for use by other components (e.g., SceneManagerPanel Save).</summary>
    public static JsonSerializerOptions GetJsonOptions() => JsonOptions;

    // 🛠️ FIX #8: Cache the parsed game.ing manifest to avoid full re-parse on every call.
    // The manifest is invalidated (set to null) whenever SaveGameIng or SaveAllRegisteredScenes
    // writes to game.ing, so subsequent reads get fresh data.
    private static SceneManifest? _cachedGameIngManifest = null;
    private static DateTime _gameIngLastWriteTime = DateTime.MinValue;
    private static bool _gameIngCacheValid = false;

    /// <summary>Invalidate the game.ing cache so the next read re-parses the file.</summary>
    private static void InvalidateGameIngCache()
    {
        _cachedGameIngManifest = null;
        _gameIngCacheValid = false;
    }

    /// <summary>Directory where .ing scene files are stored.</summary>
    public static string ScenesDirectory =>
        Project.ProjectManager.IsProjectLoaded ? Project.ProjectManager.ScenesDir
        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scenes");

    /// <summary>Full path for a named scene file (e.g. "MainMenu" → "scenes/MainMenu.ing").</summary>
    public static string GetScenePath(string sceneName) =>
        Project.ProjectManager.IsProjectLoaded ? Project.ProjectManager.GetScenePath(sceneName)
        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scenes", $"{sceneName}.ing");

    /// <summary>Full path for the combined game.ing file.</summary>
    public static string GameIngPath =>
        Project.ProjectManager.IsProjectLoaded ? Project.ProjectManager.GameIngPath
        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game.ing");

    // ── Per-scene transition settings (editor → save pipeline) ──
    // TransitionPanel writes to this dictionary; SaveAllRegisteredScenes reads from it.
    public static Dictionary<string, (string TransitionType, float Duration, float[] Color, string Easing, bool BlockInput)> TransitionSettings { get; } = new();

    /// <summary>Set transition settings for a scene (called by TransitionPanel).</summary>
    public static void SetTransitionSettings(string sceneName, string transitionType, float duration, float[] color, string easing, bool blockInput)
    {
        TransitionSettings[sceneName] = (transitionType, duration, color, easing, blockInput);
    }

    /// <summary>Apply saved transition settings to a SceneAsset during save.</summary>
    public static void ApplyTransitionSettings(SceneAsset asset)
    {
        if (TransitionSettings.TryGetValue(asset.SceneName, out var ts))
        {
            asset.TransitionType = ts.TransitionType;
            asset.TransitionDuration = ts.Duration;
            asset.TransitionColor = ts.Color;
            asset.TransitionEasing = ts.Easing;
            asset.TransitionBlockInput = ts.BlockInput;
        }
    }

    /// <summary>Load a SceneManifest from an arbitrary .ing file path.
    /// Supports both manifest files (multiple scenes) and single scene files.
    /// Returns null if not found or invalid.</summary>
    public static SceneManifest? LoadManifestFromPath(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[SceneAsset] File not found: {filePath}");
            return null;
        }

        // 🛠️ FIX #8: Use caching for game.ing path specifically.
        // Check if the file was modified since last cache.
        bool isGameIng = string.Equals(
            filePath, GameIngPath, StringComparison.OrdinalIgnoreCase);

        if (isGameIng && _gameIngCacheValid)
        {
            try
            {
                DateTime lastWrite = File.GetLastWriteTimeUtc(filePath);
                if (lastWrite <= _gameIngLastWriteTime && _cachedGameIngManifest != null)
                {
                    Console.WriteLine($"[SceneAsset] Using cached game.ing manifest ({_cachedGameIngManifest.Scenes.Count} scenes)");
                    return _cachedGameIngManifest;
                }
            }
            catch
            {
                // If we can't check last write time, fall through to re-parse
            }
        }

        try
        {
            string json = File.ReadAllText(filePath);

            // Try parsing as SceneManifest first (game.ing format, multiple scenes)
            var manifest = JsonSerializer.Deserialize<SceneManifest>(json, JsonOptions);
            if (manifest != null && manifest.Scenes.Count > 0)
            {
                // 🛠️ FIX #8: Cache the parsed manifest for game.ing
                if (isGameIng)
                {
                    _cachedGameIngManifest = manifest;
                    _gameIngCacheValid = true;
                    try { _gameIngLastWriteTime = File.GetLastWriteTimeUtc(filePath); }
                    catch { /* ignore */ }
                    Console.WriteLine($"[SceneAsset] Parsed & cached game.ing ({manifest.Scenes.Count} scenes)");
                }
                return manifest;
            }

            // Fallback: try as single SceneAsset file
            var asset = JsonSerializer.Deserialize<SceneAsset>(json, JsonOptions);
            if (asset != null && !string.IsNullOrEmpty(asset.SceneName))
            {
                return new SceneManifest { Scenes = [asset] };
            }

            Console.WriteLine($"[SceneAsset] Unknown format in: {filePath}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneAsset] Failed to parse {filePath}: {ex.Message}");
            return null;
        }
    }

    // ── Save ──

    /// <summary>Save a single scene (root UIElement + background objects) to a .ing file.</summary>
    public static void SaveScene(UIElement root, string filePath, List<BackgroundObjectData>? bgObjects = null)
    {
        var asset = new SceneAsset
        {
            SceneName = root.Name,
            Elements = [ToSceneData(root)],
            BackgroundObjects = bgObjects ?? []
        };
        ApplyTransitionSettings(asset);

        // Store asset paths relative to the exe so the .ing is portable.
        NormalizeBgForSave(asset.BackgroundObjects);

        string json = JsonSerializer.Serialize(asset, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, json);
        Console.WriteLine($"[SceneAsset] Saved: {filePath} ({asset.BackgroundObjects.Count} bg objects)");
    }

    /// <summary>
    /// Save/replace one or more scenes in game.ing (combined file).
    /// Creates a fresh manifest from the supplied data — does NOT load existing file.
    /// </summary>
    public static void SaveGameIng(params (string name, UIElement root)[] scenes)
    {
        // Build fresh manifest from supplied scenes only
        var manifest = new SceneManifest();

        foreach (var (name, root) in scenes)
        {
            var sceneAsset = new SceneAsset
            {
                SceneName = name,
                Elements = [ToSceneData(root)],
                BackgroundObjects = []
            };
            ApplyTransitionSettings(sceneAsset);
            manifest.Scenes.Add(sceneAsset);
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(GameIngPath)!);
        File.WriteAllText(GameIngPath, json);
        Console.WriteLine($"[SceneAsset] Saved game.ing with {manifest.Scenes.Count} scenes (fresh write)");

        // Invalidate cache so next read gets fresh data
        InvalidateGameIngCache();
    }

    // ── Load ──

    /// <summary>Load a single scene .ing file. Returns null if not found.</summary>
    public static SceneAsset? LoadScene(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[SceneAsset] File not found: {filePath}");
            return null;
        }

        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<SceneAsset>(json, JsonOptions);
    }

    /// <summary>Load the combined game.ing file. Returns null if not found.
    /// 🛠️ FIX #8: Now routes through LoadManifestFromPath to use the cache.</summary>
    public static SceneManifest? LoadGameIng()
    {
        return LoadManifestFromPath(GameIngPath);
    }

    /// <summary>
    /// Find a scene by name in a manifest file.
    /// If filePath is null, searches game.ing (legacy fallback).
    /// </summary>
    public static SceneAsset? FindScene(string sceneName, string? filePath = null)
    {
        var manifest = LoadManifestFromPath(filePath ?? GameIngPath);
        return manifest?.Scenes.Find(s =>
            s.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase));
    }

    // ── Convert: SceneElementData → UIElement ──

    /// <summary>Convert a deserialized SceneElementData tree back to live UIElement objects.</summary>
    public static UIElement ToUIElement(SceneElementData data)
    {
        var type = data.Type.ToLowerInvariant() switch
        {
            "scene" => UIElementType.Scene,
            "container" => UIElementType.Container,
            "button" => UIElementType.Button,
            "label" => UIElementType.Label,
            // Backward compat: old "dialog" type → Container, old "slider" → SliderNumber
            "dialog" => UIElementType.Container,
            "slider" => UIElementType.SliderNumber,
            "slidernumber" => UIElementType.SliderNumber,
            "slidertext" => UIElementType.SliderText,
            "checkbox" => UIElementType.Checkbox,
            "dropdown" => UIElementType.Dropdown,
            "textbox" => UIElementType.TextBox,
            "radiobutton" => UIElementType.RadioButton,
            _ => UIElementType.Button,
        };

        var elem = new UIElement
        {
            Name = data.Name,
            Text = data.Text,
            Type = type,
            X = data.X,
            Y = data.Y,
            Width = data.Width,
            Height = data.Height,
            ImagePath = ResolveImagePath(data.ImagePath),
            ImageMode = data.ImageMode.ToLowerInvariant() switch
            {
                "zoom" => ImageMode.Zoom,
                "fill" => ImageMode.Fill,
                _ => ImageMode.Stretch,
            },
            FontSize = data.FontSize,
            FontPath = PathHelpers.Resolve(data.FontPath),
            TextColor = ArrayToVec3(data.TextColor, new Vector3(0.95f, 0.95f, 1f)),
            BgColor = ArrayToVec3(data.BgColor, new Vector3(0.10f, 0.12f, 0.18f)),
            BorderColor = ArrayToVec3(data.BorderColor, new Vector3(0.15f, 0.18f, 0.25f)),
            HoverTextColor = ArrayToVec3(data.HoverTextColor, new Vector3(0.95f, 0.95f, 1f)),
            HoverBgColor = ArrayToVec3(data.HoverBgColor, new Vector3(0.22f, 0.28f, 0.45f)),
            HoverBorderColor = ArrayToVec3(data.HoverBorderColor, new Vector3(0.5f, 0.6f, 1.0f)),
            Alignment = data.Alignment.ToLowerInvariant() switch
            {
                "left" => TextAlignment.Left,
                "right" => TextAlignment.Right,
                _ => TextAlignment.Center,
            },
            IsVisible = data.IsVisible,
            Opacity = Math.Clamp(data.Opacity, 0f, 1f),
            AutoFillWindow = data.AutoFillWindow,
            AutoCenterX = data.AutoCenterX,
            AutoCenterY = data.AutoCenterY,
            Anchor = !string.IsNullOrEmpty(data.Anchor) ? data.Anchor.ToLowerInvariant() switch
            {
                "topleft" => UIAnchor.TopLeft,
                "topcenter" => UIAnchor.TopCenter,
                "topright" => UIAnchor.TopRight,
                "centerleft" => UIAnchor.CenterLeft,
                "center" => UIAnchor.Center,
                "centerright" => UIAnchor.CenterRight,
                "bottomleft" => UIAnchor.BottomLeft,
                "bottomcenter" => UIAnchor.BottomCenter,
                "bottomright" => UIAnchor.BottomRight,
                _ => UIAnchor.None
            } : UIAnchor.None,
            UseHover = data.UseHover,
            ClickBehaviorLabel = data.ClickBehavior,
            HoverEnterLabel = data.HoverEnterBehavior,
            HoverExitLabel = data.HoverExitBehavior,            FallbackText = data.FallbackText,
            WordWrap = data.WordWrap,
            TriggeredByKeyboardButton = data.TriggeredByKeyboardButton,
 
            // ── New element type properties ──
            MinValue = data.MinValue,
            MaxValue = data.MaxValue,
            Step = data.Step,
            CurrentValue = data.CurrentValue,
            IsChecked = data.IsChecked,
            Options = data.Options,
            SelectedIndex = data.SelectedIndex,
            Placeholder = data.Placeholder,
            MaxLength = data.MaxLength,
            InputText = data.InputText,
            TextOptions = data.TextOptions,
            SelectedTextIndex = data.SelectedTextIndex,

            // ── Container scroll properties ──
            ScrollY = data.ScrollY,
            ScrollBarWidth = data.ScrollBarWidth > 0f ? data.ScrollBarWidth : 10f,
            ScrollBarTrackColor = ArrayToVec3(data.ScrollBarTrackColor, new Vector3(0.15f, 0.15f, 0.20f)),
            ScrollBarThumbColor = ArrayToVec3(data.ScrollBarThumbColor, new Vector3(0.45f, 0.45f, 0.55f)),
            ScrollBarThumbHoverColor = ArrayToVec3(data.ScrollBarThumbHoverColor, new Vector3(0.55f, 0.55f, 0.65f)),

            // ── Visual style properties ──
            SliderTrackColor = ArrayToVec3(data.SliderTrackColor, new Vector3(0.30f, 0.30f, 0.35f)),
            SliderFillColor = ArrayToVec3(data.SliderFillColor, new Vector3(0.3f, 0.6f, 1.0f)),
            SliderThumbColor = ArrayToVec3(data.SliderThumbColor, new Vector3(0.9f, 0.9f, 1.0f)),
            SliderThumbBorderColor = ArrayToVec3(data.SliderThumbBorderColor, new Vector3(0.3f, 0.6f, 1.0f)),
            SliderThumbSize = data.SliderThumbSize > 0f ? data.SliderThumbSize : 14f,
            SliderTrackHeight = data.SliderTrackHeight > 0f ? data.SliderTrackHeight : 6f,
            SliderValuePosition = data.SliderLabelPosition switch
            {
                0 => SliderLabelPosition.None,
                1 => SliderLabelPosition.Left,
                2 => SliderLabelPosition.Right,
                3 => SliderLabelPosition.Top,
                4 => SliderLabelPosition.Bottom,
                _ => SliderLabelPosition.Left,
            },
            SliderLabelSpacing = data.SliderLabelSpacing,
            LabelSpacing = data.LabelSpacing,
            CheckmarkColor = ArrayToVec3(data.CheckmarkColor, new Vector3(0.9f, 0.9f, 1.0f)),
            CheckedBgColor = ArrayToVec3(data.CheckedBgColor, new Vector3(0.25f, 0.55f, 1.0f)),
            UncheckedBgColor = ArrayToVec3(data.UncheckedBgColor, new Vector3(0.15f, 0.15f, 0.22f)),
            ArrowColor = ArrayToVec3(data.ArrowColor, new Vector3(0.5f, 0.5f, 0.7f)),
            CursorColor = ArrayToVec3(data.CursorColor, new Vector3(0.5f, 0.8f, 1.0f)),
            RadioSelectedColor = ArrayToVec3(data.RadioSelectedColor, new Vector3(0.3f, 0.7f, 1.0f)),
            RadioSelectedBgColor = ArrayToVec3(data.RadioSelectedBgColor, new Vector3(0.2f, 0.4f, 0.65f)),
            RadioUnselectedBgColor = ArrayToVec3(data.RadioUnselectedBgColor, new Vector3(0.15f, 0.15f, 0.22f)),
            RadioGroup = data.RadioGroup ?? "default",
        };

        foreach (var childData in data.Children)
            elem.AddChild(ToUIElement(childData));

        return elem;
    }

    // ── Convert: UIElement → SceneElementData ──

    /// <summary>Convert a UIElement tree to serializable SceneElementData.</summary>
    public static SceneElementData ToData(UIElement elem)
    {
        var data = new SceneElementData
        {
            Name = elem.Name,
            Type = elem.Type switch
            {
                UIElementType.Scene => "Scene",
                UIElementType.Container => "Container",
                UIElementType.Button => "Button",
                UIElementType.Label => "Label",
                UIElementType.SliderNumber => "SliderNumber",
                UIElementType.SliderText => "SliderText",
                UIElementType.Checkbox => "Checkbox",
                UIElementType.Dropdown => "Dropdown",
                UIElementType.TextBox => "TextBox",
                UIElementType.RadioButton => "RadioButton",
                _ => "Button",
            },
            Text = elem.Text,
            X = elem.X,
            Y = elem.Y,
            Width = elem.Width,
            Height = elem.Height,
            ImagePath = MakeRelativePath(elem.ImagePath),
            ImageMode = elem.ImageMode switch
            {
                ImageMode.Zoom => "Zoom",
                ImageMode.Fill => "Fill",
                _ => "Stretch",
            },
            FontSize = elem.FontSize,
            FontPath = PathHelpers.MakeRelative(elem.FontPath),
            TextColor = Vec3ToArray(elem.TextColor),
            BgColor = Vec3ToArray(elem.BgColor),
            BorderColor = Vec3ToArray(elem.BorderColor),
            HoverTextColor = Vec3ToArray(elem.HoverTextColor),
            HoverBgColor = Vec3ToArray(elem.HoverBgColor),
            HoverBorderColor = Vec3ToArray(elem.HoverBorderColor),
            Alignment = elem.Alignment switch
            {
                TextAlignment.Left => "Left",
                TextAlignment.Right => "Right",
                _ => "Center",
            },
            IsVisible = elem.IsVisible,
            Opacity = elem.Opacity,
            AutoFillWindow = elem.AutoFillWindow,
            AutoCenterX = elem.AutoCenterX,
            AutoCenterY = elem.AutoCenterY,
            Anchor = elem.Anchor.ToString(),
            UseHover = elem.UseHover,
            ClickBehavior = elem.ClickBehaviorLabel,
            HoverEnterBehavior = elem.HoverEnterLabel,
            HoverExitBehavior = elem.HoverExitLabel,            FallbackText = elem.FallbackText,
            WordWrap = elem.WordWrap,
            TriggeredByKeyboardButton = elem.TriggeredByKeyboardButton,
 
            // ── New element type properties ──
            MinValue = elem.MinValue,
            MaxValue = elem.MaxValue,
            Step = elem.Step,
            CurrentValue = elem.CurrentValue,
            IsChecked = elem.IsChecked,
            Options = elem.Options,
            SelectedIndex = elem.SelectedIndex,
            Placeholder = elem.Placeholder,
            MaxLength = elem.MaxLength,
            InputText = elem.InputText,
            TextOptions = elem.TextOptions,
            SelectedTextIndex = elem.SelectedTextIndex,

            // ── Container scroll properties ──
            ScrollY = elem.ScrollY,
            ScrollBarWidth = elem.ScrollBarWidth,
            ScrollBarTrackColor = Vec3ToArray(elem.ScrollBarTrackColor),
            ScrollBarThumbColor = Vec3ToArray(elem.ScrollBarThumbColor),
            ScrollBarThumbHoverColor = Vec3ToArray(elem.ScrollBarThumbHoverColor),

            // ── Visual style properties ──
            SliderTrackColor = Vec3ToArray(elem.SliderTrackColor),
            SliderFillColor = Vec3ToArray(elem.SliderFillColor),
            SliderThumbColor = Vec3ToArray(elem.SliderThumbColor),
            SliderThumbBorderColor = Vec3ToArray(elem.SliderThumbBorderColor),
            SliderThumbSize = elem.SliderThumbSize,
            SliderTrackHeight = elem.SliderTrackHeight,
            SliderLabelPosition = elem.SliderValuePosition switch
            {
                SliderLabelPosition.None => 0,
                SliderLabelPosition.Left => 1,
                SliderLabelPosition.Right => 2,
                SliderLabelPosition.Top => 3,
                SliderLabelPosition.Bottom => 4,
                _ => 2,
            },
            SliderLabelSpacing = elem.SliderLabelSpacing,
            LabelSpacing = elem.LabelSpacing,
            CheckmarkColor = Vec3ToArray(elem.CheckmarkColor),
            CheckedBgColor = Vec3ToArray(elem.CheckedBgColor),
            UncheckedBgColor = Vec3ToArray(elem.UncheckedBgColor),
            ArrowColor = Vec3ToArray(elem.ArrowColor),
            CursorColor = Vec3ToArray(elem.CursorColor),
            RadioSelectedColor = Vec3ToArray(elem.RadioSelectedColor),
            RadioSelectedBgColor = Vec3ToArray(elem.RadioSelectedBgColor),
            RadioUnselectedBgColor = Vec3ToArray(elem.RadioUnselectedBgColor),
            RadioGroup = elem.RadioGroup,
        };

        foreach (var child in elem.Children)
            data.Children.Add(ToData(child));

        return data;
    }

    /// <summary>
    /// Ensure the serialized top-level element is a Scene root. If the provided
    /// element is not a Scene, wrap it under a Scene-typed SceneElementData so
    /// saved .ing files always contain a Scene root per scene definition.
    /// This avoids losing the Scene root type when the runtime root is a single
    /// interactive element (legacy/compact format).
    /// </summary>
    public static SceneElementData ToSceneData(UIElement root)
    {
        if (root.Type == UIElementType.Scene)
            return ToData(root);

        var wrapper = new SceneElementData
        {
            Name = root.Name,
            Type = "Scene",
        };
        wrapper.Children.Add(ToData(root));
        return wrapper;
    }

    // ── Helpers ──

    private static Vector3 ArrayToVec3(float[]? arr, Vector3 fallback)
    {
        if (arr == null || arr.Length < 3) return fallback;
        return new Vector3(arr[0], arr[1], arr[2]);
    }

    private static float[] Vec3ToArray(Vector3 v) => [v.X, v.Y, v.Z];

    /// <summary>Base directory used for relative asset paths (so .ing files are portable).</summary>
    private static string AppBaseDir => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>Convert an absolute image path to an exe-relative path (for saving to .ing).
    /// Empty or already-relative paths pass through.</summary>
    private static string MakeRelativePath(string path) => PathHelpers.MakeRelative(path);

    /// <summary>Resolve a possibly-relative image path to an absolute path (for runtime use).
    /// Empty or already-absolute paths pass through.</summary>
    private static string ResolveImagePath(string path) => PathHelpers.Resolve(path);

    /// <summary>Normalize a BackgroundObjectData's model path to exe-relative form (for saving).</summary>
    public static BackgroundObjectData NormalizeBgForSave(BackgroundObjectData bg)
    {
        if (bg != null) bg.ModelPath = PathHelpers.MakeRelative(bg.ModelPath);
        return bg;
    }

    /// <summary>Normalize a list of background objects' model paths to exe-relative form (for saving).</summary>
    public static void NormalizeBgForSave(List<BackgroundObjectData>? bgObjects)
    {
        if (bgObjects == null) return;
        foreach (var bg in bgObjects)
            NormalizeBgForSave(bg);
    }

    /// <summary>Resolve a BackgroundObjectData's model path to an absolute path (for runtime use).</summary>
    public static BackgroundObjectData NormalizeBgForLoad(BackgroundObjectData bg)
    {
        if (bg != null) bg.ModelPath = PathHelpers.Resolve(bg.ModelPath);
        return bg;
    }

    /// <summary>Resolve a list of background objects' model paths to absolute paths (for runtime use).</summary>
    public static void NormalizeBgForLoad(List<BackgroundObjectData>? bgObjects)
    {
        if (bgObjects == null) return;
        foreach (var bg in bgObjects)
            NormalizeBgForLoad(bg);
    }

    // ── Registered scene roots (for IDE Save All) ──

    /// <summary>Dictionary of scene name → UIElement root, registered by each scene on Enter().</summary>
    private static readonly Dictionary<string, UIElement> _registeredSceneRoots = [];

    /// <summary>Register a scene root so it can be included when saving all scenes to game.ing.</summary>
    public static void RegisterSceneRoot(string sceneName, UIElement root)
    {
        _registeredSceneRoots[sceneName] = root;
    }

    /// <summary>Unregister a scene root (called on scene Exit()).</summary>
    public static void UnregisterSceneRoot(string sceneName)
    {
        _registeredSceneRoots.Remove(sceneName);
    }

    /// <summary>Dictionary of scene name → background object data, registered by each scene on Enter().</summary>
    private static readonly Dictionary<string, List<BackgroundObjectData>> _registeredBgObjects = [];

    /// <summary>Register background object data for a scene so it's included when saving to game.ing.</summary>
    public static void RegisterBgObjects(string sceneName, List<BackgroundObjectData> bgObjects)
    {
        _registeredBgObjects[sceneName] = bgObjects;
    }

    /// <summary>Save ALL registered scene roots to game.ing at once, including background objects.
    /// Creates a fresh manifest — does NOT load existing file.</summary>
    public static void SaveAllRegisteredScenes()
    {
        if (_registeredSceneRoots.Count == 0)
        {
            Console.WriteLine("[SceneAsset] No registered scenes to save.");
            return;
        }

        // Build fresh manifest from registered scenes only
        var manifest = new SceneManifest();

        foreach (var (name, root) in _registeredSceneRoots)
        {
            var bgObjects = _registeredBgObjects.TryGetValue(name, out var registered)
                ? registered
                : [];

            var sceneAsset = new SceneAsset
            {
                SceneName = name,
                Elements = [ToSceneData(root)],
                BackgroundObjects = bgObjects
            };
            ApplyTransitionSettings(sceneAsset);
            manifest.Scenes.Add(sceneAsset);

            // Store asset paths relative to the exe so the .ing is portable.
            NormalizeBgForSave(manifest.Scenes[^1].BackgroundObjects);
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(GameIngPath)!);
        File.WriteAllText(GameIngPath, json);
        Console.WriteLine($"[SceneAsset] Saved {_registeredSceneRoots.Count} registered scenes to {GameIngPath} (fresh write)");

        // Invalidate cache so next read gets fresh data
        InvalidateGameIngCache();
    }

    /// <summary>Get the list of registered scene names (for IDE display).</summary>
    public static IReadOnlyCollection<string> GetRegisteredSceneNames() =>
        _registeredSceneRoots.Keys;

    // ── Ensure default scenes directory exists ──

    /// <summary>Create the scenes directory and return its path.</summary>
    public static void EnsureScenesDirectory()
    {
        Directory.CreateDirectory(ScenesDirectory);
    }
}
