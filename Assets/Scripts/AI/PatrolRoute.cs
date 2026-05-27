using UnityEngine;

public class PatrolRoute : MonoBehaviour
{
    [SerializeField] private Transform[] _waypoints;

    public int Count => _waypoints?.Length ?? 0;

    public Transform GetWaypoint(int index)
    {
        if (_waypoints == null || _waypoints.Length == 0) return transform;
        return _waypoints[index % _waypoints.Length];
    }

    private void OnDrawGizmosSelected()
    {
        if (_waypoints == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < _waypoints.Length; i++)
        {
            if (_waypoints[i] == null) continue;
            Gizmos.DrawSphere(_waypoints[i].position, 0.2f);
            int next = (i + 1) % _waypoints.Length;
            if (_waypoints[next] != null)
                Gizmos.DrawLine(_waypoints[i].position, _waypoints[next].position);
        }
    }
}
