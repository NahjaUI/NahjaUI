using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// A number field that does not apply what is being typed digit by digit.
/// The value changes when Enter is pressed, when the field is left, or when the + / - buttons are used.
/// </summary>
internal static class CommitInput
{
    // What is being typed in each field: the value it had when the edit began and the text typed so far.
    private static readonly Dictionary<uint, (int Start, int Typed)> Pending = new();

    /// <summary>
    /// Same as <c>ImGui.InputInt</c>, but returns true (and updates <paramref name="value"/>) only once the edit is finished.
    /// </summary>
    public static bool Int(string label, ref int value, int step = 1, int stepFast = 100)
    {
        var id = ImGui.GetID(label);
        var hasPending = Pending.TryGetValue(id, out var pending);
        var shown = hasPending ? pending.Typed : value;

        if (ImGui.InputInt(label, ref shown, step, stepFast))
        {
            Pending[id] = (hasPending ? pending.Start : value, shown);
            hasPending = true;
            pending = Pending[id];
        }

        if (!hasPending || ImGui.IsItemActive())
            return false;

        // The edit is over. If the field now belongs to another element, the typed number is dropped.
        Pending.Remove(id);
        if (pending.Start != value || pending.Typed == pending.Start)
            return false;

        value = pending.Typed;
        return true;
    }
}
