using System;

namespace DarkNights.Client
{
    /// <summary>What the world looks like right now, read off the game once per frame.</summary>
    public struct NightInputs
    {
        /// <summary>Degrees above the horizon. Negative once the sun has set.</summary>
        public float SunElevation;

        /// <summary>Degrees above the horizon.</summary>
        public float MoonElevation;

        /// <summary>
        /// 0 new moon, 1 full moon. The same quantity ToDController uses for
        /// Night.ColorMultiplier: 0.5 - 0.5 * dot(moonDirection, sunDirection).
        /// </summary>
        public float MoonPhase;

        /// <summary>WeatherCurve.Cloudiness, roughly -1 (clear) to 1 (overcast).</summary>
        public float Cloudiness;

        /// <summary>WeatherCurve.Fog, on the game's own tiny scale (about 0 to 0.02).</summary>
        public float Fog;

        /// <summary>WeatherCurve.Rain, 0 to 1.</summary>
        public float Rain;

        public bool NightVisionOn;
    }

    /// <summary>
    /// The darkness a preset (or the Custom entries) asks for. Every "night" value is a
    /// multiplier on what the game itself would have drawn, reached at full night.
    /// </summary>
    public struct NightSettings
    {
        /// <summary>Sun elevation, degrees, where darkening starts. Above it nothing changes.</summary>
        public float TwilightStart;

        /// <summary>Sun elevation, degrees, where darkening is complete.</summary>
        public float TwilightEnd;

        /// <summary>Sky ambient on a clear night with no moon up.</summary>
        public float MoonlessAmbient;

        /// <summary>Sky ambient on a clear night under a high full moon.</summary>
        public float FullMoonAmbient;

        /// <summary>The moon's direct light.</summary>
        public float Moonlight;

        /// <summary>
        /// Daylight carried into interiors by the game's interior ambient volumes, as a share of
        /// the night sky ambient: the room gets this much of what reaches it from outside.
        /// </summary>
        public float Interior;

        /// <summary>Extra multiplier under full overcast -- cloud blocks moon and starlight.</summary>
        public float Overcast;

        /// <summary>Extra multiplier in full fog.</summary>
        public float FogDark;

        /// <summary>Extra multiplier in full rain.</summary>
        public float RainDark;

        /// <summary>0 = NVGs see the darkened world, 1 = NVGs see vanilla nights.</summary>
        public float NightVisionRetention;

        /// <summary>
        /// The highest auto-exposure PrismEffects may reach at full night, in its own
        /// exposureUpperLimit units (the component defaults to -6..6). Eye adaptation is what
        /// would otherwise brighten a darkened night straight back up.
        /// </summary>
        public float ExposureCeiling;
    }

    /// <summary>One frame's answer: a multiplier per thing the game lights, plus how night it is.</summary>
    public struct NightState
    {
        /// <summary>0 by day, 1 at full night. Every multiplier below is 1 when this is 0.</summary>
        public float Nightness;

        public float Ambient;
        public float Moonlight;
        public float Interior;

        /// <summary>How far the auto-exposure ceiling moves toward its night value, 0 to 1.</summary>
        public float Exposure;

        public static NightState Vanilla => new NightState
        {
            Nightness = 0f,
            Ambient = 1f,
            Moonlight = 1f,
            Interior = 1f,
            Exposure = 0f,
        };
    }

    /// <summary>
    /// The whole darkening decision, as plain arithmetic. No Unity type and no game type, so
    /// it is compiled straight into the tests.
    ///
    /// The point is that nothing is one slider. Darkness comes from where the sun is, then
    /// the moon gives some of it back by its phase and height, and cloud, fog and rain take
    /// that back again -- the same quantities the game already uses, pushed further. The
    /// result is applied to the sky ambient, the moonlight and the interiors together, so the
    /// world dims as one thing rather than an image getting darker.
    /// </summary>
    public static class NightModel
    {
        /// <summary>
        /// Where EFT itself treats cloud as heavy. ToDController boosts its top harmonic over
        /// InverseLerp(-0.4, 0.2, cloudiness), and WeatherController derives Fogginess over
        /// 0 to 0.4. Overcast here spans the same ground.
        /// </summary>
        public const float ClearCloudiness = -0.4f;
        public const float OvercastCloudiness = 0.4f;

