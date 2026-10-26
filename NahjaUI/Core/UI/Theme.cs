using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace NahjaUI.Core.Ui;

/// <summary>
/// Design tokens of NahjaUI: deep indigo palette, one user-configurable accent colour (violet by default).
/// Every window and widget takes its colours from here, so changing the look means changing this file.
/// </summary>
internal static class Theme
{
    public static readonly Vector4 DefaultAccent = Rgb(139, 124, 255);

    public static readonly Vector4 Background = Rgb(16, 18, 30);
    public static readonly Vector4 Sidebar = Rgb(21, 24, 40);
    public static readonly Vector4 Card = Rgb(28, 32, 52);
    public static readonly Vector4 Field = Rgb(40, 45, 72);
    public static readonly Vector4 Border = Rgb(58, 64, 100);

    public static readonly Vector4 Text = Rgb(232, 234, 248);
    public static readonly Vector4 TextMuted = Rgb(140, 148, 180);
    public static readonly Vector4 TextOnAccent = Rgb(14, 12, 32);

    // Semantic colours.
    public static readonly Vector4 Info = Rgb(91, 157, 235);
    public static readonly Vector4 Warning = Rgb(232, 166, 52);
    public static readonly Vector4 Success = Rgb(102, 204, 128);
    public static readonly Vector4 Error = Rgb(230, 90, 90);

    // The current accent. ThemeScope updates it from the configuration every frame.
    public static Vector4 Accent { get; internal set; } = DefaultAccent;

    public static Vector4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    public static Vector4 WithAlpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, t);
}

/// <summary>
/// Pushes the NahjaUI colours and style values onto ImGui. Create it in PreDraw, dispose it in PostDraw.
/// </summary>
internal sealed class ThemeScope : IDisposable
{
    private int colorCount;
    private int varCount;
    private bool disposed;

    // The font of the menus. Pushed before the window starts, so the title bar uses it too.
    private readonly IDisposable? fontScope;

    public ThemeScope(Configuration config)
    {
        var scale = ImGuiHelpers.GlobalScale;

        // Logical pixels: the fonts are already built for the UI scale.
        fontScope = OverlayFonts.Current?.PushClosest(OverlayFont.NahjaText, config.MenuTextSize);
        var accent = config.AccentColor;
        Theme.Accent = accent;

        var accentHover = Theme.Mix(accent, Vector4.One, 0.15f);

        // Text
        PushColor(ImGuiCol.Text, Theme.Text);
        PushColor(ImGuiCol.TextDisabled, Theme.TextMuted);

        // Surfaces
        PushColor(ImGuiCol.WindowBg, Theme.Background);
        PushColor(ImGuiCol.ChildBg, Theme.Card);
        PushColor(ImGuiCol.PopupBg, Theme.Card);
        PushColor(ImGuiCol.Border, Theme.Border);
        PushColor(ImGuiCol.Separator, Theme.Border);
        PushColor(ImGuiCol.TitleBg, Theme.Sidebar);
        PushColor(ImGuiCol.TitleBgActive, Theme.Sidebar);
        PushColor(ImGuiCol.TitleBgCollapsed, Theme.Sidebar);

        // Inputs
        PushColor(ImGuiCol.FrameBg, Theme.Field);
        PushColor(ImGuiCol.FrameBgHovered, Theme.Mix(Theme.Field, accent, 0.15f));
        PushColor(ImGuiCol.FrameBgActive, Theme.Mix(Theme.Field, accent, 0.25f));
        PushColor(ImGuiCol.CheckMark, accent);
        PushColor(ImGuiCol.SliderGrab, accent);
        PushColor(ImGuiCol.SliderGrabActive, accentHover);

        // Buttons (secondary look; the primary button is in Widgets)
        PushColor(ImGuiCol.Button, Theme.Field);
        PushColor(ImGuiCol.ButtonHovered, Theme.Mix(Theme.Field, accent, 0.30f));
        PushColor(ImGuiCol.ButtonActive, Theme.Mix(Theme.Field, accent, 0.50f));

        // Headers and selectables
        PushColor(ImGuiCol.Header, Theme.WithAlpha(accent, 0.25f));
        PushColor(ImGuiCol.HeaderHovered, Theme.WithAlpha(accent, 0.35f));
        PushColor(ImGuiCol.HeaderActive, Theme.WithAlpha(accent, 0.45f));

        // Scrollbars and resize grip
        PushColor(ImGuiCol.ScrollbarBg, Theme.WithAlpha(Theme.Background, 0f));
        PushColor(ImGuiCol.ScrollbarGrab, Theme.Border);
        PushColor(ImGuiCol.ScrollbarGrabHovered, Theme.Mix(Theme.Border, accent, 0.4f));
        PushColor(ImGuiCol.ScrollbarGrabActive, accent);
        PushColor(ImGuiCol.ResizeGrip, Theme.WithAlpha(accent, 0.25f));
        PushColor(ImGuiCol.ResizeGripHovered, Theme.WithAlpha(accent, 0.60f));
        PushColor(ImGuiCol.ResizeGripActive, Theme.WithAlpha(accent, 0.90f));

        // Shape: rounded everywhere, generous spacing. All pixel values scale with the user's UI scale.
        PushVar(ImGuiStyleVar.WindowRounding, 10f * scale);
        PushVar(ImGuiStyleVar.ChildRounding, 8f * scale);
        PushVar(ImGuiStyleVar.FrameRounding, 6f * scale);
        PushVar(ImGuiStyleVar.GrabRounding, 6f * scale);
        PushVar(ImGuiStyleVar.PopupRounding, 8f * scale);
        PushVar(ImGuiStyleVar.ScrollbarRounding, 8f * scale);
        PushVar(ImGuiStyleVar.WindowBorderSize, 1f);
        PushVar(ImGuiStyleVar.ChildBorderSize, 1f);
        PushVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 12f) * scale);
        PushVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 8f) * scale);
    }

    private void PushColor(ImGuiCol index, Vector4 value)
    {
        ImGui.PushStyleColor(index, value);
        colorCount++;
    }

    private void PushVar(ImGuiStyleVar index, float value)
    {
        ImGui.PushStyleVar(index, value);
        varCount++;
    }

    private void PushVar(ImGuiStyleVar index, Vector2 value)
    {
        ImGui.PushStyleVar(index, value);
        varCount++;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        ImGui.PopStyleVar(varCount);
        ImGui.PopStyleColor(colorCount);
        fontScope?.Dispose();
    }
}

/// <summary>
/// Padding of buttons and input fields. Create it inside Draw(), after the window has begun,
/// and NOT in PreDraw: a bigger FramePadding makes the title bar taller and Dalamud's own
/// title bar buttons then overlap.
/// </summary>
internal sealed class ContentStyleScope : IDisposable
{
    private bool disposed;

    public ContentStyleScope()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 6f) * ImGuiHelpers.GlobalScale);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        ImGui.PopStyleVar(1);
    }
}
