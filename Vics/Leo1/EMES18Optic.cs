using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
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
        public static bool DebugDisableNormalizeScopeSprite { get; set; } = true;
        public static bool DebugDisableThermalDefaultScopeHide { get; set; } = true;
        public static bool DebugDisableDayDefaultScopeShow { get; set; } = true;
        public static float DebugDayCircleRadiusMrad { get; set; } = 0.3f;
        public static float DebugDayCircleThicknessMrad { get; set; } = 0.2f;
        public static float DebugDayInnerOffsetMrad { get; set; } = 0.55f;
        public static float DebugDayInnerLengthMrad { get; set; } = 0.5f;
        public static float DebugDayAllPositionScale { get; set; } = 0.9f;
        public static float DebugDayAllLengthScale { get; set; } = 0.9f;
        public static float DebugDayAllThicknessScale { get; set; } = 0.5f;

        // === 单条线调试偏移参数 ===
        public sealed class EmesDayLineDebugTuning
        {
            public float PositionOffsetX;  // mrad
            public float PositionOffsetY;  // mrad
            public float LengthOffset;     // mrad
            public float ThicknessOffset;  // mrad
        }

        // 外框线 6 条 (索引0-5)
        public static EmesDayLineDebugTuning[] DebugDayOuterLineOffsets { get; private set; } = new EmesDayLineDebugTuning[6];
        // 十字线 6 条 (索引0-5: 右、左、下、上、上竖、下竖)
        public static EmesDayLineDebugTuning[] DebugDayCrossLineOffsets { get; private set; } = new EmesDayLineDebugTuning[6];

        // 静态初始化默认偏移值
        static EMES18Optic()
        {
            InitDayLineOffsetsDefaults();
        }

        private static void InitDayLineOffsetsDefaults()
        {
            DebugDayOuterLineOffsets[0] = new EmesDayLineDebugTuning { PositionOffsetX = -0.8f, PositionOffsetY = 0f, LengthOffset = -0.7f, ThicknessOffset = 0f };
            DebugDayOuterLineOffsets[1] = new EmesDayLineDebugTuning { PositionOffsetX = 1f, PositionOffsetY = -0.4f, LengthOffset = -1f, ThicknessOffset = 0f };
            DebugDayOuterLineOffsets[2] = new EmesDayLineDebugTuning { PositionOffsetX = 0.8f, PositionOffsetY = 0f, LengthOffset = -0.7f, ThicknessOffset = 0f };
            DebugDayOuterLineOffsets[3] = new EmesDayLineDebugTuning { PositionOffsetX = -1f, PositionOffsetY = -0.4f, LengthOffset = -1f, ThicknessOffset = 0f };
            DebugDayOuterLineOffsets[4] = new EmesDayLineDebugTuning { PositionOffsetX = -1f, PositionOffsetY = 0.4f, LengthOffset = -1f, ThicknessOffset = 0f };
            DebugDayOuterLineOffsets[5] = new EmesDayLineDebugTuning { PositionOffsetX = 1f, PositionOffsetY = 0.4f, LengthOffset = -1f, ThicknessOffset = 0f };

            DebugDayCrossLineOffsets[0] = new EmesDayLineDebugTuning { PositionOffsetX = 0f, PositionOffsetY = 0f, LengthOffset = 0f, ThicknessOffset = 0f };
            DebugDayCrossLineOffsets[1] = new EmesDayLineDebugTuning { PositionOffsetX = 0f, PositionOffsetY = 0f, LengthOffset = 0f, ThicknessOffset = 0f };
            DebugDayCrossLineOffsets[2] = new EmesDayLineDebugTuning { PositionOffsetX = 0f, PositionOffsetY = 0f, LengthOffset = 0f, ThicknessOffset = 0f };
            DebugDayCrossLineOffsets[3] = new EmesDayLineDebugTuning { PositionOffsetX = 0f, PositionOffsetY = 0f, LengthOffset = 0f, ThicknessOffset = 0f };
            DebugDayCrossLineOffsets[4] = new EmesDayLineDebugTuning { PositionOffsetX = 0f, PositionOffsetY = -0.6f, LengthOffset = -0.4f, ThicknessOffset = 0f };
            DebugDayCrossLineOffsets[5] = new EmesDayLineDebugTuning { PositionOffsetX = 0f, PositionOffsetY = 0.6f, LengthOffset = -0.4f, ThicknessOffset = 0f };
        }

        public static float DebugThermalWfovCircleRadiusMrad { get; set; } = 0.3f;
        public static float DebugThermalWfovCircleThicknessMrad { get; set; } = 0.2f;
        public static float DebugThermalWfovInnerOffsetMrad { get; set; } = 0.55f;
        public static float DebugThermalWfovInnerLengthMrad { get; set; } = 0.5f;
        public static float DebugThermalWfovAllPositionScale { get; set; } = 1f;
        public static float DebugThermalWfovAllLengthScale { get; set; } = 1f;
        public static float DebugThermalWfovAllThicknessScale { get; set; } = 1f;
        public static float DebugThermalNfovCircleRadiusMrad { get; set; } = 0.3f;
        public static float DebugThermalNfovCircleThicknessMrad { get; set; } = 0.2f;
        public static float DebugThermalNfovInnerOffsetMrad { get; set; } = 0.7f;
        public static float DebugThermalNfovInnerLengthMrad { get; set; } = 0.7f;
        public static float DebugThermalNfovAllPositionScale { get; set; } = 1f;
        public static float DebugThermalNfovAllLengthScale { get; set; } = 1f;
        public static float DebugThermalNfovAllThicknessScale { get; set; } = 1f;
        private const float EMES_WFOV = 16.0f;
        private const float EMES_NFOV = 4.0f;
        private const float EMES_DAY_FOV = 4.0f;
        private const float EMES_FOV_TOLERANCE = 0.2f;
        private const float EMES_RANGE_MAX = 4000f;
        private const float EMES_BLACKBAR_ALPHA = 0.95f;

        public enum EmesChannelMode
        {
            Thermal,
            Day
        }

        public class EMES18ModeMarker : MonoBehaviour
        {
            public EmesChannelMode Mode;
        }

        private static readonly FieldInfo f_fcs_originalRangeLimits = typeof(FireControlSystem).GetField("_originalRangeLimits", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_fcs_originalRangeStep = typeof(FireControlSystem).GetField("_originalRangeStep", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_optic_hasOverrideRangeLimits = typeof(UsableOptic).GetField("<HasOverrideRangeLimits>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_reticleMesh_reticle = typeof(ReticleMesh).GetField("reticle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_reticleMesh_smr = typeof(ReticleMesh).GetField("SMR", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static GameObject emes18_prefab;
        private static GameObject flir_post_green;
        private static Material flir_blit_material;
        private static bool default_scope_temporarily_hidden;

        private sealed class EmesReticleClone
        {
            public ReticleSO Tree;
            public object Cached;
        }

        private sealed class EmesThermalDonor
        {
            public ReticleMesh NfovMesh;
            public ReticleMesh WfovMesh;
            public float DefaultFov;
            public float[] OtherFovs;
            public float WideFov;
            public float NarrowFov;
        }

        private sealed class EmesDayReticleLineSpec
        {
            public Vector2 PositionMrads;
            public float RotationMrad;
            public float LengthMrad;
            public float ThicknessMrad;
        }

        private sealed class EmesDayReticleCircleSpec
        {
            public Vector2 PositionMrads;
            public float RadiusMrad;
            public float ThicknessMrad;
            public int Segments;
        }

        private sealed class EmesReticleTuningProfile
        {
            public float CircleRadiusMrad;
            public float CircleThicknessMrad;
            public float InnerOffsetMrad;
            public float InnerLengthMrad;
            public float PositionScale;
            public float LengthScale;
            public float ThicknessScale;
        }

        private static readonly EmesDayReticleLineSpec[] emes_day_tree_outer_lines = new EmesDayReticleLineSpec[]
        {
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(3.68155384f, 0f), RotationMrad = 0f, LengthMrad = 2.45436931f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(-4.908737f, 1.96349537f), RotationMrad = 0f, LengthMrad = 4.90873861f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(-3.68155384f, 0f), RotationMrad = 0f, LengthMrad = 2.45436931f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(4.90873861f, 1.96349537f), RotationMrad = 0f, LengthMrad = 4.90873861f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(4.90873861f, -1.96349537f), RotationMrad = 0f, LengthMrad = 4.90873861f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(-4.90873861f, -1.96349537f), RotationMrad = 0f, LengthMrad = 4.90873861f, ThicknessMrad = 0.196349546f }
        };

        private static readonly EmesDayReticleLineSpec[] emes_day_tree_cross_lines = new EmesDayReticleLineSpec[]
        {
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(0.55f, 0f), RotationMrad = 0f, LengthMrad = 0.5f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(-0.55f, 0f), RotationMrad = 0f, LengthMrad = 0.5f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(0f, -0.55f), RotationMrad = 1570.79639f, LengthMrad = 0.5f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(0f, 0.55f), RotationMrad = 1570.79639f, LengthMrad = 0.5f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(0f, 2.94524312f), RotationMrad = 1570.79639f, LengthMrad = 1.96349537f, ThicknessMrad = 0.196349546f },
            new EmesDayReticleLineSpec { PositionMrads = new Vector2(0f, -2.94524312f), RotationMrad = 1570.79639f, LengthMrad = 1.96349537f, ThicknessMrad = 0.196349546f }
        };

        private static readonly EmesDayReticleCircleSpec emes_day_tree_circle = new EmesDayReticleCircleSpec
        {
            PositionMrads = Vector2.zero,
            RadiusMrad = 0.3f,
            ThicknessMrad = 0.1f,
            Segments = 64
        };

        private static EmesThermalDonor emes_thermal_donor;
        private static ReticleMesh emes_day_donor_mesh;
        private static bool emes_thermal_debug_defaults_initialized;
        private static EmesReticleTuningProfile emes_thermal_wfov_default_tuning;
        private static EmesReticleTuningProfile emes_thermal_nfov_default_tuning;
        private static readonly Dictionary<int, string> emes_thermal_failure_summaries = new Dictionary<int, string>();

        
        private sealed class Emes18ThermalRetryApplier : MonoBehaviour
        {
            public UsableOptic Optic;
            public string LastFailureSummary;
            private float _nextTryTime;
            private int _tries;

            private void Update()
            {
                if (Optic == null)
                {
                    Destroy(this);
                    return;
                }

                if (Time.time < _nextTryTime)
                    return;

                _nextTryTime = Time.time + 0.5f;
                _tries++;

                if (ApplyThermalDonorReticles(Optic))
                {
#if DEBUG
                    if (string.IsNullOrWhiteSpace(LastFailureSummary))
                        UnderdogsDebug.LogEMES($"[EMES18] Thermal donor reticle retry succeeded on attempt {_tries}");
                    else
                        UnderdogsDebug.LogEMES($"[EMES18] Thermal donor reticle retry succeeded on attempt {_tries} after: {LastFailureSummary}");
#endif
                    Destroy(this);
                    return;
                }

                LastFailureSummary = GetThermalFailureSummary(Optic);
                MelonLoader.MelonLogger.Warning($"[EMES18] Thermal donor reticle retry attempt {_tries} failed: {LastFailureSummary ?? "unknown"}");

                if (_tries >= 12)
                {
                    MelonLoader.MelonLogger.Warning($"[EMES18] Thermal donor reticle retry exhausted: {LastFailureSummary ?? "unknown"}");
                    Destroy(this);
                }
            }
        }        

        private sealed class DefaultScopeSnapshot
        {
            public bool GameObjectActiveSelf;
            public readonly Dictionary<int, bool> SpriteRendererStates = new Dictionary<int, bool>();
            public readonly Dictionary<int, bool> PostMeshStates = new Dictionary<int, bool>();
            public readonly Dictionary<int, bool> CanvasStates = new Dictionary<int, bool>();
        }

        private static readonly Dictionary<int, DefaultScopeSnapshot> default_scope_snapshots = new Dictionary<int, DefaultScopeSnapshot>();

        // ============================================================
        // PZB-200 原生视觉组件路径（EMES18 替换时需要禁用）
        // ============================================================
        // 这些是原版 PZB-200 瞄准镜的视觉组件，在应用 EMES-18 时需要禁用，
        // 因为 EMES-18 有自己的 UI 和瞄准线系统
        // ============================================================
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
            if (DebugDisableNormalizeScopeSprite) return;
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
                bool shouldHideScope = !DebugDisableThermalDefaultScopeHide && marker != null && marker.Mode == EmesChannelMode.Thermal;
                if (shouldHideScope)
                {
                    default_scope_temporarily_hidden = true;
                    return false;
                }

                if (!DebugDisableDayDefaultScopeShow && marker != null && marker.Mode == EmesChannelMode.Day)
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

        // ============================================================
        // Leopard A1A3 B-171 特殊检测
        // ============================================================
        // Leopard A1A3 使用 B-171 瞄准镜，但与 Leopard 1A3/1A3A2/A1A1 不同:
        // - Leopard 1A3, Leopard 1A3A2, Leopard A1A1: B-171 瞄准镜，炮塔造型不同
        // - Leopard A1A3: B-171 瞄准镜，但炮塔造型与 A1A4 相同（焊接炮塔）
        //
        // 此函数用于在 EMES18 应用时跳过某些 PZB-200 特有的处理逻辑，
        // 因为 B-171 瞄准镜的视觉结构不同，不需要禁用 PZB-200 的 render texture 面板
        // ============================================================
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

            string n = (t.name ?? string.Empty);
            if (n.Equals("rangefinder mark vis parent", StringComparison.OrdinalIgnoreCase))
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

            string n = (t.name ?? string.Empty);
            if (n.Equals("Stereo rangefinder", StringComparison.OrdinalIgnoreCase)) return true;
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

        
        
        private static AngularLength CreateAngularLength(float mrad, AngularLength.AngularUnit unit)
        {
            object boxed = new AngularLength(0f, unit);
            typeof(AngularLength).GetField("mrad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(boxed, mrad);
            typeof(AngularLength).GetField("unit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(boxed, unit);
            return (AngularLength)boxed;
        }

        private static ReticleTree.Position CreatePosition(float x, float y)
        {
            return new ReticleTree.Position(x, y, AngularLength.AngularUnit.MIL_NATO, LinearLength.LinearUnit.M);
        }

        private static void TrySetMember(object target, string memberName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName)) return;

            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo field = target.GetType().GetField(memberName, flags);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                PropertyInfo property = target.GetType().GetProperty(memberName, flags);
                if (property != null && property.CanWrite)
                    property.SetValue(target, value, null);
            }
            catch { }
        }

        private static void SetOpticPresentationItems(UsableOptic optic, UsableOptic.FovLimitedItem[] fovLimitedItems, ReticleMesh[] additionalReticleMeshes)
        {
            if (optic == null)
                return;

            TrySetMember(optic, "FovLimitedItems", fovLimitedItems);
            TrySetMember(optic, "<FovLimitedItems>k__BackingField", fovLimitedItems);
            TrySetMember(optic, "AdditionalReticleMeshes", additionalReticleMeshes);
            TrySetMember(optic, "<AdditionalReticleMeshes>k__BackingField", additionalReticleMeshes);
        }

        private static UsableOptic.FovLimitedItem CreateFovLimitedItem(Vector2 fovRange, GameObject[] exclusiveObjects)
        {
            object boxed = FormatterServices.GetUninitializedObject(typeof(UsableOptic.FovLimitedItem));
            TrySetMember(boxed, "FovRange", fovRange);
            TrySetMember(boxed, "ExclusiveObjects", exclusiveObjects);
            TrySetMember(boxed, "<FovRange>k__BackingField", fovRange);
            TrySetMember(boxed, "<ExclusiveObjects>k__BackingField", exclusiveObjects);
            return (UsableOptic.FovLimitedItem)boxed;
        }


        private static object CloneCachedReticleObject(object source, ReticleSO treeClone)
        {
            if (source == null || treeClone == null)
                return null;

            Type cachedType = source.GetType();
            object cachedClone = Activator.CreateInstance(cachedType);
            UECommonUtil.ShallowCopy(cachedClone, source);

            FieldInfo treeField = cachedType.GetField("tree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo meshField = cachedType.GetField("mesh", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            treeField?.SetValue(cachedClone, treeClone);
            meshField?.SetValue(cachedClone, null);
            return cachedClone;
        }

        private static EmesReticleClone CloneReticleFromMesh(ReticleMesh sourceMesh, string cloneName)
        {
            if (sourceMesh == null || sourceMesh.reticleSO == null)
                return null;

            ReticleSO treeClone = ScriptableObject.Instantiate(sourceMesh.reticleSO);
            if (!string.IsNullOrWhiteSpace(cloneName))
                treeClone.name = cloneName;

            object sourceCached = null;
            try { sourceCached = f_reticleMesh_reticle?.GetValue(sourceMesh); } catch { }

            return new EmesReticleClone
            {
                Tree = treeClone,
                Cached = CloneCachedReticleObject(sourceCached, treeClone)
            };
        }

        private static void AssignReticleToMesh(ReticleMesh targetMesh, EmesReticleClone clone)
        {
            if (targetMesh == null || clone == null || clone.Tree == null)
                return;

            targetMesh.reticleSO = clone.Tree;
            try { f_reticleMesh_reticle?.SetValue(targetMesh, clone.Cached); } catch { }
            try { f_reticleMesh_smr?.SetValue(targetMesh, null); } catch { }
            targetMesh.Load();
        }

        private static ReticleMesh ResolveNamedReticleMesh(UsableOptic optic, params string[] names)
        {
            if (optic == null)
                return null;

            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    string candidate = names[i];
                    if (string.IsNullOrWhiteSpace(candidate)) continue;

                    Transform match = optic.transform.Find(candidate);
                    if (match != null)
                    {
                        ReticleMesh mesh = match.GetComponent<ReticleMesh>();
                        if (mesh != null) return mesh;
                    }
                }
            }

            return optic.GetComponentsInChildren<ReticleMesh>(true)
                .FirstOrDefault(mesh => mesh != null && names != null && names.Any(n => !string.IsNullOrWhiteSpace(n) && mesh.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static void SetReticleMeshEnabled(ReticleMesh mesh, bool enabled)
        {
            if (mesh == null)
                return;

            try
            {
                mesh.enabled = enabled;
                mesh.gameObject.SetActive(enabled);
                SkinnedMeshRenderer renderer = mesh.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null) renderer.enabled = enabled;

                foreach (PostMeshComp postMesh in mesh.GetComponentsInChildren<PostMeshComp>(true))
                {
                    if (postMesh != null)
                        postMesh.enabled = enabled;
                }
            }
            catch { }
        }

        private static void EnsureReticleMeshVisible(ReticleMesh reticleMesh)
        {
            if (reticleMesh == null)
                return;

            if (!reticleMesh.gameObject.activeSelf)
                reticleMesh.gameObject.SetActive(true);
            if (!reticleMesh.enabled)
                reticleMesh.enabled = true;

            SkinnedMeshRenderer renderer = reticleMesh.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null && !renderer.enabled)
                renderer.enabled = true;

            foreach (PostMeshComp postMesh in reticleMesh.GetComponentsInChildren<PostMeshComp>(true))
            {
                if (postMesh != null && !postMesh.enabled)
                    postMesh.enabled = true;
            }
        }

        private static void DisableOtherReticleMeshes(UsableOptic optic, params ReticleMesh[] keepMeshes)
        {
            if (optic == null)
                return;

            HashSet<ReticleMesh> keep = new HashSet<ReticleMesh>(keepMeshes.Where(mesh => mesh != null));
            foreach (ReticleMesh mesh in optic.GetComponentsInChildren<ReticleMesh>(true))
            {
                if (mesh == null || keep.Contains(mesh)) continue;
                SetReticleMeshEnabled(mesh, false);
            }
        }

        private static void CleanupLegacyEmes18Presentation(UsableOptic optic)
        {
            if (optic == null)
                return;

            foreach (EMES18Monitor monitor in optic.GetComponentsInChildren<EMES18Monitor>(true))
            {
                if (monitor != null)
                    GameObject.Destroy(monitor.gameObject);
            }

            Transform legacyCanvas = optic.transform.Find("EMES18DayReticleCanvas");
            if (legacyCanvas != null)
                GameObject.Destroy(legacyCanvas.gameObject);
        }

        private static bool IsRangefindingReticleMesh(ReticleMesh mesh)
        {
            if (mesh == null)
                return false;

            string name = mesh.name ?? string.Empty;
            return name.IndexOf("rangefinding", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("range finder", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ReticleMesh ResolveEmesDayHostReticleMesh(UsableOptic optic)
        {
            if (optic == null)
                return null;

            try
            {
                if (optic.reticleMesh != null && !IsRangefindingReticleMesh(optic.reticleMesh))
                    return optic.reticleMesh;
            }
            catch { }

            Transform direct = optic.transform.Find("Reticle Mesh");
            if (direct != null)
            {
                ReticleMesh directMesh = direct.GetComponent<ReticleMesh>();
                if (directMesh != null && !IsRangefindingReticleMesh(directMesh))
                    return directMesh;
            }

            return optic.GetComponentsInChildren<ReticleMesh>(true)
                .FirstOrDefault(mesh => mesh != null && !IsRangefindingReticleMesh(mesh) &&
                    ((mesh.name ?? string.Empty).IndexOf("Reticle Mesh", StringComparison.OrdinalIgnoreCase) >= 0 || mesh.gameObject.activeSelf));
        }

        private static void DisableLegacyMonitorReticleGraphics(Transform root)
        {
            if (root == null)
                return;

            string[] nonUiRoots = new[]
            {                
                "TURRET"  // L1A5炮塔mesh由Leopard1Model处理，这里隐藏
            };

            for (int i = 0; i < nonUiRoots.Length; i++)
            {
                Transform node = root.Find(nonUiRoots[i]);
                if (node != null)
                    node.gameObject.SetActive(false);
            }

            Transform ui = root.Find("UI");
            if (ui != null)
                ui.gameObject.SetActive(true);
        }

        private static Transform EnsureThermalMonitorUi(UsableOptic optic)
        {
            if (optic == null || emes18_prefab == null)
                return null;

            Transform existing = optic.transform.Find("__UE_EMES18_UI__");
            if (existing == null)
            {
                GameObject monitor = GameObject.Instantiate(emes18_prefab, optic.transform);
                monitor.name = "__UE_EMES18_UI__";
                existing = monitor.transform;
            }

            ApplyMonitorPreset(existing);
            ApplyMonitorChannelMode(existing, EmesChannelMode.Thermal);
            DisableLegacyMonitorReticleGraphics(existing);

            EMES18Monitor monitorComp = existing.GetComponent<EMES18Monitor>();
            if (monitorComp == null)
                monitorComp = existing.gameObject.AddComponent<EMES18Monitor>();

            monitorComp.ForceDayChannel = false;
            monitorComp.UiOnlyThermalMonitor = true;
            monitorComp.RefreshModeVisuals();

            TextMeshProUGUI rangeText = existing.Find("UI/RANGE")?.GetComponent<TextMeshProUGUI>();
            Transform readyNode = existing.Find("UI/READYSTATE");
            if (rangeText != null) optic.RangeText = rangeText;
            if (readyNode != null) optic.ReadyToFireObject = readyNode.gameObject;
            return existing;
        }

        private static void ClearThermalPresentationOverrides(UsableOptic optic)
        {
            if (optic == null)
                return;

            SetOpticPresentationItems(optic, new UsableOptic.FovLimitedItem[0], new ReticleMesh[0]);
            TrySetMember(optic, "_reticleMeshLocalPositions", new Vector2[] { Vector2.zero, Vector2.zero });

            foreach (Transform child in optic.transform)
            {
                if (child == null) continue;
                if ((child.name ?? string.Empty).StartsWith("__UE_EMES18_WFOV__", StringComparison.OrdinalIgnoreCase))
                    GameObject.Destroy(child.gameObject);
            }
        }

        private static void ApplyThermalFovItems(UsableOptic optic, ReticleMesh narrowMesh, ReticleMesh wideMesh, float wideFov, float narrowFov)
        {
            if (optic == null || narrowMesh == null || wideMesh == null)
                return;

            float resolvedWideFov = wideFov > 0.1f ? wideFov : EMES_WFOV;
            float resolvedNarrowFov = narrowFov > 0.1f ? narrowFov : EMES_NFOV;
            if (resolvedWideFov < resolvedNarrowFov)
            {
                float swap = resolvedWideFov;
                resolvedWideFov = resolvedNarrowFov;
                resolvedNarrowFov = swap;
            }

            float threshold = (resolvedWideFov + resolvedNarrowFov) * 0.5f;
            UsableOptic.FovLimitedItem wideLim = CreateFovLimitedItem(
                new Vector2(threshold, 360f),
                new GameObject[] { wideMesh.gameObject });

            UsableOptic.FovLimitedItem narrowLim = CreateFovLimitedItem(
                new Vector2(0f, threshold),
                new GameObject[] { narrowMesh.gameObject });

            TrySetMember(optic, "_reticleMeshLocalPositions", new Vector2[] { Vector2.zero, Vector2.zero });
            SetOpticPresentationItems(optic,
                new UsableOptic.FovLimitedItem[] { wideLim, narrowLim },
                new ReticleMesh[] { wideMesh });
        }

        private static string FormatReticleTuningProfile(EmesReticleTuningProfile profile)
        {
            if (profile == null)
                return "null";

            return string.Format(CultureInfo.InvariantCulture,
                "CircleRadius={0:F3}, CircleThickness={1:F3}, InnerOffset={2:F3}, InnerLength={3:F3}, PositionScale={4:F3}, LengthScale={5:F3}, ThicknessScale={6:F3}",
                profile.CircleRadiusMrad,
                profile.CircleThicknessMrad,
                profile.InnerOffsetMrad,
                profile.InnerLengthMrad,
                profile.PositionScale,
                profile.LengthScale,
                profile.ThicknessScale);
        }

        private static EmesReticleTuningProfile CreateDefaultReticleTuningProfile()
        {
            return new EmesReticleTuningProfile
            {
                CircleRadiusMrad = 0.3f,
                CircleThicknessMrad = 0.2f,
                InnerOffsetMrad = 0.7f,
                InnerLengthMrad = 0.7f,
                PositionScale = 1f,
                LengthScale = 1f,
                ThicknessScale = 1f
            };
        }

        private static EmesReticleTuningProfile GetThermalTuningProfile(bool wide)
        {
            return new EmesReticleTuningProfile
            {
                CircleRadiusMrad = wide ? DebugThermalWfovCircleRadiusMrad : DebugThermalNfovCircleRadiusMrad,
                CircleThicknessMrad = wide ? DebugThermalWfovCircleThicknessMrad : DebugThermalNfovCircleThicknessMrad,
                InnerOffsetMrad = wide ? DebugThermalWfovInnerOffsetMrad : DebugThermalNfovInnerOffsetMrad,
                InnerLengthMrad = wide ? DebugThermalWfovInnerLengthMrad : DebugThermalNfovInnerLengthMrad,
                PositionScale = wide ? DebugThermalWfovAllPositionScale : DebugThermalNfovAllPositionScale,
                LengthScale = wide ? DebugThermalWfovAllLengthScale : DebugThermalNfovAllLengthScale,
                ThicknessScale = wide ? DebugThermalWfovAllThicknessScale : DebugThermalNfovAllThicknessScale
            };
        }

        private static void SetThermalTuningProfile(bool wide, EmesReticleTuningProfile profile)
        {
            if (profile == null)
                return;

            if (wide)
            {
                DebugThermalWfovCircleRadiusMrad = profile.CircleRadiusMrad;
                DebugThermalWfovCircleThicknessMrad = profile.CircleThicknessMrad;
                DebugThermalWfovInnerOffsetMrad = profile.InnerOffsetMrad;
                DebugThermalWfovInnerLengthMrad = profile.InnerLengthMrad;
                DebugThermalWfovAllPositionScale = profile.PositionScale;
                DebugThermalWfovAllLengthScale = profile.LengthScale;
                DebugThermalWfovAllThicknessScale = profile.ThicknessScale;
                return;
            }

            DebugThermalNfovCircleRadiusMrad = profile.CircleRadiusMrad;
            DebugThermalNfovCircleThicknessMrad = profile.CircleThicknessMrad;
            DebugThermalNfovInnerOffsetMrad = profile.InnerOffsetMrad;
            DebugThermalNfovInnerLengthMrad = profile.InnerLengthMrad;
            DebugThermalNfovAllPositionScale = profile.PositionScale;
            DebugThermalNfovAllLengthScale = profile.LengthScale;
            DebugThermalNfovAllThicknessScale = profile.ThicknessScale;
        }

        private static void ResetThermalReticleDebugTuning(bool wide)
        {
            EmesReticleTuningProfile defaults = wide ? emes_thermal_wfov_default_tuning : emes_thermal_nfov_default_tuning;
            SetThermalTuningProfile(wide, defaults ?? CreateDefaultReticleTuningProfile());
        }

        public static void ResetThermalWfovReticleDebugTuning()
        {
            ResetThermalReticleDebugTuning(true);
        }

        public static void ResetThermalNfovReticleDebugTuning()
        {
            ResetThermalReticleDebugTuning(false);
        }

        public static string DescribeThermalWfovReticleDebugTuning()
        {
            return FormatReticleTuningProfile(GetThermalTuningProfile(true));
        }

        public static string DescribeThermalNfovReticleDebugTuning()
        {
            return FormatReticleTuningProfile(GetThermalTuningProfile(false));
        }

        public static bool DebugReapplyThermalReticles(UsableOptic optic)
        {
            return ApplyThermalDonorReticles(optic);
        }

        private static void SetThermalFailureSummary(UsableOptic optic, string summary)
        {
            if (optic == null)
                return;

            emes_thermal_failure_summaries[optic.GetInstanceID()] = summary ?? string.Empty;
        }

        private static string GetThermalFailureSummary(UsableOptic optic)
        {
            if (optic == null)
                return null;

            string summary;
            return emes_thermal_failure_summaries.TryGetValue(optic.GetInstanceID(), out summary) ? summary : null;
        }

        private static void ClearThermalFailureSummary(UsableOptic optic)
        {
            if (optic == null)
                return;

            emes_thermal_failure_summaries.Remove(optic.GetInstanceID());
        }

        private static bool FailThermalApply(UsableOptic optic, string summary)
        {
            SetThermalFailureSummary(optic, summary);
            MelonLoader.MelonLogger.Warning(summary);
            return false;
        }

        private static void LogThermalInitState(UsableOptic optic, string stage)
        {
            // Release模式下空实现，DEBUG模式下有实际实现
        }

#if DEBUG
        private static void LogThermalInitStateDebug(UsableOptic optic, string stage)
        {
            if (optic == null)
            {
                UnderdogsDebug.LogEMES($"[EMES18][ThermalInit] {stage}: optic=null");
                return;
            }

            CameraSlot slot = optic.slot;
            ReticleMesh[] meshes = optic.GetComponentsInChildren<ReticleMesh>(true);
            string activeReticle = optic.reticleMesh != null ? optic.reticleMesh.name : "null";
            UnderdogsDebug.LogEMES($"[EMES18][ThermalInit] {stage}: activeSelf={optic.gameObject.activeSelf}, activeInHierarchy={optic.gameObject.activeInHierarchy}, opticEnabled={optic.enabled}, slotEnabled={(slot != null ? slot.enabled.ToString() : "null")}, reticleMesh={activeReticle}, meshCount={(meshes != null ? meshes.Length : 0)}");
        }
#endif

        private static void PulseThermalOpticInitialization(UsableOptic optic)
        {
            if (optic == null)
                return;

            bool originalActive = optic.gameObject.activeSelf;
            LogThermalInitState(optic, "before-pulse");

            try
            {
                optic.gameObject.SetActive(true);
                LogThermalInitState(optic, "after-setactive-true");
            }
            catch (Exception ex)
            {
#if DEBUG
                UnderdogsDebug.LogEMESWarning($"[EMES18][ThermalInit] SetActive(true) failed: {ex.Message}");
#endif
            }

            try
            {
                optic.gameObject.SetActive(false);
                LogThermalInitState(optic, "after-setactive-false");
            }
            catch (Exception ex)
            {
#if DEBUG
                UnderdogsDebug.LogEMESWarning($"[EMES18][ThermalInit] SetActive(false) failed: {ex.Message}");
#endif
            }

            try
            {
                optic.gameObject.SetActive(originalActive);
                LogThermalInitState(optic, "restored-original-active");
            }
            catch (Exception ex)
            {
#if DEBUG
                UnderdogsDebug.LogEMESWarning($"[EMES18][ThermalInit] restore active={originalActive} failed: {ex.Message}");
#endif
            }
        }

        private static void CollectReticlePrimitives(IEnumerable<ReticleTree.TransformElement> elements, List<ReticleTree.Line> lines, List<ReticleTree.Circle> circles)
        {
            if (elements == null)
                return;

            foreach (ReticleTree.TransformElement element in elements)
            {
                if (element == null)
                    continue;

                ReticleTree.Line line = element as ReticleTree.Line;
                if (line != null)
                    lines.Add(line);

                ReticleTree.Circle circle = element as ReticleTree.Circle;
                if (circle != null)
                    circles.Add(circle);

                ReticleTree.GroupBase group = element as ReticleTree.GroupBase;
                if (group != null && group.elements != null)
                    CollectReticlePrimitives(group.elements, lines, circles);
            }
        }

        private static void CollectReticlePrimitives(ReticleSO tree, List<ReticleTree.Line> lines, List<ReticleTree.Circle> circles)
        {
            if (tree == null || tree.planes == null)
                return;

            for (int i = 0; i < tree.planes.Count; i++)
            {
                var plane = tree.planes[i];
                if (plane == null || plane.elements == null)
                    continue;

                CollectReticlePrimitives(plane.elements, lines, circles);
            }
        }

        private static Vector2 GetReticleElementPosition(ReticleTree.TransformElement element)
        {
            return element != null && element.position != null ? (Vector2)element.position : Vector2.zero;
        }

        private static void SetReticleElementPosition(ReticleTree.TransformElement element, Vector2 positionMrads)
        {
            if (element == null)
                return;

            element.position = CreatePosition(positionMrads.x, positionMrads.y);
        }

        private static float NormalizeSignedDegrees(float degrees)
        {
            while (degrees > 180f) degrees -= 360f;
            while (degrees < -180f) degrees += 360f;
            return degrees;
        }

        private static bool IsHorizontalReticleLine(ReticleTree.Line line)
        {
            if (line == null)
                return false;

            float rotation = Mathf.Abs(NormalizeSignedDegrees(line.rotation.DEGS));
            return rotation <= 15f || Mathf.Abs(rotation - 180f) <= 15f;
        }

        private static bool IsVerticalReticleLine(ReticleTree.Line line)
        {
            if (line == null)
                return false;

            float rotation = Mathf.Abs(NormalizeSignedDegrees(line.rotation.DEGS));
            return Mathf.Abs(rotation - 90f) <= 15f;
        }

        private static ReticleTree.Line FindInnerReticleLine(IEnumerable<ReticleTree.Line> lines, bool horizontal, int sign)
        {
            if (lines == null)
                return null;

            ReticleTree.Line best = null;
            float bestScore = float.MaxValue;
            foreach (ReticleTree.Line line in lines)
            {
                if (line == null)
                    continue;

                Vector2 pos = GetReticleElementPosition(line);
                if (horizontal)
                {
                    if (!IsHorizontalReticleLine(line))
                        continue;
                    if (sign > 0 && pos.x <= 0f)
                        continue;
                    if (sign < 0 && pos.x >= 0f)
                        continue;
                }
                else
                {
                    if (!IsVerticalReticleLine(line))
                        continue;
                    if (sign > 0 && pos.y <= 0f)
                        continue;
                    if (sign < 0 && pos.y >= 0f)
                        continue;
                }

                float primary = horizontal ? Mathf.Abs(pos.x) : Mathf.Abs(pos.y);
                float secondary = horizontal ? Mathf.Abs(pos.y) : Mathf.Abs(pos.x);
                float score = primary + secondary * 2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = line;
                }
            }

            return best;
        }

        private static EmesReticleTuningProfile CaptureThermalTuningProfile(ReticleMesh mesh)
        {
            EmesReticleTuningProfile profile = CreateDefaultReticleTuningProfile();
            if (mesh == null || mesh.reticleSO == null)
                return profile;

            List<ReticleTree.Line> lines = new List<ReticleTree.Line>();
            List<ReticleTree.Circle> circles = new List<ReticleTree.Circle>();
            CollectReticlePrimitives(mesh.reticleSO, lines, circles);

            ReticleTree.Circle circle = circles.FirstOrDefault();
            if (circle != null)
            {
                profile.CircleRadiusMrad = circle.radius.MRADS;
                profile.CircleThicknessMrad = circle.thickness.MRADS;
            }

            ReticleTree.Line right = FindInnerReticleLine(lines, true, 1);
            ReticleTree.Line left = FindInnerReticleLine(lines, true, -1);
            ReticleTree.Line down = FindInnerReticleLine(lines, false, -1);
            ReticleTree.Line up = FindInnerReticleLine(lines, false, 1);
            ReticleTree.Line reference = right ?? left ?? down ?? up;
            if (reference != null)
                profile.InnerLengthMrad = reference.length.MRADS;
            if (right != null) profile.InnerOffsetMrad = Mathf.Abs(GetReticleElementPosition(right).x);
            else if (left != null) profile.InnerOffsetMrad = Mathf.Abs(GetReticleElementPosition(left).x);
            else if (up != null) profile.InnerOffsetMrad = Mathf.Abs(GetReticleElementPosition(up).y);
            else if (down != null) profile.InnerOffsetMrad = Mathf.Abs(GetReticleElementPosition(down).y);

            return profile;
        }

        private static void EnsureThermalDebugDefaultsInitialized(EmesThermalDonor donor)
        {
            if (emes_thermal_debug_defaults_initialized || donor == null)
                return;

            emes_thermal_wfov_default_tuning = CaptureThermalTuningProfile(donor.WfovMesh);
            emes_thermal_nfov_default_tuning = new EmesReticleTuningProfile
            {
                CircleRadiusMrad = 0.3f,
                CircleThicknessMrad = 0.2f,
                InnerOffsetMrad = 0.7f,
                InnerLengthMrad = 0.7f,
                PositionScale = 1f,
                LengthScale = 1f,
                ThicknessScale = 1f
            };
            SetThermalTuningProfile(true, emes_thermal_wfov_default_tuning);
            SetThermalTuningProfile(false, emes_thermal_nfov_default_tuning);
            emes_thermal_debug_defaults_initialized = true;
#if DEBUG
            UnderdogsDebug.LogEMES($"[EMES18] Thermal reticle debug defaults seeded | WFOV: {DescribeThermalWfovReticleDebugTuning()} | NFOV: {DescribeThermalNfovReticleDebugTuning()}");
#endif
        }

        private static void ApplyReticleTuningProfile(ReticleSO tree, EmesReticleTuningProfile profile)
        {
            if (tree == null || profile == null)
                return;

            List<ReticleTree.Line> lines = new List<ReticleTree.Line>();
            List<ReticleTree.Circle> circles = new List<ReticleTree.Circle>();
            CollectReticlePrimitives(tree, lines, circles);

            ReticleTree.Line right = FindInnerReticleLine(lines, true, 1);
            ReticleTree.Line left = FindInnerReticleLine(lines, true, -1);
            ReticleTree.Line down = FindInnerReticleLine(lines, false, -1);
            ReticleTree.Line up = FindInnerReticleLine(lines, false, 1);

            foreach (ReticleTree.Line line in lines)
            {
                if (line == null)
                    continue;

                SetReticleElementPosition(line, GetReticleElementPosition(line) * profile.PositionScale);
                line.length = CreateAngularLength(line.length.MRADS * profile.LengthScale, AngularLength.AngularUnit.MIL_NATO);
                line.thickness = CreateAngularLength(line.thickness.MRADS * profile.ThicknessScale, AngularLength.AngularUnit.MIL_NATO);
            }

            foreach (ReticleTree.Circle circle in circles)
            {
                if (circle == null)
                    continue;

                SetReticleElementPosition(circle, GetReticleElementPosition(circle) * profile.PositionScale);
                circle.radius = CreateAngularLength(profile.CircleRadiusMrad * profile.LengthScale, AngularLength.AngularUnit.MIL_NATO);
                circle.thickness = CreateAngularLength(profile.CircleThicknessMrad * profile.ThicknessScale, AngularLength.AngularUnit.MIL_NATO);
            }

            if (right != null) { SetReticleElementPosition(right, new Vector2(profile.InnerOffsetMrad * profile.PositionScale, 0f)); right.length = CreateAngularLength(profile.InnerLengthMrad * profile.LengthScale, AngularLength.AngularUnit.MIL_NATO); }
            if (left != null) { SetReticleElementPosition(left, new Vector2(-profile.InnerOffsetMrad * profile.PositionScale, 0f)); left.length = CreateAngularLength(profile.InnerLengthMrad * profile.LengthScale, AngularLength.AngularUnit.MIL_NATO); }
            if (down != null) { SetReticleElementPosition(down, new Vector2(0f, -profile.InnerOffsetMrad * profile.PositionScale)); down.length = CreateAngularLength(profile.InnerLengthMrad * profile.LengthScale, AngularLength.AngularUnit.MIL_NATO); }
            if (up != null) { SetReticleElementPosition(up, new Vector2(0f, profile.InnerOffsetMrad * profile.PositionScale)); up.length = CreateAngularLength(profile.InnerLengthMrad * profile.LengthScale, AngularLength.AngularUnit.MIL_NATO); }
        }

        private static void ApplyThermalReticleDebugTuning(EmesReticleClone clone, bool wide)
        {
            if (clone == null || clone.Tree == null)
                return;

            ApplyReticleTuningProfile(clone.Tree, GetThermalTuningProfile(wide));
        }

        private static EmesDayReticleLineSpec ApplyGlobalDayLineScale(EmesDayReticleLineSpec spec)
        {
            return new EmesDayReticleLineSpec
            {
                PositionMrads = spec.PositionMrads * DebugDayAllPositionScale,
                RotationMrad = spec.RotationMrad,
                LengthMrad = spec.LengthMrad * DebugDayAllLengthScale,
                ThicknessMrad = spec.ThicknessMrad * DebugDayAllThicknessScale
            };
        }

        private static EmesDayReticleLineSpec ApplyLineOffset(EmesDayReticleLineSpec spec, EmesDayLineDebugTuning offset)
        {
            if (offset == null) return spec;
            return new EmesDayReticleLineSpec
            {
                PositionMrads = spec.PositionMrads + new Vector2(offset.PositionOffsetX, offset.PositionOffsetY),
                RotationMrad = spec.RotationMrad,
                LengthMrad = spec.LengthMrad + offset.LengthOffset,
                ThicknessMrad = spec.ThicknessMrad + offset.ThicknessOffset
            };
        }

        private static EmesDayReticleLineSpec GetDebugAdjustedOuterLineSpec(int index)
        {
            EmesDayReticleLineSpec baseSpec = emes_day_tree_outer_lines[index];
            EmesDayLineDebugTuning offset = DebugDayOuterLineOffsets[index] ?? new EmesDayLineDebugTuning();
            return ApplyGlobalDayLineScale(ApplyLineOffset(baseSpec, offset));
        }

        private static EmesDayReticleLineSpec GetDebugAdjustedCrossLineSpec(int index)
        {
            EmesDayReticleLineSpec source = emes_day_tree_cross_lines[index];
            EmesDayReticleLineSpec spec = new EmesDayReticleLineSpec
            {
                PositionMrads = source.PositionMrads,
                RotationMrad = source.RotationMrad,
                LengthMrad = source.LengthMrad,
                ThicknessMrad = source.ThicknessMrad
            };

            // 前4条十字线使用全局Inner参数覆盖
            if (index == 0)
            {
                spec.PositionMrads = new Vector2(DebugDayInnerOffsetMrad, 0f);
                spec.LengthMrad = DebugDayInnerLengthMrad;
            }
            else if (index == 1)
            {
                spec.PositionMrads = new Vector2(-DebugDayInnerOffsetMrad, 0f);
                spec.LengthMrad = DebugDayInnerLengthMrad;
            }
            else if (index == 2)
            {
                spec.PositionMrads = new Vector2(0f, -DebugDayInnerOffsetMrad);
                spec.LengthMrad = DebugDayInnerLengthMrad;
            }
            else if (index == 3)
            {
                spec.PositionMrads = new Vector2(0f, DebugDayInnerOffsetMrad);
                spec.LengthMrad = DebugDayInnerLengthMrad;
            }

            // 应用单独偏移
            EmesDayLineDebugTuning offset = DebugDayCrossLineOffsets[index] ?? new EmesDayLineDebugTuning();
            spec = ApplyLineOffset(spec, offset);

            return ApplyGlobalDayLineScale(spec);
        }

        private static EmesDayReticleCircleSpec GetDebugAdjustedCircleSpec()
        {
            return new EmesDayReticleCircleSpec
            {
                PositionMrads = emes_day_tree_circle.PositionMrads * DebugDayAllPositionScale,
                RadiusMrad = DebugDayCircleRadiusMrad * DebugDayAllLengthScale,
                ThicknessMrad = DebugDayCircleThicknessMrad * DebugDayAllThicknessScale,
                Segments = emes_day_tree_circle.Segments
            };
        }

        public static void ResetDayReticleDebugTuning()
        {
            DebugDayCircleRadiusMrad = 0.3f;
            DebugDayCircleThicknessMrad = 0.2f;
            DebugDayInnerOffsetMrad = 0.55f;
            DebugDayInnerLengthMrad = 0.5f;
            DebugDayAllPositionScale = 0.9f;
            DebugDayAllLengthScale = 0.9f;
            DebugDayAllThicknessScale = 0.5f;

            InitDayLineOffsetsDefaults();
        }

        public static void ResetDayLineOffsets()
        {
            for (int i = 0; i < 6; i++)
            {
                DebugDayOuterLineOffsets[i] = new EmesDayLineDebugTuning();
                DebugDayCrossLineOffsets[i] = new EmesDayLineDebugTuning();
            }
        }

        public static bool DebugReapplyDayReticle(UsableOptic optic)
        {
            return ApplyPeriZ11DayReticleOverride(optic);
        }


        public static string DescribeDayReticleDebugTuning()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "CircleRadius={0:F3}, CircleThickness={1:F3}, InnerOffset={2:F3}, InnerLength={3:F3}, PositionScale={4:F3}, LengthScale={5:F3}, ThicknessScale={6:F3}",
                DebugDayCircleRadiusMrad,
                DebugDayCircleThicknessMrad,
                DebugDayInnerOffsetMrad,
                DebugDayInnerLengthMrad,
                DebugDayAllPositionScale,
                DebugDayAllLengthScale,
                DebugDayAllThicknessScale);
        }

        private static void BuildPeriZ11DayReticleTree(ReticleSO tree)
        {
            if (tree == null || tree.planes == null || tree.planes.Count == 0)
                return;

            tree.name = "PERI-Z11 NFOV (UE)";
            tree.planes[0].elements = new List<ReticleTree.TransformElement>();

            ReticleTree.Angular boresight = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.Boresight);
            boresight.name = "Boresight";
            boresight.position = CreatePosition(0f, 0f);
            boresight.rotation = CreateAngularLength(0f, AngularLength.AngularUnit.DEG);
            boresight.align = ReticleTree.GroupBase.Alignment.Boresight;
            boresight.elements = new List<ReticleTree.TransformElement>();

            boresight.elements.Add(CreateDayLine(GetDebugAdjustedOuterLineSpec(0)));

            ReticleTree.Angular aimingCross = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.Boresight);
            aimingCross.name = "aiming cross";
            aimingCross.position = CreatePosition(0f, 0f);
            aimingCross.rotation = CreateAngularLength(0f, AngularLength.AngularUnit.DEG);
            aimingCross.align = ReticleTree.GroupBase.Alignment.Boresight;
            aimingCross.elements = new List<ReticleTree.TransformElement>();
            aimingCross.elements.Add(CreateDayCircle(GetDebugAdjustedCircleSpec()));
            for (int i = 0; i < emes_day_tree_cross_lines.Length; i++)
                aimingCross.elements.Add(CreateDayLine(GetDebugAdjustedCrossLineSpec(i)));

            boresight.elements.Add(aimingCross);

            for (int i = 1; i < emes_day_tree_outer_lines.Length; i++)
                boresight.elements.Add(CreateDayLine(GetDebugAdjustedOuterLineSpec(i)));

            tree.planes[0].elements.Add(boresight);
        }

        private static ReticleTree.Line CreateDayLine(EmesDayReticleLineSpec spec)
        {
            ReticleTree.Line line = new ReticleTree.Line();
            line.position = CreatePosition(spec.PositionMrads.x, spec.PositionMrads.y);
            line.rotation = CreateAngularLength(spec.RotationMrad, AngularLength.AngularUnit.DEG);
            line.visualType = ReticleTree.VisualElement.Type.Painted;
            line.illumination = ReticleTree.Light.Type.NightIllumination;
            line.thickness = CreateAngularLength(spec.ThicknessMrad, AngularLength.AngularUnit.MIL_NATO);
            line.length = CreateAngularLength(spec.LengthMrad, AngularLength.AngularUnit.MIL_NATO);
            TrySetMember(line, "roundness", 1f);
            TrySetMember(line, "writingTransform", false);
            return line;
        }

        private static ReticleTree.Circle CreateDayCircle(EmesDayReticleCircleSpec spec)
        {
            ReticleTree.Circle circle = new ReticleTree.Circle();
            circle.position = CreatePosition(spec.PositionMrads.x, spec.PositionMrads.y);
            circle.rotation = CreateAngularLength(0f, AngularLength.AngularUnit.DEG);
            circle.visualType = ReticleTree.VisualElement.Type.Painted;
            circle.illumination = ReticleTree.Light.Type.NightIllumination;
            circle.radius = CreateAngularLength(spec.RadiusMrad, AngularLength.AngularUnit.MIL_NATO);
            circle.thickness = CreateAngularLength(spec.ThicknessMrad, AngularLength.AngularUnit.MIL_NATO);
            TrySetMember(circle, "segments", spec.Segments);
            return circle;
        }

        private static bool ApplyPeriZ11DayReticleOverride(UsableOptic optic)
        {
            if (optic == null)
                return false;

            ReticleMesh dayMesh = ResolveEmesDayHostReticleMesh(optic);
            if (dayMesh == null)
                return false;

            ReticleMesh donorMesh = LoadPeriZ11DayDonorMesh() ?? dayMesh;
            EmesReticleClone clone = CloneReticleFromMesh(donorMesh, "PERI-Z11 NFOV (UE EMES18)");
            if (clone == null && donorMesh != dayMesh)
                clone = CloneReticleFromMesh(dayMesh, "TEM2 (UE EMES18)");
            if (clone == null)
                return false;

            BuildPeriZ11DayReticleTree(clone.Tree);
            AssignReticleToMesh(dayMesh, clone);
            optic.reticleMesh = dayMesh;
            EnsureReticleMeshVisible(dayMesh);
            DisableOtherReticleMeshes(optic, dayMesh);
            return true;
        }

        private static ReticleMesh LoadPeriZ11DayDonorMesh()
        {
            if (emes_day_donor_mesh != null)
                return emes_day_donor_mesh;

            Vehicle donorVehicle = UEAssetUtil.PrewarmVanillaVehicle("MARDER1A2", new[] { "Marder1A1_rig/hull/turret/FLIR", "FLIR", "Marder1A1_rig/hull/turret/PERI Z11", "PERI Z11" });
            if (donorVehicle == null)
            {
                MelonLoader.MelonLogger.Warning("[EMES18] Thermal donor load failed: Marder donor vehicle unavailable");
                return null;
            }

            UsableOptic donorOptic = donorVehicle.transform.Find("Marder1A1_rig/hull/turret/PERI Z11")?.GetComponent<UsableOptic>();
            if (donorOptic == null)
            {
                donorOptic = donorVehicle.GetComponentsInChildren<UsableOptic>(true)
                    .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.name, "PERI Z11", StringComparison.OrdinalIgnoreCase));
            }

            if (donorOptic == null)
                return null;

            emes_day_donor_mesh = ResolveNamedReticleMesh(donorOptic, "NFOV reticle", "Reticle Mesh", "NFOV");
            return emes_day_donor_mesh;
        }

        private static EmesThermalDonor LoadThermalDonor()
        {
            if (emes_thermal_donor != null)
            {
                EnsureThermalDebugDefaultsInitialized(emes_thermal_donor);
                return emes_thermal_donor;
            }

            Vehicle donorVehicle = UEAssetUtil.PrewarmVanillaVehicle("MARDER1A2", new[] { "Marder1A1_rig/hull/turret/FLIR", "FLIR", "Marder1A1_rig/hull/turret/PERI Z11", "PERI Z11" });
            if (donorVehicle == null)
                return null;

            UsableOptic donorOptic = donorVehicle.transform.Find("Marder1A1_rig/hull/turret/FLIR")?.GetComponent<UsableOptic>();
            if (donorOptic == null)
            {
                donorOptic = donorVehicle.GetComponentsInChildren<UsableOptic>(true)
                    .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.name, "FLIR", StringComparison.OrdinalIgnoreCase));
            }

            if (donorOptic == null || donorOptic.slot == null)
            {
                MelonLoader.MelonLogger.Warning("[EMES18] Thermal donor load failed: donor FLIR optic unavailable");
                return null;
            }

            ReticleMesh wfovMesh = ResolveNamedReticleMesh(donorOptic, "Reticle Mesh WFOV", "WFOV");
            ReticleMesh nfovMesh = ResolveNamedReticleMesh(donorOptic, "Reticle Mesh", "NFOV");
            if (nfovMesh == null)
                nfovMesh = donorOptic.reticleMesh;

            if (nfovMesh == null || wfovMesh == null)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Thermal donor load failed: donor meshes missing (NFOV={nfovMesh != null}, WFOV={wfovMesh != null})");
                return null;
            }

            // Use EMES18's own FOV values, not donor's
            float wideFov = EMES_WFOV;
            float narrowFov = EMES_NFOV;

            emes_thermal_donor = new EmesThermalDonor
            {
                NfovMesh = nfovMesh,
                WfovMesh = wfovMesh,
                DefaultFov = EMES_WFOV,
                OtherFovs = new float[] { EMES_NFOV },
                WideFov = wideFov,
                NarrowFov = narrowFov
            };

            EnsureThermalDebugDefaultsInitialized(emes_thermal_donor);
            return emes_thermal_donor;
        }

        private static ReticleMesh CloneDonorReticleMesh(ReticleMesh donorMesh, Transform parent, string cloneName, bool applyThermalTuning = false, bool wide = false)
        {
            if (donorMesh == null || parent == null)
                return null;

            GameObject cloneObject = GameObject.Instantiate(donorMesh.gameObject, parent);
            cloneObject.name = cloneName;
            ReticleMesh cloneMesh = cloneObject.GetComponent<ReticleMesh>();
            if (cloneMesh == null)
                return null;

            EmesReticleClone clone = CloneReticleFromMesh(donorMesh, donorMesh.reticleSO != null ? donorMesh.reticleSO.name + " (UE)" : cloneName);
            if (clone != null)
            {
                if (applyThermalTuning)
                    ApplyThermalReticleDebugTuning(clone, wide);
                AssignReticleToMesh(cloneMesh, clone);
            }

            return cloneMesh;
        }

        private static int GetConfiguredEmesThermalWidth()
        {
            return 1024;
        }

        private static int GetConfiguredEmesThermalHeight()
        {
            return 576;
        }

        private static Material GetConfiguredEmesThermalBlitMaterial()
        {
            return flir_blit_material;
        }

        private static void ConfigureThermalZoom(CameraSlot slot, EmesThermalDonor donor)
        {
            if (slot == null || donor == null)
                return;

            slot.DefaultFov = donor.DefaultFov > 0.1f ? donor.DefaultFov : EMES_WFOV;
            slot.OtherFovs = donor.OtherFovs != null ? (float[])donor.OtherFovs.Clone() : new float[] { EMES_NFOV };
            try { slot.ForceUpdateFov(); } catch { }
        }

        private static bool ApplyThermalDonorReticles(UsableOptic optic)
        {
            if (optic == null || optic.slot == null)
                return false;

            EmesThermalDonor donor = LoadThermalDonor();
            if (donor == null)
                return FailThermalApply(optic, "[EMES18] ApplyThermalDonorReticles failed: donor unavailable");

            PulseThermalOpticInitialization(optic);
            ClearThermalPresentationOverrides(optic);

            ReticleMesh nfovMesh = ResolveEmesDayHostReticleMesh(optic) ?? optic.reticleMesh;
            if (nfovMesh == null)
                nfovMesh = optic.GetComponentsInChildren<ReticleMesh>(true).FirstOrDefault(mesh => mesh != null && !IsRangefindingReticleMesh(mesh));

            if (nfovMesh == null)
                return FailThermalApply(optic, "[EMES18] ApplyThermalDonorReticles failed: host NFOV mesh unavailable");

            EmesReticleClone narrowClone = CloneReticleFromMesh(donor.NfovMesh, donor.NfovMesh.reticleSO != null ? donor.NfovMesh.reticleSO.name + " (UE EMES18)" : "UE EMES18 NFOV");
            if (narrowClone == null)
                return FailThermalApply(optic, "[EMES18] ApplyThermalDonorReticles failed: donor NFOV clone unavailable");

            ApplyThermalReticleDebugTuning(narrowClone, false);
            AssignReticleToMesh(nfovMesh, narrowClone);

            ReticleMesh wfovMesh = CloneDonorReticleMesh(donor.WfovMesh, optic.transform, "__UE_EMES18_WFOV__", false, true);
            if (wfovMesh == null)
                return FailThermalApply(optic, $"[EMES18] ApplyThermalDonorReticles failed: cloned WFOV mesh unavailable (host NFOV={nfovMesh != null}, cloned WFOV={wfovMesh != null})");

            ConfigureThermalZoom(optic.slot, donor);

            optic.reticleMesh = nfovMesh;
            EnsureReticleMeshVisible(nfovMesh);
            EnsureReticleMeshVisible(wfovMesh);

            try
            {
                ApplyThermalFovItems(optic, nfovMesh, wfovMesh, donor.WideFov, donor.NarrowFov);
            }
            catch (Exception ex)
            {
                return FailThermalApply(optic, $"[EMES18] ApplyThermalDonorReticles failed: presentation apply threw {ex.GetType().Name}: {ex.Message}");
            }

            ClearThermalFailureSummary(optic);
            return true;
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
                return slot.FovIndex <= 0;
            }
            catch { }

            try
            {
                float currentFov = slot.CurrentFov;
                float wideFov = slot.DefaultFov;
                float narrowFov = slot.OtherFovs != null && slot.OtherFovs.Length > 0
                    ? slot.OtherFovs[0]
                    : slot.DefaultFov;

                if (wideFov < narrowFov)
                {
                    float swap = wideFov;
                    wideFov = narrowFov;
                    narrowFov = swap;
                }

                if (Mathf.Abs(currentFov - narrowFov) <= EMES_FOV_TOLERANCE) return false;
                if (Mathf.Abs(currentFov - wideFov) <= EMES_FOV_TOLERANCE) return true;
                return currentFov > (narrowFov + wideFov) * 0.5f;
            }
            catch
            {
                return true;
            }
        }

        private static void ConfigureOpticBehavior(UsableOptic optic)
        {
            if (optic == null) return;
            try
            {
                // 把热像视轴固定在炮轴，不跟随弹道修正漂移
                optic.Alignment = OpticAlignment.BoresightStabilized;
                optic.RotateAzimuth = true;
                optic.CantCorrect = true;
                optic.CantCorrectMaxSpeed = 5f;
                optic.ForceHorizontalReticleAlign = true;
                optic.ZeroOutInvalidRange = true;

                if (optic.slot != null)
                {
                    optic.slot.VibrationShakeMultiplier = 0f;
                    optic.slot.VibrationBlurScale = 0f;
                    optic.slot.fovAspect = false;
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to configure optic behavior: {ex.Message}");
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
                        f_fcs_originalRangeLimits.SetValue(fcs, new Vector2(minRange, EMES_RANGE_MAX));
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

                TrySetMember(fcs, "DisplayRangeIncrement", 10);
                TrySetMember(fcs, "<DisplayRangeIncrement>k__BackingField", 10);

                if (fcs.DefaultRange > EMES_RANGE_MAX) fcs.DefaultRange = EMES_RANGE_MAX;
                if (fcs.TargetRange > EMES_RANGE_MAX) fcs.TargetRange = EMES_RANGE_MAX;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to enforce range capability: {ex.Message}");
            }
        }

        private static void EnforceOpticRangeCapability(UsableOptic optic)
        {
            if (optic == null) return;

            try
            {
                float minRange = 0f;
                try
                {
                    Vector2 overrideLimits = optic.OverrideRangeLimits;
                    if (!float.IsNaN(overrideLimits.x))
                        minRange = Mathf.Clamp(overrideLimits.x, 0f, 500f);
                }
                catch
                {
                    try
                    {
                        if (optic.FCS != null)
                        {
                            Vector2 registered = optic.FCS.RegisteredRangeLimits;
                            if (!float.IsNaN(registered.x))
                                minRange = Mathf.Clamp(registered.x, 0f, 500f);
                        }
                    }
                    catch { }
                }

                Vector2 desiredLimits = new Vector2(minRange, EMES_RANGE_MAX);
                try
                {
                    Vector2 currentLimits = optic.OverrideRangeLimits;
                    if (!Mathf.Approximately(currentLimits.x, desiredLimits.x) || !Mathf.Approximately(currentLimits.y, desiredLimits.y))
                        optic.OverrideRangeLimits = desiredLimits;
                }
                catch
                {
                    optic.OverrideRangeLimits = desiredLimits;
                }

                bool hasOverrideRangeLimits = false;
                try { hasOverrideRangeLimits = optic.HasOverrideRangeLimits; } catch { }
                if (!hasOverrideRangeLimits && f_optic_hasOverrideRangeLimits != null)
                {
                    try { f_optic_hasOverrideRangeLimits.SetValue(optic, true); } catch { }
                }

                int desiredStep = 50;
                try
                {
                    if (optic.FCS != null && optic.FCS.RangeStep > 0)
                        desiredStep = optic.FCS.RangeStep;
                }
                catch { }

                if (optic.OverrideRangeStep != desiredStep)
                    optic.OverrideRangeStep = desiredStep;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[EMES18] Failed to enforce optic range capability: {ex.Message}");
            }
        }

        private static void EnsureEmesRangeCapability(UsableOptic optic)
        {
            if (optic == null) return;
            EnforceFcsRangeCapability(optic.FCS);
            EnforceOpticRangeCapability(optic);
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
                SetBoolMember("_fixParallaxForVectorMode", true);
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
#if DEBUG
                if (UnderdogsDebug.DEBUG_LRF)
                {
                    MelonLoader.MelonLogger.Msg($"=== {vehicleName} 激光测距改装 ===");
                    MelonLoader.MelonLogger.Msg($"原测距仪: {(fcs.OpticalRangefinder != null ? "存在" : "不存在")}");
                }
#endif

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

#if DEBUG
                if (UnderdogsDebug.DEBUG_LRF)
                    MelonLoader.MelonLogger.Msg($"激光测距已启用 | LaserOrigin={fcs.LaserOrigin?.name ?? "null"} MaxRange={fcs.MaxLaserRange}m");
#endif
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

                UnderdogsDebug.LogEMES("[EMES18][A1A3] Created dedicated B 171 takeover optic");
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
            UECommonUtil.InstallLaserPointCorrection(gpsDayOptic.FCS ?? thermalOptic.FCS, gpsDayOptic, thermalOptic);
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
            UECommonUtil.InstallLaserPointCorrection(resolvedFcs, gpsDayOptic, stockNightOptic);
        }

        // ============================================================
        // Leopard 1 EMES-18 应用函数
        // ============================================================
        // 参数 usePzb200NightOptic 决定夜间瞄准镜路径:
        //
        // PZB-200 变种 (usePzb200NightOptic = true):
        //   - Leopard 1A3A1, Leopard 1A3A3, Leopard A1A2, Leopard A1A4
        //   - 路径: "LEO1A1A1_rig/HULL/TURRET/Mantlet/--Gun Scripts--/PZB-200"
        //
        // B-171 变种 (usePzb200NightOptic = false):
        //   - Leopard 1A3, Leopard 1A3A2, Leopard A1A1, Leopard A1A3
        //   - 路径: "LEO1A1A1_rig/HULL/TURRET/--Turret Scripts--/Sights/B 171"
        //
        // 注意: 现在所有变种都通过 Leopard1SpawnConverter 转换为 A1A4 → 1A5，
        // 统一传入 usePzb200NightOptic = true
        // ============================================================
        public static bool TryApplyLeopardEmes18Suite(Vehicle vehicle, FireControlSystem fcs, string vehicleName, bool usePzb200NightOptic)
        {
            if (vehicle == null) return false;

            if (true)
                UnderdogsDebug.LogEMES($"[EMES] Applying Leopard EMES18 suite to {vehicleName}");

            var gpsDayOptic = vehicle.gameObject.transform.Find("LEO1A1A1_rig/HULL/TURRET/--Turret Scripts--/Sights/GPS")
                ?.GetComponent<UsableOptic>();
            if (gpsDayOptic == null)
            {
                gpsDayOptic = vehicle.GetComponentsInChildren<UsableOptic>(true)
                    .FirstOrDefault(optic => optic != null && optic.name.Equals("GPS", StringComparison.OrdinalIgnoreCase));
            }

            string fallbackNightOpticName = usePzb200NightOptic ? "PZB-200" : "B 171";
            string fallbackNightOpticPath = usePzb200NightOptic
                ? "LEO1A1A1_rig/HULL/TURRET/Mantlet/--Gun Scripts--/PZB-200"
                : "LEO1A1A1_rig/HULL/TURRET/--Turret Scripts--/Sights/B 171";

            UsableOptic linkedNightOptic = null;
            try { linkedNightOptic = gpsDayOptic?.slot?.LinkedNightSight?.PairedOptic; } catch { }

            if (linkedNightOptic == null)
            {
                linkedNightOptic = vehicle.gameObject.transform.Find(fallbackNightOpticPath)
                    ?.GetComponent<UsableOptic>();
            }

            if (linkedNightOptic == null)
            {
                linkedNightOptic = vehicle.GetComponentsInChildren<UsableOptic>(true)
                    .FirstOrDefault(optic => optic != null && optic.name.Equals(fallbackNightOpticName, StringComparison.OrdinalIgnoreCase));
            }

            if (gpsDayOptic == null || linkedNightOptic == null)
            {
                if (true)
                    UnderdogsDebug.LogEMESWarning($"[EMES] Leopard optics missing | veh={vehicleName} GPS={(gpsDayOptic != null)} Night={(linkedNightOptic != null)} expected={fallbackNightOpticName}");

                return false;
            }

            gpsDayOptic.enabled = true;
            linkedNightOptic.enabled = true;
            ApplyLeopardEmes18Suite(gpsDayOptic, linkedNightOptic, fcs, vehicleName);

            if (true)
                UnderdogsDebug.LogEMES($"[EMES] Leopard EMES18 suite applied ({fallbackNightOpticName} + GPS)");

            return true;
        }

        // EMES18 monitor/prefab 当前仅用于 thermal 的 UI-only 层。
        // 准星本体由 ReticleMesh 提供，prefab 不再承担 reticle 绘制职责。
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

            // UI-only donor: only the UI root is used; WFOV/NFOV/DAYOPTIC stay hidden.
            Apply("UI", new Vector3(0f, 324.14f, 0f), new Vector3(1f, 1f, 1f));
            Apply("UI/BLACKBAR", new Vector3(0f, 6.9f, 0f), new Vector3(8f, 5.76f, 1f));
            Apply("UI/AMMOCATE", new Vector3(103f, 0f, 0f), new Vector3(5f, 5f, 5f));
            Apply("UI/READYSTATE", new Vector3(-100f, 0f, 0f), new Vector3(5f, 5f, 5f));
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
                        string cn = r.name;
                        if (cn.Equals("Reticle Mesh rangefinding", StringComparison.OrdinalIgnoreCase)) continue;
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
                    UnderdogsDebug.LogEMES($"[EMES18][target+][Renderer] path={_renderers[id].path} dist={dist:F2} size={r.bounds.size} pathHit={pathHit} rtHit={rtHit} mats={_renderers[id].materialInfo}");
                    added++;
                }

                foreach (var c in searchRoot.GetComponentsInChildren<Canvas>(true))
                {
                    if (c == null) continue;
                    if (c.transform.IsChildOf(optic.transform))
                    {
                        string cn = c.name;
                        if (cn.Equals("Leopard 1 GPS canvas", StringComparison.OrdinalIgnoreCase)) continue;
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
                    UnderdogsDebug.LogEMES($"[EMES18][target+][Canvas] path={_canvases[id].path} dist={dist:F2} pathHit={pathHit} enabled={c.enabled}");
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

        public class EMES18RangeKeeper : MonoBehaviour
        {
            private const float RANGE_RESTORE_EPSILON = 10f;
            private const float RANGE_RESTORE_INTERVAL = 0.05f;
            private const float RANGE_RESTORE_WINDOW = 0.75f;

            private FireControlSystem fcs;
            private UsableOptic optic;
            private bool wasActive;
            private float pendingRestoreRange;
            private float pendingRestoreUntil;
            private float nextRestoreAttemptTime;
            private static readonly Dictionary<int, float> savedRanges = new Dictionary<int, float>();

            void Start()
            {
                optic = GetComponent<UsableOptic>();
                if (optic != null) fcs = optic.FCS;

                wasActive = IsActiveView();
                if (wasActive)
                    SaveCurrentRange();
            }

            private bool IsActiveView()
            {
                return fcs != null && optic != null && optic.slot != null && CameraSlot.ActiveInstance == optic.slot && !optic.slot.IsExterior;
            }

            private void SaveCurrentRange()
            {
                if (fcs == null) return;

                float currentRange = fcs.CurrentRange;
                if (currentRange > 0f)
                    savedRanges[fcs.GetInstanceID()] = currentRange;
            }

            private void BeginRestore()
            {
                if (fcs == null || optic == null) return;

                EnsureEmesRangeCapability(optic);

                if (!savedRanges.TryGetValue(fcs.GetInstanceID(), out float savedRange) || savedRange <= 0f)
                {
                    pendingRestoreRange = 0f;
                    return;
                }

                pendingRestoreRange = savedRange;
                pendingRestoreUntil = Time.time + RANGE_RESTORE_WINDOW;
                nextRestoreAttemptTime = 0f;
            }

            private void TryRestoreRange()
            {
                if (fcs == null || optic == null) return;
                if (pendingRestoreRange <= 0f) return;
                if (Time.time < nextRestoreAttemptTime) return;

                EnsureEmesRangeCapability(optic);

                if (Mathf.Abs(fcs.CurrentRange - pendingRestoreRange) <= RANGE_RESTORE_EPSILON)
                {
                    pendingRestoreRange = 0f;
                    SaveCurrentRange();
                    return;
                }

                try { fcs.SetRange(pendingRestoreRange, true); } catch { }
                nextRestoreAttemptTime = Time.time + RANGE_RESTORE_INTERVAL;
            }

            void Update()
            {
                if (fcs == null || optic == null || optic.slot == null) return;

                bool isActive = IsActiveView();
                if (isActive && !wasActive)
                    BeginRestore();
                wasActive = isActive;

                if (!isActive) return;

                if (pendingRestoreRange > 0f)
                {
                    if (Time.time <= pendingRestoreUntil)
                    {
                        TryRestoreRange();
                        if (Mathf.Abs(fcs.CurrentRange - pendingRestoreRange) <= RANGE_RESTORE_EPSILON)
                            pendingRestoreRange = 0f;
                    }
                    else
                    {
                        pendingRestoreRange = 0f;
                    }
                }

                if (pendingRestoreRange <= 0f)
                    SaveCurrentRange();
            }
        }

        public class EMES18Monitor : MonoBehaviour
        {
            public bool ForceDayChannel;
            public bool UiOnlyThermalMonitor;

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

                    string n2 = (info.Weapon.MetaName ?? info.Weapon.name ?? string.Empty);
                    if (n2.Equals("7.62mm Machine Gun MG3", StringComparison.OrdinalIgnoreCase))
                    {
                        coax_weapon_system = info.Weapon;
                        return;
                    }
                }
            }

            private void OnZoomChanged()
            {
                if (camera_slot == null) return;

                if (UiOnlyThermalMonitor)
                {
                    DisableLegacyMonitorReticleGraphics(transform);
                    return;
                }

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

                    string n = (activeWeapon.MetaName ?? activeWeapon.name ?? string.Empty);
                    return n.Equals("7.62mm Machine Gun MG3", StringComparison.OrdinalIgnoreCase);
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
                    if (UiOnlyThermalMonitor)
                    {
                    }
                    else if (ForceDayChannel)
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

        private static void RefreshFlirReferences()
        {
            flir_post_green = UEResourceController.GetThermalFlirPostPrefab();
            flir_blit_material = UEResourceController.GetThermalFlirBlitMaterial();
        }

        private static void RefreshResourceBindings(bool includeThermalAssets)
        {
            emes18_prefab = UEResourceController.GetEmes18MonitorPrefab();

            if (includeThermalAssets)
                RefreshFlirReferences();
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
            RefreshResourceBindings(includeThermalAssets: false);
            UnderdogsDebug.LogEMES($"[EMES18] Static assets loaded (UI prefab available={emes18_prefab != null})");
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
            RefreshResourceBindings(includeThermalAssets: mode == EmesChannelMode.Thermal);

            if (optic == null || optic.slot == null) return;

            bool isThermal = mode == EmesChannelMode.Thermal;

            EnsureLinkedSightAvailability(optic.slot);
            CleanupLegacyEmes18Presentation(optic);

            // 重入时只刷新关键配置，避免重复重建 donor/reticle
            if (HasModeMarker(optic, mode))
            {
                EnsureEmesRangeCapability(optic);
                ConfigureOpticBehavior(optic);
                if (isThermal)
                {
                    if (!ApplyThermalDonorReticles(optic))
                        MelonLoader.MelonLogger.Warning($"[EMES18] Thermal donor reticle reapply failed during refresh: {GetThermalFailureSummary(optic) ?? "unknown"}");
                    NormalizeScopeSprite(optic.slot, "emes18-thermal");
                    EnsureThermalMonitorUi(optic);
                }
                else
                {
                    ConfigureDayZoom(optic.slot);
                    if (!ApplyPeriZ11DayReticleOverride(optic))
                        MelonLoader.MelonLogger.Warning("[EMES18] Day reticle override failed; keeping stock GPS reticle where possible");
                    EnsureThermalMonitorUi(optic);
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
                        UnderdogsDebug.LogEMES($"[EMES18] Disabled stock render-texture visuals: {hiddenByRT}");
                    if (hardKilled > 0)
                        UnderdogsDebug.LogEMES($"[EMES18] Hard-killed logged PZB artifacts: {hardKilled}");

                    var suppressor = optic.GetComponent<PzbDisplaySuppressor>();
                    if (suppressor == null) suppressor = optic.gameObject.AddComponent<PzbDisplaySuppressor>();
                    suppressor.optic = optic;
                    int runtimeTargets = suppressor.DiscoverTargets();
                    if (runtimeTargets > 0)
                        UnderdogsDebug.LogEMES($"[EMES18] Runtime suppressor targets: {runtimeTargets}");
                }
            }
            else
            {
                int hiddenDayScale = HideDayStockVisuals(optic);
                if (hiddenDayScale > 0)
                    UnderdogsDebug.LogEMES($"[EMES18] Hidden day stock visuals: {hiddenDayScale}");
            }

            if (isThermal)
            {
                optic.post = null;
            }

            if (isThermal)
            {
                optic.slot.VisionType = NightVisionType.Thermal;
                optic.slot.BaseBlur = 0f;
                optic.slot.VibrationBlurScale = 0.05f;
                optic.slot.VibrationShakeMultiplier = 0.01f;
                optic.slot.VibrationPreBlur = true;
                optic.slot.OverrideFLIRResolution = true;
                optic.slot.FLIRWidth = GetConfiguredEmesThermalWidth();
                optic.slot.FLIRHeight = GetConfiguredEmesThermalHeight();
                optic.slot.CanToggleFlirPolarity = true;
                optic.slot.FLIRFilterMode = FilterMode.Point;
                Material configuredBlit = GetConfiguredEmesThermalBlitMaterial();
                if (configuredBlit != null) optic.slot.FLIRBlitMaterialOverride = configuredBlit;
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
            ConfigureOpticBehavior(optic);
            if (!isThermal)
            {
                HideDayStockVisuals(optic);
                if (!ApplyPeriZ11DayReticleOverride(optic))
                    MelonLoader.MelonLogger.Warning("[EMES18] Day reticle override failed; keeping stock GPS reticle where possible");
                NormalizeScopeSprite(optic.slot, "emes18-day");
                EnsureThermalMonitorUi(optic);
            }
            else
            {
                if (!ApplyThermalDonorReticles(optic))
                {
                    MelonLoader.MelonLogger.Warning($"[EMES18] Thermal donor reticle apply failed; keeping stock reticles where possible | {GetThermalFailureSummary(optic) ?? "unknown"}");
                    var retry = optic.GetComponent<Emes18ThermalRetryApplier>();
                    if (retry == null) retry = optic.gameObject.AddComponent<Emes18ThermalRetryApplier>();
                    retry.Optic = optic;
                    retry.LastFailureSummary = GetThermalFailureSummary(optic);
                }
                else
                {
                    UnderdogsDebug.LogEMES("[EMES18][ThermalInit] Thermal donor apply succeeded after pulse on initial attempt");
                }
                EnsureThermalMonitorUi(optic);
            }

            if (isThermal && flir_post_green != null)
            {
                GameObject post = GameObject.Instantiate(flir_post_green, optic.transform);
                post.SetActive(true);
            }
            AddModeMarker(optic, mode);

            if (optic.GetComponent<EMES18RangeKeeper>() == null)
                optic.gameObject.AddComponent<EMES18RangeKeeper>();

            UnderdogsDebug.LogEMES($"[EMES18] Applied successfully ({mode})");
        }

        /// <summary>
        /// EMES-18损毁联动组件 - 损毁时禁用激光测距
        /// </summary>
        public class EMES18DestructibleLinker : MonoBehaviour
        {
            private GHPC.Weapons.FireControlSystem _fcs;
            private GHPC.Equipment.DestructibleComponent _emesDestructible;
            private bool _destroyed;

            /// <summary>
            /// 初始化联动，订阅Destroyed事件
            /// </summary>
            public void Initialize(GHPC.Weapons.FireControlSystem fcs, GHPC.Equipment.DestructibleComponent destructible)
            {
                _fcs = fcs;
                _emesDestructible = destructible;

                if (_emesDestructible != null)
                {
                    _emesDestructible.Destroyed += OnEmesDestroyed;
                    MelonLoader.MelonLogger.Msg($"[EMES18] DestructibleLinker initialized, subscribed to Destroyed event");
                }
            }

            private void OnDestroy()
            {
                try
                {
                    if (_emesDestructible != null)
                    {
                        _emesDestructible.Destroyed -= OnEmesDestroyed;
                    }
                }
                catch { }
            }

            /// <summary>
            /// EMES-18被损毁时禁用激光测距
            /// </summary>
            private void OnEmesDestroyed(GHPC.Equipment.IDestructible destructible)
            {
                if (_destroyed) return;
                _destroyed = true;

                MelonLoader.MelonLogger.Msg("[EMES18] EMES-18 destroyed - disabling laser rangefinder");

                if (_fcs != null)
                {
                    try
                    {
                        // 禁用激光测距
                        _fcs.MaxLaserRange = 0f;
                        MelonLoader.MelonLogger.Msg("[EMES18] Laser rangefinder disabled");
                    }
                    catch (Exception ex)
                    {
                        MelonLoader.MelonLogger.Warning($"[EMES18] Failed to disable LRF: {ex.Message}");
                    }
                }
            }
        }
    }
}
