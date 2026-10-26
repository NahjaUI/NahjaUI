using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using NahjaUI.Core.Ui;

namespace NahjaUI.Windows;

/// <summary>
/// What the main window turns into when it is minimized: a small bar with the NahjaUI mark and name, a button to open
/// the window again and a button to close it. The bar can be dragged by its left part. Opening the window puts it
/// where the bar is (top right corner on top right corner).
/// </summary>
public sealed class MiniBarWindow : Window, IDisposable
{
    // Logical pixels.
    private const float BarWidth = 190f;
    private const float BarHeight = 34f;

    private readonly Plugin plugin;
    private ThemeScope? themeScope;
    private bool paddingPushed;

    // Where the bar must appear. A couple of frames are used so the position wins over any other window logic.
    private Vector2 wantedTopRight;
    private int placeFrames;

    public MiniBarWindow(Plugin plugin)
        : base(
            "NahjaUI###NahjaUIMiniBar",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings)
    {
        this.plugin = plugin;

        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;
    }

    /// <summary>The top right corner of the bar, in screen pixels, as of the last frame.</summary>
    public Vector2 TopRight { get; private set; }

    /// <summary>Opens the bar with its top right corner at the given point (it is kept inside the screen).</summary>
    public void ShowAt(Vector2 topRight)
    {
        wantedTopRight = topRight;
        TopRight = topRight;
        placeFrames = 2;
        IsOpen = true;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        themeScope?.Dispose();
        themeScope = new ThemeScope(plugin.Configuration);

        var scale = ImGuiHelpers.GlobalScale;

        // Less padding than the big window, so the bar stays small.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 6f) * scale);
        paddingPushed = true;

        var size = new Vector2(MathF.Round(BarWidth * scale), MathF.Round(BarHeight * scale));
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);

        if (placeFrames > 0)
        {
            placeFrames--;

            var viewport = ImGui.GetMainViewport();
            var maxX = Math.Max(viewport.Pos.X, viewport.Pos.X + viewport.Size.X - size.X);
            var maxY = Math.Max(viewport.Pos.Y, viewport.Pos.Y + viewport.Size.Y - size.Y);

            var x = Math.Clamp(wantedTopRight.X - size.X, viewport.Pos.X, maxX);
            var y = Math.Clamp(wantedTopRight.Y, viewport.Pos.Y, maxY);

            ImGui.SetNextWindowPos(new Vector2(MathF.Round(x), MathF.Round(y)), ImGuiCond.Always);
        }
    }

    public override void PostDraw()
    {
        if (paddingPushed)
        {
            ImGui.PopStyleVar();
            paddingPushed = false;
        }

        themeScope?.Dispose();
        themeScope = null;
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();

        TopRight = new Vector2(pos.X + size.X, pos.Y);

        var padding = MathF.Round(10f * scale);
        var gap = MathF.Round(4f * scale);
        var button = MathF.Round(22f * scale);
        var centerY = pos.Y + size.Y / 2f;

        // The two buttons at the right: open the window, close the bar.
        var closePos = new Vector2(MathF.Round(pos.X + size.X - padding - button), MathF.Round(centerY - button / 2f));
        var openPos = new Vector2(closePos.X - gap - button, closePos.Y);

        // The mark and the name at the left. The whole left part is also the handle to drag the bar.
        var markSize = MathF.Round(20f * scale);
        var markPos = new Vector2(MathF.Round(pos.X + padding), MathF.Round(centerY - markSize / 2f));

        NahjaMark.Draw(drawList, markPos, markSize);

        var textPos = new Vector2(
            MathF.Round(markPos.X + markSize + 8f * scale),
            MathF.Round(centerY - ImGui.GetTextLineHeight() / 2f));
        drawList.AddText(textPos, ImGui.GetColorU32(Theme.Text), "NahjaUI");

        ImGui.SetCursorScreenPos(pos + new Vector2(1f, 1f));
        ImGui.InvisibleButton("##nui_minibar_drag", new Vector2(openPos.X - gap - pos.X, size.Y - 2f));

        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (delta != Vector2.Zero)
                ImGui.SetWindowPos(pos + delta);
        }

        if (IconButton("##nui_minibar_open", openPos, button, DrawOpenGlyph, "Open NahjaUI"))
            plugin.ExpandMainWindow();

        if (IconButton("##nui_minibar_close", closePos, button, DrawCloseGlyph, "Close"))
            plugin.CloseMiniBar();
    }

    // A square button with a hand-drawn symbol, so it does not depend on any font.
    private static bool IconButton(string id, Vector2 pos, float size, Action<ImDrawListPtr, Vector2, float, uint> glyph, string tooltip)
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();

        if (hovered)
        {
            drawList.AddRectFilled(
                pos,
                pos + new Vector2(size, size),
                ImGui.GetColorU32(Theme.WithAlpha(Theme.Accent, 0.30f)),
                6f * scale);
        }

        glyph(drawList, pos, size, ImGui.GetColorU32(hovered ? Theme.Text : Theme.TextMuted));

        if (hovered)
            ImGui.SetTooltip(tooltip);

        return clicked;
    }

    // A small square: "open the window".
    private static void DrawOpenGlyph(ImDrawListPtr drawList, Vector2 pos, float size, uint color)
    {
        var inset = MathF.Round(size * 0.28f);
        var thickness = MathF.Max(1f, MathF.Round(ImGuiHelpers.GlobalScale * 1.5f));

        drawList.AddRect(
            pos + new Vector2(inset, inset),
            pos + new Vector2(size - inset, size - inset),
            color,
            2f * ImGuiHelpers.GlobalScale,
            ImDrawFlags.None,
            thickness);
    }

    // A cross: "close".
    private static void DrawCloseGlyph(ImDrawListPtr drawList, Vector2 pos, float size, uint color)
    {
        var inset = MathF.Round(size * 0.30f);
        var thickness = MathF.Max(1f, MathF.Round(ImGuiHelpers.GlobalScale * 1.5f));

        var a = pos + new Vector2(inset, inset);
        var b = pos + new Vector2(size - inset, size - inset);
        var c = pos + new Vector2(size - inset, inset);
        var d = pos + new Vector2(inset, size - inset);

        drawList.AddLine(a, b, color, thickness);
        drawList.AddLine(c, d, color, thickness);
    }
}
