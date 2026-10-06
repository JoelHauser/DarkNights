# Dark Nights

Darker, more realistic nights for SPT, inspired by Darker Nights for Fallout 4.

Not an overlay and not a brightness slider. Tarkov builds its night out of several separate
things, and Dark Nights darkens each one where the game produces it, together:

- **The sky's ambient light**, which lights everything outdoors.
- **The moon's direct light.**
- **Daylight inside buildings.** At night it goes, except in rooms that are actually lit.
  Interiors that are already dark are left alone.
- **Eye adaptation.** Auto-exposure can only brighten a dark night so far.
- **Reflections.** Weapon metal, glass and wet ground stop shining with daylight at night.
- **No more glowing hands.** EFT draws an extra light pass on your hands (and your weapon,
  where the game counts it as part of them) so they're always bright. Dark Nights removes
  it, day and night by default, so they're lit like everything around them.

How dark depends on the night. A high full moon gives some of the light back, and a new
moon or a moon below the horizon doesn't. Cloud hides the moon, and fog and rain darken it
further. Dusk and dawn fade smoothly with the sun, and daytime is exactly vanilla.

Flashlights, lamps, lasers, muzzle flash and every other real light are never changed, so
they matter more at night. Thermals are unaffected. Night vision keeps a configurable share
of the vanilla night (half by default).

## Bots see by the same night (with SAIN)

Bots in Tarkov never look at the rendered picture, so a darker screen alone would leave you
in the dark while they see as before. SAIN already makes bots see less at night, but it
goes by the clock: dusk at 20:00, full dark at 22:00, the moon ignored. With SAIN installed,
Dark Nights hands SAIN the same sky you're looking at instead. Night falls when the sun
actually sets, a bright full moon gives bots some sight back, and a cloudy moonless night
takes it away. SAIN keeps its own night strength, weather effects and everything else,
including when bots switch flashlights and NVGs on, which now follows the real darkness.
Turn it on or off under **Bots (SAIN)**.

**Bots can't see into the dark.** At night, or in a room daylight doesn't reach, a bot
without its flashlight or NVGs on only sees you from a few metres away. Stand near a lamp or
use your own light and you're visible as normal. This part works with or without SAIN.
Bots can still hear you.

**Rooms without daylight are dark at any hour.** A windowless room, or a bunker, is dark at
noon and lit only by its lamps.

> **Status: 0.1.9, in testing.** Played in game, but the darkness levels and the indoor
> daylight are still being tuned.

## Install

Extract the zip into your SPT folder. You should end up with
`BepInEx\plugins\DarkNights\DarkNights.Client.dll`. It's client-only, with no server part.

## Settings

Press F12 (BepInEx Configuration Manager), or edit
`BepInEx\config\com.mybutthasarash.darknights.cfg`.

- **Darkness**: Lightest, Lighter, Light, Medium (default), Dark, Darker, Darkest, or Custom.
- **What gets darker**: sky ambient, moonlight, clouds, interiors and eye adaptation, each
  switchable on its own.
- **How the world reacts**: when dusk starts and ends, how much cloud, fog and rain add,
  how much night vision keeps, and the interior floor.
- **Excluded Maps**: Labs, Labyrinth, both Factories and the hideout by default.

`Ctrl+F8` switches between Dark Nights and vanilla so you can compare.

## Compatibility

Dark Nights scales the game's own light where it's produced, instead of overwriting
settings that other mods also change. In principle it stacks with graphics mods rather than
fighting them. In practice that's not yet tested. Mods read so far: Amands's Graphics,
Borkel's Realistic NVGs, FogSix, Better Night Skies, SAIN, CloudSix, SSRSix, AOSix and POMSix.

**CloudSix:** its volumetric clouds are lit separately from the rest of the game, so Dark
Nights darkens them directly to match the night. If CloudSix's "Disable Eye Adaptation" is
on (its default), exposure is locked by CloudSix and Dark Nights leaves it alone.

If you also use a ReShade preset that crushes blacks, the two will stack.
