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

    /// <summary>Small droplets per rain splash (2–4 = the reference: one big ring plus
    /// a few little circles above it). 0 = ring only.</summary>
    public static int RainSplashAmount = 3;

    /// <summary>Splash size multiplier over the camera-relative base — scales BOTH the
    /// ground ring and the small droplets (1 = default). Panel: "Splash Size".</summary>
    public static float RainSplashSize = 1.2f;

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

    /// <summary>THICK FOG on/off — reference #1: big soft cloud masses (puffs) drifting
    /// with the wind over a low base haze. Dense; it hides the scene behind it.</summary>
    public static bool FogEnabled;

    /// <summary>Vertical extent of the fog bank as a fraction of the view
    /// (0.70 = most of the screen, like the reference).</summary>
    public static float FogCoverage = 0.75f;

    /// <summary>How many cloud CLUSTERS are drawn. Each cluster stamps one big cloud
    /// mass plus a few satellite lobes, so the field reads as lumpy clouds (reference #1)
    /// rather than separate round glows.</summary>
    public static int FogPuffs = 12;

    /// <summary>Cluster size multiplier over the camera-relative base (1 = default).</summary>
    public static float FogPuffSize = 1.30f;

    /// <summary>Per-puff alpha 0..1. Puffs overlap heavily, so 0.2–0.4 already builds a
    /// thick bank; this is NOT a whole-screen wash.</summary>
    public static float FogOpacity = 0.30f;

    /// <summary>Flat base haze under the puffs (fills the gaps between clouds), 0..1.</summary>
    public static float FogBaseHaze = 0.35f;

    /// <summary>Drift/morph speed multiplier for the puffs.</summary>
    public static float FogSpeed = 1f;

    /// <summary>Fog colour — soft grey by default (reference #1).</summary>
    public static Vector3 FogColor = new(0.72f, 0.74f, 0.78f);

    /// <summary>Steer the fog colour toward the ambient <see cref="TintColor"/>.</summary>
    public static bool FogUseTint = true;

    /// <summary>THIN MIST on/off — reference #2: small, sparse wisps hugging the ground.
    /// Deliberately NOT a cloud bank — that is fog's job.</summary>
    public static bool MistEnabled;

    /// <summary>How many wisps are drawn. Mist is MANY small pieces (reference #2),
    /// not a few big bands.</summary>
    public static int MistWisps = 56;

    /// <summary>Height of the wisp zone as a fraction of the view (hugs the ground).</summary>
    public static float MistHeight = 0.20f;

    /// <summary>Wisp length as a multiple of half the view width. Small by design —
    /// a wisp is a short streak, not a band spanning the screen.</summary>
    public static float MistLength = 0.22f;

    /// <summary>Wisp thickness as a multiple of the camera half-height.</summary>
    public static float MistThickness = 0.030f;

    /// <summary>Wisp alpha 0..1 — thin, so 0.12–0.25 is the usable range.</summary>
    public static float MistOpacity = 0.55f;

    /// <summary>Drift speed multiplier for the wisps.</summary>
    public static float MistSpeed = 1f;

    /// <summary>Mist colour — pale white-blue by default.</summary>
    public static Vector3 MistColor = new(0.85f, 0.88f, 0.92f);

    /// <summary>Steer the mist colour toward the ambient <see cref="TintColor"/>.</summary>
    public static bool MistUseTint = true;

    /// <summary>How strongly WindX speeds up / reverses the wisp drift.</summary>
    public static float MistWindDrift = 0.50f;

    // ═══════════════════════ Pixel atmosphere FX ═══════════════════════
    // Five optional overlay effects (lightning / god rays / heat haze / frost / wind
    // streaks). Everything is drawn from hard-edged procedural quads and segments so it
    // matches the pixel-art look — no sprite assets, no post-processing pass.

    /// <summary>Lightning on/off — procedural bolts that strike the weather ground.
    /// Timer-driven; <see cref="StrikeLightning"/> forces one immediately.</summary>
    public static bool LightningEnabled;

    /// <summary>Seconds between automatic strikes (0 = manual only).</summary>
    public static float LightningInterval = 4f;

    /// <summary>Bolt thickness as a fraction of the camera half-height.</summary>
    public static float LightningThickness = 0.045f;

    /// <summary>Full-view flash strength 0..1 at the moment of the strike.</summary>
    public static float LightningFlash = 0.5f;

    public static Vector3 LightningColor = new(0.92f, 0.96f, 1f);
    public static Vector3 LightningGlowColor = new(0.55f, 0.70f, 0.95f);

    /// <summary>Sun shafts on/off (diagonal translucent beams + a bloom at the source).</summary>
    public static bool GodRaysEnabled;
    public static int GodRaysCount = 7;
    public static float GodRaysStrength = 0.22f;
    /// <summary>Total angular width of the beam fan, degrees.</summary>
    public static float GodRaysSpread = 46f;
    /// <summary>Central beam direction, degrees (0 = right, 90 = up). 236 = down-left.</summary>
    public static float GodRaysAngle = 236f;
    /// <summary>Beam length as a multiple of the view's half-diagonal.</summary>
    public static float GodRaysLength = 1.7f;
    public static Vector3 GodRaysColor = new(1f, 0.95f, 0.75f);

    /// <summary>Visible wind streaks on/off (horizontal drifting tailed dashes).</summary>
    public static bool WindStreaksEnabled;
    /// <summary>Streak count multiplier (1 = the default 32 streaks).</summary>
    public static float WindStreakAmount = 1f;
    public static float WindStreakSpeed = 1f;
    public static Vector3 WindStreakColor = new(0.92f, 0.97f, 1f);

    /// <summary>Wind streak style mode: <c>0</c> = lurus (tailed dashes), <c>1</c> =
    /// melingkar/pecah (sparse puffs/curls drawn from the mist wisp texture, so the wind
    /// sometimes reads as curls rather than one uniform line field).</summary>
    /// <summary>Wind streak style mode: <c>0</c> = lurus (tailed dashes from TexWindStreak),
    /// <c>1</c> = kumparan/sumber (sparse puffs/curls drawn from the mist wisp texture, so the
    /// wind sometimes reads as curls rather than one uniform line field).</summary>
    public static int WindStreakMode;

    /// <summary>Wind streak vertical band height as a fraction of the view half-height
    /// (0.1 = thin ribbon near the centre, 2 = fills the whole view). Keeps the field
    /// from spreading edge-to-edge when the user wants a narrow stream of wind.</summary>
    public static float WindStreakHeight = 0.5f;

    /// <summary>Heat haze on/off — wobbling warm shimmer lines over the bottom of the view.</summary>
    public static bool HeatHazeEnabled;
    public static float HeatHazeStrength = 0.35f;
    /// <summary>Height of the shimmer zone as a fraction of the view (measured from the bottom).</summary>
    public static float HeatHazeHeight = 0.35f;
    public static float HeatHazeSpeed = 1f;
    public static Vector3 HeatHazeColor = new(1f, 0.85f, 0.60f);

    /// <summary>Frost on/off — procedural ice ferns hugging the view edges.</summary>
    public static bool FrostEnabled;
    public static float FrostStrength = 0.85f;
    /// <summary>Pattern density multiplier (1 = default).</summary>
    public static float FrostDensity = 1f;
    public static Vector3 FrostColor = new(0.90f, 0.96f, 1f);

    private static int _frostSeed = 1337;

    /// <summary>Regenerate the frost fern pattern (Effects panel "New pattern").</summary>
    public static void ReshuffleFrost() => _frostSeed = Environment.TickCount;

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
    public const int TexDrop = 3;    // vertical rain streak: elongated core + trailing tail
    public const int TexCoin = 4;    // gold coin (own colors, tint white)
    public const int TexSquare = 5;  // hard-edged pixel square (debris)
    public const int TexRing = 6;    // outline ring (rain splash ripple on the ground)
    public const int TexCrown = 7;   // splash crown: falling drop + rising jet + expanding rings
    public const int TexCloud = 8;   // lumpy cloud mass (fog clusters — reference #1)
    public const int TexWisp = 9;    // short thin ragged streak (mist — reference #2)
    public const int TexWindStreak = 10;  // tailed wind-streak dash (used by lurus and the lurus portion of campur)
    public const int TexWindPuff = 11;    // sparse wind puff/curl (drawn from the mist wisp atlas, WindStreakMode == 1)

    // ═══════════════════════ Procedural texture slots ═══════════════════════
    public const int TexRainLine = 12;   // rain core line — a single thin streak (the raindrop itself).
    public const int TexRainAccent = 13; // smaller sideline tick flicker, no big splash ring.
    // public const int TexRainLineLive = 14; // unused placeholder reserved if needed



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
                if (o is not { IsVisible: true, PrimitiveType: EditorPrimitiveType.Map2D }

) continue;
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
                if (o is not { IsVisible: true, PrimitiveType: EditorPrimitiveType.Effect2D }

) continue;
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
        UpdateLightning(cam, dt);

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
        BuildSpriteRects(mgr); // once per frame — broad-phase for SpriteHitAt
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


                float prevY = p.Y;   // pre-move position: the surface query must start
                float prevX = p.X;   // from where the particle WAS — scanning from the new
                                     // spot (below the surface, or in a column whose slope
                                     // art is higher) finds the NEXT surface down and the
                                     // particle sticks UNDER the slope.
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
                        var (bodyHit, _) = SpriteHitAt(p.X, p.Y, p.Y);
                        if (bodyHit)
                        {
                            SpawnSplash(p.X, p.Y);
                            DrawImpactInk(p.X, p.Y + 0.2f * MathF.Max(0.5f, _cam.OrthoSize) * 0.008f);
                            dead = true;
                        }


                    }


                    // Surface = the HIGHER of the collision ground and any sprite top
                    // under this particle — rain splashes and snow RESTS there.
                    if (!dead)
                    {
                        // Scan the surface from the PREVIOUS position (prevY), not the new
                        // one: on a diagonal slope a flake that crossed the art top this frame
                        // would otherwise find the surface BELOW the slope and stick under it
                        // (the "salju nempel di turunan" bug).
                        float gy = GroundYAtAny(prevX, prevY);
                        var (_, sy) = SpriteHitAt(prevX, p.Y, prevY);
                        if (sy > gy) gy = sy;
                        if (!float.IsNegativeInfinity(gy) && p.Y <= gy + 0.02f)
                        {
                            if (p.Splash)
                            {
                                SpawnSplash(p.X, gy);
                                DrawImpactInk(p.X, gy + 0.2f * MathF.Max(0.5f, _cam.OrthoSize) * 0.008f);
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
                                    p.MaxLife = rest; p.Life = rest;            // fresh fade clock
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
            Preset = preset,
            Rate = 30f,
            Tex = TexGlow,
            LifeMin = 0.35f,
            LifeMax = 0.7f,
            SpeedMin = 0.6f,
            SpeedMax = 1.6f,
            Angle = -MathF.PI * 0.5f,
            Spread = 0.45f,
            SizeMin = 0.14f,
            SizeMax = 0.3f,
            Grow = -0.12f,
            ColorStart = new Vector4(1f, 0.75f, 0.25f, 0.95f),
            ColorEnd = new Vector4(0.9f, 0.15f, 0.02f, 0f),
            WindFactor = 0.35f,
            Z = 0.075f,
        }

,
        // Fireball trail: dense hot core + fast fade (attach to a moving projectile).
        "Fireball" => new EmitConfig
        {
            Preset = preset,
            Rate = 60f,
            Tex = TexGlow,
            LifeMin = 0.2f,
            LifeMax = 0.45f,
            SpeedMin = 0.1f,
            SpeedMax = 0.6f,
            Angle = -MathF.PI * 0.5f,
            Spread = MathF.PI,
            SizeMin = 0.16f,
            SizeMax = 0.34f,
            Grow = -0.3f,
            ColorStart = new Vector4(1f, 0.85f, 0.4f, 1f),
            ColorEnd = new Vector4(1f, 0.25f, 0.02f, 0f),
            WindFactor = 0.1f,
            Z = 0.075f,
        }

,
        // Explosion core flash (burst part 1).
        "ExplosionCore" => new EmitConfig
        {
            Preset = preset,
            Tex = TexGlow,
            LifeMin = 0.15f,
            LifeMax = 0.35f,
            SpeedMin = 1.5f,
            SpeedMax = 4.5f,
            Angle = 0f,
            Spread = MathF.PI,
            SizeMin = 0.25f,
            SizeMax = 0.55f,
            Grow = -0.4f,
            ColorStart = new Vector4(1f, 0.9f, 0.5f, 1f),
            ColorEnd = new Vector4(1f, 0.3f, 0.05f, 0f),
            WindFactor = 0f,
            Z = 0.08f,
        }

,
        // Explosion debris (burst part 2): squares with gravity + spin.
        "ExplosionDebris" => new EmitConfig
        {
            Preset = preset,
            Tex = TexSquare,
            LifeMin = 0.4f,
            LifeMax = 0.9f,
            SpeedMin = 2f,
            SpeedMax = 6f,
            Angle = -MathF.PI * 0.5f,
            Spread = MathF.PI,
            SizeMin = 0.06f,
            SizeMax = 0.16f,
            Gravity = 9f,
            SpinSpeed = 14f,
            ColorStart = new Vector4(0.9f, 0.55f, 0.15f, 1f),
            ColorEnd = new Vector4(0.35f, 0.2f, 0.12f, 0f),
            WindFactor = 0.2f,
            Z = 0.08f,
        }

,
        // Shield-hit sparks: bright radial streaks, gravity, quick fade.
        "Sparks" => new EmitConfig
        {
            Preset = preset,
            Tex = TexSpark,
            AlignVelocity = true,
            AlignVelScale = 3.5f,
            LifeMin = 0.2f,
            LifeMax = 0.5f,
            SpeedMin = 3f,
            SpeedMax = 8f,
            Angle = 0f,
            Spread = MathF.PI,
            SizeMin = 0.05f,
            SizeMax = 0.1f,
            Gravity = 12f,
            ColorStart = new Vector4(1f, 0.95f, 0.55f, 1f),
            ColorEnd = new Vector4(1f, 0.5f, 0.1f, 0f),
            WindFactor = 0.1f,
            Z = 0.08f,
        }

,
        // Coin: spinning gold disks with gravity (drop/pickup bursts).
        "Coin" => new EmitConfig
        {
            Preset = preset,
            Tex = TexCoin,
            CoinSpin = true,
            SpinPhase = 9f,
            LifeMin = 0.7f,
            LifeMax = 1.1f,
            SpeedMin = 2f,
            SpeedMax = 4.5f,
            Angle = -MathF.PI * 0.5f,
            Spread = 0.9f,
            SizeMin = 0.22f,
            SizeMax = 0.32f,
            Gravity = 8f,
            ColorStart = new Vector4(1f, 1f, 1f, 1f),
            ColorEnd = new Vector4(1f, 1f, 1f, 0f),
            WindFactor = 0.05f,
            Z = 0.08f,
        }

,
        // Dust: soft grey motes drifting with the wind (landing/footsteps).
        "Dust" => new EmitConfig
        {
            Preset = preset,
            Tex = TexDot,
            Rate = 10f,
            LifeMin = 0.5f,
            LifeMax = 1.2f,
            SpeedMin = 0.2f,
            SpeedMax = 0.9f,
            Angle = 0f,
            Spread = MathF.PI,
            SizeMin = 0.06f,
            SizeMax = 0.14f,
            ColorStart = new Vector4(0.8f, 0.78f, 0.7f, 0.7f),
            ColorEnd = new Vector4(0.6f, 0.58f, 0.52f, 0f),
            WindFactor = 0.8f,
            Z = 0.07f,
        }

,
        // Smoke: dark puffs rising, growing, fading.
        "Smoke" => new EmitConfig
        {
            Preset = preset,
            Tex = TexGlow,
            Rate = 14f,
            LifeMin = 0.8f,
            LifeMax = 1.6f,
            SpeedMin = 0.4f,
            SpeedMax = 1.1f,
            Angle = -MathF.PI * 0.5f,
            Spread = 0.5f,
            SizeMin = 0.2f,
            SizeMax = 0.4f,
            Grow = 0.35f,
            ColorStart = new Vector4(0.35f, 0.35f, 0.38f, 0.55f),
            ColorEnd = new Vector4(0.2f, 0.2f, 0.22f, 0f),
            WindFactor = 0.6f,
            Z = 0.07f,
        }

,
        // Snow: slow white flakes, strongly wind-driven.
        "Snow" => new EmitConfig
        {
            Preset = preset,
            Tex = TexDot,
            Rate = 25f,
            LifeMin = 2.5f,
            LifeMax = 4f,
            SpeedMin = 0.8f,
            SpeedMax = 1.6f,
            Angle = -MathF.PI * 0.5f,
            Spread = 0.35f,
            SizeMin = 0.05f,
            SizeMax = 0.11f,
            ColorStart = new Vector4(1f, 1f, 1f, 0.9f),
            ColorEnd = new Vector4(1f, 1f, 1f, 0f),
            WindFactor = 1.2f,
            Z = 0.075f,
        }

,
        _ => PresetConfig("Fire"),
    }

;

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
            Life = life,
            MaxLife = life,
            Size = Lerp(c.SizeMin, c.SizeMax),
            SizeY = 0f,
            Grow = c.Grow,
            Rot = c.AlignVelocity ? 0f : (float)(_rng.NextDouble() * MathF.Tau),
            Spin = (float)(_rng.NextDouble() * 2 - 1) * c.SpinSpeed,
            Phase = 0f,
            SpinPhase = c.CoinSpin
                ? (c.SpinPhase > 0f ? c.SpinPhase : c.SpinSpeed) * (0.7f + (float)_rng.NextDouble() * 0.6f)
                : 0f,
            Tex = c.Tex,
            Wind = c.WindFactor,
            C0 = c.ColorStart,
            C1 = c.ColorEnd,
            Gravity = c.Gravity,
            Drag = c.Drag,
            AlignVel = c.AlignVelocity,
            AlignVelScale = c.AlignVelScale,
            Coin = c.CoinSpin,
        }

);
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
            float life = 30f;            float len = u * (0.05f + (float)_rng.NextDouble() * 0.06f); // varied streak lengths
            float wid = MathF.Max(0.02f, u * 0.003f);

            _particles.Add(new P
            {
                X = x,
                Y = y,
                Z = 0.075f,
                VX = vx,
                VY = vy,
                Life = life,
                MaxLife = life,
                Size = wid,
                SizeY = len,
                // SOLID square texture: crisp dashes like the reference. The drop
                // texture's thin streak core was sampling to near-nothing at this size.
                Tex = TexSquare, Wind = RainWindFactor * 0.25f,
                C0 = new Vector4(0.82f, 0.96f, 1f, 0.9f),
                C1 = new Vector4(0.82f, 0.96f, 1f, 0.9f),
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
                X = x,
                Y = y,
                Z = 0.075f,
                VX = ((float)_rng.NextDouble() - 0.5f) * 0.4f,
                VY = -(1.2f + (float)_rng.NextDouble() * 0.9f),
                Life = life,
                MaxLife = life,
                Size = u * (0.008f + (float)_rng.NextDouble() * 0.008f),
                Grow = u * 0.001f,
                Tex = TexDot,
                Wind = 0.9f,
                // PURE WHITE + fully opaque while alive (user: snow reads bluish/
                // translucent over the blue fog backdrop). Fade only at death via
                // the C1 alpha-0 lerp; no blue tint anywhere.
                C0 = new Vector4(1f, 1f, 1f, 1f),
                C1 = new Vector4(1f, 1f, 1f, 0.85f),
                GroundKill = 1f,
            }

);
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
        if (!RainEnabled && !SnowEnabled && !FogEnabled && !TintEnabled && !MistEnabled
            && !LightningEnabled && !GodRaysEnabled && !HeatHazeEnabled && !FrostEnabled
            && !WindStreaksEnabled) return;
        _dt = dt; _cam = cam; _map = map; _mgr = mgr;
        TrackCameraTravel(cam, dt);
        BuildSurfaceMaps(mgr, map);
        _weatherClock += dt;
        if (RainEnabled) SpawnRain(dt);
        if (SnowEnabled) SpawnSnow(dt);
        UpdateLightning(cam, dt);
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
        MistEnabled = false;
        switch (preset)
        {
            case "Clear Day":
                RainEnabled = false; SnowEnabled = false; FogEnabled = false;
                TintEnabled = true; TintColor = new Vector3(1f, 1f, 0.92f); TintStrength = 0.10f;
                break;
            case "Sunset":
                RainEnabled = false; SnowEnabled = false; FogEnabled = false;
                TintEnabled = true; TintColor = new Vector3(1f, 0.45f, 0.20f); TintStrength = 0.35f;
                break;
            case "Rain":
                RainEnabled = true; SnowEnabled = false; FogEnabled = true;
                FogCoverage = 0.45f; FogPuffs = 7; FogPuffSize = 1.10f;
                FogOpacity = 0.20f * fogDensity; FogBaseHaze = 0.30f;
                TintEnabled = true; TintColor = new Vector3(0.45f, 0.55f, 0.75f); TintStrength = 0.30f;
                break;
            case "Storm":
                RainEnabled = true; SnowEnabled = false; FogEnabled = true;
                FogCoverage = 0.75f; FogPuffs = 16; FogPuffSize = 1.35f;
                FogOpacity = 0.34f * fogDensity; FogBaseHaze = 0.40f;
                TintEnabled = true; TintColor = new Vector3(0.30f, 0.36f, 0.50f); TintStrength = 0.45f;
                WindX = MathF.Max(WindX, 3f);
                break;
            case "Snow":
                RainEnabled = false; SnowEnabled = true; FogEnabled = true;
                FogCoverage = 0.35f; FogPuffs = 7; FogPuffSize = 1.10f;
                FogOpacity = 0.16f * fogDensity; FogBaseHaze = 0.26f;
                TintEnabled = true; TintColor = new Vector3(0.80f, 0.88f, 1f); TintStrength = 0.22f;
                break;
            case "Fog":
                // Fog = the HEAVY layer: big lumpy cloud masses over a low base haze.
                RainEnabled = false; SnowEnabled = false; FogEnabled = true; MistEnabled = false;
                FogCoverage = 0.75f; FogPuffs = 12; FogPuffSize = 1.30f;
                FogOpacity = 0.30f * fogDensity; FogBaseHaze = 0.35f; FogSpeed = 1f;
                FogColor = new Vector3(0.72f, 0.74f, 0.78f); FogUseTint = true;
                TintEnabled = false;
                break;
            case "Mist":
                // Mist = the THIN layer: many small, short wispy streaks hugging the ground.
                RainEnabled = false; SnowEnabled = false; FogEnabled = false;
                MistEnabled = true;
                MistWisps = 56; MistHeight = 0.20f; MistLength = 0.22f; MistThickness = 0.030f;
                MistOpacity = 0.55f * fogDensity; MistSpeed = 1f;
                MistColor = new Vector3(0.85f, 0.88f, 0.92f); MistUseTint = true;
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
        MistEnabled = false;
        TintEnabled = false;
        LightningEnabled = false;
        GodRaysEnabled = false;
        WindStreaksEnabled = false;
        WindStreakMode = 0;
        HeatHazeEnabled = false;
        FrostEnabled = false;
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

        // A weather WASH is an atmosphere overlay, not a lit surface: keep its color.
        SpriteLighting2D.ApplyUnlit(_shader);

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
        => OverlayQuadGrad(x0, y0, x1, y1, z, col, col);

    /// <summary>Overlay quad with a VERTICAL colour/alpha ramp (bottom edge → top edge).
    /// This is what makes fog bands and the mist veil soft instead of hard-edged slabs:
    /// the fragment colour is interpolated per vertex, so one quad can fade 0 → alpha → 0.
    /// Same 54-float scratch + corner order as <see cref="OverlayQuad"/>.</summary>
    private static unsafe void OverlayQuadGrad(float x0, float y0, float x1, float y1, float z,
        Vector4 colBottom, Vector4 colTop)
    {
        var buf = _overlayBuf;
        int o = 0;
        // BL, BR, TR, BL, TR, TL
        buf[o++] = x0; buf[o++] = y0; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
        buf[o++] = colBottom.X; buf[o++] = colBottom.Y; buf[o++] = colBottom.Z; buf[o++] = colBottom.W;
        buf[o++] = x1; buf[o++] = y0; buf[o++] = z; buf[o++] = 1f; buf[o++] = 0f;
        buf[o++] = colBottom.X; buf[o++] = colBottom.Y; buf[o++] = colBottom.Z; buf[o++] = colBottom.W;
        buf[o++] = x1; buf[o++] = y1; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
        buf[o++] = colTop.X; buf[o++] = colTop.Y; buf[o++] = colTop.Z; buf[o++] = colTop.W;
        buf[o++] = x0; buf[o++] = y0; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
        buf[o++] = colBottom.X; buf[o++] = colBottom.Y; buf[o++] = colBottom.Z; buf[o++] = colBottom.W;
        buf[o++] = x1; buf[o++] = y1; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
        buf[o++] = colTop.X; buf[o++] = colTop.Y; buf[o++] = colTop.Z; buf[o++] = colTop.W;
        buf[o++] = x0; buf[o++] = y1; buf[o++] = z; buf[o++] = 0f; buf[o++] = 1f;
        buf[o++] = colTop.X; buf[o++] = colTop.Y; buf[o++] = colTop.Z; buf[o++] = colTop.W;
        fixed (float* p = buf)
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(54 * sizeof(float)), p, Const.GL_DYNAMIC_DRAW);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
    }



    /// <summary>Pure-white, fully opaque texture slot used by the soft gradients (the
    /// texture only shapes coverage, the vertex colour carries the haze itself).</summary>
    private static void BindOverlayTex()
        => GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[TexSquare]);



    /// <summary>THICK FOG drawn IN FRONT of the world (reference #1: big soft cloud masses,
    /// not bands): N large soft puffs drifting with the wind over a low base haze, so the
    /// scene reads through a heavy cloud bank. This is the HEAVY layer — user: fog = tebal,
    /// mist = tipis kecil-kecil. Drawn before the mist wisps.</summary>
    public static unsafe void RenderFogFront(Camera camera)
    {
        if (!FogEnabled || FogOpacity <= 0.001f) return;
        EnsureResources();
        if (_shader == 0) return;
        float halfH = MathF.Max(0.5f, camera.OrthoSize);
        float halfW = halfH * camera.GetAspect();
        float cx = camera.Position.X, cy = camera.Position.Y;
        float t = _weatherClock;
        float a = Math.Clamp(FogOpacity, 0f, 1f);
        int puffs = Math.Clamp(FogPuffs, 1, 32);
        float sizeMul = Math.Clamp(FogPuffSize, 0.1f, 2f);
        float coverage = Math.Clamp(FogCoverage, 0.05f, 1f);
        float speed = Math.Clamp(FogSpeed, 0f, 4f);
        float windK = MathF.Abs(WindX);
        float dir = WindX < -0.01f ? -1f : 1f;

        // Colour is usually the authored grey (reference #1); an active Tint nudges it so
        // fog also reads correctly in a sunset/snow/storm mood.
        Vector3 col = FogColor;
        if (FogUseTint && TintEnabled && TintStrength > 0.001f)
            col = Vector3.Lerp(col, TintColor, 0.35f);

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

        SpriteLighting2D.ApplyUnlit(_shader); // front fog: an overlay, never a lit surface

        GL.BindVertexArray(_vao);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
        GL.ActiveTexture(Const.GL_TEXTURE0);
        BindOverlayTex();

        // ── Base haze: fills the gaps between puffs so the bank reads as THICK fog ──
        float yb = cy - halfH;
        float zone = 2f * halfH * coverage;
        float x0 = cx - halfW * 1.2f, x1 = cx + halfW * 1.2f;
        float baseA = a * Math.Clamp(FogBaseHaze, 0f, 1f) * 0.6f;
        if (baseA > 0.002f)
        {
            BindOverlayTex();
            OverlayQuadGrad(x0, yb, x1, yb + zone * 0.72f, 0.0205f,
                new Vector4(col, baseA), new Vector4(col, baseA));
            OverlayQuadGrad(x0, yb + zone * 0.72f, x1, yb + zone, 0.0205f,
                new Vector4(col, baseA), new Vector4(col, 0f));
        }



        // ── Cloud masses: big lumpy clusters drifting with the wind (reference #1) ──
        // Each cluster stamps a core mass plus satellite lobes, so the field reads as
        // HEAVY cloud — this is what separates fog from mist (which is small wisps).
        GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[TexCloud]);
        float span = halfW * 3.2f;
        for (int i = 0; i < puffs; i++)
        {
            float h1 = Hash01(i * 71 + 13), h2 = Hash01(i * 37 + 5), h3 = Hash01(i * 53 + 29);
            float size = halfH * (0.52f + 0.78f * h1) * sizeMul;
            float drift = (0.35f + windK * 0.22f) * dir * (0.6f + 0.8f * h2) * speed;
            float travel = h3 * span + t * drift * halfH;
            float x = cx - halfW * 1.6f + ((travel % span) + span) % span;
            float y = yb + zone * (0.10f + 0.78f * h2)
                    + MathF.Sin(t * (0.12f + 0.10f * h3) * speed + i * 1.7f) * halfH * 0.045f;
            float alpha = Math.Clamp(a * (0.60f + 0.40f * h1)
                * (0.85f + 0.15f * MathF.Sin(t * 0.25f * speed + i * 2.3f)), 0f, 1f);
            EmitCloudMass(x, y, size, alpha, col, 0.021f);
        }



        GL.BindVertexArray(0);
        if (cull) GL.Enable(Const.GL_CULL_FACE);
        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
        GL.UseProgram(Shader.GetShaderProgram());
    }



    /// <summary>One drifting cloud mass: a wide core + three satellite lobes. The lobes are
    /// what make the silhouette lumpy like reference #1 — a single quad still reads as one
    /// soft blob no matter which texture it uses. Offsets are fixed so the shape is stable
    /// frame to frame; only the size/alpha vary per cluster.</summary>
    private static void EmitCloudMass(float x, float y, float size, float alpha, Vector3 col, float z)
    {
        OverlayQuad(x - size, y - size * 0.58f, x + size, y + size * 0.58f, z,
            new Vector4(col, alpha));
        OverlayQuad(x - size * 1.45f, y - size * 0.34f, x - size * 0.10f, y + size * 0.40f, z,
            new Vector4(col, alpha * 0.82f));
        OverlayQuad(x + size * 0.15f, y - size * 0.26f, x + size * 1.35f, y + size * 0.48f, z,
            new Vector4(col, alpha * 0.74f));
        OverlayQuad(x - size * 0.60f, y + size * 0.06f, x + size * 0.58f, y + size * 1.02f, z,
            new Vector4(col, alpha * 0.55f));
    }



    /// <summary>THIN MIST drawn after the fog bank (reference #2): many small, short wisps
    /// hugging the ground — each one a taut ragged streak, never a band across the view.
    /// Deliberately not a cloud bank (that is fog's job). Drifts with the wind.</summary>
    public static unsafe void RenderMistFront(Camera camera)
    {
        if (!MistEnabled || MistOpacity <= 0.001f) return;
        EnsureResources();
        if (_shader == 0) return;

        float halfH = MathF.Max(0.5f, camera.OrthoSize);
        float halfW = halfH * camera.GetAspect();
        float cx = camera.Position.X, cy = camera.Position.Y;
        float t = _weatherClock;
        int wisps = Math.Clamp(MistWisps, 1, 96);
        float zone = 2f * halfH * Math.Clamp(MistHeight, 0.02f, 1f);
        float len = halfW * Math.Clamp(MistLength, 0.02f, 0.60f);
        float thick = halfH * Math.Clamp(MistThickness, 0.004f, 0.08f);
        float a = Math.Clamp(MistOpacity, 0f, 1f);
        float speed = Math.Clamp(MistSpeed, 0f, 4f);

        Vector3 col = MistColor;
        if (MistUseTint && TintEnabled && TintStrength > 0.001f)
            col = Vector3.Lerp(col, TintColor, 0.45f);

        // Wisps move in the same direction as the wind streaks; stronger wind = faster.
        float dir = WindX < -0.01f ? -1f : 1f;
        float windK = 1f + MathF.Abs(WindX) * Math.Clamp(MistWindDrift, 0f, 1f);

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

        SpriteLighting2D.ApplyUnlit(_shader); // mist is atmosphere, never a lit surface

        GL.BindVertexArray(_vao);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
        GL.ActiveTexture(Const.GL_TEXTURE0);
        BindOverlayTex();

        // ── Base veil: a soft gradient band filling the wisp zone so the mist reads as a
        //    continuous bank (the reference is a dense fluffy mass, not isolated slivers).
        //    Drawn FIRST; the wisps on top add the moving texture.
        float veil = a * 0.50f;
        if (veil > 0.003f)
        {
            BindOverlayTex();
            float yb0 = cy - halfH;
            OverlayQuadGrad(cx - halfW * 1.1f, yb0, cx + halfW * 1.1f, yb0 + zone,
                0.0218f, new Vector4(col, veil), new Vector4(col, veil * 0.25f));
        }



        // ── Small wispy streaks near the ground (reference #2) ──
        // Each wisp is one SHORT, thin TexWisp quad: many small pieces, never a band that
        // spans the screen. Sparse-ish and low alpha by design — mist is the light layer.
        GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[TexWisp]);
        float yb = cy - halfH;
        float span = halfW * 2.2f;
        for (int i = 0; i < wisps; i++)
        {
            float h1 = Hash01(i * 43 + 7), h2 = Hash01(i * 67 + 19), h3 = Hash01(i * 29 + 3);
            float w = len * (0.55f + 0.85f * h1);
            float drift = (0.6f + windK * 0.8f) * dir * (0.7f + 0.6f * h2) * speed;
            float travel = h3 * span + t * drift * halfW * 0.35f;
            float x = cx - halfW * 1.10f + ((travel % span) + span) % span;
            // Most wisps sit in the low ground band; a few strays drift higher, like the
            // reference sheet where loose wisps float above the dense row.
            float stray = h3 > 0.82f ? 2.6f : 1f;
            float y = yb + zone * (0.06f + 0.86f * h2) * stray
                    + MathF.Sin(t * 0.5f * speed + i * 1.9f) * zone * 0.10f;
            float alpha = Math.Clamp(a * (0.45f + 0.55f * h3)
                * (0.70f + 0.30f * MathF.Sin(t * 0.30f * speed + i)), 0f, 1f);
            float th = thick * (0.7f + 0.8f * h1);
            OverlayQuad(x - w, y - th, x + w, y + th, 0.022f, new Vector4(col, alpha));
        }



        GL.BindVertexArray(0);
        if (cull) GL.Enable(Const.GL_CULL_FACE);
        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
        GL.UseProgram(Shader.GetShaderProgram());
    }



    // ═══════════════════════ Pixel atmosphere FX ═══════════════════════

    /// <summary>Deterministic 0..1 hash — keeps procedural shapes STABLE across frames
    /// (Random() per frame would make rays/wind/frost flicker).</summary>
    private static float Hash01(int i)
    {
        uint h = (uint)i * 2654435761u;
        h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
        return (h & 0x00FFFFFF) / 16777215f;
    }

    /// <summary>Non-negative floating remainder: result in [0, m) for m > 0. Used by the
    /// wind/puff wrap so it keeps working when the compiler's % emits CS0103 on floats.</summary>
    private static float FloatMod(float x, float m)
    {
        float r = x % m;
        return r >= 0f ? r : r + m;
    }



    /// <summary>Rotated solid quad (centre, half extents, radians). Used by the god rays
    /// (long thin beams) and by every segment writer below.</summary>
    private static unsafe void OverlayQuadRot(float cx, float cy, float hw, float hh,
        float angle, float z, Vector4 col)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        float ax = cx - hw * c + hh * s, ay = cy - hw * s - hh * c; // BL
        float bx = cx + hw * c + hh * s, by = cy + hw * s - hh * c; // BR
        float dx = cx + hw * c - hh * s, dy = cy + hw * s + hh * c; // TR
        float ex = cx - hw * c - hh * s, ey = cy - hw * s + hh * c; // TL
        var buf = _overlayBuf;
        int o = 0;
        buf[o++] = ax; buf[o++] = ay; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = bx; buf[o++] = by; buf[o++] = z; buf[o++] = 1f; buf[o++] = 0f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = dx; buf[o++] = dy; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = ax; buf[o++] = ay; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = dx; buf[o++] = dy; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = ex; buf[o++] = ey; buf[o++] = z; buf[o++] = 0f; buf[o++] = 1f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        fixed (float* p = buf)
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(54 * sizeof(float)), p, Const.GL_DYNAMIC_DRAW);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
    }



    // ── Batched line/segment writer ──
    // Frost alone is ~1000 short segments; one draw call per segment would hammer the
    // driver. Everything stroke-shaped (bolts, frost ferns, wind streaks, shimmer lines)
    // appends here and flushes once per pass.
    private static readonly float[] _strokeBuf = new float[54 * 4096];
    private static int _strokeUsed;

    /// <summary>Append one thick segment (rotated quad) to the stroke batch.</summary>
    private static void Stroke(float x0, float y0, float x1, float y1,
        float thickness, float z, Vector4 col)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.0001f) return;
        if (_strokeUsed + 54 > _strokeBuf.Length) FlushStrokes();

        float hw = len * 0.5f, hh = MathF.Max(0.0001f, thickness * 0.5f);
        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
        float ang = MathF.Atan2(dy, dx);
        float c = MathF.Cos(ang), s = MathF.Sin(ang);
        float ax = cx - hw * c + hh * s, ay = cy - hw * s - hh * c; // BL
        float bx = cx + hw * c + hh * s, by = cy + hw * s - hh * c; // BR
        float dx2 = cx + hw * c - hh * s, dy2 = cy + hw * s + hh * c; // TR
        float ex = cx - hw * c - hh * s, ey = cy - hw * s + hh * c; // TL

        var buf = _strokeBuf;
        int o = _strokeUsed;
        buf[o++] = ax; buf[o++] = ay; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = bx; buf[o++] = by; buf[o++] = z; buf[o++] = 1f; buf[o++] = 0f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = dx2; buf[o++] = dy2; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = ax; buf[o++] = ay; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = dx2; buf[o++] = dy2; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        buf[o++] = ex; buf[o++] = ey; buf[o++] = z; buf[o++] = 0f; buf[o++] = 1f;
        buf[o++] = col.X; buf[o++] = col.Y; buf[o++] = col.Z; buf[o++] = col.W;
        _strokeUsed = o;
    }



    /// <summary>Upload + draw the accumulated strokes in ONE call, then clear the batch.</summary>
    private static unsafe void FlushStrokes()
    {
        if (_strokeUsed == 0) return;
        fixed (float* p = _strokeBuf)
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(_strokeUsed * sizeof(float)), p, Const.GL_DYNAMIC_DRAW);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, _strokeUsed / 9);
        _strokeUsed = 0;
    }



    /// <summary>Shared overlay pass setup for the pixel FX renderers (shader, matrices,
    /// blend, unlit, VAO/VBO, solid texture). Restore with <see cref="EndOverlay"/>.</summary>
    private static unsafe void BeginOverlay(Camera camera, out bool cull, out bool depth)
    {
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
        cull = GL.IsEnabled(Const.GL_CULL_FACE);
        GL.Disable(Const.GL_CULL_FACE);
        depth = GL.IsEnabled(Const.GL_DEPTH_TEST);
        GL.Disable(Const.GL_DEPTH_TEST); // frontmost overlays — draw order decides

        SpriteLighting2D.ApplyUnlit(_shader);

        GL.BindVertexArray(_vao);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
        GL.ActiveTexture(Const.GL_TEXTURE0);
        BindOverlayTex();
    }



    private static void EndOverlay(bool cull, bool depth)
    {
        GL.BindVertexArray(0);
        if (cull) GL.Enable(Const.GL_CULL_FACE);
        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
        GL.UseProgram(Shader.GetShaderProgram());
    }



    // ── Lightning state ──
    private static readonly List<(Vector2 A, Vector2 B)> _boltSegs = new();
    private static readonly List<(Vector2 A, Vector2 B)> _boltBranchSegs = new();
    private const float StrikeMaxLife = 0.45f;
    private static float _strikeLife;
    private static float _nextStrikeIn;
    private static bool _strikeRequested;

    /// <summary>Force a strike on the next tick (Effects panel "Strike now").</summary>
    public static void StrikeLightning() => _strikeRequested = true;

    /// <summary>Advance the strike timer. Called from BOTH the gameplay tick and the
    /// editor weather tick so lightning also fires while editing.</summary>
    private static void UpdateLightning(Camera? cam, float dt)
    {
        if (!LightningEnabled)
        {
            _boltSegs.Clear();
            _boltBranchSegs.Clear();
            _strikeLife = 0f;
            _nextStrikeIn = 0f;
            _strikeRequested = false;
            return;
        }


        _nextStrikeIn -= dt;
        bool due = LightningInterval > 0.05f && _nextStrikeIn <= 0f;
        if (_strikeRequested || due)
        {
            if (cam != null) GenerateBolt(cam);
            _strikeRequested = false;
            float iv = MathF.Max(0.4f, LightningInterval);
            _nextStrikeIn = iv * (0.7f + (float)_rng.NextDouble() * 0.6f);
        }


        if (_strikeLife > 0f) _strikeLife = MathF.Max(0f, _strikeLife - dt);
    }



    /// <summary>Build one jagged bolt from above the view down to the weather ground,
    /// plus short forks. Stored in world space so it stays put if the camera moves.</summary>
    private static void GenerateBolt(Camera camera)
    {
        float halfH = MathF.Max(0.5f, camera.OrthoSize);
        float halfW = halfH * camera.GetAspect();
        float cx = camera.Position.X, cy = camera.Position.Y;
        float x = cx + ((float)_rng.NextDouble() * 2f - 1f) * halfW * 0.60f;
        float yTop = cy + halfH * 1.25f;
        float yEnd = cy - halfH * 1.05f;                       // default: off the bottom
        float ground = GroundYAtAny(x, yTop);
        if (!float.IsNegativeInfinity(ground) && ground < yTop - 0.1f && ground < cy + halfH)
            yEnd = ground - halfH * 0.02f;                     // strike INTO the surface

        _boltSegs.Clear();
        _boltBranchSegs.Clear();

        const int steps = 16;
        float stepY = (yEnd - yTop) / steps;
        float px = x;
        for (int i = 0; i < steps; i++)
        {
            float py = yTop + stepY * i;
            float nx = px + ((float)_rng.NextDouble() * 2f - 1f) * halfW * 0.05f;
            float ny = py + stepY;
            _boltSegs.Add((new Vector2(px, py), new Vector2(nx, ny)));

            // Forks: thin secondary strikes peeling off the main channel.
            if (i > 2 && i < steps - 1 && _rng.NextDouble() < 0.22)
            {
                float bx = nx, by = ny;
                float dir = _rng.NextDouble() < 0.5 ? -1f : 1f;
                int bsteps = 2 + _rng.Next(3);
                for (int b = 0; b < bsteps; b++)
                {
                    float nbx = bx + dir * halfW * (0.02f + (float)_rng.NextDouble() * 0.03f);
                    float nby = by - MathF.Abs(stepY) * (0.4f + (float)_rng.NextDouble() * 0.5f);
                    _boltBranchSegs.Add((new Vector2(bx, by), new Vector2(nbx, nby)));
                    bx = nbx; by = nby;
                }


            }


            px = nx;
        }


        _strikeLife = StrikeMaxLife;
    }



    /// <summary>Draw the live bolt: sky flash → wide dim halo → bright core → forks →
    /// ground glow. Strobed so a strike reads as 2-3 pulses, not one fade.</summary>
    private static unsafe void DrawLightning(float cx, float cy, float halfW, float halfH)
    {
        if (_boltSegs.Count == 0 || _strikeLife <= 0f) return;
        float k = Math.Clamp(_strikeLife / StrikeMaxLife, 0f, 1f);
        float flick = 0.35f + 0.65f * MathF.Abs(MathF.Sin(k * 22f));
        float alpha = Math.Clamp(k * flick * 1.6f, 0f, 1f);
        float th = MathF.Max(0.004f, halfH * Math.Clamp(LightningThickness, 0.005f, 0.30f));
        const float z = 0.028f;

        // Sky flash first so the bolt stays crisp on top of its own wash.
        float flash = Math.Clamp(LightningFlash, 0f, 1f) * alpha * 0.30f;
        if (flash > 0.002f)
            OverlayQuad(cx - halfW * 1.2f, cy - halfH * 1.2f,
                        cx + halfW * 1.2f, cy + halfH * 1.2f, z - 0.0004f,
                        new Vector4(0.85f, 0.90f, 1f, flash));

        var glow = new Vector4(LightningGlowColor, alpha * 0.30f);
        foreach (var (a, b) in _boltSegs)
            Stroke(a.X, a.Y, b.X, b.Y, th * 3.2f, z, glow);
        FlushStrokes();

        var core = new Vector4(LightningColor, alpha);
        foreach (var (a, b) in _boltSegs)
            Stroke(a.X, a.Y, b.X, b.Y, th, z + 0.0004f, core);
        var fork = new Vector4(LightningColor, alpha * 0.75f);
        foreach (var (a, b) in _boltBranchSegs)
            Stroke(a.X, a.Y, b.X, b.Y, th * 0.45f, z + 0.0002f, fork);
        FlushStrokes();

        // Ground hit glow (soft texture) + restore the solid overlay texture.
        var end = _boltSegs[^1].B;
        GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[TexGlow]);
        float gr = halfH * 0.20f;
        OverlayQuad(end.X - gr, end.Y - gr * 0.55f, end.X + gr, end.Y + gr * 0.55f,
            z + 0.0006f, new Vector4(LightningColor, alpha * 0.55f));
        BindOverlayTex();
    }



    /// <summary>Sun shafts: a fan of long rotated quads converging above the view, with a
    /// slow shimmer. Hard edges + low alpha = the pixel-art ray look.</summary>
    private static unsafe void DrawGodRays(float cx, float cy, float halfW, float halfH)
    {
        float strength = Math.Clamp(GodRaysStrength, 0f, 1f);
        if (strength <= 0.001f) return;
        int n = Math.Clamp(GodRaysCount, 1, 24);
        float spread = Math.Clamp(GodRaysSpread, 0f, 160f) * MathF.PI / 180f;
        float centre = GodRaysAngle * MathF.PI / 180f;
        float diag = MathF.Sqrt(halfW * halfW + halfH * halfH);
        float len = diag * Math.Clamp(GodRaysLength, 0.5f, 4f);
        float t = _weatherClock;
        float ox = cx + halfW * 0.25f;
        float oy = cy + halfH * 1.10f;
        const float z = 0.024f;

        for (int i = 0; i < n; i++)
        {
            float f = n == 1 ? 0.5f : i / (float)(n - 1);
            float ang = centre + (f - 0.5f) * spread;
            float h1 = Hash01(i * 37 + 11), h2 = Hash01(i * 91 + 5);
            float w = halfH * (0.06f + 0.16f * h1);
            float shimmer = 0.55f + 0.45f * MathF.Sin(t * (0.25f + 0.25f * h2) + i * 1.7f);
            float a = strength * shimmer * (0.5f + 0.5f * h2);
            float c = MathF.Cos(ang), s = MathF.Sin(ang);
            OverlayQuadRot(ox + c * len * 0.5f, oy + s * len * 0.5f,
                len * 0.5f, w * 0.5f, ang, z, new Vector4(GodRaysColor, a));
        }

        // Bloom at the source so the beams read as one light, not detached sticks.
        GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[TexGlow]);
        float br = halfH * 0.30f;
        OverlayQuad(ox - br, oy - br, ox + br, oy + br, z + 0.0002f,
            new Vector4(GodRaysColor, strength * 0.35f));
        BindOverlayTex();
    }

    private static unsafe void DrawWindStreaks(float cx, float cy, float halfW, float halfH)
    {
        float dir = WindX < -0.01f ? -1f : 1f;
        float windK = MathF.Abs(WindX);
        float t = _weatherClock;
        const float z = 0.023f;
        GL.ActiveTexture(Const.GL_TEXTURE0);
        // Modes 1/2: slynyrd-style procedural swirl field. Marks are flat vertex-coloured
        // ribbons, so the solid white overlay texture is bound (texture = coverage only).
        if (WindStreakMode == 1 || WindStreakMode == 2)
        {
            BindOverlayTex();
            DrawWindWispField(cx, cy, halfW, halfH, dir, windK, t, z, WindStreakMode == 2);
            return;
        }

        GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[TexWindStreak]);
        DrawWindStreaksLine(cx, cy, halfW, halfH, dir, windK, t, z);
    }

    private static unsafe void DrawWindStreaksLine(
        float cx, float cy, float halfW, float halfH,
        float dir, float windK, float t, float z)
    {
        int n = (int)Math.Clamp(24f * Math.Clamp(WindStreakAmount, 0.1f, 4f), 1, 140);
        float speed = (2.4f + MathF.Abs(WindX) * 1.8f) * Math.Clamp(WindStreakSpeed, 0f, 5f);
        float span = halfW * 2.8f;
        float lenBase = halfW * (0.16f + 0.20f * windK);
        float hhBase = halfH * (0.010f + 0.006f * windK);
        float a = 0.22f;
        float bandH = halfH * Math.Clamp(WindStreakHeight, 0.1f, 2f);

        for (int i = 0; i < n; i++)
        {
            float h1 = Hash01(i * 41 + 11), h2 = Hash01(i * 53 + 7), h3 = Hash01(i * 71 + 23);
            // Wind Height band + a gentle vertical wave so the line field is animated too.
            float y = cy + (h2 - 0.5f) * bandH
                + MathF.Sin(t * 1.2f + i * 1.7f) * bandH * 0.04f;
            float aLine = a * (0.8f + 0.2f * MathF.Sin(t * 1.5f + i * 2.1f));
            float travel = h3 * span + dir * t * speed;
            float x = cx - halfW * 1.4f + FloatMod(travel, span);
            float ang = dir > 0f ? 0f : MathF.PI;
            float hw = lenBase * (0.7f + 0.3f * h3);
            float hh2 = hhBase * (0.8f + 0.2f * h1);

            float ax = x - hw * MathF.Cos(ang) + hh2 * MathF.Sin(ang);
            float ay = y - hw * MathF.Sin(ang) - hh2 * MathF.Cos(ang);
            float bx = x + hw * MathF.Cos(ang) + hh2 * MathF.Sin(ang);
            float by = y + hw * MathF.Sin(ang) - hh2 * MathF.Cos(ang);
            float dx = x + hw * MathF.Cos(ang) - hh2 * MathF.Sin(ang);
            float dy = y + hw * MathF.Sin(ang) + hh2 * MathF.Cos(ang);
            float ex = x - hw * MathF.Cos(ang) - hh2 * MathF.Sin(ang);
            float ey = y - hw * MathF.Sin(ang) + hh2 * MathF.Cos(ang);

            var buf = _overlayBuf;
            int o = 0;
            buf[o++] = ax; buf[o++] = ay; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
            buf[o++] = WindStreakColor.X; buf[o++] = WindStreakColor.Y; buf[o++] = WindStreakColor.Z; buf[o++] = aLine;
            buf[o++] = bx; buf[o++] = by; buf[o++] = z; buf[o++] = 1f; buf[o++] = 0f;
            buf[o++] = WindStreakColor.X; buf[o++] = WindStreakColor.Y; buf[o++] = WindStreakColor.Z; buf[o++] = aLine;
                buf[o++] = dx; buf[o++] = dy; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
                buf[o++] = WindStreakColor.X; buf[o++] = WindStreakColor.Y; buf[o++] = WindStreakColor.Z; buf[o++] = aLine;
                buf[o++] = ax; buf[o++] = ay; buf[o++] = z; buf[o++] = 0f; buf[o++] = 0f;
                buf[o++] = WindStreakColor.X; buf[o++] = WindStreakColor.Y; buf[o++] = WindStreakColor.Z; buf[o++] = aLine;
                buf[o++] = dx; buf[o++] = dy; buf[o++] = z; buf[o++] = 1f; buf[o++] = 1f;
                buf[o++] = WindStreakColor.X; buf[o++] = WindStreakColor.Y; buf[o++] = WindStreakColor.Z; buf[o++] = aLine;
                buf[o++] = ex; buf[o++] = ey; buf[o++] = z; buf[o++] = 0f; buf[o++] = 1f;
                buf[o++] = WindStreakColor.X; buf[o++] = WindStreakColor.Y; buf[o++] = WindStreakColor.Z; buf[o++] = aLine;
                fixed (float* p = buf)
                    GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(54 * sizeof(float)), p, Const.GL_DYNAMIC_DRAW);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            }
        }

    /// <summary>SLYNYRD-style wind field: wind is read through SWIRLS — comma-shaped marks
    /// whose heading bends progressively from head to tail, so each mark hooks around at the
    /// head and thins + fades into a streamed tail (ref: slynyrd pixelblog-33 wind effects).
    /// Species: hooked curl (most), lazy S-wave streak, a tiny dust speck; in campur mode a
    /// few plain lurus dashes are mixed in so the field never looks uniform.</summary>
    private static unsafe void DrawWindWispField(float cx, float cy, float halfW, float halfH,
        float dir, float windK, float t, float z, bool mixStraight)
    {
        int n = (int)Math.Clamp(16f * Math.Clamp(WindStreakAmount, 0.1f, 4f), 1, 70);
        float speed = (2.0f + MathF.Abs(WindX) * 1.6f) * Math.Clamp(WindStreakSpeed, 0f, 5f);
        float span = halfW * 2.8f;
        float lenBase = halfW * (0.13f + 0.16f * windK);
        float hhBase = halfH * (0.014f + 0.008f * windK);
        float a = 0.22f;
        float baseAng = dir > 0f ? 0f : MathF.PI;
        float bandH = halfH * Math.Clamp(WindStreakHeight, 0.1f, 2f);
        const int steps = 5;

        for (int i = 0; i < n; i++)
        {
            float h1 = Hash01(i * 53 + 7), h2 = Hash01(i * 29 + 3), h3 = Hash01(i * 71 + 17);
            float h4 = Hash01(i * 97 + 23);
            // Vertical spread: the Wind Height band (a fraction of the view half-height),
            // with a per-mark bob so the whole field breathes instead of sliding flat.
            float y = cy + (h2 - 0.5f) * bandH + MathF.Sin(t * 1.1f + i * 1.7f) * bandH * 0.05f;
            // Wrap the marks across a span wider than the view so the field stays continuous.
            float travel = h3 * span + dir * t * speed;
            float x = cx - halfW * 1.3f + FloatMod(travel, span);

            // Species pick: curl = hooked comma (the slynyrd signature), wave = lazy S-bend,
            // dash = plain lurus streak (campur only), speck = dust fleck.
            int species = mixStraight
                ? (h1 < 0.42f ? 0 : h1 < 0.62f ? 1 : h1 < 0.88f ? 2 : 3)
                : (h1 < 0.62f ? 0 : h1 < 0.90f ? 1 : 3);

            // Shared ANIMATION terms: every mark shimmers and bobs as it drifts.
            float shimmer = 0.78f + 0.22f * MathF.Sin(t * 1.6f + i * 2.3f);
            float bob = MathF.Sin(t * 1.3f + i * 2.7f) * bandH * 0.03f;

            if (species == 3)
            {
                // Dust speck: a tiny fleck whipped along by the gust.
                float ds = hhBase * (0.7f + 0.6f * h4);
                OverlayQuadRot(x, y + bob, ds, ds, 0f, z,
                    new Vector4(WindStreakColor.X, WindStreakColor.Y, WindStreakColor.Z,
                        a * (0.45f + 0.4f * h2) * shimmer));
                continue;
            }

            if (species == 2)
            {
                // Plain lurus dash: one short flat streak with a slight tilt.
                OverlayQuadRot(x, y + bob, lenBase * (0.35f + 0.2f * h4), hhBase * 0.65f,
                    baseAng + (h2 - 0.5f) * 0.3f + MathF.Sin(t * 0.9f + i * 1.1f) * 0.12f, z,
                    new Vector4(WindStreakColor.X, WindStreakColor.Y, WindStreakColor.Z,
                        a * (0.7f + 0.3f * h4) * shimmer));
                continue;
            }

            // Swirl / wave mark: walk a chain of short segments whose heading bends along
            // the mark — monotonic bend for the hooked comma, bend-and-return for the wave.
            float markLen = lenBase * (species == 0 ? 0.8f + 0.4f * h4 : 1.1f + 0.5f * h4);
            float segLen = markLen / steps;
            float curl = (h4 < 0.5f ? -1f : 1f) * (species == 0
                ? 1.8f + 1.4f * h2
                : 0.7f + 0.6f * h2);
            // ANIMATION: the hook opens and closes over time (breathe) and the whole mark
            // rolls gently, so swirls read as churning air instead of frozen commas.
            float breathe = 0.55f + 0.45f * MathF.Sin(t * 1.4f + i * 2.1f);
            float roll = MathF.Sin(t * 0.9f + i * 1.1f) * 0.35f;

            float px = x, py = y + bob;
            for (int s = 0; s < steps; s++)
            {
                float fHead = s / (float)steps;            // 0 = head … → 1 = tail
                float bend = species == 0
                    ? curl * breathe * fHead               // hook: keep turning, pulsing
                    // wave: a ripple TRAVELS down the tail instead of a frozen S.
                    : curl * MathF.Sin((fHead * 1.15f - t * 0.7f) * MathF.PI);
                float ang = baseAng + bend + roll;
                float nx = px + MathF.Cos(ang) * segLen;
                float ny = py + MathF.Sin(ang) * segLen;
                // Taper the ribbon and fade alpha toward the tail so the mark streams out.
                float th = hhBase * (1f - 0.72f * fHead);
                float sa = a * (1f - 0.78f * fHead) * shimmer;
                OverlayQuadRot((px + nx) * 0.5f, (py + ny) * 0.5f, segLen * 0.65f, th,
                    ang, z, new Vector4(WindStreakColor.X, WindStreakColor.Y, WindStreakColor.Z, sa));
                px = nx; py = ny;
            }
        }
    }

    /// <summary>Heat haze: wobbling warm shimmer dashes over the bottom of the view —
    /// the pixel-art shorthand for rising hot air (no post-process distortion pass).</summary>
    private static unsafe void DrawHeatHaze(float cx, float cy, float halfW, float halfH)

    {
        float s = Math.Clamp(HeatHazeStrength, 0f, 1f);
        if (s <= 0.001f) return;
        float h = Math.Clamp(HeatHazeHeight, 0.02f, 1f);
        float zoneH = 2f * halfH * h;
        float y0 = cy - halfH;
        float t = _weatherClock * Math.Clamp(HeatHazeSpeed, 0f, 4f);
        int rows = Math.Clamp((int)(zoneH / (halfH * 0.06f)), 3, 40);
        const float z = 0.0235f;
        BindOverlayTex();

        for (int r = 0; r < rows; r++)
        {
            float f = rows == 1 ? 0f : r / (float)(rows - 1);
            float yBase = y0 + f * zoneH;
            for (int i = 0; i < 3; i++)
            {
                float h1 = Hash01(r * 31 + i * 17 + 5);
                float x = cx - halfW + (((h1 + t * 0.03f * (0.5f + h1)) % 1f) * 2f * halfW);
                float len = halfW * (0.10f + 0.22f * Hash01(r * 13 + i * 7 + 9));
                float wy = MathF.Sin(t * 1.6f + h1 * 6.283f + r * 0.7f) * halfH * 0.006f;
                float a = s * 0.10f * (1f - 0.4f * f);
                Stroke(x, yBase + wy, x + len, yBase + wy, halfH * 0.008f, z,
                    new Vector4(HeatHazeColor, a));
            }


        }


        FlushStrokes();
    }



    // ── Frost pattern cache (normalized view coords, regenerated on seed/density change) ──
    private static readonly List<(float Ax, float Ay, float Bx, float By)> _frostSegs = new();
    private static int _frostCacheSeed = int.MinValue;
    private static int _frostCacheDensity = int.MinValue;

    /// <summary>Build the ice-fern pattern along the view edges. Deterministic for a given
    /// seed, so the frost is static like real window ice (no per-frame flicker).</summary>
    private static void EnsureFrost()
    {
        int dens = Math.Clamp((int)MathF.Round(FrostDensity * 100f), 10, 400);
        if (_frostCacheSeed == _frostSeed && _frostCacheDensity == dens) return;
        _frostCacheSeed = _frostSeed;
        _frostCacheDensity = dens;
        _frostSegs.Clear();

        var rng = new System.Random(_frostSeed);
        int ferns = Math.Clamp((int)(dens * 0.32f), 6, 90);
        for (int i = 0; i < ferns; i++)
        {
            int edge = rng.Next(4);
            float u = (float)rng.NextDouble();
            float x, y, inward;   // anchor on an edge + direction pointing into the view
            switch (edge)
            {
                case 0: x = u; y = 0f; inward = MathF.PI * 0.5f; break;    // top
                case 1: x = u; y = 1f; inward = -MathF.PI * 0.5f; break;   // bottom
                case 2: x = 0f; y = u; inward = 0f; break;                 // left
                default: x = 1f; y = u; inward = MathF.PI; break;          // right
            }


            float len = 0.05f + (float)rng.NextDouble() * 0.16f;
            float ang = inward + ((float)rng.NextDouble() - 0.5f) * 0.8f;
            float dx = MathF.Cos(ang), dy = MathF.Sin(ang);
            float sx = x, sy = y;
            int steps = 4 + rng.Next(4);
            for (int st = 0; st < steps; st++)
            {
                float nx = sx + dx * len / steps;
                float ny = sy + dy * len / steps;
                _frostSegs.Add((sx, sy, nx, ny));
                if (st > 0)
                {
                    float bl = (len / steps) * (1.4f - 0.15f * st);
                    float bAng = ang + (st % 2 == 0 ? 1f : -1f) * 1.05f;
                    _frostSegs.Add((nx, ny, nx + MathF.Cos(bAng) * bl, ny + MathF.Sin(bAng) * bl));
                }


                sx = nx; sy = ny;
            }


        }


    }



    /// <summary>Frost overlay: procedural ice ferns at the edges + a soft frosted border.
    /// Screen-space (follows the camera) — it reads as frost on the "lens".</summary>
    private static unsafe void DrawFrost(float cx, float cy, float halfW, float halfH)
    {
        float s = Math.Clamp(FrostStrength, 0f, 1f);
        if (s <= 0.001f) return;
        EnsureFrost();
        BindOverlayTex();

        float x0 = cx - halfW, y0 = cy - halfH;
        float w = 2f * halfW, h = 2f * halfH;
        float y1 = y0 + h;
        var col = new Vector4(FrostColor, 0.55f * s);
        float th = halfH * 0.008f;
        foreach (var (ax, ay, bx, by) in _frostSegs)
            Stroke(x0 + ax * w, y0 + (1f - ay) * h, x0 + bx * w, y0 + (1f - by) * h,
                th, 0.030f, col);
        FlushStrokes();

        // Frosted border wash: top and bottom edges (the sides are covered by the ferns).
        float band = MathF.Min(halfH * 0.12f, halfW * 0.12f);
        float ea = 0.20f * s;
        var edge = new Vector4(FrostColor, ea);
        var clear = new Vector4(FrostColor, 0f);
        OverlayQuadGrad(x0, y1 - band, x0 + w, y1, 0.0295f, clear, edge);
        OverlayQuadGrad(x0, y0, x0 + w, y0 + band, 0.0295f, edge, clear);
    }



    /// <summary>One entry point for every pixel atmosphere effect, called after the fog /
    /// mist layers. Order: god rays → wind → heat haze → lightning → frost.
    /// All five are independent toggles; nothing runs when they are off.</summary>
    public static unsafe void RenderPixelFxFront(Camera camera)
    {
        bool boltAlive = _strikeLife > 0f && _boltSegs.Count > 0;
        if (!GodRaysEnabled && !WindStreaksEnabled && !HeatHazeEnabled && !FrostEnabled && !boltAlive)
            return;
        EnsureResources();
        if (_shader == 0) return;

        float halfH = MathF.Max(0.5f, camera.OrthoSize);
        float halfW = halfH * camera.GetAspect();
        float cx = camera.Position.X, cy = camera.Position.Y;

        BeginOverlay(camera, out bool cull, out bool depth);
        if (GodRaysEnabled) DrawGodRays(cx, cy, halfW, halfH);
        if (WindStreaksEnabled) DrawWindStreaks(cx, cy, halfW, halfH);
        if (HeatHazeEnabled) DrawHeatHaze(cx, cy, halfW, halfH);

        if (boltAlive) DrawLightning(cx, cy, halfW, halfH);
        if (FrostEnabled) DrawFrost(cx, cy, halfW, halfH);
        EndOverlay(cull, depth);
    }


    /// <summary>Tiny camera-shake ink blob on hard impacts (rain drop on stone, snow hit,
    /// wind puff hitting a hard surface). Read as a small ink blot on the hit spot, not an
    /// explosion.</summary>
    public static void DrawImpactInk(float x, float y)
    {
        float u = _cam != null ? MathF.Max(0.5f, _cam.OrthoSize) : 1f;
        float r = u * 0.022f;
        GL.ActiveTexture(Const.GL_TEXTURE0);
        GL.BindTexture(Const.GL_TEXTURE_2D, _texIds[TexGlow]);
        Vector4 col = new(0.9f, 0.95f, 1f, 0.25f);
        OverlayQuad(x - r, y - r, x + r, y + r, 0.026f, col);
    }



    /// <summary>Rain splash where a drop hits a surface: ONE flat ring (the ripple) plus
    /// 2–4 small circles floating above it — deliberately simple, per the user's sketch
    /// per-splash draw call, of which the big ring is one.</summary>
    private static void SpawnSplash(float x, float groundY)
    {
        float u = _cam != null ? MathF.Max(0.5f, _cam.OrthoSize) : 1f;
        int drops = Math.Clamp(RainSplashAmount, 0, 6);
        float sizeMul = Math.Clamp(RainSplashSize, 0.25f, 4f);

        // 1) Small ground arch — the "v"/arc mark where a drop lands (pixel-art rain
        //    reference: a tiny flattened bowl on the surface, NOT a big ring or a crown).
        if (_particles.Count < MaxParticles)
        {
            float arc = u * 0.055f * sizeMul;
            _particles.Add(new P
            {
                X = x,
                Y = groundY + u * 0.008f,
                Z = 0.075f,
                VX = 0f,
                VY = 0f,
                Life = 0.22f,
                MaxLife = 0.22f,
                Size = arc,
                SizeY = arc * 0.45f,   // flat arch, not a circle
                Grow = u * 0.010f,                  // spreads a touch as it fades
                Gravity = 0f,
                Tex = TexRing,
                Wind = 0f,
                C0 = new Vector4(0.85f, 0.93f, 1f, 0.85f),
                C1 = new Vector4(0.85f, 0.93f, 1f, 0f),
            }

);
        }



        // 2) 2 small dots popping above the arch (reference: two tiny marks per splash).
        for (int i = 0; i < drops; i++)
        {
            if (_particles.Count >= MaxParticles) return;
            float t = drops <= 1 ? 0.5f : i / (float)(drops - 1);   // 0..1 left → right
            float side = (t - 0.5f) * 2f;                            // -1..1
            float rnd = (float)_rng.NextDouble();
            float vx = side * 1.1f + (rnd - 0.5f) * 0.35f + WindX * 0.25f;
            float vy = 1.4f + rnd * 0.7f - MathF.Abs(side) * 0.30f;
            float life = 0.20f + rnd * 0.10f;
            _particles.Add(new P
            {
                X = x + side * u * 0.045f,
                Y = groundY + u * 0.02f,
                Z = 0.075f,
                VX = vx,
                VY = vy,
                Life = life,
                MaxLife = life,
                Size = u * 0.022f * sizeMul,
                Grow = -u * 0.005f,
                Gravity = 6f,
                Tex = TexDot,
                Wind = 0.25f,
                C0 = new Vector4(0.82f, 0.91f, 1f, 0.85f),
                C1 = new Vector4(0.82f, 0.91f, 1f, 0f),
            }

);
        }



    }



    /// <summary>World Y of the first collision tile below (x, yStart) on any visible
    /// layer — the rain's splash plane. Negative infinity when the map/column is empty.</summary>
    /// <summary>Highest weather surface below (x, yStart) across ALL scene maps —
    /// rain splash and snow rest land on the highest one. Per map, per column:
    /// COLLISION tiles = full box; DECOR tiles = PER-PIXEL tileset outline (first
    /// opaque pixel in this column — tent slopes splash, transparent areas don't).
    /// Sprites are handled separately via per-pixel SpriteHitAt.</summary>
    // ── Ground-surface column cache (rain/snow FPS dip) ──
    // With heavy weather ACTIVE (70+ particles/s × 30 s rain life = thousands
    // alive) each particle queried GroundYAtAny EVERY frame → a full tilemap
    // column scan (rows × layers × per-pixel profile) per particle per frame —
    // at 45 ms/frame budget this was the bit that crushed the FPS to ~22 when
    // rain was on. The terrain NEVER moves during a session (tiles are only
    // edited in edit mode; weather physics runs in gameplay), so a per-frame
    // column cache is CORRECT: keyed by (map ref, column), value = surface Y.
    private struct GroundCacheEntry
    {
        public Tilemap2D Map; public int Col;
        public int Tile; public float TileTop;   // Tile < 0 = full-box fallback (no pixels)
        public int Cols; public int Rows;        // tileset grid for the profile lookup
        public long Stamp;
    }


    private static readonly List<GroundCacheEntry> _groundCache = new();
    private static long _groundCacheStamp;

    private static float GroundYAtAny(float x, float yStart)
    {
        float best = float.NegativeInfinity;
        long stamp = ++_groundCacheStamp;
        foreach (var (map, cols, rows) in _surfaceMaps)
        {
            if (map == null) continue;
            float cell = map.TileSize * Tilemap2D.WorldScale;
            if (cell <= 0f || map.Width <= 0 || map.Height <= 0) continue;
            int col = (int)MathF.Floor(x / cell);
            if (col < 0 || col >= map.Width) continue;

            // CACHE HIT: this (map, column) was computed in the previous frames —
            // reuse the stored surface Y. Entries older than ~2 s get evicted
            // lazily (map edit invalidation is handled by ClearGroundCache). 
            bool cachedEntry = false;
            for (int ci = 0; ci < _groundCache.Count; ci++)
            {
                var e = _groundCache[ci];
                if (e.Map != map || e.Col != col) continue;
                e.Stamp = stamp;
                _groundCache[ci] = e;
                // Per-caller surface: the cached TILE is probed at the caller's OWN x
                // inside the column — a slope's art height varies across the tile, so the
                // old column-center probe made flakes rest at the middle height (embedded
                // under the slope's high side).
                float cachedSurf = SurfaceFromEntry(in e, x, cell);
                // Only usable when the caller is ABOVE it. A caller already below the
                // cached tile (inside a slope) must rescan deeper — serving the higher
                // cached surface would rest them on the slope they already passed.
                if (!float.IsNegativeInfinity(cachedSurf) && yStart >= cachedSurf - 0.001f)
                {
                    if (cachedSurf > best) best = cachedSurf;
                    goto nextMap;
                }


                cachedEntry = true;   // rescan below; do NOT overwrite the entry
                break;
            }



            float topWorld = map.Height * cell;
            // PERF: the expensive profile build (per-pixel scan of a tileset sheet,
            // first time per tile id) is skipped for particles already far ABOVE the
            // map — they cannot be near any surface (p.Y > gy always), so the full
            // column scan would return −∞ anyway. NOTE: deliberately NO cache entry
            // here — a cached −∞ would poison SUBSEQUENT particles in this column that
            // fall into range later (cache hits serve stored Y without knowing the
            // caller's yStart), making rain fall through the ground until eviction.
            // When a particle drops back in range, the normal path below computes and
            // caches the real surface.
            if (yStart > topWorld * 2f)
                continue;
            int rowStart = (int)MathF.Floor((topWorld - yStart) / cell);
            rowStart = Math.Clamp(rowStart, 0, map.Height - 1);
            float fxCol = x / cell - MathF.Floor(x / cell);   // caller's x inside the column
            float colBest = float.NegativeInfinity;
            int hitTile = -1; float hitTop = 0f;   // tile that produced colBest (cache record)
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
                        // Probe at the CALLER's x inside the column (fx), not the column
                        // center: on a slope the art height varies across the tile — the
                        // center probe put the whole column on the middle height.
                        int ci2 = Math.Clamp((int)(fxCol * prof.Length), 0, prof.Length - 1);
                        float f = prof[ci2];
                        if (f < 1f)
                        {
                            // f counts DOWN from the tile top (row 0 = top in the image)
                            float surf = tileTop - f * cell;
                            if (surf > colBest) { colBest = surf; hitTile = tile; hitTop = tileTop; }


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
                        if (tileTop > colBest) { colBest = tileTop; hitTile = -1; hitTop = tileTop; }


                        found = true;
                        break;
                    }


                }


                if (found) break; // this map's highest surface for the column is done
            }


            // Cache ONLY a surface the caller was ABOVE: a query from under a slope
            // would cache the deeper block top and poison every particle above it in
            // this column (they fall through the slope and pile underneath it).
            if (!cachedEntry && !float.IsNegativeInfinity(colBest) && yStart >= colBest - 0.001f)
                _groundCache.Add(new GroundCacheEntry
                {
                    Map = map,
                    Col = col,
                    Tile = hitTile,
                    TileTop = hitTop,
                    Cols = cols,
                    Rows = rows,
                    Stamp = stamp,
                }

);
            if (colBest > best) best = colBest;
        nextMap:;
        }


        // Lazy eviction: entries last touched before ~2 s of stamps ago get
        // dropped (stamps increment per query — an interval of stamps, not time).
        if (_groundCache.Count > 256)
        {
            for (int i = _groundCache.Count - 1; i >= 0; i--)
                if (stamp - _groundCache[i].Stamp > 512)
                    _groundCache.RemoveAt(i);
        }


        return best;
    }



    /// <summary>Surface Y for a caller at x from a cached column entry: probes the stored
    /// tile's profile at the caller's OWN x fraction (slopes vary across a tile), or the
    /// stored full-box top when the tile had no pixel data. −Infinity when the caller's x
    /// falls on transparent art of that tile (they need a deeper scan).</summary>
    private static float SurfaceFromEntry(in GroundCacheEntry e, float x, float cell)
    {
        if (e.Tile < 0) return e.TileTop;   // full-box fallback (no pixel data)
        var prof = GetTileTopProfile(e.Map.TilesetImagePath ?? "", e.Tile, e.Cols, e.Rows);
        if (prof == null || prof.Length == 0) return e.TileTop;
        float fx = x / cell - MathF.Floor(x / cell);
        int xi = Math.Clamp((int)(fx * prof.Length), 0, prof.Length - 1);
        float f = prof[xi];
        return f < 1f ? e.TileTop - f * cell : float.NegativeInfinity;
    }



    /// <summary>Column cache invalidation — tiles edited/scene changed. Called
    /// from the same sites that already reset the weather state (session begin,
    /// project close, map switch) — a stale surface would lurk one column below
    /// the ground or above a removed platform.</summary>
    public static void ClearGroundCache() => _groundCache.Clear();

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
            // Relative stored paths (sheet/anim JSON must be drive-letter free) resolve
            // against the project root / exe folder — same rule as all project assets.
            string full = DarkEngine3D_gl_csharp.Engine.Helpers.PathHelpers.ResolveProjectAsset(path);
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
    // ── BROAD-PHASE (per-frame sprite snapshot) ──
    // OLD SHAPE: every falling particle called SpriteHitAt TWICE (body + surface),
    // and each call iterated ALL scene objects running TryGetPlayer2DDrawData /
    // TryGetSprite2DDrawData (full quad math + clip lookup) per object. With rain's
    // 30 s particle life (thousands alive) that was particles × objects quad-math
    // evaluations per frame — a huge per-particle cost independent of collision.
    // NEW SHAPE: once per IntegrateParticles, snapshot every visible sprite's RECT
    // + sheet-path + uv window into a reusable buffer; per particle we then only do
    // a cheap X-range scan over that buffer and run the per-pixel work exclusively
    // on candidates whose X range actually contains the drop. Semantics identical
    // (same rects → same hits) because the snapshot is taken from the same draw-
    // data calls the renderer itself uses.
    private struct SpriteRect { public float X0, X1, Y0, Y1, UMin, UMax, VMin, VMax; public string Path; public bool Pixels; }


    private static readonly List<SpriteRect> _spriteRects = new();
    private static int _spriteRectCount;

    private static (bool BodyHit, float TopY) SpriteHitAt(float x, float y, float yStart)
    {
        float bestTop = float.NegativeInfinity;
        var rects = _spriteRects;
        for (int si = 0; si < _spriteRectCount; si++)
        {
            var e = rects[si];
            if (x < e.X0 || x > e.X1) continue;

            // Pixel path (preferred): decode the sheet + per-column top profile.
            // FALLBACK (never silently skip a sprite — the "scarecrow tembus" bug):
            // when pixels are unavailable, use the RECT top / rect body.
            byte[]? data = null; int W = 0, H = 0, px = 0;
            bool havePixels = false;
            float surfaceY = e.Y1; // rect-top fallback

            if (e.Pixels)
            {
                var fp = GetFrameTopProfile(e.Path, e.UMin, e.UMax, e.VMin, e.VMax);
                if (fp != null)
                {
                    var (profArr, px0, px1, pdata, pw, ph) = fp.Value;
                    float u = e.UMin + Math.Clamp((x - e.X0) / MathF.Max(0.0001f, e.X1 - e.X0), 0f, 1f) * (e.UMax - e.UMin);
                    px = Math.Clamp((int)(u * pw), 0, pw - 1);
                    int idx = Math.Clamp(px - px0, 0, Math.Max(0, px1 - px0 - 1));
                    if (profArr[idx] < 1f)
                    {
                        havePixels = true;
                        data = pdata; W = pw; H = ph;
                        surfaceY = e.Y1 - profArr[idx] * (e.Y1 - e.Y0); // real outline surface
                    }


                    else
                        continue; // transparent column → fall through this sprite
                }


            }



            if (surfaceY <= yStart && surfaceY > bestTop) bestTop = surfaceY;

            // Body hit: the drop is inside the sprite; with pixels, require the exact
            // pixel to be opaque; without, the rect body is the fallback surface.
            if (y >= e.Y0 && y <= e.Y1)
            {
                if (!havePixels)
                    return (true, surfaceY); // rect body fallback
                float fy = Math.Clamp((y - e.Y0) / MathF.Max(0.0001f, e.Y1 - e.Y0), 0f, 1f); // 0 bottom → 1 top
                float v = e.VMax + fy * (e.VMin - e.VMax);
                int py = Math.Clamp((int)(v * H), 0, H - 1);
                if (data != null && data[(py * W + px) * 4 + 3] >= WeatherAlphaThreshold)
                    return (true, surfaceY);
            }


        }


        return (false, bestTop);
    }



    /// <summary>Snapshot every visible Player2D/Sprite2D's rendered rect + uv window
    /// for this frame's weather broad-phase. Called once from IntegrateParticles.
    /// Zero alloc steady-state (single reusable List, overwritten each frame).</summary>
    private static void BuildSpriteRects(EditorObjectManager? mgr)
    {
        _spriteRectCount = 0;
        if (mgr == null) return;
        foreach (var o in mgr.Objects)
        {
            if (o is not { IsVisible: true, PrimitiveType: EditorPrimitiveType.Player2D or EditorPrimitiveType.Sprite2D }

)
                continue;
            uint t;
            System.Numerics.Vector2 uvMin, uvMax;
            System.Numerics.Vector3 bl, br, tr, tl;
            bool ok = o.PrimitiveType == EditorPrimitiveType.Player2D
                ? o.TryGetPlayer2DDrawData(out t, out uvMin, out uvMax, out bl, out br, out tr, out tl)
                : o.TryGetSprite2DDrawData(out t, out uvMin, out uvMax, out bl, out br, out tr, out tl);
            if (!ok) continue;

            string imagePath = "";
            if (o.TryGetPlayer2DClip(out var sheet, out var _) && sheet != null)
                imagePath = sheet.ImagePath ?? "";
            bool pixels = !string.IsNullOrWhiteSpace(imagePath) && GetFrameTopProfile(imagePath, uvMin.X, uvMax.X, uvMin.Y, uvMax.Y) != null;

            var e = new SpriteRect
            {
                X0 = MathF.Min(bl.X, tl.X),
                X1 = MathF.Max(br.X, tr.X),
                Y0 = MathF.Min(bl.Y, br.Y),
                Y1 = MathF.Max(tl.Y, tr.Y),
                UMin = uvMin.X,
                UMax = uvMax.X,
                VMin = uvMin.Y,
                VMax = uvMax.Y,
                Path = imagePath,
                Pixels = pixels,
            }

;
            if (_spriteRectCount < _spriteRects.Count) _spriteRects[_spriteRectCount] = e;
            else _spriteRects.Add(e);
            _spriteRectCount++;
        }


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
        ClearGroundCache();   // map/scene may change between sessions — never serve stale column surfaces
        RainEnabled = false;
        WindX = 0f;
        SnowEnabled = false;
        FogEnabled = false;
        MistEnabled = false;
        TintEnabled = false;
        LightningEnabled = false;
        GodRaysEnabled = false;
        WindStreaksEnabled = false;
        HeatHazeEnabled = false;
        FrostEnabled = false;
        _boltSegs.Clear();
        _boltBranchSegs.Clear();
        _strikeLife = 0f;
        RainOffsetX = RainOffsetY = 0f;
        RainSizeMul = 1f;
        RainSplashAmount = 3;
        RainSplashSize = 1.2f;
        SnowOffsetX = SnowOffsetY = 0f;
        SnowSizeMul = 1f;
        SnowRestSeconds = 6f;
    }



    // ═══════════════════════ Rendering ═══════════════════════

    private static uint _shader, _vao, _vbo;
    private static int _locView, _locProj, _locModel, _locTex;
    private static readonly uint[] _texIds = new uint[14];
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
        SpriteLighting2D.Apply(_shader);
        SpriteLighting2D.BindNormal(_shader, 0);
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
        SpriteLighting2D.Apply(_shader);
        SpriteLighting2D.BindNormal(_shader, 0);
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
out vec3 vWorld;
void main() {
    vec4 world = model * vec4(aPos, 1.0);
    vWorld = world.xyz;
    gl_Position = projection * view * world;
    vUV = aUV;
    vTint = aTint;
}";
        // Same 2D lighting block as the sprite/tilemap shader: world particles respond
        // to the Point/Spot markers instead of glowing flat through a dark scene.
        string fragSrc = @"#version 330 core
