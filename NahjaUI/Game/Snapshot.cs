using System;
using System.Collections.Generic;
using System.Numerics;

namespace NahjaUI.Game;

// The snapshot is a read-only picture of the game, built once per frame by GameDataProvider.
// Modules read these records and never talk to Dalamud directly.

/// <summary>An enhancement or enfeeblement on a unit.</summary>
/// <param name="Id">Status id (row of the Status sheet).</param>
/// <param name="SourceId">Entity id of whoever applied it. Compare with the player's entity id to know if it is yours.</param>
/// <param name="RemainingTime">Seconds left. Statuses without a duration report 0 or a negative value.</param>
/// <param name="Param">Raw parameter of the status. For stackable statuses it holds the stack count.</param>
internal sealed record StatusSnapshot(uint Id, uint SourceId, float RemainingTime, ushort Param);

/// <summary>An action a unit is casting right now.</summary>
/// <param name="ActionType">What is being cast: 1 = an action (spell, weaponskill or ability), 2 = an item, 5 = a general action, 13 = a mount.</param>
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
/// <param name="Highlighted">The game is making the button of the current form glow (a ready action, or a follow-up like Goring Blade).</param>
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
    UnitKind Kind = UnitKind.Character,
    Presence Presence = Presence.Here);

/// <summary>Where a party member is, seen from you: next to you (maybe watching a cutscene), too far away, in another area, or offline.</summary>
internal enum Presence
{
    Here,
    ViewingCutscene,
    OutOfRange,
    OtherArea,
    Offline
}

/// <summary>What a target is, to give its frame a colour: a character (player, enemy, NPC that fights), a pet or minion,
/// an NPC without HP (shopkeepers, retainers), or an object (furniture, chests, aetherytes...).</summary>
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

/// <summary>The Dark Knight job gauge: Blood (0 to 100), the Darkside time left (milliseconds, up to 60 seconds) and whether Dark Arts is active. Only present while the player is a Dark Knight.</summary>
internal sealed record DarkKnightGaugeSnapshot(byte Blood, ushort DarksideMs, bool DarkArts, ushort LivingShadowMs);

/// <summary>The Gunbreaker job gauge: Cartridges (0 to 3). Only present while the player is a Gunbreaker.</summary>
internal sealed record GunbreakerGaugeSnapshot(byte Cartridges);

/// <summary>The Scholar job gauge: Aetherflow stacks (0 to 3), the Faerie Gauge (0 to 100) and the time left of the Seraph (milliseconds, 0 when she is not out).</summary>
internal sealed record ScholarGaugeSnapshot(byte Aetherflow, byte Faerie, ushort SeraphMs);

/// <summary>
/// The Astrologian job gauge: the three drawn cards (true = the slot holds a card), whether they come from the Umbral Draw (false = Astral Draw),
/// and the Crown card (0 none, 1 Lord of Crowns, 2 Lady of Crowns).
/// </summary>
internal sealed record AstrologianGaugeSnapshot(bool Card1, bool Card2, bool Card3, bool Umbral, byte Crown);

/// <summary>The Sage job gauge: Addersgall stacks (0 to 3), the time towards the next one (milliseconds, counts up to 20 seconds) and Addersting stacks (0 to 3).</summary>
internal sealed record SageGaugeSnapshot(byte Addersgall, short AddersgallMs, byte Addersting);

/// <summary>The Red Mage job gauge: White Mana and Black Mana (0 to 100 each) and the Mana Stacks (0 to 3). Only present while the player is a Red Mage.</summary>
internal sealed record RedMageGaugeSnapshot(byte WhiteMana, byte BlackMana, byte ManaStacks);

/// <summary>How a part of a generic job gauge is drawn: a bar (Value out of Max), pips (Value filled out of Max, the next one filled by Partial) or slots (each one empty or in the colour of SlotColors).</summary>
internal enum GaugePartKind
{
    Bar,
    Pips,
    Slots
}

/// <summary>
/// One row of a generic job gauge (the melee, physical ranged and caster jobs). Id is the row of the job's definition; the job decides every frame
/// how the row is drawn (Kind), so a row can change shape (the Reaper's Shroud becomes the Lemure Shroud while enshrouded).
/// Variant is the index of the colour in the row's list of colours. Text is written over the row.
/// </summary>
internal sealed record GaugePartSnapshot(string Id, GaugePartKind Kind, float Value, float Max, float Partial = 0f, string? Text = null, int Variant = 0, int[]? SlotColors = null);

/// <summary>The gauge of one of the jobs that use the generic gauge: its rows, in the order of the job's definition.</summary>
/// <summary>The global cooldown: whether it is running, how long it has run and how long it lasts (seconds).</summary>
internal sealed record GcdSnapshot(bool Active, float Elapsed, float Total);

internal sealed record ExtraGaugeSnapshot(uint JobId, IReadOnlyList<GaugePartSnapshot> Parts);

