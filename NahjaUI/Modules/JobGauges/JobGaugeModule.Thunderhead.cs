using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Game;
using NahjaUI.Modules.Trackers;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// The auras of the Black Mage procs. They have no timer and no desaturated copy: each one is drawn while the status of the player is on and it
/// is not drawn otherwise. Thunderhead (the next Thunder is instant) is one arch above the character; Firestarter (the next Fire III is instant
/// and free) is two arcs, one at each side, like the wings of the Warrior.
/// </summary>
internal sealed partial class JobGaugeModule
{
    // The width of the Thunderhead arch over its height (the image is 960 x 360).
    private const float ThunderheadAspect = 960f / 360f;

    private static uint thunderheadId;
    private static uint firestarterId;
    private static bool blackMageIdsFound;

    public LayoutElement ThunderheadElement { get; private set; } = null!;

    public LayoutElement FirestarterElement { get; private set; } = null!;

    private void RegisterThunderheadElement()
    {
        // Above the character, like the arch of the Warrior (the two jobs never meet).
        ThunderheadElement = plugin.Layout.Register("gauge.blm.thunderhead", "Black Mage Thunderhead aura", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = -190,
            Width = 240,
            Height = 90
        });

        // The box holds both arcs: one on its left edge, the other on its right edge (like the proc auras of the Warrior).
        FirestarterElement = plugin.Layout.Register("gauge.blm.firestarter", "Black Mage Firestarter auras", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 20,
            Width = 380,
            Height = 270
        });
    }

    private static void FindBlackMageIds()
    {
        if (blackMageIdsFound)
            return;

        thunderheadId = TrackerPresets.FindStatus("Thunderhead");
        firestarterId = TrackerPresets.FindStatus("Firestarter");
        blackMageIdsFound = true;
    }

    private static bool HasStatus(UnitSnapshot player, uint statusId)
    {
        if (statusId == 0)
            return false;

        foreach (var status in player.Statuses)
        {
            if (status.Id == statusId)
                return true;
        }

        return false;
    }

    // The real statuses while playing; a sample of both while the page is open or the elements are unlocked, so they can be placed.
    // They work for the Thaumaturge too (isBlackMage counts the class before the job): the procs come before the job does.
    private void DrawBlackMageAuras(BlackMageAuraSettings settings, UnitSnapshot? player, uint previewJob, bool unlock, bool isBlackMage)
    {
        var shown = previewJob == BlackMageJobId || (isBlackMage && (unlock || player is not null));
        if (!shown)
            return;

        var sample = previewJob == BlackMageJobId || unlock || player is null;

        FindBlackMageIds();
        var thunderhead = sample || HasStatus(player!, thunderheadId);
        var firestarter = sample || HasStatus(player!, firestarterId);

        if (settings.ShowThunderheadAura && thunderhead)
            Cover(sample, () => DrawThunderheadAura(settings), ThunderheadElement);

        if (settings.ShowFirestarterAura && firestarter)
            Cover(sample, () => DrawFirestarterAura(settings), FirestarterElement);
    }

    // The arch keeps the proportions of its image inside the box.
    private void DrawThunderheadAura(BlackMageAuraSettings settings)
    {
        if (!LayoutManager.IsShownNow(ThunderheadElement) || !auraTextures.TryGetValue("thunderhead", out var texture))
            return;

        var (pos, size) = LayoutManager.GetRect(ThunderheadElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(size.X, MathF.Round(size.Y * ThunderheadAspect));
        var height = MathF.Round(width / ThunderheadAspect);
        var min = new Vector2(MathF.Round(pos.X + (size.X - width) / 2f), MathF.Round(pos.Y + (size.Y - height) / 2f));

        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.ThunderheadAuraOpacity, 0f, 1f)));
        ImGui.GetBackgroundDrawList().AddImage(texture.Handle, min, min + new Vector2(width, height), new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
    }

    // The two fire arcs: one at the left edge of the box and the mirrored one at the right edge, each as tall as the box (images of 2:3).
    private void DrawFirestarterAura(BlackMageAuraSettings settings)
    {
        if (!LayoutManager.IsShownNow(FirestarterElement)
            || !auraTextures.TryGetValue("firestarter_left", out var left) || !auraTextures.TryGetValue("firestarter_right", out var right))
            return;

        var (pos, size) = LayoutManager.GetRect(FirestarterElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(MathF.Round(size.Y * AuraAspect), MathF.Round(size.X / 2f));
        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.FirestarterAuraOpacity, 0f, 1f)));
        var end = pos + size;
        var drawList = ImGui.GetBackgroundDrawList();

        drawList.AddImage(left.Handle, pos, new Vector2(pos.X + width, end.Y), new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
        drawList.AddImage(right.Handle, new Vector2(end.X - width, pos.Y), end, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
    }
}
