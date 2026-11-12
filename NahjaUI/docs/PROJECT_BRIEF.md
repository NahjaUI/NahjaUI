# NahjaUI - project brief

Paste this at the start of every AI session. Last updated: 2026-10-26.

## Working rules

- Code, comments and UI text are in English.
- Give COMPLETE files ready to paste (not diffs or fragments) and say exactly where each file goes.
- One small change at a time. After each change: build, test in game, git commit.
- Never invent Dalamud or ImGui API names. If unsure, say so and ask for the relevant template code or compiler error.
- When the build fails, ask for the exact compiler message and fix only that.

## What we are building

NahjaUI: a modular interface suite for FFXIV (Dalamud plugin), in one plugin with internal modules. Goals:

1. Modern, intuitive settings UI.
2. Pixel-perfect placement of every element.
3. A tracker engine (WeakTrackers-like): the player chooses exactly which buffs, debuffs and cooldowns to see.

## Fixed decisions

- Name: NahjaUI. Internal name: NahjaUI. Chat command: /nui. Interface language: English.
- UI only: read game data and draw ImGui overlays. NEVER automate actions or send input.
- Style: dark, rounded, deep indigo palette with ONE user-configurable accent colour (default violet). Structure inspired by Rotation Solver Reborn and EllesmereUI, but the colours must stay clearly different from both.
- Settings structure (reference: EllesmereUI): left sidebar with module on/off switches, fixed live preview at the top of each module page, two-column setting rows (label + control + optional gear), "Apply to: All | Multiple" links, fixed footer with "Reset module" and "Done", CPU usage readout.
- Pixel perfect: integer offsets in logical pixels, anchors (9 points) to screen or to another element, scale with ImGuiHelpers.GlobalScale, round to whole pixels. Unlock mode (ElvUI style): drag plain boxes with NO text on them (the always-visible name/coordinate tags were removed on purpose: with many elements they overlapped and hid each other). A panel (name, X/Y fields with typed value or +/- buttons, Locked, Reset position) appears only next to the box that is being dragged or under the mouse, stays 0.4 s after the mouse leaves (and while the mouse is on the panel or a field is being edited), then falls back to the selected box; with nothing selected and nothing under the mouse there is no panel. Boxes are drawn biggest first and use `NoBringToFrontOnFocus`, so small boxes always stay on top of big ones and can be clicked. Keyboard arrows, an on-screen arrow pad, 10 px steps and grid snapping were all dropped on purpose to keep it simple. Undo/redo later.
- Trackers are DATA (JSON), not code. Tracker = triggers + load conditions + display + actions + position. Trigger types v1: status (buff/debuff), action cooldown, resource, condition. Groups, including dynamic groups with a whitelist of status ids.
- Game access lives in ONE layer (Game/): a read-only snapshot built once per frame. Modules read the snapshot only and never call Dalamud services directly.
- Errors are isolated per module and per tracker (try/catch, log, auto-disable after repeated errors).
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
  Core/Ui/OutlinedText.cs                       (the ONE place that draws text with a thin black outline: ONE PHYSICAL pixel whatever the UI scale (the owner runs 200%); the owner chose a fixed thin outline over per-text options)
  Core/Layout/                                  (AnchorPoint, ElementLayout, LayoutManager, UnlockOverlay)
  Modules/Trackers/                                (TrackerDefinition data, TrackerEngine, TrackerEditor page)
  Modules/UnitFrames/                           (player, target, target of target, focus frames and their editor page)
  Modules/PartyFrames/                          (party bars with buffs and debuffs: PartyFrameModule, PartyFrameSettings)
  Core/Config/                                  (profiles, import/export)
  Game/                                         (Snapshot.cs records, GameDataProvider.cs, GameNames.cs)
  Modules/Trackers/                                (H4+: tracker engine)
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

## Tracker engine (H4)

