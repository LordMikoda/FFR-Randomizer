FFR Randomizer 0.2.0
====================

Shuffles chests, enemy drops, steals, shop stock, all 26 Visions and the
enemies in random encounters in Final Fantasy Resonance, from a seed you can
share. Runs in memory through UE4SS: no game files are changed, and removing
the mod undoes it.

Install
-------
1. Install UE4SS for Final Fantasy Resonance (a build that supports Unreal Engine 5.6).
2. Copy the FFRRandomizer folder into:
   FINAL FANTASY RESONANCE\FFRS\Binaries\Win64\ue4ss\Mods\
3. Run FFRRandomizer.exe. It finds the game through Steam; if not, click Browse.
4. Pick a seed and options, then click Create Randomizer File.
5. Start the game and begin a New Game.

Keep the same Randomizer File for the whole playthrough. To change seeds,
make a new file and restart the game.

Enemies (experimental)
----------------------
Off by default; turn on Enemies in the app. Random encounter enemies swap
one-for-one with enemies of the same size. Each swapped-in enemy takes the
level, HP, stats, EXP, Gil, drops and steals of the one it replaced, so every
area plays and pays as before. Story and side-quest battles and bosses are
not changed.

Spoiler log
-----------
The game writes Spoiler_<seed>.txt into the FFRRandomizer mod folder the first
time it starts with a seed. It lists where every item, shop slot and Vision
went, and which enemy replaced which. Open it from the app with Open spoiler
log.

Uninstall
---------
Delete the FFRRandomizer folder from ue4ss\Mods.
