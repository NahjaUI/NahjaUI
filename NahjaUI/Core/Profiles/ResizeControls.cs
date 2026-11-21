using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Ui;

namespace NahjaUI.Core.Profiles;

/// <summary>
/// The controls to make the whole interface bigger or smaller (see <see cref="ConfigRescaler"/>): a slider from 50 to 200 % of the ORIGINAL
/// size. The interface follows the slider while it moves, and the result is saved when it is let go. Used by the Layout page of the menu and by the setup.
/// </summary>
internal static class ResizeControls
{
    /// <summary>Draws the controls. <paramref name="id"/> keeps the ids of the widgets apart when they are shown in two windows.</summary>
    public static void Draw(Plugin plugin, string id)
    {
        var rescaler = plugin.Rescaler;

        var percent = rescaler.AppliedPercent;
        ImGui.SetNextItemWidth(260f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt($"Size of everything (%)##resize_{id}", ref percent, ConfigRescaler.MinPercent, ConfigRescaler.MaxPercent))
            rescaler.Preview(percent);

        if (ImGui.IsItemDeactivatedAfterEdit())
            rescaler.Commit();

        ImGui.Spacing();
        if (rescaler.AppliedPercent != 100)
        {
            if (ImGui.Button($"Back to normal (100 %)##resize_normal_{id}"))
                rescaler.BackToNormal();

            ImGui.SameLine();
            ImGui.TextColored(Theme.TextMuted, $"Now at {rescaler.AppliedPercent} % of the normal size.");
        }
        else
        {
            ImGui.TextColored(Theme.TextMuted, "Now at the normal size (100 %).");
        }

        if (rescaler.Message.Length > 0)
            ImGui.TextWrapped(rescaler.Message);
    }
}
