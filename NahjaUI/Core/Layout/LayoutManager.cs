using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

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
    public static bool IsShownNow(LayoutElement element) => Current?.IsShown(element) ?? true;

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

        if (SelectedId == id)
            SelectedId = null;
    }

    /// <summary>Removes an element and forgets its saved position (used when a tracker is deleted).</summary>
    public void Unregister(string id)
    {
        elements.RemoveAll(e => e.Id == id);
        config.Layouts.Remove(id);
        config.JobLayouts.Remove(id);
        loadedFor.Remove(id);

        if (SelectedId == id)
            SelectedId = null;
    }

    // ---------------------------------------------------------------- one position for each job

    // The key whose position each element currently shows.
    private readonly Dictionary<string, uint> loadedFor = new();

    /// <summary>A position "for each job" is saved under a key: the job id in PvE, and the job id plus this number in PvP.</summary>
    public const uint PvpKeyOffset = 1000;

    /// <summary>The key of a job's position in PvE or in PvP (0, the base, when no job is known).</summary>
    public static uint KeyOf(uint jobId, bool pvp) => jobId != 0 && pvp ? jobId + PvpKeyOffset : jobId;

    /// <summary>The key (job and zone) the positions show right now.</summary>
    public uint CurrentKey { get; private set; }

    /// <summary>True when the element keeps a different position for each job.</summary>
    public bool IsPerJob(string id) => config.JobLayouts.ContainsKey(id);

    /// <summary>
    /// Turns "a position for each job" on or off for an element. On: the current position becomes the base (what a job that
    /// was never moved starts from) and the position of the key being played (job and zone). Off: the element keeps what it shows now.
    /// </summary>
    public void SetPerJob(LayoutElement element, bool on, uint key)
    {
        if (!on)
        {
            config.JobLayouts.Remove(element.Id);
            loadedFor.Remove(element.Id);
            return;
        }

        var jobs = new Dictionary<uint, ElementLayout> { [0] = element.Layout.Clone() };
        if (key != 0)
            jobs[key] = element.Layout.Clone();

        config.JobLayouts[element.Id] = jobs;
        loadedFor[element.Id] = key;
    }

    /// <summary>
    /// Call once per frame with the job being played (0 when not known) and whether it is in PvP. The elements with a position for
    /// each job show the one of that job and zone when either changes; while they stay, what is moved or edited is stored for them.
    /// A job that was never moved in PvP starts from its position in PvE.
    /// </summary>
    public void SyncJob(uint jobId, bool pvp)
    {
        var key = KeyOf(jobId, pvp);
        CurrentKey = key;

        if (jobId == 0 || config.JobLayouts.Count == 0)
            return;

        foreach (var element in elements)
        {
            if (!config.JobLayouts.TryGetValue(element.Id, out var jobs))
                continue;

            if (!loadedFor.TryGetValue(element.Id, out var loaded) || loaded != key)
            {
                // A different job or zone (or the first frame): show its position; if it never had its own, the one of the job in PvE
                // (for PvP), and the base one after that.
                if (!jobs.TryGetValue(key, out var own))
                {
                    if (pvp && jobs.TryGetValue(jobId, out var pveLayout))
                        own = pveLayout.Clone();
                    else
                        own = jobs.TryGetValue(0, out var baseLayout) ? baseLayout.Clone() : element.Layout.Clone();

                    jobs[key] = own;
                }

                element.Layout.CopyPositionFrom(own);
                loadedFor[element.Id] = key;
            }
            else
            {
                // The same job and zone: keep its saved position equal to what is shown (moved by hand or edited).
                if (!jobs.TryGetValue(key, out var own))
                {
                    own = element.Layout.Clone();
                    jobs[key] = own;
                }

                own.CopyPositionFrom(element.Layout);
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
        var scale = ImGuiHelpers.GlobalScale;
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
