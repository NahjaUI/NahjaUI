# NahjaUI - project brief

Paste this at the start of every AI session. Last updated: 2026-10-31.
Full specification (Spanish): https://claude.ai/artifact/V7JzhmVTNhnXd3v3zKFRCN

## How to work with the owner

- Owner: Sandro. Not a programmer. Speaks Spanish: explain in simple Spanish. Code, comments and UI text are in English.
- Give COMPLETE files ready to paste (not diffs or fragments) and say exactly where each file goes.
- One small change at a time. After each change: build, test in game, git commit.
- Never invent Dalamud or ImGui API names. If unsure, say so and ask Sandro to paste the relevant template code or compiler error.
- When the build fails, ask for the exact compiler message and fix only that.

## What we are building

NahjaUI: a modular interface suite for FFXIV (Dalamud plugin), in one plugin with internal modules. Goals:

1. Modern, intuitive settings UI.
2. Pixel-perfect placement of every element.
3. An aura engine (WeakAuras-like): the player chooses exactly which buffs, debuffs and cooldowns to see.

## Fixed decisions

- Name: NahjaUI. Internal name: NahjaUI. Chat command: /nui. Interface language: English.
- UI only: read game data and draw ImGui overlays. NEVER automate actions or send input.
- Style: dark, rounded, deep indigo palette with ONE user-configurable accent colour (default violet). Structure inspired by Rotation Solver Reborn and EllesmereUI, but the colours must stay clearly different from both.
- Settings structure (reference: EllesmereUI): left sidebar with module on/off switches, fixed live preview at the top of each module page, two-column setting rows (label + control + optional gear), "Apply to: All | Multiple" links, fixed footer with "Reset module" and "Done", CPU usage readout.
- Pixel perfect: integer offsets in logical pixels, anchors (9 points) to screen or to another element, scale with ImGuiHelpers.GlobalScale, round to whole pixels. Unlock mode (ElvUI style): drag boxes; a tag with name and coordinates OUTSIDE each box (works for any size); a panel next to the selected box with X/Y fields (typed value or +/- buttons), Locked and Reset position. Keyboard arrows, an on-screen arrow pad, 10 px steps and grid snapping were all dropped on purpose to keep it simple. Undo/redo later.
- Auras are DATA (JSON), not code. Aura = triggers + load conditions + display + actions + position. Trigger types v1: status (buff/debuff), action cooldown, resource, condition. Groups, including dynamic groups with a whitelist of status ids.
- Game access lives in ONE layer (Game/): a read-only snapshot built once per frame. Modules read the snapshot only and never call Dalamud services directly.
- Errors are isolated per module and per aura (try/catch, log, auto-disable after repeated errors).
- Out of scope for v1: cooldowns of other players, combat log parsing, any automation.

## Technical facts (verified 2026-10-25 against goatcorp/SamplePlugin)

- Project SDK: Dalamud.NET.Sdk/15.0.0. Solution format: .slnx.
- ImGui: `using Dalamud.Bindings.ImGui;`
- Windows derive from `Dalamud.Interface.Windowing.Window` (PreDraw / Draw / PostDraw / OnClose) and are registered in a `WindowSystem`.
- RAII helpers: `ImRaii` in `Dalamud.Interface.Utility.Raii`. Scale: `ImGuiHelpers.GlobalScale` in `Dalamud.Interface.Utility`.
- Services are injected as `[PluginService] internal static` properties on Plugin: IDalamudPluginInterface, ICommandManager, IClientState, IPlayerState, IDataManager, ITextureProvider, IPluginLog.
- Config: class implementing `IPluginConfiguration` (int Version), saved with `PluginInterface.SavePluginConfig(this)`.
- Dev loop: build Debug x64, then /xlsettings > Experimental > Dev Plugin Locations (path to NahjaUI.dll), then /xlplugins > Dev Tools to load or reload.
- The Dalamud API level changes with game patches. Always check dalamud.dev and the current template before using a new API.

## Folder layout

