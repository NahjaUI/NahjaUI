using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.Trackers;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// What the Warrior shows besides the Beast Gauge: the Inner Release pips, the Surging Tempest bar, the flash of the Beast Gauge while
/// Nascent Chaos is on, the auras of Primal Rend and Primal Ruination (they empty like a timer), and the pips of the combo. Everything comes from the statuses of the player
/// and the combo of the game.
/// </summary>
internal sealed partial class JobGaugeModule
{
    // The statuses of the Warrior that the gauge reads, and the time of the Surging Tempest bar when it is full (the game caps it at 60 seconds).
    private const float TempestMaxSeconds = 60f;
    private const int InnerReleaseMaxStacks = 3;

    // The longest combo (Heavy Swing, Maim, Storm's Path or Storm's Eye): the pips that the empty outline shows.
    private const int ComboMaxSteps = 3;

    // The time that Primal Rend Ready and Primal Ruination Ready last when they start: the auras empty over it.
    private const float RendMaxSeconds = 30f;
    private const float RuinationMaxSeconds = 20f;
    private const float WrathMaxSeconds = 30f;

    // The width of an aura image over its height (the images are 360 x 540).
    private const float AuraAspect = 360f / 540f;

    // ComboLength is 0 while there is no combo of the Warrior going on; ComboDone is how many of its steps have been used.
    // AuraSeconds is the time left of the proc whose aura is shown (Primal Ruination Ready if both are on).
    private readonly record struct WarriorState(int InnerReleaseStacks, bool NascentChaos, float TempestSeconds, bool RendReady, bool RuinationReady, float AuraSeconds, int ComboLength, int ComboDone, bool WrathReady, float WrathSeconds);

    private static uint innerReleaseId;
    private static uint nascentChaosId;
    private static uint surgingTempestId;
    // The game can list a new status for a frame or two before its time is filled in (the time reads 0). Until a real time has been read once, the aura shows
    // the full time of its proc instead of an empty one. After that the time is used as it comes, so the aura still empties at the end.
    private static bool rendTimed;
    private static bool ruinationTimed;
    private static bool wrathTimed;

    private static float ProcSeconds(float remaining, ref bool timed, float maxSeconds)
    {
        if (remaining > 0f)
        {
            timed = true;
            return remaining;
        }

        return timed ? 0f : maxSeconds;
    }

    private static uint primalRendReadyId;
    private static uint primalRuinationReadyId;
    private static uint wrathfulId;
    private static uint heavySwingId;
    private static uint maimId;
    private static uint overpowerId;
    private static bool warriorIdsFound;

    // The wings, in color and desaturated, loaded from the plugin once. Each one is missing from the dictionary until it is ready (nothing is drawn meanwhile).
    private static readonly string[] AuraNames =
    {
        "primal_rend_left", "primal_rend_right", "primal_ruination_left", "primal_ruination_right",
        "primal_rend_left_gray", "primal_rend_right_gray", "primal_ruination_left_gray", "primal_ruination_right_gray",
        "wrathful", "wrathful_gray", "thunderhead", "firestarter_left", "firestarter_right", "paradox_left", "paradox_right"
    };

    // The width of the Primal Wrath arch over its height (the image is 640 x 240).
    private const float WrathAspect = 640f / 240f;

    private readonly ConcurrentDictionary<string, IDalamudTextureWrap> auraTextures = new();
    private bool disposed;

    public LayoutElement WarriorReleaseElement { get; private set; } = null!;

    public LayoutElement WarriorTempestElement { get; private set; } = null!;

    public LayoutElement WarriorAurasElement { get; private set; } = null!;

    public LayoutElement WarriorComboElement { get; private set; } = null!;

    public LayoutElement WarriorWrathElement { get; private set; } = null!;

    private void RegisterWarriorElements()
    {
        WarriorReleaseElement = plugin.Layout.Register("gauge.war.release", "Warrior Inner Release pips", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 216,
            Width = 156,
            Height = 6
        });

