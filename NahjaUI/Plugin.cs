using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.Auras;
using NahjaUI.Modules.CastBars;
using NahjaUI.Modules.JobGauges;
using NahjaUI.Modules.NamePlates;
using NahjaUI.Modules.PartyFrames;
using NahjaUI.Modules.UnitFrames;
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
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IJobGauges JobGauges { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;

    private const string CommandName = "/nui";

    public Configuration Configuration { get; init; }

    internal LayoutManager Layout { get; init; }

    // The only reader of game data. Everything else uses its snapshot.
    internal GameDataProvider GameData { get; init; }

    public readonly WindowSystem WindowSystem = new("NahjaUI");

    private MainWindow MainWindow { get; init; }
    private MiniBarWindow MiniBar { get; init; }
    private UnlockOverlay UnlockOverlay { get; init; }
    internal OverlayFonts Fonts { get; init; }
    internal AuraEngine AuraSystem { get; init; }
    internal AuraEditor AuraEditorPage { get; init; }
    internal CastBarModule CastBarSystem { get; init; }
    internal CastBarEditor CastBarEditorPage { get; init; }
    internal JobGaugeModule JobGaugeSystem { get; init; }
    internal JobGaugeEditor JobGaugeEditorPage { get; init; }
    internal UnitFrameModule UnitFrameSystem { get; init; }
    internal UnitFrameEditor UnitFrameEditorPage { get; init; }
    internal PartyFrameModule PartyFrameSystem { get; init; }
    internal NamePlateModule NamePlateSystem { get; init; }

    // Set when something changed; the configuration is written once the mouse button is released.
    private bool saveRequested;

    // Unlock mode hides the main window while elements are moved and shows it again afterwards.
    private bool wasUnlocked;
    private bool reopenWindowAfterUnlock;
    private bool reopenMiniBarAfterUnlock;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        GameData = new GameDataProvider();

        Layout = new LayoutManager(Configuration);
        Fonts = new OverlayFonts();
        AuraSystem = new AuraEngine(this);
        AuraEditorPage = new AuraEditor(this);
        CastBarSystem = new CastBarModule(this);
        CastBarEditorPage = new CastBarEditor(this);
        JobGaugeSystem = new JobGaugeModule(this);
        JobGaugeEditorPage = new JobGaugeEditor(this);
        UnitFrameSystem = new UnitFrameModule(this);
        UnitFrameEditorPage = new UnitFrameEditor(this);
        PartyFrameSystem = new PartyFrameModule(this);
        NamePlateSystem = new NamePlateModule(this);
        UnlockOverlay = new UnlockOverlay(this, Layout);

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        MiniBar = new MiniBarWindow(this);
        WindowSystem.AddWindow(MiniBar);

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
        MiniBar.Dispose();
        CastBarSystem.Dispose();
        JobGaugeSystem.Dispose();
        UnitFrameSystem.Dispose();
        PartyFrameSystem.Dispose();
        NamePlateSystem.Dispose();
        GameData.Dispose();
        Fonts.Dispose();

        CommandManager.RemoveHandler(CommandName);

        SaveNow();
    }

    // Everything that is drawn each frame goes through here, in this order.
    private void Draw()
    {
        // Keep the accent colour in sync even if the main window is closed.
        Theme.Accent = Configuration.AccentColor;

        FollowUnlockMode();

        WindowSystem.Draw();
        UnitFrameSystem.Draw();
        PartyFrameSystem.Draw();
        NamePlateSystem.Draw();
        AuraSystem.Draw();
        CastBarSystem.Draw();
        JobGaugeSystem.Draw();
        UnlockOverlay.Draw();

        FlushSave();
    }

    // When unlock mode starts, the main window gets out of the way (if it was open), so the whole screen can be used.
    // When unlock mode ends (the Done button or "/nui unlock"), the window comes back at the same page.
    private void FollowUnlockMode()
    {
        var unlocked = Layout.UnlockMode;
        if (unlocked == wasUnlocked)
            return;

        wasUnlocked = unlocked;

        if (unlocked)
        {
            reopenWindowAfterUnlock = MainWindow.IsOpen;
            reopenMiniBarAfterUnlock = MiniBar.IsOpen;
            MainWindow.IsOpen = false;
            MiniBar.IsOpen = false;
        }
        else
        {
            if (reopenWindowAfterUnlock)
                MainWindow.IsOpen = true;

            if (reopenMiniBarAfterUnlock)
                MiniBar.IsOpen = true;

            reopenWindowAfterUnlock = false;
            reopenMiniBarAfterUnlock = false;
        }
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().ToLowerInvariant() == "unlock")
        {
            Layout.UnlockMode = !Layout.UnlockMode;
            return;
        }

        if (args.Trim().ToLowerInvariant() == "nameplates")
        {
            NamePlateSystem.Toggle();
            return;
        }

        ToggleMainUi();
    }

    /// <summary>Opens or closes the main window. If it is minimized to the small bar, it opens it instead.</summary>
    public void ToggleMainUi()
    {
        if (MiniBar.IsOpen)
            ExpandMainWindow();
        else
            MainWindow.Toggle();
    }

    /// <summary>Turns the main window into the small bar, in the same top right corner.</summary>
    public void MinimizeMainWindow()
    {
        var topRight = MainWindow.TopRight;
        MainWindow.IsOpen = false;
        MiniBar.ShowAt(topRight);
    }

    /// <summary>Opens the main window again where the small bar is.</summary>
    public void ExpandMainWindow()
    {
        var topRight = MiniBar.TopRight;
        MiniBar.IsOpen = false;
        MainWindow.ShowAt(topRight);
    }

    /// <summary>Closes the small bar (and with it the whole interface, until /nui is used).</summary>
    public void CloseMiniBar() => MiniBar.IsOpen = false;

    /// <summary>Call after changing any setting. The file is written later, not on every frame.</summary>
    public void RequestSave() => saveRequested = true;

    /// <summary>Sets the font of every aura, aura group template and cast bar to the NahjaUI font (Saira).</summary>
    public void UseNahjaFontEverywhere()
    {
        foreach (var aura in Configuration.Auras)
            aura.Font = OverlayFont.NahjaDisplayBold;

        foreach (var group in Configuration.AuraGroups)
        {
            if (group.Template is not null)
                group.Template.Font = OverlayFont.NahjaDisplayBold;
        }

        Configuration.CastBars.Player.Font = OverlayFont.NahjaDisplay;
        Configuration.CastBars.Target.Font = OverlayFont.NahjaDisplay;
        Configuration.JobGauges.WhiteMage.Font = OverlayFont.NahjaDisplayBold;
        Configuration.JobGauges.Warrior.Font = OverlayFont.NahjaDisplayBold;
        foreach (var frame in new[]
                 {
                     Configuration.UnitFrames.Player,
                     Configuration.UnitFrames.Target,
                     Configuration.UnitFrames.TargetOfTarget,
                     Configuration.UnitFrames.Focus,
                     Configuration.UnitFrames.Party.Look
                 })
        {
            frame.Name.Font = OverlayFont.NahjaDisplay;
            frame.Health.Font = OverlayFont.NahjaDisplay;
        }

        RequestSave();
    }

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
