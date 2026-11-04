using UnityEngine;
using UnityEngine.UI;

namespace FourPlayers;

/// Turns a fixed-height VerticalLayoutGroup list (the lobby's PlayerList: 459px, laid out for 3 rows of 150px)
/// into a scrollable list, so any number of rows fit at full size:
///   [Scroll: ScrollRect, takes the list's place]  ->  [Viewport: RectMask2D]  ->  original list (+ContentSizeFitter)
///   [Scrollbar] on the right edge, hidden when everything fits.
internal static class ScrollList
{
    private const string ScrollName = "FourPlayers_Scroll";
    private const float BarWidth = 8f;

    /// Wrap `list` once. Returns the ScrollRect (existing or new).
    internal static ScrollRect Ensure(RectTransform list)
    {
        var viewport = list.parent?.TryCast<RectTransform>();
        if (viewport != null && viewport.name == "Viewport" && viewport.parent != null && viewport.parent.name == ScrollName)
            return viewport.parent.GetComponent<ScrollRect>();

        var parent = list.parent;
        int sibling = list.GetSiblingIndex();

        // Scroll root: exactly where the list was.
        var scrollGo = new GameObject(ScrollName);
        scrollGo.layer = list.gameObject.layer;
        var scroll = scrollGo.AddComponent<RectTransform>();
        scroll.SetParent(parent, false);
        scroll.SetSiblingIndex(sibling);
        scroll.anchorMin = list.anchorMin; scroll.anchorMax = list.anchorMax; scroll.pivot = list.pivot;
        scroll.anchoredPosition = list.anchoredPosition; scroll.sizeDelta = list.sizeDelta;
        scroll.localScale = list.localScale;

        // Viewport: fills the scroll root, clips the rows.
        var vpGo = new GameObject("Viewport");
        vpGo.layer = list.gameObject.layer;
        var vp = vpGo.AddComponent<RectTransform>();
        vp.SetParent(scroll, false);
        vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one; vp.pivot = new Vector2(0.5f, 1f);
        vp.offsetMin = Vector2.zero; vp.offsetMax = Vector2.zero;
        vpGo.AddComponent<RectMask2D>();
        // Transparent hit area so the mouse wheel / drag works between rows too.
        var hit = vpGo.AddComponent<Image>();
        hit.color = new Color(0, 0, 0, 0);

        // The original list becomes the scrolled content: top-anchored, full width, height = its rows.
        float width = list.rect.width;
        list.SetParent(vp, false);
        list.anchorMin = new Vector2(0.5f, 1f); list.anchorMax = new Vector2(0.5f, 1f); list.pivot = new Vector2(0.5f, 1f);
        list.anchoredPosition = Vector2.zero;
        list.sizeDelta = new Vector2(width, list.sizeDelta.y);
        var fitter = list.GetComponent<ContentSizeFitter>() ?? list.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var sr = scrollGo.AddComponent<ScrollRect>();
        sr.content = list;
        sr.viewport = vp;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;
        sr.verticalScrollbar = CreateScrollbar(scroll);
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        LayoutRebuilder.ForceRebuildLayoutImmediate(list);
        Plugin.Log.LogInfo($"ScrollList: wrapped '{list.name}' ({width:0}x{scroll.rect.height:0} viewport)");
        return sr;
    }

    private static Scrollbar CreateScrollbar(RectTransform scroll)
    {
        var go = new GameObject("Scrollbar");
        go.layer = scroll.gameObject.layer;
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(scroll, false);
        // Just outside the right edge of the list so it doesn't cover the rows' dropdown arrows.
        rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(2f, 0f); rt.sizeDelta = new Vector2(BarWidth, 0f);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.15f);

        var area = new GameObject("Sliding Area").AddComponent<RectTransform>();
        area.gameObject.layer = go.layer;
        area.SetParent(rt, false);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = Vector2.zero; area.offsetMax = Vector2.zero;

        var handle = new GameObject("Handle").AddComponent<RectTransform>();
        handle.gameObject.layer = go.layer;
        handle.SetParent(area, false);
        handle.offsetMin = Vector2.zero; handle.offsetMax = Vector2.zero;
        var himg = handle.gameObject.AddComponent<Image>();
        himg.color = new Color(1f, 1f, 1f, 0.85f);

        var sb = go.AddComponent<Scrollbar>();
        sb.handleRect = handle;
        sb.targetGraphic = himg;
        sb.direction = Scrollbar.Direction.BottomToTop;
        return sb;
    }

    /// Scroll so `row` is fully visible.
    /// Returns false while the layout isn't ready yet (content not taller than the viewport), so callers retry.
    internal static bool Reveal(ScrollRect sr, RectTransform row)
    {
        if (sr == null || row == null) return false;
        var content = sr.content;
        var vp = sr.viewport;
        float overflow = content.rect.height - vp.rect.height;
        if (overflow <= 0.5f) return false;
        float rowTop = -row.anchoredPosition.y - row.rect.height * (1f - row.pivot.y);
        float rowBottom = rowTop + row.rect.height;
        float viewTop = (1f - sr.verticalNormalizedPosition) * overflow;
        float viewBottom = viewTop + vp.rect.height;
        if (rowTop >= viewTop - 0.5f && rowBottom <= viewBottom + 0.5f) return true;
        float targetTop = rowBottom > viewBottom ? rowBottom - vp.rect.height : rowTop;
        sr.verticalNormalizedPosition = 1f - Mathf.Clamp01(targetTop / overflow);
        Plugin.Log.LogInfo($"ScrollList: revealed '{row.name}'");
        return true;
    }
}
