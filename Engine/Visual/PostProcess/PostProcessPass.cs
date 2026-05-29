using DarkEngine3D_gl_csharp.Engine.Libs;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{
    public unsafe class RainOverlayPass : IPostProcessPass
    {
        private uint _shader;
        private uint _vao, _vbo;

        private int uSceneTex;
        private int uRainAmount;
        private int uTime;

        // external control
        public float RainAmount = 0f;

        public RainOverlayPass(uint shader)
        {
            _shader = shader;
        }

        public void Init()
        {
            // fullscreen quad
            float[] quad =
            {
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
            -1f, -1f, 0f, 0f,
             1f,  1f, 1f, 1f,
            -1f,  1f, 0f, 1f
        };

            uint vao = 0, vbo = 0;
            GL.GenVertexArrays(1, &vao);
            GL.GenBuffers(1, &vbo);

            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            fixed (float* v = quad)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER,
                              (nuint)(quad.Length * sizeof(float)),
                              v,
                              Const.GL_STATIC_DRAW);
            }

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

            GL.BindVertexArray(0);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);

            _vao = vao;
            _vbo = vbo;

            // cache uniform
            uSceneTex = GL.GetUniformLocation(_shader, "uSceneTex");
            uRainAmount = GL.GetUniformLocation(_shader, "uRainAmount");
            uTime = GL.GetUniformLocation(_shader, "uTime");
        }

        public void Execute(uint inputTexture, int width, int height, float time)
        {
            GL.UseProgram(_shader);

            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, inputTexture);
            GL.Uniform1i(uSceneTex, 0);

            GL.Uniform1f(uRainAmount, RainAmount);
            GL.Uniform1f(uTime, time);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }
    }
}