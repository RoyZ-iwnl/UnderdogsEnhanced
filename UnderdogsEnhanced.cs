using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using MelonLoader;
using UnderdogsEnhanced;
using GHPC.Vehicle;
using GHPC;
using GHPC.State;
using System.Reflection;
using GHPC.Weapons;
using GHPC.Weaponry;
using GHPC.Camera;
using TMPro;
using Reticle;
using HarmonyLib;

[assembly: MelonInfo(typeof(UnderdogsEnhancedMod), "Underdogs Enhanced", "1.4.0", "RoyZ;Based on ATLAS work")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace UnderdogsEnhanced
{
    public class UnderdogsEnhancedMod : MelonMod
    {
        public static MelonPreferences_Category cfg;
        public static MelonPreferences_Entry<bool> stab_bmp;
        public static MelonPreferences_Entry<bool> stab_konkurs;
        public static MelonPreferences_Entry<bool> stab_marder;
        public static MelonPreferences_Entry<bool> stab_marder_milan;
        public static MelonPreferences_Entry<bool> stab_brdm;
        public static MelonPreferences_Entry<bool> marder_rangefinder;
        public static MelonPreferences_Entry<bool> brdm_turret_speed;
        public static MelonPreferences_Entry<bool> brdm_optics;
        public static MelonPreferences_Entry<bool> brdm_lrf;
        public static MelonPreferences_Entry<bool> bmp_lrf;
        public static MelonPreferences_Entry<bool> stab_btr70;
        public static MelonPreferences_Entry<bool> btr70_turret_speed;
        public static MelonPreferences_Entry<bool> btr70_optics;
        public static MelonPreferences_Entry<bool> btr70_lrf;
        public static MelonPreferences_Entry<bool> pt76_lrf;
        public static MelonPreferences_Entry<bool> pt76_optics;
        public static MelonPreferences_Entry<bool> stab_t64_nsvt;
        public static MelonPreferences_Entry<bool> t64_nsvt_optics;
        public static MelonPreferences_Entry<bool> t54a_lrf;
        public static MelonPreferences_Entry<bool> stab_t3485m;
        public static MelonPreferences_Entry<bool> t3485m_optics;
        public static MelonPreferences_Entry<bool> t3485m_lrf;
        public static MelonPreferences_Entry<bool> bmp1_mclos;
        public static MelonPreferences_Entry<int> bmp1_mclos_ready_count;
        public static MelonPreferences_Entry<bool> bmp1_mclos_flir_high_res;
        public static MelonPreferences_Entry<bool> bmp1_mclos_flir_no_scanline;

        private static GameObject range_readout;
        private static object reticle_cached_bmp = null;
        private static object reticle_cached_brdm = null;
        private static object reticle_cached_btr70 = null;
        private static object reticle_cached_pt76 = null;
        private static object reticle_cached_t54a = null;
        private static object reticle_cached_t3485m = null;
        private static readonly string[] leopard1_variants = new string[]
        {
            "Leopard A1A4",
            "Leopard 1A3",
            "Leopard 1A3A2",
            "Leopard A1A1",
            "Leopard A1A3",
            "Leopard 1A3A1",
            "Leopard 1A3A3",
            "Leopard A1A2"
        };
        private static readonly HashSet<string> leopard1_b171_variants = new HashSet<string>(new string[]
        {
            "Leopard 1A3",
            "Leopard 1A3A2",
            "Leopard A1A1",
            "Leopard A1A3"
        }, System.StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> leopard1_pzb200_variants = new HashSet<string>(new string[]
        {
            "Leopard 1A3A1",
            "Leopard 1A3A3",
            "Leopard A1A2",
            "Leopard A1A4"
        }, System.StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MelonPreferences_Entry<bool>> leopard1_emes18_prefs = new Dictionary<string, MelonPreferences_Entry<bool>>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MelonPreferences_Entry<bool>> leopard1_dm63_prefs = new Dictionary<string, MelonPreferences_Entry<bool>>(System.StringComparer.OrdinalIgnoreCase);
        private const string BMP1_DAY_OPTIC_PATH = "BMP1_rig/HULL/TURRET/GUN/Gun Scripts/gunner day sight/Optic";
        private static readonly FieldInfo f_uo_hasGuidance = typeof(GHPC.Equipment.Optics.UsableOptic).GetField("_hasGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_fcs_mainOptic = typeof(FireControlSystem).GetProperty("MainOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_mainOptic_backing = typeof(FireControlSystem).GetField("<MainOptic>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_authoritativeOptic = typeof(FireControlSystem).GetField("AuthoritativeOptic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_fixParallaxForVectorMode = typeof(FireControlSystem).GetField("_fixParallaxForVectorMode", BindingFlags.Instance | BindingFlags.NonPublic);

        private static HashSet<int> _modifiedVehicleIds = new HashSet<int>();
        private static HashSet<int> _leopard1AmmoApplied = new HashSet<int>();

        private static void CreateLeopardVariantPreferences()
        {
            leopard1_emes18_prefs.Clear();
            leopard1_dm63_prefs.Clear();

            foreach (string variant in leopard1_variants)
            {
                var emesEntry = cfg.CreateEntry($"{variant} EMES18 Sight", true);
                emesEntry.Description = $"Applies EMES18 FCS to {variant} (laser rangefinder + 3-12 FLIR optics + point-n-shoot; default: enabled)";
                leopard1_emes18_prefs[variant] = emesEntry;

                var dm63Entry = cfg.CreateEntry($"{variant} DM63 Ammo", true);
                dm63Entry.Description = $"Replaces stock APFSDS with DM63 on {variant} (default: enabled)";
                leopard1_dm63_prefs[variant] = dm63Entry;
            }
        }

        private static bool IsSupportedLeopard1Variant(string vehicleName)
        {
            return leopard1_emes18_prefs.ContainsKey(vehicleName);
        }

        private static bool LeopardUsesB171(string vehicleName)
        {
            return leopard1_b171_variants.Contains(vehicleName);
        }

        private static bool LeopardUsesPzb200(string vehicleName)
        {
            return leopard1_pzb200_variants.Contains(vehicleName);
        }

        private static bool IsLeopardA1A4(string vehicleName)
        {
            return string.Equals(vehicleName, "Leopard A1A4", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLeopardEmes18Enabled(string vehicleName)
        {
            return leopard1_emes18_prefs.TryGetValue(vehicleName, out var entry) && entry != null && entry.Value;
        }

        private static bool IsLeopardDm63Enabled(string vehicleName)
        {
            return leopard1_dm63_prefs.TryGetValue(vehicleName, out var entry) && entry != null && entry.Value;
        }

        private string[] invalid_scenes = new string[] { "MainMenu2_Scene", "MainMenu2-1_Scene", "LOADER_MENU", "LOADER_INITIAL", "t64_menu" };

        private static void PrintChildren(Transform t, int depth = 0)
        {
            string indent = new string(' ', depth * 2);
            MelonLogger.Msg($"{indent}{t.name} [active={t.gameObject.activeSelf}]");
            for (int i = 0; i < t.childCount; i++)
                PrintChildren(t.GetChild(i), depth + 1);
        }

        public override void OnInitializeMelon() {
            cfg = MelonPreferences.CreateCategory("Underdogs-Enhanced");
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
            stab_brdm = cfg.CreateEntry("BRDM-2 Stabilizer", true);
            stab_brdm.Description = "Gives BRDM-2 a stabilizer (default: enabled)";
            brdm_turret_speed = cfg.CreateEntry("BRDM-2 Turret Speed", true);
            brdm_turret_speed.Description = "Increases BRDM-2 turret traverse speed (default: enabled)";
            brdm_optics = cfg.CreateEntry("BRDM-2 Optics", true);
            brdm_optics.Description = "Adds zoom levels to BRDM-2 gunner sight (default: enabled)";
            brdm_lrf = cfg.CreateEntry("BRDM-2 Rangefinder", true);
            brdm_lrf.Description = "Gives BRDM-2 a laser rangefinder (display only, no auto-ranging; default: enabled)";
            stab_btr70 = cfg.CreateEntry("BTR-70 Stabilizer", true);
            stab_btr70.Description = "Gives BTR-70 a stabilizer (default: enabled)";
            btr70_turret_speed = cfg.CreateEntry("BTR-70 Turret Speed", true);
            btr70_turret_speed.Description = "Increases BTR-70 turret traverse speed (default: enabled)";
            btr70_optics = cfg.CreateEntry("BTR-70 Optics", true);
            btr70_optics.Description = "Adds zoom levels to BTR-70 gunner sight (default: enabled)";
            btr70_lrf = cfg.CreateEntry("BTR-70 Rangefinder", true);
            btr70_lrf.Description = "Gives BTR-70 a laser rangefinder (display only, no auto-ranging; default: enabled)";
            stab_marder = cfg.CreateEntry("Marder Stabilizer", true);
            stab_marder.Description = "Gives Marder series a stabilizer (default: enabled)";
            stab_marder_milan = cfg.CreateEntry("Marder MILAN Stabilizer", true);
            stab_marder_milan.Description = "Stabilizes MILAN launcher on Marder A1+ and Marder 1A2 (default: enabled)";
            marder_rangefinder = cfg.CreateEntry("Marder Rangefinder", true);
            marder_rangefinder.Description = "Gives Marder series laser rangefinder and parallax fix (default: enabled)";
            CreateLeopardVariantPreferences();
            pt76_lrf = cfg.CreateEntry("PT-76B Rangefinder", true);
            pt76_lrf.Description = "Gives PT-76B a laser rangefinder with auto-ranging (default: enabled)";
            pt76_optics = cfg.CreateEntry("PT-76B Optics", true);
            pt76_optics.Description = "Adds zoom levels to PT-76B gunner sight (default: enabled)";
            t64_nsvt_optics = cfg.CreateEntry("T-64 NSVT Optics", true);
            t64_nsvt_optics.Description = "Adds zoom levels to T-64 series NSVT sight (default: enabled)";
            stab_t64_nsvt = cfg.CreateEntry("T-64 NSVT Stabilizer", true);
            stab_t64_nsvt.Description = "Stabilizes T-64 series NSVT cupola and MG platform (default: enabled)";
            t54a_lrf = cfg.CreateEntry("T-54A Rangefinder", true);
            t54a_lrf.Description = "Gives T-54A a laser rangefinder with auto-ranging (default: enabled)";
            stab_t3485m = cfg.CreateEntry("T-34-85M Stabilizer", false);
            stab_t3485m.Description = "Gives T-34-85M a stabilizer, a little bit buggy when you moving turret (default: disabled)";
            t3485m_optics = cfg.CreateEntry("T-34-85M Optics", true);
            t3485m_optics.Description = "Adds zoom levels to T-34-85M gunner sight (default: enabled)";
            t3485m_lrf = cfg.CreateEntry("T-34-85M Rangefinder", true);
            t3485m_lrf.Description = "Gives T-34-85M a laser rangefinder with auto-ranging (default: enabled)";
         }

        private static void ShallowCopy<T>(T dst, T src)
        {
            foreach (var f in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                f.SetValue(dst, f.GetValue(src));
        }

        private static GameObject GetOrCreateRangeReadout()
        {
            if (range_readout != null) return range_readout;

            foreach (Vehicle obj in Resources.FindObjectsOfTypeAll<Vehicle>())
            {
                if (obj.name != "_M1IP (variant)") continue;
                range_readout = GameObject.Instantiate(obj.transform.Find("Turret Scripts/GPS/Optic/Abrams GPS canvas").gameObject);
                GameObject.Destroy(range_readout.transform.GetChild(2).gameObject);
                GameObject.Destroy(range_readout.transform.GetChild(0).gameObject);
                range_readout.SetActive(false);
                range_readout.hideFlags = HideFlags.DontUnloadUnusedAsset;
                TextMeshProUGUI text = range_readout.GetComponentInChildren<TextMeshProUGUI>();
                text.color = new Color(1f, 0f, 0f);
                text.faceColor = new Color(1f, 0f, 0f);
                text.outlineColor = new Color32(100, 0, 0, 128);
                text.outlineWidth = 0.2f;
                text.text = "";
                break;
            }
            return range_readout;
        }

        private static void EnsureLaseReadiness(FireControlSystem fcs, GHPC.Equipment.Optics.UsableOptic dayOptic, bool forceLaseCompat)
        {
            if (fcs == null || dayOptic == null) return;

            try { dayOptic.FCS = fcs; } catch { }
            if (!forceLaseCompat) return;

            bool hasMainOptic = false;
            try
            {
                if (p_fcs_mainOptic != null)
                    hasMainOptic = p_fcs_mainOptic.GetValue(fcs, null) as GHPC.Equipment.Optics.UsableOptic != null;
            }
            catch { }

            if (!hasMainOptic)
            {
                bool assignedMainOptic = false;
                if (f_fcs_mainOptic_backing != null)
                {
                    try
                    {
                        f_fcs_mainOptic_backing.SetValue(fcs, dayOptic);
                        assignedMainOptic = true;
                    }
                    catch { }
                }

                if (!assignedMainOptic && p_fcs_mainOptic != null)
                {
                    try
                    {
                        p_fcs_mainOptic.SetValue(fcs, dayOptic, null);
                        assignedMainOptic = true;
                    }
                    catch { }
                }

            }

            if (f_fcs_authoritativeOptic != null)
            {
                try
                {
                    if (f_fcs_authoritativeOptic.GetValue(fcs) == null)
                        f_fcs_authoritativeOptic.SetValue(fcs, dayOptic);
                }
                catch { }
            }

            try { f_uo_hasGuidance?.SetValue(dayOptic, true); } catch { }
            try { dayOptic.GuidanceLight = true; } catch { }

            try { fcs.RegisterOptic(dayOptic); } catch { }
            try { fcs.NotifyActiveOpticChanged(dayOptic); } catch { }

            if (forceLaseCompat && fcs.GetComponent<ForceLaseCompat>() == null)
                fcs.gameObject.AddComponent<ForceLaseCompat>();
        }

        private static void ApplyRedDotLRF(FireControlSystem fcs, GHPC.Equipment.Optics.UsableOptic dayOptic, string cacheKey, ref object reticle_cached_ref, Transform laserParent = null, bool forceLaseCompat = false)
        {
            if (fcs == null || dayOptic == null || dayOptic.reticleMesh == null)
            {
                if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                    MelonLogger.Warning($"[UE] RedDotLRF 跳过: FCS/Optic 未就绪 | FCS={(fcs != null)} Optic={(dayOptic != null)} Reticle={(dayOptic?.reticleMesh != null)}");
                return;
            }

            if (forceLaseCompat && dayOptic != null)
            {
                // 不重挂原始 LaserOrigin，避免破坏原车节点关系导致瞄具姿态异常。
                bool needCompatOrigin = fcs.LaserOrigin == null ||
                                        fcs.LaserOrigin.name != "ue_lase" ||
                                        fcs.LaserOrigin.parent != dayOptic.transform;
                if (needCompatOrigin)
                {
                    GameObject laseCompat = new GameObject("ue_lase");
                    laseCompat.transform.SetParent(dayOptic.transform, false);
                    laseCompat.transform.localPosition = new Vector3(0f, 0f, 0.2f);
                    laseCompat.transform.localRotation = Quaternion.identity;
                    fcs.LaserOrigin = laseCompat.transform;
                }
            }
            else if (fcs.LaserOrigin == null)
            {
                GameObject lase = new GameObject("lase");
                lase.transform.SetParent(laserParent ?? fcs.transform, false);
                fcs.LaserOrigin = lase.transform;
            }

            fcs.LaserAim = LaserAimMode.Fixed;
            fcs.MaxLaserRange = 4000f;
            EnsureLaseReadiness(fcs, dayOptic, forceLaseCompat);

            var rm = dayOptic.reticleMesh;
            var f_cachedReticles = typeof(ReticleMesh).GetField("cachedReticles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var cachedReticles = f_cachedReticles?.GetValue(null) as System.Collections.IDictionary;
            if (cachedReticles != null && cachedReticles.Contains(cacheKey))
            {
                var srcCached = cachedReticles[cacheKey];
                var cachedType = srcCached.GetType();
                var f_tree = cachedType.GetField("tree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var f_mesh = cachedType.GetField("mesh", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var f_reticle = typeof(ReticleMesh).GetField("reticle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var f_smr = typeof(ReticleMesh).GetField("SMR", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var reticleSO = ScriptableObject.Instantiate(f_tree.GetValue(srcCached) as ReticleSO);
                if (reticle_cached_ref == null) reticle_cached_ref = System.Activator.CreateInstance(cachedType);
                ShallowCopy(reticle_cached_ref, srcCached);
                f_tree.SetValue(reticle_cached_ref, reticleSO);

                reticleSO.lights = new System.Collections.Generic.List<ReticleTree.Light>() { new ReticleTree.Light(), new ReticleTree.Light() };
                reticleSO.lights[0] = (f_tree.GetValue(srcCached) as ReticleSO).lights[0];
                reticleSO.lights[1].type = ReticleTree.Light.Type.Powered;
                reticleSO.lights[1].color = new RGB(15f, 0f, 0f, true);

                reticleSO.planes[0].elements.Add(new ReticleTree.Angular(new Vector2(0, 0), null, ReticleTree.GroupBase.Alignment.LasePoint));
                var lasePoint = reticleSO.planes[0].elements[reticleSO.planes[0].elements.Count - 1] as ReticleTree.Angular;
                f_mesh.SetValue(reticle_cached_ref, null);
                lasePoint.name = "LasePoint";
                lasePoint.position = new ReticleTree.Position(0, 0, AngularLength.AngularUnit.MIL_USSR, LinearLength.LinearUnit.M);
                lasePoint.elements.Add(new ReticleTree.Circle());
                var circle = lasePoint.elements[0] as ReticleTree.Circle;
                var f_mrad = typeof(AngularLength).GetField("mrad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object br = circle.radius; f_mrad.SetValue(br, 0.5236f); circle.radius = (AngularLength)br;
                object bt = circle.thickness; f_mrad.SetValue(bt, 0.16f); circle.thickness = (AngularLength)bt;
                circle.illumination = ReticleTree.Light.Type.Powered;
                circle.visualType = ReticleTree.VisualElement.Type.ReflectedAdditive;
                circle.position = new ReticleTree.Position(0, 0, AngularLength.AngularUnit.MIL_USSR, LinearLength.LinearUnit.M);

                rm.reticleSO = reticleSO;
                f_reticle?.SetValue(rm, reticle_cached_ref);
                f_smr?.SetValue(rm, null);
                rm.Load();
            }
        }

        private static void ApplyLimitedLRF(FireControlSystem fcs, GHPC.Equipment.Optics.UsableOptic dayOptic, string cacheKey, ref object reticle_cached_ref, Transform laserParent = null, Vector2 textPos = default)
        {
            if (fcs == null || dayOptic == null || dayOptic.reticleMesh == null) return;
            if (fcs.gameObject.GetComponent<LimitedLRF>() != null) return;
            if (fcs.LaserOrigin == null)
            {
                GameObject lase = new GameObject("lase");
                lase.transform.SetParent(laserParent ?? fcs.transform, false);
                fcs.LaserOrigin = lase.transform;
            }

            fcs.LaserAim = LaserAimMode.Fixed;
            fcs.MaxLaserRange = 4000f;
            EnsureLaseReadiness(fcs, dayOptic, false);
            fcs.gameObject.AddComponent<LimitedLRF>();

            var rm = dayOptic.reticleMesh;
            var f_cachedReticles = typeof(ReticleMesh).GetField("cachedReticles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var cachedReticles = f_cachedReticles?.GetValue(null) as System.Collections.IDictionary;
            if (cachedReticles != null && cachedReticles.Contains(cacheKey))
            {
                var srcCached = cachedReticles[cacheKey];
                var cachedType = srcCached.GetType();
                var f_tree = cachedType.GetField("tree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var f_mesh = cachedType.GetField("mesh", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var f_reticle = typeof(ReticleMesh).GetField("reticle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var f_smr = typeof(ReticleMesh).GetField("SMR", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var reticleSO = ScriptableObject.Instantiate(f_tree.GetValue(srcCached) as ReticleSO);
                if (reticle_cached_ref == null) reticle_cached_ref = System.Activator.CreateInstance(cachedType);
                ShallowCopy(reticle_cached_ref, srcCached);
                f_tree.SetValue(reticle_cached_ref, reticleSO);

                reticleSO.lights = new System.Collections.Generic.List<ReticleTree.Light>() { new ReticleTree.Light(), new ReticleTree.Light() };
                reticleSO.lights[0] = (f_tree.GetValue(srcCached) as ReticleSO).lights[0];
                reticleSO.lights[1].type = ReticleTree.Light.Type.Powered;
                reticleSO.lights[1].color = new RGB(15f, 0f, 0f, true);

                reticleSO.planes[0].elements.Add(new ReticleTree.Angular(new Vector2(0, 0), null, ReticleTree.GroupBase.Alignment.LasePoint));
                var lasePoint = reticleSO.planes[0].elements[reticleSO.planes[0].elements.Count - 1] as ReticleTree.Angular;
                f_mesh.SetValue(reticle_cached_ref, null);
                lasePoint.name = "LasePoint";
                lasePoint.position = new ReticleTree.Position(0, 0, AngularLength.AngularUnit.MIL_USSR, LinearLength.LinearUnit.M);
                lasePoint.elements.Add(new ReticleTree.Circle());
                var circle = lasePoint.elements[0] as ReticleTree.Circle;
                var f_mrad = typeof(AngularLength).GetField("mrad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object br = circle.radius; f_mrad.SetValue(br, 0.5236f); circle.radius = (AngularLength)br;
                object bt = circle.thickness; f_mrad.SetValue(bt, 0.16f); circle.thickness = (AngularLength)bt;
                circle.illumination = ReticleTree.Light.Type.Powered;
                circle.visualType = ReticleTree.VisualElement.Type.ReflectedAdditive;
                circle.position = new ReticleTree.Position(0, 0, AngularLength.AngularUnit.MIL_USSR, LinearLength.LinearUnit.M);

                rm.reticleSO = reticleSO;
                f_reticle?.SetValue(rm, reticle_cached_ref);
                f_smr?.SetValue(rm, null);
                rm.Load();
            }

            GameObject canvas = GameObject.Instantiate(GetOrCreateRangeReadout());
            canvas.transform.SetParent(dayOptic.transform, false);
            canvas.SetActive(true);
            var canvasText = canvas.GetComponentInChildren<TextMeshProUGUI>();
            canvasText.text = "0000";
            foreach (var t in canvas.GetComponentsInChildren<TextMeshProUGUI>())
                if (t.name == "fault text (TMP)") t.gameObject.SetActive(false);
            if (textPos != default)
                canvasText.rectTransform.anchoredPosition = textPos;
            fcs.GetComponent<LimitedLRF>().canvas = canvas.transform;
        }

        private static bool IsMclosTargetVehicle(string name)
        {
            return name == "BMP-1" || name == "BMP-1G";
        }

        private static bool IsMclosAmmoApplied(WeaponSystem atgm_ws)
        {
            var rack = atgm_ws?.Feed?.ReadyRack;
            if (rack == null || rack.ClipTypes == null || rack.ClipTypes.Length == 0) return false;

            var clip = rack.ClipTypes[0];
            return clip?.MinimalPattern != null &&
                clip.MinimalPattern.Length > 0 &&
                clip.MinimalPattern[0]?.AmmoType?.Name == BMP1MCLOSAmmo.MISSILE_NAME;
        }

        private static bool TryApplyBmp1Mclos(Vehicle vic, bool logFailure = false)
        {
            if (vic == null || !bmp1_mclos.Value) return false;
            if (!IsMclosTargetVehicle(vic.FriendlyName)) return false;

            WeaponsManager wm_mclos = vic.GetComponent<WeaponsManager>();
            if (wm_mclos == null || wm_mclos.Weapons == null || wm_mclos.Weapons.Length < 2 || wm_mclos.Weapons[1]?.Weapon == null)
            {
                if (logFailure && UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_MCLOS)
                    MelonLogger.Warning($"[BMP-1 MCLOS] 武器系统未就绪: {vic.FriendlyName}");
                return false;
            }

            WeaponSystem atgm_ws = wm_mclos.Weapons[1].Weapon;
            if (atgm_ws?.Feed?.ReadyRack?.ClipTypes == null || atgm_ws.Feed.ReadyRack.ClipTypes.Length == 0 || atgm_ws.Feed.ReadyRack.ClipTypes[0] == null)
            {
                if (logFailure && UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_MCLOS)
                    MelonLogger.Warning($"[BMP-1 MCLOS] AmmoRack 未就绪，稍后重试: {vic.FriendlyName}");
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
            else if (logFailure && UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_MCLOS)
            {
                MelonLogger.Warning($"[BMP-1 MCLOS] 初始化失败: mgu={mgu != null} day_optic={day_optic_t != null}");
            }

            UnderdogsDebug.LogMCLOS($"[BMP-1 MCLOS] 开始应用弹药: {vic.FriendlyName}");

            BMP1MCLOSAmmo.Apply(atgm_ws, vic);

            UnderdogsDebug.LogMCLOS($"[BMP-1 MCLOS] Apply() 返回: {vic.FriendlyName}");

            bool applied = IsMclosAmmoApplied(atgm_ws);
            if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_MCLOS && applied)
                MelonLogger.Msg($"[BMP-1 MCLOS] 弹药应用成功: {vic.FriendlyName}");

            return applied;
        }

        private IEnumerator EnsureBmp1MclosOnGameReady(GameState _)
        {
            if (!bmp1_mclos.Value) yield break;

            const int maxAttempts = 80; // about 20s
            bool everFoundTarget = false;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Vehicle[] allVehicles = Object.FindObjectsOfType<Vehicle>();
                bool foundTarget = false;
                bool allApplied = true;

                foreach (Vehicle vic in allVehicles)
                {
                    if (vic == null || !IsMclosTargetVehicle(vic.FriendlyName)) continue;

                    foundTarget = true;

                    if (!TryApplyBmp1Mclos(vic, logFailure: attempt == maxAttempts - 1))
                        allApplied = false;
                }

                if (foundTarget)
                    everFoundTarget = true;

                if (foundTarget && allApplied)
                {
                    if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_MCLOS)
                        MelonLogger.Msg("[BMP-1 MCLOS] GameReady 阶段应用完成");
                    yield break;
                }

                // 连续多次扫描都没发现 BMP-1/BMP-1G 则提前退出，避免无意义重试
                if (!everFoundTarget && attempt >= 5)
                {
                    if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_MCLOS)
                        MelonLogger.Msg("[BMP-1 MCLOS] 场景中无 BMP-1/BMP-1G，跳过 MCLOS 改装");
                    yield break;
                }

                yield return new WaitForSeconds(0.25f);
            }

            if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_MCLOS)
                MelonLogger.Warning("[BMP-1 MCLOS] GameReady 重试结束，仍有载具未应用");
        }

        public override void OnUpdate()
        {
            UnderdogsDebug.HandleDebugKeys();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Load static assets on menu scenes before checking invalid_scenes
            if (sceneName == "MainMenu2_Scene" || sceneName == "MainMenu2-1_Scene" || sceneName == "t64_menu")
            {
                EMES18Optic.LoadStaticAssets();
            }

            if (invalid_scenes.Contains(sceneName)) return;

            if (bmp1_mclos.Value)
                StateController.RunOrDefer(GameState.GameReady, new GameStateEventHandler(EnsureBmp1MclosOnGameReady), GameStatePriority.Lowest);
        }

        public override async void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (invalid_scenes.Contains(sceneName)) return;

            float _sceneStartTime = Time.realtimeSinceStartup;
            UnderdogsDebug.LogTiming($"[UE] >>> OnSceneWasInitialized | 场景={sceneName} | 时间={System.DateTime.Now:HH:mm:ss.fff}");

            if (UnderdogsDebug.DEBUG_MODE)
            {
                // 初始化调试UI（运行时开关，不依赖编译配置）
                AmmoDebugUI.Init();
            }

            Vehicle[] all_vehicles = new Vehicle[0];
            int _waitCount = 0;
            const int _maxWaitAttempts = 60; // 60 * 500ms = 30s max
            do {
                await Task.Delay(500);
                _waitCount++;
                all_vehicles = Object.FindObjectsOfType<Vehicle>();
                if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_TIMING && _waitCount % 4 == 0)
                    MelonLogger.Msg($"[UE] 等待载具中... {_waitCount * 500}ms | 当前={all_vehicles?.Length ?? 0}个");
            } while (_waitCount < _maxWaitAttempts && (all_vehicles == null || all_vehicles.Length == 0 || !all_vehicles.Any(v => v != null && (
                v.FriendlyName == "BMP-1" || v.FriendlyName == "BMP-1P" ||
                v.FriendlyName == "BRDM-2" || v.FriendlyName == "BTR-70" ||
                v.FriendlyName == "PT-76B" || v.FriendlyName.StartsWith("Marder") ||
                v.FriendlyName.StartsWith("Leopard") || v.FriendlyName.StartsWith("T-64")))));

            if (all_vehicles == null || all_vehicles.Length == 0)
            {
                if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_TIMING)
                    MelonLogger.Warning("[UE] 30秒内未检测到任何 Vehicle，跳过本次场景改装");
                return;
            }

            UnderdogsDebug.LogTiming($"[UE] 等待 {_waitCount * 500}ms 后发现 {all_vehicles.Length} 个载具 | 距场景加载 {Time.realtimeSinceStartup - _sceneStartTime:F3}s");




            if (UnderdogsDebug.DEBUG_MODE)
            {
                if (UnderdogsDebug.DEBUG_VEHICLE)
                {
                    var _cr = typeof(ReticleMesh).GetField("cachedReticles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as System.Collections.IDictionary;
                    MelonLogger.Msg($"=== 找到 {all_vehicles.Length} 个载具 ===");
                    foreach (Vehicle v in all_vehicles)
                    {
                        MelonLogger.Msg($"[{v.FriendlyName}] tag={v.gameObject.tag} obj={v.gameObject.name}");
                        AimablePlatform[] aps = v.AimablePlatforms;
                        var f_stabMode = typeof(AimablePlatform).GetField("_stabMode", BindingFlags.Instance | BindingFlags.NonPublic);
                        var f_stabActive = typeof(AimablePlatform).GetField("_stabActive", BindingFlags.Instance | BindingFlags.NonPublic);
                        for (int i = 0; i < aps.Length; i++)
                            MelonLogger.Msg($"  [{i}] {aps[i].name} | Stabilized={aps[i].Stabilized} _stabActive={f_stabActive?.GetValue(aps[i])} _stabMode={f_stabMode?.GetValue(aps[i])}");
                        foreach (var cs in v.gameObject.GetComponentsInChildren<CameraSlot>())
                            MelonLogger.Msg($"  CameraSlot: {UnderdogsDebug.GetPath(cs.transform, v.transform)} | DefaultFov={cs.DefaultFov} OtherFovs=[{string.Join(", ", cs.OtherFovs ?? new float[0])}]");
                        foreach (var uo in v.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true))
                        {
                            var f_hasGuidance = typeof(GHPC.Equipment.Optics.UsableOptic).GetField("_hasGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
                            bool hasGuidance = f_hasGuidance != null && (bool)f_hasGuidance.GetValue(uo);
                            MelonLogger.Msg($"  UsableOptic: {UnderdogsDebug.GetPath(uo.transform, v.transform)} | GuidanceLight={uo.GuidanceLight} _hasGuidance={hasGuidance} FCS={uo.FCS?.name ?? "null"}");
                            if (uo.reticleMesh != null)
                            {
                                string soName = uo.reticleMesh.reticleSO?.name ?? "null";
                                bool isCached = _cr != null && _cr.Contains(soName);
                                MelonLogger.Msg($"    reticleSO: {soName} | cached={isCached}");
                                var tree = uo.reticleMesh.reticleSO;
                                if (tree != null)
                                    foreach (var plane in tree.planes)
                                        for (int ei = 0; ei < plane.elements.Count; ei++)
                                            MelonLogger.Msg($"    plane element[{ei}]: {plane.elements[ei].GetType().Name}");
                            }
                        }
                        var wm = v.GetComponent<WeaponsManager>();
                        if (wm != null)
                        {
                            for (int wi = 0; wi < wm.Weapons.Length; wi++)
                            {
                                var wsi = wm.Weapons[wi];
                                var fcs = wsi.FCS;
                                if (fcs == null) continue;
                                MelonLogger.Msg($"  [武器{wi}] {wsi.Name} | FCS: {UnderdogsDebug.GetPath(fcs.transform, v.transform)}");
                                MelonLogger.Msg($"    LaserOrigin: {(fcs.LaserOrigin != null ? UnderdogsDebug.GetPath(fcs.LaserOrigin, v.transform) : "null")}");
                                MelonLogger.Msg($"    LaserAim: {fcs.LaserAim} MaxLaserRange: {fcs.MaxLaserRange} DefaultRange: {fcs.DefaultRange}");
                                MelonLogger.Msg($"    StabsActive={fcs.StabsActive} CurrentStabMode={fcs.CurrentStabMode} SuperelevateWeapon: {fcs.SuperelevateWeapon} SuperleadWeapon: {fcs.SuperleadWeapon}");

                                // WeaponSystem 详细数据
                                var ws = wsi.Weapon;
                                if (ws != null)
                                {
                                    MelonLogger.Msg($"    WeaponSystem: TriggerHoldTime={ws.TriggerHoldTime} MaxSpeedToFire={ws.MaxSpeedToFire} MaxSpeedToDeploy={ws.MaxSpeedToDeploy}");
                                    MelonLogger.Msg($"    WeaponSystem: Impulse={ws.Impulse} BaseDeviationAngle={ws.BaseDeviationAngle}");

                                    // GuidanceUnit
                                    var gu = ws.GuidanceUnit;
                                    if (gu != null)
                                    {
                                        MelonLogger.Msg($"    GuidanceUnit: path={UnderdogsDebug.GetPath(gu.transform, v.transform)}");
                                        MelonLogger.Msg($"    GuidanceUnit: IsGuidingMissile={gu.IsGuidingMissile} Damaged={gu.Damaged} RangeSetting={gu.RangeSetting}");
                                        MelonLogger.Msg($"    GuidanceUnit: AimElement={gu.AimElement?.name ?? "null"} ResetAimOnLaunch={gu.ResetAimOnLaunch}");
                                        MelonLogger.Msg($"    GuidanceUnit: ManualAimAngularVelocity={gu.ManualAimAngularVelocity}");
                                        MelonLogger.Msg($"    GuidanceUnit: CurrentMissiles.Count={gu.CurrentMissiles?.Count ?? -1}");
                                    }
                                    else
                                    {
                                        MelonLogger.Msg($"    GuidanceUnit: null");
                                    }

                                    // Feed / 弹药信息
                                    var feed = ws.Feed;
                                    if (feed != null)
                                    {
                                        var breechAmmo = feed.AmmoTypeInBreech;
                                        MelonLogger.Msg($"    Feed.AmmoTypeInBreech: {breechAmmo?.Name ?? "null"}");

                                        var rack = feed.ReadyRack;
                                        if (rack != null)
                                        {
                                            MelonLogger.Msg($"    ReadyRack: ClipTypes.Length={rack.ClipTypes?.Length ?? -1}");
                                            if (rack.ClipTypes != null)
                                            {
                                                for (int ci = 0; ci < rack.ClipTypes.Length; ci++)
                                                {
                                                    var clip = rack.ClipTypes[ci];
                                                    MelonLogger.Msg($"      ClipTypes[{ci}]: {clip?.Name ?? "null"}");
                                                    // 如果膛内没弹，从弹夹的 MinimalPattern 拿弹药数据
                                                    var ammoFromClip = (breechAmmo == null && clip?.MinimalPattern?.Length > 0)
                                                        ? clip.MinimalPattern[0]?.AmmoType : null;
                                                    var ammo = ci == 0 ? (breechAmmo ?? ammoFromClip) : ammoFromClip;
                                                    if (ammo != null)
                                                    {
                                                        MelonLogger.Msg($"        AmmoCodex={clip?.MinimalPattern?[0]?.name ?? "null"}");
                                                        MelonLogger.Msg($"        Caliber={ammo.Caliber} Category={ammo.Category}");
                                                        MelonLogger.Msg($"        RhaPen={ammo.RhaPenetration} MuzzleVel={ammo.MuzzleVelocity} Mass={ammo.Mass}");
                                                        MelonLogger.Msg($"        TntKg={ammo.TntEquivalentKg} Spall={ammo.SpallMultiplier}");
                                                        MelonLogger.Msg($"        Guidance={ammo.Guidance} Flight={ammo.Flight}");
                                                        MelonLogger.Msg($"        TurnSpeed={ammo.TurnSpeed}");
                                                        MelonLogger.Msg($"        GuidanceLockoutTime={ammo.GuidanceLockoutTime} GuidanceNoLockoutRange={ammo.GuidanceNoLockoutRange}");
                                                        MelonLogger.Msg($"        GuidanceLeadDistance={ammo.GuidanceLeadDistance} GuidanceNoLoiterRange={ammo.GuidanceNoLoiterRange}");
                                                        MelonLogger.Msg($"        ClimbAngle={ammo.ClimbAngle} DiveAngle={ammo.DiveAngle} LoiterAltitude={ammo.LoiterAltitude}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // LoadoutManager 信息
                        var lm = v.GetComponent<LoadoutManager>();
                        if (lm != null)
                        {
                            var f_totalAmmoCount = typeof(LoadoutManager).GetField("_totalAmmoCount", BindingFlags.Instance | BindingFlags.NonPublic);
                            int totalCount = f_totalAmmoCount != null ? (int)f_totalAmmoCount.GetValue(lm) : -1;
                            MelonLogger.Msg($"  LoadoutManager: _totalAmmoCount={totalCount}");
                            if (lm.TotalAmmoCounts != null)
                                MelonLogger.Msg($"    TotalAmmoCounts=[{string.Join(", ", lm.TotalAmmoCounts)}]");
                            if (lm.LoadedAmmoList?.AmmoClips != null)
                            {
                                MelonLogger.Msg($"    LoadedAmmoList.AmmoClips.Length={lm.LoadedAmmoList.AmmoClips.Length}");
                                for (int i = 0; i < lm.LoadedAmmoList.AmmoClips.Length; i++)
                                {
                                    var clipCodex = lm.LoadedAmmoList.AmmoClips[i];
                                    MelonLogger.Msg($"      [{i}] {clipCodex?.name ?? "null"} -> {clipCodex?.ClipType?.Name ?? "null"}");
                                }
                            }
                            if (lm.RackLoadouts != null)
                            {
                                MelonLogger.Msg($"    RackLoadouts.Length={lm.RackLoadouts.Length}");
                                for (int i = 0; i < lm.RackLoadouts.Length; i++)
                                {
                                    var rl = lm.RackLoadouts[i];
                                    MelonLogger.Msg($"      Rack[{i}]: Capacity={rl.Rack?.ClipCapacity ?? -1}");
                                }
                            }
                        }
                    }
                }

                if (UnderdogsDebug.DEBUG_CHILDREN)
                {
                    MelonLogger.Msg($"=== 子节点结构 ===");
                    foreach (Vehicle v in all_vehicles)
                    {
                        MelonLogger.Msg($"[{v.FriendlyName}] 子节点结构:");
                        for (int ci = 0; ci < v.transform.childCount; ci++)
                            PrintChildren(v.transform.GetChild(ci), 1);
                    }
                }

                if (UnderdogsDebug.DEBUG_ARMOR)
                {
                    MelonLogger.Msg("=== 装甲数据 ===");
                    var f_avgRha = typeof(VariableArmor).GetField("AverageRha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var f_armorType = typeof(VariableArmor).GetField("_armorType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (GameObject armour in GameObject.FindGameObjectsWithTag("Penetrable"))
                    {
                        if (armour == null) continue;
                        GHPC.IArmor ia = (GHPC.IArmor)armour.GetComponent<UniformArmor>() ?? armour.GetComponent<VariableArmor>();
                        if (ia == null) continue;
                        string vicName = armour.GetComponentInParent<Vehicle>()?.FriendlyName ?? "unknown";
                        if (ia is VariableArmor va2)
                        {
                            float avg = f_avgRha != null ? (float)f_avgRha.GetValue(va2) : 0f;
                            var codex = f_armorType?.GetValue(va2) as ArmorCodexScriptable;
                            var at = codex?.ArmorType;
                            MelonLogger.Msg($"  [{vicName}] (Variable) {ia.Name} | AverageRha={avg} BHN={at?.BHN} KeMult={at?.RhaeMultiplierKe} CeMult={at?.RhaeMultiplierCe} | HEAT={ia.HeatRha} KE={ia.SabotRha}");
                        }
                        else
                            MelonLogger.Msg($"  [{vicName}] (Uniform) {ia.Name} | HEAT={ia.HeatRha} KE={ia.SabotRha}");
                    }
                }
            }

            var currentIds = new HashSet<int>(all_vehicles.Where(v => v != null).Select(v => v.GetInstanceID()));
            _modifiedVehicleIds.IntersectWith(currentIds);

            // 试车场执行2轮扫描，第2轮捕获其他mod（如GMPC）延迟生成的载具
            int _uePassCount = (sceneName == "TR01_showcase") ? 2 : 1;

            for (int _uePass = 1; _uePass <= _uePassCount; _uePass++)
            {
                if (_uePass > 1)
                {
                    if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_TIMING)
                        MelonLogger.Msg($"[UE] 试车场第{_uePass}轮: 等待3秒后重新扫描载具...");

                    await Task.Delay(3000);
                    all_vehicles = Object.FindObjectsOfType<Vehicle>();
                    if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_TIMING)
                        MelonLogger.Msg($"[UE] 第{_uePass}轮扫描到 {all_vehicles.Length} 个载具");
                }

                float _passStart = Time.realtimeSinceStartup;
                if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_TIMING)
                    MelonLogger.Msg($"[UE] === 第{_uePass}/{_uePassCount}轮改装开始 | 场景={sceneName} | 载具数={all_vehicles.Length} | {System.DateTime.Now:HH:mm:ss.fff} ===");

                foreach (Vehicle vic in all_vehicles)
                {
                    if (vic == null) continue;
                    int _vid = vic.GetInstanceID();
                    if (_modifiedVehicleIds.Contains(_vid)) continue;
                    _modifiedVehicleIds.Add(_vid);

                    string name = vic.FriendlyName;
                    bool dumpA1A3 = UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_VEHICLE && name == "Leopard A1A3";
                    if (dumpA1A3)
                        UnderdogsDebug.DumpVehicleOpticsSnapshot(vic, "PRE", detailedOptics: true);
                    UnderdogsDebug.LogTiming($"[UE] 第{_uePass}轮 >> [{name}] ID={_vid} obj={vic.gameObject.name}");

                    try
                    {

                if ((stab_bmp.Value || bmp_lrf.Value) && (name == "BMP-1" || name == "BMP-1P"))
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 BMP-1 改装 (stab={stab_bmp.Value} lrf={bmp_lrf.Value})");
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

                        if (stab_konkurs.Value && name == "BMP-1P") {
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
                        var day_optic = vic.gameObject.transform.Find("BMP1_rig/HULL/TURRET/GUN/Gun Scripts/gunner day sight/Optic").GetComponent<GHPC.Equipment.Optics.UsableOptic>();

                        if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                        {
                            MelonLogger.Msg($"=== {name} LRF改装 ===");
                            MelonLogger.Msg($"FCS path={UnderdogsDebug.GetPath(fcs.transform, vic.transform)}");
                            MelonLogger.Msg($"LaserOrigin: {(fcs?.LaserOrigin != null ? UnderdogsDebug.GetPath(fcs.LaserOrigin, vic.transform) : "null，将自动创建")}");
                            MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                            if (day_optic?.reticleMesh?.reticleSO != null)
                                MelonLogger.Msg($"reticle planes[0] element count={day_optic.reticleMesh.reticleSO.planes[0].elements.Count}");
                        }

                        var gun = vic.gameObject.transform.Find("BMP1_rig/HULL/TURRET/GUN");
                        ApplyLimitedLRF(fcs, day_optic, "BMP-1", ref reticle_cached_bmp, gun, new Vector2(46.8f, 469.4f));

                        if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                            MelonLogger.Msg($"{name} LRF完成 | LaserOrigin={fcs.LaserOrigin.name} MaxRange={fcs.MaxLaserRange}");
                    }

                }

                if (stab_marder.Value && (name == "Marder 1A2" || name == "Marder A1-" || name == "Marder A1+"))
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 Marder 改装");
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

                bool isSupportedLeopard = IsSupportedLeopard1Variant(name);
                bool isA1A4 = IsLeopardA1A4(name);
                bool leopardUsesB171 = LeopardUsesB171(name);
                bool leopardUsesPzb200 = LeopardUsesPzb200(name);
                bool leopardEmes18Enabled = isSupportedLeopard && IsLeopardEmes18Enabled(name);
                bool leopardDm63Enabled = isSupportedLeopard && IsLeopardDm63Enabled(name);

                // 应用DM63弹药到所有豹1
                if (isSupportedLeopard && leopardDm63Enabled)
                {
                    int vicId = vic.GetInstanceID();
                    if (_leopard1AmmoApplied.Contains(vicId))
                    {
                        UnderdogsDebug.LogTiming($"[Leopard1] {name} 已应用过DM63，跳过");
                    }
                    else
                    {
                        if (UnderdogsDebug.DEBUG_MODE) MelonLogger.Msg($"[Leopard1] 检测到 {name}，准备应用DM63");

                        LoadoutManager loadout_manager = vic.GetComponent<LoadoutManager>();
                        if (loadout_manager?.LoadedAmmoList?.AmmoClips != null)
                        {
                            if (UnderdogsDebug.DEBUG_MODE) MelonLogger.Msg($"[Leopard1] LoadoutManager弹药数={loadout_manager.LoadedAmmoList.AmmoClips.Length}");

                            // 从LoadoutManager查找APFSDS
                            for (int i = 0; i < loadout_manager.LoadedAmmoList.AmmoClips.Length; i++)
                            {
                                var clipCodex = loadout_manager.LoadedAmmoList.AmmoClips[i];
                                var ammo = clipCodex?.ClipType?.MinimalPattern?[0]?.AmmoType;
                                if (ammo != null && (ammo.Name.Contains("DM23") || ammo.Name.Contains("DM13")))
                                {
                                    if (UnderdogsDebug.DEBUG_MODE) MelonLogger.Msg($"[Leopard1] 找到APFSDS: {ammo.Name} at index {i}");

                                    // 每次都重新初始化，因为不同豹1变种可能用不同基础弹药
                                    Leopard1Ammo.Init(ammo);
                                    if (UnderdogsDebug.DEBUG_MODE) MelonLogger.Msg($"[Leopard1] DM63初始化: {ammo.Name} -> DM63");

                                    if (Leopard1Ammo.clip_dm63 != null)
                                    {
                                        // 创建新的ClipCodex并替换
                                        var new_clip_codex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
                                        new_clip_codex.name = "clip_dm63";
                                        new_clip_codex.ClipType = Leopard1Ammo.clip_dm63;
                                        loadout_manager.LoadedAmmoList.AmmoClips[i] = new_clip_codex;

                                        _leopard1AmmoApplied.Add(vicId);
                                        if (UnderdogsDebug.DEBUG_MODE) MelonLogger.Msg($"[Leopard1] {name} DM63已应用");
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }

                if (isSupportedLeopard && leopardEmes18Enabled)
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 Leopard 激光测距改装");
                    WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
                    WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];
                    FireControlSystem fcs = main_gun_info.FCS;

                    // Apply EMES18 suite to Leopard 1 optics
                    if (isSupportedLeopard)
                    {
                        if (UnderdogsDebug.DEBUG_MODE) MelonLogger.Msg($"[UE] Applying Leopard EMES18 suite to {name}");

                        GHPC.Equipment.Optics.UsableOptic gps_day =
                            vic.gameObject.transform.Find("LEO1A1A1_rig/HULL/TURRET/--Turret Scripts--/Sights/GPS")
                            ?.GetComponent<GHPC.Equipment.Optics.UsableOptic>();
                        if (gps_day == null)
                            gps_day = vic.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true)
                                .FirstOrDefault(o => o != null && o.name.Equals("GPS", System.StringComparison.OrdinalIgnoreCase));

                        string fallbackNightOpticName = isA1A4 || leopardUsesPzb200 ? "PZB-200" : "B 171";
                        string fallbackNightOpticPath = isA1A4 || leopardUsesPzb200
                            ? "LEO1A1A1_rig/HULL/TURRET/Mantlet/--Gun Scripts--/PZB-200"
                            : "LEO1A1A1_rig/HULL/TURRET/--Turret Scripts--/Sights/B 171";

                        GHPC.Equipment.Optics.UsableOptic linked_night_optic = null;
                        try { linked_night_optic = gps_day?.slot?.LinkedNightSight?.PairedOptic; } catch { }
                        if (linked_night_optic == null)
                            linked_night_optic = vic.gameObject.transform.Find(fallbackNightOpticPath)
                                ?.GetComponent<GHPC.Equipment.Optics.UsableOptic>();
                        if (linked_night_optic == null)
                            linked_night_optic = vic.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true)
                                .FirstOrDefault(o => o != null && o.name.Equals(fallbackNightOpticName, System.StringComparison.OrdinalIgnoreCase));

                        if (gps_day != null && linked_night_optic != null)
                        {
                            gps_day.enabled = true;
                            linked_night_optic.enabled = true;
                            EMES18Optic.ApplyLeopardEmes18Suite(gps_day, linked_night_optic, fcs, name);
                            if (UnderdogsDebug.DEBUG_MODE) MelonLogger.Msg($"[UE] Leopard EMES18 suite applied ({fallbackNightOpticName} + GPS)");
                        }
                        else if (UnderdogsDebug.DEBUG_MODE)
                        {
                            MelonLogger.Warning($"[UE] Leopard optics missing | veh={name} GPS={(gps_day != null)} Night={(linked_night_optic != null)} expected={fallbackNightOpticName}");
                        }
                    }
                }

                if ((stab_brdm.Value || brdm_lrf.Value) && name == "BRDM-2")
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 BRDM-2 改装 (stab={stab_brdm.Value} lrf={brdm_lrf.Value})");
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

                        if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                        {
                            MelonLogger.Msg($"=== BRDM-2 LRF改装 ===");
                            MelonLogger.Msg($"FCS: {fcs?.name ?? "null"}");
                            MelonLogger.Msg($"LaserOrigin: {(fcs?.LaserOrigin != null ? fcs.LaserOrigin.name : "null，将自动创建")}");
                            MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                        }

                        var brdm_gun = vic.gameObject.transform.Find("BRDM2_rig/HULL/TURRET/GUN");
                        ApplyLimitedLRF(fcs, day_optic, "BRDM2", ref reticle_cached_brdm, brdm_gun, new Vector2(31.8f, 319.4f));

                        if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                            MelonLogger.Msg($"BRDM-2 LRF完成 | LaserOrigin={fcs.LaserOrigin.name} MaxRange={fcs.MaxLaserRange}");
                    }
                }

                if ((stab_btr70.Value || btr70_lrf.Value) && name == "BTR-70")
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 BTR-70 改装 (stab={stab_btr70.Value} lrf={btr70_lrf.Value})");
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

                        if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                        {
                            MelonLogger.Msg($"=== BTR-70 LRF改装 ===");
                            MelonLogger.Msg($"FCS: {fcs?.name ?? "null"}");
                            MelonLogger.Msg($"LaserOrigin: {(fcs?.LaserOrigin != null ? fcs.LaserOrigin.name : "null，将自动创建")}");
                            MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                        }

                        var btr70_gun = vic.gameObject.transform.Find("BTR70_rig/HULL/TURRET/GUN/Gun Aimable");
                        ApplyLimitedLRF(fcs, day_optic, "BRDM2", ref reticle_cached_btr70, btr70_gun, new Vector2(31.8f, 319.4f));

                        if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                            MelonLogger.Msg($"BTR-70 LRF完成 | LaserOrigin={fcs.LaserOrigin.name} MaxRange={fcs.MaxLaserRange}");
                    }
                }            

                if (pt76_lrf.Value && name == "PT-76B")
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 PT-76B 测距改装");
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

                    if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                    {
                        MelonLogger.Msg($"=== PT-76B LRF改装 ===");
                        MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                    }

                    //ApplyLimitedLRF(fcs, day_optic, "PT", ref reticle_cached_pt76, pt76_gun, new Vector2(-278.2f, 289.4f));
                    ApplyRedDotLRF(fcs, day_optic, "PT", ref reticle_cached_pt76, pt76_gun);

                    if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                        MelonLogger.Msg($"PT-76B LRF完成 | LaserOrigin={fcs.LaserOrigin?.name} MaxRange={fcs.MaxLaserRange}");
                }

                if (stab_t64_nsvt.Value && name.StartsWith("T-64") && name != "T-64R")
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 T-64 NSVT稳定改装");
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

                if (t64_nsvt_optics.Value && name.StartsWith("T-64") && name != "T-64R")
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 T-64 NSVT瞄具改装");
                    CameraSlot cws_sight = vic.gameObject.transform.Find("---T64A_MESH---/HULL/TURRET/TC ring/TC AA sight/CWS gunsight")?.GetComponent<CameraSlot>();
                    if (cws_sight != null)
                        cws_sight.OtherFovs = new float[] { 25f, 12.5f, 6.25f };
                }


                if (t54a_lrf.Value && name == "T-54A")
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 T-54A 测距改装");
                    WeaponsManager wm = vic.GetComponent<WeaponsManager>();
                    FireControlSystem fcs = wm.Weapons[0].FCS;
                    var day_optic = vic.gameObject.transform.Find("T55A_skeleton/HULL/Turret/GUN/Gun Scripts/Sights (and FCS)/GPS")?.GetComponent<GHPC.Equipment.Optics.UsableOptic>();
                    var gun_node = vic.gameObject.transform.Find("T55A_skeleton/HULL/Turret/GUN");
                    //ApplyLimitedLRF(fcs, day_optic, "T55", ref reticle_cached_t54a, gun_node, new Vector2(-278.2f, 289.4f));
                    ApplyRedDotLRF(fcs, day_optic, "T55", ref reticle_cached_t54a, gun_node, forceLaseCompat: true);
                }

                if ((stab_t3485m.Value || t3485m_optics.Value || t3485m_lrf.Value) && name == "T-34-85M")
                {
                    UnderdogsDebug.LogTiming($"[UE]   > 匹配 T-34-85M 改装 (stab={stab_t3485m.Value} optics={t3485m_optics.Value} lrf={t3485m_lrf.Value})");
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
                        if (sight != null) { sight.DefaultFov = 7.5f; sight.OtherFovs = new float[] { 3.75f }; }
                    }

                    if (t3485m_lrf.Value)
                    {
                        var day_optic = vic.gameObject.transform.Find("T34_rig/T34/HULL/TURRET/MANTLET/Sights and FCS/GPS")?.GetComponent<GHPC.Equipment.Optics.UsableOptic>();
                        var gun_node = vic.gameObject.transform.Find("T34_rig/T34/HULL/TURRET/MANTLET");
                        //ApplyLimitedLRF(fcs, day_optic, "T34-85", ref reticle_cached_t3485m, gun_node, new Vector2(-88.2f, 314.4f));
                        ApplyRedDotLRF(fcs, day_optic, "T34-85", ref reticle_cached_t3485m, gun_node, forceLaseCompat: true);
                    }
                }

                    if (dumpA1A3)
                        UnderdogsDebug.DumpVehicleOpticsSnapshot(vic, "POST", detailedOptics: true);

                    } // end try
                    catch (System.Exception ex)
                    {
                        MelonLogger.Error($"[UE] 第{_uePass}轮 改装 [{name}] (ID={_vid}) 异常: {ex}");
                    }
                } // end foreach

                if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_TIMING)
                    MelonLogger.Msg($"[UE] === 第{_uePass}/{_uePassCount}轮改装完成 | 耗时={Time.realtimeSinceStartup - _passStart:F3}s ===");
            } // end for pass

            if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_TIMING)
                MelonLogger.Msg($"[UE] <<< OnSceneWasInitialized 结束 | 总耗时={Time.realtimeSinceStartup - _sceneStartTime:F3}s");
        }
    }
}
