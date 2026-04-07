using GHPC.Utility;
using GHPC;
using GHPC.Weapons;
using GHPC.Equipment.Optics;
using Reticle;
using TMPro;
using UnityEngine;
using HarmonyLib;
using MelonLoader;
using System.Reflection;
using System;
using System.Collections.Generic;

namespace UnderdogsEnhanced
{
    public class ForceLaseCompat : MonoBehaviour { }

    public static class ForceLaseCompatUtil
    {
        private static readonly FieldInfo f_fcs_currentRange = typeof(GHPC.Weapons.FireControlSystem).GetField("_currentRange", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_targetRange = typeof(GHPC.Weapons.FireControlSystem).GetField("_targetRange", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_reportedRange = typeof(GHPC.Weapons.FireControlSystem).GetField("<ReportedRange>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_rangeMoving = typeof(GHPC.Weapons.FireControlSystem).GetField("_rangeMoving", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_rangeChanged = typeof(GHPC.Weapons.FireControlSystem).GetField("RangeChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo m_uo_fcsRangeChanged = typeof(GHPC.Equipment.Optics.UsableOptic).GetMethod("FCS_RangeChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static bool TryFindClosestHitIgnoringSelf(Vector3 origin, Vector3 direction, float maxRange, int mask, Transform ownRoot, out float distance, bool smokeOnly = false)
        {
            var hits = Physics.RaycastAll(
                origin,
                direction,
                maxRange,
                mask,
                QueryTriggerInteraction.Ignore);

            distance = -1f;
            if (hits == null || hits.Length == 0) return false;
            float best = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null) continue;

                var hitTransform = hit.collider.transform;
                if (hitTransform != null && hitTransform.IsChildOf(ownRoot)) continue;
                if (smokeOnly && hit.collider.tag != "Smoke") continue;
                if (hit.distance <= 0f) continue;
                if (hit.distance < best) best = hit.distance;
            }

            if (best == float.MaxValue) return false;
            distance = best;
            return true;
        }

        public static bool TryMeasureRangeIgnoringSelf(GHPC.Weapons.FireControlSystem fcs, out float measured)
        {
            measured = -1f;
            if (fcs?.LaserOrigin == null) return false;

            float maxRange = fcs.MaxLaserRange > 0f ? fcs.MaxLaserRange : 4000f;
            Transform ownRoot = fcs.transform.root;

            float smokeDistance = -1f;
            int smokeMask = 1 << CodeUtils.LAYER_INDEX_VISIBILITYONLY;
            TryFindClosestHitIgnoringSelf(
                fcs.LaserOrigin.position,
                fcs.LaserOrigin.forward,
                maxRange,
                smokeMask,
                ownRoot,
                out smokeDistance,
                smokeOnly: true);

            float laserDistance = -1f;
            int laserMask = ConstantsAndInfoManager.Instance != null
                ? ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask.value
                : Physics.DefaultRaycastLayers;
            TryFindClosestHitIgnoringSelf(
                fcs.LaserOrigin.position,
                fcs.LaserOrigin.forward,
                maxRange,
                laserMask,
                ownRoot,
                out laserDistance);

            if (smokeDistance > 0f) measured = smokeDistance;
            if (laserDistance > 0f && (measured < 0f || laserDistance < measured)) measured = laserDistance;
            return measured > 0f;
        }

        public static void PushRangeToFcsDisplay(GHPC.Weapons.FireControlSystem fcs, float preferredMeasured = -1f)
        {
            if (fcs == null) return;

            float measured = preferredMeasured;
            bool customHit = measured > 0f;
            if (!customHit)
                customHit = TryMeasureRangeIgnoringSelf(fcs, out measured);

            if (measured <= 0f)
                measured = fcs.ReportedRange;
            if (measured <= 0f) measured = fcs.TargetRange;
            if (measured <= 0f) return;

            measured = MathUtil.RoundFloatToMultipleOf(measured, 50);
            try { f_fcs_reportedRange?.SetValue(fcs, measured); } catch { }
            try { fcs.TargetRange = measured; } catch { }
            try { fcs.RangeInvalid = false; } catch { }

            try { fcs.SetManualMode(true); } catch { }
            try { fcs.SetRange(measured, true); } catch { }
            try { fcs.NotifyManualRangeAdjust(); } catch { }
            try { fcs.UpdateRange(); } catch { }

            float currentAfter = 0f;
            try { currentAfter = fcs.CurrentRange; } catch { }
            if (currentAfter <= 0f || Mathf.Abs(currentAfter - measured) > 1f)
            {
                try { f_fcs_currentRange?.SetValue(fcs, measured); } catch { }
                try { f_fcs_targetRange?.SetValue(fcs, measured); } catch { }
                try { f_fcs_rangeMoving?.SetValue(fcs, false); } catch { }
                try
                {
                    var cb = f_fcs_rangeChanged?.GetValue(fcs) as Action<float>;
                    cb?.Invoke(measured);
                }
                catch { }
            }

            try
            {
                var root = fcs.transform != null ? fcs.transform.root : null;
                var allOptics = root != null ? root.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true) : new GHPC.Equipment.Optics.UsableOptic[0];
                var seen = new HashSet<GHPC.Equipment.Optics.UsableOptic>();
                foreach (var optic in allOptics)
                {
                    if (optic == null || seen.Contains(optic)) continue;
                    seen.Add(optic);
                    if (optic.FCS != fcs) continue;

                    try { m_uo_fcsRangeChanged?.Invoke(optic, new object[] { measured }); } catch { }
                    try
                    {
                        if (optic.RangeText != null)
                        {
                            float shown = measured;
                            int div = optic.RangeTextDivideBy <= 0 ? 1 : optic.RangeTextDivideBy;
                            shown /= div;
                            int q = optic.RangeTextQuantize;
                            if (q > 0) shown = MathUtil.RoundFloatToMultipleOf(shown, q);
                            int shownInt = Mathf.Max(0, Mathf.RoundToInt(shown));
                            string body = shownInt.ToString("0000");
                            optic.RangeText.text = (optic.RangeTextPrefix ?? "") + body + (optic.RangeTextSuffix ?? "");
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    public class LimitedLRF : MonoBehaviour
    {
        public Transform canvas;
        private const float STEP = 5f;

        void Update()
        {
#if DEBUG
            if (canvas == null) return;
            var textObj = canvas.GetComponentInChildren<TextMeshProUGUI>();
            if (textObj == null) return;
            bool changed = false;
            var pos = textObj.rectTransform.anchoredPosition;
            if (Input.GetKeyDown(KeyCode.Keypad8)) { pos.y += STEP; changed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad2)) { pos.y -= STEP; changed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad4)) { pos.x -= STEP; changed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad6)) { pos.x += STEP; changed = true; }
            if (changed) { textObj.rectTransform.anchoredPosition = pos; MelonLoader.MelonLogger.Msg($"[LRF pos] anchoredPos={pos.x:F1},{pos.y:F1}"); }
#endif
        }
    }

    [HarmonyPatch(typeof(GHPC.Weapons.FireControlSystem), "DoLase")]
    public static class LimitedLase
    {
        private static readonly FieldInfo _laseQueued = typeof(GHPC.Weapons.FireControlSystem).GetField("_laseQueued", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool Prefix(GHPC.Weapons.FireControlSystem __instance)
        {
            if (!__instance.GetComponent<LimitedLRF>())
                return true;

            _laseQueued.SetValue(__instance, false);

            float num = -1f;
            int layerMask = 1 << CodeUtils.LAYER_INDEX_VISIBILITYONLY;

            RaycastHit raycastHit;
            if (Physics.Raycast(__instance.LaserOrigin.position, __instance.LaserOrigin.forward, out raycastHit, __instance.MaxLaserRange, layerMask) && raycastHit.collider.tag == "Smoke")
                num = raycastHit.distance;

            if (Physics.Raycast(__instance.LaserOrigin.position, __instance.LaserOrigin.forward, out raycastHit, __instance.MaxLaserRange, ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask.value) && (raycastHit.distance < num || num == -1f))
                num = raycastHit.distance;

            if (num != -1f)
            {
                __instance.GetComponent<LimitedLRF>().canvas.gameObject.GetComponentInChildren<TextMeshProUGUI>().text = ((int)MathUtil.RoundFloatToMultipleOf(num, 50)).ToString("0000");
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(GHPC.Weapons.FireControlSystem), "Lase")]
    public static class ForceLaseCompatFireControlPatch
    {
        private static bool Prefix(GHPC.Weapons.FireControlSystem __instance)
        {
            if (__instance == null || !__instance.GetComponent<ForceLaseCompat>())
                return true;

            if (__instance.LaserOrigin == null)
                return true;

            float measured = -1f;
            bool manualHit = false;
            try
            {
                manualHit = ForceLaseCompatUtil.TryMeasureRangeIgnoringSelf(__instance, out measured);
            }
            catch { }

            if (!manualHit)
            {
                try
                {
                    __instance.DoLase();
                }
                catch { }
            }

            ForceLaseCompatUtil.PushRangeToFcsDisplay(__instance, measured);
            return false;
        }
    }

    [HarmonyPatch(typeof(GHPC.Equipment.Optics.UsableOptic), "Guidance_TriggerDown")]
    public static class ForceLaseCompatOpticPatch
    {
        private static bool Prefix(GHPC.Equipment.Optics.UsableOptic __instance)
        {
            var fcs = __instance?.FCS;
            if (fcs == null || !fcs.GetComponent<ForceLaseCompat>())
                return true;

            if (fcs.LaserOrigin == null)
                return true;

            float measured = -1f;
            bool manualHit = false;
            try
            {
                manualHit = ForceLaseCompatUtil.TryMeasureRangeIgnoringSelf(fcs, out measured);
            }
            catch { }

            if (!manualHit)
            {
                try
                {
                    fcs.DoLase();
                }
                catch { }
            }

            ForceLaseCompatUtil.PushRangeToFcsDisplay(fcs, measured);
            return false;
        }
    }

    /// <summary>
    /// LRF 应用工具类，提供 ApplyLimitedLRF 和 ApplyRedDotLRF 方法
    /// </summary>
    public static class LRFApplicator
    {
        private static readonly AccessTools.FieldRef<UsableOptic, bool> f_uo_hasGuidance =
            AccessTools.FieldRefAccess<UsableOptic, bool>("_hasGuidance");
        private static readonly AccessTools.FieldRef<FireControlSystem, UsableOptic> f_fcs_mainOptic =
            AccessTools.FieldRefAccess<FireControlSystem, UsableOptic>("<MainOptic>k__BackingField");
        private static readonly AccessTools.FieldRef<FireControlSystem, UsableOptic> f_fcs_authoritativeOptic =
            AccessTools.FieldRefAccess<FireControlSystem, UsableOptic>("AuthoritativeOptic");

        private static GameObject GetOrCreateRangeReadout()
        {
            return UEResourceController.GetLimitedLrfRangeReadoutTemplate();
        }

        private static void EnsureLaseReadiness(FireControlSystem fcs, UsableOptic dayOptic, bool forceLaseCompat)
        {
            if (fcs == null || dayOptic == null) return;

            try { dayOptic.FCS = fcs; } catch { }
            if (!forceLaseCompat) return;

            var mainOptic = f_fcs_mainOptic(fcs);
            if (mainOptic == null)
            {
                try { f_fcs_mainOptic(fcs) = dayOptic; } catch { }
            }

            if (f_fcs_authoritativeOptic(fcs) == null)
            {
                try { f_fcs_authoritativeOptic(fcs) = dayOptic; } catch { }
            }

            try { f_uo_hasGuidance(dayOptic) = true; } catch { }
            try { dayOptic.GuidanceLight = true; } catch { }

            try { fcs.RegisterOptic(dayOptic); } catch { }
            try { fcs.NotifyActiveOpticChanged(dayOptic); } catch { }

            if (forceLaseCompat && fcs.GetComponent<ForceLaseCompat>() == null)
                fcs.gameObject.AddComponent<ForceLaseCompat>();
        }

        public static void ApplyRedDotLRF(FireControlSystem fcs, UsableOptic dayOptic, string cacheKey, ref object reticle_cached_ref, Transform laserParent = null, bool forceLaseCompat = false)
        {
            if (fcs == null || dayOptic == null || dayOptic.reticleMesh == null)
            {
#if DEBUG
                if (UnderdogsDebug.DEBUG_LRF)
                    MelonLogger.Warning($"[UE] RedDotLRF 跳过: FCS/Optic 未就绪 | FCS={(fcs != null)} Optic={(dayOptic != null)} Reticle={(dayOptic?.reticleMesh != null)}");
#endif
                return;
            }

            if (forceLaseCompat && dayOptic != null)
            {
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
                UECommonUtil.ShallowCopy(reticle_cached_ref, srcCached);
                f_tree.SetValue(reticle_cached_ref, reticleSO);

                reticleSO.lights = new List<ReticleTree.Light>() { new ReticleTree.Light(), new ReticleTree.Light() };
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

        public static void ApplyLimitedLRF(FireControlSystem fcs, UsableOptic dayOptic, string cacheKey, ref object reticle_cached_ref, Transform laserParent = null, Vector2 textPos = default)
        {
            if (fcs == null || dayOptic == null || dayOptic.reticleMesh == null) return;
            if (fcs.gameObject.GetComponent<LimitedLRF>() != null) return;
            if (fcs.LaserOrigin == null)
            {
                GameObject lase = new GameObject("lase");
                lase.transform.SetParent(laserParent ?? fcs.transform, false);
                fcs.LaserOrigin = lase.transform;
            }

            GameObject rangeReadoutTemplate = GetOrCreateRangeReadout();
            if (rangeReadoutTemplate == null)
            {
                MelonLogger.Warning($"[UE] LimitedLRF 跳过: 缺少量程显示模板 | cacheKey={cacheKey}");
                return;
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
                UECommonUtil.ShallowCopy(reticle_cached_ref, srcCached);
                f_tree.SetValue(reticle_cached_ref, reticleSO);

                reticleSO.lights = new List<ReticleTree.Light>() { new ReticleTree.Light(), new ReticleTree.Light() };
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

            GameObject canvas = GameObject.Instantiate(rangeReadoutTemplate);
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
    }
}
