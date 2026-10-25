using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Plugin.Services;

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

        return new GameSnapshot(
            true,
            ToUnit(localPlayer),
            ToUnit(target),
            ToUnit(Plugin.TargetManager.FocusTarget),
            ToUnit(target?.TargetObject),
            BuildParty(),
            context,
            frame);
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
            ToStatuses(chara.StatusList));
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

        return new CastSnapshot(chara.CastActionId, chara.CurrentCastTime, chara.TotalCastTime, chara.IsCastInterruptible);
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
