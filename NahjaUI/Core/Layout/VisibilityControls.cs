using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Layout;

/// <summary>The "show when" choices of an element (combat and weapon). One place draws them for the Layout page and for unlock mode.</summary>
internal static class VisibilityControls
{
    /// <summary>Draws the two combo boxes. Returns true when one of them changed (the caller saves the configuration).</summary>
    public static bool Draw(ElementLayout data, float width = 0f)
    {
        var changed = false;

        if (width > 0f)
            ImGui.SetNextItemWidth(width);

        if (ImGui.BeginCombo("Combat", LayoutManager.Label(data.ShowInCombat)))
        {
            foreach (var option in new[] { ShowCombat.Always, ShowCombat.InCombat, ShowCombat.OutOfCombat })
            {
                if (ImGui.Selectable(LayoutManager.Label(option), data.ShowInCombat == option))
                {
                    data.ShowInCombat = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        if (width > 0f)
            ImGui.SetNextItemWidth(width);

        if (ImGui.BeginCombo("Weapon", LayoutManager.Label(data.ShowWithWeapon)))
        {
            foreach (var option in new[] { ShowWeapon.Always, ShowWeapon.Drawn, ShowWeapon.Sheathed })
            {
                if (ImGui.Selectable(LayoutManager.Label(option), data.ShowWithWeapon == option))
                {
                    data.ShowWithWeapon = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }
}
