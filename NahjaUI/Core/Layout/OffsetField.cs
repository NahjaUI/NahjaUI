using System;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Layout;

/// <summary>
/// The X/Y fields of an element's position, in logical pixels (the scale of Dalamud makes them bigger or smaller on the screen).
/// </summary>
internal static class OffsetField
{
    /// <summary>The offset as the field shows it.</summary>
    public static int ToField(float offset) => (int)MathF.Round(offset);

    /// <summary>The offset to save for a value typed in the field.</summary>
    public static float FromField(int value) => value;

    /// <summary>The label unit.</summary>
    public static string Unit => "px";

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