        /// <summary>
        /// WeatherCurve.Fog at which fog counts as full. 0.018 is the "full fog" SAIN clamps
        /// to, and FogSix normalises over the same range. Not read from the game itself.
        /// </summary>
        public const float FullFog = 0.018f;

        /// <summary>The moon starts to count just below the horizon and counts fully by 8 degrees.</summary>
        public const float MoonRiseStart = -1f;
        public const float MoonRiseEnd = 8f;

        /// <summary>Even a clear-sky full moon loses this share of its credit to the cloud it is behind.</summary>
        public const float CloudBlocksMoon = 0.85f;

        /// <summary>
        /// How much of the preset's cut a high, clear full moon gives back to its own direct light.
        /// The darkness of a real night is in the shadows and the moonless sky; a bright moon still
        /// lights treetops and casts shadows. Up to 0.1.10 the cut was flat, so Darkest kept 12%
        /// of a near-full moon and nothing it shone on read as lit.
        /// </summary>
        public const float BrightMoonKeeps = 0.75f;

        /// <summary>No multiplier ever goes below this, so a bad setting cannot black the screen out.</summary>
        public const float Floor = 0.02f;

        public static NightState Evaluate(NightInputs input, NightSettings s)
        {
            float nightness = Smoothstep(InverseLerp(s.TwilightStart, s.TwilightEnd, input.SunElevation));
            if (nightness <= 0f)
            {
                return NightState.Vanilla;
            }

            float cloud = Cloud(input);
            float fog = Clamp01(input.Fog / FullFog);
            float rain = Clamp01(input.Rain);
            float moonCredit = MoonCredit(input);

            float weather = Lerp(1f, s.Overcast, cloud) * Lerp(1f, s.FogDark, fog) * Lerp(1f, s.RainDark, rain);

            float ambientNight = Lerp(s.MoonlessAmbient, s.FullMoonAmbient, moonCredit) * weather;
            float moonlightNight = Lerp(s.Moonlight, 1f, moonCredit * BrightMoonKeeps) * Lerp(1f, s.Overcast, cloud);

            var state = new NightState
            {
                Nightness = nightness,
                Ambient = Lerp(1f, ambientNight, nightness),
                Moonlight = Lerp(1f, moonlightNight, nightness),
                // An unlit room only gets what comes in through its windows, so it darkens with
                // the night outside -- moon, cloud, fog and rain -- and then by its own share on
                // top. 0.1.3 used s.Interior alone, so a house was as bright on a moonless,
                // overcast night as under a full moon.
                Interior = Lerp(1f, s.Interior * ambientNight, nightness),
                Exposure = nightness,
            };

            if (input.NightVisionOn)
            {
                float keep = Clamp01(s.NightVisionRetention);
                state.Ambient = Lerp(state.Ambient, 1f, keep);
                state.Moonlight = Lerp(state.Moonlight, 1f, keep);
                state.Interior = Lerp(state.Interior, 1f, keep);
                state.Exposure = Lerp(state.Exposure, 0f, keep);
            }

            state.Ambient = Limit(state.Ambient);
            state.Moonlight = Limit(state.Moonlight);
            state.Interior = Limit(state.Interior);
            return state;
        }

        /// <summary>
        /// What is left of the sky and interior ambient deep in a bunker: pitch black, so only
        /// lamps light it. The multipliers act on whatever the game is drawing, which by day is
        /// daylight, so a night-sized share (0.1-0.2) would still leave a bunker lit at noon.
        /// Below the normal Floor on purpose -- a bunker is meant to be black.
        /// </summary>
        public const float BunkerLevel = 0.01f;

        /// <summary>
        /// How sealed off a place counts as, 0 (open air) to 1 (no daylight reaches it), once
        /// the time of day is taken into account. A darker room at night is what Dark Nights is
        /// for, so by default it fades in with dusk and a day is vanilla, indoors and out.
        /// Dark rooms at noon are a lighting overhaul rather than a darker night, so they are
        /// the player's choice ("byDay").
        /// </summary>
        public static float RoomDarkness(float sealedOff, float nightness, bool byDay)
        {
            sealedOff = Clamp01(sealedOff);
            return byDay ? sealedOff : sealedOff * Clamp01(nightness);
        }

