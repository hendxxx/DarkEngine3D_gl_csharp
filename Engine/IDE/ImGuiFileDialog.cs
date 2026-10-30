using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE;

/// <summary>
/// Simple ImGui-based file dialog for opening/saving .ing files.
/// Usage:
///   _fileDialog.OpenForLoad();  // or OpenForSave(defaultName)
///   // In Render():
///   _fileDialog.Render();
///   if (_fileDialog.IsConfirmed) { string path = _fileDialog.SelectedPath; ... }
/// </summary>
public class ImGuiFileDialog
{
    private enum DialogMode { None, Open, Save, PickFolder }
    private DialogMode _mode = DialogMode.None;

    private string _currentDir;
    private string _filter = "*.ing";
    private string[] _customFilters = [];
    private string _fileNameBuffer = "";
    private string[] _files = [];
    private string[] _dirs = [];
    private int _selectedIdx = -1;

    private const int InputBufSize = 256;

    /// <summary>Default folder to create (for "New Folder" button in pick-folder mode).</summary>
    public string NewFolderName { get; set; } = "New Folder";

    /// <summary>Whether the "New Folder" button is shown in pick-folder mode.</summary>
    public bool ShowNewFolderButton { get; set; } = true;

    /// <summary>Button width for action buttons (Select/Open/Save and Cancel).</summary>
    public float ActionButtonWidth { get; set; } = 160f;

    /// <summary>The full path the user selected (null if cancelled).</summary>
    public string? SelectedPath { get; private set; }

    // ── MULTI-SELECT (Open mode) ──
    /// <summary>When opened via OpenForLoadMulti the file list supports Ctrl+Click
    /// (toggle) and Shift+Click (range) multi-selection; confirming returns ALL of them.</summary>
    private bool _multiSelect;
    private readonly List<string> _selectedFiles = [];
    /// <summary>Primary index of the last multi-select click — the Shift+Click range
    /// anchor (previous selection, before this click's clear/re-add).</summary>
    private int _lastMultiIdx = -1;
    /// <summary>All confirmed selections in multi-select mode (null = none/overwrite).
    /// In single-select mode this mirrors SelectedPath as a 1-element list for callers
    /// that want one code path.</summary>
    public IReadOnlyList<string>? SelectedPaths { get; private set; }

    // ════════════════ LAST-FOLDER MEMORY (open-last folder) ════════════════
    /// <summary>Remembered folder PER DIALOG PURPOSE (key = explicit dirKey, else the
    /// title). Written when the user CONFIRMS a choice and read the next time the same
    /// dialog opens — re-importing sheets or re-picking a texture never forces the user to
    /// navigate back from the project root again. Persisted per project in
    /// `file_dialog_history.json` (IDE-local state, like settings.json / imgui.ini).</summary>
    private static readonly Dictionary<string, string> _lastDirs = new(StringComparer.OrdinalIgnoreCase);
    private static string _historyPath = "";
    private string _dirKey = "";

    private static string HistoryPath()
    {
        string dir = Engine.Project.ProjectManager.IsProjectLoaded &&
            !string.IsNullOrEmpty(Engine.Project.ProjectManager.ProjectRoot)
            ? Engine.Project.ProjectManager.ProjectRoot!
            : AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(dir, "file_dialog_history.json");
    }

