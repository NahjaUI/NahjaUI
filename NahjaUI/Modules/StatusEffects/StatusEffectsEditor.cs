using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI.Modules.StatusEffects;

/// <summary>
/// The "Status effects" page: your enhancements and enfeeblements drawn by NahjaUI. It follows the game's own layout option (HUD Layout >
/// Status Effects: one element, three groups or four groups); each group has its own box, its row layout and the look of its icons.
/// </summary>
internal sealed class StatusEffectsEditor
{
    private readonly Plugin plugin;

    // The group being edited. Until the user picks one, it is the first group the game's layout option uses.
    private StatusGroupKind? selected;

    public StatusEffectsEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var system = plugin.StatusEffectsSystem;
        var settings = plugin.Configuration.StatusEffects;
        var mode = system.CurrentMode;

        selected ??= FirstInUse(mode);
        var kind = selected.Value;

        // While this page is open, the group being edited is drawn (with samples if there is nothing to show).
        system.PreviewRequested = true;
        system.PreviewGroup = kind;

        DrawMain(settings, mode);
        DrawPicker(mode, ref kind);
        selected = kind;

        var group = settings.For(kind);
        DrawGroupCard(group, kind);
        DrawIconsCard(group.Look, kind);
        DrawTimeCard(group.Look, kind);
        DrawStacksCard(group.Look, kind);
    }

    private static StatusGroupKind FirstInUse(StatusLayoutMode mode) => mode switch
    {
        StatusLayoutMode.OneElement => StatusGroupKind.Single,
        _ => StatusGroupKind.Enhancements
    };

    // ---------------------------------------------------------------- main switch

    private void DrawMain(StatusEffectsSettings settings, StatusLayoutMode mode)
    {
        Widgets.Card("##card_status_main", 6, () =>
        {
            Widgets.SectionLabel("Status effects");

            var enabled = settings.Enabled;
            if (ImGui.Checkbox("Enabled (NahjaUI draws them and hides the game's)", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }

            var layoutName = mode switch
            {
                StatusLayoutMode.OneElement => "Display as Single Element",
                StatusLayoutMode.ThreeGroups => "Split Element into 3 Groups",
                _ => "Split Element into 4 Groups"
            };

            ImGui.TextColored(Theme.TextMuted, $"The game's layout now: {layoutName}.");
            ImGui.TextColored(Theme.TextMuted, "NahjaUI follows it. Change it in the game: HUD Layout > Status Effects > UI Element Settings.");
            ImGui.TextColored(Theme.TextMuted, "Move each group with /nui unlock. Right click on one of your own statuses cancels it, as in the game.");
        });
    }

    // ---------------------------------------------------------------- group picker

    private void DrawPicker(StatusLayoutMode mode, ref StatusGroupKind kind)
    {
        var current = kind;

        Widgets.Card("##card_status_picker", 3, () =>
        {
            Widgets.SectionLabel("Group");

            var value = current;
            if (UnitFrameEditor.EnumCombo("Group to edit", ref value, group => GroupLabel(group, mode)))
                current = value;

            ImGui.TextColored(Theme.TextMuted, "The ones marked \"in use\" are the ones the game's layout shows now. The others are kept for when you change it.");
        });

        kind = current;
    }

    private static string GroupLabel(StatusGroupKind kind, StatusLayoutMode mode)
    {
        var name = kind switch
        {
            StatusGroupKind.Single => "All together (single element)",
            StatusGroupKind.Enhancements => "Enhancements",
            StatusGroupKind.Conditional => "Conditional enhancements",
            StatusGroupKind.Enfeeblements => "Enfeeblements",
            _ => "Others"
        };

        return StatusEffectsModule.IsInUse(kind, mode) ? $"{name}  (in use)" : name;
    }

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

            var columns = group.Columns;
            if (ImGui.SliderInt("Icons in a row", ref columns, 1, 30))
            {
                group.Columns = columns;
                plugin.RequestSave();
            }

            var most = group.Look.MaxIcons;
            if (ImGui.SliderInt("Most icons shown", ref most, 1, 30))
            {
                group.Look.MaxIcons = most;
                plugin.RequestSave();
            }

            var justify = group.Justify;
            if (UnitFrameEditor.EnumCombo("Row justified to the", ref justify, JustifyLabel))
            {
                group.Justify = justify;
                plugin.RequestSave();
            }

            var up = group.RowsGrowUp;
            if (ImGui.Checkbox("The next rows go above the first", ref up))
            {
                group.RowsGrowUp = up;
                plugin.RequestSave();
            }

            if (kind == StatusGroupKind.Conditional)
                ImGui.TextColored(Theme.TextMuted, "The game's conditional enhancements are not told apart yet: this group is empty for now.");
        });
    }

    private static string JustifyLabel(StatusJustify justify) => justify switch
    {
        StatusJustify.Left => "Left",
        StatusJustify.Right => "Right",
        _ => "Center"
    };

    // ---------------------------------------------------------------- the icons

    private void DrawIconsCard(NahjaUI.Modules.PartyFrames.PartyAuraSettings look, StatusGroupKind kind)
    {
        Widgets.Card($"##card_status_{kind}_icons", 8, () =>
        {
            Widgets.SectionLabel("Icons");

            var width = look.IconWidth;
            if (ImGui.InputInt("Icon width (px)", ref width, 1, 4))
            {
                look.IconWidth = Math.Clamp(width, 8, 128);
                plugin.RequestSave();
            }

            var height = look.IconHeight;
            if (ImGui.InputInt("Icon height (px)", ref height, 1, 4))
            {
                look.IconHeight = Math.Clamp(height, 8, 128);
                plugin.RequestSave();
            }

            var crop = look.CropPercent;
            if (ImGui.SliderInt("Icon crop (%)", ref crop, 0, 30))
            {
                look.CropPercent = crop;
                plugin.RequestSave();
            }

            var cornerRadius = look.CornerRadiusPx;
            if (ImGui.SliderInt("Corner rounding (px)", ref cornerRadius, 0, 12))
            {
                look.CornerRadiusPx = cornerRadius;
                plugin.RequestSave();
            }

            var spacing = look.SpacingPx;
            if (ImGui.SliderInt("Space between icons (px)", ref spacing, 0, 20))
            {
                look.SpacingPx = spacing;
                plugin.RequestSave();
            }
        });
    }

    // ---------------------------------------------------------------- the time

    private void DrawTimeCard(NahjaUI.Modules.PartyFrames.PartyAuraSettings look, StatusGroupKind kind)
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

            var size = look.TimerSize;
            if (UnitFrameEditor.SizeCombo("Time size (px)", ref size))
            {
                look.TimerSize = size;
                plugin.RequestSave();
            }

            var anchor = look.TimerAnchor;
            if (UnitFrameEditor.EnumCombo("Time position", ref anchor, point => point.Label()))
            {
                look.TimerAnchor = anchor;
                plugin.RequestSave();
            }

            var x = look.TimerOffsetX;
            if (ImGui.InputInt("Time offset X (px)", ref x, 1, 5))
            {
                look.TimerOffsetX = x;
                plugin.RequestSave();
            }

            // Up is plus (the saved value counts downwards, so the sign is turned in the editors).
            var y = -look.TimerOffsetY;
            if (ImGui.InputInt("Time offset Y (px)", ref y, 1, 5))
            {
                look.TimerOffsetY = -y;
                plugin.RequestSave();
            }

            var font = look.Font;
            if (UnitFrameEditor.EnumCombo("Text font", ref font, UnitFrameEditor.FontLabel))
            {
                look.Font = font;
                plugin.RequestSave();
            }
        });
    }

    // ---------------------------------------------------------------- the stacks

    private void DrawStacksCard(NahjaUI.Modules.PartyFrames.PartyAuraSettings look, StatusGroupKind kind)
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

            var size = look.StackTextSize > 0 ? look.StackTextSize : 8;
            if (ImGui.SliderInt("Stack size (px)", ref size, 4, 24))
            {
                look.StackTextSize = size;
                plugin.RequestSave();
            }

            var anchor = look.StacksAnchor;
            if (UnitFrameEditor.EnumCombo("Stack position", ref anchor, point => point.Label()))
            {
                look.StacksAnchor = anchor;
                plugin.RequestSave();
            }

            var x = look.StacksOffsetX;
            if (ImGui.InputInt("Stack offset X (px)", ref x, 1, 5))
            {
                look.StacksOffsetX = x;
                plugin.RequestSave();
            }

            var y = -look.StacksOffsetY;
            if (ImGui.InputInt("Stack offset Y (px)", ref y, 1, 5))
            {
                look.StacksOffsetY = -y;
                plugin.RequestSave();
            }
        });
    }
}
