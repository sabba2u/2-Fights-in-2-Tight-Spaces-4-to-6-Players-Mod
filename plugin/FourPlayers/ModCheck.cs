using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using KITS.Framework;
using KITS.Platforms.Steam;

namespace FourPlayers;

/// Tells everyone in a Steam lobby who is missing the mod (or has it without the raised player cap).
///
/// Why: the cap is raised on each PC separately. A guest whose cap stayed at 3 can still join a modded host's
/// lobby as player 4+, but their game never builds that player's turn data: no cards, "-1/-1", and the fight
/// waits on them forever.
///
/// How: each player's mod writes a Steam lobby member-data tag ("fits2mod" = "version|cap"); Steam syncs it to
/// every member. Everyone then reads every member's tag. A member with no tag (after a grace period) has no mod
/// running; a tag with cap 3 means the mod loaded but the cap didn't apply (unsupported game version).
/// Steam's flat C API is called directly: the game's Facepunch wrapper has the member-data methods stripped.
/// Cloud (non-Steam) lobbies have no member data, so they aren't checked.
internal static class ModCheck
{
    private const string Key = "fits2mod";
    private const float GraceSeconds = 10f;   // member data reaches the others a moment after joining

    private static bool _disabled;
    private static int _errors;
    private static ulong _taggedLobby;
    private static float _nextRetag;
    private static readonly Dictionary<ulong, float> FirstSeen = new();
    private static readonly Dictionary<ulong, string> Reported = new();   // member -> problem already logged
    private static string _selfProblem;
    internal static string TestText;   // "banner <text>" command: preview the warning strip

    internal static void Tick()
    {
        string self = SelfProblem();
        if (self != _selfProblem) { _selfProblem = self; if (self != null) Plugin.Log.LogError("ModCheck: " + self); }

        var problems = new List<string>();
        if (TestText != null) problems.Add(TestText);
        if (self != null) problems.Add(self);
        if (!_disabled)
        {
            try { CheckLobby(problems); _errors = 0; }
            catch (DllNotFoundException) { _disabled = true; Plugin.Log.LogInfo("ModCheck: Steam not available, lobby check off"); }
            catch (EntryPointNotFoundException e) { _disabled = true; Plugin.Log.LogWarning("ModCheck: " + e.Message); }
            catch (Exception e)
            {
                // Never let the lobby check block the banner; give up after repeated failures.
                if (++_errors == 1) Plugin.Log.LogWarning("ModCheck: " + e);
                if (_errors >= 20) { _disabled = true; Plugin.Log.LogWarning("ModCheck: lobby check off after repeated errors"); }
            }
        }
        Banner.Show(problems.Count == 0 ? null : string.Join("\n", problems));
    }

    private static string SelfProblem() =>
        CapPatch.Cap >= 4 ? null
            : "4-6 Players Mod: the player cap is NOT active on your PC (" + CapPatch.Status + "). " +
              "Play with 3 or fewer, or update the mod.";

    private static void CheckLobby(List<string> problems)
    {
        var sm = PersistentSystem<SteamManager>.Instance;
        if (sm == null || !SteamManager.Initialised) return;    // no Steam (or not started yet)
        ulong lobby = ActiveLobbyId(sm);
        if (lobby == 0) { Forget(); return; }

        IntPtr mm = SteamAPI_SteamMatchmaking_v009();
        if (mm == IntPtr.Zero) return;
        float now = UnityEngine.Time.unscaledTime;

        // Our own tag: set on joining a lobby, refreshed now and then (cheap; Steam only sends changes).
        if (lobby != _taggedLobby || now >= _nextRetag)
        {
            if (lobby != _taggedLobby) { Forget(); Plugin.Log.LogInfo($"ModCheck: in Steam lobby {lobby}, tag {Tag()}"); }
            SteamAPI_ISteamMatchmaking_SetLobbyMemberData(mm, lobby, Key, Tag());
            _taggedLobby = lobby;
            _nextRetag = now + 30f;
        }

        ulong me = SteamAPI_ISteamUser_GetSteamID(SteamAPI_SteamUser_v023());
        int count = SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(mm, lobby);
        var present = new HashSet<ulong>();
        bool anyMember = false;
        for (int i = 0; i < count; i++)
        {
            ulong member = SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex(mm, lobby, i);
            if (member == 0 || member == me) continue;
            present.Add(member);
            if (!FirstSeen.ContainsKey(member)) FirstSeen[member] = now;

            string tag = Utf8(SteamAPI_ISteamMatchmaking_GetLobbyMemberData(mm, lobby, member, Key));
            string problem = null;
            if (string.IsNullOrEmpty(tag))
            {
                if (now - FirstSeen[member] >= GraceSeconds)
                    problem = $"{Name(member)} does NOT have the 4-6 Players Mod running.";
            }
            else
            {
                var parts = tag.Split('|');
                int cap = parts.Length > 1 && int.TryParse(parts[1], out var c) ? c : 0;
                if (cap < 4)
                    problem = $"{Name(member)} has the mod, but their player cap is NOT active (different game version?).";
            }

            // Log once per change (still in the grace period with no tag = undecided, nothing logged).
            bool decided = problem != null || !string.IsNullOrEmpty(tag);
            if (decided && (!Reported.TryGetValue(member, out var before) || before != problem))
            {
                if (problem != null) Plugin.Log.LogWarning("ModCheck: " + problem + (tag != "" ? $" [tag '{tag}']" : ""));
                else Plugin.Log.LogInfo($"ModCheck: {Name(member)} has the mod [tag '{tag}']");
                Reported[member] = problem;
            }
            if (problem != null) { problems.Add(problem); anyMember = true; }
        }
        foreach (var gone in new List<ulong>(FirstSeen.Keys))
            if (!present.Contains(gone)) { FirstSeen.Remove(gone); Reported.Remove(gone); }

        if (anyMember)
            problems.Add("With more than 3 players their fight will freeze (no cards). Everyone needs the mod working: see the GitHub page.");
    }

