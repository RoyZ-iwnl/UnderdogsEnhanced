using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Thermals;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using HarmonyLib;
using MelonLoader;
using Reticle;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

namespace UnderdogsEnhanced
{
    internal enum SpikeGuidanceMode
    {
        Idle,
        PreLaunchLock,
        FnF,
        MCLOS
    }

    internal static class MarderSpikeGuidanceRecovery
    {
        private static readonly FieldInfo f_gu_unguidedMissiles = typeof(MissileGuidanceUnit).GetField("_unguidedMissiles", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_gu_isGuidingMissile = typeof(MissileGuidanceUnit).GetProperty("IsGuidingMissile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_gu_isGuidingMissile_backing = typeof(MissileGuidanceUnit).GetField("<IsGuidingMissile>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_feed_waitingOnMissile = typeof(AmmoFeed).GetProperty("WaitingOnMissile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feed_waitingOnMissile_backing = typeof(AmmoFeed).GetField("<WaitingOnMissile>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_feed_forcePauseReload = typeof(AmmoFeed).GetProperty("ForcePauseReload", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feed_forcePauseReload_backing = typeof(AmmoFeed).GetField("<ForcePauseReload>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static bool IsSpikeWeapon(WeaponSystem ws)
        {
            if (ws?.Feed == null)
                return false;

            AmmoType ammo = ws.Feed.AmmoTypeInBreech;
            if (!MarderSpikeAmmo.IsSpikeAmmo(ammo))
            {
                AmmoType.AmmoClip clip = ws.Feed.ReadyRack?.ClipTypes != null && ws.Feed.ReadyRack.ClipTypes.Length > 0
                    ? ws.Feed.ReadyRack.ClipTypes[0]
                    : null;
                if (clip?.MinimalPattern != null && clip.MinimalPattern.Length > 0)
                    ammo = clip.MinimalPattern[0]?.AmmoType;
            }

            return MarderSpikeAmmo.IsSpikeAmmo(ammo);
        }

        private static bool IsMissileAlive(LiveRound missile)
        {
            return missile != null && !missile.IsDestroyed;
        }

        internal static bool HasLiveMissiles(MissileGuidanceUnit guidanceUnit)
        {
            if (guidanceUnit == null)
                return false;

            bool hasLive = false;
            List<LiveRound> missiles = guidanceUnit.CurrentMissiles;
            if (missiles != null)
            {
                for (int i = missiles.Count - 1; i >= 0; i--)
                {
                    if (!IsMissileAlive(missiles[i]))
                        missiles.RemoveAt(i);
                }

                hasLive = missiles.Count > 0;
            }

            List<LiveRound> unguided = f_gu_unguidedMissiles?.GetValue(guidanceUnit) as List<LiveRound>;
            if (unguided != null)
            {
                for (int i = unguided.Count - 1; i >= 0; i--)
                {
                    if (!IsMissileAlive(unguided[i]))
                        unguided.RemoveAt(i);
                }

                if (unguided.Count > 0)
                    hasLive = true;
            }

            return hasLive;
        }

        internal static WeaponSystem FindOwningWeapon(MissileGuidanceUnit guidanceUnit, LiveRound missileHint)
        {
            if (guidanceUnit == null)
                return null;

            WeaponSystem ws = guidanceUnit.GetComponent<WeaponSystem>();
            if (ws != null && ws.GuidanceUnit == guidanceUnit)
                return ws;

            ws = guidanceUnit.GetComponentInParent<WeaponSystem>();
            if (ws != null && ws.GuidanceUnit == guidanceUnit)
                return ws;

            if (missileHint?.Shooter != null)
            {
                WeaponSystem[] byShooter = missileHint.Shooter.GetComponentsInChildren<WeaponSystem>(true);
                for (int i = 0; i < byShooter.Length; i++)
                {
                    WeaponSystem candidate = byShooter[i];
                    if (candidate != null && candidate.GuidanceUnit == guidanceUnit)
                        return candidate;
                }
            }

            WeaponSystem[] allWeapons = UnityEngine.Object.FindObjectsOfType<WeaponSystem>();
            for (int i = 0; i < allWeapons.Length; i++)
            {
                WeaponSystem candidate = allWeapons[i];
                if (candidate != null && candidate.GuidanceUnit == guidanceUnit)
                    return candidate;
            }

            return null;
        }

        private static WeaponSystem FindWeaponByMissile(LiveRound missile)
        {
            if (missile == null)
                return null;

            if (missile.Shooter != null)
            {
                WeaponSystem[] byShooter = missile.Shooter.GetComponentsInChildren<WeaponSystem>(true);
                for (int i = 0; i < byShooter.Length; i++)
                {
                    WeaponSystem ws = byShooter[i];
                    MissileGuidanceUnit guidanceUnit = ws != null ? ws.GuidanceUnit : null;
                    if (ws == null || guidanceUnit == null)
                        continue;

                    if (guidanceUnit.CurrentMissiles != null && guidanceUnit.CurrentMissiles.Contains(missile))
                        return ws;
                }
            }

            WeaponSystem[] allWeapons = UnityEngine.Object.FindObjectsOfType<WeaponSystem>();
            for (int i = 0; i < allWeapons.Length; i++)
            {
                WeaponSystem ws = allWeapons[i];
                MissileGuidanceUnit guidanceUnit = ws != null ? ws.GuidanceUnit : null;
                if (ws == null || guidanceUnit == null)
                    continue;

                if (guidanceUnit.CurrentMissiles != null && guidanceUnit.CurrentMissiles.Contains(missile))
                    return ws;
            }

            return null;
        }

        internal static void ForceUnlockGuidance(MissileGuidanceUnit guidanceUnit, WeaponSystem ws)
        {
            if (guidanceUnit == null)
                return;

            if (ws == null)
                ws = FindOwningWeapon(guidanceUnit, null);

            if (!IsSpikeWeapon(ws))
                return;

            if (HasLiveMissiles(guidanceUnit))
                return;

            try { guidanceUnit.StopGuidance(); } catch { }

            try
            {
                if (p_gu_isGuidingMissile != null && p_gu_isGuidingMissile.CanWrite)
                    p_gu_isGuidingMissile.SetValue(guidanceUnit, false, null);
                else
                    f_gu_isGuidingMissile_backing?.SetValue(guidanceUnit, false);
            }
            catch { }

            AmmoFeed feed = ws != null ? ws.Feed : null;
            if (feed != null)
            {
                try
                {
                    if (p_feed_waitingOnMissile != null && p_feed_waitingOnMissile.CanWrite)
                        p_feed_waitingOnMissile.SetValue(feed, false, null);
                    else
                        f_feed_waitingOnMissile_backing?.SetValue(feed, false);
                }
                catch { }

                try
                {
                    if (p_feed_forcePauseReload != null && p_feed_forcePauseReload.CanWrite)
                        p_feed_forcePauseReload.SetValue(feed, false, null);
                    else
                        f_feed_forcePauseReload_backing?.SetValue(feed, false);
                }
                catch { }
            }
        }

        internal static bool TryRecoverStaleGuidanceOnFire(WeaponSystem ws)
        {
            if (!IsSpikeWeapon(ws))
                return false;

            MissileGuidanceUnit guidanceUnit = ws.GuidanceUnit;
            AmmoFeed feed = ws.Feed;
            if (guidanceUnit == null || feed == null)
                return false;

            bool appearsLocked = guidanceUnit.IsGuidingMissile || ws.BlockedByMissileGuidance || feed.WaitingOnMissile || feed.ForcePauseReload;
            if (!appearsLocked || HasLiveMissiles(guidanceUnit))
                return false;

            ForceUnlockGuidance(guidanceUnit, ws);
            return true;
        }

        internal static void TryClearWaitingOnMissile(MissileGuidanceUnit guidanceUnit)
        {
            if (guidanceUnit == null)
                return;

            WeaponSystem ws = FindOwningWeapon(guidanceUnit, null);
            if (ws == null || ws.GuidanceUnit != guidanceUnit || !IsSpikeWeapon(ws))
                return;

            ForceUnlockGuidance(guidanceUnit, ws);
        }

        internal static void NotifyMissileGone(LiveRound missile)
        {
            if (missile == null || missile.Info == null || !MarderSpikeAmmo.IsSpikeAmmoName(missile.Info.Name))
                return;

            WeaponSystem ws = FindWeaponByMissile(missile);
            MissileGuidanceUnit guidanceUnit = ws != null ? ws.GuidanceUnit : null;
            if (guidanceUnit == null || !IsSpikeWeapon(ws))
                return;

            try { guidanceUnit.MissileDestroyed(missile, missile.transform.position); } catch { }
            ForceUnlockGuidance(guidanceUnit, ws);
        }
    }

    internal static class MarderSpikeSystem
    {
        private const string ThermalSlotName = "Spike Thermal";
        private static readonly Dictionary<int, MarderSpikeRig> rigsByVehicleId = new Dictionary<int, MarderSpikeRig>();
        private static readonly FieldInfo f_cm_allCamSlots = typeof(CameraManager).GetField("_allCamSlots", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_uo_hasGuidance = typeof(UsableOptic).GetField("_hasGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_fcs_mainOptic = typeof(FireControlSystem).GetProperty("MainOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_fcs_nightOptic = typeof(FireControlSystem).GetProperty("NightOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_mainOptic_backing = typeof(FireControlSystem).GetField("<MainOptic>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_nightOptic_backing = typeof(FireControlSystem).GetField("<NightOptic>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_authoritativeOptic = typeof(FireControlSystem).GetField("AuthoritativeOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_snv_postVolume = typeof(SimpleNightVision).GetField("_postVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static bool TryApply(Vehicle vehicle, WeaponSystemInfo weaponInfo)
        {
            if (vehicle == null || weaponInfo == null || weaponInfo.Weapon == null || weaponInfo.FCS == null)
                return false;

            CameraSlot daySlot = ResolveDaySlot(vehicle, weaponInfo);
            if (daySlot == null)
            {
#if DEBUG
                UnderdogsDebug.LogSpikeWarning($"No MILAN camera slot found on {vehicle.FriendlyName}");
#endif
                return false;
            }

            UsableOptic dayOptic = ResolveDayOptic(vehicle, weaponInfo, daySlot);
            MarderSpikeRig rig = UECommonUtil.GetOrAddComponent<MarderSpikeRig>(weaponInfo.FCS.gameObject);
            rig.Configure(vehicle, weaponInfo, daySlot, dayOptic, f_uo_hasGuidance, p_fcs_mainOptic, p_fcs_nightOptic, f_fcs_mainOptic_backing, f_fcs_nightOptic_backing, f_fcs_authoritativeOptic, f_snv_postVolume, ThermalSlotName);
            rigsByVehicleId[vehicle.GetInstanceID()] = rig;
            return rig.IsConfigured;
        }

        internal static void UnregisterRig(MarderSpikeRig rig)
        {
            if (rig == null)
                return;

            int[] keys = rigsByVehicleId.Where(pair => pair.Value == rig).Select(pair => pair.Key).ToArray();
            for (int i = 0; i < keys.Length; i++)
                rigsByVehicleId.Remove(keys[i]);
        }

        internal static bool IsPlayerUsingManualSpikeInput()
        {
            GHPC.Player.PlayerInput playerInput = GHPC.Player.PlayerInput.Instance;
            WeaponSystem weapon = playerInput != null ? playerInput.CurrentPlayerWeapon?.Weapon : null;
            if (weapon == null || !MarderSpikeGuidanceRecovery.IsSpikeWeapon(weapon))
                return false;

            Vehicle vehicle = weapon.GetComponentInParent<Vehicle>();
            if (vehicle == null)
                return false;

            if (!rigsByVehicleId.TryGetValue(vehicle.GetInstanceID(), out MarderSpikeRig rig) || rig == null)
                return false;

            return rig.IsManualMissileViewActive();
        }

        internal static bool TryOverridePlayerManualAimInput(ref float horizontal, ref float vertical)
        {
            GHPC.Player.PlayerInput playerInput = GHPC.Player.PlayerInput.Instance;
            WeaponSystem weapon = playerInput != null ? playerInput.CurrentPlayerWeapon?.Weapon : null;
            if (weapon == null || !MarderSpikeGuidanceRecovery.IsSpikeWeapon(weapon))
                return false;

            Vehicle vehicle = weapon.GetComponentInParent<Vehicle>();
            if (vehicle == null)
                return false;

            if (!rigsByVehicleId.TryGetValue(vehicle.GetInstanceID(), out MarderSpikeRig rig) || rig == null)
                return false;

            return rig.TryApplyManualAimInput(ref horizontal, ref vertical);
        }

        internal static void NotifyMissileSpawn(LiveRound round)
        {
            if (round == null)
                return;

            string ammoName = round.Info != null ? round.Info.Name : null;
            if (!MarderSpikeAmmo.IsSpikeAmmoName(ammoName) && !MarderSpikeAmmo.IsOriginalMilanName(ammoName))
                return;

            Vehicle vehicle = round.Shooter != null ? round.Shooter.GetComponentInParent<Vehicle>() : null;
            if (vehicle == null)
                return;

            if (!rigsByVehicleId.TryGetValue(vehicle.GetInstanceID(), out MarderSpikeRig rig) || rig == null)
                return;

            MarderSpikeMissileCameraFollow follow = round.GetComponent<MarderSpikeMissileCameraFollow>();
            if (follow == null)
                follow = round.gameObject.AddComponent<MarderSpikeMissileCameraFollow>();

            follow.Configure(rig, vehicle);
            rig.NotifyMissileSpawn(round);
        }

        private static CameraSlot ResolveDaySlot(Vehicle vehicle, WeaponSystemInfo weaponInfo)
        {
            CameraSlot[] slots = vehicle.GetComponentsInChildren<CameraSlot>(true);

            CameraSlot slot = slots.FirstOrDefault(s => ContainsHint(s, "milan sight and fcs"))
                ?? slots.FirstOrDefault(s => ContainsHint(s, "milan"));

            if (slot != null)
                return slot;

            if (weaponInfo.FCS != null)
            {
                Transform searchRoot = weaponInfo.FCS.transform.parent != null ? weaponInfo.FCS.transform.parent : weaponInfo.FCS.transform;
                slot = searchRoot.GetComponentsInChildren<CameraSlot>(true).FirstOrDefault(s => ContainsHint(s, "milan"));
            }

            return slot;
        }

        private static UsableOptic ResolveDayOptic(Vehicle vehicle, WeaponSystemInfo weaponInfo, CameraSlot daySlot)
        {
            if (daySlot == null)
                return null;

            UsableOptic optic = daySlot.GetComponentInParent<UsableOptic>();
            if (optic != null)
                return optic;

            foreach (UsableOptic candidate in vehicle.GetComponentsInChildren<UsableOptic>(true))
            {
                if (candidate == null)
                    continue;

                if (candidate.slot == daySlot)
                    return candidate;

                if (candidate.FCS == weaponInfo.FCS)
                    return candidate;

                if (candidate.transform.IsChildOf(daySlot.transform) || daySlot.transform.IsChildOf(candidate.transform))
                    return candidate;
            }

            return null;
        }

        private static bool ContainsHint(CameraSlot slot, string hint)
        {
            if (slot == null || string.IsNullOrEmpty(hint))
                return false;

            string combined = string.Concat(
                slot.name ?? string.Empty,
                " ",
                slot.transform != null ? slot.transform.name : string.Empty,
                " ",
                slot.transform != null && slot.transform.parent != null ? slot.transform.parent.name : string.Empty).ToLowerInvariant();
            return combined.Contains(hint.ToLowerInvariant());
        }
    }

    internal sealed class MarderSpikeRig : MonoBehaviour
    {
        private const string AimProxyName = "SpikeAimProxy";
        private const string OverlayName = "MarderSpikeOverlay";
        private const string MissileSlotName = "Spike Missile Camera";
        private const float LockRange = 5000f;
        private const float ProxyRange = 2500f;
        private const int LockConfirmFrames = 2;
        private const float ManualControlSensitivity = 2.5f;
        private const float ThermalLowWidth = 320f;
        private const float ThermalLowHeight = 180f;
        private const float ThermalHighWidth = 1024f;
        private const float ThermalHighHeight = 576f;
        private const float LockBoundsRefreshInterval = 0.1f;
        private const float LockScreenRectRefreshInterval = 0.2f;
        private const float ScopeCanvasRefreshInterval = 0.5f;
        private const float SceneMissileSearchInterval = 0.5f;
        private const float PassedTargetDistanceThreshold = 15f;

        private static readonly FieldInfo f_cm_allCamSlots = typeof(CameraManager).GetField("_allCamSlots", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_gu_unguidedMissiles = typeof(MissileGuidanceUnit).GetField("_unguidedMissiles", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Sprite whiteSprite;
        private static Texture2D whiteTexture;
        private static int defaultScopeOwnerId;

        private sealed class ScopeCanvasSnapshot
        {
            public bool GameObjectActiveSelf;
            public bool CanvasEnabled;
        }

        private Vehicle vehicle;
        private WeaponSystemInfo weaponInfo;
        private WeaponSystem weapon;
        private FireControlSystem fcs;
        private MissileGuidanceUnit guidanceUnit;
        private CameraSlot daySlot;
        private UsableOptic dayOptic;
        private UsableOptic thermalOptic;
        private CameraSlot thermalSlot;
        private GameObject thermalSlotObject;
        private UsableOptic missileOptic;
        private CameraSlot missileSlot;
        private GameObject missileSlotObject;
        private LiveRound activeMissile;
        private Transform aimProxy;
        private CameraSlot latestNonMissileSlot;
        private CameraSlot preLaunchSightSlot;
        private Vehicle lockedVehicle;
        private Transform lockedAnchor;
        private Renderer lockedRenderer;
        private Collider lockedCollider;
        private Collider[] lockedColliders;
        private Renderer[] lockedRenderers;
        private float lockedDistance = float.PositiveInfinity;
        private SpikeGuidanceMode guidanceMode;
        private GameObject overlayObject;
        private RectTransform overlayRoot;
        private RectTransform overlayReticleRoot;
        private RectTransform overlayTrackingRoot;
        private RectTransform lockBoxRect;
        private readonly List<Graphic> overlayGraphics = new List<Graphic>();
        private FieldInfo opticGuidanceField;
        private PropertyInfo mainOpticProperty;
        private PropertyInfo nightOpticProperty;
        private FieldInfo mainOpticBackingField;
        private FieldInfo nightOpticBackingField;
        private FieldInfo authoritativeOpticField;
        private FieldInfo snvPostVolumeField;
        private string thermalSlotName;
        private bool preferTvControl;
        private bool lockRequestQueued;
        private bool opticsInitialized;
        private bool lockConfirmed;
        private bool missileViewRequested;
        private bool lastCanControlSpikeLock; // 用于追踪 CanControlSpikeLock 变化
        private Vector3 lastKnownAimPoint;
        private bool hasLastKnownAimPoint;
        private Bounds cachedTargetBounds;
        private bool hasCachedTargetBounds;
        private Rect cachedTargetScreenRect;
        private bool hasCachedTargetScreenRect;
        private float nextTargetBoundsRefreshTime;
        private float nextTargetScreenRectRefreshTime;
        private float nextScopeCanvasRefreshTime;
        private float nextSceneMissileSearchTime;
        private float closestMissileTargetDistance = float.PositiveInfinity;
        private readonly Vector3[] targetBoundsCorners = new Vector3[8];
        private Transform fnfGuideTransform;
        private Transform mclosGuideTransform;
        private bool slotRegistrationDirty = true;
        private bool scopeCanvasSuppressed;
        private bool scopeCanvasCacheInitialized;
        private readonly List<Canvas> cachedScopeCanvases = new List<Canvas>();
        private readonly Dictionary<int, ScopeCanvasSnapshot> scopeCanvasSnapshots = new Dictionary<int, ScopeCanvasSnapshot>();

        internal bool IsConfigured => vehicle != null && weapon != null && fcs != null && daySlot != null;

        internal void Configure(
            Vehicle configuredVehicle,
            WeaponSystemInfo configuredWeaponInfo,
            CameraSlot configuredDaySlot,
            UsableOptic configuredDayOptic,
            FieldInfo configuredOpticGuidanceField,
            PropertyInfo configuredMainOpticProperty,
            PropertyInfo configuredNightOpticProperty,
            FieldInfo configuredMainOpticBackingField,
            FieldInfo configuredNightOpticBackingField,
            FieldInfo configuredAuthoritativeOpticField,
            FieldInfo configuredSnvPostVolumeField,
            string configuredThermalSlotName)
        {
            vehicle = configuredVehicle;
            weaponInfo = configuredWeaponInfo;
            weapon = configuredWeaponInfo != null ? configuredWeaponInfo.Weapon : null;
            fcs = configuredWeaponInfo != null ? configuredWeaponInfo.FCS : null;
            guidanceUnit = weapon != null ? weapon.GuidanceUnit : null;
            daySlot = configuredDaySlot;
            dayOptic = configuredDayOptic;
            opticGuidanceField = configuredOpticGuidanceField;
            mainOpticProperty = configuredMainOpticProperty;
            nightOpticProperty = configuredNightOpticProperty;
            mainOpticBackingField = configuredMainOpticBackingField;
            nightOpticBackingField = configuredNightOpticBackingField;
            authoritativeOpticField = configuredAuthoritativeOpticField;
            snvPostVolumeField = configuredSnvPostVolumeField;
            thermalSlotName = configuredThermalSlotName;
            slotRegistrationDirty = true;

            EnsureAimProxy();
            EnsureThermalSlot();
            EnsureFnfGuide();
            ApplyWeaponDefaults();
            EnsureOverlay();
            enabled = true;
        }

        private void LateUpdate()
        {
            if (!IsConfigured)
            {
#if DEBUG
                UnderdogsDebug.LogSpikeWarning($"LateUpdate: Not configured, vehicle={vehicle != null}, weapon={weapon != null}, fcs={fcs != null}, daySlot={daySlot != null}");
#endif
                return;
            }

            EnsureSlotRegistration();
            CacheLatestNonMissileSlot();
            ApplyWeaponDefaults();
            UpdateMissileReference();
            HandleLockInput();
            HandleModeToggleInput();
            UpdateGuidanceMode();
            UpdateLockTarget();
            UpdateTargetPassFallback();
            UpdateAimProxy();
            UpdateMissileControl();
            EnsureInitialThermalView();
            TryEnterMissileViewIfNeeded();
            UpdateScopeCanvasSuppression();
            UpdateDefaultScopeSuppression();
            UpdateOverlay();
            CleanupMissileSlotIfNeeded();
        }

        private void OnDisable()
        {
            ReleaseScopeCanvasSuppression();
            ReleaseDefaultScopeSuppression();
        }

        private void OnDestroy()
        {
            MarderSpikeSystem.UnregisterRig(this);
            ReleaseScopeCanvasSuppression();
            ReleaseDefaultScopeSuppression();
        }

        internal void NotifyMissileSpawn(LiveRound round)
        {
            if (round == null || round.IsDestroyed)
                return;

#if DEBUG
            UnderdogsDebug.LogSpike($"NotifyMissileSpawn: round spawned, previous activeMissile={(activeMissile != null ? "exists" : "null")}");
#endif

            CachePreLaunchSightSlot();
            activeMissile = round;
            closestMissileTargetDistance = float.PositiveInfinity;
            bool startManual = preferTvControl || !HasLockTarget();
            preferTvControl = startManual;
            SyncMissileFollow(round);
            MarderSpikeMissileCameraFollow follow = GetMissileFollow(round);
            if (follow == null)
                return;

            if (startManual)
            {
                // MCLOS模式：也进入导弹视角，但标记为手动模式（可按MMB接管）
                missileViewRequested = false;
                follow.SetManualModeActive(true);
                follow.EnterMissileView(true, GetPreferredReturnSlotForMissile());
            }
            else
            {
                // FNF模式：自动进入导弹视角
                missileViewRequested = true;
                follow.EnterMissileView(false, GetPreferredReturnSlotForMissile());
            }
        }

        internal GameObject GetMissileCameraDonorObject()
        {
            return thermalOptic != null ? thermalOptic.gameObject : dayOptic != null ? dayOptic.gameObject : null;
        }

        internal bool IsPlayerControllingThisRig()
        {
            GHPC.Player.PlayerInput playerInput = GHPC.Player.PlayerInput.Instance;
            Vehicle currentVehicle = playerInput != null ? playerInput.CurrentPlayerUnit as Vehicle : null;
            if (currentVehicle == null && playerInput != null && playerInput.CurrentPlayerWeapon != null && playerInput.CurrentPlayerWeapon.Weapon != null)
                currentVehicle = playerInput.CurrentPlayerWeapon.Weapon.GetComponentInParent<Vehicle>();

            return currentVehicle != null && vehicle != null && currentVehicle == vehicle;
        }

        internal CameraSlot GetPreferredReturnSlotForMissile()
        {
            return FindPreferredReturnSlot();
        }

        internal bool PreferManualMissileControl => preferTvControl;

        internal bool IsManualMissileViewActive()
        {
            if (!preferTvControl || activeMissile == null || !IsPlayerControllingThisRig())
                return false;

            CameraManager cameraManager = CameraManager.Instance;
            if (cameraManager != null && cameraManager.ExteriorMode)
                return false;

            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            if (activeSlot == missileSlot)
                return true;

            return activeSlot != null && activeSlot != missileSlot && SlotBelongsToVehicle(activeSlot);
        }

        internal bool TryApplyManualAimInput(ref float horizontal, ref float vertical)
        {
            if (!IsManualMissileViewActive() || guidanceUnit == null || activeMissile == null || activeMissile.Info == null)
                return false;

            bool applyTuning = BMP1MCLOSAmmo.MclosInputTuning.ShouldApplyNow(true);
            BMP1MCLOSAmmo.MclosInputTuning.ApplyDynamicTurnSpeed(activeMissile.Info, horizontal, vertical, applyTuning);
            horizontal = BMP1MCLOSAmmo.MclosInputTuning.ProcessAxis(horizontal, applyTuning);
            vertical = BMP1MCLOSAmmo.MclosInputTuning.ProcessAxis(vertical, applyTuning);

            EnsureMclosGuide();
            guidanceUnit.ManualAimAngularVelocity = new Vector2(horizontal, vertical) * ManualControlSensitivity;
            activeMissile.SkipGuidanceLockout = true;
            activeMissile.Guided = true;

            if (mclosGuideTransform != null && guidanceUnit.AimElement != mclosGuideTransform)
                guidanceUnit.AimElement = mclosGuideTransform;

            return true;
        }

        private void SetManualControlActive(bool active, bool forceMissileView)
        {
            preferTvControl = active;

            if (activeMissile == null)
                return;

            if (active)
                DisableInfraredTracking();

            MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
            if (follow == null)
                return;

            if (forceMissileView)
                follow.EnterMissileView(active, GetPreferredReturnSlotForMissile());
            else
                follow.SetManualModeActive(active);
        }

        private void DisableInfraredTracking()
        {
            if (activeMissile == null)
                return;

            SpikeInfraredTracker tracker = activeMissile.GetComponent<SpikeInfraredTracker>();
            if (tracker != null)
                Destroy(tracker);
        }

        internal void OnMissileFollowReady(MarderSpikeMissileCameraFollow follow)
        {
            if (follow == null)
                return;

            SetMissileSlot(follow.MissileSlot);
            if (preferTvControl)
                follow.SetManualModeActive(true);
        }

        internal void OnMissileFollowDestroyed(MarderSpikeMissileCameraFollow follow)
        {
            if (follow == null)
                return;

#if DEBUG
            UnderdogsDebug.LogSpike($"OnMissileFollowDestroyed: follow.Round exists={follow.Round != null}, activeMissile exists={activeMissile != null}");
#endif

            if (missileSlot == follow.MissileSlot)
                SetMissileSlot(null);

            if (activeMissile == follow.Round)
                activeMissile = null;
        }

        private void EnsureAimProxy()
        {
            if (fcs == null)
                return;

            if (aimProxy == null)
            {
                Transform existing = fcs.transform.Find(AimProxyName);
                if (existing != null)
                {
                    aimProxy = existing;
                }
                else
                {
                    GameObject proxy = new GameObject(AimProxyName);
                    aimProxy = proxy.transform;
                    aimProxy.SetParent(fcs.transform, false);
                }
            }

            if (daySlot != null)
            {
                aimProxy.position = daySlot.transform.position + daySlot.transform.forward * ProxyRange;
                aimProxy.rotation = Quaternion.LookRotation(daySlot.transform.forward, Vector3.up);
            }
        }

        private void ApplyWeaponDefaults()
        {
            if (fcs != null)
            {
                if (fcs.MaxLaserRange < 4000f)
                    fcs.MaxLaserRange = 4000f;

                if (fcs.LaserOrigin == null)
                {
                    GameObject laser = new GameObject("spike lase");
                    laser.transform.SetParent(daySlot != null ? daySlot.transform : fcs.transform, false);
                    fcs.LaserOrigin = laser.transform;
                }
            }

            EnsureFnfGuide();

            if (dayOptic != null)
            {
                try { opticGuidanceField?.SetValue(dayOptic, true); } catch { }
                try { dayOptic.GuidanceLight = true; } catch { }
                try { dayOptic.enabled = true; } catch { }
                try { if (!dayOptic.gameObject.activeSelf) dayOptic.gameObject.SetActive(true); } catch { }
                SuppressOpticPresentation(dayOptic, false);
            }

            if (thermalOptic != null)
            {
                try { opticGuidanceField?.SetValue(thermalOptic, true); } catch { }
                try { thermalOptic.GuidanceLight = true; } catch { }
                try { thermalOptic.enabled = true; } catch { }
                try { if (!thermalOptic.gameObject.activeSelf) thermalOptic.gameObject.SetActive(true); } catch { }
                SuppressOpticPresentation(thermalOptic, true);
            }

            if (!opticsInitialized)
            {
                try { fcs?.RegisterOptic(dayOptic); } catch { }
                TryAssignMainAndNightOptics(dayOptic, null);
                TryAssignAuthoritativeOptic(dayOptic);
                try { daySlot.WasUsingNightMode = false; } catch { }
                try { if (thermalSlot != null) thermalSlot.WasUsingNightMode = true; } catch { }
                try { fcs?.NotifyActiveOpticChanged(dayOptic); } catch { }
                try { if (CameraSlot.ActiveInstance == thermalSlot && daySlot != null) CameraSlot.SetActiveSlot(daySlot); } catch { }
                opticsInitialized = true;
            }

            if (fcs != null && dayOptic != null && thermalOptic != null)
                UECommonUtil.InstallLaserPointCorrection(fcs, dayOptic, thermalOptic);

            if (guidanceUnit != null)
            {
                guidanceUnit.ResetAimOnLaunch = false;
                Transform desiredAimElement = guidanceMode == SpikeGuidanceMode.FnF && fnfGuideTransform != null ? fnfGuideTransform : aimProxy;
                if (desiredAimElement != null && guidanceUnit.AimElement != desiredAimElement)
                    guidanceUnit.AimElement = desiredAimElement;
            }
        }

        private void EnsureFnfGuide()
        {
            if (fcs == null || fnfGuideTransform != null)
                return;

            GameObject guide = new GameObject("SpikeFnfGuide");
            fnfGuideTransform = guide.transform;
            fnfGuideTransform.SetParent(fcs.transform, false);
            fnfGuideTransform.localPosition = new Vector3(0f, 0f, ProxyRange);
            fnfGuideTransform.localRotation = Quaternion.identity;
        }

        private void TryAssignMainAndNightOptics(UsableOptic mainOptic, UsableOptic nightOptic)
        {
            if (fcs == null)
                return;

            try { mainOpticProperty?.SetValue(fcs, mainOptic, null); } catch { }
            try { mainOpticBackingField?.SetValue(fcs, mainOptic); } catch { }
            try { nightOpticProperty?.SetValue(fcs, nightOptic, null); } catch { }
            try { nightOpticBackingField?.SetValue(fcs, nightOptic); } catch { }
        }

        private void TryAssignAuthoritativeOptic(UsableOptic optic)
        {
            if (optic == null || fcs == null)
                return;

            try { authoritativeOpticField?.SetValue(fcs, optic); } catch { }
        }

        private void EnsureInitialThermalView()
        {
            // SPIKE炮镜独立热成像：只在进入SPIKE炮镜槽位时切换到热成像
            // 不影响车长视角等其他槽位

            // 非SPIKE武器：不干预，让游戏原生槽位记忆机制工作
            if (!IsSpikeWeaponSelected())
                return;

            if (activeMissile != null)
                return;

            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            // 只在SPIKE炮镜槽位（白光或热成像槽）时处理
            if (activeSlot != daySlot && activeSlot != thermalSlot)
                return;

            // 如果当前在白光槽，切换到热成像槽
            if (activeSlot == daySlot && thermalSlot != null)
            {
                CameraSlot.SetActiveSlot(thermalSlot);
                TryAssignAuthoritativeOptic(thermalOptic);
                try { fcs?.NotifyActiveOpticChanged(thermalOptic); } catch { }
            }
        }

        private void TryEnterMissileViewIfNeeded()
        {
            // 有导弹发射后，进入SPIKE炮镜视角自动切换到导弹摄像机
            if (activeMissile == null || activeMissile.IsDestroyed)
                return;

            if (!missileViewRequested)
                return;

            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            // 只在进入SPIKE炮镜槽位时切换到导弹视角
            if (activeSlot != daySlot && activeSlot != thermalSlot)
                return;

            MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
            if (follow == null)
                return;

            // 切换到导弹视角
            follow.EnterMissileView(preferTvControl, GetPreferredReturnSlotForMissile());
            // 只执行一次
            missileViewRequested = false;
        }

        private void EnsureThermalSlot()
        {
            if (daySlot == null)
                return;

            if (thermalSlot != null)
            {
                RefreshSlotTransform(thermalSlotObject != null ? thermalSlotObject.transform : thermalSlot.transform, daySlot.transform);
                if (thermalSlot.gameObject != null)
                    thermalSlot.gameObject.name = thermalSlotName ?? "Spike Thermal";
                LinkThermalSlot();
                return;
            }

            thermalSlotObject = new GameObject(thermalSlotName ?? "Spike Thermal");
            thermalSlotObject.transform.SetParent(daySlot.transform.parent, false);
            thermalSlot = thermalSlotObject.AddComponent<CameraSlot>();
            thermalOptic = null;
            slotRegistrationDirty = true;

            RefreshSlotTransform(thermalSlotObject.transform, daySlot.transform);
            ConfigureThermalCameraSlot(thermalSlot, daySlot.DefaultFov, daySlot.OtherFovs);
            if (thermalSlot != null && thermalSlot.gameObject != null)
                thermalSlot.gameObject.name = thermalSlotName ?? "Spike Thermal";
            LinkThermalSlot();
            SetupThermalPost(thermalSlotObject, thermalSlot);
        }

        private void LinkThermalSlot()
        {
            if (daySlot == null || thermalSlot == null)
                return;

            UECommonUtil.LinkSightSlots(daySlot, thermalSlot);
            try { daySlot.enabled = true; } catch { }
            try { thermalSlot.enabled = true; } catch { }
            try { daySlot.RefreshAvailability(); } catch { }
            try { thermalSlot.RefreshAvailability(); } catch { }
        }

        private void EnsureSlotRegistration()
        {
            if (!slotRegistrationDirty)
                return;

            CameraManager cameraManager = CameraManager.Instance;
            if (cameraManager == null || f_cm_allCamSlots == null)
                return;

            CameraSlot[] slots = f_cm_allCamSlots.GetValue(cameraManager) as CameraSlot[];
            List<CameraSlot> updated = new List<CameraSlot>(slots != null ? slots.Length + 3 : 3);
            HashSet<CameraSlot> seen = new HashSet<CameraSlot>();

            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    CameraSlot slot = slots[i];
                    if (slot == null || !seen.Add(slot))
                        continue;

                    updated.Add(slot);
                }
            }

            if (daySlot != null && seen.Add(daySlot))
                updated.Add(daySlot);
            if (thermalSlot != null && seen.Add(thermalSlot))
                updated.Add(thermalSlot);
            if (missileSlot != null && seen.Add(missileSlot))
                updated.Add(missileSlot);

            bool changed = slots == null || slots.Length != updated.Count;
            if (!changed && slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != updated[i])
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
                f_cm_allCamSlots.SetValue(cameraManager, updated.ToArray());

            slotRegistrationDirty = false;
        }

        private void RefreshSlotTransform(Transform destination, Transform source)
        {
            if (destination == null || source == null)
                return;

            destination.localPosition = source.localPosition;
            destination.localRotation = source.localRotation;
            destination.localScale = source.localScale;
        }

        private void ConfigureThermalCameraSlot(CameraSlot slot, float defaultFov, float[] otherFovs)
        {
            if (slot == null)
                return;

            slot.VisionType = NightVisionType.Thermal;
            slot.IsExterior = false;
            slot.BaseBlur = 0f;
            slot.OverrideFLIRResolution = true;
            slot.CanToggleFlirPolarity = true;
            slot.FLIRWidth = GetConfiguredFlirWidth();
            slot.FLIRHeight = GetConfiguredFlirHeight();
            slot.FLIRFilterMode = FilterMode.Point;
            slot.VibrationShakeMultiplier = 0.01f;
            slot.DefaultFov = defaultFov > 0.1f ? defaultFov : 5f;
            slot.OtherFovs = otherFovs != null ? (float[])otherFovs.Clone() : new float[0];
            slot.SpriteType = daySlot != null ? daySlot.SpriteType : CameraSpriteManager.SpriteType.DefaultScope;

            Material blitMaterial = GetConfiguredBlitMaterial();
            if (blitMaterial != null)
                slot.FLIRBlitMaterialOverride = blitMaterial;
        }

        private void SetupThermalPost(GameObject slotObject, CameraSlot slot)
        {
            if (slotObject == null || slot == null)
                return;

            GameObject flirPrefab = UEResourceController.GetThermalFlirPostPrefab();
            if (flirPrefab == null)
                return;

            SimpleNightVision snv = UECommonUtil.GetOrAddComponent<SimpleNightVision>(slotObject);
            PostProcessVolume existingVolume = slotObject.GetComponent<PostProcessVolume>();
            if (existingVolume != null)
                Destroy(existingVolume);

            Transform existingPost = slotObject.transform.Find("FLIR Post Processing - Green(Clone)");
            if (existingPost != null)
                Destroy(existingPost.gameObject);

            GameObject post = Instantiate(flirPrefab, slotObject.transform);
            Transform mainVolume = post.transform.Find("MainCam Volume");
            if (mainVolume != null)
                mainVolume.gameObject.SetActive(false);

            PostProcessVolume flirOnlyVolume = post.transform.Find("FLIR Only Volume")?.GetComponent<PostProcessVolume>();
            if (flirOnlyVolume != null)
                flirOnlyVolume.enabled = true;

            try { snvPostVolumeField?.SetValue(snv, flirOnlyVolume); } catch { }
        }

        internal void ConfigureMissileViewSlot(GameObject slotObject, CameraSlot slot, UsableOptic optic)
        {
            if (slotObject == null || slot == null)
                return;

            ConfigureThermalCameraSlot(slot, 12f, new float[] { 6f, 3f });
            if (optic != null)
                SuppressOpticPresentation(optic, true);
            SetupThermalPost(slotObject, slot);
            try { slot.WasUsingNightMode = true; } catch { }
        }

        private void SuppressOpticPresentation(UsableOptic optic, bool thermal)
        {
            if (optic == null)
                return;

            try { optic.post = null; } catch { }
            try { optic.GuidanceLight = true; } catch { }

            if (thermal)
            {
                try
                {
                    if (optic.reticleMesh != null)
                        optic.reticleMesh.Clear();
                }
                catch { }

                try { optic.reticleMesh = null; } catch { }
            }

            DisableOpticReticleMeshes(optic.transform, optic);
            SetScopeSpriteRendererEnabled(optic.transform, false);
        }

        private void DisableOpticReticleMeshes(Transform root, UsableOptic optic)
        {
            if (root == null)
                return;

            ReticleMesh[] reticleMeshes = root.GetComponentsInChildren<ReticleMesh>(true);
            for (int i = 0; i < reticleMeshes.Length; i++)
            {
                ReticleMesh reticleMesh = reticleMeshes[i];
                if (reticleMesh == null)
                    continue;

                if (optic != null && reticleMesh == optic.reticleMesh)
                {
                    if (optic.transform.Find("camera") != null && reticleMesh.transform.IsChildOf(optic.transform.Find("camera")))
                        continue;
                }

                if (reticleMesh.enabled)
                    reticleMesh.enabled = false;
                if (reticleMesh.gameObject.activeSelf)
                    reticleMesh.gameObject.SetActive(false);

                SkinnedMeshRenderer smr = reticleMesh.GetComponent<SkinnedMeshRenderer>();
                if (smr != null)
                    smr.enabled = false;

                PostMeshComp[] postMeshes = reticleMesh.GetComponentsInChildren<PostMeshComp>(true);
                for (int j = 0; j < postMeshes.Length; j++)
                    if (postMeshes[j] != null)
                        postMeshes[j].enabled = false;
            }
        }

        private void SetScopeSpriteRendererEnabled(Transform root, bool enabled)
        {
            if (root == null)
                return;

            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                string combined = string.Concat(
                    renderer.name ?? string.Empty,
                    " ",
                    renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty).ToLowerInvariant();

                if (!combined.Contains("scope"))
                    continue;

                renderer.enabled = enabled;
            }
        }

        private void HandleLockInput()
        {
            // 调试：每帧检查组件状态（仅当按下中键时才输出）
            bool mmbPressed = Input.GetMouseButtonDown(2);
            if (mmbPressed )
            {
#if DEBUG
                UnderdogsDebug.LogSpike($"MMB pressed! enabled={enabled}, IsConfigured={IsConfigured}, lockConfirmed={lockConfirmed}, activeMissile={activeMissile != null}, preferTvControl={preferTvControl}");
#endif
            }

            if (!mmbPressed)
                return;

            if (!IsPlayerControllingThisRig())
            {
#if DEBUG
                UnderdogsDebug.LogSpike("HandleLockInput: not controlling rig");
#endif
                return;
            }

            CameraManager cameraManager = CameraManager.Instance;
            CameraSlot activeSlot = CameraSlot.ActiveInstance;

            // 允许导弹视角下的锁定（即使ExteriorMode为true）
            if (cameraManager != null && cameraManager.ExteriorMode && activeSlot != missileSlot)
            {
#if DEBUG
                UnderdogsDebug.LogSpike($"HandleLockInput: exterior mode, missileSlot exists={missileSlot != null}");
#endif
                return;
            }

            // 白光槽、热成像槽、导弹槽都能锁定
            if (activeSlot != thermalSlot && activeSlot != missileSlot && activeSlot != daySlot)
            {
#if DEBUG
                UnderdogsDebug.LogSpike($"HandleLockInput: wrong slot, thermalSlot exists={thermalSlot != null}, daySlot exists={daySlot != null}, missileSlot exists={missileSlot != null}");
#endif
                return;
            }

            if (HasLockTarget())
            {
                ClearLockTarget("user_manual_clear");
                if (activeMissile != null)
                    SetManualControlActive(true, true);

#if DEBUG
                UnderdogsDebug.LogSpike("Lock cleared");
#endif
                return;
            }

            // MCLOS模式下且导弹已发射：中键切换到导弹视角并请求锁定
            if (preferTvControl && activeMissile != null)
            {
                MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
                if (follow != null)
                {
                    // 切换到导弹视角
                    follow.EnterMissileView(true, GetPreferredReturnSlotForMissile());
                    // 请求锁定（通过导弹视角的摄像机进行射线检测）
                    missileViewRequested = true;
                }
            }

            lockRequestQueued = true;
#if DEBUG
            UnderdogsDebug.LogSpike("Lock requested");
#endif
        }

        private void HandleModeToggleInput()
        {
            if (activeMissile == null)
                return;

            if (!Input.GetKeyDown(KeyCode.V))
                return;

            if (!IsSpikeWeaponSelected())
                return;

            CameraManager cameraManager = CameraManager.Instance;
            if (cameraManager != null && cameraManager.ExteriorMode)
                return;

            MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
            if (follow == null)
                return;

            if (!preferTvControl)
            {
                SetManualControlActive(true, true);
#if DEBUG
                UnderdogsDebug.LogSpike("Mode: FnF → MCLOS");
#endif
                return;
            }

            if (preferTvControl && HasLockTarget())
            {
                SetManualControlActive(false, false);
#if DEBUG
                UnderdogsDebug.LogSpike("Mode: MCLOS → FnF");
#endif
                return;
            }

#if DEBUG
            UnderdogsDebug.LogSpikeWarning("Cannot enter FnF: no lock");

            UnderdogsDebug.LogSpike($"Mode toggle => {(preferTvControl ? "MCLOS" : "FnF")}");
#endif
        }

        private MarderSpikeMissileCameraFollow GetMissileFollow(LiveRound missile = null)
        {
            LiveRound targetMissile = missile ?? activeMissile;
            return targetMissile != null ? targetMissile.GetComponent<MarderSpikeMissileCameraFollow>() : null;
        }

        private void SyncMissileFollow(LiveRound missile = null)
        {
            MarderSpikeMissileCameraFollow follow = GetMissileFollow(missile);
            SetMissileSlot(follow != null ? follow.MissileSlot : null);
            EnsureMclosGuide();
        }

        private void EnsureMclosGuide()
        {
            mclosGuideTransform = activeMissile != null ? aimProxy : null;
        }

        private void CacheLatestNonMissileSlot()
        {
            if (!IsPlayerControllingThisRig())
                return;

            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            if (activeSlot == null)
                return;

            if (activeSlot == missileSlot)
                return;

            if (!SlotBelongsToVehicle(activeSlot))
                return;

            latestNonMissileSlot = activeSlot;
            // 白光槽或热成像槽都保留
            if (activeSlot == thermalSlot || activeSlot == daySlot)
                preLaunchSightSlot = activeSlot;
        }

        private void CachePreLaunchSightSlot()
        {
            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            // 白光槽或热成像槽都保留
            if (activeSlot == thermalSlot || activeSlot == daySlot)
            {
                preLaunchSightSlot = activeSlot;
                return;
            }

            if (latestNonMissileSlot == thermalSlot || latestNonMissileSlot == daySlot)
            {
                preLaunchSightSlot = latestNonMissileSlot;
                return;
            }

            if (preLaunchSightSlot == null)
                preLaunchSightSlot = thermalSlot;
        }

        private void UpdateMissileReference()
        {
            LiveRound currentMissile = ResolveCurrentMissile();

            if (currentMissile == null)
            {
                // 只有之前确实有活跃导弹（已发射状态）才清除锁定
                // 发射前（activeMissile == null）不应该清除锁定
                if (activeMissile != null)
                {
#if DEBUG
                    UnderdogsDebug.LogSpike($"Missile destroyed: activeMissile exists, clearing states");
#endif

                    activeMissile = null;
                    preferTvControl = false;
                    closestMissileTargetDistance = float.PositiveInfinity;
                    SetMissileSlot(null);
                    // 重置锁定状态
                    ClearLockTarget("missile_destroyed");
                }
                return;
            }

            bool changed = currentMissile != activeMissile;
            activeMissile = currentMissile;
            if (changed)
                closestMissileTargetDistance = float.PositiveInfinity;
            SyncMissileFollow(activeMissile);

            if (changed && !preferTvControl)
                TryReturnToPreferredSlot();
        }

        private LiveRound ResolveCurrentMissile()
        {
            if (guidanceUnit != null && guidanceUnit.CurrentMissiles != null)
            {
                for (int i = 0; i < guidanceUnit.CurrentMissiles.Count; i++)
                {
                    LiveRound round = guidanceUnit.CurrentMissiles[i];
                    if (round != null && !round.IsDestroyed)
                        return round;
                }
            }

            List<LiveRound> unguided = f_gu_unguidedMissiles != null ? f_gu_unguidedMissiles.GetValue(guidanceUnit) as List<LiveRound> : null;
            if (unguided != null)
            {
                for (int i = 0; i < unguided.Count; i++)
                {
                    LiveRound round = unguided[i];
                    if (round != null && !round.IsDestroyed)
                        return round;
                }
            }

            if (activeMissile != null && !activeMissile.IsDestroyed)
                return activeMissile;

            if (Time.unscaledTime < nextSceneMissileSearchTime)
                return null;

            LiveRound sceneMissile = FindSceneMissile();
            if (sceneMissile != null)
                return sceneMissile;

            return null;
        }

        private LiveRound FindSceneMissile()
        {
            nextSceneMissileSearchTime = Time.unscaledTime + SceneMissileSearchInterval;
            LiveRound[] rounds = Resources.FindObjectsOfTypeAll<LiveRound>();
            for (int i = 0; i < rounds.Length; i++)
            {
                LiveRound round = rounds[i];
                if (round == null || round.IsDestroyed || round.Info == null)
                    continue;

                if (!MarderSpikeAmmo.IsSpikeAmmoName(round.Info.Name) && !MarderSpikeAmmo.IsOriginalMilanName(round.Info.Name))
                    continue;

                Vehicle shooterVehicle = round.Shooter != null ? round.Shooter.GetComponentInParent<Vehicle>() : null;
                if (shooterVehicle != vehicle)
                    continue;

                return round;
            }

            return null;
        }

        private void EnsureMissileSlot(LiveRound missile)
        {
            if (missile == null)
                return;

            if (missileSlotObject == null)
            {
                GameObject donor = thermalOptic != null ? thermalOptic.gameObject : dayOptic != null ? dayOptic.gameObject : null;
                if (donor != null)
                {
                    missileSlotObject = Instantiate(donor, vehicle.transform);
                    missileSlotObject.name = MissileSlotName;
                    missileOptic = missileSlotObject.GetComponent<UsableOptic>();
                    missileSlot = missileOptic != null ? missileOptic.slot : missileSlotObject.GetComponent<CameraSlot>();
                }
                else
                {
                    missileSlotObject = new GameObject(MissileSlotName);
                    missileSlotObject.transform.SetParent(vehicle != null ? vehicle.transform : transform, false);
                    missileSlot = missileSlotObject.AddComponent<CameraSlot>();
                }

                ConfigureThermalCameraSlot(missileSlot, 12f, new float[] { 6f, 3f });
                if (missileOptic != null)
                    SuppressOpticPresentation(missileOptic, true);
                SetupThermalPost(missileSlotObject, missileSlot);
            }

            UpdateMissileSlotPose();
        }

        private void UpdateMissileSlotPose()
        {
            if (activeMissile == null || missileSlotObject == null)
                return;

            Transform missileTransform = activeMissile.transform;
            if (missileTransform == null)
                return;

            missileSlotObject.transform.position = missileTransform.TransformPoint(new Vector3(0f, 0.02f, -0.12f));
            missileSlotObject.transform.rotation = missileTransform.rotation;
        }

        private void TryReturnToPreferredSlot()
        {
            CameraSlot targetSlot = FindPreferredReturnSlot();
            if (targetSlot != null && CameraSlot.ActiveInstance != targetSlot)
                CameraSlot.SetActiveSlot(targetSlot);
        }

        private CameraSlot FindPreferredReturnSlot()
        {
            if (preLaunchSightSlot != null && preLaunchSightSlot != missileSlot)
                return preLaunchSightSlot;

            if (latestNonMissileSlot != null && latestNonMissileSlot != missileSlot)
                return latestNonMissileSlot;

            CameraSlot[] slots = vehicle != null ? vehicle.GetComponentsInChildren<CameraSlot>(true) : new CameraSlot[0];
            CameraSlot preferred = slots.FirstOrDefault(IsCommanderLikeSlot);
            if (preferred != null && preferred != missileSlot)
                return preferred;

            // 允许车长视角作为回退（不再排除IsExterior，因为车长视角通常是外部视角）
            return slots.FirstOrDefault(slot => slot != null && slot != missileSlot && slot != thermalSlot && slot != daySlot)
                ?? daySlot
                ?? thermalSlot;
        }

        private void UpdateGuidanceMode()
        {
            if (!IsPlayerControllingThisRig() && activeMissile == null)
            {
                guidanceMode = SpikeGuidanceMode.Idle;
                return;
            }

            if (activeMissile == null)
            {
                guidanceMode = HasLockTarget() ? SpikeGuidanceMode.PreLaunchLock : SpikeGuidanceMode.Idle;
                return;
            }

            if (preferTvControl)
            {
                guidanceMode = SpikeGuidanceMode.MCLOS;
                return;
            }

            guidanceMode = HasLockTarget() ? SpikeGuidanceMode.FnF : SpikeGuidanceMode.MCLOS;
        }

        private void UpdateLockTarget()
        {
            bool canControlLock = CanControlSpikeLock();

            if (!canControlLock)
            {
                if (lockRequestQueued)
                {
#if DEBUG
                    UnderdogsDebug.LogSpike($"CanControlSpikeLock=false, dropping lock request");
#endif
                    lockRequestQueued = false;
                }
                if (activeMissile == null)
                    ClearLockTarget("can_control_lock_false");
                return;
            }

            if (lockRequestQueued)
            {
                lockRequestQueued = false;
#if DEBUG
                UnderdogsDebug.LogSpike($"=== START LOCK REQUEST ===");
#endif

                try
                {
                    Vehicle targetVehicle = null;
                    Transform targetAnchor = null;
                    Renderer targetRenderer = null;
                    float targetDistance = float.PositiveInfinity;

                    Transform refTransform = null;
                    Vector3 aimDirection = Vector3.forward;

                    // 检查是否在导弹视角，使用导弹摄像机进行锁定检测
                    CameraSlot activeSlot = CameraSlot.ActiveInstance;
                    bool isMissileView = activeSlot == missileSlot && activeMissile != null;
                    if (isMissileView)
                    {
                        // 导弹视角：使用导弹的位置和方向
                        refTransform = activeMissile.transform;
                        aimDirection = activeMissile.transform.forward;
#if DEBUG
                        UnderdogsDebug.LogSpike($"Locking from missile view");
#endif
                    }
                    else
                    {
                        // 炮镜视角：使用FCS参考点
                        refTransform = fcs?.ReferenceTransform;
                        aimDirection = fcs != null ? fcs.AimWorldVector : Vector3.forward;
#if DEBUG
                        UnderdogsDebug.LogSpike($"Locking from optic view");
#endif
                    }

                if (refTransform == null)
                {
                    Camera camera = CameraManager.MainCam;
                    if (camera != null)
                        refTransform = camera.transform;

#if DEBUG
                    UnderdogsDebug.LogSpike($"refTransform fallback to MainCam: {refTransform != null}");
#endif
                }

                if (refTransform == null)
                {
#if DEBUG
                    UnderdogsDebug.LogSpikeWarning($"refTransform is null, skipping raycast");
#endif
                }

                if (refTransform != null)
                {
#if DEBUG
                    UnderdogsDebug.LogSpike($"Performing raycast from origin={refTransform.position}");
#endif
                    Vector3 origin = refTransform.position;
                    // 注意：fcs.AimWorldVector 和导弹transform.forward 已经是世界空间方向
                    Vector3 direction = (activeSlot == missileSlot && activeMissile != null)
                        ? activeMissile.transform.forward  // 导弹视角
                        : (fcs != null ? fcs.AimWorldVector : refTransform.forward);  // 炮镜视角
                    Ray ray = new Ray(origin, direction);
                    RaycastHit hit;

                    // LayerMask只检测载具层(14)和地形层(18)
                    int vehicleLayer = 1 << 14;
                    int terrainLayer = 1 << 18;
                    int layerMask = vehicleLayer | terrainLayer;

                    // 使用 SphereCast 增加锁定容错半径
#if DEBUG
                    if (Physics.SphereCast(ray, UnderdogsDebug.SpikeLockRadius, out hit, LockRange, layerMask))
#else
                    if (Physics.SphereCast(ray, 2.0f, out hit, LockRange, layerMask))
#endif
                    {
                        targetVehicle = hit.collider != null ? hit.collider.GetComponentInParent<Vehicle>() : null;
#if DEBUG
                        UnderdogsDebug.LogSpike($"Raycast hit: collider exists={hit.collider != null}, vehicle exists={targetVehicle != null}, selfVehicle exists={vehicle != null}");
#endif

                        if (targetVehicle == vehicle)
                        {
#if DEBUG
                            UnderdogsDebug.LogSpike($"Hit self vehicle, ignoring");
#endif
                            targetVehicle = null;
                        }

                        if (targetVehicle != null)
                        {
                            targetAnchor = ResolveTrackingAnchor(targetVehicle, hit.collider != null ? hit.collider.transform : null);
                            targetRenderer = ResolveTrackingRenderer(targetVehicle, targetAnchor);
                            targetDistance = hit.distance;
#if DEBUG
                            UnderdogsDebug.LogSpike($"Target resolved: anchor exists={targetAnchor != null}, renderer exists={targetRenderer != null}, dist={targetDistance:F1}");
#endif
                        }
                    }
#if DEBUG
                    else
                    {
                        UnderdogsDebug.LogSpikeWarning($"Raycast missed, origin={origin}, direction={direction}");
                    }
#endif
                }
#if DEBUG
                else
                {
                    UnderdogsDebug.LogSpike($"Skipping raycast (refTransform null)");
                }
#endif

#if DEBUG
                    UnderdogsDebug.LogSpike($"Raycast complete: targetVehicle exists={targetVehicle != null}");
#endif

                    if (targetVehicle != null)
                    {
#if DEBUG
                        UnderdogsDebug.LogSpike($"Applying lock target...");
#endif
                        ApplyLockTarget(targetVehicle, targetAnchor, targetRenderer, targetDistance);
                        if (TryGetLockAimPoint(out Vector3 aimPoint))
                        {
                            lastKnownAimPoint = aimPoint;
                            hasLastKnownAimPoint = true;
                        }

                        lockConfirmed = true;

                        if (activeMissile != null && preferTvControl)
                        {
                            preferTvControl = false;
                            MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
                            if (follow != null)
                                follow.SetManualModeActive(false);
#if DEBUG
                            UnderdogsDebug.LogSpike($"Auto switch: MCLOS → FnF (lock acquired)");
#endif
                        }

#if DEBUG
                        UnderdogsDebug.LogSpike($"Lock SUCCESS: dist={lockedDistance:F1} mode={guidanceMode}");
#endif
                    }
                    else if (HasLockTarget())
                    {
#if DEBUG
                        UnderdogsDebug.LogSpike($"No target hit but had previous lock, clearing...");
#endif
                        ClearLockTarget("raycast_missed");
#if DEBUG
                        UnderdogsDebug.LogSpike("Lock cleared");
#endif
                    }

#if DEBUG
                    UnderdogsDebug.LogSpike($"=== END LOCK REQUEST ===");
#endif
                }
                catch (System.Exception ex)
                {
#if DEBUG
                    UnderdogsDebug.LogSpikeWarning($"Exception in lock request: {ex.Message}");
                    UnderdogsDebug.LogSpikeWarning($"Stack: {ex.StackTrace}");
#endif
                }

                return;
            }

            if (HasLockTarget())
            {
                if (TryGetLockAimPoint(out Vector3 preservedAimPoint))
                {
                    lastKnownAimPoint = preservedAimPoint;
                    hasLastKnownAimPoint = true;
                    RefreshLockedDistance(preservedAimPoint);
                }
                else if (activeMissile == null)
                {
                    ClearLockTarget("aim_point_lost");
                    return;
                }

                lockConfirmed = true;
            }
        }

        private void UpdateTargetPassFallback()
        {
            if (activeMissile == null || preferTvControl)
            {
                closestMissileTargetDistance = float.PositiveInfinity;
                return;
            }

            Vector3 aimPoint;
            if (!TryGetLockAimPoint(out aimPoint))
            {
                if (!hasLastKnownAimPoint)
                {
                    closestMissileTargetDistance = float.PositiveInfinity;
                    return;
                }

                aimPoint = lastKnownAimPoint;
            }

            float currentDistance = Vector3.Distance(activeMissile.transform.position, aimPoint);
            if (float.IsInfinity(closestMissileTargetDistance) || currentDistance < closestMissileTargetDistance)
            {
                closestMissileTargetDistance = currentDistance;
                return;
            }

            if (currentDistance <= closestMissileTargetDistance + PassedTargetDistanceThreshold)
                return;

            closestMissileTargetDistance = currentDistance;
            SetManualControlActive(true, true);

#if DEBUG
            UnderdogsDebug.LogSpike("Auto switch: FnF → MCLOS (passed target)");
#endif
        }

        private void ApplyLockTarget(Vehicle targetVehicle, Transform targetAnchor, Renderer targetRenderer, float targetDistance)
        {
            lockedVehicle = targetVehicle;
            lockedAnchor = targetAnchor;
            lockedRenderer = targetRenderer;
            lockedCollider = targetAnchor != null ? targetAnchor.GetComponent<Collider>() : null;
            lockedDistance = targetDistance;
            lockedColliders = targetVehicle != null ? targetVehicle.GetComponentsInChildren<Collider>(true) : null;
            lockedRenderers = targetVehicle != null ? targetVehicle.GetComponentsInChildren<Renderer>(true) : null;
            hasCachedTargetBounds = false;
            hasCachedTargetScreenRect = false;
            nextTargetBoundsRefreshTime = 0f;
            nextTargetScreenRectRefreshTime = 0f;
            closestMissileTargetDistance = float.PositiveInfinity;
        }

        private void RefreshLockedDistance(Vector3 aimPoint)
        {
            Transform referenceTransform = fcs != null && fcs.ReferenceTransform != null ? fcs.ReferenceTransform : null;
            if (referenceTransform != null)
            {
                lockedDistance = Vector3.Distance(referenceTransform.position, aimPoint);
                return;
            }

            Camera camera = CameraManager.MainCam;
            if (camera != null)
                lockedDistance = Vector3.Distance(camera.transform.position, aimPoint);
        }

        private void ClearLockTarget(string reason = "unknown")
        {
            if (lockConfirmed)
#if DEBUG
                UnderdogsDebug.LogSpike($"ClearLockTarget called: reason={reason}, hadLock={lockConfirmed}, lockedVehicle exists={lockedVehicle != null}");
#endif

            lockedVehicle = null;
            lockedAnchor = null;
            lockedRenderer = null;
            lockedCollider = null;
            lockedColliders = null;
            lockedRenderers = null;
            lockedDistance = float.PositiveInfinity;
            lockConfirmed = false;
            hasLastKnownAimPoint = false;
            lastKnownAimPoint = Vector3.zero;
            hasCachedTargetBounds = false;
            cachedTargetBounds = default(Bounds);
            hasCachedTargetScreenRect = false;
            cachedTargetScreenRect = new Rect();
            nextTargetBoundsRefreshTime = 0f;
            nextTargetScreenRectRefreshTime = 0f;
            closestMissileTargetDistance = float.PositiveInfinity;

            // 强制隐藏锁定框
            if (lockBoxRect != null && lockBoxRect.gameObject.activeSelf)
                lockBoxRect.gameObject.SetActive(false);
        }

        private Transform ResolveTrackingAnchor(Vehicle targetVehicle, Transform fallback)
        {
            if (targetVehicle == null)
                return fallback;

            Transform trackingObject = targetVehicle.transform.Find("TRACKING OBJECT");
            if (trackingObject == null)
            {
                Transform[] transforms = targetVehicle.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform candidate = transforms[i];
                    if (candidate != null && candidate.name.IndexOf("TRACKING", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        trackingObject = candidate;
                        break;
                    }
                }
            }

            if (trackingObject == null)
                MarderSpikeTrackingDimensions.TryEnsureFallbackAnchor(targetVehicle, out trackingObject);

            return trackingObject != null ? trackingObject : fallback;
        }

        private Renderer ResolveTrackingRenderer(Vehicle targetVehicle, Transform preferredAnchor)
        {
            if (preferredAnchor != null && !MarderSpikeTrackingDimensions.IsFallbackAnchor(preferredAnchor))
            {
                Renderer preferredRenderer = preferredAnchor.GetComponent<Renderer>() ?? preferredAnchor.GetComponentInChildren<Renderer>(true);
                if (preferredRenderer != null)
                    return preferredRenderer;
            }

            if (targetVehicle == null)
                return null;

            Renderer[] renderers = targetVehicle.GetComponentsInChildren<Renderer>(true);
            Renderer largestRenderer = null;
            float largestSize = 0f;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                float rendererSize = renderer.bounds.size.sqrMagnitude;
                if (largestRenderer == null || rendererSize > largestSize)
                {
                    largestRenderer = renderer;
                    largestSize = rendererSize;
                }
            }

            return largestRenderer;
        }

        private void UpdateAimProxy()
        {
            if (aimProxy == null)
                return;

            Camera activeCamera = CameraManager.MainCam;
            Vector3 aimPoint;

            if (guidanceMode == SpikeGuidanceMode.FnF && TryGetLockAimPoint(out aimPoint))
            {
            }
            else if (guidanceMode == SpikeGuidanceMode.PreLaunchLock && TryGetLockAimPoint(out aimPoint))
            {
            }
            else if (activeCamera != null)
            {
                Ray ray = new Ray(activeCamera.transform.position, activeCamera.transform.forward);
                RaycastHit hit;
                aimPoint = Physics.Raycast(ray, out hit, LockRange) ? hit.point : ray.origin + ray.direction * ProxyRange;
            }
            else if (daySlot != null)
            {
                aimPoint = daySlot.transform.position + daySlot.transform.forward * ProxyRange;
            }
            else
            {
                aimPoint = transform.position + transform.forward * ProxyRange;
            }

            aimProxy.position = aimPoint;

            if (guidanceMode == SpikeGuidanceMode.MCLOS && activeCamera != null)
            {
                aimProxy.rotation = Quaternion.LookRotation(activeCamera.transform.forward, activeCamera.transform.up);
                return;
            }

            Vector3 lookFrom = fcs != null && fcs.ReferenceTransform != null ? fcs.ReferenceTransform.position : transform.position;
            Vector3 forward = aimPoint - lookFrom;
            if (forward.sqrMagnitude > 0.001f)
            {
                aimProxy.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                if (fnfGuideTransform != null)
                {
                    fnfGuideTransform.position = aimPoint;
                    fnfGuideTransform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                }
            }
        }

        private void UpdateMissileControl()
        {
            if (guidanceUnit == null)
                return;

            if (guidanceMode == SpikeGuidanceMode.MCLOS && activeMissile != null)
            {
                EnsureMclosGuide();
                activeMissile.SkipGuidanceLockout = true;
                activeMissile.Guided = true;

                if (mclosGuideTransform != null && guidanceUnit.AimElement != mclosGuideTransform)
                    guidanceUnit.AimElement = mclosGuideTransform;
                return;
            }

            if (guidanceUnit.ManualAimAngularVelocity != Vector2.zero)
                guidanceUnit.ManualAimAngularVelocity = Vector2.zero;

            if (activeMissile != null && activeMissile.Info != null)
                BMP1MCLOSAmmo.MclosInputTuning.ApplyDynamicTurnSpeed(activeMissile.Info, 0f, 0f, false);

            if (guidanceMode == SpikeGuidanceMode.FnF && activeMissile != null)
            {
                SpikeInfraredTracker tracker = activeMissile.GetComponent<SpikeInfraredTracker>();
                if (tracker == null)
                {
                    tracker = activeMissile.gameObject.AddComponent<SpikeInfraredTracker>();
                    if (HasLockTarget())
                        tracker.SetInitialLock(lockedVehicle, lockedAnchor);
                }

                if (tracker.MissedTarget && !preferTvControl)
                {
                    SetManualControlActive(true, true);
#if DEBUG
                    UnderdogsDebug.LogSpike("Auto switch: FnF → MCLOS (missed target)");
#endif
                }

                activeMissile.SkipGuidanceLockout = true;
                activeMissile.Guided = true;

                if (fnfGuideTransform != null && guidanceUnit.AimElement != fnfGuideTransform)
                    guidanceUnit.AimElement = fnfGuideTransform;
            }
        }

        private bool TryGetLockAimPoint(out Vector3 aimPoint)
        {
            if (TryGetTargetBounds(out Bounds bounds))
            {
                aimPoint = bounds.center;
                return true;
            }

            if (lockedRenderer != null)
            {
                aimPoint = lockedRenderer.bounds.center;
                return true;
            }

            if (lockedAnchor != null)
            {
                Renderer anchorRenderer = lockedAnchor.GetComponent<Renderer>() ?? lockedAnchor.GetComponentInChildren<Renderer>(true);
                if (anchorRenderer != null)
                {
                    aimPoint = anchorRenderer.bounds.center;
                    return true;
                }

                Collider anchorCollider = lockedAnchor.GetComponent<Collider>() ?? lockedAnchor.GetComponentInChildren<Collider>(true);
                if (anchorCollider != null)
                {
                    aimPoint = anchorCollider.bounds.center;
                    return true;
                }

                aimPoint = lockedAnchor.position;
                return true;
            }

            if (hasLastKnownAimPoint)
            {
                aimPoint = lastKnownAimPoint;
                return true;
            }

            aimPoint = Vector3.zero;
            return false;
        }

        private bool TryGetVehicleBoundsCenter(Vehicle targetVehicle, out Vector3 center)
        {
            if (targetVehicle == lockedVehicle && TryGetTargetBounds(out Bounds cachedBounds))
            {
                center = cachedBounds.center;
                return true;
            }

            center = Vector3.zero;
            return false;
        }

        private bool TryGetTargetBounds(out Bounds bounds)
        {
            if (hasCachedTargetBounds && Time.unscaledTime < nextTargetBoundsRefreshTime)
            {
                bounds = cachedTargetBounds;
                return true;
            }

            if (lockedRenderer != null)
            {
                bounds = lockedRenderer.bounds;
                CacheTargetBounds(bounds);
                return true;
            }

            if (lockedCollider != null && lockedCollider.enabled && !lockedCollider.isTrigger)
            {
                bounds = lockedCollider.bounds;
                CacheTargetBounds(bounds);
                return true;
            }

            if (MarderSpikeTrackingDimensions.TryGetFallbackBounds(lockedAnchor, out bounds))
            {
                CacheTargetBounds(bounds);
                return true;
            }

            if (TryBuildBoundsFromColliders(out bounds) || TryBuildBoundsFromRenderers(out bounds))
            {
                CacheTargetBounds(bounds);
                return true;
            }

            if (hasCachedTargetBounds)
            {
                bounds = cachedTargetBounds;
                return true;
            }

            // 失败时也缓存一段时间，避免每帧都尝试（导致日志刷屏）
            bounds = default(Bounds);
            hasCachedTargetBounds = true;
            nextTargetBoundsRefreshTime = Time.unscaledTime + 1f; // 0.5秒后重试
            return false;
        }

        private void CacheTargetBounds(Bounds bounds)
        {
            cachedTargetBounds = bounds;
            hasCachedTargetBounds = true;
            nextTargetBoundsRefreshTime = Time.unscaledTime + LockBoundsRefreshInterval;
            hasCachedTargetScreenRect = false;
            nextTargetScreenRectRefreshTime = 0f;
        }

        private bool TryBuildBoundsFromColliders(out Bounds bounds)
        {
            if (lockedColliders != null && lockedColliders.Length > 0)
            {
                bool initialized = false;
                bounds = new Bounds();
                for (int i = 0; i < lockedColliders.Length; i++)
                {
                    Collider collider = lockedColliders[i];
                    if (collider == null || collider.isTrigger)
                        continue;

                    if (!initialized)
                    {
                        bounds = collider.bounds;
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                }

                if (initialized)
                    return true;
            }

            bounds = default(Bounds);
            return false;
        }

        private bool TryBuildBoundsFromRenderers(out Bounds bounds)
        {
            if (lockedRenderers != null && lockedRenderers.Length > 0)
            {
                bool initialized = false;
                bounds = new Bounds();
                for (int i = 0; i < lockedRenderers.Length; i++)
                {
                    Renderer renderer = lockedRenderers[i];
                    if (renderer == null)
                        continue;

                    if (!initialized)
                    {
                        bounds = renderer.bounds;
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                if (initialized)
                    return true;
            }

            bounds = default(Bounds);
            return false;
        }

        private void EnsureOverlay()
        {
            if (overlayObject != null)
                return;

            overlayObject = new GameObject("MissileReticleCanvas");
            overlayObject.transform.SetParent(transform, false);

            Canvas canvas = overlayObject.AddComponent<Canvas>();
            ApplyBmp1CanvasPreset(canvas);

            CanvasScaler scaler = overlayObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GraphicRaycaster raycaster = overlayObject.AddComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            overlayRoot = CreateRect("Root", overlayObject.transform, Vector2.zero, new Vector2(1920f, 1080f));
            overlayReticleRoot = CreateRect("Reticle", overlayRoot, Vector2.zero, new Vector2(400f, 400f));
            CreateCenterCross(overlayReticleRoot);
            overlayTrackingRoot = CreateRect("TRACKING GATE HOLDER", overlayRoot, Vector2.zero, new Vector2(1920f, 1080f));
            lockBoxRect = CreateRect("GATE", overlayTrackingRoot, Vector2.zero, new Vector2(200f, 140f));
            CreateCornerBox(lockBoxRect, 34f, 5f);
            lockBoxRect.gameObject.SetActive(false);
            overlayObject.SetActive(false);
        }

        private void ApplyBmp1CanvasPreset(Canvas canvas)
        {
            if (canvas == null)
                return;

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = false;
            canvas.sortingOrder = 0;
            canvas.planeDistance = 1f;
            canvas.worldCamera = null;
        }

        private void UpdateOverlay()
        {
            if (overlayObject == null || overlayRoot == null)
                return;

            bool active = IsAnySpikeViewActive();
            if (overlayObject.activeSelf != active)
                overlayObject.SetActive(active);

            if (!active)
                return;

            Rect targetRect;
            bool hasTargetRect = TryGetTargetScreenRect(out targetRect);
            bool hasLock = HasLockTarget();

            if (!hasLock && !hasTargetRect)
            {
                lockBoxRect.gameObject.SetActive(false);
                return;
            }

            float fallbackSize = GetDistanceScaledLockSize();
            Vector2 size = new Vector2(fallbackSize, fallbackSize * 0.68f);
            Vector2 anchoredPosition = Vector2.zero;

            if (hasTargetRect)
            {
                size.x = Mathf.Max(size.x, targetRect.width);
                size.y = Mathf.Max(size.y, targetRect.height);
                anchoredPosition = ScreenToCanvas(targetRect.center, overlayRoot.sizeDelta);
            }
            else if (hasLock)
            {
                // 有锁定但目标不在视野内：显示在准星位置（中心）
                anchoredPosition = Vector2.zero;
            }
            else
            {
                // 无锁定也无目标矩形：隐藏
                lockBoxRect.gameObject.SetActive(false);
                return;
            }

            lockBoxRect.sizeDelta = size;
            lockBoxRect.anchoredPosition = anchoredPosition;
            if (!lockBoxRect.gameObject.activeSelf)
                lockBoxRect.gameObject.SetActive(true);
        }

        private bool TryGetTargetScreenRect(out Rect rect)
        {
            if (hasCachedTargetScreenRect && Time.unscaledTime < nextTargetScreenRectRefreshTime)
            {
                rect = cachedTargetScreenRect;
                return rect.width > 1f && rect.height > 1f;
            }

            rect = new Rect();
            // 使用主相机进行屏幕坐标转换（Canvas是ScreenSpaceOverlay）
            Camera camera = CameraManager.MainCam;
            if (camera == null)
                return false;

            Bounds bounds;
            if (!TryGetTargetBounds(out bounds))
                return false;

            targetBoundsCorners[0] = new Vector3(bounds.min.x, bounds.min.y, bounds.min.z);
            targetBoundsCorners[1] = new Vector3(bounds.min.x, bounds.min.y, bounds.max.z);
            targetBoundsCorners[2] = new Vector3(bounds.min.x, bounds.max.y, bounds.min.z);
            targetBoundsCorners[3] = new Vector3(bounds.min.x, bounds.max.y, bounds.max.z);
            targetBoundsCorners[4] = new Vector3(bounds.max.x, bounds.min.y, bounds.min.z);
            targetBoundsCorners[5] = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
            targetBoundsCorners[6] = new Vector3(bounds.max.x, bounds.max.y, bounds.min.z);
            targetBoundsCorners[7] = new Vector3(bounds.max.x, bounds.max.y, bounds.max.z);

            bool anyPoint = false;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (int i = 0; i < targetBoundsCorners.Length; i++)
            {
                Vector3 screen = camera.WorldToScreenPoint(targetBoundsCorners[i]);
                if (screen.z <= 0f)
                    continue;

                anyPoint = true;
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }

            if (!anyPoint)
                return false;

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            cachedTargetScreenRect = rect;
            hasCachedTargetScreenRect = true;
            nextTargetScreenRectRefreshTime = Time.unscaledTime + LockScreenRectRefreshInterval;
            return rect.width > 1f && rect.height > 1f;
        }

        private float GetDistanceScaledLockSize()
        {
            float distance = float.IsInfinity(lockedDistance) ? 4000f : lockedDistance;
            float t = Mathf.InverseLerp(4000f, 150f, distance);
            return Mathf.Lerp(72f, 260f, t);
        }

        private Vector2 ScreenToCanvas(Vector2 screenPoint, Vector2 canvasSize)
        {
            float x = (screenPoint.x / Mathf.Max(Screen.width, 1)) - 0.5f;
            float y = (screenPoint.y / Mathf.Max(Screen.height, 1)) - 0.5f;
            return new Vector2(x * canvasSize.x, y * canvasSize.y);
        }

        private bool HasLockTarget()
        {
            bool result = lockConfirmed && (lockedVehicle != null || lockedRenderer != null || lockedAnchor != null || hasLastKnownAimPoint);
            return result;
        }

        private bool CanControlSpikeLock()
        {
            bool newResult;

            if (!IsPlayerControllingThisRig())
            {
                newResult = false;
            }
            else
            {
                CameraManager cameraManager = CameraManager.Instance;
                CameraSlot active = CameraSlot.ActiveInstance;

                // 允许导弹视角下的锁定（即使ExteriorMode为true）
                if (cameraManager != null && cameraManager.ExteriorMode && active != missileSlot)
                {
                    newResult = false;
                }
                else if (active == missileSlot)
                {
                    newResult = activeMissile != null;
                }
                else
                {
                    bool weaponSelected = IsSpikeWeaponSelected();
                    bool validSlot = active == thermalSlot || active == daySlot;
                    newResult = weaponSelected && validSlot;
                }
            }

            // 只在变化时输出日志，或有锁定请求时
            if (lockRequestQueued)
            {
                UnderdogsDebug.LogSpike($"CanControlSpikeLock={newResult}: playerControl={IsPlayerControllingThisRig()}, weaponSelected={IsSpikeWeaponSelected()}");
            }
            else if (newResult != lastCanControlSpikeLock )
            {
                lastCanControlSpikeLock = newResult;
                UnderdogsDebug.LogSpike($"CanControlSpikeLock changed to {newResult}");
            }

            return newResult;
        }

        private bool IsAnySpikeViewActive()
        {
            CameraSlot active = CameraSlot.ActiveInstance;
            if (active == null)
                return false;

            // 导弹视角始终显示UI（只要有活跃导弹）
            if (active == missileSlot && activeMissile != null)
                return true;

            // 炮镜视角需要检查武器选择
            if (!IsSpikeWeaponSelected())
                return false;

            // 白光槽或热成像槽都算Spike视角
            return active == thermalSlot || active == daySlot;
        }

        private bool IsSpikeWeaponSelected()
        {
            if (!IsPlayerControllingThisRig())
                return false;

            GHPC.Player.PlayerInput playerInput = GHPC.Player.PlayerInput.Instance;
            WeaponSystemInfo currentWeapon = playerInput != null ? playerInput.CurrentPlayerWeapon : null;
            if (currentWeapon == null)
                return false;

            if (weaponInfo != null && currentWeapon == weaponInfo)
                return true;

            return currentWeapon.Weapon != null && currentWeapon.Weapon == weapon;
        }

        private bool SlotBelongsToVehicle(CameraSlot slot)
        {
            return slot != null && vehicle != null && slot.transform != null && slot.transform.IsChildOf(vehicle.transform);
        }

        private static bool IsCommanderLikeSlot(CameraSlot slot)
        {
            if (slot == null)
                return false;

            string nameValue = string.Concat(
                slot.name ?? string.Empty,
                " ",
                slot.transform != null ? slot.transform.name : string.Empty,
                " ",
                slot.transform != null && slot.transform.parent != null ? slot.transform.parent.name : string.Empty).ToLowerInvariant();
            return nameValue.Contains("commander")
                || nameValue.Contains("peri")
                || nameValue.Contains("head")
                || nameValue.Contains("cupola")
                || nameValue.Contains("tc");
        }

        private void CleanupMissileSlotIfNeeded()
        {
            if (activeMissile != null)
                return;

            if (missileSlot != null && CameraSlot.ActiveInstance == missileSlot)
            {
                CameraSlot targetSlot = FindPreferredReturnSlot();
                if (targetSlot != null && targetSlot != missileSlot)
                    CameraSlot.SetActiveSlot(targetSlot);
            }

            if (missileSlotObject != null)
            {
                Destroy(missileSlotObject);
                missileSlotObject = null;
                SetMissileSlot(null);
            }
        }

        private void UpdateDefaultScopeSuppression()
        {
            bool shouldHide = IsAnySpikeViewActive();
            if (shouldHide)
            {
                if (TrySetDefaultScopeRendered(false))
                    defaultScopeOwnerId = GetInstanceID();
                return;
            }

            if (defaultScopeOwnerId == GetInstanceID())
            {
                TrySetDefaultScopeRendered(true);
                defaultScopeOwnerId = 0;
            }
        }

        private void ReleaseDefaultScopeSuppression()
        {
            if (defaultScopeOwnerId != GetInstanceID())
                return;

            TrySetDefaultScopeRendered(true);
            defaultScopeOwnerId = 0;
        }

        private bool TrySetDefaultScopeRendered(bool enabled)
        {
            return UECommonUtil.TrySetDefaultScopeSpriteRendered(enabled);
        }

        private void UpdateScopeCanvasSuppression()
        {
            bool shouldHide = IsAnySpikeViewActive();
            if (shouldHide)
            {
                SuppressScopeCanvases();
                return;
            }

            ReleaseScopeCanvasSuppression();
        }

        private void SuppressScopeCanvases()
        {
            if (scopeCanvasSuppressed && Time.unscaledTime < nextScopeCanvasRefreshTime)
                return;

            nextScopeCanvasRefreshTime = Time.unscaledTime + ScopeCanvasRefreshInterval;
            EnsureScopeCanvasCache();
            for (int i = 0; i < cachedScopeCanvases.Count; i++)
            {
                Canvas canvas = cachedScopeCanvases[i];
                if (!IsScopeCanvasCandidate(canvas))
                    continue;

                int id = canvas.GetInstanceID();
                if (!scopeCanvasSnapshots.ContainsKey(id))
                {
                    scopeCanvasSnapshots[id] = new ScopeCanvasSnapshot
                    {
                        CanvasEnabled = canvas.enabled,
                        GameObjectActiveSelf = canvas.gameObject.activeSelf
                    };
                }

                if (canvas.enabled)
                    canvas.enabled = false;
                if (canvas.gameObject.activeSelf)
                    canvas.gameObject.SetActive(false);
            }

            scopeCanvasSuppressed = scopeCanvasSnapshots.Count > 0;
        }

        private void ReleaseScopeCanvasSuppression()
        {
            if (!scopeCanvasSuppressed && scopeCanvasSnapshots.Count == 0)
                return;

            for (int i = 0; i < cachedScopeCanvases.Count; i++)
            {
                Canvas canvas = cachedScopeCanvases[i];
                if (canvas == null)
                    continue;

                if (!scopeCanvasSnapshots.TryGetValue(canvas.GetInstanceID(), out ScopeCanvasSnapshot snapshot))
                    continue;

                if (canvas.gameObject.activeSelf != snapshot.GameObjectActiveSelf)
                    canvas.gameObject.SetActive(snapshot.GameObjectActiveSelf);
                if (canvas.enabled != snapshot.CanvasEnabled)
                    canvas.enabled = snapshot.CanvasEnabled;
            }

            scopeCanvasSnapshots.Clear();
            scopeCanvasSuppressed = false;
            nextScopeCanvasRefreshTime = 0f;
        }

        private void EnsureScopeCanvasCache()
        {
            if (scopeCanvasCacheInitialized)
                return;

            scopeCanvasCacheInitialized = true;
            cachedScopeCanvases.Clear();

            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (!IsScopeCanvasCandidate(canvas))
                    continue;

                cachedScopeCanvases.Add(canvas);
            }
        }

        private void SetMissileSlot(CameraSlot slot)
        {
            if (missileSlot == slot)
                return;

            UnderdogsDebug.LogSpike($"MissileSlot changed: {missileSlot != null} -> {slot != null}");

            missileSlot = slot;
            slotRegistrationDirty = true;
        }

        private bool IsScopeCanvasCandidate(Canvas canvas)
        {
            if (canvas == null)
                return false;

            if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
                return false;

            string n1 = canvas.name ?? string.Empty;
            string n2 = canvas.gameObject != null ? canvas.gameObject.name : string.Empty;
            string n3 = canvas.transform != null && canvas.transform.parent != null ? canvas.transform.parent.name : string.Empty;
            string combined = (n1 + " " + n2 + " " + n3).ToLowerInvariant();
            return combined.Contains("scope");
        }

        private int GetConfiguredFlirWidth()
        {
            // SPIKE thermal is fixed to the high-resolution path.
            return (int)ThermalHighWidth;
        }

        private int GetConfiguredFlirHeight()
        {
            return (int)ThermalHighHeight;
        }

        private Material GetConfiguredBlitMaterial()
        {
            // SPIKE uses a white-hot, no-scanline thermal material by default.
            Material whiteHotMaterial = UEResourceController.GetThermalFlirWhiteBlitMaterialNoScope();
            if (whiteHotMaterial != null)
            {
                return UEResourceController.CreateThermalMaterial(whiteHotMaterial,
                    new ThermalConfig { ColorMode = ThermalColorMode.WhiteHot });
            }

            // 回退到绿色热成像（基于 BMP1 MCLOS）
            Material thermalMaterial = UEResourceController.GetThermalFlirBlitMaterial();
            if (thermalMaterial != null)
            {
                return UEResourceController.CreateThermalMaterial(thermalMaterial,
                    new ThermalConfig { ColorMode = ThermalColorMode.WhiteHot });
            }

            Material noScan = UEResourceController.GetThermalFlirBlitMaterialNoScan();
            return noScan != null ? noScan : thermalMaterial;
        }

        private RectTransform CreateRect(string name, Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject gameObject = new GameObject(name);
            RectTransform rect = gameObject.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private void CreateCenterCross(RectTransform parent)
        {
            CreateBar(parent, "Top", new Vector2(0f, 110f), new Vector2(6f, 180f));
            CreateBar(parent, "Bottom", new Vector2(0f, -110f), new Vector2(6f, 180f));
            CreateBar(parent, "Left", new Vector2(-110f, 0f), new Vector2(180f, 6f));
            CreateBar(parent, "Right", new Vector2(110f, 0f), new Vector2(180f, 6f));
            CreateCornerBox(parent, 28f, 4f);
        }

        private void CreateCornerBox(RectTransform parent, float segment, float thickness)
        {
            CreateCorner(parent, "TL", new Vector2(-1f, 1f), segment, thickness);
            CreateCorner(parent, "TR", new Vector2(1f, 1f), segment, thickness);
            CreateCorner(parent, "BL", new Vector2(-1f, -1f), segment, thickness);
            CreateCorner(parent, "BR", new Vector2(1f, -1f), segment, thickness);
        }

        private void CreateCorner(RectTransform parent, string name, Vector2 sign, float segment, float thickness)
        {
            CreateBar(parent, name + "H", new Vector2(sign.x * (parent.sizeDelta.x * 0.5f - segment * 0.5f), sign.y * parent.sizeDelta.y * 0.5f), new Vector2(segment, thickness));
            CreateBar(parent, name + "V", new Vector2(sign.x * parent.sizeDelta.x * 0.5f, sign.y * (parent.sizeDelta.y * 0.5f - segment * 0.5f)), new Vector2(thickness, segment));
        }

        private void CreateBar(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchoredPosition, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            overlayGraphics.Add(image);
        }

        private static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null)
                return whiteSprite;

            whiteTexture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            whiteTexture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            whiteTexture.Apply();
            whiteTexture.hideFlags = HideFlags.DontUnloadUnusedAsset;

            whiteSprite = Sprite.Create(whiteTexture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            whiteSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return whiteSprite;
        }
    }

    [HarmonyPatch(typeof(GHPC.Crew.CrewBrainWeaponsModule), "AdjustAimByRatio")]
    [HarmonyPriority(Priority.First)]
    internal static class MarderSpikeManualAimInputPatch
    {
        private static bool Prefix(ref float horizontal, ref float vertical)
        {
            return !MarderSpikeSystem.TryOverridePlayerManualAimInput(ref horizontal, ref vertical);
        }
    }

    [HarmonyPatch(typeof(LiveRound), "Start")]
    internal static class MarderSpikeLiveRoundPatch
    {
        private static void Postfix(LiveRound __instance)
        {
            MarderSpikeSystem.NotifyMissileSpawn(__instance);
        }
    }

    [HarmonyPatch(typeof(LiveRound), "Detonate")]
    internal static class MarderSpikeLiveRoundDetonatePatch
    {
        private static void Prefix(LiveRound __instance)
        {
            MarderSpikeGuidanceRecovery.NotifyMissileGone(__instance);
            __instance.GetComponent<MarderSpikeMissileCameraFollow>()?.Restore();
        }
    }

    [HarmonyPatch(typeof(LiveRound), "doDestroy")]
    internal static class MarderSpikeLiveRoundDoDestroyPatch
    {
        private static void Prefix(LiveRound __instance)
        {
            MarderSpikeGuidanceRecovery.NotifyMissileGone(__instance);
            __instance.GetComponent<MarderSpikeMissileCameraFollow>()?.Restore();
        }
    }

    [HarmonyPatch(typeof(LiveRound), "ForceDestroy")]
    internal static class MarderSpikeLiveRoundForceDestroyPatch
    {
        private static void Prefix(LiveRound __instance)
        {
            MarderSpikeGuidanceRecovery.NotifyMissileGone(__instance);
            __instance.GetComponent<MarderSpikeMissileCameraFollow>()?.Restore();
        }
    }

    [HarmonyPatch(typeof(MissileGuidanceUnit), "MissileDestroyed")]
    internal static class MarderSpikeMissileGuidanceDestroyedPatch
    {
        private static void Postfix(MissileGuidanceUnit __instance)
        {
            MarderSpikeGuidanceRecovery.TryClearWaitingOnMissile(__instance);
        }
    }

    [HarmonyPatch(typeof(MissileGuidanceUnit), "OnGuidanceStopped")]
    internal static class MarderSpikeMissileGuidanceStoppedPatch
    {
        private static void Postfix(MissileGuidanceUnit __instance)
        {
            MarderSpikeGuidanceRecovery.TryClearWaitingOnMissile(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponSystem), "Fire")]
    internal static class MarderSpikeWeaponSystemFireRecoveryPatch
    {
        private static void Prefix(WeaponSystem __instance)
        {
            MarderSpikeGuidanceRecovery.TryRecoverStaleGuidanceOnFire(__instance);
        }
    }
}
