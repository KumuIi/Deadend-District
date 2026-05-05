#nullable enable
using UnityEngine;
using FracturedProtocol.Combat.Controllers;

namespace FracturedProtocol.Combat.UI
{
    /// <summary>
    /// Four-tick dynamic crosshair driven by WeaponController.SpreadChanged.
    /// Ticks move outward proportionally to current spread in degrees.
    /// Subscribe/unsubscribe in OnEnable/OnDisable to avoid leaks across scenes.
    /// </summary>
    public sealed class CrosshairUI : MonoBehaviour
    {
        [SerializeField] private RectTransform? _top;
        [SerializeField] private RectTransform? _bottom;
        [SerializeField] private RectTransform? _left;
        [SerializeField] private RectTransform? _right;

        [SerializeField] private WeaponController? _weaponController;

        /// <summary>Screen pixels per degree of spread.</summary>
        [SerializeField] private float _pixelsPerDegree = 50f;

        private void OnEnable()
        {
            if (_weaponController != null)
                _weaponController.SpreadChanged += OnSpreadChanged;
        }

        private void OnDisable()
        {
            if (_weaponController != null)
                _weaponController.SpreadChanged -= OnSpreadChanged;
        }

        private void OnSpreadChanged(float spread)
        {
            float offset = spread * _pixelsPerDegree;
            if (_top    != null) _top.localPosition    = new Vector3(0f,     offset, 0f);
            if (_bottom != null) _bottom.localPosition = new Vector3(0f,    -offset, 0f);
            if (_left   != null) _left.localPosition   = new Vector3(-offset, 0f,   0f);
            if (_right  != null) _right.localPosition  = new Vector3( offset, 0f,   0f);
        }
    }
}
