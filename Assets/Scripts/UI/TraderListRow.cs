using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row in the trader UI — used for both the buy list (trader stock) and the sell list
/// (player loot). A dumb view: TraderUI binds the text + button callback each refresh.
/// Assign the references in the row prefab.
/// </summary>
public class TraderListRow : MonoBehaviour
{
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _detailText;   // price, stock count, etc.
    [SerializeField] private Button   _actionButton;
    [SerializeField] private TMP_Text _actionLabel;  // "Buy" / "Sell"

    private Action _onAction;

    public void Bind(string itemName, string detail, string actionLabel, bool interactable, Action onAction)
    {
        if (_nameText   != null) _nameText.text   = itemName;
        if (_detailText != null) _detailText.text = detail;
        if (_actionLabel != null) _actionLabel.text = actionLabel;

        _onAction = onAction;

        if (_actionButton != null)
        {
            _actionButton.interactable = interactable;
            _actionButton.onClick.RemoveAllListeners();
            _actionButton.onClick.AddListener(() => _onAction?.Invoke());
        }
    }
}
