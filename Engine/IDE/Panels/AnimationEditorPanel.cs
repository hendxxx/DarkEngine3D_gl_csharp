using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Animation workspace (Phase 1): one place to browse, create, edit, preview and bake
/// the sprite clips that currently live as a flat list in the Sprite Editor.
///
/// The runtime contract is unchanged — clips are still baked atlas frame strips. This
/// panel edits the AUTHORING side (source frame strip, holds, events, groups) and uses
/// the Sprite Editor's bake/undo/save plumbing so both panels share one state.
///
/// Layout: groups + clips on the left, live preview + timeline + properties on the right,
/// plus a full-width Library dashboard tab (search/filter/sort/bulk bake).
/// </summary>
public class AnimationEditorPanel
{
    private readonly SpriteEditorPanel _spriteEditor;
    private bool _visible = false;

    // ── Open-at-clip request (Asset Browser / Inspector navigation) ──
    /// <summary>Consumed next Render: focus the window and select the Workspace tab so
    /// the requested clip is actually visible, not hidden behind the Library tab.</summary>
    private bool _focusWindow;
    private bool _focusWorkspaceTab;
    /// <summary>Active page: 0 = Workspace (tree + preview + timeline), 1 = Library.
    /// Two buttons instead of a tab bar because ImGui.NET's only flagged BeginTabItem
    /// overload requires a close-button bool, and navigation must select Workspace.</summary>
    private int _tabMode;
    /// <summary>Set by OpenAtClip: the clip was requested by a navigation entry point,
    /// so playback starts instead of waiting for a Play click. Consumed on rebind.</summary>
    private bool _autoplayOnBind;

    // ── Selection / filters ──
    private int _clipIdx = -1;
    private string _search = "";
    private string _groupFilter = "";
    private bool _dirtyOnly;

    // ── Preview / playback ──
    private AnimationClip2D? _boundClip;
    private bool _playing;
    private float _time;
    private int _cursor;
    private float _previewZoom = 2f;

    // ── Name editing (committed on Enter/blur, never per keystroke) ──
    private string _nameBuffer = "";
    private AnimationClip2D? _nameBufferClip;
    private bool _nameNotice;
    /// <summary>Previous name of the clip just renamed — the reference scan looks for it
    /// ("Update references" rewrites scenes/items still pointing at it).</summary>
    private string _pendingRenameOld = "";

    // ── Library sort ──
    private int _libSortCol;          // 0=Clip 1=Group 2=Sheet 3=FPS 4=Frames 5=Duration 6=Loop 7=State 8=Mode 9=Refs
    private bool _libSortAsc = true;

    // ── Preview Loadout (drag equipment onto the preview) ──
    /// <summary>Drag payloads follow the AssetBrowser pattern: an empty payload marks
    /// the drag, the static field carries the data to whichever target accepts it.</summary>
    private static string? _dragLoadoutItemId;
    private static string? _dragLoadoutSheetName;
    /// <summary>Session-only loadout for UNGROUPED clips (no group to persist it on).</summary>
    private readonly List<PreviewLoadoutEntry> _ungroupedLoadout = new();
    private string _loadoutFilter = "";

    // ── Frame editing ──
    private int _dragFrameFrom = -1;
    private int _insertFrameIndex;
    private string _eventBuffer = "";
    private int _eventFrame = -1;
    private bool _eventEditing;

    // ── Dialogs ──
    private bool _openNewGroup;
    private string _newGroupPath = "New Group";

    private bool _openRenameGroup;
    private string _renameGroupOld = "";
    private string _renameGroupNew = "";

    private bool _openNewClip;
    private string _newClipName = "New Clip";
    private int _newClipSourceIdx = -1;
    private int _newClipStart;
    private int _newClipEnd;
    private float _newClipFps = 10f;
    private bool _newClipLoop = true;
    private bool _newClipBake = true;

    private bool _openNewSet;
    private bool _setInitialized;
    private int _setSourceIdx = -1;
    private string _setGroup = "";
    private bool _setBake = true;
    private readonly List<SetRow> _setRows = new();

    private sealed class SetRow
    {
        public bool Enabled = true;
        public string Action = "idle";
        public string Label = "Idle";
        public int Start;
        public int End;
        public float Fps = 8f;
        public bool Loop = true;
        public string EventName = "";

        public SetRow() { }

        public SetRow(AnimationSetPreset p)
        {
            Action = p.Action;
            Label = p.Label;
            Fps = p.Fps;
            Loop = p.Loop;
            EventName = p.EventName ?? "";
        }
    }

    // ── Status line ──
    private string _status = "";
    private float _statusTime;

    private static readonly Vector4 ColText = new(0.92f, 0.92f, 0.96f, 1f);
    private static readonly Vector4 ColDim = new(0.55f, 0.55f, 0.62f, 1f);
    private static readonly Vector4 ColGreen = new(0.35f, 0.9f, 0.4f, 1f);
    private static readonly Vector4 ColAmber = new(1f, 0.75f, 0.25f, 1f);

    public AnimationEditorPanel(SpriteEditorPanel spriteEditor)
    {
        _spriteEditor = spriteEditor;
    }

    public void ShowInMenu() => ImGui.MenuItem("Animation Editor", null, ref _visible);

    /// <summary>Opens the panel (Sprite Editor's "Open Animation Editor" shortcut).</summary>
    public void Show() => _visible = true;

    /// <summary>Open the editor AT a clip — the single navigation entry point used by
    /// the Asset Browser clip boxes, the Inspector's clip combo and Player2D action rows.
    /// Filters are cleared when they would hide the clip (the tree shows the clip with its
    /// group context), and a missing clip reports instead of silently selecting another.
    /// </summary>
    public void OpenAtClip(string sheetName, string clipName)
    {
        _visible = true;
        _focusWindow = true;

        int idx = FindClipIndex(sheetName, clipName);
        if (idx < 0)
        {
            Status(string.IsNullOrEmpty(sheetName)
                ? $"Clip '{clipName}' not found."
                : $"Clip '{clipName}' not found on sheet '{sheetName}'.");
            return;
        }

        var clip = _spriteEditor.AnimationClips[idx];
        // Clear every filter that would hide the row we just navigated to.
        if (!ClipMatches(clip))
        {
            _search = "";
            _groupFilter = "";
            _dirtyOnly = false;
        }

        SelectClip(idx);
        _autoplayOnBind = true;
        _focusWorkspaceTab = true;
        Status(string.IsNullOrEmpty(clip.GroupPath)
            ? $"Opened '{clip.Name}' (ungrouped)"
            : $"Opened '{clip.Name}' in '{clip.GroupPath}'");
    }

