
namespace NahjaUI.Core.Ui;

/// <summary>
/// The scale of everything NahjaUI draws over the game (bars, frames, trackers, gauges, texts, nameplates): the "Interface scale" chosen in Appearance,
/// in screen pixels. The scale of Dalamud does not change it. The windows of NahjaUI (the menu) do follow the scale of Dalamud.
/// </summary>
internal static class UiScale
{
    public const float MinInterface = 0.5f;
    public const float MaxInterface = 6f;

    /// <summary>The "Interface scale" of Appearance: what a logical pixel of an overlay is worth in screen pixels (1 = one). Set by the plugin every frame.</summary>
    public static float Interface { get; set; } = 1f;

    /// <summary>"Pixel perfect" of the Layout page: offsets are shown and edited in real screen pixels. Set by the plugin every frame.</summary>
    public static bool PixelPerfect { get; set; }

    /// <summary>What a logical pixel of an overlay is worth in physical pixels.</summary>
    public static float Overlay => Interface;
}
