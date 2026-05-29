using System;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Libs;

public unsafe class RainOverlay
{
    public uint VAO, VBO;
    private uint shader;

    private int uSceneTex;
    private int uRainAmount;
    private int uTime;

    public RainOverlay(uint overlayShader)
    {
        shader = overlayShader;

        GenerateFullscreenQuad();
        SetupVAO();
        CacheUniforms();
    }

    // ============================
    // 1. FULLSCREEN QUAD
    // ============================
    void GenerateFullscreenQuad()
    {
        float[] quad =
        {
            // pos      // uv
            -1f, -1f,   0f, 0f,
             1f, -1f,   1f, 0f,
             1f,  1f,   1f, 1f,

            -1f, -1f,   0f, 0f,
             1f,  1f,   1f, 1f,
            -1f,  1f,   0f, 1f
        };

        uint _VBO = 0;
        GL.GenBuffers(1, &_VBO);

        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _VBO);

        fixed (void* iptr = quad)
        {
            GL.BufferData(Const.GL_ARRAY_BUFFER,
                          (uint)(quad.Length * sizeof(float)),
                          iptr,
                          (uint)Const.GL_STATIC_DRAW);
        }

        VBO = _VBO;
    }

    // ============================
    // 2. VAO SETUP
    // ============================
    void SetupVAO()
    {
        uint _VAO = 0;
        GL.GenVertexArrays(1, &_VAO);
        GL.BindVertexArray(_VAO);

        GL.BindBuffer(Const.GL_ARRAY_BUFFER, VBO);

        // aPos (vec2)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);

        // aUV (vec2)
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

        VAO = _VAO;
    }

    // ============================
    // 3. CACHE UNIFORMS
    // ============================
    void CacheUniforms()
    {
        uSceneTex = GL.GetUniformLocation(shader, "uSceneTex");
        uRainAmount = GL.GetUniformLocation(shader, "uRainAmount");
        uTime = GL.GetUniformLocation(shader, "uTime");
    }

    // ============================
    // 4. DRAW FULLSCREEN PASS
    // ============================
    public void Draw(uint sceneTexture, float rainAmount, float time)
    {
        GL.UseProgram(shader);

        // Bind scene texture
        GL.ActiveTexture(Const.GL_TEXTURE0);
        GL.BindTexture(Const.GL_TEXTURE_2D, sceneTexture);
        GL.Uniform1i(uSceneTex, 0);

        GL.Uniform1f(uRainAmount, rainAmount);
        GL.Uniform1f(uTime, time);

        GL.BindVertexArray(VAO);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
    }
}
