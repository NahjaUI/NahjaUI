using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Ui;

namespace NahjaUI.Core.Profiles;

/// <summary>
/// The "Profiles" page of the main window: copy your configuration as a text code (to keep it, move it to another
/// computer, or share it) and paste a code to import it.
/// </summary>
internal sealed class ProfilesPage
{
    // Big enough for a full configuration with hundreds of auras.
    private const int MaxCodeLength = 1_000_000;

    private readonly Plugin plugin;
    private readonly ProfileManager manager;

    // Export.
    private readonly HashSet<ProfileBlock> exportBlocks = new(Enum.GetValues<ProfileBlock>());
    private string exportCode = string.Empty;
    private string exportMessage = string.Empty;

    // Import.
    private string importCode = string.Empty;
    private ProfilePreview? preview;
    private readonly HashSet<ProfileBlock> importBlocks = new();
    private AuraImportMode auraMode = AuraImportMode.Add;
    private string importMessage = string.Empty;
    private bool importOk;

    public ProfilesPage(Plugin plugin)
    {
        this.plugin = plugin;
        manager = new ProfileManager(plugin);
    }

    public void Draw()
    {
        DrawExport();
        DrawImport();
        DrawBackups();
    }

    // ---------------------------------------------------------------- export

    private void DrawExport()
    {
        Widgets.Card("##card_profile_export", 14, () =>
        {
            Widgets.SectionLabel("Export");
            ImGui.TextColored(Theme.TextMuted, "Choose what goes into the code. With everything ticked it is a full backup of your settings.");
            ImGui.Spacing();

            foreach (var block in Enum.GetValues<ProfileBlock>())
            {
                var on = exportBlocks.Contains(block);
                if (ImGui.Checkbox($"{ProfileManager.Label(block)}##export_{block}", ref on))
                {
                    if (on)
                        exportBlocks.Add(block);
                    else
                        exportBlocks.Remove(block);
                }
            }

            ImGui.Spacing();

            ImGui.BeginDisabled(exportBlocks.Count == 0);
            if (Widgets.PrimaryButton("Create code"))
            {
                exportCode = manager.Export(exportBlocks);
                exportMessage = string.Empty;
            }
            ImGui.EndDisabled();

            if (exportCode.Length == 0)
                return;

            ImGui.Spacing();

            // Read only: it is there to be copied.
            var width = ImGui.GetContentRegionAvail().X;
            ImGui.InputTextMultiline("##export_code", ref exportCode, MaxCodeLength, new Vector2(width, 90f * ImGuiHelpers.GlobalScale), ImGuiInputTextFlags.ReadOnly);

            if (ImGui.Button("Copy to clipboard"))
            {
                ImGui.SetClipboardText(exportCode);
                exportMessage = "Copied. Paste it in Discord, or keep it somewhere safe.";
            }

            ImGui.SameLine();

            if (ImGui.Button("Save to a file"))
            {
                try
                {
                    var path = manager.SaveToFile(ProfileManager.ExportDirectory, "profile", exportCode);
                    exportMessage = $"Saved to {path}";
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, "Could not save the NahjaUI export file.");
                    exportMessage = "The file could not be saved.";
                }
            }

            if (exportMessage.Length > 0)
                ImGui.TextWrapped(exportMessage);
        });
    }

    // ---------------------------------------------------------------- import

    private void DrawImport()
    {
        Widgets.Card("##card_profile_import", 14, () =>
        {
            Widgets.SectionLabel("Import");
            ImGui.TextColored(Theme.TextMuted, "Paste a code, check what is inside, and choose what to take. Nothing changes until you press Import.");
            ImGui.Spacing();

            var width = ImGui.GetContentRegionAvail().X;
            if (ImGui.InputTextMultiline("##import_code", ref importCode, MaxCodeLength, new Vector2(width, 90f * ImGuiHelpers.GlobalScale), ImGuiInputTextFlags.None))
            {
                // A new text means the old check no longer applies.
                preview = null;
                importMessage = string.Empty;
            }

            if (ImGui.Button("Paste from clipboard"))
            {
                importCode = ImGui.GetClipboardText() ?? string.Empty;
                preview = null;
                importMessage = string.Empty;
            }

            ImGui.SameLine();

            ImGui.BeginDisabled(importCode.Length == 0);
            if (ImGui.Button("Check code"))
                CheckCode();
            ImGui.EndDisabled();

            if (preview is not null && !preview.Ok)
                ImGui.TextColored(Theme.Error, preview.Error);

            if (preview is { Ok: true })
                DrawPreview(preview);

            if (importMessage.Length > 0)
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, importOk ? Theme.Success : Theme.Error);
                ImGui.TextWrapped(importMessage);
                ImGui.PopStyleColor();
            }
        });
    }

    private void CheckCode()
    {
        preview = manager.Read(importCode);
        importMessage = string.Empty;
        importBlocks.Clear();

        if (!preview.Ok)
            return;

        foreach (var block in preview.Present)
            importBlocks.Add(block);

        // A code with positions is a backup of a whole setup: restore it as it was. Without positions it is a set of
        // auras made to be shared: add them to mine.
        auraMode = preview.Present.Contains(ProfileBlock.Placement) ? AuraImportMode.Replace : AuraImportMode.Add;
    }

    private void DrawPreview(ProfilePreview shown)
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.Accent, "This code contains");

        foreach (var block in Enum.GetValues<ProfileBlock>())
        {
            if (!shown.Present.Contains(block))
                continue;

            var on = importBlocks.Contains(block);
            if (ImGui.Checkbox($"{shown.Summary[IndexOf(shown, block)]}##import_{block}", ref on))
            {
                if (on)
                    importBlocks.Add(block);
                else
                    importBlocks.Remove(block);
            }
        }

        if (importBlocks.Contains(ProfileBlock.Auras))
        {
            ImGui.Spacing();

            if (ImGui.RadioButton("Add these auras to mine", auraMode == AuraImportMode.Add))
                auraMode = AuraImportMode.Add;

            if (ImGui.RadioButton("Replace all my auras with these", auraMode == AuraImportMode.Replace))
                auraMode = AuraImportMode.Replace;

            if (auraMode == AuraImportMode.Add && importBlocks.Contains(ProfileBlock.Placement))
                ImGui.TextColored(Theme.TextMuted, "Added auras get no position from the code: you place them yourself.");
        }

        ImGui.Spacing();

        ImGui.BeginDisabled(importBlocks.Count == 0);
        if (Widgets.PrimaryButton("Import"))
        {
            importMessage = manager.Apply(shown, importBlocks, auraMode, out importOk);

            if (importOk)
            {
                preview = null;
                importCode = string.Empty;
            }
        }
        ImGui.EndDisabled();
    }

    // The summary lines come in the same order as the blocks that are present.
    private static int IndexOf(ProfilePreview shown, ProfileBlock block)
    {
        var index = 0;
        foreach (var candidate in Enum.GetValues<ProfileBlock>())
        {
            if (candidate == block)
                return index;

            if (shown.Present.Contains(candidate))
                index++;
        }

        return 0;
    }

    // ---------------------------------------------------------------- backups

    private void DrawBackups()
    {
        var backups = manager.ListBackups();

        Widgets.Card("##card_profile_backups", 6 + Math.Min(backups.Count, 8), () =>
        {
            Widgets.SectionLabel("Automatic backups");
            ImGui.TextColored(Theme.TextMuted, "Before every import your settings are saved here. Load one to undo an import.");
            ImGui.TextColored(Theme.TextMuted, ProfileManager.BackupDirectory);
            ImGui.Spacing();

            if (backups.Count == 0)
            {
                ImGui.TextColored(Theme.TextMuted, "No backups yet.");
                return;
            }

            var shownCount = 0;
            foreach (var file in backups)
            {
                if (shownCount++ >= 8)
                    break;

                ImGui.Text(file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                ImGui.SameLine();

                if (ImGui.Button($"Load into the import box##backup_{file.Name}"))
                    LoadBackup(file);
            }
        });
    }

    private void LoadBackup(FileInfo file)
    {
        try
        {
            importCode = File.ReadAllText(file.FullName);
            CheckCode();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not read a NahjaUI backup.");
            importMessage = "That backup file could not be read.";
            importOk = false;
        }
    }
}
