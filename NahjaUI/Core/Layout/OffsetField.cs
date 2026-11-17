using System;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Ui;

namespace NahjaUI.Core.Layout;

/// <summary>
/// The X/Y fields of an element's position. The offset is saved in logical pixels (it can have decimals); with "Pixel perfect" on
/// the field shows and takes real screen pixels (at a scale of 2, one real pixel is 0.5 logical pixels), so a gap of exactly one
/// pixel can be asked for at any scale.
/// </summary>
internal static class OffsetField
{
    /// <summary>The offset as the field shows it.</summary>
    public static int ToField(float offset) =>
        (int)MathF.Round(UiScale.PixelPerfect ? offset * UiScale.Overlay : offset);

    /// <summary>The offset to save for a value typed in the field.</summary>
    public static float FromField(int value) =>
        UiScale.PixelPerfect ? value / UiScale.Overlay : value;

    /// <summary>The label unit: real pixels with Pixel perfect on, logical ones otherwise.</summary>
    public static string Unit => UiScale.PixelPerfect ? "real px" : "px";

    /// <summary>
    /// An integer field for one offset. <paramref name="invert"/> turns the sign (the saved Y counts downwards, the field counts upwards).
    /// Returns true when the value was changed.
    /// </summary>
    public static bool Draw(string label, ref float offset, bool invert)
    {
        var value = ToField(invert ? -offset : offset);
        if (!ImGui.InputInt(label, ref value, 1, 5))
            return false;

        var saved = FromField(value);
        offset = invert ? -saved : saved;
        return true;
    }
}
