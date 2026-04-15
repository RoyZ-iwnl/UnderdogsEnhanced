using System;
using System.Collections;
using System.Collections.Generic;
using GHPC;
using GHPC.Vehicle;
using GHPC.Equipment;
using GHPC.Equipment.Optics;
using GHPC.Utility;
using GHPC.State;
using GHPC.Camera;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    /// <summary>
    /// Leopard 1A5 模型修改模块
    ///
    /// 只对 Leopard A1A4（Spawn 转换后）执行 1A5 改装
    /// 转换开关由 Leopard1SpawnConverter 的 "XXX Convert to 1A5" 配置控制
    /// </summary>
    public static class Leopard1Model
    {
        /// <summary>
        /// 已转换车辆ID记录（防止重复转换）
        /// </summary>
        private static readonly HashSet<int> _convertedVehicleIds = new HashSet<int>();

        /// <summary>
        /// 检查车辆是否已被转换为 1A5
        /// </summary>
        public static bool IsConverted(int vehicleInstanceId)
        {
            return _convertedVehicleIds.Contains(vehicleInstanceId);
        }

        // ============================================================
        // 模型修改主入口
        // ============================================================
        public static IEnumerator Convert(GameState _)
        {
            GameObject l1a5Prefab = UEResourceController.GetL1a5Prefab();

            if (l1a5Prefab == null)
            {
                UnderdogsDebug.LogLeoWarning("[Leopard1Model] L1A5预制件未加载");
                yield break;
            }

            // 从预制件中提取炮塔 Mesh
            // 打印预制件结构以便调试
            UnderdogsDebug.LogLeo($"[Leopard1Model] 预制件根节点: {l1a5Prefab.name}");
            for (int i = 0; i < l1a5Prefab.transform.childCount; i++)
            {
                UnderdogsDebug.LogLeo($"  子节点[{i}]: {l1a5Prefab.transform.GetChild(i).name}");
            }

            // 直接从预制件获取TURRET节点和炮塔mesh
            Transform turretAddonRoot = l1a5Prefab.transform.Find("TURRET");
            Mesh newTurretMesh = null;

            // 获取L1A5TURRET mesh
            Transform prefabTurretMesh = turretAddonRoot.Find("L1A5TURRET");
            if (prefabTurretMesh != null)
            {
                MeshFilter mf = prefabTurretMesh.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    newTurretMesh = mf.sharedMesh;
                    UnderdogsDebug.LogLeo($"[Leopard1Model] 获取炮塔mesh: {newTurretMesh.name}");
                }
            }

            if (newTurretMesh == null)
            {
                UnderdogsDebug.LogLeoWarning("[Leopard1Model] 未找到炮塔mesh");
            }

            Vehicle[] vehicles = UnityEngine.Object.FindObjectsOfType<Vehicle>();

            foreach (Vehicle vic in vehicles)
            {
                if (vic == null) continue;

                // 只处理 Leopard A1A4
                if (!IsTargetVehicle(vic)) continue;

                // 防止重复转换
                int vicId = vic.GetInstanceID();
                if (_convertedVehicleIds.Contains(vicId)) continue;
                _convertedVehicleIds.Add(vicId);

                GameObject vicGo = vic.gameObject;

                UnderdogsDebug.LogLeo($"[Leopard1Model] 开始转换: {vic.FriendlyName}");

                try
                {
                    // 1. 隐藏原探照灯部件
                    HideSearchlightComponents(vicGo);

                    // 2. 替换炮塔mesh + 添加EMES
                    ApplyTurretModifications(vicGo, turretAddonRoot, newTurretMesh);

                    // 3. 修改原装甲数值
                    //ModifyArmorValues(vicGo);

                    // 4. 更新车辆名称
                    UpdateVehicleName(vic);

                    UnderdogsDebug.LogLeo($"[Leopard1Model] 转换完成: {vic.FriendlyName}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Leopard1Model] 转换失败 {vic.FriendlyName}: {ex}");
                }

                yield return null;
            }
        }

        // ============================================================
        // 判断是否为目标车辆
        // 只有 Leopard A1A4（原始或 Spawn 转换后）才执行 1A5 改装
        // 其他变种如果 Spawn 转换关闭，保持原样不改装
        // ============================================================
        private static bool IsTargetVehicle(Vehicle vic)
        {
            if (string.IsNullOrEmpty(vic.FriendlyName)) return false;
            if (!LeopardMain.IsModEnabled()) return false;
            // 只匹配 A1A4 和 1A5（Spawn 转换后改名）
            return LeopardMain.IsA1A4(vic.FriendlyName);
        }

        // ============================================================
        // 1. 隐藏探照灯部件
        // ============================================================
        private static void HideSearchlightComponents(GameObject vic)
        {
            string[] pathsToHide = new[]
            {
                "LEO1A1A1_rig/HULL/TURRET/Mantlet/searchlight closed",
                "LEO1A1A1_rig/HULL/TURRET/Mantlet/searchlight open",
                "LEO1A1A1_rig/HULL/TURRET/Mantlet/spotlight visuals",
                "LEO1A1A1_rig/HULL/TURRET/searchlight cord"
            };

            foreach (string path in pathsToHide)
            {
                Transform target = vic.transform.Find(path);
                if (target != null)
                    target.gameObject.SetActive(false);
            }
        }

        // ============================================================
        // 替换炮塔模型 + 添加EMES
        // 只换mesh不换材质，EMES手动从预制件创建并修正scale
        // 预制件结构: EMES18(根) -> TURRET -> L1A5TURRET/EMES -> EMES -> VISUAL/HITBOX
        // ============================================================
        private static void ApplyTurretModifications(GameObject vicGo, Transform turretAddonRoot, Mesh newTurretMesh)
        {
            if (newTurretMesh == null)
            {
                UnderdogsDebug.LogLeoWarning("[Leopard1Model] 炮塔mesh为空，跳过");
                return;
            }

            Transform turretSkeleton = vicGo.transform.Find("LEO1A1A1_rig/HULL/TURRET");
            if (turretSkeleton == null)
            {
                UnderdogsDebug.LogLeoWarning("[Leopard1Model] 未找到炮塔骨骼 LEO1A1A1_rig/HULL/TURRET");
                return;
            }

            // --- 1. 替换炮塔mesh（保留游戏原材质）---
            string[] turretVisualPaths = {
                "LEO1A1A1_rig/HULL/TURRET/turret_early",
                "LEO1A1A1_rig/HULL/TURRET/turret_late"
            };

            foreach (string path in turretVisualPaths)
            {
                Transform tv = vicGo.transform.Find(path);
                if (tv == null) continue;

                MeshFilter oldMf = tv.GetComponent<MeshFilter>();
                if (oldMf != null)
                {
                    Mesh oldMesh = oldMf.sharedMesh;
                    oldMf.sharedMesh = newTurretMesh;
                    UnderdogsDebug.LogLeo($"[Leopard1Model] 炮塔mesh已替换: {path}, 旧={oldMesh?.name ?? "null"}  新={newTurretMesh.name}");
                }
            }

            // --- 2. 获取LateFollowTarget（装甲跟随层）---
            LateFollowTarget lft = turretSkeleton.GetComponent<LateFollowTarget>();
            if (lft == null || lft._lateFollowers == null || lft._lateFollowers.Count == 0)
            {
                UnderdogsDebug.LogLeoWarning("[Leopard1Model] 炮塔没有LateFollowTarget");
                return;
            }
            Transform lateFollower = lft._lateFollowers[0].transform;

            // --- 3. 定位预制件中的EMES节点 ---
            // 预制件结构: EMES18(根) -> TURRET(=turretAddonRoot) -> EMES -> VISUAL/HITBOX
            // 注意: turretAddonRoot 已经是 TURRET 节点（在 Convert 方法中已 Find）
            Transform prefabTurretRoot = turretAddonRoot;

            Transform emesParent = prefabTurretRoot.Find("EMES");
            if (emesParent == null)
            {
                UnderdogsDebug.LogLeoWarning("[Leopard1Model] 预制件中未找到EMES节点");
                return;
            }

            // --- 4. 实例化EMES ---
            // 实例语义在这里配置，静态资源阶段只保留模板prefab加载
            if (emesParent != null)
            {
                // 挂到TURRET骨骼下
                GameObject emesInstance = GameObject.Instantiate(emesParent.gameObject, turretSkeleton);

                // 硬编码位置修正
                emesInstance.transform.localPosition = new Vector3(0f, 0.42f, -0.35f);
                emesInstance.transform.localRotation = emesParent.localRotation;
                emesInstance.transform.localScale = emesParent.localScale;

                UnderdogsDebug.LogLeo($"[Leopard1Model] EMES已创建: pos={emesInstance.transform.localPosition}");

                // 检查VISUAL材质并设置FLIR
                Transform emesVisualInst = emesInstance.transform.Find("VISUAL");
                if (emesVisualInst != null)
                {
                    // Instantiate后重新设置FLIR shader和HeatSource
                    UECommonUtil.SetupFLIRShaders(emesVisualInst.gameObject, 0.55f);
                    UnderdogsDebug.LogLeo($"[Leopard1Model] VISUAL FLIR已设置");
                }

                // 配置可破坏组件
                Transform emesHitboxInst = emesInstance.transform.Find("HITBOX");
                if (emesHitboxInst != null)
                {
                    // Instantiate后重新设置tag/layer（可能丢失）
                    emesHitboxInst.tag = "Penetrable";
                    emesHitboxInst.gameObject.layer = 8;

                    // 确保MeshCollider有有效的mesh
                    MeshCollider mc = emesHitboxInst.GetComponent<MeshCollider>();
                    MeshFilter mf = emesHitboxInst.GetComponent<MeshFilter>();

                    // 如果HITBOX mesh没有triangles，使用VISUAL的mesh代替
                    Mesh mesh = mf?.sharedMesh;
                    bool hasTriangles = mesh != null && mesh.triangles != null && mesh.triangles.Length > 0;
                    if (!hasTriangles)
                    {
                        Transform visual = emesInstance.transform.Find("VISUAL");
                        UnderdogsDebug.LogLeo($"[Leopard1Model] 检查VISUAL: name={visual?.name}, childCount={visual?.childCount}");

                        // 检查VISUAL本身
                        MeshFilter visualMf = visual?.GetComponent<MeshFilter>();
                        Mesh visualMesh = visualMf?.sharedMesh;
                        UnderdogsDebug.LogLeo($"[Leopard1Model] VISUAL自身: MeshFilter={(visualMf != null)}, mesh={(visualMesh?.name ?? "null")}, triangles={(visualMesh?.triangles?.Length ?? -1)}");

                        // 检查VISUAL的子节点
                        bool foundMesh = false;
                        if (visual != null && visual.childCount > 0)
                        {
                            for (int i = 0; i < visual.childCount; i++)
                            {
                                Transform child = visual.GetChild(i);
                                MeshFilter childMf = child.GetComponent<MeshFilter>();
                                Mesh childMesh = childMf?.sharedMesh;
                                UnderdogsDebug.LogLeo($"[Leopard1Model] VISUAL子节点[{i}]: {child.name}, MeshFilter={(childMf != null)}, mesh={(childMesh?.name ?? "null")}");

                                if (childMesh != null && childMesh.triangles != null && childMesh.triangles.Length > 0)
                                {
                                    mf.sharedMesh = childMesh;
                                    UnderdogsDebug.LogLeo($"[Leopard1Model] HITBOX使用VISUAL子节点mesh: {childMesh.name}");
                                    foundMesh = true;
                                    break;
                                }
                            }
                        }

                        if (!foundMesh && visualMesh != null && visualMesh.triangles != null && visualMesh.triangles.Length > 0)
                        {
                            mf.sharedMesh = visualMesh;
                            UnderdogsDebug.LogLeo($"[Leopard1Model] HITBOX使用VISUAL的mesh: {visualMesh.name}");
                        }
                        else if (!foundMesh)
                        {
                            UnderdogsDebug.LogLeoWarning($"[Leopard1Model] VISUAL也没有有效mesh!");
                        }
                    }

                    if (mc != null && mf != null && mf.sharedMesh != null)
                    {
                        mc.sharedMesh = null;
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = true;

                        UnderdogsDebug.LogLeo($"[Leopard1Model] HITBOX mesh: name={mf.sharedMesh.name}, vertices={mf.sharedMesh.vertexCount}, triangles={mf.sharedMesh.triangles.Length / 3}, bounds={mc.bounds.size}");
                    }
                    else
                    {
                        UnderdogsDebug.LogLeoWarning($"[Leopard1Model] HITBOX缺少组件: MeshCollider={(mc != null)}, MeshFilter={(mf != null)}, mesh={(mf?.sharedMesh?.name ?? "null")}");
                    }

                    // 硬编码：EMES-18可破坏，生命值20
                    GHPC.Equipment.DestructibleComponent destructible = emesHitboxInst.GetComponent<GHPC.Equipment.DestructibleComponent>();
                    if (destructible == null)
                        destructible = emesHitboxInst.gameObject.AddComponent<GHPC.Equipment.DestructibleComponent>();
                    destructible._health = 20f;
                    destructible._fullHealth = 20f;
                    destructible._pressureTolerance = 1f;
                    destructible._shockResistance = 0.50f;
                    destructible._name = "EMES-18";

                    UnderdogsDebug.LogLeo($"[Leopard1Model] HITBOX可破坏组件已配置: health={destructible._health}");

                    // === 建立损毁联动 - 注册到FCS ===
                    GHPC.Weapons.FireControlSystem fcs = vicGo.GetComponentInChildren<GHPC.Weapons.FireControlSystem>();
                    if (fcs != null)
                    {
                        // 注册到FCS，使用游戏内置的激光损毁机制
                        fcs.LaserComponent = destructible;
                        destructible.Destroyed += fcs.LaserDestroyed;
                        UnderdogsDebug.LogLeo($"[Leopard1Model] EMES18已注册为FCS激光组件");
                    }
                    else
                    {
                        UnderdogsDebug.LogLeoWarning($"[Leopard1Model] 未找到FCS，无法建立损毁联动");
                    }
                }
            }
        }
               

        // ============================================================
        // 3. 修改装甲数值
        // ============================================================
        private static void ModifyArmorValues(GameObject vic)
        {
            Transform turret = vic.transform.Find("LEO1A1A1_rig/HULL/TURRET");
            if (turret == null) return;

            LateFollowTarget lateFollow = turret.GetComponent<LateFollowTarget>();
            if (lateFollow == null || lateFollow._lateFollowers == null || lateFollow._lateFollowers.Count == 0)
                return;

            Transform armorParent = lateFollow._lateFollowers[0].transform;

            GHPC.VariableArmor[] armors = armorParent.GetComponentsInChildren<GHPC.VariableArmor>(true);
            GHPC.UniformArmor[] uniformArmors = armorParent.GetComponentsInChildren<GHPC.UniformArmor>(true);

            foreach (var armor in armors)
            {
                armor.AverageRha = (480f + 420f) / 2f;
            }

            foreach (var armor in uniformArmors)
            {
                armor.PrimaryHeatRha = 480f;
                armor.PrimarySabotRha = 420f;
            }
        }

        // ============================================================
        // 4. 更新车辆名称
        // ============================================================
        private static void UpdateVehicleName(Vehicle vic)
        {
            if (vic._friendlyName != null)
            {
                vic._friendlyName = vic._friendlyName.Replace("A1A4", "1A5");
            }
        }

        // ============================================================
        // 辅助方法
        // ============================================================
        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                if (child != null)
                    SetLayerRecursive(child.gameObject, layer);
            }
        }

        private static void SetTagRecursive(GameObject obj, string tag)
        {
            if (obj == null) return;
            obj.tag = tag;
            foreach (Transform child in obj.transform)
            {
                if (child != null)
                    SetTagRecursive(child.gameObject, tag);
            }
        }

        // ============================================================
        // 初始化入口
        // ============================================================
        public static void Init()
        {
            StateController.RunOrDefer(
                GameState.GameReady,
                new GameStateEventHandler(Convert),
                GameStatePriority.Medium);
        }
    }
}
