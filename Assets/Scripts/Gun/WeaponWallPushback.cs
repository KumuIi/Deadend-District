using UnityEngine;

public class WeaponWallPushback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform gunTip;          // Point near the muzzle/front of the gun
    [SerializeField] private Transform castOrigin;      // Usually your camera or weapon root parent

    [Header("Detection")]
    [SerializeField] private float checkDistance = 1.0f;
    [SerializeField] private float sphereRadius = 0.08f;
    [SerializeField] private LayerMask collisionMask;

    [Header("Pushback")]
    [SerializeField] private float maxPushback = 0.5f;
    [SerializeField] private float pushbackPadding = 0.05f;
    [SerializeField] private float smoothSpeed = 12f;

    private Vector3 defaultLocalPos;
    private Vector3 currentLocalPos;
    private float currentPushback;

    private void Start()
    {
        defaultLocalPos = transform.localPosition;
        currentLocalPos = defaultLocalPos;

        if (castOrigin == null)
            castOrigin = Camera.main != null ? Camera.main.transform : transform.parent;
    }

    private void LateUpdate()
    {
        float targetPushback = 0f;

        if (gunTip != null && castOrigin != null)
        {
            Vector3 origin = castOrigin.position;
            Vector3 direction = (gunTip.position - origin).normalized;

            float distanceToTip = Vector3.Distance(origin, gunTip.position);
            float castDistance = Mathf.Max(checkDistance, distanceToTip);

            if (Physics.SphereCast(origin, sphereRadius, direction, out RaycastHit hit, castDistance, collisionMask, QueryTriggerInteraction.Ignore))
            {
                float desired = castDistance - hit.distance + pushbackPadding;
                targetPushback = Mathf.Clamp(desired, 0f, maxPushback);
            }
        }

        currentPushback = Mathf.Lerp(currentPushback, targetPushback, Time.deltaTime * smoothSpeed);

        Vector3 targetLocalPos = defaultLocalPos - new Vector3(0f, 0f, currentPushback);
        currentLocalPos = Vector3.Lerp(currentLocalPos, targetLocalPos, Time.deltaTime * smoothSpeed);

        transform.localPosition = currentLocalPos;
    }
}