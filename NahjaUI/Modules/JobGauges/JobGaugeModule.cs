using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NahjaUI.Core.Layout;
using NahjaUI.Modules.HudBars;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Modules.JobGauges;

/// <summary>
/// Draws the job gauges: White Mage (lilies), Warrior (Beast Gauge), Paladin (Oath Gauge) and Red Mage (mana). It also hides the game's own gauge. It only reads the game snapshot.
/// </summary>
internal sealed partial class JobGaugeModule : IDisposable
{
    // The game's own White Mage gauge.
    private const string NativeWhiteMageGauge = "JobHudWHM0";

    // The game's own Warrior gauge.
    private const string NativeWarriorGauge = "JobHudWAR0";

    // Job ids of the game (White Mage 24, Warrior 21).
    private const uint WhiteMageJobId = 24;
    private const uint WarriorJobId = 21;

    // The game's own Paladin gauge.
    private const string NativePaladinGauge = "JobHudPLD0";

    private const uint PaladinJobId = 19;

    // The game's own Red Mage gauge.
    private const string NativeRedMageGauge = "JobHudRDM0";

    private const uint RedMageJobId = 35;

    // The game's own Dark Knight gauge.
    private const string NativeDarkKnightGauge = "JobHudDRK0";

    private const uint DarkKnightJobId = 32;

    // The game's Dark Knight window with the Darkside gauge, Dark Arts and the Living Shadow time (the Blood gauge is JobHudDRK0).
    private const string NativeDarkside = "JobHudDRK1";

    // The game's own Gunbreaker gauge.
    private const string NativeGunbreakerGauge = "JobHudGNB0";

    private const uint GunbreakerJobId = 37;

    // The game's own Scholar, Astrologian and Sage gauges.
    private const string NativeScholarGauge = "JobHudSCH0";
    // The second window of the Scholar and the Astrologian (the game splits their gauge in two, like the Sage's): hidden together with the first.
    private const string NativeScholarGauge2 = "JobHudSCH1";
    // The window of the Arcanist family: the game uses it for Scholar (and probably Summoner).
    private const string NativeArcanistGauge = "JobHudACN0";
    private const uint SummonerJobId = 27;
    private const string NativeAstrologianGauge = "JobHudAST0";
    private const string NativeAstrologianGauge2 = "JobHudAST1";
    private const string NativeSageGauge = "JobHudGFF0";
    private const string NativeSageGauge2 = "JobHudGFF1";

    private const uint ScholarJobId = 28;
    private const uint AstrologianJobId = 33;
    private const uint SageJobId = 40;

    // The Seraph stays 22 seconds.
    private const float SeraphMaxMs = 22000f;

    // The Darkside bar is full at 60 seconds.
    private const float DarksideMaxMs = 60000f;

    // The game's own Limit Break gauge. It is not a job: this id is only the page entry.
    private const string NativeLimitBreak = "_LimitBreak";

    internal const uint LimitBreakPageId = 0xFFFF;

    // The page entry of the PvP Limit Break gauge.
    internal const uint LimitBreakPvpPageId = 0xFFFD;

    // The game's own Frontline gauge (PvP). It is not a job: this id is only the page entry.
    private const string NativeFrontline = "PvPFrontlineGauge";

    internal const uint FrontlinePageId = 0xFFFE;

    private static readonly string[] NativeGauges = new[] { NativeWhiteMageGauge, NativeWarriorGauge, NativePaladinGauge, NativeRedMageGauge, NativeDarkKnightGauge, NativeDarkside, NativeGunbreakerGauge, NativeScholarGauge, NativeScholarGauge2, NativeArcanistGauge, NativeAstrologianGauge, NativeAstrologianGauge2, NativeSageGauge, NativeSageGauge2, NativeLimitBreak, NativeFrontline }.Concat(ExtraGauges.NativeWindows).ToArray();

    // A new lily every 20 seconds.
    private const float LilyIntervalMs = 20000f;

    // What is shown in the preview: two lilies and a third one filling up, and a full set of blood lilies.
    private static readonly WhiteMageGaugeSnapshot Sample = new(2, 3, 11000f);

    // Preview of the Warrior: enough gauge for one spender.
    private static readonly WarriorGaugeSnapshot WarriorSample = new(70);

    // Preview of the Paladin: enough gauge for one spender.
    private static readonly PaladinGaugeSnapshot PaladinSample = new(70);

    // Preview of the Dark Knight: enough Blood for one spender, 40 seconds of Darkside and Dark Arts active.
    private static readonly DarkKnightGaugeSnapshot DarkKnightSample = new(70, 40000, true, 18000);

    // Preview of the Gunbreaker: two cartridges.
    private static readonly GunbreakerGaugeSnapshot GunbreakerSample = new(2);

    // Preview of the Scholar: two Aetherflow, a Faerie Gauge at 60.
    private static readonly ScholarGaugeSnapshot ScholarSample = new(2, 60, 0);

    // Preview of the Astrologian: two cards of the Astral Draw and a Lord of Crowns.
    private static readonly AstrologianGaugeSnapshot AstrologianSample = new(true, true, false, false, 1);

    // Preview of the Sage: two Addersgall and a third one filling up, one Addersting.
    private static readonly SageGaugeSnapshot SageSample = new(2, 11000, 1);

    // Preview of the Red Mage: both manas past 50 and two stacks.
    private static readonly RedMageGaugeSnapshot RedMageSample = new(72, 58, 2);

    // Preview of the Limit Break: one bar full and the next one half.
    private static readonly LimitBreakSnapshot LimitBreakSample = new(15000, 10000, 3);

