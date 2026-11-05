using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace NahjaUI.Game;

/// <summary>
/// A tool for the Debug page only: what a game window holds right now (its values and the texts it shows). It is how the numbers
/// of a game window (the PvP gauges, for example) are found before reading them. Nothing in the interface depends on it.
/// </summary>
internal static unsafe class AddonDump
{
    private const int MaxLines = 200;

    public static List<string> Read(string windowName)
    {
        var lines = new List<string>();

        try
        {
            var pointer = Plugin.GameGui.GetAddonByName(windowName);
            if (pointer.Address == nint.Zero)
            {
                lines.Add("That window does not exist right now.");
                return lines;
            }

            var addon = (AtkUnitBase*)pointer.Address;
            lines.Add($"Visible: {addon->IsVisible}");

            lines.Add($"Values ({addon->AtkValuesCount}):");
            for (var i = 0; i < addon->AtkValuesCount && lines.Count < MaxLines; i++)
            {
                var value = &addon->AtkValues[i];
                lines.Add($"  [{i}] {value->Type}: {value->GetValueAsString()}");
            }

            lines.Add("Texts:");
            ReadNodes(&addon->UldManager, 0, lines);
        }
        catch (Exception ex)
        {
            lines.Add($"Could not read it: {ex.Message}");
        }

        return lines;
    }

    // The text nodes of a window, and of the components inside it (a few levels down).
    private static void ReadNodes(AtkUldManager* manager, int depth, List<string> lines)
    {
        if (manager == null || depth > 3)
            return;

        for (var i = 0; i < manager->NodeListCount && lines.Count < MaxLines; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            var type = (ushort)node->Type;
            var indent = new string(' ', 2 + depth * 2);

            if (type == 3)
            {
                var text = ((AtkTextNode*)node)->NodeText.ToString();
                if (!string.IsNullOrEmpty(text))
                    lines.Add($"{indent}node {node->NodeId}: {text}");
            }
            else if (type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                    ReadNodes(&component->UldManager, depth + 1, lines);
            }
        }
    }

    // ---------------------------------------------------------------- raw bytes comparison
    // Finds a setting that the game keeps inside a window: remember the bytes of the window and of its nodes (positions, sizes,
    // visibility), change the setting in game, and what changed is listed. Bytes that change by themselves can be ignored.

    private const int RawSize = 0xD00;
    private const int NodeSize = 0xB0;
    private static Dictionary<string, byte[]>? remembered;
    private static readonly HashSet<string> Ignored = new();

    private static Dictionary<string, byte[]>? ReadRaw(string windowName)
    {
        var pointer = Plugin.GameGui.GetAddonByName(windowName);
        if (pointer.Address == nint.Zero)
            return null;

        var result = new Dictionary<string, byte[]>();
        var bytes = new byte[RawSize];
        System.Runtime.InteropServices.Marshal.Copy(pointer.Address, bytes, 0, RawSize);
        result["window"] = bytes;

        var addon = (AtkUnitBase*)pointer.Address;
        ReadRawNodes(&addon->UldManager, "n", 0, result);
        return result;
    }

    private static void ReadRawNodes(AtkUldManager* manager, string prefix, int depth, Dictionary<string, byte[]> result)
    {
        if (manager == null || depth > 2)
            return;

        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            var key = $"{prefix}{i}(id {node->NodeId})";
            var bytes = new byte[NodeSize];
            System.Runtime.InteropServices.Marshal.Copy((nint)node, bytes, 0, NodeSize);
            result[key] = bytes;

            if ((ushort)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                    ReadRawNodes(&component->UldManager, key + "/", depth + 1, result);
            }
        }
    }

    public static bool HasRemembered => remembered is not null;

    public static void Remember(string windowName)
    {
        try { remembered = ReadRaw(windowName); Ignored.Clear(); } catch { remembered = null; }
    }

    public static void Forget()
    {
        remembered = null;
        Ignored.Clear();
    }

    public static void IgnoreChanging(string windowName)
    {
        foreach (var change in Changes(windowName, false))
            Ignored.Add(change.Key);
    }

    private static List<(string Key, string Text)> Changes(string windowName, bool skipIgnored)
    {
        var result = new List<(string, string)>();
        try
        {
            var now = ReadRaw(windowName);
            if (now is null || remembered is null)
                return result;

            foreach (var (region, before) in remembered)
            {
                if (!now.TryGetValue(region, out var after))
                {
                    result.Add(($"{region}#gone", $"{region}: node is gone"));
                    continue;
                }

                for (var i = 0; i < before.Length; i++)
                {
                    if (before[i] == after[i])
                        continue;

                    var key = $"{region}+{i:X}";
                    if (skipIgnored && Ignored.Contains(key))
                        continue;

                    result.Add((key, $"{region} +0x{i:X}: {before[i]:X2} -> {after[i]:X2}"));
                }
            }
        }
        catch { }

        return result;
    }

    // Every change, grouped one line per region (for copying and pasting into the chat).
    public static string ExportAll(string windowName)
    {
        if (remembered is null)
            return "Nothing remembered.";

        var groups = new Dictionary<string, List<string>>();
        foreach (var change in Changes(windowName, true))
        {
            var cut = change.Text.IndexOf(" +0x", StringComparison.Ordinal);
            var region = cut > 0 ? change.Text.Substring(0, cut) : change.Text;
            if (!groups.TryGetValue(region, out var list))
                groups[region] = list = new List<string>();

            list.Add(cut > 0 ? change.Text.Substring(cut + 1) : "gone");
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Window {windowName}: {groups.Count} regions changed");
        foreach (var (region, list) in groups)
            sb.AppendLine($"{region}: {string.Join(" | ", list)}");

        return sb.ToString();
    }

    public static List<string> Diff(string windowName)
    {
        var lines = new List<string>();
        if (remembered is null)
            return lines;

        if (Plugin.GameGui.GetAddonByName(windowName).Address == nint.Zero)
        {
            lines.Add("That window does not exist right now.");
            return lines;
        }

        foreach (var change in Changes(windowName, true))
        {
            lines.Add("  " + change.Text);
            if (lines.Count >= 60)
            {
                lines.Add("  ... (more changes: use \"Ignore\" and try again)");
                break;
            }
        }

        if (lines.Count == 0)
            lines.Add("  Nothing has changed.");

        return lines;
    }
}
