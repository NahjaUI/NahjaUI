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

    // Status search
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

        if (definition.Trigger == AuraTriggerType.Cooldown)
            DrawAbility(definition, snapshot);

        DrawStatus(definition, snapshot);
        DrawDisplay(engine, definition, aura, null);
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

        rows.Clear();
        explorerHovered = ImGui.IsWindowHovered();

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

            if (target is not null)
                target.Definition.Expanded = true;

            SelectAura(created.Definition.Id);
        }

        ImGui.Spacing();

        // Rows are close together, like in a file explorer.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f * scale, RowGap * scale));

        var entries = engine.RootEntries();
        if (entries.Count == 0)
            ImGui.TextColored(Theme.TextMuted, "Nothing here yet.");

        foreach (var entry in entries)
        {
            if (entry.Group is { } group)
            {
                var members = engine.MembersOf(group);
                DrawGroupRow(group, members.Count);

                if (group.Definition.Expanded)
                {
                    foreach (var member in members)
                        DrawAuraRow(member, group);
                }
            }
            else if (entry.Aura is { } aura)
            {
                DrawAuraRow(aura, null);
            }
        }

        ImGui.PopStyleVar();

        HandleDrag(engine);
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

        var gripBox = (Min: new Vector2(min.X, min.Y), Max: new Vector2(min.X + 22f * scale, max.Y));
        var chevronBox = (Min: new Vector2(min.X + 22f * scale, min.Y), Max: new Vector2(min.X + 46f * scale, max.Y));

        var drawList = ImGui.GetWindowDrawList();
        FillRow(drawList, min, max, selected, hovered && dragKey is null, dragKey == key);

        DrawGrip(drawList, new Vector2(min.X + 11f * scale, center));
        DrawChevron(drawList, new Vector2(min.X + 34f * scale, center), definition.Expanded);
        DrawFolder(drawList, new Vector2(min.X + 52f * scale, center - 9f * scale), 18f * scale);

        var name = string.IsNullOrWhiteSpace(definition.Name) ? "Unnamed group" : definition.Name;
        var textY = center - ImGui.GetTextLineHeight() / 2f;
        drawList.AddText(new Vector2(min.X + 78f * scale, textY), ImGui.GetColorU32(Theme.Text), name);

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
            else
            {
                SelectGroup(definition.Id);

                if (Inside(gripBox.Min, gripBox.Max, mouse))
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

        var gripBox = (Min: new Vector2(min.X + indent, min.Y), Max: new Vector2(min.X + indent + 22f * scale, max.Y));

        var drawList = ImGui.GetWindowDrawList();
        FillRow(drawList, min, max, selected, hovered && dragKey is null, dragKey == key);

        DrawGrip(drawList, new Vector2(min.X + indent + 11f * scale, center));

        // The game's own icon: the ability for a cooldown aura, the status for a buff or debuff aura.
        var iconSize = 24f * scale;
        var iconMin = new Vector2(min.X + indent + 28f * scale, center - iconSize / 2f);
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
            SelectAura(definition.Id);

            if (Inside(gripBox.Min, gripBox.Max, mouse))
                StartDrag(key, mouse);
        }
    }

    // The small label at the right of an aura row.
    private static string KindTag(AuraDefinition definition)
    {
        if (!definition.Enabled)
            return "Off";

        if (definition.Trigger == AuraTriggerType.Cooldown)
            return "Cooldown";

        if (definition.StatusId == 0)
            return "No status";

        return GameNames.StatusCategory(definition.StatusId) switch
        {
            1 => "Buff",
            2 => "Debuff",
            _ => "Status"
        };
    }

    private static void DrawAuraIcon(ImDrawListPtr drawList, AuraDefinition definition, Vector2 min, float size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var isCooldown = definition.Trigger == AuraTriggerType.Cooldown;
        var max = min + new Vector2(size, size);

        var iconId = isCooldown
            ? GameNames.ActionIconId(definition.ActionId)
            : GameNames.StatusIconId(definition.StatusId);

        if (iconId == 0)
        {
            // Nothing chosen yet: an empty frame.
            drawList.AddRect(min, max, ImGui.GetColorU32(Theme.WithAlpha(Theme.TextMuted, 0.6f)), 3f * scale, ImDrawFlags.None, 1f);
            return;
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        var (uv0, uv1) = AuraEngine.IconUv(new Vector2(size, size), isCooldown ? 1f : AuraEngine.StatusIconAspect, 0.10f);

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
            ImGui.TextColored(Theme.TextMuted, "The group's box is moved in unlock mode (Layout page).");
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
        var isCooldown = definition.Trigger == AuraTriggerType.Cooldown;

        // A cooldown aura has no "watch" or "only mine" here: those belong to its optional effect (see the effect card).
        Widgets.Card("##card_aura_general", isCooldown ? 8 : 10, () =>
        {
            Widgets.SectionLabel("General");

            var name = definition.Name;
            if (ImGui.InputText("Name", ref name, 64))
            {
                definition.Name = name;
                plugin.RequestSave();
            }

            var enabled = definition.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                definition.Enabled = enabled;
                plugin.RequestSave();
            }

            var trigger = definition.Trigger;
            if (EnumCombo("Tracks", ref trigger, TriggerLabel))
            {
                definition.Trigger = trigger;
                plugin.RequestSave();
            }

            // An aura in a group is placed by the group. Without a group it has its own position.
            var currentGroup = engine.FindGroup(definition.GroupId);
            if (ImGui.BeginCombo("Group", currentGroup?.Definition.Name ?? NoGroupLabel, ImGuiComboFlags.None))
            {
                if (ImGui.Selectable(NoGroupLabel, currentGroup is null) && currentGroup is not null)
                    engine.MoveAuraToRoot(runtime, AuraEngine.GroupKey(currentGroup.Definition), true);

                foreach (var group in engine.Groups)
                {
                    if (ImGui.Selectable($"{group.Definition.Name}##pick_{group.Definition.Id}", currentGroup == group) && currentGroup != group)
                    {
                        engine.MoveAuraIntoGroup(runtime, group, null, false);
                        group.Definition.Expanded = true;
                    }
                }

                ImGui.EndCombo();
            }

            if (isCooldown)
            {
                var show = definition.CooldownShow;
                if (EnumCombo("Show the icon", ref show, CooldownShowLabel))
                {
                    definition.CooldownShow = show;
                    plugin.RequestSave();
                }
            }
            else
            {
                var unit = definition.Unit;
                if (EnumCombo("Watch", ref unit, UnitLabel))
                {
                    definition.Unit = unit;
                    plugin.RequestSave();
                }

                var mode = definition.ShowMode;
                if (EnumCombo("Show the icon", ref mode, ShowModeLabel))
                {
                    definition.ShowMode = mode;
                    plugin.RequestSave();
                }

                var onlyMine = definition.OnlyMine;
                if (ImGui.Checkbox("Only statuses I applied", ref onlyMine))
                {
                    definition.OnlyMine = onlyMine;
                    plugin.RequestSave();
                }
            }
        });
    }

    // ---------------------------------------------------------------- ability picker (cooldown auras)

    private void DrawAbility(AuraDefinition definition, GameSnapshot snapshot)
    {
        // The abilities of the current job are listed again only when the job or the level changes.
        var jobId = snapshot.Player?.JobId ?? 0;
        var level = snapshot.Player?.Level ?? 0;

        if (jobId != abilitiesJobId || level != abilitiesLevel)
        {
            abilitiesJobId = jobId;
            abilitiesLevel = level;
            abilities = jobId == 0 ? Array.Empty<ActionInfo>() : GameNames.AbilitiesOfJob(jobId, level);
        }

        // Run the name search only when the text changed.
        if (actionSearch != appliedActionSearch)
        {
            appliedActionSearch = actionSearch;
            actionResults = actionSearch.Trim().Length >= 3
                ? GameNames.SearchActions(actionSearch.Trim(), MaxSearchResults)
                : Array.Empty<ActionInfo>();
        }

        var lines = 10 + AbilityListRows + actionResults.Count;

        Widgets.Card("##card_aura_ability", lines, () =>
        {
            Widgets.SectionLabel("Ability to track");

            if (definition.ActionId == 0)
                ImGui.TextColored(Theme.Warning, "No ability selected yet.");
            else
                ImGui.Text($"Selected: {GameNames.ActionName(definition.ActionId)} ({definition.ActionId})");

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "Abilities of your job with a real cooldown (click one):");

            var scale = ImGuiHelpers.GlobalScale;
            var listHeight = ImGui.GetTextLineHeightWithSpacing() * AbilityListRows + 8f * scale;

            using (var list = ImRaii.Child("##ability_list", new Vector2(0f, listHeight), true))
            {
                if (list.Success)
                {
                    if (abilities.Count == 0)
                        ImGui.TextColored(Theme.TextMuted, "Nothing to list. Log in, or use the search below.");

                    foreach (var ability in abilities)
                    {
                        var charges = ability.MaxCharges > 1 ? $"   x{ability.MaxCharges} charges" : string.Empty;
                        var label = $"{ability.Name}   {ability.RecastSeconds:0}s{charges}##ability_{ability.Id}";

                        if (ImGui.Selectable(label, definition.ActionId == ability.Id))
                            PickAction(definition, ability.Id);
                    }
                }
            }

            ImGui.Spacing();

            var text = actionSearch;
            if (ImGui.InputText("Or search any ability", ref text, 64))
                actionSearch = text;

            foreach (var result in actionResults)
            {
                var label = $"{result.Name} ({result.Id})   {result.RecastSeconds:0}s##actionresult_{result.Id}";
                if (ImGui.Selectable(label, definition.ActionId == result.Id))
                    PickAction(definition, result.Id);
            }
        });
    }

    private void PickAction(AuraDefinition definition, uint actionId)
    {
        definition.ActionId = actionId;

        // A brand new aura takes the name of the ability it watches.
        if (definition.Name == DefaultName)
            definition.Name = GameNames.ActionName(actionId);

        plugin.RequestSave();
    }

    // ---------------------------------------------------------------- display

    // The display settings of one aura (runtime given), or of a whole group (group given, definition = its template).
    // For a group, every change is copied to all the auras of the group.
    private void DrawDisplay(AuraEngine engine, AuraDefinition definition, AuraRuntime? runtime, AuraGroupRuntime? group)
    {
        var isGroup = group is not null;

        // A group can hold both kinds of aura, so it shows every option.
        var isCooldown = isGroup || definition.Trigger == AuraTriggerType.Cooldown;

        // The size of an icon belongs to the layout system (the same size you see in unlock mode).
        var elementLayout = runtime?.Element.Layout;
        var isSquare = elementLayout is null || elementLayout.Width == elementLayout.Height;

        var lines = (isSquare ? 12 : 13) + (isCooldown ? 6 : 0) + (isGroup ? 4 : 0) + 9;
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
                // A square icon. The +/- buttons move by 2 px (10 px with Ctrl); you can also type a value.
                var iconSize = elementLayout.Width;
                if (ImGui.InputInt("Icon size (px)", ref iconSize, 2, 10))
                {
                    iconSize = Math.Clamp(iconSize, 1, 512);
                    elementLayout.Width = iconSize;
                    elementLayout.Height = iconSize;
                    plugin.RequestSave();
                }

                if (!isSquare)
                    ImGui.TextColored(Theme.TextMuted, $"Now {elementLayout.Width} x {elementLayout.Height}. Changing the size makes it square.");
            }

            var iconCrop = definition.IconCropPercent;
            if (ImGui.SliderInt("Icon crop (%)", ref iconCrop, 0, 30))
            {
                definition.IconCropPercent = iconCrop;
                changed = true;
            }

            if (isCooldown)
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

                var glowReady = definition.GlowWhenReady;
                if (ImGui.Checkbox("Glow when the ability is ready", ref glowReady))
                {
                    definition.GlowWhenReady = glowReady;
                    changed = true;
                }

                var glowActive = definition.GlowWhenActive;
                if (ImGui.Checkbox("Glow while the effect is active", ref glowActive))
                {
                    definition.GlowWhenActive = glowActive;
                    changed = true;
                }

                var ignoreGcd = definition.IgnoreGlobalCooldown;
                if (ImGui.Checkbox("Ignore the global cooldown", ref ignoreGcd))
                {
                    definition.IgnoreGlobalCooldown = ignoreGcd;
                    changed = true;
                }
            }

            ImGui.Spacing();

            var showTimer = definition.ShowTimer;
            if (ImGui.Checkbox("Show the remaining time", ref showTimer))
            {
                definition.ShowTimer = showTimer;
                changed = true;
            }

            var showStacks = definition.ShowStacks;
            if (ImGui.Checkbox(isCooldown ? "Show the charges / stack count" : "Show the stack count", ref showStacks))
            {
                definition.ShowStacks = showStacks;
                changed = true;
            }

            // Font sizes are fixed steps: each one is a real font, so the text is always sharp.
            if (ImGui.BeginCombo("Text size (px)", $"{definition.TextSize} px", ImGuiComboFlags.None))
            {
                foreach (var size in OverlayFonts.AvailableSizes)
                {
                    if (ImGui.Selectable($"{size} px", definition.TextSize == size))
                    {
                        definition.TextSize = size;
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }

            var font = definition.Font;
            if (EnumCombo("Text font", ref font, FontLabel))
            {
                definition.Font = font;
                changed = true;
            }

            // Where the texts sit on the icon.
            ImGui.Spacing();

            var timerAnchor = definition.TimerAnchor;
            var timerX = definition.TimerOffsetX;
            var timerY = definition.TimerOffsetY;
            if (PlacementControls("Time", ref timerAnchor, ref timerX, ref timerY))
            {
                definition.TimerAnchor = timerAnchor;
                definition.TimerOffsetX = timerX;
                definition.TimerOffsetY = timerY;
                changed = true;
            }

            ImGui.Spacing();

            var stacksAnchor = definition.StacksAnchor;
            var stacksX = definition.StacksOffsetX;
            var stacksY = definition.StacksOffsetY;
            if (PlacementControls(isCooldown ? "Charges" : "Stacks", ref stacksAnchor, ref stacksX, ref stacksY))
            {
                definition.StacksAnchor = stacksAnchor;
                definition.StacksOffsetX = stacksX;
                definition.StacksOffsetY = stacksY;
                changed = true;
            }

            ImGui.TextColored(Theme.TextMuted, "Position: the point of the icon the text is attached to (the text's own");
            ImGui.TextColored(Theme.TextMuted, "same point goes there, so a right position right-justifies it).");
            ImGui.TextColored(Theme.TextMuted, "Offset: pixels from that point. +X is right, +Y is down.");

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

    // ---------------------------------------------------------------- status picker

    // For status auras this is the status to watch. For cooldown auras it is the optional EFFECT of the ability
    // (the buff it gives): while it is on, the icon shows how long it lasts.
    private void DrawStatus(AuraDefinition definition, GameSnapshot snapshot)
    {
        var isCooldown = definition.Trigger == AuraTriggerType.Cooldown;

        var unit = UnitOf(definition.Unit, snapshot);
        var currentCount = unit is null ? 1 : Math.Min(Math.Max(unit.Statuses.Count, 1), MaxListedStatuses);

        // Run the name search only when the text changed.
        if (search != appliedSearch)
        {
            appliedSearch = search;
            searchResults = search.Trim().Length >= 3
                ? GameNames.SearchStatuses(search.Trim(), MaxSearchResults)
                : Array.Empty<StatusSearchResult>();
        }

        var lines = 8 + currentCount + searchResults.Count + (isCooldown ? 7 : 0);

        Widgets.Card("##card_aura_status", lines, () =>
        {
            Widgets.SectionLabel(isCooldown ? "Effect while active (optional)" : "Status to watch");

            if (isCooldown)
            {
                ImGui.TextColored(Theme.TextMuted, "Pick the buff the ability gives, to show how long it lasts.");
                ImGui.TextColored(Theme.TextMuted, "Leave it empty to track only the cooldown.");
                ImGui.Spacing();

                // Where the effect lives once the ability is used.
                var effectUnit = definition.Unit;
                if (EnumCombo("Look for the effect on", ref effectUnit, UnitLabel))
                {
                    definition.Unit = effectUnit;
                    plugin.RequestSave();
                }

                ImGui.TextColored(Theme.TextMuted, "Player = on you. Target = on whoever you are targeting.");
                ImGui.Spacing();
            }

            if (definition.StatusId == 0)
            {
                ImGui.TextColored(isCooldown ? Theme.TextMuted : Theme.Warning, isCooldown ? "No effect selected." : "No status selected yet.");
            }
            else
            {
                ImGui.Text($"Selected: {GameNames.StatusName(definition.StatusId)} ({definition.StatusId})");

                if (isCooldown)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Clear##clear_effect"))
                    {
                        definition.StatusId = 0;
                        plugin.RequestSave();
                    }
                }
            }

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, $"On {UnitLabel(definition.Unit).ToLowerInvariant()} right now (click one):");

            if (unit is null || unit.Statuses.Count == 0)
            {
                ImGui.TextColored(Theme.TextMuted, unit is null ? "  Nobody there at the moment." : "  No statuses at the moment.");
            }
            else
            {
                var shown = 0;
                foreach (var status in unit.Statuses)
                {
                    if (shown >= MaxListedStatuses)
                        break;

                    var label = $"{GameNames.StatusName(status.Id)} ({status.Id})   {status.RemainingTime:0}s##cur_{shown}";
                    if (ImGui.Selectable(label, definition.StatusId == status.Id))
                        PickStatus(definition, status.Id);

                    shown++;
                }
            }

            ImGui.Spacing();

            var text = search;
            if (ImGui.InputText("Search by name", ref text, 64))
                search = text;

            foreach (var result in searchResults)
            {
                var kind = result.Category switch
                {
                    1 => "buff",
                    2 => "debuff",
                    _ => string.Empty
                };

                var label = $"{result.Name} ({result.Id})   {kind}##res_{result.Id}";
                if (ImGui.Selectable(label, definition.StatusId == result.Id))
                    PickStatus(definition, result.Id);
            }

            // Only effects the player applied (their own buffs), for statuses that other players could also apply.
            if (isCooldown && definition.StatusId != 0)
            {
                ImGui.Spacing();

                var onlyMine = definition.OnlyMine;
                if (ImGui.Checkbox("Only effects I applied", ref onlyMine))
                {
                    definition.OnlyMine = onlyMine;
                    plugin.RequestSave();
                }
            }
        });
    }

    private void PickStatus(AuraDefinition definition, uint statusId)
    {
        definition.StatusId = statusId;

        // A brand new status aura takes the name of the status it watches.
        if (definition.Trigger == AuraTriggerType.Status && definition.Name == DefaultName)
            definition.Name = GameNames.StatusName(statusId);

        plugin.RequestSave();
    }

    // ---------------------------------------------------------------- load conditions

    // The load conditions of one aura, or of a whole group (group given, definition = its template).
    // For a group, every change is copied to all the auras of the group.
    private void DrawLoad(AuraEngine engine, AuraDefinition definition, GameSnapshot snapshot, AuraGroupRuntime? group)
    {
        var isGroup = group is not null;
        var changed = false;

        Widgets.Card(isGroup ? "##card_group_load" : "##card_aura_load", isGroup ? 9 : 7, () =>
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

            var currentJob = snapshot.Player?.JobId;
            if (currentJob is { } job && !definition.Load.JobIds.Contains(job))
            {
                if (ImGui.Button($"Add my current job ({GameNames.JobAbbreviation(job)})"))
                {
                    definition.Load.JobIds.Add(job);
                    changed = true;
                }
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

    private static string TriggerLabel(AuraTriggerType trigger) => trigger switch
    {
        AuraTriggerType.Status => "A buff or debuff",
        AuraTriggerType.Cooldown => "An ability cooldown",
        _ => trigger.ToString()
    };

    private static string UnitLabel(AuraUnit unit) => unit switch
    {
        AuraUnit.Player => "Player",
        AuraUnit.Target => "Target",
        AuraUnit.Focus => "Focus target",
        AuraUnit.TargetOfTarget => "Target of target",
        _ => unit.ToString()
    };

    private static string ShowModeLabel(AuraShowMode mode) => mode switch
    {
        AuraShowMode.WhenPresent => "When the status is present",
        AuraShowMode.WhenMissing => "When the status is missing",
        _ => mode.ToString()
    };

    private static string CooldownShowLabel(CooldownShowMode mode) => mode switch
    {
        CooldownShowMode.Always => "Always (ready, recharging, active)",
        CooldownShowMode.OnlyOnCooldown => "Only while recharging or active",
        CooldownShowMode.OnlyWhenReady => "Only while ready",
        _ => mode.ToString()
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

        if (ImGui.InputInt($"{label} offset Y (px)", ref offsetY, 1, 5))
            changed = true;

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
