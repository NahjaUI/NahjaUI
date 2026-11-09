# Color inventory (colors1 plan)

Goal (Nahja): the shared colors live in **Appearance** and apply to the whole interface. Every place that had its own picker gets a
"Custom color" checkbox (off by default = uses the Appearance color; on = shows its own picker). The menus get shorter and nobody loses the choice.

Numbers come from a search of the editors: about 46 color pickers in the editors, 19 of them "background", 20 of them "border", 8 of them text.

## Where the colors are today

| Menu | Colors there |
|---|---|
| Appearance (already) | Accent (menu only), Target ring, Role colors (Tank, Healer, Damage, Disciples of the Hand, Disciples of the Land) |
| HUD bars > Parameter bar > HP | Background, Border, Shield; text colors (name, HP numbers) |
| HUD bars > Parameter bar > MP | MP color, Background, Border; text color (MP numbers) |
| HUD bars > Target, Target of target, Focus | Friendly NPC HP, Enemy HP, Pet / minion HP, Object, Background, Border, Shield; text colors |
| HUD bars > Party List | Background, Border, Shield, MP color; Timer color and Stacks color of the status rows; Dispel border |
| Progress bars > Player / Target | Bar color, Slidecast, Interruptible, Not interruptible, Background, Border |
| Progress bars > Global Cooldown Bar | Bar, Queue window, Weave limit, Background, Border |
| Nameplates | Name text color, Level color, Background, Border; cast bar: Interruptible, Not interruptible, Background, Border |
| Enmity list | HP, Background, Border, Enemy is attacking me, Enemy is not attacking me; cast bar: Interruptible, Not interruptible, Background, Border |
| Job gauges | Per job: its own colors (Lily, Blood lily, Full frame, Aetherflow, Faerie, Seraph, Astral/Umbral Draw, Lord/Lady of Crowns, Addersgall, Addersting, filling/full/empty bars, cartridges, "from 50 up", marks...) plus Background and Border on almost every gauge |
| Tooltip | Name, Description, Details, Background, Border |
| Trackers | Text color of every text of every tracker |

## Proposal

### Goes to Appearance (global, with "Custom color" where it is used)
1. **Background** (HP, MP, cast bars, GCD bar, gauges, nameplates, enmity list, tooltip) - 19 pickers become 1.
2. **Border** - 20 pickers become 1.
3. **Text** (frame texts, nameplate name, tracker texts, tooltip name) - 1 global; the per-text picker becomes "Custom color".
4. **Shield** (HP bars, party list).
5. **MP** (player MP bar, party list MP).
6. **Cast: can be interrupted** (progress bars, nameplates, enmity list).
7. **Cast: cannot be interrupted** (same three places).
8. **Highlight** (the gold: marks, glows, "ready" frames, levels, dispels).

Already there and stay: Accent, Target ring, Role colors.

### Stays where it is (it only means something there)
- Friendly NPC, Enemy HP, Pet / minion, Object (HUD bars).
- Enemy is attacking me / not attacking me (enmity list).
- Slidecast, Queue window, Weave limit (progress bars).
- Every color that is a part of one job gauge (Lily, Aetherflow, Astral Draw...).
- Timer color / Stacks color of status rows, Level color (nameplates), Tooltip description and details colors: to decide (they are texts: probably "Custom color" over the global text).

### "Custom color" behavior
- Off (default): the color is the one of Appearance, so changing it there changes everything that does not have its own.
- On: the picker appears; only that place changes.
- Migration: what the user has already tuned must not be lost. A saved color equal to the old default becomes "off"; a different one becomes "on" with that color.

## Order of work (proposed)
1. Appearance gets the global colors (1-8) and the settings that hold them.
2. A reusable "color with Custom color" control, and the migration.
3. Move the editors one by one (HUD bars first), removing the pickers that now come from Appearance.
4. Text color: global + "Custom color" in every text card.
