using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
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
using NahjaUI.Modules.AllianceList;
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
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IJobGauges JobGauges { get; private set; } = null!;
    [PluginService] internal static Dalamud.Plugin.Services.IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
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
    private SetupWindow SetupWindow { get; init; }
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
    internal MouseoverActions MouseoverSystem { get; init; }

    /// <summary>Makes the whole interface bigger or smaller once, on request (Layout page and the setup).</summary>
    internal NahjaUI.Core.Profiles.ConfigRescaler Rescaler { get; init; }
    internal HudBarEditor HudBarEditorPage { get; init; }
    internal StatusEffectsModule StatusEffectsSystem { get; init; }
    internal StatusEffectsEditor StatusEffectsEditorPage { get; init; }
    internal FiltersEditor FiltersEditorPage { get; init; }
    internal PartyListModule PartyListSystem { get; init; }
    internal AllianceListModule AllianceListSystem { get; init; }
    internal AllianceListEditor AllianceListEditorPage { get; init; }
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
    private bool reopenSetupAfterUnlock;

    // The setup window opens by itself once per session while the configuration says it is pending.
    private bool setupAutoOpened;

    public Plugin()
    {
        // A new install starts from the author's interface (see DefaultConfiguration); a settings file that already exists is never touched.
        var isNewInstall = !PluginInterface.ConfigFile.Exists;
        NahjaUI.Core.Profiles.DefaultConfiguration.WriteIfMissing();

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Only a new install gets the setup window; someone who already has settings never does (they can still open it from General).
        if (isNewInstall)
            Configuration.SetupPending = true;

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
        Layout.Changed = RequestSave;
        Layout.ContextSource = () => GameData.Current.Context;
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
            NahjaUI.Modules.Filters.DefaultFilters.EnsureCreated(Configuration);
            NahjaUI.Modules.Filters.DefaultFilters.AddMissingThunders(Configuration.Filters);
            NahjaUI.Modules.Filters.DefaultFilters.RemoveClosedPositionFromDancePartner(Configuration.Filters);
            WhitenOldLimitBreakFrame(Configuration.JobGauges.LimitBreak);
            WhitenOldLimitBreakFrame(Configuration.JobGauges.LimitBreakPvp);
            WhitenUmbralHearts(Configuration.JobGauges.Extra);
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
        MouseoverSystem = new MouseoverActions(this);
        Rescaler = new NahjaUI.Core.Profiles.ConfigRescaler(this);
        HudBarEditorPage = new HudBarEditor(this);
        StatusEffectsSystem = new StatusEffectsModule(this);
        StatusEffectsEditorPage = new StatusEffectsEditor(this);
        FiltersEditorPage = new FiltersEditor(this);
        PartyListSystem = new PartyListModule(this);
        AllianceListSystem = new AllianceListModule(this);
        AllianceListEditorPage = new AllianceListEditor(this);
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

        SetupWindow = new SetupWindow(this);
        WindowSystem.AddWindow(SetupWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open or close the NahjaUI window.\n/nui unlock \u2192 move the elements of the interface wherever you want; use it again to lock them.\n/nui setup \u2192 run the setup again."
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
        Safely(() => MouseoverSystem.Dispose());
        Safely(() => HudBarSystem.Dispose());
        Safely(() => StatusEffectsSystem.Dispose());
        Safely(() => PartyListSystem.Dispose());
        Safely(() => AllianceListSystem.Dispose());
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
        Guard("setup", AutoOpenSetup);
        Guard("presets", ImportDefaultPresets);

        BarGradient.Strength = System.Math.Clamp(Configuration.BarGradientPercent, 0, 200) / 100f;
        BarGradient.TargetColor = new System.Numerics.Vector3(Configuration.BarGradientColor.X, Configuration.BarGradientColor.Y, Configuration.BarGradientColor.Z);
        BorderStyle.BarPx = System.Math.Clamp(Configuration.BarBorderPx, 0, 3);
        BorderStyle.IconPx = System.Math.Clamp(Configuration.IconBorderPx, 0, 3);

        // The elements that have a position for each job show the one of the job being played.
        Guard("layout", () =>
        {
            var currentSnapshot = GameData.Current;
            Layout.SyncJob(currentSnapshot.IsLoggedIn ? GameNames.ProfileJobId(currentSnapshot.Player?.JobId ?? 0u) : 0u, currentSnapshot.Context.InPvP);
        });

        // Nothing stays out of the screen: an element that is partly out is moved back inside.
        Guard("keep on screen", Layout.KeepAllOnScreen);

        // Which game windows cover the screen right now (the frames are not drawn over them).
        Guard("window cover", () => GameCover.Update(Configuration));
        StreamerMode.Enabled = Configuration.StreamerMode;
        StreamerMode.Seed = Configuration.StreamerSeed;

        Guard("windows", WindowSystem.Draw);

        // While the game hides its own interface, nothing of ours is drawn either (except while the elements are unlocked, so they can still be moved).
        var hidesInterface = GameHidesItsInterface();
        if (hidesInterface != wasHidingInterface)
        {
            wasHidingInterface = hidesInterface;
            if (hidesInterface)
                LogWhyHidden();
        }

        if (hidesInterface && !Layout.UnlockMode)
        {
            Guard("cursor", HudBarSystem.UpdateCursor);
            FlushSave();
            return;
        }

        Guard("cover areas", GameCover.DrawAreas);

        // Drawn first, so the nameplates stay under every frame (the player's, the target's, the enemies', the party's).
        Guard("nameplates", NamePlateSystem.Draw);
        Guard("HUD bars", HudBarSystem.Draw);
        Guard("status effects", StatusEffectsSystem.Draw);
        Guard("party list", PartyListSystem.Draw);
        Guard("alliance list", AllianceListSystem.Draw);
        Guard("enmity list", EnmityListSystem.Draw);
        Guard("trackers", TrackerSystem.Draw);
        Guard("progress bars", ProgressBarSystem.Draw);
        Guard("job gauges", JobGaugeSystem.Draw);
        Guard("unlock overlay", UnlockOverlay.Draw);

        Guard("cursor", HudBarSystem.UpdateCursor);

        FlushSave();
    }

    // The border of a full Limit Break bar used to be light blue by default; it is white now. Only the old default is changed (a color the user chose stays).
    private static void WhitenOldLimitBreakFrame(NahjaUI.Modules.JobGauges.LimitBreakGaugeSettings settings)
    {
        var color = settings.ReadyFrameColor;
        if (System.MathF.Abs(color.X - 0.30f) < 0.01f && System.MathF.Abs(color.Y - 0.85f) < 0.01f && System.MathF.Abs(color.Z - 1f) < 0.01f)
            settings.ReadyFrameColor = new System.Numerics.Vector4(1f, 1f, 1f, 1f);
    }

    // Once: the Umbral Hearts of the Black Mage used to be light blue, like the Umbral Ice. The game's own gauge shows them white, so a saved
    // color that is still exactly the old default becomes the new one. A color the user chose is not touched, and it is not changed again.
    private static void WhitenUmbralHearts(NahjaUI.Modules.JobGauges.ExtraGaugesSettings extra)
    {
        if (extra.UmbralHeartsWhitened)
            return;

        extra.UmbralHeartsWhitened = true;

        if (!extra.Jobs.TryGetValue("BLM", out var job) || !job.Parts.TryGetValue("hearts", out var part))
            return;

        if (part.Colors.TryGetValue(0, out var color)
            && System.MathF.Abs(color.X - 0.5f) < 0.01f && System.MathF.Abs(color.Y - 0.8f) < 0.01f && System.MathF.Abs(color.Z - 1f) < 0.01f)
            part.Colors[0] = new System.Numerics.Vector4(0.95f, 0.95f, 0.90f, 1f);
    }

    private static bool wasHidingInterface;

    // Each time the interface is hidden, the log says which conditions of the game were active, so a hiding that should not happen can be traced (/xllog).
    private static void LogWhyHidden()
    {
        try
        {
            var active = new System.Collections.Generic.List<string>();
            foreach (var flag in System.Enum.GetValues<ConditionFlag>())
            {
                if (Condition[flag])
                    active.Add(flag.ToString());
            }

            Log.Information($"NahjaUI: the interface is hidden. Active conditions: {string.Join(", ", active)}. Casting: {ObjectTable.LocalPlayer?.IsCasting}. Game event status: {HasGameEventStatus()}.");
        }
        catch
        {
            // Only a log line: it never gets in the way.
        }
    }

    // True while the game itself puts its interface away: cutscenes, loading screens and map changes (teleports, entering or leaving
    // an instance), every "occupied" state (talks with characters, shops, quests, the summoning bell) and the scenes that interrupt a fight. Dalamud hides plugins in some of these but not in all of them.
    private static bool GameHidesItsInterface()
    {
        try
        {
            if (Condition[ConditionFlag.OccupiedInCutSceneEvent]
                || Condition[ConditionFlag.WatchingCutscene]
                || Condition[ConditionFlag.WatchingCutscene78]
                || Condition[ConditionFlag.BetweenAreas]
                || Condition[ConditionFlag.BetweenAreas51]
                || HasGameEventStatus())
                return true;

            // "Occupied in a quest event" alone does not hide the interface: the game sets it when a quest item is used, a quest object is picked up
            // or a gate of a dungeon is opened, and its interface stays in all of them (the log of a gate: OccupiedInQuestEvent with no cast).
            // The scenes that really put the interface away carry the cutscene flags above, which always hide.
            // While the player is casting, the other "occupied" flags (talks, shops, the bell) do not hide it either.
            if (Condition[ConditionFlag.Casting] || ObjectTable.LocalPlayer?.IsCasting == true)
                return false;

            return Condition[ConditionFlag.OccupiedInEvent]
                || Condition[ConditionFlag.OccupiedSummoningBell]
                || Condition[ConditionFlag.Occupied]
                || Condition[ConditionFlag.Occupied30]
                || Condition[ConditionFlag.Occupied33]
                || Condition[ConditionFlag.Occupied38]
                || Condition[ConditionFlag.Occupied39];
        }
        catch
        {
            return false;
        }
    }

    // The ids of the status "Participating in a Game Event", the one the player carries during the scenes that interrupt a fight
    // (a trial that stops to tell part of the story). Found once by its English name, so it does not depend on the language of the game.
    private static System.Collections.Generic.HashSet<uint>? gameEventStatusIds;

    private static bool HasGameEventStatus()
    {
        if (gameEventStatusIds is null)
        {
            var ids = new System.Collections.Generic.HashSet<uint>();
            foreach (var row in DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>(Dalamud.Game.ClientLanguage.English))
            {
                if (string.Equals(row.Name.ToString(), "Participating in a Game Event", System.StringComparison.OrdinalIgnoreCase))
                    ids.Add(row.RowId);
            }

            gameEventStatusIds = ids;
        }

        if (gameEventStatusIds.Count == 0 || ObjectTable.LocalPlayer is not { } player)
            return false;

        foreach (var status in player.StatusList)
        {
            if (gameEventStatusIds.Contains(status.StatusId))
                return true;
        }

        return false;
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
            reopenSetupAfterUnlock = SetupWindow.IsOpen;
            MainWindow.IsOpen = false;
            MiniBar.IsOpen = false;
            if (reopenSetupAfterUnlock)
                SetupWindow.HideForUnlock();
        }
        else
        {
            if (reopenWindowAfterUnlock)
                MainWindow.IsOpen = true;

            if (reopenMiniBarAfterUnlock)
                MiniBar.IsOpen = true;

            if (reopenSetupAfterUnlock)
                SetupWindow.ShowAfterUnlock();

            reopenWindowAfterUnlock = false;
            reopenMiniBarAfterUnlock = false;
            reopenSetupAfterUnlock = false;
        }
    }

    private int presetImportIndex;

    // A new install comes with the trackers of every job: one job per frame (so the game does not hitch), once the player is in the game.
    // The jobs that already have trackers are left alone. They can be imported again from the Presets button of the tracker list.
    private void ImportDefaultPresets()
    {
        if (Configuration.AllPresetsImported || !GameData.Current.IsLoggedIn)
            return;

        var names = NahjaUI.Modules.Trackers.JobSetups.Names;
        if (presetImportIndex >= names.Length)
        {
            Configuration.AllPresetsImported = true;
            RequestSave();
            return;
        }

        var name = names[presetImportIndex++];
        try
        {
            if (!NahjaUI.Modules.Trackers.JobSetups.IsSetUp(TrackerSystem, name))
                NahjaUI.Modules.Trackers.JobSetups.Apply(this, TrackerSystem, name);

            // Every job gets the standard column, also the ones the default settings bring set up with their own old places.
            NahjaUI.Modules.Trackers.JobSetups.Arrange(this, TrackerSystem, name);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, $"NahjaUI: could not import the preset of {name}.");
        }
    }

    // Opens the setup window by itself, once, on a new install (when the player is in the game, not on the login screen).
    private void AutoOpenSetup()
    {
        if (setupAutoOpened || !Configuration.SetupPending || !GameData.Current.IsLoggedIn)
            return;

        setupAutoOpened = true;
        SetupWindow.Open();
    }

    /// <summary>Opens the setup window from its first step.</summary>
    public void OpenSetup() => SetupWindow.Open();

    private void OnCommand(string command, string args)
    {
        var argument = args.Trim().ToLowerInvariant();
        if (argument == "unlock")
        {
            Layout.UnlockMode = !Layout.UnlockMode;
            return;
        }

        if (argument == "setup")
        {
            OpenSetup();
            return;
        }

        // Shows the areas of the game's windows that NahjaUI cuts around (to find which window cuts a frame); use it again to hide them.
        if (argument == "covers")
        {
            GameCover.ShowAreas = !GameCover.ShowAreas;
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
