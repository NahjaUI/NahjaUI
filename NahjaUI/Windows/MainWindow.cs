using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Windows;

public class MainWindow : Window, IDisposable
{
    private static readonly string[] Pages = { "General", "Appearance", "Layout", "Debug" };

    private readonly Plugin plugin;
    private ThemeScope? themeScope;

    // Only used by the preview card on the Appearance page.
    private bool previewToggle = true;
    private int previewValue = 46;

    public MainWindow(Plugin plugin)
        : base("NahjaUI###NahjaUIMainWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;

        Size = new Vector2(860, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    // The theme must be pushed before the window begins and popped after it ends.
    public override void PreDraw()
    {
        themeScope?.Dispose();
        themeScope = new ThemeScope(plugin.Configuration);
    }

    public override void PostDraw()
    {
        themeScope?.Dispose();
        themeScope = null;
    }

    public override void OnClose()
    {
        plugin.SaveNow();
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        // Padding of buttons and fields goes here, not in PreDraw, so the title bar buttons do not overlap.
        using var contentStyle = new ContentStyleScope();

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.Sidebar))
        using (var sidebar = ImRaii.Child("##nui_sidebar", new Vector2(200f * scale, 0f), false))
        {
            if (sidebar.Success)
                DrawSidebar(scale);
        }

        ImGui.SameLine();

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.Background))
        using (var content = ImRaii.Child("##nui_content", Vector2.Zero, false))
        {
            if (content.Success)
                DrawContent(scale);
        }
    }

    // ---------------------------------------------------------------- sidebar

    private void DrawSidebar(float scale)
    {
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        ImGui.Indent(14f * scale);
        ImGui.TextColored(Theme.Accent, "NahjaUI");
        ImGui.TextColored(Theme.TextMuted, PlayerSummary());
        ImGui.Unindent(14f * scale);

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        ImGui.Indent(10f * scale);
        foreach (var page in Pages)
        {
            if (SidebarItem(page, plugin.Configuration.SelectedPage == page, scale))
            {
                plugin.Configuration.SelectedPage = page;
                plugin.RequestSave();
            }
        }
        ImGui.Unindent(10f * scale);

        // Version, anchored to the bottom of the sidebar.
        var bottomY = ImGui.GetWindowHeight() - ImGui.GetTextLineHeightWithSpacing() - 12f * scale;
        ImGui.SetCursorPosY(Math.Max(ImGui.GetCursorPosY(), bottomY));
        ImGui.Indent(14f * scale);
        ImGui.TextColored(Theme.TextMuted, $"v{typeof(Plugin).Assembly.GetName().Version}");
        ImGui.Unindent(14f * scale);
    }

    // One sidebar entry with a rounded highlight. Returns true when it was clicked.
    private static bool SidebarItem(string label, bool selected, float scale)
    {
        var width = ImGui.GetContentRegionAvail().X - 10f * scale;
        var height = 34f * scale;
        var pos = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton($"##nav_{label}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();

        var drawList = ImGui.GetWindowDrawList();

        if (selected || hovered)
        {
            var fill = Theme.WithAlpha(Theme.Accent, selected ? 0.28f : 0.14f);
            drawList.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.GetColorU32(fill), 8f * scale);
        }

        if (selected)
        {
            // Thin accent bar on the left edge.
            drawList.AddRectFilled(
                pos + new Vector2(0f, 8f * scale),
                pos + new Vector2(3f * scale, height - 8f * scale),
                ImGui.GetColorU32(Theme.Accent),
                2f * scale);
        }

        var textPos = new Vector2(pos.X + 16f * scale, pos.Y + (height - ImGui.GetTextLineHeight()) / 2f);
        drawList.AddText(textPos, ImGui.GetColorU32(selected ? Theme.Text : Theme.TextMuted), label);

        return clicked;
    }

    private static string PlayerSummary()
    {
        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded || !playerState.ClassJob.IsValid)
            return "Not logged in";

        return $"{playerState.ClassJob.Value.Abbreviation.ToString()}  Lv {playerState.Level}";
    }

    // ---------------------------------------------------------------- content

    private void DrawContent(float scale)
    {
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.Indent(16f * scale);

        var page = plugin.Configuration.SelectedPage;
        ImGui.TextColored(Theme.Accent, page);
        ImGui.TextColored(Theme.TextMuted, PageSubtitle(page));

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        switch (page)
        {
            case "Appearance":
                DrawAppearance();
                break;
            case "Layout":
                DrawLayout();
                break;
            case "Debug":
                DrawDebug();
                break;
            default:
                DrawGeneral();
                break;
        }

        ImGui.Unindent(16f * scale);
    }

    private static string PageSubtitle(string page) => page switch
    {
        "Appearance" => "Theme and look of the interface",
        "Layout" => "Position and size of every element",
        "Debug" => "Live game data and diagnostics",
        _ => "Overview and status"
    };

    private void DrawGeneral()
    {
        Widgets.Callout("##callout_open", "Use /nui to open or close this window at any time.", Theme.Info, 1);

        Widgets.Callout(
            "##callout_wip",
            "Early development build. Only two test boxes are drawn so far: auras arrive in H4.",
            Theme.Warning,
            2);

        Widgets.Card("##card_about", 5, () =>
        {
            ImGui.TextColored(Theme.Accent, "About NahjaUI");
            ImGui.Spacing();
            ImGui.TextWrapped("A modular interface suite for FINAL FANTASY XIV with an aura engine, so you only see the buffs, debuffs and cooldowns you choose.");
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "Current milestone: H2 - positioning and unlock mode.");
        });
    }

    private void DrawAppearance()
    {
        var config = plugin.Configuration;

        Widgets.Card("##card_theme", 5, () =>
        {
            Widgets.SectionLabel("Theme");

            var accent = config.AccentColor;
            if (ImGui.ColorEdit4("Accent color", ref accent, ImGuiColorEditFlags.NoAlpha))
            {
                config.AccentColor = new Vector4(accent.X, accent.Y, accent.Z, 1f);
                plugin.RequestSave();
            }

            ImGui.Spacing();
            if (ImGui.Button("Reset to default"))
            {
                config.AccentColor = Theme.DefaultAccent;
                plugin.RequestSave();
            }
        });

        Widgets.Card("##card_preview", 6, () =>
        {
            Widgets.SectionLabel("Preview");

            Widgets.PrimaryButton("Primary");
            ImGui.SameLine();
            ImGui.Button("Secondary");

            ImGui.Spacing();
            ImGui.Checkbox("Toggle", ref previewToggle);
            ImGui.SliderInt("Slider", ref previewValue, 0, 100);
        });
    }

    // ---------------------------------------------------------------- layout page

    private void DrawLayout()
    {
        var layout = plugin.Layout;
        var config = plugin.Configuration;

        Widgets.Callout(
            "##callout_unlock",
            "Unlock mode shows every element as a box you can drag. Select one to get X/Y fields next to it for exact positions. Positions are saved automatically.",
            Theme.Info,
            3);

        Widgets.Card("##card_positioning", 3, () =>
        {
            Widgets.SectionLabel("Positioning");

            if (Widgets.PrimaryButton("Unlock elements"))
                layout.UnlockMode = true;

            ImGui.SameLine();
            var showTest = config.ShowTestElements;
            if (ImGui.Checkbox("Show test elements", ref showTest))
            {
                config.ShowTestElements = showTest;
                plugin.RequestSave();
            }
        });

        Widgets.Card("##card_elements", layout.Elements.Count + 2, () =>
        {
            Widgets.SectionLabel("Elements");

            foreach (var element in layout.Elements)
            {
                if (ImGui.Selectable(element.Name, layout.SelectedId == element.Id))
                    layout.SelectedId = element.Id;
            }
        });

        var selected = layout.SelectedElement;
        if (selected is null)
        {
            ImGui.TextColored(Theme.TextMuted, "Select an element to edit its position and size.");
            return;
        }

        Widgets.Card("##card_editor", 14, () => DrawElementEditor(selected));
    }

    private void DrawElementEditor(LayoutElement element)
    {
        var data = element.Layout;

        Widgets.SectionLabel(element.Name);

        var screenAnchor = data.ScreenAnchor;
        if (AnchorCombo("Screen anchor", ref screenAnchor))
        {
            data.ScreenAnchor = screenAnchor;
            plugin.RequestSave();
        }

        var selfAnchor = data.SelfAnchor;
        if (AnchorCombo("Element anchor", ref selfAnchor))
        {
            data.SelfAnchor = selfAnchor;
            plugin.RequestSave();
        }

        ImGui.Spacing();

        // The +/- buttons next to each field move by 1 px (10 px with Ctrl held).
        var x = data.OffsetX;
        if (ImGui.InputInt("Offset X", ref x, 1, 10))
        {
            data.OffsetX = x;
            plugin.RequestSave();
        }

        var y = data.OffsetY;
        if (ImGui.InputInt("Offset Y", ref y, 1, 10))
        {
            data.OffsetY = y;
            plugin.RequestSave();
        }

        var width = data.Width;
        if (ImGui.InputInt("Width", ref width, 1, 10))
        {
            data.Width = Math.Max(1, width);
            plugin.RequestSave();
        }

        var height = data.Height;
        if (ImGui.InputInt("Height", ref height, 1, 10))
        {
            data.Height = Math.Max(1, height);
            plugin.RequestSave();
        }

        ImGui.Spacing();

        var locked = data.Locked;
        if (ImGui.Checkbox("Locked (cannot be moved in unlock mode)", ref locked))
        {
            data.Locked = locked;
            plugin.RequestSave();
        }

        if (ImGui.Button("Reset position and size"))
        {
            element.ResetToDefaults();
            plugin.RequestSave();
        }
    }

    private static bool AnchorCombo(string label, ref AnchorPoint value)
    {
        var changed = false;

        if (ImGui.BeginCombo(label, value.Label(), ImGuiComboFlags.None))
        {
            foreach (var point in AnchorPointExtensions.All)
            {
                if (ImGui.Selectable(point.Label(), point == value))
                {
                    value = point;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    // ---------------------------------------------------------------- debug page

    private static void DrawDebug()
    {
        Widgets.Card("##card_player", 6, () =>
        {
            Widgets.SectionLabel("Player state");

            var playerState = Plugin.PlayerState;
            if (!playerState.IsLoaded)
            {
                ImGui.Text("Not logged in.");
                return;
            }

            if (!playerState.ClassJob.IsValid)
            {
                ImGui.Text("Current job is not valid.");
                return;
            }

            ImGui.Text($"Job: {playerState.ClassJob.Value.Abbreviation.ToString()} [Level {playerState.Level}]");

            var territoryId = Plugin.ClientState.TerritoryType;
            if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
                ImGui.Text($"Location: {territoryRow.PlaceName.Value.Name.ToString()}");
            else
                ImGui.Text("Invalid territory.");
        });

        Widgets.Card("##card_interface", 4, () =>
        {
            Widgets.SectionLabel("Interface");
            ImGui.Text($"UI scale: {ImGuiHelpers.GlobalScale:0.00}");
        });
    }
}
