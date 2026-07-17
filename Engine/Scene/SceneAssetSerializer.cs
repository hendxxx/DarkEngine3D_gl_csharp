using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    };

    /// <summary>Directory where .ing scene files are stored.</summary>
    public static string ScenesDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scenes");

    /// <summary>Full path for a named scene file (e.g. "MainMenu" → "scenes/MainMenu.ing").</summary>
    public static string GetScenePath(string sceneName) =>
        Path.Combine(ScenesDirectory, $"{sceneName}.ing");

    /// <summary>Full path for the combined game.ing file.</summary>
    public static string GameIngPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game.ing");

    // ── Save ──

    /// <summary>Save a single scene (root UIElement + background objects) to a .ing file.</summary>
    public static void SaveScene(UIElement root, string filePath, List<BackgroundObjectData>? bgObjects = null)
    {
        var asset = new SceneAsset
        {
            SceneName = root.Name,
            Elements = [ToData(root)],
            BackgroundObjects = bgObjects ?? []
        };

        string json = JsonSerializer.Serialize(asset, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, json);
        Console.WriteLine($"[SceneAsset] Saved: {filePath} ({asset.BackgroundObjects.Count} bg objects)");
    }

    /// <summary>
    /// Save/replace one or more scenes in game.ing (combined file).
    /// Background objects are included in the saved scene data.
    /// </summary>
    public static void SaveGameIng(params (string name, UIElement root)[] scenes)
    {
        // Load existing manifest if present
        var manifest = LoadGameIng() ?? new SceneManifest();

        foreach (var (name, root) in scenes)
        {
            // Remove any existing scene with the same name (replace)
            manifest.Scenes.RemoveAll(s =>
                s.SceneName.Equals(name, StringComparison.OrdinalIgnoreCase));

            // Try to preserve background objects from previous save
            var existingBg = manifest.Scenes.Find(s =>
                s.SceneName.Equals(name, StringComparison.OrdinalIgnoreCase));
            var bgObjects = existingBg?.BackgroundObjects ?? [];

            // Add the new version
            manifest.Scenes.Add(new SceneAsset
            {
                SceneName = name,
                Elements = [ToData(root)],
                BackgroundObjects = bgObjects
            });
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(GameIngPath)!);
        File.WriteAllText(GameIngPath, json);
        Console.WriteLine($"[SceneAsset] Saved game.ing with {manifest.Scenes.Count} scenes");
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

    /// <summary>Load the combined game.ing file. Returns null if not found.</summary>
    public static SceneManifest? LoadGameIng()
    {
        if (!File.Exists(GameIngPath))
        {
            Console.WriteLine($"[SceneAsset] game.ing not found at: {GameIngPath}");
            return null;
        }

        string json = File.ReadAllText(GameIngPath);
        return JsonSerializer.Deserialize<SceneManifest>(json, JsonOptions);
    }

    /// <summary>
    /// Find a scene by name in the game.ing manifest.
    /// </summary>
    public static SceneAsset? FindScene(string sceneName)
    {
        var manifest = LoadGameIng();
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
            "dialog" => UIElementType.Dialog,
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
            FontSize = data.FontSize,
            FontPath = data.FontPath,
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
            ClickBehaviorLabel = data.ClickBehavior,
            HoverEnterLabel = data.HoverEnterBehavior,
            HoverExitLabel = data.HoverExitBehavior,
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
                UIElementType.Dialog => "Dialog",
                _ => "Button",
            },
            Text = elem.Text,
            X = elem.X,
            Y = elem.Y,
            Width = elem.Width,
            Height = elem.Height,
            FontSize = elem.FontSize,
            FontPath = elem.FontPath,
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
            ClickBehavior = elem.ClickBehaviorLabel,
            HoverEnterBehavior = elem.HoverEnterLabel,
            HoverExitBehavior = elem.HoverExitLabel,
        };

        foreach (var child in elem.Children)
            data.Children.Add(ToData(child));

        return data;
    }

    // ── Helpers ──

    private static Vector3 ArrayToVec3(float[]? arr, Vector3 fallback)
    {
        if (arr == null || arr.Length < 3) return fallback;
        return new Vector3(arr[0], arr[1], arr[2]);
    }

    private static float[] Vec3ToArray(Vector3 v) => [v.X, v.Y, v.Z];

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

    /// <summary>Save ALL registered scene roots to game.ing at once, including background objects.</summary>
    public static void SaveAllRegisteredScenes()
    {
        if (_registeredSceneRoots.Count == 0)
        {
            Console.WriteLine("[SceneAsset] No registered scenes to save.");
            return;
        }

        // Load existing manifest
        var manifest = LoadGameIng() ?? new SceneManifest();

        foreach (var (name, root) in _registeredSceneRoots)
        {
            // Remove any existing scene with the same name
            manifest.Scenes.RemoveAll(s =>
                s.SceneName.Equals(name, StringComparison.OrdinalIgnoreCase));

            // Use registered bg objects if available, otherwise fall back to existing manifest data
            var bgObjects = _registeredBgObjects.TryGetValue(name, out var registered)
                ? registered
                : [];

            manifest.Scenes.Add(new SceneAsset
            {
                SceneName = name,
                Elements = [ToData(root)],
                BackgroundObjects = bgObjects
            });
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(GameIngPath)!);
        File.WriteAllText(GameIngPath, json);
        Console.WriteLine($"[SceneAsset] Saved {_registeredSceneRoots.Count} registered scenes to {GameIngPath}");
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
