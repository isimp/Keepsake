using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Keepsake.UI
{
    /// <summary>A pooled row: its own root plus whatever widgets the list's create function gave it.</summary>
    public abstract class RowView
    {
        public RectTransform Rect;
    }

    /// <summary>
    /// A scroll list that only has rows for what is on screen. The content is sized for every item
    /// at a fixed row height, and a pool of rows, just enough to fill the view, moves along with
    /// the scroll and is given the items that are showing. So thirteen thousand settings cost the
    /// same as thirty, and a redraw only changes the text of the rows already there.
    /// </summary>
    public sealed class VirtualList<TItem, TView> where TView : RowView
    {
        private readonly ScrollRect _scroll;
        private readonly RectTransform _content;
        private readonly float _rowHeight;
        private readonly float _padding;
        private readonly Func<RectTransform, TView> _create;
        private readonly Action<TView, TItem> _bind;
        private readonly List<TView> _pool = new List<TView>();

        private IList<TItem> _items = new TItem[0];
        private int _first = -1;

        public VirtualList(ScrollRect scroll, float rowHeight, float padding,
            Func<RectTransform, TView> create, Action<TView, TItem> bind)
        {
            _scroll = scroll;
            _content = scroll.content;
            _rowHeight = rowHeight;
            _padding = padding;
            _create = create;
            _bind = bind;

            // Rows are placed by hand, so nothing may lay the content out on top of that.
            var layout = _content.GetComponent<LayoutGroup>();
            if (layout != null) UnityEngine.Object.DestroyImmediate(layout);
            var fitter = _content.GetComponent<ContentSizeFitter>();
            if (fitter != null) UnityEngine.Object.DestroyImmediate(fitter);

            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(0f, _padding * 2f);

            _scroll.onValueChanged.AddListener(_ => Refresh(false));
        }

        /// <summary>Shows a new set of items, from the top or where the view already was.</summary>
        public void SetItems(IList<TItem> items, bool keepPosition)
        {
            _items = items ?? new TItem[0];
            _content.sizeDelta = new Vector2(0f, _items.Count * _rowHeight + _padding * 2f);

            if (!keepPosition) _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
            Refresh(true);
        }

        /// <summary>Gives the rows on screen their items again, after something they show changed.</summary>
        public void Rebind() => Refresh(true);

        private void Refresh(bool rebind)
        {
            var viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;
            var height = viewport.rect.height;

            var scrolled = Mathf.Max(0f, _content.anchoredPosition.y - _padding);
            var first = Mathf.Max(0, Mathf.FloorToInt(scrolled / _rowHeight));
            var needed = Mathf.CeilToInt(height / _rowHeight) + 2;

            if (!rebind && first == _first) return;
            _first = first;

            while (_pool.Count < needed) _pool.Add(Create());

            for (var i = 0; i < _pool.Count; i++)
            {
                var view = _pool[i];
                var index = first + i;

                if (i >= needed || index >= _items.Count)
                {
                    if (view.Rect.gameObject.activeSelf) view.Rect.gameObject.SetActive(false);
                    continue;
                }

                if (!view.Rect.gameObject.activeSelf) view.Rect.gameObject.SetActive(true);
                view.Rect.anchoredPosition = new Vector2(0f, -(_padding + index * _rowHeight));
                _bind(view, _items[index]);
            }
        }

        private TView Create()
        {
            var go = new GameObject("row", typeof(RectTransform));
            go.transform.SetParent(_content, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-_padding * 2f, _rowHeight);

            var view = _create(rect);
            view.Rect = rect;
            return view;
        }
    }
}
