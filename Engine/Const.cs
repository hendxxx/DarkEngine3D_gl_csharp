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

        // Texture-related
        public const uint GL_TEXTURE_2D          = 0x0DE1;
        public const uint GL_RGB                 = 0x1907;
        public const uint GL_RGBA                = 0x1908;
        public const uint GL_UNSIGNED_BYTE       = 0x1401;
        public const uint GL_TEXTURE_MIN_FILTER  = 0x2801;
        public const uint GL_TEXTURE_MAG_FILTER  = 0x2800;
        public const uint GL_TEXTURE_WRAP_S      = 0x2802;
        public const uint GL_TEXTURE_WRAP_T      = 0x2803;
        public const uint GL_LINEAR              = 0x2601;
        public const uint GL_NEAREST             = 0x2600;
        public const uint GL_LINEAR_MIPMAP_LINEAR = 0x2703;
        public const uint GL_REPEAT              = 0x2901;
        public const uint GL_CLAMP_TO_EDGE       = 0x812F;
        public const uint GL_TEXTURE0            = 0x84C0;
        public const uint GL_BLEND               = 0x0BE2;
        public const uint GL_SRC_ALPHA           = 0x0302;
        public const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
        
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

        // Mouse buttons
        public const int GLFW_MOUSE_BUTTON_LEFT  = 0;
        public const int GLFW_MOUSE_BUTTON_RIGHT = 1;

        public const int GLFW_KEY_ESCAPE = 256;
        public const int GLFW_KEY_F1 = 290;
        public const int GLFW_KEY_F2 = 291;
        public const int GLFW_KEY_P = 80;
        public const int GLFW_KEY_SPACE = 32;
        public const int GLFW_KEY_C = 67;

        // Number row 1..6 — quick-slot shortcuts
        public const int GLFW_KEY_1 = 49;
        public const int GLFW_KEY_2 = 50;
        public const int GLFW_KEY_3 = 51;
        public const int GLFW_KEY_4 = 52;
        public const int GLFW_KEY_5 = 53;
        public const int GLFW_KEY_6 = 54;

        // Added keys for object controls (I J K L)
        public const int GLFW_KEY_J = 74;
        public const int GLFW_KEY_K = 75;
        public const int GLFW_KEY_L = 76;

        // Shift keys (used to boost movement speed)
        public const int GLFW_KEY_LEFT_SHIFT = 340;
        public const int GLFW_KEY_RIGHT_SHIFT = 344;

        // Speed multiplier while shift is held
        public const float SHIFT_SPEED_MULTIPLIER = 3.0f;

        // Player eye height above terrain in walk mode
        public const float PLAYER_EYE_HEIGHT = 2.0f;
        public const float PLAYER_CROUCH_HEIGHT = 1.0f;
        public const float PLAYER_JUMP_SPEED = 15.0f; // initial upward velocity on jump
        public const float PLAYER_GRAVITY = 40.0f;    // world-units / sec^2

        // Stamina tunables (units per second)
        public const float STAMINA_MAX = 100.0f;
        public const float STAMINA_DRAIN_WALK = 3.5f;
        public const float STAMINA_DRAIN_RUN = 10.0f;
        public const float STAMINA_REGEN = 15.0f;
        public const float STAMINA_LOW_THRESHOLD_PCT = 30.0f; // below this %, movement crawls
        public const float STAMINA_LOW_SPEED_FACTOR = 0.15f;  // very slow when tired
        public const float STAMINA_JUMP_COST = 10.0f;         // flat cost per jump

        // Player attacks
        public const float STONE_SPEED         = 25.0f;   // launch velocity along camera forward
        public const float STONE_LAUNCH_UP     = 3.0f;    // small upward arc on throw
        public const float STONE_GRAVITY       = 30.0f;
        public const float STONE_RADIUS        = 0.25f;   // collision sphere
        public const float STONE_DAMAGE        = 18.0f;
        public const float STONE_LIFETIME      = 5.0f;
        public const float THROW_COOLDOWN      = 0.55f;
        public const float THROW_ANIM_DURATION = 0.4f;
        public const float PUNCH_RANGE         = 2.5f;
        public const float PUNCH_DAMAGE        = 14.0f;
        public const float PUNCH_CONE_DOT      = 0.5f;    // cos(60°): hits anything within ±60° of view
        public const float PUNCH_COOLDOWN      = 0.4f;
        public const float PUNCH_ANIM_DURATION = 0.3f;

        // Enemy
        public const float ENEMY_MAX_HP        = 30.0f;   // smaller than player's 100
        public const float ENEMY_DEATH_DURATION = 0.8f;   // fall-over animation length
    }
}
