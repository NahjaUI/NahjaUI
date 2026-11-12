using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace NahjaUI.Modules.Filters;

/// <summary>
/// The four defensive filters (invulnerabilities, mitigation, shields and barriers, sustained healing). The statuses are written by their
/// English name and looked up in the game's own table, so the ids are always the real ones. The names that the game does not know are reported.
/// </summary>
internal static class DefensiveFilters
{
    internal sealed record Definition(string Id, string Name, string[] Statuses);

    public const string InvulnerabilitiesId = "defensive-invulnerabilities";
    public const string MitigationId = "defensive-mitigation";
    public const string ShieldsId = "defensive-shields";
    public const string HealingId = "defensive-sustained-healing";
    public const string RaiseId = "defensive-raise";
    public const string MovementId = "defensive-movement";
    public const string DancePartnerId = "defensive-dance-partner";
    public const string RotationId = "rotation-upkeep";

    public static readonly Definition[] All =
    {
        new(InvulnerabilitiesId, "Invulnerabilities", new[]
        {
            "Hallowed Ground", "Holmgang", "Living Dead", "Walking Dead", "Superbolide"
        }),
        new(MitigationId, "Mitigation", new[]
        {
            // Tanks
            "Rampart", "Sentinel", "Guardian", "Vengeance", "Damnation", "Shadow Wall", "Shadowed Vigil", "Dark Mind", "Dark Missionary",
            "Camouflage", "Nebula", "Great Nebula", "Heart of Light", "Heart of Stone", "Heart of Corundum", "Sheltron", "Holy Sheltron",
            "Knight's Resolve", "Passage of Arms", "Intervention", "Cover", "Oblation", "Raw Intuition", "Bloodwhetting", "Stem the Flow",
            "Nascent Flash", "Nascent Glint", "Aurora", "Clarity of Corundum", "Catharsis of Corundum", "Arm's Length",
            // Healers
            "Sacred Soil", "Kerachole", "Holos", "Collective Unconscious", "Exaltation", "Neutral Sect", "Temperance", "Divine Grace",
            "Expedient", "Seraphic Veil", "Fey Illumination", "Aquaveil", "Taurochole", "Krasis", "Zoe", "Philosophia", "Macrocosmos",
            // Role actions of the DPS (the first ones are on your party; Feint, Addle, Reprisal and Dismantled are on the enemy)
            "Troubadour", "Tactician", "Shield Samba", "Magick Barrier", "Tengentsu", "Tengentsu's Foresight",
            "Feint", "Addle", "Reprisal", "Dismantled"
        }),
        new(ShieldsId, "Shields", new[]
        {
            "Divine Veil", "Shake It Off", "The Blackest Night", "Stem the Tide", "Brutal Shell", "Divine Benison", "Galvanize", "Catalyze",
            "Eukrasian Diagnosis", "Eukrasian Prognosis", "Haima", "Panhaima", "Haimatinon", "Panhaimatinon", "Radiant Aegis", "Shade Shift",
            "Manaward", "Consolation", "Sun Sign", "Eukrasian Prognosis II"
        }),
        new(HealingId, "HoTs", new[]
        {
            "Regen", "Medica II", "Asylum", "Aspected Benefic", "Aspected Helios", "Whispering Dawn", "Excogitation", "Wheel of Fortune",
            "Physis II", "Fey Union", "Equilibrium", "Thrill of Battle", "Kardia", "Soteria", "Embrace", "Eukrasian Diagnosis (HoT)",
            "Helios Conjunction", "Lustrate"
        }),
        new(RaiseId, "Raise", new[] { "Raise" }),
        // The "+" means every status with that name (the Dancer and the partner have a Closed Position each, with different ids).
        new(DancePartnerId, "Dance Partner", new[] { "+Closed Position", "+Dance Partner" }),
        new(MovementId, "Movement", new[] { "Sprint", "Peloton", "Jog" }),
        // The enfeeblements a job has to keep on the enemy all the time during its rotation (the damage over time of each job and the
        // ones that work like it). The "+" is every status with that name; the old names are for the low levels.
        new(RotationId, "Rotation upkeep", new[]
        {
            "+Dia", "+Aero II", "+Aero",                                     // White Mage
            "+Biolysis", "+Bio II", "+Bio",                                  // Scholar
            "+Combust III", "+Combust II", "+Combust",                       // Astrologian
            "+Eukrasian Dosis III", "+Eukrasian Dosis II", "+Eukrasian Dosis", // Sage
            "+Chaotic Spring", "+Chaos Thrust",                              // Dragoon
            "+Higanbana",                                                    // Samurai
            "+Death's Design",                                               // Reaper
            "+Stormbite", "+Windbite", "+Caustic Bite", "+Venomous Bite",    // Bard
            "+High Thunder", "+Thunder III", "+Thunder",                     // Black Mage
            "+Demolish",                                                     // Monk
            "+Sonic Break", "+Bow Shock"                                     // Gunbreaker
        })
    };

    // The order of the filters on the party list: a dead player with a Raise on first, movement last.
    private static readonly string[] RowOrder = { RaiseId, InvulnerabilitiesId, MitigationId, ShieldsId, HealingId, DancePartnerId, MovementId };

