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

        internal static Vehicle LoadFirstVanillaVehicle(params string[] names)
        {
            if (names == null)
                return null;

            for (int i = 0; i < names.Length; i++)
            {
                string candidate = names[i];
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                Vehicle vehicle = LoadVanillaVehicle(candidate);
                if (vehicle != null)
                    return vehicle;
            }

            return null;
        }

        internal static Vehicle PrewarmVanillaVehicle(string logLabel, string[] candidateNames, string[] probePaths = null)
        {
            if (candidateNames == null || candidateNames.Length == 0)
                return null;

            string cacheKey = string.Join("|", candidateNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray());
            Vehicle vehicle = LoadFirstVanillaVehicle(candidateNames);
            if (vehicle == null)
            {
                string label = string.IsNullOrWhiteSpace(logLabel) ? "unknown" : logLabel;
                MelonLoader.MelonLogger.Warning($"[Assets] Vanilla donor prewarm miss: {label} | candidates=[{string.Join(", ", candidateNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray())}]");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(cacheKey) && prewarmedVehicleKeys.Contains(cacheKey))
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

                if (!string.IsNullOrWhiteSpace(cacheKey))
                    prewarmedVehicleKeys.Add(cacheKey);

#if DEBUG
                string label = string.IsNullOrWhiteSpace(logLabel) ? vehicle.name : logLabel;
                UnderdogsDebug.Log($"[Assets] Prewarmed vanilla donor: {label} | vehicle={vehicle.name} transforms={(transforms != null ? transforms.Length : 0)} optics={(optics != null ? optics.Length : 0)} reticles={(meshes != null ? meshes.Length : 0)} probes={resolvedProbes}/{(probePaths != null ? probePaths.Length : 0)}");
#endif
            }
            catch (Exception ex)
            {
                string label = string.IsNullOrWhiteSpace(logLabel) ? vehicle.name : logLabel;
                MelonLoader.MelonLogger.Warning($"[Assets] Vanilla donor prewarm failed: {label} | {ex.Message}");
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
