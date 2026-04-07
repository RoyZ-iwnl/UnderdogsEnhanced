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
    /// <summary>
    /// 热成像颜色模式
    /// </summary>
    public enum ThermalColorMode
    {
        WhiteHot,   // 白热
        BlackHot,   // 黑热
        GreenHot    // 绿热
    }

    /// <summary>
    /// 热成像配置
    /// </summary>
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
        // 未来的资源模块应遵循此模式：
        // 1. 在 Initialize() 中注册模块一次。
        // 2. 在 LoadStaticAssets() 中放置资源包加载/预制件查找/不可变共享引用。
        // 3. 在 LoadDynamicAssets() 中放置来自捐赠源的运行时引用或克隆的仅运行时对象。
        // 4. 在 UnloadDynamicAssets() 中销毁仅运行时对象。
        // 5. 保持 getter 无副作用：场景生命周期先加载，调用者仅消费结果。
        private static readonly Dictionary<string, UEResourceModule> modules = new Dictionary<string, UEResourceModule>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<UEResourceModule> moduleLoadOrder = new List<UEResourceModule>();
        private static bool initialized = false;
        private static UESharedVanillaAssetsModule sharedVanillaAssets;
        private static UEOpticsAssetsModule opticsAssets;

        // 热成像渐变纹理缓存
        private static readonly Dictionary<ThermalColorMode, Texture2D> thermalRampTextures = new Dictionary<ThermalColorMode, Texture2D>();
        public static ThermalConfig GlobalThermalConfig { get; private set; } = new ThermalConfig();

        internal static void Initialize()
        {
            if (initialized) return;

            sharedVanillaAssets = new UESharedVanillaAssetsModule();
            opticsAssets = new UEOpticsAssetsModule();

            RegisterModule(sharedVanillaAssets);
            RegisterModule(opticsAssets);
            initialized = true;
        }

        internal static void LoadStaticAssets()
        {
            Initialize();

            foreach (UEResourceModule module in moduleLoadOrder)
            {
                if (module != null && module.TryLoadStaticAssets())
                    MelonLogger.Msg("UE static assets loaded from module: " + module.Id);
            }
        }

        internal static void LoadDynamicAssets()
        {
            Initialize();

            foreach (UEResourceModule module in moduleLoadOrder)
            {
                if (module != null && module.TryLoadDynamicAssets())
                    MelonLogger.Msg("UE dynamic assets loaded from module: " + module.Id);
            }
        }

        internal static void UnloadDynamicAssets()
        {
            Initialize();

            foreach (UEResourceModule module in moduleLoadOrder)
            {
                if (module != null && module.TryUnloadDynamicAssets())
                    MelonLogger.Msg("UE dynamic assets unloaded from module: " + module.Id);
            }
        }

        internal static void PrewarmCommonVanillaDonors()
        {
            Initialize();

            UEAssetUtil.PrewarmVanillaVehicle("MARDER1A2", new[] { "Marder1A1_rig/hull/turret/FLIR", "FLIR", "Marder1A1_rig/hull/turret/PERI Z11", "PERI Z11" });
        }

        internal static void PrewarmMenuVanillaDonors()
        {
            Initialize();

            UEAssetUtil.PrewarmVanillaVehicle("MARDER1A2", new[] { "Marder1A1_rig/hull/turret/FLIR", "FLIR", "Marder1A1_rig/hull/turret/PERI Z11", "PERI Z11" });
            UEAssetUtil.PrewarmVanillaVehicle("M60A3TTS", new[] { "Turret Scripts/Sights/FLIR", "Turret Scripts/Sights/FLIR/FLIR Post Processing - Green" });
            UEAssetUtil.PrewarmVanillaVehicle("M1IP", new[] { "Turret Scripts/GPS/Optic/Abrams GPS canvas" });
        }

        internal static void PrewarmSceneSpecificVanillaDonors(string sceneName)
        {
            Initialize();

            if (string.Equals(sceneName, "TR01_showcase", StringComparison.OrdinalIgnoreCase))
            {
                UEAssetUtil.PrewarmVanillaVehicle("MARDER1A2", new[] { "Marder1A1_rig/hull/turret/FLIR", "FLIR", "Marder1A1_rig/hull/turret/PERI Z11", "PERI Z11" });
            }
        }

        internal static void ReleaseVanillaDonorAssets()
        {
            Initialize();
            UEAssetUtil.ReleaseVanillaAssets();
        }

        internal static GameObject GetLimitedLrfRangeReadoutTemplate()
        {
            UESharedVanillaAssetsModule module = RequireDynamicModule(sharedVanillaAssets, "LimitedLrfRangeReadoutTemplate");
            return module != null ? module.LimitedLrfRangeReadoutTemplate : null;
        }

        internal static GameObject GetEmes18MonitorPrefab()
        {
            UEOpticsAssetsModule module = RequireStaticModule(opticsAssets, "Emes18MonitorPrefab");
            return module != null ? module.Emes18MonitorPrefab : null;
        }

        internal static GameObject GetL1a5Prefab()
        {
            UEOpticsAssetsModule module = RequireStaticModule(opticsAssets, "L1a5Prefab");
            return module != null ? module.L1a5Prefab : null;
        }

        internal static GameObject GetThermalFlirPostPrefab()
        {
            UEOpticsAssetsModule module = RequireDynamicModule(opticsAssets, "ThermalFlirPostPrefab");
            return module != null ? module.ThermalFlirPostPrefab : null;
        }

        internal static Material GetThermalFlirBlitMaterial()
        {
            UEOpticsAssetsModule module = RequireDynamicModule(opticsAssets, "ThermalFlirBlitMaterial");
            return module != null ? module.ThermalFlirBlitMaterial : null;
        }

        internal static Material GetThermalFlirBlitMaterialNoScan()
        {
            UEOpticsAssetsModule module = RequireDynamicModule(opticsAssets, "ThermalFlirBlitMaterialNoScan");
            return module != null ? module.ThermalFlirBlitMaterialNoScan : null;
        }

        internal static Material GetThermalFlirWhiteBlitMaterialNoScope()
        {
            UEOpticsAssetsModule module = RequireDynamicModule(opticsAssets, "ThermalFlirWhiteBlitMaterialNoScope");
            return module != null ? module.ThermalFlirWhiteBlitMaterialNoScope : null;
        }

        internal static GameObject GetMissileReticleTemplate()
        {
            UEOpticsAssetsModule module = RequireDynamicModule(opticsAssets, "MissileReticleTemplate");
            return module != null ? module.MissileReticleTemplate : null;
        }

        internal static GameObject CreateMissileReticleInstance()
        {
            UEOpticsAssetsModule module = RequireDynamicModule(opticsAssets, "MissileReticleTemplate");
            return module != null ? module.CreateMissileReticleInstance() : null;
        }

        #region Thermal Vision

        public static void InitThermalRampTextures()
        {
            if (thermalRampTextures.Count > 0) return;

            thermalRampTextures[ThermalColorMode.WhiteHot] = CreateRampTexture(new Color(0f, 0f, 0f, 1f), new Color(1f, 1f, 1f, 1f));
            thermalRampTextures[ThermalColorMode.BlackHot] = CreateRampTexture(new Color(1f, 1f, 1f, 1f), new Color(0f, 0f, 0f, 1f));
            thermalRampTextures[ThermalColorMode.GreenHot] = CreateRampTexture(new Color(0f, 0.2f, 0f, 1f), new Color(0f, 1f, 0.3f, 1f));
        }

        public static Texture2D GetThermalRampTexture(ThermalColorMode mode)
        {
            InitThermalRampTextures();
            return thermalRampTextures.TryGetValue(mode, out var tex) ? tex : null;
        }

        public static void UpdateGlobalThermalConfig(ThermalConfig config)
        {
            if (config == null) return;
            GlobalThermalConfig = config.Clone();
        }

        public static Material CreateThermalMaterial(Material sourceMaterial, ThermalConfig config = null)
        {
            if (sourceMaterial == null) return null;

            var cfg = config ?? GlobalThermalConfig;
            InitThermalRampTextures();

            Shader blitShader = Shader.Find("Blit (FLIR)/Blit Simple");
            if (blitShader == null) return null;

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
            if (slot == null) return;

            Material sourceMat = GetThermalFlirBlitMaterial();
            if (sourceMat == null) return;

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

        #endregion

        private static void RegisterModule(UEResourceModule module)
        {
            if (module == null || string.IsNullOrEmpty(module.Id))
                return;

            if (!modules.ContainsKey(module.Id))
                moduleLoadOrder.Add(module);

            modules[module.Id] = module;
        }

        private static T RequireStaticModule<T>(T module, string assetName) where T : UEResourceModule
        {
            return RequireModule(module, assetName, requireDynamicAssets: false);
        }

        private static T RequireDynamicModule<T>(T module, string assetName) where T : UEResourceModule
        {
            return RequireModule(module, assetName, requireDynamicAssets: true);
        }

        private static T RequireModule<T>(T module, string assetName, bool requireDynamicAssets) where T : UEResourceModule
        {
            Initialize();

            if (module == null)
                return null;

            bool ready = requireDynamicAssets ? module.DynamicAssetsLoaded : module.StaticAssetsLoaded;
            if (ready)
                return module;

            string phase = requireDynamicAssets ? "dynamic" : "static";
            MelonLogger.Warning($"[Assets] Requested {phase} asset before module load: {module.Id}.{assetName}");
            return null;
        }
    }

    internal sealed class UESharedVanillaAssetsModule : UEResourceModule
    {
        private const string AbramsRangeCanvasPath = "Turret Scripts/GPS/Optic/Abrams GPS canvas";

        internal GameObject LimitedLrfRangeReadoutTemplate { get; private set; }

        internal UESharedVanillaAssetsModule() : base("SharedVanillaAssets")
        {
        }

        protected override void LoadDynamicAssets()
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

        protected override void UnloadDynamicAssets()
        {
            if (LimitedLrfRangeReadoutTemplate != null)
            {
                GameObject.DestroyImmediate(LimitedLrfRangeReadoutTemplate);
                LimitedLrfRangeReadoutTemplate = null;
            }
        }
    }

    internal sealed class UEOpticsAssetsModule : UEResourceModule
    {
        private const string EmesBundleRelativePath = "UE/emes18";
        private const string M60A3TtsPrefabName = "M60A3TTS";
        private const string M60A3TtsFriendlyName = "M60A3 TTS";
        private const string FlirPostPath = "Turret Scripts/Sights/FLIR/FLIR Post Processing - Green";
        private const string FlirSlotPath = "Turret Scripts/Sights/FLIR";

        private AssetBundle emes18Bundle;
        private Texture2D missileReticleTexture;
        private Sprite missileReticleSprite;

        // 当前 EMES18 运行时链路中，这个 prefab 仅作为 thermal UI-only donor 使用。
        // 准星本体仍由原生 ReticleMesh/thermal donor 提供。
        internal GameObject Emes18MonitorPrefab { get; private set; }
        internal GameObject L1a5Prefab { get; private set; }
        internal GameObject ThermalFlirPostPrefab { get; private set; }
        internal Material ThermalFlirBlitMaterial { get; private set; }
        internal Material ThermalFlirBlitMaterialNoScan { get; private set; }
        internal Material ThermalFlirWhiteBlitMaterialNoScope { get; private set; }
        internal GameObject MissileReticleTemplate { get; private set; }

        private Texture2D whiteHotRampTexture;

        internal UEOpticsAssetsModule() : base("OpticsAssets")
        {
        }

        protected override void LoadStaticAssets()
        {
            if (Emes18MonitorPrefab != null) return;

            string bundlePath = Path.Combine(MelonEnvironment.ModsDirectory, EmesBundleRelativePath);
            if (emes18Bundle == null)
                emes18Bundle = FindLoadedBundle(bundlePath);

            if (emes18Bundle == null)
                emes18Bundle = AssetBundle.LoadFromFile(bundlePath);

            if (emes18Bundle == null)
                throw new FileNotFoundException($"EMES18 bundle load failed: {bundlePath}");

            // 加载 EMES18 预制件（光学系统用）
            Emes18MonitorPrefab = emes18Bundle.LoadAsset<GameObject>("EMES18");
            if (Emes18MonitorPrefab == null)
                throw new InvalidOperationException("EMES18 prefab not found in bundle.");

            Emes18MonitorPrefab.hideFlags = HideFlags.DontUnloadUnusedAsset;

            // 加载 L1A5 预制件（模型用，同一bundle中的另一个根节点）
            L1a5Prefab = emes18Bundle.LoadAsset<GameObject>("L1A5");
            if (L1a5Prefab == null)
                MelonLogger.Warning("[UE][Assets] L1A5 prefab not found in bundle, falling back to EMES18");

            if (L1a5Prefab == null)
                L1a5Prefab = Emes18MonitorPrefab; // fallback

            if (L1a5Prefab != null)
            {
                L1a5Prefab.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }

        }

        protected override void LoadDynamicAssets()
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

        protected override void UnloadDynamicAssets()
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

        internal GameObject CreateMissileReticleInstance()
        {
            return UEAssetUtil.CloneInactive(MissileReticleTemplate, "MissileReticleCanvas");
        }

        private static AssetBundle FindLoadedBundle(string expectedPath)
        {
            try
            {
                string normalizedExpected = expectedPath.Replace('\\', '/').ToLowerInvariant();
                foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (bundle == null) continue;
                    string name = null;
                    try { name = bundle.name; } catch { }
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    string normalizedName = name.Replace('\\', '/').ToLowerInvariant();
                    if (normalizedName == normalizedExpected ||
                        normalizedName.EndsWith("/emes18", StringComparison.Ordinal) ||
                        normalizedName.EndsWith("/emes18.unity3d", StringComparison.Ordinal) ||
                        normalizedName.Contains("/ue/emes18"))
                        return bundle;
                }
            }
            catch { }

            return null;
        }

        private static Vehicle LoadM60A3TtsVehicle()
        {
            Vehicle donor = UEAssetUtil.LoadVanillaVehicle(M60A3TtsPrefabName);
            if (donor != null) return donor;

            donor = UEAssetUtil.LoadVanillaVehicle(M60A3TtsFriendlyName);
            if (donor != null) return donor;

            return Resources.FindObjectsOfTypeAll<Vehicle>().FirstOrDefault(v =>
                v != null &&
                (string.Equals(v.name, M60A3TtsPrefabName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(v.name, M60A3TtsFriendlyName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(v.FriendlyName, M60A3TtsFriendlyName, StringComparison.OrdinalIgnoreCase)));
        }

        private static Material CreateNoScanFlirMaterial(Material source)
        {
            Shader blitShader = Shader.Find("Blit (FLIR)/Blit Simple");
            if (blitShader == null) return null;

            Material material = new Material(blitShader);
            material.CopyPropertiesFromMaterial(source);
            material.SetTexture("_PixelCookie", null);
            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return material;
        }

        private static Material CreateWhiteNoScopeFlirMaterial(Material source, ref Texture2D rampTexture)
        {
            Shader blitShader = Shader.Find("Blit (FLIR)/Blit Simple");
            if (blitShader == null || source == null) return null;

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

        private GameObject BuildMissileReticleTemplate()
        {
            var go = new GameObject("MissileReticleCanvas");
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

        private void CreateMissileReticleUi(Transform parent)
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

        private Sprite CreateMissileReticleSprite(float radius, float thickness)
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
                    colors[y * size + x] = (distance <= outerRadius && distance >= innerRadius) ? Color.white : Color.clear;
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
