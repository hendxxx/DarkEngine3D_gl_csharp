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
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scenes");

    /// <summary>Full path for a named scene file (e.g. "MainMenu" → "scenes/MainMenu.ing").</summary>
    public static string GetScenePath(string sceneName) =>
        Path.Combine(ScenesDirectory, $"{sceneName}.ing");

    /// <summary>Full path for the combined game.ing file.</summary>
    public static string GameIngPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game.ing");

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
    /// Creates a fresh manifest from the supplied data — does NOT load existing file.
    /// </summary>
    public static void SaveGameIng(params (string name, UIElement root)[] scenes)
    {
        // Build fresh manifest from supplied scenes only
        var manifest = new SceneManifest();

        foreach (var (name, root) in scenes)
        {
            manifest.Scenes.Add(new SceneAsset
            {
                SceneName = name,
                Elements = [ToData(root)],
                BackgroundObjects = []
            });
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(GameIngPath)!);
        File.WriteAllText(GameIngPath, json);
        Console.WriteLine($"[SceneAsset] Saved game.ing with {manifest.Scenes.Count} scenes (fresh write)");
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
    /// Find a scene by name in the game.ing manifest.
    /// 🛠️ FIX #8: Now routes through LoadManifestFromPath to use the cache.
    /// </summary>
    public static SceneAsset? FindScene(string sceneName)
    {
        var manifest = LoadManifestFromPath(GameIngPath);
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
            ImagePath = ResolveImagePath(data.ImagePath),
            ImageMode = data.ImageMode.ToLowerInvariant() switch
            {
                "zoom" => ImageMode.Zoom,
                "fill" => ImageMode.Fill,
                _ => ImageMode.Stretch,
            },
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
            AutoFillWindow = data.AutoFillWindow,
            AutoCenter = data.AutoCenter,
            UseHover = data.UseHover,
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
            ImagePath = MakeRelativePath(elem.ImagePath),
            ImageMode = elem.ImageMode switch
            {
                ImageMode.Zoom => "Zoom",
                ImageMode.Fill => "Fill",
                _ => "Stretch",
            },
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
            AutoFillWindow = elem.AutoFillWindow,
            AutoCenter = elem.AutoCenter,
            UseHover = elem.UseHover,
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

    /// <summary>Base directory used for relative image paths (so .ing files are portable).</summary>
    private static string AppBaseDir => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>
    /// Convert an absolute image path to a relative path (for saving to .ing).
    /// If the path is empty or already relative, return as-is.
    /// </summary>
    private static string MakeRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (!Path.IsPathRooted(path)) return path; // already relative
        try
        {
            return Path.GetRelativePath(AppBaseDir, path);
        }
        catch
        {
            return path; // fallback: keep original
        }
    }

    /// <summary>
    /// Resolve a possibly-relative image path to an absolute path (for runtime use).
    /// If the path is empty or already absolute, return as-is.
    /// </summary>
    private static string ResolveImagePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (Path.IsPathRooted(path)) return path; // already absolute
        try
        {
            return Path.GetFullPath(Path.Combine(AppBaseDir, path));
        }
        catch
        {
            return path; // fallback: keep original
        }
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
        Console.WriteLine($"[SceneAsset] Saved {_registeredSceneRoots.Count} registered scenes to {GameIngPath} (fresh write)");
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
