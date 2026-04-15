using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using NWH.VehiclePhysics;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public static class MarderMain
    {
        public static MelonPreferences_Entry<bool> marder_enabled;
        public static MelonPreferences_Entry<string> marder_cannon_caliber;
        public static MelonPreferences_Entry<string> marder_25mm_ap_belt;
        public static MelonPreferences_Entry<int> marder_25mm_ap_count;
        public static MelonPreferences_Entry<int> marder_25mm_he_count;
        public static MelonPreferences_Entry<string> marder_35mm_ap_belt;
        public static MelonPreferences_Entry<int> marder_35mm_ap_count;
        public static MelonPreferences_Entry<int> marder_35mm_he_count;
        public static MelonPreferences_Entry<bool> stab_marder;
        public static MelonPreferences_Entry<bool> stab_marder_milan;
        public static MelonPreferences_Entry<bool> marder_a1_thermal_retrofit;
        public static MelonPreferences_Entry<bool> marder_spike;
        public static MelonPreferences_Entry<int> marder_spike_ready_count;
        public static MelonPreferences_Entry<bool> marder_better_fcs;
        public static MelonPreferences_Entry<bool> marder_turret_speedup;
        public static MelonPreferences_Entry<bool> marder_engine_upgrade;
        public static void Config(MelonPreferences_Category cfg)
        {
            marder_enabled = cfg.CreateEntry("Marder Mod Master Switch", true);
            marder_enabled.Description = "////////////////////////////////////Marder Mod////////////////////////////////////";

            marder_cannon_caliber = cfg.CreateEntry("Marder Cannon Caliber", "25mm");
            marder_cannon_caliber.Description = "Main gun caliber selection. 25mm = Oerlikon KBA (175/600 RPM [B] toggle, M791/M792 or PMB090 ammo); 35mm = Oerlikon Revolver (200/1000 RPM [B] toggle, DM33/DM23/DM13 AP + DM11A1 HE). Valid: 25mm, 35mm (default: 25mm)";

            marder_25mm_ap_belt = cfg.CreateEntry("Marder 25mm AP Belt", "PMB090");
            marder_25mm_ap_belt.Description = "AP belt for 25mm KBA. PMB090 = Swiss APFSDS (92mm pen); M791. Valid: PMB090, M791 (default: PMB090)";
            marder_25mm_ap_count = cfg.CreateEntry("Marder 25mm AP Count", 254);
            marder_25mm_ap_count.Description = "AP belt round count. Default 254. Combined AP+HE should not exceed 508.";
            marder_25mm_he_count = cfg.CreateEntry("Marder 25mm HE Count", 254);
            marder_25mm_he_count.Description = "HE belt (M792 HEI-T) round count. Default 254. Combined AP+HE should not exceed 508.";

            marder_35mm_ap_belt = cfg.CreateEntry("Marder 35mm AP Belt", "DM33");
            marder_35mm_ap_belt.Description = "AP belt for 35mm Revolver. DM33 = APFSDS (131mm pen); DM23 = APDS (127mm pen); DM13 = APHE (66mm pen). Valid: DM33, DM23, DM13 (default: DM33)";
            marder_35mm_ap_count = cfg.CreateEntry("Marder 35mm AP Count", 254);
            marder_35mm_ap_count.Description = "AP belt round count. Default 254. Combined AP+HE should not exceed 508.";
            marder_35mm_he_count = cfg.CreateEntry("Marder 35mm HE Count", 254);
            marder_35mm_he_count.Description = "HE belt (DM11A1 HEI-T) round count. Default 254. Combined AP+HE should not exceed 508.";

            stab_marder = cfg.CreateEntry("Marder Stabilizer", true);
            stab_marder.Description = "Gives Marder series a stabilizer (default: enabled)";
            stab_marder_milan = cfg.CreateEntry("Marder MILAN Stabilizer", true);
            stab_marder_milan.Description = "Stabilizes MILAN launcher on Marder A1+ and Marder 1A2 (default: enabled)";
            marder_a1_thermal_retrofit = cfg.CreateEntry("Marder A1 Thermal Retrofit", true);
            marder_a1_thermal_retrofit.Description = "Replaces the stock night sight on Marder A1+, Marder A1- and Marder A1- (no ATGM) with the Marder 1A2 thermal sight (default: enabled)";
            marder_better_fcs = cfg.CreateEntry("Marder BetterFCS", true);
            marder_better_fcs.Description = "Enhanced FCS: laser rangefinder (6000m), superlead/superelevation, parallax fix, laser point correction, and RotateAzimuth (reticle stays on target when leading)";

            marder_turret_speedup = cfg.CreateEntry("Marder Turret Speedup", true);
            marder_turret_speedup.Description = "Increased turret rotation speed (default: enabled)";

            marder_engine_upgrade = cfg.CreateEntry("Marder Engine Upgrade", true);
            marder_engine_upgrade.Description = "Improved engine power, transmission, suspension, steering, and max speed (default: enabled)";

            marder_spike = cfg.CreateEntry("Marder Spike WIP", false);
            marder_spike.Description = "WIP WARNING:Converts Marder 1A2, Marder A1+ and Marder A1- MILAN launcher to a Spike-style hybrid TV/FnF suite (default: disabled)";
            marder_spike_ready_count = cfg.CreateEntry("Marder Spike Ready Missiles", -1);
            marder_spike_ready_count.Description = "WIP WARNING:Reserve missile count for Marder Spike (does not include the initial round loaded into breech). -1 uses game's original reserve logic; >0 overrides. (max: 64)";
        }

        public static void Apply(Vehicle vic)
        {
            string name = vic.FriendlyName;
            if (!marder_enabled.Value) return;
            if (!IsSupportedVariant(name)) return;

            WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
            WeaponSystemInfo main_gun_info = weapons_manager?.Weapons != null && weapons_manager.Weapons.Length > 0
                ? weapons_manager.Weapons[0]
                : null;
            WeaponSystem main_gun = main_gun_info?.Weapon;

            if (Is25mmSelected() && main_gun != null)
            {
                Marder25mmWeapon.ApplyDescriptor(main_gun_info);
                UECommonUtil.GetOrAddComponent<Marder25mmFireRateToggle>(main_gun.gameObject);
            }
            else if (Is35mmSelected() && main_gun != null)
            {
                Marder35mmWeapon.ApplyDescriptor(main_gun_info);
                UECommonUtil.GetOrAddComponent<Marder35mmFireRateToggle>(main_gun.gameObject);
            }

            if (marder_a1_thermal_retrofit != null && marder_a1_thermal_retrofit.Value)
                MarderThermalRetrofit.TryApply(vic);

            if (!stab_marder.Value) return;

#if DEBUG
            UnderdogsDebug.LogTiming($"[TIMING]   > 匹配 Marder 改装");
#endif

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

            // BetterFCS - FCS参数增强
            if (marder_better_fcs != null && marder_better_fcs.Value && main_gun_info?.FCS != null)
            {
                main_gun.FCS.MaxLaserRange = 6000;
                main_gun.FCS.SuperleadWeapon = true;
                main_gun.FCS.SuperelevateWeapon = true;
                main_gun.FCS.RegisteredRangeLimits = new Vector2(100, 6000);
                main_gun.FCS.RecordTraverseRateBuffer = true;
                main_gun.FCS.TraverseBufferSeconds = 0.5f;
                main_gun.FCS.DisplayRangeIncrement = 1;
                main_gun.FCS.LaserAim = LaserAimMode.ImpactPoint;
                main_gun.FCS.SuperelevateFireGating = false;
                main_gun.FCS.FireGateAngle = 0.5f;
                main_gun.FCS.InertialCompensation = false;
                main_gun.FCS.IgnoreHorizontalForFireGating = true;
                main_gun.FCS.WeaponAuthoritative = false;

                // 视差修正
                FieldInfo fixParallaxField = typeof(FireControlSystem).GetField("_fixParallaxForVectorMode", BindingFlags.Instance | BindingFlags.NonPublic);
                fixParallaxField.SetValue(main_gun.FCS, true);

                // 镭射点修正 - 通过反射获取 MainOptic 和 NightOptic
                //TODO 以后不用反射了，直接在 FCS 里加个接口获取光学瞄具就好了，考虑好和FILR改装的关系
                PropertyInfo p_fcs_mainOptic = typeof(FireControlSystem).GetProperty("MainOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo p_fcs_nightOptic = typeof(FireControlSystem).GetProperty("NightOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                UsableOptic dayOptic = p_fcs_mainOptic?.GetValue(main_gun_info.FCS, null) as UsableOptic;
                UsableOptic nightOptic = p_fcs_nightOptic?.GetValue(main_gun_info.FCS, null) as UsableOptic;

                if (dayOptic != null && nightOptic != null)
                    UECommonUtil.InstallLaserPointCorrection(main_gun_info.FCS, dayOptic, nightOptic);

                // RotateAzimuth - 射击提前量时准星始终在目标上
                if (dayOptic != null)
                {
                    dayOptic.RotateAzimuth = true;
                    dayOptic.Alignment = OpticAlignment.BoresightStabilized;
                    dayOptic.CantCorrect = true;
                    dayOptic.CantCorrectMaxSpeed = 5f;
                    dayOptic.ForceHorizontalReticleAlign = true;
                    dayOptic.ZeroOutInvalidRange = true;
                    if (dayOptic.slot != null)
                    {
                        dayOptic.slot.VibrationShakeMultiplier = 0f;
                        dayOptic.slot.VibrationPreBlur = false;
                        dayOptic.slot.VibrationBlurScale = 0f;
                    }
                }
                if (nightOptic != null)
                {
                    nightOptic.RotateAzimuth = true;
                    nightOptic.Alignment = OpticAlignment.BoresightStabilized;
                    nightOptic.CantCorrect = true;
                    nightOptic.CantCorrectMaxSpeed = 5f;
                    if (nightOptic.slot != null)
                    {
                        nightOptic.slot.VibrationShakeMultiplier = 0f;
                        nightOptic.slot.VibrationPreBlur = false;
                    }
                }
            }

            // Turret Speedup - 炮塔转速提升
            if (marder_turret_speedup != null && marder_turret_speedup.Value)
            {
                if (aimables != null && aimables.Length > 0)
                {
                    aimables[0].SpeedPowered = 80;
                    aimables[0].SpeedUnpowered = 20;
                }
            }

            // Engine Upgrade - 恢复为原版动力增强逻辑。
            // 双履带反向驱动实验代码已移到 MarderDualTrackSteering.cs 中，仅保留注释供后续调试。
            if (marder_engine_upgrade != null && marder_engine_upgrade.Value)
            {
                NwhChassis nwhChassis = vic.Chassis as NwhChassis;
                VehicleController vc = nwhChassis?.VehicleController;

                if (vc != null && nwhChassis != null)
                {
                    vc.engine.maxPower = 1200f;
                    vc.engine.maxRPM = 4500f;
                    vc.engine.maxRpmChange = 3000f;

                    vc.brakes.maxTorque = 55590;

                    nwhChassis._maxForwardSpeed = 32f;
                    nwhChassis._maxReverseSpeed = 16f;

                    List<float> fwGears = new List<float> { 6.28f, 4.81f, 2.98f, 1.76f, 1.36f, 1.16f };
                    vc.transmission.forwardGears = fwGears;
                    vc.transmission.gearMultiplier = 9.918f;
                    vc.transmission.initialShiftDuration = 0.1f;
                    vc.transmission.shiftDurationRandomness = 0f;
                    vc.transmission.shiftPointRandomness = 0.05f;

                    // 原版增强仅保留基础转向响应提升。
                    vc.steering.lowSpeedAngle = 45f;
                    vc.steering.highSpeedAngle = 15f;
                    vc.steering.trackedSteerIntensity = 3f;
                    vc.steering.degreesPerSecondLimit = 60f;
                    nwhChassis.SteerAccelerationMultiplier = 2f;

                    // 减少原车转向顿感。
                    nwhChassis.setHeadingSmoothTime = 0.05f;
                    nwhChassis.setHeadingMaxVel = 999f;

                    for (int i = 0; i < vc.wheels.Count; i++)
                    {
                        vc.wheels[i].wheelController.damper.maxForce = 6500;
                        vc.wheels[i].wheelController.damper.unitBumpForce = 6500;
                        vc.wheels[i].wheelController.damper.unitReboundForce = 9000;

                        vc.wheels[i].wheelController.spring.force = 24079.51f;
                        vc.wheels[i].wheelController.spring.length = 0.32f;
                        vc.wheels[i].wheelController.spring.maxForce = 100000;
                        vc.wheels[i].wheelController.spring.maxLength = 0.58f;

                        vc.wheels[i].wheelController.fFriction.forceCoefficient = 1.25f;
                        vc.wheels[i].wheelController.fFriction.slipCoefficient = 1f;
                        vc.wheels[i].wheelController.sFriction.forceCoefficient = 0.85f;
                        vc.wheels[i].wheelController.sFriction.slipCoefficient = 1f;
                    }
                }
            }

            if (stab_marder_milan.Value && (name == "Marder A1+" || name == "Marder 1A2" || name == "Marder A1-"))
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

        internal static bool ShouldScheduleAmmoConversion()
        {
            return marder_enabled.Value && (Is25mmSelected() || Is35mmSelected() || marder_spike.Value);
        }

        internal static bool Is35mmSelected()
        {
            return string.Equals(marder_cannon_caliber?.Value, "35mm", System.StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Is25mmSelected()
        {
            return !Is35mmSelected();
        }

        internal static bool IsSupportedVariant(string vehicleName)
        {
            return vehicleName == "Marder 1A2" || vehicleName == "Marder A1-" || vehicleName == "Marder A1+" || vehicleName == "Marder A1- (no ATGM)";
        }

        internal static string Get25mmApAmmoType()
        {
            string ammoType = marder_25mm_ap_belt != null ? marder_25mm_ap_belt.Value : null;
            if (string.Equals(ammoType, "M791", System.StringComparison.OrdinalIgnoreCase))
                return "M791";

            return "PMB090";
        }

        internal static int Get25mmApCount()
        {
            return GetNormalized25mmCounts().apCount;
        }

        internal static int Get25mmHeCount()
        {
            return GetNormalized25mmCounts().heCount;
        }

        private static (int apCount, int heCount) GetNormalized25mmCounts()
        {
            int apCount = marder_25mm_ap_count != null ? marder_25mm_ap_count.Value : 254;
            int heCount = marder_25mm_he_count != null ? marder_25mm_he_count.Value : 254;

            apCount = System.Math.Max(1, apCount);
            heCount = System.Math.Max(1, heCount);

            if (apCount + heCount > 508)
                return (254, 254);

            return (apCount, heCount);
        }

        internal static string Get35mmApAmmoType()
        {
            string ammoType = marder_35mm_ap_belt != null ? marder_35mm_ap_belt.Value : null;
            if (string.Equals(ammoType, "DM23", System.StringComparison.OrdinalIgnoreCase))
                return "DM23";
            if (string.Equals(ammoType, "DM13", System.StringComparison.OrdinalIgnoreCase))
                return "DM13";

            return "DM33";
        }

        internal static int Get35mmApCount()
        {
            return GetNormalized35mmCounts().apCount;
        }

        internal static int Get35mmHeCount()
        {
            return GetNormalized35mmCounts().heCount;
        }

        private static (int apCount, int heCount) GetNormalized35mmCounts()
        {
            int apCount = marder_35mm_ap_count != null ? marder_35mm_ap_count.Value : 254;
            int heCount = marder_35mm_he_count != null ? marder_35mm_he_count.Value : 254;

            apCount = System.Math.Max(1, apCount);
            heCount = System.Math.Max(1, heCount);

            if (apCount + heCount > 508)
                return (254, 254);

            return (apCount, heCount);
        }
    }
}