    /// <summary>The names the last build did not find (shown on the Filters page).</summary>
    public static IReadOnlyList<string> LastMissing { get; private set; } = Array.Empty<string>();

    /// <summary>Creates the starting filters once, and puts them on the party list.</summary>
    public static void EnsureCreated(Configuration configuration)
    {
        var filters = configuration.Filters;
        if (filters.DefensivesCreated)
            return;

        filters.DefensivesCreated = true;

        var ids = BuildIds();
        var missing = new List<string>();
        foreach (var definition in All)
        {
            if (filters.Find(definition.Id) is not null)
                continue;

            var filter = new StatusFilter { Id = definition.Id, Name = definition.Name };
            filters.Filters.Add(filter);
            Fill(filter, definition, ids, missing);
        }

        // The partner carries "Dance Partner", not "Closed Position" (that one is the Dancer's own).
        var dancePartner = filters.Find(DancePartnerId);
        if (dancePartner is not null)
        {
            foreach (var id in IdsNamed("Dance Partner"))
            {
                if (!dancePartner.StatusIds.Contains(id))
                    dancePartner.StatusIds.Add(id);
            }
        }

        LastMissing = missing;

        var party = configuration.HudBars.Party;
        var enhancements = party.Enhancements;

        // The enhancements row shows only these filters, in this order; the enfeeblements row shows all of them with a border on the
        // ones a cleanse can remove.
        enhancements.Filter = NahjaUI.Modules.PartyListBar.StatusRowFilter.OnlyListed;
        enhancements.StatusIds.Clear();
        enhancements.OnlyMine = false;
        enhancements.MaxIcons = 6;
        enhancements.PriorityByFilter = true;
        enhancements.FilterIds = new List<string>(RowOrder);
        enhancements.OnlyMineFilterIds = new List<string> { HealingId, DancePartnerId };

        var enfeeblements = party.Enfeeblements;
        enfeeblements.Filter = NahjaUI.Modules.PartyListBar.StatusRowFilter.All;
        enfeeblements.OnlyMine = false;
    }

    /// <summary>Recreates the default filters that are missing (same ids, so what uses them finds them again). The ones that exist are not touched.</summary>
    public static int RestoreMissing(FiltersSettings filters)
    {
        var restored = 0;

        if (filters.Find(FiltersSettings.CrowdControlId) is null)
        {
            var crowdControl = new StatusFilter { Id = FiltersSettings.CrowdControlId, Name = "Crowd control" };
            if (Game.GameNames.StatusName(2) == "Stun")
                crowdControl.StatusIds.Add(2);

            filters.Filters.Add(crowdControl);
            restored++;
        }

        var ids = BuildIds();
        var missing = new List<string>();
        foreach (var definition in All)
        {
            if (filters.Find(definition.Id) is not null)
                continue;

            var filter = new StatusFilter { Id = definition.Id, Name = definition.Name };
            filters.Filters.Add(filter);
            Fill(filter, definition, ids, missing);
            restored++;
        }

        LastMissing = missing;
        return restored;
    }

    /// <summary>Fills a filter again with its default statuses (the other statuses the user added are kept).</summary>
    public static void Restore(FiltersSettings filters, string filterId)
    {
        var definition = Array.Find(All, d => d.Id == filterId);
        var filter = filters.Find(filterId);
        if (definition is null || filter is null)
            return;

        var missing = new List<string>();
        Fill(filter, definition, BuildIds(), missing);
        LastMissing = missing;
    }

    private static void Fill(StatusFilter filter, Definition definition, Dictionary<string, uint> ids, List<string> missing)
    {
        foreach (var name in definition.Statuses)
        {
            if (name.StartsWith('+'))
            {
                var every = IdsNamed(name.Substring(1));
                if (every.Count == 0)
                    missing.Add($"{definition.Name}: {name.Substring(1)}");

                foreach (var one in every)
                {
                    if (!filter.StatusIds.Contains(one))
                        filter.StatusIds.Add(one);
                }

                continue;
            }

            if (ids.TryGetValue(name, out var id))
            {
                if (!filter.StatusIds.Contains(id))
                    filter.StatusIds.Add(id);
            }
            else
            {
                missing.Add($"{definition.Name}: {name}");
            }
        }
    }

    // Every status with this exact name (PvP versions included: they are harmless in a filter).
    private static List<uint> IdsNamed(string name)
    {
        var result = new List<uint>();
        foreach (var row in Plugin.DataManager.GetExcelSheet<Status>(Dalamud.Game.ClientLanguage.English))
        {
            if (string.Equals(row.Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
                result.Add(row.RowId);
        }

        return result;
    }

    // Every status name of the English table with its lowest id (the PvE one: PvP statuses are made later).
    private static Dictionary<string, uint> BuildIds()
    {
        var ids = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Plugin.DataManager.GetExcelSheet<Status>(Dalamud.Game.ClientLanguage.English))
        {
            var name = row.Name.ToString();
            if (name.Length > 0 && !ids.ContainsKey(name))
                ids[name] = row.RowId;
        }

        return ids;
    }
}
