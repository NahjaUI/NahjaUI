using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// Draws the job gauges: White Mage (lilies), Warrior (Beast Gauge) and Paladin (Oath Gauge). It also hides the game's own gauge. It only reads the game snapshot.
/// </summary>
internal sealed class JobGaugeModule : IDisposable
{
    // The game's own White Mage gauge.
    private const string NativeWhiteMageGauge = "JobHudWHM0";

    // The game's own Warrior gauge (check the name in Debug > Game windows while playing a Warrior).
    private const string NativeWarriorGauge = "JobHudWAR0";

    // Job ids of the game (White Mage 24, Warrior 21).
    private const uint WhiteMageJobId = 24;
    private const uint WarriorJobId = 21;

    // The game's own Paladin gauge.
    private const string NativePaladinGauge = "JobHudPLD0";

    private const uint PaladinJobId = 19;

    private static readonly string[] NativeGauges = { NativeWhiteMageGauge, NativeWarriorGauge, NativePaladinGauge };

    // A new lily every 20 seconds.
    private const float LilyIntervalMs = 20000f;

    // What is shown in the preview: two lilies and a third one filling up, and a full set of blood lilies.
    private static readonly WhiteMageGaugeSnapshot Sample = new(2, 3, 11000f);

    // Preview of the Warrior: enough gauge for one spender.
    private static readonly WarriorGaugeSnapshot WarriorSample = new(70);

    // Preview of the Paladin: enough gauge for one spender.
    private static readonly PaladinGaugeSnapshot PaladinSample = new(70);

    private readonly Plugin plugin;
    private readonly NativeWindowHider hider = new();

    public JobGaugeModule(Plugin plugin)
    {
        this.plugin = plugin;

        LilyElement = plugin.Layout.Register("gauge.whm.lily", "White Mage lilies", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 190,
            Width = 156,
            Height = 14
        });

