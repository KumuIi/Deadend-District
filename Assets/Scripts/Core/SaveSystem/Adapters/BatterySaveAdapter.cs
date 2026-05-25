using System;
using UnityEngine;

/// <summary>
/// Saves/restores the equipped flashlight's current charge mid-run.
/// Resets on death or extraction (RunScopeTag.Run).
/// Attach to the same GameObject as FlashlightSlot (or any persistent GO).
/// </summary>
public class BatterySaveAdapter : MonoBehaviour, ISaveable
{
    [SerializeField] private FlashlightSlot _flashlightSlot;

    public string      SaveId    => "player.battery";
    public string      SaveType  => "FlashlightCharge";
    public RunScopeTag SaveScope => RunScopeTag.Run;

    private void Start()     => SaveSystem.Instance?.Register(this);
    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData()
    {
        float charge = _flashlightSlot?.EquippedFlashlight?.CurrentCharge ?? -1f;
        return new FlashlightChargeSaveData { currentCharge = charge };
    }

    public void RestoreSaveData(object data)
    {
        var dto = JsonUtility.FromJson<FlashlightChargeSaveData>((string)data);
        if (dto == null || dto.currentCharge < 0f) return;

        var fl = _flashlightSlot?.EquippedFlashlight;
        if (fl == null) return;
        fl.CurrentCharge = Mathf.Clamp(dto.currentCharge, 0f, fl.MaxCharge);
    }
}

[Serializable]
public class FlashlightChargeSaveData
{
    public float currentCharge;
}
