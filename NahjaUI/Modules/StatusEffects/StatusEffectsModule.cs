using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NahjaUI.Core.Layout;
using NahjaUI.Game;
using NahjaUI.Modules.PartyFrames;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI.Modules.StatusEffects;

/// <summary>
/// Draws the enhancements and enfeeblements of the player in two groups, each in its own box, whatever the game's own layout option
/// says. It also hides the game's own status windows while it is on.
/// Right click on one of your own statuses cancels it, as in the game. It only reads the game snapshot.
/// </summary>
internal sealed class StatusEffectsModule : IDisposable
{
    // The game's own status windows, as guessed from its structures: they are checked with the Debug page ("Statuses: game windows").
    private static readonly string[] NativeWindows =
    {
        "_Status", "_StatusCustom0", "_StatusCustom1", "_StatusCustom2", "_StatusCustom3"
    };

    private readonly Plugin plugin;
    private readonly NativeWindowHider hider = new();
    private readonly LayoutElement[] elements;

    public StatusEffectsModule(Plugin plugin)
    {
        this.plugin = plugin;

        // The game puts its status element at the top right, at the left of the minimap. The groups start there, one under the other.
        elements = new[]
        {
            Register("status.enhancements", "Enhancements", 0),
            Register("status.enfeeblements", "Enfeeblements", 70)
        };

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeWindows, OnNativePreDraw);
    }

    private LayoutElement Register(string id, string name, int offsetY) => plugin.Layout.Register(id, name, new ElementLayout
    {
        ScreenAnchor = AnchorPoint.TopRight,
        SelfAnchor = AnchorPoint.TopRight,
        OffsetX = -260,
        OffsetY = 20 + offsetY,
        Width = 350,
        Height = 24
    });

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeWindows, OnNativePreDraw);
    }

    public LayoutElement ElementOf(StatusGroupKind kind) => elements[(int)kind];

    /// <summary>Set by the settings page every frame it is open: the groups are then drawn with samples if there is nothing to show.</summary>
    public bool PreviewRequested { get; set; }

    // ---------------------------------------------------------------- hiding the game's windows

    private void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        if (!plugin.Configuration.StatusEffects.Enabled)
            return;

        hider.Hide(args);
    }

    // ---------------------------------------------------------------- drawing

    public void Draw()
    {
        var settings = plugin.Configuration.StatusEffects;
        hider.ShowAgain(_ => settings.Enabled);
        var preview = PreviewRequested || plugin.Layout.UnlockMode || elements.Any(e => plugin.Layout.IsPreviewing(e.Id));
        PreviewRequested = false;

        var snapshot = plugin.GameData.Current;
        var player = snapshot.IsLoggedIn ? snapshot.Player : null;

        foreach (var kind in Enum.GetValues<StatusGroupKind>())
        {
            var element = elements[(int)kind];
            var group = settings.For(kind);

            var shown = settings.Enabled && group.Enabled;
            element.IsHidden = !shown;
            if (!shown)
                continue;

            var statuses = Collect(kind, player);
            var isSample = false;

            if (statuses.Count == 0 && preview)
            {
                statuses = Sample(kind);
                isSample = true;
            }

            DrawGroup(element, group, statuses, isSample);
        }
    }

    // The statuses of the player that belong to a group. The game says if a status is an enfeeblement; everything else goes with the enhancements.
    private static List<StatusSnapshot> Collect(StatusGroupKind kind, UnitSnapshot? player)
    {
        var result = new List<StatusSnapshot>();
        if (player is null)
            return result;

        foreach (var status in player.Statuses)
        {
            // A status without an icon cannot be drawn.
            if (GameNames.StatusIconId(status.Id) == 0)
                continue;

            var isEnfeeblement = GameNames.StatusCategory(status.Id) == 2;
            if (isEnfeeblement == (kind == StatusGroupKind.Enfeeblements))
                result.Add(status);
        }

        return result;
    }

    // What a group shows while it is being adjusted and there is nothing real to show.
    private static List<StatusSnapshot> Sample(StatusGroupKind kind)
    {
        var result = new List<StatusSnapshot>();
        result.AddRange(kind == StatusGroupKind.Enhancements ? StatusRowRenderer.PreviewEnhancements : StatusRowRenderer.PreviewEnfeeblements);
        return result;
    }

    private void DrawGroup(LayoutElement element, StatusGroupSettings group, List<StatusSnapshot> statuses, bool isSample)
    {
        var look = group.Look;
        var scale = ImGuiHelpers.GlobalScale;

        // A horizontal growth fills lines that are rows (and starts a new row when one is full); a vertical growth fills columns.
        var growth = look.Growth;
        var vertical = growth is StatusRowGrowth.Up or StatusRowGrowth.Down or StatusRowGrowth.CenteredVertically;

        var perLine = Math.Clamp(look.IconsPerLine, 1, 30);
        var most = Math.Clamp(look.MaxIcons, 1, 30);
        var lines = (most + perLine - 1) / perLine;
        var along = Math.Min(perLine, most);
        var iconWidth = Math.Max(4, look.IconWidth);
        var iconHeight = Math.Max(4, look.IconHeight);

        // The box is exactly the area the icons can cover, so unlock mode shows where they will be.
        var across = lines;
        element.Layout.Width = vertical
            ? across * iconWidth + (across - 1) * look.SpacingPx
            : along * iconWidth + (along - 1) * look.SpacingPx;
        element.Layout.Height = vertical
            ? along * iconHeight + (along - 1) * look.SpacingPx
            : across * iconHeight + (across - 1) * look.SpacingPx;

        var (pos, size) = LayoutManager.GetRect(element.Layout);
        var iconSize = new Vector2(MathF.Round(iconWidth * scale), MathF.Round(iconHeight * scale));
        var spacing = MathF.Round(look.SpacingPx * scale);
        var drawList = ImGui.GetBackgroundDrawList();

        statuses = StatusRowRenderer.Sorted(statuses, look.Order);

        var count = Math.Min(statuses.Count, most);
        for (var k = 0; k < count; k++)
        {
            var line = k / perLine;
            var index = k % perLine;
            var inLine = Math.Min(perLine, count - line * perLine);

            float x;
            float y;

            if (!vertical)
            {
                var lineWidth = inLine * iconSize.X + (inLine - 1) * spacing;
                x = growth switch
                {
                    StatusRowGrowth.Left => pos.X + size.X - (index + 1) * iconSize.X - index * spacing,
                    StatusRowGrowth.CenteredHorizontally => pos.X + MathF.Round((size.X - lineWidth) / 2f) + index * (iconSize.X + spacing),
                    _ => pos.X + index * (iconSize.X + spacing)
                };

                y = look.NextLineNow == StatusRowGrowth.Up
                    ? pos.Y + size.Y - iconSize.Y - line * (iconSize.Y + spacing)
                    : pos.Y + line * (iconSize.Y + spacing);
            }
            else
            {
                var lineHeight = inLine * iconSize.Y + (inLine - 1) * spacing;
                y = growth switch
                {
                    StatusRowGrowth.Up => pos.Y + size.Y - (index + 1) * iconSize.Y - index * spacing,
                    StatusRowGrowth.CenteredVertically => pos.Y + MathF.Round((size.Y - lineHeight) / 2f) + index * (iconSize.Y + spacing),
                    _ => pos.Y + index * (iconSize.Y + spacing)
                };

                x = look.NextLineNow == StatusRowGrowth.Left
                    ? pos.X + size.X - iconSize.X - line * (iconSize.X + spacing)
                    : pos.X + line * (iconSize.X + spacing);
            }

            var iconPos = new Vector2(MathF.Round(x), MathF.Round(y));
            var status = statuses[k];

            var drawnStatus = status;
            plugin.GameCover.Draw(iconPos, iconSize, 20f * scale, () => plugin.StatusRows.DrawIcon(drawList, look, drawnStatus, iconPos, iconSize));

            if (isSample || plugin.Layout.UnlockMode || plugin.GameCover.Contains(ImGui.GetIO().MousePos))
                continue;

            var hovered = ImGui.IsMouseHoveringRect(iconPos, iconPos + iconSize, false) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow);
            if (!hovered)
                continue;

            plugin.StatusRows.ShowTooltip(status, GameNames.StatusCategory(status.Id) != 2, "player");

            if (!plugin.UnitFrameSystem.MayCapture())
                continue;

            // The mouse is ours while it is over an icon (so the game does not take the click) and the game's cursor stays.
            ImGui.SetNextFrameWantCaptureMouse(true);
            plugin.UnitFrameSystem.KeepGameCursor();

            // Right click on a status: the game cancels it if it can be canceled (Sprint, Peloton...), and does nothing otherwise.
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                StatusManager.ExecuteStatusOff(status.Id, status.SourceId);
        }
    }
}
