using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Two-level FSM for a guard NPC.
///
/// Outer states: Patrol → Investigate → Engage.
/// Inner EngagePhase (active only during Engage):
///   SuppressAndRetreat — spotted in the open: shoot immediately while backing to cover.
///   HoldGround         — no cover available, or holding position after player retreats.
///   SeekCoverToReload  — magazine empty: move to cover silently, reload, then Peek.
///   Peek               — in cover: burst fire when player is visible, duck back.
///
/// Body always faces player during Engage (NavMeshAgent.updateRotation = false).
/// Aim pivot only activates during phases that shoot (SuppressAndRetreat, HoldGround, Peek).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyBrain : MonoBehaviour
{
    private enum BrainState   { Patrol, Investigate, Engage }
    private enum EngagePhase  { SuppressAndRetreat, HoldGround, SeekCoverToReload, Peek }

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

    [Header("Engage Timers")]
    [SerializeField] private float _holdGroundTimeout   = 30f; // LOS lost → InvestigateSlow
    [SerializeField] private float _coverSeekTimeout    = 4f;  // max time to reach cover
    [SerializeField] private float _emergencyReloadDelay = 2f; // pause before in-place reload
    [SerializeField] private float _investigateSlowSpeed = 1.5f;

    [Header("Tuning")]
    [SerializeField] private float _investigateTimeout = 8f;

    [Header("Movement")]
    [SerializeField] private float _rotationSpeed = 360f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private float        _defaultAgentSpeed;
    private BrainState   _state = BrainState.Patrol;
    private EngagePhase  _engagePhase;
    private Coroutine    _stateCoroutine;
    private Transform    _playerTransform;
    private int          _patrolIndex;

    // Engage shared state
    private bool  _isInCover;
    private float _losTimer;          // seconds since player was last seen (in Engage)

    // ── Init ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = false;
        _defaultAgentSpeed    = _agent.speed;

        ValidateRefs();

        var ph = Object.FindObjectOfType<PlayerHealth>();
        _playerTransform = ph != null ? ph.transform : null;
        if (_playerTransform == null)
            Debug.LogError($"[EnemyBrain] {name}: PlayerHealth not found — AI will not function.");

        _perception?.Initialize(_playerTransform);
        SetupGunAndIK();

        if (_health     != null) _health.OnDeath               += Die;
        if (_perception != null) _perception.OnPerceptionEvent += OnPerceptionEvent;
    }

    private void Start() => SetState(BrainState.Patrol);

    private void Update()
    {
        RotateBody();

        // Tick LOS-lost timer during Engage
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
            targetDir = _playerTransform.position - transform.position;
            targetDir.y = 0f;
        }
        else
        {
            if (_agent.velocity.sqrMagnitude > 0.04f)
                targetDir = _agent.velocity;
            else
                return;
            targetDir.y = 0f;
        }

        if (targetDir.sqrMagnitude < 0.001f) return;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(targetDir),
            _rotationSpeed * Time.deltaTime);
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

    // ── Investigate ───────────────────────────────────────────────────────────

    private IEnumerator InvestigateRoutine()
    {
        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();
        _agent.isStopped = false;

        if (_perception != null)
            _agent.SetDestination(_perception.LastKnownPosition);

        float elapsed = 0f;
        while (elapsed < _investigateTimeout)
        {
            if (_perception != null && _perception.CanSeeTarget)
            {
                _agent.speed = _defaultAgentSpeed;
                SetState(BrainState.Engage);
                yield break;
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
        _losTimer         = 0f;
        _isInCover        = false;
        _cachedCoverPoint = null; // clear stale point from prior Engage session
        _agent.speed      = _defaultAgentSpeed;

        // Entry: pick opening phase based on whether cover is reachable
        Vector3? entryCover = FindCover();
        if (entryCover.HasValue)
        {
            _cachedCoverPoint = entryCover.Value;
            yield return StartCoroutine(SuppressAndRetreatRoutine());
        }
        else
        {
            yield return StartCoroutine(HoldGroundRoutine());
        }

        // After each phase the coroutine returns here — drive the next phase
        while (_state == BrainState.Engage)
        {
            // Global exit: LOS gone for too long
            if (_losTimer >= _holdGroundTimeout)
            {
                Debug.Log("[Brain] LOS timeout → InvestigateSlow");
                _agent.speed = _investigateSlowSpeed;
                SetState(BrainState.Investigate);
                yield break;
            }

            // Choose next phase by current tactical situation
            if (_weaponDriver != null && _weaponDriver.NeedsReload)
            {
                yield return StartCoroutine(SeekCoverToReloadRoutine());
            }
            else if (_isInCover)
            {
                yield return StartCoroutine(PeekRoutine());
            }
            else
            {
                // Not in cover — re-evaluate
                Vector3? cover = FindCover();
                if (cover.HasValue)
                {
                    _cachedCoverPoint = cover.Value;
                    yield return StartCoroutine(SuppressAndRetreatRoutine());
                }
                else
                {
                    yield return StartCoroutine(HoldGroundRoutine());
                }
            }

            yield return null; // safety yield to avoid frame hang if phases end instantly
        }
    }

    // Cached cover destination shared across phases within one Engage session (null = none found yet)
    private Vector3? _cachedCoverPoint;

    // ── SuppressAndRetreat ────────────────────────────────────────────────────

    private IEnumerator SuppressAndRetreatRoutine()
    {
        Debug.Log("[Brain] Phase: SuppressAndRetreat");
        _engagePhase  = EngagePhase.SuppressAndRetreat;
        _isInCover    = false;
        _agent.isStopped = false;
        if (_cachedCoverPoint.HasValue) _agent.SetDestination(_cachedCoverPoint.Value);

        // Aim active immediately — shoot while backing up
        _aimComponent?.SetEngaged(true);
        if (_playerTransform != null) _weaponDriver?.SetAimTarget(_playerTransform);
        yield return null; // one frame for aim pivot to orient

        float   timeout      = _coverSeekTimeout;
        int     shotsInBurst = 0;
        bool    exitEarly    = false;

        while (!HasArrived() && timeout > 0f)
        {
            if (_losTimer >= _holdGroundTimeout) { exitEarly = true; break; }

            if (_weaponDriver != null && _weaponDriver.NeedsReload)
                break; // go reload — let outer loop handle it

            // Burst fire while moving
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
                            if (_losTimer >= _holdGroundTimeout || (_weaponDriver != null && _weaponDriver.NeedsReload))
                            { exitEarly = true; break; }
                            cd += Time.deltaTime;
                            yield return null;
                        }
                        if (exitEarly) break;
                        _aimComponent?.SetEngaged(true);
                        shotsInBurst = 0;
                    }
                }
            }

            timeout -= Time.deltaTime;
            yield return null;
        }

        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();

        if (!exitEarly && HasArrived())
            _isInCover = true;
    }

    // ── HoldGround ────────────────────────────────────────────────────────────

    private IEnumerator HoldGroundRoutine()
    {
        Debug.Log("[Brain] Phase: HoldGround");
        _engagePhase     = EngagePhase.HoldGround;
        _agent.isStopped = true;

        _aimComponent?.SetEngaged(true);
        if (_playerTransform != null) _weaponDriver?.SetAimTarget(_playerTransform);
        yield return null; // one frame for aim pivot

        int  shotsInBurst     = 0;
        bool usedEmergencyRld = false;

        while (true)
        {
            // Global LOS timeout
            if (_losTimer >= _holdGroundTimeout) yield break;

            // Reload needed
            if (_weaponDriver != null && _weaponDriver.NeedsReload)
            {
                // Try normal radius, then widened radius before giving up on cover
                Vector3? cover = FindCover();
                if (!cover.HasValue)
                    cover = EnemyCoverUtility.FindCoverPoint(
                        transform.position,
                        _playerTransform?.position ?? transform.position,
                        _coverSearchRadius * 1.5f, _coverSampleCount, _eyeHeight, _coverMask, _agent);

                if (cover.HasValue)
                {
                    _cachedCoverPoint = cover.Value;
                    _aimComponent?.SetEngaged(false);
                    _weaponDriver?.ClearAim();
                    yield break; // outer loop → SeekCoverToReload
                }

                // No cover anywhere — one emergency in-place reload, then keep fighting
                if (!usedEmergencyRld)
                {
                    usedEmergencyRld = true; // never reset — only one exposed reload per HoldGround
                    _aimComponent?.SetEngaged(false);
                    _weaponDriver?.ClearAim();
                    yield return new WaitForSeconds(_emergencyReloadDelay);
                    _weaponDriver?.Reload();
                    float reloadWait = _weaponData != null ? _weaponData.reloadTime + 0.1f : 2.1f;
                    yield return new WaitForSeconds(reloadWait);
                    _aimComponent?.SetEngaged(true);
                    if (_playerTransform != null) _weaponDriver?.SetAimTarget(_playerTransform);
                    shotsInBurst = 0;
                }
                else
                {
                    // No cover, emergency reload already spent, ammo gone again — guard retreats
                    // rather than looping with no shots fired (losTimer would never tick at 30s)
                    Debug.Log("[Brain] HoldGround: no cover, no ammo, emergency used — retreating");
                    _aimComponent?.SetEngaged(false);
                    _weaponDriver?.ClearAim();
                    _agent.speed = _investigateSlowSpeed;
                    SetState(BrainState.Investigate);
                    yield break;
                }
            }

            // Fire while visible
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
    }

    // ── SeekCoverToReload ─────────────────────────────────────────────────────

    private IEnumerator SeekCoverToReloadRoutine()
    {
        Debug.Log("[Brain] Phase: SeekCoverToReload");
        _engagePhase = EngagePhase.SeekCoverToReload;

        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();

        // Capture whether we're already at cover BEFORE touching _isInCover
        bool startedInCover = _isInCover;
        _isInCover = false; // will be re-confirmed below

        if (startedInCover)
        {
            // Already at cover (arriving from Peek) — no movement needed
            _isInCover = true;
        }
        else
        {
            // Prefer the cover point already found by a prior phase (e.g. HoldGround found it
            // one frame earlier at the same radius — re-searching may miss it randomly).
            // Only run a fresh search if no cached point exists.
            Vector3? cover = _cachedCoverPoint ?? FindCover();

            if (!cover.HasValue)
                cover = EnemyCoverUtility.FindCoverPoint(
                    transform.position, _playerTransform?.position ?? transform.position,
                    _coverSearchRadius * 1.5f, _coverSampleCount, _eyeHeight, _coverMask, _agent);

            if (cover.HasValue)
            {
                _cachedCoverPoint = cover.Value;
                _agent.isStopped  = false;
                _agent.SetDestination(cover.Value);

                float timeout = _coverSeekTimeout * 1.5f;
                while (!HasArrived() && timeout > 0f)
                {
                    if (_losTimer >= _holdGroundTimeout) yield break;
                    timeout -= Time.deltaTime;
                    yield return null;
                }

                if (HasArrived()) _isInCover = true;
            }
        }

        _agent.isStopped = true;

        if (_isInCover)
        {
            // In cover — reload immediately
            if (_weaponDriver != null) _weaponDriver.Reload();
            float reloadWait = _weaponData != null ? _weaponData.reloadTime + 0.1f : 2.1f;
            yield return new WaitForSeconds(reloadWait);
        }
        else
        {
            // Timed out without reaching cover — exposed emergency reload with delay
            yield return new WaitForSeconds(_emergencyReloadDelay);
            if (_weaponDriver != null) _weaponDriver.Reload();
            float reloadWait = _weaponData != null ? _weaponData.reloadTime + 0.1f : 2.1f;
            yield return new WaitForSeconds(reloadWait);
        }

        _agent.isStopped = false;
        // Outer loop decides next phase based on _isInCover and _losTimer
    }

    // ── Peek ─────────────────────────────────────────────────────────────────

    private IEnumerator PeekRoutine()
    {
        Debug.Log("[Brain] Phase: Peek");
        _engagePhase     = EngagePhase.Peek;
        _agent.isStopped = true;

        _aimComponent?.SetEngaged(true);
        if (_playerTransform != null) _weaponDriver?.SetAimTarget(_playerTransform);
        yield return null; // one frame for aim pivot

        int  shotsInBurst = 0;
        bool exitEarly    = false;

        while (!exitEarly)
        {
            // LOS lost for too long — outer loop will push to InvestigateSlow
            if (_losTimer >= _holdGroundTimeout) break;

            // LOS lost briefly — stand ground (stay in Peek, just don't fire)
            // This loop continues — guard waits in cover until timer hits 30s or player reappears.

            if (_weaponDriver != null && _weaponDriver.NeedsReload)
                break; // outer loop → SeekCoverToReload

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
                            if (_losTimer >= _holdGroundTimeout || (_weaponDriver != null && _weaponDriver.NeedsReload))
                            { exitEarly = true; break; }
                            cd += Time.deltaTime;
                            yield return null;
                        }
                        if (!exitEarly) _aimComponent?.SetEngaged(true);
                        shotsInBurst = 0;
                    }
                }
            }

            yield return null;
        }

        _aimComponent?.SetEngaged(false);
        _weaponDriver?.ClearAim();
        // _isInCover stays true — guard is still physically at the cover point
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
                if (_state == BrainState.Patrol)
                    SetState(BrainState.Investigate);
                break;

            case EnemyPerception.PerceptionEvent.TargetLost:
                break;
        }
    }

    // ── Death ─────────────────────────────────────────────────────────────────

    private void Die()
    {
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);

        _weaponDriver?.DetachAndDrop();
        _agent.isStopped = true;
        _aimComponent?.SetEngaged(false);

        if (_perception   != null) _perception.enabled   = false;
        if (_aimComponent != null) _aimComponent.enabled = false;
        enabled = false;

        if (WorldStateManager.Instance != null)
            WorldStateManager.Instance.SetBool($"npc.{gameObject.name}.dead", true);

        Debug.Log($"[Brain] {name} died — weapon dropped.");
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
