namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    public static class Const
    {
        // ======================
        // ENGINE INFO
        // ======================
        public const uint VERSION = 0x1F02;
        public const uint VENDOR = 0x1F00;
        public const uint RENDERER = 0x1F01;
        public const uint SHADING_LANGUAGE_VERSION = 0x8B8C;

        // ======================
        // OPENGL STATES
        // ======================
        public const uint GL_NONE = 0;
        public const uint GL_FLOAT = 0x1406;
        public const uint GL_INT = 0x1404;

        public const uint GL_FRONT_AND_BACK = 0x0408;
        public const uint GL_FRONT = 0x0404;
        public const uint GL_BACK = 0x0405;

        public const uint GL_LINE = 0x1B01;
        public const uint GL_FILL = 0x1B02;

        public const uint GL_DEPTH_TEST = 0x0B71;
        public const uint GL_CULL_FACE = 0x0B44;
        public const uint GL_CCW = 0x0901;
        public const uint GL_CW = 0x0900;

        public const uint GL_BLEND = 0x0BE2;
        public const uint GL_DEPTH_CLAMP = 0x864F;

        // ======================
        // BUFFERS
        // ======================
        public const uint GL_ARRAY_BUFFER = 0x8892;
        public const uint GL_ELEMENT_ARRAY_BUFFER = 0x8893;

        public const uint GL_STATIC_DRAW = 0x88E8;
        public const uint GL_DYNAMIC_DRAW = 0x88E4;

        // ======================
        // SHADERS
        // ======================
        public const uint GL_VERTEX_SHADER = 0x8B31;
        public const uint GL_FRAGMENT_SHADER = 0x8B30;

        public const uint GL_COMPILE_STATUS = 0x8B81;
        public const uint GL_LINK_STATUS = 0x8B82;
        public const uint GL_INFO_LOG_LENGTH = 0x8B84;

        // ======================
        // DRAW MODES
        // ======================
        public const uint GL_TRIANGLES = 0x0004;
        public const uint GL_TRIANGLE_STRIP = 0x0005;
        public const uint GL_LINES = 0x0001;

        // ======================
        // CLEAR MASKS
        // ======================
        public const uint GL_COLOR_BUFFER_BIT = 0x00004000;
        public const uint GL_DEPTH_BUFFER_BIT = 0x00000100;

        // ======================
        // TEXTURES
        // ======================
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
        public const uint GL_TEXTURE_BORDER_COLOR = 0x1004;

        public const uint GL_REPEAT = 0x2901;
        public const uint GL_CLAMP_TO_EDGE = 0x812F;
        public const uint GL_CLAMP_TO_BORDER = 0x812D;

        public const uint GL_LINEAR = 0x2601;
        public const uint GL_NEAREST = 0x2600;
        public const uint GL_LINEAR_MIPMAP_LINEAR = 0x2703;

        public const uint GL_RGB = 0x1907;
        public const uint GL_RGBA = 0x1908;
        public const uint GL_RED = 0x1903;
        public const uint GL_ALPHA = 0x1906;
        public const uint GL_SRC_ALPHA = 0x0302;
        public const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;

        public const uint GL_UNSIGNED_BYTE = 0x1401;
        public const uint GL_UNSIGNED_INT = 0x1405;

        public const uint GL_TEXTURE_MAX_ANISOTROPY = 0x84FE;
        public const int GL_POLYGON_OFFSET_FILL = 0x8037;
        // ======================
        // FRAMEBUFFER
        // ======================
        public const uint GL_FRAMEBUFFER = 0x8D40;
        public const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
        public const uint GL_DEPTH_ATTACHMENT = 0x8D00;

        public const uint GL_R8 = 0x8229;
        public const uint GL_RGB8 = 0x8051;
        public const uint GL_RGBA16F = 0x881A;
        public const uint GL_RGBA32F = 0x8814;
        public const uint GL_RGBA8 = 0x8058;

        public const uint GL_DEPTH_COMPONENT = 0x1902;
        public const uint GL_DEPTH_COMPONENT24 = 0x81A6;
        public const uint GL_DEPTH_COMPONENT32F = 0x8CAC;

        public const uint GL_DEPTH_STENCIL_ATTACHMENT = 0x821A;
        public const uint GL_DEPTH_STENCIL = 0x84F9;
        public const uint GL_DEPTH24_STENCIL8 = 0x88F0;
        public const uint GL_UNSIGNED_INT_24_8 = 0x84FA;

        // Shadow mapping
        public const uint GL_TEXTURE_COMPARE_MODE = 0x884C;
        public const uint GL_TEXTURE_COMPARE_FUNC = 0x884D;
        public const uint GL_COMPARE_REF_TO_TEXTURE = 0x900E;

        public const uint GL_RENDERBUFFER = 0x8D41;
        public const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;

        // ======================
        // DEPTH FUNCTIONS
        // ======================
        public const uint GL_NEVER = 0x0200;
        public const uint GL_LESS = 0x0201;
        public const uint GL_EQUAL = 0x0202;
        public const uint GL_LEQUAL = 0x0203;
        public const uint GL_GREATER = 0x0204;
        public const uint GL_NOTEQUAL = 0x0205;
        public const uint GL_GEQUAL = 0x0206;
        public const uint GL_ALWAYS = 0x0207;

        // ======================
        // GLFW KEYS
        // ======================
        public const int GLFW_PRESS = 1;
        public const int GLFW_RELEASE = 0;

        // Movement keys
        public const int GLFW_KEY_W = 87;
        public const int GLFW_KEY_S = 83;
        public const int GLFW_KEY_A = 65;
        public const int GLFW_KEY_D = 68;

        // Camera / misc keys
        public const int GLFW_KEY_Q = 81;
        public const int GLFW_KEY_E = 69;
        public const int GLFW_KEY_I = 73;
        public const int GLFW_KEY_J = 74;
        public const int GLFW_KEY_K = 75;
        public const int GLFW_KEY_L = 76;
        public const int GLFW_KEY_O = 79;
        public const int GLFW_KEY_G = 71;
        public const int GLFW_KEY_H = 72;

        public const int GLFW_KEY_ESCAPE = 256;
        public const int GLFW_KEY_F1 = 290;
        public const int GLFW_KEY_P = 80;
        public const int GLFW_KEY_T = 84;
        public const int GLFW_KEY_U = 85;

        // Number keys
        public const int GLFW_KEY_1 = 49;
        public const int GLFW_KEY_2 = 50;
        public const int GLFW_KEY_3 = 51;
        public const int GLFW_KEY_7 = 55;
        public const int GLFW_KEY_8 = 56;
        public const int GLFW_KEY_9 = 57;

        // Numpad
        public const int GLFW_KEY_KP_1 = 321;
        public const int GLFW_KEY_KP_2 = 322;
        public const int GLFW_KEY_KP_3 = 323;

        // Shift
        public const int GLFW_KEY_LEFT_SHIFT = 340;
        public const int GLFW_KEY_RIGHT_SHIFT = 344;

        public const float SHIFT_SPEED_MULTIPLIER = 1.5f;

        // Alt + Ctrl
        public const int GLFW_KEY_LEFT_CONTROL= 341;
        public const int GLFW_KEY_LEFT_ALT = 342;

        public const int GLFW_KEY_RIGHT_CONTROL = 345;
        public const int GLFW_KEY_RIGHT_ALT = 346;
        
        // Special
        public const int GLFW_KEY_COMMA = 44;
        public const int GLFW_KEY_PERIOD = 46;


        // Space
        public const int GLFW_KEY_SPACE = 32;

        // +/- keys
        public const int GLFW_KEY_EQUAL = 61;
        public const int GLFW_KEY_MINUS = 45;

        // Fog toggle
        public const int GLFW_KEY_F = 70;

        // Camera toggle
        public const int GLFW_KEY_V = 86;

        // ======================
        // MOUSE BUTTONS
        // ======================
        public const int GLFW_CURSOR = 0x00033001;
        public const int GLFW_CURSOR_NORMAL = 0x00034001;
        public const int GLFW_CURSOR_DISABLED = 0x00034003;


        public const int GLFW_MOUSE_BUTTON_LEFT = 0;
        public const int GLFW_MOUSE_BUTTON_RIGHT = 1;
        public const int GLFW_MOUSE_BUTTON_MIDDLE = 2;
    }
}
