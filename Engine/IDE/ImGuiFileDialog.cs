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

    /// <summary>The full path the user selected (null if cancelled).</summary>
    public string? SelectedPath { get; private set; }

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

    public void OpenForLoad(string filter = "*.ing", string title = "Open .ing file")
    {
        _mode = DialogMode.Open;
        Title = title;
        _filter = filter;
        _customFilters = filter.Contains(';') ? filter.Split(';', StringSplitOptions.RemoveEmptyEntries) : [filter];
        _fileNameBuffer = "";
        _selectedIdx = -1;
        SelectedPath = null;
        IsConfirmed = false;
        if (Engine.Project.ProjectManager.IsProjectLoaded)
            _currentDir = Engine.Project.ProjectManager.ProjectRoot!;
        Refresh();
    }

    public void OpenForSave(string defaultName = "game.ing")
    {
        _mode = DialogMode.Save;
        Title = "Save .ing file";
        _fileNameBuffer = defaultName;
        _selectedIdx = -1;
        SelectedPath = null;
        IsConfirmed = false;
        if (Engine.Project.ProjectManager.IsProjectLoaded)
            _currentDir = Engine.Project.ProjectManager.ProjectRoot!;
        Refresh();
    }

    public void OpenForPickFolder(string title = "Select Folder")
    {
        _mode = DialogMode.PickFolder;
        Title = title;
        _fileNameBuffer = "";
        _selectedIdx = -1;
        SelectedPath = null;
        IsConfirmed = false;
        if (Engine.Project.ProjectManager.IsProjectLoaded)
            _currentDir = Engine.Project.ProjectManager.ProjectRoot!;
        Refresh();
    }

    public void Close()
    {
        _mode = DialogMode.None;
        IsConfirmed = false;
        SelectedPath = null;
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
                    bool isSel = i == _selectedIdx;
                    if (ImGui.Selectable(_files[i], ref isSel))
                    {
                        _selectedIdx = i;
                        _fileNameBuffer = _files[i];
                    }
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _selectedIdx = i;
                        _fileNameBuffer = _files[i];
                        ConfirmSelection();
                    }
                }
            }

            ImGui.EndChild();

            // ── Folder selection: show "Select This Folder" button ──
            if (_mode == DialogMode.PickFolder)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.5f, 1f), $"Selected: {_currentDir}");
                if (ImGui.Button("Select This Folder", new Vector2(160, 0)))
                {
                    SelectedPath = _currentDir;
                    IsConfirmed = true;
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

            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                SelectedPath = null;
                _mode = DialogMode.None;
            }

            ImGui.EndPopup();
        }

        return _mode != DialogMode.None;
    }

    private void ConfirmSelection()
    {
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
        // NOTE: keep _mode as-is (Save/Open) so the caller can still read
        // IsSaveMode after confirmation. Close() resets it to None.

        Console.WriteLine($"[FileDialog] Selected: {SelectedPath}");
    }
}
