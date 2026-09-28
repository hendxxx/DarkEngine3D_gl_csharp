using System.Numerics;
using System.Collections.Generic;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.IDE;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Sprite-based projectile system for the 2D sidescroller. A projectile is an
/// ANIMATED SPRITE (sheet + clip from the Sprite Editor registry — the same sheets
/// actions/portals use, so art is reused) that flies with speed/Gravity/MaxDistance,
/// spins to face its velocity, and on collision (solid tile or another character):
/// plays a HIT animation at the impact point, fires an optional particle burst,
/// subtracts HP/MP, and despawns (unless Piercing).
///
/// Damage model (phase 1):
///  • Any Player2D hit (except the shooter) loses RUNTIME HP (starts at 100); at 0
///    the object hides — a simple enemy death.
///  • When the hit target is the scene's FIRST Player2D object (the player), the
///    damage also lands on Player2DStats so the HUD bars visibly drop.
/// </summary>
public static class Projectile2DSystem
{
    // ═══════════════════════ Config (authored on actions / triggers) ═══════════════════════

    /// <summary>Full projectile description. Filled from a Player2DAction (Inspector)
    /// or a "Spawn Projectile" trigger action (Map Editor).</summary>
    public sealed class Config
    {
        public string Sheet = "";
        public string Clip = "";
        /// <summary>Flight speed in world units/second.</summary>
        public float Speed = 8f;
        /// <summary>Maximum travel distance (world units) before the projectile ends.</summary>
        public float MaxDistance = 15f;
        /// <summary>Downward acceleration (world units/s²). 0 = straight flight.</summary>
        public float Gravity = 0f;
        /// <summary>Initial vertical velocity (up positive) — arcs when combined with Gravity.</summary>
        public float VelY = 0f;
        /// <summary>Sprite world height (same normalization convention as Player2DHeight).</summary>
        public float WorldHeight = 0.8f;
        /// <summary>Damage applied on hit (HP/MP; negative = heal).</summary>
        public float DamageHP = 0f;
        public float DamageMP = 0f;
        /// <summary>Hit animation (sheet + clip) played once at the impact point.</summary>
        public string HitSheet = "";
        public string HitClip = "";
        /// <summary>Size multiplier of the hit animation sprite (1 = projectile's own
        /// WorldHeight — the hit plays the HIT CLIP, not particles, per user decision).</summary>
        public float HitScale = 1f;
        /// <summary>Optional particle burst preset at the impact point (empty = none).</summary>
        public string HitFx = "";
        /// <summary>Size multiplier of the optional impact particle burst.</summary>
        public float HitFxScale = 1f;
        /// <summary>Rotate the sprite along the velocity (fireball arcs). False = face
        /// the travel direction by UV mirroring only.</summary>
        public bool RotateToVelocity = false;
        /// <summary>True = pass through targets/tiles and keep flying until MaxDistance.</summary>
        public bool Piercing = false;
        /// <summary>Safety cap (seconds) regardless of distance.</summary>
        public float MaxTime = 6f;
        public float Z = 0.065f;

        public bool IsValid => !string.IsNullOrWhiteSpace(Sheet) && !string.IsNullOrWhiteSpace(Clip);
    }

    // ═══════════════════════ Runtime state ═══════════════════════

    public sealed class Projectile
    {
        public Config Cfg = new();
        public Vector3 Pos;
        public Vector2 Vel;
        public float Traveled;
        public float Time;
        public float AnimTime;
        public bool FacingRight = true;
        public EditorObject? Owner;
        public bool Dead;
    }

    /// <summary>One-shot impact animation played in place (no movement).</summary>
    public sealed class HitEffect
    {
        public string Sheet = "";
        public string Clip = "";
        public Vector3 Pos;
        public float Height = 0.8f;
        public float Time;
        public bool FacingRight = true;
    }
    private static readonly List<Projectile> _projectiles = new();
    private static readonly List<HitEffect> _hits = new();
    /// <summary>Runtime HP per Player2D object (enemy phase-1 health). Cleared per session.</summary>
    private static readonly Dictionary<EditorObject, float> _runtimeHp = new();

    public const float DefaultRuntimeHp = 100f;
    public const int MaxProjectiles = 200;

    /// <summary>Quad corner template (unit half-extents): BL, BR, TR, BL, TR, TL —
    /// matches the portal/sprite winding so the shared quad pipeline draws it right.</summary>
    private static readonly (float x, float y)[] QuadCorners =
        [(-1f, -1f), (1f, -1f), (1f, 1f), (-1f, -1f), (1f, 1f), (-1f, 1f)];

