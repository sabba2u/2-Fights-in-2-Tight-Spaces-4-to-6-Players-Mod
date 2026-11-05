using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FourPlayers;

/// A red warning strip at the top of the screen, on its own overlay canvas above the game's UI.
/// Built on first use (so it costs nothing while there's nothing to show) and kept across scene loads.
internal static class Banner
{
    private static GameObject _root;
    private static TextMeshProUGUI _text;
    private static string _shown;

    internal static void Show(string message)
    {
        if (message == _shown && (_root != null || message == null)) return;
        if (message == null) { if (_root != null) _root.SetActive(false); _shown = null; return; }
        if (_root == null && !Build()) return;
        _text.text = message;
        _root.SetActive(true);
        _shown = message;
    }

    private static bool Build()
    {
        var font = TMP_Settings.defaultFontAsset;
        if (font == null)
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();   // one-off, only when a warning is due
            if (fonts.Length == 0) return false;                              // fonts not loaded yet: retry next tick
            font = fonts[0];
        }

        _root = new GameObject("FourPlayers.Banner");
        Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var bg = new GameObject("Background");
        bg.transform.SetParent(_root.transform, false);
        var bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0.2f, 1f);
        bgRt.anchorMax = new Vector2(0.8f, 1f);
        bgRt.pivot = new Vector2(0.5f, 1f);
        bgRt.anchoredPosition = new Vector2(0, -120);  // below the turn bar and the objective line
        var img = bg.AddComponent<Image>();
        img.color = new Color(0.55f, 0.05f, 0.05f, 0.92f);
        img.raycastTarget = false;
        var fit = bg.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var layout = bg.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 10, 10);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        var txt = new GameObject("Text");
        txt.transform.SetParent(bg.transform, false);
        txt.AddComponent<RectTransform>();
        _text = txt.AddComponent<TextMeshProUGUI>();
        _text.font = font;
        _text.fontSize = 24;
        _text.color = Color.white;
        _text.alignment = TextAlignmentOptions.Center;
        _text.enableWordWrapping = true;
        _text.raycastTarget = false;
        return true;
    }
}
