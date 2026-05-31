using UnityEngine;

/// <summary>
/// Procedurally plants the Mimic's leg tips on the nearest surfaces so it reads as a
/// spider-orb gripping whatever it's near — walls, floor, ceiling, props. Purely visual:
/// it drives a set of "tip" transforms that an IK rig (Two-Bone IK constraints) targets.
/// Movement and AI live in <see cref="MonsterAI"/>; this only positions feet.
///
/// Each leg casts a ray from the body outward along that leg's rest direction (rotated with
/// the body). Where it hits, the foot steps to the hit point. Feet only re-step once they've
/// drifted past a threshold, which avoids constant sliding and gives a discrete "stepping" feel.
/// </summary>
public class MonsterLegPlacer : MonoBehaviour
{
    [Tooltip("Body/centre the legs cast out from. Defaults to this transform.")]
    [SerializeField] private Transform _body;
    [Tooltip("Leg-tip transforms to drive. Their starting offset from the body defines each " +
             "leg's reach direction. Wire these as IK targets.")]
    [SerializeField] private Transform[] _legTargets;

    [Tooltip("Max distance a leg reaches for a surface.")]
    [SerializeField] private float _legReach = 2f;
    [Tooltip("How far a foot may drift from its surface target before it re-steps.")]
    [SerializeField] private float _stepThreshold = 0.45f;
    [Tooltip("Foot move speed toward a new step position.")]
    [SerializeField] private float _stepSpeed = 10f;
    [Tooltip("Surfaces the legs can grip — same mask as the Mimic's crawl surfaces.")]
    [SerializeField] private LayerMask _surfaceMask = ~0;

    private Vector3[] _restDirLocal;  // each leg's reach direction, in body local space
    private Vector3[] _footTarget;    // where each foot is currently stepping to (world)

    private void Awake()
    {
        if (_body == null) _body = transform;
        if (_legTargets == null || _legTargets.Length == 0) return;

        _restDirLocal = new Vector3[_legTargets.Length];
        _footTarget   = new Vector3[_legTargets.Length];

        for (int i = 0; i < _legTargets.Length; i++)
        {
            if (_legTargets[i] == null) continue;
            Vector3 worldDir = _legTargets[i].position - _body.position;
            if (worldDir.sqrMagnitude < 0.0001f) worldDir = _body.forward;
            _restDirLocal[i] = _body.InverseTransformDirection(worldDir.normalized);
            _footTarget[i]   = _legTargets[i].position;
        }
    }

    private void LateUpdate()
    {
        if (_legTargets == null || _restDirLocal == null) return;

        for (int i = 0; i < _legTargets.Length; i++)
        {
            Transform tip = _legTargets[i];
            if (tip == null) continue;

            Vector3 castDir = _body.TransformDirection(_restDirLocal[i]);
            Vector3 desired;
            if (Physics.Raycast(_body.position, castDir, out RaycastHit hit, _legReach, _surfaceMask))
                desired = hit.point;
            else
                desired = _body.position + castDir * _legReach; // no surface — dangle at reach

            // Re-step only once the surface target has moved past the threshold.
            if (Vector3.Distance(_footTarget[i], desired) > _stepThreshold)
                _footTarget[i] = desired;

            tip.position = Vector3.MoveTowards(tip.position, _footTarget[i], _stepSpeed * Time.deltaTime);
        }
    }
}
