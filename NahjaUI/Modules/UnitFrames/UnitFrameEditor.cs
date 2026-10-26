using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

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

    // The picker has one more button after the frames: the class colors, shared by all of them.
    private const int ClassColorsPage = 4;
    private static readonly string[] PickerNames = { "Player", "Target", "Target of target", "Focus", "Class colors" };

    private readonly Plugin plugin;

    // Which frame the cards below edit (an index into FrameNames).
    private int selectedFrame;

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

        var (settings, element) = selectedFrame switch
        {
            1 => (frames.Target, system.TargetElement),
            2 => (frames.TargetOfTarget, system.TargetOfTargetElement),
            3 => (frames.Focus, system.FocusElement),
            _ => (frames.Player, system.PlayerElement)
        };

        var isPlayer = selectedFrame == 0;

        DrawGeneral(settings, element, isPlayer);
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
        Widgets.Card("##card_uf_general", 22, () =>
        {
            Widgets.SectionLabel($"{FrameNames[selectedFrame]} frame");

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
