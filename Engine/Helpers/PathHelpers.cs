using System.Text;

namespace DarkEngine3D_gl_csharp.Engine.Helpers;

/// <summary>
/// Central helpers for resolving asset paths relative to the executable folder.
///
/// Rule (per project convention): every path stored in a .ing file / settings file
/// must be RELATIVE to the exe directory (e.g. "Artifacts/fonts/Worldstar.ttf"),
/// never an absolute "C:\..." path. At runtime, relative paths are resolved against
/// <see cref="AppBaseDir"/> so the whole game folder can be moved / installed anywhere.
/// </summary>
public static class PathHelpers
{
    /// <summary>Directory the executable runs from.</summary>
    public static string AppBaseDir => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>Normalize a path: '/' → '\\' and collapse duplicated separators.
    /// This also cleans up the old double-backslash defaults
    /// ("Artifacts\\fonts\\...").
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? "";
        string p = path.Replace('/', '\\');

        var sb = new StringBuilder(p.Length);
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i] == '\\' && sb.Length > 0 && sb[^1] == '\\') continue;
            sb.Append(p[i]);
        }
        return sb.ToString();
    }

    /// <summary>Resolve a possibly-relative path to an absolute path.
    /// When a project is loaded, known project assets (for example
    /// "Artifacts/...", "Assets/...", "Scenes/...", and other project-relative paths)
    /// are resolved against the loaded project root first, then the exe folder is used
    /// as a fallback. Absolute paths are returned as-is.</summary>
    public static string Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? "";
        string p = Normalize(path);
        try
        {
            // When a project is loaded, force known project-relative asset paths to resolve
            // against the loaded project root (for example
            // "Artifacts/shaders/vertex_shader.glsl" ->
            // "C:\Users\...\Dark Projects\Sample UI - Basic Menu\Artifacts\shaders\vertex_shader.glsl"),
            // even if a copy also exists next to the executable. That keeps the resolved path
            // anchored to the active project instead of the debug/publish folder.
            if (DarkEngine3D_gl_csharp.Engine.Project.ProjectManager.IsProjectLoaded)
            {
                string projectRootPath = Path.GetFullPath(Path.Combine(DarkEngine3D_gl_csharp.Engine.Project.ProjectManager.ProjectRoot!, p));
                return projectRootPath;
            }

            string combined = Path.Combine(AppBaseDir, p);
            return Path.GetFullPath(combined);
        }
        catch
        {
            return p; // fallback: keep original
        }
    }

    /// <summary>
    /// Convert an absolute path to an exe-relative path (for saving to .ing).
    /// If the path is empty or already relative, it is returned as-is.
    /// Paths living under an "Artifacts" folder (either the project source or the
    /// output copy) are stored as "Artifacts\..." so they stay portable: the same
    /// .ing file works wherever the exe is installed.
    /// </summary>
    public static string MakeRelative(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? "";
        string p = Normalize(path);
        if (!Path.IsPathRooted(p)) return p; // already relative

        try
        {
            // If the path contains an "Artifacts" directory segment, store the portable
            // "Artifacts\..." form so it resolves against the exe's own Artifacts folder.
            string[] parts = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Equals("Artifacts", StringComparison.OrdinalIgnoreCase))
                    return string.Join('\\', parts, i, parts.Length - i);
            }

            return Path.GetRelativePath(AppBaseDir, p);
        }
        catch
        {
            return p; // fallback: keep original
        }
    }

    /// <summary>True when the path carries a Windows drive root ("C:\", "D:\\\...")
    /// — i.e. it must NOT be stored in project JSON files.</summary>
    public static bool IsWindowsAbsolutePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 3) return false;
        return char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }

    /// <summary>Convert an ABSOLUTE path (pickers return "C:\\...") into a path stored
    /// in project JSON files (*.tilemap.json / .ing / *.projing) that must never contain
    /// a drive letter. When a project is loaded the path is made relative to the PROJECT
    /// ROOT ("Artifacts\\Maps\\tiles.png"); without a project it falls back to the
    /// classic exe-relative MakeRelative form. Relative input passes through unchanged.</summary>
    public static string MakeProjectRelative(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? "";
        string p = Normalize(path);
        if (!Path.IsPathRooted(p)) return p; // already relative

        string? root = Project.ProjectManager.ProjectRoot;
        if (Project.ProjectManager.IsProjectLoaded && !string.IsNullOrEmpty(root))
        {
            try
            {
                string rel = Path.GetRelativePath(root, p);
                if (!rel.StartsWith("..")) // inside the project → store project-relative
                    return rel.Replace('/', '\\');
                // Outside the project: copy nothing, just keep it exe-relative/portable
                // (same rule as the rest of the engine — no drive letter in saved JSON).
                return MakeRelative(p);
            }
            catch
            {
                // fall through to the exe-relative fallback below
            }
        }
        return MakeRelative(p);
    }

    /// <summary>Resolve a PROJECT data path (stored relative in a tilemap/scene JSON)
    /// to an absolute path for file reads, trying multiple anchors so old saves and
    /// hand-edited files still load: (1) the path as-is when already absolute and
    /// existing; (2) against the project ROOT; (3) against the exe folder; (4) seeded
    /// under the project's Artifacts folder by filename. Returns the input when nothing
    /// exists (caller handles the missing-file case).</summary>
    public static string ResolveProjectAsset(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? "";
        string p = Normalize(path);

        // Already absolute (legacy data): use it when it exists, else keep looking.
        if (Path.IsPathRooted(p) && File.Exists(p)) return p;

        if (Project.ProjectManager.IsProjectLoaded && !string.IsNullOrEmpty(Project.ProjectManager.ProjectRoot))
        {
            string inRoot = Path.Combine(Project.ProjectManager.ProjectRoot, p);
            if (File.Exists(inRoot)) return inRoot;
        }

        string inExe = Path.Combine(AppBaseDir, p);
        if (File.Exists(inExe)) return inExe;

        // Filename-seeded lookup under the project's Artifacts/ tree (old saves that
        // stored a bare "C:\...\tiles.png" with the file inside the project folder).
        string fileName = Path.GetFileName(p);
        if (!string.IsNullOrEmpty(fileName))
        {
            string seeds = Path.Combine(Project.ProjectManager.AssetsDir, fileName);
            if (File.Exists(seeds)) return seeds;
        }
        return p;
    }
}
