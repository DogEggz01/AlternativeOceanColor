// This fixture runs the actual Plugin.cs and real BepInEx ConfigFile/Unity Color
// math. Native Unity lifecycle/rendering, game types, and Harmony dispatch are
// boundaries only; this is not an in-game or Unity Play Mode test.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace BepInEx
{
    public class BaseUnityPlugin
    {
        public static ConfigFile NextConfig;
        public ConfigFile Config { get; } = NextConfig;
        public bool isActiveAndEnabled = true;
        public FixtureLogger Logger { get; } = new FixtureLogger();
    }
    public sealed class FixtureLogger { public void LogInfo(object message) { } }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
    }
    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void PatchAll(Assembly assembly) { }
        public void UnpatchSelf() { }
    }
}

namespace UnityEngine
{
    public static class Shader
    {
        private static readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        public static int PropertyToID(string name)
        {
            if (!ids.TryGetValue(name, out int id)) ids[name] = id = ids.Count + 1;
            return id;
        }
    }
    public sealed class Material
    {
        private readonly Dictionary<int, Color> colors = new Dictionary<int, Color>();
        private readonly Dictionary<int, float> numbers = new Dictionary<int, float>();
        public bool HasProperty(int id) => colors.ContainsKey(id) || numbers.ContainsKey(id);
        public Color GetColor(int id) => colors[id];
        public float GetFloat(int id) => numbers[id];
        public void SetColor(int id, Color value) => colors[id] = value;
        public void SetFloat(int id, float value) => numbers[id] = value;
    }
}

public struct OceanColorPalette
{
    public Color waterColor, surfaceColor, scatteringColor, fog;
    public float temperature, tint, oceanSpecular;
    public Material skyMaterial;
}
public sealed class WeatherSet
{
    public string name;
    public OceanColorPalette dayPalette, dawnPalette;
    public static void BlendSets() { }
    public static void CopyFrom() { }
}
public sealed class Weather { }
public sealed class Region { public string name; public WeatherSet clearWeather; }
public sealed class RegionBlender { public Region initialRegion; }
public sealed class Sun
{
    public static Sun sun;
    public float GetDawnLerp() => 0f;
    public float GetNightLerp() => 0f;
}
public sealed class OceanColorBlender { public static void ApplyPalette() { } }
