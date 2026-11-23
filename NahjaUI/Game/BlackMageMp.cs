using System;
using Dalamud.Game.ClientState.JobGauge.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using NahjaUI.Modules.Trackers;

namespace NahjaUI.Game;

/// <summary>What the MP bar of the Black Mage says while in Astral Fire.</summary>
internal enum BlackMageMpState
{
    /// <summary>The MP is enough for Fire (or there is no Astral Fire).</summary>
    Normal,

    /// <summary>Not enough for Fire any more, but enough for Flare: time for the finisher.</summary>
    Warning,

    /// <summary>Not enough even for Flare: time for ice.</summary>
    Empty
}

/// <summary>
/// The state of the MP of a Black Mage in Astral Fire, read from the game: the cost that the game itself gives for Fire (the filler) and Flare (the finisher)
/// right now, with Astral Fire and the Umbral Hearts already counted, against the MP of the player. It is not "can the action be used": that is false during
/// the recast of the global cooldown and the bar would flicker with every spell. The two reference actions are the same at every level
/// (Fire costs what Fire IV costs and Flare what Despair costs). Below the level of Flare there are only two states.
/// </summary>
internal static class BlackMageMp
{
    private const uint BlackMageId = 25;
    private const uint ThaumaturgeId = 7;

    private static uint fireId;
    private static uint flareId;
    private static bool idsFound;

    private static long checkedAt;
    private static BlackMageMpState cached;

    /// <summary>The cost the game gave for Fire and Flare the last time (0 when it was not asked). Only for the line of the page.</summary>
    public static int LastFireCost { get; private set; }

    public static int LastFlareCost { get; private set; }

    /// <summary>The Black Mage page was drawn a moment ago: the MP bar shows the three states one after the other.</summary>
    public static long PreviewAt { get; set; }

    public static bool IsPreviewing => Environment.TickCount64 - PreviewAt < 400;

    /// <summary>The state shown by the preview of the page: normal, warning and empty, one after the other.</summary>
    public static BlackMageMpState PreviewState => (BlackMageMpState)(Environment.TickCount64 / 1500 % 3);

    /// <summary>The preview also moves the fill of the bar, so each state is seen with a different amount of MP.</summary>
    public static float PreviewFraction => PreviewState switch
    {
        BlackMageMpState.Normal => 0.6f,
        BlackMageMpState.Warning => 0.18f,
        _ => 0.05f
    };

    public static BlackMageMpState Current(UnitSnapshot? player)
    {
        if (player is null || player.JobId is not (BlackMageId or ThaumaturgeId))
            return BlackMageMpState.Normal;

        var now = Environment.TickCount64;
        if (now - checkedAt < 100)
            return cached;

        checkedAt = now;
        cached = Compute(player);
        return cached;
    }

    private static BlackMageMpState Compute(UnitSnapshot player)
    {
        try
        {
            if (!Plugin.JobGauges.Get<BLMGauge>().InAstralFire)
                return BlackMageMpState.Normal;

            if (!idsFound)
            {
                fireId = TrackerPresets.FindAction("Fire", BlackMageId);
                flareId = TrackerPresets.FindAction("Flare", BlackMageId);
                idsFound = true;
            }

            if (fireId == 0)
                return BlackMageMpState.Normal;

            var fireCost = Cost(fireId);
            LastFireCost = fireCost;

            // If the game gives nothing, the bar keeps its color.
            if (fireCost <= 0 || player.Mp >= fireCost)
                return BlackMageMpState.Normal;

            // Without Flare (before level 50, or the sync lowers the level) there are only two states.
            if (flareId == 0 || GameNames.ActionLevel(flareId) > player.Level || !GameNames.ActionUsableBy(player.JobId, flareId))
                return BlackMageMpState.Empty;

            var flareCost = Cost(flareId);
            LastFlareCost = flareCost;
            return flareCost <= 0 || player.Mp >= flareCost ? BlackMageMpState.Warning : BlackMageMpState.Empty;
        }
        catch
        {
            return BlackMageMpState.Normal;
        }
    }

    // The MP that the game asks for this action now.
    private static int Cost(uint actionId) => ActionManager.GetActionCost(ActionType.Action, actionId, 0, 0, 0, 0);
}
