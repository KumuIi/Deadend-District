using UnityEngine;

/// <summary>
/// Shows a marker (e.g. a ❗ sprite) above a <see cref="QuestGiver"/> whenever it has something for
/// the player — a new quest to offer, or a turn-in the player is carrying. Hides it otherwise.
///
/// Setup: put this on the NPC (next to the QuestGiver), assign the QuestGiver and an Icon object
/// (a child world-space sprite/quad). The icon is billboarded to face the camera.
/// </summary>
public class QuestGiverIcon : MonoBehaviour
{
    [Tooltip("The QuestGiver this marker reflects.")]
    [SerializeField] private QuestGiver _giver;

    [Tooltip("The visual to show/hide (a child sprite or quad floating above the NPC's head).")]
    [SerializeField] private GameObject _icon;

    [Tooltip("Re-check interval in seconds. Catches turn-in readiness (inventory has no global event).")]
    [SerializeField] private float _pollInterval = 0.4f;

    [Tooltip("Rotate the icon to face the camera each frame.")]
    [SerializeField] private bool _billboard = true;

    private float _timer;
    private Camera _cam;

    private void OnEnable()
    {
        _cam = Camera.main; // cached — Camera.main does a tag search, don't call it per frame
        if (QuestManager.Instance != null) QuestManager.Instance.OnQuestsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (QuestManager.Instance != null) QuestManager.Instance.OnQuestsChanged -= Refresh;
    }

    private void Update()
    {
        // Quest-status changes fire OnQuestsChanged; the poll additionally catches turn-in readiness
        // (picking up the hand-in item doesn't raise a quest event).
        _timer += Time.deltaTime;
        if (_timer >= _pollInterval) { _timer = 0f; Refresh(); }

        if (_billboard && _icon != null && _icon.activeSelf)
        {
            if (_cam == null) _cam = Camera.main; // re-acquire if the camera was swapped
            if (_cam != null)
                _icon.transform.rotation = Quaternion.LookRotation(
                    _icon.transform.position - _cam.transform.position);
        }
    }

    private void Refresh()
    {
        if (_icon == null || _giver == null) return;
        bool show = _giver.HasSomethingForPlayer();
        if (_icon.activeSelf != show) _icon.SetActive(show);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_giver == null) _giver = GetComponent<QuestGiver>();
    }
#endif
}