        WarriorTempestElement = plugin.Layout.Register("gauge.war.tempest", "Warrior Surging Tempest bar", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 244,
            Width = 156,
            Height = 6
        });

        WarriorComboElement = plugin.Layout.Register("gauge.war.combo", "Warrior combo pips", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 156,
            Height = 6
        });

        // The arch of Primal Wrath: above the character, a crescent open downwards.
        WarriorWrathElement = plugin.Layout.Register("gauge.war.wrath", "Warrior Primal Wrath aura", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = -190,
            Width = 320,
            Height = 120
        });

        // The box of the auras holds both wings: one on its left edge, the other on its right edge.
        WarriorAurasElement = plugin.Layout.Register("gauge.war.auras", "Warrior proc auras", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 20,
            Width = 380,
            Height = 270
        });
    }

    private void LoadAuraTextures()
    {
        foreach (var name in AuraNames)
            LoadAura(name);
    }

    private void LoadAura(string name)
    {
        using var stream = typeof(JobGaugeModule).Assembly.GetManifestResourceStream($"Aura.{name}.png");
        if (stream is null)
            return;

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);

        Plugin.TextureProvider.CreateFromImageAsync(bytes, name).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully)
                return;

            // The plugin can be disposed while the image is loading.
            if (disposed)
                task.Result.Dispose();
            else
                auraTextures[name] = task.Result;
        });
    }

    private void DisposeAuraTextures()
    {
        disposed = true;
        foreach (var texture in auraTextures.Values)
            texture.Dispose();

        auraTextures.Clear();
    }

    // The ids of the statuses and the first steps of the combos, by their English name. The statuses the trackers already know come from there (TrackerPresets keeps the confirmed ids).
    private static void FindWarriorIds()
    {
        if (warriorIdsFound)
            return;

        innerReleaseId = TrackerPresets.FindStatus("Inner Release");
        nascentChaosId = TrackerPresets.FindStatus("Nascent Chaos");
        surgingTempestId = TrackerPresets.FindStatus("Surging Tempest");
        primalRendReadyId = TrackerPresets.FindStatus("Primal Rend Ready");
        primalRuinationReadyId = TrackerPresets.FindStatus("Primal Ruination Ready");
        wrathfulId = TrackerPresets.FindStatus("Wrathful");
        heavySwingId = TrackerPresets.FindAction("Heavy Swing", WarriorJobId);
        maimId = TrackerPresets.FindAction("Maim", WarriorJobId);
        overpowerId = TrackerPresets.FindAction("Overpower", WarriorJobId);
        warriorIdsFound = true;
    }

    private static WarriorState ReadWarriorState(UnitSnapshot player, ComboSnapshot? combo)
    {
        FindWarriorIds();

        var stacks = 0;
        var nascentChaos = false;
        var tempest = 0f;
        var rend = false;
        var ruination = false;
        var wrath = false;
        var wrathSeconds = 0f;
        var rendSeconds = 0f;
        var ruinationSeconds = 0f;

        foreach (var status in player.Statuses)
        {
            if (status.Id == innerReleaseId)
                stacks = Math.Clamp((int)status.Param, 0, InnerReleaseMaxStacks);
            else if (status.Id == nascentChaosId)
                nascentChaos = true;
            else if (status.Id == surgingTempestId)
                tempest = MathF.Max(status.RemainingTime, 0f);
            else if (status.Id == wrathfulId)
            {
                wrath = true;
                wrathSeconds = ProcSeconds(status.RemainingTime, ref wrathTimed, WrathMaxSeconds);
            }
            else if (status.Id == primalRendReadyId)
            {
                rend = true;
                rendSeconds = ProcSeconds(status.RemainingTime, ref rendTimed, RendMaxSeconds);
            }
            else if (status.Id == primalRuinationReadyId)
            {
                ruination = true;
                ruinationSeconds = ProcSeconds(status.RemainingTime, ref ruinationTimed, RuinationMaxSeconds);
            }
        }

        // A proc that is gone forgets that its time was read, so the next one starts again from its full time.
        if (!wrath) wrathTimed = false;
        if (!rend) rendTimed = false;
        if (!ruination) ruinationTimed = false;

        var (length, done) = ReadWarriorCombo(combo);
        return new WarriorState(stacks, nascentChaos, tempest, rend, ruination, ruination ? ruinationSeconds : rendSeconds, length, done, wrath, wrathSeconds);
    }

    // The branch of the combo comes from the last action used: Heavy Swing and Maim are the first two steps of three, Overpower the first of two.
    private static (int Length, int Done) ReadWarriorCombo(ComboSnapshot? combo)
    {
        if (combo is not { Timer: > 0f })
            return (0, 0);

        if (combo.Action == heavySwingId)
            return (3, 1);

        if (combo.Action == maimId)
            return (3, 2);

        if (combo.Action == overpowerId)
            return (2, 1);

        return (0, 0);
    }

    // The preview: two pips, Nascent Chaos, a good part of the Tempest bar, and the two auras one after the other (in the game they never come together).
    // The Primal Wrath arch shows with the Rend wings (in the game Wrathful comes while Primal Rend Ready is still on).
    // The combo follows them: the single-target combo with the Rend aura, the area one with the Ruination aura. The aura empties during its two seconds.
    private static WarriorState WarriorStateSample()
    {
        var time = ImGui.GetTime();
        var rend = (int)(time / 2.0) % 2 == 0;
        var left = (rend ? RendMaxSeconds : RuinationMaxSeconds) * (1f - (float)(time % 2.0 / 2.0));
        return new WarriorState(2, true, 38f, rend, !rend, left, rend ? 3 : 2, rend ? 2 : 1, rend, left * WrathMaxSeconds / (rend ? RendMaxSeconds : RuinationMaxSeconds));
    }

    // The elements of the Warrior that are on, for the box the game windows cut (see Cover).
    private LayoutElement[] WarriorCoverElements(WarriorGaugeSettings settings)
    {
        var elements = new List<LayoutElement> { WarriorElement };

        if (settings.ShowInnerRelease)
            elements.Add(WarriorReleaseElement);

        if (settings.ShowCombo)
            elements.Add(WarriorComboElement);

        if (settings.ShowSurgingTempest)
            elements.Add(WarriorTempestElement);

        if (settings.ShowProcAuras)
            elements.Add(WarriorAurasElement);

        if (settings.ShowWrathAura)
            elements.Add(WarriorWrathElement);

        return elements.ToArray();
    }

    private void DrawWarrior(WarriorGaugeSettings settings, WarriorGaugeSnapshot gauge, WarriorState state)
    {
        DrawBar(settings, WarriorElement, gauge.BeastGauge, flash: settings.ShowNascentChaosFlash && state.NascentChaos ? settings.NascentChaosFlashColor : null);

        var drawList = ImGui.GetBackgroundDrawList();

        if (settings.ShowInnerRelease)
        {
            var slots = new Vector4?[InnerReleaseMaxStacks];
            for (var i = 0; i < slots.Length; i++)
                slots[i] = i < state.InnerReleaseStacks ? settings.InnerReleaseColor : null;

            DrawSlots(drawList, WarriorReleaseElement, settings.InnerReleaseGapPx, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor, slots);
        }

        if (settings.ShowSurgingTempest)
        {
            var text = settings.ShowSurgingTempestSeconds && state.TempestSeconds > 0f ? ((int)MathF.Ceiling(state.TempestSeconds)).ToString() : null;
            DrawFillBar(drawList, WarriorTempestElement, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor, settings.SurgingTempestColor,
                state.TempestSeconds / TempestMaxSeconds, text, settings.SurgingTempestText);
        }

        DrawCombo(drawList, settings, state);
        DrawProcAuras(drawList, settings, state);
        DrawWrathAura(drawList, settings, state);
    }

    // The pips of the combo: the steps used are full and the next one pulses. While Surging Tempest is low, the last pip takes the refresh color (the finisher renews it).
    private void DrawCombo(ImDrawListPtr drawList, WarriorGaugeSettings settings, WarriorState state)
    {
        if (!settings.ShowCombo || !LayoutManager.IsShownNow(WarriorComboElement))
            return;

        var length = state.ComboLength;
        if (length == 0 && !settings.ShowComboOutline)
            return;

        var slots = new Vector4?[length == 0 ? ComboMaxSteps : length];
        for (var i = 0; i < state.ComboDone; i++)
            slots[i] = settings.ComboColor;

        if (length > 0 && state.ComboDone == length - 1)
        {
            var low = settings.ComboRefreshSeconds > 0 && state.TempestSeconds < settings.ComboRefreshSeconds;
            var color = low ? settings.ComboRefreshColor : settings.ComboColor;
            var pulse = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 5f));
            slots[length - 1] = new Vector4(color.X, color.Y, color.Z, color.W * pulse);
        }

        // Each pip is as wide as the bar allows: with two of them (the area combo) they are wider than with three.
        DrawSlots(drawList, WarriorComboElement, settings.ComboGapPx, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor, slots);
    }

    // The wings of the proc that is on: Primal Ruination (the follow-up of Primal Rend) or Primal Rend, one on each side of the box.
    // They work as a timer: the desaturated wing is always there and the wing in color shrinks from the top down while the proc runs out.
    private void DrawProcAuras(ImDrawListPtr drawList, WarriorGaugeSettings settings, WarriorState state)
    {
        if (!settings.ShowProcAuras || !LayoutManager.IsShownNow(WarriorAurasElement))
            return;

        var proc = state.RuinationReady ? "primal_ruination" : state.RendReady ? "primal_rend" : null;
        if (proc is null
            || !auraTextures.TryGetValue($"{proc}_left", out var left) || !auraTextures.TryGetValue($"{proc}_right", out var right)
            || !auraTextures.TryGetValue($"{proc}_left_gray", out var leftGray) || !auraTextures.TryGetValue($"{proc}_right_gray", out var rightGray))
            return;

        var (pos, size) = LayoutManager.GetRect(WarriorAurasElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(MathF.Round(size.Y * AuraAspect), MathF.Round(size.X / 2f));
        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.ProcAuraOpacity, 0f, 1f)));
        var end = pos + size;

        var leftMin = pos;
        var leftMax = new Vector2(pos.X + width, end.Y);
        var rightMin = new Vector2(end.X - width, pos.Y);

        drawList.AddImage(leftGray.Handle, leftMin, leftMax, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
        drawList.AddImage(rightGray.Handle, rightMin, end, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);

        // The part in color is what is left of the time: its top edge goes down as the seconds run out.
        var top = end.Y - size.Y * Math.Clamp(state.AuraSeconds / (state.RuinationReady ? RuinationMaxSeconds : RendMaxSeconds), 0f, 1f);

        drawList.PushClipRect(new Vector2(leftMin.X, top), leftMax, true);
        drawList.AddImage(left.Handle, leftMin, leftMax, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
        drawList.PopClipRect();

        drawList.PushClipRect(new Vector2(rightMin.X, top), end, true);
        drawList.AddImage(right.Handle, rightMin, end, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
        drawList.PopClipRect();
    }

    // The arch of Primal Wrath (Wrathful, 30 seconds): one crescent above the character while the status is on. It keeps the proportions of its image inside
    // the box. Like the wings it works as a timer: the desaturated arch is always there and the gray spreads from the center to both sides over the one in color.
    private void DrawWrathAura(ImDrawListPtr drawList, WarriorGaugeSettings settings, WarriorState state)
    {
        if (!settings.ShowWrathAura || !state.WrathReady || !LayoutManager.IsShownNow(WarriorWrathElement)
            || !auraTextures.TryGetValue("wrathful", out var texture) || !auraTextures.TryGetValue("wrathful_gray", out var gray))
            return;

        var (pos, size) = LayoutManager.GetRect(WarriorWrathElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var width = MathF.Min(size.X, MathF.Round(size.Y * WrathAspect));
        var height = MathF.Round(width / WrathAspect);
        var min = new Vector2(MathF.Round(pos.X + (size.X - width) / 2f), MathF.Round(pos.Y + (size.Y - height) / 2f));

        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(settings.WrathAuraOpacity, 0f, 1f)));
        var max = min + new Vector2(width, height);
        drawList.AddImage(gray.Handle, min, max, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);

        // The gray grows from the center to both sides: the color stays in the two outer parts, and each one shrinks toward its end as the time runs out.
        var left = Math.Clamp(state.WrathSeconds / WrathMaxSeconds, 0f, 1f);
        var half = width / 2f * left;
        var centerX = min.X + width / 2f;
        foreach (var (from, to) in new[] { (min.X, MathF.Round(min.X + half)), (MathF.Round(max.X - half), max.X) })
        {
            if (to - from < 1f)
                continue;

            drawList.PushClipRect(new Vector2(from, min.Y), new Vector2(to, max.Y), true);
            drawList.AddImage(texture.Handle, min, max, new Vector2(0f, 0f), new Vector2(1f, 1f), tint);
            drawList.PopClipRect();
        }
    }
}
