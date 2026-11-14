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

namespace NahjaUI.Modules.HudBars;

/// <summary>
/// Draws the HUD bars: yours, your target's, the target of your target and your focus. Each is a HP bar with the
/// name and the HP text drawn over it where the user wants them and a shield on top of the HP. Yours can also have a
/// MP bar. It also hides the game's own player bar. It only reads the game snapshot.
/// </summary>
internal sealed class HudBarModule : IDisposable
{
    // The game's own bars: yours (HP and MP), the main part of the target info
    // (it includes the target of target) and the focus. The target's statuses (_TargetInfoBuffDebuff) are hidden only while
    // our own enhancement or enfeeblement rows of the target frame are on. The target cursor (_TargetCursor) is left alone. This needs the target info set to separate parts in the game's HUD layout.
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
    private readonly NativeWindowHider hider = new();

    public HudBarModule(Plugin plugin)
    {
        this.plugin = plugin;

        PlayerElement = plugin.Layout.Register("hudbar.player", "Player frame", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = -330,
            OffsetY = 190,
            Width = 280,
            Height = 26
        });

        // The MP bar can live in its own box, for example next to the job gauge.
        MpElement = plugin.Layout.Register("hudbar.player.mp", "Player MP bar", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 224,
            Width = 156,
            Height = 8
        });

        // The target takes the mirrored place of the player's frame. The small ones sit under and over it, at its right.
        TargetElement = plugin.Layout.Register("hudbar.target", "Target frame", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 330,
            OffsetY = 190,
            Width = 280,
            Height = 26
        });

        TargetOfTargetElement = plugin.Layout.Register("hudbar.targetoftarget", "Target of target frame", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 395,
            OffsetY = 218,
            Width = 150,
            Height = 18
        });

        FocusElement = plugin.Layout.Register("hudbar.focus", "Focus frame", new ElementLayout
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

    public LayoutElement MpElement { get; }

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
        hider.ShowAll();
        RestoreCursor();
        ClearMouseOver();
    }

    // The game's cursor is kept only while the mouse is over one of our frames (the setting is global in Dalamud, so it is
    // put back as soon as the mouse leaves). Called once per frame after everything has been drawn.
    private bool cursorWanted;
    private bool cursorChanged;

    // A group of status icons asks to keep the game's cursor while the mouse is over one of its icons.
    internal void KeepGameCursor() => cursorWanted = true;

    // The mouse may be taken from the game only for a press that started over one of our frames. A press that started elsewhere (for
    // example the right click that closes the game's menu, which also starts the camera) must reach the game with its release too,
    // or the game keeps thinking the button is held and the cursor is lost.
    private bool capturedLastFrame;
    private bool capturedThisFrame;
    private bool pressIsOurs;

    internal bool MayCapture()
    {
        if (!ImGui.IsAnyMouseDown())
        {
            pressIsOurs = false;
            capturedThisFrame = true;
            return true;
        }

        if (!pressIsOurs && capturedLastFrame
            && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseClicked(ImGuiMouseButton.Middle)))
            pressIsOurs = true;

        if (pressIsOurs)
            capturedThisFrame = true;

        return pressIsOurs;
    }

    // The unit under the mouse this frame (one of our frames), to publish as the game's mouseover target.
    private uint mouseOverEntityId;
    private bool mouseOverPublished;

    private void ClearMouseOver()
    {
        if (!mouseOverPublished)
            return;

        mouseOverPublished = false;
        Plugin.TargetManager.MouseOverTarget = null;
    }

    // Tells the game which unit is under the mouse (only while the mouse is over one of our frames), and takes it back when the mouse leaves.
    private void UpdateMouseOver()
    {
        var entityId = mouseOverEntityId;
        mouseOverEntityId = 0;

        if (entityId == 0)
        {
            ClearMouseOver();
            return;
        }

        var target = Plugin.ObjectTable.SearchByEntityId(entityId);
        if (target is null)
        {
            ClearMouseOver();
            return;
        }

        Plugin.TargetManager.MouseOverTarget = target;
        mouseOverPublished = true;
    }

    internal void UpdateCursor()
    {
        UpdateMouseOver();

        capturedLastFrame = capturedThisFrame;
        capturedThisFrame = false;

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
        if (!HidesNative(args.AddonName))
            return;

        hider.Hide(args);
    }

    // Does this module want the game's window hidden right now?
    private bool HidesNative(string windowName)
    {
        var frames = plugin.Configuration.HudBars;

        switch (windowName)
        {
            case NativeTargetStatuses:
                var target = frames.Target;
                return target.Enabled && (target.Enhancements.Enabled || target.Enfeeblements.Enabled);

            // The game's player bar (HP and MP together) is hidden whenever the Parameter bar page is Enabled.
            case NativePlayerBar:
                return frames.Player.Enabled;

            case NativeTargetBar:
                return frames.Target.Enabled;

            case NativeFocusBar:
                return frames.Focus.Enabled;

            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- drawing

    public void Draw()
    {
        hider.ShowAgain(HidesNative);
        var snapshot = plugin.GameData.Current;
        var preview = PreviewRequested || plugin.Layout.UnlockMode
            || plugin.Layout.IsPreviewing(PlayerElement.Id) || plugin.Layout.IsPreviewing(MpElement.Id) || plugin.Layout.IsPreviewing(TargetElement.Id)
            || plugin.Layout.IsPreviewing(TargetOfTargetElement.Id) || plugin.Layout.IsPreviewing(FocusElement.Id);
        PreviewRequested = false;
        PreviewActive = preview;

        var frames = plugin.Configuration.HudBars;

        // The HP bar and the MP bar are two things: each one is on or off for the job you play. A box that is off
        // cannot be moved in unlock mode.
        var player = snapshot.IsLoggedIn ? snapshot.Player : null;
        var hpOn = frames.Player.Enabled && (player is null || frames.Player.IsHpOn(player.JobId));
        var mpOn = frames.Player.Enabled && (player is null || frames.Player.IsMpOn(player.JobId));

        PlayerElement.IsHidden = player is not null && !hpOn;
        MpElement.IsHidden = player is not null && !mpOn;

        if (hpOn)
            DrawUnit(frames.Player, PlayerElement, snapshot.Player, SamplePlayer, true, preview);

        if (mpOn)
            DrawPlayerMp(frames.Player, snapshot.Player, preview);

        DrawUnit(frames.Target, TargetElement, snapshot.Target, SampleTarget, false, preview);
        DrawUnit(frames.TargetOfTarget, TargetOfTargetElement, snapshot.TargetOfTarget, SampleTargetOfTarget, false, preview);
        DrawUnit(frames.Focus, FocusElement, snapshot.Focus, SampleFocus, false, preview);
    }

    // One frame: the real unit when there is one, a sample while the settings are open or in unlock mode, else nothing.
    private void DrawUnit(HudBarSettings settings, LayoutElement element, UnitSnapshot? unit, UnitSnapshot sample, bool isPlayerFrame, bool preview)
    {
        if (!settings.Enabled || !plugin.Layout.IsShown(element))
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

            // Enhancements and enfeeblements: not on the target of target (only a small frame) nor on the player (its own page, "Status effects").
            if (!isPlayerFrame && unit.Kind != UnitKind.Object && !ReferenceEquals(settings, plugin.Configuration.HudBars.TargetOfTarget))
            {
                var drawList = ImGui.GetBackgroundDrawList();
                var playerId = plugin.GameData.Current.Player?.EntityId ?? 0;
                var enhancements = isReal ? unit.Statuses : StatusRowRenderer.PreviewEnhancements;
                var enfeeblements = isReal ? unit.Statuses : StatusRowRenderer.PreviewEnfeeblements;

                var frames = plugin.Configuration.HudBars;
                var place = ReferenceEquals(settings, frames.Player) ? "player" : ReferenceEquals(settings, frames.Focus) ? "focus" : "target";

                plugin.StatusRows.DrawRows(drawList, settings.Enhancements, settings.Enfeeblements, enhancements, enfeeblements, !isReal, playerId, barPos, barSize, place, !unit.IsHostile);
            }
        });

        // Clicks only make sense on a real unit, and not while the boxes are being moved (they use the same place).
        // The target of target only selects with the left click: the game gives it no menu.
        if (isReal && !plugin.Layout.UnlockMode)
            HandleClicks(element, unit, !ReferenceEquals(settings, plugin.Configuration.HudBars.TargetOfTarget));
    }

    // ---------------------------------------------------------------- clicks

    // A see-through window exactly over the frame takes the mouse clicks, so the game does not get them.
    // Left click targets the unit. Right click opens the game's own menu for the unit.
    private void HandleClicks(LayoutElement element, UnitSnapshot unit, bool contextMenu)
    {
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        HandleClicks(element.Id, pos, size, unit, contextMenu);
    }

    // The same, for a box that is not a layout element of its own (the bars of the party list).
    internal void HandleClicks(string id, Vector2 pos, Vector2 size, UnitSnapshot unit, bool contextMenu = true)
    {
        if (size.X < 1f || size.Y < 1f)
            return;

        // Where a game window is over our frame (its menu, the map...), our click areas step aside so the window can be clicked.
        if (plugin.GameCover.Contains(ImGui.GetIO().MousePos))
            return;

        // No window over the frame: while the mouse is over the box we tell Dalamud the mouse is ours (so the game does not
        // get the click) and read the click ourselves.
        var hovered = ImGui.IsMouseHoveringRect(pos, pos + size, false) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow);
        if (!hovered || !MayCapture())
            return;

        ImGui.SetNextFrameWantCaptureMouse(true);

        // Dalamud swaps the game's cursor for the Windows one while it captures the mouse; this asks it to keep the game's.
        cursorWanted = true;
        mouseOverEntityId = unit.EntityId;

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var clicked = Plugin.ObjectTable.SearchByEntityId(unit.EntityId);
            if (clicked is not null)
                Plugin.TargetManager.Target = clicked;
        }

        // Right click: the game's own menu for that unit (the same one as on its own frames), where the game has one.
        if (contextMenu && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
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

    // The MP bar of the player (CP for crafters, GP for gatherers): its own bar, in its own box of the Layout.
    private void DrawPlayerMp(HudBarSettings settings, UnitSnapshot? unit, bool preview)
    {
        if (!plugin.Layout.IsShown(MpElement))
            return;

        float fraction;
        uint mp, maxMp;

        if (unit is not null && unit.MaxMp > 0)
        {
            fraction = Math.Clamp(unit.Mp / (float)unit.MaxMp, 0f, 1f);
            mp = unit.Mp;
            maxMp = unit.MaxMp;
        }
        else if (preview)
        {
            fraction = 0.7f; // a sample, so there is something to see and drag
            mp = 8400;
            maxMp = 12000;
        }
        else
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(MpElement.Layout);
        var drawList = ImGui.GetBackgroundDrawList();
        var thickness = MathF.Max(1f, MathF.Round(scale));

        plugin.GameCover.Draw(pos, size, 400f * scale, () =>
        {
            DrawMpBar(drawList, settings, pos, pos + size, fraction, scale, thickness);

            // The number over the bar, with the same choices as the HP text.
            var mpText = settings.MpText;
            DrawText(drawList, UnitTextFormatter.FormatValue(mpText, mp, maxMp), mpText.Text, pos, size);
        });
    }

    private void DrawFrame(HudBarSettings settings, LayoutElement element, UnitSnapshot unit, bool isPlayerFrame)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        var max = pos + size;
        var drawList = ImGui.GetBackgroundDrawList();

        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        // Something without HP (an object, a shopkeeper) is a full bar in its colour.
        var hasHp = unit.MaxHp > 0;
        var fraction = hasHp ? Math.Clamp(unit.Hp / (float)unit.MaxHp, 0f, 1f) : (unit.Kind == UnitKind.Character ? 0f : 1f);
        var fillColor = HpColor(settings, unit, isPlayerFrame);

        // The target of target can be shown as just its name (no bar): the box stays only as the place of the text and the click.
        var barShown = !ReferenceEquals(settings, plugin.Configuration.HudBars.TargetOfTarget) || settings.ShowFrame;

        // Background, then the HP. The right end of the fill is straight, cut with a clip rectangle.
        if (barShown)
            drawList.AddRectFilled(pos, max, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        var fillRight = MathF.Round(pos.X + size.X * fraction);
        if (barShown && fillRight > pos.X)
        {
            // The whole rounded bar, cut at the end of the HP: the fill never sticks out of the rounded corners
            // (with a straight end it did when the HP was between about 95% and 100%).
            drawList.PushClipRect(pos, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(pos, max, ImGui.GetColorU32(fillColor), rounding);
            drawList.PopClipRect();
        }

        if (barShown && unit.ShieldPercent > 0)
            DrawShield(drawList, settings, pos, size, fraction, rounding, unit.ShieldPercent);

        if (barShown)
            drawList.AddRect(pos, max, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        // The texts are drawn last, over everything, and may stick out of the bar.
        // The name is shortened with "..." when it runs into the HP text (and when it starts inside the bar and would go out of it).
        var name = UnitTextFormatter.FormatName(unit.Name, settings.NameFormat, unit.IsPlayer, settings.NameFormatOnlyPlayers);

        // No HP number for things that have no HP.
        string? hpText = null;
        if (barShown && (hasHp || unit.Kind == UnitKind.Character))
            hpText = UnitTextFormatter.FormatValue(settings, unit.Hp, unit.MaxHp);

        var hpArea = hpText is null ? null : MeasureText(hpText, settings.Hp, pos, size);
        DrawText(drawList, name, settings.Name, pos, size, fitInBox: true, avoid: hpArea);

        if (hpText is not null)
            DrawText(drawList, hpText, settings.Hp, pos, size);
    }

    // The shield is as tall as the bar and follows its rounded corners.
    // If it fits, it goes right after the HP. If HP and shield together are more than the bar, the shield is drawn from the
    // right end of the bar over the HP (the more shield, the further left it reaches), so there is always a visible block.
    // It is translucent: the HP and the background show through it, so where it covers the HP you still see it.
    internal static void DrawShield(ImDrawListPtr drawList, HudBarSettings settings, Vector2 pos, Vector2 size, float fraction, float rounding, byte shieldPercent)
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
        color.W = Math.Clamp(color.W * Fade.Alpha, 0f, 1f);

        // The rounded bar cut to the part of the shield: it follows the corners of the bar, never sticks out of them.
        drawList.PushClipRect(new Vector2(left, pos.Y), new Vector2(right, max.Y), true);
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(color), rounding);
        drawList.PopClipRect();
    }

    // The colour of the HP: the hostile colour for enemies, and for players (characters controlled by a person) always the colour
    // of their role. Anything else (friendly NPCs) uses the friendly colour.
    // Sunset Orange (183, 92, 45): the colour of Ember, the chocobo of the author. It is the colour while the game's own is not known.
    private static readonly Vector4 DefaultChocoboColor = new(183f / 255f, 92f / 255f, 45f / 255f, 1f);

    // The colour of the chocobo frame: the colour the chocobo has now (read from the game). Dark coats (the blacks) are lifted to a
    // minimum brightness so the bar can be seen; the hue and the saturation are kept.
    private Vector4 ChocoboColor()
    {
        var color = plugin.GameData.ChocoboColor ?? DefaultChocoboColor;

        // Brightness (the biggest channel): below 0.62 every channel is scaled up so the biggest one reaches it (black becomes grey).
        var max = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        if (max < 0.62f)
        {
            if (max <= 0.001f)
                return new Vector4(0.62f, 0.62f, 0.62f, 1f);

            var scale = 0.62f / max;
            return new Vector4(color.X * scale, color.Y * scale, color.Z * scale, 1f);
        }

        return color;
    }

    internal Vector4 HpColor(HudBarSettings settings, UnitSnapshot unit, bool isPlayerFrame)
    {
        if (unit.IsCompanion)
            return ChocoboColor();

        if (unit.Kind == UnitKind.Object)
            return GlobalColors.Current.ObjectColor;

        if (unit.Kind == UnitKind.Pet)
            return GlobalColors.Current.PetMinion;

        var isPlayer = isPlayerFrame || unit.IsPlayer;

        if (!isPlayer && unit.IsHostile)
            return GlobalColors.Current.EnemyHp;

        if (isPlayer)
            return JobRoles.Color(plugin.Configuration.HudBars, unit.JobId, GlobalColors.Current.FriendlyNpc);

        return GlobalColors.Current.FriendlyNpc;
    }

    // A MP bar between two corners, filled to <fraction>.
    private static void DrawMpBar(ImDrawListPtr drawList, HudBarSettings settings, Vector2 min, Vector2 max, float fraction, float scale, float thickness)
    {
        var width = max.X - min.X;
        var rounding = MathF.Min(MathF.Round(GlobalColors.Current.Rounding * scale), (max.Y - min.Y) / 2f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(settings.MpBackgroundColor), rounding);

        var fillRight = MathF.Round(min.X + width * fraction);
        if (fillRight > min.X)
        {
            drawList.PushClipRect(min, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(settings.MpColor), rounding);
            drawList.PopClipRect();
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(settings.MpBorderColor), rounding, ImDrawFlags.None, thickness);
    }

    // Where the top left corner of a text goes: the same point of the text is placed on the anchor of the box, then the offset moves it.
    // Horizontally the same point of text and box meet; vertically the CENTRE of the letters goes on the anchor line.
    private static Vector2 PlaceText(FrameTextSettings settings, Vector2 boxPos, Vector2 boxSize, Vector2 textSize, float scale)
    {
        var fraction = settings.Anchor.Fraction();
        var pos = boxPos + boxSize * fraction - new Vector2(textSize.X * fraction.X, textSize.Y * 0.5f) + new Vector2(settings.OffsetX, settings.OffsetY) * scale;
        return new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
    }

    /// <summary>The area a text would take (null when it is off or empty). Another text can use it to keep away from it.</summary>
    internal (Vector2 Min, Vector2 Max)? MeasureText(string text, FrameTextSettings settings, Vector2 boxPos, Vector2 boxSize, OverlayFont font = OverlayFont.NahjaDisplay)
    {
        if (!settings.Enabled || string.IsNullOrEmpty(text))
            return null;

        using (var face = plugin.Fonts.PushCap(font, settings.TextSize))
        {
            var textSize = face.Measure(text);
            var pos = PlaceText(settings, boxPos, boxSize, textSize, ImGuiHelpers.GlobalScale);
            return (pos, pos + textSize);
        }
    }

    // Draws a text attached to a point of the bar: the same point of the text is placed on it, then the offset moves it.
    // White (or the chosen colour) with a black outline, readable over any bar colour and over the game behind it.
    // With <fitInBox> a text is shortened and ends in "..." (if not even the dots fit, nothing is drawn) when:
    //  - it runs into the area <avoid> (another text), always; and
    //  - it goes out of the box at the left or the right, but only if it STARTS inside the box (its point of the bar, moved by the
    //    offsets, is inside the box). A text the user put outside the box on purpose is never cut for that.
    internal void DrawText(ImDrawListPtr drawList, string text, FrameTextSettings settings, Vector2 boxPos, Vector2 boxSize, OverlayFont font = OverlayFont.NahjaDisplay,
        bool fitInBox = false, (Vector2 Min, Vector2 Max)? avoid = null)
    {
        if (!settings.Enabled || string.IsNullOrEmpty(text))
            return;

        var scale = ImGuiHelpers.GlobalScale;

        // The size is in logical pixels: the fonts are already built scaled by the UI scale.
        // The size is the height of the letters (not of the font's line box).
        using (var face = plugin.Fonts.PushCap(font, settings.TextSize))
        {
            var textSize = face.Measure(text);
            var pos = PlaceText(settings, boxPos, boxSize, textSize, scale);

            if (fitInBox)
            {
                var gap = MathF.Round(4f * scale);

                // Does the text start inside the box? Its point of the box, moved by the offsets, is the start.
                var startPoint = boxPos + boxSize * settings.Anchor.Fraction() + new Vector2(settings.OffsetX, settings.OffsetY) * scale;
                var startsInside = startPoint.X >= boxPos.X && startPoint.X <= boxPos.X + boxSize.X
                                   && startPoint.Y >= boxPos.Y && startPoint.Y <= boxPos.Y + boxSize.Y;
                var cut = text.Length;

                while (true)
                {
                    var candidate = cut >= text.Length ? text : text.Substring(0, cut) + "...";
                    var candidateSize = face.Measure(candidate);
                    var candidatePos = PlaceText(settings, boxPos, boxSize, candidateSize, scale);

                    var fits = !startsInside || (candidatePos.X >= boxPos.X && candidatePos.X + candidateSize.X <= boxPos.X + boxSize.X);

                    if (fits && avoid is { } area)
                    {
                        var overlapsX = candidatePos.X < area.Max.X + gap && candidatePos.X + candidateSize.X > area.Min.X - gap;
                        var overlapsY = candidatePos.Y < area.Max.Y && candidatePos.Y + candidateSize.Y > area.Min.Y;
                        fits = !(overlapsX && overlapsY);
                    }

                    if (fits)
                    {
                        text = candidate;
                        pos = candidatePos;
                        break;
                    }

                    // Not even the three dots fit: nothing is drawn.
                    if (cut == 0)
                        return;

                    cut--;
                }
            }

            face.Draw(drawList, pos, text, ImGui.GetColorU32(settings.Color));
        }
    }
}
