FFR Randomizer
==============

Shuffles chests, enemy drops, steals, shop stock, all 26 Visions and their
abilities, the characters' moves, finishers and stats, gear stats and
passives, the enemies in random encounters, their weaknesses and passives,
the bosses and the music in Final Fantasy Resonance, from a seed you can
share. Runs in memory through UE4SS: no game files are changed, and removing
the mod undoes it.

Install
-------
1. Run FFRRandomizer-Setup.exe. It finds the game through Steam; if not, click
   Browse. It installs the mod into the game's ue4ss\Mods folder and the
   FFR Randomizer app, with a Start menu shortcut. UE4SS (a build for Unreal
   Engine 5.6) is needed; if the game has none, tick "Install UE4SS
   4.0.0-rc1 too" and Setup adds it (MIT license, see ue4ss\LICENSE).
2. Open FFR Randomizer, pick a preset or your own options, then click
   Create Randomizer File.
3. Start the game and begin a New Game.

The app sorts its options into three tabs: We Are the Light (the fair
randomizer), We must BECOME Chaos (wilder, still finishable) and We are
Chaos (no guard rails, with a warning before the file is made).

Presets set every switch in one click: WoL Mode (items, Visions and
enemies), Dark Knight Mode (every shuffle that keeps the game's rules) and
True Chaos Mode (everything, may be unwinnable). The Rewards sliders on the
first tab scale EXP, Gil and MRP from 0.25x to 10x; presets leave them be.

Music, also on the first tab: Off keeps the game's own tracks, Shuffle
swaps battle tracks among battles and field and town tracks among theirs,
and Mixed puts them all in one pool. Every place a track played gets the
same new one. Airship and chocobo music don't change.

Keep the same Randomizer File for the whole playthrough. To change seeds,
make a new file and restart the game.

Running Setup again updates the mod and keeps your Randomizer File.

Portable app
------------
Portable\FFRRandomizer.exe is the same app Setup installs, on its own. Run
it from anywhere, without installing; it needs the mod already in the game.
To update, you can swap in the new exe instead of rerunning Setup. If the
app says the installed mod is too old, run Setup once to update the mod.

Sharing and checking a run
--------------------------
The settings code under the seed holds the seed and every option in one
line, such as FFR2-0631G07ZQZ-49353958654. Codes from 0.7.0 (FFR1) and
0.8.0 still load. Copy sends it; Paste code loads a
friend's code, then Create Randomizer File saves it.

After the game starts, the card above Create shows whether it applied your
file, how many changes it made and any errors, plus a seed check of four
words. Two players with the same seed check have the same run. The card
also says when the game is still on an older file and needs a restart.

Enemies and bosses
------------------
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
- Supports stay supports: Fina, Leah, Ayaka and Y'shtola keep at least 5
  healing abilities.
- Finishers: the 26 finishers trade places. Works without Abilities.

Characters
----------
All off by default; turn them on in the app.
- Character moves: level-up moves swap among characters of the same attack
  type. Each character keeps how many moves they learn and at which levels.
  Lid's Mechabo moves and Archwitch Fina's Witching Hour stay put.
- Character finishers: finishers swap the same way. Lid keeps his.
- Mix character moves: moves and finishers cross between Physical and Magic
  characters.
- Random character stats: each character's Attack, Defence, Intelligence,
  Mind and Agility trade growth curves. Totals, HP and MP stay the same.
Borrowed moves work, but their animations can look odd on someone else.

Gear and enemies (We must BECOME Chaos)
---------------------------------------
- Gear stats: gear of one kind trades stat sets. Prices stay.
- Gear passives: passives swap among weapons, armor or accessories.
- Enemy weaknesses: each enemy's element weaknesses and resistances trade
  places, enemies and bosses alike.
- Status resistances: regular enemies resist different ailments. Bosses
  keep theirs.
- Enemy passives: passives swap among regular enemies. Bosses keep theirs.
- Encounter rate: Normal, Half or Double.

We are Chaos
------------
These can make a run unwinnable. The app asks before it saves a file with
any of them on.
- Fully random stats: every stat curve, HP and MP too, comes from any
  character. No budget.
- Swap gear types: characters trade weapon and armor types. Starting gear
  the new types can't use may come off.
- Enemies at full strength: swapped enemies and bosses keep their own stats
  but still give the spot's EXP, Gil, drops and steals.
- Shops sell anything: any item with a price can turn up in any shop.
- Story items in the shuffle: key and quest items move too. Can softlock
  the story.

Spoiler log
-----------
The game writes Spoiler_<seed>.txt into the FFRRandomizer mod folder the first
time it starts with a seed. It lists where every item, shop slot and Vision
went, which enemy and boss replaced which, each Vision's abilities and
finisher, each character's moves and stats, the gear and enemy changes,
and which track replaced which. Open it from the app with Open spoiler log.

Uninstall
---------
Windows Settings > Apps > Installed apps > FFR Randomizer > Uninstall.
This removes the app and the FFRRandomizer mod folder. UE4SS stays, unless
Setup installed it; then its files go too, and other mods you added stay.
