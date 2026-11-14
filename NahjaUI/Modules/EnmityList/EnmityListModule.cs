using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Modules.EnmityList;

/// <summary>
/// Draws our own enmity list: the same enemies as the game's list (the ones that are fighting you, up to 8), one bar each
/// with the name, the HP, a dot that says who the enemy is attacking, and its cast under the bar.
/// It only reads the game snapshot.
/// </summary>
internal sealed class EnmityListModule : IDisposable
{
    // The game's own enmity list.
    private const string NativeList = "_EnemyList";

    private static readonly string[] NativeLists = { NativeList };

    // What the preview shows when no real enemy is fighting you.
    private static readonly EnemyEntrySnapshot[] SampleEnemies =
    {
        Sample("Gurangatch", 5300000, 10000000, true, new CastSnapshot(7, 1.2f, 3.0f, true)),
        Sample("Hunter's Lackey", 740000, 1000000, false, new CastSnapshot(7, 2.1f, 2.5f, false)),
        Sample("Fell Totem", 1000000, 1000000, false, null),
        Sample("Ravening Wolf", 180000, 1000000, true, null)
    };

    private readonly Plugin plugin;
    private readonly NativeWindowHider hider = new();

    public EnmityListModule(Plugin plugin)
    {
        this.plugin = plugin;

        // The box of the list: its size is one row; the rows are placed one after another inside a box of 8 rows, from the top or (option) from the bottom.
        Element = plugin.Layout.Register("enmitylist", "Enmity list", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.TopRight,
            SelfAnchor = AnchorPoint.TopRight,
            OffsetX = -60,
            OffsetY = 260,
            Width = 210,
            Height = 18
        });

