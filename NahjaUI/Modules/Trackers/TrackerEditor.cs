using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Modules.Trackers;

/// <summary>
/// The "Trackers" page of the main window. On the left, an explorer: groups and loose trackers mixed in any order,
/// groups that open and close, and drag and drop to reorder or to move trackers in and out of groups.
/// On the right, the editor of what is selected.
/// </summary>
internal sealed class TrackerEditor
{
    private const string DefaultName = "New tracker";
    private const string NoGroupLabel = "None (free position)";
    private const int MaxListedStatuses = 12;
    private const int MaxSearchResults = 10;
    private const int AbilityListRows = 8;

    // Sizes of the explorer rows, in logical pixels.
    private const float RowHeight = 34f;
    private const float RowGap = 2f;
    private const float MemberIndent = 30f;

    private readonly Plugin plugin;

    // At most one of these two is set: the thing being edited.
    private string? selectedTrackerId;
    private string? selectedGroupId;

    // The tracker or group waiting for a second click on its delete button.
    private string? pendingDeleteId;
    private TrackerRuntime? pendingDuplicate;

    // The name box that opens from the right-click menu of a tracker.
    private string? renamingTrackerId;
    private bool openRename;
    private bool focusRename;
    private TrackerRuntime? pendingRemove;
    private TrackerGroupRuntime? pendingDuplicateGroup;
    private TrackerGroupRuntime? pendingRemoveGroup;

    // What the last preset did (shown under the buttons of the explorer until it is dismissed).
    private string? presetReport;

    // Status search
    // The filter of the explorer: only the trackers that load on a job (the one you play, or the one you pick), or every tracker.
    private enum JobFilterMode
    {
        MyJob,
        AllJobs,
        PickedJob
    }

    // PvE trackers and PvP trackers never are the same ones: by default the list shows the kind of content you are in.
    private enum ContentFilterMode
    {
        Here,
        Pve,
        Pvp,
        Both
    }

    private ContentFilterMode contentFilter = ContentFilterMode.Here;

    private JobFilterMode jobFilter = JobFilterMode.MyJob;
    private uint pickedFilterJob;

    private string search = string.Empty;
    private string appliedSearch = string.Empty;
    private IReadOnlyList<StatusSearchResult> searchResults = Array.Empty<StatusSearchResult>();

    // Ability picker: the abilities of the current job (rebuilt when the job or level changes) and a name search.
    private uint abilitiesJobId;
    private int abilitiesLevel;
    private IReadOnlyList<ActionInfo> abilities = Array.Empty<ActionInfo>();
    private string actionSearch = string.Empty;
    private string appliedActionSearch = string.Empty;
    private IReadOnlyList<ActionInfo> actionResults = Array.Empty<ActionInfo>();

    // Explorer: the rows drawn this frame (used to find where a dragged row would land) and the drag in progress.
    private readonly List<RowRect> rows = new();
    private bool explorerHovered;
    private string? dragKey;
    private bool dragging;
    private Vector2 pressPosition;

    public TrackerEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    // ---------------------------------------------------------------- explorer data types

    // A row of the explorer, with its place on the screen. Parent is the group of a member row.
    private sealed record RowRect(string Key, TrackerGroupRuntime? Group, TrackerRuntime? Tracker, TrackerGroupRuntime? Parent, Vector2 Min, Vector2 Max)
    {
        public bool IsGroup => Group is not null;
    }

    private enum DropKind
    {
        None,
        RootBefore,
        RootAfter,
        IntoGroupEnd,
        IntoGroupBefore,
        IntoGroupAfter
    }

    // Where the dragged row would land. LineY and LineX are for the line that shows it.
    private readonly record struct DropTarget(
        DropKind Kind,
        string? TargetKey,
        TrackerGroupRuntime? Group,
        TrackerRuntime? Neighbour,
        float LineY,
        float LineX,
        RowRect? Highlight);

    // ---------------------------------------------------------------- layout of the page

    public void Draw()
    {
        var engine = plugin.TrackerSystem;

        if (pendingRemove is not null)
        {
            engine.Remove(pendingRemove);
            if (selectedTrackerId == pendingRemove.Definition.Id)
                selectedTrackerId = null;

            pendingRemove = null;
            pendingDeleteId = null;
            pendingDuplicate = null;
        }

        if (pendingRemoveGroup is not null)
        {
            if (selectedGroupId == pendingRemoveGroup.Definition.Id)
                selectedGroupId = null;

            engine.RemoveGroupWithTrackers(pendingRemoveGroup);
            pendingRemoveGroup = null;
            selectedTrackerId = selectedTrackerId is not null && engine.Trackers.Any(a => a.Definition.Id == selectedTrackerId) ? selectedTrackerId : null;
        }

        if (pendingDuplicateGroup is not null)
        {
            var groupCopy = engine.DuplicateGroup(pendingDuplicateGroup);
            pendingDuplicateGroup = null;
            SelectGroup(groupCopy.Definition.Id);
        }

        if (pendingDuplicate is not null)
        {
            var copy = engine.Duplicate(pendingDuplicate);
            pendingDuplicate = null;
            SelectTracker(copy.Definition.Id);
        }
        var snapshot = plugin.GameData.Current;
        var scale = ImGuiHelpers.GlobalScale;

        var available = ImGui.GetContentRegionAvail();

        // With room: explorer on the left, editor on the right. In a narrow window: one above the other.
        if (available.X >= 720f * scale)
        {
            var explorerWidth = Math.Clamp(available.X * 0.42f, 300f * scale, 440f * scale);

            using (var left = ImRaii.Child("##tracker_explorer_column", new Vector2(explorerWidth, 0f), false))
            {
                if (left.Success)
                    DrawExplorer(engine, ImGui.GetContentRegionAvail().Y - 4f * scale);
            }

            ImGui.SameLine();

            using (var right = ImRaii.Child("##tracker_editor_column", Vector2.Zero, false))
            {
                if (right.Success)
                    DrawEditor(engine, snapshot);
            }
        }
        else
        {
            DrawExplorer(engine, 320f * scale);
            DrawEditor(engine, snapshot);
        }
    }

