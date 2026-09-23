using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Keepsake.UI
{
    /// <summary>
    /// The panel's drawing primitives: Jotunn widgets wrapped so the rest of the panel can say
    /// what it wants rather than how to build it.
    /// </summary>
    public static partial class KeepsakePanel
    {
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color Kept = new Color(1f, 0.75f, 0.38f);
        private static readonly Color Problem = new Color(1f, 0.45f, 0.4f);
        private static readonly Color Selected = new Color(1f, 0.7f, 0.2f, 0.32f);
        private static readonly Color Clearish = new Color(0f, 0f, 0f, 0.01f);
        private static readonly Color KeptRow = new Color(1f, 0.7f, 0.2f, 0.10f);
        private static readonly Color DefaultBar = new Color(0.44f, 0.69f, 1f, 0.9f);

        private static GameObject Label(string text, Transform parent, float width, float height, int fontSize, Color color, bool bold = false)
        {
            var go = GUIManager.Instance.CreateText(text, parent,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                bold ? GUIManager.Instance.AveriaSerifBold : GUIManager.Instance.AveriaSerif,
                fontSize, color, true, Color.black, width, height, false);

            var label = go.GetComponent<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return go;
        }

        /// <summary>Text that grows downwards instead of being cut off.</summary>
        private static GameObject Wrapped(string text, Transform parent, float width, int fontSize, Color color, bool bold = false)
        {
            var go = GUIManager.Instance.CreateText(text, parent,
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero,
                bold ? GUIManager.Instance.AveriaSerifBold : GUIManager.Instance.AveriaSerif,
                fontSize, color, true, Color.black, width, 20f, false);

            var label = go.GetComponent<Text>();
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            // No preferred height: the fitter derives it from the wrapped text.
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;

            var fitter = go.GetComponent<ContentSizeFitter>() ?? go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        private static GameObject Button(string text, Transform parent, float width, float height, UnityEngine.Events.UnityAction onClick)
        {
            var go = GUIManager.Instance.CreateButton(text, parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, width, height);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go;
        }

        /// <summary>A button that keeps its size inside a layout group, which would otherwise resize it.</summary>
        private static void FixedButton(string text, Transform parent, float width, float height, UnityEngine.Events.UnityAction onClick) =>
            Fix(Button(text, parent, width, height, onClick), width, height);

        /// <summary>A row of buttons in the detail column.</summary>
        private static Transform ButtonRow(float height = 34f)
        {
            var row = new GameObject("buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(_detail, false);
            Fix(row, DetailInner, height);

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 10f;
            return row.transform;
        }

        private static void Spacer(float height)
        {
            var go = new GameObject("spacer", typeof(RectTransform));
            go.transform.SetParent(_detail, false);
            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }

        /// <summary>Creates a scroll view. Its content is laid out by whoever fills it.</summary>
        private static ScrollRect MakeScrollView(float width, float height, Vector2 position)
        {
            var scroll = GUIManager.Instance.CreateScrollView(
                _root.transform, false, true, 8f, 10f, GUIManager.Instance.ValheimScrollbarHandleColorBlock,
                new Color(0f, 0f, 0f, 0.25f), width, height);
            Anchor(scroll, new Vector2(0f, 1f), position);

            // Jotunn hands back a container; the ScrollRect itself sits on a child of it.
            var scrollRect = scroll.GetComponent<ScrollRect>() ?? scroll.GetComponentInChildren<ScrollRect>(true);
            if (scrollRect == null)
            {
                Plugin.Log.LogError("Keepsake: scroll view has no ScrollRect.");
                return null;
            }

            scrollRect.scrollSensitivity = 300f;
            return scrollRect;
        }

        /// <summary>Lays a scroll view's content out top to bottom, each child as tall as it asks to be.</summary>
        private static RectTransform Stack(ScrollRect scrollRect, int padding, float spacing)
        {
            var content = scrollRect.content;

            var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;

            var fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return content;
        }

        private static void Fix(GameObject go, float width, float height)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.preferredHeight = height;
            element.minHeight = height;
        }

        /// <summary>Places a widget by its left edge, so the given x is where it starts.</summary>
        private static void AnchorLeft(GameObject go, float x, float y)
        {
            var rect = go.GetComponent<RectTransform>();
            var size = rect.rect.size;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>Places a widget by its right edge, so the given negative x is where it ends.</summary>
        private static void AnchorRight(GameObject go, float x, float y)
        {
            var rect = go.GetComponent<RectTransform>();
            var size = rect.rect.size;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(x, y);
        }

        private static void Anchor(GameObject go, Vector2 anchor, Vector2 position)
        {
            var rect = go.GetComponent<RectTransform>();
            var size = rect.rect.size;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void Clear(Transform parent)
        {
            // Unparented first, so the layout does not count rows that are only waiting to be destroyed.
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.transform.SetParent(null, false);
                Object.Destroy(child);
            }
        }
    }
}
