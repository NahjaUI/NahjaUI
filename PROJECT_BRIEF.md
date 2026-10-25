# NahjaUI - project brief

Paste this at the start of every AI session. Last updated: 2026-10-28.
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
  Modules/LayoutDemo.cs                         (temporary test boxes, removed in H4)
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
- H3 Game data snapshot: IN PROGRESS. Provider builds a snapshot per frame (player, target, focus, target of target, party, statuses, casts, context). Debug page shows it live. Cooldowns and job gauge are NOT in H3 (H5).
- H4 First auras: status trigger, icon display, basic load conditions.
- H5 Cooldown and resource triggers, bars.
- H6 Full aura editor and creation wizard.
- H7 Groups, profiles, import/export.
- H8 Polish, performance, templates per job.

Move to the next milestone only when the previous one is stable and committed in Git.

## Session start checklist

1. Which milestone are we on? (see above)
2. Paste any file you want changed, plus the exact compiler error if there is one.
3. Ask for one small step.
