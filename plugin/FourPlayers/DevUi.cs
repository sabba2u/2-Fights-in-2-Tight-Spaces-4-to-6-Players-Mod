using System.IO;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FourPlayers;

/// Dev helpers driven from the command file: list/click UI buttons and take in-game screenshots,
/// so the game can be driven without focusing its window.
internal static class DevUi
{
    private static string Label(Component c)
    {
        var tmp = c.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) return tmp.text.Trim().Replace("\n", " ");
        var txt = c.GetComponentInChildren<Text>(true);
        return txt != null ? txt.text.Trim().Replace("\n", " ") : "";
    }

    private static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    private static Button[] ActiveButtons()
    {
        var all = Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return all;
    }

    internal static string ListButtons()
    {
        var sb = new StringBuilder();
        foreach (var b in ActiveButtons())
            if (b.isActiveAndEnabled)
                sb.AppendLine($"  [{(b.interactable ? "on " : "off")}] '{Label(b)}'  {PathOf(b.transform)}");
        return sb.ToString();
    }

    /// First active, interactable button whose label or object name contains the text, or whose path ends with it.
    internal static Button FindButton(string text)
    {
        string t = text.ToLowerInvariant();
        foreach (var b in ActiveButtons())
        {
            try
            {
                if (b == null || !b.isActiveAndEnabled || !b.interactable) continue;
                if (Label(b).ToLowerInvariant().Contains(t) || b.name.ToLowerInvariant().Contains(t) || PathOf(b.transform).ToLowerInvariant().EndsWith(t))
                    return b;
            }
            catch (System.Exception) { /* destroyed mid screen-transition; skip */ }
        }
        return null;
    }

    internal static void Press(Button b)
    {
        Plugin.Log.LogInfo($"Clicking '{Label(b)}' ({PathOf(b.transform)})");
        // Pop-up buttons act through PopUpButton.OnButtonPressed, not Button.onClick.
        var pb = b.GetComponent<KITS.PopUps.PopUpButton>();
        if (pb != null) pb.PressButton();
        else b.onClick.Invoke();
    }

    internal static void Click(string text)
    {
        var b = FindButton(text);
        if (b != null) Press(b);
        else Plugin.Log.LogWarning($"No active button matching '{text}'");
    }

    private static float _closeFollowUpUntil;

    /// Test copies start with a fresh profile, so every screen shows the first-play "view the tutorials?" pop-up.
    /// Press its "No Thanks", then "Close" on the "That's the spirit!" follow-up that appears right after.
    internal static void AutoSkipTutorial()
    {
        if (!Plugin.AutoSkipTutorialPrompt.Value) return;
        // The prompts it answers are front-end ones; don't pay for a scene-wide search during play.
        var st = KITS.Framework.PersistentSystem<KITS.UI.UIStateSystem>.Instance?.CurrentState.ToString();
        if (st is "Game" or "Map" or "Gym" or "Medical" or "BlackMarket" or "LevelComplete" or "LevelReward") return;
        foreach (var pb in Object.FindObjectsByType<KITS.PopUps.PopUpButton>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            string label = Label(pb).ToLowerInvariant();
            if (label == "no thanks")
            {
                Plugin.Log.LogInfo("AutoSkipTutorial: No Thanks");
                pb.PressButton();
                _closeFollowUpUntil = Time.unscaledTime + 5f;
                return;
            }
            // Test copies have no cloud login, so fetching coherence room regions times out with an error pop-up.
            if (label == "ok" && PopUpText(pb).Contains("room regions"))
            {
                Plugin.Log.LogInfo("AutoSkipTutorial: OK on 'error while fetching room regions'");
                pb.PressButton();
                return;
            }
            if (label == "close" && Time.unscaledTime < _closeFollowUpUntil)
            {
                Plugin.Log.LogInfo("AutoSkipTutorial: Close");
                pb.PressButton();
                _closeFollowUpUntil = 0;
                return;
            }
        }
    }

    /// All text in the pop-up a button belongs to (lower-case).
    private static string PopUpText(Component button)
    {
        var t = button.transform;
        while (t.parent != null && !t.name.StartsWith("PopUp")) t = t.parent;
        var sb = new StringBuilder();
        foreach (var tmp in t.GetComponentsInChildren<TMP_Text>(true)) sb.Append(tmp.text).Append(' ');
        return sb.ToString().ToLowerInvariant();
    }

    /// List active toggles, or set the first one whose label/name/path contains the text: "toggle <text> on|off".
    internal static void Toggle(string args)
    {
        var all = Object.FindObjectsByType<Toggle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (string.IsNullOrWhiteSpace(args))
        {
            var sb = new StringBuilder();
            foreach (var t in all)
                if (t.isActiveAndEnabled) sb.AppendLine($"  [{(t.isOn ? "x" : " ")}] '{Label(t)}'  {PathOf(t.transform)}");
            Plugin.Log.LogInfo("\n" + sb);
            return;
        }
        var parts = args.Trim().Split(' ');
        string state = parts[^1].ToLowerInvariant();
        bool? want = state == "on" ? true : state == "off" ? false : null;
        string text = (want.HasValue ? string.Join(" ", parts[..^1]) : args).ToLowerInvariant();
        foreach (var t in all)
        {
            if (!t.isActiveAndEnabled || !t.interactable) continue;
            if (Label(t).ToLowerInvariant().Contains(text) || PathOf(t.transform).ToLowerInvariant().Contains(text))
            {
                t.isOn = want ?? !t.isOn;
                Plugin.Log.LogInfo($"Toggle '{Label(t)}' ({PathOf(t.transform)}) -> {t.isOn}");
                return;
            }
        }
        Plugin.Log.LogWarning($"No active toggle matching '{text}'");
    }

    /// "tree <object name or path suffix> [depth]": print the hierarchy with rect sizes and layout components.
    internal static void Tree(string args)
    {
        var parts = args.Trim().Split(' ');
        int depth = 3;
        if (parts.Length > 1 && int.TryParse(parts[^1], out int d)) { depth = d; parts = parts[..^1]; }
        string key = string.Join(" ", parts).ToLowerInvariant();
        int shown = 0;
        foreach (var t in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.gameObject.scene.IsValid()) continue;
            string p = PathOf(t).ToLowerInvariant();
            if (t.name.ToLowerInvariant() == key || p.EndsWith(key))
            {
                var sb = new StringBuilder(PathOf(t)).AppendLine();
                Describe(t, sb, 0, depth);
                Plugin.Log.LogInfo("\n" + sb);
                if (++shown >= 8) return;   // every match: several screens reuse the same prefab names
            }
        }
        if (shown == 0) Plugin.Log.LogWarning($"tree: nothing matching '{key}'");
    }

    private static void Describe(Transform t, StringBuilder sb, int level, int max)
    {
        var rt = t.TryCast<RectTransform>();
        sb.Append(' ', level * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (inactive)");
        if (rt != null) sb.Append($"  size={rt.rect.width:0}x{rt.rect.height:0} pos={rt.anchoredPosition} pivot={rt.pivot} anchors={rt.anchorMin}-{rt.anchorMax} scale={rt.localScale.x:0.##}");
        var lg = t.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (lg != null) sb.Append($"  [{lg.GetIl2CppType().Name} spacing={lg.spacing} ctrlH={lg.childControlHeight} forceExpandH={lg.childForceExpandHeight} pad={lg.padding.top}/{lg.padding.bottom}]");
        var le = t.GetComponent<LayoutElement>();
        if (le != null) sb.Append($"  [LE pref={le.preferredHeight} min={le.minHeight} flex={le.flexibleHeight}]");
        if (t.GetComponent<ContentSizeFitter>() != null) sb.Append("  [Fitter]");
        if (t.GetComponent<Image>() != null) sb.Append("  [Image]");
        if (t.GetComponent<RectMask2D>() != null || t.GetComponent<Mask>() != null) sb.Append("  [Mask]");
        sb.AppendLine();
        if (level < max)
            for (int i = 0; i < t.childCount; i++) Describe(t.GetChild(i), sb, level + 1, max);
    }

    internal static void Shot(string file)
    {
        file ??= Path.Combine(Paths.BepInExRootPath, "shot.png");
        ScreenCapture.CaptureScreenshot(file, 1);
        Plugin.Log.LogInfo($"Screenshot -> {file}");
    }
}
