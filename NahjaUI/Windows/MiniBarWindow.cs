using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using NahjaUI.Core.Ui;

namespace NahjaUI.Windows;

/// <summary>
/// What the main window turns into when it is minimized: a small bar with the NahjaUI mark and name, the arrow to open
/// the window again and the cross to close it. The bar can be dragged by its left part. Opening the window puts it
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
        var centerY = pos.Y + size.Y / 2f;

        // The two buttons at the right: the arrow (open the window) and the cross (close the bar).
        var button = TitleButtons.Size;
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

        // Double click on the bar: back to the window, like the double click that minimized it.
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            plugin.ExpandMainWindow();
            return;
        }

        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (delta != Vector2.Zero)
                ImGui.SetWindowPos(pos + delta);
        }

        if (TitleButtons.Draw("##nui_minibar_open", openPos, TitleButtons.Glyph.Arrow, "Open NahjaUI"))
            plugin.ExpandMainWindow();

        if (TitleButtons.Draw("##nui_minibar_close", closePos, TitleButtons.Glyph.Close, "Close"))
            plugin.CloseMiniBar();
    }
}
