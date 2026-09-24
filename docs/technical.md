# Technical notes

## Why a profile sync overwrites settings

A profile sync in a mod manager such as Gale uploads everything under `BepInEx/config`, plus files with the extensions `cfg`, `txt`, `json`, `yml`, `yaml` and `ini` anywhere in the profile. On the receiving side each uploaded file replaces the local copy, and config files the sender does not have are deleted. Pulls happen when the game is launched, before it starts. So a setting changed on a following profile lasts until the next launch.

## How Keepsake keeps a value

Keepsake has two parts.

`Keepsake.Preloader.dll` is a BepInEx preloader patcher. It patches nothing: BepInEx calls `Initialize` on every patcher before any plugin loads, and that is all it uses. It reads `BepInEx/keepsake.pins` and writes each kept value into its mod's cfg file, replacing only that one line and leaving comments, order and line endings as they were. The file is written aside and swapped in, so an interrupted write leaves the old file; a copy left aside that way beside a kept setting's cfg file or Keepsake's own files is removed at the next launch, since under `BepInEx/config` a profile owner's sync would upload it. Only those names are checked, not every file in the profile. Because this happens before any plugin exists, a mod reads your value from its own file whether it reads the setting once in `Awake` or every frame. The order in which BepInEx loads plugins follows their dependencies and is otherwise not something one plugin controls, which is why this is a patcher rather than part of the plugin.

Whatever value the preloader replaces is recorded as the profile's value. Releasing a setting puts that value back.

When the value it replaces is not the profile's value it recorded last time, the profile changed it while you kept yours. The preloader adds each such change to `BepInEx/keepsake.changes`, where it waits until you answer it in the panel, so one found in a session where the panel stayed shut is still there the next time. A setting that changes again keeps the value the profile had before its first change, and drops out if the profile goes back to it. A setting made quiet is not added: its profile value is still recorded, so Release puts back the latest one, but you are not asked about it, which suits values the profile's owner moves all the time, such as a volume or a window size. A quiet setting is tagged quiet in the lists, and a kept setting whose value is the profile's, which keeping changes nothing about for now, is tagged same. Releasing a setting forgets that it was quiet. Once your character appears, a line in the corner says how many changes wait, once a session; `ProfileChangeNotice` turns that off. The pins file keeps its format, since Bindrune reads it too. A cfg file that holds your value tells nothing about the profile, since it looks the same whether no sync happened or the profile moved to your value, so a profile change to exactly your value is not seen.

`Keepsake.dll` is the plugin. Once every plugin has loaded, it reads the settings of every plugin whose cfg file sits under `BepInEx/config`, puts in any kept value the preloader could not place because the file or the line did not exist yet, and subscribes to each file's `SettingChanged`. A second pass runs shortly after a character first spawns if some kept settings were still missing, since some mods bind settings only when a world loads, and opening the panel runs it again.

When a kept setting changes while the game runs, through a config manager or the mod itself, the kept value follows, so it always holds your latest choice. The new value is taken at once and written to the pins file once the changes stop for a second, and when the game closes: a config manager's slider sets its setting in every frame it moves. A mod that reloads its cfg file from a file watcher of its own may change its settings on that watcher's thread; such a change is taken in on the game's main thread at the next frame. Values are compared and stored in their serialized form, the text the cfg file holds.

## Server-synced settings

ServerSync and Jotunn both hand a server's value to a setting while you are connected to a server that runs the mod, and both patch `ConfigEntryBase.GetSerializedValue` and `SetSerializedValue` so the cfg file keeps your local value during that time. Keepsake reads and writes through those same methods, so it only ever sees and keeps your local value, and a server handing its value over is never taken for a change of yours. Such settings can be kept like any other. They are recognised by the libraries' own switches: ServerSync's `SynchronizedConfig` on the entry it adds to the setting's description tags, and Jotunn's `IsAdminOnly` on a `ConfigurationManagerAttributes` tag.

## Keybinds and Bindrune

Bindrune keeps keybinds of its own through profile syncs, so where both are installed they would otherwise keep the same setting twice, and what it ends up as would depend on which wrote last. Instead a keybind, meaning a setting of type `KeyCode` or `KeyboardShortcut`, has one keeper:

