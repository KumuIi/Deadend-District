#nullable enable
using System.Collections.Generic;
using UnityEngine;
using FracturedProtocol.Combat.Stats;

namespace FracturedProtocol.Combat.Items
{
    /// <summary>Defines an attachment that occupies one slot and applies stat modifiers.</summary>
    [CreateAssetMenu(fileName = "New_Attachment", menuName = "Items/Attachment")]
    public sealed class AttachmentSO : ItemSO
    {
        public AttachmentSlotType slotType;
        public List<StatModifier> modifiers = new List<StatModifier>();
    }
}
