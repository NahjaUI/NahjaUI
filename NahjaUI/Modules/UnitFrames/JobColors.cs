using System.Collections.Generic;
using System.Numerics;

namespace NahjaUI.Modules.UnitFrames;

/// <summary>
/// The colour of each job, for the HP bar when "class color" is chosen.
/// The game has no official colour per job (it only colours by role). These are the colours FFLogs uses for the jobs,
/// as transcribed in a public list (stocky37's "fflogs-colours.yaml", January 2022) for the jobs that existed then,
/// plus Viper, Pictomancer and Blue Mage as given by the owner from the current FFLogs. Every one can be changed in the
/// settings; what the user changes is stored in
/// <see cref="UnitFramesSettings.JobColorOverrides"/> and these stay as the defaults.
/// </summary>
internal static class JobColors
{
    /// <summary>The jobs in the order of the game's role groups: tanks, healers, melee, ranged, casters.</summary>
    public static readonly uint[] Order = { 19, 21, 32, 37, 24, 28, 33, 40, 20, 22, 30, 34, 39, 41, 23, 31, 38, 25, 27, 35, 42, 36 };

    private static readonly Dictionary<uint, Vector4> Defaults = new()
    {
        [19] = Hex(0xA8D2E6), // Paladin
        [21] = Hex(0xCF2621), // Warrior
        [32] = Hex(0xD126CC), // Dark Knight
        [37] = Hex(0x998D50), // Gunbreaker
        [24] = Hex(0xFFF0DC), // White Mage
        [28] = Hex(0x8657FF), // Scholar
        [33] = Hex(0xFFE74A), // Astrologian
        [40] = Hex(0x80A0F0), // Sage
        [20] = Hex(0xD69C00), // Monk
        [22] = Hex(0x4164CD), // Dragoon
        [30] = Hex(0xAF1964), // Ninja
        [34] = Hex(0xE46D04), // Samurai
        [39] = Hex(0x965A90), // Reaper
        [41] = Hex(0x108210), // Viper (from the current FFLogs, as given by the owner)
        [23] = Hex(0x91BA5E), // Bard
        [31] = Hex(0x6EE1D6), // Machinist
        [38] = Hex(0xE2B0AF), // Dancer
        [25] = Hex(0xA579D6), // Black Mage
        [27] = Hex(0x2D9B78), // Summoner
        [35] = Hex(0xE87B7B), // Red Mage
        [42] = Hex(0xFF66CC), // Pictomancer (from the current FFLogs, as given by the owner)
        [36] = Hex(0x00AEEF)  // Blue Mage (as given by the owner)
    };

    // The classes before a job use the colour of that job.
    private static readonly Dictionary<uint, uint> ClassToJob = new()
    {
        [1] = 19,  // Gladiator
        [2] = 20,  // Pugilist
        [3] = 21,  // Marauder
        [4] = 22,  // Lancer
        [5] = 23,  // Archer
        [6] = 24,  // Conjurer
        [7] = 25,  // Thaumaturge
        [26] = 27, // Arcanist
        [29] = 30  // Rogue
    };

    /// <summary>The built-in colour of a job, or null when it has none.</summary>
    public static Vector4? Default(uint jobId)
    {
        if (ClassToJob.TryGetValue(jobId, out var job))
            jobId = job;

        return Defaults.TryGetValue(jobId, out var color) ? color : null;
    }

    /// <summary>The colour to use for a job: the one the user chose, else the built-in one, else the given fallback.</summary>
    public static Vector4 Get(IReadOnlyDictionary<uint, Vector4> overrides, uint jobId, Vector4 fallback)
    {
        if (overrides.TryGetValue(jobId, out var chosen))
            return chosen;

        return Default(jobId) ?? fallback;
    }

    // 0xRRGGBB as a colour.
    private static Vector4 Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}
