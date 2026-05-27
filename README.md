# FanControl.AcerPredatorPH315

A [FanControl](https://github.com/Rem0o/FanControl.Releases) plugin that adds
native fan control for the **Acer Predator Helios 300 (PH315-53)** by talking
to the Embedded Controller through [PawnIO](https://pawnio.eu/).

The register map was transcribed from Arghadip Deb's NBFC config
("Acer Predator PH315-53.xml") and cross-checked against
[LokeshP13/only-laptop-fans](https://github.com/LokeshP13/only-laptop-fans).

> ⚠️ Writing the wrong byte to the wrong EC register can brick fans or melt
> your laptop. Use at your own risk. Before attaching a curve, manually
> validate `Set(0)` and `Set(100)` on a cool system.

## What it exposes

| Sensor                | Kind                    | EC reg (dec / hex)           |
|-----------------------|-------------------------|------------------------------|
| CPU fan speed         | `IPluginSensor`         | 19 / 0x13 (word, raw 0–6122) |
| GPU fan speed         | `IPluginSensor`         | 21 / 0x15 (word, raw 0–6122) |
| CPU fan control 0–100 | `IPluginControlSensor2` | 55 / 0x37                    |
| GPU fan control 0–100 | `IPluginControlSensor2` | 58 / 0x3A                    |

The "fan speed" reading is the raw EC word, which NBFC calibrates against
`MaxSpeedValueRead = 6122`. It is **not** a true tachometer reading — treat it
as a 0..6122 scalar that tracks the real fan well enough to drive a graph
(idle ≈ 0, full tilt ≈ 5000–6000). FanControl labels it as RPM in the UI
regardless.

`PairedFanSensorId` on the two control sensors lets FanControl auto-bind each
slider to the matching speed readout.

## Init / reset writes

`Initialize()` flips the EC into manual-fan mode and silences CoolBoost.
`Close()` restores the BIOS-managed values.

| Purpose         | Reg | On-init value | Reset value |
|-----------------|-----|---------------|-------------|
| CPU manual mode | 34  | 12            | 4           |
| GPU manual mode | 33  | 48            | 16          |
| CoolBoost off   | 16  | 0             | 1           |

## Install

1. **Install FanControl V238 or newer.** This version ships PawnIO and
   registers its kernel driver. Verify `C:\Program Files\PawnIO\PawnIOLib.dll`
   exists.
2. In FanControl, open **Settings → Plugins** and install
   `FanControl.AcerPredatorPH315.dll` from there.
3. Restart FanControl. Two new fan speed sensors and two control sliders
   should appear under **Acer Predator PH315-53**.

If the plugin fails to initialize, FanControl swallows the exception. See
[Diagnostics](#diagnostics) for where to look.

## Build

1. Copy `FanControl.Plugins.dll` from your FanControl install dir into
   `./lib/`.
2. Download `LpcACPIEC.bin` from
   [namazso/PawnIO.Modules releases](https://github.com/namazso/PawnIO.Modules/releases)
   and drop it into `./modules/`.
3. `dotnet build FanControl.AcerPredatorPH315.sln -c Release`

The project multi-targets `netstandard2.0` and `net10.0-windows`:

- `bin/Release/netstandard2.0/FanControl.AcerPredatorPH315.dll` — loads into
  either the .NET 8 or `_net_10_0` FanControl installer.
- `bin/Release/net10.0-windows/FanControl.AcerPredatorPH315.dll` — only the
  `_net_10_0` installer.

If you don't care about portability, the `net10.0-windows` DLL matches a
current FanControl install.

The solution contains the multi-targeted plugin project and a separate
`AcerProbe` console utility. There is no wrapper build project; a normal
solution build produces both plugin target-framework outputs directly.

### Probe utility

`Probe/FanControl.AcerPredatorPH315.Probe.csproj` builds `AcerProbe`, a
standalone administrator console tool for exploring the Acer gaming WMI
surface without loading the FanControl plugin. It does not use PawnIO or touch
the EC directly.

Example:

```
dotnet run --project Probe/FanControl.AcerPredatorPH315.Probe.csproj -- enum
```

## Known issues

- **Fan speed reads occasionally fail.** The ACPI driver and this plugin both
  arbitrate for the EC; when ACPI wins, the read times out. The sensor holds
  its last good value instead of dropping to zero so the graph stays smooth.
- **Fans don't return to BIOS auto on a hard crash.** `Close()` writes the
  reset values, but a FanControl crash skips that. The EC's own thermal trip
  still protects the CPU; if you see a stuck low duty after a crash, reboot.

## Diagnostics

FanControl silently catches plugin `Initialize()` exceptions and surfaces them
only as *"Acer Predator PH315-53 could not initialize or has no sensors"*. The
plugin therefore writes its own log next to the DLL:

```
<plugin folder>\FanControl.AcerPredatorPH315.log
```

Check that file first whenever the plugin behaves oddly.

## Layout

```
FanControl.AcerPredatorPH315.sln      # plugin + probe solution
FanControl.AcerPredatorPH315.csproj   # multi-target plugin project
AcerPredatorPlugin.cs                 # IPlugin2 entrypoint
Diagnostics/PluginLog.cs              # file logger (FanControl swallows init exceptions)
Ec/AcerEc.cs                          # EC handshake over 0x62/0x66
Ec/PredatorRegisters.cs               # register map (NBFC xml + only-laptop-fans)
PawnIO/PawnIONative.cs                # P/Invoke -> PawnIOLib.dll
PawnIO/PawnIOLibLoader.cs             # locates PawnIOLib.dll outside PATH and LoadLibrary's it
PawnIO/PawnIOSession.cs               # RAII handle + module loader
Sensors/PredatorFanSpeedSensor.cs     # IPluginSensor (read)
Sensors/PredatorFanControlSensor.cs   # IPluginControlSensor2 (write, paired)
Probe/                                # standalone WMI probe utility
modules/LpcACPIEC.bin                 # embedded PawnIO module (you supply)
lib/FanControl.Plugins.dll            # plugin SDK (you supply)
```

## Author

Built by **Samuel Johansson** ([@phaax](https://github.com/phaax)) — Phaax Games.

## Credits

- [Rem0o/FanControl.Releases](https://github.com/Rem0o/FanControl.Releases) —
  the host application and plugin SDK.
- [namazso/PawnIO](https://github.com/namazso/PawnIO) and
  [namazso/PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) — the
  signed kernel driver and `LpcACPIEC` module that make EC access possible
  without a custom driver.
- Arghadip Deb's NBFC config for the PH315-53 register map.
- [LokeshP13/only-laptop-fans](https://github.com/LokeshP13/only-laptop-fans)
  — independent Python implementation used to cross-check register semantics.
