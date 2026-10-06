# Dark Nights

Darker, more realistic nights for SPT, inspired by Darker Nights for Fallout 4.

> **Status: 0.1.11, pre-release.** Played in game, but the darkness levels and the indoor
> lighting are still being tuned. Reports are very welcome; see
> [Reporting a problem](#reporting-a-problem).

It isn't an overlay or a brightness slider. Tarkov builds its night out of several separate
things, and Dark Nights darkens each one where the game produces it, together:

- **The sky's ambient light**, which lights everything outdoors. Clouds dim with it.
- **The moon's direct light.**
- **The daylight the game carries into buildings.** It goes at night. Rooms that are
  actually lit keep their light.
- **Reflections.** Weapon metal, glass and wet ground stop shining with daylight at night.
- **No more glowing hands.** EFT draws an extra light pass on your hands (and your weapon,
  where the game counts it as part of them) so they're always bright. Dark Nights removes
  it, day and night by default, so they're lit like everything around them.

How dark it gets depends on the night:

- A high full moon gives some of the light back. A new moon, or a moon below the horizon,
  doesn't.
- Cloud hides the moon, and fog and rain darken things further.
- Dusk and dawn fade smoothly with the real sun. Daytime is unchanged (unless you turn on
  **Dark Rooms By Day Too**, below).

Flashlights, lamps, lasers, muzzle flash and every other real light are never changed, so
they matter more at night. Thermals are unaffected. Night vision keeps a configurable share
of the vanilla night (half by default).

## Dark rooms

At night a room is dark, lit only by its lamps and whatever light comes in through its
windows and doors. The areas a map marks as bunkers are as dark as a moonless overcast
night. The sky's light comes back as you reach a doorway or step outside. This fades in
with dusk, like the rest of the night. There's no eye adaptation: rooms don't slowly
brighten while you stand in them.

**Dark Rooms By Day Too** (off by default) carries this into the daytime. A room is then
lit only where light actually reaches it: the patch of sun through its window or doorway,
and its lamps. The rest stays dark, even at noon, as a real room does. That's closer to a
lighting overhaul than to a darker night, which is why it's optional.

## Bots and the dark

**Bots can't see into the dark.** At night, a bot without its flashlight or NVGs on sees
you in the dark only from a few metres away. That applies in an unlit room, and outdoors,
where it sees further the brighter the moon. With **Dark Rooms By Day Too** on, it applies
in rooms by day as well. Stand near a lamp, or use your own
light, and you're visible as normal. Bots can still hear you. This works with or without
SAIN.

**With SAIN, bots see by the same night you do.** Bots never look at the rendered picture,
so a darker screen alone would leave you in the dark while they see as before. SAIN already
makes bots see less at night, but it goes by the clock: dusk at 20:00, full dark at 22:00,
and the moon ignored. Dark Nights hands SAIN the sky you're looking at instead:

- Night falls when the sun actually sets.
- A bright full moon gives bots some sight back, and a cloudy moonless night takes it away.

SAIN keeps its own night strength, weather effects and everything else. That includes when
bots switch their flashlights and NVGs on, which now follows the real darkness.

## Install

Extract the zip into your SPT folder. You should end up with
`BepInEx\plugins\DarkNights\DarkNights.Client.dll`. It's client-only, with no server part.

**Upgrading from 0.1.0:** your settings carry over, but **Interior Floor**'s default has
dropped from 0.02 to 0.005, and a saved config keeps the old value. Set it to 0.005, or
delete `BepInEx\config\com.mybutthasarash.darknights.cfg` to start from the new defaults.
Otherwise rooms stay brighter than intended.

## Settings

Press F12 (BepInEx Configuration Manager), or edit
`BepInEx\config\com.mybutthasarash.darknights.cfg`. Changes apply immediately, in raid.

- **Darkness**: Lightest, Lighter, Light, Medium (default), Dark, Darker, Darkest, or Custom.
- **What gets darker**: sky ambient, moonlight, clouds, interiors, reflections, eye
  adaptation, dark rooms and dark bunkers, each switchable on its own. **Dark Rooms By Day
  Too** (off) extends dark rooms and bunkers to the daytime. **Hand Glow** sets how much of
  the hand glow is left (0 by default).
- **How the world reacts**: when dusk starts and ends, how much cloud, fog and rain add,
  how much night vision keeps, and the interior floor.
- **Custom darkness**: your own values, used when Darkness is set to Custom.
- **Bots**: **Darkness Blinds Bots**, **Pitch Black Sight** (how close a bot without light
  must be to see you in total darkness, 3 m by default), and **Bots Share The Night** (SAIN
  only).
- **Excluded Maps**: Labs, Labyrinth, both Factories and the hideout by default.

Many settings only do something in particular conditions: the weather sliders in that
weather, Night Vision Keeps Vanilla with NVGs on, the Custom section only on Custom. Each
description says when it applies. On a clear night some sliders won't visibly change
anything; that's expected.

To compare and test:

- `Ctrl+F8` switches between Dark Nights and vanilla.
- `Ctrl+F11` writes a full diagnostic line to the log.
- To set the hour and weather for a test, use
  [Time & Weather Changer NG](https://sp-mod.com/mod/2120). Dark Nights follows whatever it
  sets.

## Known limits

- **Looking out a doorway:** while you stand in a dark room, the yard you see through the
  door dims with the room.
- **With Dark Rooms By Day Too on:** on overcast days, with no direct sun coming in, a room
  with windows is close to black. Bots without a light can't see into a windowed room at
  noon either. That's on purpose, but say so if it plays badly.

## Compatibility

Dark Nights scales the game's own light where it's produced, instead of overwriting
settings other mods also change, so it's built to stack with graphics mods rather than
fight them. These mods were checked by reading their source:

- **SAIN**: integrated, see above.
- **CloudSix**: its clouds are lit separately from the rest of the game, so Dark Nights
  darkens them directly to match the night. With CloudSix's "Disable Eye Adaptation" on (its
  default), exposure is locked and Dark Nights' **Limit Eye Adaptation** has nothing to do.
- **SSRSix, AOSix, POMSix**: no overlap. With SSR on (vanilla or SSRSix), reflections come
  from the already darkened screen, so the **Reflections** setting has nothing to do.
- **Amands's Graphics, Borkel's Realistic NVGs, FogSix, Better Night Skies**: designed to
  stack, not fight.
- **Time & Weather Changer NG**: Dark Nights reads the live sun, moon and weather, so it
  follows whatever that mod sets.

If you also use a ReShade preset that crushes blacks, the two will stack.

## Reporting a problem

Send `BepInEx\LogOutput.log` from the raid where it happened, ideally with screenshots taken
with `Ctrl+F8` on and off. The log has what's needed to track it down:

- **At startup:** the version, and a `Features:` line saying which parts found their place
  in the game. Anything marked `OFF` was not found in your game version.
- **When a raid starts:** the map, your darkness level, and every setting you've changed
  from its default.
- **Every 30 seconds in raid:** a `[night]` line with the sun, moon, weather, how much each
  part was darkened, what the bots see, and what the mod costs in frame time. It ends with
  `errors none` if nothing has gone wrong.
- **Any error:** logged once in full with what failed, and what you'll see while it's off.
  A part that keeps failing turns itself off for the rest of the session, so the game keeps
  running normally.

For "too bright" or "too dark" in a room, the `open sky` value on the `[night]` line helps
a lot. Press `Ctrl+F11` while standing in the room to write one.
