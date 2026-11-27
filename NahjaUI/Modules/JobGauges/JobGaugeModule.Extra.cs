using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Modules.JobGauges;

/// <summary>The generic gauges of the melee, physical ranged and caster jobs (see ExtraGauges): the elements, and the drawing of bars, pips and slots.</summary>
internal sealed partial class JobGaugeModule
{
    // The rows of each job, by abbreviation, in the order of its definition.
    private readonly Dictionary<string, LayoutElement[]> extraElements = new();

    private void RegisterExtraElements()
    {
        foreach (var def in ExtraGauges.All)
        {
            var elements = new LayoutElement[def.Parts.Length];

            for (var i = 0; i < def.Parts.Length; i++)
            {
                var part = def.Parts[i];
                elements[i] = plugin.Layout.Register($"gauge.{def.Abbr.ToLowerInvariant()}.{part.Id}", $"{def.Name} {part.Label}", new ElementLayout
                {
                    ScreenAnchor = AnchorPoint.Center,
                    SelfAnchor = AnchorPoint.Center,
                    OffsetX = 0,
                    OffsetY = 230 + i * 7,
                    Width = 240,
                    Height = part.Height
                });
            }

            extraElements[def.Abbr] = elements;
        }
    }

    /// <summary>The rows of the generic gauge of a job, from the top one to the bottom one (for the job setups: the column puts them in place).</summary>
    public LayoutElement[] ElementsOf(string abbreviation)
        => extraElements.TryGetValue(abbreviation, out var elements) ? elements : Array.Empty<LayoutElement>();

    private void HideExtraGauges()
    {
        foreach (var elements in extraElements.Values)
        {
            foreach (var element in elements)
                element.IsHidden = true;
        }
    }

    private void DrawExtraGauges(GameSnapshot snapshot, UnitSnapshot? player, uint previewJob, bool unlock)
    {
        foreach (var def in ExtraGauges.All)
        {
            var settings = plugin.Configuration.JobGauges.Extra.For(def.Abbr);
            var elements = extraElements[def.Abbr];

            // Unlock mode only offers the rows of the job you are playing (all of them while the job is unknown) that are not switched off.
            // The Thaumaturge (7) has the gauge of the Black Mage (25).
            var isJob = player is null || player.JobId == def.JobId || (def.JobId == 25 && player.JobId == 7) || (def.JobId == 23 && player.JobId == 5);
            for (var i = 0; i < elements.Length; i++)
                elements[i].IsHidden = !isJob || !settings.Part(def.Parts[i].Id).Show;

            if (!settings.Enabled)
                continue;

            var live = snapshot.ExtraGauge is { } extra && extra.JobId == def.JobId ? extra.Parts : null;
            var parts = live ?? (previewJob == def.JobId || (unlock && isJob) ? def.Sample : null);
            if (parts is null)
                continue;

            // A row the job has not got at this moment (the Repertoire in the Ballad, the rows not learned yet) is not drawn and its place in the column is
            // given back; in unlock mode all the rows stay so they can be placed.
            if (live is not null && !unlock)
            {
                for (var i = 0; i < elements.Length; i++)
                {
                    var id = def.Parts[i].Id;
                    if (!live.Any(p => p.Id == id))
                        elements[i].IsHidden = true;
                }
            }

            var shown = elements.Where((_, i) => settings.Part(def.Parts[i].Id).Show).ToArray();
            if (shown.Length == 0)
                continue;

            Cover(live is null, () => DrawExtraGauge(def, settings, elements, parts), shown);
        }
    }

