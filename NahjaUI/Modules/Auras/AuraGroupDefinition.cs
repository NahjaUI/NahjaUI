using System;

namespace NahjaUI.Modules.Auras;

/// <summary>How the icons of a group line up, starting from the group's box.</summary>
public enum GroupGrowth
{
    /// <summary>The first icon is at the left of the box, the rest follow to the right.</summary>
    ToRight,

    /// <summary>The first icon is at the right of the box, the rest follow to the left.</summary>
    ToLeft,

    /// <summary>The row is always centred on the box, whatever the number of icons.</summary>
    FromCenterHorizontal,

    /// <summary>The first icon is at the top of the box, the rest follow downwards.</summary>
    Down,

    /// <summary>The first icon is at the bottom of the box, the rest follow upwards.</summary>
    Up,

    /// <summary>The column is always centred on the box, whatever the number of icons.</summary>
    FromCenterVertical
}

/// <summary>
/// A group of auras that are lined up automatically (rotation, defensives, miscellaneous...).
/// Only the group has a position (its box in unlock mode); the icons inside it are placed by the group.
/// </summary>
[Serializable]
public class AuraGroupDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New group";

    public GroupGrowth Growth { get; set; } = GroupGrowth.FromCenterHorizontal;

    /// <summary>Space between two icons, in pixels.</summary>
    public int Spacing { get; set; } = 4;

    /// <summary>Whether the group is open in the explorer list of the Auras page.</summary>
    public bool Expanded { get; set; } = true;

    /// <summary>
    /// The icon size chosen for the whole group, in pixels. It is applied to every aura of the group at once,
    /// and to auras that join the group later. 0 means "not chosen yet" (an older group): the size of its first aura is used.
    /// </summary>
    public int IconWidth { get; set; }

    public int IconHeight { get; set; }

    /// <summary>
    /// The display settings and load conditions of the whole group. Only those settings of this aura are used
    /// (not its name, trigger or status). Changing them rewrites the same settings in every aura of the group,
    /// and auras that join the group take them. Null in groups made before this existed.
    /// </summary>
    public AuraDefinition? Template { get; set; }
}
