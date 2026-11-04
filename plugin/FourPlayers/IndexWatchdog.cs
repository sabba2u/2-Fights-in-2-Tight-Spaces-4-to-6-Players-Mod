using FITS2.Network;
using KITS.Framework;
using UnityEngine;

namespace FourPlayers;

/// Host only: keep every player's PlayerIndex equal to their join-order position.
/// NetworkManagerSystem.SortPlayerInstancesByJoinOrder sends SetPlayerIndex(i) to each player's owning client; if that
/// command lands while the client is still setting up it is lost and the player keeps index 0 — sharing the host's
/// colour, icon and vote slot. Seen with 4 quick joins. Re-send the game's own RequestSetPlayerIndex when they differ.
internal static class IndexWatchdog
{
    private static float _next;

    internal static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 2f;
        var nms = PersistentSystem<NetworkManagerSystem>.Instance;
        if (nms == null || !nms.IsConnected || !NetworkManagerSystem.IsGameAuthority || nms.AreAnyPlayersJoining()) return;
        var list = nms._playerInstancesInJoinOrder;
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (p == null || p.PlayerIndex == i) continue;
            Plugin.Log.LogWarning($"IndexWatchdog: '{p.PlayerName}' has PlayerIndex {p.PlayerIndex}, join order {i} - re-sending");
            p.RequestSetPlayerIndex((byte)i);
        }
    }
}
