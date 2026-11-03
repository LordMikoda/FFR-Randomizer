![FFR Randomizer](art/banner-1300x372.png)

# FFR Randomizer

A seeded randomizer for **Final Fantasy Resonance** on PC. It shuffles chests, enemy drops, steals, shop stock and all 26 Visions. Share a seed and a friend gets the same run.

It's a UE4SS Lua mod that changes the game's tables in memory while the game runs. No game files are changed, and removing the mod undoes it.

**Download:** get `FFRRandomizer-<version>.zip` from [Releases](https://github.com/LordMikoda/FFR-Randomizer/releases).

## Features
- **Items:** chests, drops and steals swap places, so the overall mix of items stays the same. Key items, Tablet Shards and every item a quest or event needs stay where the game puts them.
- **Chest quantity:** keep each chest's own count, or roll 1 to 9 or 1 to 50. Weapons, armor and accessories always stay at 1.
- **Shops:** every shop keeps its categories and sells random items at the game's own prices. Quest items never show up in shops.
- **Visions:** all 26 Visions swap among their rewards, including Sanctum, subquest and story Visions.
- **Spoiler log:** a text file listing where everything went.

In development on `main`, off by default: an enemy randomizer for random encounters and a boss randomizer. Both give the new enemy or boss the stats, EXP and Gil of the one it replaced.

## Install
1. Install [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) for the game (a build that supports Unreal Engine 5.6).
2. Run `FFRRandomizer-Setup.exe` from the release zip. It finds the game through Steam, installs the mod into `ue4ss\Mods\FFRRandomizer` and the FFR Randomizer app, with a Start menu shortcut.
3. Open FFR Randomizer, pick a seed and options, then click **Create Randomizer File**.
4. Start the game and begin a New Game.

Keep the same Randomizer File for the whole playthrough. Uninstall from Windows Settings > Apps > Installed apps.

### Manual install
Copy `mod/FFRRandomizer` into `FINAL FANTASY RESONANCE\FFRS\Binaries\Win64\ue4ss\Mods\`. Without the app, the mod picks a seed on first launch and writes it to `Randomizer.txt` in that folder; edit that file to change the seed or options (`items`, `shops`, `visions`, `enemies`, `bosses`: `on` or `off`; `chest_quantity`: `default`, `9` or `50`).

## Repository
| Folder | What's in it |
|---|---|
| `mod/FFRRandomizer` | The UE4SS mod. `Scripts/main.lua` builds and applies the seeded plan; `ItemNames.lua` and `EncounterGroups.lua` are generated lookups. |
| `gui` | The app (WinForms, C# 5). `build.cmd` compiles it with the compiler that ships with .NET Framework 4.8, so nothing needs installing. |
| `installer` | `FFRRandomizer-Setup.exe`, also built with the .NET Framework compiler. |
| `tools` | `make_release.ps1` builds a release zip; `make_art.ps1` draws the icon and banners; the Python scripts regenerate the Lua lookups from a data table export (not included). |
| `art` | Icon and banners. |

Build a release:
```
powershell -ExecutionPolicy Bypass -File tools/make_release.ps1 -Version 0.1.1
```

## License
MIT, see [LICENSE](LICENSE). Final Fantasy Resonance and its item names belong to their owners; this project isn't affiliated with them.
