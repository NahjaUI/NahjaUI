using System;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace NahjaUI.Core;

/// <summary>
/// NahjaUI's entry in the game's server info bar (top right, next to the clock), through Dalamud's standard DTR bar.
/// Left click opens or closes the window; right click turns unlock mode on or off (to place the elements on screen).
/// It uses the standard format on purpose: a fixed title ("NahjaUI"), a normal text with a game icon, and a tooltip, so a plugin
/// that restyles the bar can recognise and draw it. The user can hide it with our own option (General) or reorder it in Dalamud's settings.
/// </summary>
internal sealed class ServerInfoBarEntry : IDisposable
{
    // The title is the key other plugins and Dalamud's settings know the entry by: it must not change.
    private const string Title = "NahjaUI";

    private readonly Plugin plugin;
    private readonly IDtrBarEntry entry;

    public ServerInfoBarEntry(Plugin plugin)
    {
        this.plugin = plugin;

        entry = Plugin.DtrBar.Get(Title);
        UpdateIcon();
        entry.Tooltip = new SeString(new TextPayload(
            "NahjaUI\nLeft click: open or close the window.\nRight click: unlock mode, to move the elements on screen."));
        entry.OnClick = OnClick;
        entry.Shown = plugin.Configuration.ShowInServerBar;
    }

    /// <summary>Shows or hides the entry (Configuration.ShowInServerBar).</summary>
    public void UpdateShown() => entry.Shown = plugin.Configuration.ShowInServerBar;

    /// <summary>Puts the icon in the entry: the tank role icon, fixed.</summary>
    public void UpdateIcon()
    {
        if (!Enum.TryParse<BitmapFontIcon>(plugin.Configuration.ServerBarIconNow, out var icon))
            icon = BitmapFontIcon.Mentor;

        entry.Text = new SeString(new IconPayload(icon), new TextPayload(" NahjaUI"));
    }

    private void OnClick(DtrInteractionEvent click)
    {
        switch (click.ClickType)
        {
            case MouseClickType.Left:
                plugin.ToggleMainUi();
                break;

            case MouseClickType.Right:
                plugin.Layout.UnlockMode = !plugin.Layout.UnlockMode;
                break;
        }
    }

    public void Dispose()
    {
        entry.Remove();
    }
}
