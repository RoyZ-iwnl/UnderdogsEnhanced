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
| BMP-1P | Konkurs 反坦克导弹稳定器 | 关闭 |
| Marder 1A2 / A1- / A1+ | 稳定器 | 开启 |
| Marder 1A2 / A1- / A1+ | 激光测距仪（4000m）+ 视差修正 | 开启 |
| BRDM-2 | 稳定器 | 开启 |
| BRDM-2 | 炮塔旋转速度提升 | 开启 |
| BRDM-2 | 炮手瞄准镜变焦档位 | 开启 |
| BRDM-2 | 激光测距仪（仅显示，无自动测距） | 开启 |
| BTR-70 | 稳定器 | 开启 |
| BTR-70 | 炮塔旋转速度提升 | 开启 |
| BTR-70 | 炮手瞄准镜变焦档位 | 开启 |
| BTR-70 | 激光测距仪（仅显示，无自动测距） | 开启 |
| Leopard 1A3 / A3A1 / A3A2 / A3A3 / A1A1 / A1A2 / A1A3 / A1A4 | 激光测距仪替换光学测距仪（4000m） | 开启 |
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
BMP-1P Konkurs Stab = false
Marder Stabilizer = true
Marder Rangefinder = true
BRDM-2 Stabilizer = true
BRDM-2 Turret Speed = true
BRDM-2 Optics = true
BRDM-2 Rangefinder = true
BTR-70 Stabilizer = true
BTR-70 Turret Speed = true
BTR-70 Optics = true
BTR-70 Rangefinder = true
Leopard 1 Laser = true
PT-76B Rangefinder = true
PT-76B Optics = true
T-64 NSVT Stabilizer = true
T-64 NSVT Optics = true
T-54A Rangefinder = true
T-34-85M Stabilizer = false
T-34-85M Optics = true
T-34-85M Rangefinder = true
```
