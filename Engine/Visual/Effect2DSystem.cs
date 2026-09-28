using System.Numerics;
using System.Collections.Generic;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Lightweight 2D particle/effect system for the sidescroller (preview + in-game).
/// ONE static particle pool rendered as world-space textured quads through a tiny
/// dedicated shader (same vertex layout as the Map2D/portal passes: pos3+uv2+tint4).
///
/// Features:
///  • Presets: Explosion, Fire, Fireball, Sparks (shield hits), Coin, Dust, Smoke, Snow
///  • Global RAIN with wind slant + ground splashes (Trigger action "Rain")
///  • Global WIND advecting particles (Trigger action "Set Wind", per-emitter factor)
///  • Continuous emitters (Effect2D editor objects) + one-shot bursts (Spawn Effect)
/// All particles are procedural textures generated at startup (dot/glow/spark/drop/
/// coin/square) — no extra assets required. Sprite-sheet art FX remain Sprite2D objects.
/// </summary>
public static class Effect2DSystem
{
    // ═══════════════════════ Global weather state ═══════════════════════

    /// <summary>Global horizontal wind in world units/second. Positive = blowing right.
    /// Particles add wind × WindFactor to their velocity each frame (advection).</summary>
    public static float WindX;

    /// <summary>Global rain on/off (Trigger action "Rain"). Rain spawns across the
    /// camera view, falls with wind slant, and splashes on the map's collision ground.</summary>
    public static bool RainEnabled;

    /// <summary>Rain density 0..1 (drops per second scale).</summary>
    public static float RainIntensity = 0.7f;

    /// <summary>How much the global wind slants the rain (0 = vertical rain).</summary>
    public static float RainWindFactor = 1f;

    // ═══════════════════════ Emitter config ═══════════════════════

    /// <summary>Full emission description for one continuous emitter (an Effect2D
    /// editor object) or one burst group. Fields left at defaults come from the preset.</summary>
    public sealed class EmitConfig
    {
        public string Preset = "Fire";
        /// <summary>Particles per second (continuous emitters).</summary>
        public float Rate = 25f;
        /// <summary>Accumulator for sub-1 rates (internal).</summary>
        public float Acc;
        public Vector3 Pos;
        public float LifeMin = 0.5f, LifeMax = 0.9f;
        public float SpeedMin = 0.5f, SpeedMax = 1.5f;
        /// <summary>Emission direction (radians, 0 = right). Spread = ± half-angle.</summary>
        public float Angle = -MathF.PI * 0.5f; // default: up
        public float Spread = 0.5f;
        public float SizeMin = 0.12f, SizeMax = 0.22f;
        public float Grow;                    // size delta per second
        public float Gravity;
        public float Drag;
        public Vector4 ColorStart = new(1f, 0.8f, 0.3f, 1f);
        public Vector4 ColorEnd = new(1f, 0.3f, 0.05f, 0f);
        /// <summary>0..1 — how strongly the global wind advects this emitter's particles.</summary>
        public float WindFactor = 0.2f;
        public int Tex = TexDot;
        public bool AlignVelocity;           // rotate the quad along the velocity (streaks)
        public float SpinPhase;              // coin spin speed (radians/s of the width oscillation)
        public float AlignVelScale = 3f;     // streak length = size × this
        public bool CoinSpin;                // spinning-coin width modulation
        public float SpinSpeed = 6f;
        public float Z = 0.075f;             // world depth (in front of portals/player)
        public float GroundKill;             // >0: kill particles crossing ground + splash
        public int Burst;                    // >0: one-shot count (Spawn Effect)
    }

    // Procedural texture slots
    public const int TexDot = 0;     // soft filled circle
    public const int TexGlow = 1;    // gaussian-ish blob (explosions/fire)
    public const int TexSpark = 2;   // bright core + horizontal streak
    public const int TexDrop = 3;    // vertical rain streak
    public const int TexCoin = 4;    // gold coin (own colors, tint white)
    public const int TexSquare = 5;  // hard-edged pixel square (debris)

    // ═══════════════════════ Particle pool ═══════════════════════

    private struct P
    {
        public float X, Y, Z, VX, VY;
        public float Life, MaxLife;
        public float Size, SizeY, Grow;
        public float Rot, Spin;
        public float Phase, SpinPhase; // coin spin
        public int Tex;
        public float Wind;
        public Vector4 C0, C1;
        public float Gravity, Drag;
        public bool AlignVel;
        public float AlignVelScale;
        public bool Coin;
        public bool Splash;            // raindrop → spawn splash on ground hit
        public float GroundKill;
    }

