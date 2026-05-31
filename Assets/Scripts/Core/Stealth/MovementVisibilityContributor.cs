using UnityEngine;

/// <summary>
/// Visibility contributor for how the player is moving. Faster motion is easier to spot;
/// crouching makes you harder to see. Merges the movement and crouch factors the plan
/// lists separately:
///   sprinting → 1.0, walking → 0.7, still → 0.2; crouching multiplies the result by 0.5.
/// </summary>
public class MovementVisibilityContributor : MonoBehaviour, IVisibilityContributor
{
    [Tooltip("PlayerVisibility this contributor registers with. Falls back to the singleton.")]
    [SerializeField] private PlayerVisibility _visibility;
    [SerializeField] private PlayerMotor _motor;

    [Header("Movement Factors")]
    [SerializeField] private float _sprintFactor = 1.0f;
    [SerializeField] private float _walkFactor   = 0.7f;
    [SerializeField] private float _stillFactor  = 0.2f;
    [Tooltip("Multiplier applied on top while crouching.")]
    [SerializeField] private float _crouchMultiplier = 0.5f;
    [Tooltip("Horizontal speed above which the player counts as moving (walking).")]
    [SerializeField] private float _movingSpeedThreshold = 0.5f;

    public string ContributorName => "Movement";

    // Register in both OnEnable and Start so a missed registration (when the singleton
    // isn't ready during OnEnable) is backstopped after all Awakes run. Register dedups.
    private void OnEnable() => Resolve()?.Register(this);
    private void Start()    => Resolve()?.Register(this);
    private void OnDisable() => Resolve()?.Unregister(this);

    private PlayerVisibility Resolve() =>
        _visibility != null ? _visibility : PlayerVisibility.Instance;

    public float GetVisibilityFactor()
    {
        if (_motor == null) return _walkFactor;

        float factor;
        if (_motor.IsSprinting)
            factor = _sprintFactor;
        else if (_motor.HorizontalVelocity.magnitude >= _movingSpeedThreshold)
            factor = _walkFactor;
        else
            factor = _stillFactor;

        if (_motor.IsCrouching)
            factor *= _crouchMultiplier;

        return factor;
    }
}
