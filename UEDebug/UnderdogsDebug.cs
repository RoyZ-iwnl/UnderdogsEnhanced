#if DEBUG
using GHPC;
using GHPC.Camera;
using MelonLoader;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public static class UnderdogsDebug
    {
        // Debug标志 - 改为const编译时常量
        public const bool DEBUG_TIMING = false;   // [TIMING] 时机日志
        public const bool DEBUG_MCLOS = false;    // [BMP-1 MCLOS] 日志
        public const bool DEBUG_EMES = false;     // [EMES18] 热像初始化日志
        public const bool DEBUG_LRF = true;      // LRF 改装日志
        public const bool DEBUG_LEO = false;      // [Leopard1] Spawn转换与模型改装日志
        public const bool DEBUG_SPIKE = false;   // [Marder Spike] 导弹系统日志
        public const bool DEBUG_REPAIR = false;  // [Repair] 修复功能日志

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

        // SPIKE 锁定半径配置
        private static float _spikeLockRadius = 2f;
        public static float SpikeLockRadius
        {
            get => _spikeLockRadius;
            set => _spikeLockRadius = Mathf.Clamp(value, 0.1f, 10f);
        }

        public static void HandleDebugKeys()
        {
            EMES18Optic.TickGlobalDefaultScopeState();

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

        private static void PrintChildrenDetailed(Transform t, int indent)
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
#endif

// Release模式下的空实现桩
#if !DEBUG
namespace UnderdogsEnhanced
{
    public static class UnderdogsDebug
    {
        public const bool DEBUG_TIMING = false;
        public const bool DEBUG_MCLOS = false;
        public const bool DEBUG_EMES = false;
        public const bool DEBUG_LRF = false;
        public const bool DEBUG_LEO = false;
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
        public static void LogLRF(string msg) { }
        public static void LogLeo(string msg) { }
        public static void LogLeoWarning(string msg) { }
        public static void HandleDebugKeys() { }

        public static float SpikeLockRadius => 2.0f;
    }
}
#endif