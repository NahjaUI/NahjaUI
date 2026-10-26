using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace NahjaUI.Game;

/// <summary>
/// A tool for the Debug page only: the names of the game's own windows and whether each one is visible.
/// It is how the names of the game's target and focus bars are found (to hide them) without guessing.
/// Nothing in the interface depends on it.
/// </summary>
internal static unsafe class GameWindowList
{
    // The list holds up to 256 windows, each a pointer, starting 8 bytes into the list.
    private const int MaxEntries = 256;
    private const int EntriesOffset = 0x8;

    // A window's name is a text of up to 32 bytes, 8 bytes into the window.
    private const int NameOffset = 0x8;

    public static List<(string Name, bool Visible)> Read()
    {
        var result = new List<(string Name, bool Visible)>();

        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return result;

        // The rapture manager is an AtkUnitManager with more fields after it, so both start at the same place.
        var list = &((AtkUnitManager*)manager)->AllLoadedUnitsList;
        var count = Math.Min((int)list->Count, MaxEntries);

        for (var i = 0; i < count; i++)
        {
            var unit = *(AtkUnitBase**)((byte*)list + EntriesOffset + i * sizeof(nint));
            if (unit == null)
                continue;

            var name = Marshal.PtrToStringUTF8((nint)((byte*)unit + NameOffset)) ?? string.Empty;
            result.Add((name, unit->IsVisible));
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }
}
