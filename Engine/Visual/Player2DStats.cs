using System.Collections.Generic;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>Named stat slots a UI Bar can bind to (PlayerInfoPanel shows them all).</summary>
    public static class PlayerStatNames
    {
        public const string Health = "Health";
        public const string Mana = "Mana";
        public const string Level = "Level";
        public const string Experience = "Experience";
        public const string Fitness = "Fitness";
        public const string None = "None";

        /// <summary>All binding targets in dropdown order.</summary>
        public static readonly string[] All =
            [None, Health, Mana, Level, Experience, Fitness];
    }

    /// <summary>
    /// Live player status values for the 2D sidescroller, shared between the IDE's
    /// Player Info panel and UI Bar elements. One static instance (single player):
    /// Player2DSystem keeps it fresh each frame while playing, the panel edits it in
    /// the editor, and UI Bars read Current/Max through <see cref="GetCurrent"/> /
    /// <see cref="GetMax"/> so "connect the bar to a stat" is one dropdown away.
    /// </summary>
    public static class Player2DStats
    {
        // ── Current values ──
        public static float Health = 100f;
        public static float Mana = 50f;
        public static float Level = 1f;
        public static float Experience = 0f;
        public static float Fitness = 100f;

        // ── Maxima (bars fill relative to these) ──
        public static float HealthMax = 100f;
        public static float ManaMax = 50f;
        public static float LevelMax = 99f;
        public static float ExperienceMax = 100f;
        public static float FitnessMax = 100f;

        /// <summary>True while a play session runs (Player2DSystem sets it) — lets the
        /// panel show a "playing" badge and lets runtime-only effects apply.</summary>
        public static bool SessionActive;

        /// <summary>Named access for UI Bar bindings: current value of a stat slot.</summary>
        public static float GetCurrent(string statName) => statName switch
        {
            PlayerStatNames.Health => Health,
            PlayerStatNames.Mana => Mana,
            PlayerStatNames.Level => Level,
            PlayerStatNames.Experience => Experience,
            PlayerStatNames.Fitness => Fitness,
            _ => 0f,
        };

        /// <summary>Named access for UI Bar bindings: max value of a stat slot.</summary>
        public static float GetMax(string statName) => statName switch
        {
            PlayerStatNames.Health => HealthMax,
            PlayerStatNames.Mana => ManaMax,
            PlayerStatNames.Level => LevelMax,
            PlayerStatNames.Experience => ExperienceMax,
            PlayerStatNames.Fitness => FitnessMax,
            _ => 1f,
        };

        /// <summary>Named write (used by the panel and future gameplay code).</summary>
        public static void SetCurrent(string statName, float value)
        {
            value = System.MathF.Max(0f, value);
            switch (statName)
            {
                case PlayerStatNames.Health: Health = value; break;
                case PlayerStatNames.Mana: Mana = value; break;
                case PlayerStatNames.Level: Level = value; break;
                case PlayerStatNames.Experience: Experience = value; break;
                case PlayerStatNames.Fitness: Fitness = value; break;
            }
        }

        /// <summary>Named write for the maxima.</summary>
        public static void SetMax(string statName, float value)
        {
            value = System.MathF.Max(1f, value);
            switch (statName)
            {
                case PlayerStatNames.Health: HealthMax = value; break;
                case PlayerStatNames.Mana: ManaMax = value; break;
                case PlayerStatNames.Level: LevelMax = value; break;
                case PlayerStatNames.Experience: ExperienceMax = value; break;
                case PlayerStatNames.Fitness: FitnessMax = value; break;
            }
        }

        /// <summary>Clamp every current value into [0, Max] — call after edits.</summary>
        public static void ClampAll()
        {
            Health = System.Math.Clamp(Health, 0f, HealthMax);
            Mana = System.Math.Clamp(Mana, 0f, ManaMax);
            Level = System.Math.Clamp(Level, 0f, LevelMax);
            Experience = System.Math.Clamp(Experience, 0f, ExperienceMax);
            Fitness = System.Math.Clamp(Fitness, 0f, FitnessMax);
        }

        /// <summary>Reset to defaults (new session / project open).</summary>
        public static void ResetToDefaults()
        {
            Health = HealthMax = 100f;
            Mana = ManaMax = 50f;
            Level = 1f;
            LevelMax = 99f;
            Experience = 0f;
            ExperienceMax = 100f;
            Fitness = FitnessMax = 100f;
            SessionActive = false;
        }

        /// <summary>Fraction [0..1] of a stat — exactly what a bound Bar renders.</summary>
        public static float GetFraction(string statName)
        {
            float max = GetMax(statName);
            return max > 0.001f ? System.Math.Clamp(GetCurrent(statName) / max, 0f, 1f) : 0f;
        }
    }
}
