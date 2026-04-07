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

        internal static AmmoType OriginalAmmo { get; private set; }
        internal static AmmoType SpikeAmmo { get; private set; }

        private static AmmoType.AmmoClip spikeClip;
        private static AmmoCodexScriptable spikeCodex;
        private static AmmoClipCodexScriptable spikeClipCodex;
        private const float SpikeMaximumRange = 20000f;
        private const float SpikeTurnSpeed = 0.2f;
        private const float SpikeNoisePower = 2f;
        private const float SpikeNoiseTimeScale = 2f;
        private const float SpikeSpiralPower = 2.5f;
        private const float SpikeSpiralAngularRate = 1500f;
        private const float SpikeMuzzleVelocity = 120f;
        private const float SpikeRangedFuseTime = 500f;
        private const float SpikePenetration = 850f;
        private const float SpikeTntEquivalent = 1.7f;

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
                    desiredStoredClips = Mathf.Clamp(MarderMain.marder_spike_ready_count.Value, 0, 64);

                int preloadClipCount = desiredStoredClips + 1;

                UECommonUtil.ReplaceReadyRack(rack, spikeClip, preloadClipCount);
                UECommonUtil.RefreshLauncherFeed(weaponSystem.Feed);
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
            spikeClip.MinimalPattern = new AmmoCodexScriptable[] { spikeCodex };

            spikeClipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            spikeClipCodex.ClipType = spikeClip;
            spikeClipCodex.name = "clip_spike_lr";

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
        }

    }
}