    /// SteamManager._activeLobby is a Nullable<Steamworks.Data.Lobby>; Il2CppInterop can't wrap a returned
    /// Nullable of a struct (get_ActiveLobby throws), so read the field's memory: { bool hasValue; Lobby value }
    /// with Lobby = { SteamId Id = { ulong Value } } at offset 8.
    private static uint _lobbyFieldOffset;
    private static ulong ActiveLobbyId(SteamManager sm)
    {
        if (_lobbyFieldOffset == 0)
        {
            var fi = typeof(SteamManager).GetField("NativeFieldInfoPtr__activeLobby",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (fi == null) throw new MissingFieldException("SteamManager._activeLobby");
            _lobbyFieldOffset = Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset((IntPtr)fi.GetValue(null));
            if (_lobbyFieldOffset == 0) throw new MissingFieldException("SteamManager._activeLobby offset");
        }
        IntPtr field = sm.Pointer + (int)_lobbyFieldOffset;
        if (Marshal.ReadByte(field) == 0) return 0;
        ulong id = (ulong)Marshal.ReadInt64(field + 8);
        // A lobby SteamID: universe 1, account type 8 (chat) -> top 12 bits 0x018. Anything else = wrong layout.
        if (id >> 52 != 0x018) throw new InvalidOperationException($"unexpected lobby id 0x{id:X16}");
        return id;
    }

    private static string Tag() => $"{Plugin.Version}|{CapPatch.Cap}";

    private static void Forget()
    {
        _taggedLobby = 0;
        FirstSeen.Clear();
        Reported.Clear();
    }

    private static string Name(ulong id)
    {
        try
        {
            string n = Utf8(SteamAPI_ISteamFriends_GetFriendPersonaName(SteamAPI_SteamFriends_v017(), id));
            if (!string.IsNullOrEmpty(n) && n != "[unknown]") return n;
        }
        catch { }
        return "Steam user " + id;
    }

    /// For the "modcheck" command.
    internal static void Report()
    {
        Plugin.Log.LogInfo($"ModCheck: self tag {Tag()}, cap status '{CapPatch.Status}', disabled={_disabled}, lobby={_taggedLobby}");
        if (_disabled || _taggedLobby == 0) return;
        IntPtr mm = SteamAPI_SteamMatchmaking_v009();
        int count = SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(mm, _taggedLobby);
        for (int i = 0; i < count; i++)
        {
            ulong m = SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex(mm, _taggedLobby, i);
            Plugin.Log.LogInfo($"  {Name(m)} ({m}): '{Utf8(SteamAPI_ISteamMatchmaking_GetLobbyMemberData(mm, _taggedLobby, m, Key))}'");
        }
    }

    private static string Utf8(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";

    // steam_api64.dll is already loaded by the game (2FightsIn2TightSpaces_Data/Plugins/x86_64): LoadLibrary by
    // name returns that module. Test copies run without it -> DllNotFoundException -> check disabled.
    private const string Steam = "steam_api64";
    [DllImport(Steam)] private static extern IntPtr SteamAPI_SteamMatchmaking_v009();
    [DllImport(Steam)] private static extern IntPtr SteamAPI_SteamUser_v023();
    [DllImport(Steam)] private static extern IntPtr SteamAPI_SteamFriends_v017();
    [DllImport(Steam)] private static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);
    [DllImport(Steam)] private static extern int SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(IntPtr self, ulong lobby);
    [DllImport(Steam)] private static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex(IntPtr self, ulong lobby, int index);
    [DllImport(Steam)] private static extern IntPtr SteamAPI_ISteamMatchmaking_GetLobbyMemberData(IntPtr self, ulong lobby, ulong user,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [DllImport(Steam)] private static extern void SteamAPI_ISteamMatchmaking_SetLobbyMemberData(IntPtr self, ulong lobby,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Steam)] private static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong friend);
}
