using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Windows;

/// <summary>
/// A tool for the Debug page only: shows live how the game itself lists its enhancements and enfeeblements (the HUD arrays of the
/// player, target, focus and party list), the game options that change them, and which of its status windows are on screen.
/// It is how the native behavior is copied without guessing: change an option in the game and watch what moves here.
/// Nothing in the interface depends on it.
/// </summary>
internal static unsafe class StatusDebugCard
{
    // Game options worth watching. Each one is tried in the UiConfig and in the UiControl section.
    private static readonly string[] KnownOptions =
    {
        "PartyListStatus", "PartyListStatusTimer", "BuffDispType", "ColorBuffGive", "ColorDebuffGive", "TargetInfoSelfBuff"
    };

    // The game's own windows that hold statuses. Guesses: the real names are in the "Game windows" card (filter "Status").
    private static readonly string[] NativeWindows =
    {
        "_Status", "_StatusCustom0", "_StatusCustom1", "_StatusCustom2", "_StatusCustom3",
        "_TargetInfoBuffDebuff", "_TargetInfoMainTarget", "_TargetInfo", "_FocusTargetInfo", "_PartyList"
    };

    private static string optionName = string.Empty;

    public static void Draw(GameSnapshot snapshot)
    {
        DrawPlayer(snapshot);
        DrawTargetAndFocus();
        DrawParty();
        DrawOptions();
        DrawOptionChanges();
        DrawWindows();
    }

    // ---------------------------------------------------------------- player

