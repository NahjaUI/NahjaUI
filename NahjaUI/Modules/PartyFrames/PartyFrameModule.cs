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
using NahjaUI.Modules.Auras;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI.Modules.PartyFrames;

/// <summary>
/// Draws the party frames: one life bar per member, one under the other, with the name and the life text, a shield, a
/// stripe for the role, a mana bar for the members you choose, and a ring and an arrow on the member you have targeted.
/// It also hides the game's own party list. It only reads the game snapshot.
/// Buffs and debuffs next to the bars come in a later step.
/// </summary>
internal sealed class PartyFrameModule : IDisposable
{
    // The game's own party list, found in game with the Debug page.
    private const string NativePartyList = "_PartyList";

    private static readonly string[] NativeLists = { NativePartyList };

    // Class job ids of the roles and of the jobs that can raise a dead member.
    private static readonly uint[] TankJobs = { 1, 19, 3, 21, 32, 37 };
    private static readonly uint[] HealerJobs = { 6, 24, 26, 28, 33, 40 };
    private static readonly uint[] RaiserJobs = { 27, 35 };

    private static readonly Vector4 TankColor = new(0.31f, 0.55f, 0.88f, 1f);
    private static readonly Vector4 HealerColor = new(0.31f, 0.75f, 0.45f, 1f);
    private static readonly Vector4 DamageColor = new(0.85f, 0.33f, 0.31f, 1f);
    private static readonly Vector4 ChocoboColor = new(0.88f, 0.75f, 0.30f, 1f);

    // What is shown in the preview when there is no real party: a full party of eight, with a shield, mana and a dead member.
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

    private readonly Plugin plugin;

