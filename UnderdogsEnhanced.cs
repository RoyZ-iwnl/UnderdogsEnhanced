using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using UnderdogsEnhanced;
using GHPC.Vehicle;
using GHPC;
using GHPC.State;
using GHPC.Weapons;
using GHPC.Weaponry;

[assembly: MelonInfo(typeof(UnderdogsEnhancedMod), "Underdogs Enhanced", "1.5.0-beta.5", "RoyZ;Based on ATLAS work")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace UnderdogsEnhanced
{
    public class UnderdogsEnhancedMod : MelonMod
    {
        public static MelonPreferences_Category cfg;

        private string currentGameplaySceneName = string.Empty;

        private static HashSet<int> _modifiedVehicleIds = new HashSet<int>();

        private static bool IsEnabled(MelonPreferences_Entry<bool> entry)
        {
            return entry != null && entry.Value;
        }

        private bool HasAnyVehicleFeatureEnabled()
        {
            return IsEnabled(Bmp1Main.bmp1_enabled)
                || IsEnabled(Brdm2Main.brdm2_enabled)
                || IsEnabled(MarderMain.marder_enabled)
                || IsEnabled(LeopardMain.leopard_enabled)
                || IsEnabled(Btr70Main.btr70_enabled)
                || IsEnabled(Pt76Main.pt76_enabled)
                || IsEnabled(T64Main.t64_enabled)
                || IsEnabled(T54aMain.t54a_enabled)
                || IsEnabled(T34Main.t34_enabled);
        }

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
            Marder25mmLowRateAudio.Initialize();
         }

        private IEnumerator EnsureAmmoConversionsOnGameReady(GameState _)
        {
            yield return UEAmmoConversionCoordinator.EnsureAmmoConversionsOnGameReady(_, currentGameplaySceneName);
        }

        private IEnumerator OnGameReady(GameState _)
        {
            if (!HasAnyVehicleFeatureEnabled())
                yield break;

            UEResourceController.LoadDynamicAssets();
            UEResourceController.PrewarmCommonVanillaDonors();

            Vehicle[] all_vehicles = Object.FindObjectsOfType<Vehicle>();
            if (all_vehicles == null || all_vehicles.Length == 0)
                yield break;

            var currentIds = new HashSet<int>(all_vehicles.Where(v => v != null).Select(v => v.GetInstanceID()));
            _modifiedVehicleIds.IntersectWith(currentIds);

            int _uePassCount = (currentGameplaySceneName == "TR01_showcase") ? 2 : 1;

            for (int _uePass = 1; _uePass <= _uePassCount; _uePass++)
            {
                if (_uePass > 1)
                {
                    yield return new WaitForSeconds(3f);
                    all_vehicles = Object.FindObjectsOfType<Vehicle>();
                }

                foreach (Vehicle vic in all_vehicles)
                {
                    if (vic == null) continue;
                    int _vid = vic.GetInstanceID();
                    if (_modifiedVehicleIds.Contains(_vid)) continue;
                    _modifiedVehicleIds.Add(_vid);

                    string name = vic.FriendlyName;

                    try
                    {
                        Bmp1Main.Apply(vic);
                        MarderMain.Apply(vic);

                        if (MarderMain.marder_enabled.Value && MarderMain.marder_spike.Value && (name == "Marder 1A2" || name == "Marder A1+" || name == "Marder A1-"))
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
                        Brdm2Main.Apply(vic);
                        Btr70Main.Apply(vic);
                        Pt76Main.Apply(vic);
                        T64Main.Apply(vic);
                        T54aMain.Apply(vic);
                        T34Main.Apply(vic);
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Error($"Pass {_uePass} modification failed for [{name}] (ID={_vid}): {ex}");
                    }
                }
            }
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
            Marder25mmLowRateAudio.OnSceneLoaded(sceneName);
            BMP1MissileCameraPatch.OnSceneChanged(sceneName);
            MarderSpikeSystem.OnSceneChanged(sceneName);

            UEResourceController.UnloadDynamicAssets();

            bool hasVehicleFeaturesEnabled = HasAnyVehicleFeatureEnabled();

            if (!hasVehicleFeaturesEnabled)
            {
                if (UECommonUtil.IsPrimaryMenuScene(sceneName))
                    UEResourceController.ReleaseVanillaDonorAssets();

                return;
            }

            if (UECommonUtil.IsPrimaryMenuScene(sceneName))
            {
                UEResourceController.ReleaseVanillaDonorAssets();
                UEResourceController.LoadStaticAssets();
                EMES18Optic.LoadStaticAssets();
            }

            if (UECommonUtil.IsMenuScene(sceneName)) return;

            UEResourceController.PrewarmSceneSpecificVanillaDonors(sceneName);

            StateController.RunOrDefer(GameState.GameReady, new GameStateEventHandler(OnGameReady), GameStatePriority.Medium);

            if (Bmp1Main.ShouldScheduleAmmoConversion() || MarderMain.ShouldScheduleAmmoConversion() || LeopardMain.ShouldScheduleAmmoConversion())
                StateController.RunOrDefer(GameState.GameReady, new GameStateEventHandler(EnsureAmmoConversionsOnGameReady), GameStatePriority.Medium);

            LeopardMain.OnSceneLoaded();
        }
    }
}

