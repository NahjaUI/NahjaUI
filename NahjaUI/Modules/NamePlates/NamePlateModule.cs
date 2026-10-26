using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.NamePlate;
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

    private readonly Plugin plugin;

    public NamePlateModule(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.NamePlateGui.OnDataUpdate += OnDataUpdate;
    }

    public void Dispose()
    {
        Plugin.NamePlateGui.OnDataUpdate -= OnDataUpdate;
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
    }
}
