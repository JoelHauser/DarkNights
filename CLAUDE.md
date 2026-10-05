# Dark Nights -- working notes for Claude

Darker, more realistic nights for SPT. Client-only BepInEx plugin, **no `spt-*` reference
and no `Assembly-CSharp` reference at all**. Inspired by Darker Nights for Fallout 4
(nexusmods.com/fallout4/mods/191), and the part of it worth copying is the principle,
not a value: that mod reduces sunlight, moonlight **and** ambient light, makes interiors
darker at night *unless they are well lit*, follows the weather, and offers seven levels.
It is not a slider and not a shader over the image. The whole world dims as one thing.

**Nothing in this repo has run in the game.** 0.1.0 was built on 2026-10-05 from static
analysis of the game assembly and the source of four Forge mods. Every number in
`Presets.cs` is a starting ladder, not a tuned value.

## The box this was built on

| | development | live |
| --- | --- | --- |
| SPT install | `C:\HUH` -- never launched | `H:\SPT4.1.X` (per the UltrawideStash notes) |
| SPT version | 4.1.5 | 4.1.6 |
| EFT client | `0.16.9.40743` | `0.16.9.5.40743` |
| Patched Assembly-CSharp | **not on this box, and no hpatchz** | -- |

```
scripts\pack.ps1 -SPTPath C:\HUH            # version check, build, test, reference check, zip
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install
dotnet test tests\DarkNights.Tests
```

**Run those through PowerShell, not Bash** -- the `C:HUH` mangling trap, same as the
sibling repos.

The user does **not** run Amands's Graphics. Compatibility with it is still designed for,
because it is the most-downloaded graphics mod on the Forge, but it is not the test bed.

## Why there is no Assembly-CSharp reference

Same as LoadingRaid and UltrawideStash: the `Assembly-CSharp.dll` in `Managed` is the
unpatched original until the SPT Launcher applies its delta, and the delta renames
obfuscated types. Everything this mod touches has a readable name in the unpatched
assembly -- TOD_Sky is a third-party asset, and AmbientLight, PrismEffects, NightVision,
WeatherController and EnvironmentManager are BSG's but not obfuscated -- so it is all
resolved by name in `GameTypes.cs`. **Those names were read off the unpatched assembly
only.** The first live log's `Features:` line is the check that they survive the delta.

New game members go in `GameTypes`, never at the patch site. Each feature has its own
`*Ready` flag, so a rename turns one part off with a warning.

## How EFT lights a night -- verified from the decompiled assembly

Read with ilspycmd from `C:\HUH\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll`.

- **`ToDController.Update`** (a serialised class on WeatherController, run from
  `WeatherController.LateUpdate`) is the real night-ambient path. Every frame it rewrites
  `TOD_Sky.Atmosphere` brightness/contrast/scattering from curves on the sun's height,
  sets `Night.ColorMultiplier` from the moon phase (`0.5 - 0.5 * dot(moon, sun)` mapped
  through `MoonLightMinMax`), builds spherical harmonics from curves, cloud and fog on a
  job, and calls **`AmbientLight.SetSH(SH)`**. `TODSkySimple`, the other sky, also ends in
  `AmbientLight.SetSH`. That method is the one place all sky ambient passes.
- **`TOD_Sky.UpdateAmbient()` and `UpdateReflection()` are empty** in EFT's build, and
  `Night.AmbientMultiplier` / `ReflectionMultiplier` are never read. Setting them does
  nothing.
- **Moonlight**: `TOD_Sky` sets the directional light's intensity to
  `Night.LightIntensity * clamp01((1 - Fogginess) * moonAboveHorizon)` at night -- but only
  every `Light.UpdateInterval`, which ToDController sets from shadow quality (1, 0.5, 0.1,
  0 s). Multiplying it every frame would compound; hence `Tracked`.
- **The twilight curve exists already**: `TOD_Sky.LerpValue = InverseLerp(110, 80, SunZenith)`,
  0 with the sun 20 degrees down, 1 with it 10 up. This mod uses sun elevation directly
  (0 to -12 by default) so day is exactly vanilla.
- **`AmbientHighlight.SetSH`** gets the same harmonics and draws them again on hands and
  characters (stencil type Hands by default). **`CloudController.UpdateAmbient`** is passed
  `ToDController.SH`, the *unscaled* property, so clouds need their own patch.
