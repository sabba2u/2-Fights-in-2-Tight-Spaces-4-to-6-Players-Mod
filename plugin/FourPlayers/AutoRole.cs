using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace FourPlayers;

/// Test copies: walk the menus automatically so a session is just "launch and wait".
/// Config Debug.AutoRole = host | join. Each step waits until its button is present, clicks it, and moves on —
/// no fixed delays. Hosts end in the skirmish lobby (local room); guests open Join and join the room the host
/// published to room.json (see Rooms).
internal static class AutoRole
{
    private const string MM = "Main Content/";

    private static readonly string[] HostSteps =
    {
        "PRESS ENTER",
        MM + "Home/content/container/Column 1/Multiplayer",
        MM + "Multiplayer Select/content/container/Column 2/Host",
        MM + "Host Setup/content/container/Column 2/Host",
        "@modepick",    // Mode Select Multiplayer: Skirmish (default) or Endless — Debug.HostMode
        "@modestart",   // then that mode's "New ..." button
        "DifficultyButtons/Classic",
        MM + "Generic_Difficulty_Select/content/container/Column 3/Start",
        "@startgame",
        "@deck",
    };

    private static readonly string[] JoinSteps =
    {
        "PRESS ENTER",
        MM + "Home/content/container/Column 1/Multiplayer",
        MM + "Multiplayer Select/content/container/Column 3/JoinOld",
        "@joinroom",
        "@deck",
    };

    private static int _step;
    private static float _nextAt, _stepStarted = -1;
    private static bool _done;
    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime;

    private static float _lastPick = -10f, _deckSeen = -1f, _buttonSeen = -1f;