```
NahjaUI/
  Plugin.cs
  Configuration.cs
  Core/Ui/Theme.cs, ThemeScope, Widgets.cs     (theme tokens and reusable components)
  Core/Layout/                                  (AnchorPoint, ElementLayout, LayoutManager, UnlockOverlay)
  Modules/Auras/                                (AuraDefinition data, AuraEngine, AuraEditor page)
  Core/Config/                                  (profiles, import/export)
  Game/                                         (Snapshot.cs records, GameDataProvider.cs, GameNames.cs)
  Modules/Auras/                                (H4+: aura engine)
  Windows/MainWindow.cs
```

## Layout system (H2a)

- A module registers each thing it draws with `LayoutManager.Register(id, name, defaults)` and gets back a `LayoutElement`.
- To draw it: `var (pos, size) = LayoutManager.GetRect(element.Layout);` then draw with the ImGui background draw list at that exact position.
- Layout data (`ElementLayout`): ScreenAnchor, SelfAnchor (9 points each), OffsetX/OffsetY and Width/Height as ints in logical pixels, Locked. Saved in `Configuration.Layouts` by element id.
- Saving: call `plugin.RequestSave()` after any change. The file is written when the mouse button is released (debounced). Never call Save() every frame.
- Theme: `ThemeScope` pushes ImGui colours and style before a window begins (PreDraw). `ContentStyleScope` (button and field padding) is created inside Draw(), never before Begin, or Dalamud's title bar buttons overlap. `Theme.Accent` is kept in sync every frame by Plugin.Draw.

## Game data layer (H3)

- `GameDataProvider` (the ONLY reader of Dalamud game data) rebuilds `GameSnapshot` on every `IFramework.Update`. Read it with `plugin.GameData.Current`.
- Snapshot: IsLoggedIn, Player, Target, Focus, TargetOfTarget (all `UnitSnapshot?`), Party, Context (territory id, InCombat, InDuty, party size), Frame.
- `UnitSnapshot`: EntityId, Name, JobId, Level, Hp/MaxHp, Mp/MaxMp, IsDead, Cast, Statuses. `StatusSnapshot`: Id, SourceId, RemainingTime, Param (stack count for stackable statuses).
- Only battle characters (players, enemies, battle NPCs) become units; other targets give null.
- Party list includes the local player when in a party; it is empty when solo.
- `GameNames` turns status and job ids into names (cached).
- Dalamud APIs used (verified against goatcorp/Dalamud on 2026-10-28): `IObjectTable.LocalPlayer`, `ITargetManager.Target/FocusTarget`, `IGameObject.TargetObject`, `IBattleChara` (StatusList, IsCasting, CastActionId, CurrentCastTime, TotalCastTime, IsCastInterruptible), `IPartyList` / `IPartyMember`, `ICondition[ConditionFlag.InCombat / BoundByDuty]`, `IClientState.TerritoryType`, `IPlayerState.IsLoaded`.

## Aura engine (H4)

