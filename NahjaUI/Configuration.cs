using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

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

    // Saved position and size of every element, keyed by element id (for example "demo.a").
    public Dictionary<string, ElementLayout> Layouts { get; set; } = new();

    // Shows the two temporary test boxes (removed in H4).
    public bool ShowTestElements { get; set; } = true;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
