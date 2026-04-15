# Underdogs Enhanced

一个用于 **Gunner HEAT PC** 的 [MelonLoader](https://melonwiki.xyz/) Mod，为多款历史上装备了稳定器和激光测距仪功能。

Underdogs 只是个玩笑，别当真。

基于 [ATLAS](https://github.com/thebeninator/Stabilized-BMP-1) 的工作成果

## 与 [Stabilized BMP-1](https://github.com/thebeninator/Stabilized-BMP-1) 的冲突

## 功能

| 载具 | 增强内容 | 默认状态 |
|---|---|---|
| BMP-1 / BMP-1P | 稳定器（炮管 + 炮塔） | 开启 |
| BMP-1 / BMP-1P | 激光测距仪（仅显示，无自动测距） | 开启 |
| BMP-1 / BMP-1G | 9M14TV Malyutka-TV 电视制导导弹（含热成像摄像机） | 开启 |
| BMP-1P | Konkurs 反坦克导弹稳定器 | 关闭 |
| Marder 1A2 / A1- / A1- (no ATGM) / A1+ | 稳定器 | 开启 |
| Marder 1A2 / A1+ | MILAN 导弹发射器稳定器 | 开启 |
| Marder 1A2 / A1- / A1- (no ATGM) / A1+ | FCS增强（6000m激光测距、superlead、视差修正） | 开启 |
| Marder 1A2 / A1- / A1- (no ATGM) / A1+ | 主炮口径可选（25mm KBA 或 35mm Revolver） | 25mm |
| Marder 1A2 / A1- / A1- (no ATGM) / A1+（25mm） | AP弹链：PMB090瑞士钢针（92mm穿深）或 M791 | PMB090 |
| Marder 1A2 / A1- / A1- (no ATGM) / A1+（25mm） | 双射速 175/600 RPM（[B]键切换） | 开启 |
| Marder 1A2 / A1- / A1- (no ATGM) / A1+（35mm） | AP弹链：DM33/DM23/DM13 可选 | DM33 |
| Marder 1A2 / A1- / A1- (no ATGM) / A1+（35mm） | 双射速 200/1000 RPM（[B]键切换） | 开启 |
| Marder A1+ / A1- / A1- (no ATGM) | 热成像改装（Marder 1A2 FLIR瞄具） | 开启 |
| Marder 1A2 / A1- / A1- (no ATGM) / A1+ | 炮塔转速提升 | 开启 |
| Marder 1A2 / A1- / A1- (no ATGM) / A1+ | 动力升级（发动机、悬挂、转向） | 开启 |
| Marder 1A2 / A1+ / A1- | Spike WIP：TV/FnF混合制导 + 攻顶弹道 | 关闭 |
| BRDM-2 | 稳定器 | 开启 |
| BRDM-2 | 炮塔旋转速度提升 | 开启 |
| BRDM-2 | 炮手瞄准镜变焦档位 | 开启 |
| BRDM-2 | 激光测距仪（仅显示，无自动测距） | 开启 |
| BTR-70 | 稳定器 | 开启 |
| BTR-70 | 炮塔旋转速度提升 | 开启 |
| BTR-70 | 炮手瞄准镜变焦档位 | 开启 |
| BTR-70 | 激光测距仪（仅显示，无自动测距） | 开启 |
| Leopard 1 系列（所有型号） | EMES18 火控系统（激光测距仪 + 3-12x FLIR 热成像 + point-n-shoot） | 开启 |
| Leopard 1 系列（所有型号） | DM63 APFSDS-T 穿甲弹替换原版 APFSDS | 开启 |
| PT-76B | 激光测距仪（含自动测距） | 开启 |
| PT-76B | 炮手瞄准镜变焦档位 | 开启 |
| T-64 系列（NSVT） | NSVT 高射机枪稳定器 | 开启 |
| T-64 系列（NSVT） | 炮手瞄准镜变焦档位 | 开启 |
| T-54A | 激光测距仪（含自动测距） | 开启 |
| T-34-85M | 稳定器（略有 bug） | 关闭 |
| T-34-85M | 炮手瞄准镜变焦档位 | 开启 |
| T-34-85M | 激光测距仪（含自动测距） | 开启 |

## 安装

在 [GHPC Mod Manager](https://GHPC.DMR.gg/) 上一键安装

1. 为 GHPC 安装 [MelonLoader](https://melonwiki.xyz/#/?id=requirements)
2. 将 `UnderdogsEnhanced.dll` 放入 `Mods/` 文件夹

## 配置

首次启动后，编辑 `UserData/MelonPreferences.cfg`：

```
[Underdogs-Enhanced]
BMP-1 Stabilizer = true
BMP-1 Rangefinder = true
BMP-1 9M14TV Malyutka-TV = true
BMP-1 MCLOS Ready Missiles = -1
BMP-1 MCLOS FLIR High Resolution = false
BMP-1 MCLOS FLIR Remove Scanline = false
BMP-1P Konkurs Stab = false
Marder Mod Master Switch = true
Marder Cannon Caliber = 25mm
Marder 25mm AP Belt = PMB090
Marder 25mm AP Count = 254
Marder 25mm HE Count = 254
Marder 35mm AP Belt = DM33
Marder 35mm AP Count = 254
Marder 35mm HE Count = 254
Marder Stabilizer = true
Marder MILAN Stabilizer = true
Marder A1 Thermal Retrofit = true
Marder BetterFCS = true
Marder Turret Speedup = true
Marder Engine Upgrade = true
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
Leopard A1A4 EMES18 Sight = true
Leopard A1A4 DM63 Ammo = true
Leopard 1A3 EMES18 Sight = true
Leopard 1A3 DM63 Ammo = true
Leopard 1A3A2 EMES18 Sight = true
Leopard 1A3A2 DM63 Ammo = true
Leopard A1A1 EMES18 Sight = true
Leopard A1A1 DM63 Ammo = true
Leopard A1A3 EMES18 Sight = true
Leopard A1A3 DM63 Ammo = true
Leopard 1A3A1 EMES18 Sight = true
Leopard 1A3A1 DM63 Ammo = true
Leopard 1A3A3 EMES18 Sight = true
Leopard 1A3A3 DM63 Ammo = true
Leopard A1A2 EMES18 Sight = true
Leopard A1A2 DM63 Ammo = true
PT-76B Rangefinder = true
PT-76B Optics = true
T-64 NSVT Stabilizer = true
T-64 NSVT Optics = true
T-54A Rangefinder = true
T-34-85M Stabilizer = false
T-34-85M Optics = true
T-34-85M Rangefinder = true
```

## Credit

- This project bundles the DSEG font family by Keshikan, licensed under the SIL Open Font License 1.1
    - Source: https://github.com/keshikan/DSEG
