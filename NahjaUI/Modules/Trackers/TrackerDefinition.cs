using System;
using System.Collections.Generic;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.Trackers;

/// <summary>Where the value of a text on a tracker comes from.</summary>
public enum TextSource
{
    /// <summary>The time of the tracker itself: the status time, or the cooldown (or the effect time) of the ability.</summary>
    OwnTime,

    /// <summary>The stacks of the status of the tracker, or the charges of its ability.</summary>
    OwnStacks,

    /// <summary>The time left of the game's combo.</summary>
    ComboTime,

    /// <summary>The time left of a status chosen by the user (on the player).</summary>
    StatusTime,

    /// <summary>The stacks of a status chosen by the user (on the player).</summary>
    StatusStacks,

    /// <summary>The cooldown of an ability chosen by the user.</summary>
    AbilityCooldown,

    /// <summary>The charges of an ability chosen by the user.</summary>
    AbilityCharges
}

/// <summary>Where the picture of a tracker comes from.</summary>
public enum IconSource
{
    /// <summary>The icon of what the tracker tracks: the status, or the ability (in its current form).</summary>
    Automatic,

    /// <summary>The icon of a status chosen by the user, whatever the tracker tracks.</summary>
    Status,

    /// <summary>The icon of an ability chosen by the user, whatever the tracker tracks.</summary>
    Ability
}

/// <summary>What has to be true for an icon trigger to match.</summary>
public enum ConditionKind
{
    /// <summary>An enhancement or enfeeblement is on the unit.</summary>
    StatusPresent,

    /// <summary>An enhancement or enfeeblement is NOT on the unit (a reminder that it fell off).</summary>
    StatusMissing,

    /// <summary>The game turns an ability on right now: it glows, or its button changes into it (glowing actions, follow-up steps).</summary>
    AbilityHighlighted,

    /// <summary>An ability has no charge left (it is on cooldown).</summary>
    AbilityOnCooldown,

    /// <summary>An ability can be used (it has at least one charge).</summary>
    AbilityOffCooldown,

    /// <summary>An ability with several charges: all of them are available.</summary>
    AbilityFull,

    /// <summary>An ability with several charges: at least one is still recharging.</summary>
    AbilityNotFull,

    /// <summary>The game highlights the ability and it is not on cooldown (a follow-up ability that has its own cooldown, like Salt and Darkness).</summary>
    AbilityUsable
}

/// <summary>
/// One icon trigger of a tracker. The triggers of a tracker are tried in order: the first one that matches decides the picture,
/// the glow and the values of the texts. When none matches, the tracker is hidden.
/// </summary>
[Serializable]
public class TrackerRule
{
    public ConditionKind Kind { get; set; } = ConditionKind.StatusPresent;

    /// <summary>Status conditions: the unit to look at.</summary>
    public TrackerUnit Unit { get; set; } = TrackerUnit.Player;

    public uint StatusId { get; set; }
    public uint ActionId { get; set; }

    /// <summary>Status conditions: only count a status the player applied.</summary>
    public bool OnlyMine { get; set; }

    /// <summary>The texts drawn over the icon while this trigger decides: any number, each with its own value, size, font, position and colour.</summary>
    public List<TrackerTextDefinition> Texts { get; set; } = new();

    /// <summary>The picture shown while this trigger decides. It can differ from what is tracked.</summary>
    public IconSource Icon { get; set; } = IconSource.Automatic;
    public uint IconStatusId { get; set; }
    public uint IconActionId { get; set; }

    // The three effects of a trigger: applied whenever the trigger matches.
    public bool Glow { get; set; }

    /// <summary>A red mask over the icon (the border stays black).</summary>
    public bool Red { get; set; }

    /// <summary>The icon darkened.</summary>
    public bool Dim { get; set; }

    /// <summary>A dark sweep over the part of the icon that is still recharging (triggers of an ability, while it recharges).</summary>
    public bool Sweep { get; set; }

    [Newtonsoft.Json.JsonIgnore]
    public bool IsAbility => Kind is not (ConditionKind.StatusPresent or ConditionKind.StatusMissing);
}

/// <summary>One place a text can take its value from. A text tries its sources in order: the first one with a value wins.</summary>
[Serializable]
public class TrackerTextSource
{
    public TextSource Source { get; set; } = TextSource.OwnTime;
    public uint StatusId { get; set; }
    public uint ActionId { get; set; }
}

/// <summary>One text drawn over the icon of a tracker, with its own source and look.</summary>
[Serializable]
public class TrackerTextDefinition
{
    /// <summary>The places the value can come from, in order of priority.</summary>
    public List<TrackerTextSource> Sources { get; set; } = new();

    /// <summary>Height of the letters, in logical pixels.</summary>
    public int Size { get; set; } = 8;

    public AnchorPoint Anchor { get; set; } = AnchorPoint.Center;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public System.Numerics.Vector4 Color { get; set; } = new(1f, 1f, 1f, 1f);
}

/// <summary>Which unit a tracker watches.</summary>
public enum TrackerUnit
{
    Player,
    Target,
    Focus,
    TargetOfTarget
}

/// <summary>In which kind of content a tracker loads. The statuses and abilities of PvP are not the ones of PvE, so trackers are for PvE unless said otherwise.</summary>
public enum TrackerPvpMode
{
    PveOnly,
    PvpOnly,
    Everywhere
}

/// <summary>Rules that decide whether a tracker is active at all. A tracker that is not loaded costs nothing.</summary>
[Serializable]
public class TrackerLoadConditions
{
    /// <summary>Jobs the tracker loads on. Empty means every job.</summary>
    public List<uint> JobIds { get; set; } = new();

    /// <summary>PvE only by default.</summary>
    public TrackerPvpMode Where { get; set; } = TrackerPvpMode.PveOnly;
}

/// <summary>
/// One tracker, as saved in the configuration. Trackers are DATA, not code:
/// they can be edited, exported and shared safely.
/// Position and size are not stored here, they belong to the layout system.
/// </summary>
[Serializable]
public class TrackerDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New tracker";
    public bool Enabled { get; set; } = true;

    /// <summary>Id of the group that places this tracker, or null when the tracker has its own position.</summary>
    public string? GroupId { get; set; }

    /// <summary>The icon triggers, in order of priority: the first one that matches decides the picture and the glow.</summary>
    public List<TrackerRule> Rules { get; set; } = new();

    // Load conditions
    public TrackerLoadConditions Load { get; set; } = new();

    /// <summary>
    /// Copies the load conditions of another tracker.
    /// The name, the trigger, the group and the position are not touched.
    /// </summary>
    public void CopyStyleFrom(TrackerDefinition other)
    {
        Load = new TrackerLoadConditions
        {
            Where = other.Load.Where,
            JobIds = new List<uint>(other.Load.JobIds)
        };
    }
}
