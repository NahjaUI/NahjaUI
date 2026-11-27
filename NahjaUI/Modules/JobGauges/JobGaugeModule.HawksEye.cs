using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Game;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// The aura of Hawk's Eye of the Bard (Heavy Shot, the DoTs and the other basic shots have a chance to grant it, and it makes Straight Shot / Refulgent Arrow
/// and Shadowbite usable). One arch of electric blue feathers above the character, like the arch of Freecure. It works as a timer: the desaturated arch is
/// always there and the arch in color is cut from the right toward the left while the 30 seconds run out.
/// </summary>
internal sealed partial class JobGaugeModule
{
    private const uint BardJobId = 23;

    // The duration of the status (the time left over this is the part in color).
    private const float HawksEyeSeconds = 30f;

    // The width of the arch over its height (the image is 960 x 360).
    private const float HawksEyeAspect = 960f / 360f;

    public LayoutElement HawksEyeElement { get; private set; } = null!;

    private long hawksEyeFirstSeen;
    private bool hawksEyeTimeKnown;
    private float hawksEyeLastRemaining;

    // The status Hawk's Eye of the game (3861: the table has an older one with the same name that never shows up), found by name once.
    private static uint hawksEyeId;

    private void RegisterHawksEyeElement()
    {
        // Above the character, like the arches of Thunderhead and Freecure (the jobs never meet).
        HawksEyeElement = plugin.Layout.Register("gauge.brd.hawkseye", "Bard Hawk's Eye aura", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 0,
            Width = 150,
            Height = 90
        });
    }

    // The Archer has Hawk's Eye too (Heavy Shot), so it shows the aura like the Bard does.
    private static bool IsBardOrArcher(UnitSnapshot? player)
        => player is null || NahjaUI.Game.GameNames.ProfileJobId(player.JobId) == BardJobId;

    // The real status while playing; a sample while the page is open or the elements are unlocked, so it can be placed.
    private void DrawHawksEyeAura(BardAuraSettings settings, UnitSnapshot? player, uint previewJob, bool unlock)
    {
        if (!settings.ShowHawksEyeAura)
            return;

        var isBard = IsBardOrArcher(player);
        var shown = previewJob == BardJobId || (isBard && (unlock || player is not null));
        if (!shown)
            return;

        var sample = previewJob == BardJobId || unlock || player is null;
        var fraction = 1f;
        if (!sample)
        {
            if (hawksEyeId == 0)
                hawksEyeId = NahjaUI.Modules.Trackers.TrackerPresets.FindStatus("Hawk's Eye");

            var found = false;
            foreach (var status in player!.Statuses)
            {
                if (status.Id == hawksEyeId)
                {
                    fraction = TimedFraction(status.RemainingTime, HawksEyeSeconds, ref hawksEyeFirstSeen, ref hawksEyeTimeKnown, ref hawksEyeLastRemaining);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                hawksEyeFirstSeen = 0;
                hawksEyeTimeKnown = false;
                hawksEyeLastRemaining = 0f;
                return;
            }
        }

        Cover(sample, () => DrawHawksEye(settings, fraction), HawksEyeElement);
    }

    // The arch keeps the proportions of its image inside the box.
    private void DrawHawksEye(BardAuraSettings settings, float fraction)
    {
        if (!LayoutManager.IsShownNow(HawksEyeElement)
            || !auraTextures.TryGetValue("hawks_eye", out var texture) || !auraTextures.TryGetValue("hawks_eye_gray", out var gray))
            return;

        var (pos, size) = LayoutManager.GetRect(HawksEyeElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(size.X, MathF.Round(size.Y * HawksEyeAspect));
        var height = MathF.Round(width / HawksEyeAspect);
        var min = new Vector2(MathF.Round(pos.X + (size.X - width) / 2f), MathF.Round(pos.Y + (size.Y - height) / 2f));

        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.HawksEyeAuraOpacity, 0f, 1f)));
        var max = min + new Vector2(width, height);
        var drawList = ImGui.GetBackgroundDrawList();

        // The desaturated arch is always there.
        drawList.AddImage(gray.Handle, min, max, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);

        // The part in color is what is left of the time: it is cut from the right edge toward the left as the seconds run out.
        if (fraction <= 0f)
            return;

        var colorEnd = min.X + width * Math.Clamp(fraction, 0f, 1f);

        drawList.PushClipRect(min, new Vector2(colorEnd, max.Y), true);
        drawList.AddImage(texture.Handle, min, max, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
        drawList.PopClipRect();
    }
}
