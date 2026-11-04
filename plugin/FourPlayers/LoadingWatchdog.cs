using FITS2.Network;
using KITS.Framework;
using KITS.UI;
using UnityEngine;

namespace FourPlayers;

/// Endless mode: after a fight's rewards, LevelRewardSystem.FinishRewardSelectionForEndlessMode puts up the loading
/// screen with load event PlayerActionsEnded while someone still has a game action (CompletingLevel) set. The
/// screen only re-checks in TransitionLoadingScreenSystem.OnPlayerGameActionsChanged; when that notification is
/// missed (seen every time in test runs, solo and 4 players), it waits forever with nobody acting.
/// Watchdog: if the loading screen is waiting on PlayerActionsEnded and no player has had a game action for 2s, run
/// the game's own re-check. It does nothing when the game is working normally.
internal static class LoadingWatchdog
{
    private static float _idleSince = -1f, _next;

    private static bool WaitingForActionsEnded(TransitionLoadingScreenSystem tls)
    {
        var events = tls._waitForEvents;
        if (events == null) return false;
        for (int i = 0; i < events.Count; i++)
            if (events[i] == TransitionLoadingScreenSystem.ELoadEvent.PlayerActionsEnded) return true;
        return false;
    }

    internal static void Tick()
    {
        float now = Time.unscaledTime;
        if (now < _next) return;
        _next = now + 0.5f;
        var tls = PersistentSystem<TransitionLoadingScreenSystem>.Instance;
        var nms = PersistentSystem<NetworkManagerSystem>.Instance;
        // Only when the screen is actually waiting for PlayerActionsEnded (it's in _waitForEvents) -- never poke
        // other loading screens.
        if (tls == null || nms == null || !tls.IsLoading() || !WaitingForActionsEnded(tls) ||
            !nms.IsConnected || nms.AreAnyPlayersPerformingGameAction())
        {
            _idleSince = -1f;
            return;
        }
        if (_idleSince < 0) { _idleSince = now; return; }
        if (now - _idleSince < 2f) return;
        _idleSince = now;   // retry every 2s if it still hasn't moved
        Plugin.Log.LogInfo("LoadingWatchdog: loading screen waiting on PlayerActionsEnded with nobody acting; re-checking");
        tls.OnPlayerGameActionsChanged();
    }
}
