using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;

namespace FourPlayers;

[BepInPlugin(Guid, "Four Players", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "fits2.fourplayers";
    public const string Version = "0.3.1";

    internal static ManualLogSource Log;
    internal static ConfigEntry<int> MaxPlayers, FrameRateCap;
    internal static ConfigEntry<string> ExtraColours;
    internal static ConfigEntry<bool> AutoSkipTutorialPrompt;
    internal static ConfigEntry<bool> PublishRoom;
    internal static ConfigEntry<string> AutoRole;
    internal static ConfigEntry<int> AutoStartPlayers;
    internal static ConfigEntry<bool> AutoMapVote;
    internal static ConfigEntry<int> VoteCountdown;
    internal static ConfigEntry<string> WindowRect, WindowTitle;
    internal static ConfigEntry<bool> AutoContinue, AutoWinFights, UnlockAll, KeepCardSelection;
    internal static ConfigEntry<string> AutoMapPrefer, HostMode;

    public override void Load()
    {
        Log = base.Log;
        KeepCardSelection = Config.Bind("General", "KeepCardSelection", true,
            "Game bug fix: when a teammate plays a card, the game deselects the card you had picked. Reselect it.");
        FrameRateCap = Config.Bind("General", "FrameRateCap", 0,
            "The game caps itself at 60 fps (only applies with VSync off). 0 = leave the game's cap, -1 = match the " +
            "monitor's refresh rate, any other number = that cap.");
        MaxPlayers = Config.Bind("General", "MaxPlayers", 6,
            "Maximum players per lobby (1-16). Everyone in the lobby should use the same value.");
        ExtraColours = Config.Bind("General", "ExtraPlayerColours", "#3FBF59,#3F8FFF,#F2D33F,#E04FB0,#3FD9D9",
            "Colours (hex, comma-separated) for players beyond the game's 3 (red, orange, purple). Player 4 first.");

        AutoSkipTutorialPrompt = Config.Bind("Debug", "AutoSkipTutorialPrompt", false,
            "Test copies: answer the first-play 'view the tutorials?' pop-up with No Thanks, then Close the follow-up.");

        PublishRoom = Config.Bind("Debug", "PublishRoom", false,
            "Test copies: write the hosted room to <instances>/room.json so other copies can 'joinroom' without Steam.");

        AutoRole = Config.Bind("Debug", "AutoRole", "",
            "Test copies: 'host' walks the menus to a hosted skirmish lobby, 'join' joins the room in room.json. Empty = off.");

        AutoStartPlayers = Config.Bind("Debug", "AutoStartPlayers", 0,
            "Test copies (host): press Play once this many players are in the lobby and ready. 0 = don't.");

        AutoMapVote = Config.Bind("Debug", "AutoMapVote", false,
            "Test copies: on the map, vote for the first selectable location if this copy hasn't voted yet.");

        VoteCountdown = Config.Bind("Debug", "VoteCountdown", -1,
            "Test copies (host): vote countdown in seconds to start the game with (0 = off: a vote only resolves when every player has voted). -1 = leave as is.");

        WindowRect = Config.Bind("Debug", "WindowRect", "",
            "Test copies: \"x,y,w,h\" — windowed resolution w x h, window moved to x,y. Empty = leave the window alone.");
        WindowTitle = Config.Bind("Debug", "WindowTitle", "", "Test copies: window title. Empty = leave it.");

        AutoContinue = Config.Bind("Debug", "AutoContinue", false, "Test copies: press Continue on Mission Complete.");
        AutoWinFights = Config.Bind("Debug", "AutoWinFights", false,
            "Test copies (host): end each fight 5s into the player phase via LevelSystem.CompleteFight (networked).");

        AutoMapPrefer = Config.Bind("Debug", "AutoMapPrefer", "",
            "Test copies: preferred location type for AutoMapVote (Level, Gym, Medic, BlackMarket, MissionEvent). Empty = none.");

        HostMode = Config.Bind("Debug", "HostMode", "Skirmish", "Test copies (host): multiplayer mode to start — Skirmish or Endless.");

        UnlockAll = Config.Bind("Debug", "UnlockAll", false,
            "Test copies: on the main menu, do what Options > Unlock All does (this copy's own save only).");

        // First: raise the player cap in memory (replaces patching GameAssembly.dll on disk).
        try { CapPatch.Apply(Math.Clamp(MaxPlayers.Value, 1, 16)); }
        catch (Exception e) { Log.LogError($"CapPatch failed: {e}"); }

        ClassInjector.RegisterTypeInIl2Cpp<Driver>();
        AddComponent<Driver>();
        Log.LogInfo($"Loaded. MaxPlayers={MaxPlayers.Value}");
    }
}
