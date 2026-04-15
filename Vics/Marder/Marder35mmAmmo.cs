using System;
using System.Collections.Generic;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal static class Marder35mmAmmo
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

        private const string Dm33Name = "35mm APFSDS-T DM33";
        private const string Dm23Name = "35mm APDS-T DM23";
        private const string Dm13Name = "35mm AP-T DM13";
        private const string Dm11Name = "35mm HEI-T DM11A1";

        // Donor弹药：使用30mm弹药作为模板
        private const string DonorApName = "30mm APDS-T 3UBR6";
        private const string DonorHeName = "30mm HE-T 3UOR6";

        private static readonly Dictionary<string, AmmoBundle> BundleCache = new Dictionary<string, AmmoBundle>(StringComparer.OrdinalIgnoreCase);

        // 35mm AP弹药参数：DM33钢针、DM23脱穿、DM13穿甲
        private static readonly Dictionary<string, (string name, float pen, float velocity, float mass, float tnt)> ApAmmoParams =
            new Dictionary<string, (string, float, float, float, float)>(StringComparer.OrdinalIgnoreCase)
        {
            // DM33 钢针：1450速 131穿 0.25 Mass
            { "DM33", (Dm33Name, 131f, 1450f, 0.25f, 0f) },
            // DM23 脱穿：1400速 127穿 0.38 Mass
            { "DM23", (Dm23Name, 127f, 1400f, 0.38f, 0f) },
            // DM13 穿甲：1175速 66穿 37.4克当量 0.55 Mass
            { "DM13", (Dm13Name, 66f, 1175f, 0.55f, 0.0374f) }
        };

        // HE弹药固定：DM11A1: 1175速 11穿 204克当量 0.55 Mass
        private static readonly (string name, float pen, float velocity, float mass, float tnt) HeParams =
            (Dm11Name, 11f, 1175f, 0.55f, 0.204f);

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
                && MarderMain.Is35mmSelected()
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

            string apAmmoType = MarderMain.Get35mmApAmmoType();
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

            if (sourceName.Contains("hei") || sourceName.Contains("he") || sourceName.Contains("frag") || sourceName.Contains("sprg"))
                return BeltKind.HE;

            if (sourceName.Contains("ap") || sourceName.Contains("ds") || sourceName.Contains("hvap"))
                return BeltKind.AP;

            return fallbackIndex == 0 ? BeltKind.AP : BeltKind.HE;
        }

        private static AmmoBundle GetOrCreateBundle(AmmoType.AmmoClip originalClip, BeltKind belt, string apAmmoType)
        {
            if (originalClip == null)
                return null;

            int desiredCapacity = belt == BeltKind.AP ? MarderMain.Get35mmApCount() : MarderMain.Get35mmHeCount();
            string beltKey = belt == BeltKind.AP ? apAmmoType : "DM11A1";
            string cacheKey = $"{beltKey}|{desiredCapacity}|{GetClipKey(originalClip)}";
            if (BundleCache.TryGetValue(cacheKey, out AmmoBundle existing) && IsBundleUsable(existing))
                return existing;

            if (existing != null)
                BundleCache.Remove(cacheKey);

            AmmoType donorAmmo = originalClip.MinimalPattern != null && originalClip.MinimalPattern.Length > 0
                ? originalClip.MinimalPattern[0]?.AmmoType
                : null;
            if (donorAmmo == null)
                return null;

            AmmoBundle bundle = new AmmoBundle();
            bundle.OriginalAmmo = donorAmmo;
            bundle.Ammo = new AmmoType();
            UECommonUtil.ShallowCopy(bundle.Ammo, donorAmmo);
            bundle.Ammo.CachedIndex = -1;

            // 应用35mm弹药参数
            if (belt == BeltKind.AP)
            {
                if (!ApAmmoParams.TryGetValue(apAmmoType, out var apParams))
                    return null;

                bundle.Ammo.Name = apParams.name;
                bundle.Ammo.RhaPenetration = apParams.pen;
                bundle.Ammo.MuzzleVelocity = apParams.velocity;
                bundle.Ammo.Mass = apParams.mass;
                bundle.Ammo.TntEquivalentKg = apParams.tnt;
            }
            else
            {
                bundle.Ammo.Name = HeParams.name;
                bundle.Ammo.RhaPenetration = HeParams.pen;
                bundle.Ammo.MuzzleVelocity = HeParams.velocity;
                bundle.Ammo.Mass = HeParams.mass;
                bundle.Ammo.TntEquivalentKg = HeParams.tnt;
            }

            // 设置35mm口径
            bundle.Ammo.Caliber = 35f;
            bundle.Ammo.SectionalArea = 0.00096f;  // 35mm截面积
            bundle.Ammo.Coeff = 0.012f;

            bundle.Codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            bundle.Codex.name = belt == BeltKind.AP
                ? $"ammo_marder_35mm_{apAmmoType.ToLowerInvariant()}_{desiredCapacity}"
                : $"ammo_marder_35mm_dm11a1_{desiredCapacity}";
            bundle.Codex.AmmoType = bundle.Ammo;

            bundle.Clip = new AmmoType.AmmoClip();
            UECommonUtil.ShallowCopy(bundle.Clip, originalClip);
            bundle.Clip.Name = bundle.Ammo.Name;
            bundle.Clip.Capacity = desiredCapacity;
            bundle.Clip.MinimalPattern = new[] { bundle.Codex };

            bundle.ClipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            bundle.ClipCodex.name = belt == BeltKind.AP
                ? $"clip_marder_35mm_{apAmmoType.ToLowerInvariant()}_{desiredCapacity}"
                : $"clip_marder_35mm_dm11a1_{desiredCapacity}";
            bundle.ClipCodex.ClipType = bundle.Clip;

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

            BundleCache[cacheKey] = bundle;
            return bundle;
        }

        private static bool EnsureAmmoBundles(string apAmmoType)
        {
            if (!ApAmmoParams.ContainsKey(apAmmoType))
            {
                MelonLogger.Warning($"[Marder 35mm] Unsupported AP ammo type '{apAmmoType}', falling back to DM33.");
                return EnsureAmmoBundles("DM33");
            }

            // 验证Donor弹药是否存在（30mm弹药）
            AmmoType donorAp = FindAmmoByCaliber(30f, AmmoType.AmmoCategory.Penetrator);
            AmmoType donorHe = FindAmmoByCaliber(30f, AmmoType.AmmoCategory.Explosive);

            if (donorAp != null && donorHe != null)
                return true;

            MelonLogger.Warning("[Marder 35mm] Failed to locate 30mm donor ammo.");
            return false;
        }

        private static AmmoType FindAmmoByCaliber(float caliber, AmmoType.AmmoCategory category)
        {
            AmmoCodexScriptable[] ammoCodices = Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>();
            if (ammoCodices == null || ammoCodices.Length == 0)
                return null;

            for (int i = 0; i < ammoCodices.Length; i++)
            {
                AmmoType ammo = ammoCodices[i]?.AmmoType;
                if (ammo == null || string.IsNullOrEmpty(ammo.Name))
                    continue;

                if (ammo.Caliber >= caliber - 1f && ammo.Caliber <= caliber + 1f && ammo.Category == category)
                    return ammo;
            }

            return null;
        }

        private static AmmoType FindAmmoByName(string ammoName)
        {
            AmmoCodexScriptable[] ammoCodices = Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>();
            if (ammoCodices == null || ammoCodices.Length == 0)
                return null;

            for (int i = 0; i < ammoCodices.Length; i++)
            {
                AmmoType ammo = ammoCodices[i]?.AmmoType;
                if (ammo == null || string.IsNullOrEmpty(ammo.Name))
                    continue;

                if (string.Equals(ammo.Name, ammoName, StringComparison.OrdinalIgnoreCase))
                    return ammo;
            }

            return null;
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
            return bundle != null
                && bundle.Ammo != null
                && bundle.Codex != null
                && bundle.Clip != null
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

            // 35mm判断范围：15-40mm
            return ammo.Caliber >= 15f && ammo.Caliber <= 40f;
        }
    }
}