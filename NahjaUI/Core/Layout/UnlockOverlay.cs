using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Ui;

namespace NahjaUI.Core.Layout;

/// <summary>
/// Unlock mode, in the style of ElvUI movers: every element is a plain box. The panel with the name, the exact
/// X/Y fields, Locked and Reset position only appears next to the box under the mouse, or next to the selected one,
/// so the screen stays clean and every box is easy to find and click.
/// </summary>
internal sealed class UnlockOverlay
{
    private readonly Plugin plugin;
    private readonly Configuration config;
    private readonly LayoutManager layout;

    // State of the drag that is currently in progress.
    private Vector2 dragTotal;
    private int dragStartX;
    private int dragStartY;

    // Which box is being dragged, or is under the mouse, this frame (filled while the boxes are drawn).
    private string? activeId;
    private string? hoveredId;

    // The panel: whose it was last frame, where it is, and for how long it stays after the mouse leaves a box
    // (so the mouse has time to travel from the box to the panel).
    private string? panelId;
    private bool panelShown;
    private bool panelBusy;
    private double keepPanelUntil;
    private Vector2 panelPos;
    private Vector2 panelSize;

    private const double PanelGraceSeconds = 0.4;

    // Frames since unlock mode opened, to center the top bar a few times (see DrawToolbar).
    private int toolbarFrames;

    public UnlockOverlay(Plugin plugin, LayoutManager layout)
    {
        this.plugin = plugin;
        this.config = plugin.Configuration;
        this.layout = layout;
    }

    public void Draw()
    {
        if (!layout.UnlockMode)
        {
            toolbarFrames = 0;
            return;
        }

        using var theme = new ThemeScope(config);
        using var contentStyle = new ContentStyleScope();

        DrawGuides();

        // Elements placed by a group are not moved by hand: the group's own box is.
        // The biggest boxes are drawn first, so the small ones sit on top of them and can always be clicked.
        var movable = layout.Positionable
            .OrderByDescending(element => (long)element.Layout.Width * element.Layout.Height)
            .ToList();

        activeId = null;
        hoveredId = null;

        foreach (var element in movable)
            DrawElement(element);

        DrawInspector(movable);
        DrawToolbar();
    }

    // Thin lines through the centre of the screen, to help with alignment.
    private static void DrawGuides()
    {
        var viewport = ImGui.GetMainViewport();
        var drawList = ImGui.GetBackgroundDrawList();
        var color = ImGui.GetColorU32(Theme.WithAlpha(Theme.Accent, 0.35f));

        var centerX = MathF.Round(viewport.Pos.X + viewport.Size.X * 0.5f);
        var centerY = MathF.Round(viewport.Pos.Y + viewport.Size.Y * 0.5f);

        drawList.AddLine(
            new Vector2(centerX, viewport.Pos.Y),
            new Vector2(centerX, viewport.Pos.Y + viewport.Size.Y),
            color, 1f);

        drawList.AddLine(
            new Vector2(viewport.Pos.X, centerY),
            new Vector2(viewport.Pos.X + viewport.Size.X, centerY),
            color, 1f);
    }

    // ---------------------------------------------------------------- elements

    // The draggable box of one element. It has no text, so any size works.
    private void DrawElement(LayoutElement element)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var data = element.Layout;
        var (pos, size) = LayoutManager.GetDisplayRect(element);
        var selected = layout.SelectedId == element.Id;

        // A bare window with no padding or border, exactly the size of the element.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1f, 1f));

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus;

