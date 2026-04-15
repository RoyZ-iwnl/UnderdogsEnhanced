using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;

namespace UnderdogsEnhanced
{
    public static class T34Main
    {
        public static MelonPreferences_Entry<bool> t34_enabled;
        public static MelonPreferences_Entry<bool> stab_t3485m;
        public static MelonPreferences_Entry<bool> t3485m_optics;
        public static MelonPreferences_Entry<bool> t3485m_lrf;

        private static object reticle_cached_t3485m = null;

        public static void Config(MelonPreferences_Category cfg)
        {
            t34_enabled = cfg.CreateEntry("T-34-85M Mod Master Switch", true);
            t34_enabled.Description = "////////////////////////////////////T-34-85M Mod////////////////////////////////////";

            stab_t3485m = cfg.CreateEntry("T-34-85M Stabilizer", false);
            stab_t3485m.Description = "Gives T-34-85M a stabilizer, a little bit buggy when you moving turret (default: disabled)";
            t3485m_optics = cfg.CreateEntry("T-34-85M Optics", true);
            t3485m_optics.Description = "Adds zoom levels to T-34-85M gunner sight (default: enabled)";
            t3485m_lrf = cfg.CreateEntry("T-34-85M Rangefinder", true);
            t3485m_lrf.Description = "Gives T-34-85M a laser rangefinder with auto-ranging (default: enabled)";
        }

        public static void Apply(Vehicle vic)
        {
            string name = vic.FriendlyName;
            if (name != "T-34-85M") return;
            if (!t34_enabled.Value) return;
            if (!stab_t3485m.Value && !t3485m_optics.Value && !t3485m_lrf.Value) return;

#if DEBUG
            UnderdogsDebug.LogTiming($"[TIMING]   > 匹配 T-34-85M 改装 (stab={stab_t3485m.Value} optics={t3485m_optics.Value} lrf={t3485m_lrf.Value})");
#endif

            WeaponsManager wm = vic.GetComponent<WeaponsManager>();
            FireControlSystem fcs = wm.Weapons[0].FCS;

            if (stab_t3485m.Value)
            {
                AimablePlatform[] aimables = vic.AimablePlatforms;
                FieldInfo stab_mode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo stab_active = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
                PropertyInfo stab_FCS_active = typeof(FireControlSystem).GetProperty("StabsActive", BindingFlags.Instance | BindingFlags.Public);

                stab_FCS_active.SetValue(fcs, true);
                fcs.CurrentStabMode = StabilizationMode.Vector;

                aimables[1].Stabilized = true;
                stab_active.SetValue(aimables[1], true);
                stab_mode.SetValue(aimables[1], StabilizationMode.Vector);

                aimables[0].Stabilized = true;
                stab_active.SetValue(aimables[0], true);
                stab_mode.SetValue(aimables[0], StabilizationMode.Vector);
            }

            if (t3485m_optics.Value)
            {
                CameraSlot sight = vic.gameObject.transform.Find("T34_rig/T34/HULL/TURRET/MANTLET/Sights and FCS")?.GetComponent<CameraSlot>();
                if (sight != null)
                {
                    sight.DefaultFov = 7.5f;
                    sight.OtherFovs = new float[] { 3.75f };
                }
            }

            if (t3485m_lrf.Value)
            {
                var day_optic = vic.gameObject.transform.Find("T34_rig/T34/HULL/TURRET/MANTLET/Sights and FCS/GPS")?.GetComponent<GHPC.Equipment.Optics.UsableOptic>();
                var gun_node = vic.gameObject.transform.Find("T34_rig/T34/HULL/TURRET/MANTLET");
                LRFApplicator.ApplyRedDotLRF(fcs, day_optic, "T34-85", ref reticle_cached_t3485m, gun_node, forceLaseCompat: true);
            }
        }
    }
}