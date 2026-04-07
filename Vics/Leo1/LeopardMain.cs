using System;
using System.Collections.Generic;
using System.Linq;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;

namespace UnderdogsEnhanced
{
    public static class LeopardMain
    {
        private const string PreferenceSeparator = "////////////////////////////////////Leopard Mod////////////////////////////////////";

        private static readonly string[] leopard1_series_variants = new string[]
        {
            "Leopard 1A3",
            "Leopard 1A3A1",
            "Leopard 1A3A2",
            "Leopard 1A3A3"
        };

        private static readonly string[] leopard_a1_series_variants = new string[]
        {
            "Leopard A1A1",
            "Leopard A1A2",
            "Leopard A1A3",
            "Leopard A1A4"
        };

        private static readonly HashSet<string> leopard1_pzb200_variants = new HashSet<string>(new string[]
        {
            "Leopard 1A3A1",
            "Leopard 1A3A3",
            "Leopard A1A2",
            "Leopard A1A4"
        }, StringComparer.OrdinalIgnoreCase);

        public static MelonPreferences_Entry<bool> leopard_enabled;

        private static readonly Dictionary<string, MelonPreferences_Entry<bool>> leopard1_convert_prefs = new Dictionary<string, MelonPreferences_Entry<bool>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MelonPreferences_Entry<bool>> leopard1_emes18_prefs = new Dictionary<string, MelonPreferences_Entry<bool>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MelonPreferences_Entry<string>> leopard1_ammo_prefs = new Dictionary<string, MelonPreferences_Entry<string>>(StringComparer.OrdinalIgnoreCase);

        public static void Config(MelonPreferences_Category cfg)
        {
            leopard1_convert_prefs.Clear();
            leopard1_emes18_prefs.Clear();
            leopard1_ammo_prefs.Clear();

            leopard_enabled = cfg.CreateEntry("Leopard Mod Master Switch", true);
            leopard_enabled.Description = PreferenceSeparator;

            foreach (string variant in leopard1_series_variants)
                CreateVariantPreferences(cfg, variant);

            foreach (string variant in leopard_a1_series_variants)
                CreateVariantPreferences(cfg, variant);

            var ammoEntry1A5 = cfg.CreateEntry("Leopard 1A5 Ammo AP Rounds", "Default");
            ammoEntry1A5.Description = "AP round type selection: Default=vanilla, DM33=420mm penetration, DM63=447mm penetration";
            ammoEntry1A5.Comment = "Default, DM33, DM63";
            leopard1_ammo_prefs["Leopard 1A5"] = ammoEntry1A5;
        }

        private static void CreateVariantPreferences(MelonPreferences_Category cfg, string variant)
        {
            var convertEntry = cfg.CreateEntry($"{variant} Convert to 1A5", true);
            convertEntry.Description = "Convert to Leopard 1A5 (EMES-18 sight + new turret appearance); disable to keep vanilla appearance";
            leopard1_convert_prefs[variant] = convertEntry;

            var emes18Entry = cfg.CreateEntry($"{variant} EMES18 FCS Only", false);
            emes18Entry.Description = $"Apply EMES-18 fire control system only, without model changes; only effective when '{variant} Convert to 1A5' is disabled";
            leopard1_emes18_prefs[variant] = emes18Entry;

            var ammoEntry = cfg.CreateEntry($"{variant} Ammo AP Rounds", "Default");
            ammoEntry.Description = "AP round type selection: Default=vanilla, DM33=420mm penetration, DM63=447mm penetration";
            ammoEntry.Comment = "Default, DM33, DM63";
            leopard1_ammo_prefs[variant] = ammoEntry;
        }

        internal static bool IsModEnabled()
        {
            return leopard_enabled != null && leopard_enabled.Value;
        }

        internal static bool IsSupportedVariant(string vehicleName)
        {
            return leopard1_convert_prefs.ContainsKey(vehicleName) || vehicleName == "Leopard 1A5";
        }

        internal static bool IsConvertEnabled(string vehicleName)
        {
            return IsModEnabled()
                && leopard1_convert_prefs.TryGetValue(vehicleName, out var entry)
                && entry != null
                && entry.Value;
        }

        internal static bool IsEmes18Enabled(string vehicleName)
        {
            return IsModEnabled()
                && leopard1_emes18_prefs.TryGetValue(vehicleName, out var entry)
                && entry != null
                && entry.Value;
        }

        internal static string GetAmmoType(string vehicleName)
        {
            if (leopard1_ammo_prefs.TryGetValue(vehicleName, out var entry) && entry != null)
                return entry.Value;
            return "Default";
        }

        internal static bool IsCustomAmmoEnabled(string vehicleName)
        {
            if (!IsModEnabled()) return false;
            string ammoType = GetAmmoType(vehicleName);
            return !string.Equals(ammoType, "Default", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsA1A4(string vehicleName)
        {
            return string.Equals(vehicleName, "Leopard A1A4", StringComparison.OrdinalIgnoreCase)
                || string.Equals(vehicleName, "Leopard 1A5", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ShouldScheduleAmmoConversion()
        {
            return IsModEnabled()
                && leopard1_ammo_prefs.Values.Any(entry => entry != null && !string.Equals(entry.Value, "Default", StringComparison.OrdinalIgnoreCase));
        }

        internal static void OnSceneLoaded()
        {
            if (IsModEnabled())
                Leopard1Model.Init();
        }

        internal static void Apply(Vehicle vic)
        {
            if (vic == null || !IsModEnabled()) return;

            string name = vic.FriendlyName;
            bool isSupportedLeopard = IsSupportedVariant(name);
            bool isA1A4 = IsA1A4(name);
            bool is1A5 = string.Equals(name, "Leopard 1A5", StringComparison.OrdinalIgnoreCase);
            bool leopardUsesPzb200 = leopard1_pzb200_variants.Contains(name);

            bool convertEnabled = IsConvertEnabled(name);
            bool emes18OnlyEnabled = !is1A5 && !convertEnabled && IsEmes18Enabled(name);
            bool convertedLeopardNeedsEmes18 = is1A5 || (isA1A4 && convertEnabled);

            if (convertedLeopardNeedsEmes18 || emes18OnlyEnabled)
            {
                WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
                WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];
                FireControlSystem fcs = main_gun_info.FCS;

                bool usePzb200Path = is1A5 || leopardUsesPzb200;
                EMES18Optic.TryApplyLeopardEmes18Suite(vic, fcs, name, usePzb200Path);
            }

            string ammoType = GetAmmoType(name);
            bool customAmmoEnabled = !string.Equals(ammoType, "Default", StringComparison.OrdinalIgnoreCase);
            if (isSupportedLeopard && customAmmoEnabled)
            {
#if DEBUG
                UnderdogsDebug.LogTiming($"[Leopard1] {name} Ammo={ammoType} 由 GameReady 弹药链统一处理");
#endif
            }
        }
    }
}
