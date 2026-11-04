# 2 Fights in 2 Tight Spaces: 4 to 6 Players Mod

Play **2 Fights in 2 Tight Spaces** multiplayer with up to **6 players** instead of 3.

> Fan-made mod. Not affiliated with or endorsed by Ground Shatter. Use at your own risk; it's easy to remove (see below).

## Install (2 minutes)

**Everyone in the lobby needs the mod**, both the host and every guest.

1. Download **`FITS2-4to6Players-vX.Y.Z.zip`** from the [Releases page](../../releases/latest).
2. In Steam, right-click **2 Fights in 2 Tight Spaces** › **Manage** › **Browse local files**.
   This opens the folder with `2FightsIn2TightSpaces.exe` in it.
3. Extract **everything** in the zip into that folder, so `winhttp.dll` ends up next to `2FightsIn2TightSpaces.exe`.
   If Windows asks, choose **Replace**.
4. Start the game from Steam as usual.

**The first start takes 1–3 minutes** (black screen or splash) while the mod sets itself up. That's normal and only happens once.

Everything you need is in the zip: the mod plus the exact mod loader version it needs ([BepInEx](https://github.com/BepInEx/BepInEx) 6.0.0-be.791). You don't need to download anything else or edit any files.

## Play

Host a multiplayer game as usual (**Multiplayer › Host**). Up to 6 players can join. With 3 or fewer players the game plays exactly like the original.

## What the mod does

- Raises the multiplayer player cap from 3 to 6 (lobby, cloud lobby, session setup, fight data, ping markers). The cap is raised in memory when the game starts; **no game files are modified**.
- Adds player colours and icons for players 4 to 6: green star, blue hexagon and yellow square.
- Adds the missing slots for players 4 to 6 everywhere the game shows players:
  - the lobby list (scrollable);
  - the party panel on the left;
  - map mission votes;
  - the end-turn "ready" indicator;
  - rollback and "One more chance" votes;
  - event choices;
  - end-of-run stats.
- Fixes a player-number mix-up: two players could end up with the same colour when joining at the same time.
- Fixes a bug from the original game: when a teammate played a card, the card you had picked (but not played yet) got deselected. The mod now selects it again.
- Fixes Endless mode sometimes freezing on the loading screen after a fight's rewards.

## Settings (optional)

After the first start, `BepInEx\config\fits2.fourplayers.cfg` appears in the game folder:

| Setting | Default | What it does |
|---|---|---|
| `MaxPlayers` | `6` | Maximum players per lobby (everyone should use the same value). |
| `KeepCardSelection` | `true` | Reselect your card after a teammate plays one (see above). |
| `FrameRateCap` | `0` | The game caps itself at 60 fps when VSync is off. `0` = keep that, `-1` = match your monitor, or any number. |

The `[Debug]` section is only for testing the mod and is off by default.

## Uninstall

Delete these from the game folder: `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, and the folders `BepInEx` and `dotnet`.
The game is then back to normal; the mod never changed any game files.

## Problems?

- **Game updated and the mod stopped working?** After a game update the mod can't find what it changes, so it leaves the game at 3 players and writes `CapPatch: unknown game build` in the log. It needs an update for the new game version.
- **Something broke?** Open an [issue](../../issues) and attach `BepInEx\LogOutput.log` from the game folder.

## Status

This is an early test release (game version 0.4.23790, Oct 29 2026 build).

**Tested** with 3, 4 and 6 players, each running their own copy of the game on one PC through the game's local server:
- joining, the lobby, ready-up and deck selection;
- map votes;
- fights with all players spawned and holding cards;
- the end-turn votes;
- rollback and the "One more chance" vote after a player dies;
- card rewards, Gym and Medic;
- full Skirmish runs to the end;
- Endless runs.

**Not tested yet:**
- online play through Steam lobbies with real people on different PCs;
- story events in multiplayer;
- the Endless upgrade shop;
- Endless boss fights.

Difficulty doesn't scale with player count yet: 6 players face the same fights as 3.

## Building from source

The mod is a BepInEx 6 IL2CPP plugin (`plugin/FourPlayers`, .NET 6).
1. Install the mod once and start the game, so BepInEx generates `BepInEx\interop`.
2. Set `GameDir` in `plugin/FourPlayers/FourPlayers.csproj` to your game folder.
3. Run `dotnet build -c Release`.
4. Run `python release/make_release.py` to build the drop-in zip (it needs the BepInEx be.791 IL2CPP win-x64 build in `tools/bepinex/be791`).

## License

The mod's code is MIT (see `LICENSE`). The release zip includes BepInEx, which is LGPL-2.1 (`BepInEx/LICENSE-BepInEx.txt`).
