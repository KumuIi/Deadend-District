#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using FracturedProtocol.Combat.FireBehaviors;

namespace FracturedProtocol.Combat.Items
{
    /// <summary>
    /// Per-slot attachment whitelist on a weapon. Each slot has an explicit list
    /// of compatible attachments; anything not listed is rejected.
    /// </summary>
    [Serializable]
    public sealed class WeaponSlot
    {
        public AttachmentSlotType slotType;
        public List<AttachmentSO> compatibleAttachments = new List<AttachmentSO>();
    }

    /// <summary>
    /// Defines a weapon's base stats, accepted magazines, available attachment slots,
    /// and the fire behavior strategy. Never mutated at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "New_Weapon", menuName = "Items/Weapon")]
    public sealed class WeaponSO : ItemSO
    {
        public float fireRate = 600f;
        public float baseSpread = 1f;
        public AnimationCurve bloomCurve = AnimationCurve.Linear(0f, 1f, 10f, 3f);
        public Vector2 recoilPattern;
        public List<MagazineSO> acceptedMagazines = new List<MagazineSO>();
        public List<WeaponSlot> attachmentSlots = new List<WeaponSlot>();
        public FireBehaviorSO? fireBehavior;
        public AnimatorOverrideController? animatorOverride;
    }
}
