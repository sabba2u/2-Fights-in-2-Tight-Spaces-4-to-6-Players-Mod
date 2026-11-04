using System;
using System.IO;
using System.Text.Json;
using BepInEx;
using Coherence.Cloud;
using FITS2.Network;
using KITS.Framework;

namespace FourPlayers;

/// Joining a copy's local room without Steam.
/// With Steam, a guest learns the host's RoomId/RoomUid from the Steam lobby and connects over a Steam relay.
/// Test copies have no Steam, so the host writes its created room (ReplicationServerSystem._createdRoom) to a
/// shared JSON file and guests rebuild the RoomData and call ReplicationServerSystem.JoinExistingRoom — the game's
/// own join path, connecting straight to the local replication server.
internal static class Rooms
{
    // <instances dir>/room.json — the copies live in <instances dir>/pN, so this is shared between them.
    private static readonly string RoomFile = Path.Combine(Path.GetDirectoryName(Paths.GameRootPath) ?? Paths.GameRootPath, "room.json");
    internal static string FilePath => RoomFile;
    internal static bool FileExists => File.Exists(RoomFile);
    private static string _publishedState;

    private sealed class RoomInfo
    {
        public ushort Id { get; set; }
        public ulong UniqueId { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
        public string Region { get; set; }
        public string RSVersion { get; set; }
        public int MaxPlayers { get; set; }
        public string SimSlug { get; set; }
        public string Secret { get; set; }
        public string AuthToken { get; set; }
        public string PublishedBy { get; set; }
        public int Players { get; set; }
        public bool AnyJoining { get; set; }
        public DateTime PublishedAt { get; set; }
    }

    /// Host side, called every fix tick: publish the created room once per new room.
    internal static void PublishIfHosting()
    {
        if (!Plugin.PublishRoom.Value) return;
        var rss = PersistentSystem<ReplicationServerSystem>.Instance;
        if (rss == null) return;
        var room = rss._createdRoom;
        if (room.UniqueId == 0) return;
        var nms = PersistentSystem<NetworkManagerSystem>.Instance;
        int players = nms?._playerInstancesInJoinOrder?.Count ?? 0;
        bool joining = nms != null && nms.AreAnyPlayersJoining();
        string state = $"{room.UniqueId}/{players}/{joining}";
        if (state == _publishedState) return;
        var info = new RoomInfo
        {
            Id = room.Id, UniqueId = room.UniqueId,
            Ip = room.Host.Ip, Port = room.Host.Port, Region = room.Host.Region, RSVersion = room.Host.RSVersion,
            MaxPlayers = room.MaxPlayers, SimSlug = room.SimSlug, Secret = room.Secret, AuthToken = room.AuthToken,
            PublishedBy = nms?.LocalPlayerInstance?.PlayerName,
            PublishedAt = DateTime.Now,
            Players = players, AnyJoining = joining,
        };
        File.WriteAllText(RoomFile, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        _publishedState = state;
        Plugin.Log.LogInfo($"Published room {info.Id}/{info.UniqueId} at {info.Ip}:{info.Port} ({info.Region}) players={players} joining={joining}");
    }

    /// Guest side: true when the host's room has exactly `before` players and nobody is mid-join,
    /// so guests join one at a time like real players would.
    internal static bool IsMyTurn(int before)
    {
        try
        {
            var info = JsonSerializer.Deserialize<RoomInfo>(File.ReadAllText(RoomFile));
            return info != null && info.Players == before && !info.AnyJoining;
        }
        catch (IOException) { return false; }      // host is rewriting it
        catch (JsonException) { return false; }
    }

    /// Guest side: join the room in the shared file.
    internal static void Join()
    {
        if (!File.Exists(RoomFile)) { Plugin.Log.LogWarning($"joinroom: no {RoomFile} (host first)"); return; }
        var info = JsonSerializer.Deserialize<RoomInfo>(File.ReadAllText(RoomFile));
        var rss = PersistentSystem<ReplicationServerSystem>.Instance;
        if (rss == null) { Plugin.Log.LogWarning("joinroom: ReplicationServerSystem not ready"); return; }

        var host = new RoomHostData { Ip = info.Ip, Port = info.Port, Region = info.Region, RSVersion = info.RSVersion };
        var room = new RoomData
        {
            Id = info.Id, UniqueId = info.UniqueId, Host = host, MaxPlayers = info.MaxPlayers,
            SimSlug = info.SimSlug, Secret = info.Secret, AuthToken = info.AuthToken,
            KV = new Il2CppSystem.Collections.Generic.Dictionary<string, string>(),
            Tags = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(0),
        };
        Plugin.Log.LogInfo($"joinroom: {info.Id}/{info.UniqueId} at {info.Ip}:{info.Port} ({info.Region}), published by {info.PublishedBy} at {info.PublishedAt:T}");
        rss.JoinExistingRoom(room);
    }
}
