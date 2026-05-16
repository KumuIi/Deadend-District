using UnityEngine;

/// <summary>
/// A world-space pickup. Implements IInteractable so PlayerInteractor shows
/// a prompt and calls Interact() on E-press.
/// </summary>
public class LootItemWorld : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemSO _itemSO;
    [SerializeField] private bool   _emitSoundOnPickup;
    [SerializeField] private float  _soundRadius = 8f;

    private InventoryUI _inventory;

    private void Start()
    {
        _inventory = FindObjectOfType<InventoryUI>();
        if (_inventory == null)
            Debug.LogWarning("[LootItemWorld] No InventoryUI found in scene.", this);
    }

    public bool   CanInteract(GameObject interactor) => _itemSO != null && _inventory != null;
    public string GetPrompt(GameObject interactor)   => $"Pick up {(_itemSO != null ? _itemSO.name : "item")}";

    public void Interact(GameObject interactor)
    {
        if (_itemSO == null || _inventory == null) return;

        var instance = ItemInstanceFactory.Create(_itemSO);
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

        Destroy(gameObject);
    }
}
