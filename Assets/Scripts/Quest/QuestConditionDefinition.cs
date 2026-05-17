using System;
using UnityEngine;

/// <summary>
/// A single evaluatable condition backed by WorldStateManager.
/// Used for quest activation, success objectives, and fail conditions — same struct, three roles.
///
/// Evaluation rules:
///   Bool/String : Equals, NotEquals only (numeric comparisons are no-ops → false)
///   Int/Float   : all comparisons; float Equals uses 0.001 epsilon
/// </summary>
[Serializable]
public class QuestConditionDefinition
{
    [WsmKey] public string wsmKey;
    public string          description;
    public QuestValueType  valueType  = QuestValueType.Bool;
    public QuestComparison comparison = QuestComparison.Equals;

    public bool   expectedBool;
    public int    expectedInt;
    public float  expectedFloat;
    public string expectedString;

    private const float FloatEpsilon = 0.001f;

    public bool Evaluate()
    {
        if (string.IsNullOrEmpty(wsmKey) || WorldStateManager.Instance == null)
            return false;

        // A key that was never set means the world fact doesn't exist yet — treat as false.
        // This prevents NotEquals from spuriously passing on absent keys.
        if (!WorldStateManager.Instance.HasKey(wsmKey)) return false;

        switch (valueType)
        {
            case QuestValueType.Bool:
                bool b = WorldStateManager.Instance.GetBool(wsmKey);
                return comparison == QuestComparison.Equals    ? b == expectedBool
                     : comparison == QuestComparison.NotEquals ? b != expectedBool
                     : false;

            case QuestValueType.Int:
                int i = WorldStateManager.Instance.GetInt(wsmKey);
                return CompareInt(i, expectedInt, comparison);

            case QuestValueType.Float:
                float f = WorldStateManager.Instance.GetFloat(wsmKey);
                return CompareFloat(f, expectedFloat, comparison);

            case QuestValueType.String:
                string s = WorldStateManager.Instance.GetString(wsmKey);
                return comparison == QuestComparison.Equals    ?
                           string.Equals(s, expectedString, StringComparison.OrdinalIgnoreCase)
                     : comparison == QuestComparison.NotEquals ?
                           !string.Equals(s, expectedString, StringComparison.OrdinalIgnoreCase)
                     : false;

            default: return false;
        }
    }

    private static bool CompareInt(int actual, int expected, QuestComparison cmp) => cmp switch
    {
        QuestComparison.Equals         => actual == expected,
        QuestComparison.NotEquals      => actual != expected,
        QuestComparison.GreaterThan    => actual >  expected,
        QuestComparison.GreaterOrEqual => actual >= expected,
        QuestComparison.LessThan       => actual <  expected,
        QuestComparison.LessOrEqual    => actual <= expected,
        _                              => false,
    };

    private static bool CompareFloat(float actual, float expected, QuestComparison cmp) => cmp switch
    {
        QuestComparison.Equals         => Mathf.Abs(actual - expected) <= FloatEpsilon,
        QuestComparison.NotEquals      => Mathf.Abs(actual - expected) >  FloatEpsilon,
        QuestComparison.GreaterThan    => actual >  expected,
        QuestComparison.GreaterOrEqual => actual >= expected,
        QuestComparison.LessThan       => actual <  expected,
        QuestComparison.LessOrEqual    => actual <= expected,
        _                              => false,
    };
}
