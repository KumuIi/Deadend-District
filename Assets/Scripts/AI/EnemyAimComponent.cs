using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(-200)]
public class EnemyAimComponent : MonoBehaviour
{
    [SerializeField] private Transform           _aimPivot;
    [SerializeField] private RigBuilder          _rigBuilder;
    [SerializeField] private TwoBoneIKConstraint _rightArmConstraint;
    [SerializeField] private TwoBoneIKConstraint _leftArmConstraint;

    [SerializeField] private float _aimClampAngle  = 80f;
    [SerializeField] private float _blendSpeed     = 5f;
    [Min(0f)]
    [SerializeField] private float _aimRotateSpeed = 45f;

    public Transform AimPivot => _aimPivot;

    private Transform _target;
    private bool      _aiming;
    private bool      _gunEquipped;
    private float     _currentWeight;

    private void Awake()
    {
        if (_aimPivot           == null) Debug.LogError($"[EnemyAimComponent] {name}: AimPivot is null.");
        if (_rigBuilder         == null) Debug.LogError($"[EnemyAimComponent] {name}: RigBuilder is null.");
        if (_rightArmConstraint == null) Debug.LogError($"[EnemyAimComponent] {name}: RightArmConstraint is null.");
        if (_leftArmConstraint  == null) Debug.LogError($"[EnemyAimComponent] {name}: LeftArmConstraint is null.");
    }

    public void Initialize(Transform rightGrip, Transform leftGrip)
    {
        if (rightGrip == null) Debug.LogError($"[EnemyAimComponent] {name}: rightGrip is null.");
        if (leftGrip  == null) Debug.LogError($"[EnemyAimComponent] {name}: leftGrip is null.");

        SetConstraintTarget(_rightArmConstraint, rightGrip);
        SetConstraintTarget(_leftArmConstraint,  leftGrip);

        _rigBuilder?.Build();
        _gunEquipped = true;
        Debug.Log($"[EnemyAimComponent] {name}: Rig built with grip targets.");
    }

    public void SetTarget(Transform target) => _target = target;
    public void ClearTarget()               => _target = null;
    public void SetEngaged(bool engaged)    => _aiming  = engaged;

    /// <summary>
    /// Destroys this enemy's RigBuilder component, which tears down its PlayableGraph
    /// via RigBuilder.OnDestroy → Clear(). This stops AR from writing to the arm bones
    /// without triggering the scene-wide rebuild that OnDisable/OnEnable causes.
    /// The gun must be destroyed one frame AFTER this call (see EnemyBrain.Die).
    /// </summary>
    public void Disarm()
    {
        _gunEquipped   = false;
        _aiming        = false;
        _target        = null;
        _currentWeight = 0f;

        if (_rigBuilder != null)
            Destroy(_rigBuilder);
    }

    private static void SetConstraintTarget(TwoBoneIKConstraint constraint, Transform target)
    {
        if (constraint == null) return;
        var d    = constraint.data;
        d.target = target;
        constraint.data = d;
    }

    private void Update()
    {
        float targetWeight = _gunEquipped ? 1f : 0f;
        _currentWeight = Mathf.MoveTowards(_currentWeight, targetWeight, _blendSpeed * Time.deltaTime);
        if (_rightArmConstraint != null) _rightArmConstraint.weight = _currentWeight;
        if (_leftArmConstraint  != null) _leftArmConstraint.weight  = _currentWeight;

        if (_aimPivot == null) return;

        if (!_aiming || _target == null)
        {
            _aimPivot.rotation = Quaternion.Slerp(
                _aimPivot.rotation,
                Quaternion.LookRotation(transform.forward, Vector3.up),
                _blendSpeed * Time.deltaTime);
            return;
        }

        Vector3 dir = (_target.position + Vector3.up * 0.8f) - _aimPivot.position;
        if (dir.sqrMagnitude < 0.01f) return;

        if (Vector3.Angle(transform.forward, dir) < _aimClampAngle)
            _aimPivot.rotation = Quaternion.RotateTowards(
                _aimPivot.rotation,
                Quaternion.LookRotation(dir, Vector3.up),
                _aimRotateSpeed * Time.deltaTime);
    }
}
