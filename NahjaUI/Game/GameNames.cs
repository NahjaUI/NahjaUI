using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace NahjaUI.Game;

/// <summary>
/// Turns ids into readable names (statuses, jobs) using the game's own data.
/// Results are cached, so each id is looked up only once.
/// </summary>
internal static class GameNames
{
    private static readonly Dictionary<uint, string> StatusNames = new();
    private static readonly Dictionary<uint, string> JobNames = new();

    public static string StatusName(uint statusId)
    {
        if (StatusNames.TryGetValue(statusId, out var cached))
            return cached;

        var name = string.Empty;
        if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(statusId, out var row))
            name = row.Name.ToString();

        if (string.IsNullOrEmpty(name))
            name = $"Status {statusId}";

        StatusNames[statusId] = name;
        return name;
    }

    public static string JobAbbreviation(uint jobId)
    {
        if (JobNames.TryGetValue(jobId, out var cached))
            return cached;

        var name = string.Empty;
        if (Plugin.DataManager.GetExcelSheet<ClassJob>().TryGetRow(jobId, out var row))
            name = row.Abbreviation.ToString();

        if (string.IsNullOrEmpty(name))
            name = "???";

        JobNames[jobId] = name;
        return name;
    }
}
