namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    public static class Const
    {
        public const uint GL_FRONT_AND_BACK = 0x0408;
        public const uint GL_FRONT = 0x0404;
        public const uint GL_BACK = 0x0405;

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

        public const uint GL_STATIC_DRAW = 0x88E8;
        public const uint GL_FLOAT = 0x1406;
        public const uint GL_INT = 0x1404;

        public const uint GL_TRIANGLES = 0x0004;
        public const uint GL_BLEND = 0x0BE2;
        public const uint GL_TRIANGLE_STRIP = 0x0005;

        public const uint GL_VERTEX_SHADER = 0x8B31;
        public const uint GL_FRAGMENT_SHADER = 0x8B30;

        public const uint GL_LINES = 0x0001;

        public const uint GL_DEPTH_CLAMP = 0x864F;

        public const uint GL_CULL_FACE = 0x0B44; 
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
        public const int GLFW_KEY_O = 79;

        // Shift keys (used to boost movement speed)
        public const int GLFW_KEY_LEFT_SHIFT = 340;
        public const int GLFW_KEY_RIGHT_SHIFT = 344;

        // Speed multiplier while shift is held
        public const float SHIFT_SPEED_MULTIPLIER = 2.0f;

        public const int GLFW_KEY_EQUAL = 61;
        public const int GLFW_KEY_MINUS = 45;

        // ← TAMBAHKAN: Travel measurement key
        public const int GLFW_KEY_T = 84;

        public const uint GL_TEXTURE0 = 0x84C0;
        public const uint GL_TEXTURE1 = 0x84C1;
        public const uint GL_TEXTURE2 = 0x84C2;
        public const uint GL_TEXTURE3 = 0x84C3;
        public const uint GL_TEXTURE4 = 0x84C4;
        public const uint GL_TEXTURE5 = 0x84C5;
        public const uint GL_TEXTURE_2D = 0x0DE1;
        public const uint GL_TEXTURE_WRAP_S = 0x2802;
        public const uint GL_TEXTURE_WRAP_T = 0x2803;
        public const uint GL_TEXTURE_MIN_FILTER = 0x2801;
        public const uint GL_TEXTURE_MAG_FILTER = 0x2800;
        public const uint GL_REPEAT = 0x2901;
        public const uint GL_LINEAR = 0x2601;
        public const uint GL_LINEAR_MIPMAP_LINEAR = 0x2703;
        public const uint GL_RGB = 0x1907;
        public const uint GL_RGBA = 0x1908;
        public const uint GL_UNSIGNED_BYTE = 0x1401;
        public const uint GL_UNSIGNED_INT = 0x1405;
        public const uint GL_CLAMP_TO_EDGE = 0x812F;

        public const uint GL_TEXTURE_MAX_ANISOTROPY = 0x84FE;
        public const uint GL_SRC_ALPHA = 0x0302;
        public const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303; 

        public const uint GL_RED = 0x1903;
        public const uint GL_LUMINANCE = 0x1909;
        public const uint GL_ALPHA = 0x1906;
        public const uint GL_NEAREST = 0x2600;
        public const uint GL_UNPACK_ALIGNMENT = 0x0CF5;

        public const uint MIN_FILTER = 0x2801;
        public const uint MAG_FILTER = 0x2800;

        // --- TAMBAHKAN KODE TOMBOL KEYBOARD GLFW DI SINI ---
        public const int GLFW_KEY_1 = 49;
        public const int GLFW_KEY_2 = 50;
        public const int GLFW_KEY_3 = 51;

        // Animation control keys (idle / walk / run)
        public const int GLFW_KEY_7 = 55;
        public const int GLFW_KEY_8 = 56;
        public const int GLFW_KEY_9 = 57;

        public const int GLFW_KEY_KP_1 = 321;
        public const int GLFW_KEY_KP_2 = 322;
        public const int GLFW_KEY_KP_3 = 323;

        public const int GLFW_KEY_F = 70; // Tombol untuk toggle kabut

        public const int GL_POLYGON_OFFSET_FILL = 0x8037;
        public const int GL_ELEMENT_ARRAY_BUFFER = 0x8893;

        public const uint GL_FRAMEBUFFER = 0x8D40;
        public const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
        public const uint GL_DEPTH_ATTACHMENT = 0x8D00;
        public const uint GL_RGBA16F = 0x881A;
        public const uint GL_DEPTH_COMPONENT24 = 0x81A6;
        public const uint GL_DEPTH_COMPONENT = 0x1902;

        public const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;

        public const uint GL_RGBA8 = 0x8058; 
        public const uint GL_DEPTH_STENCIL_ATTACHMENT = 0x821A; 
        public const uint GL_DEPTH_STENCIL = 0x84F9;
        public const uint GL_DEPTH24_STENCIL8 = 0x88F0;
        public const uint GL_UNSIGNED_INT_24_8 = 0x84FA;
        public const uint GL_RENDERBUFFER = 0x8D41;
    }
}
