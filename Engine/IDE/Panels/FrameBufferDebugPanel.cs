using DarkEngine3D_gl_csharp.Engine.Libs;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels
{
    /// <summary>
    /// FrameBuffer Debug panel — answers "is the framebuffer safe?" visually:
    /// live thumbnails + completeness validation of every off-screen render target
    /// used by the two render paths:
    /// <list type="bullet">
    /// <item>Editor Shared FBO (<see cref="Scene.SceneManager.SharedColorTex"/>) — edit mode.</item>
    /// <item>GameScene <see cref="Visual.PostProcessing.PostProcessStack"/> scene FBO — play/preview.</item>
    /// <item><see cref="Visual.PostProcessing.DepthOfFieldComposite"/> scratch target.</item>
    /// </list>
    /// Also shows WHICH texture the viewport panel is actually sampling right now —
    /// the #1 question when a post effect "does nothing" (effect ran on a texture
    /// the viewport never displays). Focus circles from the Post FX panel are drawn
    /// over the thumbnails so radius/feather can be eyeballed against the image.
    /// </summary>
    public unsafe class FrameBufferDebugPanel
    {
        private bool _visible = false;
        private readonly IDEBridge _bridge;
        private int _thumbW = 240;
        private readonly List<string> _lastValidation = new();

        private sealed record TargetInfo(string Label, uint Tex, uint Fbo, int W, int H);

        private static readonly (uint Code, string Name)[] FboStatusNames =
        {
            (0x8CD5, "COMPLETE"),
            (0x8CD6, "UNDEFINED"),
            (0x8CD7, "INCOMPLETE_READ_BUFFER"),
            (0x8CD8, "INCOMPLETE_WRITE_BUFFER"),
            (0x8CD9, "INCOMPLETE_MULTISAMPLE"),
            (0x8CDA, "INCOMPLETE_LAYER_TARGETS"),
        };

        public FrameBufferDebugPanel(IDEBridge bridge) { _bridge = bridge; }

        public void ShowInMenu() => ImGui.MenuItem("FrameBuffer Debug", null, ref _visible);

        public void Render()
        {
            if (!_visible) return;
            ImGui.Begin("FrameBuffer Debug", ref _visible);

            // ── Build the list of live targets ──
            var sm = _bridge.SceneManager;
            var stack = Visual.PostProcessing.PostProcessStack.Active;
            var targets = new List<TargetInfo>();

            if (sm != null && sm.SharedColorTex != 0)
                targets.Add(new TargetInfo("Editor Shared Color (edit mode)",
                    sm.SharedColorTex, sm.SharedFBO, Glfw.WindowWidth, Glfw.WindowHeight));

            if (stack != null && stack.SceneColorTex != 0)
                targets.Add(new TargetInfo("GameScene SceneColorTex (play/preview)",
                    stack.SceneColorTex, stack.SceneFBO, stack.Width, stack.Height));

            uint scratch = Visual.PostProcessing.DepthOfFieldComposite.ScratchTex;
            var (sw, sh) = Visual.PostProcessing.DepthOfFieldComposite.ScratchSize;
            if (scratch != 0 && sw > 0)
                targets.Add(new TargetInfo("DoF Scratch (composite result)", scratch, 0, sw, sh));

            // Sprite-shape focus mask (white = sharp silhouette) — only exists once the
            // sprite-shape mode rendered at least once.
            uint mask = Visual.PostProcessing.DepthOfFieldComposite.MaskTex;
            var (mw, mh) = Visual.PostProcessing.DepthOfFieldComposite.MaskSize;
            if (mask != 0 && mw > 0)
                targets.Add(new TargetInfo("DoF Sprite Mask (white = sharp)", mask, 0, mw, mh));

            // ── Reactive bloom + auto-exposure intermediates (Post FX chain) — one
            // thumbnail per FX stage so a broken/empty stage is instantly visible. ──
            var pfx = Visual.PostProcessing.PostFxProcessor.Shared;
            if (pfx.IsAllocated)
            {
                var (ow, oh) = pfx.OutputSize;
                if (ow > 0)
                {
                    targets.Add(new TargetInfo("PostFX Composite (bloom+tonemap+gamma)",
                        pfx.CompositeTex, 0, ow, oh));
                    targets.Add(new TargetInfo("PostFX Output (blitted to display)",
                        pfx.OutputTex, 0, ow, oh));
                }

                var (lw, lh) = pfx.LumaSize;
                if (lw > 0)
                    targets.Add(new TargetInfo("PostFX Luma (auto-exposure input)",
                        pfx.LumaTex, 0, lw, lh));

                for (int i = 0; i < pfx.DebugMipCount; i++)
                {
                    var (bw, bh) = pfx.GetMipSize(i);
                    if (bw <= 0) continue;
                    targets.Add(new TargetInfo($"PostFX Bloom Mip {i} ({bw}x{bh})",
                        pfx.GetMipTex(i), 0, bw, bh));
                }
            }

            // ── What is the viewport ACTUALLY sampling this frame? ──
            uint sampled = _bridge.SceneTextureID;
            string sampledName =
                sampled == 0 ? "— nothing yet (scene didn't render) —"
                : sm != null && sampled == sm.SharedColorTex ? "Editor Shared Color"
                : stack != null && sampled == stack.SceneColorTex ? "GameScene SceneColorTex"
                : $"UNKNOWN texture 0x{sampled:X}!";
            ImGui.Text("Viewport samples: ");
            ImGui.SameLine();
            bool known = sampled != 0 && (sampledName != $"UNKNOWN texture 0x{sampled:X}!");
            ImGui.TextColored(known ? new Vector4(0.4f, 0.9f, 0.5f, 1f) : new Vector4(0.95f, 0.75f, 0.3f, 1f),
                $"0x{sampled:X} → {sampledName}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("If DoF runs on a texture that is NOT this one, the viewport shows an unblurred image.\nDepthOfFieldComposite composites into BOTH shared + gamescene paths, so any name above is fine.");

            // ── DoF liveness counters ──
            ImGui.Text("DoF composite: ");
            ImGui.SameLine();
            if (Visual.PostProcessing.DepthOfFieldComposite.RanLastCall)
                ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), $"RUNNING — {Visual.PostProcessing.DepthOfFieldComposite.RunCount} composites so far (counter must tick up every frame)");
            else
                ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.3f, 1f), $"idle — {Visual.PostProcessing.DepthOfFieldComposite.RunCount} total (0 or stalled = effect not executing, see console [DepthOfField])");

            // ── Post FX (reactive bloom + auto exposure) liveness ──
            ImGui.Text("Post FX chain: ");
            ImGui.SameLine();
            if (!Config.PostFxSettings.Enabled)
                ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.3f, 1f), "disabled (master switch off)");
            else if (Visual.PostProcessing.PostFxProcessor.Shared.IsAllocated)
                ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f),
                    $"RUNNING — exposure {Config.PostFxSettings.CurrentAutoExposure:F2} (auto {(Config.PostFxSettings.AutoExposure ? "ON" : "off")}, mips {(int)Config.PostFxSettings.BloomMips})");
            else
                ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.3f, 1f), "waiting for first frame…");

            ImGui.Separator();
            ImGui.SliderInt("Thumbnail width", ref _thumbW, 80, 480);

            ImGui.BeginChild("##fbtargets", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() - 8), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

            foreach (var t in targets)
                DrawTarget(t);

            if (targets.Count == 0)
                ImGui.TextDisabled("No off-screen targets exist yet.\nEnter play/preview mode or open a scene to allocate them.");

            ImGui.EndChild();

            // ── Validation ──
            if (ImGui.Button("Validate All FBOs"))
            {
                _lastValidation.Clear();
                foreach (var t in targets)
                {
                    if (t.Fbo == 0) continue;
                    string status = CheckFboCompleteness(t.Fbo);
                    _lastValidation.Add($"FBO 0x{t.Fbo:X} ({t.Label}): {status}");
                    Console.WriteLine($"[FBDebug] FBO 0x{t.Fbo:X} '{t.Label}' → {status}");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Binds every reachable FBO and runs glCheckFramebufferStatus.\nResults also go to the console as [FBDebug] lines.");

            if (_lastValidation.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"{_lastValidation.Count} FBOs checked — see below/console");
                foreach (var line in _lastValidation)
                {
                    bool ok = line.Contains("COMPLETE");
                    ImGui.TextColored(ok ? new Vector4(0.35f, 0.85f, 0.45f, 1f) : new Vector4(0.95f, 0.35f, 0.35f, 1f), line);
                }
            }

            ImGui.End();
        }

        private void DrawTarget(TargetInfo t)
        {
            ImGui.PushID(t.Label);
            ImGui.TextUnformatted(t.Label);
            ImGui.SameLine();
            ImGui.TextDisabled($"tex 0x{t.Tex:X}  fbo 0x{t.Fbo:X}  {t.W}x{t.H}");
            if (t.Fbo != 0)
            {
                ImGui.SameLine();
                string status = CheckFboCompleteness(t.Fbo);
                bool ok = status == "COMPLETE";
                ImGui.TextColored(ok ? new Vector4(0.35f, 0.85f, 0.45f, 1f) : new Vector4(0.95f, 0.35f, 0.35f, 1f),
                    ok ? "FBO ✓" : $"FBO ✗ {status}");
            }

            // Thumbnail (V-flipped: GL bottom-left origin → ImGui top-left).
            float aspect = t.H > 0 ? (float)t.W / t.H : 1f;
            Vector2 size = new(_thumbW, Math.Max(1f, _thumbW / aspect));
            ImGui.Image((nint)t.Tex, size, new Vector2(0f, 1f), new Vector2(1f, 0f));

            // Overlay the Post FX focus circles on the target the DoF composites into.
            if (Config.PostFxSettings.DofEnabled && t.Tex == _bridge.SceneTextureID)
            {
                var min = ImGui.GetItemRectMin();
                var sz = ImGui.GetItemRectSize();
                var dl = ImGui.GetWindowDrawList();
                Vector2 center = new(min.X + Config.PostFxSettings.DofFocusX * sz.X,
                                     min.Y + (1f - Config.PostFxSettings.DofFocusY) * sz.Y);
                float rIn = MathF.Max(2f, Config.PostFxSettings.DofRadius * sz.Y);
                float rOut = MathF.Max(2f, (Config.PostFxSettings.DofRadius + Config.PostFxSettings.DofFeather) * sz.Y);
                dl.AddCircle(center, rIn, 0x9000FF00, 48, 1.5f);          // green = sharp area
                dl.AddCircle(center, rOut, 0x90FF4444, 48, 1.5f);         // red = full blur beyond
            }
            ImGui.PopID();
            ImGui.Spacing();
        }

        /// <summary>Bind, query glCheckFramebufferStatus, restore the previous binding.</summary>
        private static string CheckFboCompleteness(uint fbo)
        {
            int prev = 0;
            GL.GetIntegerv(Const.GL_FRAMEBUFFER_BINDING, &prev);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);
            int status = GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, (uint)prev);

            foreach (var (code, name) in FboStatusNames)
                if ((uint)status == code) return name;
            return $"0x{status:X}";
        }
    }
}
