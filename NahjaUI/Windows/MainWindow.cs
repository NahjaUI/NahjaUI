using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using NahjaUI.Core;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Profiles;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Windows;

public class MainWindow : Window, IDisposable
{
    private static readonly string[] Pages = { "General", "Trackers", "HUD bars", "Progress bars", "Job gauges", "Status effects", "Nameplates", "Enmity list", "Others", "Filters", "Tooltip", "Appearance", "Layout", "Profiles" };

    private readonly Plugin plugin;
    private readonly ProfilesPage profilesPage;
    private ThemeScope? themeScope;

    // Set by the Appearance page: put the window back in the centre of the screen on the next frame.
    private bool centerRequested;
    private bool sizeResetPending;
    // Wide enough for the two columns of the Trackers page (list and editor): the page needs 720 px (times the UI scale) for its
    // content plus the sidebar and margins. Dalamud multiplies the size of a window by the UI scale itself, so it is NOT scaled here.
    private static readonly Vector2 DefaultSize = new(1040f, 680f);

    // Where the window must appear when it is opened from the small bar (its top right corner), and the corner as of the last frame.
    private Vector2 wantedTopRight;
    private int placeFrames;
    private Vector2 lastSize;

    public MainWindow(Plugin plugin)
        // No title bar of the game's window system: the window draws its own (icon, name, arrow and cross), see DrawTitleBar.
        // It is moved by that bar, so the window itself cannot be dragged from anywhere else.
        : base("NahjaUI###NahjaUIMainWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove)
    {
        this.plugin = plugin;
        profilesPage = new ProfilesPage(plugin);

        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;

        Size = DefaultSize;
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    /// <summary>The top right corner of the window, in screen pixels, as of the last frame.</summary>
    public Vector2 TopRight { get; private set; }

    /// <summary>Opens the window with its top right corner at the given point (it is kept inside the screen).</summary>
    public void ShowAt(Vector2 topRight)
    {
        wantedTopRight = topRight;
        placeFrames = 3;
        IsOpen = true;
    }

    // The theme must be pushed before the window begins and popped after it ends.
    public override void PreDraw()
    {
        themeScope?.Dispose();
        themeScope = new ThemeScope(plugin.Configuration);

        // The first time (no saved position) and when asked: the window goes to the centre of the screen.
        var mainViewport = ImGui.GetMainViewport();
        var viewportCenter = mainViewport.Pos + mainViewport.Size / 2f;
        // A saved size that does not fit on the screen (another monitor, another UI scale, an old bug) would leave the window
        // impossible to move or close: it is reset by itself to the natural size, in the centre.
        if (lastSize.X > mainViewport.Size.X || lastSize.Y > mainViewport.Size.Y)
        {
            lastSize = Vector2.Zero;
            centerRequested = true;
        }

        if (centerRequested)
        {
            // Reset: back to the centre of the screen and to the natural size.
            centerRequested = false;
            sizeResetPending = true;
            Size = DefaultSize;
            SizeCondition = ImGuiCond.Always;
            ImGui.SetNextWindowPos(viewportCenter, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        }
        else
        {
            if (sizeResetPending)
            {
                // The size was forced for one frame; from now on the user can resize it again.
                sizeResetPending = false;
                SizeCondition = ImGuiCond.FirstUseEver;
            }

            ImGui.SetNextWindowPos(viewportCenter, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        }

        // Coming back from the small bar: the window opens where the bar was.
        if (placeFrames > 0 && lastSize.X > 0f)
        {
            placeFrames--;

            var viewport = ImGui.GetMainViewport();
            var maxX = Math.Max(viewport.Pos.X, viewport.Pos.X + viewport.Size.X - lastSize.X);
            var maxY = Math.Max(viewport.Pos.Y, viewport.Pos.Y + viewport.Size.Y - lastSize.Y);

            var x = Math.Clamp(wantedTopRight.X - lastSize.X, viewport.Pos.X, maxX);
            var y = Math.Clamp(wantedTopRight.Y, viewport.Pos.Y, maxY);

            ImGui.SetNextWindowPos(new Vector2(MathF.Round(x), MathF.Round(y)), ImGuiCond.Always);
        }
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

        // Remember where the window is, for the small bar to appear in the same corner.
        var windowPos = ImGui.GetWindowPos();
        lastSize = ImGui.GetWindowSize();
        TopRight = new Vector2(windowPos.X + lastSize.X, windowPos.Y);

        // The title bar: icon and name at the left, arrow (minimize) and cross (close) at the right. It is also the handle to move the window.
        var titleHeight = DrawTitleBar(scale, windowPos);
        if (titleHeight < 0f)
            return;

        // The content starts under the title bar.
        ImGui.SetCursorPos(new Vector2(ImGui.GetStyle().WindowPadding.X, titleHeight));

        // Padding of buttons and fields goes here, not in PreDraw, so the title bar buttons do not overlap.
        using var contentStyle = new ContentStyleScope();

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.Sidebar))
        using (var sidebar = ImRaii.Child("##nui_sidebar", new Vector2(200f * scale, 0f), false))
        {
            if (sidebar.Success)
                DrawSidebar(scale);
        }

        ImGui.SameLine();

        // The pages with tabs do not scroll as a whole: each tab scrolls its own body, so the tabs stay in place.
        var tabbedPage = plugin.Configuration.SelectedPage is "HUD bars" or "Progress bars" or "Nameplates" or "Status effects" or "Enmity list" or "Others" or "Layout";
        var contentFlags = tabbedPage ? ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.Background))
        using (var content = ImRaii.Child("##nui_content", Vector2.Zero, false, contentFlags))
        {
            if (content.Success)
                DrawContent(scale);
        }
    }

