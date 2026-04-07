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
        private bool previousHadActiveSlot;
        private bool previousExteriorMode;
        private bool restoreDonorOnExit;
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
            if (manualModeActive || launchViewRequested)
                ActivateMissileCamera();
        }

        internal void SetManualModeActive(bool active)
        {
            manualModeActive = active;
        }

        internal void EnterMissileView(bool manual, CameraSlot returnSlot)
        {
            preferredReturnSlot = returnSlot;
            manualModeActive = manual;
            launchViewRequested = true;

            if (!initialized || restored)
                return;

            if (missileSlot != null && CameraSlot.ActiveInstance != missileSlot)
                CameraSlot.SetActiveSlot(missileSlot);

            ActivateMissileCamera();
        }

        private void Start()
        {
            round = GetComponent<LiveRound>();
            mainCam = CameraManager.MainCam;
            cameraManager = CameraManager.Instance;
            missileBody = GetComponent<Rigidbody>();
            donorOpticNode = ownerRig != null ? ownerRig.GetMissileCameraDonorObject() : null;
            CreateMissileSlot();
            initialized = true;
            ownerRig?.OnMissileFollowReady(this);

            if (launchViewRequested)
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

            bool shouldUseMissileView = launchViewRequested || ShouldHoldMissileView();
            if (shouldUseMissileView && !cameraActive)
                ActivateMissileCamera();
            else if (!shouldUseMissileView && cameraActive)
                DeactivateMissileCamera(false);

            if (!cameraActive)
                return;

            TrySetExteriorMode(cameraManager, false);

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
            UnregisterMissileSlot();

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

            Transform parent = mainCam != null && mainCam.transform.parent != null
                ? mainCam.transform.parent
                : launchVehicle != null ? launchVehicle.transform : transform;

            missileSlotObject = new GameObject(MissileSlotName);
            missileSlotObject.transform.SetParent(parent, false);
            missileSlot = missileSlotObject.AddComponent<CameraSlot>();

            ownerRig?.ConfigureMissileViewSlot(missileSlotObject, missileSlot, null);
            RegisterMissileSlot();
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

            previousSlot = CameraSlot.ActiveInstance;
            if (previousSlot == missileSlot)
                previousSlot = preferredReturnSlot != null ? preferredReturnSlot : latestNonMissileSlot != null ? latestNonMissileSlot : ownerRig != null ? ownerRig.GetPreferredReturnSlotForMissile() : null;
            previousHadActiveSlot = previousSlot != null;
            previousExteriorMode = cameraManager != null && cameraManager.ExteriorMode;
            restoreDonorOnExit = donorOpticNode != null && donorOpticNode.activeSelf && previousSlot != null && !previousSlot.IsExterior && !previousExteriorMode;
            if (previousSlot != null && previousSlot != missileSlot)
                latestNonMissileSlot = previousSlot;

            if (mainCam != null)
            {
                camOriginalLocalPos = mainCam.transform.localPosition;
                camOriginalLocalRot = mainCam.transform.localRotation;
                camOriginalFov = mainCam.fieldOfView;
            }

            TrySetExteriorMode(cameraManager, false);

            if (donorOpticNode != null)
                donorOpticNode.SetActive(false);

            if (missileSlot != null && CameraSlot.ActiveInstance != missileSlot)
                CameraSlot.SetActiveSlot(missileSlot);

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

            if (mainCam != null)
            {
                mainCam.transform.localPosition = camOriginalLocalPos;
                mainCam.transform.localRotation = camOriginalLocalRot;
                mainCam.fieldOfView = ResolveRestoreFov();
            }

            TrySetExteriorMode(cameraManager, previousExteriorMode);
            if (restoreSlot)
                TryRestoreCameraSlot();
            RestoreDonorNode();
            RestoreMissileAudio();

#if DEBUG
            MelonLogger.Msg($"[Marder Spike] Missile camera restored: active={CameraSlot.ActiveInstance?.name ?? "null"}");
#endif
        }

        private void TryRestoreCameraSlot()
        {
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

            if (targetSlot != null && CameraSlot.ActiveInstance != targetSlot)
                CameraSlot.SetActiveSlot(targetSlot);
            else if (!previousHadActiveSlot && previousExteriorMode)
                CameraSlot.SetActiveSlot(null);
        }

        private float ResolveRestoreFov()
        {
            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            if (activeSlot != null && activeSlot != missileSlot && activeSlot.DefaultFov > 0.1f)
                return activeSlot.DefaultFov;

            CameraSlot refSlot = latestNonMissileSlot != null && latestNonMissileSlot != missileSlot
                ? latestNonMissileSlot
                : previousSlot;

            if (refSlot != null && refSlot.DefaultFov > 0.1f)
                return refSlot.DefaultFov;

            return previousExteriorMode ? 60f : camOriginalFov;
        }

        private bool ShouldHoldMissileView()
        {
            if (missileSlot == null || round == null || round.IsDestroyed)
                return false;

            if (ownerRig == null || !ownerRig.IsPlayerControllingThisRig())
                return false;

            if (cameraManager != null && cameraManager.ExteriorMode)
                return false;

            return CameraSlot.ActiveInstance == missileSlot;
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
            if (donorOpticNode == null)
                return;

            bool interiorOpticSlot = CameraSlot.ActiveInstance != null && !CameraSlot.ActiveInstance.IsExterior;
            donorOpticNode.SetActive(restoreDonorOnExit && interiorOpticSlot);
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
