using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Ui;

namespace NahjaUI.Core.Layout;

/// <summary>An element that can be positioned: a module registers one of these for each thing it draws.</summary>
internal sealed class LayoutElement
{
    public LayoutElement(string id, string name, ElementLayout defaults, ElementLayout layout)
    {
        Id = id;
        Name = name;
        Defaults = defaults;
        Layout = layout;
    }

    public string Id { get; }
    public string Name { get; set; }
    public ElementLayout Defaults { get; }

    /// <summary>
    /// True when something else (a tracker group) decides where this element is.
    /// Such elements keep their size but cannot be moved, so unlock mode and the Layout page skip them.
    /// </summary>
    public bool IsManaged { get; set; }

    /// <summary>
    /// True when the element belongs to something that does not apply right now (a tracker or gauge of another job).
    /// Hidden elements are left out of unlock mode and the Layout page, so only what you can use is shown.
    /// Their saved position is kept.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Set every frame by a module whose drawn area is not the same as its layout box (a tracker group: the box that
    /// unlock mode shows is the whole area covered by its icons). Physical pixels. Null means "use the layout".
    /// </summary>
    public (Vector2 Pos, Vector2 Size)? DisplayRect { get; set; }

    /// <summary>Extra options of this element, drawn in the panel that unlock mode shows under the X and Y fields (null = none).</summary>
    public Action? DrawOptions { get; set; }

    /// <summary>
    /// The elements with the same name here share one position between jobs (the Rotation row of every job, for example):
    /// it is set once and each job shows it. Null = the element has a position of its own.
    /// </summary>
    public string? PositionGroup { get; set; }

    /// <summary>True when only the place (anchors and offsets) is shared between jobs and the size stays the element's own (tracker groups).</summary>
    public bool PlacementOnly { get; set; }

    /// <summary>True when the element has its own common position in PvP (the progress bars: PvP has another column of bars).</summary>
    public bool SeparatePvp { get; set; }

    /// <summary>The saved, editable layout (it lives inside the plugin configuration).</summary>
    public ElementLayout Layout { get; }

    /// <summary>Back to the default place (what it is attached to, anchors and offsets). The size is chosen in the page of each element, so it is not touched.</summary>
    public void ResetToDefaults()
    {
        Layout.AttachTo = Defaults.AttachTo;
        Layout.ScreenAnchor = Defaults.ScreenAnchor;
        Layout.SelfAnchor = Defaults.SelfAnchor;
        Layout.OffsetX = Defaults.OffsetX;
        Layout.OffsetY = Defaults.OffsetY;
    }
}

/// <summary>
/// Keeps the list of positionable elements and turns an <see cref="ElementLayout"/> into
/// an exact rectangle on the screen.
/// </summary>
internal sealed class LayoutManager
{
    private readonly Configuration config;
    private readonly List<LayoutElement> elements = new();

    public LayoutManager(Configuration config)
    {
        this.config = config;
        Current = this;
    }

    // The manager in use: positions are worked out by a static method that may need the element another one is attached to.
    private static LayoutManager? Current { get; set; }

    // How many attachments are being followed (an element attached to one attached to it would never end).
    private static int attachDepth;

    public IReadOnlyList<LayoutElement> Elements => elements;

    /// <summary>The elements the user can move by hand (those not placed by a group and not belonging to another job).</summary>
    public IReadOnlyList<LayoutElement> Positionable => elements.Where(e => !e.IsManaged && !e.IsHidden).ToList();

    /// <summary>True while the user is moving elements around.</summary>
    public bool UnlockMode { get; set; }

    public string? SelectedId { get; set; }

    /// <summary>Set by the Layout page every frame it is shown: the element selected there is then drawn with sample values even if it has nothing to show.</summary>
    public void MarkLayoutPageOpen() => layoutPageFrame = ImGui.GetFrameCount();

    private int layoutPageFrame = -10;

    /// <summary>Is this element the one selected in the Layout page (open right now)? Its module then shows a sample of it, so it can be moved.</summary>
    public bool IsPreviewing(string id)
    {
        if (SelectedId is null || ImGui.GetFrameCount() - layoutPageFrame > 1)
            return false;

        // The selected element, and the ones it is attached to (and those to which they are attached): the position only makes sense if they are seen too.
        var current = SelectedId;
        for (var step = 0; current is not null && step < 8; step++)
        {
            if (current == id)
                return true;

            current = elements.Find(e => e.Id == current)?.Layout.AttachTo;
        }

        return false;
    }