- **Interiors** are `StencilShadow` volumes (a fixed `Ambient` colour, default black) and
  `AnalyticSource` volumes (fill light at doors and windows: `LightIntensity`,
  `LightIntensityMultiplicator`, `AddAmbient`, pushed to the GPU only by
  `UpdateSettings()`). Both are drawn only within `Culling.Distance` of the **camera**
  (default 100 m, 5 m fade). Neither follows time of day in any code read.
  `AnalyticSource.EnableDraw` exists but `AmbientLight` never reads it -- do not build a
  test on it.
- **Auto-exposure**: every frame `PrismEffects` copies `exposureMiddleGrey` and
  `exposureSpeed` from `EnvironmentManager`, which smooths toward per-`IndoorTrigger`
  values (outdoor 0.23 / indoor 0.14 by default). `exposureUpperLimit` /
  `exposureLowerLimit` (component defaults 6 / -6) are only set by preset loads. Whether
  `useExposure` is on in EFT's presets is serialised data -- the log line reports it.
- **`LevelSettings`** reapplies `RenderSettings.ambient*` on every camera pre-cull, and
  switches to its `NightVision*` colours while NVGs are on. Anything writing
  `RenderSettings.ambient*` is overwritten every frame. This mod does not touch it.
- **Thermal** blits `GBuffer0` (albedo) to the camera target and renders temperature, so
  it never sees scene lighting. **NVG** is a gain blit over the lit image plus the
  `LevelSettings` ambient swap -- so darker ambient means darker NVG unless compensated.
- **`EnvironmentManager`** also cuts shadow distance to as little as a third between 05-10
  and 17-23 (`EnableLongShadowsCorrection`).
- Shader property names are behind BSG's string encryption (`\uf2c3.\ue000(N)`), so
  how `StencilShadow.Ambient` composites (replace or multiply) is **not** knowable from
  here. The design assumes replace; the bubble tests will say.

## The design rule: multiply at the output, never set an input someone else owns

| Part | Hook | Why it does not fight |
| --- | --- | --- |
| Sky ambient | prefix `AmbientLight.SetSH(ref __0)` | downstream of every curve; a mod that edits the curves still shapes it |
| Hands/characters | prefix `AmbientHighlight.SetSH` | same harmonics, same factor |
| Clouds | prefix `CloudController.UpdateAmbient` | it is handed the unscaled SH |
| Moonlight | postfix `TOD_Sky.LateUpdate`, through `Tracked` | adopts every value TOD (or a mod) writes as the new base |
| Interiors | `Interiors.cs`, from cached originals | no mod read touches these volumes |
| Eye adaptation | `exposureUpperLimit` through `Tracked` | adopts a preset load as the new base |