- `TrackerDefinition` (data, saved in `Configuration.Trackers`): Id, Name, Enabled, Unit (Player/Target/Focus/TargetOfTarget), StatusId, OnlyMine, ShowMode (WhenPresent / WhenMissing), ShowTimer, ShowStacks, Load (JobIds, Combat mode). Position and size are NOT in the definition: each tracker registers a `LayoutElement` with id `tracker.<Id>`.
- `TrackerEngine` (Plugin.TrackerSystem): per frame, for each enabled tracker: evaluate against `plugin.GameData.Current`, draw the icon with the background draw list at `LayoutManager.GetRect`. Errors are caught per tracker; 5 in a row switches that tracker off.
- `TrackerEditor` (Plugin.TrackerEditorPage): the Trackers page. Statuses can be picked from what the unit has right now or found by name (GameNames.SearchStatuses).
- When the unit does not exist (no target), nothing is shown, not even a "missing" reminder.
- Status icon: `GameNames.StatusIconId` (Lumina Status sheet `Icon`), drawn with `ITextureProvider.GetFromGameIcon` + `ImDrawListPtr.AddImage`. Icons are portrait 24x32: they are CROPPED (UV rectangle centred, 8 % inset) to the shape of the box, never stretched. 1 px black border (red when the status is missing).
- Per-tracker display settings: `TextSize` (font size in px, a fixed step from `OverlayFonts.AvailableSizes`, default 12; never scaled, always a real baked font) and `IconCropPercent` (default 13; the game's icons have rounded corners baked into the image, cropping more hides them).
- Stack/charge text has its own size: `TrackerDefinition.StackTextSize` (px from `OverlayFonts.AvailableSizes`; 0 = automatic = 75% of `TextSize`, the old behaviour, so existing trackers look the same). Edited in the Display card ("Stack text size (px)"), copied by `CopyStyleFrom`, so group templates carry it. Timer and stacks share the same font.
- Timer and stack text use a game font chosen per tracker (`TrackerDefinition.Font`: GameNumbers = Meidinger, GameUi = Axis, GameUiBold, Condensed = Trump Gothic). `OverlayFonts` prepares each family lazily at fixed sizes and pushes it with `IFontHandle.Push()`. Icon size (square) is edited on the Display card of the tracker, it is stored in the layout element (Width/Height), rectangles are still possible in the Layout page with a thick black outline. Falls back to the normal font until the game fonts have loaded. The timer is ONE plain text ("17m"), with no spacing workaround: the owner removed the earlier trick that drew the "m" as a separate smaller part, on purpose, so that a future font change can be judged as it really looks. Meidinger numerals are fixed width (that is why "1" has extra space) and digits touch letters.
- Stack numbers only show for statuses with `MaxStacks > 1` (`GameNames.MaxStacks`). For other statuses `Param` means something else (Sprint: +30 % speed).
- Groups (`TrackerGroupDefinition` in `Configuration.TrackerGroups`, `TrackerDefinition.GroupId`): a group has ONE layout element (its box, moved in unlock mode) and lines its trackers up from it. Growth: ToRight, ToLeft, FromCenterHorizontal, Down, Up, FromCenterVertical; spacing in px. Trackers in a group keep their own size but not their position (`LayoutElement.IsManaged`, hidden from unlock mode and the Layout list). Order inside a group = order of the tracker list. The order of the top level (groups and loose trackers mixed in any order) = `Configuration.RootOrder` (entries "group:<id>" / "tracker:<id>"; trackers inside a group are not listed there). Both are changed by drag and drop in the explorer. Only visible trackers take a slot, so a centred group stays centred. In unlock mode every enabled member is drawn as a ghost so the group can be placed.
- Cooldown trackers (`TrackerDefinition.Trigger = Cooldown`): `ActionId` = the ability to track; optional `StatusId`/`Unit`/`OnlyMine` = the ability's EFFECT (a buff): while it is on, the icon is in phase Active and shows the effect's remaining time. Phases: Ready (usable), Cooling (no charge available, shows time to next charge and a dark sweep), Active (effect on). Options: CooldownShow (Always / OnlyOnCooldown / OnlyWhenReady), CooldownLook while Cooling (Normal / Dimmed / Red), ShowCooldownSweep, GlowWhenReady, GlowWhenActive (gold pulsing glow), IgnoreGlobalCooldown (cooldowns up to 3.5 s count as ready), ShowStacks = show charges (only when the ability has more than 1).
- Cooldown data flow: the engine calls `plugin.GameData.SetWatchedActions(ids)` every frame; `GameDataProvider.BuildCooldowns()` (unsafe, FFXIVClientStructs `ActionManager`: GetAdjustedActionId, GetMaxCharges (static), GetCurrentCharges, GetRecastTime, GetRecastTimeElapsed, IsRecastTimerActive) fills `GameSnapshot.Cooldowns[actionId]` = `CooldownSnapshot(ActionId, Charges, MaxCharges, Remaining, Total)` one frame later. For multi-charge abilities the game keeps ONE timer for all charges (Total = per charge = recast / maxCharges). Not done yet: usability by resources/range (GetActionStatus), role actions in the ability list (they are found by search).
- Ability picker: `GameNames.AbilitiesOfJob(job, level)` lists the job's abilities (Action sheet: IsPlayerAction, not PvP, ClassJob = job or its parent class, Recast100ms > 35) and `GameNames.SearchActions` searches by name.
- Explorer (Trackers page, `TrackerEditor`): one list like a file explorer. Left: groups and loose trackers mixed freely, groups open/close (`TrackerGroupDefinition.Expanded`, saved), rows show the game's own icon (action icon for cooldown trackers, status icon for buff/debuff trackers, cropped with `TrackerEngine.IconUv`) and a tag (Cooldown / Buff / Debuff / Off). Right: the editor of the selection (two columns when the page is at least 720 px wide, otherwise stacked).
- Drag and drop is hand made (no ImGui drag-drop API): drag by the six-dot grip, the drop target is computed from the row rectangles of the frame (`ComputeDrop`). Drop zones: loose row top/bottom half = before/after at the top level; group row top 30 % = before the group, middle = into the group (at its end), bottom 30 % = first place inside (if open) or after (if closed); member row top/bottom half = before/after that member; far-left edge of a member row = take it out, right after its group. A dragged group can only go to the top level. Moves go through `TrackerEngine.MoveTrackerToRoot / MoveTrackerIntoGroup / MoveGroup`.
- A group is a TEMPLATE: `TrackerGroupDefinition.IconWidth/IconHeight` plus `TrackerGroupDefinition.Template` (an `TrackerDefinition` of which only the display settings and the load conditions are used). The group editor shows the same Display and Load cards as a tracker, bound to the template; any change calls `TrackerEngine.ApplyGroupStyle(group)`, which copies the template (`TrackerDefinition.CopyStyleFrom`) and the size to EVERY tracker of the group. A tracker that joins a group takes the template and the size too. After that each tracker can still be edited on its own. Groups made earlier get their template from their first tracker at startup (not applied to the others).
- Text placement (per tracker, copied by groups): `TimerAnchor/TimerOffsetX/Y` and `StacksAnchor/StacksOffsetX/Y` (stacks or charges). The anchor is one of 9 points of the icon and the SAME point of the text is attached to it (so a right anchor right-justifies, a centre anchor centres); the offset (px, +X right, +Y down) moves it. Defaults: timer centre, stacks bottom right with offset (-2,-1).
- Cooldown reading: `GameDataProvider.BuildCooldowns` reads the recast group's gauge (`GetRecastGroup`, `GetRecastGroupDetail`, `GetRecastTimeForGroup`) and does NOT rely on `IsRecastTimerActive` (it did not report an ability with one charge spent and one left). The Debug page lists the raw gauge values of every watched ability.
- Cooldown trackers: the "effect" status is optional. Its unit (`Unit`: Player / Target / Focus / Target of target) and `OnlyMine` are edited in the effect card (not in General) because they only matter when an effect is chosen. The unit says where to look for the buff the ability leaves after being used; individual party members are not tracked yet.
- "New tracker" goes into the selected group (or the group of the selected tracker), otherwise to the end of the top level.
- Progress bars (`Modules/ProgressBars`): `ProgressBarModule` (Plugin.ProgressBarSystem) draws the player's and the target's progress bar from `GameSnapshot.Player/Target.Cast` (`CastSnapshot` has ActionType: 1 action, 2 item, 5 general action, 13 mount; `GameNames.CastName/CastIconId`). Rectangular bar filling left to right, square spell icon INSIDE the bar at its left (optional), spell name at the left and time at the right (`ProgressBarSettings`: ShowIcon/ShowName/ShowTime, TimeMode Remaining or ElapsedOverTotal, TextSize + Font from OverlayFonts, colours, uninterruptible colour for the target). Size and position are layout elements `progressbar.player` / `progressbar.target`. A sample cast is drawn while the Progress bars page is open or in unlock mode (`PreviewRequested`).
- Slidecast facts: the window is a fixed ~0.5 s at the end of every cast (the same for every spell; casts shorter than that have no marker), and latency adds a little extra slack that the game does not report. Neither Dalamud nor ClientStructs exposes the player's ping, so the window is a plain setting. Per-spell or per-ping automatic windows are not possible.
- Slidecast marker (player bar only): `ProgressBarSettings.ShowSlideCast`, `SlideCastMs` (default 500) and `SlideCastColor`. A tinted zone covers the last `SlideCastMs` of the bar and a line marks where it starts. A cast shorter than the window shows nothing.
- Job gauges (`Modules/JobGauges`, started with White Mage): `JobGaugeModule` (Plugin.JobGaugeSystem) draws 3 rounded lily pips (the next one fills over 20 s, with the seconds left written on it) and 3 blood lily pips from `GameSnapshot.WhiteMage` (`WhiteMageGaugeSnapshot(Lily, BloodLily, LilyTimerMs)`, only non-null while the player's job id is 24). Data comes from Dalamud `IJobGauges.Get<WHMGauge>()` (`Plugin.JobGauges` service), read ONLY in `GameDataProvider.BuildWhiteMageGauge`. Two layout elements: `gauge.whm.lily` and `gauge.whm.bloodlily` (size in the layout, defaults 156x14 and 156x8 at center +190/+208). Settings in `JobGaugesSettings.WhiteMage` (Enabled, HideNative, ShowBloodLilies, ShowTimer, GlowWhenFull = frame when 3 blood lilies, gap, rounding, text size/font, colours). The game's own gauge `JobHudWHM0` is hidden with the same PreDraw + `PreventOriginal()` method as the progress bars. A sample is drawn while the "Job gauges" page is open or in unlock mode. VERIFIED by the owner in game: `LilyTimer` counts UP from 0 to 20000 ms towards the next lily (the Debug page card "White Mage gauge (raw)" shows the raw values). Pending: load conditions (only in combat, etc.), other jobs.
- Warrior gauge (added, NOT yet verified in game): `GameSnapshot.Warrior` (`WarriorGaugeSnapshot(BeastGauge)`, only while the job id is 21), read from `IJobGauges.Get<WARGauge>().BeastGauge` in `GameDataProvider.BuildWarriorGauge`. `JobGaugeModule.DrawWarrior` draws ONE rounded bar 0-100 (layout element `gauge.war.beast`, default 156x14 at center +230), optional mark at 50, number on the bar, colour change from 50 up (a spender costs 50). Settings: `JobGaugesSettings.Warrior` (`WarriorGaugeSettings`). Native gauge name `JobHudWAR0` is a GUESS from the WHM pattern: check it in Debug > Game windows as a Warrior. The Debug page has a card 'Warrior gauge (raw)'. `Widgets.Card` chains the mouse wheel to the page when the card is at its scroll end. Inner Release / Surging Tempest are statuses and belong to trackers, not to this gauge.
- Job filter in unlock mode / Layout page: `LayoutElement.IsHidden` (not saved; `LayoutManager.Positionable` skips hidden elements). `TrackerEngine` sets it every frame: an ungrouped tracker is hidden when its `Load.JobIds` does not contain the player's job; a group is hidden when it has members and none applies; ghosts in unlock mode only for members that apply. `JobGaugeModule` hides the WHM/WAR elements of the other job and draws the unlock-mode sample only for the current job (the Job gauges page still shows both samples). While the job is unknown (not logged in) nothing is hidden. Job gauge cards follow the game's job order: tanks PLD WAR DRK GNB, healers WHM SCH AST SGE, melee MNK DRG NIN SAM RPR VPR BST, physical ranged BRD MCH DNC, magical ranged BLM SMN RDM PCT BLU. BST (Beastmaster) is a new limited job (patch 7.5x, level cap 50, like Blue Mage), melee DPS, gauges: TP gauge, Instinct gauge, Instinct stacks (per consolegameswiki; Dalamud support for its gauge NOT checked). Next planned: cards that size to their content (one scroll per page), then profiles and import/export (shareable strings via Discord).
- MINIMIZE: the main window has NO native collapse arrow (`NoCollapse`); a title bar button (FontAwesome WindowMinimize) calls `Plugin.MinimizeMainWindow()`, which closes it and opens `MiniBarWindow` (`Windows/MiniBarWindow.cs`: 190x34 px bar, no title bar, NahjaUI mark + name at the left (also the drag handle), an open button and a close button drawn with plain lines) with its top right corner at the window's top right corner. `Plugin.ExpandMainWindow()` reverses it, putting the window's top right corner where the bar is; `/nui` and the plugin list buttons expand it when the bar is shown (`Plugin.ToggleMainUi`); unlock mode hides both and restores whichever was open. The window titles start with three spaces so the text stays clear of the rounded corner. The mark is `Core/Ui/NahjaMark.cs` (the final icon).
- ICON (done): the interface is named after the owner's character Nahja Stark (Miqo'te Paladin). The owner CHOSE concept C (shield made of four rounded tiles with two cat ears; 2x2 grid, bottom tiles curved into the shield point, top right and bottom left tiles at 55% opacity). Character reference (owner's images): Miqo'te with long straight black hair and bangs, black cat ears with pink inside, violet-blue eyes (the source of the accent colour), tan skin, two diagonal red cheek marks, black baseball cap with a white winged emblem and white piping, white halter top, black fingerless gloves, starry blue galaxy background. Four variants of C were shown (C plain, C1 whisker slashes, C2 small star, C3 cat eye) and the owner picked plain C, so the icon is FINAL: `Core/Ui/NahjaMark.cs` draws it with ImDrawList shapes on a 128 grid scaled to any size, following the accent colour (inner ears = accent mixed 60% with white); the same drawing is `images/icon.svg` and `images/icon.png` (512 px, for the plugin list / repository manifest later). Possible later detail variants (whisker slashes, star, eye) are in the Icon concepts canvas. Single accent violet #8B7CFF on indigo, must read at 20 px. 
- Unit frames (`Modules/UnitFrames`; player, target, target of target and focus frames): `UnitFrameModule` (Plugin.UnitFrameSystem) draws a life bar (layout element `unitframe.player`, default 280x26 at center -330/+190), a shield on top of the life (`UnitSnapshot.ShieldPercent` = the game's `ShieldPercentage`, a % of max life), drawn as ONE block as tall as the bar with the bar's rounded corners (only the corners touching the bar's ends are rounded): right after the life if it fits, otherwise from the right end of the bar over the life, reaching further left the more shield there is; the shield is translucent (`UnitFrameSettings.ShieldOpacity`, default 0.65, multiplied with the colour's alpha; the owner asked for it and for NO divider line) so the life and the background show through it, an optional mana bar whose place is chosen PER JOB (`ManaPlacement`: Off, UnderLifeBar = as wide as the life bar, OwnBox = a separate layout element `unitframe.player.mana`, default 156x8 at center +224, meant to sit next to the job gauge; `UnitFrameSettings.ManaJobs` = under the life bar, default healers and casters, `ManaOwnBoxJobs` = own box, `GetManaPlacement/SetManaPlacement`; the own box shows a sample while moving things; all jobs of the own box share ONE position for now, per-job positions would need profiles). The life colour can be fixed or the class colour (`LifeColorMode`; `JobColors`: the FFLogs job colours (the game has no official per-job colour; it only colours by role: Lodestone tank blue #2d3a80, healer green #346624, DPS red #732828). Source: stocky37's public gist `fflogs-colours.yaml` (Jan 2022, 19 jobs) plus Viper #108210, Pictomancer #FF66CC and Blue Mage #00AEEF given by the owner from the current FFLogs (all 22 jobs now come from FFLogs). The earlier NahjaUI-made palette was replaced; editable per job in `JobColorOverrides`; base classes use the colour of their job; enemies always use `HostileColor`), and two free texts. Each text is a `FrameTextSettings` (Enabled, Anchor = 9-point on the bar with the text's own same point placed there, OffsetX/Y, TextSize, Font, Color) drawn last with a black outline, so it can stick out of the bar. Defaults reproduce the owner's reference image: name at TopRight offset (-4,-9) (half over the top edge), life text at BottomLeft offset (4,-2) reading `738K | 100%`. Options: `NameFormat` (Full, FirstName, LastName, FirstInitialLastName, FirstNameLastInitial, Initials; `NameFormatOnlyPlayers` leaves enemy/NPC names alone, using `UnitSnapshot.IsPlayer` = ObjectKind.Pc) and `HealthTextPreset` (CurrentAndPercent, CurrentFullAndPercent, Current, CurrentFull, Percent, CurrentAndMax, Deficit, Custom template with {cur} {curk} {max} {maxk} {def} {defk} {pct}), `Separator`, `PercentDecimals` (rounded DOWN so non-full life never reads 100%), `AbbreviationDecimals` (K; millions keep at least one decimal), `Thousands` separator (comma/dot/space/none; dot also switches the decimal mark to a comma). All text building is in `UnitTextFormatter`. The game's own player bar `_ParameterWidget` is hidden with PreDraw + `PreventOriginal()` (`HideNative`, default on). The four frames share one drawing code and one settings class (`UnitFramesSettings.Player/Target/TargetOfTarget/Focus`; defaults from `UnitFrameSettings.CreateTarget()` = the player's look mirrored with no mana, and `CreateSmall()` = thin 150x18 bars with the name inside at the left and the percentage at the right); layout elements `unitframe.target`, `unitframe.targetoftarget`, `unitframe.focus`. The class colour table `JobColorOverrides` is shared (it lives in `UnitFramesSettings`). Colour of the life: enemies = `UnitSnapshot.IsHostile` (the game's StatusFlags.Hostile) and not a player use `HostileColor`; players use the class colour or the fixed one; friendly NPCs use the fixed one. The game's own bars are hidden with PreDraw + `PreventOriginal()` for three frames (names VERIFIED in game with the Debug "Game windows" card): player = `_ParameterWidget`, target = `_TargetInfoMainTarget` (the target of target is part of it, so it has no bar of its own and hiding the target hides it too), focus = `_FocusTargetInfo`. Left alone on purpose: `_TargetInfoBuffDebuff` (target statuses, until frames show buffs) and `_TargetCursor`. Visible names seen with the target info in SEPARATE PARTS mode; the combined mode's `_TargetInfo` was not seen and is not handled (the editor says to use separate parts). `HideNative` defaults to true for all frames; a config saved earlier keeps false for target and focus until ticked. The Debug "Game windows" card (`Game/GameWindowList.cs`) stays for finding other windows. Buffs/debuffs on the player, target and focus frames come later (the party frames already have them, see below). A sample unit is drawn while the "Unit frames" page is open or in unlock mode.
- Unit frame clicks: each real (not sample) frame has a see-through ImGui window exactly over the bar (`UnitFrameModule.HandleClicks`, skipped in unlock mode) that takes the mouse, so the game does not get the click. `UnitFrameSettings.Clickable` (default true) turns it off. LEFT click = `Plugin.TargetManager.Target = unit` (found with `ObjectTable.SearchByEntityId`). RIGHT click = a small NahjaUI popup (Target, Set as focus target, Clear focus target, Copy name). The game's own character context menu is NOT available: ClientStructs has no function to open it for a given character (only `AgentContext.OpenContextMenu` and `OpenContextMenuForAddon`, which depend on internal state), so it was not guessed. Target of target when you target YOURSELF: Dalamud's `TargetObject` reads a field that is not valid for the local player (ClientStructs says the local player's target must be read from the target system), so `GameDataProvider.TargetOfTarget` returns the target itself in that case.
- Party frames (`Modules/PartyFrames`, `Plugin.PartyFrameSystem`, DONE phases 1 and 2; settings in `UnitFramesSettings.Party`, edited on the Unit frames page tab "Party"): one bar per member in `GameSnapshot.Party` (the local player is in it when grouped; empty when solo), one under the other, drawn from the layout element `unitframe.party` (the FIRST bar; default 216x34 at top left +60/+300; the others follow downwards with `RowSpacingPx`; in unlock mode the box is the whole stack via `DisplayRect`). The look of a bar is a normal `UnitFrameSettings` (`PartyFrameSettings.Look`, created by `CreateLook()`: class colours, rounding 4, name at the left and percentage at the right in Saira bold) so the same editor cards are reused. Party order and chocobo: `PartyFrameSettings.Order` (`GameOrder` default = the order of the game's own party list, read in `GameDataProvider.ReadGameListOrder` from `AgentHUD.Instance()->PartyMembers[i].Index` (HudPartyMember: EntityId + Index = real position in the UI; the span itself always has the local player first; `PartyMemberCount`), so it follows the game's sort option by itself; verified in the ClientStructs source on 2026-10-26, not yet compiled; `MeFirst` = you first, then the others in the game's order; if the agent cannot be read the order of `IPartyList` is used; the enum values were swapped on purpose so a saved 0 now means the game's order) and `ShowChocobo` (default on): `GameSnapshot.Chocobo` is found by `GameDataProvider.BuildChocobo` (scans the object table every ~30 frames for an `IBattleNpc` with `BattleNpcKind == BattleNpcSubKind.Buddy` and `OwnerId` = you; `BattleNpcSubKind.Buddy` = the chocobo companion, verified in the ClientStructs source; only while in a party), drawn as the last bar with a gold stripe, no mana (`UnitSnapshot.IsCompanion`). Smooth status timers: `GameDataProvider.SmoothRemaining` (all units, so trackers too): the game refreshes the remaining time of other players' statuses only every few seconds, so when the same value is read again the elapsed time is taken off locally; a new value from the game wins. In the party bars, the role stripe, the life fill and the mana fill are drawn with `FillRounded`: the full rounded box cut by a clip rectangle, so narrow fills follow the bar corners (a plain narrow rounded rect sticks out because ImGui clamps the rounding to the width). Party members always count as players (`UnitSnapshot.IsPlayer` is false for them because `ToUnit(IPartyMember)` does not set it), so class colours and name formats are forced with `isPlayer = true`. Extras: a role stripe at the left edge (tank blue, healer green, damage red), a mana strip inside the bottom of the bar chosen by `PartyManaMode` (Off, Healers, HealersAndRaisers = healers + SMN/RDM (default), Everyone), and a targeted-member marker for gamepad users (ring + arrow, `SelectionColor`, default accent #8B7CFF). Clicks use `UnitFrameModule.HandleClicks(id, pos, size, unit)` (left = target, right = NahjaUI menu). The game's own party list `_PartyList` (VERIFIED in game with the Debug page) is hidden with PreDraw + `PreventOriginal()` (`Look.HideNative`). With no party, 8 sample members are drawn while the Party tab is open or in unlock mode.
- Party buffs and debuffs (phase 2): two rows of icons per member, `PartyFrameSettings.Buffs` (default: attached to the Left of the bar by the icon's Right, offset -8, growing Left) and `Debuffs` (Right of the bar by the icon's Left, offset +8, growing Right), each a `StatusRowSettings`: Enabled, OnlyMine (`StatusSnapshot.SourceId` == local player), `Filter` (All = every buff / debuff by the Status sheet category 1 / 2, OnlyListed, AllExceptListed) with `StatusIds` list (edited by clicking the statuses the party has right now, or a name search), BarAnchor, IconAnchor, OffsetX/Y, IconWidth x IconHeight (DEFAULT 32x24: the owner chose it over 36x24; rectangular, wider than tall like the trackers), CropPercent (default 13, same crop as the trackers: `TrackerEngine.IconUv` with `StatusIconAspect`; icons are cropped, never stretched), SpacingPx, MaxIcons, Growth (Left, Right, Up, Down), timer and stack count (the timer text reuses `TrackerEngine.DrawOverlayText` and `FormatTime`, made internal). Every icon has a 1 px black border and rounded corners (`CornerRadiusPx`, default 3, drawn with `AddImageRounded`; trackers have the same setting, `TrackerDefinition.CornerRadiusPx`, default 3, so the whole interface looks consistent; the cooldown sweep wedge follows the rounded corners: a fan of triangles to the edge of the rounded box, found with `EdgeDirection`). Statuses come from the party list data (`IPartyMember.Statuses`), which can lag a little behind the real ones. The timer and the stack count each have their own anchor on the icon (9 points), offset X/Y and size (stack size 0 = automatic, three quarters of the time size), plus one font per row; defaults like the trackers (time centered, stacks at BottomRight offset -2/-1). Preview icons use real status ids (160, 1191, 50, 638, 43, 144) and ignore the filters.
- DECISIONS for party frames (owner): layout A (buffs left of the bar, debuffs right); only removable debuffs get a violet frame (#B49CFF, phase 3, NOT done), no red frames, no frame on shields or HoTs; mana bar visible for healers and optionally the jobs that can raise; NO hotbar / keybind / mouseover automation (dropped for good: UI only). Phase 3 needs the "removable" flag of the Status sheet (probably a `CanDispel` column, NOT verified: check it or fall back to an editable list). Later: an ElvUI-style filters section, and choosing party statuses through the tracker system (list) instead of the per-row list.
- FONT DECISION (owner chose Saira over Barlow after comparing two mockups): NahjaUI's own interface font is Saira, embedded in the plugin (`Core/Ui/EmbeddedFonts.cs`, generated, base64 of three subset TTFs: Saira Condensed SemiBold = `OverlayFont.NahjaDisplay` for names and progress bar texts, Saira Condensed Bold = `NahjaDisplayBold` for timers, stacks and numbers, Saira Regular = `NahjaText` for menus, chat and labels). Licence SIL OFL 1.1 (no reserved font name); `docs/FONT_LICENSE.txt` must stay in the repo and ship with the plugin. Subset: Basic Latin, Latin-1, Latin Extended-A, general punctuation U+2010-2027, U+2039-203A, euro; no CJK. `OverlayFonts` loads them with `NewDelegateFontHandle` + `OnPreBuild` + `AddFontFromMemory` (SizePx is logical, Dalamud multiplies by the scale) at 15 fixed sizes (overlay sizes plus menu sizes 12-20). `OverlayFonts.Current` lets `ThemeScope` push the `NahjaText` font at `Configuration.MenuTextSize` (default 15) before every NahjaUI window starts. New trackers default to `NahjaDisplayBold`, new progress bars to `NahjaDisplay`; existing ones keep their saved font until the owner presses Appearance > "Use the NahjaUI font everywhere" (`Plugin.UseNahjaFontEverywhere`). Enum values were only appended, so saved settings stay valid. Possible later step: custom-drawn number glyphs for timers (owner has not asked yet).
- FONT SIZES: `OverlayFonts` fonts are built by Dalamud already multiplied by `ImGuiHelpers.GlobalScale` (the plugin's atlas is globally scaled). Always pass the LOGICAL pixel size the user chose to `PushClosest` and never multiply it by the scale again (that bug made text bigger than selected). Positions, offsets and borders of the overlays are in physical pixels and DO use the scale.
- Hiding native UI: `IAddonLifecycle` `AddonEvent.PreDraw` listener on `_CastBar` and `_TargetInfoProgressBar`; it calls `args.PreventOriginal()` (the game's own Draw is skipped, which also covers the closing fade-out animation: clearing only the visible flag let the native bar reappear for the last ~0.4 s of a cast) and also sets `AtkUnitBase.IsVisible = false`, right before the game draws them (only when that bar is enabled and "Hide the game's progress bar" is on). The target's native bar is only separate when the game's Target Info is split into parts in the HUD layout. Same technique will hide other native elements for the unit frames.
- Unlock mode and the main window: `Plugin.FollowUnlockMode()` (called every frame in `Plugin.Draw`) closes the main window when unlock mode starts, if it was open, and reopens it when unlock mode ends, whatever way it was started or ended (button, Done, `/nui unlock`).
- Planned next (H5 rest): cooldown and resource triggers, bars, several triggers per tracker.

## UI components and unlock mode (current behaviour)

- `Widgets.Card` / `Callout` size themselves to their content: the height is measured while drawing (cursor Y minus item spacing plus padding, cached per id in `MeasuredHeights`) and used from the next frame; the first frame uses the `lines` guess. They have no scrollbar and never take the mouse wheel, so each page has ONE scroll (the page). Still with their own scroll on purpose: the Trackers page explorer / editor columns and the ability list (a two-panel design; ask the owner before changing).
- Unlock mode: `LayoutElement.DisplayRect` lets a module give the box to show (tracker groups show the area their icons cover). Clicking a box selects it and its panel stays with it even when the mouse passes over other boxes; right click on the selected box lets go. The top bar is centered when unlock mode opens.
- Unit frames page: picker buttons Player / Target / Target of target / Focus / Party / Class colors (the class colors are shared and have their own page).

## Profiles: export / import (Core/Profiles)

- `ProfileManager`: a code is `NUI1:` + base64(gzip(JSON)) built with Newtonsoft.Json. Blocks (`ProfileBlock`): Trackers (trackers + groups + rootOrder), Placement (all `Layouts`), UnitFrames, ProgressBars, JobGauges, Appearance (accent, menu text size). Export takes any subset; all blocks = full backup (the owner wants everything to be restorable after a reinstall). `Read` never throws and changes nothing (size cap 16 MB unpacked, format/version check); `Apply` reads all chosen blocks first, writes a backup file (`<plugin config dir>/backups/backup-*.txt`, last 20 kept) and then changes the configuration.
- Trackers import modes: Add (new ids so nothing clashes, NO positions: the owner wants shared trackers to be placed by each user; placement keys of trackers are skipped) or Replace (keeps ids, full restore with positions; default when the code contains Placement). Positions of elements that already exist are copied INTO the existing `ElementLayout` objects (modules keep references); settings blocks are replaced on the configuration (modules read them every frame). After importing trackers `TrackerEngine.ReloadFromConfiguration()` rebuilds the runtimes (`LayoutManager.Detach` keeps saved positions).
- UI: `ProfilesPage` (sidebar page "Profiles"): export checkboxes + copy/save to file, import box + Check code + per-block checkboxes + Import, list of automatic backups to load back. VERIFIED by the owner in game (export, import).
- Not done: named profiles with quick switching, automatic profile per job (the owner said: not now), sanity limits on imported values (icon sizes, etc.), a library of example profiles.

## Nameplates: design and research (2026-10-26, not coded yet)

Design (mockup approved by the owner, canvas "Nameplates NahjaUI"):
- Same Saira fonts. Name at the top-left, touching the top edge of the life bar like the unit frames (offset about 4,-9). Level written right after the name on the same line, with a "show level" option (and optionally hide when equal to mine). Every text has anchor, offset, size, font.
- Progress bar under the life bar, SAME width, spell icon INSIDE the frame at the left (like `ProgressBarModule`). Amber glow = interruptible, steel gray + lock = not interruptible.
- Rounded corners option on bars and icons (intrinsic to the whole interface).
- Three icon rows, each with its own anchor/offset/size/crop/rounding/timer/stacks settings:
  1. Debuffs: of the ones I applied, a filter list (only these / all except these) + an "extra from anyone" list. Stuns never appear here.
  2. Buffs: same two lists (mine filtered + extra from anyone, e.g. raid buffs).
  3. Stuns: EVERY stun / crowd control on the mob, from anyone (mine included), only in this row, amber frame. Goal: never overwrite a stun.
- Friendly players/party: always show the debuffs I can remove with Esuna, violet frame.
- Tabs of settings: Enemies, Friendly players, Party members, NPCs, Pets, Class colors.

Research (verified in source, shell `git clone` of ClientStructs, Dalamud; datamining CSV):
- Status sheet (xivapi/ffxiv-datamining `csv/en/Status.csv`, raw.githubusercontent works from the shell) has columns `CanDispel`, `CanStatusOff`, `LockMovement`, `LockActions`, `LockControl`, `StatusCategory`. `CanDispel` = removable by Esuna: 263 of 2165 debuffs (Dia, Bio, Vulnerability Up, Addle, Stun, Sleep, Bind, Heavy, Silence, Paralysis, Poison, Bleeding... yes; Petrification, Doom, Leaden, Terror... no). Only 1 buff has it, so there is NO game flag for "removable/stealable enemy buff": that frame must come from an editable list. Lumina column name in C# to be checked when coding (probably `CanDispel`).
- Stuns: no single flag. Plan = editable default list of status ids (Stun 2, Sleep 3, Bind 13, Heavy 14, Silence 7, Pacification 6, Paralysis 17, plus others) and optionally an automatic rule with `LockActions`. To refine in game.
- Native nameplates: addon `NamePlate` (`AddonNamePlate`, 50 `NamePlateObject`), enemy progress bars: addon `CastBarEnemy` (`AddonCastBarEnemy`, 10 slots with EntityId, Progress, CastName, Interruptible, screen position). Per-plate data from `NamePlateNumberArray.Instance()` (ClientStructs): `ActiveNamePlateCount`, and per plate `NamePlateKind`, `X`, `Y` (screen position computed by the game), `Depth`, `Scale`, `GaugeFillPercentage`, `EntityId`, `DrawFlags`, `VisibilityFlags` (0x1 draw name, 0x2 draw gauge). This list already follows the player's game options for which plates show.
- Two ways to hide the game's plates: (a) Dalamud `INamePlateGui` (official service, `OnNamePlateUpdate` handlers with `RemoveName()`, `VisibilityFlags`, `GameObject`) or (b) same trick as other native windows (PreDraw + PreventOriginal + hide `NamePlate` and `CastBarEnemy`). To test in game which one is safer; (b) must be checked to not stop the number array updates.
- Plan: read positions from `NamePlateNumberArray` (or WorldToScreen on the object), draw our plate with ImGui, keep click-through and targeting in mind (clicks on a plate should target the unit, as the unit frames do).

Nameplates step 1 (DONE and tested in game 2026-10-26): the Nameplates page (Enabled) turns on our plates for ENEMIES only (no commands for settings that are in the menu: owner's rule) (life bar + name with level, target ring, rounded, Saira). Files: `Modules/NamePlates/NamePlateModule.cs` and `NamePlateSettings.cs`; `GameDataProvider.BuildNamePlates()` reads the plates; `NamePlateSnapshot`. Verified facts:
- The game's own enemy plate is hidden with Dalamud `INamePlateGui.OnDataUpdate` and `handler.VisibilityFlags = 0` for kind 3 (BattleNpcEnemy); turning it off with `RequestRedraw()` brings the native plates back. No duplicated plates.
- Plates are read from `NamePlateNumberArray.Instance()` (per plate: `NamePlateKind`, `EntityId`, `Scale` 0-100, in `ObjectData`, which is a Span in the user's ClientStructs: read by index, NOT `GetPointer`) and `UI3DModule.NamePlateObjectInfoPointers[i]` (same index). `info->NamePlatePos` is NOT a world position: x,y are screen coordinates from -1 to 1 (y UP), z is depth (<=0 = behind the camera). Pixel = ((x+1)/2*W, (1-y)/2*H), same as the game's `ObjectData.X/Y`. It already includes the height of the enemy (flying mobs are right). `WorldToScreen` on the object position is wrong for tall or flying enemies.
- Entity id 0 or 0xE0000000 = empty plate. `Scale` makes far plates smaller (we use 60% to 100%).
- Step 2 DONE and tested: enemy progress bar under the life bar (same width, no spell icon: enemy action icons mean nothing; color of the fill = interruptible or not, same colors as the target progress bar (Progress bars page), no icon and no lock, name and time inside). Native `CastBarEnemy` hidden with PreDraw + PreventOriginal + `IsVisible=false` while our plates are on.
- Step 3 DONE and tested: click on a plate (name + bar area) targets the enemy, right click opens the NahjaUI unit menu; reuses `UnitFrameModule.HandleClicks`. Setting `Clickable`.
- Still missing: settings page and unlock element, friendly players/party/NPC/pets plates, buff/debuff/stun rows, class colors, distance limit and "only in combat" option.

## Code rules

- Integers for pixel values. Scale only when drawing.
- Save configuration debounced (never every frame).
- Wrap each module and tracker update in try/catch; log through `Plugin.Log`.
- Prefer small classes with one job. Comment the "why", not the "what".
- No new libraries without asking.

## Milestones and status

- H0 Environment: DONE (template compiled and loaded in game).
- H1 Core foundation: DONE (config, theme, main window with sidebar, /nui).
- H2 Positioning and unlock mode: DONE for now (can be refined later). H2a = elements with anchors and integer offsets, unlock overlay (drag, inspector panel on hover/selection with X/Y fields, Locked and Reset position), Layout page, /nui unlock. H2b (next) = anchoring to other elements, alignment guides to other elements, match size and align, undo/redo.
- H3 Game data snapshot: DONE. Provider builds a snapshot per frame (player, target, focus, target of target, party, statuses, casts, context). Debug page shows it live. Cooldowns and job gauge are NOT in H3 (H5).
- H4 First trackers: IN PROGRESS. Status trigger on player/target/focus/target of target, icon with timer and stacks, load conditions (jobs, combat), editor page with status picker (current statuses + name search). LayoutDemo test boxes removed.
- H5 Cooldown and resource triggers, bars.
- H6 Full tracker editor and creation wizard.
- H7 Groups, profiles, import/export: groups DONE, import/export DONE (see Profiles). Named profiles with switching: not started.
- H8 Polish, performance, templates per job.

Next steps agreed with the owner (in this order of interest): named profiles with manual switching; buffs/debuffs on the player, target and focus frames (then hide `_TargetInfoBuffDebuff`); party frames phase 3 (violet frame on removable debuffs) and filters; nameplates (riskiest, depends on game updates; see "Nameplates: design and research", to be coded at the owner's home PC where he can test); more job gauges in the game's job order (tanks PLD WAR DRK GNB, healers WHM SCH AST SGE, melee MNK DRG NIN SAM RPR VPR BST, physical ranged BRD MCH DNC, magical ranged BLM SMN RDM PCT BLU; BST = Beastmaster, new limited melee job, gauge support in Dalamud not checked); load conditions for gauges; verify the Warrior gauge native window name `JobHudWAR0` in game if not done.

Move to the next milestone only when the previous one is stable and committed in Git.

## Session start checklist

1. Which milestone are we on? (see above)
2. Paste any file you want changed, plus the exact compiler error if there is one.
3. Ask for one small step.

## Buffs and debuffs on the unit frames (step 1 done, needs testing)
- `Modules/UnitFrames/StatusRowRenderer.cs` (`plugin.StatusRows`) draws a row of status icons; party frames and the Player/Target/Focus frames share it.
- `UnitFrameSettings.Buffs` / `.Debuffs` are `StatusRowSettings` (off by default; buffs above the bar, debuffs under it, debuffs "only mine").
- `StatusRowSettings.ExtraStatusIds`: always shown, whoever applied them (overrides "only mine").
- Target frame: `HideNativeStatuses` hides `_TargetInfoBuffDebuff` while one of our rows is on.
- Not on the target of target. Editor: cards Buffs/Debuffs per frame (same widgets as the party page).
- To do: ordering/cap for player buffs, Esuna-removable highlight, stun row.

## Paladin gauge (done, needs testing)
- Oath Gauge (0-100) via `PLDGauge.OathGauge`, native addon `JobHudPLD0`, job id 19. Mark/ready colour at 50 (Holy Sheltron / Intervention).
- Warrior and Paladin share `BarGaugeSettings`, `JobGaugeModule.DrawBar` and `JobGaugeEditor.DrawBarGauge`. New bar-type jobs (DRK Blood? GNB cartridges are pips, not bars) should reuse them when they fit.
- Fix: the mana bar sample in its own box is only drawn for jobs set to "own box".

## Red Mage gauge (done, needs testing)
- `RDMGauge` (WhiteMana, BlackMana 0-100, ManaStacks 0-3), class job 35, native addon `JobHudRDM0` (verify the name in Debug > Game windows). Snapshot `RedMageGaugeSnapshot`, `GameSnapshot.RedMage`.
- Three layout elements: White Mana bar, Black Mana bar, Mana Stacks (3 pips). Mark at 50 on each bar (melee combo needs 50 of each); optional frame when both >= 50. Settings `RedMageGaugeSettings`, editor entry "Red Mage" (job 35).

## Target kinds (done, needs testing)
- `UnitSnapshot.Kind` (`UnitKind`: Character, Pet, Npc, Object). `ToUnit(IGameObject)` now also returns non-battle objects (no life, no statuses) so the target frame is no longer empty for furniture/chests/aetherytes.
- Object kinds by number (ObjectKind): 3 EventNpc and 10 Retainer = Npc; 9 Companion = Pet; the rest = Object. Battle NPCs with SubKind 2 (Pet) or 3 (Buddy) = Pet.
- Colours per frame: `PetColor`, `ObjectColor` (grey). Objects: full bar in grey, name only, no life text, no buff rows.

## Combo trackers (done, needs testing)
- `ComboSnapshot(Action, Timer)` from `ActionManager.Instance()->Combo` (last combo action used + seconds left).
- Cooldown trackers have `OnlyAsComboStep`: shown only while `Combo.Action == Action sheet ActionCombo of the tracker's ActionId` and the timer runs; the timer shows the combo time. The "comes after" action is read from the game data (no picker needed).
- Procs that are statuses (Primal Rend Ready...) use normal status trackers. Idea for later: `IsActionHighlighted` for procs that are not statuses.

## Font lab (done)
- Debug page > "Font lab": every font x several outline shapes (`OutlinedText.Style`) at the real UI scale, size slider.
- Eight extra fonts were tried (Barlow, Fira Sans Condensed, PT Sans Narrow, Rajdhani, Titillium Web, Saira Semi Condensed) and the user did not like any: removed. We keep Saira Condensed (SemiBold / Bold) and Saira Regular.
- The outline stays 1 physical px, 8 directions (`OutlinedText.Draw`). If it is revisited: the lab has 4 dir, 1.5 px x12 and 2 px x16 variants.

## Tracker icon follows the game's action swap (needs testing)
- `CooldownSnapshot.CurrentActionId` (= `GetAdjustedActionId`) -> `TrackerState.IconActionId`: a cooldown tracker draws the icon of the form the game shows now (Inner Release -> Primal Wrath). The cooldown data already used that form.
- Not verified in game which buttons swap and when; name on the tracker stays the chosen one.

## Game window cover, tooltips, mouse (needs testing)
- `Game/GameWindowCover.cs` (`plugin.GameCover`, updated at the start of `Plugin.Draw`): rects of game windows that cover the screen (for now `ContextMenu`; add names in `CoverNames`; rect = addon X/Y + RootNode size * Scale, unverified). `Draw(pos,size,margin,action)` runs the drawing once per uncovered part of the screen with a clip rect, so frames are not drawn over the game's menu. Used by unit frames and party rows.
- Right click on a frame opens the game's own menu via `AgentHUD.OpenContextMenuFromTarget`. Click areas step aside while a cover is on screen.
- Buff/debuff tooltip (`StatusRowRenderer.ShowTooltip`): hover test with `IsMouseHoveringRect` (no window), `ImGui.BeginTooltip`; description from the Status sheet evaluated with `ISeStringEvaluator`. Option `UnitFrames.ShowStatusTooltips`.
- `UnitFrames.AlternateClicks` (experimental, off): no click window, `SetNextFrameWantCaptureMouse(true)` while hovering + `IsMouseClicked` (DelvUI's technique; DelvUI is AGPL: only used as a reference, no code copied). To see if the game's cursor stays.
- Idea: add more cover windows (all windows with a WindowNode, like DelvUI's ClipRectsHelper) and use `GameCover.Draw` in trackers, gauges, progress bars, nameplates.

## Nameplate level color
- `NamePlateSettings.LevelColor` (default yellow 1, 0.82, 0.25), editor "Level color" in the Name card. The name and the level are drawn as two texts (level after the name + two spaces), so NPC names with numbers are not confused with the level.

## Glow when the game highlights the ability
- Cooldown trackers: `GlowWhenHighlighted`; `CooldownSnapshot.Highlighted` = `ActionManager.IsActionHighlighted(Action, adjustedId)` (what the hotbar uses for its glow; DelvUI uses it for Viper). Works with the icon swap (Fight or Flight -> Goring Blade), so the tracker tracks Fight or Flight, changes its icon and glows when the game lights the button. Untested in game.

## Progress bar rounding
- `ProgressBarSettings.RoundingPx` (default 4, slider 0-12 in the Colors card of each progress bar). Background, border and icon (left corners) are rounded; fill and slidecast tint are the rounded shape cut by a clip rect (right corners only when the icon is shown). Same rule as unit frames / nameplates.

## Several effects on a cooldown tracker
- `TrackerDefinition.ExtraEffectIds`: extra statuses that count as "the effect is active" (Fight or Flight + Goring Blade Ready). `TryFindStatus` checks StatusId first, then the extras in order; the first present one gives the timer. Editor: tick "The next one I click is an extra effect", then click a status (clicking one already in the list removes it). Clear on the main effect also clears the extras.

## Job icons on party frames
- Option `PartyFrameSettings.ShowJobIcon` (+ `JobIconStyle` Plain/Light, `JobIconSizePx`): the game's own job icon (62000 + jobId, or 62100 + jobId) drawn inside the bar at its left, vertically centered; the name text shifts right. Replaces the role stripe. Hidden for chocobos. Covers all jobs incl. crafters/gatherers (ids 8-18), not verified in game.
- No icon files are bundled or downloaded: they are Square Enix artwork, so we read them from the game at runtime with `TextureProvider.GetFromGameIcon` (same as action/status icons).

## Enmity list (phase 1, needs testing)
- New module `Modules/EnmityList` (settings, module, editor page "Enmity list"). Source: the game's own list `EnemyListNumberArray` (up to 8 entity ids, same enemies and order as the native `_EnemyList`), resolved through the ObjectTable into `EnemyEntrySnapshot(Unit, TargetsMe)` (`GameSnapshot.EnmityList`, read only while `GameData.WantEnmityList`). `TargetsMe` = enemy `TargetObjectId == player GameObjectId`.
- One bar per enemy: name, life %, enmity dot (red = attacking me, green = attacking someone else), target ring, progress bar under the bar using the Progress bars page target colors (different fill = cannot be interrupted). Native `_EnemyList` hidden by PreDraw (option). Clickable like the nameplates. Wrapped in `GameCover.Draw`. Element "enmitylist" in Layout (box = first bar).
- Phase 2 planned: debuff filter (count how many enemies of the list have my debuffs, e.g. Bard DoTs) and stun tracking (which enemies are stunned / can be stunned).

## Party list alone (follows the game's option)
- Reads `Plugin.GameConfig.UiControl.TryGet("PartyListSoloOff", out uint)` every frame (`ContextSnapshot.PartyListWhenSolo`, `SoloOptionRaw`). Assumption: 0 = list shown alone (the name says "off"); the Party options card shows the raw value so it can be checked in game and flipped if backwards.
- No checkbox of our own: the game decides. With no party and the option on, the party frames show you (+ chocobo, `BuildChocobo` also allowed alone). A read-out line in Party options shows what the plugin reads (remove it once the 0 = shown assumption is confirmed).

## Game cursor in alternate click mode
- Dalamud hooks `SetCursor`: while ImGui captures the mouse and `UiBuilder.OverrideGameCursor` is true, the game's cursor is replaced by the Windows one. In alternate click mode `UnitFrameModule.HandleClicks` sets `cursorWanted` while hovering a frame and `UpdateCursor()` (end of `Plugin.Draw`) sets `OverrideGameCursor = false` only then, back to true when the mouse leaves (it is a global setting). DelvUI sets it false too. Needs testing.

## Tooltip page
- New left menu page "Tooltip" (`Modules/Tooltip`: `TooltipSettings`, `TooltipEditor`, `Configuration.Tooltip`). Replaces `UnitFrames.ShowStatusTooltips` (removed). Choose where it shows (buffs/debuffs of Player, Target, Focus, Party; `Allows(place, isBuff)`), what it says (name and description always; optional ID and who applied it) and the look (text size, max width, rounding, name accent/color, description/detail/background/border colors, Reset).
- `StatusRowRenderer.DrawRow(..., place)` gets "player"/"target"/"focus"/"party" from the frame that draws it; `ShowTooltip` reads `Configuration.Tooltip`.
- Idea: the same tooltip for the status trackers of the Trackers page and the nameplates (not hoverable yet).

## Job menu in the load conditions
- Trackers and groups: "Choose jobs..." button in the Load conditions card opens a popup (`DrawJobPicker`) with every job by role (tanks, healers, melee, ranged, casters, crafters, gatherers), checkboxes and All / None per role, plus Clear (= every job). It edits `Load.JobIds`; "Add my current job" stays.
- "Add my current job" removed (the menu is enough). Job order as in the game: Blue Mage (36) last of the casters (after Pictomancer) and Beastmaster in the melee row after Viper; its id is looked up in the game's ClassJob sheet by abbreviation "BST" (`GameNames.JobIdByAbbreviation`), so no id is hardcoded. Beastmaster was added in patch 7.56 (melee DPS, limited job).

## Clicks without a window are now the only method
- Removed `UnitFrames.AlternateClicks`, the "Mouse (all frames)" card, the see-through click windows and the small NahjaUI right-click menu (`DrawUnitMenu`). `UnitFrameModule.HandleClicks` always uses `IsMouseHoveringRect` + `SetNextFrameWantCaptureMouse` + `OverrideGameCursor = false` while hovering (cursor of the game kept), left click targets, right click opens the game's menu (`OpenGameMenu`). Used by unit frames, party, nameplates and enmity list.

## Player: HP bar and Mana bar are separate (needs testing)
- Unit frames picker: "HP bar" (the old Player page) and "Mana bar" are separate pages. Each is on/off per job (`HideLifeJobs`/`IsLifeOn`, `ManaOnJobs`/`IsManaOn`) with a button per job by role (tanks, healers, melee + Beastmaster after Viper, ranged, casters + Blue Mage last, crafters 8-15, gatherers 16-18). Life defaults on for every job (new jobs included); mana defaults on for healers and casters.
- Mana has no placement any more: always its own bar ("unitframe.player.mana" in the Layout), independent of the Player frame's Enabled. Own look: size (layout), `ManaRoundingPx`, `ManaColor`, `ManaBackgroundColor`, `ManaBorderColor`. The old lists `ManaJobs`/`ManaOwnBoxJobs` are migrated once into `ManaOnJobs` (`MigrateManaLists`, called in the Plugin constructor).
- Crafters show CP and gatherers GP in the mana bar (`GameDataProvider.ResourceNow/ResourceMax` fill `UnitSnapshot.Mp/MaxMp`).
- The game's player bar (life + mana together) is hidden when "Hide the game's life and mana bar" is on and the HP frame or the mana bar is enabled.
- Beastmaster id: looked up in the ClassJob sheet by "BST" (`GameNames.JobIdByAbbreviation`). TODO: the Trackers job menu has its own copy of the role list; unify.

## Sprint in the ability search
- `GameNames.UniversalActionIds` ({3 = Sprint}): general actions every job has but the game does not mark as player actions, so they were missing from the ability search and the job list. Add more ids there if other general actions with a cooldown are wanted.

## Job filter in the trackers explorer
- The explorer of the Trackers page has a "Show trackers of" combo: my job (default; all jobs when there is no player), all jobs, or one job of the list (by role, with Beastmaster/Blue Mage in their place). A tracker passes when its `Load.JobIds` is empty or contains the job. Groups with trackers but none for that job are left out (empty groups always show). A line says how many trackers are hidden. "New tracker" while filtering by a job adds that job to its load conditions. Trackers stay shared (no per-job profiles for now; decided: filter first, per-job profiles later only for layout positions / player bars if still wanted).

## New groups and loose trackers start in the centre of the screen
- Cause of the "General group never shows" report: new groups were created at `OffsetY = 200 + groups.Count * 60`, which ends up off the screen after a few groups (the job-limit theory was wrong; all jobs were ticked).
- Now every new group and every new loose tracker is created at the centre (offset 0, 0); trackers used to go in rows of 10 (`120 + row * 50`), which could also leave the screen. Existing groups keep their saved position; a group already off-screen can be reset in the Layout page.
- "New tracker" adds the filtered job only to loose trackers (not inside a group). There is deliberately no "every job" button in the load cards (only Sprint is for all jobs); use the role All/None buttons of the job picker.

## PILLAR: NahjaUI only redraws, the game's rules stay (owner's rule, 2026-10-28)
- Every option the game has for a piece of interface (nameplates: which plates show name / life bar / always, only with target, only when not full life, per category; party list and its options; etc.) must also work in the NahjaUI version of it. We replace how it is DRAWN, never the rules about WHAT shows.
- Reason: on patch days (or any time the plugin is not available) the player must not have to go back through the game's menus and re-enable plates, party list, names and so on. The game's own settings are never changed by the plugin; what we hide (flags / addons) comes back by itself when the plugin is off.
- Consequence for new work: read the game's decision (flags, config values) instead of inventing our own rule; add an own option only for things the game cannot do.

## Nameplates follow the game's options (step 1b, enemies)
- `INamePlateGui.OnDataUpdate` fires BEFORE the game's own update. Before we write `VisibilityFlags = 0` we keep the flags the game gave (bit 1 = name, bit 2 = life bar) per entity in `NamePlateModule.gameFlags`. A zero read later is not stored (could be our own write). Option `FollowGameOptions` (default on) draws the name / bar only if the game would have; `ShowGameFlags` writes the flag number next to the plate (test aid).
- TO VERIFY IN GAME: that the game rewrites the flags when its options change or the life changes (e.g. "only when not at full life"). If the number never changes, the flags are only written on full refreshes and we need another source (the game's config values) or restore the flags in `OnPostDataUpdate`.
- Plan: allied plates (players, party, pets, friendly NPCs) then text-only plates (minions, objects, NPCs), same rule.

## Nameplates step 2: players (phase 1 of allies)
- TESTED in game: the game's flags follow its options with no delay (7 = name+bar+4, 5 = name only; "When HP is below 100%" on engaged enemies goes 5 -> 7 on the first hit). So `FollowGameOptions` works.
- Kind 0 (`UIObjectKind.PlayerCharacter`) plates are now read too (`WantPlayerPlates`) and drawn with the same look. `NamePlateRelation`: Enemy (also hostile players), Self, Party (`Plugin.PartyList` entity ids), OtherPlayer. Own bar colors and on/off per relation (Players card). The level is only written on enemies.
- Alliance members are NOT told apart yet (they count as OtherPlayer): no verified API used.
- Not done yet: friendly battle NPCs/pets (kind 4), text-only plates (minions, objects, NPCs), title / FC tag / icons.

## Nameplates step 3: name display type and title come from the game
- No own options for when the name shows, the name format (full name / surname / abbreviated / forename / initials) or the title: the text is the one the game wrote in the plate (`handler.Name`, `handler.Title`, `handler.DisplayTitle`, `handler.IsPrefixTitle`), read in `OnDataUpdate` (before the game's own update) for player plates and stored per entity (`gameTexts`). Read again only when `handler.IsUpdating` (or first time) to save work.
- The title is drawn above the name (prefix) or under the bar, like the game; only its size and color are ours (`TitleSize`, `TitleColor`).
- TO VERIFY IN GAME: that changing the name display type or title option in Character Configuration updates our plates (if not, the text is only refreshed on `IsUpdating` and we should read it every frame). FC tag, status icons and target suffix are not drawn yet.

- Title fix: the game's title text came with its own quote symbols (private-use characters), shown as "?" by our font. `CleanTitle` keeps only characters the font has (same ranges as `OverlayFonts.LatinRanges`) and wraps the title in plain quotes. Title size default 13 and offsets X/Y (`TitleOffsetX/Y`) added to place it.

## Y offsets: plus is UP in every editor (owner's rule, 2026-10-28)
- The saved values keep counting downwards (screen coordinates), so no saved file changes. Only the editors turn the sign: unlock overlay "Y", Layout page "Offset Y", unit frame / party text and status row offsets (position, time, stacks), tracker time/stacks (`PlacementControls`), nameplate name and title offsets.
- Rule for new editors: any vertical offset control shows `-value` and stores `-input`. Dragging in unlock mode is not affected.

## Nameplates: the title is part of the name block (replaces the earlier title options)
- No title options at all (no size, color, offsets, anchor, follow-game tick). Title text is drawn with the name's font, size and color, and moves with the name's position and offsets.
- Name + title are one block of two lines (title above or under the name as the game says, `GameTexts.TitleAbove`). The same point of the block goes on the chosen point of the bar. If the name is over the bar (anchor Y fraction < 0.5) the block grows UPWARDS when a title exists, so the extra line never covers the life bar; otherwise it grows downwards. Lines are aligned inside the block like the anchor (left / centre / right).
- Page order: Name card, Level card (no title card). Level has no placement of its own (after the name, enemies only).

- Removed the "Show the name" checkbox (`NamePlateSettings.ShowName`) of the Nameplates page: whether a name shows is only the game's option (flag bit 1). The progress bar page keeps its own `ShowName` (the spell name).

- Removed the nameplates "Hide the game's plate" checkbox (`HideNative`): it did the same as Enabled. Enabled on = our plates and the game's plates of the same kinds are hidden; Enabled off = the game's plates come back (the Enabled checkbox asks the game to redraw them).

- Removed `HideNativeCast` (nameplates): our progress bar on (Enabled + Show the progress bar) hides the game's enemy progress bar; off brings it back.
- A kind of player plate that we do not draw (Me / Party / Other players switched off) keeps the game's plate: `OnDataUpdate` asks `GameDataProvider.PlayerRelation` and skips it; the checkboxes call `RequestRedraw` so the game's plate comes back at once.

## Unit frames: "Parameter bar" page (HP bar + Mana bar together)
- One page "Parameter bar" (the game's own name). Its card has the master `Enabled` (player `UnitFrameSettings.Enabled`): on = NahjaUI draws and the game's player bar (addon of life + mana) is hidden; off = the game's bar shows and nothing of ours is drawn. Inside, two tabs: Life bar and Mana bar, each with its own "Show ..." checkbox (`ShowLife` new, `ShowMana`) and its per-job on/off list. Position stays separate (two layout elements).
- The player's "Hide the game's life and mana bar" option is gone (always follows Enabled). The other frames (target, focus, party) keep Enabled + Hide native.
- `lifeOn = Enabled && ShowLife && job on`, `manaOn = Enabled && ShowMana && job on`.
- Migration (`MigrateParameterBar`, once): old Enabled=off with ShowMana=on becomes Enabled=on and ShowLife=off.
- Still to review: the other "Hide the game's ..." checkboxes (target, focus, party, enmity list, gauges) for the same redundancy.

- Enmity list: removed the "Hide the game's enmity list" checkbox (`HideNative`). Enabled on = ours drawn and the game's list hidden; off = the game's list shows.

## Enmity list follows the game's cast option
- Our enmity list casts show only if the game's option is on: `Plugin.GameConfig.UiConfig.TryGet("EnemyListCastbarEnable", out uint)` (0 = off; unreadable = shown). Own checkbox `ShowCast` removed.
- Other game options with "EnmityList" in Dalamud's config list: UiConfig `EnmityList` and UiControl `EnemyListDisp`. What their numbers mean is NOT known yet: the Casts card shows the three raw values (test line) so they can be matched to the game's settings by changing them in game. Do not use them before that.

- CONFIRMED in game (2026-10-28): `UiConfig EnemyListCastbarEnable` 1/0 = progress bars on/off; `UiControl EnemyListDisp` 1/0 = enmity list shown/hidden; `UiConfig EnmityList` always read 0 (not used). Our list follows both (list hidden when Disp = 0, except the unlock-mode samples). The test line with the raw values was removed.

## Enmity list: width and cast placement
- Bars card: Width (px) (same layout width as in Layout / unlock mode).
- Cast card: side (`CastSide`: Below / Right / Left of the bar), width (`CastWidthPx`, 0 = same as the bar), height, space between bar and cast, offsets X (plus = right) and Y (plus = up, saved downwards). Below reserves room in the column (`castBlock`); at the sides it does not. `DisplayRect` and the game cover use the box of the bar plus the place of its cast.

## PILLAR 2: one protocol for every frame (owner's rule, 2026-10-28)
Every frame NahjaUI draws (player parameter bar, target, target of target, focus, party, enmity list, nameplates, and any new one) offers the same things, in the same order, whenever the frame has them:
1. Enabled (follows the game: ours on = the game's hidden; no separate "hide the game's" checkbox, except where the game has two different elements).
2. Size: width and height (also reachable from Layout / unlock mode).
3. Bar: rounding, colors (life, background, border).
4. Name: show, point of the bar, offsets (plus Y = up), size, font, color. (`FrameTextSettings` + `UnitFrameEditor.TextControls`, drawn with `UnitFrameModule.DrawText`.)
5. Life text: same controls (+ format where it applies).
6. Cast (when the frame has one): side, width, height, space, offsets; spell name and time left as `FrameTextSettings` too.
7. Buffs / debuffs (when it applies), clicks (left target, right menu).
8. Anything the game has an option for is read from the game, never repeated as an own option.
New frames start from this list; check it when touching an old one.

## Enmity list follows the frame protocol
- Name, life percentage, cast spell name and cast time are `FrameTextSettings` (point, offsets, size, font, color, show). Cast name default: centred, Y -1; time: right. Old own `TextSize/Font/ShowPercent` were removed.
- The name starts after the enmity dot and is shortened with "..." before the percentage / the time.
- Other frames still to check against the protocol list.

## Page texts: no repeated intros (owner's rule, 2026-10-28)
- Under the page title and subtitle there is no extra text that repeats the subtitle or says that things are moved in Layout / unlock mode (obvious) or that samples show while the page is open. Keep only a line that tells something not obvious (e.g. nameplates have no Layout place; "hover an icon to see the tooltip"). Same rule for new pages.
- Texts that mention an offset direction say +Y is up (the tracker text controls said "+Y is down": fixed).

## Test step (2026-10-28): game colors and untargetable enemies
- Nameplates: with the test checkbox, next to each plate: `flags N text XXXXXXXX edge XXXXXXXX` = `handler.TextColor` / `handler.EdgeColor` (colors the game gives the plate by category, from the player's Character Configuration colors). Plan: once the number format is known, use them for our name color, outline and bar, and remove our own color options.
- Enmity list: with the test checkbox, `act A lock L tgt T` next to each bar = `EnmityListEnemyNumberArray.ActiveInList`, `.LockedInList` and `IGameObject.IsTargetable`. Idea to check: the game dims the rows of enemies that cannot be targeted. If one of them matches, dim our bar the same way (and no clicks).

## Nameplate colors come from the game
- Name text and title: `handler.TextColor`; outline: `handler.EdgeColor`; life bar fill: the same text color. Captured per entity in `OnDataUpdate` (`gameColors`). Own options removed: name color, my / party / other players bar colors. The level keeps its own color (the game has none for it).
- ASSUMED FORMAT (to confirm in game): a number with red in the lowest byte (0xAABBGGRR, same as ImGui colors); alpha 0 is drawn solid; 0 = not set (white text / black outline). If red and blue look swapped, the format is the other one: swap the bytes in `FromGame`.
- Enemy bars use the same text color (so engaged / claimed / unclaimed colors from the game apply). `FallbackBarColor` only covers the first frames.

## Correction: name color is ours again (owner's rule, 2026-10-28)
- CONFIRMED in game: the number format of the game's colors is right (red is red).
- In the game's own options the color only means the BAR color, not the name. So: bar fill = game's text color; outline of the texts = game's edge color; name and title color = `NameColor` (own option, default white); level = `LevelColor` (own option). These two are not game options, so they are ours.

## Nameplates: outline black again, game's name icon
- Outline of the texts: black fixed again (the game's edge color looked bad). The edge color is only shown in the test line.
- The small icon at the left of the name (hostile / neutral / hunt ranks): `handler.NameIconId` captured per entity every update (`gameNameIcons`, -1 = none), drawn as a game icon (`TextureProvider.GetFromGameIcon`) at the left of the name line, only when the game would show the name. Own options: show it, size (px), space to the name. The test line now also shows `icon N`.
- TO VERIFY IN GAME: that all the icons seen on the game's plates come through this field (some symbols are text characters inside the name, not icons). The marker above the head (`MarkerIconId`) is not drawn yet.

## Nameplates: marker above the plate
- `handler.MarkerIconId` (big icon the game floats above the plate: quest marks, objectives; 0 = none) captured per entity (`gameMarkerIcons`) and drawn centered over the bar. Own options in the Name card: show, size (default 32 px), offset X/Y (Y up in the editor, saved downward). Test line shows `marker N`.
- TO VERIFY IN GAME: which plates actually carry a marker and whether the default position clears the name.

## Enmity list: test option to see the game's list too
- `ShowGameList` (test, default off): the game's `_EnemyList` is no longer hidden and our rows are drawn without the dark cover, so both can be compared (move ours in unlock mode). Purpose: watch how the game's list reacts in an untargetable phase (Susano trial) and find which value changes. Remove when the untargetable dimming is solved.

## Nameplates: game's marker hidden when ours is drawn
- The game kept drawing its marker (above the plate) although the plate is switched off, so it showed twice. Now `handler.MarkerIconId = 0` is written when `ShowMarkerIcon` is on. The stored id is only overwritten by a zero when `handler.IsUpdating` (our own zero would otherwise erase it).
- TO VERIFY IN GAME: that the game's marker disappears, and that ours still updates when the marker goes away (quest done, etc.).

## Nameplates: game's target arrow (step 1, test)
- Option `HideGameTargetArrow` (test, off by default): hides the addon "_TargetCursor" in PreDraw like the other native windows. The class `AddonTargetCursor` exists in the clones but the addon name string is from memory and NOT confirmed: if the arrow stays visible, the name or the idea (maybe it is not this addon) is wrong.
- Step 2 (not done): our own subtler marker for the target (arrows at the sides of the plate, or similar). The game has no option for the arrow, so a checkbox of ours is acceptable here.

## Nameplates: target rules (agreed with Nahja)
- `ShowBarOnTarget` (default on): the target's plate always draws the life bar, even when the game's options would show only the name or nothing. Deliberate exception to "follow the game": it is a visible checkbox of ours. The name still follows the game's flags. Applies to what we draw (enemies, players when enabled).
- The target is marked by the accent ring around the bar (replaces the game's big arrow, `HideGameTargetArrow`, now a normal option; "_TargetCursor" works, confirmed).
- PLAN (next phase, not done): things WITHOUT a life bar (NPCs, doors, pets, objects) need text-only plates of ours; the target among them is marked with chevrons `‹ Name ›` pointing inward, in the accent color (U+2039/U+203A are inside the font ranges). Until that phase exists, hiding the game's arrow leaves those targets without any mark: keep the option off if that bothers.

## Nameplates: options removed (decided with Nahja)
- Removed: intro line of the page; "Follow the game's options" (now fixed behavior); "Click on a plate to target" and every click window over plates (invisible windows over many plates stole the mouse focus and broke camera turning with the held mouse; they stay on player/target/party frames); "Smaller plates when far"; "Hide the game's target arrow" (now always hidden while nameplates are Enabled); the whole Players card (Draw players / Me / Party / Other players: when Nameplates are enabled, they are for every unit; whoever wants the game's plates elsewhere turns ours off).
- Kept: Enabled, "Always show the life bar of my target", and the flags/colors test line (still useful until the text-only plates phase; remove then).
- Old saved settings with the removed names are ignored by the loader.

## Nameplates: every kind of plate (text-only plates, chevrons)
- Now ALL the game's plates are taken over while Nameplates are enabled (every kind: enemies, players, pets/chocobo/friendly battle NPCs, NPCs, doors/objects, minions, retainers, gathering points, treasure). The kind filter of `OnDataUpdate` is gone.
- Provider (`BuildNamePlates`): the unit is found through the object the plate points to (`ObjectInfo.GameObject` -> `ObjectTable.CreateObjectReference`), not through the entity id (objects can have none). New relations: `Friendly` (battle character, not hostile, not a player: has life) and `Other` (no life: text only). The snapshot carries `Key` (GameObjectId) and `IsTarget` (matches the current target by GameObjectId). All per-plate dictionaries in the module are keyed by GameObjectId now.
- Text-only plates (`Other`): never a bar; the name follows the game's flags (a zero the game just wrote counts as "show nothing" for these kinds only; enemies/players keep the old rule). Texts (name, title) are read from the handler for every kind except enemies.
- The TARGET without a life bar gets accent-colored chevrons pointing inward (`›` Name `‹`, U+203A / U+2039, inside the font ranges) around the name line (left of the game's icon when there is one), and its name always shows.
- TO VERIFY IN GAME: names of NPCs/doors/objects/minions show as the game would; the game's own plates really disappear for every kind; performance in crowded cities; chevrons on target NPC/door/pet-without-bar; titles of NPCs (e.g. <Gatekeeper>) look right; whether nothing important lost its plate (retainers, treasure coffers...).

## Nameplates: name icon only on plates with life
- A leaf-quest "Destination" (an object) showed the hostile icon while the game shows only the name. `NameIconId` of non-fighting plates is not reliable (probably a leftover of the plate slot), so the small icon is now drawn only for plates with life (enemies, players, friendly battle characters). The large marker is separate and unchanged.
- TO VERIFY: if some NPC/object does have a real small icon in the game, it will be missing now (the test line still prints `icon N` for every plate).

## Nameplates: marker anchored to the name
- The game's big marker (quest marks...) now sits centered over the NAME BLOCK (title included) and on top of it; when the name is not drawn it falls back to the bar. No selectable anchor (a marker has one natural place): only size and offset X/Y (Y measured from the top of the name block; editor Y up, saved downward). Default Y is now 2 px (saved -2); Nahja will give the final value to use as the default for fresh installs.

- Final default for the marker Y: editor value -6 (saved +6: the icon's bottom edge 6 px below the top of the name block), tested by Nahja. -22 in the editor sat too low and covered the name.

## Nameplates: the name line is centered with its icon
- The small icon (and the level) are part of the name line: the width used to center/align the block is icon + gap + name + level, so the whole line is centered over the bar (before, only the text was and the icon hung to the left, which looked off-center). The title is aligned inside the same block.

## Enmity list: untargetable enemies are drawn half transparent
- Values read in game (Susano trial): a normal enemy shows act 1, lock 0, tgt 1 (whether targeted or not); when the enemy cannot be targeted (the game fades its row to ~50%) all three go to 0. `tgt` (IGameObject.IsTargetable) is the signal we use.
- Rows with `Targetable == 0` are drawn with 50% alpha (`UntargetableAlpha`) and are not clickable. Done with `Core/Ui/DrawListFade`: it multiplies the alpha of the vertices drawn since a mark (works for bar, border, texts and cast together). No option: we follow the game.
- The meaning of `act`/`lock` is still unknown (not needed). The "show the game's values" and "keep the game's list visible" test options can go when Nahja confirms the dimming.
- TO VERIFY IN GAME: the row really fades during the closed phase and returns to normal when it can be targeted again; no flicker.

- Enmity list: the enmity dot has its own size (diameter in px, default 12; before it was 22% of the bar height, about 8 px, too small). Fix: `DrawListFade.cs` was missing `using System;` (MathF).

## Target ring: no gap
- The accent ring of the target (enmity list rows and nameplate bars) was drawn with its rectangle pushed out by a full line width, but a line is centered on its rectangle, so a gap of half a line (about 1 px with a non-integer UI scale) showed the screen between bar and ring. Now the rectangle is pushed out by HALF the line and the line width is whole pixels, so the ring touches the bar.
- Reviewed every frame that draws a ring or a frame outside a bar: enmity list, nameplates and PARTY frames had the gap (fixed the same way in the three). The job gauge pip glow (offset = line width, line centered) already touches the pip border, and the tracker borders and unit frames have no outside ring: nothing to change there. Rule for any new ring: push the rectangle out by half the line and use whole-pixel line widths.

- Party frames: the small arrow beside the targeted member was removed (our own invention, the game has none; it overlapped the buffs on the left). The ring around the member (same as the other frames) is the only mark.

## Target ring: one global color, always on
- `Configuration.TargetRingColor` (default WHITE; Appearance page > "Target ring color"; included in the Appearance block of profiles; "Reset to default" resets it too). The ring around the target is drawn with it on the enmity list rows, the nameplate bars and the party frames; the nameplate chevrons use it too. Reason: the same signal must look the same everywhere, and the old accent violet got lost on dark/blue backgrounds. (On very light backgrounds white can get lost: the user changes it.)
- Party frames: removed `SelectionRing` (the ring is always on) and `SelectionColor` (old saved values are ignored).

## Enmity list: out of range fade
- Seen in game (Fire Sprite far away): `act 0 lock 0 tgt 1`; the game fades the row a little, it is still clickable but cannot be selected because of the distance. So `act` (ActiveInList) very probably means "in range / selectable". Rows with `act 0` and `tgt 1` are drawn at 70% (`OutOfRangeAlpha`, an ESTIMATE of the game's lighter fade) and stay clickable; `tgt 0` stays at 50% and not clickable.
- TO VERIFY: that `act` flips with the distance (walk in and out of range), and tune 0.7 against the game's own row.
- Correction (Nahja): an out-of-range row (`act 0`, `tgt 1`) is NOT clickable in the game; only its hover glow shows. An untargetable row (`tgt 0`) shows neither glow nor click. So our rows are not clickable when `act 0` or `tgt 0`. We have no hover glow on the rows (the game's glow on targetable rows is not copied yet).

## Clicks: no right click menu where the game has none; target of target can be name only
- The game's right click menu is opened only on frames where the game has one (player, target, focus, party). The enmity list rows and the target of target now only react to the LEFT click (select). `HandleClicks(..., contextMenu)` has the new parameter (default true). The enmity list checkbox is now "Click on a bar to target".
- The target of target in the game does have a life bar and can be selected with the left click (checked by Nahja): it keeps its bar by default. New option `UnitFrameSettings.ShowFrame` (only used by the target of target page, "Show the life bar (off: only the name)"): off hides background, life, shield, border and the life number; the name stays at its place and the box is still the click area. Name color etc. unchanged for now.
- Also: those boxes are not covered/hidden by our drawing when the game's menu is open because the enmity list and target of target no longer open any menu.

## Terminology and foldable cards
- CODE NAMES FOLLOW THE GAME TOO (decision of the user, old saved configs/profiles may be lost, accepted): everything in code, ids and saved keys now uses the game's words: Enhancement/Enfeeblement (not Buff/Debuff), Hp (not Life), Mp (not Mana): `Enhancements`, `Enfeeblements`, `PlayerEnhancements`, `isEnhancement`, `HpColor`, `ShowHp`, `MpElement`, `"unitframe.player.mp"`, card ids `##card_pf_enhancements`, `##card_np_hp`... Exceptions that are NOT ours and must keep their name: game addon `_TargetInfoBuffDebuff`, `VtxBuffer`/`buffer`, `AddonLifecycle`, `...Manager` classes. WRITE ALL NEW CODE WITH THESE WORDS. The rules below that say "code names stay" are superseded by this line.
- TERMINOLOGY RULE: in everything the player reads, the life of a unit is called HP (the game's own word), never "life" or "health". Code names (LifeColor, ShowLife, card ids like `##card_np_life`) stay as they are; card ids must NOT change (the folded state is saved by id).
- Interface words (agreed with Nahja): the list on the left of the main window is the MENU (sidebar), the row at the top of a page (e.g. Parameter bar / Target / Target of target / Focus / Party / Class colors) are TABS, and each box inside a tab is a CARD.
- FOLDABLE CARDS: the first `Widgets.SectionLabel` of a card is its header; clicking it (a small triangle at the right: down = open, pointing left = folded) folds the card down to its title. Implementation in `Widgets.DrawBox`: the card box is shrunk to the header height and its content is still run but clipped by the box (nothing below can be seen or clicked); the full height is still measured only while open. The folded ids are saved in `Configuration.CollapsedCards` (loaded into `Widgets.Collapsed` in the plugin constructor; `Widgets.CollapsedChanged` saves). Callouts and boxes without a SectionLabel are not foldable. Everything starts OPEN. Idea for later: fold the long cards by default for new users.
- TO VERIFY IN GAME: the click on the header folds/opens, the next cards move up with no jitter, nothing below a folded title can be clicked, and the state survives a restart.

- Terminology, third part (buffs): the game calls buffs "Enhancements" and debuffs "Enfeeblements" (HUD Layout: groups Enhancements / Conditional Enhancements / Enfeeblements / Others). Everything the player reads says enhancements/enfeeblements, never buff/debuff ("Enhancements", "Player: enhancements", "A enhancement" -> "An enhancement"). Code names (`Buffs`, `Debuffs`, `PlayerBuffs`, `isBuff`), card ids (`##card_pf_buffs`...) and saved/profile keys stay as they are.
- Terminology, second part (checked on the FFXIV wiki: HP, MP = "magic points", CP = crafting, GP = gathering): the game never says "mana"; everything the player reads says MP ("MP bar", "MP color"...). On crafter/gatherer jobs that same bar shows CP / GP, still called the MP bar in the settings. Internal ids (`ManaElement`, `"unitframe.player.mana"`, the job toggle id `"mana"`) stay.
- Foldable cards, fix: the first control of a folded card peeked out under the title. A folded card now clips everything after its title with an EMPTY clip rectangle (pushed in `SectionLabel`, popped in `DrawBox`) and its height is the title line plus the window padding top and bottom.

## Buffs and debuffs: what the game does natively (reference for the rework, 2026-10-29)
Sources: the official UI guide (na.finalfantasyxiv.com/uiguide: "battle buff separate", "ecast setting"), the Square Enix forum thread "Suggestion: option to show..." (party list 5-10 icons), and the game structures in the FFXIVClientStructs / Dalamud clones (`HudNumberArray`, `Hud2NumberArray`, `PartyListNumberArray`, `AgentHUD.HudStatus`, config enums). Items marked (?) are NOT verified in game.
- PLAYER: HUD Layout element "Status Effects". Layout option "Split Element into 3 Groups": together (default; enhancements and enfeeblements in the same row) or split into Enhancements, Enfeeblements and Other. Each group has its own position, size and transparency. The structure has `HudStatusElementLayout`: ThreeElements = 0, OneElement = 1, FourElements = 2 (a four-element layout exists in the structure; the official page documents only three groups) (?), and four index lists: positive (20), negative (20), other (20), combo (8) over 30 icon ids with a "dispellable" flag each. `AgentHUD.HudStatus` knows 4 `_StatusCustom` addons (?). An Esuna-curable status is marked with a bar above its icon (official). Each icon shows a timer and a stack count.
- TARGET: window `_TargetInfoBuffDebuff` (the statuses part of the target bar). HUD Layout element "Target Bar" option "Display Target Info Independently": splits it into separate HP, status and progress-bar elements, each with its own size and transparency; otherwise the combined `_TargetInfo` (`TargetInfoUICombined`). Data: up to 30 icons, each with a dispellable flag and a UIColor row id in the last 3 bytes of the icon id. Config `UiControl TargetInfoSelfBuff` is very probably the official option "Displaying only the enfeeblements inflicted by you" (?).
- FOCUS TARGET (`_FocusTargetInfo`): at most 5 statuses (array of 5 + dispellable flags).
- PARTY LIST (`_PartyList`): per member at most 10 icons (+ dispellable flag); Character Configuration > UI Settings > Party List sets how many are shown (5 to 10; config `UiConfig PartyListStatus`) and `PartyListStatusTimer` (?). Buffs and debuffs are in the same list, debuffs first, with no separate control (forum).
- Not found in the game: statuses on the enmity list, on nameplates, or on the target of target.
- Other config names that may matter (meaning unknown): `UiConfig BuffDispType`, `ColorBuffGive`, `ColorDebuffGive`.
- Battle Effects (visual effects of skills: show all / limited / enemy PC / none) are NOT status icons: separate topic.
- What NahjaUI has now: party frames with one buff row and one debuff row each (`StatusRowSettings`: filter all/by id, extra ids always shown, only mine, anchor point and offset, icon size, crop, rounding, spacing, max icons, growth direction, timer and stack count with font/anchor), the same rows on player/target/focus frames (`StatusRowRenderer`; the native target statuses are hidden while ours show), tooltips, and the tracker engine (separate system).
- NEXT: a Debug card that prints live the values above (arrays + config options) so the owner can flip the HUD Layout options in game and we see what changes; then decide the native-copy model (which groups, per frame) and only then the value-add (ideas: priority/size by importance, own first, dispellable highlight, group by category, filters per job).

## Enhancements and enfeeblements rework: decisions (user, 2026-10-29)
- The game's own HUD Layout > Status Effects options are RESPECTED, not ignored: single element, 3 groups or 4 groups (Enhancements / Conditional Enhancements (8 slots) / Enfeeblements / Others), each group placeable by the player, with the game's own display settings (Default / Prioritize own / Others' enhancements in group 4). Whoever wants the split can use it with no filter, like in the game.
- The user himself plays with ONE element (no split): all enhancements together at the game's natural place (top right, next to the minimap). He tracks the important ones with trackers (a duplicated enhancement is fine). NEVER build an option to hide from the row the statuses a tracker already tracks.
- Enfeeblements matter in raids: movable, centrable, able to grow from the center outward; stable order (by moment of application, not re-sorted by time each frame, because growth from the center moves every icon when one appears).
- Keep the game's RIGHT CLICK on an own status (it cancels removable ones like Sprint). `StatusManager.ExecuteStatusOff(statusId, sourceId)` is a static function in the clones, so it is possible. If our row replaces the game's, it must keep it.
- Focus: same philosophy as target/player; the user does not use the focus himself.
- Later idea (trackers): a filter that shows all the important party enhancements (the 2 minute burst windows).
- Classification of a status: do not depend only on the HUD arrays; the status list plus the Status sheet Category (1 enhancement, 2 enfeeblement, else other) is independent of the HUD options. The HUD arrays are what tells in which group the game puts it.
- Debug page: card set "Statuses: ..." (`Windows/StatusDebugCard.cs`) prints live the HUD arrays (layout, 4 slot lists, icons, dispellable), target/focus statuses, party list statuses, game options (PartyListStatus, PartyListStatusTimer, BuffDispType, ColorBuffGive, ColorDebuffGive, TargetInfoSelfBuff and any typed name) and whether the guessed native windows (`_Status`, `_StatusCustom0-3`, `_TargetInfoBuffDebuff`...) are visible. Purpose: the user flips the game's options and reports what moves; then the copy model is decided.

## Status effects page (player enhancements and enfeeblements), first step
- New page "Status effects" (sidebar, after Unit frames), module `Modules/StatusEffects/` (`StatusEffectsSettings`, `StatusEffectsModule`, `StatusEffectsEditor`), config `Configuration.StatusEffects`. The player frame (HP/MP, "Parameter bar") NO LONGER has enhancement/enfeeblement rows; target and focus keep theirs for now (the user wants to do the player first and then see).
- `StatusEffectsSettings.Enabled` (default off): draws ours and hides the game's windows `_Status`, `_StatusCustom0..3` (names guessed from AgentHUD, to confirm in game: Debug > "Statuses: game windows").
- It FOLLOWS the game's layout option (read live from `HudNumberArray.StatusElementLayout`): OneElement -> group Single; ThreeElements -> Enhancements, Enfeeblements, Others; FourElements -> Enhancements, Conditional, Enfeeblements, Others. Each group = own layout element (`status.single`, `status.enhancements`, `status.conditional`, `status.enfeeblements`, `status.others`, top right by default), own `StatusGroupSettings` (Enabled, Columns, Justify Left/Right/Center, RowsGrowUp, and `Look` = a `StatusRowSettings` used only for icon/time/stack look; its filters are NOT used: a group shows what the game puts in it, no filters).
- Group membership for now: by the Status sheet category (1 enhancement, 2 enfeeblement, else other), in StatusList order. The conditional group is empty until the HUD arrays are understood (the debug showed: in 3/4 groups the slot lists index the icon slots, in 1 element they do not; icons carry bit 0x40000000 which looks like "applied by me").
- Right click on an icon calls `StatusManager.ExecuteStatusOff(statusId, sourceId)` (the game checks it can be canceled). Tooltip as before (place "player").
- Layout box of a group: width/height are set by the module every frame from columns/rows/icon size; only the position is the user's.
- NOT done yet: profile export of this page, reset button, the order inside a group as the game orders it, the unknown extra statuses (icon not in the status list), dispellable mark.

## Giving the game's windows back (fix, 2026-10-29)
- Bug: when a module stopped hiding a game window (Enabled turned off) the window stayed invisible until a loading screen, because hiding sets `IsVisible = false` and the game never sets it back by itself.
- Fix: `Game/NativeWindowHider.cs`. `Hide(args)` (from PreDraw) skips the drawing, marks the window not visible and remembers it only if it WAS visible; `ShowAgain(stillHidden)` is called at the start of every module's `Draw()` (even if the module is off) and sets `IsVisible = true` for the remembered windows the module no longer wants hidden. A window the game itself had hidden is never made visible. Used by: unit frames, status effects, progress bars, job gauges, enmity list, party frames. EVERY NEW MODULE THAT HIDES A GAME WINDOW MUST USE IT.
- Known edge: if the game's layout changes while a window is hidden by us and the module is then turned off, the window may come back visible although the game wanted it hidden; the next HUD change of the game fixes it.

## TEXT SIZE = HEIGHT OF THE LETTERS (decision, all frames)
- Before, "12 px" meant the font's line box, which is much taller than the letters, so 24 on a buff timer looked tiny and 24 on a nameplate looked huge, and anchors (Top/Center/Bottom) used the invisible line box.
- Now the size of every overlay text is the HEIGHT OF A CAPITAL LETTER / DIGIT in logical pixels, and anchors use that visible box. 24 is 24 and 12 is 12 everywhere.
- Implementation: `OverlayFonts.PushCap(font, capPx)` returns a `TextFace` (Core/Ui/OverlayFonts.cs): it measures the cap ratio of the family from glyph 'H' (cached), picks the closest built font and draws with the exact size. `face.Width(text)`, `face.Measure(text)`, `face.Height`, `face.Draw(drawList, topLeftOfLetters, text, color[, outline])`. `Fit` helpers take the face.
- Converted: tracker/status timers and stacks (TrackerEngine.DrawOverlayText), unit frames text, progress bars, enmity list, job gauges, nameplates (name/title block uses a line gap of 0.45 x letter height, cast text). Menu and tooltip text stay as before (font size).
- `OverlayFonts.AvailableSizes` is now the list offered in the editors (6..48); fonts built are `BuiltOverlaySizes`.
- Defaults lowered to keep the old look (timer 12->8, progress bar 14->10, name 14->10, unit frame 16->11); saved values keep their number, so they now look bigger.

## Enmity dot and party job icon (decisions)
- The word "aggro" is gone everywhere: the game calls it enmity (`ShowEnmityDot`, `EnmityDotSizePx`, `EnmityDotAnchor`, `EnmityDotOffsetX/Y`; saved values of the old names are lost on purpose).
- The enmity list dot is placed like the texts: a point of the bar (anchor) + offsets; default Left, X 8, Y 0. The name starts after the dot only when the dot is in the left half.
- The party frame job icon no longer has a cap at the bar height: it is the size chosen (the name moves right by the icon width).

## Enmity list (rename) and clicks
- "Enemy list" is now "Enmity list" everywhere (names, files, folder `Modules/EnmityList`, layout id `enmitylist`, page, config `EnmityList`). Saved settings of the old names are lost on purpose. The game's own identifiers stay as they are: `_EnemyList` (window), `EnemyListNumberArray`, `EnemyListDisp`, `EnemyListCastbarEnable`.
- The option "Click on a bar to target" is removed: clicking a bar always targets the enemy (left click) like before when it was on.

## Text anchors: vertical centre on the anchor line (decision, texts and enmity dot only)
- Horizontal: the same point of the text and of the box meet (left-left, centre-centre, right-right). Vertical: the CENTRE of the letters sits on the anchor line (top edge, middle or bottom edge of the bar). So Top left = text centred on the top edge, Bottom left = on the bottom edge; no overlap between them even on a 6 px bar.
- Applies to unit frame texts (also used by the enmity list), tracker timers/stacks, the nameplate name block and the enmity dot. NOT to the Layout (ScreenAnchor/SelfAnchor stay corner to corner). Saved offsets of Top/Bottom texts now look different.
- Fix (cap ratio), TESTED by Nahja: the ratio letter height / font size is measured with `ImFontPtr.FontSize` (size the font was built at), not `ImGui.GetFontSize()`. Before, letters came out about twice too big; with the fix they are the real size. Do not revert.

## MP text
- The MP bar of the Parameter bar has a text with exactly the same choices as the HP text (what it says, separator, decimals, thousands, position, size, font, color): `MpTextSettings` (Off by default, default says "738K" style current value). One shared card `DrawValueText` and one shared formatter (`IValueTextFormat`) for HP and MP.

## Appearance page reorganised
- Theme card: accent color, menu text size, reset. New card "Game interface": target ring color, timer tenths, "use the NahjaUI font everywhere". The class colors card moved from Unit frames to Appearance. Unit frames picker no longer has "Class colors".

## Window chrome (decisions)
- The main window has NO title bar of the window system (no hamburger menu, no pin/clickthrough, no Dalamud buttons). It draws its own bar: NahjaUI icon + name at the left, arrow (minimize) and cross (close) at the right. Drag the bar to move the window (a part always stays on screen); double click minimizes.
- The small bar shows the icon + name, the arrow (opens the window) and the cross. Same arrow in both (a triangle pointing up, like the card arrows). `Core/Ui/TitleButtons.cs` draws both buttons with shapes, centred in a round highlight.
- Appearance > Theme: `Window corner rounding (px)` (config `WindowRoundingPx`, default 10, saved in profiles), `Center the window` button, `Reset to default`. First opening: centred on the screen. The "Preview" card is gone.

## Role colors replace job colors (decision)
- The 40 job colors (`JobColors.cs`, `JobColorOverrides`) are gone. There are three role colors in Appearance > "Role colors": tank, healer, damage (`UnitFramesSettings.TankColor/HealerColor/DamageColor`, defaults blue 0.31/0.55/0.88, green 0.31/0.75/0.45, red 0.85/0.33/0.31). No official hex number was found (the game's role colors live inside its textures); the defaults are the ones the party stripe already used. Editable.
- `HpColorMode.Class` is now `HpColorMode.Role` ("The color of the role (tank, healer, damage)"). `JobRoles.RoleOf(jobId)` gives the role (crafters and gatherers have none and use the frame's normal color). The party stripe uses the same three colors.
- Nameplates keep taking their colors from the game's own options; nothing of ours there.

## A position for each job (Layout)
- Any positionable element can have "A different position for each job" (Layout page, element editor). Config `JobLayouts`: element id -> (job id -> `ElementLayout`), job 0 is the base (what a job never moved starts from). Position AND size are saved per job.
- `LayoutManager.SyncJob(jobId)` runs once per frame (Plugin.Draw): when the job changes the element shows that job's layout; otherwise what is moved/edited (unlock mode, editors like the MP bar's width) is copied into the job's entry. The modules keep reading the same `element.Layout` object, so nothing in them changed.
- Not done yet: profile export/import of `JobLayouts`; "copy from another job".

## Icon: two tones, symmetric (decision)
- The icon uses two solid tones only (accent and the accent at 55% over the tile), no transparency. Top tiles: bright left, dark right; bottom tiles: dark left, bright right. Ears mirror each other exactly and take the OPPOSITE tone of the tile under them (checkerboard going up); the inside of each ear has the opposite tone of its ear. Same in `images/icon.svg`, `images/icon.png` and `Core/Ui/NahjaMark.cs`.

- Trackers: a single tracker now has "Icon width (px)" and "Icon height (px)" like a group (before: one "Icon size" that forced a square).

- Stack / charge text size: the "automatic = 3/4 of the time size" is gone everywhere (trackers, party/target rows, Status effects). Everybody chooses the size; default 8, an old saved 0 counts as 8. Text sizes can now go down to 4 (before they were clamped at 8, which hid the new 6 and 7 px sizes).
- Tracker Display card order: time (show, size, position), then stacks (show, size, position), then the font last.

## Unlearned abilities
- Cooldown trackers whose ability has a learn level (Action sheet `ClassJobLevel`) above the player's current level are hidden. The player level is the game's effective level, so level sync in dungeons/FATEs is covered. No option (minimalism). Unlock mode still shows them as ghosts so they can be placed.
- Status trackers are not tied to an ability, so they are not filtered (a status that is not there is simply absent; only "show when missing" reminders could appear).

## "Hide the game's ..." options stay
- Decision: the option to hide the game's own element (progress bar, etc.) is KEPT, on by default. Reason: showing both at once is useful to compare the game with the addon. Do not remove these options.

## Cap-height ratio cache
- `PushCap` measures a family's cap-height ratio only with that family's OWN font (never with a fallback while it loads); otherwise a wrong ratio stayed cached for the whole session and every text of that family came out the wrong size.

## Target progress bar has no icon
- The game's target progress bar shows no spell icon, so ours never does and has no option for it (only the player progress bar has "Show the spell icon").

## Job gauges page: one job at a time
- The page shows the gauge of the job being played by default, with a "Job" combo to edit another job's gauge. Picking the current job goes back to following it. New jobs are added to `Entries` in JobGaugeEditor.

## Parameter bar pages: jobs card last
- HP page: the "HP bar on each job" card is the last one. MP tab: the MP bar card, the MP text card, then "MP bar on each job" (with "Show the MP bar") last. Same shape on both.

## Name icon only on enemies and players
- The small icon at the left of a nameplate name is drawn only for Enemy/Self/Party/OtherPlayer plates. Friendly battle NPCs (FATE turn-in NPCs, pets) hold leftover values in that slot (hostile or class icon) that the game never shows.

## Ability search finds combo steps
- `SearchActions` also accepts actions that belong to a job and have a learn level even if the sheet does not mark them as player actions (Riot Blade was not found by the search).

## Duplicate a tracker
- Right click on a tracker row in the Trackers list opens a menu with "Duplicate". The copy (all settings, name + " (copy)", new id) goes right below the original, in the same group or at the top level, with the same size and position. Done after the list is drawn (`pendingDuplicate`).

## Ability search includes non-hotbar actions
- `SearchActions` no longer requires the action to be a player action: Blade of Faith/Truth/Valor cannot be on a hotbar but are combo steps (their combo parent is Confiteor / the previous blade). Track them with a cooldown tracker on the blade + "Only while it is the next step of my combo". Job actions are listed first.

## Debug: Combo card
- The Debug page shows the combo the game reports (now and last seen) and what Blade of Faith/Truth/Valor follow, to diagnose combo-step trackers.

## Combo steps that are forms of another action
- Confiteor does NOT start the game's combo timer (Debug > Combo showed no combo after Requiescat + Confiteor). Blade of Faith/Truth/Valor are forms the Confiteor button turns into. A combo-step tracker is therefore shown when EITHER the game's combo is on the parent step, OR the first action of its chain (`GameNames.ComboRoot`, watched too) currently has the tracker's action as its adjusted form. In the second case there is no timer.

## Debug: Action flags card
- Lists the job's actions the game makes glow (`IsActionHighlighted`) or that are in another form (`GetAdjustedActionId`), to check what can be read for a future "ability available" tracker type (idea: 3 tracker types: status, cooldown, ability the game offers now; optional timer from a status or from the combo timer).

## Three tracker types + free counter
- Tracker types: **An enhancement or enfeeblement** (Status), **An ability cooldown** (Cooldown), **An activated ability** (Activated): shown only while the game turns the ability on. Offered = the game's glow on the current form of the ability (`IsActionHighlighted`), or, for a combo step (has a combo chain), the game's combo is on the previous step OR the first action of its chain (Confiteor) has turned into it (form). Cover/Clemency/Intervention report glow always (the flag is not a proc for them): not for this type.
- "Number on the icon" (`Counter`): Standard (status time / cooldown / ability cooldown), No number, The combo time, A status I choose, The cooldown of an ability I choose. Works for all three types; a chosen counter removes the recharge sweep.
- `OnlyAsComboStep` and the checkbox are gone; old trackers that had it become Activated on load (the field is kept only for that migration).

## Recommended use of "An activated ability" (tested in game)
- Track the BASE ability (Confiteor, Atonement): one tracker. It is shown while the game glows its current form and the icon changes with the form (Blade of Faith, Truth, Valor; Supplication, Sepulchre). No need for one tracker per step.

## Tracker editor redesign (agreed plan, in 3 deliveries)
1. (done) General card: Name, Enabled, **Trigger** (no group selector: the left tree does it). Right click on a tracker row: Duplicate and Delete. The "Delete tracker" button at the bottom of the page stays too (duplicated on purpose).
2. Texts: a list of texts on every tracker, each with its own source (cooldown of another ability, combo time, status time/stacks, ...) and the usual text controls (size, position, offset, font). Replaces "Number on the icon" and the timer/stack text options.
3. Display merged with the primary trigger: width, height, crop, corner rounding, glow. Cooldown trackers keep the recharge look and sweep. "Effect while active" stays on the primary trigger (its time can also be a text). Groups control icon size, crop and rounding; texts are per tracker.

## Tracker texts (delivery 2)
- Every tracker has a list of texts: Text 1, Text 2... (`TrackerDefinition.Texts`, `TrackerTextDefinition`). Each: source (own time, own stacks/charges, combo time, status time/stacks of a chosen status, cooldown/charges of a chosen ability), size (cap height), position + offset, font, colour. "Add text" button after the last card; "Remove this text" in each card.
- The old timer/stack text options of the Display card, and the old "Number on the icon" counter, are gone. Trackers saved before get their timer/stack text converted into texts once (`TextsMigrated`).
- A text with nothing to show (no time left, no stacks) draws nothing. Texts are drawn only when the tracker is really shown (not on unlock ghosts).
- The context menu of a tracker row has Enabled (first), Duplicate and Delete. In the General card, Enabled is the first control.

## Tracker editor order and defaults (after delivery 2)
- Page order: General, Ability to track / Status to watch (with "Effect while active" for cooldowns), Display, Text 1..n (+ Add text), Load conditions, Delete tracker.
- New trackers start with ONE text (own time, centre, offset 0,0). "Add text" adds a text at the bottom right, offset 0,0.
- Context menu of a tracker: Disable/Enable (one toggling entry), Duplicate, Delete.
- "Effect while active" stays: one icon shows both phases of an ability that gives a buff (buff time while it lasts, then the cooldown), with its own glow/recharge looks. Texts cannot do that phase change.

## "Effect while active" removed (cooldown trackers)
- Reason: the button of an ability can change form when used (Fight or Flight turns into Goring Blade), so the tracker could not follow its own buff. Cooldown trackers now show only the cooldown. Buffs are tracked with status trackers, or shown as a text (source: time of a status I choose).
- Old cooldown trackers with an effect: the effect becomes a text (status time, top right); extra effects are dropped.

## Icon of a tracker can differ from what it tracks
- Display card, "Icon": the icon of what it tracks (default), the icon of a status I choose, or of an ability I choose (`TrackerDefinition.Icon`, `IconStatusId`, `IconActionId`; `TrackerEngine.ResolveIcon`). Reason: the icon of the Requiescat status (1368) has its stack number drawn in it; tracking 1368 with the icon of 1369 (no number) and showing the stacks as our own text. No automatic icon-by-stacks (decided: the user prefers own typography and free placement).

## Prioritized triggers (WeakTrackers style) — replaces the single tracker Trigger
- A tracker has an ordered list of **icon triggers** (`TrackerDefinition.Rules`, `TrackerRule`). They are tried in order; the FIRST that matches decides the icon, the glow and the values of "the trigger that decides". None matching hides the tracker. Evaluated in `TrackerEngine.Evaluate` → `Match` (`TrackerState.Rule` is the one that matched).
- Conditions (`ConditionKind`): StatusPresent, StatusMissing (unit: player/target/focus/target of target, "only mine"), AbilityActivated (glow or form/combo step, as before), AbilityOnCooldown, AbilityReady, AbilityAlways (ready or recharging). An ability not yet learned (level, sync) never matches.
- Each trigger keeps its OWN icon choice (automatic / status I choose / ability I choose) and its own glow flags (Glow; Glow when ready for "always"; Glow when the game highlights it) and "Ignore the global cooldown". Example: Requiescat = trigger "status 1368 on", icon of status 1369, stacks as own text.
- Each **text** has an ordered list of sources (`TrackerTextDefinition.Sources`, `TrackerTextSource`): the first source that gives a value is drawn. Sources: time / stacks-or-charges of the trigger that decides, combo time, time/stacks of a chosen status, cooldown/charges of a chosen ability.
- Worked example (Fight or Flight / Goring Blade): triggers 1) Goring Blade activated, 2) Fight or Flight status on, 3) Fight or Flight cooldown (always). Text 1 sources: FoF status time, then FoF cooldown.
- Editor: page order General (Enabled, Name), Icon trigger cards, Display, Text cards, Load, Delete. Order is changed with ↑/↓ arrow buttons in each trigger / source. Drag-and-drop reordering is the NEXT delivery (user wants both). A trigger list always keeps at least one trigger.
- Display card keeps size, crop, rounding and (if any ability trigger) "While recharging" look and recharge sweep, which are per tracker / per group.
- Migration (`MigrateRules`, flag `RulesMigrated`): old Status → one StatusPresent/StatusMissing rule; Cooldown → AbilityAlways/OnCooldown/Ready; Activated → AbilityActivated; old icon choice and glow flags move into the rule; each old text source becomes the first entry of `Sources`. The old fields stay in the class only for this migration.

## Trackers explorer: anchored header, grab anywhere
- The header (title, New group, New tracker, "Show trackers of" label ABOVE its combo) is fixed; only the list below scrolls (child `##tracker_explorer_list`).
- No grip dots: any point of a row (tracker or group) grabs it to drag and reorder. Double click on a group opens/closes it (chevron still works).
- Icon triggers can also be reordered by dragging: press on any empty point of a trigger card (not on a control) and drag; a line shows the slot. Arrows stay. Text sources keep arrows only.

## Fill the width (tracker icons)
- Display card: "Fill the width (cut the icon's side margins)" (`FillWidth`, copied by groups). Status icons (24x32) have an empty/dark margin at the sides that does not fill a wide box. `Game/IconMargins` reads the icon image from the game files once per icon, finds the first columns (left and right, max 30 %) that hold art (opaque and not almost black, middle rows only), and `IconUv` cuts them on top of the crop. If an icon cannot be read it is drawn whole.

## One card per chain ("if not" chains)
- Icon triggers: ONE card "Icon triggers": "Trigger 1", then "If not, trigger 2", "If not, trigger 3"... Drag by an empty point of a section, or arrows.
- A text is ONE card with its own chain: "Text from" (source 1), "If not, from" (source 2)... The first source with a value is drawn and the rest are not. A second value at the same time (for example stacks) needs another text (Text 2) with its own chain. "Add a fallback source" adds a link to the chain.
- Same look for both chains: heading "Trigger 1" / "If not, trigger N" and "Source 1" / "If not, source N" (only when there is more than one), a separator between links, arrows + Remove on each, and a normal button at the end ("Add trigger" / "Add source").
- IconMargins reads `TexFile.ImageData` (already decoded, 4 bytes per pixel); `GetRgbaImageData` does not exist in the Lumina used.
- `FillWidth` is ON by default (new trackers, groups and trackers saved before the option existed).

## Ability picker of a trigger: search only
- No list of the job's abilities. One box "Search ability" (starts with the chosen ability's name). The matches show only while the box has the focus (or the mouse is over them, so the click lands) and not after a pick; picking writes the ability name, as in the game, into the box.
- Status picker of a trigger works like the ability one: no list of current statuses, one "Search status" box with matches while it has the focus, the name is copied into the box on pick.
- Right click on the title bar of the main window: menu with "Reset the window" (same as Appearance): centre of the screen AND natural size 1040x680 (Dalamud already multiplies a window size by the UI scale: never multiply it again), wide enough for the two columns of the Trackers page.
- Known issue to investigate: a text box in the UI sometimes loses the keyboard focus when the game selects an object. Nothing in our code asks for focus (no SetKeyboardFocusHere / SetWindowFocus) and we only draw one ImGui window plus the layout overlay windows.

## Status always (icon trigger)
- `ConditionKind.StatusAlways`: "An enhancement or enfeeblement, always (on or missing)". The icon is drawn whether the status is on the unit or not (needs a unit to look at: no target = hidden). While on: the time and stacks as texts. While missing: red tint/border like "missing", plus optional "Glow when the status is missing" (`GlowWhenMissing`). Use: Bard dots, buffs to keep up.

## Trigger menus in two steps; copies take the name
- Icon trigger card: "Track" (An enhancement or enfeeblement / An ability), then "When" with only the situations of that kind: status = It is on / It is missing / Always; ability = It is activated / It is recharging / It is ready / Always. Changing "Track" resets "When" to a sensible default and clears the search box.
- A tracker whose name is "New tracker" OR ends with " (copy)" takes the name of what trigger 1 tracks when it is picked.
- Icon of a trigger: "Icon" = Default (the icon of what it tracks) / Personalized; Personalized adds "Icon of" = An enhancement or enfeeblement / An ability, then the search box. (Same data as before: `IconSource`.)

## Effects of a trigger: Glow / Red / Dimmed, each with its "when"
- Per icon trigger, three combos: **Glow**, **Red** (red mask over the icon, border stays black) and **Dimmed** (darkened icon). Each one: Never / Always / and, depending on the trigger: Status always → While it is on / While it is missing; Ability always → While it is ready / While it is recharging; any ability trigger → While the game highlights it. (`TrackerRule.GlowWhen/RedWhen/DimWhen`, `EffectWhen`; `TrackerEngine.EffectActive`.)
- The border is always black (the red/double border of missing and recharging is gone). The tracker-level "While recharging" look is gone from Display (the recharge sweep stays).
- Migration (`MigrateEffects`, flag `EffectsMigrated`; new triggers are created with it true): old glow flags → one glow "when" (priority: always, missing, ready, highlighted); missing status → Red always; status always → Red while missing; recharging abilities → Red/Dimmed (while recharging) following the old tracker look.

## Text sources in two steps
- Each text source: "Text from" (The trigger that decides / The combo / An enhancement or enfeeblement I choose / An ability I choose), then "Show" (the time or the stacks/charges; the combo has no second step: its time). Same data (`TextSource`), same pattern as Track/When of the icon triggers.
- Safety: if the window (as drawn last frame) is bigger than the screen, it resets itself (centre + natural size) on the next frame, so a bad saved size can never trap the user.
- The icon search of a trigger ("Search status icon" / "Search ability icon") works like the other search boxes: matches only while it has the focus, and picking one writes its name into the box and closes the list.
- Effects (Glow / Red / Dimmed) for ability triggers also offer "While it can be used" (ready, or recharging with a charge left) and "While it cannot be used" (recharging with no charge left; a single-charge ability counts as unusable while recharging). "While it is recharging" stays: any recharge, even with a charge left.
- Final wording of ability effects ("always" triggers): Never, Always, **While it is ready** (can be used: no cooldown, or a charge left), **While it is not ready** (cannot be used: recharging with no charge left), **While it is full** (every charge available, none being recharged), **While it is recharging** (any recharge in progress), While the game highlights it. Single-charge abilities: full = ready. (Enum names: Usable, Unavailable, Ready, Recharging.)
- "While it is full" and "While it is recharging" are offered only for abilities with charges (runtime max charges from the cooldown snapshot); a value already saved stays visible. Unknown (no data yet) = all offered.

## Presets (trackers of a job, made from names)
- `Modules/Trackers/TrackerPresets.cs`: ready-made groups and trackers written by NAME; ids are looked up in the game data when applied (exact name; status: lowest id; action: not PvP, the job's own first). Button "Presets" in the explorer header → "Add the trackers of a job" → list. A short report shows under the buttons (what was added, names not found). Refuses to add twice (group "RDM Procs" exists).
- Style rules given by the user for the Red Mage preset (use for future presets unless told otherwise): icons 32x24, Fill the Width on, corner rounding 6, texts 12 px with offset 0,0: centred for times/cooldowns, bottom right for stacks/charges.
- Red Mage preset: group "RDM Procs" (Dualcast, Verfire Ready, Verstone Ready, Grand Impact Ready*, Prefulgence Ready*, Vice of Thorns Ready*, Magicked Swordplay with stacks; * = glow), group "RDM Cooldowns" (Fleche, Contre Sixte, Corps-a-corps, Engagement, Displacement with charges, Manafication / Embolden / Acceleration with the buff first and the cooldown after), group "RDM Utility" (Swiftcast, Magick Barrier, Addle, Lucid Dreaming). Cooldown triggers are "ability always", dimmed "while it is not ready". Not included: the melee combo (Riposte...) and the mana gauge (no gauge trigger yet).

## Icon trim replaces icon crop and "Fill the width"
- Display card: one combo "Trim icon margins": None / Sides / Top and bottom / All around (`TrackerDefinition.Trim`, `IconTrim`). The percentage slider (`IconCropPercent`) and the checkbox are gone. `Game/IconMargins` measures the empty margin of each side of the icon (art = opaque and not almost black, middle part only, max 30 %); `IconUv` cuts it. The box shape is still filled without distortion.
- Migration: old "Fill the width" on → Sides, off → None (also in group templates). New trackers and groups: Sides. Presets use Sides.

## Parameter bar page without tabs (done, needs testing)
- Owner decision: the extra "Parameter bar" card with the HP bar / MP bar tabs is gone. The Parameter bar page is one scrolling page: the HP bar cards (the first card holds the master "Enabled" switch for HP and MP, with its two explanation lines), then "HP bar on each job", then the MP bar cards (MP bar, MP text, MP bar on each job) at the end.

## Texts belong to each trigger (done, needs testing)
- Owner decision: the separate "Texts" cards of a tracker are gone. Every icon trigger (`TrackerRule.Texts`) has its own texts, edited inside the trigger section ("Texts of this trigger"): each text keeps the full set of options (value chain "Text from"/"Show", any status or ability of the owner's choice, size, font, anchor + offset, colour). The engine draws the texts of the trigger that decides (`state.Rule.Texts`).
- "Text from > Active trigger" is now "This trigger" (its own status/ability time, stacks or charges).
- Migration (`TrackerEngine.MigrateRuleTexts`, flag `TrackerDefinition.RuleTextsMigrated`): old trackers copy their texts into EVERY trigger, so nothing changes on screen at first; the tracker's own `Texts` list is emptied. The owner reviews the trackers one by one and adjusts.
- New triggers (`TrackerEngine.NewRule()`) start with one text (the time). Presets build texts per trigger.
- Red Mage preset: Engagement and Displacement (shared cooldown, learned at different levels) are now ONE tracker "Engagement / Displacement" with two ability triggers (Engagement first; the engine skips abilities not learned yet), each with time + charges texts.

## Mouseover on our frames (done, needs testing)
- Owner decision: only the Dalamud way (`ITargetManager.MouseOverTarget`, get/set), for compatibility with other addons. NO hooking of the game's action use (that is ReAction/MOAction's job; against "NahjaUI only redraws").
- While the mouse is over a clickable frame (player, target, target of target, focus, party bars; same hover as the click area) the unit is published as the mouseover target; it is cleared as soon as the mouse leaves (and on dispose). Done once per frame in `UnitFrameModule.UpdateCursor` (`UpdateMouseOver`). One global switch `UnitFramesSettings.Mouseover` (default on), shown in the card of every frame.
- Works with `<mo>` macros and plugins that read the mouseover. Not tested with ReAction/MOAction yet. The frame must be Clickable (same hover area).

## Foldable triggers (done, needs testing)
- With two or more triggers, each trigger of the "Icon triggers" card is a header ("Trigger 1: <what it tracks>", "If not, trigger 2: ...") that opens and closes like the "Texts of this trigger" header (owner asked for this instead of an arrow, which was confused with the move up/down arrows). Everything of the trigger, including its move/remove buttons, is inside the header, so a folded trigger shows one line. All start open; the open state follows the trigger when it moves and is not saved. A single trigger has no header.

## Effect label "not full" (done)
- In the effect combos (Glow / Red / Dimmed) of ability triggers with charges, "While it is full" has its opposite named "While it is not full" (it was "While it is recharging"). Same behaviour: true while any charge is recharging.

## PvP detection (done, needs testing)
- `ContextSnapshot.InPvP` = Dalamud `IClientState.IsPvPExcludingDen` (Frontline, Crystalline Conflict...; the Wolves' Den counts as PvE). If the property name does not compile, check the Dalamud API of the installed version.
- Owner decision: only these change in PvP. Job gauges: nothing drawn and the game's own gauge is not hidden. Enmity list: not drawn, not read, the game's list is not hidden (it does not exist in PvP). Trackers: new load condition "Content" (`TrackerLoadConditions.Where`: Only in PvE (default, also for old trackers) / Only in PvP / Everywhere). Frames, progress bars, nameplates, status effects stay as they are. Unlock mode still shows gauges and the enmity list so they can be placed.

## Names without game symbols (done, needs testing)
- PvP nameplates of enemies showed "?" on each side of the name: the game puts symbols from the Unicode private area (U+E000-U+F8FF) in some names and our fonts do not have them. `GameDataProvider.CleanName` takes those characters out of every unit name (plates, units, party). Assumption: the "?" are such symbols; if they remain, the cause is another character (to check with a Debug dump of the name's code points).

## Nameplate names without undrawable symbols (done, needs testing)
- Real cause of the "?" in PvP: the name of an enemy player plate is taken from the game's own text (`handler.Name`, used for every plate that is not a plain enemy) and the game wraps the job name in its own quote symbols. The earlier fix only cleaned the snapshot names. Now `NamePlateModule.StripUndrawable` keeps only what the fonts can draw for the displayed name too (`CleanTitle` uses the same filter). The Battle High gauge is not touched.

## Debug: what a game window holds (done, needs testing)
- New Debug card "What a game window holds": type the exact name of a game window and see its values (`AtkValues`) and the texts of its nodes, live (`Game/AddonDump.cs`). Made to find where the numbers of the PvP Frontline gauge come from before replacing it. In Frontline the windows seen are `PvPFrontlineGauge` (probably Battle High), `PvPFrontlineHeader` and `PvPMKSBattleLog`; the gauge itself is NOT replaced yet (names confirmed, values unknown).

## Limit Break gauge (done, needs testing)
- Entry "Limit Break (every job)" in the Job gauges page (page id 0xFFFF, not a job). Reads the game's `LimitBreakController` (CurrentUnits, BarUnits) in PvE only (`GameSnapshot.LimitBreak`; null in PvP or when the game has no bar). Up to three bars side by side, each filling from the left; full bar = another colour; optional "LB1/LB2/LB3" text. Native window `_LimitBreak` (name to confirm in Debug > Game windows) is hidden only while ours is drawn.
- LIMITATION: the game exposes the units of a bar but we do not know how many bars the content allows (1 in dungeons, 3 in 8-player content), so it is a setting (`Bars`, default 3). A later improvement would be to read it from the game. Field names of `LimitBreakController` are from memory: if the build fails there, check the ClientStructs version.
- Unlock mode shows a sample so the gauge can be placed.

## Frontline gauge (PvP) (done, needs testing)
- Found with the Debug card "What a game window holds" in a Frontline: window `PvPFrontlineGauge` has 7 values: [0]=2, [1]=3, [2]=61483 (icon id, probably the team's), [3]= the number shown on the gauge (22, then 24, then 49 earlier), [4]=100 (its maximum), [5]=1, [6]=1; texts "Knockout!", "Down...", "Boosted!", "Assist!" (labels, not used). What the number measures is not named (the owner called it the Battle High gauge).
- Replaced by "Frontline gauge (PvP)" in the Job gauges page: a bar like the Warrior's (number on it, no mark at 50). Only drawn in PvP, from values 3 and 4 (`GameSnapshot.Frontline`, `BuildFrontlineGauge`); the game's window is hidden only while ours is drawn. The window's IsVisible is NOT used for the reading (hiding it sets it to false, it would flicker). The team icon (value 2) is not drawn yet. Only tried in Frontline (Seal Rock); Crystalline Conflict or others may not have this window.
- In PvP the other job gauges stay off; only this one is drawn.

## Server info bar entry (done, needs testing)
- The game's System menu has no public API to add entries (the "Dalamud Plugins/Settings" ones are Dalamud's own), so NahjaUI uses Dalamud's standard DTR bar (the server info bar, top right) instead: `Core/ServerInfoBarEntry.cs`. Fixed title "NahjaUI" (the key for Dalamud's settings and for plugins that restyle the bar), text = game icon + "NahjaUI", tooltip explaining the clicks. Left click: open/close the window (`ToggleMainUi`). Right click: unlock mode on/off. No option of ours: the user hides or reorders it in Dalamud's settings.
- Written from memory of the API (`IDtrBar.Get`, `IDtrBarEntry.OnClick(DtrInteractionEvent)`, `MouseClickType`, `BitmapFontIcon.Mentor` as the icon): if the build fails there, check the Dalamud version and adjust. The icon can only be one of the game's font icons, not our own logo.

## Server info bar icon picker (done, needs testing)
- General page, card "Server info bar icon": a grid of the game's icons (the names that exist both in Dalamud's `SeIconChar` list, which can be drawn in a window, and in `BitmapFontIcon`, which the bar writes). Click one to use it at once; hover shows its name. Saved in `Configuration.ServerBarIcon` (default "Mentor", the crown). The icons are drawn with Dalamud's default font, which holds the game's symbols; if some show as boxes, that name has no drawable glyph.

## Long names in the tracker list (done)
- In the explorer, a name that would run over the tag at the right ("Enhancement", "3 triggers", "4 trackers") is cut with "..." (`TrackerEditor.FitText`), for trackers and groups. The full name shows in the General card when selected.

## False countdown on long statuses (done, needs testing)
- Symptom: after every loading screen, the 24 hour statuses (that never run out) showed a countdown in our status icons that went to 0 while the status stayed; the game's own status bar showed no time. Cause (hypothesis): Dalamud's `Status.RemainingTime` drops the sign of the game's value, and the game stores a negative time for statuses with no real duration. Fix: `ToStatuses` reads the raw value from the game's structure and, when it is negative, or when the status data says `IsPermanent`, gives the status no time (0 = no countdown). If it persists, the raw value of those two statuses must be looked at (Debug).

- Default icon of the server info bar entry is now "Tank" (the tank role shield), until the user picks one (`ServerBarIconChosen`; the crown "Mentor" was the first default). The owner will say whether it is the right icon and whether he likes it.

## Decision reversed (2026-11-01): game options ARE respected again
- The idea "drawing options live only in our menu" was dropped the same day. Reason: some data we read (enmity list, nameplates, etc.) may only be generated by the game while its native option is on; testing one by one what can be switched is not worth it. Pillar 8 stands: anything the game has an option for is read from the game, never repeated as an own option.
- Party list alone: keeps following `PartyListSoloOff`. Enmity list: keeps following `EnemyListDisp` and `EnemyListCastbarEnable`. No own "show alone" or "show cast" checkboxes.
- Next: party frames also follow the game's option for drawing the list bottom-to-top (copy it). The member ORDER still follows the game's party order so `<2>`/`<3>` macros keep working; only the drawing direction changes. Option name not known yet: Debug > Statuses card has a new "Search all game options" box (names from Dalamud's UiConfigOption/UiControlOption, with live values). Search "PartyList", flip the option in game, report the name.

- "Align List to Bottom" (party list, HUD layout settings) is NOT in the `_PartyList` AtkValues (0 values) nor in its texts (checked 2026-11-01, identical with the option on/off). Not a UiConfig/UiControl option either. New Debug card "Find a hidden setting": remembers the raw bytes (0xD00) of a window and lists the offsets that change while the user toggles the setting in game; bytes that change by themselves can be ignored.
- Raw compare v2: the window's own bytes did not change with the option, so the compare also covers the bytes of every node of the window (positions, sizes). The effect of 'Align List to Bottom' should show in the member nodes' Y when the party has fewer than 8 members.

## Party list "Align List to Bottom" (copied from the game)
- It is a HUD layout setting, not a game option nor a window value. Found by byte comparison of `_PartyList` (2026-11-01, 2 members, option toggled): the top-level node with id 2 has a float at offset 0x48 that goes from ~280 (anchored to bottom: pushed down by the 6 empty slots) to 0 (anchored to top); every node's screen position moves with it.
- `GameDataProvider.ReadPartyListAlignment()` reads that float each frame: `y > 0.5` = aligned to bottom (`ContextSnapshot.PartyListAlignBottom`, `PartyListNodeY`). With the whole list of 8 members both anchors look the same.
- Party frames: aligned to bottom = the rows sit at the bottom of an 8-row box (empty slots on top); member ORDER unchanged (macros `<2>`, `<3>`). Not applied to the unlock-mode sample. In unlock mode the box is 8 rows tall in that case.
- Assumed (to check in game): the Y > 0 state is "aligned to bottom". Party options show the read value so it can be checked against the game's checkbox.
- The raw-bytes compare in Debug stays as a tool for finding other hidden settings.

## Party list: start from the bottom is OURS (2026-11-01, replaces the section above)
- Decision: the party frames do NOT read the game's "Align List to Bottom" (it is a HUD layout setting, not readable as an option). We have our own HUD positioning, so the option lives there. The read of the container node was removed.
- Unlock mode: the Party frames box is always 9 rows tall (8 members + chocobo), whatever the real party size; the preview sample is a party of 8 (9 with the chocobo on).
- The unlock panel (under X and Y) now shows "Start drawing from the bottom" (`PartyFrameSettings.StartFromBottom`). On: the rows sit at the bottom of the 9-row box, empty slots on top. Member ORDER never changes (macros `<2>`, `<3>`).
- Generic hook: `LayoutElement.DrawOptions` (Action) lets any element add its own widgets to the unlock panel.
- The game's "show the list alone" option is still read from the game.

## Chocobo in the party frames follows the game (2026-11-01)
- The game has no option to show or hide the chocobo companion in its party list (checked by the user with the Debug option search), so we have none either: the chocobo bar is shown whenever the game shows it (the party frames are active and the chocobo is out), as the last row. Removed `PartyFrameSettings.ShowChocobo` and its checkbox. The unlock-mode sample is always 8 members + chocobo (9 rows), matching the 9-row box.

## Enmity list: start from the bottom, 8-row box, name ignores the dot (2026-11-01)
- Same as the party frames: unlock panel option "Start drawing from the bottom" (`EnmityListSettings.StartFromBottom`); the box in unlock mode is always 8 rows tall (the most the game's list holds), whatever the number of enemies or the samples. Rows keep their order. The box includes the place of the casts (below/sides) of the first and last slot.
- The name no longer takes the enmity dot into account: with offset 0 it starts at the left edge of the bar (before it started after the dot, or 8 px in). The name is still shortened with "..." before the percentage.

## Nameplates progress bar: own colors and texts (2026-11-01)
- The progress bar of the nameplates no longer borrows the target progress bar's colors. Own settings (`NamePlateSettings`): `ProgressBarColor`, `CastUninterruptibleColor`, `CastBackgroundColor`, `CastBorderColor` (defaults = the target progress bar's), edited in the Progress bar card with the same color controls as the Progress bars page.
- The spell name and the time left are `FrameTextSettings` (`CastName`, `CastTime`): anchor, offset X/Y, size, font and color, with the same `TextControls` as the other frames; two new cards "Progress bar: spell name" / "time left". Removed `CastTextSize` and `CastFont`. The name is shortened with "..." before the time.
- Enmity list casts still use the target progress bar's colors (not changed; to decide).
- Same for the Enmity list casts (2026-11-01): own `ProgressBarColor`, `CastUninterruptibleColor`, `CastBackgroundColor`, `CastBorderColor` in `EnmityListSettings` (defaults = target progress bar's), four color controls in the Cast card. Spell name and time left already had their own offset, font and color.

## Coherence audit, first round (2026-11-01)
Audit of the editors: elements of the same kind must offer the same options with the same controls in the same order. Fixed in this round:
- Progress bars (player/target): spell name and time left are two texts, each with the common text controls (position, offset X/Y, size, font, color) in their own card; "Time format" moved to the time card. Old `ShowName/ShowTime/TextSize/Font` are only read once (`ProgressBarSettings.EnsureTexts`).
- Job gauges (White Mage, Red Mage, Warrior, Paladin, Frontline, Limit Break): the number/timer is a `FrameTextSettings` (`Text`) with the common controls (default: centered on the bar, white). Old `TextSize/Font` read once (`EnsureText`). The White Mage timer is placed inside its pip.
- Status icon texts (frames, party, status effects): time and stack count each have position, offsets, size (combo of real font sizes; the stack size was a 4-24 slider), font and color; the stack count has its own font (`StacksFont`, starts as the old shared `Font`) and colors (`TimerColor`, `StacksColor`). One helper `UnitFrameEditor.StatusTextControls` used by both editors.
- Enmity list: border color control added (the setting existed); the offsets of the dot and of the cast are numeric fields (they were sliders).
- Nameplates: background and border of the HP bar are options (`BarBackgroundColor`, `BarBorderColor`).
Still to do (medium/minor of the audit): separate vs combined "Hide the game's X" checkboxes, time format in enmity/nameplate casts, nameplate cast side/width/offsets, ranges of width/height and cast gap, nameplate name using the common text block, labels ("Show the time" / "...left", "Empty color" / "Background color").

## Coherence audit, second round (2026-11-01)
- Time format ("Time left" / "Done over total") is now also an option of the enmity list casts and of the nameplate casts (`CastTimeMode` in their settings; same combo and labels as the progress bars).
- Width/height of every bar element accept the same range (1-4000 / 1-1000); the space between a bar and its cast is 0-40 in the enmity list and in the nameplates.
- Labels unified: "Background color" (job gauges had "Empty pip color" / "Empty color"), "Show the number on the bar" (Red Mage said "numbers on the bars"), "Position" in the nameplate name block (was "Name position"). Stale comment about a spell icon in the nameplate cast removed.
- Open for decision: separate "Hide the game's X" checkbox in the enmity list / nameplates / status effects (today Enabled does both); cast side, width and offsets for the nameplate cast like the enmity list; nameplate name using the common text block (it has the same fields but its own controls).

## Decisions of 2026-11-01 (audit, round 3)
- "Hide the game's X" is gone everywhere (unit frames, party, target/focus, progress bars, job gauges, game's statuses of the target, enmity list "keep the game's list" test aid): the master Enabled of each element both draws ours and hides the game's. The developer compares by switching the element off, or by turning it on/off in the game's own options. Removed properties `HideNative`, `HideNativeStatuses`, `ShowGameList` (old saved values are ignored).
- Nameplate cast: same options as the enmity list cast: position (under / right / left of the bar, even if it can overlap other plates), width (0 = the bar's), height, space, offset X/Y. Labels unified with the enmity list.
- The nameplate name keeps its own controls (same fields as the common text block); only the label was unified.
- Removed the test aids 'Show the game's values next to each bar' (enmity list) and 'Show the game's flags and colors next to the name' (nameplates). They can be put back if needed.
- 'Enabled' checkboxes of elements the game also draws (progress bars, job gauges, parameter bar/unit frames, enmity list, nameplates) now show a tooltip on hover: "Draws NahjaUI's version and hides the game's." (Widgets.EnabledHint). Trackers and Tooltip excluded: they hide nothing of the game.

## Statuses: two groups, always (2026-11-01)
- Decision: the game's option to split statuses into 1, 3 or 4 groups is NOT followed any more (it replaces the earlier "respect the HUD splits" rule). NahjaUI always draws two groups of the player: **Enhancements** (everything that is not an enfeeblement, including statuses of no category) and **Enfeeblements**, each with its own box (unlock mode), anchor/position, icons per row, justify, growth up, icon size/crop/rounding/spacing and the common texts (time, stacks). Removed: the single, conditional and "others" groups, the layout reading and the "in use" marks.
- Enfeeblements put on the player are the important ones (vulnerability, doom...): meant to sit prominent, like under the enhancements at the top right in WoW. They are few in this game.
- Target frame: its enfeeblements row now defaults to ALL enfeeblements (no "only the ones I applied"); small, together; anything to filter is done with trackers / future Filters. Saved configs keep their old checkbox value.
- Idea approved in principle, not started: a **Filters** page (named lists of statuses: CC, defensives, damage windows...) usable in nameplates, target/party frames and trackers; "show only these" / "hide these", mine / anyone. CC in this game is very small: tank role-action stun, melee DPS role-action stun, and Holy (Conjurer/White Mage). Starter lists must be reviewed by the user.

## Status icons: trim, order, rounding default (2026-11-01)
- Icon crop of the status icons (player groups, target/focus/party rows) is now the same control as in the trackers: "Trim icon margins" = None / Sides / Top and bottom / All around, measured per icon (`IconMargins`). The old "Icon crop (%)" slider is gone (`StatusRowSettings.Trim`, default Sides). The spacing math was already identical to the trackers (gap = round(spacing × scale)); the visible difference came from the crop (a fixed 13% left more empty margin than the trackers' per-icon trim).
- New option "Order" on every row of statuses (player groups and unit/party frame rows): Ascending (shortest time first) or Descending (longest first); a permanent status counts as the longest possible time. Default Ascending. Sorted before the "most icons" limit. Trackers are not sorted (their order is the one you set).
- Default corner rounding is 6 everywhere (frames, MP bars, progress bars, gauges, nameplates, enmity list, status icons, trackers, tooltip). Saved values are kept. The plugin window rounding (10) is not part of this.

## Round of fixes (2026-11-01, later)
- Party frames: the ring of the targeted member is drawn BEFORE the texts, so a name at the very top of the bar is never cut by it (texts are always the top layer).
- "Trim icon margins" now defaults to All around everywhere (status rows, new trackers, groups, presets). Old trackers keep what they had.
- Label unification (same concept = same words): "Space between icons (px)" also in trackers (was "Spacing (px)"); "Only the ones I applied" also in tracker triggers; "Grow towards" also in tracker groups (was "Growth"); Layout page fields now say "(px)" (Offset X/Y, Width, Height). Keep doing this when a label differs for the same concept.

## Filters (2026-11-01)
- New page **Filters**: named lists of statuses (id kept inside, renaming is safe). Starter filters, created once: **Crowd control** (the stun, status 2, if the game's name for it is "Stun"), **Defensives** and **Damage windows** (empty: the user fills them). Add statuses by name search or with one click from what you / your target have right now. The "dangerous enfeeblements" marker idea was dropped (they are few and already in a visible place).
- Used in: (1) every row of icons next to a bar (unit frames, party frames, nameplates): when "Which ones" is "Only the ones in my list or filters" / "All except my list and filters", the ticked filters add their statuses to the list; (2) tracker triggers: "Which" = One status / Filter: <name> (any status of the filter counts; if several are present the one that ends first gives the icon, time and stacks); (3) nameplates.
- **Nameplates now have three rows of status icons** (all start off): Enhancements (above the name), Enfeeblements (under the bar, "only the ones I applied") and **Crowd control** (right of the bar, filter Crowd control, from anyone). Same card/options as the unit frame rows. Only plates that draw an HP bar get them. No sample is drawn in unlock mode (needs a real enemy). Tooltip: two new checkboxes "Nameplates: enhancements/enfeeblements" (off by default).
- Profiles do not carry filters yet (profiles do not carry nameplates or status effects either).
- Reset labels: "Reset position and size" (unlock panel and Layout page), "Center the window" (window), "Reset appearance" (Appearance page and Tooltip look), "Reset role colors".
- Grow towards: tracker groups have six options (from the left, from the right, from the center, down, up, from the center vertical); frame rows have four (left, right, up, down); the player's status groups use "Row justified to the" (left/right/center) + "next rows above". Candidate to unify.

## Grow towards unified, rows attached to each other, Reset window (2026-11-01, night)
- "Reset window" (not "Center"): it also gives the menu window its natural size back (title-bar right click and Appearance page).
- **Grow towards** has the same six words everywhere: Left, Right, Up, Down, Centered horizontally, Centered vertically. Used by: tracker groups (same saved values, new words and order), the icon rows next to the bars (unit frames, party frames, nameplates) and the player's two status groups. Centered rows stay centered on their point whatever the number of icons. For the player's groups: Left starts at the right edge of the box, Right at the left edge, Up at the bottom, Down at the top; "Icons in a line" wraps into the next line (rows for a horizontal growth, columns for a vertical one) and "The next lines go above / to the left of the first" flips the side of the new lines. The old "Row justified to the" was replaced (old saves are converted: Left justify = Right growth, Right = Left, Center = Centered horizontally); note that now the first icon sits at the edge it grows from.
- **Attach to**: the enhancements and enfeeblements rows of unit frames (target, focus), party frames and nameplates can be attached to the frame/bar or to the other row (the anchor point is then a point of the other row's icon area, which moves with the number of icons). If both choose each other, the enhancements row stays on the bar. The nameplate crowd control row has no such choice.

## Party frames and unit frames cleanup (2026-11-01, night)
- The "Mouseover" checkbox of the Unit frames page is gone: the unit under the mouse is ALWAYS published as the game's mouseover target (no option).
- Party frames: "Order of the bars" is gone. The order is always the game's own party list (the macros <2>, <3>... keep working). The old "Me first" mode forced the order and could break them. Removed: role color stripe (role colors live in the HP bar color).
- The member's name ignores the job icon (as the enmity list name ignores the enmity dot): its place is only its anchor and offsets.
- The party MP bar is now a bar of its own (no longer a strip inside the HP bar): card "MP bar" with Show it for, Width (0 = same as the bar), Height, Anchor on the bar, Anchor of the MP bar, Offset X/Y (plus = up), Corner rounding, Color. Every member gets the same slot = bar + MP bar area, so the space between members is respected wherever the MP bar goes; the unlock box takes that height too.
- Not done, pending a test of the game's structure: our own right-click menu that copies the game's menu entries (see the conversation). "Clickable" stays in every Unit frames tab until then.

## Job icon anchor/offset and lost-cursor fix
- Party frame job icon now has "Anchor on the bar", "Anchor of the icon", "Offset X/Y (px)" (plus = up), same as the MP bar. Defaults reproduce the old place (left, centered).
- Dalamud only offers IContextMenu.AddMenuItem/RemoveMenuItem: it cannot read or suppress the game's native menu entries. Own menu replacement stays pending; "Clickable" stays.
- Lost cursor after right-clicking outside the game's menu: we now take the mouse from the game only for a press that STARTED over our frame (UnitFrameModule.MayCapture). A press that starts elsewhere reaches the game with its release. Untested.

## Game windows always over the interface
- Decision: ALL game windows (map, character, menus, action bars, chat, minimap...) are over our HUD; if something is in the way, the user moves our element.
- Dalamud has no z-order; ImGui is drawn after the game. So GameWindowCover reads every visible window of AllLoadedUnitsList each frame and cuts our drawing around it. No names: automatic.
- Area of a window = box around its PAINTED visible nodes (Image, NineGrid, Text, Counter; components looked into, depth 4), not its rectangle (many are big and empty). Windows covering >= 85% of the screen are ignored (fades, loading).
- Clicks: our click areas step aside only where the mouse is over a cut area (Contains), not whenever a window is open.
- Draw() now subtracts only the windows touching the element's area, to keep few regions.
- Debug page: switch "Cut the interface around every game window" (default on), "Outline the cut areas in red", count.
- Uncompiled/untested. Risks: node scale of nested components (size uses window scale * node scale), nodes of hidden parents.
- Follow-up: trackers, progress bars, job gauges, player status icons and nameplates were not cut (they did not go through GameCover.Draw). All now do. Rule for any new element drawn on the screen: draw it through plugin.GameCover.Draw(pos, size, margin, ...).
- Speech balloons over characters are never cut around (GameWindowCover.NeverCut, names guessed: Balloon/_Balloon/BalloonText/_BalloonText). The outline mode writes each window's name in red; Debug has "Copy the names of the windows cut" to find the right name if the guess is wrong.
- Cover fixes: (1) a painted node counts only inside the parents that clip (NodeFlags.Clip), so a resized window (the map) is cut at its real size, not the size of its biggest image; (2) nodes inside a hidden (Visible flag) or fully transparent (Alpha_2 = 0) parent, and windows with Alpha 0 or a hidden root, do not count (hidden hotbars, alliance lists, money/bags switched off in the HUD). Risk: if Alpha_2 turns out not to be the final alpha, nothing would be cut (the red outline shows it).
- Resized windows (map): the painted box is also cut by the frame of the window (AtkUnitBase.WindowNode, the Window component, which has the real size); the inner images keep the original size.
- Components (buttons, slots, hotbar parts) are now checked like any node: own alpha, hidden or see-through parents, and clipping parents; what is inside only counts inside that. Fixes the switched-off cross hotbar still cutting.
- Diagnostic: "Copy the names of the windows cut" now gives per window: alpha, root alpha, ShowHideFlags, VisibilityFlags, depth layer, painted nodes, max node alpha, size. Used to find what tells the game a HUD part is switched off (hotbars, alliance list, bags, money, exp, main menu still cut when off).
- Found with the diagnostic: the HUD parts that are switched off (_ActionCross, _AllianceList1/2, _Money, _BagWidget, _Exp, ScenarioTree, _MainCommand) have VisibilityFlags bit 0 set; the active ones have 0. Windows with that bit are not cut around. Windows with root alpha < 32 (the inactive chat tabs, alpha 8) are skipped. The box is also cut by the root node (the minimap _NaviMap had a 1536x1536 box).
- The switch "Game windows over the interface" (default on) moved to General > Game interface (the final product has no Debug page for users). Debug keeps only the red outline, the names and the counter.
- Unlock mode only offers what is in use: Frontline gauge only in PvP; job gauges, limit break, enmity list only in PvE; trackers follow their "where" (PvE / PvP) as well as the job.
- Party frames card title is just "Party frames": the list can start from the bottom, so "the others follow downwards" was wrong.
- Any element can be attached to another (Layout page > element > "Attach to"; default Screen). ElementLayout.AttachTo = id of the other element; the "Anchor" is then a point of that element (LayoutManager.GetRect, cycles cut at depth 8). So the debuffs go under the target bar with X = 0. Tracker-group members (managed) cannot be targets.
- Draw order: the nameplates are drawn first, so they stay under every frame of ours (Plugin.Draw).
- Layout page: the element selected there is drawn with sample values (progress bars, enmity list, status groups, party frames, unit frames), so it can be placed without casting/combat. LayoutManager.IsPreviewing(id); disabled elements are not shown.
- Layout page preview also shows the elements the selected one is attached to (the whole chain).
- Layout page no longer has Width/Height: the size of every element is chosen in its own page (trackers, unit frames, progress bars, gauges, enmity list). Tracker groups now take the size of one icon (Trackers page) as their layout box, so a group attached to a bar has no extra gap. "Reset position" (Layout page and unlock panel) does not touch the size.
- The "Attach to" list only offers the elements in use (not the tracker groups of other jobs, not disabled ones), same as the list of the Layout page.
- Streamer mode (Appearance > Game interface; off by default): player character names become "Player 1", "Player 2"... (same person, same alias, while the plugin is loaded) in every frame, party, nameplate (the game's own name/title texts are skipped for them) and the "Applied by" tooltip. Done at the source (GameDataProvider snapshots) through Game/StreamerMode.cs. NOT covered: the game's own windows (chat, its native lists, target info, other plugins) and our Debug page; so it is not a guarantee of anonymity.
- Limit Break gauge: shown only when the game gives a bar count (LimitBreakController.BarCount > 0). It was shown alone with no party. Hypothesis, to be checked with the Debug card "Limit Break (what the game says)" (BarCount, units, state of the game's _LimitBreak window).
- General page redesigned as the presentation of the addon: welcome (version, what it is), getting started (4 steps + Start unlock mode button), how it works (4 principles), what is inside (clickable list of pages with their subtitles), commands, server bar icon. Everything stated there is true today; update it when features change. No links/credits yet (to decide before publishing).
- Crafters and gatherers (no role) get their own grey in the frames (Appearance > Role colors, "Crafters and gatherers"), not the friendly green, which looked like a healer. Removed the old mention of the party stripe from that card.
- Server info bar (DTR) entry can be hidden: General > "Server info bar" card (same card as its icon), Configuration.ShowInServerBar, default on. Put there and not in Appearance because it is the same entry as the icon choice.
- Server info bar: the icon picker card was removed (the icon is fixed: tank role). The show/hide option moved to Appearance > "Server info bar" card. Config keys ServerBarIcon/ServerBarIconChosen stay only for old saves (unused).
- Party frames: the visible space between two members is always the same. Each member's slot is its bar alone, or bar + MP bar if it shows MP, and the rows are stacked one after another (the box keeps its size: 9 slots of the biggest extent; "Start from the bottom" puts the stack at the bottom of it). Before, all slots had the MP extent, so members without MP looked farther apart.
- General page: the "How NahjaUI works" card was removed.

## Dark Knight gauge (done, needs testing)
- `DRKGauge` (Blood 0-100, DarksideTimeRemaining in ms, HasDarkArts), class job 32, native addon `JobHudDRK0` (name is a GUESS: verify in Debug > Game windows; there may be a second `JobHudDRK1` that stays visible). Snapshot `DarkKnightGaugeSnapshot(Blood, DarksideMs, DarkArts)`, `GameSnapshot.DarkKnight`.
- Two layout elements: Blood bar (shared `DrawBar`, mark at 50: Bloodspiller and Living Shadow cost 50) and Darkside bar (empties, full = 60 s, seconds written on it). Dark Arts = frame around the Darkside bar. Settings `DarkKnightGaugeSettings : BarGaugeSettings`; editor entry "Dark Knight" (job 32). Delirium is a status and belongs to trackers.
- Fixed missing braces after `if (...)` before `EnsureText()/DrawCenteredText` in `DrawBar`, `DrawMana` and `DrawLimitBreak`: the number/level was drawn even when the option was off (and "LB0" with no full bar).

## Job setups: the owner's philosophy (read from his Warrior config, 2026-11-02)
- One column centred under the character, each row STUCK (attach) 6 px on top of the one below it (self Bottom on the other's Top, saved OffsetY -6). From the character down: Rotation, job gauge, CDs, Defensive CDs, Tank Role (tanks only), player progress bar. Only the progress bar has a fixed position (per job, centre +320, 240x18); moving it moves everything.
- Same measures everywhere: bars 240x6 (gauge, MP, Limit Break), icons 32x24, corner 6, trim All around, rows grow from the centre, spacing 3 (Rotation 6), gauge number size 9.
- Each row answers one question: Rotation = what to press now (procs with glow + time; combo steps shown only while the game highlights them, no text; the buff to keep up always visible, red and dimmed when missing). CDs = damage cooldowns. Defensive CDs = own defensives. Tank Role = ONE shared list for the four tanks (Shirk, Arm's Length, Reprisal, Rampart, Provoke, Interject, Low Blow; glow when the game highlights).
- An ability with an effect shows the effect time while it is on, otherwise its cooldown (dimmed while recharging; abilities with charges dim only when no charge is left). Time centre size 12, charges/stacks bottom right size 9. Cooldown sweep on in CD rows, off in Rotation. Minimalism: only what changes a decision in combat. Sprint is NOT in the column (group "General", left of the player frame, grows left).
- Implemented as `Modules/Trackers/JobSetups.cs` (Trackers > Presets: Paladin, Warrior, Dark Knight, Gunbreaker) on top of `TrackerPresets.Builder` (`Row`, `Proc`, `Step`, `Upkeep`, `Cd`). Names are looked up in the game data; unknown names are reported. Gauge elements are attached by the preset (bottom first). Jobs with MP (healers, casters): MP bar between gauge and CDs is proposed, not confirmed by the owner. Next: healers, melee, ranged, casters; rework WHM/PLD/RDM to this pattern.
- Gunbreaker gauge: `GNBGauge.Ammo` (Cartridges 0-3), class job 37, native addon `JobHudGNB0` (guess), three pips 240x6 (`GunbreakerGaugeSettings`). All new bar gauges now default to 240x6 and text size 9.

## Job setups: column anchored at the TOP (supersedes the bottom-anchored description above)
- The owner's rule: the top row of every job's column (normally Rotation) sits at a fixed height (`JobSetups.TopY` = 179 px under the middle of the screen = where the Warrior's Rotation row is, computed from his chain: progress bar y 320, h 18, rows of 24, gaps of 6, gauge h 6), and everything else hangs 6 px UNDER the row above (`Builder.AttachBelow`; ScreenAnchor Bottom, SelfAnchor Top, OffsetY +6).
- Tanks (progress bar rarely used): Rotation, gauge(s), CDs, Defensive CDs, Tank Role, progress bar at the bottom.
- Jobs that cast a lot (healers, casters): Rotation, gauge(s), MP bar (240x6, under the gauge, confirmed), progress bar (240x18), then CDs and Defensive CDs start under the progress bar.
- Shared elements (progress bar, MP bar, Tank Role row) get their own position per job (`PlaceForJob`, uses JobLayouts). The Warrior's own saved setup of the owner is still the old bottom-anchored chain (progress bar fixed); the Warrior preset builds the new one.
- Stacks/charges texts are ALWAYS bottom right, size 9, no offset: new default in the editor and in presets, and a one-time migration (`Configuration.StackTextsNormalized`, `TrackerEngine.NormalizeStackTexts`) fixed the old ones (15 at size 8 with offset 3,6, 6 at size 12).
- White Mage preset (job 24) REPLACES the old groups "WHM Rotation" and "WHM Healing CDs": Rotation (Dia on target, Freecure, Sacred Sight), CDs (Presence of Mind, Thin Air, Assize), Defensive CDs (the old list, now with effect times). Gauge: Lily on top of Blood Lily.

## Job setups: every ability with a cooldown is tracked (owner's rule, 2026-11-02)
- Philosophy: ABSOLUTELY every ability that has a cooldown has a tracker (the owner's Warrior has all of them), so the hotbars can be hidden. Sprint is in "General".
- `JobSetups.Apply` now (1) builds the lists written by hand, (2) reads the game's data (`GameNames.AbilitiesOfJob(job, 100)`: recast longer than a global cooldown) and ADDS to the CDs row any ability the lists missed (reported in the preset message), (3) adds a shared role row: "Tank Role" (PLD WAR DRK GNB) or "Healer Role" (WHM SCH AST SGE: Swiftcast, Lucid Dreaming, Surecast, Rescue). Roles still to do: melee, physical ranged, casters.
- Pressing a preset for a job whose groups already exist only puts those groups in the new column (nothing else is touched): this migrates the owner's hand-made Warrior. The White Mage rebuild only happens if the old group "WHM Healing CDs" exists.
- `TopY` is 180.
- Abilities that share a cooldown (same game recast group, e.g. Upheaval/Orogeny) get ONE tracker: the tracker only says whether it can be used. The safety net of `JobSetups` skips an ability whose `ActionManager.GetRecastGroup` is already tracked by a listed one.
- Shared cooldowns, which ability is the tracker of (owner's rule): the one learned at the LOWEST level, so the cooldown is tracked from that level on (`TrackerPresets.Builder.Cd` keeps only the lowest-level ability of a list; the tracker takes its name; the effect statuses of all versions are kept). An ability of the list with a cooldown of its own (e.g. Salt and Darkness next to Salted Earth) is left out of that tracker and added by the safety net as its own tracker.
- Order inside a tracker whose ability has several versions (owner's Berserk example): first the effect of the lowest-level version (Berserk buff), then the effect of the upgraded one (Inner Release buff), and last the cooldown of the lowest-level ability. The cooldown is automatic across levels (the game adjusts the ability), but the two effects are different statuses, so both are tracked and the tracker works at any level. The lists of `JobSetups` are written in that order (old to new).

## HP colour of players (2026-11-02)
- Owner found the target frame green when the target is a PC (a character played by a person). Decision: a PC is ALWAYS coloured by role (tank, healer, damage, no role grey) on every frame (player, target, target of target, focus, party); the "HP color" combo (Fixed / Role) is gone from the editor. The friendly colour stays only for friendly NPCs ("Friendly NPC HP color", not shown on the player frame). `UnitFrameSettings.HpColorMode` is kept only so old saves load.
- A stacks/charges text only exists where the game can give them (owner's rule: Primal Rend Ready and Primal Ruination Ready never stack, so they must not have one). Presets add it only for abilities with charges (max charges at level 100 via `ActionManager.GetMaxCharges(id, 100)`, traits count) and for statuses that stack. One-time cleanup `TrackerEngine.RemoveUselessStackTexts` (`Configuration.UselessStackTextsRemoved`) removes the OwnStacks texts of rules whose status has `MaxStacks <= 1` or whose ability has one charge at most. Texts whose source is another status/ability explicitly chosen are never touched.

## DK Darkside window and list audit against the official guides
- The game's Darkside gauge (Darkside time, Dark Arts, Living Shadow time) is the separate window `JobHudDRK1` (Blood is `JobHudDRK0`). It is hidden while our DK gauge is enabled and "Show the Darkside bar" is on.
- Living Shadow seconds are drawn on the right of our Darkside bar (own text settings, can be switched off).
- Presets corrected from the official job guides (not from memory): Plunge -> Shadowstride; Rough Divide -> Trajectory; Carve and Spit shares the cooldown with Abyssal Drain (tracker built on Abyssal Drain); new steps/procs/abilities added for DRK, GNB, PLD, WHM (Cover, Intervention, Blade of Honor, Goring Blade, Aetherial Shift, Divine Grace...). Gnashing Fang, Thin Air and Tetragrammaton marked as charges.
- Rule: every list is checked against https://na.finalfantasyxiv.com/jobguide/<job>/ before it is written.

## Preset message
- The message after applying a preset is short: setup applied + the progress bar (and MP bar) moved too. No mention of the Warrior or the column height.

## Right-click menu on groups
- Groups have the same menu as trackers: Disable/Enable (the whole group stops showing and watching; the row says "Off"), Duplicate (the group with a copy of every tracker, right below, same place on screen) and Delete (the group AND its trackers; the delete button of the group editor does the same).

## Target progress bar colors
- (Also the Enmity list and nameplate casts.) The target progress bar is the same color as the player's (violet) while the cast cannot be interrupted; it only changes (red) when you CAN interrupt it. The old grey default is replaced by the player's color once on load. Both colors stay editable.

## Dark Knight MP bar
- Dark Knight is the only tank that also tracks MP. Its preset turns the MP bar on for DRK and puts it under the gauges (Blood, Darkside); the progress bar stays at the bottom like the other tanks. Column: Rotation, Blood, Darkside, MP bar, CDs, Defensive CDs, Tank Role, progress bar.

## Selected nameplate on top
- The targeted nameplate is drawn last (over the rest). A plate whose area (bar + the name above it) touches the target's area is not drawn at all, so two names never overlap.

## Chocobo frame colour
- The chocobo companion's HP bar is always Sunset Orange (RGB 183, 92, 45, from the FFXIV wiki "Chocobo Colors"), in honour of Ember. Fixed, not an option.

## Warrior PvP preset ("Warrior (PvP)" in Presets)
- Same philosophy and column as PvE, but its trackers load only in PvP (Load.Where = PvpOnly) and look up the PvP version of every action (IsPvP rows) and status (highest id of that name).
- Rows: WAR PvP Rotation (steps Maim, Storm's Path; procs Primal Ruination Ready, Inner Chaos Ready, Chaotic Cyclone Ready, Wrathful, Burgeoning Fury with stacks, Inner Release), WAR PvP CDs (Primal Rend, Onslaught, Orogeny, Blota, Bloodwhetting with its 10 s effect), WAR PvP Role (Rampart, Rampage, Full Swing, Guard, Purify, Recuperate, Standard-issue Elixir).
- Source: FFXIV wiki list of Warrior actions + Icy Veins (the official guide page is cut off after the first three PvP actions). The status names are NOT verified in the game: the report lists what was not found.
- No gauge, MP or progress bar in PvP (the Warrior has none there).

## Explorer filter: PvE / PvP
- Next to the job filter there is a second combo: "Here (PvE/PvP)" (default: the kind of content you are in), PvE, PvP, or PvE and PvP. A tracker passes when it loads there (PveOnly / PvpOnly / Everywhere). Both filters work together.
- If the two filters do not fit side by side in the explorer, the PvE/PvP one goes on the next line (never cut).

## Colors of Disciples of the Hand / Land
- Read from the official guides (computed CSS of the headings): Tank rgb(45,58,128), Healer rgb(52,102,36), DPS rgb(115,40,40) on the battle guide; Disciples of the Hand rgb(78,50,143) (purple) and of the Land rgb(143,120,50) (gold) on the crafting and gathering guide.
- Frames use the same hue and saturation as those but brighter (like our tank/healer/damage colors are the brighter version of the game's): Hand (0.48, 0.31, 0.88), Land (0.88, 0.74, 0.31). Editable in Appearance > Role colors ("Disciples of the Hand", "Disciples of the Land"). The old grey stays only for unknown jobs.

## The chocobo frame follows the chocobo's colour
- The frame takes the colour the chocobo has now: dye id of the companion (`UIState.Buddy.CompanionInfo.CurrentColorStainId`) -> `Stain` sheet colour. The wiki page is not needed (same data). Dark coats are scaled up until their brightest channel is 0.62 (same hue and saturation) so the bar is visible; Sunset Orange is the fallback while it is unknown.
- Hand/Land role colors are NOT the exact guide colors: same hue and saturation, brighter (the guide's are dark header backgrounds).

## Streamer mode names (in progress)
- Names of other players are invented from the name lists of the game's character creation (sheet `CharaMakeName`, one column per kind of name). Columns by race (from the owner's dump, see `Styles` in StreamerMode.cs): Hyur, Elezen, Roegadyn and Raen are certain; Lalafell (syllables), both Miqo'te clans, Xaela, Hrothgar and Viera are guesses from the shape of the data, judged with the per-race samples in the Debug card. The style and the names come from a stable hash of the real name. Not found -> "Player N".
- The structure of the sheet is NOT verified. Debug page has a card "Streamer mode names": how many parts were found, 12 sample names, and a button that dumps the first 40 rows (copy). Next step: use the owner's report to split first/family names per race and gender if the sheet allows it.

## Decisions for publishing (owner)
- Before publishing, once everything is migrated: DELETE every one-time migration (MigrateRules, MigrateEffects, MigrateManaLists, MigrateParameterBar, NormalizeStackTexts, RemoveUselessStackTexts, the old fields kept for them...). No dead code in the addon. Losing the owner's current configuration is fine. Add a configuration version number for future changes.
- Before publishing, EVERY job must have its preset (column setup: Rotation, gauges, CDs, Defensive CDs, role row, progress bar) so the interface works as soon as it is installed, without configuring anything. Remaining: Scholar, Astrologian, Sage, all melee, physical ranged, casters (and PvP presets for the jobs).

## Status rows: up to 30 icons, several lines, two selectors (all frames)
- The game holds at most 30 statuses on a unit, so "Most icons shown" goes up to 30 (= all of them). "Icons per line" (default 6, 1 to 30): a full line is followed by the next one.
- Two selectors, the same in every place that shows enhancements or enfeeblements (unit frames, party frames, nameplates and the Status effects page): "Grow towards" (the six words: Left, Right, Up, Down, Centered horizontally, Centered vertically) and "Next line goes" (only the two sides that fit: Down/Up for a horizontal line, Right/Left for a vertical one). A centered line is centered on the point of the row, the last (shorter) line too. The area of the row counts every line.
- The settings live in `StatusRowSettings` (`Growth`, `NextLine`, `IconsPerLine`); the Status effects groups use their `Look` for them (the old Columns, Justify, Growth and RowsGrowUp are gone).

## Standard job column (all jobs)
- Top of the column: Rotation row at 180 px below the screen centre. 6 px under it: all job gauges, then the MP bar (only if that job shows the MP bar), then the progress bar, all 1 px apart (`BarGap` in JobSetups.cs). 6 px under the progress bar: CDs, then Defensive CDs, then the role row and any other group, each 6 px apart.
- Same order for tanks, healers and casters (no more "progress bar at the bottom" for tanks). Dark Knight forces its MP bar on.

## DoT presets and the healers
- A DoT that is replaced as the job levels up is ONE tracker named like the version of the highest level (Dia, Biolysis, Combust III, Eukrasian Dosis III); its triggers are all the versions, newest first, and the icon is the one of the status that is on the target. Nothing automatic: whoever builds their own tracker puts the names by hand.
- Presets added: Scholar, Astrologian, Sage (and the White Mage DoT now has Dia, Aero II and Aero). They have no gauge yet (Aetherflow/Faerie, Cards, Addersgall/Addersting still to do) and use the Healer Role row. The names that do not exist in the game data are listed in the report after pressing the preset.

## Pages of the gauges
- "Job gauges" is only for jobs (the Job selector no longer lists Limit Break or Frontline). Limit Break and Frontline gauge have their own pages, right after it.

## Limit Break bars
- The number of bars is what the game's own window keeps visible: one component node per bar in `_LimitBreak` (3 in Windurst, 2 in Occult Crescent: the third node is off). It works with the game's window hidden. `LimitBreakController.BarCount` is only ceil(units / BarUnits) and is the fallback. The "Bars" slider is gone for good.

## Party nameplate role color
- The game's option "Apply colors according to role" (Character Configuration > General) is not followed yet: our party plates keep the color of the Others > Party Members setting. Debug has a new card "Options that changed" (remember all options, change the setting in the game, see which option changed) to find the option behind it.

## Party members who are away or dead
- A member who is dead, offline, in another area or too far away is faded (one opacity, 50 % by default) and his name is replaced by "Dead", "Offline", "Other area" or "Out of range" (one switch). The old 45 % black over a dead member is gone.
- How it is told: no character next to you = away; his territory is 0 = offline (a guess, to check when someone disconnects); his territory is not yours = other area; otherwise = out of range. Dead is read from the game. The rows of statuses next to the bar are not faded.
- Viewing a cutscene (the game's online status "Viewing Cutscene"; its number is read once from the English table) gets the same fade and the tag "Viewing cutscene": it shows who still cannot start the duty.

## Languages: never compare or search by a text of the game
- People play in several languages. NEVER find or compare something by a name or text that comes from the game's tables in the language of the client ("Sprint", "BST", "Viewing Cutscene"...): it works in English only.
- What to do instead: compare ids (numbers). When the id of something is only known by its English name (the names of the presets, a job abbreviation, an online status), read it once from the ENGLISH table (`GetExcelSheet<T>(Dalamud.Game.ClientLanguage.English)`) and keep the id. Names in the language of the game are only for showing, and for what the user types to search.
- Fixed in this pass: the presets (statuses and actions looked up in the English table), the Beastmaster job id (English table), Sprint (by its id, `GameNames.SprintId`) and the cutscene status (English table). Checked and fine: the addon names of the game (they are internal ids), the texts we write ourselves, the streamer names (only displayed).

## Tracker philosophy (guidelines for every preset from now on)
- Debuffs that are ours on the enemy and mandatory in the rotation (DoTs, the Bard's poisons): always shown with their time (and stacks if they have), red and dimmed while missing. A debuff that changes name with the level (Aero, Aero II, Dia) is ONE tracker named like the newest, with all the names as triggers. `Builder.Debuff`.
- Buffs that must be kept up all the time (Surging Tempest): the same logic on yourself. `Builder.Upkeep`.
- A cooldown that gives a buff shows the buff (all its names, if it changes with the level) and, when the buff is off, the cooldown (named after the ability learned at the lowest level, which covers the rest). Fight or Flight tracks only its buff and its cooldown; Goring Blade is its own thing.
- Abilities that open when another cooldown is used and only last a while (Goring Blade after Fight or Flight, Confiteor after Requiescat...) matter for the rotation: they go in the Rotation group, to be confirmed with the Balance guide (Study the guide of each job).
- Every ability that glows in the game glows in ours by default (`Builder.Cd` has the `glow` switch, true by default, so the owner can switch it off for a preset; the end user can always change it in the tracker editor).
- Every ability with a cooldown is tracked (the safety net of the preset adds what is missing).
- Movement abilities WITHOUT damage (Shadowstride, Aetherial Shift) go in the group "General" next to Sprint, loaded only for the job that has them. Movement abilities WITH damage (Onslaught, Intervene, Trajectory) stay in the CDs. The preset creates "General" (left of the player frame, growing left) if there is none.
- How the multi-name debuff/upkeep is built: one rule per name while it is on (so the icon is the one that is there) and a last rule, red, while none is on (icon of the newest). With no target a debuff tracker is hidden (the engine shows nothing when there is nobody to look at).
- NO ANCHORS between the elements of a preset: every group, gauge and bar has its own offsets from the middle of the screen (Rotation top at 180 px, then 6 px between rows, 1 px between bars), computed when the preset is applied. The user who moves one element never drags the others by surprise. `Builder.AttachBelow` is gone.
- The group "General": global abilities first (Sprint), then the movement abilities without damage in the order of the game's job list (PLD, WAR, DRK, GNB, healers, melee, physical ranged, casters, BLU). Checked in the official job guides: no damage = Shadowstride (DRK), Trajectory (GNB), Aetherial Shift (WHM), Icarus (SGE); with damage (stay in the CDs) = Intervene (PLD), Onslaught (WAR). SCH and AST have no dash. Only movement goes in General, not Swiftcast & co: the role actions have their own group per role (Tank Role, Healer Role, Melee Role, Physical Ranged Role, Magical Ranged Role).

## Healer gauges: Scholar, Astrologian, Sage

- Scholar (job 28, `SCHGauge`): Aetherflow = 3 pips, Faerie Gauge = a bar 0-100 (`gauge.sch.aetherflow`, `gauge.sch.faerie`). While the Seraph is out (`SeraphTimer` > 0, max 22 s) the bar shows her time left in its own colour.
- Astrologian (job 33, `ASTGauge`): 3 card slots coloured by Astral or Umbral Draw, plus the Crown card (Lord red, Lady green) (`gauge.ast.cards`, `gauge.ast.crown`). An empty slot = card played.
- Sage (job 40, `SGEGauge`): Addersgall = 3 pips, the next one fills over 20 s with the seconds written on it (timer assumed to count UP like the Lilies; verify in game); Addersting = 3 pips (`gauge.sge.addersgall`, `gauge.sge.addersting`).
- Native windows hidden: `JobHudSCH0`, `JobHudAST0`, `JobHudSGE0` (names are guesses from the pattern; the second windows `...1` are NOT hidden until checked in Debug > Game windows).
- Presets read from the guides: WAR procs Nascent Chaos and Wrathful; SCH Consolation (Rotation, charges), Summon Seraph, Emergency Tactics; AST procs Divining (Oracle) and Suntouched (Sun Sign) in Rotation instead of CDs, Earthly Dominance + Giant Dominance, Horoscope + Horoscope Helios, Lightspeed with charges; SGE Upkeep Kardia.

- Rule for every role group (Tank, Healer, and the next ones): an ability that gives you a buff shows that buff while it lasts, then its cooldown. Tank Role: Arm's Length and Rampart now do. Reprisal is a debuff on the enemy, Shirk, Provoke, Interject, Low Blow give nothing to you. Healer Role already follows it (Swiftcast, Lucid Dreaming, Surecast). Check this when building Melee, Physical Ranged and Magical Ranged roles.

## Streamer mode names: Final Fantasy characters (replaces the CharaMakeName idea)
- The invented names now come from two fixed lists in `Game/StreamerMode.cs`: 40 famous men and 40 famous women of the saga (FFI to FFXIV), mixed one after the other. The sheet `CharaMakeName`, its per-race columns, `Probe`, `Dump` and the Debug card "Streamer mode names" were removed (dead code). "Player N" no longer exists.
- Same real name = same alias (stable hash, also between sessions). A name already given is skipped while free ones remain; after 80 people the names repeat.

- Combo steps that the game upgrades by level (Rage of Halone into Royal Authority): ONE tracker that follows the lowest ability and is named after the newest (`Builder.Step(group, lowest, shownName)`), same rule as the DoTs. Only that pair exists among the tank steps; chains (Confiteor, Blade of Faith...) are different abilities and stay separate. Check this when building the other jobs.

- Paladin: Intervention moved from CDs to Defensives (with Cover and Sheltron; CDs keep only attacks and dashes with damage). Reviewed the other tanks: nothing else misplaced.
- Glow rule: an ability the game makes glow only because there is enough gauge/resource to spend (Sheltron, Cover, Intervention; Scholar Aetherflow spenders Sacred Soil, Excogitation, Indomitability; Sage Addersgall spenders Kerachole, Taurochole, Ixochole) is created with `glow: false`: the gauge already shows it and these are managed, not pressed at once. Apply to every job with a gauge that spends on defensives/heals. Glow stays for real procs.

- Chain buttons (one button that turns into the next step of a mini combo): ONE tracker named after the button, never one per step. Paladin: Atonement (Supplication, Sepulchre) and Confiteor (Blade of Faith, Truth, Valor); Dark Knight: Scarlet Delirium (Comeuppance, Torcleaver); Gunbreaker: the Gnashing Fang chain (Savage Claw, Wicked Talon) is already the Gnashing Fang CD tracker, and the Reign chain (Noble Blood, Lion Heart) is already the proc Ready To Reign. Blade of Honor stays (it is the Imperator button). Rule for the other jobs: look for chains before listing steps.

- Naming of CD trackers: when the game upgrades an ability by level (Spirits Within into Expiacion, Berserk into Inner Release, Sheltron into Holy Sheltron...), the tracker is ONE and is named after the newest, exactly like DoTs and combo steps. Renamed: Imperator, Expiacion, Guardian, Holy Sheltron, Inner Release, Damnation, Bloodwhetting, Shadowed Vigil, Blasting Zone, Great Nebula, Heart of Corundum, Phlegma III. Not upgrades, so unchanged: Living Dead, Salted Earth, Abyssal Drain.

- Presets menu: the preset of the job you play goes first, the rest in the game's job order (tanks, healers, Red Mage; the PvP one last). `TrackerPresets.NameForJob`.
- BUG fixed: `Builder.Cd` renamed the tracker to the lowest-level ability (the old rule), which cancelled the naming rule "newest version". It no longer renames.
- `FindAction` has a last resort (a name match with no job and not flagged as player action) for follow-up actions such as Blade of Honor, which came out with no ability selected.

## Order of the trackers inside a group (owner's standard)
- CDs: from the longest cooldown to the shortest.
- Defensives: same, but by kind: the big one (Hallowed Ground, Holmgang, Living Dead, Superbolide), then the ones that spend the job's resource (Paladin Oath Gauge: Cover, Intervention, Holy Sheltron; Dark Knight MP: The Blackest Night), then the ones that also cover the party, then the individual ones.
- Role groups: the odd one first (Tank: Shirk), then the defensive ones, then the crowd control (Interject, Low Blow), each from longest to shortest.
- Rotation group: a priority system. Read the guide of The Balance and put what has the highest priority on top, descending. Paladin: Blade of Honor, Confiteor, Goring Blade, Atonement, Divine Might, Royal Authority, Riot Blade (read from the guide; the owner's own order was the same).
- Applied to the four tanks and the Tank Role. Healers (and Healer Role) still to do. Cooldown lengths are from memory: the owner corrects the order if one is wrong.

- Warrior Rotation order (The Balance basic guide): Wrathful (Primal Wrath), Primal Rend Ready, Primal Ruination Ready, Surging Tempest (upkeep), Nascent Chaos (Inner Chaos), then the combo (Maim, Storm's Path, Mythril Tempest). Upheaval (priority 4 in the guide) is a CD, not in this group.

- BUG fixed (Inner Release appeared twice): `Builder.Cd` followed only the lowest-level ability of a list (Berserk), assuming that the versions share their cooldown. Berserk and Inner Release do NOT (different recast group), so the tracker followed a dead cooldown and the safety net added Inner Release as a second tracker. Now `Cd` makes one rule per version, newest first (an unlearned ability is skipped), and tracks them all. A list given to `Cd` must only contain versions of the SAME ability: Abyssal Drain / Carve and Spit and Salt and Darkness / Salted Earth are different buttons and are no longer mixed.
- Warrior CDs order: Infuriate first (tied to the Beast Gauge, like a separate option), Inner Release, Upheaval, Onslaught. Rule: an ability tied to the job's resource goes first in its group, then the rest by cooldown.

- Warrior: Nascent Chaos (Inner Chaos) is NOT tracked: it only changes Fell Cleave and Decimate, which are pressed anyway because Infuriate is used precisely to spend the gauge. Rule: do not track a proc that only transforms a button you already press for another reason.

- Dark Knight (The Balance basic guide): Rotation = Scorn (Disesteem), Scarlet Delirium (one tracker for the chain Comeuppance, Torcleaver; Impalement is the AoE form of the same button and is not tracked), then the combo (Syphon Strike, Souleater, Stalwart Soul). Bloodspiller/Quietus and Edge/Flood of Shadow are not tracked (no glow of their own; the gauge shows it). Carve and Spit and Abyssal Drain are the SAME button (AoE form), so they are one tracker named Carve and Spit (undoes the split of the previous pass). CDs: Delirium and Blood Weapon first (Blood gauge), then Living Shadow, Salted Earth, Carve and Spit, Shadowbringer. Salt and Darkness has its own cooldown: the safety net adds it.

- Combo steps go in REVERSE order inside Rotation (the last step first, like Royal Authority before Riot Blade): Warrior Storm's Path, Maim, Mythril Tempest; Dark Knight Souleater, Syphon Strike, Stalwart Soul. The AoE step goes after the single-target ones. Applies to every job.

- Gunbreaker Rotation (The Balance basic guide, which does not describe the Ready To procs; order decided from it): Continuation procs first (Ready To Gouge, Tear, Rip, Blast, Raze), then Ready To Reign, Ready To Break; then the combo in reverse (Solid Barrel, Brutal Shell, Demon Slaughter). CDs keep Bloodfest first (it fills the cartridges), then No Mercy, Double Down, Bow Shock, Gnashing Fang, Blasting Zone.

- Paladin: the AoE combo is Total Eclipse then Prominence; only Prominence (the second step) is tracked, after Riot Blade. Same idea for every combo: the first step has no glow, so no tracker.

## Healers: order and content (The Balance basic guides, 7.x)
- All four healers follow the owner's order: Rotation = DoT always up (red when missing), then procs that other cooldowns give and run out, in priority order. CDs = the ones tied to the resource first, then longest to shortest. Defensives = the emergency one, then the ones that spend the job's resource (no glow), then the ones that cover the party, then the individual ones. Healer Role: Rescue, Surecast, Lucid Dreaming, Swiftcast.
- Not tracked (they only transform a button that is pressed anyway): WHM Sacred Sight (Glare IV; Presence of Mind shows the time), SGE Eukrasia (the DoT tracker shows when to renew it).
- WHM: Dia, Divine Grace (Divine Caress after Temperance), Freecure. SCH: Biolysis, Impact Imminent, Consolation (only with the Seraph). AST: Combust, Divining (Oracle), Suntouched (Sun Sign); Earthly Star moved to Defensives; Essential Dignity and Celestial Intersection added with charges. SGE: Eukrasian Dosis, Kardia; Zoe, Soteria, Krasis in Defensives.
- The Balance guides do not give an exact priority list for healers, so the order of the procs is decided from their text. Cooldown lengths are from memory.


## Decision: presets for all the remaining jobs (allclasses1)
- Every job except Blue Mage has a preset now: MNK, DRG, NIN, SAM, RPR, VPR (melee), BRD, MCH, DNC (physical ranged), BLM, SMN, RDM, PCT (magical ranged). Blue Mage is a limited job (its spells depend on the player's spellbook) and Beastmaster does not exist as a job here: skipped.
- Same standards as the tanks and healers: CDs longest to shortest with the ones tied to the gauge first, defensives (party-wide first, then individual), rotation by The Balance priority (DoT and upkeep buffs first, procs, then the combo finishers: single target before AoE, last step first).
- Gauges of these jobs are NOT made yet (empty gauge row), except Red Mage, which keeps its White Mana / Black Mana / stack bars.
- Red Mage was moved to the same system (column, Rotation / CDs / Defensive CDs). The old three-group preset (RDM Procs / Cooldowns / Utility) and its builder helpers (NewGroup, AbilityTracker, SharedAbilityTracker) were deleted. Dualcast is no longer tracked (it only makes the next spell instant). Displacement is not a separate tracker: it shares its charges with Engagement.
- New role rows: "Melee Role" (True North, Arm's Length, Second Wind, Feint, Bloodbath, Leg Sweep), "Physical Ranged Role" (Peloton, Second Wind, Foot Graze, Leg Graze, Head Graze), "Magical Ranged Role" (Surecast, Addle, Lucid Dreaming, Swiftcast). The role code is now a table (Roles) instead of the tank/healer if.
- Movement without damage goes to "General" after Sprint, in the job-list order: MNK Thunderclap, DRG Winged Glide + Elusive Jump, NIN Shukuchi, RPR Hell's Ingress + Hell's Egress, VPR Slither (damage not confirmed: the wiki only says "rush to the target"), BRD Repelling Shot, DNC En Avant, BLM Aetherial Manipulation, PCT Smudge. Movement with damage stays in CDs (SAM Hissatsu: Gyoten, RDM Corps-a-corps / Engagement). A job can have several movements now (Moves list). The movements are tracked before the safety net so they are not added twice to the CDs.
- A job without defensive abilities (DRG, VPR) gets no empty "Defensive CDs" row.
- Cd now also reads the game's data: an ability that stores several charges shows them even if the preset did not say so.
- The three songs of the Bard are ONE tracker (they share their cooldown): it shows the song that is playing (Wanderer's Minuet, Mage's Ballad or Army's Paeon) and then the cooldown.
- Unverified (names and cooldowns from the guides): Earth's Rumination / Earth's Resolve (MNK), Shadow Walker (NIN), Full Metal Machinist (MCH), Thorned Flourish (RDM), Striking Muse vs Steel Muse (PCT), Crest of Time Borrowed (RPR). Anything the game does not find is listed in the "Not found" report when the preset is applied.

## Decision: order of the groups in the list of trackers (allclasses2)
- A preset puts its groups in the order of the game's job list: tanks, healers, melee, physical ranged, magical ranged; each job with Rotation, CDs, Defensive CDs; the role row right after the last job of its role (Tank Role after GNB, Healer Role after SGE...); "General" (Sprint, for every job) always last. The user's own groups are not touched and the user can move everything afterwards: this only decides where an imported preset puts what it creates (RankOf / PlaceInOrder in JobSetups).
- Fix: CS0104 'Status' ambiguous in TrackerPresets (a using of ClientStructs was added by mistake): the call is fully qualified now.

## Decision: party frame statuses use the live character list (partystatus1)
- Bug: the buff times in the party frame lagged and froze (Nascent Chaos showed 30 s while the own buffs showed 13 s). Two causes: the party list of the game only refreshes its statuses when the server sends them, and the smoothing clock (statusClock) used the same key (unit, status, source) for the player's own list and the party list entry of the same player, so the two values kept resetting each other and the stale one won.
- Fix: a party member that is in the object table is read from its own character (chara.StatusList, counted down by the game every frame); the party list is only used for members that are far away (no key collision then).

## Decision: gauges of the melee, physical ranged and caster jobs (gauges1)
- One generic gauge instead of twelve modules: MNK, DRG, NIN, SAM, RPR, VPR, BRD, MCH, DNC, BLM, SMN and PCT are a list of rows (ExtraGauges.cs: name, height, colours, marks) that the game data fills (GameDataProvider.BuildExtraGauge, one snapshot per frame: ExtraGaugeSnapshot) and one module draws (JobGaugeModule.Extra.cs). Red Mage keeps its own gauge. A row is a bar (Value/Max), pips (n filled, the next one partly filled) or slots (each one empty or in one colour). The job decides every frame how a row is drawn, so a row can change shape: Reaper Shroud becomes the Lemure Shroud while enshrouded, Viper Serpent Offering becomes the Anguine Tribute while reawakened, Machinist Heat / Battery become the Overheat / Automaton Queen time, Dancer Feathers become the steps while dancing.
- Rows (top to bottom): MNK Chakra, Beast Chakra, Nadi | DRG Life of the Dragon, Firstminds' Focus | NIN Ninki, Kazematoi | SAM Kenki, Sen, Meditation | RPR Soul, Shroud | VPR Rattling Coil, Serpent Offering | BRD Song (time, colour by song), Repertoire, Soul Voice | MCH Heat, Battery | DNC Feathers/steps, Esprit | BLM Astral Fire/Umbral Ice (stacks and seconds), Umbral Hearts, Polyglot (the next one fills), Astral Soul | SMN Aetherflow, Primals ready, Trance (attunement or demi-summon time) | PCT Palette, Paint, Motifs and portraits.
- Marks on the bars are where a spender can be used (50 for Ninki, Soul, Shroud, Heat, Battery, Esprit, Palette, Serpent Offering; 25 and 50 for Kenki; 20 and 80 for Soul Voice).
- The settings are one entry per job (JobGaugesSettings.Extra, by abbreviation): enabled, show each row, colours (only the ones the user changed are saved), gap, rounding, text, background, border and mark colour. One editor page (DrawExtra) for all, in the job list order. The job setups (Trackers > Presets) put the rows in the column under the Rotation row, like the other gauges.
- The game's own windows are hidden while the gauge is on. Names taken from ClientStructs: MNK0/1, DRG0, NIN0/1, SAM0/1, RRP0/1 (Reaper), RDB0/1 (Viper), BRD0, MCH0, DNC0/1, BLM0/1, SMN0/1, RPM0/1 (Pictomancer). Fix: the Sage windows are JobHudGFF0 and JobHudGFF1, not JobHudSGE0 (the old guess never matched, so the game's Sage gauge was never hidden).
- Unverified (cannot be tested without playing each job): the numbers of the enums that change between Dalamud versions (Beast Chakra and Nadi are read by name), Polyglot timer direction (read as counting down from 30 s while Enochian is on, as DelvUI does), Astral Fire / Umbral Ice seconds (read from statuses 165 / 166), maximum of the Overheat, Automaton Queen and demi-summon timers (the longest value seen is remembered), Radiant Finale / Coda not shown in the Bard song, Void Shroud and Blitz timer not shown.

## Decision: tank stance tracker in "General" (stance1)
- Each tank has a tracker for its enmity stance in the group "General": Iron Will (PLD), Defiance (WAR), Grit (DRK), Royal Guard (GNB). It loads only on its job, is always shown, has no text, and is red and dimmed while the stance is off. It goes first among the job's trackers in the group (before the movement ones).
- It is also added when the job was already set up before (re-applying the preset only adds this tracker if it is missing; nothing else is touched).

- Fix: CS0051 in ExtraGaugeJobSettings.ColorOf (public method with an internal parameter type): it is internal now (stance2).


## Defensives filters (defensives1)
- Four filters created once with fixed ids: Invulnerabilities, Mitigation, Shields and barriers, Sustained healing. Statuses are written by English name in `Modules/Filters/DefensiveFilters.cs` and resolved to ids from the game table; names not found are listed on the Filters page (button "Add the default statuses again" refills them).
- Mitigation also holds Feint, Addle, Reprisal, Dismantled (enemy debuffs): rows only show their own kind (enhancement/enfeeblement), so the same filter works on the party frame (never shows them) and on target/enmity/nameplate debuff rows.
- Row options: `PriorityByFilter` (filters in list order, time orders inside each filter), filter order with Up/Down, `OnlyMineFilterIds` (only mine per filter), `DispelBorder` + `DispelColor` (gold default) from Lumina `Status.CanDispel`.
- First run on the existing config: party buffs row = only the four filters, priority on, max 6 icons, sustained healing only mine; debuffs row = all, with dispel border.

## Defensives filters 2 (defensives2)
- Names shortened: "Shields", "HoTs" (renamed on existing configs). New filters: "Raise" (status Raise, first on the party buffs row) and "Movement" (Sprint, Peloton, Jog; last). `FiltersSettings.DefensivesVersion` (2) drives the one-time migration.
- Peloton: buff tracker in "General" for every job without its own Peloton (via Proc); BRD/MCH/DNC track the ability with its effect next to Sprint (Move gained an `Effect`), and it left the Physical Ranged role row. "Jog" as a status name is unverified (reported if missing).

## Dance partner (dancer1)
- New filter "Dance Partner" (every status named Closed Position: the Dancer's and the partner's ids), on the party buffs row before Movement (`DefensivesVersion` 3).
- Dancer "General" tracker "Closed Position": a Stance-style tracker (always shown, red and dimmed, no text) that uses the filter, so it warns when nobody carries the Closed Position. Stance() gained an optional filter id.

## Dance partner fix (dancer2)
- The partner's status is "Dance Partner" (the Dancer's own is "Closed Position"); the filter now holds both (`DefensivesVersion` 4 adds it to existing filters) and is "only mine" on the party row.

## Party enfeeblements without "only mine" (partyonly1)
- The "Only the ones I applied" checkbox is hidden on the party frame Enfeeblements card (nobody applies enfeeblements to friends; it would hide them all) and the setting is forced off there. It stays on Enhancements (simple "all but only mine" mode, e.g. a healer's HoTs, for people who use no filters) and on target, focus and nameplate rows. "Only mine" per filter is the fine-grained option.

## Restore default filters (filters-restore1)
- Button "Restore the default filters" on the Filters page: recreates the missing default filters (Crowd control, Invulnerabilities, Mitigation, Shields, HoTs, Raise, Dance Partner, Movement) with their original ids, so nameplates and party rows find them again; existing ones are untouched. Not locked against deleting; the empty starter filters (Defensives, Damage windows) are not restored.

## Status rows on the enmity list (enmity1)
- The enmity list has the same two rows of status icons as the other frames (Enhancements left of each bar, Enfeeblements right; both off by default, enfeeblements "only mine"), drawn with `StatusRowRenderer.DrawRows`, with the same cards (filters, priority, only-mine per filter). The preview shows sample icons. Nameplates already used the same cards and rows (plus a third "Crowd control" row).

## Nameplate rows aligned (nameplates2)
- The third "Crowd control" row of the nameplates is removed (the Crowd control filter still exists and can be ticked in Enfeeblements, with priority). Placement is now consistent by kind: single frames (player, target, focus, nameplates) = enhancements above the bar (offset -14), enfeeblements under it (offset 6); lists (party frame, enmity list) = enhancements left of the bar growing left, enfeeblements right growing right. Only defaults changed; saved settings are kept.

## Nameplate status icons only on chosen plates (nameplates3)
- The status rows were drawn on every plate with a bar (also the player's own). Now a card "Status icons: on which plates" has Enemies (default on), Me, Party members, Other players, Friendly (default off). One set of rows for all of them: a separate set per kind (WoW style) was rejected as four copies of every option; it can be added later if someone needs different filters per kind.

## Profiles complete + update safety (profiles2)
- Export/import already existed (Profiles page, code "NUI1:", automatic backup before each import). Gap found: Filters, Nameplates, Enmity list, Status effects and Tooltip were not in it, so a full backup lost them and party rows pointed to filters that did not exist elsewhere. Added as blocks (format version stays 1: older codes just lack them).
- `ConfigSafety.BeforeUpdate`: when the plugin version changes (or the saved format is older/newer than `Configuration.CurrentVersion`) a plain copy of NahjaUI.json goes to `backups/before-update` (last 5), listed on the Profiles page. `ConfigMigrations.Run`: one step per version number (CurrentVersion 2; step 1->2 is only the starting point). `Configuration.LastPluginVersion` stores the version that saved the file. Small repeatable migrations keep using flags.

## Ideas rejected (do not propose again)
- Incoming-heal bar on the party frame: the game gives no such data (as far as known; could not be checked in the client structs from the sandbox), and an estimate would be invented numbers. Rejected.
- Extra shield work on the party frame: it already draws the game's own shield percentage, exact. Nothing to add.
- Enmity-lost warning for tanks: the enmity dot of the list already says whether an enemy attacks you; a warning would be redundant noise (a provoke fixes enmity at once). Rejected.

## Stance warnings only when missing (stance3)
- The tank stance trackers (Iron Will, Defiance, Grit, Royal Guard) and the Dancer Closed Position warning are now shown ONLY while the status is missing (ConditionKind.StatusMissing, red and dimmed always, no text). They used to be StatusAlways (always on screen). Config version 3: step 2->3 converts the existing ones. All of them go through `Builder.Stance`, so the other tanks are the same code.

## No Esuna border on enemies (dispel2)
- The Esuna border is drawn only on friendly units: the row renderer got a `friendly` flag (party and player = true, unit frames use !IsHostile, nameplates use Relation != Enemy, enmity list = false). The enmity list card no longer offers the option.

## Cleanup: repeated "this hides the game's UI" notes (cleanup1)
- Removed the "Enabled" tooltips (`Widgets.EnabledHint`, 12 uses) and the On/Off text under the player's Enabled in Unit frames. The Getting started card already says the game's element is hidden only while ours is on. Kept: the progress bar note about the target info setting (a real fix, not the repeated warning) and the target-of-target note.

## Cleanup corrected (cleanup2)
- The "Enabled" tooltip (`Widgets.EnabledHint`) is KEPT (it takes no space and appears when you are about to switch something on). Only the menu texts were removed. The tooltip takes an optional extra line: the target progress bar and the target frame say it only hides the game's bar if the target info is set to separate parts in the game's HUD layout (their menu notes are gone). The target-of-target note is removed entirely.

## Frames always clickable (clickable1)
- Removed the "Clickable" checkbox and setting: unit frames and party frames always take the clicks (left targets, right opens the game's menu; the target of target only the left). Nobody clicks the world through a frame, as in every other game. Old saved values are ignored.

## Cleanup: header and page titles (cleanup3)
- Removed the top-left block of the sidebar (NahjaUI, job and level) and the title + one-line description at the top of every page (the title repeated the highlighted sidebar item; the descriptions still live in the "What is inside" card of General). Rule of the cleanup: if a piece of text does not give information that is useful, or does not make the interface better looking or easier to use, it goes.

## Menu order, Others, hidden Debug (menu1)
- Sidebar order: General, Trackers, Unit frames, Progress bars, Job gauges, Status effects, Nameplates, Enmity list, Others, Filters, Tooltip, Appearance, Layout, Profiles. "Others" holds Limit Break and Frontline gauge as tabs (room for the Occult Crescent bars and similar). Old saved selections ("Limit Break", "Frontline gauge") go to Others. The version at the bottom of the sidebar is gone. Debug is not in the sidebar: "/nui debug" opens it (and adds its entry for the session). The Welcome card of General stays (General may be the verbose page).

## Page descriptions rewritten (descriptions1)
- The one-line descriptions of the "What is inside" card were rewritten to say what each page does now. Rule: the plugin uses the game's own terms (enhancements, enfeeblements, status effects), never buff/debuff. The Appearance button text says "NahjaUI font", not the name of the font file.

- Appearance description shortened to "General settings of the interface" (descriptions1b): it must not list contents, or it would grow forever.

- Job gauges and Layout descriptions shortened too: "The gauges of your job", "Position and size of every element" (descriptions3).

## Wolves' Den Pier counts as PvP (pvpden1)
- BUG: `ContextSnapshot.InPvP` used Dalamud `IsPvPExcludingDen`, but in the Wolves' Den Pier the PvP actions of your job are active (consolegameswiki: "The PvP Actions for your Job are active while in this zone"), so the PvE trackers, gauges and so on stayed on. Now `InPvP` = `IsPvP` (the Den included) and the new `InPvPMatch` = `IsPvPExcludingDen` is used only for the Frontline gauge (it only exists in matches).

## Terminology: enmity dot (terms1)
- "Aggro" is not used anywhere (UI, code or docs); the game's word is enmity. The code already used `ShowEnmityDot`, `EnmityDotSizePx`, `EnmityDotAnchor`, `EnmityDotOffsetX/Y`. The enmity dot only says whether the enemy attacks YOU (it cannot tell whom it attacks otherwise): the page description is "The enemies fighting you: HP, enmity, casts and status icons" and the colour label "Enemy is not attacking me" (was "someone else").

## Tabs on Nameplates (tabs1, to be tested by the owner)
- Theme: tab colours (Tab, TabHovered, TabActive, TabUnfocused, TabUnfocusedActive) from the accent, like the sidebar: the default Dalamud red no longer shows on the Others tabs.
- Nameplates page: the general card (Enabled, always show the bar of the target) stays on top; three tabs: Plate (HP bar, Name, Level), Progress bar (frame, spell name, time left), Status icons (on which plates, Enhancements, Enfeeblements). Proposed to be reused on the Enmity list if the owner likes it.

## Nameplates: "icon" wording (tabs2)
- The big icon above the plate (quest, hunt bill, objectives; copied from the game's MarkerIconId) is called "icon" in the menu, not "marker" (markers are the target symbols in FFXIV). Labels: "Show the game's icon above the plate", "Top icon size/offset".
- Reported by the owner: the icon lingers after turning in a quest / after the hunt bill count is complete. Suspected cause: the cached value only updates to zero when the game refreshes that plate (IsUpdating); not verified in game.

## Nameplates: one style per kind of plate (kinds1, phase 1, to be tested by the owner)
- Idea: like the plates of the WoW addon the owner knows: every kind of plate has its own settings (size, progress bar, status icons...).
- Kinds (tabs): Me, Party, Other players (alliance members count here, as in the game), Friendly NPCs (pets, chocobo, friendly battle NPCs), Enemy NPCs, Enemy players (PvP), Others (NPCs and objects without HP: only the name tab).
- Code: NamePlateStyle (all the per-plate fields + Active) in NamePlateStyle.cs; NamePlateSettings keeps Enabled, ShowBarOnTarget and one style per kind (StyleOf / SetStyle). NamePlateRelation got EnemyPlayer (hostile players); Enemy is only hostile NPCs; helper IsEnemy().
- Decision: no option is hidden because of taste, only what is technically meaningless (Others has no bar / level / cast / status; dispel border not offered on enemies). What changes between kinds are the defaults: Enemies active from the start; the other kinds start OFF (the game's own plate stays), without level and without progress bar.
- Each kind has "Use NahjaUI plates for these" (off: the game's plate is not touched) and a "Copy settings from" button (a real copy, nothing stays linked).
- The casts are now read for every character with HP (before: only enemies), so allies' casts (raise...) can be shown.
- Old config: the old flat settings arrive as unknown fields (JsonExtensionData) and are turned once into the new styles: every kind starts like the old single style (level and cast only kept for enemies; status rows on only where the old StatusOn* flag was on), all Active. Works for old profiles too.
- The card "Status icons: on which plates" is gone (each kind has its own rows).
- Test log "[icons]" in the game log: every change of the game's top icon with the refresh flag, to chase the lingering quest / hunt bill icon. Remove after the diagnosis.
- Pending (phase 2/3): review defaults per kind, polish, then apply tabs to the Enmity list if the owner likes them.

## Enemy plates: starting enfeeblements row (kinds2)
- Enemy NPCs and Enemy players start with the Enfeeblements row ON, only the listed filters, in this order (priority by filter): Crowd control (stun), Mitigation (Feint, Addle, Reprisal, Dismantled: what makes the enemy hit softer), Rotation upkeep (only the ones I applied).
- New default filter "Rotation upkeep" (id rotation-upkeep) in DefensiveFilters: damage over time and upkeep enfeeblements of each job: Dia, Biolysis, Combust, Eukrasian Dosis, Chaotic Spring, Higanbana, Death's Design, Stormbite, Caustic Bite, High Thunder, Demolish, Sonic Break, Bow Shock (and the older names for low levels). Created once for old configs (FiltersSettings.RotationCreated); "Restore the default filters" brings it back.
- Applied once (NamePlateSettings.EnemyRowSeeded / SeedEnemyRows) and only if the row was never set up, so no choice of the user is overwritten.
- Left out on purpose (not rotation): Feint/Addle/Reprisal/Dismantled (in Mitigation), Dokumori/Chain Stratagem (damage windows, for the user's own "Damage windows" filter), crowd control other than stun. Candidates not added because the names are not verified: Goring Blade (Paladin), Noxious Gnash (Viper), High Thunder II.
- kinds3: fixed CS1503 (DrawCast received the global settings instead of the plate style).

## Terminology: Progress Bar (progress1)
- FFXIV calls the casting bars "Progress Bar": every "cast bar / cast frame" is now "progress bar" (menu texts, classes, files, variables, namespace NahjaUI.Modules.ProgressBars, folder Modules/ProgressBars, config property Configuration.ProgressBars, layout ids "progressbar.player/target"). Always use "progress bar" in new text.
- NOT renamed on purpose: the game's own window names (CastBarEnemy, _CastBar) and the game's option EnemyListCastbarEnable.
- progress2: the settings of the bar that were named after the cast were renamed too: ShowProgressBar, ProgressBarHeightPx/WidthPx/GapPx/OffsetX/OffsetY, ProgressBarPosition (was CastSide), ProgressBarSide (enum, was EnemyCastSide), ProgressBarName, ProgressBarTime, ProgressBarTimeMode, ProgressBar{Uninterruptible,Background,Border}Color, DrawProgressBar, card ids card_np/el_progress_bar*. Labels of the Nameplates and Enmity list pages say "Progress bar ...".
- Kept on purpose because they are about the cast itself (game data and FFXIV terms): CastSnapshot, ToCast, GameNames.CastName/CastIconId (the name/icon of the action being cast), slidecast (ShowSlideCast, SlideCastMs...), Swiftcast/Surecast/Dualcast/Triplecast, recast, the game's own CastBarEnemy / _CastBar / EnemyListCastbarEnable.
- The owner said losing a saved setting is fine; the text conversion in ConfigRename still keeps them (it also converts these property names).
- Saved files: Core/Profiles/ConfigRename.cs converts the old names by text, once, BEFORE the configuration is read (the file has "$type" names with the old namespace, so without it the settings would not load). A copy is kept as NahjaUI.json.before-progress-bar. Imported profile codes are converted the same way.
- The owner must delete the old folder Modules/CastBars from his project when copying the new files.
- progress3: the page subtitles say "progress bars" (not "casts"); the history of this brief was rewritten with the new term. Rule for the future: everything written by us that means the casting bar is a "progress bar"; only the game's own names (CastBarEnemy, _CastBar, EnemyListCastbarEnable) and the cast itself (CastSnapshot, slidecast, Swiftcast...) keep "cast".

## Terminology review (terms1 -> terms2, "game words" pass)
- Official FFXIV HUD names (uiguide): Party List, Enmity List, Target Bar, Focus Target Bar, Progress Bar, Parameter Bar, Hotbar, Minimap, Duty List. Actions are of three types (spell, weaponskill, ability); the game says "recast" and "global cooldown". Avoid WoW words (aura, unit frame, buff, debuff, proc, aggro, spell for everything...).
- Done (terms2): spell -> action in the texts ("Show the action name", "Progress bar: action name", "action icon"); AuraPresets.Proc() -> Enhancement() and Debuff() -> Enfeeblement() (and the 79+11 calls); locals buffs/debuffs -> enhancements/enfeeblements; comments say "enhancement", "enfeeblement", "ready action", "glow" (no "proc"); Health -> Hp (FrameTextSettings Hp, HpPreset, HpTextPreset, DrawHp, card id card_uf_hp); UnitTextFormatter.FormatHealth -> FormatValue (it also formats MP). ConfigRename also converts the Health names.
- Kept: the game's own names (_TargetInfoBuffDebuff, BuffDispType, CanDispel...), Mana for the Red Mage (White/Black Mana are real names), dispel (the game's own CanDispel), cooldown/recast.
- Pending decisions by the owner: "Unit frames"/"Party frames" -> Party List + Target Bars (about 230 identifiers); the name of the "Auras" page and code: DONE (trackers1), see below.

## Auras are now "Trackers" (trackers1)
- FFXIV has no word for the WeakAuras idea ("status effects" is already another page). The owner chose "Trackers" (not "Action trackers": a status is not an action).
- Everything renamed: page "Trackers", folder/namespace Modules/Trackers, Tracker* classes (TrackerEngine, TrackerDefinition, TrackerGroupRuntime, TrackerPresets...), Configuration.Trackers, layout ids "tracker." / "tracker-group.". The old history of this brief was rewritten with the new words.
- The status rows of the frames (they were PartyAura*) are now StatusRowSettings / StatusRowGrowth / StatusRowFilter / DrawStatusRows: they are rows of status icons, not trackers.
- ConfigRename converts the saved file and profile codes. The owner must delete the old folder Modules/Auras.

## Notes for the presets (to do when the owner asks for them)
- White Mage: the status of Plenary Indulgence is called "Confession" (owner, 2026-11-05). Use that name when the tracker for it is added to the White Mage preset (status name, not the action name).
- Healer Role group: the order of the trackers is Surecast, Rescue, Swiftcast, Lucid Dreaming (owner, 2026-11-05).

## Jog tracker (jog1)
- "General" now has a Jog tracker (an enhancement: shown while the status Jog is on you) right after Sprint, for every job (no job filter). New "General" groups get it; existing ones get it the next time a job preset is applied (it is placed right after Sprint). The status name "Jog" is not verified in game (the Filters page reports unknown names).
- Liturgy of the Bell (White Mage CDs/defensives) shows its stacks (effectStacks) next to the time (liturgy1). Applies to new presets / re-applied ones; an already created tracker has to get the stacks text by hand or be rebuilt.

## Stacks review against the game data (stacks1)
- Checked every status tracked by the job presets against the datamined Status sheet (MaxStacks). Stacks were missing and are now shown: Inner Release (3), Soteria (4), Raiju Ready (3), Soul Reaver (2), Executioner (2), Enshrouded (5), Further Ruin (4). Liturgy of the Bell was done before.
- Limit of the check: some statuses carry their number in the status parameter and have MaxStacks 0 in the sheet (Manafication, Firstminds' Focus show stacks and work), so those can only be found by playing. Report any other one the owner notices.
- Found while checking: the status "Kaeshi: Namikiri Ready" (Samurai rotation) does not exist in the data (the sheet only has "Kaeshi: Namikiri" = a barrier status); the follow-up is probably an action that glows (Step). To decide with the owner.

## Global cooldown bar and White Mage presets (gcd1)
- Global cooldown bar (module ProgressBars, element "progressbar.gcd"): thin bar attached to the top of the player's progress bar (1 px gap), fills while the global cooldown runs. Reads recast group 57 (unverified in game).
- Queue window: tinted last 500 ms (setting). Weave limit: line 700 ms before the end (setting). The owner will test whether both fit.
- White Mage presets: Rotation = Dia, Sacred Sight (stacks), Divine Grace, Freecure. Defensive CDs = Liturgy of the Bell, Temperance, Asylum, Plenary Indulgence (status Confession), Benediction, Aquaveil, Tetragrammaton, Divine Benison. Healer Role = Surecast, Rescue, Swiftcast, Lucid Dreaming.

## gcd2
- Class presets place the global cooldown bar between the MP bar (or gauges) and the progress bar; everything below (progress bar, CDs rows) moves down. GCD bar defaults: width 240, height 6, rounding 6.

## gcd3
- Progress bars page has three tabs: Player Progress Bar, Target Progress Bar, Global Cooldown Bar.

## gcd4
- Bug fix: the rename had changed the game window name _TargetInfoCastBar to _TargetInfoProgressBar, so the game target progress bar was not hidden. Game-internal names must never be renamed.

## gcd5
- Unit frames page: the row of buttons is now a tab bar (Parameter bar, Target, Target of target, Focus, Party), same style as nameplates and progress bars.

## tooltip note
- Nameplates: the explanation of "Use NahjaUI plates for these" shows as a tooltip on hover, not as permanent text.

## colors1 - shared colors
- Theme now holds the colors shared by all bars: BarBackground (0.05,0.06,0.10,0.90), BarBorder (black), ProgressBarViolet (player's bar and uninterruptible casts), ProgressBarRed (enemy/target casts). Every default of those names (unit frames, MP, gauges, progress bars incl. GCD, plates, enmity list) points to them. Tooltip keeps its own (it is a window). Only defaults changed: saved colors are kept. Rule: a color with the same name is defined once in Theme.

## colors2
- Theme.Gold (1,0.82,0.30) is the one gold: slidecast, weave limit, gauge glow/ready frame/full, Astral, plate level, dispel. Asked by owner: very similar colors are unified, but no everything the same.

## fontnames
- Font drop-downs show only NahjaUI Display / Numbers / Text (no Saira in the label). Saira is OFL: credit lives in docs/FONT_LICENSE.txt, which must travel with the plugin.

## prerelease1 - cleanup, safety, renames
- All development tools removed (Debug page, "/nui debug", Font Lab, AddonDump, ContextMenuCapture, StatusDebugCard, GameWindowList, ReadActionFlags, LimitBreakDebug, [icons] log, OutlineCoverAreas). If a tool is needed again, rewrite it; nothing dev-only stays in the code.
- Safety: Plugin.Guard() isolates each part of the frame (3 failures in a row => rests 10 s); Dispose runs each part inside Safely(); every module gives back the game windows it hid (NativeWindowHider.ShowAll in Dispose; nameplates now use the hider too); Widgets.Card catches errors of its content.
- Renames: Unit frames -> "HUD bars" (code HudBar*, folder Modules/HudBars, layout ids hudbar.*), Party frames -> "Party List" (code PartyList*, folder Modules/PartyListBar). Saved settings/layouts/profiles converted by ConfigRename. Delete the old folders Modules/UnitFrames and Modules/PartyFrames.

## markerfix (reverted)
- Owner wants: "Show the game's icon above the plate" ON = we draw it and the game's is removed; OFF = the game draws its own. That is how it works; do not change it. The test NPC (Tiroro, an event NPC) belongs to the "Others" tab, not "Friendly NPCs" (Friendly = fighting non-hostile characters with HP: pets, chocobo, trust/duty NPCs).

## plate icon defaults
- Default of the game icon above the plate (every kind): shown (ON), size 72, offsets X 0 and Y 0. Tabs "Friendly NPCs" and "Others" keep their names.

## jog preset
- Jog tracker (group General, right after Sprint): no text and never glows (the game gives the status when Sprint ends and removes it in combat or on zone change). Builder.PlainStatus.

## plugin listing
- Dalamud listing text rewritten (NahjaUI.json: punchline, description in FFXIV terms, tags; command help of /nui in Plugin.cs). The manifest file in the owner project must be replaced with this one; add RepoUrl and IconUrl when the GitHub repo exists. images/icon.png must be copied to the output folder (csproj) or the dev plugin shows a "?".

## peloton preset
- Peloton tracker never glows (both the one in General for jobs without it and the ability tracker of Bard, Machinist and Dancer).

## Blue Mage preset (bluepreset1)
- New job setup "Blue Mage" (id 36, BLU), no gauge, MP bar on. Built for the solo build (Basic Instinct, Mighty Guard, Devour, Rehydration, vibe check, Doom, Peat Pelt + Deep Clean, Dragon Force, White Wind...).
- Rotation row: effects on you (Aetheric Mimicry: Tank, Basic Instinct, Mighty Guard, Devour, Rehydration), shown only while on, no glow.
- CDs / Defensive CDs rows use the new `Builder.Spell(...)`: the icon is shown ONLY while its effect is on or while the spell recharges (global cooldown ignored). A spell that is not in the active set never recharges, so it never appears: no need to read the spellbook.
- The "safety net" that adds every ability with a cooldown is skipped for the Blue Mage (`Spellbook: true`), or it would add ~120 spells.
- Status names (Waxing Nocturne, Devour, Rehydration...) come from memory: the preset report lists any name not found in the game data.

## WHM icons (whmicons1)
- Builder: `Enhancement/StatusTracker(..., iconAbility: "Name")` and `Cd(..., abilityIcon: true)` make the effect rule show the ICON OF AN ABILITY instead of the status icon (rule.Icon = Ability), because the game's status icon can carry its own stack count.
- WHM: Sacred Sight shows the Glare IV icon; Liturgy of the Bell (its effect) shows the icon of the ability. Author in the csproj is now "Nahja Stark".

## Filters are not for trackers (trackerfilter1, decided with Nahja)
- A tracker draws ONE icon, so a filter (list of statuses) collapsed to the one that ends first: no count, no way to tell which. The filters stay on the party list, nameplates and the other bars, where each status is its own icon.
- The "Which: One status / Filter" selector of the tracker editor is now only shown for a trigger that ALREADY uses a filter (the Dancer's partner preset, older setups), so it can be switched to one status. New triggers can no longer pick a filter. The engine still supports `FilterId` (the Dancer's stance needs it).
- Idea if it is ever wanted: a group that draws one icon per active status of a filter (not built; the party list row already does it for others).

## Filter support removed from the tracker engine (trackerfilter2, decided with Nahja)
- Removed `TrackerRule.FilterId/UsesFilter`, the filter matching in the engine, the tracker editor selector and the `filterId` of `Builder.Stance`. The filters stay for the party list, nameplates and bars. Do not bring them back to trackers unless Nahja finds a real case.
- The Dancer's "Closed Position" tracker is a normal one-status tracker: it only loads when playing a Dancer and is shown red and dimmed while the player does NOT have the status (the dance partner was not set), like a tank stance. It does not track anything when not playing a Dancer. It uses the lowest-id status named "Closed Position" (FindStatus), assumed to be the Dancer's own; TO TEST in game with a partner set. Old saved trackers with the filter get that status on load.

## Parameter bar sub-tabs (hudtabs1)
- HUD bars > Parameter bar now has two sub-tabs: "HP Bar" (general card, name, HP numbers, HP on each job) and "MP Bar" (MP bar, MP numbers, MP on each job). The master "Enabled" stays in the HP Bar tab and its tooltip says it turns the whole Parameter bar on or off.
- Menu goal set by Nahja: fewer cards per page, easier to navigate. Proposal given in chat (sub-tabs by topic, fold cards by default, group small cards).

## MP bar on for crafters and gatherers (mpcraft1)
- Default `MpOnJobs` now includes the crafters (8-15) and gatherers (16-18) (CP / GP). `MigrateMpLists` adds them once to setups that already existed (flag `MpCraftersGatherersAdded`); after that the user can turn them off again in "MP bar on each job".

## Shared colors, phase 1 (colors1, decided with Nahja)
- Decisions: Shield and MP are global; Level and Timer/Stacks are treated as text (global text + "Custom color"); Tooltip description/details: global text + "Custom color".
- New `GlobalColors` (Core/Ui) saved in `Configuration.Colors` and in the Appearance block of profiles: Background, Border, Text, Shield, Mp. Appearance has a card "Colors of the interface" with a reset. (Cast interruptible / not interruptible and Highlight come with the progress bars, nameplates, enmity list and gauges.)
- How a place uses it: its settings keep a flag `CustomX` and the getter returns `CustomX ? own : GlobalColors.Current.X`, so the renderers did not change. The saved value is the color in use. The editor control is `HudBarEditor.SharedColor`: a checkbox "Custom ..." and, only when on, the picker; turning it on starts from the color in use.
- Migration: an `[OnDeserialized]` step (once, marker `SharedColorsMigrated`; a new object is born migrated) turns a color that differs from the old default into a custom one, so tuned colors survive.
- Done so far: HUD bars (HP bar background/border/shield, MP bar color/background/border, party list MP color and the text color of every HUD bar text). Left: progress bars, nameplates, enmity list, job gauges, tooltip, tracker texts, status row timer/stacks colors.

## Dispel border color (dispel1)
- The border of the enfeeblements that Esuna and the like can remove is now a light blue (0.62, 0.84, 1.0), between the blue of the game's frame and the white of the Esuna icon (it was gold). `StatusRowSettings.DefaultDispelColor`. Setups saved with the old gold are moved to the new color once; a color the user chose is kept.

## Scholar / Astrologian native gauges (gaugehide1)
- Nahja saw the game's Aetherflow gauge staying on screen with our Scholar gauge on. The game splits the Scholar and Astrologian gauges in two windows, like the Sage's (GFF0/GFF1), and only the first one (`JobHudSCH0`, `JobHudAST0`) was hidden. Now `JobHudSCH1` and `JobHudAST1` are hidden too, with the same rule (only while our gauge of the job is enabled). Names guessed from the pattern of the other jobs: TO TEST in game; if something still shows, get its window name.

## Role actions under level sync (rolesync1)
- The tracker engine hid every ability whose level was above the (synced) player level, role actions included. Role actions (Rescue, Swiftcast, Lucid Dreaming, Feint...) stay usable under sync once learned, so they are now exempt (`GameNames.IsRoleAction`, from the game's `IsRoleAction` column). Job abilities keep being hidden when the level is too low. TO TEST: Scholar level 100 synced in a FATE: the Rescue tracker must show.

## Arcanist gauge window and role actions (round 2)
- The game's Scholar gauge window is `JobHudACN0` (the Arcanist family), not `JobHudSCH0`. It is now hidden for Scholar (when our Scholar gauge is on) and for Summoner (when our Summoner gauge is on). SCH1/AST1 stay as harmless guesses.
- `GameNames.IsRoleAction` also accepts player actions with no class/job and a level (fallback in case the `IsRoleAction` column does not flag Rescue). Not confirmed in game.

## Corner rounding is global only (owner decision)
- One slider in Appearance ("Shared style of the interface", `GlobalColors.Rounding`, 0-12). Every bar, frame, gauge, name plate, enmity bar, progress bar, GCD bar and tooltip reads `GlobalColors.Current.Rounding`. There is NO per-element rounding: mixed roundness has no aesthetic sense. ImGui itself limits the rounding to half the height of a small bar.
- Left out on purpose: the corner radius of tracker icons (`CornerRadiusPx`), because they are small.
- Nothing compiled or tested.

## Rule: no migration code (owner decision)
- Do not write code that reads old keys, converts old settings or migrates saved files. The plugin is not public yet; anything that only keeps the owner's current setup is not worth it. Losing a setting and placing it again is fine.
- Only write compatibility code if it will matter for people who install the public plugin.
- Older migrations already in the code (shared colors, MP lists, etc.) were written before this rule.

## Cleanup of migrations (done after the rule above)
- Removed: shared-color and dispel-color markers, Parameter bar and MP list migrations, gauge/progress bar/status row text migrations, name plate legacy style conversion, the whole tracker legacy model (old fields of TrackerDefinition and the engine code that converted them), stack-text normalization, group size migration, defensive filter version steps, JobSetups legacy groups, ConfigRename (and its use in profile import), the steps of ConfigMigrations (the framework stays, empty), main window page redirect.
- Kept on purpose (real features): ConfigSafety.BeforeUpdate (copy of the file before an update), ConfigMigrations framework, profile import version check, "Use the NahjaUI font everywhere" button (now edits the fonts of the trigger texts, progress bars and gauges).
- Files to delete by hand in Visual Studio (this tool cannot delete): `Core\Ui\RoundingControl.cs` and `Core\Profiles\ConfigRename.cs`.
- Old saved files lose what only the old code understood; that is accepted.

## Trackers: General card removed
- The "General" card of the tracker editor is gone. A tracker is turned on/off by double click on its row in the explorer or from the right-click menu (Enable/Disable). The right-click menu has a new "Edit name" entry that opens a small name box (the name stays automatic otherwise).
- Idea in discussion: an "Advanced" mode that hides niche options by default (inventory of menus first, then a helper that marks controls as advanced). Not started.
- Nothing compiled or tested.

## Ability conditions renamed, new "On cooldown with charges left"
- Labels of the "When" combo for ability triggers: "Highlighted by the game" (was It is activated), "On cooldown" (was It is recharging: no charge left), "Off cooldown" (was It is ready: at least one charge), "Always (on or off cooldown)".
- New `ConditionKind.AbilityOnCooldownWithCharges` (appended at the end of the enum so saved values do not shift): matches while an ability with several charges has one recharging and at least one still usable. It overlaps with "Off cooldown" on purpose (a trigger list is tried in order). Its phase counts as Cooling for the effects.
- Nothing compiled or tested.

## Trackers trigger refactor (supersedes the section above)
- `ConditionKind` now: StatusPresent, StatusMissing, AbilityHighlighted ("When the game highlights it"), AbilityOnCooldown, AbilityOffCooldown, AbilityFull, AbilityNotFull. Removed: AbilityAlways, StatusAlways, AbilityOnCooldownWithCharges. "Always" is built by combining triggers (the order decides).
- Enum values are not kept for old saves (no-migration rule). `EffectWhen` is gone: `TrackerRule` has plain bools `Glow`, `Red`, `Dim`, applied whenever the trigger matches. `IgnoreGlobalCooldown` is gone: a single-charge ability whose total cooldown is within the global cooldown always counts as off cooldown.
- Engine: OnCooldown = no charge left; OffCooldown = at least one charge; Full = all charges; NotFull = some charge recharging (multi-charge only). Phase is Cooling while something recharges, so sweep and time show.
- Editor: "Full" and "Not full" are hidden when the game says the ability has one charge (the saved value stays visible). New ability trigger defaults to Off cooldown.
- Presets: per ability version (newest first): [Highlighted + Glow, only if glow], [On cooldown + Dim], [Off cooldown]; each with time text (+ stacks when it has charges). Upkeep with one name = present + missing (red and dim). Blue Mage `Spell` = On cooldown + Dim.
- Nothing compiled or tested.

## Icon trim is always on
- Removed the "Trim icon margins" option everywhere (trackers, hud bar status rows, status effects, party list setting). Removed `IconTrim`, `TrackerDefinition.Trim` and `TrimLabel`. The empty margins of the game's icons are always cut (`IconMargins.Get` straight into `IconUv`). Nothing compiled or tested.

## Fixed accent color and window rounding
- Removed the user options "Accent color" and "Window corner rounding", and the "Reset appearance" button. `Theme.Accent` is now a fixed readonly color (violet 139,124,255) and `Theme.WindowRounding` a constant (6). Removed `Configuration.AccentColor/WindowRoundingPx` and their entries in profile export/import. Nothing compiled or tested.

## Icon corner rounding is global
- Removed `CornerRadiusPx` (and its sliders) from trackers, tracker group template, status rows of the HUD bars, Status Effects and the party list. Icons use `GlobalColors.Current.Rounding`, capped at half of the shorter side. Nothing compiled or tested.

## Appearance page reorganised
- Theme card: Menu text size, "Show NahjaUI in the server info bar", and the buttons "Reset window" + "Use the NahjaUI font everywhere".
- "Shared style of the interface" card now holds: Background, Border, Text, Shield, MP, Target ring color, Corner rounding and the Role colors (tank, healer, damage, crafter, gatherer). No "Reset shared style" and no "Reset role colors" buttons: every color uses `Widgets.ColorSetting` (swatch + own picker popup with a "Reset" button that restores the original color). Removed `GlobalColors.Reset` and `DrawRoleColorsCard` (now `DrawRoleColors`, content only).
- Removed options: "Timer tenths below" (fixed `TrackerEngine.DecimalsBelowSeconds = 3`) and "Game windows over the interface" (always on). Removed `Configuration.TimerDecimalsBelowSeconds/GameWindowsOnTop`.
- Streamer mode now lives in a small "Privacy" card. The "Game interface" and "Server info bar" cards are gone.
- `ColorSetting` uses `ImGui.ColorButton` + `ColorPicker4`: if the bindings differ in signature, fix there. Nothing compiled or tested.

## Timers without tenths, stronger shield yellow, streamer mode in Theme
- Timers never show tenths any more: removed `DecimalsBelowSeconds` and the decimal branch of `FormatTime` (whole seconds rounded up, minutes from 60 s).
- `GlobalColors.DefaultShield` is now (1.0, 0.85, 0.25) (was a pale 0.96, 0.91, 0.66). Saved shield colors keep their value.
- Streamer mode moved to the Theme card; the "Privacy" card is gone. Nothing compiled or tested.

## One font family, no font choice
- The interface only uses the three NahjaUI fonts, fixed by the kind of text: **Display** (Saira Condensed SemiBold) for names, HP texts and progress bar / cast texts (frames, party, nameplates, enmity list, progress bars); **Numbers** (Saira Condensed Bold) for the timers and stacks of trackers and of the status icon rows, and for the text of job gauges; **Text** (Saira Regular) for menus, tooltips and labels.
- Removed every font selector (frame texts, job gauge texts, nameplate name, progress bars, trackers, status rows) and every saved font field (`FrameTextSettings.Font`, `NamePlateStyle.NameFont`, `StatusRowSettings.Font/StacksFont`, `TrackerTextDefinition.Font`). Only the size stays configurable. `HudBarModule.DrawText` takes an optional `font` (default Display); job gauges pass Numbers.
- `OverlayFont` enum now has only NahjaDisplay, NahjaDisplayBold and NahjaText (game fonts Meidinger, Axis and Trump Gothic removed). Removed the "Use the NahjaUI font everywhere" button and `Plugin.UseNahjaFontEverywhere`. The enmity list now uses Display like the other frames.
- Decision: no custom font option, ever (for now). Nothing compiled or tested.

## HP bar master checkbox removed
- Removed "Show the HP bar" from the "HP bar on each job" card and `HudBarSettings.ShowHp`: the per-job toggles are enough (all jobs off hides the bar). The player's HP bar shows when the frame is enabled and the job is on. Nothing compiled or tested.

## HUD bar colors are global, fewer switches
- New global colors in `GlobalColors` (Appearance, "Shared style of the interface"): `FriendlyNpc`, `EnemyHp`, `PetMinion`, `ObjectColor`, `TimerText`, `StacksText` (each with a Reset in its picker). Removed `FriendlyColor/HostileColor/PetColor/ObjectColor` from every HUD bar (player, target, target of target, focus, party look) and `TimerColor/StacksColor` from the icon rows (now read-only properties of `StatusRowSettings` that return the global ones, `[JsonIgnore]`).
- HUD bars: removed all "Custom" color options (background, border, shield, MP, MP background, MP border) and their fields; `BackgroundColor/BorderColor/ShieldColor/MpColor/MpBackgroundColor/MpBorderColor` are read-only and come from `GlobalColors.Current`. The "Custom color" of texts (`FrameTextSettings.CustomColor`) is still there (shared with gauges, progress bars and enmity list).
- Removed `ShowShield` (shields always drawn, also on target of target / focus / party) and `ShowMp` (the MP bar of the player depends only on the per-job list) with their checkboxes. Removed the MP color picker of the party options.
- Nothing compiled or tested.

## Tester release setup (repo.json)
- GitHub repo: https://github.com/NahjaUI/NahjaUI (branch master). Custom repository URL for Dalamud: `https://raw.githubusercontent.com/NahjaUI/NahjaUI/master/repo.json`.
- `repo.json` (repo root) lists the plugin; downloads point to `releases/latest/download/latest.zip`. `.github/workflows/release.yml`: pushing a tag `vX.Y.Z.W` builds Release, creates a GitHub release with `latest.zip` (made by DalamudPackager) and updates `AssemblyVersion` and `LastUpdate` in repo.json, committing to master. `DalamudApiLevel` in repo.json is 15 (matches Dalamud.NET.Sdk 15): check it against the NahjaUI.json inside the first latest.zip.
- csproj: Version 0.1.0.0, RepoUrl, IconUrl, and docs/FONT_LICENSE.txt copied next to the dll (so it travels in the zip). `pr-build.yml` is still the unedited SamplePlugin template (wrong names): fix or delete. The stale files listed earlier were already deleted by the owner (checked).
- Nothing built or published yet.

## README
- `README.md` written at the repo root (English): what it is, install through the custom repository, first steps (`/nui`, `/nui unlock`, trackers presets), how to report (GitHub issues), build, credits (AGPL v3 per LICENSE.md, Saira OFL, Square Enix disclaimer). Discord is not mentioned until it exists.

## Show when: combat and weapon conditions for every layout element (written 2026-11-11, NOT compiled or tested yet)
- Decided with the owner: the condition lives in the Layout system, not in the page of each module (same idea as the shared colors: fewer options per page). It is edited in two places: the Layout page (element editor, "Show this element") and the unlock mode panel (two combo boxes under the X/Y fields). Both call `VisibilityControls.Draw`.
- Data: `ElementLayout.ShowInCombat` (Always / InCombat / OutOfCombat) and `ElementLayout.ShowWithWeapon` (Always / Drawn / Sheathed). Both must be true. They are NOT part of the position (`CopyPositionFrom` does not copy them), so they are not saved per job.
- Rule: `LayoutManager.IsShown(element)` (instance) and `LayoutManager.IsShownNow(element)` (static, for static drawing helpers). Always true while unlock mode is on or the element is selected on the Layout page, so it can be placed. The context comes from `ContextSnapshot` (`InCombat`, new `WeaponDrawn` = `StatusFlags.WeaponOut` of the local player, read in `GameDataProvider`).
- Wired in: HUD bars (frames and player MP), enmity list, party list, progress bars (player, target, GCD), status effects groups, job gauges (every part, extra gauges too), trackers (single trackers and groups; a group's condition applies to all its trackers). A hidden HUD bar does not bring the game's own bar back (the game's bar stays hidden while ours is enabled).
- NOT covered: nameplates (they are not layout elements; they follow the game's own options), the tooltip.
- To verify in game: that `StatusFlags.WeaponOut` is the right flag (weapon drawn/sheathed), and that the combos fit in the unlock panel.
- The Layout page list of elements is now alphabetical (case-insensitive by name).
- Tester asked for nameplates that NahjaUI enables by itself when the game's own plates are off. Decision: NOT done. NahjaUI does not override or duplicate what the game already offers unless there is a very good reason; keeping the game's settings as close to normal as possible means that when the plugin goes down (patch days) the player does not have to configure the interface again. Nameplates keep following the game's options.
- Tester also asked for a global UI scale (1440p, enmity list hard to read). Open: ask which UI scale he uses in the game; for now the enmity list has its own text and bar sizes. A global scale would touch every use of `ImGuiHelpers.GlobalScale`.

## Trackers: the "Combat" load condition is gone (2026-11-11, not compiled or tested yet)
- Removed `TrackerCombatMode`, `TrackerLoadConditions.Combat`, its combo box on the Load conditions card (cards one line shorter) and its check in `TrackerEngine.IsLoaded`. Nothing in the presets used it. Combat and weapon conditions now exist only as the "show when" of the layout element (a tracker, or its group). No migration: saved values of the old field are ignored.
- Job gauges were already layout elements, so they got the same "show when" with the layout change (checked).

## Blurry overlay text (tester eryk, 2026-11-11, not tested yet)
- Cause found in `OverlayFonts.PushCap`: the font chosen was the closest BUILT size, but the text was drawn at `wantedLine * GlobalScale`, a different size, so the letters were stretched a few percent (blur). Now `TextFace` draws at the size the font was built at (`ImFontPtr.FontSize`), and the letter height is `ratios.Height * size` (a fraction of a pixel off the asked height). Added built sizes 9, 11, 22, 26, 34, 40, 46, 52, 60 so the difference stays small. Only while no font is ready the old wanted size is used.
- To verify in game: text of the HP bars, enmity list, nameplates, trackers and progress bars is sharp; letters have the size the page says (a little more or less is expected).

## Enmity list cut at the top-left corner (tester eryk, 2026-11-11): NOT fixed, cause unknown
- No code reason found: the list draws on the background draw list inside `GameCover.Draw` (clips only around game windows that cover it, with a margin of 400 px). Hypothesis: a game window near the corner is treated as a cover and cuts the list. Next step: ask the tester to press "Copy the names of the windows cut" (diagnostic of the cover) with the list at the corner, and a screenshot.

## Correction: overlay text was tiny after the blur fix (2026-11-11, not compiled or tested yet)
- The first blur fix drew at `ImFontPtr.FontSize`. In game (owner, 4K) all overlay texts came out about half size: that value is the LOGICAL size of the font, not the size in screen pixels (the old code, which worked, drew at logical size times `ImGuiHelpers.GlobalScale`).
- Now `OverlayFonts.PushCap` keeps the logical size of the font it chose (`FindClosest(..., out builtPx)`) and draws at `builtPx * GlobalScale`. That is the size the font was built at, so no stretching, and the same scale the old code used. Lesson: never read the drawing size from `FontSize`; take it from the size table times the UI scale.
- Assumption to verify in game: a font built at N logical px is built by Dalamud at N times the UI scale. If text is still blurry at UI scale above 1 (the owner's 4K), the assumption is wrong and the sizes need rechecking.

## Enmity list cut by the alliance lists (tester eryk, 2026-11-11, not compiled or tested yet)
- The tester found that the game's alliance lists (`_AllianceList1`, `_AllianceList2`), switched on in the HUD layout but not shown (not in an alliance), still counted in `GameWindowCover` as a covering window and cut our enmity list (names started cut, "Gurangatch" looked like "ngatch").
- Fix: `GameWindowCover.Update` skips every window whose name starts with `_AllianceList`. It is a fix for this window only: other windows that are "visible but empty" could behave the same; the diagnostic "Copy the names of the windows cut" was removed from the published build, so a new case needs it back for a moment.
