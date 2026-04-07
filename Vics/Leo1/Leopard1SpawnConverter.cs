using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using HarmonyLib;
using GHPC.Mission;
using GHPC.Mission.Data;
using GHPC.AI;
using UnityEngine;

namespace UnderdogsEnhanced
{
    /// <summary>
    /// 车辆生成拦截器 - 将 Leopard 1 变种转换为 A1A4（用于后续 1A5 改装）
    /// </summary>
    public static class Leopard1SpawnConverter
    {
        // 内部 Key → FriendlyName 映射
        private static readonly Dictionary<string, string> key_to_name = new Dictionary<string, string>
        {
            { "LEO1A3", "Leopard 1A3" },
            { "LEO1A3A1", "Leopard 1A3A1" },
            { "LEO1A3A2", "Leopard 1A3A2" },
            { "LEO1A3A3", "Leopard 1A3A3" },
            { "LEO1A1", "Leopard A1A1" },
            { "LEO1A1A2", "Leopard A1A2" },
            { "LEO1A1A3", "Leopard A1A3" },
            { "LEO1A1A4", "Leopard A1A4" }
        };

        // 目标 Key - A1A4
        private const string TARGET_KEY = "LEO1A1A4";

        /// <summary>
        /// 检查指定 Key 是否应该转换为 A1A4
        /// </summary>
        public static bool ShouldConvertToA1A4(string key)
        {
            if (key == TARGET_KEY) return false; // 已经是 A1A4

            if (!key_to_name.TryGetValue(key, out string friendlyName))
            {
                return false;
            }

            // 查询该变种的转换配置
            return LeopardMain.IsConvertEnabled(friendlyName);
        }
    }

    // Harmony Patch - 拦截 UnitSpawner.SpawnUnit(string, UnitMetaData, WaypointHolder, Transform)
    // 直接拦截 uniqueName 参数
    [HarmonyPatch(typeof(UnitSpawner), "SpawnUnit", new System.Type[] { typeof(string), typeof(UnitMetaData), typeof(WaypointHolder), typeof(Transform) })]
    public static class Leopard1SpawnPatch_UnitSpawner
    {
        private static void Prefix(ref string uniqueName)
        {
            if (uniqueName == null) return;

            // 检查是否为 Leopard 1 变种且需要转换
            if (Leopard1SpawnConverter.ShouldConvertToA1A4(uniqueName))
            {
                UnderdogsDebug.LogLeo($"[Leopard1Spawn] UnitSpawner.SpawnUnit: {uniqueName} → LEO1A1A4");
                uniqueName = "LEO1A1A4";
            }
        }
    }
}
