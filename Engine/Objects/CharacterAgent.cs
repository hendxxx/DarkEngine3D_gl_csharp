using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public class CharacterAgent
    {
        public enum Mentality { Aggressive, Coward }
        public enum Behavior { Wander, Chase, Fight, Flee }
        private enum Gait { Idle, Walk, Run }
        private enum CombatAct { None, Attack, Block, Hurt }

        // ---- movement tunables -------------------------------------------------
        private const float WalkSpeed = 1.6f;
        private const float RunSpeed = 4.6f;
        private const float SprintSpeed = 7.4f;
        private const float SprintAnimScale = 1.5f;
        private const float TurnRate = 5.0f;
        private const float BlendTime = 0.22f;
        private const float Radius = 0.45f;

        // ---- perception / engagement ------------------------------------------
        private const float VisionRange = 14f;
        private const float VisionCos = 0.50f;
        private const float FleeRange = 9f;
        private const float FightRange = 1.05f;
        private const float StrikeDist = 0.8f;
        private const float LungeSpeed = 2.2f;
        private const float GiveUpRange = 12f;
        private const float LoseRange = 19f;

        // ---- combat tunables ---------------------------------------------------
        public const float MaxHealth = 100f;
        private const float RegenIdle = 9f;
        private const float RegenActive = 1.5f;
        private const float HitFraction = 0.38f;
        private const float PunchDamage = 12f;
        private const float HookDamage = 22f;
        private const float BlockedMul = 0.12f;
        private const float BlockChance = 0.55f;
        private const float BlockHold = 0.6f;
        private const float HurtHold = 0.9f;
        private const float ReactMax = 0.5f;
        private const float ReactMin = 0.05f;
        private const float RetaliateTime = 4f;

        private const float FacingOffsetDeg = 0f;

        public static float CollisionRadius => Radius;
        public Mentality Temper { get; }
        public Behavior Mode { get; private set; } = Behavior.Wander;
        public CharacterAgent? Target { get; private set; }
        public float Health { get; private set; } = MaxHealth;
        public bool Dead { get; private set; }
        public float DeadElapsed => _deadTime;
        public Vector3 Position { get => _obj.Position; set => _obj.Position = value; }
        public Vector3 Forward => new(MathF.Sin(_heading), 0f, MathF.Cos(_heading));

        private readonly GltfObject _obj;
        private readonly Random _rng;

        private readonly string _idleClip;
        private readonly string _walkClip;
        private readonly string _runClip;
        private readonly string _stanceClip;
        private readonly string? _blockClip;
        private readonly string? _hurtClip;
        private readonly string? _dyingClip;
        private readonly string? _lookClip;
        private readonly string? _entryClip;
        private readonly float _victoryDur;
        private readonly List<string> _attackClips;

        private float _heading;
        private float _targetHeading;
        private float _speed;
        private float _wanderTimer;
        private Vector3 _fleeFrom;
        private Behavior _prevMode = Behavior.Wander;

        private CharacterAgent? _gaveUpOn;
        private float _giveUpTimer;

        private CombatAct _act;
        private float _actTime;
        private float _actDur;
        private float _hitAt;
        private bool _hitResolved;
        private float _pendingDamage;
        private CharacterAgent? _atkTarget;
        private float _attackCooldown;
        private float _deadTime;

        private CharacterAgent? _struckBy;
        private float _struckTimer;
        private float _victoryTimer;

        // ============================
        // AAA-style AI LOD
        // ============================
        public enum AiLodLevel { Full = 0, Reduced = 1, Simulated = 2, Frozen = 3 }
        public  AiLodLevel AiLOD = AiLodLevel.Full;

        // tick accumulator (AI tidak selalu jalan tiap frame)
        private float _aiTickAccum = 0f;
         
        private float tickInterval = 0f;

        public CharacterAgent(GltfObject obj, Random rng)
        { 

            _obj = obj;
            _rng = rng;
            Temper = _rng.NextDouble() < 0.5 ? Mentality.Aggressive : Mentality.Coward;

            var clips = obj.GetClipNames();
            _idleClip = First(clips, "idle") ?? "idle";
            _walkClip = First(clips, "walk") ?? "walk";
            _runClip = First(clips, "run") ?? _walkClip;
            _stanceClip = First(clips, "fightstance", "fightingidle", "fighting-idle", "fighting_idle", "guard", "stance")
                       ?? First(clips, "fistfight", "fighting", "fight", "boxing", "combat", "brawl")
                       ?? _idleClip;
            _blockClip = First(clips, "block", "bodyblock", "body-block", "defend");
            _hurtClip = First(clips, "hurt", "takepunch", "taking-punch", "takingpunch", "flinch", "impact");
            _dyingClip = First(clips, "dying", "death", "die", "dead");
            _lookClip = First(clips, "lookaround", "looking", "look");
            _entryClip = First(clips, "entry", "victory", "taunt", "celebrat", "win");
            _victoryDur = _entryClip != null ? MathF.Min(obj.GetClipDuration(_entryClip), 3.5f) : 0f;
            _attackClips = All(clips, "fistfight", "punchbag", "hook", "jab", "cross", "uppercut", "kick", "strike");
            _attackClips.RemoveAll(c =>
                   string.Equals(c, _stanceClip, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, _blockClip, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, _hurtClip, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, _dyingClip, StringComparison.OrdinalIgnoreCase));

            _heading = _targetHeading = RandomAngle();
            ChooseWanderAction();
        }

        // -----------------------------------------------------------------------
        //  Per-frame think with AAA-style LOD tick
        // -----------------------------------------------------------------------
        public void UpdateBehavior(float dt, IReadOnlyList<CharacterAgent> all)
        {
            if (Dead) { _deadTime += dt; return; }

            // Tentukan interval tick berdasarkan LOD
            float tickInterval = AiLOD switch
            {
                AiLodLevel.Full => LODConfig.TickFull,
                AiLodLevel.Reduced => LODConfig.TickReduced,
                AiLodLevel.Simulated => LODConfig.TickSimulated,
                AiLodLevel.Frozen => LODConfig.TickFrozen,
                _ => LODConfig.TickFull
            };

            _aiTickAccum += dt;

            // LOD3 (Frozen): hanya regen ringan, tidak combat/perception
            if (AiLOD == AiLodLevel.Frozen)
            {
                if (_aiTickAccum < tickInterval)
                    return;
                float step = _aiTickAccum;
                _aiTickAccum = 0f;

                bool restingF = Mode == Behavior.Wander && _speed <= 0.01f;
                Health = MathF.Min(MaxHealth, Health + (restingF ? RegenIdle : RegenActive) * step);
                return;
            }

            // LOD1/2: hanya jalan kalau sudah lewat interval
            if (tickInterval > 0f && _aiTickAccum < tickInterval)
                return;

            float tickDt = tickInterval > 0f ? _aiTickAccum : dt;
            _aiTickAccum = 0f;

            bool resting = Mode == Behavior.Wander && _speed <= 0.01f;
            Health = MathF.Min(MaxHealth, Health + (resting ? RegenIdle : RegenActive) * tickDt);

            if (_giveUpTimer > 0f) _giveUpTimer -= tickDt;
            if (_struckTimer > 0f) _struckTimer -= tickDt;
            if (_victoryTimer > 0f) _victoryTimer -= tickDt;

            AdvanceAction(tickDt);

            var p = Position;
            var fwd = Forward;
            CharacterAgent? nearestSeen = null; float seenDist = float.MaxValue;
            CharacterAgent? threat = null; float threatDist = float.MaxValue;
            CharacterAgent? attacker = null; float atkDist = float.MaxValue;

            // Perception LOD: LOD2 pakai radius lebih kecil dan FOV lebih ketat
            float visionRange = AiLOD == AiLodLevel.Simulated ? VisionRange * 0.6f : VisionRange;
            float visionCos = AiLOD == AiLodLevel.Simulated ? 0.7f : VisionCos;

            foreach (var o in all)
            {
                if (ReferenceEquals(o, this) || o.Dead) continue;
                float dx = o.Position.X - p.X, dz = o.Position.Z - p.Z;
                float d = MathF.Sqrt(dx * dx + dz * dz);

                if (o.Temper == Mentality.Aggressive && ReferenceEquals(o.Target, this) && d < LoseRange && d < atkDist)
                { attacker = o; atkDist = d; }

                if (d > visionRange) continue;
                float inv = d > 1e-4f ? 1f / d : 0f;
                if (fwd.X * dx * inv + fwd.Z * dz * inv < visionCos) continue;

                bool ignored = _giveUpTimer > 0f && ReferenceEquals(o, _gaveUpOn);
                bool busy = o.Mode == Behavior.Fight || o.Mode == Behavior.Chase;
                if (d < seenDist && !ignored && !busy) { nearestSeen = o; seenDist = d; }

                bool dangerous = o.Mode == Behavior.Fight || o.Mode == Behavior.Chase
                                 || (o.Temper == Mentality.Aggressive && d < FleeRange);
                if (dangerous && d < threatDist) { threat = o; threatDist = d; }
            }

            if (_struckTimer > 0f && _struckBy != null && !_struckBy.Dead)
            {
                Target = _struckBy;
                Mode = DistTo(_struckBy.Position) <= FightRange ? Behavior.Fight : Behavior.Chase;
            }
            else if (_victoryTimer > 0f)
            {
                Mode = Behavior.Wander; Target = null;
            }
            else if (Temper == Mentality.Aggressive) DecideAggressive(nearestSeen, attacker);
            else DecideCoward(threat, attacker);

            Act(tickDt);
            _prevMode = Mode;
        }

        private void DecideAggressive(CharacterAgent? seen, CharacterAgent? attacker)
        {
            if (Target != null && Target.Dead)
            {
                Target = null;
                if (_entryClip != null && _victoryDur > 0f)
                {
                    _victoryTimer = _victoryDur;
                    _obj.PlayOnce(_entryClip, _idleClip, BlendTime);
                    Mode = Behavior.Wander;
                    return;
                }
            }

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

            if (Target != null)
            {
            }
            else
                Target = seen ?? attacker;
            Mode = Target == null ? Behavior.Wander
                 : DistTo(Target.Position) <= FightRange ? Behavior.Fight
                 : Behavior.Chase;
        }

        private void DecideCoward(CharacterAgent? threat, CharacterAgent? attacker)
        {
            Target = null;
            var run = threat ?? attacker;
            if (run != null) { Mode = Behavior.Flee; _fleeFrom = run.Position; }
            else Mode = Behavior.Wander;
        }

        private void Act(float dt)
        {
            _obj.PlaybackSpeed = 1f;
            if (_victoryTimer > 0f) { _speed = 0f; return; }

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
                    if (_act == CombatAct.Attack && !_hitResolved && Target != null
                        && DistTo(Target.Position) > StrikeDist)
                        _speed = LungeSpeed;
                    break;

                default:
                    if (_act == CombatAct.None)
                    {
                        if (_prevMode != Behavior.Wander) ChooseWanderAction();
                        else { _wanderTimer -= dt; if (_wanderTimer <= 0f) ChooseWanderAction(); }
                    }
                    break;
            }
        }

        private void CombatUpdate(float dt)
        {
            if (_prevMode != Behavior.Fight && _act == CombatAct.None)
            {
                _obj.Play(_stanceClip, BlendTime);
                _attackCooldown = 0.3f + (float)_rng.NextDouble() * 0.6f;
            }

            if (_act != CombatAct.None) return;
            if (Target == null) return;
            if (DistTo(Target.Position) > FightRange * 1.35f) return;

            _obj.Play(_stanceClip, BlendTime);
            _attackCooldown -= dt;

            // Combat LOD: di Simulated, kurangi frekuensi serangan
            float cooldownMul = AiLOD == AiLodLevel.Simulated ? 1.8f : 1f;

            if (_blockClip != null && Target.IsThreateningHit(this) && _rng.NextDouble() < BlockChance)
            {
                StartBlock();
            }
            else if (_attackCooldown <= 0f)
            {
                _attackCooldown *= cooldownMul;
                StartAttack(Target);
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
            else if (_actTime >= _actDur) _act = CombatAct.None;
        }

        private void StartAttack(CharacterAgent target)
        {
            if (_attackClips.Count == 0) { _attackCooldown = 1f; return; }
            string clip = _attackClips[_rng.Next(_attackClips.Count)];
            float dur = _obj.GetClipDuration(clip);
            if (dur <= 0f) dur = 2f;

            _obj.PlayOnce(clip, _stanceClip, 0.12f);
            _act = CombatAct.Attack;
            _actTime = 0f;
            _actDur = dur;
            _hitAt = dur * HitFraction;
            _hitResolved = false;
            _atkTarget = target;
            _pendingDamage = clip.Contains("hook", StringComparison.InvariantCultureIgnoreCase) ? HookDamage : PunchDamage;
            _attackCooldown = 0.5f + (float)_rng.NextDouble() * 0.7f;
        }

        private void StartBlock()
        {
            if (_blockClip == null) return;
            _obj.PlayOnce(_blockClip, _stanceClip, 0.1f);
            _act = CombatAct.Block;
            _actTime = 0f;
            _actDur = BlockHold;
            _attackCooldown = 0.15f + (float)_rng.NextDouble() * 0.2f;
        }

        public bool IsThreateningHit(CharacterAgent victim)
        {
            if (_act != CombatAct.Attack || _hitResolved || !ReferenceEquals(_atkTarget, victim)) return false;
            float ttl = _hitAt - _actTime;
            return ttl > ReactMin && ttl < ReactMax;
        }

        public void ReceiveHit(float damage, CharacterAgent from)
        {
            if (Dead) return;
            _struckBy = from; _struckTimer = RetaliateTime;
            _victoryTimer = 0f;
            if (_act == CombatAct.Block)
            {
                Health -= damage * BlockedMul;
            }
            else
            {
                Health -= damage;
                if (_hurtClip != null)
                {
                    _obj.PlayOnce(_hurtClip, _stanceClip, 0.1f);
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
            if (_dyingClip != null) _obj.PlayOnce(_dyingClip, "", 0.15f);
            else _obj.Play(_idleClip, 0.2f);
        }

        public void Respawn(Vector3 pos)
        {
            Dead = false;
            Health = MaxHealth;
            Target = null;
            _gaveUpOn = null; _giveUpTimer = 0f;
            _struckBy = null; _struckTimer = 0f; _victoryTimer = 0f;
            _act = CombatAct.None;
            Mode = _prevMode = Behavior.Wander;
            _obj.Position = pos;
            _obj.PlaybackSpeed = 1f;
            _heading = _targetHeading = RandomAngle();
            _aiTickAccum = 0f;
            ChooseWanderAction();
        }

        private void ChooseWanderAction()
        {
            double r = _rng.NextDouble();
            Gait g = r < 0.30 ? Gait.Idle : r < 0.72 ? Gait.Walk : Gait.Run;
            _speed = g switch { Gait.Walk => WalkSpeed, Gait.Run => RunSpeed, _ => 0f };
            _targetHeading = RandomAngle();
            _wanderTimer = 2.0f + (float)_rng.NextDouble() * 4.0f;

            string idle = (Temper == Mentality.Coward && _lookClip != null) ? _lookClip : _idleClip;
            _obj.Play(g switch { Gait.Walk => _walkClip, Gait.Run => _runClip, _ => idle }, BlendTime);
        }

        // -----------------------------------------------------------------------
        //  Movement with LOD
        // -----------------------------------------------------------------------
        public void Move(float dt, TerrainChunk terrain, Vector3 center, float maxRadius)
        {
            if (Dead)
            {
                var dp = _obj.Position;
                dp.Y = terrain.GetHeightAt(dp.X, dp.Z);
                _obj.Position = dp;
                return;
            }

            // LOD3: hanya clamp Y, tidak gerak horizontal
            if (AiLOD == AiLodLevel.Frozen)
            {
                var pF = _obj.Position;
                pF.Y = terrain.GetHeightAt(pF.X, pF.Z);
                _obj.Position = pF;
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
            _heading += MathF.Abs(diff) <= step ? diff : MathF.Sign(diff) * step;
            _heading = WrapAngle(_heading);
            _obj.SetFacing(_heading * 180f / MathF.PI + FacingOffsetDeg);

            // LOD2: gerak lebih lambat (coarse)
            float speedMul = AiLOD == AiLodLevel.Simulated ? LODConfig.SimulatedSpeedMultiplier : 1f;


            if (_speed > 0f)
            {
                var f = Forward;
                p.X += f.X * _speed * speedMul * dt;
                p.Z += f.Z * _speed * speedMul * dt;
            }
            p.Y = terrain.GetHeightAt(p.X, p.Z - 0.8f);
            _obj.Position = p;
        }

        public void AvoidFrom(Vector3 other)
        {
            if (Mode != Behavior.Wander || _act != CombatAct.None) return;
            var p = _obj.Position;
            _targetHeading = MathF.Atan2(p.X - other.X, p.Z - other.Z);
            if (_wanderTimer > 0.4f) _wanderTimer = 0.4f;
        }

        private float DistTo(Vector3 t)
        {
            var p = _obj.Position; float dx = t.X - p.X, dz = t.Z - p.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        private float HeadingTo(Vector3 t) { var p = _obj.Position; return MathF.Atan2(t.X - p.X, t.Z - p.Z); }
        private float HeadingAway(Vector3 t) { var p = _obj.Position; return MathF.Atan2(p.X - t.X, p.Z - t.Z); }
        private float RandomAngle() => (float)(_rng.NextDouble() * MathF.PI * 2.0);

        private static float WrapAngle(float a)
        {
            while (a > MathF.PI) a -= MathF.PI * 2f;
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
                foreach (var k in keys)
                {
                    if (l.Contains(k))
                    {
                        res.Add(l);
                        break;
                    }
                }
            }
            return res;
        }
    }
}