    /// <summary>Gives the general situation (combat, weapon). Set by the plugin once the game data exists.</summary>
    public Func<NahjaUI.Game.ContextSnapshot>? ContextSource { get; set; }

    /// <summary>
    /// Do the "show when" conditions of this element allow drawing it right now? While you move elements (unlock mode) or
    /// edit one on the Layout page it is always shown, so it can be placed.
    /// </summary>
    public bool IsShown(LayoutElement element, bool preview) => preview || IsShown(element);

    /// <summary>
    /// The same, for a module that is showing a preview of itself right now (the page of the menu is open and the player is not in combat):
    /// the conditions are ignored then, so the element can always be placed and adjusted.
    /// </summary>
    public bool IsShown(LayoutElement element)
    {
        var data = element.Layout;
        if (data.HasNoCondition || UnlockMode || IsPreviewing(element.Id))
            return true;

        var context = ContextSource?.Invoke();
        if (context is null)
            return true;

        if (data.ShowInCombat == ShowCombat.InCombat && !context.InCombat)
            return false;

        if (data.ShowInCombat == ShowCombat.OutOfCombat && context.InCombat)
            return false;

        if (data.ShowWithWeapon == ShowWeapon.Drawn && !context.WeaponDrawn)
            return false;

        if (data.ShowWithWeapon == ShowWeapon.Sheathed && context.WeaponDrawn)
            return false;

        return true;
    }

    /// <summary>Like <see cref="IsShown"/>, for code that has no reference to the manager (static drawing helpers).</summary>
    public static bool IsShownNow(LayoutElement element) => PreviewNow || (Current?.IsShown(element) ?? true);

    /// <summary>
    /// True while a module draws the preview of its page of the menu (set by that module around its drawing): the "show when" conditions
    /// are ignored then, also for static drawing helpers that have no reference to the manager.
    /// </summary>
    public static bool PreviewNow { get; set; }

    /// <summary>The label of an option, for the combo boxes.</summary>
    public static string Label(ShowCombat value) => value switch
    {
        ShowCombat.InCombat => "Only in combat",
        ShowCombat.OutOfCombat => "Only out of combat",
        _ => "Always"
    };

    public static string Label(ShowWeapon value) => value switch
    {
        ShowWeapon.Drawn => "Only with the weapon drawn",
        ShowWeapon.Sheathed => "Only with the weapon sheathed",
        _ => "Always"
    };

    public LayoutElement? SelectedElement =>
        SelectedId is null ? null : elements.Find(e => e.Id == SelectedId);

    /// <summary>
    /// Adds an element. If the user already has a saved position for this id it is used,
    /// otherwise a copy of <paramref name="defaults"/> is stored in the configuration.
    /// </summary>
    public LayoutElement Register(string id, string name, ElementLayout defaults)
    {
        if (!config.Layouts.TryGetValue(id, out var saved))
        {
            saved = defaults.Clone();
            config.Layouts[id] = saved;
        }

        var element = new LayoutElement(id, name, defaults, saved);
        elements.Add(element);
        return element;
    }

    /// <summary>Removes an element from the list but keeps its saved position (used when trackers are reloaded).</summary>
    public void Detach(string id)
    {
        elements.RemoveAll(e => e.Id == id);
        loadedFrom.Remove(id);

        if (SelectedId == id)
            SelectedId = null;
    }

    /// <summary>Removes an element and forgets its saved position (used when a tracker is deleted).</summary>
    public void Unregister(string id)
    {
        var element = elements.Find(e => e.Id == id);
        elements.RemoveAll(e => e.Id == id);
        config.Layouts.Remove(id);
        config.JobLayouts.Remove(id);
        loadedFrom.Remove(id);

        // The position a whole kind of group shares stays: other jobs still use it.
        if (element?.PositionGroup is null)
            config.CommonLayouts.Remove(id);

        if (SelectedId == id)
            SelectedId = null;
    }

    // ---------------------------------------------------------------- one position for every job, and a different one for a job

    /// <summary>A position that belongs to a job is saved under a key: the job id, and for PvP (elements with their own PvP position) the job id plus this number.</summary>
    public const uint PvpKeyOffset = 1000;

    // The layout each element currently shows (the common position or the one of the job), to know when it has to change.
    private readonly Dictionary<string, ElementLayout> loadedFrom = new();

    /// <summary>The job being played (0 when not known) and whether it is in PvP.</summary>
    public uint CurrentJob { get; private set; }

    public bool InPvp { get; private set; }

