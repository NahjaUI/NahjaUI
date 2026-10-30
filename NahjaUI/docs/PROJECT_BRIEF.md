# NahjaUI - project brief

Paste this at the start of every AI session. Last updated: 2026-10-26.
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
- Pixel perfect: integer offsets in logical pixels, anchors (9 points) to screen or to another element, scale with ImGuiHelpers.GlobalScale, round to whole pixels. Unlock mode (ElvUI style): drag plain boxes with NO text on them (the always-visible name/coordinate tags were removed on purpose: with many elements they overlapped and hid each other). A panel (name, X/Y fields with typed value or +/- buttons, Locked, Reset position) appears only next to the box that is being dragged or under the mouse, stays 0.4 s after the mouse leaves (and while the mouse is on the panel or a field is being edited), then falls back to the selected box; with nothing selected and nothing under the mouse there is no panel. Boxes are drawn biggest first and use `NoBringToFrontOnFocus`, so small boxes always stay on top of big ones and can be clicked. Keyboard arrows, an on-screen arrow pad, 10 px steps and grid snapping were all dropped on purpose to keep it simple. Undo/redo later.
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
  Core/Ui/OutlinedText.cs                       (the ONE place that draws text with a thin black outline: ONE PHYSICAL pixel whatever the UI scale (the owner runs 200%); the owner chose a fixed thin outline over per-text options)
  Core/Layout/                                  (AnchorPoint, ElementLayout, LayoutManager, UnlockOverlay)
  Modules/Auras/                                (AuraDefinition data, AuraEngine, AuraEditor page)
  Modules/UnitFrames/                           (player, target, target of target, focus frames and their editor page)
  Modules/PartyFrames/                          (party bars with buffs and debuffs: PartyFrameModule, PartyFrameSettings)
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
- Stack/charge text has its own size: `AuraDefinition.StackTextSize` (px from `OverlayFonts.AvailableSizes`; 0 = automatic = 75% of `TextSize`, the old behaviour, so existing auras look the same). Edited in the Display card ("Stack text size (px)"), copied by `CopyStyleFrom`, so group templates carry it. Timer and stacks share the same font.
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
- Cast bars (`Modules/CastBars`): `CastBarModule` (Plugin.CastBarSystem) draws the player's and the target's cast bar from `GameSnapshot.Player/Target.Cast` (`CastSnapshot` has ActionType: 1 action, 2 item, 5 general action, 13 mount; `GameNames.CastName/CastIconId`). Rectangular bar filling left to right, square spell icon INSIDE the bar at its left (optional), spell name at the left and time at the right (`CastBarSettings`: ShowIcon/ShowName/ShowTime, TimeMode Remaining or ElapsedOverTotal, TextSize + Font from OverlayFonts, colours, uninterruptible colour for the target). Size and position are layout elements `castbar.player` / `castbar.target`. A sample cast is drawn while the Cast bars page is open or in unlock mode (`PreviewRequested`).
- Slidecast facts: the window is a fixed ~0.5 s at the end of every cast (the same for every spell; casts shorter than that have no marker), and latency adds a little extra slack that the game does not report. Neither Dalamud nor ClientStructs exposes the player's ping, so the window is a plain setting. Per-spell or per-ping automatic windows are not possible.
- Slidecast marker (player bar only): `CastBarSettings.ShowSlideCast`, `SlideCastMs` (default 500) and `SlideCastColor`. A tinted zone covers the last `SlideCastMs` of the bar and a line marks where it starts. A cast shorter than the window shows nothing.
- Job gauges (`Modules/JobGauges`, started with White Mage): `JobGaugeModule` (Plugin.JobGaugeSystem) draws 3 rounded lily pips (the next one fills over 20 s, with the seconds left written on it) and 3 blood lily pips from `GameSnapshot.WhiteMage` (`WhiteMageGaugeSnapshot(Lily, BloodLily, LilyTimerMs)`, only non-null while the player's job id is 24). Data comes from Dalamud `IJobGauges.Get<WHMGauge>()` (`Plugin.JobGauges` service), read ONLY in `GameDataProvider.BuildWhiteMageGauge`. Two layout elements: `gauge.whm.lily` and `gauge.whm.bloodlily` (size in the layout, defaults 156x14 and 156x8 at center +190/+208). Settings in `JobGaugesSettings.WhiteMage` (Enabled, HideNative, ShowBloodLilies, ShowTimer, GlowWhenFull = frame when 3 blood lilies, gap, rounding, text size/font, colours). The game's own gauge `JobHudWHM0` is hidden with the same PreDraw + `PreventOriginal()` method as the cast bars. A sample is drawn while the "Job gauges" page is open or in unlock mode. VERIFIED by the owner in game: `LilyTimer` counts UP from 0 to 20000 ms towards the next lily (the Debug page card "White Mage gauge (raw)" shows the raw values). Pending: load conditions (only in combat, etc.), other jobs.
- Warrior gauge (added, NOT yet verified in game): `GameSnapshot.Warrior` (`WarriorGaugeSnapshot(BeastGauge)`, only while the job id is 21), read from `IJobGauges.Get<WARGauge>().BeastGauge` in `GameDataProvider.BuildWarriorGauge`. `JobGaugeModule.DrawWarrior` draws ONE rounded bar 0-100 (layout element `gauge.war.beast`, default 156x14 at center +230), optional mark at 50, number on the bar, colour change from 50 up (a spender costs 50). Settings: `JobGaugesSettings.Warrior` (`WarriorGaugeSettings`). Native gauge name `JobHudWAR0` is a GUESS from the WHM pattern: check it in Debug > Game windows as a Warrior. The Debug page has a card 'Warrior gauge (raw)'. `Widgets.Card` chains the mouse wheel to the page when the card is at its scroll end. Inner Release / Surging Tempest are statuses and belong to auras, not to this gauge.
- Job filter in unlock mode / Layout page: `LayoutElement.IsHidden` (not saved; `LayoutManager.Positionable` skips hidden elements). `AuraEngine` sets it every frame: an ungrouped aura is hidden when its `Load.JobIds` does not contain the player's job; a group is hidden when it has members and none applies; ghosts in unlock mode only for members that apply. `JobGaugeModule` hides the WHM/WAR elements of the other job and draws the unlock-mode sample only for the current job (the Job gauges page still shows both samples). While the job is unknown (not logged in) nothing is hidden. Job gauge cards follow the game's job order: tanks PLD WAR DRK GNB, healers WHM SCH AST SGE, melee MNK DRG NIN SAM RPR VPR BST, physical ranged BRD MCH DNC, magical ranged BLM SMN RDM PCT BLU. BST (Beastmaster) is a new limited job (patch 7.5x, level cap 50, like Blue Mage), melee DPS, gauges: TP gauge, Instinct gauge, Instinct stacks (per consolegameswiki; Dalamud support for its gauge NOT checked). Next planned: cards that size to their content (one scroll per page), then profiles and import/export (shareable strings via Discord).
- MINIMIZE: the main window has NO native collapse arrow (`NoCollapse`); a title bar button (FontAwesome WindowMinimize) calls `Plugin.MinimizeMainWindow()`, which closes it and opens `MiniBarWindow` (`Windows/MiniBarWindow.cs`: 190x34 px bar, no title bar, NahjaUI mark + name at the left (also the drag handle), an open button and a close button drawn with plain lines) with its top right corner at the window's top right corner. `Plugin.ExpandMainWindow()` reverses it, putting the window's top right corner where the bar is; `/nui` and the plugin list buttons expand it when the bar is shown (`Plugin.ToggleMainUi`); unlock mode hides both and restores whichever was open. The window titles start with three spaces so the text stays clear of the rounded corner. The mark is `Core/Ui/NahjaMark.cs` (the final icon).
- ICON (done): the interface is named after the owner's character Nahja Stark (Miqo'te Paladin). The owner CHOSE concept C (shield made of four rounded tiles with two cat ears; 2x2 grid, bottom tiles curved into the shield point, top right and bottom left tiles at 55% opacity). Character reference (owner's images): Miqo'te with long straight black hair and bangs, black cat ears with pink inside, violet-blue eyes (the source of the accent colour), tan skin, two diagonal red cheek marks, black baseball cap with a white winged emblem and white piping, white halter top, black fingerless gloves, starry blue galaxy background. Four variants of C were shown (C plain, C1 whisker slashes, C2 small star, C3 cat eye) and the owner picked plain C, so the icon is FINAL: `Core/Ui/NahjaMark.cs` draws it with ImDrawList shapes on a 128 grid scaled to any size, following the accent colour (inner ears = accent mixed 60% with white); the same drawing is `images/icon.svg` and `images/icon.png` (512 px, for the plugin list / repository manifest later). Possible later detail variants (whisker slashes, star, eye) are in the Icon concepts canvas. Single accent violet #8B7CFF on indigo, must read at 20 px. 
- Unit frames (`Modules/UnitFrames`; player, target, target of target and focus frames): `UnitFrameModule` (Plugin.UnitFrameSystem) draws a life bar (layout element `unitframe.player`, default 280x26 at center -330/+190), a shield on top of the life (`UnitSnapshot.ShieldPercent` = the game's `ShieldPercentage`, a % of max life), drawn as ONE block as tall as the bar with the bar's rounded corners (only the corners touching the bar's ends are rounded): right after the life if it fits, otherwise from the right end of the bar over the life, reaching further left the more shield there is; the shield is translucent (`UnitFrameSettings.ShieldOpacity`, default 0.65, multiplied with the colour's alpha; the owner asked for it and for NO divider line) so the life and the background show through it, an optional mana bar whose place is chosen PER JOB (`ManaPlacement`: Off, UnderLifeBar = as wide as the life bar, OwnBox = a separate layout element `unitframe.player.mana`, default 156x8 at center +224, meant to sit next to the job gauge; `UnitFrameSettings.ManaJobs` = under the life bar, default healers and casters, `ManaOwnBoxJobs` = own box, `GetManaPlacement/SetManaPlacement`; the own box shows a sample while moving things; all jobs of the own box share ONE position for now, per-job positions would need profiles). The life colour can be fixed or the class colour (`LifeColorMode`; `JobColors`: the FFLogs job colours (the game has no official per-job colour; it only colours by role: Lodestone tank blue #2d3a80, healer green #346624, DPS red #732828). Source: stocky37's public gist `fflogs-colours.yaml` (Jan 2022, 19 jobs) plus Viper #108210, Pictomancer #FF66CC and Blue Mage #00AEEF given by the owner from the current FFLogs (all 22 jobs now come from FFLogs). The earlier NahjaUI-made palette was replaced; editable per job in `JobColorOverrides`; base classes use the colour of their job; enemies always use `HostileColor`), and two free texts. Each text is a `FrameTextSettings` (Enabled, Anchor = 9-point on the bar with the text's own same point placed there, OffsetX/Y, TextSize, Font, Color) drawn last with a black outline, so it can stick out of the bar. Defaults reproduce the owner's reference image: name at TopRight offset (-4,-9) (half over the top edge), life text at BottomLeft offset (4,-2) reading `738K | 100%`. Options: `NameFormat` (Full, FirstName, LastName, FirstInitialLastName, FirstNameLastInitial, Initials; `NameFormatOnlyPlayers` leaves enemy/NPC names alone, using `UnitSnapshot.IsPlayer` = ObjectKind.Pc) and `HealthTextPreset` (CurrentAndPercent, CurrentFullAndPercent, Current, CurrentFull, Percent, CurrentAndMax, Deficit, Custom template with {cur} {curk} {max} {maxk} {def} {defk} {pct}), `Separator`, `PercentDecimals` (rounded DOWN so non-full life never reads 100%), `AbbreviationDecimals` (K; millions keep at least one decimal), `Thousands` separator (comma/dot/space/none; dot also switches the decimal mark to a comma). All text building is in `UnitTextFormatter`. The game's own player bar `_ParameterWidget` is hidden with PreDraw + `PreventOriginal()` (`HideNative`, default on). The four frames share one drawing code and one settings class (`UnitFramesSettings.Player/Target/TargetOfTarget/Focus`; defaults from `UnitFrameSettings.CreateTarget()` = the player's look mirrored with no mana, and `CreateSmall()` = thin 150x18 bars with the name inside at the left and the percentage at the right); layout elements `unitframe.target`, `unitframe.targetoftarget`, `unitframe.focus`. The class colour table `JobColorOverrides` is shared (it lives in `UnitFramesSettings`). Colour of the life: enemies = `UnitSnapshot.IsHostile` (the game's StatusFlags.Hostile) and not a player use `HostileColor`; players use the class colour or the fixed one; friendly NPCs use the fixed one. The game's own bars are hidden with PreDraw + `PreventOriginal()` for three frames (names VERIFIED in game with the Debug "Game windows" card): player = `_ParameterWidget`, target = `_TargetInfoMainTarget` (the target of target is part of it, so it has no bar of its own and hiding the target hides it too), focus = `_FocusTargetInfo`. Left alone on purpose: `_TargetInfoBuffDebuff` (target statuses, until frames show buffs) and `_TargetCursor`. Visible names seen with the target info in SEPARATE PARTS mode; the combined mode's `_TargetInfo` was not seen and is not handled (the editor says to use separate parts). `HideNative` defaults to true for all frames; a config saved earlier keeps false for target and focus until ticked. The Debug "Game windows" card (`Game/GameWindowList.cs`) stays for finding other windows. Buffs/debuffs on the player, target and focus frames come later (the party frames already have them, see below). A sample unit is drawn while the "Unit frames" page is open or in unlock mode.
- Unit frame clicks: each real (not sample) frame has a see-through ImGui window exactly over the bar (`UnitFrameModule.HandleClicks`, skipped in unlock mode) that takes the mouse, so the game does not get the click. `UnitFrameSettings.Clickable` (default true) turns it off. LEFT click = `Plugin.TargetManager.Target = unit` (found with `ObjectTable.SearchByEntityId`). RIGHT click = a small NahjaUI popup (Target, Set as focus target, Clear focus target, Copy name). The game's own character context menu is NOT available: ClientStructs has no function to open it for a given character (only `AgentContext.OpenContextMenu` and `OpenContextMenuForAddon`, which depend on internal state), so it was not guessed. Target of target when you target YOURSELF: Dalamud's `TargetObject` reads a field that is not valid for the local player (ClientStructs says the local player's target must be read from the target system), so `GameDataProvider.TargetOfTarget` returns the target itself in that case.
- Party frames (`Modules/PartyFrames`, `Plugin.PartyFrameSystem`, DONE phases 1 and 2; settings in `UnitFramesSettings.Party`, edited on the Unit frames page tab "Party"): one bar per member in `GameSnapshot.Party` (the local player is in it when grouped; empty when solo), one under the other, drawn from the layout element `unitframe.party` (the FIRST bar; default 216x34 at top left +60/+300; the others follow downwards with `RowSpacingPx`; in unlock mode the box is the whole stack via `DisplayRect`). The look of a bar is a normal `UnitFrameSettings` (`PartyFrameSettings.Look`, created by `CreateLook()`: class colours, rounding 4, name at the left and percentage at the right in Saira bold) so the same editor cards are reused. Party order and chocobo: `PartyFrameSettings.Order` (`GameOrder` default = the order of the game's own party list, read in `GameDataProvider.ReadGameListOrder` from `AgentHUD.Instance()->PartyMembers[i].Index` (HudPartyMember: EntityId + Index = real position in the UI; the span itself always has the local player first; `PartyMemberCount`), so it follows the game's sort option by itself; verified in the ClientStructs source on 2026-10-26, not yet compiled; `MeFirst` = you first, then the others in the game's order; if the agent cannot be read the order of `IPartyList` is used; the enum values were swapped on purpose so a saved 0 now means the game's order) and `ShowChocobo` (default on): `GameSnapshot.Chocobo` is found by `GameDataProvider.BuildChocobo` (scans the object table every ~30 frames for an `IBattleNpc` with `BattleNpcKind == BattleNpcSubKind.Buddy` and `OwnerId` = you; `BattleNpcSubKind.Buddy` = the chocobo companion, verified in the ClientStructs source; only while in a party), drawn as the last bar with a gold stripe, no mana (`UnitSnapshot.IsCompanion`). Smooth status timers: `GameDataProvider.SmoothRemaining` (all units, so auras too): the game refreshes the remaining time of other players' statuses only every few seconds, so when the same value is read again the elapsed time is taken off locally; a new value from the game wins. In the party bars, the role stripe, the life fill and the mana fill are drawn with `FillRounded`: the full rounded box cut by a clip rectangle, so narrow fills follow the bar corners (a plain narrow rounded rect sticks out because ImGui clamps the rounding to the width). Party members always count as players (`UnitSnapshot.IsPlayer` is false for them because `ToUnit(IPartyMember)` does not set it), so class colours and name formats are forced with `isPlayer = true`. Extras: a role stripe at the left edge (tank blue, healer green, damage red), a mana strip inside the bottom of the bar chosen by `PartyManaMode` (Off, Healers, HealersAndRaisers = healers + SMN/RDM (default), Everyone), and a targeted-member marker for gamepad users (ring + arrow, `SelectionColor`, default accent #8B7CFF). Clicks use `UnitFrameModule.HandleClicks(id, pos, size, unit)` (left = target, right = NahjaUI menu). The game's own party list `_PartyList` (VERIFIED in game with the Debug page) is hidden with PreDraw + `PreventOriginal()` (`Look.HideNative`). With no party, 8 sample members are drawn while the Party tab is open or in unlock mode.
- Party buffs and debuffs (phase 2): two rows of icons per member, `PartyFrameSettings.Buffs` (default: attached to the Left of the bar by the icon's Right, offset -8, growing Left) and `Debuffs` (Right of the bar by the icon's Left, offset +8, growing Right), each a `PartyAuraSettings`: Enabled, OnlyMine (`StatusSnapshot.SourceId` == local player), `Filter` (All = every buff / debuff by the Status sheet category 1 / 2, OnlyListed, AllExceptListed) with `StatusIds` list (edited by clicking the statuses the party has right now, or a name search), BarAnchor, IconAnchor, OffsetX/Y, IconWidth x IconHeight (DEFAULT 32x24: the owner chose it over 36x24; rectangular, wider than tall like the auras), CropPercent (default 13, same crop as the auras: `AuraEngine.IconUv` with `StatusIconAspect`; icons are cropped, never stretched), SpacingPx, MaxIcons, Growth (Left, Right, Up, Down), timer and stack count (the timer text reuses `AuraEngine.DrawOverlayText` and `FormatTime`, made internal). Every icon has a 1 px black border and rounded corners (`CornerRadiusPx`, default 3, drawn with `AddImageRounded`; auras have the same setting, `AuraDefinition.CornerRadiusPx`, default 3, so the whole interface looks consistent; the cooldown sweep wedge follows the rounded corners: a fan of triangles to the edge of the rounded box, found with `EdgeDirection`). Statuses come from the party list data (`IPartyMember.Statuses`), which can lag a little behind the real ones. The timer and the stack count each have their own anchor on the icon (9 points), offset X/Y and size (stack size 0 = automatic, three quarters of the time size), plus one font per row; defaults like the auras (time centered, stacks at BottomRight offset -2/-1). Preview icons use real status ids (160, 1191, 50, 638, 43, 144) and ignore the filters.
- DECISIONS for party frames (owner): layout A (buffs left of the bar, debuffs right); only removable debuffs get a violet frame (#B49CFF, phase 3, NOT done), no red frames, no frame on shields or HoTs; mana bar visible for healers and optionally the jobs that can raise; NO hotbar / keybind / mouseover automation (dropped for good: UI only). Phase 3 needs the "removable" flag of the Status sheet (probably a `CanDispel` column, NOT verified: check it or fall back to an editable list). Later: an ElvUI-style filters section, and choosing party statuses through the aura system (list) instead of the per-row list.
- FONT DECISION (owner chose Saira over Barlow after comparing two mockups): NahjaUI's own interface font is Saira, embedded in the plugin (`Core/Ui/EmbeddedFonts.cs`, generated, base64 of three subset TTFs: Saira Condensed SemiBold = `OverlayFont.NahjaDisplay` for names and cast bar texts, Saira Condensed Bold = `NahjaDisplayBold` for timers, stacks and numbers, Saira Regular = `NahjaText` for menus, chat and labels). Licence SIL OFL 1.1 (no reserved font name); `docs/FONT_LICENSE.txt` must stay in the repo and ship with the plugin. Subset: Basic Latin, Latin-1, Latin Extended-A, general punctuation U+2010-2027, U+2039-203A, euro; no CJK. `OverlayFonts` loads them with `NewDelegateFontHandle` + `OnPreBuild` + `AddFontFromMemory` (SizePx is logical, Dalamud multiplies by the scale) at 15 fixed sizes (overlay sizes plus menu sizes 12-20). `OverlayFonts.Current` lets `ThemeScope` push the `NahjaText` font at `Configuration.MenuTextSize` (default 15) before every NahjaUI window starts. New auras default to `NahjaDisplayBold`, new cast bars to `NahjaDisplay`; existing ones keep their saved font until the owner presses Appearance > "Use the NahjaUI font everywhere" (`Plugin.UseNahjaFontEverywhere`). Enum values were only appended, so saved settings stay valid. Possible later step: custom-drawn number glyphs for timers (owner has not asked yet).
- FONT SIZES: `OverlayFonts` fonts are built by Dalamud already multiplied by `ImGuiHelpers.GlobalScale` (the plugin's atlas is globally scaled). Always pass the LOGICAL pixel size the user chose to `PushClosest` and never multiply it by the scale again (that bug made text bigger than selected). Positions, offsets and borders of the overlays are in physical pixels and DO use the scale.
- Hiding native UI: `IAddonLifecycle` `AddonEvent.PreDraw` listener on `_CastBar` and `_TargetInfoCastBar`; it calls `args.PreventOriginal()` (the game's own Draw is skipped, which also covers the closing fade-out animation: clearing only the visible flag let the native bar reappear for the last ~0.4 s of a cast) and also sets `AtkUnitBase.IsVisible = false`, right before the game draws them (only when that bar is enabled and "Hide the game's cast bar" is on). The target's native bar is only separate when the game's Target Info is split into parts in the HUD layout. Same technique will hide other native elements for the unit frames.
- Unlock mode and the main window: `Plugin.FollowUnlockMode()` (called every frame in `Plugin.Draw`) closes the main window when unlock mode starts, if it was open, and reopens it when unlock mode ends, whatever way it was started or ended (button, Done, `/nui unlock`).
- Planned next (H5 rest): cooldown and resource triggers, bars, several triggers per aura.

## UI components and unlock mode (current behaviour)

- `Widgets.Card` / `Callout` size themselves to their content: the height is measured while drawing (cursor Y minus item spacing plus padding, cached per id in `MeasuredHeights`) and used from the next frame; the first frame uses the `lines` guess. They have no scrollbar and never take the mouse wheel, so each page has ONE scroll (the page). Still with their own scroll on purpose: the Auras page explorer / editor columns and the ability list (a two-panel design; ask the owner before changing).
- Unlock mode: `LayoutElement.DisplayRect` lets a module give the box to show (aura groups show the area their icons cover). Clicking a box selects it and its panel stays with it even when the mouse passes over other boxes; right click on the selected box lets go. The top bar is centered when unlock mode opens.
- Unit frames page: picker buttons Player / Target / Target of target / Focus / Party / Class colors (the class colors are shared and have their own page).

## Profiles: export / import (Core/Profiles)

- `ProfileManager`: a code is `NUI1:` + base64(gzip(JSON)) built with Newtonsoft.Json. Blocks (`ProfileBlock`): Auras (auras + groups + rootOrder), Placement (all `Layouts`), UnitFrames, CastBars, JobGauges, Appearance (accent, menu text size). Export takes any subset; all blocks = full backup (the owner wants everything to be restorable after a reinstall). `Read` never throws and changes nothing (size cap 16 MB unpacked, format/version check); `Apply` reads all chosen blocks first, writes a backup file (`<plugin config dir>/backups/backup-*.txt`, last 20 kept) and then changes the configuration.
- Auras import modes: Add (new ids so nothing clashes, NO positions: the owner wants shared auras to be placed by each user; placement keys of auras are skipped) or Replace (keeps ids, full restore with positions; default when the code contains Placement). Positions of elements that already exist are copied INTO the existing `ElementLayout` objects (modules keep references); settings blocks are replaced on the configuration (modules read them every frame). After importing auras `AuraEngine.ReloadFromConfiguration()` rebuilds the runtimes (`LayoutManager.Detach` keeps saved positions).
- UI: `ProfilesPage` (sidebar page "Profiles"): export checkboxes + copy/save to file, import box + Check code + per-block checkboxes + Import, list of automatic backups to load back. VERIFIED by the owner in game (export, import).
- Not done: named profiles with quick switching, automatic profile per job (the owner said: not now), sanity limits on imported values (icon sizes, etc.), a library of example profiles.

## Nameplates: design and research (2026-10-26, not coded yet)

Design (mockup approved by the owner, canvas "Nameplates NahjaUI"):
- Same Saira fonts. Name at the top-left, touching the top edge of the life bar like the unit frames (offset about 4,-9). Level written right after the name on the same line, with a "show level" option (and optionally hide when equal to mine). Every text has anchor, offset, size, font.
- Cast frame under the life bar, SAME width, spell icon INSIDE the frame at the left (like `CastBarModule`). Amber glow = interruptible, steel gray + lock = not interruptible.
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
- Native nameplates: addon `NamePlate` (`AddonNamePlate`, 50 `NamePlateObject`), enemy cast bars: addon `CastBarEnemy` (`AddonCastBarEnemy`, 10 slots with EntityId, Progress, CastName, Interruptible, screen position). Per-plate data from `NamePlateNumberArray.Instance()` (ClientStructs): `ActiveNamePlateCount`, and per plate `NamePlateKind`, `X`, `Y` (screen position computed by the game), `Depth`, `Scale`, `GaugeFillPercentage`, `EntityId`, `DrawFlags`, `VisibilityFlags` (0x1 draw name, 0x2 draw gauge). This list already follows the player's game options for which plates show.
- Two ways to hide the game's plates: (a) Dalamud `INamePlateGui` (official service, `OnNamePlateUpdate` handlers with `RemoveName()`, `VisibilityFlags`, `GameObject`) or (b) same trick as other native windows (PreDraw + PreventOriginal + hide `NamePlate` and `CastBarEnemy`). To test in game which one is safer; (b) must be checked to not stop the number array updates.
- Plan: read positions from `NamePlateNumberArray` (or WorldToScreen on the object), draw our plate with ImGui, keep click-through and targeting in mind (clicks on a plate should target the unit, as the unit frames do).

Nameplates step 1 (DONE and tested in game 2026-10-26): the Nameplates page (Enabled) turns on our plates for ENEMIES only (no commands for settings that are in the menu: owner's rule) (life bar + name with level, target ring, rounded, Saira). Files: `Modules/NamePlates/NamePlateModule.cs` and `NamePlateSettings.cs`; `GameDataProvider.BuildNamePlates()` reads the plates; `NamePlateSnapshot`. Verified facts:
- The game's own enemy plate is hidden with Dalamud `INamePlateGui.OnDataUpdate` and `handler.VisibilityFlags = 0` for kind 3 (BattleNpcEnemy); turning it off with `RequestRedraw()` brings the native plates back. No duplicated plates.
- Plates are read from `NamePlateNumberArray.Instance()` (per plate: `NamePlateKind`, `EntityId`, `Scale` 0-100, in `ObjectData`, which is a Span in the user's ClientStructs: read by index, NOT `GetPointer`) and `UI3DModule.NamePlateObjectInfoPointers[i]` (same index). `info->NamePlatePos` is NOT a world position: x,y are screen coordinates from -1 to 1 (y UP), z is depth (<=0 = behind the camera). Pixel = ((x+1)/2*W, (1-y)/2*H), same as the game's `ObjectData.X/Y`. It already includes the height of the enemy (flying mobs are right). `WorldToScreen` on the object position is wrong for tall or flying enemies.
- Entity id 0 or 0xE0000000 = empty plate. `Scale` makes far plates smaller (we use 60% to 100%).
- Step 2 DONE and tested: enemy cast frame under the life bar (same width, no spell icon: enemy action icons mean nothing; color of the fill = interruptible or not, same colors as the target cast bar (Cast bars page), no icon and no lock, name and time inside). Native `CastBarEnemy` hidden with PreDraw + PreventOriginal + `IsVisible=false` while our plates are on.
- Step 3 DONE and tested: click on a plate (name + bar area) targets the enemy, right click opens the NahjaUI unit menu; reuses `UnitFrameModule.HandleClicks`. Setting `Clickable`.
- Still missing: settings page and unlock element, friendly players/party/NPC/pets plates, buff/debuff/stun rows, class colors, distance limit and "only in combat" option.

## Code rules

- Integers for pixel values. Scale only when drawing.
- Save configuration debounced (never every frame).
- Wrap each module and aura update in try/catch; log through `Plugin.Log`.
- Prefer small classes with one job. Comment the "why", not the "what".
- No new libraries without asking Sandro.

## Milestones and status

- H0 Environment: DONE (template compiled and loaded in game).
- H1 Core foundation: DONE (config, theme, main window with sidebar, /nui).
- H2 Positioning and unlock mode: DONE for now (can be refined later). H2a = elements with anchors and integer offsets, unlock overlay (drag, inspector panel on hover/selection with X/Y fields, Locked and Reset position), Layout page, /nui unlock. H2b (next) = anchoring to other elements, alignment guides to other elements, match size and align, undo/redo.
- H3 Game data snapshot: DONE. Provider builds a snapshot per frame (player, target, focus, target of target, party, statuses, casts, context). Debug page shows it live. Cooldowns and job gauge are NOT in H3 (H5).
- H4 First auras: IN PROGRESS. Status trigger on player/target/focus/target of target, icon with timer and stacks, load conditions (jobs, combat), editor page with status picker (current statuses + name search). LayoutDemo test boxes removed.
- H5 Cooldown and resource triggers, bars.
- H6 Full aura editor and creation wizard.
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
- `UnitFrameSettings.Buffs` / `.Debuffs` are `PartyAuraSettings` (off by default; buffs above the bar, debuffs under it, debuffs "only mine").
- `PartyAuraSettings.ExtraStatusIds`: always shown, whoever applied them (overrides "only mine").
- Target frame: `HideNativeStatuses` hides `_TargetInfoBuffDebuff` while one of our rows is on.
- Not on the target of target. Editor: cards Buffs/Debuffs per frame (same widgets as the party page).
- To do: ordering/cap for player buffs, Esuna-removable highlight, stun row.

## Paladin gauge (done, needs testing)
- Oath Gauge (0-100) via `PLDGauge.OathGauge`, native addon `JobHudPLD0`, job id 19. Mark/ready colour at 50 (Holy Sheltron / Intervention).
- Warrior and Paladin share `BarGaugeSettings`, `JobGaugeModule.DrawBar` and `JobGaugeEditor.DrawBarGauge`. New bar-type jobs (DRK Blood? GNB cartridges are pips, not bars) should reuse them when they fit.
- Fix: the mana bar sample in its own box is only drawn for jobs set to "own box".

## Target kinds (done, needs testing)
- `UnitSnapshot.Kind` (`UnitKind`: Character, Pet, Npc, Object). `ToUnit(IGameObject)` now also returns non-battle objects (no life, no statuses) so the target frame is no longer empty for furniture/chests/aetherytes.
- Object kinds by number (ObjectKind): 3 EventNpc and 10 Retainer = Npc; 9 Companion = Pet; the rest = Object. Battle NPCs with SubKind 2 (Pet) or 3 (Buddy) = Pet.
- Colours per frame: `PetColor`, `ObjectColor` (grey). Objects: full bar in grey, name only, no life text, no buff rows.

## Combo auras (done, needs testing)
- `ComboSnapshot(Action, Timer)` from `ActionManager.Instance()->Combo` (last combo action used + seconds left).
- Cooldown auras have `OnlyAsComboStep`: shown only while `Combo.Action == Action sheet ActionCombo of the aura's ActionId` and the timer runs; the timer shows the combo time. The "comes after" action is read from the game data (no picker needed).
- Procs that are statuses (Primal Rend Ready...) use normal status auras. Idea for later: `IsActionHighlighted` for procs that are not statuses.

## Font lab (done)
- Debug page > "Font lab": every font x several outline shapes (`OutlinedText.Style`) at the real UI scale, size slider.
- Eight extra fonts were tried (Barlow, Fira Sans Condensed, PT Sans Narrow, Rajdhani, Titillium Web, Saira Semi Condensed) and the user did not like any: removed. We keep Saira Condensed (SemiBold / Bold) and Saira Regular.
- The outline stays 1 physical px, 8 directions (`OutlinedText.Draw`). If it is revisited: the lab has 4 dir, 1.5 px x12 and 2 px x16 variants.

## Aura icon follows the game's action swap (needs testing)
- `CooldownSnapshot.CurrentActionId` (= `GetAdjustedActionId`) -> `AuraState.IconActionId`: a cooldown aura draws the icon of the form the game shows now (Inner Release -> Primal Wrath). The cooldown data already used that form.
- Not verified in game which buttons swap and when; name on the aura stays the chosen one.

## Game window cover, tooltips, mouse (needs testing)
- `Game/GameWindowCover.cs` (`plugin.GameCover`, updated at the start of `Plugin.Draw`): rects of game windows that cover the screen (for now `ContextMenu`; add names in `CoverNames`; rect = addon X/Y + RootNode size * Scale, unverified). `Draw(pos,size,margin,action)` runs the drawing once per uncovered part of the screen with a clip rect, so frames are not drawn over the game's menu. Used by unit frames and party rows.
- Right click on a frame opens the game's own menu via `AgentHUD.OpenContextMenuFromTarget`. Click areas step aside while a cover is on screen.
- Buff/debuff tooltip (`StatusRowRenderer.ShowTooltip`): hover test with `IsMouseHoveringRect` (no window), `ImGui.BeginTooltip`; description from the Status sheet evaluated with `ISeStringEvaluator`. Option `UnitFrames.ShowStatusTooltips`.
- `UnitFrames.AlternateClicks` (experimental, off): no click window, `SetNextFrameWantCaptureMouse(true)` while hovering + `IsMouseClicked` (DelvUI's technique; DelvUI is AGPL: only used as a reference, no code copied). To see if the game's cursor stays.
- Idea: add more cover windows (all windows with a WindowNode, like DelvUI's ClipRectsHelper) and use `GameCover.Draw` in auras, gauges, cast bars, nameplates.

## Nameplate level color
- `NamePlateSettings.LevelColor` (default yellow 1, 0.82, 0.25), editor "Level color" in the Name card. The name and the level are drawn as two texts (level after the name + two spaces), so NPC names with numbers are not confused with the level.

## Glow when the game highlights the ability
- Cooldown auras: `GlowWhenHighlighted`; `CooldownSnapshot.Highlighted` = `ActionManager.IsActionHighlighted(Action, adjustedId)` (what the hotbar uses for its glow; DelvUI uses it for Viper). Works with the icon swap (Fight or Flight -> Goring Blade), so the aura tracks Fight or Flight, changes its icon and glows when the game lights the button. Untested in game.

## Cast bar rounding
- `CastBarSettings.RoundingPx` (default 4, slider 0-12 in the Colors card of each cast bar). Background, border and icon (left corners) are rounded; fill and slidecast tint are the rounded shape cut by a clip rect (right corners only when the icon is shown). Same rule as unit frames / nameplates.

## Several effects on a cooldown aura
- `AuraDefinition.ExtraEffectIds`: extra statuses that count as "the effect is active" (Fight or Flight + Goring Blade Ready). `TryFindStatus` checks StatusId first, then the extras in order; the first present one gives the timer. Editor: tick "The next one I click is an extra effect", then click a status (clicking one already in the list removes it). Clear on the main effect also clears the extras.

## Job icons on party frames
- Option `PartyFrameSettings.ShowJobIcon` (+ `JobIconStyle` Plain/Light, `JobIconSizePx`): the game's own job icon (62000 + jobId, or 62100 + jobId) drawn inside the bar at its left, vertically centered; the name text shifts right. Replaces the role stripe. Hidden for chocobos. Covers all jobs incl. crafters/gatherers (ids 8-18), not verified in game.
- No icon files are bundled or downloaded: they are Square Enix artwork, so we read them from the game at runtime with `TextureProvider.GetFromGameIcon` (same as action/status icons).

## Enmity list (phase 1, needs testing)
- New module `Modules/EnmityList` (settings, module, editor page "Enmity list"). Source: the game's own list `EnemyListNumberArray` (up to 8 entity ids, same enemies and order as the native `_EnemyList`), resolved through the ObjectTable into `EnemyEntrySnapshot(Unit, TargetsMe)` (`GameSnapshot.EnmityList`, read only while `GameData.WantEnmityList`). `TargetsMe` = enemy `TargetObjectId == player GameObjectId`.
- One bar per enemy: name, life %, enmity dot (red = attacking me, green = attacking someone else), target ring, cast bar under the bar using the Cast bars page target colors (different fill = cannot be interrupted). Native `_EnemyList` hidden by PreDraw (option). Clickable like the nameplates. Wrapped in `GameCover.Draw`. Element "enmitylist" in Layout (box = first bar).
- Phase 2 planned: debuff filter (count how many enemies of the list have my debuffs, e.g. Bard DoTs) and stun tracking (which enemies are stunned / can be stunned).

## Party list alone (follows the game's option)
- Reads `Plugin.GameConfig.UiControl.TryGet("PartyListSoloOff", out uint)` every frame (`ContextSnapshot.PartyListWhenSolo`, `SoloOptionRaw`). Assumption: 0 = list shown alone (the name says "off"); the Party options card shows the raw value so it can be checked in game and flipped if backwards.
- No checkbox of our own: the game decides. With no party and the option on, the party frames show you (+ chocobo, `BuildChocobo` also allowed alone). A read-out line in Party options shows what the plugin reads (remove it once the 0 = shown assumption is confirmed).

## Game cursor in alternate click mode
- Dalamud hooks `SetCursor`: while ImGui captures the mouse and `UiBuilder.OverrideGameCursor` is true, the game's cursor is replaced by the Windows one. In alternate click mode `UnitFrameModule.HandleClicks` sets `cursorWanted` while hovering a frame and `UpdateCursor()` (end of `Plugin.Draw`) sets `OverrideGameCursor = false` only then, back to true when the mouse leaves (it is a global setting). DelvUI sets it false too. Needs testing.

## Tooltip page
- New left menu page "Tooltip" (`Modules/Tooltip`: `TooltipSettings`, `TooltipEditor`, `Configuration.Tooltip`). Replaces `UnitFrames.ShowStatusTooltips` (removed). Choose where it shows (buffs/debuffs of Player, Target, Focus, Party; `Allows(place, isBuff)`), what it says (name and description always; optional ID and who applied it) and the look (text size, max width, rounding, name accent/color, description/detail/background/border colors, Reset).
- `StatusRowRenderer.DrawRow(..., place)` gets "player"/"target"/"focus"/"party" from the frame that draws it; `ShowTooltip` reads `Configuration.Tooltip`.
- Idea: the same tooltip for the status auras of the Auras page and the nameplates (not hoverable yet).

## Job menu in the load conditions
- Auras and groups: "Choose jobs..." button in the Load conditions card opens a popup (`DrawJobPicker`) with every job by role (tanks, healers, melee, ranged, casters, crafters, gatherers), checkboxes and All / None per role, plus Clear (= every job). It edits `Load.JobIds`; "Add my current job" stays.
- "Add my current job" removed (the menu is enough). Job order as in the game: Blue Mage (36) last of the casters (after Pictomancer) and Beastmaster in the melee row after Viper; its id is looked up in the game's ClassJob sheet by abbreviation "BST" (`GameNames.JobIdByAbbreviation`), so no id is hardcoded. Beastmaster was added in patch 7.56 (melee DPS, limited job).

## Clicks without a window are now the only method
- Removed `UnitFrames.AlternateClicks`, the "Mouse (all frames)" card, the see-through click windows and the small NahjaUI right-click menu (`DrawUnitMenu`). `UnitFrameModule.HandleClicks` always uses `IsMouseHoveringRect` + `SetNextFrameWantCaptureMouse` + `OverrideGameCursor = false` while hovering (cursor of the game kept), left click targets, right click opens the game's menu (`OpenGameMenu`). Used by unit frames, party, nameplates and enmity list.

## Player: HP bar and Mana bar are separate (needs testing)
- Unit frames picker: "HP bar" (the old Player page) and "Mana bar" are separate pages. Each is on/off per job (`HideLifeJobs`/`IsLifeOn`, `ManaOnJobs`/`IsManaOn`) with a button per job by role (tanks, healers, melee + Beastmaster after Viper, ranged, casters + Blue Mage last, crafters 8-15, gatherers 16-18). Life defaults on for every job (new jobs included); mana defaults on for healers and casters.
- Mana has no placement any more: always its own bar ("unitframe.player.mana" in the Layout), independent of the Player frame's Enabled. Own look: size (layout), `ManaRoundingPx`, `ManaColor`, `ManaBackgroundColor`, `ManaBorderColor`. The old lists `ManaJobs`/`ManaOwnBoxJobs` are migrated once into `ManaOnJobs` (`MigrateManaLists`, called in the Plugin constructor).
- Crafters show CP and gatherers GP in the mana bar (`GameDataProvider.ResourceNow/ResourceMax` fill `UnitSnapshot.Mp/MaxMp`).
- The game's player bar (life + mana together) is hidden when "Hide the game's life and mana bar" is on and the HP frame or the mana bar is enabled.
- Beastmaster id: looked up in the ClassJob sheet by "BST" (`GameNames.JobIdByAbbreviation`). TODO: the Auras job menu has its own copy of the role list; unify.

## Sprint in the ability search
- `GameNames.UniversalActionIds` ({3 = Sprint}): general actions every job has but the game does not mark as player actions, so they were missing from the ability search and the job list. Add more ids there if other general actions with a cooldown are wanted.

## Job filter in the auras explorer
- The explorer of the Auras page has a "Show auras of" combo: my job (default; all jobs when there is no player), all jobs, or one job of the list (by role, with Beastmaster/Blue Mage in their place). An aura passes when its `Load.JobIds` is empty or contains the job. Groups with auras but none for that job are left out (empty groups always show). A line says how many auras are hidden. "New aura" while filtering by a job adds that job to its load conditions. Auras stay shared (no per-job profiles for now; decided: filter first, per-job profiles later only for layout positions / player bars if still wanted).

## New groups and loose auras start in the centre of the screen
- Cause of the "General group never shows" report: new groups were created at `OffsetY = 200 + groups.Count * 60`, which ends up off the screen after a few groups (the job-limit theory was wrong; all jobs were ticked).
- Now every new group and every new loose aura is created at the centre (offset 0, 0); auras used to go in rows of 10 (`120 + row * 50`), which could also leave the screen. Existing groups keep their saved position; a group already off-screen can be reset in the Layout page.
- "New aura" adds the filtered job only to loose auras (not inside a group). There is deliberately no "every job" button in the load cards (only Sprint is for all jobs); use the role All/None buttons of the job picker.

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
- The saved values keep counting downwards (screen coordinates), so no saved file changes. Only the editors turn the sign: unlock overlay "Y", Layout page "Offset Y", unit frame / party text and status row offsets (position, time, stacks), aura time/stacks (`PlacementControls`), nameplate name and title offsets.
- Rule for new editors: any vertical offset control shows `-value` and stores `-input`. Dragging in unlock mode is not affected.

## Nameplates: the title is part of the name block (replaces the earlier title options)
- No title options at all (no size, color, offsets, anchor, follow-game tick). Title text is drawn with the name's font, size and color, and moves with the name's position and offsets.
- Name + title are one block of two lines (title above or under the name as the game says, `GameTexts.TitleAbove`). The same point of the block goes on the chosen point of the bar. If the name is over the bar (anchor Y fraction < 0.5) the block grows UPWARDS when a title exists, so the extra line never covers the life bar; otherwise it grows downwards. Lines are aligned inside the block like the anchor (left / centre / right).
- Page order: Name card, Level card (no title card). Level has no placement of its own (after the name, enemies only).

- Removed the "Show the name" checkbox (`NamePlateSettings.ShowName`) of the Nameplates page: whether a name shows is only the game's option (flag bit 1). The cast bar page keeps its own `ShowName` (the spell name).

- Removed the nameplates "Hide the game's plate" checkbox (`HideNative`): it did the same as Enabled. Enabled on = our plates and the game's plates of the same kinds are hidden; Enabled off = the game's plates come back (the Enabled checkbox asks the game to redraw them).

- Removed `HideNativeCast` (nameplates): our cast frame on (Enabled + Show the cast frame) hides the game's enemy cast bar; off brings it back.
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

- CONFIRMED in game (2026-10-28): `UiConfig EnemyListCastbarEnable` 1/0 = cast bars on/off; `UiControl EnemyListDisp` 1/0 = enmity list shown/hidden; `UiConfig EnmityList` always read 0 (not used). Our list follows both (list hidden when Disp = 0, except the unlock-mode samples). The test line with the raw values was removed.

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
- Texts that mention an offset direction say +Y is up (the aura text controls said "+Y is down": fixed).

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

## Nameplates: target rules (agreed with Sandro)
- `ShowBarOnTarget` (default on): the target's plate always draws the life bar, even when the game's options would show only the name or nothing. Deliberate exception to "follow the game": it is a visible checkbox of ours. The name still follows the game's flags. Applies to what we draw (enemies, players when enabled).
- The target is marked by the accent ring around the bar (replaces the game's big arrow, `HideGameTargetArrow`, now a normal option; "_TargetCursor" works, confirmed).
- PLAN (next phase, not done): things WITHOUT a life bar (NPCs, doors, pets, objects) need text-only plates of ours; the target among them is marked with chevrons `‹ Name ›` pointing inward, in the accent color (U+2039/U+203A are inside the font ranges). Until that phase exists, hiding the game's arrow leaves those targets without any mark: keep the option off if that bothers.

## Nameplates: options removed (decided with Sandro)
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
- The game's big marker (quest marks...) now sits centered over the NAME BLOCK (title included) and on top of it; when the name is not drawn it falls back to the bar. No selectable anchor (a marker has one natural place): only size and offset X/Y (Y measured from the top of the name block; editor Y up, saved downward). Default Y is now 2 px (saved -2); Sandro will give the final value to use as the default for fresh installs.

- Final default for the marker Y: editor value -6 (saved +6: the icon's bottom edge 6 px below the top of the name block), tested by Sandro. -22 in the editor sat too low and covered the name.

## Nameplates: the name line is centered with its icon
- The small icon (and the level) are part of the name line: the width used to center/align the block is icon + gap + name + level, so the whole line is centered over the bar (before, only the text was and the icon hung to the left, which looked off-center). The title is aligned inside the same block.

## Enmity list: untargetable enemies are drawn half transparent
- Values read in game (Susano trial): a normal enemy shows act 1, lock 0, tgt 1 (whether targeted or not); when the enemy cannot be targeted (the game fades its row to ~50%) all three go to 0. `tgt` (IGameObject.IsTargetable) is the signal we use.
- Rows with `Targetable == 0` are drawn with 50% alpha (`UntargetableAlpha`) and are not clickable. Done with `Core/Ui/DrawListFade`: it multiplies the alpha of the vertices drawn since a mark (works for bar, border, texts and cast together). No option: we follow the game.
- The meaning of `act`/`lock` is still unknown (not needed). The "show the game's values" and "keep the game's list visible" test options can go when Sandro confirms the dimming.
- TO VERIFY IN GAME: the row really fades during the closed phase and returns to normal when it can be targeted again; no flicker.

- Enmity list: the enmity dot has its own size (diameter in px, default 12; before it was 22% of the bar height, about 8 px, too small). Fix: `DrawListFade.cs` was missing `using System;` (MathF).

## Target ring: no gap
- The accent ring of the target (enmity list rows and nameplate bars) was drawn with its rectangle pushed out by a full line width, but a line is centered on its rectangle, so a gap of half a line (about 1 px with a non-integer UI scale) showed the screen between bar and ring. Now the rectangle is pushed out by HALF the line and the line width is whole pixels, so the ring touches the bar.
- Reviewed every frame that draws a ring or a frame outside a bar: enmity list, nameplates and PARTY frames had the gap (fixed the same way in the three). The job gauge pip glow (offset = line width, line centered) already touches the pip border, and the aura borders and unit frames have no outside ring: nothing to change there. Rule for any new ring: push the rectangle out by half the line and use whole-pixel line widths.

- Party frames: the small arrow beside the targeted member was removed (our own invention, the game has none; it overlapped the buffs on the left). The ring around the member (same as the other frames) is the only mark.

## Target ring: one global color, always on
- `Configuration.TargetRingColor` (default WHITE; Appearance page > "Target ring color"; included in the Appearance block of profiles; "Reset to default" resets it too). The ring around the target is drawn with it on the enmity list rows, the nameplate bars and the party frames; the nameplate chevrons use it too. Reason: the same signal must look the same everywhere, and the old accent violet got lost on dark/blue backgrounds. (On very light backgrounds white can get lost: the user changes it.)
- Party frames: removed `SelectionRing` (the ring is always on) and `SelectionColor` (old saved values are ignored).

## Enmity list: out of range fade
- Seen in game (Fire Sprite far away): `act 0 lock 0 tgt 1`; the game fades the row a little, it is still clickable but cannot be selected because of the distance. So `act` (ActiveInList) very probably means "in range / selectable". Rows with `act 0` and `tgt 1` are drawn at 70% (`OutOfRangeAlpha`, an ESTIMATE of the game's lighter fade) and stay clickable; `tgt 0` stays at 50% and not clickable.
- TO VERIFY: that `act` flips with the distance (walk in and out of range), and tune 0.7 against the game's own row.
- Correction (Sandro): an out-of-range row (`act 0`, `tgt 1`) is NOT clickable in the game; only its hover glow shows. An untargetable row (`tgt 0`) shows neither glow nor click. So our rows are not clickable when `act 0` or `tgt 0`. We have no hover glow on the rows (the game's glow on targetable rows is not copied yet).

## Clicks: no right click menu where the game has none; target of target can be name only
- The game's right click menu is opened only on frames where the game has one (player, target, focus, party). The enmity list rows and the target of target now only react to the LEFT click (select). `HandleClicks(..., contextMenu)` has the new parameter (default true). The enmity list checkbox is now "Click on a bar to target".
- The target of target in the game does have a life bar and can be selected with the left click (checked by Sandro): it keeps its bar by default. New option `UnitFrameSettings.ShowFrame` (only used by the target of target page, "Show the life bar (off: only the name)"): off hides background, life, shield, border and the life number; the name stays at its place and the box is still the click area. Name color etc. unchanged for now.
- Also: those boxes are not covered/hidden by our drawing when the game's menu is open because the enmity list and target of target no longer open any menu.

## Terminology and foldable cards
- CODE NAMES FOLLOW THE GAME TOO (decision of the user, old saved configs/profiles may be lost, accepted): everything in code, ids and saved keys now uses the game's words: Enhancement/Enfeeblement (not Buff/Debuff), Hp (not Life), Mp (not Mana): `Enhancements`, `Enfeeblements`, `PlayerEnhancements`, `isEnhancement`, `HpColor`, `ShowHp`, `MpElement`, `"unitframe.player.mp"`, card ids `##card_pf_enhancements`, `##card_np_hp`... Exceptions that are NOT ours and must keep their name: game addon `_TargetInfoBuffDebuff`, `VtxBuffer`/`buffer`, `AddonLifecycle`, `...Manager` classes. WRITE ALL NEW CODE WITH THESE WORDS. The rules below that say "code names stay" are superseded by this line.
- TERMINOLOGY RULE: in everything the player reads, the life of a unit is called HP (the game's own word), never "life" or "health". Code names (LifeColor, ShowLife, card ids like `##card_np_life`) stay as they are; card ids must NOT change (the folded state is saved by id).
- Interface words (agreed with Sandro): the list on the left of the main window is the MENU (sidebar), the row at the top of a page (e.g. Parameter bar / Target / Target of target / Focus / Party / Class colors) are TABS, and each box inside a tab is a CARD.
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
- What NahjaUI has now: party frames with one buff row and one debuff row each (`PartyAuraSettings`: filter all/by id, extra ids always shown, only mine, anchor point and offset, icon size, crop, rounding, spacing, max icons, growth direction, timer and stack count with font/anchor), the same rows on player/target/focus frames (`StatusRowRenderer`; the native target statuses are hidden while ours show), tooltips, and the aura engine (separate system).
- NEXT: a Debug card that prints live the values above (arrays + config options) so the owner can flip the HUD Layout options in game and we see what changes; then decide the native-copy model (which groups, per frame) and only then the value-add (ideas: priority/size by importance, own first, dispellable highlight, group by category, filters per job).

## Enhancements and enfeeblements rework: decisions (user, 2026-10-29)
- The game's own HUD Layout > Status Effects options are RESPECTED, not ignored: single element, 3 groups or 4 groups (Enhancements / Conditional Enhancements (8 slots) / Enfeeblements / Others), each group placeable by the player, with the game's own display settings (Default / Prioritize own / Others' enhancements in group 4). Whoever wants the split can use it with no filter, like in the game.
- The user himself plays with ONE element (no split): all enhancements together at the game's natural place (top right, next to the minimap). He tracks the important ones with auras (a duplicated enhancement is fine). NEVER build an option to hide from the row the statuses an aura already tracks.
- Enfeeblements matter in raids: movable, centrable, able to grow from the center outward; stable order (by moment of application, not re-sorted by time each frame, because growth from the center moves every icon when one appears).
- Keep the game's RIGHT CLICK on an own status (it cancels removable ones like Sprint). `StatusManager.ExecuteStatusOff(statusId, sourceId)` is a static function in the clones, so it is possible. If our row replaces the game's, it must keep it.
- Focus: same philosophy as target/player; the user does not use the focus himself.
- Later idea (auras): a filter that shows all the important party enhancements (the 2 minute burst windows).
- Classification of a status: do not depend only on the HUD arrays; the status list plus the Status sheet Category (1 enhancement, 2 enfeeblement, else other) is independent of the HUD options. The HUD arrays are what tells in which group the game puts it.
- Debug page: card set "Statuses: ..." (`Windows/StatusDebugCard.cs`) prints live the HUD arrays (layout, 4 slot lists, icons, dispellable), target/focus statuses, party list statuses, game options (PartyListStatus, PartyListStatusTimer, BuffDispType, ColorBuffGive, ColorDebuffGive, TargetInfoSelfBuff and any typed name) and whether the guessed native windows (`_Status`, `_StatusCustom0-3`, `_TargetInfoBuffDebuff`...) are visible. Purpose: the user flips the game's options and reports what moves; then the copy model is decided.

## Status effects page (player enhancements and enfeeblements), first step
- New page "Status effects" (sidebar, after Unit frames), module `Modules/StatusEffects/` (`StatusEffectsSettings`, `StatusEffectsModule`, `StatusEffectsEditor`), config `Configuration.StatusEffects`. The player frame (HP/MP, "Parameter bar") NO LONGER has enhancement/enfeeblement rows; target and focus keep theirs for now (the user wants to do the player first and then see).
- `StatusEffectsSettings.Enabled` (default off): draws ours and hides the game's windows `_Status`, `_StatusCustom0..3` (names guessed from AgentHUD, to confirm in game: Debug > "Statuses: game windows").
- It FOLLOWS the game's layout option (read live from `HudNumberArray.StatusElementLayout`): OneElement -> group Single; ThreeElements -> Enhancements, Enfeeblements, Others; FourElements -> Enhancements, Conditional, Enfeeblements, Others. Each group = own layout element (`status.single`, `status.enhancements`, `status.conditional`, `status.enfeeblements`, `status.others`, top right by default), own `StatusGroupSettings` (Enabled, Columns, Justify Left/Right/Center, RowsGrowUp, and `Look` = a `PartyAuraSettings` used only for icon/time/stack look; its filters are NOT used: a group shows what the game puts in it, no filters).
- Group membership for now: by the Status sheet category (1 enhancement, 2 enfeeblement, else other), in StatusList order. The conditional group is empty until the HUD arrays are understood (the debug showed: in 3/4 groups the slot lists index the icon slots, in 1 element they do not; icons carry bit 0x40000000 which looks like "applied by me").
- Right click on an icon calls `StatusManager.ExecuteStatusOff(statusId, sourceId)` (the game checks it can be canceled). Tooltip as before (place "player").
- Layout box of a group: width/height are set by the module every frame from columns/rows/icon size; only the position is the user's.
- NOT done yet: profile export of this page, reset button, the order inside a group as the game orders it, the unknown extra statuses (icon not in the status list), dispellable mark.

## Giving the game's windows back (fix, 2026-10-29)
- Bug: when a module stopped hiding a game window (Enabled turned off) the window stayed invisible until a loading screen, because hiding sets `IsVisible = false` and the game never sets it back by itself.
- Fix: `Game/NativeWindowHider.cs`. `Hide(args)` (from PreDraw) skips the drawing, marks the window not visible and remembers it only if it WAS visible; `ShowAgain(stillHidden)` is called at the start of every module's `Draw()` (even if the module is off) and sets `IsVisible = true` for the remembered windows the module no longer wants hidden. A window the game itself had hidden is never made visible. Used by: unit frames, status effects, cast bars, job gauges, enmity list, party frames. EVERY NEW MODULE THAT HIDES A GAME WINDOW MUST USE IT.
- Known edge: if the game's layout changes while a window is hidden by us and the module is then turned off, the window may come back visible although the game wanted it hidden; the next HUD change of the game fixes it.

## TEXT SIZE = HEIGHT OF THE LETTERS (decision, all frames)
- Before, "12 px" meant the font's line box, which is much taller than the letters, so 24 on a buff timer looked tiny and 24 on a nameplate looked huge, and anchors (Top/Center/Bottom) used the invisible line box.
- Now the size of every overlay text is the HEIGHT OF A CAPITAL LETTER / DIGIT in logical pixels, and anchors use that visible box. 24 is 24 and 12 is 12 everywhere.
- Implementation: `OverlayFonts.PushCap(font, capPx)` returns a `TextFace` (Core/Ui/OverlayFonts.cs): it measures the cap ratio of the family from glyph 'H' (cached), picks the closest built font and draws with the exact size. `face.Width(text)`, `face.Measure(text)`, `face.Height`, `face.Draw(drawList, topLeftOfLetters, text, color[, outline])`. `Fit` helpers take the face.
- Converted: aura/status timers and stacks (AuraEngine.DrawOverlayText), unit frames text, cast bars, enmity list, job gauges, nameplates (name/title block uses a line gap of 0.45 x letter height, cast text). Menu and tooltip text stay as before (font size).
- `OverlayFonts.AvailableSizes` is now the list offered in the editors (6..48); fonts built are `BuiltOverlaySizes`.
- Defaults lowered to keep the old look (timer 12->8, cast bar 14->10, name 14->10, unit frame 16->11); saved values keep their number, so they now look bigger.

## Enmity dot and party job icon (decisions)
- The word "aggro" is gone everywhere: the game calls it enmity (`ShowEnmityDot`, `EnmityDotSizePx`, `EnmityDotAnchor`, `EnmityDotOffsetX/Y`; saved values of the old names are lost on purpose).
- The enmity list dot is placed like the texts: a point of the bar (anchor) + offsets; default Left, X 8, Y 0. The name starts after the dot only when the dot is in the left half.
- The party frame job icon no longer has a cap at the bar height: it is the size chosen (the name moves right by the icon width).

## Enmity list (rename) and clicks
- "Enemy list" is now "Enmity list" everywhere (names, files, folder `Modules/EnmityList`, layout id `enmitylist`, page, config `EnmityList`). Saved settings of the old names are lost on purpose. The game's own identifiers stay as they are: `_EnemyList` (window), `EnemyListNumberArray`, `EnemyListDisp`, `EnemyListCastbarEnable`.
- The option "Click on a bar to target" is removed: clicking a bar always targets the enemy (left click) like before when it was on.

## Text anchors: vertical centre on the anchor line (decision, texts and enmity dot only)
- Horizontal: the same point of the text and of the box meet (left-left, centre-centre, right-right). Vertical: the CENTRE of the letters sits on the anchor line (top edge, middle or bottom edge of the bar). So Top left = text centred on the top edge, Bottom left = on the bottom edge; no overlap between them even on a 6 px bar.
- Applies to unit frame texts (also used by the enmity list), aura timers/stacks, the nameplate name block and the enmity dot. NOT to the Layout (ScreenAnchor/SelfAnchor stay corner to corner). Saved offsets of Top/Bottom texts now look different.
- Fix (cap ratio), TESTED by Sandro: the ratio letter height / font size is measured with `ImFontPtr.FontSize` (size the font was built at), not `ImGui.GetFontSize()`. Before, letters came out about twice too big; with the fix they are the real size. Do not revert.

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

- Auras: a single aura now has "Icon width (px)" and "Icon height (px)" like a group (before: one "Icon size" that forced a square).

- Stack / charge text size: the "automatic = 3/4 of the time size" is gone everywhere (auras, party/target rows, Status effects). Everybody chooses the size; default 8, an old saved 0 counts as 8. Text sizes can now go down to 4 (before they were clamped at 8, which hid the new 6 and 7 px sizes).
- Aura Display card order: time (show, size, position), then stacks (show, size, position), then the font last.

## Unlearned abilities
- Cooldown auras whose ability has a learn level (Action sheet `ClassJobLevel`) above the player's current level are hidden. The player level is the game's effective level, so level sync in dungeons/FATEs is covered. No option (minimalism). Unlock mode still shows them as ghosts so they can be placed.
- Status auras are not tied to an ability, so they are not filtered (a status that is not there is simply absent; only "show when missing" reminders could appear).

## "Hide the game's ..." options stay
- Decision: the option to hide the game's own element (cast bar, etc.) is KEPT, on by default. Reason: showing both at once is useful to compare the game with the addon. Do not remove these options.

## Cap-height ratio cache
- `PushCap` measures a family's cap-height ratio only with that family's OWN font (never with a fallback while it loads); otherwise a wrong ratio stayed cached for the whole session and every text of that family came out the wrong size.

## Target cast bar has no icon
- The game's target cast bar shows no spell icon, so ours never does and has no option for it (only the player cast bar has "Show the spell icon").

## Job gauges page: one job at a time
- The page shows the gauge of the job being played by default, with a "Job" combo to edit another job's gauge. Picking the current job goes back to following it. New jobs are added to `Entries` in JobGaugeEditor.

## Parameter bar pages: jobs card last
- HP page: the "HP bar on each job" card is the last one. MP tab: the MP bar card, the MP text card, then "MP bar on each job" (with "Show the MP bar") last. Same shape on both.

## Name icon only on enemies and players
- The small icon at the left of a nameplate name is drawn only for Enemy/Self/Party/OtherPlayer plates. Friendly battle NPCs (FATE turn-in NPCs, pets) hold leftover values in that slot (hostile or class icon) that the game never shows.

## Ability search finds combo steps
- `SearchActions` also accepts actions that belong to a job and have a learn level even if the sheet does not mark them as player actions (Riot Blade was not found by the search).

## Duplicate an aura
- Right click on an aura row in the Auras list opens a menu with "Duplicate". The copy (all settings, name + " (copy)", new id) goes right below the original, in the same group or at the top level, with the same size and position. Done after the list is drawn (`pendingDuplicate`).

## Ability search includes non-hotbar actions
- `SearchActions` no longer requires the action to be a player action: Blade of Faith/Truth/Valor cannot be on a hotbar but are combo steps (their combo parent is Confiteor / the previous blade). Track them with a cooldown aura on the blade + "Only while it is the next step of my combo". Job actions are listed first.

## Debug: Combo card
- The Debug page shows the combo the game reports (now and last seen) and what Blade of Faith/Truth/Valor follow, to diagnose combo-step auras.

## Combo steps that are forms of another action
- Confiteor does NOT start the game's combo timer (Debug > Combo showed no combo after Requiescat + Confiteor). Blade of Faith/Truth/Valor are forms the Confiteor button turns into. A combo-step aura is therefore shown when EITHER the game's combo is on the parent step, OR the first action of its chain (`GameNames.ComboRoot`, watched too) currently has the aura's action as its adjusted form. In the second case there is no timer.

## Debug: Action flags card
- Lists the job's actions the game makes glow (`IsActionHighlighted`) or that are in another form (`GetAdjustedActionId`), to check what can be read for a future "ability available" aura type (idea: 3 aura types: status, cooldown, ability the game offers now; optional timer from a status or from the combo timer).

## Three aura types + free counter
- Aura types: **An enhancement or enfeeblement** (Status), **An ability cooldown** (Cooldown), **An activated ability** (Activated): shown only while the game turns the ability on. Offered = the game's glow on the current form of the ability (`IsActionHighlighted`), or, for a combo step (has a combo chain), the game's combo is on the previous step OR the first action of its chain (Confiteor) has turned into it (form). Cover/Clemency/Intervention report glow always (the flag is not a proc for them): not for this type.
- "Number on the icon" (`Counter`): Standard (status time / cooldown / ability cooldown), No number, The combo time, A status I choose, The cooldown of an ability I choose. Works for all three types; a chosen counter removes the recharge sweep.
- `OnlyAsComboStep` and the checkbox are gone; old auras that had it become Activated on load (the field is kept only for that migration).

## Recommended use of "An activated ability" (tested in game)
- Track the BASE ability (Confiteor, Atonement): one aura. It is shown while the game glows its current form and the icon changes with the form (Blade of Faith, Truth, Valor; Supplication, Sepulchre). No need for one aura per step.

## Aura editor redesign (agreed plan, in 3 deliveries)
1. (done) General card: Name, Enabled, **Trigger** (no group selector: the left tree does it). Right click on an aura row: Duplicate and Delete. The "Delete aura" button at the bottom of the page stays too (duplicated on purpose).
2. Texts: a list of texts on every aura, each with its own source (cooldown of another ability, combo time, status time/stacks, ...) and the usual text controls (size, position, offset, font). Replaces "Number on the icon" and the timer/stack text options.
3. Display merged with the primary trigger: width, height, crop, corner rounding, glow. Cooldown auras keep the recharge look and sweep. "Effect while active" stays on the primary trigger (its time can also be a text). Groups control icon size, crop and rounding; texts are per aura.

## Aura texts (delivery 2)
- Every aura has a list of texts: Text 1, Text 2... (`AuraDefinition.Texts`, `AuraTextDefinition`). Each: source (own time, own stacks/charges, combo time, status time/stacks of a chosen status, cooldown/charges of a chosen ability), size (cap height), position + offset, font, colour. "Add text" button after the last card; "Remove this text" in each card.
- The old timer/stack text options of the Display card, and the old "Number on the icon" counter, are gone. Auras saved before get their timer/stack text converted into texts once (`TextsMigrated`).
- A text with nothing to show (no time left, no stacks) draws nothing. Texts are drawn only when the aura is really shown (not on unlock ghosts).
- The context menu of an aura row has Enabled (first), Duplicate and Delete. In the General card, Enabled is the first control.

## Aura editor order and defaults (after delivery 2)
- Page order: General, Ability to track / Status to watch (with "Effect while active" for cooldowns), Display, Text 1..n (+ Add text), Load conditions, Delete aura.
- New auras start with ONE text (own time, centre, offset 0,0). "Add text" adds a text at the bottom right, offset 0,0.
- Context menu of an aura: Disable/Enable (one toggling entry), Duplicate, Delete.
- "Effect while active" stays: one icon shows both phases of an ability that gives a buff (buff time while it lasts, then the cooldown), with its own glow/recharge looks. Texts cannot do that phase change.

## "Effect while active" removed (cooldown auras)
- Reason: the button of an ability can change form when used (Fight or Flight turns into Goring Blade), so the aura could not follow its own buff. Cooldown auras now show only the cooldown. Buffs are tracked with status auras, or shown as a text (source: time of a status I choose).
- Old cooldown auras with an effect: the effect becomes a text (status time, top right); extra effects are dropped.

## Icon of an aura can differ from what it tracks
- Display card, "Icon": the icon of what it tracks (default), the icon of a status I choose, or of an ability I choose (`AuraDefinition.Icon`, `IconStatusId`, `IconActionId`; `AuraEngine.ResolveIcon`). Reason: the icon of the Requiescat status (1368) has its stack number drawn in it; tracking 1368 with the icon of 1369 (no number) and showing the stacks as our own text. No automatic icon-by-stacks (decided: the user prefers own typography and free placement).

## Prioritized triggers (WeakAuras style) — replaces the single aura Trigger
- An aura has an ordered list of **icon triggers** (`AuraDefinition.Rules`, `AuraRule`). They are tried in order; the FIRST that matches decides the icon, the glow and the values of "the trigger that decides". None matching hides the aura. Evaluated in `AuraEngine.Evaluate` → `Match` (`AuraState.Rule` is the one that matched).
- Conditions (`ConditionKind`): StatusPresent, StatusMissing (unit: player/target/focus/target of target, "only mine"), AbilityActivated (glow or form/combo step, as before), AbilityOnCooldown, AbilityReady, AbilityAlways (ready or recharging). An ability not yet learned (level, sync) never matches.
- Each trigger keeps its OWN icon choice (automatic / status I choose / ability I choose) and its own glow flags (Glow; Glow when ready for "always"; Glow when the game highlights it) and "Ignore the global cooldown". Example: Requiescat = trigger "status 1368 on", icon of status 1369, stacks as own text.
- Each **text** has an ordered list of sources (`AuraTextDefinition.Sources`, `AuraTextSource`): the first source that gives a value is drawn. Sources: time / stacks-or-charges of the trigger that decides, combo time, time/stacks of a chosen status, cooldown/charges of a chosen ability.
- Worked example (Fight or Flight / Goring Blade): triggers 1) Goring Blade activated, 2) Fight or Flight status on, 3) Fight or Flight cooldown (always). Text 1 sources: FoF status time, then FoF cooldown.
- Editor: page order General (Enabled, Name), Icon trigger cards, Display, Text cards, Load, Delete. Order is changed with ↑/↓ arrow buttons in each trigger / source. Drag-and-drop reordering is the NEXT delivery (user wants both). A trigger list always keeps at least one trigger.
- Display card keeps size, crop, rounding and (if any ability trigger) "While recharging" look and recharge sweep, which are per aura / per group.
- Migration (`MigrateRules`, flag `RulesMigrated`): old Status → one StatusPresent/StatusMissing rule; Cooldown → AbilityAlways/OnCooldown/Ready; Activated → AbilityActivated; old icon choice and glow flags move into the rule; each old text source becomes the first entry of `Sources`. The old fields stay in the class only for this migration.

## Auras explorer: anchored header, grab anywhere
- The header (title, New group, New aura, "Show auras of" label ABOVE its combo) is fixed; only the list below scrolls (child `##aura_explorer_list`).
- No grip dots: any point of a row (aura or group) grabs it to drag and reorder. Double click on a group opens/closes it (chevron still works).
- Icon triggers can also be reordered by dragging: press on any empty point of a trigger card (not on a control) and drag; a line shows the slot. Arrows stay. Text sources keep arrows only.

## Fill the width (aura icons)
- Display card: "Fill the width (cut the icon's side margins)" (`FillWidth`, copied by groups). Status icons (24x32) have an empty/dark margin at the sides that does not fill a wide box. `Game/IconMargins` reads the icon image from the game files once per icon, finds the first columns (left and right, max 30 %) that hold art (opaque and not almost black, middle rows only), and `IconUv` cuts them on top of the crop. If an icon cannot be read it is drawn whole.

## One card per chain ("if not" chains)
- Icon triggers: ONE card "Icon triggers": "Trigger 1", then "If not, trigger 2", "If not, trigger 3"... Drag by an empty point of a section, or arrows.
- A text is ONE card with its own chain: "Text from" (source 1), "If not, from" (source 2)... The first source with a value is drawn and the rest are not. A second value at the same time (for example stacks) needs another text (Text 2) with its own chain. "Add a fallback source" adds a link to the chain.
- Same look for both chains: heading "Trigger 1" / "If not, trigger N" and "Source 1" / "If not, source N" (only when there is more than one), a separator between links, arrows + Remove on each, and a normal button at the end ("Add trigger" / "Add source").
- IconMargins reads `TexFile.ImageData` (already decoded, 4 bytes per pixel); `GetRgbaImageData` does not exist in the Lumina used.
- `FillWidth` is ON by default (new auras, groups and auras saved before the option existed).

## Ability picker of a trigger: search only
- No list of the job's abilities. One box "Search ability" (starts with the chosen ability's name). The matches show only while the box has the focus (or the mouse is over them, so the click lands) and not after a pick; picking writes the ability name, as in the game, into the box.
- Status picker of a trigger works like the ability one: no list of current statuses, one "Search status" box with matches while it has the focus, the name is copied into the box on pick.
- Right click on the title bar of the main window: menu with "Center the window" (same as Appearance).
- Known issue to investigate: a text box in the UI sometimes loses the keyboard focus when the game selects an object. Nothing in our code asks for focus (no SetKeyboardFocusHere / SetWindowFocus) and we only draw one ImGui window plus the layout overlay windows.
