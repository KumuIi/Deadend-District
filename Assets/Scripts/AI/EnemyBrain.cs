using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Two-level FSM for a guard NPC.
///
/// Outer states: Patrol → Investigate → Engage.
/// Inner EngagePhase (active only during Engage):
///   BadPosition  — caught in the open: shoot immediately while slowly backing away.
///                  LOS lost → SeekCover(LostLOS); NeedsReload → SeekCover(Reload).
///   SeekCover    — sprint to cover for any reason (Reload / QuickCover / LostLOS / ForcedRelocation).
///                  On arrival → Peek. No cover found → fallback per intent.
///   Peek         — behind cover: shoot immediately when player visible, hold 30 s on LOS loss.
///
/// Engage entry: cover within _quickCoverDist → SeekCover(QuickCover); otherwise → BadPosition.
/// Retreat to cover ONLY on reload or LOS loss — never randomly.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyBrain : MonoBehaviour
{
    private enum BrainState  { Patrol, Suspicious, Investigate, Engage }
    private enum EngagePhase { BadPosition, Peek, SeekCover }
    private enum CoverIntent { Reload, QuickCover, LostLOS, ForcedRelocation }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Required Components")]
    [SerializeField] private EnemyPerception   _perception;
    [SerializeField] private EnemyAimComponent _aimComponent;
    [SerializeField] private EnemyWeaponDriver _weaponDriver;
    [SerializeField] private EnemyHealth       _health;
    [SerializeField] private PatrolRoute       _patrolRoute;

    [Header("Weapon Setup")]
    [SerializeField] private WeaponSO   _weaponData;
    [SerializeField] private GameObject _gunInstance;

    [Header("Gun Transform Names")]
    [SerializeField] private string _muzzleName    = "MuzzlePoint";
    [SerializeField] private string _rightGripName = "RightHandGrip";
    [SerializeField] private string _leftGripName  = "LeftHandGrip";

    [Header("Combat")]
    [SerializeField] private float     _coverSearchRadius = 8f;
    [SerializeField] private int       _coverSampleCount  = 12;
    [SerializeField] private float     _eyeHeight         = 1.4f;
    [SerializeField] private float     _fireAccuracy      = 0.7f;
    [SerializeField] private LayerMask _coverMask;

    [Header("Fire Pattern")]
    [SerializeField] private int   _burstCount    = 3;
    [SerializeField] private float _burstCooldown = 1.5f;

    [Header("BadPosition — Open-Ground Fighting")]
    [Tooltip("Max distance to cover for a QuickCover sprint on first detection.")]
    [SerializeField] private float _quickCoverDist      = 6f;
    [Tooltip("How far (metres) to step backward per interval while in BadPosition.")]
    [SerializeField] private float _backStepDist        = 3f;
    [Tooltip("Seconds between each backward step destination update.")]
    [SerializeField] private float _backStepInterval    = 1.5f;
    [Tooltip("Agent speed while backing away in BadPosition.")]
    [SerializeField] private float _backStepSpeed       = 1.8f;
    [Tooltip("Seconds of continuous LOS loss before BadPosition reacts (debounce for brief occlusions).")]
    [SerializeField] private float _losDebounce         = 1.5f;
    [Tooltip("Minimum distance a ForcedRelocation cover point must be from the current cover position.")]
    [SerializeField] private float _forceRelocateMinDist = 3f;

    [Header("Cover Movement")]
    [Tooltip("Agent speed while sprinting to cover.")]
    [SerializeField] private float _coverSprintSpeed   = 5f;
    [Tooltip("Max seconds allowed to reach cover before fallback triggers.")]
    [SerializeField] private float _coverSeekTimeout   = 4f;
    [Tooltip("Pause before an exposed emergency reload when no cover exists.")]
    [SerializeField] private float _emergencyReloadDelay = 2f;

    [Header("Engage Timers")]
    [Tooltip("Seconds of LOS loss from Peek before slow-pushing to LastKnownPosition.")]
    [SerializeField] private float _holdGroundTimeout   = 30f;
    [SerializeField] private float _investigateSlowSpeed = 1.5f;

    [Header("Investigate")]
    [SerializeField] private float _investigateTimeout = 8f;
    [Tooltip("Wary movement speed while investigating a sound — slower than patrol so the guard reads as cautious.")]
    [SerializeField] private float _investigateMoveSpeed = 1.6f;

    [Header("Awareness — Sound Reaction")]
    [Tooltip("Heard-sound loudness (0..1) at/above which the guard turns and investigates the location (sprint footstep, gunshot).")]
    [SerializeField] private float _investigateLoudness = 0.45f;
    [Tooltip("Heard-sound loudness (0..1) at/above which the guard pauses and grows suspicious (someone walking nearby).")]
    [SerializeField] private float _suspiciousLoudness  = 0.15f;
    [Tooltip("Seconds the guard scans in place when suspicious before resuming patrol.")]
    [SerializeField] private float _suspiciousScanTime  = 4f;
    [Tooltip("How far left/right (degrees) the guard sweeps its view while suspicious or investigating.")]
    [SerializeField] private float _scanSweepAngle      = 70f;
    [Tooltip("Sweep oscillation speed (radians/sec) for the look-around scan.")]
    [SerializeField] private float _scanRate            = 2.2f;

    [Header("Movement")]
    [SerializeField] private float _rotationSpeed = 360f;

    [Header("Damage Response")]
    [Tooltip("Hits at the same cover position before relocating.")]
    [SerializeField] private int _hitsToChangeCover = 3;

    [Header("First-Detection Reaction")]
    [Tooltip("Seconds the guard takes to react on first detection before firing. Resets each time Engage is entered.")]
    [SerializeField] private float _reactionDelay = 1f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private float        _defaultAgentSpeed;
    private BrainState   _state = BrainState.Patrol;
    private EngagePhase  _engagePhase;
    private Coroutine    _stateCoroutine;
    private Transform    _playerTransform;
    private int          _patrolIndex;

    // Awareness scan
    private float        _scanPhase;    // advances every frame; drives the look-around sweep
    private Vector3      _scanBaseDir;  // facing the heard sound while Suspicious

    // Death
    private Vector3 _lastHitNormal; // direction of the killing/last shot — used to throw the dropped weapon

    // Engage shared state
    private bool          _reactedThisEngage; // false until reaction delay has elapsed once per Engage
    private bool          _isInCover;
    private float         _losTimer;
    private bool          _shouldChangeCover;
    private int           _hitsAtCurrentCover;
    private Vector3?      _cachedCoverPoint;
    private CoverIntent?  _pendingIntent;   // set by phases before yielding break
    private bool          _goToInvestigate; // set when any phase wants to exit to Investigate

    // ── Init ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = false;
        _defaultAgentSpeed    = _agent.speed;

        // W3-07: ladders are Mimic-only. Guards must never path over ladder NavMeshLinks.
        NavAreas.ExcludeLadder(_agent);

        ValidateRefs();

        var ph = Object.FindObjectOfType<PlayerHealth>();
        _playerTransform = ph != null ? ph.transform : null;
        if (_playerTransform == null)
            Debug.LogError($"[EnemyBrain] {name}: PlayerHealth not found — AI will not function.");

        _perception?.Initialize(_playerTransform);
        SetupGunAndIK();

        if (_health     != null) _health.OnDeath               += Die;
        if (_health     != null) _health.OnDamaged             += OnHealthDamaged;
        if (_perception != null) _perception.OnPerceptionEvent += OnPerceptionEvent;
    }

    private void Start() => SetState(BrainState.Patrol);

    /// <summary>
    /// Assigns the patrol route for this guard. Called by <see cref="EnemySpawnPoint"/>
    /// right after Instantiate — before Start() runs PatrolRoutine, so the route is in
    /// place by the time patrolling begins. Lets one guard prefab serve many spawn points
    /// with different scene routes, instead of baking a route into the prefab.
    /// </summary>
    public void AssignPatrolRoute(PatrolRoute route)
    {
        if (route != null) _patrolRoute = route;
    }

    private void Update()
    {
        _scanPhase += Time.deltaTime * _scanRate;
        RotateBody();

        if (_state == BrainState.Engage)
        {
            if (_perception != null && !_perception.CanSeeTarget)
                _losTimer += Time.deltaTime;
            else
                _losTimer = 0f;
        }
    }

    // ── Body rotation ─────────────────────────────────────────────────────────

    private void RotateBody()
    {
        Vector3 targetDir;

        if (_state == BrainState.Engage && _playerTransform != null)
        {
            // Lock straight onto the player — no sweep while fighting.
            targetDir   = _playerTransform.position - transform.position;
            targetDir.y = 0f;
        }
        else if (_state == BrainState.Suspicious)
        {
            // Stand and sweep around the direction the sound came from ("looking around").
            targetDir = ApplyScanSweep(_scanBaseDir);
        }
        else
        {
            // Patrol / Investigate: face travel direction. While investigating, weave the
            // view left/right so the guard reads as alert and scanning rather than tunnel-walking.
            if (_agent.velocity.sqrMagnitude > 0.04f)
                targetDir = _agent.velocity;
            else if (_state == BrainState.Investigate)
                targetDir = transform.forward; // standing mid-investigate — keep scanning in place
            else
                return;

            targetDir.y = 0f;
            if (_state == BrainState.Investigate)
                targetDir = ApplyScanSweep(targetDir);
        }

        if (targetDir.sqrMagnitude < 0.001f) return;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(targetDir),
            _rotationSpeed * Time.deltaTime);
    }

    /// <summary>Rotates a base facing direction left/right by an oscillating yaw to fake a head/body scan.</summary>
    private Vector3 ApplyScanSweep(Vector3 baseDir)
    {
        baseDir.y = 0f;
        if (baseDir.sqrMagnitude < 0.001f) return baseDir;
        float yaw = Mathf.Sin(_scanPhase) * _scanSweepAngle * 0.5f;
        return Quaternion.Euler(0f, yaw, 0f) * baseDir;
    }

    // ── Validate / setup ──────────────────────────────────────────────────────

    private void ValidateRefs()
    {
        if (_perception   == null) Debug.LogError($"[EnemyBrain] {name}: EnemyPerception is null.");
        if (_aimComponent == null) Debug.LogError($"[EnemyBrain] {name}: EnemyAimComponent is null.");
        if (_weaponDriver == null) Debug.LogError($"[EnemyBrain] {name}: EnemyWeaponDriver is null.");
        if (_health       == null) Debug.LogError($"[EnemyBrain] {name}: EnemyHealth is null.");
        if (_weaponData   == null) Debug.LogError($"[EnemyBrain] {name}: WeaponData is null.");
        if (_gunInstance  == null) Debug.LogError($"[EnemyBrain] {name}: GunInstance is null.");
    }

    private void SetupGunAndIK()
    {
        if (_gunInstance == null) return;

        var muzzle    = FindDeep(_gunInstance.transform, _muzzleName);
        var rightGrip = FindDeep(_gunInstance.transform, _rightGripName);
        var leftGrip  = FindDeep(_gunInstance.transform, _leftGripName);

        if (muzzle    == null) Debug.LogError($"[EnemyBrain] {name}: '{_muzzleName}' not found in gun GO.");
        if (rightGrip == null) Debug.LogError($"[EnemyBrain] {name}: '{_rightGripName}' not found in gun GO.");
        if (leftGrip  == null) Debug.LogError($"[EnemyBrain] {name}: '{_leftGripName}' not found in gun GO.");

        _aimComponent?.Initialize(rightGrip, leftGrip);
        _weaponDriver?.Initialize(_weaponData, gameObject, muzzle);
    }

    // ── Outer FSM ─────────────────────────────────────────────────────────────

    private void SetState(BrainState next)
    {
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _state = next;
        _stateCoroutine = next switch
        {
            BrainState.Patrol      => StartCoroutine(PatrolRoutine()),
            BrainState.Suspicious  => StartCoroutine(SuspiciousRoutine()),
            BrainState.Investigate => StartCoroutine(InvestigateRoutine()),
            BrainState.Engage      => StartCoroutine(EngageRoutine()),
            _                      => null
        };
    }

    // ── Patrol ────────────────────────────────────────────────────────────────

    private IEnumerator PatrolRoutine()
    {
        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();
        _agent.isStopped = false;
        _agent.speed     = _defaultAgentSpeed;

        if (_patrolRoute == null || _patrolRoute.Count == 0)
        {
            yield return new WaitForSeconds(1f);
            SetState(BrainState.Patrol);
            yield break;
        }

        while (true)
        {
            var wp = _patrolRoute.GetWaypoint(_patrolIndex);
            if (wp != null) _agent.SetDestination(wp.position);

            while (!HasArrived())
            {
                if (_perception != null && _perception.CanSeeTarget)
                {
                    SetState(BrainState.Engage);
                    yield break;
                }
                yield return null;
            }

            _patrolIndex++;
            yield return new WaitForSeconds(0.3f);
        }
    }

    // ── Suspicious ────────────────────────────────────────────────────────────
    //
    // Light reaction to a faint sound (someone walking nearby): the guard halts,
    // turns toward the noise and sweeps its view for a few seconds. It does NOT
    // commit to walking over. Escalates to Engage on sight, or to Investigate if a
    // louder sound arrives (handled in OnPerceptionEvent). Otherwise resumes patrol.

    private IEnumerator SuspiciousRoutine()
    {
        Debug.Log("[Brain] Suspicious — heard something faint, scanning.");
        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();
        _agent.isStopped = true;

        if (_perception != null)
        {
            Vector3 toSound = _perception.LastKnownPosition - transform.position;
            toSound.y = 0f;
            if (toSound.sqrMagnitude > 0.001f) _scanBaseDir = toSound;
        }

        float elapsed = 0f;
        while (elapsed < _suspiciousScanTime)
        {
            if (_perception != null && _perception.CanSeeTarget)
            {
                _agent.isStopped = false;
                SetState(BrainState.Engage);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        _agent.isStopped = false;
        _agent.speed     = _defaultAgentSpeed;
        SetState(BrainState.Patrol);
    }

    // ── Investigate ───────────────────────────────────────────────────────────
    //
    // Stronger reaction (sprint footstep, gunshot): the guard turns to face the
    // sound, then moves toward it at a wary, reduced speed while sweeping its view
    // ("looking around while moving"). It never sprints blindly to the spot.

    private IEnumerator InvestigateRoutine()
    {
        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();

        // Turn to face the sound first ("turn around to check") before moving off.
        _agent.isStopped = false;
        _agent.speed     = _investigateMoveSpeed;

        Vector3 lastDest = _perception != null ? _perception.LastKnownPosition : transform.position;
        _agent.SetDestination(lastDest);

        float elapsed = 0f;
        while (elapsed < _investigateTimeout)
        {
            if (_perception != null && _perception.CanSeeTarget)
            {
                _agent.speed = _defaultAgentSpeed;
                SetState(BrainState.Engage);
                yield break;
            }

            // Pick up a refreshed sound position (another noise heard mid-investigate).
            if (_perception != null && _perception.LastKnownPosition != lastDest)
            {
                lastDest = _perception.LastKnownPosition;
                _agent.SetDestination(lastDest);
                elapsed = 0f; // fresh lead — extend the search
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        _agent.speed = _defaultAgentSpeed;
        SetState(BrainState.Patrol);
    }

    // ── Engage (outer) ────────────────────────────────────────────────────────

    private IEnumerator EngageRoutine()
    {
        Debug.Log($"[Brain] Engage — target: {_playerTransform?.name ?? "null"}");
        _losTimer            = 0f;
        _isInCover           = false;
        _shouldChangeCover   = false;
        _hitsAtCurrentCover  = 0;
        _cachedCoverPoint    = null;
        _pendingIntent       = null;
        _goToInvestigate     = false;
        _reactedThisEngage   = false;
        _agent.speed         = _defaultAgentSpeed;

        // Entry: quick nearby cover → sprint silently; otherwise fight in the open
        Vector3? entryCover = FindCover();
        if (entryCover.HasValue
            && Vector3.Distance(transform.position, entryCover.Value) <= _quickCoverDist)
        {
            _cachedCoverPoint = entryCover.Value;
            _pendingIntent    = CoverIntent.QuickCover;
        }
        // else → outer loop → BadPosition

        while (_state == BrainState.Engage)
        {
            if (_goToInvestigate)
            {
                _agent.speed = _investigateSlowSpeed;
                SetState(BrainState.Investigate);
                yield break;
            }

            if (_pendingIntent.HasValue)
            {
                CoverIntent intent = _pendingIntent.Value;
                _pendingIntent = null;
                yield return StartCoroutine(SeekCoverRoutine(intent));
            }
            else if (_isInCover)
            {
                yield return StartCoroutine(PeekRoutine());
            }
            else
            {
                yield return StartCoroutine(BadPositionRoutine());
            }

            yield return null; // safety frame to avoid infinite tight loop
        }
    }

    // ── Reaction delay ────────────────────────────────────────────────────────

    // Frame-polled so death / state change / LOS loss can interrupt cleanly.
    // Sets _reactedThisEngage = true when done; subsequent phases skip the wait.
    private IEnumerator ReactionDelayIfNeeded()
    {
        if (_reactedThisEngage) yield break;

        float elapsed = 0f;
        while (elapsed < _reactionDelay)
        {
            // Abort early if Engage exits for any reason, or if we're taking hits and need to move now
            if (_state != BrainState.Engage || _goToInvestigate || _pendingIntent.HasValue || _shouldChangeCover)
                yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }
        _reactedThisEngage = true;
    }

    // ── BadPosition ───────────────────────────────────────────────────────────

    private IEnumerator BadPositionRoutine()
    {
        Debug.Log("[Brain] Phase: BadPosition — shooting in open, backing away");
        _engagePhase     = EngagePhase.BadPosition;
        _agent.isStopped = false;
        _agent.speed     = _backStepSpeed;

        _aimComponent?.SetEngaged(true);
        if (_playerTransform != null) _weaponDriver?.SetAimTarget(_playerTransform);
        yield return null; // one frame for aim pivot to orient

        // First discovery: guard raises gun but waits before firing
        yield return StartCoroutine(ReactionDelayIfNeeded());
        if (_state != BrainState.Engage || _goToInvestigate || _pendingIntent.HasValue) yield break;

        float backTimer        = 0f;
        float losDebounceTimer = 0f;
        int   shotsInBurst     = 0;

        while (true)
        {
            // LOS debounce — brief occlusions don't immediately trigger a cover sprint
            if (_perception != null && !_perception.CanSeeTarget)
            {
                losDebounceTimer += Time.deltaTime;
                if (losDebounceTimer >= _losDebounce)
                {
                    Vector3? cover = FindCover();
                    if (cover.HasValue)
                    {
                        _cachedCoverPoint = cover.Value;
                        _pendingIntent    = CoverIntent.LostLOS;
                    }
                    else
                    {
                        _goToInvestigate = true;
                    }
                    break;
                }
            }
            else
            {
                losDebounceTimer = 0f;
            }

            // Reload
            if (_weaponDriver != null && _weaponDriver.NeedsReload)
            {
                _pendingIntent = CoverIntent.Reload;
                break;
            }

            // Too many hits at this spot — relocate
            if (_shouldChangeCover)
            {
                _shouldChangeCover = false;
                Vector3? cover = FindCoverForRelocation();
                if (cover.HasValue)
                {
                    _cachedCoverPoint = cover.Value;
                    _pendingIntent    = CoverIntent.ForcedRelocation;
                    break;
                }
                // No alternative cover — keep fighting in place
            }

            // Periodic backward NavMesh step
            backTimer += Time.deltaTime;
            if (backTimer >= _backStepInterval && _playerTransform != null)
            {
                backTimer = 0f;
                Vector3 awayDir = (transform.position - _playerTransform.position);
                awayDir.y = 0f;
                if (awayDir.sqrMagnitude > 0.001f)
                {
                    awayDir = awayDir.normalized;
                    Vector3 candidate = transform.position + awayDir * _backStepDist;
                    if (NavMesh.SamplePosition(candidate, out NavMeshHit nmHit, _backStepDist, NavMesh.AllAreas))
                        _agent.SetDestination(nmHit.position);
                }
            }

            // Burst fire
            if (_perception != null && _perception.CanSeeTarget && _weaponDriver != null)
            {
                bool fired = _weaponDriver.FireAt(
                    _playerTransform.position + Vector3.up * 0.8f, _fireAccuracy);

                if (fired)
                {
                    shotsInBurst++;
                    if (shotsInBurst >= _burstCount)
                    {
                        _aimComponent?.SetEngaged(false);
                        float cd = 0f;
                        while (cd < _burstCooldown)
                        {
                            // Keep accumulating LOS debounce during burst cooldown
                            if (_perception != null && !_perception.CanSeeTarget)
                            {
                                losDebounceTimer += Time.deltaTime;
                                if (losDebounceTimer >= _losDebounce)
                                    { cd = _burstCooldown; break; } // exit cooldown early
                            }
                            else
                            {
                                losDebounceTimer = 0f;
                            }

                            if (_weaponDriver != null && _weaponDriver.NeedsReload)
                                { cd = _burstCooldown; break; }
                            if (_shouldChangeCover)
                                { cd = _burstCooldown; break; }

                            cd += Time.deltaTime;
                            yield return null;
                        }
                        _aimComponent?.SetEngaged(true);
                        shotsInBurst = 0;
                    }
                }
            }

            yield return null;
        }

        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();
        _agent.speed = _defaultAgentSpeed;
    }

    // ── SeekCover ─────────────────────────────────────────────────────────────

    private IEnumerator SeekCoverRoutine(CoverIntent intent)
    {
        Debug.Log($"[Brain] Phase: SeekCover ({intent})");
        _engagePhase = EngagePhase.SeekCover;
        _isInCover   = false;

        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();

        Vector3? cover = _cachedCoverPoint
            ?? (intent == CoverIntent.ForcedRelocation ? FindCoverForRelocation() : FindCover());
        _cachedCoverPoint = null;

        if (cover.HasValue)
        {
            _agent.isStopped = false;
            _agent.speed     = _coverSprintSpeed;
            _agent.SetDestination(cover.Value);

            float timeout = _coverSeekTimeout * 1.5f;
            while (!HasArrived() && timeout > 0f)
            {
                if (_losTimer >= _holdGroundTimeout) { _goToInvestigate = true; yield break; }
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (HasArrived())
            {
                _isInCover          = true;
                _hitsAtCurrentCover = 0;
                _shouldChangeCover  = false;
            }
        }

        _agent.isStopped = true;
        _agent.speed     = _defaultAgentSpeed;

        if (_isInCover)
        {
            if (intent == CoverIntent.Reload)
            {
                _weaponDriver?.Reload();
                float reloadWait = _weaponData != null ? _weaponData.reloadTime + 0.1f : 2.1f;
                yield return new WaitForSeconds(reloadWait);
            }
            // All intents: outer loop → Peek (which shoots immediately if CanSeeTarget)
        }
        else
        {
            // Failed to reach cover — per-intent fallback
            switch (intent)
            {
                case CoverIntent.Reload:
                    // Emergency exposed reload then back to fighting in open
                    yield return new WaitForSeconds(_emergencyReloadDelay);
                    _weaponDriver?.Reload();
                    float rw = _weaponData != null ? _weaponData.reloadTime + 0.1f : 2.1f;
                    yield return new WaitForSeconds(rw);
                    // _isInCover stays false → outer loop → BadPosition
                    break;

                case CoverIntent.QuickCover:
                    // Nav path failed for nearby cover → fight in open
                    // _isInCover false → outer loop → BadPosition
                    break;

                case CoverIntent.LostLOS:
                    _goToInvestigate = true;
                    break;

                case CoverIntent.ForcedRelocation:
                    // Player may still be visible — don't investigate, keep fighting in open
                    // _isInCover stays false → outer loop → BadPosition
                    break;
            }
        }

        _agent.isStopped = false;
    }

    // ── Peek ──────────────────────────────────────────────────────────────────

    private IEnumerator PeekRoutine()
    {
        Debug.Log("[Brain] Phase: Peek — in cover, shooting on sight");
        _engagePhase     = EngagePhase.Peek;
        _agent.isStopped = true;

        _aimComponent?.SetEngaged(true);
        if (_playerTransform != null) _weaponDriver?.SetAimTarget(_playerTransform);
        yield return null; // one frame for aim pivot

        // First discovery (QuickCover path): guard is in cover but hasn't fired yet
        yield return StartCoroutine(ReactionDelayIfNeeded());
        if (_state != BrainState.Engage || _goToInvestigate || _pendingIntent.HasValue) yield break;

        int shotsInBurst = 0;

        while (true)
        {
            if (_losTimer >= _holdGroundTimeout) { _goToInvestigate = true; break; }
            if (_shouldChangeCover)              { _pendingIntent = CoverIntent.ForcedRelocation; break; }
            if (_weaponDriver != null && _weaponDriver.NeedsReload) { _pendingIntent = CoverIntent.Reload; break; }

            if (_perception != null && _perception.CanSeeTarget && _weaponDriver != null)
            {
                bool fired = _weaponDriver.FireAt(
                    _playerTransform.position + Vector3.up * 0.8f, _fireAccuracy);

                if (fired)
                {
                    shotsInBurst++;
                    if (shotsInBurst >= _burstCount)
                    {
                        _aimComponent?.SetEngaged(false);
                        float cd = 0f;
                        while (cd < _burstCooldown)
                        {
                            if (_losTimer >= _holdGroundTimeout) { cd = _burstCooldown; break; }
                            if (_shouldChangeCover)              { cd = _burstCooldown; break; }
                            if (_weaponDriver != null && _weaponDriver.NeedsReload) { cd = _burstCooldown; break; }
                            cd += Time.deltaTime;
                            yield return null;
                        }
                        _aimComponent?.SetEngaged(true);
                        shotsInBurst = 0;
                    }
                }
            }

            yield return null;
        }

        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();
        // _isInCover stays true — guard is physically still at the cover point
    }

    // ── Damage events ─────────────────────────────────────────────────────────

    private void OnHealthDamaged(DamageContext ctx)
    {
        _lastHitNormal = ctx.HitNormal;

        if (_state != BrainState.Engage) return;

        bool blindsided = _perception != null && !_perception.CanSeeTarget;
        if (blindsided)
        {
            _shouldChangeCover = true;
            return;
        }

        _hitsAtCurrentCover++;
        if (_hitsAtCurrentCover >= _hitsToChangeCover)
            _shouldChangeCover = true;
    }

    // ── Perception events ─────────────────────────────────────────────────────

    private void OnPerceptionEvent(EnemyPerception.PerceptionEvent evt, Vector3 position)
    {
        switch (evt)
        {
            case EnemyPerception.PerceptionEvent.TargetSpotted:
                if (_state != BrainState.Engage)
                    SetState(BrainState.Engage);
                break;

            case EnemyPerception.PerceptionEvent.SoundHeard:
                ReactToSound(_perception != null ? _perception.LastHeardIntensity : 1f);
                break;

            case EnemyPerception.PerceptionEvent.TargetLost:
                break;
        }
    }

    /// <summary>
    /// Grades the guard's response to a heard sound by its normalised loudness:
    ///   loud  (sprint footstep / gunshot) → Investigate: turn and warily approach, scanning.
    ///   faint (someone walking nearby)    → Suspicious: halt and look around in place.
    /// Already-engaged guards ignore noise (they're fighting). A faint cue never downgrades
    /// an in-progress Investigate, and a loud cue can escalate Suspicious → Investigate.
    /// </summary>
    private void ReactToSound(float loudness)
    {
        if (_state == BrainState.Engage) return;

        if (loudness >= _investigateLoudness)
        {
            // Restart Investigate even if already investigating, so the guard re-orients
            // to the newest, louder cue.
            SetState(BrainState.Investigate);
        }
        else if (loudness >= _suspiciousLoudness)
        {
            // Don't pull an actively-investigating guard back to a weaker reaction.
            if (_state == BrainState.Patrol || _state == BrainState.Suspicious)
                SetState(BrainState.Suspicious);
        }
        // Below the suspicious floor (e.g. crouch) → ignored.
    }

    // ── Death ─────────────────────────────────────────────────────────────────

    private void Die()
    {
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);

        // 1. Kill AR writes first — destroys this RigBuilder's PlayableGraph so
        //    LateUpdate can no longer write to the arm bones.
        _aimComponent?.SetEngaged(false);
        _aimComponent?.Disarm();

        // 2. Drop loot (spawns a new world item, doesn't touch the visual gun GO yet).
        Vector3 throwDir = -_lastHitNormal;
        _weaponDriver?.DetachAndDrop(throwDir);

        // 3. Destroy the visual gun one frame later — if we destroy it this frame,
        //    AR's LateUpdate for this frame fires AFTER Die() with null targets and
        //    writes bind-pose (T-pose) to the arm bones before the graph is gone.
        if (_gunInstance != null) StartCoroutine(DestroyGunNextFrame(_gunInstance));
        _gunInstance = null;

        _agent.isStopped = true;

        if (_perception   != null) _perception.enabled   = false;
        if (_aimComponent != null) _aimComponent.enabled = false;
        enabled = false;

        if (WorldStateManager.Instance != null)
            WorldStateManager.Instance.SetBool($"npc.{gameObject.name}.dead", true);

        Debug.Log($"[Brain] {name} died — weapon dropped.");
    }

    private IEnumerator DestroyGunNextFrame(GameObject gun)
    {
        yield return null;
        if (gun != null) Destroy(gun);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Vector3? FindCover() =>
        EnemyCoverUtility.FindCoverPoint(
            transform.position,
            _playerTransform?.position ?? transform.position,
            _coverSearchRadius,
            _coverSampleCount,
            _eyeHeight,
            _coverMask,
            _agent);

    private Vector3? FindCoverForRelocation() =>
        EnemyCoverUtility.FindCoverPoint(
            transform.position,
            _playerTransform?.position ?? transform.position,
            _coverSearchRadius * 1.5f,
            _coverSampleCount,
            _eyeHeight,
            _coverMask,
            _agent,
            excludeCenter: transform.position,
            excludeRadius: _forceRelocateMinDist);

    private bool HasArrived() =>
        !_agent.pathPending
        && _agent.remainingDistance <= _agent.stoppingDistance + 0.1f
        && (!_agent.hasPath || _agent.velocity.sqrMagnitude < 0.01f);

    private static Transform FindDeep(Transform root, string targetName)
    {
        if (root.name == targetName) return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, targetName);
            if (found != null) return found;
        }
        return null;
    }
}
