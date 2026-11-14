using System;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The "Corner rounding" control that every bar has: off, it follows the value of the Appearance page; on, it has its own.
/// </summary>
public static class RoundingControl
{
    /// <summary>Draws the checkbox (and the slider while it is on). Returns true when something changed.</summary>
    /// <param name="custom">The own value of the bar, or null when it follows the Appearance page.</param>
    /// <param name="effective">The value in use right now.</param>
    public static bool Draw(string id, int? custom, int effective, int max, Action<int?> set)
    {
        var changed = false;
        var on = custom.HasValue;
        if (ImGui.Checkbox($"Custom corner rounding##cr_{id}", ref on))
        {
            set(on ? Math.Min(effective, max) : null);
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Off: it uses the corner rounding of the Appearance page, the same one for the whole interface.");

        if (!on)
            return changed;

        var value = Math.Min(custom ?? effective, max);
        ImGui.Indent();
        if (ImGui.SliderInt($"Corner rounding (px)##{id}", ref value, 0, max))
        {
            set(value);
            changed = true;
        }

        ImGui.Unindent();
        return changed;
    }
}
