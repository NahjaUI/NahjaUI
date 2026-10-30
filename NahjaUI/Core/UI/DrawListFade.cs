using System;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// Makes everything drawn into a draw list after a mark more transparent (the colors of the new vertices get their alpha
/// multiplied). Used to fade a whole row (bar, border, texts) without touching every single draw call.
/// </summary>
internal static class DrawListFade
{
    /// <summary>Remember where we are: everything drawn after this call can be faded.</summary>
    public static int Mark(ImDrawListPtr drawList) => drawList.VtxBuffer.Size;

    /// <summary>Multiply the alpha of everything drawn since <paramref name="mark"/> by <paramref name="alpha"/> (0 to 1).</summary>
    public static unsafe void FadeSince(ImDrawListPtr drawList, int mark, float alpha)
    {
        var vertices = drawList.VtxBuffer.Data;
        var count = drawList.VtxBuffer.Size;
        if (vertices == null || mark < 0 || mark >= count)
            return;

        for (var i = mark; i < count; i++)
        {
            var color = vertices[i].Col;
            var faded = (uint)MathF.Round(((color >> 24) & 0xFFu) * alpha);
            vertices[i].Col = (color & 0x00FFFFFFu) | (faded << 24);
        }
    }
}
