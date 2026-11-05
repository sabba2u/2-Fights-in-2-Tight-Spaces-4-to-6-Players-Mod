using System;
using System.Globalization;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KITS.Characters;
using KITS.Framework;
using UnityEngine;

namespace FourPlayers;

/// The game has 3 player colours (ColourPaletteSO._availableIcons: red, orange, purple), copied into
/// ColourPaletteLoadSystem._availableIcons. PlayerInstance.GetPlayerIcon() reads [PlayerIndex] from it with a bounds
/// check, so player index 3 throws in the lobby row, votes, card marks, pings, party HUD...
/// Append colours until there are MaxPlayers entries. Checked every frame, so it re-applies after a reload.
internal static class Palette
{
    private static readonly string[] DefaultExtra = { "#3FBF59", "#3F8FFF", "#F2D33F", "#E04FB0", "#3FD9D9" };
    private static bool _loggedWaiting;
    private static Sprite[] _sprites; // created once, reused when the palette is re-extended

    internal static void Ensure()
    {
        var sys = PersistentSystem<ColourPaletteLoadSystem>.Instance;        if (sys == null) return;
        var icons = sys._availableIcons;
        int want = Plugin.MaxPlayers.Value;
        if (icons == null || icons.Length == 0)
        {
            if (!_loggedWaiting) { Plugin.Log.LogInfo("Palette not loaded yet"); _loggedWaiting = true; }
            return;
        }
        if (icons.Length >= want) return;

        var extra = Plugin.ExtraColours.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var grown = new Il2CppReferenceArray<PlayerIconData>(want);
        for (int i = 0; i < icons.Length; i++) grown[i] = icons[i];
        for (int i = icons.Length; i < want; i++)
        {
            int k = i - icons.Length;
            var d = new PlayerIconData();
            // Short lists (e.g. an old config with one colour) fall back to the defaults, so players never share a colour.
            d._colour = ParseColour(k < extra.Length ? extra[k] : DefaultExtra[k % DefaultExtra.Length]);
            _sprites ??= new Sprite[8];
            if (_sprites[k] == null) _sprites[k] = IconShapes.Create(k, icons[0]._sprite);
            d._sprite = _sprites[k];
            grown[i] = d;
        }
        sys._availableIcons = grown;
        Plugin.Log.LogInfo($"Palette: {icons.Length} -> {want} player colours ({Describe(grown)})");
    }

    private static Color ParseColour(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, null, out uint v)) return new Color(0.25f, 0.75f, 0.35f, 1f);
        return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
    }

    private static string Describe(Il2CppReferenceArray<PlayerIconData> a)
    {
        var parts = new string[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            var c = a[i]._colour;
            parts[i] = $"#{(int)(c.r * 255):X2}{(int)(c.g * 255):X2}{(int)(c.b * 255):X2}";
        }
        return string.Join(" ", parts);
    }
}
