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

    /// <summary>Normalize a path: '/' → '\' and collapse duplicated separators.
    /// This also cleans up the old double-backslash defaults ("Artifacts\\fonts\\...").
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

    /// <summary>Resolve a possibly-relative path to an absolute path rooted at the exe folder.
    /// Absolute paths are returned as-is; empty strings pass through.</summary>
    public static string Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? "";
        string p = Normalize(path);
        try
        {
            if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
            return Path.GetFullPath(Path.Combine(AppBaseDir, p));
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
}
