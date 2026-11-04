using System;
using System.Runtime.InteropServices;

namespace FourPlayers;

/// Raises the game's hard-coded player cap of 3 to General.MaxPlayers in memory at startup -- the same five edits
/// patcher/patch_players.py makes to GameAssembly.dll on disk, so players only install the plugin. Nothing on disk
/// changes (a Steam update or "verify files" can't leave a half-patched install).
///
/// Sites are file offsets of the instruction plus the bytes expected just before the immediate. Each build's sites
/// are checked as a set: every prefix must match and every current value must be a plausible cap (1..32), else the
/// build is unknown (game updated) and nothing is touched -- the game then simply keeps its 3-player cap.
internal static class CapPatch
{
    private readonly record struct Site(string Name, uint FileOffset, byte[] Prefix, int Size);

    private static readonly (string Label, Site[] Sites)[] Builds =
    {
        ("2026-10-29", new[]
        {
            new Site("HostSettingsPanel.ApplyHostSettings (local room MaxPlayers)", 0x8CE198, Hex("c7442438"), 4),
            new Site("SessionSetupData..cctor (static MaxPlayers)", 0x4A7907, Hex("488b88b8000000c701"), 4),
            new Site("GameEnterSystem.CreateRoomForMultiplayer (cloud lobby MaxPlayers)", 0x476F47, Hex("c74720"), 4),
            new Site("LevelPlayerResourceSystem.CreateDataPools (cmp esi,3)", 0x804D0F, Hex("83fe"), 1),
            new Site("TilePingSystem.LoadCompleted (cmp eax,3)", 0x54BFAD, Hex("83f8"), 1),
        }),
        ("2026-09-27", new[]
        {
            new Site("HostSettingsPanel.ApplyHostSettings (local room MaxPlayers)", 0x89E518, Hex("c7442438"), 4),
            new Site("SessionSetupData..cctor (static MaxPlayers)", 0x4AAD87, Hex("488b88b8000000c701"), 4),
            new Site("GameEnterSystem.CreateRoomForMultiplayer (cloud lobby MaxPlayers)", 0x47BB17, Hex("c74720"), 4),
        }),
    };

    internal static string Status = "not run";

    internal static void Apply(int players)
    {
        IntPtr module = GetModuleHandle("GameAssembly.dll");
        if (module == IntPtr.Zero) { Status = "GameAssembly.dll not loaded"; Plugin.Log.LogError("CapPatch: " + Status); return; }

        foreach (var (label, sites) in Builds)
        {
            var addrs = new IntPtr[sites.Length];
            bool match = true;
            for (int i = 0; i < sites.Length && match; i++)
            {
                long rva = FileOffsetToRva(module, sites[i].FileOffset);
                if (rva < 0) { match = false; break; }
                var at = module + (int)rva;
                for (int b = 0; b < sites[i].Prefix.Length && match; b++)
                    if (Marshal.ReadByte(at, b) != sites[i].Prefix[b]) match = false;
                int cur = ReadImm(at + sites[i].Prefix.Length, sites[i].Size);
                if (cur < 1 || cur > 32) match = false;
                addrs[i] = at + sites[i].Prefix.Length;
            }
            if (!match) continue;

            for (int i = 0; i < sites.Length; i++)
            {
                int before = ReadImm(addrs[i], sites[i].Size);
                WriteImm(addrs[i], sites[i].Size, players);
                Plugin.Log.LogInfo($"CapPatch: {sites[i].Name}: {before} -> {ReadImm(addrs[i], sites[i].Size)}");
            }
            // The static constructor may already have run with the old value: set the field too.
            try { KITS.Session.SessionSetupSystem.SessionSetupData.MaxPlayers = players; }
            catch (Exception e) { Plugin.Log.LogWarning($"CapPatch: SessionSetupData.MaxPlayers: {e.Message}"); }
            Status = $"build {label}: player cap {players}";
            Plugin.Log.LogInfo("CapPatch: " + Status);
            return;
        }
        Status = "unknown game build (updated?) -- player cap left at 3";
        Plugin.Log.LogError("CapPatch: " + Status);
    }

    private static int ReadImm(IntPtr at, int size) => size == 4 ? Marshal.ReadInt32(at) : (sbyte)Marshal.ReadByte(at);

    private static void WriteImm(IntPtr at, int size, int value)
    {
        if (!VirtualProtect(at, (UIntPtr)size, 0x40 /* PAGE_EXECUTE_READWRITE */, out uint old))
            throw new InvalidOperationException($"VirtualProtect failed ({Marshal.GetLastWin32Error()})");
        if (size == 4) Marshal.WriteInt32(at, value); else Marshal.WriteByte(at, (byte)value);
        VirtualProtect(at, (UIntPtr)size, old, out _);
        FlushInstructionCache(GetCurrentProcess(), at, (UIntPtr)size);
    }

    /// PE section table: map a file offset to its in-memory RVA.
    private static long FileOffsetToRva(IntPtr module, uint fileOffset)
    {
        int peOff = Marshal.ReadInt32(module, 0x3C);
        int sections = Marshal.ReadInt16(module, peOff + 6);
        int optSize = Marshal.ReadInt16(module, peOff + 20);
        int table = peOff + 24 + optSize;
        for (int s = 0; s < sections; s++)
        {
            int h = table + s * 40;
            uint va = (uint)Marshal.ReadInt32(module, h + 12);
            uint rawSize = (uint)Marshal.ReadInt32(module, h + 16);
            uint rawPtr = (uint)Marshal.ReadInt32(module, h + 20);
            if (fileOffset >= rawPtr && fileOffset < rawPtr + rawSize) return va + (fileOffset - rawPtr);
        }
        return -1;
    }

    private static byte[] Hex(string s) => Convert.FromHexString(s);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtect(IntPtr addr, UIntPtr size, uint newProtect, out uint oldProtect);
    [DllImport("kernel32.dll")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr addr, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
}
