using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Modules.Filters;

/// <summary>
/// The "Filters" page: named lists of enhancements and enfeeblements. A filter is made once here and used from the rows of icons next to the
/// bars (HUD bars, party list, nameplates) and from the triggers of the trackers.
/// </summary>
internal sealed class FiltersEditor
{
    private readonly Plugin plugin;

    private string? selectedId;
    private string search = string.Empty;
    private string searchApplied = string.Empty;
    private IReadOnlyList<StatusSearchResult> results = Array.Empty<StatusSearchResult>();
    private string? confirmDelete;

    public FiltersEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var settings = plugin.Configuration.Filters;

        var selected = settings.Find(selectedId) ?? (settings.Filters.Count > 0 ? settings.Filters[0] : null);
        selectedId = selected?.Id;

        DrawPicker(settings, selected);

        if (selected is null)
            return;

        DrawName(selected);
        DrawDefaults(selected);
        DrawList(selected);
        DrawAdd(selected);
    }

    private void DrawPicker(FiltersSettings settings, StatusFilter? selected)
    {
        Widgets.Card("##card_filters_pick", 4, () =>
        {
            Widgets.SectionLabel("Filters");

            if (ImGui.BeginCombo("Filter", selected?.Name ?? "(none)", ImGuiComboFlags.None))
            {
                foreach (var filter in settings.Filters)
                {
                    if (ImGui.Selectable($"{filter.Name}##pick_{filter.Id}", filter.Id == selected?.Id))
                        selectedId = filter.Id;
                }

                ImGui.EndCombo();
            }

            if (ImGui.Button("New filter"))
            {
                var fresh = new StatusFilter();
                settings.Filters.Add(fresh);
                selectedId = fresh.Id;
                plugin.RequestSave();
            }

            ImGui.SameLine();
            if (ImGui.Button("Restore the default filters"))
            {
                DefensiveFilters.RestoreMissing(settings);
                plugin.RequestSave();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Creates again the default filters that are missing. The ones you have are not touched.");

            if (selected is null)
                return;

            ImGui.SameLine();

            if (confirmDelete == selected.Id)
            {
                if (ImGui.Button("Click again to delete this filter"))
                {
                    settings.Filters.Remove(selected);
                    selectedId = null;
                    confirmDelete = null;
                    plugin.RequestSave();
                }
            }
            else if (ImGui.Button("Delete filter"))
            {
                confirmDelete = selected.Id;
            }
        });
    }

    private void DrawName(StatusFilter filter)
    {
        Widgets.Card("##card_filters_name", 2, () =>
        {
            var name = filter.Name;
            if (ImGui.InputText("Name", ref name, 48))
            {
                filter.Name = name;
                plugin.RequestSave();
            }
        });
    }

    // The four defensive filters can be filled again with their default statuses; the names the game did not know are listed.
    private void DrawDefaults(StatusFilter filter)
    {
        var isDefensive = Array.Exists(DefensiveFilters.All, definition => definition.Id == filter.Id);
        var missing = DefensiveFilters.LastMissing;
        if (!isDefensive && missing.Count == 0)
            return;

        Widgets.Card("##card_filters_defaults", 4 + missing.Count, () =>
        {
            Widgets.SectionLabel("Default list");

            if (isDefensive && ImGui.Button("Add the default statuses again"))
            {
                DefensiveFilters.Restore(plugin.Configuration.Filters, filter.Id);
                plugin.RequestSave();
            }

            if (missing.Count > 0)
            {
                ImGui.TextColored(Theme.TextMuted, "The game has no status with these names (check them in game):");
                foreach (var line in missing)
                    ImGui.TextColored(Theme.TextMuted, $"  {line}");
            }
        });
    }

    private void DrawList(StatusFilter filter)
    {
        Widgets.Card("##card_filters_list", 4 + filter.StatusIds.Count, () =>
        {
            Widgets.SectionLabel("Statuses in this filter");

            if (filter.StatusIds.Count == 0)
                ImGui.TextColored(Theme.TextMuted, "Nothing in the list yet.");

            uint? toRemove = null;
            foreach (var id in filter.StatusIds)
            {
                var kind = GameNames.StatusCategory(id) switch
                {
                    1 => "enhancement",
                    2 => "enfeeblement",
                    _ => string.Empty
                };

                ImGui.Text($"{GameNames.StatusName(id)} ({id})   {kind}");
                ImGui.SameLine();

                if (ImGui.SmallButton($"Remove##rm_{id}"))
                    toRemove = id;
            }

            if (toRemove is not null)
            {
                filter.StatusIds.Remove(toRemove.Value);
                plugin.RequestSave();
            }
        });
    }

    private void DrawAdd(StatusFilter filter)
    {
        if (search != searchApplied)
        {
            searchApplied = search;
            results = search.Trim().Length >= 3
                ? GameNames.SearchStatuses(search.Trim(), 10)
                : Array.Empty<StatusSearchResult>();
        }

        Widgets.Card("##card_filters_add", 12 + results.Count, () =>
        {
            Widgets.SectionLabel("Add statuses");

            var text = search;
            if (ImGui.InputText("Search by name", ref text, 64))
                search = text;

            foreach (var result in results)
            {
                if (filter.StatusIds.Contains(result.Id))
                    continue;

                var kind = result.Category switch
                {
                    1 => "enhancement",
                    2 => "enfeeblement",
                    _ => string.Empty
                };

                if (ImGui.Selectable($"{result.Name} ({result.Id})   {kind}##res_{result.Id}"))
                {
                    filter.StatusIds.Add(result.Id);
                    plugin.RequestSave();
                }
            }

            var snapshot = plugin.GameData.Current;
            DrawNow("On you right now (click to add):", snapshot.IsLoggedIn ? snapshot.Player?.Statuses : null, filter, "me");
            DrawNow("On your target right now (click to add):", snapshot.IsLoggedIn ? snapshot.Target?.Statuses : null, filter, "tg");
        });
    }

    // The statuses a unit has now, to add with one click (the easiest way to find a stun: use it and look).
    private void DrawNow(string title, IReadOnlyList<StatusSnapshot>? statuses, StatusFilter filter, string tag)
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, title);

        var seen = 0;
        if (statuses is not null)
        {
            foreach (var status in statuses)
            {
                if (seen >= 10)
                    break;

                if (filter.StatusIds.Contains(status.Id) || GameNames.StatusIconId(status.Id) == 0)
                    continue;

                seen++;
                if (ImGui.Selectable($"{GameNames.StatusName(status.Id)} ({status.Id})##now_{tag}_{status.Id}"))
                {
                    filter.StatusIds.Add(status.Id);
                    plugin.RequestSave();
                }
            }
        }

        if (seen == 0)
            ImGui.TextColored(Theme.TextMuted, "  Nothing new at the moment.");
    }
}
