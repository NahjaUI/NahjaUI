using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace NahjaUI.Game;

/// <summary>
/// Knows which game windows are on the screen and cover part of it (for now the right click menu, "ContextMenu").
/// Dalamud always draws the plugin interface ABOVE the game's own windows, so without this a frame would be drawn over
/// the menu. Things that are drawn through <see cref="Draw"/> are cut around the covering windows, and the mouse
/// helpers can ask whether the cursor is over one.
/// </summary>
internal sealed unsafe class GameWindowCover
{
    // The game windows that cover things. More names can be added here.
    private static readonly string[] CoverNames = { "ContextMenu" };

    private readonly List<(Vector2 Min, Vector2 Max)> rects = new();

    /// <summary>True while at least one covering window is on the screen.</summary>
    public bool Any => rects.Count > 0;

    /// <summary>Reads the windows. Call once at the start of every frame.</summary>
    public void Update()
    {
        rects.Clear();

        foreach (var name in CoverNames)
        {
            try
            {
                var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(name).Address;
                if (addon == null || !addon->IsVisible || addon->RootNode == null)
                    continue;

                var scale = addon->Scale;
                if (scale <= 0f)
                    continue;

                var min = new Vector2(addon->X, addon->Y);
                var size = new Vector2(addon->RootNode->Width * scale, addon->RootNode->Height * scale);

                if (size.X < 1f || size.Y < 1f)
                    continue;

                rects.Add((min, min + size));
            }
            catch
            {
                // A window that cannot be read is simply not a cover.
            }
        }
    }

    /// <summary>Is the point (screen pixels) over a covering window?</summary>
    public bool Contains(Vector2 point)
    {
        foreach (var (min, max) in rects)
        {
            if (point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Runs <paramref name="draw"/> (which draws on the background draw list) so that nothing is painted over a covering
    /// window. If no window is near the area, it just runs once. Otherwise it runs once for each part of the screen
    /// that is not covered, with a clip rectangle on that part.
    /// </summary>
    public void Draw(Vector2 pos, Vector2 size, float margin, Action draw)
    {
        if (rects.Count == 0)
        {
            draw();
            return;
        }

        var areaMin = pos - new Vector2(margin);
        var areaMax = pos + size + new Vector2(margin);

        var near = false;
        foreach (var (min, max) in rects)
        {
            if (min.X < areaMax.X && max.X > areaMin.X && min.Y < areaMax.Y && max.Y > areaMin.Y)
            {
                near = true;
                break;
            }
        }

        if (!near)
        {
            draw();
            return;
        }

        // The screen minus every covering window, as a list of boxes.
        var viewport = ImGui.GetMainViewport();
        var regions = new List<(Vector2 Min, Vector2 Max)> { (viewport.Pos, viewport.Pos + viewport.Size) };

        foreach (var cover in rects)
        {
            var next = new List<(Vector2 Min, Vector2 Max)>();
            foreach (var region in regions)
                Subtract(region, cover, next);

            regions = next;
        }

        var drawList = ImGui.GetBackgroundDrawList();
        foreach (var (min, max) in regions)
        {
            // Skip the parts that cannot touch the area.
            if (min.X >= areaMax.X || max.X <= areaMin.X || min.Y >= areaMax.Y || max.Y <= areaMin.Y)
                continue;

            drawList.PushClipRect(min, max, true);
            draw();
            drawList.PopClipRect();
        }
    }

    // What is left of a box after cutting a window out of it: up to four boxes (above, below, left and right of the window).
    private static void Subtract((Vector2 Min, Vector2 Max) region, (Vector2 Min, Vector2 Max) cover, List<(Vector2 Min, Vector2 Max)> output)
    {
        if (cover.Min.X >= region.Max.X || cover.Max.X <= region.Min.X || cover.Min.Y >= region.Max.Y || cover.Max.Y <= region.Min.Y)
        {
            output.Add(region);
            return;
        }

        var coverTop = MathF.Max(cover.Min.Y, region.Min.Y);
        var coverBottom = MathF.Min(cover.Max.Y, region.Max.Y);

        if (cover.Min.Y > region.Min.Y)
            output.Add((region.Min, new Vector2(region.Max.X, cover.Min.Y)));

        if (cover.Max.Y < region.Max.Y)
            output.Add((new Vector2(region.Min.X, cover.Max.Y), region.Max));

        if (cover.Min.X > region.Min.X)
            output.Add((new Vector2(region.Min.X, coverTop), new Vector2(cover.Min.X, coverBottom)));

        if (cover.Max.X < region.Max.X)
            output.Add((new Vector2(cover.Max.X, coverTop), new Vector2(region.Max.X, coverBottom)));
    }
}
