using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Config;

/// <summary>JSON-serializable data for recent projects list.</summary>
public class RecentProjectsData
{
    public List<string> RecentProjectPaths { get; set; } = [];
}

/// <summary>Manages the list of recently opened project folders, persisted to a JSON file.</summary>
public static class RecentProjectsManager
{
    private static readonly string FilePath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "recent_projects.json");

    private const int MaxRecentProjects = 10;

    private static List<string>? _cached = null;

    /// <summary>Get the list of recent project paths (most recent first).</summary>
    public static IReadOnlyList<string> GetRecentProjects()
    {
        if (_cached != null)
            return _cached.AsReadOnly();

        try
        {
            if (System.IO.File.Exists(FilePath))
            {
                string json = System.IO.File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<RecentProjectsData>(json);
                if (data?.RecentProjectPaths != null && data.RecentProjectPaths.Count > 0)
                {
                    // Filter out directories that no longer exist
                    data.RecentProjectPaths.RemoveAll(p => !System.IO.Directory.Exists(p));
                    _cached = data.RecentProjectPaths;
                    return _cached.AsReadOnly();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RecentProjects] Load failed: {ex.Message}");
        }

        _cached = [];
        return _cached.AsReadOnly();
    }

    /// <summary>Add a project path to the recent projects list (dedup, max limit).</summary>
    public static void AddRecentProject(string projectPath)
    {
        // Ensure cache is loaded
        GetRecentProjects();

        // Remove duplicate if exists
        _cached!.RemoveAll(p =>
            p.Equals(projectPath, StringComparison.OrdinalIgnoreCase));

        // Insert at front
        _cached.Insert(0, projectPath);

        // Trim to max
        while (_cached.Count > MaxRecentProjects)
            _cached.RemoveAt(_cached.Count - 1);

        Save();
    }

    /// <summary>Remove a specific project from the recent list.</summary>
    public static void RemoveRecentProject(string projectPath)
    {
        GetRecentProjects();
        _cached!.RemoveAll(p =>
            p.Equals(projectPath, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>Clear all recent projects (both in-memory and on disk).</summary>
    public static void ClearRecentProjects()
    {
        _cached = [];
        Save();
        Console.WriteLine("[RecentProjects] Cleared all recent projects");
    }

    /// <summary>Persist the recent projects list to disk.</summary>
    private static void Save()
    {
        try
        {
            var data = new RecentProjectsData { RecentProjectPaths = _cached ?? [] };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            System.IO.File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RecentProjects] Save failed: {ex.Message}");
        }
    }
}
