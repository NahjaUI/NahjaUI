using System;
using System.Collections.Generic;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace NahjaUI.Modules.HudBars;

/// <summary>
/// Mouseover actions (the "Mouseover actions" switch of the General page, off by default). The game runs every action through
/// <c>ActionManager.UseAction</c> (the hotbars, the keys, the macros). When the switch is on and the mouse is over one of our frames, the
/// target given to that call is replaced by the unit of the frame, as long as the game says the action can be used on it
/// (<c>CanUseActionOnTarget</c>: a heal can go to a party member, an attack to an enemy; an action that only works on yourself, or a heal on an
/// enemy, is left alone and goes where it would have gone). The actions that are placed on the ground (Asylum, Sacred Soil, Earthly Star...)
/// are given the unit as their target too, which the game takes as "place it where that unit is", the same as a macro with a target
/// (<c>/ac "Asylum" &lt;t&gt;</c>). The call used when the position is chosen with the cursor (<c>UseActionLocation</c>) is not touched.
/// This is the only place of the plugin that changes how the game plays.
/// </summary>
internal sealed unsafe class MouseoverActions : IDisposable
{
    private readonly Plugin plugin;
    private Hook<ActionManager.Delegates.UseAction>? hook;
    private bool failureLogged;

    // Which actions are placed on the ground (the "TargetArea" column of the Action sheet), looked up once for each.
    private readonly Dictionary<uint, bool> groundActions = new();

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
            if (plugin.Configuration.MouseoverActions && actionType == ActionType.Action)
            {
                var entityId = plugin.HudBarSystem.HoveredFrameEntityId;
                if (entityId != 0)
                {
                    var unit = Plugin.ObjectTable.SearchByEntityId(entityId);
                    if (unit is not null)
                    {
                        var gameObject = (GameObject*)unit.Address;
                        if (IsGroundAction(actionId) || ActionManager.CanUseActionOnTarget(actionId, gameObject))
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

    private bool IsGroundAction(uint actionId)
    {
        if (groundActions.TryGetValue(actionId, out var known))
            return known;

        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        var ground = sheet is not null && sheet.TryGetRow(actionId, out var row) && row.TargetArea;
        groundActions[actionId] = ground;
        return ground;
    }

    public void Dispose()
    {
        hook?.Dispose();
        hook = null;
    }
}
