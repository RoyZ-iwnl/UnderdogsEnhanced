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
    public static class Btr70Main
    {
        public static MelonPreferences_Entry<bool> btr70_enabled;
        public static MelonPreferences_Entry<bool> stab_btr70;
        public static MelonPreferences_Entry<bool> btr70_turret_speed;
        public static MelonPreferences_Entry<bool> btr70_optics;
        public static MelonPreferences_Entry<bool> btr70_lrf;

        private static object reticle_cached_btr70 = null;

        public static void Config(MelonPreferences_Category cfg)
        {
            btr70_enabled = cfg.CreateEntry("BTR-70 Mod Master Switch", true);
            btr70_enabled.Description = "////////////////////////////////////BTR-70 Mod////////////////////////////////////";

            stab_btr70 = cfg.CreateEntry("BTR-70 Stabilizer", true);
            stab_btr70.Description = "Gives BTR-70 a stabilizer (default: enabled)";
            btr70_turret_speed = cfg.CreateEntry("BTR-70 Turret Speed", true);
            btr70_turret_speed.Description = "Increases BTR-70 turret traverse speed (default: enabled)";
            btr70_optics = cfg.CreateEntry("BTR-70 Optics", true);
            btr70_optics.Description = "Adds zoom levels to BTR-70 gunner sight (default: enabled)";
            btr70_lrf = cfg.CreateEntry("BTR-70 Rangefinder", true);
            btr70_lrf.Description = "Gives BTR-70 a laser rangefinder (display only, no auto-ranging; default: enabled)";
        }

        public static void Apply(Vehicle vic)
        {
            string name = vic.FriendlyName;
            if (name != "BTR-70") return;
            if (!btr70_enabled.Value) return;
            if (!stab_btr70.Value && !btr70_lrf.Value) return;

#if DEBUG
            UnderdogsDebug.LogTiming($"[UE]   > 匹配 BTR-70 改装 (stab={stab_btr70.Value} lrf={btr70_lrf.Value})");
#endif

            WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
            WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];

            if (stab_btr70.Value)
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

                if (btr70_turret_speed.Value)
                {
                    aimables[0].SpeedPowered = 60;
                    aimables[0].SpeedUnpowered = 15;
                    aimables[1].SpeedPowered = 60;
                    aimables[1].SpeedUnpowered = 15;
                }

                if (btr70_optics.Value)
                {
                    CameraSlot sight = vic.gameObject.transform.Find("BTR70_rig/HULL/TURRET/GUN/Gun Aimable/gunner sight").GetComponent<CameraSlot>();
                    sight.DefaultFov = 16.5f;
                    sight.OtherFovs = new float[] { 8f, 4f, 2f };
                }
            }

            if (btr70_lrf.Value)
            {
                FireControlSystem fcs = main_gun_info.FCS;
                var day_optic = vic.gameObject.transform.Find("BTR70_rig/HULL/TURRET/GUN/Gun Aimable/gunner sight/GPS").GetComponent<GHPC.Equipment.Optics.UsableOptic>();

#if DEBUG
                if (UnderdogsDebug.DEBUG_LRF)
                {
                    MelonLogger.Msg($"=== BTR-70 LRF改装 ===");
                    MelonLogger.Msg($"FCS: {fcs?.name ?? "null"}");
                    MelonLogger.Msg($"LaserOrigin: {(fcs?.LaserOrigin != null ? fcs.LaserOrigin.name : "null，将自动创建")}");
                    MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                }
#endif

                var btr70_gun = vic.gameObject.transform.Find("BTR70_rig/HULL/TURRET/GUN/Gun Aimable");
                LRFApplicator.ApplyLimitedLRF(fcs, day_optic, "BRDM2", ref reticle_cached_btr70, btr70_gun, new Vector2(31.8f, 319.4f));

#if DEBUG
                if (UnderdogsDebug.DEBUG_LRF)
                    MelonLogger.Msg($"BTR-70 LRF完成 | LaserOrigin={fcs.LaserOrigin.name} MaxRange={fcs.MaxLaserRange}");
#endif
            }
        }
    }
}