using System.Collections;
using UnityEngine;

/// <summary>
/// Writes "player.in_darkness" to WorldStateManager based on flashlight depletion.
/// After _huntDelaySeconds of darkness, broadcasts a Hunt stimulus so MonsterAI can respond.
/// Subscribes to FlashlightSlot events — no BatterySystem dependency.
/// </summary>
public class DarknessStateWriter : MonoBehaviour
{
    [SerializeField] private FlashlightSlot _flashlightSlot;
    [SerializeField] private float          _huntDelaySeconds = 120f;

    private Coroutine _huntTimer;

    private void OnEnable()
    {
        if (_flashlightSlot == null) return;
        _flashlightSlot.OnDepleted  += HandleDepleted;
        _flashlightSlot.OnRestored  += HandleRestored;

        // Sync initial state
        if (_flashlightSlot.IsDepleted) HandleDepleted();
    }

    private void OnDisable()
    {
        StopHuntTimer();
        if (_flashlightSlot == null) return;
        _flashlightSlot.OnDepleted  -= HandleDepleted;
        _flashlightSlot.OnRestored  -= HandleRestored;
    }

    private void HandleDepleted()
    {
        WorldStateManager.Instance?.SetBool("player.in_darkness", true);
        StopHuntTimer();
        _huntTimer = StartCoroutine(HuntTimerRoutine());
    }

    private void HandleRestored()
    {
        WorldStateManager.Instance?.SetBool("player.in_darkness", false);
        StopHuntTimer();
    }

    private void StopHuntTimer()
    {
        if (_huntTimer != null) { StopCoroutine(_huntTimer); _huntTimer = null; }
    }

    private IEnumerator HuntTimerRoutine()
    {
        yield return new WaitForSeconds(_huntDelaySeconds);
        StimulusSystem.Instance?.Broadcast(new Stimulus(
            StimulusType.Hunt,
            transform.position,
            999f,
            1f,
            gameObject,
            gameObject));
    }
}