Never: a screen overlay, gamma, `LevelSettings`, `RenderSettings.ambient*`, or the TOD
night parameters (Amands's Graphics writes `Night.LightIntensity` and would reset it).

`NightModel` is the whole decision, pure and tested: nightness from the sun, credit back
from the moon's height and phase, taken away again by cloud, fog and rain, with NVG
retention. One `NightState` per frame (`NightDriver`, lazily on first use) feeds every part.

## The hand glow, and the weapon

The user does not want hands or weapon to glow at all, day or night ("extremely
unrealistic"). From the decompiled `AmbientHighlight`: after lighting it draws an extra
ambient pass per `_highlightSettings` entry over one stencil class (`StencilTypeToUse`,
default `Hands`; `Characters` and `Static` exist), at
`lerp(HighlightMinMultiplier 0.2, HighlightMaxMultiplier 1.7, curve(-LightObject.forward.y))`
with curve keys (-1,0) (0,0) (0.3,1) -- and at night the light object is the moon. HDR blend
is One/One (additive); non-HDR is DstColor/Zero (multiplicative). `SetSH` also adds
`_additionalLights` (fixed colour and intensity, never time-scaled) to the harmonics.
Shipped values are prefab data; `HandGlow` logs them once per raid.

`HandGlow.cs` scales min/max (around `ManualOnRenderObject(Camera)`) and the fixed-light
intensities (around `SetSH`) for that call only, restoring them in the postfix. Default
`Hand Glow` 0, `Off By Day Too` on. **HDR cameras only** -- on a non-HDR camera zeroing a
multiply would black the hands out, so that case stays vanilla with a warning. Skipping the
draw instead does NOT work: the command buffer keeps last frame's commands.

**The weapon:** which renderers write the `Hands` stencil is material/shader data, not code
(`MaterialStencilReplacer` only stores an int; nothing in the assembly reads it). If the
first-person weapon is in that stencil, the glow goes with the hands; if not, the pass never
touched it. Either way there is no other weapon glow in code (`HighLightMesh` is the hideout
and item-inspect outline). What else makes metal shine at night is the **sky reflection**:
`AmbientLight.ReflectionIntensity` (times rain wetting; 1 when SSR is on, reflecting the
already-dark screen). `Reflections.cs` scales it by the sky-ambient factor through `Tracked`.
Settings are live, so the in-game check is: night raid, F12, flip Hand Glow 0 <-> 1 and
Reflections on/off while looking at the weapon.

## The brightness bubble -- hypotheses, ranked

Reported by the user: at night the area around the player is brighter than the distance,
with a visible edge that follows them.

1. **Interior ambient volumes.** Drawn within ~100 m of the camera with a 5 m fade, fixed
   colour regardless of time. Near buildings keep their ambient; far ones fall back to the
   much darker night sky. Darkening only the sky makes this *worse*, which is why
   `Interiors` exists. **Test:** `Ctrl+F10` draws them 4x further; if the edge moves, this
   is it. `Ctrl+F9` turns the fill light off.
2. **FogSix's froxel range** (default 200 m) if installed -- near-field effects end there.
3. **Shadow distance** -- a real moving boundary, but beyond it moonlight is unshadowed, so
   the far side would be *brighter*. Wrong direction except perhaps at dusk.
4. **AmbientHighlight** -- only hands and characters, not the ground.

## Forge mods read for compatibility (source, 2026-10-05)

- **Amands's Graphics 1.8.0** (4.x source: github.com/ArchangelWTF/AmandsGraphics,
  commit 5ad9299, same day as the release): replaces `ToDController.AmbientContrast`
  curves, scales `ScatteringBrightnessMultiplier`, sets moon/sun mesh and
  `Night.LightIntensity`, `LevelSettings.NightVision*`, MBOIT and LevelSettings
  `ZeroLevel`, Prism LUT/tonemap, NVG noise. Never exposure, never SetSH.
- **Borkel's Realistic NVGs 3.0.4** (github.com/Borkel/RealisticNVG-client-2): optionally
  transpiles out NightVision's ambient swap, blocks Amands's NVG ambient writes, has its
  own NVG renderer. Whether vanilla `NightVision.On` still reports correctly under it is
  **untested**.
- **FogSix 1.0.0** (github.com/matsixx/FogSix): prefix that *skips*
  `TOD_Scattering.OnRenderImageNormalMode` and draws its own froxel fog, coloured from
  `MoonSkyColor` / `MoonLightColor`; reads the interior stencil mask. Its notes claim MBOIT
  is off on 4.1 and that `LevelSettings` is destroyed after raid start -- **not verified
  here**. Against a darker scene its fog will look relatively bright; a sky/haze factor is
  the likely next feature.
- **CloudSix 4.0.0** (matsix, github.com/matsixx/CloudSix, commit 91eaf37; GUID
  `com.matsix.cloudsix`) -- **the user requires this one to work.** It prefix-skips
  `CloudLayerRenderer.RenderClouds` and draws volumetric clouds lit by `_MoonIntensity`
  (its config, default 1), `_MoonColor` = `TOD_Sky.MoonLightColor`, `_AmbientColor` =
  `ToDController.AddTopAmbient` or its own sky-view LUT ("Ambient From Sky", default on),
  times `_AmbientStrength` -- none via SetSH, so our sky scaling never reached them.
  `CloudSixBridge`: postfix on `WeatherController.LateUpdate` with `after = cloudsix` and
  `Priority.Last`, scaling `_MoonIntensity` by the moonlight factor and `_AmbientStrength`
  by the ambient factor on `VolCloudRenderer.lowMaterial`, through `Tracked`.
  `_AmbientColor` deliberately not scaled: the shader is in its bundle and if strength
  multiplies colour, both would double-darken -- **check in game**. Everything else coexists:
  its "Ground Lighting From Sky" swaps the hue of `ToDController.LightColor`/`AddTopAmbient`
  by day only (upstream of SetSH, so the darkening stacks); its "Disable Eye Adaptation"
  (default **on**) pins Prism lower = upper = "World Exposure" (0.8) every frame, so our
  exposure ceiling is a no-op (it never goes below the lower limit) and the log's "written
  by others: exposure" climbs every frame -- expected, not a fight; its `TOD_Sky.LateUpdate`
  postfix only disables the atmosphere renderer. With CloudSix the vanilla cloud patch
  (`CloudController.UpdateAmbient`) is harmless and idle.
- **SSRSix 1.0.0** (github.com/matsixx/SSRSix, commit f907743; `com.matsix.ssrsix`): its own
  SSR, composited into `source` from a `Priority.First` prefix on
  `TOD_Scattering.OnRenderImageNormalMode` that returns true (so FogSix/vanilla fog still
  runs after it), plus a prefix making PPv2 `ScreenSpaceReflections.IsEnabledAndSupported`
  false so the game's SSR never renders while the **setting stays on** -- its author found
  that with the setting off, materials bake in day-baked reflection-probe specular that
  turns into white sparkle at night. It traces the lit scene (already darkened by us) and
  falls back to the on-screen sky or CloudSix's published `_SsrSkyReflMap`. **Consequence
  for us:** with the SSR setting on, `AmbientLight` uses 1 instead of
  `wetting * ReflectionIntensity`, so `Reflections` is a no-op -- harmless, and the
  reflections it would have dimmed come from the dark screen anyway. Same holds for vanilla
  SSR on. No hook in common.
- **AOSix 2.0.0** (github.com/matsixx/AOSix, commit d78ed84; `com.matsix.aosix`): prefix on
  `CameraManager.SetSSAO` swapping HBAO/PPv2 AO for its own, and a postfix on
  `AmbientLight.Initialize` swapping `_screenAmbientMaterial`'s shader for an AO-aware
  port of EFT's Custom Ambient. Our SetSH prefix scales the global harmonics that shader
  reads, not the shader, so the darkening carries through and AO shades it. No hook in
  common. (Its source shows the patched assembly exposes `AmbientLight.Initialize` and
  `_screenAmbientMaterial` by name; `GameTypes` finds the volume registries by type, so it
  works whatever they are called.)
- **POMSix 1.0.1** (github.com/matsixx/POMSix, commit f4bf63d; `com.matsix.pomsix`): only
  postfixes `MicroSplatTerrain.Sync` / `MicroSplatMeshTerrain.Sync` to swap terrain and road
  shaders for parallax-occlusion ones and publishes its own `_POMSix*` globals. No lighting,
  sky, exposure or reflection code. Ambient reaches terrain through `AmbientLight`'s deferred
  screen pass, not the terrain shader, so the darkening is unaffected. Nothing in common.
- **Better Night Skies 2.0.0**: replaces the star material, sets `Stars.Brightness`. Low risk.
- **Time & Weather Changer NG 2.7.0**: not read. The model reads the live sun, so it follows.
- **Realistic Lighting for Tarkov 2.0.1**: SPT 3.11 only; ReShade plus an Amands config.

None of them writes `AmbientLight.SetSH`, the interior volumes, or Prism exposure.

The Forge (`forge.sp-tarkov.com`) answered WebFetch with Cloudflare 525 all session; the
`sp-mod.com` mirror worked. Its search returns current-version results only and is thin,
so this list is not exhaustive.

## Untested, in rough order of risk

1. **Do the names resolve on the patched assembly?** The `Features:` line.
2. **Does auto-exposure even run?** If `useExposure` is off, the ceiling does nothing; if
   it runs near its ceiling at night, the presets' ceilings may be too low or too high.
   Units are Prism's own and the real night value is unknown.
3. **Interiors: replace or multiply?** If `StencilShadow.Ambient` multiplies the sky
   ambient rather than replacing it, interiors get darkened twice.
4. **Harmony on `ref SphericalHarmonicsL2 __0`** for a by-value struct parameter --
   standard, but unrun.
5. **The weather constants**: cloudiness -0.4..0.4 and fog 0.018 are taken from the game's
   own InverseLerp ranges and from SAIN/FogSix, not measured.
6. **NVG detection** with Borkel's mod.
7. **Performance**: every 5 s a pass over the volume registries (reflection), and a
   `FindObjectsOfType` every 2 s for Prism and NightVision. Expected negligible; unmeasured.

## Not in 0.1.0, on purpose

- **Sky dome and haze.** `ToDController` rewrites `Atmosphere.Brightness` every frame, but
  the SH job samples the atmosphere too, so scaling it darkens the ambient a second time,
  non-linearly. Needs a live look before it is built.
- **`RenderSettings.fogColor`** (TOD sets it every frame from the sky).
- **Bots without SAIN.** Still vanilla -- see *Bots and SAIN* below for why, and for what
  0.1.0 does when SAIN is loaded.

## Bots and SAIN

The user runs SAIN. **Bots never read rendered light**, so the visual darkening alone
would leave the player in the dark while bots see as before.

**Vanilla**, verified from `LookSensor` (2026-10-05): every 10 s (3 when flashed) a bot recomputes
  `ClearVisibleDist = clamp(Settings.Current.CurrentVisibleDistance * visionCurve(game hour)
  * WeatherVisibilityK(rain, fog), MINIMUM_VISIBLE_DIST, 9999)`; the weather term is 1
  when `AIData.IsInside`. The curve is `Curv.StandartVisionSettings`, or
  `NightVisionSettings` for bots with `SELF_NIGHTVISION`, keyed on the **clock hour**: no
  sun elevation, no moon phase, no cloud. Then `BotNightVisionData.UpdateVision` replaces it
  with `NIGHT_VISION_DIST` (105) while the bot's NVG is on (it puts them on below
  `NIGHT_VISION_ON` = 100 and off above 110), and `BotLight.UpdateLightEnable` adjusts it
  for the bot's own flashlight. A player's light on adds `ENEMY_LIGHT_ADD` (35 m) from
  `ENEMY_LIGHT_START_DIST` (40 m). Those numbers are `BotGlobalLookData` field defaults;
  live values come from SPT's bot difficulty data. A no-SAIN path would hook the input,
  `CurrentVisibleDistance`, so NVG and flashlight decisions follow -- **not built, and its
  other callers not checked**.

