using NahjaUI.Modules.JobGauges;

namespace NahjaUI.Core;

/// <summary>The three starting points of the setup window.</summary>
internal enum SetupPackage
{
    Essential,
    Full,
    TrackersOnly
}

/// <summary>
/// What each starting point of the setup window turns on and off. It only flips the main switches of the modules (the same ones as in
/// the menu); nothing is deleted, so anything can be turned back on later from the pages of the menu. It writes both ways (on and
/// off), so choosing another package after one always gives that package, whatever was chosen before.
/// </summary>
internal static class SetupPackages
{
    public static string Name(SetupPackage package) => package switch
    {
        SetupPackage.Essential => "Essential",
        SetupPackage.TrackersOnly => "Trackers only",
        _ => "Complete"
    };

    public static string Summary(SetupPackage package) => package switch
    {
        SetupPackage.Essential => "Your HP and MP bars, target, focus, party list and cast bars. Nothing else.",
        SetupPackage.TrackersOnly => "Trackers and job gauges only. The game keeps its own bars and lists.",
        _ => "Everything: bars, party and alliance lists, enemy list, nameplates, status icons, tooltips, trackers and job gauges."
    };

    public static void Apply(Configuration config, SetupPackage package)
    {
        var bars = package is SetupPackage.Essential or SetupPackage.Full;
        var jobParts = package is SetupPackage.Full or SetupPackage.TrackersOnly;
        var everything = package == SetupPackage.Full;

        // HP bars and cast bars.
        var hud = config.HudBars;
        hud.Player.Enabled = bars;
        hud.Target.Enabled = bars;
        hud.TargetOfTarget.Enabled = bars;
        hud.Focus.Enabled = bars;
        hud.Party.Look.Enabled = bars;
        config.ProgressBars.Player.Enabled = bars;
        config.ProgressBars.Target.Enabled = bars;

        // The rest of the interface.
        hud.Alliance.Enabled = everything;
        config.EnmityList.Enabled = everything;
        config.StatusEffects.Enabled = everything;
        config.Tooltip.Enabled = everything;

        var plates = config.NamePlates;
        plates.Self.Active = everything;
        plates.Party.Active = everything;
        plates.OtherPlayers.Active = everything;
        plates.FriendlyNpcs.Active = everything;
        plates.EnemyNpcs.Active = everything;
        plates.EnemyPlayers.Active = everything;
        plates.OtherPlates.Active = everything;

        // Trackers (every tracker belongs to a group, and a group that is off hides its trackers).
        foreach (var group in config.TrackerGroups)
            group.Enabled = jobParts;

        // Job gauges. The Limit Break and the Frontline gauge are not touched: they are not part of a job.
        var gauges = config.JobGauges;
        gauges.WhiteMage.Enabled = jobParts;
        gauges.Warrior.Enabled = jobParts;
        gauges.Paladin.Enabled = jobParts;
        gauges.DarkKnight.Enabled = jobParts;
        gauges.Gunbreaker.Enabled = jobParts;
        gauges.RedMage.Enabled = jobParts;
        gauges.Scholar.Enabled = jobParts;
        gauges.Astrologian.Enabled = jobParts;
        gauges.Sage.Enabled = jobParts;
        gauges.BlackMage.ShowThunderheadAura = jobParts;
        gauges.BlackMage.ShowFirestarterAura = jobParts;

        foreach (var def in ExtraGauges.All)
            gauges.Extra.For(def.Abbr).Enabled = jobParts;
    }
}
