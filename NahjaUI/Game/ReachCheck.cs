using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace NahjaUI.Game;

/// <summary>
/// Whether a unit is out of reach for the player, asked to the game itself: the game's own check of an action (range of the action and line of sight,
/// ActionManager.GetActionInRangeOrLoS) with two fixed reference actions, one for allies (Cure, 30 yalms) and one for enemies (Fire, 25 yalms).
/// The result is the same whatever job you play, because it only reads the range of those two actions, not what you know. It is remembered for a short
/// time per unit, so a list of twenty units does not ask the game twenty times every frame.
/// </summary>
internal static unsafe class ReachCheck
{
    // Cure: an ally, 30 yalms. Fire: an enemy, 25 yalms.
    private const uint AllyReferenceAction = 120;
    private const uint EnemyReferenceAction = 141;

    // What the game answers when there is no line of sight and when the target is out of range.
    private const uint NoLineOfSight = 562;
    private const uint OutOfRange = 566;

    // How long an answer is kept (milliseconds).
    private const long KeepMs = 100;

    private static readonly Dictionary<(uint Id, bool Enemy), (bool Out, long At)> Answers = new();

    /// <summary>True when the game says the unit is too far or not in sight for the reference action. False when it cannot be known (not next to you, you are the unit).</summary>
    public static bool IsOutOfReach(uint entityId, bool enemy)
    {
        if (entityId == 0)
            return false;

        var now = Environment.TickCount64;
        if (Answers.TryGetValue((entityId, enemy), out var known) && now - known.At < KeepMs)
            return known.Out;

        var result = Ask(entityId, enemy);

        // Units that left are forgotten now and then (the list never grows without end).
        if (Answers.Count > 256)
            Answers.Clear();

        Answers[(entityId, enemy)] = (result, now);
        return result;
    }

    private static readonly Dictionary<uint, (bool Enemy, long At)> EnemyAnswers = new();

    /// <summary>
    /// Whether the game lets you use the attack reference action on that unit, which is how the game itself tells an enemy from an ally (the "hostile"
    /// flag is not enough: a training dummy does not carry it). Remembered for one second per unit.
    /// </summary>
    public static bool IsEnemy(uint entityId)
    {
        if (entityId == 0)
            return false;

        var now = Environment.TickCount64;
        if (EnemyAnswers.TryGetValue(entityId, out var known) && now - known.At < 1000)
            return known.Enemy;

        var enemy = false;
        try
        {
            var target = Plugin.ObjectTable.SearchByEntityId(entityId);
            if (target is not null)
                enemy = ActionManager.CanUseActionOnTarget(EnemyReferenceAction, (GameObject*)target.Address);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "NahjaUI: the enemy check failed.");
        }

        if (EnemyAnswers.Count > 256)
            EnemyAnswers.Clear();

        EnemyAnswers[entityId] = (enemy, now);
        return enemy;
    }

    private static bool Ask(uint entityId, bool enemy)
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player is null || player.EntityId == entityId)
                return false;

            var target = Plugin.ObjectTable.SearchByEntityId(entityId);
            if (target is null)
                return false;

            // The game's own answers (read in game): 0 can be used, 562 no line of sight, 566 out of range. 565 shows up when you start to move
            // away, while the cast still works, so it does not count; any other number is not about distance or sight either.
            var code = ActionManager.GetActionInRangeOrLoS(enemy ? EnemyReferenceAction : AllyReferenceAction, (GameObject*)player.Address, (GameObject*)target.Address);
            return code is NoLineOfSight or OutOfRange;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "NahjaUI: the range check failed.");
            return false;
        }
    }
}