**SAIN 4.5.1** (Forge, SPT 4.1.6; source github.com/ArchangelWTF/SAIN, commit 2d1d925,
2026-09-04, the release date; GUID `me.sol.sain`). It replaces `LookSensor`'s update
(`DisableLookUpdatePatch` on `LookSensor.Activate`) with `SAINVisionClass.UpdateVisionDistance`:
`ClearVisibleDist = clamp(CurrentVisibleDistance * weatherMod, 30 m, base) * timeMod`, then
the same NVG and bot-light steps. Its night is **also clock-only**:
`SAIN.Components.BotController.TimeClass`, every 5 s, from the game clock -- dawn 06:00-08:00,
dusk 20:00-22:00, night `NightTimeVisionModifier` 0.2 (snow 0.35), gain-sight up to 3x
slower (`TIME_GAIN_SIGHT_SCALE_MAX`). Weather (`SAINWeatherClass`: fog, rain tiers, cloud
tiers) is separate and applies day and night. Its flashlight/NVG decisions
(`UpdateLightEnablePatch`, `ToggleNightVisionPatch`: on at <= 0.33, off at >= 0.66), the
gain-sight slowdown and `EnemyVisionDistanceClass`'s always-visible rule
(`VisibilityRatio > 0.5`) all read `TimeVisionDistanceModifier` / `VisibilityRatio`. SAIN
also spots the player's own light beams (`LightDetectionClass`; IR only for bots on NVG)
and searches toward them.

