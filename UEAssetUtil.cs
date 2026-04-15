using System;
using System.Collections.Generic;
using System.Linq;
using GHPC.Equipment.Optics;
using GHPC.Mission;
using GHPC.Vehicle;
using Reticle;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace UnderdogsEnhanced
{
    internal static class UEAssetUtil
    {
        private static UnitPrefabLookupScriptable.UnitPrefabMetadata[] lookupAllUnits;
        private static readonly List<AssetReference> loadedAssetReferences = new List<AssetReference>();
        private static readonly HashSet<string> prewarmedVehicleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 预热原生载具资源，加载 prefab 并预热指定路径下的组件。
        /// </summary>
        /// <param name="name">载具注册名（UnitPrefabLookupScriptable 中的 metadata.Name，如 "MARDER1A2"、"M60A3TTS"、"M1IP"）</param>
        /// <param name="probePaths">要预热的子物体路径数组，用于预热特定组件（如光学、热成像等）</param>
        /// <returns>加载的 Vehicle 实例，已预热或 null</returns>
        /// <example>
        /// UEAssetUtil.PrewarmVanillaVehicle("MARDER1A2", new[] { "Marder1A1_rig/hull/turret/FLIR", "FLIR" });
        /// UEAssetUtil.PrewarmVanillaVehicle("M60A3TTS", new[] { "Turret Scripts/Sights/FLIR" });
        /// UEAssetUtil.LoadVanillaVehicle("M1IP"); // 仅加载不预热特定路径
        /// </example>
        internal static Vehicle PrewarmVanillaVehicle(string name, string[] probePaths = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            Vehicle vehicle = LoadVanillaVehicle(name);
            if (vehicle == null)
            {
#if DEBUG
                UnderdogsDebug.Log($"[Assets] Vanilla donor prewarm miss: {name}");
#endif
                return null;
            }

            string cacheKey = name;
            if (prewarmedVehicleKeys.Contains(cacheKey))
                return vehicle;

            try
            {
                Transform[] transforms = vehicle.GetComponentsInChildren<Transform>(true);
                UsableOptic[] optics = vehicle.GetComponentsInChildren<UsableOptic>(true);
                ReticleMesh[] meshes = vehicle.GetComponentsInChildren<ReticleMesh>(true);

                int resolvedProbes = 0;
                if (probePaths != null)
                {
                    for (int i = 0; i < probePaths.Length; i++)
                    {
                        string probePath = probePaths[i];
                        if (string.IsNullOrWhiteSpace(probePath))
                            continue;

                        Transform probe = vehicle.transform.Find(probePath);
                        if (probe == null)
                        {
                            probe = vehicle.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && string.Equals(t.name, probePath, StringComparison.OrdinalIgnoreCase));
                        }

                        if (probe != null)
                        {
                            resolvedProbes++;
                            probe.GetComponents<Component>();
                            probe.GetComponentsInChildren<Component>(true);
                        }
                    }
                }

                prewarmedVehicleKeys.Add(cacheKey);

#if DEBUG
                UnderdogsDebug.Log($"[Assets] Prewarmed vanilla donor: {name} | transforms={(transforms != null ? transforms.Length : 0)} optics={(optics != null ? optics.Length : 0)} reticles={(meshes != null ? meshes.Length : 0)} probes={resolvedProbes}/{(probePaths != null ? probePaths.Length : 0)}");
#endif
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[Assets] Vanilla donor prewarm failed: {name} | {ex.Message}");
            }

            return vehicle;
        }

        internal static Vehicle LoadVanillaVehicle(string name)
        {
            if (lookupAllUnits == null)
            {
                var lookup = Resources.FindObjectsOfTypeAll<UnitPrefabLookupScriptable>().FirstOrDefault();
                lookupAllUnits = lookup != null ? lookup.AllUnits : null;
            }

            if (lookupAllUnits == null)
                return null;

            var metadata = lookupAllUnits.FirstOrDefault(o => o != null && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            if (metadata == null || metadata.PrefabReference == null)
                return null;

            AssetReference prefabRef = metadata.PrefabReference;
            if (prefabRef.Asset == null)
            {
                if (!loadedAssetReferences.Contains(prefabRef))
                    loadedAssetReferences.Add(prefabRef);

                // 同步 Addressables 加载点。调用方必须保证场景状态稳定，
                // 不能放在退菜单这类敏感切换窗口里阻塞主线程。
                var loaded = prefabRef.LoadAssetAsync<GameObject>().WaitForCompletion();
                return loaded != null ? loaded.GetComponent<Vehicle>() : null;
            }

            return (prefabRef.Asset as GameObject)?.GetComponent<Vehicle>();
        }

        internal static void ReleaseVanillaAssets()
        {
            foreach (var prefabRef in loadedAssetReferences)
            {
                try { prefabRef.ReleaseAsset(); }
                catch { }
            }

            loadedAssetReferences.Clear();
            prewarmedVehicleKeys.Clear();
        }

        internal static GameObject CloneInactive(GameObject source, string cloneName = null)
        {
            if (source == null) return null;

            bool wasActive = source.activeSelf;
            source.SetActive(false);
            GameObject clone = UnityEngine.Object.Instantiate(source);
            source.SetActive(wasActive);

            if (!string.IsNullOrEmpty(cloneName))
                clone.name = cloneName;

            return clone;
        }
    }
}
