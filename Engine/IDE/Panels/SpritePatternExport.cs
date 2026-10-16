using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Visual;
using StbImageSharp;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// COLUMN OFFSET I/O for sprite sheets, in one place so both the Sprite Editor and a
/// headless harness call the very same code. It answers the three questions the editor
/// asks about a sheet's Column Offset (how many whole grid cells every row is shifted
/// right before the first sliced frame):
///
///   1. IS IT IN THE FILE?  <see cref="TryLoadExport"/> reads the sheet's
///      `&lt;image name&gt;-sprite-anim.json` export — the sheet entry AND the clips in it.
///      Its Column Offset NULL means the export predates the field (every file written
///      before it existed), which is the caller's signal to predict instead of silently
///      using 0.
///   2. WHAT SHOULD IT BE?  <see cref="PredictColOffset"/> reads the source image's own
///      pixels (SpriteGridDetector) and reports how many whole cells sit empty in front of
///      the first inked column.
///   3. THE USER CHANGED THE GRID. <see cref="SaveGridToExport"/> writes the edited grid back
///      into the export — including a re-bake of its uniform frames, so the file on disk
///      slices exactly like the edited sheet.
///
/// Pure file/image work: no ImGui, no GL, no panel state.
/// </summary>
public static class SpritePatternExport
{
    /// <summary>Export file that belongs to a source image: `&lt;image name&gt;-sprite-anim.json`
    /// next to the image (the name Save As Pattern writes).</summary>
    public static string ExportPathFor(string imagePath)
    {
        string resolved = PathHelpers.ResolveProjectAsset(imagePath);
        string folder = Path.GetDirectoryName(resolved) ?? "";
        if (folder.Length == 0) return "";
        return Path.Combine(folder, Path.GetFileNameWithoutExtension(resolved) + "-sprite-anim.json");
    }

