using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Config;

/// <summary>JSON-serializable data for recent files list.</summary>
public class RecentFilesData
{
    public List<string> RecentFilePaths { get; set; } = [];
}

/// <summary>Manages the list of recently opened .ing files, persisted to a JSON file.</summary>
public static class RecentFilesManager
{
    private static readonly string FilePath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "recent_files.json");

    private const int MaxRecentFiles = 10;

    private static List<string>? _cached = null;

    /// <summary>Get the list of recent file paths (most recent first).</summary>
    public static IReadOnlyList<string> GetRecentFiles()
    {
        if (_cached != null)
            return _cached.AsReadOnly();

        try
        {
            if (System.IO.File.Exists(FilePath))
            {
                string json = System.IO.File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<RecentFilesData>(json);
                if (data?.RecentFilePaths != null && data.RecentFilePaths.Count > 0)
                {
                    // Filter out files that no longer exist
                    data.RecentFilePaths.RemoveAll(p => !System.IO.File.Exists(p));
                    _cached = data.RecentFilePaths;
                    return _cached.AsReadOnly();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RecentFiles] Load failed: {ex.Message}");
        }

        _cached = [];
        return _cached.AsReadOnly();
    }

    /// <summary>Add a file path to the recent files list (dedup, max limit).</summary>
    public static void AddRecentFile(string filePath)
    {
        // Ensure cache is loaded
        GetRecentFiles();

        // Remove duplicate if exists
        _cached!.RemoveAll(p =>
            p.Equals(filePath, System.StringComparison.OrdinalIgnoreCase));

        // Insert at front
        _cached.Insert(0, filePath);

        // Trim to max
        while (_cached.Count > MaxRecentFiles)
            _cached.RemoveAt(_cached.Count - 1);

        Save();
    }

    /// <summary>Clear all recent files (both in-memory and on disk).</summary>
    public static void ClearRecentFiles()
    {
        _cached = [];
        Save();
        Console.WriteLine("[RecentFiles] Cleared all recent files");
    }

    /// <summary>Persist the recent files list to disk.</summary>
    private static void Save()
    {
        try
        {
            var data = new RecentFilesData { RecentFilePaths = _cached ?? [] };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            System.IO.File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RecentFiles] Save failed: {ex.Message}");
        }
    }
}