While Bindrune is installed, keybinds are Bindrune's. Keepsake shows them but does not keep, write or follow them: the preloader skips them, telling a keybind by the `# Setting type:` note BepInEx writes above each setting and Bindrune by `Bindrune.dll` under `BepInEx/plugins` (a mod manager disables a mod by renaming its files, so a disabled Bindrune does not count). It looks for Bindrune only when a kept setting is a keybind, first where a mod manager or a hand install puts it, the plugins folder and one folder down, and only then through the whole folder, which takes a tenth of a second on a large profile. A keybind kept here before Bindrune was installed waits in the Kept list until Bindrune takes it over: Bindrune makes it your own key there, with the key recorded here as the profile's, and removes its line from `keepsake.pins`.

While Bindrune is not installed, keybinds are kept here like any other setting. The keys Bindrune holds as yours are then applied by nothing, so the panel offers to take them over. That only happens when asked: Keepsake reads `BepInEx/bindrune.keys` and keeps each key that is in use there, for settings that are loaded and not kept here already, and leaves Bindrune's file as it is. If Bindrune comes back, it takes those keys over again.

### What the two mods rely on in each other

Keepsake reads `bindrune.keys` and Bindrune reads and takes lines out of `keepsake.pins`, so these have to stay in step between the two mods, and neither may change without the other:

| | |
|---|---|
| `keepsake.pins` | First line `# keepsake pins v1`, then one setting per line, tab separated: cfg file relative to `BepInEx/config`, section, setting, value, and optionally the profile's value, values in the form the cfg file holds them. |
| `bindrune.keys` | First line `# bindrune state v3`; in its `[keys]` section one key per line, tab separated: `cfg:<mod guid>:<section>:<setting>`, your key, the profile's key, `1` while yours is in use, keys in the form BepInEx writes a `KeyboardShortcut`, `none` for no key. |
| Names | The GUIDs `isimp.Keepsake` and `isimp.Bindrune`, and the file name `Bindrune.dll`. |
| Keybinds | Settings of type `KeyCode` or `KeyboardShortcut`. |

Each mod reads the other's file only in the version it knows and otherwise leaves it alone and says so once in the log, so a format change on one side switches the hand over off rather than misreading it. Both repositories keep identical samples of the two files in `tests/contract`. The build workflow compares them with the other repository's on every push, and the tests check each mod's reading and writing against them.

One known gap: a Jotunn button that is backed by a setting but bound to a gamepad axis is shown read-only in Bindrune, while Keepsake still leaves it to Bindrune as a keybind, so neither keeps it.

## Kept files

Some mods keep state in files of their own under `BepInEx/config` rather than in settings, such as Seasonality's timer per world in `Seasonality/LastSeasonChangeData`. A sync replaces those with the owner's copies and deletes the ones the owner does not have. A file or folder can be kept whole, from the Files place in the panel. It lists every folder under BepInEx/config with its files under it, leaving out the mods' own cfg files, whose settings are kept one by one, BepInEx.cfg and logs. Images are left out of the list unless its filter asks for them, since mods ship textures by the hundred. The Files count on the left is kept files out of all of them; the folder is read on another thread as the panel opens, so the count fills in a moment later. A file's detail shows a text file in a read only viewer of its own, with a darker background and a fixed width font, up to its first 64 KB, and the image when it is a PNG or JPEG.

As the game closes, the plugin copies every kept file that changed into `BepInEx/keepsake-files`, each copy under its own path with `.kept` added to its name, and writes down when the game closed. It does so from each hook the game gives on the way out, the runtime's exit event last, so a mod that saves its file on the way out is still copied. A sync takes `cfg`, `txt`, `json`, `yml`, `yaml` and `ini` files from anywhere in the profile, so a plain copy of such a file would be synced like the original. Every copy carries the time of the file it was made from.

At launch, before any mod reads a kept file, the preloader looks at every one that is missing or differs from its copy in length or time. A missing one is put back. A sync or a mod manager changes files while the game is closed; a mod changes them while it runs.

When Keepsake saw the last game close, meaning a close written down after the last launch and a BepInEx log that stopped within a minute of it, every kept file was copied on the way out. A file that differs from its copy then changed after the game closed, and the copy goes back, whatever time the file carries. That matters because mod managers give the files they extract the time stored in the zip, which is often long past: Thunderstore Mod Manager and r2modman do, Thunderstore Mod Manager's own exports stamp every file with 1 January 2024, and Gale gives a package's files the time it first unpacked that version. The one exception is a file written after the close and before the log ended, which a mod saving on the way out leaves; its copy is brought up to date from it. BepInEx opens the log for the new game only after the preloader has run, so the preloader still sees when the last game wrote to it, and every game of the profile writes it, with Keepsake or without.

