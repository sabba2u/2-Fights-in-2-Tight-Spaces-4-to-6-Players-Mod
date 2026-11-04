using FITS2.Map;
using FITS2.Network;
using KITS.Framework;
using KITS.UI;
using UnityEngine;

namespace FourPlayers;

/// Test copies: keep a session moving between rounds without someone clicking each window.
/// Debug.AutoMapVote: on the map, vote for the furthest selectable location (highest layer, lowest slot) if this copy
/// hasn't voted.
internal static class AutoPlay
{
    private static float _nextMapVote;

    /// Fresh test profiles show first-time help pages ("Cooperative Play", ...) at the start of a fight.
    /// Page through them with Next; on the last page Next is the Exit button.
    private static void SkipHelp()
    {
        if (!Plugin.AutoSkipTutorialPrompt.Value || Time.unscaledTime < _nextHelp) return;
        _nextHelp = Time.unscaledTime + 0.4f;
        // Through HelpUISystem rather than a scene-wide search (those cost 15-25ms each in a fight).
        var h = PersistentSystem<HelpUISystem>.Instance?._helpUIController;
        if (h == null || !h.gameObject.activeInHierarchy) return;
        Plugin.Log.LogInfo("AutoSkipHelp: Next");
        h.Input_Next();
    }

    private static float _nextHelp;

    private static float _nextContinue, _playerPhaseSince = -1f;

    /// Debug.AutoContinue: press Continue on Mission Complete (the visible page's Complete button).
    /// Debug.AutoWinFights (host): end each fight via LevelSystem.CompleteFight 5s into the player phase.
    private static void ContinueAndWin()
    {
        var ui = PersistentSystem<UIStateSystem>.Instance;
        if (ui == null) return;
        float now = Time.unscaledTime;
        if (Plugin.AutoContinue.Value && ui.CurrentState == UIStateSystem.EUIState.LevelComplete && now >= _nextContinue)
        {
            _nextContinue = now + 3f;
            // Multi-fight missions show one page per fight (only the last has a Continue button), so call the
            // controller's Complete() — what Continue does — instead of pressing a page's button.
            var c = PersistentSystem<LevelCompleteUISystem>.Instance?._levelCompleteController;
            if (c != null) { Plugin.Log.LogInfo("AutoContinue: LevelComplete.Complete"); c.Complete(); }
        }
        // IsGameAuthority throws before the network bridge exists, so check the state (and connection) first.
        if (Plugin.AutoWinFights.Value && ui.CurrentState == UIStateSystem.EUIState.Game &&
            PersistentSystem<NetworkManagerSystem>.Instance?.IsConnected == true && NetworkManagerSystem.IsGameAuthority)
        {
            var turns = PersistentSystem<KITS.Level.LevelTurnSystem>.Instance;
            bool playerPhase = turns != null && turns.IsPlayerPhase();
            if (!playerPhase) { _playerPhaseSince = -1f; return; }
            if (_playerPhaseSince < 0) _playerPhaseSince = now;
            if (now - _playerPhaseSince > 5f)
            {
                Plugin.Log.LogInfo("AutoWinFights: CompleteFight");
                PersistentSystem<KITS.Level.LevelSystem>.Instance?.CompleteFight();
                _playerPhaseSince = now + 30f;   // don't fire again while the fight wraps up
            }
        }
    }

    private static float _shopSince = -1f, _nextLeave;
    private static UIStateSystem.EUIState _shopState;

    /// Autoplay: leave map shops after a few seconds (Gym, Medical, BlackMarket: <X>UISystem.Action_Close;
    /// Trader, Blacksmith, Tavern, Necromancer, Recruit: <X>UIController.Input_Back).
    private static void LeaveShops()
    {
        if (!Plugin.AutoContinue.Value) return;
        var ui = PersistentSystem<UIStateSystem>.Instance;
        if (ui == null) return;
        var st = ui.CurrentState;
        string name = st.ToString();
        bool shop = name is "Gym" or "Medical" or "BlackMarket" or "EndlessUpgrade" or "Trader" or "Blacksmith" or "Tavern" or "Necromancer" or "Recruit";
        float now = Time.unscaledTime;
        if (!shop) { _shopSince = -1f; return; }
        if (_shopSince < 0 || st != _shopState) { _shopSince = now; _shopState = st; }
        if (now - _shopSince < 4f || now < _nextLeave) return;
        _nextLeave = now + 3f;
        switch (name)
        {
            case "Gym": PersistentSystem<FITS2.UI.GymUISystem>.Instance?.Action_Close(); break;
            case "Medical": PersistentSystem<FITS2.UI.MedicalUISystem>.Instance?.Action_Close(); break;
            case "BlackMarket": PersistentSystem<FITS2.UI.BlackMarketUISystem>.Instance?.Action_Close(); break;
            case "EndlessUpgrade": PersistentSystem<FITS2.UI.EndlessUpgradeUISystem>.Instance?.Action_Close(); break;
            case "Trader": PersistentSystem<TraderUISystem>.Instance?._controller?.Input_Back(); break;
            case "Blacksmith": PersistentSystem<BlacksmithUISystem>.Instance?._controller?.Input_Back(); break;
            case "Tavern": PersistentSystem<TavernUISystem>.Instance?._controller?.Input_Back(); break;
            case "Necromancer": PersistentSystem<NecromancerUISystem>.Instance?._controller?.Input_Back(); break;
            case "Recruit": PersistentSystem<RecruitUISystem>.Instance?._controller?.Input_Back(); break;
        }
        Plugin.Log.LogInfo($"AutoLeave: left {name}");
    }

