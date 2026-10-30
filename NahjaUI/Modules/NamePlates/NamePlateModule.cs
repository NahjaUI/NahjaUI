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
/// Draws our own plates over the enemies (a HP bar and a name) and hides the game's plate of those enemies.
/// It only reads the game snapshot; the plates themselves (which enemies, where) are found in GameDataProvider.
/// </summary>
internal sealed class NamePlateModule : IDisposable
{
    // The kind of plate of an enemy (UIObjectKind.BattleNpcEnemy in the game's own list).
    private const int EnemyKind = 3;

    // The kind of plate of a player character (UIObjectKind.PlayerCharacter).
    private const int PlayerKind = 0;

    private static readonly Vector4 BackgroundColor = new(0.04f, 0.035f, 0.09f, 0.92f);
    private static readonly Vector4 BorderColor = new(0f, 0f, 0f, 1f);
    private static readonly Vector4 EnemyColor = new(0.79f, 0.29f, 0.27f, 1f);

    // The game's own cast bars of enemies, found in the game's addon list.
    private static readonly string[] NativeCastBars = { "CastBarEnemy" };

    // The big arrow the game floats over the target. The game has the addon AddonTargetCursor; its name string "_TargetCursor"
    // is NOT confirmed in the clones (test: if the arrow stays, the name is wrong).
    private static readonly string[] NativeTargetCursor = { "_TargetCursor" };

    // What the game decided for each plate before we hid it (bit 1: draw the name, bit 2: draw the HP bar).
    private const int GameDrawsName = 1;
    private const int GameDrawsGauge = 2;

    // The texts the game wrote for a plate, already shaped by the game's own options (name display type: full name, surname,
    // abbreviated, forename, initials; title shown or not). We never format a name ourselves.
    private sealed record GameTexts(string Name, string Title, bool ShowTitle, bool TitleAbove);

    private readonly Dictionary<ulong, GameTexts> gameTexts = new();
    private readonly Dictionary<ulong, int> gameFlags = new();
    private readonly Dictionary<ulong, (uint Text, uint Edge)> gameColors = new();
    private readonly Dictionary<ulong, int> gameNameIcons = new();
    private readonly Dictionary<ulong, int> gameMarkerIcons = new();
    private readonly object gameFlagsLock = new();

    private readonly Plugin plugin;

