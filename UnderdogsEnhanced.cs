using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using MelonLoader;
using UnderdogsEnhanced;
using GHPC.Vehicle;
using GHPC;
using System.Reflection;
using GHPC.Weapons;
using GHPC.Camera;
using TMPro;
using Reticle;

[assembly: MelonInfo(typeof(UnderdogsEnhancedMod), "Underdogs Enhanced", "1.2.0", "RoyZ;Based on ATLAS work")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace UnderdogsEnhanced
{
    public class UnderdogsEnhancedMod : MelonMod
    {
        public static readonly bool DEBUG_MODE = true;
        public static readonly bool DEBUG_ARMOR = false;    // 装甲数据调试子开关，受 DEBUG_MODE 控制
        public static readonly bool DEBUG_CHILDREN = false; // 子节点结构调试子开关，受 DEBUG_MODE 控制

        public static MelonPreferences_Category cfg;
        public static MelonPreferences_Entry<bool> stab_bmp;
        public static MelonPreferences_Entry<bool> stab_konkurs;
        public static MelonPreferences_Entry<bool> stab_marder;
        public static MelonPreferences_Entry<bool> stab_marder_milan;
        public static MelonPreferences_Entry<bool> stab_brdm;
        public static MelonPreferences_Entry<bool> marder_rangefinder;
        public static MelonPreferences_Entry<bool> leopard_laser;
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

        private static GameObject range_readout;
        private static object reticle_cached_bmp = null;
        private static object reticle_cached_brdm = null;
        private static object reticle_cached_btr70 = null;
        private static object reticle_cached_pt76 = null;
        private static object reticle_cached_t54a = null;
        private static object reticle_cached_t3485m = null;

        private string[] invalid_scenes = new string[] { "MainMenu2_Scene", "LOADER_MENU", "LOADER_INITIAL", "t64_menu" };

        private static string GetPath(Transform t, Transform root)
        {
            string path = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        private static void PrintChildren(Transform t, int depth = 0)
        {
            string indent = new string(' ', depth * 2);
            MelonLogger.Msg($"{indent}{t.name}");
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
            stab_marder_milan.Description = "Stabilizes MILAN launcher on Marder A1+ (default: enabled)";
            marder_rangefinder = cfg.CreateEntry("Marder Rangefinder", true);
            marder_rangefinder.Description = "Gives Marder series laser rangefinder and parallax fix (default: enabled)";
            leopard_laser = cfg.CreateEntry("Leopard 1 Laser", true);
            leopard_laser.Description = "Replace optical rangefinder with laser on Leopard 1 series (default: enabled)";
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

        private static void ApplyRedDotLRF(FireControlSystem fcs, GHPC.Equipment.Optics.UsableOptic dayOptic, string cacheKey, ref object reticle_cached_ref, Transform laserParent = null)
        {
            if (fcs.LaserOrigin == null)
            {
                GameObject lase = new GameObject("lase");
                lase.transform.SetParent(laserParent ?? fcs.transform, false);
                fcs.LaserOrigin = lase.transform;
            }

            fcs.LaserAim = LaserAimMode.Fixed;
            fcs.MaxLaserRange = 4000f;

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
            if (fcs.gameObject.GetComponent<LimitedLRF>() != null) return;
            if (fcs.LaserOrigin == null)
            {
                GameObject lase = new GameObject("lase");
                lase.transform.SetParent(laserParent ?? fcs.transform, false);
                fcs.LaserOrigin = lase.transform;
            }

            fcs.LaserAim = LaserAimMode.Fixed;
            fcs.MaxLaserRange = 4000f;
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

        public override async void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (invalid_scenes.Contains(sceneName)) return;

            Vehicle[] all_vehicles;
            int prevCount = -1, stableFor = 0;
            do {
                await Task.Delay(500);
                all_vehicles = Object.FindObjectsOfType<Vehicle>();
                int groundCount = all_vehicles.Count(v => v != null && v.gameObject.tag == "Vehicle");
                if (groundCount > 0 && groundCount == prevCount) stableFor++;
                else { stableFor = 0; prevCount = groundCount; }
            } while (stableFor < 10);

            if (DEBUG_MODE)
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
                        MelonLogger.Msg($"  CameraSlot: {GetPath(cs.transform, v.transform)} | DefaultFov={cs.DefaultFov} OtherFovs=[{string.Join(", ", cs.OtherFovs ?? new float[0])}]");
                    foreach (var uo in v.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true))
                    {
                        MelonLogger.Msg($"  UsableOptic: {GetPath(uo.transform, v.transform)}");
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
                            MelonLogger.Msg($"  [武器{wi}] {wsi.Name} | FCS: {GetPath(fcs.transform, v.transform)}");
                            MelonLogger.Msg($"    LaserOrigin: {(fcs.LaserOrigin != null ? GetPath(fcs.LaserOrigin, v.transform) : "null")}");
                            MelonLogger.Msg($"    LaserAim: {fcs.LaserAim} MaxLaserRange: {fcs.MaxLaserRange} DefaultRange: {fcs.DefaultRange}");
                            MelonLogger.Msg($"    StabsActive={fcs.StabsActive} CurrentStabMode={fcs.CurrentStabMode} SuperelevateWeapon: {fcs.SuperelevateWeapon} SuperleadWeapon: {fcs.SuperleadWeapon}");
                        }
                    }
                }

                if (DEBUG_CHILDREN)
                {
                    MelonLogger.Msg($"=== 子节点结构 ===");
                    foreach (Vehicle v in all_vehicles)
                    {
                        MelonLogger.Msg($"[{v.FriendlyName}] 子节点结构:");
                        for (int ci = 0; ci < v.transform.childCount; ci++)
                            PrintChildren(v.transform.GetChild(ci), 1);
                    }
                }

                if (DEBUG_ARMOR)
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

            foreach (Vehicle vic in all_vehicles)
            {
                if (vic == null) continue;

                string name = vic.FriendlyName;

                if ((stab_bmp.Value || bmp_lrf.Value) && (name == "BMP-1" || name == "BMP-1P"))
                {
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

                        if (DEBUG_MODE)
                        {
                            MelonLogger.Msg($"=== {name} LRF改装 ===");
                            MelonLogger.Msg($"FCS path={GetPath(fcs.transform, vic.transform)}");
                            MelonLogger.Msg($"LaserOrigin: {(fcs?.LaserOrigin != null ? GetPath(fcs.LaserOrigin, vic.transform) : "null，将自动创建")}");
                            MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                            if (day_optic?.reticleMesh?.reticleSO != null)
                                MelonLogger.Msg($"reticle planes[0] element count={day_optic.reticleMesh.reticleSO.planes[0].elements.Count}");
                        }

                        var gun = vic.gameObject.transform.Find("BMP1_rig/HULL/TURRET/GUN");
                        ApplyLimitedLRF(fcs, day_optic, "BMP-1", ref reticle_cached_bmp, gun, new Vector2(46.8f, 469.4f));

                        if (DEBUG_MODE)
                            MelonLogger.Msg($"{name} LRF完成 | LaserOrigin={fcs.LaserOrigin.name} MaxRange={fcs.MaxLaserRange}");
                    }
                }

                if (stab_marder.Value && (name == "Marder 1A2" || name == "Marder A1-" || name == "Marder A1+"))
                {
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

                    if (stab_marder_milan.Value && name == "Marder A1+")
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

                if (leopard_laser.Value && (name == "Leopard 1A3" || name == "Leopard 1A3A1" || name == "Leopard 1A3A2" ||
                    name == "Leopard 1A3A3" || name == "Leopard A1A1" || name == "Leopard A1A2" ||
                    name == "Leopard A1A3" || name == "Leopard A1A4"))
                {
                    WeaponsManager weapons_manager = vic.GetComponent<WeaponsManager>();
                    WeaponSystemInfo main_gun_info = weapons_manager.Weapons[0];
                    FireControlSystem fcs = main_gun_info.FCS;

                    if (DEBUG_MODE)
                    {
                        MelonLogger.Msg($"=== {name} 激光测距改装 ===");
                        MelonLogger.Msg($"原测距仪: {(fcs.OpticalRangefinder != null ? "存在" : "不存在")}");
                    }

                    if (fcs.OpticalRangefinder != null)
                    {
                        GameObject.Destroy(fcs.OpticalRangefinder);
                    }

                    fcs.LaserAim = LaserAimMode.ImpactPoint;
                    fcs.MaxLaserRange = 4000f;

                    if (DEBUG_MODE)
                    {
                        MelonLogger.Msg($"激光测距已启用 | 最大距离: {fcs.MaxLaserRange}m");
                    }
                }

                if ((stab_brdm.Value || brdm_lrf.Value) && name == "BRDM-2")
                {
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

                        if (DEBUG_MODE)
                        {
                            MelonLogger.Msg($"=== BRDM-2 LRF改装 ===");
                            MelonLogger.Msg($"FCS: {fcs?.name ?? "null"}");
                            MelonLogger.Msg($"LaserOrigin: {(fcs?.LaserOrigin != null ? fcs.LaserOrigin.name : "null，将自动创建")}");
                            MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                        }

                        var brdm_gun = vic.gameObject.transform.Find("BRDM2_rig/HULL/TURRET/GUN");
                        ApplyLimitedLRF(fcs, day_optic, "BRDM2", ref reticle_cached_brdm, brdm_gun, new Vector2(31.8f, 319.4f));

                        if (DEBUG_MODE)
                            MelonLogger.Msg($"BRDM-2 LRF完成 | LaserOrigin={fcs.LaserOrigin.name} MaxRange={fcs.MaxLaserRange}");
                    }
                }

                if ((stab_btr70.Value || btr70_lrf.Value) && name == "BTR-70")
                {
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

                        if (DEBUG_MODE)
                        {
                            MelonLogger.Msg($"=== BTR-70 LRF改装 ===");
                            MelonLogger.Msg($"FCS: {fcs?.name ?? "null"}");
                            MelonLogger.Msg($"LaserOrigin: {(fcs?.LaserOrigin != null ? fcs.LaserOrigin.name : "null，将自动创建")}");
                            MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                        }

                        var btr70_gun = vic.gameObject.transform.Find("BTR70_rig/HULL/TURRET/GUN/Gun Aimable");
                        ApplyLimitedLRF(fcs, day_optic, "BRDM2", ref reticle_cached_btr70, btr70_gun, new Vector2(31.8f, 319.4f));

                        if (DEBUG_MODE)
                            MelonLogger.Msg($"BTR-70 LRF完成 | LaserOrigin={fcs.LaserOrigin.name} MaxRange={fcs.MaxLaserRange}");
                    }
                }            

                if (pt76_lrf.Value && name == "PT-76B")
                {
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

                    if (DEBUG_MODE)
                    {
                        MelonLogger.Msg($"=== PT-76B LRF改装 ===");
                        MelonLogger.Msg($"UsableOptic: {day_optic?.name ?? "null"}");
                    }

                    //ApplyLimitedLRF(fcs, day_optic, "PT", ref reticle_cached_pt76, pt76_gun, new Vector2(-278.2f, 289.4f));
                    ApplyRedDotLRF(fcs, day_optic, "PT", ref reticle_cached_pt76, pt76_gun);

                    if (DEBUG_MODE)
                        MelonLogger.Msg($"PT-76B LRF完成 | LaserOrigin={fcs.LaserOrigin?.name} MaxRange={fcs.MaxLaserRange}");
                }

                if (stab_t64_nsvt.Value && name.StartsWith("T-64") && name != "T-64R")
                {
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
                    CameraSlot cws_sight = vic.gameObject.transform.Find("---T64A_MESH---/HULL/TURRET/TC ring/TC AA sight/CWS gunsight")?.GetComponent<CameraSlot>();
                    if (cws_sight != null)
                        cws_sight.OtherFovs = new float[] { 25f, 12.5f, 6.25f };
                }


                if (t54a_lrf.Value && name == "T-54A")
                {
                    WeaponsManager wm = vic.GetComponent<WeaponsManager>();
                    FireControlSystem fcs = wm.Weapons[0].FCS;
                    var day_optic = vic.gameObject.transform.Find("T55A_skeleton/HULL/Turret/GUN/Gun Scripts/Sights (and FCS)/GPS")?.GetComponent<GHPC.Equipment.Optics.UsableOptic>();
                    var gun_node = vic.gameObject.transform.Find("T55A_skeleton/HULL/Turret/GUN");
                    //ApplyLimitedLRF(fcs, day_optic, "T55", ref reticle_cached_t54a, gun_node, new Vector2(-278.2f, 289.4f));
                    ApplyRedDotLRF(fcs, day_optic, "T55", ref reticle_cached_t54a, gun_node);
                }

                if ((stab_t3485m.Value || t3485m_optics.Value || t3485m_lrf.Value) && name == "T-34-85M")
                {
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
                        ApplyRedDotLRF(fcs, day_optic, "T34-85", ref reticle_cached_t3485m, gun_node);
                    }
                }
            }
        }
    }
}
