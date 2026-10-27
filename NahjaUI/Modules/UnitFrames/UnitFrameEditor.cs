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
    // The jobs of the mana list, grouped as in the game: tanks, healers, melee, ranged, casters.
    private static readonly uint[] Jobs = { 19, 21, 32, 37, 24, 28, 33, 40, 20, 22, 30, 34, 39, 41, 23, 31, 38, 25, 27, 35, 42, 36 };

    private const int JobsPerRow = 6;

    private static readonly (string Value, string Label)[] Separators =
    {
        (" | ", "Vertical bar ( | )"),
        (" / ", "Slash ( / )"),
        (" - ", "Dash ( - )"),
        (" \u00B7 ", "Dot ( \u00B7 )"),
        (" ", "Space")
    };

    private static readonly string[] FrameNames = { "Player", "Target", "Target of target", "Focus" };

    // The picker has two more buttons after the frames: the party frames, and the class colors shared by all of them.
    private const int PartyPage = 4;
    private const int ClassColorsPage = 5;
    private static readonly string[] PickerNames = { "Player", "Target", "Target of target", "Focus", "Party", "Class colors" };

    private readonly Plugin plugin;

    // Which frame the cards below edit (an index into FrameNames).
    private int selectedFrame;

    // The name search of the list of the buffs and the one of the debuffs of the party.
    private sealed class StatusSearchState
    {
        public string Text = string.Empty;
        public string Applied = string.Empty;
        public IReadOnlyList<StatusSearchResult> Results = Array.Empty<StatusSearchResult>();
    }

    private readonly StatusSearchState buffSearch = new();
    private readonly StatusSearchState debuffSearch = new();
    private readonly StatusSearchState frameBuffSearch = new();
    private readonly StatusSearchState frameDebuffSearch = new();

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

        ImGui.TextColored(Theme.TextMuted, "Move the frames in unlock mode (Layout page). Texts are attached to a point of the bar.");
        ImGui.Spacing();

        DrawFramePicker();

        var frames = plugin.Configuration.UnitFrames;
        var system = plugin.UnitFrameSystem;

        // The class colors are the same for every frame, so they have their own page instead of a card in each frame.
        if (selectedFrame == ClassColorsPage)
        {
            DrawClassColors(frames.Player);
            return;
        }

        // The party frames: the same cards as the other frames (their look), then the ones that are only about the party.
        if (selectedFrame == PartyPage)
        {
            plugin.PartyFrameSystem.PreviewRequested = true;

            var party = frames.Party;
            DrawGeneral(party.Look, plugin.PartyFrameSystem.PartyElement, true);
            DrawPartyOptions(party);
            DrawPartyAuras("##card_pf_buffs", "Buffs", party.Buffs, true, buffSearch);
            DrawPartyAuras("##card_pf_debuffs", "Debuffs", party.Debuffs, false, debuffSearch);
            DrawName(party.Look);
            DrawHealth(party.Look);
            return;
        }

        var (settings, element) = selectedFrame switch
        {
            1 => (frames.Target, system.TargetElement),
            2 => (frames.TargetOfTarget, system.TargetOfTargetElement),
            3 => (frames.Focus, system.FocusElement),
            _ => (frames.Player, system.PlayerElement)
        };

        var isPlayer = selectedFrame == 0;

        DrawGeneral(settings, element, isPlayer);

        // Buffs and debuffs next to the bar (not on the target of target).
        if (selectedFrame != 2)
        {
            DrawFrameStatusOptions(settings);
            DrawPartyAuras($"##card_uf{selectedFrame}_buffs", "Buffs", settings.Buffs, true, frameBuffSearch);
            DrawPartyAuras($"##card_uf{selectedFrame}_debuffs", "Debuffs", settings.Debuffs, false, frameDebuffSearch);
        }

        DrawName(settings);
        DrawHealth(settings);

        // Only your own frame has a mana bar.
        if (isPlayer)
            DrawMana(settings);
    }

    // A row of buttons, one per frame; the selected one is lit with the accent colour.
    private void DrawFramePicker()
    {
        for (var i = 0; i < PickerNames.Length; i++)
        {
            var selected = i == selectedFrame;

            ImGui.PushStyleColor(ImGuiCol.Button, selected ? Theme.Mix(Theme.Field, Theme.Accent, 0.85f) : Theme.Field);
            if (ImGui.Button(PickerNames[i]))
                selectedFrame = i;
            ImGui.PopStyleColor();

            if (i < PickerNames.Length - 1)
                ImGui.SameLine();
        }

        ImGui.Spacing();
    }

    // ---------------------------------------------------------------- cards

    private void DrawGeneral(UnitFrameSettings settings, LayoutElement element, bool isPlayer)
    {
        Widgets.Card("##card_uf_general", 25, () =>
        {
            Widgets.SectionLabel(selectedFrame == PartyPage ? "Party frames (the first bar; the others follow it downwards)" : $"{FrameNames[selectedFrame]} frame");

            var enabled = settings.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }

            var clickable = settings.Clickable;
            if (ImGui.Checkbox("Clickable: left click targets, right click opens a menu", ref clickable))
            {
                settings.Clickable = clickable;
                plugin.RequestSave();
            }

            // The game shows the target of target inside its target bar, so it has no bar of its own to hide.
            var nativeLabel = selectedFrame switch
            {
                0 => "Hide the game's life and mana bar",
                1 => "Hide the game's target bar (and its target of target)",
                3 => "Hide the game's focus bar",
                PartyPage => "Hide the game's party list",
                _ => null
            };

            if (nativeLabel is not null)
            {
                var hideNative = settings.HideNative;
                if (ImGui.Checkbox(nativeLabel, ref hideNative))
                {
                    settings.HideNative = hideNative;
                    plugin.RequestSave();
                }

                if (selectedFrame == 1)
                    ImGui.TextColored(Theme.TextMuted, "Needs the target info set to separate parts in the game's HUD layout.");
            }
            else
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

            var colorMode = settings.LifeColorMode;
            if (EnumCombo("Life color", ref colorMode, LifeColorModeLabel))
            {
                settings.LifeColorMode = colorMode;
                plugin.RequestSave();
            }

            var life = settings.FriendlyColor;
            if (ImGui.ColorEdit4("Fixed life color", ref life, ImGuiColorEditFlags.NoAlpha))
            {
                settings.FriendlyColor = new Vector4(life.X, life.Y, life.Z, 1f);
                plugin.RequestSave();
            }

            // Enemies always use their own colour; the player's frame never shows one.
            if (!isPlayer)
            {
                var hostile = settings.HostileColor;
                if (ImGui.ColorEdit4("Enemy life color", ref hostile, ImGuiColorEditFlags.NoAlpha))
                {
                    settings.HostileColor = new Vector4(hostile.X, hostile.Y, hostile.Z, 1f);
                    plugin.RequestSave();
                }
            }

            // Pets, minions and objects (furniture, chests, aetherytes...) have their own colours.
            if (!isPlayer)
            {
                var pet = settings.PetColor;
                if (ImGui.ColorEdit4("Pet / minion life color", ref pet, ImGuiColorEditFlags.NoAlpha))
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
            if (ImGui.Checkbox("Show shields on the life bar", ref showShield))
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

    // What is only about the party: space between bars, mana bar, and how the targeted member is marked.
    private void DrawPartyOptions(PartyFrameSettings party)
    {
        Widgets.Card("##card_pf_options", 24, () =>
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
                if (ImGui.SliderInt("Icon size (px)", ref iconSize, 8, 40))
                {
                    party.JobIconSizePx = iconSize;
                    plugin.RequestSave();
                }
            }

            ImGui.Spacing();

            var manaMode = party.ManaMode;
            if (EnumCombo("Mana bar", ref manaMode, PartyManaLabel))
            {
                party.ManaMode = manaMode;
                plugin.RequestSave();
            }

            var manaHeight = party.Look.ManaHeightPx;
            if (ImGui.SliderInt("Mana bar height (px)", ref manaHeight, 2, 12))
            {
                party.Look.ManaHeightPx = manaHeight;
                plugin.RequestSave();
            }

            var manaColor = party.Look.ManaColor;
            if (ImGui.ColorEdit4("Mana color", ref manaColor, ImGuiColorEditFlags.NoAlpha))
            {
                party.Look.ManaColor = new Vector4(manaColor.X, manaColor.Y, manaColor.Z, 1f);
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var ring = party.SelectionRing;
            if (ImGui.Checkbox("Ring around the member I have targeted", ref ring))
            {
                party.SelectionRing = ring;
                plugin.RequestSave();
            }

            var arrow = party.SelectionArrow;
            if (ImGui.Checkbox("Arrow next to the member I have targeted", ref arrow))
            {
                party.SelectionArrow = arrow;
                plugin.RequestSave();
            }

            var selection = party.SelectionColor;
            if (ImGui.ColorEdit4("Targeted member color", ref selection, ImGuiColorEditFlags.NoAlpha))
            {
                party.SelectionColor = new Vector4(selection.X, selection.Y, selection.Z, 1f);
                plugin.RequestSave();
            }

            ImGui.TextColored(Theme.TextMuted, "With no party, sample members are shown while this page is open or in unlock mode.");
        });
    }

    // Options shared by the two rows of a frame.
    private void DrawFrameStatusOptions(UnitFrameSettings settings)
    {
        if (selectedFrame != 1)
            return;

        Widgets.Card($"##card_uf{selectedFrame}_native_status", 3, () =>
        {
            Widgets.SectionLabel("Buffs and debuffs of the game");

            var hide = settings.HideNativeStatuses;
            if (ImGui.Checkbox("Hide the game's buffs and debuffs of the target while mine are on", ref hide))
            {
                settings.HideNativeStatuses = hide;
                plugin.RequestSave();
            }
        });
    }

    // One row of icons of the party (buffs or debuffs): what it shows, where it goes, how big the icons are.
    private void DrawPartyAuras(string cardId, string title, PartyAuraSettings row, bool isBuff, StatusSearchState search)
    {
        var kind = isBuff ? "buff" : "debuff";

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

            var offsetY = row.OffsetY;
            if (ImGui.InputInt("Offset Y (px)", ref offsetY, 1, 5))
            {
                row.OffsetY = Math.Clamp(offsetY, -500, 500);
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

            var timerY = row.TimerOffsetY;
            if (ImGui.InputInt("Time offset Y (px)", ref timerY, 1, 5))
            {
                row.TimerOffsetY = timerY;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var showStacks = row.ShowStacks;
            if (ImGui.Checkbox("Show the stack count", ref showStacks))
            {
                row.ShowStacks = showStacks;
                plugin.RequestSave();
            }

            // 0 = automatic (three quarters of the time size), as in the auras.
            var stackSize = row.StackTextSize;
            if (ImGui.SliderInt("Stack size (px, 0 = automatic)", ref stackSize, 0, 24))
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

            var stacksY = row.StacksOffsetY;
            if (ImGui.InputInt("Stack offset Y (px)", ref stacksY, 1, 5))
            {
                row.StacksOffsetY = stacksY;
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
                DrawPartyStatusList(row, isBuff, kind, search);
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
    private void DrawPartyStatusList(PartyAuraSettings row, bool isBuff, string kind, StatusSearchState search)
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

        var wanted = (byte)(isBuff ? 1 : 2);
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
                1 => "buff",
                2 => "debuff",
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
    private static bool SizeCombo(string label, ref int value)
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

    private static string PartyManaLabel(PartyManaMode mode) => mode switch
    {
        PartyManaMode.Off => "No one",
        PartyManaMode.Healers => "Healers",
        PartyManaMode.HealersAndRaisers => "Healers and jobs that can raise",
        _ => "Everyone"
    };

    // One swatch per job. What is changed here is kept apart from the built-in colours, so it can always be reset.
    private void DrawClassColors(UnitFrameSettings settings)
    {
        Widgets.Card("##card_uf_classcolors", 12, () =>
        {
            Widgets.SectionLabel("Class colors");
            ImGui.TextColored(Theme.TextMuted, "The same for every frame (player, target, target of target and focus). Used when a frame's life color is set to the class color.");
            ImGui.TextColored(Theme.TextMuted, "Click a swatch to change it.");
            ImGui.Spacing();

            var overrides = plugin.Configuration.UnitFrames.JobColorOverrides;

            for (var i = 0; i < JobColors.Order.Length; i++)
            {
                var jobId = JobColors.Order[i];
                var color = JobColors.Get(overrides, jobId, settings.FriendlyColor);

                var flags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoAlpha;
                if (ImGui.ColorEdit4($"##class_color_{jobId}", ref color, flags))
                {
                    overrides[jobId] = new Vector4(color.X, color.Y, color.Z, 1f);
                    plugin.RequestSave();
                }

                ImGui.SameLine();
                ImGui.Text(GameNames.JobAbbreviation(jobId));

                if ((i + 1) % JobsPerRow != 0 && i < JobColors.Order.Length - 1)
                    ImGui.SameLine();
            }

            ImGui.Spacing();
            if (ImGui.Button("Reset all class colors"))
            {
                overrides.Clear();
                plugin.RequestSave();
            }
        });
    }

    private void DrawName(UnitFrameSettings settings)
    {
        Widgets.Card("##card_uf_name", 14, () =>
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

            var onlyPlayers = settings.NameFormatOnlyPlayers;
            if (ImGui.Checkbox("Only shorten the names of players", ref onlyPlayers))
            {
                settings.NameFormatOnlyPlayers = onlyPlayers;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            if (TextControls(settings.Name))
                plugin.RequestSave();
        });
    }

    private void DrawHealth(UnitFrameSettings settings)
    {
        var custom = settings.HealthPreset == HealthTextPreset.Custom;

        Widgets.Card("##card_uf_health", custom ? 22 : 19, () =>
        {
            Widgets.SectionLabel("Life text");

            var enabled = settings.Health.Enabled;
            if (ImGui.Checkbox("Show the life text", ref enabled))
            {
                settings.Health.Enabled = enabled;
                plugin.RequestSave();
            }

            var preset = settings.HealthPreset;
            if (EnumCombo("What it says", ref preset, HealthPresetLabel))
            {
                settings.HealthPreset = preset;
                plugin.RequestSave();
            }

            if (custom)
            {
                var template = settings.CustomTemplate ?? string.Empty;
                if (ImGui.InputText("Template", ref template, 64))
                {
                    settings.CustomTemplate = template;
                    plugin.RequestSave();
                }

                ImGui.TextColored(Theme.TextMuted, "{cur} {max} {def} in full, {curk} {maxk} {defk} abbreviated, {pct} percentage.");
                ImGui.TextColored(Theme.TextMuted, "def is the missing life.");
            }

            // The separator only matters for the presets that show two things.
            var separatorLabel = SeparatorLabel(settings.Separator);
            if (ImGui.BeginCombo("Separator", separatorLabel, ImGuiComboFlags.None))
            {
                foreach (var (value, label) in Separators)
                {
                    if (ImGui.Selectable(label, value == settings.Separator))
                    {
                        settings.Separator = value;
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }

            var percentDecimals = settings.PercentDecimals;
            if (ImGui.SliderInt("Percentage decimals", ref percentDecimals, 0, 2))
            {
                settings.PercentDecimals = percentDecimals;
                plugin.RequestSave();
            }

            var abbreviationDecimals = settings.AbbreviationDecimals;
            if (ImGui.SliderInt("Decimals of 738K", ref abbreviationDecimals, 0, 2))
            {
                settings.AbbreviationDecimals = abbreviationDecimals;
                plugin.RequestSave();
            }

            var thousands = settings.Thousands;
            if (EnumCombo("Thousands separator", ref thousands, ThousandsLabel))
            {
                settings.Thousands = thousands;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            if (TextControls(settings.Health))
                plugin.RequestSave();

            // What it looks like right now, with the real life of this frame's unit (or a sample one).
            var snapshot = plugin.GameData.Current;
            var unit = selectedFrame switch
            {
                1 => snapshot.Target,
                2 => snapshot.TargetOfTarget,
                3 => snapshot.Focus,
                _ => snapshot.Player
            };

            var hp = unit?.Hp ?? 104071u;
            var maxHp = unit?.MaxHp ?? 140000u;

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, $"Now it reads: {UnitTextFormatter.FormatHealth(settings, hp, maxHp)}");
        });
    }

    private void DrawMana(UnitFrameSettings settings)
    {
        Widgets.Card("##card_uf_mana", 27, () =>
        {
            Widgets.SectionLabel("Mana bar");

            var showMana = settings.ShowMana;
            if (ImGui.Checkbox("Show the mana bar", ref showMana))
            {
                settings.ShowMana = showMana;
                plugin.RequestSave();
            }

            // When it goes under the life bar it is as wide as it.
            var height = settings.ManaHeightPx;
            if (ImGui.SliderInt("Height under the life bar (px)", ref height, 2, 24))
            {
                settings.ManaHeightPx = height;
                plugin.RequestSave();
            }

            var gap = settings.ManaGapPx;
            if (ImGui.SliderInt("Space under the life bar (px)", ref gap, 0, 12))
            {
                settings.ManaGapPx = gap;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            // In its own box it has its own size, like its position (moved in unlock mode).
            var boxLayout = plugin.UnitFrameSystem.ManaElement.Layout;

            var boxWidth = boxLayout.Width;
            if (ImGui.InputInt("Own box width (px)", ref boxWidth, 2, 10))
            {
                boxLayout.Width = Math.Clamp(boxWidth, 1, 4000);
                plugin.RequestSave();
            }

            var boxHeight = boxLayout.Height;
            if (ImGui.InputInt("Own box height (px)", ref boxHeight, 2, 10))
            {
                boxLayout.Height = Math.Clamp(boxHeight, 1, 1000);
                plugin.RequestSave();
            }

            var color = settings.ManaColor;
            if (ImGui.ColorEdit4("Mana color", ref color, ImGuiColorEditFlags.NoAlpha))
            {
                settings.ManaColor = new Vector4(color.X, color.Y, color.Z, 1f);
                plugin.RequestSave();
            }

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "Choose where the mana bar goes on each job. Click a job to change it:");
            ImGui.TextColored(Theme.TextMuted, "off, under the life bar, or in its own box (moved in unlock mode).");

            // A shortcut for the job being played.
            if (plugin.GameData.Current.Player?.JobId is { } jobNow)
            {
                var current = settings.GetManaPlacement(jobNow);
                if (EnumCombo($"Mana on my current job ({GameNames.JobAbbreviation(jobNow)})", ref current, PlacementLabel))
                {
                    settings.SetManaPlacement(jobNow, current);
                    plugin.RequestSave();
                }
            }

            ImGui.Spacing();

            // One button per job; each click goes to the next place: off, under the life bar, own box.
            var buttonWidth = 112f * ImGuiHelpers.GlobalScale;

            for (var i = 0; i < Jobs.Length; i++)
            {
                var jobId = Jobs[i];
                var placement = settings.GetManaPlacement(jobId);

                var tint = placement switch
                {
                    ManaPlacement.UnderLifeBar => Theme.Mix(Theme.Field, Theme.Accent, 0.45f),
                    ManaPlacement.OwnBox => Theme.Mix(Theme.Field, Theme.Accent, 0.85f),
                    _ => Theme.Field
                };

                ImGui.PushStyleColor(ImGuiCol.Button, tint);
                var clicked = ImGui.Button($"{GameNames.JobAbbreviation(jobId)}  {PlacementShort(placement)}##mana_job_{jobId}", new Vector2(buttonWidth, 0f));
                ImGui.PopStyleColor();

                if (clicked)
                {
                    var next = placement switch
                    {
                        ManaPlacement.Off => ManaPlacement.UnderLifeBar,
                        ManaPlacement.UnderLifeBar => ManaPlacement.OwnBox,
                        _ => ManaPlacement.Off
                    };

                    settings.SetManaPlacement(jobId, next);
                    plugin.RequestSave();
                }

                if ((i + 1) % 4 != 0 && i < Jobs.Length - 1)
                    ImGui.SameLine();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    // The point of the bar, the two offsets, the size, the font and the colour of one text.
    private static bool TextControls(FrameTextSettings text)
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

        var y = text.OffsetY;
        if (ImGui.InputInt("Offset Y (px)", ref y, 1, 5))
        {
            text.OffsetY = y;
            changed = true;
        }

        ImGui.TextColored(Theme.TextMuted, "The point of the bar the text is attached to (the text's own same point goes there).");

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

    private static string LifeColorModeLabel(LifeColorMode mode) => mode switch
    {
        LifeColorMode.Fixed => "One fixed color",
        LifeColorMode.Class => "The color of the class (job)",
        _ => mode.ToString()
    };

    private static string PlacementLabel(ManaPlacement placement) => placement switch
    {
        ManaPlacement.Off => "No mana bar",
        ManaPlacement.UnderLifeBar => "Under the life bar",
        ManaPlacement.OwnBox => "In its own box",
        _ => placement.ToString()
    };

    private static string PlacementShort(ManaPlacement placement) => placement switch
    {
        ManaPlacement.Off => "off",
        ManaPlacement.UnderLifeBar => "under",
        ManaPlacement.OwnBox => "own box",
        _ => placement.ToString()
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

    private static string HealthPresetLabel(HealthTextPreset preset) => preset switch
    {
        HealthTextPreset.CurrentAndPercent => "Life abbreviated and percentage (738K | 100%)",
        HealthTextPreset.CurrentFullAndPercent => "Life in full and percentage (738,000 | 100%)",
        HealthTextPreset.Current => "Life abbreviated (738K)",
        HealthTextPreset.CurrentFull => "Life in full (738,000)",
        HealthTextPreset.Percent => "Percentage only (100%)",
        HealthTextPreset.CurrentAndMax => "Life and maximum (738K / 738K)",
        HealthTextPreset.Deficit => "Missing life (-12K)",
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

    private static string FontLabel(OverlayFont font) => font switch
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
    private static bool EnumCombo<T>(string label, ref T value, Func<T, string> labeler) where T : struct, Enum
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