    private void DrawExtraGauge(ExtraGaugeDef def, ExtraGaugeJobSettings settings, LayoutElement[] elements, IReadOnlyList<GaugePartSnapshot> parts)
    {
        var drawList = ImGui.GetBackgroundDrawList();

        for (var i = 0; i < def.Parts.Length; i++)
        {
            var partDef = def.Parts[i];
            if (!settings.Part(partDef.Id).Show)
                continue;

            var part = parts.FirstOrDefault(p => p.Id == partDef.Id);
            if (part is null)
                continue;

            if (!LayoutManager.IsShownNow(elements[i]))
                continue;

            var (pos, size) = LayoutManager.GetRect(elements[i].Layout);
            if (size.X < 2f || size.Y < 2f)
                continue;

            switch (part.Kind)
            {
                case GaugePartKind.Bar:
                    DrawExtraBar(drawList, settings, partDef, part, pos, size);
                    break;

                case GaugePartKind.Pips:
                {
                    var count = Math.Max(1, (int)part.Max);
                    var filled = (int)MathF.Floor(part.Value);
                    var slots = new int[count];
                    for (var s = 0; s < count; s++)
                        slots[s] = s < filled ? part.Variant : -1;

                    var partial = filled < count && part.Partial > 0f ? part.Partial : 0f;
                    DrawExtraSlots(drawList, settings, partDef, pos, size, slots, filled, partial, part.Variant, part.Text);
                    break;
                }

                default:
                    DrawExtraSlots(drawList, settings, partDef, pos, size, SlotsShown(part), -1, 0f, 0, part.Text, part.Dim);
                    break;
            }
        }
    }

    // A rounded bar filled from the left, with the marks where a spender can be used and the text over it. A row can hold two bars side by side (the DoTs
    // of the Bard): the second one is there when Value2 is not negative, and the two share the width with a gap between them.
    private void DrawExtraBar(ImDrawListPtr drawList, ExtraGaugeJobSettings settings, GaugePartDef def, GaugePartSnapshot part, Vector2 pos, Vector2 size)
    {
        if (part.Value2 < 0f)
        {
            DrawOneBar(drawList, settings, def, pos, size, part.Value, part.Max, part.Variant, part.Alert, part.Text, part.Marks, part.Flash);
            return;
        }

        var gap = GapPixels(settings.Part(def.Id).GapPx ?? settings.GapPx, UiScale.Overlay);
        var half = MathF.Round((size.X - gap) / 2f);
        var other = new Vector2(pos.X + half + gap, pos.Y);

        DrawOneBar(drawList, settings, def, pos, new Vector2(half, size.Y), part.Value, part.Max, part.Variant, part.Alert, part.Text, part.Marks, part.Flash);
        DrawOneBar(drawList, settings, def, other, new Vector2(MathF.Max(1f, pos.X + size.X - other.X), size.Y), part.Value2, part.Max, part.Variant2, part.Alert2, part.Text2, part.Marks, part.Flash);
    }

    private void DrawOneBar(ImDrawListPtr drawList, ExtraGaugeJobSettings settings, GaugePartDef def, Vector2 pos, Vector2 size, float value, float max, int variant, bool alert, string? text, float[]? marks, float flash = 0f)
    {
        var scale = UiScale.Overlay;
        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var end = pos + size;

        drawList.AddRectFilled(pos, end, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        // The buff that the bar follows is missing: the whole bar pulses in red.
        if (alert)
        {
            var pulse = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 5f));
            drawList.AddRectFilled(pos, end, ImGui.GetColorU32(new Vector4(0.85f, 0.12f, 0.12f, pulse)), rounding);
        }

        var fraction = max <= 0f ? 0f : Math.Clamp(value / max, 0f, 1f);
        var fillWidth = MathF.Round(size.X * fraction);
        if (fillWidth > 0f)
        {
            // Clipped to the bar so the fill keeps its rounded outline.
            drawList.PushClipRect(pos, new Vector2(pos.X + fillWidth, end.Y), true);
            BarGradient.Fill(drawList, pos, end, ImGui.GetColorU32(settings.ColorOf(def, variant)), rounding, ImDrawFlags.None, pos.X, pos.X + fillWidth);
            drawList.PopClipRect();
        }

