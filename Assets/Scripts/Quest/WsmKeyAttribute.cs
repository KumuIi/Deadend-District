using UnityEngine;

/// <summary>
/// Apply to any serialized string field that holds a WorldStateManager key.
/// The WsmKeyDrawer (in Editor/) replaces the plain text field with a searchable
/// dropdown backed by WsmKeyRegistrySO.
///
/// Example:
///   [WsmKey] public string wsmKey;
///   [SerializeField, WsmKey] private string _key;
/// </summary>
public class WsmKeyAttribute : PropertyAttribute { }
