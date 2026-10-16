using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using DarkEngine3D_gl_csharp.Engine.IDE.Panels;
using DarkEngine3D_gl_csharp.Engine.Visual;

// Harness for the Sprite Editor's COLUMN OFFSET contract, run against the REAL engine
// (project reference — the very same SpritePatternExport / SpriteSheet / SpriteGridDetector
// code the editor calls).
//
//   A. JSON contract: a value in the file is honoured; a file WITHOUT the field reports
//      null so the editor PREDICTS it from the image; a changed value is what the next
//      load sees; the loaded value really moves the sliced grid.
//   B. Real files: a copy of a genuine project export — no ColOffset (predict), then the
//      user's edited GRID (offset, then columns/rows/frame size) saved back into that export
//      with every clip kept intact, plus the legacy "<image.png>-sprite-anim.json" name.
//   C. The exact pair the editor is asked about every day: "Cooking area.png" next to
//      "Cooking area-sprite-anim.json" (spaces in the name, a similarly named sibling
//      export beside it). The export is read from the working tree, or from the commit
//      when it is not checked out, so the check never silently disappears.
//   D. The image has NO export yet: the first write CREATES "<image>-sprite-anim.json" next
//      to it (grid + Column Offset + frames, portable image path), an existing file is never
//      overwritten, and no file appears next to an image that is not there.
//
// Exit 0 = all checks pass, 1 = at least one failed, 2 = harness could not run.

namespace DarkEngine3D_gl_csharp.Tools.SpriteSheetDataJsonTest;

public static class App
{
    private static int _fails;

    public static int Main()
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        string tempRoot = Path.Combine(Path.GetTempPath(), $"coloffset-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            JsonContract(Path.Combine(tempRoot, "json"), opts);
            string? repoRoot = FindRepoRoot();
            RealExportFiles(tempRoot, repoRoot);
            RealImageAndExportPair(tempRoot, repoRoot);
            CreateExportWhenMissing(tempRoot, repoRoot);
        }
        catch (Exception ex)
        {
            Check($"harness crashed: {ex}", false);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        Console.WriteLine(_fails == 0
            ? "ALL SPRITE EXPORT CASES PASS (column offset + grid save + import data + creation)"
            : $"{_fails} SPRITE EXPORT FAILURES");
        return _fails == 0 ? 0 : 1;
    }

    // ── A. the JSON contract ──────────────────────────────────────────────────────────

    private static void JsonContract(string dir, JsonSerializerOptions opts)
    {
        Directory.CreateDirectory(dir);
        Console.WriteLine("== A. Column Offset through the sheet JSON (real DTOs) ==\n");

        // Value PRESENT in the file: loaded verbatim, twice.
        var sheet = new SpriteSheetData
        {
            Name = "Banner",
            ImagePath = "Artifacts/images/banner.png",
            ImageWidth = 560,
            ImageHeight = 128,
            Columns = 4,
            Rows = 2,
            FrameWidth = 64,
            FrameHeight = 64,
            PaddingX = 16,
            ColOffset = 3
        };
        string path = Path.Combine(dir, "Banner-sprite-anim.json");
        File.WriteAllText(path,
            JsonSerializer.Serialize(new SpriteSheetsSaveData { Sheets = [sheet] }, opts));

        string raw = File.ReadAllText(path);
        Check("file carries the written value", raw.Contains("\"ColOffset\": 3"));

        var loaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(raw);
        Check("value loaded from the export", loaded?.Sheets?[0].ColOffset == 3);

        var again = JsonSerializer.Deserialize<SpriteSheetsSaveData>(JsonSerializer.Serialize(loaded, opts));
        Check("value survives a second round-trip", again?.Sheets?[0].ColOffset == 3);

        // Value ABSENT (every export written before the field existed).
        string legacyJson = JsonSerializer.Serialize(
            new SpriteSheetsSaveData { Sheets = [new SpriteSheetData { Name = "Legacy" }] }, opts);
        Check("absent value is not written to the file", !legacyJson.Contains("ColOffset"));
        var legacyLoaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(legacyJson);
        Check("absent value stays null (the editor predicts it)", legacyLoaded?.Sheets?[0].ColOffset == null);
        Check("FromData turns an absent value into 0",
            SpriteSheet.FromData(legacyLoaded!.Sheets[0]).ColOffset == 0);

        // The loaded value really moves the sliced grid.
        var live = SpriteSheet.FromData(loaded!.Sheets![0]);
        Check("FromData keeps the file's value", live.ColOffset == 3);
        live.BakeUniformFrames();
        int pitch = live.FrameWidth + live.PaddingX;
        Check("frame 0 starts one cell-width per Column Offset in",
            live.CustomFrames![0].X == live.OffsetX + 3 * pitch);
        Check("frame 1 follows one pitch later", live.CustomFrames[1].X == live.OffsetX + 4 * pitch);
        Check("UVs use the same shift",
            Math.Abs(live.GetFrameUV(0).uvMin.X * live.ImageWidth - (live.OffsetX + 3 * pitch)) < 0.001f);

        // A CHANGED value is what the next load sees.
        var stored = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(path));
        stored!.Sheets[0].ColOffset = 5;
        var baked = SpriteSheet.FromData(stored.Sheets[0]);
        baked.ColOffset = 5;
        baked.BakeUniformFrames();
        stored.Sheets[0].CustomFrames = baked.ToData().CustomFrames;
        File.WriteAllText(path, JsonSerializer.Serialize(stored, opts));

