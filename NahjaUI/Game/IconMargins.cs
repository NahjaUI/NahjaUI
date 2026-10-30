using System;
using System.Collections.Generic;
using Dalamud.Interface.Textures;

namespace NahjaUI.Game;

/// <summary>
/// Measures how much empty (transparent or dark) margin a game icon has at its left and right, so an aura can cut it away
/// and fill the width of its box with the art. Measured once per icon, from the image file of the game.
/// </summary>
internal static class IconMargins
{
    private static readonly Dictionary<uint, (float Left, float Right)> Cache = new();

    /// <summary>The fraction of the width (0 to 0.3) to cut on each side. (0, 0) when it cannot be measured.</summary>
    public static (float Left, float Right) Get(uint iconId)
    {
        if (iconId == 0)
            return (0f, 0f);

        if (Cache.TryGetValue(iconId, out var known))
            return known;

        var result = (0f, 0f);

        try
        {
            result = Measure(iconId);
        }
        catch (Exception)
        {
            // Not measurable: the icon is drawn whole.
        }

        Cache[iconId] = result;
        return result;
    }

    private static (float, float) Measure(uint iconId)
    {
        var path = Plugin.TextureProvider.GetIconPath(new GameIconLookup(iconId));
        if (string.IsNullOrEmpty(path))
            return (0f, 0f);

        var tex = Plugin.DataManager.GetFile<Lumina.Data.Files.TexFile>(path);
        if (tex is null)
            return (0f, 0f);

        var width = (int)tex.Header.Width;
        var height = (int)tex.Header.Height;
        var data = tex.ImageData;
        if (width < 4 || height < 4 || data.Length < width * height * 4)
            return (0f, 0f);

        // A column belongs to the art when enough of its pixels (in the middle part of the icon, away from the rounded corners)
        // are opaque and not nearly black.
        var top = height / 5;
        var bottom = height - height / 5;

        bool IsArt(int x)
        {
            var art = 0;
            for (var y = top; y < bottom; y++)
            {
                var i = (y * width + x) * 4;
                var brightness = Math.Max(data[i], Math.Max(data[i + 1], data[i + 2]));
                if (data[i + 3] > 128 && brightness > 40)
                    art++;
            }

            return art >= (bottom - top) * 0.5f;
        }

        var left = 0;
        while (left < width / 2 && !IsArt(left))
            left++;

        var right = 0;
        while (right < width / 2 && !IsArt(width - 1 - right))
            right++;

        return (Math.Min(left / (float)width, 0.3f), Math.Min(right / (float)width, 0.3f));
    }
}
