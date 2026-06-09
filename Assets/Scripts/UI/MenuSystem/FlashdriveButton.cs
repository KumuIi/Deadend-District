using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// One physical flashdrive in the save/load menu.
/// Flies in from the right on local X. On select: moves to an anchor in front of
/// the camera and resets local Z rotation to -90. On deselect: returns to base.
///
/// FlyOutLeft  — return drive continues leftward off screen.
/// ShrinkOut   — slot drives scale to zero when closing.
///
/// Implementors: one per flashdrive model under FlashdriveMenuRoot.
/// </summary>
public class FlashdriveButton : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private bool   _isReturn  = false;
    [SerializeField] private string _slotName  = "slot0";
    [SerializeField] private TextMeshPro _label;

    [Header("Fly in / out (local X)")]
    [SerializeField] private float _flyInDistance  = 0.3f;
    [SerializeField] private float _flyInDuration  = 0.22f;
    [SerializeField] private Ease  _flyInEase      = Ease.OutBack;
    [SerializeField] private float _flyOutDistance = 0.5f;
    [SerializeField] private float _flyOutDuration = 0.2f;
    [SerializeField] private Ease  _flyOutEase     = Ease.InBack;

    [Header("Shrink (slot drives on close)")]
    [SerializeField] private float _shrinkDuration = 0.18f;

    [Header("Select (move to anchor)")]
    [SerializeField] private float _selectDuration = 0.25f;

    [Header("Hover")]
    [SerializeField] private float _hoverOffset   = 0.008f;
    [SerializeField] private float _hoverDuration = 0.12f;

    // ── State ──────────────────────────────────────────────────────────────

    public bool   IsReturn   => _isReturn;
    public string SlotName   => _slotName;
    public bool   IsSelected { get; private set; }

    // FlashdriveMenuController subscribes to this
    public event Action<FlashdriveButton> OnClicked;

    private Vector3 _baseLocalPos;
    private Vector3 _baseLocalEuler;
    private Vector3 _baseLocalScale;
    private bool    _baseInitialized;
    private bool    _isHovered;

    // ── Init ───────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        MenuHitRegistry<FlashdriveButton>.Register(this);
        ResetToBase();
    }

    private void OnDisable() => MenuHitRegistry<FlashdriveButton>.Unregister(this);

    public void ResetToBase()
    {
        transform.DOKill();

        if (!_baseInitialized)
        {
            _baseLocalPos   = transform.localPosition;
            _baseLocalEuler = transform.localEulerAngles;
            _baseLocalScale = transform.localScale;
            _baseInitialized = true;
        }

        transform.localPosition    = new Vector3(_baseLocalPos.x + _flyInDistance, _baseLocalPos.y, _baseLocalPos.z);
        transform.localEulerAngles = _baseLocalEuler;
        transform.localScale       = _baseLocalScale;
        IsSelected = false;
        _isHovered = false;

        if (_label != null && !_isReturn)
            _label.text = SlotDisplayName();
    }

    // ── Fly in / out ───────────────────────────────────────────────────────

    public void FlyIn(float delay = 0f)
    {
        transform.DOKill();
        transform.DOLocalMoveX(_baseLocalPos.x, _flyInDuration)
                 .SetDelay(delay).SetEase(_flyInEase).SetUpdate(true);
    }

    /// <summary>
    /// Return drive: snaps back to base (undoing any hover offset) then continues
    /// leftward off screen, so it visually passes through base before exiting.
    /// </summary>
    public void FlyOutLeft(float delay = 0f)
    {
        transform.DOKill();
        Sequence seq = DOTween.Sequence().SetUpdate(true).SetDelay(delay);
        // Step 1: return to base position (undo hover)
        seq.Append(transform.DOLocalMoveX(_baseLocalPos.x, _hoverDuration)
                            .SetEase(Ease.OutQuad));
        // Step 2: continue left past base to exit
        seq.Append(transform.DOLocalMoveX(_baseLocalPos.x - _flyOutDistance, _flyOutDuration)
                            .SetEase(_flyOutEase));
    }

    /// <summary>Slot drives shrink to zero on close.</summary>
    public void ShrinkOut(float delay = 0f)
    {
        transform.DOKill();
        transform.DOScale(Vector3.zero, _shrinkDuration)
                 .SetDelay(delay).SetEase(Ease.InBack).SetUpdate(true);
    }

    // ── Select / Deselect ──────────────────────────────────────────────────

    /// <summary>Move to the given world-space anchor and reset Z rotation to -90.</summary>
    public void Select(Transform anchor)
    {
        if (IsSelected) return;
        IsSelected = true;
        _isHovered = false;

        transform.DOKill();

        Vector3 targetEuler = new Vector3(_baseLocalEuler.x, _baseLocalEuler.y, -90f);

        Sequence seq = DOTween.Sequence().SetUpdate(true);
        seq.Join(transform.DOMove(anchor.position, _selectDuration).SetEase(Ease.OutQuart));
        seq.Join(transform.DOLocalRotate(targetEuler, _selectDuration).SetEase(Ease.OutQuart));

        if (_label != null) _label.text = "Confirm";
    }

    public void Deselect()
    {
        if (!IsSelected) return;
        IsSelected = false;

        transform.DOKill();

        Sequence seq = DOTween.Sequence().SetUpdate(true);
        seq.Join(transform.DOLocalMove(_baseLocalPos, _selectDuration).SetEase(Ease.OutQuart));
        seq.Join(transform.DOLocalRotate(_baseLocalEuler, _selectDuration).SetEase(Ease.OutQuart));

        RefreshLabel(SaveSlotButton3D.SlotMode.Load); // controller will re-set correct mode
    }

    // ── Hover ──────────────────────────────────────────────────────────────

    public void OnHoverEnter()
    {
        if (_isHovered || IsSelected) return;
        _isHovered = true;
        transform.DOLocalMoveX(_baseLocalPos.x + _hoverOffset, _hoverDuration)
                 .SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnHoverExit()
    {
        if (!_isHovered || IsSelected) return;
        _isHovered = false;
        transform.DOLocalMoveX(_baseLocalPos.x, _hoverDuration)
                 .SetEase(Ease.OutQuad).SetUpdate(true);
    }

    // ── Click ──────────────────────────────────────────────────────────────

    public void Click() => OnClicked?.Invoke(this);

    // ── Label ──────────────────────────────────────────────────────────────

    /// <summary>Sets label to slot name only — the default non-hovered state.</summary>
    public void RefreshLabel(SaveSlotButton3D.SlotMode mode)
    {
        if (_label == null || _isReturn) return;
        _label.text = SlotDisplayName();
    }

    private string SlotDisplayName()
    {
        // e.g. "slot0" → "SLOT 1"
        if (int.TryParse(_slotName.Replace("slot", ""), out int idx))
            return $"SLOT {idx + 1}";
        return _slotName.ToUpper();
    }

    private string BuildStatsText()
    {
        var meta = SaveMetadataIO.Read(_slotName);
        if (meta == null) return $"{SlotDisplayName()}\n<size=80%>EMPTY</size>";

        string loc    = string.IsNullOrEmpty(meta.SceneId) ? "?" : meta.SceneId;
        string time   = FormatTime(meta.PlaySeconds);
        string creds  = $"{meta.Credits:N0} cr";
        string date   = FormatDate(meta.SaveTime);
        return $"<b>{SlotDisplayName()}</b>\n{loc}  ·  {time}\n{creds}  ·  {date}";
    }

    private static string FormatTime(float s)
    {
        int h = (int)(s / 3600), m = (int)((s % 3600) / 60);
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }

    private static string FormatDate(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (System.DateTime.TryParse(iso, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("MMM d · HH:mm");
        return "";
    }
}
