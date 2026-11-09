using System;
using System.Numerics;

namespace NahjaUI.Modules.HudBars;

/// <summary>The role of a job, as the game groups them: tank, healer, damage. Crafters, gatherers and unknown jobs have none.</summary>
internal enum JobRole
{
    None,
    Tank,
    Healer,
    Damage
}

/// <summary>Which role a job has, and the colour of each role (set in Appearance, shared by every frame).</summary>
internal static class JobRoles
{
    // Classes and jobs: Gladiator, Paladin, Marauder, Warrior, Dark Knight, Gunbreaker.
    private static readonly uint[] Tanks = { 1, 19, 3, 21, 32, 37 };

    // Conjurer, White Mage, Scholar, Astrologian, Sage. (Arcanist is a damage class.)
    private static readonly uint[] Healers = { 6, 24, 28, 33, 40 };

    public static JobRole RoleOf(uint jobId)
    {
        if (jobId == 0)
            return JobRole.None;

        if (Array.IndexOf(Tanks, jobId) >= 0)
            return JobRole.Tank;

        if (Array.IndexOf(Healers, jobId) >= 0)
            return JobRole.Healer;

        // 8 to 18 are the crafters and the gatherers.
        if (jobId >= 8 && jobId <= 18)
            return JobRole.None;

        return JobRole.Damage;
    }

    /// <summary>The colour of the role of a job; crafters and gatherers have their own grey; the fallback is only for an unknown job (0).</summary>
    public static Vector4 Color(HudBarsSettings frames, uint jobId, Vector4 fallback) => RoleOf(jobId) switch
    {
        // Carpenter to Culinarian (8 to 15) and Miner, Botanist, Fisher (16 to 18).
        JobRole.None when jobId >= 8 && jobId <= 15 => frames.CrafterColor,
        JobRole.None when jobId >= 16 && jobId <= 18 => frames.GathererColor,
        JobRole.Tank => frames.TankColor,
        JobRole.Healer => frames.HealerColor,
        JobRole.Damage => frames.DamageColor,
        _ => jobId == 0 ? fallback : frames.NoRoleColor
    };
}
