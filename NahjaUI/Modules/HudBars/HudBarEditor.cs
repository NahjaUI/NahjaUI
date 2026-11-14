using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.PartyListBar;
using NahjaUI.Modules.AllianceList;

namespace NahjaUI.Modules.HudBars;

/// <summary>The "HUD bars" page of the main window: pick a frame at the top, then edit it in the cards below.</summary>
internal sealed class HudBarEditor
{
    // The jobs of the game by role, in the order of the game (for the on/off lists of the HP bar and the MP bar).
    private static readonly (string Role, uint[] Jobs)[] JobsByRole =
    {
        ("Tanks", new uint[] { 19, 21, 32, 37 }),
        ("Healers", new uint[] { 24, 28, 33, 40 }),
        ("Melee", new uint[] { 20, 22, 30, 34, 39, 41 }),
        ("Ranged", new uint[] { 23, 31, 38 }),
        ("Casters", new uint[] { 25, 27, 35, 42, 36 }),
        ("Crafters", new uint[] { 8, 9, 10, 11, 12, 13, 14, 15 }),
        ("Gatherers", new uint[] { 16, 17, 18 })
    };

    // The roles with Beastmaster (a melee, after Viper) added: its id is read from the game's job list by its abbreviation.
    private static uint beastmasterId;
    private static bool beastmasterLooked;

    private static List<(string Role, uint[] Jobs)> RolesWithBeastmaster()
    {
        if (!beastmasterLooked)
        {
            beastmasterId = GameNames.JobIdByAbbreviation("BST");
            beastmasterLooked = true;
        }

        var roles = new List<(string Role, uint[] Jobs)>(JobsByRole);
        if (beastmasterId != 0)
        {
            for (var i = 0; i < roles.Count; i++)
            {
                if (roles[i].Role == "Melee")
                    roles[i] = (roles[i].Role, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Append(roles[i].Jobs, beastmasterId)));
            }
        }

