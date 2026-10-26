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

namespace NahjaUI.Modules.UnitFrames;

/// <summary>
/// Draws the unit frames: yours, your target's, the target of your target and your focus. Each is a life bar with the
/// name and the life text drawn over it where the user wants them and a shield on top of the life. Yours can also have a
/// mana bar. It also hides the game's own player bar. It only reads the game snapshot.
/// </summary>
internal sealed class UnitFrameModule : IDisposable
{
    // The game's own bars, found in game with the Debug page: yours (life and mana), the main part of the target info
    // (it includes the target of target) and the focus. The target's statuses (_TargetInfoBuffDebuff) and the target
    // cursor (_TargetCursor) are left alone. This needs the target info set to separate parts in the game's HUD layout.
    private const string NativePlayerBar = "_ParameterWidget";
    private const string NativeTargetBar = "_TargetInfoMainTarget";
    private const string NativeFocusBar = "_FocusTargetInfo";

    private static readonly string[] NativeBars = { NativePlayerBar, NativeTargetBar, NativeFocusBar };

    // What is shown in the preview when the real unit is not there.
    private static readonly UnitSnapshot SamplePlayer = new(
        0, "Nahja Stark", 24, 100, 104071, 140000, 7200, 10000, false, null, Array.Empty<StatusSnapshot>(), true, 14);

    private static readonly UnitSnapshot SampleTarget = new(
        0, "Occult Knight", 0, 99, 412300, 650000, 0, 0, false, null, Array.Empty<StatusSnapshot>(), false, 0, true);

    private static readonly UnitSnapshot SampleTargetOfTarget = new(
        0, "Rowan Vale", 19, 100, 133420, 145000, 0, 0, false, null, Array.Empty<StatusSnapshot>(), true, 0, false);

    private static readonly UnitSnapshot SampleFocus = new(
        0, "Sensual Sandy", 0, 99, 250000, 650000, 0, 0, false, null, Array.Empty<StatusSnapshot>(), false, 0, true);

    private readonly Plugin plugin;

    public UnitFrameModule(Plugin plugin)
    {
        this.plugin = plugin;

        PlayerElement = plugin.Layout.Register("unitframe.player", "Player frame", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = -330,
            OffsetY = 190,
            Width = 280,
            Height = 26
        });

