using System;
using System.IO;

namespace NahjaUI.Core.Profiles;

/// <summary>
/// The interface a new install starts from: the settings file of the author (Paladin, Tank Role and General trackers, positions,
/// bars, frames, colors, filters, every option) travels inside the plugin as "DefaultConfiguration.json". It is written as the
/// settings file only when there is none yet, so nobody who already has settings is touched.
/// </summary>
internal static class DefaultConfiguration
{
    private const string ResourceName = "DefaultConfiguration.json";

    /// <summary>Call once at startup, before the settings are read.</summary>
    public static void WriteIfMissing()
    {
        try
        {
            var file = Plugin.PluginInterface.ConfigFile;
            if (file.Exists)
                return;

            using var resource = typeof(DefaultConfiguration).Assembly.GetManifestResourceStream(ResourceName);
            if (resource is null)
            {
                Plugin.Log.Warning("NahjaUI: the default settings are not inside the plugin.");
                return;
            }

            file.Directory?.Create();
            using var output = File.Create(file.FullName);
            resource.CopyTo(output);
        }
        catch (Exception ex)
        {
            // Without them the plugin simply starts with its plain defaults.
            Plugin.Log.Error(ex, "NahjaUI: could not write the default settings.");
        }
    }
}
