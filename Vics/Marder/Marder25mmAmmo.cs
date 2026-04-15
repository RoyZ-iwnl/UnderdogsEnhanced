using System;
using System.Collections.Generic;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal static class Marder25mmAmmo
    {
        private enum BeltKind
        {
            AP,
            HE
        }

        private sealed class AmmoBundle
        {
            internal AmmoType OriginalAmmo;
            internal AmmoType Ammo;
            internal AmmoType.AmmoClip Clip;
            internal AmmoCodexScriptable Codex;
            internal AmmoClipCodexScriptable ClipCodex;
        }

        private const string DonorApName = "25mm APDS-T M791";
        private const string DonorHeName = "25mm HEI-T M792";
        private const string Pmb090Name = "25mm APFSDS-T PMB090";

        private static AmmoCodexScriptable nativeM791Codex;
        private static AmmoCodexScriptable nativeM792Codex;

        private static readonly Dictionary<string, AmmoBundle> BundleCache = new Dictionary<string, AmmoBundle>(StringComparer.OrdinalIgnoreCase);

        // PMB090参数：瑞士钢针弹，92mm穿透，1385m/s，0.10kg弹芯
        private static readonly (string name, float pen, float velocity, float mass) Pmb090Params =
            (Pmb090Name, 92f, 1385f, 0.10f);

        internal static void ResetSceneState()
        {
            foreach (AmmoBundle bundle in BundleCache.Values)
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

            BundleCache.Clear();
        }

        internal static bool MatchesVehicle(Vehicle vehicle)
        {
            return vehicle != null
                && MarderMain.marder_enabled.Value
                && MarderMain.Is25mmSelected()
                && MarderMain.IsSupportedVariant(vehicle.FriendlyName);
        }

        internal static bool TryApply(Vehicle vehicle)
        {
            if (!MatchesVehicle(vehicle))
                return false;

            LoadoutManager loadoutManager = vehicle.GetComponent<LoadoutManager>();
            WeaponsManager weaponsManager = vehicle.GetComponent<WeaponsManager>();
            WeaponSystemInfo weaponInfo = weaponsManager?.Weapons != null && weaponsManager.Weapons.Length > 0
                ? weaponsManager.Weapons[0]
                : null;
            WeaponSystem weapon = weaponInfo?.Weapon;

            if (loadoutManager?.LoadedAmmoList?.AmmoClips == null || weapon?.Feed?.ReadyRack?.ClipTypes == null)
                return false;

            string apAmmoType = MarderMain.Get25mmApAmmoType();
            if (!EnsureAmmoBundles(apAmmoType))
                return false;

            if (!TryResolveSelectedBundles(loadoutManager, weapon, apAmmoType, out Dictionary<BeltKind, AmmoBundle> selectedBundles))
                return false;

            ApplyRackLoadoutOverrides(loadoutManager, selectedBundles);
            ApplyLoadedAmmoListOverrides(loadoutManager, selectedBundles);

            UECommonUtil.EmptyAllLoadoutRacks(loadoutManager);
            UECommonUtil.ClearAmmoInBreech(weapon.Feed);
            UECommonUtil.RespawnLoadout(loadoutManager);
            ApplyReadyRackToWeapon(weapon, selectedBundles);
            loadoutManager.RegisterAllBallistics();

            AmmoType currentAmmo = weapon.Feed?.ReadyRack?.ClipTypes != null && weapon.Feed.ReadyRack.ClipTypes.Length > 0
                ? weapon.Feed.ReadyRack.ClipTypes[0]?.MinimalPattern?[0]?.AmmoType
                : null;
            UECommonUtil.SyncWeaponCurrentAmmo(weapon, currentAmmo);

            return true;
        }

        private static bool TryResolveSelectedBundles(LoadoutManager loadoutManager, WeaponSystem weapon, string apAmmoType, out Dictionary<BeltKind, AmmoBundle> selectedBundles)
        {
            selectedBundles = new Dictionary<BeltKind, AmmoBundle>();

            if (loadoutManager?.LoadedAmmoList?.AmmoClips != null)
            {
                for (int i = 0; i < loadoutManager.LoadedAmmoList.AmmoClips.Length; i++)
                {
                    AmmoType.AmmoClip clip = loadoutManager.LoadedAmmoList.AmmoClips[i]?.ClipType;
                    if (!IsMainGunAmmoClip(clip))
                        continue;

                    BeltKind belt = ClassifyBelt(clip, i);
                    if (selectedBundles.ContainsKey(belt))
                        continue;

                    AmmoBundle bundle = GetOrCreateBundle(clip, belt, apAmmoType);
                    if (bundle != null)
                        selectedBundles[belt] = bundle;
                }
            }

            if ((!selectedBundles.ContainsKey(BeltKind.AP) || !selectedBundles.ContainsKey(BeltKind.HE)) && weapon?.Feed?.ReadyRack?.ClipTypes != null)
            {
                for (int i = 0; i < weapon.Feed.ReadyRack.ClipTypes.Length; i++)
                {
                    AmmoType.AmmoClip clip = weapon.Feed.ReadyRack.ClipTypes[i];
                    if (!IsMainGunAmmoClip(clip))
                        continue;

                    BeltKind belt = ClassifyBelt(clip, i);
                    if (selectedBundles.ContainsKey(belt))
                        continue;

                    AmmoBundle bundle = GetOrCreateBundle(clip, belt, apAmmoType);
                    if (bundle != null)
                        selectedBundles[belt] = bundle;
                }
            }

            return selectedBundles.ContainsKey(BeltKind.AP) && selectedBundles.ContainsKey(BeltKind.HE);
        }

        private static void ApplyLoadedAmmoListOverrides(LoadoutManager loadoutManager, Dictionary<BeltKind, AmmoBundle> selectedBundles)
        {
            if (loadoutManager?.LoadedAmmoList?.AmmoClips == null || selectedBundles == null || selectedBundles.Count == 0)
                return;

            if (loadoutManager.LoadedAmmoList.AmmoClips.Length >= 2)
            {
                loadoutManager.LoadedAmmoList.AmmoClips[0] = selectedBundles[BeltKind.AP].ClipCodex;
                loadoutManager.LoadedAmmoList.AmmoClips[1] = selectedBundles[BeltKind.HE].ClipCodex;
            }

            for (int i = 2; i < loadoutManager.LoadedAmmoList.AmmoClips.Length; i++)
            {
                AmmoType.AmmoClip clip = loadoutManager.LoadedAmmoList.AmmoClips[i]?.ClipType;
                if (!IsMainGunAmmoClip(clip))
                    continue;

                BeltKind belt = ClassifyBelt(clip, i);
                if (!selectedBundles.TryGetValue(belt, out AmmoBundle replacement))
                    continue;

                loadoutManager.LoadedAmmoList.AmmoClips[i] = replacement.ClipCodex;
            }
        }

        private static void ApplyRackLoadoutOverrides(LoadoutManager loadoutManager, Dictionary<BeltKind, AmmoBundle> selectedBundles)
        {
            if (loadoutManager?.RackLoadouts == null || selectedBundles == null || selectedBundles.Count == 0)
                return;

            for (int i = 0; i < loadoutManager.RackLoadouts.Length; i++)
            {
                var rackLoadout = loadoutManager.RackLoadouts[i];
                if (rackLoadout == null)
                    continue;

                if (rackLoadout.OverrideInitialClips != null)
                {
                    for (int j = 0; j < rackLoadout.OverrideInitialClips.Length; j++)
                    {
                        AmmoClipCodexScriptable originalClipCodex = rackLoadout.OverrideInitialClips[j];
                        AmmoType.AmmoClip originalClip = originalClipCodex?.ClipType;
                        if (!IsMainGunAmmoClip(originalClip))
                            continue;

                        BeltKind belt = ClassifyBelt(originalClip, j);
                        if (!selectedBundles.TryGetValue(belt, out AmmoBundle replacement))
                            continue;

                        rackLoadout.OverrideInitialClips[j] = replacement.ClipCodex;
                    }
                }

                GHPC.Weapons.AmmoRack rack = rackLoadout.Rack;
                if (rack?.ClipTypes == null)
                    continue;

                for (int j = 0; j < rack.ClipTypes.Length; j++)
                {
                    AmmoType.AmmoClip originalClip = rack.ClipTypes[j];
                    if (!IsMainGunAmmoClip(originalClip))
                        continue;

                    BeltKind belt = ClassifyBelt(originalClip, j);
                    if (!selectedBundles.TryGetValue(belt, out AmmoBundle replacement))
                        continue;

                    rack.ClipTypes[j] = replacement.Clip;
                }
            }
        }

        private static void ApplyReadyRackToWeapon(WeaponSystem weapon, Dictionary<BeltKind, AmmoBundle> selectedBundles)
        {
            if (weapon?.Feed?.ReadyRack?.ClipTypes == null || selectedBundles == null || selectedBundles.Count == 0)
                return;

            AmmoType.AmmoClip currentClip = null;
            AmmoType currentAmmo = weapon.Feed.AmmoTypeInBreech;

            if (weapon.Feed.ReadyRack.ClipTypes.Length >= 2)
            {
                weapon.Feed.ReadyRack.ClipTypes[0] = selectedBundles[BeltKind.AP].Clip;
                weapon.Feed.ReadyRack.ClipTypes[1] = selectedBundles[BeltKind.HE].Clip;
                currentClip = selectedBundles[BeltKind.AP].Clip;
                currentAmmo = selectedBundles[BeltKind.AP].Ammo;
            }
            else
            {
                for (int i = 0; i < weapon.Feed.ReadyRack.ClipTypes.Length; i++)
                {
                    AmmoType.AmmoClip originalClip = weapon.Feed.ReadyRack.ClipTypes[i];
                    if (!IsMainGunAmmoClip(originalClip))
                        continue;

                    BeltKind belt = ClassifyBelt(originalClip, i);
                    if (!selectedBundles.TryGetValue(belt, out AmmoBundle replacement))
                        continue;

                    weapon.Feed.ReadyRack.ClipTypes[i] = replacement.Clip;

                    if (currentClip == null)
                    {
                        currentClip = replacement.Clip;
                        currentAmmo = replacement.Ammo;
                    }
                }
            }

            if (currentClip != null)
                UECommonUtil.ResetFeedForClip(weapon.Feed, currentAmmo, currentClip, true, queueClipType: true);
            else
                UECommonUtil.RestartFeed(weapon.Feed);
        }

        private static BeltKind ClassifyBelt(AmmoType.AmmoClip originalClip, int fallbackIndex)
        {
            AmmoType donorAmmo = originalClip?.MinimalPattern != null && originalClip.MinimalPattern.Length > 0
                ? originalClip.MinimalPattern[0]?.AmmoType
                : null;

            string sourceName = $"{originalClip?.Name ?? string.Empty} {donorAmmo?.Name ?? string.Empty}".ToLowerInvariant();

            if (donorAmmo != null)
            {
                if (donorAmmo.Category == AmmoType.AmmoCategory.Explosive)
                    return BeltKind.HE;

                if (donorAmmo.Category == AmmoType.AmmoCategory.Penetrator)
                    return BeltKind.AP;
            }

            return fallbackIndex == 0 ? BeltKind.AP : BeltKind.HE;
        }

        private static AmmoBundle GetOrCreateBundle(AmmoType.AmmoClip originalClip, BeltKind belt, string apAmmoType)
        {
            if (originalClip == null)
                return null;

            int desiredCapacity = belt == BeltKind.AP ? MarderMain.Get25mmApCount() : MarderMain.Get25mmHeCount();
            string beltKey = belt == BeltKind.AP ? apAmmoType : "M792";
            string cacheKey = $"{beltKey}|{desiredCapacity}|{GetClipKey(originalClip)}";
            if (BundleCache.TryGetValue(cacheKey, out AmmoBundle existing) && IsBundleUsable(existing))
                return existing;

            if (existing != null)
                BundleCache.Remove(cacheKey);

            AmmoBundle bundle = new AmmoBundle();

            // HE弹链：直接使用原生M792 codex
            if (belt == BeltKind.HE)
            {
                bundle.OriginalAmmo = nativeM792Codex.AmmoType;
                bundle.Ammo = nativeM792Codex.AmmoType;
                bundle.Codex = nativeM792Codex;
            }
            // AP弹链：M791直接使用原生codex，PMB090需要创建新弹药
            else if (string.Equals(apAmmoType, "M791", StringComparison.OrdinalIgnoreCase))
            {
                bundle.OriginalAmmo = nativeM791Codex.AmmoType;
                bundle.Ammo = nativeM791Codex.AmmoType;
                bundle.Codex = nativeM791Codex;
            }
            else // PMB090：复制M791模板并修改参数
            {
                AmmoType donorAmmo = originalClip.MinimalPattern != null && originalClip.MinimalPattern.Length > 0
                    ? originalClip.MinimalPattern[0]?.AmmoType
                    : null;
                if (donorAmmo == null)
                    donorAmmo = nativeM791Codex.AmmoType;

                bundle.OriginalAmmo = donorAmmo;
                bundle.Ammo = new AmmoType();
                UECommonUtil.ShallowCopy(bundle.Ammo, donorAmmo);
                bundle.Ammo.CachedIndex = -1;
                bundle.Ammo.Name = Pmb090Params.name;
                bundle.Ammo.RhaPenetration = Pmb090Params.pen;
                bundle.Ammo.MuzzleVelocity = Pmb090Params.velocity;
                bundle.Ammo.Mass = Pmb090Params.mass;

                bundle.Codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
                bundle.Codex.name = $"ammo_marder_pmb090_{desiredCapacity}";
                bundle.Codex.AmmoType = bundle.Ammo;

                if (bundle.Ammo.VisualModel != null)
                {
                    GameObject visual = UnityEngine.Object.Instantiate(bundle.Ammo.VisualModel);
                    visual.name = $"{bundle.Ammo.Name} visual";
                    bundle.Ammo.VisualModel = visual;

                    AmmoStoredVisual ammoStoredVisual = visual.GetComponent<AmmoStoredVisual>();
                    if (ammoStoredVisual != null)
                    {
                        ammoStoredVisual.AmmoType = bundle.Ammo;
                        ammoStoredVisual.AmmoScriptable = bundle.Codex;
                    }
                }
            }

            // 创建clip（自定义容量）
            bundle.Clip = new AmmoType.AmmoClip();
            UECommonUtil.ShallowCopy(bundle.Clip, originalClip);
            bundle.Clip.Name = bundle.Ammo.Name;
            bundle.Clip.Capacity = desiredCapacity;
            bundle.Clip.MinimalPattern = new[] { bundle.Codex };

            // 创建clip codex（用于LoadedAmmoList）
            bundle.ClipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            bundle.ClipCodex.name = belt == BeltKind.AP
                ? $"clip_marder_{apAmmoType.ToLowerInvariant()}_{desiredCapacity}"
                : $"clip_marder_m792_{desiredCapacity}";
            bundle.ClipCodex.ClipType = bundle.Clip;

            BundleCache[cacheKey] = bundle;
            return bundle;
        }

        private static bool EnsureAmmoBundles(string apAmmoType)
        {
            // 验证AP弹药类型
            if (!string.Equals(apAmmoType, "PMB090", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(apAmmoType, "M791", StringComparison.OrdinalIgnoreCase))
            {
                MelonLogger.Warning($"[Marder 25mm] Unsupported AP ammo type '{apAmmoType}', falling back to PMB090.");
                return EnsureAmmoBundles("PMB090");
            }

            // 缓存原生弹药codex
            if (nativeM791Codex == null || nativeM792Codex == null)
            {
                AmmoCodexScriptable[] codices = Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>();
                foreach (AmmoCodexScriptable codex in codices)
                {
                    if (codex?.AmmoType?.Name == null)
                        continue;

                    if (string.Equals(codex.AmmoType.Name, DonorApName, StringComparison.OrdinalIgnoreCase))
                        nativeM791Codex = codex;
                    else if (string.Equals(codex.AmmoType.Name, DonorHeName, StringComparison.OrdinalIgnoreCase))
                        nativeM792Codex = codex;
                }
            }

            if (nativeM791Codex != null && nativeM792Codex != null)
                return true;

            MelonLogger.Warning("[Marder 25mm] Failed to locate native M791/M792 ammo codex.");
            return false;
        }

        private static string GetClipKey(AmmoType.AmmoClip clip)
        {
            if (clip == null)
                return string.Empty;

            string ammoName = clip.MinimalPattern != null && clip.MinimalPattern.Length > 0
                ? clip.MinimalPattern[0]?.AmmoType?.Name ?? string.Empty
                : string.Empty;

            return $"{clip.Name}|{clip.Capacity}|{ammoName}";
        }

        private static bool IsBundleUsable(AmmoBundle bundle)
        {
            if (bundle == null || bundle.Ammo == null || bundle.Codex == null)
                return false;

            // 原生codex不需要检查Clip/ClipCodex/VisualModel
            if (bundle.Codex == nativeM791Codex || bundle.Codex == nativeM792Codex)
                return bundle.Clip != null && bundle.ClipCodex != null;

            // 自创建的弹药需要完整检查
            return bundle.Clip != null
                && bundle.ClipCodex != null
                && bundle.Ammo.VisualModel != null;
        }

        private static bool IsMainGunAmmoClip(AmmoType.AmmoClip clip)
        {
            AmmoType ammo = clip?.MinimalPattern != null && clip.MinimalPattern.Length > 0
                ? clip.MinimalPattern[0]?.AmmoType
                : null;

            if (ammo == null)
                return false;

            if (ammo.Category != AmmoType.AmmoCategory.Penetrator && ammo.Category != AmmoType.AmmoCategory.Explosive)
                return false;

            return ammo.Caliber >= 15f && ammo.Caliber <= 35f;
        }
    }
}
