using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using GHPC.Weaponry;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public sealed class UEAlreadyConverted : MonoBehaviour
    {
        private void Awake()
        {
            enabled = false;
        }
    }

    internal sealed class LaserPointCorrection : MonoBehaviour
    {
        internal Transform laser;
        internal UsableOptic day_optic;
        internal UsableOptic night_optic;

        private Vector3 preserved_spot_day;
        private Quaternion preserved_rotation_day;
        private Vector3 preserved_spot_night;
        private Quaternion preserved_rotation_night;
        private bool initialized;

        private void Start()
        {
            RefreshBindings();
        }

        internal void RefreshBindings()
        {
            initialized = false;

            if (laser == null || day_optic == null || night_optic == null)
            {
                enabled = false;
                return;
            }

            laser.SetParent(day_optic.transform, true);
            preserved_spot_day = laser.localPosition;
            preserved_rotation_day = laser.localRotation;

            laser.SetParent(night_optic.transform, true);
            preserved_spot_night = laser.localPosition;
            preserved_rotation_night = laser.localRotation;

            ApplyToOptic(day_optic, preserved_spot_day, preserved_rotation_day);

            initialized = true;
            enabled = true;
        }

        private void LateUpdate()
        {
            if (!initialized || laser == null)
                return;

            UsableOptic activeOptic = GetActiveOptic();
            if (activeOptic == day_optic)
            {
                ApplyToOptic(day_optic, preserved_spot_day, preserved_rotation_day);
            }
            else if (activeOptic == night_optic)
            {
                ApplyToOptic(night_optic, preserved_spot_night, preserved_rotation_night);
            }
        }

        private UsableOptic GetActiveOptic()
        {
            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            if (activeSlot != null)
            {
                if (day_optic != null && day_optic.slot == activeSlot)
                    return day_optic;

                if (night_optic != null && night_optic.slot == activeSlot)
                    return night_optic;
            }

            if (day_optic != null && day_optic.isActiveAndEnabled && (night_optic == null || !night_optic.isActiveAndEnabled))
                return day_optic;

            if (night_optic != null && night_optic.isActiveAndEnabled)
                return night_optic;

            return day_optic;
        }

        private void ApplyToOptic(UsableOptic optic, Vector3 localPosition, Quaternion localRotation)
        {
            if (optic == null || laser == null)
                return;

            if (laser.parent != optic.transform)
                laser.SetParent(optic.transform, true);

            laser.SetLocalPositionAndRotation(localPosition, localRotation);
        }
    }

    internal static class UECommonUtil
    {
        private static readonly string[] AmmoRackStoredClipsFieldNames = new string[]
        {
            "StoredClips",
            "<StoredClips>k__BackingField",
            "_storedClips",
            "storedClips"
        };
        private static readonly BindingFlags AmmoRackFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly MethodInfo m_emesSetDefaultScopeSpriteRendered = typeof(EMES18Optic).GetMethod("SetDefaultScopeSpriteRendered", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo m_ammoRackRemoveAmmoVisual = typeof(GHPC.Weapons.AmmoRack).GetMethod("RemoveAmmoVisualFromSlot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo m_feedSetNextClipType = typeof(AmmoFeed).GetMethod("SetNextClipType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo m_feedGetLoadedClipByType = typeof(AmmoFeed).GetMethod("GetLoadedClipByType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo m_loadoutRefreshSnapshot = typeof(LoadoutManager).GetMethod("RefreshSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_ammoRackStoredClips = typeof(GHPC.Weapons.AmmoRack).GetProperty("StoredClips", AmmoRackFlags);
        private static readonly FieldInfo f_loadoutTotalAmmoCount = typeof(LoadoutManager).GetField("_totalAmmoCount", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_loadoutTotalAmmoTypes = typeof(LoadoutManager).GetField("_totalAmmoTypes", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feedAmmoTypeInBreechBacking = typeof(AmmoFeed).GetField("<AmmoTypeInBreech>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feedLoadedClipTypeBacking = typeof(AmmoFeed).GetField("<LoadedClipType>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feedQueuedClipTypeBacking = typeof(AmmoFeed).GetField("<QueuedClipType>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feedQueuedClipTypeLockedIn = typeof(AmmoFeed).GetField("_queuedClipTypeLockedIn", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feedClipMain = typeof(AmmoFeed).GetField("_feedClipMain", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feedClipAux = typeof(AmmoFeed).GetField("_feedClipAux", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feedAuxFeedMode = typeof(AmmoFeed).GetField("_auxFeedMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_feedLoadedClip = typeof(AmmoFeed).GetProperty("LoadedClip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_mainGunAmmo = typeof(MainGun).GetField("Ammo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_mainGunReadyRack = typeof(MainGun).GetField("_readyRack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_mainGunAvailableAmmo = typeof(MainGun).GetField("AvailableAmmo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_mainGunAmmoCounts = typeof(MainGun).GetField("AmmoCounts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_mainGunAmmoIndexInBreech = typeof(MainGun).GetField("_ammoIndexInBreech", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_mainGunCurrentAmmoIndex = typeof(MainGun).GetField("_currentAmmoIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_mainGunNextAmmoIndex = typeof(MainGun).GetField("_nextAmmoIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_mainGunUseAmmoRacks = typeof(MainGun).GetProperty("UseAmmoRacks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo m_mainGunUpdateAmmoCounts = typeof(MainGun).GetMethod("UpdateAmmoCounts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo m_mainGunSelectAmmoType = typeof(MainGun).GetMethod("SelectAmmoType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo m_mainGunForceRoundToBreech = typeof(MainGun).GetMethod("ForceRoundToBreech", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        internal static readonly HashSet<string> MenuScenes = new HashSet<string>(StringComparer.Ordinal)
        {
            "MainMenu2_Scene",
            "MainMenu2-1_Scene",
            "LOADER_MENU",
            "LOADER_INITIAL",
            "t64_menu"
        };
        internal static readonly HashSet<string> PrimaryMenuScenes = new HashSet<string>(StringComparer.Ordinal)
        {
            "MainMenu2_Scene",
            "MainMenu2-1_Scene",
            "t64_menu"
        };

        internal static bool IsMenuScene(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName) && MenuScenes.Contains(sceneName);
        }

        internal static bool IsPrimaryMenuScene(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName) && PrimaryMenuScenes.Contains(sceneName);
        }

        internal static void ShallowCopy(object destination, object source)
        {
            if (destination == null || source == null)
                return;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Dictionary<string, FieldInfo> destinationFields = destination.GetType()
                .GetFields(flags)
                .Where(field => !field.IsLiteral && !field.IsInitOnly)
                .ToDictionary(field => field.Name, field => field, StringComparer.Ordinal);

            foreach (FieldInfo sourceField in source.GetType().GetFields(flags))
            {
                if (sourceField.IsLiteral) continue;
                if (!destinationFields.TryGetValue(sourceField.Name, out FieldInfo destinationField)) continue;
                if (destinationField.FieldType != sourceField.FieldType) continue;

                try
                {
                    destinationField.SetValue(destination, sourceField.GetValue(source));
                }
                catch
                {
                }
            }
        }

        internal static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            if (gameObject == null) return null;

            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        internal static T[] AppendToArray<T>(T[] array, T newItem)
        {
            if (array == null)
                return new[] { newItem };

            T[] output = new T[array.Length + 1];
            Array.Copy(array, output, array.Length);
            output[array.Length] = newItem;
            return output;
        }

        internal static string DescribeAmmoFeedState(AmmoFeed feed)
        {
            if (feed == null)
                return "feed=null";

            string loadedClipFront = "null";
            int loadedClipCount = -1;

            try
            {
                PropertyInfo loadedClipProperty = typeof(AmmoFeed).GetProperty("LoadedClip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object loadedClip = loadedClipProperty != null ? loadedClipProperty.GetValue(feed, null) : null;
                if (loadedClip != null)
                {
                    PropertyInfo countProperty = loadedClip.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
                    loadedClipCount = countProperty != null ? (int)countProperty.GetValue(loadedClip, null) : -1;

                    if (loadedClipCount > 0)
                    {
                        MethodInfo peekMethod = loadedClip.GetType().GetMethod("Peek", BindingFlags.Instance | BindingFlags.Public);
                        AmmoType frontAmmo = peekMethod != null ? peekMethod.Invoke(loadedClip, null) as AmmoType : null;
                        loadedClipFront = frontAmmo?.Name ?? "null";
                    }
                    else if (loadedClipCount == 0)
                    {
                        loadedClipFront = "empty";
                    }
                }
            }
            catch { }

            return $"AmmoTypeInBreech={feed.AmmoTypeInBreech?.Name ?? "null"} | LoadedClipType={feed.LoadedClipType?.Name ?? "null"} | QueuedClipType={feed.QueuedClipType?.Name ?? "null"} | LoadedClip.Count={loadedClipCount} | LoadedClip.Front={loadedClipFront} | CurrentClipRemainingCount={feed.CurrentClipRemainingCount} | WaitingOnMissile={feed.WaitingOnMissile} | Reloading={feed.Reloading} | ForcePauseReload={feed.ForcePauseReload}";
        }

        internal static int FindLoadedAmmoClipIndex(LoadoutManager loadoutManager, Predicate<AmmoClipCodexScriptable> match)
        {
            if (loadoutManager?.LoadedAmmoList?.AmmoClips == null || match == null)
                return -1;

            for (int i = 0; i < loadoutManager.LoadedAmmoList.AmmoClips.Length; i++)
            {
                if (match(loadoutManager.LoadedAmmoList.AmmoClips[i]))
                    return i;
            }

            return -1;
        }

        internal static bool TryReplaceLoadedAmmoClip(LoadoutManager loadoutManager, Predicate<AmmoClipCodexScriptable> match, AmmoClipCodexScriptable replacementClipCodex, int? totalAmmoCount = null)
        {
            if (loadoutManager?.LoadedAmmoList?.AmmoClips == null || match == null || replacementClipCodex == null)
                return false;

            int index = FindLoadedAmmoClipIndex(loadoutManager, match);
            if (index < 0)
                return false;

            loadoutManager.LoadedAmmoList.AmmoClips[index] = replacementClipCodex;

            if (totalAmmoCount.HasValue && loadoutManager.TotalAmmoCounts != null && index < loadoutManager.TotalAmmoCounts.Length)
                loadoutManager.TotalAmmoCounts[index] = totalAmmoCount.Value;

            return true;
        }

        internal static LaserPointCorrection InstallLaserPointCorrection(FireControlSystem fcs, UsableOptic dayOptic, UsableOptic nightOptic)
        {
            if (fcs == null || fcs.LaserOrigin == null || dayOptic == null || nightOptic == null)
                return null;

            GameObject host = fcs.transform.parent != null ? fcs.transform.parent.gameObject : fcs.gameObject;
            LaserPointCorrection correction = GetOrAddComponent<LaserPointCorrection>(host);
            correction.laser = fcs.LaserOrigin;
            correction.day_optic = dayOptic;
            correction.night_optic = nightOptic;
            correction.RefreshBindings();
            return correction;
        }

        internal static void LinkSightSlots(CameraSlot daySlot, CameraSlot nightSlot)
        {
            if (daySlot == null || nightSlot == null)
                return;

            daySlot.LinkedNightSight = nightSlot;
            daySlot.NightSightAtNightOnly = false;
            nightSlot.LinkedDaySight = daySlot;
            nightSlot.IsLinkedNightSight = true;
            nightSlot.NightSightAtNightOnly = false;

            try { daySlot.RefreshAvailability(); } catch { }
            try { nightSlot.RefreshAvailability(); } catch { }
        }

        internal static bool TrySetDefaultScopeSpriteRendered(bool enabled)
        {
            try
            {
                if (m_emesSetDefaultScopeSpriteRendered == null)
                    return false;

                object result = m_emesSetDefaultScopeSpriteRendered.Invoke(null, new object[] { enabled });
                return result is bool boolResult && boolResult;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TrySetAmmoRackStoredClips(GHPC.Weapons.AmmoRack rack, List<AmmoType.AmmoClip> clips)
        {
            if (rack == null || clips == null)
                return false;

            try
            {
                rack.StoredClips = clips;
                return true;
            }
            catch { }

            try
            {
                if (p_ammoRackStoredClips != null)
                {
                    p_ammoRackStoredClips.SetValue(rack, clips, null);
                    return true;
                }
            }
            catch { }

            for (int i = 0; i < AmmoRackStoredClipsFieldNames.Length; i++)
            {
                try
                {
                    FieldInfo field = typeof(GHPC.Weapons.AmmoRack).GetField(AmmoRackStoredClipsFieldNames[i], AmmoRackFlags);
                    if (field == null)
                        continue;

                    field.SetValue(rack, clips);
                    return true;
                }
                catch { }
            }

            return false;
        }

        internal static void EmptyRack(GHPC.Weapons.AmmoRack rack)
        {
            if (rack == null)
                return;

            try
            {
                TrySetAmmoRackStoredClips(rack, new List<AmmoType.AmmoClip>());
            }
            catch { }

            try
            {
                rack.SlotIndicesByAmmoType = new Dictionary<AmmoType, List<byte>>();
            }
            catch { }

            try
            {
                IEnumerable<Transform> visualSlots = rack.VisualSlots;
                if (visualSlots == null)
                    return;

                foreach (Transform visualSlot in visualSlots)
                {
                    if (visualSlot == null)
                        continue;

                    AmmoStoredVisual visual = visualSlot.GetComponentInChildren<AmmoStoredVisual>();
                    if (visual == null || visual.AmmoType == null)
                        continue;

                    TryRemoveAmmoVisual(rack, visualSlot);
                }
            }
            catch { }
        }

        internal static void EmptyAllLoadoutRacks(LoadoutManager loadoutManager)
        {
            if (loadoutManager?.RackLoadouts == null)
                return;

            for (int i = 0; i < loadoutManager.RackLoadouts.Length; i++)
            {
                EmptyRack(loadoutManager.RackLoadouts[i]?.Rack);
            }
        }

        internal static void ReplaceReadyRack(GHPC.Weapons.AmmoRack rack, AmmoType.AmmoClip clipType, int storedCount)
        {
            if (rack == null || clipType == null || rack.ClipTypes == null || rack.ClipTypes.Length == 0)
                return;

            rack.ClipTypes[0] = clipType;

            List<AmmoType.AmmoClip> clips = new List<AmmoType.AmmoClip>();
            for (int i = 0; i < storedCount; i++)
                clips.Add(clipType);

            TrySetAmmoRackStoredClips(rack, clips);

            try
            {
                rack.RegenerateSlotIndices();
            }
            catch { }
        }

        internal static void RespawnLoadout(LoadoutManager loadoutManager)
        {
            if (loadoutManager == null)
                return;

            loadoutManager.SpawnCurrentLoadout();
        }

        internal static void RestoreLoadoutAmmoCounts(LoadoutManager loadoutManager, int[] totalAmmoCounts, int[][] rackAmmoCounts, bool refreshSnapshot = true)
        {
            if (loadoutManager == null)
                return;

            if (totalAmmoCounts != null)
            {
                if (loadoutManager.TotalAmmoCounts == null || loadoutManager.TotalAmmoCounts.Length != totalAmmoCounts.Length)
                    loadoutManager.TotalAmmoCounts = (int[])totalAmmoCounts.Clone();
                else
                    Array.Copy(totalAmmoCounts, loadoutManager.TotalAmmoCounts, totalAmmoCounts.Length);

                try
                {
                    if (f_loadoutTotalAmmoTypes != null)
                        f_loadoutTotalAmmoTypes.SetValue(loadoutManager, totalAmmoCounts.Length);
                }
                catch { }

                try
                {
                    if (f_loadoutTotalAmmoCount != null)
                        f_loadoutTotalAmmoCount.SetValue(loadoutManager, totalAmmoCounts.Sum());
                }
                catch { }
            }

            if (rackAmmoCounts != null && loadoutManager.RackLoadouts != null)
            {
                int rackCount = Math.Min(loadoutManager.RackLoadouts.Length, rackAmmoCounts.Length);
                for (int i = 0; i < rackCount; i++)
                {
                    if (loadoutManager.RackLoadouts[i] == null || rackAmmoCounts[i] == null)
                        continue;

                    loadoutManager.RackLoadouts[i].AmmoCounts = (int[])rackAmmoCounts[i].Clone();
                }
            }

            if (!refreshSnapshot)
                return;

            try
            {
                m_loadoutRefreshSnapshot?.Invoke(loadoutManager, null);
            }
            catch { }
        }

        internal static void RefreshLauncherFeed(AmmoFeed feed)
        {
            if (feed == null)
                return;

            try { feed.AmmoTypeInBreech = null; } catch { }
            try { feed.Start(); } catch { }
        }

        internal static void ClearAmmoInBreech(AmmoFeed feed)
        {
            if (feed == null)
                return;

            try { feed.AmmoTypeInBreech = null; } catch { }
        }

        internal static void RestartFeed(AmmoFeed feed)
        {
            if (feed == null)
                return;

            try { feed.Start(); } catch { }
        }

        internal static void SyncAutocannonFeed(WeaponSystem weapon, AmmoType.AmmoClip[] clipTypes, AmmoType[] ammoTypes, int[] ammoCounts, int selectedIndex)
        {
            AmmoFeed feed = weapon?.Feed;
            if (feed == null || clipTypes == null || ammoTypes == null || ammoCounts == null)
                return;

            for (int ammoIndex = 0; ammoIndex < ammoCounts.Length && ammoIndex < clipTypes.Length; ammoIndex++)
            {
                object queue = GetLoadedClipQueueByType(feed, clipTypes[ammoIndex]);
                PopulateQueue(queue, ammoIndex < ammoTypes.Length ? ammoTypes[ammoIndex] : null, clipTypes[ammoIndex], Math.Max(0, ammoCounts[ammoIndex]));
            }

            object mainQueue = f_feedClipMain != null ? f_feedClipMain.GetValue(feed) : null;
            object auxQueue = f_feedClipAux != null ? f_feedClipAux.GetValue(feed) : null;
            PopulateQueue(mainQueue, ammoTypes.Length > 0 ? ammoTypes[0] : null, clipTypes.Length > 0 ? clipTypes[0] : null, ammoCounts.Length > 0 ? Math.Max(0, ammoCounts[0]) : 0);
            PopulateQueue(auxQueue, ammoTypes.Length > 1 ? ammoTypes[1] : null, clipTypes.Length > 1 ? clipTypes[1] : null, ammoCounts.Length > 1 ? Math.Max(0, ammoCounts[1]) : 0);

            if (selectedIndex < 0 || selectedIndex >= clipTypes.Length)
                selectedIndex = 0;

            AmmoType.AmmoClip selectedClip = clipTypes[selectedIndex];
            AmmoType selectedAmmo = selectedIndex < ammoTypes.Length ? ammoTypes[selectedIndex] : GetAmmoTypeFromClip(selectedClip);

            try
            {
                if (f_feedAuxFeedMode != null)
                    f_feedAuxFeedMode.SetValue(feed, selectedIndex > 0);
            }
            catch { }

            try
            {
                if (selectedClip != null)
                    feed.SetNextClipType(selectedClip);
            }
            catch
            {
                try
                {
                    if (m_feedSetNextClipType != null && selectedClip != null)
                        m_feedSetNextClipType.Invoke(feed, new object[] { selectedClip });
                }
                catch { }
            }

            PopulateQueue(GetLoadedClipObject(feed), selectedAmmo, selectedClip, selectedIndex < ammoCounts.Length ? Math.Max(0, ammoCounts[selectedIndex]) : 0);

            try { feed.LoadedClipType = selectedClip; } catch { }
            try { feed.QueuedClipType = selectedClip; } catch { }
            try { feed.AmmoTypeInBreech = selectedAmmo; } catch { }
            try { feed.Reloading = false; } catch { }
            try { feed.ForcePauseReload = false; } catch { }
            try { if (f_feedLoadedClipTypeBacking != null) f_feedLoadedClipTypeBacking.SetValue(feed, selectedClip); } catch { }
            try { if (f_feedQueuedClipTypeBacking != null) f_feedQueuedClipTypeBacking.SetValue(feed, selectedClip); } catch { }
            try { if (f_feedQueuedClipTypeLockedIn != null) f_feedQueuedClipTypeLockedIn.SetValue(feed, selectedClip); } catch { }
            try { if (f_feedAmmoTypeInBreechBacking != null) f_feedAmmoTypeInBreechBacking.SetValue(feed, selectedAmmo); } catch { }
        }

        internal static void SyncLegacyMainGun(WeaponSystem weapon, AmmoCodexScriptable[] ammoCodexes, AmmoType[] ammoTypes, int[] ammoCounts, int selectedIndex)
        {
            if (weapon == null || ammoCodexes == null || ammoTypes == null || ammoCounts == null)
                return;

            MainGun mainGun = FindLegacyMainGun(weapon);
            if (mainGun == null)
                return;

            try { p_mainGunUseAmmoRacks?.SetValue(mainGun, true, null); } catch { }
            try { f_mainGunAmmo?.SetValue(mainGun, ammoCodexes); } catch { }
            try { f_mainGunAvailableAmmo?.SetValue(mainGun, ammoTypes); } catch { }
            try { f_mainGunAmmoCounts?.SetValue(mainGun, (int[])ammoCounts.Clone()); } catch { }
            try { m_mainGunUpdateAmmoCounts?.Invoke(mainGun, null); } catch { }

            if (selectedIndex < 0 || selectedIndex >= ammoCounts.Length)
                selectedIndex = 0;

            try { f_mainGunCurrentAmmoIndex?.SetValue(mainGun, selectedIndex); } catch { }
            try { f_mainGunNextAmmoIndex?.SetValue(mainGun, selectedIndex); } catch { }
            try { m_mainGunSelectAmmoType?.Invoke(mainGun, new object[] { selectedIndex }); } catch { }

            if (selectedIndex >= 0 && selectedIndex < ammoCounts.Length && ammoCounts[selectedIndex] > 0)
            {
                try { f_mainGunAmmoIndexInBreech?.SetValue(mainGun, selectedIndex); } catch { }
                try { m_mainGunForceRoundToBreech?.Invoke(mainGun, new object[] { selectedIndex }); } catch { }
            }
            else
            {
                try { f_mainGunAmmoIndexInBreech?.SetValue(mainGun, -1); } catch { }
            }

            try { m_mainGunUpdateAmmoCounts?.Invoke(mainGun, null); } catch { }
        }
        internal static void ResetFeedForClip(AmmoFeed feed, AmmoType ammoType, AmmoType.AmmoClip clipType, bool startFeed, bool queueClipType = false)
        {
            if (feed == null)
                return;

            TryAssignFeedState("AmmoTypeInBreech", () => feed.AmmoTypeInBreech = ammoType, () => feed.AmmoTypeInBreech?.Name ?? "null");
            TryAssignFeedState("LoadedClipType", () => feed.LoadedClipType = clipType, () => feed.LoadedClipType?.Name ?? "null");
            TryAssignFeedState("QueuedClipType", () => feed.QueuedClipType = queueClipType ? clipType : null, () => feed.QueuedClipType?.Name ?? "null");

            try
            {
                if (queueClipType && clipType != null)
                    feed.SetNextClipType(clipType);
            }
            catch
            {
                try
                {
                    if (queueClipType && m_feedSetNextClipType != null && clipType != null)
                        m_feedSetNextClipType.Invoke(feed, new object[] { clipType });
                }
                catch { }
            }

            TryAssignFeedState("Reloading", () => feed.Reloading = false, () => feed.Reloading.ToString());
            TryAssignFeedState("ForcePauseReload", () => feed.ForcePauseReload = false, () => feed.ForcePauseReload.ToString());

            if (!startFeed)
                return;

            try { feed.Start(); } catch { }
        }

        internal static void SyncWeaponCurrentAmmo(WeaponSystem weaponSystem, AmmoType ammoType)
        {
            if (weaponSystem == null)
                return;

            try { weaponSystem.CurrentAmmoType = ammoType; } catch { }

            try
            {
                if (weaponSystem.FCS != null)
                    weaponSystem.FCS.CurrentAmmoType = ammoType;
            }
            catch { }
        }

        private static void TryAssignFeedState(string name, Action assign, Func<string> readBack)
        {
            try
            {
                assign();
            }
            catch { }
        }

        private static void TryRemoveAmmoVisual(GHPC.Weapons.AmmoRack rack, Transform visualSlot)
        {
            if (rack == null || visualSlot == null)
                return;

            try
            {
                rack.RemoveAmmoVisualFromSlot(visualSlot);
                return;
            }
            catch { }

            try
            {
                if (m_ammoRackRemoveAmmoVisual != null)
                    m_ammoRackRemoveAmmoVisual.Invoke(rack, new object[] { visualSlot });
            }
            catch { }
        }

        private static object GetLoadedClipQueueByType(AmmoFeed feed, AmmoType.AmmoClip clipType)
        {
            try
            {
                return m_feedGetLoadedClipByType != null ? m_feedGetLoadedClipByType.Invoke(feed, new object[] { clipType }) : null;
            }
            catch
            {
                return null;
            }
        }

        private static object GetLoadedClipObject(AmmoFeed feed)
        {
            try
            {
                return p_feedLoadedClip != null ? p_feedLoadedClip.GetValue(feed, null) : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool PopulateQueue(object queue, AmmoType ammoType, AmmoType.AmmoClip clipType, int count)
        {
            try
            {
                if (queue == null)
                    return false;

                Type queueType = queue.GetType();
                MethodInfo clearMethod = queueType.GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public);
                MethodInfo enqueueMethod = queueType.GetMethod("Enqueue", BindingFlags.Instance | BindingFlags.Public);
                Type itemType = queueType.IsGenericType ? queueType.GetGenericArguments()[0] : null;
                if (clearMethod == null || enqueueMethod == null || itemType == null)
                    return false;

                clearMethod.Invoke(queue, null);

                object queueItem = null;
                if (ammoType != null && itemType.IsInstanceOfType(ammoType))
                    queueItem = ammoType;
                else if (clipType != null && itemType.IsInstanceOfType(clipType))
                    queueItem = clipType;
                else
                {
                    AmmoCodexScriptable ammoCodex = clipType?.MinimalPattern != null && clipType.MinimalPattern.Length > 0
                        ? clipType.MinimalPattern[0]
                        : null;
                    if (ammoCodex != null && itemType.IsInstanceOfType(ammoCodex))
                        queueItem = ammoCodex;
                }

                if (queueItem == null)
                    return count == 0;

                for (int i = 0; i < count; i++)
                    enqueueMethod.Invoke(queue, new object[] { queueItem });

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static AmmoType GetAmmoTypeFromClip(AmmoType.AmmoClip clip)
        {
            return clip?.MinimalPattern != null && clip.MinimalPattern.Length > 0
                ? clip.MinimalPattern[0]?.AmmoType
                : null;
        }

        private static MainGun FindLegacyMainGun(WeaponSystem weapon)
        {
            if (weapon == null)
                return null;

            GHPC.Weapons.AmmoRack readyRack = weapon.Feed != null ? weapon.Feed.ReadyRack : null;
            MainGun fallback = null;
            MainGun[] candidates = weapon.transform.root != null
                ? weapon.transform.root.GetComponentsInChildren<MainGun>(true)
                : weapon.GetComponentsInChildren<MainGun>(true);

            if (candidates == null || candidates.Length == 0)
                return null;

            for (int i = 0; i < candidates.Length; i++)
            {
                MainGun candidate = candidates[i];
                if (candidate == null)
                    continue;

                if (fallback == null)
                    fallback = candidate;

                try
                {
                    GHPC.Weapons.AmmoRack candidateRack = f_mainGunReadyRack != null ? f_mainGunReadyRack.GetValue(candidate) as GHPC.Weapons.AmmoRack : null;
                    if (candidateRack != null && candidateRack == readyRack)
                        return candidate;
                }
                catch { }
            }

            return fallback;
        }

        /// <summary>
        /// 设置FLIR热成像着色器
        /// 将所有MeshRenderer的材质shader替换为Standard (FLIR)，并添加HeatSource组件
        /// </summary>
        /// <param name="parent">目标GameObject</param>
        /// <param name="heat">热源强度（默认0.55）</param>
        internal static void SetupFLIRShaders(GameObject parent, float heat = 0.55f)
        {
            if (parent == null) return;

            foreach (MeshRenderer mrend in parent.GetComponentsInChildren<MeshRenderer>(includeInactive: false))
            {
                if (mrend == null) continue;
                foreach (Material mat in mrend.materials)
                {
                    if (mat == null) continue;
                    Shader flirShader = Shader.Find("Standard (FLIR)");
                    if (flirShader != null)
                        mat.shader = flirShader;
                }
            }

            // 添加热源组件用于FLIR显示
            GHPC.Thermals.HeatSource src = parent.AddComponent<GHPC.Thermals.HeatSource>();
            src.heat = heat;
        }

    }
}
