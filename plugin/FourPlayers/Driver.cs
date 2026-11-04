using System;
using System.IO;
using BepInEx;
using KITS.Characters;
using KITS.Framework;
using UnityEngine;

namespace FourPlayers;

/// The plugin's only entry point into the game loop: Unity calls Update on this injected MonoBehaviour.
/// Every fix is a check-and-repair run from here (no Harmony hooks), and all of it is wrapped in try/catch
/// because an exception escaping into native code would take the process down.
/// Command file: write lines to BepInEx/FourPlayers.cmd (palette | buttons | click <text> | shot [path]).
public class Driver : MonoBehaviour
{
    public Driver(IntPtr ptr) : base(ptr) { }

    private float _nextFix, _nextCmdPoll;
    private static readonly string CmdPath = Path.Combine(Paths.BepInExRootPath, "FourPlayers.cmd");

    // perf: frame times and per-check plugin cost since the last "perf" command; "perf off" skips all plugin work (A/B).
    private static readonly System.Collections.Generic.Dictionary<string, (double ms, int n, double max)> _cost = new();
    private static double _frameMsSum, _frameMsMax; private static int _frames; private static bool _paused;

    private void Update()
    {
        float dt = Time.unscaledDeltaTime * 1000f;
        _frameMsSum += dt; _frames++; if (dt > _frameMsMax) _frameMsMax = dt;
        float now = Time.unscaledTime;
        if (_paused)
        {
            if (now >= _nextCmdPoll) { _nextCmdPoll = now + 0.5f; Guard("commands", PollCommandFile); }
            return;
        }
        if (now >= _nextFix)
        {
            _nextFix = now + 0.25f;
            Guard("palette", Palette.Ensure);
            
            // Several copies on one PC: unfocused copies must keep running or they drop out of the session.
            ApplyFrameRateCap();
            if (!Application.runInBackground) { Application.runInBackground = true; Plugin.Log.LogInfo("runInBackground = true"); }
        }
        Guard("uislots", UiSlots.Tick);
        Guard("cardreselect", CardReselect.Tick);
        Guard("loadingwatchdog", LoadingWatchdog.Tick);
        if (now >= _nextCmdPoll)
        {
            _nextCmdPoll = now + 0.5f;
            Guard("commands", PollCommandFile);
            Guard("tutorial", DevUi.AutoSkipTutorial);
            Guard("autorole", FourPlayers.AutoRole.Tick);
            Guard("autoplay", AutoPlay.Tick);
            Guard("window", WindowLayout.Tick);
            Guard("indexwatchdog", IndexWatchdog.Tick);
            Guard("publishroom", Rooms.PublishIfHosting);
        }
    }

    /// General.FrameRateCap: GameSceneInit sets Application.targetFrameRate = 60 on every scene init, so re-apply.
    private static void ApplyFrameRateCap()
    {
        int cap = Plugin.FrameRateCap.Value;
        if (cap == 0) return;
        int want = cap < 0 ? (int)Math.Round(Screen.currentResolution.refreshRateRatio.value) : cap;
        if (want > 0 && Application.targetFrameRate != want) Application.targetFrameRate = want;
    }

