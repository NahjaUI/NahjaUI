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
    /// True when something else (an aura group) decides where this element is.
    /// Such elements keep their size but cannot be moved, so unlock mode and the Layout page skip them.
    /// </summary>
    public bool IsManaged { get; set; }

    /// <summary>
    /// True when the element belongs to something that does not apply right now (an aura or gauge of another job).
    /// Hidden elements are left out of unlock mode and the Layout page, so only what you can use is shown.
    /// Their saved position is kept.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Set every frame by a module whose drawn area is not the same as its layout box (an aura group: the box that
    /// unlock mode shows is the whole area covered by its icons). Physical pixels. Null means "use the layout".
    /// </summary>
    public (Vector2 Pos, Vector2 Size)? DisplayRect { get; set; }

    /// <summary>The saved, editable layout (it lives inside the plugin configuration).</summary>
    public ElementLayout Layout { get; }

    public void ResetToDefaults() => Layout.CopyPositionFrom(Defaults);
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
    }

    public IReadOnlyList<LayoutElement> Elements => elements;

    /// <summary>The elements the user can move by hand (those not placed by a group and not belonging to another job).</summary>
    public IReadOnlyList<LayoutElement> Positionable => elements.Where(e => !e.IsManaged && !e.IsHidden).ToList();

    /// <summary>True while the user is moving elements around.</summary>
    public bool UnlockMode { get; set; }

    public string? SelectedId { get; set; }

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

    /// <summary>Removes an element from the list but keeps its saved position (used when auras are reloaded).</summary>
    public void Detach(string id)
    {
        elements.RemoveAll(e => e.Id == id);

        if (SelectedId == id)
            SelectedId = null;
    }

    /// <summary>Removes an element and forgets its saved position (used when an aura is deleted).</summary>
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

    // The job whose position each element currently shows.
    private readonly Dictionary<string, uint> loadedFor = new();

    /// <summary>True when the element keeps a different position for each job.</summary>
    public bool IsPerJob(string id) => config.JobLayouts.ContainsKey(id);

    /// <summary>
    /// Turns "a position for each job" on or off for an element. On: the current position becomes the base (what a job that
    /// was never moved starts from) and the position of the job being played. Off: the element keeps what it shows now.
    /// </summary>
    public void SetPerJob(LayoutElement element, bool on, uint jobId)
    {
        if (!on)
        {
            config.JobLayouts.Remove(element.Id);
            loadedFor.Remove(element.Id);
            return;
        }

        var jobs = new Dictionary<uint, ElementLayout> { [0] = element.Layout.Clone() };
        if (jobId != 0)
            jobs[jobId] = element.Layout.Clone();

        config.JobLayouts[element.Id] = jobs;
        loadedFor[element.Id] = jobId;
    }

    /// <summary>
    /// Call once per frame with the job being played (0 when not known). The elements with a position for each job show
    /// the one of that job when the job changes; while the job stays, what is moved or edited is stored for that job.
    /// </summary>
    public void SyncJob(uint jobId)
    {
        if (jobId == 0 || config.JobLayouts.Count == 0)
            return;

        foreach (var element in elements)
        {
            if (!config.JobLayouts.TryGetValue(element.Id, out var jobs))
                continue;

            if (!loadedFor.TryGetValue(element.Id, out var loaded) || loaded != jobId)
            {
                // A different job (or the first frame): show its position, or the base one if it never had its own.
                if (!jobs.TryGetValue(jobId, out var own))
                {
                    own = jobs.TryGetValue(0, out var baseLayout) ? baseLayout.Clone() : element.Layout.Clone();
                    jobs[jobId] = own;
                }

                element.Layout.CopyPositionFrom(own);
                loadedFor[element.Id] = jobId;
            }
            else
            {
                // The same job: keep its saved position equal to what is shown (moved by hand or edited).
                if (!jobs.TryGetValue(jobId, out var own))
                {
                    own = element.Layout.Clone();
                    jobs[jobId] = own;
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

        var size = new Vector2(
            MathF.Round(Math.Max(1, layout.Width) * scale),
            MathF.Round(Math.Max(1, layout.Height) * scale));

        var offset = new Vector2(layout.OffsetX, layout.OffsetY) * scale;
        var pos = screenPoint + offset - size * layout.SelfAnchor.Fraction();

        return (new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y)), size);
    }
}