    /// <summary>The key of the job's own position for this element now: the job, or the job in PvP for the elements with their own PvP column.</summary>
    public uint KeyFor(LayoutElement element) => CurrentJob != 0 && element.SeparatePvp && InPvp ? CurrentJob + PvpKeyOffset : CurrentJob;

    // 0 = the common position of PvE, PvpKeyOffset = the common position of PvP (only for the elements with their own).
    private uint CommonKeyFor(LayoutElement element) => element.SeparatePvp && InPvp ? PvpKeyOffset : 0;

    private static string CommonId(LayoutElement element) => element.PositionGroup ?? element.Id;

    /// <summary>True when the job being played has its own position for this element (in this zone, for the elements that tell PvP apart).</summary>
    public bool HasOwnPosition(LayoutElement element) =>
        CurrentJob != 0 && config.JobLayouts.TryGetValue(element.Id, out var own) && own.ContainsKey(KeyFor(element));

    /// <summary>True when some job has its own position for this element.</summary>
    public bool HasAnyOwnPosition(string id) => config.JobLayouts.TryGetValue(id, out var own) && own.Count > 0;

    /// <summary>
    /// Turns the own position of the job being played on or off. On: the job starts from what is shown now. Off: the job goes back to the common position.
    /// </summary>
    public void SetOwnPosition(LayoutElement element, bool on)
    {
        if (CurrentJob == 0)
            return;

        var key = KeyFor(element);

        if (!on)
        {
            if (config.JobLayouts.TryGetValue(element.Id, out var existing))
            {
                existing.Remove(key);
                if (existing.Count == 0)
                    config.JobLayouts.Remove(element.Id);
            }

            loadedFrom.Remove(element.Id);
            return;
        }

        EnsureCommon(element);

        if (!config.JobLayouts.TryGetValue(element.Id, out var own))
        {
            own = new Dictionary<uint, ElementLayout>();
            config.JobLayouts[element.Id] = own;
        }

        var layout = element.Layout.Clone();
        own[key] = layout;
        loadedFrom[element.Id] = layout;
    }

    // The common positions of an element: the one it shows now, saved the first time it is needed.
    private Dictionary<uint, ElementLayout> EnsureCommon(LayoutElement element)
    {
        var id = CommonId(element);
        if (!config.CommonLayouts.TryGetValue(id, out var common))
        {
            common = new Dictionary<uint, ElementLayout> { [0] = element.Layout.Clone() };
            config.CommonLayouts[id] = common;
        }

        return common;
    }

    private static bool SamePlace(ElementLayout a, ElementLayout b) =>
        a.AttachTo == b.AttachTo && a.ScreenAnchor == b.ScreenAnchor && a.SelfAnchor == b.SelfAnchor && a.OffsetX == b.OffsetX && a.OffsetY == b.OffsetY;

    private static void CopyPlace(ElementLayout from, ElementLayout to)
    {
        to.AttachTo = from.AttachTo;
        to.ScreenAnchor = from.ScreenAnchor;
        to.SelfAnchor = from.SelfAnchor;
        to.OffsetX = from.OffsetX;
        to.OffsetY = from.OffsetY;
    }

    private static void Show(LayoutElement element, ElementLayout source)
    {
        if (element.PlacementOnly)
            CopyPlace(source, element.Layout);
        else
            element.Layout.CopyPositionFrom(source);
    }

    private static void Save(LayoutElement element, ElementLayout target)
    {
        if (element.PlacementOnly)
            CopyPlace(element.Layout, target);
        else
            target.CopyPositionFrom(element.Layout);
    }

