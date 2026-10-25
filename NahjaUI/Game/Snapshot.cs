using System;
using System.Collections.Generic;

namespace NahjaUI.Game;

// The snapshot is a read-only picture of the game, built once per frame by GameDataProvider.
// Modules read these records and never talk to Dalamud directly.

/// <summary>A buff or debuff on a unit.</summary>
/// <param name="Id">Status id (row of the Status sheet).</param>
/// <param name="SourceId">Entity id of whoever applied it. Compare with the player's entity id to know if it is yours.</param>
/// <param name="RemainingTime">Seconds left. Statuses without a duration report 0 or a negative value.</param>
/// <param name="Param">Raw parameter of the status. For stackable statuses it holds the stack count.</param>
internal sealed record StatusSnapshot(uint Id, uint SourceId, float RemainingTime, ushort Param);

/// <summary>An action a unit is casting right now.</summary>
internal sealed record CastSnapshot(uint ActionId, float CurrentTime, float TotalTime, bool Interruptible);

/// <summary>A character: the player, the target, a party member...</summary>
internal sealed record UnitSnapshot(
    uint EntityId,
    string Name,
    uint JobId,
    int Level,
    uint Hp,
    uint MaxHp,
    uint Mp,
    uint MaxMp,
    bool IsDead,
    CastSnapshot? Cast,
    IReadOnlyList<StatusSnapshot> Statuses);

/// <summary>General situation: where we are and what we are doing.</summary>
internal sealed record ContextSnapshot(uint TerritoryId, bool InCombat, bool InDuty, int PartySize)
{
    public static readonly ContextSnapshot None = new(0, false, false, 0);
}

/// <summary>Everything the plugin knows about the game during one frame.</summary>
internal sealed record GameSnapshot(
    bool IsLoggedIn,
    UnitSnapshot? Player,
    UnitSnapshot? Target,
    UnitSnapshot? Focus,
    UnitSnapshot? TargetOfTarget,
    IReadOnlyList<UnitSnapshot> Party,
    ContextSnapshot Context,
    long Frame)
{
    public static readonly GameSnapshot Empty = new(
        false, null, null, null, null, Array.Empty<UnitSnapshot>(), ContextSnapshot.None, 0);
}
