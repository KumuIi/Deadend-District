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
    [Tooltip("Total charge capacity. Drain rate from LightSource is subtracted per second.")]
    [Min(0f)] public float maxCharge = 100f;
}
