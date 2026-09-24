# Keepsake

Keep your own value for any mod setting on a shared profile. When a profile sync would put the profile's value back, Keepsake puts yours in first, before any mod reads it.

![The Keepsake panel](https://raw.githubusercontent.com/isimp/Keepsake/main/docs/images/screenshot.webp)

## Getting started

Press Home to open the panel. Pick a mod on the left or search, select a setting and press Keep. You can change a kept value right there or in any config manager, and Keepsake follows along. Release hands the setting back to the profile.

## What else it does

Changed this session lists what you just tried out in a config manager, and Keep all keeps that whole list, or all of a mod's settings, in one click.

When the profile changes a value you keep, Keepsake tells you once your character appears, and you choose whether to take the new value. A setting the profile changes all the time, such as a volume, can be made quiet.

Files a mod keeps its state in, such as Seasonality's timer for each world, can be kept whole from the Files place, so your own worlds carry on where you left them.

## Good to know

Settings a server hands out while you are connected still follow the server, and your value is back when you leave. Keepsake runs on your own machine only and needs Jotunn.

If you are the one sharing the profile, what you keep goes out to everyone who follows it, because kept values are written into the mods' own config files.

Keybinds are left to Bindrune when it is installed, which keeps them the same way and shows every keybind of the game and your mods in one panel. Bindrune is on Thunderstore at https://thunderstore.io/c/valheim/p/isimp/Bindrune/ and on Hexium at https://valheim.hexium.gg/mods/isimp/Bindrune

## AI notice

Most of Keepsake was written by Claude Code (Anthropic), which did the heavy lifting on implementation and design. Heads-up so you can judge for yourself.

## Settings

All settings are in BepInEx/config/isimp.Keepsake.cfg, each with a description. They include the key that opens the panel, the panel's size, its sounds and the notice about profile changes.

## More

Technical notes and build instructions are on GitHub at https://github.com/isimp/Keepsake
