using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// 2D sprite rendering for sidescroller game.
/// Renders a textured quad in world space using orthographic projection.
/// Supports position, scale, rotation, flip, color tint, opacity, and sprite sheet UV slicing.
/// </summary>
public unsafe class Sprite2D : IDisposable
{
    // ── Transform ──
    public Vector2 Position;       // World position (X=right, Y=up)
    public Vector2 Scale = Vector2.One;
    public float Rotation;         // Degrees, clockwise
    public Vector2 Pivot = new(0.5f, 0.5f); // 0,0 = bottom-left, 1,1 = top-right

    // ── Appearance ──
    public Vector4 TintColor = Vector4.One; // RGBA
    public float Opacity = 1f;
    public bool FlipX;
    public bool FlipY;

    // ── Sprite Sheet UV ──
    public Vector2 UVMin = Vector2.Zero;   // Top-left UV (0,0)
    public Vector2 UVMax = Vector2.One;    // Bottom-right UV (1,1)

    // ── Sorting ──
    public int SortingOrder; // Higher = drawn later (on top)

    // ── Layer / Tag ──
    public string Layer = "Default";
    public string Tag = "";

    // ── Texture ──
    private uint _textureId;
    public uint TextureId
    {
        get => _textureId;
        set => _textureId = value;
    }

    // ── Dimensions (pixels of source sprite, for world-size calculation) ──
    public float PixelWidth = 100f;
    public float PixelHeight = 100f;

    // ── Static shared quad mesh ──
    private static uint _quadVAO, _quadVBO, _quadEBO;
    private static bool _quadInitialized;

    // ── Static shader ──
    private static uint _shader;
    private static bool _shaderCompiled;
    private static int _locView, _locProj, _locModel;
    private static int _locTint, _locOpacity;

    // ── Visibility ──
    public bool IsVisible = true;

    // ── Physics (set by CollisionShape) ──
    public bool IsCollider;
    public bool IsTrigger;

    // ── Animation ──
    public bool IsAnimated;
    public float AnimFPS = 12f;
    public int AnimFrame;
    public int AnimFrameStart;
    public int AnimFrameEnd;
    public bool AnimLoop = true;
    public bool AnimPlaying;
    private float _animTimer;

    // ── Parent reference (for hierarchy in tilemap/object tree) ──
    public Sprite2D? Parent;

    // ── Name ──
    public string Name = "Sprite2D";

    // ── Instance ID ──
    public int InstanceId { get; }

    private static int _nextId;

    public Sprite2D()
    {
        InstanceId = Interlocked.Increment(ref _nextId);
    }

    /// <summary>
    /// Initialize the shared quad VAO/VBO/EBO (called once).
    /// </summary>
    public static void InitQuad()
    {
        if (_quadInitialized) return;
        _quadInitialized = true;

        // Quad vertices: pos(x,y) + uv(u,v)
        float[] vertices = [
            // pos       // uv
            -0.5f, -0.5f,  0f, 1f,  // bottom-left  (UV Y flipped for OpenGL)
             0.5f, -0.5f,  1f, 1f,  // bottom-right
             0.5f,  0.5f,  1f, 0f,  // top-right
            -0.5f,  0.5f,  0f, 0f,  // top-left
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];

        fixed (float* vPtr = vertices)
        fixed (uint* iPtr = indices)
        {
            fixed (uint* pVao = &_quadVAO) GL.GenVertexArrays(1, pVao);
            GL.BindVertexArray(_quadVAO);

            fixed (uint* pVbo = &_quadVBO) GL.GenBuffers(1, pVbo);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _quadVBO);
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(vertices.Length * sizeof(float)), vPtr, Const.GL_STATIC_DRAW);

            fixed (uint* pEbo = &_quadEBO) GL.GenBuffers(1, pEbo);
            GL.BindBuffer(Const.GL_ELEMENT_ARRAY_BUFFER, _quadEBO);
            GL.BufferData(Const.GL_ELEMENT_ARRAY_BUFFER, (nuint)(indices.Length * sizeof(uint)), iPtr, Const.GL_STATIC_DRAW);

