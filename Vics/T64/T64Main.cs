using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;

namespace UnderdogsEnhanced
{
    public static class T64Main
    {
        public static MelonPreferences_Entry<bool> t64_enabled;
        public static MelonPreferences_Entry<bool> stab_t64_nsvt;
        public static MelonPreferences_Entry<bool> t64_nsvt_optics;

        public static void Config(MelonPreferences_Category cfg)
        {
            t64_enabled = cfg.CreateEntry("T-64 Mod Master Switch", true);
            t64_enabled.Description = "////////////////////////////////////T-64 Mod////////////////////////////////////";

            t64_nsvt_optics = cfg.CreateEntry("T-64 NSVT Optics", true);
            t64_nsvt_optics.Description = "Adds zoom levels to T-64 series NSVT sight (default: enabled)";
            stab_t64_nsvt = cfg.CreateEntry("T-64 NSVT Stabilizer", true);
            stab_t64_nsvt.Description = "Stabilizes T-64 series NSVT cupola and MG platform (default: enabled)";
        }

        public static void Apply(Vehicle vic)
        {
            string name = vic.FriendlyName;
            if (!name.StartsWith("T-64") || name == "T-64R") return;
            if (!t64_enabled.Value) return;

            if (stab_t64_nsvt.Value)
            {
#if DEBUG
                UnderdogsDebug.LogTiming($"[UE]   > 匹配 T-64 NSVT稳定改装");
#endif
                AimablePlatform[] aimables = vic.AimablePlatforms;
                FieldInfo stab_mode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo stab_active = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
                PropertyInfo stab_FCS_active = typeof(FireControlSystem).GetProperty("StabsActive", BindingFlags.Instance | BindingFlags.Public);

                WeaponSystemInfo nsvt_info = vic.GetComponent<WeaponsManager>()?.Weapons[2];
                if (nsvt_info != null)
                {
                    stab_FCS_active.SetValue(nsvt_info.FCS, true);
                    nsvt_info.FCS.CurrentStabMode = StabilizationMode.Vector;
                }

                aimables[1].Stabilized = true;
                stab_active.SetValue(aimables[1], true);
                stab_mode.SetValue(aimables[1], StabilizationMode.Vector);

                aimables[2].Stabilized = true;
                stab_active.SetValue(aimables[2], true);
                stab_mode.SetValue(aimables[2], StabilizationMode.Vector);
            }

            if (t64_nsvt_optics.Value)
            {
#if DEBUG
                UnderdogsDebug.LogTiming($"[UE]   > 匹配 T-64 NSVT瞄具改装");
#endif
                CameraSlot cws_sight = vic.gameObject.transform.Find("---T64A_MESH---/HULL/TURRET/TC ring/TC AA sight/CWS gunsight")?.GetComponent<CameraSlot>();
                if (cws_sight != null)
                    cws_sight.OtherFovs = new float[] { 25f, 12.5f, 6.25f };
            }
        }
    }
}