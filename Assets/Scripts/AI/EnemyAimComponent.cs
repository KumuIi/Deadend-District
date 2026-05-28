using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Drives the enemy's upper-body IK and aim pivot.
/// Mirrors WeaponManager.ApplyIKTargets: sets TwoBoneIKConstraint targets to the
/// gun's hand grip transforms, then calls RigBuilder.Build().
///
/// [DefaultExecutionOrder(-200)] ensures the aim pivot rotation is written before
/// Animation Rigging's LateUpdate evaluates the TwoBoneIK constraints.
/// </summary>
[DefaultExecutionOrder(-200)]
public class EnemyAimComponent : MonoBehaviour
{
    [SerializeField] private Transform           _aimPivot;
    [SerializeField] private RigBuilder          _rigBuilder;
    [SerializeField] private TwoBoneIKConstraint _rightArmConstraint;
    [SerializeField] private TwoBoneIKConstraint _leftArmConstraint;

    [SerializeField] private float _aimClampAngle  = 80f;
    [SerializeField] private float _blendSpeed     = 5f;
    [Tooltip("Degrees per second the aim pivot rotates toward the target. Lower = more lag, easier to dodge.")]
    [Min(0f)]
    [SerializeField] private float _aimRotateSpeed = 45f;

    public Transform AimPivot => _aimPivot;

    private Transform _target;
    private bool      _aiming;        // controls pivot LookAt only
    private bool      _gunEquipped;   // controls IK weight — true as soon as gun is initialized
    private float     _currentWeight;

    private void Awake()
    {
        if (_aimPivot           == null) Debug.LogError($"[EnemyAimComponent] {name}: AimPivot is null.");
        if (_rigBuilder         == null) Debug.LogError($"[EnemyAimComponent] {name}: RigBuilder is null.");
        if (_rightArmConstraint == null) Debug.LogError($"[EnemyAimComponent] {name}: RightArmConstraint is null.");
        if (_leftArmConstraint  == null) Debug.LogError($"[EnemyAimComponent] {name}: LeftArmConstraint is null.");
    }

    /// <summary>
    /// Called by EnemyBrain after instantiating the gun. Mirrors WeaponManager.ApplyIKTargets:
    /// points each TwoBoneIKConstraint at the gun's hand grip transforms, then rebuilds the rig.
    /// </summary>
    public void Initialize(Transform rightGrip, Transform leftGrip)
    {
        if (rightGrip != null && _rightArmConstraint != null)
        {
            var d  = _rightArmConstraint.data;
            d.target = rightGrip;
            _rightArmConstraint.data = d;
        }
        else if (rightGrip == null)
            Debug.LogError($"[EnemyAimComponent] {name}: rightGrip is null — right hand IK will not work.");

        if (leftGrip != null && _leftArmConstraint != null)
        {
            var d  = _leftArmConstraint.data;
            d.target = leftGrip;
            _leftArmConstraint.data = d;
        }
        else if (leftGrip == null)
            Debug.LogError($"[EnemyAimComponent] {name}: leftGrip is null — left hand IK will not work.");

        _rigBuilder?.Build();
        _gunEquipped = true;   // hands now have valid targets — keep weight at 1 from here on
        Debug.Log($"[EnemyAimComponent] {name}: Rig built with new grip targets.");
    }

    public void SetTarget(Transform target) => _target = target;
    public void ClearTarget()               => _target = null;

    /// <summary>
    /// Engaged = actively tracking a target and driving the aim pivot.
    /// IK weight stays at 1 regardless — hands always hold the gun once equipped.
    /// </summary>
    public void SetEngaged(bool engaged) => _aiming = engaged;

    /// <summary>Called on death — immediately zeros both IK constraint weights so arms drop.</summary>
    public void Disarm()
    {
        _gunEquipped   = false;
        _currentWeight = 0f;
        if (_rightArmConstraint != null) _rightArmConstraint.weight = 0f;
        if (_leftArmConstraint  != null) _leftArmConstraint.weight  = 0f;
    }

    private void Update()
    {
        // IK weight: always 1 once the gun is equipped so arms never drop to T-pose.
        // Without a matching idle animation to fall back on, weight=0 causes the bind pose.
        float targetWeight = _gunEquipped ? 1f : 0f;
        _currentWeight = Mathf.MoveTowards(_currentWeight, targetWeight, _blendSpeed * Time.deltaTime);
        if (_rightArmConstraint != null) _rightArmConstraint.weight = _currentWeight;
        if (_leftArmConstraint  != null) _leftArmConstraint.weight  = _currentWeight;

        if (_aimPivot == null) return;

        if (!_aiming || _target == null)
        {
            // Not aiming — smoothly return pivot to body forward so gun faces straight ahead
            _aimPivot.rotation = Quaternion.Slerp(
                _aimPivot.rotation,
                Quaternion.LookRotation(transform.forward, Vector3.up),
                _blendSpeed * Time.deltaTime);
            return;
        }

        // Aiming — drive pivot toward target
        Vector3 dir = (_target.position + Vector3.up * 0.8f) - _aimPivot.position;
        if (dir.sqrMagnitude < 0.01f) return;

        // Clamp so the guard can't aim backward or too far off-axis,
        // and rotate toward target at limited speed so fast movement can dodge shots.
        if (Vector3.Angle(transform.forward, dir) < _aimClampAngle)
            _aimPivot.rotation = Quaternion.RotateTowards(
                _aimPivot.rotation,
                Quaternion.LookRotation(dir, Vector3.up),
                _aimRotateSpeed * Time.deltaTime);
    }
}
