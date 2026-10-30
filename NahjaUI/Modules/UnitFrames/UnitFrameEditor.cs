using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.PartyFrames;

namespace NahjaUI.Modules.UnitFrames;

/// <summary>The "Unit frames" page of the main window: pick a frame at the top, then edit it in the cards below.</summary>
internal sealed class UnitFrameEditor
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

    private static readonly (string Value, string Label)[] Separators =
    {
        (" | ", "Vertical bar ( | )"),
        (" / ", "Slash ( / )"),
        (" - ", "Dash ( - )"),
        (" \u00B7 ", "Dot ( \u00B7 )"),
        (" ", "Space")
    };

    // The pages of the picker: the HP bar and the MP bar of the player are two separate pages.
    private const int HpPage = 0;
    private const int MpPage = 1;
    private const int TargetPage = 2;
    private const int TargetOfTargetPage = 3;
    private const int FocusPage = 4;
    private const int PartyPage = 5;
    // (The MP page is a tab inside the Parameter bar page, not a button of the picker.)
    private static readonly string[] PickerNames = { "Parameter bar", "MP bar", "Target", "Target of target", "Focus", "Party" };
    private static readonly string[] FrameNames = { "Player HP bar", "Player MP bar", "Target frame", "Target of target frame", "Focus frame" };

    private readonly Plugin plugin;

    // Which frame the cards below edit (an index into FrameNames).
    private int selectedFrame;

    // Inside the Parameter bar page: 0 HP bar, 1 MP bar.
    private int parameterTab;

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

    public UnitFrameEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        // While this page is open, the frame shows sample values if the real unit is not there.
        plugin.UnitFrameSystem.PreviewRequested = true;

        DrawFramePicker();

        var frames = plugin.Configuration.UnitFrames;
        var system = plugin.UnitFrameSystem;


        // The party frames: the same cards as the other frames (their look), then the ones that are only about the party.
        if (selectedFrame == PartyPage)
        {
            plugin.PartyFrameSystem.PreviewRequested = true;

            var party = frames.Party;
            DrawGeneral(party.Look, plugin.PartyFrameSystem.PartyElement, true);
            DrawPartyOptions(party);
            DrawPartyAuras("##card_pf_enhancements", "Enhancements", party.Enhancements, true, enhancementSearch);
            DrawPartyAuras("##card_pf_enfeeblements", "Enfeeblements", party.Enfeeblements, false, enfeeblementSearch);
            DrawName(party.Look);
            DrawHealth(party.Look);
            return;
        }

        // The Parameter bar (the game's name for the HP and MP bars of the player): one switch for both, then a tab each.
        if (selectedFrame == HpPage)
        {
            DrawParameterBarSwitch(frames.Player);

            if (parameterTab == 1)
            {
                DrawMp(frames.Player);
                return;
            }
        }

        var (settings, element) = selectedFrame switch
        {
            TargetPage => (frames.Target, system.TargetElement),
            TargetOfTargetPage => (frames.TargetOfTarget, system.TargetOfTargetElement),
            FocusPage => (frames.Focus, system.FocusElement),
            _ => (frames.Player, system.PlayerElement)
        };

        var isPlayer = selectedFrame == HpPage;

        DrawGeneral(settings, element, isPlayer);

        // Enhancements and enfeeblements next to the bar (not on the target of target; the player has its own page, "Status effects").
        if (selectedFrame != TargetOfTargetPage && selectedFrame != HpPage)
        {
            DrawFrameStatusOptions(settings);
            DrawPartyAuras($"##card_uf{selectedFrame}_enhancements", "Enhancements", settings.Enhancements, true, frameEnhancementSearch);
            DrawPartyAuras($"##card_uf{selectedFrame}_enfeeblements", "Enfeeblements", settings.Enfeeblements, false, frameEnfeeblementSearch);
        }

        DrawName(settings);
        DrawHealth(settings);

        // The jobs card is always the last one of the page.
        if (isPlayer)
            DrawHpJobs(settings);
    }

    // A row of buttons, one per frame; the selected one is lit with the accent colour.
    private void DrawFramePicker()
    {
        var first = true;
        for (var i = 0; i < PickerNames.Length; i++)
        {
            if (i == MpPage)
                continue;

            if (!first)
                ImGui.SameLine();

            first = false;

            var selected = i == selectedFrame;

            ImGui.PushStyleColor(ImGuiCol.Button, selected ? Theme.Mix(Theme.Field, Theme.Accent, 0.85f) : Theme.Field);
            if (ImGui.Button(PickerNames[i]))
                selectedFrame = i;
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
    }

    // ---------------------------------------------------------------- cards

    private void DrawGeneral(UnitFrameSettings settings, LayoutElement element, bool isPlayer)
    {
        Widgets.Card("##card_uf_general", selectedFrame == TargetOfTargetPage ? 26 : 25, () =>
        {
            Widgets.SectionLabel(selectedFrame == PartyPage ? "Party frames (the first bar; the others follow it downwards)" : FrameNames[selectedFrame]);

            // The player's Enabled is the master switch of the Parameter bar page (the card above).
            if (!isPlayer)
            {
                var enabled = settings.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                {
                    settings.Enabled = enabled;
                    plugin.RequestSave();
                }
            }

            // The game gives the target of target no menu: only the left click targets it.
            var clickable = settings.Clickable;
            var clickLabel = selectedFrame == TargetOfTargetPage ? "Clickable: left click targets" : "Clickable: left click targets, right click opens a menu";
            if (ImGui.Checkbox(clickLabel, ref clickable))
            {
                settings.Clickable = clickable;
                plugin.RequestSave();
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

            // The game shows the target of target inside its target bar, so it has no bar of its own to hide.
            var nativeLabel = selectedFrame switch
            {
                HpPage => "Hide the game's HP and MP bar",
                TargetPage => "Hide the game's target bar (and its target of target)",
                FocusPage => "Hide the game's focus bar",
                PartyPage => "Hide the game's party list",
                _ => null
            };

            if (nativeLabel is not null && !isPlayer)
            {
                var hideNative = settings.HideNative;
                if (ImGui.Checkbox(nativeLabel, ref hideNative))
                {
                    settings.HideNative = hideNative;
                    plugin.RequestSave();
                }

                if (selectedFrame == TargetPage)
                    ImGui.TextColored(Theme.TextMuted, "Needs the target info set to separate parts in the game's HUD layout.");
            }
            else if (!isPlayer)
            {
                ImGui.TextColored(Theme.TextMuted, "The game's target of target is part of its target bar: hide it with the Target frame.");
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

            var rounding = settings.RoundingPx;
            if (ImGui.SliderInt("Corner rounding (px)", ref rounding, 0, 12))
            {
                settings.RoundingPx = rounding;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var colorMode = settings.HpColorMode;
            if (EnumCombo("HP color", ref colorMode, HpColorModeLabel))
            {
                settings.HpColorMode = colorMode;
                plugin.RequestSave();
            }

            var hp = settings.FriendlyColor;
            if (ImGui.ColorEdit4("Fixed HP color", ref hp, ImGuiColorEditFlags.NoAlpha))
            {
                settings.FriendlyColor = new Vector4(hp.X, hp.Y, hp.Z, 1f);
                plugin.RequestSave();
            }

            // Enemies always use their own colour; the player's frame never shows one.
            if (!isPlayer)
            {
                var hostile = settings.HostileColor;
                if (ImGui.ColorEdit4("Enemy HP color", ref hostile, ImGuiColorEditFlags.NoAlpha))
                {
                    settings.HostileColor = new Vector4(hostile.X, hostile.Y, hostile.Z, 1f);
                    plugin.RequestSave();
                }
            }

            // Pets, minions and objects (furniture, chests, aetherytes...) have their own colours.
            if (!isPlayer)
            {
                var pet = settings.PetColor;
                if (ImGui.ColorEdit4("Pet / minion HP color", ref pet, ImGuiColorEditFlags.NoAlpha))
                {
                    settings.PetColor = new Vector4(pet.X, pet.Y, pet.Z, 1f);
                    plugin.RequestSave();
                }

                var obj = settings.ObjectColor;
                if (ImGui.ColorEdit4("Object color", ref obj, ImGuiColorEditFlags.NoAlpha))
                {
                    settings.ObjectColor = new Vector4(obj.X, obj.Y, obj.Z, 1f);
                    plugin.RequestSave();
                }
            }

            var background = settings.BackgroundColor;
            if (ImGui.ColorEdit4("Background color", ref background, ImGuiColorEditFlags.None))
            {
                settings.BackgroundColor = background;
                plugin.RequestSave();
            }

            var border = settings.BorderColor;
            if (ImGui.ColorEdit4("Border color", ref border, ImGuiColorEditFlags.NoAlpha))
            {
                settings.BorderColor = new Vector4(border.X, border.Y, border.Z, 1f);
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var showShield = settings.ShowShield;
            if (ImGui.Checkbox("Show shields on the HP bar", ref showShield))
            {
                settings.ShowShield = showShield;
                plugin.RequestSave();
            }

            var shield = settings.ShieldColor;
            if (ImGui.ColorEdit4("Shield color", ref shield, ImGuiColorEditFlags.NoAlpha))
            {
                settings.ShieldColor = new Vector4(shield.X, shield.Y, shield.Z, 1f);
                plugin.RequestSave();
            }

            var opacity = (int)MathF.Round(settings.ShieldOpacity * 100f);
            if (ImGui.SliderInt("Shield opacity (%)", ref opacity, 10, 100))
            {
                settings.ShieldOpacity = opacity / 100f;
                plugin.RequestSave();
            }
        });
    }

    // What is only about the party: space between bars, MP bar, and how the targeted member is marked.
    private void DrawPartyOptions(PartyFrameSettings party)
    {
        Widgets.Card("##card_pf_options", 20, () =>
        {
            Widgets.SectionLabel("Party options");

            var order = party.Order;
            if (EnumCombo("Order of the bars", ref order, PartyOrderLabel))
            {
                party.Order = order;
                plugin.RequestSave();
            }

            var showChocobo = party.ShowChocobo;
            if (ImGui.Checkbox("Show my chocobo at the end", ref showChocobo))
            {
                party.ShowChocobo = showChocobo;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var spacing = party.RowSpacingPx;
            if (ImGui.SliderInt("Space between bars (px)", ref spacing, 0, 30))
            {
                party.RowSpacingPx = spacing;
                plugin.RequestSave();
            }

            var roleStripe = party.RoleStripe;
            if (ImGui.Checkbox("Color stripe for the role (tank, healer, damage)", ref roleStripe))
            {
                party.RoleStripe = roleStripe;
                plugin.RequestSave();
            }

            var soloRaw = plugin.GameData.Current.Context.SoloOptionRaw;
            ImGui.TextColored(Theme.TextMuted, soloRaw < 0
                ? "The game's option could not be read."
                : $"Alone, the game shows its party list: {(soloRaw == 0 ? "yes" : "no")} (raw value {soloRaw}).");

            var showJobIcon = party.ShowJobIcon;
            if (ImGui.Checkbox("Job icon inside the bar (replaces the stripe)", ref showJobIcon))
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
            }

            ImGui.Spacing();

            var mpMode = party.MpMode;
            if (EnumCombo("MP bar", ref mpMode, PartyMpLabel))
            {
                party.MpMode = mpMode;
                plugin.RequestSave();
            }

            var mpHeight = party.Look.MpHeightPx;
            if (ImGui.SliderInt("MP bar height (px)", ref mpHeight, 2, 12))
            {
                party.Look.MpHeightPx = mpHeight;
                plugin.RequestSave();
            }

            var mpColor = party.Look.MpColor;
            if (ImGui.ColorEdit4("MP color", ref mpColor, ImGuiColorEditFlags.NoAlpha))
            {
                party.Look.MpColor = new Vector4(mpColor.X, mpColor.Y, mpColor.Z, 1f);
                plugin.RequestSave();
            }
        });
    }

    // Options shared by the two rows of a frame.
    private void DrawFrameStatusOptions(UnitFrameSettings settings)
    {
        if (selectedFrame != TargetPage)
            return;

        Widgets.Card($"##card_uf{selectedFrame}_native_status", 3, () =>
        {
            Widgets.SectionLabel("Enhancements and enfeeblements of the game");

            var hide = settings.HideNativeStatuses;
            if (ImGui.Checkbox("Hide the game's enhancements and enfeeblements of the target while mine are on", ref hide))
            {
                settings.HideNativeStatuses = hide;
                plugin.RequestSave();
            }
        });
    }

    // One row of icons of the party (enhancements or enfeeblements): what it shows, where it goes, how big the icons are.
    private void DrawPartyAuras(string cardId, string title, PartyAuraSettings row, bool isEnhancement, StatusSearchState search)
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

        Widgets.Card(cardId, 40 + row.StatusIds.Count + search.Results.Count + row.ExtraStatusIds.Count + extra.Results.Count, () =>
        {
            Widgets.SectionLabel(title);

            var enabled = row.Enabled;
            if (ImGui.Checkbox($"Show {kind}s next to the bar", ref enabled))
            {
                row.Enabled = enabled;
                plugin.RequestSave();
            }

            var onlyMine = row.OnlyMine;
            if (ImGui.Checkbox("Only the ones I applied", ref onlyMine))
            {
                row.OnlyMine = onlyMine;
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

            var barAnchor = row.BarAnchor;
            if (EnumCombo("Anchor on the bar", ref barAnchor, point => point.Label()))
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
            if (EnumCombo("Grow towards", ref growth, PartyGrowthLabel))
            {
                row.Growth = growth;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var width = row.IconWidth;
            if (ImGui.InputInt("Icon width (px)", ref width, 1, 4))
            {
                row.IconWidth = Math.Clamp(width, 8, 128);
                plugin.RequestSave();
            }

            var height = row.IconHeight;
            if (ImGui.InputInt("Icon height (px)", ref height, 1, 4))
            {
                row.IconHeight = Math.Clamp(height, 8, 128);
                plugin.RequestSave();
            }

            var crop = row.CropPercent;
            if (ImGui.SliderInt("Icon crop (%)", ref crop, 0, 30))
            {
                row.CropPercent = crop;
                plugin.RequestSave();
            }

            var cornerRadius = row.CornerRadiusPx;
            if (ImGui.SliderInt("Corner rounding (px)", ref cornerRadius, 0, 12))
            {
                row.CornerRadiusPx = cornerRadius;
                plugin.RequestSave();
            }

            var spacing = row.SpacingPx;
            if (ImGui.SliderInt("Space between icons (px)", ref spacing, 0, 20))
            {
                row.SpacingPx = spacing;
                plugin.RequestSave();
            }

            var maxIcons = row.MaxIcons;
            if (ImGui.SliderInt("Most icons shown", ref maxIcons, 1, 12))
            {
                row.MaxIcons = maxIcons;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var showTimer = row.ShowTimer;
            if (ImGui.Checkbox("Show the time left", ref showTimer))
            {
                row.ShowTimer = showTimer;
                plugin.RequestSave();
            }

            var timerSize = row.TimerSize;
            if (SizeCombo("Time size (px)", ref timerSize))
            {
                row.TimerSize = timerSize;
                plugin.RequestSave();
            }

            var timerAnchor = row.TimerAnchor;
            if (EnumCombo("Time position", ref timerAnchor, point => point.Label()))
            {
                row.TimerAnchor = timerAnchor;
                plugin.RequestSave();
            }

            var timerX = row.TimerOffsetX;
            if (ImGui.InputInt("Time offset X (px)", ref timerX, 1, 5))
            {
                row.TimerOffsetX = timerX;
                plugin.RequestSave();
            }

            var timerY = -row.TimerOffsetY;
            if (ImGui.InputInt("Time offset Y (px)", ref timerY, 1, 5))
            {
                row.TimerOffsetY = -timerY;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var showStacks = row.ShowStacks;
            if (ImGui.Checkbox("Show the stack count", ref showStacks))
            {
                row.ShowStacks = showStacks;
                plugin.RequestSave();
            }

            var stackSize = row.StackTextSize > 0 ? row.StackTextSize : 8;
            if (ImGui.SliderInt("Stack size (px)", ref stackSize, 4, 24))
            {
                row.StackTextSize = stackSize;
                plugin.RequestSave();
            }

            var stacksAnchor = row.StacksAnchor;
            if (EnumCombo("Stack position", ref stacksAnchor, point => point.Label()))
            {
                row.StacksAnchor = stacksAnchor;
                plugin.RequestSave();
            }

            var stacksX = row.StacksOffsetX;
            if (ImGui.InputInt("Stack offset X (px)", ref stacksX, 1, 5))
            {
                row.StacksOffsetX = stacksX;
                plugin.RequestSave();
            }

            var stacksY = -row.StacksOffsetY;
            if (ImGui.InputInt("Stack offset Y (px)", ref stacksY, 1, 5))
            {
                row.StacksOffsetY = -stacksY;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var font = row.Font;
            if (EnumCombo("Text font", ref font, FontLabel))
            {
                row.Font = font;
                plugin.RequestSave();
            }

            if (row.Filter == PartyAuraFilter.All)
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
    private void DrawExtraStatusList(PartyAuraSettings row, string kind, string cardId, StatusSearchState search)
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

    // The list of statuses of a row: what is in it, a way to add from what the party has right now, and a name search.
    private void DrawPartyStatusList(PartyAuraSettings row, bool isEnhancement, string kind, StatusSearchState search)
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, row.Filter == PartyAuraFilter.OnlyListed ? $"Only these {kind}s are shown:" : $"These {kind}s are hidden:");

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

    private static string PartyFilterLabel(PartyAuraFilter mode, string kind) => mode switch
    {
        PartyAuraFilter.All => $"Every {kind}",
        PartyAuraFilter.OnlyListed => "Only the ones in my list",
        _ => "All except the ones in my list"
    };

    private static string PartyGrowthLabel(PartyAuraGrowth growth) => growth switch
    {
        PartyAuraGrowth.Left => "Left",
        PartyAuraGrowth.Right => "Right",
        PartyAuraGrowth.Up => "Up",
        _ => "Down"
    };

    private static string PartyOrderLabel(PartyOrder order) => order switch
    {
        PartyOrder.GameOrder => "Same as the game's party list (follows its option)",
        _ => "Me first, then the others"
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
    internal void DrawRoleColorsCard()
    {
        var frames = plugin.Configuration.UnitFrames;

        Widgets.Card("##card_uf_rolecolors", 9, () =>
        {
            Widgets.SectionLabel("Role colors");
            ImGui.TextColored(Theme.TextMuted, "The same for every frame (player, target, target of target, focus and party). Used when a frame's HP color is set to the color of the role,");
            ImGui.TextColored(Theme.TextMuted, "and for the stripe of the party. Nameplates take their colors from the game's own options.");
            ImGui.Spacing();

            var tank = frames.TankColor;
            if (ImGui.ColorEdit4("Tank", ref tank, ImGuiColorEditFlags.NoAlpha))
            {
                frames.TankColor = new Vector4(tank.X, tank.Y, tank.Z, 1f);
                plugin.RequestSave();
            }

            var healer = frames.HealerColor;
            if (ImGui.ColorEdit4("Healer", ref healer, ImGuiColorEditFlags.NoAlpha))
            {
                frames.HealerColor = new Vector4(healer.X, healer.Y, healer.Z, 1f);
                plugin.RequestSave();
            }

            var damage = frames.DamageColor;
            if (ImGui.ColorEdit4("Damage", ref damage, ImGuiColorEditFlags.NoAlpha))
            {
                frames.DamageColor = new Vector4(damage.X, damage.Y, damage.Z, 1f);
                plugin.RequestSave();
            }

            ImGui.Spacing();
            if (ImGui.Button("Reset role colors"))
            {
                var defaults = new UnitFramesSettings();
                frames.TankColor = defaults.TankColor;
                frames.HealerColor = defaults.HealerColor;
                frames.DamageColor = defaults.DamageColor;
                plugin.RequestSave();
            }
        });
    }

    private void DrawName(UnitFrameSettings settings)
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

    private void DrawHealth(UnitFrameSettings settings)
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

        DrawValueText("##card_uf_health", "HP", settings, settings.Health, unit?.Hp ?? 104071u, unit?.MaxHp ?? 140000u);
    }

    // The card of a number text (HP or MP): what it says, separator, decimals, thousands, and the look of the text.
    // Both bars use it, so they have exactly the same choices.
    private void DrawValueText(string cardId, string name, IValueTextFormat format, FrameTextSettings text, uint current, uint max)
    {
        var custom = format.HealthPreset == HealthTextPreset.Custom;

        Widgets.Card(cardId, custom ? 22 : 19, () =>
        {
            Widgets.SectionLabel($"{name} text");

            var enabled = text.Enabled;
            if (ImGui.Checkbox($"Show the {name} text", ref enabled))
            {
                text.Enabled = enabled;
                plugin.RequestSave();
            }

            var preset = format.HealthPreset;
            if (EnumCombo("What it says", ref preset, value => HealthPresetLabel(value, name)))
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

            // The separator only matters for the presets that show two things.
            var separatorLabel = SeparatorLabel(format.Separator);
            if (ImGui.BeginCombo("Separator", separatorLabel, ImGuiComboFlags.None))
            {
                foreach (var (value, label) in Separators)
                {
                    if (ImGui.Selectable(label, value == format.Separator))
                    {
                        SetSeparator(format, value);
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }

            var percentDecimals = format.PercentDecimals;
            if (ImGui.SliderInt("Percentage decimals", ref percentDecimals, 0, 2))
            {
                SetPercentDecimals(format, percentDecimals);
                plugin.RequestSave();
            }

            var abbreviationDecimals = format.AbbreviationDecimals;
            if (ImGui.SliderInt("Decimals of 738K", ref abbreviationDecimals, 0, 2))
            {
                SetAbbreviationDecimals(format, abbreviationDecimals);
                plugin.RequestSave();
            }

            var thousands = format.Thousands;
            if (EnumCombo("Thousands separator", ref thousands, ThousandsLabel))
            {
                SetThousands(format, thousands);
                plugin.RequestSave();
            }

            ImGui.Spacing();

            if (TextControls(text))
                plugin.RequestSave();

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, $"Now it reads: {UnitTextFormatter.FormatHealth(format, current, max)}");
        });
    }

    // The two kinds of settings (a frame and the MP text) keep the same properties; the editor writes them through these.
    private static void SetPreset(IValueTextFormat f, HealthTextPreset v) { if (f is UnitFrameSettings u) u.HealthPreset = v; else if (f is MpTextSettings m) m.HealthPreset = v; }
    private static void SetTemplate(IValueTextFormat f, string v) { if (f is UnitFrameSettings u) u.CustomTemplate = v; else if (f is MpTextSettings m) m.CustomTemplate = v; }
    private static void SetSeparator(IValueTextFormat f, string v) { if (f is UnitFrameSettings u) u.Separator = v; else if (f is MpTextSettings m) m.Separator = v; }
    private static void SetPercentDecimals(IValueTextFormat f, int v) { if (f is UnitFrameSettings u) u.PercentDecimals = v; else if (f is MpTextSettings m) m.PercentDecimals = v; }
    private static void SetAbbreviationDecimals(IValueTextFormat f, int v) { if (f is UnitFrameSettings u) u.AbbreviationDecimals = v; else if (f is MpTextSettings m) m.AbbreviationDecimals = v; }
    private static void SetThousands(IValueTextFormat f, ThousandsSeparator v) { if (f is UnitFrameSettings u) u.Thousands = v; else if (f is MpTextSettings m) m.Thousands = v; }

    // ---------------------------------------------------------------- HP bar and MP bar of the player

    // The jobs where your HP bar is on. Everything is on by default, new jobs included.
    // The master switch of the Parameter bar page and the two tabs (HP bar, MP bar).
    private void DrawParameterBarSwitch(UnitFrameSettings settings)
    {
        Widgets.Card("##card_uf_parameter", 7, () =>
        {
            Widgets.SectionLabel("Parameter bar");

            var enabled = settings.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }

            ImGui.TextColored(Theme.TextMuted, "On: NahjaUI draws your HP and MP and the game's parameter bar is hidden.");
            ImGui.TextColored(Theme.TextMuted, "Off: the game's bar is shown and nothing of this page is drawn.");
            ImGui.Spacing();

            string[] tabs = { "HP bar", "MP bar" };
            for (var i = 0; i < tabs.Length; i++)
            {
                if (i > 0)
                    ImGui.SameLine();

                ImGui.PushStyleColor(ImGuiCol.Button, i == parameterTab ? Theme.Mix(Theme.Field, Theme.Accent, 0.85f) : Theme.Field);
                if (ImGui.Button(tabs[i]))
                    parameterTab = i;
                ImGui.PopStyleColor();
            }
        });
    }

    private void DrawHpJobs(UnitFrameSettings settings)
    {
        Widgets.Card("##card_uf_hp_jobs", 22, () =>
        {
            Widgets.SectionLabel("HP bar on each job");

            var showHp = settings.ShowHp;
            if (ImGui.Checkbox("Show the HP bar", ref showHp))
            {
                settings.ShowHp = showHp;
                plugin.RequestSave();
            }

            ImGui.TextColored(Theme.TextMuted, "Click a job to turn its HP bar on or off.");

            if (DrawJobToggles("hp", settings.IsHpOn, settings.SetHpOn))
                plugin.RequestSave();
        });
    }

    private void DrawMp(UnitFrameSettings settings)
    {
        var player = plugin.GameData.Current.Player;
        DrawMpBarCard(settings);
        DrawValueText("##card_uf_mp_text", "MP", settings.MpText, settings.MpText.Text, player?.Mp ?? 8400u, player?.MaxMp ?? 12000u);
        DrawMpJobs(settings);
    }

    private void DrawMpJobs(UnitFrameSettings settings)
    {
        Widgets.Card("##card_uf_mp_jobs", 23, () =>
        {
            Widgets.SectionLabel("MP bar on each job");

            var showMp = settings.ShowMp;
            if (ImGui.Checkbox("Show the MP bar", ref showMp))
            {
                settings.ShowMp = showMp;
                plugin.RequestSave();
            }

            ImGui.TextColored(Theme.TextMuted, "Click a job to turn its MP bar on or off. Crafters show CP and gatherers GP.");

            if (DrawJobToggles("mp", settings.IsMpOn, settings.SetMpOn))
                plugin.RequestSave();
        });
    }

    private void DrawMpBarCard(UnitFrameSettings settings)
    {
        Widgets.Card("##card_uf_mp", 9, () =>
        {
            Widgets.SectionLabel("MP bar");

            var boxLayout = plugin.UnitFrameSystem.MpElement.Layout;

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

            var rounding = settings.MpRoundingPx;
            if (ImGui.SliderInt("Corner rounding (px)", ref rounding, 0, 12))
            {
                settings.MpRoundingPx = rounding;
                plugin.RequestSave();
            }

            var color = settings.MpColor;
            if (ImGui.ColorEdit4("MP color", ref color, ImGuiColorEditFlags.NoAlpha))
            {
                settings.MpColor = new Vector4(color.X, color.Y, color.Z, 1f);
                plugin.RequestSave();
            }

            var background = settings.MpBackgroundColor;
            if (ImGui.ColorEdit4("Background color", ref background, ImGuiColorEditFlags.None))
            {
                settings.MpBackgroundColor = background;
                plugin.RequestSave();
            }

            var border = settings.MpBorderColor;
            if (ImGui.ColorEdit4("Border color", ref border, ImGuiColorEditFlags.NoAlpha))
            {
                settings.MpBorderColor = new Vector4(border.X, border.Y, border.Z, 1f);
                plugin.RequestSave();
            }
        });
    }

    // One button per job, by role, lit when the job is on. All / None for each role.
    private bool DrawJobToggles(string key, Func<uint, bool> isOn, Action<uint, bool> setOn)
    {
        var changed = false;
        var buttonWidth = 84f * ImGuiHelpers.GlobalScale;

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

            for (var i = 0; i < jobs.Length; i++)
            {
                var jobId = jobs[i];
                var on = isOn(jobId);

                ImGui.PushStyleColor(ImGuiCol.Button, on ? Theme.Mix(Theme.Field, Theme.Accent, 0.85f) : Theme.Field);
                var clicked = ImGui.Button($"{GameNames.JobAbbreviation(jobId)}  {(on ? "on" : "off")}##{key}_job_{jobId}", new Vector2(buttonWidth, 0f));
                ImGui.PopStyleColor();

                if (clicked)
                {
                    setOn(jobId, !on);
                    changed = true;
                }

                if ((i + 1) % 6 != 0 && i < jobs.Length - 1)
                    ImGui.SameLine();
            }

            ImGui.Spacing();
        }

        return changed;
    }

    // ---------------------------------------------------------------- helpers

    // The point of the bar, the two offsets, the size, the font and the colour of one text.
    internal static bool TextControls(FrameTextSettings text)
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

        var font = text.Font;
        if (EnumCombo("Text font", ref font, FontLabel))
        {
            text.Font = font;
            changed = true;
        }

        var color = text.Color;
        if (ImGui.ColorEdit4("Text color", ref color, ImGuiColorEditFlags.NoAlpha))
        {
            text.Color = new Vector4(color.X, color.Y, color.Z, 1f);
            changed = true;
        }

        return changed;
    }

    private static string HpColorModeLabel(HpColorMode mode) => mode switch
    {
        HpColorMode.Fixed => "One fixed color",
        HpColorMode.Role => "The color of the role (tank, healer, damage)",
        _ => mode.ToString()
    };


    private static string SeparatorLabel(string value)
    {
        foreach (var (separator, label) in Separators)
        {
            if (separator == value)
                return label;
        }

        return $"\"{value}\"";
    }

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

    private static string HealthPresetLabel(HealthTextPreset preset, string name) => preset switch
    {
        HealthTextPreset.CurrentAndPercent => $"{name} abbreviated and percentage (738K | 100%)",
        HealthTextPreset.CurrentFullAndPercent => $"{name} in full and percentage (738,000 | 100%)",
        HealthTextPreset.Current => $"{name} abbreviated (738K)",
        HealthTextPreset.CurrentFull => $"{name} in full (738,000)",
        HealthTextPreset.Percent => "Percentage only (100%)",
        HealthTextPreset.CurrentAndMax => $"{name} and maximum (738K / 738K)",
        HealthTextPreset.Deficit => $"Missing {name} (-12K)",
        HealthTextPreset.Custom => "Custom template",
        _ => preset.ToString()
    };

    private static string ThousandsLabel(ThousandsSeparator separator) => separator switch
    {
        ThousandsSeparator.Comma => "Comma (104,071)",
        ThousandsSeparator.Dot => "Dot (104.071)",
        ThousandsSeparator.Space => "Space (104 071)",
        ThousandsSeparator.None => "None (104071)",
        _ => separator.ToString()
    };

    internal static string FontLabel(OverlayFont font) => font switch
    {
        OverlayFont.GameNumbers => "Game numbers (Meidinger)",
        OverlayFont.GameUi => "Game interface (Axis)",
        OverlayFont.GameUiBold => "Game interface, bold (Axis)",
        OverlayFont.Condensed => "Condensed (Trump Gothic)",
        OverlayFont.NahjaDisplay => "NahjaUI display (Saira Condensed)",
        OverlayFont.NahjaDisplayBold => "NahjaUI numbers (Saira Condensed, bold)",
        OverlayFont.NahjaText => "NahjaUI text (Saira)",
        _ => font.ToString()
    };

    // A drop-down list for any enum. Returns true when the user picked a different value.
    internal static bool EnumCombo<T>(string label, ref T value, Func<T, string> labeler) where T : struct, Enum
    {
        var changed = false;

        if (ImGui.BeginCombo(label, labeler(value), ImGuiComboFlags.None))
        {
            foreach (var option in Enum.GetValues<T>())
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
