using UnityEngine;

/// <summary>
/// Possessed corpse — kinematic root bone drags the ragdoll via CharacterJoint chain.
///
/// On Awake:
///   - Armature detached from NavMeshAgent root (world-space, pose preserved).
///   - All ragdoll bones set non-kinematic EXCEPT _rootBone (Hips), which stays kinematic.
///   - Initial world offset between _rootBone and the agent root is cached.
///
/// FixedUpdate: _rootBone tracks the NavMeshAgent via MovePosition/MoveRotation.
///   MovePosition is physics-aware — connected bones receive velocity data and physically
///   lag behind on fast turns, producing organic swing through the CharacterJoint chain.
///
/// On death: _rootBone goes non-kinematic, body falls freely with death impulse.
///           NavMeshAgent is managed by EnemyBrain.Die() — not touched here.
///
/// IMPORTANT — prefab setup: do NOT add Animator or RigBuilder to this enemy prefab.
///   The possessed corpse is physics-owned from frame 1; it has no animation clips.
///   Adding those components and assigning them here breaks both the enemy aim IK
///   and the player's arm IK via Unity's Animation Rigging scene-wide rebuild.
/// </summary>
public class EnemyRagdoll : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyHealth _health;

    [Tooltip("Root of the skeleton to detach (usually 'Armature').")]
    [SerializeField] private Transform _armatureRoot;

    [Tooltip("The root ragdoll Rigidbody (usually Hips). Stays kinematic — drags everything else.")]
    [SerializeField] private Rigidbody _rootBone;

    [Header("Root Bone Offset")]
    [Tooltip("Euler angle offset applied to the root bone on top of the agent's rotation. " +
             "Use X to tilt forward/back, Z to lean sideways.")]
    [SerializeField] private Vector3 _rootBoneAngleOffset = Vector3.zero;

    [Header("Head")]
    [Tooltip("The Head Rigidbody. A continuous torque is applied to push it backward, " +
             "preventing it from flopping forward while still allowing left/right sway.")]
    [SerializeField] private Rigidbody _headBone;
    [Tooltip("Torque magnitude pushing the head back. Raise until it no longer flops forward.")]
    [SerializeField] private float _headBackTorque = 5f;

    [Header("Leg Reactivity")]
    [Tooltip("Leg Rigidbodies to push during movement (e.g. LeftUpLeg, RightUpLeg, LeftLeg, RightLeg).")]
    [SerializeField] private Rigidbody[] _legBones;
    [Tooltip("Force multiplier applied to legs opposite to movement direction. Higher = more aggressive flailing.")]
    [SerializeField] private float _legForceMultiplier = 15f;

    [Header("Death")]
    [SerializeField] private float _deathForce = 200f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Rigidbody[]   _allBones;
    private Vector3       _rootOffset;    // world-space offset: agent root → rootBone at spawn
    private Vector3       _prevPosition;  // for velocity estimation
    private DamageContext _lastHit;
    private bool          _alive = true;

    // ── Awake ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        ValidateRefs();

        _allBones = _armatureRoot != null
            ? _armatureRoot.GetComponentsInChildren<Rigidbody>()
            : _rootBone.GetComponentsInChildren<Rigidbody>();

        _rootOffset   = _rootBone.position - transform.position;
        _prevPosition = transform.position;

        foreach (var rb in _allBones)
        {
            rb.isKinematic   = (rb == _rootBone);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        if (_armatureRoot != null)
        {
            _armatureRoot.SetParent(null, worldPositionStays: true);

            // Bone colliders are now orphaned from EnemyHealth — plant a proxy so
            // GetComponentInParent<IDamageable> on any hit bone still reaches us.
            var proxy = _armatureRoot.GetComponent<EnemyDamageProxy>()
                     ?? _armatureRoot.gameObject.AddComponent<EnemyDamageProxy>();
            proxy.Initialize(_health);
        }

        if (_health != null)
        {
            _health.OnDamaged += ctx => _lastHit = ctx;
            _health.OnDeath   += OnDeath;
        }
        else
        {
            Debug.LogError($"[EnemyRagdoll] {name}: EnemyHealth is null.");
        }
    }

    // ── FixedUpdate ───────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (!_alive) return;

        _rootBone.MovePosition(transform.position + _rootOffset);
        _rootBone.MoveRotation(transform.rotation * Quaternion.Euler(_rootBoneAngleOffset));

        if (_headBone != null)
            _headBone.AddTorque(transform.right * _headBackTorque, ForceMode.Force);

        Vector3 velocity = (transform.position - _prevPosition) / Time.fixedDeltaTime;
        _prevPosition = transform.position;

        if (velocity.sqrMagnitude > 0.001f && _legBones != null)
        {
            Vector3 legForce = -velocity * _legForceMultiplier;
            foreach (var leg in _legBones)
                if (leg != null) leg.AddForce(legForce, ForceMode.Force);
        }
    }

    // ── Death ─────────────────────────────────────────────────────────────────

    private void OnDeath()
    {
        _alive = false;

        // Re-scan: RigBuilder.Build() (called when the gun is equipped) may have set
        // constrained arm-bone Rigidbodies back to kinematic so Animation Rigging can
        // own their transforms. Force every bone non-kinematic now so the full ragdoll
        // activates, including arms and hands.
        _allBones = _armatureRoot != null
            ? _armatureRoot.GetComponentsInChildren<Rigidbody>()
            : _rootBone.GetComponentsInChildren<Rigidbody>();

        foreach (var rb in _allBones)
            rb.isKinematic = false;

        if (_lastHit.HitPoint != Vector3.zero)
        {
            Rigidbody hitBone = FindClosestBone(_lastHit.HitPoint);
            hitBone?.AddForceAtPosition(
                -_lastHit.HitNormal * _deathForce,
                _lastHit.HitPoint,
                ForceMode.Impulse);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Rigidbody FindClosestBone(Vector3 point)
    {
        Rigidbody closest = null;
        float     minDist = float.MaxValue;
        foreach (var rb in _allBones)
        {
            float d = (rb.position - point).sqrMagnitude;
            if (d < minDist) { minDist = d; closest = rb; }
        }
        return closest;
    }

    private void ValidateRefs()
    {
        if (_armatureRoot == null) Debug.LogError($"[EnemyRagdoll] {name}: ArmatureRoot is null.");
        if (_rootBone     == null) Debug.LogError($"[EnemyRagdoll] {name}: RootBone Rigidbody is null.");
        if (_health       == null) Debug.LogError($"[EnemyRagdoll] {name}: EnemyHealth is null.");
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (GetComponent<Animator>() != null || GetComponentInChildren<Animator>() != null)
            Debug.LogWarning($"[EnemyRagdoll] {name}: Animator found on this prefab — remove it. Possessed ragdoll is physics-only; an Animator here will break player arm IK via Animation Rigging.");
    }
#endif
}
