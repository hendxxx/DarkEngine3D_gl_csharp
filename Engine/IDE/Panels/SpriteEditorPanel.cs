using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using StbImageSharp;
using System.Numerics;
using System.IO;
using System.Text.Json;
using System.Collections.Concurrent;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Sprite Editor panel for 2D sidescroller game.
/// Handles sprite sheet slicing, frame preview, animation editing, and sprite properties.
/// </summary>
public class SpriteEditorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;
    private readonly ImGuiFileDialog _importDialog = new();
    private readonly ImGuiFileDialog _loadSheetsDialog = new();
    private readonly ImGuiFileDialog _saveAsJsonDialog = new();

    // ── Save As Pattern (auto-clip per row) ──
    /// <summary>Default free-text action list for Save As Pattern — one action per
    /// row, '|' as separator. Also the fallback when the field is cleared.</summary>
    public const string DefaultPatternActions = "idle|walk|run|jump start|jump end|attack|dead";
    /// <summary>Standard clip names assigned to rows IN ORDER; rows past the action
    /// list are NOT saved (the free text defines how many actions are written).</summary>
    private static readonly string[] PatternClipNames =
        ["idle", "walk", "run", "jump start", "jump end", "attack", "dead"];
    /// <summary>Actions that play ONCE (matched by action name, case-insensitive);
    /// every other action loops (e.g. custom "effect" loops).</summary>
    private static readonly string[] PatternOnceActions =
        ["jump start", "jump end", "attack", "dead"];
    /// <summary>Image extensions scanned when batch-generating pattern clips.</summary>
    private static readonly string[] PatternImageExts =
        [".png", ".jpg", ".jpeg", ".bmp", ".tga"];
    /// <summary>Clip names currently merged from *-sprite-anim.json exports — a
    /// refresh removes this set before re-merging, so Save As Pattern re-runs update
    /// clips in place instead of duplicating them.</summary>
    /// <summary>Clip instances currently merged from *-sprite-anim.json exports — a
    /// refresh removes this set before re-merging, so Save As Pattern re-runs update
    /// clips in place instead of duplicating them. Tracked BY INSTANCE (not name):
    /// a manually created clip with the same name must survive the refresh, and
    /// snapshot-based undo rebuilds instances, which would orphan name tracking.</summary>
    private readonly HashSet<AnimationClip2D> _autoLoadedPatternClips = new();
    /// <summary>Free-text action list for Save As Pattern — names per row separated
    /// by '|' (e.g. "effect" for a single-row sheet). Persisted in the save file.</summary>
    private string _patternActions = DefaultPatternActions;

    // ── Sprite sheets ──
    /// <summary>Temporary source sheets used only for slicing/preview; never written
    /// into the project's canonical sheets file.</summary>
    public List<SpriteSheet> SpriteSheets = new();
    /// <summary>Persisted animation-baking atlases used by the runtime registry.</summary>
    private readonly List<SpriteSheet> _bakedAtlasSheets = new();

    /// <summary>Multi-selected sheet indexes (Sprite Sheets list). ALWAYS contains
    /// `_selectedSheetIdx` (the primary selection drives Sheet Settings + the preview);
    /// extra entries extend it: Ctrl+Click toggles, Shift+Click adds the range,
    /// a plain click (or re-click on the primary) clears it. Enforced by the clamps in
    /// OnProjectChanged / LoadSheetsFromFile / Delete Sheet / undo ApplyStateSnapshot —
    /// NEVER cleared blindly there (that would flash the set empty every load) but
    /// rebuilt from the surviving indexes.</summary>
    private readonly HashSet<int> _multiSelectedSheets = new();

    /// <summary>True when a Shift+Click extended the previous range (never shrink a
    /// range) — a plain click runs AFTER it on the same list and must not drag the old
    /// range into the new one.</summary>
    private bool _sheetRangeAnchored;
    private int _selectedSheetIdx = -1;
    private SpriteSheet? SelectedSheet => _selectedSheetIdx >= 0 && _selectedSheetIdx < SpriteSheets.Count
        ? SpriteSheets[_selectedSheetIdx] : null;

    // ── Edit state ──
    private string _newSheetName = "NewSheet";
    private int _editCols = 8;
    private int _editRows = 8;
    private int _editFrameW = 64;
    private int _editFrameH = 64;
    private int _editPadX, _editPadY;
    private int _editOffsetX, _editOffsetY;

    // ── Frame selection ──
    private int _selectedFrameIdx = -1;
    private SpriteFrame? SelectedFrame => _selectedFrameIdx >= 0 && SelectedSheet?.CustomFrames != null && _selectedFrameIdx < SelectedSheet.CustomFrames.Count
        ? SelectedSheet.CustomFrames[_selectedFrameIdx] : null;

    // ── Animation preview ──
    private bool _animPlaying;
    private float _animTime;
    private float _animFPS = 12f;
    private int _animStartFrame;
    private int _animEndFrame;
    private bool _animLoop = true;
    private int _currentPreviewFrame;
    private float _animPreviewZoom = 1f;

    // ── Clip editing preview ──
    private AnimationClip2D? _editingClip;
    private bool _clipPlaying;
    private float _clipTime;
    private int _clipCurrentFrame; // local index into FrameIndices
    private float _clipZoom = 2f;

    // ── Frame drag state ──
    private enum FrameDragMode { None, MoveHitbox, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight, Anchor }
    private FrameDragMode _frameDragMode = FrameDragMode.None;
    private bool _frameDragActive;
    private Vector2 _frameDragStartMouse;
    private Vector4 _frameDragStartHitbox; // X, Y, W, H
    private Vector2 _frameDragStartAnchor;

    // ── Animation clips ──
    public List<AnimationClip2D> AnimationClips = new();
    private int _selectedClipIdx = -1;
    private AnimationClip2D? SelectedClip => _selectedClipIdx >= 0 && _selectedClipIdx < AnimationClips.Count
        ? AnimationClips[_selectedClipIdx] : null;
    private string _newClipName = "New Clip";

    // ── Undo/redo (snapshot-based) ──
    // One undo step = full JSON snapshot of sheets + clips + preview settings.
    // Covers every mutation path (sheet settings, master box, per-frame offsets,
    // clips, FPS, hitboxes, import/delete) with zero per-widget plumbing.
    private const int MaxUndoSteps = 30;
    private readonly List<string> _undoStack = [];
    private readonly List<string> _redoStack = [];
    private string _capturedState = "";   // baseline state at the start of the current frame
    private bool _captureActive;
    private bool _skipNextCapture;         // set by Undo/Redo so the restore itself isn't recorded

    // ── Preview ──
    private float _previewZoom = 1f;
    private readonly Dictionary<string, uint> _previewTextures = new();
    private bool _showPreview = true;
    private float _previewMaxWidth = 600f;

    // Full atlas repack state. Worker callbacks only enqueue immutable progress data;
    // atlas files, editor clips, ImGui and GL textures are committed on the render thread.
    private readonly ConcurrentQueue<(int Completed, int Total, string Status)> _repackProgress = new();
    private Task<SpriteAtlasRepackStagedResult>? _repackTask;
    private CancellationTokenSource? _repackCancellation;
    private bool _repackRequested;
    private float _repackPercent;
    private string _repackStatus = "";
    private string _repackError = "";

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // ── Tool modes ──
    private enum ToolMode { Select, Slice, Anchor, Hitbox }
    private ToolMode _toolMode = ToolMode.Select;

    public SpriteEditorPanel(IDEBridge bridge)
    {
        _bridge = bridge;
    }

    public SpriteEditorPanel()
    {
        // Default ctor used by the IDE initialization; set bridge later if needed.
    }

    public void ShowInMenu() => ImGui.MenuItem("Sprite Editor", null, ref _visible);

    /// <summary>Public one-liner for IDE in-game mode (panel Render() is skipped there):
    /// refresh the static sheet/clip registry so Player2D keeps animating in-game.</summary>
    /// <summary>Throttle: sync the sprite registry at most every N editor frames.
    /// The registry sync's signature gate (IDEBridge.SyncSpriteRegistry) already skips
    /// no-op rebuilds, but the signature itself + enumerable walks still cost per
    /// frame; with the throttle the steady-state cost is ~1/N of that. Any COUNT
    /// change (sheets/clips/textures added or removed) syncs IMMEDIATELY so imports
    /// stay instant; same-count edits (rename/FPS) settle within N frames (~0.13s at
    /// 60fps — imperceptible).</summary>
    private int _syncFrameCounter = 0;
    private int _lastSheetCount = -1;
    private int _lastClipCount = -1;
    private int _lastTextureCount = -1;
    private const int SyncEveryNFrames = 8;

    public void SyncRegistry()
    {
        int sc = _bakedAtlasSheets.Count, cc = AnimationClips.Count, tc = _previewTextures.Count;
        bool countsChanged = sc != _lastSheetCount || cc != _lastClipCount || tc != _lastTextureCount;
        if (!countsChanged && _syncFrameCounter++ % SyncEveryNFrames != 0)
            return; // throttled — signature gate made full syncs conditional anyway
        _lastSheetCount = sc; _lastClipCount = cc; _lastTextureCount = tc;
        var legacyRuntimeSheets = SpriteSheets.Where(sheet => AnimationClips.Any(clip =>
            string.Equals(clip.SpriteSheetName, sheet.Name, StringComparison.OrdinalIgnoreCase)));
        IDEBridge.SyncSpriteRegistry(legacyRuntimeSheets.Concat(_bakedAtlasSheets), _previewTextures, AnimationClips);
    }

    public void Render()
    {
        PumpAtlasRepack();
        // Keep the static sprite registry fresh even when the panel is hidden, so
        // Player2D objects keep resolving their sheet/clip + live texture in-game.
        SyncRegistry();

        if (!_visible) return;

        BeginUndoCapture();

        ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Sprite Editor", ref _visible))
        {
            // ── Window-level drag-drop target ──
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                if (AssetBrowserPanel._dragImagePath != null)
                {
                    string imgPath = AssetBrowserPanel._dragImagePath;
                    AssetBrowserPanel._dragImagePath = null;
                    ImportSpriteFromPath(imgPath);
                    Console.WriteLine($"[SpriteEditor] Drag-drop imported: {imgPath}");
                }
                ImGui.EndDragDropTarget();
            }

            IDE.PanelFocus.Notify("Sprite Editor");

            // ── Toolbar ──
            ImGui.BeginDisabled(!CanUndo);
            if (ImGui.Button("↶ Undo"))
                Undo();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!CanRedo);
            if (ImGui.Button("Redo ↷"))
                Redo();
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Import Sheet"))
                ImportNewSheet();
            ImGui.SameLine();
            if (SelectedSheet != null && ImGui.Button("Delete Sheet"))
            {
                // Delete EVERY selected sheet (multi-select): the highest index first so
                // the remaining removes never shift an index still in the set.
                var doomed = MultiSelectedSheetIndexes();
                foreach (int idx in doomed.OrderByDescending(x => x))
                    SpriteSheets.RemoveAt(idx);
                int nearest = doomed.Min(); // smallest removed index = the slot that replaced them
                _selectedSheetIdx = Math.Min(nearest, SpriteSheets.Count - 1);
                _multiSelectedSheets.Clear(); // indexes shifted — a stale set would point at the wrong sheets
                _sheetRangeAnchored = false;
            }
            ImGui.SameLine();
            if (SelectedSheet != null)
                ImGui.TextDisabled($"+ {_multiSelectedSheets.Count} selected (multi)");
            if (ImGui.Button("Save"))
                SaveAllSheets();
            ImGui.SameLine();
            if (ImGui.Button("Load"))
                LoadAllSheetsDialog();
            ImGui.SameLine();
            if (ImGui.Button("Auto Load"))
                AutoLoadProjectSheets();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Load baked clips from Artifacts/Sprites/sprites.sheets.json, then bake clips from *-sprite-anim.json source exports.\nRequires an open project.");
            ImGui.SameLine();
            ImGui.TextDisabled($"({SpriteSheets.Count} source sheets, {_bakedAtlasSheets.Count} baked atlases, {AnimationClips.Count} clips)");

            if (_repackTask != null || _repackRequested)
            {
                ImGui.ProgressBar(_repackPercent / 100f, new Vector2(-1f, 0f),
                    $"{_repackPercent:0}%  {_repackStatus}");
                ImGui.TextDisabled("Sprite atlas repack runs in the background; the editor remains responsive.");
            }
            else if (!string.IsNullOrEmpty(_repackError))
            {
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.3f, 1f), $"Atlas repack failed: {_repackError}");
            }

            // ── Export row (new line): free-path JSON save + pattern batch auto-clip ──
            if (ImGui.Button("Save As JSON"))
                SaveAsJsonDialog();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Save baked atlas metadata + clips to a JSON file at a free path. Source sheets are not saved.");
            ImGui.SameLine();
            ImGui.BeginDisabled(SelectedSheet == null);
            if (ImGui.Button("Save As Pattern"))
                SaveAsPattern();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Auto-clip per ROW using this pattern sheet (grid + padding/offset + flip + master box).\nAction per row comes from the 'Pattern actions' column (split by '|');\nrows beyond the number of names are NOT saved.\nempty frames (alpha 0) are skipped, FPS = frame count, name <sheet>-<clip>\nRuns for ALL image files in the same folder;\noutput per file: <file name>-sprite-anim.json");

            // ── Free-text action list (defines HOW MANY actions + their names) ──
            ImGui.Text("Pattern actions");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Action names PER ROW, split by '|', ordered from the top row.\nDefault: idle|walk|run|jump start|jump end|attack|dead\nSingle-row sheet: just type 'effect' -> clip <sheet>-effect\nNumber of names = number of actions SAVED (rows beyond the names are not created).\nEmpty = back to the default. jump start/jump end/attack/dead play ONCE;\nother actions (including custom ones like 'effect') LOOP.\nUsed by the Save As Pattern button; stored in the save file (follows Save/Load).");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##pattern_actions", "idle|walk|run|jump start|jump end|attack|dead",
                ref _patternActions, 256);

            ImGui.Separator();

            // ── Temporary source sheet list (not persisted) ──
            if (ImGui.CollapsingHeader("Sprite Sheets", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextDisabled("Source sheets are temporary bake inputs; only baked clips and atlas metadata are saved.");
                if (SpriteSheets.Count == 0)
                    ImGui.TextDisabled("No temporary source sheets loaded. Existing baked clips are listed below; use Import Sheet to create more.");
                for (int i = 0; i < SpriteSheets.Count; i++)
                {
                    bool isSelected = i == _selectedSheetIdx || _multiSelectedSheets.Contains(i);
                    if (ImGui.Selectable($"{SpriteSheets[i].Name}##{i}", isSelected))
                        HandleSheetListClick(i);
                }
            }

    private void RequestFullAtlasRepack(string reason)
    {
        if (_repackTask != null)
        {
            _repackRequested = true;
            _repackStatus = $"Queued ({reason})";
            return;
        }
        _repackRequested = true;
        _repackError = "";
        _repackStatus = $"Preparing full repack ({reason})";
    }

    /// <summary>UI-thread pump: starts workers, consumes queued progress, and commits
    /// completed staging output. No ImGui or OpenGL calls occur in worker callbacks.</summary>
    private void PumpAtlasRepack()
    {
        while (_repackProgress.TryDequeue(out var progress))
        {
            int total = Math.Max(1, progress.Total);
            _repackPercent = Math.Clamp(progress.Completed * 100f / total, 0f, 100f);
            _repackStatus = progress.Status;
        }

        if (_repackTask != null && _repackTask.IsCompleted)
        {
            var completedTask = _repackTask;
            _repackTask = null;
            _repackCancellation?.Dispose();
            _repackCancellation = null;
            try
            {
                var staged = completedTask.GetAwaiter().GetResult();
                CommitFullAtlasRepack(staged);
                _repackPercent = 100f;
                _repackStatus = "Repack complete";
                _repackError = "";
            }
            catch (Exception ex)
            {
                _repackError = ex.GetBaseException().Message;
                _repackStatus = "Repack failed; existing atlas data was retained";
                Console.WriteLine($"[SpriteEditor] Full atlas repack failed safely: {ex}");
            }
            finally
            {
                // Preserve a queued rerun requested while the worker was active.
            }
        }

        if (_repackTask != null || !_repackRequested) return;
        try
        {
            var (sources, clips) = CaptureFullRepackInputs();
            if (clips.Count == 0)
            {
                _repackRequested = false;
                _repackPercent = 100f;
                _repackStatus = "No baked clips to pack";
                return;
            }

            _repackCancellation = new CancellationTokenSource();
            var progress = new InlineProgress<(int Completed, int Total, string Status)>(
                value => _repackProgress.Enqueue(value));
            _repackStatus = "Packing sprite clips...";
            _repackPercent = 0f;
            _repackRequested = false;
            _repackTask = SpriteAtlasBaker.RepackAllAsync(GetProjectSpriteDirectory(),
                sources, clips, progress, _repackCancellation.Token);
        }
        catch (Exception ex)
        {
            _repackRequested = false;
            _repackError = ex.GetBaseException().Message;
            _repackStatus = "Could not start repack";
            Console.WriteLine($"[SpriteEditor] Could not start atlas repack: {ex}");
        }
    }

    private (List<SpriteAtlasRepackSource> Sources, List<SpriteAtlasRepackClipInput> Clips)
        CaptureFullRepackInputs()
    {
        string directory = GetProjectSpriteDirectory();
        if (directory.Length == 0) throw new InvalidOperationException("Atlas repack requires an open project.");

        var referencedClips = AnimationClips
            .Where(c => IsBakedAtlas(c.SpriteSheetName) && c.FrameIndices.Count > 0)
            .ToList();
        var sourceSheets = _bakedAtlasSheets
            .Where(s => referencedClips.Any(c => string.Equals(c.SpriteSheetName, s.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var sources = sourceSheets.Select(sheet => new SpriteAtlasRepackSource(
            sheet.Name,
            Engine.Helpers.PathHelpers.ResolveProjectAsset(sheet.ImagePath),
            Path.Combine(directory, sheet.Name + "Normal.png"),
            (sheet.CustomFrames ?? []).Select(CloneSpriteFrame).ToList())).ToList();

        var clips = new List<SpriteAtlasRepackClipInput>(referencedClips.Count);
        foreach (var clip in referencedClips)
        {
            var sheet = sourceSheets.FirstOrDefault(s => string.Equals(s.Name,
                clip.SpriteSheetName, StringComparison.OrdinalIgnoreCase));
            if (sheet?.CustomFrames == null)
                throw new InvalidDataException($"Clip '{clip.Name}' references missing atlas '{clip.SpriteSheetName}'.");
            var frames = new List<SpriteAtlasRepackFrameInput>(clip.FrameIndices.Count);
            foreach (int frameIndex in clip.FrameIndices)
            {
                if (frameIndex < 0 || frameIndex >= sheet.CustomFrames.Count)
                    throw new InvalidDataException($"Clip '{clip.Name}' references invalid frame {sheet.Name}[{frameIndex}].");
                var frame = sheet.CustomFrames[frameIndex];
                frames.Add(new SpriteAtlasRepackFrameInput(sheet.Name, frameIndex,
                    frame.Width, frame.Height, CloneSpriteFrame(frame)));
            }
            clips.Add(new SpriteAtlasRepackClipInput(clip.Name, frames));
        }
        return (sources, clips);
    }

    private static SpriteFrame CloneSpriteFrame(SpriteFrame frame) => new()
    {
        X = frame.X, Y = frame.Y, Width = frame.Width, Height = frame.Height,
        Name = frame.Name, AnchorX = frame.AnchorX, AnchorY = frame.AnchorY,
        RenderOffsetX = frame.RenderOffsetX, RenderOffsetY = frame.RenderOffsetY,
        HitboxX = frame.HitboxX, HitboxY = frame.HitboxY,
        HitboxW = frame.HitboxW, HitboxH = frame.HitboxH,
        Tags = frame.Tags != null ? new Dictionary<string, string>(frame.Tags) : new()
    };

    /// <summary>Commit stage outputs transactionally on the UI thread. Existing target
    /// atlas files and the canonical metadata are backed up and restored if any file
    /// replacement fails. Live clip/sheet state changes only after metadata is written.</summary>
    private void CommitFullAtlasRepack(SpriteAtlasRepackStagedResult staged)
    {
        string savePath = GetDefaultSavePath();
        string directory = GetProjectSpriteDirectory();
        string backupDirectory = Path.Combine(directory, $".atlas-repack-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        var fileBackups = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string metadataTemp = savePath + ".repack.tmp";
        var oldAtlasSheets = _bakedAtlasSheets.ToList();
        var oldClips = AnimationClips.Select(CloneClip).ToList();
        bool metadataReplaced = false;
        try
        {
            var newAtlasSheets = staged.Atlases.ToList();
            var newClips = AnimationClips.Select(CloneClip).ToList();
            foreach (var placement in staged.Clips)
            {
                var clip = newClips.FirstOrDefault(c => string.Equals(c.Name, placement.ClipName,
                    StringComparison.OrdinalIgnoreCase));
                if (clip == null) throw new InvalidDataException($"Staged repack is missing live clip '{placement.ClipName}'.");
                clip.SpriteSheetName = $"mainsprite{placement.AtlasNumber}";
                clip.FrameIndices = placement.FramePlacements.Select(p => p.AtlasFrameIndex).ToList();
            }

            var candidateData = CaptureSaveData();
            candidateData.Sheets = newAtlasSheets.Select(s => s.ToData()).ToList();
            candidateData.AnimationClips = newClips.Select(c => c.ToData()).ToList();
            File.WriteAllText(metadataTemp, JsonSerializer.Serialize(candidateData,
                new JsonSerializerOptions { WriteIndented = true }));

            foreach (var atlas in newAtlasSheets)
            {
                foreach (string suffix in new[] { ".png", "Normal.png" })
                {
                    string target = Path.Combine(directory, atlas.Name + suffix);
                    string? backup = null;
                    if (File.Exists(target))
                    {
                        backup = Path.Combine(backupDirectory, Path.GetFileName(target));
                        File.Copy(target, backup, overwrite: true);
                    }
                    fileBackups[target] = backup;
                    string stagedPath = Path.Combine(staged.StagingDirectory, atlas.Name + suffix);
                    File.Move(stagedPath, target, overwrite: true);
                }
            }

            string? metadataBackup = null;
            if (File.Exists(savePath))
            {
                metadataBackup = Path.Combine(backupDirectory, Path.GetFileName(savePath));
                File.Copy(savePath, metadataBackup, overwrite: true);
            }
            fileBackups[savePath] = metadataBackup;
            File.Move(metadataTemp, savePath, overwrite: true);
            metadataReplaced = true;

            _bakedAtlasSheets.Clear();
            _bakedAtlasSheets.AddRange(newAtlasSheets);
            AnimationClips.Clear();
            AnimationClips.AddRange(newClips);
            ReloadRepackedAtlasTextures(newAtlasSheets);
            SyncRegistry();
            Engine.Helpers.BinaryObjectCache.TryWrite(savePath, candidateData);
            _selectedClipIdx = AnimationClips.Count > 0
                ? Math.Clamp(_selectedClipIdx, 0, AnimationClips.Count - 1) : -1;
            Console.WriteLine($"[SpriteEditor] Full repack committed: {AnimationClips.Count} clips into {newAtlasSheets.Count} atlases.");
        }
        catch
        {
            foreach (var (path, backup) in fileBackups.Reverse())
            {
                try
                {
                    if (backup == null) { if (File.Exists(path)) File.Delete(path); }
                    else File.Copy(backup, path, overwrite: true);
                }
                catch (Exception rollbackEx)
                {
                    Console.WriteLine($"[SpriteEditor] WARNING: atlas repack rollback failed for {path}: {rollbackEx.Message}");
                }
            }
            _bakedAtlasSheets.Clear();
            _bakedAtlasSheets.AddRange(oldAtlasSheets);
            AnimationClips.Clear();
            AnimationClips.AddRange(oldClips);
            try { ReloadRepackedAtlasTextures(oldAtlasSheets); } catch { }
            SyncRegistry();
            if (metadataReplaced)
                Engine.Helpers.BinaryObjectCache.TryWrite(savePath, CaptureSaveData());
            throw;
        }
        finally
        {
            try { if (File.Exists(metadataTemp)) File.Delete(metadataTemp); } catch { }
            try { if (Directory.Exists(staged.StagingDirectory)) Directory.Delete(staged.StagingDirectory, recursive: true); } catch { }
            try { if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: true); } catch { }
        }
    }

    private static AnimationClip2D CloneClip(AnimationClip2D clip) => new()
    {
        Name = clip.Name, SpriteSheetName = clip.SpriteSheetName,
        FrameIndices = clip.FrameIndices.ToList(), FPS = clip.FPS,
        Loop = clip.Loop, Reverse = clip.Reverse, SpeedMultiplier = clip.SpeedMultiplier,
        Events = clip.Events.ToDictionary(kv => kv.Key, kv => kv.Value),
        MasterWidth = clip.MasterWidth, MasterHeight = clip.MasterHeight,
        SpriteOffsetX = clip.SpriteOffsetX, SpriteOffsetY = clip.SpriteOffsetY
    };

    private void ReloadRepackedAtlasTextures(IEnumerable<SpriteSheet> atlases)
    {
        foreach (var atlas in atlases)
            ReloadPreviewTexture(atlas.ImagePath);
    }

            if (SelectedSheet != null)
            {
                ImGui.Separator();

                // ── Sheet Settings ──
                if (ImGui.CollapsingHeader("Sheet Settings", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.InputText("Sheet Name##sheet", ref _newSheetName, 256);

                    bool gridChanged = false;
                    gridChanged |= ImGui.InputInt("Columns", ref _editCols);
                    gridChanged |= ImGui.InputInt("Rows", ref _editRows);
                    gridChanged |= ImGui.InputInt("Frame Width", ref _editFrameW);
                    gridChanged |= ImGui.InputInt("Frame Height", ref _editFrameH);
                    gridChanged |= ImGui.InputInt("Padding X", ref _editPadX);
                    gridChanged |= ImGui.InputInt("Padding Y", ref _editPadY);
                    gridChanged |= ImGui.InputInt("Offset X", ref _editOffsetX);
                    gridChanged |= ImGui.InputInt("Offset Y", ref _editOffsetY);

                    if (gridChanged)
                    {
                        SelectedSheet.Name = _newSheetName;
                        SelectedSheet.Columns = Math.Max(1, _editCols);
                        SelectedSheet.Rows = Math.Max(1, _editRows);
                        SelectedSheet.FrameWidth = Math.Max(1, _editFrameW);
                        SelectedSheet.FrameHeight = Math.Max(1, _editFrameH);
                        SelectedSheet.PaddingX = Math.Max(0, _editPadX);
                        SelectedSheet.PaddingY = Math.Max(0, _editPadY);
                        SelectedSheet.OffsetX = Math.Max(0, _editOffsetX);
                        SelectedSheet.OffsetY = Math.Max(0, _editOffsetY);
                        SelectedSheet.BakeUniformFrames();
                    }
                    ImGui.Text($"Frames: {SelectedSheet.FrameCount}");

                    ImGui.Separator();
                    ImGui.TextDisabled("Render Normalization (per sheet)");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("The FINAL sprite size (master box W × H) shared by all sheets. Frames render as-is inside the box, nudged by offsets. Viewport shows the master box.");

                    float masterW = SelectedSheet.MasterWidth;
                    if (ImGui.DragFloat("Master Width (px)", ref masterW, 1f, 0f, 1024f, "%.0f"))
                        SelectedSheet.MasterWidth = MathF.Max(0f, masterW);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Master box width — the final sprite render width shared by all sheets. 0 = square (same as Master Height).");

                    float masterH = SelectedSheet.MasterHeight;
                    if (ImGui.DragFloat("Master Height (px)", ref masterH, 1f, 0f, 1024f, "%.0f"))
                        SelectedSheet.MasterHeight = MathF.Max(0f, masterH);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Master box height — the final sprite render height. The player's Sprite Height maps to exactly this many px. 0 = no normalization (native frame size).");

                    float sOffX = SelectedSheet.SpriteOffsetX;
                    if (ImGui.DragFloat("Render Offset X (px)", ref sOffX, 0.5f, -512f, 512f, "%.1f"))
                        SelectedSheet.SpriteOffsetX = Math.Clamp(sOffX, -512f, 512f);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Sheet-wide horizontal nudge applied to every frame. Positive = right.");

                    float sOffY = SelectedSheet.SpriteOffsetY;
                    if (ImGui.DragFloat("Render Offset Y (px)", ref sOffY, 0.5f, -512f, 512f, "%.1f"))
                        SelectedSheet.SpriteOffsetY = Math.Clamp(sOffY, -512f, 512f);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Sheet-wide vertical nudge from the ground line applied to every frame. Positive = up.");

                    bool flipY = SelectedSheet.FlipY;
                    if (ImGui.Checkbox("Flip Y (vertical mirror)", ref flipY))
                        SelectedSheet.FlipY = flipY;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Vertical mirror (Flip Y) for all frames of this sheet.\nDefault OFF. Art drawn upside-down in the source file\nwill display correctly when rendered (Player2D, Sprite2D, DoF mask,\nand this editor preview). Stored in the sheet data.");

                    bool flipX = SelectedSheet.FlipX;
                    if (ImGui.Checkbox("Flip X (horizontal mirror)", ref flipX))
                        SelectedSheet.FlipX = flipX;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Horizontal mirror (Flip X / HFlip) for all frames of this sheet.\nDefault OFF. Art facing the wrong way in the source file displays\nmirrored when rendered. Combined with the object's facing:\ntwo horizontal mirrors cancel out (FlipX + facing left = normal).\nStored in the sheet data.");
                }

                ImGui.Separator();

                // ── Sprite Sheet Preview ──
                if (ImGui.CollapsingHeader("Preview", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    RenderSheetPreview();
                }

                // ── Frame Grid Preview ──
                if (ImGui.CollapsingHeader("Frame Grid", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    RenderFrameGrid();
                }

                ImGui.Separator();

                // ── Selected Frame ──
                if (SelectedFrame != null)
                {
                    if (ImGui.CollapsingHeader("Frame Properties", ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        // ── Frame thumbnail preview ──
                        RenderFrameThumbnail();

                        ImGui.InputText("Frame Name##frame", ref SelectedFrame.Name, 256);
                        ImGui.SliderFloat("Anchor X", ref SelectedFrame.AnchorX, 0f, 1f);
                        ImGui.SliderFloat("Anchor Y", ref SelectedFrame.AnchorY, 0f, 1f);
                        ImGui.Separator();
                        ImGui.TextDisabled("Render Offset (per frame, inside master box):");
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Nudges THIS frame only when rendering (master normalization on). Positive X = right, positive Y = up from the ground line.");
                        float fOffX = SelectedFrame.RenderOffsetX;
                        if (ImGui.DragFloat("Offset X (px)##frameoff", ref fOffX, 0.5f, -256f, 256f, "%.1f"))
                            SelectedFrame.RenderOffsetX = Math.Clamp(fOffX, -256f, 256f);
                        float fOffY = SelectedFrame.RenderOffsetY;
                        if (ImGui.DragFloat("Offset Y (px)##frameoff", ref fOffY, 0.5f, -256f, 256f, "%.1f"))
                            SelectedFrame.RenderOffsetY = Math.Clamp(fOffY, -256f, 256f);
                        ImGui.Separator();
                        ImGui.Text("Hitbox (pixels):");
                        ImGui.InputInt("X", ref SelectedFrame.HitboxX);
                        ImGui.InputInt("Y", ref SelectedFrame.HitboxY);
                        ImGui.InputInt("W", ref SelectedFrame.HitboxW);
                        ImGui.InputInt("H", ref SelectedFrame.HitboxH);
                    }
                }

                ImGui.Separator();

                // ── Animation Preview ──
                if (ImGui.CollapsingHeader("Animation Preview", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    RenderAnimationPreview();
                }

            }

            ImGui.Separator();
            // Baked clips remain browseable even when temporary source sheets are not
            // loaded (normal after project open, since baking only persists atlases).
            if (ImGui.CollapsingHeader("Animation Clips", ImGuiTreeNodeFlags.DefaultOpen))
                RenderAnimationClips();

            // ── Drop zone indicator ──
            if (SelectedSheet == null)
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.5f, 0.7f, 1f, 0.8f), "Drop sprite sheet image here");
            }

            // ── Bottom drop target (fallback) ──
            var availSize = ImGui.GetContentRegionAvail();
            if (availSize.Y > 0)
            {
                ImGui.InvisibleButton("##drop_target", new Vector2(Math.Max(availSize.X, 100f), Math.Max(availSize.Y, 20f)));
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (AssetBrowserPanel._dragImagePath != null)
                    {
                        string imgPath = AssetBrowserPanel._dragImagePath;
                        AssetBrowserPanel._dragImagePath = null;
                        ImportSpriteFromPath(imgPath);
                        Console.WriteLine($"[SpriteEditor] Drag-drop imported: {imgPath}");
                    }
                    ImGui.EndDragDropTarget();
                }
            }
        }
        ImGui.End();

        // Render file dialogs outside the panel window
        _importDialog.Render();
        ProcessImportResult();
        _loadSheetsDialog.Render();
        ProcessLoadSheetsResult();
        _saveAsJsonDialog.Render();
        ProcessSaveAsJsonResult();

        EndUndoCapture();
    }

    // ═══════════════════════════════════════════════
    //  UNDO / REDO (snapshot-based)
    // ═══════════════════════════════════════════════

    /// <summary>Start the per-frame capture window (top of Render).</summary>
    private void BeginUndoCapture()
    {
        _captureActive = true;
        // First baseline: record current state without pushing an undo entry.
        if (_capturedState.Length == 0 && _undoStack.Count == 0 && _redoStack.Count == 0)
            _capturedState = SerializeForUndo();
    }

    /// <summary>End the capture window (bottom of Render): if the serialized state
    /// changed since frame start, the PRE-change snapshot becomes an undo entry.</summary>
    private void EndUndoCapture()
    {
        _captureActive = false;
        string newState = SerializeForUndo();
        if (_skipNextCapture)
        {
            _skipNextCapture = false;
            _capturedState = newState;
            return;
        }
        if (newState != _capturedState)
        {
            if (_capturedState.Length > 0)
            {
                _undoStack.Add(_capturedState);
                if (_undoStack.Count > MaxUndoSteps)
                    _undoStack.RemoveAt(0);
                _redoStack.Clear();
            }
            _capturedState = newState;
        }
    }

    private string SerializeForUndo() => JsonSerializer.Serialize(CaptureUndoData());

    /// <summary>Drop all undo/redo history (project change / file load).</summary>
    private void ClearUndoHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _capturedState = "";
        _skipNextCapture = false;
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Add(SerializeForUndo());          // current state → redo
        string state = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        ApplyStateSnapshot(state);
        _skipNextCapture = true;                      // don't record the restore as a change
        Console.WriteLine("[SpriteEditor] Undo");
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Add(SerializeForUndo());           // current state → undo
        string state = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        ApplyStateSnapshot(state);
        _skipNextCapture = true;
        Console.WriteLine("[SpriteEditor] Redo");
    }

    /// <summary>Restore sheets + clips + preview settings from a JSON snapshot.
    /// Keeps the current sheet/clip selection clamped to the restored counts.</summary>
    private void ApplyStateSnapshot(string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<SpriteSheetsSaveData>(json);
            if (data?.Sheets == null) return;

            SpriteSheets.Clear();
            _bakedAtlasSheets.Clear();
            _autoLoadedPatternClips.Clear();
            foreach (var sd in data.Sheets)
            {
                var sheet = SpriteSheet.FromData(sd);
                _bakedAtlasSheets.Add(sheet);
                if (!string.IsNullOrEmpty(sheet.ImagePath) && File.Exists(Engine.Helpers.PathHelpers.ResolveProjectAsset(sheet.ImagePath)))
                    LoadPreviewTexture(sheet.ImagePath);
            }
            if (data.UndoSourceSheets != null)
                SpriteSheets.AddRange(data.UndoSourceSheets.Select(SpriteSheet.FromData));
            AnimationClips.Clear();
            if (data.AnimationClips != null)
                foreach (var cd in data.AnimationClips)
                    AnimationClips.Add(AnimationClip2D.FromData(cd));

            _selectedSheetIdx = SpriteSheets.Count > 0
                ? Math.Clamp(_selectedSheetIdx, 0, SpriteSheets.Count - 1) : -1;
            _multiSelectedSheets.RemoveWhere(i => i < 0 || i >= SpriteSheets.Count); // undo snapshot: keep the in-bounds extras
            _selectedClipIdx = AnimationClips.Count > 0
                ? Math.Clamp(_selectedClipIdx, 0, AnimationClips.Count - 1) : -1;
            _selectedFrameIdx = -1;
            _editingClip = null;

            if (SelectedSheet != null)
            {
                LoadSheetSettings();
                _animStartFrame = Math.Clamp(data.PreviewStartFrame >= 0 ? data.PreviewStartFrame : 0,
                    0, Math.Max(0, SelectedSheet.FrameCount - 1));
                _animEndFrame = Math.Clamp(Math.Max(data.PreviewEndFrame, _animStartFrame),
                    _animStartFrame, Math.Max(0, SelectedSheet.FrameCount - 1));
                if (data.PreviewFps > 0)
                    _animFPS = Math.Clamp(data.PreviewFps, 1f, 120f);
                _animLoop = data.PreviewLoop;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Undo apply failed: {ex.Message}");
        }
    }

    private void RenderSheetPreview()
    {
        if (SelectedSheet == null) return;

        ImGui.Checkbox("Show Preview", ref _showPreview);
        ImGui.SameLine();
        ImGui.PushItemWidth(150);
        ImGui.SliderFloat("Zoom##preview", ref _previewZoom, 1f, 8f, "%.1fx");
        ImGui.PopItemWidth();

        if (!_showPreview) return;

        if (string.IsNullOrEmpty(SelectedSheet.ImagePath) || !System.IO.File.Exists(Engine.Helpers.PathHelpers.ResolveProjectAsset(SelectedSheet.ImagePath)))
        {
            ImGui.TextDisabled("No image loaded");
            return;
        }

        // Load texture if not cached
        LoadPreviewTexture(SelectedSheet.ImagePath);

        if (!_previewTextures.TryGetValue(Engine.Helpers.PathHelpers.ResolveProjectAsset(SelectedSheet.ImagePath), out uint texId) || texId == 0)
        {
            ImGui.TextDisabled("Failed to load image");
            return;
        }

        // Calculate preview size with zoom
        float imgW = SelectedSheet.ImageWidth;
        float imgH = SelectedSheet.ImageHeight;
        float maxW = ImGui.GetContentRegionAvail().X;
        float baseScale = Math.Min(maxW / imgW, 400f / imgH);
        float scale = baseScale * _previewZoom;
        float dispW = imgW * scale;
        float dispH = imgH * scale;

        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.Image((nint)texId, new Vector2(dispW, dispH));

        // Draw frame grid overlay
        var drawList = ImGui.GetWindowDrawList();
        uint gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 0f, 0.5f));
        uint selectedColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 0.8f));

        for (int r = 0; r < SelectedSheet.Rows; r++)
        {
            for (int c = 0; c < SelectedSheet.Columns; c++)
            {
                float fx = cursorPos.X + (SelectedSheet.OffsetX + c * (SelectedSheet.FrameWidth + SelectedSheet.PaddingX)) * scale;
                float fy = cursorPos.Y + (SelectedSheet.OffsetY + r * (SelectedSheet.FrameHeight + SelectedSheet.PaddingY)) * scale;
                float fw = SelectedSheet.FrameWidth * scale;
                float fh = SelectedSheet.FrameHeight * scale;

                int frameIdx = r * SelectedSheet.Columns + c;
                uint color = frameIdx == _selectedFrameIdx ? selectedColor : gridColor;

                drawList.AddRect(new Vector2(fx, fy), new Vector2(fx + fw, fy + fh), color, 1f);

                // Frame number label
                string label = $"{frameIdx}";
                var textSize = ImGui.CalcTextSize(label);
                if (textSize.X < fw && textSize.Y < fh)
                {
                    drawList.AddText(
                        new Vector2(fx + (fw - textSize.X) * 0.5f, fy + (fh - textSize.Y) * 0.5f),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.8f)), label);
                }
            }
        }

        ImGui.Dummy(new Vector2(dispW, dispH));
    }

    private void RenderFrameGrid()
    {
        if (SelectedSheet == null) return;

        float cellSize = 48f;
        int cols = SelectedSheet.Columns;
        int rows = SelectedSheet.Rows;
        float gridW = cols * cellSize;
        float gridH = rows * cellSize;

        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Draw grid
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int frameIdx = r * cols + c;
                float x = cursorPos.X + c * cellSize;
                float y = cursorPos.Y + r * cellSize;

                bool isSelected = frameIdx == _selectedFrameIdx;
                uint bgColor = isSelected
                    ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.6f, 1f, 0.8f))
                    : ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.25f, 0.9f));
                uint borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.5f, 1f));

                drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + cellSize - 1, y + cellSize - 1), bgColor, 2f);
                drawList.AddRect(new Vector2(x, y), new Vector2(x + cellSize - 1, y + cellSize - 1), borderColor, 2f);

                // Frame number
                string label = $"{frameIdx}";
                var textSize = ImGui.CalcTextSize(label);
                drawList.AddText(
                    new Vector2(x + (cellSize - textSize.X) * 0.5f, y + (cellSize - textSize.Y) * 0.5f),
                    ImGui.ColorConvertFloat4ToU32(Vector4.One), label);

                // Click detection
                ImGui.SetCursorScreenPos(new Vector2(x, y));
                ImGui.InvisibleButton($"##frame_{frameIdx}", new Vector2(cellSize, cellSize));
                if (ImGui.IsItemClicked())
                    _selectedFrameIdx = frameIdx;
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + gridH + 4f));
    }

    private void RenderFrameThumbnail()
    {
        if (SelectedSheet == null || SelectedFrame == null) return;
        if (string.IsNullOrEmpty(SelectedSheet.ImagePath) || !File.Exists(Engine.Helpers.PathHelpers.ResolveProjectAsset(SelectedSheet.ImagePath))) return;
        if (SelectedSheet.ImageWidth <= 0 || SelectedSheet.ImageHeight <= 0) return;

        LoadPreviewTexture(SelectedSheet.ImagePath);
        if (!_previewTextures.TryGetValue(Engine.Helpers.PathHelpers.ResolveProjectAsset(SelectedSheet.ImagePath), out uint texId) || texId == 0) return;

        // Calculate UV for this specific frame
        float u0 = (float)SelectedFrame.X / SelectedSheet.ImageWidth;
        float v0 = (float)SelectedFrame.Y / SelectedSheet.ImageHeight;
        float u1 = (float)(SelectedFrame.X + SelectedFrame.Width) / SelectedSheet.ImageWidth;
        float v1 = (float)(SelectedFrame.Y + SelectedFrame.Height) / SelectedSheet.ImageHeight;

        // Display size: scale up to a nice preview, max 128px
        float maxSize = 128f;
        float frameW = SelectedFrame.Width;
        float frameH = SelectedFrame.Height;
        float scale = Math.Min(maxSize / Math.Max(frameW, frameH), 4f);
        float dispW = frameW * scale;
        float dispH = frameH * scale;

        // Checkerboard background to show transparency
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        uint checkerA = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.35f, 0.35f, 1f));
        uint checkerB = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.25f, 1f));
        int checkerSize = (int)(8f * scale);
        if (checkerSize < 4) checkerSize = 4;
        for (int cy = 0; cy < dispH; cy += checkerSize)
        {
            for (int cx = 0; cx < dispW; cx += checkerSize)
            {
                bool isA = ((cx / checkerSize) + (cy / checkerSize)) % 2 == 0;
                drawList.AddRectFilled(
                    new Vector2(cursorPos.X + cx, cursorPos.Y + cy),
                    new Vector2(Math.Min(cursorPos.X + cx + checkerSize, cursorPos.X + dispW),
                                Math.Min(cursorPos.Y + cy + checkerSize, cursorPos.Y + dispH)),
                    isA ? checkerA : checkerB);
            }
        }

        // Draw the frame image
        ImGui.Image((nint)texId, new Vector2(dispW, dispH),
            new Vector2(u0, v0), new Vector2(u1, v1));

        // ── Interactive overlay ──
        var mousePos = ImGui.GetIO().MousePos;
        bool mouseInFrame = mousePos.X >= cursorPos.X && mousePos.X <= cursorPos.X + dispW &&
                            mousePos.Y >= cursorPos.Y && mousePos.Y <= cursorPos.Y + dispH;

        // Draw hitbox overlay
        float hx = cursorPos.X + SelectedFrame.HitboxX * scale;
        float hy = cursorPos.Y + SelectedFrame.HitboxY * scale;
        float hw = Math.Max(SelectedFrame.HitboxW * scale, 4f);
        float hh = Math.Max(SelectedFrame.HitboxH * scale, 4f);
        bool hasHitbox = SelectedFrame.HitboxW > 0 && SelectedFrame.HitboxH > 0;

        if (hasHitbox)
        {
            drawList.AddRect(
                new Vector2(hx, hy), new Vector2(hx + hw, hy + hh),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.2f, 0.2f, 0.9f)), 0f, 0, 1.5f);

            // Resize handles (corners + edges)
            float handleSize = 5f;
            uint handleColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 1f));
            // Corners
            drawList.AddRectFilled(new Vector2(hx - handleSize, hy - handleSize), new Vector2(hx + handleSize, hy + handleSize), handleColor);
            drawList.AddRectFilled(new Vector2(hx + hw - handleSize, hy - handleSize), new Vector2(hx + hw + handleSize, hy + handleSize), handleColor);
            drawList.AddRectFilled(new Vector2(hx - handleSize, hy + hh - handleSize), new Vector2(hx + handleSize, hy + hh + handleSize), handleColor);
            drawList.AddRectFilled(new Vector2(hx + hw - handleSize, hy + hh - handleSize), new Vector2(hx + hw + handleSize, hy + hh + handleSize), handleColor);

            // Hitbox label
            string hbLabel = $"Hitbox {SelectedFrame.HitboxW}x{SelectedFrame.HitboxH}";
            var hbSize = ImGui.CalcTextSize(hbLabel);
            drawList.AddRectFilled(new Vector2(hx, hy - hbSize.Y - 4), new Vector2(hx + hbSize.X + 6, hy - 1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.7f)));
            drawList.AddText(new Vector2(hx + 3, hy - hbSize.Y - 3),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.3f, 0.3f, 1f)), hbLabel);
        }

        // Draw anchor point
        {
            float ax = cursorPos.X + SelectedFrame.AnchorX * dispW;
            float ay = cursorPos.Y + SelectedFrame.AnchorY * dispH;
            drawList.AddCircleFilled(new Vector2(ax, ay), 4f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 0f, 1f)));
            drawList.AddCircle(new Vector2(ax, ay), 6f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0.5f, 0f, 1f)));
        }

        // ── Normalized render preview — WYSIWYG with the in-game viewport: the mini
        // box IS the master box; this frame draws as-is inside it (native px), feet
        // on the cyan ground line, nudged by sheet + frame offsets. ──
        {
            float miniBox = 80f;
            float mCellW = MathF.Max(1, SelectedFrame.Width);
            float mCellH = MathF.Max(1, SelectedFrame.Height);
            bool mNorm = SelectedSheet.MasterHeight > 0f;
            float mMasterH = mNorm ? SelectedSheet.MasterHeight : mCellH;
            float mMasterW = mNorm
                ? (SelectedSheet.MasterWidth > 0f ? SelectedSheet.MasterWidth : mMasterH)
                : mCellW;
            float mScale = miniBox / mMasterH;

            float mW = mCellW * mScale, mH = mCellH * mScale;
            var mPos = new Vector2(cursorPos.X + dispW + 16, cursorPos.Y);
            drawList.AddRectFilled(mPos, new Vector2(mPos.X + miniBox, mPos.Y + miniBox),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.18f, 1f)));
            // Yellow master-box outline + cyan ground line (= feet anchor).
            if (mNorm)
                drawList.AddRect(mPos, new Vector2(mPos.X + miniBox, mPos.Y + miniBox),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 0.6f)));
            drawList.AddLine(new Vector2(mPos.X, mPos.Y + miniBox), new Vector2(mPos.X + miniBox, mPos.Y + miniBox),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.8f, 1f, 0.7f)), 1f);
            float mOffX = (SelectedSheet.SpriteOffsetX + SelectedFrame.RenderOffsetX) * mScale;
            // +Y offset = UP in world space → SUBTRACT in ImGui screen space (Y grows down)
            // so the editor preview shows exactly what the viewport will render.
            float mOffY = (SelectedSheet.SpriteOffsetY + SelectedFrame.RenderOffsetY) * mScale;
            var imgMin = new Vector2(mPos.X + (miniBox - mW) * 0.5f + mOffX, mPos.Y + miniBox - mH - mOffY);
            // Flip Y / Flip X sheet: mirror in the WYSIWYG preview (same as viewport).
            // The editing canvas above stays in source-art space so hitbox/anchor
            // mouse mapping keeps working unchanged.
            bool mFX = SelectedSheet.FlipX, mFY = SelectedSheet.FlipY;
            var mUvMin = new Vector2(mFX ? u1 : u0, mFY ? v1 : v0);
            var mUvMax = new Vector2(mFX ? u0 : u1, mFY ? v0 : v1);
            drawList.AddImage((nint)texId, imgMin, new Vector2(imgMin.X + mW, imgMin.Y + mH),
                mUvMin, mUvMax);

            string mLabel = mNorm
                ? $"master {mMasterW:0}×{mMasterH:0}px · frame {mCellW:0}×{mCellH:0}px"
                : "render: native size";
            drawList.AddText(new Vector2(mPos.X, mPos.Y + miniBox + 6),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.85f, 0.55f, 1f)), mLabel);
        }

        // Frame info
        ImGui.SameLine();
        ImGui.Text($"{SelectedFrame.Width}x{SelectedFrame.Height}px @ ({SelectedFrame.X},{SelectedFrame.Y})");

        // ── Interactive hitbox editing ──
        if (mouseInFrame && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.GetIO().KeyCtrl)
        {
            float mx = mousePos.X - cursorPos.X;
            float my = mousePos.Y - cursorPos.Y;

            // Check if clicking near anchor (drag anchor)
            float ax = SelectedFrame.AnchorX * dispW;
            float ay = SelectedFrame.AnchorY * dispH;
            float anchorDist = MathF.Sqrt((mx - ax) * (mx - ax) + (my - ay) * (my - ay));
            if (anchorDist < 10f)
            {
                _frameDragMode = FrameDragMode.Anchor;
                _frameDragActive = true;
            }
            // Check if clicking on hitbox resize handles
            else if (hasHitbox)
            {
                float hbx = SelectedFrame.HitboxX * scale;
                float hby = SelectedFrame.HitboxY * scale;
                float hbw = SelectedFrame.HitboxW * scale;
                float hbh = SelectedFrame.HitboxH * scale;

                // Check corners for resize
                if (MathF.Abs(mx - hbx) < 8f && MathF.Abs(my - hby) < 8f)
                    { _frameDragMode = FrameDragMode.ResizeTopLeft; _frameDragActive = true; }
                else if (MathF.Abs(mx - (hbx + hbw)) < 8f && MathF.Abs(my - hby) < 8f)
                    { _frameDragMode = FrameDragMode.ResizeTopRight; _frameDragActive = true; }
                else if (MathF.Abs(mx - hbx) < 8f && MathF.Abs(my - (hby + hbh)) < 8f)
                    { _frameDragMode = FrameDragMode.ResizeBottomLeft; _frameDragActive = true; }
                else if (MathF.Abs(mx - (hbx + hbw)) < 8f && MathF.Abs(my - (hby + hbh)) < 8f)
                    { _frameDragMode = FrameDragMode.ResizeBottomRight; _frameDragActive = true; }
                // Check inside hitbox body (move)
                else if (mx >= hbx && mx <= hbx + hbw && my >= hby && my <= hby + hbh)
                    { _frameDragMode = FrameDragMode.MoveHitbox; _frameDragActive = true; }
                // Click outside hitbox — create new hitbox
                else
                {
                    SelectedFrame.HitboxX = (int)(mx / scale);
                    SelectedFrame.HitboxY = (int)(my / scale);
                    SelectedFrame.HitboxW = 16;
                    SelectedFrame.HitboxH = 16;
                    _frameDragMode = FrameDragMode.ResizeBottomRight;
                    _frameDragActive = true;
                }
            }
            else
            {
                // No hitbox — create one at click position
                SelectedFrame.HitboxX = (int)(mx / scale);
                SelectedFrame.HitboxY = (int)(my / scale);
                SelectedFrame.HitboxW = 16;
                SelectedFrame.HitboxH = 16;
                _frameDragMode = FrameDragMode.ResizeBottomRight;
                _frameDragActive = true;
            }
            _frameDragStartMouse = mousePos;
            _frameDragStartHitbox = new Vector4(SelectedFrame.HitboxX, SelectedFrame.HitboxY,
                SelectedFrame.HitboxW, SelectedFrame.HitboxH);
            _frameDragStartAnchor = new Vector2(SelectedFrame.AnchorX, SelectedFrame.AnchorY);
        }

        // ── Drag update ──
        if (_frameDragActive && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            float dmX = (mousePos.X - _frameDragStartMouse.X) / scale;
            float dmY = (mousePos.Y - _frameDragStartMouse.Y) / scale;

            switch (_frameDragMode)
            {
                case FrameDragMode.MoveHitbox:
                    SelectedFrame.HitboxX = Math.Clamp((int)(_frameDragStartHitbox.X + dmX), 0, SelectedFrame.Width - SelectedFrame.HitboxW);
                    SelectedFrame.HitboxY = Math.Clamp((int)(_frameDragStartHitbox.Y + dmY), 0, SelectedFrame.Height - SelectedFrame.HitboxH);
                    break;

                case FrameDragMode.ResizeTopLeft:
                    SelectedFrame.HitboxX = Math.Clamp((int)(_frameDragStartHitbox.X + dmX), 0, (int)(_frameDragStartHitbox.X + _frameDragStartHitbox.Z - 4));
                    SelectedFrame.HitboxY = Math.Clamp((int)(_frameDragStartHitbox.Y + dmY), 0, (int)(_frameDragStartHitbox.Y + _frameDragStartHitbox.W - 4));
                    SelectedFrame.HitboxW = (int)(_frameDragStartHitbox.Z - (SelectedFrame.HitboxX - _frameDragStartHitbox.X));
                    SelectedFrame.HitboxH = (int)(_frameDragStartHitbox.W - (SelectedFrame.HitboxY - _frameDragStartHitbox.Y));
                    break;

                case FrameDragMode.ResizeTopRight:
                    SelectedFrame.HitboxY = Math.Clamp((int)(_frameDragStartHitbox.Y + dmY), 0, (int)(_frameDragStartHitbox.Y + _frameDragStartHitbox.W - 4));
                    SelectedFrame.HitboxW = Math.Clamp((int)(_frameDragStartHitbox.Z + dmX), 4, (int)(SelectedFrame.Width - _frameDragStartHitbox.X));
                    SelectedFrame.HitboxH = (int)(_frameDragStartHitbox.W - (SelectedFrame.HitboxY - _frameDragStartHitbox.Y));
                    break;

                case FrameDragMode.ResizeBottomLeft:
                    SelectedFrame.HitboxX = Math.Clamp((int)(_frameDragStartHitbox.X + dmX), 0, (int)(_frameDragStartHitbox.X + _frameDragStartHitbox.Z - 4));
                    SelectedFrame.HitboxW = (int)(_frameDragStartHitbox.Z - (SelectedFrame.HitboxX - _frameDragStartHitbox.X));
                    SelectedFrame.HitboxH = Math.Clamp((int)(_frameDragStartHitbox.W + dmY), 4, (int)(SelectedFrame.Height - _frameDragStartHitbox.Y));
                    break;

                case FrameDragMode.ResizeBottomRight:
                    SelectedFrame.HitboxW = Math.Clamp((int)(_frameDragStartHitbox.Z + dmX), 4, (int)(SelectedFrame.Width - _frameDragStartHitbox.X));
                    SelectedFrame.HitboxH = Math.Clamp((int)(_frameDragStartHitbox.W + dmY), 4, (int)(SelectedFrame.Height - _frameDragStartHitbox.Y));
                    break;

                case FrameDragMode.Anchor:
                    float newAX = Math.Clamp(_frameDragStartAnchor.X + dmX / (SelectedFrame.Width), 0f, 1f);
                    float newAY = Math.Clamp(_frameDragStartAnchor.Y + dmY / (SelectedFrame.Height), 0f, 1f);
                    SelectedFrame.AnchorX = (float)Math.Round(newAX, 2);
                    SelectedFrame.AnchorY = (float)Math.Round(newAY, 2);
                    break;
            }
        }
        else if (_frameDragActive && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _frameDragActive = false;
        }

        ImGui.Separator();
    }

    private void RenderAnimationPreview()
    {
        if (SelectedSheet == null || SelectedSheet.FrameCount == 0) return;

        // Controls
        ImGui.Checkbox("Loop", ref _animLoop);
        ImGui.SameLine();
        int fpsInt = (int)_animFPS;
        ImGui.PushItemWidth(60);
        ImGui.InputInt("FPS", ref fpsInt);
        ImGui.PopItemWidth();
        _animFPS = Math.Clamp(fpsInt, 1, 120);
        ImGui.SameLine();
        ImGui.PushItemWidth(80);
        ImGui.SliderFloat("Zoom##anim", ref _animPreviewZoom, 1f, 8f, "%.1fx");
        ImGui.PopItemWidth();

        ImGui.PushItemWidth(80);
        ImGui.InputInt("Start", ref _animStartFrame);
        ImGui.SameLine();
        ImGui.InputInt("End", ref _animEndFrame);
        ImGui.PopItemWidth();

        _animStartFrame = Math.Clamp(_animStartFrame, 0, SelectedSheet.FrameCount - 1);
        _animEndFrame = Math.Clamp(_animEndFrame, _animStartFrame, SelectedSheet.FrameCount - 1);

        int totalFrames = _animEndFrame - _animStartFrame + 1;
        float duration = totalFrames / _animFPS;
        ImGui.Text($"{_animFPS} FPS | {totalFrames} frames | {duration:F2}s | 1/{_animFPS:F0}s per frame");

        // Play controls
        if (ImGui.Button(_animPlaying ? "Stop" : "Play"))
            _animPlaying = !_animPlaying;

        if (_animPlaying)
        {
            float dt = ImGui.GetIO().DeltaTime;
            _animTime += dt;
            float frameDuration = 1f / _animFPS;
            int frameCount = _animEndFrame - _animStartFrame + 1;
            int frame = (int)(_animTime / frameDuration);
            if (_animLoop)
                frame = ((frame % frameCount) + frameCount) % frameCount;
            else
                frame = Math.Min(frame, frameCount - 1);
            _currentPreviewFrame = _animStartFrame + frame;

            if (!_animLoop && frame >= frameCount - 1)
                _animPlaying = false;
        }

        // Draw preview frame
        ImGui.Separator();
        ImGui.Text($"Frame: {_currentPreviewFrame} / {SelectedSheet.FrameCount - 1}");

        float previewSize = 128f * _animPreviewZoom;
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Background
        drawList.AddRectFilled(cursorPos, new Vector2(cursorPos.X + previewSize, cursorPos.Y + previewSize),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.18f, 1f)), 4f);

        // Render actual frame image using UV coordinates
        if (!string.IsNullOrEmpty(SelectedSheet.ImagePath) && SelectedSheet.ImageWidth > 0 && SelectedSheet.ImageHeight > 0)
        {
            LoadPreviewTexture(SelectedSheet.ImagePath);
            if (_previewTextures.TryGetValue(Engine.Helpers.PathHelpers.ResolveProjectAsset(SelectedSheet.ImagePath), out uint texId) && texId != 0)
            {
                int cols = Math.Max(1, SelectedSheet.Columns);
                int frameIdx = Math.Clamp(_currentPreviewFrame, 0, SelectedSheet.FrameCount - 1);
                int row = frameIdx / cols;
                int col = frameIdx % cols;

                // Calculate UV coordinates for this specific frame
                float u0 = (SelectedSheet.OffsetX + col * (SelectedSheet.FrameWidth + SelectedSheet.PaddingX)) / (float)SelectedSheet.ImageWidth;
                float v0 = (SelectedSheet.OffsetY + row * (SelectedSheet.FrameHeight + SelectedSheet.PaddingY)) / (float)SelectedSheet.ImageHeight;
                float u1 = u0 + SelectedSheet.FrameWidth / (float)SelectedSheet.ImageWidth;
                float v1 = v0 + SelectedSheet.FrameHeight / (float)SelectedSheet.ImageHeight;

                // ── Normalized preview — WYSIWYG with the in-game viewport ──
                // Frame renders AS-IS (native px) centered + bottom-anchored in the
                // preview area; offsets are plain nudges (sheet base + this frame's
                // own Render Offset X/Y). The yellow outline is just a size reference.
                float cellW = MathF.Max(1, SelectedSheet.FrameWidth);
                float cellH = MathF.Max(1, SelectedSheet.FrameHeight);
                bool normalized = SelectedSheet.MasterHeight > 0f;
                float masterH = normalized ? SelectedSheet.MasterHeight : cellH;
                float masterW = normalized
                    ? (SelectedSheet.MasterWidth > 0f ? SelectedSheet.MasterWidth : masterH)
                    : cellW;
                float pxScale = previewSize / masterH;
                float dispW = cellW * pxScale;
                float dispH = cellH * pxScale;

                SpriteFrame? pfFrame = null;
                if (SelectedSheet.CustomFrames != null && frameIdx < SelectedSheet.CustomFrames.Count)
                    pfFrame = SelectedSheet.CustomFrames[frameIdx];
                float offXp = (SelectedSheet.SpriteOffsetX + (pfFrame?.RenderOffsetX ?? 0f)) * pxScale;
                // +Y offset = UP in world space → SUBTRACT in ImGui screen space.
                float offYp = (SelectedSheet.SpriteOffsetY + (pfFrame?.RenderOffsetY ?? 0f)) * pxScale;
                offYp = -offYp;

                // Frame as-is: centered, bottom on the ground line.
                float drawX = cursorPos.X + (previewSize - dispW) * 0.5f + offXp;
                float drawY = cursorPos.Y + (previewSize - dispH) + offYp;

                // Guides: cyan ground line at the box bottom + yellow master-box outline.
                uint groundCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.8f, 1f, 0.7f));
                drawList.AddLine(new Vector2(cursorPos.X, cursorPos.Y + previewSize),
                    new Vector2(cursorPos.X + previewSize, cursorPos.Y + previewSize), groundCol, 1f);
                if (normalized)
                {
                    uint masterCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 0.5f));
                    float boxWp = masterW * pxScale;
                    float bx0 = cursorPos.X + (previewSize - boxWp) * 0.5f;
                    drawList.AddRect(new Vector2(bx0, cursorPos.Y),
                        new Vector2(bx0 + boxWp, cursorPos.Y + previewSize), masterCol);
                }

                drawList.AddImage((nint)texId, new Vector2(drawX, drawY),
                    new Vector2(drawX + dispW, drawY + dispH), new Vector2(u0, v0), new Vector2(u1, v1));

                string frameLabel = normalized
                    ? $"Frame {_currentPreviewFrame} — master {masterW:0}×{masterH:0}px, frame {cellW:0}×{cellH:0}px"
                    : $"Frame {_currentPreviewFrame}";
                var textSize = ImGui.CalcTextSize(frameLabel);
                drawList.AddText(
                    new Vector2(cursorPos.X + 4, cursorPos.Y + 4),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.8f, 1f)), frameLabel);

                ImGui.Dummy(new Vector2(previewSize, previewSize));
                return;
            }
        }

        // Fallback: gray box
        string fallbackLabel = $"Frame {_currentPreviewFrame}";
        var fallbackSize = ImGui.CalcTextSize(fallbackLabel);
        drawList.AddText(
            new Vector2(cursorPos.X + 4, cursorPos.Y + previewSize - fallbackSize.Y - 4),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.8f, 1f)), fallbackLabel);
        ImGui.Dummy(new Vector2(previewSize, previewSize));
    }

    private void RenderAnimationClips()
    {
        if (_autoLoadedPatternClips.Count > 0)
            ImGui.TextDisabled($"({_autoLoadedPatternClips.Count} clips baked from *-sprite-anim.json)");
        if (SelectedSheet == null)
            ImGui.TextDisabled("Baked clips below remain available. Import a source sheet to create or replace clips.");
        else
        {
            // ── Create clip from current animation preview range ──
            ImGui.Text("Create from preview:");
            ImGui.SameLine();
            ImGui.PushItemWidth(120);
            ImGui.InputText("##clipname", ref _newClipName, 128);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            if (ImGui.Button("Create Clip"))
            {
                int start = Math.Clamp(_animStartFrame, 0, SelectedSheet.FrameCount - 1);
                int end = Math.Clamp(_animEndFrame, start, SelectedSheet.FrameCount - 1);
                int count = end - start + 1;

                var sourceClip = new AnimationClip2D
                {
                    Name = _newClipName,
                    SpriteSheetName = SelectedSheet.Name,
                    FrameIndices = Enumerable.Range(start, count).ToList(),
                    FPS = _animFPS,
                    Loop = _animLoop,
                    // Snapshot the sheet's master render box + base offsets NOW, so this
                    // clip always renders exactly as aligned here — even if the sheet's
                    // normalization is changed later (old clips keep their own box).
                    MasterWidth = SelectedSheet.MasterWidth,
                    MasterHeight = SelectedSheet.MasterHeight,
                    SpriteOffsetX = SelectedSheet.SpriteOffsetX,
                    SpriteOffsetY = SelectedSheet.SpriteOffsetY
                };
                if (TryBakeClip(SelectedSheet, sourceClip, out var bakedClip))
                {
                    _selectedClipIdx = AddOrReplaceClip(bakedClip);
                    _editingClip = bakedClip;
                    Console.WriteLine($"[SpriteEditor] Baked clip '{bakedClip.Name}' to {bakedClip.SpriteSheetName}: frames {start}-{end} ({count} frames, {bakedClip.FPS} FPS)");
                    RequestFullAtlasRepack("clip created");
                }
            }
        }

        ImGui.Separator();

        // ── Clip list ──
        if (AnimationClips.Count == 0)
        {
            ImGui.TextDisabled("No animation clips. Set Start/End frames above, then click 'Create Clip'.");
            return;
        }

        int deleteIdx = -1;
        for (int i = 0; i < AnimationClips.Count; i++)
        {
            var clip = AnimationClips[i];
            bool isSelected = i == _selectedClipIdx;
            string loopTag = clip.Loop ? " [loop]" : "";
            int start = clip.FrameIndices.Count > 0 ? clip.FrameIndices[0] : 0;
            int end = clip.FrameIndices.Count > 0 ? clip.FrameIndices[^1] : 0;
            string label = $"{clip.Name} ({start}-{end}, {clip.FrameIndices.Count}f @ {clip.FPS:F0}fps){loopTag}##{i}";

            ImGui.PushID(i);
            if (ImGui.SmallButton("X"))
            {
                deleteIdx = i;
                ImGui.PopID();
                continue;
            }
            ImGui.SameLine();
            if (ImGui.Selectable(label, isSelected))
                _selectedClipIdx = i;
            ImGui.PopID();
        }
        if (deleteIdx >= 0)
        {
            AnimationClips.RemoveAt(deleteIdx);
            _selectedClipIdx = Math.Min(_selectedClipIdx, AnimationClips.Count - 1);
            if (_editingClip != null && !AnimationClips.Contains(_editingClip))
            {
                _editingClip = null;
                _clipPlaying = false;
            }
            RequestFullAtlasRepack("clip deleted");
        }

        // ── Selected clip properties ──
        if (SelectedClip != null)
        {
            ImGui.Separator();
            ImGui.Text($"Editing: {SelectedClip.Name}");

            ImGui.InputText("Name##clipname", ref SelectedClip.Name, 128);

            float fps = SelectedClip.FPS;
            if (ImGui.SliderFloat("FPS##clip", ref fps, 1f, 60f, "%.0f", ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic))
            {
                fps = MathF.Round(fps); // whole-number steps only (per 1.0)
                SelectedClip.FPS = fps;
            }

            bool loop = SelectedClip.Loop;
            if (ImGui.Checkbox("Loop##clip", ref loop))
                SelectedClip.Loop = loop;

            bool reverse = SelectedClip.Reverse;
            if (ImGui.Checkbox("Reverse##clip", ref reverse))
                SelectedClip.Reverse = reverse;

            float speed = SelectedClip.SpeedMultiplier;
            if (ImGui.SliderFloat("Speed##clip", ref speed, 0.1f, 4f, "%.1fx"))
                SelectedClip.SpeedMultiplier = speed;

            // Frame range info
            int clipStart = SelectedClip.FrameIndices.Count > 0 ? SelectedClip.FrameIndices[0] : 0;
            int clipEnd = SelectedClip.FrameIndices.Count > 0 ? SelectedClip.FrameIndices[^1] : 0;
            ImGui.Text($"Range: frame {clipStart}-{clipEnd} | {SelectedClip.FrameIndices.Count} frames | {SelectedClip.Duration:F2}s");

            // Frame indices display
            ImGui.Text("Frames:");
            ImGui.SameLine();
            string frameList = string.Join(", ", SelectedClip.FrameIndices.Take(20));
            if (SelectedClip.FrameIndices.Count > 20) frameList += $"... (+{SelectedClip.FrameIndices.Count - 20})";
            ImGui.TextDisabled(frameList);

            // Play this clip in the editing preview
            ImGui.Separator();
            if (ImGui.Button("▶ Play in Preview"))
            {
                _editingClip = SelectedClip;
                _clipPlaying = true;
                _clipTime = 0f;
                _clipCurrentFrame = 0;

            }
        }

        // ── Clip Editing Preview (shows when a clip is playing) ──
        if (_editingClip != null && _editingClip.FrameIndices.Count > 0)
        {
            ImGui.Separator();
            RenderClipEditingPreview();
        }
    }

    private void RenderClipEditingPreview()
    {
        if (_editingClip == null) return;
        var clip = _editingClip;
        var previewSheet = _bakedAtlasSheets.FirstOrDefault(s => string.Equals(s.Name,
            clip.SpriteSheetName, StringComparison.OrdinalIgnoreCase));
        if (previewSheet == null || string.IsNullOrEmpty(previewSheet.ImagePath) ||
            !File.Exists(Engine.Helpers.PathHelpers.ResolveProjectAsset(previewSheet.ImagePath))) return;
        if (clip.FrameIndices.Count == 0) return;
        ImGui.Text($"  Clip: {clip.Name}  ");
        ImGui.SameLine();
        if (ImGui.SmallButton("X##closeclip"))
        {
            _editingClip = null;
            _clipPlaying = false;
            return;
        }

        // Playback controls
        if (ImGui.Button(_clipPlaying ? "|| Pause" : "> Play"))
            _clipPlaying = !_clipPlaying;
        ImGui.SameLine();
        if (ImGui.Button("|<"))
        {
            _clipCurrentFrame = 0;
            _clipTime = 0f;
        }
        ImGui.SameLine();
        if (ImGui.Button("<"))
        {
            _clipCurrentFrame = Math.Max(0, _clipCurrentFrame - 1);
            _clipTime = _clipCurrentFrame / (clip.FPS * clip.SpeedMultiplier);
        }
        ImGui.SameLine();
        if (ImGui.Button(">"))
        {
            _clipCurrentFrame = Math.Min(clip.FrameIndices.Count - 1, _clipCurrentFrame + 1);
            _clipTime = _clipCurrentFrame / (clip.FPS * clip.SpeedMultiplier);
        }
        ImGui.SameLine();
        if (ImGui.Button(">|"))
        {
            _clipCurrentFrame = clip.FrameIndices.Count - 1;
            _clipTime = _clipCurrentFrame / (clip.FPS * clip.SpeedMultiplier);
        }

        ImGui.SameLine();
        ImGui.PushItemWidth(80);
        ImGui.SliderFloat("Zoom##clippreview", ref _clipZoom, 1f, 8f, "%.1fx");
        ImGui.PopItemWidth();

        // Update playback
        if (_clipPlaying)
        {
            float dt = ImGui.GetIO().DeltaTime;
            _clipTime += dt * clip.SpeedMultiplier * (clip.Reverse ? -1f : 1f);
            float frameDuration = 1f / clip.FPS;
            int newFrame = (int)(_clipTime / frameDuration);
            if (clip.Loop)
                newFrame = ((newFrame % clip.FrameIndices.Count) + clip.FrameIndices.Count) % clip.FrameIndices.Count;
            else
                newFrame = Math.Clamp(newFrame, 0, clip.FrameIndices.Count - 1);
            _clipCurrentFrame = newFrame;
            if (!clip.Loop && _clipTime >= clip.Duration)
                _clipPlaying = false;
        }

        // Frame info
        int spriteFrameIdx = clip.FrameIndices[_clipCurrentFrame];
        ImGui.Text($"Frame {_clipCurrentFrame + 1}/{clip.FrameIndices.Count} | sprite #{spriteFrameIdx} | {clip.FPS:F0} FPS | {clip.Duration:F2}s");

        // Draw the frame
        LoadPreviewTexture(previewSheet.ImagePath);
        if (!_previewTextures.TryGetValue(Engine.Helpers.PathHelpers.ResolveProjectAsset(previewSheet.ImagePath), out uint texId) || texId == 0) return;

        var (uvMin, uvMax) = previewSheet.GetFrameUV(spriteFrameIdx);
        float u0 = uvMin.X;
        float v0 = 1f - uvMax.Y;
        float u1 = uvMax.X;
        float v1 = 1f - uvMin.Y;

        float previewSize = 128f * _clipZoom;

        // ── Normalized clip preview — SAME math as the Animation Preview & in-game ──
        // Box = the clip's snapshotted master box (falls back to the sheet's live
        // values when the clip has none); the frame renders AS-IS (native px),
        // feet on the ground line, nudged by the base offsets plus this sprite
        // frame's own Render Offset X/Y. So what you save here is what plays.
        float cellW = MathF.Max(1, previewSheet.FrameWidth);
        float cellH = MathF.Max(1, previewSheet.FrameHeight);
        SpriteFrame? clipFrame = null;
        if (previewSheet.CustomFrames != null && spriteFrameIdx < previewSheet.CustomFrames.Count)
        {
            clipFrame = previewSheet.CustomFrames[spriteFrameIdx];
            cellW = MathF.Max(1, clipFrame.Width);
            cellH = MathF.Max(1, clipFrame.Height);
        }
        bool normalized = clip.MasterHeight > 0f || previewSheet.MasterHeight > 0f;
        float clipMasterH = normalized
            ? (clip.MasterHeight > 0f ? clip.MasterHeight : previewSheet.MasterHeight)
            : cellH;
        float clipMasterW = normalized
            ? (clip.MasterWidth > 0f ? clip.MasterWidth
                : (previewSheet.MasterWidth > 0f ? previewSheet.MasterWidth : clipMasterH))
            : cellW;
        float clipBaseOffX = clip.MasterHeight > 0f ? clip.SpriteOffsetX : previewSheet.SpriteOffsetX;
        float clipBaseOffY = clip.MasterHeight > 0f ? clip.SpriteOffsetY : previewSheet.SpriteOffsetY;

        float pxScale = previewSize / clipMasterH;
        float dispW = cellW * pxScale;
        float dispH = cellH * pxScale;
        float clipOffX = (clipBaseOffX + (clipFrame?.RenderOffsetX ?? 0f)) * pxScale;
        // +Y offset = UP in world space → SUBTRACT in ImGui screen space (Y grows down).
        float clipOffY = -((clipBaseOffY + (clipFrame?.RenderOffsetY ?? 0f)) * pxScale);

        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        // Render AS-IS: frame at its native size, centered + bottom-anchored in the
        // preview area; offsets are plain nudges from there (same as the viewport).
        float drawX = cursorPos.X + (previewSize - dispW) * 0.5f + clipOffX;
        float drawY = cursorPos.Y + (previewSize - dispH) + clipOffY;

        // Checkerboard background over the whole master box
        uint ckA = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.35f, 0.35f, 1f));
        uint ckB = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.25f, 1f));
        int ck = 8;
        for (int cy = 0; cy < (int)previewSize; cy += ck)
            for (int cx = 0; cx < (int)previewSize; cx += ck)
            {
                bool isA = ((cx / ck) + (cy / ck)) % 2 == 0;
                drawList.AddRectFilled(
                    new Vector2(cursorPos.X + cx, cursorPos.Y + cy),
                    new Vector2(Math.Min(cursorPos.X + cx + ck, cursorPos.X + previewSize),
                                Math.Min(cursorPos.Y + cy + ck, cursorPos.Y + previewSize)),
                    isA ? ckA : ckB);
            }

        // Guides: cyan ground line + yellow master-box outline (when normalized).
        drawList.AddLine(new Vector2(cursorPos.X, cursorPos.Y + previewSize),
            new Vector2(cursorPos.X + previewSize, cursorPos.Y + previewSize),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.8f, 1f, 0.7f)), 1f);
        if (normalized)
            drawList.AddRect(cursorPos, new Vector2(cursorPos.X + previewSize, cursorPos.Y + previewSize),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 0.5f)));

        drawList.AddImage((nint)texId, new Vector2(drawX, drawY),
            new Vector2(drawX + dispW, drawY + dispH), new Vector2(u0, v0), new Vector2(u1, v1));
        ImGui.Dummy(new Vector2(previewSize, previewSize));

        // Frame timeline bar
        ImGui.Separator();
        float timelineW = ImGui.GetContentRegionAvail().X;
        float timelineH = 20f;
        var tlCursor = ImGui.GetCursorScreenPos();
        uint tlBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.2f, 1f));
        drawList.AddRectFilled(tlCursor, new Vector2(tlCursor.X + timelineW, tlCursor.Y + timelineH), tlBg, 3f);

        // Draw frame markers
        float frameW = timelineW / clip.FrameIndices.Count;
        for (int i = 0; i < clip.FrameIndices.Count; i++)
        {
            float fx = tlCursor.X + i * frameW;
            bool isActive = i == _clipCurrentFrame;
            uint markerColor = isActive
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.7f, 1f, 0.9f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.4f, 0.6f));
            drawList.AddRectFilled(
                new Vector2(fx + 1, tlCursor.Y + 2),
                new Vector2(fx + frameW - 1, tlCursor.Y + timelineH - 2),
                markerColor, 2f);

            if (frameW > 12f)
            {
                string num = $"{i}";
                var numSize = ImGui.CalcTextSize(num);
                if (numSize.X < frameW - 2)
                    drawList.AddText(new Vector2(fx + (frameW - numSize.X) * 0.5f, tlCursor.Y + 3),
                        ImGui.ColorConvertFloat4ToU32(Vector4.One), num);
            }
        }

        // Click on timeline to jump
        ImGui.InvisibleButton("##timeline", new Vector2(timelineW, timelineH));
        if (ImGui.IsItemClicked())
        {
            float clickX = ImGui.GetIO().MousePos.X - tlCursor.X;
            int clickFrame = Math.Clamp((int)(clickX / frameW), 0, clip.FrameIndices.Count - 1);
            _clipCurrentFrame = clickFrame;
            _clipTime = clickFrame / (clip.FPS * clip.SpeedMultiplier);
        }

        ImGui.Dummy(new Vector2(0, 4));
    }

    private void ImportNewSheet()
    {
        _importDialog.OpenForLoad("*.png;*.jpg", "Import Sprite Sheet");
    }

    // ── Save/Load ──

    /// <summary>Get the default save path for sprite sheets in the current project.</summary>
    private string GetDefaultSavePath()
    {
        string dir = Engine.Project.ProjectManager.IsProjectLoaded
            ? Path.Combine(Engine.Project.ProjectManager.ProjectRoot!, "Artifacts", "Sprites")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sprites");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "sprites.sheets.json");
    }

    /// <summary>Snapshot all editor state (sheets + clips + Animation Preview settings).
    /// Used by every save path so Save / Save As... never drop data.</summary>
    private SpriteSheetsSaveData CaptureSaveData()
    {
        var data = new SpriteSheetsSaveData
        {
            Sheets = _bakedAtlasSheets.Select(s => s.ToData()).ToList(),
            AnimationClips = AnimationClips.Select(c => c.ToData()).ToList(),
            PatternActions = _patternActions
        };
        // Persist the Animation Preview range (Start/End), FPS and Loop so they come
        // back exactly as the user left them after a reload.
        if (SelectedSheet != null && SelectedSheet.FrameCount > 0)
        {
            data.PreviewStartFrame = Math.Clamp(_animStartFrame, 0, SelectedSheet.FrameCount - 1);
            data.PreviewEndFrame = Math.Clamp(_animEndFrame, data.PreviewStartFrame, SelectedSheet.FrameCount - 1);
            data.PreviewFps = Math.Clamp(_animFPS, 1f, 120f);
            data.PreviewLoop = _animLoop;
        }
        return data;
    }

    private SpriteSheetsSaveData CaptureUndoData()
    {
        var data = CaptureSaveData();
        data.UndoSourceSheets = SpriteSheets.Select(s => s.ToData()).ToList();
        return data;
    }

    private string GetProjectSpriteDirectory()
    {
        if (!Engine.Project.ProjectManager.IsProjectLoaded ||
            string.IsNullOrEmpty(Engine.Project.ProjectManager.ProjectRoot))
            return "";
        return Path.Combine(Engine.Project.ProjectManager.ProjectRoot!, "Artifacts", "Sprites");
    }

    /// <summary>Copy the requested source cells and metadata into the baking input.
    /// Source sheets are editor-only: the returned buffers, not the source path, are
    /// what is written into the runtime atlas.</summary>
    private static List<BakedSpriteFrame>? ExtractBakedFrames(SpriteSheet source, IReadOnlyList<int> indices)
    {
        string path = Engine.Helpers.PathHelpers.ResolveProjectAsset(source.ImagePath);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Console.WriteLine($"[SpriteEditor] Source image is unavailable for sheet '{source.Name}'.");
            return null;
        }

        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var result = new List<BakedSpriteFrame>(indices.Count);
        foreach (int index in indices)
        {
            SpriteFrame frame;
            if (source.CustomFrames != null)
            {
                if (index < 0 || index >= source.CustomFrames.Count)
                    throw new InvalidDataException($"Frame index {index} is outside source sheet '{source.Name}'.");
                frame = source.CustomFrames[index];
            }
            else
            {
                if (index < 0 || index >= source.FrameCount)
                    throw new InvalidDataException($"Frame index {index} is outside source sheet '{source.Name}'.");
                int col = index % Math.Max(1, source.Columns);
                int row = index / Math.Max(1, source.Columns);
                frame = new SpriteFrame
                {
                    X = source.OffsetX + col * (source.FrameWidth + source.PaddingX),
                    Y = source.OffsetY + row * (source.FrameHeight + source.PaddingY),
                    Width = source.FrameWidth,
                    Height = source.FrameHeight,
                    Name = $"{source.Name}_{index}"
                };
            }

            if (frame.Width <= 0 || frame.Height <= 0 || frame.X < 0 || frame.Y < 0 ||
                frame.X + frame.Width > image.Width || frame.Y + frame.Height > image.Height)
                throw new InvalidDataException($"Frame {index} of '{source.Name}' is outside its source image.");

            byte[] pixels = new byte[checked(frame.Width * frame.Height * 4)];
            for (int y = 0; y < frame.Height; y++)
            {
                int sourceY = source.FlipY ? frame.Y + frame.Height - 1 - y : frame.Y + y;
                for (int x = 0; x < frame.Width; x++)
                {
                    int sourceX = source.FlipX ? frame.X + frame.Width - 1 - x : frame.X + x;
                    int src = (sourceY * image.Width + sourceX) * 4;
                    int dst = (y * frame.Width + x) * 4;
                    Buffer.BlockCopy(image.Data, src, pixels, dst, 4);
                }
            }
            result.Add(new BakedSpriteFrame { Pixels = pixels, Width = frame.Width, Height = frame.Height, Metadata = frame });
        }
        return result;
    }

    /// <summary>Bake a clip's selected source frames and return its runtime-ready
    /// atlas reference. A same-name clip is excluded from occupancy so its old slots
    /// can be reused rather than duplicated.</summary>
    private bool TryBakeClip(SpriteSheet source, AnimationClip2D clip, out AnimationClip2D baked)
    {
        baked = clip;
        string directory = GetProjectSpriteDirectory();
        if (directory.Length == 0)
        {
            Console.WriteLine("[SpriteEditor] Baking requires an open project.");
            return false;
        }
        try
        {
            var frames = ExtractBakedFrames(source, clip.FrameIndices);
            if (frames == null || frames.Count == 0) return false;
            var keep = AnimationClips.Where(c => !string.Equals(c.Name, clip.Name,
                StringComparison.OrdinalIgnoreCase)).ToList();
            var result = SpriteAtlasBaker.BakeClip(directory, clip.Name, frames,
                _bakedAtlasSheets, keep);
            if (result == null) return false;

            var atlas = _bakedAtlasSheets.FirstOrDefault(s => string.Equals(s.Name,
                result.Atlas.Name, StringComparison.OrdinalIgnoreCase));
            if (atlas == null) _bakedAtlasSheets.Add(result.Atlas);
            else if (!ReferenceEquals(atlas, result.Atlas))
                _bakedAtlasSheets[_bakedAtlasSheets.IndexOf(atlas)] = result.Atlas;

            baked = new AnimationClip2D
            {
                Name = clip.Name,
                SpriteSheetName = result.Atlas.Name,
                FrameIndices = result.FrameIndices,
                FPS = clip.FPS,
                Loop = clip.Loop,
                Reverse = clip.Reverse,
                SpeedMultiplier = clip.SpeedMultiplier,
                Events = clip.Events.ToDictionary(kv => kv.Key, kv => kv.Value),
                MasterWidth = clip.MasterWidth > 0f ? clip.MasterWidth : source.MasterWidth,
                MasterHeight = clip.MasterHeight > 0f ? clip.MasterHeight : source.MasterHeight,
                SpriteOffsetX = clip.MasterHeight > 0f ? clip.SpriteOffsetX : source.SpriteOffsetX,
                SpriteOffsetY = clip.MasterHeight > 0f ? clip.SpriteOffsetY : source.SpriteOffsetY
            };
            ReloadPreviewTexture(result.Atlas.ImagePath);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Baking clip '{clip.Name}' failed: {ex.Message}");
            return false;
        }
    }

    private int AddOrReplaceClip(AnimationClip2D clip)
    {
        int index = AnimationClips.FindIndex(c => string.Equals(c.Name, clip.Name,
            StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            AnimationClips.Add(clip);
            return AnimationClips.Count - 1;
        }
        AnimationClips[index] = clip;
        return index;
    }

    /// <summary>Save all sprite sheets to the default project location.</summary>
    public void SaveAllSheets()
    {
        string path = GetDefaultSavePath();
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var data = CaptureSaveData();
            string json = JsonSerializer.Serialize(data, opts);
            File.WriteAllText(path, json);
            Engine.Helpers.BinaryObjectCache.TryWrite(path, data);
            Console.WriteLine($"[SpriteEditor] Saved {SpriteSheets.Count} sheets + {AnimationClips.Count} clips to: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Save failed: {ex.Message}");
        }
    }

    // ── Save As JSON (path bebas) ──

    /// <summary>Open a Save dialog so the user can write the sheets + clips JSON
    /// to ANY folder/path they want (not just the default project location).</summary>
    private void SaveAsJsonDialog()
    {
        string defaultPath = GetDefaultSavePath();
        string? startDir = Path.GetDirectoryName(defaultPath);
        _saveAsJsonDialog.OpenForSave(
            defaultName: Path.GetFileName(defaultPath),
            filter: "*.json",
            title: "Save Sprite Sheets As JSON",
            startDir: startDir);
    }

    /// <summary>Write the JSON after the user confirms the Save As dialog.</summary>
    private void ProcessSaveAsJsonResult()
    {
        if (!_saveAsJsonDialog.IsConfirmed || _saveAsJsonDialog.SelectedPath == null) return;

        string path = _saveAsJsonDialog.SelectedPath;
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var data = CaptureSaveData();
            File.WriteAllText(path, JsonSerializer.Serialize(data, opts));
            Engine.Helpers.BinaryObjectCache.TryWrite(path, data);
            Console.WriteLine($"[SpriteEditor] Save As JSON: {SpriteSheets.Count} sheets + {AnimationClips.Count} clips -> {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Save As JSON failed: {ex.Message}");
        }
        _saveAsJsonDialog.Close();
    }

    // ── Save As Pattern (auto-clip per baris, batch per folder) ──

    /// <summary>Batch-generate animation clips for EVERY image file in the selected
    /// sheet's folder, using this sheet's pattern (grid + padding/offset + flip +
    /// master box). One clip per ROW; action names &amp; count come from the free-text
    /// "Pattern actions" field (default idle..dead) — rows past the action list are
    /// not saved. Empty frames (fully transparent / out of bounds) are skipped, FPS =
    /// frame count (one second per clip), clip name = &lt;sheet name&gt;-&lt;action&gt;.
    /// Result per file: &lt;file name&gt;-sprite-anim.json (same folder).</summary>
    private void SaveAsPattern()
    {
        if (SelectedSheet == null) return;
        var pattern = SelectedSheet;
        var actions = ParsePatternActions(_patternActions);

        string folder = "";
        try
        {
            if (string.IsNullOrEmpty(pattern.ImagePath))
            {
                Console.WriteLine($"[SpriteEditor] Save As Pattern: sheet '{pattern.Name}' has no source image.");
                return;
            }
            folder = Path.GetDirectoryName(Path.GetFullPath(Engine.Helpers.PathHelpers.ResolveProjectAsset(pattern.ImagePath)))!;
        }
        catch { folder = ""; }

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Console.WriteLine($"[SpriteEditor] Save As Pattern: folder not found for sheet '{pattern.Name}'.");
            return;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(folder)
                .Where(f => PatternImageExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Save As Pattern: cannot scan folder: {ex.Message}");
            return;
        }

        if (files.Length == 0)
        {
            Console.WriteLine($"[SpriteEditor] Save As Pattern: no image files in {folder}");
            return;
        }

        int saved = 0, totalClips = 0;
        foreach (string file in files)
        {
            try
            {
                var (sheetData, clips) = BuildPatternForFile(pattern, file, actions);
                if (clips.Count == 0)
                {
                    Console.WriteLine($"[SpriteEditor] Pattern skip {Path.GetFileName(file)}: all rows empty");
                    continue;
                }

                string outPath = Path.Combine(folder,
                    Path.GetFileNameWithoutExtension(file) + "-sprite-anim.json");
                var data = new SpriteSheetsSaveData
                {
                    Sheets = [sheetData],
                    AnimationClips = clips,
                    PatternActions = _patternActions
                };
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(outPath, JsonSerializer.Serialize(data, opts));
                Engine.Helpers.BinaryObjectCache.TryWrite(outPath, data);
                saved++;
                totalClips += clips.Count;
                Console.WriteLine($"[SpriteEditor] Pattern {Path.GetFileName(file)}: {clips.Count} clips -> {Path.GetFileName(outPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpriteEditor] Pattern failed for {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        Console.WriteLine($"[SpriteEditor] Save As Pattern: {saved}/{files.Length} files, {totalClips} clips -> {folder}");

        // Refresh the clip list right away — generated clips appear immediately.
        LoadPatternExports();
    }

    /// <summary>Parse the free-text action list: split on '|', trim, drop empties.
    /// Empty input falls back to the default list (idle..dead).</summary>
    private static string[] ParsePatternActions(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return PatternClipNames;
        var names = text.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
        return names.Length > 0 ? names : PatternClipNames;
    }

    /// <summary>Build one file's sheet (same pattern as the source sheet, incl. flips)
    /// + its per-row clips from the parsed action names. Frame emptiness comes from
    /// the image's alpha channel.</summary>
    private static (SpriteSheetData sheet, List<AnimationClip2DData> clips) BuildPatternForFile(
        SpriteSheet pattern, string imagePath, string[] actionNames)
    {
        ImageResult image;
        using (var stream = File.OpenRead(imagePath))
            image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        string name = Path.GetFileNameWithoutExtension(imagePath);
        var sheet = new SpriteSheet
        {
            Name = name,
            ImagePath = Tilemap2D.MakeDataPathRelative(imagePath),
            ImageWidth = image.Width,
            ImageHeight = image.Height,
            // Pattern = the source sheet's settings verbatim (grid + padding/offset
            // + master box + render offsets + flip), so every file slices identically.
            Columns = pattern.Columns,
            Rows = pattern.Rows,
            FrameWidth = pattern.FrameWidth,
            FrameHeight = pattern.FrameHeight,
            PaddingX = pattern.PaddingX,
            PaddingY = pattern.PaddingY,
            OffsetX = pattern.OffsetX,
            OffsetY = pattern.OffsetY,
            MasterWidth = pattern.MasterWidth,
            MasterHeight = pattern.MasterHeight,
            SpriteOffsetX = pattern.SpriteOffsetX,
            SpriteOffsetY = pattern.SpriteOffsetY,
            FlipX = pattern.FlipX,
            FlipY = pattern.FlipY
        };
        sheet.BakeUniformFrames();

        var clips = new List<AnimationClip2DData>();
        for (int row = 0; row < sheet.Rows && row < actionNames.Length; row++)
        {
            // Per-row detection: keep only non-empty frames, left to right.
            var indices = new List<int>();
            for (int col = 0; col < sheet.Columns; col++)
            {
                int x = sheet.OffsetX + col * (sheet.FrameWidth + sheet.PaddingX);
                int y = sheet.OffsetY + row * (sheet.FrameHeight + sheet.PaddingY);
                if (!IsFrameEmpty(image, x, y, sheet.FrameWidth, sheet.FrameHeight))
                    indices.Add(row * sheet.Columns + col);
            }
            if (indices.Count == 0) continue; // fully empty row -> no clip

            string clipName = actionNames[row];
            var clip = new AnimationClip2D
            {
                Name = $"{name}-{clipName}",
                SpriteSheetName = name,
                FrameIndices = indices,
                FPS = indices.Count,     // FPS = jumlah frame (clip = 1 detik)
                Loop = !PatternOnceActions.Contains(clipName, StringComparer.OrdinalIgnoreCase),
                // Snapshot like Create Clip: master box + offsets frozen with the clip.
                MasterWidth = sheet.MasterWidth,
                MasterHeight = sheet.MasterHeight,
                SpriteOffsetX = sheet.SpriteOffsetX,
                SpriteOffsetY = sheet.SpriteOffsetY
            };
            clips.Add(clip.ToData());
        }
        return (sheet.ToData(), clips);
    }

    /// <summary>A frame counts as EMPTY when every pixel in it is fully transparent
    /// (alpha 0), or when its rect lies outside the image. Note: JPEG has no alpha
    /// channel (decoded alpha = 255), so JPEG frames are never reported empty.</summary>
    private static bool IsFrameEmpty(ImageResult img, int x, int y, int w, int h)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(img.Width, x + w), y1 = Math.Min(img.Height, y + h);
        if (x1 <= x0 || y1 <= y0) return true;
        if (img.Data == null) return true;

        int stride = img.Width * 4; // ColorComponents.RedGreenBlueAlpha -> 4 Bpp
        byte[] data = img.Data;
        for (int py = y0; py < y1; py++)
        {
            int rowStart = py * stride;
            for (int px = x0; px < x1; px++)
                if (data[rowStart + px * 4 + 3] != 0)
                    return false;
        }
        return true;
    }

    // ── Auto-load *-sprite-anim.json (SHEET + Animation Clips) ──

    /// <summary>Merge every *-sprite-anim.json export (its SHEET entry AND its
    /// clips) into the panel so both appear WITHOUT a manual Load. Runs on project
    /// open, after a manual Load, and after Save As Pattern. Files are enumerated
    /// FIRST (a folder reachable only through an auto-loaded sheet's ImagePath stays
    /// reachable across refreshes), then the previous auto-loaded sets are dropped
    /// and re-merged — refresh-in-place. Names already present (manual sheets/clips,
    /// the sheets file) are kept as-is and never duplicated.</summary>
    private void LoadPatternExports()
    {
        // 1) Enumerate BEFORE removing: roots include auto-loaded sheet folders.
        var files = EnumeratePatternAnimFiles().ToList();

        // 2) Drop the previous auto-loaded sets so a re-run replaces, not duplicates.
        if (_autoLoadedPatternClips.Count > 0)
        {
            AnimationClips.RemoveAll(c => _autoLoadedPatternClips.Contains(c));
            _autoLoadedPatternClips.Clear();
        }

        int readFiles = 0, clipsBaked = 0;
        foreach (string file in files)
        {
            try
            {
                var data = Engine.Helpers.BinaryObjectCache.TryLoad<SpriteSheetsSaveData>(file)
                           ?? JsonSerializer.Deserialize<SpriteSheetsSaveData>(File.ReadAllText(file));
                if (data == null || (data.Sheets == null && data.AnimationClips == null)) continue;
                // Self-heal: a sidecar missed by TryLoad (stale v1 / layout mismatch)
                // is rewritten from the freshly parsed object — the NEXT load is a
                // binary hit instead of a JSON parse on every single load.
                if (!Engine.Helpers.BinaryObjectCache.LastLoadWasBinary)
                    Engine.Helpers.BinaryObjectCache.TryWrite(file, data);
                bool touched = false;
                var sourceSheets = (data.Sheets ?? [])
                    .Where(sd => !string.IsNullOrWhiteSpace(sd?.Name))
                    .Select(SpriteSheet.FromData)
                    .ToList();

                // Pattern-export sheets exist only as bake inputs. Each clip is
                // cropped from its original image and immediately moved to an atlas.
                foreach (var clipData in data.AnimationClips ?? [])
                {
                    if (string.IsNullOrEmpty(clipData?.Name)) continue;
                    var source = sourceSheets.FirstOrDefault(s => string.Equals(s.Name,
                        clipData.SpriteSheetName, StringComparison.OrdinalIgnoreCase))
                        ?? sourceSheets.FirstOrDefault();
                    if (source == null) continue;

                    var sourceClip = AnimationClip2D.FromData(clipData);
                    if (!TryBakeClip(source, sourceClip, out var bakedClip)) continue;
                    AddOrReplaceClip(bakedClip);
                    _autoLoadedPatternClips.Add(bakedClip);
                    clipsBaked++;
                    touched = true;
                }

                if (touched) readFiles++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpriteEditor] Pattern auto-load failed for {file}: {ex.Message}");
            }
        }

        // 3) Keep sheet selection + preview range valid after removals/additions.
        if (_selectedSheetIdx >= SpriteSheets.Count)
            _selectedSheetIdx = SpriteSheets.Count - 1;
        _multiSelectedSheets.RemoveWhere(i => i < 0 || i >= SpriteSheets.Count); // pattern refresh pruned sheets — drop dead indexes
        if (SelectedSheet != null)
        {
            LoadSheetSettings();
            if (SelectedSheet.FrameCount > 0)
            {
                _animStartFrame = Math.Clamp(_animStartFrame, 0, SelectedSheet.FrameCount - 1);
                _animEndFrame = Math.Clamp(_animEndFrame, _animStartFrame, SelectedSheet.FrameCount - 1);
            }
        }
        // Keep clip selection / edited clip valid after removals.
        _selectedClipIdx = AnimationClips.Count > 0
            ? Math.Clamp(_selectedClipIdx, 0, AnimationClips.Count - 1) : -1;
        if (_editingClip != null && !AnimationClips.Contains(_editingClip))
            _editingClip = null;

        if (clipsBaked > 0)
        {
            SaveAllSheets();
            Console.WriteLine($"[SpriteEditor] Baked {clipsBaked} clips from {readFiles} *-sprite-anim.json files into project atlases.");
        }
    }

    /// <summary>All *-sprite-anim.json files under the project's Assets folder (or the
    /// default Sprites folder without a project) plus each sheet's image folder —
    /// Save As Pattern writes its exports next to their source image.</summary>
    private IEnumerable<string> EnumeratePatternAnimFiles()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string root = Engine.Project.ProjectManager.IsProjectLoaded
                ? Path.Combine(Engine.Project.ProjectManager.ProjectRoot!, "Artifacts")
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sprites");
            if (Directory.Exists(root)) roots.Add(root);
        }
        catch { /* no usable root */ }

        foreach (var sheet in SpriteSheets)
        {
            try
            {
                if (string.IsNullOrEmpty(sheet.ImagePath)) continue;
                string? dir = Path.GetDirectoryName(Path.GetFullPath(Engine.Helpers.PathHelpers.ResolveProjectAsset(sheet.ImagePath)));
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) roots.Add(dir);
            }
            catch { /* skip broken path */ }
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rootDir in roots)
        {
            try
            {
                foreach (string f in Directory.EnumerateFiles(rootDir, "*-sprite-anim.json",
                             SearchOption.AllDirectories))
                    files.Add(f);
            }
            catch { /* unreadable folder — skip */ }
        }
        return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    private void ProcessLoadSheetsResult()
    {
        if (_loadSheetsDialog.IsConfirmed && _loadSheetsDialog.SelectedPath != null)
        {
            LoadSheetsFromFile(_loadSheetsDialog.SelectedPath);
            _loadSheetsDialog.Close();
            LoadPatternExports(); // manual Load wipes clips — re-merge pattern exports
        }
    }

    /// <summary>Open load dialog to choose a custom file. The filter accepts both the
    /// default sheets file and the generated &lt;file&gt;-sprite-anim.json exports.</summary>
    public void LoadAllSheetsDialog()
    {
        _loadSheetsDialog.OpenForLoad("*.sheets.json;*-sprite-anim.json", "Load Sprite Sheets");
    }

    private void LoadSheetsFromFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            // Binary sidecar fast path — skips the JSON parse when the sidecar is
            // fresh; any miss/staleness/corruption falls back to the parse below.
            var data = Engine.Helpers.BinaryObjectCache.TryLoad<SpriteSheetsSaveData>(path)
                       ?? JsonSerializer.Deserialize<SpriteSheetsSaveData>(json);
            if (data?.Sheets == null)
            {
                Console.WriteLine($"[SpriteEditor] Invalid save file: {path}");
                return;
            }
            // Self-heal a stale/missing sidecar after a successful JSON parse.
            if (!Engine.Helpers.BinaryObjectCache.LastLoadWasBinary)
                Engine.Helpers.BinaryObjectCache.TryWrite(path, data);

            SpriteSheets.Clear();
            _bakedAtlasSheets.Clear();
            var legacySources = new List<SpriteSheet>();
            bool normalMapsUpdated = false;
            foreach (var sheetData in data.Sheets)
            {
                var sheet = SpriteSheet.FromData(sheetData);
                if (IsBakedAtlas(sheet.Name))
                {
                    _bakedAtlasSheets.Add(sheet);
                    if (sheet.NormalMapVersion < SpriteAtlasBaker.CurrentNormalMapVersion)
                    {
                        string colorPath = Engine.Helpers.PathHelpers.ResolveProjectAsset(sheet.ImagePath);
                        string? spriteDirectory = Path.GetDirectoryName(colorPath);
                        if (!string.IsNullOrEmpty(spriteDirectory) && File.Exists(colorPath))
                        {
                            SpriteAtlasBaker.RegenerateNormalMap(spriteDirectory, sheet);
                            normalMapsUpdated = true;
                        }
                    }
                }
                else
                    legacySources.Add(sheet);
                if (!string.IsNullOrEmpty(sheet.ImagePath) && File.Exists(Engine.Helpers.PathHelpers.ResolveProjectAsset(sheet.ImagePath)))
                    LoadPreviewTexture(sheet.ImagePath);
            }
            SpriteSheets.AddRange(legacySources);
            AnimationClips.Clear();
            bool migratedLegacyData = false;
            bool allLegacyClipsMigrated = true;
            if (data.AnimationClips != null)
            {
                foreach (var clipData in data.AnimationClips)
                {
                    var clip = AnimationClip2D.FromData(clipData);
                    if (IsBakedAtlas(clip.SpriteSheetName))
                    {
                        AddOrReplaceClip(clip);
                        continue;
                    }

                    var source = legacySources.FirstOrDefault(s => string.Equals(s.Name,
                        clip.SpriteSheetName, StringComparison.OrdinalIgnoreCase));
                    if (source != null && TryBakeClip(source, clip, out var baked))
                    {
                        AddOrReplaceClip(baked);
                        migratedLegacyData = true;
                    }
                    else
                    {
                        // Keep legacy content available for manual repair if its source
                        // is missing or cannot fit; newly created clips are always baked.
                        AddOrReplaceClip(clip);
                        allLegacyClipsMigrated = false;
                    }
                }
            }
            ClearUndoHistory();
            _selectedSheetIdx = SpriteSheets.Count > 0 ? 0 : -1;
            _multiSelectedSheets.Clear(); // a loaded file = a fresh list — no stale extras
            _selectedFrameIdx = -1;
            _selectedClipIdx = -1;
            if (SelectedSheet != null) LoadSheetSettings();

            // Default preview range to the FULL first sheet before any saved values.
            _animStartFrame = 0;
            _animEndFrame = Math.Max(0, (SelectedSheet?.FrameCount ?? 1) - 1);
            _animTime = 0f;
            _currentPreviewFrame = 0;

            // Restore the last Animation Preview settings (Start/End/FPS/Loop).
            if (data.PreviewStartFrame >= 0 && SelectedSheet != null)
            {
                _animStartFrame = Math.Clamp(data.PreviewStartFrame, 0, Math.Max(0, SelectedSheet.FrameCount - 1));
                _animEndFrame = Math.Clamp(Math.Max(data.PreviewEndFrame, _animStartFrame),
                    _animStartFrame, Math.Max(0, SelectedSheet.FrameCount - 1));
                _animFPS = Math.Clamp(data.PreviewFps, 1f, 120f);
                _animLoop = data.PreviewLoop;
            }

            // Restore the free-text pattern action list when present
            // (older files without the field keep the default).
            if (!string.IsNullOrWhiteSpace(data.PatternActions))
                _patternActions = data.PatternActions;

            Console.WriteLine($"[SpriteEditor] Loaded {SpriteSheets.Count} temporary source sheets + {_bakedAtlasSheets.Count} baked atlases + {AnimationClips.Count} clips from: {path}");
            if ((migratedLegacyData || normalMapsUpdated) && allLegacyClipsMigrated &&
                string.Equals(Path.GetFullPath(path), Path.GetFullPath(GetDefaultSavePath()), StringComparison.OrdinalIgnoreCase))
                SaveAllSheets();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Load failed: {ex.Message}");
        }
    }

    /// <summary>Reset the sprite editor when the project changes (close/open).
    /// Auto-load sprite sheets + pattern exports from the project folder.</summary>
    public void OnProjectChanged(string? projectRoot)
    {
        SpriteSheets.Clear();
        _bakedAtlasSheets.Clear();
        AnimationClips.Clear();
        _autoLoadedPatternClips.Clear();
        _patternActions = DefaultPatternActions;
        _selectedSheetIdx = -1;
        _multiSelectedSheets.Clear();
        _selectedFrameIdx = -1;
        _selectedClipIdx = -1;
        _previewTextures.Clear();
        ClearUndoHistory();

        if (!string.IsNullOrEmpty(projectRoot))
        {
            string spriteDirectory = Path.Combine(projectRoot, "Artifacts", "Sprites");
            try { SpriteAtlasBaker.EnsureDefaultAtlas(spriteDirectory); }
            catch (Exception ex) { Console.WriteLine($"[SpriteEditor] Failed to create default atlas: {ex.Message}"); }
        }

        // On project open: load canonical sprites.sheets.json only.
        // Pattern exports (*-sprite-anim.json) are NOT auto-loaded here;
        // they stay button-triggered (Auto Load toolbar / Load dialog).
        string sheetsPath = GetDefaultSavePath();
        if (File.Exists(sheetsPath))
            LoadSheetsFromFile(sheetsPath);
    }

    /// <summary>Manual replacement of the removed project-open auto-load:
    /// canonical sprites.sheets.json + *-sprite-anim.json pattern-export merge,
    /// on demand. Same behavior the old OnProjectChanged auto-load had.</summary>
    public void AutoLoadProjectSheets()
    {
        if (!Engine.Project.ProjectManager.IsProjectLoaded)
        {
            Console.WriteLine("[SpriteEditor] Auto Load skipped: no project open");
            return;
        }
        string path = GetDefaultSavePath();
        if (File.Exists(path))
            LoadSheetsFromFile(path); // TryLoad sidecar di dalam (JSON fallback tetap utuh)
        else
            Console.WriteLine($"[SpriteEditor] Auto Load: no sheets file at {path}");
        LoadPatternExports(); // auto-load *-sprite-anim.json (sheet + clips)
    }

    /// <summary>Import a sprite sheet from a direct file path (used by drag-and-drop).</summary>
    private void ImportSpriteFromPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var sheet = new SpriteSheet
        {
            Name = Path.GetFileNameWithoutExtension(path),
            ImagePath = Tilemap2D.MakeDataPathRelative(path),
            Columns = _editCols,
            Rows = _editRows,
            FrameWidth = _editFrameW,
            FrameHeight = _editFrameH,
            PaddingX = _editPadX,
            PaddingY = _editPadY,
            OffsetX = _editOffsetX,
            OffsetY = _editOffsetY
        };

        // Auto-detect image dimensions and grid
        try
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            sheet.ImageWidth = image.Width;
            sheet.ImageHeight = image.Height;

            // Auto-detect frame size
            if (_editFrameW == 64 && _editFrameH == 64)
            {
                for (int frameSize = 128; frameSize >= 16; frameSize /= 2)
                {
                    if (image.Width % frameSize == 0 && image.Height % frameSize == 0)
                    {
                        sheet.FrameWidth = frameSize;
                        sheet.FrameHeight = frameSize;
                        sheet.AutoDetectGrid();
                        _editCols = sheet.Columns;
                        _editRows = sheet.Rows;
                        _editFrameW = sheet.FrameWidth;
                        _editFrameH = sheet.FrameHeight;
                        Console.WriteLine($"[SpriteEditor] Auto-detected: {sheet.Columns}x{sheet.Rows} frames @ {frameSize}px");
                        break;
                    }
                }
            }
            else
            {
                sheet.AutoDetectGrid();
                _editCols = sheet.Columns;
                _editRows = sheet.Rows;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Could not read image: {ex.Message}");
        }

        sheet.BakeUniformFrames();
        SpriteSheets.Add(sheet);
        _selectedSheetIdx = SpriteSheets.Count - 1;
        _multiSelectedSheets.Clear(); // a fresh import = a single selection
        LoadPreviewTexture(path);

        Console.WriteLine($"[SpriteEditor] Imported via drag: {sheet.Name} ({sheet.Columns}x{sheet.Rows} = {sheet.FrameCount} frames, {sheet.ImageWidth}x{sheet.ImageHeight}px)");
    }

    /// <summary>Process file dialog result after rendering.</summary>
    private unsafe void ProcessImportResult()
    {
        if (_importDialog.IsConfirmed && _importDialog.SelectedPath != null)
        {
            string path = _importDialog.SelectedPath;
            var sheet = new SpriteSheet
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                ImagePath = Tilemap2D.MakeDataPathRelative(path),
                Columns = _editCols,
                Rows = _editRows,
                FrameWidth = _editFrameW,
                FrameHeight = _editFrameH,
                PaddingX = _editPadX,
                PaddingY = _editPadY,
                OffsetX = _editOffsetX,
                OffsetY = _editOffsetY
            };

            // Auto-detect image dimensions and grid
            try
            {
                using var stream = File.OpenRead(path);
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                sheet.ImageWidth = image.Width;
                sheet.ImageHeight = image.Height;

                // Auto-detect: if frame size is default, try to detect
                if (_editFrameW == 64 && _editFrameH == 64)
                {
                    // Try common sizes
                    for (int frameSize = 128; frameSize >= 16; frameSize /= 2)
                    {
                        if (image.Width % frameSize == 0 && image.Height % frameSize == 0)
                        {
                            sheet.FrameWidth = frameSize;
                            sheet.FrameHeight = frameSize;
                            sheet.AutoDetectGrid();
                            _editCols = sheet.Columns;
                            _editRows = sheet.Rows;
                            _editFrameW = sheet.FrameWidth;
                            _editFrameH = sheet.FrameHeight;
                            Console.WriteLine($"[SpriteEditor] Auto-detected: {sheet.Columns}x{sheet.Rows} frames @ {frameSize}px");
                            break;
                        }
                    }
                }
                else
                {
                    sheet.AutoDetectGrid();
                    _editCols = sheet.Columns;
                    _editRows = sheet.Rows;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpriteEditor] Could not read image: {ex.Message}");
            }

            sheet.BakeUniformFrames();
            SpriteSheets.Add(sheet);
            _selectedSheetIdx = SpriteSheets.Count - 1;
            _multiSelectedSheets.Clear(); // a fresh import = a single selection
            _newSheetName = sheet.Name;
            // New sheet: Animation Preview defaults to the FULL frame range (End = count-1).
            _animStartFrame = 0;
            _animEndFrame = Math.Max(0, sheet.FrameCount - 1);
            _animTime = 0f;
            _currentPreviewFrame = 0;

            // Load preview texture
            LoadPreviewTexture(path);

            Console.WriteLine($"[SpriteEditor] Imported: {sheet.Name} ({sheet.Columns}x{sheet.Rows} = {sheet.FrameCount} frames, {sheet.ImageWidth}x{sheet.ImageHeight}px)");
        }
    }

    private unsafe void ReloadPreviewTexture(string path)
    {
        string resolved = Engine.Helpers.PathHelpers.ResolveProjectAsset(path);
        if (_previewTextures.Remove(resolved, out uint oldTexture) && oldTexture != 0)
            GL.DeleteTextures(1, &oldTexture);
        LoadPreviewTexture(resolved);
    }

    private unsafe void LoadPreviewTexture(string path)
    {
        // Relative stored paths resolve against project root / exe (fallbacks included
        // for legacy absolute saves); the cache keys on the RESOLVED path so hits match.
        path = Engine.Helpers.PathHelpers.ResolveProjectAsset(path);
        if (_previewTextures.ContainsKey(path)) return;
        try
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            uint texId;
            GL.GenTextures(1, &texId);
            GL.BindTexture(Const.GL_TEXTURE_2D, texId);
            fixed (byte* ptr = image.Data)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                              image.Width, image.Height, 0,
                              Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
            }
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            _previewTextures[path] = texId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteEditor] Failed to load preview texture: {ex.Message}");
        }
    }

    private static bool IsBakedAtlas(string? name)
    {
        const string prefix = "mainsprite";
        return !string.IsNullOrEmpty(name) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(prefix.Length), out int number) && number > 0;
    }

    /// <summary>All currently selected sheet indexes, ascending, always valid and
    /// always containing the primary `_selectedSheetIdx`. The one accessor the delete /
    /// re-categorize / hint paths use — keeps the multi-set and the primary in sync.</summary>
    private List<int> MultiSelectedSheetIndexes()
    {
        var result = new List<int>();
        if (_selectedSheetIdx < 0 || _selectedSheetIdx >= SpriteSheets.Count)
            return result;
        result.Add(_selectedSheetIdx);
        foreach (int i in _multiSelectedSheets)
            if (i >= 0 && i < SpriteSheets.Count && i != _selectedSheetIdx)
                result.Add(i);
        result.Sort();
        return result;
    }

    /// <summary>One Sprite Sheets list click (multi-select): plain click = select only
    /// this sheet; Ctrl+Click = toggle it in/out around the current primary; Shift+Click
    /// = extend the range from the previous primary to here (never shrinks — afterwards a
    /// plain click must NOT drag the old range along, see `_sheetRangeAnchored`). The
    /// clicked sheet always becomes the primary: Sheet Settings + the preview follow it
    /// exactly like a single-selection click always did.</summary>
    private void HandleSheetListClick(int i)
    {
        bool ctrl = ImGui.GetIO().KeyCtrl;
        bool shift = ImGui.GetIO().KeyShift;
        int previousPrimary = _selectedSheetIdx;

        if (shift && previousPrimary >= 0 && previousPrimary < SpriteSheets.Count)
        {
            _selectedSheetIdx = i;
            AddMultiSelectedSheetRange(previousPrimary, i);
            _sheetRangeAnchored = true;
        }
        else if (ctrl)
        {
            _selectedSheetIdx = i;
            if (!_multiSelectedSheets.Add(i))
            {
                // Already in the set: if this was the PRIMARY, the click removes it from
                // the selection — promote another member so Sheet Settings keep a sheet.
                _multiSelectedSheets.Remove(i);
                _selectedSheetIdx = _multiSelectedSheets.Count > 0 ? _multiSelectedSheets.Min() : i;
                if (_selectedSheetIdx != i)
                    _multiSelectedSheets.Add(i); // demote the old primary to a normal member
            }
            _sheetRangeAnchored = false;
        }
        else
        {
            // Plain click: previously a Shift+Click ran → EXTEND (keep the range); the
            // plain click right after a plain click (anchor down) = fresh single select.
            if (_sheetRangeAnchored)
            {
                _selectedSheetIdx = i;
                _multiSelectedSheets.Add(i);
                _sheetRangeAnchored = false;
            }
            else
            {
                _multiSelectedSheets.Clear();
                _selectedSheetIdx = i;
            }
        }

        _selectedFrameIdx = -1;
        LoadSheetSettings();
        // Reset the Animation Preview range to this sheet's frame count
        // (End = count-1) — the previous sheet's range is invalid here.
        _animStartFrame = 0;
        _animEndFrame = Math.Max(0, SpriteSheets[i].FrameCount - 1);
        _animTime = 0f;
        _currentPreviewFrame = 0;
        if (_selectedClipIdx >= AnimationClips.Count)
            _selectedClipIdx = AnimationClips.Count - 1;
    }

    /// <summary>Add every index between a and b (inclusive) to the multi-selection set
    /// without touching the primary. Works across sections — a range is a list range.</summary>
    private void AddMultiSelectedSheetRange(int a, int b)
    {
        int lo = Math.Min(a, b), hi = Math.Max(a, b);
        for (int k = lo; k <= hi && k < SpriteSheets.Count; k++)
            _multiSelectedSheets.Add(k);
    }

    private void LoadSheetSettings()
    {
        if (SelectedSheet == null) return;
        _newSheetName = SelectedSheet.Name;
        _editCols = SelectedSheet.Columns;
        _editRows = SelectedSheet.Rows;
        _editFrameW = SelectedSheet.FrameWidth;
        _editFrameH = SelectedSheet.FrameHeight;
        _editPadX = SelectedSheet.PaddingX;
        _editPadY = SelectedSheet.PaddingY;
        _editOffsetX = SelectedSheet.OffsetX;
        _editOffsetY = SelectedSheet.OffsetY;
    }
}

/// <summary>Top-level save data for all sprite sheets.</summary>
public class SpriteSheetsSaveData
{
    public List<SpriteSheetData> Sheets { get; set; } = new();
    public List<AnimationClip2DData> AnimationClips { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<SpriteSheetData>? UndoSourceSheets { get; set; }

    // ── Last-used Animation Preview settings (Start/End/FPS/Loop) ──
    /// <summary>Saved so the preview range the user left is restored after a reload.
    /// -1 = never saved (keep defaults).</summary>
    public int PreviewStartFrame { get; set; } = -1;
    public int PreviewEndFrame { get; set; } = -1;
    public float PreviewFps { get; set; } = 12f;
    public bool PreviewLoop { get; set; } = true;

    /// <summary>Free-text action list for Save As Pattern (pipe-separated, one name
    /// per row). Files saved before this field existed keep the default.</summary>
    public string PatternActions { get; set; } = SpriteEditorPanel.DefaultPatternActions;
}
