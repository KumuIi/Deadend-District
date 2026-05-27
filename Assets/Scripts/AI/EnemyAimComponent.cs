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

    [SerializeField] private float _aimClampAngle = 80f;
    [SerializeField] private float _blendSpeed    = 5f;

    public Transform AimPivot => _aimPivot;

    private Transform _target;
    private bool      _engaged;
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
        Debug.Log($"[EnemyAimComponent] {name}: Rig built with new grip targets.");
    }

    public void SetTarget(Transform target) => _target = target;
    public void ClearTarget()               => _target = null;

    public void SetEngaged(bool engaged) => _engaged = engaged;

    private void Update()
    {
        // Blend IK weights in/out as guard enters or leaves combat
        float targetWeight = _engaged ? 1f : 0f;
        _currentWeight = Mathf.MoveTowards(_currentWeight, targetWeight, _blendSpeed * Time.deltaTime);
        if (_rightArmConstraint != null) _rightArmConstraint.weight = _currentWeight;
        if (_leftArmConstraint  != null) _leftArmConstraint.weight  = _currentWeight;

        // Drive aim pivot toward target
        if (_aimPivot == null || _target == null || !_engaged) return;

        Vector3 dir = (_target.position + Vector3.up * 0.8f) - _aimPivot.position;
        if (dir.sqrMagnitude < 0.01f) return;

        // Clamp so the guard can't aim backward or too far off-axis
        if (Vector3.Angle(transform.forward, dir) < _aimClampAngle)
            _aimPivot.rotation = Quaternion.LookRotation(dir);
    }
}
