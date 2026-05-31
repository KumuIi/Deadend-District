using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using MimicSpace;

/// <summary>
/// The Mimic — a hostile orb that crawls along any surface (floor, walls, ceiling) by
/// sticking to the nearest geometry, drifts up and down as it travels, shakes constantly,
/// and lunges at the player when close.
///
/// Why NOT a NavMeshAgent: NavMesh is a floor-projected graph — it can't represent walls or
/// ceilings. The Mimic moves freely in 3D by raycasting to the nearest surface each physics
/// step and moving tangent to it (re-sticking after each step). A kinematic Rigidbody gives
/// smooth interpolated motion while we drive position manually.
///
/// Behaviour summary (per design):
///   • Idle    — wanders between random points near its surface, plays traversal sounds.
///   • Hunt    — drawn to the player (proximity) or to a Sound/Hunt stimulus; chases.
///   • Dash    — within range, a short windup then a fast lunge; contact damages the player.
///   • Stunned — when shot: knocked back, frozen ~1s, then ENRAGED (double speed) ~1s.
///
/// Audio is the only tell — it makes a notice sound on spotting the player and random
/// traversal sounds while moving, pitched to be audible-but-not-loud so a listening player
/// can locate and avoid it.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(NavMeshAgent))]
public class MonsterAI : MonoBehaviour, IStimulusListener, IPoolableSpawnedEntity
{
    // Stun is tracked by the _stunned flag (it can overlap Idle/Hunt), so it's not an
    // outer state here — only the movement-owning states are.
    private enum State { Idle, Hunt, Dash }

    // ── Inspector ───────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Health component. Subscribed for damage reaction (push/stun/enrage) and death.")]
    [SerializeField] private EnemyHealth _health;
    [Tooltip("The MimicSpace.Mimic leg-system component on this prefab. MonsterAI replaces " +
             "Movement.cs — it feeds Mimic.velocity every physics step so the leg placer knows " +
             "which direction to grow legs toward. Remove the Movement component from the prefab.")]
    [SerializeField] private Mimic _mimicLegs;
    [Tooltip("Child transform that holds the visual mesh — receives the constant shake so " +
             "the physics body and movement stay stable. Optional (the orb body child).")]
    [SerializeField] private Transform _bodyVisual;

    [Header("Surface Crawling")]
    [Tooltip("Layers the Mimic can crawl on and that block its line of sight (walls/floor/ceiling).")]
    [SerializeField] private LayerMask _surfaceMask = ~0;
    [Tooltip("Distance kept between the body centre and the surface (≈ orb radius).")]
    [SerializeField] private float _hoverDistance = 0.4f;
    [Tooltip("Max distance to a surface for the Mimic to stick to it. Keep this close to the " +
             "orb's own size — too large and it grabs door/wall geometry it's merely passing beside.")]
    [SerializeField] private float _surfaceStickRange = 1.5f;
    [Tooltip("Max degrees the cached surface normal may rotate per second. Prevents snapping " +
             "to a side wall when squeezing through a doorframe.")]
    [SerializeField] private float _maxNormalTurnRate = 90f;
    [Tooltip("Radius used for wall-collision sweep. Set to roughly half the orb's visual diameter.")]
    [SerializeField] private float _bodyRadius = 0.45f;

    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 3.5f;
    [Tooltip("Slower speed used during idle wandering — feels more like an ambient creature.")]
    [SerializeField] private float _wanderSpeed = 1.4f;
    [Tooltip("How quickly the body re-orients to the surface/travel direction.")]
    [SerializeField] private float _turnSpeed = 8f;

    [Header("Idle Wander")]
    [Tooltip("Minimum distance to the next wander target — keeps the Mimic from circling on the spot.")]
    [SerializeField] private float _wanderMinRadius = 10f;
    [Tooltip("Maximum distance to the next wander target — set high so it roams across rooms.")]
    [SerializeField] private float _wanderMaxRadius = 35f;
    [SerializeField] private float _wanderArriveDistance = 1.2f;
    [Tooltip("Chance [0..1] of pausing at each new wander destination.")]
    [SerializeField] private float _pauseChance = 0.35f;
    [Tooltip("Minimum seconds to pause.")]
    [SerializeField] private float _pauseMin = 1.2f;
    [Tooltip("Maximum seconds to pause. At intersections (T/X junctions) the full range is used.")]
    [SerializeField] private float _pauseMax = 3.5f;

