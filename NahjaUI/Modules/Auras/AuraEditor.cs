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

namespace NahjaUI.Modules.Auras;

/// <summary>
/// The "Auras" page of the main window. On the left, an explorer: groups and loose auras mixed in any order,
/// groups that open and close, and drag and drop to reorder or to move auras in and out of groups.
/// On the right, the editor of what is selected.
/// </summary>
internal sealed class AuraEditor
{
    private const string DefaultName = "New aura";
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
    private string? selectedAuraId;
    private string? selectedGroupId;

    // The aura or group waiting for a second click on its delete button.
    private string? pendingDeleteId;
    private AuraRuntime? pendingDuplicate;
    private AuraRuntime? pendingRemove;

    // Status search
    // The filter of the explorer: only the auras that load on a job (the one you play, or the one you pick), or every aura.
    private enum JobFilterMode
    {
        MyJob,
        AllJobs,
        PickedJob
    }

    private JobFilterMode jobFilter = JobFilterMode.MyJob;
    private uint pickedFilterJob;

    private string search = string.Empty;
    private bool addAsExtraEffect;
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

    public AuraEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    // ---------------------------------------------------------------- explorer data types

    // A row of the explorer, with its place on the screen. Parent is the group of a member row.
    private sealed record RowRect(string Key, AuraGroupRuntime? Group, AuraRuntime? Aura, AuraGroupRuntime? Parent, Vector2 Min, Vector2 Max)
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
        AuraGroupRuntime? Group,
        AuraRuntime? Neighbour,
        float LineY,
        float LineX,
        RowRect? Highlight);

    // ---------------------------------------------------------------- layout of the page

    public void Draw()
    {
        var engine = plugin.AuraSystem;

        if (pendingRemove is not null)
        {
            engine.Remove(pendingRemove);
            if (selectedAuraId == pendingRemove.Definition.Id)
                selectedAuraId = null;

            pendingRemove = null;
            pendingDeleteId = null;
            pendingDuplicate = null;
        }

        if (pendingDuplicate is not null)
        {
            var copy = engine.Duplicate(pendingDuplicate);
            pendingDuplicate = null;
            SelectAura(copy.Definition.Id);
        }
        var snapshot = plugin.GameData.Current;
        var scale = ImGuiHelpers.GlobalScale;

        var available = ImGui.GetContentRegionAvail();

        // With room: explorer on the left, editor on the right. In a narrow window: one above the other.
        if (available.X >= 720f * scale)
        {
            var explorerWidth = Math.Clamp(available.X * 0.42f, 300f * scale, 440f * scale);

            using (var left = ImRaii.Child("##aura_explorer_column", new Vector2(explorerWidth, 0f), false))
            {
                if (left.Success)
                    DrawExplorer(engine, ImGui.GetContentRegionAvail().Y - 4f * scale);
            }

            ImGui.SameLine();

            using (var right = ImRaii.Child("##aura_editor_column", Vector2.Zero, false))
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

    private void DrawEditor(AuraEngine engine, GameSnapshot snapshot)
    {
        var group = engine.Groups.FirstOrDefault(g => g.Definition.Id == selectedGroupId);
        if (group is not null)
        {
            DrawGroupEditor(engine, group, snapshot);
            return;
        }

        var aura = engine.Auras.FirstOrDefault(r => r.Definition.Id == selectedAuraId);
        if (aura is null)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "Select a group or an aura to edit it,");
            ImGui.TextColored(Theme.TextMuted, "or create a new one.");
            return;
        }

        var definition = aura.Definition;

        DrawGeneral(engine, aura);

        DrawRules(definition, snapshot);
        DrawDisplay(engine, definition, aura, null);
        DrawTexts(definition);
        DrawLoad(engine, definition, snapshot, null);
        DrawDelete(engine, aura);
    }

    // ---------------------------------------------------------------- explorer

    // The card that holds the list. A height of 0 or less fills the space that is left.
    private void DrawExplorer(AuraEngine engine, float height)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X - 16f * scale;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.Card))
        using (var card = ImRaii.Child("##aura_explorer", new Vector2(width, Math.Max(0f, height)), true))
        {
            if (card.Success)
                DrawExplorerContent(engine);
        }

        ImGui.Spacing();
    }

    private void DrawExplorerContent(AuraEngine engine)
    {
        var scale = ImGuiHelpers.GlobalScale;

        Widgets.SectionLabel("Groups and auras");

        if (Widgets.PrimaryButton("New group"))
        {
            var created = engine.AddGroup();
            SelectGroup(created.Definition.Id);
        }

        ImGui.SameLine();

        if (ImGui.Button("New aura"))
        {
            // A new aura goes into the group that is selected, or into the group of the selected aura.
            var target = SelectedGroupContext(engine);
            var created = engine.AddNew(target);

            // Looking at the auras of one job: a loose new aura loads on that job (it can be changed in its load conditions).
            // An aura created inside a group keeps loading on every job, so a general group never ends up limited by mistake.
            var filterNow = JobFilterNow();
            if (target is null && filterNow is { } jobForNew && !created.Definition.Load.JobIds.Contains(jobForNew))
                created.Definition.Load.JobIds.Add(jobForNew);

            if (target is not null)
                target.Definition.Expanded = true;

            SelectAura(created.Definition.Id);
        }

        ImGui.Spacing();

        // The header (buttons and filter) stays in place; only the list below it scrolls.
        ImGui.TextColored(Theme.TextMuted, "Show auras of");
        var filterJob = DrawJobFilter();

        ImGui.Spacing();

        using var list = ImRaii.Child("##aura_explorer_list", Vector2.Zero, false);
        if (!list.Success)
            return;

        rows.Clear();
        explorerHovered = ImGui.IsWindowHovered();

        // An aura passes the filter when it loads on every job or on the chosen one.
        bool Passes(AuraRuntime aura) =>
            filterJob is null || aura.Definition.Load.JobIds.Count == 0 || aura.Definition.Load.JobIds.Contains(filterJob.Value);

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

                // A group with auras but none for this job is left out; an empty group is always shown.
                if (allMembers.Count > 0 && members.Count == 0)
                    continue;

                DrawGroupRow(group, members.Count);

                if (group.Definition.Expanded)
                {
                    foreach (var member in members)
                        DrawAuraRow(member, group);
                }
            }
            else if (entry.Aura is { } aura)
            {
                if (Passes(aura))
                    DrawAuraRow(aura, null);
                else
                    hidden++;
            }
        }

        ImGui.PopStyleVar();

        if (hidden > 0)
            ImGui.TextColored(Theme.TextMuted, $"{hidden} aura(s) of other jobs are hidden by the filter.");

        HandleDrag(engine);
    }

    // The job filter of the explorer: a combo with my job, every job, or one job of the list. Returns the job to filter by
    // (null = show every aura).
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
        if (ImGui.BeginCombo("##aura_job_filter", label, ImGuiComboFlags.None))
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

    // The job the explorer is filtering by right now (null when it shows every aura).
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

    // The group a "New aura" should go into: the selected group, or the group of the selected aura.
    private AuraGroupRuntime? SelectedGroupContext(AuraEngine engine)
    {
        if (selectedGroupId is not null)
            return engine.FindGroup(selectedGroupId);

        var aura = engine.Auras.FirstOrDefault(a => a.Definition.Id == selectedAuraId);
        return engine.FindGroup(aura?.Definition.GroupId);
    }

    private void DrawGroupRow(AuraGroupRuntime group, int memberCount)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var definition = group.Definition;
        var key = AuraEngine.GroupKey(definition);

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
        drawList.AddText(new Vector2(min.X + 58f * scale, textY), ImGui.GetColorU32(Theme.Text), name);

        var count = memberCount == 1 ? "1 aura" : $"{memberCount} auras";
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
    }

    private void DrawAuraRow(AuraRuntime aura, AuraGroupRuntime? parent)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var definition = aura.Definition;
        var key = AuraEngine.AuraKey(definition);
        var indent = parent is null ? 0f : MemberIndent * scale;

        var width = ImGui.GetContentRegionAvail().X - 4f * scale;
        var height = RowHeight * scale;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        ImGui.InvisibleButton($"##explorer_row_{key}", new Vector2(width, height));

        rows.Add(new RowRect(key, null, aura, parent, min, max));

        var mouse = ImGui.GetMousePos();
        var hovered = explorerHovered && Inside(min, max, mouse);
        var selected = selectedAuraId == definition.Id;
        var center = (min.Y + max.Y) / 2f;


        var drawList = ImGui.GetWindowDrawList();
        FillRow(drawList, min, max, selected, hovered && dragKey is null, dragKey == key);

        // The game's own icon: the ability for a cooldown aura, the status for an enhancement or enfeeblement aura.
        var iconSize = 24f * scale;
        var iconMin = new Vector2(min.X + indent + 10f * scale, center - iconSize / 2f);
        DrawAuraIcon(drawList, definition, iconMin, iconSize);

        var nameColor = definition.Enabled ? Theme.Text : Theme.TextMuted;
        var name = string.IsNullOrWhiteSpace(definition.Name) ? "Unnamed aura" : definition.Name;
        var textY = center - ImGui.GetTextLineHeight() / 2f;
        drawList.AddText(new Vector2(iconMin.X + iconSize + 10f * scale, textY), ImGui.GetColorU32(nameColor), name);

        var tag = KindTag(definition);
        var tagWidth = ImGui.CalcTextSize(tag).X;
        drawList.AddText(new Vector2(max.X - 10f * scale - tagWidth, textY), ImGui.GetColorU32(Theme.TextMuted), tag);

        if (hovered && dragKey is null && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            // Any point of the row can be grabbed to move the aura.
            SelectAura(definition.Id);
            StartDrag(key, mouse);
        }

        // Right click: the menu of the aura.
        var menuId = $"##aura_menu_{key}";
        if (hovered && dragKey is null && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            SelectAura(definition.Id);
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

            if (ImGui.Selectable("Duplicate"))
            {
                // Done after the list is drawn, so the list is not changed while it is being walked.
                pendingDuplicate = aura;
            }

            if (ImGui.Selectable("Delete"))
                pendingRemove = aura;

            ImGui.EndPopup();
        }
    }

    // The small label at the right of an aura row.
    private static string KindTag(AuraDefinition definition)
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
            case ConditionKind.AbilityActivated:
                return "Activated";
            case ConditionKind.AbilityOnCooldown:
            case ConditionKind.AbilityReady:
            case ConditionKind.AbilityAlways:
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

    private static void DrawAuraIcon(ImDrawListPtr drawList, AuraDefinition definition, Vector2 min, float size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var max = min + new Vector2(size, size);

        var iconId = AuraEngine.ResolveIcon(definition.Rules.FirstOrDefault(), 0, out var iconIsStatus);

        if (iconId == 0)
        {
            // Nothing chosen yet: an empty frame.
            drawList.AddRect(min, max, ImGui.GetColorU32(Theme.WithAlpha(Theme.TextMuted, 0.6f)), 3f * scale, ImDrawFlags.None, 1f);
            return;
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        var (uv0, uv1) = AuraEngine.IconUv(new Vector2(size, size), iconIsStatus ? AuraEngine.StatusIconAspect : 1f, 0.10f);

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

    private void SelectAura(string id)
    {
        selectedAuraId = id;
        selectedGroupId = null;
        pendingDeleteId = null;
    }

    private void SelectGroup(string id)
    {
        selectedGroupId = id;
        selectedAuraId = null;
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
    private void HandleDrag(AuraEngine engine)
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

    private DropTarget ComputeDrop(AuraEngine engine, Vector2 mouse)
    {
        var none = new DropTarget(DropKind.None, null, null, null, 0f, 0f, null);
        if (rows.Count == 0 || dragKey is null)
            return none;

        var scale = ImGuiHelpers.GlobalScale;
        var dragAura = engine.Auras.FirstOrDefault(a => AuraEngine.AuraKey(a.Definition) == dragKey);
        var dragGroup = engine.Groups.FirstOrDefault(g => AuraEngine.GroupKey(g.Definition) == dragKey);
        if (dragAura is null && dragGroup is null)
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
        DropTarget RootAfterGroup(AuraGroupRuntime group)
        {
            var last = rows.Last(r => r.Group == group || r.Parent == group);
            return RootAfter(last, AuraEngine.GroupKey(group.Definition));
        }

        // ---- dragging a group: it can only go to the top level
        if (dragGroup is not null)
        {
            if (owner == dragGroup)
                return none;

            // Over a group (its row or one of its auras): before or after the WHOLE group,
            // depending on which half of the group's block the mouse is in.
            if (owner is not null)
            {
                var groupRow = rows.First(r => r.Group == owner);
                var blockBottom = rows.Last(r => r.Group == owner || r.Parent == owner).Max.Y;
                var upperHalf = mouse.Y < (groupRow.Min.Y + blockBottom) / 2f;

                return upperHalf
                    ? RootBefore(groupRow, AuraEngine.GroupKey(owner.Definition))
                    : RootAfterGroup(owner);
            }

            // Over a loose aura: before or after it.
            return rel < 0.5f ? RootBefore(row, row.Key) : RootAfter(row, row.Key);
        }

        // ---- dragging an aura
        if (dragAura is null)
            return none;

        // A loose aura: before or after it, at the top level.
        if (owner is null)
        {
            if (row.Aura == dragAura)
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
                if (groupDefinition.Expanded && firstMember?.Aura is { } first && first != dragAura)
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

        if (row.Aura is null || row.Aura == dragAura)
            return none;

        return rel < 0.5f
            ? new DropTarget(DropKind.IntoGroupBefore, null, owner, row.Aura, row.Min.Y - RowGap * scale / 2f, left + MemberIndent * scale, null)
            : new DropTarget(DropKind.IntoGroupAfter, null, owner, row.Aura, row.Max.Y + RowGap * scale / 2f, left + MemberIndent * scale, null);
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
    private void DrawDragLabel(AuraEngine engine, Vector2 mouse)
    {
        var scale = ImGuiHelpers.GlobalScale;

        var aura = engine.Auras.FirstOrDefault(a => AuraEngine.AuraKey(a.Definition) == dragKey);
        var group = engine.Groups.FirstOrDefault(g => AuraEngine.GroupKey(g.Definition) == dragKey);
        var name = aura?.Definition.Name ?? group?.Definition.Name ?? string.Empty;
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

    private void ApplyDrop(AuraEngine engine, DropTarget drop)
    {
        if (drop.Kind == DropKind.None || dragKey is null)
            return;

        var aura = engine.Auras.FirstOrDefault(a => AuraEngine.AuraKey(a.Definition) == dragKey);
        var group = engine.Groups.FirstOrDefault(g => AuraEngine.GroupKey(g.Definition) == dragKey);

        switch (drop.Kind)
        {
            case DropKind.RootBefore:
            case DropKind.RootAfter:
                var after = drop.Kind == DropKind.RootAfter;

                if (aura is not null)
                    engine.MoveAuraToRoot(aura, drop.TargetKey!, after);
                else if (group is not null)
                    engine.MoveGroup(group, drop.TargetKey!, after);
                break;

            case DropKind.IntoGroupEnd:
            case DropKind.IntoGroupBefore:
            case DropKind.IntoGroupAfter:
                if (aura is null || drop.Group is null)
                    break;

                engine.MoveAuraIntoGroup(aura, drop.Group, drop.Neighbour, drop.Kind == DropKind.IntoGroupAfter);

                // Show where it went.
                drop.Group.Definition.Expanded = true;
                break;
        }

        if (aura is not null)
            SelectAura(aura.Definition.Id);
        else if (group is not null)
            SelectGroup(group.Definition.Id);
    }

    // ---------------------------------------------------------------- group editor

    private void DrawGroupEditor(AuraEngine engine, AuraGroupRuntime group, GameSnapshot snapshot)
    {
        var definition = group.Definition;

        // The template holds the display settings and the load conditions of the whole group.
        var template = definition.Template ??= new AuraDefinition();

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
            if (EnumCombo("Growth", ref growth, GrowthLabel))
            {
                definition.Growth = growth;
                plugin.RequestSave();
            }

            var spacing = definition.Spacing;
            if (ImGui.InputInt("Spacing (px)", ref spacing, 1, 4))
            {
                definition.Spacing = Math.Clamp(spacing, 0, 128);
                plugin.RequestSave();
            }

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "Drag the auras in the list on the left to change their order.");
        });

        // The same cards as for one aura, but what is set here is applied to every aura of the group.
        DrawDisplay(engine, template, null, group);
        DrawLoad(engine, template, snapshot, group);

        if (pendingDeleteId == definition.Id)
        {
            if (ImGui.Button("Click again to delete this group"))
            {
                engine.RemoveGroup(group);
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

        ImGui.TextColored(Theme.TextMuted, "Deleting a group keeps its auras; they stay in the list.");
    }

    // ---------------------------------------------------------------- general

    private void DrawGeneral(AuraEngine engine, AuraRuntime runtime)
    {
        var definition = runtime.Definition;

        Widgets.Card("##card_aura_general", 4, () =>
        {
            Widgets.SectionLabel("General");

            var enabled = definition.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                definition.Enabled = enabled;
                plugin.RequestSave();
            }

            var name = definition.Name;
            if (ImGui.InputText("Name", ref name, 64))
            {
                definition.Name = name;
                plugin.RequestSave();
            }
        });
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

    private static string ConditionLabel(ConditionKind kind) => kind switch
    {
        ConditionKind.StatusPresent => "An enhancement or enfeeblement is on",
        ConditionKind.StatusMissing => "An enhancement or enfeeblement is missing",
        ConditionKind.AbilityActivated => "An ability is activated",
        ConditionKind.AbilityOnCooldown => "An ability is recharging",
        ConditionKind.AbilityReady => "An ability is ready",
        _ => "An ability, always (ready or recharging)"
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

    // The icon triggers of an aura, in order of priority. The first one that matches decides the picture and the glow.
    private void DrawRules(AuraDefinition definition, GameSnapshot snapshot)
    {
        EnsureAbilities(snapshot);

        var moveFrom = -1;
        var moveTo = -1;
        var remove = -1;

        ruleRects.Clear();

        // All the triggers live in one card: trigger 1, then "if not" trigger 2, and so on.
        Widgets.Card("##card_aura_triggers", 22 * definition.Rules.Count + 2, () =>
        {
            Widgets.SectionLabel("Icon triggers");

            for (var i = 0; i < definition.Rules.Count; i++)
            {
                if (i > 0)
                    ImGui.Separator();

                DrawRuleSection(definition, i, snapshot, ref moveFrom, ref moveTo, ref remove);
            }

            ImGui.Spacing();

            if (ImGui.Button("Add trigger"))
            {
                definition.Rules.Add(new AuraRule());
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
    private void HandleRuleDrag(AuraDefinition definition, ref int moveFrom, ref int moveTo)
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

    private void DrawRuleSection(AuraDefinition definition, int index, GameSnapshot snapshot, ref int moveFrom, ref int moveTo, ref int remove)
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

            if (count > 1)
                ImGui.TextColored(Theme.Accent, index == 0 ? "Trigger 1" : $"If not, trigger {index + 1}");

            // Order: the first trigger that matches wins. Arrows move this one up or down.
            if (count > 1)
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

            var kind = rule.Kind;
            if (EnumCombo("When", ref kind, ConditionLabel))
            {
                rule.Kind = kind;
                pick.Query = string.Empty;
                plugin.RequestSave();
            }

            if (isStatus)
                DrawStatusPart(definition, index, rule, pick, unit);
            else
                DrawAbilityPart(definition, index, rule, pick);

            ImGui.Spacing();
            DrawRuleIcon(rule, iconPick);

            ImGui.Spacing();

            var glow = rule.Glow;
            if (ImGui.Checkbox("Glow while this trigger decides", ref glow))
            {
                rule.Glow = glow;
                plugin.RequestSave();
            }

            if (rule.Kind == ConditionKind.AbilityAlways)
            {
                var glowReady = rule.GlowWhenReady;
                if (ImGui.Checkbox("Glow when the ability is ready", ref glowReady))
                {
                    rule.GlowWhenReady = glowReady;
                    plugin.RequestSave();
                }
            }

            if (rule.IsAbility)
            {
                var glowHighlighted = rule.GlowWhenHighlighted;
                if (ImGui.Checkbox("Glow when the game highlights it", ref glowHighlighted))
                {
                    rule.GlowWhenHighlighted = glowHighlighted;
                    plugin.RequestSave();
                }

                if (rule.Kind != ConditionKind.AbilityActivated)
                {
                    var ignoreGcd = rule.IgnoreGlobalCooldown;
                    if (ImGui.Checkbox("Ignore the global cooldown", ref ignoreGcd))
                    {
                        rule.IgnoreGlobalCooldown = ignoreGcd;
                        plugin.RequestSave();
                    }
                }
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

    private void DrawStatusPart(AuraDefinition definition, int index, AuraRule rule, TextSearch pick, UnitSnapshot? unit)
    {
        var watched = rule.Unit;
        if (EnumCombo("Watch", ref watched, UnitLabel))
        {
            rule.Unit = watched;
            plugin.RequestSave();
        }

        var onlyMine = rule.OnlyMine;
        if (ImGui.Checkbox("Only statuses I applied", ref onlyMine))
        {
            rule.OnlyMine = onlyMine;
            plugin.RequestSave();
        }

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

    private void DrawAbilityPart(AuraDefinition definition, int index, AuraRule rule, TextSearch pick)
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

    private static string IconSourceLabel(IconSource source) => source switch
    {
        IconSource.Automatic => "The icon of what it tracks",
        IconSource.Status => "The icon of a status I choose",
        _ => "The icon of an ability I choose"
    };

    // Any game icon can be the picture of a trigger, whatever it tracks: the icon of Requiescat's status has the stack number drawn
    // in it, so another status icon (without the number) can be used while the stacks are shown as a text of our own.
    private void DrawRuleIcon(AuraRule rule, TextSearch iconPick)
    {
        var source = rule.Icon;
        if (EnumCombo("Icon", ref source, IconSourceLabel))
        {
            rule.Icon = source;
            iconPick.Query = string.Empty;
            plugin.RequestSave();
        }

        if (rule.Icon == IconSource.Automatic)
            return;

        var isStatus = rule.Icon == IconSource.Status;
        var chosen = isStatus ? rule.IconStatusId : rule.IconActionId;

        if (chosen == 0)
            ImGui.TextColored(Theme.Warning, isStatus ? "No status selected yet." : "No ability selected yet.");
        else
            ImGui.Text($"Selected: {(isStatus ? GameNames.StatusName(chosen) : GameNames.ActionName(chosen))} ({chosen})");

        var query = iconPick.Query;
        if (ImGui.InputText(isStatus ? "Search status icon by name" : "Search ability icon by name", ref query, 64))
            iconPick.Query = query;

        foreach (var result in iconPick.Statuses)
        {
            if (ImGui.Selectable($"{result.Name} ({result.Id})##icon_status_{result.Id}", rule.IconStatusId == result.Id))
            {
                rule.IconStatusId = result.Id;
                plugin.RequestSave();
            }
        }

        foreach (var result in iconPick.Actions)
        {
            if (ImGui.Selectable($"{result.Name} ({result.Id})##icon_action_{result.Id}", rule.IconActionId == result.Id))
            {
                rule.IconActionId = result.Id;
                plugin.RequestSave();
            }
        }
    }

    private void PickStatus(AuraDefinition definition, int index, AuraRule rule, uint statusId)
    {
        rule.StatusId = statusId;

        // A brand new aura takes the name of the first thing it watches.
        if (index == 0 && definition.Name == DefaultName)
            definition.Name = GameNames.StatusName(statusId);

        plugin.RequestSave();
    }

    private void PickAction(AuraDefinition definition, int index, AuraRule rule, uint actionId)
    {
        rule.ActionId = actionId;

        if (index == 0 && definition.Name == DefaultName)
            definition.Name = GameNames.ActionName(actionId);

        plugin.RequestSave();
    }

    // ---------------------------------------------------------------- texts over the icon

    private static string TextSourceLabel(TextSource source) => source switch
    {
        TextSource.OwnTime => "The time of the trigger that decides",
        TextSource.OwnStacks => "The stacks or charges of the trigger that decides",
        TextSource.ComboTime => "The combo time",
        TextSource.StatusTime => "The time of a status I choose",
        TextSource.StatusStacks => "The stacks of a status I choose",
        TextSource.AbilityCooldown => "The cooldown of an ability I choose",
        _ => "The charges of an ability I choose"
    };

    // The texts of an aura: Text 1, Text 2... Each one has its own sources (the first with a value wins) and look, and sits over the icon.
    private void DrawTexts(AuraDefinition definition)
    {
        for (var i = 0; i < definition.Texts.Count; i++)
        {
            if (DrawTextCard(definition, i))
                break;
        }

        if (ImGui.Button("Add text"))
        {
            // A new text starts at the bottom right corner (no offset), so it does not hide behind Text 1 in the centre.
            definition.Texts.Add(new AuraTextDefinition
            {
                Sources = { new AuraTextSource { Source = TextSource.OwnStacks } },
                Anchor = AnchorPoint.BottomRight
            });

            plugin.RequestSave();
        }

        ImGui.Spacing();
    }

    // One text card. Returns true when the text was removed (the list changed, so the caller stops walking it).
    private bool DrawTextCard(AuraDefinition definition, int index)
    {
        var text = definition.Texts[index];
        var removed = false;

        var moveFrom = -1;
        var moveTo = -1;
        var removeSource = -1;

        var searches = new TextSearch[text.Sources.Count];
        var lines = 12;

        for (var j = 0; j < text.Sources.Count; j++)
        {
            var source = text.Sources[j];
            var needsStatus = source.Source is TextSource.StatusTime or TextSource.StatusStacks;
            var needsAbility = source.Source is TextSource.AbilityCooldown or TextSource.AbilityCharges;

            searches[j] = SearchOf($"{definition.Id}:text{index}:{j}");
            RunSearch(searches[j], needsStatus, needsAbility, (int)source.Source);

            lines += 2 + ((needsStatus || needsAbility) ? 3 + searches[j].Statuses.Count + searches[j].Actions.Count : 0);
        }

        Widgets.Card($"##card_aura_text_{index}", lines, () =>
        {
            ImGui.PushID(index);

            Widgets.SectionLabel($"Text {index + 1}");

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
                text.Sources.Add(new AuraTextSource());
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

            var font = text.Font;
            if (EnumCombo("Text font", ref font, FontLabel))
            {
                text.Font = font;
                plugin.RequestSave();
            }

            var color = text.Color;
            if (ImGui.ColorEdit4("Text color", ref color, ImGuiColorEditFlags.NoAlpha))
            {
                text.Color = new Vector4(color.X, color.Y, color.Z, 1f);
                plugin.RequestSave();
            }

            if (ImGui.Button("Remove this text"))
                removed = true;

            ImGui.PopID();
        });

        // The list of sources changes only after the card is drawn.
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

        definition.Texts.RemoveAt(index);
        textSearches.Clear();
        plugin.RequestSave();
        return true;
    }

    // One source of a text: where the value comes from, with arrows to change its priority.
    private void DrawSourceRow(AuraTextDefinition text, int j, TextSearch search, ref int moveFrom, ref int moveTo, ref int removeSource)
    {
        var source = text.Sources[j];
        var count = text.Sources.Count;

        var needsStatus = source.Source is TextSource.StatusTime or TextSource.StatusStacks;
        var needsAbility = source.Source is TextSource.AbilityCooldown or TextSource.AbilityCharges;

        if (count > 1)
            ImGui.TextColored(Theme.Accent, j == 0 ? "Source 1" : $"If not, source {j + 1}");

        var choice = source.Source;
        if (EnumCombo("Text from", ref choice, TextSourceLabel))
        {
            source.Source = choice;
            search.Query = string.Empty;
            plugin.RequestSave();
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

    // The display settings of one aura (runtime given), or of a whole group (group given, definition = its template).
    // For a group, every change is copied to all the auras of the group.
    private void DrawDisplay(AuraEngine engine, AuraDefinition definition, AuraRuntime? runtime, AuraGroupRuntime? group)
    {
        var isGroup = group is not null;

        // A group can hold both kinds of aura, so it shows every option.
        var hasAbility = isGroup || definition.Rules.Any(r => r.IsAbility);

        // The size of an icon belongs to the layout system (the same size you see in unlock mode).
        var elementLayout = runtime?.Element.Layout;

        var lines = 8 + (isGroup ? 0 : 4) + (hasAbility ? 3 : 0) + (isGroup ? 3 : 0);
        var changed = false;

        Widgets.Card(isGroup ? "##card_group_display" : "##card_aura_display", lines, () =>
        {
            Widgets.SectionLabel(isGroup ? "Display (every aura in the group)" : "Display");

            if (group is not null)
            {
                // One size for every aura of the group at once.
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
            }
            else if (elementLayout is not null)
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

            var iconCrop = definition.IconCropPercent;
            if (ImGui.SliderInt("Icon crop (%)", ref iconCrop, 0, 30))
            {
                definition.IconCropPercent = iconCrop;
                changed = true;
            }

            var fillWidth = definition.FillWidth;
            if (ImGui.Checkbox("Fill the width (cut the icon's side margins)", ref fillWidth))
            {
                definition.FillWidth = fillWidth;
                changed = true;
            }

            var cornerRadius = definition.CornerRadiusPx;
            if (ImGui.SliderInt("Corner rounding (px)", ref cornerRadius, 0, 12))
            {
                definition.CornerRadiusPx = cornerRadius;
                changed = true;
            }

            if (hasAbility)
            {
                ImGui.Spacing();

                var look = definition.CooldownLook;
                if (EnumCombo("While recharging", ref look, CooldownLookLabel))
                {
                    definition.CooldownLook = look;
                    changed = true;
                }

                var sweep = definition.ShowCooldownSweep;
                if (ImGui.Checkbox("Show the recharge sweep", ref sweep))
                {
                    definition.ShowCooldownSweep = sweep;
                    changed = true;
                }
            }

            if (isGroup)
            {
                ImGui.Spacing();
                ImGui.TextColored(Theme.TextMuted, "A change here overwrites that setting in every aura of the group,");
                ImGui.TextColored(Theme.TextMuted, "and auras that join the group later take these values too.");
            }
        });

        if (!changed)
            return;

        if (group is not null)
            engine.ApplyGroupStyle(group);
        else
            plugin.RequestSave();
    }

    // ---------------------------------------------------------------- load conditions

    // The load conditions of one aura, or of a whole group (group given, definition = its template).
    // For a group, every change is copied to all the auras of the group.
    // The jobs of the game by role, to choose which ones an aura loads on.
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

    // The content of the job menu: a row of checkboxes for each role, with All / None for the whole role.
    private static bool DrawJobPicker(List<uint> selected)
    {
        var changed = false;

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

        if (ImGui.Button("Clear (every job)##clear_jobs"))
        {
            selected.Clear();
            changed = true;
        }

        return changed;
    }

    private void DrawLoad(AuraEngine engine, AuraDefinition definition, GameSnapshot snapshot, AuraGroupRuntime? group)
    {
        var isGroup = group is not null;
        var changed = false;

        Widgets.Card(isGroup ? "##card_group_load" : "##card_aura_load", isGroup ? 10 : 8, () =>
        {
            Widgets.SectionLabel(isGroup ? "Load conditions (every aura in the group)" : "Load conditions");

            var combat = definition.Load.Combat;
            if (EnumCombo("Combat", ref combat, CombatLabel))
            {
                definition.Load.Combat = combat;
                changed = true;
            }

            ImGui.Spacing();

            if (definition.Load.JobIds.Count == 0)
                ImGui.TextColored(Theme.TextMuted, "Loads on every job. Add jobs to limit it (click a job to remove it):");
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
            var popupId = isGroup ? "##jobs_popup_group" : "##jobs_popup_aura";
            if (ImGui.Button("Choose jobs..."))
                ImGui.OpenPopup(popupId);

            if (ImGui.BeginPopup(popupId))
            {
                if (DrawJobPicker(definition.Load.JobIds))
                    changed = true;

                ImGui.EndPopup();
            }

            if (isGroup)
            {
                ImGui.Spacing();
                ImGui.TextColored(Theme.TextMuted, "A change here overwrites the load conditions of every aura in the group.");
            }
        });

        if (!changed)
            return;

        if (group is not null)
            engine.ApplyGroupStyle(group);
        else
            plugin.RequestSave();
    }

    // ---------------------------------------------------------------- delete

    private void DrawDelete(AuraEngine engine, AuraRuntime runtime)
    {
        var id = runtime.Definition.Id;

        if (pendingDeleteId == id)
        {
            if (ImGui.Button("Click again to delete this aura"))
            {
                engine.Remove(runtime);
                selectedAuraId = null;
                pendingDeleteId = null;
                return;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                pendingDeleteId = null;
        }
        else if (ImGui.Button("Delete aura"))
        {
            pendingDeleteId = id;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static UnitSnapshot? UnitOf(AuraUnit unit, GameSnapshot snapshot) => unit switch
    {
        AuraUnit.Player => snapshot.Player,
        AuraUnit.Target => snapshot.Target,
        AuraUnit.Focus => snapshot.Focus,
        AuraUnit.TargetOfTarget => snapshot.TargetOfTarget,
        _ => null
    };

    private static string UnitLabel(AuraUnit unit) => unit switch
    {
        AuraUnit.Player => "Player",
        AuraUnit.Target => "Target",
        AuraUnit.Focus => "Focus target",
        AuraUnit.TargetOfTarget => "Target of target",
        _ => unit.ToString()
    };

    private static string CooldownLookLabel(CooldownLook look) => look switch
    {
        CooldownLook.Normal => "Normal",
        CooldownLook.Dimmed => "Dimmed",
        CooldownLook.Red => "Red",
        _ => look.ToString()
    };

    private static string GrowthLabel(GroupGrowth growth) => growth switch
    {
        GroupGrowth.ToRight => "Starts at the left, grows to the right",
        GroupGrowth.ToLeft => "Starts at the right, grows to the left",
        GroupGrowth.FromCenterHorizontal => "From the center, horizontal",
        GroupGrowth.Down => "Starts at the top, grows down",
        GroupGrowth.Up => "Starts at the bottom, grows up",
        GroupGrowth.FromCenterVertical => "From the center, vertical",
        _ => growth.ToString()
    };

    private static string FontLabel(OverlayFont font) => font switch
    {
        OverlayFont.GameNumbers => "Game numbers (Meidinger)",
        OverlayFont.GameUi => "Game interface (Axis)",
        OverlayFont.GameUiBold => "Game interface, bold (Axis)",
        OverlayFont.Condensed => "Condensed (Trump Gothic)",
        OverlayFont.NahjaDisplay => "NahjaUI display (Saira Condensed)",
        OverlayFont.NahjaDisplayBold => "NahjaUI numbers (Saira Condensed, bold)",
        OverlayFont.NahjaText => "NahjaUI text (Saira)",
        _ => font.ToString()
    };

    private static string CombatLabel(AuraCombatMode mode) => mode switch
    {
        AuraCombatMode.Any => "Always",
        AuraCombatMode.InCombat => "Only in combat",
        AuraCombatMode.OutOfCombat => "Only out of combat",
        _ => mode.ToString()
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
    private static bool EnumCombo<T>(string label, ref T value, Func<T, string> labeler) where T : struct, Enum
    {
        var changed = false;

        if (ImGui.BeginCombo(label, labeler(value), ImGuiComboFlags.None))
        {
            foreach (var option in Enum.GetValues<T>())
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