            // Position attribute (location 0)
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);
            // UV attribute (location 1)
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

            GL.BindVertexArray(0);
        }
    }

    /// <summary>
    /// Compile the sprite shader if not already compiled.
    /// </summary>
    public static void InitShader()
    {
        if (_shaderCompiled) return;
        _shaderCompiled = true;

        string vertSrc = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUV;
uniform mat4 view;
uniform mat4 projection;
uniform mat4 model;
out vec2 vUV;
void main() {
    gl_Position = projection * view * model * vec4(aPos, 0.0, 1.0);
    vUV = aUV;
}";

        string fragSrc = @"#version 330 core
in vec2 vUV;
uniform sampler2D tex;
uniform vec4 tint;
uniform float opacity;
out vec4 FragColor;
void main() {
    vec4 c = texture(tex, vUV) * tint;
    c.a *= opacity;
    if (c.a < 0.01) discard;
    FragColor = c;
}";

        uint vert = GL.CreateShader(Const.GL_VERTEX_SHADER);
        GL.ShaderSource(vert, vertSrc);
        GL.CompileShader(vert);

        uint frag = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
        GL.ShaderSource(frag, fragSrc);
        GL.CompileShader(frag);

        _shader = GL.CreateProgram();
        GL.AttachShader(_shader, vert);
        GL.AttachShader(_shader, frag);
        GL.LinkProgram(_shader);
        GL.DeleteShader(vert);
        GL.DeleteShader(frag);

        _locView = GL.GetUniformLocation(_shader, "view");
        _locProj = GL.GetUniformLocation(_shader, "projection");
        _locModel = GL.GetUniformLocation(_shader, "model");
        _locTint = GL.GetUniformLocation(_shader, "tint");
        _locOpacity = GL.GetUniformLocation(_shader, "opacity");
    }

    /// <summary>
    /// Set UV slice from sprite sheet coordinates.
    /// </summary>
    public void SetSpriteSheetUV(int col, int row, int totalCols, int totalRows)
    {
        float uMin = (float)col / totalCols;
        float uMax = (float)(col + 1) / totalCols;
        float vMin = (float)row / totalRows;
        float vMax = (float)(row + 1) / totalRows;
        UVMin = new Vector2(uMin, vMin);
        UVMax = new Vector2(uMax, vMax);
    }

    /// <summary>
    /// Advance animation by deltaTime. Returns true if frame changed.
    /// </summary>
    public bool UpdateAnimation(float deltaTime)
    {
        if (!IsAnimated || !AnimPlaying) return false;

        _animTimer += deltaTime;
        float frameDuration = 1f / AnimFPS;
        if (_animTimer >= frameDuration)
        {
            _animTimer -= frameDuration;
            int prevFrame = AnimFrame;
            AnimFrame++;
            if (AnimFrame > AnimFrameEnd)
            {
                AnimFrame = AnimLoop ? AnimFrameStart : AnimFrameEnd;
                if (!AnimLoop) AnimPlaying = false;
            }
            return AnimFrame != prevFrame;
        }
        return false;
    }

    /// <summary>
    /// Play animation from start.
    /// </summary>
    public void PlayAnimation()
    {
        AnimFrame = AnimFrameStart;
        AnimPlaying = true;
        _animTimer = 0f;
    }

    /// <summary>
    /// Stop animation at current frame.
    /// </summary>
    public void StopAnimation()
    {
        AnimPlaying = false;
    }

    /// <summary>
    /// Build the model matrix from transform properties.
    /// </summary>
    public Matrix4x4 GetModelMatrix()
    {
        // Scale by pixel dimensions * vector scale
        float sx = PixelWidth * Scale.X * (FlipX ? -1f : 1f);
        float sy = PixelHeight * Scale.Y * (FlipY ? -1f : 1f);

        var scale = Matrix4x4.CreateScale(sx, sy, 1f);
        var rotation = Matrix4x4.CreateRotationZ(MathHelper.DegreesToRadians(Rotation));
        var translate = Matrix4x4.CreateTranslation(Position.X, Position.Y, 0f);

        // Pivot offset: shift so pivot point is at Position
        float pivotX = (0.5f - Pivot.X) * PixelWidth * Scale.X;
        float pivotY = (0.5f - Pivot.Y) * PixelHeight * Scale.Y;
        var pivotOffset = Matrix4x4.CreateTranslation(pivotX, pivotY, 0f);

        return scale * rotation * pivotOffset * translate;
    }

    /// <summary>
    /// Draw this sprite using the shared quad mesh.
    /// </summary>
    public void Draw(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
    {
        if (!IsVisible || _textureId == 0) return;

        InitQuad();
        InitShader();

        GL.UseProgram(_shader);
        GL.UniformMatrix4fv(_locView, 1, false, (float*)&viewMatrix);
        GL.UniformMatrix4fv(_locProj, 1, false, (float*)&projectionMatrix);

        var model = GetModelMatrix();
        GL.UniformMatrix4fv(_locModel, 1, false, (float*)&model);

        GL.Uniform4f(_locTint, TintColor.X, TintColor.Y, TintColor.Z, TintColor.W);
        GL.Uniform1f(_locOpacity, Opacity * (Parent?.Opacity ?? 1f));

        GL.ActiveTexture(Const.GL_TEXTURE0);
        GL.BindTexture(Const.GL_TEXTURE_2D, _textureId);

        GL.BindVertexArray(_quadVAO);
        GL.DrawElements(Const.GL_TRIANGLES, 6, Const.GL_UNSIGNED_INT, (void*)0);
        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Batch draw multiple sprites with the same texture (more efficient).
    /// </summary>
    public static void DrawBatch(List<Sprite2D> sprites, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
    {
        if (sprites.Count == 0) return;

        InitQuad();
        InitShader();

        GL.UseProgram(_shader);
        GL.UniformMatrix4fv(_locView, 1, false, (float*)&viewMatrix);
        GL.UniformMatrix4fv(_locProj, 1, false, (float*)&projectionMatrix);

        GL.BindVertexArray(_quadVAO);

        // Group by texture for batch rendering
        var sorted = sprites.Where(s => s.IsVisible && s._textureId != 0)
                           .OrderBy(s => s._textureId)
                           .ThenBy(s => s.SortingOrder);

        uint currentTex = 0;
        foreach (var sprite in sorted)
        {
            if (sprite._textureId != currentTex)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, sprite._textureId);
                currentTex = sprite._textureId;
            }

            var model = sprite.GetModelMatrix();
            GL.UniformMatrix4fv(_locModel, 1, false, (float*)&model);

            var tint = sprite.TintColor;
            float opacity = sprite.Opacity * (sprite.Parent?.Opacity ?? 1f);
            GL.Uniform4f(_locTint, tint.X, tint.Y, tint.Z, tint.W);
            GL.Uniform1f(_locOpacity, opacity);

            GL.DrawElements(Const.GL_TRIANGLES, 6, Const.GL_UNSIGNED_INT, (void*)0);
        }

        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        // Don't delete shared resources (quad, shader) here
        GC.SuppressFinalize(this);
    }

    // ── Math helper ──
    private static class MathHelper
    {
        public static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
    }
}