        var reloaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(path));
        var reloadedLive = SpriteSheet.FromData(reloaded!.Sheets![0]);
        Check("changed value is saved to the file", reloaded.Sheets[0].ColOffset == 5);
        Check("saved frames match the changed value",
            reloadedLive.CustomFrames![0].X == reloadedLive.OffsetX + 5 * pitch);
        Console.WriteLine();
    }

    // ── B. the real product code on a real project export ─────────────────────────────

    private static void RealExportFiles(string tempRoot, string? repoRoot)
    {
        if (repoRoot == null)
        {
            Console.WriteLine("== B. real export files: SKIPPED (repo root not found) ==\n");
            return;
        }

        Console.WriteLine("== B. real export files (SpritePatternExport, the editor's own code) ==\n");
        // The export ships with the project, so a checkout without it (or one where the file
        // was removed) still tests against the committed data instead of skipping.
        const string fxRel = "Dark Projects/Sample game 2D Forest Adventures/Artifacts/images/FX";
        byte[]? exportBytes = ReadProductFile(repoRoot, $"{fxRel}/fireball-sprite-anim.json");
        if (exportBytes == null)
        {
            Console.WriteLine("     SKIPPED: the fireball export is neither on disk nor in the commit\n");
            return;
        }
        byte[] pngBytes = File.ReadAllBytes(Path.Combine(repoRoot, "Dark Projects", "Sample game 2D Forest Adventures",
            "Artifacts", "images", "FX", "fireball.png"));

        // Work on COPIES — the real project files are never written by this harness.
        string dir = Path.Combine(tempRoot, "real");
        Directory.CreateDirectory(dir);
        string tempPng = Path.Combine(dir, "fireball.png");
        string tempExport = Path.Combine(dir, "fireball-sprite-anim.json");
        File.WriteAllBytes(tempPng, pngBytes);
        File.WriteAllBytes(tempExport, exportBytes);

        // The copy is made to look like a file written BEFORE the field existed (the shipped
        // one may already carry ColOffset, because the editor now writes it on every grid
        // edit). The pre-field path is then tested on real data whatever the tree holds.
        StripColOffset(tempExport);

        // 1) The export predates the field -> null, so the editor PREDICTS from the pixels.
        Check("pre-field export reports no value (predict path)", SpritePatternExport.TryLoadColOffset(tempPng) == null);
        int predicted = SpritePatternExport.PredictColOffset(tempPng);
        Console.WriteLine($"     predicted from the image: {predicted}");
        Check("prediction is a sane non-negative column count", predicted >= 0);
        var pairFile = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(tempExport))!;
        Check("the export is returned with its own entry and clips",
            SpritePatternExport.TryLoadExport(tempPng, out var loadedData, out var loadedEntry)
            && loadedEntry!.Name == pairFile.Sheets[0].Name
            && loadedEntry.CustomFrames!.Count == pairFile.Sheets[0].CustomFrames!.Count
            && loadedData!.AnimationClips.Count == pairFile.AnimationClips.Count);

        // 2) The user changes Column Offset -> written straight into the export.
        var before = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(tempExport))!;
        int clipsBefore = before.AnimationClips.Count;
        var live = SpriteSheet.FromData(before.Sheets[0]);
        live.ColOffset = 2;
        Check("save reports one export updated", SpritePatternExport.SaveGridToExport(tempPng, live) == 1);

        var after = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(tempExport))!;
        Check("export now carries the changed value", after.Sheets[0].ColOffset == 2);
        Check("reload sees the changed value", SpritePatternExport.TryLoadColOffset(tempPng) == 2);
        Check("every animation clip survived the rewrite", after.AnimationClips.Count == clipsBefore);
        Check("the export's frames were re-baked onto the new grid",
            after.Sheets[0].CustomFrames![0].X
            == after.Sheets[0].OffsetX + 2 * (after.Sheets[0].FrameWidth + after.Sheets[0].PaddingX));
        Check("the source image path in the export is untouched",
            after.Sheets[0].ImagePath == before.Sheets[0].ImagePath);

        // 2b) Every OTHER grid value the user edits is written the same way — the export stops
        //     describing the sheet only when the file follows the edit.
        live.Columns = 2;
        live.Rows = 2;
        live.FrameWidth = 32;
        Check("a grid edit reports the export updated again",
            SpritePatternExport.SaveGridToExport(tempPng, live) == 1);
        var gridAfter = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(tempExport))!;
        Check("Columns / Rows / Frame Width are saved to the export",
            gridAfter.Sheets[0].Columns == 2 && gridAfter.Sheets[0].Rows == 2
            && gridAfter.Sheets[0].FrameWidth == 32);
        Check("the export's frames follow the new grid (4 cells, last one two pitches in)",
            gridAfter.Sheets[0].CustomFrames!.Count == 4
            && gridAfter.Sheets[0].CustomFrames![3].X == gridAfter.Sheets[0].OffsetX
                + (2 + 1) * (32 + gridAfter.Sheets[0].PaddingX));
        Check("every animation clip survived the grid edit too",
            gridAfter.AnimationClips.Count == clipsBefore);
        Check("a save with nothing changed writes nothing",
            SpritePatternExport.SaveGridToExport(tempPng, live) == 0);

        // 3) LEGACY file name "<image.png>-sprite-anim.json" (written by older builds): the
        //    value must still be found through the folder scan + image-path match.
        string legacyDir = Path.Combine(tempRoot, "legacy");
        Directory.CreateDirectory(legacyDir);
        string legacyPng = Path.Combine(legacyDir, "Old Sheet.png");
        File.WriteAllBytes(legacyPng, pngBytes);
        File.WriteAllText(Path.Combine(legacyDir, "Old Sheet.png-sprite-anim.json"),
            JsonSerializer.Serialize(new SpriteSheetsSaveData
            {
                Sheets = [new SpriteSheetData
                {
                    Name = "Old Sheet",
                    ImagePath = legacyPng,
                    ImageWidth = 256,
                    ImageHeight = 32,
                    Columns = 1,
                    Rows = 1,
                    FrameWidth = 64,
                    FrameHeight = 64,
                    ColOffset = 7
                }]
            }, new JsonSerializerOptions { WriteIndented = true }));
        Check("value found in a legacy \"<image.png>-sprite-anim.json\"",
            SpritePatternExport.TryLoadColOffset(legacyPng) == 7);
        Console.WriteLine();
    }

    // ── C. the everyday pair: "<image>.png" + "<image>-sprite-anim.json" ────────────────

    /// <summary>The case the editor is asked about constantly: an image whose export sits
    /// beside it under the SAME base name (spaces and all), with a similarly named sibling
    /// export in the folder that must never be touched.</summary>
    private static void RealImageAndExportPair(string tempRoot, string? repoRoot)
    {
        if (repoRoot == null)
        {
            Console.WriteLine("== C. real image + export pair: SKIPPED (repo root not found) ==\n");
            return;
        }

        const string baseName = "Cooking area";
        const string fxRel = "Dark Projects/Sample game 2D Forest Adventures/Artifacts/images/FX";
        string pngOnDisk = Path.Combine(repoRoot, "Dark Projects", "Sample game 2D Forest Adventures",
            "Artifacts", "images", "FX", baseName + ".png");
        byte[]? exportBytes = ReadProductFile(repoRoot, $"{fxRel}/{baseName}-sprite-anim.json");
        byte[]? siblingBytes = ReadProductFile(repoRoot, $"{fxRel}/{baseName} - Off-sprite-anim.json");
        if (!File.Exists(pngOnDisk) || exportBytes == null || siblingBytes == null)
        {
            Console.WriteLine("== C. real image + export pair: SKIPPED (source files unavailable) ==\n");
            return;
        }

        Console.WriteLine($"== C. real pair '{baseName}.png' + '{baseName}-sprite-anim.json' ==\n");
        string dir = Path.Combine(tempRoot, "pair");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, baseName + ".png");
        string export = Path.Combine(dir, baseName + "-sprite-anim.json");
        string sibling = Path.Combine(dir, baseName + " - Off-sprite-anim.json");
        File.Copy(pngOnDisk, png);
        File.WriteAllBytes(export, exportBytes);
        File.WriteAllBytes(sibling, siblingBytes);

        // The name the editor looks for is the image's own base name + the export suffix.
        Check("export path is built from the image's base name (spaces kept)",
            string.Equals(Path.GetFileName(SpritePatternExport.ExportPathFor(png)),
                baseName + "-sprite-anim.json", StringComparison.Ordinal));

        // A pre-field export reports NULL so the editor PREDICTS from the pixels.
        var onFile = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(export))!;
        int? fileValue = onFile.Sheets[0].ColOffset;
        Check("the export is found and read through its base-name pairing",
            SpritePatternExport.TryLoadColOffset(png) == fileValue);
        int predicted = SpritePatternExport.PredictColOffset(png);
        Console.WriteLine($"     predicted from '{baseName}.png': {predicted}"
            + (fileValue == null ? " (export predates the field)" : $" (export carries {fileValue})"));
        Check("prediction is a sane non-negative column count", predicted >= 0);
        Check("the whole export (entry + clips) is handed back to the import",
            SpritePatternExport.TryLoadExport(png, out var importData, out var importEntry)
            && importEntry!.Name == onFile.Sheets[0].Name
            && importEntry.CustomFrames!.Count == onFile.Sheets[0].CustomFrames!.Count
            && importData!.AnimationClips.Count == onFile.AnimationClips.Count);
        // What the import builds from that entry (CreateSheetForImport) is the file's own
        // sheet: same grid, same master box, same frame regions.
        var importedSheet = SpriteSheet.FromData(importEntry!);
        Check("a sheet built from the export carries the file's grid and frames verbatim",
            importedSheet.Columns == onFile.Sheets[0].Columns
            && importedSheet.Rows == onFile.Sheets[0].Rows
            && importedSheet.FrameWidth == onFile.Sheets[0].FrameWidth
            && importedSheet.FrameHeight == onFile.Sheets[0].FrameHeight
            && importedSheet.MasterWidth == onFile.Sheets[0].MasterWidth
            && importedSheet.CustomFrames!.Count == onFile.Sheets[0].CustomFrames!.Count
            && importedSheet.CustomFrames![0].X == onFile.Sheets[0].CustomFrames![0].X
            && importedSheet.CustomFrames![0].Height == onFile.Sheets[0].CustomFrames![0].Height);

        // The user changes the value -> written straight into THAT export, sibling untouched.
        byte[] siblingBefore = File.ReadAllBytes(sibling);
        int clipsBefore = onFile.AnimationClips.Count;
        var framesBefore = onFile.Sheets[0].CustomFrames!;
        var live = SpriteSheet.FromData(onFile.Sheets[0]);
        live.ColOffset = predicted + 1;
        Check("save reports one export updated",
            SpritePatternExport.SaveGridToExport(png, live) == 1);
        // …and a later grid edit lands in the same file.
        live.Columns = 3;
        Check("a grid edit is saved to the same '<image>-sprite-anim.json'",
            SpritePatternExport.SaveGridToExport(png, live) == 1);

        var after = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(export))!;
        int pitch = after.Sheets[0].FrameWidth + after.Sheets[0].PaddingX;
        Check("the change is saved in '<image>-sprite-anim.json'",
            after.Sheets[0].ColOffset == predicted + 1);
        Check("the next load sees the saved value",
            SpritePatternExport.TryLoadColOffset(png) == predicted + 1);
        Check("the grid edit is in the file", after.Sheets[0].Columns == 3);
        Check("the similarly named sibling export was NOT rewritten",
            siblingBefore.SequenceEqual(File.ReadAllBytes(sibling)));
        Check("every animation clip of the pair survived",
            after.AnimationClips.Count == clipsBefore);
        Check("the sheet still points at its own image",
            after.Sheets[0].ImagePath == onFile.Sheets[0].ImagePath);
        Check("the frames were moved onto the new grid, none lost",
            after.Sheets[0].CustomFrames!.Count == 3
            && after.Sheets[0].CustomFrames![0].X == after.Sheets[0].OffsetX + (predicted + 1) * pitch);
        Check("per-frame data is kept (name, anchor, hitbox, tags)",
            after.Sheets[0].CustomFrames![0].Name == framesBefore[0].Name
            && after.Sheets[0].CustomFrames![0].AnchorX == framesBefore[0].AnchorX
            && after.Sheets[0].CustomFrames![0].AnchorY == framesBefore[0].AnchorY
            && after.Sheets[0].CustomFrames![0].HitboxW == framesBefore[0].HitboxW
            && (after.Sheets[0].CustomFrames![0].Tags?.Count ?? 0) == (framesBefore[0].Tags?.Count ?? 0)
            && after.Sheets[0].CustomFrames!.All(f => f.Width == after.Sheets[0].FrameWidth
                && f.Height == after.Sheets[0].FrameHeight));

        // The same pairing is also recognised when the export names the image by PATH
        // (the form the editor writes for a project-relative "Artifacts\\..." image).
        string byPathDir = Path.Combine(tempRoot, "pair-by-path");
        Directory.CreateDirectory(byPathDir);
        string byPathPng = Path.Combine(byPathDir, baseName + ".png");
        string byPathExport = Path.Combine(byPathDir, baseName + "-sprite-anim.json");
        File.Copy(pngOnDisk, byPathPng);
        var byPath = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(export))!;
        byPath.Sheets[0].ImagePath = byPathPng;
        File.WriteAllText(byPathExport,
            JsonSerializer.Serialize(byPath, new JsonSerializerOptions { WriteIndented = true }));
        Check("an export that names the image by path is matched too",
            SpritePatternExport.TryLoadColOffset(byPathPng) == predicted + 1);
        Console.WriteLine();
    }

    // ── D. the image has no export: one is created next to it ───────────────────────────

    /// <summary>The "there is no `<image>-sprite-anim.json`, so make one" case, on the real
    /// Cooking area image: the export must appear next to the PNG with exactly the grid and
    /// Column Offset the sheet holds, must be readable straight back, and must never replace a
    /// file that is already there.</summary>
    private static void CreateExportWhenMissing(string tempRoot, string? repoRoot)
    {
        if (repoRoot == null)
        {
            Console.WriteLine("== D. creating a missing export: SKIPPED (repo root not found) ==\n");
            return;
        }

        string pngOnDisk = Path.Combine(repoRoot, "Dark Projects", "Sample game 2D Forest Adventures",
            "Artifacts", "images", "FX", "Cooking area.png");
        if (!File.Exists(pngOnDisk))
        {
            Console.WriteLine("== D. creating a missing export: SKIPPED (source image unavailable) ==\n");
            return;
        }

        Console.WriteLine("== D. no export yet -> '<image>-sprite-anim.json' is created ==\n");
        string dir = Path.Combine(tempRoot, "create");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, "Cooking area.png");
        string export = Path.Combine(dir, "Cooking area-sprite-anim.json");
        File.Copy(pngOnDisk, png);
        Check("nothing is there to start with", !File.Exists(export));

        // What the import path hands over: a sheet auto-fitted from the image, its Column Offset
        // predicted, its image path stored the project-relative way the editor stores it.
        var sheet = SpriteSheet.FromData(new SpriteSheetData
        {
            Name = "Cooking area",
            ImagePath = Tilemap2D.MakeDataPathRelative(png),
            ImageWidth = 768,
            ImageHeight = 64,
            Columns = 12,
            Rows = 1,
            FrameWidth = 64,
            FrameHeight = 64,
            ColOffset = SpritePatternExport.PredictColOffset(png)
        });
        sheet.BakeUniformFrames();

        Check("a write creates the export next to the image",
            SpritePatternExport.SaveGridToExport(sheet.ImagePath, sheet) == 1 && File.Exists(export));
        var created = JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(export))!;
        Check("the created file carries the sheet's grid and Column Offset",
            created.Sheets.Count == 1 && created.Sheets[0].Name == "Cooking area"
            && created.Sheets[0].Columns == 12 && created.Sheets[0].Rows == 1
            && created.Sheets[0].FrameWidth == 64 && created.Sheets[0].FrameHeight == 64
            && created.Sheets[0].ColOffset == sheet.ColOffset);
        Check("the created file carries the sliced frames",
            created.Sheets[0].CustomFrames!.Count == 12
            && created.Sheets[0].CustomFrames![0].X
                == created.Sheets[0].OffsetX + sheet.ColOffset * 64);
        Check("its image path stays portable (no drive letter)",
            !created.Sheets[0].ImagePath.Contains(':'));
        Check("the new value is read straight back from the created file",
            SpritePatternExport.TryLoadColOffset(png) == sheet.ColOffset);
        Check("the export is handed to the import like any other",
            SpritePatternExport.TryLoadExport(png, out _, out var createdEntry)
            && createdEntry!.Columns == 12);
        Check("a second write with nothing changed writes nothing",
            SpritePatternExport.SaveGridToExport(sheet.ImagePath, sheet) == 0);

        // An existing file is NEVER overwritten by the create path.
        string otherDir = Path.Combine(tempRoot, "create-existing");
        Directory.CreateDirectory(otherDir);
        string otherPng = Path.Combine(otherDir, "Cooking area.png");
        string otherExport = Path.Combine(otherDir, "Cooking area-sprite-anim.json");
        File.Copy(pngOnDisk, otherPng);
        File.WriteAllText(otherExport, JsonSerializer.Serialize(new SpriteSheetsSaveData
        {
            Sheets = [new SpriteSheetData { Name = "Somebody else", ImagePath = "Artifacts/images/other.png", ColOffset = 5 }]
        }, new JsonSerializerOptions { WriteIndented = true }));
        byte[] existingBytes = File.ReadAllBytes(otherExport);
        Check("an existing file is left exactly as it is",
            SpritePatternExport.CreateExportIfMissing(Tilemap2D.MakeDataPathRelative(otherPng), sheet) == 0
            && existingBytes.SequenceEqual(File.ReadAllBytes(otherExport)));

        // No image on disk -> nothing to sit next to.
        string ghostDir = Path.Combine(tempRoot, "create-ghost");
        Directory.CreateDirectory(ghostDir);
        Check("no export appears for an image that is not there",
            SpritePatternExport.CreateExportIfMissing(Path.Combine(ghostDir, "Ghost.png"), sheet) == 0
            && !File.Exists(Path.Combine(ghostDir, "Ghost-sprite-anim.json")));
        Console.WriteLine();
    }

    /// <summary>Remove the sheet-level `ColOffset` property from an export COPY, so it is exactly
    /// the kind of file the editor wrote before the field existed — the input the
    /// predict-from-pixels path is written against.</summary>
    private static void StripColOffset(string path)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        if (root?["Sheets"] is System.Text.Json.Nodes.JsonArray sheets)
            foreach (var entry in sheets)
                entry?.AsObject().Remove("ColOffset");
        File.WriteAllText(path, root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}");
    }

    /// <summary>Read a file that belongs to the product: the working-tree copy when it is
    /// checked out, otherwise the committed copy. An export deleted from elsewhere in the
    /// tree is still the real data this contract is written against, so the harness keeps
    /// testing it instead of quietly skipping.</summary>
    private static byte[]? ReadProductFile(string repoRoot, string relPath)
    {
        string onDisk = Path.Combine(repoRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(onDisk)) return File.ReadAllBytes(onDisk);
        try
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = repoRoot, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add($"HEAD:{relPath}");
            using var process = Process.Start(psi);
            if (process == null) return null;
            using var buffer = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(buffer);
            process.WaitForExit();
            return process.ExitCode == 0 && buffer.Length > 0 ? buffer.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Walk up from the working directory to the repo root (the folder holding the
    /// solution) so the harness can reach the sample project files.</summary>
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DarkEngine3D_gl_csharp.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static void Check(string what, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {what}");
        if (!ok) _fails++;
    }
}
