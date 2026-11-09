using System;
using System.Collections.Generic;
using System.Linq;
using NahjaUI.Game;

namespace NahjaUI.Modules.Filters;

/// <summary>A named list of statuses (enhancements and enfeeblements) that can be used from many places: rows of icons next to the bars, nameplates and tracker triggers.</summary>
[Serializable]
public class StatusFilter
{
    /// <summary>Never shown. The places that use a filter remember this, so renaming the filter does not break them.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New filter";

    public List<uint> StatusIds { get; set; } = new();
}

[Serializable]
public class FiltersSettings
{
    /// <summary>The id of the starter "Crowd control" filter: the crowd control row of the nameplates starts with it.</summary>
    public const string CrowdControlId = "crowd-control";

    public List<StatusFilter> Filters { get; set; } = new();

    /// <summary>True once the starter filters have been created, so a filter the user deletes does not come back.</summary>
    public bool StartersCreated { get; set; }

    public StatusFilter? Find(string? id) => string.IsNullOrEmpty(id) ? null : Filters.FirstOrDefault(filter => filter.Id == id);

    /// <summary>Is this status in any of the filters?</summary>
    public bool Contains(IReadOnlyList<string> filterIds, uint statusId)
    {
        if (filterIds.Count == 0)
            return false;

        foreach (var id in filterIds)
        {
            var filter = Find(id);
            if (filter is not null && filter.StatusIds.Contains(statusId))
                return true;
        }

        return false;
    }

    /// <summary>Is this status in any of the filters? A filter in "onlyMineIds" only counts when the status is yours.</summary>
    public bool Contains(IReadOnlyList<string> filterIds, uint statusId, IReadOnlyList<string> onlyMineIds, bool isMine)
        => RankOf(filterIds, statusId, onlyMineIds, isMine) != int.MaxValue;

    /// <summary>The place (0, 1, 2...) of the first filter of the list that has this status, or int.MaxValue when none has it.</summary>
    public int RankOf(IReadOnlyList<string> filterIds, uint statusId, IReadOnlyList<string>? onlyMineIds = null, bool isMine = true)
    {
        for (var i = 0; i < filterIds.Count; i++)
        {
            var filter = Find(filterIds[i]);
            if (filter is null || !filter.StatusIds.Contains(statusId))
                continue;

            if (!isMine && onlyMineIds is not null && onlyMineIds.Contains(filter.Id))
                continue;

            return i;
        }

        return int.MaxValue;
    }

    /// <summary>The starting filters were created (once; if the user deletes one, it does not come back by itself).</summary>
    public bool DefensivesCreated { get; set; }

    /// <summary>The first time: the filters the interface starts with. Crowd control has the one stun of the game; the others wait for the user to fill them.</summary>
    public void EnsureStarters()
    {
        if (StartersCreated)
            return;

        StartersCreated = true;

        var crowdControl = new StatusFilter { Id = CrowdControlId, Name = "Crowd control" };
        if (GameNames.StatusName(2) == "Stun")
            crowdControl.StatusIds.Add(2);

        Filters.Add(crowdControl);
        Filters.Add(new StatusFilter { Name = "Defensives" });
        Filters.Add(new StatusFilter { Name = "Damage windows" });
    }
}
