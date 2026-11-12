using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Enums;
using GaugeCardType = Dalamud.Game.ClientState.JobGauge.Enums.CardType;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;

namespace NahjaUI.Game;

/// <summary>
/// The ONLY place where NahjaUI reads game data from Dalamud.
/// Once per frame it builds a <see cref="GameSnapshot"/>; modules read <see cref="Current"/> and nothing else.
/// If a game patch breaks something, this is the file to fix.
/// </summary>
internal sealed class GameDataProvider : IDisposable
{
    private long frame;
    private int consecutiveErrors;

    // The game only refreshes the remaining time of many statuses every few seconds (the ones of other players, for example).
    // To show a smooth countdown, the first time a given value is seen is remembered and the time that has passed since then
    // is taken off. When the game sends a new value, it wins. Key = (unit, status, who applied it).
    private Dictionary<(uint Unit, uint Status, uint Source), (float Reported, long Stamp)> statusClock = new();
    private Dictionary<(uint Unit, uint Status, uint Source), (float Reported, long Stamp)> nextStatusClock = new();

    // Your chocobo companion: found by scanning the object table now and then (not every frame), then looked up by id.
    private uint chocoboId;
    private long nextChocoboSearchFrame;

    // Cooldowns are only read for the actions someone asked for.
    private readonly HashSet<uint> watchedActions = new();
    private static readonly IReadOnlyDictionary<uint, CooldownSnapshot> NoCooldowns = new Dictionary<uint, CooldownSnapshot>();

    /// <summary>Set by the nameplate module every frame: the plates are only read while someone wants them.</summary>
    public bool WantNamePlates { get; set; }

    /// <summary>Set by the enmity list every frame it is on: the provider reads the game's enmity list only then.</summary>
    public bool WantEnmityList { get; set; }

    /// <summary>The snapshot of the latest frame. Never null.</summary>
    public GameSnapshot Current { get; private set; } = GameSnapshot.Empty;

    /// <summary>How long building the last snapshot took, in milliseconds.</summary>
    public double LastBuildMs { get; private set; }

    /// <summary>Message of the last error while building the snapshot, or null if all is well.</summary>
    public string? LastError { get; private set; }

    public GameDataProvider()
    {
        Plugin.Framework.Update += OnUpdate;
    }

    /// <summary>
    /// Says which actions to read cooldowns for. Call it every frame with the actions that are needed right now;
    /// the cooldowns then appear in the next snapshot.
    /// </summary>
    public void SetWatchedActions(IEnumerable<uint> actionIds)
    {
        watchedActions.Clear();

        foreach (var id in actionIds)
            watchedActions.Add(id);
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        var start = Stopwatch.GetTimestamp();

        try
        {
            Current = Build();
            consecutiveErrors = 0;
            LastError = null;
        }
        catch (Exception ex)
        {
            // One failing frame must never break the plugin: show an empty snapshot and log the first error only.
            Current = GameSnapshot.Empty with { Frame = frame };
            LastError = ex.Message;

            consecutiveErrors++;
            if (consecutiveErrors == 1)
                Plugin.Log.Error(ex, "NahjaUI: failed to build the game snapshot.");
        }

        LastBuildMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private GameSnapshot Build()
    {
        frame++;

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer is null || !Plugin.PlayerState.IsLoaded)
        {
            statusClock.Clear();
            return GameSnapshot.Empty with { Frame = frame };
        }

        nextStatusClock.Clear();

        var target = Plugin.TargetManager.Target;

        // The game's option to show the party list while alone (the name says "off": 0 means it is shown).
        var soloRaw = -1;
        if (Plugin.GameConfig.UiControl.TryGet("PartyListSoloOff", out uint rawValue))
            soloRaw = (int)rawValue;

        var soloShown = soloRaw == 0;

        var context = new ContextSnapshot(
            Plugin.ClientState.TerritoryType,
            Plugin.Condition[ConditionFlag.InCombat],
            Plugin.Condition[ConditionFlag.BoundByDuty],
            Plugin.PartyList.Length,
            soloShown,
            soloRaw,
            Plugin.ClientState.IsPvP,
            Plugin.ClientState.IsPvPExcludingDen,
            localPlayer.StatusFlags.HasFlag(StatusFlags.WeaponOut));

        var player = ToUnit(localPlayer);

        var snapshot = new GameSnapshot(
            true,
            player,
            ToUnit(target),
            ToUnit(Plugin.TargetManager.FocusTarget),
            ToUnit(TargetOfTarget(target, localPlayer)),
            BuildParty(),
            context,
            BuildCooldowns(),
            frame,
            BuildWhiteMageGauge(player),
            BuildWarriorGauge(player),
            BuildChocobo(localPlayer, soloShown),
            BuildNamePlates(),
            BuildPaladinGauge(player),
            BuildCombo(),
            BuildEnmityList(localPlayer),
            BuildRedMageGauge(player),
            BuildLimitBreak(context),
            BuildFrontlineGauge(context),
            BuildDarkKnightGauge(player),
            BuildGunbreakerGauge(player),
            BuildScholarGauge(player),
            BuildAstrologianGauge(player),
            BuildSageGauge(player),
            BuildExtraGauge(player),
            BuildGcd(),
            BuildAlliance());

        // The statuses seen this frame are the ones to remember for the next one.
        (statusClock, nextStatusClock) = (nextStatusClock, statusClock);
        return snapshot;
    }

    // The colour of your chocobo as the game has it now (its dye, "Current Color" in the Companion window), or null when unknown.
    // It is read from the game's own data: the dye id of the companion and the colour of that dye in the Stain sheet.
    public Vector4? ChocoboColor { get; private set; }

    private byte lastChocoboStain = byte.MaxValue;