    // Draws the title bar and handles it. Returns its height, or -1 when the window was minimized (nothing else must be drawn).
    private float DrawTitleBar(float scale, Vector2 windowPos)
    {
        var drawList = ImGui.GetWindowDrawList();
        var width = ImGui.GetWindowSize().X;
        var height = MathF.Round(MathF.Max(34f * scale, ImGui.GetTextLineHeight() + 14f * scale));
        var rounding = MathF.Min(MathF.Round(Theme.WindowRounding * scale), height / 2f + 12f * scale);

        drawList.AddRectFilled(windowPos, windowPos + new Vector2(width, height), ImGui.GetColorU32(Theme.Sidebar), rounding, ImDrawFlags.RoundCornersTop);

        var padding = MathF.Round(12f * scale);
        var gap = MathF.Round(4f * scale);
        var button = TitleButtons.Size;
        var centerY = windowPos.Y + height / 2f;

        // The two buttons at the right.
        var closePos = new Vector2(MathF.Round(windowPos.X + width - padding - button), MathF.Round(centerY - button / 2f));
        var minimizePos = new Vector2(closePos.X - gap - button, closePos.Y);

        // The icon and the name at the left.
        var markSize = MathF.Round(22f * scale);
        var markPos = new Vector2(MathF.Round(windowPos.X + padding), MathF.Round(centerY - markSize / 2f));
        NahjaMark.Draw(drawList, markPos, markSize);

        var textPos = new Vector2(MathF.Round(markPos.X + markSize + 8f * scale), MathF.Round(centerY - ImGui.GetTextLineHeight() / 2f));
        drawList.AddText(textPos, ImGui.GetColorU32(Theme.Text), "NahjaUI");

        // The rest of the bar moves the window; a double click minimizes it.
        ImGui.SetCursorScreenPos(windowPos + new Vector2(1f, 1f));
        ImGui.InvisibleButton("##nui_title_drag", new Vector2(minimizePos.X - gap - windowPos.X, height - 2f));

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            plugin.MinimizeMainWindow();
            return -1f;
        }

        // Right click on the bar: the same "centre the window" as the Appearance page.
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup("##nui_title_menu");

