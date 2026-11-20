using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.Trackers;
using NahjaUI.Modules.ProgressBars;
using NahjaUI.Modules.JobGauges;
using NahjaUI.Modules.EnmityList;
using NahjaUI.Modules.Filters;
using NahjaUI.Modules.NamePlates;
using NahjaUI.Modules.StatusEffects;
using NahjaUI.Modules.Tooltip;
using NahjaUI.Modules.HudBars;

namespace NahjaUI;

[Serializable]
public class Configuration : IPluginConfiguration
{
    // Dalamud uses this number to tell config versions apart.
    // When the saved format changes in a way that needs conversion, raise CurrentVersion and add a step in ConfigMigrations.
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>The plugin version that saved these settings: a change means an update, and a copy of the file is kept first.</summary>
    public string LastPluginVersion { get; set; } = string.Empty;

    /// <summary>The game icon of the NahjaUI entry in the server info bar (a name of the game's icon list, "Mentor" is the crown).</summary>
    public string ServerBarIcon { get; set; } = "Tank";

    /// <summary>True once the user has picked an icon: until then the default ("Tank", the shield of the tank role) is used, whatever an earlier build saved.</summary>
    public bool ServerBarIconChosen { get; set; }

    /// <summary>Show the NahjaUI entry in the game's server info bar (top right). /nui always works.</summary>
    public bool ShowInServerBar { get; set; } = true;

    [Newtonsoft.Json.JsonIgnore]
    public string ServerBarIconNow => "Tank";

    /// <summary>Streamer mode: the names of player characters are replaced by names of Final Fantasy characters in everything NahjaUI draws.</summary>
    public bool StreamerMode { get; set; }

    /// <summary>Which set of invented names streamer mode gives. A new number (the "Change names" button) gives everyone a different name.</summary>
    public int StreamerSeed { get; set; }

    // Page selected in the sidebar of the main window.
    public string SelectedPage { get; set; } = "General";

    // The ring that marks your target on the enmity list, the nameplates and the party list (one color for all of them).
    public Vector4 TargetRingColor { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>The colors shared by the whole interface (background, border, text, shield, MP). Places with a "Custom color" turned on keep their own.</summary>
    public GlobalColors Colors { get; set; } = new();

    // Ids of the cards of the menu that the user has folded (only the title shows).
    public List<string> CollapsedCards { get; set; } = new();

    // Size of the text in the NahjaUI windows, in logical pixels (see OverlayFonts.MenuTextSizes).
    public int MenuTextSize { get; set; } = 15;

    // Appearance page: the slow animated background of the setup window and the menu (a plain one when off).
    public bool AnimatedBackground { get; set; } = true;

    // Appearance page: how much gradient the filled part of bars and pips has, from 0 (flat color) to 200 (100 is the default, see BarGradient).
    public int BarGradientPercent { get; set; } = 100;

    // Saved position and size of every element, keyed by element id (for example "demo.a").
    public Dictionary<string, ElementLayout> Layouts { get; set; } = new();

    // The position every job shows by default, for the elements that can have one of their own for a job (and the groups that share one):
    // element id (or the name of the shared position, "Rotation") to (0 = PvE, 1000 = PvP for the elements with their own PvP position, to layout).
    // The live position of the job being played is in Layouts.
    public Dictionary<string, Dictionary<uint, ElementLayout>> CommonLayouts { get; set; } = new();

    // The jobs that have a position of their own for an element: element id to (job id, or job id + 1000 in PvP, to layout).
    public Dictionary<string, Dictionary<uint, ElementLayout>> JobLayouts { get; set; } = new();

    // The trackers the user has created.
    public List<TrackerDefinition> Trackers { get; set; } = new();

    // The groups that line up trackers automatically.
    public List<TrackerGroupDefinition> TrackerGroups { get; set; } = new();

    // The progress bars (yours and your target's).
    public ProgressBarsSettings ProgressBars { get; set; } = new();

    // The job gauges (White Mage for now).
    public JobGaugesSettings JobGauges { get; set; } = new();

    // The HUD bars (the player's for now).
    public HudBarsSettings HudBars { get; set; } = new();

    // The enhancements and enfeeblements of the player.
    public StatusEffectsSettings StatusEffects { get; set; } = new();

    // The named lists of statuses (Filters page), used by the rows of icons, the nameplates and the tracker triggers.
    public FiltersSettings Filters { get; set; } = new();

    // The nameplates over the enemies (first step: HP bar and name).
    public NamePlateSettings NamePlates { get; set; } = new();

    // The list of enemies that fight you.
    public EnmityListSettings EnmityList { get; set; } = new();

    // The box that describes an enhancement or enfeeblement under the mouse.
    public TooltipSettings Tooltip { get; set; } = new();

    // True on a new install until the setup window is finished or skipped: it opens by itself while this is true.
    public bool SetupPending { get; set; }

    // Test: the Black Mage "effect is active" triggers got the white pulse (applied once, then it is the user's choice).
    public bool BlackMagePulseTestApplied { get; set; }

    // The top level of the Trackers explorer, in the order the user chose: groups and loose trackers mixed.
    // Entries look like "group:<id>" or "tracker:<id>". Trackers inside a group are not listed here.
    public List<string> RootOrder { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
