using GHPC.Camera;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;
using GHPC.Equipment.Optics;
using FMODUnity;

namespace UnderdogsEnhanced
{
    // 持久 MonoBehaviour，专门用来跑不能在销毁对象上执行的 coroutine
    public class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner _instance;
        public static CoroutineRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("UE_CoroutineRunner");
                    GameObject.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CoroutineRunner>();
                }
                return _instance;
            }
        }

        public static void Run(IEnumerator routine) => Instance.StartCoroutine(routine);
    }

    // 导弹热成像资源管理器
    public static class MissileThermalAssets
    {
        public static bool initialized = false;
        public static GameObject flirPostPrefab;
        public static Material flirBlitMaterialOriginal; // 原始扫描线版本
        public static Material flirBlitMaterialNoScan; // 无扫描线版本
        public static GameObject reticleCanvas;

        // FLIR分辨率设置（更低的分辨率产生更模糊的效果）
        public const int FLIR_LOW_WIDTH = 320;
        public const int FLIR_LOW_HEIGHT = 180;
        public const int FLIR_HIGH_WIDTH = 1024;
        public const int FLIR_HIGH_HEIGHT = 576;

        public static int GetFlirWidth()
        {
            bool highRes = UnderdogsEnhancedMod.bmp1_mclos_flir_high_res != null && UnderdogsEnhancedMod.bmp1_mclos_flir_high_res.Value;
            return highRes ? FLIR_HIGH_WIDTH : FLIR_LOW_WIDTH;
        }

        public static int GetFlirHeight()
        {
            bool highRes = UnderdogsEnhancedMod.bmp1_mclos_flir_high_res != null && UnderdogsEnhancedMod.bmp1_mclos_flir_high_res.Value;
            return highRes ? FLIR_HIGH_HEIGHT : FLIR_LOW_HEIGHT;
        }

        public static Material GetConfiguredBlitMaterial()
        {
            bool removeScanline = UnderdogsEnhancedMod.bmp1_mclos_flir_no_scanline == null || UnderdogsEnhancedMod.bmp1_mclos_flir_no_scanline.Value;
            if (removeScanline)
                return flirBlitMaterialNoScan != null ? flirBlitMaterialNoScan : flirBlitMaterialOriginal;
            return flirBlitMaterialOriginal != null ? flirBlitMaterialOriginal : flirBlitMaterialNoScan;
        }

        public static void Init()
        {
            if (initialized) return;
            flirBlitMaterialOriginal = null;

            // 从M60A3 TTS获取FLIR资源
            var m60a3 = Resources.FindObjectsOfTypeAll<GHPC.Vehicle.Vehicle>()
                .FirstOrDefault(v => v.name == "M60A3 TTS");

            if (m60a3 != null)
            {
                var flirPost = m60a3.transform.Find("Turret Scripts/Sights/FLIR/FLIR Post Processing - Green");
                if (flirPost != null)
                {
                    flirPostPrefab = flirPost.gameObject;
                }

                var flirSlot = m60a3.transform.Find("Turret Scripts/Sights/FLIR");
                if (flirSlot != null)
                {
                    var camSlot = flirSlot.GetComponent<CameraSlot>();
                    if (camSlot != null)
                    {
                        flirBlitMaterialOriginal = camSlot.FLIRBlitMaterialOverride;
                    }
                }
            }

            // 备选：从任何热成像CameraSlot获取
            if (flirPostPrefab == null || flirBlitMaterialOriginal == null)
            {
                var allCamSlots = Resources.FindObjectsOfTypeAll<CameraSlot>();
                foreach (var slot in allCamSlots)
                {
                    if (slot.VisionType == NightVisionType.Thermal || slot.FLIRBlitMaterialOverride != null)
                    {
                        if (flirBlitMaterialOriginal == null && slot.FLIRBlitMaterialOverride != null)
                        {
                            flirBlitMaterialOriginal = slot.FLIRBlitMaterialOverride;
                        }

                        if (flirPostPrefab == null)
                        {
                            var flirPost = slot.transform.Find("FLIR Post Processing - Green");
                            if (flirPost != null)
                            {
                                flirPostPrefab = flirPost.gameObject;
                            }
                            else
                            {
                                flirPostPrefab = slot.gameObject;
                            }
                        }

                        if (flirBlitMaterialOriginal != null) break;
                    }
                }
            }

            // 创建无扫描线的FLIR材质
            if (flirBlitMaterialOriginal != null)
            {
                Shader blitShader = Shader.Find("Blit (FLIR)/Blit Simple");
                if (blitShader != null)
                {
                    flirBlitMaterialNoScan = new Material(blitShader);
                    flirBlitMaterialNoScan.CopyPropertiesFromMaterial(flirBlitMaterialOriginal);
                    flirBlitMaterialNoScan.SetTexture("_PixelCookie", null);
                }
            }

            // 创建准星Canvas
            CreateReticleCanvas();

            initialized = true;

            if (UnderdogsEnhancedMod.DEBUG_MODE)
            {
                MelonLogger.Msg($"[BMP-1 导弹热成像] 资源初始化完成: FLIR={flirPostPrefab != null}, BlitMatOriginal={flirBlitMaterialOriginal != null}, BlitMatNoScan={flirBlitMaterialNoScan != null}");
            }
        }

        // 创建独立的准星实例（每个导弹独立，避免多准星问题）
        public static GameObject CreateReticleInstance()
        {
            var go = new GameObject("MissileReticleCanvas");
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<GraphicRaycaster>();
            CreateReticleUI(go.transform);
            return go;
        }

        static void CreateReticleCanvas()
        {
            // 创建准星UI Canvas
            reticleCanvas = new GameObject("MissileReticleCanvas");
            reticleCanvas.hideFlags = HideFlags.DontUnloadUnusedAsset;

            Canvas canvas = reticleCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = reticleCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            reticleCanvas.AddComponent<GraphicRaycaster>();

            // 创建准星图案
            CreateReticleUI(reticleCanvas.transform);

            reticleCanvas.SetActive(false);
        }

        static void CreateReticleUI(Transform parent)
        {
            // 创建准星容器
            GameObject reticleObj = new GameObject("Reticle");
            RectTransform rt = reticleObj.AddComponent<RectTransform>();
            rt.SetParent(parent);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(400, 400);

            // 中心点
            GameObject centerDot = new GameObject("CenterDot");
            RectTransform dotRt = centerDot.AddComponent<RectTransform>();
            dotRt.SetParent(rt);
            dotRt.anchorMin = new Vector2(0.5f, 0.5f);
            dotRt.anchorMax = new Vector2(0.5f, 0.5f);
            dotRt.pivot = new Vector2(0.5f, 0.5f);
            dotRt.anchoredPosition = Vector2.zero;
            dotRt.sizeDelta = new Vector2(4, 4);

            Image dotImg = centerDot.AddComponent<Image>();
            dotImg.color = new Color(0f, 0.6f, 0f, 0.9f); // 黑色

            // 内圈
            CreateCircle(rt, "InnerRing", 50f, 2f, new Color(0f, 0.6f, 0f, 0.8f));
        }

        static void CreateCircle(Transform parent, string name, float radius, float thickness, Color color)
        {
            GameObject circle = new GameObject(name);
            RectTransform rt = circle.AddComponent<RectTransform>();
            rt.SetParent(parent);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(radius * 2, radius * 2);

            Image img = circle.AddComponent<Image>();
            img.sprite = CreateCircleSprite(radius, thickness);
            img.type = Image.Type.Simple;
            img.color = color;
        }

        static void CreateCrosshairLine(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject line = new GameObject(name);
            RectTransform rt = line.AddComponent<RectTransform>();
            rt.SetParent(parent);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = line.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.8f); // 黑色
        }

        static Sprite CreateCircleSprite(float radius, float thickness)
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size);
            Color[] colors = new Color[size * size];

            Vector2 center = new Vector2(size / 2f, size / 2f);
            float outerRadius = size / 2f;
            float innerRadius = outerRadius - (thickness / radius) * outerRadius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= outerRadius && dist >= innerRadius)
                    {
                        colors[y * size + x] = Color.white;
                    }
                    else
                    {
                        colors[y * size + x] = Color.clear;
                    }
                }
            }

            tex.SetPixels(colors);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }

    public class MissileCameraFollow : MonoBehaviour
    {
        private static readonly FieldInfo f_cm_allCamSlots = typeof(CameraManager).GetField("_allCamSlots", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_cm_exteriorMode = typeof(CameraManager).GetProperty("ExteriorMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_cm_exteriorMode = typeof(CameraManager).GetField("<ExteriorMode>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? typeof(CameraManager).GetField("_exteriorMode", BindingFlags.Instance | BindingFlags.NonPublic);

        public static float MissileAudioVolume = BMP1MCLOSAmmo.DEFAULT_MISSILE_AUDIO_VOLUME;

        private Camera _cam;
        private CameraManager _cm;
        private Rigidbody _rb;
        private bool _restored = false;
        private CameraSlot _prevSlot;
        private bool _prevHadActiveSlot;
        private bool _prevExteriorMode;
        private bool _restoreOpticOnExit;
        private Vector3 _camOriginalLocalPos;
        private Quaternion _camOriginalLocalRot;
        private float _camOriginalFov;

        // 热成像相关
        private GameObject _thermalSlotObj;
        private CameraSlot _thermalSlot;
        private SimpleNightVision _snv;

        // 每个实例独立的准星
        private GameObject _reticleCanvas;

        // 音效控制
        private AudioSource[] _audioSources;
        private float[] _originalVolumes;
        private StudioEventEmitter[] _fmodEmitters;

        public GameObject opticNode;

        void Start()
        {
            if (UnderdogsEnhancedMod.DEBUG_MODE)
                MelonLogger.Msg("[BMP-1 MCLOS] Start() 被调用");

            try
            {
                // 初始化热成像资源
                MissileThermalAssets.Init();

                if (UnderdogsEnhancedMod.DEBUG_MODE)
                    MelonLogger.Msg("[BMP-1 MCLOS] MissileThermalAssets.Init() 完成");

                _cam = CameraManager.MainCam;
                if (_cam == null) {
                    MelonLogger.Warning("[BMP-1 MCLOS] MainCam 为空，销毁组件");
                    Destroy(this);
                    return;
                }

                // 安全检查：确保摄像机对象仍然有效
                try {
                    string camName = _cam.name;
                    if (string.IsNullOrEmpty(camName)) {
                        MelonLogger.Warning("[BMP-1 MCLOS] MainCam 名称无效，销毁组件");
                        Destroy(this);
                        return;
                    }
                    if (UnderdogsEnhancedMod.DEBUG_MODE)
                        MelonLogger.Msg($"[BMP-1 MCLOS] MainCam 获取成功: {camName}");
                }
                catch (System.Exception) {
                    MelonLogger.Warning("[BMP-1 MCLOS] MainCam 对象已销毁或无效，销毁组件");
                    Destroy(this);
                    return;
                }

                _cm = CameraManager.Instance;
                _rb = GetComponent<Rigidbody>();
                _prevSlot = CameraSlot.ActiveInstance;
                _prevHadActiveSlot = _prevSlot != null;
                _prevExteriorMode = _cm != null && _cm.ExteriorMode;
                _restoreOpticOnExit = opticNode != null && opticNode.activeSelf && _prevSlot != null && !_prevSlot.IsExterior && !_prevExteriorMode;

                // 保存摄像机原始本地位置和旋转
                _camOriginalLocalPos = _cam.transform.localPosition;
                _camOriginalLocalRot = _cam.transform.localRotation;
                _camOriginalFov = _cam.fieldOfView;

                // 外部模式会让相机跟随系统抢控制权，导致导弹视角变成追尾第三人称
                TrySetExteriorMode(_cm, false);

                if (opticNode != null) opticNode.SetActive(false);

                MissileCameraActive.IsActive = true;

                // 设置热成像效果
                SetupThermalVision();

                // 强制设置相机 FOV
                if (_cam != null)
                    _cam.fieldOfView = 15f;

                // 创建独立准星（每个导弹实例独立，避免多准星问题）
                _reticleCanvas = MissileThermalAssets.CreateReticleInstance();
                if (_reticleCanvas != null)
                    _reticleCanvas.SetActive(true);

                // 降低导弹音效音量
                SetupMissileAudio();

                if (UnderdogsEnhancedMod.DEBUG_MODE)
                    MelonLogger.Msg($"[BMP-1 MCLOS] 摄像机跟随启动，optic={opticNode?.name ?? "null"}, 热成像={_thermalSlotObj != null}");
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"[BMP-1 MCLOS] Start() 异常: {e.Message}\n{e.StackTrace}");
            }
        }

        void SetupMissileAudio()
        {
            // Unity AudioSource
            _audioSources = GetComponentsInChildren<AudioSource>(true);
            if (_audioSources != null && _audioSources.Length > 0)
            {
                _originalVolumes = new float[_audioSources.Length];
                for (int i = 0; i < _audioSources.Length; i++)
                {
                    _originalVolumes[i] = _audioSources[i].volume;
                    _audioSources[i].volume = _originalVolumes[i] * MissileAudioVolume;
                }
            }

            // FMOD StudioEventEmitter (仅检测，暂不控制音量)
            _fmodEmitters = GetComponentsInChildren<StudioEventEmitter>(true);
        }

        void SetupThermalVision()
        {
            if (UnderdogsEnhancedMod.DEBUG_MODE)
                MelonLogger.Msg($"[BMP-1 MCLOS] SetupThermalVision 开始, flirPostPrefab={MissileThermalAssets.flirPostPrefab != null}");

            if (MissileThermalAssets.flirPostPrefab == null)
            {
                if (UnderdogsEnhancedMod.DEBUG_MODE)
                    MelonLogger.Warning("[BMP-1 MCLOS] FLIR预制件未找到，跳过热成像设置");
                return;
            }

            // 创建独立的CameraSlot对象
            _thermalSlotObj = new GameObject("MissileThermalSlot");
            _thermalSlotObj.transform.SetParent(_cam.transform.parent);
            _thermalSlotObj.transform.localPosition = Vector3.zero;
            _thermalSlotObj.transform.localRotation = Quaternion.identity;

            // 添加CameraSlot组件
            _thermalSlot = _thermalSlotObj.AddComponent<CameraSlot>();

            // 配置CameraSlot
            _thermalSlot.VisionType = NightVisionType.Thermal;
            _thermalSlot.IsExterior = false;
            _thermalSlot.BaseBlur = 0f;
            _thermalSlot.OverrideFLIRResolution = true;
            _thermalSlot.CanToggleFlirPolarity = true;
            _thermalSlot.FLIRWidth = MissileThermalAssets.GetFlirWidth();
            _thermalSlot.FLIRHeight = MissileThermalAssets.GetFlirHeight();
            _thermalSlot.DefaultFov = 15f;

            // FLIR模糊效果参数
            _thermalSlot.FLIRFilterMode = FilterMode.Trilinear;
            _thermalSlot.VibrationShakeMultiplier = 0.175f;

            // 根据配置选择FLIR材质（可开关扫描线）
            var blitMaterial = MissileThermalAssets.GetConfiguredBlitMaterial();
            if (blitMaterial != null)
            {
                _thermalSlot.FLIRBlitMaterialOverride = blitMaterial;
            }

            // 获取或添加SimpleNightVision组件
            _snv = _thermalSlotObj.GetComponent<SimpleNightVision>();
            if (_snv == null)
            {
                _snv = _thermalSlotObj.AddComponent<SimpleNightVision>();
            }

            // 销毁现有的PostProcessVolume（如果有的话）
            var existingVolume = _thermalSlotObj.GetComponent<PostProcessVolume>();
            if (existingVolume != null)
            {
                Component.Destroy(existingVolume);
            }

            // 实例化FLIR后处理
            GameObject post = GameObject.Instantiate(MissileThermalAssets.flirPostPrefab, _thermalSlotObj.transform);
            post.transform.Find("MainCam Volume").gameObject.SetActive(false);

            // 获取并启用FLIR Volume
            PostProcessVolume postVol = post.transform.Find("FLIR Only Volume").GetComponent<PostProcessVolume>();
            postVol.enabled = true;

            // 设置SimpleNightVision的_postVolume（使用反射）
            var postVolumeField = typeof(SimpleNightVision).GetField("_postVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (postVolumeField != null)
            {
                postVolumeField.SetValue(_snv, postVol);
            }
            else
            {
                MelonLogger.Warning("[BMP-1 MCLOS] 无法找到 _postVolume 字段");
            }

            // 激活这个CameraSlot
            CameraSlot.SetActiveSlot(_thermalSlot);

            if (UnderdogsEnhancedMod.DEBUG_MODE)
            {
                bool removeScanline = UnderdogsEnhancedMod.bmp1_mclos_flir_no_scanline == null || UnderdogsEnhancedMod.bmp1_mclos_flir_no_scanline.Value;
                MelonLogger.Msg($"[BMP-1 MCLOS] 热成像设置完成: Slot={_thermalSlot != null}, SNV={_snv != null}, PostVolumeSet={postVolumeField != null}, Res={_thermalSlot.FLIRWidth}x{_thermalSlot.FLIRHeight}, NoScan={removeScanline}");
            }
        }

        void LateUpdate()
        {
            if (_restored || _cam == null) return;

            // P键调试：列出导弹及其父子对象的音频组件
            if (Input.GetKeyDown(KeyCode.P))
            {
                MelonLogger.Msg($"[BMP-1 MCLOS] === 导弹音频组件列表 ===");

                // 检查导弹自身
                MelonLogger.Msg("[导弹本体]");
                LogAudioComponents(gameObject);

                // 检查所有子对象
                var children = GetComponentsInChildren<Transform>(true);
                foreach (var child in children)
                {
                    if (child == transform) continue;
                    var audioSources = child.GetComponents<AudioSource>();
                    if (audioSources != null && audioSources.Length > 0)
                    {
                        MelonLogger.Msg($"[子对象: {child.name}]");
                        LogAudioComponents(child.gameObject);
                    }
                }
            }

            // 阻止切换到其他视角（如commander视角）
            if (_thermalSlot != null && CameraSlot.ActiveInstance != _thermalSlot)
                CameraSlot.SetActiveSlot(_thermalSlot);

            // 导弹头部视角：位于导弹前方
            _cam.transform.position = transform.position + transform.forward * 0.5f;

            Vector3 dir = _rb != null && _rb.velocity.sqrMagnitude > 0.1f
                ? _rb.velocity.normalized
                : transform.forward;

            if (dir.sqrMagnitude > 0.01f)
                _cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        void LogAudioComponents(GameObject obj)
        {
            var audioSources = obj.GetComponents<AudioSource>();
            if (audioSources != null && audioSources.Length > 0)
            {
                MelonLogger.Msg($"  [AudioSource] {audioSources.Length} 个:");
                for (int i = 0; i < audioSources.Length; i++)
                {
                    var a = audioSources[i];
                    MelonLogger.Msg($"    [{i}] Vol={a.volume}, Play={a.isPlaying}, Clip={a.clip?.name ?? "null"}");
                }
            }

            var fmodEmitters = obj.GetComponents<StudioEventEmitter>();
            if (fmodEmitters != null && fmodEmitters.Length > 0)
            {
                MelonLogger.Msg($"  [FMOD StudioEventEmitter] {fmodEmitters.Length} 个:");
                for (int i = 0; i < fmodEmitters.Length; i++)
                {
                    var e = fmodEmitters[i];
                    MelonLogger.Msg($"    [{i}] Event={e.Event}, Attenuation={e.OverrideAttenuation}, Playing={e.IsPlaying()}");
                }
            }
        }

        public void Restore()
        {
            if (_restored) return;
            _restored = true;

            MissileCameraActive.IsActive = false;

            // 恢复音效音量
            if (_audioSources != null && _originalVolumes != null)
            {
                for (int i = 0; i < _audioSources.Length && i < _originalVolumes.Length; i++)
                {
                    if (_audioSources[i] != null)
                        _audioSources[i].volume = _originalVolumes[i];
                }
            }

            // 销毁独立准星
            if (_reticleCanvas != null)
            {
                GameObject.Destroy(_reticleCanvas);
                _reticleCanvas = null;
            }

            // 清理热成像Slot
            if (_thermalSlotObj != null)
            {
                if (_snv != null) _snv.enabled = false;
                GameObject.Destroy(_thermalSlotObj);
                _thermalSlotObj = null;
            }

            var prevSlot = _prevSlot;
            var cm = _cm;
            var thermalSlot = _thermalSlot;
            bool prevHadActiveSlot = _prevHadActiveSlot;
            bool preferExterior = _prevExteriorMode || prevSlot == null || (prevSlot != null && prevSlot.IsExterior);
            _thermalSlot = null;
            _snv = null;

            var node = opticNode;
            var cam = _cam;
            if (cam != null)
                CoroutineRunner.Run(DelayedRestore(node, cam, _camOriginalLocalPos, _camOriginalLocalRot, _camOriginalFov, prevSlot, cm, thermalSlot, prevHadActiveSlot, preferExterior, _restoreOpticOnExit, _prevExteriorMode));
            else if (node != null)
                node.SetActive(_restoreOpticOnExit);

            if (UnderdogsEnhancedMod.DEBUG_MODE)
                MelonLogger.Msg("[BMP-1 MCLOS] 摄像机恢复中...");
        }

        private static bool TrySetExteriorMode(CameraManager cm, bool value)
        {
            if (cm == null) return false;

            try
            {
                if (p_cm_exteriorMode != null && p_cm_exteriorMode.CanWrite)
                {
                    p_cm_exteriorMode.SetValue(cm, value, null);
                    return true;
                }
            }
            catch { }

            try
            {
                if (f_cm_exteriorMode != null)
                {
                    f_cm_exteriorMode.SetValue(cm, value);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static CameraSlot FindFallbackSlot(CameraManager cm, CameraSlot excludedSlot, bool preferExterior)
        {
            if (cm == null) cm = CameraManager.Instance;
            if (cm == null || f_cm_allCamSlots == null) return null;

            CameraSlot[] slots = null;
            try { slots = f_cm_allCamSlots.GetValue(cm) as CameraSlot[]; } catch { }
            if (slots == null || slots.Length == 0) return null;

            CameraSlot preferred = null;
            CameraSlot secondary = null;

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || slot == excludedSlot) continue;

                if (slot.IsExterior == preferExterior && preferred == null)
                    preferred = slot;
                else if (secondary == null)
                    secondary = slot;
            }

            return preferred != null ? preferred : secondary;
        }

        private static void TryRestoreCameraSlot(CameraSlot prevSlot, CameraManager cm, CameraSlot excludedSlot, bool prevHadActiveSlot, bool preferExterior)
        {
            if (!prevHadActiveSlot && prevSlot == null && preferExterior)
            {
                try { if (CameraSlot.ActiveInstance != null) CameraSlot.SetActiveSlot(null); } catch { }
                return;
            }

            CameraSlot targetSlot = prevSlot;
            if (targetSlot == null || targetSlot == excludedSlot)
                targetSlot = FindFallbackSlot(cm, excludedSlot, preferExterior);

            if (targetSlot != null && CameraSlot.ActiveInstance != targetSlot)
                CameraSlot.SetActiveSlot(targetSlot);
        }

        // 引爆后继续覆盖摄像机位置几帧，等游戏系统稳定后再恢复 CameraSlot 与 Optic
        static IEnumerator DelayedRestore(
            GameObject node,
            Camera cam,
            Vector3 localPos,
            Quaternion localRot,
            float originalFov,
            CameraSlot prevSlot,
            CameraManager cm,
            CameraSlot excludedSlot,
            bool prevHadActiveSlot,
            bool preferExterior,
            bool restoreOpticOnExit,
            bool restoreExteriorMode)
        {
            for (int i = 0; i < 14; i++)
            {
                TrySetExteriorMode(cm, restoreExteriorMode);
                TryRestoreCameraSlot(prevSlot, cm, excludedSlot, prevHadActiveSlot, preferExterior);

                if (cam != null)
                {
                    cam.transform.localPosition = localPos;
                    cam.transform.localRotation = localRot;
                    cam.fieldOfView = originalFov;
                }
                yield return null;
            }
            if (node != null) node.SetActive(restoreOpticOnExit);
        }

        void OnDestroy() => Restore();
    }

    [HarmonyPatch(typeof(LiveRound), "Detonate")]
    public static class LiveRoundDetonatePatch
    {
        private static void Prefix(LiveRound __instance)
        {
            MclosGuidanceRecovery.NotifyMissileGone(__instance);
            __instance.GetComponent<MissileCameraFollow>()?.Restore();
        }
    }

    [HarmonyPatch(typeof(LiveRound), "doDestroy")]
    public static class LiveRoundDoDestroyPatch
    {
        private static void Prefix(LiveRound __instance)
        {
            MclosGuidanceRecovery.NotifyMissileGone(__instance);
            __instance.GetComponent<MissileCameraFollow>()?.Restore();
        }
    }

    [HarmonyPatch(typeof(LiveRound), "ForceDestroy")]
    public static class LiveRoundForceDestroyPatch
    {
        private static void Prefix(LiveRound __instance)
        {
            MclosGuidanceRecovery.NotifyMissileGone(__instance);
            __instance.GetComponent<MissileCameraFollow>()?.Restore();
        }
    }

    // 导弹飞行时阻止视角切换
    public static class MissileCameraActive
    {
        public static bool IsActive = false;
    }

    [HarmonyPatch(typeof(GHPC.Crew.CrewBrainWeaponsModule), "AdjustAimByRatio")]
    public static class MclosInputAdjustPatch
    {
        private static bool TryGetActiveMclosAmmo(out AmmoType ammo)
        {
            ammo = null;
            var playerInput = GHPC.Player.PlayerInput.Instance;
            if (playerInput == null || !playerInput.IsUsingGuidedMissile) return false;

            var wsInfo = playerInput.CurrentPlayerWeapon;
            var ws = wsInfo?.Weapon;
            var feed = ws?.Feed;
            if (feed == null) return false;

            ammo = feed.AmmoTypeInBreech;
            if (ammo == null)
            {
                var clip = (feed.ReadyRack?.ClipTypes != null && feed.ReadyRack.ClipTypes.Length > 0)
                    ? feed.ReadyRack.ClipTypes[0]
                    : null;
                if (clip?.MinimalPattern != null && clip.MinimalPattern.Length > 0)
                    ammo = clip.MinimalPattern[0]?.AmmoType;
            }

            return ammo != null && ammo.Guidance == AmmoType.GuidanceType.MCLOS;
        }

        private static void Prefix(ref float horizontal, ref float vertical)
        {
            AmmoType ammo;
            bool hasMclosAmmo = TryGetActiveMclosAmmo(out ammo);
            bool applyNow = hasMclosAmmo && BMP1MCLOSAmmo.MclosInputTuning.ShouldApplyNow(MissileCameraActive.IsActive);
            BMP1MCLOSAmmo.MclosInputTuning.ApplyDynamicTurnSpeed(ammo, horizontal, vertical, applyNow);
            horizontal = BMP1MCLOSAmmo.MclosInputTuning.ProcessAxis(horizontal, applyNow);
            vertical = BMP1MCLOSAmmo.MclosInputTuning.ProcessAxis(vertical, applyNow);
        }
    }

    [HarmonyPatch(typeof(GHPC.Camera.CameraManager), "ToggleCameraSlotSet")]
    public static class BlockToggleCameraSlotSet
    {
        private static bool Prefix() => !MissileCameraActive.IsActive;
    }

    [HarmonyPatch(typeof(GHPC.Camera.CameraManager), "CycleCameraSlot")]
    public static class BlockCycleCameraSlot
    {
        private static bool Prefix() => !MissileCameraActive.IsActive;
    }

    [HarmonyPatch(typeof(MissileGuidanceUnit), "MissileDestroyed")]
    public static class MissileGuidanceMissileDestroyedPatch
    {
        private static void Postfix(MissileGuidanceUnit __instance)
        {
            if (__instance == null || !UnderdogsEnhancedMod.bmp1_mclos.Value) return;
            MclosGuidanceRecovery.TryClearWaitingOnMissile(__instance);
        }
    }

    [HarmonyPatch(typeof(MissileGuidanceUnit), "OnGuidanceStopped")]
    public static class MissileGuidanceStoppedPatch
    {
        private static void Postfix(MissileGuidanceUnit __instance)
        {
            if (__instance == null || !UnderdogsEnhancedMod.bmp1_mclos.Value) return;
            MclosGuidanceRecovery.TryClearWaitingOnMissile(__instance);
        }
    }

    public static class MclosGuidanceRecovery
    {
        private static readonly FieldInfo f_gu_unguidedMissiles = typeof(MissileGuidanceUnit).GetField("_unguidedMissiles", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_gu_isGuidingMissile = typeof(MissileGuidanceUnit).GetProperty("IsGuidingMissile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_gu_isGuidingMissile_backing = typeof(MissileGuidanceUnit).GetField("<IsGuidingMissile>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_feed_waitingOnMissile = typeof(AmmoFeed).GetProperty("WaitingOnMissile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feed_waitingOnMissile_backing = typeof(AmmoFeed).GetField("<WaitingOnMissile>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo p_feed_forcePauseReload = typeof(AmmoFeed).GetProperty("ForcePauseReload", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo f_feed_forcePauseReload_backing = typeof(AmmoFeed).GetField("<ForcePauseReload>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool IsMclosAmmo(AmmoType ammo)
        {
            return ammo != null && ammo.Guidance == AmmoType.GuidanceType.MCLOS;
        }

        public static bool IsMclosWeapon(WeaponSystem ws)
        {
            if (ws == null) return false;
            var feed = ws.Feed;
            if (feed == null) return false;

            var ammo = feed.AmmoTypeInBreech;
            if (!IsMclosAmmo(ammo))
            {
                var clip = (feed.ReadyRack?.ClipTypes != null && feed.ReadyRack.ClipTypes.Length > 0)
                    ? feed.ReadyRack.ClipTypes[0]
                    : null;
                if (clip?.MinimalPattern != null && clip.MinimalPattern.Length > 0)
                    ammo = clip.MinimalPattern[0]?.AmmoType;
            }

            return IsMclosAmmo(ammo);
        }

        private static bool IsMissileAlive(LiveRound missile)
        {
            return missile != null && !missile.IsDestroyed;
        }

        public static bool HasLiveMissiles(MissileGuidanceUnit gu)
        {
            var missiles = gu?.CurrentMissiles;
            bool hasLive = false;

            if (missiles != null)
            {
                for (int i = missiles.Count - 1; i >= 0; i--)
                {
                    if (!IsMissileAlive(missiles[i]))
                        missiles.RemoveAt(i);
                }
                hasLive = missiles.Count > 0;
            }

            var unguided = f_gu_unguidedMissiles?.GetValue(gu) as List<LiveRound>;
            if (unguided != null)
            {
                for (int i = unguided.Count - 1; i >= 0; i--)
                {
                    if (!IsMissileAlive(unguided[i]))
                        unguided.RemoveAt(i);
                }
                if (unguided.Count > 0)
                    hasLive = true;
            }

            return hasLive;
        }

        public static WeaponSystem FindOwningWeapon(MissileGuidanceUnit gu, LiveRound missileHint)
        {
            if (gu == null) return null;

            var ws = gu.GetComponent<WeaponSystem>();
            if (ws != null && ws.GuidanceUnit == gu) return ws;

            ws = gu.GetComponentInParent<WeaponSystem>();
            if (ws != null && ws.GuidanceUnit == gu) return ws;

            if (missileHint?.Shooter != null)
            {
                var fromShooter = missileHint.Shooter.GetComponentsInChildren<WeaponSystem>(true);
                for (int i = 0; i < fromShooter.Length; i++)
                {
                    var cand = fromShooter[i];
                    if (cand != null && cand.GuidanceUnit == gu) return cand;
                }
            }

            var all = Object.FindObjectsOfType<WeaponSystem>();
            for (int i = 0; i < all.Length; i++)
            {
                var cand = all[i];
                if (cand != null && cand.GuidanceUnit == gu) return cand;
            }
            return null;
        }

        private static WeaponSystem FindWeaponByMissile(LiveRound missile)
        {
            if (missile == null) return null;

            if (missile.Shooter != null)
            {
                var byShooter = missile.Shooter.GetComponentsInChildren<WeaponSystem>(true);
                for (int i = 0; i < byShooter.Length; i++)
                {
                    var ws = byShooter[i];
                    var gu = ws?.GuidanceUnit;
                    if (ws == null || gu == null) continue;
                    if (gu.CurrentMissiles != null && gu.CurrentMissiles.Contains(missile)) return ws;
                }

                for (int i = 0; i < byShooter.Length; i++)
                {
                    var ws = byShooter[i];
                    if (IsMclosWeapon(ws) && ws.GuidanceUnit != null) return ws;
                }
            }

            var all = Object.FindObjectsOfType<WeaponSystem>();
            for (int i = 0; i < all.Length; i++)
            {
                var ws = all[i];
                var gu = ws?.GuidanceUnit;
                if (ws == null || gu == null) continue;
                if (gu.CurrentMissiles != null && gu.CurrentMissiles.Contains(missile)) return ws;
            }

            return null;
        }

        public static void ForceUnlockGuidance(MissileGuidanceUnit gu, WeaponSystem ws)
        {
            if (gu == null) return;
            if (ws == null) ws = FindOwningWeapon(gu, null);
            if (!IsMclosWeapon(ws)) return;

            if (HasLiveMissiles(gu)) return;

            try
            {
                try { gu.StopGuidance(); } catch { }

                bool guidingSet = false;
                try
                {
                    if (p_gu_isGuidingMissile != null && p_gu_isGuidingMissile.CanWrite)
                    {
                        p_gu_isGuidingMissile.SetValue(gu, false, null);
                        guidingSet = true;
                    }
                }
                catch { }
                if (!guidingSet && f_gu_isGuidingMissile_backing != null)
                {
                    try
                    {
                        f_gu_isGuidingMissile_backing.SetValue(gu, false);
                        guidingSet = true;
                    }
                    catch { }
                }

                var feed = ws.Feed;
                bool waitingSet = false;
                bool pauseSet = false;
                if (feed != null)
                {
                    try
                    {
                        if (p_feed_waitingOnMissile != null && p_feed_waitingOnMissile.CanWrite)
                        {
                            p_feed_waitingOnMissile.SetValue(feed, false, null);
                            waitingSet = true;
                        }
                    }
                    catch { }
                    if (!waitingSet && f_feed_waitingOnMissile_backing != null)
                    {
                        try
                        {
                            f_feed_waitingOnMissile_backing.SetValue(feed, false);
                            waitingSet = true;
                        }
                        catch { }
                    }

                    try
                    {
                        if (p_feed_forcePauseReload != null && p_feed_forcePauseReload.CanWrite)
                        {
                            p_feed_forcePauseReload.SetValue(feed, false, null);
                            pauseSet = true;
                        }
                    }
                    catch { }
                    if (!pauseSet && f_feed_forcePauseReload_backing != null)
                    {
                        try
                        {
                            f_feed_forcePauseReload_backing.SetValue(feed, false);
                            pauseSet = true;
                        }
                        catch { }
                    }
                }
            }
            catch
            {
            }
        }

        public static bool TryRecoverStaleGuidanceOnFire(WeaponSystem ws)
        {
            if (ws == null) return false;
            if (!IsMclosWeapon(ws)) return false;

            var gu = ws.GuidanceUnit;
            var feed = ws.Feed;
            if (gu == null || feed == null) return false;

            bool appearsLocked = gu.IsGuidingMissile || ws.BlockedByMissileGuidance || feed.WaitingOnMissile || feed.ForcePauseReload;
            if (!appearsLocked) return false;

            if (HasLiveMissiles(gu)) return false;

            ForceUnlockGuidance(gu, ws);
            return true;
        }

        public static void TryClearWaitingOnMissile(MissileGuidanceUnit gu)
        {
            if (gu == null) return;
            var ws = FindOwningWeapon(gu, null);
            if (ws == null || ws.GuidanceUnit != gu) return;
            if (!IsMclosWeapon(ws)) return;
            ForceUnlockGuidance(gu, ws);
        }

        public static void NotifyMissileGone(LiveRound missile)
        {
            if (missile == null) return;
            if (missile.Info == null || missile.Info.Guidance != AmmoType.GuidanceType.MCLOS) return;

            var ws = FindWeaponByMissile(missile);
            if (ws == null) return;
            var gu = ws.GuidanceUnit;
            if (gu == null || !IsMclosWeapon(ws)) return;

            try
            {
                gu.MissileDestroyed(missile, missile.transform.position);
            }
            catch { }

            ForceUnlockGuidance(gu, ws);
        }
    }

    [HarmonyPatch(typeof(WeaponSystem), "Fire")]
    public static class WeaponSystemFireRecoveryPatch
    {
        private static void Prefix(WeaponSystem __instance)
        {
            if (__instance == null || !UnderdogsEnhancedMod.bmp1_mclos.Value) return;
            if (!MclosGuidanceRecovery.IsMclosWeapon(__instance)) return;
            MclosGuidanceRecovery.TryRecoverStaleGuidanceOnFire(__instance);
        }
    }

    [HarmonyPatch(typeof(LiveRound), "Start")]
    public static class BMP1MissileCameraPatch
    {
        public static GameObject BMP1OpticNode;
        private static string _currentVehicleName = null;

        // 设置当前载具名称（由 UnderdogsEnhanced.cs 调用）
        public static void SetCurrentVehicle(string name)
        {
            _currentVehicleName = name;
        }

        private static void Postfix(LiveRound __instance)
        {
            if (!UnderdogsEnhancedMod.bmp1_mclos.Value) return;
            if (__instance.NpcRound) return;

            // 从发射者获取载具名称，避免静态变量被多车覆盖的问题
            var shooter = __instance.Shooter;
            if (shooter == null) return;
            var vehicle = shooter.GetComponentInParent<GHPC.Vehicle.Vehicle>();
            if (vehicle == null) return;
            string vehicleName = vehicle.FriendlyName;
            if (vehicleName != "BMP-1" && vehicleName != "BMP-1G") return;

            string ammoName = __instance.Info?.Name;

            //if (UnderdogsEnhancedMod.DEBUG_MODE)
                //MelonLogger.Msg($"[BMP-1 MCLOS] 检测到弹药: {ammoName}");

            // 名称在不同装填时机下可能仍显示旧名，这里统一按 MCLOS 相关名称判断
            if (!BMP1MCLOSAmmo.IsOriginalMissileName(ammoName)) return;

            // 额外检查：确保发射者是玩家控制的载具
            if (!IsPlayerControlled(__instance)) return;

            // 从发射者车辆动态获取 optic 节点，避免静态变量被多车覆盖
            var optic = vehicle.gameObject.transform.Find("BMP1_rig/HULL/TURRET/GUN/Gun Scripts/gunner day sight/Optic");

            var follow = __instance.gameObject.AddComponent<MissileCameraFollow>();
            follow.opticNode = optic != null ? optic.gameObject : null;

            if (UnderdogsEnhancedMod.DEBUG_MODE)
                MelonLogger.Msg($"[BMP-1 MCLOS] 导弹摄像机已附加: {ammoName}, optic={BMP1OpticNode?.name ?? "null"}");
        }

        // 检查弹药是否由玩家控制的载具发射
        private static bool IsPlayerControlled(LiveRound round)
        {
            var shooter = round.Shooter;
            if (shooter == null) return false;

            // 从游戏管理器获取 PlayerInput
            var gameManager = GameObject.Find("_APP_GHPC_");
            if (gameManager == null) return false;

            var playerInput = gameManager.GetComponent<GHPC.Player.PlayerInput>();
            if (playerInput == null) return false;

            var playerUnit = playerInput.CurrentPlayerUnit;
            if (playerUnit == null) return false;

            // 检查发射者所在的载具是否是玩家当前控制的载具
            var shooterVehicle = shooter.GetComponentInParent<GHPC.Vehicle.Vehicle>();
            if (shooterVehicle == null) return false;

            return shooterVehicle.InstanceId == playerUnit.InstanceId;
        }
    }
}