        if (ImGui.BeginPopup("##nui_title_menu"))
        {
            if (ImGui.Selectable("Reset window"))
                centerRequested = true;

            ImGui.EndPopup();
        }

        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (delta != Vector2.Zero)
            {
                // Never lose the bar: at least a part of it always stays on the screen.
                var viewport = ImGui.GetMainViewport();
                var size = ImGui.GetWindowSize();
                var wanted = windowPos + delta;
                var x = Math.Clamp(wanted.X, viewport.Pos.X - size.X + 80f * scale, viewport.Pos.X + viewport.Size.X - 80f * scale);
                var y = Math.Clamp(wanted.Y, viewport.Pos.Y, viewport.Pos.Y + viewport.Size.Y - height);
                ImGui.SetWindowPos(new Vector2(MathF.Round(x), MathF.Round(y)));
            }
        }

        if (TitleButtons.Draw("##nui_title_minimize", minimizePos, TitleButtons.Glyph.Arrow, "Minimize to a small bar"))
        {
            plugin.MinimizeMainWindow();
            return -1f;
        }

        if (TitleButtons.Draw("##nui_title_close", closePos, TitleButtons.Glyph.Close, "Close"))
            IsOpen = false;

        return height;
    }

    // ---------------------------------------------------------------- sidebar

    private void DrawSidebar(float scale)
    {
        ImGui.Dummy(new Vector2(0f, 10f * scale));

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

    // ---------------------------------------------------------------- content

    private void DrawContent(float scale)
    {
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.Indent(16f * scale);

        var page = plugin.Configuration.SelectedPage;

        switch (page)
        {
            case "Trackers":
                plugin.TrackerEditorPage.Draw();
                break;
            case "HUD bars":
                plugin.HudBarEditorPage.Draw();
                break;
            case "Status effects":
                plugin.StatusEffectsEditorPage.Draw();
                break;
            case "Filters":
                plugin.FiltersEditorPage.Draw();
                break;
            case "Nameplates":
                plugin.NamePlateEditorPage.Draw();
                break;
            case "Enmity list":
                plugin.EnmityListEditorPage.Draw();
                break;
            case "Tooltip":
                plugin.TooltipEditorPage.Draw();
                break;
            case "Progress bars":
                plugin.ProgressBarEditorPage.Draw();
                break;
            case "Job gauges":
                plugin.JobGaugeEditorPage.Draw();
                break;
            case "Others":
                if (ImGui.BeginTabBar("##others_tabs"))
                {
                    if (ImGui.BeginTabItem("Limit Break"))
                    {
                        using (Widgets.TabBody("##others_body_lb"))
                            plugin.JobGaugeEditorPage.DrawLimitBreakPage();

                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Limit Break (PvP)"))
                    {
                        using (Widgets.TabBody("##others_body_lbpvp"))
                            plugin.JobGaugeEditorPage.DrawLimitBreakPvpPage();

                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Frontline gauge"))
                    {
                        using (Widgets.TabBody("##others_body_fl"))
                            plugin.JobGaugeEditorPage.DrawFrontlinePage();

                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                break;
            case "Appearance":
                DrawAppearance();
                break;
            case "Profiles":
                profilesPage.Draw();
                break;
            case "Layout":
                DrawLayout();
                break;
            default:
                DrawGeneral();
                break;
        }

        ImGui.Unindent(16f * scale);
    }

    private static string PageSubtitle(string page) => page switch
    {
        "Trackers" => "Icons for the enhancements, enfeeblements and ability cooldowns you want to watch",
        "HUD bars" => "Player, target, focus and party list: HP, MP, names and status icons",
        "Status effects" => "Your own enhancements and enfeeblements, as two separate groups of icons",
        "Filters" => "Named lists of statuses, reused by icon rows, nameplates and trackers",
        "Nameplates" => "Bars and names over enemies and players, with progress bars and status icons",
        "Enmity list" => "The enemies fighting you: HP, enmity, progress bars and status icons",
        "Tooltip" => "The box that describes an enhancement or enfeeblement when you hover over its icon",
        "Progress bars" => "Your progress bar and your target's",
        "Job gauges" => "The gauges of your job",
        "Others" => "Limit Break, Frontline and other special bars",
        "Appearance" => "General settings of the interface",
        "Profiles" => "Export, import and back up your settings",
        "Layout" => "Position and size of every element",
        _ => "Overview and status"
    };

    private void DrawGeneral()
    {
        // ---- Welcome
        Widgets.Card("##card_welcome", 7, () =>
        {
            ImGui.TextColored(Theme.Accent, "NahjaUI");
            ImGui.TextColored(Theme.TextMuted, $"Version {typeof(Plugin).Assembly.GetName().Version}  -  early development build");
            ImGui.Spacing();
            ImGui.TextWrapped("A modular, minimal interface for FINAL FANTASY XIV. It redraws the parts of the game's interface you choose, and a tracker engine shows only the enhancements, enfeeblements and cooldowns you care about.");
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextMuted, "Nothing here changes how the game plays: NahjaUI only reads the game and draws.");
        });

        // ---- Getting started
        Widgets.Card("##card_start", 9, () =>
        {
            Widgets.SectionLabel("Getting started");

            ImGui.TextColored(Theme.Accent, "1");
            ImGui.SameLine();
            ImGui.TextWrapped("Turn on what you want to replace: HUD bars, Nameplates, Enmity list, Progress bars, Job gauges. Each page has its own switch, and the game's own element is hidden only while ours is on.");

            ImGui.TextColored(Theme.Accent, "2");
            ImGui.SameLine();
            ImGui.TextWrapped("Create your trackers on the Trackers page: which status or cooldown to track, and how it looks.");

            ImGui.TextColored(Theme.Accent, "3");
            ImGui.SameLine();
            ImGui.TextWrapped("Place everything with unlock mode: drag each box, or type exact values. Elements can also be attached to each other on the Layout page.");

            ImGui.Spacing();
            if (ImGui.Button("Start unlock mode"))
                plugin.Layout.UnlockMode = true;

            ImGui.SameLine();
            ImGui.TextColored(Theme.TextMuted, "or type /nui unlock");

            ImGui.Spacing();
            ImGui.TextColored(Theme.Accent, "4");
            ImGui.SameLine();
            ImGui.TextWrapped("Back up and share your setup on the Profiles page.");
        });

        // ---- What is inside
        Widgets.Card("##card_pages", Pages.Length + 1, () =>
        {
            Widgets.SectionLabel("What is inside");

            foreach (var page in Pages)
            {
                if (page is "General")
                    continue;

                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
                var open = ImGui.Selectable($"{page}##open_{page}", false, ImGuiSelectableFlags.None, new Vector2(ImGui.CalcTextSize("Status effects").X + 24f * ImGuiHelpers.GlobalScale, 0f));
                ImGui.PopStyleColor();

                ImGui.SameLine();
                ImGui.TextColored(Theme.TextMuted, PageSubtitle(page));

                if (open)
                {
                    plugin.Configuration.SelectedPage = page;
                    plugin.RequestSave();
                }
            }
        });

        // ---- Commands
        Widgets.Card("##card_commands", 4, () =>
        {
            Widgets.SectionLabel("Commands");
            ImGui.TextColored(Theme.Accent, "/nui");
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextMuted, "Open or close this window.");
            ImGui.TextColored(Theme.Accent, "/nui unlock");
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextMuted, "Start or stop unlock mode, to move elements.");
        });

    }

    private void DrawAppearance()
    {
        var config = plugin.Configuration;

        Widgets.Card("##card_theme", 9, () =>
        {
            Widgets.SectionLabel("Theme");

            if (ImGui.BeginCombo("Menu text size (px)", $"{config.MenuTextSize} px"))
            {
                foreach (var size in OverlayFonts.MenuTextSizes)
                {
                    if (ImGui.Selectable($"{size} px", size == config.MenuTextSize))
                    {
                        config.MenuTextSize = size;
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }

            var inBar = config.ShowInServerBar;
            if (ImGui.Checkbox("Show NahjaUI in the server info bar", ref inBar))
            {
                config.ShowInServerBar = inBar;
                plugin.ServerInfoBar.UpdateShown();
                plugin.RequestSave();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The entry at the top right: click it to open the window, right click for unlock mode. /nui works either way.");

            var streamer = config.StreamerMode;
            if (ImGui.Checkbox("Streamer mode", ref streamer))
            {
                config.StreamerMode = streamer;
                plugin.RequestSave();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The names of player characters are replaced by names of Final Fantasy characters in everything NahjaUI draws. The game's own windows (chat, its own lists) are not changed.");

            ImGui.SameLine();
            if (ImGui.Button("Change names"))
            {
                config.StreamerSeed = Random.Shared.Next(1, int.MaxValue);
                plugin.RequestSave();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Gives everyone a different invented name, yours included.");

            ImGui.Spacing();
            if (ImGui.Button("Reset window"))
                centerRequested = true;
        });

        // The colors that every part of the interface uses unless it has its own ("Custom color").
        Widgets.Card("##card_shared_colors", 24, () =>
        {
            Widgets.SectionLabel("Shared style of the interface");
            ImGui.TextColored(Theme.TextMuted, "Used everywhere. A bar, a text or a gauge can have its own with \"Custom color\". Click a color to change it: its window has a Reset button.");

            var colors = config.Colors;

            void Pick(string label, Vector4 current, Vector4 original, bool alpha, Action<Vector4> apply)
            {
                var value = current;
                if (Widgets.ColorSetting(label, ref value, original, alpha))
                {
                    apply(value);
                    plugin.RequestSave();
                }
            }

            Pick("Background color", colors.Background, Theme.BarBackground, true, c => colors.Background = c);
            Pick("Border color", colors.Border, Theme.BarBorder, false, c => colors.Border = c);
            Pick("Cleanse border color (Esuna)", colors.DispelBorder, GlobalColors.DefaultDispelBorder, false, c => colors.DispelBorder = c);
            Pick("Text color", colors.Text, GlobalColors.DefaultText, false, c => colors.Text = c);
            Pick("Shield color", colors.Shield, GlobalColors.DefaultShield, false, c => colors.Shield = c);
            Pick("MP color", colors.Mp, GlobalColors.DefaultMp, false, c => colors.Mp = c);
            Pick("Friendly NPC HP color", colors.FriendlyNpc, GlobalColors.DefaultFriendlyNpc, false, c => colors.FriendlyNpc = c);
            Pick("Enemy HP color", colors.EnemyHp, GlobalColors.DefaultEnemyHp, false, c => colors.EnemyHp = c);
            Pick("Pet / minion HP color", colors.PetMinion, GlobalColors.DefaultPetMinion, false, c => colors.PetMinion = c);
            Pick("Object color", colors.ObjectColor, GlobalColors.DefaultObject, false, c => colors.ObjectColor = c);
            Pick("Timer text color", colors.TimerText, GlobalColors.DefaultText, false, c => colors.TimerText = c);
            Pick("Stacks text color", colors.StacksText, GlobalColors.DefaultText, false, c => colors.StacksText = c);
            Pick("Target ring color", config.TargetRingColor, new Vector4(1f, 1f, 1f, 1f), false, c => config.TargetRingColor = c);

            ImGui.Spacing();
            var cornerRounding = colors.Rounding;
            if (ImGui.SliderInt("Corner rounding (px)", ref cornerRounding, 0, 12))
            {
                colors.Rounding = cornerRounding;
                plugin.RequestSave();
            }

            ImGui.TextColored(Theme.TextMuted, "0 gives square corners. It applies to every bar, gauge, frame and icon.");

            ImGui.Spacing();
            plugin.HudBarEditorPage.DrawRoleColors();
        });
    }

    // ---------------------------------------------------------------- layout page

    private void DrawLayout()
    {
        var layout = plugin.Layout;
        layout.MarkLayoutPageOpen();

        // The explanation and the button stay pinned at the top; the rest of the page scrolls under them.
        Widgets.Callout(
            "##callout_unlock",
            "Unlock mode shows every element as a box you can drag. Select one to get X/Y fields next to it for exact positions. Positions are saved automatically.",
            Theme.Info,
            4,
            () =>
            {
                ImGui.Spacing();
                if (Widgets.PrimaryButton("Unlock elements"))
                    layout.UnlockMode = true;
            });

        using (Widgets.TabBody("##layout_body"))
        {
            // Alphabetical, so an element is found by its name.
            var movable = layout.Positionable.OrderBy(element => element.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            Widgets.Card("##card_elements", movable.Count + 2, () =>
            {
                Widgets.SectionLabel("Elements");

                foreach (var element in movable)
                {
                    var listName = layout.IsPerJob(element.Id) ? element.Name + "  (per job)" : element.Name;
                    if (ImGui.Selectable(listName + "##" + element.Id, layout.SelectedId == element.Id))
                        layout.SelectedId = element.Id;
                }
            });

            var selected = layout.SelectedElement;
            if (selected is null)
            {
                ImGui.TextColored(Theme.TextMuted, "Select an element to edit its position and size.");
                return;
            }

            Widgets.Card("##card_editor", 20, () => DrawElementEditor(selected));
        }
    }

    private void DrawElementEditor(LayoutElement element)
    {
        var data = element.Layout;

        Widgets.SectionLabel(element.Name);

        // The element can be placed on the screen or on another element (the enfeeblements under the target bar, for example).
        var attachName = "Screen";
        if (!string.IsNullOrEmpty(data.AttachTo))
            attachName = plugin.Layout.Elements.FirstOrDefault(e => e.Id == data.AttachTo)?.Name ?? "Screen";

        if (ImGui.BeginCombo("Attach to", attachName))
        {
            if (ImGui.Selectable("Screen", string.IsNullOrEmpty(data.AttachTo)))
            {
                data.AttachTo = null;
                plugin.RequestSave();
            }

            foreach (var other in plugin.Layout.Elements)
            {
                // Only what is in use: not the groups of other jobs (the one it is attached to now stays in the list).
                if (ReferenceEquals(other, element) || other.IsManaged || (other.IsHidden && data.AttachTo != other.Id))
                    continue;

                if (ImGui.Selectable($"{other.Name}##attach_{other.Id}", data.AttachTo == other.Id))
                {
                    data.AttachTo = other.Id;
                    plugin.RequestSave();
                }
            }

            ImGui.EndCombo();
        }

        var screenAnchor = data.ScreenAnchor;
        if (AnchorCombo(string.IsNullOrEmpty(data.AttachTo) ? "Screen anchor" : "Anchor on that element", ref screenAnchor))
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
        if (ImGui.InputInt("Offset X (px)", ref x, 1, 5))
        {
            data.OffsetX = x;
            plugin.RequestSave();
        }

        // Up is plus (the saved value counts downwards, so the sign is turned here).
        var y = -data.OffsetY;
        if (ImGui.InputInt("Offset Y (px)", ref y, 1, 5))
        {
            data.OffsetY = -y;
            plugin.RequestSave();
        }

        ImGui.Spacing();

        // One position for each job: it follows the job you play, so a job with a big gauge and another without it can
        // each have the bars where they fit.
        var perJob = plugin.Layout.IsPerJob(element.Id);
        if (ImGui.Checkbox("A different position for each job", ref perJob))
        {
            plugin.Layout.SetPerJob(element, perJob, plugin.Layout.CurrentKey);
            plugin.RequestSave();
        }

        if (perJob)
        {
            // PvE and PvP have each their own position for a job.
            var key = plugin.Layout.CurrentKey;
            var inPvp = key >= LayoutManager.PvpKeyOffset;
            var jobId = inPvp ? key - LayoutManager.PvpKeyOffset : key;
            var jobName = jobId == 0 ? "no job" : GameNames.JobAbbreviation(jobId);
            ImGui.TextColored(Theme.TextMuted, $"What you change here is saved for {jobName} in {(inPvp ? "PvP" : "PvE")}. A job you have not moved it on starts from where it was when you turned this on (in PvP, from its position in PvE).");
        }

        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, "Show this element");
        if (VisibilityControls.Draw(data))
            plugin.RequestSave();

        ImGui.Spacing();

        var locked = data.Locked;
        if (ImGui.Checkbox("Locked (cannot be moved in unlock mode)", ref locked))
        {
            data.Locked = locked;
            plugin.RequestSave();
        }

        if (ImGui.Button("Reset position"))
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
}
