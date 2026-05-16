/// <summary>
/// Implement on any MonoBehaviour that reacts to world stimuli (AI, alarms, proximity triggers).
/// Register/unregister with StimulusSystem.Instance in OnEnable/OnDisable.
/// </summary>
public interface IStimulusListener
{
    /// <summary>Which stimulus types this listener cares about. Checked before OnStimulus is called.</summary>
    StimulusType[] ListensTo { get; }

    /// <summary>Called when a matching stimulus is broadcast within its radius.</summary>
    void OnStimulus(in Stimulus stimulus);
}
