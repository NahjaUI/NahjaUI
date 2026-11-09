using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using NahjaUI.Core;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.Trackers;
using NahjaUI.Modules.ProgressBars;
using NahjaUI.Modules.JobGauges;
using NahjaUI.Modules.EnmityList;
using NahjaUI.Modules.Filters;
using NahjaUI.Modules.NamePlates;
using NahjaUI.Modules.StatusEffects;
using NahjaUI.Modules.Tooltip;
using NahjaUI.Modules.PartyListBar;
using NahjaUI.Modules.HudBars;
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
    [PluginService] internal static Dalamud.Plugin.Services.IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static ISeStringEvaluator SeStringEvaluator { get; private set; } = null!;

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
    internal TrackerEngine TrackerSystem { get; init; }
    internal StatusRowRenderer StatusRows { get; init; }
    internal GameWindowCover GameCover { get; } = new();
    internal TrackerEditor TrackerEditorPage { get; init; }
    internal ProgressBarModule ProgressBarSystem { get; init; }
    internal ProgressBarEditor ProgressBarEditorPage { get; init; }
    internal JobGaugeModule JobGaugeSystem { get; init; }
    internal JobGaugeEditor JobGaugeEditorPage { get; init; }
    internal HudBarModule HudBarSystem { get; init; }
    internal HudBarEditor HudBarEditorPage { get; init; }
    internal StatusEffectsModule StatusEffectsSystem { get; init; }
    internal StatusEffectsEditor StatusEffectsEditorPage { get; init; }
    internal FiltersEditor FiltersEditorPage { get; init; }
    internal PartyListModule PartyListSystem { get; init; }
    internal NamePlateModule NamePlateSystem { get; init; }
    internal NamePlateEditor NamePlateEditorPage { get; init; }
    internal TooltipEditor TooltipEditorPage { get; init; }
    internal EnmityListModule EnmityListSystem { get; init; }
    internal EnmityListEditor EnmityListEditorPage { get; init; }

    // Set when something changed; the configuration is written once the mouse button is released.
    private bool saveRequested;

    // Unlock mode hides the main window while elements are moved and shows it again afterwards.
    private bool wasUnlocked;
    private bool reopenWindowAfterUnlock;
    private bool reopenMiniBarAfterUnlock;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // A plain copy of the saved file when the plugin was updated (or the format must be converted), then the conversion itself.
        NahjaUI.Core.Profiles.ConfigSafety.BeforeUpdate(Configuration);
        NahjaUI.Core.Profiles.ConfigMigrations.Run(Configuration);
        RequestSave();

        // The cards the user folded last time stay folded.
        Widgets.Collapsed = new System.Collections.Generic.HashSet<string>(Configuration.CollapsedCards ?? new System.Collections.Generic.List<string>());
        Widgets.CollapsedChanged = () =>
        {
            Configuration.CollapsedCards = new System.Collections.Generic.List<string>(Widgets.Collapsed);
            RequestSave();
        };

        GameData = new GameDataProvider();

        // The shared colors of the Appearance page are read from the configuration (also after a profile is applied).
        NahjaUI.Core.Ui.GlobalColors.Source = () => Configuration.Colors ??= new NahjaUI.Core.Ui.GlobalColors();

        Layout = new LayoutManager(Configuration);
        Fonts = new OverlayFonts();
        TrackerSystem = new TrackerEngine(this);
        try
        {
            Configuration.Filters.EnsureStarters();
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "NahjaUI: the starter filters could not be created.");
        }

        try
        {
            NahjaUI.Modules.Filters.DefensiveFilters.EnsureCreated(Configuration);
            Configuration.NamePlates.SeedEnemyRows();
            RequestSave();
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "NahjaUI: the defensive filters could not be created.");
        }

        StatusRows = new StatusRowRenderer(this);
        TrackerEditorPage = new TrackerEditor(this);
        ProgressBarSystem = new ProgressBarModule(this);
        ProgressBarEditorPage = new ProgressBarEditor(this);
        JobGaugeSystem = new JobGaugeModule(this);
        JobGaugeEditorPage = new JobGaugeEditor(this);
        HudBarSystem = new HudBarModule(this);
        HudBarEditorPage = new HudBarEditor(this);
        StatusEffectsSystem = new StatusEffectsModule(this);
        StatusEffectsEditorPage = new StatusEffectsEditor(this);
        FiltersEditorPage = new FiltersEditor(this);
        PartyListSystem = new PartyListModule(this);
        NamePlateSystem = new NamePlateModule(this);
        NamePlateEditorPage = new NamePlateEditor(this);
        TooltipEditorPage = new TooltipEditor(this);
        EnmityListSystem = new EnmityListModule(this);
        EnmityListEditorPage = new EnmityListEditor(this);
        UnlockOverlay = new UnlockOverlay(this, Layout);

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        MiniBar = new MiniBarWindow(this);
        WindowSystem.AddWindow(MiniBar);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open or close the NahjaUI window.\n/nui unlock \u2192 move the elements of the interface wherever you want; use it again to lock them."
        });

        PluginInterface.UiBuilder.Draw += Draw;

        ServerInfoBar = new ServerInfoBarEntry(this);

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

        // Each part is closed on its own: if one fails, the others still run (above all the ones that give back the game's windows).
        Safely(() => ServerInfoBar.Dispose());
        Safely(() => WindowSystem.RemoveAllWindows());
        Safely(() => MainWindow.Dispose());
        Safely(() => MiniBar.Dispose());
        Safely(() => ProgressBarSystem.Dispose());
        Safely(() => JobGaugeSystem.Dispose());
        Safely(() => HudBarSystem.Dispose());
        Safely(() => StatusEffectsSystem.Dispose());
        Safely(() => PartyListSystem.Dispose());
        Safely(() => NamePlateSystem.Dispose());
        Safely(() => EnmityListSystem.Dispose());
        Safely(() => GameData.Dispose());
        Safely(() => Fonts.Dispose());

        Safely(() => CommandManager.RemoveHandler(CommandName));
        Safely(SaveNow);
    }

    // Everything that is drawn each frame goes through here, in this order.
    private void Draw()
    {
        Guard("unlock mode", FollowUnlockMode);

        // The elements that have a position for each job show the one of the job being played.
        Guard("layout", () =>
        {
            var currentSnapshot = GameData.Current;
            Layout.SyncJob(currentSnapshot.IsLoggedIn ? currentSnapshot.Player?.JobId ?? 0u : 0u);
        });

        // Which game windows cover the screen right now (the frames are not drawn over them).
        Guard("window cover", () => GameCover.Update(Configuration));
        StreamerMode.Enabled = Configuration.StreamerMode;

        Guard("windows", WindowSystem.Draw);

        // Drawn first, so the nameplates stay under every frame (the player's, the target's, the enemies', the party's).
        Guard("nameplates", NamePlateSystem.Draw);
        Guard("HUD bars", HudBarSystem.Draw);
        Guard("status effects", StatusEffectsSystem.Draw);
        Guard("party list", PartyListSystem.Draw);
        Guard("enmity list", EnmityListSystem.Draw);
        Guard("trackers", TrackerSystem.Draw);
        Guard("progress bars", ProgressBarSystem.Draw);
        Guard("job gauges", JobGaugeSystem.Draw);
        Guard("unlock overlay", UnlockOverlay.Draw);

        Guard("cursor", HudBarSystem.UpdateCursor);

        FlushSave();
    }

    // ---- Safety: a failure in one part never takes the others (or the game) down. ----

    private sealed class GuardState
    {
        public int Failures;
        public System.DateTime PausedUntil;
    }

    private readonly System.Collections.Generic.Dictionary<string, GuardState> guards = new();

    // Runs one part of the frame. An error is written to the log (once per pause, not once per frame); after 3 failures in a row the
    // part rests for 10 seconds and then tries again, so a broken part costs nothing and a temporary problem fixes itself.
    private void Guard(string name, System.Action action)
    {
        if (!guards.TryGetValue(name, out var state))
            guards[name] = state = new GuardState();

        if (state.PausedUntil > System.DateTime.UtcNow)
            return;

        try
        {
            action();
            state.Failures = 0;
        }
        catch (System.Exception ex)
        {
            state.Failures++;
            if (state.Failures >= 3)
            {
                state.Failures = 0;
                state.PausedUntil = System.DateTime.UtcNow.AddSeconds(10);
                Log.Error(ex, "NahjaUI: {Name} failed several times in a row and rests for 10 seconds.", name);
            }
        }
    }

    private static void Safely(System.Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "NahjaUI: a part could not be closed cleanly.");
        }
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

        ToggleMainUi();
    }

    /// <summary>Opens or closes the main window. If it is minimized to the small bar, it opens it instead.</summary>
    // The entry in the game's server info bar (left click: window, right click: unlock mode).
    internal ServerInfoBarEntry ServerInfoBar { get; private set; } = null!;

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
