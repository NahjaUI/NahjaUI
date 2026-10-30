using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NahjaUI.Core.Layout;
using NahjaUI.Game;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI.Modules.StatusEffects;

/// <summary>
/// Draws the enhancements and enfeeblements of the player in the groups the game's own layout option asks for (one element, three
/// groups or four groups), each group in its own box. It also hides the game's own status windows while it is on.
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
            Register("status.single", "Status effects (one element)", 0),
            Register("status.enhancements", "Enhancements", 0),
            Register("status.conditional", "Conditional enhancements", 70),
            Register("status.enfeeblements", "Enfeeblements", 100),
            Register("status.others", "Other statuses", 170)
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

    /// <summary>
    /// Set by the settings page every frame it is open: that group is then drawn (with samples if there is nothing to show), even if
    /// the game's layout option does not use it right now.
    /// </summary>
    public bool PreviewRequested { get; set; }

    public StatusGroupKind? PreviewGroup { get; set; }

    /// <summary>The layout option of the game, as read in the last frame.</summary>
    public StatusLayoutMode CurrentMode { get; private set; }

    /// <summary>Does the game's layout option use this group?</summary>
    public static bool IsInUse(StatusGroupKind kind, StatusLayoutMode mode) => mode switch
    {
        StatusLayoutMode.OneElement => kind == StatusGroupKind.Single,
        StatusLayoutMode.ThreeGroups => kind is StatusGroupKind.Enhancements or StatusGroupKind.Enfeeblements or StatusGroupKind.Others,
        _ => kind != StatusGroupKind.Single
    };

    // ---------------------------------------------------------------- hiding the game's windows

    private void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        if (!plugin.Configuration.StatusEffects.Enabled)
            return;

        hider.Hide(args);
    }

    // ---------------------------------------------------------------- the game's layout option

    private static unsafe StatusLayoutMode ReadMode()
    {
        var hud = HudNumberArray.Instance();
        if (hud == null)
            return StatusLayoutMode.OneElement;

        return hud->StatusElementLayout switch
        {
            HudNumberArray.HudStatusElementLayout.ThreeElements => StatusLayoutMode.ThreeGroups,
            HudNumberArray.HudStatusElementLayout.FourElements => StatusLayoutMode.FourGroups,
            _ => StatusLayoutMode.OneElement
        };
    }

    // ---------------------------------------------------------------- drawing

    public void Draw()
    {
        var settings = plugin.Configuration.StatusEffects;
        hider.ShowAgain(_ => settings.Enabled);
        var preview = PreviewRequested || plugin.Layout.UnlockMode;
        var previewGroup = PreviewRequested ? PreviewGroup : null;
        PreviewRequested = false;
        PreviewGroup = null;

        var mode = ReadMode();
        CurrentMode = mode;

        var snapshot = plugin.GameData.Current;
        var player = snapshot.IsLoggedIn ? snapshot.Player : null;

        foreach (var kind in Enum.GetValues<StatusGroupKind>())
        {
            var element = elements[(int)kind];
            var group = settings.For(kind);

            var shown = settings.Enabled && group.Enabled && (IsInUse(kind, mode) || previewGroup == kind);
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

    // The statuses of the player that belong to a group. The game says if a status is an enhancement or an enfeeblement.
    private static List<StatusSnapshot> Collect(StatusGroupKind kind, UnitSnapshot? player)
    {
        var result = new List<StatusSnapshot>();
        if (player is null || kind == StatusGroupKind.Conditional)
            return result; // the conditional enhancements are not told apart yet

        foreach (var status in player.Statuses)
        {
            // A status without an icon cannot be drawn.
            if (GameNames.StatusIconId(status.Id) == 0)
                continue;

            var category = GameNames.StatusCategory(status.Id);
            var belongs = kind switch
            {
                StatusGroupKind.Single => true,
                StatusGroupKind.Enhancements => category == 1,
                StatusGroupKind.Enfeeblements => category == 2,
                _ => category != 1 && category != 2
            };

            if (belongs)
                result.Add(status);
        }

        return result;
    }

    // What a group shows while it is being adjusted and there is nothing real to show.
    private static List<StatusSnapshot> Sample(StatusGroupKind kind)
    {
        var result = new List<StatusSnapshot>();

        switch (kind)
        {
            case StatusGroupKind.Single:
                result.AddRange(StatusRowRenderer.PreviewEnhancements);
                result.AddRange(StatusRowRenderer.PreviewEnfeeblements);
                break;
            case StatusGroupKind.Enhancements:
                result.AddRange(StatusRowRenderer.PreviewEnhancements);
                break;
            case StatusGroupKind.Conditional:
                result.Add(StatusRowRenderer.PreviewEnhancements[0]);
                result.Add(StatusRowRenderer.PreviewEnhancements[1]);
                break;
            case StatusGroupKind.Enfeeblements:
                result.AddRange(StatusRowRenderer.PreviewEnfeeblements);
                break;
            default:
                result.Add(StatusRowRenderer.PreviewEnhancements[2]);
                break;
        }

        return result;
    }

    private void DrawGroup(LayoutElement element, StatusGroupSettings group, List<StatusSnapshot> statuses, bool isSample)
    {
        var look = group.Look;
        var scale = ImGuiHelpers.GlobalScale;

        var columns = Math.Clamp(group.Columns, 1, 30);
        var most = Math.Clamp(look.MaxIcons, 1, 30);
        var rows = (most + columns - 1) / columns;
        var wide = Math.Min(columns, most);
        var iconWidth = Math.Max(4, look.IconWidth);
        var iconHeight = Math.Max(4, look.IconHeight);

        // The box is exactly the area the icons can cover, so unlock mode shows where they will be.
        element.Layout.Width = wide * iconWidth + (wide - 1) * look.SpacingPx;
        element.Layout.Height = rows * iconHeight + (rows - 1) * look.SpacingPx;

        var (pos, size) = LayoutManager.GetRect(element.Layout);
        var iconSize = new Vector2(MathF.Round(iconWidth * scale), MathF.Round(iconHeight * scale));
        var spacing = MathF.Round(look.SpacingPx * scale);
        var drawList = ImGui.GetBackgroundDrawList();

        var count = Math.Min(statuses.Count, most);
        for (var k = 0; k < count; k++)
        {
            var row = k / columns;
            var column = k % columns;
            var inRow = Math.Min(columns, count - row * columns);
            var rowWidth = inRow * iconSize.X + (inRow - 1) * spacing;

            var x = group.Justify switch
            {
                StatusJustify.Left => pos.X,
                StatusJustify.Right => pos.X + size.X - rowWidth,
                _ => pos.X + MathF.Round((size.X - rowWidth) / 2f)
            };

            var y = group.RowsGrowUp
                ? pos.Y + size.Y - iconSize.Y - row * (iconSize.Y + spacing)
                : pos.Y + row * (iconSize.Y + spacing);

            var iconPos = new Vector2(MathF.Round(x + column * (iconSize.X + spacing)), MathF.Round(y));
            var status = statuses[k];

            plugin.StatusRows.DrawIcon(drawList, look, status, iconPos, iconSize);

            if (isSample || plugin.Layout.UnlockMode || plugin.GameCover.Any)
                continue;

            var hovered = ImGui.IsMouseHoveringRect(iconPos, iconPos + iconSize, false) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow);
            if (!hovered)
                continue;

            plugin.StatusRows.ShowTooltip(status, GameNames.StatusCategory(status.Id) != 2, "player");

            // The mouse is ours while it is over an icon (so the game does not take the click) and the game's cursor stays.
            ImGui.SetNextFrameWantCaptureMouse(true);
            plugin.UnitFrameSystem.KeepGameCursor();

            // Right click on a status: the game cancels it if it can be canceled (Sprint, Peloton...), and does nothing otherwise.
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                StatusManager.ExecuteStatusOff(status.Id, status.SourceId);
        }
    }
}
