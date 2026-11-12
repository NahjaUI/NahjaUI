using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.HudBars;

namespace NahjaUI.Modules.StatusEffects;

/// <summary>
/// The "Status effects" page: your enhancements and enfeeblements drawn by NahjaUI in two groups, whatever the game's own layout option
/// says. Each group has its own box, its row layout and the look of its icons.
/// </summary>
internal sealed class StatusEffectsEditor
{
    private readonly Plugin plugin;

    private StatusGroupKind selected = StatusGroupKind.Enhancements;

    public StatusEffectsEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var settings = plugin.Configuration.StatusEffects;

        // While this page is open, the groups are drawn (with samples if there is nothing to show).
        plugin.StatusEffectsSystem.PreviewRequested = true;

        DrawMain(settings);
        DrawPicker();

        var kind = selected;
        var group = settings.For(kind);
        DrawGroupCard(group, kind);
        DrawIconsCard(group.Look, kind);
        DrawTimeCard(group.Look, kind);
        DrawStacksCard(group.Look, kind);
    }

    // ---------------------------------------------------------------- main switch

    private void DrawMain(StatusEffectsSettings settings)
    {
        Widgets.Card("##card_status_main", 3, () =>
        {
            Widgets.SectionLabel("Status effects");

            var enabled = settings.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }
            Widgets.EnabledHint();

            ImGui.TextColored(Theme.TextMuted, "Right click on one of your own statuses cancels it, as in the game.");
        });
    }

    // ---------------------------------------------------------------- group picker

    private void DrawPicker()
    {
        Widgets.Card("##card_status_picker", 2, () =>
        {
            Widgets.SectionLabel("Group");

            var value = selected;
            if (HudBarEditor.EnumCombo("Group to edit", ref value, GroupLabel))
                selected = value;
        });
    }

    private static string GroupLabel(StatusGroupKind kind) => kind == StatusGroupKind.Enhancements ? "Enhancements" : "Enfeeblements";

    // ---------------------------------------------------------------- the group

    private void DrawGroupCard(StatusGroupSettings group, StatusGroupKind kind)
    {
        Widgets.Card($"##card_status_{kind}_group", 9, () =>
        {
            Widgets.SectionLabel("Rows");

            var enabled = group.Enabled;
            if (ImGui.Checkbox("Show this group", ref enabled))
            {
                group.Enabled = enabled;
                plugin.RequestSave();
            }

            var perLine = group.Look.IconsPerLine;
            if (ImGui.SliderInt("Icons per line", ref perLine, 1, 30))
            {
                group.Look.IconsPerLine = perLine;
                plugin.RequestSave();
            }

            var most = group.Look.MaxIcons;
            if (ImGui.SliderInt("Most icons shown", ref most, 1, 30))
            {
                group.Look.MaxIcons = most;
                plugin.RequestSave();
            }

            var order = group.Look.Order;
            if (HudBarEditor.EnumCombo("Order", ref order, HudBarEditor.OrderLabel))
            {
                group.Look.Order = order;
                plugin.RequestSave();
            }

            var growth = group.Look.Growth;
            if (HudBarEditor.EnumCombo("Grow towards", ref growth, HudBarEditor.GrowthLabel))
            {
                group.Look.Growth = growth;
                plugin.RequestSave();
            }

            if (HudBarEditor.NextLineCombo(group.Look))
                plugin.RequestSave();

        });
    }

    // ---------------------------------------------------------------- the icons

    private void DrawIconsCard(NahjaUI.Modules.PartyListBar.StatusRowSettings look, StatusGroupKind kind)
    {
        Widgets.Card($"##card_status_{kind}_icons", 8, () =>
        {
            Widgets.SectionLabel("Icons");

            var width = look.IconWidth;
            if (ImGui.InputInt("Icon width (px)", ref width, 2, 10))
            {
                look.IconWidth = Math.Clamp(width, 8, 128);
                plugin.RequestSave();
            }

            var height = look.IconHeight;
            if (ImGui.InputInt("Icon height (px)", ref height, 2, 10))
            {
                look.IconHeight = Math.Clamp(height, 8, 128);
                plugin.RequestSave();
            }

            var spacing = look.SpacingPx;
            if (ImGui.SliderInt("Space between icons (px)", ref spacing, 0, 30))
            {
                look.SpacingPx = spacing;
                plugin.RequestSave();
            }
        });
    }

    // ---------------------------------------------------------------- the time

    private void DrawTimeCard(NahjaUI.Modules.PartyListBar.StatusRowSettings look, StatusGroupKind kind)
    {
        Widgets.Card($"##card_status_{kind}_time", 9, () =>
        {
            Widgets.SectionLabel("Time left");

            var show = look.ShowTimer;
            if (ImGui.Checkbox("Show the time left", ref show))
            {
                look.ShowTimer = show;
                plugin.RequestSave();
            }

            if (HudBarEditor.StatusTextControls(look, true))
                plugin.RequestSave();
        });
    }

    // ---------------------------------------------------------------- the stacks

    private void DrawStacksCard(NahjaUI.Modules.PartyListBar.StatusRowSettings look, StatusGroupKind kind)
    {
        Widgets.Card($"##card_status_{kind}_stacks", 7, () =>
        {
            Widgets.SectionLabel("Stack count");

            var show = look.ShowStacks;
            if (ImGui.Checkbox("Show the stack count", ref show))
            {
                look.ShowStacks = show;
                plugin.RequestSave();
            }

            if (HudBarEditor.StatusTextControls(look, false))
                plugin.RequestSave();
        });
    }
}
