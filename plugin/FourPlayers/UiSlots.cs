using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KITS.Framework;
using KITS.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FourPlayers;

/// Per-player UI rows are serialized arrays with exactly 3 entries in the prefabs. Without hooks we can't get in
/// before each owner's Initialise, so instead: find live instances each tick, clone the last entry next to it,
/// grow the array, then do the per-entry setup the owner's Initialise would have done.
internal static class UiSlots
{
    private static bool _inc = true;
    private static float _nextTick, _nextMap;
    private static int _statsId;
    private static Il2CppArrayBase<LegacyUISessionPanel> _statsPanels;

    /// Scene-wide FindObjectsByType costs 15-25ms in a fight (it walks every loaded object, active or not); doing 12
    /// of those 4x a second cut the game to ~10-30 fps. Instead, reach every per-player panel through the game's own
    /// references: each panel hangs off a PersistentSystem (e.g. GameUISystem._gameUIController._playerVoteDisplays).
    /// That is a few dozen field reads, so it can run every 0.25s; the fixes are idempotent (an array-length check
    /// when there's nothing to do), and a panel the game re-initialises back to 3 entries is fixed again right away.
    /// Map location icons (hundreds) are only walked while the map is the current screen, twice a second.
    internal static void Tick()
    {
        float now = Time.unscaledTime;
        if (now < _nextTick) return;
        _nextTick = now + 0.25f;

        var lobby = PersistentSystem<LobbyUISystem>.Instance?._lobbyUIController;
        if (lobby != null) EnsureLobby(lobby);

        var game = PersistentSystem<GameUISystem>.Instance;
        if (game != null)
        {
            Safe(game._partyQuickViewUIController, FixPartyQuickView);
            var gc = game._gameUIController;
            if (gc != null) Each(gc._playerVoteDisplays, FixPips);       // EndTurn + Rollback pips
        }

        var mapUi = PersistentSystem<FITS2.UI.MapUISystem>.Instance;
        if (mapUi != null)
        {
            var mc = mapUi._mapUIController;
            if (mc != null) { Safe(mc._playerVoteDisplay, FixPips); Safe(mc._partyQuickViewUIController, FixPartyQuickView); }
            var ui = PersistentSystem<UIStateSystem>.Instance;
            if (ui != null && ui.CurrentState == UIStateSystem.EUIState.Map && now >= _nextMap)
            {
                _nextMap = now + 0.5f;
                var layers = mapUi._mapController?._mapLayers;
                if (layers != null)
                    foreach (var layer in layers)
                    {
                        var locs = layer?._mapLocationControllers;
                        if (locs == null) continue;
                        foreach (var loc in locs)
                            if (loc != null) Each(loc._mapLocationIconControllers, FixMapIcon);
                    }
            }
        }

        var inter = PersistentSystem<InteractionUISystem>.Instance?._controller;
        if (inter != null) { Each(inter._choiceButtons, FixChoice); Safe(inter._defaultChoiceButton, FixChoice); Safe(inter._voteDisplay, FixPips); }
        var levelInter = PersistentSystem<LevelInteractionUISystem>.Instance?._controller;
        if (levelInter != null) { Each(levelInter._choiceButtons, FixChoice); Safe(levelInter._defaultChoiceButton, FixChoice); }

        Safe(PersistentSystem<FITS2.UI.MedicalUISystem>.Instance?._controller?._partyQuickViewUIController, FixPartyQuickView);
        Safe(PersistentSystem<TavernUISystem>.Instance?._controller, FixTavern);
        var inv = PersistentSystem<InventoryUISystem>.Instance?._controller?._partyViewController;
        if (inv != null) { FixInventory(inv); Each(inv._partyCharacterPanels, FixFrameSprites); }
        var stats = PersistentSystem<SessionStatsUISystem>.Instance?._sessionStatsUIController;
        if (stats != null)
        {
            FixSessionStats(stats);
            // The stats screen also carries a LegacyUISessionPanel (on SessionPanel) that no field points to:
            // look it up once in the screen's own (small) subtree and remember it.
            int id = stats.GetInstanceID();
            if (id != _statsId) { _statsId = id; _statsPanels = stats.GetComponentsInChildren<LegacyUISessionPanel>(true); }
            if (_statsPanels != null) foreach (var lp in _statsPanels) if (lp != null) FixLegacySession(lp);
        }
        Safe(PersistentSystem<LegacyUISystem>.Instance?._legacyUIController?._sessionPanel, FixLegacySession);
        Safe(PersistentSystem<RecruitUISystem>.Instance?._controller?._partyDismissal, FixPartyDismissal);
        var death = PersistentSystem<FITS2.UI.PostDeathRollbackUISystem>.Instance?._uiController;
        if (death != null) { Safe(death._rollbackAcceptVotes, FixVoteIcons); Safe(death._rollbackDeclineVotes, FixVoteIcons); }
    }