    public NamePlateModule(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.NamePlateGui.OnDataUpdate += OnDataUpdate;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeCastBars, OnNativeCastPreDraw);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeTargetCursor, OnNativeTargetCursorPreDraw);
    }

    public void Dispose()
    {
        Plugin.NamePlateGui.OnDataUpdate -= OnDataUpdate;
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeCastBars, OnNativeCastPreDraw);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeTargetCursor, OnNativeTargetCursorPreDraw);
        Plugin.NamePlateGui.RequestRedraw();
    }

    // The game is about to draw its plates: switch off the name and the HP bar of the enemies.
    private void OnDataUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        var settings = plugin.Configuration.NamePlates;
        if (!settings.Enabled)
            return;

        foreach (var handler in handlers)
        {
            var kind = (int)handler.NamePlateKind;

            // Remember what the game wanted before it is switched off. A zero is either "the game wants nothing" or what
            // we wrote ourselves the last time, so it does not replace what we already know.
            var flags = handler.VisibilityFlags;
            if (handler.GameObject is { } gameObject)
            {
                lock (gameFlagsLock)
                {
                    // Plates that are neither enemies nor players: a zero the game just wrote is a real "show nothing".
                    if (flags != 0 || (kind != EnemyKind && kind != PlayerKind && handler.IsUpdating))
                        gameFlags[gameObject.GameObjectId] = flags;

                    // The colors the game gives this plate (its category: engaged, claimed, party, role...). Test: only shown.
                    gameColors[gameObject.GameObjectId] = (handler.TextColor, handler.EdgeColor);

                    // The small icon the game puts at the left of the name (hostile, neutral, hunt ranks...). -1: none.
                    gameNameIcons[gameObject.GameObjectId] = handler.NameIconId;

                    // The big icon the game floats above the plate (quest mark, objective...). 0: none.
                    // Once we switch the game's marker off the handler reads 0 on the frames the game does not refresh, so a zero
                    // only counts when the game just refreshed this plate.
                    if (handler.IsUpdating || handler.MarkerIconId != 0)
                        gameMarkerIcons[gameObject.GameObjectId] = handler.MarkerIconId;
                }
            }

            // The texts only change when the game refreshes the plate, so they are read then (and the first time).
            if (kind != EnemyKind && handler.GameObject is { } owner)
            {
                var known = false;
                lock (gameFlagsLock)
                    known = gameTexts.ContainsKey(owner.GameObjectId);

                if (handler.IsUpdating || !known)
                {
                    var texts = new GameTexts(
                        handler.Name.TextValue,
                        CleanTitle(handler.Title.TextValue),
                        handler.DisplayTitle,
                        handler.IsPrefixTitle);

                    lock (gameFlagsLock)
                        gameTexts[owner.GameObjectId] = texts;
                }
            }

            handler.VisibilityFlags = 0;

            // The game floats its own marker even when the plate is switched off: when ours is drawn the game's goes away.
            if (settings.ShowMarkerIcon)
                handler.MarkerIconId = 0;
        }
    }

    // Same trick as the other native windows: do not let the game draw it and keep it hidden.
    private unsafe void OnNativeCastPreDraw(AddonEvent type, AddonArgs args)
    {
        var settings = plugin.Configuration.NamePlates;
        if (!settings.Enabled || !settings.ShowCast)
            return;

        args.PreventOriginal();

        var addon = args.GetAddonPointer<AtkUnitBase>();
        if (addon != null)
            addon->IsVisible = false;
    }

    // The game's big target arrow is always hidden while the plates are on (our plate's accent ring marks the target).
    private unsafe void OnNativeTargetCursorPreDraw(AddonEvent type, AddonArgs args)
    {
        var settings = plugin.Configuration.NamePlates;
        if (!settings.Enabled)
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

        foreach (var plate in snapshot.NamePlates)
        {
            DrawPlate(drawList, scale, settings, plate, plate.IsTarget, snapshot.Player?.Level ?? 0);
        }
    }

    private void DrawPlate(ImDrawListPtr drawList, float scale, NamePlateSettings settings, NamePlateSnapshot plate, bool isTarget, int playerLevel)
    {
        // What the game would have drawn for this plate (everything, when it is not known yet or the option is off).
        var flags = GameDrawsName | GameDrawsGauge;
        lock (gameFlagsLock)
        {
            if (gameFlags.TryGetValue(plate.Key, out var known))
                flags = known;
        }

        // The target always gets its HP bar, whatever the game's options say (looking at something is asking for more than its name).
        // The name still follows the game.
        if (isTarget && settings.ShowBarOnTarget)
            flags |= GameDrawsGauge;


        // The name and the title the game would have written (players); enemies keep the plain name.
        var displayName = plate.Name;
        GameTexts? texts = null;
        if (plate.Relation != NamePlateRelation.Enemy)
        {
            lock (gameFlagsLock)
                gameTexts.TryGetValue(plate.Key, out texts);

            if (texts is not null && !string.IsNullOrWhiteSpace(texts.Name))
                displayName = texts.Name;
        }

        // The bar takes the color the game gives this plate (Character Configuration > Nameplates). The color of the name is
        // ours (the game's option colors the bar) and the outline of the texts is always black.
        var nameColor = ImGui.GetColorU32(settings.NameColor);
        var edgeColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f));
        Vector4? gameBarColor = null;
        lock (gameFlagsLock)
        {
            if (gameColors.TryGetValue(plate.Key, out var colors))
            {
                var gameText = FromGame(colors.Text, 0xFFFFFFFFu);
                gameBarColor = ImGui.ColorConvertU32ToFloat4(gameText);
            }
        }

        var nameIconId = 0;
        var markerIconId = 0;
        lock (gameFlagsLock)
        {
            gameNameIcons.TryGetValue(plate.Key, out nameIconId);
            gameMarkerIcons.TryGetValue(plate.Key, out markerIconId);
        }

        var hasHp = plate.Relation != NamePlateRelation.Other;

        // The small icon belongs to enemies and players. On friendly battle NPCs (the turn-in NPCs of a FATE, for example) the value
        // the game keeps in that slot is a leftover (sometimes a hostile icon, sometimes a class icon) that the game never shows.
        var hasNameIcon = hasHp && plate.Relation != NamePlateRelation.Friendly;
        var drawBar = hasHp && (flags & GameDrawsGauge) != 0;
        var drawName = (flags & GameDrawsName) != 0;

        // The target without a HP bar (an NPC, a door...) is marked with chevrons around its name, so its name always shows.
        var chevrons = isTarget && !hasHp;
        if (chevrons)
            drawName = true;

        var width = MathF.Round(settings.WidthPx * scale);
        var height = MathF.Round(settings.HeightPx * scale);
        var rounding = MathF.Min(MathF.Round(settings.RoundingPx * scale), height / 2f);

        // The game's point is in the range -1 to 1 with y going up: turn it into pixels of the screen.
        var size = ImGuiHelpers.MainViewport.Size;
        var screen = new Vector2((plate.Ndc.X + 1f) * 0.5f * size.X, (1f - plate.Ndc.Y) * 0.5f * size.Y);

        // The bar is centred under that point; the name above it overlaps the top edge.
        var min = new Vector2(MathF.Round(screen.X - width / 2f), MathF.Round(screen.Y));
        var max = min + new Vector2(width, height);

        if (drawBar)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(BackgroundColor), rounding);

            // The fill is the full rounded bar cut by a clip rectangle, so a short fill never sticks out of the corners.
            var fraction = plate.MaxHp == 0 ? 0f : Math.Clamp(plate.Hp / (float)plate.MaxHp, 0f, 1f);
            var fillRight = MathF.Round(min.X + width * fraction);
            if (fillRight > min.X)
            {
                drawList.PushClipRect(min, new Vector2(fillRight, max.Y), true);
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(gameBarColor ?? FallbackBarColor(plate.Relation)), rounding);
                drawList.PopClipRect();
            }

            drawList.AddRect(min, max, ImGui.GetColorU32(BorderColor), rounding, ImDrawFlags.None, 1f);

            if (isTarget)
            {
                // A line is centered on its rectangle: the rectangle is pushed out by half the line (whole pixels) so the ring starts
                // exactly where the bar ends (no gap of screen showing between the bar and the ring).
                var ringWidth = MathF.Max(2f, MathF.Round(2f * scale));
                var ring = new Vector2(ringWidth / 2f, ringWidth / 2f);
                drawList.AddRect(min - ring, max + ring, ImGui.GetColorU32(plugin.Configuration.TargetRingColor), rounding + ringWidth / 2f, ImDrawFlags.None, ringWidth);
            }
        }

        // Where the big marker goes: over the name block (title included) when the name shows, over the bar otherwise.
        var markerTop = min.Y;
        var markerCenterX = min.X + (max.X - min.X) / 2f;

        if (drawName)
        {
            var showLevel = plate.Relation == NamePlateRelation.Enemy && settings.LevelMode != NamePlateLevelMode.Hidden
                            && !(settings.HideSameLevel && plate.Level == playerLevel);
            var levelText = settings.LevelMode == NamePlateLevelMode.WithPrefix ? $"Lv {plate.Level}" : plate.Level.ToString();
            var text = showLevel ? $"{displayName}  {levelText}" : displayName;
            // The size is the height of the letters; a line is that tall, and the next one starts a gap below.
            using (var face = plugin.Fonts.PushCap(settings.NameFont, settings.NameSize))
            {
                // Name and title are ONE block of text (title in the line above or under the name, as the game puts it).
                // The same point of the block is placed on the chosen point of the bar, then the offset moves it.
                var title = texts is { ShowTitle: true } && !string.IsNullOrWhiteSpace(texts.Title) ? texts.Title : null;
                var textSize = face.Measure(text);
                var lineGap = MathF.Round(face.Height * 0.45f);
                var titleSize = title is null ? Vector2.Zero : face.Measure(title);

                var anchorFraction = settings.NameAnchor.Fraction();
                var offset = new Vector2(settings.NameOffsetX, settings.NameOffsetY) * scale;

                // Where the name alone would be.
                var namePos = min + (max - min) * anchorFraction - new Vector2(textSize.X * anchorFraction.X, textSize.Y * 0.5f) + offset;

                // The small icon is part of the name line: the whole line (icon + name + level) is what gets centered.
                var iconWidth = settings.ShowNameIcon && nameIconId > 0 && hasNameIcon
                    ? MathF.Round(settings.NameIconSizePx * scale) + MathF.Round(settings.NameIconGapPx * scale)
                    : 0f;
                var lineWidth = iconWidth + textSize.X;

                var blockWidth = MathF.Max(lineWidth, titleSize.X);
                var blockLeft = min.X + (max.X - min.X) * anchorFraction.X - blockWidth * anchorFraction.X + offset.X;

                // With a title the block gets taller. A name placed over the bar grows upwards (so the extra line never
                // covers the bar); a name placed under or beside the bar grows downwards.
                var blockTop = namePos.Y;
                var titleAbove = title is not null && texts!.TitleAbove;
                if (title is not null && anchorFraction.Y < 0.5f)
                    blockTop -= titleSize.Y + lineGap;

                markerTop = blockTop;
                markerCenterX = blockLeft + blockWidth / 2f;

                var nameTop = titleAbove ? blockTop + titleSize.Y + lineGap : blockTop;
                var titleTop = titleAbove ? blockTop : blockTop + textSize.Y + lineGap;

                // Each line is lined up inside the block like the name is on the bar (left, centre or right).
                var nameX = MathF.Round(blockLeft + (blockWidth - lineWidth) * anchorFraction.X + iconWidth);
                var pos = new Vector2(nameX, MathF.Round(nameTop));

                var leftEdge = pos.X;

                // The game's small icon at the left of the name (only for plates with HP: on objects and NPCs the value the game keeps in
                // that slot is not shown by the game itself, it looked like a leftover, e.g. a hostile icon on a quest destination).
                if (settings.ShowNameIcon && nameIconId > 0 && hasNameIcon)
                {
                    var side = MathF.Round(settings.NameIconSizePx * scale);
                    var iconMin = new Vector2(
                        pos.X - side - MathF.Round(settings.NameIconGapPx * scale),
                        MathF.Round(pos.Y + (textSize.Y - side) / 2f));
                    var iconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup((uint)nameIconId)).GetWrapOrEmpty();
                    drawList.AddImage(iconTexture.Handle, iconMin, iconMin + new Vector2(side, side), new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);
                    leftEdge = iconMin.X;
                }

                if (showLevel)
                {
                    // The name and the level are two pieces, so the level can have its own color.
                    var namePart = $"{displayName}  ";
                    face.Draw(drawList, pos, namePart, nameColor, edgeColor);
                    var levelPos = new Vector2(MathF.Round(pos.X + face.Width(namePart)), pos.Y);
                    face.Draw(drawList, levelPos, levelText, ImGui.GetColorU32(settings.LevelColor), edgeColor);
                }
                else
                {
                    face.Draw(drawList, pos, text, nameColor, edgeColor);
                }

                // The target without a HP bar: chevrons pointing in at both sides of the name line.
                if (chevrons)
                {
                    var accent = ImGui.GetColorU32(plugin.Configuration.TargetRingColor);
                    var chevronGap = MathF.Round(4f * scale);
                    var leftChevronWidth = face.Width("\u203A");
                    face.Draw(drawList, new Vector2(MathF.Round(leftEdge - chevronGap - leftChevronWidth), pos.Y), "\u203A", accent, edgeColor);
                    face.Draw(drawList, new Vector2(MathF.Round(pos.X + textSize.X + chevronGap), pos.Y), "\u2039", accent, edgeColor);
                }

                if (title is not null)
                {
                    var titleX = MathF.Round(blockLeft + (blockWidth - titleSize.X) * anchorFraction.X);
                    face.Draw(drawList, new Vector2(titleX, MathF.Round(titleTop)), title, nameColor, edgeColor);
                }
            }
        }

        // The game's big marker, centered over the name block and sitting on top of it.
        if (settings.ShowMarkerIcon && markerIconId > 0)
        {
            var side = MathF.Round(settings.MarkerIconSizePx * scale);
            var markerMin = new Vector2(
                MathF.Round(markerCenterX - side / 2f + settings.MarkerOffsetX * scale),
                MathF.Round(markerTop - side + settings.MarkerOffsetY * scale));
            var markerTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup((uint)markerIconId)).GetWrapOrEmpty();
            drawList.AddImage(markerTexture.Handle, markerMin, markerMin + new Vector2(side, side), new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);
        }

        if (settings.ShowGameFlags)
        {
            var colorsText = string.Empty;
            lock (gameFlagsLock)
            {
                if (gameColors.TryGetValue(plate.Key, out var colors))
                    colorsText = $"  text {colors.Text:X8}  edge {colors.Edge:X8}";
            }

            using (var face = plugin.Fonts.PushCap(settings.NameFont, 9))
                face.Draw(drawList, new Vector2(max.X + 4f * scale, min.Y), $"flags {flags}{colorsText}  icon {nameIconId}  marker {markerIconId}", ImGui.GetColorU32(new Vector4(0.6f, 1f, 0.6f, 1f)));
        }

        if (settings.ShowCast && plate.Cast is not null && plate.Cast.TotalTime > 0f)
            DrawCast(drawList, scale, settings, plate.Cast, min.X, max.Y + MathF.Round(settings.CastGapPx * scale), width);
    }

    // The game wraps the title in its own quote symbols (special characters our font does not have, they showed as "?").
    // Everything the font cannot draw is dropped and the title gets plain quotes.
    private static string CleanTitle(string raw)
    {
        var kept = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            var drawable = (c >= 0x20 && c <= 0x7E) || (c >= 0xA0 && c <= 0x17F) || (c >= 0x2010 && c <= 0x2027);
            if (drawable)
                kept.Append(c);
        }

        var text = kept.ToString().Trim();
        return text.Length == 0 ? string.Empty : $"\u00AB{text}\u00BB";
    }

    // The game stores a color as a number (red in the lowest byte, like the colors of ImGui). A zero alpha means "not set":
    // it is drawn solid. A zero color gives the fallback.
    private static uint FromGame(uint value, uint fallback)
    {
        if (value == 0)
            return fallback;

        return (value >> 24) == 0 ? value | 0xFF000000u : value;
    }

    // Only until the game's color of a plate is known (the first frames).
    private static Vector4 FallbackBarColor(NamePlateRelation relation) =>
        relation == NamePlateRelation.Enemy ? EnemyColor : new Vector4(0.62f, 0.62f, 0.72f, 1f);

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
        using (var face = plugin.Fonts.PushCap(settings.CastFont, settings.CastTextSize))
        {
            var y = MathF.Round((min.Y + max.Y - face.Height) / 2f);
            var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

            // The time is at the right, the spell name at the left (shortened with "..." if it does not fit).
            var timeText = MathF.Max(0f, cast.TotalTime - cast.CurrentTime).ToString("0.0");
            var timeWidth = face.Width(timeText);
            face.Draw(drawList, new Vector2(MathF.Round(max.X - padding - timeWidth), y), timeText, white);

            var name = Fit(face, GameNames.CastName(cast.ActionType, cast.ActionId), width - padding * 3f - timeWidth);
            face.Draw(drawList, new Vector2(MathF.Round(min.X + padding), y), name, white);
        }
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
