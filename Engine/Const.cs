using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DarkEngine3D_gl_csharp.Engine
{
    public static class Const
    { 
        public const uint GL_FRONT_AND_BACK = 0x0408;
        public const uint GL_LINE = 0x1B01;
        public const uint GL_FILL = 0x1B02;
        public const uint GL_DYNAMIC_DRAW = 0x88E4;
        
        public const uint GL_COLOR_BUFFER_BIT = 0x00004000;
        public const uint GL_DEPTH_BUFFER_BIT = 0x00000100;
         
        public const int GLFW_CURSOR = 0x00033001;
        public const int GLFW_CURSOR_NORMAL = 0x00034001;
        public const int GLFW_CURSOR_DISABLED = 0x00034003;

        public const uint GL_DEPTH_TEST = 0x0B71;

        public const uint GL_ARRAY_BUFFER = 0x8892;

        public const uint GL_STATIC_DRAW = 0x88E4;
        public const uint GL_FLOAT = 0x1406;
        public const uint GL_TRIANGLES = 0x0004;

        public const uint GL_VERTEX_SHADER = 0x8B31;
        public const uint GL_FRAGMENT_SHADER = 0x8B30;

        public const uint GL_LINES = 0x0001;
        
        public const uint GL_DEPTH_CLAMP = 0x864F;

        public const uint GL_CULL_FACE = 0x0B44;
        public const uint GL_BACK = 0x0405;
        public const uint GL_CCW = 0x0901;
        public const uint GL_CW = 0x0900;

        public const int GLFW_PRESS = 1;
        public const int GLFW_RELEASE = 0;

        // Added keys for object controls (W A S D)
        public const int GLFW_KEY_W = 87, GLFW_KEY_S = 83, GLFW_KEY_A = 65, GLFW_KEY_D = 68;
        
        public const int GLFW_KEY_I = 73;

        public const int GLFW_KEY_ESCAPE = 256;
        public const int GLFW_KEY_F1 = 290;
        public const int GLFW_KEY_P = 80;

        // Added keys for object controls (I J K L)
        public const int GLFW_KEY_J = 74;
        public const int GLFW_KEY_K = 75;
        public const int GLFW_KEY_L = 76;

        // Shift keys (used to boost movement speed)
        public const int GLFW_KEY_LEFT_SHIFT = 340;
        public const int GLFW_KEY_RIGHT_SHIFT = 344;

        // Speed multiplier while shift is held
        public const float SHIFT_SPEED_MULTIPLIER = 10.0f;
    }
}
