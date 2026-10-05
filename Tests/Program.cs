using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AlternativeEmeraldSea;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

internal static class Program
{
    private const string TwilightKey = " - Use Custom Dusk/Dawn Colors";
    private static readonly string[] Names = { "Caribbean Turquoise", "Emerald Sea", "Winter Aestrin", "Open Chronos Ocean" };
    private static readonly string[] Files = { "CaribbeanTurquoise", "EmeraldSea", "WinterAestrin", "OpenChronosOcean" };
    private static readonly List<object> Results = new List<object>();
    private static readonly string ResultDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "results"));
    private static int failures, fixtureCount;
    private static readonly OceanColorPalette Day = Palette("#819EB7", "#93ADBE", "#678B9E", .6f);
    private static readonly OceanColorPalette Dawn = Palette("#A47F83", "#BA9591", "#936C7E", .4f);
    private static readonly OceanColorPalette Night = Palette("#182235", "#293043", "#23334B", .2f);

    private static int Main()
    {
        Directory.CreateDirectory(ResultDir);
        Run("four new toggles default On and follow each preset's controls", () => WithPlugin((plugin, config) =>
        {
            for (int i = 0; i < 4; i++) Assert(Toggle(config, i).Value, Names[i] + " default");
            Ordered(config, "Emerald Archipelagos", "Caribbean Turquoise", "Caribbean Turquoise" + TwilightKey,
                "Emerald Sea", "Emerald Sea" + TwilightKey);
            Ordered(config, "Aestrin", "Winter Aestrin", "Winter Aestrin - Apply to", "Winter Aestrin" + TwilightKey,
                "Open Chronos Ocean", "Open Chronos Ocean - Apply to", "Open Chronos Ocean" + TwilightKey);
        }));

        for (int i = 0; i < 4; i++)
        {
            int preset = i;
            Run(Names[i] + ": twilight On uses the saved endpoint; Off restores vanilla", () => WithPlugin((plugin, config) =>
            {
                Select(config, preset);
                var source = Source(preset);
                Toggle(config, preset).Value = true;
                var actual = Dawn;
                plugin.ApplyPalette(ref actual, source, 1f, 0f);
                EqualColors(actual, ExpectedPreset(preset, "5 - Clear Dawn"));
                EqualOtherFields(actual, Dawn);
                Toggle(config, preset).Value = false;
                actual = Dawn;
                plugin.ApplyPalette(ref actual, source, 1f, 0f);
                EqualPalette(actual, Dawn);
            }));
            Run(Names[i] + ": clear day is identical with twilight On and Off", () => WithPlugin((plugin, config) =>
            {
                Select(config, preset);
                var on = Day;
                plugin.ApplyPalette(ref on, Source(preset), 0f, 0f);
                EqualColors(on, ExpectedPreset(preset, "2 - Ocean"));
                Toggle(config, preset).Value = false;
                var off = Day;
                plugin.ApplyPalette(ref off, Source(preset), 0f, 0f);
                EqualPalette(off, on);
            }));
            Run(Names[i] + ": night and zero clear-weather contribution stay vanilla", () => WithPlugin((plugin, config) =>
            {
                Select(config, preset);
                foreach (bool enabled in new[] { true, false })
                {
                    Toggle(config, preset).Value = enabled;
                    var actual = Night;
                    plugin.ApplyPalette(ref actual, Source(preset), .8f, 1f);
                    EqualPalette(actual, Night);
                    actual = Dawn;
                    plugin.ApplyPalette(ref actual, default(RegionalContribution), 1f, 0f);
                    EqualPalette(actual, Dawn);
                }
            }));
            Run(Names[i] + ": mixed twilight/night and partial clear weather blend correctly", () => WithPlugin((plugin, config) =>
            {
                Select(config, preset);
                foreach (float clear in new[] { 1f, .35f })
                foreach (bool enabled in new[] { true, false })
                {
                    Toggle(config, preset).Value = enabled;
                    var baseline = Mix(Day, Dawn, Night, .48f, .32f, .2f);
                    var actual = baseline;
                    plugin.ApplyPalette(ref actual, Source(preset, clear), .4f, .2f);
                    var expected = Mix(ExpectedPreset(preset, "2 - Ocean"),
                        enabled ? ExpectedPreset(preset, "5 - Clear Dawn") : Dawn, Night, .48f, .32f, .2f);
                    EqualColors(actual, Lerp(baseline, expected, clear));
                    EqualAlpha(actual, baseline);
                }
            }));
            Run(Names[i] + ": changing twilight does not change daytime material settings", () => WithPlugin((plugin, config) =>
            {
                Select(config, preset);
                var palette = Day;
                plugin.ApplyPalette(ref palette, Source(preset), 0f, 0f);
                var material = MaterialFixture();
                plugin.ApplyMaterial(material);
                Color shallow = material.GetColor(Shader.PropertyToID("_SubSurfaceShallowCol"));
                float scattering = material.GetFloat(Shader.PropertyToID("_SubSurfaceBase"));
                Toggle(config, preset).Value = false;
                EqualColor(material.GetColor(Shader.PropertyToID("_SubSurfaceShallowCol")), shallow);
                Near(material.GetFloat(Shader.PropertyToID("_SubSurfaceBase")), scattering);
            }));
        }

        for (int mask = 0; mask < 16; mask++)
        {
            int selection = mask;
            Run("independent toggle combination " + Convert.ToString(mask, 2).PadLeft(4, '0'), () => WithPlugin((plugin, config) =>
            {
                Set(config, "Aestrin", "Winter Aestrin", true);
                for (int i = 0; i < 4; i++) Toggle(config, i).Value = (selection & (1 << i)) != 0;
                foreach (int emerald in new[] { 0, 1 })
                {
                    Set(config, "Emerald Archipelagos", Names[emerald], true);
                    var source = RegionalContribution.Blend(Source(emerald),
                        RegionalContribution.Blend(Source(2), Source(3), .4f), .6f);
                    var actual = Dawn;
                    plugin.ApplyPalette(ref actual, source, 1f, 0f);
                    OceanColorPalette Endpoint(int index) => Toggle(config, index).Value
                        ? ExpectedPreset(index, "5 - Clear Dawn") : Dawn;
                    var expected = Lerp(Endpoint(emerald), Lerp(Endpoint(2), Endpoint(3), .4f), .6f);
                    EqualColors(actual, expected);
                    EqualAlpha(actual, Dawn);
                }
            }));
        }

        foreach (int preset in new[] { 2, 3 })
        foreach (OceanRegion region in new[] { OceanRegion.Aestrin, OceanRegion.Chronos, OceanRegion.Both })
        {
            int chosen = preset;
            OceanRegion destination = region;
            Run(Names[preset] + ": toggle follows Apply to " + region, () => WithPlugin((plugin, config) =>
            {
                Set(config, "Aestrin", Names[2], false);
                Set(config, "Aestrin", Names[3], false);
                Set(config, "Aestrin", Names[chosen] + " - Apply to", destination);
                Set(config, "Aestrin", Names[chosen], true);
                foreach (bool enabled in new[] { true, false })
                foreach (OceanRegion target in new[] { OceanRegion.Aestrin, OceanRegion.Chronos })
                {
                    Toggle(config, chosen).Value = enabled;
                    var source = target == OceanRegion.Aestrin ? Source(2) : Source(3);
                    var actual = Dawn;
                    plugin.ApplyPalette(ref actual, source, 1f, 0f);
                    EqualColors(actual, enabled && (destination & target) != 0
                        ? ExpectedPreset(chosen, "5 - Clear Dawn") : Dawn);
                }
            }));
        }

        Run("master switch suppresses custom day and twilight colors", () => WithPlugin((plugin, config) =>
        {
            Set(config, "General", "Enabled", false);
            var actual = Day;
            plugin.ApplyPalette(ref actual, Source(0), 0f, 0f);
            EqualPalette(actual, Day);
            actual = Dawn;
            plugin.ApplyPalette(ref actual, Source(0), 1f, 0f);
            EqualPalette(actual, Dawn);
        }));
        Run("all four toggle values survive real BepInEx save/reload without changing selections", () =>
        {
            string path = Path.Combine(ResultDir, "persistence.cfg");
            File.WriteAllText(path, "[General]\nEnabled = true\n[Emerald Archipelagos]\nCaribbean Turquoise = false\nEmerald Sea = true\n[Aestrin]\nWinter Aestrin = true\nWinter Aestrin - Apply to = Both\nOpen Chronos Ocean = false\n");
            WithPlugin((plugin, config) =>
            {
                for (int i = 0; i < 4; i++) Toggle(config, i).Value = false;
                config.Save();
            }, path);
            WithPlugin((plugin, config) =>
            {
                for (int i = 0; i < 4; i++) Assert(!Toggle(config, i).Value, "saved Off for " + Names[i]);
                Assert(!Get<bool>(config, "Emerald Archipelagos", Names[0]).Value, "Caribbean selection");
                Assert(Get<bool>(config, "Emerald Archipelagos", Names[1]).Value, "Emerald selection");
                Assert(Get<bool>(config, "Aestrin", Names[2]).Value, "Winter selection");
                Assert(Get<OceanRegion>(config, "Aestrin", Names[2] + " - Apply to").Value == OceanRegion.Both, "Both assignment");
                Assert(!Get<bool>(config, "Aestrin", Names[3]).Value, "Open Chronos selection");
                var actual = Dawn;
                plugin.ApplyPalette(ref actual, Source(2), 1f, 0f);
                EqualPalette(actual, Dawn);
            }, path);
        });
        Run("1.2.0 upgrade preserves selections and defaults new toggles On", () =>
        {
            string path = Path.Combine(ResultDir, "upgrade.cfg");
            File.WriteAllText(path, "[General]\nEnabled = false\n[Emerald Archipelagos]\nCaribbean Turquoise = false\nEmerald Sea = true\n[Aestrin]\nWinter Aestrin = true\nWinter Aestrin - Apply to = Chronos\nOpen Chronos Ocean = false\nOpen Chronos Ocean - Apply to = Aestrin\n");
            WithPlugin((plugin, config) =>
            {
                Assert(!Get<bool>(config, "General", "Enabled").Value, "saved master switch");
                Assert(Get<bool>(config, "Emerald Archipelagos", Names[1]).Value, "saved Emerald selection");
                Assert(Get<OceanRegion>(config, "Aestrin", Names[2] + " - Apply to").Value == OceanRegion.Chronos, "saved Winter routing");
                Assert(!Get<bool>(config, "Aestrin", Names[3]).Value, "saved disabled Chronos preset");
                for (int i = 0; i < 4; i++) Assert(Toggle(config, i).Value, "new default On");
            }, path);
        });

        File.WriteAllText(Path.Combine(ResultDir, "results.json"), JsonSerializer.Serialize(
            new { passed = Results.Count - failures, failed = failures, validation = "Managed source fixture; native Unity and Harmony boundaries replaced", results = Results },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Results.Count - failures} passed, {failures} failed. Results: {ResultDir}");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action action)
    {
        try { action(); Results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error); }
    }
    private static void WithPlugin(Action<Plugin, ConfigFile> action, string path = null)
    {
        if (path == null)
        {
            path = Path.Combine(ResultDir, "fixture-" + ++fixtureCount + ".cfg");
            File.WriteAllText(path, "");
        }
        var config = new ConfigFile(path, false);
        BaseUnityPlugin.NextConfig = config;
        var plugin = new Plugin();
        try { Invoke(plugin, "Awake"); action(plugin, config); }
        finally { Invoke(plugin, "OnDestroy"); }
    }
    private static void Invoke(Plugin plugin, string method) =>
        typeof(Plugin).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(plugin, null);
    private static ConfigEntry<T> Get<T>(ConfigFile config, string section, string key) => (ConfigEntry<T>)config[new ConfigDefinition(section, key)];
    private static void Set<T>(ConfigFile config, string section, string key, T value) => Get<T>(config, section, key).Value = value;
    private static ConfigEntry<bool> Toggle(ConfigFile config, int preset) => Get<bool>(config, preset < 2 ? "Emerald Archipelagos" : "Aestrin", Names[preset] + TwilightKey);
    private static void Select(ConfigFile config, int preset)
    {
        if (preset < 2) Set(config, "Emerald Archipelagos", Names[preset], true);
        else Set(config, "Aestrin", Names[preset], true);
    }
    private static void Ordered(ConfigFile config, string section, params string[] keys)
    {
        int previous = int.MaxValue;
        foreach (string key in keys)
        {
            int order = ((SettingOrder)config[new ConfigDefinition(section, key)].Description.Tags[0]).Order;
            Assert(order < previous, "UI order of " + key);
            previous = order;
        }
    }
    private static RegionalContribution Source(int preset, float weight = 1f)
    {
        var set = new WeatherSet { dayPalette = Day, dawnPalette = Dawn };
        var full = new RegionalContribution();
        if (preset < 2) full.Emerald = PaletteContribution.From(set);
        else if (preset == 2) full.Aestrin = PaletteContribution.From(set);
        else full.Chronos = PaletteContribution.From(set);
        return RegionalContribution.Blend(default(RegionalContribution), full, weight);
    }
    private static OceanColorPalette ExpectedPreset(int preset, string section)
    {
        string current = "";
        var values = new Dictionary<string, string>();
        foreach (string raw in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Presets", Files[preset] + ".OceanColorConfigurator.cfg")))
        {
            string line = raw.Trim();
            if (line.StartsWith("[")) current = line.Trim('[', ']');
            int equals = line.IndexOf('=');
            if (current == section && equals > 0 && !line.StartsWith("#")) values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
        }
        return Palette(values["Water Color"], values["Surface Color"], values["Scattering Color"], 1f);
    }
    private static OceanColorPalette Palette(string water, string surface, string scattering, float alpha) => new OceanColorPalette
    {
        waterColor = Hex(water, alpha), surfaceColor = Hex(surface, alpha), scatteringColor = Hex(scattering, alpha),
        fog = Hex("#8899AA", alpha), temperature = 4f, tint = 3f, oceanSpecular = .7f
    };
    private static Color Hex(string hex, float alpha) => new Color(Convert.ToInt32(hex.Substring(1, 2), 16) / 255f,
        Convert.ToInt32(hex.Substring(3, 2), 16) / 255f, Convert.ToInt32(hex.Substring(5, 2), 16) / 255f, alpha);
    private static OceanColorPalette Mix(OceanColorPalette day, OceanColorPalette dawn, OceanColorPalette night, float d, float t, float n) => new OceanColorPalette
    {
        waterColor = day.waterColor * d + dawn.waterColor * t + night.waterColor * n,
        surfaceColor = day.surfaceColor * d + dawn.surfaceColor * t + night.surfaceColor * n,
        scatteringColor = day.scatteringColor * d + dawn.scatteringColor * t + night.scatteringColor * n,
        fog = day.fog * d + dawn.fog * t + night.fog * n,
        temperature = day.temperature * d + dawn.temperature * t + night.temperature * n,
        tint = day.tint * d + dawn.tint * t + night.tint * n,
        oceanSpecular = day.oceanSpecular * d + dawn.oceanSpecular * t + night.oceanSpecular * n
    };
    private static OceanColorPalette Lerp(OceanColorPalette one, OceanColorPalette two, float weight) => Mix(one, two, default(OceanColorPalette), 1f - weight, weight, 0f);
    private static void EqualColors(OceanColorPalette actual, OceanColorPalette expected)
    {
        EqualColor(actual.waterColor, expected.waterColor);
        EqualColor(actual.surfaceColor, expected.surfaceColor);
        EqualColor(actual.scatteringColor, expected.scatteringColor);
    }
    private static void EqualColor(Color actual, Color expected) { Near(actual.r, expected.r); Near(actual.g, expected.g); Near(actual.b, expected.b); }
    private static void EqualAlpha(OceanColorPalette actual, OceanColorPalette expected)
    {
        Near(actual.waterColor.a, expected.waterColor.a); Near(actual.surfaceColor.a, expected.surfaceColor.a); Near(actual.scatteringColor.a, expected.scatteringColor.a);
    }
    private static void EqualOtherFields(OceanColorPalette actual, OceanColorPalette expected)
    {
        EqualColor(actual.fog, expected.fog); Near(actual.fog.a, expected.fog.a);
        Near(actual.temperature, expected.temperature); Near(actual.tint, expected.tint); Near(actual.oceanSpecular, expected.oceanSpecular);
        Assert(actual.skyMaterial == expected.skyMaterial, "sky material");
    }
    private static void EqualPalette(OceanColorPalette actual, OceanColorPalette expected) { EqualColors(actual, expected); EqualAlpha(actual, expected); EqualOtherFields(actual, expected); }
    private static void Near(float actual, float expected) { Assert(Math.Abs(actual - expected) < .00001f, $"expected {expected}, got {actual}"); }
    private static void Assert(bool condition, string description) { if (!condition) throw new Exception(description); }
    private static Material MaterialFixture()
    {
        var material = new Material();
        material.SetColor(Shader.PropertyToID("_SubSurfaceShallowCol"), Hex("#819EB7", .7f));
        material.SetColor(Shader.PropertyToID("_SkyTowardsSun"), Hex("#93ADBE", .7f));
        material.SetColor(Shader.PropertyToID("_SkyAwayFromSun"), Hex("#678B9E", .7f));
        material.SetFloat(Shader.PropertyToID("_SubSurfaceBase"), .8f);
        material.SetFloat(Shader.PropertyToID("_SubSurfaceSun"), .9f);
        return material;
    }
}