    // Preview of the Frontline gauge.
    private static readonly FrontlineGaugeSnapshot FrontlineSample = new(49, 100);

    private readonly Plugin plugin;
    private readonly NativeWindowHider hider = new();

    public JobGaugeModule(Plugin plugin)
    {
        this.plugin = plugin;

        LilyElement = plugin.Layout.Register("gauge.whm.lily", "White Mage lilies", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 190,
            Width = 156,
            Height = 14
        });

        BloodLilyElement = plugin.Layout.Register("gauge.whm.bloodlily", "White Mage blood lilies", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 208,
            Width = 156,
            Height = 8
        });

        WarriorElement = plugin.Layout.Register("gauge.war.beast", "Warrior Beast Gauge", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 156,
            Height = 14
        });

        PaladinElement = plugin.Layout.Register("gauge.pld.oath", "Paladin Oath Gauge", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 156,
            Height = 14
        });

        DarkBloodElement = plugin.Layout.Register("gauge.drk.blood", "Dark Knight Blood", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 240,
            Height = 6
        });

        DarksideElement = plugin.Layout.Register("gauge.drk.darkside", "Dark Knight Darkside", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 242,
            Width = 240,
            Height = 6
        });

        GunbreakerElement = plugin.Layout.Register("gauge.gnb.cartridges", "Gunbreaker Cartridges", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 240,
            Height = 6
        });

        AetherflowElement = plugin.Layout.Register("gauge.sch.aetherflow", "Scholar Aetherflow", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 240,
            Height = 6
        });

        FaerieElement = plugin.Layout.Register("gauge.sch.faerie", "Scholar Faerie Gauge", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 237,
            Width = 240,
            Height = 6
        });

        CardsElement = plugin.Layout.Register("gauge.ast.cards", "Astrologian Cards", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 240,
            Height = 6
        });

        CrownElement = plugin.Layout.Register("gauge.ast.crown", "Astrologian Crown card", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 237,
            Width = 240,
            Height = 6
        });

        AddersgallElement = plugin.Layout.Register("gauge.sge.addersgall", "Sage Addersgall", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 230,
            Width = 240,
            Height = 6
        });

        AdderstingElement = plugin.Layout.Register("gauge.sge.addersting", "Sage Addersting", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 237,
            Width = 240,
            Height = 6
        });

        RedWhiteElement = plugin.Layout.Register("gauge.rdm.white", "Red Mage White Mana", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 200,
            Width = 156,
            Height = 10
        });

        RedBlackElement = plugin.Layout.Register("gauge.rdm.black", "Red Mage Black Mana", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 214,
            Width = 156,
            Height = 10
        });

        RedStackElement = plugin.Layout.Register("gauge.rdm.stacks", "Red Mage Mana Stacks", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 229,
            Width = 156,
            Height = 8
        });

        LimitBreakElement = plugin.Layout.Register("gauge.limitbreak", "Limit Break gauge", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 250,
            Width = 156,
            Height = 10
        });

        LimitBreakPvpElement = plugin.Layout.Register("gauge.limitbreak.pvp", "Limit Break gauge (PvP)", new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Top,
            SelfAnchor = AnchorPoint.Top,
            OffsetX = 0,
            OffsetY = 56,
            Width = 156,
            Height = 10
        });

        FrontlineElement = plugin.Layout.Register("gauge.frontline", "Frontline gauge (PvP)", new ElementLayout
        {
            // In PvP there are no job gauges: it works as one, on top of the MP bar of PvP (1 px apart, like the other stacked bars).
            // The presets set its place for each job.
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Top,
            OffsetX = 0,
            OffsetY = 217,
            Width = 240,
            Height = 6
        });

        RegisterExtraElements();

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, NativeGauges, OnNativePreDraw);
    }

    public LayoutElement LilyElement { get; }

    public LayoutElement BloodLilyElement { get; }

    public LayoutElement WarriorElement { get; }

    public LayoutElement PaladinElement { get; }

    public LayoutElement FrontlineElement { get; }

    public LayoutElement LimitBreakElement { get; }

    public LayoutElement LimitBreakPvpElement { get; }

    public LayoutElement DarkBloodElement { get; }

    public LayoutElement DarksideElement { get; }

    public LayoutElement GunbreakerElement { get; }

    public LayoutElement AetherflowElement { get; }

    public LayoutElement FaerieElement { get; }

    public LayoutElement CardsElement { get; }

    public LayoutElement CrownElement { get; }

    public LayoutElement AddersgallElement { get; }

    public LayoutElement AdderstingElement { get; }

    public LayoutElement RedWhiteElement { get; }

    public LayoutElement RedBlackElement { get; }

    public LayoutElement RedStackElement { get; }

    /// <summary>
    /// Set by the settings page every frame it is open, to the job being edited: that gauge then shows sample values so its look can be adjusted
    /// without being a White Mage in combat. (Unlock mode shows the sample too.)
    /// </summary>
    public uint PreviewJobId { get; set; }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, NativeGauges, OnNativePreDraw);
        hider.ShowAll();
    }

    // ---------------------------------------------------------------- hiding the game's gauge

    // Runs right before the game draws its own gauge. Skipping the drawing hides it completely, even during its animations.
    private void OnNativePreDraw(AddonEvent type, AddonArgs args)
    {
        if (!HidesNative(args.AddonName))
            return;

        hider.Hide(args);
    }

    // Does this module want the game's gauge window hidden right now?
    private bool HidesNative(string windowName)
    {
        // The Frontline gauge only exists in PvP, and only while ours is really drawn (the game gave us its values).
        if (windowName == NativeFrontline)
            return plugin.GameData.Current.Context.InPvPMatch && plugin.Configuration.JobGauges.Frontline is { Enabled: true }
                && plugin.GameData.Current.Frontline is not null;

        // The PvP Limit Break: only while ours is really drawn (the game gave us the gauge), so a failed reading never leaves the screen without one.
        if (windowName == NativeLimitBreak && plugin.GameData.Current.Context.InPvP)
            return plugin.Configuration.JobGauges.LimitBreakPvp is { Enabled: true } && plugin.GameData.Current.LimitBreak is not null;

        // In PvP the game's gauges are the PvP ones and ours do not apply: the game's stay.
        if (plugin.GameData.Current.Context.InPvP)
            return false;

        var gauges = plugin.Configuration.JobGauges;

        // The windows of the melee, physical ranged and caster jobs: hidden while the generic gauge of the job is on.
        if (ExtraGauges.JobOfWindow.TryGetValue(windowName, out var extraJob))
            return gauges.Extra.For(extraJob).Enabled;

        return windowName switch
        {
            NativeWarriorGauge => gauges.Warrior is { Enabled: true },
            NativePaladinGauge => gauges.Paladin is { Enabled: true },
            NativeRedMageGauge => gauges.RedMage is { Enabled: true },
            NativeDarkKnightGauge => gauges.DarkKnight is { Enabled: true },

            // Only while ours is drawn: with our Darkside bar switched off, the game's stays.
            NativeDarkside => gauges.DarkKnight is { Enabled: true, ShowDarkside: true },
            NativeGunbreakerGauge => gauges.Gunbreaker is { Enabled: true },
            NativeArcanistGauge => plugin.GameData.Current.Player?.JobId switch
            {
                ScholarJobId => gauges.Scholar is { Enabled: true },
                SummonerJobId => gauges.Extra.For("SMN").Enabled,
                _ => false
            },
            NativeScholarGauge or NativeScholarGauge2 => gauges.Scholar is { Enabled: true },
            NativeAstrologianGauge or NativeAstrologianGauge2 => gauges.Astrologian is { Enabled: true },
            NativeSageGauge or NativeSageGauge2 => gauges.Sage is { Enabled: true },

            // Only while ours is really drawn (the game gave us the gauge), so a failed reading never leaves the screen without one.
            NativeLimitBreak => gauges.LimitBreak is { Enabled: true } && plugin.GameData.Current.LimitBreak is not null,
            _ => gauges.WhiteMage is { Enabled: true }
        };
    }

    // ---------------------------------------------------------------- drawing

    public void Draw()
    {
        hider.ShowAgain(HidesNative);
        var snapshot = plugin.GameData.Current;
        var player = snapshot.IsLoggedIn ? snapshot.Player : null;
        var previewJob = PreviewJobId;
        PreviewJobId = 0;

        // The job gauges are for PvE: in PvP only the Frontline gauge is drawn (unlock mode still shows all of them so they can be placed).
        var frontline = plugin.Configuration.JobGauges.Frontline;

        // Unlock mode only offers what is in use: the Frontline gauge in PvP, the job gauges in PvE.
        FrontlineElement.IsHidden = !snapshot.Context.InPvPMatch;

        if (frontline.Enabled)
        {
            var frontlineGauge = snapshot.Context.InPvPMatch ? snapshot.Frontline : null;
            frontlineGauge ??= previewJob == FrontlinePageId || (plugin.Layout.UnlockMode && snapshot.Context.InPvPMatch) ? FrontlineSample : null;
            if (frontlineGauge is not null)
            {
                var frontlineValue = (byte)Math.Clamp(frontlineGauge.Max == 0 ? 0 : frontlineGauge.Value * 100f / frontlineGauge.Max, 0f, 100f);
                Cover(() => DrawBar(frontline, FrontlineElement, frontlineValue, (int)frontlineGauge.Value), FrontlineElement);
            }
        }

        // The Limit Break of PvP has its own bar; unlock mode only offers it in PvP.
        LimitBreakPvpElement.IsHidden = !snapshot.Context.InPvP;
        var limitBreakPvp = plugin.Configuration.JobGauges.LimitBreakPvp;
        if (limitBreakPvp.Enabled && (snapshot.Context.InPvP || previewJob == LimitBreakPvpPageId))
        {
            var pvpGauge = snapshot.Context.InPvP ? snapshot.LimitBreak : null;
            pvpGauge ??= previewJob == LimitBreakPvpPageId || plugin.Layout.UnlockMode ? LimitBreakSample : null;
            if (pvpGauge is not null)
                Cover(() => DrawLimitBreak(limitBreakPvp, pvpGauge, LimitBreakPvpElement), LimitBreakPvpElement);
        }

        if (snapshot.Context.InPvP)
        {
            LilyElement.IsHidden = true;
            BloodLilyElement.IsHidden = true;
            WarriorElement.IsHidden = true;
            PaladinElement.IsHidden = true;
            DarkBloodElement.IsHidden = true;
            DarksideElement.IsHidden = true;
            GunbreakerElement.IsHidden = true;
            AetherflowElement.IsHidden = true;
            FaerieElement.IsHidden = true;
            CardsElement.IsHidden = true;
            CrownElement.IsHidden = true;
            AddersgallElement.IsHidden = true;
            AdderstingElement.IsHidden = true;
            LimitBreakElement.IsHidden = true;
            RedWhiteElement.IsHidden = true;
            RedBlackElement.IsHidden = true;
            RedStackElement.IsHidden = true;
            HideExtraGauges();
            return;
        }

        LimitBreakElement.IsHidden = false;

        // Unlock mode only offers the gauge of the job you are playing (all of them while the job is unknown).
        var isWhiteMage = player is null || player.JobId == WhiteMageJobId;
        var isWarrior = player is null || player.JobId == WarriorJobId;
        LilyElement.IsHidden = !isWhiteMage;
        BloodLilyElement.IsHidden = !isWhiteMage;
        WarriorElement.IsHidden = !isWarrior;

        var isPaladin = player is null || player.JobId == PaladinJobId;
        PaladinElement.IsHidden = !isPaladin;

        var isDarkKnight = player is null || player.JobId == DarkKnightJobId;
        DarkBloodElement.IsHidden = !isDarkKnight;
        DarksideElement.IsHidden = !isDarkKnight || !plugin.Configuration.JobGauges.DarkKnight.ShowDarkside;

        var isGunbreaker = player is null || player.JobId == GunbreakerJobId;
        GunbreakerElement.IsHidden = !isGunbreaker;

        var isScholar = player is null || player.JobId == ScholarJobId;
        AetherflowElement.IsHidden = !isScholar;
        FaerieElement.IsHidden = !isScholar;

        var isAstrologian = player is null || player.JobId == AstrologianJobId;
        CardsElement.IsHidden = !isAstrologian;
        CrownElement.IsHidden = !isAstrologian || !plugin.Configuration.JobGauges.Astrologian.ShowCrown;

        var isSage = player is null || player.JobId == SageJobId;
        AddersgallElement.IsHidden = !isSage;
        AdderstingElement.IsHidden = !isSage || !plugin.Configuration.JobGauges.Sage.ShowAddersting;

        var isRedMage = player is null || player.JobId == RedMageJobId;
        RedWhiteElement.IsHidden = !isRedMage;
        RedBlackElement.IsHidden = !isRedMage;
        RedStackElement.IsHidden = !isRedMage;

        var unlock = plugin.Layout.UnlockMode;

        var whiteMage = plugin.Configuration.JobGauges.WhiteMage;
        if (whiteMage.Enabled)
        {
            // The gauge only exists while the player is a White Mage. The settings page shows a sample of the job being edited,
            // unlock mode only for the job you are playing.
            var gauge = snapshot.WhiteMage ?? (previewJob == WhiteMageJobId || (unlock && isWhiteMage) ? Sample : null);
            if (gauge is not null)
                Cover(() => DrawWhiteMage(whiteMage, gauge), LilyElement, BloodLilyElement);
        }

        var warrior = plugin.Configuration.JobGauges.Warrior;
        if (warrior.Enabled)
        {
            var gauge = snapshot.Warrior ?? (previewJob == WarriorJobId || (unlock && isWarrior) ? WarriorSample : null);
            if (gauge is not null)
                Cover(() => DrawBar(warrior, WarriorElement, gauge.BeastGauge), WarriorElement);
        }

        var paladin = plugin.Configuration.JobGauges.Paladin;
        if (paladin.Enabled)
        {
            var gauge = snapshot.Paladin ?? (previewJob == PaladinJobId || (unlock && isPaladin) ? PaladinSample : null);
            if (gauge is not null)
                Cover(() => DrawBar(paladin, PaladinElement, gauge.OathGauge), PaladinElement);
        }

        var darkKnight = plugin.Configuration.JobGauges.DarkKnight;
        if (darkKnight.Enabled)
        {
            var gauge = snapshot.DarkKnight ?? (previewJob == DarkKnightJobId || (unlock && isDarkKnight) ? DarkKnightSample : null);
            if (gauge is not null)
                Cover(() => DrawDarkKnight(darkKnight, gauge), DarkBloodElement, DarksideElement);
        }

        var gunbreaker = plugin.Configuration.JobGauges.Gunbreaker;
        if (gunbreaker.Enabled)
        {
            var gauge = snapshot.Gunbreaker ?? (previewJob == GunbreakerJobId || (unlock && isGunbreaker) ? GunbreakerSample : null);
            if (gauge is not null)
                Cover(() => DrawGunbreaker(gunbreaker, gauge), GunbreakerElement);
        }

        var scholar = plugin.Configuration.JobGauges.Scholar;
        if (scholar.Enabled)
        {
            var gauge = snapshot.Scholar ?? (previewJob == ScholarJobId || (unlock && isScholar) ? ScholarSample : null);
            if (gauge is not null)
                Cover(() => DrawScholar(scholar, gauge), AetherflowElement, FaerieElement);
        }

        var astrologian = plugin.Configuration.JobGauges.Astrologian;
        if (astrologian.Enabled)
        {
            var gauge = snapshot.Astrologian ?? (previewJob == AstrologianJobId || (unlock && isAstrologian) ? AstrologianSample : null);
            if (gauge is not null)
                Cover(() => DrawAstrologian(astrologian, gauge), CardsElement, CrownElement);
        }

        var sage = plugin.Configuration.JobGauges.Sage;
        if (sage.Enabled)
        {
            var gauge = snapshot.Sage ?? (previewJob == SageJobId || (unlock && isSage) ? SageSample : null);
            if (gauge is not null)
                Cover(() => DrawSage(sage, gauge), AddersgallElement, AdderstingElement);
        }

        var limitBreak = plugin.Configuration.JobGauges.LimitBreak;
        if (limitBreak.Enabled)
        {
            var gauge = snapshot.LimitBreak ?? (previewJob == LimitBreakPageId || unlock ? LimitBreakSample : null);
            if (gauge is not null)
                Cover(() => DrawLimitBreak(limitBreak, gauge, LimitBreakElement), LimitBreakElement);
        }

        var redMage = plugin.Configuration.JobGauges.RedMage;
        if (redMage.Enabled)
        {
            var gauge = snapshot.RedMage ?? (previewJob == RedMageJobId || (unlock && isRedMage) ? RedMageSample : null);
            if (gauge is not null)
                Cover(() => DrawRedMage(redMage, gauge), RedWhiteElement, RedBlackElement, RedStackElement);
        }

        DrawExtraGauges(snapshot, player, previewJob, unlock);
    }

    // Draws a gauge so that the game's windows stay over it: the box around its elements is cut around those windows.
    private void Cover(Action draw, params LayoutElement[] elements)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var element in elements)
        {
            var (pos, size) = LayoutManager.GetRect(element.Layout);
            min = Vector2.Min(min, pos);
            max = Vector2.Max(max, pos + size);
        }

        if (max.X < min.X)
        {
            draw();
            return;
        }

        plugin.GameCover.Draw(min, max - min, 120f * ImGuiHelpers.GlobalScale, draw);
    }

    // As many bars as the content has (1 to 3), side by side: each one fills from the left; a full bar has the "full" colour. The level (LB1...) is written over it.
    // The width of the box is the width of THREE bars: with one or two bars each keeps the size it would have with three, and they stay centered in the box.
    private void DrawLimitBreak(LimitBreakGaugeSettings settings, LimitBreakSnapshot gauge, LayoutElement element)
    {
        if (gauge.BarUnits == 0 || !LayoutManager.IsShownNow(element))
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var bars = Math.Clamp(gauge.Bars, 1, 3);
        var gap = MathF.Round(settings.GapPx * scale);
        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        const int MaxBars = 3;
        var barWidth = MathF.Floor((size.X - gap * (MaxBars - 1)) / MaxBars);
        if (barWidth < 1f)
            return;

        var total = barWidth * bars + gap * (bars - 1);
        var left = pos.X + MathF.Round((size.X - total) / 2f);

        var amount = gauge.Units / (float)gauge.BarUnits;
        var fullBars = Math.Min((int)amount, bars);

        for (var i = 0; i < bars; i++)
        {
            var a = new Vector2(left + i * (barWidth + gap), pos.Y);
            var b = new Vector2(a.X + barWidth, pos.Y + size.Y);

            drawList.AddRectFilled(a, b, ImGui.GetColorU32(settings.BackgroundColor), rounding);

            var fill = Math.Clamp(amount - i, 0f, 1f);
            if (fill > 0f)
            {
                var color = fill >= 1f ? settings.FullColor : settings.FillColor;
                drawList.PushClipRect(a, new Vector2(a.X + MathF.Round(barWidth * fill), b.Y), true);
                drawList.AddRectFilled(a, b, ImGui.GetColorU32(color), rounding);
                drawList.PopClipRect();
            }

            // A full bar (a level you can use) has its own border color.
            drawList.AddRect(a, b, ImGui.GetColorU32(fill >= 1f ? settings.ReadyFrameColor : settings.BorderColor), rounding, ImDrawFlags.None, thickness);
        }

        if (settings.ShowLevel && fullBars >= 1)
        {
            DrawCenteredText(drawList, settings.Text, pos, size, $"LB{fullBars}");
        }
    }

    // Blood is a bar like the Warrior's. Under it, the Darkside time: a bar that empties (60 seconds when full), the seconds written on it,
    // and a frame around it while Dark Arts is active.
    private void DrawDarkKnight(DarkKnightGaugeSettings settings, DarkKnightGaugeSnapshot gauge)
    {
        DrawBar(settings, DarkBloodElement, gauge.Blood);

        if (!settings.ShowDarkside || !LayoutManager.IsShownNow(DarksideElement))
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(DarksideElement.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var end = pos + size;

        drawList.AddRectFilled(pos, end, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        var amount = Math.Clamp(gauge.DarksideMs / DarksideMaxMs, 0f, 1f);
        var fillWidth = MathF.Round(size.X * amount);
        if (fillWidth > 0f)
        {
            drawList.PushClipRect(pos, new Vector2(pos.X + fillWidth, end.Y), true);
            drawList.AddRectFilled(pos, end, ImGui.GetColorU32(settings.DarksideColor), rounding);
            drawList.PopClipRect();
        }

        // Dark Arts is the border painted in its color: it never leaves the bar.
        var darkArts = settings.ShowDarkArts && gauge.DarkArts;
        drawList.AddRect(pos, end, ImGui.GetColorU32(darkArts ? settings.DarkArtsColor : settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        if (settings.ShowDarksideSeconds && gauge.DarksideMs > 0)
        {
            DrawCenteredText(drawList, settings.DarksideText, pos, size, ((int)MathF.Ceiling(gauge.DarksideMs / 1000f)).ToString());
        }

        if (settings.ShowLivingShadow && gauge.LivingShadowMs > 0)
            DrawCenteredText(drawList, settings.LivingShadowText, pos, size, ((int)MathF.Ceiling(gauge.LivingShadowMs / 1000f)).ToString());
    }

    // The Cartridges: three pips side by side.
    private void DrawGunbreaker(GunbreakerGaugeSettings settings, GunbreakerGaugeSnapshot gauge)
    {
        if (!LayoutManager.IsShownNow(GunbreakerElement))
            return;

        const int count = 3;
        var drawList = ImGui.GetBackgroundDrawList();
        var (pos, size) = LayoutManager.GetRect(GunbreakerElement.Layout);
        var scale = ImGuiHelpers.GlobalScale;
        var gap = MathF.Round(settings.GapPx * scale);
        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        var pipWidth = MathF.Floor((size.X - gap * (count - 1)) / count);
        if (pipWidth < 1f || size.Y < 1f)
            return;

        var total = pipWidth * count + gap * (count - 1);
        var left = pos.X + MathF.Round((size.X - total) / 2f);
        var background = ImGui.GetColorU32(settings.BackgroundColor);
        var fill = ImGui.GetColorU32(settings.CartridgeColor);
        var border = ImGui.GetColorU32(settings.BorderColor);
        var filled = Math.Min((int)gauge.Cartridges, count);

        for (var i = 0; i < count; i++)
        {
            var a = new Vector2(left + i * (pipWidth + gap), pos.Y);
            var b = new Vector2(a.X + pipWidth, pos.Y + size.Y);
            drawList.AddRectFilled(a, b, i < filled ? fill : background, rounding);
            drawList.AddRect(a, b, border, rounding, ImDrawFlags.None, thickness);
        }
    }

    private void DrawRedMage(RedMageGaugeSettings settings, RedMageGaugeSnapshot gauge)
    {
        var drawList = ImGui.GetBackgroundDrawList();
        var white = Math.Clamp((int)gauge.WhiteMana, 0, 100);
        var black = Math.Clamp((int)gauge.BlackMana, 0, 100);
        var frame = settings.FrameWhenReady && white >= 50 && black >= 50;

        DrawMana(drawList, settings, RedWhiteElement, white, settings.WhiteColor, frame);
        DrawMana(drawList, settings, RedBlackElement, black, settings.BlackColor, frame);

        if (settings.ShowStacks)
            DrawStacks(drawList, settings, Math.Min((int)gauge.ManaStacks, 3));
    }

    // One mana bar: rounded, filled from the left, optional mark at 50, optional number.
    private void DrawMana(ImDrawListPtr drawList, RedMageGaugeSettings settings, LayoutElement element, int value, Vector4 color, bool frame)
    {
        if (!LayoutManager.IsShownNow(element))
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var end = pos + size;

        drawList.AddRectFilled(pos, end, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        var fillWidth = MathF.Round(size.X * value / 100f);
        if (fillWidth > 0f)
        {
            drawList.PushClipRect(pos, new Vector2(pos.X + fillWidth, end.Y), true);
            drawList.AddRectFilled(pos, end, ImGui.GetColorU32(color), rounding);
            drawList.PopClipRect();
        }

        if (settings.ShowThreshold)
        {
            var x = pos.X + MathF.Round(size.X / 2f);
            drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, end.Y), ImGui.GetColorU32(settings.ThresholdColor), thickness);
        }

        // The ready frame is the border painted in its color.
        drawList.AddRect(pos, end, ImGui.GetColorU32(frame ? settings.ReadyFrameColor : settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        if (settings.ShowNumbers)
        {
            DrawCenteredText(drawList, settings.Text, pos, size, value.ToString());
        }
    }

    // The Mana Stacks: three pips side by side.
    private void DrawStacks(ImDrawListPtr drawList, RedMageGaugeSettings settings, int filled)
    {
        if (!LayoutManager.IsShownNow(RedStackElement))
            return;

        const int count = 3;
        var (pos, size) = LayoutManager.GetRect(RedStackElement.Layout);
        var scale = ImGuiHelpers.GlobalScale;
        var gap = MathF.Round(settings.GapPx * scale);
        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        var pipWidth = MathF.Floor((size.X - gap * (count - 1)) / count);
        if (pipWidth < 1f || size.Y < 1f)
            return;

        var total = pipWidth * count + gap * (count - 1);
        var left = pos.X + MathF.Round((size.X - total) / 2f);
        var background = ImGui.GetColorU32(settings.BackgroundColor);
        var fill = ImGui.GetColorU32(settings.StackColor);
        var border = ImGui.GetColorU32(settings.BorderColor);

        for (var i = 0; i < count; i++)
        {
            var a = new Vector2(left + i * (pipWidth + gap), pos.Y);
            var b = new Vector2(a.X + pipWidth, pos.Y + size.Y);
            drawList.AddRectFilled(a, b, i < filled ? fill : background, rounding);
            drawList.AddRect(a, b, border, rounding, ImDrawFlags.None, thickness);
        }
    }

    // One rounded bar from 0 to 100, with an optional mark at 50 and the number in the middle.
    private void DrawBar(BarGaugeSettings settings, LayoutElement element, byte gaugeValue, int? shownNumber = null)
    {
        if (!LayoutManager.IsShownNow(element))
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var (pos, size) = LayoutManager.GetRect(element.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var value = Math.Clamp((int)gaugeValue, 0, 100);
        var rounding = MathF.Round(GlobalColors.Current.Rounding * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var end = pos + size;

        var ready = settings.HighlightWhenReady && value >= 50;
        var color = ready ? settings.ReadyColor : settings.BarColor;

        drawList.AddRectFilled(pos, end, ImGui.GetColorU32(settings.BackgroundColor), rounding);

        var fillWidth = MathF.Round(size.X * value / 100f);
        if (fillWidth > 0f)
        {
            // Clipped to the bar so the fill keeps the rounded outline of the bar, not its own rounded end.
            drawList.PushClipRect(pos, new Vector2(pos.X + fillWidth, end.Y), true);
            drawList.AddRectFilled(pos, end, ImGui.GetColorU32(color), rounding);
            drawList.PopClipRect();
        }

        if (settings.ShowThreshold)
        {
            var x = pos.X + MathF.Round(size.X / 2f);
            drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, end.Y), ImGui.GetColorU32(settings.ThresholdColor), thickness);
        }

        drawList.AddRect(pos, end, ImGui.GetColorU32(settings.BorderColor), rounding, ImDrawFlags.None, thickness);

        if (settings.ShowNumber)
        {
            DrawCenteredText(drawList, settings.Text, pos, size, (shownNumber ?? value).ToString());
        }
    }

    // A number on a gauge, placed like every other text: a point of the box, the offsets, size, font and color of the settings.
    private void DrawCenteredText(ImDrawListPtr drawList, FrameTextSettings text, Vector2 pos, Vector2 size, string value)
    {
        plugin.HudBarSystem.DrawText(drawList, value, text, pos, size, OverlayFont.NahjaDisplayBold);
    }

    private void DrawWhiteMage(WhiteMageGaugeSettings settings, WhiteMageGaugeSnapshot gauge)
    {
        var drawList = ImGui.GetBackgroundDrawList();

        // How far the next lily is. The game counts this timer up from 0 to 20 seconds.
        var lilies = Math.Min((int)gauge.Lily, 3);
        var progress = lilies >= 3 ? 0f : Math.Clamp(gauge.LilyTimerMs / LilyIntervalMs, 0f, 1f);

        var (lilyPos, lilySize) = LayoutManager.GetRect(LilyElement.Layout);
        var lilyShown = LayoutManager.IsShownNow(LilyElement);
        if (lilyShown)
            DrawPips(drawList, settings.GapPx, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor, settings.GlowColor, lilyPos, lilySize, lilies, progress, settings.LilyColor, false);

        if (lilyShown && settings.ShowTimer && lilies < 3 && progress > 0f)
        {
            var secondsLeft = (int)MathF.Ceiling((1f - progress) * LilyIntervalMs / 1000f);
            DrawPipText(drawList, settings.Text, settings.GapPx, lilyPos, lilySize, lilies, secondsLeft.ToString());
        }

        if (!settings.ShowBloodLilies || !LayoutManager.IsShownNow(BloodLilyElement))
            return;

        var blood = Math.Min((int)gauge.BloodLily, 3);
        var (bloodPos, bloodSize) = LayoutManager.GetRect(BloodLilyElement.Layout);
        DrawPips(drawList, settings.GapPx, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor, settings.GlowColor, bloodPos, bloodSize, blood, 0f, settings.BloodLilyColor, settings.GlowWhenFull && blood >= 3);
    }

    // Three rounded pips side by side, inside the box of the layout element. The first <filled> are full and the next one
    // is filled by <partial> (0 to 1) from the left, like a small bar.
    private static void DrawPips(ImDrawListPtr drawList, int gapPx, int roundingPx, Vector4 backgroundColor, Vector4 borderColor, Vector4 glowFrameColor, Vector2 pos, Vector2 size, int filled, float partial, Vector4 color, bool glow)
    {
        const int count = 3;

        var scale = ImGuiHelpers.GlobalScale;
        var gap = MathF.Round(gapPx * scale);
        var rounding = MathF.Round(roundingPx * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        // Whole pixels, so every pip has the same sharp edges.
        var pipWidth = MathF.Floor((size.X - gap * (count - 1)) / count);
        if (pipWidth < 1f || size.Y < 1f)
            return;

        var total = pipWidth * count + gap * (count - 1);
        var left = pos.X + MathF.Round((size.X - total) / 2f);

        var background = ImGui.GetColorU32(backgroundColor);
        var fill = ImGui.GetColorU32(color);
        var partialFill = ImGui.GetColorU32(Theme.WithAlpha(color, 0.65f));
        var border = ImGui.GetColorU32(borderColor);
        var glowColor = ImGui.GetColorU32(glowFrameColor);

        for (var i = 0; i < count; i++)
        {
            var a = new Vector2(left + i * (pipWidth + gap), pos.Y);
            var b = new Vector2(a.X + pipWidth, pos.Y + size.Y);

            drawList.AddRectFilled(a, b, background, rounding);

            if (i < filled)
            {
                drawList.AddRectFilled(a, b, fill, rounding);
            }
            else if (i == filled && partial > 0f)
            {
                var right = a.X + MathF.Round(pipWidth * partial);
                if (right > a.X)
                    drawList.AddRectFilled(a, new Vector2(right, b.Y), partialFill, rounding);
            }

            // When the gauge is full the pip's own border is painted in the frame color.
            drawList.AddRect(a, b, glow ? glowColor : border, rounding, ImDrawFlags.None, thickness);
        }
    }

    // A short text (the seconds until the next lily) in the middle of one pip.
    private void DrawPipText(ImDrawListPtr drawList, FrameTextSettings textSettings, int gapPx, Vector2 pos, Vector2 size, int pipIndex, string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = MathF.Round(gapPx * scale);
        var pipWidth = MathF.Floor((size.X - gap * 2f) / 3f);
        if (pipWidth < 1f)
            return;

        var total = pipWidth * 3f + gap * 2f;
        var left = pos.X + MathF.Round((size.X - total) / 2f);
        var centerX = left + pipIndex * (pipWidth + gap) + pipWidth / 2f;

        plugin.HudBarSystem.DrawText(drawList, text, textSettings, new Vector2(centerX - pipWidth / 2f, pos.Y), new Vector2(pipWidth, size.Y), OverlayFont.NahjaDisplayBold);
    }

    // ---------------------------------------------------------------- healers: Scholar, Astrologian, Sage

    private void DrawScholar(ScholarGaugeSettings settings, ScholarGaugeSnapshot gauge)
    {
        var drawList = ImGui.GetBackgroundDrawList();

        var (flowPos, flowSize) = LayoutManager.GetRect(AetherflowElement.Layout);
        if (LayoutManager.IsShownNow(AetherflowElement))
            DrawPips(drawList, settings.GapPx, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor, default, flowPos, flowSize,
                Math.Min((int)gauge.Aetherflow, 3), 0f, settings.AetherflowColor, false);

        // While the Seraph is out the Faerie Gauge is hers: the bar shows the time she has left.
        var seraph = gauge.SeraphMs > 0;
        var fraction = seraph ? gauge.SeraphMs / SeraphMaxMs : gauge.Faerie / 100f;
        var text = seraph ? ((int)MathF.Ceiling(gauge.SeraphMs / 1000f)).ToString() : gauge.Faerie.ToString();
        DrawFillBar(drawList, FaerieElement, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor,
            seraph ? settings.SeraphColor : settings.FaerieColor, fraction, settings.ShowNumber ? text : null, settings.Text);
    }

    private void DrawAstrologian(AstrologianGaugeSettings settings, AstrologianGaugeSnapshot gauge)
    {
        var drawList = ImGui.GetBackgroundDrawList();
        var cardColor = gauge.Umbral ? settings.UmbralColor : settings.AstralColor;

        DrawSlots(drawList, CardsElement, settings.GapPx, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor,
            new Vector4?[] { gauge.Card1 ? cardColor : null, gauge.Card2 ? cardColor : null, gauge.Card3 ? cardColor : null });

        if (!settings.ShowCrown)
            return;

        Vector4? crown = gauge.Crown == 1 ? settings.LordColor : gauge.Crown == 2 ? settings.LadyColor : null;
        DrawSlots(drawList, CrownElement, settings.GapPx, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor, new[] { crown });
    }

    private void DrawSage(SageGaugeSettings settings, SageGaugeSnapshot gauge)
    {
        var drawList = ImGui.GetBackgroundDrawList();

        // Like the lilies of the White Mage: the next stack fills over 20 seconds.
        const float intervalMs = 20000f;
        var stacks = Math.Min((int)gauge.Addersgall, 3);
        var progress = stacks >= 3 ? 0f : Math.Clamp(gauge.AddersgallMs / intervalMs, 0f, 1f);

        var (gallPos, gallSize) = LayoutManager.GetRect(AddersgallElement.Layout);
        var gallShown = LayoutManager.IsShownNow(AddersgallElement);
        if (gallShown)
            DrawPips(drawList, settings.GapPx, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor, default, gallPos, gallSize,
                stacks, progress, settings.AddersgallColor, false);

        if (gallShown && settings.ShowTimer && stacks < 3 && progress > 0f)
        {
            var secondsLeft = (int)MathF.Ceiling((1f - progress) * intervalMs / 1000f);
            DrawPipText(drawList, settings.Text, settings.GapPx, gallPos, gallSize, stacks, secondsLeft.ToString());
        }

        if (!settings.ShowAddersting || !LayoutManager.IsShownNow(AdderstingElement))
            return;

        var (stingPos, stingSize) = LayoutManager.GetRect(AdderstingElement.Layout);
        DrawPips(drawList, settings.GapPx, GlobalColors.Current.Rounding, settings.BackgroundColor, settings.BorderColor, default, stingPos, stingSize,
            Math.Min((int)gauge.Addersting, 3), 0f, settings.AdderstingColor, false);
    }

    // Slots side by side inside the box of the element; a slot with a colour is full, one without is empty.
    private static void DrawSlots(ImDrawListPtr drawList, LayoutElement element, int gapPx, int roundingPx, Vector4 backgroundColor, Vector4 borderColor, Vector4?[] slots)
    {
        if (!LayoutManager.IsShownNow(element))
            return;

        var (pos, size) = LayoutManager.GetRect(element.Layout);
        var scale = ImGuiHelpers.GlobalScale;
        var gap = MathF.Round(gapPx * scale);
        var rounding = MathF.Round(roundingPx * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));

        var width = MathF.Floor((size.X - gap * (slots.Length - 1)) / slots.Length);
        if (width < 1f || size.Y < 1f)
            return;

        var total = width * slots.Length + gap * (slots.Length - 1);
        var left = pos.X + MathF.Round((size.X - total) / 2f);
        var background = ImGui.GetColorU32(backgroundColor);
        var border = ImGui.GetColorU32(borderColor);

        for (var i = 0; i < slots.Length; i++)
        {
            var a = new Vector2(left + i * (width + gap), pos.Y);
            var b = new Vector2(a.X + width, pos.Y + size.Y);
            drawList.AddRectFilled(a, b, slots[i] is { } color ? ImGui.GetColorU32(color) : background, rounding);
            drawList.AddRect(a, b, border, rounding, ImDrawFlags.None, thickness);
        }
    }

    // One rounded bar filled from the left (0 to 1), with an optional text on it.
    private void DrawFillBar(ImDrawListPtr drawList, LayoutElement element, int roundingPx, Vector4 backgroundColor, Vector4 borderColor, Vector4 color, float fraction, string? text, FrameTextSettings textSettings)
    {
        if (!LayoutManager.IsShownNow(element))
            return;

        var (pos, size) = LayoutManager.GetRect(element.Layout);
        if (size.X < 2f || size.Y < 2f)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var rounding = MathF.Round(roundingPx * scale);
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var end = pos + size;

        drawList.AddRectFilled(pos, end, ImGui.GetColorU32(backgroundColor), rounding);

        var fillWidth = MathF.Round(size.X * Math.Clamp(fraction, 0f, 1f));
        if (fillWidth > 0f)
        {
            drawList.PushClipRect(pos, new Vector2(pos.X + fillWidth, end.Y), true);
            drawList.AddRectFilled(pos, end, ImGui.GetColorU32(color), rounding);
            drawList.PopClipRect();
        }

        drawList.AddRect(pos, end, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, thickness);

        if (text is not null)
            plugin.HudBarSystem.DrawText(drawList, text, textSettings, pos, size, OverlayFont.NahjaDisplayBold);
    }
}
