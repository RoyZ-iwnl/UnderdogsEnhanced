using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using MelonLoader;
using UnderdogsEnhanced;
using GHPC.Vehicle;
using GHPC;
using GHPC.State;
using System.Reflection;
using GHPC.Weapons;
using GHPC.Weaponry;
using GHPC.Camera;
using TMPro;
using Reticle;
using HarmonyLib;

[assembly: MelonInfo(typeof(UnderdogsEnhancedMod), "Underdogs Enhanced", "1.5.0", "RoyZ;Based on ATLAS work")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace UnderdogsEnhanced
{
    public class UnderdogsEnhancedMod : MelonMod
    {
        public static MelonPreferences_Category cfg;

        private string currentGameplaySceneName = string.Empty;

        private static HashSet<int> _modifiedVehicleIds = new HashSet<int>();

        public override void OnInitializeMelon() {
            UEResourceController.Initialize();
            cfg = MelonPreferences.CreateCategory("Underdogs-Enhanced");

            // 各载具模块配置
            Bmp1Main.Config(cfg);
            Brdm2Main.Config(cfg);
            MarderMain.Config(cfg);
            LeopardMain.Config(cfg);
            Btr70Main.Config(cfg);
            Pt76Main.Config(cfg);
            T64Main.Config(cfg);
            T54aMain.Config(cfg);
            T34Main.Config(cfg);
            UERepairMain.Config(cfg);
         }

        private IEnumerator EnsureAmmoConversionsOnGameReady(GameState _)
        {
            yield return UEAmmoConversionCoordinator.EnsureAmmoConversionsOnGameReady(_, currentGameplaySceneName);
        }

        public override void OnUpdate()
        {
#if DEBUG
            UnderdogsDebug.HandleDebugKeys();
#endif
            if (UERepairMain.repair_enabled.Value)
                UERepairMain.HandleRepairHotkey();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
#if DEBUG
            MelonLogger.Warning("|||||||||||||||||UnderdogsEnhanced DEBUG VERSION|||||||||||||||||");
#endif
            currentGameplaySceneName = sceneName;
            UEAmmoConversionCoordinator.ResetSceneState();

            UEResourceController.UnloadDynamicAssets();

            if (UECommonUtil.IsMenuScene(sceneName))
            {
                UEResourceController.LoadStaticAssets();
                EMES18Optic.LoadStaticAssets();
            }

            if (UECommonUtil.IsMenuScene(sceneName)) return;

            UEResourceController.PrewarmSceneSpecificVanillaDonors(sceneName);

            // 弹药转换先注册（确保在外观改装前执行）
            if (Bmp1Main.ShouldScheduleAmmoConversion() || (MarderMain.marder_enabled.Value && MarderMain.marder_spike.Value) || LeopardMain.ShouldScheduleAmmoConversion())
                StateController.RunOrDefer(GameState.GameReady, new GameStateEventHandler(EnsureAmmoConversionsOnGameReady), GameStatePriority.Medium);

            LeopardMain.OnSceneLoaded();
        }

        public override async void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (UECommonUtil.IsMenuScene(sceneName)) return;

            UEResourceController.LoadDynamicAssets();
            UEResourceController.PrewarmCommonVanillaDonors();

            float _sceneStartTime = Time.realtimeSinceStartup;
#if DEBUG
            UnderdogsDebug.LogTiming($"[UE] >>> OnSceneWasInitialized | 场景={sceneName} | 时间={System.DateTime.Now:HH:mm:ss.fff}");

            // 初始化调试UI（运行时开关，不依赖编译配置）
            FCSDebugUI.Init();
#endif

            Vehicle[] all_vehicles = new Vehicle[0];
            int _waitCount = 0;
            const int _maxWaitAttempts = 60; // 60 * 500ms = 30s max
            do {
                await Task.Delay(500);
                _waitCount++;
                all_vehicles = Object.FindObjectsOfType<Vehicle>();
#if DEBUG
                if (UnderdogsDebug.DEBUG_TIMING && _waitCount % 4 == 0)
                    MelonLogger.Msg($"[UE] 等待载具中... {_waitCount * 500}ms | 当前={all_vehicles?.Length ?? 0}个");
#endif
            } while (_waitCount < _maxWaitAttempts && (all_vehicles == null || all_vehicles.Length == 0 || !all_vehicles.Any(v => v != null && (
                v.FriendlyName == "BMP-1" || v.FriendlyName == "BMP-1P" ||
                v.FriendlyName == "BRDM-2" || v.FriendlyName == "BTR-70" ||
                v.FriendlyName == "PT-76B" || v.FriendlyName.StartsWith("Marder") ||
                v.FriendlyName.StartsWith("Leopard") || v.FriendlyName.StartsWith("T-64")))));

            if (all_vehicles == null || all_vehicles.Length == 0)
            {
#if DEBUG
                if (UnderdogsDebug.DEBUG_TIMING)
                    MelonLogger.Warning("[UE] 30秒内未检测到任何 Vehicle，跳过本次场景改装");
#endif
                return;
            }

#if DEBUG
            UnderdogsDebug.LogTiming($"[UE] 等待 {_waitCount * 500}ms 后发现 {all_vehicles.Length} 个载具 | 距场景加载 {Time.realtimeSinceStartup - _sceneStartTime:F3}s");
#endif




#if DEBUG
            UnderdogsDebug.DumpAllVehiclesDetailedInfo(all_vehicles);
            UnderdogsDebug.DumpAllVehiclesChildren(all_vehicles);
            UnderdogsDebug.DumpAllArmorData();
#endif

            var currentIds = new HashSet<int>(all_vehicles.Where(v => v != null).Select(v => v.GetInstanceID()));
            _modifiedVehicleIds.IntersectWith(currentIds);

            // 试车场执行2轮扫描，第2轮捕获GMPC延迟生成的载具
            int _uePassCount = (sceneName == "TR01_showcase") ? 2 : 1;

            for (int _uePass = 1; _uePass <= _uePassCount; _uePass++)
            {
                if (_uePass > 1)
                {
#if DEBUG
                    if (UnderdogsDebug.DEBUG_TIMING)
                        MelonLogger.Msg($"[UE] 试车场第{_uePass}轮: 等待3秒后重新扫描载具...");
#endif

                    await Task.Delay(3000);
                    all_vehicles = Object.FindObjectsOfType<Vehicle>();
#if DEBUG
                    if (UnderdogsDebug.DEBUG_TIMING)
                        MelonLogger.Msg($"[UE] 第{_uePass}轮扫描到 {all_vehicles.Length} 个载具");
#endif
                }

                float _passStart = Time.realtimeSinceStartup;
#if DEBUG
                if (UnderdogsDebug.DEBUG_TIMING)
                    MelonLogger.Msg($"[UE] === 第{_uePass}/{_uePassCount}轮改装开始 | 场景={sceneName} | 载具数={all_vehicles.Length} | {System.DateTime.Now:HH:mm:ss.fff} ===");
#endif

                foreach (Vehicle vic in all_vehicles)
                {
                    if (vic == null) continue;
                    int _vid = vic.GetInstanceID();
                    if (_modifiedVehicleIds.Contains(_vid)) continue;
                    _modifiedVehicleIds.Add(_vid);

                    string name = vic.FriendlyName;
#if DEBUG
                    bool dumpA1A3 = UnderdogsDebug.DEBUG_VEHICLE && name == "Leopard A1A3";
                    if (dumpA1A3)
                        UnderdogsDebug.DumpVehicleOpticsSnapshot(vic, "PRE", detailedOptics: true);
                    UnderdogsDebug.LogTiming($"[UE] 第{_uePass}轮 >> [{name}] ID={_vid} obj={vic.gameObject.name}");
#endif

                    try
                    {

                // 调用各载具模块的 Apply 方法
                Bmp1Main.Apply(vic);
                MarderMain.Apply(vic);
                if (MarderMain.marder_enabled.Value && MarderMain.marder_spike.Value && (name == "Marder 1A2" || name == "Marder A1+"))
                {
                    WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
                    WeaponSystemInfo milan_info = weapons_manager != null && weapons_manager.Weapons != null && weapons_manager.Weapons.Length > 1
                        ? weapons_manager.Weapons[1]
                        : null;

                    if (milan_info?.Weapon != null)
                    {
                        MarderSpikeSystem.TryApply(vic, milan_info);
                    }
                }

                LeopardMain.Apply(vic);

                // 调用各载具模块的 Apply 方法
                Btr70Main.Apply(vic);
                Pt76Main.Apply(vic);
                T64Main.Apply(vic);
                T54aMain.Apply(vic);
                T34Main.Apply(vic);

#if DEBUG
                    if (dumpA1A3)
                        UnderdogsDebug.DumpVehicleOpticsSnapshot(vic, "POST", detailedOptics: true);
#endif

                    } // end try
                    catch (System.Exception ex)
                    {
                        MelonLogger.Error($"[UE] Pass {_uePass} modification failed for [{name}] (ID={_vid}): {ex}");
                    }
                } // end foreach

#if DEBUG
                if (UnderdogsDebug.DEBUG_TIMING)
                    MelonLogger.Msg($"[UE] === 第{_uePass}/{_uePassCount}轮改装完成 | 耗时={Time.realtimeSinceStartup - _passStart:F3}s ===");
            } // end for pass

            if (UnderdogsDebug.DEBUG_TIMING)
                MelonLogger.Msg($"[UE] <<< OnSceneWasInitialized 结束 | 总耗时={Time.realtimeSinceStartup - _sceneStartTime:F3}s");
#else
            } // end for pass
#endif
        }
    }
}