    // ═══════════════════════ Spawning ═══════════════════════

    /// <summary>Spawn from a character's animation action (the action's own sheet/clip
    /// dropdowns are reused for the projectile art). Spawn point = character's mid
    /// body + the action's configured offset (X mirrors with the facing).</summary>
    public static void SpawnFromAction(EditorObject owner, Player2DAction act,
        EditorObjectManager? mgr, float offsetX, float offsetY, float speed, float maxDistance,
        float gravity, float velY, float worldHeight, float damageHp, float damageMp,
        string hitSheet, string hitClip, float hitScale, string hitFx, float hitFxScale,
        bool rotate, bool piercing)
    {
        var cfg = new Config
        {
            // "(action art)": ProjectileSheet left empty → reuse the ACTION's own
            // sheet/clip (the common case — art already picked in the action row).
            Sheet = string.IsNullOrWhiteSpace(act.ProjectileSheet) ? act.SpriteSheet : act.ProjectileSheet,
            Clip = string.IsNullOrWhiteSpace(act.ProjectileSheet)
                ? act.Clip
                : (string.IsNullOrWhiteSpace(act.ProjectileClip)
                    ? (IDEBridge.GetClipNames(act.ProjectileSheet).FirstOrDefault() ?? "")
                    : act.ProjectileClip),
            Speed = speed, MaxDistance = maxDistance, Gravity = gravity, VelY = velY,
            WorldHeight = worldHeight, DamageHP = damageHp, DamageMP = damageMp,
            HitSheet = hitSheet, HitClip = hitClip, HitScale = hitScale,
            HitFx = hitFx, HitFxScale = hitFxScale,
            RotateToVelocity = rotate, Piercing = piercing,
        };
        if (!cfg.IsValid) return;
        bool facing = owner.Player2DFacingRight;
        var pos = new Vector3(
            owner.Position.X + offsetX * (facing ? 1f : -1f),
            owner.Position.Y + owner.Player2DHeight * 0.5f + offsetY,
            owner.Position.Z + cfg.Z);
        Spawn(cfg, pos, facing, owner);
    }

    /// <summary>Spawn from a trigger action: flies toward the player's current side
    /// (horizontal direction only) — a classic enemy trap / turret.</summary>
    public static void SpawnFromTrigger(Config cfg, Vector3 origin, bool towardPlayer)
    {
        if (!cfg.IsValid) return;
        bool facing = towardPlayer
            ? (TriggerEventSystem.LastPlayerPosition is Vector3 p ? p.X >= origin.X : true)
            : false;
        var pos = new Vector3(origin.X, origin.Y, origin.Z + cfg.Z);
        Spawn(cfg, pos, facing, null);
    }

    private static void Spawn(Config cfg, Vector3 pos, bool facingRight, EditorObject? owner)
    {
        if (_projectiles.Count >= MaxProjectiles) return;
        _projectiles.Add(new Projectile
        {
            Cfg = cfg,
            Pos = pos,
            Vel = new Vector2(cfg.Speed * (facingRight ? 1f : -1f), cfg.VelY),
            FacingRight = facingRight,
            Owner = owner,
        });
    }

    // ═══════════════════════ Simulation ═══════════════════════

    /// <summary>Per-frame update: integrate, collide with tiles + characters, spawn
    /// hit effects. Call from Player2DSystem.Update (preview/in-game only).</summary>
    public static void Tick(EditorObjectManager? mgr, IEnumerable<Tilemap2D> maps, float dt)
    {
        if (dt <= 0f) return;

        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            var pr = _projectiles[i];
            pr.Time += dt;
            pr.AnimTime += dt;

            // Integrate.
            pr.Vel.Y -= pr.Cfg.Gravity * dt;
            float step = pr.Vel.Length() * dt;
            pr.Pos.X += pr.Vel.X * dt;
            pr.Pos.Y += pr.Vel.Y * dt;
            pr.Traveled += step;

            // Lifetime / distance end (no hit animation — the projectile just expires).
            if (pr.Time >= pr.Cfg.MaxTime || pr.Traveled >= pr.Cfg.MaxDistance || pr.Pos.Y < -20f)
            {
                _projectiles.RemoveAt(i);
                continue;
            }

            if (pr.Cfg.Piercing) continue; // piercing: nothing stops it

            // ── Solid tile collision (any visible map's collision layers) ──
            bool hitTile = false;
            foreach (var m in maps)
            {
                if (IsSolidAt(m, pr.Pos.X, pr.Pos.Y)) { hitTile = true; break; }
            }
            bool hitTarget = false;
            if (!hitTile && mgr != null)
                hitTarget = TryHitCharacters(pr, mgr);

            if (hitTile || hitTarget)
            {
                ResolveHit(pr);
                _projectiles.RemoveAt(i);
            }
        }