    /// <summary>Load the remembered folders once per project (a project switch swaps the
    /// file, so the cache reloads instead of leaking the previous project's folders).</summary>
    private static void EnsureHistoryLoaded()
    {
        string path = HistoryPath();
        if (string.Equals(path, _historyPath, StringComparison.OrdinalIgnoreCase)) return;
        _lastDirs.Clear();
        _historyPath = path;
        try
        {
            if (!File.Exists(path)) return;
            var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(path));
            if (loaded == null) return;
            foreach (var kv in loaded)
                if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    _lastDirs[kv.Key] = kv.Value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileDialog] history read failed: {ex.Message}");
        }
    }

    private static void SaveHistory()
    {
        try
        {
            if (string.IsNullOrEmpty(_historyPath)) return;
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_historyPath, System.Text.Json.JsonSerializer.Serialize(_lastDirs, opts));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileDialog] history write failed: {ex.Message}");
        }
    }

    /// <summary>Remember the folder of a confirmed choice for this dialog purpose.
    /// <paramref name="isFile"/> = the value is a FILE (remember its parent folder) rather
    /// than a folder itself.</summary>
    private void RememberDir(string? dirOrFile, bool isFile)
    {
        if (string.IsNullOrWhiteSpace(dirOrFile) || string.IsNullOrEmpty(_dirKey)) return;
        string? dir = isFile ? Path.GetDirectoryName(dirOrFile) : dirOrFile;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        _lastDirs[_dirKey] = dir;
        SaveHistory();
    }

    /// <summary>Folder the dialog opens in: the REMEMBERED one first, then the caller's
    /// start folder (folder of the value being edited), then the project root.</summary>
    private string ResolveStartDir(string? startDir)
    {
        EnsureHistoryLoaded();
        if (!string.IsNullOrEmpty(_dirKey) &&
            _lastDirs.TryGetValue(_dirKey, out var remembered) &&
            !string.IsNullOrEmpty(remembered) && Directory.Exists(remembered))
            return remembered;
        if (!string.IsNullOrEmpty(startDir) && Directory.Exists(startDir))
            return startDir!;
        if (Engine.Project.ProjectManager.IsProjectLoaded &&
            !string.IsNullOrEmpty(Engine.Project.ProjectManager.ProjectRoot))
            return Engine.Project.ProjectManager.ProjectRoot!;
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    /// <summary>Open an Open-mode dialog with MULTI file selection. Filter syntax is
    /// the same as OpenForLoad ("*.png;*.jpg"). Click = select one, Ctrl+Click =
    /// toggle, Shift+Click = extend the range; Open confirms every selected file.</summary>
    public void OpenForLoadMulti(string filter, string title = "Open files",
        string? startDir = null, string? dirKey = null)
    {
        OpenForLoad(filter, title, startDir, dirKey);
        _multiSelect = true;
        _selectedFiles.Clear();
        _lastMultiIdx = -1;
    }

    /// <summary>True when the user confirmed a selection this frame. Check SelectedPath after.</summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>The title shown in the dialog window.</summary>
    public string Title { get; set; } = "Select .ing file";

    public ImGuiFileDialog()
    {
        _currentDir = AppDomain.CurrentDomain.BaseDirectory;
        Refresh();
    }

    /// <summary>True if the dialog is in Save mode (vs Open mode).</summary>
    public bool IsSaveMode => _mode == DialogMode.Save;

    /// <param name="startDir">Optional folder when there is no remembered one yet — pass the
    /// folder of the value being edited so re-picking starts where the asset lives.</param>
    /// <param name="dirKey">Optional stable key for the remembered folder (default = title),
    /// so several dialogs that pick the same kind of asset share one memory.</param>
    public void OpenForLoad(string filter = "*.ing", string title = "Open .ing file",
        string? startDir = null, string? dirKey = null)
    {
        _mode = DialogMode.Open;
        Title = title;
        _dirKey = dirKey ?? title;
        _filter = filter;
        _customFilters = filter.Contains(';') ? filter.Split(';', StringSplitOptions.RemoveEmptyEntries) : [filter];
        _fileNameBuffer = "";
        _selectedIdx = -1;
        SelectedPath = null;
        SelectedPaths = null;
        _multiSelect = false;
        _selectedFiles.Clear();
        IsConfirmed = false;
        _currentDir = ResolveStartDir(startDir);
        Refresh();
    }

    /// <param name="filter">Optional file filter (e.g. "*.json"). When null the
    /// current filter is kept — existing callers keep their old behavior.</param>
    /// <param name="title">Optional dialog title (default "Save .ing file").</param>
    /// <param name="startDir">Optional starting folder (falls back to project root).
    /// Lets "Save As" open in the folder of the file being saved.</param>
    public void OpenForSave(string defaultName = "game.ing", string? filter = null,
        string? title = null, string? startDir = null, string? dirKey = null)
    {
        _mode = DialogMode.Save;
        Title = title ?? "Save .ing file";
        _dirKey = dirKey ?? Title;
        if (filter != null)
        {
            _filter = filter;
            _customFilters = filter.Contains(';') ? filter.Split(';', StringSplitOptions.RemoveEmptyEntries) : [filter];
        }
        _fileNameBuffer = defaultName;
        _selectedIdx = -1;
        SelectedPath = null;
        IsConfirmed = false;
        _currentDir = ResolveStartDir(startDir);
        Refresh();
    }

    public void OpenForPickFolder(string title = "Select Folder", string? startDir = null)
    {
        _mode = DialogMode.PickFolder;
        Title = title;
        _dirKey = title;
        _fileNameBuffer = "";
        _selectedIdx = -1;
        SelectedPath = null;
        IsConfirmed = false;
        _currentDir = ResolveStartDir(startDir);
        Refresh();
    }

    public void Close()
    {
        _mode = DialogMode.None;
        IsConfirmed = false;
        SelectedPath = null;
        SelectedPaths = null;
        _multiSelect = false;
        _selectedFiles.Clear();
    }

    private void Refresh()
    {
        try
        {
            var di = new DirectoryInfo(_currentDir);
            _dirs = di.GetDirectories()
                .Select(d => d.Name)
                .OrderBy(n => n)
                .ToArray();

            // Support multiple filters separated by semicolons
            if (_customFilters.Length > 1)
            {
                var fileSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in _customFilters)
                {
                    try
                    {
                        foreach (var fi in di.GetFiles(f))
                            fileSet.Add(fi.Name);
                    }
                    catch { /* skip unsupported filter patterns */ }
                }
                _files = fileSet.ToArray();
            }
            else
            {
                _files = di.GetFiles(_filter)
                    .Select(f => f.Name)
                    .OrderBy(n => n)
                    .ToArray();
            }
        }
        catch
        {
            _dirs = [];
            _files = [];
        }
    }

    /// <summary>Call this each frame to render the dialog if open. Returns true while open.</summary>
    public bool Render()
    {
        if (_mode == DialogMode.None) return false;

        // Reset one-shot flags at start of frame
        IsConfirmed = false;

        ImGui.OpenPopup(Title);
        bool open = true;

        Vector2 windowSize = new(600, 400);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.Once);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Once, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal(Title, ref open, ImGuiWindowFlags.NoDocking))
        {
            // ── Breadcrumb + parent navigation ──
            {
                if (_currentDir.Length > 3)
                {
                    if (ImGui.Button("⬆ .."))
                    {
                        _currentDir = Directory.GetParent(_currentDir)?.FullName ?? _currentDir;
                        Refresh();
                    }
                    ImGui.SameLine();
                }

                string shortDir = _currentDir.Length > 60 ? "..." + _currentDir[^60..] : _currentDir;
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), shortDir);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_currentDir);
            }

            // ── New Folder button (pick-folder mode only) ──
            if (_mode == DialogMode.PickFolder && ShowNewFolderButton)
            {
                if (ImGui.Button("➕ New Folder", new Vector2(130f, 0)))
                {
                    string folderName = NewFolderName.Trim();
                    if (!string.IsNullOrEmpty(folderName))
                    {
                        try
                        {
                            string newPath = Path.Combine(_currentDir, folderName);
                            Directory.CreateDirectory(newPath);
                            Refresh();
                        }
                        catch (Exception)
                        {
                            // Failed to create folder (e.g. permission / already exists).
                        }
                    }
                }
                ImGui.SameLine();
            }

            // ── Filter (file modes only) ──
            if (_mode != DialogMode.PickFolder)
            {
                ImGui.SetNextItemWidth(120);
                if (ImGui.BeginCombo("##filter", _filter))
                {
                    // Build filter list: custom filters + common defaults
                    var filterList = new List<string>(_customFilters);
                    if (!filterList.Contains("*.*")) filterList.Add("*.*");
                    if (!filterList.Contains("*.ing")) filterList.Add("*.ing");
                    foreach (var f in filterList)
                    {
                        bool isF = _filter == f;
                        if (ImGui.Selectable(f, isF))
                        {
                            _filter = f;
                            _customFilters = [f];
                            Refresh();
                        }
                    }
                    ImGui.EndCombo();
                }
            }

            ImGui.Separator();

            // ── File list ──
            ImGui.BeginChild("##file_list", new Vector2(0, -60), ImGuiChildFlags.None, ImGuiWindowFlags.None);

            // Directories
            for (int di = 0; di < _dirs.Length; di++)
            {
                string dir = _dirs[di];
                if (ImGui.Selectable($"[Dir] {dir}"))
                {
                    _currentDir = Path.Combine(_currentDir, dir);
                    _selectedIdx = -1;
                    Refresh();
                }
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _currentDir = Path.Combine(_currentDir, dir);
                    _selectedIdx = -1;
                    Refresh();
                }
            }

            // Files (only in file modes)
            if (_mode != DialogMode.PickFolder)
            {
                ImGui.Separator();
                for (int i = 0; i < _files.Length; i++)
                {
                    bool isSel = i == _selectedIdx ||
                        (_multiSelect && _selectedFiles.Contains(_files[i]));
                    if (ImGui.Selectable(_files[i], ref isSel))
                    {
                        _selectedIdx = i;
                        _fileNameBuffer = _files[i];
                        if (_multiSelect)
                        {
                            // Multi-select: plain click = only this file; Ctrl+Click =
                            // toggle it in/out; Shift+Click = extend the range from the
                            // last confirmed primary. Selection = file NAMES; paths are
                            // rebuilt from the current dir on confirm.
                            var io = ImGui.GetIO();
                            if (io.KeyCtrl)
                            {
                                if (!_selectedFiles.Remove(_files[i]))
                                    _selectedFiles.Add(_files[i]);
                            }
                            else if (io.KeyShift && _lastMultiIdx >= 0 && _lastMultiIdx < _files.Length)
                            {
                                // Range from the previous primary (post-click it equals i,
                                // so capture via the last add before clearing).
                                _selectedFiles.Clear();
                                int lo = Math.Min(_lastMultiIdx, i), hi = Math.Max(_lastMultiIdx, i);
                                for (int k = lo; k <= hi; k++)
                                    _selectedFiles.Add(_files[k]);
                            }
                            else
                            {
                                _selectedFiles.Clear();
                                _selectedFiles.Add(_files[i]);
                            }
                            _lastMultiIdx = i;
                        }
                    }
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _selectedIdx = i;
                        _fileNameBuffer = _files[i];
                        if (_multiSelect && !_selectedFiles.Contains(_files[i]))
                            _selectedFiles.Add(_files[i]);
                        ConfirmSelection();
                    }
                }
            }

            ImGui.EndChild();

            // ── Folder selection: show "Select This Folder" button ──
            if (_mode == DialogMode.PickFolder)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.5f, 1f), $"Selected: {_currentDir}");
                ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ActionButtonWidth * 2f) / 2f);
                if (ImGui.Button("Select This Folder", new Vector2(ActionButtonWidth, 0)))
                {
                    SelectedPath = _currentDir;
                    IsConfirmed = true;
                    RememberDir(_currentDir, isFile: false);
                    _mode = DialogMode.None;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(ActionButtonWidth, 0)))
                {
                    SelectedPath = null;
                    _mode = DialogMode.None;
                }
            }

            // ── File name input (Save mode only) ──
            if (_mode == DialogMode.Save)
            {
                ImGui.Text("File name:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(300);
                ImGui.InputText("##filename", ref _fileNameBuffer, InputBufSize);
            }

            // ── Action buttons ──
            if (_mode != DialogMode.PickFolder)
            {
                string confirmLabel = _mode == DialogMode.Open ? "Open" : "Save";
                if (ImGui.Button(confirmLabel, new Vector2(100, 0)))
                {
                    ConfirmSelection();
                }
                ImGui.SameLine();
            } 

            ImGui.EndPopup();
        }

        // The window's ✕ (X) button sets ref open=false — mark the dialog closed
        // (and the popup ID released) so callers polling Render() see a real cancel.
        if (!open)
        {
            SelectedPath = null;
            IsConfirmed = false;
            _mode = DialogMode.None;
        }

        return _mode != DialogMode.None;
    }

    private void ConfirmSelection()
    {
        // Multi-select Open: confirmed = EVERY selected file in the current dir.
        if (_multiSelect && _mode == DialogMode.Open && _selectedFiles.Count > 0)
        {
            var paths = _selectedFiles
                .Select(f => Path.Combine(_currentDir, f))
                .Where(File.Exists)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (paths.Count > 0)
            {
                SelectedPaths = paths;
                SelectedPath = paths[0];
                IsConfirmed = true;
                RememberDir(_currentDir, isFile: false);   // next open starts here
                Console.WriteLine($"[FileDialog] Multi-selected: {paths.Count} file(s)");
                return;
            }
            return; // nothing valid to confirm
        }
        // Single-select: mirror the path into the 1-element list for uniform callers.
        SelectedPaths = null;

        string fileName = _fileNameBuffer.Trim();
        if (string.IsNullOrEmpty(fileName))
        {
            SelectedPath = null;
            return;
        }

        // Add default extension only for Save mode (Open mode picks existing files)
        if (_mode == DialogMode.Save)
        {
            string ext = Path.GetExtension(_filter); // e.g. ".ing" from "*.ing"
            if (!string.IsNullOrEmpty(ext) &&
                !fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                fileName += ext;
        }

        SelectedPath = Path.Combine(_currentDir, fileName);
        IsConfirmed = true;
        RememberDir(SelectedPath, isFile: true);   // next open starts in this folder
        // NOTE: keep _mode as-is (Save/Open) so the caller can still read
        // IsSaveMode after confirmation. Close() resets it to None.

        Console.WriteLine($"[FileDialog] Selected: {SelectedPath}");
    }
}
