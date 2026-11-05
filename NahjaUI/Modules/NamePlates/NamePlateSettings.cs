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
    public bool Enabled { get; set; } = false;

    /// <summary>The plate of the target always shows its HP bar, even when the game's options would show only the name (or nothing).</summary>
    public bool ShowBarOnTarget { get; set; } = true;

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

    // ---- Settings saved before the plates had one style per kind: one single style for everything. ----
    // They arrive here as unknown fields and are turned into the new styles once, the first time the file is read.

    [JsonExtensionData]
    public IDictionary<string, JToken>? Legacy { get => legacy; set => legacy = value; }

    private IDictionary<string, JToken>? legacy;

    [OnDeserialized]
    private void OnDeserializedMigrate(StreamingContext context)
    {
        if (legacy is null || !legacy.ContainsKey("WidthPx"))
        {
            legacy = null;
            return;
        }

        var old = new JObject();
        foreach (var pair in legacy)
            old[pair.Key] = pair.Value;

        legacy = null;

        bool Flag(string name, bool fallback) => old.TryGetValue(name, out var token) ? token.Value<bool>() : fallback;

        // Every kind starts like the old single style, so nothing changes on screen.
        NamePlateStyle Copy(bool statusIcons, bool enemy)
        {
            var style = old.ToObject<NamePlateStyle>() ?? new NamePlateStyle();
            style.Active = true;
            style.Enhancements.Enabled = style.Enhancements.Enabled && statusIcons;
            style.Enfeeblements.Enabled = style.Enfeeblements.Enabled && statusIcons;

            // The level and the progress bar used to be only for the enemies.
            if (!enemy)
            {
                style.LevelMode = NamePlateLevelMode.Hidden;
                style.ShowProgressBar = false;
            }

            return style;
        }

        EnemyNpcs = Copy(Flag("StatusOnEnemies", true), true);
        EnemyPlayers = Copy(Flag("StatusOnEnemies", true), true);
        Self = Copy(Flag("StatusOnSelf", false), false);
        Party = Copy(Flag("StatusOnParty", false), false);
        OtherPlayers = Copy(Flag("StatusOnOtherPlayers", false), false);
        FriendlyNpcs = Copy(Flag("StatusOnFriendly", false), false);
        OtherPlates = Copy(false, false);
    }
}
