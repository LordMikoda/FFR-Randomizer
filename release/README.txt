FFR Randomizer
==============

Shuffles chests, enemy drops, steals, shop stock, all 26 Visions and their
abilities, the enemies in random encounters and the bosses in Final Fantasy
Resonance, from a seed you can share. Runs in memory through UE4SS: no game files are
changed, and removing the mod undoes it.

Install
-------
1. Install UE4SS for Final Fantasy Resonance (a build that supports Unreal Engine 5.6).
2. Run FFRRandomizer-Setup.exe. It finds the game through Steam; if not, click
   Browse. It installs the mod into the game's ue4ss\Mods folder and the
   FFR Randomizer app, with a Start menu shortcut.
3. Open FFR Randomizer, pick a seed and options, then click Create Randomizer File.
4. Start the game and begin a New Game.

Keep the same Randomizer File for the whole playthrough. To change seeds,
make a new file and restart the game.

Running Setup again updates the mod and keeps your Randomizer File.

Enemies and bosses (experimental)
---------------------------------
Both are off by default; turn them on in the app. Random encounter enemies
swap one-for-one with enemies of the same size, and bosses swap with other
bosses. Each swapped-in enemy or boss keeps its own abilities and takes the
level, HP, stats, EXP, Gil, drops and steals of the one it replaced, so every
fight plays and pays as before. Lose-on-purpose and multi-phase boss fights
are not changed.

Vision abilities
----------------
All off by default; turn them on in the app.
- Abilities: each Vision's abilities and passives swap with other Visions'.
  A Vision keeps its number of each and the level it learns them at, never
  gets the same ability twice, and never gets two versions of one stat
  bonus, such as Speed +10% and Speed +20%.
- Mix Physical and Magic: off keeps Physical and Magic Visions in separate
  pools; on puts all 26 together.
- Supports stay supports: Leah, Ayaka and Y'shtola keep at least 5 healing
  abilities.
- Finishers: the 26 finishers trade places. Works without Abilities.

Spoiler log
-----------
The game writes Spoiler_<seed>.txt into the FFRRandomizer mod folder the first
time it starts with a seed. It lists where every item, shop slot and Vision
went, which enemy and boss replaced which, and each Vision's abilities and
finisher. Open it from the app with Open
spoiler log.

Uninstall
---------
Windows Settings > Apps > Installed apps > FFR Randomizer > Uninstall.
This removes the app and the FFRRandomizer mod folder. UE4SS stays.
