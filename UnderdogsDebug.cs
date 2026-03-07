using GHPC.Camera;
using GHPC.Vehicle;
using MelonLoader;
using Reticle;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public static class UnderdogsDebug
    {
        // Debug标志
        public static readonly bool DEBUG_MODE = true;
        public static readonly bool DEBUG_TIMING = false;   // [UE] 时机日志，受 DEBUG_MODE 控制
        public static readonly bool DEBUG_MCLOS = false;    // [BMP-1 MCLOS] 日志，受 DEBUG_MODE 控制
        public static readonly bool DEBUG_LRF = false;      // LRF 改装日志，受 DEBUG_MODE 控制
        public static readonly bool DEBUG_VEHICLE = false; // 车辆调试子开关，受 DEBUG_MODE 控制
        public static readonly bool DEBUG_ARMOR = false;   // 装甲数据调试子开关，受 DEBUG_MODE 控制
        public static readonly bool DEBUG_CHILDREN = false; // 子节点结构调试子开关，受 DEBUG_MODE 控制

        private static GameObject _displayDebugUiHost;

        // 条件日志方法
        public static void Log(string msg)
        {
            if (DEBUG_MODE) MelonLogger.Msg(msg);
        }

        public static void LogTiming(string msg)
        {
            if (DEBUG_MODE && DEBUG_TIMING) MelonLogger.Msg(msg);
        }

        public static void LogMCLOS(string msg)
        {
            if (DEBUG_MODE && DEBUG_MCLOS) MelonLogger.Msg(msg);
        }

        public static void LogLRF(string msg)
        {
            if (DEBUG_MODE && DEBUG_LRF) MelonLogger.Msg(msg);
        }

        public static void LogVehicle(string msg)
        {
            if (DEBUG_MODE && DEBUG_VEHICLE) MelonLogger.Msg(msg);
        }

        public static void LogArmor(string msg)
        {
            if (DEBUG_MODE && DEBUG_ARMOR) MelonLogger.Msg(msg);
        }

        public static void EnsureDisplayDebugUiHost()
        {
            if (_displayDebugUiHost != null) return;

            _displayDebugUiHost = new GameObject("__UE_DISPLAY_DEBUG_UI__");
            _displayDebugUiHost.hideFlags = HideFlags.HideAndDontSave;
            GameObject.DontDestroyOnLoad(_displayDebugUiHost);
            _displayDebugUiHost.AddComponent<EMES18DebugUI>();
        }

        public static void HandleDebugKeys()
        {
            EMES18Optic.TickGlobalDefaultScopeState();

            if (DEBUG_MODE) EnsureDisplayDebugUiHost();
            if (!DEBUG_MODE) return;

            if (Input.GetKeyDown(KeyCode.F8))
            {
                EnsureDisplayDebugUiHost();
                EMES18DebugUI.Instance?.ToggleWindow();
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                EnsureDisplayDebugUiHost();
                EMES18DebugUI.Instance?.RefreshWindowTargets();
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
    }
}
