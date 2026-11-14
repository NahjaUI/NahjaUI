using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.PartyListBar;
using NahjaUI.Modules.Trackers;

namespace NahjaUI.Modules.AllianceList;

/// <summary>
/// Draws the alliance list as one grid: the members of the other parties of the alliance, one cell each, in the order of the game
/// (first the members of the first party, then the second). Every slot has its own place, so a missing member leaves an empty cell
/// and nobody moves. It hides the game's own alliance lists. It only reads the game snapshot.
/// </summary>
internal sealed class AllianceListModule : IDisposable
{
    // The game's own alliance lists (HUD layout: Alliance List 1 and 2).
    private static readonly string[] NativeLists = { "_AllianceList1", "_AllianceList2" };

    // The slots of a normal alliance (two other parties of eight) shown in the preview and in the settings.
    public const int NormalSlots = 16;

    // What is shown in the preview when there is no real alliance: job, HP percent and a state (D = dead, F = far away).
    private static readonly (uint Job, uint HpPercent, char State)[] SampleMembers =
    {
        (19, 100, ' '), (21, 92, ' '), (24, 100, ' '), (28, 78, ' '),
        (20, 100, ' '), (22, 64, ' '), (30, 100, ' '), (34, 100, ' '),
        (23, 88, ' '), (31, 100, ' '), (38, 55, ' '), (25, 100, ' '),
        (27, 0, 'D'), (35, 100, 'F'), (37, 100, ' '), (0, 0, ' ')
    };

    // The icon of each of those statuses (declared first: the search below fills it).
    private static readonly Dictionary<uint, uint> StatusIcons = new();

    // The status ids that mean "a Raise is waiting for this member": every status of the game called Raise.
    private static readonly HashSet<uint> RaiseStatusIds = FindStatusIds("Raise");

    private readonly Plugin plugin;
    private readonly NativeWindowHider hider = new();

