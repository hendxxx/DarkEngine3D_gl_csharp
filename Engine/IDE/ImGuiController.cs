using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.IDE;

/// <summary>
/// Manages Dear ImGui context, OpenGL backend, and GLFW input bridging.
/// Uses the engine's GL class (function pointers loaded via wglGetProcAddress).
/// </summary>
public unsafe class ImGuiController : IDisposable
{
    private nint _window;
    private uint _fontTexture;
    private uint _shader;
    private uint _vertShader, _fragShader;
    private uint _vao, _vbo, _ebo;
    private int _projMtxLoc, _textureLoc;

    private int _vboCapacity = 4096;
    private int _eboCapacity = 8192;

    private bool _hasVtxOffset = false;

    private readonly Dictionary<int, ImGuiKey> _glfwToImGuiKey = [];

    // ── Character input for ImGui text/edit widgets (DragFloat, InputText, etc.) ──
    // GLFW's glfwSetCharCallback delivers typed characters that ImGui needs for text input.
    private static readonly List<uint> _charQueue = [];

    /// <summary>GLFW char callback — called for each typed character.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static void OnChar(IntPtr window, uint codepoint)
    {
        lock (_charQueue)
            _charQueue.Add(codepoint);
    }

    public ImGuiController(nint window)
    {
        _window = window;

        Console.WriteLine("[ImGui] Step 1: CreateContext...");
        ImGui.CreateContext();
        Console.WriteLine("[ImGui] Step 1 OK");

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        _hasVtxOffset = GL.DrawElementsBaseVertexPtr != IntPtr.Zero;
        if (_hasVtxOffset)
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        io.ConfigWindowsMoveFromTitleBarOnly = true;

        Console.WriteLine("[ImGui] Step 2: BuildKeyMap...");
        BuildKeyMap();
        Console.WriteLine("[ImGui] Step 2 OK");

        Console.WriteLine("[ImGui] Step 3: CreateDeviceResources...");
        CreateDeviceResources();
        Console.WriteLine("[ImGui] Step 3 OK");

        Console.WriteLine("[ImGui] Step 4: UpdateFontTexture...");
        UpdateFontTexture();
        Console.WriteLine("[ImGui] Step 4 OK");

        // ── Step 5: Set up GLFW character callback for ImGui text input ──
        InstallCharCallback();

        Console.WriteLine("[ImGuiController] Initialized.");
    }

    /// <summary>Load glfwSetCharCallback and register the static OnChar handler.</summary>
    private void InstallCharCallback()
    {
        var glfwLib = Glfw.GetglfwLib();
        if (glfwLib == 0) return;

        // Signature: GLFWcharfun glfwSetCharCallback(GLFWwindow*, GLFWcharfun)
        var setCharFn = (delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, uint, void>, IntPtr>)
            NativeLibrary.GetExport(glfwLib, "glfwSetCharCallback");

        setCharFn(_window, &OnChar);
        Console.WriteLine("[ImGui] Char callback installed.");
    }

    // ═══════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════

