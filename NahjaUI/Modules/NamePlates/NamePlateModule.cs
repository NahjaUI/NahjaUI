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

    // Cast frame: amber when you can interrupt the cast, steel gray when you cannot.
    private static readonly Vector4 CastAmber = new(0.91f, 0.69f, 0.29f, 1f);
    private static readonly Vector4 CastAmberGlow = new(1f, 0.89f, 0.64f, 1f);
    private static readonly Vector4 CastSteel = new(0.43f, 0.47f, 0.60f, 1f);
    private static readonly Vector4 CastSteelBorder = new(0.71f, 0.74f, 0.84f, 1f);

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

    /// <summary>Turns the plates on or off and makes the game redraw its own plates.</summary>
    public void Toggle()
    {
        var settings = plugin.Configuration.NamePlates;
        settings.Enabled = !settings.Enabled;
        Plugin.NamePlateGui.RequestRedraw();
        plugin.RequestSave();
        Plugin.Log.Information("NahjaUI nameplates: {State}", settings.Enabled ? "on" : "off");
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
            DrawPlate(drawList, scale, settings, plate, plate.EntityId == targetId);
    }

    private void DrawPlate(ImDrawListPtr drawList, float scale, NamePlateSettings settings, NamePlateSnapshot plate, bool isTarget)
    {
        // Far enemies get a smaller plate, like in the game (never smaller than 60%).
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

        var text = settings.ShowLevel ? $"{plate.Name}  Lv {plate.Level}" : plate.Name;
        using (plugin.Fonts.PushClosest(OverlayFont.NahjaDisplay, settings.NameSize))
        {
            var pos = min + new Vector2(settings.NameOffsetX, settings.NameOffsetY) * scale;
            pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
            OutlinedText.Draw(drawList, pos, text, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));
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

    // The cast frame: the spell icon is INSIDE it at the left (a square as tall as the frame), then the fill, the spell name
    // and the time. Amber glow = you can interrupt it. Steel gray frame with a lock = you cannot.
    private void DrawCast(ImDrawListPtr drawList, float scale, NamePlateSettings settings, CastSnapshot cast, float left, float top, float width)
    {
        var height = MathF.Round(settings.CastHeightPx * scale);
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), height / 2f);
        var min = new Vector2(left, top);
        var max = min + new Vector2(width, height);
        var interruptible = cast.Interruptible;
        var color = interruptible ? CastAmber : CastSteel;

        if (interruptible)
            drawList.AddRect(min - new Vector2(1f), max + new Vector2(1f), ImGui.GetColorU32(CastAmberGlow), rounding + 1f, ImDrawFlags.None, 1f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(BackgroundColor), rounding);

        // Everything inside is cut by the rounded shape of the frame, so nothing sticks out of the corners.
        var iconRight = min.X + height;
        var fraction = Math.Clamp(cast.CurrentTime / cast.TotalTime, 0f, 1f);
        var fillRight = MathF.Round(iconRight + (max.X - iconRight) * fraction);

        if (fillRight > iconRight)
        {
            drawList.PushClipRect(new Vector2(iconRight, min.Y), new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(color), rounding);
            drawList.PopClipRect();
        }

        var iconId = GameNames.CastIconId(cast.ActionType, cast.ActionId);
        if (iconId != 0)
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            var (uv0, uv1) = AuraEngine.IconUv(new Vector2(height, height), 1f, 0.08f);
            drawList.AddImageRounded(texture.Handle, min, new Vector2(iconRight, max.Y), uv0, uv1, 0xFFFFFFFFu, rounding, ImDrawFlags.RoundCornersLeft);
        }

        var border = interruptible ? BorderColor : CastSteelBorder;
        drawList.AddRect(min, max, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, 1f);
        drawList.AddLine(new Vector2(iconRight, min.Y), new Vector2(iconRight, max.Y), ImGui.GetColorU32(BorderColor), 1f);

        var padding = 6f * scale;
        using (plugin.Fonts.PushClosest(OverlayFont.NahjaDisplay, settings.CastTextSize))
        {
            var y = MathF.Round((min.Y + max.Y - ImGui.GetTextLineHeight()) / 2f);
            var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

            // The time is at the right; the lock goes before it when the cast cannot be interrupted.
            var timeText = MathF.Max(0f, cast.TotalTime - cast.CurrentTime).ToString("0.0");
            var timeWidth = ImGui.CalcTextSize(timeText).X;
            var textRight = max.X - padding;
            OutlinedText.Draw(drawList, new Vector2(MathF.Round(textRight - timeWidth), y), timeText, white);
            textRight -= timeWidth + padding;

            if (!interruptible)
            {
                var lockHeight = MathF.Round(height * 0.6f);
                var lockWidth = MathF.Round(lockHeight * 0.8f);
                DrawLock(drawList, new Vector2(MathF.Round(textRight - lockWidth), MathF.Round((min.Y + max.Y - lockHeight) / 2f)), lockWidth, lockHeight, scale);
                textRight -= lockWidth + padding;
            }

            // The spell name is shortened with "..." if it does not fit.
            var name = Fit(GameNames.CastName(cast.ActionType, cast.ActionId), textRight - (iconRight + padding));
            OutlinedText.Draw(drawList, new Vector2(MathF.Round(iconRight + padding), y), name, white);
        }
    }

    // A small padlock: a body and a shackle on top.
    private static void DrawLock(ImDrawListPtr drawList, Vector2 pos, float width, float height, float scale)
    {
        var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        var black = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f));
        var bodyTop = pos.Y + height * 0.42f;
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var inset = width * 0.22f;

        drawList.AddRect(new Vector2(pos.X + inset, pos.Y), new Vector2(pos.X + width - inset, bodyTop + thickness), black, width * 0.3f, ImDrawFlags.RoundCornersTop, thickness * 3f);
        drawList.AddRect(new Vector2(pos.X + inset, pos.Y), new Vector2(pos.X + width - inset, bodyTop + thickness), white, width * 0.3f, ImDrawFlags.RoundCornersTop, thickness);
        drawList.AddRectFilled(new Vector2(pos.X - 1f, bodyTop) , new Vector2(pos.X + width + 1f, pos.Y + height), black, 1f);
        drawList.AddRectFilled(new Vector2(pos.X, bodyTop + 1f), new Vector2(pos.X + width, pos.Y + height - 1f), white, 1f);
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
