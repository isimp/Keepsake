# Technical notes

## Why a profile sync overwrites settings

A profile sync in a mod manager such as Gale uploads everything under `BepInEx/config`, plus files with the extensions `cfg`, `txt`, `json`, `yml`, `yaml` and `ini` anywhere in the profile. On the receiving side each uploaded file replaces the local copy, and config files the sender does not have are deleted. Pulls happen when the game is launched, before it starts. So a setting changed on a following profile lasts until the next launch.

## How Keepsake keeps a value

Keepsake has two parts.

`Keepsake.Preloader.dll` is a BepInEx preloader patcher. It patches nothing: BepInEx calls `Initialize` on every patcher before any plugin loads, and that is all it uses. It reads `BepInEx/keepsake.pins` and writes each kept value into its mod's cfg file, replacing only that one line and leaving comments, order and line endings as they were. The file is written aside and swapped in, so an interrupted write leaves the old file. Because this happens before any plugin exists, a mod reads your value from its own file whether it reads the setting once in `Awake` or every frame. The order in which BepInEx loads plugins follows their dependencies and is otherwise not something one plugin controls, which is why this is a patcher rather than part of the plugin.

Whatever value the preloader replaces is recorded as the profile's value. Releasing a setting puts that value back.

`Keepsake.dll` is the plugin. Once every plugin has loaded, it reads the settings of every plugin whose cfg file sits under `BepInEx/config`, puts in any kept value the preloader could not place because the file or the line did not exist yet, and subscribes to each file's `SettingChanged`. A second pass runs shortly after a character first spawns if some kept settings were still missing, since some mods bind settings only when a world loads, and opening the panel runs it again.

When a kept setting changes while the game runs, through a config manager or the mod itself, the kept value follows, so it always holds your latest choice. Values are compared and stored in their serialized form, the text the cfg file holds.

## Server-synced settings

ServerSync and Jotunn both hand a server's value to a setting while you are connected to a server that runs the mod, and both patch `ConfigEntryBase.GetSerializedValue` and `SetSerializedValue` so the cfg file keeps your local value during that time. Keepsake reads and writes through those same methods, so it only ever sees and keeps your local value, and a server handing its value over is never taken for a change of yours. Such settings can be kept like any other. They are recognised by the libraries' own switches: ServerSync's `SynchronizedConfig` on the entry it adds to the setting's description tags, and Jotunn's `IsAdminOnly` on a `ConfigurationManagerAttributes` tag.

## The panel

The left column lists your kept settings, the settings changed since the game started, and every mod. The middle column lists the chosen place's settings under their sections, with a bar on values that differ from the mod's default, and the right column shows the selected setting's description, default, allowed values and, once kept, its editor. The search runs over mod names, file names, sections, setting names and descriptions.

Both lists create rows only for what is on screen and reuse them while scrolling, so the number of settings does not affect how fast the panel draws. Settings changed while the panel is open redraw it at most twice a second.

A setting counts as changed this session when it differs from the value it had when Keepsake first saw it. Changes Keepsake makes itself do not count.

## Files

`BepInEx/keepsake.pins` holds the kept settings, one per line, tab separated: the cfg file relative to `BepInEx/config`, the section, the setting, your value, and the profile's value. The last field may be left off when adding a line by hand; it is filled in on the next launch. The file may be edited while the game runs; it is read again when the panel opens and before Keepsake writes to it.

Keepsake's own settings are in `BepInEx/config/isimp.Keepsake.cfg`.

## Limits

Kept values are written into the real cfg files, so the owner of a shared profile sends their kept values to everyone who follows it.

Only settings of loaded BepInEx plugins whose cfg file is under `BepInEx/config` are listed. Settings a mod keeps in a `ConfigFile` it creates itself, in its own file format, or outside that folder are not reachable, and neither is `BepInEx.cfg`, which is read before the preloader runs.

A value containing a tab or a line break cannot be kept. Flag enums are edited as text. A value added to the pins file by hand that the setting refuses is reported once in the log, and the mod falls back to its default as it would for any unreadable cfg value.

The preloader also runs on a dedicated server if installed there, and does nothing without a pins file. The plugin runs on the game client only.

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
