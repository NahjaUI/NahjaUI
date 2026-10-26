using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.Auras;

namespace NahjaUI.Modules.CastBars;

/// <summary>
/// Draws your cast bar and your target's cast bar: a plain rectangle that fills from left to right,
/// with the spell icon inside it, the spell name and the time. It also hides the game's own cast bars.
/// It only reads the game snapshot.
/// </summary>
internal sealed class CastBarModule : IDisposable
{
    // The game's own cast bars: yours, and the one of the target (the separate part of "Target Info").
    private const string NativePlayerBar = "_CastBar";
    private const string NativeTargetBar = "_TargetInfoCastBar";

    private static readonly string[] NativeBars = { NativePlayerBar, NativeTargetBar };

    private readonly Plugin plugin;

    public CastBarModule(Plugin plugin)
    {
        this.plugin = plugin;

        PlayerElement = plugin.Layout.Register("castbar.player", "Player cast bar", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 280,
            Height = 26
        });

        TargetElement = plugin.Layout.Register("castbar.target", "Target cast bar", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = -230,
            Width = 280,
            Height = 22
        });

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeBars, OnNativePreDraw);
    }

    public LayoutElement PlayerElement { get; }

    public LayoutElement TargetElement { get; }

    /// <summary>
    /// Set by the settings page every frame it is open: the bars then show a sample cast so their look can be adjusted
    /// without having to cast anything. (Unlock mode shows the sample too.)
    /// </summary>
    public bool PreviewRequested { get; set; }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeBars, OnNativePreDraw);
    }

    // ---------------------------------------------------------------- hiding the game's bars

    // Runs right before the game draws one of its own cast bars: marking it invisible makes the game skip it.
    private unsafe void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        var settings = args.AddonName == NativePlayerBar
            ? plugin.Configuration.CastBars.Player
            : plugin.Configuration.CastBars.Target;

        if (!settings.Enabled || !settings.HideNative)
            return;

        // Skipping the game's own drawing hides the bar completely. Only clearing the "visible" flag was not enough:
        // at the end of a cast the game plays a closing animation (a fade) and kept drawing the bar during it.
        args.PreventOriginal();

        // Also mark it as hidden, so the game's own logic agrees with what is on screen.
        var addon = args.GetAddonPointer<AtkUnitBase>();
        if (addon != null)
            addon->IsVisible = false;
    }

    // ---------------------------------------------------------------- drawing

    // What a bar shows for one frame.
    private readonly record struct CastView(string Name, uint IconId, float Current, float Total, bool Interruptible);

    public void Draw()
    {
        var snapshot = plugin.GameData.Current;
        var preview = PreviewRequested || plugin.Layout.UnlockMode;
        PreviewRequested = false;

        var settings = plugin.Configuration.CastBars;

        DrawBar(settings.Player, PlayerElement, snapshot.Player?.Cast, preview, false);
        DrawBar(settings.Target, TargetElement, snapshot.Target?.Cast, preview, true);
    }

    private void DrawBar(CastBarSettings settings, LayoutElement element, CastSnapshot? cast, bool preview, bool isTarget)
    {
        if (!settings.Enabled)
            return;

        CastView view;

        if (cast is not null && cast.TotalTime > 0f)
        {
            view = new CastView(
                GameNames.CastName(cast.ActionType, cast.ActionId),
                GameNames.CastIconId(cast.ActionType, cast.ActionId),
                cast.CurrentTime,
                cast.TotalTime,
                cast.Interruptible);
        }
        else if (preview)
        {
            // A sample cast, 60 % done.
            view = new CastView("Spell name", GameNames.ActionIconId(3), 1.2f, 2.0f, true);
        }
        else
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        var max = pos + size;
        var drawList = ImGui.GetBackgroundDrawList();

        var fraction = Math.Clamp(view.Current / view.Total, 0f, 1f);
        var barColor = isTarget && !view.Interruptible ? settings.UninterruptibleColor : settings.BarColor;

        // Background
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.BackgroundColor), 0f);

        // The icon sits inside the bar, at its left, as a square as tall as the bar.
        var barLeft = pos.X;
        var hasIcon = settings.ShowIcon && view.IconId != 0;

        if (hasIcon)
        {
            var iconMax = new Vector2(pos.X + size.Y, max.Y);
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(view.IconId)).GetWrapOrEmpty();
            var (uv0, uv1) = AuraEngine.IconUv(new Vector2(size.Y, size.Y), 1f, 0.08f);

            drawList.AddImage(texture.Handle, pos, iconMax, uv0, uv1, 0xFFFFFFFFu);
            barLeft = iconMax.X;
        }

        // The fill grows from left to right over what is left of the bar.
        var fillRight = MathF.Round(barLeft + (max.X - barLeft) * fraction);
        if (fillRight > barLeft)
            drawList.AddRectFilled(new Vector2(barLeft, pos.Y), new Vector2(fillRight, max.Y), ImGui.GetColorU32(barColor), 0f);

        var thickness = MathF.Max(1f, MathF.Round(scale));

        // Slidecast: the last part of the cast is tinted and a line shows where it starts.
        // From that point on you can move and the cast still goes through.
        if (!isTarget && settings.ShowSlideCast)
            DrawSlideCast(drawList, settings, view, barLeft, pos, max, thickness);

        // Border, and a line between the icon and the bar.
        var border = ImGui.GetColorU32(settings.BorderColor);

        drawList.AddRect(pos, max, border, 0f, ImDrawFlags.None, thickness);

        if (hasIcon)
            drawList.AddLine(new Vector2(barLeft, pos.Y), new Vector2(barLeft, max.Y), border, thickness);

        DrawTexts(drawList, settings, view, barLeft, pos, max);
    }

    private static void DrawSlideCast(ImDrawListPtr drawList, CastBarSettings settings, CastView view, float barLeft, Vector2 pos, Vector2 max, float thickness)
    {
        var window = settings.SlideCastMs / 1000f;

        // A cast shorter than the window has nothing to mark.
        if (window <= 0f || view.Total <= window)
            return;

        var startFraction = (view.Total - window) / view.Total;
        var x = MathF.Round(barLeft + (max.X - barLeft) * startFraction);

        // The tinted zone, from the start of the window to the end of the bar.
        drawList.AddRectFilled(
            new Vector2(x, pos.Y),
            new Vector2(max.X, max.Y),
            ImGui.GetColorU32(Theme.WithAlpha(settings.SlideCastColor, 0.35f)),
            0f);

        // The line at the start of the window (moved half a pixel so it is sharp).
        var lineX = x + thickness / 2f;
        drawList.AddLine(
            new Vector2(lineX, pos.Y),
            new Vector2(lineX, max.Y),
            ImGui.GetColorU32(settings.SlideCastColor),
            thickness);
    }

    // The spell name at the left and the time at the right, in the interface font with an outline.
    private void DrawTexts(ImDrawListPtr drawList, CastBarSettings settings, CastView view, float barLeft, Vector2 pos, Vector2 max)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padding = 6f * scale;

        // The size is in logical pixels: the fonts are already built scaled by the UI scale.
        using (plugin.Fonts.PushClosest(settings.Font, settings.TextSize))
        {
            var y = MathF.Round((pos.Y + max.Y - ImGui.GetTextLineHeight()) / 2f);

            var timeWidth = 0f;
            if (settings.ShowTime)
            {
                var remaining = Math.Max(0f, view.Total - view.Current);
                var timeText = settings.TimeMode == CastTimeMode.Remaining
                    ? remaining.ToString("0.0")
                    : $"{view.Current:0.0} / {view.Total:0.0}";

                timeWidth = ImGui.CalcTextSize(timeText).X;
                DrawOutlined(drawList, new Vector2(MathF.Round(max.X - padding - timeWidth), y), timeText);
            }

            if (settings.ShowName)
            {
                // The name never runs into the time: it is shortened if it does not fit.
                var available = max.X - padding - (settings.ShowTime ? timeWidth + padding : 0f) - (barLeft + padding);
                var name = Fit(view.Name, available);

                DrawOutlined(drawList, new Vector2(MathF.Round(barLeft + padding), y), name);
            }
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

    private static void DrawOutlined(ImDrawListPtr drawList, Vector2 pos, string text)
    {
        OutlinedText.Draw(drawList, pos, text, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));
    }
}
