using System;
using System.Collections.Generic;
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
using NahjaUI.Modules.HudBars;

namespace NahjaUI.Modules.PartyListBar;

/// <summary>
/// Draws the party list: one HP bar per member, one under the other, with the name and the HP text, a shield, a
/// stripe for the role, a MP bar for the members you choose, and a ring and an arrow on the member you have targeted.
/// It also hides the game's own party list. It only reads the game snapshot.
/// Enhancements and enfeeblements next to the bars come in a later step.
/// </summary>
internal sealed class PartyListModule : IDisposable
{
    // The game's own party list.
    private const string NativePartyList = "_PartyList";

    private static readonly string[] NativeLists = { NativePartyList };

    // Class job ids of the roles and of the jobs that can raise a dead member.
    private static readonly uint[] TankJobs = { 1, 19, 3, 21, 32, 37 };
    private static readonly uint[] HealerJobs = { 6, 24, 26, 28, 33, 40 };
    private static readonly uint[] RaiserJobs = { 27, 35 };


    // What is shown in the preview when there is no real party: a full party of eight, with a shield, MP and a dead member.
    private static readonly UnitSnapshot[] SampleParty =
    {
        Sample("Nahja Stark", 24, 100000, 10000, 10000, 0),
        Sample("Aldric", 19, 82000, 10000, 10000, 12),
        Sample("Brogan", 21, 64000, 10000, 10000, 0),
        Sample("Selene", 28, 100000, 3800, 10000, 18),
        Sample("Kaito", 20, 41000, 10000, 10000, 0),
        Sample("Rhea", 35, 100000, 7100, 10000, 0),
        Sample("Tobias", 23, 77000, 10000, 10000, 0),
        Sample("Vera", 25, 0, 10000, 10000, 0)
    };

    // In the preview the third member is shown as the one you have targeted, so the ring and the arrow can be seen.
    private const int PreviewSelectedIndex = 2;

    // The box that unlock mode shows: a full party of 8 and the chocobo.
    private const int BoxSlots = 9;

    private readonly Plugin plugin;
    private readonly NativeWindowHider hider = new();

    public PartyListModule(Plugin plugin)
    {
        this.plugin = plugin;

        // The box of the list: its size is one row; the rows are placed one after another inside a box of 9 rows, from the top or (option) from the bottom.
        PartyElement = plugin.Layout.Register("hudbar.party", "Party list", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.TopLeft,
            SelfAnchor = AnchorPoint.TopLeft,
            OffsetX = 60,
            OffsetY = 300,
            Width = 216,
            Height = 34
        });