    public PartyFrameModule(Plugin plugin)
    {
        this.plugin = plugin;

        // The box of the first bar. The others follow it downwards.
        PartyElement = plugin.Layout.Register("unitframe.party", "Party frames", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.TopLeft,
            SelfAnchor = AnchorPoint.TopLeft,
            OffsetX = 60,
            OffsetY = 300,
            Width = 216,
            Height = 34
        });

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
    }

    public LayoutElement PartyElement { get; }

    /// <summary>Set by the settings page every frame the Party tab is open: sample members are shown when there is no party.</summary>
    public bool PreviewRequested { get; set; }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeLists, OnNativePreDraw);
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
    private static List<UnitSnapshot> BuildMembers(GameSnapshot snapshot, PartyFrameSettings settings)
    {
        var party = snapshot.Party;
        var result = new List<UnitSnapshot>(party.Count + 1);

        var playerId = snapshot.Player?.EntityId;

        // Alone, and the game is set to show its list alone: you are the only member.
        if (party.Count == 0 && snapshot.Context.PartyListWhenSolo && snapshot.Player is not null)
        {
            result.Add(snapshot.Player);

            if (settings.ShowChocobo && snapshot.Chocobo is not null)
                result.Add(snapshot.Chocobo);

            return result;
        }

        if (settings.Order == PartyOrder.MeFirst && playerId is not null)
        {
            foreach (var member in party)
            {
                if (member.EntityId == playerId)
                    result.Add(member);
            }

            foreach (var member in party)
            {
                if (member.EntityId != playerId)
                    result.Add(member);
            }
        }
        else
        {
            result.AddRange(party);
        }

        // Like the game, the chocobo only shows while you are in a party.
        if (result.Count > 0 && settings.ShowChocobo && snapshot.Chocobo is not null)
            result.Add(snapshot.Chocobo);

        return result;
    }

    private static UnitSnapshot Sample(string name, uint job, uint hp, uint mp, uint maxMp, byte shield) =>
        new(0, name, job, 100, hp, 100000, mp, maxMp, hp == 0, null, Array.Empty<StatusSnapshot>(), true, shield);

    // ---------------------------------------------------------------- hiding the game's list

    private unsafe void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        var look = plugin.Configuration.UnitFrames.Party.Look;
        if (!look.Enabled || !look.HideNative)
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

        var settings = plugin.Configuration.UnitFrames.Party;
        if (!settings.Look.Enabled)
            return;

        IReadOnlyList<UnitSnapshot> members = BuildMembers(snapshot, settings);
        var isSample = false;

        if (members.Count == 0)
        {
            if (!preview)
            {
                PartyElement.DisplayRect = null;
                return;
            }

            members = settings.ShowChocobo ? SamplePartyWithChocobo : SampleParty;
            isSample = true;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(PartyElement.Layout);
        var step = size.Y + MathF.Round(settings.RowSpacingPx * scale);

        var targetId = snapshot.Target?.EntityId;
        var drawList = ImGui.GetBackgroundDrawList();

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var rowPos = new Vector2(pos.X, pos.Y + i * step);

            var selected = isSample ? i == PreviewSelectedIndex : targetId is not null && member.EntityId == targetId;

            var index = i;

            // Drawn around the game's own right click menu, never over it.
            plugin.GameCover.Draw(rowPos, size, 400f * scale, () =>
            {
                DrawMember(drawList, settings, member, rowPos, size, selected);

                // The rows of buffs and debuffs next to the bar.
                var playerId = snapshot.Player?.EntityId ?? 0u;

                IReadOnlyList<StatusSnapshot> buffs = isSample
                    ? new ArraySegment<StatusSnapshot>(StatusRowRenderer.PreviewBuffs, 0, Math.Min(StatusRowRenderer.PreviewBuffs.Length, 2 + index % 4))
                    : member.Statuses;

                IReadOnlyList<StatusSnapshot> debuffs = isSample
                    ? new ArraySegment<StatusSnapshot>(StatusRowRenderer.PreviewDebuffs, 0, index % 3 + 1)
                    : member.Statuses;

                plugin.StatusRows.DrawRow(drawList, settings.Buffs, true, buffs, isSample, playerId, rowPos, size, "party");
                plugin.StatusRows.DrawRow(drawList, settings.Debuffs, false, debuffs, isSample, playerId, rowPos, size, "party");
            });

            // Clicks only for real members, and not while the boxes are being moved.
            if (!isSample && settings.Look.Clickable && !plugin.Layout.UnlockMode)
                plugin.UnitFrameSystem.HandleClicks($"party.{member.EntityId}", rowPos, size, member);
        }

        // In unlock mode the box is the whole stack of bars.
        PartyElement.DisplayRect = (pos, new Vector2(size.X, (members.Count - 1) * step + size.Y));
    }

    // The game's own job icons: 62000 + job id (plain) or 62100 + job id (light). Crafters and gatherers (jobs 8-18) have them too.
    private static uint JobIconId(uint jobId, PartyJobIconStyle style) =>
        (style == PartyJobIconStyle.Light ? 62100u : 62000u) + jobId;

    private void DrawMember(ImDrawListPtr drawList, PartyFrameSettings settings, UnitSnapshot member, Vector2 pos, Vector2 size, bool selected)
    {
        var look = settings.Look;
        var scale = ImGuiHelpers.GlobalScale;
        var max = pos + size;

        var rounding = MathF.Round(look.RoundingPx * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        var fraction = member.MaxHp == 0 ? 0f : Math.Clamp(member.Hp / (float)member.MaxHp, 0f, 1f);
        var fillColor = plugin.UnitFrameSystem.LifeColor(look, member, true);

        // Background, then the life. The right end of the fill is straight, except when the life is full.
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(look.BackgroundColor), rounding);

        var fillRight = MathF.Round(pos.X + size.X * fraction);
        if (fillRight > pos.X)
            FillRounded(drawList, pos, max, fillRight, ImGui.GetColorU32(fillColor), rounding, ImDrawFlags.RoundCornersAll);

        if (look.ShowShield && member.ShieldPercent > 0)
            UnitFrameModule.DrawShield(drawList, look, pos, size, fraction, rounding, member.ShieldPercent);

        // The job icon of the member (the game's own icon, so every job has one). It takes the place of the role stripe.
        var textPos = pos;
        var textSize = size;
        var hasJobIcon = settings.ShowJobIcon && !member.IsCompanion && member.JobId != 0;

        if (hasJobIcon)
        {
            var inset = MathF.Max(1f, MathF.Round(2f * scale));
            var iconSide = MathF.Min(MathF.Round(settings.JobIconSizePx * scale), size.Y - inset * 2f);
            iconSide = MathF.Max(iconSide, 4f);
            var iconMin = new Vector2(pos.X + inset + 1f, MathF.Round(pos.Y + (size.Y - iconSide) / 2f));
            var iconId = JobIconId(member.JobId, settings.JobIconStyle);
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();

            drawList.AddImage(texture.Handle, iconMin, iconMin + new Vector2(iconSide, iconSide), new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);

            // The name moves right so it does not sit over the icon.
            var shift = iconMin.X + iconSide + inset - pos.X;
            textPos = new Vector2(pos.X + shift, pos.Y);
            textSize = new Vector2(size.X - shift, size.Y);
        }

        if (settings.RoleStripe && !hasJobIcon)
        {
            // The stripe is the left part of the bar's own rounded shape, so its corners follow the bar exactly.
            var stripeRight = pos.X + MathF.Max(2f, MathF.Round(4f * scale));
            FillRounded(drawList, pos, max, stripeRight, ImGui.GetColorU32(RoleColor(member)), rounding, ImDrawFlags.RoundCornersAll);
        }

        if (!member.IsCompanion && ShowsMana(settings.ManaMode, member) && member.MaxMp > 0)
        {
            var height = MathF.Max(2f, MathF.Round(look.ManaHeightPx * scale));
            var top = max.Y - height;
            var manaFraction = Math.Clamp(member.Mp / (float)member.MaxMp, 0f, 1f);

            // The strip sits inside the bar, along its bottom edge.
            drawList.AddRectFilled(new Vector2(pos.X, top), max, ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.05f, 0.9f)), rounding, ImDrawFlags.RoundCornersBottom);

            var manaRight = MathF.Round(pos.X + size.X * manaFraction);
            if (manaRight > pos.X)
                FillRounded(drawList, new Vector2(pos.X, top), max, manaRight, ImGui.GetColorU32(look.ManaColor), rounding, ImDrawFlags.RoundCornersBottom);

            // A thin dark line between the life and the mana.
            drawList.AddLine(new Vector2(pos.X, top), new Vector2(max.X, top), ImGui.GetColorU32(look.BorderColor), thickness);
        }

        // A dead member is darkened.
        if (member.IsDead || member.Hp == 0)
            drawList.AddRectFilled(pos, max, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)), rounding);

        drawList.AddRect(pos, max, ImGui.GetColorU32(look.BorderColor), rounding, ImDrawFlags.None, thickness);

        // The texts last, over everything.
        var name = UnitTextFormatter.FormatName(member.Name, look.NameFormat, true, look.NameFormatOnlyPlayers);
        plugin.UnitFrameSystem.DrawText(drawList, name, look.Name, textPos, textSize);

        var health = UnitTextFormatter.FormatHealth(look, member.Hp, member.MaxHp);
        plugin.UnitFrameSystem.DrawText(drawList, health, look.Health, pos, size);

        if (!selected)
            return;

        var color = ImGui.GetColorU32(settings.SelectionColor);

        if (settings.SelectionRing)
        {
            var grow = new Vector2(thickness * 2f, thickness * 2f);
            drawList.AddRect(pos - grow, max + grow, color, rounding + thickness * 2f, ImDrawFlags.None, thickness * 2f);
        }

        if (settings.SelectionArrow)
        {
            // A small triangle pointing at the bar, just outside its left edge.
            var middle = pos.Y + size.Y / 2f;
            var tip = pos.X - MathF.Round(6f * scale);
            var back = tip - MathF.Round(8f * scale);
            var half = MathF.Round(6f * scale);

            drawList.AddTriangleFilled(new Vector2(back, middle - half), new Vector2(tip, middle), new Vector2(back, middle + half), color);
        }
    }

    // ---------------------------------------------------------------- helpers

    // Fills a rounded box only up to <clipRight>. The box keeps its full rounded shape and is cut by a clip rectangle, so a
    // narrow fill (the stripe, a low life) still follows the corners of the bar instead of sticking out of them.
    private static void FillRounded(ImDrawListPtr drawList, Vector2 min, Vector2 max, float clipRight, uint color, float rounding, ImDrawFlags flags)
    {
        drawList.PushClipRect(min, new Vector2(MathF.Min(clipRight, max.X), max.Y), true);
        drawList.AddRectFilled(min, max, color, rounding, flags);
        drawList.PopClipRect();
    }

    private static bool ShowsMana(PartyManaMode mode, UnitSnapshot member) => mode switch
    {
        PartyManaMode.Off => false,
        PartyManaMode.Healers => Array.IndexOf(HealerJobs, member.JobId) >= 0,
        PartyManaMode.HealersAndRaisers => Array.IndexOf(HealerJobs, member.JobId) >= 0 || Array.IndexOf(RaiserJobs, member.JobId) >= 0,
        _ => true
    };

    private static Vector4 RoleColor(UnitSnapshot member)
    {
        // The chocobo has no role: a neutral gold stripe.
        if (member.IsCompanion)
            return ChocoboColor;

        if (Array.IndexOf(TankJobs, member.JobId) >= 0)
            return TankColor;

        return Array.IndexOf(HealerJobs, member.JobId) >= 0 ? HealerColor : DamageColor;
    }
}
