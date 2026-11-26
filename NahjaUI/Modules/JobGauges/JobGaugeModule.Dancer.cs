using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Game;
using NahjaUI.Modules.Trackers;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// The auras of the Dancer procs (both come from Flourish, so they usually show together). They have no timer and no desaturated copy: each one is drawn
/// while the status of the player is on. Threefold Fan Dance (Fan Dance III) is two arcs of emerald feather fans, one at each side of the character, like
/// the wings of the Warrior; Fourfold Fan Dance (Fan Dance IV) is one arch of blue wings above the character, like the arch of Thunderhead.
/// </summary>
internal sealed partial class JobGaugeModule
{
    // The width of each Fan Dance III arc over its height (the images are 360 x 768).
    private const float ThreefoldAspect = 360f / 768f;

    // The width of the Fan Dance IV arch over its height (the image is 960 x 360).
    private const float FourfoldAspect = 960f / 360f;

    private static uint threefoldId;
    private static uint fourfoldId;
    private static bool dancerIdsFound;

    public LayoutElement ThreefoldElement { get; private set; } = null!;

    public LayoutElement FourfoldElement { get; private set; } = null!;

    private void RegisterDancerAuraElements()
    {
        // The box holds both arcs: one on its left edge, the other on its right edge (like the auras of the Black Mage).
        ThreefoldElement = plugin.Layout.Register("gauge.dnc.threefold", "Dancer Fan Dance III auras", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 120,
            Width = 300,
            Height = 180
        });

        // Above the character, like the arch of Thunderhead (the two jobs never meet).
        FourfoldElement = plugin.Layout.Register("gauge.dnc.fourfold", "Dancer Fan Dance IV aura", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 0,
            Width = 150,
            Height = 90
        });
    }

    private static void FindDancerIds()
    {
        if (dancerIdsFound)
            return;

        threefoldId = TrackerPresets.FindStatus("Threefold Fan Dance");
        fourfoldId = TrackerPresets.FindStatus("Fourfold Fan Dance");
        dancerIdsFound = true;
    }

    // The real statuses while playing; a sample of both while the page is open or the elements are unlocked, so they can be placed.
    private void DrawDancerAuras(DancerAuraSettings settings, UnitSnapshot? player, uint previewJob, bool unlock, bool isDancer)
    {
        var shown = previewJob == DancerJobId || (isDancer && (unlock || player is not null));
        if (!shown)
            return;

        var sample = previewJob == DancerJobId || unlock || player is null;

        FindDancerIds();
        var threefold = sample || HasStatus(player!, threefoldId);
        var fourfold = sample || HasStatus(player!, fourfoldId);

        if (settings.ShowThreefoldAura && threefold)
            Cover(sample, () => DrawThreefoldAura(settings), ThreefoldElement);

        if (settings.ShowFourfoldAura && fourfold)
            Cover(sample, () => DrawFourfoldAura(settings), FourfoldElement);
    }

    // The two feather arcs: one at the left edge of the box and the mirrored one at the right edge, each as tall as the box.
    private void DrawThreefoldAura(DancerAuraSettings settings)
    {
        if (!LayoutManager.IsShownNow(ThreefoldElement)
            || !auraTextures.TryGetValue("fan_dance_3_left", out var left) || !auraTextures.TryGetValue("fan_dance_3_right", out var right))
            return;

        var (pos, size) = LayoutManager.GetRect(ThreefoldElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(MathF.Round(size.Y * ThreefoldAspect), MathF.Round(size.X / 2f));
        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.ThreefoldAuraOpacity, 0f, 1f)));
        var end = pos + size;
        var drawList = ImGui.GetBackgroundDrawList();

        drawList.AddImage(left.Handle, pos, new Vector2(pos.X + width, end.Y), new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
        drawList.AddImage(right.Handle, new Vector2(end.X - width, pos.Y), end, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
    }

    // The arch keeps the proportions of its image inside the box.
    private void DrawFourfoldAura(DancerAuraSettings settings)
    {
        if (!LayoutManager.IsShownNow(FourfoldElement) || !auraTextures.TryGetValue("fan_dance_4", out var texture))
            return;

        var (pos, size) = LayoutManager.GetRect(FourfoldElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(size.X, MathF.Round(size.Y * FourfoldAspect));
        var height = MathF.Round(width / FourfoldAspect);
        var min = new Vector2(MathF.Round(pos.X + (size.X - width) / 2f), MathF.Round(pos.Y + (size.Y - height) / 2f));

        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.FourfoldAuraOpacity, 0f, 1f)));
        ImGui.GetBackgroundDrawList().AddImage(texture.Handle, min, min + new Vector2(width, height), new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
    }
}
