using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Dialogue Editor — designer-facing editor for the data-driven dialogue system.
/// Creates/edits/duplicates/deletes dialogue assets, authors nodes (text, speaker,
/// portrait, emotion, choices, auto-advance, start/end actions), manages reusable
/// speakers and themes, sets the active localization language, and previews the
/// selected asset through the live DialogueSystem (drawn in the viewport HUD).
/// No coding required — everything lives in Assets/Dialogue/dialogues.json.
/// </summary>
public class DialogueEditorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    // Selection state
    private int _selectedAsset = -1;
    private int _selectedNode = -1;
    private int _selectedSpeaker = -1;
    private int _selectedTheme = -1;
    private bool _previewMode = true;

    // Localization scratch
    private int _editLanguage;
    private string _locKey = "";
    private string _locValue = "";

    // ── Graph View state ──
    private bool _graphMode;
    private Vector2 _graphPan = new(80f, 40f);
    private float _graphZoom = 1f;
    private int _graphCtxNode = -1;           // node the context menu opened on (frozen)
    private int _graphDragNode = -1;          // node being dragged (index)
    private int _graphLinkSrc = -1;           // connection drag: source node index
    private int _graphLinkChoice = -1;        // connection drag: choice index (-1 = Next port)
    // Smooth pan animation (double-click focus).
    private bool _graphPanAnimating;
    private float _graphPanAnim;              // 0..1 progress
    private Vector2 _graphPanFrom, _graphPanTo;
    private Vector2 _graphDragOffset;         // mouse→node grab offset (graph space)
    private bool _graphPanning;
    private Vector2 _graphPanStart;
    private Vector2 _graphMouseAtPanStart;
    // Node box size in graph units (before zoom).
    private const float GraphNodeW = 190f;
    private const float GraphNodeH = 74f;

    public DialogueEditorPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Dialogue Editor", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;
        ImGui.SetNextWindowSize(new Vector2(860, 620), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Dialogue Editor", ref _visible))
        { ImGui.End(); return; }
        IDE.PanelFocus.Notify("Dialogue Editor");

        DialogueLibrary.EnsureLoaded();

        // ── Toolbar: add / duplicate / delete / save / preview ──
        if (ImGui.Button("+ Asset")) { DialogueLibrary.AddAsset(); _selectedAsset = DialogueLibrary.Assets.Count - 1; _selectedNode = 0; }
        ImGui.SameLine();
        if (ImGui.Button("Duplicate") && _selectedAsset >= 0 && _selectedAsset < DialogueLibrary.Assets.Count)
            DialogueLibrary.DuplicateAsset(DialogueLibrary.Assets[_selectedAsset].Id);
        ImGui.SameLine();
        if (ImGui.Button("Delete") && _selectedAsset >= 0 && _selectedAsset < DialogueLibrary.Assets.Count)
        {
            DialogueLibrary.RemoveAsset(DialogueLibrary.Assets[_selectedAsset].Id);
            _selectedAsset = -1; _selectedNode = -1;
        }
        ImGui.SameLine();
        if (ImGui.Button("Save")) DialogueLibrary.Save();
        ImGui.SameLine();
        if (_graphMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.4f, 0.55f, 1f));
            if (ImGui.Button("≡ List")) _graphMode = false;
            ImGui.PopStyleColor();
        }
        else if (ImGui.Button("⬡ Graph")) _graphMode = true;
        ImGui.SameLine();
        ImGui.Checkbox("Preview", ref _previewMode);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Play the selected dialogue in the viewport (needs a play session with a Player2D + map, or just renders the window).");
        ImGui.SameLine();
        if (_previewMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.45f, 0.2f, 1f));
            if (ImGui.Button("▶ Start"))
            {
                if (_selectedAsset >= 0 && _selectedAsset < DialogueLibrary.Assets.Count)
                {
                    DialogueSystem.EndConversationNow();
                    DialogueSystem.StartConversation(DialogueLibrary.Assets[_selectedAsset]);
                }
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Button("■ Stop")) DialogueSystem.EndConversationNow();
        }
        ImGui.Separator();

        // ── Left: asset list / Right: editor ──
        float listW = 210f;
        ImGui.BeginChild("asset_list", new Vector2(listW, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Dialogue Assets");
        ImGui.Separator();
        for (int i = 0; i < DialogueLibrary.Assets.Count; i++)
        {
            var a = DialogueLibrary.Assets[i];
            ImGui.PushID($"asset{i}");
            if (ImGui.Selectable($"{a.Name}##{a.Id}", i == _selectedAsset))
            { _selectedAsset = i; _selectedNode = 0; }
            ImGui.PopID();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginGroup();
        if (_selectedAsset >= 0 && _selectedAsset < DialogueLibrary.Assets.Count)
        {
            RenderAssetEditor(DialogueLibrary.Assets[_selectedAsset]);

            ImGui.Separator();
            RenderSpeakersSection();
            ImGui.Separator();
            RenderThemesSection();
            ImGui.Separator();
            RenderLocalizationSection();
        }
        else
        {
            ImGui.TextDisabled("Select or create a dialogue asset.");
        }
        ImGui.EndGroup();

        ImGui.End();
    }

    // ── Asset editor ───────────────────────────────

    private void RenderAssetEditor(DialogueAsset asset)
    {
        ImGui.PushID($"ed_{asset.Id}");
        string name = asset.Name;
        if (ImGui.InputText("Name", ref name, 64)) asset.Name = name;
        string id = asset.Id;
        if (ImGui.InputText("Id (used by triggers/NPCs)", ref id, 64) && !string.IsNullOrWhiteSpace(id))
            asset.Id = id.Trim();
        string start = asset.StartNodeId;
        if (ImGui.InputText("Start Node", ref start, 64)) asset.StartNodeId = start.Trim();

        // Theme picker
        var themes = DialogueLibrary.GetThemeNames();
        int themeIdx = themes.IndexOf(asset.ThemeName);
        if (themeIdx < 0) themeIdx = 0;
        if (ImGui.BeginCombo("Theme", themes.Count > 0 ? themes[themeIdx] : "Default"))
        {
            for (int i = 0; i < themes.Count; i++)
            {
                bool sel = i == themeIdx;
                if (ImGui.Selectable(themes[i], sel)) asset.ThemeName = themes[i];
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.Separator();

        if (_graphMode)
        {
            // ── Graph View: nodes as boxes, routing as arrows ──
            if (ImGui.Button("+ Node"))
            {
                asset.Nodes.Add(new DialogueNode { Id = UniqueNodeId(asset, "node"), GraphX = 40f, GraphY = 40f + asset.Nodes.Count * 90f });
                _selectedNode = asset.Nodes.Count - 1;
            }
            // (graph pan uses middle-mouse below)
            ImGui.SameLine();
            if (ImGui.Button("Auto Arrange")) AutoArrangeGraph(asset);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Re-layout all nodes in BFS columns from the Start node.");
            ImGui.SameLine();
            if (ImGui.Button("Center")) CenterOnNode(asset, null);
            ImGui.SameLine();
            ImGui.TextDisabled($"{asset.Nodes.Count} nodes · drag=move · ● port→node=connect · LMB canvas=pan · wheel=zoom · RMB node=menu");

            RenderGraphView(asset);
        }
        else
        {
            ImGui.TextDisabled($"Nodes ({asset.Nodes.Count})");
            // Node list (horizontal select)
            for (int i = 0; i < asset.Nodes.Count; i++)
            {
                ImGui.PushID($"node{i}");
                var n = asset.Nodes[i];
                string label = $"{i}: {n.Id}" + (string.IsNullOrEmpty(n.SpeakerId) ? "" : $" [{n.SpeakerId}]") + (n.Choices.Count > 0 ? " +" : "");
                if (ImGui.Selectable(label, i == _selectedNode)) _selectedNode = i;
                if (ImGui.BeginPopupContextItem("node_ctx"))
                {
                    if (ImGui.MenuItem("Duplicate") && n != null)
                        asset.Nodes.Insert(i + 1, CloneWithNewId(n, asset));
                    if (ImGui.MenuItem("Delete") && asset.Nodes.Count > 1)
                    {
                        asset.Nodes.RemoveAt(i);
                        _selectedNode = Math.Min(_selectedNode, asset.Nodes.Count - 1);
                        ImGui.EndPopup();
                        ImGui.PopID();
                        continue;
                    }
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }
            if (ImGui.Button("+ Node"))
            {
                asset.Nodes.Add(new DialogueNode { Id = UniqueNodeId(asset, "node") });
                _selectedNode = asset.Nodes.Count - 1;
            }
        }

        // Node editor (both modes)
        if (_selectedNode >= 0 && _selectedNode < asset.Nodes.Count)
            RenderNodeEditor(asset, asset.Nodes[_selectedNode]);
        ImGui.PopID();
    }

    // ── Graph View ───────────────────────────────

    /// <summary>BFS from the Start node → column layout. Orphans (unreachable) go to
    /// the last columns so nothing overlaps at origin.</summary>
    private static void AutoArrangeGraph(DialogueAsset asset)
    {
        if (asset.Nodes.Count == 0) return;
        int[] depth = new int[asset.Nodes.Count];
        Array.Fill(depth, -1);
        int startIdx = asset.Nodes.FindIndex(n => string.Equals(n.Id, asset.StartNodeId, StringComparison.OrdinalIgnoreCase));
        if (startIdx < 0) startIdx = 0;
        depth[startIdx] = 0;
        var queue = new List<int> { startIdx };
        while (queue.Count > 0)
        {
            int i = queue[0]; queue.RemoveAt(0);
            var node = asset.Nodes[i];
            void Visit(string target)
            {
                int t = asset.Nodes.FindIndex(n => string.Equals(n.Id, target, StringComparison.OrdinalIgnoreCase));
                if (t >= 0 && depth[t] < 0) { depth[t] = depth[i] + 1; queue.Add(t); }
            }
            if (!string.IsNullOrWhiteSpace(node.NextNodeId)) Visit(node.NextNodeId);
            foreach (var c in node.Choices) Visit(c.NextNodeId);
        }

        // Column counts → row index per column.
        int maxDepth = depth.Max();
        var rowOf = new int[maxDepth + 1];
        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            int d = depth[i] < 0 ? maxDepth + 1 : depth[i]; // orphans past the end
            asset.Nodes[i].GraphX = 30f + d * 230f;
            asset.Nodes[i].GraphY = 30f + rowOf[Math.Min(d, maxDepth + 1) - 1 < 0 ? 0 : Math.Min(d, maxDepth + 1) - 1 >= rowOf.Length ? maxDepth : Math.Min(d, maxDepth + 1) - 1] * 96f;
            rowOf[Math.Min(d, maxDepth)]++;
        }
    }

    /// <summary>Smoothly pan the Graph View so a node centers in the canvas (null =
    /// center the whole graph's bounding box). Animated over ~0.25s with ease-out so
    /// the motion reads as a camera move, not a teleport. Zoom untouched.</summary>
    private void CenterOnNode(DialogueAsset asset, DialogueNode? node)
    {
        Vector2 target;
        if (node != null)
        {
            target = new Vector2(node.GraphX + GraphNodeW * 0.5f, node.GraphY + GraphNodeH * 0.5f);
        }
        else if (asset.Nodes.Count > 0)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var n in asset.Nodes)
            {
                min = new Vector2(MathF.Min(min.X, n.GraphX), MathF.Min(min.Y, n.GraphY));
                max = new Vector2(MathF.Max(max.X, n.GraphX + GraphNodeW), MathF.Max(max.Y, n.GraphY + GraphNodeH));
            }
            target = (min + max) * 0.5f;
        }
        else return;

        // The pan needed to place the target at the canvas center. The canvas rect is
        // not known here — recompute the same way RenderGraphView does (cursor pos at
        // call time sits right where the canvas starts next frame).
        var avail = ImGui.GetContentRegionAvail();
        var canvasCenter = new Vector2(MathF.Max(120f, avail.X * 0.5f), MathF.Max(110f, MathF.Min(420f, avail.Y - 40f) * 0.5f));
        Vector2 goalPan = canvasCenter - target * _graphZoom;
        _graphPanFrom = _graphPan;
        _graphPanTo = goalPan;
        _graphPanAnim = 0f;
    }

    private void RenderGraphView(DialogueAsset asset)
    {
        // Advance the double-click focus pan (ease-out cubic, ~0.25s).
        // Any manual pan input cancels it so the user always wins.
        if (_graphPanAnimating)
        {
            _graphPanAnim = MathF.Min(1f, _graphPanAnim + ImGui.GetIO().DeltaTime / 0.25f);
            float t = 1f - MathF.Pow(1f - _graphPanAnim, 3f);
            _graphPan = Vector2.Lerp(_graphPanFrom, _graphPanTo, t);
            if (_graphPanAnim >= 1f) _graphPanAnimating = false;
        }
        float canvasH = MathF.Min(420f, ImGui.GetContentRegionAvail().Y - 40f);
        var canvasMin = ImGui.GetCursorScreenPos();
        var canvasSize = new Vector2(ImGui.GetContentRegionAvail().X, MathF.Max(220f, canvasH));
        ImGui.InvisibleButton("##graph_canvas", canvasSize,
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle | ImGuiButtonFlags.MouseButtonRight);
        var dl = ImGui.GetWindowDrawList();
        var canvasMax = canvasMin + canvasSize;
        dl.PushClipRect(canvasMin, canvasMax, true);

        // ImGui draw-list colors are ABGR-packed — build them from RGBA components.
        static uint C(byte r, byte g, byte b, byte a = 255)
            => ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));

        // Background + dot grid.
        dl.AddRectFilled(canvasMin, canvasMax, C(22, 24, 32));
        float gridStep = 32f * _graphZoom;
        if (gridStep > 8f)
        {
            uint gridCol = C(44, 48, 62);
            float ox = _graphPan.X % gridStep, oy = _graphPan.Y % gridStep;
            for (float x = ox; x < canvasSize.X; x += gridStep)
                for (float y = oy; y < canvasSize.Y; y += gridStep)
                    dl.AddCircleFilled(new Vector2(canvasMin.X + x, canvasMin.Y + y), 1.2f, gridCol);
        }

        Vector2 ToScreen(Vector2 graphPos) => canvasMin + _graphPan + graphPos * _graphZoom;

        // ── Scaled typography — ALL text draws at the zoomed size inside clipped
        // boxes, so nothing ever spills outside a node at any zoom level. ──
        var font = ImGui.GetFont();
        float fs = MathF.Max(8f, ImGui.GetFontSize() * _graphZoom);
        float lineH = fs * 1.4f;
        float padX = 9f * _graphZoom;
        float nodeW = GraphNodeW * _graphZoom;
        float headerH = 20f * _graphZoom;
        float footerH = 17f * _graphZoom;
        float bodyPadY = 5f * _graphZoom;
        const int maxLines = 3;
        float bodyTextW = nodeW - padX * 2f;

        // Word-wrap at the SCALED size; ellipsize the last visible line.
        List<string> Wrap(string text)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (var word in text.Split(' '))
                {
                    string test = lines.Count == 0 ? word : lines[^1] + " " + word;
                    if (font.CalcTextSizeA(fs, float.MaxValue, 0f, test).X > bodyTextW && lines.Count > 0) lines.Add(word);
                    else if (lines.Count == 0) lines.Add(word);
                    else lines[^1] = test;
                }
            }
            if (lines.Count == 0) lines.Add("(no text)");
            if (lines.Count > maxLines)
            {
                lines.RemoveAt(maxLines);
                string last = lines[^1];
                while (last.Length > 1 && font.CalcTextSizeA(fs, float.MaxValue, 0f, last + "…").X > bodyTextW)
                    last = last[..^1];
                lines[^1] = last + "…";
            }
            return lines;
        }

        // Pass 1 — per-node wrapped body + dynamic height (edges anchor to these).
        var bodyLines = new List<string>[asset.Nodes.Count];
        var nodeH = new float[asset.Nodes.Count];
        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            var n = asset.Nodes[i];
            bodyLines[i] = n is null ? ["(no text)"] : Wrap(n.Text);
            nodeH[i] = headerH + bodyPadY * 2f + bodyLines[i].Count * lineH + footerH;
        }

        // ── Ports: bottom edge = outputs (one per choice, or center = Next when the
        // node has no choices), top-center = input ring. Edge anchors sit on them. ──
        Vector2 PortPos(int nodeIdx, int portIdx)
        {
            var nn = asset.Nodes[nodeIdx];
            int total = nn.Choices.Count > 0 ? nn.Choices.Count : 1;
            var basePos = ToScreen(new Vector2(nn.GraphX, nn.GraphY));
            float frac = (portIdx + 1) / (float)(total + 1);
            return basePos + new Vector2(nodeW * frac, nodeH[nodeIdx]);
        }

        // ── Edges (drawn first so nodes sit on top) ──
        uint nextCol = C(120, 170, 235);
        uint choiceCol = C(95, 195, 150);
        uint brokenCol = C(235, 92, 80);
        float edgeThick = Math.Clamp(1.8f * _graphZoom, 1f, 3f);

        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            var src = asset.Nodes[i];
            if (src is null) continue;

            void Edge(Vector2 fromAnchor, string targetId, string label, uint color)
            {
                if (string.IsNullOrWhiteSpace(targetId)) return;
                int t = asset.Nodes.FindIndex(n => string.Equals(n.Id, targetId, StringComparison.OrdinalIgnoreCase));
                if (t < 0)
                {
                    // Broken target: red stub so a typo'd route is visible.
                    dl.AddLine(fromAnchor, fromAnchor + new Vector2(26f * _graphZoom, -10f * _graphZoom), brokenCol, edgeThick);
                    dl.AddCircleFilled(fromAnchor + new Vector2(26f * _graphZoom, -10f * _graphZoom), 3f, brokenCol);
                    return;
                }
                var dstPos = ToScreen(new Vector2(asset.Nodes[t].GraphX, asset.Nodes[t].GraphY));
                var dstAnchor = dstPos + new Vector2(nodeW * 0.5f, 0f); // top-center input ring

                Vector2 c1, c2;
                if (t == i)
                {
                    // Self-loop: bulge out to the right of the node.
                    float loop = nodeW * 0.85f;
                    c1 = fromAnchor + new Vector2(loop, 14f * _graphZoom);
                    c2 = dstAnchor + new Vector2(loop, -14f * _graphZoom);
                }
                else
                {
                    float dy = MathF.Max(24f * _graphZoom, MathF.Abs(dstAnchor.Y - fromAnchor.Y) * 0.45f);
                    c1 = fromAnchor + new Vector2(0, dy);
                    c2 = dstAnchor - new Vector2(0, dy);
                }
                dl.AddBezierCubic(fromAnchor, c1, c2, dstAnchor, color, edgeThick);
                // Arrowhead at the target.
                dl.AddTriangleFilled(
                    dstAnchor + new Vector2(-4f * _graphZoom, -1f),
                    dstAnchor + new Vector2(4f * _graphZoom, -1f),
                    dstAnchor + new Vector2(0f, 6f * _graphZoom), color);

                if (!string.IsNullOrEmpty(label))
                {
                    // Cubic-bezier midpoint at t=0.5 (Bernstein: 1/8, 3/8, 3/8, 1/8).
                    var mid = 0.125f * fromAnchor + 0.375f * c1 + 0.375f * c2 + 0.125f * dstAnchor;
                    var tsz = font.CalcTextSizeA(fs, float.MaxValue, 0f, label);
                    var cmin = mid - tsz * 0.5f - new Vector2(4f * _graphZoom, 2f);
                    var cmax = mid + tsz * 0.5f + new Vector2(4f * _graphZoom, 2f);
                    dl.AddRectFilled(cmin, cmax, C(18, 20, 28, 215), 3f);
                    dl.AddText(font, fs, mid - tsz * 0.5f, color, label);
                }
            }

            // Next edge only when the node has no choices — with choices the runtime
            // follows the picks, so drawing a Next line would lie about the routing.
            if (src.Choices.Count == 0)
                Edge(PortPos(i, 0), src.NextNodeId, "", nextCol);
            for (int ci = 0; ci < src.Choices.Count; ci++)
                Edge(PortPos(i, ci), src.Choices[ci].NextNodeId, $"{ci + 1}", choiceCol);
        }

        // ── Nodes ──
        var mouse = ImGui.GetIO().MousePos;
        bool leftClicked = ImGui.IsMouseClicked(0);
        bool leftReleased = ImGui.IsMouseReleased(0);
        bool canvasHovered = ImGui.IsItemHovered();
        int hoveredNode = -1;
        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            var n = asset.Nodes[i];
            if (n is null) continue;
            var nmin = ToScreen(new Vector2(n.GraphX, n.GraphY));
            var nmax = nmin + new Vector2(nodeW, nodeH[i]);
            if (mouse.X >= nmin.X && mouse.X <= nmax.X && mouse.Y >= nmin.Y && mouse.Y <= nmax.Y) hoveredNode = i;

            bool selected = i == _selectedNode;
            bool isStart = string.Equals(n.Id, asset.StartNodeId, StringComparison.OrdinalIgnoreCase);
            uint accent = isStart ? C(95, 190, 120) : selected ? C(140, 190, 245) : C(74, 84, 110);
            uint headerBg = isStart ? C(36, 62, 48) : selected ? C(46, 60, 84) : C(43, 49, 66);
            uint bodyBg = C(33, 38, 51);
            uint borderCol = accent;

            // Shadow → body → header strip → border.
            dl.AddRectFilled(nmin + new Vector2(3, 3), nmax + new Vector2(3, 3), C(0, 0, 0, 90), 6f);
            dl.AddRectFilled(nmin, nmax, bodyBg, 6f);
            dl.AddRectFilled(nmin, new Vector2(nmax.X, nmin.Y + headerH), headerBg, 6f, ImDrawFlags.RoundCornersTop);
            dl.AddRect(nmin, nmax, borderCol, 6f, ImDrawFlags.None, selected || isStart ? 1.8f : 1.1f);
            // Accent strip on the left edge.
            dl.AddRectFilled(nmin + new Vector2(1, headerH), new Vector2(nmin.X + 3f * _graphZoom, nmax.Y - 1f), accent);

            // Per-node clip: text physically cannot escape the box.
            dl.PushClipRect(nmin, nmax, true);

            string head = (isStart ? "▶ " : "") + n.Id;
            dl.AddText(font, fs, nmin + new Vector2(padX + 4f * _graphZoom, (headerH - fs) * 0.5f),
                isStart ? C(156, 232, 180) : C(208, 218, 240), head);

            var lines = bodyLines[i];
            float ty = nmin.Y + headerH + bodyPadY;
            foreach (var line in lines)
            {
                dl.AddText(font, fs, new Vector2(nmin.X + padX, ty), C(168, 178, 198), line);
                ty += lineH;
            }

            string foot = (string.IsNullOrEmpty(n.SpeakerId) ? "narrator" : n.SpeakerId)
                + (n.Choices.Count > 0 ? $"  ·  {n.Choices.Count} choice{(n.Choices.Count > 1 ? "s" : "")}" : "");
            dl.AddText(font, MathF.Max(7f, fs - 1f),
                new Vector2(nmin.X + padX, nmax.Y - footerH + (footerH - fs) * 0.5f), C(126, 136, 160), foot);

            dl.PopClipRect();
        }

        // ── Port circles (on top of nodes, clickable drag handles) ──
        // Radius grows on hover — the affordance that it can be dragged.
        float portR = MathF.Max(3.5f, 4.5f * _graphZoom);
        int hoveredPortNode = -1, hoveredPortChoice = -1;
        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            var n = asset.Nodes[i];
            if (n is null) continue;
            int total = n.Choices.Count > 0 ? n.Choices.Count : 1;
            for (int p = 0; p < total; p++)
            {
                var pp = PortPos(i, p);
                bool portHovered = Vector2.Distance(mouse, pp) <= portR + 3f;
                if (portHovered) { hoveredPortNode = i; hoveredPortChoice = n.Choices.Count > 0 ? p : -1; }
                uint pc = portHovered || (_graphLinkSrc == i && _graphLinkChoice == (n.Choices.Count > 0 ? p : -1))
                    ? C(250, 220, 120) : C(150, 160, 190);
                dl.AddCircleFilled(pp, portR + (portHovered ? 1.5f : 0f), pc);
                dl.AddCircle(pp, portR, C(20, 22, 30), 0, 1.2f);
            }
        }

        // ── Connection drag: pull from a port, drop on another node ──
        if (_graphLinkSrc >= 0)
        {
            int total = asset.Nodes[_graphLinkSrc].Choices.Count;
            var from = PortPos(_graphLinkSrc, _graphLinkChoice < 0 ? 0 : _graphLinkChoice);
            // Ghost curve to the cursor + ring on whatever node is under it.
            float dy = MathF.Max(24f * _graphZoom, MathF.Abs(mouse.Y - from.Y) * 0.45f);
            dl.AddBezierCubic(from, from + new Vector2(0, dy), mouse - new Vector2(0, dy), mouse, C(250, 220, 120), edgeThick);
            if (hoveredNode >= 0 && hoveredNode != _graphLinkSrc)
            {
                var hmin = ToScreen(new Vector2(asset.Nodes[hoveredNode].GraphX, asset.Nodes[hoveredNode].GraphY));
                dl.AddRect(hmin - new Vector2(2, 2), hmin + new Vector2(nodeW + 2, nodeH[hoveredNode] + 2), C(250, 220, 120), 6f, ImDrawFlags.None, 2f);
            }
            if (leftReleased)
            {
                if (hoveredNode >= 0 && hoveredNode != _graphLinkSrc)
                {
                    string targetId = asset.Nodes[hoveredNode].Id;
                    var srcNode = asset.Nodes[_graphLinkSrc];
                    if (_graphLinkChoice >= 0 && _graphLinkChoice < srcNode.Choices.Count)
                    {
                        srcNode.Choices[_graphLinkChoice].NextNodeId = targetId;
                        Console.WriteLine($"[Dialogue Graph] choice '{_graphLinkChoice + 1}' of '{srcNode.Id}' → '{targetId}'");
                    }
                    else
                    {
                        srcNode.NextNodeId = targetId;
                        Console.WriteLine($"[Dialogue Graph] '{srcNode.Id}' next → '{targetId}'");
                    }
                }
                _graphLinkSrc = -1; _graphLinkChoice = -1;
            }
        }
        else if (canvasHovered)
        {
            // Start a connection drag ONLY on a port (not the node body — body = move).
            if (leftClicked && hoveredPortNode >= 0)
            {
                _graphLinkSrc = hoveredPortNode;
                _graphLinkChoice = hoveredPortChoice;
            }
            else if (leftClicked && hoveredNode >= 0)
            {
                _selectedNode = hoveredNode;
                _graphDragNode = hoveredNode;
                var nodePos = ToScreen(new Vector2(asset.Nodes[hoveredNode].GraphX, asset.Nodes[hoveredNode].GraphY));
                _graphDragOffset = (nodePos - mouse) / _graphZoom;
            }
        }

        // Node drag (LMB on a node body moves it) — while not dragging a connection.
        if (_graphLinkSrc < 0 && _graphDragNode >= 0 && _graphDragNode < asset.Nodes.Count)
        {
            if (ImGui.IsMouseDragging(0))
            {
                var gn = asset.Nodes[_graphDragNode];
                var screen = mouse + _graphDragOffset * _graphZoom - canvasMin - _graphPan;
                gn.GraphX = screen.X / _graphZoom;
                gn.GraphY = screen.Y / _graphZoom;
            }
            else if (leftReleased)
                _graphDragNode = -1;
        }

        // ── Interactions ──
        if (canvasHovered)
        {
            // Manual pan cancels the double-click focus animation — the user wins.
            if (_graphPanAnimating && (ImGui.IsMouseClicked(0) || ImGui.IsMouseClicked(ImGuiMouseButton.Middle)))
                _graphPanAnimating = false;
            // Pan: LMB on empty space (only when nothing else is in progress) or MMB.
            if (ImGui.IsMouseClicked(0) && hoveredNode < 0 && hoveredPortNode < 0 && _graphLinkSrc < 0)
            {
                _graphPanning = true;
                _graphPanStart = _graphPan;
                _graphMouseAtPanStart = mouse;
            }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
            {
                _graphPanning = true;
                _graphPanStart = _graphPan;
                _graphMouseAtPanStart = mouse;
            }
            // Zoom: wheel — keep centered on the mouse.
            float wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f)
            {
                float oldZoom = _graphZoom;
                _graphZoom = Math.Clamp(_graphZoom * (wheel > 0 ? 1.12f : 1 / 1.12f), 0.35f, 2.2f);
                // Keep the point under the cursor stable.
                var before = (mouse - canvasMin - _graphPan) / oldZoom;
                _graphPan = mouse - canvasMin - before * _graphZoom;
            }
        }
        if (_graphPanning && (ImGui.IsMouseDragging(0) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle)))
        {
            _graphPan = _graphPanStart + (mouse - _graphMouseAtPanStart);
        }
        else if (_graphPanning)
            _graphPanning = false;

        // Double-click node → smoothly pan the canvas so that node centers in view.
        // (Double-click on empty space → re-center on the whole graph.)
        if (canvasHovered && ImGui.IsMouseDoubleClicked(0))
        {
            if (hoveredNode >= 0)
                CenterOnNode(asset, asset.Nodes[hoveredNode]);
            else
                CenterOnNode(asset, null);
        }

        // RMB on node → context menu (Set Start / Duplicate / Delete).
        // Key freezes the node index at click time — hoveredNode changes as the mouse
        // moves and a hover-keyed popup would follow the cursor.
        if (canvasHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && hoveredNode >= 0)
        {
            _selectedNode = hoveredNode;
            _graphCtxNode = hoveredNode;
            ImGui.OpenPopup("graph_node_ctx");
        }
        if (ImGui.BeginPopup("graph_node_ctx"))
        {
            var n = _graphCtxNode >= 0 && _graphCtxNode < asset.Nodes.Count ? asset.Nodes[_graphCtxNode] : null;
            if (n != null)
            {
                if (ImGui.MenuItem("Set Start")) asset.StartNodeId = n.Id;
                if (ImGui.MenuItem("Duplicate"))
                { asset.Nodes.Insert(_graphCtxNode + 1, CloneWithNewId(n, asset)); _selectedNode = _graphCtxNode + 1; }
                if (ImGui.MenuItem("Delete") && asset.Nodes.Count > 1)
                {
                    asset.Nodes.RemoveAt(_graphCtxNode);
                    _selectedNode = Math.Min(_selectedNode, asset.Nodes.Count - 1);
                }
            }
            ImGui.EndPopup();
        }

        dl.PopClipRect();
    }

    private static string UniqueNodeId(DialogueAsset asset, string prefix)
    {
        int n = asset.Nodes.Count + 1;
        string id = $"{prefix}{n}";
        while (asset.Nodes.Any(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            id = $"{prefix}{++n}";
        return id;
    }

    private static DialogueNode CloneWithNewId(DialogueNode src, DialogueAsset asset)
    {
        var copy = src.Clone();
        copy.Id = UniqueNodeId(asset, "node");
        return copy;
    }

    private unsafe void RenderNodeEditor(DialogueAsset asset, DialogueNode node)
    {
        ImGui.Separator();
        ImGui.Text("Node");
        ImGui.SetNextItemWidth(180f);
        string id = node.Id;
        if (ImGui.InputText("##nodeid", ref id, 64) && !string.IsNullOrWhiteSpace(id))
        {
            string oldId = node.Id;
            node.Id = id.Trim();
            if (asset.StartNodeId == oldId) asset.StartNodeId = node.Id;
        }
        ImGui.SameLine();
        if (ImGui.Button("Set Start")) asset.StartNodeId = node.Id;

        // Speaker picker
        var speakerIds = DialogueLibrary.GetSpeakerIds();
        int spIdx = speakerIds.IndexOf(node.SpeakerId);
        string spLabel = spIdx >= 0 ? speakerIds[spIdx] : (string.IsNullOrEmpty(node.SpeakerId) ? "(narrator)" : node.SpeakerId);
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("Speaker", spLabel))
        {
            if (ImGui.Selectable("(narrator)", string.IsNullOrEmpty(node.SpeakerId)))
                node.SpeakerId = "";
            for (int i = 0; i < speakerIds.Count; i++)
            {
                bool sel = i == spIdx;
                if (ImGui.Selectable(speakerIds[i], sel)) node.SpeakerId = speakerIds[i];
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Emotion picker
        int emoIdx = Array.IndexOf(DialogueEmotions.All, node.Emotion);
        if (emoIdx < 0) emoIdx = 0;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("Emotion", DialogueEmotions.All[emoIdx]))
        {
            for (int i = 0; i < DialogueEmotions.All.Length; i++)
            {
                bool sel = i == emoIdx;
                if (ImGui.Selectable(DialogueEmotions.All[i], sel)) node.Emotion = DialogueEmotions.All[i];
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Portrait path — type it OR drag an image straight from the Asset Browser.
        // Drop target MUST sit directly on the input item (BeginDragDropTarget applies
        // to the last submitted item — an X button in between steals the drop).
        ImGui.Text("Portrait (png, optional)");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-60f);
        string portrait = node.PortraitPath;
        if (ImGui.InputText("##dlgportrait", ref portrait, 256))
            node.PortraitPath = portrait.Trim();
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
            if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
            {
                node.PortraitPath = Helpers.PathHelpers.MakeRelative(AssetBrowserPanel._dragImagePath);
                AssetBrowserPanel._dragImagePath = null;
            }
            ImGui.EndDragDropTarget();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("X##dlgportraitx") && !string.IsNullOrEmpty(node.PortraitPath))
            node.PortraitPath = "";
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Clear portrait.\nDrag an image from the Asset Browser onto the field.\nEmotion variants try <name>_Happy.png etc. first.");

        // Text (localized display)
        string text = node.Text;
        if (ImGui.InputTextMultiline("Text", ref text, 600, new Vector2(-1, 64)))
            node.Text = text;
        string preview = DialogueLibrary.Localize(node.Text);
        if (preview != node.Text)
            ImGui.TextDisabled($"[{DialogueLibrary.CurrentLanguage}] {preview}");

        // Next node — dropdown of node ids in this asset (typo-proof routing).
        ImGui.Text("Next Node (end = dialog selesai)");
        if (NodeIdCombo(asset, node.NextNodeId, "##nextnode") is { } nn)
            node.NextNodeId = nn;

        // Auto advance
        bool auto = node.AutoAdvance;
        if (ImGui.Checkbox("Auto Advance", ref auto)) node.AutoAdvance = auto;
        if (auto)
        {
            float delay = node.AutoAdvanceDelay;
            if (ImGui.DragFloat("Auto Delay (s)", ref delay, 0.1f, 0.1f, 30f))
                node.AutoAdvanceDelay = MathF.Max(0.1f, delay);
        }

        // Choices
        ImGui.Separator();
        ImGui.TextDisabled($"Choices ({node.Choices.Count})");
        for (int i = 0; i < node.Choices.Count; i++)
        {
            var c = node.Choices[i];
            ImGui.PushID($"choice{i}");
            string cText = c.Text;
            ImGui.SetNextItemWidth(160f);
            if (ImGui.InputText("##ctext", ref cText, 128)) c.Text = cText;
            ImGui.SameLine();
            if (NodeIdCombo(asset, c.NextNodeId, "##cnext") is { } cn)
                c.NextNodeId = cn;
            ImGui.SameLine();
            string cond = string.Join(";", c.Conditions);
            ImGui.SetNextItemWidth(140f);
            if (ImGui.InputText("##ccond", ref cond, 256))
                c.Conditions = cond.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Conditions separated by ';' — e.g. level:5;flag:met_elder;item:potion;gold:100");
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
            if (ImGui.SmallButton("x"))
            { node.Choices.RemoveAt(i); ImGui.PopStyleColor(); ImGui.PopID(); continue; }
            ImGui.PopStyleColor();
            // Choice actions (compact)
            if (c.Actions.Count > 0)
                ImGui.TextDisabled($"  → {c.Actions.Count} action(s): {string.Join(", ", c.Actions.Select(a => a.Type))}");
            if (ImGui.Button("+ Action"))
                c.Actions.Add(new TilemapTriggerAction { Type = TriggerActionTypes.GiveItem, Param = "item_1", Param2 = "1" });
            ImGui.SameLine();
            ImGui.SetNextItemWidth(130f);
            int aIdx = 0;
            string[] allActions = TriggerActionTypes.All;
            if (ImGui.BeginCombo("##ctype", c.Actions.Count > 0 ? "edit last…" : "type"))
            {
                for (int i2 = 0; i2 < allActions.Length; i2++)
                {
                    if (ImGui.Selectable(allActions[i2]))
                    {
                        if (c.Actions.Count > 0) c.Actions[^1].Type = allActions[i2];
                        else c.Actions.Add(new TilemapTriggerAction { Type = allActions[i2] });
                    }
                }
                ImGui.EndCombo();
            }
            if (c.Actions.Count > 0)
            {
                ImGui.SameLine();
                string p = c.Actions[^1].Param;
                ImGui.SetNextItemWidth(100f);
                if (ImGui.InputText("##cparam", ref p, 128)) c.Actions[^1].Param = p;
                ImGui.SameLine();
                string p2 = c.Actions[^1].Param2;
                ImGui.SetNextItemWidth(70f);
                if (ImGui.InputText("##cparam2", ref p2, 64)) c.Actions[^1].Param2 = p2;
            }
            ImGui.PopID();
        }
        if (ImGui.Button("+ Choice"))
            node.Choices.Add(new DialogueChoice { Text = $"Choice {node.Choices.Count + 1}" });

        // Node start/end actions
        ImGui.Separator();
        RenderActionList("On Start Actions", node.OnStartActions);
        RenderActionList("On End Actions", node.OnEndActions);
    }

    private static void RenderActionList(string label, List<TilemapTriggerAction> actions)
    {
        ImGui.PushID(label.Replace(" ", ""));
        if (ImGui.TreeNode(label))
        {
            for (int i = 0; i < actions.Count; i++)
            {
                ImGui.PushID($"a{i}");
                var a = actions[i];
                int tIdx = Array.IndexOf(TriggerActionTypes.All, a.Type);
                if (tIdx < 0) tIdx = 0;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.BeginCombo("##t", TriggerActionTypes.All[tIdx]))
                {
                    for (int t = 0; t < TriggerActionTypes.All.Length; t++)
                        if (ImGui.Selectable(TriggerActionTypes.All[t], t == tIdx)) a.Type = TriggerActionTypes.All[t];
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                string p = a.Param;
                ImGui.SetNextItemWidth(110f);
                if (ImGui.InputText("##p", ref p, 128)) a.Param = p;
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
                if (ImGui.SmallButton("x")) { actions.RemoveAt(i); ImGui.PopStyleColor(); ImGui.PopID(); continue; }
                ImGui.PopStyleColor();
                ImGui.PopID();
            }
            if (ImGui.Button("+ Action"))
                actions.Add(new TilemapTriggerAction { Type = TriggerActionTypes.PlaySound, Param = "click.wav" });
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    // ── Speakers ───────────────────────────────────

    private unsafe void RenderSpeakersSection()
    {
        if (!ImGui.CollapsingHeader($"Speakers ({DialogueLibrary.Speakers.Count})")) return;
        for (int i = 0; i < DialogueLibrary.Speakers.Count; i++)
        {
            var s = DialogueLibrary.Speakers[i];
            ImGui.PushID($"sp{i}");
            bool open = ImGui.TreeNode($"{s.Name} ({s.Id})");
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
            if (ImGui.SmallButton("x")) { DialogueLibrary.RemoveSpeaker(s); ImGui.PopStyleColor(); ImGui.PopID(); if (open) ImGui.TreePop(); continue; }
            ImGui.PopStyleColor();
            if (open)
            {
                string id = s.Id;
                if (ImGui.InputText("Id", ref id, 64)) s.Id = id.Trim();
                string name = s.Name;
                if (ImGui.InputText("Name", ref name, 64)) s.Name = name;
                string portrait = s.PortraitPath;
                ImGui.SetNextItemWidth(-60f);
                if (ImGui.InputText("##spkportrait", ref portrait, 256)) s.PortraitPath = portrait.Trim();
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    {
                        s.PortraitPath = Helpers.PathHelpers.MakeRelative(AssetBrowserPanel._dragImagePath);
                        AssetBrowserPanel._dragImagePath = null;
                    }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("X##spkportraitx") && !string.IsNullOrEmpty(s.PortraitPath))
                    s.PortraitPath = "";
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Clear. Drag an image from the Asset Browser onto the field.");
                Vector3 col = new(s.ColorR, s.ColorG, s.ColorB);
                if (ImGui.ColorEdit3("Name Color", ref col))
                { s.ColorR = col.X; s.ColorG = col.Y; s.ColorB = col.Z; }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        if (ImGui.Button("+ Speaker"))
            DialogueLibrary.AddSpeaker(new SpeakerData { Id = $"speaker_{DialogueLibrary.Speakers.Count + 1}", Name = $"Speaker {DialogueLibrary.Speakers.Count + 1}" });
    }

    // ── Themes ─────────────────────────────────────

    private void RenderThemesSection()
    {
        if (!ImGui.CollapsingHeader($"Themes ({DialogueLibrary.Themes.Count})")) return;
        for (int i = 0; i < DialogueLibrary.Themes.Count; i++)
        {
            var t = DialogueLibrary.Themes[i];
            ImGui.PushID($"th{i}");
            bool open = ImGui.TreeNode($"{t.Name}{(string.IsNullOrEmpty(t.Parent) ? "" : $" (← {t.Parent})")}");
            ImGui.SameLine();
            if (t.Name == "Default") ImGui.TextDisabled("(built-in)");
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
                if (ImGui.SmallButton("x")) { DialogueLibrary.RemoveTheme(t); ImGui.PopStyleColor(); ImGui.PopID(); if (open) ImGui.TreePop(); continue; }
                ImGui.PopStyleColor();
            }
            if (open)
            {
                string name = t.Name;
                if (ImGui.InputText("Name", ref name, 64) && !string.IsNullOrWhiteSpace(name)) t.Name = name.Trim();
                var parents = DialogueLibrary.GetThemeNames().Where(n => n != t.Name).Prepend("").ToList();
                int pIdx = parents.IndexOf(t.Parent);
                if (pIdx < 0) pIdx = 0;
                if (ImGui.BeginCombo("Parent", parents[pIdx]))
                {
                    for (int p = 0; p < parents.Count; p++)
                        if (ImGui.Selectable(string.IsNullOrEmpty(parents[p]) ? "(none)" : parents[p], p == pIdx))
                            t.Parent = parents[p];
                    ImGui.EndCombo();
                }
                string bg = t.BgImagePath;
                if (ImGui.InputText("Window BG Image", ref bg, 256)) t.BgImagePath = bg.Trim();

                Vector3 win = t.WindowColor;
                if (ImGui.ColorEdit3("Window", ref win))
                { t.WindowColorR = win.X; t.WindowColorG = win.Y; t.WindowColorB = win.Z; }
                Vector3 border = t.BorderColor;
                if (ImGui.ColorEdit3("Border", ref border))
                { t.BorderColorR = border.X; t.BorderColorG = border.Y; t.BorderColorB = border.Z; }
                Vector3 nameC = t.NameColor;
                if (ImGui.ColorEdit3("Name", ref nameC))
                { t.NameColorR = nameC.X; t.NameColorG = nameC.Y; t.NameColorB = nameC.Z; }
                Vector3 textC = t.TextColor;
                if (ImGui.ColorEdit3("Text", ref textC))
                { t.TextColorR = textC.X; t.TextColorG = textC.Y; t.TextColorB = textC.Z; }
                Vector3 choice = t.ChoiceColor;
                if (ImGui.ColorEdit3("Choice", ref choice))
                { t.ChoiceColorR = choice.X; t.ChoiceColorG = choice.Y; t.ChoiceColorB = choice.Z; }
                Vector3 choiceH = t.ChoiceHoverColor;
                if (ImGui.ColorEdit3("Choice Hover", ref choiceH))
                { t.ChoiceHoverColorR = choiceH.X; t.ChoiceHoverColorG = choiceH.Y; t.ChoiceHoverColorB = choiceH.Z; }
                Vector3 bub = t.BubbleColor;
                if (ImGui.ColorEdit3("Bubble BG", ref bub))
                { t.BubbleColorR = bub.X; t.BubbleColorG = bub.Y; t.BubbleColorB = bub.Z; }
                Vector3 bubB = t.BubbleBorderColor;
                if (ImGui.ColorEdit3("Bubble Border", ref bubB))
                { t.BubbleBorderColorR = bubB.X; t.BubbleBorderColorG = bubB.Y; t.BubbleBorderColorB = bubB.Z; }

                string font = t.FontPath;
                if (ImGui.InputText("Font", ref font, 256)) t.FontPath = font;
                float fs = t.WindowFontSize;
                if (ImGui.DragFloat("Window Font", ref fs, 0.5f, 8f, 48f)) t.WindowFontSize = fs;
                float ns = t.NameFontSize;
                if (ImGui.DragFloat("Name Font", ref ns, 0.5f, 8f, 48f)) t.NameFontSize = ns;
                float bs = t.BubbleFontSize;
                if (ImGui.DragFloat("Bubble Font", ref bs, 0.5f, 8f, 48f)) t.BubbleFontSize = bs;
                float tw = t.TypewriterCharsPerSecond;
                if (ImGui.DragFloat("Typewriter (chars/s, 0 = off)", ref tw, 1f, 0f, 200f)) t.TypewriterCharsPerSecond = tw;
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        if (ImGui.Button("+ Theme"))
            DialogueLibrary.AddTheme(new DialogueThemeData { Name = $"Theme{DialogueLibrary.Themes.Count + 1}" });
    }

    // ── Localization ───────────────────────────────

    private void RenderLocalizationSection()
    {
        if (!ImGui.CollapsingHeader($"Localization ({DialogueLibrary.CurrentLanguage})")) return;

        int langIdx = Array.IndexOf(DialogueLibrary.Languages, DialogueLibrary.CurrentLanguage);
        if (langIdx < 0) langIdx = 0;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo("Active Language", DialogueLibrary.Languages[langIdx]))
        {
            for (int i = 0; i < DialogueLibrary.Languages.Length; i++)
            {
                bool sel = i == langIdx;
                if (ImGui.Selectable(DialogueLibrary.Languages[i], sel))
                {
                    DialogueLibrary.CurrentLanguage = DialogueLibrary.Languages[i];
                    _editLanguage = i;
                }
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        _editLanguage = langIdx;

        ImGui.TextDisabled("Overrides for the selected language (original text → translation):");
        string lang = DialogueLibrary.Languages[_editLanguage];
        var table = DialogueLibrary.Localizations.GetValueOrDefault(lang) ?? [];

        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("Original##lockey", ref _locKey, 400);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("Translation##locval", ref _locValue, 400);
        ImGui.SameLine();
        if (ImGui.Button("Set") && !string.IsNullOrWhiteSpace(_locKey))
        {
            if (!DialogueLibrary.Localizations.TryGetValue(lang, out var tbl))
                DialogueLibrary.Localizations[lang] = tbl = new(StringComparer.OrdinalIgnoreCase);
            tbl[_locKey] = _locValue;
        }

        if (table.Count > 0)
        {
            ImGui.TextDisabled($"{table.Count} translation(s)");
            foreach (var kv in table.Take(20))
                ImGui.BulletText($"{kv.Key} → {kv.Value}");
            if (table.Count > 20) ImGui.TextDisabled("…");
        }
    }

    /// <summary>Dropdown of node ids in the asset for routing fields (Next Node / choice
    /// targets). Items: "(end)" = empty id → dialog selesai, lalu setiap node id dengan
    /// preview teks singkat. Menampilkan id tersimpan apa adanya (merah + tanda ⚠) bila    /// tidak ada di asset — supaya salah ketik lama tidak hilang diam-diam.
    /// Returns the newly selected id, or null when nothing was picked this frame.</summary>
    private static string? NodeIdCombo(DialogueAsset asset, string currentId, string id)
    {
        string? picked = null;
        bool missing = !string.IsNullOrEmpty(currentId) && asset.GetNode(currentId) == null;
        string shown = string.IsNullOrEmpty(currentId) ? "(end)"
            : missing ? $"⚠ {currentId} (tidak ada)" : currentId;

        ImGui.SetNextItemWidth(150f);
        if (ImGui.BeginCombo(id, shown))
        {
            // "(end)" first — an empty NextNodeId ends the conversation.
            if (ImGui.Selectable("(end)", string.IsNullOrEmpty(currentId)))
                picked = "";
            foreach (var n in asset.Nodes)
            {
                bool sel = string.Equals(n.Id, currentId, StringComparison.Ordinal);
                // Node id + a trimmed text excerpt so designers see WHAT node it is.
                string excerpt = n.Text.Length > 28 ? n.Text[..28] + "…" : n.Text;
                if (ImGui.Selectable($"{n.Id}   — {excerpt}", sel))
                    picked = n.Id;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        if (missing)
            ImGui.SetItemTooltip($"Node '{currentId}' tidak ada di asset ini — pilih dari daftar.");
        return picked;
    }
}