    private static void Safe<T>(T c, Action<T> fix) where T : Component
    {
        if (c != null) fix(c);
    }

    private static void Each<T>(Il2CppReferenceArray<T> arr, Action<T> fix) where T : Component
    {
        if (arr == null) return;
        foreach (var c in arr) if (c != null) fix(c);
    }

    /// "slotscan" command: the old scene-wide search, to check nothing is missed by the reference walk above.
    internal static void FullScan()
    {
        var found = new System.Text.StringBuilder();
        void Scan<T>(Action<T> fix) where T : Component
        {
            int before = _extended;
            foreach (var c in Find<T>()) fix(c);
            if (_extended > before) found.Append($" {typeof(T).Name}x{_extended - before}");
        }
        Scan<LobbyUIController>(EnsureLobby);
        Scan<FITS2.UI.Map.MapLocationIconController>(FixMapIcon);
        Scan<ChoiceButton>(FixChoice);
        Scan<FITS2.UI.PlayerVoteDisplay>(FixPips);
        Scan<PartyQuickViewUIController>(FixPartyQuickView);
        Scan<TavernUIController>(FixTavern);
        Scan<InventoryPartyViewController>(FixInventory);
        Scan<SessionStatsUIController>(FixSessionStats);
        Scan<LegacyUISessionPanel>(FixLegacySession);
        Scan<PartyDismissalUIController>(FixPartyDismissal);
        Scan<PartyCharacterPanel>(FixFrameSprites);
        Scan<FITS2.UI.PlayerVoteIconDisplay>(FixVoteIcons);
        Plugin.Log.LogInfo("slotscan: missed by the reference walk:" + (found.Length == 0 ? " none" : found.ToString()));
    }

    // Map mission icons: 3 fixed PlayerIcons packed into _playerVotingIcons by Initialise();
    // SetVote(playerIndex, ...) indexes it -> IndexOutOfRange for player 4's mission vote.
    private static void FixMapIcon(FITS2.UI.Map.MapLocationIconController c)
    {
        var a = c._playerVotingIcons;
        if (Extend(ref a, "MapLocationIcon votes", out _, quiet: true)) c._playerVotingIcons = a;
    }

    // Event/interaction choices: same pattern.
    private static void FixChoice(ChoiceButton c)
    {
        var a = c._playerVotingIcons;
        if (Extend(ref a, "ChoiceButton votes", out _, quiet: true)) c._playerVotingIcons = a;
    }

    // Vote pips (EndTurn = bottom-right "ready" indicator, Rollback, SelectMapLocation, EventOption): 3 pips.
    private static void FixPips(FITS2.UI.PlayerVoteDisplay c)
    {
        var a = c._pips;
        if (Extend(ref a, $"PlayerVoteDisplay({c._voteType}) pips", out _)) c._pips = a;
        FitRow(c._pips, $"PlayerVoteDisplay({c._voteType}) pips");
    }

    // In-fight / map party HUD (top left): 3 CharacterQuickViews; Initialise() inits each, SetUpForParty() fills them.
    private static void FixPartyQuickView(PartyQuickViewUIController c)
    {
        var a = c._characterQuickViews;
        if (!Extend(ref a, "PartyQuickView", out int added)) return;
        c._characterQuickViews = a;
        for (int i = a.Length - added; i < a.Length; i++)
            try { a[i].Initialise(i); } catch (Exception ex) { Plugin.Log.LogWarning($"CharacterQuickView[{i}].Initialise: {ex.Message}"); }
        if (c.gameObject.activeInHierarchy) try { c.SetUpForParty(); } catch (Exception ex) { Plugin.Log.LogWarning($"PartyQuickView.SetUpForParty: {ex.Message}"); }
    }

