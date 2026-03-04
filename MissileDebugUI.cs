using MelonLoader;
using UnityEngine;
using System.Collections.Generic;
using System.Globalization;

namespace UnderdogsEnhanced
{
    public class MissileDebugUI : MonoBehaviour
    {
        public static MissileDebugUI Instance { get; private set; }

        private bool _showUI = false;
        private Rect _windowRect = new Rect(20, 20, 700, 760);
        private Vector2 _scrollPos = Vector2.zero;
        private readonly Dictionary<string, string> _inputTexts = new Dictionary<string, string>();

        // 飞行参数
        private float _spiralPower = 2.5f;
        private float _spiralAngularRate = 1500f;
        private float _maximumRange = 20000f;
        private float _noisePowerX = 2f;
        private float _noisePowerY = 2f;
        private float _noiseTimeScale = 2f;
        private float _turnSpeed = 0.2f;

        // 威力参数
        private float _tntEquivalent = 3.25f;
        private float _rhaPenetration = 675f;
        private float _maxSpallRha = 35.6f;
        private float _minSpallRha = 17.8f;
        private float _spallMultiplier = 2.5f;

        // 其他参数
        private float _rangedFuseTime = 500f;
        private float _rhaToFuse = 0f;
        private float _muzzleVelocity = 140f;
        private float _mass = 10.9f;

        // UI参数
        private float _uiScale = 1f;

        // 音效参数
        private float _missileAudioVolume = BMP1MCLOSAmmo.DEFAULT_MISSILE_AUDIO_VOLUME;

        // MCLOS输入调试参数（实时生效）
        private bool _mclosInputTuningEnabled = BMP1MCLOSAmmo.MclosInputTuning.DEFAULT_ENABLED;
        private bool _mclosInputOnlyWhenMissileCam = BMP1MCLOSAmmo.MclosInputTuning.DEFAULT_ONLY_WHEN_MISSILE_CAMERA;
        private float _mclosInputScale = BMP1MCLOSAmmo.MclosInputTuning.DEFAULT_INPUT_SCALE;
        private float _mclosInputCurveExponent = BMP1MCLOSAmmo.MclosInputTuning.DEFAULT_CURVE_EXPONENT;
        private bool _mclosDynamicTurnSpeedEnabled = BMP1MCLOSAmmo.MclosInputTuning.DEFAULT_DYNAMIC_TURNSPEED_ENABLED;
        private float _mclosDynamicTurnSpeedMinMul = BMP1MCLOSAmmo.MclosInputTuning.DEFAULT_DYNAMIC_TURNSPEED_MIN_MULTIPLIER;
        private float _mclosDynamicTurnSpeedMaxMul = BMP1MCLOSAmmo.MclosInputTuning.DEFAULT_DYNAMIC_TURNSPEED_MAX_MULTIPLIER;
        private float _mclosDynamicTurnSpeedExponent = BMP1MCLOSAmmo.MclosInputTuning.DEFAULT_DYNAMIC_TURNSPEED_EXPONENT;

        public static void Init()
        {
            if (Instance != null) return;

            var go = new GameObject("MissileDebugUI");
            GameObject.DontDestroyOnLoad(go);
            Instance = go.AddComponent<MissileDebugUI>();
            Instance.LoadFromDebugParams();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                _showUI = !_showUI;
                MelonLogger.Msg($"[Debug UI] {(_showUI ? "打开" : "关闭")}");
            }
        }

        void OnGUI()
        {
            if (!_showUI) return;

            var oldMatrix = GUI.matrix;
            // 以窗口左上角为缩放锚点，避免缩放时窗口向屏幕原点漂移
            GUIUtility.ScaleAroundPivot(new Vector2(_uiScale, _uiScale), new Vector2(_windowRect.x, _windowRect.y));
            _windowRect = GUI.Window(12345, _windowRect, DrawWindow, "导弹参数调试 (F10关闭)");
            GUI.matrix = oldMatrix;

            ClampWindowToScreen();
        }

        void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            _uiScale = LabeledSliderWithInput("ui_scale", "UI缩放", _uiScale, 0.6f, 2.2f, "仅影响此调试窗口");
            GUILayout.Space(6);

