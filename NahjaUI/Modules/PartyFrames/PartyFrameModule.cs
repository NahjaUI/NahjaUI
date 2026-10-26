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

    // Statuses shown in the preview (real status ids, so they have real icons). The filters do not apply to them.
    private static readonly StatusSnapshot[] PreviewBuffs =
    {
        new(160, 0, 14f, 0), new(1191, 0, 8f, 0), new(50, 0, 22f, 0),
        new(160, 0, 5f, 0), new(1191, 0, 31f, 0), new(50, 0, 12f, 0)
    };

    private static readonly StatusSnapshot[] PreviewDebuffs =
    {
        new(638, 0, 12f, 0), new(43, 0, 9f, 0), new(144, 0, 21f, 0)
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

            DrawMember(drawList, settings, member, rowPos, size, selected);

            // The rows of buffs and debuffs next to the bar.
            var playerId = snapshot.Player?.EntityId ?? 0u;

            IReadOnlyList<StatusSnapshot> buffs = isSample
                ? new ArraySegment<StatusSnapshot>(PreviewBuffs, 0, Math.Min(PreviewBuffs.Length, 2 + i % 4))
                : member.Statuses;

            IReadOnlyList<StatusSnapshot> debuffs = isSample
                ? new ArraySegment<StatusSnapshot>(PreviewDebuffs, 0, i % 3 + 1)
                : member.Statuses;

            DrawStatusRow(drawList, settings.Buffs, true, buffs, isSample, playerId, rowPos, size);
            DrawStatusRow(drawList, settings.Debuffs, false, debuffs, isSample, playerId, rowPos, size);

            // Clicks only for real members, and not while the boxes are being moved.
            if (!isSample && settings.Look.Clickable && !plugin.Layout.UnlockMode)
                plugin.UnitFrameSystem.HandleClicks($"party.{member.EntityId}", rowPos, size, member);
        }

        // In unlock mode the box is the whole stack of bars.
        PartyElement.DisplayRect = (pos, new Vector2(size.X, (members.Count - 1) * step + size.Y));
    }

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

        if (settings.RoleStripe)
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
        plugin.UnitFrameSystem.DrawText(drawList, name, look.Name, pos, size);

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

    // ---------------------------------------------------------------- buffs and debuffs

    // One row of icons next to a bar. The first icon is attached to a point of the bar (plus the offset) by one of its own
    // points, and the next ones follow in the direction of the growth.
    private void DrawStatusRow(ImDrawListPtr drawList, PartyAuraSettings row, bool isBuff, IReadOnlyList<StatusSnapshot> statuses, bool isSample, uint playerId, Vector2 barPos, Vector2 barSize)
    {
        if (!row.Enabled || row.MaxIcons <= 0 || statuses.Count == 0)
            return;

        var scale = ImGuiHelpers.GlobalScale;

        var iconSize = new Vector2(MathF.Round(Math.Max(4, row.IconWidth) * scale), MathF.Round(Math.Max(4, row.IconHeight) * scale));
        var spacing = MathF.Round(row.SpacingPx * scale);

        var attach = barPos + barSize * row.BarAnchor.Fraction() + new Vector2(row.OffsetX, row.OffsetY) * scale;
        var first = attach - iconSize * row.IconAnchor.Fraction();
        first = new Vector2(MathF.Round(first.X), MathF.Round(first.Y));

        var step = row.Growth switch
        {
            PartyAuraGrowth.Left => new Vector2(-(iconSize.X + spacing), 0f),
            PartyAuraGrowth.Right => new Vector2(iconSize.X + spacing, 0f),
            PartyAuraGrowth.Up => new Vector2(0f, -(iconSize.Y + spacing)),
            _ => new Vector2(0f, iconSize.Y + spacing)
        };

        var shown = 0;
        foreach (var status in statuses)
        {
            if (shown >= row.MaxIcons)
                break;

            if (!Accepts(row, isBuff, status, isSample, playerId))
                continue;

            DrawStatusIcon(drawList, row, status, first + step * shown, iconSize);
            shown++;
        }
    }

    // Does this row show this status?
    private static bool Accepts(PartyAuraSettings row, bool isBuff, StatusSnapshot status, bool isSample, uint playerId)
    {
        if (isSample)
            return true;

        // A status without an icon cannot be drawn.
        if (GameNames.StatusIconId(status.Id) == 0)
            return false;

        if (row.OnlyMine && status.SourceId != playerId)
            return false;

        var listed = row.StatusIds.Contains(status.Id);

        switch (row.Filter)
        {
            case PartyAuraFilter.OnlyListed:
                return listed;

            case PartyAuraFilter.AllExceptListed:
                if (listed)
                    return false;
                break;
        }

        // Every buff, or every debuff: the game says which of the two a status is.
        return GameNames.StatusCategory(status.Id) == (isBuff ? 1 : 2);
    }

    // One icon: cropped (never stretched) to the shape of the box, with a 1 px black border, the time and the stacks.
    private void DrawStatusIcon(ImDrawListPtr drawList, PartyAuraSettings row, StatusSnapshot status, Vector2 pos, Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var iconId = GameNames.StatusIconId(status.Id);

        // Rounded corners, like the bars. They can never be more than half of the shorter side.
        var rounding = MathF.Min(MathF.Round(Math.Clamp(row.CornerRadiusPx, 0, 24) * scale), MathF.Min(size.X, size.Y) / 2f);

        if (iconId != 0)
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            var crop = Math.Clamp(row.CropPercent, 0, 30) / 100f;
            var (uv0, uv1) = AuraEngine.IconUv(size, AuraEngine.StatusIconAspect, crop);

            drawList.AddImageRounded(texture.Handle, pos, pos + size, uv0, uv1, ImGui.GetColorU32(Vector4.One), rounding, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.18f, 0.9f)), rounding);
        }

        var thickness = MathF.Max(1f, MathF.Round(scale));
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f)), rounding, ImDrawFlags.None, thickness);

        // The text size is in logical pixels (the fonts are already built scaled).
        var timerPx = Math.Clamp(row.TimerSize, 8, 64);
        var stackPx = row.StackTextSize > 0 ? Math.Clamp(row.StackTextSize, 8, 64) : timerPx * 0.75f;

        if (row.ShowTimer && status.RemainingTime > 0f)
            plugin.AuraSystem.DrawOverlayText(drawList, AuraEngine.FormatTime(status.RemainingTime), timerPx, row.Font,
                pos, size, row.TimerAnchor, new Vector2(row.TimerOffsetX, row.TimerOffsetY));

        // Only statuses that really stack show a number.
        var maxStacks = GameNames.MaxStacks(status.Id);
        if (row.ShowStacks && maxStacks > 1 && status.Param > 1 && status.Param <= maxStacks)
            plugin.AuraSystem.DrawOverlayText(drawList, status.Param.ToString(), stackPx, row.Font,
                pos, size, row.StacksAnchor, new Vector2(row.StacksOffsetX, row.StacksOffsetY));
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
