using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public static class Brdm2Main
    {
        public static MelonPreferences_Entry<bool> brdm2_enabled;
        public static MelonPreferences_Entry<bool> stab_brdm;
        public static MelonPreferences_Entry<bool> brdm_turret_speed;
        public static MelonPreferences_Entry<bool> brdm_optics;
        public static MelonPreferences_Entry<bool> brdm_lrf;

        private static object reticle_cached_brdm = null;

        public static void Config(MelonPreferences_Category cfg)
        {
            brdm2_enabled = cfg.CreateEntry("BRDM-2 Mod Master Switch", true);
            brdm2_enabled.Description = "////////////////////////////////////BRDM-2 Mod////////////////////////////////////";

            stab_brdm = cfg.CreateEntry("BRDM-2 Stabilizer", true);
            stab_brdm.Description = "Gives BRDM-2 a stabilizer (default: enabled)";
            brdm_turret_speed = cfg.CreateEntry("BRDM-2 Turret Speed", true);
            brdm_turret_speed.Description = "Increases BRDM-2 turret traverse speed (default: enabled)";
            brdm_optics = cfg.CreateEntry("BRDM-2 Optics", true);
            brdm_optics.Description = "Adds zoom levels to BRDM-2 gunner sight (default: enabled)";
            brdm_lrf = cfg.CreateEntry("BRDM-2 Rangefinder", true);
            brdm_lrf.Description = "Gives BRDM-2 a laser rangefinder (display only, no auto-ranging; default: enabled)";
        }

        public static void Apply(Vehicle vic)
        {
            string name = vic.FriendlyName;
            if (name != "BRDM-2") return;
            if (!brdm2_enabled.Value) return;
            if (!stab_brdm.Value && !brdm_lrf.Value) return;

#if DEBUG
            UnderdogsDebug.LogTiming($"[TIMING]   > 匹配 BRDM-2 改装 (stab={stab_brdm.Value} lrf={brdm_lrf.Value})");
#endif

            WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
            WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];

            if (stab_brdm.Value)
            {
                AimablePlatform[] aimables = vic.AimablePlatforms;
                FieldInfo stab_mode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo stab_active = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
                PropertyInfo stab_FCS_active = typeof(FireControlSystem).GetProperty("StabsActive", BindingFlags.Instance | BindingFlags.Public);

                stab_FCS_active.SetValue(main_gun_info.FCS, true);
                main_gun_info.FCS.CurrentStabMode = StabilizationMode.Vector;

                aimables[0].Stabilized = true;
                stab_active.SetValue(aimables[0], true);
                stab_mode.SetValue(aimables[0], StabilizationMode.Vector);

                aimables[1].Stabilized = true;
                stab_active.SetValue(aimables[1], true);
                stab_mode.SetValue(aimables[1], StabilizationMode.Vector);

                if (brdm_turret_speed.Value)
                {
                    aimables[0].SpeedPowered = 60;
                    aimables[0].SpeedUnpowered = 15;
                    aimables[1].SpeedPowered = 60;
                    aimables[1].SpeedUnpowered = 15;
                }

                if (brdm_optics.Value)
                {
                    CameraSlot sight = vic.gameObject.transform.Find("BRDM2_rig/HULL/TURRET/GUN/---Gun Scripts/gunner sight").GetComponent<CameraSlot>();
                    sight.DefaultFov = 16.5f;
                    sight.OtherFovs = new float[] { 8f, 4f, 2f };
                }
            }

            if (brdm_lrf.Value)
            {
                FireControlSystem fcs = main_gun_info.FCS;
                var day_optic = vic.gameObject.transform.Find("BRDM2_rig/HULL/TURRET/GUN/---Gun Scripts/gunner sight/GPS").GetComponent<GHPC.Equipment.Optics.UsableOptic>();

#if DEBUG
                if (UnderdogsDebug.DEBUG_LRF)
                {
                    MelonLogger.Msg($"=== BRDM-2 LRF改装 ===");
                    MelonLogger.Msg($"FCS: {fcs?.name ?? "null"}");
                    MelonLogger.Msg($"LaserOrigin: {(fcs?.LaserOrigin != null ? fcs.LaserOrigin.name : "null，将自动创建")}");
                    MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                }
#endif

                var brdm_gun = vic.gameObject.transform.Find("BRDM2_rig/HULL/TURRET/GUN");
                LRFApplicator.ApplyLimitedLRF(fcs, day_optic, "BRDM2", ref reticle_cached_brdm, brdm_gun, new Vector2(31.8f, 319.4f));

#if DEBUG
                if (UnderdogsDebug.DEBUG_LRF)
                    MelonLogger.Msg($"BRDM-2 LRF完成 | LaserOrigin={fcs.LaserOrigin.name} MaxRange={fcs.MaxLaserRange}");
#endif
            }
        }
    }
}