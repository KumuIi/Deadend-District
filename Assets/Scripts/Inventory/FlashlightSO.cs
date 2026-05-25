using UnityEngine;

/// <summary>
/// ScriptableObject definition for a hand-held flashlight item.
/// The prefab carries all runtime behaviour (LightSource, Light, AudioSource, FlashlightView).
/// This SO is only the inventory identity: name, grid size, weight, and which prefab to spawn.
///
/// Implementors: standard flashlight (1x3), compact flashlight (1x2).
/// Equipped via FlashlightSlot ("flashlight" equipment slot).
/// </summary>
[CreateAssetMenu(menuName = "Deadend/Items/Flashlight")]
public class FlashlightSO : ItemSO
{
    [Tooltip("Must have FlashlightView, LightSource, Light, and AudioSource components.")]
    public GameObject flashlightPrefab;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (flashlightPrefab == null)
        {
            Debug.LogWarning($"[FlashlightSO] '{name}': flashlightPrefab is not assigned.", this);
            return;
        }
        if (flashlightPrefab.GetComponentInChildren<FlashlightView>() == null)
            Debug.LogWarning($"[FlashlightSO] '{name}': flashlightPrefab has no FlashlightView component.", this);
    }
#endif
}
