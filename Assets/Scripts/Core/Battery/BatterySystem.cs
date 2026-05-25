using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that owns the player's active batteries and drives all IBatteryDrainer consumers.
///
/// Slot rules:
///   ActiveRechargeable — primary slot, refillable at RechargeStation (Wave 2).
///   ActiveOneTime      — overflow slot; drains first, remainder spills to rechargeable same frame.
///
/// Scene setup: place on a persistent GameObject. Assign _startingRechargeable in the Inspector.
/// </summary>
[DefaultExecutionOrder(-5)]
public class BatterySystem : MonoBehaviour
{
    public static BatterySystem Instance { get; private set; }

    [SerializeField] private BatteryItemSO _startingRechargeable;

    public BatteryItemInstance ActiveRechargeable { get; private set; }
    public BatteryItemInstance ActiveOneTime      { get; private set; }

    // ── Public properties ──────────────────────────────────────────────────

    public float ActiveCharge => (ActiveRechargeable?.CurrentCharge ?? 0f)
                               + (ActiveOneTime?.CurrentCharge      ?? 0f);

    public float ActiveMaxCharge => (ActiveRechargeable?.MaxCharge ?? 0f)
                                  + (ActiveOneTime?.MaxCharge      ?? 0f);

    public float ActiveChargeNormalized => ActiveMaxCharge > 0f
        ? ActiveCharge / ActiveMaxCharge : 0f;

    public bool IsDepleted => ActiveCharge <= 0f
                           && ActiveRechargeable == null && ActiveOneTime == null
                           || (ActiveRechargeable != null && ActiveRechargeable.CurrentCharge <= 0f
                               && ActiveOneTime == null);

    // ── Events ─────────────────────────────────────────────────────────────

    /// <summary>Normalized charge [0..1]. Fires when delta > 0.001.</summary>
    public event Action<float> OnChargeChanged;
    public event Action        OnBatteryDepleted;
    public event Action        OnChargeRestored;

    // ── Private ────────────────────────────────────────────────────────────

    private readonly List<IBatteryDrainer> _drainers   = new List<IBatteryDrainer>();
    private float _lastReportedNormalized = 1f;
    private bool  _wasDepleted;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (_startingRechargeable != null)
            ActiveRechargeable = new BatteryItemInstance(_startingRechargeable);
    }

    private void Update()
    {
        float totalDrain = 0f;
        foreach (var d in _drainers)
            totalDrain += d.DrainRate;

        if (totalDrain > 0f && (ActiveOneTime != null || ActiveRechargeable != null))
            ApplyDrain(totalDrain * Time.deltaTime);

        bool depleted = IsDepleted;
        if (depleted && !_wasDepleted)
        {
            _wasDepleted = true;
            OnBatteryDepleted?.Invoke();
        }
        else if (!depleted && _wasDepleted)
        {
            _wasDepleted = false;
            OnChargeRestored?.Invoke();
        }

        float norm = ActiveChargeNormalized;
        if (Mathf.Abs(norm - _lastReportedNormalized) > 0.001f)
        {
            _lastReportedNormalized = norm;
            OnChargeChanged?.Invoke(norm);
        }
    }

    private void ApplyDrain(float amount)
    {
        // OneTime drains first; overflow spills to rechargeable same frame.
        if (ActiveOneTime != null)
        {
            float overflow = amount - ActiveOneTime.CurrentCharge;
            ActiveOneTime.CurrentCharge = Mathf.Max(0f, ActiveOneTime.CurrentCharge - amount);
            if (ActiveOneTime.CurrentCharge <= 0f)
            {
                ActiveOneTime = null;
                if (overflow > 0f && ActiveRechargeable != null)
                    ActiveRechargeable.CurrentCharge =
                        Mathf.Max(0f, ActiveRechargeable.CurrentCharge - overflow);
            }
        }
        else if (ActiveRechargeable != null)
        {
            ActiveRechargeable.CurrentCharge =
                Mathf.Max(0f, ActiveRechargeable.CurrentCharge - amount);
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void RegisterDrainer(IBatteryDrainer drainer)
    {
        if (drainer == null || _drainers.Contains(drainer)) return;
        _drainers.Add(drainer);
    }

    public void UnregisterDrainer(IBatteryDrainer drainer) =>
        _drainers.Remove(drainer);

    public void SwapBattery(BatteryItemInstance newBattery)
    {
        bool wasDepletedBefore = _wasDepleted;

        if (newBattery.BatteryType == BatteryType.OneTime)
            ActiveOneTime = newBattery;
        else
            ActiveRechargeable = newBattery;

        _wasDepleted = IsDepleted;

        if (!wasDepletedBefore && _wasDepleted)  OnBatteryDepleted?.Invoke();
        if (wasDepletedBefore  && !_wasDepleted) OnChargeRestored?.Invoke();
    }

    /// <summary>Called by RechargeStation (Wave 2).</summary>
    public void RefillRechargeable()
    {
        if (ActiveRechargeable == null) return;
        ActiveRechargeable.CurrentCharge = ActiveRechargeable.MaxCharge;
    }
}
