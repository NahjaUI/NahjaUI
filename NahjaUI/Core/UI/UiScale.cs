using Dalamud.Interface.Utility;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The scale of everything NahjaUI draws over the game: the one of Dalamud (Dalamud settings, "UI scale"). NahjaUI has no scale of its own.
/// </summary>
internal static class UiScale
{
    /// <summary>What a logical pixel of an overlay is worth in physical pixels: the scale of Dalamud.</summary>
    public static float Overlay => ImGuiHelpers.GlobalScale;
}
