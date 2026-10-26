using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;

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

    // The game only refreshes the remaining time of many statuses every few seconds (the ones of other players, for example).
    // To show a smooth countdown, the first time a given value is seen is remembered and the time that has passed since then
    // is taken off. When the game sends a new value, it wins. Key = (unit, status, who applied it).
    private Dictionary<(uint Unit, uint Status, uint Source), (float Reported, long Stamp)> statusClock = new();
    private Dictionary<(uint Unit, uint Status, uint Source), (float Reported, long Stamp)> nextStatusClock = new();

    // Your chocobo companion: found by scanning the object table now and then (not every frame), then looked up by id.
    private uint chocoboId;
    private long nextChocoboSearchFrame;

    // Cooldowns are only read for the actions someone asked for.
    private readonly HashSet<uint> watchedActions = new();
    private static readonly IReadOnlyDictionary<uint, CooldownSnapshot> NoCooldowns = new Dictionary<uint, CooldownSnapshot>();

    /// <summary>Set by the nameplate module every frame: the plates are only read while someone wants them.</summary>
    public bool WantNamePlates { get; set; }

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
        {
            statusClock.Clear();
            return GameSnapshot.Empty with { Frame = frame };
        }

        nextStatusClock.Clear();

        var target = Plugin.TargetManager.Target;

        var context = new ContextSnapshot(
            Plugin.ClientState.TerritoryType,
            Plugin.Condition[ConditionFlag.InCombat],
            Plugin.Condition[ConditionFlag.BoundByDuty],
            Plugin.PartyList.Length);

        var player = ToUnit(localPlayer);

        var snapshot = new GameSnapshot(
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
            BuildWarriorGauge(player),
            BuildChocobo(localPlayer),
            BuildNamePlates());

        // The statuses seen this frame are the ones to remember for the next one.
        (statusClock, nextStatusClock) = (nextStatusClock, statusClock);
        return snapshot;
    }

    // Your chocobo companion (the one the game shows at the end of its party list), or null when it is not out.
    private UnitSnapshot? BuildChocobo(IGameObject localPlayer)
    {
        // Only while in a party, like the game's list.
        if (Plugin.PartyList.Length == 0)
            return null;

        IBattleNpc? chocobo = null;

        if (chocoboId != 0 && Plugin.ObjectTable.SearchByEntityId(chocoboId) is IBattleNpc known &&
            known.BattleNpcKind == BattleNpcSubKind.Buddy && known.OwnerId == localPlayer.EntityId)
        {
            chocobo = known;
        }
        else if (frame >= nextChocoboSearchFrame)
        {
            // Not found: look again in about half a second.
            nextChocoboSearchFrame = frame + 30;
            chocoboId = 0;

            foreach (var gameObject in Plugin.ObjectTable)
            {
                if (gameObject is IBattleNpc npc && npc.BattleNpcKind == BattleNpcSubKind.Buddy && npc.OwnerId == localPlayer.EntityId)
                {
                    chocobo = npc;
                    chocoboId = npc.EntityId;
                    break;
                }
            }
        }

        var unit = chocobo is null ? null : ToUnit(chocobo);
        return unit is null ? null : unit with { IsCompanion = true };
    }

    // The enemies that have a plate in the game, with the screen position of the point above their head.
    // The game keeps its plates in two lists with the same order: the numbers (kind, entity id) and the 3D objects (world position).
    private unsafe IReadOnlyList<NamePlateSnapshot> BuildNamePlates()
    {
        var result = new List<NamePlateSnapshot>();
        if (!WantNamePlates)
            return result;

        var uiModule = UIModule.Instance();
        if (uiModule == null)
            return result;

        var ui3d = uiModule->GetUI3DModule();
        var numbers = NamePlateNumberArray.Instance();
        if (ui3d == null || numbers == null)
            return result;

        var count = Math.Min(Math.Min(numbers->ActiveNamePlateCount, ui3d->NamePlateObjectInfoCount), 50);

        // In this version of the game structures, the per plate data is a Span: it is read by index.
        var plateData = numbers->ObjectData;

        for (var i = 0; i < count; i++)
        {
            if (plateData[i].NamePlateKind != UIObjectKind.BattleNpcEnemy)
                continue;

            // 0xE0000000 is how the game marks an empty plate.
            var entityId = plateData[i].EntityId;
            if (entityId == 0 || entityId == 0xE0000000)
                continue;

            var info = ui3d->NamePlateObjectInfoPointers[i].Value;
            if (info == null)
                continue;

            if (Plugin.ObjectTable.SearchByEntityId(entityId) is not IBattleChara chara || chara.IsDead)
                continue;

            // The game gives the plate position as screen coordinates from -1 to 1 (checked with the log: x 0 is the middle of
            // the screen and y 0.43 is a bit above the middle). The z value is the depth: not positive means behind the camera.
            var point = info->NamePlatePos;
            if (point.Z <= 0f || MathF.Abs(point.X) > 1.2f || MathF.Abs(point.Y) > 1.2f)
                continue;

            var scale = Math.Clamp(plateData[i].Scale / 100f, 0f, 1f);
            result.Add(new NamePlateSnapshot(entityId, chara.Name.ToString(), chara.Level, chara.CurrentHp, chara.MaxHp, new Vector2(point.X, point.Y), scale));
        }

        return result;
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
    private UnitSnapshot? ToUnit(IGameObject? gameObject)
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
            ToStatuses(chara.EntityId, chara.StatusList),
            chara.ObjectKind == ObjectKind.Pc,
            chara.ShieldPercentage,
            chara.StatusFlags.HasFlag(StatusFlags.Hostile));
    }

    private UnitSnapshot ToUnit(IPartyMember member)
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
            ToStatuses(member.EntityId, member.Statuses));
    }

    private static CastSnapshot? ToCast(IBattleChara chara)
    {
        if (!chara.IsCasting)
            return null;

        return new CastSnapshot(chara.CastActionId, chara.CurrentCastTime, chara.TotalCastTime, chara.IsCastInterruptible, chara.CastActionType);
    }

    private IReadOnlyList<StatusSnapshot> ToStatuses(uint unitId, StatusList statusList)
    {
        if (statusList.Length == 0)
            return Array.Empty<StatusSnapshot>();

        var result = new List<StatusSnapshot>(statusList.Length);

        foreach (var status in statusList)
        {
            // Empty slots have id 0.
            if (status.StatusId == 0)
                continue;

            result.Add(new StatusSnapshot(status.StatusId, status.SourceId, SmoothRemaining(unitId, status.StatusId, status.SourceId, status.RemainingTime), status.Param));
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

    // The remaining time of a status, counted down between the moments the game refreshes it (see statusClock).
    private float SmoothRemaining(uint unitId, uint statusId, uint sourceId, float reported)
    {
        // No time limit (or already over): nothing to count.
        if (reported <= 0f)
            return reported;

        var key = (unitId, statusId, sourceId);
        var now = Stopwatch.GetTimestamp();

        if (statusClock.TryGetValue(key, out var previous) && previous.Reported == reported)
        {
            // The game still says the same number: it has not refreshed yet, so count the time ourselves.
            nextStatusClock[key] = previous;
            var elapsed = (float)Stopwatch.GetElapsedTime(previous.Stamp, now).TotalSeconds;
            return MathF.Max(0f, reported - elapsed);
        }

        // New status, or the game sent a new value: start counting from it.
        nextStatusClock[key] = (reported, now);
        return reported;
    }

    private IReadOnlyList<UnitSnapshot> BuildParty()
    {
        var partyList = Plugin.PartyList;
        if (partyList.Length == 0)
            return Array.Empty<UnitSnapshot>();

        var result = new List<UnitSnapshot>(partyList.Length);

        foreach (var member in partyList)
            result.Add(ToUnit(member));

        // The game's own party list has its own order (the "sort" option of the game: you first, by role...). The party list of
        // Dalamud does not follow it, so the position of each member in the game's list is read and used here.
        var order = ReadGameListOrder();
        if (order is not null && result.Count > 1)
            return result.OrderBy(unit => order.TryGetValue(unit.EntityId, out var position) ? position : int.MaxValue).ToList();

        return result;
    }

    // The position of each party member in the game's party list (entity id -> position, 0 is the top one), or null when it
    // cannot be read. It comes from the game's HUD agent: its list has you first, but each member also knows its real
    // position in the interface (HudPartyMember.Index).
    private static unsafe Dictionary<uint, int>? ReadGameListOrder()
    {
        var agent = AgentHUD.Instance();
        if (agent == null)
            return null;

        var members = agent->PartyMembers;
        var count = Math.Min((int)agent->PartyMemberCount, members.Length);
        if (count <= 0)
            return null;

        var order = new Dictionary<uint, int>(count);

        for (var i = 0; i < count; i++)
        {
            var entityId = members[i].EntityId;
            if (entityId != 0)
                order[entityId] = members[i].Index;
        }

        return order;
    }
}
