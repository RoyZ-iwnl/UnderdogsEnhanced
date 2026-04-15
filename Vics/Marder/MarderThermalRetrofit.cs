using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Thermals;
using GHPC.Vehicle;
using MelonLoader;
using Reticle;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace UnderdogsEnhanced
{
    internal static class MarderThermalRetrofit
    {
        private const string DonorVehicleName = "MARDER1A2";
        private const string DonorOpticPath = "Marder1A1_rig/hull/turret/FLIR";
        private const string A1PlusOpticPath = "Marder1A1_rig/hull/turret/BiV";
        private const string A1MinusOpticPath = "Marder1A1_rig/hull/turret/Infrarot-Zielfernrohr";
        private const string WideClonePrefix = "__UE_MARDER_A1_WFOV__";
        private const string PostCloneName = "__UE_MARDER_A1_FLIR_POST__";
        private static readonly string[] LegacyNightVisionNodeNames =
        {
            "NOD post volume",
            "FLIR night alt",
            "Scanline FOV change"
        };

        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo f_reticleMesh_reticle = typeof(ReticleMesh).GetField("reticle", InstanceFlags);
        private static readonly FieldInfo f_reticleMesh_smr = typeof(ReticleMesh).GetField("SMR", InstanceFlags);
        private static readonly FieldInfo f_snv_postVolume = typeof(SimpleNightVision).GetField("_postVolume", InstanceFlags);

        private sealed class ReticleClone
        {
            public ReticleSO Tree;
            public object Cached;
        }

        private sealed class ThermalDonor
        {
            public UsableOptic Optic;
            public CameraSlot Slot;
            public ReticleMesh NarrowMesh;
            public ReticleMesh WideMesh;
            public float DefaultFov;
            public float[] OtherFovs;
            public float WideFov;
            public float NarrowFov;
            public GameObject PostPrefab;
            public Material BlitMaterial;
            public bool DoMechanicalFlir;
            public int MechanicalScanWidth;
            public int MechanicalScanHeight;
        }

        private static ThermalDonor donor;

        internal static bool TryApply(Vehicle vehicle)
        {
            if (vehicle == null || !IsSupported(vehicle.FriendlyName))
                return false;

            UsableOptic targetOptic = ResolveTargetOptic(vehicle);
            CameraSlot targetSlot = targetOptic != null ? targetOptic.slot : null;
            if (targetOptic == null || targetSlot == null)
            {
                MelonLogger.Warning($"[Marder Thermal Retrofit] Target optic not found on {vehicle.FriendlyName}.");
                return false;
            }

            ThermalDonor thermalDonor = LoadDonor();
            if (thermalDonor == null)
            {
                MelonLogger.Warning($"[Marder Thermal Retrofit] Donor load failed; skipping {vehicle.FriendlyName}.");
                return false;
            }

            if (!ApplyThermalToOptic(vehicle, targetOptic, targetSlot, thermalDonor))
                return false;

            return true;
        }

        internal static bool IsSupported(string vehicleName)
        {
            return vehicleName == "Marder A1+" || vehicleName == "Marder A1-" || vehicleName == "Marder A1- (no ATGM)";
        }

        private static bool ApplyThermalToOptic(Vehicle vehicle, UsableOptic targetOptic, CameraSlot targetSlot, ThermalDonor thermalDonor)
        {
            try
            {
                PulseOpticInitialization(targetOptic);
                ClearThermalPresentationOverrides(targetOptic);
                RemoveExistingThermalPost(targetOptic.transform);
                CleanupLegacyNightVision(targetOptic.transform);

                ReticleMesh hostNarrowMesh = ResolveHostReticleMesh(targetOptic);
                if (hostNarrowMesh == null)
                {
                    MelonLogger.Warning($"[Marder Thermal Retrofit] Host reticle mesh missing on {vehicle.FriendlyName} ({targetOptic.name}).");
                    return false;
                }

                ReticleClone narrowClone = CloneReticleFromMesh(thermalDonor.NarrowMesh, thermalDonor.NarrowMesh.reticleSO != null ? thermalDonor.NarrowMesh.reticleSO.name + " (UE Marder A1)" : "UE Marder A1 NFOV");
                if (narrowClone == null)
                {
                    MelonLogger.Warning($"[Marder Thermal Retrofit] Donor NFOV clone failed on {vehicle.FriendlyName}.");
                    return false;
                }

                AssignReticleToMesh(hostNarrowMesh, narrowClone);

                ReticleMesh wideMesh = CloneDonorReticleMesh(thermalDonor.WideMesh, targetOptic.transform, WideClonePrefix + thermalDonor.WideMesh.name);
                if (wideMesh == null)
                {
                    MelonLogger.Warning($"[Marder Thermal Retrofit] Donor WFOV clone failed on {vehicle.FriendlyName}.");
                    return false;
                }

                targetOptic.reticleMesh = hostNarrowMesh;
                EnsureReticleMeshVisible(hostNarrowMesh);
                EnsureReticleMeshVisible(wideMesh);
                DisableOtherReticleMeshes(targetOptic, hostNarrowMesh, wideMesh);
                ApplyThermalFovItems(targetOptic, hostNarrowMesh, wideMesh, thermalDonor.WideFov, thermalDonor.NarrowFov);

                ConfigureThermalSlot(targetSlot, thermalDonor);
                SetupThermalPost(targetOptic.gameObject, targetSlot, thermalDonor.PostPrefab);

                if (!targetOptic.gameObject.activeSelf)
                    targetOptic.gameObject.SetActive(true);
                if (!targetOptic.enabled)
                    targetOptic.enabled = true;
                if (!targetSlot.enabled)
                    targetSlot.enabled = true;

                try { targetSlot.RefreshAvailability(); } catch { }
                try { targetSlot.ForceUpdateFov(); } catch { }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Marder Thermal Retrofit] Apply failed on {vehicle?.FriendlyName ?? "unknown"}: {ex.Message}");
                return false;
            }
        }

        private static void ConfigureThermalSlot(CameraSlot targetSlot, ThermalDonor thermalDonor)
        {
            if (targetSlot == null || thermalDonor == null || thermalDonor.Slot == null)
                return;

            targetSlot.VisionType = NightVisionType.Thermal;
            targetSlot.IsExterior = false;
            targetSlot.BaseBlur = thermalDonor.Slot.BaseBlur;
            targetSlot.VibrationBlurScale = thermalDonor.Slot.VibrationBlurScale;
            targetSlot.VibrationShakeMultiplier = thermalDonor.Slot.VibrationShakeMultiplier;
            targetSlot.VibrationPreBlur = thermalDonor.Slot.VibrationPreBlur;
            targetSlot.OverrideFLIRResolution = thermalDonor.Slot.OverrideFLIRResolution;
            targetSlot.FLIRWidth = thermalDonor.Slot.FLIRWidth;
            targetSlot.FLIRHeight = thermalDonor.Slot.FLIRHeight;
            targetSlot.CanToggleFlirPolarity = thermalDonor.Slot.CanToggleFlirPolarity;
            targetSlot.FLIRFilterMode = thermalDonor.Slot.FLIRFilterMode;
            targetSlot.SpriteType = thermalDonor.Slot.SpriteType;
            targetSlot.DefaultFov = thermalDonor.DefaultFov;
            targetSlot.OtherFovs = thermalDonor.OtherFovs != null ? (float[])thermalDonor.OtherFovs.Clone() : new float[0];
            targetSlot.DoMechanicalFLIR = thermalDonor.DoMechanicalFlir;
            targetSlot.MechanicalScanWidth = thermalDonor.MechanicalScanWidth;
            targetSlot.MechanicalScanHeight = thermalDonor.MechanicalScanHeight;
            targetSlot.NightSightAtNightOnly = false;
            try { targetSlot.WasUsingNightMode = true; } catch { }

            if (thermalDonor.BlitMaterial != null)
                targetSlot.FLIRBlitMaterialOverride = thermalDonor.BlitMaterial;
        }

        private static void SetupThermalPost(GameObject slotObject, CameraSlot slot, GameObject postPrefab)
        {
            if (slotObject == null || slot == null || postPrefab == null)
                return;

            ConfigureThermalCameraComponents(slotObject, slot);

            PostProcessVolume directVolume = slotObject.GetComponent<PostProcessVolume>();
            if (directVolume != null)
                UnityEngine.Object.Destroy(directVolume);

            GameObject post = UnityEngine.Object.Instantiate(postPrefab, slotObject.transform);
            post.name = PostCloneName;
            if (!post.activeSelf)
                post.SetActive(true);

            SetNamedChildrenActive(post.transform, "MainCam Volume", true);

            PostProcessVolume flirOnlyVolume = post.transform.Find("FLIR Only Volume")?.GetComponent<PostProcessVolume>();
            if (flirOnlyVolume != null)
            {
                flirOnlyVolume.enabled = true;
                flirOnlyVolume.weight = 1f;
                flirOnlyVolume.priority = 100f;
            }

            try { slot.WasUsingNightMode = true; } catch { }
        }

        private static void SetNamedChildrenActive(Transform root, string nodeName, bool active)
        {
            if (root == null || string.IsNullOrWhiteSpace(nodeName))
                return;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform node = transforms[i];
                if (node == null || !string.Equals(node.name, nodeName, StringComparison.OrdinalIgnoreCase))
                    continue;

                node.gameObject.SetActive(active);

                PostProcessVolume volume = node.GetComponent<PostProcessVolume>();
                if (volume != null)
                    volume.enabled = active;
            }
        }

        private static void ConfigureThermalCameraComponents(GameObject slotObject, CameraSlot slot)
        {
            if (slotObject == null || slot == null)
                return;

            SimpleNightVision[] nightVisionComponents = slotObject.GetComponentsInChildren<SimpleNightVision>(true);
            for (int i = 0; i < nightVisionComponents.Length; i++)
            {
                SimpleNightVision snv = nightVisionComponents[i];
                if (snv == null)
                    continue;

                try { f_snv_postVolume?.SetValue(snv, null); } catch { }
                UnityEngine.Object.Destroy(snv);
            }

            FLIRCamera[] flirCameras = slotObject.GetComponentsInChildren<FLIRCamera>(true);
            for (int i = 0; i < flirCameras.Length; i++)
            {
                FLIRCamera flirCamera = flirCameras[i];
                if (flirCamera == null)
                    continue;

                try { flirCamera.DoMechanicalScan = false; } catch { }
                try { flirCamera.MechanicalScanWidth = slot.FLIRWidth; } catch { }
                try { flirCamera.MechanicalScanHeight = slot.FLIRHeight; } catch { }
                try { flirCamera.enabled = true; } catch { }
            }
        }

        private static void RemoveExistingThermalPost(Transform opticRoot)
        {
            if (opticRoot == null)
                return;

            Transform[] children = opticRoot.Cast<Transform>().ToArray();
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null)
                    continue;

                string name = child.name ?? string.Empty;
                if (string.Equals(name, PostCloneName, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("FLIR Post Processing", StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static void CleanupLegacyNightVision(Transform opticRoot)
        {
            if (opticRoot == null)
                return;

            Transform[] transforms = opticRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform node = transforms[i];
                if (node == null)
                    continue;

                string name = node.name ?? string.Empty;
                bool destroyNode = false;
                for (int j = 0; j < LegacyNightVisionNodeNames.Length; j++)
                {
                    if (string.Equals(name, LegacyNightVisionNodeNames[j], StringComparison.OrdinalIgnoreCase))
                    {
                        destroyNode = true;
                        break;
                    }
                }

                if (!destroyNode)
                    continue;

                UnityEngine.Object.Destroy(node.gameObject);
            }
        }

        private static ThermalDonor LoadDonor()
        {
            if (donor != null)
                return donor;

            Vehicle donorVehicle = UEAssetUtil.PrewarmVanillaVehicle(DonorVehicleName, new[]
            {
                DonorOpticPath,
                "FLIR",
                "Marder1A1_rig/hull/turret/PERI Z11",
                "PERI Z11"
            });
            if (donorVehicle == null)
                return null;

            UsableOptic donorOptic = donorVehicle.transform.Find(DonorOpticPath)?.GetComponent<UsableOptic>();
            if (donorOptic == null)
            {
                donorOptic = donorVehicle.GetComponentsInChildren<UsableOptic>(true)
                    .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.name, "FLIR", StringComparison.OrdinalIgnoreCase));
            }

            CameraSlot donorSlot = donorOptic != null ? donorOptic.slot : null;
            if (donorOptic == null || donorSlot == null)
                return null;

            ReticleMesh narrowMesh = ResolveNamedReticleMesh(donorOptic, "Reticle Mesh", "NFOV");
            ReticleMesh wideMesh = ResolveNamedReticleMesh(donorOptic, "Reticle Mesh WFOV", "WFOV");
            if (narrowMesh == null)
                narrowMesh = donorOptic.reticleMesh;

            if (narrowMesh == null || wideMesh == null)
                return null;

            try { narrowMesh.Load(); } catch { }
            try { wideMesh.Load(); } catch { }

            donor = new ThermalDonor
            {
                Optic = donorOptic,
                Slot = donorSlot,
                NarrowMesh = narrowMesh,
                WideMesh = wideMesh,
                DefaultFov = donorSlot.DefaultFov > 0.1f ? donorSlot.DefaultFov : 12f,
                OtherFovs = donorSlot.OtherFovs != null ? (float[])donorSlot.OtherFovs.Clone() : new float[0],
                WideFov = ResolveWideFov(donorSlot),
                NarrowFov = ResolveNarrowFov(donorSlot),
                PostPrefab = ResolveDonorPostPrefab(donorOptic),
                BlitMaterial = donorSlot.FLIRBlitMaterialOverride != null ? donorSlot.FLIRBlitMaterialOverride : UEResourceController.GetThermalFlirBlitMaterial(),
                DoMechanicalFlir = donorSlot.DoMechanicalFLIR,
                MechanicalScanWidth = donorSlot.MechanicalScanWidth,
                MechanicalScanHeight = donorSlot.MechanicalScanHeight
            };

            if (donor.PostPrefab == null)
                donor.PostPrefab = UEResourceController.GetThermalFlirPostPrefab();

            return donor;
        }

        private static float ResolveWideFov(CameraSlot slot)
        {
            if (slot == null)
                return 12f;

            List<float> fovs = new List<float>();
            if (slot.DefaultFov > 0.1f)
                fovs.Add(slot.DefaultFov);

            if (slot.OtherFovs != null)
            {
                for (int i = 0; i < slot.OtherFovs.Length; i++)
                {
                    float fov = slot.OtherFovs[i];
                    if (fov > 0.1f)
                        fovs.Add(fov);
                }
            }

            return fovs.Count > 0 ? fovs.Max() : 12f;
        }

        private static float ResolveNarrowFov(CameraSlot slot)
        {
            if (slot == null)
                return 4f;

            List<float> fovs = new List<float>();
            if (slot.DefaultFov > 0.1f)
                fovs.Add(slot.DefaultFov);

            if (slot.OtherFovs != null)
            {
                for (int i = 0; i < slot.OtherFovs.Length; i++)
                {
                    float fov = slot.OtherFovs[i];
                    if (fov > 0.1f)
                        fovs.Add(fov);
                }
            }

            return fovs.Count > 0 ? fovs.Min() : 4f;
        }

        private static GameObject ResolveDonorPostPrefab(UsableOptic donorOptic)
        {
            if (donorOptic == null)
                return null;

            Transform direct = donorOptic.transform.Find("FLIR Post Processing - Green");
            if (direct != null)
                return direct.gameObject;

            return donorOptic.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate != null && (candidate.name ?? string.Empty).StartsWith("FLIR Post Processing", StringComparison.OrdinalIgnoreCase))
                ?.gameObject;
        }

        private static UsableOptic ResolveTargetOptic(Vehicle vehicle)
        {
            if (vehicle == null)
                return null;

            string targetPath = vehicle.FriendlyName == "Marder A1+" ? A1PlusOpticPath : A1MinusOpticPath;
            string targetName = vehicle.FriendlyName == "Marder A1+" ? "BiV" : "Infrarot-Zielfernrohr";

            UsableOptic direct = vehicle.transform.Find(targetPath)?.GetComponent<UsableOptic>();
            if (direct != null && direct.slot != null)
                return direct;

            return vehicle.GetComponentsInChildren<UsableOptic>(true)
                .FirstOrDefault(optic =>
                {
                    if (optic == null || optic.slot == null)
                        return false;

                    if (string.Equals(optic.name, targetName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    string path = GetTransformPath(optic.transform);
                    return path.EndsWith(targetPath, StringComparison.OrdinalIgnoreCase);
                });
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            List<string> names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static void PulseOpticInitialization(UsableOptic optic)
        {
            if (optic == null)
                return;

            bool originalActive = optic.gameObject.activeSelf;
            try { optic.gameObject.SetActive(true); } catch { }
            try { optic.gameObject.SetActive(false); } catch { }
            try { optic.gameObject.SetActive(originalActive); } catch { }
        }

        private static void ClearThermalPresentationOverrides(UsableOptic optic)
        {
            if (optic == null)
                return;

            SetOpticPresentationItems(optic, new UsableOptic.FovLimitedItem[0], new ReticleMesh[0]);
            TrySetMember(optic, "_reticleMeshLocalPositions", new Vector2[] { Vector2.zero, Vector2.zero });

            Transform[] children = optic.transform.Cast<Transform>().ToArray();
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null)
                    continue;

                if ((child.name ?? string.Empty).StartsWith(WideClonePrefix, StringComparison.OrdinalIgnoreCase))
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static void ApplyThermalFovItems(UsableOptic optic, ReticleMesh narrowMesh, ReticleMesh wideMesh, float wideFov, float narrowFov)
        {
            if (optic == null || narrowMesh == null || wideMesh == null)
                return;

            float resolvedWideFov = wideFov > 0.1f ? wideFov : 12f;
            float resolvedNarrowFov = narrowFov > 0.1f ? narrowFov : 4f;
            if (resolvedWideFov < resolvedNarrowFov)
            {
                float swap = resolvedWideFov;
                resolvedWideFov = resolvedNarrowFov;
                resolvedNarrowFov = swap;
            }

            float threshold = (resolvedWideFov + resolvedNarrowFov) * 0.5f;
            UsableOptic.FovLimitedItem wideLim = CreateFovLimitedItem(new Vector2(threshold, 360f), new[] { wideMesh.gameObject });
            UsableOptic.FovLimitedItem narrowLim = CreateFovLimitedItem(new Vector2(0f, threshold), new[] { narrowMesh.gameObject });

            TrySetMember(optic, "_reticleMeshLocalPositions", new Vector2[] { Vector2.zero, Vector2.zero });
            SetOpticPresentationItems(optic, new[] { wideLim, narrowLim }, new[] { wideMesh });
        }

        private static void SetOpticPresentationItems(UsableOptic optic, UsableOptic.FovLimitedItem[] fovLimitedItems, ReticleMesh[] additionalReticleMeshes)
        {
            if (optic == null)
                return;

            TrySetMember(optic, "FovLimitedItems", fovLimitedItems);
            TrySetMember(optic, "<FovLimitedItems>k__BackingField", fovLimitedItems);
            TrySetMember(optic, "AdditionalReticleMeshes", additionalReticleMeshes);
            TrySetMember(optic, "<AdditionalReticleMeshes>k__BackingField", additionalReticleMeshes);
        }

        private static void TrySetMember(object target, string memberName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return;

            try
            {
                FieldInfo field = target.GetType().GetField(memberName, InstanceFlags);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                PropertyInfo property = target.GetType().GetProperty(memberName, InstanceFlags);
                if (property != null && property.CanWrite)
                    property.SetValue(target, value, null);
            }
            catch { }
        }

        private static UsableOptic.FovLimitedItem CreateFovLimitedItem(Vector2 fovRange, GameObject[] exclusiveObjects)
        {
            object boxed = FormatterServices.GetUninitializedObject(typeof(UsableOptic.FovLimitedItem));
            TrySetMember(boxed, "FovRange", fovRange);
            TrySetMember(boxed, "ExclusiveObjects", exclusiveObjects);
            TrySetMember(boxed, "<FovRange>k__BackingField", fovRange);
            TrySetMember(boxed, "<ExclusiveObjects>k__BackingField", exclusiveObjects);
            return (UsableOptic.FovLimitedItem)boxed;
        }

        private static object CloneCachedReticleObject(object source, ReticleSO treeClone)
        {
            if (source == null || treeClone == null)
                return null;

            Type cachedType = source.GetType();
            object cachedClone = Activator.CreateInstance(cachedType);
            UECommonUtil.ShallowCopy(cachedClone, source);

            FieldInfo treeField = cachedType.GetField("tree", InstanceFlags);
            FieldInfo meshField = cachedType.GetField("mesh", InstanceFlags);
            treeField?.SetValue(cachedClone, treeClone);
            meshField?.SetValue(cachedClone, null);
            return cachedClone;
        }

        private static ReticleClone CloneReticleFromMesh(ReticleMesh sourceMesh, string cloneName)
        {
            if (sourceMesh == null || sourceMesh.reticleSO == null)
                return null;

            ReticleSO treeClone = ScriptableObject.Instantiate(sourceMesh.reticleSO);
            if (!string.IsNullOrWhiteSpace(cloneName))
                treeClone.name = cloneName;

            object sourceCached = null;
            try { sourceCached = f_reticleMesh_reticle?.GetValue(sourceMesh); } catch { }

            return new ReticleClone
            {
                Tree = treeClone,
                Cached = CloneCachedReticleObject(sourceCached, treeClone)
            };
        }

        private static void AssignReticleToMesh(ReticleMesh targetMesh, ReticleClone clone)
        {
            if (targetMesh == null || clone == null || clone.Tree == null)
                return;

            targetMesh.reticleSO = clone.Tree;
            try { f_reticleMesh_reticle?.SetValue(targetMesh, clone.Cached); } catch { }
            try { f_reticleMesh_smr?.SetValue(targetMesh, null); } catch { }
            targetMesh.Load();
        }

        private static ReticleMesh CloneDonorReticleMesh(ReticleMesh donorMesh, Transform parent, string cloneName)
        {
            if (donorMesh == null || parent == null)
                return null;

            GameObject cloneObject = UnityEngine.Object.Instantiate(donorMesh.gameObject, parent);
            cloneObject.name = cloneName;
            ReticleMesh cloneMesh = cloneObject.GetComponent<ReticleMesh>();
            if (cloneMesh == null)
                return null;

            ReticleClone clone = CloneReticleFromMesh(donorMesh, donorMesh.reticleSO != null ? donorMesh.reticleSO.name + " (UE Marder A1)" : cloneName);
            if (clone != null)
                AssignReticleToMesh(cloneMesh, clone);

            return cloneMesh;
        }

        private static ReticleMesh ResolveNamedReticleMesh(UsableOptic optic, params string[] names)
        {
            if (optic == null)
                return null;

            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    string candidate = names[i];
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;

                    Transform match = optic.transform.Find(candidate);
                    if (match == null)
                        continue;

                    ReticleMesh directMesh = match.GetComponent<ReticleMesh>();
                    if (directMesh != null)
                        return directMesh;
                }
            }

            return optic.GetComponentsInChildren<ReticleMesh>(true)
                .FirstOrDefault(mesh => mesh != null && names != null && names.Any(name => !string.IsNullOrWhiteSpace(name) && (mesh.name ?? string.Empty).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static ReticleMesh ResolveHostReticleMesh(UsableOptic optic)
        {
            if (optic == null)
                return null;

            try
            {
                if (optic.reticleMesh != null && !IsRangefindingReticleMesh(optic.reticleMesh))
                    return optic.reticleMesh;
            }
            catch { }

            Transform direct = optic.transform.Find("Reticle Mesh");
            if (direct != null)
            {
                ReticleMesh directMesh = direct.GetComponent<ReticleMesh>();
                if (directMesh != null && !IsRangefindingReticleMesh(directMesh))
                    return directMesh;
            }

            return optic.GetComponentsInChildren<ReticleMesh>(true)
                .FirstOrDefault(mesh => mesh != null && !IsRangefindingReticleMesh(mesh) &&
                    (((mesh.name ?? string.Empty).IndexOf("Reticle Mesh", StringComparison.OrdinalIgnoreCase) >= 0) || mesh.gameObject.activeSelf));
        }

        private static bool IsRangefindingReticleMesh(ReticleMesh mesh)
        {
            if (mesh == null)
                return false;

            string name = mesh.name ?? string.Empty;
            return name.IndexOf("rangefinding", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("range finder", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DisableOtherReticleMeshes(UsableOptic optic, params ReticleMesh[] keepMeshes)
        {
            if (optic == null)
                return;

            HashSet<ReticleMesh> keep = new HashSet<ReticleMesh>(keepMeshes.Where(mesh => mesh != null));
            ReticleMesh[] meshes = optic.GetComponentsInChildren<ReticleMesh>(true);
            for (int i = 0; i < meshes.Length; i++)
            {
                ReticleMesh mesh = meshes[i];
                if (mesh == null || keep.Contains(mesh))
                    continue;

                SetReticleMeshEnabled(mesh, false);
            }
        }

        private static void SetReticleMeshEnabled(ReticleMesh mesh, bool enabled)
        {
            if (mesh == null)
                return;

            try
            {
                mesh.enabled = enabled;
                mesh.gameObject.SetActive(enabled);

                SkinnedMeshRenderer renderer = mesh.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null)
                    renderer.enabled = enabled;

                PostMeshComp[] postMeshes = mesh.GetComponentsInChildren<PostMeshComp>(true);
                for (int i = 0; i < postMeshes.Length; i++)
                {
                    if (postMeshes[i] != null)
                        postMeshes[i].enabled = enabled;
                }
            }
            catch { }
        }

        private static void EnsureReticleMeshVisible(ReticleMesh mesh)
        {
            if (mesh == null)
                return;

            if (!mesh.gameObject.activeSelf)
                mesh.gameObject.SetActive(true);
            if (!mesh.enabled)
                mesh.enabled = true;

            SkinnedMeshRenderer renderer = mesh.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null && !renderer.enabled)
                renderer.enabled = true;

            PostMeshComp[] postMeshes = mesh.GetComponentsInChildren<PostMeshComp>(true);
            for (int i = 0; i < postMeshes.Length; i++)
            {
                if (postMeshes[i] != null && !postMeshes[i].enabled)
                    postMeshes[i].enabled = true;
            }
        }
    }
}