        return roles;
    }

    private const int JobsPerRow = 6;

    // The pages of the picker: the HP bar and the MP bar of the player are two separate pages.
    private const int HpPage = 0;
    private const int MpPage = 1;
    private const int TargetPage = 2;
    private const int TargetOfTargetPage = 3;
    private const int FocusPage = 4;
    private const int PartyPage = 5;
    private const int AlliancePage = 6;
    // (The MP cards come at the end of the Parameter bar page; MpPage is not a button of the picker.)
    private static readonly string[] PickerNames = { "Parameter bar", "MP bar", "Target", "Target of target", "Focus", "Party List", "Alliance List" };
    private static readonly string[] FrameNames = { "HP bar", "Player MP bar", "Target frame", "Target of target frame", "Focus frame" };

    private readonly Plugin plugin;

    // Which frame the cards below edit (an index into FrameNames).
    private int selectedFrame;

    // The name search of the list of the enhancements and the one of the enfeeblements of the party.
    private sealed class StatusSearchState
    {
        public string Text = string.Empty;
        public string Applied = string.Empty;
        public IReadOnlyList<StatusSearchResult> Results = Array.Empty<StatusSearchResult>();
    }

    private readonly StatusSearchState enhancementSearch = new();
    private readonly StatusSearchState enfeeblementSearch = new();
    private readonly StatusSearchState frameEnhancementSearch = new();
    private readonly StatusSearchState frameEnfeeblementSearch = new();

    // The search of the "always show" list of each card of icons, by card.
    private readonly Dictionary<string, StatusSearchState> extraSearches = new();

    public HudBarEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        // While this page is open, the frame shows sample values if the real unit is not there.
        plugin.HudBarSystem.PreviewRequested = true;

        // One tab per frame, like the nameplates and the progress bars.
        if (!ImGui.BeginTabBar("##uf_tabs"))
            return;

        for (var i = 0; i < PickerNames.Length; i++)
        {
            if (i == MpPage)
                continue;

            if (!ImGui.BeginTabItem(PickerNames[i]))
                continue;

            selectedFrame = i;

            // The pages that only hold sub-tabs (Parameter bar; Target, Focus and Party List with Frame and Status icons) let the sub-tabs scroll their own bodies.
            using (Widgets.TabBody($"##uf_body_{i}", i is not (HpPage or TargetPage or FocusPage or PartyPage)))
                DrawPage();

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    // The cards of the selected frame.
    private void DrawPage()
    {
        var frames = plugin.Configuration.HudBars;
        var system = plugin.HudBarSystem;


        // The alliance list: one grid with its own cards.
        if (selectedFrame == AlliancePage)
        {
            plugin.AllianceListEditorPage.Draw();
            return;
        }

        // The party list: the same cards as the other frames (their look), then the ones that are only about the party.
        if (selectedFrame == PartyPage)
        {
            plugin.PartyListSystem.PreviewRequested = true;

            var party = frames.Party;
            DrawFrameTabs("party",
                () =>
                {
                    DrawGeneral(party.Look, plugin.PartyListSystem.PartyElement, true);
                    DrawPartyOptions(party);
                    DrawName(party.Look);
                    DrawHp(party.Look);
                },
                () =>
                {
                    DrawStatusRows("##card_pf_enhancements", "Enhancements", party.Enhancements, true, enhancementSearch);
                    DrawStatusRows("##card_pf_enfeeblements", "Enfeeblements", party.Enfeeblements, false, enfeeblementSearch, canOnlyMine: false);
                });
            return;
        }

        var (settings, element) = selectedFrame switch
        {
            TargetPage => (frames.Target, system.TargetElement),
            TargetOfTargetPage => (frames.TargetOfTarget, system.TargetOfTargetElement),
            FocusPage => (frames.Focus, system.FocusElement),
            _ => (frames.Player, system.PlayerElement)
        };

        var isPlayer = selectedFrame == HpPage;

        // The Parameter bar page has two sub-tabs, one for the HP bar and one for the MP bar.
        if (isPlayer)
        {
            DrawParameterBarTabs(settings, element);
            return;
        }

        // The target of target has no status icons: only its cards, without tabs.
        if (selectedFrame == TargetOfTargetPage)
        {
            DrawGeneral(settings, element, isPlayer);
            DrawName(settings);
            DrawHp(settings);
            return;
        }

        // The same order on every page: the frame (general, texts), then its status icons (enhancements and enfeeblements).
        // The player has its own page for the statuses, "Status effects".
        var frame = selectedFrame;
        DrawFrameTabs($"frame{frame}",
            () =>
            {
                DrawGeneral(settings, element, isPlayer);
                DrawName(settings);
                DrawHp(settings);
            },
            () =>
            {
                DrawFrameStatusOptions(settings);
                DrawStatusRows($"##card_uf{frame}_enhancements", "Enhancements", settings.Enhancements, true, frameEnhancementSearch);
                DrawStatusRows($"##card_uf{frame}_enfeeblements", "Enfeeblements", settings.Enfeeblements, false, frameEnfeeblementSearch);
            });
    }

    // The two sub-tabs of a frame (the same names as in the nameplates and the enmity list): the frame itself and its status icons.
    private static void DrawFrameTabs(string id, Action frame, Action statusIcons)
    {
        if (!ImGui.BeginTabBar($"##uf_sub_tabs_{id}"))
            return;

        if (ImGui.BeginTabItem("Frame"))
        {
            using (Widgets.TabBody($"##uf_body_{id}_frame"))
                frame();

            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Status icons"))
        {
            using (Widgets.TabBody($"##uf_body_{id}_status"))
                statusIcons();

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    // The Parameter bar page (the game's name for the HP and MP bars of the player): one sub-tab for each bar.
    private void DrawParameterBarTabs(HudBarSettings settings, LayoutElement element)
    {
        if (!ImGui.BeginTabBar("##uf_param_tabs"))
            return;

        if (ImGui.BeginTabItem("HP Bar"))
        {
            using (Widgets.TabBody("##uf_body_hp"))
            {
                DrawGeneral(settings, element, true);
                DrawName(settings);
                DrawHp(settings);
            }

            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("MP Bar"))
        {
            using (Widgets.TabBody("##uf_body_mp"))
                DrawMp(settings);

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    // ---------------------------------------------------------------- cards

    private void DrawGeneral(HudBarSettings settings, LayoutElement element, bool isPlayer)
    {
        Widgets.Card("##card_uf_general", selectedFrame == TargetOfTargetPage ? 26 : selectedFrame == HpPage ? 29 : 25, () =>
        {
            Widgets.SectionLabel(selectedFrame == PartyPage ? "Party List" : FrameNames[selectedFrame]);

            // The player's Enabled is the master switch of the whole Parameter bar page (HP and MP).
            {
                var enabled = settings.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                {
                    settings.Enabled = enabled;
                    plugin.RequestSave();
                }
                Widgets.EnabledHint(selectedFrame == TargetPage ? "It only works if the game's target info is set to separate parts in its HUD layout."
                    : selectedFrame == HpPage ? "Turns the whole Parameter bar on or off: the HP bar and the MP bar."
                    : selectedFrame == PartyPage ? "The game's Party List is the one hidden."
                    : null);
            }

            if (selectedFrame == TargetOfTargetPage)
            {
                var showFrame = settings.ShowFrame;
                if (ImGui.Checkbox("Show the HP bar (off: only the name)", ref showFrame))
                {
                    settings.ShowFrame = showFrame;
                    plugin.RequestSave();
                }
            }

            ImGui.Spacing();

            // The size belongs to the layout system, like the position.
            var layout = element.Layout;

            var width = layout.Width;
            if (ImGui.InputInt("Width (px)", ref width, 2, 10))
            {
                layout.Width = Math.Clamp(width, 1, 4000);
                plugin.RequestSave();
            }

            var height = layout.Height;
            if (ImGui.InputInt("Height (px)", ref height, 2, 10))
            {
                layout.Height = Math.Clamp(height, 1, 1000);
                plugin.RequestSave();
            }

            ImGui.Spacing();

            // Only your own HP bar can be switched off on some jobs.
            if (selectedFrame == HpPage)
            {
                ImGui.Spacing();

                if (DrawJobsMenu("hp", "HP bar", settings.IsHpOn, settings.SetHpOn))
                    plugin.RequestSave();
            }
        });
    }

    // What is only about the party: space between bars, MP bar, and how the targeted member is marked.
    private void DrawPartyOptions(PartyListSettings party)
    {
        Widgets.Card("##card_pf_options", 23, () =>
        {
            Widgets.SectionLabel("Party options");

            var spacing = party.RowSpacingPx;
            if (ImGui.SliderInt("Space between bars (px)", ref spacing, 0, 30))
            {
                party.RowSpacingPx = spacing;
                plugin.RequestSave();
            }

            var showJobIcon = party.ShowJobIcon;
            if (ImGui.Checkbox("Show the job icon", ref showJobIcon))
            {
                party.ShowJobIcon = showJobIcon;
                plugin.RequestSave();
            }

            if (party.ShowJobIcon)
            {
                var iconStyle = party.JobIconStyle;
                if (EnumCombo("Icon set", ref iconStyle, style => style == PartyJobIconStyle.Plain ? "Plain" : "Light"))
                {
                    party.JobIconStyle = iconStyle;
                    plugin.RequestSave();
                }

                var iconSize = party.JobIconSizePx;
                if (ImGui.SliderInt("Icon size (px)", ref iconSize, 8, 64))
                {
                    party.JobIconSizePx = iconSize;
                    plugin.RequestSave();
                }

                var iconBarAnchor = party.JobIconBarAnchor;
                if (EnumCombo("Anchor on the bar", ref iconBarAnchor, point => point.Label()))
                {
                    party.JobIconBarAnchor = iconBarAnchor;
                    plugin.RequestSave();
                }

                var iconSelfAnchor = party.JobIconSelfAnchor;
                if (EnumCombo("Anchor of the icon", ref iconSelfAnchor, point => point.Label()))
                {
                    party.JobIconSelfAnchor = iconSelfAnchor;
                    plugin.RequestSave();
                }

                var iconOffsetX = party.JobIconOffsetX;
                if (ImGui.InputInt("Offset X (px)", ref iconOffsetX, 1, 5))
                {
                    party.JobIconOffsetX = Math.Clamp(iconOffsetX, -500, 500);
                    plugin.RequestSave();
                }

                // Up is plus (the saved value counts downwards).
                var iconOffsetY = -party.JobIconOffsetY;
                if (ImGui.InputInt("Offset Y (px)", ref iconOffsetY, 1, 5))
                {
                    party.JobIconOffsetY = -Math.Clamp(iconOffsetY, -500, 500);
                    plugin.RequestSave();
                }
            }

        });

        Widgets.Card("##card_pf_mp", 16, () =>
        {
            Widgets.SectionLabel("MP bar");

            var mpMode = party.MpMode;
            if (EnumCombo("Show it for", ref mpMode, PartyMpLabel))
            {
                party.MpMode = mpMode;
                plugin.RequestSave();
            }

            var mpWidth = party.MpWidthPx;
            if (ImGui.InputInt("Width (px, 0 = same as the bar)", ref mpWidth, 2, 10))
            {
                party.MpWidthPx = Math.Clamp(mpWidth, 0, 500);
                plugin.RequestSave();
            }

            var mpHeight = party.Look.MpHeightPx;
            if (ImGui.InputInt("Height (px)", ref mpHeight, 2, 10))
            {
                party.Look.MpHeightPx = Math.Clamp(mpHeight, 1, 1000);
                plugin.RequestSave();
            }

            var barAnchor = party.MpBarAnchor;
            if (EnumCombo("Anchor on the bar", ref barAnchor, point => point.Label()))
            {
                party.MpBarAnchor = barAnchor;
                plugin.RequestSave();
            }

            var selfAnchor = party.MpSelfAnchor;
            if (EnumCombo("Anchor of the MP bar", ref selfAnchor, point => point.Label()))
            {
                party.MpSelfAnchor = selfAnchor;
                plugin.RequestSave();
            }

            var offsetX = party.MpOffsetX;
            if (ImGui.InputInt("Offset X (px)", ref offsetX, 1, 5))
            {
                party.MpOffsetX = Math.Clamp(offsetX, -500, 500);
                plugin.RequestSave();
            }

            // Up is plus (the saved value counts downwards, so the sign is turned in the editors).
            var offsetY = -party.MpOffsetY;
            if (ImGui.InputInt("Offset Y (px)", ref offsetY, 1, 5))
            {
                party.MpOffsetY = -Math.Clamp(offsetY, -500, 500);
                plugin.RequestSave();
            }

        });
    }

    // Options shared by the two rows of a frame.
    private void DrawFrameStatusOptions(HudBarSettings settings)
    {
        if (selectedFrame != TargetPage)
            return;

    }

    // One row of icons of the party (enhancements or enfeeblements): what it shows, where it goes, how big the icons are.
    private void DrawStatusRows(string cardId, string title, StatusRowSettings row, bool isEnhancement, StatusSearchState search, bool canAttach = true, bool canOnlyMine = true)
    {
        var kind = isEnhancement ? "enhancement" : "enfeeblement";

        // Run the name search only when the text changed.
        if (search.Text != search.Applied)
        {
            search.Applied = search.Text;
            search.Results = search.Text.Trim().Length >= 3
                ? GameNames.SearchStatuses(search.Text.Trim(), 10)
                : Array.Empty<StatusSearchResult>();
        }

        if (!extraSearches.TryGetValue(cardId, out var extra))
            extraSearches[cardId] = extra = new StatusSearchState();

        if (extra.Text != extra.Applied)
        {
            extra.Applied = extra.Text;
            extra.Results = extra.Text.Trim().Length >= 3
                ? GameNames.SearchStatuses(extra.Text.Trim(), 10)
                : Array.Empty<StatusSearchResult>();
        }

        Widgets.Card(cardId, 44 + plugin.Configuration.Filters.Filters.Count * 2 + row.StatusIds.Count + search.Results.Count + row.ExtraStatusIds.Count + extra.Results.Count, () =>
        {
            Widgets.SectionLabel(title);

            var enabled = row.Enabled;
            if (ImGui.Checkbox($"Show {kind}s next to the bar", ref enabled))
            {
                row.Enabled = enabled;
                plugin.RequestSave();
            }

            // On the party list nobody applies enfeeblements to their friends: the option would only hide them all.
            if (canOnlyMine)
            {
                var onlyMine = row.OnlyMine;
                if (ImGui.Checkbox("Only the ones I applied", ref onlyMine))
                {
                    row.OnlyMine = onlyMine;
                    plugin.RequestSave();
                }
            }
            else if (row.OnlyMine)
            {
                row.OnlyMine = false;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var filter = row.Filter;
            if (EnumCombo("Which ones", ref filter, mode => PartyFilterLabel(mode, kind)))
            {
                row.Filter = filter;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            // The row can follow the other row of the frame instead of the bar (the enhancements one the enfeeblements and the other way round).
            if (canAttach)
            {
                var otherName = isEnhancement ? "The enfeeblements row" : "The enhancements row";
                if (ImGui.BeginCombo("Attach to", row.AttachTo == StatusRowAttach.Frame ? "The frame" : otherName, ImGuiComboFlags.None))
                {
                    if (ImGui.Selectable("The frame", row.AttachTo == StatusRowAttach.Frame))
                    {
                        row.AttachTo = StatusRowAttach.Frame;
                        plugin.RequestSave();
                    }

                    if (ImGui.Selectable(otherName, row.AttachTo == StatusRowAttach.OtherRow))
                    {
                        row.AttachTo = StatusRowAttach.OtherRow;
                        plugin.RequestSave();
                    }

                    ImGui.EndCombo();
                }
            }

            var attachedToRow = canAttach && row.AttachTo == StatusRowAttach.OtherRow;
            var barAnchor = row.BarAnchor;
            if (EnumCombo(attachedToRow ? "Anchor on the other row" : "Anchor on the bar", ref barAnchor, point => point.Label()))
            {
                row.BarAnchor = barAnchor;
                plugin.RequestSave();
            }

            var iconAnchor = row.IconAnchor;
            if (EnumCombo("Anchor of the first icon", ref iconAnchor, point => point.Label()))
            {
                row.IconAnchor = iconAnchor;
                plugin.RequestSave();
            }

            var offsetX = row.OffsetX;
            if (ImGui.InputInt("Offset X (px)", ref offsetX, 1, 5))
            {
                row.OffsetX = Math.Clamp(offsetX, -500, 500);
                plugin.RequestSave();
            }

            // Up is plus (the saved value counts downwards, so the sign is turned in the editors).
            var offsetY = -row.OffsetY;
            if (ImGui.InputInt("Offset Y (px)", ref offsetY, 1, 5))
            {
                row.OffsetY = -Math.Clamp(offsetY, -500, 500);
                plugin.RequestSave();
            }

            var growth = row.Growth;
            if (EnumCombo("Grow towards", ref growth, GrowthLabel))
            {
                row.Growth = growth;
                plugin.RequestSave();
            }

            if (NextLineCombo(row))
                plugin.RequestSave();

            var perLine = row.IconsPerLine;
            if (ImGui.SliderInt("Icons per line", ref perLine, 1, 30))
            {
                row.IconsPerLine = perLine;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var width = row.IconWidth;
            if (ImGui.InputInt("Icon width (px)", ref width, 2, 10))
            {
                row.IconWidth = Math.Clamp(width, 8, 128);
                plugin.RequestSave();
            }

            var height = row.IconHeight;
            if (ImGui.InputInt("Icon height (px)", ref height, 2, 10))
            {
                row.IconHeight = Math.Clamp(height, 8, 128);
                plugin.RequestSave();
            }

            var spacing = row.SpacingPx;
            if (ImGui.SliderInt("Space between icons (px)", ref spacing, 0, 30))
            {
                row.SpacingPx = spacing;
                plugin.RequestSave();
            }

            var maxIcons = row.MaxIcons;
            if (ImGui.SliderInt("Most icons shown", ref maxIcons, 1, 30))
            {
                row.MaxIcons = maxIcons;
                plugin.RequestSave();
            }

            var order = row.Order;
            if (EnumCombo("Order", ref order, OrderLabel))
            {
                row.Order = order;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var showTimer = row.ShowTimer;
            if (ImGui.Checkbox("Show the time left", ref showTimer))
            {
                row.ShowTimer = showTimer;
                plugin.RequestSave();
            }

            if (StatusTextControls(row, true))
                plugin.RequestSave();

            ImGui.Spacing();

            var showStacks = row.ShowStacks;
            if (ImGui.Checkbox("Show the stack count", ref showStacks))
            {
                row.ShowStacks = showStacks;
                plugin.RequestSave();
            }

            if (StatusTextControls(row, false))
                plugin.RequestSave();

            if (row.Filter == StatusRowFilter.All)
            {
                ImGui.Spacing();
                ImGui.TextColored(Theme.TextMuted, $"Every {kind} is shown. Pick \"Only the ones in my list\" to choose them.");
            }
            else
            {
                DrawPartyStatusList(row, isEnhancement, kind, search);
            }

            DrawExtraStatusList(row, kind, cardId, extra);
        });
    }

    // Statuses that are always shown whoever applied them, even with "only the ones I applied" on.
    private void DrawExtraStatusList(StatusRowSettings row, string kind, string cardId, StatusSearchState search)
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, $"Always show these {kind}s, whoever applied them:");

        if (row.ExtraStatusIds.Count == 0)
            ImGui.TextColored(Theme.TextMuted, "  Nothing in the list yet.");

        uint? toRemove = null;
        foreach (var id in row.ExtraStatusIds)
        {
            ImGui.Text($"  {GameNames.StatusName(id)} ({id})");
            ImGui.SameLine();

            if (ImGui.SmallButton($"Remove##xrm_{cardId}_{id}"))
                toRemove = id;
        }

        if (toRemove is not null)
        {
            row.ExtraStatusIds.Remove(toRemove.Value);
            plugin.RequestSave();
        }

        var text = search.Text;
        if (ImGui.InputText($"Search by name##xsearch_{cardId}", ref text, 64))
            search.Text = text;

        foreach (var result in search.Results)
        {
            if (row.ExtraStatusIds.Contains(result.Id))
                continue;

            if (ImGui.Selectable($"{result.Name} ({result.Id})##xres_{cardId}_{result.Id}"))
            {
                row.ExtraStatusIds.Add(result.Id);
                plugin.RequestSave();
            }
        }
    }

    private readonly Dictionary<string, StatusSearchState> otherRowSearches = new();

    /// <summary>One row of status icons for another page (the nameplates): the same card as the one of the HUD bars.</summary>
    internal void DrawStatusRowCard(string cardId, string title, StatusRowSettings row, bool isEnhancement, bool canAttach = true)
    {
        if (!otherRowSearches.TryGetValue(cardId, out var search))
            otherRowSearches[cardId] = search = new StatusSearchState();

        DrawStatusRows(cardId, title, row, isEnhancement, search, canAttach);
    }

    // The filters of the Filters page: the statuses of a ticked filter count as part of the list. The ticked ones come first, in the order
    // of priority, and each one can be limited to the statuses you applied.
    internal void DrawFilterChecks(StatusRowSettings row)
    {
        var filters = plugin.Configuration.Filters.Filters;
        if (filters.Count == 0)
            return;

        // Filters that were deleted do not stay in the list.
        row.FilterIds.RemoveAll(id => plugin.Configuration.Filters.Find(id) is null);
        row.OnlyMineFilterIds.RemoveAll(id => !row.FilterIds.Contains(id));

        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, "Filters (their statuses count as part of the list):");

        var priority = row.PriorityByFilter;
        if (ImGui.Checkbox($"The first filter goes first, whatever the time left##prio_{row.GetHashCode()}", ref priority))
        {
            row.PriorityByFilter = priority;
            plugin.RequestSave();
        }

        int? moveUp = null;
        int? moveDown = null;
        for (var i = 0; i < row.FilterIds.Count; i++)
        {
            var filter = plugin.Configuration.Filters.Find(row.FilterIds[i])!;

            var on = true;
            if (ImGui.Checkbox($"{filter.Name}##flt_{row.GetHashCode()}_{filter.Id}", ref on) && !on)
            {
                row.FilterIds.RemoveAt(i);
                row.OnlyMineFilterIds.Remove(filter.Id);
                plugin.RequestSave();
                return;
            }

            ImGui.SameLine();
            if (i > 0 && ImGui.SmallButton($"Up##up_{row.GetHashCode()}_{filter.Id}"))
                moveUp = i;

            if (i > 0)
                ImGui.SameLine();

            if (i < row.FilterIds.Count - 1 && ImGui.SmallButton($"Down##dn_{row.GetHashCode()}_{filter.Id}"))
                moveDown = i;

            if (i < row.FilterIds.Count - 1)
                ImGui.SameLine();

            var mine = row.OnlyMineFilterIds.Contains(filter.Id);
            if (ImGui.Checkbox($"Only mine##mine_{row.GetHashCode()}_{filter.Id}", ref mine))
            {
                if (mine)
                    row.OnlyMineFilterIds.Add(filter.Id);
                else
                    row.OnlyMineFilterIds.Remove(filter.Id);

                plugin.RequestSave();
            }
        }

        if (moveUp is { } up)
        {
            (row.FilterIds[up - 1], row.FilterIds[up]) = (row.FilterIds[up], row.FilterIds[up - 1]);
            plugin.RequestSave();
        }
        else if (moveDown is { } down)
        {
            (row.FilterIds[down + 1], row.FilterIds[down]) = (row.FilterIds[down], row.FilterIds[down + 1]);
            plugin.RequestSave();
        }

        foreach (var filter in filters)
        {
            if (row.FilterIds.Contains(filter.Id))
                continue;

            var on = false;
            if (ImGui.Checkbox($"{filter.Name}##flt_{row.GetHashCode()}_{filter.Id}", ref on) && on)
            {
                row.FilterIds.Add(filter.Id);
                plugin.RequestSave();
            }
        }
    }

    // The list of statuses of a row: what is in it, a way to add from what the party has right now, and a name search.
    private void DrawPartyStatusList(StatusRowSettings row, bool isEnhancement, string kind, StatusSearchState search)
    {
        DrawFilterChecks(row);

        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, row.Filter == StatusRowFilter.OnlyListed ? $"Only these {kind}s are shown:" : $"These {kind}s are hidden:");

        if (row.StatusIds.Count == 0)
            ImGui.TextColored(Theme.TextMuted, "  Nothing in the list yet.");

        uint? toRemove = null;
        for (var i = 0; i < row.StatusIds.Count; i++)
        {
            var id = row.StatusIds[i];
            ImGui.Text($"  {GameNames.StatusName(id)} ({id})");
            ImGui.SameLine();

            if (ImGui.SmallButton($"Remove##rm_{kind}_{id}"))
                toRemove = id;
        }

        if (toRemove is not null)
        {
            row.StatusIds.Remove(toRemove.Value);
            plugin.RequestSave();
        }

        // What the party has right now, to add with one click.
        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, $"On your party right now (click to add):");

        var wanted = (byte)(isEnhancement ? 1 : 2);
        var seen = new HashSet<uint>();

        foreach (var member in plugin.GameData.Current.Party)
        {
            foreach (var status in member.Statuses)
            {
                if (seen.Count >= 10)
                    break;

                if (GameNames.StatusCategory(status.Id) != wanted || row.StatusIds.Contains(status.Id) || !seen.Add(status.Id))
                    continue;

                if (ImGui.Selectable($"{GameNames.StatusName(status.Id)} ({status.Id})##now_{kind}_{status.Id}"))
                {
                    row.StatusIds.Add(status.Id);
                    plugin.RequestSave();
                }
            }
        }

        if (seen.Count == 0)
            ImGui.TextColored(Theme.TextMuted, "  Nothing new at the moment.");

        ImGui.Spacing();

        var text = search.Text;
        if (ImGui.InputText($"Search by name##search_{kind}", ref text, 64))
            search.Text = text;

        foreach (var result in search.Results)
        {
            if (row.StatusIds.Contains(result.Id))
                continue;

            var resultKind = result.Category switch
            {
                1 => "enhancement",
                2 => "enfeeblement",
                _ => string.Empty
            };

            if (ImGui.Selectable($"{result.Name} ({result.Id})   {resultKind}##res_{kind}_{result.Id}"))
            {
                row.StatusIds.Add(result.Id);
                plugin.RequestSave();
            }
        }
    }

    // A text size chosen from the fixed steps: each one is a real font, so the text is always sharp.
    internal static bool SizeCombo(string label, ref int value)
    {
        var changed = false;

        if (ImGui.BeginCombo(label, $"{value} px", ImGuiComboFlags.None))
        {
            foreach (var size in OverlayFonts.AvailableSizes)
            {
                if (ImGui.Selectable($"{size} px", value == size))
                {
                    value = size;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private static string PartyFilterLabel(StatusRowFilter mode, string kind) => mode switch
    {
        StatusRowFilter.All => $"Every {kind}",
        StatusRowFilter.OnlyListed => "Only the ones in my list or filters",
        _ => "All except my list and filters"
    };

    internal static string OrderLabel(StatusOrder order) => order == StatusOrder.Ascending
        ? "Ascending (shortest time first)"
        : "Descending (longest time first)";

    /// <summary>"Next line goes": the two sides that fit the growth of the row (above or under for a horizontal line, left or right for a vertical one).</summary>
    internal static bool NextLineCombo(StatusRowSettings row)
    {
        var options = row.GrowsHorizontally
            ? new[] { StatusRowGrowth.Down, StatusRowGrowth.Up }
            : new[] { StatusRowGrowth.Right, StatusRowGrowth.Left };

        var value = row.NextLineNow;
        if (!EnumCombo("Next line goes", ref value, GrowthLabel, options))
            return false;

        row.NextLine = value;
        return true;
    }

    /// <summary>The six words of every "Grow towards" of the interface.</summary>
    internal static string GrowthLabel(StatusRowGrowth growth) => growth switch
    {
        StatusRowGrowth.Left => "Left",
        StatusRowGrowth.Right => "Right",
        StatusRowGrowth.Up => "Up",
        StatusRowGrowth.Down => "Down",
        StatusRowGrowth.CenteredHorizontally => "Centered horizontally",
        _ => "Centered vertically"
    };

    private static string PartyMpLabel(PartyMpMode mode) => mode switch
    {
        PartyMpMode.Off => "No one",
        PartyMpMode.Healers => "Healers",
        PartyMpMode.HealersAndRaisers => "Healers and jobs that can raise",
        _ => "Everyone"
    };

    // One swatch per job. What is changed here is kept apart from the built-in colours, so it can always be reset.
    // Shown on the Appearance page: three colours, one per role, used by every frame (and the stripe of the party).
    internal void DrawRoleColors()
    {
        var frames = plugin.Configuration.HudBars;
        var defaults = new HudBarsSettings();

        {
            ImGui.TextColored(Theme.TextMuted, "Role colors: the same for every frame (player, target, target of target, focus and party). Used when a frame's HP color is set to the color of the role.");
            ImGui.TextColored(Theme.TextMuted, "Nameplates take their colors from the game's own options.");
            ImGui.Spacing();

            void Role(string label, Vector4 current, Vector4 original, Action<Vector4> apply)
            {
                var value = current;
                if (Widgets.ColorSetting(label, ref value, original))
                {
                    apply(value);
                    plugin.RequestSave();
                }
            }

            Role("Tank", frames.TankColor, defaults.TankColor, v => frames.TankColor = v);
            Role("Healer", frames.HealerColor, defaults.HealerColor, v => frames.HealerColor = v);
            Role("Damage", frames.DamageColor, defaults.DamageColor, v => frames.DamageColor = v);
            Role("Disciples of the Hand", frames.CrafterColor, defaults.CrafterColor, v => frames.CrafterColor = v);
            Role("Disciples of the Land", frames.GathererColor, defaults.GathererColor, v => frames.GathererColor = v);
        }
    }

    private void DrawName(HudBarSettings settings)
    {
        Widgets.Card("##card_uf_name", selectedFrame == HpPage ? 13 : 14, () =>
        {
            Widgets.SectionLabel("Name");

            var enabled = settings.Name.Enabled;
            if (ImGui.Checkbox("Show the name", ref enabled))
            {
                settings.Name.Enabled = enabled;
                plugin.RequestSave();
            }

            var format = settings.NameFormat;
            if (EnumCombo("Name format", ref format, NameFormatLabel))
            {
                settings.NameFormat = format;
                plugin.RequestSave();
            }

            // Your own frame is always a player, so the option only makes sense where the unit can be an enemy or an NPC.
            if (selectedFrame != HpPage)
            {
                var onlyPlayers = settings.NameFormatOnlyPlayers;
                if (ImGui.Checkbox("Only shorten the names of players", ref onlyPlayers))
                {
                    settings.NameFormatOnlyPlayers = onlyPlayers;
                    plugin.RequestSave();
                }
            }

            ImGui.Spacing();

            if (TextControls(settings.Name))
                plugin.RequestSave();
        });
    }

    private void DrawHp(HudBarSettings settings)
    {
        // What it looks like right now, with the real HP of this frame's unit (or a sample one).
        var snapshot = plugin.GameData.Current;
        var unit = selectedFrame switch
        {
            TargetPage => snapshot.Target,
            TargetOfTargetPage => snapshot.TargetOfTarget,
            FocusPage => snapshot.Focus,
            _ => snapshot.Player
        };

        DrawValueText("##card_uf_hp", "HP", settings, settings.Hp, unit?.Hp ?? 104071u, unit?.MaxHp ?? 140000u);
    }

    // The card of a number text (HP or MP): what it says, decimals, and the look of the text.
    // Both bars use it, so they have exactly the same choices.
    private void DrawValueText(string cardId, string name, IValueTextFormat format, FrameTextSettings text, uint current, uint max)
    {
        var custom = format.HpPreset == HpTextPreset.Custom;

        Widgets.Card(cardId, custom ? 19 : 16, () =>
        {
            Widgets.SectionLabel($"{name} text");

            var enabled = text.Enabled;
            if (ImGui.Checkbox($"Show the {name} text", ref enabled))
            {
                text.Enabled = enabled;
                plugin.RequestSave();
            }

            var preset = format.HpPreset;
            if (EnumCombo("What it says", ref preset, value => HpPresetLabel(value, name)))
            {
                SetPreset(format, preset);
                plugin.RequestSave();
            }

            if (custom)
            {
                var template = format.CustomTemplate ?? string.Empty;
                if (ImGui.InputText("Template", ref template, 64))
                {
                    SetTemplate(format, template);
                    plugin.RequestSave();
                }

                ImGui.TextColored(Theme.TextMuted, "{cur} {max} {def} in full, {curk} {maxk} {defk} abbreviated, {pct} percentage.");
                ImGui.TextColored(Theme.TextMuted, $"def is the missing {name}.");
            }

            var percentDecimals = format.PercentDecimals;
            if (DecimalsCombo("Percentage decimals", ref percentDecimals, new[] { "0 (87%)", "1 (87.3%)", "2 (87.34%)" }))
            {
                SetPercentDecimals(format, percentDecimals);
                plugin.RequestSave();
            }

            var abbreviationDecimals = format.AbbreviationDecimals;
            if (DecimalsCombo($"{name} decimals", ref abbreviationDecimals, new[] { "0 (738K)", "1 (738.2K)", "2 (738.24K)" }))
            {
                SetAbbreviationDecimals(format, abbreviationDecimals);
                plugin.RequestSave();
            }

            ImGui.Spacing();

            if (TextControls(text))
                plugin.RequestSave();

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, $"Now it reads: {UnitTextFormatter.FormatValue(format, current, max)}");
        });
    }

    // The two kinds of settings (a frame and the MP text) keep the same properties; the editor writes them through these.
    private static void SetPreset(IValueTextFormat f, HpTextPreset v) { if (f is HudBarSettings u) u.HpPreset = v; else if (f is MpTextSettings m) m.HpPreset = v; }
    private static void SetTemplate(IValueTextFormat f, string v) { if (f is HudBarSettings u) u.CustomTemplate = v; else if (f is MpTextSettings m) m.CustomTemplate = v; }
    private static void SetPercentDecimals(IValueTextFormat f, int v) { if (f is HudBarSettings u) u.PercentDecimals = v; else if (f is MpTextSettings m) m.PercentDecimals = v; }
    private static void SetAbbreviationDecimals(IValueTextFormat f, int v) { if (f is HudBarSettings u) u.AbbreviationDecimals = v; else if (f is MpTextSettings m) m.AbbreviationDecimals = v; }

    // ---------------------------------------------------------------- HP bar and MP bar of the player

    private void DrawMp(HudBarSettings settings)
    {
        var player = plugin.GameData.Current.Player;
        DrawMpBarCard(settings);
        DrawValueText("##card_uf_mp_text", "MP", settings.MpText, settings.MpText.Text, player?.Mp ?? 8400u, player?.MaxMp ?? 12000u);
    }

    private void DrawMpBarCard(HudBarSettings settings)
    {
        Widgets.Card("##card_uf_mp", 13, () =>
        {
            Widgets.SectionLabel("MP bar");

            var boxLayout = plugin.HudBarSystem.MpElement.Layout;

            var boxWidth = boxLayout.Width;
            if (ImGui.InputInt("Width (px)", ref boxWidth, 2, 10))
            {
                boxLayout.Width = Math.Clamp(boxWidth, 1, 4000);
                plugin.RequestSave();
            }

            var boxHeight = boxLayout.Height;
            if (ImGui.InputInt("Height (px)", ref boxHeight, 2, 10))
            {
                boxLayout.Height = Math.Clamp(boxHeight, 1, 1000);
                plugin.RequestSave();
            }

            ImGui.Spacing();

            if (DrawJobsMenu("mp", "MP bar", settings.IsMpOn, settings.SetMpOn, "Crafters show CP and gatherers GP."))
                plugin.RequestSave();
        });
    }

    // A color of the Appearance page, or its own when "Custom color" is on. Turning it on starts from the color in use, so nothing jumps.
    // Returns true when something changed (the caller saves).
    internal static bool SharedColor(string id, string label, bool custom, Vector4 effective, bool alpha, Action<bool> setCustom, Action<Vector4> setColor, Vector4? original = null)
    {
        var changed = false;
        var on = custom;
        if (ImGui.Checkbox($"Custom {label.ToLowerInvariant()}##cc_{id}", ref on))
        {
            if (on)
                setColor(effective);

            setCustom(on);
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Off: it uses the color of the Appearance page, the same one for the whole interface.");

        if (!custom)
            return changed;

        var value = effective;
        ImGui.Indent();
        if (Widgets.ColorSetting($"{label}##{id}", ref value, original, alpha))
        {
            setColor(value);
            changed = true;
        }

        ImGui.Unindent();
        return changed;
    }

    // The background and the border of a bar: the colors of Appearance, or its own when "Custom" is on. Returns true when something changed.
    internal static bool BackgroundAndBorder(string id, string backgroundLabel, string borderLabel,
        bool customBackground, Vector4 background, Action<bool> setCustomBackground, Action<Vector4> setBackground,
        bool customBorder, Vector4 border, Action<bool> setCustomBorder, Action<Vector4> setBorder)
    {
        var changed = SharedColor($"{id}_bg", backgroundLabel, customBackground, background, true, setCustomBackground, setBackground);
        changed |= SharedColor($"{id}_bd", borderLabel, customBorder, border, false, setCustomBorder, setBorder);
        return changed;
    }

    // How many jobs have the bar on, and a button that opens the menu to choose them (the same menu as the trackers).
    private bool DrawJobsMenu(string key, string name, Func<uint, bool> isOn, Action<uint, bool> setOn, string? note = null)
    {
        var total = 0;
        var on = 0;
        foreach (var (_, jobs) in RolesWithBeastmaster())
        {
            foreach (var jobId in jobs)
            {
                total++;
                if (isOn(jobId))
                    on++;
            }
        }

        ImGui.TextColored(Theme.TextMuted, on == total ? $"The {name} shows on every job." : $"The {name} shows on {on} of {total} jobs.");

        if (note is not null)
            ImGui.TextColored(Theme.TextMuted, note);

        var changed = false;
        var popupId = $"##jobs_popup_{key}";

        if (ImGui.Button($"Choose jobs...##{key}"))
            ImGui.OpenPopup(popupId);

        if (ImGui.BeginPopup(popupId))
        {
            changed = DrawJobToggles(key, isOn, setOn);
            ImGui.EndPopup();
        }

        return changed;
    }

    // One checkbox per job, by role. All / None for each role.
    private bool DrawJobToggles(string key, Func<uint, bool> isOn, Action<uint, bool> setOn)
    {
        var changed = false;

        foreach (var (role, jobs) in RolesWithBeastmaster())
        {
            ImGui.TextColored(Theme.TextMuted, role);
            ImGui.SameLine();

            if (ImGui.SmallButton($"All##{key}_all_{role}"))
            {
                foreach (var jobId in jobs)
                    setOn(jobId, true);

                changed = true;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton($"None##{key}_none_{role}"))
            {
                foreach (var jobId in jobs)
                    setOn(jobId, false);

                changed = true;
            }

            foreach (var jobId in jobs)
            {
                var on = isOn(jobId);
                if (ImGui.Checkbox($"{GameNames.JobAbbreviation(jobId)}##{key}_job_{jobId}", ref on))
                {
                    setOn(jobId, on);
                    changed = true;
                }

                ImGui.SameLine();
            }

            ImGui.NewLine();
            ImGui.Spacing();
        }

        return changed;
    }

    // A drop-down list with a few numbers (0, 1, 2 decimals), each with an example. The value is the position in the list.
    private static bool DecimalsCombo(string label, ref int value, string[] options)
    {
        var changed = false;
        var current = Math.Clamp(value, 0, options.Length - 1);

        if (ImGui.BeginCombo(label, options[current], ImGuiComboFlags.None))
        {
            for (var i = 0; i < options.Length; i++)
            {
                if (ImGui.Selectable(options[i], i == current))
                {
                    value = i;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    // ---------------------------------------------------------------- helpers

    // The time left or the stack count of a status icon, with the same controls and the same order as every other text:
    // position, the two offsets, size, font and color. Returns true when something changed.
    internal static bool StatusTextControls(NahjaUI.Modules.PartyListBar.StatusRowSettings row, bool time)
    {
        var changed = false;
        var name = time ? "Time" : "Stack";

        var anchor = time ? row.TimerAnchor : row.StacksAnchor;
        if (EnumCombo($"{name} position", ref anchor, point => point.Label()))
        {
            if (time) row.TimerAnchor = anchor; else row.StacksAnchor = anchor;
            changed = true;
        }

        var x = time ? row.TimerOffsetX : row.StacksOffsetX;
        if (ImGui.InputInt($"{name} offset X (px)", ref x, 1, 5))
        {
            if (time) row.TimerOffsetX = x; else row.StacksOffsetX = x;
            changed = true;
        }

        // Up is plus (the saved value counts downwards, so the sign is turned here).
        var y = -(time ? row.TimerOffsetY : row.StacksOffsetY);
        if (ImGui.InputInt($"{name} offset Y (px)", ref y, 1, 5))
        {
            if (time) row.TimerOffsetY = -y; else row.StacksOffsetY = -y;
            changed = true;
        }

        var size = time ? row.TimerSize : (row.StackTextSize > 0 ? row.StackTextSize : 8);
        if (SizeCombo($"{name} size (px)", ref size))
        {
            if (time) row.TimerSize = size; else row.StackTextSize = size;
            changed = true;
        }

        return changed;
    }

    // The point of the bar, the two offsets, the size, the font and the colour of one text.
    // defaultColor: the color the text starts with when it has its own one (it gets a reset button back to it).
    internal static bool TextControls(FrameTextSettings text, Vector4? defaultColor = null)
    {
        var changed = false;

        var anchor = text.Anchor;
        if (EnumCombo("Position", ref anchor, point => point.Label()))
        {
            text.Anchor = anchor;
            changed = true;
        }

        var x = text.OffsetX;
        if (ImGui.InputInt("Offset X (px)", ref x, 1, 5))
        {
            text.OffsetX = x;
            changed = true;
        }

        var y = -text.OffsetY;
        if (ImGui.InputInt("Offset Y (px)", ref y, 1, 5))
        {
            text.OffsetY = -y;
            changed = true;
        }

        // Font sizes are fixed steps: each one is a real font, so the text is always sharp.
        if (ImGui.BeginCombo("Text size (px)", $"{text.TextSize} px", ImGuiComboFlags.None))
        {
            foreach (var size in OverlayFonts.AvailableSizes)
            {
                if (ImGui.Selectable($"{size} px", text.TextSize == size))
                {
                    text.TextSize = size;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        if (SharedColor("text", "Text color", text.CustomColor, text.Color, false, on => text.CustomColor = on, c => text.Color = c, defaultColor))
            changed = true;

        return changed;
    }

    private static string HpColorModeLabel(HpColorMode mode) => mode switch
    {
        HpColorMode.Fixed => "One fixed color",
        HpColorMode.Role => "The color of the role (tank, healer, damage)",
        _ => mode.ToString()
    };


    private static string NameFormatLabel(NameFormat format) => format switch
    {
        NameFormat.Full => "Full name (Nahja Stark)",
        NameFormat.FirstName => "First name only (Nahja)",
        NameFormat.LastName => "Last name only (Stark)",
        NameFormat.FirstInitialLastName => "First initial and last name (N. Stark)",
        NameFormat.FirstNameLastInitial => "First name and last initial (Nahja S.)",
        NameFormat.Initials => "Initials (N. S.)",
        _ => format.ToString()
    };

    private static string HpPresetLabel(HpTextPreset preset, string name) => preset switch
    {
        HpTextPreset.CurrentAndPercent => $"{name} abbreviated and percentage (738K | 100%)",
        HpTextPreset.CurrentFullAndPercent => $"{name} in full and percentage (738,000 | 100%)",
        HpTextPreset.Current => $"{name} abbreviated (738K)",
        HpTextPreset.CurrentFull => $"{name} in full (738,000)",
        HpTextPreset.Percent => "Percentage only (100%)",
        HpTextPreset.CurrentAndMax => $"{name} and maximum (738K / 738K)",
        HpTextPreset.Deficit => $"Missing {name} (-12K)",
        HpTextPreset.Custom => "Custom template",
        _ => preset.ToString()
    };

    // A drop-down list for any enum. Returns true when the user picked a different value.
    internal static bool EnumCombo<T>(string label, ref T value, Func<T, string> labeler, IReadOnlyList<T>? options = null) where T : struct, Enum
    {
        var changed = false;

        if (ImGui.BeginCombo(label, labeler(value), ImGuiComboFlags.None))
        {
            foreach (var option in options ?? Enum.GetValues<T>())
            {
                if (ImGui.Selectable(labeler(option), EqualityComparer<T>.Default.Equals(option, value)))
                {
                    value = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }
}