        BloodLilyElement = plugin.Layout.Register("gauge.whm.bloodlily", "White Mage blood lilies", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 208,
            Width = 156,
            Height = 8
        });

        WarriorElement = plugin.Layout.Register("gauge.war.beast", "Warrior Beast Gauge", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 156,
            Height = 14
        });

        PaladinElement = plugin.Layout.Register("gauge.pld.oath", "Paladin Oath Gauge", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 156,
            Height = 14
        });

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeGauges, OnNativePreDraw);
    }

    public LayoutElement LilyElement { get; }

    public LayoutElement BloodLilyElement { get; }

    public LayoutElement WarriorElement { get; }

    public LayoutElement PaladinElement { get; }

    /// <summary>
    /// Set by the settings page every frame it is open, to the job being edited: that gauge then shows sample values so its look can be adjusted
    /// without being a White Mage in combat. (Unlock mode shows the sample too.)
    /// </summary>
    public uint PreviewJobId { get; set; }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeGauges, OnNativePreDraw);
    }

    // ---------------------------------------------------------------- hiding the game's gauge

    // Runs right before the game draws its own gauge. Skipping the drawing hides it completely, even during its animations.
    private void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        if (!HidesNative(args.AddonName))
            return;

        hider.Hide(args);
    }

    // Does this module want the game's gauge window hidden right now?
    private bool HidesNative(string windowName)
    {
        var gauges = plugin.Configuration.JobGauges;
        return windowName switch
        {
            NativeWarriorGauge => gauges.Warrior is { Enabled: true, HideNative: true },
            NativePaladinGauge => gauges.Paladin is { Enabled: true, HideNative: true },
            _ => gauges.WhiteMage is { Enabled: true, HideNative: true }
        };
    }

    // ---------------------------------------------------------------- drawing

    public void Draw()
    {
        hider.ShowAgain(HidesNative);
        var snapshot = plugin.GameData.Current;
        var player = snapshot.IsLoggedIn ? snapshot.Player : null;
        var previewJob = PreviewJobId;
        PreviewJobId = 0;

        // Unlock mode only offers the gauge of the job you are playing (all of them while the job is unknown).
        var isWhiteMage = player is null || player.JobId == WhiteMageJobId;
        var isWarrior = player is null || player.JobId == WarriorJobId;
        LilyElement.IsHidden = !isWhiteMage;
        BloodLilyElement.IsHidden = !isWhiteMage;
        WarriorElement.IsHidden = !isWarrior;

        var isPaladin = player is null || player.JobId == PaladinJobId;
        PaladinElement.IsHidden = !isPaladin;

        var unlock = plugin.Layout.UnlockMode;

        var whiteMage = plugin.Configuration.JobGauges.WhiteMage;
        if (whiteMage.Enabled)
        {
            // The gauge only exists while the player is a White Mage. The settings page shows a sample of the job being edited,
            // unlock mode only for the job you are playing.
            var gauge = snapshot.WhiteMage ?? (previewJob == WhiteMageJobId || (unlock && isWhiteMage) ? Sample : null);
            if (gauge is not null)
                DrawWhiteMage(whiteMage, gauge);
        }

        var warrior = plugin.Configuration.JobGauges.Warrior;
        if (warrior.Enabled)
        {
            var gauge = snapshot.Warrior ?? (previewJob == WarriorJobId || (unlock && isWarrior) ? WarriorSample : null);
            if (gauge is not null)
                DrawBar(warrior, WarriorElement, gauge.BeastGauge);
        }

        var paladin = plugin.Configuration.JobGauges.Paladin;
        if (paladin.Enabled)
        {
            var gauge = snapshot.Paladin ?? (previewJob == PaladinJobId || (unlock && isPaladin) ? PaladinSample : null);
            if (gauge is not null)
                DrawBar(paladin, PaladinElement, gauge.OathGauge);
        }
    }

    // One rounded bar from 0 to 100, with an optional mark at 50 and the number in the middle.
    private void DrawBar(BarGaugeSettings settings, LayoutElement element, byte gaugeValue)
    {
        var drawList = ImGui.GetBackgroundDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var value = Math.Clamp((int)gaugeValue, 0, 100);
        var rounding = MathF.Round(settings.RoundingPx * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var end = pos + size;

        var ready = settings.HighlightWhenReady && value >= 50;
        var color = ready ? settings.ReadyColor : settings.BarColor;

        drawList.AddRectFilled(pos, end, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        var fillWidth = MathF.Round(size.X * value / 100f);
        if (fillWidth > 0f)
        {
            // Clipped to the bar so the fill keeps the rounded outline of the bar, not its own rounded end.
            drawList.PushClipRect(pos, new Vector2(pos.X + fillWidth, end.Y), true);
            drawList.AddRectFilled(pos, end, ImGui.GetColorU32(color), rounding);
            drawList.PopClipRect();
        }

        if (settings.ShowThreshold)
        {
            var x = pos.X + MathF.Round(size.X / 2f);
            drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, end.Y), ImGui.GetColorU32(settings.ThresholdColor), thickness);
        }

        drawList.AddRect(pos, end, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        if (settings.ShowNumber)
            DrawCenteredText(drawList, settings.Font, settings.TextSize, pos, size, value.ToString());
    }

    private void DrawCenteredText(ImDrawListPtr drawList, OverlayFont font, int fontSize, Vector2 pos, Vector2 size, string text)
    {
        var scale = ImGuiHelpers.GlobalScale;

        // The size is the height of the letters, in logical pixels.
        using (var face = plugin.Fonts.PushCap(font, fontSize))
        {
            var textSize = face.Measure(text);
            var textPos = new Vector2(MathF.Round(pos.X + (size.X - textSize.X) / 2f), MathF.Round(pos.Y + (size.Y - textSize.Y) / 2f));

            face.Draw(drawList, textPos, text, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));
        }
    }

    private void DrawWhiteMage(WhiteMageGaugeSettings settings, WhiteMageGaugeSnapshot gauge)
    {
        var drawList = ImGui.GetBackgroundDrawList();

        // How far the next lily is. The game counts this timer up from 0 to 20 seconds (check the Debug page if it looks wrong).
        var lilies = Math.Min((int)gauge.Lily, 3);
        var progress = lilies >= 3 ? 0f : Math.Clamp(gauge.LilyTimerMs / LilyIntervalMs, 0f, 1f);

        var (lilyPos, lilySize) = LayoutManager.GetRect(LilyElement.Layout);
        DrawPips(drawList, settings, lilyPos, lilySize, lilies, progress, settings.LilyColor, false);

        if (settings.ShowTimer && lilies < 3 && progress > 0f)
        {
            var secondsLeft = (int)MathF.Ceiling((1f - progress) * LilyIntervalMs / 1000f);
            DrawPipText(drawList, settings, lilyPos, lilySize, lilies, secondsLeft.ToString());
        }

        if (!settings.ShowBloodLilies)
            return;

        var blood = Math.Min((int)gauge.BloodLily, 3);
        var (bloodPos, bloodSize) = LayoutManager.GetRect(BloodLilyElement.Layout);
        DrawPips(drawList, settings, bloodPos, bloodSize, blood, 0f, settings.BloodLilyColor, settings.GlowWhenFull && blood >= 3);
    }

    // Three rounded pips side by side, inside the box of the layout element. The first <filled> are full and the next one
    // is filled by <partial> (0 to 1) from the left, like a small bar.
    private static void DrawPips(ImDrawListPtr drawList, WhiteMageGaugeSettings settings, Vector2 pos, Vector2 size, int filled, float partial, Vector4 color, bool glow)
    {
        const int count = 3;

        var scale = ImGuiHelpers.GlobalScale;
        var gap = MathF.Round(settings.GapPx * scale);
        var rounding = MathF.Round(settings.RoundingPx * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        // Whole pixels, so every pip has the same sharp edges.
        var pipWidth = MathF.Floor((size.X - gap * (count - 1)) / count);
        if (pipWidth < 1f || size.Y < 1f)
            return;

        var total = pipWidth * count + gap * (count - 1);
        var left = pos.X + MathF.Round((size.X - total) / 2f);

        var background = ImGui.GetColorU32(settings.BackgroundColor);
        var fill = ImGui.GetColorU32(color);
        var partialFill = ImGui.GetColorU32(Theme.WithAlpha(color, 0.65f));
        var border = ImGui.GetColorU32(settings.BorderColor);
        var glowColor = ImGui.GetColorU32(settings.GlowColor);

        for (var i = 0; i < count; i++)
        {
            var a = new Vector2(left + i * (pipWidth + gap), pos.Y);
            var b = new Vector2(a.X + pipWidth, pos.Y + size.Y);

            drawList.AddRectFilled(a, b, background, rounding);

            if (i < filled)
            {
                drawList.AddRectFilled(a, b, fill, rounding);
            }
            else if (i == filled && partial > 0f)
            {
                var right = a.X + MathF.Round(pipWidth * partial);
                if (right > a.X)
                    drawList.AddRectFilled(a, new Vector2(right, b.Y), partialFill, rounding);
            }

            drawList.AddRect(a, b, border, rounding, ImDrawFlags.None, thickness);

            // A frame just outside the pip when the gauge is full.
            if (glow)
            {
                var outside = new Vector2(thickness, thickness);
                drawList.AddRect(a - outside, b + outside, glowColor, rounding + thickness, ImDrawFlags.None, thickness);
            }
        }
    }

    // A short text (the seconds until the next lily) in the middle of one pip.
    private void DrawPipText(ImDrawListPtr drawList, WhiteMageGaugeSettings settings, Vector2 pos, Vector2 size, int pipIndex, string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = MathF.Round(settings.GapPx * scale);
        var pipWidth = MathF.Floor((size.X - gap * 2f) / 3f);
        if (pipWidth < 1f)
            return;

        var total = pipWidth * 3f + gap * 2f;
        var left = pos.X + MathF.Round((size.X - total) / 2f);
        var centerX = left + pipIndex * (pipWidth + gap) + pipWidth / 2f;

        // The size is the height of the letters, in logical pixels.
        using (var face = plugin.Fonts.PushCap(settings.Font, settings.TextSize))
        {
            var textSize = face.Measure(text);
            var textPos = new Vector2(MathF.Round(centerX - textSize.X / 2f), MathF.Round(pos.Y + (size.Y - textSize.Y) / 2f));

            face.Draw(drawList, textPos, text, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));
        }
    }
}