        // Shown in the panel of unlock mode, under X and Y.
        Element.DrawOptions = () =>
        {
            var settings = plugin.Configuration.EnmityList;
            var fromBottom = settings.StartFromBottom;
            if (ImGui.Checkbox("Start drawing from the bottom", ref fromBottom))
            {
                settings.StartFromBottom = fromBottom;
                plugin.RequestSave();
            }
        };

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
    }

    // The box that unlock mode shows: the most enemies the game's list holds.
    private const int BoxSlots = 8;

    public LayoutElement Element { get; }

    /// <summary>Set by the settings page every frame it is open: sample enemies are shown when nobody is fighting you.</summary>
    public bool PreviewRequested { get; set; }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
        hider.ShowAll();
    }

    private static EnemyEntrySnapshot Sample(string name, uint hp, uint maxHp, bool onMe, CastSnapshot? cast) =>
        new(new UnitSnapshot(0, name, 0, 100, hp, maxHp, 0, 0, false, cast, Array.Empty<StatusSnapshot>(), IsHostile: true), onMe);

    // Runs right before the game draws its own list: marking it invisible makes the game skip it.
    private unsafe void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        var settings = plugin.Configuration.EnmityList;
        if (!settings.Enabled)
            return;

        // The enmity list does not exist in PvP: the game's own is left alone.
        if (plugin.GameData.Current.Context.InPvP)
            return;

        hider.Hide(args);
    }

    // The game's own options of its enmity list (checked in game: 1 = on, 0 = off). Ours follow them.
    //  - UiConfig EnemyListCastbarEnable: show the progress bars.
    //  - UiControl EnemyListDisp: show the enmity list at all.
    // (UiConfig "EnmityList" always read 0: not used.) When an option cannot be read it counts as on.
    private bool showProgressBarNow = true;
    private bool listShownNow = true;

    private void ReadGameOptions()
    {
        showProgressBarNow = !(Plugin.GameConfig.UiConfig.TryGet("EnemyListCastbarEnable", out uint cast) && cast == 0);
        listShownNow = !(Plugin.GameConfig.UiControl.TryGet("EnemyListDisp", out uint disp) && disp == 0);
    }

    public void Draw()
    {
        var settings = plugin.Configuration.EnmityList;
        var inPvp = plugin.GameData.Current.Context.InPvP;
        hider.ShowAgain(_ => settings.Enabled && !inPvp);
        ReadGameOptions();

        // Tell the provider whether to read the list at all (it costs nothing while this is off, or in PvP).
        plugin.GameData.WantEnmityList = settings.Enabled && listShownNow && !inPvp;

        // No enmity list in PvP, so unlock mode does not offer it there.
        Element.IsHidden = inPvp;
        if (inPvp && !PreviewRequested)
            return;

        var preview = PreviewRequested || plugin.Layout.UnlockMode || plugin.Layout.IsPreviewing(Element.Id);
        PreviewRequested = false;

        if (!settings.Enabled || !plugin.Layout.IsShown(Element))
            return;

        // The game's option can hide the list (the samples of the unlock mode still show, to place it).
        if (!listShownNow && !preview)
            return;

        var snapshot = plugin.GameData.Current;
        if (!snapshot.IsLoggedIn)
            return;

        IReadOnlyList<EnemyEntrySnapshot> enemies = snapshot.EnmityList ?? Array.Empty<EnemyEntrySnapshot>();
        var isSample = false;

        if (enemies.Count == 0 && preview)
        {
            enemies = SampleEnemies;
            isSample = true;
        }

        if (enemies.Count == 0)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetBackgroundDrawList();
        var (pos, size) = LayoutManager.GetRect(Element.Layout);

        // The part of a cast that hangs below its bar takes room in the column (the next bar starts under it); at the sides it takes none.
        var progressBarBlock = 0f;
        if (showProgressBarNow)
        {
            var (progressBarPos, _) = ProgressBarRect(settings, scale, Vector2.Zero, size);
            progressBarBlock = MathF.Max(0f, progressBarPos.Y + MathF.Round(settings.ProgressBarHeightPx * scale) - size.Y);
        }
        var step = size.Y + progressBarBlock + MathF.Round(settings.SpacingPx * scale);
        var targetId = snapshot.Target?.EntityId;

        // The box is always 8 rows tall. With "Start from the bottom" the rows sit at the bottom of it (empty slots on top).
        var slots = Math.Max(BoxSlots, enemies.Count);
        var slotOffset = settings.StartFromBottom ? slots - enemies.Count : 0;

        var (firstMin, firstMax) = RowBounds(settings, scale, new Vector2(pos.X, MathF.Round(pos.Y)), size);
        var (lastMin, lastMax) = RowBounds(settings, scale, new Vector2(pos.X, MathF.Round(pos.Y + step * (slots - 1))), size);
        var boxMin = Vector2.Min(firstMin, lastMin);
        var boxMax = Vector2.Max(firstMax, lastMax);

        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            var rowPos = new Vector2(pos.X, MathF.Round(pos.Y + step * (i + slotOffset)));

            var isTarget = !isSample && enemy.Unit.EntityId == targetId;
            var index = i;
            // The part of the screen the row covers: the bar and, when it is shown, the place of its cast.
            var (rowMin, rowMax) = RowBounds(settings, scale, rowPos, size);
            plugin.GameCover.Draw(rowMin, rowMax - rowMin, 400f * scale, () =>
                DrawRow(drawList, scale, settings, enemy, rowPos, size, isTarget, index));

            boxMin = Vector2.Min(boxMin, rowMin);
            boxMax = Vector2.Max(boxMax, rowMax);

            if (!isSample && enemy.Targetable != 0 && enemy.ActiveInList != 0 && !plugin.Layout.UnlockMode)
                plugin.HudBarSystem.HandleClicks($"enemy.{enemy.Unit.EntityId}", rowPos, size, enemy.Unit, contextMenu: false);
        }

        Element.DisplayRect = (boxMin, boxMax - boxMin);
    }

    private void DrawRow(ImDrawListPtr drawList, float scale, EnmityListSettings settings, EnemyEntrySnapshot enemy, Vector2 pos, Vector2 size, bool isTarget, int index)
    {
        var unit = enemy.Unit;

        // Values read in game: normal = act 1 lock 0 tgt 1.
        //  - tgt 0 (act 0 lock 0 too): the enemy cannot be targeted right now (closed phase): the game draws its row half transparent.
        //  - act 0 with tgt 1: still listed but too far to be selected (the game shows its hover glow but the click does nothing):
        //    the game fades its row a little (our guess is that "act" means "in range").
        var untargetable = enemy.Targetable == 0;
        var outOfRange = !untargetable && enemy.ActiveInList == 0;
        var fadeMark = DrawListFade.Mark(drawList);

        var max = pos + size;
        var rounding = MathF.Min(MathF.Round(GlobalColors.Current.Rounding * scale), size.Y / 2f);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        // The HP is the full rounded bar cut by a clip rectangle, so it never sticks out of the corners.
        var fraction = unit.MaxHp == 0 ? 0f : Math.Clamp(unit.Hp / (float)unit.MaxHp, 0f, 1f);
        var fillRight = MathF.Round(pos.X + size.X * fraction);
        if (fillRight > pos.X)
        {
            drawList.PushClipRect(pos, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.HpColor), rounding);
            drawList.PopClipRect();
        }

        drawList.AddRect(pos, max, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        if (isTarget)
        {
            // A line is centered on its rectangle: the rectangle is pushed out by half the line so the ring starts exactly where
            // the bar ends (no gap of screen showing between the bar and the ring).
            var ringWidth = thickness * 2f;
            var ring = new Vector2(ringWidth / 2f, ringWidth / 2f);
            drawList.AddRect(pos - ring, max + ring, ImGui.GetColorU32(plugin.Configuration.TargetRingColor), rounding + ringWidth / 2f, ImDrawFlags.None, ringWidth);
        }

        // The dot: one color when the enemy is attacking you, another when it is attacking someone else.
        if (settings.ShowEnmityDot)
        {
            // A point of the dot goes on a point of the bar, then the offsets move it (like every other image or bar of the interface).
            var diameter = MathF.Max(4f, MathF.Round(settings.EnmityDotSizePx * scale));
            var radius = diameter / 2f;
            var dotMin = pos + size * settings.EnmityDotAnchor.Fraction() - new Vector2(diameter, diameter) * settings.EnmityDotSelfAnchor.Fraction()
                         + new Vector2(settings.EnmityDotOffsetX, settings.EnmityDotOffsetY) * scale;
            var center = new Vector2(MathF.Round(dotMin.X + radius), MathF.Round(dotMin.Y + radius));
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(enemy.TargetsMe ? settings.OnMeColor : settings.OnOthersColor));
            drawList.AddCircle(center, radius, ImGui.GetColorU32(settings.BorderColor), 0, thickness);
        }

        // The percentage and the name are attached to the whole bar (the dot is not taken into account: the offset of the name
        // counts from the edge of the bar). The name is shortened with "..." so it never goes out of the bar or runs into the percentage.
        string? percentText = null;
        if (settings.Hp.Enabled && unit.MaxHp > 0)
            percentText = $"{MathF.Floor(fraction * 100f):0}%";

        if (settings.Name.Enabled)
        {
            var percentArea = percentText is null ? null : plugin.HudBarSystem.MeasureText(percentText, settings.Hp, pos, size);
            plugin.HudBarSystem.DrawText(drawList, unit.Name, settings.Name, pos, size, fitInBox: true, avoid: percentArea);
        }

        if (percentText is not null)
            plugin.HudBarSystem.DrawText(drawList, percentText, settings.Hp, pos, size);

        if (showProgressBarNow && unit.Cast is not null && unit.Cast.TotalTime > 0f)
        {
            var (progressBarPos, progressBarWidth) = ProgressBarRect(settings, scale, pos, size);
            DrawProgressBar(drawList, scale, settings, unit.Cast, progressBarPos.X, progressBarPos.Y, progressBarWidth);
        }

        // The rows of status icons of the enemy (the preview shows sample icons).
        var playerId = plugin.GameData.Current.Player?.EntityId ?? 0u;
        var sampleRow = unit.EntityId == 0;
        plugin.StatusRows.DrawRows(drawList, settings.Enhancements, settings.Enfeeblements,
            sampleRow ? Modules.HudBars.StatusRowRenderer.PreviewEnhancements : unit.Statuses,
            sampleRow ? Modules.HudBars.StatusRowRenderer.PreviewEnfeeblements : unit.Statuses,
            sampleRow, playerId, pos, size, "enemy", friendly: false);

        // One fade for both: an enemy that cannot be targeted or is out of range (the same value as every other faded thing).
        if (untargetable || outOfRange)
            DrawListFade.FadeSince(drawList, fadeMark, Fade.Alpha);
    }

    // Where the cast of a bar goes: a point of the cast on a point of the bar, then moved by the offsets (plus Y is up in the page, saved downwards).
    private static (Vector2 Pos, float Width) ProgressBarRect(EnmityListSettings settings, float scale, Vector2 barPos, Vector2 barSize)
    {
        var width = settings.ProgressBarWidthPx > 0 ? MathF.Round(settings.ProgressBarWidthPx * scale) : barSize.X;
        var height = MathF.Round(settings.ProgressBarHeightPx * scale);

        var pos = barPos + barSize * settings.ProgressBarBarAnchor.Fraction() - new Vector2(width, height) * settings.ProgressBarSelfAnchor.Fraction();
        pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
        pos += new Vector2(MathF.Round(settings.ProgressBarOffsetX * scale), MathF.Round(settings.ProgressBarOffsetY * scale));
        return (pos, width);
    }

    // The box of a row: the bar and the place of its cast (when the game's option shows the casts).
    private (Vector2 Min, Vector2 Max) RowBounds(EnmityListSettings settings, float scale, Vector2 barPos, Vector2 barSize)
    {
        var min = barPos;
        var max = barPos + barSize;

        if (showProgressBarNow)
        {
            var (progressBarPos, progressBarWidth) = ProgressBarRect(settings, scale, barPos, barSize);
            var progressBarEnd = progressBarPos + new Vector2(progressBarWidth, MathF.Round(settings.ProgressBarHeightPx * scale));
            min = Vector2.Min(min, progressBarPos);
            max = Vector2.Max(max, progressBarEnd);
        }

        return (min, max);
    }

    // The cast of a row. It uses its own colors (Enmity list page): the fill color says if you can interrupt the cast.
    private void DrawProgressBar(ImDrawListPtr drawList, float scale, EnmityListSettings settings, CastSnapshot cast, float left, float top, float width)
    {
        var height = MathF.Round(settings.ProgressBarHeightPx * scale);
        var rounding = MathF.Min(MathF.Round(GlobalColors.Current.Rounding * scale), height / 2f);
        var min = new Vector2(left, top);
        var max = min + new Vector2(width, height);
        var color = cast.Interruptible ? settings.ProgressBarColor : settings.ProgressBarUninterruptibleColor;

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(settings.ProgressBarBackgroundColor), rounding);

        var fraction = Math.Clamp(cast.CurrentTime / cast.TotalTime, 0f, 1f);
        var fillRight = MathF.Round(min.X + width * fraction);
        if (fillRight > min.X)
        {
            drawList.PushClipRect(min, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(color), rounding);
            drawList.PopClipRect();
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(settings.ProgressBarBorderColor), rounding, ImDrawFlags.None, MathF.Max(1f, MathF.Round(scale)));

        // The action name and the time left: texts attached to the progress bar, like the other frames.
        var boxSize = max - min;
        var timeText = settings.ProgressBarTimeMode == NahjaUI.Modules.ProgressBars.ProgressBarTimeMode.Remaining
            ? MathF.Max(0f, cast.TotalTime - cast.CurrentTime).ToString("0.0")
            : $"{cast.CurrentTime:0.0} / {cast.TotalTime:0.0}";
        if (settings.ProgressBarName.Enabled)
        {
            var timeArea = plugin.HudBarSystem.MeasureText(timeText, settings.ProgressBarTime, min, boxSize);
            plugin.HudBarSystem.DrawText(drawList, GameNames.CastName(cast.ActionType, cast.ActionId), settings.ProgressBarName, min, boxSize, fitInBox: true, avoid: timeArea);
        }

        if (settings.ProgressBarTime.Enabled)
            plugin.HudBarSystem.DrawText(drawList, timeText, settings.ProgressBarTime, min, boxSize);
    }
}
