# Underdogs Enhanced

[中文版](README_CN.md)

A [MelonLoader](https://melonwiki.xyz/) mod for **Gunner HEAT PC** that adds stabilizers and laser rangefinders to several vehicles.

Underdogs just a joke, don't be serious.

Based on work by [ATLAS](https://github.com/thebeninator/Stabilized-BMP-1)

## Conflict with [Stabilized BMP-1](https://github.com/thebeninator/Stabilized-BMP-1)

## Features

| Vehicle | Enhancement | Default |
|---|---|---|
| BMP-1 / BMP-1P | Vector stabilizer (gun + turret) | On |
| BMP-1 / BMP-1P | Laser rangefinder (display only, no auto-ranging) | On |
| BMP-1 / BMP-1G | 9M14TV Malyutka-TV TV-guided missile with FLIR camera | On |
| BMP-1P | Konkurs ATGM stabilizer | Off |
| Marder 1A2 / A1- / A1+ | Vector stabilizer | On |
| Marder 1A2 / A1+ | MILAN launcher stabilizer | On |
| Marder 1A2 / A1- / A1+ | Laser rangefinder (4000m) + parallax fix | On |
| Marder 1A2 / A1+ | Spike-style hybrid TV/FnF missile (WIP) | Off |
| BRDM-2 | Vector stabilizer | On |
| BRDM-2 | Increased turret traverse speed | On |
| BRDM-2 | Gunner sight zoom levels | On |
| BRDM-2 | Laser rangefinder (display only, no auto-ranging) | On |
| BTR-70 | Vector stabilizer | On |
| BTR-70 | Increased turret traverse speed | On |
| BTR-70 | Gunner sight zoom levels | On |
| BTR-70 | Laser rangefinder (display only, no auto-ranging) | On |
| Leopard 1A3 / 1A3A1 / 1A3A2 / 1A3A3 | Convert to Leopard 1A5 (EMES-18 sight + new turret) | On |
| Leopard 1A3 series | EMES-18 FCS only (without model change) | Off |
| Leopard A1A1 / A1A2 / A1A3 / A1A4 | Convert to Leopard 1A5 (EMES-18 sight + new turret) | On |
| Leopard A1 series | EMES-18 FCS only (without model change) | Off |
| Leopard 1A5 / converted variants | AP round selection: Default / DM33 (420mm) / DM63 (447mm) | Default |
| PT-76B | Laser rangefinder with auto-ranging | On |
| PT-76B | Gunner sight zoom levels | On |
| T-64 series (NSVT) | NSVT cupola stabilizer | On |
| T-64 series (NSVT) | Gunner sight zoom levels | On |
| T-54A | Laser rangefinder with auto-ranging | On |
| T-34-85M | Stabilizer (slightly buggy) | Off |
| T-34-85M | Gunner sight zoom levels | On |
| T-34-85M | Laser rangefinder with auto-ranging | On |

### Debug Features

| Feature | Description | Default |
|---|---|---|
| Vehicle Repair | Press **J** key to fully repair current vehicle (engine, transmission, tracks, turret, crew, etc.) | On |

## Installation

One key install Available on [GHPC Mod Manager](https://GHPC.DMR.gg/?lang=en)

1. Install [MelonLoader](https://melonwiki.xyz/#/?id=requirements) for GHPC
2. Drop `UnderdogsEnhanced.dll` into the `Mods/` folder

## Configuration

After first launch, edit `UserData/MelonPreferences.cfg`:

```
[Underdogs-Enhanced]
BMP-1 Stabilizer = true
BMP-1 Rangefinder = true
BMP-1 9M14TV Malyutka-TV = true
BMP-1 MCLOS Ready Missiles = -1
BMP-1 MCLOS FLIR High Resolution = false
BMP-1 MCLOS FLIR Remove Scanline = false
BMP-1P Konkurs Stab = false
Marder Stabilizer = true
Marder MILAN Stabilizer = true
Marder Rangefinder = true
Marder Spike WIP = false
Marder Spike Ready Missiles = -1
BRDM-2 Stabilizer = true
BRDM-2 Turret Speed = true
BRDM-2 Optics = true
BRDM-2 Rangefinder = true
BTR-70 Stabilizer = true
BTR-70 Turret Speed = true
BTR-70 Optics = true
BTR-70 Rangefinder = true
Leopard 1A3 Convert to 1A5 = true
Leopard 1A3 EMES18 FCS Only = false
Leopard 1A3 Ammo AP Rounds = Default
Leopard 1A3A1 Convert to 1A5 = true
Leopard 1A3A1 EMES18 FCS Only = false
Leopard 1A3A1 Ammo AP Rounds = Default
Leopard 1A3A2 Convert to 1A5 = true
Leopard 1A3A2 EMES18 FCS Only = false
Leopard 1A3A2 Ammo AP Rounds = Default
Leopard 1A3A3 Convert to 1A5 = true
Leopard 1A3A3 EMES18 FCS Only = false
Leopard 1A3A3 Ammo AP Rounds = Default
Leopard A1A1 Convert to 1A5 = true
Leopard A1A1 EMES18 FCS Only = false
Leopard A1A1 Ammo AP Rounds = Default
Leopard A1A2 Convert to 1A5 = true
Leopard A1A2 EMES18 FCS Only = false
Leopard A1A2 Ammo AP Rounds = Default
Leopard A1A3 Convert to 1A5 = true
Leopard A1A3 EMES18 FCS Only = false
Leopard A1A3 Ammo AP Rounds = Default
Leopard A1A4 Convert to 1A5 = true
Leopard A1A4 EMES18 FCS Only = false
Leopard A1A4 Ammo AP Rounds = Default
Leopard 1A5 Ammo AP Rounds = Default
PT-76B Rangefinder = true
PT-76B Optics = true
T-64 NSVT Stabilizer = true
T-64 NSVT Optics = true
T-54A Rangefinder = true
T-34-85M Stabilizer = false
T-34-85M Optics = true
T-34-85M Rangefinder = true
Repair Function = true
```

## Credit

- This project bundles the DSEG font family by Keshikan, licensed under the SIL Open Font License 1.1
    - Source: https://github.com/keshikan/DSEG

- 3D model "[EMES18]" by [KojfDiscord] is licensed under CC BY 4.0. Source: [Sketchfab](https://skfb.ly/psUAy). Modified by RoyZ.
