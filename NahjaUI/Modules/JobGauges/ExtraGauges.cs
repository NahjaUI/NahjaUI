using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NahjaUI.Game;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// One row of a generic job gauge: its name, its height and its list of colours (the game data says which colour each row uses
/// at every moment, see GaugePartSnapshot.Variant and SlotColors). Marks are fractions of a bar where a line is drawn (where a spender can be used).
/// </summary>
internal sealed record GaugePartDef(string Id, string Label, int Height, Vector4[] Colors, string[] ColorNames, float[]? Marks = null);

/// <summary>A job that uses the generic gauge: its rows from top to bottom, the windows of the game that are hidden when it is on, and a sample for the preview.</summary>
internal sealed record ExtraGaugeDef(uint JobId, string Abbr, string Name, GaugePartDef[] Parts, string[] NativeWindows, GaugePartSnapshot[] Sample);

/// <summary>
/// The gauges of the melee, physical ranged and caster jobs (Red Mage has its own). They are not one class each: every job is a list of rows
/// (bars, pips and slots) that the game data fills (GameDataProvider.BuildExtraGauge) and one module draws, with one settings page for all.
/// </summary>
internal static class ExtraGauges
{
    private static Vector4 C(float r, float g, float b) => new(r, g, b, 1f);

    private static readonly float[] Half = { 0.5f };

