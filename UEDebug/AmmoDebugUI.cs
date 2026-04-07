#if DEBUG
using MelonLoader;
using UnityEngine;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using GHPC.Weaponry;

namespace UnderdogsEnhanced
{
    public class AmmoDebugUI : MonoBehaviour
    {
        public static AmmoDebugUI Instance { get; private set; }

        private bool _showUI = false;
        private Rect _windowRect = new Rect(20, 20, 700, 600);
        private Vector2 _scrollPos = Vector2.zero;
        private readonly Dictionary<string, string> _inputTexts = new Dictionary<string, string>();

        private enum AmmoCategory { BMP1Missile }
        private AmmoCategory _currentType = AmmoCategory.BMP1Missile;

        // BMP1 飞行参数
        private float _bmp_spiralPower = BMP1MCLOSAmmo.DEFAULT_SPIRAL_POWER;
        private float _bmp_spiralAngularRate = BMP1MCLOSAmmo.DEFAULT_SPIRAL_ANGULAR_RATE;
        private float _bmp_maximumRange = BMP1MCLOSAmmo.DEFAULT_MAXIMUM_RANGE;
        private float _bmp_noisePowerX = BMP1MCLOSAmmo.DEFAULT_NOISE_POWER_X;
        private float _bmp_noisePowerY = BMP1MCLOSAmmo.DEFAULT_NOISE_POWER_Y;
        private float _bmp_noiseTimeScale = BMP1MCLOSAmmo.DEFAULT_NOISE_TIME_SCALE;
        private float _bmp_turnSpeed = BMP1MCLOSAmmo.DEFAULT_TURN_SPEED;
        // BMP1 威力参数
        private float _bmp_tntEquivalent = BMP1MCLOSAmmo.DEFAULT_TNT_EQUIVALENT;
        private float _bmp_rhaPen = BMP1MCLOSAmmo.DEFAULT_RHA_PENETRATION;
        private float _bmp_maxSpallRha = BMP1MCLOSAmmo.DEFAULT_MAX_SPALL_RHA;
        private float _bmp_minSpallRha = BMP1MCLOSAmmo.DEFAULT_MIN_SPALL_RHA;
        private float _bmp_spallMultiplier = BMP1MCLOSAmmo.DEFAULT_SPALL_MULTIPLIER;
        private float _bmp_rangedFuseTime = BMP1MCLOSAmmo.DEFAULT_RANGED_FUSE_TIME;
        private float _bmp_rhaToFuse = BMP1MCLOSAmmo.DEFAULT_RHA_TO_FUSE;
        private float _bmp_muzzleVel = BMP1MCLOSAmmo.DEFAULT_MUZZLE_VELOCITY;
        private float _bmp_mass = BMP1MCLOSAmmo.DEFAULT_MASS;

