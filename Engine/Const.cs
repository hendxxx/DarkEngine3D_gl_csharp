using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DarkEngine3D_gl_csharp.Engine
{
    public static class Const
    {
        public const int GLFW_KEY_W = 87, GLFW_KEY_S = 83, GLFW_KEY_A = 65, GLFW_KEY_D = 68;
        public const int GLFW_PRESS = 1;

        public const int GLFW_KEY_ESCAPE = 256;
        public const int GLFW_RELEASE = 0;

        public const uint GL_FRONT_AND_BACK = 0x0408;
        public const uint GL_LINE = 0x1B01;
        public const uint GL_FILL = 0x1B02;

        public const int GLFW_KEY_F1 = 290; 

        public const uint GL_COLOR_BUFFER_BIT = 0x00004000;
        public const uint GL_DEPTH_BUFFER_BIT = 0x00000100;
         
        public const int GLFW_CURSOR = 0x00033001;
        public const int GLFW_CURSOR_DISABLED = 0x00034003;

        public const uint GL_DEPTH_TEST = 0x0B71;

        public const uint GL_ARRAY_BUFFER = 0x8892;

        public const uint GL_STATIC_DRAW = 0x88E4;
        public const uint GL_FLOAT = 0x1406;
        public const uint GL_TRIANGLES = 0x0004;

        public const uint GL_VERTEX_SHADER = 0x8B31;
        public const uint GL_FRAGMENT_SHADER = 0x8B30;

    }
}
