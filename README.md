# Keepsake

Keep your own value for any mod setting on a shared profile. When a profile sync would put the profile's value back, Keepsake puts yours in again before any mod has read it, so even settings a mod only reads at startup keep your value.

Press Home to open the panel. Pick a mod on the left or search by mod, setting or description, select a setting and press Keep. A kept setting can be given a new value right there or in any config manager, and Keepsake follows along. Release hands it back to the profile and puts the profile's value back. The panel also lists every setting that changed since the game started, so something you just tried out in a config manager is one click away from being kept.

Settings a server hands out while you are connected to it still follow the server, and your value is back when you leave. Keepsake runs on your own machine only, and servers do not need it. It needs Jotunn.

Your kept values are stored in BepInEx/keepsake.pins, a file profile syncs leave alone. They are also written into each mod's own config file, so if you are the one sharing a profile, whatever you keep goes out to everyone who follows it.

## AI notice

Most of Keepsake was written by Claude Code (Anthropic), which did the heavy lifting on implementation and design. Heads-up so you can judge for yourself.

## Settings

All settings are in BepInEx/config/isimp.Keepsake.cfg, each with a description. They include the key that opens the panel, the panel's size and whether it plays sounds.

## More

Technical notes and build instructions are on GitHub at https://github.com/isimp/Keepsake
