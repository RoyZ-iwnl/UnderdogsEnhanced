using System;
using System.Reflection;
using GHPC.Camera;
using GHPC.Vehicle;
using GHPC.Weapons;
using FMODUnity;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal sealed class MarderSpikeMissileCameraFollow : MonoBehaviour
    {
        private const string MissileSlotName = "Spike Missile Camera";
        private const float MissileCameraFov = 15f;
        private const float MissileCameraForwardOffset = 0.5f;
        private static readonly FieldInfo f_cm_allCamSlots = typeof(CameraManager).GetField("_allCamSlots", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_cm_exteriorMode = typeof(CameraManager).GetProperty("ExteriorMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_cm_exteriorMode = typeof(CameraManager).GetField("<ExteriorMode>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? typeof(CameraManager).GetField("_exteriorMode", BindingFlags.Instance | BindingFlags.NonPublic);

        private MarderSpikeRig ownerRig;
        private Vehicle launchVehicle;
        private LiveRound round;
        private Camera mainCam;
        private CameraManager cameraManager;
        private Rigidbody missileBody;
        private CameraSlot missileSlot;
        private GameObject missileSlotObject;
        private GameObject donorOpticNode;
        private CameraSlot previousSlot;
        private CameraSlot latestNonMissileSlot;
        private float previousSlotFov;
        private float latestNonMissileSlotFov;
        private float preferredReturnSlotFov;
        private bool previousHadActiveSlot;
        private bool previousExteriorMode;
        private bool initialized;
        private bool manualModeActive;
        private bool launchViewRequested;
        private bool cameraActive;
        private bool restored;
        private CameraSlot preferredReturnSlot;
        private Vector3 camOriginalLocalPos;
        private Quaternion camOriginalLocalRot;
        private float camOriginalFov;
        private AudioSource[] audioSources;
        private float[] originalVolumes;
        private StudioEventEmitter[] fmodEmitters;

        internal CameraSlot MissileSlot => missileSlot;
        internal LiveRound Round => round;
        internal bool CameraActive => cameraActive;

        internal void Configure(MarderSpikeRig rig, Vehicle vehicle)
        {
            ownerRig = rig;
            launchVehicle = vehicle;
            round = GetComponent<LiveRound>();
            donorOpticNode = ownerRig != null ? ownerRig.GetMissileCameraDonorObject() : null;

            if (!initialized)
                return;

            ownerRig?.OnMissileFollowReady(this);
            if ((manualModeActive || launchViewRequested) && ownerRig != null && ownerRig.ShouldHoldMissileCamera(this))
                ActivateMissileCamera();
        }

        internal void SetManualModeActive(bool active)
        {
            manualModeActive = active;
        }

        internal void EnterMissileView(bool manual, CameraSlot returnSlot)
        {
            preferredReturnSlot = returnSlot;
            preferredReturnSlotFov = CaptureLiveSlotFovSnapshot(returnSlot);
            manualModeActive = manual;
            launchViewRequested = true;

            if (!initialized || restored)
                return;

            if (ownerRig != null && ownerRig.ShouldHoldMissileCamera(this))
                ActivateMissileCamera();
        }

        private void Start()
        {
            round = GetComponent<LiveRound>();
            mainCam = CameraManager.MainCam;
            cameraManager = CameraManager.Instance;
            missileBody = GetComponent<Rigidbody>();
            donorOpticNode = ownerRig != null ? ownerRig.GetMissileCameraDonorObject() : null;

            // 如果关键依赖无效，延迟初始化或跳过
            if (cameraManager == null || mainCam == null)
            {
                initialized = true;
                ownerRig?.OnMissileFollowReady(this);
                return;
            }

            CreateMissileSlot();
            initialized = true;
            ownerRig?.OnMissileFollowReady(this);

            if (launchViewRequested && ownerRig != null && ownerRig.ShouldHoldMissileCamera(this))
                ActivateMissileCamera();

#if DEBUG
            MelonLogger.Msg($"[Marder Spike] Missile follow ready: slot={(missileSlot != null)} round={round?.Info?.Name ?? "null"}");
#endif
        }

        private void LateUpdate()
        {
            if (!initialized || restored)
                return;

            if (round == null || round.IsDestroyed)
            {
                Restore();
                return;
            }

            if (mainCam == null)
                mainCam = CameraManager.MainCam;
            if (cameraManager == null)
                cameraManager = CameraManager.Instance;

            // 如果 CameraManager 持续为 null，可能场景正在切换，应该清理
            if (cameraManager == null || mainCam == null)
            {
                Restore();
                return;
            }

            bool shouldUseMissileView = (launchViewRequested && ownerRig != null && ownerRig.ShouldHoldMissileCamera(this))
                || ShouldHoldMissileView();
            if (shouldUseMissileView && !cameraActive)
                ActivateMissileCamera();
            else if (!shouldUseMissileView && cameraActive)
                DeactivateMissileCamera(false);

            if (!cameraActive)
                return;

            if (mainCam == null)
                return;

            mainCam.fieldOfView = MissileCameraFov;
            mainCam.transform.position = transform.position + transform.forward * MissileCameraForwardOffset;

            Vector3 direction = missileBody != null && missileBody.velocity.sqrMagnitude > 0.1f
                ? missileBody.velocity.normalized
                : transform.forward;

            if (direction.sqrMagnitude > 0.01f)
                mainCam.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        internal void Restore()
        {
            if (restored)
                return;

            restored = true;
            DeactivateMissileCamera(true);

            if (missileSlotObject != null)
            {
                Destroy(missileSlotObject);
                missileSlotObject = null;
            }

            missileSlot = null;
            ownerRig?.OnMissileFollowDestroyed(this);
        }

        private void OnDestroy()
        {
            Restore();
        }

        private void CreateMissileSlot()
        {
            if (missileSlotObject != null)
                return;

            // 检查关键依赖是否有效，避免在场景切换时创建失败
            if (cameraManager == null)
                cameraManager = CameraManager.Instance;
            if (mainCam == null)
                mainCam = CameraManager.MainCam;

            // 如果 CameraManager 不存在，跳过创建（可能场景正在切换）
            if (cameraManager == null || mainCam == null)
            {
                return;
            }

            Transform parent = mainCam != null && mainCam.transform.parent != null
                ? mainCam.transform.parent
                : launchVehicle != null ? launchVehicle.transform : transform;

            missileSlotObject = new GameObject(MissileSlotName);
            missileSlotObject.transform.SetParent(parent, false);

            try
            {
                missileSlot = missileSlotObject.AddComponent<CameraSlot>();
            }
            catch (System.Exception)
            {
                if (missileSlotObject != null)
                {
                    Destroy(missileSlotObject);
                    missileSlotObject = null;
                }
                return;
            }

            ownerRig?.ConfigureMissileViewSlot(missileSlotObject, missileSlot, null);
        }

        private void ActivateMissileCamera()
        {
            if (cameraActive || restored)
                return;

            if (mainCam == null)
                mainCam = CameraManager.MainCam;
            if (cameraManager == null)
                cameraManager = CameraManager.Instance;
            donorOpticNode = donorOpticNode != null ? donorOpticNode : ownerRig != null ? ownerRig.GetMissileCameraDonorObject() : null;
            previousExteriorMode = cameraManager != null && cameraManager.ExteriorMode;

            previousSlot = CameraSlot.ActiveInstance;
            previousHadActiveSlot = previousSlot != null;
            if (previousSlot != null && previousSlot != missileSlot)
            {
                previousSlotFov = CaptureLiveSlotFovSnapshot(previousSlot);
                latestNonMissileSlot = previousSlot;
                latestNonMissileSlotFov = previousSlotFov;
                if (preferredReturnSlot == previousSlot && previousSlotFov > 0.1f)
                    preferredReturnSlotFov = previousSlotFov;
            }
            else
            {
                previousSlotFov = 0f;
            }

            if (mainCam != null)
            {
                camOriginalLocalPos = mainCam.transform.localPosition;
                camOriginalLocalRot = mainCam.transform.localRotation;
                camOriginalFov = mainCam.fieldOfView;
            }

            if (mainCam != null)
                mainCam.fieldOfView = MissileCameraFov;

            SetupMissileAudio();
            launchViewRequested = false;

            cameraActive = true;

#if DEBUG
            MelonLogger.Msg($"[Marder Spike] Missile camera activated: prev={previousSlot?.name ?? "null"}");
#endif
        }

        private void DeactivateMissileCamera(bool restoreSlot)
        {
            if (!cameraActive)
                return;

            cameraActive = false;

            // 确保在 CameraManager 有效的情况下才尝试恢复
            if (cameraManager == null)
                cameraManager = CameraManager.Instance;

            CameraSlot restoreReferenceSlot = restoreSlot ? TryRestoreCameraSlot() : CameraSlot.ActiveInstance;
            if (restoreSlot && cameraManager != null)
                TrySetExteriorMode(cameraManager, previousExteriorMode);

            if (mainCam == null)
                mainCam = CameraManager.MainCam;

            if (mainCam != null)
            {
                mainCam.transform.localPosition = camOriginalLocalPos;
                mainCam.transform.localRotation = camOriginalLocalRot;
                mainCam.fieldOfView = ResolveRestoreFov(restoreReferenceSlot);
            }

            RestoreMissileAudio();

#if DEBUG
            MelonLogger.Msg($"[Marder Spike] Missile camera restored: active={CameraSlot.ActiveInstance?.name ?? "null"}");
#endif
        }

        private CameraSlot TryRestoreCameraSlot()
        {
            // 如果 CameraManager 已经不存在（场景切换），直接返回 null 不尝试切换
            if (cameraManager == null)
            {
                cameraManager = CameraManager.Instance;
                if (cameraManager == null)
                {
                    return null;
                }
            }

            CameraSlot targetSlot = preferredReturnSlot != null ? preferredReturnSlot : ownerRig != null ? ownerRig.GetPreferredReturnSlotForMissile() : null;
            if (targetSlot == null || targetSlot == missileSlot)
                targetSlot = latestNonMissileSlot;
            if (targetSlot == null || targetSlot == missileSlot)
                targetSlot = previousSlot;

            if (targetSlot == null && cameraManager != null)
            {
                CameraSlot[] registeredSlots = f_cm_allCamSlots != null ? f_cm_allCamSlots.GetValue(cameraManager) as CameraSlot[] : null;
                if (registeredSlots != null)
                {
                    for (int i = 0; i < registeredSlots.Length; i++)
                    {
                        CameraSlot slot = registeredSlots[i];
                        if (slot == null || slot == missileSlot || slot.IsExterior)
                            continue;

                        targetSlot = slot;
                        break;
                    }
                }
            }

            // 只有在 CameraManager 有效时才尝试切换 slot
            if (cameraManager != null)
            {
                if (targetSlot != null && CameraSlot.ActiveInstance != targetSlot)
                    CameraSlot.SetActiveSlot(targetSlot);
                else if (!previousHadActiveSlot && previousExteriorMode)
                    CameraSlot.SetActiveSlot(null);
            }

            return targetSlot;
        }

        private float ResolveRestoreFov(CameraSlot targetSlot)
        {
            float targetSnapshotFov = ResolveStoredSnapshotFov(targetSlot);
            if (targetSnapshotFov > 0.1f)
                return targetSnapshotFov;

            float targetCurrentFov = ResolveSlotCurrentFov(targetSlot);
            if (targetCurrentFov > 0.1f)
                return targetCurrentFov;

            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            float activeSnapshotFov = ResolveStoredSnapshotFov(activeSlot);
            if (activeSnapshotFov > 0.1f)
                return activeSnapshotFov;

            float activeCurrentFov = ResolveSlotCurrentFov(activeSlot);
            if (activeCurrentFov > 0.1f)
                return activeCurrentFov;

            float latestSnapshotFov = ResolveStoredSnapshotFov(latestNonMissileSlot);
            if (latestSnapshotFov > 0.1f)
                return latestSnapshotFov;

            float latestCurrentFov = ResolveSlotCurrentFov(latestNonMissileSlot);
            if (latestCurrentFov > 0.1f)
                return latestCurrentFov;

            float previousSnapshot = ResolveStoredSnapshotFov(previousSlot);
            if (previousSnapshot > 0.1f)
                return previousSnapshot;

            float previousCurrentFov = ResolveSlotCurrentFov(previousSlot);
            if (previousCurrentFov > 0.1f)
                return previousCurrentFov;

            if (camOriginalFov > 0.1f)
                return camOriginalFov;

            return 60f;
        }

        private bool ShouldHoldMissileView()
        {
            if (round == null || round.IsDestroyed)
                return false;

            return ownerRig != null && ownerRig.ShouldHoldMissileCamera(this);
        }

        private void SetupMissileAudio()
        {
            if (audioSources != null)
                return;

            audioSources = GetComponentsInChildren<AudioSource>(true);
            if (audioSources != null && audioSources.Length > 0)
            {
                originalVolumes = new float[audioSources.Length];
                for (int i = 0; i < audioSources.Length; i++)
                {
                    if (audioSources[i] == null)
                        continue;

                    originalVolumes[i] = audioSources[i].volume;
                    audioSources[i].volume = originalVolumes[i] * BMP1MCLOSAmmo.DEFAULT_MISSILE_AUDIO_VOLUME;
                }
            }

            fmodEmitters = GetComponentsInChildren<StudioEventEmitter>(true);
        }

        private void RestoreMissileAudio()
        {
            if (audioSources != null && originalVolumes != null)
            {
                for (int i = 0; i < audioSources.Length && i < originalVolumes.Length; i++)
                {
                    if (audioSources[i] != null)
                        audioSources[i].volume = originalVolumes[i];
                }
            }

            audioSources = null;
            originalVolumes = null;
            fmodEmitters = null;
        }

        private void RestoreDonorNode()
        {
        }

        private float CaptureLiveSlotFovSnapshot(CameraSlot slot)
        {
            if (slot == null || slot == missileSlot)
                return 0f;

            if (slot == CameraSlot.ActiveInstance && mainCam != null && mainCam.fieldOfView > 0.1f)
                return mainCam.fieldOfView;

            return ResolveSlotCurrentFov(slot);
        }

        private float ResolveStoredSnapshotFov(CameraSlot slot)
        {
            if (slot == null || slot == missileSlot)
                return 0f;

            if (slot == previousSlot && previousSlotFov > 0.1f)
                return previousSlotFov;

            if (slot == latestNonMissileSlot && latestNonMissileSlotFov > 0.1f)
                return latestNonMissileSlotFov;

            if (slot == preferredReturnSlot && preferredReturnSlotFov > 0.1f)
                return preferredReturnSlotFov;

            return 0f;
        }

        private static float ResolveSlotCurrentFov(CameraSlot slot)
        {
            if (slot == null)
                return 0f;

            try
            {
                float currentFov = slot.CurrentFov;
                if (currentFov > 0.1f)
                    return currentFov;
            }
            catch { }

            try
            {
                if (slot.DefaultFov > 0.1f)
                    return slot.DefaultFov;
            }
            catch { }

            return 0f;
        }

        private void RegisterMissileSlot()
        {
            if (cameraManager == null || missileSlot == null || f_cm_allCamSlots == null)
                return;

            CameraSlot[] slots = f_cm_allCamSlots.GetValue(cameraManager) as CameraSlot[];
            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] == missileSlot)
                        return;
            }

            int oldLength = slots != null ? slots.Length : 0;
            CameraSlot[] updated = new CameraSlot[oldLength + 1];
            if (oldLength > 0)
                Array.Copy(slots, updated, oldLength);
            updated[oldLength] = missileSlot;
            f_cm_allCamSlots.SetValue(cameraManager, updated);
        }

        private void UnregisterMissileSlot()
        {
            if (cameraManager == null || missileSlot == null || f_cm_allCamSlots == null)
                return;

            CameraSlot[] slots = f_cm_allCamSlots.GetValue(cameraManager) as CameraSlot[];
            if (slots == null || slots.Length == 0)
                return;

            int count = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i] != missileSlot)
                    count++;

            CameraSlot[] updated = new CameraSlot[count];
            int index = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null || slots[i] == missileSlot)
                    continue;

                updated[index++] = slots[i];
            }

            f_cm_allCamSlots.SetValue(cameraManager, updated);
        }

        private static bool TrySetExteriorMode(CameraManager cm, bool value)
        {
            if (cm == null)
                return false;

            try
            {
                if (p_cm_exteriorMode != null && p_cm_exteriorMode.CanWrite)
                {
                    p_cm_exteriorMode.SetValue(cm, value, null);
                    return true;
                }
            }
            catch { }

            try
            {
                if (f_cm_exteriorMode != null)
                {
                    f_cm_exteriorMode.SetValue(cm, value);
                    return true;
                }
            }
            catch { }

            return false;
        }
    }
}
