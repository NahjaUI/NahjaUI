using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NahjaUI.Modules.Trackers;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.HudBars;
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

    private static readonly Vector4 EnemyColor = new(0.79f, 0.29f, 0.27f, 1f);

    // The game's own progress bars of enemies, found in the game's addon list.
    private static readonly string[] NativeProgressBars = { "CastBarEnemy" };

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
    private readonly NativeWindowHider hider = new();
    private readonly Dictionary<ulong, int> gameMarkerIcons = new();
    private readonly object gameFlagsLock = new();

    private readonly Plugin plugin;

    public NamePlateModule(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.NamePlateGui.OnDataUpdate += OnDataUpdate;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeProgressBars, OnNativeProgressBarPreDraw);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeTargetCursor, OnNativeTargetCursorPreDraw);
    }

    public void Dispose()
    {
        Plugin.NamePlateGui.OnDataUpdate -= OnDataUpdate;
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeProgressBars, OnNativeProgressBarPreDraw);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeTargetCursor, OnNativeTargetCursorPreDraw);
        hider.ShowAll();
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

            // A kind of plate that is switched off keeps the game's own plate: nothing is read or changed.
            var style = settings.StyleOf(RelationOf(handler, kind));
            if (!style.Active)
                continue;

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

                    // The colors the game gives this plate (its category: engaged, claimed, party, role...)..
                    gameColors[gameObject.GameObjectId] = (handler.TextColor, handler.EdgeColor);

                    // The small icon the game puts at the left of the name (hostile, neutral, hunt ranks...). -1: none.
                    gameNameIcons[gameObject.GameObjectId] = handler.NameIconId;

                    // The big icon the game floats above the plate (quest mark, objective...). 0: none.
                    // Once we switch the game's marker off the handler reads 0 on the frames the game does not refresh, so a zero
                    // only counts when the game just refreshed this plate.
                    if (handler.IsUpdating || handler.MarkerIconId != 0)
                    {
                        gameMarkerIcons[gameObject.GameObjectId] = handler.MarkerIconId;
                    }
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
            if (style.ShowMarkerIcon)
                handler.MarkerIconId = 0;
        }
    }

    // Which kind of plate this is (the same kinds as the ones of the game snapshot).
    private static NamePlateRelation RelationOf(INamePlateUpdateHandler handler, int kind)
    {
        if (kind == EnemyKind)
            return NamePlateRelation.Enemy;

        if (handler.GameObject is not { } owner)
            return NamePlateRelation.Other;

        if (kind == PlayerKind)
            return GameDataProvider.PlayerRelation(owner);

        return owner is Dalamud.Game.ClientState.Objects.Types.IBattleChara ? NamePlateRelation.Friendly : NamePlateRelation.Other;
    }

    // Same trick as the other native windows: do not let the game draw it and keep it hidden.
    private unsafe void OnNativeProgressBarPreDraw(AddonEvent type, AddonArgs args)
    {
        var settings = plugin.Configuration.NamePlates;
        // The game's progress bar of the target goes away when our progress bar replaces it for the enemies.
        var replaced = settings.EnemyNpcs.Active && settings.EnemyNpcs.ShowProgressBar || settings.EnemyPlayers.Active && settings.EnemyPlayers.ShowProgressBar;
        if (!settings.Enabled || !replaced)
            return;

        hider.Hide(args);
    }

    // The game's big target arrow is always hidden while the plates are on (our plate's accent ring marks the target).
    private unsafe void OnNativeTargetCursorPreDraw(AddonEvent type, AddonArgs args)
    {
        var settings = plugin.Configuration.NamePlates;
        if (!settings.Enabled)
            return;

        hider.Hide(args);
    }

    // Does the module want one of the game's windows hidden right now?
    private bool HidesNative(string windowName)
    {
        var settings = plugin.Configuration.NamePlates;
        if (!settings.Enabled)
            return false;

        if (windowName != NativeTargetCursor[0])
        {
            return settings.EnemyNpcs.Active && settings.EnemyNpcs.ShowProgressBar || settings.EnemyPlayers.Active && settings.EnemyPlayers.ShowProgressBar;
        }

        return true;
    }

    public void Draw()
    {
        hider.ShowAgain(HidesNative);
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

        var screenSize = ImGuiHelpers.MainViewport.Size;

        Vector2 AnchorOf(NamePlateSnapshot plate) => new((plate.Ndc.X + 1f) * 0.5f * screenSize.X, (1f - plate.Ndc.Y) * 0.5f * screenSize.Y);
        Vector2 SizeOf(NamePlateSnapshot plate)
        {
            var style = settings.StyleOf(plate.Relation);
            return new Vector2(style.WidthPx * scale, style.HeightPx * scale);
        }

        // The area a plate takes: its bar plus the name above it (the name can be wider than the bar).
        (Vector2 Min, Vector2 Max) AreaOf(NamePlateSnapshot plate)
        {
            var anchor = AnchorOf(plate);
            var plateSize = SizeOf(plate);
            var half = MathF.Max(plateSize.X, 150f * scale) / 2f;
            return (new Vector2(anchor.X - half, anchor.Y - 30f * scale), new Vector2(anchor.X + half, anchor.Y + plateSize.Y));
        }

        // Only the kinds of plate that are switched on.
        var plates = snapshot.NamePlates.Where(p => settings.StyleOf(p.Relation).Active).ToList();

        // The target is drawn over the rest: a plate whose area touches the target's is not drawn, so two names never mix.
        var target = plates.FirstOrDefault(p => p.IsTarget);
        var targetArea = target is null ? default : AreaOf(target);

        foreach (var plate in plates.OrderBy(p => p.IsTarget))
        {
            var anchor = AnchorOf(plate);
            var plateSize = SizeOf(plate);
            var current = plate;

            var hidden = false;
            if (target is not null && !plate.IsTarget)
            {
                var area = AreaOf(plate);
                hidden = area.Min.X < targetArea.Max.X && area.Max.X > targetArea.Min.X
                         && area.Min.Y < targetArea.Max.Y && area.Max.Y > targetArea.Min.Y;
            }

            plugin.GameCover.Draw(anchor - new Vector2(plateSize.X / 2f, 0f), plateSize, 200f * scale,
                hidden ? () => { } : () => DrawPlate(drawList, scale, settings, settings.StyleOf(current.Relation), current, current.IsTarget, snapshot.Player?.Level ?? 0));
        }
    }

    private void DrawPlate(ImDrawListPtr drawList, float scale, NamePlateSettings settings, NamePlateStyle style, NamePlateSnapshot plate, bool isTarget, int playerLevel)
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
        if (isTarget)
            flags |= GameDrawsGauge;

        // The name and the title the game would have written (players); enemies keep the plain name.
        var displayName = plate.Name;
        GameTexts? texts = null;
        if (!plate.Relation.IsEnemy() && !StreamerMode.IsAlias(plate.Name))
        {
            lock (gameFlagsLock)
                gameTexts.TryGetValue(plate.Key, out texts);

            if (texts is not null && !string.IsNullOrWhiteSpace(texts.Name))
                displayName = texts.Name;
        }

        // The game wraps some names in its own quote symbols (in PvP the plate of an enemy player is only its job, between two of them):
        // our font does not have them and they showed as "?". Only what the font can draw is kept.
        var drawableName = StripUndrawable(displayName);
        if (drawableName.Length > 0)
            displayName = drawableName;

        // The bar takes the color the game gives this plate (Character Configuration > Nameplates). The color of the name is
        // ours (the game's option colors the bar) and the outline of the texts is always black.
        var nameColor = ImGui.GetColorU32(style.NameColor);
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

        var width = MathF.Round(style.WidthPx * scale);
        var height = MathF.Round(style.HeightPx * scale);
        var rounding = MathF.Min(MathF.Round(GlobalColors.Current.Rounding * scale), height / 2f);

        // The game's point is in the range -1 to 1 with y going up: turn it into pixels of the screen.
        var size = ImGuiHelpers.MainViewport.Size;
        var screen = new Vector2((plate.Ndc.X + 1f) * 0.5f * size.X, (1f - plate.Ndc.Y) * 0.5f * size.Y);

        // The bar is centred under that point; the name above it overlaps the top edge.
        var min = new Vector2(MathF.Round(screen.X - width / 2f), MathF.Round(screen.Y));
        var max = min + new Vector2(width, height);

        if (drawBar)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(style.BarBackgroundColor), rounding);

            // The fill is the full rounded bar cut by a clip rectangle, so a short fill never sticks out of the corners.
            var fraction = plate.MaxHp == 0 ? 0f : Math.Clamp(plate.Hp / (float)plate.MaxHp, 0f, 1f);
            var fillRight = MathF.Round(min.X + width * fraction);
            if (fillRight > min.X)
            {
                drawList.PushClipRect(min, new Vector2(fillRight, max.Y), true);
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(gameBarColor ?? FallbackBarColor(plate.Relation)), rounding);
                drawList.PopClipRect();
            }

            drawList.AddRect(min, max, ImGui.GetColorU32(style.BarBorderColor), rounding, ImDrawFlags.None, 1f);

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
            var showLevel = hasHp && style.LevelMode != NamePlateLevelMode.Hidden
                            && !(style.HideSameLevel && plate.Level == playerLevel);
            var levelText = style.LevelMode == NamePlateLevelMode.WithPrefix ? $"Lv {plate.Level}" : plate.Level.ToString();
            var text = showLevel ? $"{displayName}  {levelText}" : displayName;
            // The size is the height of the letters; a line is that tall, and the next one starts a gap below.
            using (var face = plugin.Fonts.PushCap(OverlayFont.NahjaDisplay, style.NameSize))
            {
                // Name and title are ONE block of text (title in the line above or under the name, as the game puts it).
                // The same point of the block is placed on the chosen point of the bar, then the offset moves it.
                var title = texts is { ShowTitle: true } && !string.IsNullOrWhiteSpace(texts.Title) ? texts.Title : null;
                var textSize = face.Measure(text);
                var lineGap = MathF.Round(face.Height * 0.45f);
                var titleSize = title is null ? Vector2.Zero : face.Measure(title);

                var anchorFraction = style.NameAnchor.Fraction();
                var offset = new Vector2(style.NameOffsetX, style.NameOffsetY) * scale;

                // Where the name alone would be.
                var namePos = min + (max - min) * anchorFraction - new Vector2(textSize.X * anchorFraction.X, textSize.Y * 0.5f) + offset;

                // The small icon is part of the name line: the whole line (icon + name + level) is what gets centered.
                var iconWidth = style.ShowNameIcon && nameIconId > 0 && hasNameIcon
                    ? MathF.Round(style.NameIconSizePx * scale) + MathF.Round(style.NameIconGapPx * scale)
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
                if (style.ShowNameIcon && nameIconId > 0 && hasNameIcon)
                {
                    var side = MathF.Round(style.NameIconSizePx * scale);
                    var iconMin = new Vector2(
                        pos.X - side - MathF.Round(style.NameIconGapPx * scale),
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
                    face.Draw(drawList, levelPos, levelText, ImGui.GetColorU32(style.LevelColor), edgeColor);
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
        if (style.ShowMarkerIcon && markerIconId > 0)
        {
            var side = MathF.Round(style.MarkerIconSizePx * scale);
            var markerMin = new Vector2(
                MathF.Round(markerCenterX - side / 2f + style.MarkerOffsetX * scale),
                MathF.Round(markerTop - side + style.MarkerOffsetY * scale));
            var markerTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup((uint)markerIconId)).GetWrapOrEmpty();
            drawList.AddImage(markerTexture.Handle, markerMin, markerMin + new Vector2(side, side), new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);
        }

        // Text-only plates (Others) never have a progress bar or status icons.
        var hasExtras = plate.Relation != NamePlateRelation.Other;

        if (hasExtras && style.ShowProgressBar && plate.Cast is not null && plate.Cast.TotalTime > 0f)
        {
            var progressBarHeight = MathF.Round(style.ProgressBarHeightPx * scale);
            var progressBarWidth = style.ProgressBarWidthPx > 0 ? MathF.Round(style.ProgressBarWidthPx * scale) : width;

            // A point of the progress bar on a point of the HP bar, then moved by the offsets (plus Y is up in the page, saved downwards).
            var hpBarSize = max - min;
            var progressBarPos = min + hpBarSize * style.ProgressBarBarAnchor.Fraction()
                                 - new Vector2(progressBarWidth, progressBarHeight) * style.ProgressBarSelfAnchor.Fraction();
            progressBarPos = new Vector2(MathF.Round(progressBarPos.X), MathF.Round(progressBarPos.Y));

            progressBarPos += new Vector2(MathF.Round(style.ProgressBarOffsetX * scale), MathF.Round(style.ProgressBarOffsetY * scale));
            DrawProgressBar(drawList, scale, style, plate.Cast, progressBarPos.X, progressBarPos.Y, progressBarWidth);
        }

        // The rows of status icons are attached to the HP bar, so they only go with plates that draw one.
        if (hasExtras && drawBar && plate.Statuses is { Count: > 0 } statuses)
        {
            var playerId = plugin.GameData.Current.Player?.EntityId ?? 0u;
            var barSize = max - min;
            plugin.StatusRows.DrawRows(drawList, style.Enhancements, style.Enfeeblements, statuses, statuses, false, playerId, min, barSize, "nameplate", !plate.Relation.IsEnemy());
        }
    }

    // The game wraps the title in its own quote symbols (special characters our font does not have, they showed as "?").
    // Everything the font cannot draw is dropped and the title gets plain quotes.
    private static string CleanTitle(string raw)
    {
        var text = StripUndrawable(raw);
        return text.Length == 0 ? string.Empty : $"\u00AB{text}\u00BB";
    }

    // Keeps only the characters the NahjaUI fonts can draw (the same ranges as OverlayFonts: Latin, accents, common punctuation).
    private static string StripUndrawable(string raw)
    {
        var kept = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            var drawable = (c >= 0x20 && c <= 0x7E) || (c >= 0xA0 && c <= 0x17F) || (c >= 0x2010 && c <= 0x2027);
            if (drawable)
                kept.Append(c);
        }

        return kept.ToString().Trim();
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
        relation.IsEnemy() ? EnemyColor : new Vector4(0.62f, 0.62f, 0.72f, 1f);

    // The progress bar: the fill, the action name and the time. The colors are ours (Nameplates page); the fill says if the cast
    // can be interrupted. The texts are placed like the other frame texts.
    private void DrawProgressBar(ImDrawListPtr drawList, float scale, NamePlateStyle settings, CastSnapshot cast, float left, float top, float width)
    {
        var height = MathF.Round(settings.ProgressBarHeightPx * scale);
        var rounding = MathF.Min(MathF.Round(GlobalColors.Current.Rounding * scale), height / 2f);
        var min = new Vector2(left, top);
        var max = min + new Vector2(width, height);
        var color = cast.Interruptible ? settings.ProgressBarColor : settings.ProgressBarUninterruptibleColor;

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(settings.ProgressBarBackgroundColor), rounding);

        // The fill is the full rounded frame cut by a clip rectangle, so it never sticks out of the corners.
        var fraction = Math.Clamp(cast.CurrentTime / cast.TotalTime, 0f, 1f);
        var fillRight = MathF.Round(min.X + width * fraction);

        if (fillRight > min.X)
        {
            drawList.PushClipRect(min, new Vector2(fillRight, max.Y), true);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(color), rounding);
            drawList.PopClipRect();
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(settings.ProgressBarBorderColor), rounding, ImDrawFlags.None, 1f);

        var boxSize = max - min;
        var timeText = settings.ProgressBarTimeMode == NahjaUI.Modules.ProgressBars.ProgressBarTimeMode.Remaining
            ? MathF.Max(0f, cast.TotalTime - cast.CurrentTime).ToString("0.0")
            : $"{cast.CurrentTime:0.0} / {cast.TotalTime:0.0}";
        // The action name is shortened with "..." so it does not go out of the bar or run into the time.
        if (settings.ProgressBarName.Enabled)
        {
            var timeArea = plugin.HudBarSystem.MeasureText(timeText, settings.ProgressBarTime, min, boxSize);
            plugin.HudBarSystem.DrawText(drawList, GameNames.CastName(cast.ActionType, cast.ActionId), settings.ProgressBarName, min, boxSize, fitInBox: true, avoid: timeArea);
        }

        if (settings.ProgressBarTime.Enabled)
            plugin.HudBarSystem.DrawText(drawList, timeText, settings.ProgressBarTime, min, boxSize);
    }
}