- `AuraDefinition` (data, saved in `Configuration.Auras`): Id, Name, Enabled, Unit (Player/Target/Focus/TargetOfTarget), StatusId, OnlyMine, ShowMode (WhenPresent / WhenMissing), ShowTimer, ShowStacks, Load (JobIds, Combat mode). Position and size are NOT in the definition: each aura registers a `LayoutElement` with id `aura.<Id>`.
- `AuraEngine` (Plugin.AuraSystem): per frame, for each enabled aura: evaluate against `plugin.GameData.Current`, draw the icon with the background draw list at `LayoutManager.GetRect`. Errors are caught per aura; 5 in a row switches that aura off.
- `AuraEditor` (Plugin.AuraEditorPage): the Auras page. Statuses can be picked from what the unit has right now or found by name (GameNames.SearchStatuses).
- When the unit does not exist (no target), nothing is shown, not even a "missing" reminder.
- Status icon: `GameNames.StatusIconId` (Lumina Status sheet `Icon`), drawn with `ITextureProvider.GetFromGameIcon` + `ImDrawListPtr.AddImage`. Icons are portrait 24x32: they are CROPPED (UV rectangle centred, 8 % inset) to the shape of the box, never stretched. 1 px black border (red when the status is missing).
- Per-aura display settings: `TextSize` (font size in px, a fixed step from `OverlayFonts.AvailableSizes`, default 12; never scaled, always a real baked font) and `IconCropPercent` (default 13; the game's icons have rounded corners baked into the image, cropping more hides them).
- Timer and stack text use a game font chosen per aura (`AuraDefinition.Font`: GameNumbers = Meidinger, GameUi = Axis, GameUiBold, Condensed = Trump Gothic). `OverlayFonts` prepares each family lazily at fixed sizes and pushes it with `IFontHandle.Push()`. Icon size (square) is edited on the Display card of the aura, it is stored in the layout element (Width/Height), rectangles are still possible in the Layout page with a thick black outline. Falls back to the normal font until the game fonts have loaded. The timer is ONE plain text ("17m"), with no spacing workaround: the owner removed the earlier trick that drew the "m" as a separate smaller part, on purpose, so that a future font change can be judged as it really looks. Meidinger numerals are fixed width (that is why "1" has extra space) and digits touch letters.
- Stack numbers only show for statuses with `MaxStacks > 1` (`GameNames.MaxStacks`). For other statuses `Param` means something else (Sprint: +30 % speed).
- Groups (`AuraGroupDefinition` in `Configuration.AuraGroups`, `AuraDefinition.GroupId`): a group has ONE layout element (its box, moved in unlock mode) and lines its auras up from it. Growth: ToRight, ToLeft, FromCenterHorizontal, Down, Up, FromCenterVertical; spacing in px. Auras in a group keep their own size but not their position (`LayoutElement.IsManaged`, hidden from unlock mode and the Layout list). Order inside a group = order of the aura list. The order of the top level (groups and loose auras mixed in any order) = `Configuration.RootOrder` (entries "group:<id>" / "aura:<id>"; auras inside a group are not listed there). Both are changed by drag and drop in the explorer. Only visible auras take a slot, so a centred group stays centred. In unlock mode every enabled member is drawn as a ghost so the group can be placed.
- Cooldown auras (`AuraDefinition.Trigger = Cooldown`): `ActionId` = the ability to track; optional `StatusId`/`Unit`/`OnlyMine` = the ability's EFFECT (a buff): while it is on, the icon is in phase Active and shows the effect's remaining time. Phases: Ready (usable), Cooling (no charge available, shows time to next charge and a dark sweep), Active (effect on). Options: CooldownShow (Always / OnlyOnCooldown / OnlyWhenReady), CooldownLook while Cooling (Normal / Dimmed / Red), ShowCooldownSweep, GlowWhenReady, GlowWhenActive (gold pulsing glow), IgnoreGlobalCooldown (cooldowns up to 3.5 s count as ready), ShowStacks = show charges (only when the ability has more than 1).
- Cooldown data flow: the engine calls `plugin.GameData.SetWatchedActions(ids)` every frame; `GameDataProvider.BuildCooldowns()` (unsafe, FFXIVClientStructs `ActionManager`: GetAdjustedActionId, GetMaxCharges (static), GetCurrentCharges, GetRecastTime, GetRecastTimeElapsed, IsRecastTimerActive) fills `GameSnapshot.Cooldowns[actionId]` = `CooldownSnapshot(ActionId, Charges, MaxCharges, Remaining, Total)` one frame later. For multi-charge abilities the game keeps ONE timer for all charges (Total = per charge = recast / maxCharges). Not done yet: usability by resources/range (GetActionStatus), role actions in the ability list (they are found by search).
- Ability picker: `GameNames.AbilitiesOfJob(job, level)` lists the job's abilities (Action sheet: IsPlayerAction, not PvP, ClassJob = job or its parent class, Recast100ms > 35) and `GameNames.SearchActions` searches by name.
- Explorer (Auras page, `AuraEditor`): one list like a file explorer. Left: groups and loose auras mixed freely, groups open/close (`AuraGroupDefinition.Expanded`, saved), rows show the game's own icon (action icon for cooldown auras, status icon for buff/debuff auras, cropped with `AuraEngine.IconUv`) and a tag (Cooldown / Buff / Debuff / Off). Right: the editor of the selection (two columns when the page is at least 720 px wide, otherwise stacked).
- Drag and drop is hand made (no ImGui drag-drop API): drag by the six-dot grip, the drop target is computed from the row rectangles of the frame (`ComputeDrop`). Drop zones: loose row top/bottom half = before/after at the top level; group row top 30 % = before the group, middle = into the group (at its end), bottom 30 % = first place inside (if open) or after (if closed); member row top/bottom half = before/after that member; far-left edge of a member row = take it out, right after its group. A dragged group can only go to the top level. Moves go through `AuraEngine.MoveAuraToRoot / MoveAuraIntoGroup / MoveGroup`.
- A group is a TEMPLATE: `AuraGroupDefinition.IconWidth/IconHeight` plus `AuraGroupDefinition.Template` (an `AuraDefinition` of which only the display settings and the load conditions are used). The group editor shows the same Display and Load cards as an aura, bound to the template; any change calls `AuraEngine.ApplyGroupStyle(group)`, which copies the template (`AuraDefinition.CopyStyleFrom`) and the size to EVERY aura of the group. An aura that joins a group takes the template and the size too. After that each aura can still be edited on its own. Groups made earlier get their template from their first aura at startup (not applied to the others).
- Text placement (per aura, copied by groups): `TimerAnchor/TimerOffsetX/Y` and `StacksAnchor/StacksOffsetX/Y` (stacks or charges). The anchor is one of 9 points of the icon and the SAME point of the text is attached to it (so a right anchor right-justifies, a centre anchor centres); the offset (px, +X right, +Y down) moves it. Defaults: timer centre, stacks bottom right with offset (-2,-1).
- Cooldown reading: `GameDataProvider.BuildCooldowns` reads the recast group's gauge (`GetRecastGroup`, `GetRecastGroupDetail`, `GetRecastTimeForGroup`) and does NOT rely on `IsRecastTimerActive` (it did not report an ability with one charge spent and one left). The Debug page lists the raw gauge values of every watched ability.
- Cooldown auras: the "effect" status is optional. Its unit (`Unit`: Player / Target / Focus / Target of target) and `OnlyMine` are edited in the effect card (not in General) because they only matter when an effect is chosen. The unit says where to look for the buff the ability leaves after being used; individual party members are not tracked yet.
- "New aura" goes into the selected group (or the group of the selected aura), otherwise to the end of the top level.
- Planned next (H5 rest): cooldown and resource triggers, bars, several triggers per aura.

## Code rules

- Integers for pixel values. Scale only when drawing.
- Save configuration debounced (never every frame).
- Wrap each module and aura update in try/catch; log through `Plugin.Log`.
- Prefer small classes with one job. Comment the "why", not the "what".
- No new libraries without asking Sandro.

## Milestones and status

- H0 Environment: DONE (template compiled and loaded in game).
- H1 Core foundation: DONE (config, theme, main window with sidebar, /nui).
- H2 Positioning and unlock mode: DONE for now (can be refined later). H2a = elements with anchors and integer offsets, unlock overlay (drag, outside coordinate tags, inspector panel with X/Y fields, Locked and Reset position), Layout page, /nui unlock. H2b (next) = anchoring to other elements, alignment guides to other elements, match size and align, undo/redo.
- H3 Game data snapshot: DONE. Provider builds a snapshot per frame (player, target, focus, target of target, party, statuses, casts, context). Debug page shows it live. Cooldowns and job gauge are NOT in H3 (H5).
- H4 First auras: IN PROGRESS. Status trigger on player/target/focus/target of target, icon with timer and stacks, load conditions (jobs, combat), editor page with status picker (current statuses + name search). LayoutDemo test boxes removed.
- H5 Cooldown and resource triggers, bars.
- H6 Full aura editor and creation wizard.
- H7 Groups, profiles, import/export.
- H8 Polish, performance, templates per job.

Move to the next milestone only when the previous one is stable and committed in Git.

## Session start checklist

1. Which milestone are we on? (see above)
2. Paste any file you want changed, plus the exact compiler error if there is one.
3. Ask for one small step.
