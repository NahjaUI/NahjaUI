using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Game;
using NahjaUI.Modules.Trackers;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// The aura of Sacred Sight of the White Mage (Glare IV x3 after Presence of Mind). Two flame arcs, one at each side of the character, each with three sigils:
/// the sigils that are lit are the stacks left (3, 2 or 1). It works as a timer, like the wings of the Warrior: the desaturated arcs are always there
/// and the arcs in color shrink from the top down while the 30 seconds run out. The image of each number of stacks has the lightning all along the arc
/// (only the sigils change), so the gray part reads the same with three stacks as with one.
/// </summary>
internal sealed partial class JobGaugeModule
{
    // The width of each arc over its height (the images are 360 x 768).
    private const float SacredSightAspect = 360f / 768f;

    // The duration of the status (the time left over this is the part in color).
    private const float SacredSightSeconds = 30f;

    private static uint sacredSightId;
    private static bool sacredSightIdFound;

    private long sacredSightFirstSeen;
    private bool sacredSightTimeKnown;
    private float sacredSightLastRemaining;

    public LayoutElement SacredSightElement { get; private set; } = null!;

    private void RegisterSacredSightElement()
    {
        // The box holds both arcs: one on its left edge, the other on its right edge (like the Firestarter auras of the Black Mage; the two jobs never meet).
        SacredSightElement = plugin.Layout.Register("gauge.whm.sacredsight", "White Mage Sacred Sight auras", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 120,
            Width = 300,
            Height = 180
        });
    }

    // The real status while playing; a sample (three stacks) while the page is open or the elements are unlocked, so it can be placed.
    private void DrawSacredSightAura(WhiteMageAuraSettings settings, UnitSnapshot? player, uint previewJob, bool unlock, bool isWhiteMage)
    {
        if (!settings.ShowSacredSightAura)
            return;

        var shown = previewJob == WhiteMageJobId || (isWhiteMage && (unlock || player is not null));
        if (!shown)
            return;

        var sample = previewJob == WhiteMageJobId || unlock || player is null;

        var stacks = 3;
        var fraction = 1f;
        if (!sample)
        {
            if (!sacredSightIdFound)
            {
                sacredSightId = TrackerPresets.FindStatus("Sacred Sight");
                sacredSightIdFound = true;
            }

            stacks = 0;
            if (sacredSightId != 0)
            {
                foreach (var status in player!.Statuses)
                {
                    if (status.Id == sacredSightId)
                    {
                        // The stacks are in the parameter of the status; a status that does not report them counts as the full three.
                        stacks = status.Param is >= 1 and <= 3 ? status.Param : 3;
                        fraction = TimedFraction(status.RemainingTime, SacredSightSeconds, ref sacredSightFirstSeen, ref sacredSightTimeKnown, ref sacredSightLastRemaining);
                        break;
                    }
                }
            }
        }

        if (stacks == 0)
        {
            sacredSightFirstSeen = 0;
            sacredSightTimeKnown = false;
            sacredSightLastRemaining = 0f;
            return;
        }

        Cover(sample, () => DrawSacredSight(settings, stacks, fraction), SacredSightElement);
    }

    private void DrawSacredSight(WhiteMageAuraSettings settings, int stacks, float fraction)
    {
        if (!LayoutManager.IsShownNow(SacredSightElement)
            || !auraTextures.TryGetValue($"sacred_sight_{stacks}_stacks_left", out var left)
            || !auraTextures.TryGetValue($"sacred_sight_{stacks}_stacks_right", out var right)
            || !auraTextures.TryGetValue($"sacred_sight_{stacks}_stacks_left_gray", out var leftGray)
            || !auraTextures.TryGetValue($"sacred_sight_{stacks}_stacks_right_gray", out var rightGray))
            return;

        var (pos, size) = LayoutManager.GetRect(SacredSightElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(MathF.Round(size.Y * SacredSightAspect), MathF.Round(size.X / 2f));
        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.SacredSightAuraOpacity, 0f, 1f)));
        var end = pos + size;
        var drawList = ImGui.GetBackgroundDrawList();

        var leftMax = new Vector2(pos.X + width, end.Y);
        var rightMin = new Vector2(end.X - width, pos.Y);

        drawList.AddImage(leftGray.Handle, pos, leftMax, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
        drawList.AddImage(rightGray.Handle, rightMin, end, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);

        // The part in color is what is left of the time: its top edge goes down as the seconds run out.
        var top = end.Y - size.Y * Math.Clamp(fraction, 0f, 1f);

        drawList.PushClipRect(new Vector2(pos.X, top), leftMax, true);
        drawList.AddImage(left.Handle, pos, leftMax, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
        drawList.PopClipRect();

        drawList.PushClipRect(new Vector2(rightMin.X, top), end, true);
        drawList.AddImage(right.Handle, rightMin, end, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
        drawList.PopClipRect();
    }
}
