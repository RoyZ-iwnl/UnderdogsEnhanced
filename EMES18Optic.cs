using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using MelonLoader.Utils;
using Reticle;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

namespace UnderdogsEnhanced
{
    public class EMES18Optic
    {
        // 保留调试字段，兼容 UnderdogsEnhanced.cs 的输出
        public static bool DebugForceHideSprites => false;
        public static bool DebugDisableDefaultScopeControl { get; set; } = true;
        private const float EMES_WFOV = 16.0f;
        private const float EMES_NFOV = 4.0f;
        private const float EMES_DAY_FOV = 4.0f;
        private const float EMES_FOV_TOLERANCE = 0.2f;
        private const float EMES_RANGE_MAX = 4000f;
        private const float EMES_BLACKBAR_ALPHA = 0.95f;

        private enum EmesChannelMode
        {
            Thermal,
            Day
        }

        private class EMES18ModeMarker : MonoBehaviour
        {
            public EmesChannelMode Mode;
        }

        private static readonly FieldInfo f_fcs_originalRangeLimits = typeof(FireControlSystem).GetField("_originalRangeLimits", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_originalRangeStep = typeof(FireControlSystem).GetField("_originalRangeStep", BindingFlags.Instance | BindingFlags.NonPublic);

        private static GameObject emes18_prefab;
        private static GameObject flir_post_green;
        private static Material flir_blit_material;
        private static bool default_scope_temporarily_hidden;
        private static AssetBundle emes18_bundle;

        private sealed class DefaultScopeSnapshot
        {
            public bool GameObjectActiveSelf;
            public readonly Dictionary<int, bool> SpriteRendererStates = new Dictionary<int, bool>();
            public readonly Dictionary<int, bool> PostMeshStates = new Dictionary<int, bool>();
            public readonly Dictionary<int, bool> CanvasStates = new Dictionary<int, bool>();
        }

        private static readonly Dictionary<int, DefaultScopeSnapshot> default_scope_snapshots = new Dictionary<int, DefaultScopeSnapshot>();

        private static readonly string[] hard_kill_renderer_paths = new string[]
        {
            "TURRET ARMOR/LEO1_AAR_turret/pzb 200",
            "TURRET ARMOR/LEO1_AAR_turret/pzb 200 mount",
            "LEO1A1_mesh/PZB 200"
        };
        private static readonly string[] hard_kill_canvas_paths = new string[]
        {
            "PZB-200 canvas"
        };

        private static bool PathMatchesConfiguredTargets(string fullPath, string[] configuredPaths)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || configuredPaths == null || configuredPaths.Length == 0) return false;

