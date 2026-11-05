using System;
using System.Collections.Generic;
using Dalamud.Interface.Textures;

namespace NahjaUI.Game;

/// <summary>
/// Measures how much empty (transparent or dark) margin a game icon has at its left and right, so a tracker can cut it away
/// and fill the width of its box with the art. Measured once per icon, from the image file of the game.
/// </summary>
internal static class IconMargins
{
    private static readonly Dictionary<uint, (float Left, float Top, float Right, float Bottom)> Cache = new();

    /// <summary>The fraction (0 to 0.3) of the width / height to cut on each side. All 0 when it cannot be measured.</summary>
    public static (float Left, float Top, float Right, float Bottom) Get(uint iconId)
    {
        if (iconId == 0)
            return (0f, 0f, 0f, 0f);

        if (Cache.TryGetValue(iconId, out var known))
            return known;

        var result = (0f, 0f, 0f, 0f);

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

    private static (float, float, float, float) Measure(uint iconId)
    {
        var path = Plugin.TextureProvider.GetIconPath(new GameIconLookup(iconId));
        if (string.IsNullOrEmpty(path))
            return (0f, 0f, 0f, 0f);

        var tex = Plugin.DataManager.GetFile<Lumina.Data.Files.TexFile>(path);
        if (tex is null)
            return (0f, 0f, 0f, 0f);

        var width = (int)tex.Header.Width;
        var height = (int)tex.Header.Height;
        var data = tex.ImageData;
        if (width < 4 || height < 4 || data.Length < width * height * 4)
            return (0f, 0f, 0f, 0f);

        // A line (column or row) belongs to the art when enough of its pixels, in the middle part of the icon (away from the
        // rounded corners), are opaque and not nearly black.
        var from = width / 5;
        var toX = width - width / 5;
        var top = height / 5;
        var bottom = height - height / 5;

        bool IsArt(int x, int y)
        {
            var i = (y * width + x) * 4;
            var brightness = Math.Max(data[i], Math.Max(data[i + 1], data[i + 2]));
            return data[i + 3] > 128 && brightness > 40;
        }

        bool ColumnIsArt(int x)
        {
            var art = 0;
            for (var y = top; y < bottom; y++)
                art += IsArt(x, y) ? 1 : 0;

            return art >= (bottom - top) * 0.5f;
        }

        bool RowIsArt(int y)
        {
            var art = 0;
            for (var x = from; x < toX; x++)
                art += IsArt(x, y) ? 1 : 0;

            return art >= (toX - from) * 0.5f;
        }

        var left = 0;
        while (left < width / 2 && !ColumnIsArt(left))
            left++;

        var right = 0;
        while (right < width / 2 && !ColumnIsArt(width - 1 - right))
            right++;

        var up = 0;
        while (up < height / 2 && !RowIsArt(up))
            up++;

        var down = 0;
        while (down < height / 2 && !RowIsArt(height - 1 - down))
            down++;

        return (Math.Min(left / (float)width, 0.3f), Math.Min(up / (float)height, 0.3f),
            Math.Min(right / (float)width, 0.3f), Math.Min(down / (float)height, 0.3f));
    }
}
