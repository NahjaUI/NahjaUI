using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ActionSheet = Lumina.Excel.Sheets.Action;

namespace NahjaUI.Modules.HudBars;

/// <summary>
/// Mouseover actions (the "Mouseover actions" switch of the General page, off by default). The game runs every action through
/// <c>ActionManager.UseAction</c> (the hotbars, the keys, the macros). When the switch is on and the mouse is over one of our frames, the
/// target given to that call is replaced by the unit of the frame, as long as the game says the action can be used on it
/// (<c>CanUseActionOnTarget</c>: a heal can go to a party member, an attack to an enemy; an action that only works on yourself, or a heal on an
/// enemy, is left alone and goes where it would have gone). Actions that are placed on the ground (Asylum, Sacred Soil, Leylines) go to the position
/// of the unit of the frame, as a macro with the mouseover placeholder does, instead of opening the ground cursor and landing under the frame.
/// This is the only place of the plugin that changes how the game plays.
/// </summary>
internal sealed unsafe class MouseoverActions : IDisposable
{
    private readonly Plugin plugin;
    private Hook<ActionManager.Delegates.UseAction>? hook;
    private bool failureLogged;

    // True while our own call to place an action on the ground is running (the game calls UseAction again inside it).
    private bool placing;

    private static readonly Dictionary<uint, bool> GroundActions = new();

    private static bool IsGroundAction(uint actionId)
    {
        if (GroundActions.TryGetValue(actionId, out var known))
            return known;

        var ground = Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var row) && row.TargetArea;
        GroundActions[actionId] = ground;
        return ground;
    }

    public MouseoverActions(Plugin plugin)
    {
        this.plugin = plugin;

        try
        {
            hook = Plugin.GameInteropProvider.HookFromAddress<ActionManager.Delegates.UseAction>(ActionManager.Addresses.UseAction.Value, Detour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            // Without the hook the rest of the plugin works the same; only this feature is missing.
            Plugin.Log.Warning(ex, "NahjaUI: mouseover actions are not available.");
            hook?.Dispose();
            hook = null;
        }
    }

    private bool Detour(ActionManager* manager, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        try
        {
            if (plugin.Configuration.MouseoverActions && actionType == ActionType.Action && !placing)
            {
                var entityId = plugin.HudBarSystem.HoveredFrameEntityId;
                if (entityId != 0)
                {
                    var unit = Plugin.ObjectTable.SearchByEntityId(entityId);
                    if (unit is not null && IsGroundAction(actionId))
                    {
                        // On the ground: at the unit, not under the mouse (the frame is on top of the ground).
                        var position = unit.Position;
                        placing = true;
                        try
                        {
                            return manager->UseActionLocation(actionType, actionId, 0xE0000000, &position, extraParam);
                        }
                        finally
                        {
                            placing = false;
                        }
                    }

                    if (unit is not null)
                    {
                        var gameObject = (GameObject*)unit.Address;
                        if (ActionManager.CanUseActionOnTarget(actionId, gameObject))
                            targetId = gameObject->GetGameObjectId().Id;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Never let this stop an action: the original call below goes on with the target it was given.
            if (!failureLogged)
            {
                failureLogged = true;
                Plugin.Log.Warning(ex, "NahjaUI: a mouseover action could not be redirected.");
            }
        }

        return hook!.Original(manager, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
    }

    public void Dispose()
    {
        hook?.Dispose();
        hook = null;
    }
}
