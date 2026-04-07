#if DEBUG
using GHPC;
using GHPC.Camera;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using Reticle;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public static class UnderdogsDebug
    {
        // Debug标志 - 改为const编译时常量
        public const bool DEBUG_TIMING = false;   // [UE] 时机日志
        public const bool DEBUG_MCLOS = false;    // [BMP-1 MCLOS] 日志
        public const bool DEBUG_EMES = false;     // [EMES18] 热像初始化日志
        public const bool DEBUG_LRF = false;      // LRF 改装日志
        public const bool DEBUG_LEO = false;      // [Leopard1] Spawn转换与模型改装日志
        public const bool DEBUG_VEHICLE = false; // 车辆调试子开关
        public const bool DEBUG_ARMOR = false;   // 装甲数据调试子开关
        public const bool DEBUG_CHILDREN = false; // 子节点结构调试子开关
        public const bool DEBUG_SPIKE = false;    // [Marder Spike] 导弹系统日志
        public const bool DEBUG_REPAIR = false;  // [Repair] 修复功能日志

        private static GameObject _displayDebugUiHost;

        // 条件日志方法
        public static void Log(string msg)
        {
            MelonLogger.Msg(msg);
        }

        public static void LogTiming(string msg)
        {
            if (DEBUG_TIMING) MelonLogger.Msg(msg);
        }

        public static void LogMCLOS(string msg)
        {
            if (DEBUG_MCLOS) MelonLogger.Msg(msg);
        }

        public static void LogEMES(string msg)
        {
            if (DEBUG_EMES) MelonLogger.Msg(msg);
        }

        public static void LogEMESWarning(string msg)
        {
            if (DEBUG_EMES) MelonLogger.Warning(msg);
        }

        public static void LogLRF(string msg)
        {
            if (DEBUG_LRF) MelonLogger.Msg(msg);
        }

        public static void LogLeo(string msg)
        {
            if (DEBUG_LEO) MelonLogger.Msg(msg);
        }

        public static void LogLeoWarning(string msg)
        {
            if (DEBUG_LEO) MelonLogger.Warning(msg);
        }

        public static void LogVehicle(string msg)
        {
            if (DEBUG_VEHICLE) MelonLogger.Msg(msg);
        }

        public static void LogArmor(string msg)
        {
            if (DEBUG_ARMOR) MelonLogger.Msg(msg);
        }

        public static void LogSpike(string msg)
        {
            if (DEBUG_SPIKE) MelonLogger.Msg(msg);
        }

        public static void LogSpikeWarning(string msg)
        {
            if (DEBUG_SPIKE) MelonLogger.Warning(msg);
        }

        public static void LogRepair(string msg)
        {
            if (DEBUG_REPAIR) MelonLogger.Msg(msg);
        }

        // SPIKE 锁定半径配置（可在 DebugUI 中调整）
        private static float _spikeLockRadius = 2f;
        public static float SpikeLockRadius
        {
            get => _spikeLockRadius;
            set => _spikeLockRadius = Mathf.Clamp(value, 0.1f, 10f);
        }

        public static void EnsureDisplayDebugUiHost()
        {
            if (FCSDebugUI.Instance != null)
            {
                _displayDebugUiHost = FCSDebugUI.Instance.gameObject;
                return;
            }

            if (_displayDebugUiHost != null)
            {
                if (_displayDebugUiHost.GetComponent<FCSDebugUI>() == null)
                    _displayDebugUiHost.AddComponent<FCSDebugUI>();
                return;
            }

            _displayDebugUiHost = new GameObject("__UE_DISPLAY_DEBUG_UI__");
            _displayDebugUiHost.hideFlags = HideFlags.HideAndDontSave;
            GameObject.DontDestroyOnLoad(_displayDebugUiHost);
            _displayDebugUiHost.AddComponent<FCSDebugUI>();
        }

        public static void HandleDebugKeys()
        {
            EMES18Optic.TickGlobalDefaultScopeState();
            EnsureDisplayDebugUiHost();

            if (Input.GetKeyDown(KeyCode.F8))
            {
                EnsureDisplayDebugUiHost();
                FCSDebugUI.Instance?.ToggleWindow();
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                EnsureDisplayDebugUiHost();
                FCSDebugUI.Instance?.RefreshWindowTargets();
            }

            if (!Input.GetKeyDown(KeyCode.P)) return;

            var cm = CameraManager.Instance;
            if (cm == null) { MelonLogger.Msg("[DEBUG-P] CameraManager 未找到"); return; }

            MelonLogger.Msg("=== [DEBUG-P] 摄像机状态 ===");
            MelonLogger.Msg($"  MainCam: {CameraManager.MainCam?.name ?? "null"}");
            MelonLogger.Msg($"  MainCam parent: {CameraManager.MainCam?.transform.parent?.name ?? "null"}");
            MelonLogger.Msg($"  ActiveInstance: {CameraSlot.ActiveInstance?.name ?? "null"}");
            MelonLogger.Msg($"  ExteriorMode: {cm.ExteriorMode}");
            MelonLogger.Msg($"  EMES ForceHideSprites: {EMES18Optic.DebugForceHideSprites}");

            var allSlots = typeof(CameraManager)
                .GetField("_allCamSlots", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(cm) as CameraSlot[];
            if (allSlots != null)
            {
                MelonLogger.Msg($"  已注册 CameraSlot 数量: {allSlots.Length}");
                foreach (var s in allSlots)
                    if (s != null)
                        MelonLogger.Msg($"    [{(s.IsActive ? "*" : " ")}] {s.name} | Fov={s.DefaultFov}");
            }

            try
            {
                var f_spriteMgr = typeof(CameraManager).GetField("_spriteManager", BindingFlags.Instance | BindingFlags.NonPublic);
                var spriteMgr = f_spriteMgr?.GetValue(cm);
                if (spriteMgr != null)
                {
                    var f_sprites = spriteMgr.GetType().GetField("Sprites", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var sprites = f_sprites?.GetValue(spriteMgr) as System.Array;
                    if (sprites != null)
                    {
                        MelonLogger.Msg($"  CameraSprite 数量: {sprites.Length}");
                        var f_type = spriteMgr.GetType().GetNestedType("CameraSpriteData", BindingFlags.Public | BindingFlags.NonPublic)?.GetField("Type", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var f_obj = spriteMgr.GetType().GetNestedType("CameraSpriteData", BindingFlags.Public | BindingFlags.NonPublic)?.GetField("SpriteObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        foreach (var data in sprites)
                        {
                            if (data == null) continue;
                            var st = f_type?.GetValue(data);
                            var go = f_obj?.GetValue(data) as GameObject;
                            var sr = go != null ? go.GetComponent<SpriteRenderer>() : null;
                            var cv = go != null ? go.GetComponent<Canvas>() : null;
                            MelonLogger.Msg($"    Sprite[{st}] obj={go?.name ?? "null"} active={go?.activeSelf} renderer={(sr != null ? sr.enabled.ToString() : "null")} canvas={(cv != null ? cv.enabled.ToString() : "null")}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[DEBUG-P] CameraSprite dump failed: {ex.Message}");
            }

            var activeSlot = CameraSlot.ActiveInstance;
            if (activeSlot != null && !activeSlot.IsExterior)
            {
                MelonLogger.Msg($"\n=== 当前激活瞄准镜: {activeSlot.name} ===");
                var usableOptic = activeSlot.GetComponentInParent<GHPC.Equipment.Optics.UsableOptic>();
                if (usableOptic != null)
                {
                    MelonLogger.Msg($"  Path: {GetPath(usableOptic.transform, usableOptic.transform.root)}");
                    MelonLogger.Msg($"  VisionType: {activeSlot.VisionType}");
                    MelonLogger.Msg($"  FLIR: {activeSlot.FLIRWidth}x{activeSlot.FLIRHeight}");
                    MelonLogger.Msg($"  ReticleSO: {usableOptic.reticleMesh?.reticleSO?.name ?? "null"}");

                    var components = usableOptic.GetComponents<Component>();
                    MelonLogger.Msg($"  组件列表 ({components.Length}):");
                    foreach (var comp in components)
                        MelonLogger.Msg($"    - {comp.GetType().Name}");

                    MelonLogger.Msg("  子节点结构:");
                    PrintChildrenDetailed(usableOptic.transform, 2);
                }
            }
        }

        public static void DumpVehicleOpticsSnapshot(Vehicle v, string tag, bool detailedOptics = false)
        {
            if (v == null) return;

            try
            {
                var cachedReticles = typeof(ReticleMesh).GetField("cachedReticles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as System.Collections.IDictionary;
                MelonLogger.Msg($"[A1A3DUMP:{tag}] veh={v.FriendlyName} obj={v.gameObject.name} tag={v.gameObject.tag}");

                foreach (var cs in v.gameObject.GetComponentsInChildren<CameraSlot>(true).OrderBy(x => GetPath(x.transform, v.transform)))
                {
                    string otherFovs = string.Join(", ", cs.OtherFovs ?? new float[0]);
                    string linkedNight = cs.LinkedNightSight != null ? cs.LinkedNightSight.name : "null";
                    string linkedDay = cs.LinkedDaySight != null ? cs.LinkedDaySight.name : "null";
                    string paired = cs.PairedOptic != null ? cs.PairedOptic.name : "null";
                    MelonLogger.Msg($"  CameraSlot: {GetPath(cs.transform, v.transform)} | enabled={cs.enabled} vision={cs.VisionType} sprite={cs.SpriteType} DefaultFov={cs.DefaultFov} OtherFovs=[{otherFovs}] linkedNight={linkedNight} linkedDay={linkedDay} paired={paired} nightOnly={cs.NightSightAtNightOnly} linked={cs.IsLinkedNightSight}");
                }

                var hasGuidanceField = typeof(GHPC.Equipment.Optics.UsableOptic).GetField("_hasGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var uo in v.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true).OrderBy(x => GetPath(x.transform, v.transform)))
                {
                    bool hasGuidance = hasGuidanceField != null && (bool)hasGuidanceField.GetValue(uo);
                    var slot = uo.slot;
                    string soName = uo.reticleMesh?.reticleSO?.name ?? "null";
                    bool isCached = cachedReticles != null && cachedReticles.Contains(soName);
                    string slotName = slot != null ? slot.name : "null";
                    string slotVision = slot != null ? slot.VisionType.ToString() : "null";
                    string slotSprite = slot != null ? slot.SpriteType.ToString() : "null";
                    string linkedNight = slot?.LinkedNightSight != null ? slot.LinkedNightSight.name : "null";
                    string linkedDay = slot?.LinkedDaySight != null ? slot.LinkedDaySight.name : "null";
                    string paired = slot?.PairedOptic != null ? slot.PairedOptic.name : "null";
                    MelonLogger.Msg($"  UsableOptic: {GetPath(uo.transform, v.transform)} | enabled={uo.enabled} slot={slotName} vision={slotVision} sprite={slotSprite} linkedNight={linkedNight} linkedDay={linkedDay} paired={paired} GuidanceLight={uo.GuidanceLight} _hasGuidance={hasGuidance} FCS={uo.FCS?.name ?? "null"}");
                    MelonLogger.Msg($"    reticleSO: {soName} | cached={isCached}");

                    var tree = uo.reticleMesh?.reticleSO;
                    if (tree != null)
                    {
                        foreach (var plane in tree.planes)
                        {
                            for (int ei = 0; ei < plane.elements.Count; ei++)
                                MelonLogger.Msg($"    plane element[{ei}]: {plane.elements[ei].GetType().Name}");
                        }
                    }

                    if (detailedOptics && (uo.name == "GPS" || uo.name == "B 171" || uo.name == "PZB-200"))
                    {
                        MelonLogger.Msg($"    子节点结构({uo.name}):");
                        PrintChildrenDetailed(uo.transform, 6);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[A1A3DUMP:{tag}] failed: {ex.Message}");
            }
        }

        public static void PrintChildrenDetailed(Transform t, int indent)
        {
            string prefix = new string(' ', indent);
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                var components = child.GetComponents<Component>();
                string compStr = string.Join(", ", components.Select(c => c.GetType().Name));
                MelonLogger.Msg($"{prefix}{child.name} [active={child.gameObject.activeSelf}] [{compStr}]");

                var renderer = child.GetComponent<Renderer>();
                if (renderer != null && renderer.material != null)
                    MelonLogger.Msg($"{prefix}  RendererEnabled: {renderer.enabled} | Material: {renderer.material.name} Shader: {renderer.material.shader.name}");
                var canvas = child.GetComponent<Canvas>();
                if (canvas != null)
                    MelonLogger.Msg($"{prefix}  CanvasEnabled: {canvas.enabled}");

                PrintChildrenDetailed(child, indent + 2);
            }
        }

        public static string GetPath(Transform t, Transform root)
        {
            string path = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        /// <summary>
        /// 转储所有载具的详细信息（DEBUG_VEHICLE块）
        /// </summary>
        public static void DumpAllVehiclesDetailedInfo(Vehicle[] allVehicles)
        {
            if (!DEBUG_VEHICLE) return;

            var _cr = typeof(ReticleMesh).GetField("cachedReticles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as System.Collections.IDictionary;
            if (_cr != null)
            {
                var keys = new List<string>();
                foreach (var k in _cr.Keys) keys.Add(k.ToString());
                keys.Sort();
                MelonLogger.Msg($"=== cachedReticles ({keys.Count}): [{string.Join(", ", keys)}] ===");
            }
            else
            {
                MelonLogger.Msg("=== cachedReticles: null ===");
            }
            MelonLogger.Msg($"=== 找到 {allVehicles.Length} 个载具 ===");
            foreach (Vehicle v in allVehicles)
            {
                MelonLogger.Msg($"[{v.FriendlyName}] tag={v.gameObject.tag} obj={v.gameObject.name}");
                AimablePlatform[] aps = v.AimablePlatforms;
                var f_stabMode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
                var f_stabActive = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int i = 0; i < aps.Length; i++)
                    MelonLogger.Msg($"  [{i}] {aps[i].name} | Stabilized={aps[i].Stabilized} _stabActive={f_stabActive?.GetValue(aps[i])} _stabMode={f_stabMode?.GetValue(aps[i])}");
                foreach (var cs in v.gameObject.GetComponentsInChildren<CameraSlot>())
                    MelonLogger.Msg($"  CameraSlot: {GetPath(cs.transform, v.transform)} | DefaultFov={cs.DefaultFov} OtherFovs=[{string.Join(", ", cs.OtherFovs ?? new float[0])}]");
                foreach (var uo in v.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true))
                {
                    var f_hasGuidance = typeof(GHPC.Equipment.Optics.UsableOptic).GetField("_hasGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
                    bool hasGuidance = f_hasGuidance != null && (bool)f_hasGuidance.GetValue(uo);
                    MelonLogger.Msg($"  UsableOptic: {GetPath(uo.transform, v.transform)} | GuidanceLight={uo.GuidanceLight} _hasGuidance={hasGuidance} FCS={uo.FCS?.name ?? "null"}");
                    if (uo.reticleMesh != null)
                    {
                        string soName = uo.reticleMesh.reticleSO?.name ?? "null";
                        bool isCached = _cr != null && _cr.Contains(soName);
                        MelonLogger.Msg($"    reticleSO: {soName} | cached={isCached}");
                        var tree = uo.reticleMesh.reticleSO;
                        if (tree != null)
                            foreach (var plane in tree.planes)
                                for (int ei = 0; ei < plane.elements.Count; ei++)
                                    MelonLogger.Msg($"    plane element[{ei}]: {plane.elements[ei].GetType().Name}");
                    }
                }
                var wm = v.GetComponent<WeaponsManager>();
                if (wm != null)
                {
                    for (int wi = 0; wi < wm.Weapons.Length; wi++)
                    {
                        var wsi = wm.Weapons[wi];
                        var fcs = wsi.FCS;
                        if (fcs == null) continue;
                        MelonLogger.Msg($"  [武器{wi}] {wsi.Name} | FCS: {GetPath(fcs.transform, v.transform)}");
                        MelonLogger.Msg($"    LaserOrigin: {(fcs.LaserOrigin != null ? GetPath(fcs.LaserOrigin, v.transform) : "null")}");
                        MelonLogger.Msg($"    LaserAim: {fcs.LaserAim} MaxLaserRange: {fcs.MaxLaserRange} DefaultRange: {fcs.DefaultRange}");
                        MelonLogger.Msg($"    StabsActive={fcs.StabsActive} CurrentStabMode={fcs.CurrentStabMode} SuperelevateWeapon: {fcs.SuperelevateWeapon} SuperleadWeapon: {fcs.SuperleadWeapon}");

                        var ws = wsi.Weapon;
                        if (ws != null)
                        {
                            MelonLogger.Msg($"    WeaponSystem: TriggerHoldTime={ws.TriggerHoldTime} MaxSpeedToFire={ws.MaxSpeedToFire} MaxSpeedToDeploy={ws.MaxSpeedToDeploy}");
                            MelonLogger.Msg($"    WeaponSystem: Impulse={ws.Impulse} BaseDeviationAngle={ws.BaseDeviationAngle}");

                            var gu = ws.GuidanceUnit;
                            if (gu != null)
                            {
                                MelonLogger.Msg($"    GuidanceUnit: path={GetPath(gu.transform, v.transform)}");
                                MelonLogger.Msg($"    GuidanceUnit: IsGuidingMissile={gu.IsGuidingMissile} Damaged={gu.Damaged} RangeSetting={gu.RangeSetting}");
                                MelonLogger.Msg($"    GuidanceUnit: AimElement={gu.AimElement?.name ?? "null"} ResetAimOnLaunch={gu.ResetAimOnLaunch}");
                                MelonLogger.Msg($"    GuidanceUnit: ManualAimAngularVelocity={gu.ManualAimAngularVelocity}");
                                MelonLogger.Msg($"    GuidanceUnit: CurrentMissiles.Count={gu.CurrentMissiles?.Count ?? -1}");
                            }
                            else
                            {
                                MelonLogger.Msg($"    GuidanceUnit: null");
                            }

                            var feed = ws.Feed;
                            if (feed != null)
                            {
                                var breechAmmo = feed.AmmoTypeInBreech;
                                MelonLogger.Msg($"    Feed.AmmoTypeInBreech: {breechAmmo?.Name ?? "null"}");

                                var rack = feed.ReadyRack;
                                if (rack != null)
                                {
                                    MelonLogger.Msg($"    ReadyRack: ClipTypes.Length={rack.ClipTypes?.Length ?? -1}");
                                    if (rack.ClipTypes != null)
                                    {
                                        for (int ci = 0; ci < rack.ClipTypes.Length; ci++)
                                        {
                                            var clip = rack.ClipTypes[ci];
                                            MelonLogger.Msg($"      ClipTypes[{ci}]: {clip?.Name ?? "null"}");
                                            var ammoFromClip = (breechAmmo == null && clip?.MinimalPattern?.Length > 0)
                                                ? clip.MinimalPattern[0]?.AmmoType : null;
                                            var ammo = ci == 0 ? (breechAmmo ?? ammoFromClip) : ammoFromClip;
                                            if (ammo != null)
                                            {
                                                MelonLogger.Msg($"        AmmoCodex={clip?.MinimalPattern?[0]?.name ?? "null"}");
                                                MelonLogger.Msg($"        Caliber={ammo.Caliber} Category={ammo.Category}");
                                                MelonLogger.Msg($"        RhaPen={ammo.RhaPenetration} MuzzleVel={ammo.MuzzleVelocity} Mass={ammo.Mass}");
                                                MelonLogger.Msg($"        TntKg={ammo.TntEquivalentKg} Spall={ammo.SpallMultiplier}");
                                                MelonLogger.Msg($"        Guidance={ammo.Guidance} Flight={ammo.Flight}");
                                                MelonLogger.Msg($"        TurnSpeed={ammo.TurnSpeed}");
                                                MelonLogger.Msg($"        GuidanceLockoutTime={ammo.GuidanceLockoutTime} GuidanceNoLockoutRange={ammo.GuidanceNoLockoutRange}");
                                                MelonLogger.Msg($"        GuidanceLeadDistance={ammo.GuidanceLeadDistance} GuidanceNoLoiterRange={ammo.GuidanceNoLoiterRange}");
                                                MelonLogger.Msg($"        ClimbAngle={ammo.ClimbAngle} DiveAngle={ammo.DiveAngle} LoiterAltitude={ammo.LoiterAltitude}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    var lm = v.GetComponent<LoadoutManager>();
                    if (lm != null)
                    {
                        var f_totalAmmoCount = typeof(LoadoutManager).GetField("_totalAmmoCount", BindingFlags.Instance | BindingFlags.NonPublic);
                        int totalCount = f_totalAmmoCount != null ? (int)f_totalAmmoCount.GetValue(lm) : -1;
                        MelonLogger.Msg($"  LoadoutManager: _totalAmmoCount={totalCount}");
                        if (lm.TotalAmmoCounts != null)
                            MelonLogger.Msg($"    TotalAmmoCounts=[{string.Join(", ", lm.TotalAmmoCounts)}]");
                        if (lm.LoadedAmmoList?.AmmoClips != null)
                        {
                            MelonLogger.Msg($"    LoadedAmmoList.AmmoClips.Length={lm.LoadedAmmoList.AmmoClips.Length}");
                            for (int i = 0; i < lm.LoadedAmmoList.AmmoClips.Length; i++)
                            {
                                var clipCodex = lm.LoadedAmmoList.AmmoClips[i];
                                MelonLogger.Msg($"      [{i}] {clipCodex?.name ?? "null"} -> {clipCodex?.ClipType?.Name ?? "null"}");
                            }
                        }
                        if (lm.RackLoadouts != null)
                        {
                            MelonLogger.Msg($"    RackLoadouts.Length={lm.RackLoadouts.Length}");
                            for (int i = 0; i < lm.RackLoadouts.Length; i++)
                            {
                                var rl = lm.RackLoadouts[i];
                                MelonLogger.Msg($"      Rack[{i}]: Capacity={rl.Rack?.ClipCapacity ?? -1}");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 转储所有载具的子节点结构（DEBUG_CHILDREN块）
        /// </summary>
        public static void DumpAllVehiclesChildren(Vehicle[] allVehicles)
        {
            if (!DEBUG_CHILDREN) return;

            MelonLogger.Msg($"=== 子节点结构 ===");
            foreach (Vehicle v in allVehicles)
            {
                MelonLogger.Msg($"[{v.FriendlyName}] 子节点结构:");
                for (int ci = 0; ci < v.transform.childCount; ci++)
                    PrintChildrenDetailed(v.transform.GetChild(ci), 1);
            }
        }

        /// <summary>
        /// 转储所有装甲数据（DEBUG_ARMOR块）
        /// </summary>
        public static void DumpAllArmorData()
        {
            if (!DEBUG_ARMOR) return;

            MelonLogger.Msg("=== 装甲数据 ===");
            var f_avgRha = typeof(VariableArmor).GetField("AverageRha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var f_armorType = typeof(VariableArmor).GetField("_armorType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (GameObject armour in GameObject.FindGameObjectsWithTag("Penetrable"))
            {
                if (armour == null) continue;
                GHPC.IArmor ia = (GHPC.IArmor)armour.GetComponent<UniformArmor>() ?? armour.GetComponent<VariableArmor>();
                if (ia == null) continue;
                string vicName = armour.GetComponentInParent<Vehicle>()?.FriendlyName ?? "unknown";
                if (ia is VariableArmor va2)
                {
                    float avg = f_avgRha != null ? (float)f_avgRha.GetValue(va2) : 0f;
                    var codex = f_armorType?.GetValue(va2) as ArmorCodexScriptable;
                    var at = codex?.ArmorType;
                    MelonLogger.Msg($"  [{vicName}] (Variable) {ia.Name} | AverageRha={avg} BHN={at?.BHN} KeMult={at?.RhaeMultiplierKe} CeMult={at?.RhaeMultiplierCe} | HEAT={ia.HeatRha} KE={ia.SabotRha}");
                }
                else
                    MelonLogger.Msg($"  [{vicName}] (Uniform) {ia.Name} | HEAT={ia.HeatRha} KE={ia.SabotRha}");
            }
        }
    }
}
#endif

// Release模式下的空实现桩
#if !DEBUG
using GHPC.Vehicle;

namespace UnderdogsEnhanced
{
    public static class UnderdogsDebug
    {
        public const bool DEBUG_TIMING = false;
        public const bool DEBUG_MCLOS = false;
        public const bool DEBUG_EMES = false;
        public const bool DEBUG_LRF = false;
        public const bool DEBUG_LEO = false;
        public const bool DEBUG_VEHICLE = false;
        public const bool DEBUG_ARMOR = false;
        public const bool DEBUG_CHILDREN = false;
        public const bool DEBUG_SPIKE = false;
        public const bool DEBUG_REPAIR = false;

        public static void Log(string msg) { }
        public static void LogTiming(string msg) { }
        public static void LogMCLOS(string msg) { }
        public static void LogMCLOSWarning(string msg) { }
        public static void LogEMES(string msg) { }
        public static void LogEMESWarning(string msg) { }
        public static void LogSpike(string msg) { }
        public static void LogSpikeWarning(string msg) { }
        public static void LogRepair(string msg) { }
        public static void LogVehicle(string msg) { }
        public static void LogArmor(string msg) { }
        public static void LogLRF(string msg) { }
        public static void LogLeo(string msg) { }
        public static void LogLeoWarning(string msg) { }
        public static void HandleDebugKeys() { }
        public static void DumpVehicleOpticsSnapshot(Vehicle vic) { }
        public static void DumpAllVehiclesDetailedInfo(Vehicle[] allVehicles) { }
        public static void DumpAllVehiclesChildren(Vehicle[] allVehicles) { }
        public static void DumpAllArmorData() { }

        public static float SpikeLockRadius => 2.0f;
    }
}
#endif
/*
 M60A3TTS
 T55A
 T72M1
 M2BRADLEY
 M2BRADLEY(ALT)
 M901
 M113
 M113G
 M151M232
 M151
 BRDM2
 UAZ469
 STATIC_TOW
 STATIC_9K111
 STATIC_SPG9
 STATIC_SPG9_SA
 T3485
 TC
 BMP1
 URAL375D
 M923
 BMP2
 LEO1A1
 LEO1A1A2
 LEO1A1A3
 LEO1A1A4
 LEO1A3
 LEO1A3A1
 LEO1A3A2
 LEO1A3A3
 T62
 T64R
 T64A74
 T64A79
 T64A81
 T64A
 T64A84
 T64B
 T64B81
 T64B1
 T64B181
 T72M
 M60A1RISEP
 M60A1RISEP77
 M60A1
 M60A1AOS
 M60A3
 M1
 M1IP
 T72GILLS
 T72UV1
 T72UV2
 T72ULEM
 T72
 SquadUnit_US_PASGT
 SquadUnit_GDR
 BMP1P
 BMP1P_SA
 STATIC_9K111_SA
 BMP1_SA
 BMP2_SA
 BRDM2_SA
 BTR60PB
 BTR60PB_SA
 URAL375D_SA
 PT76B
 T80B
 T54A
 BTR70
 AH1
 MI2
 Mi8T
 Mi24
 Mi24V_SA
 OH58A
 Mi24V_NVA
 STATIC_SPG9_TRENCH
 STATIC_SPG9_SA_TRENCH
 STATIC_9K111_TRENCH
 STATIC_9K111_SA_TRENCH
 STATIC_TOW_TRENCH
 AH1_rockets
 Mi24_rockets
 Mi24V_SA_rockets
 Mi24V_NVA_rockets
 RTS_CAMERA
 MARDERA1
 MARDERA1_NO_ATGM
 MARDERA1PLUS
 MARDER1A2
 */