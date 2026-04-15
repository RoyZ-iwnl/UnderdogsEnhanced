using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GHPC.Camera;
using GHPC.Vehicle;
using MelonLoader;
using MelonLoader.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderdogsEnhanced
{
    public enum ThermalColorMode
    {
        WhiteHot,
        BlackHot,
        GreenHot
    }

    public sealed class ThermalConfig
    {
        public ThermalColorMode ColorMode = ThermalColorMode.GreenHot;
        public Color ColdColor = new Color(0f, 0.2f, 0f, 1f);
        public Color HotColor = new Color(0f, 1f, 0.3f, 1f);

        public ThermalConfig Clone() => new ThermalConfig
        {
            ColorMode = this.ColorMode,
            ColdColor = this.ColdColor,
            HotColor = this.HotColor
        };
    }

    internal static class UEResourceController
    {
        private const string SharedAssetsId = "SharedVanillaAssets";
        private const string OpticsAssetsId = "OpticsAssets";

        private const string AbramsRangeCanvasPath = "Turret Scripts/GPS/Optic/Abrams GPS canvas";
        private const string EmesBundleRelativePath = "UE/emes18";
        private const string M60A3TtsPrefabName = "M60A3TTS";
        private const string M60A3TtsFriendlyName = "M60A3 TTS";
        private const string FlirPostPath = "Turret Scripts/Sights/FLIR/FLIR Post Processing - Green";
        private const string FlirSlotPath = "Turret Scripts/Sights/FLIR";

        private static bool initialized;
        private static bool sharedDynamicAssetsLoaded;
        private static bool opticsStaticAssetsLoaded;
        private static bool opticsDynamicAssetsLoaded;

        private static AssetBundle emes18Bundle;
        private static Texture2D missileReticleTexture;
        private static Sprite missileReticleSprite;
        private static Texture2D whiteHotRampTexture;

        internal static GameObject LimitedLrfRangeReadoutTemplate { get; private set; }
        internal static GameObject Emes18MonitorPrefab { get; private set; }
        internal static GameObject L1a5Prefab { get; private set; }
        internal static GameObject ThermalFlirPostPrefab { get; private set; }
        internal static Material ThermalFlirBlitMaterial { get; private set; }
        internal static Material ThermalFlirBlitMaterialNoScan { get; private set; }
        internal static Material ThermalFlirWhiteBlitMaterialNoScope { get; private set; }
        internal static GameObject MissileReticleTemplate { get; private set; }

        private static readonly Dictionary<ThermalColorMode, Texture2D> thermalRampTextures = new Dictionary<ThermalColorMode, Texture2D>();
        public static ThermalConfig GlobalThermalConfig { get; private set; } = new ThermalConfig();

        internal static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;
        }

        internal static void LoadStaticAssets()
        {
            Initialize();
            TryLoadStaticAssetsStep(ref opticsStaticAssetsLoaded, OpticsAssetsId, LoadOpticsStaticAssets);
        }

        internal static void LoadDynamicAssets()
        {
            Initialize();
            TryLoadDynamicAssetsStep(ref sharedDynamicAssetsLoaded, SharedAssetsId, LoadSharedDynamicAssets);
            TryLoadDynamicAssetsStep(ref opticsDynamicAssetsLoaded, OpticsAssetsId, LoadOpticsDynamicAssets);
        }

        internal static void UnloadDynamicAssets()
        {
            Initialize();
            TryUnloadDynamicAssetsStep(ref sharedDynamicAssetsLoaded, SharedAssetsId, UnloadSharedDynamicAssets);
            TryUnloadDynamicAssetsStep(ref opticsDynamicAssetsLoaded, OpticsAssetsId, UnloadOpticsDynamicAssets);
        }

        internal static void PrewarmCommonVanillaDonors()
        {
            Initialize();
            UEAssetUtil.PrewarmVanillaVehicle("MARDER1A2", new[] { "Marder1A1_rig/hull/turret/FLIR", "FLIR", "Marder1A1_rig/hull/turret/PERI Z11", "PERI Z11" });
        }

        internal static void PrewarmMenuVanillaDonors()
        {
            Initialize();
            // 这个入口不能挂在“退战斗场景回主菜单”的场景加载钩子上。
            // 其内部 donor 预热会走同步 Addressables 加载，只能在明确、安全的预加载时机使用。
            UEAssetUtil.PrewarmVanillaVehicle("MARDER1A2", new[] { "Marder1A1_rig/hull/turret/FLIR", "FLIR", "Marder1A1_rig/hull/turret/PERI Z11", "PERI Z11" });
            UEAssetUtil.PrewarmVanillaVehicle("M60A3TTS", new[] { "Turret Scripts/Sights/FLIR", "Turret Scripts/Sights/FLIR/FLIR Post Processing - Green" });
            UEAssetUtil.PrewarmVanillaVehicle("M1IP", new[] { "Turret Scripts/GPS/Optic/Abrams GPS canvas" });
        }

        internal static void PrewarmSceneSpecificVanillaDonors(string sceneName)
        {
            Initialize();

            if (string.Equals(sceneName, "TR01_showcase", StringComparison.OrdinalIgnoreCase))
                UEAssetUtil.PrewarmVanillaVehicle("MARDER1A2", new[] { "Marder1A1_rig/hull/turret/FLIR", "FLIR", "Marder1A1_rig/hull/turret/PERI Z11", "PERI Z11" });
        }

        internal static void ReleaseVanillaDonorAssets()
        {
            Initialize();
            UEAssetUtil.ReleaseVanillaAssets();
        }

        internal static GameObject GetLimitedLrfRangeReadoutTemplate()
        {
            return RequireSharedDynamicAsset(nameof(LimitedLrfRangeReadoutTemplate)) ? LimitedLrfRangeReadoutTemplate : null;
        }

        internal static GameObject GetEmes18MonitorPrefab()
        {
            return RequireOpticsStaticAsset(nameof(Emes18MonitorPrefab)) ? Emes18MonitorPrefab : null;
        }

        internal static GameObject GetL1a5Prefab()
        {
            return RequireOpticsStaticAsset(nameof(L1a5Prefab)) ? L1a5Prefab : null;
        }

        internal static GameObject GetThermalFlirPostPrefab()
        {
            return RequireOpticsDynamicAsset(nameof(ThermalFlirPostPrefab)) ? ThermalFlirPostPrefab : null;
        }

        internal static Material GetThermalFlirBlitMaterial()
        {
            return RequireOpticsDynamicAsset(nameof(ThermalFlirBlitMaterial)) ? ThermalFlirBlitMaterial : null;
        }

        internal static Material GetThermalFlirBlitMaterialNoScan()
        {
            return RequireOpticsDynamicAsset(nameof(ThermalFlirBlitMaterialNoScan)) ? ThermalFlirBlitMaterialNoScan : null;
        }

        internal static Material GetThermalFlirWhiteBlitMaterialNoScope()
        {
            return RequireOpticsDynamicAsset(nameof(ThermalFlirWhiteBlitMaterialNoScope)) ? ThermalFlirWhiteBlitMaterialNoScope : null;
        }

        internal static GameObject GetMissileReticleTemplate()
        {
            return RequireOpticsDynamicAsset(nameof(MissileReticleTemplate)) ? MissileReticleTemplate : null;
        }

        internal static GameObject CreateMissileReticleInstance()
        {
            return RequireOpticsDynamicAsset(nameof(MissileReticleTemplate))
                ? UEAssetUtil.CloneInactive(MissileReticleTemplate, "MissileReticleCanvas")
                : null;
        }

        public static void InitThermalRampTextures()
        {
            if (thermalRampTextures.Count > 0)
                return;

            thermalRampTextures[ThermalColorMode.WhiteHot] = CreateRampTexture(new Color(0f, 0f, 0f, 1f), new Color(1f, 1f, 1f, 1f));
            thermalRampTextures[ThermalColorMode.BlackHot] = CreateRampTexture(new Color(1f, 1f, 1f, 1f), new Color(0f, 0f, 0f, 1f));
            thermalRampTextures[ThermalColorMode.GreenHot] = CreateRampTexture(new Color(0f, 0.2f, 0f, 1f), new Color(0f, 1f, 0.3f, 1f));
        }

        public static Texture2D GetThermalRampTexture(ThermalColorMode mode)
        {
            InitThermalRampTextures();
            return thermalRampTextures.TryGetValue(mode, out Texture2D tex) ? tex : null;
        }

        public static void UpdateGlobalThermalConfig(ThermalConfig config)
        {
            if (config == null)
                return;

            GlobalThermalConfig = config.Clone();
        }

        public static Material CreateThermalMaterial(Material sourceMaterial, ThermalConfig config = null)
        {
            if (sourceMaterial == null)
                return null;

            ThermalConfig cfg = config ?? GlobalThermalConfig;
            InitThermalRampTextures();

            Shader blitShader = Shader.Find("Blit (FLIR)/Blit Simple");
            if (blitShader == null)
                return null;

            Material material = new Material(blitShader);
            material.CopyPropertiesFromMaterial(sourceMaterial);

            Texture2D ramp = GetThermalRampTexture(cfg.ColorMode);
            if (ramp != null)
                material.SetTexture("_ColorRamp", ramp);

            Texture noise = sourceMaterial.GetTexture("_Noise");
            if (noise != null)
                material.SetTexture("_Noise", noise);

            material.SetTexture("_PixelCookie", null);
            material.EnableKeyword("_USE_COLOR_RAMP");
            material.EnableKeyword("_TONEMAP");
            material.EnableKeyword("_FLIR_POLARITY");
            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return material;
        }

        public static void ApplyThermalToCameraSlot(CameraSlot slot, ThermalConfig config = null)
        {
            if (slot == null)
                return;

            Material sourceMat = GetThermalFlirBlitMaterial();
            if (sourceMat == null)
                return;

            Material newMaterial = CreateThermalMaterial(sourceMat, config ?? GlobalThermalConfig);
            if (newMaterial != null)
                slot.FLIRBlitMaterialOverride = newMaterial;
        }

        public static string GetThermalColorModeName(ThermalColorMode mode)
        {
            switch (mode)
            {
                case ThermalColorMode.WhiteHot: return "白热";
                case ThermalColorMode.BlackHot: return "黑热";
                case ThermalColorMode.GreenHot: return "绿热";
                default: return "未知";
            }
        }

        private static bool TryLoadStaticAssetsStep(ref bool loaded, string id, Action loadAction)
        {
            if (loaded)
                return false;

            try
            {
                loadAction();
                loaded = true;
                MelonLogger.Msg("UE static assets loaded from module: " + id);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Assets] Static resource load failed: {id} | {ex}");
                return false;
            }
        }

        private static bool TryLoadDynamicAssetsStep(ref bool loaded, string id, Action loadAction)
        {
            if (loaded)
                return false;

            try
            {
                loadAction();
                loaded = true;
                MelonLogger.Msg("UE dynamic assets loaded from module: " + id);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Assets] Dynamic resource load failed: {id} | {ex}");
                return false;
            }
        }

        private static bool TryUnloadDynamicAssetsStep(ref bool loaded, string id, Action unloadAction)
        {
            if (!loaded)
                return false;

            try
            {
                unloadAction();
                MelonLogger.Msg("UE dynamic assets unloaded from module: " + id);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Assets] Dynamic resource unload failed: {id} | {ex}");
            }
            finally
            {
                loaded = false;
            }

            return true;
        }

        private static void LoadSharedDynamicAssets()
        {
            Vehicle m1ip = UEAssetUtil.LoadVanillaVehicle("M1IP");
            if (m1ip == null)
                throw new InvalidOperationException("M1IP PREFAB LOAD FAILED");

            Transform canvasTransform = m1ip.transform.Find(AbramsRangeCanvasPath);
            if (canvasTransform == null)
                throw new InvalidOperationException($"M1IP PATH MISS: {AbramsRangeCanvasPath}");

            LimitedLrfRangeReadoutTemplate = UEAssetUtil.CloneInactive(canvasTransform.gameObject, "ue range canvas template");
            if (LimitedLrfRangeReadoutTemplate == null)
                throw new InvalidOperationException("CLONE M1IP LRF RANGE CANVAS FAILED");

            if (LimitedLrfRangeReadoutTemplate.transform.childCount > 2)
                GameObject.Destroy(LimitedLrfRangeReadoutTemplate.transform.GetChild(2).gameObject);

            if (LimitedLrfRangeReadoutTemplate.transform.childCount > 0)
                GameObject.Destroy(LimitedLrfRangeReadoutTemplate.transform.GetChild(0).gameObject);

            LimitedLrfRangeReadoutTemplate.SetActive(false);
            LimitedLrfRangeReadoutTemplate.hideFlags = HideFlags.DontUnloadUnusedAsset;

            TextMeshProUGUI text = LimitedLrfRangeReadoutTemplate.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
            {
                text.color = new Color(1f, 0f, 0f);
                text.faceColor = new Color(1f, 0f, 0f);
                text.outlineColor = new Color32(100, 0, 0, 128);
                text.outlineWidth = 0.2f;
                text.text = string.Empty;
            }
        }

        private static void UnloadSharedDynamicAssets()
        {
            if (LimitedLrfRangeReadoutTemplate != null)
            {
                GameObject.DestroyImmediate(LimitedLrfRangeReadoutTemplate);
                LimitedLrfRangeReadoutTemplate = null;
            }
        }

        private static void LoadOpticsStaticAssets()
        {
            if (Emes18MonitorPrefab != null)
                return;

            string bundlePath = Path.Combine(MelonEnvironment.ModsDirectory, EmesBundleRelativePath);
            if (emes18Bundle == null)
                emes18Bundle = FindLoadedBundle(bundlePath);

            if (emes18Bundle == null)
                emes18Bundle = AssetBundle.LoadFromFile(bundlePath);

            if (emes18Bundle == null)
                throw new FileNotFoundException($"EMES18 bundle load failed: {bundlePath}");

            Emes18MonitorPrefab = emes18Bundle.LoadAsset<GameObject>("EMES18");
            if (Emes18MonitorPrefab == null)
                throw new InvalidOperationException("EMES18 prefab not found in bundle.");

            Emes18MonitorPrefab.hideFlags = HideFlags.DontUnloadUnusedAsset;

            L1a5Prefab = emes18Bundle.LoadAsset<GameObject>("L1A5");
            if (L1a5Prefab == null)
                MelonLogger.Warning("[TIMING][Assets] L1A5 prefab not found in bundle, falling back to EMES18");

            if (L1a5Prefab == null)
                L1a5Prefab = Emes18MonitorPrefab;

            if (L1a5Prefab != null)
                L1a5Prefab.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }

        private static void LoadOpticsDynamicAssets()
        {
            Vehicle m60a3 = LoadM60A3TtsVehicle();
            if (m60a3 == null)
                throw new InvalidOperationException("M60A3 TTS RESOURCE LOAD FAILED");

            Transform flirPost = m60a3.transform.Find(FlirPostPath);
            if (flirPost != null)
                ThermalFlirPostPrefab = flirPost.gameObject;

            CameraSlot flirSlot = m60a3.transform.Find(FlirSlotPath)?.GetComponent<CameraSlot>();
            if (flirSlot != null)
                ThermalFlirBlitMaterial = flirSlot.FLIRBlitMaterialOverride;

            if (ThermalFlirBlitMaterial != null)
                ThermalFlirBlitMaterialNoScan = CreateNoScanFlirMaterial(ThermalFlirBlitMaterial);

            if (ThermalFlirBlitMaterial != null)
                ThermalFlirWhiteBlitMaterialNoScope = CreateWhiteNoScopeFlirMaterial(ThermalFlirBlitMaterial, ref whiteHotRampTexture);

            MissileReticleTemplate = BuildMissileReticleTemplate();
        }

        private static void UnloadOpticsDynamicAssets()
        {
            ThermalFlirPostPrefab = null;
            ThermalFlirBlitMaterial = null;

            if (ThermalFlirBlitMaterialNoScan != null)
            {
                GameObject.DestroyImmediate(ThermalFlirBlitMaterialNoScan);
                ThermalFlirBlitMaterialNoScan = null;
            }

            if (ThermalFlirWhiteBlitMaterialNoScope != null)
            {
                GameObject.DestroyImmediate(ThermalFlirWhiteBlitMaterialNoScope);
                ThermalFlirWhiteBlitMaterialNoScope = null;
            }

            if (whiteHotRampTexture != null)
            {
                GameObject.DestroyImmediate(whiteHotRampTexture);
                whiteHotRampTexture = null;
            }

            if (MissileReticleTemplate != null)
            {
                GameObject.DestroyImmediate(MissileReticleTemplate);
                MissileReticleTemplate = null;
            }

            if (missileReticleSprite != null)
            {
                GameObject.DestroyImmediate(missileReticleSprite);
                missileReticleSprite = null;
            }

            if (missileReticleTexture != null)
            {
                GameObject.DestroyImmediate(missileReticleTexture);
                missileReticleTexture = null;
            }
        }

        private static bool RequireSharedDynamicAsset(string assetName)
        {
            return RequireAssetsLoaded(SharedAssetsId, assetName, sharedDynamicAssetsLoaded, true);
        }

        private static bool RequireOpticsStaticAsset(string assetName)
        {
            return RequireAssetsLoaded(OpticsAssetsId, assetName, opticsStaticAssetsLoaded, false);
        }

        private static bool RequireOpticsDynamicAsset(string assetName)
        {
            return RequireAssetsLoaded(OpticsAssetsId, assetName, opticsDynamicAssetsLoaded, true);
        }

        private static bool RequireAssetsLoaded(string id, string assetName, bool ready, bool dynamic)
        {
            Initialize();

            if (ready)
                return true;

            string phase = dynamic ? "dynamic" : "static";
            MelonLogger.Warning($"[Assets] Requested {phase} asset before module load: {id}.{assetName}");
            return false;
        }

        private static Texture2D CreateRampTexture(Color cold, Color hot, int width = 256)
        {
            Texture2D tex = new Texture2D(width, 1, TextureFormat.ARGB32, false, true);
            Color[] colors = new Color[width];

            for (int i = 0; i < width; i++)
            {
                float t = i / (float)(width - 1);
                colors[i] = Color.Lerp(cold, hot, t);
            }

            tex.SetPixels(colors);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        private static AssetBundle FindLoadedBundle(string expectedPath)
        {
            string normalizedExpected = expectedPath.Replace('\\', '/').ToLowerInvariant();
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null)
                    continue;

                string name = bundle.name?.Replace('\\', '/').ToLowerInvariant();
                if (name == normalizedExpected)
                    return bundle;
            }

            return null;
        }

        private static Vehicle LoadM60A3TtsVehicle()
        {
            Vehicle donor = UEAssetUtil.LoadVanillaVehicle(M60A3TtsPrefabName);
            if (donor != null)
                return donor;

            donor = UEAssetUtil.LoadVanillaVehicle(M60A3TtsFriendlyName);
            if (donor != null)
                return donor;

            return Resources.FindObjectsOfTypeAll<Vehicle>().FirstOrDefault(v =>
                v != null &&
                (string.Equals(v.name, M60A3TtsPrefabName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(v.name, M60A3TtsFriendlyName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(v.FriendlyName, M60A3TtsFriendlyName, StringComparison.OrdinalIgnoreCase)));
        }

        private static Material CreateNoScanFlirMaterial(Material source)
        {
            Shader blitShader = Shader.Find("Blit (FLIR)/Blit Simple");
            if (blitShader == null)
                return null;

            Material material = new Material(blitShader);
            material.CopyPropertiesFromMaterial(source);
            material.SetTexture("_PixelCookie", null);
            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return material;
        }

        private static Material CreateWhiteNoScopeFlirMaterial(Material source, ref Texture2D rampTexture)
        {
            Shader blitShader = Shader.Find("Blit (FLIR)/Blit Simple");
            if (blitShader == null || source == null)
                return null;

            if (rampTexture == null)
                rampTexture = CreateWhiteHotRampTexture();

            Material material = new Material(blitShader);
            Texture noise = source.GetTexture("_Noise");
            if (noise != null)
                material.SetTexture("_Noise", noise);

            material.SetTexture("_ColorRamp", rampTexture);
            material.SetTexture("_PixelCookie", null);
            material.EnableKeyword("_USE_COLOR_RAMP");
            material.EnableKeyword("_TONEMAP");
            material.EnableKeyword("_FLIR_POLARITY");
            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return material;
        }

        private static Texture2D CreateWhiteHotRampTexture()
        {
            Texture2D texture = new Texture2D(2, 1, TextureFormat.ARGB32, false, true);
            texture.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));
            texture.SetPixel(1, 0, new Color(1f, 1f, 1f, 1f));
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply();
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return texture;
        }

        private static GameObject BuildMissileReticleTemplate()
        {
            GameObject go = new GameObject("MissileReticleCanvas");
            go.hideFlags = HideFlags.DontUnloadUnusedAsset;

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = false;
            canvas.sortingOrder = 0;
            canvas.planeDistance = 1f;
            canvas.worldCamera = null;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<GraphicRaycaster>();

            CreateMissileReticleUi(go.transform);
            go.SetActive(false);
            return go;
        }

        private static void CreateMissileReticleUi(Transform parent)
        {
            GameObject reticleObject = new GameObject("Reticle");
            RectTransform reticleRect = reticleObject.AddComponent<RectTransform>();
            reticleRect.SetParent(parent, false);
            reticleRect.anchorMin = new Vector2(0.5f, 0.5f);
            reticleRect.anchorMax = new Vector2(0.5f, 0.5f);
            reticleRect.pivot = new Vector2(0.5f, 0.5f);
            reticleRect.anchoredPosition = Vector2.zero;
            reticleRect.sizeDelta = new Vector2(400f, 400f);

            GameObject centerDot = new GameObject("CenterDot");
            RectTransform dotRect = centerDot.AddComponent<RectTransform>();
            dotRect.SetParent(reticleRect, false);
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.anchoredPosition = Vector2.zero;
            dotRect.sizeDelta = new Vector2(4f, 4f);

            Image dotImage = centerDot.AddComponent<Image>();
            dotImage.color = new Color(0f, 0.6f, 0f, 0.9f);

            GameObject circle = new GameObject("InnerRing");
            RectTransform circleRect = circle.AddComponent<RectTransform>();
            circleRect.SetParent(reticleRect, false);
            circleRect.anchorMin = new Vector2(0.5f, 0.5f);
            circleRect.anchorMax = new Vector2(0.5f, 0.5f);
            circleRect.pivot = new Vector2(0.5f, 0.5f);
            circleRect.anchoredPosition = Vector2.zero;
            circleRect.sizeDelta = new Vector2(100f, 100f);

            Image circleImage = circle.AddComponent<Image>();
            circleImage.sprite = CreateMissileReticleSprite(50f, 2f);
            circleImage.type = Image.Type.Simple;
            circleImage.color = new Color(0f, 0.6f, 0f, 0.8f);
        }

        private static Sprite CreateMissileReticleSprite(float radius, float thickness)
        {
            if (missileReticleSprite != null)
                return missileReticleSprite;

            const int size = 256;
            missileReticleTexture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color[] colors = new Color[size * size];

            Vector2 center = new Vector2(size / 2f, size / 2f);
            float outerRadius = size / 2f;
            float innerRadius = outerRadius - (thickness / radius) * outerRadius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    colors[y * size + x] = distance <= outerRadius && distance >= innerRadius ? Color.white : Color.clear;
                }
            }

            missileReticleTexture.SetPixels(colors);
            missileReticleTexture.Apply();
            missileReticleTexture.hideFlags = HideFlags.DontUnloadUnusedAsset;

            missileReticleSprite = Sprite.Create(missileReticleTexture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            missileReticleSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return missileReticleSprite;
        }
    }
}
