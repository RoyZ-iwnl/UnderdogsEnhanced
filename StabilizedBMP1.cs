using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using MelonLoader;
using UnderdogsEnhanced;
using GHPC.Vehicle;
using GHPC;
using System.Reflection;
using GHPC.Weapons;

[assembly: MelonInfo(typeof(UnderdogsEnhancedMod), "Underdogs Enhanced", "1.0.0", "RoyZ;Based on ATLAS work")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace UnderdogsEnhanced
{
    public class UnderdogsEnhancedMod : MelonMod
    {
        private static readonly bool DEBUG_MODE = false;

        public static MelonPreferences_Category cfg;
        public static MelonPreferences_Entry<bool> stab_konkurs;
        public static MelonPreferences_Entry<bool> stab_marder;
        public static MelonPreferences_Entry<bool> stab_brdm;

        private GameObject[] vic_gos;
        private string[] invalid_scenes = new string[] { "MainMenu2_Scene", "LOADER_MENU", "LOADER_INITIAL", "t64_menu" };

        public override void OnInitializeMelon() {
            cfg = MelonPreferences.CreateCategory("Underdogs-Enhanced");
            stab_konkurs = cfg.CreateEntry("BMP-1P Konkurs Stab", false);
            stab_konkurs.Description = "Gives the Konkurs on the BMP-1P a stabilizer";
            stab_marder = cfg.CreateEntry("Marder Stabilizer", true);
            stab_marder.Description = "Gives Marder series a stabilizer (default: enabled)";
            stab_brdm = cfg.CreateEntry("BRDM-2 Stabilizer", true);
            stab_brdm.Description = "Gives BRDM-2 a stabilizer (default: enabled)";
         }

        public override async void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (invalid_scenes.Contains(sceneName)) return;
            vic_gos = GameObject.FindGameObjectsWithTag("Vehicle");

            while (vic_gos.Length == 0)
            {
                vic_gos = GameObject.FindGameObjectsWithTag("Vehicle");
                await Task.Delay(1);
            }

            await Task.Delay(3000);

            Vehicle[] all_vehicles = Object.FindObjectsOfType<Vehicle>();

            if (DEBUG_MODE)
            {
                MelonLogger.Msg($"=== 搜索所有Vehicle组件 ===");
                MelonLogger.Msg($"找到 {all_vehicles.Length} 个Vehicle组件");
                foreach (Vehicle v in all_vehicles)
                {
                    MelonLogger.Msg($"载具: {v.FriendlyName} | 标签: {v.gameObject.tag} | 对象名: {v.gameObject.name}");
                }
            }

            foreach (Vehicle vic in all_vehicles)
            {
                if (vic == null) continue;

                string name = vic.FriendlyName;

                if (name == "BMP-1" || name == "BMP-1P")
                {
                    AimablePlatform[] aimables = vic.AimablePlatforms;

                    if (DEBUG_MODE)
                    {
                        MelonLogger.Msg($"=== {name} 平台信息 ===");
                        MelonLogger.Msg($"平台总数: {aimables.Length}");
                        for (int i = 0; i < aimables.Length; i++)
                        {
                            MelonLogger.Msg($"索引 {i}: {aimables[i].name} | 已稳定: {aimables[i].Stabilized}");
                        }
                    }

                    FieldInfo stab_mode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo stab_active = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
                    PropertyInfo stab_FCS_active = typeof(FireControlSystem).GetProperty("StabsActive", BindingFlags.Instance | BindingFlags.Public);

                    WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
                    WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];
                    stab_FCS_active.SetValue(main_gun_info.FCS, true);
                    main_gun_info.FCS.CurrentStabMode = StabilizationMode.Vector;

                    aimables[0].Stabilized = true;
                    stab_active.SetValue(aimables[0], true);
                    stab_mode.SetValue(aimables[0], StabilizationMode.Vector);

                    int turret_platform_idx = name == "BMP-1" ? 3 : 1;

                    aimables[turret_platform_idx].Stabilized = true;
                    stab_active.SetValue(aimables[turret_platform_idx], true);
                    stab_mode.SetValue(aimables[turret_platform_idx], StabilizationMode.Vector);

                    if (stab_konkurs.Value && name == "BMP-1P") {
                        WeaponSystemInfo atgm = weapons_manager.Weapons[1];
                        stab_FCS_active.SetValue(atgm.FCS, true);
                        atgm.FCS.CurrentStabMode = StabilizationMode.Vector;

                        aimables[2].Stabilized = true;
                        stab_active.SetValue(aimables[2], true);
                        stab_mode.SetValue(aimables[2], StabilizationMode.Vector);

                        aimables[3].Stabilized = true;
                        stab_active.SetValue(aimables[3], true);
                        stab_mode.SetValue(aimables[3], StabilizationMode.Vector);
                    }
                }

                if (stab_marder.Value && (name == "Marder 1A2" || name == "Marder A1-" || name == "Marder A1+"))
                {
                    AimablePlatform[] aimables = vic.AimablePlatforms;

                    if (DEBUG_MODE)
                    {
                        MelonLogger.Msg($"=== {name} 平台信息 ===");
                        MelonLogger.Msg($"平台总数: {aimables.Length}");
                        for (int i = 0; i < aimables.Length; i++)
                        {
                            MelonLogger.Msg($"索引 {i}: {aimables[i].name} | 已稳定: {aimables[i].Stabilized}");
                        }
                    }

                    FieldInfo stab_mode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo stab_active = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
                    PropertyInfo stab_FCS_active = typeof(FireControlSystem).GetProperty("StabsActive", BindingFlags.Instance | BindingFlags.Public);

                    WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
                    WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];
                    stab_FCS_active.SetValue(main_gun_info.FCS, true);
                    main_gun_info.FCS.CurrentStabMode = StabilizationMode.Vector;

                    aimables[0].Stabilized = true;
                    stab_active.SetValue(aimables[0], true);
                    stab_mode.SetValue(aimables[0], StabilizationMode.Vector);

                    aimables[1].Stabilized = true;
                    stab_active.SetValue(aimables[1], true);
                    stab_mode.SetValue(aimables[1], StabilizationMode.Vector);
                }

                if (stab_brdm.Value && name == "BRDM-2")
                {
                    AimablePlatform[] aimables = vic.AimablePlatforms;

                    if (DEBUG_MODE)
                    {
                        MelonLogger.Msg($"=== BRDM-2 平台信息 ===");
                        MelonLogger.Msg($"平台总数: {aimables.Length}");
                        for (int i = 0; i < aimables.Length; i++)
                        {
                            MelonLogger.Msg($"索引 {i}: {aimables[i].name} | 已稳定: {aimables[i].Stabilized}");
                        }
                    }

                    FieldInfo stab_mode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo stab_active = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
                    PropertyInfo stab_FCS_active = typeof(FireControlSystem).GetProperty("StabsActive", BindingFlags.Instance | BindingFlags.Public);

                    WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
                    WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];
                    stab_FCS_active.SetValue(main_gun_info.FCS, true);
                    main_gun_info.FCS.CurrentStabMode = StabilizationMode.Vector;

                    aimables[0].Stabilized = true;
                    stab_active.SetValue(aimables[0], true);
                    stab_mode.SetValue(aimables[0], StabilizationMode.Vector);

                    aimables[1].Stabilized = true;
                    stab_active.SetValue(aimables[1], true);
                    stab_mode.SetValue(aimables[1], StabilizationMode.Vector);
                }
            }
        }
    }
}