        /// <summary>
        /// A bunker gets no daylight, so inside one only lamps light it, whatever the hour.
        /// "bunker" is 0 outside, 1 inside, and fades between. Each part only ever gets darker
        /// than the night already made it; moonlight and exposure are left to the night.
        /// </summary>
        public static NightState WithBunker(NightState night, NightSettings s, float bunker, bool nightVisionOn)
        {
            bunker = Clamp01(bunker);
            if (bunker <= 0f)
            {
                return night;
            }

            float ambient = BunkerLevel;
            float interior = BunkerLevel;
            if (nightVisionOn)
            {
                float keep = Clamp01(s.NightVisionRetention);
                ambient = Lerp(ambient, 1f, keep);
                interior = Lerp(interior, 1f, keep);
            }

            night.Nightness = Math.Max(night.Nightness, bunker);
            night.Ambient = Lerp(night.Ambient, Math.Min(night.Ambient, ambient), bunker);
            night.Interior = Lerp(night.Interior, Math.Min(night.Interior, interior), bunker);
            return night;
        }

        /// <summary>0 under a clear sky, 1 under full overcast, on the game's own cloudiness scale.</summary>
        private static float Cloud(NightInputs input) =>
            Clamp01(InverseLerp(ClearCloudiness, OvercastCloudiness, input.Cloudiness));

        /// <summary>How much light the moon is giving, 0 to 1: its height, its phase, and the cloud in front of it.</summary>
        private static float MoonCredit(NightInputs input)
        {
            float moonUp = Smoothstep(InverseLerp(MoonRiseStart, MoonRiseEnd, input.MoonElevation));
            return moonUp * Clamp01(input.MoonPhase) * (1f - Cloud(input) * CloudBlocksMoon);
        }

        // ------------------------------------------------------------------ bots

        /// <summary>
        /// How visible the night is to a bot, on SAIN's scale: 1 by day, 0 at its darkest.
        /// SAIN reads this off the clock hour; this reads it off the sky, so a bright full
        /// moon gives bots some sight back and an overcast moonless night takes it all.
        ///
        /// Fog and rain are deliberately left out: SAIN multiplies its own weather penalty on
        /// top of this, and counting them here as well would apply them twice. Cloud only
        /// enters as the moon it hides, which is a night-only effect SAIN does not model.
        /// </summary>
        public static float BotVisibility(NightInputs input, NightSettings s)
        {
            float nightness = Smoothstep(InverseLerp(s.TwilightStart, s.TwilightEnd, input.SunElevation));
            if (nightness <= 0f)
            {
                return 1f;
            }

            return Clamp01(1f - nightness * (1f - MoonCredit(input) * MoonShare(s)));
        }

        /// <summary>
        /// How much of a dark night a high clear full moon gives back, taken from the preset's
        /// own ambient ladder, so a darker preset makes the moon help bots less as well as you.
        /// </summary>
        public static float MoonShare(NightSettings s)
        {
            if (s.MoonlessAmbient >= 1f)
            {
                return 1f;
            }

            return Clamp01((s.FullMoonAmbient - s.MoonlessAmbient) / (1f - s.MoonlessAmbient));
        }

        /// <summary>
        /// SAIN's vision multiplier is lerp(nightMinimum, 1, visibilityRatio). Given one of its
        /// own answers, recover the minimum -- which picks up its snow setting and anything the
        /// player has configured, without reading SAIN's settings. Impossible by day (ratio 1).
        /// </summary>
        public static bool TryInferSainMinimum(float ratio, float result, out float minimum)
        {
            minimum = 0f;
            if (ratio >= 0.999f)
            {
                return false;
            }

            minimum = (result - ratio) / (1f - ratio);
            return minimum > 0f && minimum <= 1f;
        }

        /// <summary>Outdoors at night there is always some starlight and sky glow; a silhouette shows against it.</summary>
        public const float Starlight = 0.06f;

        /// <summary>A target this lit (0..1, 1 = daylight) is seen as normal. Below it, a bot's sight is capped.</summary>
        public const float LitThreshold = 0.5f;

        /// <summary>The cap just below LitThreshold. It falls from here to the pitch-black range as the light goes.</summary>
        public const float DimSightRange = 80f;

