using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  CharacterAgent — autonomous character with a mentality, gaits, and melee
    //  combat (health, attacks, blocking, hit reactions, death).
    //
    //  Mentality (random): Aggressive chases and fights; Coward flees (sprints).
    //  Behavior FSM: Wander, Chase, Fight, Flee.
    //  Combat (during Fight): each fighter holds a stance, attacks on a cadence
    //  (fist-fight / punching-bag / hook), reads the opponent and may block an
    //  incoming attack (body-block), takes a hit reaction (taking-punch) and loses
    //  health when struck, and dies (dying) at 0 HP — after which the winner finds
    //  a new opponent. Health regenerates over time (faster while idle/resting).
    // ===========================================================================
    public class CharacterAgent
    {
        public enum Mentality { Aggressive, Coward }
        public enum Behavior  { Wander, Chase, Fight, Flee }
        private enum Gait     { Idle, Walk, Run }
        private enum CombatAct { None, Attack, Block, Hurt }

        // ---- movement tunables -------------------------------------------------
        private const float WalkSpeed       = 1.6f;
        private const float RunSpeed        = 4.6f;    // normal run (wander, chasing)
        private const float SprintSpeed     = 7.4f;    // terrified flee — outruns a chaser
        private const float SprintAnimScale = 1.5f;
        private const float TurnRate        = 5.0f;
        private const float BlendTime       = 0.22f;
        private const float Radius          = 0.45f;

        // ---- perception / engagement ------------------------------------------
        private const float VisionRange = 14f;
        private const float VisionCos   = 0.50f;   // ~120° FOV
        private const float FleeRange   = 9f;
        private const float FightRange  = 1.05f;   // engage at striking distance
        private const float StrikeDist  = 0.8f;    // lunge in to ~a fist's reach when punching (bodies don't clip)
        private const float LungeSpeed  = 2.2f;    // closing speed during an attack
        private const float GiveUpRange = 12f;
        private const float LoseRange   = 19f;

        // ---- combat tunables ---------------------------------------------------
        public  const float MaxHealth    = 100f;
        private const float RegenIdle    = 9f;     // HP/s while resting (idle)
        private const float RegenActive  = 1.5f;   // HP/s otherwise
        private const float HitFraction  = 0.38f;  // when in an attack clip the blow lands
        private const float PunchDamage  = 12f;
        private const float HookDamage   = 22f;
        private const float BlockedMul   = 0.12f;  // damage that leaks through a block
        private const float BlockChance  = 0.55f;  // chance to read & block an incoming hit
        private const float BlockHold    = 0.6f;   // how long a block guards
        private const float HurtHold     = 0.9f;   // stagger time after being hit
        private const float ReactMax     = 0.5f;   // can block if the hit is within this
        private const float ReactMin     = 0.05f;
        private const float RetaliateTime = 4f;    // after being struck, fight the attacker for this long

        // Model "front" yaw offset (deg). Flip 0<->180 if characters face backwards.
        private const float FacingOffsetDeg = 0f;

        public float      CollisionRadius => Radius;
        public Mentality  Temper          { get; }
        public Behavior   Mode            { get; private set; } = Behavior.Wander;
        public CharacterAgent? Target     { get; private set; }
        public float      Health          { get; private set; } = MaxHealth;
        public bool       Dead            { get; private set; }
        public float      DeadElapsed     => _deadTime;
        public Vector3    Position { get => _obj.Position; set => _obj.Position = value; }
        public Vector3    Forward  => new(MathF.Sin(_heading), 0f, MathF.Cos(_heading));

        private readonly GltfObject _obj;
        private readonly Random     _rng;

        // resolved clip names (some optional → null)
        private readonly string        _idleClip;
        private readonly string        _walkClip;
        private readonly string        _runClip;
        private readonly string        _stanceClip;
        private readonly string?       _blockClip;
        private readonly string?       _hurtClip;
        private readonly string?       _dyingClip;
        private readonly List<string>  _attackClips;

        private float    _heading;
        private float    _targetHeading;
        private float    _speed;
        private float    _wanderTimer;
        private Vector3  _fleeFrom;
        private Behavior _prevMode = Behavior.Wander;

        private CharacterAgent? _gaveUpOn;
        private float           _giveUpTimer;

        // combat action state
        private CombatAct       _act;
        private float           _actTime;
        private float           _actDur;
        private float           _hitAt;
        private bool            _hitResolved;
        private float           _pendingDamage;
        private CharacterAgent? _atkTarget;
        private float           _attackCooldown;
        private float           _deadTime;

        // retaliation (turn around and fight whoever struck you, even from behind)
        private CharacterAgent? _struckBy;
        private float           _struckTimer;

        public CharacterAgent(GltfObject obj, Random rng)
        {
            _obj = obj;
            _rng = rng;
            Temper = _rng.NextDouble() < 0.5 ? Mentality.Aggressive : Mentality.Coward;

            var clips   = obj.GetClipNames();
            _idleClip   = First(clips, "idle") ?? "idle";
            _walkClip   = First(clips, "walk") ?? "walk";
            _runClip    = First(clips, "run")  ?? _walkClip;
            _stanceClip = First(clips, "fightstance", "fightingidle", "fighting-idle", "fighting_idle", "guard", "stance")
                       ?? First(clips, "fistfight", "fighting", "fight", "boxing", "combat", "brawl")
                       ?? _idleClip;
            _blockClip  = First(clips, "block", "bodyblock", "body-block", "defend");
            _hurtClip   = First(clips, "hurt", "takepunch", "taking-punch", "takingpunch", "flinch", "impact");
            _dyingClip  = First(clips, "dying", "death", "die", "dead");
            _attackClips = All(clips, "fistfight", "punchbag", "hook", "jab", "cross", "uppercut", "kick", "strike");
            _attackClips.RemoveAll(c =>
                   string.Equals(c, _stanceClip, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, _blockClip,  StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, _hurtClip,   StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, _dyingClip,  StringComparison.OrdinalIgnoreCase));

            _heading = _targetHeading = RandomAngle();
            ChooseWanderAction();
        }

        // -----------------------------------------------------------------------
        //  Per-frame think: regen, advance any combat action, perceive, decide, act.
        // -----------------------------------------------------------------------
        public void UpdateBehavior(float dt, IReadOnlyList<CharacterAgent> all)
        {
            if (Dead) { _deadTime += dt; return; }

            bool resting = Mode == Behavior.Wander && _speed <= 0.01f;
            Health = MathF.Min(MaxHealth, Health + (resting ? RegenIdle : RegenActive) * dt);

            if (_giveUpTimer > 0f) _giveUpTimer -= dt;
            if (_struckTimer > 0f) _struckTimer -= dt;

            AdvanceAction(dt);

            var p   = Position;
            var fwd = Forward;
            CharacterAgent? nearestSeen = null; float seenDist   = float.MaxValue;
            CharacterAgent? threat      = null; float threatDist = float.MaxValue;
            CharacterAgent? attacker    = null; float atkDist    = float.MaxValue;

            foreach (var o in all)
            {
                if (ReferenceEquals(o, this) || o.Dead) continue;
                float dx = o.Position.X - p.X, dz = o.Position.Z - p.Z;
                float d  = MathF.Sqrt(dx * dx + dz * dz);

                if (o.Temper == Mentality.Aggressive && ReferenceEquals(o.Target, this) && d < LoseRange && d < atkDist)
                { attacker = o; atkDist = d; }

                if (d > VisionRange) continue;
                float inv = d > 1e-4f ? 1f / d : 0f;
                if (fwd.X * dx * inv + fwd.Z * dz * inv < VisionCos) continue;

                // Only acquire FREE opponents — never join someone else's fight or
                // pile onto a char that is already fighting/chasing.
                bool ignored = _giveUpTimer > 0f && ReferenceEquals(o, _gaveUpOn);
                bool busy    = o.Mode == Behavior.Fight || o.Mode == Behavior.Chase;
                if (d < seenDist && !ignored && !busy) { nearestSeen = o; seenDist = d; }

                bool dangerous = o.Mode == Behavior.Fight || o.Mode == Behavior.Chase
                                 || (o.Temper == Mentality.Aggressive && d < FleeRange);
                if (dangerous && d < threatDist) { threat = o; threatDist = d; }
            }

            if (_struckTimer > 0f && _struckBy != null && !_struckBy.Dead)
            {
                // Someone hit us (possibly from behind): turn around and fight back.
                Target = _struckBy;
                Mode = DistTo(_struckBy.Position) <= FightRange ? Behavior.Fight : Behavior.Chase;
            }
            else if (Temper == Mentality.Aggressive) DecideAggressive(nearestSeen, attacker);
            else                                     DecideCoward(threat, attacker);

            Act(dt);
            _prevMode = Mode;
        }

        private void DecideAggressive(CharacterAgent? seen, CharacterAgent? attacker)
        {
            if (Target != null && Target.Dead) Target = null;             // opponent down → find another

            if (Target != null)
            {
                float d = DistTo(Target.Position);
                bool sprintedAway = Target.Mode == Behavior.Flee && d > GiveUpRange;
                if (sprintedAway || d > LoseRange)
                {
                    if (sprintedAway) { _gaveUpOn = Target; _giveUpTimer = 5f; }
                    Target = null;
                }
            }

            if (Target == null) Target = seen ?? attacker;

            Mode = Target == null ? Behavior.Wander
                 : DistTo(Target.Position) <= FightRange ? Behavior.Fight
                 : Behavior.Chase;
        }

        private void DecideCoward(CharacterAgent? threat, CharacterAgent? attacker)
        {
            Target = null;
            var run = threat ?? attacker;
            if (run != null) { Mode = Behavior.Flee; _fleeFrom = run.Position; }
            else             Mode = Behavior.Wander;
        }

        // -----------------------------------------------------------------------
        //  Turn the decision into heading / speed / animation.
        // -----------------------------------------------------------------------
        private void Act(float dt)
        {
            _obj.PlaybackSpeed = 1f;

            switch (Mode)
            {
                case Behavior.Chase:
                    if (Target != null) _targetHeading = HeadingTo(Target.Position);
                    _speed = RunSpeed;
                    if (_act == CombatAct.None) _obj.Play(_runClip, BlendTime);
                    break;

                case Behavior.Flee:
                    _targetHeading = HeadingAway(_fleeFrom);
                    _speed = SprintSpeed;
                    _obj.PlaybackSpeed = SprintAnimScale;
                    if (_act == CombatAct.None) _obj.Play(_runClip, BlendTime);
                    break;

                case Behavior.Fight:
                    if (Target != null) _targetHeading = HeadingTo(Target.Position);
                    _speed = 0f;
                    CombatUpdate(dt);
                    // Lunge forward while a punch is winding up so the fist actually
                    // reaches the target, then hold once close.
                    if (_act == CombatAct.Attack && !_hitResolved && Target != null
                        && DistTo(Target.Position) > StrikeDist)
                        _speed = LungeSpeed;
                    break;

                default: // Wander
                    if (_act == CombatAct.None)
                    {
                        if (_prevMode != Behavior.Wander) ChooseWanderAction();
                        else { _wanderTimer -= dt; if (_wanderTimer <= 0f) ChooseWanderAction(); }
                    }
                    break;
            }
        }

        // -----------------------------------------------------------------------
        //  Melee combat while in Fight.
        // -----------------------------------------------------------------------
        private void CombatUpdate(float dt)
        {
            if (_prevMode != Behavior.Fight && _act == CombatAct.None)
            {
                _obj.Play(_stanceClip, BlendTime);
                _attackCooldown = 0.3f + (float)_rng.NextDouble() * 0.6f;
            }

            if (_act != CombatAct.None) return;          // mid attack / block / hurt
            if (Target == null) return;
            if (DistTo(Target.Position) > FightRange * 1.35f) return;   // drifted apart

            _obj.Play(_stanceClip, BlendTime);     // hold the guard between actions
            _attackCooldown -= dt;

            // Read the opponent: if a hit is incoming, try to block it.
            if (_blockClip != null && Target.IsThreateningHit(this) && _rng.NextDouble() < BlockChance)
            {
                StartBlock();
            }
            else if (_attackCooldown <= 0f)
            {
                StartAttack(Target);            // ...otherwise throw a punch/hook back
            }
        }

        private void AdvanceAction(float dt)
        {
            if (_act == CombatAct.None) return;
            _actTime += dt;

            if (_act == CombatAct.Attack)
            {
                if (!_hitResolved && _actTime >= _hitAt)
                {
                    _hitResolved = true;
                    if (_atkTarget != null && !_atkTarget.Dead && DistTo(_atkTarget.Position) <= FightRange * 1.6f)
                        _atkTarget.ReceiveHit(_pendingDamage, this);
                }
                if (_actTime >= _actDur) _act = CombatAct.None;
            }
            else if (_actTime >= _actDur) _act = CombatAct.None;   // Block / Hurt recovery
        }

        private void StartAttack(CharacterAgent target)
        {
            if (_attackClips.Count == 0) { _attackCooldown = 1f; return; }
            string clip = _attackClips[_rng.Next(_attackClips.Count)];
            float dur = _obj.GetClipDuration(clip);
            if (dur <= 0f) dur = 2f;

            _obj.PlayOnce(clip, _stanceClip, 0.12f);
            _act           = CombatAct.Attack;
            _actTime       = 0f;
            _actDur        = dur;
            _hitAt         = dur * HitFraction;
            _hitResolved   = false;
            _atkTarget     = target;
            _pendingDamage = clip.ToLowerInvariant().Contains("hook") ? HookDamage : PunchDamage;
            _attackCooldown = 0.5f + (float)_rng.NextDouble() * 0.7f;
        }

        private void StartBlock()
        {
            if (_blockClip == null) return;
            _obj.PlayOnce(_blockClip, _stanceClip, 0.1f);
            _act     = CombatAct.Block;
            _actTime = 0f;
            _actDur  = BlockHold;
            _attackCooldown = 0.15f + (float)_rng.NextDouble() * 0.2f;  // counter quickly
        }

        // True while this agent is mid-attack with a blow about to land on `victim`.
        public bool IsThreateningHit(CharacterAgent victim)
        {
            if (_act != CombatAct.Attack || _hitResolved || !ReferenceEquals(_atkTarget, victim)) return false;
            float ttl = _hitAt - _actTime;
            return ttl > ReactMin && ttl < ReactMax;
        }

        public void ReceiveHit(float damage, CharacterAgent from)
        {
            if (Dead) return;
            _struckBy = from; _struckTimer = RetaliateTime;   // provoke retaliation
            if (_act == CombatAct.Block)
            {
                Health -= damage * BlockedMul;           // chip damage through the guard
            }
            else
            {
                Health -= damage;
                if (_hurtClip != null)
                {
                    _obj.PlayOnce(_hurtClip, _stanceClip, 0.1f);   // stagger (cancels any attack)
                    _act = CombatAct.Hurt; _actTime = 0f; _actDur = HurtHold;
                }
            }
            if (Health <= 0f) Die();
        }

        private void Die()
        {
            Dead = true;
            Health = 0f;
            Target = null;
            _deadTime = 0f;
            _act = CombatAct.None;
            _obj.PlaybackSpeed = 1f;
            if (_dyingClip != null) _obj.PlayOnce(_dyingClip, "", 0.15f);  // play once, hold on the ground
            else                    _obj.Play(_idleClip, 0.2f);
        }

        public void Respawn(Vector3 pos)
        {
            Dead = false;
            Health = MaxHealth;
            Target = null;
            _gaveUpOn = null; _giveUpTimer = 0f;
            _struckBy = null; _struckTimer = 0f;
            _act = CombatAct.None;
            Mode = _prevMode = Behavior.Wander;
            _obj.Position = pos;
            _obj.PlaybackSpeed = 1f;
            _heading = _targetHeading = RandomAngle();
            ChooseWanderAction();
        }

        private void ChooseWanderAction()
        {
            double r = _rng.NextDouble();
            Gait g = r < 0.30 ? Gait.Idle : r < 0.72 ? Gait.Walk : Gait.Run;
            _speed = g switch { Gait.Walk => WalkSpeed, Gait.Run => RunSpeed, _ => 0f };
            _targetHeading = RandomAngle();
            _wanderTimer = 2.0f + (float)_rng.NextDouble() * 4.0f;
            _obj.Play(g switch { Gait.Walk => _walkClip, Gait.Run => _runClip, _ => _idleClip }, BlendTime);
        }

        // -----------------------------------------------------------------------
        //  Rotate toward the target heading, move, and clamp to the terrain.
        // -----------------------------------------------------------------------
        public void Move(float dt, TerrainChunk terrain, Vector3 center, float maxRadius)
        {
            if (Dead)
            {
                // Stay where we fell; the dying clip's retargeted root motion lays the
                // body down onto the ground (feet origin stays clamped to the terrain).
                var dp = _obj.Position;
                dp.Y = terrain.GetHeightAt(dp.X, dp.Z);
                _obj.Position = dp;
                return;
            }

            var p = _obj.Position;

            if (Mode == Behavior.Wander)
            {
                float fx = p.X - center.X, fz = p.Z - center.Z;
                if (fx * fx + fz * fz > maxRadius * maxRadius)
                    _targetHeading = MathF.Atan2(center.X - p.X, center.Z - p.Z);
            }

            float diff = WrapAngle(_targetHeading - _heading);
            float step = TurnRate * dt;
            _heading  += MathF.Abs(diff) <= step ? diff : MathF.Sign(diff) * step;
            _heading   = WrapAngle(_heading);
            _obj.SetFacing(_heading * 180f / MathF.PI + FacingOffsetDeg);

            if (_speed > 0f)
            {
                var f = Forward;
                p.X += f.X * _speed * dt;
                p.Z += f.Z * _speed * dt;
            }
            p.Y = terrain.GetHeightAt(p.X, p.Z);
            _obj.Position = p;
        }

        public void AvoidFrom(Vector3 other)
        {
            if (Mode != Behavior.Wander || _act != CombatAct.None) return;
            var p = _obj.Position;
            _targetHeading = MathF.Atan2(p.X - other.X, p.Z - other.Z);
            if (_wanderTimer > 0.4f) _wanderTimer = 0.4f;
        }

        // -----------------------------------------------------------------------
        private float DistTo(Vector3 t)
        {
            var p = _obj.Position; float dx = t.X - p.X, dz = t.Z - p.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        private float HeadingTo(Vector3 t)   { var p = _obj.Position; return MathF.Atan2(t.X - p.X, t.Z - p.Z); }
        private float HeadingAway(Vector3 t) { var p = _obj.Position; return MathF.Atan2(p.X - t.X, p.Z - t.Z); }
        private float RandomAngle()          => (float)(_rng.NextDouble() * MathF.PI * 2.0);

        private static float WrapAngle(float a)
        {
            while (a >  MathF.PI) a -= MathF.PI * 2f;
            while (a < -MathF.PI) a += MathF.PI * 2f;
            return a;
        }

        private static string? First(IReadOnlyList<string> names, params string[] keys)
        {
            foreach (var n in names)
            {
                var l = (n ?? "").ToLowerInvariant();
                foreach (var k in keys) if (l.Contains(k)) return n;
            }
            return null;
        }

        private static List<string> All(IReadOnlyList<string> names, params string[] keys)
        {
            var res = new List<string>();
            foreach (var n in names)
            {
                var l = (n ?? "").ToLowerInvariant();
                foreach (var k in keys) if (l.Contains(k)) { res.Add(n); break; }
            }
            return res;
        }
    }
}
