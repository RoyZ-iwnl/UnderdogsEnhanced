using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;

namespace UnderdogsEnhanced
{
    public static class T54aMain
    {
        public static MelonPreferences_Entry<bool> t54a_enabled;
        public static MelonPreferences_Entry<bool> t54a_lrf;

        private static object reticle_cached_t54a = null;

        public static void Config(MelonPreferences_Category cfg)
        {
            t54a_enabled = cfg.CreateEntry("T-54A Mod Master Switch", true);
            t54a_enabled.Description = "////////////////////////////////////T-54A Mod////////////////////////////////////";

            t54a_lrf = cfg.CreateEntry("T-54A Rangefinder", true);
            t54a_lrf.Description = "Gives T-54A a laser rangefinder with auto-ranging (default: enabled)";
        }

        public static void Apply(Vehicle vic)
        {
            string name = vic.FriendlyName;
            if (name != "T-54A") return;
            if (!t54a_enabled.Value) return;
            if (!t54a_lrf.Value) return;

#if DEBUG
            UnderdogsDebug.LogTiming($"[UE]   > 匹配 T-54A 测距改装");
#endif

            WeaponsManager wm = vic.GetComponent<WeaponsManager>();
            FireControlSystem fcs = wm.Weapons[0].FCS;
            var day_optic = vic.gameObject.transform.Find("T55A_skeleton/HULL/Turret/GUN/Gun Scripts/Sights (and FCS)/GPS")?.GetComponent<GHPC.Equipment.Optics.UsableOptic>();
            var gun_node = vic.gameObject.transform.Find("T55A_skeleton/HULL/Turret/GUN");
            LRFApplicator.ApplyRedDotLRF(fcs, day_optic, "T55", ref reticle_cached_t54a, gun_node, forceLaseCompat: true);
        }
    }
}