        // Shown in the panel of unlock mode, under X and Y.
        PartyElement.DrawOptions = () =>
        {
            var settings = plugin.Configuration.HudBars.Party;
            var fromBottom = settings.StartFromBottom;
            if (ImGui.Checkbox("Start drawing from the bottom", ref fromBottom))
            {
                settings.StartFromBottom = fromBottom;
                plugin.RequestSave();
            }
        };

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
    }

    public LayoutElement PartyElement { get; }

    /// <summary>Set by the settings page every frame the Party tab is open: sample members are shown when there is no party.</summary>
    public bool PreviewRequested { get; set; }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
        hider.ShowAll();
    }

    // The chocobo companion in the preview.
    private static readonly UnitSnapshot[] SamplePartyWithChocobo = WithSampleChocobo(SampleParty);

    private static UnitSnapshot[] WithSampleChocobo(UnitSnapshot[] party)
    {
        var all = new UnitSnapshot[party.Length + 1];
        Array.Copy(party, all, party.Length);
        all[^1] = Sample("Chocobo", 0, 60000, 0, 0, 0) with { IsCompanion = true };
        return all;
    }

    // The bars to draw: the party in the chosen order, then your chocobo if wanted.
    private static List<UnitSnapshot> BuildMembers(GameSnapshot snapshot, PartyListSettings settings)
    {
        var party = snapshot.Party;
        var result = new List<UnitSnapshot>(party.Count + 1);

        // Alone, and the game is set to show its list alone: you are the only member.
        if (party.Count == 0 && snapshot.Context.PartyListWhenSolo && snapshot.Player is not null)
        {
            result.Add(snapshot.Player);

            if (snapshot.Chocobo is not null)
                result.Add(snapshot.Chocobo);

            return result;
        }

        // The order is the game's own party list (so the macros <2>, <3>... keep pointing at the same members).
        result.AddRange(party);

        // Like the game, the chocobo only shows while you are in a party.
        if (result.Count > 0 && snapshot.Chocobo is not null)
            result.Add(snapshot.Chocobo);

        return result;
    }

    private static UnitSnapshot Sample(string name, uint job, uint hp, uint mp, uint maxMp, byte shield) =>
        new(0, name, job, 100, hp, 100000, mp, maxMp, hp == 0, null, Array.Empty<StatusSnapshot>(), true, shield);

    // ---------------------------------------------------------------- hiding the game's list

    private unsafe void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        var look = plugin.Configuration.HudBars.Party.Look;
        if (!look.Enabled)
            return;

        hider.Hide(args);
    }

    // ---------------------------------------------------------------- drawing

    public void Draw()
    {
        var hideLook = plugin.Configuration.HudBars.Party.Look;
        hider.ShowAgain(_ => hideLook.Enabled);
        var snapshot = plugin.GameData.Current;
        var preview = PreviewRequested || plugin.Layout.UnlockMode || plugin.Layout.IsPreviewing(PartyElement.Id);
        PreviewRequested = false;

        var settings = plugin.Configuration.HudBars.Party;
        if (!settings.Look.Enabled)
            return;

        if (!plugin.Layout.IsShown(PartyElement))
        {
            PartyElement.DisplayRect = null;
            return;
        }

        IReadOnlyList<UnitSnapshot> members = BuildMembers(snapshot, settings);
        var isSample = false;

        if (members.Count == 0)
        {
            if (!preview)
            {
                PartyElement.DisplayRect = null;
                return;
            }

            members = SamplePartyWithChocobo;
            isSample = true;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(PartyElement.Layout);

        // The MP bar is a bar of its own that can sit anywhere around the member's bar. A member that shows it takes the area of the bar and
        // the MP bar together, one that does not takes the bar alone: so the visible space between two members is always the same.
        var gap = MathF.Round(settings.RowSpacingPx * scale);
        var mpTop = 0f;
        var mpBottom = size.Y;
        if (settings.MpMode != PartyMpMode.Off)
        {
            var (mpPos, mpSize) = MpRect(settings, Vector2.Zero, size, scale);
            mpTop = MathF.Min(0f, mpPos.Y);
            mpBottom = MathF.Max(size.Y, mpPos.Y + mpSize.Y);
        }

        // The box of the list holds the 9 slots with the biggest extent, so it never changes size.
        var biggest = mpBottom - mpTop;
        var boxHeight = (BoxSlots - 1) * (biggest + gap) + biggest;

        // Top and height of the slot of each member.
        var tops = new float[members.Count];
        var heights = new float[members.Count];
        var total = 0f;
        for (var i = 0; i < members.Count; i++)
        {
            var withMp = settings.MpMode != PartyMpMode.Off && ShowsMp(settings.MpMode, members[i]);
            tops[i] = withMp ? mpTop : 0f;
            heights[i] = withMp ? mpBottom - mpTop : size.Y;
            total += heights[i] + (i > 0 ? gap : 0f);
        }

        var targetId = snapshot.Target?.EntityId;
        var drawList = ImGui.GetBackgroundDrawList();

        // The rows go one under the other from the top of the box; with "Start from the bottom" the whole stack sits at the bottom of it
        // (the empty space is on top). The order of the members does not change (macros <2>, <3>).
        var cursor = settings.StartFromBottom ? pos.Y + MathF.Max(0f, boxHeight - total) : pos.Y;

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var rowPos = new Vector2(pos.X, cursor - tops[i]);
            cursor += heights[i] + gap;

            var selected = isSample ? i == PreviewSelectedIndex : targetId is not null && member.EntityId == targetId;

            var index = i;

            // Drawn around the game's own right click menu, never over it.
            plugin.GameCover.Draw(rowPos, size, 400f * scale, () =>
            {
                DrawMember(drawList, settings, member, rowPos, size, selected);

                // The rows of enhancements and enfeeblements next to the bar.
                var playerId = snapshot.Player?.EntityId ?? 0u;

                IReadOnlyList<StatusSnapshot> enhancements = isSample
                    ? new ArraySegment<StatusSnapshot>(StatusRowRenderer.PreviewEnhancements, 0, Math.Min(StatusRowRenderer.PreviewEnhancements.Length, 2 + index % 4))
                    : member.Statuses;

                IReadOnlyList<StatusSnapshot> enfeeblements = isSample
                    ? new ArraySegment<StatusSnapshot>(StatusRowRenderer.PreviewEnfeeblements, 0, index % 3 + 1)
                    : member.Statuses;

                plugin.StatusRows.DrawRows(drawList, settings.Enhancements, settings.Enfeeblements, enhancements, enfeeblements, isSample, playerId, rowPos, size, "party");
            });

            // Clicks only for real members, and not while the boxes are being moved.
            if (!isSample && !plugin.Layout.UnlockMode)
            {
                plugin.HudBarSystem.HandleClicks($"party.{member.EntityId}", rowPos, size, member);

                // A member whose name is replaced by Dead, Offline, Other area... shows it in a small box while the mouse is over his bar.
                if (AwayTag(member) is not null && IsHovered(rowPos, size))
                    plugin.StatusRows.ShowTextTooltip(member.Name);
            }
        }

        // In unlock mode the box is the whole stack of bars.
        PartyElement.DisplayRect = (pos, new Vector2(size.X, boxHeight));
    }

    private bool IsHovered(Vector2 pos, Vector2 size)
    {
        if (plugin.GameCover.Contains(ImGui.GetIO().MousePos))
            return false;

        return ImGui.IsMouseHoveringRect(pos, pos + size, false) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow);
    }

    // The game's own job icons: 62000 + job id (plain) or 62100 + job id (light). Crafters and gatherers (jobs 8-18) have them too.
    private static uint JobIconId(uint jobId, PartyJobIconStyle style) =>
        (style == PartyJobIconStyle.Light ? 62100u : 62000u) + jobId;

    private void DrawMember(ImDrawListPtr drawList, PartyListSettings settings, UnitSnapshot member, Vector2 pos, Vector2 size, bool selected)
    {
        var fadeMark = DrawListFade.Mark(drawList);
        var look = settings.Look;
        var scale = ImGuiHelpers.GlobalScale;
        var max = pos + size;

        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        var fraction = member.MaxHp == 0 ? 0f : Math.Clamp(member.Hp / (float)member.MaxHp, 0f, 1f);
        var fillColor = plugin.HudBarSystem.HpColor(look, member, true);

        // Background, then the HP. The right end of the fill is straight, except when the HP is full.
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(look.BackgroundColor), rounding);

        var fillRight = MathF.Round(pos.X + size.X * fraction);
        if (fillRight > pos.X)
            FillRounded(drawList, pos, max, fillRight, ImGui.GetColorU32(fillColor), rounding, ImDrawFlags.RoundCornersAll);

        if (member.ShieldPercent > 0)
            HudBarModule.DrawShield(drawList, look, pos, size, fraction, rounding, member.ShieldPercent);

        // The job icon of the member (the game's own icon, so every job has one). The name does not care about it: its place is only
        // the anchor and the offsets chosen for it, as on the enmity list with its dot.
        var hasJobIcon = settings.ShowJobIcon && !member.IsCompanion && member.JobId != 0;

        if (hasJobIcon)
        {
            // The icon is the size chosen, even if it is taller than the bar.
            var iconSide = MathF.Max(MathF.Round(settings.JobIconSizePx * scale), 4f);
            var iconAttach = pos + size * settings.JobIconBarAnchor.Fraction() + new Vector2(settings.JobIconOffsetX, settings.JobIconOffsetY) * scale;
            var iconMin = iconAttach - new Vector2(iconSide, iconSide) * settings.JobIconSelfAnchor.Fraction();
            iconMin = new Vector2(MathF.Round(iconMin.X), MathF.Round(iconMin.Y));
            var iconId = JobIconId(member.JobId, settings.JobIconStyle);
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();

            drawList.AddImage(texture.Handle, iconMin, iconMin + new Vector2(iconSide, iconSide), new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);

        }

        drawList.AddRect(pos, max, ImGui.GetColorU32(look.BorderColor), rounding, ImDrawFlags.None, thickness);

        if (!member.IsCompanion && ShowsMp(settings.MpMode, member) && member.MaxMp > 0)
            DrawMpBar(drawList, settings, member, pos, size);

        // The ring of the member you have targeted. It is always on and its color is the one of the whole interface
        // (Appearance > Target ring color). A line is centered on its rectangle: pushing the rectangle out by half the line makes
        // the ring start exactly where the bar ends (no gap of screen between them).
        if (selected)
        {
            var ringWidth = thickness * 2f;
            var grow = new Vector2(ringWidth / 2f, ringWidth / 2f);
            drawList.AddRect(pos - grow, max + grow, ImGui.GetColorU32(plugin.Configuration.TargetRingColor), rounding + ringWidth / 2f, ImDrawFlags.None, ringWidth);
        }

        // The texts last, over everything (the ring too: a name at the very top of the bar must not be cut by it).
        // A member who is away (offline, in another area, too far) or dead is faded, and his name is replaced by what is happening.
        var away = AwayTag(member);
        var name = away is not null
            ? away
            : UnitTextFormatter.FormatName(member.Name, look.NameFormat, true, look.NameFormatOnlyPlayers);
        // The name is shortened with "..." when it would go out of the bar or run into the HP text.
        var hpText = UnitTextFormatter.FormatValue(look, member.Hp, member.MaxHp);
        var hpArea = plugin.HudBarSystem.MeasureText(hpText, look.Hp, pos, size);
        plugin.HudBarSystem.DrawText(drawList, name, look.Name, pos, size, fitInBox: true, avoid: hpArea);

        plugin.HudBarSystem.DrawText(drawList, hpText, look.Hp, pos, size);

        if (away is not null)
            DrawListFade.FadeSince(drawList, fadeMark, Fade.Alpha);
    }

    // ---------------------------------------------------------------- helpers

    // What keeps a member from being "here": offline, another area, too far, or dead. Null when he is fine.
    private static string? AwayTag(UnitSnapshot member) => member.Presence switch
    {
        Presence.Offline => "Offline",
        Presence.OtherArea => "Other area",
        Presence.OutOfRange => "Out of range",
        Presence.ViewingCutscene => "Viewing cutscene",
        _ => member.IsDead || member.Hp == 0 ? "Dead" : null
    };

    // Fills a rounded box only up to <clipRight>. The box keeps its full rounded shape and is cut by a clip rectangle, so a
    // narrow fill (the stripe, a low HP) still follows the corners of the bar instead of sticking out of them.
    private static void FillRounded(ImDrawListPtr drawList, Vector2 min, Vector2 max, float clipRight, uint color, float rounding, ImDrawFlags flags)
    {
        drawList.PushClipRect(min, new Vector2(MathF.Min(clipRight, max.X), max.Y), true);
        drawList.AddRectFilled(min, max, color, rounding, flags);
        drawList.PopClipRect();
    }

    private static bool ShowsMp(PartyMpMode mode, UnitSnapshot member) => mode switch
    {
        PartyMpMode.Off => false,
        PartyMpMode.Healers => Array.IndexOf(HealerJobs, member.JobId) >= 0,
        PartyMpMode.HealersAndRaisers => Array.IndexOf(HealerJobs, member.JobId) >= 0 || Array.IndexOf(RaiserJobs, member.JobId) >= 0,
        _ => true
    };

    // Where the MP bar goes: a point of the MP bar on a point of the member's bar, moved by the offsets (plus Y is up in the page, saved downwards).
    private static (Vector2 Pos, Vector2 Size) MpRect(PartyListSettings settings, Vector2 pos, Vector2 size, float scale)
    {
        var width = settings.MpWidthPx > 0 ? MathF.Round(settings.MpWidthPx * scale) : size.X;
        var height = MathF.Max(2f, MathF.Round(settings.Look.MpHeightPx * scale));
        var mpSize = new Vector2(width, height);

        var attach = pos + size * settings.MpBarAnchor.Fraction() + new Vector2(settings.MpOffsetX, settings.MpOffsetY) * scale;
        var mpPos = attach - mpSize * settings.MpSelfAnchor.Fraction();
        return (new Vector2(MathF.Round(mpPos.X), MathF.Round(mpPos.Y)), mpSize);
    }

    private static void DrawMpBar(ImDrawListPtr drawList, PartyListSettings settings, UnitSnapshot member, Vector2 pos, Vector2 size)
    {
        var look = settings.Look;
        var scale = ImGuiHelpers.GlobalScale;
        var (mpPos, mpSize) = MpRect(settings, pos, size, scale);
        var mpMax = mpPos + mpSize;

        var rounding = MathF.Min(MathF.Round(GlobalColors.Current.Rounding * scale), MathF.Min(mpSize.X, mpSize.Y) / 2f);
        var fraction = Math.Clamp(member.Mp / (float)member.MaxMp, 0f, 1f);

        drawList.AddRectFilled(mpPos, mpMax, ImGui.GetColorU32(look.MpBackgroundColor), rounding);

        var fillRight = MathF.Round(mpPos.X + mpSize.X * fraction);
        if (fillRight > mpPos.X)
            FillRounded(drawList, mpPos, mpMax, fillRight, ImGui.GetColorU32(look.MpColor), rounding, ImDrawFlags.RoundCornersAll);

        drawList.AddRect(mpPos, mpMax, ImGui.GetColorU32(look.BorderColor), rounding, ImDrawFlags.None, MathF.Max(1f, MathF.Round(scale)));
    }
}
