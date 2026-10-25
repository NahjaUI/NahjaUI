using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules;

/// <summary>
/// Temporary module: draws two plain boxes so the positioning system can be tested.
/// It is removed when the first real module (auras) arrives in H4.
/// </summary>
internal sealed class LayoutDemo
{
    private readonly Configuration config;
    private readonly LayoutElement boxA;
    private readonly LayoutElement boxB;

    public LayoutDemo(Configuration config, LayoutManager layout)
    {
        this.config = config;

        boxA = layout.Register("demo.a", "Test element A", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = -140,
            OffsetY = 0,
            Width = 200,
            Height = 40
        });

        boxB = layout.Register("demo.b", "Test element B", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 140,
            OffsetY = 0,
            Width = 200,
            Height = 40
        });
    }

    public void Draw()
    {
        if (!config.ShowTestElements)
            return;

        DrawBox(boxA);
        DrawBox(boxB);
    }

    private static void DrawBox(LayoutElement element)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(element.Layout);

        // The background draw list sits above the game but below every ImGui window.
        var drawList = ImGui.GetBackgroundDrawList();

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Theme.WithAlpha(Theme.Accent, 0.55f)), 0f);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(Theme.Accent), 0f, ImDrawFlags.None, 1f);
        drawList.AddText(pos + new Vector2(8f, 6f) * scale, ImGui.GetColorU32(Theme.Text), element.Name);
    }
}