    private static void FixTavern(TavernUIController c)
    {
        var a = c._characterQuickViews;
        if (!Extend(ref a, "Tavern quick views", out _)) return;
        c._characterQuickViews = a;
        if (c.gameObject.activeInHierarchy) try { c.UpdatePartyViews(); } catch (Exception ex) { Plugin.Log.LogWarning($"Tavern.UpdatePartyViews: {ex.Message}"); }
    }

    private static void FixInventory(InventoryPartyViewController c)
    {
        var a = c._partyCharacterPanels;
        if (!Extend(ref a, "Inventory party panels", out _)) return;
        c._partyCharacterPanels = a;
        if (c.gameObject.activeInHierarchy) try { c.SetUpForParty(); } catch (Exception ex) { Plugin.Log.LogWarning($"Inventory.SetUpForParty: {ex.Message}"); }
    }

    // End-of-run stats and legacy session screens.
    private static void FixSessionStats(SessionStatsUIController c)
    {
        var a = c._characterDisplays;
        if (Extend(ref a, "SessionStats displays", out _)) c._characterDisplays = a;
    }

    private static void FixLegacySession(LegacyUISessionPanel c)
    {
        var a = c._characterDisplays;
        if (Extend(ref a, "LegacySession displays", out _, quiet: true)) c._characterDisplays = a;
    }

    // Party dismissal: loops all party characters but has 3 cards -> IndexOutOfRange for player 4.
    private static void FixPartyDismissal(PartyDismissalUIController c)
    {
        var a = c._partyCards;
        if (Extend(ref a, "PartyDismissal cards", out _)) c._partyCards = a;
    }

    // Party panel frame sprite is picked by player index: _frameSprites[playerIndex], 3 sprites.
    private static void FixFrameSprites(PartyCharacterPanel c)
    {
        var s = c._frameSprites;
        int want = Plugin.MaxPlayers.Value;
        if (s == null || s.Length == 0 || s.Length >= want) return;
        var grown = new Il2CppReferenceArray<Sprite>(want);
        for (int i = 0; i < want; i++) grown[i] = s[Math.Min(i, s.Length - 1)];
        c._frameSprites = grown;
    }

    // Vote icons (PostDeathRollbackOption): 3 icons.
    private static void FixVoteIcons(FITS2.UI.PlayerVoteIconDisplay c)
    {
        var a = c._icons;
        if (Extend(ref a, $"PlayerVoteIconDisplay({c._voteType}) icons", out _)) c._icons = a;
        FitRow(c._icons, $"PlayerVoteIconDisplay({c._voteType}) icons");
    }

    private static readonly System.Collections.Generic.Dictionary<int, Vector2> _rowOrigSize = new();

    /// Vote pip rows are fixed-width boxes sized for the game's 3 players (e.g. 80px for 20px pips). Past 4 the
    /// clones spill out of the box (over the Rollback icon). Shrink every entry so the row fits its box again;
    /// rows that already fit (4 players) are left at the prefab size.
    private static void FitRow<T>(Il2CppReferenceArray<T> row, string label) where T : Component
    {
        if (row == null || row.Length == 0 || row[0] == null) return;
        var layout = row[0].transform.parent?.TryCast<RectTransform>();
        var first = row[0].transform.TryCast<RectTransform>();
        if (layout == null || first == null || layout.GetComponent<HorizontalLayoutGroup>() == null) return;
        int key = first.GetInstanceID();
        if (!_rowOrigSize.TryGetValue(key, out var orig)) _rowOrigSize[key] = orig = first.sizeDelta;
        float width = layout.rect.width;
        if (width <= 0 || orig.x <= 0) return;
        var hlg = layout.GetComponent<HorizontalLayoutGroup>();
        // Size for the entries actually shown: with the cap at 6 the game hides pips of players who aren't there.
        int shown = 0;
        for (int i = 0; i < row.Length; i++) if (row[i] != null && row[i].gameObject.activeSelf) shown++;
        if (shown == 0) return;
        float avail = width - hlg.padding.left - hlg.padding.right - hlg.spacing * (shown - 1);
        float f = Mathf.Min(1f, avail / (orig.x * shown));
        var want = orig * f;
        if (Mathf.Abs(first.sizeDelta.x - want.x) < 0.1f) return;
        for (int i = 0; i < row.Length; i++)
            if (row[i] != null) row[i].transform.TryCast<RectTransform>().sizeDelta = want;
        Plugin.Log.LogInfo($"{label}: {shown} shown x {orig.x:0}px in {width:0}px -> {want.x:0.#}px each");
    }

