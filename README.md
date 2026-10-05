# AlternativeOceanColor

**Version 1.2.1** — four regional presets with independently saved clear dawn/dusk controls.

## Dawn and dusk controls

Each preset has its own **Use Custom Dusk/Dawn Colors** toggle in BepInEx Configuration Manager. All four default to **On**.

- **On:** use that preset's custom water, surface, and scattering colors at clear dawn/dusk.
- **Off:** use Sailwind's vanilla clear dawn/dusk endpoint. Clear daytime keeps the preset's custom colors.

Sailwind uses one endpoint for dawn and dusk. The normal smooth day, twilight, night, region, and weather blends remain in use: during a transition, custom daytime colors gradually blend toward the selected twilight endpoint. Cloudy twilight and night retain their vanilla contributions.

| Preset | Dawn/dusk water | Surface | Scattering |
| --- | --- | --- | --- |
| Caribbean Turquoise | `#287E88` | `#6B9799` | `#3F9195` |
| Emerald Sea | `#286F70` | `#648A82` | `#347B73` |
| Winter Aestrin | `#385F83` | `#71869E` | `#4F7E9F` |
| Open Chronos Ocean | `#274A76` | `#536A89` | `#365F8D` |

Winter Aestrin gains a muted slate-blue twilight palette in 1.2.1. The other three twilight palettes and all daytime palettes keep their existing colors.

Under **Emerald Archipelagos**, each preset's twilight toggle follows its enable checkbox. Under **Aestrin**, each group is ordered **enable**, **Apply to**, then **Use Custom Dusk/Dawn Colors**. Winter and Open Chronos twilight choices follow their preset to Aestrin, Chronos, or Both. Changing a twilight toggle does not change preset selection or resolve region overlaps. Palette changes appear on the next normal weather update.

## Install / upgrade

Requires BepInEx. Close Sailwind, then extract the release ZIP's **AlternativeOceanColor** folder into `BepInEx/plugins`, replacing the previous version. Remove any old **AlternativeEmeraldSea.dll** to avoid loading both versions.

Keep `BepInEx/config/DogEggz.AlternativeEmeraldSea.cfg`. The GUID and config path are retained, along with saved preset enable choices, region assignments, and the master switch. The new twilight toggles default to On when missing from an older config; saved Off choices survive restart.

If Ocean Color Configurator is installed, turn its **Enable Color Overrides** setting Off when using this mod. No configurator dependency is required. Included `Presets` files are reference/manual-restoration snapshots, not live configuration inputs. The release and build script do not change your installed config or game saves.

## Emerald Archipelagos

- Emerald Sea: Lower Color Temperature (less yellow). Darker, more Emerald like sea. This one will be closer to Vanilla.
<img width="1536" height="864" alt="image" src="https://github.com/user-attachments/assets/c7afcb29-2953-4b4a-ae2f-fff0b0c43101" />

- Caribbean Turquoise: Color Temperature lower more. Turquoise Sea. Custom color.
<img width="1536" height="864" alt="image" src="https://github.com/user-attachments/assets/fff5d04f-9dc6-4dff-82ca-668b9aba416a" />

## Aestrin and Chronos

- Winter Aestrin: Cold color temperature. Crystal blue.
<img width="3840" height="2160" alt="209284~1" src="https://github.com/user-attachments/assets/516e5bae-b504-4a93-bb1e-03a901b8c9ad" />

- Open Chronos Ocean: Neutral color temperature. Darker navy blue.
<img width="3840" height="2160" alt="2026D5~1" src="https://github.com/user-attachments/assets/c4f12f79-2767-4759-bb04-1084b072271b" />

- You can choose in configurator which color set apply to which region.(For Aestrin color only)

## Build and validation

Run `./build.ps1` with .NET SDK 10 installed. If the source is outside `Sailwind/ModSource`, supply `-GameDir 'path/to/Sailwind'`. The script compiles the .NET Standard 2.0 DLL against the game's actual BepInEx, Harmony, and Unity assemblies, runs the managed regression fixture, and creates `dist/AlternativeOceanColor-1.2.1.zip`. It does not install the plugin. `-SkipTests` only builds/packages and should not be used for release validation.

The managed fixture compiles the actual `Plugin.cs` with real BepInEx config save/load and Unity color math. It replaces native Unity lifecycle/rendering, game types, and Harmony dispatch boundaries. Checks cover all four toggle endpoints, unchanged daytime colors/material settings, mixed time/weather/region contributions, all 16 independent toggle combinations, Aestrin/Chronos/Both routing, config persistence, and 1.2.0 upgrades. Results are written under `Tests/results`.

These tests do not exercise the actual game Harmony hooks or rendered Crest ocean. An in-game visual check is still required to assess the new Winter palette's appearance.
