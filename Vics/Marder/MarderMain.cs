using System.Reflection;
using GHPC;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;

namespace UnderdogsEnhanced
{
    public static class MarderMain
    {
        public static MelonPreferences_Entry<bool> marder_enabled;
        public static MelonPreferences_Entry<bool> stab_marder;
        public static MelonPreferences_Entry<bool> stab_marder_milan;
        public static MelonPreferences_Entry<bool> marder_rangefinder;
        public static MelonPreferences_Entry<bool> marder_spike;
        public static MelonPreferences_Entry<int> marder_spike_ready_count;
        public static void Config(MelonPreferences_Category cfg)
        {
            marder_enabled = cfg.CreateEntry("Marder Mod Master Switch", true);
            marder_enabled.Description = "////////////////////////////////////Marder Mod////////////////////////////////////";

            stab_marder = cfg.CreateEntry("Marder Stabilizer", true);
            stab_marder.Description = "Gives Marder series a stabilizer (default: enabled)";
            stab_marder_milan = cfg.CreateEntry("Marder MILAN Stabilizer", true);
            stab_marder_milan.Description = "Stabilizes MILAN launcher on Marder A1+ and Marder 1A2 (default: enabled)";
            marder_rangefinder = cfg.CreateEntry("Marder Rangefinder", true);
            marder_rangefinder.Description = "Gives Marder series laser rangefinder and parallax fix (default: enabled)";
            marder_spike = cfg.CreateEntry("Marder Spike WIP", false);
            marder_spike.Description = "WIP WARNING:Converts Marder 1A2 and Marder A1+ MILAN launcher to a Spike-style hybrid TV/FnF suite (default: disabled)";
            marder_spike_ready_count = cfg.CreateEntry("Marder Spike Ready Missiles", -1);
            marder_spike_ready_count.Description = "WIP WARNING:Reserve missile count for Marder Spike (does not include the initial round loaded into breech). -1 uses game's original reserve logic; >0 overrides. (max: 64)";
        }

        public static void Apply(Vehicle vic)
        {
            string name = vic.FriendlyName;
            if (!marder_enabled.Value) return;
            if (!stab_marder.Value) return;
            if (name != "Marder 1A2" && name != "Marder A1-" && name != "Marder A1+") return;

#if DEBUG
            UnderdogsDebug.LogTiming($"[UE]   > 匹配 Marder 改装");
#endif

            AimablePlatform[] aimables = vic.AimablePlatforms;
            FieldInfo stab_mode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo stab_active = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo stab_FCS_active = typeof(FireControlSystem).GetProperty("StabsActive", BindingFlags.Instance | BindingFlags.Public);

            WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
            WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];
            WeaponSystem main_gun = main_gun_info.Weapon;

            stab_FCS_active.SetValue(main_gun_info.FCS, true);
            main_gun_info.FCS.CurrentStabMode = StabilizationMode.Vector;

            if (marder_rangefinder.Value)
            {
                // 激光测距仪配置
                main_gun.FCS.MaxLaserRange = 4000;

                // 视差修正
                FieldInfo fixParallaxField = typeof(FireControlSystem).GetField("_fixParallaxForVectorMode", BindingFlags.Instance | BindingFlags.NonPublic);
                fixParallaxField.SetValue(main_gun.FCS, true);
            }

            aimables[0].Stabilized = true;
            stab_active.SetValue(aimables[0], true);
            stab_mode.SetValue(aimables[0], StabilizationMode.Vector);

            aimables[1].Stabilized = true;
            stab_active.SetValue(aimables[1], true);
            stab_mode.SetValue(aimables[1], StabilizationMode.Vector);

            if (stab_marder_milan.Value && (name == "Marder A1+" || name == "Marder 1A2"))
            {
                WeaponSystemInfo milan_info = weapons_manager.Weapons[1];
                stab_FCS_active.SetValue(milan_info.FCS, true);
                milan_info.FCS.CurrentStabMode = StabilizationMode.Vector;

                aimables[2].Stabilized = true;
                stab_active.SetValue(aimables[2], true);
                stab_mode.SetValue(aimables[2], StabilizationMode.Vector);

                aimables[3].Stabilized = true;
                stab_active.SetValue(aimables[3], true);
                stab_mode.SetValue(aimables[3], StabilizationMode.Vector);
            }
        }
    }
}
