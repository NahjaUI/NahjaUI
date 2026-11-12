using System;
using Dalamud.Bindings.ImGui;
using System.Linq;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.HudBars;
using NahjaUI.Modules.PartyListBar;

namespace NahjaUI.Modules.AllianceList;

/// <summary>The cards of the Alliance List tab of the HUD bars page.</summary>
internal sealed class AllianceListEditor
{
    private readonly Plugin plugin;

    public AllianceListEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        plugin.AllianceListSystem.PreviewRequested = true;
        var settings = plugin.Configuration.HudBars.Alliance;

        DrawGeneral(settings);
        DrawOptions(settings);
        DrawDeadText(settings);
        DrawDiagnostics();
    }

    private void DrawGeneral(AllianceListSettings settings)
    {
        Widgets.Card("##card_al_general", 10, () =>
        {
            Widgets.SectionLabel("Alliance List");

            var enabled = settings.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }

            Widgets.EnabledHint("Hides the game's Alliance List 1 and 2, and shows one grid with all the other members of the alliance.");

            ImGui.Spacing();

            // The size is the size of one cell. It belongs to the layout system, like the position.
            var layout = plugin.AllianceListSystem.AllianceElement.Layout;

            var width = layout.Width;
            if (ImGui.InputInt("Width (px)", ref width, 2, 10))
            {
                layout.Width = Math.Clamp(width, 1, 4000);
                plugin.RequestSave();
            }

            var height = layout.Height;
            if (ImGui.InputInt("Height (px)", ref height, 2, 10))
            {
                layout.Height = Math.Clamp(height, 1, 1000);
                plugin.RequestSave();
            }

            ImGui.Spacing();

            // Columns and rows are linked: the number of slots is fixed, so changing one changes the other.
            var slots = plugin.GameData.Current.Alliance?.Count ?? AllianceListModule.NormalSlots;
            var columns = Math.Clamp(settings.Columns, 1, slots);
            var rows = (slots + columns - 1) / columns;

            if (ImGui.SliderInt("Columns", ref columns, 1, slots))
            {
                settings.Columns = Math.Clamp(columns, 1, slots);
                plugin.RequestSave();
            }

            if (ImGui.SliderInt("Rows", ref rows, 1, slots))
            {
                settings.Columns = Math.Clamp((slots + Math.Clamp(rows, 1, slots) - 1) / Math.Clamp(rows, 1, slots), 1, slots);
                plugin.RequestSave();
            }

            var spacing = settings.SpacingPx;
            if (ImGui.SliderInt("Space between cells (px)", ref spacing, 0, 30))
            {
                settings.SpacingPx = spacing;
                plugin.RequestSave();
            }
        });
    }

    // The word over a dead member: the same controls as every other text.
    private void DrawDeadText(AllianceListSettings settings)
    {
        Widgets.Card("##card_al_dead", 11, () =>
        {
            Widgets.SectionLabel("Dead text");

            var show = settings.DeadText.Enabled;
            if (ImGui.Checkbox("Show the word Dead over a dead member", ref show))
            {
                settings.DeadText.Enabled = show;
                plugin.RequestSave();
            }

            ImGui.TextColored(Theme.TextMuted, "If the cell is too narrow for the word, it ends in ... so it never goes out of the cell.");

            if (HudBarEditor.TextControls(settings.DeadText))
                plugin.RequestSave();
        });
    }

    // What the game gives right now, to see why the grid shows (or does not show) members.
    private void DrawDiagnostics()
    {
        Widgets.Card("##card_al_diagnostics", 5, () =>
        {
            Widgets.SectionLabel("What the game says right now");

            var list = Plugin.PartyList;
            var filled = 0;

            if (list.IsAlliance)
            {
                for (var i = 0; i < (plugin.GameData.Current.Alliance?.Count ?? 16); i++)
                {
                    var member = list.CreateAllianceMemberReference(list.GetAllianceMemberAddress(i));
                    if (member is not null && member.EntityId != 0)
                        filled++;
                }
            }

            var shown = plugin.GameData.Current.Alliance?.Count(unit => unit is not null) ?? 0;
            ImGui.TextColored(Theme.TextMuted, $"You are in an alliance: {(list.IsAlliance ? "yes" : "no")}");
            ImGui.TextColored(Theme.TextMuted, $"Members of your own party: {list.Length}");
            ImGui.TextColored(Theme.TextMuted, $"Alliance slots with a member: {filled}");
            ImGui.TextColored(Theme.TextMuted, $"Cells drawn: {shown}");
        });
    }

    private void DrawOptions(AllianceListSettings settings)
    {
        Widgets.Card("##card_al_options", 24, () =>
        {
            Widgets.SectionLabel("Alliance options");

            var awayOpacity = settings.AwayOpacityPercent;
            if (ImGui.SliderInt("Opacity when dead, offline, away or in a cutscene (%)", ref awayOpacity, 10, 100))
            {
                settings.AwayOpacityPercent = awayOpacity;
                plugin.RequestSave();
            }

            var showJobIcon = settings.ShowJobIcon;
            if (ImGui.Checkbox("Show the job icon", ref showJobIcon))
            {
                settings.ShowJobIcon = showJobIcon;
                plugin.RequestSave();
            }

            if (settings.ShowJobIcon)
            {
                var iconStyle = settings.JobIconStyle;
                if (HudBarEditor.EnumCombo("Icon set", ref iconStyle, style => style == PartyJobIconStyle.Plain ? "Plain" : "Light"))
                {
                    settings.JobIconStyle = iconStyle;
                    plugin.RequestSave();
                }

                var iconSize = settings.JobIconSizePx;
                if (ImGui.SliderInt("Icon size (px)", ref iconSize, 8, 64))
                {
                    settings.JobIconSizePx = iconSize;
                    plugin.RequestSave();
                }

                var iconBarAnchor = settings.JobIconBarAnchor;
                if (HudBarEditor.EnumCombo("Anchor on the cell", ref iconBarAnchor, point => point.Label()))
                {
                    settings.JobIconBarAnchor = iconBarAnchor;
                    plugin.RequestSave();
                }

                var iconSelfAnchor = settings.JobIconSelfAnchor;
                if (HudBarEditor.EnumCombo("Anchor of the icon", ref iconSelfAnchor, point => point.Label()))
                {
                    settings.JobIconSelfAnchor = iconSelfAnchor;
                    plugin.RequestSave();
                }

                var iconOffsetX = settings.JobIconOffsetX;
                if (ImGui.InputInt("Offset X (px)", ref iconOffsetX, 1, 5))
                {
                    settings.JobIconOffsetX = Math.Clamp(iconOffsetX, -500, 500);
                    plugin.RequestSave();
                }

                // Up is plus (the saved value counts downwards).
                var iconOffsetY = -settings.JobIconOffsetY;
                if (ImGui.InputInt("Offset Y (px)", ref iconOffsetY, 1, 5))
                {
                    settings.JobIconOffsetY = -Math.Clamp(iconOffsetY, -500, 500);
                    plugin.RequestSave();
                }
            }

            var showRaise = settings.ShowRaiseIcon;
            if (ImGui.Checkbox("Show the Raise icon on a dead member who has a Raise waiting", ref showRaise))
            {
                settings.ShowRaiseIcon = showRaise;
                plugin.RequestSave();
            }

            if (settings.ShowRaiseIcon)
            {
                var raiseWidth = settings.RaiseIconWidthPx;
                if (ImGui.InputInt("Raise icon width (px)", ref raiseWidth, 2, 10))
                {
                    settings.RaiseIconWidthPx = Math.Clamp(raiseWidth, 4, 200);
                    plugin.RequestSave();
                }

                var raiseHeight = settings.RaiseIconHeightPx;
                if (ImGui.InputInt("Raise icon height (px)", ref raiseHeight, 2, 10))
                {
                    settings.RaiseIconHeightPx = Math.Clamp(raiseHeight, 4, 200);
                    plugin.RequestSave();
                }

                var raiseBarAnchor = settings.RaiseIconBarAnchor;
                if (HudBarEditor.EnumCombo("Raise icon anchor on the cell", ref raiseBarAnchor, point => point.Label()))
                {
                    settings.RaiseIconBarAnchor = raiseBarAnchor;
                    plugin.RequestSave();
                }

                var raiseSelfAnchor = settings.RaiseIconSelfAnchor;
                if (HudBarEditor.EnumCombo("Raise icon anchor of the icon", ref raiseSelfAnchor, point => point.Label()))
                {
                    settings.RaiseIconSelfAnchor = raiseSelfAnchor;
                    plugin.RequestSave();
                }

                var raiseOffsetX = settings.RaiseIconOffsetX;
                if (ImGui.InputInt("Raise icon offset X (px)", ref raiseOffsetX, 1, 5))
                {
                    settings.RaiseIconOffsetX = Math.Clamp(raiseOffsetX, -500, 500);
                    plugin.RequestSave();
                }

                var raiseOffsetY = -settings.RaiseIconOffsetY;
                if (ImGui.InputInt("Raise icon offset Y (px)", ref raiseOffsetY, 1, 5))
                {
                    settings.RaiseIconOffsetY = -Math.Clamp(raiseOffsetY, -500, 500);
                    plugin.RequestSave();
                }
            }

            var showName = settings.ShowNameOnHover;
            if (ImGui.Checkbox("Show the name while the mouse is over a cell", ref showName))
            {
                settings.ShowNameOnHover = showName;
                plugin.RequestSave();
            }
        });
    }
}