        public static void Init()
        {
            if (Instance != null) return;
            var go = new GameObject("AmmoDebugUI");
            GameObject.DontDestroyOnLoad(go);
            Instance = go.AddComponent<AmmoDebugUI>();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F11))
            {
                _showUI = !_showUI;
                MelonLogger.Msg($"[AmmoDebugUI] {(_showUI ? "打开" : "关闭")}");
            }
        }

        void OnGUI()
        {
            if (!_showUI) return;
            _windowRect = GUI.Window(12346, _windowRect, DrawWindow, "弹药调试 (F11)");
        }

        void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            GUILayout.Label("弹药类型:");
            _currentType = (AmmoCategory)GUILayout.SelectionGrid((int)_currentType, new string[] { "BMP-1导弹" }, 1);

            GUILayout.Space(10);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, true, GUILayout.Height(450));

            DrawOriginalVsCurrent_BMP();
            GUILayout.Space(10);
            DrawBMP1Params();

            GUILayout.EndScrollView();

            if (GUILayout.Button("应用", GUILayout.Height(30)))
                ApplyParams();

            if (GUILayout.Button("重置", GUILayout.Height(30)))
                ResetParams();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        void DrawAmmoTypeAllProperties(AmmoType ammo)
        {
            // 读取所有公共字段
            var fields = typeof(AmmoType).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                try
                {
                    var value = field.GetValue(ammo);
                    if (value != null && IsSimpleType(field.FieldType))
                    {
                        GUILayout.Label($"{field.Name}: {value}");
                    }
                }
                catch { }
            }

            // 读取所有公共属性
            var props = typeof(AmmoType).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                try
                {
                    var value = prop.GetValue(ammo);
                    if (value != null && IsSimpleType(prop.PropertyType))
                    {
                        GUILayout.Label($"{prop.Name}: {value}");
                    }
                }
                catch { }
            }
        }

        bool IsSimpleType(System.Type type)
        {
            return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type.IsEnum;
        }

        void DrawOriginalVsCurrent_BMP()
        {
            if (BMP1MCLOSAmmo.ammo_9m14_mclos != null)
            {
                GUILayout.Label("<b>=== 当前导弹属性 ===</b>", new GUIStyle(GUI.skin.label) { richText = true });
                DrawAmmoTypeAllProperties(BMP1MCLOSAmmo.ammo_9m14_mclos);
            }
            if (BMP1MCLOSAmmo.ammo_9m14_original != null)
            {
                GUILayout.Label("<b>=== 原版9M14导弹属性 ===</b>", new GUIStyle(GUI.skin.label) { richText = true });
                DrawAmmoTypeAllProperties(BMP1MCLOSAmmo.ammo_9m14_original);
                GUILayout.Space(10);
            }
        }

        void DrawBMP1Params()
        {
            GUILayout.Label("<b>飞行参数</b>", new GUIStyle(GUI.skin.label) { richText = true });
            _bmp_spiralPower = LabeledSlider("bmp_spiral", "螺旋强度", _bmp_spiralPower, 0f, 50f);
            _bmp_spiralAngularRate = LabeledSlider("bmp_angular", "螺旋角速度", _bmp_spiralAngularRate, 0f, 5000f);
            _bmp_maximumRange = LabeledSlider("bmp_range", "最大射程", _bmp_maximumRange, 1000f, 30000f);
            _bmp_noisePowerX = LabeledSlider("bmp_noiseX", "噪声X", _bmp_noisePowerX, 0f, 10f);
            _bmp_noisePowerY = LabeledSlider("bmp_noiseY", "噪声Y", _bmp_noisePowerY, 0f, 10f);
            _bmp_noiseTimeScale = LabeledSlider("bmp_noiseT", "噪声时间", _bmp_noiseTimeScale, 0f, 5f);
            _bmp_turnSpeed = LabeledSlider("bmp_turn", "转向速度", _bmp_turnSpeed, 0f, 5f);

            GUILayout.Space(5);
            GUILayout.Label("<b>威力参数</b>", new GUIStyle(GUI.skin.label) { richText = true });
            _bmp_tntEquivalent = LabeledSlider("bmp_tnt", "装药kg", _bmp_tntEquivalent, 0f, 10f);
            _bmp_rhaPen = LabeledSlider("bmp_rha", "穿深mm", _bmp_rhaPen, 0f, 1000f);
            _bmp_maxSpallRha = LabeledSlider("bmp_maxSpall", "最大破片", _bmp_maxSpallRha, 0f, 100f);
            _bmp_minSpallRha = LabeledSlider("bmp_minSpall", "最小破片", _bmp_minSpallRha, 0f, 100f);
            _bmp_spallMultiplier = LabeledSlider("bmp_spallMult", "破片倍数", _bmp_spallMultiplier, 0.1f, 3f);
            _bmp_rangedFuseTime = LabeledSlider("bmp_fuseTime", "引信时间", _bmp_rangedFuseTime, 0f, 5000f);
            _bmp_rhaToFuse = LabeledSlider("bmp_rhaFuse", "引信RHA", _bmp_rhaToFuse, 0f, 300f);
            _bmp_muzzleVel = LabeledSlider("bmp_vel", "初速m/s", _bmp_muzzleVel, 0f, 300f);
            _bmp_mass = LabeledSlider("bmp_mass", "质量kg", _bmp_mass, 0.1f, 20f);
        }

        float LabeledSlider(string key, string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120));
            float newVal = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(200));
            GUILayout.Label(newVal.ToString("F1"), GUILayout.Width(60));
            GUILayout.EndHorizontal();
            return newVal;
        }

        void ApplyParams()
        {
            BMP1MCLOSAmmo.SetDebugParams(_bmp_spiralPower, _bmp_spiralAngularRate, _bmp_maximumRange,
                _bmp_noisePowerX, _bmp_noisePowerY, _bmp_noiseTimeScale, _bmp_turnSpeed,
                _bmp_tntEquivalent, _bmp_rhaPen, _bmp_maxSpallRha, _bmp_minSpallRha,
                _bmp_spallMultiplier, _bmp_rangedFuseTime, _bmp_rhaToFuse, _bmp_muzzleVel, _bmp_mass);
            MelonLogger.Msg($"[AmmoDebugUI] BMP-1参数已应用");
        }

        void ResetParams()
        {
            _bmp_spiralPower = BMP1MCLOSAmmo.DEFAULT_SPIRAL_POWER;
            _bmp_spiralAngularRate = BMP1MCLOSAmmo.DEFAULT_SPIRAL_ANGULAR_RATE;
            _bmp_maximumRange = BMP1MCLOSAmmo.DEFAULT_MAXIMUM_RANGE;
            _bmp_noisePowerX = BMP1MCLOSAmmo.DEFAULT_NOISE_POWER_X;
            _bmp_noisePowerY = BMP1MCLOSAmmo.DEFAULT_NOISE_POWER_Y;
            _bmp_noiseTimeScale = BMP1MCLOSAmmo.DEFAULT_NOISE_TIME_SCALE;
            _bmp_turnSpeed = BMP1MCLOSAmmo.DEFAULT_TURN_SPEED;
            _bmp_tntEquivalent = BMP1MCLOSAmmo.DEFAULT_TNT_EQUIVALENT;
            _bmp_rhaPen = BMP1MCLOSAmmo.DEFAULT_RHA_PENETRATION;
            _bmp_maxSpallRha = BMP1MCLOSAmmo.DEFAULT_MAX_SPALL_RHA;
            _bmp_minSpallRha = BMP1MCLOSAmmo.DEFAULT_MIN_SPALL_RHA;
            _bmp_spallMultiplier = BMP1MCLOSAmmo.DEFAULT_SPALL_MULTIPLIER;
            _bmp_rangedFuseTime = BMP1MCLOSAmmo.DEFAULT_RANGED_FUSE_TIME;
            _bmp_rhaToFuse = BMP1MCLOSAmmo.DEFAULT_RHA_TO_FUSE;
            _bmp_muzzleVel = BMP1MCLOSAmmo.DEFAULT_MUZZLE_VELOCITY;
            _bmp_mass = BMP1MCLOSAmmo.DEFAULT_MASS;
            ApplyParams();
        }
    }
}
#endif