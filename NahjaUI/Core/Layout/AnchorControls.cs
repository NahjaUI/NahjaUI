using System.Linq;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Layout;

/// <summary>
/// The controls that say where an element is attached and which points are joined (the anchor of the screen or of the element it
/// is attached to, and the anchor of the element itself), plus its width and height. Shared by the Layout page and unlock mode.
/// </summary>
internal static class AnchorControls
{
    /// <summary>"Attach to", "Screen anchor" and "Element anchor". Returns true when something changed.</summary>
    public static bool DrawAnchors(LayoutManager layout, LayoutElement element, float width = 0f)
    {
        var data = element.Layout;
        var changed = false;

        var attachName = "Screen";
        if (!string.IsNullOrEmpty(data.AttachTo))
            attachName = layout.Elements.FirstOrDefault(e => e.Id == data.AttachTo)?.Name ?? "Screen";

        if (width > 0f)
            ImGui.SetNextItemWidth(width);

        if (ImGui.BeginCombo("Attach to", attachName))
        {
            if (ImGui.Selectable("Screen", string.IsNullOrEmpty(data.AttachTo)))
            {
                data.AttachTo = null;
                changed = true;
            }

            // In alphabetical order, so the one you look for is easy to find.
            foreach (var other in layout.Elements.OrderBy(e => e.Name, System.StringComparer.CurrentCultureIgnoreCase))
            {
                // Only what is in use: not the groups of other jobs (the one it is attached to now stays in the list).
                if (ReferenceEquals(other, element) || other.IsManaged || (other.IsHidden && data.AttachTo != other.Id))
                    continue;

                if (ImGui.Selectable($"{other.Name}##attach_{other.Id}", data.AttachTo == other.Id))
                {
                    data.AttachTo = other.Id;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        var screenAnchor = data.ScreenAnchor;
        if (AnchorCombo(string.IsNullOrEmpty(data.AttachTo) ? "Screen anchor" : "Anchor on that element", ref screenAnchor, width))
        {
            data.ScreenAnchor = screenAnchor;
            changed = true;
        }

        var selfAnchor = data.SelfAnchor;
        if (AnchorCombo("Element anchor", ref selfAnchor, width))
        {
            data.SelfAnchor = selfAnchor;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Width and height fields, in logical pixels. They are not shown for elements whose size is decided by something else
    /// (tracker groups take it from their icons, status effects from their icon settings). Returns true when something changed.
    /// </summary>
    public static bool DrawSize(LayoutElement element, float fieldWidth)
    {
        if (element.PlacementOnly || element.SizeIsAutomatic)
            return false;

        var data = element.Layout;
        var changed = false;

        ImGui.SetNextItemWidth(fieldWidth);
        var width = data.Width;
        if (NahjaUI.Core.Ui.CommitInput.Int("Width", ref width, 1, 10) && !data.Locked)
        {
            data.Width = System.Math.Clamp(width, 1, 4000);
            changed = true;
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(fieldWidth);
        var height = data.Height;
        if (NahjaUI.Core.Ui.CommitInput.Int("Height", ref height, 1, 10) && !data.Locked)
        {
            data.Height = System.Math.Clamp(height, 1, 1000);
            changed = true;
        }

        return changed;
    }

    private static bool AnchorCombo(string label, ref AnchorPoint value, float width)
    {
        var changed = false;

        if (width > 0f)
            ImGui.SetNextItemWidth(width);

        if (ImGui.BeginCombo(label, value.Label(), ImGuiComboFlags.None))
        {
            foreach (var point in AnchorPointExtensions.All)
            {
                if (ImGui.Selectable(point.Label(), point == value))
                {
                    value = point;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }
}
