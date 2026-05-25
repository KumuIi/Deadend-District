using System;
using UnityEngine;

/// <summary>
/// Saves/restores battery charge mid-run.
/// Resets on death or extraction (RunScopeTag.Run).
/// Attach to the same GameObject as BatterySystem.
/// </summary>
public class BatterySaveAdapter : MonoBehaviour, ISaveable
{
    public string      SaveId    => "player.battery";
    public string      SaveType  => "BatterySystem";
    public RunScopeTag SaveScope => RunScopeTag.Run;

    private void Start()       => SaveSystem.Instance?.Register(this);
    private void OnDisable()   => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData()
    {
        var bs = BatterySystem.Instance;
        return new BatterySaveData
        {
            rechargeableCharge = bs?.ActiveRechargeable?.CurrentCharge ?? -1f,
            oneTimeCharge      = bs?.ActiveOneTime?.CurrentCharge      ?? -1f,
        };
    }

    public void RestoreSaveData(object data)
    {
        var dto = JsonUtility.FromJson<BatterySaveData>((string)data);
        if (dto == null) return;

        var bs = BatterySystem.Instance;
        if (bs == null) return;

        if (dto.rechargeableCharge >= 0f && bs.ActiveRechargeable != null)
            bs.ActiveRechargeable.CurrentCharge = dto.rechargeableCharge;
        if (dto.oneTimeCharge >= 0f && bs.ActiveOneTime != null)
            bs.ActiveOneTime.CurrentCharge = dto.oneTimeCharge;
    }
}

[Serializable]
public class BatterySaveData
{
    public float rechargeableCharge;
    public float oneTimeCharge;
}
