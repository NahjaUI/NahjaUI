using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.Auras;
using NahjaUI.Modules.CastBars;
using NahjaUI.Modules.JobGauges;
using NahjaUI.Modules.EnemyList;
using NahjaUI.Modules.NamePlates;
using NahjaUI.Modules.Tooltip;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI;

[Serializable]
public class Configuration : IPluginConfiguration
{
    // Dalamud uses this number to tell config versions apart.
    // When the saved format changes in a way that needs conversion, bump it and add a migration (planned for H7).
    public int Version { get; set; } = 1;

    // Page selected in the sidebar of the main window.
    public string SelectedPage { get; set; } = "General";

    // The single accent colour of the theme.
    public Vector4 AccentColor { get; set; } = Theme.DefaultAccent;

    // Timers (auras, icons next to the bars) show tenths of a second only below this many seconds. 0 = never.
    public int TimerDecimalsBelowSeconds { get; set; } = 3;

    // Size of the text in the NahjaUI windows, in logical pixels (see OverlayFonts.MenuTextSizes).
    public int MenuTextSize { get; set; } = 15;

    // Saved position and size of every element, keyed by element id (for example "demo.a").
    public Dictionary<string, ElementLayout> Layouts { get; set; } = new();

    // The auras the user has created.
    public List<AuraDefinition> Auras { get; set; } = new();

    // The groups that line up auras automatically.
    public List<AuraGroupDefinition> AuraGroups { get; set; } = new();

    // The cast bars (yours and your target's).
    public CastBarsSettings CastBars { get; set; } = new();

    // The job gauges (White Mage for now).
    public JobGaugesSettings JobGauges { get; set; } = new();

    // The unit frames (the player's for now).
    public UnitFramesSettings UnitFrames { get; set; } = new();

    // The nameplates over the enemies (first step: life bar and name).
    public NamePlateSettings NamePlates { get; set; } = new();

    // The list of enemies that fight you.
    public EnemyListSettings EnemyList { get; set; } = new();

    // The box that describes a buff or debuff under the mouse.
    public TooltipSettings Tooltip { get; set; } = new();

    // The top level of the Auras explorer, in the order the user chose: groups and loose auras mixed.
    // Entries look like "group:<id>" or "aura:<id>". Auras inside a group are not listed here.
    public List<string> RootOrder { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