    private void DrawEditor(TrackerEngine engine, GameSnapshot snapshot)
    {
        var group = engine.Groups.FirstOrDefault(g => g.Definition.Id == selectedGroupId);
        if (group is not null)
        {
            DrawGroupEditor(engine, group, snapshot);
            return;
        }

        var tracker = engine.Trackers.FirstOrDefault(r => r.Definition.Id == selectedTrackerId);
        if (tracker is null)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "Select a group or a tracker to edit it,");
            ImGui.TextColored(Theme.TextMuted, "or create a new one.");
            return;
        }

        var definition = tracker.Definition;

        DrawRules(definition, snapshot);
        DrawTrackerSettings(definition, tracker);
        DrawDelete(engine, tracker);
    }

    // ---------------------------------------------------------------- explorer

    // The card that holds the list. A height of 0 or less fills the space that is left.
    private void DrawExplorer(TrackerEngine engine, float height)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X - 16f * scale;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.Card))
        using (var card = ImRaii.Child("##tracker_explorer", new Vector2(width, Math.Max(0f, height)), true))
        {
            if (card.Success)
                DrawExplorerContent(engine);
        }

        ImGui.Spacing();
    }

    private void DrawExplorerContent(TrackerEngine engine)
    {
        var scale = ImGuiHelpers.GlobalScale;

        Widgets.SectionLabel("Groups and trackers");

        if (Widgets.PrimaryButton("New group"))
        {
            var created = engine.AddGroup();
            SelectGroup(created.Definition.Id);
        }

        ImGui.SameLine();

        if (ImGui.Button("New tracker"))
        {
            // A new tracker goes into the group that is selected, or into the group of the selected tracker.
            var target = SelectedGroupContext(engine);
            var created = engine.AddNew(target);

            // Looking at the trackers of one job: a loose new tracker loads on that job (it can be changed in its load conditions).
            // A tracker created inside a group keeps loading on every job, so a general group never ends up limited by mistake.
            var filterNow = JobFilterNow();
            if (target is null && filterNow is { } jobForNew && !created.Definition.Load.JobIds.Contains(jobForNew))
                created.Definition.Load.JobIds.Add(jobForNew);

            if (target is not null)
                target.Definition.Expanded = true;

            SelectTracker(created.Definition.Id);
        }

        ImGui.SameLine();

        if (ImGui.Button("Presets"))
            ImGui.OpenPopup("##tracker_presets");

        if (ImGui.BeginPopup("##tracker_presets"))
        {
            ImGui.TextColored(Theme.TextMuted, "Add the trackers of a job");

            // The preset of the job you are playing first, then the others in the order of the game's job list.
            var myPreset = TrackerPresets.NameForJob(plugin.GameData.Current.Player?.JobId ?? 0);
            var ordered = TrackerPresets.Names.OrderBy(name => name == myPreset ? 0 : 1).ToArray();

            foreach (var presetName in ordered)
            {
                if (ImGui.Selectable(presetName))
                    presetReport = TrackerPresets.Apply(engine, presetName);
            }

            ImGui.EndPopup();
        }

        if (openRename)
        {
            ImGui.OpenPopup("##rename_tracker");
            openRename = false;
            focusRename = true;
        }

        if (ImGui.BeginPopup("##rename_tracker"))
        {
            var renaming = engine.Trackers.FirstOrDefault(r => r.Definition.Id == renamingTrackerId)?.Definition;
            if (renaming is null)
            {
                ImGui.CloseCurrentPopup();
            }
            else
            {
                ImGui.TextColored(Theme.TextMuted, "Name of the tracker");

                if (focusRename)
                {
                    ImGui.SetKeyboardFocusHere();
                    focusRename = false;
                }

                var newName = renaming.Name;
                ImGui.SetNextItemWidth(240f * scale);
                if (ImGui.InputText("##rename_input", ref newName, 64))
                {
                    renaming.Name = newName;
                    plugin.RequestSave();
                }

                // Enter (or clicking away after typing) closes the box.
                if (ImGui.IsItemDeactivatedAfterEdit())
                    ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (presetReport is not null)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(presetReport);

            if (ImGui.SmallButton("OK##preset_report"))
                presetReport = null;
        }

        ImGui.Spacing();

        // The header (buttons and filter) stays in place; only the list below it scrolls.
        ImGui.TextColored(Theme.TextMuted, "Show trackers of");
        var filterWidth = ImGui.GetContentRegionAvail().X;
        var filterJob = DrawJobFilter();

        // The second filter goes next to the first one when both fit; otherwise it goes on the next line (never cut).
        var filterGap = ImGui.GetStyle().ItemSpacing.X;
        if (filterWidth >= (220f + 150f) * ImGuiHelpers.GlobalScale + filterGap + 8f * ImGuiHelpers.GlobalScale)
            ImGui.SameLine();

        var filterPvp = DrawContentFilter();

        ImGui.Spacing();

        using var list = ImRaii.Child("##tracker_explorer_list", Vector2.Zero, false);
        if (!list.Success)
            return;

        rows.Clear();
        explorerHovered = ImGui.IsWindowHovered();

        // A tracker passes the filter when it loads on every job or on the chosen one.
        bool Passes(TrackerRuntime tracker) =>
            (filterJob is null || tracker.Definition.Load.JobIds.Count == 0 || tracker.Definition.Load.JobIds.Contains(filterJob.Value))
            && (filterPvp is null || tracker.Definition.Load.Where == TrackerPvpMode.Everywhere
                || (filterPvp.Value ? tracker.Definition.Load.Where == TrackerPvpMode.PvpOnly : tracker.Definition.Load.Where == TrackerPvpMode.PveOnly));

        // Rows are close together, like in a file explorer.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f * scale, RowGap * scale));

        var hidden = 0;
        var entries = engine.RootEntries();
        if (entries.Count == 0)
            ImGui.TextColored(Theme.TextMuted, "Nothing here yet.");

        foreach (var entry in entries)
        {
            if (entry.Group is { } group)
            {
                var allMembers = engine.MembersOf(group);
                var members = allMembers.Where(Passes).ToList();
                hidden += allMembers.Count - members.Count;

                // A group with trackers but none for this job is left out; an empty group is always shown.
                if (allMembers.Count > 0 && members.Count == 0)
                    continue;

                DrawGroupRow(group, members.Count);

                if (group.Definition.Expanded)
                {
                    foreach (var member in members)
                        DrawTrackerRow(member, group);
                }
            }
            else if (entry.Tracker is { } tracker)
            {
                if (Passes(tracker))
                    DrawTrackerRow(tracker, null);
                else
                    hidden++;
            }
        }

        ImGui.PopStyleVar();

        if (hidden > 0)
            ImGui.TextColored(Theme.TextMuted, $"{hidden} tracker(s) of other jobs are hidden by the filter.");

        HandleDrag(engine);
    }

    // The job filter of the explorer: a combo with my job, every job, or one job of the list. Returns the job to filter by
    // (null = show every tracker).
    private uint? DrawJobFilter()
    {
        var myJob = plugin.GameData.Current.Player?.JobId;

        // Without a job to look at (not logged in), "my job" shows everything.
        var mode = jobFilter == JobFilterMode.MyJob && myJob is null ? JobFilterMode.AllJobs : jobFilter;

        var label = mode switch
        {
            JobFilterMode.AllJobs => "All jobs",
            JobFilterMode.PickedJob => GameNames.JobAbbreviation(pickedFilterJob),
            _ => $"My job ({GameNames.JobAbbreviation(myJob ?? 0)})"
        };

        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##tracker_job_filter", label, ImGuiComboFlags.None))
        {
            if (myJob is not null && ImGui.Selectable($"My job ({GameNames.JobAbbreviation(myJob.Value)})", jobFilter == JobFilterMode.MyJob))
                jobFilter = JobFilterMode.MyJob;

            if (ImGui.Selectable("All jobs", jobFilter == JobFilterMode.AllJobs))
                jobFilter = JobFilterMode.AllJobs;

            foreach (var (role, jobs) in RolesWithBeastmaster())
            {
                ImGui.TextColored(Theme.TextMuted, role);

                foreach (var jobId in jobs)
                {
                    if (ImGui.Selectable($"   {GameNames.JobAbbreviation(jobId)}##filter_{jobId}", jobFilter == JobFilterMode.PickedJob && pickedFilterJob == jobId))
                    {
                        jobFilter = JobFilterMode.PickedJob;
                        pickedFilterJob = jobId;
                    }
                }
            }

            ImGui.EndCombo();
        }

        return mode switch
        {
            JobFilterMode.AllJobs => null,
            JobFilterMode.PickedJob => pickedFilterJob,
            _ => myJob
        };
    }

    // The content filter of the explorer: the kind of content you are in, PvE, PvP or both. Returns true to show the trackers of PvP,
    // false for PvE, null for both.
    private bool? DrawContentFilter()
    {
        var inPvp = plugin.GameData.Current.Context.InPvP;

        var label = contentFilter switch
        {
            ContentFilterMode.Pve => "PvE",
            ContentFilterMode.Pvp => "PvP",
            ContentFilterMode.Both => "PvE and PvP",
            _ => inPvp ? "Here (PvP)" : "Here (PvE)"
        };

        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##tracker_content_filter", label, ImGuiComboFlags.None))
        {
            if (ImGui.Selectable(inPvp ? "Here (PvP)" : "Here (PvE)", contentFilter == ContentFilterMode.Here))
                contentFilter = ContentFilterMode.Here;

            if (ImGui.Selectable("PvE", contentFilter == ContentFilterMode.Pve))
                contentFilter = ContentFilterMode.Pve;

            if (ImGui.Selectable("PvP", contentFilter == ContentFilterMode.Pvp))
                contentFilter = ContentFilterMode.Pvp;

            if (ImGui.Selectable("PvE and PvP", contentFilter == ContentFilterMode.Both))
                contentFilter = ContentFilterMode.Both;

            ImGui.EndCombo();
        }

        return contentFilter switch
        {
            ContentFilterMode.Pve => false,
            ContentFilterMode.Pvp => true,
            ContentFilterMode.Both => null,
            _ => inPvp
        };
    }

    // The job the explorer is filtering by right now (null when it shows every tracker).
    private uint? JobFilterNow()
    {
        var myJob = plugin.GameData.Current.Player?.JobId;
        return jobFilter switch
        {
            JobFilterMode.AllJobs => null,
            JobFilterMode.PickedJob => pickedFilterJob,
            _ => myJob
        };
    }

    // The group a "New tracker" should go into: the selected group, or the group of the selected tracker.
    private TrackerGroupRuntime? SelectedGroupContext(TrackerEngine engine)
    {
        if (selectedGroupId is not null)
            return engine.FindGroup(selectedGroupId);

        var tracker = engine.Trackers.FirstOrDefault(a => a.Definition.Id == selectedTrackerId);
        return engine.FindGroup(tracker?.Definition.GroupId);
    }

    private void DrawGroupRow(TrackerGroupRuntime group, int memberCount)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var definition = group.Definition;
        var key = TrackerEngine.GroupKey(definition);

        var width = ImGui.GetContentRegionAvail().X - 4f * scale;
        var height = RowHeight * scale;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        ImGui.InvisibleButton($"##explorer_row_{key}", new Vector2(width, height));

        rows.Add(new RowRect(key, group, null, null, min, max));

        var mouse = ImGui.GetMousePos();
        var hovered = explorerHovered && Inside(min, max, mouse);
        var selected = selectedGroupId == definition.Id;
        var center = (min.Y + max.Y) / 2f;

        var chevronBox = (Min: new Vector2(min.X, min.Y), Max: new Vector2(min.X + 28f * scale, max.Y));

        var drawList = ImGui.GetWindowDrawList();
        FillRow(drawList, min, max, selected, hovered && dragKey is null, dragKey == key);

        DrawChevron(drawList, new Vector2(min.X + 14f * scale, center), definition.Expanded);
        DrawFolder(drawList, new Vector2(min.X + 32f * scale, center - 9f * scale), 18f * scale);

        var name = string.IsNullOrWhiteSpace(definition.Name) ? "Unnamed group" : definition.Name;
        var textY = center - ImGui.GetTextLineHeight() / 2f;
        var groupCountText = !definition.Enabled ? "Off" : memberCount == 1 ? "1 tracker" : $"{memberCount} trackers";
        var nameRoom = max.X - 10f * scale - ImGui.CalcTextSize(groupCountText).X - 10f * scale - (min.X + 58f * scale);
        drawList.AddText(new Vector2(min.X + 58f * scale, textY), ImGui.GetColorU32(Theme.Text), FitText(name, nameRoom));

        var count = groupCountText;
        var countWidth = ImGui.CalcTextSize(count).X;
        drawList.AddText(new Vector2(max.X - 10f * scale - countWidth, textY), ImGui.GetColorU32(Theme.TextMuted), count);

        if (hovered && dragKey is null && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (Inside(chevronBox.Min, chevronBox.Max, mouse))
            {
                definition.Expanded = !definition.Expanded;
                plugin.RequestSave();
            }
            else if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                // Double click on the group opens or closes it.
                definition.Expanded = !definition.Expanded;
                plugin.RequestSave();
            }
            else
            {
                // Any point of the row can be grabbed to move the group.
                SelectGroup(definition.Id);
                StartDrag(key, mouse);
            }
        }

        // Right click: the menu of the group.
        var menuId = $"##group_menu_{key}";
        if (hovered && dragKey is null && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            SelectGroup(definition.Id);
            ImGui.OpenPopup(menuId);
        }

        if (ImGui.BeginPopup(menuId))
        {
            if (ImGui.Selectable(definition.Enabled ? "Disable" : "Enable"))
            {
                definition.Enabled = !definition.Enabled;
                plugin.RequestSave();
            }

            if (ImGui.Selectable("Duplicate"))
                pendingDuplicateGroup = group;

            if (ImGui.Selectable("Delete"))
                pendingRemoveGroup = group;

            ImGui.EndPopup();
        }
    }

    // The text cut with "..." at the end so it is not wider than <room> pixels (the whole text when it fits).
    private static string FitText(string text, float room)
    {
        if (ImGui.CalcTextSize(text).X <= room)
            return text;

        var end = text.Length;
        while (end > 0 && ImGui.CalcTextSize(text[..end].TrimEnd() + "...").X > room)
            end--;

        return end <= 0 ? string.Empty : text[..end].TrimEnd() + "...";
    }

    private void DrawTrackerRow(TrackerRuntime tracker, TrackerGroupRuntime? parent)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var definition = tracker.Definition;
        var key = TrackerEngine.TrackerKey(definition);
        var indent = parent is null ? 0f : MemberIndent * scale;

        var width = ImGui.GetContentRegionAvail().X - 4f * scale;
        var height = RowHeight * scale;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        ImGui.InvisibleButton($"##explorer_row_{key}", new Vector2(width, height));

        rows.Add(new RowRect(key, null, tracker, parent, min, max));

        var mouse = ImGui.GetMousePos();
        var hovered = explorerHovered && Inside(min, max, mouse);
        var selected = selectedTrackerId == definition.Id;
        var center = (min.Y + max.Y) / 2f;


        var drawList = ImGui.GetWindowDrawList();
        FillRow(drawList, min, max, selected, hovered && dragKey is null, dragKey == key);

        // The game's own icon: the ability for a cooldown tracker, the status for an enhancement or enfeeblement tracker.
        var iconSize = 24f * scale;
        var iconMin = new Vector2(min.X + indent + 10f * scale, center - iconSize / 2f);
        DrawTrackerIcon(drawList, definition, iconMin, iconSize);

        var nameColor = definition.Enabled ? Theme.Text : Theme.TextMuted;
        var name = string.IsNullOrWhiteSpace(definition.Name) ? "Unnamed tracker" : definition.Name;
        var textY = center - ImGui.GetTextLineHeight() / 2f;
        var tag = KindTag(definition);
        var tagWidth = ImGui.CalcTextSize(tag).X;

        // A long name is cut with "..." so it never runs over the tag at the right.
        var nameLeft = iconMin.X + iconSize + 10f * scale;
        var nameRoom = max.X - 10f * scale - tagWidth - 10f * scale - nameLeft;
        drawList.AddText(new Vector2(nameLeft, textY), ImGui.GetColorU32(nameColor), FitText(name, nameRoom));

        drawList.AddText(new Vector2(max.X - 10f * scale - tagWidth, textY), ImGui.GetColorU32(Theme.TextMuted), tag);

        if (hovered && dragKey is null && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                // Double click on the tracker turns it on or off.
                definition.Enabled = !definition.Enabled;
                plugin.RequestSave();
            }
            else
            {
                // Any point of the row can be grabbed to move the tracker.
                SelectTracker(definition.Id);
                StartDrag(key, mouse);
            }
        }

        // Right click: the menu of the tracker.
        var menuId = $"##tracker_menu_{key}";
        if (hovered && dragKey is null && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            SelectTracker(definition.Id);
            ImGui.OpenPopup(menuId);
        }

        if (ImGui.BeginPopup(menuId))
        {
            // One entry that flips: Disable while it is on, Enable while it is off.
            if (ImGui.Selectable(definition.Enabled ? "Disable" : "Enable"))
            {
                definition.Enabled = !definition.Enabled;
                plugin.RequestSave();
            }

            if (ImGui.Selectable("Edit name"))
            {
                renamingTrackerId = definition.Id;
                openRename = true;
            }

            if (ImGui.Selectable("Duplicate"))
            {
                // Done after the list is drawn, so the list is not changed while it is being walked.
                pendingDuplicate = tracker;
            }

            if (ImGui.Selectable("Delete"))
                pendingRemove = tracker;

            ImGui.EndPopup();
        }
    }

    // The small label at the right of a tracker row.
    private static string KindTag(TrackerDefinition definition)
    {
        if (!definition.Enabled)
            return "Off";

        if (definition.Rules.Count > 1)
            return $"{definition.Rules.Count} triggers";

        var rule = definition.Rules.FirstOrDefault();
        if (rule is null)
            return "No trigger";

        switch (rule.Kind)
        {
            case ConditionKind.AbilityHighlighted:
                return "Activated";
            case ConditionKind.AbilityOnCooldown:
            case ConditionKind.AbilityOffCooldown:
            case ConditionKind.AbilityFull:
            case ConditionKind.AbilityNotFull:
                return "Cooldown";
        }

        if (rule.StatusId == 0)
            return "No status";

        return GameNames.StatusCategory(rule.StatusId) switch
        {
            1 => "Enhancement",
            2 => "Enfeeblement",
            _ => "Status"
        };
    }

    private static void DrawTrackerIcon(ImDrawListPtr drawList, TrackerDefinition definition, Vector2 min, float size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var max = min + new Vector2(size, size);

        var iconId = TrackerEngine.ResolveIcon(definition.Rules.FirstOrDefault(), 0, out var iconIsStatus);

        if (iconId == 0)
        {
            // Nothing chosen yet: an empty frame.
            drawList.AddRect(min, max, ImGui.GetColorU32(Theme.WithAlpha(Theme.TextMuted, 0.6f)), 3f * scale, ImDrawFlags.None, 1f);
            return;
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        var (uv0, uv1) = TrackerEngine.IconUv(new Vector2(size, size), iconIsStatus ? TrackerEngine.StatusIconAspect : 1f, 0.10f, 0.10f, 0.10f, 0.10f);

        drawList.AddImage(texture.Handle, min, max, uv0, uv1, 0xFFFFFFFFu);
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f)), 0f, ImDrawFlags.None, 1f);
    }

    private static void FillRow(ImDrawListPtr drawList, Vector2 min, Vector2 max, bool selected, bool hovered, bool beingDragged)
    {
        var scale = ImGuiHelpers.GlobalScale;

        if (beingDragged)
        {
            // The row that is being carried stays in its place as a faded outline.
            drawList.AddRect(min, max, ImGui.GetColorU32(Theme.WithAlpha(Theme.TextMuted, 0.6f)), 6f * scale, ImDrawFlags.None, 1f);
            return;
        }

        if (selected)
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.WithAlpha(Theme.Accent, 0.26f)), 6f * scale);
        else if (hovered)
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.WithAlpha(Theme.Accent, 0.12f)), 6f * scale);
    }

    // Six dots: the handle to drag a row.
    private static void DrawGrip(ImDrawListPtr drawList, Vector2 center)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var color = ImGui.GetColorU32(Theme.WithAlpha(Theme.TextMuted, 0.85f));
        var radius = 1.5f * scale;

        foreach (var dx in new[] { -3f, 3f })
        {
            foreach (var dy in new[] { -5f, 0f, 5f })
                drawList.AddCircleFilled(center + new Vector2(dx, dy) * scale, radius, color);
        }
    }

    private static void DrawChevron(ImDrawListPtr drawList, Vector2 center, bool expanded)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var color = ImGui.GetColorU32(Theme.TextMuted);

        if (expanded)
        {
            drawList.AddTriangleFilled(
                center + new Vector2(-4f, -2f) * scale,
                center + new Vector2(4f, -2f) * scale,
                center + new Vector2(0f, 3f) * scale,
                color);
        }
        else
        {
            drawList.AddTriangleFilled(
                center + new Vector2(-2f, -4f) * scale,
                center + new Vector2(3f, 0f) * scale,
                center + new Vector2(-2f, 4f) * scale,
                color);
        }
    }

    // A small folder drawn with two rounded rectangles.
    private static void DrawFolder(ImDrawListPtr drawList, Vector2 topLeft, float size)
    {
        var color = ImGui.GetColorU32(Theme.Accent);
        var unit = size / 18f;

        drawList.AddRectFilled(topLeft + new Vector2(0f, 1f) * unit, topLeft + new Vector2(8f, 6f) * unit, color, 2f * unit);
        drawList.AddRectFilled(topLeft + new Vector2(0f, 4f) * unit, topLeft + new Vector2(18f, 16f) * unit, color, 2f * unit);
    }

    private static bool Inside(Vector2 min, Vector2 max, Vector2 point)
    {
        return point.X >= min.X && point.X < max.X && point.Y >= min.Y && point.Y < max.Y;
    }

    private void SelectTracker(string id)
    {
        selectedTrackerId = id;
        selectedGroupId = null;
        pendingDeleteId = null;
    }

    private void SelectGroup(string id)
    {
        selectedGroupId = id;
        selectedTrackerId = null;
        pendingDeleteId = null;
    }

    // ---------------------------------------------------------------- drag and drop

    private void StartDrag(string key, Vector2 mouse)
    {
        dragKey = key;
        dragging = false;
        pressPosition = mouse;
    }

    // Runs once per frame after the rows are drawn: follows the drag, shows where the row would land
    // and, when the mouse button is released, moves it there.
    private void HandleDrag(TrackerEngine engine)
    {
        if (dragKey is null)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var mouse = ImGui.GetMousePos();

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            // A small movement is a click, not a drag.
            if (!dragging && Vector2.Distance(mouse, pressPosition) > 5f * scale)
                dragging = true;

            if (dragging)
            {
                DrawDropIndicator(ComputeDrop(engine, mouse));
                DrawDragLabel(engine, mouse);
            }

            return;
        }

        if (dragging)
            ApplyDrop(engine, ComputeDrop(engine, mouse));

        dragKey = null;
        dragging = false;
    }

    private DropTarget ComputeDrop(TrackerEngine engine, Vector2 mouse)
    {
        var none = new DropTarget(DropKind.None, null, null, null, 0f, 0f, null);
        if (rows.Count == 0 || dragKey is null)
            return none;

        var scale = ImGuiHelpers.GlobalScale;
        var dragTracker = engine.Trackers.FirstOrDefault(a => TrackerEngine.TrackerKey(a.Definition) == dragKey);
        var dragGroup = engine.Groups.FirstOrDefault(g => TrackerEngine.GroupKey(g.Definition) == dragKey);
        if (dragTracker is null && dragGroup is null)
            return none;

        // The row under the mouse. Above the list it is the first row, below the list the last one.
        RowRect? row = null;
        var rel = 0.5f;

        foreach (var candidate in rows)
        {
            if (mouse.Y >= candidate.Min.Y - 1f && mouse.Y <= candidate.Max.Y + 1f)
            {
                row = candidate;
                rel = (mouse.Y - candidate.Min.Y) / Math.Max(1f, candidate.Max.Y - candidate.Min.Y);
                break;
            }
        }

        if (row is null)
        {
            if (mouse.Y < rows[0].Min.Y)
            {
                row = rows[0];
                rel = 0f;
            }
            else
            {
                row = rows[^1];
                rel = 1f;
            }
        }

        var left = rows[0].Min.X;

        // The group a row belongs to (the group itself for a group row), and where that group's block ends.
        var owner = row.Group ?? row.Parent;

        DropTarget RootBefore(RowRect target, string targetKey) =>
            new(DropKind.RootBefore, targetKey, null, null, target.Min.Y - RowGap * scale / 2f, left, null);

        DropTarget RootAfter(RowRect target, string targetKey) =>
            new(DropKind.RootAfter, targetKey, null, null, target.Max.Y + RowGap * scale / 2f, left, null);

        // After a group means after its last visible row.
        DropTarget RootAfterGroup(TrackerGroupRuntime group)
        {
            var last = rows.Last(r => r.Group == group || r.Parent == group);
            return RootAfter(last, TrackerEngine.GroupKey(group.Definition));
        }

        // ---- dragging a group: it can only go to the top level
        if (dragGroup is not null)
        {
            if (owner == dragGroup)
                return none;

            // Over a group (its row or one of its trackers): before or after the WHOLE group,
            // depending on which half of the group's block the mouse is in.
            if (owner is not null)
            {
                var groupRow = rows.First(r => r.Group == owner);
                var blockBottom = rows.Last(r => r.Group == owner || r.Parent == owner).Max.Y;
                var upperHalf = mouse.Y < (groupRow.Min.Y + blockBottom) / 2f;

                return upperHalf
                    ? RootBefore(groupRow, TrackerEngine.GroupKey(owner.Definition))
                    : RootAfterGroup(owner);
            }

            // Over a loose tracker: before or after it.
            return rel < 0.5f ? RootBefore(row, row.Key) : RootAfter(row, row.Key);
        }

        // ---- dragging a tracker
        if (dragTracker is null)
            return none;

        // A loose tracker: before or after it, at the top level.
        if (owner is null)
        {
            if (row.Tracker == dragTracker)
                return none;

            return rel < 0.5f ? RootBefore(row, row.Key) : RootAfter(row, row.Key);
        }

        // A group row.
        if (row.IsGroup)
        {
            var groupDefinition = owner.Definition;
            var firstMember = rows.FirstOrDefault(r => r.Parent == owner);

            if (rel < 0.3f)
                return RootBefore(row, row.Key);

            if (rel > 0.7f)
            {
                if (groupDefinition.Expanded && firstMember?.Tracker is { } first && first != dragTracker)
                {
                    return new DropTarget(DropKind.IntoGroupBefore, null, owner, first,
                        firstMember.Min.Y - RowGap * scale / 2f, left + MemberIndent * scale, null);
                }

                if (!groupDefinition.Expanded)
                    return RootAfter(row, row.Key);
            }

            return new DropTarget(DropKind.IntoGroupEnd, null, owner, null, 0f, 0f, row);
        }

        // A member of a group. The far left edge of the row means "take it out of the group".
        if (mouse.X < row.Min.X + MemberIndent * scale * 0.7f)
            return RootAfterGroup(owner);

        if (row.Tracker is null || row.Tracker == dragTracker)
            return none;

        return rel < 0.5f
            ? new DropTarget(DropKind.IntoGroupBefore, null, owner, row.Tracker, row.Min.Y - RowGap * scale / 2f, left + MemberIndent * scale, null)
            : new DropTarget(DropKind.IntoGroupAfter, null, owner, row.Tracker, row.Max.Y + RowGap * scale / 2f, left + MemberIndent * scale, null);
    }

    private void DrawDropIndicator(DropTarget drop)
    {
        if (drop.Kind == DropKind.None || rows.Count == 0)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(Theme.Accent);

        if (drop.Kind == DropKind.IntoGroupEnd && drop.Highlight is { } highlight)
        {
            // Dropping onto a group: the whole group row is highlighted.
            drawList.AddRectFilled(highlight.Min, highlight.Max, ImGui.GetColorU32(Theme.WithAlpha(Theme.Accent, 0.16f)), 6f * scale);
            drawList.AddRect(highlight.Min, highlight.Max, color, 6f * scale, ImDrawFlags.None, 2f * scale);
            return;
        }

        // Dropping between two rows: a line with a dot at its start.
        var right = rows[0].Max.X;
        drawList.AddLine(new Vector2(drop.LineX, drop.LineY), new Vector2(right, drop.LineY), color, 2f * scale);
        drawList.AddCircleFilled(new Vector2(drop.LineX, drop.LineY), 4f * scale, color);
    }

    // A small label that follows the mouse while a row is being dragged.
    private void DrawDragLabel(TrackerEngine engine, Vector2 mouse)
    {
        var scale = ImGuiHelpers.GlobalScale;

        var tracker = engine.Trackers.FirstOrDefault(a => TrackerEngine.TrackerKey(a.Definition) == dragKey);
        var group = engine.Groups.FirstOrDefault(g => TrackerEngine.GroupKey(g.Definition) == dragKey);
        var name = tracker?.Definition.Name ?? group?.Definition.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            name = "Unnamed";

        var padding = new Vector2(10f, 6f) * scale;
        var textSize = ImGui.CalcTextSize(name);
        var min = mouse + new Vector2(14f, 10f) * scale;
        var max = min + textSize + padding * 2f;

        var foreground = ImGui.GetForegroundDrawList();
        foreground.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.17f, 0.19f, 0.35f, 0.96f)), 6f * scale);
        foreground.AddRect(min, max, ImGui.GetColorU32(Theme.Accent), 6f * scale, ImDrawFlags.None, 1f);
        foreground.AddText(min + padding, ImGui.GetColorU32(Theme.Text), name);
    }

    private void ApplyDrop(TrackerEngine engine, DropTarget drop)
    {
        if (drop.Kind == DropKind.None || dragKey is null)
            return;

        var tracker = engine.Trackers.FirstOrDefault(a => TrackerEngine.TrackerKey(a.Definition) == dragKey);
        var group = engine.Groups.FirstOrDefault(g => TrackerEngine.GroupKey(g.Definition) == dragKey);

        switch (drop.Kind)
        {
            case DropKind.RootBefore:
            case DropKind.RootAfter:
                var after = drop.Kind == DropKind.RootAfter;

                if (tracker is not null)
                    engine.MoveTrackerToRoot(tracker, drop.TargetKey!, after);
                else if (group is not null)
                    engine.MoveGroup(group, drop.TargetKey!, after);
                break;

            case DropKind.IntoGroupEnd:
            case DropKind.IntoGroupBefore:
            case DropKind.IntoGroupAfter:
                if (tracker is null || drop.Group is null)
                    break;

                engine.MoveTrackerIntoGroup(tracker, drop.Group, drop.Neighbour, drop.Kind == DropKind.IntoGroupAfter);

                // Show where it went.
                drop.Group.Definition.Expanded = true;
                break;
        }

        if (tracker is not null)
            SelectTracker(tracker.Definition.Id);
        else if (group is not null)
            SelectGroup(group.Definition.Id);
    }

    // ---------------------------------------------------------------- group editor

    private void DrawGroupEditor(TrackerEngine engine, TrackerGroupRuntime group, GameSnapshot snapshot)
    {
        var definition = group.Definition;

        // The template holds the display settings and the load conditions of the whole group.
        var template = definition.Template ??= new TrackerDefinition();

        Widgets.Card("##card_group_settings", 8, () =>
        {
            Widgets.SectionLabel("Group");

            var name = definition.Name;
            if (ImGui.InputText("Name", ref name, 64))
            {
                definition.Name = name;
                plugin.RequestSave();
            }

            var growth = definition.Growth;
            if (EnumCombo("Grow towards", ref growth, GrowthLabel, GrowthOrder))
            {
                definition.Growth = growth;
                plugin.RequestSave();
            }

            var spacing = definition.Spacing;
            if (ImGui.InputInt("Space between icons (px)", ref spacing, 1, 5))
            {
                definition.Spacing = Math.Clamp(spacing, 0, 128);
                plugin.RequestSave();
            }

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "Drag the trackers in the list on the left to change their order.");
        });

        // One card for what is set on the whole group: the icon size and the load conditions are applied to every tracker of it.
        DrawGroupApply(engine, group, template);

        if (pendingDeleteId == definition.Id)
        {
            if (ImGui.Button("Click again to delete this group"))
            {
                engine.RemoveGroupWithTrackers(group);
                selectedGroupId = null;
                pendingDeleteId = null;
                return;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                pendingDeleteId = null;
        }
        else if (ImGui.Button("Delete group"))
        {
            pendingDeleteId = definition.Id;
        }

        ImGui.TextColored(Theme.TextMuted, "Deleting a group deletes all the trackers inside it.");
    }

    // ---------------------------------------------------------------- icon triggers

    // The search boxes of the cards (one per trigger / text source, so cards do not share what is typed).
    private sealed class TextSearch
    {
        public string Query = string.Empty;
        public string Applied = string.Empty;
        public int AppliedKind = -1;

        // Ability search box: the results show only while the box has the focus (or the mouse is over them),
        // and not after one was picked.
        public bool Initialized;
        public bool Dismissed;
        public bool ResultsHovered;
        public IReadOnlyList<StatusSearchResult> Statuses = Array.Empty<StatusSearchResult>();
        public IReadOnlyList<ActionInfo> Actions = Array.Empty<ActionInfo>();
    }

    private readonly Dictionary<string, TextSearch> textSearches = new();

    private TextSearch SearchOf(string key)
    {
        if (!textSearches.TryGetValue(key, out var found))
            textSearches[key] = found = new TextSearch();

        return found;
    }

    // Runs the name search only when the typed text (or what is searched) changed.
    private static void RunSearch(TextSearch state, bool statuses, bool actions, int kind)
    {
        if (state.Query == state.Applied && state.AppliedKind == kind)
            return;

        state.Applied = state.Query;
        state.AppliedKind = kind;

        var query = state.Query.Trim();
        state.Statuses = statuses && query.Length >= 3
            ? GameNames.SearchStatuses(query, MaxSearchResults)
            : Array.Empty<StatusSearchResult>();
        state.Actions = actions && query.Length >= 3
            ? GameNames.SearchActions(query, MaxSearchResults)
            : Array.Empty<ActionInfo>();
    }

    private static readonly ConditionKind[] StatusConditions =
        { ConditionKind.StatusPresent, ConditionKind.StatusMissing };

    private static readonly ConditionKind[] AbilityConditions =
        { ConditionKind.AbilityHighlighted, ConditionKind.AbilityOnCooldown, ConditionKind.AbilityOffCooldown, ConditionKind.AbilityFull, ConditionKind.AbilityNotFull };

    // An effect of a trigger: a plain checkbox, applied whenever the trigger matches.
    private bool EffectCheckbox(string label, bool value)
    {
        if (ImGui.Checkbox(label, ref value))
            plugin.RequestSave();

        return value;
    }

    private static string WhenLabel(ConditionKind kind) => kind switch
    {
        ConditionKind.StatusPresent => "It is on",
        ConditionKind.StatusMissing => "It is missing",
        ConditionKind.AbilityHighlighted => "When the game highlights it",
        ConditionKind.AbilityOnCooldown => "On cooldown",
        ConditionKind.AbilityOffCooldown => "Off cooldown",
        ConditionKind.AbilityFull => "Full of charges",
        _ => "Not full of charges"
    };

    private void EnsureAbilities(GameSnapshot snapshot)
    {
        // The abilities of the current job are listed again only when the job or the level changes.
        var jobId = snapshot.Player?.JobId ?? 0;
        var level = snapshot.Player?.Level ?? 0;

        if (jobId == abilitiesJobId && level == abilitiesLevel)
            return;

        abilitiesJobId = jobId;
        abilitiesLevel = level;
        abilities = jobId == 0 ? Array.Empty<ActionInfo>() : GameNames.AbilitiesOfJob(jobId, level);
    }

    // Dragging an icon trigger by any empty point of its card (arrows do the same).
    private int ruleDragIndex = -1;
    private bool ruleDragging;
    private Vector2 ruleDragStart;
    private readonly List<(Vector2 Min, Vector2 Max)> ruleRects = new();

    // The icon triggers of a tracker, in order of priority. The first one that matches decides the picture and the glow.
    private void DrawRules(TrackerDefinition definition, GameSnapshot snapshot)
    {
        EnsureAbilities(snapshot);

        var moveFrom = -1;
        var moveTo = -1;
        var remove = -1;

        ruleRects.Clear();

        // All the triggers live in one card: trigger 1, trigger 2, and so on.
        Widgets.Card("##card_tracker_triggers", 22 * definition.Rules.Count + 2, () =>
        {
            Widgets.SectionLabel("Icon triggers");

            if (definition.Rules.Count > 1)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Theme.TextMuted))
                    ImGui.TextWrapped("Triggers are tried in order. The first one that matches decides the icon; if it does not match, the next one is tried.");
            }

            for (var i = 0; i < definition.Rules.Count; i++)
            {
                if (i > 0)
                    ImGui.Separator();

                DrawRuleSection(definition, i, snapshot, ref moveFrom, ref moveTo, ref remove);
            }

            ImGui.Spacing();

            if (ImGui.Button("Add trigger"))
            {
                definition.Rules.Add(TrackerEngine.NewRule());
                plugin.RequestSave();
            }
        });

        HandleRuleDrag(definition, ref moveFrom, ref moveTo);

        // The list changes only here, after every card has been drawn.
        if (remove >= 0 && definition.Rules.Count > 1)
        {
            definition.Rules.RemoveAt(remove);
            textSearches.Clear();
            plugin.RequestSave();
        }
        else if (moveFrom >= 0 && moveTo >= 0 && moveTo < definition.Rules.Count && moveFrom != moveTo)
        {
            var moved = definition.Rules[moveFrom];
            definition.Rules.RemoveAt(moveFrom);
            definition.Rules.Insert(moveTo, moved);
            textSearches.Clear();
            plugin.RequestSave();
        }
    }

    // Follows a drag of a trigger card: a line shows where it would land; on release it moves there.
    private void HandleRuleDrag(TrackerDefinition definition, ref int moveFrom, ref int moveTo)
    {
        if (ruleDragIndex < 0)
            return;

        var count = definition.Rules.Count;
        var scale = ImGuiHelpers.GlobalScale;
        var mouse = ImGui.GetMousePos();

        if (ruleDragIndex >= count || ruleRects.Count != count)
        {
            ruleDragIndex = -1;
            ruleDragging = false;
            return;
        }

        // The slot where it would land: before the first card whose middle is below the mouse, or at the end.
        var slot = count;
        for (var i = 0; i < count; i++)
        {
            if (mouse.Y < (ruleRects[i].Min.Y + ruleRects[i].Max.Y) / 2f)
            {
                slot = i;
                break;
            }
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (!ruleDragging && Vector2.Distance(mouse, ruleDragStart) > 5f * scale)
                ruleDragging = true;

            if (ruleDragging)
            {
                var y = slot < count ? ruleRects[slot].Min.Y - 3f * scale : ruleRects[count - 1].Max.Y + 3f * scale;
                var left = ruleRects[0].Min.X;
                var right = ruleRects[0].Max.X;
                ImGui.GetForegroundDrawList().AddLine(new Vector2(left, y), new Vector2(right, y), ImGui.GetColorU32(Theme.Accent), 3f * scale);
            }

            return;
        }

        if (ruleDragging)
        {
            var destination = slot > ruleDragIndex ? slot - 1 : slot;
            if (destination != ruleDragIndex)
            {
                moveFrom = ruleDragIndex;
                moveTo = destination;
            }
        }

        ruleDragIndex = -1;
        ruleDragging = false;
    }

    // What a folded trigger shows after its title: what it tracks and when it counts ("Requiescat · status on you, present").
    private static string RuleSummary(TrackerRule rule)
    {
        var id = rule.IsAbility ? rule.ActionId : rule.StatusId;
        if (id == 0)
            return "(nothing selected)";

        var name = rule.IsAbility ? GameNames.ActionName(id) : GameNames.StatusName(id);

        var whose = rule.Unit switch
        {
            TrackerUnit.Target => "on the target",
            TrackerUnit.Focus => "on the focus",
            TrackerUnit.TargetOfTarget => "on the target of the target",
            _ => "on you"
        };

        var when = rule.Kind switch
        {
            ConditionKind.StatusPresent => $"status {whose}, present",
            ConditionKind.StatusMissing => $"status {whose}, missing",
            ConditionKind.AbilityHighlighted => "ability, highlighted",
            ConditionKind.AbilityOnCooldown => "ability, on cooldown",
            ConditionKind.AbilityOffCooldown => "ability, off cooldown",
            ConditionKind.AbilityFull => "ability, full of charges",
            _ => "ability, not full of charges"
        };

        return $"{name} · {when}";
    }

    private void DrawRuleSection(TrackerDefinition definition, int index, GameSnapshot snapshot, ref int moveFrom, ref int moveTo, ref int remove)
    {
        var rule = definition.Rules[index];
        var count = definition.Rules.Count;
        var key = $"{definition.Id}:rule{index}";
        var pick = SearchOf(key);
        var iconPick = SearchOf(key + ":icon");

        var isStatus = !rule.IsAbility;
        RunSearch(pick, isStatus, !isStatus, (int)rule.Kind);

        var iconIsStatus = rule.Icon == IconSource.Status;
        RunSearch(iconPick, iconIsStatus, rule.Icon == IconSource.Ability, (int)rule.Icon);

        var unit = UnitOf(rule.Unit, snapshot);
        var startY = ImGui.GetCursorScreenPos().Y;

        {
            ImGui.PushID(index);

            // With several triggers each one is a header that opens and closes like the texts of a trigger (it follows the
            // trigger when it moves), to work on the others without a huge card. Everything of the trigger is inside it.
            var collapsed = false;
            if (count > 1)
            {
                var title = $"Trigger {index + 1}";
                var headerId = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(rule);
                collapsed = !ImGui.CollapsingHeader($"{title}: {RuleSummary(rule)}###rule_header_{headerId}", ImGuiTreeNodeFlags.DefaultOpen);
            }

            // Order: the first trigger that matches wins. Arrows move this one up or down.
            if (count > 1 && !collapsed)
            {
                if (index > 0)
                {
                    if (ImGui.ArrowButton("##rule_up", ImGuiDir.Up))
                    {
                        moveFrom = index;
                        moveTo = index - 1;
                    }

                    ImGui.SameLine();
                }

                if (index < count - 1)
                {
                    if (ImGui.ArrowButton("##rule_down", ImGuiDir.Down))
                    {
                        moveFrom = index;
                        moveTo = index + 1;
                    }

                    ImGui.SameLine();
                }

                if (ImGui.SmallButton("Remove this trigger"))
                    remove = index;
            }

            if (!collapsed)
            {
                // Two steps: what is tracked (a status or an ability), then in which situation.
                var tracksAbility = rule.IsAbility;
                if (ImGui.BeginCombo("Track", tracksAbility ? "An ability" : "An enhancement or enfeeblement", ImGuiComboFlags.None))
                {
                    if (ImGui.Selectable("An enhancement or enfeeblement", !tracksAbility) && tracksAbility)
                    {
                        rule.Kind = ConditionKind.StatusPresent;
                        pick.Query = string.Empty;
                        pick.Initialized = false;
                        plugin.RequestSave();
                    }

                    if (ImGui.Selectable("An ability", tracksAbility) && !tracksAbility)
                    {
                        rule.Kind = ConditionKind.AbilityOffCooldown;
                        pick.Query = string.Empty;
                        pick.Initialized = false;
                        plugin.RequestSave();
                    }

                    ImGui.EndCombo();
                }

                var options = tracksAbility ? AbilityConditions : StatusConditions;

                // An ability with a single charge has no "full" and no "not full": they are offered only when the game says it has
                // several charges (or while that is not known yet). The saved value always stays visible.
                if (tracksAbility && snapshot.Cooldowns.TryGetValue(rule.ActionId, out var chargeData) && chargeData.MaxCharges <= 1)
                    options = options.Where(o => o is not (ConditionKind.AbilityFull or ConditionKind.AbilityNotFull) || o == rule.Kind).ToArray();
                if (ImGui.BeginCombo("When", WhenLabel(rule.Kind), ImGuiComboFlags.None))
                {
                    foreach (var option in options)
                    {
                        if (ImGui.Selectable(WhenLabel(option), rule.Kind == option))
                        {
                            rule.Kind = option;
                            plugin.RequestSave();
                        }
                    }

                    ImGui.EndCombo();
                }

                if (isStatus)
                    DrawStatusPart(definition, index, rule, pick, unit);
                else
                    DrawAbilityPart(definition, index, rule, pick);

                ImGui.Spacing();
                DrawRuleIcon(rule, iconPick);

                ImGui.Spacing();

                // Effects: applied whenever this trigger matches.
                rule.Glow = EffectCheckbox("Glow", rule.Glow);
                rule.Red = EffectCheckbox("Red", rule.Red);
                rule.Dim = EffectCheckbox("Dimmed", rule.Dim);

                // The sweep is for the triggers of an ability: it covers the part of the icon that is still recharging.
                if (rule.IsAbility)
                    rule.Sweep = EffectCheckbox("Recharge sweep", rule.Sweep);

                ImGui.Spacing();
                DrawRuleTexts(definition, index, rule);
            }

            // Pressing on an empty point of the section (not on a control) grabs it, to drag it to another place in the list.
            var endY = ImGui.GetCursorScreenPos().Y;
            var windowPos = ImGui.GetWindowPos();
            ruleRects.Add((new Vector2(windowPos.X, startY), new Vector2(windowPos.X + ImGui.GetWindowSize().X, endY)));

            var mouse = ImGui.GetMousePos();
            if (count > 1 && ruleDragIndex < 0 && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered()
                && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && mouse.Y >= startY && mouse.Y <= endY)
            {
                ruleDragIndex = index;
                ruleDragging = false;
                ruleDragStart = mouse;
            }

            ImGui.PopID();
        }
    }

    private void DrawStatusPart(TrackerDefinition definition, int index, TrackerRule rule, TextSearch pick, UnitSnapshot? unit)
    {
        var watched = rule.Unit;
        if (EnumCombo("Watch", ref watched, UnitLabel))
        {
            rule.Unit = watched;
            plugin.RequestSave();
        }

        var onlyMine = rule.OnlyMine;
        if (ImGui.Checkbox("Only the ones I applied", ref onlyMine))
        {
            rule.OnlyMine = onlyMine;
            plugin.RequestSave();
        }

        // A tracker follows ONE status. The filters (lists of statuses) belong to the party list, the nameplates and the other bars.
        if (rule.StatusId == 0)
            ImGui.TextColored(Theme.Warning, "No status selected yet.");
        else
            ImGui.Text($"Selected: {GameNames.StatusName(rule.StatusId)} ({rule.StatusId})");

        // The box starts with the name of the status already chosen.
        if (!pick.Initialized)
        {
            pick.Initialized = true;

            if (rule.StatusId != 0)
            {
                pick.Query = GameNames.StatusName(rule.StatusId);
                pick.Dismissed = true;
            }
        }

        var query = pick.Query;
        if (ImGui.InputText("Search status", ref query, 64))
        {
            pick.Query = query;
            pick.Dismissed = false;
        }

        var active = ImGui.IsItemActive();
        if (ImGui.IsItemActivated())
            pick.Dismissed = false;

        // The matches show while the box has the focus (or the mouse is over them, so a click still lands).
        if (pick.Dismissed || !(active || pick.ResultsHovered) || pick.Statuses.Count == 0)
        {
            pick.ResultsHovered = false;
            return;
        }

        var top = ImGui.GetCursorScreenPos().Y;

        foreach (var result in pick.Statuses)
        {
            var kindText = result.Category switch
            {
                1 => "enhancement",
                2 => "enfeeblement",
                _ => string.Empty
            };

            if (ImGui.Selectable($"{result.Name} ({result.Id})   {kindText}##res_{result.Id}", rule.StatusId == result.Id))
            {
                PickStatus(definition, index, rule, result.Id);
                pick.Query = result.Name;
                pick.Dismissed = true;
            }
        }

        var bottom = ImGui.GetCursorScreenPos().Y;
        var mouse = ImGui.GetMousePos();
        var windowX = ImGui.GetWindowPos().X;
        pick.ResultsHovered = !pick.Dismissed
            && mouse.Y >= top && mouse.Y <= bottom
            && mouse.X >= windowX && mouse.X <= windowX + ImGui.GetWindowSize().X;
    }

    private void DrawAbilityPart(TrackerDefinition definition, int index, TrackerRule rule, TextSearch pick)
    {
        if (rule.ActionId == 0)
            ImGui.TextColored(Theme.Warning, "No ability selected yet.");
        else
            ImGui.Text($"Selected: {GameNames.ActionName(rule.ActionId)} ({rule.ActionId})");

        // The box starts with the name of the ability already chosen.
        if (!pick.Initialized)
        {
            pick.Initialized = true;

            if (rule.ActionId != 0)
            {
                pick.Query = GameNames.ActionName(rule.ActionId);
                pick.Dismissed = true;
            }
        }

        var query = pick.Query;
        if (ImGui.InputText("Search ability", ref query, 64))
        {
            pick.Query = query;
            pick.Dismissed = false;
        }

        var active = ImGui.IsItemActive();
        if (ImGui.IsItemActivated())
            pick.Dismissed = false;

        // The list of matches appears while typing and goes away when the focus leaves the box.
        // (It stays while the mouse is over it, so a click on a result still lands.)
        if (pick.Dismissed || !(active || pick.ResultsHovered) || pick.Actions.Count == 0)
        {
            pick.ResultsHovered = false;
            return;
        }

        var top = ImGui.GetCursorScreenPos().Y;

        foreach (var result in pick.Actions)
        {
            if (ImGui.Selectable($"{result.Name} ({result.Id})   {result.RecastSeconds:0}s##actionresult_{result.Id}", rule.ActionId == result.Id))
            {
                PickAction(definition, index, rule, result.Id);

                // The name goes into the box, as it is written in the game.
                pick.Query = result.Name;
                pick.Dismissed = true;
            }
        }

        var bottom = ImGui.GetCursorScreenPos().Y;
        var mouse = ImGui.GetMousePos();
        var windowX = ImGui.GetWindowPos().X;
        pick.ResultsHovered = !pick.Dismissed
            && mouse.Y >= top && mouse.Y <= bottom
            && mouse.X >= windowX && mouse.X <= windowX + ImGui.GetWindowSize().X;
    }

    // Any game icon can be the picture of a trigger, whatever it tracks: the icon of Requiescat's status has the stack number drawn
    // in it, so another status icon (without the number) can be used while the stacks are shown as a text of our own.
    private void DrawRuleIcon(TrackerRule rule, TextSearch iconPick)
    {
        // Default = the icon of what the trigger tracks. Personalized = any game icon, of an enhancement/enfeeblement or of an ability.
        var personalized = rule.Icon != IconSource.Automatic;
        if (ImGui.BeginCombo("Icon", personalized ? "Personalized" : "Default", ImGuiComboFlags.None))
        {
            if (ImGui.Selectable("Default", !personalized) && personalized)
            {
                rule.Icon = IconSource.Automatic;
                iconPick.Query = string.Empty;
                plugin.RequestSave();
            }

            if (ImGui.Selectable("Personalized", personalized) && !personalized)
            {
                rule.Icon = rule.IsAbility ? IconSource.Ability : IconSource.Status;
                iconPick.Query = string.Empty;
                plugin.RequestSave();
            }

            ImGui.EndCombo();
        }

        if (rule.Icon != IconSource.Automatic)
        {
            var fromAbility = rule.Icon == IconSource.Ability;
            if (ImGui.BeginCombo("Icon of", fromAbility ? "An ability" : "An enhancement or enfeeblement", ImGuiComboFlags.None))
            {
                if (ImGui.Selectable("An enhancement or enfeeblement", !fromAbility) && fromAbility)
                {
                    rule.Icon = IconSource.Status;
                    iconPick.Query = string.Empty;
                    plugin.RequestSave();
                }

                if (ImGui.Selectable("An ability", fromAbility) && !fromAbility)
                {
                    rule.Icon = IconSource.Ability;
                    iconPick.Query = string.Empty;
                    plugin.RequestSave();
                }

                ImGui.EndCombo();
            }
        }

        if (rule.Icon == IconSource.Automatic)
            return;

        var isStatus = rule.Icon == IconSource.Status;
        var chosen = isStatus ? rule.IconStatusId : rule.IconActionId;

        if (chosen == 0)
            ImGui.TextColored(Theme.Warning, isStatus ? "No status selected yet." : "No ability selected yet.");
        else
            ImGui.Text($"Selected: {(isStatus ? GameNames.StatusName(chosen) : GameNames.ActionName(chosen))} ({chosen})");

        // The box starts with the name of the icon already chosen.
        if (!iconPick.Initialized)
        {
            iconPick.Initialized = true;

            if (chosen != 0)
            {
                iconPick.Query = isStatus ? GameNames.StatusName(chosen) : GameNames.ActionName(chosen);
                iconPick.Dismissed = true;
            }
        }

        var query = iconPick.Query;
        if (ImGui.InputText(isStatus ? "Search status icon" : "Search ability icon", ref query, 64))
        {
            iconPick.Query = query;
            iconPick.Dismissed = false;
        }

        var active = ImGui.IsItemActive();
        if (ImGui.IsItemActivated())
            iconPick.Dismissed = false;

        // The matches show while the box has the focus (or the mouse is over them, so a click still lands).
        var hasResults = isStatus ? iconPick.Statuses.Count > 0 : iconPick.Actions.Count > 0;
        if (iconPick.Dismissed || !(active || iconPick.ResultsHovered) || !hasResults)
        {
            iconPick.ResultsHovered = false;
            return;
        }

        var top = ImGui.GetCursorScreenPos().Y;

        foreach (var result in iconPick.Statuses)
        {
            if (ImGui.Selectable($"{result.Name} ({result.Id})##icon_status_{result.Id}", rule.IconStatusId == result.Id))
            {
                rule.IconStatusId = result.Id;
                iconPick.Query = result.Name;
                iconPick.Dismissed = true;
                plugin.RequestSave();
            }
        }

        foreach (var result in iconPick.Actions)
        {
            if (ImGui.Selectable($"{result.Name} ({result.Id})##icon_action_{result.Id}", rule.IconActionId == result.Id))
            {
                rule.IconActionId = result.Id;
                iconPick.Query = result.Name;
                iconPick.Dismissed = true;
                plugin.RequestSave();
            }
        }

        var bottom = ImGui.GetCursorScreenPos().Y;
        var mouse = ImGui.GetMousePos();
        var windowX = ImGui.GetWindowPos().X;
        iconPick.ResultsHovered = !iconPick.Dismissed
            && mouse.Y >= top && mouse.Y <= bottom
            && mouse.X >= windowX && mouse.X <= windowX + ImGui.GetWindowSize().X;
    }

    // A brand new tracker, or a copy that still carries "(copy)", takes the name of what its first trigger tracks.
    private static bool IsRenamable(string name) => name == DefaultName || name.EndsWith(" (copy)", StringComparison.Ordinal);

    private void PickStatus(TrackerDefinition definition, int index, TrackerRule rule, uint statusId)
    {
        rule.StatusId = statusId;

        // A brand new tracker takes the name of the first thing it watches.
        if (index == 0 && IsRenamable(definition.Name))
            definition.Name = GameNames.StatusName(statusId);

        plugin.RequestSave();
    }

    private void PickAction(TrackerDefinition definition, int index, TrackerRule rule, uint actionId)
    {
        rule.ActionId = actionId;

        if (index == 0 && IsRenamable(definition.Name))
            definition.Name = GameNames.ActionName(actionId);

        plugin.RequestSave();
    }

    // ---------------------------------------------------------------- texts over the icon

    private enum TextGroup
    {
        Trigger,
        Combo,
        Status,
        Ability
    }

    private static readonly TextGroup[] TextGroups = { TextGroup.Trigger, TextGroup.Combo, TextGroup.Status, TextGroup.Ability };

    private static TextGroup TextGroupOf(TextSource source) => source switch
    {
        TextSource.OwnTime or TextSource.OwnStacks => TextGroup.Trigger,
        TextSource.ComboTime => TextGroup.Combo,
        TextSource.StatusTime or TextSource.StatusStacks => TextGroup.Status,
        _ => TextGroup.Ability
    };

    // The source for a group; "second" = the stacks / charges instead of the time.
    private static TextSource TextSourceOf(TextGroup group, bool second) => group switch
    {
        TextGroup.Trigger => second ? TextSource.OwnStacks : TextSource.OwnTime,
        TextGroup.Combo => TextSource.ComboTime,
        TextGroup.Status => second ? TextSource.StatusStacks : TextSource.StatusTime,
        _ => second ? TextSource.AbilityCharges : TextSource.AbilityCooldown
    };

    private static string TextGroupLabel(TextGroup group) => group switch
    {
        TextGroup.Trigger => "This trigger",
        TextGroup.Combo => "The combo",
        TextGroup.Status => "An enhancement or enfeeblement I choose",
        _ => "An ability I choose"
    };

    private static string TextValueLabel(TextGroup group, bool second) => group switch
    {
        TextGroup.Trigger => second ? "The stacks or charges" : "The time",
        TextGroup.Status => second ? "The stacks" : "The time",
        _ => second ? "The charges" : "The cooldown"
    };

    // The texts of one trigger: Text 1, Text 2... They are drawn over the icon while this trigger decides. Each one has its own
    // sources (the first with a value wins) and its own look.
    private void DrawRuleTexts(TrackerDefinition definition, int ruleIndex, TrackerRule rule)
    {
        if (!ImGui.CollapsingHeader($"Texts of this trigger ({rule.Texts.Count})###texts", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        for (var i = 0; i < rule.Texts.Count; i++)
        {
            if (DrawTextBlock(definition, ruleIndex, rule, i))
                break;
        }

        if (ImGui.Button("Add text"))
        {
            // A new text starts at the bottom right corner (no offset), so it does not hide behind Text 1 in the centre.
            rule.Texts.Add(new TrackerTextDefinition
            {
                Sources = { new TrackerTextSource { Source = TextSource.OwnStacks } },
                Anchor = AnchorPoint.BottomRight,
                Size = 9
            });

            plugin.RequestSave();
        }
    }

    // One text of a trigger. Returns true when the text was removed (the list changed, so the caller stops walking it).
    private bool DrawTextBlock(TrackerDefinition definition, int ruleIndex, TrackerRule rule, int index)
    {
        var text = rule.Texts[index];
        var removed = false;

        var moveFrom = -1;
        var moveTo = -1;
        var removeSource = -1;

        var searches = new TextSearch[text.Sources.Count];

        for (var j = 0; j < text.Sources.Count; j++)
        {
            var source = text.Sources[j];
            var needsStatus = source.Source is TextSource.StatusTime or TextSource.StatusStacks;
            var needsAbility = source.Source is TextSource.AbilityCooldown or TextSource.AbilityCharges;

            searches[j] = SearchOf($"{definition.Id}:rule{ruleIndex}:text{index}:{j}");
            RunSearch(searches[j], needsStatus, needsAbility, (int)source.Source);
        }

        ImGui.PushID(index);
        ImGui.Separator();
        ImGui.TextColored(Theme.Accent, $"Text {index + 1}");

        for (var j = 0; j < text.Sources.Count; j++)
        {
            if (j > 0)
                ImGui.Separator();

            ImGui.PushID(j);
            DrawSourceRow(text, j, searches[j], ref moveFrom, ref moveTo, ref removeSource);
            ImGui.PopID();
        }

        if (ImGui.Button("Add source"))
        {
            text.Sources.Add(new TrackerTextSource());
            plugin.RequestSave();
        }

        ImGui.Spacing();

        // Font sizes are fixed steps: each one is a real font, so the text is always sharp.
        if (ImGui.BeginCombo("Text size (px)", $"{text.Size} px", ImGuiComboFlags.None))
        {
            foreach (var size in OverlayFonts.AvailableSizes)
            {
                if (ImGui.Selectable($"{size} px", text.Size == size))
                {
                    text.Size = size;
                    plugin.RequestSave();
                }
            }

            ImGui.EndCombo();
        }

        var anchor = text.Anchor;
        var x = text.OffsetX;
        var y = text.OffsetY;
        if (PlacementControls("Text", ref anchor, ref x, ref y))
        {
            text.Anchor = anchor;
            text.OffsetX = x;
            text.OffsetY = y;
            plugin.RequestSave();
        }

        var color = text.Color;
        if (Widgets.ColorSetting("Text color", ref color, Widgets.Default(text).Color))
        {
            text.Color = color;
            plugin.RequestSave();
        }

        if (ImGui.Button("Remove this text"))
            removed = true;

        ImGui.PopID();

        // The list of sources changes only after the block is drawn.
        if (removeSource >= 0 && text.Sources.Count > 1)
        {
            text.Sources.RemoveAt(removeSource);
            textSearches.Clear();
            plugin.RequestSave();
        }
        else if (moveFrom >= 0 && moveTo >= 0 && moveTo < text.Sources.Count)
        {
            (text.Sources[moveFrom], text.Sources[moveTo]) = (text.Sources[moveTo], text.Sources[moveFrom]);
            textSearches.Clear();
            plugin.RequestSave();
        }

        if (!removed)
            return false;

        rule.Texts.RemoveAt(index);
        textSearches.Clear();
        plugin.RequestSave();
        return true;
    }

    // One source of a text: where the value comes from, with arrows to change its priority.
    private void DrawSourceRow(TrackerTextDefinition text, int j, TextSearch search, ref int moveFrom, ref int moveTo, ref int removeSource)
    {
        var source = text.Sources[j];
        var count = text.Sources.Count;

        var needsStatus = source.Source is TextSource.StatusTime or TextSource.StatusStacks;
        var needsAbility = source.Source is TextSource.AbilityCooldown or TextSource.AbilityCharges;

        if (count > 1)
            ImGui.TextColored(Theme.Accent, j == 0 ? "Source 1" : $"If not, source {j + 1}");

        // Two steps, like the icon triggers: where the value comes from, then which value.
        var group = TextGroupOf(source.Source);
        if (ImGui.BeginCombo("Text from", TextGroupLabel(group), ImGuiComboFlags.None))
        {
            foreach (var option in TextGroups)
            {
                if (ImGui.Selectable(TextGroupLabel(option), group == option) && option != group)
                {
                    source.Source = TextSourceOf(option, false);
                    search.Query = string.Empty;
                    plugin.RequestSave();
                }
            }

            ImGui.EndCombo();
        }

        group = TextGroupOf(source.Source);
        if (group != TextGroup.Combo)
        {
            var second = source.Source is TextSource.OwnStacks or TextSource.StatusStacks or TextSource.AbilityCharges;
            if (ImGui.BeginCombo("Show", TextValueLabel(group, second), ImGuiComboFlags.None))
            {
                foreach (var option in new[] { false, true })
                {
                    if (ImGui.Selectable(TextValueLabel(group, option), second == option) && option != second)
                    {
                        source.Source = TextSourceOf(group, option);
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }
        }

        if (count > 1)
        {
            if (j > 0)
            {
                if (ImGui.ArrowButton("##source_up", ImGuiDir.Up))
                {
                    moveFrom = j;
                    moveTo = j - 1;
                }

                ImGui.SameLine();
            }

            if (j < count - 1)
            {
                if (ImGui.ArrowButton("##source_down", ImGuiDir.Down))
                {
                    moveFrom = j;
                    moveTo = j + 1;
                }

                ImGui.SameLine();
            }

            if (ImGui.SmallButton("Remove this source"))
                removeSource = j;
        }

        if (needsStatus)
        {
            if (source.StatusId == 0)
                ImGui.TextColored(Theme.Warning, "No status selected yet.");
            else
                ImGui.Text($"Selected: {GameNames.StatusName(source.StatusId)} ({source.StatusId})");

            var query = search.Query;
            if (ImGui.InputText("Search status by name", ref query, 64))
                search.Query = query;

            foreach (var result in search.Statuses)
            {
                if (ImGui.Selectable($"{result.Name} ({result.Id})##text_status_{result.Id}", source.StatusId == result.Id))
                {
                    source.StatusId = result.Id;
                    plugin.RequestSave();
                }
            }
        }
        else if (needsAbility)
        {
            if (source.ActionId == 0)
                ImGui.TextColored(Theme.Warning, "No ability selected yet.");
            else
                ImGui.Text($"Selected: {GameNames.ActionName(source.ActionId)} ({source.ActionId})");

            var query = search.Query;
            if (ImGui.InputText("Search ability by name", ref query, 64))
                search.Query = query;

            foreach (var result in search.Actions)
            {
                if (ImGui.Selectable($"{result.Name} ({result.Id})   {result.RecastSeconds:0}s##text_action_{result.Id}", source.ActionId == result.Id))
                {
                    source.ActionId = result.Id;
                    plugin.RequestSave();
                }
            }
        }
    }

    // ---------------------------------------------------------------- display

    // The icon size and the load conditions of one tracker, in one card.
    private void DrawTrackerSettings(TrackerDefinition definition, TrackerRuntime? runtime)
    {
        // The size of an icon belongs to the layout system (the same size you see in unlock mode).
        var elementLayout = runtime?.Element.Layout;

        Widgets.Card("##card_tracker_settings", 16, () =>
        {
            Widgets.SectionLabel("Display and load conditions");

            if (elementLayout is not null)
            {
                // Width and height are chosen apart, so the icon does not have to be square. The +/- buttons move by 2 px (10 px with Ctrl).
                var width = elementLayout.Width;
                if (ImGui.InputInt("Icon width (px)", ref width, 2, 10))
                {
                    elementLayout.Width = Math.Clamp(width, 1, 512);
                    plugin.RequestSave();
                }

                var height = elementLayout.Height;
                if (ImGui.InputInt("Icon height (px)", ref height, 2, 10))
                {
                    elementLayout.Height = Math.Clamp(height, 1, 512);
                    plugin.RequestSave();
                }
            }

            ImGui.Spacing();
            Widgets.SectionLabel("Load conditions");

            if (DrawLoadContent(definition, "##jobs_popup_tracker"))
                plugin.RequestSave();
        });
    }

    // What is set on a whole group, in one card that starts folded: the icon size and the load conditions are copied to every tracker of
    // the group (and to the ones that join it later).
    private void DrawGroupApply(TrackerEngine engine, TrackerGroupRuntime group, TrackerDefinition template)
    {
        const string cardId = "##card_group_apply";

        // The first time the card is ever drawn it is folded (the second id remembers that, so a card the user opened stays open).
        if (Widgets.Collapsed.Add("##card_group_apply_seen"))
        {
            Widgets.Collapsed.Add(cardId);
            Widgets.CollapsedChanged?.Invoke();
        }

        var changed = false;

        Widgets.Card(cardId, 20, () =>
        {
            Widgets.SectionLabel("Apply to every tracker in the group");

            var width = group.Definition.IconWidth;
            if (ImGui.InputInt("Icon width (px)", ref width, 2, 10))
            {
                group.Definition.IconWidth = Math.Clamp(width, 1, 512);
                changed = true;
            }

            var height = group.Definition.IconHeight;
            if (ImGui.InputInt("Icon height (px)", ref height, 2, 10))
            {
                group.Definition.IconHeight = Math.Clamp(height, 1, 512);
                changed = true;
            }

            ImGui.Spacing();
            Widgets.SectionLabel("Load conditions");
            changed |= DrawLoadContent(template, "##jobs_popup_group");

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "A change here overwrites that setting in every tracker of the group,");
            ImGui.TextColored(Theme.TextMuted, "and trackers that join the group later take these values too.");
        });

        if (changed)
            engine.ApplyGroupStyle(group);
    }

    // ---------------------------------------------------------------- load conditions

    // The jobs of the game by role, to choose which ones a tracker loads on.
    private static readonly (string Role, uint[] Jobs)[] JobsByRole =
    {
        ("Tanks", new uint[] { 19, 21, 32, 37 }),
        ("Healers", new uint[] { 24, 28, 33, 40 }),
        ("Melee", new uint[] { 20, 22, 30, 34, 39, 41 }),
        ("Ranged", new uint[] { 23, 31, 38 }),
        ("Casters", new uint[] { 25, 27, 35, 42, 36 }),
        ("Crafters", new uint[] { 8, 9, 10, 11, 12, 13, 14, 15 }),
        ("Gatherers", new uint[] { 16, 17, 18 })
    };

    // Beastmaster (a melee, after Viper): its id is read from the game's job list by its abbreviation. 0 until it is found.
    private static uint beastmasterId;
    private static bool beastmasterLooked;

    // The roles with their jobs in the order of the game, with Beastmaster after Viper.
    private static List<(string Role, uint[] Jobs)> RolesWithBeastmaster()
    {
        if (!beastmasterLooked)
        {
            beastmasterId = GameNames.JobIdByAbbreviation("BST");
            beastmasterLooked = true;
        }

        var roles = new List<(string Role, uint[] Jobs)>(JobsByRole);

        if (beastmasterId != 0)
        {
            for (var i = 0; i < roles.Count; i++)
            {
                if (roles[i].Role == "Melee")
                    roles[i] = (roles[i].Role, roles[i].Jobs.Append(beastmasterId).ToArray());
            }
        }

        return roles;
    }

    // Every job of the picker.
    private static List<uint> AllJobIds()
    {
        var all = new List<uint>();
        foreach (var (_, jobs) in RolesWithBeastmaster())
            all.AddRange(jobs);

        return all;
    }

    // The content of the job menu: a row of checkboxes for each role, with All / None for the whole role.
    // An empty list means every job, so then every job shows ticked; when all of them end up ticked the list goes back to empty.
    private static bool DrawJobPicker(List<uint> selected)
    {
        var changed = false;

        var allJobs = AllJobIds();
        if (selected.Count == 0)
            selected.AddRange(allJobs);

        var roles = RolesWithBeastmaster();

        foreach (var (role, jobs) in roles)
        {
            ImGui.TextColored(Theme.TextMuted, role);
            ImGui.SameLine();

            if (ImGui.SmallButton($"All##all_{role}"))
            {
                foreach (var jobId in jobs)
                {
                    if (!selected.Contains(jobId))
                        selected.Add(jobId);
                }

                changed = true;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton($"None##none_{role}"))
            {
                foreach (var jobId in jobs)
                    changed |= selected.Remove(jobId);
            }

            foreach (var jobId in jobs)
            {
                var on = selected.Contains(jobId);
                if (ImGui.Checkbox($"{GameNames.JobAbbreviation(jobId)}##pick_{jobId}", ref on))
                {
                    if (on)
                        selected.Add(jobId);
                    else
                        selected.Remove(jobId);

                    changed = true;
                }

                ImGui.SameLine();
            }

            ImGui.NewLine();
            ImGui.Spacing();
        }

        if (ImGui.Button("Every job##clear_jobs"))
        {
            selected.Clear();
            changed = true;
        }

        if (allJobs.TrueForAll(selected.Contains))
            selected.Clear();

        return changed;
    }

    // The content of the load conditions (content and jobs) of one tracker or of the template of a group. True when something changed.
    private bool DrawLoadContent(TrackerDefinition definition, string popupId)
    {
        var changed = false;

        var where = definition.Load.Where;
        if (EnumCombo("Content", ref where, PvpLabel))
        {
            definition.Load.Where = where;
            changed = true;
        }

        ImGui.Spacing();

        // Every job ticked is the same as no list at all: it is shown as "every job", never as a list of all of them.
        if (definition.Load.JobIds.Count > 0 && AllJobIds().TrueForAll(definition.Load.JobIds.Contains))
        {
            definition.Load.JobIds.Clear();
            changed = true;
        }

        if (definition.Load.JobIds.Count == 0)
            ImGui.TextColored(Theme.TextMuted, "Loads on every job.");
        else
            ImGui.TextColored(Theme.TextMuted, "Loads only on these jobs (click a job to remove it):");

        uint? removeJob = null;
        foreach (var jobId in definition.Load.JobIds)
        {
            if (ImGui.SmallButton($"{GameNames.JobAbbreviation(jobId)}  x##job_{jobId}"))
                removeJob = jobId;

            ImGui.SameLine();
        }

        ImGui.NewLine();

        if (removeJob is { } toRemove)
        {
            definition.Load.JobIds.Remove(toRemove);
            changed = true;
        }

        // A menu to pick any jobs, by role (no need to be playing them).
        if (ImGui.Button("Choose jobs..."))
            ImGui.OpenPopup(popupId);

        if (ImGui.BeginPopup(popupId))
        {
            if (DrawJobPicker(definition.Load.JobIds))
                changed = true;

            ImGui.EndPopup();
        }

        return changed;
    }

    // ---------------------------------------------------------------- delete

    private void DrawDelete(TrackerEngine engine, TrackerRuntime runtime)
    {
        var id = runtime.Definition.Id;

        if (pendingDeleteId == id)
        {
            if (ImGui.Button("Click again to delete this tracker"))
            {
                engine.Remove(runtime);
                selectedTrackerId = null;
                pendingDeleteId = null;
                return;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                pendingDeleteId = null;
        }
        else if (ImGui.Button("Delete tracker"))
        {
            pendingDeleteId = id;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static UnitSnapshot? UnitOf(TrackerUnit unit, GameSnapshot snapshot) => unit switch
    {
        TrackerUnit.Player => snapshot.Player,
        TrackerUnit.Target => snapshot.Target,
        TrackerUnit.Focus => snapshot.Focus,
        TrackerUnit.TargetOfTarget => snapshot.TargetOfTarget,
        _ => null
    };

    private static string UnitLabel(TrackerUnit unit) => unit switch
    {
        TrackerUnit.Player => "Player",
        TrackerUnit.Target => "Target",
        TrackerUnit.Focus => "Focus target",
        TrackerUnit.TargetOfTarget => "Target of target",
        _ => unit.ToString()
    };

    // The same six words as every "Grow towards" of the interface (the icon rows next to the bars and the player's groups).
    private static readonly GroupGrowth[] GrowthOrder =
    {
        GroupGrowth.ToLeft, GroupGrowth.ToRight, GroupGrowth.Up, GroupGrowth.Down,
        GroupGrowth.FromCenterHorizontal, GroupGrowth.FromCenterVertical
    };

    private static string GrowthLabel(GroupGrowth growth) => growth switch
    {
        GroupGrowth.ToRight => "Right",
        GroupGrowth.ToLeft => "Left",
        GroupGrowth.FromCenterHorizontal => "Centered horizontally",
        GroupGrowth.Down => "Down",
        GroupGrowth.Up => "Up",
        GroupGrowth.FromCenterVertical => "Centered vertically",
        _ => growth.ToString()
    };

    private static string PvpLabel(TrackerPvpMode mode) => mode switch
    {
        TrackerPvpMode.PveOnly => "Only in PvE (not in PvP)",
        TrackerPvpMode.PvpOnly => "Only in PvP",
        _ => "Everywhere (PvE and PvP)"
    };

    private static string AnchorLabel(AnchorPoint point) => point.Label();

    // The three controls that place a text on the icon: the anchor point and the two offsets.
    private static bool PlacementControls(string label, ref AnchorPoint anchor, ref int offsetX, ref int offsetY)
    {
        var changed = false;

        if (EnumCombo($"{label} position", ref anchor, AnchorLabel))
            changed = true;

        if (ImGui.InputInt($"{label} offset X (px)", ref offsetX, 1, 5))
            changed = true;

        // Up is plus (the saved value counts downwards, so the sign is turned here).
        var up = -offsetY;
        if (ImGui.InputInt($"{label} offset Y (px)", ref up, 1, 5))
        {
            offsetY = -up;
            changed = true;
        }

        return changed;
    }

    // A drop-down list for any enum. Returns true when the user picked a different value.
    private static bool EnumCombo<T>(string label, ref T value, Func<T, string> labeler, IReadOnlyList<T>? options = null) where T : struct, Enum
    {
        var changed = false;

        if (ImGui.BeginCombo(label, labeler(value), ImGuiComboFlags.None))
        {
            foreach (var option in options ?? Enum.GetValues<T>())
            {
                if (ImGui.Selectable(labeler(option), EqualityComparer<T>.Default.Equals(option, value)))
                {
                    value = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }
}
