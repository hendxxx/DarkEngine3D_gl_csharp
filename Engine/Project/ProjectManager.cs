using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Project;

/// <summary>
/// Project metadata stored in {name}.projing inside the project root.
/// </summary>
public class ProjectData
{
    public string ProjectName { get; set; } = "Untitled";
    public string Version { get; set; } = "1.0";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime LastOpened { get; set; } = DateTime.UtcNow;
    public DateTime LastSaved { get; set; } = DateTime.MinValue;
}

/// <summary>
/// Manages project folder structure. All assets are relative to the project root.
/// 
/// Project structure:
///   ProjectRoot/
///   ├── {name}.projing      (metadata)
///   ├── game.ing             (combined scene file)
///   ├── settings.json        (editor settings, per-project)
///   ├── Assets/
///   │   ├── fonts/
///   │   ├── images/
///   │   └── models/
///   └── Scenes/
///       └── *.ing            (individual scene files)
/// </summary>
public static class ProjectManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Current project root directory (null = no project loaded).</summary>
    public static string? ProjectRoot { get; private set; }

    /// <summary>Whether a project is currently loaded.</summary>
    public static bool IsProjectLoaded => !string.IsNullOrEmpty(ProjectRoot) && Directory.Exists(ProjectRoot);

    // ── Derived paths (relative to ProjectRoot) ──

    public static string AssetsDir => Path.Combine(ProjectRoot ?? "", "Assets");
    public static string FontsDir => Path.Combine(AssetsDir, "fonts");
    public static string ImagesDir => Path.Combine(AssetsDir, "images");
    public static string ModelsDir => Path.Combine(AssetsDir, "models");
    public static string ScenesDir => Path.Combine(ProjectRoot ?? "", "Scenes");
    public static string GameIngPath => Path.Combine(ProjectRoot ?? "", "game.ing");
    public static string SettingsPath => Path.Combine(ProjectRoot ?? "", "settings.json");
    public static string ProjectJsonPath => Path.Combine(ProjectRoot ?? "", ProjectName + ".projing");

    /// <summary>Project name (read from .projing file).</summary>
    public static string ProjectName { get; private set; } = "Untitled";

    /// <summary>Event fired when project is opened or closed.</summary>
    public static event Action? OnProjectChanged;

    /// <summary>
    /// Create a new project at the given directory.
    /// Creates the folder structure and project.json.
    /// </summary>
    public static void CreateProject(string rootPath, string projectName)
    {
        if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(projectName))
            throw new ArgumentException("Root path and project name are required.");

        rootPath = Path.GetFullPath(rootPath);

        // Create folder structure
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "Assets"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Assets", "fonts"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Assets", "images"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Assets", "models"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Scenes"));

        // Copy default fonts from the exe's Artifacts into the project
        CopyFontsToProject(rootPath);

        // Write {name}.projing
        var projectData = new ProjectData
        {
            ProjectName = projectName,
            Created = DateTime.UtcNow,
            LastOpened = DateTime.UtcNow,
        };
        string json = JsonSerializer.Serialize(projectData, JsonOpts);
        File.WriteAllText(Path.Combine(rootPath, projectName + ".projing"), json);

        ProjectName = projectName;
        ProjectRoot = rootPath;
        Console.WriteLine($"[ProjectManager] Created project '{projectName}' at {rootPath}");
        OnProjectChanged?.Invoke();
    }

    /// <summary>
    /// Open an existing project from a .projing file path or directory.
    /// If a directory is given, scans for *.projing inside it.
    /// Returns false if no .projing found.
    /// </summary>
    public static bool OpenProject(string path)
    {
        path = Path.GetFullPath(path);
        string projectFile;
        string rootPath;

        if (File.Exists(path) && path.EndsWith(".projing", StringComparison.OrdinalIgnoreCase))
        {
            // Opened a .projing file directly
            projectFile = path;
            rootPath = Path.GetDirectoryName(path)!;
        }
        else if (Directory.Exists(path))
        {
            // Directory given — scan for *.projing
            var projFiles = Directory.GetFiles(path, "*.projing");
            if (projFiles.Length == 0)
            {
                Console.WriteLine($"[ProjectManager] No .projing file found at {path}");
                return false;
            }
            projectFile = projFiles[0];
            rootPath = path;
        }
        else
        {
            Console.WriteLine($"[ProjectManager] Path not found: {path}");
            return false;
        }

        // Read metadata and update lastOpened
        try
        {
            string json = File.ReadAllText(projectFile);
            var data = JsonSerializer.Deserialize<ProjectData>(json, JsonOpts);
            if (data != null)
            {
                ProjectName = data.ProjectName;
                data.LastOpened = DateTime.UtcNow;
                File.WriteAllText(projectFile, JsonSerializer.Serialize(data, JsonOpts));
            }
        }
        catch { /* ignore */ }

        ProjectRoot = rootPath;
        Console.WriteLine($"[ProjectManager] Opened project '{ProjectName}' at {rootPath}");
        OnProjectChanged?.Invoke();
        return true;
    }

    /// <summary>Close the current project.</summary>
    public static void CloseProject()
    {
        ProjectRoot = null;
        Console.WriteLine("[ProjectManager] Project closed");
        OnProjectChanged?.Invoke();
    }

    /// <summary>Update the .projing file's LastSaved timestamp. Called on every Save All.</summary>
    public static void TouchProject()
    {
        if (!IsProjectLoaded) return;
        string projectFile = ProjectJsonPath;
        if (!File.Exists(projectFile)) return;
        try
        {
            string json = File.ReadAllText(projectFile);
            var data = JsonSerializer.Deserialize<ProjectData>(json, JsonOpts);
            if (data != null)
            {
                data.LastSaved = DateTime.UtcNow;
                File.WriteAllText(projectFile, JsonSerializer.Serialize(data, JsonOpts));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProjectManager] TouchProject failed: {ex.Message}");
        }
    }

    /// <summary>Get a scene file path relative to the project.</summary>
    public static string GetScenePath(string sceneName)
    {
        if (IsProjectLoaded)
            return Path.Combine(ScenesDir, $"{sceneName}.ing");
        // Fallback to exe directory (no project)
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scenes", $"{sceneName}.ing");
    }

    /// <summary>Get the game.ing path (combined scene file).</summary>
    public static string GetGameIngPath()
    {
        if (IsProjectLoaded)
            return GameIngPath;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game.ing");
    }

    /// <summary>Get the settings.json path.</summary>
    public static string GetSettingsPath()
    {
        if (IsProjectLoaded)
            return SettingsPath;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
    }

    /// <summary>Get the fonts directory path.</summary>
    public static string GetFontsDir()
    {
        if (IsProjectLoaded)
            return FontsDir;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "fonts");
    }

    /// <summary>Get the images directory path.</summary>
    public static string GetImagesDir()
    {
        if (IsProjectLoaded)
            return ImagesDir;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "images");
    }

    /// <summary>
    /// Resolve a relative asset path against the project or exe directory.
    /// If the path is already absolute, returns it as-is.
    /// </summary>
    public static string ResolveAssetPath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return relativePath;

        if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
            return relativePath;

        // Try project assets first
        if (IsProjectLoaded)
        {
            string projectPath = Path.Combine(ProjectRoot!, relativePath);
            if (File.Exists(projectPath))
                return projectPath;

            // Also try Assets/ subfolder
            string assetsPath = Path.Combine(AssetsDir, relativePath);
            if (File.Exists(assetsPath))
                return assetsPath;
        }

        // Fallback to exe directory
        string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (File.Exists(exePath))
            return exePath;

        // Return original path even if not found (caller handles missing)
        return relativePath;
    }

    /// <summary>Copy default fonts from the exe's Artifacts/fonts into the project's Assets/fonts.</summary>
    private static void CopyFontsToProject(string projectRoot)
    {
        string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "fonts");
        string dstDir = Path.Combine(projectRoot, "Assets", "fonts");
        if (!Directory.Exists(srcDir)) return;
        try
        {
            foreach (string srcFile in Directory.GetFiles(srcDir, "*.ttf"))
            {
                string dstFile = Path.Combine(dstDir, Path.GetFileName(srcFile));
                if (!File.Exists(dstFile))
                    File.Copy(srcFile, dstFile, false);
            }
            Console.WriteLine($"[ProjectManager] Copied fonts to {dstDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProjectManager] Font copy failed: {ex.Message}");
        }
    }
}
