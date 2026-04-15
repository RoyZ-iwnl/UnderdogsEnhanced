using System;
using System.Collections.Generic;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public static class Leopard1Ammo
    {
        private sealed class AmmoBundle
        {
            internal AmmoType OriginalAmmo;
            internal AmmoType Ammo;
            internal AmmoType.AmmoClip Clip;
            internal AmmoCodexScriptable AmmoCodex;
            internal AmmoClipCodexScriptable ClipCodex;
        }

        // DM33 参数
        public const float DM33_RHA_PENETRATION = 420f;
        public const float DM33_MUZZLE_VELOCITY = 1455f;
        public const float DM33_MASS = 3.79f;

        // DM63 参数
        public const float DM63_RHA_PENETRATION = 447f;
        public const float DM63_MUZZLE_VELOCITY = 1470f;
        public const float DM63_MASS = 4.4f;

        // 弹药类型字典：key = "DM33" 或 "DM63"
        private static readonly Dictionary<string, AmmoBundle> bundleCache = new Dictionary<string, AmmoBundle>(StringComparer.OrdinalIgnoreCase);

        // 弹药类型映射：弹药名称 -> 弹药参数
        private static readonly Dictionary<string, (float pen, float velocity, float mass)> ammoParams = new Dictionary<string, (float, float, float)>
        {
            { "DM33", (DM33_RHA_PENETRATION, DM33_MUZZLE_VELOCITY, DM33_MASS) },
            { "DM63", (DM63_RHA_PENETRATION, DM63_MUZZLE_VELOCITY, DM63_MASS) }
        };

        public static void Init(AmmoType donorAmmo)
        {
            // 预创建所有弹药类型
            foreach (string ammoType in ammoParams.Keys)
            {
                GetOrCreateBundle(donorAmmo, ammoType);
            }
        }

        internal static void ResetSceneState()
        {
            foreach (AmmoBundle bundle in bundleCache.Values)
            {
                try
                {
                    if (bundle?.Ammo?.VisualModel != null)
                        UnityEngine.Object.Destroy(bundle.Ammo.VisualModel);
                }
                catch
                {
                }
            }

            bundleCache.Clear();
        }

        internal static bool TryApply(Vehicle vehicle, string ammoType)
        {
            if (string.IsNullOrEmpty(ammoType) || string.Equals(ammoType, "Default", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!ammoParams.ContainsKey(ammoType))
                return false;

            LoadoutManager loadoutManager = vehicle != null ? vehicle.GetComponent<LoadoutManager>() : null;
            if (loadoutManager?.LoadedAmmoList?.AmmoClips == null)
                return false;

            WeaponsManager weaponsManager = vehicle.GetComponent<WeaponsManager>();
            WeaponSystemInfo weaponInfo = weaponsManager?.Weapons?[0];
            WeaponSystem weapon = weaponInfo?.Weapon as WeaponSystem;

            for (int i = 0; i < loadoutManager.LoadedAmmoList.AmmoClips.Length; i++)
            {
                AmmoClipCodexScriptable clipCodex = loadoutManager.LoadedAmmoList.AmmoClips[i];
                AmmoType donorAmmo = clipCodex?.ClipType?.MinimalPattern?[0]?.AmmoType;
                if (!IsSupportedDonor(donorAmmo))
                    continue;

                AmmoBundle bundle = GetOrCreateBundle(donorAmmo, ammoType);
                if (bundle == null)
                    return false;

                loadoutManager.LoadedAmmoList.AmmoClips[i] = bundle.ClipCodex;

                // 先清空所有 rack，再重建 loadout，避免沿用上一关/上一套弹药残留的可视化状态。
                UECommonUtil.EmptyAllLoadoutRacks(loadoutManager);
                UECommonUtil.ClearAmmoInBreech(weapon?.Feed);
                UECommonUtil.RespawnLoadout(loadoutManager);
                UECommonUtil.RestartFeed(weapon?.Feed);
                loadoutManager.RegisterAllBallistics();

                //MelonLogger.Msg($"[Leopard1Ammo] {vehicle.FriendlyName} {ammoType}: {donorAmmo.Name} -> {bundle.Ammo.Name}");

                return true;
            }

            return false;
        }

        private static bool IsSupportedDonor(AmmoType ammo)
        {
            if (ammo == null || string.IsNullOrEmpty(ammo.Name))
                return false;

            // 支持 DM23 和 DM13 作为 donor
            return ammo.Name.IndexOf("DM23", StringComparison.OrdinalIgnoreCase) >= 0
                || ammo.Name.IndexOf("DM13", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetFamilyKey(AmmoType ammo)
        {
            if (ammo == null || string.IsNullOrEmpty(ammo.Name))
                return null;

            if (ammo.Name.IndexOf("DM23", StringComparison.OrdinalIgnoreCase) >= 0)
                return "DM23";

            if (ammo.Name.IndexOf("DM13", StringComparison.OrdinalIgnoreCase) >= 0)
                return "DM13";

            return ammo.Name;
        }

        private static AmmoBundle GetOrCreateBundle(AmmoType donorAmmo, string ammoType)
        {
            if (!IsSupportedDonor(donorAmmo))
                return null;

            string familyKey = GetFamilyKey(donorAmmo);
            if (string.IsNullOrEmpty(familyKey))
                return null;

            // 缓存 key = "{ammoType}_{familyKey}"，例如 "DM33_DM23"
            string cacheKey = $"{ammoType}_{familyKey}";

            if (bundleCache.TryGetValue(cacheKey, out AmmoBundle existingBundle) && IsBundleUsable(existingBundle))
                return existingBundle;

            if (existingBundle != null)
                bundleCache.Remove(cacheKey);

            if (!ammoParams.TryGetValue(ammoType, out var params_))
                return null;

            if (donorAmmo.VisualModel == null)
            {
                MelonLogger.Warning($"[UE Ammo] Skip Leopard ammo bundle rebuild for {ammoType}: donor visual model missing ({donorAmmo.Name})");
                return null;
            }

            AmmoBundle bundle = new AmmoBundle();
            bundle.OriginalAmmo = donorAmmo;

            bundle.Ammo = new AmmoType();
            UECommonUtil.ShallowCopy(bundle.Ammo, donorAmmo);
            bundle.Ammo.Name = $"{ammoType} APFSDS-T";
            bundle.Ammo.RhaPenetration = params_.pen;
            bundle.Ammo.MuzzleVelocity = params_.velocity;
            bundle.Ammo.Mass = params_.mass;
            bundle.Ammo.CachedIndex = -1;

            bundle.AmmoCodex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            bundle.AmmoCodex.AmmoType = bundle.Ammo;
            bundle.AmmoCodex.name = $"ammo_{ammoType.ToLower()}";

            bundle.Clip = new AmmoType.AmmoClip();
            bundle.Clip.Capacity = 1;
            bundle.Clip.Name = $"{ammoType} APFSDS-T";
            bundle.Clip.MinimalPattern = new AmmoCodexScriptable[] { bundle.AmmoCodex };

            bundle.ClipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            bundle.ClipCodex.name = $"clip_{ammoType.ToLower()}";
            bundle.ClipCodex.ClipType = bundle.Clip;

            if (bundle.Ammo.VisualModel != null)
            {
                GameObject vis = UnityEngine.Object.Instantiate(bundle.Ammo.VisualModel);
                vis.name = $"{ammoType} visual";
                bundle.Ammo.VisualModel = vis;

                AmmoStoredVisual ammoStoredVisual = vis.GetComponent<AmmoStoredVisual>();
                if (ammoStoredVisual != null)
                {
                    ammoStoredVisual.AmmoType = bundle.Ammo;
                    ammoStoredVisual.AmmoScriptable = bundle.AmmoCodex;
                }
            }

            bundleCache[cacheKey] = bundle;
            return bundle;
        }

        private static bool IsBundleUsable(AmmoBundle bundle)
        {
            return bundle != null
                && bundle.Ammo != null
                && bundle.ClipCodex != null
                && bundle.AmmoCodex != null
                && bundle.Clip != null
                && bundle.Ammo.VisualModel != null;
        }
    }
}