        // Hit animations: play once then remove.
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            var h = _hits[i];
            h.Time += dt;
            float dur = GetClipDuration(h.Sheet, h.Clip);
            if (h.Time >= dur) _hits.RemoveAt(i);
        }
    }

    /// <summary>Impact: hit animation + particle burst + (already applied) damage.</summary>
    private static void ResolveHit(Projectile pr)
    {
        if (!string.IsNullOrWhiteSpace(pr.Cfg.HitClip) && !string.IsNullOrWhiteSpace(pr.Cfg.HitSheet))
            _hits.Add(new HitEffect
            {
                Sheet = pr.Cfg.HitSheet,
                Clip = pr.Cfg.HitClip,
                Pos = pr.Pos,
                // Hit Scale: 1 = the projectile's own WorldHeight (the hit IS the
                // Hit Clip animation; particles are optional extras).
                Height = MathF.Max(0.01f, pr.Cfg.WorldHeight * MathF.Max(0.01f, pr.Cfg.HitScale)),
                FacingRight = pr.FacingRight,
            });
        if (!string.IsNullOrWhiteSpace(pr.Cfg.HitFx))
            Effect2DSystem.SpawnBurst(pr.Cfg.HitFx.Trim(), pr.Pos, MathF.Max(0.01f, pr.Cfg.HitFxScale));
    }

    /// <summary>Characters (Player2D objects other than the shooter) whose capsule AABB
    /// contains the projectile point. Applies runtime HP + global stat damage.</summary>
    private static bool TryHitCharacters(Projectile pr, EditorObjectManager mgr)
    {
        // The scene's FIRST Player2D object is "the player" — global stat damage
        // (HP bar) only applies to it; every other character uses runtime HP.
        EditorObject? mainPlayer = null;
        foreach (var o in mgr.Objects)
            if (o is { IsVisible: true, PrimitiveType: EditorPrimitiveType.Player2D })
            { mainPlayer = o; break; }

        bool hitAny = false;
        foreach (var o in mgr.Objects)
        {
            if (o is not { IsVisible: true, PrimitiveType: EditorPrimitiveType.Player2D }) continue;
            if (ReferenceEquals(o, pr.Owner)) continue; // never hit the shooter

            float r = o.Player2DCapsuleRadius;
            float minX = o.Position.X + o.Player2DCapsuleOffsetX - r;
            float maxX = o.Position.X + o.Player2DCapsuleOffsetX + r;
            float minY = o.Position.Y + o.Player2DCapsuleOffsetY - r * 0.5f;
            float maxY = o.Position.Y + o.Player2DCapsuleOffsetY + o.Player2DCapsuleHeight;
            if (pr.Pos.X < minX || pr.Pos.X > maxX || pr.Pos.Y < minY || pr.Pos.Y > maxY)
                continue;

            hitAny = true;

            // Runtime HP (phase-1 enemy health): damage → 0 hides the object.
            float hp = _runtimeHp.TryGetValue(o, out float v) ? v : DefaultRuntimeHp;
            hp -= MathF.Max(0f, pr.Cfg.DamageHP);
            if (hp <= 0f)
            {
                o.IsVisible = false; // enemy death (reappears on session reset)
                _runtimeHp.Remove(o);
                Console.WriteLine($"[Projectile] '{o.Name}' HP depleted → died (hit by {(pr.Owner?.Name ?? "trigger")})");
            }
            else
            {
                _runtimeHp[o] = hp;
                Console.WriteLine($"[Projectile] '{o.Name}' runtime HP {hp:F0} (−{pr.Cfg.DamageHP:F0})");
            }

            // Global stats: only when the target is the main player (HUD bars).
            if (ReferenceEquals(o, mainPlayer))
            {
                if (pr.Cfg.DamageHP != 0f)
                    Player2DStats.SetCurrent(PlayerStatNames.Health,
                        MathF.Max(0f, Player2DStats.Health - pr.Cfg.DamageHP));
                if (pr.Cfg.DamageMP != 0f)
                    Player2DStats.SetCurrent(PlayerStatNames.Mana,
                        MathF.Max(0f, Player2DStats.Mana - pr.Cfg.DamageMP));
                Player2DStats.ClampAll();
                Console.WriteLine($"[Projectile] Player hit → HP {Player2DStats.Health:F0}, MP {Player2DStats.Mana:F0}");
            }
            break; // one target per projectile
        }
        return hitAny;
    }

    /// <summary>Solid-collision probe at a world point (any visible collision layer).</summary>
    private static bool IsSolidAt(Tilemap2D map, float x, float y)
    {
        float cell = map.TileSize * Tilemap2D.WorldScale;
        if (cell <= 0f || map.Width <= 0 || map.Height <= 0) return false;
        int col = (int)MathF.Floor(x / cell);
        float topWorld = map.Height * cell;
        int row = (int)MathF.Floor((topWorld - y) / cell);
        if (col < 0 || col >= map.Width || row < 0 || row >= map.Height) return false;
        for (int li = 0; li < map.Layers.Count; li++)
        {
            var layer = map.Layers[li];
            if (layer == null || !layer.IsVisible || layer.CollisionTileIds == null || layer.CollisionTileIds.Count == 0)
                continue;
            int tile = map.GetTile(li, col, row);
            if (tile > 0 && layer.CollisionTileIds.Contains(tile))
                return true;
        }
        return false;
    }

    private static float GetClipDuration(string sheetName, string clipName)
    {
        if (!IDEBridge.TryGetSpriteClip(sheetName, clipName, out var sheet, out var clip)
            || sheet == null || clip == null || clip.FrameIndices.Count == 0)
            return 0.5f;
        return clip.FrameIndices.Count / MathF.Max(0.01f, clip.FPS * MathF.Max(0.01f, clip.SpeedMultiplier));
    }

    // ═══════════════════════ Session + queries ═══════════════════════

    /// <summary>Clear everything (session start / project close). Runtime HP resets too.</summary>
    public static void Clear()
    {
        _projectiles.Clear();
        _hits.Clear();
        _runtimeHp.Clear();
    }

    public static int ActiveCount => _projectiles.Count;

    /// <summary>Runtime HP of a character (phase-1 enemy health; 100 default).</summary>
    public static float GetRuntimeHp(EditorObject o) =>
        _runtimeHp.TryGetValue(o, out float v) ? v : DefaultRuntimeHp;

    public static void SetRuntimeHp(EditorObject o, float hp)
    {
        if (hp <= 0f) { _runtimeHp.Remove(o); o.IsVisible = false; }
        else _runtimeHp[o] = hp;
    }

    // ═══════════════════════ Rendering ═══════════════════════

    /// <summary>Draw every projectile + hit effect as animated sprite quads (same
    /// frame-resolution/UV math as DrawSprite2D). Called from the EditorObjectManager
    /// 2D pass after the particles.</summary>
    public static unsafe void Render(Camera camera)
    {
        if (_projectiles.Count == 0 && _hits.Count == 0) return;
        EditorObject.EnsureMap2DShaderPublic();
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();

        bool cull = GL.IsEnabled(Const.GL_CULL_FACE);
        bool depth = GL.IsEnabled(Const.GL_DEPTH_TEST);

        foreach (var pr in _projectiles)
            RenderOne(camera, pr.Cfg.Sheet, pr.Cfg.Clip, pr.Pos, pr.Cfg.WorldHeight,
                pr.AnimTime, loop: true, pr.FacingRight, pr.Cfg.RotateToVelocity
                    ? MathF.Atan2(pr.Vel.Y, pr.Vel.X) : 0f, pr.Cfg.RotateToVelocity, pr.Cfg.Z);
        foreach (var h in _hits)
            RenderOne(camera, h.Sheet, h.Clip, h.Pos, h.Height, h.Time,
                loop: false, h.FacingRight, 0f, rotate: false, 0.07f);

        if (cull) GL.Enable(Const.GL_CULL_FACE);
        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
        GL.UseProgram(Shader.GetShaderProgram());
    }

    private static unsafe void RenderOne(Camera camera, string sheetName, string clipName,
        Vector3 pos, float worldHeight, float animTime, bool loop, bool facingRight,
        float rotation, bool rotate, float z)
    {
        if (!IDEBridge.TryGetSpriteClip(sheetName, clipName, out var sheet, out var clip)
            || sheet == null || clip == null || clip.FrameIndices.Count == 0)
            return;
        if (!IDEBridge.TryGetSpriteSheetTexture(sheetName, out uint texId, out int _, out int _)
            || texId == 0)
            return;

        int count = clip.FrameIndices.Count;
        float frameDur = 1f / MathF.Max(0.01f, clip.FPS * MathF.Max(0.01f, clip.SpeedMultiplier));
        int f = (int)(animTime / frameDur);
        f = loop ? ((f % count) + count) % count : Math.Clamp(f, 0, count - 1);
        int frameIdx = clip.FrameIndices[f];

        var (uvMinRaw, uvMaxRaw) = sheet.GetFrameUV(frameIdx);
        float su0 = uvMinRaw.X, su1 = uvMaxRaw.X;
        float svBot = 1f - uvMinRaw.Y;
        float svTop = 1f - uvMaxRaw.Y;
        if (sheet.FlipY) (svBot, svTop) = (svTop, svBot);
        // Face the travel direction with a UV mirror (rotated projectiles keep their art).
        if (!facingRight && !rotate) (su0, su1) = (su1, su0);

        // Sizing: same snapshot normalization as DrawSprite2D (native px → world via
        // WorldHeight vs the clip's saved master height).
        float cellH = sheet.FrameHeight > 0 ? sheet.FrameHeight : sheet.ImageHeight;
        if (cellH <= 0) cellH = 64;
        float cellW = sheet.FrameWidth > 0 ? sheet.FrameWidth : cellH;
        if (sheet.CustomFrames != null && frameIdx < sheet.CustomFrames.Count)
        {
            var drawFrame = sheet.CustomFrames[frameIdx];
            cellW = drawFrame.Width;
            cellH = drawFrame.Height;
            if (cellH <= 0) cellH = 1;
        }
        float snapH = clip.MasterHeight;
        float pxToWorld = worldHeight / (snapH > 0f ? snapH : cellH);
        float w = MathF.Max(0.05f, cellW * pxToWorld);
        float h = MathF.Max(0.05f, cellH * pxToWorld);

        // Centered on pos (feet-agnostic): projectiles fly at their authored height.
        float cos = rotate ? MathF.Cos(rotation) : 1f;
        float sin = rotate ? MathF.Sin(rotation) : 0f;
        float hx = w * 0.5f, hy = h * 0.5f;

        // Raw float vertex buffer (54 floats = 6 verts × [pos3 uv2 tint4]) — the
        // Map2DVertex struct is private to EditorObject, and the shared VAO already
        // consumes this exact 9-float stride, so write floats directly.
        Span<float> buf = stackalloc float[54];
        int o = 0;
        for (int ci = 0; ci < QuadCorners.Length; ci++)
        {
            var (ux, uy) = QuadCorners[ci];
            float lx = ux * hx, ly = uy * hy;
            buf[o++] = pos.X + lx * cos - ly * sin;
            buf[o++] = pos.Y + lx * sin + ly * cos;
            buf[o++] = z;
            // UV map per corner index (BL, BR, TR, BL, TR, TL): u0/u1 × vBot/vTop.
            buf[o++] = ci == 1 || ci == 2 || ci == 4 ? su1 : su0;
            buf[o++] = ci == 2 || ci == 4 || ci == 5 ? svTop : svBot;
            buf[o++] = 1f; buf[o++] = 1f; buf[o++] = 1f; buf[o++] = 1f;
        }

        // Fresh matrices for THIS quad (RenderOne runs outside Render's scope).
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();
        GL.UseProgram(EditorObject.Map2DShaderProgram);
        GL.UniformMatrix4fv(EditorObject.Map2DLocView, 1, false, &view.M11);
        GL.UniformMatrix4fv(EditorObject.Map2DLocProj, 1, false, &proj.M11);
        var identity = Matrix4x4.Identity;
        GL.UniformMatrix4fv(EditorObject.Map2DLocModel, 1, false, &identity.M11);
        GL.ActiveTexture(Const.GL_TEXTURE0);
        GL.BindTexture(Const.GL_TEXTURE_2D, texId);
        GL.Uniform1i(EditorObject.Map2DLocTex, 0);
        GL.Enable(Const.GL_BLEND);
        GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
        GL.Disable(Const.GL_CULL_FACE);
        GL.Disable(Const.GL_DEPTH_TEST);

        var vao = EditorObject.GetSharedSpriteQuadVao();
        GL.BindVertexArray(vao.vao);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, vao.vbo);
        fixed (float* p = buf)
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(54 * sizeof(float)), p, Const.GL_DYNAMIC_DRAW);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
        GL.BindVertexArray(0);
        GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        GL.Disable(Const.GL_BLEND);
    }
}
