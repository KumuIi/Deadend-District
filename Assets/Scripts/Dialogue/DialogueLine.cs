using System;
using UnityEngine;

/// <summary>
/// One spoken line: who says it, an optional portrait, the text, and optional WSM writes that fire
/// when the line is SHOWN. Keep <see cref="writesOnShow"/> for low-risk facts only (e.g. "seen intro")
/// — put quest-critical writes on a <see cref="DialogueChoice"/> (writesOnPick) so they commit on an
/// explicit player action, never just by reading a line.
/// </summary>
[Serializable]
public class DialogueLine
{
    public string speakerName;
    public Sprite portrait;
    [TextArea(2, 5)] public string text;

    [Tooltip("Advanced: WSM facts written when this line appears. Prefer choice writes for quest state.")]
    public DialogueWrite[] writesOnShow;
}
