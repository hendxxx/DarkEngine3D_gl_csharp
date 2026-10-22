using ImGuiNET;
using System.Numerics;
using System.Text;

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

    // Tail-follow state, kept ACROSS frames. Auto-scroll used to be re-decided every frame as
    // "is the view already within 20px of the end?" — a log that is not at the end right now
    // never gets pulled there, so a console found at the top (or left there by a scroll, a
    // re-docked window, a resize) stayed at the top with Auto-scroll checked. Following the
    // tail is now a state: on by default, and only the user scrolling back turns it off.
    private bool _followTail = true;
    private float _lastScrollY;
    // False while the panel is hidden: its window is gone then, so the scroll it comes back
    // with says nothing about what the user did.
    private bool _drawnLastFrame;

    private readonly List<LogEntry> _entries = [];
    private readonly object _lock = new();
    private readonly StringWriter _captureWriter;
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;

    private static readonly Vector4 ColorInfo = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Vector4 ColorWarning = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorError = new(1f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 ColorDebug = new(0.5f, 0.6f, 0.8f, 1f);

    public ConsolePanel(IDEBridge bridge)
    {
        _bridge = bridge;

        // Save original console writers so we can still output to the system terminal
        _originalOut = Console.Out;
        _originalError = Console.Error;

        // Create a dual writer that writes to both the system console AND the capture buffer
        _captureWriter = new StringWriter();
        var dualOut = new DualTextWriter(_originalOut, _captureWriter);
        var dualError = new DualTextWriter(_originalError, _captureWriter);
        Console.SetOut(dualOut);
        Console.SetError(dualError);

        // Hook AppDomain unhandled exceptions to log full stack trace
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Console.Error.WriteLine($"[FATAL] Unhandled exception: {ex}\nStack: {ex?.StackTrace}");
        };

        // Hook TaskScheduler unobserved task exceptions
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Console.Error.WriteLine($"[FATAL] Unobserved task exception: {args.Exception}\nStack: {args.Exception.StackTrace}");
            args.SetObserved();
        };

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
            try
            {
                Thread.Sleep(50);

                string text;
                lock (_lock)
                {
                    // Read and clear under lock to prevent race with Console.Write
                    text = _captureWriter.ToString();
                    _captureWriter.GetStringBuilder().Clear();
                }

                if (text.Length > 0)
                {
                    // Split by newlines and add each line (no lock needed — _entries is only accessed here and in Render under _lock)
                    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    lock (_lock)
                    {
                        foreach (var line in lines)
                        {
                            var trimmed = line.TrimEnd('\r');
                            if (trimmed.Length > 0)
                                _entries.Add(new LogEntry(trimmed, Classify(trimmed)));
                        }

                        // Limit entries to prevent memory leak
                        if (_entries.Count > 5000)
                            _entries.RemoveRange(0, _entries.Count - 5000);
                    }
                }
            }
            catch (Exception ex)
            {
                // Critical: swallow any exception so the background thread never dies.
                // If the thread dies, the StringWriter never gets cleared → memory leak.
                // Log full stack trace to file for debugging.
                try
                {
                    System.IO.File.AppendAllText("console_capture_error.log",
                        $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===\n" +
                        $"Type: {ex.GetType().FullName}\n" +
                        $"Message: {ex.Message}\n" +
                        $"Source: {ex.Source}\n" +
                        $"Stack:\n{ex.StackTrace}\n" +
                        $"Inner: {ex.InnerException}\n\n");
                }
                catch { }
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
        if (!_visible)
        {
            _drawnLastFrame = false;
            return;
        }

        ImGui.Begin("Console", ref _visible);
        IDE.PanelFocus.Notify("Console");

        // ── Toolbar ──
        bool autoScrollWasOn = _autoScroll;
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);
        if (_autoScroll && !autoScrollWasOn) _followTail = true; // re-checked = follow again
        ImGui.SameLine();
        ImGui.InputText("Filter", ref _filter, 256);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            lock (_lock) _entries.Clear();
            _followTail = true;
            _lastScrollY = 0f;
        }

        ImGui.Separator();

        // ── Log entries ──
        lock (_lock)
        {
            ImGui.BeginChild("LogScroll", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

            // ImGui reports, at the top of a child, the scroll position the user saw at the end
            // of the last frame. The view only ever moves UP because the user scrolled there
            // (new lines never pull it up), so that is what pauses the tail; content arriving
            // must not. Touching the end again (by hand) resumes it.
            float scrollY = ImGui.GetScrollY();
            float scrollMaxY = ImGui.GetScrollMaxY();
            // "At the end" is measured in lines, not pixels: a line arriving between the user's
            // last scroll and this frame must not count as them having left the end.
            float tailSlack = MathF.Max(2f, ImGui.GetTextLineHeight() * 3f);
            bool atEnd = scrollY >= scrollMaxY - tailSlack;
            if (_drawnLastFrame && !atEnd && scrollY < _lastScrollY - 1f)
                _followTail = false;                                 // user scrolled up
            else if (!_followTail && atEnd) _followTail = true;      // back at the end
            _lastScrollY = scrollY;

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

            // SetScrollHereY targets the bottom of the last line, so it lands on the true end
            // of the content no matter how many lines arrived since the previous frame.
            if (_autoScroll && _followTail)
                ImGui.SetScrollHereY(1f);

            ImGui.EndChild();
        }

        ImGui.End();
        _drawnLastFrame = true;
    }

    private class LogEntry(string text, LogLevel level)
    {
        public string Text { get; } = text;
        public LogLevel Level { get; } = level;
    }

    private enum LogLevel { Info, Warning, Error, Debug }
}

/// <summary>
/// A TextWriter that duplicates output to two underlying writers.
/// Used so Console output goes to BOTH the system terminal and the capture buffer.
/// </summary>
internal class DualTextWriter : TextWriter
{
    private readonly TextWriter _first;
    private readonly TextWriter _second;

    public DualTextWriter(TextWriter first, TextWriter second)
    {
        _first = first;
        _second = second;
    }

    public override Encoding Encoding => _first.Encoding;

    public override void Write(char value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void Write(string? value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _first.WriteLine(value);
        _second.WriteLine(value);
    }

    public override void Flush()
    {
        _first.Flush();
        _second.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _first.Dispose();
            _second.Dispose();
        }
        base.Dispose(disposing);
    }
}
