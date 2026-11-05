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
using NahjaUI.Modules.Trackers;

namespace NahjaUI.Modules.ProgressBars;

/// <summary>
/// Draws your progress bar and your target's progress bar: a plain rectangle that fills from left to right,
/// with the action icon inside it, the action name and the time. It also hides the game's own progress bars.
/// It only reads the game snapshot.
/// </summary>
internal sealed class ProgressBarModule : IDisposable
{
    // The game's own progress bars: yours, and the one of the target (the separate part of "Target Info").
    private const string NativePlayerBar = "_CastBar";
    private const string NativeTargetBar = "_TargetInfoCastBar";

    private static readonly string[] NativeBars = { NativePlayerBar, NativeTargetBar };

    private readonly Plugin plugin;
    private readonly NativeWindowHider hider = new();

    public ProgressBarModule(Plugin plugin)
    {
        this.plugin = plugin;

        PlayerElement = plugin.Layout.Register("progressbar.player", "Player progress bar", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 280,
            Height = 26
        });

        TargetElement = plugin.Layout.Register("progressbar.target", "Target progress bar", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = -230,
            Width = 280,
            Height = 22
        });

        // The global cooldown bar sits on top of the player's progress bar (1 px apart, like the other stacked bars).
        GcdElement = plugin.Layout.Register("progressbar.gcd", "Global cooldown bar", new ElementLayout
        {
            AttachTo = "progressbar.player",
            ScreenAnchor = AnchorPoint.Top,
            SelfAnchor = AnchorPoint.Bottom,
            OffsetX = 0,
            OffsetY = -1,
            Width = 240,
            Height = 6
        });

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeBars, OnNativePreDraw);
    }

    public LayoutElement PlayerElement { get; }

    public LayoutElement GcdElement { get; }

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

    // Runs right before the game draws one of its own progress bars: marking it invisible makes the game skip it.
    private unsafe void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        var settings = args.AddonName == NativePlayerBar
            ? plugin.Configuration.ProgressBars.Player
            : plugin.Configuration.ProgressBars.Target;

        if (!settings.Enabled)
            return;

        // Skipping the game's own drawing hides the bar completely. Only clearing the "visible" flag was not enough:
        // at the end of a cast the game plays a closing animation (a fade) and kept drawing the bar during it.
        // The hider also marks it as hidden, so the game's own logic agrees with what is on screen.
        hider.Hide(args);
    }

    // ---------------------------------------------------------------- drawing

    // What a bar shows for one frame.
    private readonly record struct ProgressBarView(string Name, uint IconId, float Current, float Total, bool Interruptible);

    // Does this module want the game's window hidden right now?
    private bool HidesNative(string windowName)
    {
        var settings = windowName == NativePlayerBar ? plugin.Configuration.ProgressBars.Player : plugin.Configuration.ProgressBars.Target;
        return settings.Enabled;
    }

    public void Draw()
    {
        hider.ShowAgain(HidesNative);
        var snapshot = plugin.GameData.Current;
        var preview = PreviewRequested || plugin.Layout.UnlockMode;
        PreviewRequested = false;

        var settings = plugin.Configuration.ProgressBars;

        var (playerPos, playerSize) = LayoutManager.GetRect(PlayerElement.Layout);
        plugin.GameCover.Draw(playerPos, playerSize, 80f * ImGuiHelpers.GlobalScale, () => DrawBar(settings.Player, PlayerElement, snapshot.Player?.Cast, preview, false, true));

        DrawGcd(settings.Gcd, snapshot.Gcd, preview);

        // The icon of an enemy action means nothing (the game's own bar has none): the target bar shows it only for allies.
        var (targetPos, targetSize) = LayoutManager.GetRect(TargetElement.Layout);
        plugin.GameCover.Draw(targetPos, targetSize, 80f * ImGuiHelpers.GlobalScale, () => DrawBar(settings.Target, TargetElement, snapshot.Target?.Cast, preview, true, false));
    }

    // The global cooldown: a thin bar that fills while it runs, with the queue window (last 0.5 s) and the weave limit marked.
    private void DrawGcd(GcdBarSettings settings, GcdSnapshot? gcd, bool preview)
    {
        if (!settings.Enabled)
            return;

        preview |= plugin.Layout.IsPreviewing(GcdElement.Id);

        float elapsed, total;
        if (gcd is { Active: true })
        {
            elapsed = gcd.Elapsed;
            total = gcd.Total;
        }
        else if (preview)
        {
            total = 2.5f;
            elapsed = 1.5f;
        }
        else if (settings.AlwaysVisible)
        {
            total = 2.5f;
            elapsed = 0f;
        }
        else
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(GcdElement.Layout);
        var max = pos + size;
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), size.Y / 2f);
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var drawList = ImGui.GetBackgroundDrawList();

        var fraction = Math.Clamp(elapsed / total, 0f, 1f);

        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.BackgroundColor), rounding, ImDrawFlags.RoundCornersAll);

        var fillRight = MathF.Round(pos.X + size.X * fraction);
        if (fillRight > pos.X)
            FillShape(drawList, pos, max, pos.X, fillRight, ImGui.GetColorU32(settings.BarColor), rounding, ImDrawFlags.RoundCornersAll);

        // The queue window: from 0.5 s before the end to the end. An action pressed here is kept and used right when the cooldown ends.
        var queue = settings.QueueMs / 1000f;
        if (settings.ShowQueueWindow && queue > 0f && total > queue)
        {
            var x = MathF.Round(pos.X + size.X * ((total - queue) / total));
            FillShape(drawList, pos, max, x, max.X, ImGui.GetColorU32(Theme.WithAlpha(settings.QueueColor, 0.45f)), rounding, ImDrawFlags.RoundCornersAll);
            var lineX = x + thickness / 2f;
            drawList.AddLine(new Vector2(lineX, pos.Y), new Vector2(lineX, max.Y), ImGui.GetColorU32(settings.QueueColor), thickness);
        }

        // The weave limit: an ability used after this line delays the next global action.
        var weave = settings.WeaveMs / 1000f;
        if (settings.ShowWeaveWindow && weave > 0f && total > weave)
        {
            var x = MathF.Round(pos.X + size.X * ((total - weave) / total));
            var lineX = x + thickness / 2f;
            drawList.AddLine(new Vector2(lineX, pos.Y), new Vector2(lineX, max.Y), ImGui.GetColorU32(settings.WeaveColor), thickness);
        }

        drawList.AddRect(pos, max, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.RoundCornersAll, thickness);
    }

    private void DrawBar(ProgressBarSettings settings, LayoutElement element, CastSnapshot? cast, bool preview, bool isTarget, bool allowIcon)
    {
        if (!settings.Enabled)
            return;

        settings.EnsureTexts();

        // The bar selected in the Layout page shows a sample, so it can be placed even when nothing is being cast.
        preview |= plugin.Layout.IsPreviewing(element.Id);

        ProgressBarView view;

        if (cast is not null && cast.TotalTime > 0f)
        {
            view = new ProgressBarView(
                GameNames.CastName(cast.ActionType, cast.ActionId),
                GameNames.CastIconId(cast.ActionType, cast.ActionId),
                cast.CurrentTime,
                cast.TotalTime,
                cast.Interruptible);
        }
        else if (preview)
        {
            // A sample cast, 60 % done.
            view = new ProgressBarView("Action name", GameNames.ActionIconId(3), 1.2f, 2.0f, true);
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

        // The corners are rounded like the rest of the interface (never more than half the height).
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), size.Y / 2f);

        // Background
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.BackgroundColor), rounding, ImDrawFlags.RoundCornersAll);

        // The icon sits inside the bar, at its left, as a square as tall as the bar.
        var barLeft = pos.X;
        var hasIcon = !isTarget && settings.ShowIcon && view.IconId != 0;

        if (hasIcon)
        {
            var iconMax = new Vector2(pos.X + size.Y, max.Y);
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(view.IconId)).GetWrapOrEmpty();
            var (uv0, uv1) = TrackerEngine.IconUv(new Vector2(size.Y, size.Y), 1f, 0.08f);

            drawList.AddImageRounded(texture.Handle, pos, iconMax, uv0, uv1, 0xFFFFFFFFu, rounding, ImDrawFlags.RoundCornersLeft);
            barLeft = iconMax.X;
        }

        // The fill grows from left to right over what is left of the bar.
        var fillRight = MathF.Round(barLeft + (max.X - barLeft) * fraction);

        // The shape of the bar part: all corners rounded, or only the right ones when the icon takes the left side.
        var shapeFlags = hasIcon ? ImDrawFlags.RoundCornersRight : ImDrawFlags.RoundCornersAll;
        var shapeMin = new Vector2(barLeft, pos.Y);

        if (fillRight > barLeft)
            FillShape(drawList, shapeMin, max, barLeft, fillRight, ImGui.GetColorU32(barColor), rounding, shapeFlags);

        var thickness = MathF.Max(1f, MathF.Round(scale));

        // Slidecast: the last part of the cast is tinted and a line shows where it starts.
        // From that point on you can move and the cast still goes through.
        if (!isTarget && settings.ShowSlideCast)
            DrawSlideCast(drawList, settings, view, barLeft, pos, max, thickness, rounding, shapeFlags);

        // Border, and a line between the icon and the bar.
        var border = ImGui.GetColorU32(settings.BorderColor);

        drawList.AddRect(pos, max, border, rounding, ImDrawFlags.RoundCornersAll, thickness);

        if (hasIcon)
            drawList.AddLine(new Vector2(barLeft, pos.Y), new Vector2(barLeft, max.Y), border, thickness);

        DrawTexts(drawList, settings, view, barLeft, pos, max);
    }

    // A fill that follows the rounded shape of the bar: the full shape cut by a clip rectangle, so it never sticks out of a corner.
    private static void FillShape(ImDrawListPtr drawList, Vector2 shapeMin, Vector2 max, float clipLeft, float clipRight, uint color, float rounding, ImDrawFlags flags)
    {
        drawList.PushClipRect(new Vector2(clipLeft, shapeMin.Y), new Vector2(clipRight, max.Y), true);
        drawList.AddRectFilled(shapeMin, max, color, rounding, flags);
        drawList.PopClipRect();
    }

    private static void DrawSlideCast(ImDrawListPtr drawList, ProgressBarSettings settings, ProgressBarView view, float barLeft, Vector2 pos, Vector2 max, float thickness, float rounding, ImDrawFlags shapeFlags)
    {
        var window = settings.SlideCastMs / 1000f;

        // A cast shorter than the window has nothing to mark.
        if (window <= 0f || view.Total <= window)
            return;

        var startFraction = (view.Total - window) / view.Total;
        var x = MathF.Round(barLeft + (max.X - barLeft) * startFraction);

        // The tinted zone, from the start of the window to the end of the bar.
        FillShape(drawList, new Vector2(barLeft, pos.Y), max, x, max.X,
            ImGui.GetColorU32(Theme.WithAlpha(settings.SlideCastColor, 0.35f)), rounding, shapeFlags);

        // The line at the start of the window (moved half a pixel so it is sharp).
        var lineX = x + thickness / 2f;
        drawList.AddLine(
            new Vector2(lineX, pos.Y),
            new Vector2(lineX, max.Y),
            ImGui.GetColorU32(settings.SlideCastColor),
            thickness);
    }

    // The action name and the time left: two texts, each placed like every other text of the interface (point of the bar, offsets, size, font, color).
    // The name's box starts after the icon; the name is shortened with "..." so it never runs into the time.
    private void DrawTexts(ImDrawListPtr drawList, ProgressBarSettings settings, ProgressBarView view, float barLeft, Vector2 pos, Vector2 max)
    {
        var scale = ImGuiHelpers.GlobalScale;

        var remaining = Math.Max(0f, view.Total - view.Current);
        var timeText = settings.TimeMode == ProgressBarTimeMode.Remaining
            ? remaining.ToString("0.0")
            : $"{view.Current:0.0} / {view.Total:0.0}";

        var timeRoom = 0f;
        if (settings.Time.Enabled && settings.Time.Anchor.Fraction().X >= 0.5f)
        {
            using (var face = plugin.Fonts.PushCap(settings.Time.Font, settings.Time.TextSize))
                timeRoom = face.Width(timeText) + MathF.Round(12f * scale);
        }

        var boxPos = new Vector2(barLeft, pos.Y);
        var boxSize = new Vector2(max.X - barLeft, max.Y - pos.Y);

        if (settings.Name.Enabled)
        {
            string name;
            using (var face = plugin.Fonts.PushCap(settings.Name.Font, settings.Name.TextSize))
                name = Fit(face, view.Name, boxSize.X - timeRoom - MathF.Round(12f * scale));

            plugin.UnitFrameSystem.DrawText(drawList, name, settings.Name, boxPos, boxSize);
        }

        if (settings.Time.Enabled)
            plugin.UnitFrameSystem.DrawText(drawList, timeText, settings.Time, boxPos, boxSize);
    }

    // Shortens a text with "..." until it fits the width. The font must already be the one used to draw it.
    private static string Fit(TextFace face, string text, float available)
    {
        if (available <= 0f)
            return string.Empty;

        if (face.Width(text) <= available)
            return text;

        while (text.Length > 1)
        {
            text = text[..^1];

            var candidate = text + "...";
            if (face.Width(candidate) <= available)
                return candidate;
        }

        return string.Empty;
    }
}
