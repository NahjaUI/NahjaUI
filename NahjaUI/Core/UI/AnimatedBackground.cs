using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// A slow, soft animated background for the windows of the plugin: a deep indigo base with a few large blurred spots of color that drift around
/// on their own paths. Each spot is a stack of translucent circles (a radial fade), so nothing needs a texture or a shader. It is only drawn
/// while the window is open, and the user can turn it off (Appearance): then the plain background is drawn. Used by the setup window and the menu
/// (the menu uses it fainter, so the text of its cards stays easy to read).
/// </summary>
internal static class AnimatedBackground
{
    // Where each spot lives: its color, the point it moves around (as a fraction of the window), how far it moves, how fast (radians per
    // second on each axis), its radius (as a fraction of the width) and how strong it is.
    private readonly record struct Spot(Vector4 Color, float CenterX, float CenterY, float RangeX, float RangeY, float SpeedX, float SpeedY, float Phase, float Radius, float Strength);

    private static readonly Spot[] Spots =
    {
        new(Theme.Accent, 0.18f, 0.22f, 0.16f, 0.14f, 0.23f, 0.17f, 0.0f, 0.62f, 0.50f),
        new(Theme.Rgb(84, 170, 235), 0.82f, 0.30f, 0.14f, 0.18f, 0.19f, 0.27f, 1.9f, 0.55f, 0.40f),
        new(Theme.Rgb(206, 112, 232), 0.55f, 0.88f, 0.22f, 0.10f, 0.15f, 0.21f, 3.6f, 0.60f, 0.38f),
        new(Theme.Rgb(70, 62, 210), 0.30f, 0.70f, 0.18f, 0.16f, 0.26f, 0.13f, 5.1f, 0.58f, 0.46f),
        new(Theme.Rgb(70, 205, 200), 0.90f, 0.85f, 0.10f, 0.12f, 0.17f, 0.24f, 0.8f, 0.40f, 0.22f)
    };

    // How many circles make one spot. More is smoother, and costs more triangles.
    private const int Layers = 14;

    /// <summary>Draws the background inside the box. <paramref name="rounding"/> and <paramref name="flags"/> are the corners of the base (the spots are cut a little inside the corners).</summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, ImDrawFlags flags, bool animated, float strength = 1f)
    {
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Background), rounding, flags);

        if (!animated)
            return;

        var size = max - min;
        if (size.X < 8f || size.Y < 8f)
            return;

        // The spots are cut to the box, a little inside so they never reach past a rounded corner.
        var inset = MathF.Round(rounding * 0.3f);
        drawList.PushClipRect(min + new Vector2(inset), max - new Vector2(inset), true);

        var time = (float)ImGui.GetTime();
        foreach (var spot in Spots)
        {
            var x = spot.CenterX + spot.RangeX * MathF.Sin(time * spot.SpeedX + spot.Phase);
            var y = spot.CenterY + spot.RangeY * MathF.Sin(time * spot.SpeedY + spot.Phase * 1.7f);
            var center = new Vector2(min.X + size.X * x, min.Y + size.Y * y);
            var radius = size.X * spot.Radius;

            // From the biggest and faintest circle to the smallest: the middle of the spot is where they pile up.
            var alpha = spot.Strength * strength / 12f;
            var color = ImGui.GetColorU32(new Vector4(spot.Color.X, spot.Color.Y, spot.Color.Z, alpha));
            for (var i = 0; i < Layers; i++)
            {
                var fraction = 1f - i / (float)Layers;
                drawList.AddCircleFilled(center, radius * fraction, color, 40);
            }
        }

        drawList.PopClipRect();
    }
}
