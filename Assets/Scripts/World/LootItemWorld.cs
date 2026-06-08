using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A world-space pickup. Implements IInteractable so PlayerInteractor shows
/// a prompt and calls Interact() on E-press.
/// </summary>
public class LootItemWorld : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemSO _itemSO;
    [SerializeField] private bool   _emitSoundOnPickup;
    [SerializeField] private float  _soundRadius = 8f;

    /// <summary>Fired just before the GameObject is destroyed on successful pickup.</summary>
    public UnityEvent OnPickup;

    /// <summary>Global signal: ANY loot picked up (the ItemSO collected). Used by ObjectiveService.</summary>
    public static event System.Action<ItemSO> AnyPickup;

    private ItemInstance _instance; // set when spawned from inventory drop
    private InventoryUI  _inventory;

    /// <summary>
    /// Called by ItemDropSpawner when spawning a dropped inventory item.
    /// Preserves the live ItemInstance (loaded mag, ammo count, etc.).
    /// </summary>
    public void Initialize(ItemInstance instance)
    {
        _instance = instance;
        _itemSO   = instance?.data;
    }

    private void Start()
    {
        // Route to the PLAYER inventory specifically — FindObjectOfType could bind to the stash
        // (or any other) grid now that multiple InventoryUI panels exist.
        _inventory = InventoryUI.Player;
        if (_inventory == null)
            Debug.LogWarning("[LootItemWorld] No player InventoryUI registered.", this);
    }

    public bool   CanInteract(GameObject interactor) => (_itemSO != null) && _inventory != null;
    public string GetPrompt(GameObject interactor)   => $"Pick up {(_itemSO != null ? _itemSO.itemName : "item")}";

    public void Interact(GameObject interactor)
    {
        if (_itemSO == null || _inventory == null) return;

        // Use the live instance when dropped from inventory; create fresh for scene-placed loot
        var instance = _instance ?? ItemInstanceFactory.Create(_itemSO);
        if (_inventory.TryPickup(instance) == PickupResult.NoSpace)
        {
            Debug.Log("[LootItemWorld] Inventory full.");
            return;
        }

        if (_emitSoundOnPickup && StimulusSystem.Instance != null)
        {
            StimulusSystem.Instance.Broadcast(new Stimulus(
                StimulusType.Sound,
                position:   transform.position,
                radius:     _soundRadius,
                intensity:  0.3f,
                source:     gameObject,
                instigator: interactor
            ));
        }

        OnPickup?.Invoke();
        AnyPickup?.Invoke(_itemSO);
        Destroy(gameObject);
    }
}