            GUILayout.Label("<b>=== 音效参数 ===</b>", new GUIStyle(GUI.skin.label) { richText = true });
            _missileAudioVolume = LabeledSliderWithInput("missile_audio_volume", "导弹音量", _missileAudioVolume, 0f, 1f, "导弹飞行时音效音量倍率");
            MissileCameraFollow.MissileAudioVolume = _missileAudioVolume;
            GUILayout.Space(6);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, true, GUILayout.Height(_windowRect.height - 170f));

            GUILayout.Label("<b>=== 飞行参数 ===</b>", new GUIStyle(GUI.skin.label) { richText = true });

            _spiralPower = LabeledSliderWithInput("spiral_power", "螺旋强度", _spiralPower, 0f, 50f, "导弹螺旋摆动幅度");
            _spiralAngularRate = LabeledSliderWithInput("spiral_angular", "螺旋角速度", _spiralAngularRate, 0f, 5000f, "螺旋摆动频率(度/秒)");
            _maximumRange = LabeledSliderWithInput("max_range", "最大射程", _maximumRange, 1000f, 30000f, "单位: 米");
            _noisePowerX = LabeledSliderWithInput("noise_x", "噪声X", _noisePowerX, 0f, 10f, "横向随机抖动");
            _noisePowerY = LabeledSliderWithInput("noise_y", "噪声Y", _noisePowerY, 0f, 10f, "纵向随机抖动");
            _noiseTimeScale = LabeledSliderWithInput("noise_time", "噪声时间", _noiseTimeScale, 0f, 5f, "噪声变化速度");
            _turnSpeed = LabeledSliderWithInput("turn_speed", "转向速度", _turnSpeed, 0f, 5f, "导弹转向响应速度");

            GUILayout.Space(5);
            GUILayout.Label("<b>=== 威力参数 ===</b>", new GUIStyle(GUI.skin.label) { richText = true });

            _tntEquivalent = LabeledSliderWithInput("tnt", "装药", _tntEquivalent, 0f, 10f, "装药当量kg");
            _rhaPenetration = LabeledSliderWithInput("rha_pen", "穿深", _rhaPenetration, 0f, 1000f, "穿透RHA装甲mm");
            _maxSpallRha = LabeledSliderWithInput("max_spall", "最大破片", _maxSpallRha, 0f, 1000f, "破片穿透上限mm");
            _minSpallRha = LabeledSliderWithInput("min_spall", "最小破片", _minSpallRha, 0f, 1000f, "破片穿透下限mm");
            _spallMultiplier = LabeledSliderWithInput("spall_mult", "破片倍数", _spallMultiplier, 0.1f, 3f, "破片数量倍率");
            _rangedFuseTime = LabeledSliderWithInput("ranged_fuse_time", "引信触发时间", _rangedFuseTime, 0f, 5000f, "引信触发时间(s)");
            _rhaToFuse = LabeledSliderWithInput("rha_to_fuse", "引信触发所需RHA", _rhaToFuse, 0f, 300f, "引信触发所需RHA(mm)");
            _muzzleVelocity = LabeledSliderWithInput("muzzle_velocity", "初速", _muzzleVelocity, 0f, 3000f, "初速(m/s)");
            _mass = LabeledSliderWithInput("mass", "弹体质量", _mass, 0.1f, 20f, "弹体质量(kg)");

            GUILayout.Space(10);
            GUILayout.Label("<b>=== MCLOS输入修正(实时) ===</b>", new GUIStyle(GUI.skin.label) { richText = true });
            _mclosInputTuningEnabled = LabeledToggle("启用输入修正", _mclosInputTuningEnabled, "仅作用于MCLOS制导输入");
            _mclosInputOnlyWhenMissileCam = LabeledToggle("仅导弹镜头时生效", _mclosInputOnlyWhenMissileCam, "避免影响普通瞄准");
            _mclosInputScale = LabeledSliderWithInput("mclos_input_scale", "输入倍率", _mclosInputScale, 0.1f, 5f, "放大/缩小操控幅度");
            _mclosInputCurveExponent = LabeledSliderWithInput("mclos_input_exp", "曲线指数", _mclosInputCurveExponent, 0.2f, 2f, "1=不变, <1提中心响应, >1压中心响应");
            _mclosDynamicTurnSpeedEnabled = LabeledToggle("动态TurnSpeed", _mclosDynamicTurnSpeedEnabled, "按输入量自动缩放导弹转向速度");
            _mclosDynamicTurnSpeedMinMul = LabeledSliderWithInput("mclos_turn_min_mul", "Turn最小倍率", _mclosDynamicTurnSpeedMinMul, 0.05f, 2f, "输入接近0时倍率");
            _mclosDynamicTurnSpeedMaxMul = LabeledSliderWithInput("mclos_turn_max_mul", "Turn最大倍率", _mclosDynamicTurnSpeedMaxMul, 0.1f, 4f, "输入接近1时倍率");
            _mclosDynamicTurnSpeedExponent = LabeledSliderWithInput("mclos_turn_exp", "Turn映射指数", _mclosDynamicTurnSpeedExponent, 0.2f, 3f, "1=线性, >1前段更柔, <1前段更激进");
            GUILayout.Label($"当前状态: {(BMP1MCLOSAmmo.MclosInputTuning.RuntimeApplying ? "已生效" : "未生效")} | MissileCam={MissileCameraActive.IsActive} | TurnScale={FormatFloat(BMP1MCLOSAmmo.MclosInputTuning.LastTurnSpeedMultiplier)}");
            SyncMclosInputTuning();

            GUILayout.Space(10);
            DrawOriginalParamsBlock();
            GUILayout.EndScrollView();

            GUILayout.Space(10);

            if (GUILayout.Button("应用到新导弹", GUILayout.Height(30)))
            {
                ApplyParams();
            }

            if (GUILayout.Button("重置为默认", GUILayout.Height(30)))
            {
                ResetToDefaults();
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private static bool TryParseFloat(string input, out float value)
        {
            if (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            if (float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;
            if (float.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            value = 0f;
            return false;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        float LabeledSliderWithInput(string key, string label, float value, float min, float max, string tooltip = "")
        {
            if (!_inputTexts.ContainsKey(key))
                _inputTexts[key] = FormatFloat(value);

            GUILayout.BeginHorizontal();
            string labelText = string.IsNullOrEmpty(tooltip) ? label : $"{label} ({tooltip})";
            GUILayout.Label(labelText, GUILayout.Width(290));

            float result = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(190));

            string text = GUILayout.TextField(_inputTexts[key], GUILayout.Width(90));
            if (text != _inputTexts[key])
            {
                _inputTexts[key] = text;
                if (TryParseFloat(text, out float parsed))
                    result = Mathf.Clamp(parsed, min, max);
            }

            if (Mathf.Abs(result - value) > 0.0001f)
                _inputTexts[key] = FormatFloat(result);

            GUILayout.Label(FormatFloat(result), GUILayout.Width(70));
            GUILayout.EndHorizontal();
            return result;
        }

        bool LabeledToggle(string label, bool value, string tooltip = "")
        {
            GUILayout.BeginHorizontal();
            string labelText = string.IsNullOrEmpty(tooltip) ? label : $"{label} ({tooltip})";
            GUILayout.Label(labelText, GUILayout.Width(290));
            bool result = GUILayout.Toggle(value, value ? "开" : "关", GUILayout.Width(90));
            GUILayout.EndHorizontal();
            return result;
        }

        void DrawOriginalParamsBlock()
        {
            GUILayout.Label("<b>=== 原始参数快照 ===</b>", new GUIStyle(GUI.skin.label) { richText = true });

            if (!BMP1MCLOSAmmo.TryGetOriginalParams(out var raw))
            {
                GUILayout.Label("尚未读取到原始弹药参数（进入含 BMP-1 的战局并完成弹药初始化后可见）");
                return;
            }

            DrawRaw("SpiralPower", raw.SpiralPower);
            DrawRaw("SpiralAngularRate", raw.SpiralAngularRate);
            DrawRaw("MaximumRange", raw.MaximumRange);
            DrawRaw("NoisePowerX", raw.NoisePowerX);
            DrawRaw("NoisePowerY", raw.NoisePowerY);
            DrawRaw("NoiseTimeScale", raw.NoiseTimeScale);
            DrawRaw("TurnSpeed", raw.TurnSpeed);
            DrawRaw("TntEquivalentKg", raw.TntEquivalentKg);
            DrawRaw("RhaPenetration", raw.RhaPenetration);
            DrawRaw("MaxSpallRha", raw.MaxSpallRha);
            DrawRaw("MinSpallRha", raw.MinSpallRha);
            DrawRaw("SpallMultiplier", raw.SpallMultiplier);
            DrawRaw("RangedFuseTime", raw.RangedFuseTime);
            DrawRaw("RhaToFuse", raw.RhaToFuse);
            DrawRaw("MuzzleVelocity", raw.MuzzleVelocity);
            DrawRaw("Mass", raw.Mass);
        }

        void DrawRaw(string name, float value)
        {
            GUILayout.Label($"{name}: {FormatFloat(value)}");
        }

        void ClampWindowToScreen()
        {
            float scaledWidth = _windowRect.width * _uiScale;
            float scaledHeight = _windowRect.height * _uiScale;
            float maxX = Mathf.Max(0f, Screen.width - scaledWidth);
            float maxY = Mathf.Max(0f, Screen.height - scaledHeight);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, maxX);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, maxY);
        }

        void LoadFromDebugParams()
        {
            var p = BMP1MCLOSAmmo.GetCurrentDebugParams();
            _spiralPower = p.SpiralPower;
            _spiralAngularRate = p.SpiralAngularRate;
            _maximumRange = p.MaximumRange;
            _noisePowerX = p.NoisePowerX;
            _noisePowerY = p.NoisePowerY;
            _noiseTimeScale = p.NoiseTimeScale;
            _turnSpeed = p.TurnSpeed;
            _tntEquivalent = p.TntEquivalentKg;
            _rhaPenetration = p.RhaPenetration;
            _maxSpallRha = p.MaxSpallRha;
            _minSpallRha = p.MinSpallRha;
            _spallMultiplier = p.SpallMultiplier;
            _rangedFuseTime = p.RangedFuseTime;
            _rhaToFuse = p.RhaToFuse;
            _muzzleVelocity = p.MuzzleVelocity;
            _mass = p.Mass;
            _mclosInputTuningEnabled = BMP1MCLOSAmmo.MclosInputTuning.Enabled;
            _mclosInputOnlyWhenMissileCam = BMP1MCLOSAmmo.MclosInputTuning.OnlyWhenMissileCamera;
            _mclosInputScale = BMP1MCLOSAmmo.MclosInputTuning.InputScale;
            _mclosInputCurveExponent = BMP1MCLOSAmmo.MclosInputTuning.CurveExponent;
            _mclosDynamicTurnSpeedEnabled = BMP1MCLOSAmmo.MclosInputTuning.DynamicTurnSpeedEnabled;
            _mclosDynamicTurnSpeedMinMul = BMP1MCLOSAmmo.MclosInputTuning.DynamicTurnSpeedMinMultiplier;
            _mclosDynamicTurnSpeedMaxMul = BMP1MCLOSAmmo.MclosInputTuning.DynamicTurnSpeedMaxMultiplier;
            _mclosDynamicTurnSpeedExponent = BMP1MCLOSAmmo.MclosInputTuning.DynamicTurnSpeedExponent;
            _inputTexts.Clear();
        }

        void ResetToDefaults()
        {
            BMP1MCLOSAmmo.ResetDebugParamsToDefaults();
            BMP1MCLOSAmmo.MclosInputTuning.ResetToDefaults();
            LoadFromDebugParams();
        }

        void ApplyParams()
        {
            BMP1MCLOSAmmo.SetDebugParams(
                _spiralPower,
                _spiralAngularRate,
                _maximumRange,
                _noisePowerX,
                _noisePowerY,
                _noiseTimeScale,
                _turnSpeed,
                _tntEquivalent,
                _rhaPenetration,
                _maxSpallRha,
                _minSpallRha,
                _spallMultiplier,
                _rangedFuseTime,
                _rhaToFuse,
                _muzzleVelocity,
                _mass
            );

            MelonLogger.Msg($"[Debug UI] 参数已应用: Spiral={_spiralPower}, Range={_maximumRange}, TurnSpeed={_turnSpeed}, TNT={_tntEquivalent}kg, Fuse={_rangedFuseTime}, V0={_muzzleVelocity}, Mass={_mass}");
        }

        void SyncMclosInputTuning()
        {
            BMP1MCLOSAmmo.MclosInputTuning.Enabled = _mclosInputTuningEnabled;
            BMP1MCLOSAmmo.MclosInputTuning.OnlyWhenMissileCamera = _mclosInputOnlyWhenMissileCam;
            BMP1MCLOSAmmo.MclosInputTuning.InputScale = _mclosInputScale;
            BMP1MCLOSAmmo.MclosInputTuning.CurveExponent = _mclosInputCurveExponent;
            BMP1MCLOSAmmo.MclosInputTuning.DynamicTurnSpeedEnabled = _mclosDynamicTurnSpeedEnabled;
            BMP1MCLOSAmmo.MclosInputTuning.DynamicTurnSpeedMinMultiplier = _mclosDynamicTurnSpeedMinMul;
            BMP1MCLOSAmmo.MclosInputTuning.DynamicTurnSpeedMaxMultiplier = _mclosDynamicTurnSpeedMaxMul;
            BMP1MCLOSAmmo.MclosInputTuning.DynamicTurnSpeedExponent = _mclosDynamicTurnSpeedExponent;
        }
    }
}