After a crash, a game the plugin did not load in, or games played without Keepsake, the file's own time is all there is, against the end of the log. A file written before it is yours, and its copy is brought up to date from it. A file written after it may be a mod's last write or a change made after the game, which look the same. Such a file is left as it is and waits in the Files place, tagged waiting and listed whatever the filter, until you choose. Its detail shows your copy over the file as it is, each with the time it was written: a text in a viewer of its own, with the line where the two first differ; an image as a thumbnail with its size; a binary file with its size and the byte where the two first differ. The choices are: Put my copy back puts the copy in at the next launch, before the mod that owns the file reads it again, and Keep this one makes the copy from the file as it is. A waiting file keeps its copy through later closes, since the copy is one of the two to choose from, and once your character appears a line in the corner says how many wait. Until it is answered, a waiting file is not put back at launch, whatever changed it.

A kept folder covers every file in it, and every file a mod adds to it later. Files in a kept folder that have no copy, such as ones a sync brought, are left as they are, and a copy stays when its file is gone, so a file a sync removed comes back rather than being forgotten. Releasing a file or folder removes its copies and leaves the file itself as it is.

A kept file keeps whatever lands on disk while the game runs, including what a mod writes from data a server sent it: ServerSync keeps a server's values out of the cfg files, but nothing does that for a mod's own files. Seasonality, for one, saves the timer a server sends under the server's world name, apart from the timers of your own worlds. At launch your copy wins, so a kept file changed while the game is closed is put back to your copy.

For Seasonality, keeping the Season setting and the `Seasonality/LastSeasonChangeData` folder together lets your own worlds carry on through syncs: the setting holds which season it is, and follows the mod as the season moves on, and the folder when it last changed. Making Season quiet saves being asked each time the owner's season moves.

## Profile updates

| Update | Kept settings | Kept files |
|---|---|---|
| Gale profile sync, or a Gale import into the same profile | Yes | Yes |
| Installing or updating a modpack, in any mod manager | Yes | Yes, when the game last closed normally |
| Thunderstore Mod Manager or r2modman, Update existing profile | No | No |
| Importing as a new profile, in any mod manager | Carried over by hand | Carried over by hand |

Kept settings do not depend on file times: the preloader writes each kept value into its cfg file at every launch, whatever put another value there.

After a crash, a modpack's file with an older time reads as written during the game and is taken as yours. With a fresh time, as a Gale sync or import gives, it waits for an answer instead.

Update existing profile in Thunderstore Mod Manager and r2modman builds the imported profile in a folder of its own, deletes the whole existing profile folder and moves the new one in its place, so nothing Keepsake keeps in the profile survives it. Keepsake keeps nothing outside the profile. A profile imported as new starts with nothing kept, and what was kept stays with the old profile. Copying `keepsake.pins`, `keepsake.files`, `keepsake.changes` and the `keepsake-files` folder from the old profile's `BepInEx` folder into the new one's carries it all over; every path in them is relative to the profile, and the next launch picks them up.

## The panel

The left column lists your kept settings, the settings changed since the game started, and every mod. The middle column lists the chosen place's settings under their sections, with a bar on values that differ from the mod's default, and the right column shows the selected setting's description, default, allowed values and, once kept, its editor. The search runs over mod names, file names, sections, setting names and descriptions. The panel reopens where it was left, with the same place, search, selection and scroll; clearing the search goes back to the place chosen before it, with both lists at the top.

Both lists create rows only for what is on screen and reuse them while scrolling, so the number of settings does not affect how fast the panel draws. Settings changed while the panel is open redraw the lists at most twice a second, and the right column only when the change is to the setting shown there, so an open dropdown stays open. Opening the panel reads every mod's settings again, and sorts them again only when a mod added or dropped one; the first time in a session, the log says how long opening took, apart from the first drawing of the lists, which also creates their rows.

A setting counts as changed this session when it differs from the value it had when Keepsake first saw it. Changes Keepsake makes itself do not count. Keep all, over that list and over a mod's settings, keeps every setting in it that is not kept yet, each at its current value and with its value at launch as the profile's, in one write; each is then kept like one kept by hand. Over a mod, Release all releases its kept settings again, each back to the profile's value.

