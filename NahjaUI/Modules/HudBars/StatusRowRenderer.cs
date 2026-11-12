using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.Trackers;
using NahjaUI.Modules.PartyListBar;

namespace NahjaUI.Modules.HudBars;

/// <summary>
/// Draws one row of status icons (enhancements or enfeeblements) attached to a bar. The party list and the player, target and focus
/// frames all use it, so every row looks and is set up the same way.
/// </summary>
internal sealed class StatusRowRenderer
{
    // Statuses shown while a frame is being adjusted (real status ids, so they have real icons). The filters do not apply to them.
    public static readonly StatusSnapshot[] PreviewEnhancements =
    {
        new(160, 0, 14f, 0), new(1191, 0, 8f, 0), new(50, 0, 22f, 0),
        new(160, 0, 5f, 0), new(1191, 0, 31f, 0), new(50, 0, 12f, 0)
    };

    public static readonly StatusSnapshot[] PreviewEnfeeblements =
    {
        new(638, 0, 12f, 0), new(43, 0, 9f, 0), new(144, 0, 21f, 0)
    };

    private readonly Plugin plugin;

    public StatusRowRenderer(Plugin plugin)
    {
        this.plugin = plugin;
    }

    /// <summary>The area covered by the icons of a row (a point when the row shows nothing). Another row can be attached to it.</summary>
    internal readonly record struct RowRect(Vector2 Min, Vector2 Max)
    {
        public Vector2 Size => Max - Min;
    }

    // The statuses a row shows, in the order of the row and without more than it can hold.
    private List<StatusSnapshot> Shown(StatusRowSettings row, bool isEnhancement, IReadOnlyList<StatusSnapshot> statuses, bool isSample, uint playerId)
    {
        var result = new List<StatusSnapshot>();
        if (!row.Enabled || row.MaxIcons <= 0)
            return result;

        var accepted = new List<StatusSnapshot>();
        foreach (var status in statuses)
        {
            if (Accepts(row, isEnhancement, status, isSample, playerId))
                accepted.Add(status);
        }

        foreach (var status in SortedForRow(row, accepted, playerId))
        {
            if (result.Count >= row.MaxIcons)
                break;

            result.Add(status);
        }

        return result;
    }

    /// <summary>Where the icons of a row go: every icon has its own place (lines, centering), and the row covers the area of all of them.</summary>
    private readonly record struct RowPlace(StatusRowSettings Row, int Count, Vector2 Attach, Vector2 First, Vector2 Step, Vector2 Line, int PerLine, Vector2 IconSize, float Spacing, RowRect Rect)
    {
        public Vector2 PositionOf(int index)
        {
            var line = index / PerLine;
            var inLine = Math.Min(PerLine, Count - line * PerLine);
            var position = First + Step * (index % PerLine) + Line * line;

            // A centered line is centered on the point of the row, whatever the number of icons in it.
            if (Row.Growth == StatusRowGrowth.CenteredHorizontally)
                position.X = Attach.X - (inLine * IconSize.X + (inLine - 1) * Spacing) / 2f + (index % PerLine) * (IconSize.X + Spacing);
            else if (Row.Growth == StatusRowGrowth.CenteredVertically)
                position.Y = Attach.Y - (inLine * IconSize.Y + (inLine - 1) * Spacing) / 2f + (index % PerLine) * (IconSize.Y + Spacing);

            return new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
        }
    }

    // Where the icons of a row go: the first icon is attached to a point of the target (the bar, or the other row) plus the offset, by one of
    // its own points, and the next ones follow in the direction of the growth. When a line is full ("icons per line") the next icon
    // starts the next line, on the side the row says.
    private RowPlace Place(StatusRowSettings row, int count, Vector2 targetPos, Vector2 targetSize)
    {
        var scale = ImGuiHelpers.GlobalScale;

        var iconSize = new Vector2(MathF.Round(Math.Max(4, row.IconWidth) * scale), MathF.Round(Math.Max(4, row.IconHeight) * scale));
        var spacing = MathF.Round(row.SpacingPx * scale);

        var attach = targetPos + targetSize * row.BarAnchor.Fraction() + new Vector2(row.OffsetX, row.OffsetY) * scale;
        var first = attach - iconSize * row.IconAnchor.Fraction();

        var stepX = iconSize.X + spacing;
        var stepY = iconSize.Y + spacing;

        var step = row.Growth switch
        {
            StatusRowGrowth.Left => new Vector2(-stepX, 0f),
            StatusRowGrowth.Right or StatusRowGrowth.CenteredHorizontally => new Vector2(stepX, 0f),
            StatusRowGrowth.Up => new Vector2(0f, -stepY),
            _ => new Vector2(0f, stepY)
        };

        var line = row.NextLineNow switch
        {
            StatusRowGrowth.Up => new Vector2(0f, -stepY),
            StatusRowGrowth.Left => new Vector2(-stepX, 0f),
            StatusRowGrowth.Right => new Vector2(stepX, 0f),
            _ => new Vector2(0f, stepY)
        };

        var place = new RowPlace(row, count, attach, first, step, line, Math.Clamp(row.IconsPerLine, 1, 30), iconSize, spacing, default);
        if (count == 0)
            return place with { Rect = new RowRect(attach, attach) };

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < count; i++)
        {
            var position = place.PositionOf(i);
            min = Vector2.Min(min, position);
            max = Vector2.Max(max, position + iconSize);
        }