/// <summary>The party's Limit Break gauge (same for every job): units gathered so far and how many units one bar holds.</summary>
internal sealed record LimitBreakSnapshot(uint Units, uint BarUnits, int Bars);

/// <summary>The gauge of the game's Frontline window (PvP): the number it shows and its maximum. Only present while that window exists.</summary>
internal sealed record FrontlineGaugeSnapshot(uint Value, uint Max);

/// <summary>The combo of the game: the last action of the combo you used (0 if none) and the seconds left to continue it.</summary>
internal sealed record ComboSnapshot(uint Action, float Timer)
{
    public static readonly ComboSnapshot None = new(0, 0f);
}

/// <summary>General situation: where we are and what we are doing.</summary>
/// <param name="InPvP">In a PvP zone (Frontline, Crystalline Conflict... and the Wolves' Den Pier, where the PvP actions of your job are active): PvE actions, job gauges and the enmity list do not work there.</param>
/// <param name="InPvPMatch">In a PvP match (Frontline, Crystalline Conflict...), not the Wolves' Den Pier: the Frontline gauge only exists here.</param>
/// <param name="PartyListWhenSolo">The game's option "show the party list when playing alone" is on (raw value of PartyListSoloOff: 0 = on).</param>
/// <param name="SoloOptionRaw">The raw value of the option (-1 if it could not be read), shown in the settings to check it.</param>
internal sealed record ContextSnapshot(uint TerritoryId, bool InCombat, bool InDuty, int PartySize, bool PartyListWhenSolo = false, int SoloOptionRaw = -1, bool InPvP = false, bool InPvPMatch = false)
{
    public static readonly ContextSnapshot None = new(0, false, false, 0);
}

/// <summary>An enemy that has a nameplate in the game right now.</summary>
/// <param name="Ndc">Where the game hangs the plate, in screen coordinates from -1 to 1 (x to the right, y UP). It already accounts for the height of the enemy.</param>
/// <param name="Scale">Size of the plate the game would use, from 0 (far) to 1 (near).</param>
/// <param name="Cast">What the enemy is casting, or null.</param>
/// <summary>Who a plate belongs to, from the point of view of the player.</summary>
internal enum NamePlateRelation
{
    /// <summary>A hostile battle NPC.</summary>
    Enemy,
    Self,
    Party,
    OtherPlayer,
    /// <summary>A fighting character that is not hostile and not a player: pets, the chocobo, friendly battle NPCs. Has HP.</summary>
    Friendly,
    /// <summary>Everything without HP: NPCs, doors and objects, minions, retainers, gathering points, treasure. Text only.</summary>
    Other,
    /// <summary>A hostile player (PvP).</summary>
    EnemyPlayer
}

internal static class NamePlateRelations
{
    /// <summary>Hostile NPCs and hostile players.</summary>
    public static bool IsEnemy(this NamePlateRelation relation) => relation is NamePlateRelation.Enemy or NamePlateRelation.EnemyPlayer;
}

internal sealed record NamePlateSnapshot(uint EntityId, string Name, int Level, uint Hp, uint MaxHp, Vector2 Ndc, float Scale, CastSnapshot? Cast,
    NamePlateRelation Relation = NamePlateRelation.Enemy, ulong Key = 0, bool IsTarget = false, IReadOnlyList<StatusSnapshot>? Statuses = null);

/// <summary>An enemy of the game's enmity list: its data, and whether it is attacking you.</summary>
/// <param name="ActiveInList">Test values (-1 unknown): the game's "active in list" flag of the row.</param>
/// <param name="LockedInList">Test values (-1 unknown): the game's "locked in list" flag of the row.</param>
/// <param name="Targetable">Test values (-1 unknown): whether the enemy can be targeted right now.</param>
internal sealed record EnemyEntrySnapshot(UnitSnapshot Unit, bool TargetsMe, int ActiveInList = -1, int LockedInList = -1, int Targetable = -1);

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
    IReadOnlyList<EnemyEntrySnapshot>? EnmityList = null,
    RedMageGaugeSnapshot? RedMage = null,
    LimitBreakSnapshot? LimitBreak = null,
    FrontlineGaugeSnapshot? Frontline = null,
    DarkKnightGaugeSnapshot? DarkKnight = null,
    GunbreakerGaugeSnapshot? Gunbreaker = null,
    ScholarGaugeSnapshot? Scholar = null,
    AstrologianGaugeSnapshot? Astrologian = null,
    SageGaugeSnapshot? Sage = null,
    ExtraGaugeSnapshot? ExtraGauge = null,
    GcdSnapshot? Gcd = null)
{
    public static readonly GameSnapshot Empty = new(
        false, null, null, null, null, Array.Empty<UnitSnapshot>(), ContextSnapshot.None,
        new Dictionary<uint, CooldownSnapshot>(), 0);
}