    [Header("Height Switching")]
    [Tooltip("Seconds between flips of the up/down travel bias.")]
    [SerializeField] private float _heightSwitchInterval = 2f;
    [Tooltip("Strength of the vertical drift. Needs to be significant to push the Mimic off " +
             "the floor and onto walls/ceiling — values around 1.5-3 work well.")]
    [SerializeField] private float _verticalDrift = 2.5f;

    [Header("Detection")]
    [Tooltip("Player within this range (and not behind a wall) is noticed → Hunt.")]
    [SerializeField] private float _noticeRange = 14f;
    [Tooltip("Once hunting, give up if the player gets farther than this.")]
    [SerializeField] private float _loseRange = 30f;

    [Header("Dash Attack")]
    [Tooltip("Start a dash when within this range of the player during Hunt.")]
    [SerializeField] private float _dashRange = 6f;
    [Tooltip("Telegraph time before the lunge (the player's window to dodge).")]
    [SerializeField] private float _dashWindup = 0.35f;
    [SerializeField] private float _dashSpeed = 12f;
    [SerializeField] private float _dashDuration = 0.45f;
    [Tooltip("Contact distance during a dash that lands the hit.")]
    [SerializeField] private float _attackHitRange = 1.3f;
    [SerializeField] private float _attackDamage = 18f;
    [SerializeField] private float _attackCooldown = 2f;

    [Header("Damage Reaction (sensitive creature)")]
    [Tooltip("Distance knocked back along the hit direction when shot.")]
    [SerializeField] private float _knockback = 1.5f;
    [Tooltip("Seconds frozen/stunned after being shot.")]
    [SerializeField] private float _stunDuration = 1f;
    [Tooltip("Seconds after a stun ends before it can be stunned again. Prevents infinite stun-lock.")]
    [SerializeField] private float _stunCooldown = 3f;
    [Tooltip("Speed multiplier while enraged (right after the stun).")]
    [SerializeField] private float _enragedSpeedMult = 2f;
    [Tooltip("Seconds the enraged double-speed charge lasts before returning to normal.")]
    [SerializeField] private float _enragedDuration = 1f;

    [Header("Audio")]
    [Tooltip("Played once when the player is first noticed.")]
    [SerializeField] private AudioClip[] _noticeClips;
    [Tooltip("Played at random intervals while moving so the player can hear it coming.")]
    [SerializeField] private AudioClip[] _traversalClips;
    [SerializeField] private Vector2 _traversalInterval = new Vector2(2f, 5f);
    [Tooltip("Kept moderate — audible enough to locate, quiet enough to be a fair warning.")]
    [SerializeField, Range(0f, 1f)] private float _traversalVolume = 0.4f;

    [Header("Shake (mimic feel)")]
    [SerializeField] private float _shakeAmplitude = 0.05f;
    [SerializeField] private float _shakeRotation = 5f;
    [SerializeField] private float _shakeFrequency = 18f;

    [Header("Bounds (optional)")]
    [Tooltip("If set, the Mimic is kept inside this box while free-floating (no surface near). " +
             "Surface crawling already keeps it on geometry; this guards the open-air case.")]
    [SerializeField] private BoxCollider _roamBounds;

    // ── Runtime ─────────────────────────────────────────────────────────────

    private Rigidbody    _rb;
    private AudioSource  _audio;
    private NavMeshAgent _agent;
    private Transform    _player;
    private IDamageable _playerDamageable;

    private State   _state = State.Idle;
    private Vector3 _surfaceNormal = Vector3.up;
    private Vector3 _wanderTarget;
    private Vector3 _lastKnownPos;
    private bool    _hasLead;          // a sound/hunt cue is pulling us somewhere

    private float   _heightTimer;
    private float   _heightSign = 1f;  // current up/down travel bias
    private float   _traversalTimer;
    private float   _attackTimer;      // cooldown after an attack

    private bool    _stunned;
    private bool    _pausing;
    private float   _speedMult = 1f;
    private float   _stunCooldownTimer;   // counts down after a stun; blocks re-stun while > 0
    private Coroutine _reactionRoutine;