    /// <summary>Index of the requested clip. Clips reference their SOURCE sheet; baked clips
    /// fall back to their atlas name. When the sheet does not match anything (renamed sheet,
    /// stale reference) a name-only match still navigates — better than a dead end.</summary>
    private int FindClipIndex(string sheetName, string clipName)
    {
        int nameOnly = -1;
        for (int i = 0; i < _spriteEditor.AnimationClips.Count; i++)
        {
            var c = _spriteEditor.AnimationClips[i];
            if (!string.Equals(c.Name, clipName, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(sheetName)) return i;
            if (string.Equals(c.SourceSheetName, sheetName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.SpriteSheetName, sheetName, StringComparison.OrdinalIgnoreCase))
                return i;
            if (nameOnly < 0) nameOnly = i;
        }
        return nameOnly;
    }

    public void Render()
    {
        if (!_visible) return;

        // Snapshot undo also works while the Sprite Editor is hidden (its Render returns
        // before BeginUndoCapture when closed). If it IS visible, its own capture owns
        // the frame and ours is skipped.
        bool ownCapture = !_spriteEditor.UndoCaptureActive;
        if (ownCapture) _spriteEditor.BeginUndoCapturePublic();

        if (_statusTime > 0f) _statusTime -= ImGui.GetIO().DeltaTime;

        // Navigation requests must surface the panel, not just select a hidden row.
        if (_focusWindow)
        {
            ImGui.SetNextWindowFocus();
            _focusWindow = false;
        }
        if (_focusWorkspaceTab)
        {
            _tabMode = 0;
            _focusWorkspaceTab = false;
        }

        ImGui.SetNextWindowSize(new Vector2(1120, 740), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Animation Editor", ref _visible))
        {
            IDE.PanelFocus.Notify("Animation Editor");
            ClampSelection();
            DrawToolbar();

            // Page switch (Workspace | Library). DrawWorkspace owns the tree + preview +
            // timeline; DrawLibrary owns the dashboard (it used to be a tab bar).
            ImGui.PushStyleColor(ImGuiCol.Button,
                _tabMode == 0 ? new Vector4(0.24f, 0.34f, 0.52f, 1f) : new Vector4(0.16f, 0.16f, 0.2f, 1f));
            if (ImGui.Button("Workspace", new Vector2(110, 0))) _tabMode = 0;
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button,
                _tabMode == 1 ? new Vector4(0.24f, 0.34f, 0.52f, 1f) : new Vector4(0.16f, 0.16f, 0.2f, 1f));
            if (ImGui.Button("Library", new Vector2(110, 0))) _tabMode = 1;
            ImGui.PopStyleColor();
            ImGui.Separator();

            if (_tabMode == 0) DrawWorkspace();
            else DrawLibrary();

            // Dialogs live in the panel's window context: opening a popup outside any
            // window (after ImGui.End) is invalid and the modal never appears.
            DrawDialogs();
        }
        ImGui.End();

        if (ownCapture) _spriteEditor.EndUndoCapturePublic();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Shared state helpers
    // ═══════════════════════════════════════════════════════════════

    private AnimationClip2D? SelectedClip
        => _clipIdx >= 0 && _clipIdx < _spriteEditor.AnimationClips.Count
            ? _spriteEditor.AnimationClips[_clipIdx] : null;

    private void ClampSelection()
    {
        int n = _spriteEditor.AnimationClips.Count;
        if (n == 0) { _clipIdx = -1; _boundClip = null; return; }
        _clipIdx = Math.Clamp(_clipIdx, 0, n - 1);
        _spriteEditor.SelectedClipIndexPublic = _clipIdx;
    }

    private void SelectClip(int index)
    {
        if (index < 0 || index >= _spriteEditor.AnimationClips.Count) return;
        _clipIdx = index;
        _spriteEditor.SelectedClipIndexPublic = index;
        _boundClip = null; // rebind playback state next frame
    }

    private void Status(string text)
    {
        _status = text;
        _statusTime = 6f;
        Console.WriteLine($"[AnimationEditor] {text}");
    }

    private bool ClipMatches(AnimationClip2D clip, bool respectGroupFilter = true)
    {
        if (_dirtyOnly && !clip.BakeDirty) return false;
        if (respectGroupFilter && !string.IsNullOrEmpty(_groupFilter))
        {
            bool inGroup = IsInGroup(clip.GroupPath, _groupFilter);
            if (!inGroup) return false;
        }
        if (!string.IsNullOrWhiteSpace(_search))
        {
            string s = _search.Trim();
            bool hit = clip.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                || clip.GroupPath.Contains(s, StringComparison.OrdinalIgnoreCase)
                || clip.SpriteSheetName.Contains(s, StringComparison.OrdinalIgnoreCase);
            if (!hit) return false;
        }
        return true;
    }

    private List<string> GroupPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in _spriteEditor.AnimationGroups) AddWithParents(set, g.Path);
        foreach (var c in _spriteEditor.AnimationClips) AddWithParents(set, c.GroupPath);
        return set.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Adds a path and every implied parent ("Player/Hero/Idle" also yields
    /// "Player" and "Player/Hero") so nested groups render as a tree even when only the
    /// leaf path was ever stored.</summary>
    private static void AddWithParents(HashSet<string> set, string path)
    {
        string p = (path ?? "").Trim().Trim('/');
        while (p.Length > 0)
        {
            set.Add(p);
            int slash = p.LastIndexOf('/');
            p = slash > 0 ? p[..slash] : "";
        }
    }

    private static bool IsInGroup(string groupPath, string root)
        => string.Equals(groupPath, root, StringComparison.OrdinalIgnoreCase)
        || groupPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    private static string GroupLeaf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static int GroupDepth(string path)
    {
        int depth = 0;
        foreach (char ch in path) if (ch == '/') depth++;
        return depth;
    }

    private static List<int> EditableFrames(AnimationClip2D clip)
        => clip.SourceFrameIndices.Count > 0 ? clip.SourceFrameIndices : clip.FrameIndices;

    private static float HoldOf(AnimationClip2D clip, int localFrame)
        => localFrame >= 0 && localFrame < clip.FrameHolds.Count
            ? MathF.Max(0.05f, clip.FrameHolds[localFrame]) : 1f;

    private static float DurationFor(AnimationClip2D clip, int count)
    {
        if (count <= 0) return 0f;
        float frame = 1f / MathF.Max(0.0001f, clip.FPS * MathF.Max(0.0001f, clip.SpeedMultiplier));
        float total = 0f;
        for (int i = 0; i < count; i++) total += frame * HoldOf(clip, i);
        return total;
    }

    private static int LocalFrameAt(AnimationClip2D clip, int count, float time, bool loop)
    {
        if (count <= 0) return 0;
        float frame = 1f / MathF.Max(0.0001f, clip.FPS * MathF.Max(0.0001f, clip.SpeedMultiplier));
        float total = DurationFor(clip, count);
        if (loop)
        {
            if (total <= 0f) return 0;
            time %= total;
            if (time < 0f) time += total;
        }
        else time = Math.Clamp(time, 0f, MathF.Max(0f, total - 0.00001f));

        float acc = 0f;
        for (int i = 0; i < count; i++)
        {
            acc += frame * HoldOf(clip, i);
            if (time < acc) return i;
        }
        return count - 1;
    }

    private static float FrameStartTime(AnimationClip2D clip, int localFrame)
    {
        float frame = 1f / MathF.Max(0.0001f, clip.FPS * MathF.Max(0.0001f, clip.SpeedMultiplier));
        float t = 0f;
        for (int i = 0; i < localFrame; i++) t += frame * HoldOf(clip, i);
        return t;
    }

    private static void EnsureHolds(AnimationClip2D clip, int count)
    {
        while (clip.FrameHolds.Count < count) clip.FrameHolds.Add(1f);
        if (clip.FrameHolds.Count > count) clip.FrameHolds.RemoveRange(count, clip.FrameHolds.Count - count);
    }

    private void MarkEdited(AnimationClip2D clip, bool frameListChanged)
    {
        if (frameListChanged && clip.SourceFrameIndices.Count > 0)
            clip.BakeDirty = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Toolbar
    // ═══════════════════════════════════════════════════════════════

    private void DrawToolbar()
    {
        ImGui.PushItemWidth(160);
        ImGui.InputText("##animsearch", ref _search, 128);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.TextDisabled("Search");

        ImGui.SameLine();
        if (ImGui.Button("+ Clip")) OpenNewClipDialog();
        ImGui.SameLine();
        if (ImGui.Button("New Set")) OpenNewSetDialog();
        ImGui.SameLine();
        if (ImGui.Button("+ Group")) { _newGroupPath = "New Group"; _openNewGroup = true; }
        ImGui.SameLine();
        if (ImGui.Button("Save")) { _spriteEditor.SaveAllSheets(); Status("Saved clips + groups to sprites.sheets.json"); }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        var clip = SelectedClip;
        ImGui.BeginDisabled(clip == null || !clip.CanRebake);
        if (ImGui.Button("Bake")) { if (clip != null) BakeClip(clip); }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Bake the selected clip's authored frame strip into its atlas.\nNeeds a source sheet (clips created here always carry one).");
        ImGui.SameLine();
        int dirty = _spriteEditor.AnimationClips.Count(c => c.BakeDirty && c.CanRebake);
        ImGui.BeginDisabled(dirty == 0);
        if (ImGui.Button($"Bake Dirty ({dirty})")) BakeAllDirty();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!_spriteEditor.CanUndo);
        if (ImGui.Button("Undo")) _spriteEditor.UndoPublic();
        ImGui.SameLine();
        if (ImGui.Button("Redo")) _spriteEditor.RedoPublic();
        ImGui.EndDisabled();

        if (_statusTime > 0f && !string.IsNullOrEmpty(_status))
        {
            ImGui.SameLine();
            ImGui.TextColored(ColGreen, _status);
        }

        // Quick actions: one click per standard action, built from the CURRENT preview
        // range on the selected source sheet with the preset's timing/event.
        ImGui.TextDisabled("Quick (from the current preview range):");
        for (int i = 0; i < AnimationSetPreset.Defaults.Length && i < 5; i++)
        {
            var preset = AnimationSetPreset.Defaults[i];
            ImGui.SameLine();
            if (ImGui.SmallButton($"+{preset.Label}##quick"))
                CreateQuickClip(preset);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Create '{preset.Action}' from the current preview range on the selected source sheet.\n"
                    + $"Default: {(preset.Loop ? "loop" : "once, holds the last frame")}, {preset.Fps:0} fps"
                    + (string.IsNullOrWhiteSpace(preset.EventName) ? "." : $", '{preset.EventName}' event.")
                    + "\nAn existing <sheet>-<action> clip is selected, never overwritten.");
        }

        ImGui.Separator();
    }

    /// <summary>Toolbar quick action: create the preset action from the current preview
    /// range on the selected source sheet (or the selected clip's source sheet). An
    /// existing `&lt;sheet&gt;-&lt;action&gt;` clip is SELECTED instead of overwritten — a second
    /// click must never destroy authored frames/holds/events.
    /// </summary>
    private void CreateQuickClip(AnimationSetPreset preset)
    {
        var source = _spriteEditor.SelectedSourceSheetPublic;
        if (source == null && SelectedClip is { } current)
            source = _spriteEditor.FindSourceSheetPublic(current.SourceSheetName);
        if (source == null && _spriteEditor.SpriteSheets.Count > 0)
            source = _spriteEditor.SpriteSheets[0];
        if (source == null)
        {
            Status("No source sheet — import an image in the Sprite Editor first.");
            return;
        }

        string name = $"{source.Name}-{preset.Action}";
        int existing = FindClipIndex(source.Name, name);
        if (existing >= 0)
        {
            SelectClip(existing);
            Status($"'{name}' already exists — selected (edit its strip in the timeline).");
            return;
        }

        var range = _spriteEditor.PreviewRangePublic;
        int max = Math.Max(0, source.FrameCount - 1);
        int start = Math.Clamp(range.Start, 0, max);
        int end = Math.Clamp(Math.Max(range.End, start), start, max);
        var frames = Enumerable.Range(start, end - start + 1).ToList();
        string group = string.IsNullOrEmpty(_groupFilter) ? source.Name : _groupFilter;
        var clip = BuildClip(source, name, frames, preset.Fps, preset.Loop, group,
            preset.EventName, preset.EventAtPercent);
        int idx = AddClip(clip, source, bake: true);
        SelectClip(idx);
        _spriteEditor.ForceSynchronizeRegistry();
        _spriteEditor.RequestAtlasRepackForEditor($"quick {preset.Label}");
        bool baked = idx >= 0 && idx < _spriteEditor.AnimationClips.Count
            && !_spriteEditor.AnimationClips[idx].BakeDirty;
        Status($"Created '{name}' ({frames.Count} frames, {preset.Fps:0} fps"
            + (preset.Loop ? ", loop" : ", once") + $"){(baked ? " and baked" : " — bake pending")}.");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Workspace: groups | preview + timeline + properties
    // ═══════════════════════════════════════════════════════════════

    private void DrawWorkspace()
    {
        float leftW = 270f;
        ImGui.BeginChild("##anim_left", new Vector2(leftW, 0), ImGuiChildFlags.Borders);
        DrawGroupTree();
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##anim_main", new Vector2(0, 0), ImGuiChildFlags.None);
        var clip = SelectedClip;
        if (clip == null)
        {
            ImGui.TextDisabled("Select a clip in the tree, or create one with + Clip / New Set.");
            ImGui.Spacing();
            ImGui.TextWrapped("Tip: pick a source sheet in the Sprite Editor first — New Set then pre-fills Idle/Walk/Run/Attack/Dead from that sheet's rows.");
        }
        else
        {
            DrawPreview(clip);
            ImGui.Separator();
            DrawTimeline(clip);
            ImGui.Separator();
            DrawClipProperties(clip);
            ImGui.Separator();
            DrawLoadoutSection(clip);
        }
        ImGui.EndChild();
    }

    private void DrawGroupTree()
    {
        ImGui.TextDisabled("ANIMATION TREE");

        if (ImGui.BeginCombo("##groupfilter", string.IsNullOrEmpty(_groupFilter) ? "All groups" : _groupFilter))
        {
            if (ImGui.Selectable("All groups", string.IsNullOrEmpty(_groupFilter))) _groupFilter = "";
            foreach (string path in GroupPaths())
                if (ImGui.Selectable(path, string.Equals(_groupFilter, path, StringComparison.OrdinalIgnoreCase)))
                    _groupFilter = path;
            ImGui.EndCombo();
        }

        bool dirty = _dirtyOnly;
        if (ImGui.Checkbox("Dirty only", ref dirty)) _dirtyOnly = dirty;
        if (ImGui.Button("+ Group")) { _newGroupPath = string.IsNullOrEmpty(_groupFilter) ? "New Group" : _groupFilter + "/New Group"; _openNewGroup = true; }

        ImGui.Separator();
        var paths = GroupPaths();
        int shownClips = 0;

        ImGui.BeginChild("##animtree", new Vector2(0, 0), ImGuiChildFlags.None);
        foreach (string path in paths)
        {
            string? parent = ParentPath(path);
            if (parent != null && paths.Contains(parent, StringComparer.OrdinalIgnoreCase)) continue;
            if (!GroupMatchesSearch(path)) continue;
            DrawGroupNode(path, paths, ref shownClips);
        }

        // Clips with no group live at the root, below the tree.
        var ungrouped = ClipsForGroup("");
        if (ungrouped.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled("UNGROUPED");
            foreach (var (clip, idx) in ungrouped) DrawClipRow(clip, idx, 1);
        }
        if (shownClips == 0 && ungrouped.Count == 0 && paths.Count == 0)
            ImGui.TextDisabled("No clips yet. Use + Clip or New Set above.");
        ImGui.EndChild();
    }

    /// <summary>Group path without its last segment (null for a root group).</summary>
    private static string? ParentPath(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash > 0 ? path[..slash] : null;
    }

    private bool GroupMatchesSearch(string path)
    {
        string s = _search.Trim();
        if (s.Length == 0) return true;
        if (path.Contains(s, StringComparison.OrdinalIgnoreCase)) return true;
        return _spriteEditor.AnimationClips.Any(c => IsInGroup(c.GroupPath, path) && ClipMatches(c, false));
    }

    /// <summary>Clips DIRECTLY in <paramref name="path"/> ("" = ungrouped), honouring
    /// search/dirty filters but not the group filter — the tree is the filter.</summary>
    private List<(AnimationClip2D Clip, int Index)> ClipsForGroup(string path)
    {
        var list = new List<(AnimationClip2D, int)>();
        for (int i = 0; i < _spriteEditor.AnimationClips.Count; i++)
        {
            var clip = _spriteEditor.AnimationClips[i];
            if (!string.Equals(clip.GroupPath ?? "", path, StringComparison.OrdinalIgnoreCase)) continue;
            if (!ClipMatches(clip, respectGroupFilter: false)) continue;
            list.Add((clip, i));
        }
        return list;
    }

    private void DrawGroupNode(string path, List<string> allPaths, ref int shownClips)
    {
        var kids = allPaths
            .Where(p => string.Equals(ParentPath(p), path, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var direct = ClipsForGroup(path);
        int total = _spriteEditor.AnimationClips.Count(c => IsInGroup(c.GroupPath, path) && ClipMatches(c, false));

        ImGui.PushID(path);
        var flags = ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (string.Equals(_groupFilter, path, StringComparison.OrdinalIgnoreCase)) flags |= ImGuiTreeNodeFlags.Selected;
        if (kids.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;

        bool open = ImGui.TreeNodeEx($"{GroupLeaf(path)}  ({total})", flags);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            _groupFilter = string.Equals(_groupFilter, path, StringComparison.OrdinalIgnoreCase) ? "" : path;

        if (ImGui.BeginPopupContextItem("##grpctx"))
        {
            if (ImGui.MenuItem("New clip here")) { _groupFilter = path; OpenNewClipDialog(); }
            if (ImGui.MenuItem("New sub-group"))
            {
                _groupFilter = path;
                _newGroupPath = path + "/New Group";
                _openNewGroup = true;
            }
            if (ImGui.MenuItem("Rename group"))
            {
                _renameGroupOld = path;
                _renameGroupNew = path;
                _openRenameGroup = true;
            }
            if (ImGui.MenuItem("Delete group (clips become ungrouped)"))
            {
                _spriteEditor.AnimationGroups.RemoveAll(g => IsInGroup(g.Path, path));
                foreach (var c in _spriteEditor.AnimationClips)
                    if (IsInGroup(c.GroupPath, path)) c.GroupPath = "";
                if (IsInGroup(_groupFilter, path)) _groupFilter = "";
                Status($"Deleted group '{path}' (and its sub-groups)");
            }
            ImGui.EndPopup();
        }

        if (open)
        {
            foreach (var (clip, idx) in direct)
            {
                DrawClipRow(clip, idx, 1);
                shownClips++;
            }
            foreach (string child in kids)
                if (GroupMatchesSearch(child) || ClipsForGroup(child).Count > 0)
                    DrawGroupNode(child, allPaths, ref shownClips);
            ImGui.TreePop();
        }
        else
        {
            shownClips += direct.Count;
        }
        ImGui.PopID();
    }

    private void DrawClipRow(AnimationClip2D clip, int idx, int indent)
    {
        ImGui.Indent(12f * indent);
        ImGui.PushID($"clip{idx}");
        string badge = clip.BakeDirty ? " *" : "";
        if (ImGui.Selectable($"{clip.Name}{badge}", idx == _clipIdx))
            SelectClip(idx);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{clip.Name}\nGroup: {(string.IsNullOrEmpty(clip.GroupPath) ? "(ungrouped)" : clip.GroupPath)}\n"
                + $"Atlas: {clip.SpriteSheetName}\nFrames: {clip.FrameIndices.Count}  FPS: {clip.FPS:0.#}");
        ImGui.PopID();
        ImGui.Unindent(12f * indent);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Preview + playback
    // ═══════════════════════════════════════════════════════════════

    private void DrawPreview(AnimationClip2D clip)
    {
        var source = _spriteEditor.FindSourceSheetPublic(clip.SourceSheetName);
        bool useSource = source != null && clip.SourceFrameIndices.Count > 0;
        var sheet = useSource ? source : _spriteEditor.FindBakedAtlasPublic(clip.SpriteSheetName);
        var frames = useSource ? clip.SourceFrameIndices : clip.FrameIndices;
        int count = frames.Count;

        if (!ReferenceEquals(clip, _boundClip))
        {
            _boundClip = clip;
            // Navigation (Asset Browser/Inspector) opens ready to play; a plain tree
            // click keeps the old paused-on-first-frame behavior.
            _playing = _autoplayOnBind;
            _autoplayOnBind = false;
            _time = 0f;
            _cursor = 0;
            _eventFrame = -1;
        }

        ImGui.Text($"{clip.Name}");
        ImGui.SameLine();
        ImGui.TextDisabled(useSource
            ? (clip.BakeDirty ? "SOURCE (dirty — bake to update the runtime clip)" : "SOURCE (baked)")
            : "BAKED ATLAS (legacy clip: timing edits only)");
        ImGui.SameLine();
        ImGui.TextDisabled($"| {clip.Mode} | {(string.IsNullOrEmpty(clip.GroupPath) ? "ungrouped" : clip.GroupPath)}");

        if (sheet == null || count == 0)
        {
            ImGui.TextDisabled(useSource
                ? "Source image is missing on disk."
                : "This clip has no baked atlas frames.");
            return;
        }

        _cursor = Math.Clamp(_cursor, 0, count - 1);
        float duration = DurationFor(clip, count);

        // Playback
        if (_playing)
        {
            float dt = ImGui.GetIO().DeltaTime;
            _time += dt * (clip.Reverse ? -1f : 1f);
            if (clip.Loop)
            {
                if (duration > 0f) { _time %= duration; if (_time < 0f) _time += duration; }
            }
            else if (_time >= duration)
            {
                _time = duration;
                _playing = false;
            }
            _cursor = LocalFrameAt(clip, count, _time, clip.Loop);
        }

        // Controls
        if (ImGui.Button(_playing ? "Pause" : "Play")) _playing = !_playing;
        ImGui.SameLine();
        if (ImGui.Button("|<")) { _time = 0f; _cursor = 0; _playing = false; }
        ImGui.SameLine();
        if (ImGui.Button("<"))
        {
            _playing = false;
            _cursor = Math.Max(0, _cursor - 1);
            _time = FrameStartTime(clip, _cursor);
        }
        ImGui.SameLine();
        if (ImGui.Button(">"))
        {
            _playing = false;
            _cursor = Math.Min(count - 1, _cursor + 1);
            _time = FrameStartTime(clip, _cursor);
        }
        ImGui.SameLine();
        if (ImGui.Button(">|")) { _cursor = count - 1; _time = FrameStartTime(clip, _cursor); _playing = false; }

        ImGui.SameLine();
        bool loop = clip.Loop;
        if (ImGui.Checkbox("Loop##preview", ref loop)) clip.Loop = loop;
        ImGui.SameLine();
        ImGui.PushItemWidth(120);
        ImGui.SliderFloat("Zoom##anim", ref _previewZoom, 1f, 3f, "%.1fx");
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.TextDisabled($"Frame {_cursor + 1}/{count}  |  {duration:0.00}s  |  {clip.FPS:0.#} FPS");

        // Progress
        float frac = duration > 0f ? Math.Clamp(_time / duration, 0f, 1f) : 0f;
        ImGui.ProgressBar(frac, new Vector2(-1, 4), "");

        // Canvas
        float availW = Math.Max(160f, ImGui.GetContentRegionAvail().X - 12f);
        float previewSize = MathF.Min(360f * _previewZoom, availW);

        int frameIdx = frames[Math.Clamp(_cursor, 0, count - 1)];
        SpriteFrame? frame = sheet.CustomFrames != null && frameIdx >= 0 && frameIdx < sheet.CustomFrames.Count
            ? sheet.CustomFrames[frameIdx] : null;
        float cellW = frame?.Width ?? (sheet.FrameWidth > 0 ? sheet.FrameWidth : sheet.ImageWidth);
        float cellH = frame?.Height ?? (sheet.FrameHeight > 0 ? sheet.FrameHeight : sheet.ImageHeight);
        if (cellW <= 0f) cellW = 64f;
        if (cellH <= 0f) cellH = 64f;

        bool normalized = clip.MasterHeight > 0f || sheet.MasterHeight > 0f;
        float masterH = clip.MasterHeight > 0f ? clip.MasterHeight
            : (sheet.MasterHeight > 0f ? sheet.MasterHeight : cellH);
        float masterW = clip.MasterWidth > 0f ? clip.MasterWidth
            : (sheet.MasterWidth > 0f ? sheet.MasterWidth : masterH);
        if (masterH <= 0f) masterH = cellH;
        float baseOffX = clip.MasterHeight > 0f ? clip.SpriteOffsetX : sheet.SpriteOffsetX;
        float baseOffY = clip.MasterHeight > 0f ? clip.SpriteOffsetY : sheet.SpriteOffsetY;

        float pxScale = previewSize / masterH;
        float dispW = cellW * pxScale;
        float dispH = cellH * pxScale;
        float offX = (baseOffX + (frame?.RenderOffsetX ?? 0f)) * pxScale;
        float offY = -((baseOffY + (frame?.RenderOffsetY ?? 0f)) * pxScale);

        uint tex = _spriteEditor.EnsureImageTextureForEditor(sheet.ImagePath);
        if (tex == 0)
        {
            ImGui.TextDisabled("Preview texture unavailable (image file missing).");
            return;
        }

        uint checkA = ImGui.ColorConvertFloat4ToU32(new Vector4(0.33f, 0.33f, 0.33f, 1f));
        uint checkB = ImGui.ColorConvertFloat4ToU32(new Vector4(0.24f, 0.24f, 0.24f, 1f));
        var drawList = ImGui.GetWindowDrawList();
        Vector2 cursorPos = ImGui.GetCursorScreenPos();
        const int check = 16;
        for (int cy = 0; cy < (int)previewSize; cy += check)
            for (int cx = 0; cx < (int)previewSize; cx += check)
            {
                bool a = ((cx / check) + (cy / check)) % 2 == 0;
                drawList.AddRectFilled(
                    new Vector2(cursorPos.X + cx, cursorPos.Y + cy),
                    new Vector2(Math.Min(cursorPos.X + cx + check, cursorPos.X + previewSize),
                                Math.Min(cursorPos.Y + cy + check, cursorPos.Y + previewSize)),
                    a ? checkA : checkB);
            }

        drawList.AddLine(new Vector2(cursorPos.X, cursorPos.Y + previewSize),
            new Vector2(cursorPos.X + previewSize, cursorPos.Y + previewSize),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.8f, 1f, 0.7f)), 1f);
        if (normalized)
        {
            float boxW = masterW * pxScale;
            float boxX = cursorPos.X + (previewSize - boxW) * 0.5f;
            drawList.AddRect(new Vector2(boxX, cursorPos.Y),
                new Vector2(boxX + boxW, cursorPos.Y + previewSize),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 0.5f)));
        }

        var (uvMin, uvMax) = sheet.GetFrameUV(frameIdx);
        float u0 = uvMin.X, u1 = uvMax.X;
        float vTop = 1f - uvMax.Y;
        float vBot = 1f - uvMin.Y;
        if (sheet.FlipY) (vTop, vBot) = (vBot, vTop);
        if (sheet.FlipX) (u0, u1) = (u1, u0);

        float drawX = cursorPos.X + (previewSize - dispW) * 0.5f + offX;
        float drawY = cursorPos.Y + (previewSize - dispH) + offY;

        // Equipment overlays (Preview Loadout): negative layers paint BEHIND the base
        // sprite, the rest in front — frame resolve mirrors the runtime's rules.
        var overlays = ResolveLoadoutLayers(clip.Name, _cursor, frameIdx);
        foreach (var layer in overlays)
            if (layer.Layer < 0) DrawLoadoutLayer(layer, cursorPos, previewSize, drawList);

        drawList.AddImage((nint)tex, new Vector2(drawX, drawY),
            new Vector2(drawX + dispW, drawY + dispH), new Vector2(u0, vTop), new Vector2(u1, vBot));

        foreach (var layer in overlays)
            if (layer.Layer >= 0) DrawLoadoutLayer(layer, cursorPos, previewSize, drawList);

        // InvisibleButton instead of Dummy: same reserved space, but it is a real item —
        // the drop target for palette drags onto the preview character.
        ImGui.InvisibleButton("##animpreviewcanvas", new Vector2(previewSize, previewSize));
        HandleLoadoutDrop();

        ImGui.TextDisabled($"Master box: {masterW:0.#} x {masterH:0.#} px" +
            (normalized ? "" : "  (native frame size)"));
    }

    // ═══════════════════════════════════════════════════════════
    //  Preview Loadout — palette, layers, overlay drawing
    // ═══════════════════════════════════════════════════════════

    /// <summary>The loadout being edited: the selected clip's GROUP stores it (persisted
    /// with the group in sprites.sheets.json); ungrouped clips fall back to a
    /// session-only list so the feature still works before a group is assigned.</summary>
    private List<PreviewLoadoutEntry> ActiveLoadout()
    {
        string path = SelectedClip?.GroupPath ?? "";
        if (string.IsNullOrEmpty(path)) return _ungroupedLoadout;
        var group = _spriteEditor.AnimationGroups.FirstOrDefault(
            g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase));
        if (group == null)
        {
            _spriteEditor.EnsureAnimationGroup(path);
            group = _spriteEditor.AnimationGroups.FirstOrDefault(
                g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase));
        }
        return group?.PreviewLoadout ?? _ungroupedLoadout;
    }

    private void AddLoadoutItem(string itemId)
    {
        var def = InventorySystem.Find(itemId);
        if (def == null) return;
        var list = ActiveLoadout();
        if (list.Any(e => string.Equals(e.ItemId, itemId, StringComparison.OrdinalIgnoreCase)))
        {
            Status($"'{def.Name}' is already on the preview.");
            return;
        }
        list.Add(new PreviewLoadoutEntry { ItemId = itemId });
        Status($"Added '{def.Name}' to the preview loadout (equip layer {def.EquipLayer}).");
    }

    private void AddLoadoutSheet(string sheetName)
    {
        var list = ActiveLoadout();
        if (list.Any(e => string.IsNullOrEmpty(e.ItemId) &&
                          string.Equals(e.Sheet, sheetName, StringComparison.OrdinalIgnoreCase)))
        {
            Status($"Sheet '{sheetName}' is already on the preview.");
            return;
        }
        list.Add(new PreviewLoadoutEntry { Sheet = sheetName, Layer = 1 });
        Status($"Added sheet '{sheetName}' to the preview loadout.");
    }

    /// <summary>Accept either palette payload — used by BOTH drop targets (the preview
    /// canvas and the layers list). unsafe: the payload pointer is only valid on the
    /// frame it is delivered (same pattern as the Inspector's drop handlers).</summary>
    private unsafe void HandleLoadoutDrop()
    {
        if (!ImGui.BeginDragDropTarget()) return;
        var pItem = ImGui.AcceptDragDropPayload("ANIM_LOADOUT_ITEM");
        if (pItem.NativePtr != null && _dragLoadoutItemId != null)
        {
            AddLoadoutItem(_dragLoadoutItemId);
            _dragLoadoutItemId = null;
        }
        var pSheet = ImGui.AcceptDragDropPayload("ANIM_LOADOUT_SHEET");
        if (pSheet.NativePtr != null && _dragLoadoutSheetName != null)
        {
            AddLoadoutSheet(_dragLoadoutSheetName);
            _dragLoadoutSheetName = null;
        }
        ImGui.EndDragDropTarget();
    }

    /// <summary>One loadout layer resolved to a drawable frame. The frame resolve is a
    /// line-for-line mirror of <c>EditorObject.DrawEquipmentSpriteOverlays</c> (authored
    /// clip + action-token auto-match → position-in-clip; else raw index clamp; unsynced
    /// = own FPS) so the preview shows exactly what the game will draw.</summary>
    private sealed class ResolvedLoadoutLayer
    {
        public PreviewLoadoutEntry Entry = null!;
        public string Label = "";
        public SpriteSheet? Sheet;
        public uint Tex;
        public int FrameIdx;
        public int Layer;
        /// <summary>World-unit offsets/height (the preview treats the character as 1 unit tall).</summary>
        public float OffsetX, OffsetY, WorldHeight;
        public bool Visible = true;
        /// <summary>Non-empty = why this layer cannot draw (shown in the layers list).</summary>
        public string Problem = "";
    }

    private List<ResolvedLoadoutLayer> ResolveLoadoutLayers(string baseClipName, int baseLocalFrame, int baseFrameIdx)
    {
        var result = new List<ResolvedLoadoutLayer>();
        var entries = ActiveLoadout();
        if (entries.Count == 0) return result;

        foreach (var entry in entries)
        {
            var layer = new ResolvedLoadoutLayer { Entry = entry, Visible = entry.Visible };
            result.Add(layer);

            string sheetName, clipName;
            bool sync;
            if (!string.IsNullOrEmpty(entry.ItemId))
            {
                // Item entries resolve LIVE: editing the item in the Item Editor updates
                // this preview with no re-link (the plan's "no duplicate data" rule).
                var def = InventorySystem.Find(entry.ItemId);
                if (def == null) { layer.Label = $"{entry.ItemId} (missing item)"; layer.Problem = "item not in catalog"; continue; }
                layer.Label = def.Name;
                if (!def.IsEquipment || string.IsNullOrEmpty(def.EquipSheet))
                { layer.Problem = "item has no equip art (set Equip Art Sheet in the Item Editor)"; continue; }
                sheetName = def.EquipSheet;
                clipName = def.EquipClip;
                layer.Layer = def.EquipLayer;
                layer.OffsetX = def.EquipOffsetX;
                layer.OffsetY = def.EquipOffsetY;
                layer.WorldHeight = def.EquipWorldHeight;
                sync = def.EquipSyncFrame;
            }
            else
            {
                layer.Label = entry.Sheet;
                sheetName = entry.Sheet;
                clipName = entry.Clip;
                layer.Layer = entry.Layer;
                layer.OffsetX = entry.OffsetX;
                layer.OffsetY = entry.OffsetY;
                layer.WorldHeight = entry.WorldHeight;
                sync = entry.SyncFrame;
            }
            if (string.IsNullOrEmpty(sheetName)) { layer.Problem = "no overlay sheet"; continue; }

            // Registry first (the runtime resolves from it); the editor's own sheet lists
            // are the fallback so an unbaked sheet still previews while authoring.
            SpriteSheet? osheet;
            if (!IDEBridge.TryGetSpriteSheet(sheetName, out osheet) || osheet == null)
                osheet = _spriteEditor.FindSourceSheetPublic(sheetName)
                    ?? _spriteEditor.FindBakedAtlasPublic(sheetName);
            if (osheet == null) { layer.Problem = "sheet not loaded in the Sprite Editor"; continue; }
            layer.Sheet = osheet;

            AnimationClip2D? oclip = null;
            bool haveClip = !string.IsNullOrEmpty(clipName)
                && IDEBridge.TryGetSpriteClip(sheetName, clipName, out _, out oclip)
                && oclip != null && oclip.FrameIndices.Count > 0;
            int count = haveClip ? oclip!.FrameIndices.Count
                : osheet.Columns > 0 && osheet.Rows > 0 ? osheet.Columns * osheet.Rows : 1;
            if (count <= 0) { layer.Problem = "overlay sheet has no frames"; continue; }

            AnimationClip2D? syncClip = null;
            if (sync && baseLocalFrame >= 0)
            {
                syncClip = haveClip
                    ? IDEBridge.FindAnimationClipForAction(sheetName, oclip!.Name, baseClipName) ?? oclip
                    : IDEBridge.FindAnimationClipForBase(sheetName, baseClipName);
            }

            int frameIdx;
            if (syncClip != null)
            {
                int sc = syncClip.FrameIndices.Count;
                frameIdx = syncClip.FrameIndices[Math.Clamp(baseLocalFrame, 0, sc - 1)];
            }
            else if (sync)
            {
                frameIdx = Math.Clamp(baseFrameIdx, 0, count - 1);
            }
            else
            {
                float frameDur = haveClip
                    ? 1f / MathF.Max(0.01f, oclip!.FPS * MathF.Max(0.01f, oclip.SpeedMultiplier))
                    : 1f / 8f; // 8 FPS implicit-grid fallback, same as portals/auras
                int f = (int)(_time / frameDur);
                f = ((f % count) + count) % count;
                frameIdx = haveClip ? oclip!.FrameIndices[f] : Math.Clamp(f, 0, count - 1);
            }
            layer.FrameIdx = frameIdx;

            uint tex = _spriteEditor.EnsureImageTextureForEditor(osheet.ImagePath);
            if (tex == 0) { layer.Problem = "overlay image file missing on disk"; continue; }
            layer.Tex = tex;
        }
        return result;
    }

    private void DrawLoadoutLayer(ResolvedLoadoutLayer l, Vector2 cursorPos, float previewSize, ImDrawListPtr drawList)
    {
        if (!l.Visible || l.Problem.Length > 0 || l.Sheet == null || l.Tex == 0) return;
        var sheet = l.Sheet;

        float ocellW, ocellH;
        if (sheet.CustomFrames != null && l.FrameIdx >= 0 && l.FrameIdx < sheet.CustomFrames.Count)
        {
            var fr = sheet.CustomFrames[l.FrameIdx];
            ocellW = fr.Width;
            ocellH = fr.Height > 0 ? fr.Height : 1f;
        }
        else
        {
            ocellH = sheet.FrameHeight > 0 ? sheet.FrameHeight : sheet.ImageHeight;
            if (ocellH <= 0f) ocellH = 64f;
            ocellW = sheet.FrameWidth > 0 ? sheet.FrameWidth : ocellH;
        }

        // Size: EquipWorldHeight in world units (0 = the character's own height; width
        // keeps the art's aspect). The preview treats the character as 1 world unit tall —
        // in-game the scene object's real height wins.
        float oh = (l.WorldHeight > 0f ? l.WorldHeight : 1f) * previewSize;
        float ow = oh * (ocellW / MathF.Max(1f, ocellH));

        float tileBottom = cursorPos.Y + previewSize;          // the feet line (master-box bottom)
        float tileCenterX = cursorPos.X + previewSize * 0.5f;  // master-box center
        float cx = tileCenterX + l.OffsetX * previewSize;
        float bottom = tileBottom - l.OffsetY * previewSize;
        float x0 = cx - ow * 0.5f;

        var (ouvMin, ouvMax) = sheet.GetFrameUV(l.FrameIdx);
        float u0 = ouvMin.X, u1 = ouvMax.X;
        float vTop = 1f - ouvMax.Y;
        float vBot = 1f - ouvMin.Y;
        if (sheet.FlipY) (vBot, vTop) = (vTop, vBot);
        if (sheet.FlipX) (u0, u1) = (u1, u0);

        drawList.AddImage((nint)l.Tex, new Vector2(x0, bottom - oh),
            new Vector2(x0 + ow, bottom), new Vector2(u0, vTop), new Vector2(u1, vBot));
    }

    /// <summary>Layers list (left, drop target) + palette (right, drag sources) below the
    /// clip properties. The loadout persists with the clip's group.</summary>
    private void DrawLoadoutSection(AnimationClip2D clip)
    {
        ImGui.TextDisabled("PREVIEW LOADOUT");
        ImGui.SameLine();
        ImGui.TextDisabled("— drag an item or a sheet onto the preview (or the list) to dress the character");
        if (string.IsNullOrEmpty(clip.GroupPath))
            ImGui.TextColored(ColDim,
                "Ungrouped clip: this loadout is session-only — assign a Group in CLIP properties to keep it.");

        var list = ActiveLoadout();
        float palW = 260f;
        float h = 156f;

        ImGui.BeginChild("##loadoutlayers",
            new Vector2(MathF.Max(240f, ImGui.GetContentRegionAvail().X - palW - 8f), h),
            ImGuiChildFlags.Borders);
        int removeIdx = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            ImGui.PushID($"loadrow{i}");
            bool vis = e.Visible;
            if (ImGui.Checkbox("##vis", ref vis)) e.Visible = vis;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Show / hide this layer on the preview");
            ImGui.SameLine();

            string name, info;
            if (!string.IsNullOrEmpty(e.ItemId))
            {
                var def = InventorySystem.Find(e.ItemId);
                name = def?.Name ?? $"{e.ItemId} (missing)";
                info = def != null
                    ? $"item → {def.EquipSheet} · live from item · L{def.EquipLayer}"
                    : "item not in catalog";
            }
            else
            {
                name = e.Sheet;
                info = $"sheet · L{e.Layer}" + (e.SyncFrame ? " · synced" : " · own FPS");
            }
            if (!string.IsNullOrEmpty(e.ItemId) && InventorySystem.Find(e.ItemId) is { IsEquipment: false })
                info = "item has no equip art";

            ImGui.Text(name);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(e.ItemId.Length > 0
                    ? "Equipment item layer — sheet/clip/layer/offsets resolve LIVE from the item.\n"
                      + "Edit it in the Item Editor; the preview follows."
                    : "Direct sheet layer — no item behind it.");
            ImGui.SameLine();
            ImGui.TextDisabled(info);
            ImGui.SameLine();
            if (ImGui.Button("X##rmlayer")) removeIdx = i;
            ImGui.PopID();
        }
        if (removeIdx >= 0) list.RemoveAt(removeIdx);
        if (list.Count == 0)
            ImGui.TextDisabled("(empty — drop an item or a sheet here)");

        // Drop zone (second target besides the preview canvas itself).
        Vector2 zonePos = ImGui.GetCursorScreenPos();
        Vector2 zoneSize = new(MathF.Max(80f, ImGui.GetContentRegionAvail().X - 6f), 26f);
        ImGui.InvisibleButton("##loadoutdrop", zoneSize);
        var dl = ImGui.GetWindowDrawList();
        uint zoneCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.8f, 1f, 0.55f));
        dl.AddRect(zonePos, zonePos + zoneSize, zoneCol, 4f, ImDrawFlags.None, 1.5f);
        dl.AddText(zonePos + new Vector2(7f, 6f), zoneCol, "Drop items / sheets here");
        HandleLoadoutDrop();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##loadoutpalette", new Vector2(0, h), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("PALETTE");
        ImGui.PushItemWidth(-1);
        ImGui.InputTextWithHint("##loadoutfilter", "filter…", ref _loadoutFilter, 64);
        ImGui.PopItemWidth();
        string filter = _loadoutFilter.Trim();
        int shown = 0;

        foreach (var it in InventorySystem.Items)
        {
            if (!it.IsEquipment || string.IsNullOrEmpty(it.EquipSheet)) continue;
            if (filter.Length > 0 && !it.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !it.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !it.EquipSheet.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            if (ImGui.Selectable($"{it.Name}##loi{it.Id}")) { /* click = preview only; drag = add */ }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Equipment item\nEquip art: {it.EquipSheet}" +
                    (string.IsNullOrEmpty(it.EquipClip) ? " (auto-matched clip)" : $" / {it.EquipClip}") +
                    "\nDrag onto the preview or the layers list.");
            if (ImGui.BeginDragDropSource())
            {
                ImGui.SetDragDropPayload("ANIM_LOADOUT_ITEM", nint.Zero, 0);
                _dragLoadoutItemId = it.Id;
                ImGui.Text($"{it.Name}  (L{it.EquipLayer})");
                ImGui.EndDragDropSource();
            }
            shown++;
        }

        foreach (var s in _spriteEditor.SpriteSheets)
        {
            if (filter.Length > 0 && !s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            if (ImGui.Selectable($"{s.Name}##los{s.Name}")) { }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Sprite sheet — added as a DIRECT layer (no item).\n" +
                    "Drag onto the preview or the layers list.");
            if (ImGui.BeginDragDropSource())
            {
                ImGui.SetDragDropPayload("ANIM_LOADOUT_SHEET", nint.Zero, 0);
                _dragLoadoutSheetName = s.Name;
                ImGui.Text($"{s.Name}");
                ImGui.EndDragDropSource();
            }
            shown++;
        }
        if (shown == 0)
            ImGui.TextDisabled(filter.Length == 0
                ? "(no equipment items in the catalog, no sheets loaded)"
 : "(nothing matches the filter)");
        ImGui.EndChild();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Timeline / frame strip
    // ═══════════════════════════════════════════════════════════════

    private void DrawTimeline(AnimationClip2D clip)
    {
        var frames = EditableFrames(clip);
        int count = frames.Count;
        bool editableSource = clip.SourceFrameIndices.Count > 0;

        ImGui.Text("TIMELINE");
        ImGui.SameLine();
        ImGui.TextDisabled(editableSource
            ? "source strip — drag frames to reorder / edit holds & events, then Bake"
            : "baked strip — timing edits are live (no source to rebake from)");

        if (count == 0)
        {
            ImGui.TextDisabled("No frames. Add one below.");
        }
        else
        {
            EnsureHolds(clip, count);
            _cursor = Math.Clamp(_cursor, 0, count - 1);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _dragFrameFrom = -1;

            ImGui.BeginChild("##frame_strip", new Vector2(0, 88), ImGuiChildFlags.Borders,
                ImGuiWindowFlags.HorizontalScrollbar);
            var drawList = ImGui.GetWindowDrawList();
            for (int i = 0; i < count; i++)
            {
                ImGui.PushID(i);
                Vector2 p = ImGui.GetCursorScreenPos();
                ImGui.InvisibleButton("##cell", new Vector2(42, 66));
                bool hovered = ImGui.IsItemHovered();
                bool clicked = ImGui.IsItemClicked();
                bool selected = i == _cursor;

                uint bg = selected
                    ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.45f, 0.85f, 0.95f))
                    : ImGui.ColorConvertFloat4ToU32(hovered ? new Vector4(0.3f, 0.3f, 0.38f, 1f) : new Vector4(0.2f, 0.2f, 0.26f, 1f));
                drawList.AddRectFilled(p, p + new Vector2(40, 64), bg, 3f);
                if (selected)
                    drawList.AddRect(p, p + new Vector2(40, 64),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 0.85f, 1f, 1f)), 3f, ImDrawFlags.None, 2f);

                drawList.AddText(new Vector2(p.X + 5f, p.Y + 5f),
                    ImGui.ColorConvertFloat4ToU32(selected ? Vector4.One : ColText), frames[i].ToString());
                float hold = HoldOf(clip, i);
                if (hold != 1f)
                    drawList.AddText(new Vector2(p.X + 5f, p.Y + 22f),
                        ImGui.ColorConvertFloat4ToU32(ColAmber), $"x{hold:0.##}");
                if (clip.Events.ContainsKey(i))
                    drawList.AddCircleFilled(new Vector2(p.X + 33f, p.Y + 10f), 3.5f,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.35f, 0.3f, 1f)));
                drawList.AddText(new Vector2(p.X + 5f, p.Y + 44f),
                    ImGui.ColorConvertFloat4ToU32(ColDim), $"#{i}");

                if (clicked)
                {
                    _playing = false;
                    _cursor = i;
                    _time = FrameStartTime(clip, i);
                    _eventFrame = -1;
                }

                if (ImGui.BeginDragDropSource())
                {
                    _dragFrameFrom = i;
                    ImGui.SetDragDropPayload("ANIM_FRAME", nint.Zero, 0);
                    ImGui.Text($"Frame {i}");
                    ImGui.EndDragDropSource();
                }
                if (ImGui.BeginDragDropTarget())
                {
                    ImGui.AcceptDragDropPayload("ANIM_FRAME");
                    if (_dragFrameFrom >= 0 && _dragFrameFrom != i)
                    {
                        MoveFrame(clip, _dragFrameFrom, i);
                        _dragFrameFrom = -1;
                    }
                    ImGui.EndDragDropTarget();
                }

                ImGui.SameLine();
                ImGui.PopID();
            }
            ImGui.EndChild();
        }

        // Selected frame tools
        if (count > 0)
        {
            _cursor = Math.Clamp(_cursor, 0, count - 1);
            ImGui.Text($"Selected: local #{_cursor}  →  frame {frames[_cursor]}");

            ImGui.PushItemWidth(100);
            float hold = HoldOf(clip, _cursor);
            if (ImGui.DragFloat("Hold", ref hold, 0.02f, 0.1f, 8f, "%.2fx"))
            {
                EnsureHolds(clip, count);
                clip.FrameHolds[_cursor] = MathF.Max(0.05f, hold);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Duration multiplier for this frame (2 = twice as long).\nTiming is runtime data, so it applies immediately.");

            // Do not reload the buffer while the field is focused: clicking another
            // timeline cell changes _cursor BEFORE ImGui reports the deactivation, so a
            // reload here would wipe the typed text before it could be committed.
            if (!_eventEditing && _eventFrame != _cursor)
            {
                _eventBuffer = clip.Events.TryGetValue(_cursor, out string? ev) ? ev : "";
                _eventFrame = _cursor;
            }
            ImGui.InputText("Event", ref _eventBuffer, 64);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Event fired when playback reaches this frame.\nWritten to the clip's Events map (runtime CheckEvent).");
            if (ImGui.IsItemActivated())
            {
                _eventEditing = true;
                _eventFrame = _cursor;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                int target = Math.Clamp(_eventFrame >= 0 ? _eventFrame : _cursor, 0, count - 1);
                if (string.IsNullOrWhiteSpace(_eventBuffer)) clip.Events.Remove(target);
                else clip.Events[target] = _eventBuffer.Trim();
                _eventEditing = false;
                _eventFrame = -1;
            }
            else if (ImGui.IsItemDeactivated())
            {
                _eventEditing = false;
                _eventFrame = -1;
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Move <") && _cursor > 0) { MoveFrame(clip, _cursor, _cursor - 1); _cursor--; }
            ImGui.SameLine();
            if (ImGui.Button("Move >") && _cursor < count - 1) { MoveFrame(clip, _cursor, _cursor + 1); _cursor++; }
            ImGui.SameLine();
            if (ImGui.Button("Duplicate")) DuplicateFrame(clip);
            ImGui.SameLine();
            ImGui.BeginDisabled(count <= 1);
            if (ImGui.Button("Remove")) RemoveFrame(clip);
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            ImGui.DragInt("##insertidx", ref _insertFrameIndex, 1f, 0, 100000);
            ImGui.SameLine();
            if (ImGui.Button("Insert frame #"))
            {
                var sheet = _spriteEditor.FindSourceSheetPublic(clip.SourceSheetName)
                    ?? _spriteEditor.FindBakedAtlasPublic(clip.SpriteSheetName);
                int max = Math.Max(0, (sheet?.FrameCount ?? 1) - 1);
                InsertFrame(clip, Math.Clamp(_insertFrameIndex, 0, max));
            }
        }
    }

    private void MoveFrame(AnimationClip2D clip, int from, int to)
    {
        var frames = EditableFrames(clip);
        int count = frames.Count;
        if (from < 0 || from >= count || to < 0 || to >= count || from == to) return;
        EnsureHolds(clip, count);

        int f = frames[from];
        frames.RemoveAt(from);
        frames.Insert(to, f);

        float hold = clip.FrameHolds[from];
        clip.FrameHolds.RemoveAt(from);
        clip.FrameHolds.Insert(to, hold);

        MoveEvent(clip.Events, from, to, count);
        MarkEdited(clip, true);
        _eventFrame = -1;
    }

    private void DuplicateFrame(AnimationClip2D clip)
    {
        var frames = EditableFrames(clip);
        int count = frames.Count;
        if (count == 0) return;
        EnsureHolds(clip, count);
        int at = Math.Clamp(_cursor, 0, count - 1);
        frames.Insert(at + 1, frames[at]);
        clip.FrameHolds.Insert(at + 1, clip.FrameHolds[at]);
        InsertEvent(clip.Events, at, count);
        MarkEdited(clip, true);
        _cursor = at + 1;
        _eventFrame = -1;
    }

    private void InsertFrame(AnimationClip2D clip, int sourceFrame)
    {
        var frames = EditableFrames(clip);
        int count = frames.Count;
        EnsureHolds(clip, count);
        int at = count == 0 ? 0 : Math.Clamp(_cursor + 1, 0, count);
        frames.Insert(at, sourceFrame);
        clip.FrameHolds.Insert(at, 1f);
        InsertEventEmpty(clip.Events, at, count);
        MarkEdited(clip, true);
        _cursor = at;
        _eventFrame = -1;
    }

    private void RemoveFrame(AnimationClip2D clip)
    {
        var frames = EditableFrames(clip);
        int count = frames.Count;
        if (count <= 1) return;
        int at = Math.Clamp(_cursor, 0, count - 1);
        frames.RemoveAt(at);
        EnsureHolds(clip, count);
        clip.FrameHolds.RemoveAt(Math.Min(at, clip.FrameHolds.Count - 1));
        RemoveEvent(clip.Events, at, count);
        MarkEdited(clip, true);
        _cursor = Math.Clamp(at, 0, frames.Count - 1);
        _eventFrame = -1;
    }

    private static void MoveEvent(Dictionary<int, string> events, int from, int to, int count)
    {
        if (events.Count == 0) return;
        var map = new Dictionary<int, string>();
        foreach (var kv in events)
        {
            int idx = kv.Key;
            if (idx == from) idx = to;
            else if (from < to && idx > from && idx <= to) idx--;
            else if (to < from && idx >= to && idx < from) idx++;
            if (idx >= 0 && idx < count) map[idx] = kv.Value;
        }
        events.Clear();
        foreach (var kv in map) events[kv.Key] = kv.Value;
    }

    private static void InsertEvent(Dictionary<int, string> events, int at, int count)
    {
        if (events.Count == 0) return;
        var map = new Dictionary<int, string>();
        foreach (var kv in events)
        {
            int idx = kv.Key > at ? kv.Key + 1 : kv.Key;
            if (idx < count + 1) map[idx] = kv.Value;
        }
        // Duplicate the source frame's event onto the new frame too.
        if (events.TryGetValue(at, out string? ev)) map[at + 1] = ev;
        events.Clear();
        foreach (var kv in map) events[kv.Key] = kv.Value;
    }

    private static void InsertEventEmpty(Dictionary<int, string> events, int at, int count)
    {
        if (events.Count == 0) return;
        var map = new Dictionary<int, string>();
        foreach (var kv in events)
        {
            int idx = kv.Key >= at ? kv.Key + 1 : kv.Key;
            if (idx < count + 1) map[idx] = kv.Value;
        }
        events.Clear();
        foreach (var kv in map) events[kv.Key] = kv.Value;
    }

    private static void RemoveEvent(Dictionary<int, string> events, int at, int count)
    {
        if (events.Count == 0) return;
        var map = new Dictionary<int, string>();
        foreach (var kv in events)
        {
            if (kv.Key == at) continue;
            int idx = kv.Key > at ? kv.Key - 1 : kv.Key;
            if (idx >= 0 && idx < count - 1) map[idx] = kv.Value;
        }
        events.Clear();
        foreach (var kv in map) events[kv.Key] = kv.Value;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Clip properties
    // ═══════════════════════════════════════════════════════════════

    private void DrawClipProperties(AnimationClip2D clip)
    {
        ImGui.TextDisabled("CLIP");

        ImGui.PushItemWidth(220);
        if (!ReferenceEquals(_nameBufferClip, clip))
        {
            _nameBufferClip = clip;
            _nameBuffer = clip.Name;
            _nameNotice = false;
            _pendingRenameOld = "";
        }
        ImGui.InputText("Name##clip", ref _nameBuffer, 128);
        if (ImGui.IsItemDeactivatedAfterEdit()) ApplyClipRename(clip);
        if (_nameNotice && !string.IsNullOrEmpty(_pendingRenameOld))
        {
            // Recount live: the scene/items may have changed since the rename.
            int refs = CollectClipReferences(_pendingRenameOld).Count;
            if (refs > 0)
            {
                ImGui.TextColored(ColAmber,
                    $"Renamed — {refs} reference(s) still use '{_pendingRenameOld}'.");
                if (ImGui.Button($"Update {refs} reference(s)##updrefs"))
                {
                    int updated = UpdateClipReferences(_pendingRenameOld, clip.Name);
                    _pendingRenameOld = "";
                    _nameNotice = false;
                    Status($"Updated {updated} reference(s) → '{clip.Name}'");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Rewrites the loaded scene objects (base clip, action " +
                        "clips, equipment layers) and inventory items that still point at " +
                        "the old name. Save the scene/items afterwards to persist.");
            }
            else
            {
                ImGui.TextColored(ColDim, $"Renamed '{_pendingRenameOld}' → '{clip.Name}' (no references to update).");
                _pendingRenameOld = "";
            }
        }

        string groupLabel = string.IsNullOrEmpty(clip.GroupPath) ? "(ungrouped)" : clip.GroupPath;
        if (ImGui.BeginCombo("Group##clip", groupLabel))
        {
            if (ImGui.Selectable("(ungrouped)", string.IsNullOrEmpty(clip.GroupPath))) clip.GroupPath = "";
            foreach (string path in GroupPaths())
                if (ImGui.Selectable(path, string.Equals(clip.GroupPath, path, StringComparison.OrdinalIgnoreCase)))
                    clip.GroupPath = path;
            ImGui.EndCombo();
        }

        float fps = clip.FPS;
        if (ImGui.SliderFloat("FPS##clip", ref fps, 1f, 60f, "%.0f", ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic))
            clip.FPS = MathF.Round(fps);

        bool loop = clip.Loop;
        if (ImGui.Checkbox("Loop##clip", ref loop)) clip.Loop = loop;
        ImGui.SameLine();
        bool reverse = clip.Reverse;
        if (ImGui.Checkbox("Reverse##clip", ref reverse)) clip.Reverse = reverse;

        float speed = clip.SpeedMultiplier;
        if (ImGui.SliderFloat("Speed##clip", ref speed, 0.1f, 4f, "%.1fx")) clip.SpeedMultiplier = speed;

        ImGui.TextDisabled($"Atlas: {clip.SpriteSheetName}   |   Baked frames: {clip.FrameIndices.Count}");
        if (clip.CanRebake)
            ImGui.TextDisabled($"Source: {clip.SourceSheetName}   |   Authored frames: {clip.SourceFrameIndices.Count}");
        else
            ImGui.TextColored(ColAmber, "No source sheet — frame/timing edits cannot be rebaked (legacy clip).");

        if (clip.BakeDirty)
            ImGui.TextColored(ColAmber, "DIRTY — source strip changed since the last bake.");
        else if (clip.CanRebake)
            ImGui.TextColored(ColGreen, "BAKED — runtime clip matches the authored strip.");

        ImGui.PopItemWidth();

        ImGui.Separator();

        ImGui.BeginDisabled(!clip.CanRebake);
        if (ImGui.Button("Bake Clip")) BakeClip(clip);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Duplicate Clip"))
        {
            var copy = CloneClip(clip);
            copy.Name = MakeUniqueName(clip.Name + " Copy");
            int idx = _spriteEditor.AddOrReplaceClipForEditor(copy);
            SelectClip(idx);
            Status($"Duplicated '{clip.Name}' → '{copy.Name}'");
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete Clip"))
        {
            string name = clip.Name;
            _spriteEditor.AnimationClips.RemoveAt(_clipIdx);
            _clipIdx = Math.Clamp(_clipIdx, 0, Math.Max(0, _spriteEditor.AnimationClips.Count - 1));
            _boundClip = null;
            _spriteEditor.SelectedClipIndexPublic = _clipIdx;
            _spriteEditor.ForceSynchronizeRegistry();
            Status($"Deleted clip '{name}'");
        }
    }

    /// <summary>Commits the name buffer once (Enter/blur). Duplicate names are rejected
    /// because the clip list resolves by name everywhere (runtime, equipment layers).</summary>
    private void ApplyClipRename(AnimationClip2D clip)
    {
        string newName = (_nameBuffer ?? "").Trim();
        if (newName.Length == 0 || newName == clip.Name)
        {
            _nameBuffer = clip.Name;
            return;
        }
        if (_spriteEditor.AnimationClips.Any(c =>
                !ReferenceEquals(c, clip) && string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            Status($"Rename rejected: a clip named '{newName}' already exists.");
            _nameBuffer = clip.Name;
            return;
        }
        string oldName = clip.Name;
        int refs = CollectClipReferences(oldName).Count;
        clip.Name = newName;
        _nameNotice = true;
        _pendingRenameOld = oldName;
        Status(refs > 0
            ? $"Renamed '{oldName}' → '{newName}' — {refs} reference(s) still use the old name (Update button above)."
            : $"Renamed '{oldName}' → '{newName}'");
    }

    // ═══════════════════════════════════════════════════════════
    //  Reference scan (rename safety)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Collect a setter for every clip-name slot currently holding
    /// <paramref name="clipName"/>: the active scene's objects (base clip, action clips,
    /// direct equipment layers) and inventory items (EquipClip). Counting and updating
    /// share this list so they can never disagree. Effects/trigger params and saved-but-
    /// unloaded scenes are not scanned — documented in the Update button's tooltip.</summary>
    private List<Action<string>> CollectClipReferences(string clipName)
    {
        var slots = new List<Action<string>>();
        if (string.IsNullOrEmpty(clipName)) return slots;
        static bool Is(string? value, string name)
            => string.Equals(value, name, StringComparison.OrdinalIgnoreCase);

        var objects = _spriteEditor.BridgePublic?.EditorObjectManager?.Objects;
        if (objects != null)
        {
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                if (Is(obj.Player2DAnimationClip, clipName))
                {
                    var o = obj;
                    slots.Add(v => o.Player2DAnimationClip = v);
                }
                foreach (var act in obj.Actions)
                {
                    if (!Is(act.Clip, clipName)) continue;
                    var a = act;
                    slots.Add(v => a.Clip = v);
                }
                foreach (var layer in obj.DirectEquipment)
                {
                    if (!Is(layer.Clip, clipName)) continue;
                    var l = layer;
                    slots.Add(v => l.Clip = v);
                }
            }
        }

        foreach (var item in InventorySystem.Items)
        {
            if (!Is(item.EquipClip, clipName)) continue;
            var it = item;
            slots.Add(v => it.EquipClip = v);
        }
        return slots;
    }

    /// <summary>Rewrite every scanned slot from <paramref name="oldName"/> to
    /// <paramref name="newName"/> and return how many were changed.</summary>
    private int UpdateClipReferences(string oldName, string newName)
    {
        var slots = CollectClipReferences(oldName);
        foreach (var set in slots) set(newName);
        return slots.Count;
    }

    /// <summary>Clip name → live reference count (scene + items), for the Library's Refs column.</summary>
    private Dictionary<string, int> BuildClipReferenceCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Bump(string? name)
        {
            if (string.IsNullOrEmpty(name)) return;
            counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
        }

        var objects = _spriteEditor.BridgePublic?.EditorObjectManager?.Objects;
        if (objects != null)
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                Bump(obj.Player2DAnimationClip);
                foreach (var act in obj.Actions) Bump(act.Clip);
                foreach (var layer in obj.DirectEquipment) Bump(layer.Clip);
            }
        foreach (var item in InventorySystem.Items) Bump(item.EquipClip);
        return counts;
    }

    private string MakeUniqueName(string baseName)
    {
        string name = baseName;
        int n = 2;
        while (_spriteEditor.AnimationClips.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {n++}";
        return name;
    }

    private static AnimationClip2D CloneClip(AnimationClip2D clip) => new()
    {
        Name = clip.Name,
        SpriteSheetName = clip.SpriteSheetName,
        FrameIndices = clip.FrameIndices.ToList(),
        FPS = clip.FPS,
        Loop = clip.Loop,
        Reverse = clip.Reverse,
        SpeedMultiplier = clip.SpeedMultiplier,
        Events = new Dictionary<int, string>(clip.Events),
        MasterWidth = clip.MasterWidth,
        MasterHeight = clip.MasterHeight,
        SpriteOffsetX = clip.SpriteOffsetX,
        SpriteOffsetY = clip.SpriteOffsetY,
        GroupPath = clip.GroupPath,
        Mode = clip.Mode,
        SourceSheetName = clip.SourceSheetName,
        SourceFrameIndices = clip.SourceFrameIndices.ToList(),
        FrameHolds = clip.FrameHolds.ToList(),
        BakeDirty = clip.BakeDirty,
        // A duplicate is a NEW clip (fresh id from the field initializer) but it keeps
        // the rig + tracks + bake settings it was cloned from.
        RigId = clip.RigId,
        Tracks = clip.Tracks?.Select(t => t.Clone()).ToList(),
        Bake = clip.Bake?.Clone(),
    };

    // ═══════════════════════════════════════════════════════════════
    //  Library dashboard
    // ═══════════════════════════════════════════════════════════════

    private void DrawLibrary()
    {
        ImGui.PushItemWidth(180);
        ImGui.InputText("##libsearch", ref _search, 128);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.BeginCombo("##libgroup", string.IsNullOrEmpty(_groupFilter) ? "All groups" : _groupFilter))
        {
            if (ImGui.Selectable("All groups", string.IsNullOrEmpty(_groupFilter))) _groupFilter = "";
            foreach (string path in GroupPaths())
                if (ImGui.Selectable(path, string.Equals(_groupFilter, path, StringComparison.OrdinalIgnoreCase)))
                    _groupFilter = path;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        bool dirty = _dirtyOnly;
        if (ImGui.Checkbox("Dirty only##lib", ref dirty)) _dirtyOnly = dirty;
        ImGui.SameLine();
        if (ImGui.Button("Bake Dirty")) BakeAllDirty();
        ImGui.SameLine();
        if (ImGui.Button("Save")) { _spriteEditor.SaveAllSheets(); Status("Saved clips + groups"); }

        var refCounts = BuildClipReferenceCounts();
        var rows = _spriteEditor.AnimationClips
            .Select((clip, idx) => (clip, idx))
            .Where(t => ClipMatches(t.clip))
            .ToList();
        rows.Sort((a, b) => CompareLibraryRows(a, b, refCounts));

        int referenced = rows.Count(t => refCounts.ContainsKey(t.clip.Name));
        ImGui.TextDisabled($"{rows.Count} / {_spriteEditor.AnimationClips.Count} clips   ·   {referenced} referenced in scene/items");

        if (ImGui.BeginTable("##animlib", 10,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                new Vector2(0, MathF.Max(120f, ImGui.GetContentRegionAvail().Y))))
        {
            ImGui.TableSetupColumn("Clip", ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("Sheet", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("FPS", ImGuiTableColumnFlags.WidthFixed, 46f);
            ImGui.TableSetupColumn("Frames", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn("Loop", ImGuiTableColumnFlags.WidthFixed, 48f);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableSetupColumn("Mode", ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn("Refs", ImGuiTableColumnFlags.WidthFixed, 48f);
            // Sortable by click instead of ImGui's sort-specs: same behaviour, and it
            // keeps the sort state (column + direction) as plain panel fields.
            LibHeader("Clip", 0);
            LibHeader("Group", 1);
            LibHeader("Sheet", 2);
            LibHeader("FPS", 3);
            LibHeader("Frames", 4);
            LibHeader("Duration", 5);
            LibHeader("Loop", 6);
            LibHeader("State", 7);
            LibHeader("Mode", 8);
            LibHeader("Refs", 9);

            foreach (var (clip, idx) in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{clip.Name}##lib{idx}", idx == _clipIdx, ImGuiSelectableFlags.SpanAllColumns))
                    SelectClip(idx);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Click = select   ·   Double-click = open it in the Workspace (timeline + preview)");
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        SelectClip(idx);
                        _tabMode = 0;
                    }
                }

                ImGui.TableNextColumn(); ImGui.Text(string.IsNullOrEmpty(clip.GroupPath) ? "-" : clip.GroupPath);
                ImGui.TableNextColumn(); ImGui.Text(clip.SpriteSheetName);
                ImGui.TableNextColumn(); ImGui.Text($"{clip.FPS:0.#}");
                int authoredFrames = EditableFrames(clip).Count;
                ImGui.TableNextColumn(); ImGui.Text(authoredFrames.ToString());
                ImGui.TableNextColumn(); ImGui.Text($"{DurationFor(clip, authoredFrames):0.00}s");
                ImGui.TableNextColumn(); ImGui.Text(clip.Loop ? "yes" : "no");
                ImGui.TableNextColumn();
                if (clip.BakeDirty) ImGui.TextColored(ColAmber, "dirty");
                else if (clip.CanRebake) ImGui.TextColored(ColGreen, "baked");
                else ImGui.TextColored(ColDim, "legacy");
                ImGui.TableNextColumn(); ImGui.Text(clip.Mode.ToString());
                ImGui.TableNextColumn();
                if (refCounts.TryGetValue(clip.Name, out int rc) && rc > 0)
                    ImGui.TextColored(ColAmber, rc.ToString());
                else
                    ImGui.TextColored(ColDim, "0");
            }
            ImGui.EndTable();
        }
    }

    /// <summary>Table header that toggles the library sort for its column (click the same
    /// header again to flip the direction).</summary>
    private void LibHeader(string label, int col)
    {
        string arrow = _libSortCol == col ? (_libSortAsc ? " \u25b2" : " \u25bc") : "";
        ImGui.TableHeader($"{label}{arrow}##libcol{col}");
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (_libSortCol == col) _libSortAsc = !_libSortAsc;
            else { _libSortCol = col; _libSortAsc = true; }
        }
    }

    /// <summary>State badge text shared by the State column and its sort.</summary>
    private static string LibraryState(AnimationClip2D clip)
        => clip.BakeDirty ? "dirty" : clip.CanRebake ? "baked" : "legacy";

    private int CompareLibraryRows((AnimationClip2D clip, int idx) a, (AnimationClip2D clip, int idx) b,
        Dictionary<string, int> refCounts)
    {
        static int Ref(Dictionary<string, int> m, string name)
            => m.TryGetValue(name, out int n) ? n : 0;
        int cmp = _libSortCol switch
        {
            1 => string.Compare(a.clip.GroupPath, b.clip.GroupPath, StringComparison.OrdinalIgnoreCase),
            2 => string.Compare(a.clip.SpriteSheetName, b.clip.SpriteSheetName, StringComparison.OrdinalIgnoreCase),
            3 => a.clip.FPS.CompareTo(b.clip.FPS),
            4 => EditableFrames(a.clip).Count.CompareTo(EditableFrames(b.clip).Count),
            5 => DurationFor(a.clip, EditableFrames(a.clip).Count)
                    .CompareTo(DurationFor(b.clip, EditableFrames(b.clip).Count)),
            6 => a.clip.Loop.CompareTo(b.clip.Loop),
            7 => string.Compare(LibraryState(a.clip), LibraryState(b.clip), StringComparison.OrdinalIgnoreCase),
            8 => string.Compare(a.clip.Mode.ToString(), b.clip.Mode.ToString(), StringComparison.OrdinalIgnoreCase),
            9 => Ref(refCounts, a.clip.Name).CompareTo(Ref(refCounts, b.clip.Name)),
            _ => string.Compare(a.clip.Name, b.clip.Name, StringComparison.OrdinalIgnoreCase),
        };
        // Deterministic tiebreak so equal rows never jitter between frames.
        if (cmp == 0)
            cmp = string.Compare(a.clip.Name, b.clip.Name, StringComparison.OrdinalIgnoreCase);
        return _libSortAsc ? cmp : -cmp;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Bake
    // ═══════════════════════════════════════════════════════════════

    private bool BakeClip(AnimationClip2D clip)
    {
        var source = _spriteEditor.FindSourceSheetPublic(clip.SourceSheetName);
        if (source == null)
        {
            Status($"Bake skipped: source sheet '{clip.SourceSheetName}' is not loaded.");
            return false;
        }
        if (!_spriteEditor.BakeClipForEditor(source, clip, out var baked))
        {
            Status($"Bake failed for '{clip.Name}' — see Console for the reason.");
            return false;
        }

        int oldIdx = _spriteEditor.AnimationClips.IndexOf(clip);
        int newIdx = _spriteEditor.AddOrReplaceClipForEditor(baked);
        if (oldIdx >= 0 && oldIdx == _clipIdx) SelectClip(newIdx);
        _spriteEditor.ForceSynchronizeRegistry();
        _spriteEditor.RequestAtlasRepackForEditor("animation editor bake");
        Status($"Baked '{baked.Name}' → {baked.SpriteSheetName} ({baked.FrameIndices.Count} frames)");
        return true;
    }

    private void BakeAllDirty()
    {
        var names = _spriteEditor.AnimationClips
            .Where(c => c.BakeDirty && c.CanRebake)
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        int ok = 0, failed = 0;
        foreach (string name in names)
        {
            var clip = _spriteEditor.AnimationClips.FirstOrDefault(
                c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (clip == null) continue;
            if (BakeClip(clip)) ok++; else failed++;
        }
        Status(failed == 0
            ? $"Baked {ok} dirty clip(s)."
            : $"Baked {ok} clip(s), {failed} failed — see Console.");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Dialogs: new group / rename group / new clip / new set
    // ═══════════════════════════════════════════════════════════════

    private void DrawDialogs()
    {
        DrawNewGroupDialog();
        DrawRenameGroupDialog();
        DrawNewClipDialog();
        DrawNewSetDialog();
    }

    private void DrawNewGroupDialog()
    {
        if (_openNewGroup) { ImGui.OpenPopup("New Animation Group"); _openNewGroup = false; }

        if (ImGui.BeginPopupModal("New Animation Group", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Group path (use / for nesting):");
            ImGui.SetNextItemWidth(320);
            ImGui.InputText("##newgrouppath", ref _newGroupPath, 128);
            ImGui.TextDisabled("Example: Player/Hero/Locomotion");

            bool valid = !string.IsNullOrWhiteSpace(_newGroupPath);
            ImGui.BeginDisabled(!valid);
            if (ImGui.Button("Create", new Vector2(110, 0)))
            {
                string path = _newGroupPath.Trim().Trim('/');
                _spriteEditor.EnsureAnimationGroup(path);
                _groupFilter = path;
                Status($"Created group '{path}'");
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110, 0))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void DrawRenameGroupDialog()
    {
        if (_openRenameGroup) { ImGui.OpenPopup("Rename Animation Group"); _openRenameGroup = false; }

        if (ImGui.BeginPopupModal("Rename Animation Group", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Rename '{_renameGroupOld}' to:");
            ImGui.SetNextItemWidth(320);
            ImGui.InputText("##renamegroup", ref _renameGroupNew, 128);

            bool valid = !string.IsNullOrWhiteSpace(_renameGroupNew);
            ImGui.BeginDisabled(!valid);
            if (ImGui.Button("Rename", new Vector2(110, 0)))
            {
                string newPath = _renameGroupNew.Trim().Trim('/');
                string oldPath = _renameGroupOld;
                // Renaming a parent also moves its sub-groups (paths are the tree).
                static string Rebase(string p, string from, string to)
                    => string.Equals(p, from, StringComparison.OrdinalIgnoreCase) ? to
                       : p.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase) ? to + p[from.Length..]
                       : p;
                foreach (var g in _spriteEditor.AnimationGroups)
                    g.Path = Rebase(g.Path, oldPath, newPath);
                foreach (var c in _spriteEditor.AnimationClips)
                    c.GroupPath = Rebase(c.GroupPath, oldPath, newPath);
                if (IsInGroup(_groupFilter, oldPath)) _groupFilter = Rebase(_groupFilter, oldPath, newPath);
                Status($"Renamed group '{_renameGroupOld}' → '{newPath}'");
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110, 0))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void OpenNewClipDialog()
    {
        _openNewClip = true;
        _newClipSourceIdx = _spriteEditor.SelectedSourceSheetPublic != null
            ? _spriteEditor.SpriteSheets.IndexOf(_spriteEditor.SelectedSourceSheetPublic) : -1;
        if (_newClipSourceIdx < 0 && _spriteEditor.SpriteSheets.Count > 0) _newClipSourceIdx = 0;

        var source = SourceAt(_newClipSourceIdx);
        var range = _spriteEditor.PreviewRangePublic;
        _newClipStart = range.Start;
        _newClipEnd = range.End;
        if (source != null)
        {
            int max = Math.Max(0, source.FrameCount - 1);
            _newClipStart = Math.Clamp(_newClipStart, 0, max);
            _newClipEnd = Math.Clamp(Math.Max(_newClipEnd, _newClipStart), _newClipStart, max);
        }
        _newClipName = source != null ? $"{source.Name}-clip" : "New Clip";
        _newClipFps = 10f;
        _newClipLoop = true;
        _newClipBake = true;
    }

    private void DrawNewClipDialog()
    {
        if (_openNewClip) { ImGui.OpenPopup("New Animation Clip"); _openNewClip = false; }

        if (ImGui.BeginPopupModal("New Animation Clip", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Name:");
            ImGui.SetNextItemWidth(320);
            ImGui.InputText("##newclipname", ref _newClipName, 128);

            ImGui.Text("Source sheet:");
            DrawSourceCombo("##newclipsource", ref _newClipSourceIdx);
            if (_spriteEditor.SpriteSheets.Count == 0)
                ImGui.TextColored(ColAmber, "No source sheets loaded — import images in the Sprite Editor first (Asset Browser → import, or Sprite Editor → Import Image).");

            var source = SourceAt(_newClipSourceIdx);
            int max = source != null ? Math.Max(0, source.FrameCount - 1) : 0;
            _newClipStart = Math.Clamp(_newClipStart, 0, max);
            _newClipEnd = Math.Clamp(_newClipEnd, _newClipStart, max);

            ImGui.PushItemWidth(100);
            ImGui.DragInt("Start", ref _newClipStart, 1f, 0, max);
            ImGui.SameLine();
            ImGui.DragInt("End", ref _newClipEnd, 1f, _newClipStart, max);
            ImGui.PopItemWidth();

            ImGui.PushItemWidth(120);
            ImGui.DragFloat("FPS##newclip", ref _newClipFps, 0.5f, 1f, 60f, "%.0f");
            ImGui.PopItemWidth();
            ImGui.Checkbox("Loop##newclip", ref _newClipLoop);
            ImGui.Checkbox("Bake after create", ref _newClipBake);

            bool valid = !string.IsNullOrWhiteSpace(_newClipName) && source != null;
            ImGui.Separator();
            ImGui.BeginDisabled(!valid);
            if (ImGui.Button("Create", new Vector2(110, 0)))
            {
                var frames = Enumerable.Range(_newClipStart, _newClipEnd - _newClipStart + 1).ToList();
                var clip = BuildClip(source!, _newClipName.Trim(), frames, _newClipFps, _newClipLoop,
                    string.IsNullOrEmpty(_groupFilter) ? source!.Name : _groupFilter, null, 0f);
                int idx = AddClip(clip, source, _newClipBake);
                SelectClip(idx);
                _spriteEditor.ForceSynchronizeRegistry();
                if (_newClipBake) _spriteEditor.RequestAtlasRepackForEditor("new clip");
                Status($"Created clip '{clip.Name}' ({frames.Count} frames)");
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110, 0))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void OpenNewSetDialog()
    {
        _openNewSet = true;
        _setInitialized = false;
    }

    private void InitSetDialog()
    {
        var source = _spriteEditor.SelectedSourceSheetPublic;
        if (source == null && SelectedClip != null && SelectedClip.CanRebake)
            source = _spriteEditor.FindSourceSheetPublic(SelectedClip.SourceSheetName);
        if (source == null && _spriteEditor.SpriteSheets.Count > 0)
            source = _spriteEditor.SpriteSheets[0];

        _setSourceIdx = source != null ? _spriteEditor.SpriteSheets.IndexOf(source) : -1;
        _setGroup = source?.Name ?? "Animations";
        _setBake = true;

        _setRows.Clear();
        // Data-driven templates: the project's saved rows win, the built-in standard
        // set seeds a fresh project (templates persist in sprites.sheets.json).
        IEnumerable<AnimationSetPreset> presets = _spriteEditor.AnimationTemplates.Count > 0
            ? _spriteEditor.AnimationTemplates
            : AnimationSetPreset.Defaults;
        foreach (var preset in presets)
            _setRows.Add(new SetRow(preset));

        if (source != null)
        {
            if (source.Rows > 1 && source.Columns > 0) FillSetFromRows(source);
            else FillSetFromPreview(source);
        }
        _setInitialized = true;
    }

    private void FillSetFromRows(SpriteSheet source)
    {
        int cols = Math.Max(1, source.Columns);
        int max = Math.Max(0, source.FrameCount - 1);
        for (int i = 0; i < _setRows.Count; i++)
        {
            var row = _setRows[i];
            int start = i * cols;
            int end = start + cols - 1;
            if (start > max) { row.Enabled = false; row.Start = max; row.End = max; continue; }
            row.Start = start;
            row.End = Math.Min(end, max);
        }
    }

    private void FillSetFromPreview(SpriteSheet source)
    {
        var range = _spriteEditor.PreviewRangePublic;
        int max = Math.Max(0, source.FrameCount - 1);
        int start = Math.Clamp(range.Start, 0, max);
        int end = Math.Clamp(Math.Max(range.End, start), start, max);
        foreach (var row in _setRows)
        {
            row.Start = start;
            row.End = end;
        }
    }

    private void DrawNewSetDialog()
    {
        if (_openNewSet) { ImGui.OpenPopup("New Animation Set"); _openNewSet = false; }

        if (ImGui.BeginPopupModal("New Animation Set", ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!_setInitialized) InitSetDialog();

            ImGui.TextWrapped("Create the standard actions for one character in a single step. " +
                "Ranges are prefilled from the sheet's rows (one action per row) or from the current preview range — edit anything below.");

            ImGui.Text("Source sheet:");
            DrawSourceCombo("##setsource", ref _setSourceIdx);
            var source = SourceAt(_setSourceIdx);
            if (_spriteEditor.SpriteSheets.Count == 0)
                ImGui.TextColored(ColAmber, "No source sheets loaded — import images in the Sprite Editor first.");

            ImGui.Text("Group:");
            ImGui.SetNextItemWidth(320);
            ImGui.InputText("##setgroup", ref _setGroup, 128);

            ImGui.SameLine();
            ImGui.Checkbox("Bake all", ref _setBake);

            ImGui.SameLine();
            ImGui.BeginDisabled(source == null);
            if (ImGui.Button("Fill from rows")) { if (source != null) FillSetFromRows(source); }
            ImGui.SameLine();
            if (ImGui.Button("Fill from preview")) { if (source != null) FillSetFromPreview(source); }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Save as template"))
            {
                _spriteEditor.AnimationTemplates = _setRows
                    .Select(r => new AnimationSetPreset
                    {
                        Action = r.Action,
                        Label = r.Label,
                        Fps = r.Fps,
                        Loop = r.Loop,
                        EventName = string.IsNullOrWhiteSpace(r.EventName) ? null : r.EventName.Trim(),
                    })
                    .ToList();
                Status("Saved as the project's animation set template (written on Save).");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Store these rows as the project's default set template —\n" +
                    "the next New Animation Set opens with them instead of the built-in\n" +
                    "Idle/Walk/Run/Attack/Dead list. Persisted in sprites.sheets.json.");

            ImGui.Separator();
            if (ImGui.BeginTable("##setrows", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 34f);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 90f);
                ImGui.TableSetupColumn("Start", ImGuiTableColumnFlags.WidthFixed, 64f);
                ImGui.TableSetupColumn("End", ImGuiTableColumnFlags.WidthFixed, 64f);
                ImGui.TableSetupColumn("FPS", ImGuiTableColumnFlags.WidthFixed, 64f);
                ImGui.TableSetupColumn("Loop / Event", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableHeadersRow();

                int max = source != null ? Math.Max(0, source.FrameCount - 1) : 0;
                for (int i = 0; i < _setRows.Count; i++)
                {
                    var row = _setRows[i];
                    ImGui.TableNextRow();
                    ImGui.PushID($"setrow{i}");
                    ImGui.TableNextColumn();
                    ImGui.Checkbox("##en", ref row.Enabled);
                    ImGui.TableNextColumn();
                    ImGui.Text(row.Label);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(58);
                    ImGui.DragInt("##start", ref row.Start, 1f, 0, max);
                    row.Start = Math.Clamp(row.Start, 0, max);
                    row.End = Math.Clamp(row.End, row.Start, max);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(58);
                    ImGui.DragInt("##end", ref row.End, 1f, row.Start, max);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(58);
                    ImGui.DragFloat("##fps", ref row.Fps, 0.5f, 1f, 60f, "%.0f");
                    ImGui.TableNextColumn();
                    ImGui.Checkbox("Loop", ref row.Loop);
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(90);
                    ImGui.InputTextWithHint("##evt", "event", ref row.EventName, 48);
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }

            ImGui.Separator();
            bool valid = source != null && !string.IsNullOrWhiteSpace(_setGroup) &&
                _setRows.Any(r => r.Enabled);
            ImGui.BeginDisabled(!valid);
            if (ImGui.Button("Create Set", new Vector2(130, 0)))
            {
                CreateSet(source!, _setGroup.Trim());
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110, 0))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void CreateSet(SpriteSheet source, string group)
    {
        _spriteEditor.EnsureAnimationGroup(group);
        int created = 0;
        int firstIdx = -1;
        foreach (var row in _setRows.Where(r => r.Enabled))
        {
            int max = Math.Max(0, source.FrameCount - 1);
            int start = Math.Clamp(row.Start, 0, max);
            int end = Math.Clamp(row.End, start, max);
            if (end < start) continue;
            var frames = Enumerable.Range(start, end - start + 1).ToList();
            string name = $"{source.Name}-{row.Action}";
            var clip = BuildClip(source, name, frames, row.Fps, row.Loop, group,
                string.IsNullOrWhiteSpace(row.EventName) ? null : row.EventName.Trim(), 0.6f);
            int idx = AddClip(clip, source, _setBake);
            if (firstIdx < 0) firstIdx = idx;
            created++;
        }

        _spriteEditor.ForceSynchronizeRegistry();
        if (_setBake) _spriteEditor.RequestAtlasRepackForEditor("animation set");
        if (firstIdx >= 0) SelectClip(firstIdx);
        Status($"Created {created} clip(s) in '{group}'{( _setBake ? " and baked them" : "")}.");
    }

    private AnimationClip2D BuildClip(SpriteSheet source, string name, List<int> frames,
        float fps, bool loop, string group, string? eventName, float eventPercent)
    {
        var clip = new AnimationClip2D
        {
            Name = name,
            SpriteSheetName = source.Name,
            FrameIndices = frames.ToList(),
            SourceSheetName = source.Name,
            SourceFrameIndices = frames.ToList(),
            GroupPath = group,
            Mode = AnimationClipMode.Sprite,
            FPS = MathF.Max(1f, MathF.Round(fps)),
            Loop = loop,
            MasterWidth = source.MasterWidth,
            MasterHeight = source.MasterHeight,
            SpriteOffsetX = source.SpriteOffsetX,
            SpriteOffsetY = source.SpriteOffsetY,
        };

        if (frames.Count > 0 && !string.IsNullOrWhiteSpace(eventName))
        {
            int at = Math.Clamp((int)MathF.Round((frames.Count - 1) * eventPercent), 0, frames.Count - 1);
            clip.Events[at] = eventName!;
        }
        return clip;
    }

    private int AddClip(AnimationClip2D clip, SpriteSheet? source, bool bake)
    {
        if (!string.IsNullOrWhiteSpace(clip.GroupPath))
            _spriteEditor.EnsureAnimationGroup(clip.GroupPath);

        if (bake && source != null && _spriteEditor.BakeClipForEditor(source, clip, out var baked))
            return _spriteEditor.AddOrReplaceClipForEditor(baked);

        // Either the user turned baking off or the bake failed: keep the authored clip
        // visible in the library and flag it so "Bake Dirty" can finish the job.
        clip.BakeDirty = true;
        return _spriteEditor.AddOrReplaceClipForEditor(clip);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Small helpers
    // ═══════════════════════════════════════════════════════════════

    private SpriteSheet? SourceAt(int index)
        => index >= 0 && index < _spriteEditor.SpriteSheets.Count ? _spriteEditor.SpriteSheets[index] : null;

    private void DrawSourceCombo(string id, ref int index)
    {
        var sheets = _spriteEditor.SpriteSheets;
        string preview = index >= 0 && index < sheets.Count ? sheets[index].Name : "(no source sheets)";
        ImGui.SetNextItemWidth(320);
        if (ImGui.BeginCombo(id, preview))
        {
            for (int i = 0; i < sheets.Count; i++)
                if (ImGui.Selectable(sheets[i].Name, i == index)) index = i;
            ImGui.EndCombo();
        }
    }
}