    private static int _quietCount, _extended;

    // perf: time spent finding each component type (the scene-wide search is the expensive part).
    internal static readonly System.Collections.Generic.Dictionary<string, double> FindMs = new();
    private static Il2CppArrayBase<T> Find<T>() where T : Object
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var r = Object.FindObjectsByType<T>(_inc ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        string k = (_inc ? "full " : "quick ") + typeof(T).Name;
        FindMs.TryGetValue(k, out double old); FindMs[k] = old + ms;
        return r;
    }

    // LobbyUIController.Initialise: for each row -> SetActive + LobbyPlayerEntry.ListArchetypes.
    private static void EnsureLobby(LobbyUIController c)
    {
        var arr = c._playerEntries;
        if (arr != null && arr.Length >= Plugin.MaxPlayers.Value) { ScrollLobby(c, arr); return; }
        if (!Extend(ref arr, "LobbyPlayerEntry", out int added)) return;
        c._playerEntries = arr;
        for (int i = arr.Length - added; i < arr.Length; i++)
        {
            var e = arr[i];
            try { e.Initialise(); } catch (Exception ex) { Plugin.Log.LogWarning($"LobbyPlayerEntry[{i}].Initialise: {ex.Message}"); }
            try { e.ListArchetypes(); } catch (Exception ex) { Plugin.Log.LogWarning($"LobbyPlayerEntry[{i}].ListArchetypes: {ex.Message}"); }
        }
        if (c.gameObject.activeInHierarchy)
        {
            c.RefreshPlayerList();
            c.RefreshReadyState();
        }
    }

    private static int _revealedFor = -1;

    /// PlayerList was laid out for 3 rows; with more, wrap it in a scroll list (rows stay full size) and make sure the
    /// local player's own row (with their archetype dropdown) is in view.
    private static void ScrollLobby(LobbyUIController c, Il2CppReferenceArray<LobbyPlayerEntry> rows)
    {
        var list = rows[0].transform.parent?.TryCast<RectTransform>();
        if (list == null) return;
        var sr = ScrollList.Ensure(list);
        var me = KITS.Framework.PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance?.LocalPlayerInstance;
        if (me == null || sr == null) return;
        int idx = me.PlayerIndex;
        if (idx == _revealedFor || idx >= rows.Length) return;
        if (ScrollList.Reveal(sr, rows[idx].transform.TryCast<RectTransform>())) _revealedFor = idx;
    }

    /// Grow a component array to MaxPlayers by cloning its last element next to it (same parent, next sibling).
    internal static bool Extend<T>(ref Il2CppReferenceArray<T> arr, string label, out int added, bool quiet = false) where T : Component
    {
        added = 0;
        int want = Plugin.MaxPlayers.Value;
        if (arr == null || arr.Length == 0 || arr.Length >= want) return false;
        var last = arr[arr.Length - 1];
        if (last == null) return false;

        var grown = new Il2CppReferenceArray<T>(want);
        for (int i = 0; i < arr.Length; i++) grown[i] = arr[i];

        var prev = arr.Length > 1 ? arr[arr.Length - 2] : null;
        var parent = last.transform.parent;
        bool autoLayout = parent != null && parent.GetComponent<LayoutGroup>() != null;
        for (int i = arr.Length; i < want; i++)
        {
            var go = Object.Instantiate(last.gameObject, parent, false);
            go.name = $"{last.gameObject.name.Split(' ')[0]} ({i}) [FourPlayers]";
            go.transform.SetSiblingIndex(grown[i - 1].transform.GetSiblingIndex() + 1);
            if (!autoLayout && prev != null)
            {
                var step = last.transform.localPosition - prev.transform.localPosition;
                go.transform.localPosition = last.transform.localPosition + step * (i - arr.Length + 1);
            }
            grown[i] = go.GetComponent<T>();
            added++;
        }
        // Map icons exist once per map location; log the first few only.
        if (!quiet || ++_quietCount <= 3) Plugin.Log.LogInfo($"{label}: {arr.Length} -> {want} (parent '{parent?.name}', layoutGroup={autoLayout})");
        arr = grown;
        _extended++;
        return true;
    }
}
