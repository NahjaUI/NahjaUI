using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
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
    // (it includes the target of target) and the focus. The target's statuses (_TargetInfoBuffDebuff) are hidden only while
    // our own buff or debuff rows of the target frame are on. The target cursor (_TargetCursor) is left alone. This needs the target info set to separate parts in the game's HUD layout.
    private const string NativePlayerBar = "_ParameterWidget";
    private const string NativeTargetBar = "_TargetInfoMainTarget";
    private const string NativeFocusBar = "_FocusTargetInfo";

    private const string NativeTargetStatuses = "_TargetInfoBuffDebuff";

    private static readonly string[] NativeBars = { NativePlayerBar, NativeTargetBar, NativeFocusBar, NativeTargetStatuses };

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
        RestoreCursor();
    }

    // The game's cursor is kept only while the mouse is over one of our frames (the setting is global in Dalamud, so it is
    // put back as soon as the mouse leaves). Called once per frame after everything has been drawn.
    private bool cursorWanted;
    private bool cursorChanged;

    internal void UpdateCursor()
    {
        if (cursorWanted)
        {
            Plugin.PluginInterface.UiBuilder.OverrideGameCursor = false;
            cursorChanged = true;
        }
        else
        {
            RestoreCursor();
        }

        cursorWanted = false;
    }

    private void RestoreCursor()
    {
        if (!cursorChanged)
            return;

        Plugin.PluginInterface.UiBuilder.OverrideGameCursor = true;
        cursorChanged = false;
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
            NativeTargetStatuses => frames.Target,
            _ => null
        };

        if (args.AddonName == NativeTargetStatuses)
        {
            var target = frames.Target;
            if (!target.Enabled || !target.HideNativeStatuses || !(target.Buffs.Enabled || target.Debuffs.Enabled))
                return;
        }
        else if (settings is null || !settings.Enabled || !settings.HideNative)
        {
            return;
        }

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

        // The mana box can only be moved when the job you play has its mana bar in its own box.
        var player = snapshot.IsLoggedIn ? snapshot.Player : null;
        ManaElement.IsHidden = player is not null
            && !(frames.Player.Enabled && frames.Player.ShowMana && frames.Player.GetManaPlacement(player.JobId) == ManaPlacement.OwnBox);

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

        // Everything of the frame is drawn around the game's own right click menu, never over it.
        var (barPos, barSize) = LayoutManager.GetRect(element.Layout);
        plugin.GameCover.Draw(barPos, barSize, 400f * ImGuiHelpers.GlobalScale, () =>
        {
            DrawFrame(settings, element, unit, isPlayerFrame);

            // Buffs and debuffs: not on the target of target, which is only a small frame.
            if (unit.Kind != UnitKind.Object && !ReferenceEquals(settings, plugin.Configuration.UnitFrames.TargetOfTarget))
            {
                var drawList = ImGui.GetBackgroundDrawList();
                var playerId = plugin.GameData.Current.Player?.EntityId ?? 0;
                var buffs = isReal ? unit.Statuses : StatusRowRenderer.PreviewBuffs;
                var debuffs = isReal ? unit.Statuses : StatusRowRenderer.PreviewDebuffs;

                var frames = plugin.Configuration.UnitFrames;
                var place = ReferenceEquals(settings, frames.Player) ? "player" : ReferenceEquals(settings, frames.Focus) ? "focus" : "target";

                plugin.StatusRows.DrawRow(drawList, settings.Buffs, true, buffs, !isReal, playerId, barPos, barSize, place);
                plugin.StatusRows.DrawRow(drawList, settings.Debuffs, false, debuffs, !isReal, playerId, barPos, barSize, place);
            }
        });

        // Clicks only make sense on a real unit, and not while the boxes are being moved (they use the same place).
        if (isReal && settings.Clickable && !plugin.Layout.UnlockMode)
            HandleClicks(element, unit);
    }

    // ---------------------------------------------------------------- clicks

    // A see-through window exactly over the frame takes the mouse clicks, so the game does not get them.
    // Left click targets the unit. Right click opens the game's own menu for the unit.
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

        // While the game's own menu is open, our click areas step aside so its entries can be clicked.
        if (plugin.GameCover.Any)
            return;

        // No window over the frame: while the mouse is over the box we tell Dalamud the mouse is ours (so the game does not
        // get the click) and read the click ourselves.
        var hovered = ImGui.IsMouseHoveringRect(pos, pos + size, false) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow);
        if (!hovered)
            return;

        ImGui.SetNextFrameWantCaptureMouse(true);

        // Dalamud swaps the game's cursor for the Windows one while it captures the mouse; this asks it to keep the game's.
        cursorWanted = true;

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var clicked = Plugin.ObjectTable.SearchByEntityId(unit.EntityId);
            if (clicked is not null)
                Plugin.TargetManager.Target = clicked;
        }

        // Right click: the game's own menu for that unit (the same one as on its own frames).
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            OpenGameMenu(unit);
    }

    // Opens the game's own right click menu for a unit, at the mouse. Returns false when it could not.
    private static unsafe bool OpenGameMenu(UnitSnapshot unit)
    {
        var gameObject = Plugin.ObjectTable.SearchByEntityId(unit.EntityId);
        if (gameObject is null)
            return false;

        var agent = AgentHUD.Instance();
        if (agent == null)
            return false;

        agent->OpenContextMenuFromTarget((GameObject*)gameObject.Address);
        return true;
    }

    private void DrawFrame(UnitFrameSettings settings, LayoutElement element, UnitSnapshot unit, bool isPlayerFrame)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        var max = pos + size;
        var drawList = ImGui.GetBackgroundDrawList();

        var rounding = MathF.Round(settings.RoundingPx * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        // Something without life (an object, a shopkeeper) is a full bar in its colour.
        var hasLife = unit.MaxHp > 0;
        var fraction = hasLife ? Math.Clamp(unit.Hp / (float)unit.MaxHp, 0f, 1f) : (unit.Kind == UnitKind.Character ? 0f : 1f);
        var fillColor = LifeColor(settings, unit, isPlayerFrame);

        // Background, then the life. The right end of the fill is straight, cut with a clip rectangle.
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        var fillRight = MathF.Round(pos.X + size.X * fraction);
        if (fillRight > pos.X)
        {
            // The whole rounded bar, cut at the end of the life: the fill never sticks out of the rounded corners
            // (with a straight end it did when the life was between about 95% and 100%).
            drawList.PushClipRect(pos, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(pos, max, ImGui.GetColorU32(fillColor), rounding);
            drawList.PopClipRect();
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
        // Only when this job really has its mana bar in its own box: with "off" there is nothing to show.
        if (isPlayerFrame && settings.ShowMana && !manaDrawn && PreviewActive
            && settings.GetManaPlacement(unit.JobId) == ManaPlacement.OwnBox)
        {
            var (manaPos, manaSize) = LayoutManager.GetRect(ManaElement.Layout);
            DrawManaBar(drawList, settings, manaPos, manaPos + manaSize, 0.7f, scale, thickness);
        }

        // The texts are drawn last, over everything, and may stick out of the bar.
        var name = UnitTextFormatter.FormatName(unit.Name, settings.NameFormat, unit.IsPlayer, settings.NameFormatOnlyPlayers);
        DrawText(drawList, name, settings.Name, pos, size);

        // No life number for things that have no life.
        if (hasLife || unit.Kind == UnitKind.Character)
        {
            var health = UnitTextFormatter.FormatHealth(settings, unit.Hp, unit.MaxHp);
            DrawText(drawList, health, settings.Health, pos, size);
        }
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

        var color = settings.ShieldColor;
        color.W = Math.Clamp(color.W * settings.ShieldOpacity, 0f, 1f);

        // The rounded bar cut to the part of the shield: it follows the corners of the bar, never sticks out of them.
        drawList.PushClipRect(new Vector2(left, pos.Y), new Vector2(right, max.Y), true);
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(color), rounding);
        drawList.PopClipRect();
    }

    // The colour of the life: the hostile colour for enemies, and for players the job's own colour when class colours
    // are chosen, the fixed one otherwise. Anything else (friendly NPCs) uses the fixed one.
    internal Vector4 LifeColor(UnitFrameSettings settings, UnitSnapshot unit, bool isPlayerFrame)
    {
        if (unit.Kind == UnitKind.Object)
            return settings.ObjectColor;

        if (unit.Kind == UnitKind.Pet)
            return settings.PetColor;

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
            drawList.PushClipRect(min, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(settings.ManaColor), rounding);
            drawList.PopClipRect();
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