in vec2 vUV;
in vec4 vTint;
in vec3 vWorld;
uniform sampler2D tex;
out vec4 FragColor;
" + SpriteLighting2D.GlslLighting + @"
void main() {
    vec4 c = texture(tex, vUV) * vTint;
    if (c.a < 0.01) discard;
    c.rgb = Apply2DLighting(c.rgb, vUV, vWorld);
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
            _texIds[TexGlow] = UploadTex(BuildRadial(4.5f, soft: true));             _texIds[TexSpark] = UploadTex(BuildSpark());
            _texIds[TexRainLine] = UploadTex(BuildRainLine());
            _texIds[TexRainAccent] = UploadTex(BuildRainAccent());
            _texIds[TexCoin] = UploadTex(BuildCoin());
            _texIds[TexSquare] = UploadTex(BuildSquare());
            _texIds[TexRing] = UploadTex(BuildRing());
            _texIds[TexCrown] = UploadTex(BuildCrown());
            _texIds[TexCloud] = UploadTex(BuildCloud());
            _texIds[TexWisp] = UploadTex(BuildWisp());
            _texIds[TexWindStreak] = UploadTex(BuildWindStreakNew());
            _texIds[TexWindPuff] = UploadTex(BuildWindPuffNew());
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
    private const int SW = 64;   // wind streak atlas: wide strip so a single texture can carry the tail

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
                int i = (y * S + x) * 4; px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;
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

    // Rain core streak: thin vertical line, brightest near the head (bottom = fall
    // direction), with a short tapered tail above so each drop reads as a drop, not a
    // bare square. Camera-relative sizing + LeanToVel keeps the look at any zoom and
    // slants the streak live with the wind.
    private static byte[] BuildRainLine()
    {
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                float a = MathF.Max(0f, 1f - MathF.Abs(u - 0.5f) * 4.5f) * (0.35f + 0.65f * v);
                int i = (y * S + x) * 4;
                px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;
                px[i + 3] = (byte)(Math.Clamp(a, 0f, 1f) * 255f);
            }
        return px;
    }



    // Rain droplet: small tilted drops, fidgets to the side of the big streak (no big ring).
    private static byte[] BuildRainAccent()
    {
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                float a = MathF.Max(0f, 1f - MathF.Abs(u - 0.5f) * 6f) * (0.3f + 0.7f * v) * 0.55f;
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






    /// <summary>Lumpy cloud mass (fog — reference #1). Five overlapping lobes give the
    /// irregular cauliflower silhouette of a real cloud, plus a stable per-pixel jitter that
    /// thins the rim. A single radial glow cannot produce this shape.</summary>
    private static byte[] BuildCloud()
    {
        var lobes = new (float cx, float cy, float r)[]
        {
            (0.00f, -0.05f, 0.60f),
            (-0.42f, 0.05f, 0.44f),
            (0.40f, 0.08f, 0.40f),
            (-0.10f, 0.32f, 0.34f),
            (0.24f, 0.28f, 0.30f),
           }

;
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float a = 0f;
                for (int i = 0; i < lobes.Length; i++)
                {
                    var (cx, cy, r) = lobes[i];
                    float ex = dx - cx, ey = dy - cy;
                    float d = MathF.Sqrt(ex * ex + ey * ey);
                    float n = 1f - d / r;
                    if (n > 0f) a = MathF.Max(a, n * n * 1.7f);   // max-blend keeps lobes distinct
                }


                a *= 0.80f + 0.20f * Hash01(x * 131 + y * 977);
                int idx = (y * S + x) * 4;
                px[idx] = 255; px[idx + 1] = 255; px[idx + 2] = 255;
                px[idx + 3] = (byte)Math.Clamp(a * 255f, 0f, 255f);
            }
        return px;
    }






    /// <summary>Short, thin, ragged streak (mist — reference #2). Tapered ends and a soft
    /// vertical falloff; the render stretches it wider, so the texture itself stays a plain
    /// horizontal sliver instead of a round glow.</summary>
    private static byte[] BuildWisp()
    {
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f;   // along the streak
                float v = (y + 0.5f) / S * 2f - 1f;   // across it
                float wob = MathF.Sin(u * 5.2f + 0.7f) * 0.09f;
                float across = MathF.Max(0f, 1f - MathF.Abs(v + wob) * 6.5f);
                float ends = MathF.Max(0f, 1f - MathF.Abs(u) * MathF.Abs(u) * MathF.Abs(u));
                float a = across * across * ends;
                int idx = (y * S + x) * 4;
                px[idx] = 255; px[idx + 1] = 255; px[idx + 2] = 255;
                px[idx + 3] = (byte)Math.Clamp(a * 255f, 0f, 255f);
            }
        return px;
    }






    /// <summary>Hard-edged outline ring — stretched into a flat ellipse by SizeY for
    /// the rain splash ripple (one big circle on the ground, per the user's sketch).</summary>
    private static byte[] BuildRing()
    {
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float a = r is > 0.70f and < 0.92f ? 1f : 0f;   // crisp pixel-art band
                int i = (y * S + x) * 4;
                px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;
                px[i + 3] = (byte)(a * 255f);
            }
        return px;
    }



    /// <summary>Splash crown: falling drop at the top, a thin rising jet, plus three rings
    /// that expand and thin out as they go. Reference look: one droplet about to hit, a
    /// little crown column, and 2-3 expanding ripples on the water.</summary>
    private static byte[] BuildCrown()
    {
        var px = new byte[S * S * 4];
        // Drop (top third). Soft filled blob with a bright highlight band.
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                // drop lives in the top fifth of the sprite
                if (dy < 0.4f)
                {
                    float r = MathF.Sqrt(dx * dx + dy * dy);
                    float core = MathF.Max(0f, 1f - r * 2.4f);
                    float hi = (dx < 0f && dy < 0f) ? 1.25f : 1f;
                    int i = (y * S + x) * 4;
                    px[i] = px[i + 1] = px[i + 2] = 255;
                    px[i + 3] = (byte)Math.Clamp(core * hi * 255f, 0f, 255f);
                }


                else
                    px[(y * S + x) * 4 + 3] = 0;
            }



        // Jet: thin vertical column rising from center, fading toward the top.
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f;
                float dy = (y + 0.5f) / S - 0.5f;
                float rad = MathF.Sqrt(dx * dx);
                // a thin column whose width grows slightly with height, alpha fading up at top
                float col = MathF.Max(0f, 1f - rad * 20f);
                float topFade = Math.Clamp((dy - 0.25f) / 0.55f, 0f, 1f);
                float a = col * topFade * 0.9f;
                if (dy > -0.5f && dy < 0.5f)
                {
                    int i = (y * S + x) * 4;
                    px[i] = px[i + 1] = px[i + 2] = 255;
                    px[i + 3] = (byte)Math.Clamp(a * 255f, 0f, 255f);
                }


            }



        // Expanding rings: 3 ellipses whose major axis grows outward and whose alpha thins.
        // Bottom half only (the water plane is the lower region of the sprite).
        float ringsY = -0.35f * 2f;   // rings sit in the lower part of the sprite
        var ringData = new[] { (0.18f, 0.62f), (0.34f, 0.35f), (0.50f, 0.20f) }

