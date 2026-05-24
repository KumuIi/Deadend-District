/// <summary>
/// Anything that consumes battery charge each frame.
/// Implementors: LightSource, Headlamp, MountedLight, CyberneticAugment (night vision etc.)
/// Register/unregister with BatterySystem in OnEnable/OnDisable.
/// Return DrainRate = 0 when inactive — do NOT unregister to pause draining.
/// </summary>
public interface IBatteryDrainer
{
    /// <summary>Battery units consumed per second. Return 0f when the drainer is inactive.</summary>
    float DrainRate { get; }

    /// <summary>Human-readable name shown in the debug battery HUD.</summary>
    string DrainerName { get; }
}
