using System;
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
        private static readonly PropertyInfo p_ammoRackStoredClips = typeof(GHPC.Weapons.AmmoRack).GetProperty("StoredClips", AmmoRackFlags);
        internal static readonly HashSet<string> MenuScenes = new HashSet<string>(StringComparer.Ordinal)
        {
            "MainMenu2_Scene",
            "MainMenu2-1_Scene",
            "LOADER_MENU",
            "LOADER_INITIAL",
            "t64_menu"
        };

        internal static bool IsMenuScene(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName) && MenuScenes.Contains(sceneName);
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