Profile changed, on the left while there are any, lists the kept settings whose profile's value changed while you kept yours. Each one stays there, across launches, until you take the profile's value (the setting stays kept, at that value), stay with yours, give it another value, or release it.

Release not loaded, over the kept settings once a world is up, releases every kept setting no loaded mod has bound, such as those of a mod that was removed or renamed them. It is not offered at the start menu, since some mods bind their settings only when a world loads.

## Files

`BepInEx/keepsake.pins` holds the kept settings, one per line, tab separated: the cfg file relative to `BepInEx/config`, the section, the setting, your value, and the profile's value. The last field may be left off when adding a line by hand; it is filled in on the next launch. The file may be edited while the game runs; it is read again when the panel opens and before Keepsake writes to it.

`BepInEx/keepsake.changes` holds, under `[changes]`, the profile changes waiting for an answer, one per line, tab separated: the cfg file, the section, the setting, the profile's value before and its value now; and under `[quiet]` the kept settings whose changes are recorded without asking: the cfg file, the section and the setting. It is removed when it would hold nothing. Like the pins file it sits outside `config`, with an extension no profile sync picks up.

`BepInEx/keepsake.files` lists the kept files and folders, one per line, relative to `BepInEx/config`, a folder with a slash at the end. Paths leaving `BepInEx/config` are skipped. The copies are in `BepInEx/keepsake-files`.

`BepInEx/keepsake.session` holds, while anything is kept, when the game last started and closed with Keepsake, in UTC, with the hook that saw the close, and under `[files]` the kept files waiting for an answer: the path, then `ask`, or `put back` once you chose your copy.

Keepsake's own settings are in `BepInEx/config/isimp.Keepsake.cfg`.

## Limits

Kept values are written into the real cfg files, and kept files stay the real files, so the owner of a shared profile sends what they keep to everyone who follows it.

Only settings of loaded BepInEx plugins whose cfg file is under `BepInEx/config` are listed. Settings a mod keeps in a `ConfigFile` it creates itself, or in its own file format under that folder, can be kept as a whole file instead. Files outside that folder are not reachable, and neither is `BepInEx.cfg`, which is read before the preloader runs.

A value containing a tab or a line break cannot be kept. Flag enums are edited as text. A value added to the pins file by hand that the setting refuses is reported once in the log, and the mod falls back to its default as it would for any unreadable cfg value.

The preloader also runs on a dedicated server if installed there, and does nothing without a pins file. The plugin runs on the game client only.

## Tests

`tests/Keepsake.Tests` covers what needs no game: the pins file, the cfg text the preloader rewrites, both files shared with Bindrune, checked against the samples in `tests/contract`, the preloader's pass over a temporary profile, keeping, releasing, setting and following values on real BepInEx settings, keybinds with and without Bindrune loaded, and kept files saved and put back over a sync, told apart from a mod's own writing after a clean close, a crash, and games without Keepsake. The plugin class needs the game, so the tests stand in for it with `PluginShim.cs`, and hand their own settings to `SettingIndex` in place of the loaded plugins, and say themselves whether Bindrune is loaded. The tests run on .NET 8 against the real `BepInEx.dll` of a local profile, which is not part of the repository, so they run locally rather than on the build server:

```
dotnet test tests/Keepsake.Tests
```

`.githooks/pre-push` runs them before every push and stops the push when one fails; without a local `BepInEx.dll` it lets the push through with a warning. Git uses it once told to, per clone: `git config core.hooksPath .githooks`.

The parts that touch the running game (telling whether Bindrune is loaded, the panel, the notice on screen) are not covered and are checked in game.

## Building

Requires the .NET SDK 8 or newer. To build against the reference stubs in `lib/`, with no game installation needed:

```
dotnet build Keepsake.sln -c Release -p:LibsDir=lib
```

To build against a local installation and copy both assemblies into a BepInEx profile:

```
dotnet build Keepsake.sln -c Release -p:ValheimDir="<Valheim folder>" -p:ProfileDir="<profile folder>"
```

The `VALHEIM_DIR` environment variable can be used instead of `ValheimDir`. Without these, a default Steam installation and a default Gale profile are assumed. The version and the shared paths are set once in `Directory.Build.props`, and the code both assemblies use is in `src/Shared`.

The files in `lib/` contain metadata only: every method body is replaced and resources are removed, so they can be compiled against but not run. Regenerate them after a game update with `tools/strip-references.ps1`. Releasing is described in [releasing.md](releasing.md).
