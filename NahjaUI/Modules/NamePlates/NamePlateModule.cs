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
    // JobOnly: the text of the game is the name of the job of the player (what it writes over an enemy player in PvP), not a real name.
    private sealed record GameTexts(string Name, string Title, bool ShowTitle, bool TitleAbove, bool JobOnly = false);

    private readonly Dictionary<ulong, GameTexts> gameTexts = new();
    private readonly Dictionary<ulong, int> gameFlags = new();
    private readonly Dictionary<ulong, (uint Text, uint Edge)> gameColors = new();
    private readonly Dictionary<ulong, int> gameNameIcons = new();
    private readonly NativeWindowHider hider = new();
    private readonly Dictionary<ulong, int> gameMarkerIcons = new();
    private readonly object gameFlagsLock = new();

    private readonly Plugin plugin;

    // The party members who are watching a cutscene this frame (their plates get the cutscene icon).
    private readonly HashSet<uint> cutsceneMembers = new();
    // The job of every member of the party (entity id -> job id), for the class icon of a plate that the game draws nothing for.
    private readonly Dictionary<uint, uint> partyJobs = new();

    // The key of the made-up plate of the preview: no real object has it, so nothing the game told us is mixed in.
    private const ulong PreviewKey = ulong.MaxValue - 1;

    /// <summary>Set by the settings page every frame a tab of a kind of plate is open: one made-up plate of that kind is drawn at a fixed place.</summary>
    public NamePlateRelation? PreviewKind { get; set; }

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
                    else if (gameMarkerIcons.TryGetValue(gameObject.GameObjectId, out var known) && known != 0 && NativeMarkerIcon(gameObject) == 0)
                    {
                        // The game cleared the mark on the object (a quest was turned in) without refreshing the plate: our copy would
                        // keep painting it forever, so it is dropped here.
                        gameMarkerIcons[gameObject.GameObjectId] = 0;
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
                    var rawName = handler.Name.TextValue;
                    var texts = new GameTexts(
                        rawName,
                        CleanTitle(handler.Title.TextValue),
                        handler.DisplayTitle,
                        handler.IsPrefixTitle,
                        IsJobName(StripUndrawable(rawName), owner));

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

    // The icon the game itself keeps on the object (it is not the plate's copy, so our own zero never touches it).
    private static unsafe uint NativeMarkerIcon(Dalamud.Game.ClientState.Objects.Types.IGameObject gameObject)
    {
        if (gameObject.Address == IntPtr.Zero)
            return 0;

        return ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)gameObject.Address)->NamePlateIconId;
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
        var scale = UiScale.Overlay;

        // Only the kinds of plate that are switched on.
        var plates = snapshot.NamePlates.Where(p => settings.StyleOf(p.Relation).Active).ToList();

        cutsceneMembers.Clear();
        partyJobs.Clear();
        foreach (var member in snapshot.Party)
        {
            partyJobs[member.EntityId] = member.JobId;
            if (member.Presence == Presence.ViewingCutscene)
                cutsceneMembers.Add(member.EntityId);
        }

        DrawPreview(drawList, scale, settings, snapshot);

        // The plates overlap like the game's own; the target is drawn last, so it is over the rest.
        foreach (var plate in plates.OrderBy(p => p.IsTarget))
        {
            // The plates are part of the world, like the game's own: windows of the game never cut them.
            // A plate of an enemy, a party member or a friendly fighter that is out of reach (the game's own range and sight check) is as faded as in the
            // frames. Other players, NPCs and objects are left alone (a city full of players would mean a check for every one of them).
            var fadeMark = DrawListFade.Mark(drawList);
            DrawPlate(drawList, scale, settings, settings.StyleOf(plate.Relation), plate, plate.IsTarget, snapshot.Player?.Level ?? 0);

            if (plugin.Configuration.HudBars.FadeNameplates && plate.Relation is NamePlateRelation.Enemy or NamePlateRelation.EnemyPlayer or NamePlateRelation.Party or NamePlateRelation.Friendly &&
                ReachCheck.IsOutOfReach(plate.EntityId, plate.Relation is NamePlateRelation.Enemy or NamePlateRelation.EnemyPlayer))
            {
                DrawListFade.FadeSince(drawList, fadeMark, Fade.Alpha);
            }
        }
    }

    // One made-up plate of the kind whose tab is open, over a fixed place of the screen (a bit above the middle), with every piece it can have
    // (name, title, level, bar, cast, status icons). Never during combat, and it always carries the "PREVIEW" mark.
    private void DrawPreview(ImDrawListPtr drawList, float scale, NamePlateSettings settings, GameSnapshot snapshot)
    {
        var kind = PreviewKind;
        PreviewKind = null;

        if (kind is null || snapshot.Context.InCombat)
            return;

        var style = settings.StyleOf(kind.Value);
        if (!style.Active)
            return;

        var (name, level) = kind.Value switch
        {
            NamePlateRelation.Self => ("Nahja Stark", 100),
            NamePlateRelation.Party => ("Aldric", 100),
            NamePlateRelation.OtherPlayer => ("Selene", 100),
            NamePlateRelation.Friendly => ("Allied Soldier", 90),
            NamePlateRelation.Enemy => ("Gurangatch", 92),
            NamePlateRelation.EnemyPlayer => ("Dragoon", 100),
            _ => ("Aetheryte", 1)
        };

        var withCast = kind.Value != NamePlateRelation.Other;
        var plate = new NamePlateSnapshot(0, name, level, 620000, 1000000, new Vector2(0f, 0.1f), 1f,
            withCast ? new CastSnapshot(7, 1.2f, 3f, true) : null, kind.Value, PreviewKey, false, withCast ? StatusRowRenderer.PreviewEnfeeblements : null);

        var size = ImGuiHelpers.MainViewport.Size;
        var anchor = new Vector2((plate.Ndc.X + 1f) * 0.5f * size.X, (1f - plate.Ndc.Y) * 0.5f * size.Y);
        var plateSize = new Vector2(style.WidthPx * scale, style.HeightPx * scale);

        DrawPlate(drawList, scale, settings, style, plate, false, snapshot.Player?.Level ?? 0);
        PreviewBadge.Draw(drawList, new Vector2(anchor.X - plateSize.X / 2f, anchor.Y - 46f * scale));
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
        else if (plate.Relation == NamePlateRelation.EnemyPlayer)
        {
            // In PvP the game writes only the job over an enemy player: nothing to hide, so streamer mode leaves it alone.
            // A real name (Crystalline Conflict) is not a job, so it keeps its invented name.
            GameTexts? enemyTexts;
            lock (gameFlagsLock)
                gameTexts.TryGetValue(plate.Key, out enemyTexts);

            if (enemyTexts is { JobOnly: true })
                displayName = enemyTexts.Name;
        }

        // The made-up plate of the preview: players get a title, so the title can be placed.
        var isPreview = plate.Key == PreviewKey;
        if (isPreview && plate.Relation is NamePlateRelation.Self or NamePlateRelation.Party or NamePlateRelation.OtherPlayer)
            texts = new GameTexts(plate.Name, "Hero", true, false);

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
        // Your own plate has no bar while you are dead (it would only be an empty bar over your body). The made-up plate of the preview keeps it.
        var ownDead = plate.Relation == NamePlateRelation.Self && !isPreview && plate.MaxHp > 0 && plate.Hp == 0;
        var drawBar = hasHp && !ownDead && (flags & GameDrawsGauge) != 0;
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
                BarGradient.Fill(drawList, min, max, ImGui.GetColorU32(gameBarColor ?? FallbackBarColor(plate.Relation)), rounding, ImDrawFlags.None, min.X, fillRight);
                drawList.PopClipRect();
            }

            BorderStyle.BarRect(drawList, min, max, ImGui.GetColorU32(style.BarBorderColor), rounding, ImDrawFlags.None, 1f);

            if (isTarget)
            {
                // A line is centered on its rectangle: the rectangle is pushed out by half the line (whole pixels) so the ring starts
                // exactly where the bar ends (no gap of screen showing between the bar and the ring).
                var ringWidth = MathF.Max(2f, MathF.Round(2f * scale));
                var ring = new Vector2(ringWidth / 2f, ringWidth / 2f);
                drawList.AddRect(min - ring, max + ring, ImGui.GetColorU32(plugin.Configuration.TargetRingColor), rounding + ringWidth / 2f, ImDrawFlags.None, ringWidth);
            }
        }

        // A player of your party who is watching a cutscene: the game's own icon of that status, on a point of the HP bar.
        // The preview of the Party tab always shows it, so it can be placed. While the plate has no bar (the player is not targeted) the icon goes
        // centered over his head instead (see below), and it comes back to this place when the bar shows.
        var showsCutsceneIcon = style.ShowCutsceneIcon && GameDataProvider.CutsceneIconId != 0 && plate.Relation == NamePlateRelation.Party
                                && (isPreview || cutsceneMembers.Contains(plate.EntityId));
        if (showsCutsceneIcon && drawBar)
        {
            var cutsceneSide = MathF.Max(MathF.Round(style.CutsceneIconSizePx * scale), 4f);
            var cutsceneAttach = min + (max - min) * style.CutsceneIconBarAnchor.Fraction() + new Vector2(style.CutsceneIconOffsetX, style.CutsceneIconOffsetY) * scale;
            var cutsceneMin = cutsceneAttach - new Vector2(cutsceneSide, cutsceneSide) * style.CutsceneIconSelfAnchor.Fraction();
            cutsceneMin = new Vector2(MathF.Round(cutsceneMin.X), MathF.Round(cutsceneMin.Y));
            var cutsceneTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(GameDataProvider.CutsceneIconId)).GetWrapOrEmpty();
            drawList.AddImage(cutsceneTexture.Handle, cutsceneMin, cutsceneMin + new Vector2(cutsceneSide, cutsceneSide), new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);
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

        // A party member whose name the game does not write: his class icon is always there, centered over the head when the game draws nothing, and
        // centered over the HP bar when it draws only the bar (below 100 %). When the name shows, the icon next to the name is the class icon, so
        // this one is not drawn (never twice). It is the icon the game puts next to the name when it has one, else the icon of his job.
        if (plate.Relation == NamePlateRelation.Party && style.ShowClassIconAlone && !drawName)
        {
            var classIconId = nameIconId > 0 ? (uint)nameIconId : partyJobs.TryGetValue(plate.EntityId, out var partyJob) && partyJob != 0 ? 62100u + partyJob : 0u;
            if (classIconId != 0)
            {
                var classSide = MathF.Max(MathF.Round(style.NameIconSizePx * scale), 4f);
                var classMin = new Vector2(MathF.Round(markerCenterX - classSide / 2f), MathF.Round(markerTop - classSide - MathF.Round(2f * scale)));
                var classTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(classIconId)).GetWrapOrEmpty();
                drawList.AddImage(classTexture.Handle, classMin, classMin + new Vector2(classSide, classSide), new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);

                // What goes over the head or the bar (the cutscene icon of a plate with no bar, the big marker) goes above this icon.
                markerTop = classMin.Y;
            }
        }

        // The cutscene icon of a plate with no bar: centered over the head (over the name when it shows), a little above it.
        if (showsCutsceneIcon && !drawBar)
        {
            var cutsceneSide = MathF.Max(MathF.Round(style.CutsceneIconSizePx * scale), 4f);
            var cutsceneGap = MathF.Round(2f * scale);
            var cutsceneMin = new Vector2(MathF.Round(markerCenterX - cutsceneSide / 2f), MathF.Round(markerTop - cutsceneSide - cutsceneGap));
            var cutsceneTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(GameDataProvider.CutsceneIconId)).GetWrapOrEmpty();
            drawList.AddImage(cutsceneTexture.Handle, cutsceneMin, cutsceneMin + new Vector2(cutsceneSide, cutsceneSide), new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);

            // The big marker (if the game has one) goes above this icon.
            markerTop = cutsceneMin.Y;
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
            // The preview shows both rows (enhancements and enfeeblements), whatever the filters say.
            var enhancements = isPreview ? StatusRowRenderer.PreviewEnhancements : statuses;
            plugin.StatusRows.DrawRows(drawList, style.Enhancements, style.Enfeeblements, enhancements, statuses, isPreview, playerId, min, barSize, "nameplate", !plate.Relation.IsEnemy());
        }
    }

    // The game wraps the title in its own quote symbols (special characters our font does not have, they showed as "?").
    // Everything the font cannot draw is dropped and the title gets plain quotes.
    private static string CleanTitle(string raw)
    {
        var text = StripUndrawable(raw);
        return text.Length == 0 ? string.Empty : $"\u00AB{text}\u00BB";
    }

    // Is this text the name of the job of the player? (the game's own text, in the language of the game)
    private static bool IsJobName(string text, Dalamud.Game.ClientState.Objects.Types.IGameObject? owner)
    {
        if (text.Length == 0 || owner is not Dalamud.Game.ClientState.Objects.Types.IBattleChara chara)
            return false;

        if (!Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().TryGetRow(chara.ClassJob.RowId, out var row))
            return false;

        var job = row.Name.ToString();
        return !string.IsNullOrWhiteSpace(job) && text.Contains(job, StringComparison.OrdinalIgnoreCase);
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
            BarGradient.Fill(drawList, min, max, ImGui.GetColorU32(color), rounding, ImDrawFlags.None, min.X, fillRight);
            drawList.PopClipRect();
        }

        BorderStyle.BarRect(drawList, min, max, ImGui.GetColorU32(settings.ProgressBarBorderColor), rounding, ImDrawFlags.None, 1f);

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
