using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace NahjaUI.Core.Ui;

/// <summary>The fonts an aura can use for its timer and counters. All of them come from the game itself.</summary>
public enum OverlayFont
{
    /// <summary>The game's bold number font (Meidinger). Digits all have the same width.</summary>
    GameNumbers,

    /// <summary>The game's normal interface font (Axis). Clean and neutral.</summary>
    GameUi,

    /// <summary>The game's interface font in bold.</summary>
    GameUiBold,

    /// <summary>A narrow, bold font (Trump Gothic). Fits more text in a small icon.</summary>
    Condensed
}

/// <summary>
/// The game's own fonts at a few fixed sizes, for timers and counters drawn on icons.
/// Every size is a real font built by Dalamud, never a stretched one, so text is always sharp.
/// A font family is prepared the first time something asks for it (they load in the background);
/// until it is ready, the game number font (or the normal font) is used instead.
/// </summary>
internal sealed class OverlayFonts : IDisposable
{
    /// <summary>The font sizes (in pixels) that are prepared.</summary>
    public static IReadOnlyList<int> AvailableSizes { get; } = new[] { 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 42, 48 };

    private static readonly float[] Sizes = AvailableSizes.Select(size => (float)size).ToArray();

    private readonly Dictionary<OverlayFont, IFontHandle[]> sets = new();

    public OverlayFonts()
    {
        // The default family is prepared right away.
        GetSet(OverlayFont.GameNumbers);
    }

    /// <summary>
    /// Makes ImGui use the font of the given family closest to <paramref name="wantedPx"/> until the returned value
    /// is disposed. Returns null (and changes nothing) when no game font is ready yet; <c>using (null)</c> is fine in C#.
    /// </summary>
    public IDisposable? PushClosest(OverlayFont font, float wantedPx)
    {
        var handle = FindClosest(GetSet(font), wantedPx);

        // The family is still loading: use the default one meanwhile.
        if (handle is null && font != OverlayFont.GameNumbers)
            handle = FindClosest(GetSet(OverlayFont.GameNumbers), wantedPx);

        return handle?.Push();
    }

    private static IFontHandle? FindClosest(IFontHandle[] set, float wantedPx)
    {
        IFontHandle? best = null;
        var bestDifference = float.MaxValue;

        for (var i = 0; i < set.Length; i++)
        {
            if (!set[i].Available)
                continue;

            var difference = MathF.Abs(Sizes[i] - wantedPx);
            if (difference < bestDifference)
            {
                bestDifference = difference;
                best = set[i];
            }
        }

        return best;
    }

    private IFontHandle[] GetSet(OverlayFont font)
    {
        if (sets.TryGetValue(font, out var existing))
            return existing;

        var atlas = Plugin.PluginInterface.UiBuilder.FontAtlas;

        var family = font switch
        {
            OverlayFont.GameUi or OverlayFont.GameUiBold => GameFontFamily.Axis,
            OverlayFont.Condensed => GameFontFamily.TrumpGothic,
            _ => GameFontFamily.MiedingerMid
        };

        var bold = font == OverlayFont.GameUiBold;

        var created = Sizes
            .Select(size =>
            {
                var style = new GameFontStyle(family, size);
                if (bold)
                    style.Bold = true;

                return atlas.NewGameFontHandle(style);
            })
            .ToArray();

        sets[font] = created;
        return created;
    }

    public void Dispose()
    {
        foreach (var set in sets.Values)
        {
            foreach (var handle in set)
                handle.Dispose();
        }

        sets.Clear();
    }
}
