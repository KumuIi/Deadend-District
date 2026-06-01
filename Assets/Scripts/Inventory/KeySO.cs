using UnityEngine;

/// <summary>
/// A key item. Unlocks the <see cref="LockedDoor"/> whose <c>doorId</c> matches
/// <see cref="targetDoorId"/>. Pure data — the unlock logic lives on the door,
/// which searches the player grid for a KeySO with the right id.
///
/// Design note: key↔door coupling is by string id (data-driven), not by asset
/// reference. This mirrors the weapon/ammo and trader-stock patterns — assets stay
/// decoupled and the same id can be reused by quests, map rewards, etc.
/// </summary>
[CreateAssetMenu(menuName = "Deadend/Items/Key")]
public class KeySO : ItemSO
{
    [Header("=== Key ===")]
    [Tooltip("Must match the LockedDoor.doorId of the door this key opens. " +
             "The full WSM flag becomes \"door.{doorId}.unlocked\".")]
    public string targetDoorId;

    [Tooltip("Consume the key from the inventory the first time it unlocks a door. " +
             "Uncheck for reusable / master keys.")]
    public bool singleUse = true;
}
