using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Game;
using NahjaUI.Modules.Trackers;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// The auras of Verfire Ready and Verstone Ready of the Red Mage. Two flame arcs, one at each side of the character. With only one of the two procs on, both
/// arcs are of that proc; with both on, Verstone is the left arc and Verfire the right one. Each works as a timer, like Sacred Sight: the desaturated arc is
/// always there and the arc in color shrinks from the top down while the 30 seconds run out. Until the game gives the time, the arc is entirely in color.
/// </summary>
internal sealed partial class JobGaugeModule
{
    // The width of each arc over its height (the images are 360 x 768).
    private const float RedMageAuraAspect = 360f / 768f;

    // The duration of both statuses.
    private const float RedMageProcSeconds = 30f;

    // How much of its brightness the desaturated Verstone keeps (1 = the image as it is). Lower it to make the spent time stand out more.
    private const float VerstoneGrayLevel = 0.45f;

    private static uint verfireReadyId;
    private static uint verstoneReadyId;
    private static bool redMageProcIdsFound;

    private long verfireFirstSeen;
    private bool verfireTimeKnown;
    private long verstoneFirstSeen;
    private bool verstoneTimeKnown;

    public LayoutElement RedProcAurasElement { get; private set; } = null!;

    private void RegisterRedMageAuraElement()
    {
        // The box holds both arcs: one on its left edge, the other on its right edge (like the auras of the White Mage and the Black Mage).
        RedProcAurasElement = plugin.Layout.Register("gauge.rdm.procauras", "Red Mage Verfire and Verstone auras", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 120,
            Width = 300,
            Height = 180
        });
    }

    // The real statuses while playing; a sample (both on) while the page is open or the elements are unlocked, so it can be placed.
    private void DrawRedMageProcAuras(RedMageGaugeSettings settings, UnitSnapshot? player, uint previewJob, bool unlock, bool isRedMage)
    {
        if (!settings.ShowProcAuras)
            return;

        var shown = previewJob == RedMageJobId || (isRedMage && (unlock || player is not null));
        if (!shown)
            return;

        var sample = previewJob == RedMageJobId || unlock || player is null;

        // The part of the time left of each proc (1 = all of it), or -1 while that proc is not on.
        var verfire = 1f;
        var verstone = 1f;
        if (!sample)
        {
            if (!redMageProcIdsFound)
            {
                verfireReadyId = TrackerPresets.FindStatus("Verfire Ready");
                verstoneReadyId = TrackerPresets.FindStatus("Verstone Ready");
                redMageProcIdsFound = true;
            }

            verfire = -1f;
            verstone = -1f;
            foreach (var status in player!.Statuses)
            {
                if (verfireReadyId != 0 && status.Id == verfireReadyId)
                    verfire = TimedFraction(status.RemainingTime, RedMageProcSeconds, ref verfireFirstSeen, ref verfireTimeKnown);
                else if (verstoneReadyId != 0 && status.Id == verstoneReadyId)
                    verstone = TimedFraction(status.RemainingTime, RedMageProcSeconds, ref verstoneFirstSeen, ref verstoneTimeKnown);
            }

            if (verfire < 0f)
            {
                verfireFirstSeen = 0;
                verfireTimeKnown = false;
            }

            if (verstone < 0f)
            {
                verstoneFirstSeen = 0;
                verstoneTimeKnown = false;
            }

            if (verfire < 0f && verstone < 0f)
                return;
        }

        Cover(sample, () => DrawRedMageProcArcs(settings, verfire, verstone), RedProcAurasElement);
    }

    private void DrawRedMageProcArcs(RedMageGaugeSettings settings, float verfire, float verstone)
    {
        if (!LayoutManager.IsShownNow(RedProcAurasElement))
            return;

        // Only Verstone: its two arcs. Only Verfire: its two arcs. Both: Verstone on the left and Verfire on the right.
        var leftIsVerstone = verstone >= 0f;
        var rightIsVerfire = verfire >= 0f;
        var leftName = leftIsVerstone ? "verstone" : "verfire";
        var rightName = rightIsVerfire ? "verfire" : "verstone";
        var leftFraction = leftIsVerstone ? verstone : verfire;
        var rightFraction = rightIsVerfire ? verfire : verstone;

        if (!auraTextures.TryGetValue($"{leftName}_left", out var left)
            || !auraTextures.TryGetValue($"{leftName}_left_gray", out var leftGray)
            || !auraTextures.TryGetValue($"{rightName}_right", out var right)
            || !auraTextures.TryGetValue($"{rightName}_right_gray", out var rightGray))
            return;

        var (pos, size) = LayoutManager.GetRect(RedProcAurasElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(MathF.Round(size.Y * RedMageAuraAspect), MathF.Round(size.X / 2f));
        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.ProcAuraOpacity, 0f, 1f)));
        var end = pos + size;
        var drawList = ImGui.GetBackgroundDrawList();

        var leftMax = new Vector2(pos.X + width, end.Y);
        var rightMin = new Vector2(end.X - width, pos.Y);
        var uv0 = new Vector2(0f, 0f);
        var uv1 = new Vector2(1f, 1f);

        // The desaturated Verstone is almost white, so it hardly differs from the arc in color: its gray copy is drawn darker so the time that ran out is seen.
        var opacity = Math.Clamp(settings.ProcAuraOpacity, 0f, 1f);
        var leftGrayTint = leftName == "verstone" ? ImGui.GetColorU32(new Vector4(VerstoneGrayLevel, VerstoneGrayLevel, VerstoneGrayLevel, opacity)) : tint;
        var rightGrayTint = rightName == "verstone" ? ImGui.GetColorU32(new Vector4(VerstoneGrayLevel, VerstoneGrayLevel, VerstoneGrayLevel, opacity)) : tint;
        drawList.AddImage(leftGray.Handle, pos, leftMax, uv0, uv1, leftGrayTint);
        drawList.AddImage(rightGray.Handle, rightMin, end, uv0, uv1, rightGrayTint);

        // The part in color is what is left of the time of each proc: its top edge goes down as the seconds run out.
        var leftTop = end.Y - size.Y * Math.Clamp(leftFraction, 0f, 1f);
        drawList.PushClipRect(new Vector2(pos.X, leftTop), leftMax, true);
        drawList.AddImage(left.Handle, pos, leftMax, uv0, uv1, tint);
        drawList.PopClipRect();

        var rightTop = end.Y - size.Y * Math.Clamp(rightFraction, 0f, 1f);
        drawList.PushClipRect(new Vector2(rightMin.X, rightTop), end, true);
        drawList.AddImage(right.Handle, rightMin, end, uv0, uv1, tint);
        drawList.PopClipRect();
    }
}
