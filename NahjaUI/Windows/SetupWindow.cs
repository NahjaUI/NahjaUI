using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using NahjaUI.Core;
using NahjaUI.Core.Profiles;
using NahjaUI.Core.Ui;

namespace NahjaUI.Windows;

/// <summary>
/// The setup window: opens by itself the first time on a new install, and again from the General page ("Run the setup again") or
/// with /nui setup. Five short steps; every choice is applied right away, so it can be seen on screen while choosing.
/// </summary>
public sealed class SetupWindow : Window
{
    private const int StepCount = 5;

    // Logical pixels (Dalamud multiplies the size of a window by the UI scale itself).
    private static readonly Vector2 WindowSize = new(600f, 470f);

    private readonly Plugin plugin;
    private ThemeScope? themeScope;
    private int step;
    private SetupPackage package = SetupPackage.Full;

    // The arrow of the title bar folds the window to its bar (and unfolds it).
    private bool folded;

    // Set while unlock mode hides the window: closing it then is not the user's choice, so the setup is not marked as done.
    private bool hiddenForUnlock;

    public SetupWindow(Plugin plugin)
        // No title bar of the game's window system: the window draws its own, like the main window (see DrawTitleBar).
        // It is moved by that bar, so the window itself cannot be dragged from anywhere else.
        : base("NahjaUI setup###NahjaUISetup", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove)
    {
        this.plugin = plugin;

        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;

        // The size is set in PreDraw (it changes when the window is folded), so it is not set here.
    }

    /// <summary>Opens the setup from the first step.</summary>
    public void Open()
    {
        step = 0;
        folded = false;
        hiddenForUnlock = false;
        IsOpen = true;
    }

    /// <summary>Unlock mode needs the whole screen: the window steps aside and comes back with <see cref="ShowAfterUnlock"/>.</summary>
    public void HideForUnlock()
    {
        hiddenForUnlock = true;
        IsOpen = false;
    }

    public void ShowAfterUnlock()
    {
        hiddenForUnlock = false;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        themeScope?.Dispose();
        themeScope = new ThemeScope(plugin.Configuration);

        // Physical pixels: the whole window, or only its title bar when folded.
        var scale = ImGuiHelpers.GlobalScale;
        var size = folded ? new Vector2(WindowSize.X * scale, TitleBarHeight(scale)) : WindowSize * scale;
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size / 2f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
    }

    public override void PostDraw()
    {
        themeScope?.Dispose();
        themeScope = null;
    }

    // Closing the window with the cross counts as "skip": it does not open by itself again.
    public override void OnClose()
    {
        if (hiddenForUnlock)
            return;

        Finish();
    }

