using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Possessed corpse movement via ConfigurableJoint anchor.
///
/// On Awake:
///   - Animator and RigBuilder disabled (physics owns the body).
///   - Armature detached from NavMeshAgent root (world-space, pose preserved).
///   - All ragdoll bones set non-kinematic.
///   - A kinematic anchor Rigidbody is created on a child GameObject at hip height.
///   - Hips Rigidbody connected to anchor via ConfigurableJoint spring.
///
/// FixedUpdate: anchor tracks NavMeshAgent root via MovePosition (physics-aware).
///
/// On death: joint disabled, body falls freely with death impulse.
///           NavMeshAgent is managed by EnemyBrain.Die() — not touched here.
/// </summary>
public class EnemyRagdoll : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator    _animator;
    [SerializeField] private RigBuilder  _rigBuilder;
    [SerializeField] private EnemyHealth _health;

    [Tooltip("Root of the skeleton to detach (usually 'Armature').")]
    [SerializeField] private Transform  _armatureRoot;

    [Tooltip("The Hips Rigidbody — ConfigurableJoint is added here.")]
    [SerializeField] private Rigidbody  _hips;

    [Header("Anchor")]
    [Tooltip("Height above the root where the ghost holds the body (roughly hip height).")]
    [SerializeField] private float _hipHeight = 0.9f;

    [Header("Joint Spring")]
    [SerializeField] private float _spring   = 1000f;
    [SerializeField] private float _damper   = 100f;
    [SerializeField] private float _maxForce = 10000f;

    [Header("Death")]
    [SerializeField] private float _deathForce = 200f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Rigidbody[]       _allBones;
    private Rigidbody         _anchorRb;
    private ConfigurableJoint _joint;
    private DamageContext     _lastHit;
    private bool              _alive = true;

    // ── Awake ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        ValidateRefs();

        // Disable animation systems — physics owns the body from frame 1
        if (_animator   != null) _animator.enabled  = false;
        if (_rigBuilder != null) _rigBuilder.enabled = false;

        // Collect all ragdoll Rigidbodies before detaching the armature
        _allBones = _armatureRoot != null
            ? _armatureRoot.GetComponentsInChildren<Rigidbody>()
            : _hips.GetComponentsInChildren<Rigidbody>();

        // All bones non-kinematic — full ragdoll
        foreach (var rb in _allBones)
        {
            rb.isKinematic   = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        // Detach armature from NavMeshAgent root so moving the root
        // does not teleport the physics bodies via transform hierarchy
        if (_armatureRoot != null)
            _armatureRoot.SetParent(null, worldPositionStays: true);

        // Create a dedicated child GO for the anchor — NOT on the NavMeshAgent root,
        // which would conflict with the agent's own position management
        var anchorGO = new GameObject("_RagdollAnchor");
        anchorGO.transform.SetParent(transform, false);
        anchorGO.transform.localPosition = Vector3.up * _hipHeight;

        _anchorRb             = anchorGO.AddComponent<Rigidbody>();
        _anchorRb.isKinematic = true;
        _anchorRb.useGravity  = false;

        // Build the ConfigurableJoint on Hips, connected to the anchor
        if (_hips != null)
            BuildJoint();

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

        // Move the anchor to follow the NavMeshAgent root each physics step.
        // MovePosition is physics-aware — it gives the joint velocity information,
        // creating cloth-like lag when the root accelerates or turns.
        _anchorRb.MovePosition(transform.position + Vector3.up * _hipHeight);
        _anchorRb.MoveRotation(transform.rotation);
    }

    // ── Death ─────────────────────────────────────────────────────────────────

    private void OnDeath()
    {
        _alive = false;

        // Ghost leaves — disable the spring, body falls freely
        if (_joint != null) Destroy(_joint);

        // Apply death impulse at the bone nearest the bullet hit
        if (_lastHit.HitPoint != Vector3.zero)
        {
            Rigidbody hitBone = FindClosestBone(_lastHit.HitPoint);
            hitBone?.AddForceAtPosition(
                -_lastHit.HitNormal * _deathForce,
                _lastHit.HitPoint,
                ForceMode.Impulse);
        }

        // Note: NavMeshAgent is stopped by EnemyBrain.Die() — not touched here
        // to avoid ordering conflicts between OnDeath subscribers.
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void BuildJoint()
    {
        _joint = _hips.gameObject.AddComponent<ConfigurableJoint>();
        _joint.connectedBody = _anchorRb;

        // Disable auto-configuration so we control anchor positions explicitly
        _joint.autoConfigureConnectedAnchor = false;
        _joint.anchor          = Vector3.zero; // joint pivot at Hips center
        _joint.connectedAnchor = Vector3.zero; // joint target at anchor center

        // All axes free — spring drives position, limits would fight the ragdoll
        _joint.xMotion        = ConfigurableJointMotion.Free;
        _joint.yMotion        = ConfigurableJointMotion.Free;
        _joint.zMotion        = ConfigurableJointMotion.Free;
        _joint.angularXMotion = ConfigurableJointMotion.Free;
        _joint.angularYMotion = ConfigurableJointMotion.Free;
        _joint.angularZMotion = ConfigurableJointMotion.Free;

        var drive = new JointDrive
        {
            positionSpring = _spring,
            positionDamper = _damper,
            maximumForce   = _maxForce,
        };
        _joint.xDrive = drive;
        _joint.yDrive = drive;
        _joint.zDrive = drive;

        // Hips wants to sit at the anchor — zero offset from anchor center
        _joint.targetPosition = Vector3.zero;
    }

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
        if (_hips         == null) Debug.LogError($"[EnemyRagdoll] {name}: Hips Rigidbody is null.");
        if (_health       == null) Debug.LogError($"[EnemyRagdoll] {name}: EnemyHealth is null.");
    }
}
