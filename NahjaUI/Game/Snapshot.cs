using System;
using System.Collections.Generic;
using System.Numerics;

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
/// <param name="ActionType">What is being cast: 1 = an action (spell or ability), 2 = an item, 5 = a general action, 13 = a mount.</param>
internal sealed record CastSnapshot(uint ActionId, float CurrentTime, float TotalTime, bool Interruptible, byte ActionType = 1);

/// <summary>The cooldown state of one player action (an ability).</summary>
/// <param name="ActionId">The action id that was asked for.</param>
/// <param name="Charges">Charges available now. Actions without charges give 1 when ready and 0 when on cooldown.</param>
/// <param name="MaxCharges">1 for normal actions, 2 or more for actions with charges.</param>
/// <param name="Remaining">Seconds until the next charge (or until the action can be used again). 0 when fully ready.</param>
/// <param name="Total">Seconds one charge takes to recharge.</param>
/// <param name="RawElapsed">Diagnostics: the game's own gauge level (seconds), as read.</param>
/// <param name="RawTotal">Diagnostics: seconds to fill the gauge from empty (all charges), as read.</param>
/// <param name="GroupActive">Diagnostics: the game's "recast group is active" flag.</param>
/// <param name="Group">Diagnostics: the recast group of the action.</param>
/// <param name="Highlighted">The game is making the button of the current form glow (a proc, or a follow-up like Goring Blade).</param>
/// <param name="CurrentActionId">The form the game shows for the action right now (it can change: Inner Release turns into Primal Wrath).</param>
internal sealed record CooldownSnapshot(
    uint ActionId,
    int Charges,
    int MaxCharges,
    float Remaining,
    float Total,
    float RawElapsed = 0f,
    float RawTotal = 0f,
    bool GroupActive = false,
    int Group = -1,
    uint CurrentActionId = 0,
    bool Highlighted = false)
{
    /// <summary>True when there is no charge to use right now.</summary>
    public bool IsOnCooldown => Charges <= 0;
}

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
    IReadOnlyList<StatusSnapshot> Statuses,
    bool IsPlayer = false,
    byte ShieldPercent = 0,
    bool IsHostile = false,
    bool IsCompanion = false,
    UnitKind Kind = UnitKind.Character);

/// <summary>What a target is, to give its frame a colour: a character (player, enemy, NPC that fights), a pet or minion,
/// an NPC without life (shopkeepers, retainers), or an object (furniture, chests, aetherytes...).</summary>
internal enum UnitKind
{
    Character,
    Pet,
    Npc,
    Object
}

/// <summary>
/// The White Mage job gauge. Only present while the player is a White Mage.
/// LilyTimerMs is the raw timer of the game, in milliseconds (it counts up towards the next lily).
/// </summary>
internal sealed record WhiteMageGaugeSnapshot(byte Lily, byte BloodLily, float LilyTimerMs);

/// <summary>The Warrior job gauge (Beast Gauge, 0 to 100). Only present while the player is a Warrior.</summary>
internal sealed record WarriorGaugeSnapshot(byte BeastGauge);

/// <summary>The Paladin job gauge (Oath Gauge, 0 to 100). Only present while the player is a Paladin.</summary>
internal sealed record PaladinGaugeSnapshot(byte OathGauge);

/// <summary>The combo of the game: the last action of the combo you used (0 if none) and the seconds left to continue it.</summary>
internal sealed record ComboSnapshot(uint Action, float Timer)
{
    public static readonly ComboSnapshot None = new(0, 0f);
}

/// <summary>General situation: where we are and what we are doing.</summary>
/// <param name="PartyListWhenSolo">The game's option "show the party list when playing alone" is on (raw value of PartyListSoloOff: 0 = on).</param>
/// <param name="SoloOptionRaw">The raw value of the option (-1 if it could not be read), shown in the settings to check it.</param>
internal sealed record ContextSnapshot(uint TerritoryId, bool InCombat, bool InDuty, int PartySize, bool PartyListWhenSolo = false, int SoloOptionRaw = -1)
{
    public static readonly ContextSnapshot None = new(0, false, false, 0);
}

/// <summary>An enemy that has a nameplate in the game right now.</summary>
/// <param name="Ndc">Where the game hangs the plate, in screen coordinates from -1 to 1 (x to the right, y UP). It already accounts for the height of the enemy.</param>
/// <param name="Scale">Size of the plate the game would use, from 0 (far) to 1 (near).</param>
/// <param name="Cast">What the enemy is casting, or null.</param>
internal sealed record NamePlateSnapshot(uint EntityId, string Name, int Level, uint Hp, uint MaxHp, Vector2 Ndc, float Scale, CastSnapshot? Cast);

/// <summary>An enemy of the game's enemy list: its data, and whether it is attacking you.</summary>
internal sealed record EnemyEntrySnapshot(UnitSnapshot Unit, bool TargetsMe);

/// <summary>Everything the plugin knows about the game during one frame.</summary>
internal sealed record GameSnapshot(
    bool IsLoggedIn,
    UnitSnapshot? Player,
    UnitSnapshot? Target,
    UnitSnapshot? Focus,
    UnitSnapshot? TargetOfTarget,
    IReadOnlyList<UnitSnapshot> Party,
    ContextSnapshot Context,
    IReadOnlyDictionary<uint, CooldownSnapshot> Cooldowns,
    long Frame,
    WhiteMageGaugeSnapshot? WhiteMage = null,
    WarriorGaugeSnapshot? Warrior = null,
    UnitSnapshot? Chocobo = null,
    IReadOnlyList<NamePlateSnapshot>? NamePlates = null,
    PaladinGaugeSnapshot? Paladin = null,
    ComboSnapshot? Combo = null,
    IReadOnlyList<EnemyEntrySnapshot>? EnemyList = null)
{
    public static readonly GameSnapshot Empty = new(
        false, null, null, null, null, Array.Empty<UnitSnapshot>(), ContextSnapshot.None,
        new Dictionary<uint, CooldownSnapshot>(), 0);
}