    /// <summary>The sheet entry describing this image inside its `*-sprite-anim.json` export,
    /// together with the whole export (so a caller can also take the clips it carries).
    /// False when the image has no export beside it, or the export is unreadable. The exact
    /// `&lt;image&gt;-sprite-anim.json` name is tried first; legacy exports named
    /// `&lt;image.png&gt;-sprite-anim.json` are found by scanning the folder and matching the
    /// entry's image path.</summary>
    public static bool TryLoadExport(string imagePath, out SpriteSheetsSaveData? data,
        out SpriteSheetData? entry)
    {
        data = null;
        entry = null;
        foreach (string file in CandidateExports(imagePath))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(file));
                var match = MatchEntry(parsed, imagePath);
                if (match == null) continue;
                data = parsed;
                entry = match;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpritePatternExport] Could not read {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return false;
    }

    /// <summary>The Column Offset stored in the sheet's `*-sprite-anim.json` export, or NULL
    /// when there is no export / the export predates the field (caller then predicts it from
    /// the image).</summary>
    public static int? TryLoadColOffset(string imagePath)
        => TryLoadExport(imagePath, out _, out var entry) ? entry!.ColOffset : null;

    /// <summary>PREDICT a sheet's Column Offset from its source image: how many whole cell
    /// widths sit empty in front of the first inked column (0 when the image is missing or
    /// carries no grid for the detector to measure).</summary>
    public static int PredictColOffset(string imagePath)
    {
        try
        {
            string resolved = PathHelpers.ResolveProjectAsset(imagePath);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved)) return 0;
            using var stream = File.OpenRead(resolved);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            var fit = SpriteGridDetector.Detect(image.Data, image.Width, image.Height);
            return Math.Max(0, fit?.ColOffset ?? 0);
        }
        catch
        {
            return 0; // unreadable image: the neutral value, never a hard failure
        }
    }

    /// <summary>Write the sheet's edited GRID back into its export(s): Name, Columns, Rows,
    /// Frame Width/Height, Padding X/Y, Offset X/Y and Column Offset. Only entries that
    /// describe THIS image are touched, and only when something differs; when a grid value
    /// changed the entry's uniform frames are re-baked so the file slices exactly like the
    /// edited sheet (a rename alone never disturbs the frames). Returns the number of export
    /// files updated (0 = no export exists — the canonical sprites.sheets.json is the Save
    /// button's job).</summary>
    public static int SaveGridToExport(string imagePath, SpriteSheet sheet)
    {
        int updated = 0;
        bool describedByAnExport = false;
        foreach (string file in CandidateExports(imagePath))
        {
            try
            {
                var data = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(file));
                var target = MatchEntry(data, imagePath);
                if (data == null || target == null) continue;
                describedByAnExport = true;

                bool gridDiffers = target.Columns != sheet.Columns
                    || target.Rows != sheet.Rows
                    || target.FrameWidth != sheet.FrameWidth
                    || target.FrameHeight != sheet.FrameHeight
                    || target.PaddingX != sheet.PaddingX
                    || target.PaddingY != sheet.PaddingY
                    || target.OffsetX != sheet.OffsetX
                    || target.OffsetY != sheet.OffsetY
                    || target.ColOffset != sheet.ColOffset;
                if (!gridDiffers && string.Equals(target.Name, sheet.Name, StringComparison.Ordinal))
                    continue;

                target.Name = sheet.Name;
                target.Columns = sheet.Columns;
                target.Rows = sheet.Rows;
                target.FrameWidth = sheet.FrameWidth;
                target.FrameHeight = sheet.FrameHeight;
                target.PaddingX = sheet.PaddingX;
                target.PaddingY = sheet.PaddingY;
                target.OffsetX = sheet.OffsetX;
                target.OffsetY = sheet.OffsetY;
                target.ColOffset = sheet.ColOffset;
                if (gridDiffers)
                {
                    var baked = SpriteSheet.FromData(target);
                    baked.BakeUniformFrames();
                    target.CustomFrames = baked.ToData().CustomFrames;
                }

                File.WriteAllText(file, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                // The export changed on disk: re-stamp its sidecar so the next load is a binary hit.
                BinaryObjectCache.TryWrite(file, data);
                updated++;
                Console.WriteLine($"[SpritePatternExport] Grid saved to {Path.GetFileName(file)} (sheet '{sheet.Name}': {sheet.Columns}x{sheet.Rows} @ {sheet.FrameWidth}x{sheet.FrameHeight}px, pad {sheet.PaddingX},{sheet.PaddingY}, offset {sheet.OffsetX},{sheet.OffsetY}, col offset {sheet.ColOffset})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpritePatternExport] Could not save the grid to {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        // Nothing on disk describes this image yet: CREATE the export that belongs to it, so the
        // grid the user just edited is remembered next to the image instead of only in memory.
        if (!describedByAnExport) updated += CreateExportIfMissing(imagePath, sheet);
        return updated;
    }

    /// <summary>Write the sheet's own `&lt;image&gt;-sprite-anim.json` NEXT TO ITS IMAGE when that
    /// file does not exist yet — the "there is no export, so make one" case. The file is written
    /// in the same shape Save As Pattern writes (one sheet entry, no clips: an export is a bake
    /// input), its image path is stored project-relative so the JSON never carries a drive
    /// letter, and an EXISTING file is never overwritten. Nothing is created next to an image
    /// that is not there. Returns 1 when a file was created.</summary>
    public static int CreateExportIfMissing(string imagePath, SpriteSheet sheet)
    {
        string resolved = PathHelpers.ResolveProjectAsset(imagePath);
        if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved)) return 0;

        string path = ExportPathFor(imagePath);
        if (path.Length == 0) return 0;
        if (File.Exists(path))
        {
            // Already there: its entry — and any value in it — belongs to the user. Never reset.
            return 0;
        }

        try
        {
            var entry = sheet.ToData();
            entry.ImagePath = PathHelpers.MakeProjectRelative(sheet.ImagePath);
            var data = new SpriteSheetsSaveData { Sheets = [entry] };
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            BinaryObjectCache.TryWrite(path, data);
            Console.WriteLine($"[SpritePatternExport] Created {Path.GetFileName(path)} (sheet '{sheet.Name}': {sheet.Columns}x{sheet.Rows} @ {sheet.FrameWidth}x{sheet.FrameHeight}px, pad {sheet.PaddingX},{sheet.PaddingY}, offset {sheet.OffsetX},{sheet.OffsetY}, col offset {sheet.ColOffset})");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpritePatternExport] Could not create {Path.GetFileName(path)}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Export files that could belong to this image: the exact
    /// `&lt;image name&gt;-sprite-anim.json` first, then every sibling export (legacy names
    /// such as `&lt;image.png&gt;-sprite-anim.json`).</summary>
    private static List<string> CandidateExports(string imagePath)
    {
        var files = new List<string>();
        try
        {
            string resolved = PathHelpers.ResolveProjectAsset(imagePath);
            string folder = Path.GetDirectoryName(resolved) ?? "";
            if (folder.Length == 0 || !Directory.Exists(folder)) return files;

            string primary = Path.Combine(folder, Path.GetFileNameWithoutExtension(resolved) + "-sprite-anim.json");
            if (File.Exists(primary)) files.Add(primary);
            foreach (string f in Directory.EnumerateFiles(folder, "*-sprite-anim.json", SearchOption.TopDirectoryOnly))
                if (!files.Contains(f, StringComparer.OrdinalIgnoreCase)) files.Add(f);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpritePatternExport] Could not list exports for {Path.GetFileName(imagePath)}: {ex.Message}");
        }
        return files;
    }

    /// <summary>The export's sheet entry describing this image: resolved image path first
    /// (exact), then the bare `&lt;image name&gt;` display name.</summary>
    private static SpriteSheetData? MatchEntry(SpriteSheetsSaveData? data, string imagePath)
    {
        if (data?.Sheets == null || data.Sheets.Count == 0) return null;
        string resolved = PathHelpers.ResolveProjectAsset(imagePath);
        string name = Path.GetFileNameWithoutExtension(resolved);

        foreach (var sd in data.Sheets)
        {
            if (sd == null) continue;
            try
            {
                if (PathHelpers.ResolveProjectAsset(sd.ImagePath).Equals(resolved, StringComparison.OrdinalIgnoreCase))
                    return sd;
            }
            catch { }
        }
        foreach (var sd in data.Sheets)
            if (sd != null && string.Equals(sd.Name, name, StringComparison.OrdinalIgnoreCase))
                return sd;
        return null;
    }
}
