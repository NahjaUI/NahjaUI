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

    // How see-through a row of an enemy that cannot be targeted is (the game does about half).
    private const float UntargetableAlpha = 0.5f;

    // How see-through a row of an enemy that is listed but out of range is (a lighter fade than the closed phase; an estimate).
    private const float OutOfRangeAlpha = 0.7f;
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

        // The box of the first bar. The others follow it downwards.
        Element = plugin.Layout.Register("enmitylist", "Enmity list", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.TopRight,
            SelfAnchor = AnchorPoint.TopRight,
            OffsetX = -60,
            OffsetY = 260,
            Width = 210,
            Height = 18
        });

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
    }

    public LayoutElement Element { get; }

    /// <summary>Set by the settings page every frame it is open: sample enemies are shown when nobody is fighting you.</summary>
    public bool PreviewRequested { get; set; }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
    }

    private static EnemyEntrySnapshot Sample(string name, uint hp, uint maxHp, bool onMe, CastSnapshot? cast) =>
        new(new UnitSnapshot(0, name, 0, 100, hp, maxHp, 0, 0, false, cast, Array.Empty<StatusSnapshot>(), IsHostile: true), onMe);

    // Runs right before the game draws its own list: marking it invisible makes the game skip it.
    private unsafe void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        var settings = plugin.Configuration.EnmityList;
        // Test aid: leave the game's list alone so both can be compared on screen.
        if (!settings.Enabled || settings.ShowGameList)
            return;

        hider.Hide(args);
    }

    // The game's own options of its enmity list (checked in game: 1 = on, 0 = off). Ours follow them.
    //  - UiConfig EnemyListCastbarEnable: show the cast bars.
    //  - UiControl EnemyListDisp: show the enmity list at all.
    // (UiConfig "EnmityList" always read 0: not used.) When an option cannot be read it counts as on.
    private bool showCastNow = true;
    private bool listShownNow = true;

    private void ReadGameOptions()
    {
        showCastNow = !(Plugin.GameConfig.UiConfig.TryGet("EnemyListCastbarEnable", out uint cast) && cast == 0);
        listShownNow = !(Plugin.GameConfig.UiControl.TryGet("EnemyListDisp", out uint disp) && disp == 0);
    }

    public void Draw()
    {
        var settings = plugin.Configuration.EnmityList;
        hider.ShowAgain(_ => settings.Enabled && !settings.ShowGameList);
        ReadGameOptions();

        // Tell the provider whether to read the list at all (it costs nothing while this is off).
        plugin.GameData.WantEnmityList = settings.Enabled && listShownNow;

        var preview = PreviewRequested || plugin.Layout.UnlockMode;
        PreviewRequested = false;

        if (!settings.Enabled)
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

        // A cast under the bar takes room in the column (the next bar starts below it); at the sides it takes none.
        var castBlock = showCastNow && settings.CastSide == EnemyCastSide.Below
            ? MathF.Round((settings.CastGapPx + settings.CastHeightPx) * scale) + MathF.Max(0f, MathF.Round(settings.CastOffsetY * scale))
            : 0f;
        var step = size.Y + castBlock + MathF.Round(settings.SpacingPx * scale);
        var targetId = snapshot.Target?.EntityId;

        var boxMin = pos;
        var boxMax = pos + size;

        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            var rowPos = new Vector2(pos.X, MathF.Round(pos.Y + step * i));

            var isTarget = !isSample && enemy.Unit.EntityId == targetId;
            var index = i;
            // The part of the screen the row covers: the bar and, when it is shown, the place of its cast.
            var (rowMin, rowMax) = RowBounds(settings, scale, rowPos, size);
            if (settings.ShowGameList)
                DrawRow(drawList, scale, settings, enemy, rowPos, size, isTarget, index); // no cover: the game's list stays visible
            else
                plugin.GameCover.Draw(rowMin, rowMax - rowMin, 400f * scale, () =>
                    DrawRow(drawList, scale, settings, enemy, rowPos, size, isTarget, index));

            boxMin = Vector2.Min(boxMin, rowMin);
            boxMax = Vector2.Max(boxMax, rowMax);

            if (!isSample && enemy.Targetable != 0 && enemy.ActiveInList != 0 && !plugin.Layout.UnlockMode)
                plugin.UnitFrameSystem.HandleClicks($"enemy.{enemy.Unit.EntityId}", rowPos, size, enemy.Unit, contextMenu: false);
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
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), size.Y / 2f);
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

        if (settings.ShowGameValues && enemy.ActiveInList >= 0)
        {
            using (var face = plugin.Fonts.PushCap(settings.Name.Font, 9))
                face.Draw(drawList, new Vector2(max.X + MathF.Round(6f * scale), pos.Y), $"act {enemy.ActiveInList}  lock {enemy.LockedInList}  tgt {enemy.Targetable}", ImGui.GetColorU32(new Vector4(0.6f, 1f, 0.6f, 1f)));
        }

        // The dot: one color when the enemy is attacking you, another when it is attacking someone else.
        var textLeft = pos.X + MathF.Round(8f * scale);
        if (settings.ShowEnmityDot)
        {
            // The dot is attached to a point of the bar (the same point of the dot goes there), then the offsets move it.
            var diameter = MathF.Max(4f, MathF.Round(settings.EnmityDotSizePx * scale));
            var radius = diameter / 2f;
            var dotFraction = settings.EnmityDotAnchor.Fraction();
            var dotMin = pos + size * dotFraction - new Vector2(diameter * dotFraction.X, radius)
                         + new Vector2(settings.EnmityDotOffsetX, settings.EnmityDotOffsetY) * scale;
            var center = new Vector2(MathF.Round(dotMin.X + radius), MathF.Round(dotMin.Y + radius));
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(enemy.TargetsMe ? settings.OnMeColor : settings.OnOthersColor));
            drawList.AddCircle(center, radius, ImGui.GetColorU32(settings.BorderColor), 0, thickness);

            // The name starts after the dot when the dot is in the left half of the bar.
            if (dotFraction.X < 0.5f)
                textLeft = MathF.Max(textLeft, center.X + radius + MathF.Round(6f * scale));
        }

        // The percentage is attached to the whole bar. The name is attached to the part of the bar after the dot, and it is
        // shortened with "..." so it never runs into the percentage.
        string? percentText = null;
        if (settings.Health.Enabled && unit.MaxHp > 0)
            percentText = $"{MathF.Floor(fraction * 100f):0}%";

        var percentRoom = 0f;
        if (percentText is not null && settings.Health.Anchor.Fraction().X >= 0.5f)
        {
            using (var face = plugin.Fonts.PushCap(settings.Health.Font, settings.Health.TextSize))
                percentRoom = face.Width(percentText) + MathF.Round(12f * scale);
        }

        if (settings.Name.Enabled)
        {
            var nameBoxWidth = MathF.Max(1f, max.X - textLeft);
            string nameText;
            using (var face = plugin.Fonts.PushCap(settings.Name.Font, settings.Name.TextSize))
                nameText = Fit(face, unit.Name, nameBoxWidth - percentRoom - MathF.Round(4f * scale));

            plugin.UnitFrameSystem.DrawText(drawList, nameText, settings.Name, new Vector2(textLeft, pos.Y), new Vector2(nameBoxWidth, size.Y));
        }

        if (percentText is not null)
            plugin.UnitFrameSystem.DrawText(drawList, percentText, settings.Health, pos, size);

        if (showCastNow && unit.Cast is not null && unit.Cast.TotalTime > 0f)
        {
            var (castPos, castWidth) = CastRect(settings, scale, pos, size);
            DrawCast(drawList, scale, settings, unit.Cast, castPos.X, castPos.Y, castWidth);
        }

        if (untargetable)
            DrawListFade.FadeSince(drawList, fadeMark, UntargetableAlpha);
        else if (outOfRange)
            DrawListFade.FadeSince(drawList, fadeMark, OutOfRangeAlpha);
    }

    // Where the cast of a bar goes: on the chosen side, then moved by the offsets (plus Y is up in the page, saved downwards).
    private static (Vector2 Pos, float Width) CastRect(EnmityListSettings settings, float scale, Vector2 barPos, Vector2 barSize)
    {
        var width = settings.CastWidthPx > 0 ? MathF.Round(settings.CastWidthPx * scale) : barSize.X;
        var height = MathF.Round(settings.CastHeightPx * scale);
        var gap = MathF.Round(settings.CastGapPx * scale);

        var pos = settings.CastSide switch
        {
            EnemyCastSide.Right => new Vector2(barPos.X + barSize.X + gap, barPos.Y + MathF.Round((barSize.Y - height) / 2f)),
            EnemyCastSide.Left => new Vector2(barPos.X - gap - width, barPos.Y + MathF.Round((barSize.Y - height) / 2f)),
            _ => new Vector2(barPos.X, barPos.Y + barSize.Y + gap)
        };

        pos += new Vector2(MathF.Round(settings.CastOffsetX * scale), MathF.Round(settings.CastOffsetY * scale));
        return (pos, width);
    }

    // The box of a row: the bar and the place of its cast (when the game's option shows the casts).
    private (Vector2 Min, Vector2 Max) RowBounds(EnmityListSettings settings, float scale, Vector2 barPos, Vector2 barSize)
    {
        var min = barPos;
        var max = barPos + barSize;

        if (showCastNow)
        {
            var (castPos, castWidth) = CastRect(settings, scale, barPos, barSize);
            var castEnd = castPos + new Vector2(castWidth, MathF.Round(settings.CastHeightPx * scale));
            min = Vector2.Min(min, castPos);
            max = Vector2.Max(max, castEnd);
        }

        return (min, max);
    }

    // The cast under the bar. It uses the colors of the target cast bar: its fill color says if you can interrupt the cast.
    private void DrawCast(ImDrawListPtr drawList, float scale, EnmityListSettings settings, CastSnapshot cast, float left, float top, float width)
    {
        var look = plugin.Configuration.CastBars.Target;
        var height = MathF.Round(settings.CastHeightPx * scale);
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), height / 2f);
        var min = new Vector2(left, top);
        var max = min + new Vector2(width, height);
        var color = cast.Interruptible ? look.BarColor : look.UninterruptibleColor;

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(look.BackgroundColor), rounding);

        var fraction = Math.Clamp(cast.CurrentTime / cast.TotalTime, 0f, 1f);
        var fillRight = MathF.Round(min.X + width * fraction);
        if (fillRight > min.X)
        {
            drawList.PushClipRect(min, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(color), rounding);
            drawList.PopClipRect();
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(look.BorderColor), rounding, ImDrawFlags.None, MathF.Max(1f, MathF.Round(scale)));

        // The spell name and the time left: texts attached to the cast bar, like the other frames.
        var boxSize = max - min;
        var timeText = MathF.Max(0f, cast.TotalTime - cast.CurrentTime).ToString("0.0");
        var timeRoom = 0f;
        if (settings.CastTime.Enabled && settings.CastTime.Anchor.Fraction().X >= 0.5f)
        {
            using (var face = plugin.Fonts.PushCap(settings.CastTime.Font, settings.CastTime.TextSize))
                timeRoom = face.Width(timeText) + MathF.Round(12f * scale);
        }

        if (settings.CastName.Enabled)
        {
            string name;
            using (var face = plugin.Fonts.PushCap(settings.CastName.Font, settings.CastName.TextSize))
                name = Fit(face, GameNames.CastName(cast.ActionType, cast.ActionId), width - timeRoom - MathF.Round(8f * scale));

            plugin.UnitFrameSystem.DrawText(drawList, name, settings.CastName, min, boxSize);
        }

        if (settings.CastTime.Enabled)
            plugin.UnitFrameSystem.DrawText(drawList, timeText, settings.CastTime, min, boxSize);
    }

    // Shortens a text with "..." until it fits the width. The font must already be the one used to draw it.
    private static string Fit(TextFace face, string text, float available)
    {
        if (string.IsNullOrEmpty(text) || face.Width(text) <= available)
            return text;

        for (var length = text.Length - 1; length > 0; length--)
        {
            var shorter = text[..length] + "...";
            if (face.Width(shorter) <= available)
                return shorter;
        }

        return string.Empty;
    }
}
