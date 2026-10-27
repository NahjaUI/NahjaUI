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

namespace NahjaUI.Modules.EnemyList;

/// <summary>
/// Draws our own enemy list: the same enemies as the game's list (the ones that are fighting you, up to 8), one bar each
/// with the name, the life, a dot that says who the enemy is attacking, and its cast under the bar.
/// It only reads the game snapshot.
/// </summary>
internal sealed class EnemyListModule : IDisposable
{
    // The game's own enemy list.
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

    public EnemyListModule(Plugin plugin)
    {
        this.plugin = plugin;

        // The box of the first bar. The others follow it downwards.
        Element = plugin.Layout.Register("enemylist", "Enemy list", new ElementLayout
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
        var settings = plugin.Configuration.EnemyList;
        if (!settings.Enabled || !settings.HideNative)
            return;

        args.PreventOriginal();

        var addon = args.GetAddonPointer<AtkUnitBase>();
        if (addon != null)
            addon->IsVisible = false;
    }

    public void Draw()
    {
        var settings = plugin.Configuration.EnemyList;

        // Tell the provider whether to read the list at all (it costs nothing while this is off).
        plugin.GameData.WantEnemyList = settings.Enabled;

        var preview = PreviewRequested || plugin.Layout.UnlockMode;
        PreviewRequested = false;

        if (!settings.Enabled)
            return;

        var snapshot = plugin.GameData.Current;
        if (!snapshot.IsLoggedIn)
            return;

        IReadOnlyList<EnemyEntrySnapshot> enemies = snapshot.EnemyList ?? Array.Empty<EnemyEntrySnapshot>();
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

        var castBlock = settings.ShowCast ? MathF.Round((settings.CastGapPx + settings.CastHeightPx) * scale) : 0f;
        var step = size.Y + castBlock + MathF.Round(settings.SpacingPx * scale);
        var targetId = snapshot.Target?.EntityId;

        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            var rowPos = new Vector2(pos.X, MathF.Round(pos.Y + step * i));

            var isTarget = !isSample && enemy.Unit.EntityId == targetId;
            var index = i;
            plugin.GameCover.Draw(rowPos, size + new Vector2(0f, castBlock), 400f * scale, () =>
                DrawRow(drawList, scale, settings, enemy, rowPos, size, isTarget, index));

            if (!isSample && settings.Clickable && !plugin.Layout.UnlockMode)
                plugin.UnitFrameSystem.HandleClicks($"enemy.{enemy.Unit.EntityId}", rowPos, size, enemy.Unit);
        }

        Element.DisplayRect = (pos, new Vector2(size.X, (enemies.Count - 1) * step + size.Y + castBlock));
    }

    private void DrawRow(ImDrawListPtr drawList, float scale, EnemyListSettings settings, EnemyEntrySnapshot enemy, Vector2 pos, Vector2 size, bool isTarget, int index)
    {
        var unit = enemy.Unit;
        var max = pos + size;
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), size.Y / 2f);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        // The life is the full rounded bar cut by a clip rectangle, so it never sticks out of the corners.
        var fraction = unit.MaxHp == 0 ? 0f : Math.Clamp(unit.Hp / (float)unit.MaxHp, 0f, 1f);
        var fillRight = MathF.Round(pos.X + size.X * fraction);
        if (fillRight > pos.X)
        {
            drawList.PushClipRect(pos, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.LifeColor), rounding);
            drawList.PopClipRect();
        }

        drawList.AddRect(pos, max, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        if (isTarget)
        {
            var ring = new Vector2(thickness * 2f, thickness * 2f);
            drawList.AddRect(pos - ring, max + ring, ImGui.GetColorU32(Theme.Accent), rounding + thickness * 2f, ImDrawFlags.None, thickness * 2f);
        }

        // The dot: one color when the enemy is attacking you, another when it is attacking someone else.
        var textLeft = pos.X + MathF.Round(8f * scale);
        if (settings.ShowAggroDot)
        {
            var radius = MathF.Max(3f, MathF.Round(size.Y * 0.22f));
            var center = new Vector2(pos.X + MathF.Round(8f * scale) + radius, pos.Y + size.Y / 2f);
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(enemy.TargetsMe ? settings.OnMeColor : settings.OnOthersColor));
            drawList.AddCircle(center, radius, ImGui.GetColorU32(settings.BorderColor), 0, thickness);
            textLeft = center.X + radius + MathF.Round(6f * scale);
        }

        using (plugin.Fonts.PushClosest(settings.Font, settings.TextSize))
        {
            var y = MathF.Round(pos.Y + (size.Y - ImGui.GetTextLineHeight()) / 2f);
            var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
            var right = max.X - MathF.Round(6f * scale);

            if (settings.ShowPercent && unit.MaxHp > 0)
            {
                var text = $"{MathF.Floor(fraction * 100f):0}%";
                var width = ImGui.CalcTextSize(text).X;
                OutlinedText.Draw(drawList, new Vector2(MathF.Round(right - width), y), text, white);
                right -= width + MathF.Round(6f * scale);
            }

            OutlinedText.Draw(drawList, new Vector2(MathF.Round(textLeft), y), Fit(unit.Name, right - textLeft), white);
        }

        if (settings.ShowCast && unit.Cast is not null && unit.Cast.TotalTime > 0f)
            DrawCast(drawList, scale, settings, unit.Cast, pos.X, max.Y + MathF.Round(settings.CastGapPx * scale), size.X);
    }

    // The cast under the bar. It uses the colors of the target cast bar: its fill color says if you can interrupt the cast.
    private void DrawCast(ImDrawListPtr drawList, float scale, EnemyListSettings settings, CastSnapshot cast, float left, float top, float width)
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

        var padding = MathF.Round(6f * scale);
        using (plugin.Fonts.PushClosest(settings.Font, Math.Max(10, settings.TextSize - 2)))
        {
            var y = MathF.Round((min.Y + max.Y - ImGui.GetTextLineHeight()) / 2f);
            var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

            var timeText = MathF.Max(0f, cast.TotalTime - cast.CurrentTime).ToString("0.0");
            var timeWidth = ImGui.CalcTextSize(timeText).X;
            OutlinedText.Draw(drawList, new Vector2(MathF.Round(max.X - padding - timeWidth), y), timeText, white);

            var name = Fit(GameNames.CastName(cast.ActionType, cast.ActionId), width - padding * 3f - timeWidth);
            OutlinedText.Draw(drawList, new Vector2(MathF.Round(min.X + padding), y), name, white);
        }
    }

    // Shortens a text with "..." until it fits the width. The font must already be the one used to draw it.
    private static string Fit(string text, float available)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= available)
            return text;

        for (var length = text.Length - 1; length > 0; length--)
        {
            var shorter = text[..length] + "...";
            if (ImGui.CalcTextSize(shorter).X <= available)
                return shorter;
        }

        return string.Empty;
    }
}