    public void NewFrame(float deltaTime)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(Glfw.WindowWidth, Glfw.WindowHeight);
        io.DisplayFramebufferScale = new Vector2(1f, 1f);
        io.DeltaTime = deltaTime > 0f ? deltaTime : 1f / 60f;
        PollMouse(io);
        PollKeyboard(io);
        ImGui.NewFrame();
    }

    public void Render()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    public void Dispose()
    {
        if (_vao != 0) { fixed (uint* p = &_vao) GL.DeleteVertexArrays(1, p); _vao = 0; }
        if (_vbo != 0) { fixed (uint* p = &_vbo) GL.DeleteBuffers(1, p); _vbo = 0; }
        if (_ebo != 0) { fixed (uint* p = &_ebo) GL.DeleteBuffers(1, p); _ebo = 0; }
        if (_fontTexture != 0) { fixed (uint* p = &_fontTexture) GL.DeleteTextures(1, p); _fontTexture = 0; }
        if (_shader != 0) GL.DeleteProgram(_shader);
        if (_vertShader != 0) GL.DeleteShader(_vertShader);
        if (_fragShader != 0) GL.DeleteShader(_fragShader);
        ImGui.DestroyContext();
        Console.WriteLine("[ImGuiController] Shutdown.");
    }

    // ═══════════════════════════════════════════════
    //  INPUT
    // ═══════════════════════════════════════════════

    private void PollMouse(ImGuiIOPtr io)
    {
        var pos = Mouse.GetPosition();
        io.MousePos = new Vector2(pos.X, pos.Y);
        io.MouseDown[0] = Mouse.IsButtonDown(Const.GLFW_MOUSE_BUTTON_LEFT);
        io.MouseDown[1] = Mouse.IsButtonDown(Const.GLFW_MOUSE_BUTTON_RIGHT);
        io.MouseDown[2] = Mouse.IsButtonDown(Const.GLFW_MOUSE_BUTTON_MIDDLE);
        io.MouseWheel = Mouse.GetScrollDeltaY();
    }

    private void PollKeyboard(ImGuiIOPtr io)
    {
        io.AddKeyEvent(ImGuiKey.ModCtrl, Keyboard.IsKeyDown(_window, Const.GLFW_KEY_LEFT_CONTROL) ||
                                          Keyboard.IsKeyDown(_window, Const.GLFW_KEY_RIGHT_CONTROL));
        io.AddKeyEvent(ImGuiKey.ModShift, Keyboard.IsKeyDown(_window, Const.GLFW_KEY_LEFT_SHIFT) ||
                                           Keyboard.IsKeyDown(_window, Const.GLFW_KEY_RIGHT_SHIFT));
        io.AddKeyEvent(ImGuiKey.ModAlt, Keyboard.IsKeyDown(_window, Const.GLFW_KEY_LEFT_ALT) ||
                                         Keyboard.IsKeyDown(_window, Const.GLFW_KEY_RIGHT_ALT));
        io.AddKeyEvent(ImGuiKey.ModSuper, Keyboard.IsKeyDown(_window, Const.GLFW_KEY_LEFT_SUPER) ||
                                           Keyboard.IsKeyDown(_window, Const.GLFW_KEY_RIGHT_SUPER));
        foreach (var kvp in _glfwToImGuiKey)
            io.AddKeyEvent(kvp.Value, Keyboard.IsKeyDown(_window, kvp.Key));

        // Drain character queue — needed for ImGui text input (DragFloat, InputText, etc.)
        lock (_charQueue)
        {
            if (_charQueue.Count > 0)
            {
                foreach (var cp in _charQueue)
                    io.AddInputCharacter(cp);
                _charQueue.Clear();
            }
        }
    }

    private void BuildKeyMap()
    {
        Map(Const.GLFW_KEY_LEFT, ImGuiKey.LeftArrow);
        Map(Const.GLFW_KEY_RIGHT, ImGuiKey.RightArrow);
        Map(Const.GLFW_KEY_UP, ImGuiKey.UpArrow);
        Map(Const.GLFW_KEY_DOWN, ImGuiKey.DownArrow);
        Map(Const.GLFW_KEY_TAB, ImGuiKey.Tab);
        Map(Const.GLFW_KEY_BACKSPACE, ImGuiKey.Backspace);
        Map(Const.GLFW_KEY_ENTER, ImGuiKey.Enter);
        Map(Const.GLFW_KEY_ESCAPE, ImGuiKey.Escape);
        Map(Const.GLFW_KEY_SPACE, ImGuiKey.Space);
        Map(Const.GLFW_KEY_DELETE, ImGuiKey.Delete);
        Map(Const.GLFW_KEY_INSERT, ImGuiKey.Insert);
        Map(Const.GLFW_KEY_HOME, ImGuiKey.Home);
        Map(Const.GLFW_KEY_END, ImGuiKey.End);
        Map(Const.GLFW_KEY_PAGE_UP, ImGuiKey.PageUp);
        Map(Const.GLFW_KEY_PAGE_DOWN, ImGuiKey.PageDown);
        Map(Const.GLFW_KEY_A, ImGuiKey.A); Map(Const.GLFW_KEY_B, ImGuiKey.B);
        Map(Const.GLFW_KEY_C, ImGuiKey.C); Map(Const.GLFW_KEY_D, ImGuiKey.D);
        Map(Const.GLFW_KEY_E, ImGuiKey.E); Map(Const.GLFW_KEY_F, ImGuiKey.F);
        Map(Const.GLFW_KEY_G, ImGuiKey.G); Map(Const.GLFW_KEY_H, ImGuiKey.H);
        Map(Const.GLFW_KEY_I, ImGuiKey.I); Map(Const.GLFW_KEY_J, ImGuiKey.J);
        Map(Const.GLFW_KEY_K, ImGuiKey.K); Map(Const.GLFW_KEY_L, ImGuiKey.L);
        Map(Const.GLFW_KEY_M, ImGuiKey.M); Map(Const.GLFW_KEY_N, ImGuiKey.N);
        Map(Const.GLFW_KEY_O, ImGuiKey.O); Map(Const.GLFW_KEY_P, ImGuiKey.P);
        Map(Const.GLFW_KEY_Q, ImGuiKey.Q); Map(Const.GLFW_KEY_R, ImGuiKey.R);
        Map(Const.GLFW_KEY_S, ImGuiKey.S); Map(Const.GLFW_KEY_T, ImGuiKey.T);
        Map(Const.GLFW_KEY_U, ImGuiKey.U); Map(Const.GLFW_KEY_V, ImGuiKey.V);
        Map(Const.GLFW_KEY_W, ImGuiKey.W); Map(Const.GLFW_KEY_X, ImGuiKey.X);
        Map(Const.GLFW_KEY_Y, ImGuiKey.Y); Map(Const.GLFW_KEY_Z, ImGuiKey.Z);
        Map(Const.GLFW_KEY_0, ImGuiKey._0); Map(Const.GLFW_KEY_1, ImGuiKey._1);
        Map(Const.GLFW_KEY_2, ImGuiKey._2); Map(Const.GLFW_KEY_3, ImGuiKey._3);
        Map(Const.GLFW_KEY_4, ImGuiKey._4); Map(Const.GLFW_KEY_5, ImGuiKey._5);
        Map(Const.GLFW_KEY_6, ImGuiKey._6); Map(Const.GLFW_KEY_7, ImGuiKey._7);
        Map(Const.GLFW_KEY_8, ImGuiKey._8); Map(Const.GLFW_KEY_9, ImGuiKey._9);
        Map(Const.GLFW_KEY_F1, ImGuiKey.F1); Map(Const.GLFW_KEY_F2, ImGuiKey.F2);
        Map(Const.GLFW_KEY_F3, ImGuiKey.F3); Map(Const.GLFW_KEY_F4, ImGuiKey.F4);
        Map(Const.GLFW_KEY_F5, ImGuiKey.F5); Map(Const.GLFW_KEY_F6, ImGuiKey.F6);
        Map(Const.GLFW_KEY_F7, ImGuiKey.F7); Map(Const.GLFW_KEY_F8, ImGuiKey.F8);
        Map(Const.GLFW_KEY_F9, ImGuiKey.F9); Map(Const.GLFW_KEY_F10, ImGuiKey.F10);
        Map(Const.GLFW_KEY_F11, ImGuiKey.F11); Map(Const.GLFW_KEY_F12, ImGuiKey.F12);
        Map(Const.GLFW_KEY_F13, ImGuiKey.F13); Map(Const.GLFW_KEY_F14, ImGuiKey.F14);
        Map(Const.GLFW_KEY_F15, ImGuiKey.F15);
    }

    private void Map(int glfwKey, ImGuiKey imGuiKey) => _glfwToImGuiKey[glfwKey] = imGuiKey;

    // ═══════════════════════════════════════════════
    //  OPENGL BACKEND
    // ═══════════════════════════════════════════════

    private void CreateDeviceResources()
    {
        string vsSrc = @"#version 330 core
            layout(location = 0) in vec2 aPos;
            layout(location = 1) in vec2 aUV;
            layout(location = 2) in vec4 aColor;
            uniform mat4 ProjMtx;
            out vec2 Frag_UV;
            out vec4 Frag_Color;
            void main()
            {
                Frag_UV = aUV;
                Frag_Color = aColor;
                gl_Position = ProjMtx * vec4(aPos.xy, 0.0, 1.0);
            }";

        string fsSrc = @"#version 330 core
            in vec2 Frag_UV;
            in vec4 Frag_Color;
            uniform sampler2D Texture;
            layout(location = 0) out vec4 Out_Color;
            void main()
            {
                Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
            }";

        _vertShader = GL.CreateShader(Const.GL_VERTEX_SHADER);
        GL.ShaderSource(_vertShader, vsSrc);
        GL.CompileShader(_vertShader);

        _fragShader = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
        GL.ShaderSource(_fragShader, fsSrc);
        GL.CompileShader(_fragShader);

        _shader = GL.CreateProgram();
        GL.AttachShader(_shader, _vertShader);
        GL.AttachShader(_shader, _fragShader);
        GL.LinkProgram(_shader);

        _projMtxLoc = GL.GetUniformLocation(_shader, "ProjMtx");
        _textureLoc = GL.GetUniformLocation(_shader, "Texture");

        fixed (uint* pVao = &_vao) GL.GenVertexArrays(1, pVao);
        fixed (uint* pVbo = &_vbo) GL.GenBuffers(1, pVbo);
        fixed (uint* pEbo = &_ebo) GL.GenBuffers(1, pEbo);

        GL.BindVertexArray(_vao);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
        GL.BindBuffer(Const.GL_ELEMENT_ARRAY_BUFFER, _ebo);

        // ImDrawVert: pos(2*float), uv(2*float), col(uint32) = 20 bytes
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 20, (void*)0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 20, (void*)8);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, Const.GL_UNSIGNED_BYTE, true, 20, (void*)16);
        GL.BindVertexArray(0);
    }

    private void UpdateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bpp);

        fixed (uint* pTex = &_fontTexture)
            GL.GenTextures(1, pTex);

        GL.BindTexture(Const.GL_TEXTURE_2D, _fontTexture);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
        GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, width, height, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, pixels.ToPointer());

        io.Fonts.SetTexID(new nint(unchecked((int)_fontTexture)));
        io.Fonts.ClearTexData();
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0) return;

        // Save GL state
        int lastProgram = 0, lastTexture = 0, lastVAO = 0;
        int* scissorBox = stackalloc int[4];
        int lastActive = 0;

        bool lastBlend = GL.IsEnabled(Const.GL_BLEND);
        bool lastCull = GL.IsEnabled(Const.GL_CULL_FACE);
        bool lastDepth = GL.IsEnabled(Const.GL_DEPTH_TEST);
        bool lastScissor = GL.IsEnabled(Const.GL_SCISSOR_TEST);

        GL.GetIntegerv(Const.GL_ACTIVE_TEXTURE, &lastActive);
        GL.ActiveTexture(Const.GL_TEXTURE0);
        GL.GetIntegerv(Const.GL_CURRENT_PROGRAM, &lastProgram);
        GL.GetIntegerv(Const.GL_TEXTURE_BINDING_2D, &lastTexture);
        GL.GetIntegerv(Const.GL_VERTEX_ARRAY_BINDING, &lastVAO);
        GL.GetIntegerv(Const.GL_SCISSOR_BOX, scissorBox);

        // Setup ImGui state
        GL.Enable(Const.GL_BLEND);
        GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
        GL.Disable(Const.GL_CULL_FACE);
        GL.Disable(Const.GL_DEPTH_TEST);
        GL.Enable(Const.GL_SCISSOR_TEST);

        // Ortho projection
        float l = 0f, r = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float t = 0f, b = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
        Matrix4x4 proj = Matrix4x4.Identity;
        proj.M11 = 2f / (r - l); proj.M22 = 2f / (t - b);
        proj.M41 = (r + l) / (l - r); proj.M42 = (t + b) / (b - t);

        GL.UseProgram(_shader);
        GL.Uniform1i(_textureLoc, 0);
        GL.UniformMatrix4fv(_projMtxLoc, 1, false, (float*)&proj);
        GL.BindVertexArray(_vao);

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            int vtxCount = cmdList.VtxBuffer.Size;
            int idxCount = cmdList.IdxBuffer.Size;

            if (vtxCount > _vboCapacity) _vboCapacity = vtxCount + 2048;
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
            if (cmdList.VtxBuffer.Data == IntPtr.Zero || vtxCount == 0) continue;
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(vtxCount * 20), cmdList.VtxBuffer.Data.ToPointer(), Const.GL_STREAM_DRAW);

            if (idxCount > _eboCapacity) _eboCapacity = idxCount + 4096;
            GL.BindBuffer(Const.GL_ELEMENT_ARRAY_BUFFER, _ebo);
            if (cmdList.IdxBuffer.Data == IntPtr.Zero || idxCount == 0) continue;
            GL.BufferData(Const.GL_ELEMENT_ARRAY_BUFFER, (nuint)(idxCount * 2), cmdList.IdxBuffer.Data.ToPointer(), Const.GL_STREAM_DRAW);

            for (int ci = 0; ci < cmdList.CmdBuffer.Size; ci++)
            {
                var cmd = cmdList.CmdBuffer[ci];
                if (cmd.ElemCount == 0) continue;

                int cx = (int)(cmd.ClipRect.X - drawData.DisplayPos.X);
                int cy = (int)(cmd.ClipRect.Y - drawData.DisplayPos.Y);
                int cw = (int)(cmd.ClipRect.Z - cmd.ClipRect.X);
                int ch = (int)(cmd.ClipRect.W - cmd.ClipRect.Y);
                GL.Scissor(cx, Glfw.WindowHeight - cy - ch, cw, ch);

                uint tid = unchecked((uint)(nint)cmd.GetTexID());
                GL.BindTexture(Const.GL_TEXTURE_2D, tid);

                if (_hasVtxOffset)
                {
                    GL.DrawElementsBaseVertex(Const.GL_TRIANGLES,
                        (int)cmd.ElemCount, Const.GL_UNSIGNED_SHORT,
                        (void*)(cmd.IdxOffset * 2), (int)cmd.VtxOffset);
                }
                else
                {
                    GL.DrawElements(Const.GL_TRIANGLES,
                        (int)cmd.ElemCount, Const.GL_UNSIGNED_SHORT,
                        (void*)(cmd.IdxOffset * 2));
                }
            }
        }

        // Restore GL state
        GL.UseProgram((uint)lastProgram);
        GL.BindTexture(Const.GL_TEXTURE_2D, (uint)lastTexture);
        GL.BindVertexArray((uint)lastVAO);
        GL.ActiveTexture((uint)lastActive);
        if (lastBlend) GL.Enable(Const.GL_BLEND); else GL.Disable(Const.GL_BLEND);
        if (lastCull) GL.Enable(Const.GL_CULL_FACE); else GL.Disable(Const.GL_CULL_FACE);
        if (lastDepth) GL.Enable(Const.GL_DEPTH_TEST); else GL.Disable(Const.GL_DEPTH_TEST);
        if (lastScissor) GL.Enable(Const.GL_SCISSOR_TEST); else GL.Disable(Const.GL_SCISSOR_TEST);
        GL.Scissor(scissorBox[0], scissorBox[1], scissorBox[2], scissorBox[3]);
    }
}