    public AllianceListModule(Plugin plugin)
    {
        this.plugin = plugin;

        // The size is the size of one cell; the cells are placed in a grid from the top left corner of the box.
        AllianceElement = plugin.Layout.Register("hudbar.alliance", "Alliance list", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.TopLeft,
            SelfAnchor = AnchorPoint.TopLeft,
            OffsetX = 300,
            OffsetY = 300,
            Width = 88,
            Height = 36
        });

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
    }

    public LayoutElement AllianceElement { get; }

    /// <summary>Set by the settings page every frame the Alliance List tab is open: sample members are shown when there is no alliance.</summary>
    public bool PreviewRequested { get; set; }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
        hider.ShowAll();
    }

    private static HashSet<uint> FindStatusIds(string name)
    {
        var ids = new HashSet<uint>();

        foreach (var row in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>(ClientLanguage.English))
        {
            if (row.Name.ToString() == name)
            {
                ids.Add(row.RowId);
                StatusIcons[row.RowId] = row.Icon;
            }
        }

        return ids;
    }

    private static UnitSnapshot?[] BuildSample()
    {
        var result = new UnitSnapshot?[SampleMembers.Length];

        for (var i = 0; i < SampleMembers.Length; i++)
        {
            var (job, percent, state) = SampleMembers[i];
            if (job == 0)
                continue;

            var statuses = Array.Empty<StatusSnapshot>();
            if (state == 'D')
            {
                foreach (var id in RaiseStatusIds)
                {
                    statuses = new[] { new StatusSnapshot(id, 0, 0f, 0) };
                    break;
                }
            }

            result[i] = new UnitSnapshot(0, $"Member {i + 1}", job, 100, percent * 1000u, 100000, 0, 0, state == 'D', null, statuses, true,
                Presence: state == 'F' ? Presence.OutOfRange : Presence.Here);
        }

        return result;
    }

    private static readonly UnitSnapshot?[] Sample = BuildSample();

    // ---------------------------------------------------------------- hiding the game's lists

    private unsafe void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        if (!plugin.Configuration.HudBars.Alliance.Enabled)
            return;

        hider.Hide(args);
    }

    // ---------------------------------------------------------------- drawing

    public void Draw()
    {
        var settings = plugin.Configuration.HudBars.Alliance;
        hider.ShowAgain(_ => settings.Enabled);

        var preview = PreviewRequested || plugin.Layout.UnlockMode || plugin.Layout.IsPreviewing(AllianceElement.Id);
        PreviewRequested = false;

        if (!settings.Enabled)
        {
            AllianceElement.DisplayRect = null;
            return;
        }

        if (!plugin.Layout.IsShown(AllianceElement))
        {
            AllianceElement.DisplayRect = null;
            return;
        }

        var snapshot = plugin.GameData.Current;
        IReadOnlyList<UnitSnapshot?>? members = snapshot.Alliance;
        var isSample = false;

        if (members is null)
        {
            if (!preview)
            {
                AllianceElement.DisplayRect = null;
                return;
            }

            members = Sample;
            isSample = true;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(AllianceElement.Layout);
        var gap = MathF.Round(settings.SpacingPx * scale);
        var columns = Math.Clamp(settings.Columns, 1, Math.Max(1, members.Count));
        var rows = (members.Count + columns - 1) / columns;
        var drawList = ImGui.GetBackgroundDrawList();

        // The whole grid is one box for the unlock mode.
        var box = new Vector2(columns * size.X + (columns - 1) * gap, rows * size.Y + (rows - 1) * gap);

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member is null)
                continue;

            var cellPos = pos + new Vector2(i % columns * (size.X + gap), i / columns * (size.Y + gap));

            plugin.GameCover.Draw(cellPos, size, 100f * scale, () => DrawCell(drawList, settings, member, cellPos, size));

            // Clicks (target, mouseover) only for real members, and not while the boxes are being moved.
            if (!isSample && !plugin.Layout.UnlockMode)
            {
                plugin.HudBarSystem.HandleClicks($"alliance.{member.EntityId}", cellPos, size, member);

                // The cells are small and have no name: it always shows in a small box while the mouse is over the cell.
                if (IsHovered(cellPos, size))
                    plugin.StatusRows.ShowTextTooltip(member.Name);
            }
        }

        AllianceElement.DisplayRect = (pos, box);
    }

    private bool IsHovered(Vector2 pos, Vector2 size)
    {
        if (plugin.GameCover.Contains(ImGui.GetIO().MousePos))
            return false;

        return ImGui.IsMouseHoveringRect(pos, pos + size, false) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow);
    }

    // The game's own job icons: 62000 + job id (plain) or 62100 + job id (light).
    private static uint JobIconId(uint jobId, PartyJobIconStyle style) =>
        (style == PartyJobIconStyle.Light ? 62100u : 62000u) + jobId;

    private void DrawCell(ImDrawListPtr drawList, AllianceListSettings settings, UnitSnapshot member, Vector2 pos, Vector2 size)
    {
        var fadeMark = DrawListFade.Mark(drawList);
        var scale = ImGuiHelpers.GlobalScale;
        var max = pos + size;
        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        // Dead = the game says so, or no HP left on a member that has HP at all (no data is never "dead").
        var dead = member.IsDead || (member.Hp == 0 && member.MaxHp > 0);
        // Like the party list: dead, offline, in another area, too far away or viewing a cutscene are faded. A dead member who already has a
        // Raise waiting is NOT faded: he is ready to be brought back and must be easy to see.
        var raiseReady = dead && HasRaise(member);
        var away = !raiseReady && (dead || member.Presence is Presence.Offline or Presence.OtherArea or Presence.OutOfRange or Presence.ViewingCutscene);

        var fraction = member.MaxHp == 0 ? 0f : Math.Clamp(member.Hp / (float)member.MaxHp, 0f, 1f);
        var fillColor = plugin.HudBarSystem.HpColor(plugin.Configuration.HudBars.Player, member, true);

        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(GlobalColors.Current.Background), rounding);

        var fillRight = MathF.Round(pos.X + size.X * fraction);
        if (fillRight > pos.X)
        {
            drawList.PushClipRect(pos, new Vector2(MathF.Min(fillRight, max.X), max.Y), true);
            drawList.AddRectFilled(pos, max, ImGui.GetColorU32(fillColor), rounding);
            drawList.PopClipRect();
        }

        // The job icon (the game's own icon, so every job has one).
        if (settings.ShowJobIcon && member.JobId != 0)
        {
            // The icon is the size chosen, even if it is taller than the cell.
            var side = MathF.Max(MathF.Round(settings.JobIconSizePx * scale), 4f);
            var iconAttach = pos + size * settings.JobIconBarAnchor.Fraction() + new Vector2(settings.JobIconOffsetX, settings.JobIconOffsetY) * scale;
            var iconMin = iconAttach - new Vector2(side, side) * settings.JobIconSelfAnchor.Fraction();
            iconMin = new Vector2(MathF.Round(iconMin.X), MathF.Round(iconMin.Y));
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(JobIconId(member.JobId, settings.JobIconStyle))).GetWrapOrEmpty();
            drawList.AddImage(texture.Handle, iconMin, iconMin + new Vector2(side, side), new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);
        }

        drawList.AddRect(pos, max, ImGui.GetColorU32(GlobalColors.Current.Border), rounding, ImDrawFlags.None, thickness);

        // The target ring, like the party list.
        var targetId = plugin.GameData.Current.Target?.EntityId;
        if (member.EntityId != 0 && targetId is not null && member.EntityId == targetId)
        {
            var ringWidth = thickness * 2f;
            var grow = new Vector2(ringWidth / 2f, ringWidth / 2f);
            drawList.AddRect(pos - grow, max + grow, ImGui.GetColorU32(plugin.Configuration.TargetRingColor), rounding + ringWidth / 2f, ImDrawFlags.None, ringWidth);
        }

        // With the Raise icon on him the text is left out: the cell is too small for both.
        if (dead && !raiseReady)
        {
            // The word Dead is always on: it has no switch, so it is never left off.
            settings.DeadText.Enabled = true;
            plugin.HudBarSystem.DrawText(drawList, "Dead", settings.DeadText, pos, size, fitInBox: true);
        }

        // The Raise that is waiting for him: only the game's icon, in the top right corner.
        if (raiseReady)
        {
            var iconSize = new Vector2(MathF.Max(MathF.Round(settings.RaiseIconWidthPx * scale), 4f), MathF.Max(MathF.Round(settings.RaiseIconHeightPx * scale), 4f));
            var iconAttach = pos + size * settings.RaiseIconBarAnchor.Fraction() + new Vector2(settings.RaiseIconOffsetX, settings.RaiseIconOffsetY) * scale;
            var iconMin = iconAttach - iconSize * settings.RaiseIconSelfAnchor.Fraction();
            iconMin = new Vector2(MathF.Round(iconMin.X), MathF.Round(iconMin.Y));

            // The same trim as the trackers: the empty margins of the game's icon are measured and cut, and the icon is cut (never stretched)
            // to the shape of the box.
            var iconId = RaiseIcon(member);
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            var margins = IconMargins.Get(iconId);
            var (uv0, uv1) = TrackerEngine.IconUv(iconSize, TrackerEngine.StatusIconAspect, margins.Item1, margins.Item2, margins.Item3, margins.Item4);
            var iconRounding = MathF.Min(MathF.Round(GlobalColors.Current.Rounding * scale), MathF.Min(iconSize.X, iconSize.Y) / 2f);
            drawList.AddImageRounded(texture.Handle, iconMin, iconMin + iconSize, uv0, uv1, ImGui.GetColorU32(Vector4.One), iconRounding, ImDrawFlags.RoundCornersAll);
        }

        if (away)
            DrawListFade.FadeSince(drawList, fadeMark, Fade.Alpha);
    }

    private static bool HasRaise(UnitSnapshot member)
    {
        foreach (var status in member.Statuses)
        {
            if (RaiseStatusIds.Contains(status.Id))
                return true;
        }

        return false;
    }

    private static uint RaiseIcon(UnitSnapshot member)
    {
        foreach (var status in member.Statuses)
        {
            if (RaiseStatusIds.Contains(status.Id) && StatusIcons.TryGetValue(status.Id, out var icon))
                return icon;
        }

        return 0;
    }
}
