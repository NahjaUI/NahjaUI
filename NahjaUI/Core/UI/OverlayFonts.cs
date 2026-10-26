using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace NahjaUI.Core.Ui;

/// <summary>The fonts the interface and the overlays can use.</summary>
public enum OverlayFont
{
    /// <summary>The game's bold number font (Meidinger). Digits all have the same width.</summary>
    GameNumbers,

    /// <summary>The game's normal interface font (Axis). Clean and neutral.</summary>
    GameUi,

    /// <summary>The game's interface font in bold.</summary>
    GameUiBold,

    /// <summary>A narrow, bold font (Trump Gothic). Fits more text in a small icon.</summary>
    Condensed,

    /// <summary>NahjaUI display font: Saira Condensed SemiBold. For names and cast bar texts.</summary>
    NahjaDisplay,

    /// <summary>NahjaUI number font: Saira Condensed Bold. For timers, stacks and numbers.</summary>
    NahjaDisplayBold,

    /// <summary>NahjaUI text font: Saira Regular. For menus, chat and labels.</summary>
    NahjaText
}

/// <summary>
/// The fonts of NahjaUI (Saira, stored inside the plugin) and the game's own fonts, at a few fixed sizes.
/// Every size is a real font built by Dalamud, never a stretched one, so text is always sharp.
/// A font family is prepared the first time something asks for it (they load in the background);
/// until it is ready, the default NahjaUI font is used instead.
/// </summary>
internal sealed class OverlayFonts : IDisposable
{
    /// <summary>The font the interface uses by default, and the fallback while another one is loading.</summary>
    public const OverlayFont Default = OverlayFont.NahjaDisplayBold;

    /// <summary>The font sizes (in pixels) a user can choose for overlays.</summary>
    public static IReadOnlyList<int> AvailableSizes { get; } = new[] { 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 42, 48 };

    /// <summary>The sizes the menu text can use. They are all prepared.</summary>
    public static IReadOnlyList<int> MenuTextSizes { get; } = new[] { 12, 13, 14, 15, 16, 17, 18, 20 };

    // Every size that is built: the overlay sizes plus the extra menu sizes.
    private static readonly float[] Sizes = AvailableSizes.Concat(MenuTextSizes).Distinct().OrderBy(size => size).Select(size => (float)size).ToArray();

    // The letters the NahjaUI fonts contain: Latin with accents, common punctuation and the euro sign.
    // Pairs of first and last character, and a closing zero (the format ImGui expects).
    private static readonly ushort[] LatinRanges =
    {
        0x0020, 0x007E, 0x00A0, 0x00FF, 0x0100, 0x017F, 0x2010, 0x2027, 0x2039, 0x203A, 0x20AC, 0x20AC, 0
    };

    private readonly Dictionary<OverlayFont, IFontHandle[]> sets = new();

    /// <summary>The fonts in use, so the theme (which has no reference to the plugin) can reach them.</summary>
    public static OverlayFonts? Current { get; private set; }

    public OverlayFonts()
    {
        Current = this;

        // The NahjaUI families are prepared right away.
        GetSet(OverlayFont.NahjaDisplayBold);
        GetSet(OverlayFont.NahjaText);
    }

    /// <summary>
    /// Makes ImGui use the font of the given family closest to <paramref name="wantedPx"/> until the returned value
    /// is disposed. The size is in LOGICAL pixels (what the user chose): Dalamud builds these fonts already multiplied by
    /// the UI scale, so never multiply it by ImGuiHelpers.GlobalScale before calling this. Returns null (and changes nothing) when no font is ready yet; <c>using (null)</c> is fine in C#.
    /// </summary>
    public IDisposable? PushClosest(OverlayFont font, float wantedPx)
    {
        var handle = FindClosest(GetSet(font), wantedPx);

        // The family is still loading: use the default one meanwhile.
        if (handle is null && font != Default)
            handle = FindClosest(GetSet(Default), wantedPx);

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
        IFontHandle[] created;

        if (font is OverlayFont.NahjaDisplay or OverlayFont.NahjaDisplayBold or OverlayFont.NahjaText)
        {
            // The font file is stored inside the plugin (see EmbeddedFonts).
            var data = font switch
            {
                OverlayFont.NahjaDisplay => EmbeddedFonts.DisplaySemiBold(),
                OverlayFont.NahjaDisplayBold => EmbeddedFonts.DisplayBold(),
                _ => EmbeddedFonts.TextRegular()
            };

            created = Sizes
                .Select(size => atlas.NewDelegateFontHandle(toolkit => toolkit.OnPreBuild(preBuild =>
                {
                    // The size is logical: Dalamud multiplies it by the UI scale while building.
                    var config = new SafeFontConfig { SizePx = size, GlyphRanges = LatinRanges };
                    preBuild.AddFontFromMemory(data, config, $"NahjaUI {font} {size}px");
                })))
                .ToArray();
        }
        else
        {
            var family = font switch
            {
                OverlayFont.GameUi or OverlayFont.GameUiBold => GameFontFamily.Axis,
                OverlayFont.Condensed => GameFontFamily.TrumpGothic,
                _ => GameFontFamily.MiedingerMid
            };

            var bold = font == OverlayFont.GameUiBold;

            created = Sizes
                .Select(size =>
                {
                    var style = new GameFontStyle(family, size);
                    if (bold)
                        style.Bold = true;

                    return atlas.NewGameFontHandle(style);
                })
                .ToArray();
        }

        sets[font] = created;
        return created;
    }

    public void Dispose()
    {
        if (Current == this)
            Current = null;

        foreach (var set in sets.Values)
        {
            foreach (var handle in set)
                handle.Dispose();
        }

        sets.Clear();
    }
}