        var open = true;
        if (ImGui.Begin($"##nui_layout_{element.Id}", ref open, flags))
        {
            ImGui.SetCursorPos(Vector2.Zero);
            ImGui.InvisibleButton("##drag", size);

            // The box being dragged wins over the one under the mouse.
            if (ImGui.IsItemActive())
                activeId = element.Id;
            else if (ImGui.IsItemHovered())
                hoveredId = element.Id;

            // Right click on the selected box lets go of it, so the panel can follow the mouse again.
            if (selected && ImGui.IsItemClicked(ImGuiMouseButton.Right))
                layout.SelectedId = null;

            if (ImGui.IsItemActivated())
            {
                layout.SelectedId = element.Id;
                dragTotal = Vector2.Zero;
                dragStartX = data.OffsetX;
                dragStartY = data.OffsetY;
            }

            // Dragging: the mouse moves in physical pixels, offsets are logical pixels.
            if (ImGui.IsItemActive() && !data.Locked)
            {
                dragTotal += ImGui.GetIO().MouseDelta;
                data.OffsetX = dragStartX + (int)MathF.Round(dragTotal.X / scale);
                data.OffsetY = dragStartY + (int)MathF.Round(dragTotal.Y / scale);
                plugin.RequestSave();
            }

            var drawList = ImGui.GetWindowDrawList();
            var baseColor = data.Locked ? Theme.Warning : (selected ? Theme.Accent : Theme.Info);
            var fillAlpha = selected || ImGui.IsItemHovered() ? 0.35f : 0.22f;

            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Theme.WithAlpha(baseColor, fillAlpha)), 0f);
            drawList.AddRect(pos, pos + size, ImGui.GetColorU32(baseColor), 0f, ImDrawFlags.None, selected ? 2f : 1f);
        }

        ImGui.End();
        ImGui.PopStyleVar(4);
    }

    // ---------------------------------------------------------------- inspector

    // Panel next to one box: its name, exact X/Y fields, Locked and Reset position.
    // It belongs to the box being dragged or under the mouse. When the mouse is on neither, it stays for a moment
    // (and while the mouse is on the panel or a field is being edited), and then it falls back to the selected box.
    // With nothing selected and nothing under the mouse there is no panel at all.
    private void DrawInspector(List<LayoutElement> movable)
    {
        var now = ImGui.GetTime();
        var pointedId = activeId ?? hoveredId;

        LayoutElement? selected = null;

        // Once a box has been clicked, the panel stays with it, even when the mouse passes over other boxes on its way
        // to the panel. Right click on the box (or click another box) to change.
        var chosenBox = layout.SelectedElement;
        if (chosenBox is not null && movable.Contains(chosenBox))
        {
            selected = chosenBox;
            keepPanelUntil = now + PanelGraceSeconds;
        }
        else if (pointedId is not null)
        {
            selected = movable.FirstOrDefault(element => element.Id == pointedId);
            keepPanelUntil = now + PanelGraceSeconds;
        }
        else if (panelShown && panelId is not null && (panelBusy || MouseOverPanel() || now < keepPanelUntil))
        {
            selected = movable.FirstOrDefault(element => element.Id == panelId);
        }

        if (selected is null)
        {
            var chosen = layout.SelectedElement;
            if (chosen is not null && !chosen.IsManaged)
                selected = chosen;
        }

        if (selected is null)
        {
            panelShown = false;
            panelId = null;
            panelBusy = false;
            return;
        }

        panelId = selected.Id;

        var scale = ImGuiHelpers.GlobalScale;
        var data = selected.Layout;
        var (pos, size) = LayoutManager.GetDisplayRect(selected);
        var viewport = ImGui.GetMainViewport();

        // Where to put the panel: under the box (and its tag), or above it when the box is near the bottom.
        var panelWidth = 300f * scale;
        var estimatedHeight = 215f * scale;

        var minX = viewport.Pos.X + panelWidth * 0.5f;
        var maxX = Math.Max(minX, viewport.Pos.X + viewport.Size.X - panelWidth * 0.5f);
        var centerX = Math.Clamp(pos.X + size.X * 0.5f, minX, maxX);

        var below = pos.Y + size.Y + 30f * scale;
        var fitsBelow = below + estimatedHeight <= viewport.Pos.Y + viewport.Size.Y;

        var anchorY = fitsBelow ? below : pos.Y - 30f * scale;
        var pivot = fitsBelow ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(new Vector2(MathF.Round(centerX), MathF.Round(anchorY)), ImGuiCond.Always, pivot);

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove;

        var open = true;
        if (ImGui.Begin("##nui_unlock_inspector", ref open, flags))
        {
            ImGui.TextColored(Theme.Accent, selected.Name);
            ImGui.TextColored(Theme.TextMuted, "Offset in pixels from the anchor point");
            ImGui.Spacing();

            // Exact position: type a value, or use the small +/- buttons (1 px, or 10 px with Ctrl held).
            var fieldWidth = 130f * scale;

            ImGui.SetNextItemWidth(fieldWidth);
            var x = data.OffsetX;
            if (ImGui.InputInt("X", ref x, 1, 5) && !data.Locked)
            {
                data.OffsetX = x;
                plugin.RequestSave();
            }

            ImGui.SameLine();

            ImGui.SetNextItemWidth(fieldWidth);
            // Up is plus (the saved value counts downwards, so the sign is turned here).
            var y = -data.OffsetY;
            if (ImGui.InputInt("Y", ref y, 1, 5) && !data.Locked)
            {
                data.OffsetY = -y;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            // When it is shown: a common choice for every element.
            if (VisibilityControls.Draw(data, fieldWidth * 1.6f))
                plugin.RequestSave();

            ImGui.Spacing();

            var locked = data.Locked;
            if (ImGui.Checkbox("Locked", ref locked))
            {
                data.Locked = locked;
                plugin.RequestSave();
            }

            ImGui.SameLine();

            if (ImGui.Button("Reset position"))
            {
                selected.ResetToDefaults();
                plugin.RequestSave();
            }

            if (selected.DrawOptions is not null)
            {
                ImGui.Spacing();
                selected.DrawOptions();
            }

            // Remember where the panel is and whether a field is in use, for the next frame.
            panelPos = ImGui.GetWindowPos();
            panelSize = ImGui.GetWindowSize();
            panelBusy = ImGui.IsAnyItemActive() && activeId is null;
            panelShown = true;
        }

        ImGui.End();
    }

    // True while the mouse is on the panel of the last frame (with a little margin).
    private bool MouseOverPanel()
    {
        var margin = new Vector2(8f, 8f) * ImGuiHelpers.GlobalScale;
        return ImGui.IsMouseHoveringRect(panelPos - margin, panelPos + panelSize + margin, false);
    }

    // ---------------------------------------------------------------- toolbar

    // Floating bar at the top of the screen with the Done button.
    private void DrawToolbar()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var viewport = ImGui.GetMainViewport();

        // Centered at the top each time unlock mode opens. The window sizes itself to its text, and the centering
        // uses the size of the previous frame, so it is repeated for the first few frames until the size settles.
        // After that the bar stays where the user drags it.
        ImGuiHelpers.ForceNextWindowMainViewport();
        if (toolbarFrames < 5)
        {
            ImGui.SetNextWindowPos(
                new Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + 24f * scale),
                ImGuiCond.Always,
                new Vector2(0.5f, 0f));
            toolbarFrames++;
        }

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse;

        var open = true;
        if (ImGui.Begin("##nui_unlock_bar", ref open, flags))
        {
            ImGui.TextColored(Theme.Accent, "Unlock mode");
            ImGui.TextColored(Theme.TextMuted, "Point at a box to see its name and position. Click it to keep its panel open, drag it to move it, or type exact values. Right click lets go.");
            ImGui.Spacing();

            if (Widgets.PrimaryButton("Done"))
                layout.UnlockMode = false;
        }

        ImGui.End();
    }
}
