using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NahjaUI.Modules.Auras;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Modules.NamePlates;

/// <summary>
/// Draws our own plates over the enemies (a life bar and a name) and hides the game's plate of those enemies.
/// It only reads the game snapshot; the plates themselves (which enemies, where) are found in GameDataProvider.
/// </summary>
internal sealed class NamePlateModule : IDisposable
{
    // The kind of plate of an enemy (UIObjectKind.BattleNpcEnemy in the game's own list).
    private const int EnemyKind = 3;

    private static readonly Vector4 BackgroundColor = new(0.04f, 0.035f, 0.09f, 0.92f);
    private static readonly Vector4 BorderColor = new(0f, 0f, 0f, 1f);
    private static readonly Vector4 EnemyColor = new(0.79f, 0.29f, 0.27f, 1f);

    // The game's own cast bars of enemies, found in the game's addon list.
    private static readonly string[] NativeCastBars = { "CastBarEnemy" };

    private readonly Plugin plugin;

    public NamePlateModule(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.NamePlateGui.OnDataUpdate += OnDataUpdate;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeCastBars, OnNativeCastPreDraw);
    }

    public void Dispose()
    {
        Plugin.NamePlateGui.OnDataUpdate -= OnDataUpdate;
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeCastBars, OnNativeCastPreDraw);
        Plugin.NamePlateGui.RequestRedraw();
    }

    // The game is about to draw its plates: switch off the name and the life bar of the enemies.
    private void OnDataUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        var settings = plugin.Configuration.NamePlates;
        if (!settings.Enabled || !settings.HideNative)
            return;

        foreach (var handler in handlers)
        {
            if ((int)handler.NamePlateKind == EnemyKind)
                handler.VisibilityFlags = 0;
        }
    }

    // Same trick as the other native windows: do not let the game draw it and keep it hidden.
    private unsafe void OnNativeCastPreDraw(AddonEvent type, AddonArgs args)
    {
        var settings = plugin.Configuration.NamePlates;
        if (!settings.Enabled || !settings.ShowCast || !settings.HideNativeCast)
            return;

        args.PreventOriginal();

        var addon = args.GetAddonPointer<AtkUnitBase>();
        if (addon != null)
            addon->IsVisible = false;
    }

    public void Draw()
    {
        var settings = plugin.Configuration.NamePlates;

        // Tell the provider whether to read the plates at all (it costs nothing while this is off).
        plugin.GameData.WantNamePlates = settings.Enabled;
        if (!settings.Enabled)
            return;

        var snapshot = plugin.GameData.Current;
        if (!snapshot.IsLoggedIn || snapshot.NamePlates is null)
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var targetId = snapshot.Target?.EntityId;

        foreach (var plate in snapshot.NamePlates)
            DrawPlate(drawList, scale, settings, plate, plate.EntityId == targetId, snapshot.Player?.Level ?? 0);
    }

    private void DrawPlate(ImDrawListPtr drawList, float scale, NamePlateSettings settings, NamePlateSnapshot plate, bool isTarget, int playerLevel)
    {
        // Optional: far enemies get a smaller plate, like in the game (never smaller than 60%).
        if (settings.ScaleWithDistance)
            scale *= 0.6f + 0.4f * plate.Scale;

        var width = MathF.Round(settings.WidthPx * scale);
        var height = MathF.Round(settings.HeightPx * scale);
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), height / 2f);

        // The game's point is in the range -1 to 1 with y going up: turn it into pixels of the screen.
        var size = ImGuiHelpers.MainViewport.Size;
        var screen = new Vector2((plate.Ndc.X + 1f) * 0.5f * size.X, (1f - plate.Ndc.Y) * 0.5f * size.Y);

        // The bar is centred under that point; the name above it overlaps the top edge.
        var min = new Vector2(MathF.Round(screen.X - width / 2f), MathF.Round(screen.Y));
        var max = min + new Vector2(width, height);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(BackgroundColor), rounding);

        // The fill is the full rounded bar cut by a clip rectangle, so a short fill never sticks out of the corners.
        var fraction = plate.MaxHp == 0 ? 0f : Math.Clamp(plate.Hp / (float)plate.MaxHp, 0f, 1f);
        var fillRight = MathF.Round(min.X + width * fraction);
        if (fillRight > min.X)
        {
            drawList.PushClipRect(min, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(EnemyColor), rounding);
            drawList.PopClipRect();
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(BorderColor), rounding, ImDrawFlags.None, 1f);

        if (isTarget)
        {
            var ring = new Vector2(2f * scale, 2f * scale);
            drawList.AddRect(min - ring, max + ring, ImGui.GetColorU32(Theme.Accent), rounding + 2f * scale, ImDrawFlags.None, 2f * scale);
        }

        if (settings.ShowName)
        {
            var showLevel = settings.LevelMode != NamePlateLevelMode.Hidden
                            && !(settings.HideSameLevel && plate.Level == playerLevel);
            var levelText = settings.LevelMode == NamePlateLevelMode.WithPrefix ? $"Lv {plate.Level}" : plate.Level.ToString();
            var text = showLevel ? $"{plate.Name}  {levelText}" : plate.Name;
            using (plugin.Fonts.PushClosest(settings.NameFont, settings.NameSize))
            {
                // The same point of the text is placed on the chosen point of the bar, then the offset moves it.
                var textSize = ImGui.CalcTextSize(text);
                var anchorFraction = settings.NameAnchor.Fraction();
                var pos = min + (max - min) * anchorFraction - textSize * anchorFraction + new Vector2(settings.NameOffsetX, settings.NameOffsetY) * scale;
                pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
                if (showLevel)
                {
                    // The name and the level are two pieces, so the level can have its own color.
                    var namePart = $"{plate.Name}  ";
                    OutlinedText.Draw(drawList, pos, namePart, ImGui.GetColorU32(settings.NameColor));
                    var levelPos = new Vector2(MathF.Round(pos.X + ImGui.CalcTextSize(namePart).X), pos.Y);
                    OutlinedText.Draw(drawList, levelPos, levelText, ImGui.GetColorU32(settings.LevelColor));
                }
                else
                {
                    OutlinedText.Draw(drawList, pos, text, ImGui.GetColorU32(settings.NameColor));
                }
            }
        }

        // Clicks: a see-through window over the name and the bar, like the unit frames use.
        if (settings.Clickable && !plugin.Layout.UnlockMode)
        {
            var nameRoom = MathF.Round(12f * scale);
            var unit = new UnitSnapshot(
                plate.EntityId, plate.Name, 0, plate.Level, plate.Hp, plate.MaxHp, 0, 0, false, null,
                Array.Empty<StatusSnapshot>(), IsHostile: true);

            plugin.UnitFrameSystem.HandleClicks($"plate_{plate.EntityId}", min - new Vector2(0f, nameRoom), new Vector2(width, height + nameRoom), unit);
        }

        if (settings.ShowCast && plate.Cast is not null && plate.Cast.TotalTime > 0f)
            DrawCast(drawList, scale, settings, plate.Cast, min.X, max.Y + MathF.Round(settings.CastGapPx * scale), width);
    }

    // The cast frame: the fill, the spell name and the time. It looks like the cast bar of the target (same colors, from the
    // Cast bars page): the color of the fill says if you can interrupt the cast or not.
    private void DrawCast(ImDrawListPtr drawList, float scale, NamePlateSettings settings, CastSnapshot cast, float left, float top, float width)
    {
        var look = plugin.Configuration.CastBars.Target;
        var height = MathF.Round(settings.CastHeightPx * scale);
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), height / 2f);
        var min = new Vector2(left, top);
        var max = min + new Vector2(width, height);
        var color = cast.Interruptible ? look.BarColor : look.UninterruptibleColor;

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(look.BackgroundColor), rounding);

        // The fill is the full rounded frame cut by a clip rectangle, so it never sticks out of the corners.
        var fraction = Math.Clamp(cast.CurrentTime / cast.TotalTime, 0f, 1f);
        var fillRight = MathF.Round(min.X + width * fraction);

        if (fillRight > min.X)
        {
            drawList.PushClipRect(min, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(color), rounding);
            drawList.PopClipRect();
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(look.BorderColor), rounding, ImDrawFlags.None, 1f);

        var padding = 6f * scale;
        using (plugin.Fonts.PushClosest(settings.CastFont, settings.CastTextSize))
        {
            var y = MathF.Round((min.Y + max.Y - ImGui.GetTextLineHeight()) / 2f);
            var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

            // The time is at the right, the spell name at the left (shortened with "..." if it does not fit).
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
        if (available <= 0f)
            return string.Empty;

        if (ImGui.CalcTextSize(text).X <= available)
            return text;

        while (text.Length > 1)
        {
            text = text[..^1];

            var candidate = text + "...";
            if (ImGui.CalcTextSize(candidate).X <= available)
                return candidate;
        }

        return string.Empty;
    }
}