        /// <summary>
        /// Up to this share of probe rays reaching open sky, a place is a room: no sky fill at
        /// all. A room with ordinary windows, or one whose walls leak a few rays through cracks,
        /// sits below it.
        /// </summary>
        public const float RoomOpenness = 0.12f;

        /// <summary>
        /// From this share on, a place is outdoors and gets the whole sky. A doorway you stand
        /// in, a porch, a narrow alley and the open street are all above it.
        /// </summary>
        public const float OutdoorOpenness = 0.3f;

        /// <summary>
        /// Daylight ambient reaching a point, 0 to 1, from its openness. A room is dark at any
        /// hour even with windows: the sky's light comes in as the sun's beams through them,
        /// which the game draws as a shadowed light and this leaves alone, but the rest of the
        /// room gets none of the sky's even fill. Only near outdoors does the fill come back.
        /// Being flat across the whole room range also means rays flickering through cracks
        /// as you move change nothing, so the room does not pulse.
        /// </summary>
        public static float DaylightFromOpenness(float openness) =>
            Smoothstep(InverseLerp(RoomOpenness, OutdoorOpenness, Clamp01(openness)));

        /// <summary>
        /// How lit a target is where it stands, 0 (pitch black) to 1 (daylight), for a bot with
        /// no light and no NVG of its own: the sky as bots judge it (sun, moon, cloud, never
        /// below starlight), times how much of it reaches the spot. A windowless room is dark
        /// at noon; a lamp the target stands near can only add.
        /// </summary>
        public static float TargetLight(float skyVisibility, float daylight, float lampLight)
        {
            float ambient = Math.Max(Clamp01(skyVisibility), Starlight) * Clamp01(daylight);
            return Math.Max(ambient, Clamp01(lampLight));
        }

        /// <summary>
        /// The furthest a bot without light or NVG can see a target lit this much, in metres.
        /// Infinity means no cap: the target is lit well enough for normal sight.
        /// </summary>
        public static float SightCap(float targetLight, float pitchBlackRange)
        {
            if (targetLight >= LitThreshold)
            {
                return float.PositiveInfinity;
            }

            return Lerp(pitchBlackRange, DimSightRange, targetLight / LitThreshold);
        }

        /// <summary>The multiplier to hand back to SAIN in place of its clock-based one.</summary>
        public static float SainModifier(float minimum, float ratio) => Lerp(minimum, 1f, ratio);

        // ------------------------------------------------------------- hand glow

        /// <summary>
        /// The multiplier for EFT's extra hands-and-characters ambient pass. "setting" is what
        /// is left of it: 0 removes it, 1 is vanilla. By day it is vanilla unless the glow is
        /// to go day and night; at dusk it fades with the sun like everything else.
        /// </summary>
        public static float HighlightFactor(float nightness, float setting, bool byDayToo)
        {
            float keep = Clamp01(setting);
            return byDayToo ? keep : Lerp(1f, keep, nightness);
        }

        // ------------------------------------------------------------- interiors

        /// <summary>
        /// The multiplier to put on an interior's ambient colour, given its luminance. A
        /// volume already at or below the floor is left exactly as it is -- it is already dark,
        /// and darkening it further is what makes a cellar unplayable. A brighter one is scaled
        /// by the night factor but never taken below the floor.
        /// </summary>
        public static float InteriorMultiplier(float luminance, float factor, float floorLuminance)
        {
            if (factor >= 1f || luminance <= floorLuminance || luminance <= 0f)
            {
                return 1f;
            }

            return Math.Max(factor, floorLuminance / luminance);
        }

        /// <summary>
        /// Auto-exposure's upper limit with the night applied. It only ever comes down: if the
        /// game's own ceiling is already below the night ceiling, nothing changes.
        /// </summary>
        public static float ExposureCeiling(float original, float nightCeiling, float blend)
        {
            if (blend <= 0f || original <= nightCeiling)
            {
                return original;
            }

            return Lerp(original, nightCeiling, Clamp01(blend));
        }

        public static float Smoothstep(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Mathf.InverseLerp: a and b may be either way round, and a == b gives 0.</summary>
        public static float InverseLerp(float a, float b, float value)
        {
            if (a == b)
            {
                return 0f;
            }

            return Clamp01((value - a) / (b - a));
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static float Limit(float v) => v < Floor ? Floor : v > 1f ? 1f : v;
    }
}
