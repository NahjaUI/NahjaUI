using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Game;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// The aura of Freecure of the White Mage (Cure has a chance to make the next Cure II cost no MP). One soft arch of teal mist with faint leaves and vines,
/// above the character, like the arch of Thunderhead of the Black Mage. It works as a timer, like Sacred Sight: the desaturated arch is always there and
/// the arch in color is cut from the right toward the left while the 15 seconds run out (what is left in color is the left part).
/// </summary>
internal sealed partial class JobGaugeModule
{
    // The status Freecure of the game.
    private const uint FreeCureStatusId = 155;

    // The width of the arch over its height (the image is 960 x 365).
    private const float FreeCureAspect = 960f / 365f;

    // The duration of the status (the time left over this is the part in color).
    private const float FreeCureSeconds = 15f;

    public LayoutElement FreeCureElement { get; private set; } = null!;

    private void RegisterFreeCureElement()
    {
        // Above the character, like the arch of Thunderhead (the two jobs never meet).
        FreeCureElement = plugin.Layout.Register("gauge.whm.freecure", "White Mage Freecure aura", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = -190,
            Width = 240,
            Height = 92
        });
    }

    // The Conjurer has Cure too, so it shows the aura like the White Mage does (the proc comes before the job does).
    private static bool IsWhiteMageOrConjurer(UnitSnapshot? player)
        => player is null || NahjaUI.Game.GameNames.ProfileJobId(player.JobId) == WhiteMageJobId;

    // The real status while playing; a sample while the page is open or the elements are unlocked, so it can be placed.
    private void DrawFreeCureAura(WhiteMageAuraSettings settings, UnitSnapshot? player, uint previewJob, bool unlock)
    {
        if (!settings.ShowFreeCureAura)
            return;

        var isWhiteMage = IsWhiteMageOrConjurer(player);
        var shown = previewJob == WhiteMageJobId || (isWhiteMage && (unlock || player is not null));
        if (!shown)
            return;

        var sample = previewJob == WhiteMageJobId || unlock || player is null;
        var fraction = 1f;
        if (!sample)
        {
            var found = false;
            foreach (var status in player!.Statuses)
            {
                if (status.Id == FreeCureStatusId)
                {
                    fraction = Math.Clamp(status.RemainingTime / FreeCureSeconds, 0f, 1f);
                    found = true;
                    break;
                }
            }

            if (!found)
                return;
        }

        Cover(sample, () => DrawFreeCure(settings, fraction), FreeCureElement);
    }

    // The arch keeps the proportions of its image inside the box.
    private void DrawFreeCure(WhiteMageAuraSettings settings, float fraction)
    {
        if (!LayoutManager.IsShownNow(FreeCureElement)
            || !auraTextures.TryGetValue("free_cure", out var texture) || !auraTextures.TryGetValue("free_cure_gray", out var gray))
            return;

        var (pos, size) = LayoutManager.GetRect(FreeCureElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(size.X, MathF.Round(size.Y * FreeCureAspect));
        var height = MathF.Round(width / FreeCureAspect);
        var min = new Vector2(MathF.Round(pos.X + (size.X - width) / 2f), MathF.Round(pos.Y + (size.Y - height) / 2f));

        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.FreeCureAuraOpacity, 0f, 1f)));
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
