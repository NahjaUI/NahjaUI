using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using NahjaUI.Core;
using NahjaUI.Core.Ui;

namespace NahjaUI.Windows;

/// <summary>
/// The setup window: opens by itself the first time on a new install, and again from the General page ("Run the setup again") or
/// with /nui setup. Four short steps; every choice is applied right away, so it can be seen on screen while choosing.
/// </summary>
public sealed class SetupWindow : Window
{
    private const int StepCount = 4;

    // Logical pixels (Dalamud multiplies the size of a window by the UI scale itself).
    private static readonly Vector2 WindowSize = new(600f, 470f);

    private readonly Plugin plugin;
    private ThemeScope? themeScope;
    private int step;
    private SetupPackage package = SetupPackage.Full;

    // Set while unlock mode hides the window: closing it then is not the user's choice, so the setup is not marked as done.
    private bool hiddenForUnlock;

    public SetupWindow(Plugin plugin)
        : base("NahjaUI setup###NahjaUISetup", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;

        AllowPinning = false;
        AllowClickthrough = false;

        Size = WindowSize;
        SizeCondition = ImGuiCond.Always;
    }

    /// <summary>Opens the setup from the first step.</summary>
    public void Open()
    {
        step = 0;
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

        // The background goes first, under everything the window draws after it.
        DrawBackground(scale);

        using var contentStyle = new ContentStyleScope();

        DrawHeader(scale);

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

    // Everything under the title bar of the window.
    private void DrawBackground(float scale)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var top = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().WindowPadding.Y;
        var rounding = MathF.Round(Theme.WindowRounding * scale);

        AnimatedBackground.Draw(
            ImGui.GetWindowDrawList(),
            new Vector2(windowPos.X, top),
            windowPos + windowSize,
            rounding,
            ImDrawFlags.RoundCornersBottom,
            plugin.Configuration.AnimatedBackground);
    }

    // The mark and the name at the left, and one dot for each step at the right (the one you are on is longer).
    private void DrawHeader(float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var markSize = MathF.Round(28f * scale);

        NahjaMark.Draw(drawList, pos, markSize);

        var textPos = new Vector2(MathF.Round(pos.X + markSize + 10f * scale), MathF.Round(pos.Y + (markSize - ImGui.GetTextLineHeight()) / 2f));
        drawList.AddText(textPos, ImGui.GetColorU32(Theme.Text), "NahjaUI");

        var dotHeight = MathF.Round(8f * scale);
        var gap = MathF.Round(6f * scale);
        var longDot = MathF.Round(26f * scale);
        var shortDot = dotHeight;
        var total = longDot + shortDot * (StepCount - 1) + gap * (StepCount - 1);

        var x = ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - total;
        var y = MathF.Round(pos.Y + (markSize - dotHeight) / 2f);
        for (var i = 0; i < StepCount; i++)
        {
            var width = i == step ? longDot : shortDot;
            var color = i == step ? Theme.Accent : i < step ? Theme.WithAlpha(Theme.Accent, 0.55f) : Theme.WithAlpha(Theme.TextMuted, 0.45f);
            drawList.AddRectFilled(new Vector2(MathF.Round(x), y), new Vector2(MathF.Round(x + width), y + dotHeight), ImGui.GetColorU32(color), dotHeight / 2f);
            x += width + gap;
        }

        // The cursor goes under the header, with a space before the body.
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + markSize + 8f * scale));
        ImGui.Dummy(new Vector2(0f, 4f * scale));
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
        Muted("Nothing here changes how the game plays: NahjaUI only reads the game and draws.");
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
