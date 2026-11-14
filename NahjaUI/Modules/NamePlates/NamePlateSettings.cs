using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NahjaUI.Game;

namespace NahjaUI.Modules.NamePlates;

/// <summary>How the level is written after the name.</summary>
public enum NamePlateLevelMode
{
    /// <summary>Not written at all.</summary>
    Hidden,

    /// <summary>Only the number: "100".</summary>
    NumberOnly,

    /// <summary>With the prefix: "Lv 100".</summary>
    WithPrefix
}

/// <summary>
/// Settings of the nameplates. Turned off until it is switched on in the Nameplates page. Each kind of plate (enemies, party...)
/// has its own style.
/// </summary>
[Serializable]
public class NamePlateSettings
{
    /// <summary>The module works while at least one kind of plate is enabled (each kind has its own switch in its tab).</summary>
    [JsonIgnore]
    public bool Enabled => Self.Active || Party.Active || OtherPlayers.Active || FriendlyNpcs.Active || EnemyNpcs.Active || EnemyPlayers.Active || OtherPlates.Active;

    public NamePlateStyle Self { get; set; } = NamePlateStyle.Create(NamePlateRelation.Self);
    public NamePlateStyle Party { get; set; } = NamePlateStyle.Create(NamePlateRelation.Party);
    public NamePlateStyle OtherPlayers { get; set; } = NamePlateStyle.Create(NamePlateRelation.OtherPlayer);
    public NamePlateStyle FriendlyNpcs { get; set; } = NamePlateStyle.Create(NamePlateRelation.Friendly);
    public NamePlateStyle EnemyNpcs { get; set; } = NamePlateStyle.Create(NamePlateRelation.Enemy);
    public NamePlateStyle EnemyPlayers { get; set; } = NamePlateStyle.Create(NamePlateRelation.EnemyPlayer);
    public NamePlateStyle OtherPlates { get; set; } = NamePlateStyle.Create(NamePlateRelation.Other);

    /// <summary>The starting enfeeblements row of the enemies was put in place (once, so a later change of the user is never undone).</summary>
    public bool EnemyRowSeeded { get; set; }

    /// <summary>
    /// Once: the enemies' plates start showing stuns, mitigation and the rotation's enfeeblements. Only where the row was never set up
    /// (off and with nothing chosen), so nobody's own choice is changed.
    /// </summary>
    internal void SeedEnemyRows()
    {
        if (EnemyRowSeeded)
            return;

        EnemyRowSeeded = true;
        foreach (var style in new[] { EnemyNpcs, EnemyPlayers })
        {
            var row = style.Enfeeblements;
            if (!row.Enabled && row.FilterIds.Count == 0 && row.StatusIds.Count == 0)
                NamePlateStyle.ApplyEnemyEnfeeblements(row);
        }
    }

    internal NamePlateStyle StyleOf(NamePlateRelation kind) => kind switch
    {
        NamePlateRelation.Self => Self,
        NamePlateRelation.Party => Party,
        NamePlateRelation.OtherPlayer => OtherPlayers,
        NamePlateRelation.Friendly => FriendlyNpcs,
        NamePlateRelation.Enemy => EnemyNpcs,
        NamePlateRelation.EnemyPlayer => EnemyPlayers,
        _ => OtherPlates
    };

    internal void SetStyle(NamePlateRelation kind, NamePlateStyle style)
    {
        switch (kind)
        {
            case NamePlateRelation.Self: Self = style; break;
            case NamePlateRelation.Party: Party = style; break;
            case NamePlateRelation.OtherPlayer: OtherPlayers = style; break;
            case NamePlateRelation.Friendly: FriendlyNpcs = style; break;
            case NamePlateRelation.Enemy: EnemyNpcs = style; break;
            case NamePlateRelation.EnemyPlayer: EnemyPlayers = style; break;
            default: OtherPlates = style; break;
        }
    }
}
