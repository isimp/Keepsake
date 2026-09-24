# Technical notes

## Why a profile sync overwrites settings

A profile sync in a mod manager such as Gale uploads everything under `BepInEx/config`, plus files with the extensions `cfg`, `txt`, `json`, `yml`, `yaml` and `ini` anywhere in the profile. On the receiving side each uploaded file replaces the local copy, and config files the sender does not have are deleted. Pulls happen when the game is launched, before it starts. So a setting changed on a following profile lasts until the next launch.

## How Keepsake keeps a value

Keepsake has two parts.

`Keepsake.Preloader.dll` is a BepInEx preloader patcher. It patches nothing: BepInEx calls `Initialize` on every patcher before any plugin loads, and that is all it uses. It reads `BepInEx/keepsake.pins` and writes each kept value into its mod's cfg file, replacing only that one line and leaving comments, order and line endings as they were. The file is written aside and swapped in, so an interrupted write leaves the old file; a copy left aside that way is removed at the next launch, since under `BepInEx/config` a profile owner's sync would upload it. Because this happens before any plugin exists, a mod reads your value from its own file whether it reads the setting once in `Awake` or every frame. The order in which BepInEx loads plugins follows their dependencies and is otherwise not something one plugin controls, which is why this is a patcher rather than part of the plugin.

Whatever value the preloader replaces is recorded as the profile's value. Releasing a setting puts that value back.

When the value it replaces is not the profile's value it recorded last time, the profile changed it since the last launch. The preloader hands those changes to the plugin in memory, through the application domain both run in, and the panel shows them for that session. Nothing about them is written to disk, so `keepsake.pins` keeps its format. A cfg file that holds your value tells nothing about the profile, since it looks the same whether no sync happened or the profile moved to your value, so a profile change to exactly your value is not seen.

`Keepsake.dll` is the plugin. Once every plugin has loaded, it reads the settings of every plugin whose cfg file sits under `BepInEx/config`, puts in any kept value the preloader could not place because the file or the line did not exist yet, and subscribes to each file's `SettingChanged`. A second pass runs shortly after a character first spawns if some kept settings were still missing, since some mods bind settings only when a world loads, and opening the panel runs it again.

When a kept setting changes while the game runs, through a config manager or the mod itself, the kept value follows, so it always holds your latest choice. Values are compared and stored in their serialized form, the text the cfg file holds.

## Server-synced settings

ServerSync and Jotunn both hand a server's value to a setting while you are connected to a server that runs the mod, and both patch `ConfigEntryBase.GetSerializedValue` and `SetSerializedValue` so the cfg file keeps your local value during that time. Keepsake reads and writes through those same methods, so it only ever sees and keeps your local value, and a server handing its value over is never taken for a change of yours. Such settings can be kept like any other. They are recognised by the libraries' own switches: ServerSync's `SynchronizedConfig` on the entry it adds to the setting's description tags, and Jotunn's `IsAdminOnly` on a `ConfigurationManagerAttributes` tag.

## Keybinds and Bindrune

Bindrune keeps keybinds of its own through profile syncs, so where both are installed they would otherwise keep the same setting twice, and what it ends up as would depend on which wrote last. Instead a keybind, meaning a setting of type `KeyCode` or `KeyboardShortcut`, has one keeper:

While Bindrune is installed, keybinds are Bindrune's. Keepsake shows them but does not keep, write or follow them: the preloader skips them, telling a keybind by the `# Setting type:` note BepInEx writes above each setting and Bindrune by `Bindrune.dll` under `BepInEx/plugins` (a mod manager disables a mod by renaming its files, so a disabled Bindrune does not count). A keybind kept here before Bindrune was installed waits in the Kept list until Bindrune takes it over: Bindrune makes it your own key there, with the key recorded here as the profile's, and removes its line from `keepsake.pins`.

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

## The panel

The left column lists your kept settings, the settings changed since the game started, and every mod. The middle column lists the chosen place's settings under their sections, with a bar on values that differ from the mod's default, and the right column shows the selected setting's description, default, allowed values and, once kept, its editor. The search runs over mod names, file names, sections, setting names and descriptions.

Both lists create rows only for what is on screen and reuse them while scrolling, so the number of settings does not affect how fast the panel draws. Settings changed while the panel is open redraw it at most twice a second.

A setting counts as changed this session when it differs from the value it had when Keepsake first saw it. Changes Keepsake makes itself do not count. Keep all, over that list, keeps every setting in it that is not kept yet, each at its current value and with its value at launch as the profile's, in one write.

Profile changed, on the left while there are any, lists the kept settings whose profile's value changed since the last launch. Each one stays there until you take the profile's value (the setting stays kept, at that value), stay with yours, give it another value, or release it.

## Files

`BepInEx/keepsake.pins` holds the kept settings, one per line, tab separated: the cfg file relative to `BepInEx/config`, the section, the setting, your value, and the profile's value. The last field may be left off when adding a line by hand; it is filled in on the next launch. The file may be edited while the game runs; it is read again when the panel opens and before Keepsake writes to it.

Keepsake's own settings are in `BepInEx/config/isimp.Keepsake.cfg`.

## Limits

Kept values are written into the real cfg files, so the owner of a shared profile sends their kept values to everyone who follows it.

Only settings of loaded BepInEx plugins whose cfg file is under `BepInEx/config` are listed. Settings a mod keeps in a `ConfigFile` it creates itself, in its own file format, or outside that folder are not reachable, and neither is `BepInEx.cfg`, which is read before the preloader runs.

A value containing a tab or a line break cannot be kept. Flag enums are edited as text. A value added to the pins file by hand that the setting refuses is reported once in the log, and the mod falls back to its default as it would for any unreadable cfg value.

The preloader also runs on a dedicated server if installed there, and does nothing without a pins file. The plugin runs on the game client only.

## Tests

`tests/Keepsake.Tests` covers what needs no game: the pins file, the cfg text the preloader rewrites, both files shared with Bindrune, checked against the samples in `tests/contract`, the preloader's pass over a temporary profile, and keeping, releasing, setting and following values on real BepInEx settings. The plugin class needs the game, so the tests stand in for it with `PluginShim.cs`, and hand their own settings to `SettingIndex` in place of the loaded plugins. The tests run on .NET 8 against the real `BepInEx.dll` of a local profile, which is not part of the repository, so they run locally rather than on the build server:

```
dotnet test tests/Keepsake.Tests
```

`.githooks/pre-push` runs them before every push and stops the push when one fails; without a local `BepInEx.dll` it lets the push through with a warning. Git uses it once told to, per clone: `git config core.hooksPath .githooks`.

The parts that touch the running game (telling whether Bindrune is loaded, keybinds while it is, the panel) are not covered and are checked in game.

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
