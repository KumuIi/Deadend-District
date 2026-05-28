using UnityEngine;

/// <summary>
/// Debug helper — press L to start a run in place (no scene loading).
/// The player physically walks to the sector via a door.
/// Remove or disable before shipping.
/// </summary>
public class DebugRunStarter : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            if (RunManager.Instance == null)
            {
                Debug.LogWarning("[DebugRunStarter] RunManager not found.");
                return;
            }
            RunManager.Instance.StartRunInPlace();
        }
    }
}
