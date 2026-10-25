using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Ui;

namespace NahjaUI.Core.Layout;

/// <summary>
/// Unlock mode, in the style of ElvUI movers:
/// every element is a box with a tag outside it (name and coordinates), and the selected element
/// gets a small panel next to it with exact X/Y fields, Locked and Reset position.
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

    public UnlockOverlay(Plugin plugin, LayoutManager layout)
    {
        this.plugin = plugin;
        this.config = plugin.Configuration;
        this.layout = layout;
    }

    public void Draw()
    {
        if (!layout.UnlockMode)
            return;

        using var theme = new ThemeScope(config);
        using var contentStyle = new ContentStyleScope();

        DrawGuides();

        foreach (var element in layout.Elements)
            DrawElement(element);

        foreach (var element in layout.Elements)
            DrawTag(element);

        DrawInspector();
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

    // The draggable box of one element. It has no text: the tag is drawn outside, so any size works.
    private void DrawElement(LayoutElement element)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var data = element.Layout;
        var (pos, size) = LayoutManager.GetRect(data);
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
                    ImGuiWindowFlags.NoBackground;

        var open = true;
        if (ImGui.Begin($"##nui_layout_{element.Id}", ref open, flags))
        {
            ImGui.SetCursorPos(Vector2.Zero);
            ImGui.InvisibleButton("##drag", size);

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

    // A small label with the name and coordinates, always outside the box, whatever its size.
    private void DrawTag(LayoutElement element)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var data = element.Layout;
        var (pos, size) = LayoutManager.GetRect(data);
        var selected = layout.SelectedId == element.Id;
        var viewport = ImGui.GetMainViewport();

        var text = $"{element.Name}   X {data.OffsetX}   Y {data.OffsetY}";
        if (data.Locked)
            text += "   (locked)";

        var padding = new Vector2(6f, 3f) * scale;
        var tagSize = ImGui.CalcTextSize(text) + padding * 2f;

        // Above the box; below it when there is no room at the top of the screen.
        var y = pos.Y - tagSize.Y - 3f * scale;
        if (y < viewport.Pos.Y)
            y = pos.Y + size.Y + 3f * scale;

        var maxX = Math.Max(viewport.Pos.X, viewport.Pos.X + viewport.Size.X - tagSize.X);
        var x = Math.Clamp(pos.X, viewport.Pos.X, maxX);

        var tagPos = new Vector2(MathF.Round(x), MathF.Round(y));
        var accent = data.Locked ? Theme.Warning : (selected ? Theme.Accent : Theme.Info);

        var drawList = ImGui.GetBackgroundDrawList();
        drawList.AddRectFilled(tagPos, tagPos + tagSize, ImGui.GetColorU32(Theme.WithAlpha(Theme.Card, 0.95f)), 4f * scale);
        drawList.AddRect(tagPos, tagPos + tagSize, ImGui.GetColorU32(accent), 4f * scale, ImDrawFlags.None, 1f);
        drawList.AddText(tagPos + padding, ImGui.GetColorU32(Theme.Text), text);
    }

    // ---------------------------------------------------------------- inspector

    // Panel next to the selected element: exact X/Y fields, Locked and Reset position.
    private void DrawInspector()
    {
        var selected = layout.SelectedElement;
        if (selected is null)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var data = selected.Layout;
        var (pos, size) = LayoutManager.GetRect(data);
        var viewport = ImGui.GetMainViewport();

        // Where to put the panel: under the box (and its tag), or above it when the box is near the bottom.
        var panelWidth = 300f * scale;
        var estimatedHeight = 150f * scale;

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
            if (ImGui.InputInt("X", ref x, 1, 10) && !data.Locked)
            {
                data.OffsetX = x;
                plugin.RequestSave();
            }

            ImGui.SameLine();

            ImGui.SetNextItemWidth(fieldWidth);
            var y = data.OffsetY;
            if (ImGui.InputInt("Y", ref y, 1, 10) && !data.Locked)
            {
                data.OffsetY = y;
                plugin.RequestSave();
            }

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
        }

        ImGui.End();
    }

    // ---------------------------------------------------------------- toolbar

    // Floating bar at the top of the screen with the Done button.
    private void DrawToolbar()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var viewport = ImGui.GetMainViewport();

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(
            new Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + 24f * scale),
            ImGuiCond.FirstUseEver,
            new Vector2(0.5f, 0f));

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse;

        var open = true;
        if (ImGui.Begin("##nui_unlock_bar", ref open, flags))
        {
            ImGui.TextColored(Theme.Accent, "Unlock mode");
            ImGui.TextColored(Theme.TextMuted, "Click a box to select it. Drag to move it, or type exact values in the panel.");
            ImGui.Spacing();

            if (Widgets.PrimaryButton("Done"))
                layout.UnlockMode = false;
        }

        ImGui.End();
    }
}
