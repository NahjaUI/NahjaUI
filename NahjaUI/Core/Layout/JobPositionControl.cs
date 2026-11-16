using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Core.Layout;

/// <summary>The "Use a different position for this job" choice of an element. One place draws it for the Layout page and for unlock mode.</summary>
internal static class JobPositionControl
{
    /// <summary>Draws the checkbox and a short note. Returns true when the choice changed (the caller saves the configuration).</summary>
    public static bool Draw(LayoutManager layout, LayoutElement element)
    {
        if (layout.CurrentJob == 0)
        {
            ImGui.TextColored(Theme.TextMuted, "Log in to give a job its own position.");
            return false;
        }

        var own = layout.HasOwnPosition(element);
        var changed = false;

        if (ImGui.Checkbox($"Use a different position for this job##own_{element.Id}", ref own))
        {
            layout.SetOwnPosition(element, own);
            changed = true;
        }

        if (own)
        {
            var zone = element.SeparatePvp ? (layout.InPvp ? " in PvP" : " in PvE") : string.Empty;
            ImGui.TextColored(Theme.TextMuted, $"Saved for {GameNames.JobAbbreviation(layout.CurrentJob)}{zone} only.");
        }
        else
        {
            var zone = element.SeparatePvp ? (layout.InPvp ? " (PvP)" : " (PvE)") : string.Empty;
            ImGui.TextColored(Theme.TextMuted, $"Same position for every job{zone}.");
        }

        return changed;
    }
}
