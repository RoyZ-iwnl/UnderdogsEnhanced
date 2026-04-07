using System.Reflection;
using GHPC;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public static class Bmp1Main
    {
        public static MelonPreferences_Entry<bool> bmp1_enabled;
        public static MelonPreferences_Entry<bool> stab_bmp;
        public static MelonPreferences_Entry<bool> stab_konkurs;
        public static MelonPreferences_Entry<bool> bmp_lrf;
        public static MelonPreferences_Entry<bool> bmp1_mclos;
        public static MelonPreferences_Entry<int> bmp1_mclos_ready_count;
        public static MelonPreferences_Entry<bool> bmp1_mclos_flir_high_res;
        public static MelonPreferences_Entry<bool> bmp1_mclos_flir_no_scanline;

        private static object reticle_cached_bmp = null;
        internal const string BMP1_DAY_OPTIC_PATH = "BMP1_rig/HULL/TURRET/GUN/Gun Scripts/gunner day sight/Optic";

        public static void Config(MelonPreferences_Category cfg)
        {
            bmp1_enabled = cfg.CreateEntry("BMP-1 Mod Master Switch", true);
            bmp1_enabled.Description = "////////////////////////////////////BMP-1 Mod////////////////////////////////////";

            stab_bmp = cfg.CreateEntry("BMP-1 Stabilizer", true);
            stab_bmp.Description = "Gives BMP-1/BMP-1P a stabilizer (default: enabled)";
            stab_konkurs = cfg.CreateEntry("BMP-1P Konkurs Stab", false);
            stab_konkurs.Description = "Gives the Konkurs on the BMP-1P a stabilizer(default: disabled)";
            bmp_lrf = cfg.CreateEntry("BMP-1 Rangefinder", true);
            bmp_lrf.Description = "Gives BMP-1/BMP-1P a laser rangefinder (display only, no auto-ranging; default: enabled)";
            bmp1_mclos = cfg.CreateEntry("BMP-1 9M14TV Malyutka-TV", true);
            bmp1_mclos.Description = "Adds the fictional 9M14TV Malyutka-TV TV-guided missile for the BMP-1 (default: enabled)";
            bmp1_mclos_ready_count = cfg.CreateEntry("BMP-1 MCLOS Ready Missiles", -1);
            bmp1_mclos_ready_count.Description = "Ready rack missile count for BMP-1 MCLOS. -1 uses game's original count; >0 overrides.(max: 64)";
            bmp1_mclos_flir_high_res = cfg.CreateEntry("BMP-1 MCLOS FLIR High Resolution", false);
            bmp1_mclos_flir_high_res.Description = "Use 1024x576 FLIR resolution for BMP-1 MCLOS missile camera (default: low resolution)";
            bmp1_mclos_flir_no_scanline = cfg.CreateEntry("BMP-1 MCLOS FLIR Remove Scanline", false);
            bmp1_mclos_flir_no_scanline.Description = "Remove FLIR refresh scanline effect for BMP-1 MCLOS missile camera (default: disabled)";
        }

        internal static bool ShouldScheduleAmmoConversion()
        {
            return bmp1_enabled.Value && bmp1_mclos.Value;
        }

        internal static bool IsMclosTargetVehicle(string name)
        {
            return name == "BMP-1" || name == "BMP-1G";
        }

        private static bool IsMclosAmmoApplied(WeaponSystem atgm_ws)
        {
            var rack = atgm_ws?.Feed?.ReadyRack;
            if (rack == null || rack.ClipTypes == null || rack.ClipTypes.Length == 0) return false;

            var clip = rack.ClipTypes[0];
            return clip?.MinimalPattern != null
                && clip.MinimalPattern.Length > 0
                && clip.MinimalPattern[0]?.AmmoType?.Name == BMP1MCLOSAmmo.MISSILE_NAME;
        }

        internal static bool TryApplyMclos(Vehicle vic, bool logFailure = false)
        {
            if (vic == null || !bmp1_enabled.Value || !bmp1_mclos.Value) return false;
            if (!IsMclosTargetVehicle(vic.FriendlyName)) return false;

            WeaponsManager wm_mclos = vic.GetComponent<WeaponsManager>();
            if (wm_mclos == null || wm_mclos.Weapons == null || wm_mclos.Weapons.Length < 2 || wm_mclos.Weapons[1]?.Weapon == null)
            {
#if DEBUG
                if (logFailure && UnderdogsDebug.DEBUG_MCLOS)
                    MelonLogger.Warning($"[BMP-1 MCLOS] 武器系统未就绪: {vic.FriendlyName}");
#endif
                return false;
            }

            WeaponSystem atgm_ws = wm_mclos.Weapons[1].Weapon;
            if (atgm_ws?.Feed?.ReadyRack?.ClipTypes == null || atgm_ws.Feed.ReadyRack.ClipTypes.Length == 0 || atgm_ws.Feed.ReadyRack.ClipTypes[0] == null)
            {
#if DEBUG
                if (logFailure && UnderdogsDebug.DEBUG_MCLOS)
                    MelonLogger.Warning($"[BMP-1 MCLOS] AmmoRack 未就绪，稍后重试: {vic.FriendlyName}");
#endif
                return false;
            }

            if (IsMclosAmmoApplied(atgm_ws))
                return true;

            BMP1MissileCameraPatch.SetCurrentVehicle(vic.FriendlyName);

            MissileGuidanceUnit mgu = atgm_ws.GuidanceUnit;
            var day_optic_t = vic.gameObject.transform.Find(BMP1_DAY_OPTIC_PATH);

            if (mgu != null && day_optic_t != null)
            {
                mgu.AimElement = day_optic_t;
                BMP1MissileCameraPatch.BMP1OpticNode = day_optic_t.gameObject;
            }
#if DEBUG
            else if (logFailure && UnderdogsDebug.DEBUG_MCLOS)
            {
                MelonLogger.Warning($"[BMP-1 MCLOS] 初始化失败: mgu={mgu != null} day_optic={day_optic_t != null}");
            }

            UnderdogsDebug.LogMCLOS($"[BMP-1 MCLOS] 开始应用弹药: {vic.FriendlyName}");
#endif

            BMP1MCLOSAmmo.Apply(atgm_ws, vic);

#if DEBUG
            UnderdogsDebug.LogMCLOS($"[BMP-1 MCLOS] Apply() 返回: {vic.FriendlyName}");
#endif

            bool applied = IsMclosAmmoApplied(atgm_ws);
#if DEBUG
            if (UnderdogsDebug.DEBUG_MCLOS && applied)
                MelonLogger.Msg($"[BMP-1 MCLOS] 弹药应用成功: {vic.FriendlyName}");
#else
            if (applied)
                UnderdogsDebug.LogMCLOS($"[BMP-1 MCLOS] 弹药应用成功: {vic.FriendlyName}");
#endif

            return applied;
        }

        public static void Apply(Vehicle vic)
        {
            string name = vic.FriendlyName;
            if (name != "BMP-1" && name != "BMP-1P") return;
            if (!bmp1_enabled.Value) return;
            if (!stab_bmp.Value && !bmp_lrf.Value) return;

#if DEBUG
            UnderdogsDebug.LogTiming($"[UE]   > 匹配 BMP-1 改装 (stab={stab_bmp.Value} lrf={bmp_lrf.Value})");
#endif

            WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
            WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];

            if (stab_bmp.Value)
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

                int turret_platform_idx = name == "BMP-1" ? 3 : 1;
                aimables[turret_platform_idx].Stabilized = true;
                stab_active.SetValue(aimables[turret_platform_idx], true);
                stab_mode.SetValue(aimables[turret_platform_idx], StabilizationMode.Vector);

                if (stab_konkurs.Value && name == "BMP-1P")
                {
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

            if (bmp_lrf.Value)
            {
                FireControlSystem fcs = main_gun_info.FCS;
                var day_optic = vic.gameObject.transform.Find(BMP1_DAY_OPTIC_PATH).GetComponent<GHPC.Equipment.Optics.UsableOptic>();

#if DEBUG
                if (UnderdogsDebug.DEBUG_LRF)
                {
                    MelonLogger.Msg($"=== {name} LRF改装 ===");
                    MelonLogger.Msg($"FCS path={UnderdogsDebug.GetPath(fcs.transform, vic.transform)}");
                    MelonLogger.Msg($"LaserOrigin: {(fcs?.LaserOrigin != null ? UnderdogsDebug.GetPath(fcs.LaserOrigin, vic.transform) : "null，将自动创建")}");
                    MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                    if (day_optic?.reticleMesh?.reticleSO != null)
                        MelonLogger.Msg($"reticle planes[0] element count={day_optic.reticleMesh.reticleSO.planes[0].elements.Count}");
                }
#endif

                var gun = vic.gameObject.transform.Find("BMP1_rig/HULL/TURRET/GUN");
                LRFApplicator.ApplyLimitedLRF(fcs, day_optic, "BMP-1", ref reticle_cached_bmp, gun, new Vector2(46.8f, 469.4f));

#if DEBUG
                if (UnderdogsDebug.DEBUG_LRF)
                    MelonLogger.Msg($"{name} LRF完成 | LaserOrigin={fcs.LaserOrigin.name} MaxRange={fcs.MaxLaserRange}");
#endif
            }
        }
    }
}