    // Stuck detection: sampled every _stuckCheckInterval seconds.
    private float   _stuckTimer;
    private Vector3 _stuckCheckPos;

    // For feeding MimicSpace.Mimic.velocity — the leg placer reads this to grow legs forward.
    private Vector3 _prevPosition;
    private Vector3 _smoothedVelocity;

    private Vector3 _shakeSeed;
    private Vector3 _visualBaseLocalPos;
    private Quaternion _visualBaseLocalRot;

    // ── IPoolableSpawnedEntity ──────────────────────────────────────────────
    public string PoolId => "enemy";
    public void OnSpawned() { }
    public void OnDespawned() => Destroy(gameObject);

    // ── IStimulusListener ─────────────────────────────────────────────────────
    private static readonly StimulusType[] _listenTypes = { StimulusType.Sound, StimulusType.Hunt };
    public StimulusType[] ListensTo => _listenTypes;

    // ── Init ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic   = true;   // we drive position manually
        _rb.useGravity    = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _audio = GetComponent<AudioSource>();
        _audio.spatialBlend = 1f;   // 3D so the player can locate it by ear

        // Agent handles pathfinding only — we drive actual movement via MovePosition.
        // updatePosition/updateRotation off so the agent never fights the kinematic body.
        _agent = GetComponent<NavMeshAgent>();
        _agent.updatePosition = false;
        _agent.updateRotation = false;
        _agent.speed          = _wanderSpeed;
        _agent.angularSpeed   = 0f;
        _agent.acceleration   = 999f; // instant steering — we control speed ourselves

        if (_health == null)
            Debug.LogError($"[MonsterAI] {name}: EnemyHealth not assigned — no damage reaction or death.");

        if ((_surfaceMask.value & (1 << gameObject.layer)) != 0)
            Debug.LogWarning($"[MonsterAI] {name}: _surfaceMask includes the Mimic's own layer — " +
                             "it may stick to or be occluded by itself. Exclude its layer.");

        // Prefer RunManager's player registration service (rulebook). The Mimic is spawned
        // during a run, so the player is already registered.
        var ph = RunManager.Instance != null ? RunManager.Instance.PlayerHealth : null;
#if UNITY_EDITOR
        // Editor-only fallback for bare test scenes with no RunManager/registration. Production
        // always has RunManager, so the rulebook-banned FindObjectOfType never ships.
        if (ph == null) ph = Object.FindObjectOfType<PlayerHealth>();
#endif
        if (ph != null)
        {
            _player           = ph.transform;
            _playerDamageable = ph;
        }
        else
        {
            Debug.LogWarning($"[MonsterAI] {name}: player not found — Mimic will only wander.");
        }

        if (_bodyVisual != null)
        {
            _visualBaseLocalPos = _bodyVisual.localPosition;
            _visualBaseLocalRot = _bodyVisual.localRotation;
        }
        _shakeSeed = new Vector3(Random.value * 100f, Random.value * 100f, Random.value * 100f);

