using System.Collections;
using UnityEngine;

/// <summary>
/// Writes "player.in_darkness" to WorldStateManager based on battery depletion.
/// After _huntDelaySeconds of darkness, broadcasts a Hunt stimulus so MonsterAI can respond.
/// Guards do NOT listen to StimulusType.Hunt — only MonsterAI (Wave 3).
///
/// NEVER write WSM from the visual side. NEVER read BatterySystem from the visual side.
/// </summary>
public class DarknessStateWriter : MonoBehaviour
{
    [SerializeField] private float _huntDelaySeconds = 120f;

    private Coroutine _huntTimer;

    private void OnEnable()
    {
        var bs = BatterySystem.Instance;
        if (bs == null) return;

        bs.OnBatteryDepleted += HandleDepleted;
        bs.OnChargeRestored  += HandleRestored;

        if (bs.IsDepleted) HandleDepleted();
    }

    private void OnDisable()
    {
        StopHuntTimer();

        var bs = BatterySystem.Instance;
        if (bs == null) return;

        bs.OnBatteryDepleted -= HandleDepleted;
        bs.OnChargeRestored  -= HandleRestored;
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
