using System;
using System.Collections;
using System.Collections.Generic;
using GHPC.State;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal static class UEAmmoConversionCoordinator
    {
        private static readonly HashSet<int> AppliedVehicleIds = new HashSet<int>();

        internal static void ResetSceneState()
        {
            AppliedVehicleIds.Clear();
            Leopard1Ammo.ResetSceneState();
            Marder25mmAmmo.ResetSceneState();
            Marder35mmAmmo.ResetSceneState();
        }

        internal static IEnumerator EnsureAmmoConversionsOnGameReady(GameState _, string sceneName)
        {
            // 第一轮同步扫描（无重试）
            RunPassOnce("GameReady");

            // 非 showcase 场景直接结束
            if (!string.Equals(sceneName, "TR01_showcase", StringComparison.Ordinal))
                yield break;

#if DEBUG
            if (UnderdogsDebug.DEBUG_TIMING)
                MelonLogger.Msg("[UE Ammo] 试车场等待3秒后执行第二轮弹药补扫");
#endif

            // showcase 等待3秒后第二轮补扫
            yield return new WaitForSeconds(3f);
            RunPassOnce("TR01_showcase second pass");
        }

        private static void RunPassOnce(string label)
        {
            Vehicle[] allVehicles = UnityEngine.Object.FindObjectsOfType<Vehicle>();

            foreach (Vehicle vehicle in allVehicles)
            {
                if (vehicle == null) continue;

                if (!MatchesAny(vehicle)) continue;

                int vehicleId = vehicle.GetInstanceID();
                if (AppliedVehicleIds.Contains(vehicleId)) continue;

                if (TryApplyAll(vehicle))
                {
                    AppliedVehicleIds.Add(vehicleId);
#if DEBUG
                    if (UnderdogsDebug.DEBUG_TIMING)
                        MelonLogger.Msg($"[UE Ammo] {label}: {vehicle.FriendlyName} 弹药应用成功");
#endif
                }
            }
        }

        private static bool MatchesAny(Vehicle vehicle)
        {
            if (vehicle == null) return false;

            // BMP1 MCLOS
            if (MatchesBmp1Mclos(vehicle)) return true;

            // Leopard1 Ammo
            if (MatchesLeopard1Ammo(vehicle)) return true;

            // Marder Spike
            if (MatchesMarderSpike(vehicle)) return true;

            // Marder 25mm
            if (MatchesMarder25mm(vehicle)) return true;

            // Marder 35mm
            if (MatchesMarder35mm(vehicle)) return true;

            return false;
        }

        private static bool TryApplyAll(Vehicle vehicle)
        {
            if (vehicle == null) return false;

            bool matched = false;

            // BMP1 MCLOS
            if (MatchesBmp1Mclos(vehicle))
            {
                matched = true;
                if (!TryApplyBmp1Mclos(vehicle))
                    return false;
            }

            // Leopard1 Ammo
            if (MatchesLeopard1Ammo(vehicle))
            {
                matched = true;
                if (!TryApplyLeopard1Ammo(vehicle))
                    return false;
            }

            // Marder Spike
            if (MatchesMarderSpike(vehicle))
            {
                matched = true;
                if (!TryApplyMarderSpike(vehicle))
                    return false;
            }

            // Marder 25mm
            if (MatchesMarder25mm(vehicle))
            {
                matched = true;
                if (!TryApplyMarder25mm(vehicle))
                    return false;
            }

            // Marder 35mm
            if (MatchesMarder35mm(vehicle))
            {
                matched = true;
                if (!TryApplyMarder35mm(vehicle))
                    return false;
            }

            return matched;
        }

        #region BMP1 MCLOS

        private static bool MatchesBmp1Mclos(Vehicle vehicle)
        {
            return vehicle != null
                && Bmp1Main.bmp1_enabled.Value
                && Bmp1Main.bmp1_mclos.Value
                && Bmp1Main.IsMclosTargetVehicle(vehicle.FriendlyName);
        }

        private static bool TryApplyBmp1Mclos(Vehicle vehicle)
        {
            return Bmp1Main.TryApplyMclos(vehicle, false);
        }

        #endregion

        #region Leopard 1 Ammo

        private static bool MatchesLeopard1Ammo(Vehicle vehicle)
        {
            return vehicle != null
                && LeopardMain.IsSupportedVariant(vehicle.FriendlyName)
                && LeopardMain.IsCustomAmmoEnabled(vehicle.FriendlyName);
        }

        private static bool TryApplyLeopard1Ammo(Vehicle vehicle)
        {
            string ammoType = LeopardMain.GetAmmoType(vehicle.FriendlyName);
            return Leopard1Ammo.TryApply(vehicle, ammoType);
        }

        #endregion

        #region Marder 25mm

        private static bool MatchesMarder25mm(Vehicle vehicle)
        {
            return Marder25mmAmmo.MatchesVehicle(vehicle);
        }

        private static bool TryApplyMarder25mm(Vehicle vehicle)
        {
            return Marder25mmAmmo.TryApply(vehicle);
        }

        #endregion

        #region Marder 35mm

        private static bool MatchesMarder35mm(Vehicle vehicle)
        {
            return Marder35mmAmmo.MatchesVehicle(vehicle);
        }

        private static bool TryApplyMarder35mm(Vehicle vehicle)
        {
            return Marder35mmAmmo.TryApply(vehicle);
        }

        #endregion

        #region Marder Spike

        private static bool MatchesMarderSpike(Vehicle vehicle)
        {
            return vehicle != null
                && MarderMain.marder_enabled.Value
                && MarderMain.marder_spike.Value
                && (vehicle.FriendlyName == "Marder 1A2" || vehicle.FriendlyName == "Marder A1+");
        }

        private static bool TryApplyMarderSpike(Vehicle vehicle)
        {
            WeaponsManager weaponsManager = vehicle.GetComponent<WeaponsManager>();
            WeaponSystemInfo milanInfo = weaponsManager != null && weaponsManager.Weapons != null && weaponsManager.Weapons.Length > 1
                ? weaponsManager.Weapons[1]
                : null;

            if (milanInfo?.Weapon == null)
                return false;

            return MarderSpikeAmmo.EnsureApplied(milanInfo.Weapon, vehicle);
        }

        #endregion
    }
}