    public static readonly ExtraGaugeDef[] All =
    {
        new(20, "MNK", "Monk", new GaugePartDef[]
        {
            new("chakra", "Chakra", 6, new[] { C(1f, 0.72f, 0.25f) }, new[] { "Chakra" }),
            new("blitz", "Beast Chakra", 6, new[] { C(1f, 0.80f, 0.30f), C(0.40f, 0.85f, 0.45f), C(0.90f, 0.35f, 0.30f) }, new[] { "Opo-opo", "Raptor", "Coeurl" }),
            new("nadi", "Nadi", 6, new[] { C(0.70f, 0.50f, 1f), C(1f, 0.60f, 0.25f) }, new[] { "Lunar", "Solar" })
        }, new[] { "JobHudMNK0", "JobHudMNK1" }, new GaugePartSnapshot[]
        {
            new("chakra", GaugePartKind.Pips, 3, 5),
            new("blitz", GaugePartKind.Slots, 0, 3, SlotColors: new[] { 0, 1, -1 }),
            new("nadi", GaugePartKind.Slots, 0, 2, SlotColors: new[] { 0, -1 })
        }),

        new(22, "DRG", "Dragoon", new GaugePartDef[]
        {
            new("lotd", "Life of the Dragon", 6, new[] { C(0.90f, 0.40f, 0.30f) }, new[] { "Life of the Dragon" }),
            new("focus", "Firstminds' Focus", 6, new[] { C(0.55f, 0.75f, 1f) }, new[] { "Focus" })
        }, new[] { "JobHudDRG0" }, new GaugePartSnapshot[]
        {
            new("lotd", GaugePartKind.Bar, 12000, 20000, Text: "12"),
            new("focus", GaugePartKind.Pips, 1, 2)
        }),

        new(30, "NIN", "Ninja", new GaugePartDef[]
        {
            new("ninki", "Ninki", 6, new[] { C(0.75f, 0.40f, 0.95f) }, new[] { "Ninki" }, Half),
            new("kazematoi", "Kazematoi", 6, new[] { C(0.55f, 0.85f, 0.55f) }, new[] { "Kazematoi" })
        }, new[] { "JobHudNIN0", "JobHudNIN1", "JobHudNIN1v70" }, new GaugePartSnapshot[]
        {
            new("ninki", GaugePartKind.Bar, 60, 100, Text: "60"),
            new("kazematoi", GaugePartKind.Pips, 3, 5)
        }),

        new(34, "SAM", "Samurai", new GaugePartDef[]
        {
            new("kenki", "Kenki", 6, new[] { C(0.90f, 0.30f, 0.30f) }, new[] { "Kenki" }, new[] { 0.25f, 0.5f }),
            new("sen", "Sen", 6, new[] { C(0.45f, 0.75f, 1f), C(0.90f, 0.90f, 0.95f), C(0.95f, 0.45f, 0.50f) }, new[] { "Setsu", "Getsu", "Ka" }),
            new("meditation", "Meditation", 6, new[] { C(1f, 0.75f, 0.30f) }, new[] { "Meditation" })
        }, new[] { "JobHudSAM0", "JobHudSAM1" }, new GaugePartSnapshot[]
        {
            new("kenki", GaugePartKind.Bar, 55, 100, Text: "55"),
            new("sen", GaugePartKind.Slots, 0, 3, SlotColors: new[] { 0, 1, -1 }),
            new("meditation", GaugePartKind.Pips, 2, 3)
        }),

        new(39, "RPR", "Reaper", new GaugePartDef[]
        {
            new("soul", "Soul", 6, new[] { C(0.90f, 0.35f, 0.60f) }, new[] { "Soul" }, Half),
            new("shroud", "Shroud", 6, new[] { C(0.45f, 0.55f, 0.95f), C(0.70f, 0.45f, 0.95f) }, new[] { "Shroud", "Lemure Shroud (enshrouded)" }, Half)
        }, new[] { "JobHudRRP0", "JobHudRRP1" }, new GaugePartSnapshot[]
        {
            new("soul", GaugePartKind.Bar, 50, 100, Text: "50"),
            new("shroud", GaugePartKind.Bar, 70, 100, Text: "70")
        }),

        new(41, "VPR", "Viper", new GaugePartDef[]
        {
            new("coil", "Rattling Coil", 6, new[] { C(0.35f, 0.85f, 0.55f) }, new[] { "Rattling Coil" }),
            new("offering", "Serpent Offering", 6, new[] { C(0.30f, 0.75f, 0.50f), C(1f, 0.70f, 0.30f) }, new[] { "Serpent Offering", "Anguine Tribute (reawakened)" }, Half)
        }, new[] { "JobHudRDB0", "JobHudRDB1" }, new GaugePartSnapshot[]
        {
            new("coil", GaugePartKind.Pips, 2, 3),
            new("offering", GaugePartKind.Bar, 60, 100, Text: "60")
        }),

        new(23, "BRD", "Bard", new GaugePartDef[]
        {
            new("song", "Song", 6, new[] { C(0.95f, 0.85f, 0.35f), C(0.70f, 0.45f, 0.95f), C(1f, 0.55f, 0.30f), C(0.30f, 0.30f, 0.35f) }, new[] { "Wanderer's Minuet", "Mage's Ballad", "Army's Paeon", "No song" }),
            new("repertoire", "Repertoire", 6, new[] { C(0.95f, 0.85f, 0.35f), C(0.70f, 0.45f, 0.95f), C(1f, 0.55f, 0.30f) }, new[] { "Wanderer's Minuet", "Mage's Ballad", "Army's Paeon" }),
            new("soulvoice", "Soul Voice", 6, new[] { C(0.95f, 0.80f, 0.40f) }, new[] { "Soul Voice" }, new[] { 0.2f, 0.8f })
        }, new[] { "JobHudBRD0" }, new GaugePartSnapshot[]
        {
            new("song", GaugePartKind.Bar, 30000, 45000, Text: "30"),
            new("repertoire", GaugePartKind.Pips, 2, 3),
            new("soulvoice", GaugePartKind.Bar, 40, 100, Text: "40")
        }),

        new(31, "MCH", "Machinist", new GaugePartDef[]
        {
            new("heat", "Heat", 6, new[] { C(1f, 0.50f, 0.20f), C(1f, 0.25f, 0.15f) }, new[] { "Heat", "Overheated (time left)" }, Half),
            new("battery", "Battery", 6, new[] { C(0.40f, 0.75f, 1f), C(0.55f, 0.90f, 1f) }, new[] { "Battery", "Automaton Queen (time left)" }, Half)
        }, new[] { "JobHudMCH0" }, new GaugePartSnapshot[]
        {
            new("heat", GaugePartKind.Bar, 60, 100, Text: "60"),
            new("battery", GaugePartKind.Bar, 40, 100, Text: "40")
        }),

        new(38, "DNC", "Dancer", new GaugePartDef[]
        {
            new("feathers", "Feathers and steps", 6, new[] { C(0.55f, 0.85f, 0.55f), C(0.90f, 0.35f, 0.30f), C(0.40f, 0.60f, 1f), C(0.45f, 0.85f, 0.50f), C(1f, 0.85f, 0.35f) }, new[] { "Feathers", "Emboite", "Entrechat", "Jete", "Pirouette" }),
            new("esprit", "Esprit", 6, new[] { C(1f, 0.55f, 0.75f) }, new[] { "Esprit" }, Half)
        }, new[] { "JobHudDNC0", "JobHudDNC1" }, new GaugePartSnapshot[]
        {
            new("feathers", GaugePartKind.Pips, 3, 4),
            new("esprit", GaugePartKind.Bar, 60, 100, Text: "60")
        }),

        new(25, "BLM", "Black Mage", new GaugePartDef[]
        {
            new("element", "Astral Fire / Umbral Ice", 6, new[] { C(0.95f, 0.40f, 0.25f), C(0.45f, 0.70f, 1f), C(0.30f, 0.30f, 0.40f) }, new[] { "Astral Fire", "Umbral Ice", "None" }),
            new("hearts", "Umbral Hearts", 6, new[] { C(0.50f, 0.80f, 1f) }, new[] { "Umbral Hearts" }),
            new("polyglot", "Polyglot", 6, new[] { C(0.85f, 0.55f, 1f) }, new[] { "Polyglot" }),
            new("soul", "Astral Soul", 6, new[] { C(1f, 0.55f, 0.20f) }, new[] { "Astral Soul" })
        }, new[] { "JobHudBLM0", "JobHudBLM1" }, new GaugePartSnapshot[]
        {
            new("element", GaugePartKind.Pips, 3, 3, Text: "9"),
            new("hearts", GaugePartKind.Pips, 2, 3),
            new("polyglot", GaugePartKind.Pips, 1, 3, 0.5f),
            new("soul", GaugePartKind.Pips, 4, 6)
        }),

        new(27, "SMN", "Summoner", new GaugePartDef[]
        {
            new("aetherflow", "Aetherflow", 6, new[] { C(1f, 0.60f, 0.35f) }, new[] { "Aetherflow" }),
            new("primals", "Primals ready", 6, new[] { C(0.95f, 0.40f, 0.30f), C(1f, 0.80f, 0.35f), C(0.50f, 0.90f, 0.55f) }, new[] { "Ifrit", "Titan", "Garuda" }),
            new("trance", "Trance", 6, new[] { C(0.95f, 0.40f, 0.30f), C(1f, 0.80f, 0.35f), C(0.50f, 0.90f, 0.55f), C(0.50f, 0.70f, 1f) }, new[] { "Ifrit", "Titan", "Garuda", "Demi-summon" })
        }, new[] { "JobHudSMN0", "JobHudSMN1" }, new GaugePartSnapshot[]
        {
            new("aetherflow", GaugePartKind.Pips, 1, 2),
            new("primals", GaugePartKind.Slots, 0, 3, SlotColors: new[] { 0, 1, -1 }),
            new("trance", GaugePartKind.Bar, 18000, 30000, Text: "18")
        }),

        new(42, "PCT", "Pictomancer", new GaugePartDef[]
        {
            new("palette", "Palette", 6, new[] { C(0.95f, 0.55f, 0.80f) }, new[] { "Palette" }, Half),
            new("paint", "Paint", 6, new[] { C(0.55f, 0.80f, 1f) }, new[] { "Paint" }),
            new("canvas", "Motifs and portraits", 6, new[] { C(1f, 0.70f, 0.80f), C(1f, 0.65f, 0.30f), C(0.50f, 0.80f, 1f), C(1f, 0.90f, 0.50f), C(0.70f, 0.90f, 0.70f) }, new[] { "Creature", "Weapon", "Landscape", "Moogle portrait", "Madeen portrait" })
        }, new[] { "JobHudRPM0", "JobHudRPM1" }, new GaugePartSnapshot[]
        {
            new("palette", GaugePartKind.Bar, 60, 100, Text: "60"),
            new("paint", GaugePartKind.Pips, 3, 5),
            new("canvas", GaugePartKind.Slots, 0, 5, SlotColors: new[] { 0, -1, 2, -1, -1 })
        }),
    };

    public static readonly IReadOnlyDictionary<uint, ExtraGaugeDef> ByJob = All.ToDictionary(def => def.JobId);

    /// <summary>The abbreviation of the job each window of the game belongs to (the windows that are hidden when the gauge of the job is on).</summary>
    public static readonly IReadOnlyDictionary<string, string> JobOfWindow = All
        .SelectMany(def => def.NativeWindows.Select(window => (window, def.Abbr)))
        .ToDictionary(pair => pair.window, pair => pair.Abbr);

    public static readonly string[] NativeWindows = JobOfWindow.Keys.ToArray();
}
