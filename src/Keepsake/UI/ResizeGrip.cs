using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Keepsake.UI
{
    /// <summary>
    /// Corner handle that resizes a centre-pivoted panel while keeping its top-left corner still,
    /// so the window grows towards the corner you are dragging rather than in both directions.
    /// </summary>
    public class ResizeGrip : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        public RectTransform Target;
        public Vector2 MinSize = new Vector2(900f, 520f);
        public Action<Vector2> Resized;

        public void OnDrag(PointerEventData eventData)
        {
            if (Target == null) return;

            var canvas = Target.GetComponentInParent<Canvas>();
            var scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            var delta = eventData.delta / scale;

            var size = Target.sizeDelta + new Vector2(delta.x, -delta.y);
            size.x = Mathf.Clamp(size.x, MinSize.x, Screen.width);
            size.y = Mathf.Clamp(size.y, MinSize.y, Screen.height);

            var applied = size - Target.sizeDelta;
            Target.sizeDelta = size;
            Target.anchoredPosition += new Vector2(applied.x / 2f, -applied.y / 2f);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Target != null) Resized?.Invoke(Target.sizeDelta);
        }
    }
}
