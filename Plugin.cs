using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AlternativeEmeraldSea
{
    public enum OceanRegion { Aestrin = 1, Chronos = 2, Both = 3 }

    internal sealed class SettingOrder { public int Order; }

    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "DogEggz.AlternativeEmeraldSea";
        // Keep the GUID/config path for upgrades from Alternative Emerald Sea.
        public const string Name = "Alternative Ocean Color";
        public const string Version = "1.2.1";

        internal static Plugin Instance { get; private set; }
        private ConfigEntry<bool> presetEnabled;
        private ConfigEntry<bool> caribbeanTurquoise;
        private ConfigEntry<bool> emeraldSea;
        private ConfigEntry<bool> winterAestrin, openChronosOcean;
        private ConfigEntry<bool> caribbeanTwilight, emeraldTwilight, winterTwilight, chronosTwilight;
        private ConfigEntry<OceanRegion> winterRegion, chronosRegion;
        private bool changingSelection;
        private Harmony harmony;
        private readonly ScopedMaterial material = new ScopedMaterial();
        private RegionalContribution contribution;
        private float dayWeight;
        private SeaPreset SelectedPreset => caribbeanTurquoise != null && caribbeanTurquoise.Value
            ? SeaPreset.CaribbeanTurquoise
            : emeraldSea != null && emeraldSea.Value ? SeaPreset.EmeraldSea : null;
        private SeaPreset AestrinPreset => PresetFor(OceanRegion.Aestrin);
        private SeaPreset ChronosPreset => PresetFor(OceanRegion.Chronos);

        private SeaPreset PresetFor(OceanRegion region)
        {
            if (winterAestrin.Value && Includes(winterRegion.Value, region)) return SeaPreset.WinterAestrin;
            if (openChronosOcean.Value && Includes(chronosRegion.Value, region)) return SeaPreset.OpenChronosOcean;
            return null;
        }

        private static bool Includes(OceanRegion selection, OceanRegion region) => (selection & region) != 0;
        internal bool Active => isActiveAndEnabled && presetEnabled != null && presetEnabled.Value;

        private void Awake()
        {
            Instance = this;
            presetEnabled = Config.Bind("General", "Enabled", true,
                "Apply selected regional clear-day and saved clear dawn/dusk palettes. Cloudy/rain/storm and night retain their original contributions. Disable Ocean Color Configurator overrides when using this mod.");
            bool saveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            caribbeanTurquoise = BindEmerald("Caribbean Turquoise", true, 4,
                "Use the saved turquoise water, green surface, reflections, and atmosphere settings. Selecting this turns Emerald Sea off. Both preset toggles off uses the game palette. Tuned at Gamma 1.0.");
            caribbeanTwilight = BindTwilight("Emerald Archipelagos", "Caribbean Turquoise", 3);
            emeraldSea = BindEmerald("Emerald Sea", false, 2,
                "Use the saved darker emerald water and reflections, with Temperature 15. Selecting this turns Caribbean Turquoise off. Both preset toggles off uses the game palette.");
            emeraldTwilight = BindTwilight("Emerald Archipelagos", "Emerald Sea", 1);
            winterAestrin = Config.Bind("Aestrin", "Winter Aestrin", false,
                Description("Enable Winter Aestrin in the region(s) selected below. Temperature -10, clear-day specular 0.35.", 6));
            winterRegion = Config.Bind("Aestrin", "Winter Aestrin - Apply to", OceanRegion.Aestrin,
                Description("Choose Aestrin, Chronos, or Both. The most recently enabled or reassigned preset takes overlapping regions; the other keeps any remaining region or turns off.", 5));
            winterTwilight = BindTwilight("Aestrin", "Winter Aestrin", 4);
            openChronosOcean = Config.Bind("Aestrin", "Open Chronos Ocean", true,
                Description("Enable the saved deep blue clear-day and clear dawn/dusk palette in the region(s) selected below.", 3));
            chronosRegion = Config.Bind("Aestrin", "Open Chronos Ocean - Apply to", OceanRegion.Chronos,
                Description("Choose Aestrin, Chronos, or Both. The most recently enabled or reassigned preset takes overlapping regions; the other keeps any remaining region or turns off.", 2));
            chronosTwilight = BindTwilight("Aestrin", "Open Chronos Ocean", 1);
            if (!Enum.IsDefined(typeof(OceanRegion), winterRegion.Value)) winterRegion.Value = OceanRegion.Aestrin;
            if (!Enum.IsDefined(typeof(OceanRegion), chronosRegion.Value)) chronosRegion.Value = OceanRegion.Chronos;
            // A hand-edited conflicting config has no last action: Chronos wins.
            ResolveOverlap(openChronosOcean, chronosRegion, winterAestrin, winterRegion);
            // Deterministic recovery if a hand-edited config enables both.
            if (caribbeanTurquoise.Value && emeraldSea.Value) emeraldSea.Value = false;
            presetEnabled.SettingChanged += SettingsChanged;
            caribbeanTurquoise.SettingChanged += SettingsChanged;
            emeraldSea.SettingChanged += SettingsChanged;
            winterAestrin.SettingChanged += SettingsChanged;
            openChronosOcean.SettingChanged += SettingsChanged;
            winterRegion.SettingChanged += SettingsChanged;
            chronosRegion.SettingChanged += SettingsChanged;
            caribbeanTwilight.SettingChanged += SettingsChanged;
            emeraldTwilight.SettingChanged += SettingsChanged;
            winterTwilight.SettingChanged += SettingsChanged;
            chronosTwilight.SettingChanged += SettingsChanged;
            Config.SaveOnConfigSet = saveOnSet;
            Config.Save();
            PaletteRegistry.Clear();
            harmony = new Harmony(Guid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo(Name + " " + Version + " loaded: Emerald, Aestrin, and Chronos regional presets.");
        }

        private static ConfigDescription Description(string text, int order) =>
            new ConfigDescription(text, null, new SettingOrder { Order = order });

        private ConfigEntry<bool> BindTwilight(string section, string preset, int order) =>
            Config.Bind(section, preset + " - Use Custom Dusk/Dawn Colors", true,
                Description("Use this preset's custom ocean colors at clear dawn and dusk. Disable to use Sailwind's vanilla twilight colors while retaining the preset's clear-day colors. Follows this preset's selected region(s).", order));

        private ConfigEntry<bool> BindEmerald(string key, bool fallback, int order, string description)
        {
            // Consume the legacy entry, using it only when the new category has
            // no saved value. Remove the old entry so it does not appear in UI.
            var old = Config.Bind("Presets", key, fallback);
            var entry = Config.Bind("Emerald Archipelagos", key, old.Value, Description(description, order));
            Config.Remove(old.Definition);
            return entry;
        }

        private static void ResolveOverlap(ConfigEntry<bool> winner, ConfigEntry<OceanRegion> winnerRegion,
            ConfigEntry<bool> other, ConfigEntry<OceanRegion> otherRegion)
        {
            if (!winner.Value || !other.Value || !Includes(winnerRegion.Value, otherRegion.Value)) return;
            var remaining = otherRegion.Value & ~winnerRegion.Value;
            if (remaining == 0) other.Value = false;
            else otherRegion.Value = remaining;
        }

        private void SettingsChanged(object sender, EventArgs args)
        {
            if (changingSelection) return;
            changingSelection = true;
            try
            {
                if (sender == caribbeanTurquoise && caribbeanTurquoise.Value) emeraldSea.Value = false;
                else if (sender == emeraldSea && emeraldSea.Value) caribbeanTurquoise.Value = false;
                if (sender == winterAestrin || sender == winterRegion)
                    ResolveOverlap(winterAestrin, winterRegion, openChronosOcean, chronosRegion);
                else if (sender == openChronosOcean || sender == chronosRegion)
                    ResolveOverlap(openChronosOcean, chronosRegion, winterAestrin, winterRegion);
                material.ApplyCurrent(GetMaterialOverrides());
            }
            finally { changingSelection = false; }
        }

        internal void ApplyPalette(ref OceanColorPalette palette, RegionalContribution source, float dawn, float night)
        {
            contribution = source;
            float daylight = 1f - Mathf.Clamp01(night);
            float twilight = Mathf.Clamp01(dawn) * daylight;
            dayWeight = (1f - Mathf.Clamp01(dawn)) * daylight;
            if (!Active) return;
            // The game has already blended regions, weather, dawn, and night.
            // Replace only each selected clear endpoint's weighted contribution.
            var emerald = SelectedPreset;
            var aestrin = AestrinPreset;
            var chronos = ChronosPreset;
            source.Emerald.Apply(ref palette, emerald, dayWeight, CustomTwilightEnabled(emerald) ? twilight : 0f);
            source.Aestrin.Apply(ref palette, aestrin, dayWeight, CustomTwilightEnabled(aestrin) ? twilight : 0f);
            source.Chronos.Apply(ref palette, chronos, dayWeight, CustomTwilightEnabled(chronos) ? twilight : 0f);
        }

        private bool CustomTwilightEnabled(SeaPreset preset)
        {
            if (preset == SeaPreset.CaribbeanTurquoise) return caribbeanTwilight.Value;
            if (preset == SeaPreset.EmeraldSea) return emeraldTwilight.Value;
            if (preset == SeaPreset.WinterAestrin) return winterTwilight.Value;
            if (preset == SeaPreset.OpenChronosOcean) return chronosTwilight.Value;
            return false;
        }

        private MaterialOverrides GetMaterialOverrides()
        {
            var value = new MaterialOverrides();
            if (!Active) return value;
            value.Add(SelectedPreset, contribution.Emerald.Weight * dayWeight);
            value.Add(AestrinPreset, contribution.Aestrin.Weight * dayWeight);
            value.Add(ChronosPreset, contribution.Chronos.Weight * dayWeight);
            return value;
        }

        internal void ApplyMaterial(Material ocean)
        {
            material.Apply(ocean, GetMaterialOverrides());
        }

        private void OnDisable() { material.Restore(); }

        private void OnDestroy()
        {
            if (presetEnabled != null) presetEnabled.SettingChanged -= SettingsChanged;
            if (caribbeanTurquoise != null) caribbeanTurquoise.SettingChanged -= SettingsChanged;
            if (emeraldSea != null) emeraldSea.SettingChanged -= SettingsChanged;
            if (winterAestrin != null) winterAestrin.SettingChanged -= SettingsChanged;
            if (openChronosOcean != null) openChronosOcean.SettingChanged -= SettingsChanged;
            if (winterRegion != null) winterRegion.SettingChanged -= SettingsChanged;
            if (chronosRegion != null) chronosRegion.SettingChanged -= SettingsChanged;
            if (caribbeanTwilight != null) caribbeanTwilight.SettingChanged -= SettingsChanged;
            if (emeraldTwilight != null) emeraldTwilight.SettingChanged -= SettingsChanged;
            if (winterTwilight != null) winterTwilight.SettingChanged -= SettingsChanged;
            if (chronosTwilight != null) chronosTwilight.SettingChanged -= SettingsChanged;
            material.Restore();
            harmony?.UnpatchSelf();
            PaletteRegistry.Clear();
            if (Instance == this) Instance = null;
        }
    }

    [HarmonyPatch(typeof(WeatherSet), nameof(WeatherSet.BlendSets))]
    internal static class BlendPatch
    {
        internal static void Prefix(WeatherSet setOne, WeatherSet setTwo, float lerp, out RegionalContribution __state)
        {
            __state = RegionalContribution.Blend(PaletteRegistry.Get(setOne), PaletteRegistry.Get(setTwo), lerp);
        }
        internal static void Postfix(ref WeatherSet blendedSet, RegionalContribution __state)
        {
            PaletteRegistry.Set(blendedSet, __state);
        }
    }

    [HarmonyPatch(typeof(WeatherSet), nameof(WeatherSet.CopyFrom))]
    internal static class CopyPatch
    {
        internal static void Prefix(WeatherSet originSet, out RegionalContribution __state)
        {
            __state = PaletteRegistry.Get(originSet);
        }
        internal static void Postfix(ref WeatherSet targetSet, RegionalContribution __state)
        {
            PaletteRegistry.Set(targetSet, __state);
        }
    }

    [HarmonyPatch(typeof(Weather), "ApplyWeather")]
    internal static class WeatherPatch
    {
        internal static void Prefix(ref OceanColorPalette ___finalPalette, WeatherSet ___previousWeatherSet,
            WeatherSet ___targetWeatherSet, float ___lerp)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || Sun.sun == null) return;
            plugin.ApplyPalette(ref ___finalPalette,
                RegionalContribution.Blend(PaletteRegistry.Get(___previousWeatherSet),
                    PaletteRegistry.Get(___targetWeatherSet), ___lerp),
                Sun.sun.GetDawnLerp(), Sun.sun.GetNightLerp());
        }
    }

    [HarmonyPatch(typeof(RegionBlender), "Start")]
    internal static class RegionStartPatch
    {
        internal static void Prefix(RegionBlender __instance, out Region __state)
        {
            __state = PaletteRegistry.SourceRegion;
            PaletteRegistry.SourceRegion = __instance.initialRegion;
        }
        internal static void Finalizer(Region __state) { PaletteRegistry.SourceRegion = __state; }
    }

    [HarmonyPatch(typeof(RegionBlender), "UpdateBlend")]
    internal static class RegionBlendPatch
    {
        internal static void Prefix(Region ___currentTargetRegion, out Region __state)
        {
            __state = PaletteRegistry.SourceRegion;
            PaletteRegistry.SourceRegion = ___currentTargetRegion;
        }
        internal static void Finalizer(Region __state) { PaletteRegistry.SourceRegion = __state; }
    }

    [HarmonyPatch(typeof(OceanColorBlender), nameof(OceanColorBlender.ApplyPalette))]
    internal static class MaterialPatch
    {
        internal static void Postfix(Material ___crestMaterial) { Plugin.Instance?.ApplyMaterial(___crestMaterial); }
    }

    // Fixed, versioned snapshots. Null fields are Auto and release any override
    // owned by the other preset when the player switches between them.
    internal sealed class SeaPreset
    {
        internal readonly Color Water, Surface;
        internal readonly float Temperature, BaseScattering;
        internal readonly Color? Scattering, Fog, SkyTint, Shallow, TowardSun, AwayFromSun;
        internal readonly float? Tint, Specular, Atmosphere, Exposure, SunScattering;
        internal readonly Color? DawnWater, DawnSurface, DawnScattering;

        private SeaPreset(Color water, Color surface, float temperature, float baseScattering,
            Color? scattering = null, Color? fog = null, Color? skyTint = null,
            Color? shallow = null, Color? towardSun = null, Color? awayFromSun = null,
            float? tint = null, float? specular = null, float? atmosphere = null,
            float? exposure = null, float? sunScattering = null,
            Color? dawnWater = null, Color? dawnSurface = null, Color? dawnScattering = null)
        {
            Water = water; Surface = surface; Temperature = temperature; BaseScattering = baseScattering;
            Scattering = scattering; Fog = fog; SkyTint = skyTint; Shallow = shallow;
            TowardSun = towardSun; AwayFromSun = awayFromSun;
            Tint = tint; Specular = specular; Atmosphere = atmosphere; Exposure = exposure;
            SunScattering = sunScattering;
            DawnWater = dawnWater; DawnSurface = dawnSurface; DawnScattering = dawnScattering;
        }

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        // Latest saved configurator values, including Temperature 15.
        internal static readonly SeaPreset EmeraldSea = new SeaPreset(
            Rgb(11, 143, 145), Rgb(26, 127, 119), 15f, 0.4f,
            scattering: Rgb(2, 113, 113),
            shallow: Rgb(50, 159, 141),
            towardSun: Rgb(171, 213, 239), awayFromSun: Rgb(74, 126, 140),
            specular: 0.4f, atmosphere: 0.8f, exposure: 1.6f, sunScattering: 0f,
            dawnWater: Rgb(40, 111, 112), dawnSurface: Rgb(100, 138, 130), dawnScattering: Rgb(52, 123, 115));

        // Captured from Pete's saved configurator settings on 2026-09-18.
        // Gamma 1.0 is an external baseline; this mod does not change Gamma.
        internal static readonly SeaPreset CaribbeanTurquoise = new SeaPreset(
            Rgb(0, 184, 200), Rgb(0, 184, 168), 5f, 0.4f,
            scattering: Rgb(0, 136, 159), fog: Rgb(181, 220, 224), skyTint: Rgb(106, 168, 184),
            shallow: Rgb(66, 190, 198),
            towardSun: Rgb(199, 220, 226), awayFromSun: Rgb(96, 127, 145),
            tint: 0f, specular: 0.4f, atmosphere: 0.8f, exposure: 1.6f, sunScattering: 0f,
            dawnWater: Rgb(40, 126, 136), dawnSurface: Rgb(107, 151, 153), dawnScattering: Rgb(63, 145, 149));

        // Winter's muted slate-blue twilight palette was approved for 1.2.1.
        internal static readonly SeaPreset WinterAestrin = new SeaPreset(
            Rgb(36, 85, 138), Rgb(66, 108, 149), -10f, 0.35f,
            scattering: Rgb(52, 114, 166), shallow: Rgb(79, 135, 184),
            towardSun: Rgb(187, 205, 229), awayFromSun: Rgb(69, 94, 136), tint: 0f, specular: 0.35f,
            dawnWater: Rgb(56, 95, 131), dawnSurface: Rgb(113, 134, 158), dawnScattering: Rgb(79, 126, 159));

        internal static readonly SeaPreset OpenChronosOcean = new SeaPreset(
            Rgb(16, 51, 104), Rgb(36, 74, 118), 0f, 0.25f,
            scattering: Rgb(27, 83, 141), shallow: Rgb(53, 111, 163),
            towardSun: Rgb(169, 190, 216), awayFromSun: Rgb(52, 79, 115), tint: 0f, specular: 0.4f,
            dawnWater: Rgb(39, 74, 118), dawnSurface: Rgb(83, 106, 137), dawnScattering: Rgb(54, 95, 141));
    }

    internal struct PaletteContribution
    {
        internal float Weight;
        internal Color Water;
        internal Color Surface;
        internal Color Scattering;
        internal Color DawnWater, DawnSurface, DawnScattering;
        internal Color Fog;
        internal float Temperature;
        internal float Tint, Specular;
        internal Color SkyTint;
        internal float Atmosphere, Exposure;
        internal float SkyTintWeight, AtmosphereWeight, ExposureWeight;
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int AtmosphereId = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        internal static PaletteContribution From(WeatherSet source)
        {
            OceanColorPalette day = source.dayPalette, dawn = source.dawnPalette;
            var value = new PaletteContribution { Weight = 1f, Water = day.waterColor,
                Surface = day.surfaceColor, Scattering = day.scatteringColor, Temperature = day.temperature,
                DawnWater = dawn.waterColor, DawnSurface = dawn.surfaceColor, DawnScattering = dawn.scatteringColor,
                Fog = day.fog, Tint = day.tint, Specular = day.oceanSpecular };
            Material sky = day.skyMaterial;
            if (sky != null)
            {
                if (sky.HasProperty(SkyTintId)) { value.SkyTint = sky.GetColor(SkyTintId); value.SkyTintWeight = 1f; }
                if (sky.HasProperty(AtmosphereId)) { value.Atmosphere = sky.GetFloat(AtmosphereId); value.AtmosphereWeight = 1f; }
                if (sky.HasProperty(ExposureId)) { value.Exposure = sky.GetFloat(ExposureId); value.ExposureWeight = 1f; }
            }
            return value;
        }

        internal static PaletteContribution Blend(PaletteContribution one, PaletteContribution two, float lerp)
        {
            float t = Mathf.Clamp01(lerp), a = 1f - t;
            return new PaletteContribution { Weight = one.Weight * a + two.Weight * t,
                Water = one.Water * a + two.Water * t, Surface = one.Surface * a + two.Surface * t,
                Scattering = one.Scattering * a + two.Scattering * t,
                DawnWater = one.DawnWater * a + two.DawnWater * t,
                DawnSurface = one.DawnSurface * a + two.DawnSurface * t,
                DawnScattering = one.DawnScattering * a + two.DawnScattering * t,
                Temperature = one.Temperature * a + two.Temperature * t,
                Fog = one.Fog * a + two.Fog * t, Tint = one.Tint * a + two.Tint * t,
                Specular = one.Specular * a + two.Specular * t,
                SkyTint = one.SkyTint * a + two.SkyTint * t,
                Atmosphere = one.Atmosphere * a + two.Atmosphere * t,
                Exposure = one.Exposure * a + two.Exposure * t,
                SkyTintWeight = one.SkyTintWeight * a + two.SkyTintWeight * t,
                AtmosphereWeight = one.AtmosphereWeight * a + two.AtmosphereWeight * t,
                ExposureWeight = one.ExposureWeight * a + two.ExposureWeight * t };
        }

        internal void Apply(ref OceanColorPalette palette, SeaPreset preset, float day, float dawn)
        {
            if (preset == null || Weight <= 0f) return;
            palette.waterColor = ReplaceRgb(palette.waterColor, Water, preset.Water, Weight, day);
            palette.surfaceColor = ReplaceRgb(palette.surfaceColor, Surface, preset.Surface, Weight, day);
            if (preset.Scattering.HasValue)
                palette.scatteringColor = ReplaceRgb(palette.scatteringColor, Scattering, preset.Scattering.Value, Weight, day);
            if (preset.Fog.HasValue)
                palette.fog = ReplaceRgb(palette.fog, Fog, preset.Fog.Value, Weight, day);
            palette.temperature += (preset.Temperature * Weight - Temperature) * day;
            if (preset.Tint.HasValue) palette.tint += (preset.Tint.Value * Weight - Tint) * day;
            if (preset.Specular.HasValue) palette.oceanSpecular += (preset.Specular.Value * Weight - Specular) * day;
            if (preset.DawnWater.HasValue)
                palette.waterColor = ReplaceRgb(palette.waterColor, DawnWater, preset.DawnWater.Value, Weight, dawn);
            if (preset.DawnSurface.HasValue)
                palette.surfaceColor = ReplaceRgb(palette.surfaceColor, DawnSurface, preset.DawnSurface.Value, Weight, dawn);
            if (preset.DawnScattering.HasValue)
                palette.scatteringColor = ReplaceRgb(palette.scatteringColor, DawnScattering, preset.DawnScattering.Value, Weight, dawn);
            ApplySky(palette.skyMaterial, preset, day);
        }

        internal void ApplySky(Material sky, SeaPreset preset, float day)
        {
            if (sky == null) return;
            if (preset.SkyTint.HasValue && SkyTintWeight > 0f && sky.HasProperty(SkyTintId))
                sky.SetColor(SkyTintId, ReplaceRgb(sky.GetColor(SkyTintId), SkyTint,
                    preset.SkyTint.Value, SkyTintWeight, day));
            if (preset.Atmosphere.HasValue && AtmosphereWeight > 0f && sky.HasProperty(AtmosphereId))
                sky.SetFloat(AtmosphereId, sky.GetFloat(AtmosphereId) +
                    (preset.Atmosphere.Value * AtmosphereWeight - Atmosphere) * day);
            if (preset.Exposure.HasValue && ExposureWeight > 0f && sky.HasProperty(ExposureId))
                sky.SetFloat(ExposureId, sky.GetFloat(ExposureId) +
                    (preset.Exposure.Value * ExposureWeight - Exposure) * day);
        }

        internal static Color ReplaceRgb(Color current, Color weightedOriginal, Color configured, float weight, float day)
        {
            current.r += (configured.r * weight - weightedOriginal.r) * day;
            current.g += (configured.g * weight - weightedOriginal.g) * day;
            current.b += (configured.b * weight - weightedOriginal.b) * day;
            return current; // Preserve the game's blended alpha.
        }
    }

    internal struct RegionalContribution
    {
        internal PaletteContribution Emerald, Aestrin, Chronos;

        internal static RegionalContribution Blend(RegionalContribution one, RegionalContribution two, float t)
        {
            return new RegionalContribution {
                Emerald = PaletteContribution.Blend(one.Emerald, two.Emerald, t),
                Aestrin = PaletteContribution.Blend(one.Aestrin, two.Aestrin, t),
                Chronos = PaletteContribution.Blend(one.Chronos, two.Chronos, t) };
        }
    }

    internal static class PaletteRegistry
    {
        private sealed class Entry { internal RegionalContribution Value; }
        private static ConditionalWeakTable<WeatherSet, Entry> entries = new ConditionalWeakTable<WeatherSet, Entry>();
        // Only active while the game's RegionBlender copies/blends source assets.
        // Aestrin and Chronos share the same W medi Clear instance, so a source
        // asset must never be cached with a permanent region identity.
        internal static Region SourceRegion;

        internal static void Clear()
        {
            entries = new ConditionalWeakTable<WeatherSet, Entry>();
            SourceRegion = null;
        }

        internal static RegionalContribution Get(WeatherSet set)
        {
            if (set == null) return default(RegionalContribution);
            if (SourceRegion != null && set == SourceRegion.clearWeather)
            {
                var value = new RegionalContribution();
                if (Named(SourceRegion.name, "Region Medi East")) value.Chronos = PaletteContribution.From(set);
                else if (Named(SourceRegion.name, "Region Medi")) value.Aestrin = PaletteContribution.From(set);
                else if (Named(set.name, "W emerald Clear")) value.Emerald = PaletteContribution.From(set);
                return value;
            }
            if (entries.TryGetValue(set, out Entry stored)) return stored.Value;
            // Direct Emerald source reads remain supported. Medi sources require
            // regional context; all later weather snapshots carry that provenance.
            return Named(set.name, "W emerald Clear")
                ? new RegionalContribution { Emerald = PaletteContribution.From(set) }
                : default(RegionalContribution);
        }

        private static bool Named(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actual, expected + "(Clone)", StringComparison.OrdinalIgnoreCase);
        }

        internal static void Set(WeatherSet set, RegionalContribution value)
        {
            if (set == null) return;
            if (entries.TryGetValue(set, out Entry stored)) stored.Value = value;
            else entries.Add(set, new Entry { Value = value });
            // Store zero too, so reused snapshots forget old scope. Weak keys do
            // not retain destroyed scene objects or rely on reusable instance IDs.
        }
    }

    internal struct WeightedColor
    {
        internal Color Sum;
        internal float Weight;
        internal void Add(Color? color, float weight)
        {
            if (!color.HasValue) return;
            Sum += color.Value * weight; Weight += weight;
        }
    }

    internal struct WeightedNumber
    {
        internal float Sum, Weight;
        internal void Add(float? number, float weight)
        {
            if (!number.HasValue) return;
            Sum += number.Value * weight; Weight += weight;
        }
    }

    internal struct MaterialOverrides
    {
        internal WeightedColor Shallow, Toward, Away;
        internal WeightedNumber Scattering, Sun;
        internal void Add(SeaPreset preset, float weight)
        {
            if (preset == null || weight <= 0f) return;
            Shallow.Add(preset.Shallow, weight);
            Toward.Add(preset.TowardSun, weight);
            Away.Add(preset.AwayFromSun, weight);
            Scattering.Add(preset.BaseScattering, weight);
            Sun.Add(preset.SunScattering, weight);
        }
    }

    internal struct OwnedValue<T> where T : struct
    {
        private bool applied;
        private T original;
        private T last;
        internal T Baseline(T current)
        {
            return applied && EqualityComparer<T>.Default.Equals(current, last) ? original : current;
        }
        internal bool Update(T current, bool active, T configured, out T next)
        {
            bool owned = applied && EqualityComparer<T>.Default.Equals(current, last);
            next = current;
            if (active)
            {
                if (!owned) original = current;
                next = configured; last = next; applied = true;
            }
            else
            {
                if (owned) next = original;
                applied = false;
            }
            return !EqualityComparer<T>.Default.Equals(current, next);
        }
    }

    internal sealed class ScopedMaterial
    {
        private static readonly int ShallowId = Shader.PropertyToID("_SubSurfaceShallowCol");
        private static readonly int ScatteringId = Shader.PropertyToID("_SubSurfaceBase");
        private static readonly int SunId = Shader.PropertyToID("_SubSurfaceSun");
        private static readonly int TowardId = Shader.PropertyToID("_SkyTowardsSun");
        private static readonly int AwayId = Shader.PropertyToID("_SkyAwayFromSun");
        private Material current;
        private OwnedValue<Color> shallow;
        private OwnedValue<float> scattering;
        private OwnedValue<float> sun;
        private OwnedValue<Color> toward, away;

        internal void Apply(Material material, MaterialOverrides values)
        {
            if (current != material)
            {
                Restore();
                current = material;
            }
            ApplyCurrent(values);
        }

        internal void ApplyCurrent(MaterialOverrides values)
        {
            if (current == null) return;
            ApplyColor(ShallowId, ref shallow, values.Shallow);
            ApplyColor(TowardId, ref toward, values.Toward);
            ApplyColor(AwayId, ref away, values.Away);
            ApplyNumber(ScatteringId, ref scattering, values.Scattering);
            ApplyNumber(SunId, ref sun, values.Sun);
        }

        private void ApplyColor(int id, ref OwnedValue<Color> state, WeightedColor configured)
        {
            if (!current.HasProperty(id)) { state = default(OwnedValue<Color>); return; }
            Color live = current.GetColor(id), baseline = state.Baseline(live);
            Color desired = baseline * (1f - Mathf.Clamp01(configured.Weight)) + configured.Sum; desired.a = baseline.a;
            if (state.Update(live, configured.Weight > 0f, desired, out Color next)) current.SetColor(id, next);
        }

        private void ApplyNumber(int id, ref OwnedValue<float> state, WeightedNumber configured)
        {
            if (!current.HasProperty(id)) { state = default(OwnedValue<float>); return; }
            float live = current.GetFloat(id), baseline = state.Baseline(live);
            float desired = baseline * (1f - Mathf.Clamp01(configured.Weight)) + configured.Sum;
            if (state.Update(live, configured.Weight > 0f, desired, out float next)) current.SetFloat(id, next);
        }

        internal void Restore()
        {
            ApplyCurrent(default(MaterialOverrides));
            current = null;
            shallow = default(OwnedValue<Color>);
            scattering = default(OwnedValue<float>);
            sun = default(OwnedValue<float>);
            toward = default(OwnedValue<Color>);
            away = default(OwnedValue<Color>);
        }
    }
}
