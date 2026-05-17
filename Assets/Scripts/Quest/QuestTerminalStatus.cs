/// <summary>
/// Valid terminal states for a QuestOutcomeDefinition.
/// Restricts designers from accidentally picking Active or Inactive as an outcome result.
/// </summary>
public enum QuestTerminalStatus { Succeeded, Failed, Expired }
