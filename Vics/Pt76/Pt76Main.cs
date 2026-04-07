using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;

namespace UnderdogsEnhanced
{
    public static class Pt76Main
    {
        public static MelonPreferences_Entry<bool> pt76_enabled;
        public static MelonPreferences_Entry<bool> pt76_lrf;
        public static MelonPreferences_Entry<bool> pt76_optics;

        private static object reticle_cached_pt76 = null;

        public static void Config(MelonPreferences_Category cfg)
        {
            pt76_enabled = cfg.CreateEntry("PT-76B Mod Master Switch", true);
            pt76_enabled.Description = "////////////////////////////////////PT-76B Mod////////////////////////////////////";

            pt76_lrf = cfg.CreateEntry("PT-76B Rangefinder", true);
            pt76_lrf.Description = "Gives PT-76B a laser rangefinder with auto-ranging (default: enabled)";
            pt76_optics = cfg.CreateEntry("PT-76B Optics", true);
            pt76_optics.Description = "Adds zoom levels to PT-76B gunner sight (default: enabled)";
        }

        public static void Apply(Vehicle vic)
        {
            string name = vic.FriendlyName;
            if (name != "PT-76B") return;
            if (!pt76_enabled.Value) return;
            if (!pt76_lrf.Value) return;

#if DEBUG
            UnderdogsDebug.LogTiming($"[UE]   > 匹配 PT-76B 测距改装");
#endif

            WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
            WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];
            FireControlSystem fcs = main_gun_info.FCS;

            if (pt76_optics.Value)
            {
                CameraSlot sight = vic.gameObject.transform.Find("PT76_rig/HULL/TURRET/GUN/---MAIN GUN SCRIPTS---/D-56TS/Sights (and FCS)").GetComponent<CameraSlot>();
                sight.DefaultFov = 16.5f;
                sight.OtherFovs = new float[] { 8f, 4f };
            }

            var day_optic = vic.gameObject.transform.Find("PT76_rig/HULL/TURRET/GUN/---MAIN GUN SCRIPTS---/D-56TS/Sights (and FCS)/GPS").GetComponent<GHPC.Equipment.Optics.UsableOptic>();
            var pt76_gun = vic.gameObject.transform.Find("PT76_rig/HULL/TURRET/GUN");

#if DEBUG
            if (UnderdogsDebug.DEBUG_LRF)
            {
                MelonLogger.Msg($"=== PT-76B LRF改装 ===");
                MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
            }
#endif

            LRFApplicator.ApplyRedDotLRF(fcs, day_optic, "PT", ref reticle_cached_pt76, pt76_gun);

#if DEBUG
            if (UnderdogsDebug.DEBUG_LRF)
                MelonLogger.Msg($"PT-76B LRF完成 | LaserOrigin={fcs.LaserOrigin?.name} MaxRange={fcs.MaxLaserRange}");
#endif
        }
    }
}