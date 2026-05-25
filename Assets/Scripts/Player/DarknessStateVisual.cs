using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Blends a post-process Volume weight based on the "player.in_darkness" world state.
/// Reads from WorldStateManager only — never from BatterySystem directly.
/// </summary>
public class DarknessStateVisual : MonoBehaviour
{
    [SerializeField] private Volume _postProcessVolume;
    [SerializeField] private float  _blendSpeed = 2f;

    private float _targetWeight;

    private void OnEnable()
    {
        var wsm = WorldStateManager.Instance;
        if (wsm != null)
        {
            wsm.OnStateChanged += HandleStateChanged;
            bool inDarkness = wsm.GetBool("player.in_darkness");
            _targetWeight = inDarkness ? 1f : 0f;
            if (_postProcessVolume != null)
                _postProcessVolume.weight = _targetWeight;
        }
    }

    private void OnDisable()
    {
        var wsm = WorldStateManager.Instance;
        if (wsm != null)
            wsm.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (_postProcessVolume == null) return;
        _postProcessVolume.weight = Mathf.MoveTowards(
            _postProcessVolume.weight, _targetWeight, _blendSpeed * Time.deltaTime);
    }

    private void HandleStateChanged(string key, WorldStateValue _, WorldStateValue newVal)
    {
        if (key == "player.in_darkness")
            _targetWeight = newVal.AsBool() ? 1f : 0f;
    }
}
