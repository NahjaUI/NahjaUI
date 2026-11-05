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
    internal UnitFrameModule UnitFrameSystem { get; init; }
    internal UnitFrameEditor UnitFrameEditorPage { get; init; }
    internal StatusEffectsModule StatusEffectsSystem { get; init; }
    internal StatusEffectsEditor StatusEffectsEditorPage { get; init; }
    internal FiltersEditor FiltersEditorPage { get; init; }
    internal PartyFrameModule PartyFrameSystem { get; init; }
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
        NahjaUI.Core.Profiles.ConfigRename.ConvertConfigFile();
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

        Configuration.UnitFrames.Player.MigrateMpLists();
        Configuration.UnitFrames.Player.MigrateParameterBar();

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
        UnitFrameSystem = new UnitFrameModule(this);
        UnitFrameEditorPage = new UnitFrameEditor(this);
        StatusEffectsSystem = new StatusEffectsModule(this);
        StatusEffectsEditorPage = new StatusEffectsEditor(this);
        FiltersEditorPage = new FiltersEditor(this);
        PartyFrameSystem = new PartyFrameModule(this);
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
            HelpMessage = "Open or close the NahjaUI window. Use \"/nui unlock\" to move elements."
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

        ServerInfoBar.Dispose();
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        MiniBar.Dispose();
        ProgressBarSystem.Dispose();
        JobGaugeSystem.Dispose();
        UnitFrameSystem.Dispose();
        StatusEffectsSystem.Dispose();
        PartyFrameSystem.Dispose();
        NamePlateSystem.Dispose();
        EnmityListSystem.Dispose();
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
        ContextMenuCapture.Update();

        // The elements that have a position for each job show the one of the job being played.
        var currentSnapshot = GameData.Current;
        Layout.SyncJob(currentSnapshot.IsLoggedIn ? currentSnapshot.Player?.JobId ?? 0u : 0u);

        // Which game windows cover the screen right now (the frames are not drawn over them).
        GameCover.Update(Configuration);
        StreamerMode.Enabled = Configuration.StreamerMode;

        WindowSystem.Draw();

        // Drawn first, so the nameplates stay under every frame (the player's, the target's, the enemies', the party's).
        NamePlateSystem.Draw();
        UnitFrameSystem.Draw();
        StatusEffectsSystem.Draw();
        PartyFrameSystem.Draw();
        EnmityListSystem.Draw();
        TrackerSystem.Draw();
        ProgressBarSystem.Draw();
        JobGaugeSystem.Draw();
        UnlockOverlay.Draw();

        UnitFrameSystem.UpdateCursor();

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

        if (args.Trim().ToLowerInvariant() == "debug")
        {
            MainWindow.ShowDebug();
            if (MiniBar.IsOpen)
                ExpandMainWindow();
            else
                MainWindow.IsOpen = true;

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

    /// <summary>Sets the font of every tracker, tracker group template and progress bar to the NahjaUI font (Saira).</summary>
    public void UseNahjaFontEverywhere()
    {
        foreach (var tracker in Configuration.Trackers)
            tracker.Font = OverlayFont.NahjaDisplayBold;

        foreach (var group in Configuration.TrackerGroups)
        {
            if (group.Template is not null)
                group.Template.Font = OverlayFont.NahjaDisplayBold;
        }

        Configuration.ProgressBars.Player.Font = OverlayFont.NahjaDisplay;
        Configuration.ProgressBars.Target.Font = OverlayFont.NahjaDisplay;
        Configuration.JobGauges.WhiteMage.Font = OverlayFont.NahjaDisplayBold;
        Configuration.JobGauges.Warrior.Font = OverlayFont.NahjaDisplayBold;
        Configuration.JobGauges.Paladin.Font = OverlayFont.NahjaDisplayBold;
        Configuration.JobGauges.DarkKnight.Font = OverlayFont.NahjaDisplayBold;
        Configuration.JobGauges.RedMage.Font = OverlayFont.NahjaDisplayBold;
        Configuration.JobGauges.LimitBreak.Font = OverlayFont.NahjaDisplayBold;
        Configuration.JobGauges.Frontline.Font = OverlayFont.NahjaDisplayBold;
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
            frame.Hp.Font = OverlayFont.NahjaDisplay;
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
