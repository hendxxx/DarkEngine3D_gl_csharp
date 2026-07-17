using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Console panel — captures Console.WriteLine output and displays
/// in an ImGui text window with filtering and log levels.
/// </summary>
public class ConsolePanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;
    private bool _autoScroll = true;
    private string _filter = "";

    private readonly List<LogEntry> _entries = [];
    private readonly object _lock = new();
    private readonly StringWriter _captureWriter;

    private static readonly Vector4 ColorInfo = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Vector4 ColorWarning = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorError = new(1f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 ColorDebug = new(0.5f, 0.6f, 0.8f, 1f);

    public ConsolePanel(IDEBridge bridge)
    {
        _bridge = bridge;

        // Capture Console output
        _captureWriter = new StringWriter();
        Console.SetOut(_captureWriter);

        // Start background reader
        var thread = new Thread(ReadConsoleLoop)
        {
            IsBackground = true,
            Name = "ConsoleCapture"
        };
        thread.Start();
    }

    public void ShowInMenu() => ImGui.MenuItem("Console", null, ref _visible);

    private void ReadConsoleLoop()
    {
        while (true)
        {
            Thread.Sleep(50);
            var text = _captureWriter.ToString();
            if (text.Length > 0)
            {
                lock (_lock)
                {
                    // Split by newlines and add each line
                    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.TrimEnd('\r');
                        if (trimmed.Length > 0)
                            _entries.Add(new LogEntry(trimmed, Classify(trimmed)));
                    }

                    // Limit entries to prevent memory leak
                    if (_entries.Count > 5000)
                        _entries.RemoveRange(0, _entries.Count - 5000);

                    // Clear the StringWriter
                    _captureWriter.GetStringBuilder().Clear();
                }
            }
        }
    }

    private static LogLevel Classify(string line)
    {
        var upper = line.ToUpperInvariant();
        if (upper.Contains("ERROR") || upper.Contains("FAILED") || upper.Contains("CRASH"))
            return LogLevel.Error;
        if (upper.Contains("WARN") || upper.Contains("WARNING"))
            return LogLevel.Warning;
        if (upper.Contains("DEBUG") || upper.Contains("[DEBUG]"))
            return LogLevel.Debug;
        return LogLevel.Info;
    }

    public void Render()
    {
        if (!_visible) return;

        ImGui.Begin("Console", ref _visible);

        // ── Toolbar ──
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);
        ImGui.SameLine();
        ImGui.InputText("Filter", ref _filter, 256);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            lock (_lock) _entries.Clear();
        }

        ImGui.Separator();

        // ── Log entries ──
        lock (_lock)
        {
            ImGui.BeginChild("LogScroll", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

            foreach (var entry in _entries)
            {
                if (!string.IsNullOrEmpty(_filter) &&
                    !entry.Text.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var color = entry.Level switch
                {
                    LogLevel.Error => ColorError,
                    LogLevel.Warning => ColorWarning,
                    LogLevel.Debug => ColorDebug,
                    _ => ColorInfo
                };

                ImGui.TextColored(color, entry.Text);
            }

            if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20f)
                ImGui.SetScrollHereY(1f);

            ImGui.EndChild();
        }

        ImGui.End();
    }

    private class LogEntry(string text, LogLevel level)
    {
        public string Text { get; } = text;
        public LogLevel Level { get; } = level;
    }

    private enum LogLevel { Info, Warning, Error, Debug }
}
