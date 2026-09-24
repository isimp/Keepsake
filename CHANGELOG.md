# Changelog

## 0.4.0

Files and folders in BepInEx/config can be kept whole, from the new Files place in the panel. Keepsake saves a copy while the game runs and puts it back at launch when a profile sync replaced or removed the file, so a mod that keeps state in files, such as Seasonality's timer for each world, carries on where you left it. The Files list leaves out images unless asked, and shows a text file whole in a viewer of its own, or the image itself.

Keep all over a mod's settings keeps every one of them at once, and Release all releases them again.

## 0.3.0

Profile changes to values you keep now wait until you answer them, across launches, and a line in the corner says so once your character appears. General/ProfileChangeNotice turns that line off.

Settings the profile changes all the time, such as a volume or a window size, can be made quiet: their changes are recorded without asking.

The lists tag kept settings as updated, quiet, or same when your value is the profile's.

Release not loaded, over the kept settings, releases those of mods that are gone.

Launching is faster on large profiles, dragging a config manager's slider no longer rewrites keepsake.pins every frame, changes made elsewhere no longer close an open dropdown in the panel, and the panel reopens with its search and scroll where you left them.

## 0.2.0

When the profile changes a value you keep, the panel says so. The setting shows up under Profile changed on the left, marked as updated, with what the profile changed it from and to. Use the profile's takes the new value and keeps the setting, Keep mine stays with yours.

Keep all over the list of settings changed this session keeps every one of them at its current value.

Files left half written by a game that stopped in the middle of saving are removed at the next launch.

The log no longer says keybinds are waiting for Bindrune to take them over in the session Bindrune took them.

## 0.1.0

First release.
