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

    /// <summary>Rain spawn-area offset from the camera view center (world units) —
    /// shift where the rain band lives (Effects panel).</summary>
    public static float RainOffsetX, RainOffsetY;

    /// <summary>Rain size multiplier on the camera-relative default (1 = default;
    /// Effects panel). Scales streak length AND width.</summary>
    public static float RainSizeMul = 1f;

    /// <summary>Snow spawn-area offset from the camera view center (world units).</summary>
    public static float SnowOffsetX, SnowOffsetY;

    /// <summary>Snow flake size multiplier on the camera-relative default (1 = default).</summary>
    public static float SnowSizeMul = 1f;

    /// <summary>Seconds a snowflake RESTS on the surface it landed on (collision tile —
    /// ground, platform, obstacle box) before fading out. 0 = vanish instantly (old
    /// behavior). Snow physics: fall → contact → rest → fade.</summary>
    public static float SnowRestSeconds = 6f;

    // User decision (final evolution): weather surfaces = COLLISION tiles (box) +
    // DECOR tiles at their PER-PIXEL TILESET OUTLINE (the "tenda harusnya ada
    // percikan" screenshot — tents are tile art; the old any-tile BOX rule splashed
    // in empty air, which is why decor was removed — per-pixel fixes the real want)
    // + SPRITES per-pixel alpha. Transparent tile areas let particles fall through.

    /// <summary>Per-column first-opaque-from-top profile of ONE TILE in a tileset
    /// image (fraction 0 = tile top … 1 = tile bottom; 1.0 = column transparent).
    /// Cached per (tileset, tileId, tileSize) — built lazily from the decoded sheet.</summary>
    private static readonly Dictionary<(string Path, int TileId, int Cols, int Rows), float[]> _tileTopProfiles = new();

    /// <summary>Per-column top profile of ONE tile using the OBJECT's tileset grid:
    /// pixel rect = (tileId % Cols × W/Cols, tileId / Cols × H/Rows) — the exact rect
    /// the mesh bakes with Map2dTilesetCols/Rows. Fraction per column: 0 = tile top,
    /// 1 = fully transparent column.</summary>
    private static float[]? GetTileTopProfile(string path, int tileId, int cols, int rows)
    {
        if (string.IsNullOrWhiteSpace(path) || cols <= 0 || rows <= 0) return null;
        var key = (path, tileId, cols, rows);
        if (_tileTopProfiles.TryGetValue(key, out var cached)) return cached.Length > 0 ? cached : null;
        float[]? result = null;
        var (data, W, H) = GetSheetPixels(path);
        if (data != null && W > 0 && H > 0)
        {
            int tw = W / cols, th = H / rows;
            if (tw > 0 && th > 0)
            {
                int tc = tileId % cols, tr = tileId / cols;
                int px0 = tc * tw, py0 = tr * th;
                if (py0 < H)
                {
                    var prof = new float[tw];
                    int pyEnd = Math.Min(H, py0 + th);
                    for (int c = 0; c < tw; c++)
                    {
                        int px = px0 + c;
                        if (px >= W) { prof[c] = 1f; continue; }
                        prof[c] = 1f;
                        for (int py = py0; py < pyEnd; py++)
                        {
                            if (data[(py * W + px) * 4 + 3] >= WeatherAlphaThreshold)
                            {
                                prof[c] = (py - py0) / (float)th;
                                break;
                            }
                        }
                    }
                    result = prof;
                }
            }
        }
        _tileTopProfiles[key] = result ?? Array.Empty<float>();
        return result;
    }

    /// <summary>Global snowfall on/off (Effects panel weather preset or manual).</summary>
    public static bool SnowEnabled;

    /// <summary>EDITOR-MODE weather ticking is a SEPARATE session: turning weather on in
    /// the Effects panel must not leak into the play session (which starts from a clean
    /// reset). Toggled by the IDE when the user touches the weather controls.</summary>
    public static bool EditorWeatherSession;

    /// <summary>Snow density 0..1 (flakes per second scale).</summary>
    public static float SnowIntensity = 0.7f;

    /// <summary>Ground fog overlay on/off — translucent band hugging the collision
    /// ground across the camera view (drifting puffs, wind-advected).</summary>
    public static bool FogEnabled;

    /// <summary>Fog band thickness in world units.</summary>
    public static float FogHeight = 1.2f;

    /// <summary>Fog band opacity 0..1.</summary>
    public static float FogOpacity = 0.35f;

    /// <summary>Ambient tint overlay on/off — a full-view color wash (sunset warmth,
    /// storm gloom, snow chill). Driven by weather presets or manually.</summary>
    public static bool TintEnabled;

    /// <summary>Ambient tint color (RGB 0..1).</summary>
    public static Vector3 TintColor = new(1f, 0.45f, 0.2f);

    /// <summary>Ambient tint strength 0..1 (overlay alpha).</summary>
    public static float TintStrength = 0.35f;

    /// <summary>Monotonic weather clock (seconds since session start) — animates the
    /// fog drift. Advanced in Tick; renders read it.</summary>
    private static float _weatherClock;

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
        public bool Rest;              // snowflake resting on a surface (frozen, fading)
        public bool LeanToVel;         // rain streak: long axis (SizeY) follows velocity
                                       // → slants with wind direction AND strength, live
    }

    private static readonly List<P> _particles = new();
    public const int MaxParticles = 4000;

    /// <summary>Smoothed camera X-velocity (world units/s) for the spawn-band lead —
    /// raw per-frame delta is jittery (camera smoothing); exponential blend keeps the
    /// lead stable while still reacting to start/stop within ~10 frames.</summary>
    private static void TrackCameraTravel(Camera? cam, float dt)
    {
        if (cam == null || dt <= 0f) { _camVelX = 0f; return; }
        float x = cam.Position.X;
        if (float.IsNaN(_lastCamX)) { _lastCamX = x; _camVelX = 0f; return; }
        float inst = (x - _lastCamX) / dt;
        _lastCamX = x;
        _camVelX += (inst - _camVelX) * MathF.Min(1f, dt * 12f);
        if (MathF.Abs(_camVelX) < 0.01f) _camVelX = 0f; // dead-zone: no lead when still
    }

    // Frame context set by Tick, used by spawn helpers.
    private static float _dt;
    private static Camera? _cam;
    private static Tilemap2D? _map;
    /// <summary>Manager captured by Tick/TickEditorWeather for the SPRITE-surface
    /// checks (rain splash + snow rest on top of Player2D/Sprite2D heads).</summary>
    private static EditorObjectManager? _mgr;

    /// <summary>ALL tilemaps visible in the scene with their OBJECT tileset grid
    /// (Cols/Rows): the per-pixel tile profiles MUST use the same grid the mesh bakes
    /// with (W/Cols × H/Rows) — map.TileSize is a WORLD unit and using it sampled the
    /// wrong pixel rects (tents splashed as boxes / not at all). Rebuilt each tick;
    /// active map first.</summary>
    private static readonly List<(Tilemap2D Map, int Cols, int Rows)> _surfaceMaps = new();

    private static void BuildSurfaceMaps(EditorObjectManager? mgr, Tilemap2D? activeMap)
    {
        _surfaceMaps.Clear();
        if (mgr != null)
        {
            foreach (var o in mgr.Objects)
            {
                if (o is not { IsVisible: true, PrimitiveType: EditorPrimitiveType.Map2D }) continue;
                var m = o.Map2dTilemap;
                if (m == null) continue;
                bool dup = false;
                foreach (var t in _surfaceMaps) if (ReferenceEquals(t.Map, m)) { dup = true; break; }
                if (dup) continue;
                _surfaceMaps.Add((m, Math.Max(1, o.Map2dTilesetCols), Math.Max(1, o.Map2dTilesetRows)));
            }
        }
        if (activeMap != null)
        {
            int idx = -1;
            for (int i = 0; i < _surfaceMaps.Count; i++)
                if (ReferenceEquals(_surfaceMaps[i].Map, activeMap)) { idx = i; break; }
            if (idx > 0) { var t = _surfaceMaps[idx]; _surfaceMaps.RemoveAt(idx); _surfaceMaps.Insert(0, t); }
            else if (idx < 0) _surfaceMaps.Insert(0, (activeMap, 8, 8));
        }
    }

    // Camera travel tracking (user: running left a rain hole opened on the trailing
    // side — drops spawn relative to the CURRENT camera but land where the camera
    // WAS). Smoothed camera X-velocity feeds the spawn-band lead in SpawnRain/Snow.
    private static float _lastCamX = float.NaN;
    private static float _camVelX;
    private static readonly System.Random _rng = new();

    // ═══════════════════════ Public API ═══════════════════════

    /// <summary>Per-frame update: collect Effect2D emitters, emit, rain, integrate.
    /// Called from Player2DSystem.Update (preview/in-game only).</summary>
    public static void Tick(EditorObjectManager? mgr, Tilemap2D? map, Camera? cam, float dt)
    {
        _dt = dt; _cam = cam; _map = map; _mgr = mgr;
        TrackCameraTravel(cam, dt);
        BuildSurfaceMaps(mgr, map);
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

        // 2) Global weather across the camera view. In a play session the user may
        // flip layers manually — that IS the session's weather (EditorWeatherSession
        // stays false until the EDITOR touches the controls, see SetWeatherControl).
        _weatherClock += dt;
        if (RainEnabled && cam != null) SpawnRain(dt);
        if (SnowEnabled && cam != null) SpawnSnow(dt);

        // 3) Integrate + cull (mgr enables the sprite-head splash/rest checks).
        IntegrateParticles(dt);
    }

    /// <summary>Advance every live particle: wind advection, gravity, drag, position,
    /// rotation, size growth, ground splash-kill, safety cull. SHARED by the gameplay
    /// Tick and the editor-mode TickEditorWeather (the editor bug: it only spawned —
    /// drops piled up above the view and never fell because nothing integrated them).</summary>
    private static void IntegrateParticles(float dt)
    {
        var mgr = _mgr;
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            bool dead = p.Life <= 0f;

            if (!dead && !p.Rest)
            {
                p.VX += WindX * p.Wind * dt;
                p.VY -= p.Gravity * dt;
                // Weather terminal horizontal speed: advection ACCELERATES VX (no drag),
                // so strong wind + long airtime let particles fly away unbounded. Snow
                // (GroundKill) caps at a gentle flurry drift; rain (Splash) caps a bit
                // above its initial wind kick so the slant stays believable.
                if (p.GroundKill > 0f || p.Splash)
                {
                    float maxVx = p.GroundKill > 0f
                        ? 0.4f + 0.75f * MathF.Abs(WindX)
                        : 2f * MathF.Abs(WindX * p.Wind) + 1.0f;
                    p.VX = Math.Clamp(p.VX, -maxVx, maxVx);
                }
                if (p.Drag > 0f) { float d = MathF.Max(0f, 1f - p.Drag * dt); p.VX *= d; p.VY *= d; }
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                p.Rot += p.Spin * dt;
                p.Phase += p.SpinPhase * dt;
                p.Size = MathF.Max(0.01f, p.Size + p.Grow * dt);
                // Ground/obstacle contact: Splash particles (rain) die WITH a splash
                // flick; GroundKill particles (snow) LAND and REST on the surface for
                // SnowRestSeconds (frozen, then fade) — "deteksi obstacle → diam di
                // object itu". 0 = old instant-vanish.
                if (p.VY < 0f && (p.Splash || p.GroundKill > 0f))
                {
                    // Rain BODY contact first: a drop entering a sprite's rendered rect
                    // (player capsule, tents, props) splashes AT the contact point —
                    // it must not fall through behind the sprite (user request).
                    if (p.Splash)
                    {
                        var (bodyHit, _) = SpriteHitAt(mgr, p.X, p.Y, p.Y);
                        if (bodyHit)
                        {
                            SpawnSplash(p.X, p.Y);
                            dead = true;
                        }
                    }
                    // Surface = the HIGHER of the collision ground and any sprite top
                    // under this particle — rain splashes and snow RESTS there.
                    if (!dead)
                    {
                        float gy = GroundYAtAny(p.X, p.Y);
                        var (_, sy) = SpriteHitAt(mgr, p.X, p.Y, p.Y);
                        if (sy > gy) gy = sy;
                        if (!float.IsNegativeInfinity(gy) && p.Y <= gy + 0.02f)
                        {
                            if (p.Splash)
                            {
                                SpawnSplash(p.X, gy);
                                dead = true;
                            }
                            else
                            {
                                float rest = SnowRestSeconds;
                                if (rest > 0.05f)
                                {
                                    p.Rest = true;                      // freeze from now on
                                    p.Y = gy + p.Size * 0.5f;           // sit ON the surface
                                    p.VX = p.VY = 0f;
                                    p.Wind = 0f; p.Grow = 0f; p.Spin = 0f;
                                    p.MaxLife = rest; p.Life = rest;    // fresh fade clock
                                    p.C0 = new Vector4(1f, 1f, 1f, 1f);
                                    p.C1 = new Vector4(1f, 1f, 1f, 0f);
                                }
                                else
                                    dead = true;
                            }
                        }
                    }
                }
                if (p.Y < -80f || p.X < -500f || p.X > 5000f) dead = true; // safety cull
            }
            // Resting flakes: no movement at all — Life ticks down and the fade lerp
            // (t = 1 − Life/MaxLife) dissolves them in place.

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

    /// <summary>Scale the particle SIZES of a config (burst counts scale separately in
    /// SpawnBurst). Fresh configs from PresetConfig are mutated freely. Sizes ≤ 0 stay.
    /// This is what makes the FX Scale / Hit FX Scale sliders change how BIG the effect
    /// looks — before this, `scale` only multiplied the spawn COUNT.</summary>
    public static EmitConfig WithSizeScale(EmitConfig c, float s)
    {
        if (s <= 0f || MathF.Abs(s - 1f) < 0.001f) return c;
        c.SizeMin *= s;
        c.SizeMax *= s;
        if (c.AlignVelScale > 0f) c.AlignVelScale *= s; // streak length follows
        c.Grow *= s;                                    // per-second size delta follows
        return c;
    }

    /// <summary>One-shot burst at a world position (Spawn Effect trigger action).
    /// Compound presets (Explosion/Coin) spawn several complementary groups.
    /// `scale` grows BOTH the particle count AND the particle sizes.</summary>
    public static void SpawnBurst(string preset, Vector3 pos, float scale = 1f)
    {
        switch (preset)
        {
            case "Explosion":
            {
                // Flash core
                var core = WithSizeScale(PresetConfig("ExplosionCore"), scale);
                core.Pos = pos; core.Burst = Math.Max(1, (int)(14 * scale));
                for (int i = 0; i < core.Burst; i++) Spawn(core);
                // Debris squares with gravity + spin
                var debris = WithSizeScale(PresetConfig("ExplosionDebris"), scale);
                debris.Pos = pos; debris.Burst = Math.Max(1, (int)(16 * scale));
                for (int i = 0; i < debris.Burst; i++) Spawn(debris);
                // Radial sparks (streaks)
                var sparks = WithSizeScale(PresetConfig("Sparks"), scale);
                sparks.Pos = pos; sparks.Burst = Math.Max(1, (int)(12 * scale));
                for (int i = 0; i < sparks.Burst; i++) Spawn(sparks);
                break;
            }
            case "Coin":
            {
                var c = WithSizeScale(PresetConfig("Coin"), scale);
                c.Pos = pos; c.Burst = Math.Max(3, (int)(6 * scale));
                for (int i = 0; i < c.Burst; i++) Spawn(c);
                break;
            }
            default:
            {
                var g = WithSizeScale(PresetConfig(preset), scale);
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

    /// <summary>Global rain: spawn drops across the camera view + integrate splashes.
    /// Reference look (user): SPARSE thin vertical streaks of varying length, bright
    /// white-cyan, subtle slant — not a dense wall — with tiny splash flicks on the
    /// ground (SpawnSplash already matches the little "^" marks).</summary>
    private static void SpawnRain(float dt)
    {
        if (_cam == null) return;
        float halfH = MathF.Max(0.5f, _cam.OrthoSize);
        float halfW = halfH * _cam.GetAspect();
        float cx = _cam.Position.X, cy = _cam.Position.Y;

        int count = (int)(RainIntensity * 70f * dt);
        // Camera-relative sizing: fixed world sizes vanish at far zoom (a 0.018-unit
        // streak is sub-pixel on a 40-unit view — the "rain active but invisible" bug).
        // Fractions of the half-view height keep the look identical at any zoom.
        float u = halfH * MathF.Max(0.05f, RainSizeMul);
        // Wind offset (user: "x=5 kasih offset supaya tetap 1 layar penuh"): estimate
        // the horizontal drift over the fall and WIDEN + SHIFT the spawn band upwind,
        // so drops entering the view still cover the whole screen.
        float tFall = (2f * halfH + 3f) / 13f;                     // slowest drop → longest drift
        float drift = (MathF.Abs(WindX * RainWindFactor * 0.25f) + 0.3f) * tFall
                    + 0.5f * MathF.Abs(WindX * RainWindFactor * 0.25f) * tFall * tFall;
        // CAMERA-LEAD: widen + shift the band toward the camera's travel direction by
        // how far it moves during a drop's fall — otherwise running opens a rain hole
        // on the trailing side (drops land where the camera WAS, not where it IS).
        float camDrift = MathF.Abs(_camVelX) * tFall;
        drift += camDrift;
        float span = halfW + 2f + drift;
        float lead = MathF.Sign(WindX) * drift * 0.5f + MathF.Sign(_camVelX) * camDrift * 0.5f;
        float bandC = cx + RainOffsetX - lead;
        for (int i = 0; i < count; i++)
        {
            if (_particles.Count >= MaxParticles) return;
            float x = bandC + ((float)_rng.NextDouble() * 2f - 1f) * span;
            float y = cy + RainOffsetY + halfH + 0.5f + (float)_rng.NextDouble() * 2f;
            float vy = -(13f + (float)_rng.NextDouble() * 5f);
            // Mostly vertical: wind gives only a gentle lean (and advection via Wind).
            float vx = WindX * RainWindFactor * 0.25f + ((float)_rng.NextDouble() - 0.5f) * 0.3f;
            // Life is a SAFETY NET only (user: rain/snow must ALWAYS reach the ground):
            // the real death is surface contact (Splash/GroundKill). 3 s killed drops
            // mid-air on tall views (~45 units) before they ever touched ground.
            float life = 30f;
            float len = u * (0.05f + (float)_rng.NextDouble() * 0.06f); // varied streak lengths
            float wid = MathF.Max(0.02f, u * 0.003f);
            _particles.Add(new P
            {
                X = x, Y = y, Z = 0.075f,
                VX = vx, VY = vy,
                Life = life, MaxLife = life,
                Size = wid, SizeY = len,
                // SOLID square texture: crisp dashes like the reference. The drop
                // texture's thin streak core was sampling to near-nothing at this size.
                Tex = TexSquare, Wind = RainWindFactor * 0.25f,
                C0 = new Vector4(0.82f, 0.96f, 1f, 0.9f),
                C1 = new Vector4(0.82f, 0.96f, 1f, 0.9f),
                // LeanToVel: the dash slants with the wind (direction + strength) and
                // deepens as advection accelerates the drop — live, per frame.
                AlignVel = false, LeanToVel = true, Splash = true, GroundKill = 1f,
            });
        }
    }

    /// <summary>Global snowfall: soft flakes across the camera view, slow fall with a
    /// sine sway + wind advection, no splash (they just fade near the ground).</summary>
    private static void SpawnSnow(float dt)
    {
        if (_cam == null) return;
        float halfH = MathF.Max(0.5f, _cam.OrthoSize);
        float halfW = halfH * _cam.GetAspect();
        float cx = _cam.Position.X, cy = _cam.Position.Y;

        int count = (int)(SnowIntensity * 90f * dt);
        // Camera-relative flake size (same reasoning as SpawnRain — fixed world sizes
        // are sub-pixel at far zoom and the snowfall disappears), × user multiplier.
        float u = halfH * MathF.Max(0.05f, SnowSizeMul);
        // Wind offset: flakes drift at the capped terminal speed (see IntegrateParticles)
        // for up to ~28 s of fall — extend the spawn band UPWIND by that drift so the
        // view stays fully covered at any wind strength (user request).
        float tFall = MathF.Min((2f * halfH + 3f) / 1.2f, 28f);    // slowest flake, life-capped
        float drift = (0.4f + 0.75f * MathF.Abs(WindX)) * tFall;
        // CAMERA-LEAD (see SpawnRain): snow's long fall makes the trailing hole worse
        // when running — lead the band into the camera's travel direction.
        float camDrift = MathF.Abs(_camVelX) * MathF.Min(tFall, 2f); // cap: don't over-lead
        drift += camDrift;
        float span = halfW + 2f + drift;
        float lead = MathF.Sign(WindX) * drift * 0.5f + MathF.Sign(_camVelX) * camDrift * 0.5f;
        float bandC = cx + SnowOffsetX - lead;
        for (int i = 0; i < count; i++)
        {
            if (_particles.Count >= MaxParticles) return;
            float x = bandC + ((float)_rng.NextDouble() * 2f - 1f) * span;
            float y = cy + SnowOffsetY + halfH + 0.5f + (float)_rng.NextDouble() * 2f;
            // Life is a SAFETY NET only (user: snow must ALWAYS reach the ground):
            // 1.2 u/s × tall views can need 100+ units of fall; 30 s still faded flakes
            // mid-air. 180 s covers ~215 units of fall; GroundKill kills on contact.
            float life = 180f;
            _particles.Add(new P
            {
                X = x, Y = y, Z = 0.075f,
                VX = ((float)_rng.NextDouble() - 0.5f) * 0.4f,
                VY = -(1.2f + (float)_rng.NextDouble() * 0.9f),
                Life = life, MaxLife = life,
                Size = u * (0.008f + (float)_rng.NextDouble() * 0.008f),
                Grow = u * 0.001f,
                Tex = TexDot, Wind = 0.9f,
                // PURE WHITE + fully opaque while alive (user: snow reads bluish/
                // translucent over the blue fog backdrop). Fade only at death via
                // the C1 alpha-0 lerp; no blue tint anywhere.
                C0 = new Vector4(1f, 1f, 1f, 1f),
                C1 = new Vector4(1f, 1f, 1f, 0.85f),
                GroundKill = 1f,
            });
        }
    }

    /// <summary>EDITOR weather tick: spawn + integrate rain/snow while the user is
    /// DESIGNING weather in edit mode — the gameplay Tick only runs in preview/in-game,
    /// so without this the panel showed "Partikel aktif: 0" and nothing fell. Call from
    /// the editor render path EVERY frame (cheap: 0 work when nothing is enabled).
    /// The EDITOR must pass the ACTIVE tilemap + editor object manager: the runtime's
    /// pinned map is only set during play, so using it here silently disabled every
    /// ground splash / snow rest / sprite-head check in edit mode (the "percikan
    /// hujannya mana?" bug).</summary>
    public static void TickEditorWeather(Camera? cam, float dt, Tilemap2D? map, EditorObjectManager? mgr)
    {
        if (dt <= 0f || cam == null) return;
        if (!RainEnabled && !SnowEnabled && !FogEnabled && !TintEnabled) return;
        _dt = dt; _cam = cam; _map = map; _mgr = mgr;
        TrackCameraTravel(cam, dt);
        BuildSurfaceMaps(mgr, map);
        _weatherClock += dt;
        if (RainEnabled) SpawnRain(dt);
        if (SnowEnabled) SpawnSnow(dt);
        // Integrate too — spawn-only meant drops piled up above the view forever.
        IntegrateParticles(dt);
        // Editor keeps its own session marker so a later play start resets cleanly.
        EditorWeatherSession = true;
    }

    /// <summary>Weather PRESETS (Effects panel): one switch sets rain/snow/fog/tint +
    /// wind. Sunset/Clear Day drive only the ambient tint; Clear Day also clears all
    /// precipitation. Fog density multiplies the fog band opacity.</summary>
    public static void ApplyWeatherPreset(string preset, float fogDensity = 1f)
    {
        switch (preset)
        {
            case "Clear Day":
                RainEnabled = false; SnowEnabled = false; FogEnabled = false;
                TintEnabled = true;  TintColor = new Vector3(1f, 1f, 0.92f); TintStrength = 0.10f;
                break;
            case "Sunset":
                RainEnabled = false; SnowEnabled = false; FogEnabled = false;
                TintEnabled = true;  TintColor = new Vector3(1f, 0.45f, 0.20f); TintStrength = 0.35f;
                break;
            case "Rain":
                RainEnabled = true; SnowEnabled = false; FogEnabled = true;
                FogHeight = MathF.Max(0.4f, FogHeight); FogOpacity = 0.30f * fogDensity;
                TintEnabled = true;  TintColor = new Vector3(0.45f, 0.55f, 0.75f); TintStrength = 0.30f;
                break;
            case "Storm":
                RainEnabled = true; SnowEnabled = false; FogEnabled = true;
                FogHeight = MathF.Max(0.8f, FogHeight); FogOpacity = 0.50f * fogDensity;
                TintEnabled = true;  TintColor = new Vector3(0.30f, 0.36f, 0.50f); TintStrength = 0.45f;
                WindX = MathF.Max(WindX, 3f);
                break;
            case "Snow":
                RainEnabled = false; SnowEnabled = true; FogEnabled = true;
                FogHeight = MathF.Max(0.6f, FogHeight); FogOpacity = 0.25f * fogDensity;
                TintEnabled = true;  TintColor = new Vector3(0.80f, 0.88f, 1f); TintStrength = 0.22f;
                break;
            case "Fog":
                RainEnabled = false; SnowEnabled = false; FogEnabled = true;
                FogHeight = MathF.Max(1.0f, FogHeight); FogOpacity = 0.55f * fogDensity;
                TintEnabled = false;
                break;
        }
    }

    /// <summary>Turn every global weather layer off (manual mode).</summary>
    public static void ClearWeather()
    {
        RainEnabled = false;
        SnowEnabled = false;
        FogEnabled = false;
        TintEnabled = false;
    }

    /// <summary>Draw the ambient tint wash over the CURRENT camera view. Called from
    /// the 2D pass AFTER all sprites/particles — a pure on-top overlay. The fog itself
    /// moved to RenderFogFront: fog draws IN FRONT of the world (user decision) —
    /// a translucent full atmospheric cloud layer at the end of the 2D pass.</summary>
    public static unsafe void RenderWeatherOverlay(Camera camera)
    {
        bool needTint = TintEnabled && TintStrength > 0.001f;
        if (!needTint) return;

        EnsureResources();
        if (_shader == 0) return;
        float halfH = MathF.Max(0.5f, camera.OrthoSize);
        float halfW = halfH * camera.GetAspect();
        float cx = camera.Position.X, cy = camera.Position.Y;

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

        // ── Ambient tint: one full-view wash. TexSquare = hard-edged solid fill, so
        // the color reaches every corner of the view at a UNIFORM alpha (a soft blob
        // texture would vignette the corners back to transparent). ──
        if (needTint)
        {
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[TexSquare]);
            OverlayQuad(cx - halfW, cy - halfH, cx + halfW, cy + halfH, 0.085f,
                new Vector4(TintColor, TintStrength));
        }

        GL.BindVertexArray(0);
        if (cull) GL.Enable(Const.GL_CULL_FACE);
        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
        GL.UseProgram(Shader.GetShaderProgram());
    }

    /// <summary>Shared overlay quad writer: 54-float layout written straight into the
    /// static scratch (no stackalloc in loops — CA2014; no Span capture by local
    /// functions — CS8175). Assumes the overlay shader + VAO/VBO are already bound.</summary>
    private static unsafe void OverlayQuad(float x0, float y0, float x1, float y1, float z, Vector4 col)
    {
        float r = col.X, g = col.Y, b = col.Z, a = col.W;
        var buf = _overlayBuf;
        int o = 0;
        buf[o++] = x0; buf[o++] = y0; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
        buf[o++] = r; buf[o++] = g; buf[o++] = b; buf[o++] = a;
        buf[o++] = x1; buf[o++] = y0; buf[o++] = z; buf[o++] = 1f; buf[o++] = 0f;
        buf[o++] = r; buf[o++] = g; buf[o++] = b; buf[o++] = a;
        buf[o++] = x1; buf[o++] = y1; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
        buf[o++] = r; buf[o++] = g; buf[o++] = b; buf[o++] = a;
        buf[o++] = x0; buf[o++] = y0; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
        buf[o++] = r; buf[o++] = g; buf[o++] = b; buf[o++] = a;
        buf[o++] = x1; buf[o++] = y1; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
        buf[o++] = r; buf[o++] = g; buf[o++] = b; buf[o++] = a;
        buf[o++] = x0; buf[o++] = y1; buf[o++] = z; buf[o++] = 0f; buf[o++] = 1f;
        buf[o++] = r; buf[o++] = g; buf[o++] = b; buf[o++] = a;
        fixed (float* p = buf)
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(54 * sizeof(float)), p, Const.GL_DYNAMIC_DRAW);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
    }

    /// <summary>Reference-style atmospheric fog drawn IN FRONT of the world (user:
    /// "fog harusnya muncul dipaling depan"): translucent dark wash + drifting cloud
    /// billows (brighter toward the bottom) + a horizon glow — tiles/player/particles
    /// stay VISIBLE through it. Drawn near the END of EditorObjectManager.Draw (after
    /// all world sprites, before editor gizmos); everything here is alpha-blended.</summary>
    public static unsafe void RenderFogFront(Camera camera)
    {
        if (!FogEnabled) return;
        EnsureResources();
        if (_shader == 0) return;
        float halfH = MathF.Max(0.5f, camera.OrthoSize);
        float halfW = halfH * camera.GetAspect();
        float cx = camera.Position.X, cy = camera.Position.Y;
        float t = _weatherClock;
        float A = Math.Clamp(FogOpacity * 1.4f, 0.05f, 0.95f); // cloud alpha (translucent!)
        float sizeMul = Math.Clamp(FogHeight, 0.4f, 3f);      // cloud size scale

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
        GL.Disable(Const.GL_DEPTH_TEST); // frontmost overlay — draw order decides

        GL.BindVertexArray(_vao);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
        GL.ActiveTexture(Const.GL_TEXTURE0);
        GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[TexGlow]);

        // Palette: the reference look (dark navy → blue mid → cyan glow bottom).
        // An active Tint steers it (Sunset = warm clouds, Snow = pale clouds).
        bool steer = TintEnabled && TintStrength > 0.001f;
        Vector3 bright = steer ? TintColor : new Vector3(0.30f, 0.85f, 1f);
        Vector3 mid = steer ? TintColor * 0.55f : new Vector3(0.10f, 0.35f, 0.90f);
        Vector3 deep = new Vector3(0.05f, 0.10f, 0.32f);

        // 1) Translucent dark wash over the whole view (mood base — the world shows
        // through; an opaque base would hide it since this draws ON TOP now).
        OverlayQuad(cx - halfW, cy - halfH, cx + halfW, cy + halfH, 0.016f,
            new Vector4(deep, A * 0.55f));

        // 2) Billow rows: three drifting layers, brighter + denser toward the bottom.
        float span = (halfW + 1f) * 2f;
        for (int row = 0; row < 3; row++)
        {
            float rowF = row / 2f;                                        // 0 top → 1 bottom
            float rowY = cy + halfH - (0.18f + 0.64f * rowF) * 2f * halfH;
            Vector3 col = Vector3.Lerp(mid, bright, rowF);
            float alpha = A * (0.30f + 0.38f * rowF);
            float s = halfH * (0.45f + 0.35f * rowF) * sizeMul;
            int n = (int)MathF.Ceiling(span / (s * 1.2f)) + 1;
            float spacing = span / n;
            float drift = 0.15f + WindX * (0.10f + 0.08f * row);          // slow ambient drift + wind
            for (int i = 0; i <= n; i++)
            {
                float raw = i * spacing + row * s * 0.7f + t * drift * s;
                float xw = raw - MathF.Floor(raw / span) * span;          // wrap seamlessly
                float xx = cx - halfW - 1f + xw;
                float yy = rowY + MathF.Sin(t * (0.25f + 0.07f * row) + i * 1.71f + row * 2.1f) * halfH * 0.08f;
                OverlayQuad(xx - s, yy - s * 0.8f, xx + s, yy + s * 0.8f, 0.017f + row * 0.001f,
                    new Vector4(col, alpha));
            }
        }

        // 3) Horizon bloom: wide flattened glows near the bottom — the bright cyan
        // wash under the platform in the reference.
        for (int i = 0; i < 4; i++)
        {
            float raw = i * span * 0.33f + t * (0.2f + WindX * 0.06f) * halfH;
            float xw = raw - MathF.Floor(raw / span) * span;
            float xx = cx - halfW - 1f + xw;
            float yy = cy - halfH + halfH * 0.16f + MathF.Sin(t * 0.2f + i * 2.3f) * halfH * 0.05f;
            float w = halfW * 0.8f, h = halfH * 0.42f;
            OverlayQuad(xx - w, yy - h, xx + w, yy + h, 0.021f, new Vector4(bright, A * 0.45f));
        }

        GL.BindVertexArray(0);
        if (cull) GL.Enable(Const.GL_CULL_FACE);
        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
        GL.UseProgram(Shader.GetShaderProgram());
    }

    /// <summary>Small splash where a raindrop hits the ground (user: "kena tanah ada
    /// percikannya sedikit"): 3 camera-relative dots kicked up in a tiny fan — visible
    /// but subtle. Gravity pulls them back down; they fade before re-touching.</summary>
    private static void SpawnSplash(float x, float groundY)
    {
        float u = _cam != null ? MathF.Max(0.5f, _cam.OrthoSize) : 1f;
        for (int i = 0; i < 3; i++)
        {
            if (_particles.Count >= MaxParticles) return;
            // Fan spread: outer dots lean left/right, the middle one pops straight up.
            float side = (i - 1) * 1.1f;
            float vx = side + ((float)_rng.NextDouble() - 0.5f) * 0.4f + WindX * 0.3f;
            float vy = (2.0f + (float)_rng.NextDouble() * 1.4f) - MathF.Abs(side) * 0.5f;
            float life = 0.26f + (float)_rng.NextDouble() * 0.16f;
            _particles.Add(new P
            {
                X = x, Y = groundY + u * 0.02f, Z = 0.075f,
                VX = vx, VY = vy,
                Life = life, MaxLife = life,
                Size = u * 0.034f, Grow = -u * 0.02f,
                Gravity = 9f,
                Tex = TexDot, Wind = 0.3f,
                C0 = new Vector4(0.80f, 0.90f, 1f, 0.9f),
                C1 = new Vector4(0.80f, 0.90f, 1f, 0f),
            });
        }
    }

    /// <summary>World Y of the first collision tile below (x, yStart) on any visible
    /// layer — the rain's splash plane. Negative infinity when the map/column is empty.</summary>
    /// <summary>Highest weather surface below (x, yStart) across ALL scene maps —
    /// rain splash and snow rest land on the highest one. Per map, per column:
    /// COLLISION tiles = full box; DECOR tiles = PER-PIXEL tileset outline (first
    /// opaque pixel in this column — tent slopes splash, transparent areas don't).
    /// Sprites are handled separately via per-pixel SpriteHitAt.</summary>
    private static float GroundYAtAny(float x, float yStart)
    {
        float best = float.NegativeInfinity;
        foreach (var (map, cols, rows) in _surfaceMaps)
        {
            if (map == null) continue;
            float cell = map.TileSize * Tilemap2D.WorldScale;
            if (cell <= 0f || map.Width <= 0 || map.Height <= 0) continue;
            int col = (int)MathF.Floor(x / cell);
            if (col < 0 || col >= map.Width) continue;
            float topWorld = map.Height * cell;
            int rowStart = (int)MathF.Floor((topWorld - yStart) / cell);
            rowStart = Math.Clamp(rowStart, 0, map.Height - 1);
            for (int row = rowStart; row < map.Height; row++)
            {
                bool found = false;
                for (int li = 0; li < map.Layers.Count; li++)
                {
                    var layer = map.Layers[li];
                    if (layer == null || !layer.IsVisible) continue;
                    int tile = map.GetTile(li, col, row);
                    if (tile <= 0) continue;
                    float tileTop = topWorld - row * cell;
                    bool solid = layer.CollisionTileIds != null && layer.CollisionTileIds.Count > 0
                        && layer.CollisionTileIds.Contains(tile);
                    // PER-PIXEL outline first (collision tiles too — a solid "tent"
                    // tile must splash on its SLOPED art, not an invisible box top:
                    // user "percikan ada di antara sprite bukan box nya").
                    var prof = GetTileTopProfile(map.TilesetImagePath ?? "", tile, cols, rows);
                    if (prof != null && prof.Length > 0)
                    {
                        float fx = (x - col * cell) / cell;
                        int ci = Math.Clamp((int)(fx * prof.Length), 0, prof.Length - 1);
                        float f = prof[ci]; // 0 = tile top … 1 = transparent column
                        if (f < 1f)
                        {
                            // f counts DOWN from the tile top (row 0 = top in the image)
                            float surf = tileTop - f * cell;
                            if (surf > best) best = surf;
                            found = true;
                            break;
                        }
                        // Transparent column at this x → fall through to deeper rows
                        // (even for collision tiles: opaque art decides, not the box).
                        continue;
                    }
                    // No pixel data (tileset not loaded / bad path) → collision tiles
                    // fall back to the full box; decor tiles fall through.
                    if (solid)
                    {
                        if (tileTop > best) best = tileTop; // full box surface
                        found = true;
                        break;
                    }
                }
                if (found) break; // this map's highest surface for the column is done
            }
        }
        return best;
    }

    // ── PER-PIXEL sprite detection (user: "percikan di pinggiran sprite, bukan
    // kotaknya"): the rect test splashed on invisible box corners. Now the sprite's
    // sheet image is decoded ONCE (cached) and each frame rect gets a TOP PROFILE —
    // per pixel column, the first opaque row from the top — so rain/snow surfaces
    // follow the ACTUAL outline (hat brims, tent slopes, raised swords).
    private static readonly Dictionary<string, (byte[]? Data, int W, int H)> _sheetPixels = new();
    private static readonly Dictionary<(string Path, int U0, int U1, int V0, int V1), (float[] Profile, int Px0, int Px1)> _frameTopProfiles = new();
    private const byte WeatherAlphaThreshold = 128;

    private static (byte[]? Data, int W, int H) GetSheetPixels(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, 0, 0);
        if (_sheetPixels.TryGetValue(path, out var cached)) return cached;
        (byte[]? Data, int W, int H) result = (null, 0, 0);
        try
        {
            string full = DarkEngine3D_gl_csharp.Engine.Helpers.PathHelpers.Resolve(path);
            if (File.Exists(full))
            {
                using var stream = File.OpenRead(full);
                var img = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                result = (img.Data, img.Width, img.Height);
            }
        }
        catch { result = (null, 0, 0); }
        _sheetPixels[path] = result;
        return result;
    }

    /// <summary>Per-column first-opaque-from-top profile of one frame rect (fractions
    /// 0 = frame top … 1 = frame bottom; 1.0 = column fully transparent). Keyed by the
    /// quantized uv rect — facing/flip mirrors share the same rect, so one profile per
    /// unique frame. Built lazily; sprite edits need a restart to refresh (session tool).</summary>
    private static (float[] Profile, int Px0, int Px1, byte[] Data, int W, int H)? GetFrameTopProfile(
        string path, float uMin, float uMax, float vMin, float vMax)
    {
        var (data, W, H) = GetSheetPixels(path);
        if (data == null || W <= 0 || H <= 0) return null;
        int u0 = (int)(uMin * 10000f), u1 = (int)(uMax * 10000f);
        int v0 = (int)(vMin * 10000f), v1 = (int)(vMax * 10000f);
        var key = (path, Math.Min(u0, u1), Math.Max(u0, u1), Math.Min(v0, v1), Math.Max(v0, v1));
        if (_frameTopProfiles.TryGetValue(key, out var hit))
            return (hit.Profile, hit.Px0, hit.Px1, data, W, H);

        int px0 = Math.Clamp((int)MathF.Floor(Math.Min(u0, u1) / 10000f * W), 0, W - 1);
        int px1 = Math.Clamp((int)MathF.Ceiling(Math.Max(u0, u1) / 10000f * W), 1, W);
        int py0 = Math.Clamp((int)MathF.Floor(Math.Min(v0, v1) / 10000f * H), 0, H - 1);
        int py1 = Math.Clamp((int)MathF.Ceiling(Math.Max(v0, v1) / 10000f * H), 1, H);
        int cols = Math.Max(1, px1 - px0), rows = Math.Max(1, py1 - py0);
        var profile = new float[cols];
        for (int c = 0; c < cols; c++)
        {
            int px = px0 + c;
            profile[c] = 1f; // default: transparent column
            for (int py = py0; py < py1; py++)
            {
                if (data[(py * W + px) * 4 + 3] >= WeatherAlphaThreshold)
                {
                    profile[c] = (py - py0) / (float)rows;
                    break;
                }
            }
        }
        _frameTopProfiles[key] = (profile, px0, px1);
        return (profile, px0, px1, data, W, H);
    }

    /// <summary>Sprite surface check for weather particles, PER-PIXEL: returns whether
    /// the point (x, y) sits on an OPAQUE sprite pixel (body hit — rain splashes at
    /// the contact point) and the highest VISIBLE outline surface below yStart (for
    /// snow rest / top splashes). Transparent rect areas let particles fall through.
    /// Per-type draw-data: Player2D → TryGetPlayer2DDrawData, Sprite2D →
    /// TryGetSprite2DDrawData; quads are the live render bounds (offsets + custom
    /// frames + facing mirror included).</summary>
    private static (bool BodyHit, float TopY) SpriteHitAt(EditorObjectManager? mgr, float x, float y, float yStart)
    {
        float bestTop = float.NegativeInfinity;
        if (mgr == null) return (false, bestTop);
        foreach (var o in mgr.Objects)
        {
            if (o is not { IsVisible: true, PrimitiveType: EditorPrimitiveType.Player2D or EditorPrimitiveType.Sprite2D })
                continue;
            uint t;
            System.Numerics.Vector2 uvMin, uvMax;
            System.Numerics.Vector3 bl, br, tr, tl;
            bool ok = o.PrimitiveType == EditorPrimitiveType.Player2D
                ? o.TryGetPlayer2DDrawData(out t, out uvMin, out uvMax, out bl, out br, out tr, out tl)
                : o.TryGetSprite2DDrawData(out t, out uvMin, out uvMax, out bl, out br, out tr, out tl);
            if (!ok) continue;

            float x0 = MathF.Min(bl.X, tl.X), x1 = MathF.Max(br.X, tr.X);
            float y0 = MathF.Min(bl.Y, br.Y), y1 = MathF.Max(tl.Y, tr.Y);
            if (x < x0 || x > x1) continue;

            // Pixel path (preferred): decode the sheet + per-column top profile.
            // FALLBACK (never silently skip a sprite — the "scarecrow tembus" bug):
            // when pixels are unavailable, use the RECT top / rect body.
            byte[]? data = null; int W = 0, H = 0, px = 0;
            bool havePixels = false;
            float surfaceY = y1; // rect-top fallback
            string imagePath = "";
            if (o.TryGetPlayer2DClip(out var sheet, out var _) && sheet != null)
                imagePath = sheet.ImagePath ?? "";

            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                var fp = GetFrameTopProfile(imagePath, uvMin.X, uvMax.X, uvMin.Y, uvMax.Y);
                if (fp != null)
                {
                    var (profArr, px0, px1, pdata, pw, ph) = fp.Value;
                    float u = uvMin.X + Math.Clamp((x - x0) / MathF.Max(0.0001f, x1 - x0), 0f, 1f) * (uvMax.X - uvMin.X);
                    px = Math.Clamp((int)(u * pw), 0, pw - 1);
                    int idx = Math.Clamp(px - px0, 0, Math.Max(0, px1 - px0 - 1));
                    if (profArr[idx] < 1f)
                    {
                        havePixels = true;
                        data = pdata; W = pw; H = ph;
                        surfaceY = y1 - profArr[idx] * (y1 - y0); // real outline surface
                    }
                    else
                        continue; // transparent column → fall through this sprite
                }
            }

            if (surfaceY <= yStart && surfaceY > bestTop) bestTop = surfaceY;

            // Body hit: the drop is inside the sprite; with pixels, require the exact
            // pixel to be opaque; without, the rect body is the fallback surface.
            if (y >= y0 && y <= y1)
            {
                if (!havePixels)
                    return (true, surfaceY); // rect body fallback
                float fy = Math.Clamp((y - y0) / MathF.Max(0.0001f, y1 - y0), 0f, 1f); // 0 bottom → 1 top
                float v = uvMax.Y + fy * (uvMin.Y - uvMax.Y);
                int py = Math.Clamp((int)(v * H), 0, H - 1);
                if (data != null && data[(py * W + px) * 4 + 3] >= WeatherAlphaThreshold)
                    return (true, surfaceY);
            }
        }
        return (false, bestTop);
    }

    /// <summary>Alive particle count (debug/perf readout).</summary>
    public static int ParticleCount => _particles.Count;

    /// <summary>Remove every live particle (session start / project close). Weather
    /// flags are NOT touched here — use ResetWeather for that.</summary>
    public static void Clear() => _particles.Clear();

    /// <summary>Clear particles + turn ALL weather off (project close / session end).</summary>
    public static void ResetWeather()
    {
        Clear();
        RainEnabled = false;
        WindX = 0f;
        SnowEnabled = false;
        FogEnabled = false;
        TintEnabled = false;
        RainOffsetX = RainOffsetY = 0f;
        RainSizeMul = 1f;
        SnowOffsetX = SnowOffsetY = 0f;
        SnowSizeMul = 1f;
        SnowRestSeconds = 6f;
    }

    // ═══════════════════════ Rendering ═══════════════════════

    private static uint _shader, _vao, _vbo;
    private static int _locView, _locProj, _locModel, _locTex;
    private static readonly uint[] _texIds = new uint[6];
    private static bool _glReady;

    private static readonly float[] _vertBuf = new float[MaxParticles * 6 * 9];
    /// <summary>Shared 54-float scratch for the weather overlay quads (no stackalloc
    /// in loops — CA2014 rule; single-threaded render path, so one buffer is safe).</summary>
    private static readonly float[] _overlayBuf = new float[54];

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
            // LeanToVel (rain): rotate the SizeY long axis onto the CURRENT velocity.
            // Rotating +Y by θ gives axis (−sin θ, cos θ); solving for axis ∥ (vx, vy)
            // → θ = atan2(−vx, vy). (The −vy variant mirrors the line across horizontal
            // — the streaks leaned OPPOSITE to the wind, the bug the user spotted.)
            float rot = p.LeanToVel ? MathF.Atan2(-p.VX, p.VY)
                : p.AlignVel ? MathF.Atan2(p.VY, p.VX) : p.Rot;
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