    private unsafe void ReadChocoboColor()
    {
        byte stain;
        try
        {
            var state = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
            if (state == null)
                return;

            stain = state->Buddy.CompanionInfo.CurrentColorStainId;
        }
        catch
        {
            return;
        }

        if (stain == lastChocoboStain)
            return;

        lastChocoboStain = stain;
        ChocoboColor = null;

        if (stain != 0 && Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Stain>().TryGetRow(stain, out var row) && row.Color != 0)
        {
            var rgb = row.Color;
            ChocoboColor = new Vector4(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }
    }

    // Your chocobo companion (the one the game shows at the end of its party list), or null when it is not out.
    private UnitSnapshot? BuildChocobo(IGameObject localPlayer, bool soloShown)
    {
        // Only while in a party, like the game's list (or alone, when the game is set to show the list alone).
        if (Plugin.PartyList.Length == 0 && !soloShown)
            return null;

        ReadChocoboColor();

        IBattleNpc? chocobo = null;

        if (chocoboId != 0 && Plugin.ObjectTable.SearchByEntityId(chocoboId) is IBattleNpc known &&
            known.BattleNpcKind == BattleNpcSubKind.Buddy && known.OwnerId == localPlayer.EntityId)
        {
            chocobo = known;
        }
        else if (frame >= nextChocoboSearchFrame)
        {
            // Not found: look again in about half a second.
            nextChocoboSearchFrame = frame + 30;
            chocoboId = 0;

            foreach (var gameObject in Plugin.ObjectTable)
            {
                if (gameObject is IBattleNpc npc && npc.BattleNpcKind == BattleNpcSubKind.Buddy && npc.OwnerId == localPlayer.EntityId)
                {
                    chocobo = npc;
                    chocoboId = npc.EntityId;
                    break;
                }
            }
        }

        var unit = chocobo is null ? null : ToUnit(chocobo);
        return unit is null ? null : unit with { IsCompanion = true };
    }

    // The units that have a plate in the game, with the screen position of the point above their head.
    // The game keeps its plates in two lists with the same order: the numbers (kind, entity id) and the 3D objects (world position).
    private unsafe IReadOnlyList<NamePlateSnapshot> BuildNamePlates()
    {
        var result = new List<NamePlateSnapshot>();
        if (!WantNamePlates)
            return result;

        var uiModule = UIModule.Instance();
        if (uiModule == null)
            return result;

        var ui3d = uiModule->GetUI3DModule();
        var numbers = NamePlateNumberArray.Instance();
        if (ui3d == null || numbers == null)
            return result;

        var count = Math.Min(Math.Min(numbers->ActiveNamePlateCount, ui3d->NamePlateObjectInfoCount), 50);

        // In this version of the game structures, the per plate data is a Span: it is read by index.
        var plateData = numbers->ObjectData;

        var currentTarget = Plugin.TargetManager.Target;

        for (var i = 0; i < count; i++)
        {
            var kind = plateData[i].NamePlateKind;
            var isEnemy = kind == UIObjectKind.BattleNpcEnemy;
            var isPlayer = kind == UIObjectKind.PlayerCharacter;

            // Characters of the fight keep the old check: 0xE0000000 is how the game marks an empty plate. Objects can have no
            // entity id at all, so they are found through the object the plate points to.
            var entityId = plateData[i].EntityId;
            if ((isEnemy || isPlayer) && (entityId == 0 || entityId == 0xE0000000))
                continue;

            var info = ui3d->NamePlateObjectInfoPointers[i].Value;
            if (info == null || info->GameObject == null)
                continue;

            var gameObject = Plugin.ObjectTable.CreateObjectReference((nint)info->GameObject);
            if (gameObject is null)
                continue;

            var name = StreamerMode.Name(CleanName(gameObject.Name.ToString()), isPlayer);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // The game gives the plate position as screen coordinates from -1 to 1 (checked with the log: x 0 is the middle of
            // the screen and y 0.43 is a bit above the middle). The z value is the depth: not positive means behind the camera.
            var point = info->NamePlatePos;
            if (point.Z <= 0f || MathF.Abs(point.X) > 1.2f || MathF.Abs(point.Y) > 1.2f)
                continue;

            var scale = Math.Clamp(plateData[i].Scale / 100f, 0f, 1f);
            var isTarget = currentTarget is not null && currentTarget.GameObjectId == gameObject.GameObjectId;
            var ndc = new Vector2(point.X, point.Y);

            if (gameObject is IBattleChara chara)
            {
                if (isEnemy && chara.IsDead)
                    continue;

                // Players: you, the people of your party, or anyone else (a hostile player, as in PvP, counts as an enemy).
                // Other fighting characters (pets, chocobo, friendly battle NPCs) are Friendly.
                var relation = isEnemy ? NamePlateRelation.Enemy : isPlayer ? PlayerRelation(chara) : NamePlateRelation.Friendly;
                result.Add(new NamePlateSnapshot(chara.EntityId, name, chara.Level, chara.CurrentHp, chara.MaxHp, ndc, scale, ToCast(chara), relation, chara.GameObjectId, isTarget, ToStatuses(chara.EntityId, chara.StatusList)));
            }
            else
            {
                // No HP: a plate of text only.
                result.Add(new NamePlateSnapshot(gameObject.EntityId, name, 0, 0, 0, ndc, scale, null, NamePlateRelation.Other, gameObject.GameObjectId, isTarget));
            }
        }

        return result;
    }

    /// <summary>Who a player plate belongs to (also used by the plate module to know which game plates to leave alone).</summary>
    internal static NamePlateRelation PlayerRelation(IGameObject gameObject)
    {
        if (gameObject is IBattleChara chara && chara.StatusFlags.HasFlag(StatusFlags.Hostile))
            return NamePlateRelation.EnemyPlayer;

        if (Plugin.ObjectTable.LocalPlayer is { } me && me.EntityId == gameObject.EntityId)
            return NamePlateRelation.Self;

        return IsInParty(gameObject.EntityId) ? NamePlateRelation.Party : NamePlateRelation.OtherPlayer;
    }

    private static bool IsInParty(uint entityId)
    {
        foreach (var member in Plugin.PartyList)
        {
            if (member.EntityId == entityId)
                return true;
        }

        return false;
    }

    // The enemies of the game's own enmity list (the ones that are fighting you, up to 8), with who each one is attacking.
    private unsafe IReadOnlyList<EnemyEntrySnapshot> BuildEnmityList(IGameObject localPlayer)
    {
        var result = new List<EnemyEntrySnapshot>();
        if (!WantEnmityList)
            return result;

        var numbers = EnemyListNumberArray.Instance();
        if (numbers == null)
            return result;

        var count = Math.Clamp(numbers->EnemyCount, 0, 8);
        var enemies = numbers->Enemies;

        for (var i = 0; i < count; i++)
        {
            var entityId = (uint)enemies[i].EntityId;
            if (entityId == 0 || entityId == 0xE0000000)
                continue;

            if (Plugin.ObjectTable.SearchByEntityId(entityId) is not IBattleChara chara || chara.IsDead)
                continue;

            var unit = ToUnit(chara);
            if (unit is null)
                continue;

            result.Add(new EnemyEntrySnapshot(
                unit with { IsHostile = true },
                chara.TargetObjectId == localPlayer.GameObjectId,
                enemies[i].ActiveInList ? 1 : 0,
                enemies[i].LockedInList ? 1 : 0,
                chara.IsTargetable ? 1 : 0));
        }

        return result;
    }

    // The game puts its own symbols into some names (in PvP, the marks beside enemy players): they are characters of the game's
    // private area, which our fonts do not have and draw as "?". They are taken out.
    private static string CleanName(string name)
    {
        var hasPrivate = false;
        foreach (var c in name)
        {
            if (c >= '\uE000' && c <= '\uF8FF')
            {
                hasPrivate = true;
                break;
            }
        }

        if (!hasPrivate)
            return name;

        return string.Concat(name.Where(c => c < '\uE000' || c > '\uF8FF')).Trim();
    }

    // The White Mage gauge (class job 24). Nobody else has one, and the game only fills it for White Mages.
    private const uint WhiteMageJobId = 24;

    private static WhiteMageGaugeSnapshot? BuildWhiteMageGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != WhiteMageJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<WHMGauge>();
        return new WhiteMageGaugeSnapshot(gauge.Lily, gauge.BloodLily, Math.Max((short)0, gauge.LilyTimer));
    }