;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f;
                float dy = (y + 0.5f) / S - 0.5f;
                if (dy > -0.15f) continue;   // rings only in the lower portion of the splash sprite
                float ringA = 0f;
                foreach (var (maj, alpha0) in ringData)
                {
                    float ax = dx / maj, ay = dy / 0.06f;   // circulize-ish: flat horizontal ellipse
                    float rr = MathF.Sqrt(ax * ax + ay * ay);
                    float distal = MathF.Abs(rr - 1f);
                    float ring = distal < 0.06f ? 1f : (distal < 0.12f ? MathF.Max(0f, 1f - distal * 10f) : 0f);
                    float fadeOut = MathF.Max(0f, 1f - (maj - 0.18f) * 1.4f);
                    ringA = MathF.Max(ringA, ring * alpha0 * fadeOut);
                }


                if (ringA > 0f)
                {
                    int i = (y * S + x) * 4;
                    px[i] = px[i + 1] = px[i + 2] = 255;
                    px[i + 3] = (byte)Math.Clamp(ringA * 255f, 0f, 255f);
                }


            }
        return px;
    }    /// <summary>Wind puff atlas (64×32): a sparse, soft wisp/curl shape used for the
    /// melingkar wind mode so the field has a loose, puffy look instead of only straight
    /// tailed dashes.</summary>
    private static byte[] BuildWindPuffNew()
    {
        const int W = SW, H = S;
        var px = new byte[W * H * 4];

        float cx = 0.5f, cy = 0.5f;

        // One soft irregular wisp blob: a brighter core with a few feathered edges and a
        // small hooked tendril so it reads as a curl/puff rather than a clean dash.
        float coreW = 0.12f, coreH = 0.22f;
        float wispW = 0.05f, wispL = 0.30f, wispOff = -0.10f;
        float tendrilW = 0.022f, tendrilL = 0.28f, tendrilKick = 0.06f;

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                float dx = u - cx, dy = v - cy;
                float along = -dx;
                float a = 0f;

                // Soft core blob.
                {
                    float edge = MathF.Max(0f, 1f - MathF.Abs(dy) / coreH);
                    float front = MathF.Max(0f, 1f - MathF.Abs(dx) / coreW);
                    a = MathF.Max(a, edge * edge * front);
                }

                // Feathered wisp tail on the leading side.
                {
                    float tLocal = along + wispOff;
                    float fade = MathF.Max(0f, 1f - tLocal / wispL);
                    float tip = MathF.Max(0f, 1f - (wispL - tLocal) / (wispL * 0.18f));
                    float wdy = dy - wispOff;
                    float edge = MathF.Max(0f, 1f - MathF.Abs(wdy) / wispW);
                    a = MathF.Max(a, edge * edge * fade * tip * 0.85f);
                }

                // Small hooked tendril on the trailing side.
                {
                    float tLocal = along - tendrilL * 0.5f;
                    float kickY = -MathF.Max(0f, tLocal + tendrilL * 0.2f) * (tendrilKick / tendrilL);
                    float fade = MathF.Max(0f, 1f - tLocal / tendrilL);
                    float tip = MathF.Max(0f, 1f - (tendrilL - tLocal) / (tendrilL * 0.2f));
                    float ddy = dy - kickY;
                    float edge = MathF.Max(0f, 1f - MathF.Abs(ddy) / tendrilW);
                    a = MathF.Max(a, edge * edge * fade * tip * 0.7f);
                }


                float edgeFade = MathF.Max(0.30f, 1f - MathF.Abs(dx) * 1.2f);
                a *= edgeFade;
                int i = (y * W + x) * 4;
                px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;                px[i + 3] = (byte)Math.Clamp(a * 255f, 0f, 255f);
        }
        return px;
    }

    /// <summary>Wind streak atlas (64×32). A single wide texture carries a tailed dash:
    /// a bright leading head, a slightly thicker body, a thin tapered tail, and a faint
    /// counter-tail — so at runtime every streak looks like a wind-swept tail, not a bare
    /// line segment. The quads are all the same size; the texture alone gives the look,
    /// matching the reference wind motion.</summary>
    private static byte[] BuildWindStreak()
    {

        // 64 wide × 32 tall: enough room for a clean tailed dash with a little breathing room.
        const int W = SW, H = S;
        var px = new byte[W * H * 4];

        // One tailed dash centred in the strip.
        float cx = 0.5f, cy = 0.5f;
        float headW = 0.050f, headH = 0.70f;   // bright leading head (slightly thicker)
        float bodyW = 0.028f, bodyL = 0.20f;   // body leading away from head
        float tailW = 0.020f, tailL = 0.50f;   // tapered tail trailing behind
        float ctailW = 0.012f, ctailL = 0.22f; // faint counter-tail on the other side

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                float dx = u - cx, dy = v - cy;        // horizontal axis = along the dash
                float a = 0f;

                // Leading head (right side of the dash).
                {
                    float hx = dx - headW * 0.5f;
                    float alongHead = MathF.Abs(dy) / headH;     // 0 on centreline → 1 at edge
                    float across = MathF.Max(0f, 1f - alongHead * 2f);
                    float behind = MathF.Max(0f, 1f - hx / headW);
                    float head = across * across * behind;
                    a = MathF.Max(a, head);
                }



                // Thicker mid-body directly behind the head.
                {
                    float bx = dx - (headW + bodyL) * 0.5f;
                    float alongBody = MathF.Abs(dy) / bodyW;
                    float across = MathF.Max(0f, 1f - alongBody * 2f);
                    float behind = MathF.Max(0f, 1f - bx / bodyL);
                    float body = across * across * behind * 0.85f;
                    a = MathF.Max(a, body);
                }



                // Long tapered tail trailing behind the head.
                {
                    float tx = dx + tailL * 0.5f;   // tail extends toward negative x (behind)
                    float alongTail = MathF.Abs(dy) / tailW;
                    float across = MathF.Max(0f, 1f - alongTail * 2f);
                    // taper toward the far end: strongest just behind the head, fading out
                    float dist = MathF.Max(0f, -tx);                 // 0 at the tail start, + toward end
                    float taper = MathF.Max(0f, 1f - dist / tailL);
                    float tail = across * across * taper * 0.6f;
                    a = MathF.Max(a, tail);
                }



                // Short faint counter-tail on the opposite side (a little wind-swept back edge).
                {
                    float tcx = dx + ctailL * 0.5f;
                    float alongCt = MathF.Abs(dy) / ctailW; float across = MathF.Max(0f, 1f - alongCt * 2f);
                    float dist = MathF.Max(0f, -tcx);
                    float taper = MathF.Max(0f, 1f - dist / ctailL);
                    float ctail = across * across * taper * 0.28f;
                    a = MathF.Max(a, ctail);
                }                // Soft overall edge so the streak blends cleanly (still reads pixel-art sharp).
                float edge = MathF.Max(0f, 1f - MathF.Abs(dx) - 0.02f);
                a *= MathF.Max(0.4f, edge);
                int i = (y * W + x) * 4;
                px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;                px[i + 3] = (byte)Math.Clamp(a * 255f, 0f, 255f);
            }
        return px;
    }

    /// <summary>Wind streak atlas (64×32): a tied-down wind knot with a hooked head,
    /// an S-bent body, a long tapered tail, and a faint low wisp — the texture that gives
    /// the lurus tailed wind streak its look.</summary>
    private static byte[] BuildWindStreakNew()
    {
        const int W = SW, H = S;
        var px = new byte[W * H * 4];

        float cx = 0.5f, cy = 0.5f;

        // Head: small bright hook knot on the right side.
        float headW = 0.07f, headH = 0.34f, headKick = 0.04f;
        // Body: thicker ribbon following a gentle S.
        float bodyW = 0.05f, bodyL = 0.34f;
        float bodyWobbleAmp = 0.05f, bodyWobbleW = 3.0f;
        // Tail: long, thin, tapered.
        float tailW = 0.020f, tailL = 0.95f;
        // Accent wisp below the body.
        float accentW = 0.010f, accentL = 0.72f, accentOff = -0.028f;

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                float dx = u - cx, dy = v - cy;
                float along = -dx;
                float a = 0f;

                // Head knot, bent back on the trailing side.
                {
                    float localX = along - headW * 0.5f;
                    float headCenterY = -MathF.Max(0f, localX + headW * 0.2f) * (headKick / headW);
                    float ddx = localX;
                    float ddy = dy - headCenterY;
                    float edge = MathF.Max(0f, 1f - MathF.Abs(ddy) / headH);
                    float front = MathF.Max(0f, 1f - ddx / headW);
                    a = MathF.Max(a, edge * edge * front);
                }

                // S-bent body.
                {
                    float wob = MathF.Sin(along * bodyWobbleW + 1.2f) * bodyWobbleAmp;
                    float ddx = along - bodyL * 0.5f;
                    float ddy = dy - wob;
                    float edge = MathF.Max(0f, 1f - MathF.Abs(ddy) / bodyW);
                    float front = MathF.Max(0f, 1f - ddx / bodyL);
                    a = MathF.Max(a, edge * edge * front * 0.92f);
                }

                // Tapered tail.
                {
                    float tLocal = along - bodyL;
                    float fade = MathF.Max(0f, 1f - tLocal / tailL);
                    float tip = MathF.Max(0f, 1f - (tailL - tLocal) / (tailL * 0.14f));
                    float ddy = dy;
                    float edge = MathF.Max(0f, 1f - MathF.Abs(ddy) / tailW);
                    a = MathF.Max(a, edge * edge * fade * tip * 0.80f);
                }

                // Accent below the body.
                {
                    float tLocal = along - bodyL * 0.6f;
                    float fade = MathF.Max(0f, 1f - tLocal / accentL);
                    float tip = MathF.Max(0f, 1f - (accentL - tLocal) / (accentL * 0.16f));
                    float ddy = dy - accentOff;
                    float edge = MathF.Max(0f, 1f - MathF.Abs(ddy) / accentW);
                    a = MathF.Max(a, edge * edge * fade * tip * 0.55f);
                }

                float edgeFade = MathF.Max(0.35f, 1f - MathF.Abs(dx) * 1.2f);
                a *= edgeFade;
                int i = (y * W + x) * 4;
                px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;
                px[i + 3] = (byte)Math.Clamp(a * 255f, 0f, 255f);
            }
        return px;
    }

}