        _wanderTarget  = transform.position;
        _prevPosition  = transform.position;
        _stuckCheckPos = transform.position;
    }

    private void OnEnable()
    {
        StimulusSystem.Instance?.Register(this);
        if (_health != null)
        {
            _health.OnDamaged += OnDamaged;
            _health.OnDeath   += OnDeath;
        }
    }

    private void OnDisable()
    {
        StimulusSystem.Instance?.Unregister(this);
        if (_health != null)
        {
            _health.OnDamaged -= OnDamaged;
            _health.OnDeath   -= OnDeath;
        }
    }

    // ── Detection / state (Update) ────────────────────────────────────────────

    private void Update()
    {
        if (_stunned) return;

        if (_attackTimer > 0f)      _attackTimer      -= Time.deltaTime;
        if (_stunCooldownTimer > 0f) _stunCooldownTimer -= Time.deltaTime;

        UpdateTimers();
        UpdateState();
        UpdateTraversalAudio();
    }

    private void UpdateTimers()
    {
        _heightTimer += Time.deltaTime;
        if (_heightTimer >= _heightSwitchInterval)
        {
            _heightTimer = 0f;
            _heightSign  = Random.value < 0.5f ? -1f : 1f;
        }
    }

    private void UpdateState()
    {
        float distToPlayer = _player != null
            ? Vector3.Distance(transform.position, _player.position)
            : Mathf.Infinity;

        switch (_state)
        {
            case State.Idle:
                // Notice the player by proximity + clear line of sight.
                if (_player != null && distToPlayer <= _noticeRange && HasLineOfSightTo(_player.position))
                {
                    EnterHunt(playNoticeSound: true);
                }
                else
                {
                    Wander();
                }
                break;

            case State.Hunt:
                // Reached the sound lead with nothing to chase → drop it so we can disengage.
                if (_hasLead && Vector3.Distance(transform.position, _lastKnownPos) <= _wanderArriveDistance)
                    _hasLead = false;

                if (_player == null || (distToPlayer > _loseRange && !_hasLead))
                {
                    _state = State.Idle;
                    break;
                }

                // Refresh the chase target. If we can see/are near the player, chase them
                // directly; otherwise head to the last cue position.
                if (_player != null && (distToPlayer <= _noticeRange || HasLineOfSightTo(_player.position)))
                {
                    _lastKnownPos = _player.position;
                    _hasLead      = false;
                }

                if (_player != null && distToPlayer <= _dashRange && _attackTimer <= 0f
                    && HasLineOfSightTo(_player.position))
                {
                    StartCoroutine(DashAttack());
                }
                break;

            case State.Dash:
                break; // driven by the dash coroutine
        }
    }

    // ── Movement (FixedUpdate) ──────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (_stunned || _state == State.Dash) return; // dash & stun move themselves

        if (_state == State.Idle && !_pausing)
        {
            _agent.speed = _wanderSpeed;
            SetAgentDestination(_wanderTarget);
            CrawlAlongPath(_wanderSpeed, Time.fixedDeltaTime);
        }
        else if (_state == State.Hunt)
        {
            _agent.speed = _moveSpeed * _speedMult;
            SetAgentDestination(CurrentDestination());
            CrawlAlongPath(_moveSpeed * _speedMult, Time.fixedDeltaTime);
        }

        // Keep the agent in sync with where we actually are so future paths start correct.
        _agent.nextPosition = _rb.position;

        CheckStuck();
        FeedMimicVelocity();
    }

    /// <summary>
    /// MimicSpace.Mimic.velocity tells the leg placer which direction to grow new legs
    /// toward (it keeps legs placed "in front" of the body). We compute it as the
    /// smoothed movement delta per second. Flat Y so legs always aim at the floor.
    /// </summary>
    private void FeedMimicVelocity()
    {
        if (_mimicLegs == null) return;
        Vector3 delta = _rb.position - _prevPosition;
        _prevPosition = _rb.position;
        Vector3 rawVel = delta / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        rawVel.y = 0f; // leg placer uses the horizontal travel direction only
        _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, rawVel, 8f * Time.fixedDeltaTime);
        _mimicLegs.velocity = _smoothedVelocity;
    }

    private Vector3 CurrentDestination()
    {
        if (_state == State.Hunt)
            return _hasLead ? _lastKnownPos : (_player != null ? _player.position : _lastKnownPos);
        return _wanderTarget;
    }

    private float _agentDestTimer;

    private void SetAgentDestination(Vector3 dest)
    {
        // Re-path at most ~5 times/sec — SetDestination is not free.
        _agentDestTimer += Time.fixedDeltaTime;
        if (_agentDestTimer < 0.2f) return;
        _agentDestTimer = 0f;
        if (_agent.isOnNavMesh)
            _agent.SetDestination(dest);
    }

    /// <summary>
    /// Crawls toward the agent's next path waypoint (steeringTarget) rather than the final
    /// destination in a straight line. This means the Mimic follows corridors and goes around
    /// obstacles exactly as the NavMesh dictates, while the surface-crawl handles height/sticking.
    /// Falls back to straight-line if the agent has no path yet.
    /// </summary>
    private void CrawlAlongPath(float speed, float dt)
    {
        Vector3 pos     = _rb.position;
        Vector3 navDest = _agent.isOnNavMesh && (_agent.hasPath || _agent.pathPending)
            ? _agent.steeringTarget
            : CurrentDestination();
        CrawlToward(navDest, speed, dt);
    }

    /// <summary>
    /// Core wall-crawler: find the nearest surface, move tangent to it toward the
    /// destination (with a vertical climb bias), then re-stick to keep the hover gap.
    /// Falls back to clamped free-flight when no surface is in range.
    /// </summary>
    private void CrawlToward(Vector3 destination, float speed, float dt)
    {
        Vector3 pos     = _rb.position;
        Vector3 desired = destination - pos;
        if (desired.sqrMagnitude < 0.0001f) return;
        desired.Normalize();

        // Include the vertical drift in the probe direction so the probe finds walls/ceiling
        // ahead of the body when climbing — not just the floor beneath it.
        Vector3 drift       = Vector3.up * (_heightSign * _verticalDrift);
        Vector3 travelDir   = (desired + drift * 0.5f).normalized;

        if (TryFindSurface(pos, travelDir, out Vector3 surfacePoint, out Vector3 surfaceNormal))
        {
            float maxDelta = _maxNormalTurnRate * dt;
            _surfaceNormal = Vector3.RotateTowards(_surfaceNormal, surfaceNormal, maxDelta * Mathf.Deg2Rad, 1f).normalized;

            // Project desired onto surface, add drift — do NOT re-project drift so it actually
            // pushes the body off the floor toward walls/ceiling instead of being cancelled out.
            Vector3 tangent = Vector3.ProjectOnPlane(desired, _surfaceNormal).normalized;
            tangent = (tangent + drift * 0.4f).normalized;
            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.up;

            Vector3 candidate = SafeMove(pos, tangent * speed * dt);

            // Re-stick: cast along the ESTABLISHED normal only (not a freshly hit wall normal)
            // so passing beside a wall doesn't snap the body into it.
            Vector3 castStart = candidate + _surfaceNormal * (_hoverDistance + 0.1f);
            if (Physics.Raycast(castStart, -_surfaceNormal, out RaycastHit restick,
                                 _hoverDistance + _surfaceStickRange, _surfaceMask, QueryTriggerInteraction.Ignore))
                candidate = restick.point + restick.normal * _hoverDistance;

            _rb.MovePosition(candidate);
            OrientBody(tangent, _surfaceNormal);
        }
        else
        {
            // No surface near — free-float toward the destination, kept inside bounds.
            Vector3 candidate = pos + desired * speed * dt;
            candidate = ClampToBounds(candidate);
            _rb.MovePosition(candidate);
            OrientBody(desired, Vector3.up);
        }
    }

    /// <summary>
    /// Probes three directions and returns the NEAREST hit so whichever surface the body
    /// is physically closest to wins — floor, wall, or ceiling. This lets the Mimic
    /// transition between surfaces naturally as it drifts toward them.
    /// Only three directions (not 8) so random side walls in doorframes still can't hijack.
    /// </summary>
    private bool TryFindSurface(Vector3 origin, Vector3 travelDir, out Vector3 point, out Vector3 normal)
    {
        point  = Vector3.zero;
        normal = Vector3.up;

        float   best  = Mathf.Infinity;
        bool    found = false;

        // Current surface — keeps the Mimic glued while steady.
        Probe(origin, -_surfaceNormal,   _surfaceStickRange,       ref best, ref point, ref normal, ref found);
        // Travel direction (includes vertical drift) — finds approaching wall/ceiling.
        Probe(origin, travelDir,          _surfaceStickRange,       ref best, ref point, ref normal, ref found);
        // Straight down fallback — recovers after losing a surface.
        Probe(origin, Vector3.down,       _surfaceStickRange * 2f,  ref best, ref point, ref normal, ref found);

        return found;

        void Probe(Vector3 o, Vector3 d, float range, ref float b, ref Vector3 p, ref Vector3 n, ref bool f)
        {
            if (d.sqrMagnitude < 0.0001f) return;
            if (Physics.Raycast(o, d.normalized, out RaycastHit h, range,
                                _surfaceMask, QueryTriggerInteraction.Ignore) && h.distance < b)
            { b = h.distance; p = h.point; n = h.normal; f = true; }
        }

        return false;
    }

    /// <summary>
    /// Sweeps a sphere in the movement direction and slides along any wall hit. Ignores
    /// hits that are parallel to the current surface normal (the floor/ceiling the Mimic is
    /// crawling on — the re-stick handles those). Up to two slides per step.
    /// </summary>
    private Vector3 SafeMove(Vector3 from, Vector3 delta)
    {
        if (delta.sqrMagnitude < 0.0001f) return from;

        Vector3 dir  = delta.normalized;
        float   dist = delta.magnitude;
        // Offset the cast origin away from the crawled surface so the sphere doesn't
        // immediately hit the floor/ceiling we're resting on.
        Vector3 castOrigin = from + _surfaceNormal * Mathf.Max(_hoverDistance * 0.5f, _bodyRadius * 0.5f);

        Vector3 result = from;
        for (int slide = 0; slide < 2; slide++)
        {
            if (!Physics.SphereCast(castOrigin, _bodyRadius, dir, out RaycastHit hit,
                                    dist, _surfaceMask, QueryTriggerInteraction.Ignore))
            {
                result = result + dir * dist;
                break;
            }

            // If the hit normal aligns with the surface we're crawling on it's a
            // floor/ceiling change — let the re-stick handle it, don't treat as a wall.
            if (Vector3.Dot(hit.normal, _surfaceNormal) > 0.5f)
            {
                result = result + dir * dist;
                break;
            }

            // Stop just before the wall, then slide the remaining movement along it.
            float safe = Mathf.Max(0f, hit.distance - 0.06f);
            result     += dir * safe;
            castOrigin += dir * safe;
            dist       -= safe;

            Vector3 slide_dir = Vector3.ProjectOnPlane(dir, hit.normal);
            if (slide_dir.sqrMagnitude < 0.001f) break; // corner — stop
            dir = slide_dir.normalized;
        }
        return result;
    }

    private void OrientBody(Vector3 forward, Vector3 up)
    {
        forward = Vector3.ProjectOnPlane(forward, up);
        if (forward.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(forward.normalized, up);
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, target, Time.fixedDeltaTime * _turnSpeed));
    }

    private Vector3 ClampToBounds(Vector3 p)
    {
        if (_roamBounds == null) return p;
        return _roamBounds.ClosestPoint(p);
    }

    // ── Wander ──────────────────────────────────────────────────────────────

    private void Wander()
    {
        if (_pausing) return;

        bool arrived = Vector3.Distance(transform.position, _wanderTarget) <= _wanderArriveDistance;
        if (!arrived) return;

        // Pick a new destination on the NavMesh so we never aim at walls/voids.
        PickNavMeshWanderTarget();

        // Decide whether to pause — higher chance at intersections (T/X junctions).
        float chance = _pauseChance;
        int openDirs = CountOpenDirections();
        if (openDirs >= 3) chance = Mathf.Max(chance, 0.65f); // junction — linger and look around

        if (Random.value < chance)
            StartCoroutine(PauseRoutine(openDirs));
    }

    private void PickNavMeshWanderTarget()
    {
        // Pick a random direction and a distance between min and max so the Mimic always
        // aims somewhere meaningfully far — Random.insideUnitSphere would pick near-points
        // too often and keep it circling the same room.
        for (int i = 0; i < 8; i++)
        {
            float dist = Random.Range(_wanderMinRadius, _wanderMaxRadius);
            Vector3 dir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            Vector3 candidate = transform.position + dir * dist;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                _wanderTarget = hit.position;
                return;
            }
        }
        // Fallback: at least move to a nearby valid point rather than standing still.
        if (NavMesh.SamplePosition(transform.position + Random.onUnitSphere * _wanderMinRadius,
                                   out NavMeshHit fallback, _wanderMinRadius, NavMesh.AllAreas))
            _wanderTarget = fallback.position;
    }

    /// <summary>
    /// Counts horizontal directions (N/S/E/W) with no wall within a short distance.
    /// 3+ open → T junction; 4 open → X/+ junction.
    /// </summary>
    private int CountOpenDirections()
    {
        int open = 0;
        float checkDist = 2.5f;
        Vector3 origin = transform.position + Vector3.up * 0.5f; // slightly above floor
        foreach (Vector3 d in _openDirProbes)
        {
            if (!Physics.Raycast(origin, d, checkDist, _surfaceMask, QueryTriggerInteraction.Ignore))
                open++;
        }
        return open;
    }

    private static readonly Vector3[] _openDirProbes =
        { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

    // ── Stuck recovery ──────────────────────────────────────────────────────

    private void CheckStuck()
    {
        // Only care when actually trying to move — not while pausing/stunned/dashing.
        if (_pausing || _stunned || _state == State.Dash) return;

        _stuckTimer += Time.fixedDeltaTime;
        if (_stuckTimer < 2f) return;
        _stuckTimer = 0f;

        float moved = Vector3.Distance(_rb.position, _stuckCheckPos);
        _stuckCheckPos = _rb.position;

        if (moved < 0.15f) // barely moved in 2 seconds → stuck
            Unstuck();
    }

    private void Unstuck()
    {
        // Lift slightly above the blocking geometry — the surface-crawl will re-stick
        // naturally once it finds floor/wall again.
        _rb.MovePosition(_rb.position + Vector3.up * (_hoverDistance * 3f));
        _agent.nextPosition = _rb.position; // re-sync agent to new position
        _agentDestTimer = 0.2f;             // force re-path next fixed step
        PickNavMeshWanderTarget();
    }

    private IEnumerator PauseRoutine(int openDirs)
    {
        _pausing = true;
        // At junctions use the full pause range; short pause otherwise.
        float duration = openDirs >= 3
            ? Random.Range(_pauseMin, _pauseMax)
            : Random.Range(_pauseMin, (_pauseMin + _pauseMax) * 0.5f);
        yield return new WaitForSeconds(duration);
        _pausing = false;
    }

    // ── Dash attack ─────────────────────────────────────────────────────────

    private IEnumerator DashAttack()
    {
        if (_player == null) yield break;
        _state = State.Dash;

        // Windup — telegraph (audible) and lock the lunge direction.
        PlayOneShot(_noticeClips, _traversalVolume * 1.2f);
        float t = 0f;
        while (t < _dashWindup) { if (_stunned) { _state = State.Hunt; yield break; } t += Time.deltaTime; yield return null; }

        Vector3 dir = (_player.position - transform.position);
        dir.y = Mathf.Clamp(dir.y, -0.5f, 0.5f); // mostly horizontal lunge
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

        // Drive the kinematic body on the physics step so MovePosition interpolation and
        // collision timing stay consistent with the crawl path.
        bool landed = false;
        t = 0f;
        while (t < _dashDuration)
        {
            if (_stunned) break;
            _rb.MovePosition(ClampToBounds(SafeMove(_rb.position, dir * _dashSpeed * Time.fixedDeltaTime)));
            OrientBody(dir, _surfaceNormal);

            if (!landed && _player != null
                && Vector3.Distance(transform.position, _player.position) <= _attackHitRange)
            {
                landed = true;
                HitPlayer();
            }
            t += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        _attackTimer = _attackCooldown;
        if (!_stunned) _state = State.Hunt;
    }

    private void HitPlayer()
    {
        if (_playerDamageable == null) return;
        var ctx = new DamageContext
        {
            Source           = gameObject,
            Instigator       = gameObject,
            HitPoint         = _player.position,
            HitNormal        = (transform.position - _player.position).normalized,
            HitZoneId        = "",
            Type             = DamageType.Melee,
            BaseDamage       = _attackDamage,
            StimulusLoudness = 0.2f,
        };
        _playerDamageable.ApplyDamage(ctx);
    }

    // ── Damage reaction: push → stun → enrage ──────────────────────────────────

    private void OnDamaged(DamageContext ctx)
    {
        if (_stunCooldownTimer > 0f) return; // immune — still cooling down from last stun
        if (_reactionRoutine != null) StopCoroutine(_reactionRoutine);
        _reactionRoutine = StartCoroutine(HitReaction(ctx));
    }

    private IEnumerator HitReaction(DamageContext ctx)
    {
        _stunned   = true;
        _speedMult = 1f;

        // Knock back a little along the shot direction (HitNormal points back toward shooter).
        Vector3 pushDir = -ctx.HitNormal;
        pushDir.y = 0f;
        if (pushDir.sqrMagnitude < 0.0001f && _player != null)
            pushDir = (transform.position - _player.position);
        pushDir = pushDir.sqrMagnitude > 0.0001f ? pushDir.normalized : transform.forward;

        Vector3 start = _rb.position;
        Vector3 end   = ClampToBounds(start + pushDir * _knockback);
        float kbTime  = Mathf.Min(0.15f, _stunDuration);
        float e = 0f;
        while (e < kbTime)
        {
            _rb.MovePosition(Vector3.Lerp(start, end, e / kbTime));
            e += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        // Sit stunned for the remainder.
        float remain = _stunDuration - kbTime;
        if (remain > 0f) yield return new WaitForSeconds(remain);

        _stunned           = false;
        _stunCooldownTimer = _stunCooldown; // immune until this expires

        // ENRAGE — commit to the player and charge at double speed briefly.
        if (_player != null)
        {
            _lastKnownPos = _player.position;
            _hasLead      = false;
            _state        = State.Hunt;
        }
        _speedMult = _enragedSpeedMult;
        yield return new WaitForSeconds(_enragedDuration);
        _speedMult = 1f;
        _reactionRoutine = null;
    }

    private void OnDeath()
    {
        StopAllCoroutines();
        if (_agent != null) _agent.isStopped = true;
        Destroy(gameObject);
    }

    // ── Stimulus ──────────────────────────────────────────────────────────────

    public void OnStimulus(in Stimulus s)
    {
        if (s.Instigator == gameObject) return;

        // Hunt sentinel (darkness timer): lock straight onto the cue position.
        if (s.Type == StimulusType.Hunt)
        {
            EnterHunt(playNoticeSound: _state == State.Idle);
            _lastKnownPos = s.Position;
            _hasLead      = true;
            return;
        }

        // Any sound draws the Mimic toward it.
        if (s.Type == StimulusType.Sound)
        {
            _lastKnownPos = s.Position;
            _hasLead      = true;
            if (_state == State.Idle) EnterHunt(playNoticeSound: false);
        }
    }

    private void EnterHunt(bool playNoticeSound)
    {
        if (_state == State.Idle && playNoticeSound)
            PlayOneShot(_noticeClips, Mathf.Clamp01(_traversalVolume + 0.2f));
        _state = State.Hunt;
    }

    // ── Audio / shake ─────────────────────────────────────────────────────────

    private void UpdateTraversalAudio()
    {
        if (_traversalClips == null || _traversalClips.Length == 0) return;
        // Idle and Hunt both drive CrawlToward, so the Mimic is traversing in both — a
        // kinematic body reports zero velocity, so gate on state rather than rb.velocity.
        bool moving = _state == State.Idle || _state == State.Hunt;
        if (!moving) return;

        _traversalTimer -= Time.deltaTime;
        if (_traversalTimer <= 0f)
        {
            _traversalTimer = Random.Range(_traversalInterval.x, _traversalInterval.y);
            PlayOneShot(_traversalClips, _traversalVolume);
        }
    }

    private void PlayOneShot(AudioClip[] clips, float volume)
    {
        if (_audio == null || clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip != null) _audio.PlayOneShot(clip, volume);
    }

    private void LateUpdate()
    {
        if (_bodyVisual == null) return;

        // Constant jitter on the visual only — the "mimic" twitch. Perlin so it's smooth
        // but irregular, not a sine wobble.
        float ti = Time.time * _shakeFrequency;
        Vector3 offset = new Vector3(
            (Mathf.PerlinNoise(_shakeSeed.x, ti) - 0.5f),
            (Mathf.PerlinNoise(_shakeSeed.y, ti) - 0.5f),
            (Mathf.PerlinNoise(_shakeSeed.z, ti) - 0.5f)) * (2f * _shakeAmplitude);

        _bodyVisual.localPosition = _visualBaseLocalPos + offset;
        _bodyVisual.localRotation = _visualBaseLocalRot * Quaternion.Euler(
            offset.x / Mathf.Max(0.0001f, _shakeAmplitude) * _shakeRotation,
            offset.y / Mathf.Max(0.0001f, _shakeAmplitude) * _shakeRotation,
            offset.z / Mathf.Max(0.0001f, _shakeAmplitude) * _shakeRotation);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private bool HasLineOfSightTo(Vector3 worldPos)
    {
        Vector3 from = transform.position;
        Vector3 dir  = worldPos - from;
        float dist   = dir.magnitude - 0.3f;
        if (dist <= 0f) return true;
        // Blocked if a surface is between the Mimic and the point. Triggers (pickups, zones)
        // never occlude.
        return !Physics.Raycast(from, dir.normalized, dist, _surfaceMask, QueryTriggerInteraction.Ignore);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, _noticeRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _dashRange);
    }
}
