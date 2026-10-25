using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules;
using NahjaUI.Windows;

namespace NahjaUI;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandName = "/nui";

    public Configuration Configuration { get; init; }

    internal LayoutManager Layout { get; init; }

    // The only reader of game data. Everything else uses its snapshot.
    internal GameDataProvider GameData { get; init; }

    public readonly WindowSystem WindowSystem = new("NahjaUI");

    private MainWindow MainWindow { get; init; }
    private UnlockOverlay UnlockOverlay { get; init; }
    private LayoutDemo LayoutDemo { get; init; }

    // Set when something changed; the configuration is written once the mouse button is released.
    private bool saveRequested;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        GameData = new GameDataProvider();

        Layout = new LayoutManager(Configuration);
        LayoutDemo = new LayoutDemo(Configuration, Layout);
        UnlockOverlay = new UnlockOverlay(this, Layout);

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open or close the NahjaUI window. Use \"/nui unlock\" to move elements."
        });

        PluginInterface.UiBuilder.Draw += Draw;

        // The buttons in /xlplugins ("Open" and "Settings") both open the main window for now.
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("NahjaUI loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        GameData.Dispose();

        CommandManager.RemoveHandler(CommandName);

        SaveNow();
    }

    // Everything that is drawn each frame goes through here, in this order.
    private void Draw()
    {
        // Keep the accent colour in sync even if the main window is closed.
        Theme.Accent = Configuration.AccentColor;

        WindowSystem.Draw();
        LayoutDemo.Draw();
        UnlockOverlay.Draw();

        FlushSave();
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().ToLowerInvariant() == "unlock")
        {
            Layout.UnlockMode = !Layout.UnlockMode;
            return;
        }

        MainWindow.Toggle();
    }

    public void ToggleMainUi() => MainWindow.Toggle();

    /// <summary>Call after changing any setting. The file is written later, not on every frame.</summary>
    public void RequestSave() => saveRequested = true;

    /// <summary>Writes the configuration right now.</summary>
    public void SaveNow()
    {
        Configuration.Save();
        saveRequested = false;
    }

    private void FlushSave()
    {
        if (!saveRequested)
            return;

        // While the mouse button is held (dragging, for example) wait; save when it is released.
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;

        SaveNow();
    }
}