    /// Deck select: pick a deck type -> card list -> Continue. Picking too early (as automation can, but a person
    /// can't) left a copy's own deck empty (its card list hadn't loaded) while the host still saw the deck id, so:
    /// wait 2s after the screen opens, select via DeckSelectUIController, and only confirm once the card list has cards.
    private static bool DeckStep(float now)
    {
        KITS.UI.DeckSelectUIController ui = null;
        foreach (var c in UnityEngine.Object.FindObjectsByType<KITS.UI.DeckSelectUIController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (c.gameObject.activeInHierarchy) { ui = c; break; }
        if (ui == null) { _deckSeen = -1f; return false; }
        if (_deckSeen < 0) _deckSeen = now;
        if (now - _deckSeen < 2f) return false;

        var details = ui._detailsPanelController;
        if (details != null && details._panelRoot != null && details._panelRoot.activeInHierarchy)
        {
            int cards = details._selectableCards?.Count ?? 0;
            if (cards == 0) return false;   // card list still loading
            var cont = DevUi.FindButton("DetailsPanel/BottomMenu/container/MenuButton_continue");
            if (cont == null) return false;
            Plugin.Log.LogInfo($"AutoRole: deck '{ui.SelectedDeckId}' has {cards} cards, continuing");
            DevUi.Press(cont);
            return true;
        }

        if (now - _lastPick < 3f) return false;
        var panel = ui._selectionPanelController;
        if (panel?._deckButtons == null) return false;
        for (int i = 0; i < panel._deckButtons.Count; i++)
        {
            var b = panel._deckButtons[i];
            if (b == null || b._customDeckButton || b._showLockedState) continue;
            Plugin.Log.LogInfo($"AutoRole: picking deck '{b._deckId}' (button {i})");
            ui.SetSelectedDeck(b);
            ui.GoToDeckDetails();
            _lastPick = now;
            return false;   // confirm on a later tick, once the card list is filled
        }
        return false;
    }

    internal static void Tick()
    {
        if (_done) return;
        // "host", "join", or "join:N" = wait until the host's room has N players and nobody is joining.
        string role = Plugin.AutoRole.Value.Trim().ToLowerInvariant();
        int before = -1;
        if (role.StartsWith("join:") && int.TryParse(role.Substring(5), out int n)) { before = n; role = "join"; }
        string[] steps = role == "host" ? HostSteps : role == "join" ? JoinSteps : null;
        if (steps == null) { _done = true; return; }

        float now = Time.unscaledTime;
        if (now < _nextAt) return;
        if (_step >= steps.Length) { _done = true; Plugin.Log.LogInfo($"AutoRole {role}: done in {now:0.0}s"); return; }
        if (_stepStarted < 0) _stepStarted = now;

        string s = steps[_step];
        bool fired = false;
        if (s == "@modepick" || s == "@modestart")
        {
            // Two separate presses: the mode button prepares the mode (incl. its difficulty list). Every menu panel
            // counts as active, so "New Skirmish" is findable before that — pressing it directly left an empty list.
            bool endless = Plugin.HostMode.Value.Trim().Equals("Endless", StringComparison.OrdinalIgnoreCase);
            string target = s == "@modepick"
                ? (endless ? "Mode Select Multiplayer/content/container/Column 1/Endless" : "Mode Select Multiplayer/content/container/Column 2/Skirmish")
                : (endless ? "GameMode_MP_Endless/content/container/Column 3/NewEndless" : "GameMode_MP_Skirmish/content/container/Column 3/NewSkirmish");
            var mb = DevUi.FindButton(target);
            if (mb == null) _buttonSeen = -1f;
            else if (_buttonSeen < 0) _buttonSeen = now;
            else if (now - _buttonSeen >= 1f) { DevUi.Press(mb); fired = true; _buttonSeen = -1f; }
        }
        else if (s == "@deck")
        {
            fired = DeckStep(now);
        }
        else if (s == "@startgame")
        {
            // Press Play once every expected player is in, nobody is joining, and the game says all are ready.
            int want = Plugin.AutoStartPlayers.Value;
            if (want <= 0) { _step++; return; }
            var nms = KITS.Framework.PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance;
            int have = nms?._playerInstancesInJoinOrder?.Count ?? 0;
            if (have >= want && !nms.AreAnyPlayersJoining())
            {
                foreach (var c in UnityEngine.Object.FindObjectsByType<KITS.UI.LobbyUIController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    var ready = c.AreAllPlayersReady();
                    if (ready != KITS.UI.LobbyUIController.EPlayersReadyResult.Ready) break;   // JoiningGame / NotAllInLobby / WaitingForGameState
                    // Optional test override of the lobby's vote countdown (0 = disabled, so a turn only ends when
                    // every player votes). The value lives in PlayerPrefs (shared with the real game), so restore it.
                    int orig = c._voteCountdownTime, wantCountdown = Plugin.VoteCountdown.Value;
                    if (wantCountdown >= 0 && wantCountdown != orig) c.Input_ModifyVoteCountdown(wantCountdown - orig);
                    Plugin.Log.LogInfo($"AutoRole host: {have} players ready, starting game (vote countdown {c._voteCountdownTime}s, was {orig}s)");
                    c.Input_StartGame();
                    if (c._voteCountdownTime != orig) c.Input_ModifyVoteCountdown(orig - c._voteCountdownTime);
                    fired = true;
                    break;
                }
            }
        }
        else if (s == "@joinroom")
        {
            // Wait for a room published after this copy started (the session deletes old ones anyway).
            // NetworkManagerSystem.OnJoiningStateChanged dereferences _offlinePlayerInstance._playerGameplayData;
            // both load asynchronously, and joining before they exist throws mid-join (seen on a 6-copy run).
            var offline = KITS.Framework.PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance?._offlinePlayerInstance;
            if (offline?._playerGameplayData != null &&
                Rooms.FileExists && File.GetLastWriteTime(Rooms.FilePath) > ProcessStart && (before < 0 || Rooms.IsMyTurn(before)))
            { Rooms.Join(); fired = true; }
        }
        else
        {
            // Wait 1s after the button first appears: pressing Host Setup's HOST before the panel finished
            // initialising made the game's ApplyHostSettings throw a NullReferenceException.
            var b = DevUi.FindButton(s);
            // Host Setup's HOST names the room after NetworkManagerSystem._offlinePlayerInstance, which loads
            // asynchronously; pressing before it exists throws in HostSettingsPanel.ApplyHostSettings.
            if (b != null && s.Contains("Host Setup") &&
                KITS.Framework.PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance?._offlinePlayerInstance == null)
                b = null;
            if (b == null) _buttonSeen = -1f;
            else if (_buttonSeen < 0) _buttonSeen = now;
            else if (now - _buttonSeen >= 1f) { DevUi.Press(b); fired = true; _buttonSeen = -1f; }
        }

        if (fired)
        {
            _step++;
            _stepStarted = -1;
            _nextAt = now + 0.5f;   // let the screen transition start before looking for the next button
        }
        else if (now - _stepStarted > (s.StartsWith("@") ? 240f : 60f))
        {
            Plugin.Log.LogWarning($"AutoRole {role}: gave up waiting for '{s}' (step {_step + 1}/{steps.Length})");
            _done = true;
        }
    }
}
