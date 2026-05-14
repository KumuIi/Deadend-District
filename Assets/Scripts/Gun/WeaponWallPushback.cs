using UnityEngine;

/// <summary>
/// Pulls the gun back on the Z axis when the muzzle is close to or inside a wall,
/// preventing the mesh from clipping through geometry.
///
/// Attach to the PARENT node that wraps the gun pivot (WeaponHolder), NOT to the
/// gun pivot itself — GunSway and GunController overwrite localPosition every frame
/// on the pivot object.
///
/// Hierarchy:
///   WeaponHolder  ← WeaponWallPushback lives here
///   └─ GunPivot   ← GunSway / GunController live here
///      └─ GunMesh
/// </summary>
[DefaultExecutionOrder(-100)]
public class WeaponWallPushback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform gunTip;      // Point near the muzzle / front of the gun
    [SerializeField] private Transform castOrigin;  // Usually the camera transform

    [Header("Detection")]
    [SerializeField] private float checkDistance = 1.0f;
    [SerializeField] private float sphereRadius  = 0.08f;
    [SerializeField] private LayerMask collisionMask;

    [Header("Pushback")]
    [SerializeField] private float maxPushback      = 0.5f;
    [SerializeField] private float pushbackPadding  = 0.05f;
    [SerializeField] private float smoothSpeed      = 12f;

    private Vector3 _defaultLocalPos;
    private Vector3 _currentLocalPos;

    private void Start()
    {
        _defaultLocalPos = transform.localPosition;
        _currentLocalPos = _defaultLocalPos;

        if (castOrigin == null)
            castOrigin = Camera.main != null ? Camera.main.transform : transform.parent;

        if (collisionMask == 0)
            collisionMask = LayerMask.GetMask("Default");
    }

    private void LateUpdate()
    {
        float targetPushback = 0f;

        if (gunTip != null && castOrigin != null)
        {
            Vector3 origin    = castOrigin.position;
            Vector3 direction = (gunTip.position - origin).normalized;

            float distanceToTip = Vector3.Distance(origin, gunTip.position);
            float castDistance  = Mathf.Max(checkDistance, distanceToTip);

            if (Physics.SphereCast(origin, sphereRadius, direction, out RaycastHit hit,
                castDistance, collisionMask, QueryTriggerInteraction.Ignore))
            {
                float desired = castDistance - hit.distance + pushbackPadding;
                targetPushback = Mathf.Clamp(desired, 0f, maxPushback);
            }
        }

        Vector3 targetLocalPos = _defaultLocalPos - new Vector3(0f, 0f, targetPushback);
        _currentLocalPos = Vector3.Lerp(_currentLocalPos, targetLocalPos, Time.deltaTime * smoothSpeed);
        transform.localPosition = _currentLocalPos;
    }
}
