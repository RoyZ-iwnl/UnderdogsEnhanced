using System;
using System.Collections.Generic;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal static class MarderSpikeAmmo
    {
        internal const string MISSILE_NAME = "Spike LR";
        internal static readonly string[] OriginalMissileNames = new string[]
        {
            "MILAN"
        };

        // 多管发射参数
        private const int SPIKE_CLIP_CAPACITY = 2;          // 待发导弹数量
        private const float SPIKE_RELOAD_TIME = 30f;        // 填装时间(秒)

        internal static AmmoType OriginalAmmo { get; private set; }
        internal static AmmoType SpikeAmmo { get; private set; }
        /// <summary>
        /// lr1: 260速 1000破 1.31当量 14kg重
        /// lr2: 180速 900破 1.27当量 13.5重
        /// </summary>
        private static AmmoType.AmmoClip spikeClip;
        private static AmmoCodexScriptable spikeCodex;
        private static AmmoClipCodexScriptable spikeClipCodex;
        private static WeaponSystemCodexScriptable spikeWeaponCodex;  // 武器定义
        private const float SpikeMaximumRange = 20000f;//最大距离
        private const float SpikeTurnSpeed = 0.2f;//转向速度（越大越灵活，但过大会导致过度修正和失速）
        private const float SpikeNoisePower = 2f;//噪声强度（增加弹道随机性，降低被动引导系统的命中率，但过大会导致过度偏离目标）
        private const float SpikeNoiseTimeScale = 2f;//噪声时间缩放（控制弹道随机变化的频率，较低值会产生更平滑的偏移，较高值会产生更频繁的偏移）
        private const float SpikeSpiralPower = 2.5f;//螺旋强度（增加弹道的螺旋运动，增加命中不确定性，但过大会导致过度偏离目标）
        private const float SpikeSpiralAngularRate = 1500f;//螺旋角速度（控制弹道螺旋运动的速度，较低值会产生更宽松的螺旋，较高值会产生更紧密的螺旋）
        private const float SpikeMuzzleVelocity = 180f;//发射速度（增加初始速度可以提高命中率和穿透力，但过大会导致过度修正和失速）
        private const float SpikeRangedFuseTime = 500f;//引信时间（增加引信时间可以允许导弹在更远距离引爆，但过大会导致近距离失效）
        private const float SpikePenetration = 900f;//破甲深度
        private const float SpikeTntEquivalent = 1.27f;//当量(kg TNT)
        private const float SpikeMass = 14f;// 导弹质量(kg)

        internal static bool IsSpikeAmmo(AmmoType ammo)
        {
            return ammo != null && IsSpikeAmmoName(ammo.Name);
        }

        internal static bool IsSpikeAmmoName(string ammoName)
        {
            return !string.IsNullOrEmpty(ammoName) && string.Equals(ammoName, MISSILE_NAME, StringComparison.Ordinal);
        }

        internal static bool IsOriginalMilanName(string ammoName)
        {
            if (string.IsNullOrEmpty(ammoName)) return false;
            for (int i = 0; i < OriginalMissileNames.Length; i++)
            {
                if (string.Equals(OriginalMissileNames[i], ammoName, StringComparison.Ordinal))
                    return true;
            }
            return ammoName.IndexOf("MILAN", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool EnsureApplied(WeaponSystem weaponSystem, Vehicle vehicle)
        {
            if (weaponSystem?.Feed?.ReadyRack?.ClipTypes == null || weaponSystem.Feed.ReadyRack.ClipTypes.Length == 0)
                return false;

            if (IsApplied(weaponSystem))
            {
                ApplyDebugFriendlyParams();
                return true;
            }

            if (SpikeAmmo == null)
                Init(weaponSystem);

            if (SpikeAmmo == null || spikeClip == null)
            {
                MelonLogger.Warning($"[Marder Spike] Spike ammo init failed on {vehicle?.FriendlyName ?? "unknown vehicle"}");
                return false;
            }

            try
            {
                GHPC.Weapons.AmmoRack rack = weaponSystem.Feed.ReadyRack;
                LoadoutManager loadoutManager = vehicle != null ? vehicle.GetComponent<LoadoutManager>() : null;

                int desiredStoredClips = rack.StoredClips != null ? rack.StoredClips.Count : 0;
                if (desiredStoredClips <= 0 && loadoutManager?.TotalAmmoCounts != null)
                {
                    int oldIndex = UECommonUtil.FindLoadedAmmoClipIndex(loadoutManager, clipCodex =>
                    {
                        AmmoType ammo = clipCodex?.ClipType?.MinimalPattern != null && clipCodex.ClipType.MinimalPattern.Length > 0
                            ? clipCodex.ClipType.MinimalPattern[0]?.AmmoType
                            : null;
                        return ammo != null && IsOriginalMilanName(ammo.Name);
                    });

                    if (oldIndex >= 0 && oldIndex < loadoutManager.TotalAmmoCounts.Length)
                        desiredStoredClips = System.Math.Max(desiredStoredClips, loadoutManager.TotalAmmoCounts[oldIndex]);
                }

                if (MarderMain.marder_spike_ready_count != null && MarderMain.marder_spike_ready_count.Value >= 0)
                {
                    // 用户设置的是导弹数量，需要转换成 clip 数量（向上取整）
                    int missileCount = Mathf.Clamp(MarderMain.marder_spike_ready_count.Value, 0, 64);
                    desiredStoredClips = Mathf.CeilToInt((float)missileCount / SPIKE_CLIP_CAPACITY);
                }

                int preloadClipCount = desiredStoredClips + 1;

                UECommonUtil.ReplaceReadyRack(rack, spikeClip, preloadClipCount);
                UECommonUtil.RefreshLauncherFeed(weaponSystem.Feed);

                // 修改武器名称
                if (spikeWeaponCodex != null)
                    weaponSystem.CodexEntry = spikeWeaponCodex;

                // 多管发射参数设置
                ApplyMultiBarrelSettings(weaponSystem);

                ApplyDebugFriendlyParams();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Marder Spike] EnsureApplied failed: {ex}");
            }

            return IsApplied(weaponSystem);
        }

        internal static bool IsApplied(WeaponSystem weaponSystem)
        {
            AmmoType ammo = weaponSystem?.Feed?.AmmoTypeInBreech;
            if (!IsSpikeAmmo(ammo))
            {
                AmmoType.AmmoClip clip = weaponSystem?.Feed?.ReadyRack?.ClipTypes != null && weaponSystem.Feed.ReadyRack.ClipTypes.Length > 0
                    ? weaponSystem.Feed.ReadyRack.ClipTypes[0]
                    : null;
                if (clip?.MinimalPattern != null && clip.MinimalPattern.Length > 0)
                    ammo = clip.MinimalPattern[0]?.AmmoType;
            }

            return IsSpikeAmmo(ammo);
        }

        private static void Init(WeaponSystem weaponSystem)
        {
            AmmoType original = ResolveOriginalAmmo(weaponSystem);
            if (original == null)
                return;

            OriginalAmmo = original;

            SpikeAmmo = new AmmoType();
            UECommonUtil.ShallowCopy(SpikeAmmo, original);
            SpikeAmmo.Name = MISSILE_NAME;
            SpikeAmmo.Guidance = AmmoType.GuidanceType.MCLOS;
            SpikeAmmo.Flight = AmmoType.FlightPattern.Direct;
            SpikeAmmo.MaximumRange = SpikeMaximumRange;
            SpikeAmmo.TurnSpeed = SpikeTurnSpeed;
            SpikeAmmo.NoisePowerX = SpikeNoisePower;
            SpikeAmmo.NoisePowerY = SpikeNoisePower;
            SpikeAmmo.NoiseTimeScale = SpikeNoiseTimeScale;
            SpikeAmmo.SpiralPower = SpikeSpiralPower;
            SpikeAmmo.SpiralAngularRate = SpikeSpiralAngularRate;
            SpikeAmmo.RangedFuseTime = SpikeRangedFuseTime;
            SpikeAmmo.MuzzleVelocity = Mathf.Max(original.MuzzleVelocity, SpikeMuzzleVelocity);
            SpikeAmmo.RhaPenetration = Mathf.Max(original.RhaPenetration, SpikePenetration);
            SpikeAmmo.TntEquivalentKg = Mathf.Max(original.TntEquivalentKg, SpikeTntEquivalent);
            SpikeAmmo.Mass = SpikeMass;
            SpikeAmmo.Tandem = true;
            SpikeAmmo.UseErrorCorrection = true;
            SpikeAmmo.GuidanceLeadDistance = 0f;
            SpikeAmmo.GuidanceLockoutTime = 0f;
            SpikeAmmo.GuidanceNoLockoutRange = 0f;
            SpikeAmmo.GuidanceNoLoiterRange = 0f;
            SpikeAmmo.CachedIndex = -1;

            spikeCodex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            spikeCodex.AmmoType = SpikeAmmo;
            spikeCodex.name = "ammo_spike_lr";

            AmmoType.AmmoClip originalClip = weaponSystem.Feed.ReadyRack.ClipTypes[0];
            spikeClip = new AmmoType.AmmoClip();
            UECommonUtil.ShallowCopy(spikeClip, originalClip);
            spikeClip.Name = MISSILE_NAME;
            spikeClip.Capacity = SPIKE_CLIP_CAPACITY;  // 待发导弹数量
            spikeClip.MinimalPattern = new AmmoCodexScriptable[] { spikeCodex };

            spikeClipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            spikeClipCodex.ClipType = spikeClip;
            spikeClipCodex.name = "clip_spike_lr";

            // 创建武器定义（只改名称）
            spikeWeaponCodex = ScriptableObject.CreateInstance<WeaponSystemCodexScriptable>();
            spikeWeaponCodex.name = "weapon_spike_vmls";
            spikeWeaponCodex.FriendlyName = "SPIKE VMLS";  // 新武器名称

#if DEBUG
            MelonLogger.Msg($"[Marder Spike] Init complete from {original.Name}");
#endif
        }

        private static AmmoType ResolveOriginalAmmo(WeaponSystem weaponSystem)
        {
            AmmoType ammo = weaponSystem?.Feed?.AmmoTypeInBreech;

            if (ammo == null)
            {
                AmmoType.AmmoClip clip = weaponSystem?.Feed?.ReadyRack?.ClipTypes != null && weaponSystem.Feed.ReadyRack.ClipTypes.Length > 0
                    ? weaponSystem.Feed.ReadyRack.ClipTypes[0]
                    : null;
                if (clip?.MinimalPattern != null && clip.MinimalPattern.Length > 0)
                    ammo = clip.MinimalPattern[0]?.AmmoType;
            }

            if (ammo == null)
            {
                foreach (AmmoCodexScriptable scriptable in Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>())
                {
                    if (scriptable?.AmmoType == null) continue;
                    if (IsOriginalMilanName(scriptable.AmmoType.Name))
                    {
                        ammo = scriptable.AmmoType;
                        break;
                    }
                }
            }

            return ammo;
        }

        private static void ApplyDebugFriendlyParams()
        {
            if (SpikeAmmo == null || OriginalAmmo == null) return;

            SpikeAmmo.MaximumRange = SpikeMaximumRange;
            SpikeAmmo.TurnSpeed = SpikeTurnSpeed;
            SpikeAmmo.NoisePowerX = SpikeNoisePower;
            SpikeAmmo.NoisePowerY = SpikeNoisePower;
            SpikeAmmo.NoiseTimeScale = SpikeNoiseTimeScale;
            SpikeAmmo.SpiralPower = SpikeSpiralPower;
            SpikeAmmo.SpiralAngularRate = SpikeSpiralAngularRate;
            SpikeAmmo.RangedFuseTime = SpikeRangedFuseTime;
            SpikeAmmo.MuzzleVelocity = Mathf.Max(OriginalAmmo.MuzzleVelocity, SpikeMuzzleVelocity);
            SpikeAmmo.RhaPenetration = Mathf.Max(OriginalAmmo.RhaPenetration, SpikePenetration);
            SpikeAmmo.TntEquivalentKg = Mathf.Max(OriginalAmmo.TntEquivalentKg, SpikeTntEquivalent);
            SpikeAmmo.Mass = SpikeMass;
        }

        /// <summary>
        /// 应用多管发射和填装时间设置
        /// </summary>
        private static void ApplyMultiBarrelSettings(WeaponSystem weaponSystem)
        {
            if (weaponSystem?.Feed == null) return;

            // 设置不能在引导导弹时发射
            weaponSystem.FireWhileGuidingMissile = false;

            AmmoFeed feed = weaponSystem.Feed;

            // 设置单发循环时间（发射后到下一发可用的时间）
            if (feed.RoundCycleStages != null && feed.RoundCycleStages.Length > 0)
            {
                feed.RoundCycleStages[0].Duration = SPIKE_RELOAD_TIME;
            }

            // 设置填装阶段（每个阶段 = 总时间 / clip容量）
            if (feed.ClipReloadStages != null && feed.ClipReloadStages.Length > 0)
            {
                float stageDuration = SPIKE_RELOAD_TIME / SPIKE_CLIP_CAPACITY;
                AmmoFeed.ReloadStage template = feed.ClipReloadStages[0];

                AmmoFeed.ReloadStage[] newStages = new AmmoFeed.ReloadStage[SPIKE_CLIP_CAPACITY];
                for (int i = 0; i < SPIKE_CLIP_CAPACITY; i++)
                {
                    newStages[i] = new AmmoFeed.ReloadStage();
                    UECommonUtil.ShallowCopy(newStages[i], template);
                    newStages[i].Duration = stageDuration;
                }
                feed.ClipReloadStages = newStages;
            }

            // 设置总填装时间
            feed._totalReloadTime = SPIKE_RELOAD_TIME;
        }

    }
}