        // The mana bar can live in its own box, for example next to the job gauge.
        ManaElement = plugin.Layout.Register("unitframe.player.mana", "Player mana bar", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 224,
            Width = 156,
            Height = 8
        });

        // The target takes the mirrored place of the player's frame. The small ones sit under and over it, at its right.
        TargetElement = plugin.Layout.Register("unitframe.target", "Target frame", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 330,
            OffsetY = 190,
            Width = 280,
            Height = 26
        });

        TargetOfTargetElement = plugin.Layout.Register("unitframe.targetoftarget", "Target of target frame", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 395,
            OffsetY = 218,
            Width = 150,
            Height = 18
        });

        FocusElement = plugin.Layout.Register("unitframe.focus", "Focus frame", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 395,
            OffsetY = 162,
            Width = 150,
            Height = 18
        });

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeBars, OnNativePreDraw);
    }

    public LayoutElement PlayerElement { get; }

    public LayoutElement TargetElement { get; }

    public LayoutElement TargetOfTargetElement { get; }

    public LayoutElement FocusElement { get; }

    public LayoutElement ManaElement { get; }

    /// <summary>
    /// Set by the settings page every frame it is open: the frame then shows sample values when the real unit is not
    /// there, so its look can be adjusted. (Unlock mode shows the sample too.)
    /// </summary>
    public bool PreviewRequested { get; set; }

    // True during a frame in which the sample values are allowed (settings page open or unlock mode).
    private bool PreviewActive { get; set; }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeBars, OnNativePreDraw);
    }

    // ---------------------------------------------------------------- hiding the game's bar

    // Runs right before the game draws its own bar. Skipping the drawing hides it completely, even during its animations.
    private unsafe void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        var frames = plugin.Configuration.UnitFrames;

        var settings = args.AddonName switch
        {
            NativePlayerBar => frames.Player,
            NativeTargetBar => frames.Target,
            NativeFocusBar => frames.Focus,
            _ => null
        };

        if (settings is null || !settings.Enabled || !settings.HideNative)
            return;

        args.PreventOriginal();

        var addon = args.GetAddonPointer<AtkUnitBase>();
        if (addon != null)
            addon->IsVisible = false;
    }

    // ---------------------------------------------------------------- drawing

    public void Draw()
    {
        var snapshot = plugin.GameData.Current;
        var preview = PreviewRequested || plugin.Layout.UnlockMode;
        PreviewRequested = false;
        PreviewActive = preview;

        var frames = plugin.Configuration.UnitFrames;

        DrawUnit(frames.Player, PlayerElement, snapshot.Player, SamplePlayer, true, preview);
        DrawUnit(frames.Target, TargetElement, snapshot.Target, SampleTarget, false, preview);
        DrawUnit(frames.TargetOfTarget, TargetOfTargetElement, snapshot.TargetOfTarget, SampleTargetOfTarget, false, preview);
        DrawUnit(frames.Focus, FocusElement, snapshot.Focus, SampleFocus, false, preview);
    }

    // One frame: the real unit when there is one, a sample while the settings are open or in unlock mode, else nothing.
    private void DrawUnit(UnitFrameSettings settings, LayoutElement element, UnitSnapshot? unit, UnitSnapshot sample, bool isPlayerFrame, bool preview)
    {
        if (!settings.Enabled)
            return;

        var isReal = unit is not null;

        if (unit is null)
        {
            if (!preview)
                return;

            unit = sample;
        }

        DrawFrame(settings, element, unit, isPlayerFrame);

        // Clicks only make sense on a real unit, and not while the boxes are being moved (they use the same place).
        if (isReal && settings.Clickable && !plugin.Layout.UnlockMode)
            HandleClicks(element, unit);
    }

    // ---------------------------------------------------------------- clicks

    // A see-through window exactly over the frame takes the mouse clicks, so the game does not get them.
    // Left click targets the unit. Right click opens a small NahjaUI menu (the game's own menu cannot be opened from here).
    private void HandleClicks(LayoutElement element, UnitSnapshot unit)
    {
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        HandleClicks(element.Id, pos, size, unit);
    }

    // The same, for a box that is not a layout element of its own (the bars of the party frames).
    internal void HandleClicks(string id, Vector2 pos, Vector2 size, UnitSnapshot unit)
    {
        if (size.X < 1f || size.Y < 1f)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1f, 1f));

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus |
                    ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        var menuId = $"##nui_unit_menu_{id}";

        if (ImGui.Begin($"##nui_unit_click_{id}", flags))
        {
            ImGui.SetCursorPos(Vector2.Zero);
            ImGui.InvisibleButton("##click", size);

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var clicked = Plugin.ObjectTable.SearchByEntityId(unit.EntityId);
                if (clicked is not null)
                    Plugin.TargetManager.Target = clicked;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                ImGui.OpenPopup(menuId);

            DrawUnitMenu(menuId, unit);
        }

        ImGui.End();
        ImGui.PopStyleVar(4);
    }

    // The small menu of right click: what Dalamud lets a plugin do with a unit.
    private void DrawUnitMenu(string menuId, UnitSnapshot unit)
    {
        if (!ImGui.IsPopupOpen(menuId))
            return;

        using var theme = new ThemeScope(plugin.Configuration);

        if (!ImGui.BeginPopup(menuId))
            return;

        ImGui.TextColored(Theme.Accent, unit.Name);
        ImGui.Separator();

        var target = Plugin.ObjectTable.SearchByEntityId(unit.EntityId);

        if (ImGui.Selectable("Target") && target is not null)
            Plugin.TargetManager.Target = target;

        if (ImGui.Selectable("Set as focus target") && target is not null)
            Plugin.TargetManager.FocusTarget = target;

        if (Plugin.TargetManager.FocusTarget is not null && ImGui.Selectable("Clear focus target"))
            Plugin.TargetManager.FocusTarget = null;

        if (ImGui.Selectable("Copy name"))
            ImGui.SetClipboardText(unit.Name);

        ImGui.EndPopup();
    }

    private void DrawFrame(UnitFrameSettings settings, LayoutElement element, UnitSnapshot unit, bool isPlayerFrame)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        var max = pos + size;
        var drawList = ImGui.GetBackgroundDrawList();

        var rounding = MathF.Round(settings.RoundingPx * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        var fraction = unit.MaxHp == 0 ? 0f : Math.Clamp(unit.Hp / (float)unit.MaxHp, 0f, 1f);
        var fillColor = LifeColor(settings, unit, isPlayerFrame);

        // Background, then the life. The right end of the fill is straight, except when the life is full.
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        var fillRight = MathF.Round(pos.X + size.X * fraction);
        if (fillRight > pos.X)
        {
            var flags = fraction >= 0.999f ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft;
            drawList.AddRectFilled(pos, new Vector2(fillRight, max.Y), ImGui.GetColorU32(fillColor), rounding, flags);
        }

        if (settings.ShowShield && unit.ShieldPercent > 0)
            DrawShield(drawList, settings, pos, size, fraction, rounding, unit.ShieldPercent);

        drawList.AddRect(pos, max, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        // The mana bar: under the life bar, in its own box, or not at all, depending on the job.
        var manaDrawn = false;
        if (isPlayerFrame && settings.ShowMana && unit.MaxMp > 0)
        {
            var placement = settings.GetManaPlacement(unit.JobId);
            var fractionMp = Math.Clamp(unit.Mp / (float)unit.MaxMp, 0f, 1f);

            if (placement == ManaPlacement.UnderLifeBar)
            {
                var gap = MathF.Round(settings.ManaGapPx * scale);
                var height = MathF.Max(2f, MathF.Round(settings.ManaHeightPx * scale));
                var top = pos.Y + size.Y + gap;

                DrawManaBar(drawList, settings, new Vector2(pos.X, top), new Vector2(pos.X + size.X, top + height), fractionMp, scale, thickness);
                manaDrawn = true;
            }
            else if (placement == ManaPlacement.OwnBox)
            {
                var (manaPos, manaSize) = LayoutManager.GetRect(ManaElement.Layout);
                DrawManaBar(drawList, settings, manaPos, manaPos + manaSize, fractionMp, scale, thickness);
                manaDrawn = true;
            }
        }

        // While moving things around, the own box shows a sample so there is something to see and drag.
        if (isPlayerFrame && settings.ShowMana && !manaDrawn && PreviewActive)
        {
            var (manaPos, manaSize) = LayoutManager.GetRect(ManaElement.Layout);
            DrawManaBar(drawList, settings, manaPos, manaPos + manaSize, 0.7f, scale, thickness);
        }

        // The texts are drawn last, over everything, and may stick out of the bar.
        var name = UnitTextFormatter.FormatName(unit.Name, settings.NameFormat, unit.IsPlayer, settings.NameFormatOnlyPlayers);
        DrawText(drawList, name, settings.Name, pos, size);

        var health = UnitTextFormatter.FormatHealth(settings, unit.Hp, unit.MaxHp);
        DrawText(drawList, health, settings.Health, pos, size);
    }

    // The shield is as tall as the bar and follows its rounded corners.
    // If it fits, it goes right after the life. If life and shield together are more than the bar, the shield is drawn from the
    // right end of the bar over the life (the more shield, the further left it reaches), so there is always a visible block.
    // It is translucent: the life and the background show through it, so where it covers the life you still see it.
    internal static void DrawShield(ImDrawListPtr drawList, UnitFrameSettings settings, Vector2 pos, Vector2 size, float fraction, float rounding, byte shieldPercent)
    {
        var max = pos + size;
        var shieldFraction = Math.Min(shieldPercent / 100f, 1f);

        var overflows = fraction + shieldFraction > 1f;
        var startFraction = overflows ? 1f - shieldFraction : fraction;
        var endFraction = Math.Min(fraction + shieldFraction, 1f);

        var left = MathF.Round(pos.X + size.X * startFraction);
        var right = MathF.Round(pos.X + size.X * endFraction);
        if (right <= left)
            return;

        // Only the corners that touch the ends of the bar are rounded, the same as the bar.
        var touchesLeft = left <= pos.X + 0.5f;
        var touchesRight = right >= max.X - 0.5f;

        var flags = touchesLeft && touchesRight ? ImDrawFlags.RoundCornersAll
            : touchesRight ? ImDrawFlags.RoundCornersRight
            : touchesLeft ? ImDrawFlags.RoundCornersLeft
            : ImDrawFlags.RoundCornersNone;

        var color = settings.ShieldColor;
        color.W = Math.Clamp(color.W * settings.ShieldOpacity, 0f, 1f);

        drawList.AddRectFilled(new Vector2(left, pos.Y), new Vector2(right, max.Y), ImGui.GetColorU32(color), rounding, flags);
    }

    // The colour of the life: the hostile colour for enemies, and for players the job's own colour when class colours
    // are chosen, the fixed one otherwise. Anything else (friendly NPCs) uses the fixed one.
    internal Vector4 LifeColor(UnitFrameSettings settings, UnitSnapshot unit, bool isPlayerFrame)
    {
        var isPlayer = isPlayerFrame || unit.IsPlayer;

        if (!isPlayer && unit.IsHostile)
            return settings.HostileColor;

        if (isPlayer && settings.LifeColorMode == LifeColorMode.Class)
            return JobColors.Get(plugin.Configuration.UnitFrames.JobColorOverrides, unit.JobId, settings.FriendlyColor);

        return settings.FriendlyColor;
    }

    // A mana bar between two corners, filled to <fraction>.
    private static void DrawManaBar(ImDrawListPtr drawList, UnitFrameSettings settings, Vector2 min, Vector2 max, float fraction, float scale, float thickness)
    {
        var width = max.X - min.X;
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), (max.Y - min.Y) / 2f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        var fillRight = MathF.Round(min.X + width * fraction);
        if (fillRight > min.X)
        {
            var flags = fraction >= 0.999f ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft;
            drawList.AddRectFilled(min, new Vector2(fillRight, max.Y), ImGui.GetColorU32(settings.ManaColor), rounding, flags);
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.None, thickness);
    }

    // Draws a text attached to a point of the bar: the same point of the text is placed on it, then the offset moves it.
    // White (or the chosen colour) with a black outline, readable over any bar colour and over the game behind it.
    internal void DrawText(ImDrawListPtr drawList, string text, FrameTextSettings settings, Vector2 boxPos, Vector2 boxSize)
    {
        if (!settings.Enabled || string.IsNullOrEmpty(text))
            return;

        var scale = ImGuiHelpers.GlobalScale;

        // The size is in logical pixels: the fonts are already built scaled by the UI scale.
        using (plugin.Fonts.PushClosest(settings.Font, settings.TextSize))
        {
            var textSize = ImGui.CalcTextSize(text);
            var fraction = settings.Anchor.Fraction();

            var pos = boxPos + boxSize * fraction - textSize * fraction + new Vector2(settings.OffsetX, settings.OffsetY) * scale;
            pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));

            OutlinedText.Draw(drawList, pos, text, ImGui.GetColorU32(settings.Color));
        }
    }
}