            string normalized = fullPath.Replace('\\', '/').Trim().ToLowerInvariant();
            foreach (var p in configuredPaths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string target = p.Replace('\\', '/').Trim().ToLowerInvariant();
                if (normalized == target) return true;
                if (normalized.EndsWith("/" + target, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static bool RendererUsesRenderTexture(Renderer renderer)
        {
            if (renderer == null) return false;

            Material[] mats;
            try { mats = renderer.sharedMaterials; } catch { return false; }
            if (mats == null) return false;

            foreach (var mat in mats)
            {
                if (mat == null) continue;
                Texture main = null;
                try { main = mat.mainTexture; } catch { }
                if (main is RenderTexture) return true;

                Texture t = null;
                try { t = mat.GetTexture("_MainTex"); } catch { }
                if (t is RenderTexture) return true;

                // 某些显示器材质把 RT 放在自定义纹理槽
                string[] props = null;
                try { props = mat.GetTexturePropertyNames(); } catch { }
                if (props == null) continue;
                foreach (var pn in props)
                {
                    Texture tx = null;
                    try { tx = mat.GetTexture(pn); } catch { }
                    if (tx is RenderTexture) return true;
                }
            }

            return false;
        }

        private static int DisableRenderTexturePanelsNearOptic(UsableOptic optic)
        {
            if (optic == null) return 0;
            int disabled = 0;

            var vehicle = optic.GetComponentInParent<GHPC.Vehicle.Vehicle>();
            Transform searchRoot = vehicle != null ? vehicle.transform : optic.transform.root;
            Vector3 origin = optic.transform.position;

            foreach (var renderer in searchRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!renderer.enabled) continue;

                // 不动本体 reticle 与我们自己的 UI
                if (renderer.transform.IsChildOf(optic.transform))
                {
                    string childNameLower = renderer.name.ToLowerInvariant();
                    if (childNameLower.Contains("reticle") || childNameLower.Contains("emes18")) continue;
                }

                float dist = Vector3.Distance(origin, renderer.bounds.center);
                if (dist > 3.5f) continue;

                bool rtHit = RendererUsesRenderTexture(renderer);
                if (!rtHit) continue;

                renderer.enabled = false;
                disabled++;
            }

            return disabled;
        }
        
        private static void NormalizeScopeSprite(CameraSlot slot, string tag)
        {
            if (EMES18DebugState.DisableNormalizeScopeSprite) return;
            if (slot == null) return;
            try
            {
                slot.SpriteType = CameraSpriteManager.SpriteType.DefaultScope;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to set SpriteType ({tag}): {ex.Message}");
            }
        }

        private static bool SetDefaultScopeSpriteRendered(bool enabled)
        {
            try
            {
                var cm = CameraManager.Instance;
                if (cm == null) return false;

                var f_spriteMgr = typeof(CameraManager).GetField("_spriteManager", BindingFlags.Instance | BindingFlags.NonPublic);
                var spriteMgr = f_spriteMgr?.GetValue(cm);
                if (spriteMgr == null) return false;

                var f_sprites = spriteMgr.GetType().GetField("Sprites", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var sprites = f_sprites?.GetValue(spriteMgr) as System.Array;
                if (sprites == null) return false;

                var dataType = spriteMgr.GetType().GetNestedType("CameraSpriteData", BindingFlags.Public | BindingFlags.NonPublic);
                var f_type = dataType?.GetField("Type", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var f_obj = dataType?.GetField("SpriteObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f_type == null || f_obj == null) return false;

                bool changed = false;
                foreach (var data in sprites)
                {
                    if (data == null) continue;
                    string st = f_type.GetValue(data)?.ToString();
                    if (!string.Equals(st, CameraSpriteManager.SpriteType.DefaultScope.ToString(), StringComparison.OrdinalIgnoreCase))
                        continue;

                    var go = f_obj.GetValue(data) as GameObject;
                    if (go == null) continue;

                    int scopeId = go.GetInstanceID();
                    var spriteRenderers = go.GetComponentsInChildren<SpriteRenderer>(true);
                    var postMeshes = go.GetComponentsInChildren<PostMeshComp>(true);
                    var canvases = go.GetComponentsInParent<Canvas>(true);

                    if (!enabled)
                    {
                        if (!default_scope_snapshots.ContainsKey(scopeId))
                        {
                            var snapshot = new DefaultScopeSnapshot();
                            snapshot.GameObjectActiveSelf = go.activeSelf;

                            foreach (var spriteRenderer in spriteRenderers)
                                if (spriteRenderer != null) snapshot.SpriteRendererStates[spriteRenderer.GetInstanceID()] = spriteRenderer.enabled;

                            foreach (var postMesh in postMeshes)
                                if (postMesh != null) snapshot.PostMeshStates[postMesh.GetInstanceID()] = postMesh.enabled;

                            foreach (var canvas in canvases)
                                if (canvas != null) snapshot.CanvasStates[canvas.GetInstanceID()] = canvas.enabled;

                            default_scope_snapshots[scopeId] = snapshot;
                        }

                        foreach (var spriteRenderer in spriteRenderers)
                        {
                            if (spriteRenderer == null || !spriteRenderer.enabled) continue;
                            spriteRenderer.enabled = false;
                            changed = true;
                        }

                        foreach (var postMesh in postMeshes)
                        {
                            if (postMesh == null || !postMesh.enabled) continue;
                            postMesh.enabled = false;
                            changed = true;
                        }

                        foreach (var canvas in canvases)
                        {
                            if (canvas == null || !canvas.enabled) continue;
                            canvas.enabled = false;
                            changed = true;
                        }

                        continue;
                    }

                    if (!default_scope_snapshots.TryGetValue(scopeId, out var snapshotToRestore))
                        continue;

                    if (snapshotToRestore.GameObjectActiveSelf && !go.activeSelf)
                    {
                        go.SetActive(true);
                        changed = true;
                    }

                    foreach (var spriteRenderer in spriteRenderers)
                    {
                        if (spriteRenderer == null) continue;
                        if (!snapshotToRestore.SpriteRendererStates.TryGetValue(spriteRenderer.GetInstanceID(), out var spriteEnabled)) continue;
                        if (spriteRenderer.enabled == spriteEnabled) continue;
                        spriteRenderer.enabled = spriteEnabled;
                        changed = true;
                    }

                    foreach (var postMesh in postMeshes)
                    {
                        if (postMesh == null) continue;
                        if (!snapshotToRestore.PostMeshStates.TryGetValue(postMesh.GetInstanceID(), out var postEnabled)) continue;
                        if (postMesh.enabled == postEnabled) continue;
                        postMesh.enabled = postEnabled;
                        changed = true;
                    }

                    foreach (var canvas in canvases)
                    {
                        if (canvas == null) continue;
                        if (!snapshotToRestore.CanvasStates.TryGetValue(canvas.GetInstanceID(), out var canvasEnabled)) continue;
                        if (canvas.enabled == canvasEnabled) continue;
                        canvas.enabled = canvasEnabled;
                        changed = true;
                    }

                    if (!snapshotToRestore.GameObjectActiveSelf && go.activeSelf)
                    {
                        go.SetActive(false);
                        changed = true;
                    }

                    default_scope_snapshots.Remove(scopeId);
                }

                return changed;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to set DefaultScope sprite render ({enabled}): {ex.Message}");
                return false;
            }
        }
        private static bool? QueryDefaultScopeDesiredByActiveView()
        {
            try
            {
                var activeSlot = CameraSlot.ActiveInstance;
                if (activeSlot == null)
                    return default_scope_temporarily_hidden ? (bool?)true : null;

                if (activeSlot.IsExterior)
                {
                    if (default_scope_temporarily_hidden)
                    {
                        default_scope_temporarily_hidden = false;
                        return true;
                    }

                    return null;
                }

                var activeOptic = activeSlot.GetComponentInParent<UsableOptic>();
                var marker = activeOptic != null ? activeOptic.GetComponents<EMES18ModeMarker>().FirstOrDefault(m => m != null) : null;
                bool shouldHideScope = !EMES18DebugState.DisableThermalDefaultScopeHide && marker != null && marker.Mode == EmesChannelMode.Thermal;
                if (shouldHideScope)
                {
                    default_scope_temporarily_hidden = true;
                    return false;
                }

                if (!EMES18DebugState.DisableDayDefaultScopeShow && marker != null && marker.Mode == EmesChannelMode.Day)
                {
                    default_scope_temporarily_hidden = false;
                    return true;
                }

                if (default_scope_temporarily_hidden)
                {
                    default_scope_temporarily_hidden = false;
                    return true;
                }

                return null;
            }
            catch
            {
                return default_scope_temporarily_hidden ? (bool?)true : null;
            }
        }

        public static void TickGlobalDefaultScopeState()
        {
            if (DebugDisableDefaultScopeControl)
            {
                default_scope_temporarily_hidden = false;
                SetDefaultScopeSpriteRendered(true);
                return;
            }

            try
            {
                bool? desired = QueryDefaultScopeDesiredByActiveView();
                if (desired.HasValue)
                    SetDefaultScopeSpriteRendered(desired.Value);
            }
            catch { }
        }

        private static bool IsA1A3B171Optic(UsableOptic optic)
        {
            if (optic == null || !string.Equals(optic.name, "B 171", StringComparison.OrdinalIgnoreCase))
                return false;

            var vehicle = optic.GetComponentInParent<GHPC.Vehicle.Vehicle>();
            string vehicleName = vehicle?.FriendlyName ?? vehicle?.name ?? string.Empty;
            return string.Equals(vehicleName, "Leopard A1A3", StringComparison.OrdinalIgnoreCase);
        }
        private static bool SuppressStockDayReticle(UsableOptic optic)
        {
            if (optic == null) return false;
            bool changed = false;

            string BuildKillPath(Transform node)
            {
                if (node == null) return "null";
                string pathValue = node.name;
                while (node.parent != null && node.parent != optic.transform)
                {
                    node = node.parent;
                    pathValue = node.name + "/" + pathValue;
                }
                return pathValue;
            }

            bool ShouldSuppressReticleMesh(ReticleMesh reticleMesh)
            {
                if (reticleMesh == null || reticleMesh.transform == null) return false;

                var cameraRoot = optic.transform.Find("camera");
                if (cameraRoot != null && reticleMesh.transform.IsChildOf(cameraRoot)) return false;

                string pathValue = BuildKillPath(reticleMesh.transform);
                if (pathValue.StartsWith("camera/", StringComparison.OrdinalIgnoreCase)) return false;

                return true;
            }

            try
            {
                foreach (var reticleMesh in optic.GetComponentsInChildren<ReticleMesh>(true))
                {
                    if (!ShouldSuppressReticleMesh(reticleMesh)) continue;

                    if (reticleMesh.gameObject.activeSelf)
                    {
                        reticleMesh.gameObject.SetActive(false);
                        changed = true;
                    }
                    if (reticleMesh.enabled)
                    {
                        reticleMesh.enabled = false;
                        changed = true;
                    }

                    var skinnedMeshRenderer = reticleMesh.GetComponent<SkinnedMeshRenderer>();
                    if (skinnedMeshRenderer != null && skinnedMeshRenderer.enabled)
                    {
                        skinnedMeshRenderer.enabled = false;
                        changed = true;
                    }

                    foreach (var postMesh in reticleMesh.GetComponentsInChildren<PostMeshComp>(true))
                    {
                        if (postMesh == null || !postMesh.enabled) continue;
                        postMesh.enabled = false;
                        changed = true;
                    }
                }

                if (optic.reticleMesh != null)
                {
                    optic.reticleMesh = null;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to suppress stock day reticle: {ex.Message}");
            }

            return changed;
        }
        private static void SetBlackbarOpacity(Transform root, float alpha)
        {
            if (root == null) return;

            var blackbar = root.Find("UI/BLACKBAR");
            var image = blackbar != null ? blackbar.GetComponent<Image>() : null;
            if (image == null) return;

            var c = image.color;
            if (!Mathf.Approximately(c.a, alpha))
            {
                c.a = alpha;
                image.color = c;
            }
        }

        private static void EnsureDayOverlayVisuals(Transform root)
        {
            if (root == null) return;

            var day = root.Find("DAYOPTIC");
            if (day != null && !day.gameObject.activeSelf) day.gameObject.SetActive(true);

            var dayCh = root.Find("DAYOPTIC/DAYOPCH");
            if (dayCh != null)
            {
                if (!dayCh.gameObject.activeSelf) dayCh.gameObject.SetActive(true);
                var sr = dayCh.GetComponent<SpriteRenderer>();
                if (sr != null && !sr.enabled) sr.enabled = true;
            }

            var ui = root.Find("UI");
            if (ui != null && !ui.gameObject.activeSelf) ui.gameObject.SetActive(true);

            SetBlackbarOpacity(root, EMES_BLACKBAR_ALPHA);
        }

        private static bool IsDistanceScaleNode(Transform t)
        {
            if (t == null) return false;

            string n = (t.name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("distance scale") || n.Contains("range scale") || n.Contains("rangescale"))
                return true;

            try
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    string cn = c.GetType().Name;
                    if (cn.IndexOf("RangeScaleScroll", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static bool IsDayRangefinderNode(Transform t)
        {
            if (t == null) return false;

            string n = (t.name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("stereo rangefinder")) return true;
            if (n.Contains("rangefinder mark vis parent")) return true;
            if (n.Contains("rangefinder mark")) return true;
            return false;
        }

        private static bool DisableNodeVisualsRecursive(Transform root)
        {
            if (root == null) return false;
            bool changed = false;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;

                if (t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                    changed = true;
                }

                var cv = t.GetComponent<Canvas>();
                if (cv != null && cv.enabled)
                {
                    cv.enabled = false;
                    changed = true;
                }

                var rd = t.GetComponent<Renderer>();
                if (rd != null && rd.enabled)
                {
                    rd.enabled = false;
                    changed = true;
                }
            }

            return changed;
        }

        private static int HideDayDistanceScaleVisuals(UsableOptic optic)
        {
            if (optic == null) return 0;
            int changed = 0;

            foreach (var t in optic.GetComponentsInChildren<Transform>(true))
            {
                if (!IsDistanceScaleNode(t)) continue;
                if (DisableNodeVisualsRecursive(t)) changed++;
            }

            return changed;
        }

        private static int HideDayRangefinderVisuals(UsableOptic optic)
        {
            if (optic == null) return 0;
            int changed = 0;

            foreach (var t in optic.GetComponentsInChildren<Transform>(true))
            {
                if (!IsDayRangefinderNode(t)) continue;
                if (DisableNodeVisualsRecursive(t)) changed++;
            }

            return changed;
        }

        private static int HideDayStockVisuals(UsableOptic optic)
        {
            int a = HideDayDistanceScaleVisuals(optic);
            int b = HideDayRangefinderVisuals(optic);
            return a + b;
        }

        private static void ConfigureEmesZoom(CameraSlot slot)
        {
            if (slot == null) return;
            try
            {
                slot.DefaultFov = EMES_WFOV;
                slot.OtherFovs = new float[] { EMES_NFOV };
                slot.ForceUpdateFov();
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to configure zoom levels: {ex.Message}");
            }
        }

        private static void ConfigureDayZoom(CameraSlot slot)
        {
            if (slot == null) return;
            try
            {
                slot.DefaultFov = EMES_DAY_FOV;
                slot.OtherFovs = new float[0];
                slot.ForceUpdateFov();
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to configure day zoom level: {ex.Message}");
            }
        }

        private static void EnsureLinkedSightAvailability(CameraSlot slot)
        {
            if (slot == null) return;

            try
            {
                if (!slot.enabled) slot.enabled = true;
                if (slot.NightSightAtNightOnly) slot.NightSightAtNightOnly = false;

                var linkedNight = slot.LinkedNightSight;
                if (linkedNight != null)
                {
                    if (!linkedNight.enabled) linkedNight.enabled = true;
                    if (linkedNight.NightSightAtNightOnly) linkedNight.NightSightAtNightOnly = false;
                    if (!linkedNight.IsLinkedNightSight) linkedNight.IsLinkedNightSight = true;
                    if (linkedNight.LinkedDaySight != slot) linkedNight.LinkedDaySight = slot;
                    try { linkedNight.RefreshAvailability(); } catch { }
                }

                var linkedDay = slot.LinkedDaySight;
                if (linkedDay != null)
                {
                    if (!linkedDay.enabled) linkedDay.enabled = true;
                    if (linkedDay.NightSightAtNightOnly) linkedDay.NightSightAtNightOnly = false;
                    if (linkedDay.LinkedNightSight != slot) linkedDay.LinkedNightSight = slot;
                    try { linkedDay.RefreshAvailability(); } catch { }
                }

                try { slot.RefreshAvailability(); } catch { }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to enable linked sight availability: {ex.Message}");
            }
        }

        private static bool IsWfovSelected(CameraSlot slot)
        {
            if (slot == null) return true;

            try
            {
                // FovIndex getter 通常可用：0=DefaultFov, 1=OtherFovs[0]
                return slot.FovIndex <= 0;
            }
            catch { }

            try
            {
                float fov = slot.CurrentFov;
                if (Mathf.Abs(fov - EMES_NFOV) <= EMES_FOV_TOLERANCE) return false;
                if (Mathf.Abs(fov - EMES_WFOV) <= EMES_FOV_TOLERANCE) return true;
                return fov > (EMES_NFOV + EMES_WFOV) * 0.5f;
            }
            catch
            {
                return true;
            }
        }

        private static void ConfigurePILLikeOpticBehavior(UsableOptic optic)
        {
            if (optic == null) return;
            try
            {
                // PIL 同思路：把热像视轴固定在炮轴，不跟随弹道修正漂移
                optic.Alignment = OpticAlignment.BoresightStabilized;
                optic.RotateAzimuth = true;
                optic.CantCorrect = true;
                optic.CantCorrectMaxSpeed = 5f;
                optic.ForceHorizontalReticleAlign = false;
                // 关闭超距压零，避免夜视状态下看起来被卡在较低距离上限
                optic.ZeroOutInvalidRange = false;

                if (optic.slot != null)
                {
                    optic.slot.VibrationShakeMultiplier = 0f;
                    optic.slot.VibrationBlurScale = 0f;
                    optic.slot.fovAspect = false;
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to configure PIL-like optic behavior: {ex.Message}");
            }
        }

        private static void EnforceFcsRangeCapability(FireControlSystem fcs)
        {
            if (fcs == null) return;

            try
            {
                if (fcs.MaxLaserRange < EMES_RANGE_MAX)
                    fcs.MaxLaserRange = EMES_RANGE_MAX;

                if (fcs.LaserAim != LaserAimMode.ImpactPoint)
                    fcs.LaserAim = LaserAimMode.ImpactPoint;

                var limits = fcs.RegisteredRangeLimits;
                float minRange = float.IsNaN(limits.x) ? 0f : Mathf.Clamp(limits.x, 0f, 500f);
                float maxRange = float.IsNaN(limits.y) ? EMES_RANGE_MAX : Mathf.Max(limits.y, EMES_RANGE_MAX);
                if (!Mathf.Approximately(limits.x, minRange) || !Mathf.Approximately(limits.y, maxRange))
                    fcs.RegisteredRangeLimits = new Vector2(minRange, maxRange);

                if (f_fcs_originalRangeLimits != null)
                {
                    try
                    {
                        var original = (Vector2)f_fcs_originalRangeLimits.GetValue(fcs);
                        if (original.y < EMES_RANGE_MAX)
                            f_fcs_originalRangeLimits.SetValue(fcs, new Vector2(Mathf.Min(original.x, minRange), EMES_RANGE_MAX));
                    }
                    catch { }
                }

                if (fcs.RangeStep <= 0) fcs.RangeStep = 50;
                if (f_fcs_originalRangeStep != null)
                {
                    try
                    {
                        int originalStep = (int)f_fcs_originalRangeStep.GetValue(fcs);
                        if (originalStep <= 0) f_fcs_originalRangeStep.SetValue(fcs, fcs.RangeStep);
                    }
                    catch { }
                }

                if (fcs.DefaultRange > EMES_RANGE_MAX) fcs.DefaultRange = EMES_RANGE_MAX;
                if (fcs.TargetRange > EMES_RANGE_MAX) fcs.TargetRange = EMES_RANGE_MAX;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to enforce range capability: {ex.Message}");
            }
        }

        private static void EnsureEmesRangeCapability(UsableOptic optic)
        {
            if (optic == null) return;
            EnforceFcsRangeCapability(optic.FCS);
        }

        private static void ApplyLeopardSuperFcs(FireControlSystem fcs)
        {
            if (fcs == null) return;

            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                bool SetMember(string name, object value)
                {
                    try
                    {
                        var property = typeof(FireControlSystem).GetProperty(name, flags);
                        if (property != null && property.CanWrite)
                        {
                            if (value == null || property.PropertyType.IsInstanceOfType(value))
                            {
                                property.SetValue(fcs, value, null);
                                return true;
                            }
                        }

                        var field = typeof(FireControlSystem).GetField(name, flags);
                        if (field != null)
                        {
                            if (value == null || field.FieldType.IsInstanceOfType(value))
                            {
                                field.SetValue(fcs, value);
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLoader.MelonLogger.Warning($"[EMES18] superFCS set member failed: {name}={value ?? "null"} | {ex.Message}");
                    }
                    return false;
                }

                bool SetBoolMember(string name, bool value) => SetMember(name, value);

                SetBoolMember("SuperleadWeapon", true);
                SetBoolMember("SuperelevateWeapon", true);
                SetBoolMember("RecordTraverseRateBuffer", true);
                SetMember("TraverseBufferSeconds", 0.5f);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Leopard super FCS apply failed: {ex.Message}");
            }
        }

        private static void ConfigureLeopardLaserRangefinder(FireControlSystem fcs, UsableOptic dayOptic, string vehicleName)
        {
            if (fcs == null) return;

            try
            {
                if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                {
                    MelonLoader.MelonLogger.Msg($"=== {vehicleName} 激光测距改装 ===");
                    MelonLoader.MelonLogger.Msg($"原测距仪: {(fcs.OpticalRangefinder != null ? "存在" : "不存在")}");
                }

                if (fcs.OpticalRangefinder != null)
                    UnityEngine.Object.Destroy(fcs.OpticalRangefinder);

                if (fcs.LaserOrigin == null)
                {
                    var lase = new GameObject("ue_lase");
                    lase.transform.SetParent(dayOptic != null ? dayOptic.transform : fcs.transform, false);
                    lase.transform.localPosition = new Vector3(0f, 0f, 0.2f);
                    lase.transform.localRotation = Quaternion.identity;
                    fcs.LaserOrigin = lase.transform;
                }

                fcs.LaserAim = LaserAimMode.ImpactPoint;
                fcs.MaxLaserRange = EMES_RANGE_MAX;

                if (UnderdogsDebug.DEBUG_MODE && UnderdogsDebug.DEBUG_LRF)
                    MelonLoader.MelonLogger.Msg($"激光测距已启用 | LaserOrigin={fcs.LaserOrigin?.name ?? "null"} MaxRange={fcs.MaxLaserRange}m");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Leopard laser rangefinder setup failed ({vehicleName}): {ex.Message}");
            }
        }

        private sealed class A1A3DedicatedThermalMarker : MonoBehaviour
        {
            public UsableOptic StockOptic;
            public UsableOptic DayOptic;
        }

        private static void DestroyComponentIfPresent<T>(GameObject go) where T : Component
        {
            if (go == null) return;
            var comp = go.GetComponent<T>();
            if (comp != null) UnityEngine.Object.Destroy(comp);
        }

        private static void CleanupEmesArtifacts(UsableOptic optic)
        {
            if (optic == null) return;

            try
            {
                RemoveModeMarkers(optic);

                var suppressor = optic.GetComponent<PzbDisplaySuppressor>();
                if (suppressor != null) UnityEngine.Object.Destroy(suppressor);

                foreach (var monitor in optic.GetComponentsInChildren<EMES18Monitor>(true))
                {
                    if (monitor != null) UnityEngine.Object.Destroy(monitor.gameObject);
                }

                foreach (Transform child in optic.transform)
                {
                    if (child == null) continue;
                    string childName = child.name ?? string.Empty;
                    if (childName.StartsWith("EMES18", StringComparison.OrdinalIgnoreCase) ||
                        childName.StartsWith("FLIR Post Processing - Green", StringComparison.OrdinalIgnoreCase))
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to cleanup previous artifacts: {ex.Message}");
            }
        }

        private static void RewireA1A3DedicatedPair(UsableOptic gpsDayOptic, UsableOptic thermalOptic)
        {
            if (gpsDayOptic == null || gpsDayOptic.slot == null || thermalOptic == null || thermalOptic.slot == null) return;

            try
            {
                gpsDayOptic.enabled = true;
                thermalOptic.enabled = true;
                if (!gpsDayOptic.gameObject.activeSelf) gpsDayOptic.gameObject.SetActive(true);
                if (!thermalOptic.gameObject.activeSelf) thermalOptic.gameObject.SetActive(true);

                gpsDayOptic.slot.enabled = true;
                thermalOptic.slot.enabled = true;
                gpsDayOptic.slot.LinkedNightSight = thermalOptic.slot;
                gpsDayOptic.slot.NightSightAtNightOnly = false;
                thermalOptic.slot.LinkedDaySight = gpsDayOptic.slot;
                thermalOptic.slot.IsLinkedNightSight = true;
                thermalOptic.slot.NightSightAtNightOnly = false;

                NormalizeScopeSprite(gpsDayOptic.slot, "a1a3-day");
                NormalizeScopeSprite(thermalOptic.slot, "a1a3-thermal");
                EnsureLinkedSightAvailability(gpsDayOptic.slot);
                EnsureLinkedSightAvailability(thermalOptic.slot);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to rewire A1A3 dedicated pair: {ex.Message}");
            }
        }

        private static UsableOptic GetOrCreateA1A3DedicatedThermalOptic(UsableOptic gpsDayOptic, UsableOptic stockNightOptic)
        {
            if (gpsDayOptic == null) return stockNightOptic;
            if (stockNightOptic == null) return null;

            var passedMarker = stockNightOptic.GetComponent<A1A3DedicatedThermalMarker>();
            if (passedMarker != null)
            {
                passedMarker.DayOptic = gpsDayOptic;
                RewireA1A3DedicatedPair(gpsDayOptic, stockNightOptic);
                return stockNightOptic;
            }

            try
            {
                var vehicle = gpsDayOptic.GetComponentInParent<GHPC.Vehicle.Vehicle>() ?? stockNightOptic.GetComponentInParent<GHPC.Vehicle.Vehicle>();
                if (vehicle != null)
                {
                    var existing = vehicle.GetComponentsInChildren<A1A3DedicatedThermalMarker>(true)
                        .FirstOrDefault(m => m != null && (m.DayOptic == gpsDayOptic || m.StockOptic == stockNightOptic));
                    if (existing != null)
                    {
                        var existingOptic = existing.GetComponent<UsableOptic>();
                        if (existingOptic != null)
                        {
                            existing.DayOptic = gpsDayOptic;
                            existing.StockOptic = stockNightOptic;
                            RewireA1A3DedicatedPair(gpsDayOptic, existingOptic);
                            return existingOptic;
                        }
                    }
                }

                var dedicatedObject = GameObject.Instantiate(stockNightOptic.gameObject, stockNightOptic.transform.parent);
                dedicatedObject.name = stockNightOptic.gameObject.name;
                if (!dedicatedObject.activeSelf) dedicatedObject.SetActive(true);

                var dedicatedOptic = dedicatedObject.GetComponent<UsableOptic>();
                if (dedicatedOptic == null || dedicatedOptic.slot == null)
                {
                    UnityEngine.Object.Destroy(dedicatedObject);
                    return stockNightOptic;
                }

                CleanupEmesArtifacts(dedicatedOptic);
                DestroyComponentIfPresent<PzbDisplaySuppressor>(dedicatedObject);

                var marker = dedicatedObject.GetComponent<A1A3DedicatedThermalMarker>();
                if (marker == null) marker = dedicatedObject.AddComponent<A1A3DedicatedThermalMarker>();
                marker.StockOptic = stockNightOptic;
                marker.DayOptic = gpsDayOptic;

                RewireA1A3DedicatedPair(gpsDayOptic, dedicatedOptic);

                if (stockNightOptic.slot != null)
                {
                    stockNightOptic.slot.enabled = false;
                    stockNightOptic.slot.IsLinkedNightSight = false;
                    stockNightOptic.slot.LinkedDaySight = null;
                    try { stockNightOptic.slot.RefreshAvailability(); } catch { }
                }
                stockNightOptic.enabled = false;
                stockNightOptic.gameObject.SetActive(false);

                MelonLoader.MelonLogger.Msg("[EMES18][A1A3] Created dedicated B 171 takeover optic");
                return dedicatedOptic;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to create dedicated A1A3 thermal optic: {ex.Message}");
                return stockNightOptic;
            }
        }

        public static void ApplyA1A3DedicatedOptics(UsableOptic gpsDayOptic, UsableOptic stockNightOptic)
        {
            if (gpsDayOptic == null) return;

            LoadStaticAssets();

            var thermalOptic = GetOrCreateA1A3DedicatedThermalOptic(gpsDayOptic, stockNightOptic);
            if (thermalOptic == null || thermalOptic.slot == null)
            {
                MelonLoader.MelonLogger.Warning("[EMES18][A1A3] Dedicated thermal optic unavailable");
                return;
            }

            RewireA1A3DedicatedPair(gpsDayOptic, thermalOptic);
            ApplyToThermalOptic(thermalOptic);
            ApplyToDayOptic(gpsDayOptic);
        }

        public static void ApplyLeopardEmes18Suite(UsableOptic gpsDayOptic, UsableOptic stockNightOptic, FireControlSystem fcs, string vehicleName)
        {
            if (gpsDayOptic == null || stockNightOptic == null) return;

            LoadStaticAssets();
            var resolvedFcs = fcs ?? gpsDayOptic.FCS ?? stockNightOptic.FCS;
            ConfigureLeopardLaserRangefinder(resolvedFcs, gpsDayOptic, vehicleName);
            ApplyLeopardSuperFcs(resolvedFcs);

            if (string.Equals(vehicleName, "Leopard A1A3", StringComparison.OrdinalIgnoreCase))
            {
                ApplyA1A3DedicatedOptics(gpsDayOptic, stockNightOptic);
                return;
            }

            gpsDayOptic.enabled = true;
            stockNightOptic.enabled = true;
            ApplyToThermalOptic(stockNightOptic);
            ApplyToDayOptic(gpsDayOptic);
        }

        private static void ApplyMonitorPreset(Transform root)
        {
            if (root == null) return;

            ApplyPilCanvasPreset(root.GetComponent<Canvas>());

            void Apply(string path, Vector3 pos, Vector3 scale)
            {
                var t = root.Find(path);
                if (t == null) return;
                t.localPosition = pos;
                t.localScale = scale;
            }

            Apply("WFOV", new Vector3(0f, 0f, 0f), new Vector3(5f, 5f, 1f));
            Apply("WFOV/WFOVCH", new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
            Apply("NFOV", new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
            Apply("NFOV/NFOVCH", new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 1f));
            Apply("DAYOPTIC", new Vector3(0f, 0f, 0f), new Vector3(5f, 5f, 1f));
            Apply("DAYOPTIC/DAYOPCH", new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 1f));
            Apply("UI", new Vector3(0f, 324.14f, 0f), new Vector3(1f, 1f, 1f));
            Apply("UI/BLACKBAR", new Vector3(0f, 6.9f, 0f), new Vector3(8f, 5.76f, 1f));
            Apply("UI/AMMOCATE", new Vector3(88f, 0f, 0f), new Vector3(5f, 5f, 5f));
            Apply("UI/READYSTATE", new Vector3(-80f, 0f, 0f), new Vector3(5f, 5f, 5f));
            Apply("UI/RANGE", new Vector3(0f, 0f, 0f), new Vector3(5f, 5f, 5f));
            SetBlackbarOpacity(root, EMES_BLACKBAR_ALPHA);
        }

        private static void ApplyPilCanvasPreset(Canvas canvas)
        {
            if (canvas == null) return;

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = false;
            canvas.sortingOrder = 0;
            canvas.planeDistance = 1f;
            canvas.worldCamera = null;
        }

        private static void ForceCanvasRefresh(Canvas canvas)
        {
            if (canvas == null) return;

            ApplyPilCanvasPreset(canvas);

            bool wasEnabled = canvas.enabled;
            canvas.enabled = false;
            canvas.enabled = wasEnabled;

            var rect = canvas.transform as RectTransform;
            if (rect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            Canvas.ForceUpdateCanvases();
        }

        private static void ApplyCanvasLikeDebugApply(Canvas canvas)
        {
            if (canvas == null) return;

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = false;

            int sortValue = 0;
            float planeValue = 1f;
            canvas.sortingOrder = sortValue;
            canvas.planeDistance = planeValue;
            canvas.worldCamera = null;

            Canvas.ForceUpdateCanvases();
        }

        private static void ApplyMonitorChannelMode(Transform root, EmesChannelMode mode)
        {
            if (root == null) return;

            var wfov = root.Find("WFOV");
            var nfov = root.Find("NFOV");
            var day = root.Find("DAYOPTIC");

            bool dayMode = mode == EmesChannelMode.Day;

            if (wfov != null) wfov.gameObject.SetActive(!dayMode);
            if (nfov != null) nfov.gameObject.SetActive(false);
            if (day != null) day.gameObject.SetActive(dayMode);

            var dayCh = root.Find("DAYOPTIC/DAYOPCH");
            if (dayCh != null) dayCh.gameObject.SetActive(dayMode);
            if (dayMode) EnsureDayOverlayVisuals(root);
        }

        private static void RemoveModeMarkers(UsableOptic optic)
        {
            if (optic == null) return;
            foreach (var marker in optic.GetComponents<EMES18ModeMarker>())
            {
                if (marker != null) UnityEngine.Object.Destroy(marker);
            }
        }

        private static bool HasModeMarker(UsableOptic optic, EmesChannelMode mode)
        {
            if (optic == null) return false;
            try
            {
                return optic.GetComponents<EMES18ModeMarker>().Any(m => m != null && m.Mode == mode);
            }
            catch
            {
                return false;
            }
        }

        private static void AddModeMarker(UsableOptic optic, EmesChannelMode mode)
        {
            if (optic == null || HasModeMarker(optic, mode)) return;
            var marker = optic.gameObject.AddComponent<EMES18ModeMarker>();
            marker.Mode = mode;
        }

        private static int HardKillLoggedPzbArtifacts(UsableOptic optic)
        {
            if (optic == null) return 0;
            var vehicle = optic.GetComponentInParent<GHPC.Vehicle.Vehicle>();
            Transform root = vehicle != null ? vehicle.transform : optic.transform.root;
            if (root == null) return 0;

            int killed = 0;

            foreach (var path in hard_kill_renderer_paths)
            {
                var t = root.Find(path);
                if (t == null) continue;

                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !r.enabled) continue;
                    r.enabled = false;
                    killed++;
                }
            }

            foreach (var path in hard_kill_canvas_paths)
            {
                var t = root.Find(path);
                if (t == null) continue;
                var c = t.GetComponent<Canvas>();
                if (c != null && c.enabled)
                {
                    c.enabled = false;
                    killed++;
                }
            }

            return killed;
        }

        public class PzbDisplaySuppressor : MonoBehaviour
        {
            public UsableOptic optic;

            private class RendererState
            {
                public Renderer renderer;
                public bool originalEnabled;
                public string path;
                public string materialInfo;
                public bool rtHit;
                public bool pathHit;
                public float distance;
                public Vector3 size;
            }

            private class CanvasState
            {
                public Canvas canvas;
                public bool originalEnabled;
                public string path;
                public bool pathHit;
                public float distance;
            }

            private readonly Dictionary<int, RendererState> _renderers = new Dictionary<int, RendererState>();
            private readonly Dictionary<int, CanvasState> _canvases = new Dictionary<int, CanvasState>();
            private float _nextRescanTime;
            private bool _lastSuppressed;

            private static string BuildPath(Transform t, Transform root)
            {
                if (t == null) return "null";
                string path = t.name;
                while (t.parent != null && t.parent != root)
                {
                    t = t.parent;
                    path = t.name + "/" + path;
                }
                return path;
            }

            private static string GetMaterialInfo(Renderer renderer)
            {
                if (renderer == null) return "none";
                try
                {
                    var mats = renderer.sharedMaterials;
                    if (mats == null || mats.Length == 0) return "none";
                    return string.Join(", ", mats.Where(m => m != null).Select(m =>
                    {
                        string texType = "null";
                        try { texType = m.mainTexture != null ? m.mainTexture.GetType().Name : "null"; } catch { }
                        return $"{m.name}|{m.shader?.name}|main={texType}";
                    }).ToArray());
                }
                catch { return "unknown"; }
            }

            private bool IsActivePzbView()
            {
                if (optic == null || optic.slot == null) return false;
                var active = CameraSlot.ActiveInstance;
                if (active == null || active != optic.slot || active.IsExterior) return false;

                Camera cam = CameraManager.MainCam;
                if (cam == null || cam.transform.parent == null) return false;
                Transform p = cam.transform.parent;
                return p == optic.transform || p.IsChildOf(optic.transform);
            }

            public int DiscoverTargets()
            {
                if (optic == null) return 0;
                int added = 0;

                var vehicle = optic.GetComponentInParent<GHPC.Vehicle.Vehicle>();
                Transform searchRoot = vehicle != null ? vehicle.transform : optic.transform.root;
                Vector3 origin = optic.transform.position;

                foreach (var r in searchRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    if (r.transform.IsChildOf(optic.transform))
                    {
                        string cn = r.name.ToLowerInvariant();
                        if (cn.Contains("reticle") || cn.Contains("emes18")) continue;
                    }

                    string path = BuildPath(r.transform, searchRoot);
                    bool pathHit = PathMatchesConfiguredTargets(path, hard_kill_renderer_paths);
                    bool rtHit = RendererUsesRenderTexture(r);
                    float dist = Vector3.Distance(origin, r.bounds.center);
                    if (!pathHit)
                    {
                        if (dist > 4.5f) continue;
                        if (!rtHit) continue;
                    }

                    int id = r.GetInstanceID();
                    if (_renderers.ContainsKey(id)) continue;
                    _renderers[id] = new RendererState
                    {
                        renderer = r,
                        originalEnabled = r.enabled,
                        path = path,
                        materialInfo = GetMaterialInfo(r),
                        rtHit = rtHit,
                        pathHit = pathHit,
                        distance = dist,
                        size = r.bounds.size
                    };
                    MelonLoader.MelonLogger.Msg(
                        $"[EMES18][target+][Renderer] path={_renderers[id].path} dist={dist:F2} size={r.bounds.size} pathHit={pathHit} rtHit={rtHit} mats={_renderers[id].materialInfo}");
                    added++;
                }

                foreach (var c in searchRoot.GetComponentsInChildren<Canvas>(true))
                {
                    if (c == null) continue;
                    if (c.transform.IsChildOf(optic.transform))
                    {
                        string cn = c.name.ToLowerInvariant();
                        if (cn.Contains("emes18")) continue;
                    }

                    string path = BuildPath(c.transform, searchRoot);
                    bool pathHit = PathMatchesConfiguredTargets(path, hard_kill_canvas_paths);
                    if (!pathHit) continue;
                    float dist = Vector3.Distance(origin, c.transform.position);

                    int id = c.GetInstanceID();
                    if (_canvases.ContainsKey(id)) continue;
                    _canvases[id] = new CanvasState
                    {
                        canvas = c,
                        originalEnabled = c.enabled,
                        path = path,
                        pathHit = pathHit,
                        distance = dist
                    };
                    MelonLoader.MelonLogger.Msg(
                        $"[EMES18][target+][Canvas] path={_canvases[id].path} dist={dist:F2} pathHit={pathHit} enabled={c.enabled}");
                    added++;
                }

                return added;
            }

            private void ApplySuppression(bool suppress)
            {
                foreach (var kv in _renderers.ToArray())
                {
                    var s = kv.Value;
                    if (s.renderer == null) { _renderers.Remove(kv.Key); continue; }
                    bool target = suppress ? false : s.originalEnabled;
                    if (s.renderer.enabled != target) s.renderer.enabled = target;
                }

                foreach (var kv in _canvases.ToArray())
                {
                    var s = kv.Value;
                    if (s.canvas == null) { _canvases.Remove(kv.Key); continue; }
                    bool target = suppress ? false : s.originalEnabled;
                    if (s.canvas.enabled != target) s.canvas.enabled = target;
                }
            }

            void LateUpdate()
            {
                bool active = IsActivePzbView();

                if (Time.time >= _nextRescanTime)
                {
                    DiscoverTargets();
                    _nextRescanTime = Time.time + 1f;
                }

                if (active || _lastSuppressed) ApplySuppression(active);
                _lastSuppressed = active;
            }

            void OnDisable()
            {
                ApplySuppression(false);
                _lastSuppressed = false;
            }

            void OnDestroy()
            {
                ApplySuppression(false);
                _lastSuppressed = false;
            }
        }

        public class EMES18Monitor : MonoBehaviour
        {
            public bool ForceDayChannel;

            private Transform wfov;
            private Transform nfov;
            private Transform dayoptic;
            private TextMeshProUGUI range_text;
            private TextMeshProUGUI ready_text;
            private TextMeshProUGUI ammo_text;
            private FireControlSystem fcs;
            private CameraSlot camera_slot;
            private Canvas root_canvas;
            private WeaponsManager weapons_manager;
            private WeaponSystem coax_weapon_system;
            private AmmoType current_ammo;
            private bool has_current_ammo;
            private bool firing_blocked;
            private float next_range_enforce_time;
            private float next_visual_enforce_time;
            private UsableOptic self_optic;
            private bool last_active_view;
            private int canvas_refresh_frames;

            void Awake()
            {
                wfov = transform.Find("WFOV");
                nfov = transform.Find("NFOV");
                dayoptic = transform.Find("DAYOPTIC");
                range_text = transform.Find("UI/RANGE").GetComponent<TextMeshProUGUI>();
                ready_text = transform.Find("UI/READYSTATE").GetComponent<TextMeshProUGUI>();
                ammo_text = transform.Find("UI/AMMOCATE").GetComponent<TextMeshProUGUI>();
                root_canvas = GetComponent<Canvas>();
                ApplyPilCanvasPreset(root_canvas);
                canvas_refresh_frames = 3;

                UsableOptic optic = GetComponentInParent<UsableOptic>();
                self_optic = optic;
                fcs = optic.FCS;
                camera_slot = optic.slot;
                weapons_manager = optic.GetComponentInParent<WeaponsManager>();
                ResolveCoaxWeaponSystem();

                if (camera_slot != null)
                    camera_slot.ZoomChanged += OnZoomChanged;
                if (fcs != null)
                {
                    fcs.AmmoTypeChanged += OnAmmoTypeChanged;
                    fcs.FiringBlockedChanged += OnFiringBlockedChanged;
                    firing_blocked = fcs.FiringBlocked;
                    current_ammo = fcs.CurrentAmmoType;
                    has_current_ammo = current_ammo != null;
                    EnforceFcsRangeCapability(fcs);
                }

                OnZoomChanged();
                UpdateAmmoText();
                UpdateReadyText();
            }

            private void ResolveCoaxWeaponSystem()
            {
                coax_weapon_system = null;
                if (weapons_manager == null || weapons_manager.Weapons == null) return;

                if (weapons_manager.Weapons.Length > 1)
                {
                    var info = weapons_manager.Weapons[1];
                    if (info != null && info.Weapon != null)
                    {
                        coax_weapon_system = info.Weapon;
                        return;
                    }
                }

                foreach (var info in weapons_manager.Weapons)
                {
                    if (info == null || info.Weapon == null) continue;

                    string n1 = (info.Name ?? string.Empty).ToLowerInvariant();
                    string n2 = (info.Weapon.MetaName ?? info.Weapon.name ?? string.Empty).ToLowerInvariant();
                    if (n1.Contains("coax") || n1.Contains("machine") || n1.Contains("mg") ||
                        n2.Contains("coax") || n2.Contains("machine") || n2.Contains("mg"))
                    {
                        coax_weapon_system = info.Weapon;
                        return;
                    }
                }
            }

            private void OnZoomChanged()
            {
                if (camera_slot == null) return;

                if (ForceDayChannel)
                {
                    bool hasDay = dayoptic != null;
                    if (wfov != null) wfov.gameObject.SetActive(!hasDay);
                    if (nfov != null) nfov.gameObject.SetActive(false);
                    if (dayoptic != null) dayoptic.gameObject.SetActive(true);
                    var dayCh = transform.Find("DAYOPTIC/DAYOPCH");
                    if (dayCh != null) dayCh.gameObject.SetActive(hasDay);
                    EnsureDayOverlayVisuals(transform);
                    return;
                }

                bool is_wfov = IsWfovSelected(camera_slot);
                if (wfov != null) wfov.gameObject.SetActive(is_wfov);
                if (nfov != null) nfov.gameObject.SetActive(!is_wfov);
                if (dayoptic != null) dayoptic.gameObject.SetActive(false);
            }

            public void RefreshModeVisuals()
            {
                OnZoomChanged();
            }

            private void OnAmmoTypeChanged(AmmoType ammo)
            {
                current_ammo = ammo;
                has_current_ammo = true;
                UpdateAmmoText();
            }

            private void OnFiringBlockedChanged(bool blocked)
            {
                firing_blocked = blocked;
                UpdateReadyText();
            }

            private bool IsCoaxSelected()
            {
                if (fcs == null) return false;

                try
                {
                    var activeWeapon = fcs.CurrentWeaponSystem;
                    if (activeWeapon == null) return false;

                    if (coax_weapon_system == null) ResolveCoaxWeaponSystem();
                    if (coax_weapon_system != null && activeWeapon == coax_weapon_system) return true;

                    string n = (activeWeapon.MetaName ?? activeWeapon.name ?? string.Empty).ToLowerInvariant();
                    return n.Contains("coax") || n.Contains("machine") || n.Contains("mg");
                }
                catch
                {
                    return false;
                }
            }

            private void UpdateAmmoText()
            {
                if (ammo_text == null) return;

                if (IsCoaxSelected())
                {
                    ammo_text.text = "D";
                    return;
                }

                AmmoType sourceAmmo = null;
                try { sourceAmmo = fcs?.CurrentWeaponSystem?.CurrentAmmoType; } catch { }
                if (sourceAmmo == null && has_current_ammo) sourceAmmo = current_ammo;

                string category = "D";
                if (sourceAmmo != null)
                {
                    if (sourceAmmo.Category == AmmoType.AmmoCategory.Penetrator) category = "A";
                    else if (sourceAmmo.Category == AmmoType.AmmoCategory.ShapedCharge) category = "B";
                    else if (sourceAmmo.Category == AmmoType.AmmoCategory.Explosive) category = "C";
                }

                ammo_text.text = category;
            }

            private void UpdateReadyText()
            {
                if (ready_text == null) return;

                // UI 始终显示文本，由我们按游戏可发射状态切字
                if (!ready_text.gameObject.activeSelf) ready_text.gameObject.SetActive(true);
                if (!ready_text.enabled) ready_text.enabled = true;
                var col = ready_text.color;
                if (col.a < 0.99f)
                {
                    col.a = 1f;
                    ready_text.color = col;
                }

                bool weaponReady = false;
                try
                {
                    var ws = fcs?.CurrentWeaponSystem;
                    if (ws != null)
                    {
                        var st = ws.WeaponStatus;
                        weaponReady = st.WeaponCanFire && st.IsLoaded && st.IsCycled;
                    }
                }
                catch { }

                bool ready = !firing_blocked && weaponReady;
                ready_text.text = ready ? "F" : "0";
            }

            void LateUpdate()
            {
                bool? defaultScopeDesired = QueryDefaultScopeDesiredByActiveView();
                if (defaultScopeDesired.HasValue)
                    SetDefaultScopeSpriteRendered(defaultScopeDesired.Value);

                bool activeView = camera_slot != null && CameraSlot.ActiveInstance == camera_slot && !camera_slot.IsExterior;
                if (activeView && !last_active_view)
                    canvas_refresh_frames = 3;

                if (root_canvas != null)
                {
                    bool wrongPreset = root_canvas.renderMode != RenderMode.ScreenSpaceOverlay ||
                        root_canvas.overrideSorting ||
                        root_canvas.sortingOrder != 0 ||
                        Mathf.Abs(root_canvas.planeDistance - 1f) > 0.01f ||
                        root_canvas.worldCamera != null;

                    if ((activeView && canvas_refresh_frames > 0) || wrongPreset)
                    {
                        ForceCanvasRefresh(root_canvas);
                        if (activeView && canvas_refresh_frames > 0) canvas_refresh_frames--;
                    }
                }

                if (root_canvas != null && root_canvas.enabled != activeView)
                    root_canvas.enabled = activeView;

                last_active_view = activeView;

                if (!activeView || fcs == null) return;
                if (Time.time >= next_range_enforce_time)
                {
                    EnforceFcsRangeCapability(fcs);
                    next_range_enforce_time = Time.time + 0.25f;
                }
                if (Time.time >= next_visual_enforce_time)
                {
                    if (ForceDayChannel)
                    {
                        SuppressStockDayReticle(self_optic);
                        EnsureDayOverlayVisuals(transform);
                        HideDayStockVisuals(self_optic);
                    }
                    else
                    {
                        SuppressStockDayReticle(self_optic);
                    }
                    next_visual_enforce_time = Time.time + 0.25f;
                }

                firing_blocked = fcs.FiringBlocked;
                if (!has_current_ammo)
                {
                    current_ammo = fcs.CurrentAmmoType;
                    has_current_ammo = current_ammo != null;
                }

                if (range_text != null)
                    range_text.text = ((int)(fcs.CurrentRange / 10f)).ToString("000");
                UpdateAmmoText();
                UpdateReadyText();
            }

            void OnGUI()
            {
                if (Event.current == null || Event.current.type != EventType.Repaint) return;
                if (root_canvas == null || camera_slot == null) return;
                if (CameraSlot.ActiveInstance != camera_slot || camera_slot.IsExterior) return;

                ApplyCanvasLikeDebugApply(root_canvas);
            }

            void OnDestroy()
            {
                try
                {
                    if (camera_slot != null)
                        camera_slot.ZoomChanged -= OnZoomChanged;
                    if (fcs != null)
                    {
                        fcs.AmmoTypeChanged -= OnAmmoTypeChanged;
                        fcs.FiringBlockedChanged -= OnFiringBlockedChanged;
                    }
                }
                catch { }
            }
        }

        private static AssetBundle FindLoadedEmesBundle()
        {
            try
            {
                foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (bundle == null) continue;
                    string n = null;
                    try { n = bundle.name; } catch { }
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    n = n.Replace('\\', '/').ToLowerInvariant();
                    if (n.EndsWith("/emes18", StringComparison.Ordinal) || n.EndsWith("/emes18.unity3d", StringComparison.Ordinal) || n.Contains("/ue/emes18"))
                        return bundle;
                }
            }
            catch { }

            return null;
        }

        private static void RefreshFlirReferences()
        {
            var m60a3 = Resources.FindObjectsOfTypeAll<GHPC.Vehicle.Vehicle>()
                .FirstOrDefault(v => v.name == "M60A3 TTS");
            if (m60a3 == null) return;

            var flirPost = m60a3.transform.Find("Turret Scripts/Sights/FLIR/FLIR Post Processing - Green");
            if (flirPost != null) flir_post_green = flirPost.gameObject;

            var flirSlot = m60a3.transform.Find("Turret Scripts/Sights/FLIR")?.GetComponent<CameraSlot>();
            if (flirSlot != null) flir_blit_material = flirSlot.FLIRBlitMaterialOverride;
        }

        private static void InstallReadyStateProxy(UsableOptic optic, Transform monitorRoot)
        {
            if (optic == null || monitorRoot == null) return;

            Transform proxy = monitorRoot.Find("__UE_READY_PROXY__");
            if (proxy == null)
            {
                var go = new GameObject("__UE_READY_PROXY__");
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetParent(monitorRoot, false);
                proxy = go.transform;
            }

            if (!proxy.gameObject.activeSelf) proxy.gameObject.SetActive(true);
            optic.ReadyToFireObject = proxy.gameObject;
        }

        public static void LoadStaticAssets()
        {
            string bundle_path = Path.Combine(MelonEnvironment.ModsDirectory, "UE/emes18");

            if (emes18_prefab != null)
            {
                RefreshFlirReferences();
                MelonLoader.MelonLogger.Msg($"[EMES18] Static assets already loaded (FLIR={flir_post_green != null})");
                return;
            }

            MelonLoader.MelonLogger.Msg($"[EMES18] Loading bundle from: {bundle_path}");

            if (emes18_bundle == null)
                emes18_bundle = FindLoadedEmesBundle();

            if (emes18_bundle == null)
                emes18_bundle = AssetBundle.LoadFromFile(bundle_path);

            if (emes18_bundle == null)
            {
                bool exists = File.Exists(bundle_path);
                MelonLoader.MelonLogger.Error($"[EMES18] Failed to load bundle (exists={exists})");
                return;
            }

            emes18_prefab = emes18_bundle.LoadAsset<GameObject>("EMES18");
            if (emes18_prefab == null)
            {
                MelonLoader.MelonLogger.Error("[EMES18] Failed to load EMES18 prefab from bundle");
                return;
            }

            emes18_prefab.hideFlags = HideFlags.DontUnloadUnusedAsset;
            RefreshFlirReferences();
            MelonLoader.MelonLogger.Msg($"[EMES18] Static assets loaded (FLIR={flir_post_green != null})");
        }

        public static void ApplyToOptic(UsableOptic optic)
        {
            ApplyToThermalOptic(optic);
        }

        public static void ApplyToThermalOptic(UsableOptic optic)
        {
            ApplyToOpticInternal(optic, EmesChannelMode.Thermal);
        }

        public static void ApplyToDayOptic(UsableOptic optic)
        {
            ApplyToOpticInternal(optic, EmesChannelMode.Day);
        }

        private static void ApplyToOpticInternal(UsableOptic optic, EmesChannelMode mode)
        {
            if (emes18_prefab == null || optic == null || optic.slot == null) return;

            bool isThermal = mode == EmesChannelMode.Thermal;

            EnsureLinkedSightAvailability(optic.slot);

            // 重入时只刷新关键配置，避免重复挂载 UI
            if (HasModeMarker(optic, mode))
            {
                EnsureEmesRangeCapability(optic);
                ConfigurePILLikeOpticBehavior(optic);
                if (isThermal)
                {
                    ConfigureEmesZoom(optic.slot);
                    SuppressStockDayReticle(optic);
                }
                else
                {
                    ConfigureDayZoom(optic.slot);
                    SuppressStockDayReticle(optic);
                    HideDayStockVisuals(optic);
                    var existingMonitor = optic.GetComponentInChildren<EMES18Monitor>(true);
                    if (existingMonitor != null) EnsureDayOverlayVisuals(existingMonitor.transform);
                }
                return;
            }

            if (isThermal)
            {
                UsableOptic linkedNightOptic = null;
                try { linkedNightOptic = optic.slot?.LinkedNightSight?.PairedOptic; } catch { }

                var pzbReticle = optic.transform.Find("pzb reticle");
                if (pzbReticle != null) pzbReticle.gameObject.SetActive(false);

                SuppressStockDayReticle(optic);

                NormalizeScopeSprite(optic.slot, "main");
                if (linkedNightOptic != null && linkedNightOptic != optic)
                    NormalizeScopeSprite(linkedNightOptic.slot, "linked-night");

                if (!IsA1A3B171Optic(optic))
                {
                    int hiddenByRT = DisableRenderTexturePanelsNearOptic(optic);
                    int hardKilled = HardKillLoggedPzbArtifacts(optic);
                    if (hiddenByRT > 0)
                        MelonLoader.MelonLogger.Msg($"[EMES18] Disabled stock render-texture visuals: {hiddenByRT}");
                    if (hardKilled > 0)
                        MelonLoader.MelonLogger.Msg($"[EMES18] Hard-killed logged PZB artifacts: {hardKilled}");

                    var suppressor = optic.GetComponent<PzbDisplaySuppressor>();
                    if (suppressor == null) suppressor = optic.gameObject.AddComponent<PzbDisplaySuppressor>();
                    suppressor.optic = optic;
                    int runtimeTargets = suppressor.DiscoverTargets();
                    if (runtimeTargets > 0)
                        MelonLoader.MelonLogger.Msg($"[EMES18] Runtime suppressor targets: {runtimeTargets}");
                }
            }
            else
            {
                SuppressStockDayReticle(optic);
                int hiddenDayScale = HideDayStockVisuals(optic);
                if (hiddenDayScale > 0)
                    MelonLoader.MelonLogger.Msg($"[EMES18] Hidden day stock visuals: {hiddenDayScale}");
            }

            if (isThermal)
            {
                if (optic.reticleMesh != null)
                {
                    try { optic.reticleMesh.Clear(); } catch { }
                    optic.reticleMesh = null;
                }
                optic.post = null;
            }
            else
            {
                SuppressStockDayReticle(optic);
            }

            if (isThermal)
            {
                optic.slot.VisionType = NightVisionType.Thermal;
                optic.slot.BaseBlur = 0f;
                optic.slot.VibrationBlurScale = 0.05f;
                optic.slot.VibrationShakeMultiplier = 0.01f;
                optic.slot.VibrationPreBlur = true;
                optic.slot.OverrideFLIRResolution = true;
                optic.slot.FLIRWidth = 1024;
                optic.slot.FLIRHeight = 576;
                ConfigureEmesZoom(optic.slot);
                optic.slot.CanToggleFlirPolarity = true;
                optic.slot.FLIRFilterMode = FilterMode.Point;
                if (flir_blit_material != null) optic.slot.FLIRBlitMaterialOverride = flir_blit_material;
            }
            else
            {
                optic.slot.VisionType = NightVisionType.None;
                optic.slot.BaseBlur = 0f;
                optic.slot.OverrideFLIRResolution = false;
                optic.slot.CanToggleFlirPolarity = false;
                ConfigureDayZoom(optic.slot);
            }

            EnsureEmesRangeCapability(optic);
            ConfigurePILLikeOpticBehavior(optic);
            if (!isThermal)
            {
                SuppressStockDayReticle(optic);
                HideDayStockVisuals(optic);
            }

            if (isThermal && flir_post_green != null)
            {
                GameObject post = GameObject.Instantiate(flir_post_green, optic.transform);
                post.SetActive(true);
            }

            GameObject monitor = GameObject.Instantiate(emes18_prefab, optic.transform);
            ApplyMonitorPreset(monitor.transform);
            ApplyMonitorChannelMode(monitor.transform, mode);
            var monitorComp = monitor.AddComponent<EMES18Monitor>();
            monitorComp.ForceDayChannel = !isThermal;
            monitorComp.RefreshModeVisuals();
            monitor.SetActive(true);
            if (!isThermal) EnsureDayOverlayVisuals(monitor.transform);
            InstallReadyStateProxy(optic, monitor.transform);
            AddModeMarker(optic, mode);

            MelonLoader.MelonLogger.Msg($"[EMES18] Applied successfully ({mode})");
        }
    }
}
