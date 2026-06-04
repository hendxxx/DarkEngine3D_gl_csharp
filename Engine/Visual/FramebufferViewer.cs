using System;
using DarkEngine3D_gl_csharp.Engine.Libs;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public class FramebufferViewer : IDisposable
    {
        private uint _vao;
        private uint _vbo;
        private Shader _shader;

        public FramebufferViewer()
        {
            CreateQuad();
            CreateShader();
        }

        private unsafe void CreateQuad()
        {
            // pos.xy, uv.xy
            float[] vertices =
            [
                // x, y,   u, v
                -1f, -1f, 0f, 0f,
                 1f, -1f, 1f, 0f,
                 1f,  1f, 1f, 1f,

                -1f, -1f, 0f, 0f,
                 1f,  1f, 1f, 1f,
                -1f,  1f, 0f, 1f
            ];

            uint[] vaos = new uint[1];
            uint[] vbos = new uint[1];

            fixed (uint* pVao = vaos)
            fixed (uint* pVbo = vbos)
            {
                GL.GenVertexArrays(1, pVao);
                GL.GenBuffers(1, pVbo);
            }

            _vao = vaos[0];
            _vbo = vbos[0];

            GL.BindVertexArray(_vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);

            fixed (float* pVerts = vertices)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER,
                    (nuint)(vertices.Length * sizeof(float)),
                    pVerts,
                    Const.GL_STATIC_DRAW);
            }

            // location 0 = vec2 aPos
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);

            // location 1 = vec2 aUV
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);
            GL.BindVertexArray(0);
        }

        private void CreateShader()
        {
            string vs = """
            #version 330 core
            layout (location = 0) in vec2 aPos;
            layout (location = 1) in vec2 aUV;

            out vec2 TexCoord;

            uniform vec2 uOffset; // posisi center di NDC
            uniform vec2 uScale;  // ukuran quad di NDC

            void main()
            {
                vec2 pos = aPos * uScale + uOffset;
                gl_Position = vec4(pos, 0.0, 1.0);
                TexCoord = aUV;
            }
            """;

            string fs = """
            #version 330 core
            in vec2 TexCoord;
            out vec4 FragColor;

            uniform sampler2D uDepthMap;
            uniform float uNearPlane;
            uniform float uFarPlane;
            uniform int uVisualizeLinear;

            float LinearizeDepth(float depth)
            {
                // Untuk perspective projection biasanya perlu ini.
                // Tapi shadow map directional light + ortho sering cukup pakai raw depth.
                float z = depth * 2.0 - 1.0;
                return (2.0 * uNearPlane * uFarPlane) / (uFarPlane + uNearPlane - z * (uFarPlane - uNearPlane));
            }

            void main()
            {
                float depthValue = texture(uDepthMap, TexCoord).r;

                float c = depthValue;

                if (uVisualizeLinear == 1)
                {
                    float linear = LinearizeDepth(depthValue) / uFarPlane;
                    c = linear;
                }

                // Supaya lebih enak dilihat, depth dekat biasanya gelap.
                // Kalau mau dibalik, gunakan 1.0 - c
                FragColor = vec4(vec3(c), 1.0);
            }
            """;

            _shader = new Shader(vs, fs); // sesuaikan dengan class Shader punyamu
        }

        /// <summary>
        /// Render satu depth texture ke area overlay tertentu
        /// x,y,width,height dalam pixel layar, origin diasumsikan bottom-left OpenGL viewport
        /// </summary>
        public void RenderDepthTexture(uint texture, int screenWidth, int screenHeight, int x, int y, int width, int height,
                                       float nearPlane = 0.1f, float farPlane = 300.0f, bool linearize = false)
        {
            float ndcX = ((x + width * 0.5f) / (float)screenWidth) * 2.0f - 1.0f;
            float ndcY = ((y + height * 0.5f) / (float)screenHeight) * 2.0f - 1.0f;

            float ndcW = width / (float)screenWidth;
            float ndcH = height / (float)screenHeight;

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

            GL.Disable(Const.GL_DEPTH_TEST);
            GL.DepthMask(false);

            _shader.Use();
            _shader.SetInt("uDepthMap", 0);
            _shader.SetVec2("uOffset", ndcX, ndcY);
            _shader.SetVec2("uScale", ndcW, ndcH);
            _shader.SetFloat("uNearPlane", nearPlane);
            _shader.SetFloat("uFarPlane", farPlane);
            _shader.SetInt("uVisualizeLinear", linearize ? 1 : 0);

            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, texture);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            GL.DepthMask(true);
            GL.Enable(Const.GL_DEPTH_TEST);
        }

        /// <summary>
        /// Tampilkan semua cascade shadow map di kanan atas
        /// </summary>
        public void RenderCSM(CSM csm, int screenWidth, int screenHeight)
        {
            int margin = 10;
            int boxWidth = 220;
            int boxHeight = 220;
            int spacing = 10;

            // 3 kotak berjajar dari kanan ke kiri
            for (int i = 0; i < CSM.NumCascades; i++)
            {
                int drawIndex = CSM.NumCascades - 1 - i;
                int x = screenWidth - margin - boxWidth - i * (boxWidth + spacing);
                int y = screenHeight - margin - boxHeight;

                // kalau viewport OpenGL kamu bottom-left origin, y perlu diubah:
                // karena perhitungan NDC di atas assume pixel origin bottom-left.
                // Kalau screenHeight - margin - boxHeight adalah gaya top-left UI,
                // konversi:
                int glY = screenHeight - y - boxHeight;

                RenderDepthTexture(
                    csm.ShadowTextures[drawIndex],
                    screenWidth,
                    screenHeight,
                    x,
                    glY,
                    boxWidth,
                    boxHeight,
                    0.1f,
                    csm.CascadeEnds[drawIndex],
                    false
                );
            }
        }

        public unsafe void Dispose()
        {
            if (_vbo != 0)
            {
                uint vbo = _vbo;
                GL.DeleteBuffers(1, &vbo);
                _vbo = 0;
            }

            if (_vao != 0)
            {
                uint vao = _vao;
                GL.DeleteVertexArrays(1, &vao);
                _vao = 0;
            }

            _shader?.Dispose();
        }
    }
}