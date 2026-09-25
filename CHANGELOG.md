# Changelog

## 0.5.0

What you kept now survives Update existing profile in Thunderstore Mod Manager and r2modman. For their profiles Keepsake offers to keep a spare copy next to the profiles folder, asking once before it writes anything there, and brings it back at the first launch after an update, saying so in the corner. The Spare copy button in the panel changes the answer.

Nothing Keepsake replaces or releases is lost any more. The version it replaces is set aside first, and a kept file's detail lists its last five earlier versions to look at and put back. Keepsake's own lists are set aside at each launch too.

A Keepsake file held open by another program is never written over, and keeping, releasing or setting a value says so when it cannot be saved instead of looking done. Writing a kept value keeps a cfg file's byte order mark, and a line in keepsake.pins pointing outside BepInEx/config is skipped.

## 0.4.2

Kept files now hold through modpack installs and updates in any mod manager. Mod managers give the files they extract the time stored in the zip, which made a modpack's file look older than your own, so it was taken as yours; after a game Keepsake saw close, any change is now put back, whatever time the file carries.

The README lists which ways of updating a profile keep what you kept, and how to carry it over to a profile imported as new.

## 0.4.1

Kept files are copied as the game closes instead of every ten seconds. At launch, a kept file that differs from its copy is judged by when it was written. Written during the last game, it is yours and its copy is updated. Written after the game closed, a profile sync replaced it and your copy goes back. Before, an outdated copy could be put back over a newer file after a crash, after a game in which Keepsake did not load, or when Keepsake was turned back on after a while.

When Keepsake cannot tell, after a game it did not see close, the file is left as it is and waits in Files for you to put your copy back or keep the file as it is. Both versions are shown one over the other to compare: texts in viewers with the line where they first differ, images as thumbnails.

Lists count numbers in names as numbers, so 2 comes before 10, for files as well as mods, sections and settings.

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
