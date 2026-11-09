using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;

namespace NahjaUI.Core.Ui;

/// <summary>The fonts the interface and the overlays can use.</summary>
public enum OverlayFont
{
    /// <summary>NahjaUI display font: Saira Condensed SemiBold. For names and progress bar texts.</summary>
    NahjaDisplay,

    /// <summary>NahjaUI number font: Saira Condensed Bold. For timers, stacks and numbers.</summary>
    NahjaDisplayBold,

    /// <summary>NahjaUI text font: Saira Regular. For menus, chat and labels.</summary>
    NahjaText
}

/// <summary>
/// The fonts of NahjaUI (Saira, stored inside the plugin), at a few fixed sizes. There are only these three: names and words use Display, small numbers use Numbers, menus use Text.
/// Every size is a real font built by Dalamud, never a stretched one, so text is always sharp.
/// A font family is prepared the first time something asks for it (they load in the background);
/// until it is ready, the default NahjaUI font is used instead.
/// </summary>
internal sealed class OverlayFonts : IDisposable
{
    /// <summary>The font the interface uses by default, and the fallback while another one is loading.</summary>
    public const OverlayFont Default = OverlayFont.NahjaDisplayBold;

        /// <summary>The sizes a user can choose for overlays: the HEIGHT OF THE LETTERS (capitals and digits) in pixels.</summary>
    public static IReadOnlyList<int> AvailableSizes { get; } = new[] { 6, 7, 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 42, 48 };

    // The font sizes that are really built (the letter size asked for is turned into one of these, then drawn exactly).
    private static readonly int[] BuiltOverlaySizes = { 8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 42, 48, 56, 64, 72 };

    /// <summary>The sizes the menu text can use. They are all prepared.</summary>
    public static IReadOnlyList<int> MenuTextSizes { get; } = new[] { 12, 13, 14, 15, 16, 17, 18, 20 };

    // Every size that is built: the overlay sizes plus the extra menu sizes.
    private static readonly float[] Sizes = BuiltOverlaySizes.Concat(MenuTextSizes).Distinct().OrderBy(size => size).Select(size => (float)size).ToArray();

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


    private readonly Dictionary<OverlayFont, (float Height, float Top)> capRatios = new();

    /// <summary>
    /// Makes ImGui use the given family and returns a <see cref="TextFace"/> whose letters are exactly <paramref name="capPx"/>
    /// LOGICAL pixels tall (the height of a capital letter or a digit, NOT the font's line box). So 24 is 24 and 12 is 12 in every frame.
    /// Dispose it when done.
    /// </summary>
    public unsafe TextFace PushCap(OverlayFont font, float capPx)
    {
        if (!capRatios.TryGetValue(font, out var ratios))
        {
            // Not measured yet: use rough values meanwhile.
            ratios = (0.7f, 0.15f);

            // Measure only with the family's OWN font. While it is still loading, another font (or Dalamud's default one)
            // would be measured instead, and a wrong ratio kept for the whole session makes every text of this family the wrong size.
            var own = FindClosest(GetSet(font), 20f);
            if (own is not null)
            {
                using (own.Push())
                {
                    var current = ImGui.GetFont();
                    var glyph = current.FindGlyph((ushort)'H');
                    if (glyph == null)
                        glyph = current.FindGlyph((ushort)'0');

                    // The glyph coordinates are in units of the font as built, so they are divided by the font's own built size
                    // (not by the size ImGui currently draws at, which carries a global scale of its own).
                    var fontSize = current.FontSize;
                    if (glyph != null && fontSize > 0f && glyph->Y1 > glyph->Y0)
                    {
                        ratios = ((glyph->Y1 - glyph->Y0) / fontSize, glyph->Y0 / fontSize);
                        capRatios[font] = ratios;
                    }
                }
            }
        }

        var wantedLine = capPx / ratios.Height;
        var scope = PushClosest(font, wantedLine);
        var size = wantedLine * ImGuiHelpers.GlobalScale;
        return new TextFace(scope, ImGui.GetFont(), size, size * ratios.Top, capPx * ImGuiHelpers.GlobalScale);
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

/// <summary>
/// A font set to a size where the LETTERS (capitals and digits) are the wanted height. Positions given to <see cref="Draw"/>
/// are the top-left corner of the visible letters, not of the font's line box.
/// </summary>
internal readonly struct TextFace : IDisposable
{
    private readonly IDisposable? scope;
    private readonly ImFontPtr font;
    private readonly float size;
    private readonly float capTop;

    /// <summary>The height of the letters, in physical pixels.</summary>
    public float Height { get; }

    public TextFace(IDisposable? scope, ImFontPtr font, float size, float capTop, float height)
    {
        this.scope = scope;
        this.font = font;
        this.size = size;
        this.capTop = capTop;
        Height = height;
    }

    /// <summary>The width of the text, in physical pixels.</summary>
    public float Width(string text)
    {
        return ImGui.CalcTextSizeA(font, size, float.MaxValue, 0f, text, out _).X;
    }

    /// <summary>The visible size of the text: its width and the height of its letters.</summary>
    public Vector2 Measure(string text) => new(Width(text), Height);

    /// <summary>Draws the text with a black outline. <paramref name="boxTopLeft"/> is the top-left of the letters.</summary>
    public void Draw(ImDrawListPtr drawList, Vector2 boxTopLeft, string text, uint color)
    {
        Draw(drawList, boxTopLeft, text, color, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f)));
    }

    /// <summary>Draws the text with an outline of the given colour.</summary>
    public void Draw(ImDrawListPtr drawList, Vector2 boxTopLeft, string text, uint color, uint outline)
    {
        var pos = new Vector2(MathF.Round(boxTopLeft.X), MathF.Round(boxTopLeft.Y - capTop));

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x != 0 || y != 0)
                    drawList.AddText(font, size, pos + new Vector2(x, y), outline, text);
            }
        }

        drawList.AddText(font, size, pos, color, text);
    }

    public void Dispose() => scope?.Dispose();
}
