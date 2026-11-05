using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace NahjaUI.Game;

/// <summary>
/// A tool for the Debug page only: it watches the game's right click menus and keeps what each one held when it opened (its values and
/// texts), so the entries can be read after the menu has closed. It is how we find out whether our own menu can copy the game's.
/// </summary>
internal static unsafe class ContextMenuCapture
{
    // The game's menu window and its sub menu.
    private static readonly string[] Names = { "ContextMenu", "AddonContextSub" };
    private static readonly bool[] WasVisible = new bool[Names.Length];
    private const int MaxCaptures = 8;

    public sealed record Capture(string Time, string Window, List<string> Lines);

    public static List<Capture> Captures { get; } = new();

    /// <summary>Called once per frame.</summary>
    public static void Update()
    {
        try
        {
            for (var i = 0; i < Names.Length; i++)
            {
                var pointer = Plugin.GameGui.GetAddonByName(Names[i]);
                var visible = pointer.Address != nint.Zero && ((AtkUnitBase*)pointer.Address)->IsVisible;

                // One capture per opening, a few frames after it appeared so the entries are already filled in.
                if (visible)
                {
                    openFrames[i]++;
                    if (openFrames[i] == 6)
                    {
                        Captures.Insert(0, new Capture(DateTime.Now.ToString("HH:mm:ss"), Names[i], AddonDump.Read(Names[i])));
                        if (Captures.Count > MaxCaptures)
                            Captures.RemoveAt(Captures.Count - 1);
                    }
                }
                else
                {
                    openFrames[i] = 0;
                }

                WasVisible[i] = visible;
            }
        }
        catch
        {
            // A debugging aid must never break the interface.
        }
    }

    private static readonly int[] openFrames = new int[Names.Length];

    public static void Clear() => Captures.Clear();

    public static string AsText()
    {
        var text = new System.Text.StringBuilder();
        foreach (var capture in Captures)
        {
            text.AppendLine($"== {capture.Window} at {capture.Time}");
            foreach (var line in capture.Lines)
                text.AppendLine(line);
        }

        return text.ToString();
    }
}