    // The Warrior gauge. Warrior is class job 21 (Marauder, its first class, is 3 and has no Beast Gauge).
    private const uint WarriorJobId = 21;

    private static WarriorGaugeSnapshot? BuildWarriorGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != WarriorJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<WARGauge>();
        return new WarriorGaugeSnapshot(gauge.BeastGauge);
    }

    // The combo of the game: the last action of the chain you used and the time left to continue it.
    private static unsafe ComboSnapshot BuildCombo()
    {
        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return ComboSnapshot.None;

        var combo = actionManager->Combo;
        return combo.Timer > 0f ? new ComboSnapshot(combo.Action, combo.Timer) : ComboSnapshot.None;
    }

    // The gauge of the game's Frontline window (PvP). Read from the values the game gives that window (value 3 is the number on
    // the gauge, value 4 its maximum). The window's visibility is NOT used: when ours
    // is drawn the game's window is switched off, and the values are still there.
    private static unsafe FrontlineGaugeSnapshot? BuildFrontlineGauge(ContextSnapshot context)
    {
        if (!context.InPvPMatch)
            return null;

        var pointer = Plugin.GameGui.GetAddonByName("PvPFrontlineGauge");
        if (pointer.IsNull)
            return null;

        var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)pointer.Address;
        if (addon->AtkValuesCount < 5)
            return null;

        var value = addon->AtkValues[3].UInt;
        var max = addon->AtkValues[4].UInt;
        return new FrontlineGaugeSnapshot(value, max == 0 ? 100u : max);
    }

    // How many bars the game's own Limit Break window shows (0 when it cannot be read). The window may be hidden: its nodes are still kept up to date.
    private static unsafe int NativeLimitBreakBars()
    {
        var pointer = Plugin.GameGui.GetAddonByName("_LimitBreak");
        if (pointer.IsNull)
            return 0;

        var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)pointer.Address;
        var bars = 0;
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node != null && (int)node->Type >= 1000 && (node->NodeFlags & FFXIVClientStructs.FFXIV.Component.GUI.NodeFlags.Visible) != 0)
                bars++;
        }

        return bars;
    }

    // The Limit Break gauge (units gathered, units per bar), in PvE (the party's) and in PvP (the team's, read the same way); null when the game has none.
    private static unsafe LimitBreakSnapshot? BuildLimitBreak(ContextSnapshot context)
    {
        var controller = FFXIVClientStructs.FFXIV.Client.Game.UI.LimitBreakController.Instance();
        // BarCount is how many bars the game gives the party right now (0 when there is no limit break to gather: alone, for example).
        if (controller == null || controller->BarUnits == 0 || controller->BarCount == 0)
            return null;

        // The bars the content has (2 in Occult Crescent, 3 in an alliance raid): the game keeps one component node per bar visible in its own
        // window (_LimitBreak). The controller's BarCount is only how many bars the units reach, so it is the fallback.
        var bars = NativeLimitBreakBars();
        if (bars == 0)
            bars = (int)controller->BarCount;

        return new LimitBreakSnapshot(controller->CurrentUnits, controller->BarUnits, bars);
    }

    // The Dark Knight gauge. Dark Knight is class job 32 (it has no earlier class).
    private const uint DarkKnightJobId = 32;

    private static DarkKnightGaugeSnapshot? BuildDarkKnightGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != DarkKnightJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<DRKGauge>();
        return new DarkKnightGaugeSnapshot(gauge.Blood, gauge.DarksideTimeRemaining, gauge.HasDarkArts, gauge.ShadowTimeRemaining);
    }

    // The Gunbreaker gauge. Gunbreaker is class job 37.
    private const uint GunbreakerJobId = 37;

    private static GunbreakerGaugeSnapshot? BuildGunbreakerGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != GunbreakerJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<GNBGauge>();
        return new GunbreakerGaugeSnapshot(gauge.Ammo);
    }

    // The Scholar gauge. Scholar is class job 28.
    private const uint ScholarJobId = 28;

    private static ScholarGaugeSnapshot? BuildScholarGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != ScholarJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<SCHGauge>();
        return new ScholarGaugeSnapshot(gauge.Aetherflow, gauge.FairyGauge, (ushort)Math.Max((short)0, gauge.SeraphTimer));
    }

    // The Astrologian gauge. Astrologian is class job 33.
    private const uint AstrologianJobId = 33;

    private static AstrologianGaugeSnapshot? BuildAstrologianGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != AstrologianJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<ASTGauge>();
        var crown = gauge.DrawnCrownCard;
        var crownValue = crown == GaugeCardType.Lord ? (byte)1 : crown == GaugeCardType.Lady ? (byte)2 : (byte)0;
        return new AstrologianGaugeSnapshot(
            gauge.Card1 != GaugeCardType.None,
            gauge.Card2 != GaugeCardType.None,
            gauge.Card3 != GaugeCardType.None,
            gauge.ActiveDraw == DrawType.Umbral,
            crownValue);
    }

    // The Sage gauge. Sage is class job 40.
    private const uint SageJobId = 40;

    private static SageGaugeSnapshot? BuildSageGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != SageJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<SGEGauge>();
        return new SageGaugeSnapshot(gauge.Addersgall, gauge.AddersgallTimer, gauge.Addersting);
    }

    // ---------------------------------------------------------------- the generic gauges (melee, physical ranged and casters)

    // The longest value seen of a timer while it ran: the game gives the time left, not the total, and the bar needs the total.
    private readonly Dictionary<string, float> timerMax = new();

    private float RememberMax(string key, float value, float floor)
    {
        if (value <= 0f)
        {
            timerMax.Remove(key);
            return floor;
        }

        var max = MathF.Max(floor, MathF.Max(value, timerMax.GetValueOrDefault(key)));
        timerMax[key] = max;
        return max;
    }

    private static string Secs(float milliseconds) => ((int)MathF.Ceiling(milliseconds / 1000f)).ToString();

    private static GaugePartSnapshot Bar(string id, float value, float max, string? text = null, int variant = 0)
        => new(id, GaugePartKind.Bar, value, max, 0f, text, variant);

    private static GaugePartSnapshot Pips(string id, float count, int max, float partial = 0f, string? text = null, int variant = 0)
        => new(id, GaugePartKind.Pips, count, max, partial, text, variant);

    private static GaugePartSnapshot Slots(string id, params int[] colors)
        => new(id, GaugePartKind.Slots, 0f, colors.Length, 0f, null, 0, colors);

    private ExtraGaugeSnapshot? BuildExtraGauge(UnitSnapshot? player)
    {
        if (player is null)
            return null;

        GaugePartSnapshot[]? parts = player.JobId switch
        {
            20 => BuildMonk(),
            22 => BuildDragoon(),
            30 => BuildNinja(),
            34 => BuildSamurai(),
            39 => BuildReaper(),
            41 => BuildViper(),
            23 => BuildBard(),
            31 => BuildMachinist(),
            38 => BuildDancer(),
            25 => BuildBlackMage(player),
            27 => BuildSummoner(),
            42 => BuildPictomancer(),
            _ => null
        };

        return parts is null ? null : new ExtraGaugeSnapshot(player.JobId, parts);
    }

    private static GaugePartSnapshot[] BuildMonk()
    {
        var gauge = Plugin.JobGauges.Get<MNKGauge>();

        // The index of the colour of the row for each chakra: 0 Opo-opo, 1 Raptor, 2 Coeurl. The names of the enum are used because
        // the numbers have changed between versions of Dalamud.
        static int Chakra(string name) => name.Contains("opo", StringComparison.OrdinalIgnoreCase) ? 0
            : name.Contains("raptor", StringComparison.OrdinalIgnoreCase) ? 1
            : name.Contains("coeurl", StringComparison.OrdinalIgnoreCase) ? 2
            : -1;

        // The two Nadi: a flags value written with names ("Lunar, Solar").
        var nadi = gauge.Nadi.ToString();
        var lunar = nadi.Contains("lunar", StringComparison.OrdinalIgnoreCase);
        var solar = nadi.Contains("solar", StringComparison.OrdinalIgnoreCase);
        if (!lunar && !solar && int.TryParse(nadi, out var raw) && raw != 0)
        {
            // A number without a name: both bits of the flags (lunar and solar), whichever pair of numbers this version uses.
            lunar = raw is 1 or 3 or 6 or 2;
            solar = raw is 2 or 3 or 6 or 4;
        }

        return new[]
        {
            Pips("chakra", gauge.Chakra, 5),
            Slots("blitz", Chakra(gauge.BeastChakra1.ToString()), Chakra(gauge.BeastChakra2.ToString()), Chakra(gauge.BeastChakra3.ToString())),
            Slots("nadi", lunar ? 0 : -1, solar ? 1 : -1)
        };
    }

    private static GaugePartSnapshot[] BuildDragoon()
    {
        var gauge = Plugin.JobGauges.Get<DRGGauge>();
        float life = gauge.IsLOTDActive ? Math.Max((short)0, gauge.LOTDTimer) : 0f;

        return new[]
        {
            Bar("lotd", life, 20000f, life > 0f ? Secs(life) : null),
            Pips("focus", gauge.FirstmindsFocusCount, 2)
        };
    }

    private static GaugePartSnapshot[] BuildNinja()
    {
        var gauge = Plugin.JobGauges.Get<NINGauge>();
        return new[]
        {
            Bar("ninki", gauge.Ninki, 100f, gauge.Ninki.ToString()),
            Pips("kazematoi", gauge.Kazematoi, 5)
        };
    }

    private static GaugePartSnapshot[] BuildSamurai()
    {
        var gauge = Plugin.JobGauges.Get<SAMGauge>();
        return new[]
        {
            Bar("kenki", gauge.Kenki, 100f, gauge.Kenki.ToString()),
            Slots("sen", gauge.HasSetsu ? 0 : -1, gauge.HasGetsu ? 1 : -1, gauge.HasKa ? 2 : -1),
            Pips("meditation", gauge.MeditationStacks, 3)
        };
    }

    private static GaugePartSnapshot[] BuildReaper()
    {
        var gauge = Plugin.JobGauges.Get<RPRGauge>();

        // While enshrouded the Shroud row shows the Lemure Shroud and the time left; otherwise the Shroud gauge itself.
        var enshrouded = gauge.EnshroudedTimeRemaining > 0;
        return new[]
        {
            Bar("soul", gauge.Soul, 100f, gauge.Soul.ToString()),
            enshrouded
                ? Pips("shroud", gauge.LemureShroud, 5, 0f, Secs(gauge.EnshroudedTimeRemaining), 1)
                : Bar("shroud", gauge.Shroud, 100f, gauge.Shroud.ToString())
        };
    }

    private static GaugePartSnapshot[] BuildViper()
    {
        var gauge = Plugin.JobGauges.Get<VPRGauge>();

        // While Reawakened the second row shows the Anguine Tribute (the generations left); otherwise the Serpent Offering.
        var reawakened = gauge.AnguineTribute > 0;
        return new[]
        {
            Pips("coil", gauge.RattlingCoilStacks, 3),
            reawakened
                ? Pips("offering", gauge.AnguineTribute, 5, 0f, null, 1)
                : Bar("offering", gauge.SerpentOffering, 100f, gauge.SerpentOffering.ToString())
        };
    }

    private static GaugePartSnapshot[] BuildBard()
    {
        var gauge = Plugin.JobGauges.Get<BRDGauge>();

        // The names of the enum are the ones of the songs, whatever their numbers are.
        var name = gauge.Song.ToString();
        var variant = name.Contains("Minuet", StringComparison.Ordinal) ? 0
            : name.Contains("Ballad", StringComparison.Ordinal) ? 1
            : name.Contains("Paeon", StringComparison.Ordinal) ? 2
            : 3;

        float time = variant == 3 ? 0f : gauge.SongTimer;
        var stacks = variant switch { 0 => 3, 1 => 1, 2 => 4, _ => 3 };

        return new[]
        {
            Bar("song", time, 45000f, time > 0f ? Secs(time) : null, variant),
            Pips("repertoire", gauge.Repertoire, stacks, 0f, null, Math.Min(variant, 2)),
            Bar("soulvoice", gauge.SoulVoice, 100f, gauge.SoulVoice.ToString())
        };
    }

    private GaugePartSnapshot[] BuildMachinist()
    {
        var gauge = Plugin.JobGauges.Get<MCHGauge>();

        float overheat = gauge.IsOverheated ? Math.Max((short)0, gauge.OverheatTimeRemaining) : 0f;
        float queen = gauge.IsRobotActive ? Math.Max((short)0, gauge.SummonTimeRemaining) : 0f;
        var overheatMax = RememberMax("mch.heat", overheat, 8000f);
        var queenMax = RememberMax("mch.queen", queen, 10000f);

        return new[]
        {
            overheat > 0f ? Bar("heat", overheat, overheatMax, Secs(overheat), 1) : Bar("heat", gauge.Heat, 100f, gauge.Heat.ToString()),
            queen > 0f ? Bar("battery", queen, queenMax, Secs(queen), 1) : Bar("battery", gauge.Battery, 100f, gauge.Battery.ToString())
        };
    }

    private static GaugePartSnapshot[] BuildDancer()
    {
        var gauge = Plugin.JobGauges.Get<DNCGauge>();

        GaugePartSnapshot first;
        if (gauge.IsDancing)
        {
            // The steps still to do, each in its own colour (the colours 1 to 4 of the row: Emboite, Entrechat, Jete, Pirouette).
            var steps = gauge.Steps;
            var slots = new int[4];
            for (var i = 0; i < 4; i++)
            {
                var step = (long)steps[i] - 15999L + 1L;
                slots[i] = i >= gauge.CompletedSteps && step is >= 1 and <= 4 ? (int)step : -1;
            }

            first = Slots("feathers", slots);
        }
        else
        {
            first = Pips("feathers", gauge.Feathers, 4);
        }

        return new[] { first, Bar("esprit", gauge.Esprit, 100f, gauge.Esprit.ToString()) };
    }

    private static GaugePartSnapshot[] BuildBlackMage(UnitSnapshot player)
    {
        var gauge = Plugin.JobGauges.Get<BLMGauge>();

        // Astral Fire (status 165) or Umbral Ice (status 166): the stacks are in the gauge, the seconds in the status.
        var fire = gauge.InAstralFire;
        var ice = gauge.InUmbralIce;
        var stacks = fire ? gauge.AstralFireStacks : ice ? gauge.UmbralIceStacks : 0;
        var status = player.Statuses.FirstOrDefault(s => s.Id == (fire ? 165u : 166u));
        var text = (fire || ice) && status is { RemainingTime: > 0f } ? Secs(status.RemainingTime * 1000f) : null;

        // The game counts the Polyglot timer down from 30 seconds while Enochian is on.
        var polyglot = gauge.PolyglotStacks;
        var partial = gauge.IsEnochianActive && polyglot < 3 ? Math.Clamp((30000f - gauge.EnochianTimer) / 30000f, 0f, 1f) : 0f;

        return new[]
        {
            Pips("element", stacks, 3, 0f, text, fire ? 0 : ice ? 1 : 2),
            Pips("hearts", gauge.UmbralHearts, 3),
            Pips("polyglot", polyglot, 3, partial),
            Pips("soul", gauge.AstralSoulStacks, 6)
        };
    }

    private GaugePartSnapshot[] BuildSummoner()
    {
        var gauge = Plugin.JobGauges.Get<SMNGauge>();

        // The two Aetherflow stacks are two bits of the flags.
        var flags = (int)gauge.AetherFlags;
        var aetherflow = (flags & 1) + ((flags >> 1) & 1);

        // The Trance row: the time of the primal that is attuned (Ifrit, Titan, Garuda: 30 seconds) or of the demi-summon that is out.
        var attunement = (int)gauge.AttunementType;
        float demi = gauge.SummonTimerRemaining;
        var demiMax = RememberMax("smn.demi", demi, 15000f);

        GaugePartSnapshot trance;
        if (gauge.AttunementTimerRemaining > 0 && attunement is >= 1 and <= 3)
            trance = Bar("trance", gauge.AttunementTimerRemaining, 30000f, Secs(gauge.AttunementTimerRemaining), attunement - 1);
        else if (demi > 0f)
            trance = Bar("trance", demi, demiMax, Secs(demi), 3);
        else
            trance = Bar("trance", 0f, 1f, null, 3);

        return new[]
        {
            Pips("aetherflow", aetherflow, 2),
            Slots("primals", gauge.IsIfritReady ? 0 : -1, gauge.IsTitanReady ? 1 : -1, gauge.IsGarudaReady ? 2 : -1),
            trance
        };
    }

    private static GaugePartSnapshot[] BuildPictomancer()
    {
        var gauge = Plugin.JobGauges.Get<PCTGauge>();
        return new[]
        {
            Bar("palette", gauge.PalleteGauge, 100f, gauge.PalleteGauge.ToString()),
            Pips("paint", gauge.Paint, 5),
            Slots("canvas", gauge.CreatureMotifDrawn ? 0 : -1, gauge.WeaponMotifDrawn ? 1 : -1, gauge.LandscapeMotifDrawn ? 2 : -1, gauge.MooglePortraitReady ? 3 : -1, gauge.MadeenPortraitReady ? 4 : -1)
        };
    }

    // The Red Mage gauge. Red Mage is class job 35.
    private const uint RedMageJobId = 35;

    private static RedMageGaugeSnapshot? BuildRedMageGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != RedMageJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<RDMGauge>();
        return new RedMageGaugeSnapshot(gauge.WhiteMana, gauge.BlackMana, gauge.ManaStacks);
    }

    // The Paladin gauge. Paladin is class job 19 (Gladiator, its first class, is 1 and has no Oath Gauge).
    private const uint PaladinJobId = 19;

    private static PaladinGaugeSnapshot? BuildPaladinGauge(UnitSnapshot? player)
    {
        if (player is null || player.JobId != PaladinJobId)
            return null;

        var gauge = Plugin.JobGauges.Get<PLDGauge>();
        return new PaladinGaugeSnapshot(gauge.OathGauge);
    }

    // The target of the target. For your own character the game keeps the target somewhere else than for the rest, so the
    // usual reading comes back empty when you target yourself. In that case your target's target is you.
    private static IGameObject? TargetOfTarget(IGameObject? target, IGameObject localPlayer)
    {
        if (target is null)
            return null;

        return target.EntityId == localPlayer.EntityId ? target : target.TargetObject;
    }

    // ---------------------------------------------------------------- conversion helpers

    // Only characters that fight (players, enemies, battle NPCs) have HP and statuses.
    private UnitSnapshot? ToUnit(IGameObject? gameObject)
    {
        if (gameObject is null)
            return null;

        // The kind of object the game reports (numbers of the game's ObjectKind: 3 event NPC, 9 minion, 10 retainer).
        var objectKind = (byte)gameObject.ObjectKind;

        if (gameObject is not IBattleChara chara)
        {
            // Not a fighting character: no HP, no statuses. It still gets a frame (name and colour only).
            var kind = objectKind switch
            {
                9 => UnitKind.Pet,
                3 or 10 => UnitKind.Npc,
                _ => UnitKind.Object
            };

            return new UnitSnapshot(gameObject.EntityId, CleanName(gameObject.Name.ToString()), 0, 0, 0, 0, 0, 0, false, null,
                Array.Empty<StatusSnapshot>(), Kind: kind);
        }

        // Pets (summons, carbuncle...) and companions (SubKind 2 and 3 of a battle NPC) get their own colour.
        var unitKind = objectKind == 2 && gameObject.SubKind is 2 or 3 ? UnitKind.Pet : UnitKind.Character;

        return new UnitSnapshot(
            chara.EntityId,
            StreamerMode.Name(CleanName(chara.Name.ToString()), chara.ObjectKind == ObjectKind.Pc),
            chara.ClassJob.RowId,
            chara.Level,
            chara.CurrentHp,
            chara.MaxHp,
            ResourceNow(chara),
            ResourceMax(chara),
            chara.IsDead,
            ToCast(chara),
            ToStatuses(chara.EntityId, chara.StatusList),
            chara.ObjectKind == ObjectKind.Pc,
            chara.ShieldPercentage,
            chara.StatusFlags.HasFlag(StatusFlags.Hostile),
            Kind: unitKind);
    }

    // The resource of the MP bar: MP, except for crafters (CP, jobs 8 to 15) and gatherers (GP, jobs 16 to 18).
    private static uint ResourceNow(IBattleChara chara) => chara.ClassJob.RowId switch
    {
        >= 8 and <= 15 => chara.CurrentCp,
        >= 16 and <= 18 => chara.CurrentGp,
        _ => chara.CurrentMp
    };

    private static uint ResourceMax(IBattleChara chara) => chara.ClassJob.RowId switch
    {
        >= 8 and <= 15 => chara.MaxCp,
        >= 16 and <= 18 => chara.MaxGp,
        _ => chara.MaxMp
    };

    // The online status the game gives to someone who is watching a cutscene (the icon next to his name in the party list). Its number is read once
    // from the English table, so it does not depend on the language of the game; after that only numbers are compared.
    public static readonly uint CutsceneStatusId = FindOnlineStatus("Viewing Cutscene");

    private static uint FindOnlineStatus(string name)
    {
        foreach (var row in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.OnlineStatus>(Dalamud.Game.ClientLanguage.English))
        {
            if (row.Name.ToString() == name)
                return row.RowId;
        }

        return 0;
    }

    private UnitSnapshot ToUnit(IPartyMember member)
    {
        // The party list has the data of every member, even those far away.
        // Cast and death state only exist when the member is also in the object table.
        var chara = member.GameObject as IBattleChara;

        // Without a character next to you, the member is in another area (its territory is not yours), offline (it has no territory)
        // or only too far away.
        var territory = member.Territory.RowId;
        var presence = chara is not null ? (CutsceneStatusId != 0 && chara.OnlineStatus.RowId == CutsceneStatusId ? Presence.ViewingCutscene : Presence.Here)
            : territory == 0 ? Presence.Offline
            : territory != Plugin.ClientState.TerritoryType ? Presence.OtherArea
            : Presence.OutOfRange;

        return new UnitSnapshot(
            member.EntityId,
            StreamerMode.Name(CleanName(member.Name.ToString()), true),
            member.ClassJob.RowId,
            member.Level,
            member.CurrentHP,
            member.MaxHP,
            member.CurrentMP,
            member.MaxMP,
            chara?.IsDead ?? false,
            chara is null ? null : ToCast(chara),
            // The statuses of the party list are only refreshed when the server sends them (every few seconds), so their times jump and lag
            // behind. A member next to you is read from its own character instead, which the game counts down every frame (the same
            // list the status rows use). The party list is the fallback for members that are far away.
            chara is not null ? ToStatuses(member.EntityId, chara.StatusList) : ToStatuses(member.EntityId, member.Statuses),
            ShieldPercent: chara?.ShieldPercentage ?? 0,
            Presence: presence);
    }

    private static CastSnapshot? ToCast(IBattleChara chara)
    {
        if (!chara.IsCasting)
            return null;

        return new CastSnapshot(chara.CastActionId, chara.CurrentCastTime, chara.TotalCastTime, chara.IsCastInterruptible, chara.CastActionType);
    }

    private unsafe IReadOnlyList<StatusSnapshot> ToStatuses(uint unitId, StatusList statusList)
    {
        if (statusList.Length == 0)
            return Array.Empty<StatusSnapshot>();

        var result = new List<StatusSnapshot>(statusList.Length);

        foreach (var status in statusList)
        {
            // Empty slots have id 0.
            if (status.StatusId == 0)
                continue;

            // A status with no real duration (the 24 hour ones, for example) has no countdown in the game. Dalamud gives the time as a
            // positive number even then (it drops the sign), which made a false countdown appear after every loading screen.
            // The game marks it with a negative time (or its data says it is permanent): no time.
            var remaining = status.RemainingTime;
            if (remaining > 0f)
            {
                var raw = ((FFXIVClientStructs.FFXIV.Client.Game.Status*)status.Address)->RemainingTime;
                if (raw < 0f || GameNames.StatusIsPermanent(status.StatusId))
                    remaining = 0f;
            }

            result.Add(new StatusSnapshot(status.StatusId, status.SourceId, SmoothRemaining(unitId, status.StatusId, status.SourceId, remaining), status.Param));
        }

        return result;
    }

    // ---------------------------------------------------------------- global cooldown

    // The recast group of the global cooldown (the game numbers the groups from 0; the GCD is the 58th).
    private const int GcdGroup = 57;

    private unsafe GcdSnapshot? BuildGcd()
    {
        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return null;

        var detail = actionManager->GetRecastGroupDetail(GcdGroup);
        if (detail == null)
            return null;

        var total = actionManager->GetRecastTimeForGroup(GcdGroup);
        return new GcdSnapshot(detail->IsActive && total > 0f && detail->Elapsed < total, detail->Elapsed, total);
    }

    // ---------------------------------------------------------------- cooldowns

    // Reads the recast information of the game for each watched action.
    // For actions with several charges the game keeps one timer for ALL charges: it counts up from empty
    // to full, and every (total / charges) seconds one more charge is available.
    private unsafe IReadOnlyDictionary<uint, CooldownSnapshot> BuildCooldowns()
    {
        if (watchedActions.Count == 0)
            return NoCooldowns;

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return NoCooldowns;

        var result = new Dictionary<uint, CooldownSnapshot>(watchedActions.Count);

        foreach (var requested in watchedActions)
        {
            // Some actions turn into another one at higher levels; the game works with the current form.
            var id = actionManager->GetAdjustedActionId(requested);

            var maxCharges = Math.Max(1, (int)ActionManager.GetMaxCharges(id, 0));
            var charges = Math.Min((int)actionManager->GetCurrentCharges(id), maxCharges);

            // The game keeps one "gauge" per recast group. It fills from empty to full (all the charges) as time passes.
            var group = actionManager->GetRecastGroup((int)ActionType.Action, id);
            var detail = group >= 0 ? actionManager->GetRecastGroupDetail(group) : null;

            float totalForAllCharges;
            float elapsed;
            bool groupActive;

            if (detail != null)
            {
                totalForAllCharges = actionManager->GetRecastTimeForGroup(group);
                elapsed = detail->Elapsed;
                groupActive = detail->IsActive;
            }
            else
            {
                totalForAllCharges = actionManager->GetRecastTime(ActionType.Action, id);
                elapsed = actionManager->GetRecastTimeElapsed(ActionType.Action, id);
                groupActive = actionManager->IsRecastTimerActive(ActionType.Action, id);
            }

            var perCharge = totalForAllCharges / maxCharges;

            // Seconds until the next charge. It does not depend on the "active" flag: an ability with one charge
            // spent and one left is still recharging, because its gauge is not full yet.
            var remaining = 0f;
            if (charges < maxCharges && perCharge > 0f && elapsed < totalForAllCharges)
            {
                remaining = maxCharges > 1
                    ? perCharge - (elapsed % perCharge)
                    : MathF.Max(0f, totalForAllCharges - elapsed);
            }

            result[requested] = new CooldownSnapshot(
                requested, charges, maxCharges, remaining, perCharge, elapsed, totalForAllCharges, groupActive, group, id,
                actionManager->IsActionHighlighted(ActionType.Action, id));
        }

        return result;
    }

    // The remaining time of a status, counted down between the moments the game refreshes it (see statusClock).
    private float SmoothRemaining(uint unitId, uint statusId, uint sourceId, float reported)
    {
        // No time limit (or already over): nothing to count.
        if (reported <= 0f)
            return reported;

        var key = (unitId, statusId, sourceId);
        var now = Stopwatch.GetTimestamp();

        if (statusClock.TryGetValue(key, out var previous) && previous.Reported == reported)
        {
            // The game still says the same number: it has not refreshed yet, so count the time ourselves.
            nextStatusClock[key] = previous;
            var elapsed = (float)Stopwatch.GetElapsedTime(previous.Stamp, now).TotalSeconds;
            return MathF.Max(0f, reported - elapsed);
        }

        // New status, or the game sent a new value: start counting from it.
        nextStatusClock[key] = (reported, now);
        return reported;
    }

    // The members of the other parties of the alliance, one slot each, in the game's order; null when you are not in an alliance.
    // An empty slot is null. A normal alliance has 16 slots (two other parties of eight); the game keeps room for 20, which only
    // small group alliances (parties of four) fill.
    private unsafe IReadOnlyList<UnitSnapshot?>? BuildAlliance()
    {
        var partyList = Plugin.PartyList;
        if (!partyList.IsAlliance)
            return null;

        var own = new HashSet<uint>();
        foreach (var member in partyList)
            own.Add(member.EntityId);

        // A normal alliance is two other parties of eight (16 slots). Only the small group alliances (parties of four) use all 20: the
        // game says which one it is in a flag of its group manager (bit 2). The slots after the real ones hold nothing that can be trusted.
        var group = FFXIVClientStructs.FFXIV.Client.Game.Group.GroupManager.Instance();
        var small = group != null && (group->MainGroup.AllianceFlags & 2) != 0;
        var count = small ? 20 : 16;
        var slots = new UnitSnapshot?[count];

        for (var i = 0; i < count; i++)
        {
            var member = partyList.CreateAllianceMemberReference(partyList.GetAllianceMemberAddress(i));
            if (member is null || member.EntityId == 0 || own.Contains(member.EntityId))
                continue;

            try
            {
                var unit = ToUnit(member);

                // Nobody there: no job and no HP at all. It stays an empty slot (never a dead member).
                if (unit.JobId != 0 || unit.MaxHp != 0)
                    slots[i] = unit;
            }
            catch (Exception ex)
            {
                // One member that cannot be read leaves his cell empty; it never breaks the rest of the snapshot.
                Plugin.Log.Warning(ex, "NahjaUI: an alliance member could not be read.");
            }
        }

        return slots;
    }

    private IReadOnlyList<UnitSnapshot> BuildParty()
    {
        var partyList = Plugin.PartyList;
        if (partyList.Length == 0)
            return Array.Empty<UnitSnapshot>();

        var result = new List<UnitSnapshot>(partyList.Length);

        foreach (var member in partyList)
            result.Add(ToUnit(member));

        // The game's own party list has its own order (the "sort" option of the game: you first, by role...). The party list of
        // Dalamud does not follow it, so the position of each member in the game's list is read and used here.
        var order = ReadGameListOrder();
        if (order is not null && result.Count > 1)
            return result.OrderBy(unit => order.TryGetValue(unit.EntityId, out var position) ? position : int.MaxValue).ToList();

        return result;
    }

    // The position of each party member in the game's party list (entity id -> position, 0 is the top one), or null when it
    // cannot be read. It comes from the game's HUD agent: its list has you first, but each member also knows its real
    // position in the interface (HudPartyMember.Index).
    private static unsafe Dictionary<uint, int>? ReadGameListOrder()
    {
        var agent = AgentHUD.Instance();
        if (agent == null)
            return null;

        var members = agent->PartyMembers;
        var count = Math.Min((int)agent->PartyMemberCount, members.Length);
        if (count <= 0)
            return null;

        var order = new Dictionary<uint, int>(count);

        for (var i = 0; i < count; i++)
        {
            var entityId = members[i].EntityId;
            if (entityId != 0)
                order[entityId] = members[i].Index;
        }

        return order;
    }
}
