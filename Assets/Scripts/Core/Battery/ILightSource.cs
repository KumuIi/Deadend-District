public enum LightMode { Off, Dim, Bright }

/// <summary>
/// Implemented by LightSource (and future headlamp, mounted light).
/// FlickerVisual is visual-only — it does not change mode or drain rate.
/// Only LowBatteryWarning calls FlickerVisual.
/// </summary>
public interface ILightSource
{
    bool      IsOn        { get; }
    float     Intensity   { get; }
    LightMode CurrentMode { get; }

    void Toggle();
    void SetMode(LightMode mode);

    /// <summary>Toggles the light mesh on/off without changing LightMode or drain rate.</summary>
    void FlickerVisual(bool on);
}