        // The marks of the moment (the snapshot) win over the fixed ones of the row (its definition).
        var shownMarks = marks ?? def.Marks;
        if (shownMarks is not null)
        {
            foreach (var mark in shownMarks)
            {
                var x = pos.X + MathF.Round(size.X * mark);
                drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, end.Y), ImGui.GetColorU32(settings.MarkColor), thickness);
            }
        }

        // A white flash that fades out (the proc of the Mage's Ballad).
        if (flash > 0f)
            drawList.AddRectFilled(pos, end, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.75f * Math.Clamp(flash, 0f, 1f))), rounding);

        BorderStyle.BarRect(drawList, pos, end, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        if (!string.IsNullOrEmpty(text))
            QueueText(drawList, text, settings.Text, pos, size);
    }

    // The slots of a row of steps: a slot is lit when its bit is in Highlights, at 33 % when it is in Dim, and off (only the background) otherwise.
    private static int[] SlotsShown(GaugePartSnapshot part)
    {
        var colours = part.SlotColors ?? Array.Empty<int>();
        var shown = new int[colours.Length];

        for (var i = 0; i < colours.Length; i++)
        {
            var bit = i < 31 ? 1 << i : 0;
            shown[i] = (part.Highlights & bit) != 0 || (part.Dim & bit) != 0 ? colours[i] : -1;
        }

        return shown;
    }

    // Slots side by side. Each slot colour is an index into the row's colours (-1 is an empty slot). In a row of pips the next slot after
    // the full ones (index <filled>) is filled by <partial> from the left, like a small bar.
    // dim: one bit per slot; the slots with their bit on are drawn at 33 % of their colour (the next one to light up).
    private void DrawExtraSlots(ImDrawListPtr drawList, ExtraGaugeJobSettings settings, GaugePartDef def, Vector2 pos, Vector2 size, int[] slots, int filled, float partial, int partialVariant, string? text, int dim = 0)
    {
        if (slots.Length == 0)
            return;

        var scale = UiScale.Overlay;
        var gap = GapPixels(settings.Part(def.Id).GapPx ?? settings.GapPx, scale);
        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        var width = MathF.Floor((size.X - gap * (slots.Length - 1)) / slots.Length);
        if (width < 1f || size.Y < 1f)
            return;

        var total = SlotsTotal(slots.Length, size.X, gap);
        var left = pos.X + MathF.Round((size.X - total) / 2f);
        var background = ImGui.GetColorU32(settings.BackgroundColor);
        var border = ImGui.GetColorU32(settings.BorderColor);

        for (var i = 0; i < slots.Length; i++)
        {
            var a = new Vector2(left + SlotAt(i, slots.Length, size.X, gap).Offset, pos.Y);
            var b = new Vector2(a.X + SlotAt(i, slots.Length, size.X, gap).Width, pos.Y + size.Y);

            drawList.AddRectFilled(a, b, background, rounding);

            if (slots[i] >= 0)
            {
                var colour = settings.ColorOf(def, slots[i]);
                if (i < 31 && (dim & (1 << i)) != 0)
                    colour = Theme.WithAlpha(colour, 0.33f);

                BarGradient.Fill(drawList, a, b, ImGui.GetColorU32(colour), rounding, ImDrawFlags.None, left, left + total);
            }
            else if (i == filled && partial > 0f)
            {
                var right = a.X + MathF.Round(SlotAt(i, slots.Length, size.X, gap).Width * partial);
                if (right > a.X)
                    drawList.AddRectFilled(a, new Vector2(right, b.Y), ImGui.GetColorU32(Theme.WithAlpha(settings.ColorOf(def, partialVariant), 0.65f)), rounding);
            }

            BorderStyle.BarRect(drawList, a, b, border, rounding, ImDrawFlags.None, thickness);
        }

        if (!string.IsNullOrEmpty(text))
            QueueText(drawList, text, settings.Text, pos, size);
    }
}
