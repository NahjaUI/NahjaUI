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
            var isJob = player is null || player.JobId == def.JobId;
            for (var i = 0; i < elements.Length; i++)
                elements[i].IsHidden = !isJob || !settings.Part(def.Parts[i].Id).Show;

            if (!settings.Enabled)
                continue;

            var live = snapshot.ExtraGauge is { } extra && extra.JobId == def.JobId ? extra.Parts : null;
            var parts = live ?? (previewJob == def.JobId || (unlock && isJob) ? def.Sample : null);
            if (parts is null)
                continue;

            var shown = elements.Where((_, i) => settings.Part(def.Parts[i].Id).Show).ToArray();
            if (shown.Length == 0)
                continue;

            Cover(() => DrawExtraGauge(def, settings, elements, parts), shown);
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
                    DrawExtraSlots(drawList, settings, partDef, pos, size, part.SlotColors ?? Array.Empty<int>(), -1, 0f, 0, part.Text);
                    break;
            }
        }
    }

    // A rounded bar filled from the left, with the marks where a spender can be used and the text over it.
    private void DrawExtraBar(ImDrawListPtr drawList, ExtraGaugeJobSettings settings, GaugePartDef def, GaugePartSnapshot part, Vector2 pos, Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var end = pos + size;

        drawList.AddRectFilled(pos, end, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        var fraction = part.Max <= 0f ? 0f : Math.Clamp(part.Value / part.Max, 0f, 1f);
        var fillWidth = MathF.Round(size.X * fraction);
        if (fillWidth > 0f)
        {
            // Clipped to the bar so the fill keeps its rounded outline.
            drawList.PushClipRect(pos, new Vector2(pos.X + fillWidth, end.Y), true);
            drawList.AddRectFilled(pos, end, ImGui.GetColorU32(settings.ColorOf(def, part.Variant)), rounding);
            drawList.PopClipRect();
        }

        if (def.Marks is not null)
        {
            foreach (var mark in def.Marks)
            {
                var x = pos.X + MathF.Round(size.X * mark);
                drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, end.Y), ImGui.GetColorU32(settings.MarkColor), thickness);
            }
        }

        drawList.AddRect(pos, end, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        if (!string.IsNullOrEmpty(part.Text))
            plugin.HudBarSystem.DrawText(drawList, part.Text, settings.Text, pos, size, OverlayFont.NahjaDisplayBold);
    }

    // Slots side by side. Each slot colour is an index into the row's colours (-1 is an empty slot). In a row of pips the next slot after
    // the full ones (index <filled>) is filled by <partial> from the left, like a small bar.
    private void DrawExtraSlots(ImDrawListPtr drawList, ExtraGaugeJobSettings settings, GaugePartDef def, Vector2 pos, Vector2 size, int[] slots, int filled, float partial, int partialVariant, string? text)
    {
        if (slots.Length == 0)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var gap = MathF.Round(settings.GapPx * scale);
        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        var width = MathF.Floor((size.X - gap * (slots.Length - 1)) / slots.Length);
        if (width < 1f || size.Y < 1f)
            return;

        var total = width * slots.Length + gap * (slots.Length - 1);
        var left = pos.X + MathF.Round((size.X - total) / 2f);
        var background = ImGui.GetColorU32(settings.BackgroundColor);
        var border = ImGui.GetColorU32(settings.BorderColor);

        for (var i = 0; i < slots.Length; i++)
        {
            var a = new Vector2(left + i * (width + gap), pos.Y);
            var b = new Vector2(a.X + width, pos.Y + size.Y);

            drawList.AddRectFilled(a, b, background, rounding);

            if (slots[i] >= 0)
            {
                drawList.AddRectFilled(a, b, ImGui.GetColorU32(settings.ColorOf(def, slots[i])), rounding);
            }
            else if (i == filled && partial > 0f)
            {
                var right = a.X + MathF.Round(width * partial);
                if (right > a.X)
                    drawList.AddRectFilled(a, new Vector2(right, b.Y), ImGui.GetColorU32(Theme.WithAlpha(settings.ColorOf(def, partialVariant), 0.65f)), rounding);
            }

            drawList.AddRect(a, b, border, rounding, ImDrawFlags.None, thickness);
        }

        if (!string.IsNullOrEmpty(text))
            plugin.HudBarSystem.DrawText(drawList, text, settings.Text, pos, size, OverlayFont.NahjaDisplayBold);
    }
}
