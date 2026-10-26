using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace NahjaUI.Game;

/// <summary>
/// The ONLY place where NahjaUI reads game data from Dalamud.
/// Once per frame it builds a <see cref="GameSnapshot"/>; modules read <see cref="Current"/> and nothing else.
/// If a game patch breaks something, this is the file to fix.
/// </summary>
internal sealed class GameDataProvider : IDisposable
{
    private long frame;
    private int consecutiveErrors;

    // Cooldowns are only read for the actions someone asked for.
    private readonly HashSet<uint> watchedActions = new();
    private static readonly IReadOnlyDictionary<uint, CooldownSnapshot> NoCooldowns = new Dictionary<uint, CooldownSnapshot>();

    /// <summary>The snapshot of the latest frame. Never null.</summary>
    public GameSnapshot Current { get; private set; } = GameSnapshot.Empty;

    /// <summary>How long building the last snapshot took, in milliseconds.</summary>
    public double LastBuildMs { get; private set; }

    /// <summary>Message of the last error while building the snapshot, or null if all is well.</summary>
    public string? LastError { get; private set; }

    public GameDataProvider()
    {
        Plugin.Framework.Update += OnUpdate;
    }

    /// <summary>
    /// Says which actions to read cooldowns for. Call it every frame with the actions that are needed right now;
    /// the cooldowns then appear in the next snapshot.
    /// </summary>
    public void SetWatchedActions(IEnumerable<uint> actionIds)
    {
        watchedActions.Clear();

        foreach (var id in actionIds)
            watchedActions.Add(id);
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        var start = Stopwatch.GetTimestamp();

        try
        {
            Current = Build();
            consecutiveErrors = 0;
            LastError = null;
        }
        catch (Exception ex)
        {
            // One failing frame must never break the plugin: show an empty snapshot and log the first error only.
            Current = GameSnapshot.Empty with { Frame = frame };
            LastError = ex.Message;

            consecutiveErrors++;
            if (consecutiveErrors == 1)
                Plugin.Log.Error(ex, "NahjaUI: failed to build the game snapshot.");
        }

        LastBuildMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private GameSnapshot Build()
    {
        frame++;

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer is null || !Plugin.PlayerState.IsLoaded)
            return GameSnapshot.Empty with { Frame = frame };

        var target = Plugin.TargetManager.Target;

        var context = new ContextSnapshot(
            Plugin.ClientState.TerritoryType,
            Plugin.Condition[ConditionFlag.InCombat],
            Plugin.Condition[ConditionFlag.BoundByDuty],
            Plugin.PartyList.Length);

        var player = ToUnit(localPlayer);

        return new GameSnapshot(
            true,
            player,
            ToUnit(target),
            ToUnit(Plugin.TargetManager.FocusTarget),
            ToUnit(TargetOfTarget(target, localPlayer)),
            BuildParty(),
            context,
            BuildCooldowns(),
            frame,
            BuildWhiteMageGauge(player),
            BuildWarriorGauge(player));
    }

    // The White Mage gauge (class job 24). Nobody else has one, and the game only fills it for White Mages.
    private const uint WhiteMageJobId = 24;

