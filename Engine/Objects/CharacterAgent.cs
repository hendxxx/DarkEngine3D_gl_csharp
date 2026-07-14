using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public class CharacterAgent
    {
        public bool IsPlayer = false;
        public enum Mentality { Aggressive, Coward }
        public enum Behavior { Wander, Chase, Fight, Flee }
        private enum Gait { Idle, Walk, Run }
        private enum CombatAct { None, Attack, Block, Hurt }

        // ---- movement tunables -------------------------------------------------
        private float WalkSpeed = Config.AIConfig.Walk ;
        private float RunSpeed = Config.AIConfig.Run;
        private float SprintSpeed = Config.AIConfig.Sprint;
        private const float SprintAnimScale = 1.5f;
        private const float TurnRate = 5.0f;
        private const float BlendTime = 0.22f;
        private const float Radius = 0.45f;
        private const float CharacterHeight = 1.8f; // typical human height
         
        public float CollisionHeight = CharacterHeight;

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
        public static float CapsuleHeight => CharacterHeight;
        public Mentality Temper { get; }
        public Behavior Mode { get; private set; } = Behavior.Wander;
        public CharacterAgent? Target { get; private set; }
        public float Health { get; private set; } = MaxHealth;
        public void SetHealth(float health) => Health = Math.Clamp(health, 0f, MaxHealth);
        public bool Dead { get; private set; }
        public float DeadElapsed => _deadTime;
        public Vector3 Position { get => _obj.Position; set => _obj.Position = value; }
        public Vector3 Forward
        {
            get
            {
                if (IsPlayer)
                {
                    // PLAYER: heading = derajat, sistem kamera (COS–SIN)
                    float rad = _heading * (MathF.PI / 180f);
                    return new Vector3(MathF.Cos(rad), 0f, MathF.Sin(rad));
                }
                else
                {
                    // AI: heading = radian, sistem Atan2 (SIN–COS)
                    float rad = _heading;
                    return new Vector3(MathF.Sin(rad), 0f, MathF.Cos(rad));
                }
            }
        }




        private readonly GltfObject _obj;
        private readonly Random _rng;

        private readonly string _idleClip;
        private readonly List<string> _idleClips;
        private readonly string _walkClip;
        private readonly List<string> _walkClips;
        private readonly List<string> _runClips;

        private readonly List<string> _backwardClips;
        private readonly string _strafeLeftClips;
        private readonly string _strafeRightClips;
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

        public float Heading
        {
            get => _heading;
            set
            {
                _heading = value;
                lastHeading = value;
            }
        }



        // ============================
        // AAA-style AI LOD
        // ============================
        public enum AiLodLevel { Full = 0, Reduced = 1, Simulated = 2, Frozen = 3 }
        public  AiLodLevel AiLOD = AiLodLevel.Full;

        // tick accumulator (AI tidak selalu jalan tiap frame)
        private float _aiTickAccum = 0f;
           
        public CharacterAgent(GltfObject obj, Random rng)
        { 

            _obj = obj;
            _rng = rng;
            Temper = _rng.NextDouble() < 0.5 ? Mentality.Aggressive : Mentality.Coward;

            var clips = obj.GetClipNames();
            _idleClip = First(clips, "idle", "natural-idle") ?? "idle";
            _walkClip = First(clips, "walk" ) ?? "walk";
             
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


            //Player 

            _walkClips = All(clips, "walk");

            if (_walkClips.Count == 0)
            {
                _walkClips.Add("walk");
                _walkClips.Add("walk-happy");
                _walkClips.Add("walk-standard");
                _walkClips.Add("zombie-walk");
            }

            _runClips = All(clips, "run");
            if (_runClips.Count == 0)
            {
                _runClips.Add("run");
            } 

            _backwardClips = All(clips, "backward", "backward2", "walking-backwards");
            if (_backwardClips.Count == 0)
            {
                _backwardClips.Add("backward");
                _backwardClips.Add("backward2");
            }
            _strafeLeftClips = First(clips, "strafeleft") ?? "strafeleft";
            _strafeRightClips = First(clips, "straferight") ?? "straferight";
             
            ChooseWanderAction();
        }

        public void OnCameraModeChanged(CameraMode mode)
        {
            if (!IsPlayer) return;

            if (mode == CameraMode.FirstPerson)
            {
                // Hide head mesh in 1st person
                _obj.HideMeshByNodeName("Head");
                _obj.HideMeshByNodeName("Neck");
                // Show hand meshes in 1st person if available
                _obj.ShowMeshByNodeName("Hand");
                _obj.ShowMeshByNodeName("Arm");
            }
            else
            {
                // Show everything in 3rd person
                _obj.ShowAllMeshes();
            }
        }

        // -----------------------------------------------------------------------
        //  Per-frame think with AAA-style LOD tick
        // -----------------------------------------------------------------------
        public void UpdateBehavior(float dt, IReadOnlyList<CharacterAgent> all)
        {
            if (IsPlayer)
            {
                // Player tidak pakai AI
                return;
            }

            if (Dead) { _deadTime += dt; return; }

            float tickInterval = AiLOD switch
            {
                AiLodLevel.Full => LODConfig.TickFull,
                AiLodLevel.Reduced => LODConfig.TickReduced,
                AiLodLevel.Simulated => LODConfig.TickSimulated,
                AiLodLevel.Frozen => LODConfig.TickFrozen,
                _ => LODConfig.TickFull
            };

            _aiTickAccum += dt;

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

        private bool wasFreeLook = false;
        private bool justReleasedFreeLook = false;
        private float lastHeading = 0f;
         
        // ---- locomotion system (UE-style) -------------------------------------
        private float _currentSpeed = 0f;
        private Vector3 _moveDirection = Vector3.Zero;
        // ---- locomotion tuning (feel the weight!) ----
        private const float Acceleration = 6.0f;    // fast build-up ~0.17s to full speed
        private const float Deceleration = 6.0f;    // snappy stop
        private const float GroundFriction = 3.0f;  // good friction — stops quickly
        // Speed values (walk/run feel responsive)
        private const float WalkSpeedPlayer = 1.5f;
        private const float RunSpeedPlayer = 2.0f;
        private const float MaxStepHeight = 0.45f;
        public List<StaticObjectManager>? StaticManagers;

        // ---- gait blend: smooth walk↔run transition ----
        private float _gaitBlend = 0f;      // 0 = walk, 1 = run (smoothly interpolated)
        private const float GaitBlendAccel = 6.0f;   // walk→run: ~0.17s to reach 63%
        private const float GaitBlendDecel = 10.0f;   // run→walk: ~0.1s to reach 63%

        /// <summary>Physics body for velocity-based movement (gravity, ground state, impulses).</summary>
        private float headingVelocity = 0f;
        private bool _isJumping = false;
        // -----------------------------------------------------------------------
        //  Movement with LOD
        // -----------------------------------------------------------------------
        public void Move(nint window, Camera camera, float dt, TerrainChunk? terrain, Vector3 center, float maxRadius)
        { 
            // ============================
            // PLAYER CONTROL
            // ============================
            if (IsPlayer)
            {
                if (camera.IsFlyMode) return;

                // ── FREE LOOK / HEADING ──
                justReleasedFreeLook = wasFreeLook && !camera.freeLook;
                wasFreeLook = camera.freeLook;

                bool allowOrbit = camera.CurrentPreset?.AllowFreeLook ?? false;

                if (camera.freeLook || allowOrbit)
                {
                    lastHeading = _heading;
                }
                else
                {
                    if (camera.CurrentMode == CameraMode.FirstPerson)
                        _heading = camera.Yaw;
                    else
                    {
                        float smoothTime = justReleasedFreeLook ? 0.35f : 0.12f;
                        _heading = Helpers.OGLMath.SmoothDampAngle(
                            lastHeading, camera.Yaw, ref headingVelocity, smoothTime, dt);
                    }
                    lastHeading = _heading;
                }

                // ── Movement vectors ──
                float rad = Helpers.OGLMath.ToRadians(_heading);
                Vector3 forward = new(MathF.Sin(rad), 0, MathF.Cos(rad));

                if (camera.CurrentMode == CameraMode.FirstPerson)
                {
                    wasFreeLook = false;
                    forward = new Vector3(camera.Front.X, 0, camera.Front.Z);
                    if (forward.LengthSquared() < 0.0001f)
                        forward = Forward;
                    else
                        forward = Vector3.Normalize(forward);
                }
                Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

                float speedWalkVal = WalkSpeedPlayer;
                float speedRunVal = RunSpeedPlayer;
                _isRunning = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT_SHIFT);

                // ── Input to desired movement ──
                Vector3 inputDir = Vector3.Zero;
                if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_W)) inputDir += forward;
                if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_S)) inputDir -= forward;
                if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_A)) inputDir -= right;
                if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_D)) inputDir += right;

                // ── Air control: horizontal movement is NOT blocked during jump!
                // Velocity-based physics allows reduced control in the air via AirControlFactor.
                bool hasInput = inputDir.LengthSquared() > 0.0001f;
                if (hasInput) inputDir = Vector3.Normalize(inputDir);

                // ── Scale multiplier ──
                float moveScaleMul = 1.0f;
                if (ScaleConfig.ScaleMovement)
                {
                    moveScaleMul = ScaleHelpers.Normalize(
                        _obj.Scale, ScaleConfig.MovementBaseScale,
                        ScaleConfig.MovementMinMul, ScaleConfig.MovementMaxMul);
                }

                // ── Gait blend: smooth walk↔run transition ──
                // _gaitBlend smoothly interpolates toward desired gait (0=walk, 1=run)
                float desiredGait = (_isRunning && hasInput) ? 1f : 0f;
                float gaitAccel = desiredGait > _gaitBlend ? GaitBlendAccel : GaitBlendDecel;
                float gaitFactor = 1f - MathF.Exp(-gaitAccel * dt);
                _gaitBlend += (desiredGait - _gaitBlend) * gaitFactor;

                float walkTarget = speedWalkVal * moveScaleMul;          // 1.5
                float runTarget = speedRunVal * moveScaleMul;            // 2.5

                float targetSpeed;
                if (hasInput)
                {
                    // Speed blends smoothly: walk → run on shift, run → walk on release
                    targetSpeed = walkTarget + _gaitBlend * (runTarget - walkTarget);
                }
                else if (_currentSpeed > walkTarget + 0.1f)
                {
                    // Decelerating from run: first slow to walk speed
                    targetSpeed = walkTarget;
                }
                else
                {
                    targetSpeed = 0f;
                }

                // ── Acceleration / Deceleration (smooth, weighty) ──
                float accel = hasInput ? Acceleration : Deceleration;
                float lerpFactor = 1f - MathF.Exp(-accel * dt);
                _currentSpeed += (targetSpeed - _currentSpeed) * lerpFactor;
                
                // Extra friction when stopping
                if (!hasInput && _currentSpeed > 0f)
                    _currentSpeed -= GroundFriction * dt;
                
                if (_currentSpeed < 0.01f) _currentSpeed = 0f;

                // ── Movement direction (keep last direction during deceleration!) ──
                if (hasInput) _moveDirection = inputDir;
                // When no input, _moveDirection stays from last frame so
                // backward deceleration stays backward (not snap to forward)

                // ── Calculate new position ──
                var pos = Position;
                Vector3 newPos = pos + _moveDirection * _currentSpeed * dt;

                // ── Terrain height (with step-up) ──
                float currentTerrainY = terrain.GetHeightAt(pos.X, pos.Z);
                float newTerrainY = terrain.GetHeightAt(newPos.X, newPos.Z);
                float terrainStep = newTerrainY - currentTerrainY;

                if (terrainStep > 0f && terrainStep <= MaxStepHeight)
                {
                    // Step up onto small terrain rise
                    newPos.Y = newTerrainY;
                }
                else if (terrainStep > MaxStepHeight)
                {
                    // Too steep — don't move up, just stay in place
                    newPos = pos;
                    _currentSpeed *= 0.3f;
                }
                else
                {
                    // Downhill or flat — follow terrain
                    newPos.Y = newTerrainY;
                }

                // Ensure Y is on terrain
                newPos.Y = MathF.Max(newPos.Y, terrain.GetHeightAt(newPos.X, newPos.Z));

                // =====================
                // ACTION INPUTS
                // =====================

                // --- PUNCH ---
                if (Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT) && !_oneShotPlaying && !_isJumping)
                {
                    _oneShotPlaying = true;
                    _oneShotName = "hook";
                    _obj.PlayOnce("hook", "fightstance");
                }
                // --- BLOCK ---
                else if (Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_MIDDLE) && !_oneShotPlaying && !_isJumping)
                {
                    _oneShotPlaying = true;
                    _oneShotName = "block";
                    _obj.PlayOnce("block", "fightstance");
                }
                // Ensure Y is always on terrain height
                newPos.Y = terrain.GetHeightAt(newPos.X, newPos.Z);

                // =====================
                // ONE-SHOT CHECK
                // =====================
                if (_oneShotPlaying)
                {
                    if (!_obj.IsPlaying(_oneShotName))
                    {
                        _oneShotPlaying = false;
                        _oneShotName = "";
                        _obj.PlaybackSpeed = 1f;


                    }
                    else
                    {
                        Position = newPos;
                        _obj.Position = newPos;
                        _obj.SetFacing(_heading);
                        return;
                    }
                }

                // ── Jump animation still playing — we don't return early anymore!
                // Physics allows horizontal air control, so we continue to the
                // locomotion animation section below to play appropriate animations.

                // =====================
                // LOCOMOTION ANIMATION (speed + direction)
                // =====================
                float speed = _currentSpeed;

                if (speed < 0.1f)
                {
                    // ── IDLE ──
                    _obj.Play("idle", 0.25f);
                    _isWalking = false;
                    _isBackward = false;
                    _currentGait = Gait.Idle;
                    _obj.PlaybackSpeed = 1f;
                }
                else
                {
                    // Movement direction relative to facing
                    float fwdDot = Vector3.Dot(_moveDirection, forward);
                    float rightDot = Vector3.Dot(_moveDirection, right);

                    bool isBwd  = fwdDot < -0.3f;
                    bool isStrafeL = rightDot < -0.3f;
                    bool isStrafeR = rightDot > 0.3f;

                    // speedRatio follows gait blend so animation matches intent
                    float walkMax = speedWalkVal * moveScaleMul;
                    float runMax = speedRunVal * moveScaleMul;
                    float targetMax = walkMax + _gaitBlend * (runMax - walkMax);
                    float speedRatio = targetMax > 0.01f ? MathF.Min(1f, speed / targetMax) : 0f;

                    string animClip;
                    float blend = 0.22f;
                    _obj.PlaybackSpeed = 0.5f + speedRatio * 0.8f;

                    if (isBwd && !(fwdDot > 0.3f))
                    {
                        // ── BACKWARD ──
                        if (!_isBackward || string.IsNullOrEmpty(_currentBackwardClip))
                            _currentBackwardClip = _backwardClips[_rng.Next(_backwardClips.Count)];
                        animClip = _currentBackwardClip;
                        _isBackward = true;
                        _isWalking = false;
                        _currentGait = Gait.Walk;
                    }
                    else if (isStrafeL && !(fwdDot > 0.3f))
                    {
                        // ── STRAFE LEFT (no forward) ──
                        animClip = "strafeleft";
                        _isBackward = false;
                        _isWalking = false;
                        _currentGait = Gait.Walk;
                    }
                    else if (isStrafeR && !(fwdDot > 0.3f))
                    {
                        // ── STRAFE RIGHT (no forward) ──
                        animClip = "straferight";
                        _isBackward = false;
                        _isWalking = false;
                        _currentGait = Gait.Walk;
                    }
                    else
                    {
                        // ── FORWARD ──
                        // Run anim when gait blend > 50% (smooth transition)
                        bool useRunAnim = _gaitBlend > 0.5f;

                        if (useRunAnim)
                        {
                            _currentRunClip = _runClips[_rng.Next(_runClips.Count)];
                            animClip = _currentRunClip;
                            blend = 0.18f;
                            _currentGait = Gait.Run;
                        }
                        else
                        {
                            if (!_isWalking || string.IsNullOrEmpty(_currentWalkingClip))
                                _currentWalkingClip = _walkClips[_rng.Next(_walkClips.Count)];
                            animClip = _currentWalkingClip;
                            _isWalking = true;
                            blend = 0.25f;
                            _currentGait = Gait.Walk;
                        }
                        _isBackward = false;
                    }

                    _obj.Play(animClip, blend);
                }

                // ── Apply final position + rotation ──
                Position = newPos;
                _obj.Position = newPos;
                _obj.SetFacing(_heading);
                return;
            }


            // ============================
            // NPC AI (kode lama tetap)
            // ============================

            if (Dead)
            {
                var dp = _obj.Position;
                dp.Y = terrain.GetHeightAt(dp.X, dp.Z);
                _obj.Position = dp;
                return;
            }

            // LOD3: hanya clamp Y
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

            float speedMul = AiLOD == AiLodLevel.Simulated
                ? LODConfig.SimulatedSpeedMultiplier
                : 1f;

            // ── NPC horizontal movement with obstacle avoidance ──
            if (_speed > 0f)
            {
                var f = Forward;
                float moveScaleMul = 1.0f;
                if (ScaleConfig.ScaleMovement)
                {
                    moveScaleMul = ScaleHelpers.Normalize(
                        _obj.Scale,
                        ScaleConfig.MovementBaseScale,
                        ScaleConfig.MovementMinMul,
                        ScaleConfig.MovementMaxMul
                    );
                }

                float stepX = f.X * _speed * speedMul * moveScaleMul * dt;
                float stepZ = f.Z * _speed * speedMul * moveScaleMul * dt;

    p.X += stepX;
                p.Z += stepZ;
            }            // ── NPC clamp to terrain height ──
            p.Y = terrain.GetHeightAt(p.X, p.Z);

            _obj.Position = p;
        }
        

        private enum AnimState
        {
            Idle,
            Move,
            Punch,
            Block,
            Jump
        }
        private AnimState _animState = AnimState.Idle;

        private bool _punchPlaying = false;
        private bool _blockPlaying = false;


        private bool _isBackward = false;
        private string _currentBackwardClip;


        private Gait _currentGait = Gait.Idle;
        private bool _isWalking = false;
        private bool _isRunning = false;
        private string _currentWalkingClip;
        private string _currentRunClip;

        private bool _oneShotPlaying = false;
        private string _oneShotName = "";
        private void PlayerMovement(nint window, float dt)
        {
            
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
