using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace NahjaUI.Game;

/// <summary>
/// Hides game windows for a module and gives them back when the module stops hiding them. Hiding is done right before the game
/// draws the window (<see cref="Hide"/>): the drawing is skipped and the window is marked as not visible. The game never marks it
/// visible again by itself until something shows it (a loading screen, for example), so the module has to ask for it with
/// <see cref="ShowAgain"/> every frame: it gives back the windows it hid that it no longer wants hidden.
/// </summary>
internal sealed class NativeWindowHider
{
    // The windows this hider turned from visible to not visible. Only those are given back: a window the game itself had
    // hidden (a group of the status element that the layout does not use) is never made visible.
    private readonly HashSet<string> hidden = new();

    /// <summary>Call from the PreDraw event of a window that has to be hidden.</summary>
    public unsafe void Hide(AddonArgs args)
    {
        args.PreventOriginal();

        var addon = args.GetAddonPointer<AtkUnitBase>();
        if (addon == null)
            return;

        if (addon->IsVisible)
            hidden.Add(args.AddonName);

        addon->IsVisible = false;
    }

    /// <summary>Gives back every window this hider hid (when the plugin is unloaded, so the game's own windows are not left invisible).</summary>
    public void ShowAll() => ShowAgain(_ => false);

    /// <summary>Call every frame, even when the module is off. <paramref name="stillHidden"/> says if a window must stay hidden.</summary>
    public unsafe void ShowAgain(Func<string, bool> stillHidden)
    {
        if (hidden.Count == 0)
            return;

        foreach (var name in hidden.ToArray())
        {
            if (stillHidden(name))
                continue;

            hidden.Remove(name);

            var pointer = Plugin.GameGui.GetAddonByName(name);
            if (pointer.IsNull)
                continue;

            ((AtkUnitBase*)pointer.Address)->IsVisible = true;
        }
    }
}