**What 0.1.0 does: `SainBridge`.** One Harmony postfix on the private static
`TimeClass.getModifier(float time, ETimeOfDay, out float visibilityRatio)`, found by name
and shape, so no SAIN reference. It replaces SAIN's clock ratio with
`NightModel.BotVisibility` -- sun elevation through the same twilight, moon credit times
`MoonShare(preset)` -- blended by `Strength`, and returns `lerp(min, 1, ratio)` with SAIN's
own minimum **inferred from SAIN's answer** (`TryInferSainMinimum`), so its snow value and
user config carry through. Because SAIN's caller derives the gain-sight modifier from the
ratio, and its light/NVG/always-visible decisions from the result, all of them follow.
Fog and rain are **not** in `BotVisibility`: SAIN applies its own weather on top, and
counting them twice would be wrong. Cloud enters only as the moon it hides. Soft
`BepInDependency` on SAIN for load order; the postfix turns itself off on its first error.
`BotVisibilityTests` reproduces SAIN's getModifier from source and checks the inference and
that strength 0 hands SAIN back its own answer exactly.

Untested: that Harmony binds `ref float __2` to the out parameter of a static private
method in a netstandard2.1 assembly (expected), and the Fika case -- SAIN runs on the host,
so the host's Dark Nights setting is the one that counts, and a headless host may have no
TOD_Sky (then `NightDriver` is idle and SAIN keeps its clock).

## Publishing

- Remote **https://github.com/JoelHauser/DarkNights.git**, on `main`.
- GUID `com.mybutthasarash.darknights`, the prefix the sibling repos use.
- Version lives in the csproj `<Version>` and `DarkNightsPlugin.PluginVersion`;
  `pack.ps1` refuses to pack if they disagree.
- Commit as `Joel Hauser <jhauser@bostonlightsource.com>` -- **not** the gmail address.
- Commit bodies are prose and end with a `Co-Authored-By:` trailer. Write the message to a
  file with `UTF8Encoding($false)` and use `git commit -F <file>`: a piped here-string adds
  a BOM in Windows PowerShell 5.1.
- **The Forge forbids mods substantially written by AI agents.** The user has acknowledged
  this for their other repos and said "we're all good" -- do not re-raise it.
