using System;
using UnityEngine;

namespace FourPlayers;

/// The game's player icons are white shapes on a 64x64 slice (playerIcons_1..3: diamond, triangle, circle),
/// tinted with the player's colour. Extra players get new shapes drawn at runtime so they're not mistaken for
/// players 1-3: a five-point star, then a hexagon, then a square.
internal static class IconShapes
{
    private const int Size = 64;
    private const int Supersample = 4;

    internal static Sprite Create(int extraIndex, Sprite like)
    {
        float[][] poly = (extraIndex % 3) switch
        {
            0 => Star(5, 32f, 14f),
            1 => Regular(6, 28f, 0f),
            _ => Regular(4, 27f * 1.4142f, 45f),
        };

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.name = $"FourPlayers_playerIcon_{4 + extraIndex}";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int inside = 0;
            for (int sy = 0; sy < Supersample; sy++)
            for (int sx = 0; sx < Supersample; sx++)
            {
                float fx = x + (sx + 0.5f) / Supersample - Size / 2f;
                float fy = y + (sy + 0.5f) / Supersample - Size / 2f;
                if (Inside(poly, fx, fy)) inside++;
            }
            byte a = (byte)(255 * inside / (Supersample * Supersample));
            px[y * Size + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        tex.hideFlags = HideFlags.HideAndDontSave;

        float ppu = like != null ? like.pixelsPerUnit : 100f;
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), ppu);
        sprite.name = tex.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // Polygons as [x0,y0,x1,y1,...] centred on the origin, y up.
    private static float[][] Star(int points, float outer, float inner)
    {
        var v = new float[points * 2][];
        for (int i = 0; i < points * 2; i++)
        {
            float r = i % 2 == 0 ? outer : inner;
            float ang = (90f + i * 180f / points) * Mathf.Deg2Rad;
            v[i] = new[] { r * Mathf.Cos(ang), r * Mathf.Sin(ang) - 2f };
        }
        return v;
    }

    private static float[][] Regular(int sides, float radius, float startDeg)
    {
        var v = new float[sides][];
        for (int i = 0; i < sides; i++)
        {
            float ang = (startDeg + i * 360f / sides) * Mathf.Deg2Rad;
            v[i] = new[] { radius * Mathf.Cos(ang), radius * Mathf.Sin(ang) };
        }
        return v;
    }

    // Even-odd ray casting.
    private static bool Inside(float[][] poly, float x, float y)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            float xi = poly[i][0], yi = poly[i][1], xj = poly[j][0], yj = poly[j][1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c;
        }
        return c;
    }
}
