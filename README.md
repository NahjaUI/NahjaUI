# NahjaUI

A complete, modular interface for Final Fantasy XIV, as a [Dalamud](https://github.com/goatcorp/Dalamud) plugin.

You build it piece by piece. Switch on only what you want: everything else stays exactly as the game made it.

> **Early version.** NahjaUI is in an early test phase. Things can break, and your settings may be lost between versions. Please report what you find (see below).

## What it has

- **HUD bars:** parameter bar (HP and MP), target, target of target, focus target and party list.
- **Progress bars:** your casts and your target's, with a slidecast marker, plus a global cooldown bar that shows the queue and weave windows.
- **Job gauges, status effects, nameplates and enmity list**, each one with its own style.
- **Trackers:** pick the enhancements, enfeebling effects and cooldowns you want to see. Every job comes with ready-made presets.
- **Profiles:** save, copy and share your whole setup.
- **Shared style:** one set of colors and one corner rounding for the whole interface, with its own font family (Saira).

## Install

1. In the game, open the Dalamud settings: type `/xlsettings`.
2. Go to **Experimental** and find **Custom Plugin Repositories**.
3. Paste this address, then click the **+** button and **Save**:

   ```
   https://raw.githubusercontent.com/NahjaUI/NahjaUI/master/repo.json
   ```

4. Open the plugin installer (`/xlplugins`), search for **NahjaUI** and install it.

## First steps

- Type `/nui` to open the window.
- Type `/nui unlock` to move the elements of the interface wherever you want. Use it again to lock them.
- In **Trackers**, open the presets button to load the ready-made trackers for your job.
- Turn on only the parts you want; the game keeps drawing everything you leave off.

## Report a problem

Please open an issue here: <https://github.com/NahjaUI/NahjaUI/issues>

It helps a lot if you tell us:

- what you were doing (job, level, duty or open world),
- what you expected and what happened instead,
- a screenshot of the window or of the part of the interface that fails,
- the plugin version (shown in the plugin installer).

## Build it yourself

You need the .NET SDK and a Dalamud development install (the game launched at least once with XIVLauncher). Open `NahjaUI.slnx` in Visual Studio and build the **Release** configuration.

## Credits and licenses

- NahjaUI is free software under the **GNU Affero General Public License v3**. See [LICENSE.md](LICENSE.md).
- The interface font is **Saira** and **Saira Condensed**, used under the SIL Open Font License 1.1. See [NahjaUI/docs/FONT_LICENSE.txt](NahjaUI/docs/FONT_LICENSE.txt).
- NahjaUI is a fan-made tool and is not made, endorsed or supported by Square Enix. FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd.