    private static float _rewardSince = -1f, _nextReward;

    /// Autoplay: on "Select Reward" pick the first card (LevelRewardSystem.SelectCardReward); skip other reward kinds.
    private static void PickRewards()
    {
        if (!Plugin.AutoContinue.Value) return;
        var ui = PersistentSystem<UIStateSystem>.Instance;
        float now = Time.unscaledTime;
        if (ui == null || ui.CurrentState != UIStateSystem.EUIState.LevelReward) { _rewardSince = -1f; return; }
        if (_rewardSince < 0) _rewardSince = now;
        if (now - _rewardSince < 3f || now < _nextReward) return;
        _nextReward = now + 3f;
        var rewards = PersistentSystem<KITS.Level.LevelRewardSystem>.Instance;
        var c = PersistentSystem<LevelRewardUISystem>.Instance?._controller;
        if (rewards == null || c == null) return;
        var cards = c._cardVisuals;
        if (cards != null && cards.Count > 0 && cards[0] != null && cards[0].CachedCardInstance != null)
        {
            Plugin.Log.LogInfo("AutoReward: picking the first card");
            rewards.SelectCardReward(cards[0].CachedCardInstance);
        }
        else
        {
            Plugin.Log.LogInfo("AutoReward: skipping (not a card reward)");
            rewards.SkipCurrentReward();
        }
    }

    private static bool _unlocked;

    /// Test copies have fresh profiles (starter decks only, Endless locked). Debug.UnlockAll does what the game's own
    /// Options > "Unlock All" confirmation does: PlayerSaveSystem.SetScoreToHighestUnlockThreshold. Once, in the lobby —
    /// doing it on the main menu left NetworkManagerSystem._offlinePlayerInstance null, so hosting threw in
    /// HostSettingsPanel.ApplyHostSettings (it names the room after that player). Decks are only needed after the lobby.
    private static void UnlockAll()
    {
        if (_unlocked || !Plugin.UnlockAll.Value) return;
        var ui = PersistentSystem<UIStateSystem>.Instance;
        var save = PersistentSystem<KITS.Player.PlayerSaveSystem>.Instance;
        if (ui == null || save == null || ui.CurrentState != UIStateSystem.EUIState.Lobby) return;
        save.SetScoreToHighestUnlockThreshold();
        _unlocked = true;
        Plugin.Log.LogInfo("UnlockAll: score set to highest unlock threshold");
    }

    /// The map's location controllers, through MapUISystem._mapController._mapLayers (no scene search).
    internal static System.Collections.Generic.List<MapLocationController> MapLocations()
    {
        var list = new System.Collections.Generic.List<MapLocationController>();
        var layers = PersistentSystem<FITS2.UI.MapUISystem>.Instance?._mapController?._mapLayers;
        if (layers == null) return list;
        foreach (var layer in layers)
        {
            var locs = layer?._mapLocationControllers;
            if (locs == null) continue;
            foreach (var loc in locs) if (loc != null && loc.gameObject.activeInHierarchy) list.Add(loc);
        }
        return list;
    }

    internal static void Tick()
    {
        UnlockAll();
        SkipHelp();
        PickRewards();
        LeaveShops();
        ContinueAndWin();
        if (!Plugin.AutoMapVote.Value) return;
        var ui = PersistentSystem<UIStateSystem>.Instance;
        if (ui == null || ui.CurrentState != UIStateSystem.EUIState.Map) return;
        if (Time.unscaledTime < _nextMapVote) return;
        _nextMapVote = Time.unscaledTime + 2f;

        var me = PersistentSystem<NetworkManagerSystem>.Instance?.LocalPlayerInstance?._playerGameplayData;
        if (me == null || me._currentVoteType == EPlayerVote.SelectMapLocation) return;   // already voted

        // Debug.AutoMapPrefer: a location type (e.g. MissionEvent) to pick whenever one is selectable.
        string prefer = Plugin.AutoMapPrefer.Value.Trim();
        var map = PersistentSystem<MapSystem>.Instance;
        MapLocationController best = null, preferred = null;
        var locations = MapLocations();
        foreach (var loc in locations)
        {
            if (!loc.IsSelectable) continue;
            if (prefer.Length > 0 && preferred == null && !loc.IsVisited)
            {
                try { if (map._mapLocation_LUT[loc.Id]._location.LocationType.ToString() == prefer) preferred = loc; } catch { }
            }
            // Move forward: the current location (e.g. a shop, which never gets a check mark) stays selectable,
            // so prefer the highest selectable layer; lowest slot on ties.
            if (best == null || loc._layerIndex > best._layerIndex ||
                (loc._layerIndex == best._layerIndex && loc._slotIndex < best._slotIndex))
                best = loc;
        }
        if (preferred != null && preferred._layerIndex >= best._layerIndex) best = preferred;
        // Log what was on offer (layer:type), to see which location types a mode actually generates.
        var offer = new System.Text.StringBuilder();
        foreach (var loc in locations)
        {
            if (!loc.IsSelectable) continue;
            string t = "?";
            try { t = map._mapLocation_LUT[loc.Id]._location.LocationType.ToString(); } catch { }
            offer.Append($" {loc.Id}@L{loc._layerIndex}:{t}");
        }
        Plugin.Log.LogInfo($"AutoMapVote: offered{offer}");
        if (best == null) return;
        Plugin.Log.LogInfo($"AutoMapVote: voting for location {best.Id} (layer {best._layerIndex}, slot {best._slotIndex}){(best == preferred ? " [" + prefer + "]" : "")}");
        best.ClickEvent();
    }
}