    private static void Guard(string what, Action a)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { a(); }
        catch (Exception e) { Plugin.Log.LogError($"{what}: {e}"); }
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (what.StartsWith("cmd ")) return;
        _cost.TryGetValue(what, out var c);
        _cost[what] = (c.ms + ms, c.n + 1, Math.Max(c.max, ms));
    }

    private static void Perf(string arg)
    {
        if (arg == "off" || arg == "on") { _paused = arg == "off"; Plugin.Log.LogInfo($"perf: plugin work {(_paused ? "PAUSED" : "running")}"); }
        double avg = _frames > 0 ? _frameMsSum / _frames : 0;
        Plugin.Log.LogInfo($"perf: {_frames} frames, avg {avg:0.0}ms ({(avg > 0 ? 1000 / avg : 0):0} fps), worst {_frameMsMax:0.0}ms | " +
                           $"targetFrameRate={Application.targetFrameRate} vSyncCount={QualitySettings.vSyncCount} " +
                           $"refresh={Screen.currentResolution.refreshRateRatio.value:0} screen={Screen.width}x{Screen.height} {Screen.fullScreenMode} " +
                           $"focused={Application.isFocused} paused={_paused}");
        foreach (var kv in _cost)
            Plugin.Log.LogInfo($"  " + kv.Key.PadRight(14) + $" {kv.Value.n} calls  avg {kv.Value.ms / Math.Max(1, kv.Value.n):0.00}ms  max {kv.Value.max:0.00}ms  total {kv.Value.ms:0}ms");
        foreach (var kv in UiSlots.FindMs) Plugin.Log.LogInfo("    find " + kv.Key.PadRight(36) + $" total {kv.Value:0}ms");
        UiSlots.FindMs.Clear();
        _cost.Clear(); _frameMsSum = 0; _frameMsMax = 0; _frames = 0;
    }

    private static void PollCommandFile()
    {
        if (!File.Exists(CmdPath)) return;
        string[] lines;
        try { lines = File.ReadAllLines(CmdPath); File.Delete(CmdPath); }
        catch (IOException) { return; }
        foreach (var l in lines)
            if (!string.IsNullOrWhiteSpace(l)) Guard("cmd " + l, () => Run(l.Trim()));
    }

    private static void Run(string cmd)
    {
        Plugin.Log.LogInfo($"> {cmd}");
        string verb = cmd.Split(' ')[0].ToLowerInvariant();
        string rest = cmd.Length > verb.Length ? cmd.Substring(verb.Length).Trim() : "";
        switch (verb)
        {
            case "palette":
                var sys = PersistentSystem<ColourPaletteLoadSystem>.Instance;
                var icons = sys?._availableIcons;
                if (icons == null) { Plugin.Log.LogInfo("palette: not loaded"); break; }
                for (int i = 0; i < icons.Length; i++)
                    Plugin.Log.LogInfo($"  [{i}] colour={icons[i]._colour} sprite={icons[i]._sprite?.name}");
                break;
            case "saveicon":
                var pal = PersistentSystem<ColourPaletteLoadSystem>.Instance?._availableIcons;
                if (pal == null || pal.Length < 4) { Plugin.Log.LogInfo("saveicon: palette has no 4th entry"); break; }
                var tex = pal[3]._sprite.texture;
                string file = rest.Length > 0 ? rest : Path.Combine(Paths.BepInExRootPath, "player4_icon.png");
                File.WriteAllBytes(file, ImageConversion.EncodeToPNG(tex));
                Plugin.Log.LogInfo($"saveicon -> {file}");
                break;
            case "whoami":
                var nms = PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance;
                var me = nms?.LocalPlayerInstance;
                Plugin.Log.LogInfo($"steam={KITS.Platforms.Steam.SteamManager.Initialised} local='{me?.PlayerName}' " +
                                   $"platformId={me?.ClientPlatformId.Platform}:{me?.ClientPlatformId.Id} index={me?.PlayerIndex} " +
                                   $"connected={nms?.IsConnected} players={nms?._playerInstancesInJoinOrder?.Count}");
                break;
            case "players":
                var nm = PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance;
                var list = nm?._playerInstancesInJoinOrder;
                if (list == null) { Plugin.Log.LogInfo("players: no NetworkManagerSystem"); break; }
                Plugin.Log.LogInfo($"players: count={list.Count} connected={(nm.IsConnected ? nm.ConnectedPlayerCount : 0)} anyJoining={nm.AreAnyPlayersJoining()} " +
                                   $"uiState={PersistentSystem<KITS.UI.UIStateSystem>.Instance?.CurrentState} " +
                                   $"turn={PersistentSystem<KITS.Level.LevelTurnSystem>.Instance?.TurnIndex} phase={PersistentSystem<KITS.Level.LevelTurnSystem>.Instance?.GetCurrentPhaseType()}");
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    var g = p?._playerGameplayData;
                    Plugin.Log.LogInfo($"  [{i}] '{p?.PlayerName}' idx={p?.PlayerIndex} client={p?._clientIdUint} host={p?.IsHost} joining={p?._isJoining} " +
                                       (g == null ? "(no gameplay data)" : $"ui={g._currentUIState} req={g.GameStateRequestStatus} arch={g.SelectedArchetypeIndex} vote={g._currentVoteType} act={g._currentGameActions}"));
                }
                break;
            case "hand":
                // Card piles for every player, as this copy sees them.
                var res = PersistentSystem<FITS2.Level.LevelPlayerResourceSystem>.Instance;
                var pl = PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance?._playerInstancesInJoinOrder;
                if (res == null || pl == null) { Plugin.Log.LogInfo("hand: not in a fight"); break; }
                for (int i = 0; i < pl.Count; i++)
                {
                    var cid = pl[i]._clientId;
                    int Count(KITS.Level.ECardPile p) { try { return res.GetCardPile(cid, p)?.Count ?? -1; } catch { return -2; } }
                    Plugin.Log.LogInfo($"  [{i}] '{pl[i].PlayerName}' client={pl[i]._clientIdUint} hand={Count(KITS.Level.ECardPile.Hand)} " +
                                       $"draw={Count(KITS.Level.ECardPile.DrawPile)} discard={Count(KITS.Level.ECardPile.DiscardPile)}");
                }
                break;
            case "ping":
            {
                // Local player pings the tile its own character stands on (exercises GetPlayerIcon for this slot).
                var nmsP = PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance;
                var ent = PersistentSystem<KITS.Entities.EntitySystem>.Instance?.GetFirstEntityClientHasAuthorityOver(nmsP.LocalPlayerInstance._clientId);
                var tile = ent.HasValue ? PersistentSystem<KITS.Entities.EntityTileSystem>.Instance?.GetTileForEntity(ent.Value) : null;
                if (tile == null) { Plugin.Log.LogInfo("ping: no tile for local entity"); break; }
                PersistentSystem<KITS.Level.TilePingSystem>.Instance.RequestPingAtTile(tile.TileIndex, KITS.Level.EPingVariant.Tile);
                Plugin.Log.LogInfo($"ping: requested at tile {tile.TileIndex}");
                break;
            }
            case "mark":
            {
                // "mark <slot>": local player marks the first card in that player's hand (card marks use GetPlayerIcon).
                var resM = PersistentSystem<FITS2.Level.LevelPlayerResourceSystem>.Instance;
                var plM = PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance?._playerInstancesInJoinOrder;
                int slot = int.TryParse(rest, out int sl) ? sl : 0;
                if (resM == null || plM == null || slot >= plM.Count) { Plugin.Log.LogInfo("mark: not available"); break; }
                var owner = plM[slot]._clientId;
                var handM = resM.GetCardPile(owner, KITS.Level.ECardPile.Hand);
                if (handM == null || handM.Count == 0) { Plugin.Log.LogInfo("mark: empty hand"); break; }
                resM.ToggleMarkOnCard(owner, handM[0].Id);
                Plugin.Log.LogInfo($"mark: toggled on card {handM[0].Id} of slot {slot}");
                break;
            }
            case "winfight":
                // Host only: LevelSystem.CompleteFight -> EndFight -> LevelSystemNetSync.NotifyAllPlayersOfFightEnd
                // (CompleteLevelInstant is a local-only debug path: only the host's screen moved on).
                if (!FITS2.Network.NetworkManagerSystem.IsGameAuthority) { Plugin.Log.LogInfo("winfight: host only"); break; }
                PersistentSystem<KITS.Level.LevelSystem>.Instance?.CompleteFight();
                Plugin.Log.LogInfo("winfight: CompleteFight called");
                break;
            case "maplocs":
            {
                // Selectable map locations: id, layer/slot, type.
                var map = PersistentSystem<FITS2.Map.MapSystem>.Instance;
                foreach (var loc in AutoPlay.MapLocations())
                {
                    if (!loc.IsSelectable) continue;
                    string type = "?";
                    try { type = map._mapLocation_LUT[loc.Id]._location.LocationType.ToString(); } catch { }
                    Plugin.Log.LogInfo($"  loc {loc.Id} layer {loc._layerIndex} slot {loc._slotIndex} {type}");
                }
                break;
            }
            case "votemap":
                if (ushort.TryParse(rest, out ushort locId))
                {
                    PersistentSystem<FITS2.Network.PlayerVoteSystem>.Instance?.VoteForMapLocation(locId);
                    Plugin.Log.LogInfo($"votemap: {locId}");
                }
                break;
            case "rollback":
                PersistentSystem<FITS2.Network.PlayerVoteSystem>.Instance?.VoteForRollback();
                Plugin.Log.LogInfo("rollback: voted");
                break;
            case "rbarm":
            {
                // Host, tests only: the game allows a rollback only after a card was played this turn
                // (CanUseRollback: RollbacksAvailable > 0 && CardsPlayedThisTurn > 0). Pretend one was.
                var lpsA = PersistentSystem<KITS.Level.LevelPlayerSystem>.Instance;
                if (lpsA == null || !FITS2.Network.NetworkManagerSystem.IsGameAuthority) { Plugin.Log.LogInfo("rbarm: host in a fight only"); break; }
                lpsA.CardsPlayedThisTurn = 1;
                Plugin.Log.LogInfo($"rbarm: CardsPlayedThisTurn=1 canUseRollback={lpsA.CanUseRollback()}");
                break;
            }
            case "rbcheck":
            {
                // Why a rollback vote did or didn't fire: the vote tally and LevelPlayerSystem.CanUseRollback().
                var pvs = PersistentSystem<FITS2.Network.PlayerVoteSystem>.Instance;
                var lps = PersistentSystem<KITS.Level.LevelPlayerSystem>.Instance;
                Plugin.Log.LogInfo($"rbcheck: allVotingRollback={pvs?.AreAllPlayersVotingForVoteType(FITS2.Network.EPlayerVote.Rollback)} " +
                                   $"anyVotingRollback={pvs?.IsAnyPlayerVotingForVoteType(FITS2.Network.EPlayerVote.Rollback)} canUseRollback={lps?.CanUseRollback()}");
                break;
            }
            case "option":
                if (int.TryParse(rest, out int opt)) { PersistentSystem<FITS2.Network.PlayerVoteSystem>.Instance?.VoteForEventOption(opt); Plugin.Log.LogInfo($"option: voted {opt}"); }
                break;
            case "deathoption":
                if (int.TryParse(rest, out int dopt)) { PersistentSystem<FITS2.Network.PlayerVoteSystem>.Instance?.VoteForPostDeathRollbackOption(dopt); Plugin.Log.LogInfo($"deathoption: voted {dopt}"); }
                break;
            case "kill":
            {
                // "kill <slot>": kill that player's character. Send to every copy (each simulates the fight).
                var plK = PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance?._playerInstancesInJoinOrder;
                int ks = int.TryParse(rest, out int kk) ? kk : 3;
                if (plK == null || ks >= plK.Count) break;
                var victim = PersistentSystem<KITS.Entities.EntitySystem>.Instance.GetFirstEntityClientHasAuthorityOver(plK[ks]._clientId);
                PersistentSystem<KITS.Entities.EntityAttributeModifierSystem>.Instance.KillEntity(victim, Il2CppSystem.Guid.Empty, false);
                Plugin.Log.LogInfo($"kill: slot {ks} entity {victim}");
                break;
            }
            case "endturn":
                // Same vote the End Turn button casts; the phase ends when every player has voted.
                PersistentSystem<FITS2.Network.PlayerVoteSystem>.Instance?.VoteForEndOfTurn();
                Plugin.Log.LogInfo("endturn: voted");
                break;
            case "perf": Perf(rest.ToLowerInvariant()); break;
            case "loadkick":
            {
                // Re-run the loading screen's own "player game actions changed" check (waits on PlayerActionsEnded).
                var tls = PersistentSystem<KITS.UI.TransitionLoadingScreenSystem>.Instance;
                Plugin.Log.LogInfo($"loadkick: loading={tls?.IsLoading()} actionsEnded={tls?.HasLoadEventCompleted(KITS.UI.TransitionLoadingScreenSystem.ELoadEvent.PlayerActionsEnded)} anyActing={PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance?.AreAnyPlayersPerformingGameAction()}");
                tls?.OnPlayerGameActionsChanged();
                Plugin.Log.LogInfo($"loadkick: after -> loading={tls?.IsLoading()} ui={PersistentSystem<KITS.UI.UIStateSystem>.Instance?.CurrentState}");
                break;
            }
            case "diffinfo":
            {
                // Difficulty select: which game modes got buttons, and which layout is showing.
                var dsys = PersistentSystem<KITS.UI.DifficultySelectUISystem>.Instance;
                var dc = dsys?._controller;
                Plugin.Log.LogInfo($"diffinfo: system={(dsys != null)} controller={(dc != null)} displayMode={dsys?._displayMode} active={dsys?._active}");
                if (dc == null) break;
                if (dc._buttonControllerListLUT != null)
                    foreach (var kv in dc._buttonControllerListLUT)
                    {
                        var tiers = new System.Text.StringBuilder();
                        if (kv.Value != null) foreach (var b in kv.Value) tiers.Append($" {b.DifficultyTier}{(b.gameObject.activeSelf ? "" : "(off)")}");
                        Plugin.Log.LogInfo($"  buttons[{kv.Key}] = {kv.Value?.Count}:{tiers}");
                    }
                if (dc._verticalButtonLayoutLUT != null)
                    foreach (var kv in dc._verticalButtonLayoutLUT)
                        Plugin.Log.LogInfo($"  layout[{kv.Key}] active={kv.Value?.activeSelf} children={kv.Value?.transform.childCount}");
                break;
            }
            case "sel":
            {
                var l = PersistentSystem<KITS.Level.LevelPlayerSystem>.Instance;
                Plugin.Log.LogInfo($"sel: card={l?.SelectedCardId} entity={l?.SelectedEntity} abilityActive={PersistentSystem<KITS.Abilities.AbilityActionSystem>.Instance?.IsAnAbilityActive}");
                break;
            }
            case "deselect":
                PersistentSystem<KITS.Level.LevelPlayerSystem>.Instance?.ClearCardSelection();
                Plugin.Log.LogInfo("deselect: ClearCardSelection");
                break;
            case "select":
            case "playcard":
            {
                // "select <i>" / "playcard <i>": local player's hand card i; playcard then clicks the first valid tile
                // for each targeting step (test helper: makes this copy play a real card).
                var l = PersistentSystem<KITS.Level.LevelPlayerSystem>.Instance;
                var meP = PersistentSystem<FITS2.Network.NetworkManagerSystem>.Instance?.LocalPlayerInstance;
                var resP = PersistentSystem<FITS2.Level.LevelPlayerResourceSystem>.Instance;
                if (l == null || meP == null || resP == null) { Plugin.Log.LogInfo($"{verb}: not in a fight"); break; }
                var hand = resP.GetCardPile(meP._clientId, KITS.Level.ECardPile.Hand);
                int hi = int.TryParse(rest, out int hv) ? hv : 0;
                if (hand == null || hi >= hand.Count) { Plugin.Log.LogInfo($"{verb}: no hand card {hi}"); break; }
                int cid = hand[hi].Id;
                CardReselect.Select(l, cid);
                Plugin.Log.LogInfo($"{verb}: selected hand[{hi}] card {cid} -> SelectedCardId={l.SelectedCardId}");
                if (verb == "select") break;
                var tts = PersistentSystem<KITS.Targeting.TileTargetingSystem>.Instance;
                for (int step = 0; step < 6 && l.SelectedCardId == cid; step++)
                {
                    var tiles = tts?._selectableTiles;
                    if (tiles == null || tiles.Count == 0) { Plugin.Log.LogInfo($"playcard: no selectable tile at step {step}"); break; }
                    Plugin.Log.LogInfo($"playcard: step {step}: {tiles.Count} selectable, picking tile {tiles[0].TileIndex}");
                    tts.TileSelected(tiles[0], false);
                }
                Plugin.Log.LogInfo($"playcard: done, SelectedCardId={l.SelectedCardId} inHand={CardReselect.InLocalHand(cid)}");
                break;
            }
            case "slotscan": UiSlots.FullScan(); break;
            case "buttons": Plugin.Log.LogInfo("\n" + DevUi.ListButtons()); break;
            case "click": DevUi.Click(rest); break;
            case "toggle": DevUi.Toggle(rest); break;
            case "joinroom": Rooms.Join(); break;
            case "tree": DevUi.Tree(rest); break;
            case "shot": DevUi.Shot(rest.Length > 0 ? rest : null); break;
            default: Plugin.Log.LogWarning($"Unknown command '{cmd}'"); break;
        }
    }
}