    private static void DrawPlayer(GameSnapshot snapshot)
    {
        // How many of the 30 icon slots are in use now: it sets the height of the card.
        var usedSlots = 0;
        var pre = HudNumberArray.Instance();
        if (pre != null)
        {
            var preIcons = pre->StatusIconIds;
            for (var i = 0; i < preIcons.Length; i++)
            {
                if (preIcons[i] != uint.MaxValue)
                    usedSlots++;
            }
        }

        Widgets.Card("##card_dbg_status_player", 16 + usedSlots * 2, () =>
        {
            Widgets.SectionLabel("Statuses: player (game HUD)");
            ImGui.TextColored(Theme.TextMuted, "Change HUD Layout > Status Effects (1, 3 or 4 groups, own first...) and watch the lists.");

            var hud = HudNumberArray.Instance();
            if (hud == null)
            {
                ImGui.TextColored(Theme.TextMuted, "The game's HUD data is not available now.");
                return;
            }

            ImGui.Text($"Layout: {hud->StatusElementLayout} ({(int)hud->StatusElementLayout})");
            ImGui.Text($"Enhancements (positive): {Slots(hud->PositiveStatusIndexes)}");
            ImGui.Text($"Enfeeblements (negative): {Slots(hud->NegativeStatusIndexes)}");
            ImGui.Text($"Others: {Slots(hud->OtherStatusIndexes)}");
            ImGui.Text($"Conditional enhancements (combo, 8): {Slots(hud->ComboStatusIndexes)}");
            ImGui.TextColored(Theme.TextMuted, "(a \"-\" is an empty place of the list; the numbers point to a slot below)");

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "The 30 icon slots of the game (empty ones are not shown). Flag 0x40000000 = bit set in the icon number:");

            var icons = hud->StatusIconIds;
            var dispellable = hud->StatusDispellables;
            var player = snapshot.Player;

            for (var i = 0; i < icons.Length; i++)
            {
                if (icons[i] == uint.MaxValue)
                    continue;

                var flag = (icons[i] & 0x40000000u) != 0;
                var icon = icons[i] & 0x3FFFFFFFu;
                var groups = new StringBuilder();
                AddIfContains(groups, "enhancements", hud->PositiveStatusIndexes, i);
                AddIfContains(groups, "enfeeblements", hud->NegativeStatusIndexes, i);
                AddIfContains(groups, "others", hud->OtherStatusIndexes, i);
                AddIfContains(groups, "conditional", hud->ComboStatusIndexes, i);

                ImGui.Text($"slot {i}: icon {icon}  flag {(flag ? "ON" : "off")}  dispellable {dispellable[i]}   {Describe(player, icon)}");
                ImGui.TextColored(Theme.TextMuted, $"      listed in: {(groups.Length == 0 ? "no list" : groups.ToString())}");
            }

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "The same statuses as the game's status list reports them, by the game's own category:");

            var enhancements = new StringBuilder();
            var enfeeblements = new StringBuilder();
            var others = new StringBuilder();

            if (player is not null)
            {
                foreach (var status in player.Statuses)
                {
                    var target = GameNames.StatusCategory(status.Id) switch
                    {
                        1 => enhancements,
                        2 => enfeeblements,
                        _ => others
                    };

                    if (target.Length > 0)
                        target.Append(", ");

                    var mine = status.SourceId == player.EntityId ? " mine" : " other";
                    target.Append(GameNames.StatusName(status.Id)).Append(" (").Append(status.Id).Append(", icon ")
                        .Append(GameNames.StatusIconId(status.Id)).Append(mine).Append(')');
                }
            }

            ImGui.TextWrapped($"Category 1, enhancements: {OrNone(enhancements)}");
            ImGui.TextWrapped($"Category 2, enfeeblements: {OrNone(enfeeblements)}");
            ImGui.TextWrapped($"Other category: {OrNone(others)}");
        });
    }

    // The list of slots of one group: "-" for an empty place, cut after the last used one.
    private static string Slots(ReadOnlySpan<int> values)
    {
        var last = -1;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] != -1)
                last = i;
        }

        if (last < 0)
            return "(empty)";

        var text = new StringBuilder();
        for (var i = 0; i <= last; i++)
        {
            if (i > 0)
                text.Append(' ');

            text.Append(values[i] == -1 ? "-" : values[i].ToString());
        }

        return text.ToString();
    }

    private static void AddIfContains(StringBuilder text, string name, ReadOnlySpan<int> values, int slot)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] != slot)
                continue;

            if (text.Length > 0)
                text.Append(", ");

            text.Append(name).Append(" (place ").Append(i).Append(')');
            return;
        }
    }

    // The status behind an icon number: first among the statuses the player has (the icon can be the base one plus the stack), then
    // anywhere in the game's table.
    private static string Describe(UnitSnapshot? player, uint icon)
    {
        if (player is not null)
        {
            foreach (var status in player.Statuses)
            {
                var baseIcon = GameNames.StatusIconId(status.Id);
                var stacks = Math.Max(1, GameNames.MaxStacks(status.Id));
                if (baseIcon != 0 && icon >= baseIcon && icon < baseIcon + stacks)
                    return $"{GameNames.StatusName(status.Id)} ({status.Id}), {(status.SourceId == player.EntityId ? "mine" : "from someone else")}";
            }
        }

        if (IconOwners.TryGetValue(icon, out var cached))
            return cached;

        var found = "not in the status list; ";
        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
        var names = new List<string>();
        foreach (var row in sheet)
        {
            var baseIcon = (uint)row.Icon;
            var stacks = Math.Max(1, (int)row.MaxStacks);
            if (baseIcon != 0 && icon >= baseIcon && icon < baseIcon + stacks && !string.IsNullOrEmpty(row.Name.ToString()))
            {
                names.Add($"{row.Name} ({row.RowId})");
                if (names.Count == 3)
                    break;
            }
        }

        found += names.Count == 0 ? "unknown" : "could be " + string.Join(" / ", names);
        IconOwners[icon] = found;
        return found;
    }

    private static readonly Dictionary<uint, string> IconOwners = new();

    // ---------------------------------------------------------------- target and focus

    private static void DrawTargetAndFocus()
    {
        Widgets.Card("##card_dbg_status_target", 9, () =>
        {
            Widgets.SectionLabel("Statuses: target and focus (game HUD)");

            var hud2 = Hud2NumberArray.Instance();
            if (hud2 == null)
            {
                ImGui.TextColored(Theme.TextMuted, "The game's target data is not available now.");
                return;
            }

            ImGui.Text($"Target info shown as one piece (not independent): {hud2->TargetInfoUICombined}");
            ImGui.Text($"Target statuses: {hud2->TargetStatusCount}");
            ImGui.Text($"   icons: {First(hud2->TargetStatusIconIds, hud2->TargetStatusCount)}");
            ImGui.Text($"   dispellable at: {TrueAt(hud2->TargetStatusDispellable)}");
            ImGui.Text($"Focus statuses: {hud2->FocusTargetStatusCount}");
            ImGui.Text($"   icons: {First(hud2->FocusTargetStatusIconIds, hud2->FocusTargetStatusCount)}");
            ImGui.Text($"   dispellable at: {TrueAt(hud2->FocusTargetStatusDispellable)}");
        });
    }

    // ---------------------------------------------------------------- party list

    private static void DrawParty()
    {
        Widgets.Card("##card_dbg_status_party", 11, () =>
        {
            Widgets.SectionLabel("Statuses: party list (game HUD)");

            var party = PartyListNumberArray.Instance();
            if (party == null)
            {
                ImGui.TextColored(Theme.TextMuted, "The game's party list data is not available now.");
                return;
            }

            var count = Math.Clamp(party->PartyListCount, 0, 8);
            ImGui.Text($"Party list members: {party->PartyListCount}");

            var members = party->PartyMembers;
            for (var i = 0; i < count; i++)
            {
                ImGui.Text($"   {i}: {members[i].StatusCount} statuses   icons {First(members[i].StatusIconIds, members[i].StatusCount)}");
            }
        });
    }

    // ---------------------------------------------------------------- options

    private static void DrawOptions()
    {
        Widgets.Card("##card_dbg_status_options", KnownOptions.Length + 16, () =>
        {
            Widgets.SectionLabel("Statuses: game options");
            ImGui.TextColored(Theme.TextMuted, "Live values. \"-\" means the game has no option with that name in that section.");

            foreach (var name in KnownOptions)
                ImGui.Text($"{name}:   {Read(name)}");

            ImGui.Spacing();
            ImGui.InputText("Any option name", ref optionName, 48);
            if (optionName.Length > 0)
                ImGui.Text($"{optionName}:   {Read(optionName)}");

            ImGui.Spacing();
            ImGui.InputText("Search all game options", ref searchText, 48);
            if (searchText.Length >= 3)
            {
                var shown = 0;
                foreach (var name in AllOptionNames())
                {
                    if (!name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                        continue;

                    ImGui.Text($"{name}:   {Read(name)}");
                    if (++shown >= 30)
                    {
                        ImGui.TextColored(Theme.TextMuted, "More than 30 matches: type more letters.");
                        break;
                    }
                }

                if (shown == 0)
                    ImGui.TextColored(Theme.TextMuted, "No option matches.");
            }
        });
    }

    private static Dictionary<string, string>? remembered;

    // Finds the game option behind a setting: remember every option, change the setting in the game, and see which ones changed.
    private static void DrawOptionChanges()
    {
        Widgets.Card("##card_dbg_option_changes", 14, () =>
        {
            Widgets.SectionLabel("Options that changed");
            ImGui.TextColored(Theme.TextMuted, "Press Remember, change a setting in the game (Character Configuration), then look here: the options whose value changed are listed.");

            if (ImGui.Button("Remember all options now"))
            {
                remembered = new Dictionary<string, string>();
                foreach (var name in AllOptionNames())
                    remembered[name] = Read(name);
            }

            if (remembered is null)
                return;

            var shown = 0;
            foreach (var (name, before) in remembered)
            {
                var now = Read(name);
                if (now == before)
                    continue;

                ImGui.Text($"{name}:   {before}  ->  {now}");
                if (++shown >= 20)
                {
                    ImGui.TextColored(Theme.TextMuted, "More than 20 changes: remember again and change only one setting.");
                    break;
                }
            }

            if (shown == 0)
                ImGui.TextColored(Theme.TextMuted, "Nothing has changed since you pressed Remember.");
        });
    }

    private static string searchText = string.Empty;
    private static List<string>? allOptionNames;

    // Every option name the game knows (names of Dalamud's UiConfigOption and UiControlOption lists), found by reflection.
    private static List<string> AllOptionNames()
    {
        if (allOptionNames is not null)
            return allOptionNames;

        var names = new HashSet<string>();
        var assembly = typeof(Dalamud.Plugin.Services.IGameConfig).Assembly;
        foreach (var typeName in new[] { "Dalamud.Game.Config.UiConfigOption", "Dalamud.Game.Config.UiControlOption" })
        {
            var type = assembly.GetType(typeName);
            if (type is null || !type.IsEnum)
                continue;

            foreach (var name in Enum.GetNames(type))
                names.Add(name);
        }

        allOptionNames = new List<string>(names);
        allOptionNames.Sort(StringComparer.OrdinalIgnoreCase);
        return allOptionNames;
    }

    private static string Read(string name)
    {
        var config = Plugin.GameConfig.UiConfig.TryGet(name, out uint a) ? a.ToString() : "-";
        var control = Plugin.GameConfig.UiControl.TryGet(name, out uint b) ? b.ToString() : "-";
        return $"UiConfig {config}   UiControl {control}";
    }

    // ---------------------------------------------------------------- windows

    private static void DrawWindows()
    {
        Widgets.Card("##card_dbg_status_windows", NativeWindows.Length + 4, () =>
        {
            Widgets.SectionLabel("Statuses: game windows on screen");
            ImGui.TextColored(Theme.TextMuted, "Guessed names. If one is always off, look for the real one in \"Game windows\" below (filter \"Status\").");

            foreach (var name in NativeWindows)
                ImGui.TextColored(GameWindowList.IsVisible(name) ? Theme.Text : Theme.TextMuted, $"{name}:   {(GameWindowList.IsVisible(name) ? "visible" : "not visible")}");
        });
    }

    // ---------------------------------------------------------------- helpers

    private static string OrNone(StringBuilder text) => text.Length == 0 ? "none" : text.ToString();

    // The values of a list up to the last one that is not the "empty" value of that list.
    private static string Join<T>(ReadOnlySpan<T> values, T empty) where T : IEquatable<T>
    {
        var last = -1;
        for (var i = 0; i < values.Length; i++)
        {
            if (!values[i].Equals(empty))
                last = i;
        }

        if (last < 0)
            return "(empty)";

        var text = new StringBuilder();
        for (var i = 0; i <= last; i++)
        {
            if (i > 0)
                text.Append(' ');

            text.Append(values[i]);
        }

        return text.ToString();
    }

    private static string First(ReadOnlySpan<uint> values, int count)
    {
        count = Math.Clamp(count, 0, values.Length);
        if (count == 0)
            return "(none)";

        var text = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                text.Append(' ');

            text.Append(values[i]);
        }

        return text.ToString();
    }

    private static string First(ReadOnlySpan<int> values, int count)
    {
        count = Math.Clamp(count, 0, values.Length);
        if (count == 0)
            return "(none)";

        var text = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                text.Append(' ');

            text.Append(values[i]);
        }

        return text.ToString();
    }

    private static string TrueAt(ReadOnlySpan<bool> flags)
    {
        var text = new StringBuilder();
        for (var i = 0; i < flags.Length; i++)
        {
            if (!flags[i])
                continue;

            if (text.Length > 0)
                text.Append(' ');

            text.Append(i);
        }

        return text.Length == 0 ? "(none)" : text.ToString();
    }
}
