using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.Auras;
using NahjaUI.Modules.PartyFrames;

namespace NahjaUI.Modules.UnitFrames;

/// <summary>
/// Draws one row of status icons (buffs or debuffs) attached to a bar. The party frames and the player, target and focus
/// frames all use it, so every row looks and is set up the same way.
/// </summary>
internal sealed class StatusRowRenderer
{
    // Statuses shown while a frame is being adjusted (real status ids, so they have real icons). The filters do not apply to them.
    public static readonly StatusSnapshot[] PreviewBuffs =
    {
        new(160, 0, 14f, 0), new(1191, 0, 8f, 0), new(50, 0, 22f, 0),
        new(160, 0, 5f, 0), new(1191, 0, 31f, 0), new(50, 0, 12f, 0)
    };

    public static readonly StatusSnapshot[] PreviewDebuffs =
    {
        new(638, 0, 12f, 0), new(43, 0, 9f, 0), new(144, 0, 21f, 0)
    };

    private readonly Plugin plugin;

    public StatusRowRenderer(Plugin plugin)
    {
        this.plugin = plugin;
    }

    // One row of icons next to a bar. The first icon is attached to a point of the bar (plus the offset) by one of its own
    // points, and the next ones follow in the direction of the growth.
    public void DrawRow(ImDrawListPtr drawList, PartyAuraSettings row, bool isBuff, IReadOnlyList<StatusSnapshot> statuses, bool isSample, uint playerId, Vector2 barPos, Vector2 barSize, string place = "")
    {
        if (!row.Enabled || row.MaxIcons <= 0 || statuses.Count == 0)
            return;

        var scale = ImGuiHelpers.GlobalScale;

        var iconSize = new Vector2(MathF.Round(Math.Max(4, row.IconWidth) * scale), MathF.Round(Math.Max(4, row.IconHeight) * scale));
        var spacing = MathF.Round(row.SpacingPx * scale);

        var attach = barPos + barSize * row.BarAnchor.Fraction() + new Vector2(row.OffsetX, row.OffsetY) * scale;
        var first = attach - iconSize * row.IconAnchor.Fraction();
        first = new Vector2(MathF.Round(first.X), MathF.Round(first.Y));

        var step = row.Growth switch
        {
            PartyAuraGrowth.Left => new Vector2(-(iconSize.X + spacing), 0f),
            PartyAuraGrowth.Right => new Vector2(iconSize.X + spacing, 0f),
            PartyAuraGrowth.Up => new Vector2(0f, -(iconSize.Y + spacing)),
            _ => new Vector2(0f, iconSize.Y + spacing)
        };

        var shown = 0;
        foreach (var status in statuses)
        {
            if (shown >= row.MaxIcons)
                break;

            if (!Accepts(row, isBuff, status, isSample, playerId))
                continue;

            var iconPos = first + step * shown;
            DrawIcon(drawList, row, status, iconPos, iconSize);

            // The mouse is over this icon: show its description (no window is needed to know this, only the rectangle).
            if (ImGui.IsMouseHoveringRect(iconPos, iconPos + iconSize, false))
                ShowTooltip(status, isBuff, place);

            shown++;
        }
    }

    // ---------------------------------------------------------------- tooltip

    private int tooltipFrame = -1;