    private static WhiteMageGaugeSnapshot? BuildWhiteMageGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != WhiteMageJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<WHMGauge>();
        return new WhiteMageGaugeSnapshot(gauge.Lily, gauge.BloodLily, Math.Max((short)0, gauge.LilyTimer));
    }

    // The Warrior gauge. Warrior is class job 21 (Marauder, its first class, is 3 and has no Beast Gauge).
    private const uint WarriorJobId = 21;

    private static WarriorGaugeSnapshot? BuildWarriorGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != WarriorJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<WARGauge>();
        return new WarriorGaugeSnapshot(gauge.BeastGauge);
    }

    // The target of the target. For your own character the game keeps the target somewhere else than for the rest, so the
    // usual reading comes back empty when you target yourself. In that case your target's target is you.
    private static IGameObject? TargetOfTarget(IGameObject? target, IGameObject localPlayer)
    {
        if (target is null)
            return null;

        return target.EntityId == localPlayer.EntityId ? target : target.TargetObject;
    }

    // ---------------------------------------------------------------- conversion helpers

    // Only characters that fight (players, enemies, battle NPCs) have health and statuses.
    private static UnitSnapshot? ToUnit(IGameObject? gameObject)
    {
        if (gameObject is not IBattleChara chara)
            return null;

        return new UnitSnapshot(
            chara.EntityId,
            chara.Name.ToString(),
            chara.ClassJob.RowId,
            chara.Level,
            chara.CurrentHp,
            chara.MaxHp,
            chara.CurrentMp,
            chara.MaxMp,
            chara.IsDead,
            ToCast(chara),
            ToStatuses(chara.StatusList),
            chara.ObjectKind == ObjectKind.Pc,
            chara.ShieldPercentage,
            chara.StatusFlags.HasFlag(StatusFlags.Hostile));
    }

    private static UnitSnapshot ToUnit(IPartyMember member)
    {
        // The party list has the data of every member, even those far away.
        // Cast and death state only exist when the member is also in the object table.
        var chara = member.GameObject as IBattleChara;

        return new UnitSnapshot(
            member.EntityId,
            member.Name.ToString(),
            member.ClassJob.RowId,
            member.Level,
            member.CurrentHP,
            member.MaxHP,
            member.CurrentMP,
            member.MaxMP,
            chara?.IsDead ?? false,
            chara is null ? null : ToCast(chara),
            ToStatuses(member.Statuses));
    }

    private static CastSnapshot? ToCast(IBattleChara chara)
    {
        if (!chara.IsCasting)
            return null;

        return new CastSnapshot(chara.CastActionId, chara.CurrentCastTime, chara.TotalCastTime, chara.IsCastInterruptible, chara.CastActionType);
    }

    private static IReadOnlyList<StatusSnapshot> ToStatuses(StatusList statusList)
    {
        if (statusList.Length == 0)
            return Array.Empty<StatusSnapshot>();

        var result = new List<StatusSnapshot>(statusList.Length);

        foreach (var status in statusList)
        {
            // Empty slots have id 0.
            if (status.StatusId == 0)
                continue;

            result.Add(new StatusSnapshot(status.StatusId, status.SourceId, status.RemainingTime, status.Param));
        }

        return result;
    }

    // ---------------------------------------------------------------- cooldowns

    // Reads the recast information of the game for each watched action.
    // For actions with several charges the game keeps one timer for ALL charges: it counts up from empty
    // to full, and every (total / charges) seconds one more charge is available.
    private unsafe IReadOnlyDictionary<uint, CooldownSnapshot> BuildCooldowns()
    {
        if (watchedActions.Count == 0)
            return NoCooldowns;

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return NoCooldowns;

        var result = new Dictionary<uint, CooldownSnapshot>(watchedActions.Count);

        foreach (var requested in watchedActions)
        {
            // Some actions turn into another one at higher levels; the game works with the current form.
            var id = actionManager->GetAdjustedActionId(requested);

            var maxCharges = Math.Max(1, (int)ActionManager.GetMaxCharges(id, 0));
            var charges = Math.Min((int)actionManager->GetCurrentCharges(id), maxCharges);

            // The game keeps one "gauge" per recast group. It fills from empty to full (all the charges) as time passes.
            var group = actionManager->GetRecastGroup((int)ActionType.Action, id);
            var detail = group >= 0 ? actionManager->GetRecastGroupDetail(group) : null;

            float totalForAllCharges;
            float elapsed;
            bool groupActive;

            if (detail != null)
            {
                totalForAllCharges = actionManager->GetRecastTimeForGroup(group);
                elapsed = detail->Elapsed;
                groupActive = detail->IsActive;
            }
            else
            {
                totalForAllCharges = actionManager->GetRecastTime(ActionType.Action, id);
                elapsed = actionManager->GetRecastTimeElapsed(ActionType.Action, id);
                groupActive = actionManager->IsRecastTimerActive(ActionType.Action, id);
            }

            var perCharge = totalForAllCharges / maxCharges;

            // Seconds until the next charge. It does not depend on the "active" flag: an ability with one charge
            // spent and one left is still recharging, because its gauge is not full yet.
            var remaining = 0f;
            if (charges < maxCharges && perCharge > 0f && elapsed < totalForAllCharges)
            {
                remaining = maxCharges > 1
                    ? perCharge - (elapsed % perCharge)
                    : MathF.Max(0f, totalForAllCharges - elapsed);
            }

            result[requested] = new CooldownSnapshot(
                requested, charges, maxCharges, remaining, perCharge, elapsed, totalForAllCharges, groupActive, group);
        }

        return result;
    }

    private static IReadOnlyList<UnitSnapshot> BuildParty()
    {
        var partyList = Plugin.PartyList;
        if (partyList.Length == 0)
            return Array.Empty<UnitSnapshot>();

        var result = new List<UnitSnapshot>(partyList.Length);

        foreach (var member in partyList)
            result.Add(ToUnit(member));

        return result;
    }
}