    private void Finish()
    {
        plugin.Configuration.SetupPending = false;
        plugin.SaveNow();
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        // The background goes first, under everything the window draws after it (the title bar is drawn over it).
        DrawBackground(scale);

        var titleHeight = DrawTitleBar(scale);
        if (folded)
            return;

        // The content starts under the title bar, with the same margin as at the left.
        ImGui.SetCursorPos(new Vector2(ImGui.GetStyle().WindowPadding.X, titleHeight + ImGui.GetStyle().WindowPadding.Y));

        using var contentStyle = new ContentStyleScope();

        // The body has its own area, so the buttons always stay at the bottom. Its background is see-through: the animated one shows.
        var footer = 46f * scale;
        using (ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero))
        using (var body = ImRaii.Child("##setup_body", new Vector2(0f, -footer), false))
        {
            if (body.Success)
            {
                // A little air at the left and the right of the text (the child has no padding of its own).
                ImGui.Indent(6f * scale);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - 10f * scale);

                switch (step)
                {
                    case 0:
                        DrawWelcome();
                        break;
                    case 1:
                        DrawReplaced();
                        break;
                    case 2:
                        DrawPackage(scale);
                        break;
                    case 3:
                        DrawSize();
                        break;
                    default:
                        DrawPlacement();
                        break;
                }

                ImGui.PopTextWrapPos();
                ImGui.Unindent(6f * scale);
            }
        }

        DrawFooter(scale);
    }

    // The whole window, with its rounded corners; the title bar is drawn over it.
    private void DrawBackground(float scale)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        AnimatedBackground.Draw(
            ImGui.GetWindowDrawList(),
            windowPos,
            windowPos + windowSize,
            MathF.Round(Theme.WindowRounding * scale),
            ImDrawFlags.RoundCornersAll,
            plugin.Configuration.AnimatedBackground);
    }

    private static float TitleBarHeight(float scale) => MathF.Round(MathF.Max(34f * scale, ImGui.GetTextLineHeight() + 14f * scale));

    // The same title bar as the main window: icon and name at the left (with a margin), one dot for each step (the one you are on is
    // longer), and the arrow (fold) and the cross (close) at the right. It is also the handle to move the window. Returns its height.
    private float DrawTitleBar(float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var width = ImGui.GetWindowSize().X;
        var height = TitleBarHeight(scale);
        var rounding = MathF.Min(MathF.Round(Theme.WindowRounding * scale), height / 2f + 12f * scale);

        drawList.AddRectFilled(windowPos, windowPos + new Vector2(width, height), ImGui.GetColorU32(Theme.Sidebar), rounding, ImDrawFlags.RoundCornersTop);

        var padding = MathF.Round(12f * scale);
        var gap = MathF.Round(4f * scale);
        var button = TitleButtons.Size;
        var centerY = windowPos.Y + height / 2f;

        var closePos = new Vector2(MathF.Round(windowPos.X + width - padding - button), MathF.Round(centerY - button / 2f));
        var foldPos = new Vector2(closePos.X - gap - button, closePos.Y);

        // The icon and the name at the left.
        var markSize = MathF.Round(22f * scale);
        var markPos = new Vector2(MathF.Round(windowPos.X + padding), MathF.Round(centerY - markSize / 2f));
        NahjaMark.Draw(drawList, markPos, markSize);

        var textPos = new Vector2(MathF.Round(markPos.X + markSize + 8f * scale), MathF.Round(centerY - ImGui.GetTextLineHeight() / 2f));
        drawList.AddText(textPos, ImGui.GetColorU32(Theme.Text), "NahjaUI setup");

        // The steps, just before the buttons.
        var dotHeight = MathF.Round(8f * scale);
        var dotGap = MathF.Round(6f * scale);
        var longDot = MathF.Round(26f * scale);
        var total = longDot + dotHeight * (StepCount - 1) + dotGap * (StepCount - 1);
        var x = foldPos.X - gap - 10f * scale - total;
        var y = MathF.Round(centerY - dotHeight / 2f);
        for (var i = 0; i < StepCount; i++)
        {
            var dotWidth = i == step ? longDot : dotHeight;
            var color = i == step ? Theme.Accent : i < step ? Theme.WithAlpha(Theme.Accent, 0.55f) : Theme.WithAlpha(Theme.TextMuted, 0.45f);
            drawList.AddRectFilled(new Vector2(MathF.Round(x), y), new Vector2(MathF.Round(x + dotWidth), y + dotHeight), ImGui.GetColorU32(color), dotHeight / 2f);
            x += dotWidth + dotGap;
        }

        // The rest of the bar moves the window; a double click folds it.
        ImGui.SetCursorScreenPos(windowPos + new Vector2(1f, 1f));
        ImGui.InvisibleButton("##setup_title_drag", new Vector2(foldPos.X - gap - windowPos.X, height - 2f));

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            folded = !folded;

        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (delta != Vector2.Zero)
            {
                // Never lose the bar: at least a part of it always stays on the screen.
                var viewport = ImGui.GetMainViewport();
                var size = ImGui.GetWindowSize();
                var wanted = windowPos + delta;
                var nx = Math.Clamp(wanted.X, viewport.Pos.X - size.X + 80f * scale, viewport.Pos.X + viewport.Size.X - 80f * scale);
                var ny = Math.Clamp(wanted.Y, viewport.Pos.Y, viewport.Pos.Y + viewport.Size.Y - height);
                ImGui.SetWindowPos(new Vector2(MathF.Round(nx), MathF.Round(ny)));
            }
        }

        if (TitleButtons.Draw("##setup_title_fold", foldPos, TitleButtons.Glyph.Arrow, folded ? "Show the setup" : "Fold to the title bar"))
            folded = !folded;

        if (TitleButtons.Draw("##setup_title_close", closePos, TitleButtons.Glyph.Close, "Close"))
            IsOpen = false;

        return height;
    }

    private static void Muted(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextMuted);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    // The title of a step: in the accent color, with a short line under it.
    private static void Title(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TextColored(Theme.Accent, text.ToUpperInvariant());

        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(pos.X, pos.Y + 2f * scale),
            new Vector2(pos.X + 28f * scale, pos.Y + 4f * scale),
            ImGui.GetColorU32(Theme.Accent),
            2f * scale);

        ImGui.Dummy(new Vector2(0f, 12f * scale));
    }

    private void DrawWelcome()
    {
        Title("Welcome to NahjaUI");
        ImGui.TextWrapped("A modular, minimal interface for FINAL FANTASY XIV. It redraws the parts of the game's interface you choose, and shows only the enhancements, enfeeblements and cooldowns you care about.");
        ImGui.Spacing();
        ImGui.TextWrapped("This takes less than a minute. You choose a starting point, and everything can be changed later from the menu.");
        ImGui.Spacing();
        Muted("NahjaUI only reads the game and draws. The one exception is the optional Mouseover actions switch (General page), which is off unless you turn it on.");
    }

    private void DrawReplaced()
    {
        Title("What NahjaUI replaces");
        ImGui.TextWrapped("When a module is on, NahjaUI hides the game's own element and draws its own. When it is off, the game's one comes back by itself. NahjaUI does not touch your HUD layout in the game.");
        ImGui.Spacing();
        Muted("Replaced when on: your HP and MP bars, target and focus, party and alliance lists, enemy list, cast bars, status icons and job gauges.");
        ImGui.Spacing();
        ImGui.TextWrapped("Nameplates are different: NahjaUI draws over the game's. Which plates appear (names, HP bars) is still decided by the nameplate options of the game.");

        var other = OtherInterfacePlugins();
        if (other.Length > 0)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Warning);
            ImGui.TextWrapped($"Another interface plugin is installed and loaded: {string.Join(", ", other)}. If it draws the same element as NahjaUI (HP bars, party list, enemy list), you will see it twice. Turn that element off in one of the two.");
            ImGui.PopStyleColor();
        }
    }

    private void DrawPackage(float scale)
    {
        Title("What do you want to see?");
        Muted("Pick a starting point. It changes right away, so you can see it on screen. Everything can be tuned later in the menu.");
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        foreach (var option in new[] { SetupPackage.Essential, SetupPackage.Full, SetupPackage.TrackersOnly })
        {
            if (OptionCard($"##package_{option}", SetupPackages.Name(option), SetupPackages.Summary(option), package == option) && package != option)
            {
                package = option;
                SetupPackages.Apply(plugin.Configuration, package);
                plugin.RequestSave();
            }
        }
    }

    // The size of everything: once the frames of the chosen package are on the screen, the user sees how big they are and can change it.
    private void DrawSize()
    {
        Title("How big do you want it?");
        ImGui.TextWrapped("This is how it looks now. If it is too small or too big for your screen, drag the slider: the interface follows it on the screen, so you can find the size you like.");
        ImGui.Spacing();
        Muted("The percentage always counts from the normal size (100 %), so you can go up and down and come back to it. You can change it any time on the Layout page of the menu.");
        ImGui.Spacing();
        ImGui.Spacing();

        ResizeControls.Draw(plugin, "setup");
    }

    // One option as a translucent rounded card: the title, the explanation under it, and a round mark at the right (filled when chosen).
    private static bool OptionCard(string id, string title, string text, bool selected)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X - 10f * scale;
        var padding = MathF.Round(12f * scale);
        var textWidth = width - padding * 2f - 26f * scale;

        var height = MathF.Round(padding * 2f + ImGui.GetTextLineHeight() + 4f * scale + WrappedLines(text, textWidth) * ImGui.GetTextLineHeight());

        ImGui.InvisibleButton(id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();

        var max = pos + new Vector2(width, height);
        var rounding = MathF.Round(10f * scale);
        var fill = selected ? Theme.WithAlpha(Theme.Accent, 0.24f) : Theme.WithAlpha(Theme.Card, hovered ? 0.80f : 0.58f);
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(fill), rounding);
        drawList.AddRect(pos, max, ImGui.GetColorU32(selected ? Theme.Accent : Theme.WithAlpha(Theme.Border, 0.8f)), rounding, ImDrawFlags.None, selected ? 1.5f : 1f);

        // The round mark.
        var radius = MathF.Round(8f * scale);
        var markCenter = new Vector2(max.X - padding - radius, pos.Y + padding + ImGui.GetTextLineHeight() / 2f);
        drawList.AddCircle(markCenter, radius, ImGui.GetColorU32(selected ? Theme.Accent : Theme.TextMuted), 24, 1.5f);
        if (selected)
            drawList.AddCircleFilled(markCenter, radius - 4f * scale, ImGui.GetColorU32(Theme.Accent), 24);

        // The texts, drawn with the widgets of ImGui over the card (the invisible button below them already took the click).
        ImGui.SetCursorScreenPos(pos + new Vector2(padding, padding));
        ImGui.TextColored(Theme.Text, title);

        ImGui.SetCursorScreenPos(pos + new Vector2(padding, padding + ImGui.GetTextLineHeight() + 4f * scale));
        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + textWidth);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextMuted);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();

        // Under the card, with a space before the next one.
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + height));
        ImGui.Dummy(new Vector2(width, 10f * scale));

        return clicked;
    }

    // How many lines a text takes when it wraps at the given width (words are not cut), the way the wrapped text of ImGui breaks it.
    private static int WrappedLines(string text, float width)
    {
        var lines = 1;
        var lineWidth = 0f;
        var space = ImGui.CalcTextSize(" ").X;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var wordWidth = ImGui.CalcTextSize(word).X;
            if (lineWidth > 0f && lineWidth + space + wordWidth > width)
            {
                lines++;
                lineWidth = wordWidth;
            }
            else
            {
                lineWidth += (lineWidth > 0f ? space : 0f) + wordWidth;
            }
        }

        return lines;
    }

    private void DrawPlacement()
    {
        Title("Place it your way");
        ImGui.TextWrapped("Unlock mode lets you drag every element wherever you want, or type exact values. Elements can also be attached to each other on the Layout page of the menu.");
        ImGui.Spacing();

        if (Widgets.PrimaryButton("Open unlock mode"))
            plugin.Layout.UnlockMode = true;

        ImGui.SameLine();
        ImGui.TextColored(Theme.TextMuted, "or type /nui unlock");

        ImGui.Spacing();
        ImGui.Spacing();
        Muted("To see this setup again, use \"Run the setup again\" on the General page of the menu, or type /nui setup. Questions and ideas are welcome on the Discord.");
    }

    private void DrawFooter(float scale)
    {
        ImGui.SetCursorPosY(ImGui.GetWindowHeight() - 40f * scale);

        if (step < StepCount - 1)
        {
            if (ImGui.Button("Skip setup"))
            {
                IsOpen = false;
                return;
            }
        }
        else if (ImGui.Button("Open the menu"))
        {
            plugin.ToggleMainUi();
        }

        var padding = ImGui.GetStyle().FramePadding.X * 2f;
        var nextLabel = step < StepCount - 1 ? "Next" : "Finish";
        var width = ImGui.CalcTextSize(nextLabel).X + padding;
        if (step > 0)
            width += ImGui.CalcTextSize("Back").X + padding + ImGui.GetStyle().ItemSpacing.X;

        ImGui.SameLine(ImGui.GetWindowWidth() - width - ImGui.GetStyle().WindowPadding.X);

        if (step > 0)
        {
            if (ImGui.Button("Back"))
                step--;

            ImGui.SameLine();
        }

        if (Widgets.PrimaryButton(nextLabel))
        {
            if (step < StepCount - 1)
                step++;
            else
                IsOpen = false;
        }
    }

    // Other interface plugins that are loaded and may draw the same elements. Only the names we know: the list is short on purpose.
    private static string[] OtherInterfacePlugins()
    {
        try
        {
            return Plugin.PluginInterface.InstalledPlugins
                .Where(p => p.IsLoaded && p.InternalName.Contains("DelvUI", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name)
                .Distinct()
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