    // A small box next to the mouse with what the game says about the status: name, kind, time left, description and who applied it.
    private void ShowTooltip(StatusSnapshot status, bool isBuff, string place)
    {
        var look = plugin.Configuration.Tooltip;
        if (!look.Allows(place, isBuff) || plugin.Layout.UnlockMode)
            return;

        // The row can be drawn several times in one frame (around a game window): the box is shown once.
        var frame = ImGui.GetFrameCount();
        if (frame == tooltipFrame)
            return;

        // Not when the mouse is over a window of ours or over one of the game's.
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow) || plugin.GameCover.Contains(ImGui.GetIO().MousePos))
            return;

        tooltipFrame = frame;

        var scale = ImGuiHelpers.GlobalScale;

        using var theme = new ThemeScope(plugin.Configuration);
        using var font = plugin.Fonts.PushClosest(OverlayFont.NahjaText, look.TextSize);

        ImGui.PushStyleColor(ImGuiCol.PopupBg, look.BackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, look.BorderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, MathF.Round(look.RoundingPx * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, MathF.Max(1f, MathF.Round(scale)));

        ImGui.BeginTooltip();

        ImGui.TextColored(look.NameUsesAccent ? Theme.Accent : look.NameColor, GameNames.StatusName(status.Id));

        if (look.ShowId)
            ImGui.TextColored(look.DetailColor, $"ID {status.Id}");

        var description = GameNames.StatusDescription(status.Id);
        if (description.Length > 0)
        {
            ImGui.Spacing();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + look.MaxWidthPx * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, look.DescriptionColor);
            ImGui.TextUnformatted(description);
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
        }

        if (look.ShowSource && status.SourceId != 0)
        {
            var source = Plugin.ObjectTable.SearchByEntityId(status.SourceId);
            if (source is not null)
            {
                ImGui.Spacing();
                ImGui.TextColored(look.DetailColor, $"Applied by {source.Name}");
            }
        }

        ImGui.EndTooltip();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    // Does this row show this status?
    private static bool Accepts(PartyAuraSettings row, bool isBuff, StatusSnapshot status, bool isSample, uint playerId)
    {
        if (isSample)
            return true;

        // A status without an icon cannot be drawn.
        if (GameNames.StatusIconId(status.Id) == 0)
            return false;

        // Every buff, or every debuff: the game says which of the two a status is.
        if (GameNames.StatusCategory(status.Id) != (isBuff ? 1 : 2))
            return false;

        // The "extra" list is always shown, whoever applied the status.
        if (row.ExtraStatusIds.Contains(status.Id))
            return true;

        if (row.OnlyMine && status.SourceId != playerId)
            return false;

        var listed = row.StatusIds.Contains(status.Id);

        return row.Filter switch
        {
            PartyAuraFilter.OnlyListed => listed,
            PartyAuraFilter.AllExceptListed => !listed,
            _ => true
        };
    }

    // One icon: cropped (never stretched) to the shape of the box, with a 1 px black border, the time and the stacks.
    private void DrawIcon(ImDrawListPtr drawList, PartyAuraSettings row, StatusSnapshot status, Vector2 pos, Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var iconId = GameNames.StatusIconId(status.Id);

        // Rounded corners, like the bars. They can never be more than half of the shorter side.
        var rounding = MathF.Min(MathF.Round(Math.Clamp(row.CornerRadiusPx, 0, 24) * scale), MathF.Min(size.X, size.Y) / 2f);

        if (iconId != 0)
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            var crop = Math.Clamp(row.CropPercent, 0, 30) / 100f;
            var (uv0, uv1) = AuraEngine.IconUv(size, AuraEngine.StatusIconAspect, crop);

            drawList.AddImageRounded(texture.Handle, pos, pos + size, uv0, uv1, ImGui.GetColorU32(Vector4.One), rounding, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.18f, 0.9f)), rounding);
        }

        var thickness = MathF.Max(1f, MathF.Round(scale));
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f)), rounding, ImDrawFlags.None, thickness);

        // The text size is in logical pixels (the fonts are already built scaled).
        var timerPx = Math.Clamp(row.TimerSize, 8, 64);
        var stackPx = row.StackTextSize > 0 ? Math.Clamp(row.StackTextSize, 8, 64) : timerPx * 0.75f;

        if (row.ShowTimer && status.RemainingTime > 0f)
            plugin.AuraSystem.DrawOverlayText(drawList, AuraEngine.FormatTime(status.RemainingTime), timerPx, row.Font,
                pos, size, row.TimerAnchor, new Vector2(row.TimerOffsetX, row.TimerOffsetY));

        // Only statuses that really stack show a number.
        var maxStacks = GameNames.MaxStacks(status.Id);
        if (row.ShowStacks && maxStacks > 1 && status.Param > 1 && status.Param <= maxStacks)
            plugin.AuraSystem.DrawOverlayText(drawList, status.Param.ToString(), stackPx, row.Font,
                pos, size, row.StacksAnchor, new Vector2(row.StacksOffsetX, row.StacksOffsetY));
    }
}
