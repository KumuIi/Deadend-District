using UnityEngine;

/// <summary>
/// Tracks total inventory weight and pushes stat modifiers to PlayerMotor and PlayerHealth.
/// Recalculates only when InventoryGrid.OnChanged fires — not every frame.
/// Place on the Player root GameObject.
/// </summary>
public class EncumbranceSystem : MonoBehaviour
{
    [Header("=== References ===")]
    [SerializeField] private EncumbranceSO _config;
    [SerializeField] private InventoryUI   _inventoryUI;
    [SerializeField] private PlayerMotor   _motor;
    [SerializeField] private PlayerHealth  _health;

    // ── State ──────────────────────────────────────────────────────────────

    private float _currentWeightKg;

    public float CurrentWeightKg  => _currentWeightKg;
    public float MaxCarryWeightKg => _config != null ? _config.maxCarryWeightKg : 40f;

    public bool IsOverloaded => _config != null
        && _currentWeightKg / _config.maxCarryWeightKg >= _config.sprintBlockThreshold;

    // ── Stable modifier IDs ────────────────────────────────────────────────

    private const string IdSpeed   = "encumbrance.speed";
    private const string IdStamina = "encumbrance.stamina";
    private const string IdNoise   = "encumbrance.noise";

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_config == null)
            Debug.LogError("[EncumbranceSystem] EncumbranceSO not assigned.", this);
        if (_inventoryUI == null)
            Debug.LogError("[EncumbranceSystem] InventoryUI not assigned.", this);
        if (_motor == null)
            Debug.LogError("[EncumbranceSystem] PlayerMotor not assigned.", this);
        if (_health == null)
            Debug.LogError("[EncumbranceSystem] PlayerHealth not assigned.", this);
    }

    private void OnDisable()
    {
        if (_inventoryUI != null && _inventoryUI.Grid != null)
            _inventoryUI.Grid.OnChanged -= OnInventoryChanged;

        RemoveModifiers();
    }

    private void Start()
    {
        if (_inventoryUI != null && _inventoryUI.Grid != null)
            _inventoryUI.Grid.OnChanged += OnInventoryChanged;

        Recalculate();
    }

    // ── Core ───────────────────────────────────────────────────────────────

    private void OnInventoryChanged() => Recalculate();

    private void Recalculate()
    {
        if (_config == null || _inventoryUI == null) return;

        _currentWeightKg = 0f;
        foreach (var item in _inventoryUI.Grid.PlacedItems)
            _currentWeightKg += item.data.weightKg;

        float ratio = _config.maxCarryWeightKg > 0f
            ? _currentWeightKg / _config.maxCarryWeightKg
            : 0f;

        float speedMult   = Mathf.Max(0f, _config.speedPenaltyCurve.Evaluate(ratio));
        float staminaMult = Mathf.Max(0f, _config.staminaDrainCurve.Evaluate(ratio));
        float noiseMult   = Mathf.Max(0f, _config.noiseCurve.Evaluate(ratio));
        float bobMult     = Mathf.Max(0f, _config.bobFrequencyCurve.Evaluate(ratio));

        if (_motor != null)
        {
            _motor.StatModifiers.Remove(IdSpeed);
            _motor.StatModifiers.Add(new PlayerStatModifier
                { Id = IdSpeed, Stat = StatType.Speed, Value = speedMult, IsMultiplier = true });

            _motor.StatModifiers.Remove(IdNoise);
            _motor.StatModifiers.Add(new PlayerStatModifier
                { Id = IdNoise, Stat = StatType.NoiseMultiplier, Value = noiseMult, IsMultiplier = true });

            _motor.EncumbranceWeightMultiplier = bobMult;
        }

        if (_health != null)
        {
            _health.StatModifiers.Remove(IdStamina);
            _health.StatModifiers.Add(new PlayerStatModifier
                { Id = IdStamina, Stat = StatType.StaminaDrain, Value = staminaMult, IsMultiplier = true });
        }
    }

    private void RemoveModifiers()
    {
        _motor?.StatModifiers.Remove(IdSpeed);
        _motor?.StatModifiers.Remove(IdNoise);
        _health?.StatModifiers.Remove(IdStamina);
        if (_motor != null) _motor.EncumbranceWeightMultiplier = 1f;
    }
}
