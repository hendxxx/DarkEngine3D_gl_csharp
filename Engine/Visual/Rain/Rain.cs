using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Libs;

public unsafe class Rain
{
    const int Vector3Size = sizeof(float) * 3;

    public static uint VAO, quadVBO, instanceVBO;
    public int rainCount;

    private uint shader;

    private int uView, uProj, uCameraPos, uWindDir, uRainAmount, uTime;

    public Vector3[] instancePositions;

    public Rain(uint rainShader, int count)
    {
        shader = rainShader;
        rainCount = count;

        GenerateQuad();
        GenerateInstances();
        SetupVAO();
        CacheUniforms();
    }

    // ============================
    // 1. QUAD VBO (unsafe version)
    // ============================
    static void GenerateQuad()
    {
        float[] quadVertices =
        {
            // pos      // uv
            -0.5f, -0.5f,  0f, 0f,
             0.5f, -0.5f,  1f, 0f,
             0.5f,  0.5f,  1f, 1f,

            -0.5f, -0.5f,  0f, 0f,
             0.5f,  0.5f,  1f, 1f,
            -0.5f,  0.5f,  0f, 1f
        };

        uint _quadVBO = 0;
        GL.GenBuffers(1, &_quadVBO);

        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _quadVBO);

        fixed (void* iptr = quadVertices)
        {
            GL.BufferData(Const.GL_ARRAY_BUFFER,
                          (uint)(quadVertices.Length * sizeof(float)),
                          iptr,
                          (uint)Const.GL_STATIC_DRAW);
        }

        quadVBO = _quadVBO;
    }

    // ============================
    // 2. INSTANCE VBO (unsafe)
    // ============================
    void GenerateInstances()
    {
        instancePositions = new Vector3[rainCount];
        Random rnd = new Random();

        for (int i = 0; i < rainCount; i++)
        {
            instancePositions[i] = new Vector3(
                (float)(rnd.NextDouble() * 150 - 75),
                (float)(rnd.NextDouble() * 40 + 10),
                (float)(rnd.NextDouble() * 150 - 75)
            );
        }

        uint _instanceVBO = 0;
        GL.GenBuffers(1, &_instanceVBO);

        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _instanceVBO);

        fixed (void* iptr = instancePositions)
        {
            GL.BufferData(Const.GL_ARRAY_BUFFER,
                          (uint)(rainCount * Vector3Size),
                          iptr,
                          (uint)Const.GL_DYNAMIC_DRAW);
        }

        instanceVBO = _instanceVBO;
    }

    // ============================
    // 3. VAO SETUP
    // ============================
    void SetupVAO()
    {
        uint _VAO = 0;

        GL.GenVertexArrays(1, &_VAO);
        GL.BindVertexArray(_VAO);

        // Quad VBO
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, quadVBO);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);

        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

        // Instance VBO
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, instanceVBO);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 3, Const.GL_FLOAT, false, Vector3Size, (void*)0);
        GL.VertexAttribDivisor(2, 1);

        VAO = _VAO;
    }

    // ============================
    // 4. CACHE UNIFORMS
    // ============================
    void CacheUniforms()
    {
        uView = GL.GetUniformLocation(shader, "uView");
        uProj = GL.GetUniformLocation(shader, "uProj");
        uCameraPos = GL.GetUniformLocation(shader, "uCameraPos");
        uWindDir = GL.GetUniformLocation(shader, "uWindDir");
        uRainAmount = GL.GetUniformLocation(shader, "uRainAmount");
        uTime = GL.GetUniformLocation(shader, "uTime");
    }

    // ============================
    // 5. UPDATE RAIN FALL
    // ============================
    public void Update(Vector3 camPos, Vector3 windDir)
    {
        for (int i = 0; i < rainCount; i++)
        {
            //instancePositions[i] += windDir * 0.1f;
            instancePositions[i].Y -= 0.8f;

            if (instancePositions[i].Y < camPos.Y - 5)
                instancePositions[i].Y = camPos.Y + 30;
        }

        GL.BindBuffer(Const.GL_ARRAY_BUFFER, instanceVBO);

        fixed (void* iptr = instancePositions)
        {
            GL.BufferSubData(Const.GL_ARRAY_BUFFER,
                             (nuint)IntPtr.Zero,
                             (uint)(rainCount * Vector3Size),
                             iptr);
        }
    }

    // ============================
    // 6. DRAW
    // ============================
    public void Draw(Matrix4x4 view, Matrix4x4 proj, Vector3 camPos, Vector3 windDir, float rainAmount, float time)
    {
        GL.UseProgram(shader);

        GL.UniformMatrix4fv(uView, 1, false, (float*)&view);
        GL.UniformMatrix4fv(uProj, 1, false, (float*)&proj);
        GL.Uniform3f(uCameraPos, camPos.X,camPos.Y, camPos.Z);
        GL.Uniform3f(uWindDir, windDir.X, windDir.Y, windDir.Z);
        GL.Uniform1f(uRainAmount, rainAmount);
        GL.Uniform1f(uTime, time);

        GL.BindVertexArray(VAO);
        GL.DrawArraysInstanced(Const.GL_TRIANGLES, 0, 6, rainCount);
    }
}
