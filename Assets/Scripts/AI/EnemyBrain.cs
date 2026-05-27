using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Central FSM for a guard NPC. States: Patrol → Investigate → Engage.
/// Each state owns a coroutine; SetState() stops the old and starts the new atomically.
///
/// Engage sub-phases: SeekCover → InCover → Peek (burst fire) → optionally Reload → repeat.
/// Body always faces player during Engage via manual rotation (updateRotation = false).
/// Aim pivot only tracks player during Peek; resets to forward during movement.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyBrain : MonoBehaviour
{
    private enum BrainState { Patrol, Investigate, Engage }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Required Components")]
    [SerializeField] private EnemyPerception   _perception;
    [SerializeField] private EnemyAimComponent _aimComponent;
    [SerializeField] private EnemyWeaponDriver _weaponDriver;
    [SerializeField] private EnemyHealth       _health;
    [SerializeField] private PatrolRoute       _patrolRoute;

    [Header("Weapon Setup")]
    [SerializeField] private WeaponSO   _weaponData;
    [SerializeField] private GameObject _gunInstance;  // drag the gun already placed in the prefab here

    [Header("Gun Transform Names (must match child names in gun GO)")]
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
    [SerializeField] private int   _burstCount   = 3;    // shots per burst
    [SerializeField] private float _burstCooldown = 1.5f; // seconds between bursts

    [Header("Movement")]
    [SerializeField] private float _rotationSpeed = 360f; // deg/s body rotation toward threat

    [Header("Tuning")]
    [SerializeField] private float _investigateTimeout = 8f;
    [SerializeField] private float _coverSeekTimeout   = 4f;
    [SerializeField] private float _inCoverWaitMin     = 0.5f;
    [SerializeField] private float _inCoverWaitMax     = 1.5f;
    [SerializeField] private float _peekDurationMin    = 1.5f;
    [SerializeField] private float _peekDurationMax    = 3f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private BrainState   _state = BrainState.Patrol;
    private Coroutine    _stateCoroutine;
    private Transform    _playerTransform;
    private int          _patrolIndex;

    // ── Init ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = false; // we drive rotation manually so guard never turns back to player

        ValidateRefs();

        var ph = Object.FindObjectOfType<PlayerHealth>();
        _playerTransform = ph != null ? ph.transform : null;
        if (_playerTransform == null)
            Debug.LogError($"[EnemyBrain] {name}: PlayerHealth not found in scene — AI will not function.");

        _perception?.Initialize(_playerTransform);

        SetupGunAndIK();

        if (_health     != null) _health.OnDeath                += Die;
        if (_perception != null) _perception.OnPerceptionEvent  += OnPerceptionEvent;
    }

    private void Start()
    {
        SetState(BrainState.Patrol);
    }

    private void Update()
    {
        RotateBody();
    }

    // ── Body rotation ─────────────────────────────────────────────────────────

    /// <summary>
    /// During Engage: always face the player (guard strafes sideways to cover).
    /// During Patrol/Investigate: face movement direction when moving.
    /// </summary>
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
            // Face velocity direction when moving; otherwise hold current rotation
            if (_agent.velocity.sqrMagnitude > 0.04f)
                targetDir = _agent.velocity;
            else
                return;
            targetDir.y = 0f;
        }

        if (targetDir.sqrMagnitude < 0.001f) return;

        Quaternion desired = Quaternion.LookRotation(targetDir);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, desired, _rotationSpeed * Time.deltaTime);
    }

    private void ValidateRefs()
    {
        if (_perception   == null) Debug.LogError($"[EnemyBrain] {name}: EnemyPerception is null.");
        if (_aimComponent == null) Debug.LogError($"[EnemyBrain] {name}: EnemyAimComponent is null.");
        if (_weaponDriver == null) Debug.LogError($"[EnemyBrain] {name}: EnemyWeaponDriver is null.");
        if (_health       == null) Debug.LogError($"[EnemyBrain] {name}: EnemyHealth is null.");
        if (_weaponData   == null) Debug.LogError($"[EnemyBrain] {name}: WeaponData is null.");
        if (_gunInstance  == null) Debug.LogError($"[EnemyBrain] {name}: GunInstance is null — drag the gun GO from the prefab into this slot.");
        if (_aimComponent != null && _aimComponent.AimPivot == null)
            Debug.LogError($"[EnemyBrain] {name}: EnemyAimComponent.AimPivot is null.");
    }

    private void SetupGunAndIK()
    {
        if (_gunInstance == null) return;

        var muzzle    = FindDeep(_gunInstance.transform, _muzzleName);
        var rightGrip = FindDeep(_gunInstance.transform, _rightGripName);
        var leftGrip  = FindDeep(_gunInstance.transform, _leftGripName);

        if (muzzle    == null) Debug.LogError($"[EnemyBrain] {name}: Transform '{_muzzleName}' not found in gun GO.");
        if (rightGrip == null) Debug.LogError($"[EnemyBrain] {name}: Transform '{_rightGripName}' not found in gun GO.");
        if (leftGrip  == null) Debug.LogError($"[EnemyBrain] {name}: Transform '{_leftGripName}' not found in gun GO.");

        _aimComponent.Initialize(rightGrip, leftGrip);
        _weaponDriver.Initialize(_weaponData, gameObject, muzzle);
    }

    // ── FSM ──────────────────────────────────────────────────────────────────

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
        _agent.isStopped = false;

        if (_perception != null)
            _agent.SetDestination(_perception.LastKnownPosition);

        float elapsed = 0f;
        while (elapsed < _investigateTimeout)
        {
            if (_perception != null && _perception.CanSeeTarget)
            {
                SetState(BrainState.Engage);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetState(BrainState.Patrol);
    }

    // ── Engage ────────────────────────────────────────────────────────────────

    private IEnumerator EngageRoutine()
    {
        Debug.Log($"[Brain] Entered Engage state — target: {_playerTransform?.name ?? "null"}");
        // NOTE: SetEngaged is NOT called here — aim pivot only activates during Peek
        _agent.isStopped = false;

        while (true)
        {
            if (ShouldExitEngage())
            {
                Debug.Log("[Brain] LOS timeout — Engage → Investigate");
                SetState(BrainState.Investigate);
                yield break;
            }

            yield return StartCoroutine(SeekCoverRoutine());
            if (ShouldExitEngage()) { SetState(BrainState.Investigate); yield break; }

            yield return StartCoroutine(InCoverRoutine());
            if (ShouldExitEngage()) { SetState(BrainState.Investigate); yield break; }

            yield return StartCoroutine(PeekRoutine());
            if (ShouldExitEngage()) { SetState(BrainState.Investigate); yield break; }

            if (_weaponDriver != null && _weaponDriver.NeedsReload)
                yield return StartCoroutine(ReloadRoutine());
        }
    }

    private IEnumerator SeekCoverRoutine()
    {
        // Gun faces forward while repositioning
        _aimComponent?.SetEngaged(false);

        if (_playerTransform == null) yield break;

        Vector3? cover = EnemyCoverUtility.FindCoverPoint(
            transform.position,
            _playerTransform.position,
            _coverSearchRadius,
            _coverSampleCount,
            _eyeHeight,
            _coverMask,
            _agent);

        if (cover.HasValue)
        {
            _agent.isStopped = false;
            _agent.SetDestination(cover.Value);
            float timeout = _coverSeekTimeout;
            while (!HasArrived() && timeout > 0f)
            {
                if (ShouldExitEngage()) yield break;
                timeout -= Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            if (_perception != null)
                _agent.SetDestination(_perception.LastKnownPosition);
            yield return new WaitForSeconds(1.5f);
        }
    }

    private IEnumerator InCoverRoutine()
    {
        _agent.isStopped = true;

        // Ambush response: if player is already visible, skip the wait entirely
        if (_perception != null && _perception.CanSeeTarget)
        {
            _agent.isStopped = false;
            yield break;
        }

        float elapsed = 0f;
        float wait    = Random.Range(_inCoverWaitMin, _inCoverWaitMax);

        while (elapsed < wait)
        {
            // Break out early the moment we spot the player (ambush)
            if (_perception != null && _perception.CanSeeTarget)
            {
                _agent.isStopped = false;
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        _agent.isStopped = false;
    }

    private IEnumerator PeekRoutine()
    {
        Debug.Log($"[Brain] Has target: {_playerTransform != null}");
        if (_playerTransform == null || _weaponDriver == null) yield break;

        _aimComponent?.SetEngaged(true);
        _weaponDriver.SetAimTarget(_playerTransform);
        // Yield one frame so EnemyAimComponent.Update processes the aim target before first shot
        yield return null;
        Debug.Log($"[Brain] Peek — aim engaged, CanSeeTarget: {(_perception != null ? _perception.CanSeeTarget.ToString() : "perception null")}");

        float elapsed      = 0f;
        float duration     = Random.Range(_peekDurationMin, _peekDurationMax);
        int   shotsInBurst = 0;
        bool  exitEarly    = false;

        while (elapsed < duration && !exitEarly)
        {
            if (_weaponDriver.NeedsReload || ShouldExitEngage()) break;

            if (_perception != null && _perception.CanSeeTarget)
            {
                bool fired = _weaponDriver.FireAt(
                    _playerTransform.position + Vector3.up * 0.8f, _fireAccuracy);

                if (fired)
                {
                    shotsInBurst++;
                    if (shotsInBurst >= _burstCount)
                    {
                        // Burst complete — lower gun, wait, checking for exit conditions each frame
                        _aimComponent?.SetEngaged(false);
                        float cd = 0f;
                        while (cd < _burstCooldown)
                        {
                            if (_weaponDriver.NeedsReload || ShouldExitEngage()) { exitEarly = true; break; }
                            cd += Time.deltaTime;
                            yield return null;
                        }
                        if (!exitEarly) _aimComponent?.SetEngaged(true);
                        shotsInBurst = 0;
                    }
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        _aimComponent?.SetEngaged(false);
        _weaponDriver.ClearAim();
    }

    private IEnumerator ReloadRoutine()
    {
        if (_weaponDriver != null) _weaponDriver.Reload();
        float wait = _weaponData != null ? _weaponData.reloadTime + 0.1f : 2.1f;
        yield return new WaitForSeconds(wait);
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

    private bool ShouldExitEngage() =>
        _perception != null
        && !_perception.CanSeeTarget
        && _perception.LostSightTimer >= _perception.LostSightTimeout;

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