    /// <summary>
    /// Places an element for a job from a preset (the job setup). <paramref name="forCommon"/> draws the common place (a column without job
    /// gauges), <paramref name="forJob"/> the place for this job. The common position is set only while it is still where the element
    /// starts (or missing, in PvP). If it is the one of the preset, a job whose place is different (it has gauges) gets its own position.
    /// A common position the user placed is left alone.
    /// </summary>
    public void PlaceForJob(LayoutElement element, uint jobId, bool pvp, Action<ElementLayout> forCommon, Action<ElementLayout> forJob)
    {
        var common = EnsureCommon(element);
        var separate = pvp && element.SeparatePvp;
        var commonKey = separate ? PvpKeyOffset : 0u;
        var jobKey = separate ? jobId + PvpKeyOffset : jobId;

        var presetCommon = common[0].Clone();
        forCommon(presetCommon);

        if (!common.TryGetValue(commonKey, out var commonLayout))
        {
            commonLayout = presetCommon.Clone();
            common[commonKey] = commonLayout;
        }
        else if (!separate && SamePlace(commonLayout, element.Defaults))
        {
            // Never moved: the preset's column becomes the common one (with the size the preset gives it).
            commonLayout.CopyPositionFrom(presetCommon);
        }

        if (SamePlace(commonLayout, presetCommon))
        {
            var wanted = commonLayout.Clone();
            forJob(wanted);

            if (SamePlace(wanted, commonLayout))
            {
                if (config.JobLayouts.TryGetValue(element.Id, out var existing))
                {
                    existing.Remove(jobKey);
                    if (existing.Count == 0)
                        config.JobLayouts.Remove(element.Id);
                }
            }
            else
            {
                if (!config.JobLayouts.TryGetValue(element.Id, out var own))
                {
                    own = new Dictionary<uint, ElementLayout>();
                    config.JobLayouts[element.Id] = own;
                }

                own[jobKey] = wanted;
            }
        }

        // Shown again from the saved positions on the next frame.
        loadedFrom.Remove(element.Id);
    }

    /// <summary>
    /// Call once per frame with the job being played (0 when not known) and whether it is in PvP. Every element shows the position of the
    /// job if the job has its own, and the common one otherwise; what is moved or edited is saved in the position that is shown.
    /// </summary>
    public void SyncJob(uint jobId, bool pvp)
    {
        CurrentJob = jobId;
        InPvp = pvp;

        if (jobId == 0)
            return;

        foreach (var element in elements)
        {
            if (element.IsManaged)
                continue;

            config.JobLayouts.TryGetValue(element.Id, out var own);

            ElementLayout source;
            if (own is not null && own.TryGetValue(KeyFor(element), out var ownLayout))
            {
                source = ownLayout;
                EnsureCommon(element);
            }
            else
            {
                // Only the elements that can differ from one job or zone to the next keep a common position apart from the layout.
                if (own is null && element.PositionGroup is null && !element.SeparatePvp && !config.CommonLayouts.ContainsKey(element.Id))
                {
                    loadedFrom.Remove(element.Id);
                    continue;
                }

                var common = EnsureCommon(element);
                var commonKey = CommonKeyFor(element);
                if (!common.TryGetValue(commonKey, out var commonLayout))
                {
                    // PvP starts from where PvE is.
                    commonLayout = common[0].Clone();
                    common[commonKey] = commonLayout;
                }

                source = commonLayout;
            }

            if (!loadedFrom.TryGetValue(element.Id, out var loaded) || !ReferenceEquals(loaded, source))
            {
                Show(element, source);
                loadedFrom[element.Id] = source;
            }
            else if (element.IsHidden)
            {
                // What belongs to another job is only shown (a group shared by the jobs is changed by the one that is in use).
                Show(element, source);
            }
            else
            {
                Save(element, source);
            }
        }
    }

    /// <summary>The box unlock mode draws for an element: its display rect if the module gave one, else its layout box.</summary>
    public static (Vector2 Pos, Vector2 Size) GetDisplayRect(LayoutElement element) =>
        element.DisplayRect ?? GetRect(element.Layout);

    /// <summary>
    /// Final position and size in physical screen pixels, rounded to whole pixels
    /// so edges and text stay sharp.
    /// </summary>
    public static (Vector2 Pos, Vector2 Size) GetRect(ElementLayout layout)
    {
        var scale = UiScale.Overlay;
        var viewport = ImGui.GetMainViewport();

        var screenPoint = viewport.Pos + viewport.Size * layout.ScreenAnchor.Fraction();

        // Attached to another element: the anchor point is a point of that element.
        if (!string.IsNullOrEmpty(layout.AttachTo) && Current is not null && attachDepth < 8)
        {
            var parent = Current.elements.Find(e => e.Id == layout.AttachTo);
            if (parent is not null && !ReferenceEquals(parent.Layout, layout))
            {
                attachDepth++;
                try
                {
                    var (parentPos, parentSize) = GetDisplayRect(parent);
                    screenPoint = parentPos + parentSize * layout.ScreenAnchor.Fraction();
                }
                finally
                {
                    attachDepth--;
                }
            }
        }

        var size = new Vector2(
            MathF.Round(Math.Max(1, layout.Width) * scale),
            MathF.Round(Math.Max(1, layout.Height) * scale));

        var offset = new Vector2(layout.OffsetX, layout.OffsetY) * scale;
        var pos = screenPoint + offset - size * layout.SelfAnchor.Fraction();

        return (new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y)), size);
    }
}
