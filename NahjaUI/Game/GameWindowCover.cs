using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.UI;
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
    // Up to 256 windows, each a pointer, starting 8 bytes into the list; the name is 8 bytes into the window (as in GameWindowList).
    private const int MaxEntries = 256;
    private const int EntriesOffset = 0x8;
    private const int NameOffset = 0x8;

    // Nodes looked at per frame at most, and how deep inside components we go.
    private const int MaxNodes = 40000;
    private const int MaxDepth = 4;

    // Windows that are never cut around (the speech balloons over the characters, for example). More names can be added here.
    private static readonly HashSet<string> NeverCut = new() { "Balloon", "_Balloon", "BalloonText", "_BalloonText" };

    private readonly List<(Vector2 Min, Vector2 Max)> rects = new();
    private readonly List<string> names = new();
    private int nodesSeen;

    /// <summary>The names of the windows cut around right now, one per line (for the Debug page).</summary>
    public string NamesAsText() => string.Join("\n", names);

    /// <summary>True while at least one covering window is on the screen.</summary>
    public bool Any => rects.Count > 0;

    /// <summary>How many areas are cut right now (for the Debug page).</summary>
    public int Count => rects.Count;

    /// <summary>
    /// Reads every window of the game that is on the screen. The area of each one is the box around what it really paints
    /// (images, texts, backgrounds), not its whole rectangle, because many windows are big and mostly empty. Call once at the start of every frame.
    /// </summary>
    public void Update(Configuration configuration)
    {
        rects.Clear();
        names.Clear();

        if (!configuration.GameWindowsOnTop)
            return;

        try
        {
            nodesSeen = 0;
            var viewport = ImGui.GetMainViewport();
            var screenArea = viewport.Size.X * viewport.Size.Y;

            var manager = RaptureAtkUnitManager.Instance();
            if (manager == null)
                return;

            var list = &((AtkUnitManager*)manager)->AllLoadedUnitsList;
            var count = Math.Min((int)list->Count, MaxEntries);

            for (var i = 0; i < count && nodesSeen < MaxNodes; i++)
            {
                var unit = *(AtkUnitBase**)((byte*)list + EntriesOffset + i * sizeof(nint));
                if (unit == null || !unit->IsVisible || unit->RootNode == null || unit->Scale <= 0f)
                    continue;

                // A window the game keeps "on" but has made invisible (a hidden hotbar, a part of the HUD that is switched off).
                if (unit->Alpha == 0 || (unit->RootNode->NodeFlags & NodeFlags.Visible) == 0 || unit->RootNode->Alpha_2 < 32)
                    continue;

                // Bit 0 of the visibility flags is set on the parts of the HUD that are switched off (hotbars, alliance lists, bags, money, the
                // main menu...): the game still draws the window as "visible", but it is not on the screen.
                if ((unit->VisibilityFlags & 1) != 0)
                    continue;

                var name = Marshal.PtrToStringUTF8((nint)((byte*)unit + NameOffset)) ?? string.Empty;
                if (NeverCut.Contains(name))
                    continue;

                var box = new Box { Min = new Vector2(float.MaxValue), Max = new Vector2(float.MinValue) };
                Collect(&unit->UldManager, unit->Scale, 0, new Vector2(float.MinValue), new Vector2(float.MaxValue), ref box);

                if (box.Max.X <= box.Min.X || box.Max.Y <= box.Min.Y)
                    continue;

                // Nothing of a window shows outside its root (the minimap has a huge image inside a small round window).
                var root = unit->RootNode;
                var rootMin = new Vector2(root->ScreenX, root->ScreenY);
                var rootMax = rootMin + new Vector2(root->Width * unit->Scale * MathF.Abs(root->ScaleX), root->Height * unit->Scale * MathF.Abs(root->ScaleY));
                if (rootMax.X - rootMin.X >= 8f && rootMax.Y - rootMin.Y >= 8f)
                {
                    box.Min = Vector2.Max(box.Min, rootMin);
                    box.Max = Vector2.Min(box.Max, rootMax);

                    if (box.Max.X <= box.Min.X || box.Max.Y <= box.Min.Y)
                        continue;
                }

                // A window with a frame (the map, the character window...): nothing of it shows outside the frame. A window that was resized
                // keeps the size of its inner images, but its frame has the new size.
                var frame = (AtkResNode*)unit->WindowNode;
                if (frame != null)
                {
                    var frameMin = new Vector2(frame->ScreenX, frame->ScreenY);
                    var frameMax = frameMin + new Vector2(frame->Width * unit->Scale * MathF.Abs(frame->ScaleX), frame->Height * unit->Scale * MathF.Abs(frame->ScaleY));

                    if (frameMax.X - frameMin.X >= 40f && frameMax.Y - frameMin.Y >= 40f)
                    {
                        box.Min = Vector2.Max(box.Min, frameMin);
                        box.Max = Vector2.Min(box.Max, frameMax);

                        if (box.Max.X <= box.Min.X || box.Max.Y <= box.Min.Y)
                            continue;
                    }
                }

                // A window that fills the screen (a fade, a loading screen) would hide the whole interface: it is not a window to cut around.
                var area = (box.Max.X - box.Min.X) * (box.Max.Y - box.Min.Y);
                if (area >= screenArea * 0.85f)
                    continue;

                rects.Add((box.Min, box.Max));
                names.Add($"{name}  alpha={unit->Alpha} rootAlpha2={unit->RootNode->Alpha_2} showHide={unit->ShowHideFlags} visFlags={unit->VisibilityFlags} layer={unit->DepthLayer} painted={box.Painted} nodeAlpha2={box.MaxAlpha2} nodeColorA={box.MaxColorA} size={(int)(box.Max.X - box.Min.X)}x{(int)(box.Max.Y - box.Min.Y)}");
            }
        }
        catch
        {
            // If the windows cannot be read, nothing is cut.
            rects.Clear();
            names.Clear();
        }

        if (configuration.OutlineCoverAreas)
        {
            var drawList = ImGui.GetForegroundDrawList();
            for (var i = 0; i < rects.Count; i++)
            {
                drawList.AddRect(rects[i].Min, rects[i].Max, 0xFF0000FFu);
                drawList.AddText(rects[i].Min + new Vector2(2f, 2f), 0xFF0000FFu, names[i].Split(' ')[0]);
            }
        }
    }

    private struct Box
    {
        public Vector2 Min;
        public Vector2 Max;
        public int Painted;
        public int MaxAlpha2;
        public int MaxColorA;
    }

    // Walks up the parents of a node: false when one of them is hidden or fully transparent; the rectangle is cut by every parent that clips.
    private static bool Shown(AtkResNode* node, float scale, ref Vector2 min, ref Vector2 max)
    {
        var parent = node->ParentNode;
        for (var step = 0; parent != null && step < 32; step++, parent = parent->ParentNode)
        {
            if ((parent->NodeFlags & NodeFlags.Visible) == 0 || parent->Alpha_2 == 0)
                return false;

            if ((parent->NodeFlags & NodeFlags.Clip) == 0)
                continue;

            var parentMin = new Vector2(parent->ScreenX, parent->ScreenY);
            var parentMax = parentMin + new Vector2(parent->Width * scale * MathF.Abs(parent->ScaleX), parent->Height * scale * MathF.Abs(parent->ScaleY));
            min = Vector2.Max(min, parentMin);
            max = Vector2.Min(max, parentMax);

            if (max.X <= min.X || max.Y <= min.Y)
                return false;
        }

        return true;
    }

    // Adds to the box every node of this list that paints something and is visible; components are looked into.
    private void Collect(AtkUldManager* manager, float scale, int depth, Vector2 clipMin, Vector2 clipMax, ref Box box)
    {
        if (manager == null || manager->NodeList == null)
            return;

        for (var i = 0; i < manager->NodeListCount && nodesSeen < MaxNodes; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            nodesSeen++;

            if (!node->IsVisible())
                continue;

            var type = node->Type;
            if (type == NodeType.Image || type == NodeType.NineGrid || type == NodeType.Text || type == NodeType.Counter)
            {
                if (node->Alpha_2 == 0)
                    continue;

                var min = new Vector2(node->ScreenX, node->ScreenY);
                var max = min + new Vector2(node->Width * scale * MathF.Abs(node->ScaleX), node->Height * scale * MathF.Abs(node->ScaleY));

                // Only what really shows: a node inside a hidden or see-through parent does not, and a node inside a parent that
                // clips (the map inside its window, when the window is resized) shows only the part inside it.
                if (!Shown(node, scale, ref min, ref max))
                    continue;

                // Inside a component that is hidden further up, or that clips, only what the component shows counts.
                min = Vector2.Max(min, clipMin);
                max = Vector2.Min(max, clipMax);

                if (max.X - min.X < 1f || max.Y - min.Y < 1f)
                    continue;

                box.Min = Vector2.Min(box.Min, min);
                box.Max = Vector2.Max(box.Max, max);
                box.Painted++;
                box.MaxAlpha2 = Math.Max(box.MaxAlpha2, node->Alpha_2);
                box.MaxColorA = Math.Max(box.MaxColorA, node->Color.A);
            }
            else if ((int)type >= 1000 && depth < MaxDepth)
            {
                if (node->Alpha_2 == 0)
                    continue;

                // The component itself must show (its parents too), and what is inside it shows only inside what the parents that clip allow.
                var componentMin = new Vector2(node->ScreenX, node->ScreenY);
                var componentMax = componentMin + new Vector2(node->Width * scale * MathF.Abs(node->ScaleX), node->Height * scale * MathF.Abs(node->ScaleY));
                if (!Shown(node, scale, ref componentMin, ref componentMax))
                    continue;

                var innerMin = Vector2.Max(clipMin, (node->NodeFlags & NodeFlags.Clip) != 0 ? componentMin : new Vector2(float.MinValue));
                var innerMax = Vector2.Min(clipMax, (node->NodeFlags & NodeFlags.Clip) != 0 ? componentMax : new Vector2(float.MaxValue));

                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                    Collect(&component->UldManager, scale, depth + 1, innerMin, innerMax, ref box);
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

        // The area minus every covering window that touches it, as a list of boxes.
        var regions = new List<(Vector2 Min, Vector2 Max)> { (areaMin, areaMax) };

        foreach (var cover in rects)
        {
            if (cover.Min.X >= areaMax.X || cover.Max.X <= areaMin.X || cover.Min.Y >= areaMax.Y || cover.Max.Y <= areaMin.Y)
                continue;

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