    private static readonly List<P> _particles = new();
    public const int MaxParticles = 4000;

    // Frame context set by Tick, used by spawn helpers.
    private static float _dt;
    private static Camera? _cam;
    private static Tilemap2D? _map;
    private static readonly System.Random _rng = new();

    // ═══════════════════════ Public API ═══════════════════════

    /// <summary>Per-frame update: collect Effect2D emitters, emit, rain, integrate.
    /// Called from Player2DSystem.Update (preview/in-game only).</summary>
    public static void Tick(EditorObjectManager? mgr, Tilemap2D? map, Camera? cam, float dt)
    {
        _dt = dt; _cam = cam; _map = map;
        if (dt <= 0f) return;

        // 1) Continuous emitters from visible Effect2D objects. The accumulator lives
        // ON the object (configs are rebuilt every frame — a config-local Acc would
        // reset each frame and sub-1 rates would never emit).
        if (mgr != null)
        {
            foreach (var o in mgr.Objects)
            {
                if (o is not { IsVisible: true, PrimitiveType: EditorPrimitiveType.Effect2D }) continue;
                if (!o.Effect2DEnabled) continue;
                var cfg = ConfigFromObject(o);
                o.Effect2DEmitAcc += cfg.Rate * dt;
                while (o.Effect2DEmitAcc >= 1f) { o.Effect2DEmitAcc -= 1f; Spawn(cfg); }
            }
        }

        // 2) Global rain across the camera view.
        if (RainEnabled && cam != null) SpawnRain(dt);

        // 3) Integrate + cull.
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            bool dead = p.Life <= 0f;

            if (!dead)
            {
                p.VX += WindX * p.Wind * dt;
                p.VY -= p.Gravity * dt;
                if (p.Drag > 0f) { float d = MathF.Max(0f, 1f - p.Drag * dt); p.VX *= d; p.VY *= d; }
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                p.Rot += p.Spin * dt;
                p.Phase += p.SpinPhase * dt;
                p.Size = MathF.Max(0.01f, p.Size + p.Grow * dt);
                if (p.Splash && p.VY < 0f)
                {
                    float gy = GroundYAt(map, p.X, p.Y);
                    if (!float.IsNegativeInfinity(gy) && p.Y <= gy + 0.02f)
                    {
                        SpawnSplash(p.X, gy);
                        dead = true;
                    }
                }
                if (p.Y < -80f || p.X < -500f || p.X > 5000f) dead = true; // safety cull
            }

            if (dead)
            {
                _particles[i] = _particles[^1];
                _particles.RemoveAt(_particles.Count - 1);
            }
            else
                _particles[i] = p;
        }
    }

    /// <summary>Resolve an Effect2D object's emitter config (preset defaults + overrides).
    /// WindFactor sentinel: -1 = use the preset default (JSON cannot carry NaN).</summary>
    public static EmitConfig ConfigFromObject(EditorObject o)
    {
        var cfg = PresetConfig(o.Effect2DPreset);
        cfg.Pos = new Vector3(o.Position.X, o.Position.Y + o.Effect2DOffsetY, o.Position.Z);
        if (o.Effect2DEmitRate >= 0f) cfg.Rate = o.Effect2DEmitRate;
        if (o.Effect2DWindFactor >= 0f) cfg.WindFactor = o.Effect2DWindFactor;
        return cfg;
    }

    /// <summary>One-shot burst at a world position (Spawn Effect trigger action).
    /// Compound presets (Explosion/Coin) spawn several complementary groups.</summary>
    public static void SpawnBurst(string preset, Vector3 pos, float scale = 1f)
    {
        switch (preset)
        {
            case "Explosion":
            {
                // Flash core
                var core = PresetConfig("ExplosionCore");
                core.Pos = pos; core.Burst = (int)(14 * scale);
                for (int i = 0; i < core.Burst; i++) Spawn(core);
                // Debris squares with gravity + spin
                var debris = PresetConfig("ExplosionDebris");
                debris.Pos = pos; debris.Burst = (int)(16 * scale);
                for (int i = 0; i < debris.Burst; i++) Spawn(debris);
                // Radial sparks (streaks)
                var sparks = PresetConfig("Sparks");
                sparks.Pos = pos; sparks.Burst = (int)(12 * scale);
                for (int i = 0; i < sparks.Burst; i++) Spawn(sparks);
                break;
            }
            case "Coin":
            {
                var c = PresetConfig("Coin");
                c.Pos = pos; c.Burst = Math.Max(3, (int)(6 * scale));
                for (int i = 0; i < c.Burst; i++) Spawn(c);
                break;
            }
            default:
            {
                var g = PresetConfig(preset);
                g.Pos = pos; g.Burst = Math.Max(6, (int)(18 * scale));
                for (int i = 0; i < g.Burst; i++) Spawn(g);
                break;
            }
        }
    }

    /// <summary>Preset defaults by name (also the Effect2D inspector dropdown order).</summary>
    public static readonly string[] Presets =
        ["Fire", "Fireball", "Explosion", "Sparks", "Coin", "Dust", "Smoke", "Snow"];

    public static EmitConfig PresetConfig(string preset) => preset switch
    {
        // Torch-style fire: rising orange glow shrinking out.
        "Fire" => new EmitConfig
        {
            Preset = preset, Rate = 30f, Tex = TexGlow,
            LifeMin = 0.35f, LifeMax = 0.7f, SpeedMin = 0.6f, SpeedMax = 1.6f,
            Angle = -MathF.PI * 0.5f, Spread = 0.45f,
            SizeMin = 0.14f, SizeMax = 0.3f, Grow = -0.12f,
            ColorStart = new Vector4(1f, 0.75f, 0.25f, 0.95f),
            ColorEnd = new Vector4(0.9f, 0.15f, 0.02f, 0f),
            WindFactor = 0.35f, Z = 0.075f,
        },
        // Fireball trail: dense hot core + fast fade (attach to a moving projectile).
        "Fireball" => new EmitConfig
        {
            Preset = preset, Rate = 60f, Tex = TexGlow,
            LifeMin = 0.2f, LifeMax = 0.45f, SpeedMin = 0.1f, SpeedMax = 0.6f,
            Angle = -MathF.PI * 0.5f, Spread = MathF.PI,
            SizeMin = 0.16f, SizeMax = 0.34f, Grow = -0.3f,
            ColorStart = new Vector4(1f, 0.85f, 0.4f, 1f),
            ColorEnd = new Vector4(1f, 0.25f, 0.02f, 0f),
            WindFactor = 0.1f, Z = 0.075f,
        },
        // Explosion core flash (burst part 1).
        "ExplosionCore" => new EmitConfig
        {
            Preset = preset, Tex = TexGlow,
            LifeMin = 0.15f, LifeMax = 0.35f, SpeedMin = 1.5f, SpeedMax = 4.5f,
            Angle = 0f, Spread = MathF.PI,
            SizeMin = 0.25f, SizeMax = 0.55f, Grow = -0.4f,
            ColorStart = new Vector4(1f, 0.9f, 0.5f, 1f),
            ColorEnd = new Vector4(1f, 0.3f, 0.05f, 0f),
            WindFactor = 0f, Z = 0.08f,
        },
        // Explosion debris (burst part 2): squares with gravity + spin.
        "ExplosionDebris" => new EmitConfig
        {
            Preset = preset, Tex = TexSquare,
            LifeMin = 0.4f, LifeMax = 0.9f, SpeedMin = 2f, SpeedMax = 6f,
            Angle = -MathF.PI * 0.5f, Spread = MathF.PI,
            SizeMin = 0.06f, SizeMax = 0.16f,
            Gravity = 9f, SpinSpeed = 14f,
            ColorStart = new Vector4(0.9f, 0.55f, 0.15f, 1f),
            ColorEnd = new Vector4(0.35f, 0.2f, 0.12f, 0f),
            WindFactor = 0.2f, Z = 0.08f,
        },
        // Shield-hit sparks: bright radial streaks, gravity, quick fade.
        "Sparks" => new EmitConfig
        {
            Preset = preset, Tex = TexSpark, AlignVelocity = true, AlignVelScale = 3.5f,
            LifeMin = 0.2f, LifeMax = 0.5f, SpeedMin = 3f, SpeedMax = 8f,
            Angle = 0f, Spread = MathF.PI,
            SizeMin = 0.05f, SizeMax = 0.1f,
            Gravity = 12f,
            ColorStart = new Vector4(1f, 0.95f, 0.55f, 1f),
            ColorEnd = new Vector4(1f, 0.5f, 0.1f, 0f),
            WindFactor = 0.1f, Z = 0.08f,
        },
        // Coin: spinning gold disks with gravity (drop/pickup bursts).
        "Coin" => new EmitConfig
        {
            Preset = preset, Tex = TexCoin, CoinSpin = true, SpinPhase = 9f,
            LifeMin = 0.7f, LifeMax = 1.1f, SpeedMin = 2f, SpeedMax = 4.5f,
            Angle = -MathF.PI * 0.5f, Spread = 0.9f,
            SizeMin = 0.22f, SizeMax = 0.32f,
            Gravity = 8f,
            ColorStart = new Vector4(1f, 1f, 1f, 1f),
            ColorEnd = new Vector4(1f, 1f, 1f, 0f),
            WindFactor = 0.05f, Z = 0.08f,
        },
        // Dust: soft grey motes drifting with the wind (landing/footsteps).
        "Dust" => new EmitConfig
        {
            Preset = preset, Tex = TexDot, Rate = 10f,
            LifeMin = 0.5f, LifeMax = 1.2f, SpeedMin = 0.2f, SpeedMax = 0.9f,
            Angle = 0f, Spread = MathF.PI,
            SizeMin = 0.06f, SizeMax = 0.14f,
            ColorStart = new Vector4(0.8f, 0.78f, 0.7f, 0.7f),
            ColorEnd = new Vector4(0.6f, 0.58f, 0.52f, 0f),
            WindFactor = 0.8f, Z = 0.07f,
        },
        // Smoke: dark puffs rising, growing, fading.
        "Smoke" => new EmitConfig
        {
            Preset = preset, Tex = TexGlow, Rate = 14f,
            LifeMin = 0.8f, LifeMax = 1.6f, SpeedMin = 0.4f, SpeedMax = 1.1f,
            Angle = -MathF.PI * 0.5f, Spread = 0.5f,
            SizeMin = 0.2f, SizeMax = 0.4f, Grow = 0.35f,
            ColorStart = new Vector4(0.35f, 0.35f, 0.38f, 0.55f),
            ColorEnd = new Vector4(0.2f, 0.2f, 0.22f, 0f),
            WindFactor = 0.6f, Z = 0.07f,
        },
        // Snow: slow white flakes, strongly wind-driven.
        "Snow" => new EmitConfig
        {
            Preset = preset, Tex = TexDot, Rate = 25f,
            LifeMin = 2.5f, LifeMax = 4f, SpeedMin = 0.8f, SpeedMax = 1.6f,
            Angle = -MathF.PI * 0.5f, Spread = 0.35f,
            SizeMin = 0.05f, SizeMax = 0.11f,
            ColorStart = new Vector4(1f, 1f, 1f, 0.9f),
            ColorEnd = new Vector4(1f, 1f, 1f, 0f),
            WindFactor = 1.2f, Z = 0.075f,
        },
        _ => PresetConfig("Fire"),
    };

    /// <summary>Spawn one particle from a config (randomized within its ranges).</summary>
    public static void Spawn(EmitConfig c)
    {
        if (_particles.Count >= MaxParticles) return;
        float ang = c.Angle + ((float)_rng.NextDouble() * 2f - 1f) * c.Spread;
        float spd = Lerp(c.SpeedMin, c.SpeedMax);
        float life = Lerp(c.LifeMin, c.LifeMax);
        _particles.Add(new P
        {
            X = c.Pos.X + (float)(_rng.NextDouble() * 2 - 1) * 0.05f,
            Y = c.Pos.Y + (float)(_rng.NextDouble() * 2 - 1) * 0.05f,
            Z = c.Z,
            VX = MathF.Cos(ang) * spd,
            VY = MathF.Sin(ang) * spd,
            Life = life, MaxLife = life,
            Size = Lerp(c.SizeMin, c.SizeMax), SizeY = 0f, Grow = c.Grow,
            Rot = c.AlignVelocity ? 0f : (float)(_rng.NextDouble() * MathF.Tau),
            Spin = (float)(_rng.NextDouble() * 2 - 1) * c.SpinSpeed,
            Phase = 0f,
            SpinPhase = c.CoinSpin
                ? (c.SpinPhase > 0f ? c.SpinPhase : c.SpinSpeed) * (0.7f + (float)_rng.NextDouble() * 0.6f)
                : 0f,
            Tex = c.Tex, Wind = c.WindFactor,
            C0 = c.ColorStart, C1 = c.ColorEnd,
            Gravity = c.Gravity, Drag = c.Drag,
            AlignVel = c.AlignVelocity, AlignVelScale = c.AlignVelScale,
            Coin = c.CoinSpin,
        });
    }

    /// <summary>Global rain: spawn drops across the camera view + integrate splashes.</summary>
    private static void SpawnRain(float dt)
    {
        if (_cam == null) return;
        float halfH = MathF.Max(0.5f, _cam.OrthoSize);
        float halfW = halfH * _cam.GetAspect();
        float cx = _cam.Position.X, cy = _cam.Position.Y;

        int count = (int)(RainIntensity * 220f * dt);
        for (int i = 0; i < count; i++)
        {
            if (_particles.Count >= MaxParticles) return;
            float x = cx + ((float)_rng.NextDouble() * 2f - 1f) * (halfW + 2f);
            float y = cy + halfH + 0.5f + (float)_rng.NextDouble() * 2f;
            float vy = -(15f + (float)_rng.NextDouble() * 6f);
            float vx = WindX * RainWindFactor * 1.2f + ((float)_rng.NextDouble() - 0.5f) * 0.5f;
            float life = 3f;
            _particles.Add(new P
            {
                X = x, Y = y, Z = 0.075f,
                VX = vx, VY = vy,
                Life = life, MaxLife = life,
                Size = 0.045f, SizeY = 0.42f,
                Rot = MathF.Atan2(vy, vx),
                Tex = TexDrop, Wind = RainWindFactor * 1.2f,
                C0 = new Vector4(0.65f, 0.78f, 0.95f, 0.55f),
                C1 = new Vector4(0.65f, 0.78f, 0.95f, 0.55f),
                AlignVel = false, Splash = true, GroundKill = 1f,
            });
        }
    }

    /// <summary>Tiny splash dots kicked up where a raindrop hits the ground.</summary>
    private static void SpawnSplash(float x, float groundY)
    {
        for (int i = 0; i < 2; i++)
        {
            if (_particles.Count >= MaxParticles) return;
            float vx = ((float)_rng.NextDouble() * 2f - 1f) * 1.2f + WindX * 0.3f;
            float vy = 1.2f + (float)_rng.NextDouble() * 1.4f;
            float life = 0.2f + (float)_rng.NextDouble() * 0.15f;
            _particles.Add(new P
            {
                X = x, Y = groundY + 0.03f, Z = 0.075f,
                VX = vx, VY = vy,
                Life = life, MaxLife = life,
                Size = 0.04f, Grow = -0.05f,
                Gravity = 9f,
                Tex = TexDot, Wind = 0.3f,
                C0 = new Vector4(0.75f, 0.85f, 1f, 0.8f),
                C1 = new Vector4(0.75f, 0.85f, 1f, 0f),
            });
        }
    }

    /// <summary>World Y of the first collision tile below (x, yStart) on any visible
    /// layer — the rain's splash plane. Negative infinity when the map/column is empty.</summary>
    private static float GroundYAt(Tilemap2D? map, float x, float yStart)
    {
        if (map == null) return float.NegativeInfinity;
        float cell = map.TileSize * Tilemap2D.WorldScale;
        if (cell <= 0f || map.Width <= 0 || map.Height <= 0) return float.NegativeInfinity;
        int col = (int)MathF.Floor(x / cell);
        if (col < 0 || col >= map.Width) return float.NegativeInfinity;
        float topWorld = map.Height * cell;
        int rowStart = (int)MathF.Floor((topWorld - yStart) / cell);
        rowStart = Math.Clamp(rowStart, 0, map.Height - 1);
        for (int row = rowStart; row < map.Height; row++)
        {
            for (int li = 0; li < map.Layers.Count; li++)
            {
                var layer = map.Layers[li];
                if (layer == null || !layer.IsVisible || layer.CollisionTileIds == null || layer.CollisionTileIds.Count == 0)
                    continue;
                int tile = map.GetTile(li, col, row);
                if (tile > 0 && layer.CollisionTileIds.Contains(tile))
                    return topWorld - row * cell; // top edge of the solid tile
            }
        }
        return float.NegativeInfinity;
    }

    /// <summary>Alive particle count (debug/perf readout).</summary>
    public static int ParticleCount => _particles.Count;

    /// <summary>Remove every live particle (session start / project close). Weather
    /// flags are NOT touched here — use ResetWeather for that.</summary>
    public static void Clear() => _particles.Clear();

    /// <summary>Clear particles + turn rain/wind off (project close / session end).</summary>
    public static void ResetWeather()
    {
        Clear();
        RainEnabled = false;
        WindX = 0f;
    }

    // ═══════════════════════ Rendering ═══════════════════════

    private static uint _shader, _vao, _vbo;
    private static int _locView, _locProj, _locModel, _locTex;
    private static readonly uint[] _texIds = new uint[6];
    private static bool _glReady;

    private static readonly float[] _vertBuf = new float[MaxParticles * 6 * 9];

    /// <summary>Quad corner template (unit half-extents): BL, BR, TR, BL, TR, TL —
    /// shared by every particle so no per-iteration allocation is needed.</summary>
    private static readonly (float lx, float ly, float u, float v)[] Corners =
    [
        (-1f, -1f, 0f, 0f), (1f, -1f, 1f, 0f), (1f, 1f, 1f, 1f),
        (-1f, -1f, 0f, 0f), (1f, 1f, 1f, 1f), (-1f, 1f, 0f, 1f),
    ];

    /// <summary>Render all live particles as world-space quads. Called once per frame
    /// from the EditorObjectManager 2D pass (after Sprite2D, in front of portals).</summary>
    public static unsafe void Render(Camera camera)
    {
        if (_particles.Count == 0) return;
        EnsureResources();
        if (_shader == 0) return;

        // Build + sort by texture so each procedural atlas binds once per run.
        _particles.Sort((a, b) => a.Tex.CompareTo(b.Tex));
        int vertFloats = 0;
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            float t = 1f - (p.Life / p.MaxLife); // 0 fresh → 1 dead
            var col = new Vector4(
                Lerp(p.C0.X, p.C1.X, t),
                Lerp(p.C0.Y, p.C1.Y, t),
                Lerp(p.C0.Z, p.C1.Z, t),
                Lerp(p.C0.W, p.C1.W, t));

            float w = p.Size, h = p.SizeY > 0f ? p.SizeY : p.Size;
            float rot = p.AlignVel ? MathF.Atan2(p.VY, p.VX) : p.Rot;
            if (p.Coin)
            {
                // Spinning coin: width oscillates; the edge frame stays readable.
                float c = MathF.Abs(MathF.Cos(p.Phase));
                w = MathF.Max(0.03f, p.Size * c);
            }
            float cos = MathF.Cos(rot), sin = MathF.Sin(rot);
            float hx = w * 0.5f, hy = h * 0.5f;

            // Corner template (BL, BR, TR) + (BL, TR, TL) — same winding as the portal
            // pass. UNIT positions scaled by the per-particle half extents below.
            // Hoisted out of the loop (CA2014: a stackalloc inside a loop that runs
            // thousands of iterations per frame can overflow the stack).
            for (int vI = 0; vI < Corners.Length; vI++)
            {
                var (ux, uy, u, v) = Corners[vI];
                float lx = ux * hx, ly = uy * hy;
                _vertBuf[vertFloats++] = p.X + lx * cos - ly * sin;
                _vertBuf[vertFloats++] = p.Y + lx * sin + ly * cos;
                _vertBuf[vertFloats++] = p.Z;
                _vertBuf[vertFloats++] = u;
                _vertBuf[vertFloats++] = v;
                _vertBuf[vertFloats++] = col.X;
                _vertBuf[vertFloats++] = col.Y;
                _vertBuf[vertFloats++] = col.Z;
                _vertBuf[vertFloats++] = col.W;
            }
        }

        GL.UseProgram(_shader);
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();
        GL.UniformMatrix4fv(_locView, 1, false, &view.M11);
        GL.UniformMatrix4fv(_locProj, 1, false, &proj.M11);
        var identity = Matrix4x4.Identity;
        GL.UniformMatrix4fv(_locModel, 1, false, &identity.M11);
        GL.Uniform1i(_locTex, 0);

        GL.Enable(Const.GL_BLEND);
        GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
        bool cull = GL.IsEnabled(Const.GL_CULL_FACE);
        GL.Disable(Const.GL_CULL_FACE);
        bool depth = GL.IsEnabled(Const.GL_DEPTH_TEST);

        GL.BindVertexArray(_vao);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
        unsafe
        {
            fixed (float* ptr = _vertBuf)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(vertFloats * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);
        }

        // Draw runs grouped by texture.
        int quadCount = _particles.Count;
        int runStart = 0;
        for (int i = 1; i <= quadCount; i++)
        {
            bool boundary = i == quadCount || _particles[i].Tex != _particles[runStart].Tex;
            if (!boundary) continue;
            int tex = _particles[runStart].Tex;
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[tex]);
            GL.DrawArrays(Const.GL_TRIANGLES, runStart * 6, (i - runStart) * 6);
            runStart = i;
        }

        GL.BindVertexArray(0);
        if (cull) GL.Enable(Const.GL_CULL_FACE);
        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
    }

    // ── Shared batch API (inventory equipped-item icons reuse this pass) ──

    private static readonly List<float> _batch = new();
    private static Camera? _batchCam;
    private static readonly List<(uint tex, int startQuad, int quads)> _batchRuns = new();

    /// <summary>Begin a batched quad submission (world-space, same shader/pipeline).
    /// Push any number of quads with PushQuad, then flush with EndBatch.</summary>
    public static void BeginBatch(Camera camera)
    {
        EnsureResources();
        _batch.Clear();
        _batchRuns.Clear();
        _batchCam = camera;
    }

    /// <summary>Queue one axis-aligned textured quad (world space, CCW from BL).</summary>
    public static void PushQuad(uint texId, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint, float z = 0.075f)
    {
        if (_batchCam == null) return;
        int startQuad = _batch.Count / 54; // 6 verts × 9 floats
        float x0 = x, x1 = x + w, y0 = y, y1 = y + h;
        void V(float px, float py, float u, float v)
        {
            _batch.Add(px); _batch.Add(py); _batch.Add(z);
            _batch.Add(u); _batch.Add(v);
            _batch.Add(tint.X); _batch.Add(tint.Y); _batch.Add(tint.Z); _batch.Add(tint.W);
        }
        V(x0, y0, u0, v0); V(x1, y0, u1, v0); V(x1, y1, u1, v1);
        V(x0, y0, u0, v0); V(x1, y1, u1, v1); V(x0, y1, u0, v1);
        // Merge with the previous run when the texture matches.
        if (_batchRuns.Count > 0 && _batchRuns[^1].tex == texId)
            _batchRuns[^1] = (texId, _batchRuns[^1].startQuad, _batchRuns[^1].quads + 1);
        else
            _batchRuns.Add((texId, startQuad, 1));
    }

    /// <summary>Flush the batch started by BeginBatch.</summary>
    public static unsafe void EndBatch()
    {
        if (_batchCam == null || _batch.Count == 0) { _batchCam = null; return; }
        EnsureResources();
        if (_shader == 0) { _batchCam = null; return; }
        var cam = _batchCam;

        GL.UseProgram(_shader);
        var view = cam.GetViewMatrix();
        var proj = cam.GetProjectionMatrix();
        GL.UniformMatrix4fv(_locView, 1, false, &view.M11);
        GL.UniformMatrix4fv(_locProj, 1, false, &proj.M11);
        var identity = Matrix4x4.Identity;
        GL.UniformMatrix4fv(_locModel, 1, false, &identity.M11);
        GL.Uniform1i(_locTex, 0);

        GL.Enable(Const.GL_BLEND);
        GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
        bool cull = GL.IsEnabled(Const.GL_CULL_FACE);
        GL.Disable(Const.GL_CULL_FACE);
        bool depth = GL.IsEnabled(Const.GL_DEPTH_TEST);

        GL.BindVertexArray(_vao);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
        unsafe
        {
            var arr = _batch.ToArray();
            fixed (float* ptr = arr)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(arr.Length * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);
            foreach (var (tex, startQuad, quads) in _batchRuns)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, tex);
                GL.DrawArrays(Const.GL_TRIANGLES, startQuad * 6, quads * 6);
            }
        }
        GL.BindVertexArray(0);
        if (cull) GL.Enable(Const.GL_CULL_FACE);
        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
        _batchCam = null;
    }

    /// <summary>GPU texture id for one of the procedural particle textures.</summary>
    public static uint GetProceduralTexture(int index) =>
        _glReady && index >= 0 && index < _texIds.Length ? _texIds[index] : 0u;

    // ═══════════════════════ GPU resources ═══════════════════════

    private static void EnsureResources()
    {
        if (_glReady) return;
        _glReady = true;

        // ── Shader (same vertex layout as Map2D/portal: pos3 + uv2 + tint4) ──
        string vertSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUV;
layout(location=2) in vec4 aTint;
uniform mat4 view;
uniform mat4 projection;
uniform mat4 model;
out vec2 vUV;
out vec4 vTint;
void main() {
    gl_Position = projection * view * model * vec4(aPos, 1.0);
    vUV = aUV;
    vTint = aTint;
}";
        string fragSrc = @"#version 330 core
in vec2 vUV;
in vec4 vTint;
uniform sampler2D tex;
out vec4 FragColor;
void main() {
    vec4 c = texture(tex, vUV) * vTint;
    if (c.a < 0.01) discard;
    FragColor = c;
}";
        unsafe
        {
            uint vert = GL.CreateShader(Const.GL_VERTEX_SHADER);
            GL.ShaderSource(vert, vertSrc);
            GL.CompileShader(vert);
            uint frag = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
            GL.ShaderSource(frag, fragSrc);
            GL.CompileShader(frag);
            _shader = GL.CreateProgram();
            GL.AttachShader(_shader, vert);
            GL.AttachShader(_shader, frag);
            GL.LinkProgram(_shader);
            GL.DeleteShader(vert);
            GL.DeleteShader(frag);
            _locView = GL.GetUniformLocation(_shader, "view");
            _locProj = GL.GetUniformLocation(_shader, "projection");
            _locModel = GL.GetUniformLocation(_shader, "model");
            _locTex = GL.GetUniformLocation(_shader, "tex");

            uint vao = 0, vbo = 0;
            GL.GenVertexArrays(1, &vao);
            GL.BindVertexArray(vao);
            GL.GenBuffers(1, &vbo);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 9 * sizeof(float), (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 9 * sizeof(float), (void*)(3 * sizeof(float)));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, Const.GL_FLOAT, false, 9 * sizeof(float), (void*)(5 * sizeof(float)));
            GL.BindVertexArray(0);
            _vao = vao; _vbo = vbo;

            // ── Procedural particle textures (32×32 grayscale alpha, tinted at render) ──
            _texIds[TexDot] = UploadTex(BuildRadial(2.2f, soft: true));
            _texIds[TexGlow] = UploadTex(BuildRadial(4.5f, soft: true));
            _texIds[TexSpark] = UploadTex(BuildSpark());
            _texIds[TexDrop] = UploadTex(BuildDrop());
            _texIds[TexCoin] = UploadTex(BuildCoin());
            _texIds[TexSquare] = UploadTex(BuildSquare());
        }
    }

    private static float Lerp(float a, float b, float t = -1f)
    {
        if (t < 0f) t = (float)System.Random.Shared.NextDouble();
        return a + (b - a) * t;
    }

    private static unsafe uint UploadTex(byte[] px)
    {
        const int S = 32;
        uint tex = 0;
        GL.GenTextures(1, &tex);
        GL.BindTexture(Const.GL_TEXTURE_2D, tex);
        fixed (byte* ptr = px)
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, S, S, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
        return tex;
    }

    private const int S = 32;

    private static byte[] BuildRadial(float hardness, bool soft)
    {
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float a = soft ? MathF.Max(0f, 1f - r) : MathF.Max(0f, 1f - r * hardness);
                a = a * a; // smoother falloff
                int i = (y * S + x) * 4;
                px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;
                px[i + 3] = (byte)Math.Clamp(a * 255f, 0f, 255f);
            }
        return px;
    }

    private static byte[] BuildSpark()
    {
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float core = MathF.Max(0f, 1f - MathF.Sqrt(dx * dx + dy * dy) * 2.2f);
                // Horizontal streak (the quad is velocity-aligned at render time).
                float streak = MathF.Max(0f, 1f - MathF.Abs(dy) * 6f) * MathF.Max(0f, 1f - MathF.Abs(dx));
                float a = Math.Clamp(core + streak * 0.8f, 0f, 1f);
                int i = (y * S + x) * 4;
                px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;
                px[i + 3] = (byte)(a * 255f);
            }
        return px;
    }

    private static byte[] BuildDrop()
    {
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                // Thin vertical streak, brightest near the head (bottom = falling direction).
                float a = MathF.Max(0f, 1f - MathF.Abs(u - 0.5f) * 4.5f) * (0.35f + 0.65f * v);
                int i = (y * S + x) * 4;
                px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;
                px[i + 3] = (byte)(Math.Clamp(a, 0f, 1f) * 255f);
            }
        return px;
    }

    private static byte[] BuildCoin()
    {
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                byte cr = 255, cg = 200, cb = 60, ca = 0;
                if (r < 0.92f)
                {
                    ca = 255;
                    // Rim darker, face brighter, small highlight top-left.
                    float rim = r > 0.72f ? 0.72f : 1f;
                    float hi = (dx < 0f && dy > 0f) ? 1.18f : 1f;
                    cr = (byte)Math.Clamp(255f * rim * hi, 0f, 255f);
                    cg = (byte)Math.Clamp(200f * rim * hi, 0f, 255f);
                    cb = (byte)Math.Clamp(60f * rim, 0f, 255f);
                }
                int i = (y * S + x) * 4;
                px[i] = cr; px[i + 1] = cg; px[i + 2] = cb; px[i + 3] = ca;
            }
        return px;
    }

    private static byte[] BuildSquare()
    {
        var px = new byte[S * S * 4];
        for (int i = 0; i < S * S; i++)
        {
            px[i * 4] = 255; px[i * 4 + 1] = 255; px[i * 4 + 2] = 255; px[i * 4 + 3] = 255;
        }
        return px;
    }
}