        return place with { Rect = new RowRect(min, max) };
    }

    // One row of icons next to a bar (or next to the other row of the frame).
    public RowRect DrawRow(ImDrawListPtr drawList, StatusRowSettings row, bool isEnhancement, IReadOnlyList<StatusSnapshot> statuses, bool isSample, uint playerId, Vector2 barPos, Vector2 barSize, string place = "", bool friendly = true)
    {
        var shown = Shown(row, isEnhancement, statuses, isSample, playerId);
        var where = Place(row, shown.Count, barPos, barSize);
        Paint(drawList, row, shown, isEnhancement, where, place, friendly);
        return where.Rect;
    }

    /// <summary>
    /// The enhancements row and the enfeeblements row of a frame. Each one is attached to the bar or to the other row; when one is attached
    /// to the other, the other one is placed first. If both choose each other, the enhancements row stays on the bar.
    /// </summary>
    public void DrawRows(ImDrawListPtr drawList, StatusRowSettings enhancements, StatusRowSettings enfeeblements, IReadOnlyList<StatusSnapshot> enhancementStatuses,
        IReadOnlyList<StatusSnapshot> enfeeblementStatuses, bool isSample, uint playerId, Vector2 barPos, Vector2 barSize, string place = "", bool friendly = true)
    {
        var enhancementList = Shown(enhancements, true, enhancementStatuses, isSample, playerId);
        var enfeeblementList = Shown(enfeeblements, false, enfeeblementStatuses, isSample, playerId);

        var enhancementsFollow = enhancements.AttachTo == StatusRowAttach.OtherRow && enfeeblements.AttachTo == StatusRowAttach.Frame;
        var enfeeblementsFollow = enfeeblements.AttachTo == StatusRowAttach.OtherRow && !enhancementsFollow;

        if (enhancementsFollow)
        {
            var other = Place(enfeeblements, enfeeblementList.Count, barPos, barSize);
            var mine = Place(enhancements, enhancementList.Count, other.Rect.Min, other.Rect.Size);
            Paint(drawList, enfeeblements, enfeeblementList, false, other, place, friendly);
            Paint(drawList, enhancements, enhancementList, true, mine, place, friendly);
        }
        else
        {
            var mine = Place(enhancements, enhancementList.Count, barPos, barSize);
            var other = enfeeblementsFollow
                ? Place(enfeeblements, enfeeblementList.Count, mine.Rect.Min, mine.Rect.Size)
                : Place(enfeeblements, enfeeblementList.Count, barPos, barSize);
            Paint(drawList, enhancements, enhancementList, true, mine, place, friendly);
            Paint(drawList, enfeeblements, enfeeblementList, false, other, place, friendly);
        }
    }

    private void Paint(ImDrawListPtr drawList, StatusRowSettings row, List<StatusSnapshot> shown, bool isEnhancement, RowPlace where, string place, bool friendly)
    {
        for (var i = 0; i < shown.Count; i++)
        {
            var iconPos = where.PositionOf(i);
            DrawIcon(drawList, row, shown[i], iconPos, where.IconSize, friendly);

            // The mouse is over this icon: show its description (no window is needed to know this, only the rectangle).
            if (ImGui.IsMouseHoveringRect(iconPos, iconPos + where.IconSize, false))
                ShowTooltip(shown[i], isEnhancement, place);
        }
    }

    // With "priority by filter" the filters decide first (the first ticked filter first) and the time orders the statuses inside each filter.
    private List<StatusSnapshot> SortedForRow(StatusRowSettings row, List<StatusSnapshot> accepted, uint playerId)
    {
        if (!row.PriorityByFilter || row.FilterIds.Count == 0)
            return Sorted(accepted, row.Order);

        var groups = accepted.GroupBy(status => plugin.Configuration.Filters.RankOf(row.FilterIds, status.Id, row.OnlyMineFilterIds, status.SourceId == playerId)).OrderBy(group => group.Key);
        var result = new List<StatusSnapshot>();
        foreach (var group in groups)
            result.AddRange(Sorted(group, row.Order));

        return result;
    }

    /// <summary>The statuses ordered by the time they have left. A permanent status (no end) counts as the longest possible time. The sort is stable.</summary>
    internal static List<StatusSnapshot> Sorted(IEnumerable<StatusSnapshot> statuses, StatusOrder order)
    {
        static float TimeLeft(StatusSnapshot status) => status.RemainingTime > 0f ? status.RemainingTime : float.MaxValue;

        return (order == StatusOrder.Ascending
            ? statuses.OrderBy(TimeLeft)
            : statuses.OrderByDescending(TimeLeft)).ToList();
    }

    // ---------------------------------------------------------------- tooltip

    private int tooltipFrame = -1;

    // A small box next to the mouse with what the game says about the status: name, kind, time left, description and who applied it.
    internal void ShowTooltip(StatusSnapshot status, bool isEnhancement, string place)
    {
        var look = plugin.Configuration.Tooltip;
        if (!look.Allows(place, isEnhancement) || plugin.Layout.UnlockMode)
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
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, MathF.Round(GlobalColors.Current.Rounding * scale));
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
                ImGui.TextColored(look.DetailColor, $"Applied by {StreamerMode.Name(source.Name.ToString(), source.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)}");
            }
        }

        ImGui.EndTooltip();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    // Does this row show this status?
    private bool Accepts(StatusRowSettings row, bool isEnhancement, StatusSnapshot status, bool isSample, uint playerId)
    {
        if (isSample)
            return true;

        // A status without an icon cannot be drawn.
        if (GameNames.StatusIconId(status.Id) == 0)
            return false;

        // Every enhancement, or every enfeeblement: the game says which of the two a status is.
        if (GameNames.StatusCategory(status.Id) != (isEnhancement ? 1 : 2))
            return false;

        // The "extra" list is always shown, whoever applied the status.
        if (row.ExtraStatusIds.Contains(status.Id))
            return true;

        if (row.OnlyMine && status.SourceId != playerId)
            return false;

        var listed = row.StatusIds.Contains(status.Id)
            || plugin.Configuration.Filters.Contains(row.FilterIds, status.Id, row.OnlyMineFilterIds, status.SourceId == playerId);

        return row.Filter switch
        {
            StatusRowFilter.OnlyListed => listed,
            StatusRowFilter.AllExceptListed => !listed,
            _ => true
        };
    }

    // One icon: cropped (never stretched) to the shape of the box, with a 1 px black border, the time and the stacks.
    internal void DrawIcon(ImDrawListPtr drawList, StatusRowSettings row, StatusSnapshot status, Vector2 pos, Vector2 size, bool friendly = true)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var iconId = GameNames.StatusIconId(status.Id);

        // Rounded corners, like the bars. They can never be more than half of the shorter side.
        var rounding = MathF.Min(MathF.Round(GlobalColors.Current.Rounding * scale), MathF.Min(size.X, size.Y) / 2f);

        if (iconId != 0)
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            // The same trim as the trackers: the empty margins of the game's icon, measured for each icon and always cut.
            var margins = IconMargins.Get(iconId);
            var (uv0, uv1) = TrackerEngine.IconUv(size, TrackerEngine.StatusIconAspect, margins.Item1, margins.Item2, margins.Item3, margins.Item4);

            drawList.AddImageRounded(texture.Handle, pos, pos + size, uv0, uv1, ImGui.GetColorU32(Vector4.One), rounding, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.18f, 0.9f)), rounding);
        }

        var thickness = MathF.Max(1f, MathF.Round(scale));
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f)), rounding, ImDrawFlags.None, thickness);

        // An enfeeblement that a cleanse can remove gets a coloured border (the color of the Appearance page). Only on friendly units, since
        // Esuna cannot be used on an enemy, and not in PvP, where there is no Esuna.
        if (friendly && !plugin.GameData.Current.Context.InPvP && GameNames.StatusCategory(status.Id) == 2 && GameNames.StatusCanDispel(status.Id))
            drawList.AddRect(pos, pos + size, ImGui.GetColorU32(GlobalColors.Current.DispelBorder), rounding, ImDrawFlags.None, thickness * 2f);

        // The text size is in logical pixels (the fonts are already built scaled).
        var timerPx = Math.Clamp(row.TimerSize, 4, 64);
        var stackPx = Math.Clamp(row.StackTextSize > 0 ? row.StackTextSize : 8, 4, 64);

        if (row.ShowTimer && status.RemainingTime > 0f)
            plugin.TrackerSystem.DrawOverlayText(drawList, TrackerEngine.FormatTime(status.RemainingTime), timerPx, OverlayFont.NahjaDisplayBold,
                pos, size, row.TimerAnchor, new Vector2(row.TimerOffsetX, row.TimerOffsetY), row.TimerColor);

        // Only statuses that really stack show a number.
        var maxStacks = GameNames.MaxStacks(status.Id);
        if (row.ShowStacks && maxStacks > 1 && status.Param > 1 && status.Param <= maxStacks)
            plugin.TrackerSystem.DrawOverlayText(drawList, status.Param.ToString(), stackPx, OverlayFont.NahjaDisplayBold,
                pos, size, row.StacksAnchor, new Vector2(row.StacksOffsetX, row.StacksOffsetY), row.StacksColor);
    }
}
