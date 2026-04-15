using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
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
        internal static readonly string[] DaySlotExactNames = { "MILAN sight and FCS" };
        internal static readonly string[] DaySlotExactPathSuffixes = { "Marder1A1_rig/hull/turret/turret scripts/MILAN_rig/MILAN/azimuth/elevation/MILAN elevation scripts/MILAN sight and FCS" };
        internal static readonly string[] CommanderSlotExactNames = { "commander head" };
        internal static readonly string[] CommanderSlotExactPathSuffixes = { "Marder1A1_rig/hull/turret/commander head" };
        private static readonly Dictionary<int, MarderSpikeRig> rigsByVehicleId = new Dictionary<int, MarderSpikeRig>();
        private static readonly FieldInfo f_cm_allCamSlots = typeof(CameraManager).GetField("_allCamSlots", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_uo_hasGuidance = typeof(UsableOptic).GetField("_hasGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_fcs_mainOptic = typeof(FireControlSystem).GetProperty("MainOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_fcs_nightOptic = typeof(FireControlSystem).GetProperty("NightOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_mainOptic_backing = typeof(FireControlSystem).GetField("<MainOptic>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_nightOptic_backing = typeof(FireControlSystem).GetField("<NightOptic>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_authoritativeOptic = typeof(FireControlSystem).GetField("AuthoritativeOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_snv_postVolume = typeof(SimpleNightVision).GetField("_postVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static void OnSceneChanged(string sceneName)
        {
            if (rigsByVehicleId.Count == 0)
                return;

            int[] deadKeys = rigsByVehicleId.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray();
            for (int i = 0; i < deadKeys.Length; i++)
                rigsByVehicleId.Remove(deadKeys[i]);

            if (UECommonUtil.IsMenuScene(sceneName))
                rigsByVehicleId.Clear();
        }

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

            CameraSlot slot = slots.FirstOrDefault(s => MatchesExactSlotIdentity(s, DaySlotExactNames, DaySlotExactPathSuffixes));

            if (slot != null)
            {
#if DEBUG
                UnderdogsDebug.LogSpike($"ResolveDaySlot exact match => {DescribeSlot(slot)}");
#endif
                return slot;
            }

            if (weaponInfo.FCS != null)
            {
                Transform searchRoot = weaponInfo.FCS.transform.parent != null ? weaponInfo.FCS.transform.parent : weaponInfo.FCS.transform;
                slot = searchRoot.GetComponentsInChildren<CameraSlot>(true).FirstOrDefault(s => MatchesExactSlotIdentity(s, DaySlotExactNames, DaySlotExactPathSuffixes));
            }

#if DEBUG
            if (slot != null)
                UnderdogsDebug.LogSpike($"ResolveDaySlot fallback match => {DescribeSlot(slot)}");
            else
                UnderdogsDebug.LogSpikeWarning($"ResolveDaySlot failed for vehicle={vehicle?.FriendlyName ?? "null"} weapon={weaponInfo?.Name ?? "null"}");
#endif
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

        internal static bool MatchesExactSlotIdentity(CameraSlot slot, string[] exactNames, string[] exactPathSuffixes)
        {
            if (slot == null)
                return false;

            if (MatchesExactName(slot.name, exactNames))
                return true;

            if (slot.transform != null && MatchesExactName(slot.transform.name, exactNames))
                return true;

            if (slot.transform != null && slot.transform.parent != null && MatchesExactName(slot.transform.parent.name, exactNames))
                return true;

            string path = slot.transform != null ? GetTransformPath(slot.transform) : null;
            if (!string.IsNullOrEmpty(path))
            {
                for (int i = 0; i < exactPathSuffixes.Length; i++)
                {
                    if (path.EndsWith(exactPathSuffixes[i], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static bool MatchesExactName(string candidate, string[] exactNames)
        {
            if (string.IsNullOrEmpty(candidate) || exactNames == null)
                return false;

            for (int i = 0; i < exactNames.Length; i++)
            {
                if (string.Equals(candidate, exactNames[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        internal static string DescribeSlot(CameraSlot slot)
        {
            if (slot == null)
                return "slot=null";

            string path = slot.transform != null ? GetTransformPath(slot.transform) : "no_transform";
            return $"name='{slot.name}' path='{path}' exterior={slot.IsExterior}";
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return "null";

            List<string> parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
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
        private const float ManualInputDeadzone = 0.02f;
        private const float ThermalLowWidth = 320f;
        private const float ThermalLowHeight = 180f;
        private const float ThermalHighWidth = 1024f;
        private const float ThermalHighHeight = 576f;
        private const float LockBoundsRefreshInterval = 0.1f;
        private const float LockScreenRectRefreshInterval = 0.2f;
        private const float ScopeCanvasRefreshInterval = 0.5f;
        private const float SceneMissileSearchInterval = 0.5f;
        private const float PassedTargetDistanceThreshold = 15f;
        private static readonly string[] DayStockVisualPaths =
        {
            "Optic/Reticle Mesh",
            "Optic/Quad",
            "Reticle Mesh",
            "Quad"
        };

        private static readonly FieldInfo f_cm_allCamSlots = typeof(CameraManager).GetField("_allCamSlots", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_gu_unguidedMissiles = typeof(MissileGuidanceUnit).GetField("_unguidedMissiles", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_ap_stabActive = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_ap_stabMode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
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
        private Material cachedWhiteHotBlitMaterial;
        private UsableOptic missileOptic;
        private CameraSlot missileSlot;
        private GameObject missileSlotObject;
        private LiveRound activeMissile;
        private Transform aimProxy;
        private CameraSlot latestNonMissileSlot;
        private CameraSlot preLaunchSightSlot;
        private Vehicle candidateVehicle;
        private Transform candidateAnchor;
        private Renderer candidateRenderer;
        private Collider candidateCollider;
        private Collider[] candidateColliders;
        private Renderer[] candidateRenderers;
        private float candidateDistance = float.PositiveInfinity;
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
        private RectTransform topAttackCueRect;
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
        private bool opticsInitialized;
        private bool lockConfirmed;
        private bool missileViewRequested;
        private bool lastCanControlSpikeLock; // 用于追踪 CanControlSpikeLock 变化
        private SpikeGuidanceMode lastLoggedGuidanceMode = (SpikeGuidanceMode)(-1);
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
        private string lastThermalLogSignature;
        private string lastLoggedTargetBoundsSource;
        private string lastLoggedCandidateBoundsSource;
        private string lastLoggedActiveOptic;
        private string lastLoggedViewGateSignature;
        private string lastLoggedManualAimSignature;
        private bool slotRegistrationDirty = true;
        private bool scopeCanvasSuppressed;
        private bool scopeCanvasCacheInitialized;
        private readonly List<Canvas> cachedScopeCanvases = new List<Canvas>();
        private readonly Dictionary<int, ScopeCanvasSnapshot> scopeCanvasSnapshots = new Dictionary<int, ScopeCanvasSnapshot>();
        private AimablePlatform[] milanPlatforms = new AimablePlatform[0];
        private bool[] milanPlatformOriginalStabilized = new bool[0];
        private bool[] milanPlatformOriginalStabActive = new bool[0];
        private StabilizationMode[] milanPlatformOriginalStabMode = new StabilizationMode[0];
        private bool milanStabilizerGateApplied;
        private bool milanFcsOriginalStabsActive;
        private StabilizationMode milanFcsOriginalStabMode;
        private string lastLoggedMilanGateState;

        // Top-attack mode fields
        private bool topAttackModeEnabled = false;          // V键切换状态（发射前）
        private bool topAttackModeForCurrentMissile = false; // 当前导弹的攻顶模式（发射时捕获）
        private static readonly string[] TopAttackAlertMessages = { "Direct flight", "Top-attack" };

        internal bool TopAttackModeEnabled => topAttackModeEnabled;

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
            RestoreMilanStabilizerGate();
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
            CacheMilanStabilizerTargets();
            EnsureAimProxy();
            EnsureThermalSlot();
            EnsureFnfGuide();
            ApplyWeaponDefaults();
            EnsureOverlay();
            enabled = true;
        }

        private void CacheMilanStabilizerTargets()
        {
            milanPlatforms = new AimablePlatform[0];
            milanPlatformOriginalStabilized = new bool[0];
            milanPlatformOriginalStabActive = new bool[0];
            milanPlatformOriginalStabMode = new StabilizationMode[0];
            milanStabilizerGateApplied = false;
            milanFcsOriginalStabsActive = false;
            milanFcsOriginalStabMode = default;

            if (vehicle == null || vehicle.AimablePlatforms == null)
                return;

            List<AimablePlatform> matched = new List<AimablePlatform>(2);
            for (int i = 2; i <= 3; i++)
            {
                if (vehicle.AimablePlatforms.Length <= i)
                    break;

                AimablePlatform candidate = vehicle.AimablePlatforms[i];
                if (candidate == null)
                    continue;

                matched.Add(candidate);
#if DEBUG
                UnderdogsDebug.LogSpike($"MILAN stabilizer target => index={i} path={BuildTransformPath(candidate.transform)}");
#endif
            }

            milanPlatforms = matched.ToArray();
            milanPlatformOriginalStabilized = new bool[milanPlatforms.Length];
            milanPlatformOriginalStabActive = new bool[milanPlatforms.Length];
            milanPlatformOriginalStabMode = new StabilizationMode[milanPlatforms.Length];
        }

        private static string BuildTransformPath(Transform transform)
        {
            if (transform == null)
                return null;

            List<string> parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static bool MatchesAnyPathSuffix(string path, string[] suffixes)
        {
            if (string.IsNullOrEmpty(path) || suffixes == null)
                return false;

            for (int i = 0; i < suffixes.Length; i++)
            {
                if (path.EndsWith(suffixes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool ShouldGateMilanStabilizer()
        {
            return IsSpikeWeaponSelected();
        }

        private void UpdateMilanStabilizerGate()
        {
            bool shouldDisableStabilizer = ShouldGateMilanStabilizer();

            if (shouldDisableStabilizer)
            {
                if (!milanStabilizerGateApplied)
                    ApplyMilanStabilizerGate();
            }
            else if (milanStabilizerGateApplied)
            {
                RestoreMilanStabilizerGate();
            }

#if DEBUG
            string state = shouldDisableStabilizer ? "milan_stabilizer_off" : "milan_stabilizer_restored";
            if (!string.Equals(lastLoggedMilanGateState, state, StringComparison.Ordinal))
            {
                lastLoggedMilanGateState = state;
                UnderdogsDebug.LogSpike($"MILAN stabilizer gate => {state}");
            }
#endif
        }

        private void ApplyMilanStabilizerGate()
        {
            if ((milanPlatforms == null || milanPlatforms.Length == 0) && fcs == null)
                return;

            if (fcs != null)
            {
                try { milanFcsOriginalStabsActive = fcs.StabsActive; } catch { }
                try { milanFcsOriginalStabMode = fcs.CurrentStabMode; } catch { }
                try { fcs.StabsActive = false; } catch { }
            }

            for (int i = 0; i < milanPlatforms.Length; i++)
            {
                AimablePlatform platform = milanPlatforms[i];
                if (platform == null)
                    continue;

                milanPlatformOriginalStabilized[i] = platform.Stabilized;
                milanPlatformOriginalStabActive[i] = GetAimableStabActive(platform);
                milanPlatformOriginalStabMode[i] = GetAimableStabMode(platform);

                try { platform.Stabilized = false; } catch { }
                try { f_ap_stabActive?.SetValue(platform, false); } catch { }
            }

            milanStabilizerGateApplied = true;
        }

        private void RestoreMilanStabilizerGate()
        {
            if (!milanStabilizerGateApplied)
                return;

            for (int i = 0; i < milanPlatforms.Length; i++)
            {
                AimablePlatform platform = milanPlatforms[i];
                if (platform == null)
                    continue;

                try { platform.Stabilized = milanPlatformOriginalStabilized[i]; } catch { }
                try { f_ap_stabActive?.SetValue(platform, milanPlatformOriginalStabActive[i]); } catch { }
                try { f_ap_stabMode?.SetValue(platform, milanPlatformOriginalStabMode[i]); } catch { }
            }

            if (fcs != null)
            {
                try { fcs.StabsActive = milanFcsOriginalStabsActive; } catch { }
                try { fcs.CurrentStabMode = milanFcsOriginalStabMode; } catch { }
            }

            milanStabilizerGateApplied = false;
        }

        private static bool GetAimableStabActive(AimablePlatform platform)
        {
            try
            {
                object value = f_ap_stabActive?.GetValue(platform);
                return value is bool flag && flag;
            }
            catch
            {
                return false;
            }
        }

        private static StabilizationMode GetAimableStabMode(AimablePlatform platform)
        {
            try
            {
                object value = f_ap_stabMode?.GetValue(platform);
                return value is StabilizationMode mode ? mode : default;
            }
            catch
            {
                return default;
            }
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
            HandleTopAttackToggleInput();
            UpdateGuidanceMode();
            UpdateLockTarget();
            UpdateTargetPassFallback();
            UpdateAimProxy();
            UpdateMissileControl();
            UpdateMilanStabilizerGate();
            EnsureInitialThermalView();
            TryEnterMissileViewIfNeeded();
            UpdateScopeCanvasSuppression();
            UpdateDefaultScopeSuppression();
            UpdateOverlay();
            CleanupMissileSlotIfNeeded();
        }

        private void OnDisable()
        {
            RestoreMilanStabilizerGate();
            ReleaseScopeCanvasSuppression();
            ReleaseDefaultScopeSuppression();
        }

        private void OnDestroy()
        {
            MarderSpikeSystem.UnregisterRig(this);
            RestoreMilanStabilizerGate();
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

            // Capture the selected flight mode at launch. Lock state can change later.
            // Check if target distance supports top-attack (min 650m)
            bool canUseTopAttack = topAttackModeEnabled && HasLockTarget();
            if (canUseTopAttack)
            {
                float targetDistance = lockedDistance;
                if (!SpikeTopAttackTracker.SupportsTopAttack(targetDistance))
                {
                    // Distance too short for top-attack, auto-switch to direct mode
                    topAttackModeForCurrentMissile = false;
                    ResolveAlertHud()?.AddAlertMessage($"Direct flight (< 650m)", 3f);
#if DEBUG
                    UnderdogsDebug.LogSpike($"Top-attack disabled: distance {targetDistance:F0}m < {SpikeTopAttackTracker.MinTopAttackDistance}m minimum");
#endif
                }
                else
                {
                    topAttackModeForCurrentMissile = true;
                }
            }
            else
            {
                topAttackModeForCurrentMissile = false;
            }

            bool startManual = !HasLockTarget();
            preferTvControl = startManual;
            SyncMissileFollow(round);
            MarderSpikeMissileCameraFollow follow = GetMissileFollow(round);
            if (follow == null)
                return;

            // Always remember the latest live missile, but only actually attach when the
            // player is on the SPIKE weapon and in an allowed interior view.
            missileViewRequested = true;
            follow.SetManualModeActive(startManual);
            follow.EnterMissileView(startManual, GetPreferredReturnSlotForMissile());
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

        internal bool PreferManualMissileControl => ShouldUseManualMissileControl();

        internal bool IsManualMissileViewActive()
        {
            return ShouldUseManualMissileControl();
        }

        internal bool TryApplyManualAimInput(ref float horizontal, ref float vertical)
        {
            float rawHorizontal = horizontal;
            float rawVertical = vertical;

            if (!ShouldUseManualMissileControl() || guidanceUnit == null || activeMissile == null || activeMissile.Info == null)
            {
                SetManualAimAngularVelocity(Vector2.zero, "manual_gate_closed");
                return false;
            }

            bool applyTuning = BMP1MCLOSAmmo.MclosInputTuning.ShouldApplyNow(true);
            BMP1MCLOSAmmo.MclosInputTuning.ApplyDynamicTurnSpeed(activeMissile.Info, horizontal, vertical, applyTuning);
            horizontal = ApplyManualInputDeadzone(BMP1MCLOSAmmo.MclosInputTuning.ProcessAxis(horizontal, applyTuning));
            vertical = ApplyManualInputDeadzone(BMP1MCLOSAmmo.MclosInputTuning.ProcessAxis(vertical, applyTuning));

            EnsureMclosGuide();
            SetManualAimAngularVelocity(new Vector2(horizontal, vertical) * ManualControlSensitivity,
                Mathf.Approximately(horizontal, 0f) && Mathf.Approximately(vertical, 0f) ? "manual_deadzone" : "manual_input");
            activeMissile.SkipGuidanceLockout = true;
            activeMissile.Guided = true;

            if (mclosGuideTransform != null && guidanceUnit.AimElement != mclosGuideTransform)
                guidanceUnit.AimElement = mclosGuideTransform;

#if DEBUG
            UnderdogsDebug.LogSpike($"Manual input raw=({rawHorizontal:F3},{rawVertical:F3}) tuned=({horizontal:F3},{vertical:F3}) mav={guidanceUnit.ManualAimAngularVelocity}");
#endif
            return true;
        }

        private void SetManualControlActive(bool active, bool forceMissileView)
        {
            preferTvControl = active;

            if (!active)
                SetManualAimAngularVelocity(Vector2.zero, "manual_disabled");

            if (activeMissile == null)
                return;

            if (active)
                DisableInfraredTracking();

            MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
            if (follow == null)
                return;

            bool manualMode = !HasLockTarget();
            follow.SetManualModeActive(manualMode);

            if (forceMissileView)
                follow.EnterMissileView(manualMode, GetPreferredReturnSlotForMissile());
        }

        private void DisableInfraredTracking()
        {
            if (activeMissile == null)
                return;

            SpikeInfraredTracker tracker = activeMissile.GetComponent<SpikeInfraredTracker>();
            if (tracker != null)
                Destroy(tracker);

            SpikeTopAttackTracker topTracker = activeMissile.GetComponent<SpikeTopAttackTracker>();
            if (topTracker != null)
                Destroy(topTracker);
        }

        internal void OnMissileFollowReady(MarderSpikeMissileCameraFollow follow)
        {
            if (follow == null)
                return;

            SetMissileSlot(follow.MissileSlot);
            follow.SetManualModeActive(activeMissile != null && !HasLockTarget());
        }

        internal bool ShouldHoldMissileCamera(MarderSpikeMissileCameraFollow follow)
        {
            if (follow == null || follow.Round == null || follow.Round.IsDestroyed)
                return false;

            if (activeMissile == null || follow.Round != activeMissile)
                return false;

            return CanAutoAttachSelectedMissileView();
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
            EnsureThermalSlot();

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
                SuppressDayStockReticle(dayOptic);
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
                try { if (thermalOptic != null) fcs?.RegisterOptic(thermalOptic); } catch { }
                TryAssignMainAndNightOptics(dayOptic, thermalOptic);
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
            if (!IsSpikeWeaponSelected() && !IsMissileCameraActive())
                return;

            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            UsableOptic desiredOptic = null;

            if (IsMissileCameraActive())
                desiredOptic = thermalOptic != null ? thermalOptic : missileOptic;
            else if (activeSlot == daySlot)
                desiredOptic = dayOptic;
            else if (activeSlot == thermalSlot)
                desiredOptic = thermalOptic;

            if (desiredOptic == null)
                return;

            TryAssignAuthoritativeOptic(desiredOptic);
            try { fcs?.NotifyActiveOpticChanged(desiredOptic); } catch { }

#if DEBUG
            string opticSignature = $"{desiredOptic.name}|slot={activeSlot?.name ?? "missile"}|missileView={IsMissileCameraActive()}";
            if (!string.Equals(lastLoggedActiveOptic, opticSignature, StringComparison.Ordinal))
            {
                lastLoggedActiveOptic = opticSignature;
                UnderdogsDebug.LogSpike($"Active optic => {opticSignature}");
            }
#endif

            if (desiredOptic == thermalOptic || desiredOptic == missileOptic)
                LogThermalState("Sync SPIKE thermal optic", thermalSlotObject, thermalSlot, thermalSlotObject != null ? thermalSlotObject.GetComponent<SimpleNightVision>() : null, thermalSlotObject != null ? thermalSlotObject.transform.Find("FLIR Post Processing - Green(Clone)/FLIR Only Volume")?.GetComponent<PostProcessVolume>() : null);
        }

        private void TryEnterMissileViewIfNeeded()
        {
            if (activeMissile == null || activeMissile.IsDestroyed)
                return;

            if (!missileViewRequested)
                return;

            if (!CanAutoAttachSelectedMissileView())
                return;

            MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
            if (follow == null)
                return;

            follow.EnterMissileView(!HasLockTarget(), GetPreferredReturnSlotForMissile());
            missileViewRequested = false;
        }

        private void EnsureThermalSlot()
        {
            if (daySlot == null || dayOptic == null)
                return;

            if (thermalOptic == null || thermalOptic.slot == null)
                thermalOptic = GetOrCreateThermalOptic();

            if (thermalOptic == null || thermalOptic.slot == null)
            {
                thermalSlot = null;
                thermalSlotObject = null;
                return;
            }

            thermalSlot = thermalOptic.slot;
            thermalSlotObject = thermalSlot != null ? thermalSlot.gameObject : thermalOptic.gameObject;

            RefreshOpticTransform(thermalOptic.transform, dayOptic.transform);
            if (thermalSlotObject != null)
                RefreshSlotTransform(thermalSlotObject.transform, daySlot.transform);

            ConfigureThermalOptic(thermalOptic);
            ConfigureThermalCameraSlot(thermalSlot, daySlot.DefaultFov, daySlot.OtherFovs);
            if (thermalSlot.gameObject != null)
                thermalSlot.gameObject.name = thermalSlotName ?? "Spike Thermal";
            LinkThermalSlot();
            SetupThermalPost(thermalSlotObject, thermalSlot);

            // 禁用热成像槽中的Quad和Scope sprite
            SuppressThermalVisuals(thermalSlotObject);
        }

        private void SuppressThermalVisuals(GameObject thermalSlotObj)
        {
            if (thermalSlotObj == null) return;

            try
            {
                // 禁用 Quad: Spike Thermal/Spike Thermal/Quad
                Transform thermalRoot = thermalSlotObj.transform.Find("Spike Thermal");
                if (thermalRoot != null)
                {
                    Transform quad = thermalRoot.Find("Quad");
                    if (quad != null && quad.gameObject.activeSelf)
                        quad.gameObject.SetActive(false);
                }

                // 禁用 Scope sprite: Spike Thermal/camera/Scope
                Transform cameraRoot = thermalSlotObj.transform.Find("camera");
                if (cameraRoot != null)
                {
                    Transform scope = cameraRoot.Find("Scope");
                    if (scope != null)
                    {
                        SpriteRenderer sr = scope.GetComponent<SpriteRenderer>();
                        if (sr != null && sr.enabled)
                            sr.enabled = false;
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnderdogsDebug.LogSpikeWarning($"SuppressThermalVisuals failed: {ex.Message}");
            }
        }

        private UsableOptic GetOrCreateThermalOptic()
        {
            UsableOptic existing = FindExistingThermalOptic();
            if (existing != null && existing.slot != null)
                return existing;

            return CreateThermalOpticClone();
        }

        private UsableOptic FindExistingThermalOptic()
        {
            if (vehicle == null)
                return null;

            MarderSpikeThermalOpticMarker[] markers = vehicle.GetComponentsInChildren<MarderSpikeThermalOpticMarker>(true);
            for (int i = 0; i < markers.Length; i++)
            {
                MarderSpikeThermalOpticMarker marker = markers[i];
                UsableOptic optic = marker != null ? marker.GetComponent<UsableOptic>() : null;
                if (optic == null || optic.slot == null)
                    continue;

                return optic;
            }

            return null;
        }

        private UsableOptic CreateThermalOpticClone()
        {
            if (dayOptic == null)
                return null;

            GameObject source = dayOptic.gameObject;
            Transform parent = dayOptic.transform.parent != null ? dayOptic.transform.parent : daySlot.transform.parent;
            if (source == null || parent == null)
                return null;

            GameObject clone = Instantiate(source, parent);
            clone.name = thermalSlotName ?? "Spike Thermal";
            if (!clone.activeSelf)
                clone.SetActive(true);

            UsableOptic optic = clone.GetComponent<UsableOptic>();
            CameraSlot slot = optic != null ? optic.slot : clone.GetComponent<CameraSlot>();
            if (optic == null || slot == null)
            {
                Destroy(clone);
                return null;
            }

            MarderSpikeThermalOpticMarker marker = clone.GetComponent<MarderSpikeThermalOpticMarker>();
            if (marker == null)
                marker = clone.AddComponent<MarderSpikeThermalOpticMarker>();

            slotRegistrationDirty = true;
            return optic;
        }

        private void ConfigureThermalOptic(UsableOptic optic)
        {
            if (optic == null)
                return;

            try { optic.FCS = fcs; } catch { }
            try { optic.GuidanceLight = true; } catch { }
            try { optic.RotateAzimuth = true; } catch { }
            try { optic.CantCorrect = true; } catch { }
            try { optic.CantCorrectMaxSpeed = 5f; } catch { }
            try { optic.Alignment = OpticAlignment.BoresightStabilized; } catch { }
            try { optic.ForceHorizontalReticleAlign = true; } catch { }
            try { optic.ZeroOutInvalidRange = true; } catch { }
            try { optic.post = null; } catch { }
            try
            {
                if (optic.reticleMesh != null)
                    optic.reticleMesh.Clear();
            }
            catch { }
        }

        private void LinkThermalSlot()
        {
            if (daySlot == null || thermalSlot == null)
                return;

            UECommonUtil.LinkSightSlots(daySlot, thermalSlot);
            try { daySlot.LinkedDaySight = null; } catch { }
            try { daySlot.IsLinkedNightSight = false; } catch { }
            try { thermalSlot.LinkedNightSight = null; } catch { }
            try { if (thermalOptic != null) thermalOptic.slot = thermalSlot; } catch { }
            try { if (dayOptic != null) dayOptic.slot = daySlot; } catch { }
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

        private void RefreshOpticTransform(Transform destination, Transform source)
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
            slot.CanToggleFlirPolarity = false;
            slot.FLIRWidth = GetConfiguredFlirWidth();
            slot.FLIRHeight = GetConfiguredFlirHeight();
            slot.FLIRFilterMode = FilterMode.Point;
            slot.VibrationShakeMultiplier = 0.01f;
            slot.DefaultFov = defaultFov > 0.1f ? defaultFov : 5f;
            slot.OtherFovs = otherFovs != null ? (float[])otherFovs.Clone() : new float[0];
            slot.SpriteType = daySlot != null ? daySlot.SpriteType : CameraSpriteManager.SpriteType.DefaultScope;

            Material blitMaterial = GetWhiteHotBlitMaterial();
            if (blitMaterial != null)
                slot.FLIRBlitMaterialOverride = blitMaterial;
        }

        private void SetupThermalPost(GameObject slotObject, CameraSlot slot)
        {
            if (slotObject == null || slot == null)
                return;

            GameObject flirPrefab = MarderSpikeAssets.GetThermalPostPrefab();
            if (flirPrefab == null)
                return;

            SimpleNightVision snv = UECommonUtil.GetOrAddComponent<SimpleNightVision>(slotObject);
            PostProcessVolume existingVolume = slotObject.GetComponent<PostProcessVolume>();
            if (existingVolume != null)
                Destroy(existingVolume);

            Transform existingPost = slotObject.transform.Find("FLIR Post Processing - Green(Clone)");
            GameObject post = existingPost != null ? existingPost.gameObject : Instantiate(flirPrefab, slotObject.transform);
            if (!post.activeSelf)
                post.SetActive(true);

            Transform mainVolume = post.transform.Find("MainCam Volume");
            if (mainVolume != null)
                mainVolume.gameObject.SetActive(false);

            PostProcessVolume flirOnlyVolume = post.transform.Find("FLIR Only Volume")?.GetComponent<PostProcessVolume>();
            if (flirOnlyVolume != null)
            {
                flirOnlyVolume.enabled = true;
                flirOnlyVolume.weight = 1f;
                flirOnlyVolume.priority = 100f;
            }

            try { snvPostVolumeField?.SetValue(snv, flirOnlyVolume); } catch { }
            ApplyWhiteHotThermalState(slot, snv, flirOnlyVolume);
        }

        private void ApplyWhiteHotThermalState(CameraSlot slot, SimpleNightVision snv, PostProcessVolume flirOnlyVolume)
        {
            if (slot == null)
                return;

            slot.VisionType = NightVisionType.Thermal;
            slot.OverrideFLIRResolution = true;
            slot.CanToggleFlirPolarity = false;
            slot.WasUsingNightMode = true;

            Material whiteHot = GetWhiteHotBlitMaterial();
            if (whiteHot != null)
                slot.FLIRBlitMaterialOverride = whiteHot;
            else
                MelonLogger.Warning("[Marder Spike] White-hot FLIR material unavailable for thermal slot.");

            if (snv != null)
                snv.enabled = true;

            if (flirOnlyVolume != null)
            {
                flirOnlyVolume.enabled = true;
                flirOnlyVolume.weight = 1f;
                flirOnlyVolume.priority = 100f;
            }

            LogThermalState("Configured thermal slot", slot.gameObject, slot, snv, flirOnlyVolume);
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

            if (!thermal)
                return;

            try
            {
                if (optic.reticleMesh != null)
                    optic.reticleMesh.Clear();
            }
            catch { }

            try { optic.reticleMesh = null; } catch { }

            DisableOpticReticleMeshes(optic.transform, optic);
            SetScopeSpriteRendererEnabled(optic.transform, false);
        }

        private static string NormalizeManualAimLogReason(Vector2 angularVelocity, string reason)
        {
            if (angularVelocity.sqrMagnitude <= 0.000001f)
            {
                switch (reason)
                {
                    case "manual_gate_closed":
                    case "mclos_view_inactive":
                    case "non_mclos":
                    case "manual_disabled":
                        return "manual_inactive";
                }
            }

            return string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        private static void SuppressDayStockReticle(UsableOptic optic)
        {
            if (optic == null)
                return;

            string BuildRelativePath(Transform node)
            {
                if (node == null)
                    return "null";

                string pathValue = node.name;
                while (node.parent != null && node.parent != optic.transform)
                {
                    node = node.parent;
                    pathValue = node.name + "/" + pathValue;
                }

                return pathValue;
            }

            bool IsKnownDayStockVisualPath(string pathValue)
            {
                if (string.IsNullOrEmpty(pathValue))
                    return false;

                for (int i = 0; i < DayStockVisualPaths.Length; i++)
                {
                    if (string.Equals(pathValue, DayStockVisualPaths[i], StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            void DisableKnownStockVisualNode(Transform node)
            {
                if (node == null)
                    return;

                Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && renderers[i].enabled)
                        renderers[i].enabled = false;
                }

                Graphic[] graphics = node.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                {
                    if (graphics[i] != null && graphics[i].enabled)
                        graphics[i].enabled = false;
                }

                PostMeshComp[] postMeshes = node.GetComponentsInChildren<PostMeshComp>(true);
                for (int i = 0; i < postMeshes.Length; i++)
                {
                    if (postMeshes[i] != null && postMeshes[i].enabled)
                        postMeshes[i].enabled = false;
                }

                if (node.gameObject.activeSelf)
                    node.gameObject.SetActive(false);
            }

            bool ShouldSuppressReticleMesh(ReticleMesh reticleMesh)
            {
                if (reticleMesh == null || reticleMesh.transform == null)
                    return false;

                Transform cameraRoot = optic.transform.Find("camera");
                if (cameraRoot != null && reticleMesh.transform.IsChildOf(cameraRoot))
                    return false;

                string pathValue = BuildRelativePath(reticleMesh.transform);
                if (pathValue.StartsWith("camera/", StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
            }

            try
            {
                Transform[] transforms = optic.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform node = transforms[i];
                    string pathValue = BuildRelativePath(node);
                    if (IsKnownDayStockVisualPath(pathValue))
                        DisableKnownStockVisualNode(node);
                }

                ReticleMesh[] reticleMeshes = optic.GetComponentsInChildren<ReticleMesh>(true);
                for (int i = 0; i < reticleMeshes.Length; i++)
                {
                    ReticleMesh reticleMesh = reticleMeshes[i];
                    if (!ShouldSuppressReticleMesh(reticleMesh))
                        continue;

                    if (reticleMesh.gameObject.activeSelf)
                        reticleMesh.gameObject.SetActive(false);
                    if (reticleMesh.enabled)
                        reticleMesh.enabled = false;

                    SkinnedMeshRenderer skinnedMeshRenderer = reticleMesh.GetComponent<SkinnedMeshRenderer>();
                    if (skinnedMeshRenderer != null && skinnedMeshRenderer.enabled)
                        skinnedMeshRenderer.enabled = false;

                    PostMeshComp[] postMeshes = reticleMesh.GetComponentsInChildren<PostMeshComp>(true);
                    for (int j = 0; j < postMeshes.Length; j++)
                    {
                        if (postMeshes[j] != null && postMeshes[j].enabled)
                            postMeshes[j].enabled = false;
                    }
                }

                if (optic.reticleMesh != null)
                {
                    try { optic.reticleMesh.Clear(); } catch { }
                    optic.reticleMesh = null;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Marder Spike] Failed to suppress day reticle: {ex.Message}");
            }
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
                    renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty);

                if (!combined.Equals("Scope Sprite Scope", StringComparison.OrdinalIgnoreCase))
                    continue;

                renderer.enabled = enabled;
            }
        }

        private void HandleLockInput()
        {
            bool mmbPressed = Input.GetMouseButtonDown(2);
            if (!mmbPressed)
                return;

            if (!CanControlSpikeLock())
                return;

            if (HasLockTarget())
            {
                ClearLockTarget("user_manual_clear");
                if (activeMissile != null)
                {
                    SetManualControlActive(true, true);
                    missileViewRequested = true;
                }

#if DEBUG
                UnderdogsDebug.LogSpike("Lock cleared");
#endif
                return;
            }

            if (activeMissile != null)
            {
                MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
                if (follow != null)
                    follow.EnterMissileView(true, GetPreferredReturnSlotForMissile());
            }

            TryEngageTrackingCandidate();
        }

        private bool TryEngageTrackingCandidate()
        {
            if (!HasTrackingCandidate())
                return false;

            ApplyLockTarget(candidateVehicle, candidateAnchor, candidateRenderer, candidateDistance);
            if (TryGetLockAimPoint(out Vector3 aimPoint))
            {
                lastKnownAimPoint = aimPoint;
                hasLastKnownAimPoint = true;
            }

            lockConfirmed = true;

            if (activeMissile != null)
            {
                preferTvControl = false;

                // Set up appropriate tracker based on top-attack mode captured at launch
                if (topAttackModeForCurrentMissile)
                {
                    // Use top-attack tracker
                    SpikeTopAttackTracker topTracker = activeMissile.GetComponent<SpikeTopAttackTracker>();
                    if (topTracker == null)
                    {
                        // Remove regular tracker if exists
                        SpikeInfraredTracker regularTracker = activeMissile.GetComponent<SpikeInfraredTracker>();
                        if (regularTracker != null)
                            Destroy(regularTracker);

                        topTracker = activeMissile.gameObject.AddComponent<SpikeTopAttackTracker>();
                    }

                    topTracker.SetInitialLock(lockedVehicle, lockedAnchor);
                }
                else
                {
                    // Use regular infrared tracker
                    SpikeInfraredTracker tracker = activeMissile.GetComponent<SpikeInfraredTracker>();
                    if (tracker == null)
                    {
                        tracker = activeMissile.gameObject.AddComponent<SpikeInfraredTracker>();
                    }

                    tracker.SetInitialLock(lockedVehicle, lockedAnchor);
                }

                MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
                if (follow != null)
                    follow.SetManualModeActive(false);
            }

            return true;
        }

        private void HandleTopAttackToggleInput()
        {
            // Only process V key when player is controlling this rig
            if (!IsPlayerControllingThisRig())
                return;

            // Only process when Spike weapon is selected
            if (!IsSpikeWeaponSelected())
                return;

            // Only allow toggle when player is in interior view (not exterior)
            if (!CanUseInteriorSpikeView())
                return;

            // Do not allow toggle while missile is already flying
            if (activeMissile != null && !activeMissile.IsDestroyed)
                return;

            if (!Input.GetKeyDown(KeyCode.V))
                return;

            // Toggle the mode
            topAttackModeEnabled = !topAttackModeEnabled;

            // Show HUD alert
            ResolveAlertHud()?.AddAlertMessage(TopAttackAlertMessages[topAttackModeEnabled ? 1 : 0], 2f);

#if DEBUG
            UnderdogsDebug.LogSpike($"Top-attack mode toggled => {(topAttackModeEnabled ? "ENABLED" : "DISABLED")}");
#endif
        }

        private static GHPC.UI.Hud.AlertHud cachedAlertHud;
        private static GHPC.UI.Hud.AlertHud ResolveAlertHud()
        {
            if (cachedAlertHud != null)
                return cachedAlertHud;

            GameObject app = GameObject.Find("_APP_GHPC_");
            Transform alertTransform = app != null ? app.transform.Find("UIHUDCanvas/system alert text") : null;
            cachedAlertHud = alertTransform != null ? alertTransform.GetComponent<GHPC.UI.Hud.AlertHud>() : null;
            return cachedAlertHud;
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
            if (follow != null)
                follow.SetManualModeActive(missile != null && !HasLockTarget());
            EnsureMclosGuide();
        }

        private bool IsMissileCameraActive()
        {
            MarderSpikeMissileCameraFollow follow = GetMissileFollow(activeMissile);
            return follow != null && follow.CameraActive;
        }

        private bool CanAutoAttachSelectedMissileView()
        {
            return activeMissile != null && !activeMissile.IsDestroyed && CanUseInteriorSpikeView();
        }

        private bool ShouldUseManualMissileControl()
        {
            // Manual interception is intentionally narrow: MCLOS only, with the player
            // actually riding the selected SPIKE missile view. This prevents stale input
            // from leaking into FnF, commander, or exterior viewing.
            return activeMissile != null
                && !activeMissile.IsDestroyed
                && IsMissileCameraActive()
                && CanAutoAttachSelectedMissileView()
                && !HasLockTarget();
        }

        private bool CanUseInteriorSpikeView()
        {
            if (!IsPlayerControllingThisRig() || !IsSpikeWeaponSelected())
            {
                LogViewGateState(null, false, "player_or_weapon_gate");
                return false;
            }

            CameraManager cameraManager = CameraManager.Instance;
            if (cameraManager != null && cameraManager.ExteriorMode)
            {
                LogViewGateState(CameraSlot.ActiveInstance, false, "exterior_mode");
                return false;
            }

            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            if (activeSlot == missileSlot)
            {
                LogViewGateState(activeSlot, true, "missile_slot");
                return true;
            }

            bool allowed = activeSlot != null
                && SlotBelongsToVehicle(activeSlot)
                && !activeSlot.IsExterior
                && !IsCommanderLikeSlot(activeSlot);
            LogViewGateState(activeSlot, allowed, allowed ? "interior_slot" : "blocked_slot");
            return allowed;
        }

        private static float ApplyManualInputDeadzone(float axis)
        {
            return Mathf.Abs(axis) < ManualInputDeadzone ? 0f : axis;
        }

        private void SetManualAimAngularVelocity(Vector2 angularVelocity, string reason)
        {
            if (guidanceUnit == null)
                return;

            guidanceUnit.ManualAimAngularVelocity = angularVelocity;

#if DEBUG
            string normalizedReason = NormalizeManualAimLogReason(angularVelocity, reason);
            string signature = string.Concat(
                Mathf.RoundToInt(angularVelocity.x * 100f).ToString(),
                "|",
                Mathf.RoundToInt(angularVelocity.y * 100f).ToString(),
                "|",
                normalizedReason,
                "|",
                guidanceMode.ToString());

            if (!string.Equals(lastLoggedManualAimSignature, signature, StringComparison.Ordinal))
            {
                lastLoggedManualAimSignature = signature;
                UnderdogsDebug.LogSpike($"ManualAimAngularVelocity={angularVelocity} reason={normalizedReason} mode={guidanceMode}");
            }
#endif
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
                    missileViewRequested = false;
                    closestMissileTargetDistance = float.PositiveInfinity;
                    SetManualAimAngularVelocity(Vector2.zero, "missile_destroyed");
                    SetMissileSlot(null);
                    // 重置锁定状态
                    ClearLockTarget("missile_destroyed");
                }
                return;
            }

            bool changed = currentMissile != activeMissile;
            activeMissile = currentMissile;
            if (changed)
            {
                closestMissileTargetDistance = float.PositiveInfinity;
                missileViewRequested = true;
            }
            SyncMissileFollow(activeMissile);
        }

        private LiveRound ResolveCurrentMissile()
        {
            if (guidanceUnit != null && guidanceUnit.CurrentMissiles != null)
            {
                for (int i = guidanceUnit.CurrentMissiles.Count - 1; i >= 0; i--)
                {
                    LiveRound round = guidanceUnit.CurrentMissiles[i];
                    if (round != null && !round.IsDestroyed)
                        return round;
                }
            }

            List<LiveRound> unguided = f_gu_unguidedMissiles != null ? f_gu_unguidedMissiles.GetValue(guidanceUnit) as List<LiveRound> : null;
            if (unguided != null)
            {
                for (int i = unguided.Count - 1; i >= 0; i--)
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
            LiveRound latest = null;
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

                latest = round;
            }

            return latest;
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
            // 场景切换时 CameraManager 可能已销毁
            if (CameraManager.Instance == null)
                return;

            CameraSlot targetSlot = FindPreferredReturnSlot();
            if (targetSlot != null && CameraSlot.ActiveInstance != targetSlot)
            {
                try { CameraSlot.SetActiveSlot(targetSlot); } catch { }
            }
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
            }
            else if (activeMissile == null)
            {
                guidanceMode = HasLockTarget() ? SpikeGuidanceMode.PreLaunchLock : SpikeGuidanceMode.Idle;
            }
            else
            {
                // Lock state is the primary guidance state: lock means FnF, no lock means MCLOS.
                guidanceMode = HasLockTarget() ? SpikeGuidanceMode.FnF : SpikeGuidanceMode.MCLOS;
            }

#if DEBUG
            if (guidanceMode != lastLoggedGuidanceMode)
            {
                lastLoggedGuidanceMode = guidanceMode;
                UnderdogsDebug.LogSpike($"Guidance mode => {guidanceMode} lockSource={GetLockSourceName()} activeOptic={GetActiveOpticName()}");
            }
#endif
        }

        private void UpdateLockTarget()
        {
            bool canControlLock = CanControlSpikeLock();

            if (!canControlLock)
            {
                // Exterior/commander view should block new lock interaction, but it must not
                // strip an already confirmed pre-launch or in-flight lock state.
                ClearTrackingCandidate();
                return;
            }

            RefreshTrackingCandidate();

            if (HasLockTarget())
            {
                if (TryGetLockAimPoint(out Vector3 preservedAimPoint))
                {
                    lastKnownAimPoint = preservedAimPoint;
                    hasLastKnownAimPoint = true;
                    RefreshLockedDistance(preservedAimPoint);
                }
                else
                {
                    ClearLockTarget("aim_point_lost");
                    if (activeMissile != null)
                        SetManualControlActive(true, false);
                    return;
                }

                lockConfirmed = true;
            }
        }

        private void RefreshTrackingCandidate()
        {
            Transform refTransform;
            Vector3 direction;
            if (!TryGetTrackingRay(out refTransform, out direction))
            {
                ClearTrackingCandidate();
                return;
            }

            Ray ray = new Ray(refTransform.position, direction);
            int vehicleLayer = 1 << 14;
            int terrainLayer = 1 << 18;
            int layerMask = vehicleLayer | terrainLayer;

            RaycastHit hit;
            if (!TryAcquireTrackingHit(ray, layerMask, out hit))
            {
                ClearTrackingCandidate();
                return;
            }

            Vehicle targetVehicle = hit.collider != null ? hit.collider.GetComponentInParent<Vehicle>() : null;
            if (targetVehicle == null || targetVehicle == vehicle)
            {
                ClearTrackingCandidate();
                return;
            }

            candidateVehicle = targetVehicle;
            candidateAnchor = ResolveTrackingAnchor(targetVehicle, hit.collider != null ? hit.collider.transform : null);
            candidateRenderer = ResolveTrackingRenderer(targetVehicle, candidateAnchor);
            candidateCollider = ResolveAnchorCollider(candidateAnchor);
            candidateDistance = hit.distance;
            candidateColliders = targetVehicle.GetComponentsInChildren<Collider>(true);
            candidateRenderers = targetVehicle.GetComponentsInChildren<Renderer>(true);
        }

        private bool TryAcquireTrackingHit(Ray ray, int layerMask, out RaycastHit selectedHit)
        {
            if (Physics.Raycast(ray, out RaycastHit directHit, LockRange, layerMask))
            {
                Vehicle directVehicle = directHit.collider != null ? directHit.collider.GetComponentInParent<Vehicle>() : null;
                if (directVehicle != null && directVehicle != vehicle)
                {
                    selectedHit = directHit;
                    return true;
                }
            }

            float baseRadius = GetBaseLockRadius();
            if (TryAcquireTrackingHit(ray, baseRadius, layerMask, out selectedHit))
                return true;

            float expandedRadius = GetExpandedLockRadius(baseRadius);
            if (expandedRadius > baseRadius + 0.01f && TryAcquireTrackingHit(ray, expandedRadius, layerMask, out selectedHit))
                return true;

            selectedHit = default(RaycastHit);
            return false;
        }

        private bool TryAcquireTrackingHit(Ray ray, float radius, int layerMask, out RaycastHit selectedHit)
        {
            selectedHit = default(RaycastHit);
            RaycastHit[] hits = Physics.SphereCastAll(ray, radius, LockRange, layerMask);
            if (hits == null || hits.Length == 0)
                return false;

            float nearestTerrainDistance = float.PositiveInfinity;
            float nearestVehicleDistance = float.PositiveInfinity;
            int nearestVehicleIndex = -1;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null)
                    continue;

                Vehicle hitVehicle = hit.collider.GetComponentInParent<Vehicle>();
                if (hitVehicle != null)
                {
                    if (hitVehicle == vehicle || hit.distance >= nearestVehicleDistance)
                        continue;

                    nearestVehicleDistance = hit.distance;
                    nearestVehicleIndex = i;
                    continue;
                }

                if (hit.distance < nearestTerrainDistance)
                    nearestTerrainDistance = hit.distance;
            }

            if (nearestVehicleIndex < 0)
                return false;

            if (!float.IsInfinity(nearestTerrainDistance) && nearestTerrainDistance + 0.5f < nearestVehicleDistance)
                return false;

            selectedHit = hits[nearestVehicleIndex];
            return true;
        }

        private bool TryGetTrackingRay(out Transform refTransform, out Vector3 direction)
        {
            refTransform = null;
            direction = Vector3.zero;

            if (IsMissileCameraActive() && activeMissile != null)
            {
                Camera missileCamera = CameraManager.MainCam;
                if (missileCamera != null)
                {
                    refTransform = missileCamera.transform;
                    direction = missileCamera.transform.forward;
                }
                else
                {
                    refTransform = activeMissile.transform;
                    direction = activeMissile.transform.forward;
                }
                return true;
            }

            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            if ((activeSlot == daySlot || activeSlot == thermalSlot) && fcs != null && fcs.ReferenceTransform != null)
            {
                refTransform = fcs.ReferenceTransform;
                direction = fcs.AimWorldVector;
            }
            else
            {
                Camera camera = CameraManager.MainCam;
                if (camera != null)
                {
                    refTransform = camera.transform;
                    direction = camera.transform.forward;
                }
                else if (fcs != null && fcs.ReferenceTransform != null)
                {
                    refTransform = fcs.ReferenceTransform;
                    direction = fcs.AimWorldVector;
                }
            }

            return refTransform != null && direction.sqrMagnitude > 0.001f;
        }

        private bool HasTrackingCandidate()
        {
            return candidateVehicle != null || candidateRenderer != null || candidateAnchor != null;
        }

        private void ClearTrackingCandidate()
        {
            candidateVehicle = null;
            candidateAnchor = null;
            candidateRenderer = null;
            candidateCollider = null;
            candidateColliders = null;
            candidateRenderers = null;
            candidateDistance = float.PositiveInfinity;
        }

        private void UpdateTargetPassFallback()
        {
            if (activeMissile == null || !HasLockTarget())
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
            ClearLockTarget("passed_target");
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
            lockedCollider = ResolveAnchorCollider(targetAnchor);
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
                return null;

            // Deliberately keep this exact. We only want the authored TRACKING OBJECT,
            // otherwise we fall through to the fallback anchor / aggregated bounds chain.
            Transform trackingObject = targetVehicle.transform.Find("TRACKING OBJECT");
            if (trackingObject == null)
            {
                Transform[] transforms = targetVehicle.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform candidate = transforms[i];
                    if (candidate != null && string.Equals(candidate.name, "TRACKING OBJECT", StringComparison.OrdinalIgnoreCase))
                    {
                        trackingObject = candidate;
                        break;
                    }
                }
            }

            if (trackingObject == null)
                MarderSpikeTrackingDimensions.TryEnsureFallbackAnchor(targetVehicle, out trackingObject);

            return trackingObject;
        }

        private Renderer ResolveTrackingRenderer(Vehicle targetVehicle, Transform preferredAnchor)
        {
            if (preferredAnchor != null && !MarderSpikeTrackingDimensions.IsFallbackAnchor(preferredAnchor))
            {
                Renderer preferredRenderer = preferredAnchor.GetComponent<Renderer>() ?? preferredAnchor.GetComponentInChildren<Renderer>(true);
                if (preferredRenderer != null)
                    return preferredRenderer;
            }

            return null;
        }

        private void UpdateAimProxy()
        {
            if (aimProxy == null)
                return;

            Camera activeCamera = ResolveDisplayCamera();
            Vector3 aimPoint;

            if (guidanceMode == SpikeGuidanceMode.MCLOS && activeMissile != null && !ShouldUseManualMissileControl())
            {
                aimPoint = activeMissile.transform.position + activeMissile.transform.forward * ProxyRange;
                aimProxy.position = aimPoint;
                aimProxy.rotation = Quaternion.LookRotation(activeMissile.transform.forward, Vector3.up);
                return;
            }

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

            if (guidanceMode != SpikeGuidanceMode.MCLOS || !ShouldUseManualMissileControl())
                SetManualAimAngularVelocity(Vector2.zero, guidanceMode == SpikeGuidanceMode.MCLOS ? "mclos_view_inactive" : "non_mclos");

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
                if (!TryGetLockAimPoint(out Vector3 targetAimPoint))
                {
                    ClearLockTarget("fnf_target_center_lost");
                    SetManualControlActive(true, true);
#if DEBUG
                    UnderdogsDebug.LogSpikeWarning("FnF target center lost, falling back to MCLOS");
#endif
                    return;
                }

                // Choose tracker based on top-attack mode captured at launch
                if (topAttackModeForCurrentMissile)
                {
                    // Use top-attack tracker
                    SpikeTopAttackTracker topTracker = activeMissile.GetComponent<SpikeTopAttackTracker>();
                    if (topTracker == null)
                    {
                        // Remove regular tracker if exists
                        SpikeInfraredTracker regularTracker = activeMissile.GetComponent<SpikeInfraredTracker>();
                        if (regularTracker != null)
                            Destroy(regularTracker);

                        topTracker = activeMissile.gameObject.AddComponent<SpikeTopAttackTracker>();
                    }

                    topTracker.UpdateLockSolution(
                        lockedVehicle,
                        lockedAnchor != null ? lockedAnchor : lockedRenderer != null ? lockedRenderer.transform : null,
                        lockedRenderer,
                        targetAimPoint);

                    if (topTracker.MissedTarget)
                    {
                        ClearLockTarget("top_tracker_missed_target");
                        SetManualControlActive(true, true);
#if DEBUG
                        UnderdogsDebug.LogSpike("Auto switch: FnF → MCLOS (missed target - top attack)");
#endif
                        return;
                    }
                }
                else
                {
                    // Use regular infrared tracker
                    SpikeInfraredTracker tracker = activeMissile.GetComponent<SpikeInfraredTracker>();
                    if (tracker == null)
                    {
                        tracker = activeMissile.gameObject.AddComponent<SpikeInfraredTracker>();
                    }

                    tracker.UpdateLockSolution(
                        lockedVehicle,
                        lockedAnchor != null ? lockedAnchor : lockedRenderer != null ? lockedRenderer.transform : null,
                        lockedRenderer,
                        targetAimPoint);

                    if (tracker.MissedTarget)
                    {
                        ClearLockTarget("tracker_missed_target");
                        SetManualControlActive(true, true);
#if DEBUG
                        UnderdogsDebug.LogSpike("Auto switch: FnF → MCLOS (missed target)");
#endif
                        return;
                    }
                }

                activeMissile.SkipGuidanceLockout = true;
                activeMissile.Guided = true;

                if (fnfGuideTransform != null && guidanceUnit.AimElement != fnfGuideTransform)
                    guidanceUnit.AimElement = fnfGuideTransform;
            }
        }

        private bool TryGetLockAimPoint(out Vector3 aimPoint)
        {
            Bounds bounds;
            if (TryGetTargetBounds(out bounds))
            {
                aimPoint = bounds.center;
                return true;
            }

            if (TryGetBoundsFromAnchor(lockedAnchor, out bounds))
            {
                aimPoint = bounds.center;
                return true;
            }

            if (TryGetBoundsFromRenderer(lockedRenderer, out bounds))
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

            if (TryBuildBoundsFromColliders(out bounds) || TryBuildBoundsFromRenderers(out bounds))
            {
                CacheTargetBounds(bounds);
                LogBoundsSource(true, lockedColliders != null && lockedColliders.Length > 0 ? "vehicle_colliders" : "vehicle_renderers");
                return true;
            }

            if (TryGetBoundsFromAnchor(lockedAnchor, out bounds))
            {
                CacheTargetBounds(bounds);
                LogBoundsSource(true, MarderSpikeTrackingDimensions.IsFallbackAnchor(lockedAnchor) ? "fallback_anchor" : "tracking_anchor");
                return true;
            }

            if (TryGetBoundsFromRenderer(lockedRenderer, out bounds))
            {
                CacheTargetBounds(bounds);
                LogBoundsSource(true, "tracking_renderer");
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
            LogBoundsSource(true, "none");
            return false;
        }

        private bool TryGetCandidateBounds(out Bounds bounds)
        {
            if (TryBuildBoundsFromColliders(candidateColliders, out bounds) || TryBuildBoundsFromRenderers(candidateRenderers, out bounds))
            {
                LogBoundsSource(false, candidateColliders != null && candidateColliders.Length > 0 ? "vehicle_colliders" : "vehicle_renderers");
                return true;
            }

            if (TryGetBoundsFromAnchor(candidateAnchor, out bounds))
            {
                LogBoundsSource(false, MarderSpikeTrackingDimensions.IsFallbackAnchor(candidateAnchor) ? "fallback_anchor" : "tracking_anchor");
                return true;
            }

            if (TryGetBoundsFromRenderer(candidateRenderer, out bounds))
            {
                LogBoundsSource(false, "tracking_renderer");
                return true;
            }

            bounds = default(Bounds);
            LogBoundsSource(false, "none");
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
            return TryBuildBoundsFromColliders(lockedColliders, out bounds);
        }

        private bool TryBuildBoundsFromColliders(Collider[] colliders, out Bounds bounds)
        {
            if (colliders != null && colliders.Length > 0)
            {
                bool initialized = false;
                bounds = new Bounds();
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider collider = colliders[i];
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
            return TryBuildBoundsFromRenderers(lockedRenderers, out bounds);
        }

        private bool TryBuildBoundsFromRenderers(Renderer[] renderers, out Bounds bounds)
        {
            if (renderers != null && renderers.Length > 0)
            {
                bool initialized = false;
                bounds = new Bounds();
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
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

        private Collider ResolveAnchorCollider(Transform anchor)
        {
            if (anchor == null)
                return null;

            Collider collider = anchor.GetComponent<Collider>() ?? anchor.GetComponentInChildren<Collider>(true);
            if (collider != null && collider.enabled && !collider.isTrigger)
                return collider;

            return null;
        }

        private bool TryGetBoundsFromRenderer(Renderer renderer, out Bounds bounds)
        {
            if (renderer != null)
            {
                bounds = renderer.bounds;
                return true;
            }

            bounds = default(Bounds);
            return false;
        }

        private bool TryGetBoundsFromAnchor(Transform anchor, out Bounds bounds)
        {
            if (anchor == null)
            {
                bounds = default(Bounds);
                return false;
            }

            if (MarderSpikeTrackingDimensions.TryGetFallbackBounds(anchor, out bounds))
                return true;

            Renderer anchorRenderer = anchor.GetComponent<Renderer>() ?? anchor.GetComponentInChildren<Renderer>(true);
            if (anchorRenderer != null)
            {
                bounds = anchorRenderer.bounds;
                return true;
            }

            Collider anchorCollider = ResolveAnchorCollider(anchor);
            if (anchorCollider != null)
            {
                bounds = anchorCollider.bounds;
                return true;
            }

            bounds = default(Bounds);
            return false;
        }

        private Camera ResolveDisplayCamera()
        {
            return CameraManager.MainCam;
        }

        private bool TryProjectWorldPointToScreen(Vector3 worldPoint, Camera camera, out Vector3 screenPoint)
        {
            screenPoint = Vector3.zero;
            if (camera == null)
                return false;

            screenPoint = camera.WorldToScreenPoint(worldPoint);
            return screenPoint.z > 0f;
        }

        private string GetLockSourceName()
        {
            if (lockedRenderer != null)
                return "tracking_renderer";
            if (lockedAnchor != null)
                return MarderSpikeTrackingDimensions.IsFallbackAnchor(lockedAnchor) ? "fallback_anchor" : "tracking_anchor";
            if (lockedVehicle != null)
                return "vehicle";
            if (hasLastKnownAimPoint)
                return "last_known";
            return "none";
        }

        private UsableOptic GetActiveOptic()
        {
            if (IsMissileCameraActive())
                return thermalOptic != null ? thermalOptic : missileOptic != null ? missileOptic : dayOptic;

            CameraSlot activeSlot = CameraSlot.ActiveInstance;
            if (activeSlot == thermalSlot)
                return thermalOptic;
            if (activeSlot == daySlot)
                return dayOptic;

            return activeSlot != null ? activeSlot.GetComponentInParent<UsableOptic>() : null;
        }

        private string GetActiveOpticName()
        {
            UsableOptic optic = GetActiveOptic();
            return optic != null ? optic.name : "none";
        }

        private void LogBoundsSource(bool lockedTarget, string source)
        {
#if DEBUG
            string lastSource = lockedTarget ? lastLoggedTargetBoundsSource : lastLoggedCandidateBoundsSource;
            if (string.Equals(lastSource, source, StringComparison.Ordinal))
                return;

            if (lockedTarget)
                lastLoggedTargetBoundsSource = source;
            else
                lastLoggedCandidateBoundsSource = source;
            UnderdogsDebug.LogSpike($"{(lockedTarget ? "Lock" : "Candidate")} bounds source => {source}");
#endif
        }

        private void EnsureOverlay()
        {
            if (overlayObject != null)
                return;

            overlayObject = new GameObject(OverlayName);
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
            topAttackCueRect = CreateRect("TOP ATTACK CUE", overlayRoot, Vector2.zero, new Vector2(100f, 100f));
            CreateDiagonalCross(topAttackCueRect, 84f, 5f);
            topAttackCueRect.gameObject.SetActive(false);
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

            bool showCenterReticle = IsMissileCameraActive() || CameraSlot.ActiveInstance == thermalSlot;
            if (overlayReticleRoot != null && overlayReticleRoot.gameObject.activeSelf != showCenterReticle)
                overlayReticleRoot.gameObject.SetActive(showCenterReticle);

            UpdateTopAttackCue();

            Rect targetRect;
            bool hasTargetRect = TryGetDisplayTargetScreenRect(out targetRect);
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
            else if (hasLock && TryGetLockScreenPoint(out Vector2 lockScreenPoint))
            {
                anchoredPosition = ScreenToCanvas(ClampScreenPoint(lockScreenPoint), overlayRoot.sizeDelta);
            }
            else if (hasLock)
            {
                anchoredPosition = Vector2.zero;
            }
            else
            {
                lockBoxRect.gameObject.SetActive(false);
                return;
            }

            lockBoxRect.sizeDelta = size;
            lockBoxRect.anchoredPosition = anchoredPosition;
            if (!lockBoxRect.gameObject.activeSelf)
                lockBoxRect.gameObject.SetActive(true);
        }

        private void UpdateTopAttackCue()
        {
            if (topAttackCueRect == null || overlayRoot == null)
                return;

            if (!TryGetTopAttackCueScreenPoint(out Vector2 screenPoint))
            {
                if (topAttackCueRect.gameObject.activeSelf)
                    topAttackCueRect.gameObject.SetActive(false);
                return;
            }

            topAttackCueRect.anchoredPosition = ScreenToCanvas(ClampScreenPoint(screenPoint), overlayRoot.sizeDelta);
            topAttackCueRect.localScale = Vector3.one;
            if (!topAttackCueRect.gameObject.activeSelf)
                topAttackCueRect.gameObject.SetActive(true);
        }

        private bool TryGetDisplayTargetScreenRect(out Rect rect)
        {
            if (HasLockTarget())
                return TryGetTargetScreenRect(out rect);

            if (HasTrackingCandidate())
                return TryGetCandidateScreenRect(out rect);

            rect = new Rect();
            return false;
        }

        private bool TryGetTargetScreenRect(out Rect rect)
        {
            if (hasCachedTargetScreenRect && Time.unscaledTime < nextTargetScreenRectRefreshTime)
            {
                rect = cachedTargetScreenRect;
                return rect.width > 1f && rect.height > 1f;
            }

            rect = new Rect();
            Bounds bounds;
            if (!TryGetTargetBounds(out bounds))
                return false;

            return TryProjectBoundsToScreen(bounds, out rect, cacheResult: true);
        }

        private bool TryGetCandidateScreenRect(out Rect rect)
        {
            rect = new Rect();
            Bounds bounds;
            if (!TryGetCandidateBounds(out bounds))
                return false;

            return TryProjectBoundsToScreen(bounds, out rect, cacheResult: false);
        }

        private bool TryGetLockScreenPoint(out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            Camera camera = ResolveDisplayCamera();
            if (camera == null || !TryGetLockAimPoint(out Vector3 aimPoint))
                return false;

            if (!TryProjectWorldPointToScreen(aimPoint, camera, out Vector3 projectedPoint))
                return false;

            screenPoint = new Vector2(projectedPoint.x, projectedPoint.y);
            return true;
        }

        private bool TryGetTopAttackCueScreenPoint(out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            Camera camera = ResolveDisplayCamera();
            if (camera == null || !TryGetTopAttackCueWorldPoint(out Vector3 cueWorldPoint, out bool diveCommitted))
                return false;

            if (!TryProjectWorldPointToScreen(cueWorldPoint, camera, out Vector3 projectedPoint))
            {
                if (TryGetLockScreenPoint(out Vector2 lockScreenPoint))
                {
                    screenPoint = lockScreenPoint;
                }
                else if (diveCommitted)
                {
                    screenPoint = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                screenPoint = new Vector2(projectedPoint.x, projectedPoint.y);
            }

            return true;
        }

        private bool TryGetTopAttackCueWorldPoint(out Vector3 worldPoint, out bool diveCommitted)
        {
            worldPoint = Vector3.zero;
            diveCommitted = false;

            if (activeMissile == null || activeMissile.IsDestroyed || !topAttackModeForCurrentMissile)
                return false;

            SpikeTopAttackTracker tracker = activeMissile.GetComponent<SpikeTopAttackTracker>();
            if (tracker != null)
            {
                if (!tracker.ProfileActive)
                    return false;

                diveCommitted = tracker.DiveCommitted;
                if (tracker.TryGetCurrentAimPoint(out worldPoint))
                    return true;
            }

            if (!TryGetLockAimPoint(out Vector3 targetCenter))
                return false;

            Transform sourceTransform = activeMissile != null && !activeMissile.IsDestroyed
                ? activeMissile.transform
                : fcs != null && fcs.ReferenceTransform != null ? fcs.ReferenceTransform : transform;
            Vector3 sourcePosition = sourceTransform != null ? sourceTransform.position : transform.position;
            float sourceDistance = Vector3.Distance(sourcePosition, targetCenter);
            float attackHeight = SpikeTopAttackTracker.CalculateAttackHeight(sourceDistance);

            if (activeMissile == null || activeMissile.IsDestroyed)
            {
                worldPoint = SpikeTopAttackTracker.BuildClimbAimPoint(targetCenter, attackHeight);
                return true;
            }

            worldPoint = SpikeTopAttackTracker.GetGuidanceAimPoint(
                sourcePosition,
                targetCenter,
                attackHeight,
                sourceDistance);
            return true;
        }

        private bool TryProjectBoundsToScreen(Bounds bounds, out Rect rect, bool cacheResult)
        {
            rect = new Rect();

            Camera camera = ResolveDisplayCamera();
            if (camera == null)
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
                if (!TryProjectWorldPointToScreen(targetBoundsCorners[i], camera, out Vector3 screen) || screen.z <= 0f)
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
            if (cacheResult)
            {
                cachedTargetScreenRect = rect;
                hasCachedTargetScreenRect = true;
                nextTargetScreenRectRefreshTime = Time.unscaledTime + LockScreenRectRefreshInterval;
            }
            return true;
        }

        private float GetDistanceScaledLockSize()
        {
            float distance = !float.IsInfinity(lockedDistance) ? lockedDistance : !float.IsInfinity(candidateDistance) ? candidateDistance : 4000f;
            float t = Mathf.InverseLerp(4000f, 150f, distance);
            return Mathf.Lerp(72f, 260f, t);
        }

        private float GetBaseLockRadius()
        {
#if DEBUG
            return Mathf.Max(UnderdogsDebug.SpikeLockRadius, 0.1f);
#else
            return 2.0f;
#endif
        }

        private float GetExpandedLockRadius(float baseRadius)
        {
            Camera camera = CameraManager.MainCam;
            float fov = camera != null ? camera.fieldOfView : daySlot != null ? daySlot.CurrentFov : 10f;
            float distanceHint = !float.IsInfinity(lockedDistance) ? lockedDistance : !float.IsInfinity(candidateDistance) ? candidateDistance : 2500f;
            float distanceScale = Mathf.Lerp(1f, 3.5f, Mathf.InverseLerp(500f, 4500f, distanceHint));
            float fovScale = Mathf.Clamp(fov / 10f, 0.75f, 1.6f);
            return Mathf.Clamp(baseRadius * distanceScale * fovScale, baseRadius, 12f);
        }

        private static Vector2 ClampScreenPoint(Vector2 screenPoint)
        {
            const float edgeMargin = 28f;
            float maxX = Mathf.Max(edgeMargin, Screen.width - edgeMargin);
            float maxY = Mathf.Max(edgeMargin, Screen.height - edgeMargin);
            return new Vector2(
                Mathf.Clamp(screenPoint.x, edgeMargin, maxX),
                Mathf.Clamp(screenPoint.y, edgeMargin, maxY));
        }

        private Vector2 ScreenToCanvas(Vector2 screenPoint, Vector2 canvasSize)
        {
            float x = (screenPoint.x / Mathf.Max(Screen.width, 1)) - 0.5f;
            float y = (screenPoint.y / Mathf.Max(Screen.height, 1)) - 0.5f;
            return new Vector2(x * canvasSize.x, y * canvasSize.y);
        }

        private bool HasLockTarget()
        {
            bool result = lockConfirmed && (lockedVehicle != null || lockedRenderer != null || lockedAnchor != null);
            return result;
        }

        private bool CanControlSpikeLock()
        {
            bool newResult = IsMissileCameraActive()
                ? IsPlayerControllingThisRig() && IsSpikeWeaponSelected() && activeMissile != null
                : CanUseInteriorSpikeView();
            if (newResult != lastCanControlSpikeLock)
            {
                lastCanControlSpikeLock = newResult;
                UnderdogsDebug.LogSpike($"CanControlSpikeLock changed to {newResult}");
            }

            return newResult;
        }

        private bool IsAnySpikeViewActive()
        {
            if (IsMissileCameraActive() && activeMissile != null)
                return true;

            CameraSlot active = CameraSlot.ActiveInstance;
            if (active == null)
                return false;

            if (!IsSpikeWeaponSelected())
                return false;

            return active == thermalSlot || active == daySlot;
        }

        private bool IsScopeSuppressionViewActive()
        {
            if (IsMissileCameraActive() && activeMissile != null)
                return true;

            CameraSlot active = CameraSlot.ActiveInstance;
            if (active == null || !IsSpikeWeaponSelected())
                return false;

            return active == thermalSlot;
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

            bool matched = MarderSpikeSystem.MatchesExactSlotIdentity(
                slot,
                MarderSpikeSystem.CommanderSlotExactNames,
                MarderSpikeSystem.CommanderSlotExactPathSuffixes);

#if DEBUG
            if (matched)
                UnderdogsDebug.LogSpike($"CommanderLikeSlot exact match => {MarderSpikeSystem.DescribeSlot(slot)}");
#endif
            return matched;
        }

        private void LogViewGateState(CameraSlot activeSlot, bool allowed, string reason)
        {
#if DEBUG
            CameraManager cameraManager = CameraManager.Instance;
            string signature = string.Concat(
                reason,
                "|allowed=", allowed,
                "|selected=", IsSpikeWeaponSelected(),
                "|player=", IsPlayerControllingThisRig(),
                "|ext=", cameraManager != null && cameraManager.ExteriorMode,
                "|slot=", MarderSpikeSystem.DescribeSlot(activeSlot));

            if (string.Equals(lastLoggedViewGateSignature, signature, StringComparison.Ordinal))
                return;

            lastLoggedViewGateSignature = signature;
            UnderdogsDebug.LogSpike($"Spike view gate => {signature}");
#endif
        }

        private void CleanupMissileSlotIfNeeded()
        {
            if (activeMissile != null)
                return;

            // 场景切换时 CameraManager 可能已销毁，跳过操作避免卡死
            CameraManager cameraManager = CameraManager.Instance;
            if (cameraManager == null)
            {
                // 直接清理对象，不尝试切换 slot
                if (missileSlotObject != null)
                {
                    Destroy(missileSlotObject);
                    missileSlotObject = null;
                    SetMissileSlot(null);
                }
                return;
            }

            if (missileSlot != null && CameraSlot.ActiveInstance == missileSlot)
            {
                CameraSlot targetSlot = FindPreferredReturnSlot();
                if (targetSlot != null && targetSlot != missileSlot)
                {
                    try { CameraSlot.SetActiveSlot(targetSlot); } catch { }
                }
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
            bool shouldHide = IsScopeSuppressionViewActive();
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
            bool shouldHide = IsScopeSuppressionViewActive();
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
            string combined = (n1 + " " + n2 + " " + n3);
            return combined.Equals("Scope Sprite Scope", StringComparison.OrdinalIgnoreCase);
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
            return MarderSpikeAssets.CreateConfiguredBlitMaterial();
        }

        private Material GetWhiteHotBlitMaterial()
        {
            if (cachedWhiteHotBlitMaterial != null)
                return cachedWhiteHotBlitMaterial;

            Material source = UEResourceController.GetThermalFlirWhiteBlitMaterialNoScope();
            if (source != null)
            {
                cachedWhiteHotBlitMaterial = UEResourceController.CreateThermalMaterial(source, GetWhiteHotThermalConfig());
                return cachedWhiteHotBlitMaterial;
            }

            source = UEResourceController.GetThermalFlirBlitMaterial();
            if (source != null)
            {
                cachedWhiteHotBlitMaterial = UEResourceController.CreateThermalMaterial(source, GetWhiteHotThermalConfig());
                return cachedWhiteHotBlitMaterial;
            }

            return null;
        }

        private ThermalConfig GetWhiteHotThermalConfig()
        {
            return new ThermalConfig
            {
                ColorMode = ThermalColorMode.WhiteHot,
                ColdColor = new Color(0f, 0f, 0f, 1f),
                HotColor = new Color(1f, 1f, 1f, 1f)
            };
        }

        private void LogThermalState(string label, GameObject slotObject, CameraSlot slot, SimpleNightVision snv, PostProcessVolume flirOnlyVolume)
        {
            if (slot == null)
                return;

            string objectName = slotObject != null ? slotObject.name : "null";
            string materialName = slot.FLIRBlitMaterialOverride != null ? slot.FLIRBlitMaterialOverride.name : "null";
            string opticName = slot.PairedOptic != null ? slot.PairedOptic.name : "null";
            string linkedDay = slot.LinkedDaySight != null ? slot.LinkedDaySight.name : "null";
            string linkedNight = slot.LinkedNightSight != null ? slot.LinkedNightSight.name : "null";
            Vector3 slotLocalPos = slot.transform.localPosition;
            Vector3 dayLocalPos = daySlot != null ? daySlot.transform.localPosition : Vector3.zero;
            Vector3 delta = daySlot != null ? slotLocalPos - dayLocalPos : slotLocalPos;
            string signature = string.Concat(
                label, "|",
                slot.name, "|",
                objectName, "|",
                (CameraSlot.ActiveInstance == slot) ? "1" : "0", "|",
                slot.VisionType.ToString(), "|",
                materialName, "|",
                opticName, "|",
                linkedDay, "|",
                linkedNight, "|",
                snv != null ? "1" : "0", "|",
                flirOnlyVolume != null ? flirOnlyVolume.name : "null", "|",
                slot.DefaultFov.ToString());

            if (string.Equals(lastThermalLogSignature, signature, StringComparison.Ordinal))
                return;

            lastThermalLogSignature = signature;

            UnderdogsDebug.LogSpike(
                $"[Marder Spike] {label}: slot={slot.name}, object={objectName}, active={CameraSlot.ActiveInstance == slot}, vision={slot.VisionType}, material={materialName}, pairedOptic={opticName}, linkedDay={linkedDay}, linkedNight={linkedNight}, snv={(snv != null)}, post={(flirOnlyVolume != null ? flirOnlyVolume.name : "null")}, slotLocal={slotLocalPos}, deltaVsDay={delta}, fov={slot.DefaultFov}");
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

        private void CreateDiagonalCross(RectTransform parent, float length, float thickness)
        {
            CreateRotatedBar(parent, "DiagA", Vector2.zero, new Vector2(length, thickness), 45f);
            CreateRotatedBar(parent, "DiagB", Vector2.zero, new Vector2(length, thickness), -45f);
        }

        private void CreateBar(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchoredPosition, size);
            AddOverlayImage(rect);
        }

        private void CreateRotatedBar(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 size, float zRotation)
        {
            RectTransform rect = CreateRect(name, parent, anchoredPosition, size);
            rect.localRotation = Quaternion.Euler(0f, 0f, zRotation);
            AddOverlayImage(rect);
        }

        private void AddOverlayImage(RectTransform rect)
        {
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

    internal sealed class MarderSpikeThermalOpticMarker : MonoBehaviour
    {
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

    [HarmonyPatch(typeof(GHPC.UI.Hud.WeaponHud), "Update")]
    internal static class MarderSpikeTopAttackHudPatch
    {
        private static void Postfix(GHPC.UI.Hud.WeaponHud __instance)
        {
            GHPC.Player.PlayerInput playerInput = __instance?._playerInput;
            Vehicle playerVehicle = playerInput?.CurrentPlayerUnit as Vehicle;
            if (playerVehicle == null)
                return;

            // Find the MarderSpikeRig for this vehicle
            WeaponSystemInfo weaponInfo = playerInput.CurrentPlayerWeapon;
            if (weaponInfo?.Weapon == null)
                return;

            MarderSpikeRig rig = weaponInfo.FCS?.GetComponent<MarderSpikeRig>();
            if (rig == null || !rig.IsConfigured || !rig.TopAttackModeEnabled)
                return;

            // Add top-attack text to HUD
            __instance._hudText.text = __instance._sb.ToString() + "\nTOP-ATTACK";
        }
    }
}
