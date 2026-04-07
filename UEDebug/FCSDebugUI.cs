#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using Reticle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderdogsEnhanced
{
    public class FCSDebugUI : MonoBehaviour
    {
        public static FCSDebugUI Instance { get; private set; }

        private enum DebugTab
        {
            Context,
            FCS,
            UiReticle,
            Ammo,
            Thermal,
            EMES18
        }

        private sealed class DebugTarget
        {
            public Transform Transform;
            public string Path;
            public string Kind;
            public string RootLabel;
        }

        private sealed class MemberAdapter
        {
            public string Name;
            public Type ValueType;
            public bool CanWrite;
            public bool IsField;
            public FieldInfo Field;
            public PropertyInfo Property;

            public object GetValue(object target)
            {
                return IsField ? Field.GetValue(target) : Property.GetValue(target, null);
            }

            public void SetValue(object target, object value)
            {
                if (!CanWrite) return;
                if (IsField) Field.SetValue(target, value);
                else Property.SetValue(target, value, null);
            }
        }

        private sealed class SnapshotEntry
        {
            public MemberAdapter Member;
            public object Value;
        }

        private sealed class ObjectSnapshot
        {
            public object Target;
            public readonly List<SnapshotEntry> Entries = new List<SnapshotEntry>();
        }

        private sealed class ReticleContext
        {
            public ReticleMesh Mesh;
            public ReticleSO Reticle;
            public Transform Root;
            public UsableOptic Optic;
            public bool IsOverride;
        }

        private sealed class PreviewRuntimeState
        {
            public AmmoType CurrentAmmo;
            public float CurrentRangeMeters;
            public float TargetRangeMeters;
            public float VerticalRangeOffsetMrads;
            public float RotaryRangeRotationMrads;
        }

        private sealed class PathSegment
        {
            public string MemberName;
            public readonly List<int> Indices = new List<int>();
        }

        private sealed class ImportedReticleMarker : MonoBehaviour
        {
        }

        private static readonly Dictionary<Type, List<MemberAdapter>> MemberCache = new Dictionary<Type, List<MemberAdapter>>();
        private static readonly Dictionary<Type, List<MemberAdapter>> MemberCacheWithoutUnityBase = new Dictionary<Type, List<MemberAdapter>>();

        private readonly Dictionary<string, string> _inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DebugTarget> _targets = new List<DebugTarget>();

        private bool _showUi;
        private DebugTab _activeTab = DebugTab.Context;
        private Rect _windowRect = new Rect(20f, 20f, 1400f, 1000f);
        private Vector2 _mainScroll;
        private Vector2 _contextScroll;
        private Vector2 _weaponScroll;
        private Vector2 _targetScroll;
        private Vector2 _fcsRawScroll;
        private Vector2 _ammoRawScroll;
        private float _uiScale = 1f;
        private readonly Color _windowBackgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.96f);
        private bool _followPlayerWeapon = true;
        private int _lastVehicleInstanceId = int.MinValue;

        private Vehicle _currentVehicle;
        private WeaponsManager _currentWeaponsManager;
        private WeaponSystemInfo[] _currentWeaponInfos = new WeaponSystemInfo[0];
        private int _selectedWeaponIndex = -1;
        private WeaponSystemInfo _selectedWeaponInfo;
        private WeaponSystem _selectedWeapon;
        private FireControlSystem _selectedFcs;
        private AmmoType _selectedAmmo;
        private UsableOptic _activeOptic;
        private CameraSlot _activeCameraSlot;
        private Transform _emes18Root;

        private Transform _selectedElement;
        private string _selectedPath = string.Empty;
        private string _selectedKind = string.Empty;
        private Vector3 _positionOffset = Vector3.zero;
        private Vector3 _scaleOffset = Vector3.one;
        private string _posXInput = "0";
        private string _posYInput = "0";
        private string _posZInput = "0";
        private string _scaleXInput = "1";
        private string _scaleYInput = "1";
        private string _scaleZInput = "1";
        private string _canvasSortInput = "0";
        private string _canvasPlaneInput = "1";
        private string _targetFilter = string.Empty;
        private string _fcsSearch = string.Empty;
        private string _ammoSearch = string.Empty;
        private string _selectedAmmoSource = "null";
        private string _lastReticleExportPath = string.Empty;
        private string _reticleImportPath = string.Empty;
        private string _reticleImportStatus = string.Empty;

        // 热成像标签页相关字段
        private Vector2 _thermalScroll;
        private ThermalConfig _thermalEditConfig = new ThermalConfig();
        private bool _thermalInitialized = false;
        private string _thermalStatusMessage = string.Empty;
        private float _thermalStatusTime = 0f;

        // EMES18 标签页相关字段
        private Vector2 _emes18Scroll;

        private const string ImportedReticleSuffix = "__UE_ImportedReticle";
        private static readonly bool ImportTransformPathsByDefault = false;

        private ObjectSnapshot _fcsSnapshot;
        private ObjectSnapshot _ammoSnapshot;

        public static void Init()
        {
            if (Instance != null) return;
            var go = new GameObject("__UE_FCS_DEBUG_UI__");
            go.hideFlags = HideFlags.HideAndDontSave;
            GameObject.DontDestroyOnLoad(go);
            go.AddComponent<FCSDebugUI>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            RefreshAll();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F11))
                OpenTab(DebugTab.Ammo);
        }

        public void ToggleWindow()
        {
            _showUi = !_showUi;
            if (_showUi) RefreshAll();
        }

        public void OpenTabToAmmo()
        {
            OpenTab(DebugTab.Ammo);
        }

        private void OpenTab(DebugTab tab)
        {
            _activeTab = tab;
            _showUi = true;
            RefreshAll();
        }

        public void RefreshWindowTargets()
        {
            RefreshAll();
            MelonLogger.Msg("[FCSDebugUI] Refresh | Weapon=" + DescribeWeaponInfo(_selectedWeaponInfo) + " | Ammo=" + DescribeAmmo(_selectedAmmo) + " | Source=" + _selectedAmmoSource);
        }

        private void RefreshAll()
        {
            RefreshContext();
            RefreshTargets();
            EnsureSnapshots();
        }

        private void RefreshContext()
        {
            var playerInput = PlayerInput.Instance;
            _activeCameraSlot = CameraSlot.ActiveInstance;
            _activeOptic = ResolveActiveOptic();
            _currentVehicle = null;

            if (playerInput != null)
            {
                _currentVehicle = playerInput.CurrentPlayerUnit as Vehicle;
                if (_currentVehicle == null && playerInput.CurrentPlayerWeapon != null && playerInput.CurrentPlayerWeapon.Weapon != null)
                    _currentVehicle = playerInput.CurrentPlayerWeapon.Weapon.GetComponentInParent<Vehicle>();
            }

            if (_currentVehicle == null && _activeOptic != null)
                _currentVehicle = _activeOptic.GetComponentInParent<Vehicle>();

            if (_currentVehicle == null && _activeCameraSlot != null)
                _currentVehicle = _activeCameraSlot.GetComponentInParent<Vehicle>();

            int vehicleInstanceId = _currentVehicle != null ? _currentVehicle.GetInstanceID() : int.MinValue;
            bool vehicleChanged = vehicleInstanceId != _lastVehicleInstanceId;
            _lastVehicleInstanceId = vehicleInstanceId;

            _currentWeaponsManager = _currentVehicle != null ? _currentVehicle.GetComponent<WeaponsManager>() : null;
            _currentWeaponInfos = _currentWeaponsManager != null && _currentWeaponsManager.Weapons != null
                ? _currentWeaponsManager.Weapons.Where(info => info != null).ToArray()
                : new WeaponSystemInfo[0];

            var currentWeaponInfo = playerInput != null ? playerInput.CurrentPlayerWeapon : null;
            int matchedWeaponIndex = FindWeaponInfoIndex(currentWeaponInfo);
            if (_currentWeaponInfos.Length == 0)
            {
                _selectedWeaponIndex = -1;
            }
            else if (vehicleChanged)
            {
                _selectedWeaponIndex = matchedWeaponIndex >= 0 ? matchedWeaponIndex : 0;
            }
            else if (_followPlayerWeapon && matchedWeaponIndex >= 0)
            {
                _selectedWeaponIndex = matchedWeaponIndex;
            }
            else if (_selectedWeaponIndex < 0 || _selectedWeaponIndex >= _currentWeaponInfos.Length)
            {
                _selectedWeaponIndex = matchedWeaponIndex >= 0 ? matchedWeaponIndex : 0;
            }

            _selectedWeaponInfo = _selectedWeaponIndex >= 0 && _selectedWeaponIndex < _currentWeaponInfos.Length ? _currentWeaponInfos[_selectedWeaponIndex] : null;
            _selectedWeapon = _selectedWeaponInfo != null ? _selectedWeaponInfo.Weapon : null;
            _selectedFcs = _selectedWeaponInfo != null ? (_selectedWeaponInfo.FCS ?? (_selectedWeapon != null ? _selectedWeapon.FCS : null)) : null;
            _selectedAmmo = ResolveSelectedAmmo(_selectedWeapon, out _selectedAmmoSource);
        }

        private int FindWeaponInfoIndex(WeaponSystemInfo currentWeaponInfo)
        {
            if (_currentWeaponInfos == null || currentWeaponInfo == null) return -1;
            for (int i = 0; i < _currentWeaponInfos.Length; i++)
            {
                var info = _currentWeaponInfos[i];
                if (info == currentWeaponInfo) return i;
                if (info != null && currentWeaponInfo != null && info.Weapon != null && currentWeaponInfo.Weapon != null && info.Weapon == currentWeaponInfo.Weapon)
                    return i;
            }
            return -1;
        }

        private void EnsureSnapshots()
        {
            if (!ReferenceEquals(_fcsSnapshot != null ? _fcsSnapshot.Target : null, _selectedFcs))
                _fcsSnapshot = CaptureSnapshot(_selectedFcs);
            if (!ReferenceEquals(_ammoSnapshot != null ? _ammoSnapshot.Target : null, _selectedAmmo))
                _ammoSnapshot = CaptureSnapshot(_selectedAmmo);
        }

        void OnGUI()
        {
            if (!_showUi) return;

            Matrix4x4 oldMatrix = GUI.matrix;
            float scale = Mathf.Clamp(_uiScale, 0.6f, 2.5f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            Rect scaledRect = new Rect(_windowRect.x / scale, _windowRect.y / scale, _windowRect.width / scale, _windowRect.height / scale);
            scaledRect = GUI.Window(12358, scaledRect, DrawWindow, "FCS Debug UI");
            _windowRect = new Rect(scaledRect.x * scale, scaledRect.y * scale, scaledRect.width * scale, scaledRect.height * scale);

            GUI.matrix = oldMatrix;
        }

        private void DrawWindow(int id)
        {
            Color oldColor = GUI.color;
            GUI.color = _windowBackgroundColor;
            GUI.DrawTexture(new Rect(0f, 0f, _windowRect.width / Mathf.Clamp(_uiScale, 0.6f, 2.5f), _windowRect.height / Mathf.Clamp(_uiScale, 0.6f, 2.5f)), Texture2D.whiteTexture);
            GUI.color = oldColor;

            GUILayout.BeginVertical();
            GUILayout.Label("F8: 切换窗口 | F9: 刷新 | F11: 打开Ammo页");
            DrawTabButtons();
            _mainScroll = GUILayout.BeginScrollView(_mainScroll);
            switch (_activeTab)
            {
                case DebugTab.Context:
                    DrawContextTab();
                    break;
                case DebugTab.FCS:
                    DrawFcsTab();
                    break;
                case DebugTab.UiReticle:
                    DrawUiReticleTab();
                    break;
                case DebugTab.Ammo:
                    DrawAmmoTab();
                    break;
                case DebugTab.Thermal:
                    DrawThermalTab();
                    break;
                case DebugTab.EMES18:
                    DrawEmes18Tab();
                    break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawTabButtons()
        {
            GUILayout.BeginHorizontal();
            DrawTabButton(DebugTab.Context, "Context");
            DrawTabButton(DebugTab.FCS, "FCS");
            DrawTabButton(DebugTab.UiReticle, "UI/Reticle");
            DrawTabButton(DebugTab.Ammo, "Ammo");
            DrawTabButton(DebugTab.Thermal, "Thermal");
            DrawTabButton(DebugTab.EMES18, "EMES18");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh All", GUILayout.Width(120f))) RefreshAll();
            GUILayout.EndHorizontal();
        }

        private void DrawTabButton(DebugTab tab, string label)
        {
            if (GUILayout.Button(_activeTab == tab ? ("> " + label) : label, GUILayout.Width(130f)))
                OpenTab(tab);
        }

        private void DrawContextTab()
        {
            GUILayout.Label("UI");
            GUILayout.BeginHorizontal();
            GUILayout.Label("UI Scale", GUILayout.Width(70f));
            _uiScale = GUILayout.HorizontalSlider(_uiScale, 0.6f, 2.5f, GUILayout.Width(220f));
            GUILayout.Label(_uiScale.ToString("F2", CultureInfo.InvariantCulture) + "x", GUILayout.Width(60f));
            if (GUILayout.Button("1.00x", GUILayout.Width(70f))) _uiScale = 1f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Weapon Follow", GUILayout.Width(90f));
            _followPlayerWeapon = GUILayout.Toggle(_followPlayerWeapon, _followPlayerWeapon ? "Follow CurrentPlayerWeapon" : "Manual Selection", GUILayout.Width(210f));
            if (GUILayout.Button("Sync To Player Weapon", GUILayout.Width(150f)))
            {
                _followPlayerWeapon = true;
                RefreshAll();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            _contextScroll = GUILayout.BeginScrollView(_contextScroll, GUILayout.Height(730f));
            GUILayout.Label($"Vehicle: {DescribeUnityObject(_currentVehicle)}");
            GUILayout.Label($"WeaponsManager: {DescribeUnityObject(_currentWeaponsManager)}");
            GUILayout.Label($"Selected Weapon: {DescribeWeaponInfo(_selectedWeaponInfo)}");
            GUILayout.Label($"Selected FCS: {DescribeUnityObject(_selectedFcs)}");
            GUILayout.Label($"Selected Ammo: {DescribeAmmo(_selectedAmmo)}");
            GUILayout.Label($"Active Optic: {DescribeUnityObject(_activeOptic)}");
            GUILayout.Label($"Active CameraSlot: {DescribeUnityObject(_activeCameraSlot)}");
            if (_activeCameraSlot != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Dump Active Slot", GUILayout.Width(140f))) DumpCameraSlot(_activeCameraSlot, "ActiveSlot");
                if (GUILayout.Button("Dump Thermal Params", GUILayout.Width(160f))) DumpThermalParams(_activeCameraSlot, "ActiveSlotThermal");
                if (GUILayout.Button("Dump FLIR Material", GUILayout.Width(160f))) DumpFlirMaterial(_activeCameraSlot, "ActiveSlotFlir");
                GUILayout.EndHorizontal();
                GUILayout.Label($"Slot Vision={_activeCameraSlot.VisionType} | FLIR={_activeCameraSlot.FLIRWidth}x{_activeCameraSlot.FLIRHeight} | Filter={_activeCameraSlot.FLIRFilterMode} | FOV={_activeCameraSlot.DefaultFov:F1}");
                DrawActiveThermalEditor();
            }
            GUILayout.Space(8f);
            GUILayout.Label("当前载具武器列表");
            _weaponScroll = GUILayout.BeginScrollView(_weaponScroll, GUILayout.Height(420f));
            if (_currentWeaponInfos.Length == 0)
            {
                GUILayout.Label("未找到当前载具武器。");
            }
            else
            {
                for (int i = 0; i < _currentWeaponInfos.Length; i++)
                {
                    var info = _currentWeaponInfos[i];
                    if (info == null) continue;
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(_selectedWeaponIndex == i ? ">" : "Sel", GUILayout.Width(40f)))
                    {
                        _selectedWeaponIndex = i;
                        _followPlayerWeapon = false;
                        RefreshAll();
                    }
                    string ammoSource;
                    var ammo = ResolveSelectedAmmo(info.Weapon, out ammoSource);
                    GUILayout.Label($"[{i}] {DescribeWeaponInfo(info)} | Ammo={DescribeAmmo(ammo)} | Source={ammoSource}");
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndScrollView();
        }

        private static void DumpCameraSlot(CameraSlot slot, string label)
        {
            if (slot == null)
            {
                MelonLogger.Msg("[FCSDebugUI] Dump CameraSlot => null");
                return;
            }

            MelonLogger.Msg($"[FCSDebugUI] Dump CameraSlot => {label} ({slot.name})");
            MelonLogger.Msg($"  VisionType = {slot.VisionType}");
            MelonLogger.Msg($"  IsExterior = {slot.IsExterior}");
            MelonLogger.Msg($"  BaseBlur = {slot.BaseBlur}");
            MelonLogger.Msg($"  DefaultFov = {slot.DefaultFov}");
            MelonLogger.Msg($"  OtherFovs = {(slot.OtherFovs != null ? string.Join(", ", slot.OtherFovs.Select(v => v.ToString("F2", CultureInfo.InvariantCulture)).ToArray()) : "null")}");
            MelonLogger.Msg($"  SpriteType = {slot.SpriteType}");
            MelonLogger.Msg($"  LinkedDaySight = {DescribeUnityObject(slot.LinkedDaySight)}");
            MelonLogger.Msg($"  LinkedNightSight = {DescribeUnityObject(slot.LinkedNightSight)}");
            MelonLogger.Msg($"  IsLinkedNightSight = {slot.IsLinkedNightSight}");
            MelonLogger.Msg($"  WasUsingNightMode = {slot.WasUsingNightMode}");
        }

        private static void DumpThermalParams(CameraSlot slot, string label)
        {
            if (slot == null)
            {
                MelonLogger.Msg("[FCSDebugUI] Dump Thermal => null");
                return;
            }

            MelonLogger.Msg($"[FCSDebugUI] Dump Thermal => {label} ({slot.name})");
            MelonLogger.Msg($"  VisionType = {slot.VisionType}");
            MelonLogger.Msg($"  OverrideFLIRResolution = {slot.OverrideFLIRResolution}");
            MelonLogger.Msg($"  FLIRWidth = {slot.FLIRWidth}");
            MelonLogger.Msg($"  FLIRHeight = {slot.FLIRHeight}");
            MelonLogger.Msg($"  FLIRFilterMode = {slot.FLIRFilterMode}");
            MelonLogger.Msg($"  CanToggleFlirPolarity = {slot.CanToggleFlirPolarity}");
            MelonLogger.Msg($"  FLIRBlitMaterialOverride = {DescribeUnityObject(slot.FLIRBlitMaterialOverride)}");
            MelonLogger.Msg($"  BaseBlur = {slot.BaseBlur}");
            MelonLogger.Msg($"  VibrationShakeMultiplier = {slot.VibrationShakeMultiplier}");

            SimpleNightVision snv = slot.GetComponent<SimpleNightVision>();
            MelonLogger.Msg($"  SimpleNightVision = {DescribeUnityObject(snv)}");
            if (snv != null)
            {
                FieldInfo postField = typeof(SimpleNightVision).GetField("_postVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object volume = postField != null ? postField.GetValue(snv) : null;
                MelonLogger.Msg($"  SNV._postVolume = {DescribeUnityObject(volume as UnityEngine.Object)}");
            }
        }

        private static void DumpFlirMaterial(CameraSlot slot, string label)
        {
            if (slot == null)
            {
                MelonLogger.Msg("[FCSDebugUI] Dump FLIR Material => null slot");
                return;
            }

            Material material = slot.FLIRBlitMaterialOverride;
            if (material == null)
            {
                MelonLogger.Msg($"[FCSDebugUI] Dump FLIR Material => {label}: no override material");
                return;
            }

            MelonLogger.Msg($"[FCSDebugUI] Dump FLIR Material => {label} ({material.name}) shader={material.shader?.name ?? "null"}");
            string[] floatProps = new string[] { "_Contrast", "_Brightness", "_Gain", "_Exposure", "_Intensity", "_Gamma", "_WhitePoint", "_BlackPoint", "_Threshold" };
            for (int i = 0; i < floatProps.Length; i++)
            {
                string prop = floatProps[i];
                if (!material.HasProperty(prop))
                    continue;

                MelonLogger.Msg($"  {prop} = {material.GetFloat(prop).ToString("F4", CultureInfo.InvariantCulture)}");
            }

            string[] colorProps = new string[] { "_Tint", "_Color", "_HotColor", "_ColdColor" };
            for (int i = 0; i < colorProps.Length; i++)
            {
                string prop = colorProps[i];
                if (!material.HasProperty(prop))
                    continue;

                Color color = material.GetColor(prop);
                MelonLogger.Msg($"  {prop} = ({color.r:F3}, {color.g:F3}, {color.b:F3}, {color.a:F3})");
            }

            string[] textureProps = new string[] { "_ColorRamp", "_Noise", "_PixelCookie", "_MainTex" };
            for (int i = 0; i < textureProps.Length; i++)
            {
                string prop = textureProps[i];
                if (!material.HasProperty(prop))
                    continue;

                MelonLogger.Msg($"  {prop} = {DescribeUnityObject(material.GetTexture(prop))}");
            }

            MelonLogger.Msg($"  Keywords = {(material.shaderKeywords != null && material.shaderKeywords.Length > 0 ? string.Join(", ", material.shaderKeywords) : "none")}");
        }

        private void DrawActiveThermalEditor()
        {
            CameraSlot slot = _activeCameraSlot;
            if (slot == null || slot.VisionType != NightVisionType.Thermal)
                return;

            GUILayout.Space(6f);
            GUILayout.Label("Thermal Quick Tweaks");
            DrawThermalFloatField("BaseBlur", "thermal.base_blur", slot.BaseBlur, delegate(float value) { slot.BaseBlur = value; });
            DrawThermalFloatField("Shake", "thermal.shake", slot.VibrationShakeMultiplier, delegate(float value) { slot.VibrationShakeMultiplier = value; });

            Material material = slot.FLIRBlitMaterialOverride;
            if (material == null)
            {
                GUILayout.Label("No FLIRBlitMaterialOverride");
                return;
            }

            if (material.HasProperty("_Tint"))
            {
                Color tint = material.GetColor("_Tint");
                DrawThermalFloatField("Tint R", "thermal.tint.r", tint.r, delegate(float value) { SetTintComponent(material, 0, value); });
                DrawThermalFloatField("Tint G", "thermal.tint.g", tint.g, delegate(float value) { SetTintComponent(material, 1, value); });
                DrawThermalFloatField("Tint B", "thermal.tint.b", tint.b, delegate(float value) { SetTintComponent(material, 2, value); });
                DrawThermalFloatField("Tint A", "thermal.tint.a", tint.a, delegate(float value) { SetTintComponent(material, 3, value); });

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Tint 0.85", GUILayout.Width(90f))) material.SetColor("_Tint", new Color(0.85f, 0.85f, 0.85f, tint.a));
                if (GUILayout.Button("Tint 0.70", GUILayout.Width(90f))) material.SetColor("_Tint", new Color(0.70f, 0.70f, 0.70f, tint.a));
                if (GUILayout.Button("Tint Reset", GUILayout.Width(100f))) material.SetColor("_Tint", Color.white);
                GUILayout.EndHorizontal();
            }
        }

        private void DrawThermalFloatField(string label, string key, float currentValue, Action<float> setter)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90f));
            string text = GetCachedInput(key, currentValue.ToString("F3", CultureInfo.InvariantCulture));
            text = GUILayout.TextField(text, GUILayout.Width(90f));
            _inputs[key] = text;
            GUILayout.Label(currentValue.ToString("F3", CultureInfo.InvariantCulture), GUILayout.Width(80f));
            if (GUILayout.Button("Apply", GUILayout.Width(60f)))
            {
                float parsed;
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    setter(parsed);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawEmes18QuickControls()
        {
            // UI/Reticle 页面保留的 EMES18 快速控制按钮
            GUILayout.Space(6f);
            GUILayout.Label("EMES18 Quick Controls");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open EMES18 Tab", GUILayout.Width(150f)))
            {
                OpenTab(DebugTab.EMES18);
            }
            if (GUILayout.Button("Apply Day Reticle", GUILayout.Width(150f)))
            {
                UsableOptic targetDayOptic = _activeOptic;
                if (targetDayOptic == null && _activeCameraSlot != null)
                    targetDayOptic = _activeCameraSlot.GetComponent<UsableOptic>();
                if (targetDayOptic != null && targetDayOptic.slot != null && targetDayOptic.slot.VisionType != NightVisionType.None)
                {
                    try { targetDayOptic = targetDayOptic.slot.LinkedDaySight != null ? targetDayOptic.slot.LinkedDaySight.PairedOptic : targetDayOptic; }
                    catch { }
                }
                if (!EMES18Optic.DebugReapplyDayReticle(targetDayOptic))
                    MelonLogger.Warning("[FCSDebugUI] EMES18 day reticle reapply failed");
                RefreshTargets();
            }
            if (GUILayout.Button("Apply Thermal NFOV", GUILayout.Width(150f)))
            {
                UsableOptic targetThermalOptic = ResolveEmes18ThermalTargetOptic();
                if (!EMES18Optic.DebugReapplyThermalReticles(targetThermalOptic))
                    MelonLogger.Warning("[FCSDebugUI] EMES18 thermal reticle reapply failed");
                RefreshTargets();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawEmes18Tab()
        {
            GUILayout.Label("EMES18 Reticle Configuration");
            GUILayout.Space(6f);

            _emes18Scroll = GUILayout.BeginScrollView(_emes18Scroll);

            // === 全局开关控制 ===
            GUILayout.Label("Global Scope Controls");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"DefaultScopeCtl: {(EMES18Optic.DebugDisableDefaultScopeControl ? "OFF" : "ON")}", GUILayout.Width(190f));
            if (GUILayout.Button(EMES18Optic.DebugDisableDefaultScopeControl ? "Enable" : "Disable", GUILayout.Width(100f)))
                EMES18Optic.DebugDisableDefaultScopeControl = !EMES18Optic.DebugDisableDefaultScopeControl;
            if (GUILayout.Button("Restore DefaultScope", GUILayout.Width(150f)))
            {
                EMES18Optic.DebugDisableDefaultScopeControl = true;
                EMES18Optic.TickGlobalDefaultScopeState();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"NormalizeScope: {(EMES18Optic.DebugDisableNormalizeScopeSprite ? "OFF" : "ON")}", GUILayout.Width(190f));
            if (GUILayout.Button(EMES18Optic.DebugDisableNormalizeScopeSprite ? "Enable" : "Disable", GUILayout.Width(100f)))
                EMES18Optic.DebugDisableNormalizeScopeSprite = !EMES18Optic.DebugDisableNormalizeScopeSprite;
            GUILayout.Label($"ThermalHide: {(EMES18Optic.DebugDisableThermalDefaultScopeHide ? "OFF" : "ON")}", GUILayout.Width(150f));
            if (GUILayout.Button(EMES18Optic.DebugDisableThermalDefaultScopeHide ? "Enable" : "Disable", GUILayout.Width(100f)))
                EMES18Optic.DebugDisableThermalDefaultScopeHide = !EMES18Optic.DebugDisableThermalDefaultScopeHide;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"DayShowScope: {(EMES18Optic.DebugDisableDayDefaultScopeShow ? "OFF" : "ON")}", GUILayout.Width(190f));
            if (GUILayout.Button(EMES18Optic.DebugDisableDayDefaultScopeShow ? "Enable" : "Disable", GUILayout.Width(100f)))
                EMES18Optic.DebugDisableDayDefaultScopeShow = !EMES18Optic.DebugDisableDayDefaultScopeShow;
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            // === 日间准星调整 ===
            DrawEmes18DayReticleDebug();

            GUILayout.Space(10f);

            // === 热成像准星调整 ===
            DrawEmes18ThermalReticleDebug();

            GUILayout.EndScrollView();
        }


        private void DrawEmes18DayReticleDebug()
        {
            UsableOptic targetDayOptic = _activeOptic;
            if (targetDayOptic == null && _activeCameraSlot != null)
                targetDayOptic = _activeCameraSlot.GetComponent<UsableOptic>();

            if (targetDayOptic != null && targetDayOptic.slot != null && targetDayOptic.slot.VisionType != NightVisionType.None)
            {
                try { targetDayOptic = targetDayOptic.slot.LinkedDaySight != null ? targetDayOptic.slot.LinkedDaySight.PairedOptic : targetDayOptic; }
                catch { }
            }

            GUILayout.Space(6f);
            GUILayout.Label("EMES18 Day Reticle Tuning");
            GUILayout.Label("Target Day Optic: " + DescribeUnityObject(targetDayOptic));
            DrawThermalFloatField("Circle Radius", "emes18.day.circle.radius", EMES18Optic.DebugDayCircleRadiusMrad, delegate(float value) { EMES18Optic.DebugDayCircleRadiusMrad = value; });
            DrawThermalFloatField("Circle Thick", "emes18.day.circle.thick", EMES18Optic.DebugDayCircleThicknessMrad, delegate(float value) { EMES18Optic.DebugDayCircleThicknessMrad = value; });
            DrawThermalFloatField("Inner Offset", "emes18.day.inner.offset", EMES18Optic.DebugDayInnerOffsetMrad, delegate(float value) { EMES18Optic.DebugDayInnerOffsetMrad = value; });
            DrawThermalFloatField("Inner Length", "emes18.day.inner.length", EMES18Optic.DebugDayInnerLengthMrad, delegate(float value) { EMES18Optic.DebugDayInnerLengthMrad = value; });
            DrawThermalFloatField("Pos Scale", "emes18.day.scale.pos", EMES18Optic.DebugDayAllPositionScale, delegate(float value) { EMES18Optic.DebugDayAllPositionScale = value; });
            DrawThermalFloatField("Len Scale", "emes18.day.scale.len", EMES18Optic.DebugDayAllLengthScale, delegate(float value) { EMES18Optic.DebugDayAllLengthScale = value; });
            DrawThermalFloatField("Thick Scale", "emes18.day.scale.thick", EMES18Optic.DebugDayAllThicknessScale, delegate(float value) { EMES18Optic.DebugDayAllThicknessScale = value; });

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Day Reticle", GUILayout.Width(150f)))
            {
                if (!EMES18Optic.DebugReapplyDayReticle(targetDayOptic))
                    MelonLogger.Warning("[FCSDebugUI] EMES18 day reticle reapply failed");
                RefreshTargets();
            }
            if (GUILayout.Button("Reset Day Tuning", GUILayout.Width(150f)))
            {
                EMES18Optic.ResetDayReticleDebugTuning();
                if (!EMES18Optic.DebugReapplyDayReticle(targetDayOptic))
                    MelonLogger.Warning("[FCSDebugUI] EMES18 day reticle reset/reapply failed");
                RefreshTargets();
            }
            if (GUILayout.Button("Log Day Values", GUILayout.Width(130f)))
                MelonLogger.Msg("[FCSDebugUI] " + EMES18Optic.DescribeDayReticleDebugTuning());
            GUILayout.EndHorizontal();

            // === 外框线单独调整 ===
            GUILayout.Space(8f);
            GUILayout.Label("Outer Lines (0-5): PosX/PosY/Len/Thick offsets");
            string[] outerLabels = { "R-Horiz", "L-Upper", "L-Horiz", "R-Upper", "R-Lower", "L-Lower" };
            for (int i = 0; i < 6; i++)
            {
                if (EMES18Optic.DebugDayOuterLineOffsets[i] == null)
                    EMES18Optic.DebugDayOuterLineOffsets[i] = new EMES18Optic.EmesDayLineDebugTuning();
                var tuning = EMES18Optic.DebugDayOuterLineOffsets[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"[{i}] {outerLabels[i]}:", GUILayout.Width(90f));
                DrawLineTuningField("PX", $"day.outer.{i}.px", tuning.PositionOffsetX, v => { tuning.PositionOffsetX = v; EMES18Optic.DebugReapplyDayReticle(targetDayOptic); }, 50f);
                DrawLineTuningField("PY", $"day.outer.{i}.py", tuning.PositionOffsetY, v => { tuning.PositionOffsetY = v; EMES18Optic.DebugReapplyDayReticle(targetDayOptic); }, 50f);
                DrawLineTuningField("L", $"day.outer.{i}.len", tuning.LengthOffset, v => { tuning.LengthOffset = v; EMES18Optic.DebugReapplyDayReticle(targetDayOptic); }, 50f);
                DrawLineTuningField("T", $"day.outer.{i}.thick", tuning.ThicknessOffset, v => { tuning.ThicknessOffset = v; EMES18Optic.DebugReapplyDayReticle(targetDayOptic); }, 50f);
                GUILayout.EndHorizontal();
            }

            // === 十字线单独调整 ===
            GUILayout.Space(8f);
            GUILayout.Label("Cross Lines (0-5): PosX/PosY/Len/Thick offsets");
            string[] crossLabels = { "Right", "Left", "Down", "Up", "UpVert", "DownVert" };
            for (int i = 0; i < 6; i++)
            {
                if (EMES18Optic.DebugDayCrossLineOffsets[i] == null)
                    EMES18Optic.DebugDayCrossLineOffsets[i] = new EMES18Optic.EmesDayLineDebugTuning();
                var tuning = EMES18Optic.DebugDayCrossLineOffsets[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"[{i}] {crossLabels[i]}:", GUILayout.Width(90f));
                DrawLineTuningField("PX", $"day.cross.{i}.px", tuning.PositionOffsetX, v => { tuning.PositionOffsetX = v; EMES18Optic.DebugReapplyDayReticle(targetDayOptic); }, 50f);
                DrawLineTuningField("PY", $"day.cross.{i}.py", tuning.PositionOffsetY, v => { tuning.PositionOffsetY = v; EMES18Optic.DebugReapplyDayReticle(targetDayOptic); }, 50f);
                DrawLineTuningField("L", $"day.cross.{i}.len", tuning.LengthOffset, v => { tuning.LengthOffset = v; EMES18Optic.DebugReapplyDayReticle(targetDayOptic); }, 50f);
                DrawLineTuningField("T", $"day.cross.{i}.thick", tuning.ThicknessOffset, v => { tuning.ThicknessOffset = v; EMES18Optic.DebugReapplyDayReticle(targetDayOptic); }, 50f);
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Line Offsets", GUILayout.Width(150f)))
            {
                EMES18Optic.ResetDayLineOffsets();
                if (!EMES18Optic.DebugReapplyDayReticle(targetDayOptic))
                    MelonLogger.Warning("[FCSDebugUI] EMES18 day reticle reset offsets failed");
                RefreshTargets();
            }
            if (GUILayout.Button("Log Line Offsets", GUILayout.Width(150f)))
            {
                for (int i = 0; i < 6; i++)
                {
                    var o = EMES18Optic.DebugDayOuterLineOffsets[i];
                    var c = EMES18Optic.DebugDayCrossLineOffsets[i];
                    MelonLogger.Msg($"[FCSDebugUI] Outer[{i}]: PX={o?.PositionOffsetX:F3} PY={o?.PositionOffsetY:F3} L={o?.LengthOffset:F3} T={o?.ThicknessOffset:F3}");
                    MelonLogger.Msg($"[FCSDebugUI] Cross[{i}]: PX={c?.PositionOffsetX:F3} PY={c?.PositionOffsetY:F3} L={c?.LengthOffset:F3} T={c?.ThicknessOffset:F3}");
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawLineTuningField(string label, string key, float currentValue, Action<float> setter, float width = 50f)
        {
            GUILayout.Label(label, GUILayout.Width(25f));
            string text = GetCachedInput(key, currentValue.ToString("F3", CultureInfo.InvariantCulture));
            text = GUILayout.TextField(text, GUILayout.Width(width));
            _inputs[key] = text;
            if (GUILayout.Button("Set", GUILayout.Width(35f)))
            {
                float parsed;
                if (TryParseFloat(text, out parsed))
                {
                    setter(parsed);
                    MelonLogger.Msg($"[FCSDebugUI] Set {key} = {parsed:F3}");
                }
            }
        }

        private bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private UsableOptic ResolveEmes18ThermalTargetOptic()
        {
            UsableOptic targetThermalOptic = _activeOptic;
            if (targetThermalOptic == null && _activeCameraSlot != null)
                targetThermalOptic = _activeCameraSlot.GetComponent<UsableOptic>();

            if (targetThermalOptic != null && targetThermalOptic.slot != null && targetThermalOptic.slot.VisionType == NightVisionType.Thermal)
                return targetThermalOptic;

            try
            {
                if (targetThermalOptic != null && targetThermalOptic.slot != null && targetThermalOptic.slot.LinkedNightSight != null)
                {
                    UsableOptic linked = targetThermalOptic.slot.LinkedNightSight.PairedOptic;
                    if (linked != null)
                        return linked;
                }
            }
            catch { }

            try
            {
                if (_activeCameraSlot != null && _activeCameraSlot.LinkedNightSight != null && _activeCameraSlot.LinkedNightSight.PairedOptic != null)
                    return _activeCameraSlot.LinkedNightSight.PairedOptic;
            }
            catch { }

            return null;
        }

        private void DrawEmes18ThermalReticleDebug()
        {
            UsableOptic targetThermalOptic = ResolveEmes18ThermalTargetOptic();

            GUILayout.Space(6f);
            GUILayout.Label("EMES18 Thermal Reticle Tuning");
            GUILayout.Label("Target Thermal Optic: " + DescribeUnityObject(targetThermalOptic));

            GUILayout.Label("Thermal WFOV");
            GUILayout.Label("WFOV reticle is locked to donor/original; no runtime tuning applied.");
            if (GUILayout.Button("Log WFOV Values", GUILayout.Width(150f)))
                MelonLogger.Msg("[FCSDebugUI] Thermal WFOV (locked donor) " + EMES18Optic.DescribeThermalWfovReticleDebugTuning());

            GUILayout.Label("Thermal NFOV");
            DrawThermalFloatField("Circle Radius", "emes18.thermal.nfov.circle.radius", EMES18Optic.DebugThermalNfovCircleRadiusMrad, delegate(float value) { EMES18Optic.DebugThermalNfovCircleRadiusMrad = value; });
            DrawThermalFloatField("Circle Thick", "emes18.thermal.nfov.circle.thick", EMES18Optic.DebugThermalNfovCircleThicknessMrad, delegate(float value) { EMES18Optic.DebugThermalNfovCircleThicknessMrad = value; });
            DrawThermalFloatField("Inner Offset", "emes18.thermal.nfov.inner.offset", EMES18Optic.DebugThermalNfovInnerOffsetMrad, delegate(float value) { EMES18Optic.DebugThermalNfovInnerOffsetMrad = value; });
            DrawThermalFloatField("Inner Length", "emes18.thermal.nfov.inner.length", EMES18Optic.DebugThermalNfovInnerLengthMrad, delegate(float value) { EMES18Optic.DebugThermalNfovInnerLengthMrad = value; });
            DrawThermalFloatField("Pos Scale", "emes18.thermal.nfov.scale.pos", EMES18Optic.DebugThermalNfovAllPositionScale, delegate(float value) { EMES18Optic.DebugThermalNfovAllPositionScale = value; });
            DrawThermalFloatField("Len Scale", "emes18.thermal.nfov.scale.len", EMES18Optic.DebugThermalNfovAllLengthScale, delegate(float value) { EMES18Optic.DebugThermalNfovAllLengthScale = value; });
            DrawThermalFloatField("Thick Scale", "emes18.thermal.nfov.scale.thick", EMES18Optic.DebugThermalNfovAllThicknessScale, delegate(float value) { EMES18Optic.DebugThermalNfovAllThicknessScale = value; });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply NFOV", GUILayout.Width(130f)))
            {
                if (!EMES18Optic.DebugReapplyThermalReticles(targetThermalOptic))
                    MelonLogger.Warning("[FCSDebugUI] EMES18 thermal NFOV reapply failed");
                RefreshTargets();
            }
            if (GUILayout.Button("Reset NFOV", GUILayout.Width(130f)))
            {
                EMES18Optic.ResetThermalNfovReticleDebugTuning();
                if (!EMES18Optic.DebugReapplyThermalReticles(targetThermalOptic))
                    MelonLogger.Warning("[FCSDebugUI] EMES18 thermal NFOV reset/reapply failed");
                RefreshTargets();
            }
            if (GUILayout.Button("Log NFOV Values", GUILayout.Width(150f)))
                MelonLogger.Msg("[FCSDebugUI] Thermal NFOV " + EMES18Optic.DescribeThermalNfovReticleDebugTuning());
            GUILayout.EndHorizontal();
        }

        private static void SetTintComponent(Material material, int index, float value)
        {
            if (material == null || !material.HasProperty("_Tint"))
                return;

            Color tint = material.GetColor("_Tint");
            switch (index)
            {
                case 0: tint.r = value; break;
                case 1: tint.g = value; break;
                case 2: tint.b = value; break;
                case 3: tint.a = value; break;
            }
            material.SetColor("_Tint", tint);
        }
        private void DrawFcsTab()
        {
            if (_selectedFcs == null)
            {
                GUILayout.Label("当前武器没有可用 FCS。");
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Context", GUILayout.Width(140f))) RefreshAll();
            if (GUILayout.Button("Revert Snapshot", GUILayout.Width(140f)))
            {
                RevertSnapshot(_fcsSnapshot);
                RefreshAll();
            }
            if (GUILayout.Button("Dump FCS", GUILayout.Width(120f))) DumpObject(_selectedFcs, "FCS");
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("FCS 快速参数");
            DrawNamedMembers(_selectedFcs, "fcs.quick",
                "StabsActive", "CurrentStabMode", "SuperelevateWeapon", "SuperleadWeapon",
                "InertialCompensation", "DynamicLead", "HasManualMode", "UseDeltaD",
                "MaxLaserRange", "RegisteredRangeLimits", "RangeStep", "RangeGranularity",
                "FireGateAngle", "TraverseBufferSeconds", "DefaultRange", "TargetRange",
                "ReportedRange", "LaserAim", "EnableRangeInterpolation", "DisplayRangeIncrement",
                "RangeInterpolationSmoothTime", "RangeInterpolationMaxSpeed", "MarkRangeInvalidBelow",
                "IgnoreRangeBelow", "RefreshAimOnAmmoChange", "ZeroReticleRangeInAutoMode");

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Raw 搜索", GUILayout.Width(70f));
            _fcsSearch = GUILayout.TextField(_fcsSearch, GUILayout.Width(260f));
            GUILayout.EndHorizontal();
            _fcsRawScroll = DrawEditableMembers(_selectedFcs, "fcs.raw", _fcsSearch, _fcsRawScroll);

            // Spike 锁定半径调整
            GUILayout.Space(10f);
            GUILayout.Label("=== Marder SPIKE 设置 ===");
            GUILayout.BeginHorizontal();
            GUILayout.Label("锁定半径", GUILayout.Width(80f));
            float radius = UnderdogsDebug.SpikeLockRadius;
            radius = GUILayout.HorizontalSlider(radius, 0.1f, 10f, GUILayout.Width(200f));
            GUILayout.Label(radius.ToString("F1") + "m", GUILayout.Width(50f));
            if (GUILayout.Button("重置", GUILayout.Width(50f))) radius = 2f;
            UnderdogsDebug.SpikeLockRadius = radius;
            GUILayout.EndHorizontal();
        }

        private void DrawAmmoTab()
        {
            if (_selectedWeapon == null)
            {
                GUILayout.Label("当前未解析到武器。");
                return;
            }

            GUILayout.Label($"Weapon: {DescribeWeaponInfo(_selectedWeaponInfo)}");
            GUILayout.Label($"Ammo: {DescribeAmmo(_selectedAmmo)}");
            GUILayout.Label($"Ammo Source: {_selectedAmmoSource}");
            GUILayout.Label("说明: 已发射出去的 LiveRound 不会追溯修改；未发射/待装填/当前引用同一 AmmoType 的后续弹会吃到变更。");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Context", GUILayout.Width(140f))) RefreshAll();
            if (GUILayout.Button("Revert Snapshot", GUILayout.Width(140f)))
            {
                RevertSnapshot(_ammoSnapshot);
                SyncSpecialAmmoBindings(_selectedAmmo);
                RefreshAll();
            }
            if (GUILayout.Button("Dump Ammo", GUILayout.Width(120f))) DumpObject(_selectedAmmo, "Ammo");
            if (GUILayout.Button("Sync Legacy Bridge", GUILayout.Width(160f)))
            {
                SyncSpecialAmmoBindings(_selectedAmmo);
                MelonLogger.Msg("[FCSDebugUI] 已同步旧弹药调试桥接参数");
            }
            GUILayout.EndHorizontal();

            if (_selectedAmmo == null)
            {
                GUILayout.Label("当前武器没有解析到 AmmoType。");
                return;
            }

            GUILayout.Space(6f);
            GUILayout.Label("Ammo 快速参数");
            DrawNamedMembers(_selectedAmmo, "ammo.quick",
                "Name", "Caliber", "Mass", "MuzzleVelocity", "RhaPenetration",
                "TntEquivalentKg", "Coeff", "SpallMultiplier", "MinSpallRha", "MaxSpallRha",
                "MaximumRange", "RangedFuseTime", "RhaToFuse", "Guidance", "TurnSpeed",
                "SpiralPower", "SpiralAngularRate", "GuidanceLeadDistance", "GuidanceLockoutTime",
                "GuidanceNoLockoutRange", "GuidanceNoLoiterRange", "NoisePowerX", "NoisePowerY",
                "NoiseTimeScale", "LoiterAltitude", "LoiterEndDistance", "DiveAngle", "ClimbAngle",
                "AimPointMarch", "NoLeadCompensation", "ArmingDistance", "ImpactFuseTime",
                "CertainRicochetAngle", "UseErrorCorrection", "AlwaysProduceBlast", "NoPenSpall");

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Raw 搜索", GUILayout.Width(70f));
            _ammoSearch = GUILayout.TextField(_ammoSearch, GUILayout.Width(260f));
            GUILayout.EndHorizontal();
            _selectedAmmo = ResolveSelectedAmmo(_selectedWeapon, out _selectedAmmoSource);
            _ammoRawScroll = DrawEditableMembers(_selectedAmmo, "ammo.raw", _ammoSearch, _ammoRawScroll);
        }

        private void DrawThermalTab()
        {
            if (!_thermalInitialized)
            {
                UEResourceController.InitThermalRampTextures();
                _thermalEditConfig = UEResourceController.GlobalThermalConfig.Clone();
                _thermalInitialized = true;
            }

            GUILayout.Label("热成像配置 (Thermal Vision)");
            GUILayout.Space(6f);

            if (!string.IsNullOrEmpty(_thermalStatusMessage) && Time.unscaledTime < _thermalStatusTime)
                GUILayout.Label($"Status: {_thermalStatusMessage}");

            _thermalScroll = GUILayout.BeginScrollView(_thermalScroll);

            GUILayout.Label("颜色模式 (Color Mode)");
            foreach (ThermalColorMode mode in Enum.GetValues(typeof(ThermalColorMode)))
            {
                GUILayout.BeginHorizontal();
                bool isSelected = _thermalEditConfig.ColorMode == mode;
                if (GUILayout.Button(isSelected ? ">" : "", GUILayout.Width(30f)))
                {
                    _thermalEditConfig.ColorMode = mode;
                    switch (mode)
                    {
                        case ThermalColorMode.WhiteHot:
                            _thermalEditConfig.ColdColor = new Color(0f, 0f, 0f, 1f);
                            _thermalEditConfig.HotColor = new Color(1f, 1f, 1f, 1f);
                            break;
                        case ThermalColorMode.BlackHot:
                            _thermalEditConfig.ColdColor = new Color(1f, 1f, 1f, 1f);
                            _thermalEditConfig.HotColor = new Color(0f, 0f, 0f, 1f);
                            break;
                        case ThermalColorMode.GreenHot:
                            _thermalEditConfig.ColdColor = new Color(0f, 0.2f, 0f, 1f);
                            _thermalEditConfig.HotColor = new Color(0f, 1f, 0.3f, 1f);
                            break;
                    }
                }
                GUILayout.Label(UEResourceController.GetThermalColorModeName(mode), GUILayout.Width(120f));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(15f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("应用到当前热成像 (Apply)", GUILayout.Width(180f)))
            {
                UEResourceController.UpdateGlobalThermalConfig(_thermalEditConfig);
                ApplyThermalToActiveSlot();
                _thermalStatusMessage = "配置已应用";
                _thermalStatusTime = Time.unscaledTime + 3f;
            }
            if (GUILayout.Button("重置 (Reset)", GUILayout.Width(100f)))
            {
                _thermalEditConfig = new ThermalConfig { ColorMode = ThermalColorMode.GreenHot };
                UEResourceController.UpdateGlobalThermalConfig(_thermalEditConfig);
                _thermalStatusMessage = "已重置";
                _thermalStatusTime = Time.unscaledTime + 3f;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            var globalConfig = UEResourceController.GlobalThermalConfig;
            GUILayout.Label($"当前模式: {UEResourceController.GetThermalColorModeName(globalConfig.ColorMode)}");

            // 当前 CameraSlot 信息
            if (_activeCameraSlot != null)
            {
                GUILayout.Space(10f);
                GUILayout.Label("当前 CameraSlot:");
                GUILayout.Label($"  名称: {_activeCameraSlot.name}");
                GUILayout.Label($"  视觉类型: {_activeCameraSlot.VisionType}");
                GUILayout.Label($"  是否热成像: {_activeCameraSlot.VisionType == NightVisionType.Thermal}");
                if (_activeCameraSlot.FLIRBlitMaterialOverride != null)
                {
                    GUILayout.Label($"  材质: {_activeCameraSlot.FLIRBlitMaterialOverride.name}");
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawColorEdit(string label, ref Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100f));

            // R
            GUILayout.Label("R", GUILayout.Width(20f));
            float r = color.r;
            string rText = GetCachedInput($"thermal.color.{label}.r", r.ToString("F2", CultureInfo.InvariantCulture));
            rText = GUILayout.TextField(rText, GUILayout.Width(60f));
            _inputs[$"thermal.color.{label}.r"] = rText;
            if (float.TryParse(rText, NumberStyles.Float, CultureInfo.InvariantCulture, out r))
                color.r = Mathf.Clamp01(r);

            // G
            GUILayout.Label("G", GUILayout.Width(20f));
            float g = color.g;
            string gText = GetCachedInput($"thermal.color.{label}.g", g.ToString("F2", CultureInfo.InvariantCulture));
            gText = GUILayout.TextField(gText, GUILayout.Width(60f));
            _inputs[$"thermal.color.{label}.g"] = gText;
            if (float.TryParse(gText, NumberStyles.Float, CultureInfo.InvariantCulture, out g))
                color.g = Mathf.Clamp01(g);

            // B
            GUILayout.Label("B", GUILayout.Width(20f));
            float b = color.b;
            string bText = GetCachedInput($"thermal.color.{label}.b", b.ToString("F2", CultureInfo.InvariantCulture));
            bText = GUILayout.TextField(bText, GUILayout.Width(60f));
            _inputs[$"thermal.color.{label}.b"] = bText;
            if (float.TryParse(bText, NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                color.b = Mathf.Clamp01(b);

            // 颜色预览
            GUI.color = color;
            GUILayout.Box(string.Empty, GUILayout.Width(40f), GUILayout.Height(20f));
            GUI.color = Color.white;

            GUILayout.EndHorizontal();
        }

        private void DrawThermalFloatSlider(string label, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(200f));
            string text = GetCachedInput($"thermal.{label}", value.ToString("F2", CultureInfo.InvariantCulture));
            text = GUILayout.TextField(text, GUILayout.Width(70f));
            _inputs[$"thermal.{label}"] = text;
            GUILayout.Label(value.ToString("F2", CultureInfo.InvariantCulture), GUILayout.Width(60f));
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                value = Mathf.Clamp(parsed, min, max);
            GUILayout.EndHorizontal();
        }

        private void ApplyThermalToActiveSlot()
        {
            if (_activeCameraSlot != null && _activeCameraSlot.VisionType == NightVisionType.Thermal)
            {
                UEResourceController.ApplyThermalToCameraSlot(_activeCameraSlot);
            }
            else
            {
                // 尝试找到当前载具的热成像槽
                if (_currentVehicle != null)
                {
                    var slots = _currentVehicle.GetComponentsInChildren<CameraSlot>(true);
                    foreach (var slot in slots)
                    {
                        if (slot != null && slot.VisionType == NightVisionType.Thermal)
                        {
                            UEResourceController.ApplyThermalToCameraSlot(slot);
                            if (true)
                                MelonLogger.Msg($"[FCSDebugUI] 应用热成像配置到: {slot.name}");
                        }
                    }
                }
            }
        }

        private void DrawNamedMembers(object target, string prefix, params string[] names)
        {
            if (target == null || names == null) return;
            for (int i = 0; i < names.Length; i++)
            {
                MemberAdapter member;
                if (TryGetMember(target.GetType(), names[i], out member))
                    DrawMemberRow(target, member, prefix);
            }
        }

        private Vector2 DrawEditableMembers(object target, string prefix, string search, Vector2 scroll)
        {
            if (target == null)
            {
                GUILayout.Label("null");
                return scroll;
            }

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(340f));
            var members = GetMembers(target.GetType());
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (!string.IsNullOrEmpty(search) && member.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                DrawMemberRow(target, member, prefix);
            }
            GUILayout.EndScrollView();
            return scroll;
        }

        private void DrawMemberRow(object target, MemberAdapter member, string prefix)
        {
            object value;
            try
            {
                value = member.GetValue(target);
            }
            catch (Exception ex)
            {
                GUILayout.Label(member.Name + " = <error: " + ex.Message + ">");
                return;
            }

            string key = prefix + "." + member.Name;
            Type type = member.ValueType;
            GUILayout.BeginHorizontal();
            GUILayout.Label(member.Name, GUILayout.Width(220f));

            if (type == typeof(bool))
            {
                bool oldValue = value != null && (bool)value;
                bool newValue = GUILayout.Toggle(oldValue, string.Empty, GUILayout.Width(20f));
                GUILayout.Label(newValue.ToString(), GUILayout.Width(70f));
                if (newValue != oldValue && member.CanWrite) TrySetMemberValue(target, member, newValue);
            }
            else if (type.IsEnum)
            {
                GUILayout.Label(value != null ? value.ToString() : "null", GUILayout.Width(160f));
                if (member.CanWrite && GUILayout.Button("Next", GUILayout.Width(60f)))
                    TrySetMemberValue(target, member, GetNextEnumValue(type, value));
            }
            else if (type == typeof(int))
            {
                string text = GetCachedInput(key, value != null ? ((int)value).ToString(CultureInfo.InvariantCulture) : "0");
                text = GUILayout.TextField(text, GUILayout.Width(120f));
                _inputs[key] = text;
                GUILayout.Label(value != null ? value.ToString() : "null", GUILayout.Width(90f));
                if (member.CanWrite && GUILayout.Button("Apply", GUILayout.Width(60f)))
                {
                    int parsed;
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        TrySetMemberValue(target, member, parsed);
                }
            }
            else if (type == typeof(float) || type == typeof(double))
            {
                float current = value != null ? Convert.ToSingle(value, CultureInfo.InvariantCulture) : 0f;
                string text = GetCachedInput(key, current.ToString("F3", CultureInfo.InvariantCulture));
                text = GUILayout.TextField(text, GUILayout.Width(120f));
                _inputs[key] = text;
                GUILayout.Label(current.ToString("F3", CultureInfo.InvariantCulture), GUILayout.Width(90f));
                if (member.CanWrite && GUILayout.Button("Apply", GUILayout.Width(60f)))
                {
                    float parsed;
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                        TrySetMemberValue(target, member, type == typeof(double) ? (object)(double)parsed : parsed);
                }
            }
            else if (type == typeof(string))
            {
                string text = GetCachedInput(key, value as string ?? string.Empty);
                text = GUILayout.TextField(text, GUILayout.Width(240f));
                _inputs[key] = text;
                if (member.CanWrite && GUILayout.Button("Apply", GUILayout.Width(60f)))
                    TrySetMemberValue(target, member, text);
            }
            else if (type == typeof(Vector2))
            {
                DrawVector2MemberRow(target, member, key, value != null ? (Vector2)value : Vector2.zero);
                GUILayout.EndHorizontal();
                return;
            }
            else if (type == typeof(Vector3))
            {
                DrawVector3MemberRow(target, member, key, value != null ? (Vector3)value : Vector3.zero);
                GUILayout.EndHorizontal();
                return;
            }
            else
            {
                GUILayout.Label(DescribeObjectValue(value), GUILayout.Width(520f));
            }

            GUILayout.EndHorizontal();
        }

        private void DrawVector2MemberRow(object target, MemberAdapter member, string key, Vector2 value)
        {
            string xKey = key + ".x";
            string yKey = key + ".y";
            string xText = GetCachedInput(xKey, value.x.ToString("F3", CultureInfo.InvariantCulture));
            string yText = GetCachedInput(yKey, value.y.ToString("F3", CultureInfo.InvariantCulture));
            xText = GUILayout.TextField(xText, GUILayout.Width(80f));
            yText = GUILayout.TextField(yText, GUILayout.Width(80f));
            _inputs[xKey] = xText;
            _inputs[yKey] = yText;
            GUILayout.Label($"({value.x:F2}, {value.y:F2})", GUILayout.Width(160f));
            if (member.CanWrite && GUILayout.Button("Apply", GUILayout.Width(60f)))
            {
                float x;
                float y;
                if (float.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out x) && float.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                    TrySetMemberValue(target, member, new Vector2(x, y));
            }
        }

        private void DrawVector3MemberRow(object target, MemberAdapter member, string key, Vector3 value)
        {
            string xKey = key + ".x";
            string yKey = key + ".y";
            string zKey = key + ".z";
            string xText = GetCachedInput(xKey, value.x.ToString("F3", CultureInfo.InvariantCulture));
            string yText = GetCachedInput(yKey, value.y.ToString("F3", CultureInfo.InvariantCulture));
            string zText = GetCachedInput(zKey, value.z.ToString("F3", CultureInfo.InvariantCulture));
            xText = GUILayout.TextField(xText, GUILayout.Width(70f));
            yText = GUILayout.TextField(yText, GUILayout.Width(70f));
            zText = GUILayout.TextField(zText, GUILayout.Width(70f));
            _inputs[xKey] = xText;
            _inputs[yKey] = yText;
            _inputs[zKey] = zText;
            GUILayout.Label($"({value.x:F2}, {value.y:F2}, {value.z:F2})", GUILayout.Width(210f));
            if (member.CanWrite && GUILayout.Button("Apply", GUILayout.Width(60f)))
            {
                float x;
                float y;
                float z;
                if (float.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                    float.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                    float.TryParse(zText, NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                    TrySetMemberValue(target, member, new Vector3(x, y, z));
            }
        }
        private ObjectSnapshot CaptureSnapshot(object target)
        {
            if (target == null) return null;
            var snapshot = new ObjectSnapshot { Target = target };
            var members = GetMembers(target.GetType());
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (!member.CanWrite || !IsSnapshotType(member.ValueType)) continue;
                try
                {
                    snapshot.Entries.Add(new SnapshotEntry { Member = member, Value = member.GetValue(target) });
                }
                catch
                {
                }
            }
            return snapshot;
        }

        private void RevertSnapshot(ObjectSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Target == null) return;
            for (int i = 0; i < snapshot.Entries.Count; i++)
            {
                try
                {
                    snapshot.Entries[i].Member.SetValue(snapshot.Target, snapshot.Entries[i].Value);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[FCSDebugUI] Snapshot revert failed: " + snapshot.Entries[i].Member.Name + " | " + ex.Message);
                }
            }
        }

        private static bool IsSnapshotType(Type type)
        {
            return type == typeof(bool) || type == typeof(int) || type == typeof(float) || type == typeof(double) ||
                   type == typeof(string) || type.IsEnum || type == typeof(Vector2) || type == typeof(Vector3);
        }

        private static List<MemberAdapter> GetMembers(Type type)
        {
            return GetMembers(type, true);
        }

        private static List<MemberAdapter> GetMembers(Type type, bool includeUnityBaseMembers)
        {
            var cache = includeUnityBaseMembers ? MemberCache : MemberCacheWithoutUnityBase;
            List<MemberAdapter> cached;
            if (cache.TryGetValue(type, out cached)) return cached;

            var result = new List<MemberAdapter>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                if (!includeUnityBaseMembers && (walk == typeof(UnityEngine.Object) || walk == typeof(UnityEngine.ScriptableObject) || walk == typeof(UnityEngine.Component) || walk == typeof(UnityEngine.Behaviour) || walk == typeof(MonoBehaviour)))
                    break;

                var fields = walk.GetFields(flags | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field.IsStatic) continue;
                    string key = "F:" + field.Name;
                    if (!seen.Add(key)) continue;
                    result.Add(new MemberAdapter
                    {
                        Name = field.Name,
                        ValueType = field.FieldType,
                        CanWrite = !field.IsInitOnly,
                        IsField = true,
                        Field = field
                    });
                }

                var properties = walk.GetProperties(flags | BindingFlags.DeclaredOnly);
                for (int i = 0; i < properties.Length; i++)
                {
                    var property = properties[i];
                    if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
                    string key = "P:" + property.Name;
                    if (!seen.Add(key)) continue;
                    result.Add(new MemberAdapter
                    {
                        Name = property.Name,
                        ValueType = property.PropertyType,
                        CanWrite = property.CanWrite,
                        IsField = false,
                        Property = property
                    });
                }
            }

            result.Sort(delegate(MemberAdapter a, MemberAdapter b) { return string.CompareOrdinal(a.Name, b.Name); });
            cache[type] = result;
            return result;
        }

        private static bool TryGetMember(Type type, string name, out MemberAdapter found)
        {
            return TryGetMember(type, name, out found, true);
        }

        private static bool TryGetMember(Type type, string name, out MemberAdapter found, bool includeUnityBaseMembers)
        {
            var members = GetMembers(type, includeUnityBaseMembers);
            for (int i = 0; i < members.Count; i++)
            {
                if (string.Equals(members[i].Name, name, StringComparison.Ordinal))
                {
                    found = members[i];
                    return true;
                }
            }
            found = null;
            return false;
        }

        private void TrySetMemberValue(object target, MemberAdapter member, object value)
        {
            try
            {
                object oldValue = null;
                try
                {
                    oldValue = member.GetValue(target);
                }
                catch
                {
                }

                member.SetValue(target, value);
                if (target is AmmoType) SyncSpecialAmmoBindings((AmmoType)target);
                MelonLogger.Msg("[FCSDebugUI] Set OK | " + target.GetType().Name + "." + member.Name + " | old=" + DescribeObjectValue(oldValue) + " | new=" + DescribeObjectValue(value));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[FCSDebugUI] SetValue failed: " + member.Name + " | " + ex.Message);
            }
        }

        private static object GetNextEnumValue(Type enumType, object current)
        {
            var values = Enum.GetValues(enumType);
            if (values == null || values.Length == 0) return current;
            if (current == null) return values.GetValue(0);
            int index = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (Equals(values.GetValue(i), current))
                {
                    index = i;
                    break;
                }
            }
            return values.GetValue((index + 1) % values.Length);
        }

        private string GetCachedInput(string key, string fallback)
        {
            string value;
            if (_inputs.TryGetValue(key, out value)) return value;
            _inputs[key] = fallback;
            return fallback;
        }

        private AmmoType ResolveSelectedAmmo(WeaponSystem weapon, out string source)
        {
            source = "none";
            if (weapon == null) return null;
            if (weapon.CurrentAmmoType != null)
            {
                source = "Weapon.CurrentAmmoType";
                return weapon.CurrentAmmoType;
            }
            if (weapon.Feed == null) return null;
            if (weapon.Feed.AmmoTypeInBreech != null)
            {
                source = "Feed.AmmoTypeInBreech";
                return weapon.Feed.AmmoTypeInBreech;
            }
            if (weapon.Feed.ReadyRack != null && weapon.Feed.ReadyRack.ClipTypes != null && weapon.Feed.ReadyRack.ClipTypes.Length > 0)
            {
                var clip = weapon.Feed.ReadyRack.ClipTypes[0];
                if (clip != null && clip.MinimalPattern != null && clip.MinimalPattern.Length > 0 && clip.MinimalPattern[0] != null)
                {
                    source = "ReadyRack.ClipTypes[0].MinimalPattern[0]";
                    return clip.MinimalPattern[0].AmmoType;
                }
            }
            source = "unresolved";
            return null;
        }

        private UsableOptic ResolveActiveOptic()
        {
            var activeSlot = CameraSlot.ActiveInstance;
            if (activeSlot != null)
            {
                var opticFromSlot = activeSlot.GetComponentInParent<UsableOptic>();
                if (opticFromSlot != null) return opticFromSlot;
            }

            var cameraRoot = CameraManager.MainCam != null ? CameraManager.MainCam.transform : null;
            if (cameraRoot != null)
            {
                var opticFromCam = cameraRoot.GetComponentInParent<UsableOptic>();
                if (opticFromCam != null) return opticFromCam;
            }

            return null;
        }
        private void DrawUiReticleTab()
        {
            GUILayout.Label($"EMES root: {(_emes18Root != null ? _emes18Root.name : "null")}");
            GUILayout.Label($"Active optic: {DescribeUnityObject(_activeOptic)}");
            GUILayout.Label($"MainCam parent: {(CameraManager.MainCam != null && CameraManager.MainCam.transform.parent != null ? CameraManager.MainCam.transform.parent.name : "null")}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Targets", GUILayout.Width(130f))) RefreshTargets();
            if (GUILayout.Button("Print All", GUILayout.Width(100f))) PrintAllTargets();
            if (GUILayout.Button("Print Selected", GUILayout.Width(110f))) PrintSelected();
            if (GUILayout.Button("Export Current Reticle", GUILayout.Width(160f))) ExportCurrentReticle();
            if (GUILayout.Button("Import Last Export", GUILayout.Width(150f)))
            {
                if (string.IsNullOrWhiteSpace(_lastReticleExportPath))
                    _reticleImportStatus = "No export file yet";
                else
                    ImportReticleFromFile(_lastReticleExportPath);
            }
            if (GUILayout.Button("Import Path", GUILayout.Width(110f))) ImportReticleFromFile(_reticleImportPath);
            if (GUILayout.Button("Toggle Active", GUILayout.Width(110f))) ToggleSelectedActive();
            if (GUILayout.Button("Toggle Visual", GUILayout.Width(110f))) ToggleSelectedVisual();
            if (GUILayout.Button("Pull Current", GUILayout.Width(110f))) PullSelectedTransform();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_lastReticleExportPath))
                GUILayout.Label("Last Export: " + _lastReticleExportPath);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Import File", GUILayout.Width(70f));
            _reticleImportPath = GUILayout.TextField(_reticleImportPath, GUILayout.Width(720f));
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_reticleImportStatus))
                GUILayout.Label("Import Status: " + _reticleImportStatus);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"DefaultScopeCtl: {(EMES18Optic.DebugDisableDefaultScopeControl ? "OFF" : "ON")}", GUILayout.Width(190f));
            if (GUILayout.Button(EMES18Optic.DebugDisableDefaultScopeControl ? "Enable DefaultScopeCtl" : "Disable DefaultScopeCtl", GUILayout.Width(180f)))
                EMES18Optic.DebugDisableDefaultScopeControl = !EMES18Optic.DebugDisableDefaultScopeControl;
            if (GUILayout.Button("Restore DefaultScope", GUILayout.Width(150f)))
            {
                EMES18Optic.DebugDisableDefaultScopeControl = true;
                EMES18Optic.TickGlobalDefaultScopeState();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"NormalizeScope: {(EMES18Optic.DebugDisableNormalizeScopeSprite ? "OFF" : "ON")}", GUILayout.Width(190f));
            if (GUILayout.Button(EMES18Optic.DebugDisableNormalizeScopeSprite ? "Enable NormalizeScope" : "Disable NormalizeScope", GUILayout.Width(180f)))
                EMES18Optic.DebugDisableNormalizeScopeSprite = !EMES18Optic.DebugDisableNormalizeScopeSprite;
            GUILayout.Label($"ThermalHide: {(EMES18Optic.DebugDisableThermalDefaultScopeHide ? "OFF" : "ON")}", GUILayout.Width(170f));
            if (GUILayout.Button(EMES18Optic.DebugDisableThermalDefaultScopeHide ? "Enable ThermalHide" : "Disable ThermalHide", GUILayout.Width(170f)))
                EMES18Optic.DebugDisableThermalDefaultScopeHide = !EMES18Optic.DebugDisableThermalDefaultScopeHide;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"DayShowScope: {(EMES18Optic.DebugDisableDayDefaultScopeShow ? "OFF" : "ON")}", GUILayout.Width(190f));
            if (GUILayout.Button(EMES18Optic.DebugDisableDayDefaultScopeShow ? "Enable DayShow" : "Disable DayShow", GUILayout.Width(180f)))
                EMES18Optic.DebugDisableDayDefaultScopeShow = !EMES18Optic.DebugDisableDayDefaultScopeShow;
            if (GUILayout.Button("Toggle SpriteR", GUILayout.Width(120f))) ToggleSelectedSpriteRenderer();
            if (GUILayout.Button("Toggle PostMesh", GUILayout.Width(120f))) ToggleSelectedPostMesh();
            if (GUILayout.Button("Toggle Canvas", GUILayout.Width(110f))) ToggleSelectedCanvas();
            GUILayout.EndHorizontal();

            DrawEmes18QuickControls();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Pos", GUILayout.Width(90f))) ResetSelectedPosition();
            if (GUILayout.Button("Reset Scale", GUILayout.Width(90f))) ResetSelectedScale();
            GUILayout.Label("Filter", GUILayout.Width(40f));
            _targetFilter = GUILayout.TextField(_targetFilter, GUILayout.Width(280f));
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"Targets: {_targets.Count}");
            _targetScroll = GUILayout.BeginScrollView(_targetScroll, GUILayout.Height(300f));
            for (int i = 0; i < _targets.Count; i++)
            {
                var target = _targets[i];
                if (target == null || target.Transform == null) continue;
                if (!string.IsNullOrEmpty(_targetFilter) && target.Path.IndexOf(_targetFilter, StringComparison.OrdinalIgnoreCase) < 0 && target.Kind.IndexOf(_targetFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_selectedElement == target.Transform ? ">" : "Sel", GUILayout.Width(40f))) SelectTarget(target);
                if (GUILayout.Button(target.Transform.gameObject.activeSelf ? "Hide" : "Show", GUILayout.Width(55f)))
                {
                    bool newState = !target.Transform.gameObject.activeSelf;
                    target.Transform.gameObject.SetActive(newState);
                    if (target.Transform == _selectedElement) PullSelectedTransform();
                }
                GUILayout.Label($"[{target.RootLabel}|{target.Kind}] {target.Path}");
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            if (_selectedElement == null)
            {
                GUILayout.Label("No target selected.");
                return;
            }

            GUILayout.Label($"Selected: {_selectedKind} | {_selectedPath}");
            GUILayout.TextArea(GetSelectedStateSummary(), GUILayout.Height(120f));

            GUILayout.Space(4f);
            GUILayout.Label("Transform.localPosition");
            _positionOffset.x = DrawSliderRow("X", _positionOffset.x, -2000f, 2000f);
            _positionOffset.y = DrawSliderRow("Y", _positionOffset.y, -2000f, 2000f);
            _positionOffset.z = DrawSliderRow("Z", _positionOffset.z, -200f, 200f);
            _selectedElement.localPosition = _positionOffset;

            GUILayout.Space(4f);
            GUILayout.Label("Transform.localScale");
            _scaleOffset.x = DrawSliderRow("SX", _scaleOffset.x, 0.01f, 100f);
            _scaleOffset.y = DrawSliderRow("SY", _scaleOffset.y, 0.01f, 100f);
            _scaleOffset.z = DrawSliderRow("SZ", _scaleOffset.z, 0.01f, 100f);
            _selectedElement.localScale = _scaleOffset;

            DrawRectTransformControls();
            DrawCanvasControls();
            DrawNumericInputs();
        }

        private void DrawRectTransformControls()
        {
            var rect = _selectedElement != null ? _selectedElement.GetComponent<RectTransform>() : null;
            if (rect == null) return;

            GUILayout.Space(6f);
            GUILayout.Label("RectTransform");
            DrawVector2Inline("AnchoredPos", "ui.rect.anchored", rect.anchoredPosition, delegate(Vector2 value) { rect.anchoredPosition = value; });
            DrawVector2Inline("SizeDelta", "ui.rect.size", rect.sizeDelta, delegate(Vector2 value) { rect.sizeDelta = value; });
            DrawVector2Inline("Pivot", "ui.rect.pivot", rect.pivot, delegate(Vector2 value) { rect.pivot = value; });
            DrawVector2Inline("AnchorMin", "ui.rect.anchorMin", rect.anchorMin, delegate(Vector2 value) { rect.anchorMin = value; });
            DrawVector2Inline("AnchorMax", "ui.rect.anchorMax", rect.anchorMax, delegate(Vector2 value) { rect.anchorMax = value; });
        }

        private void DrawVector2Inline(string label, string keyPrefix, Vector2 value, Action<Vector2> setter)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110f));
            string xKey = keyPrefix + ".x";
            string yKey = keyPrefix + ".y";
            string xText = GetCachedInput(xKey, value.x.ToString("F3", CultureInfo.InvariantCulture));
            string yText = GetCachedInput(yKey, value.y.ToString("F3", CultureInfo.InvariantCulture));
            xText = GUILayout.TextField(xText, GUILayout.Width(90f));
            yText = GUILayout.TextField(yText, GUILayout.Width(90f));
            _inputs[xKey] = xText;
            _inputs[yKey] = yText;
            GUILayout.Label($"({value.x:F1}, {value.y:F1})", GUILayout.Width(130f));
            if (GUILayout.Button("Apply", GUILayout.Width(60f)))
            {
                float x;
                float y;
                if (float.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out x) && float.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                    setter(new Vector2(x, y));
            }
            GUILayout.EndHorizontal();
        }

        private void DrawNumericInputs()
        {
            GUILayout.Space(6f);
            GUILayout.Label("Numeric Input");
            GUILayout.BeginHorizontal();
            GUILayout.Label("PX", GUILayout.Width(22f));
            _posXInput = GUILayout.TextField(_posXInput, GUILayout.Width(70f));
            GUILayout.Label("PY", GUILayout.Width(22f));
            _posYInput = GUILayout.TextField(_posYInput, GUILayout.Width(70f));
            GUILayout.Label("PZ", GUILayout.Width(22f));
            _posZInput = GUILayout.TextField(_posZInput, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("SX", GUILayout.Width(22f));
            _scaleXInput = GUILayout.TextField(_scaleXInput, GUILayout.Width(70f));
            GUILayout.Label("SY", GUILayout.Width(22f));
            _scaleYInput = GUILayout.TextField(_scaleYInput, GUILayout.Width(70f));
            GUILayout.Label("SZ", GUILayout.Width(22f));
            _scaleZInput = GUILayout.TextField(_scaleZInput, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Apply Numeric Values", GUILayout.Width(180f))) ApplyNumericInputs();
        }

        private void RefreshTargets()
        {
            _targets.Clear();
            _emes18Root = null;
            var activeOptic = _activeOptic != null ? _activeOptic : ResolveActiveOptic();
            if (activeOptic != null)
            {
                var monitors = activeOptic.GetComponentsInChildren<EMES18Optic.EMES18Monitor>(true);
                for (int i = 0; i < monitors.Length; i++)
                {
                    var monitor = monitors[i];
                    if (monitor == null) continue;
                    if (i == 0) _emes18Root = monitor.transform;
                    CollectTargets(monitor.transform, "EMES" + i.ToString(CultureInfo.InvariantCulture));
                }
                CollectTargets(activeOptic.transform, "Optic");
                if (activeOptic.reticleMesh != null) CollectTargets(activeOptic.reticleMesh.transform, "ReticleMesh");
            }

            var scopeRoot = CameraManager.MainCam != null ? CameraManager.MainCam.transform.Find("Scope") : null;
            if (scopeRoot != null) CollectTargets(scopeRoot, "Scope");
            if (CameraManager.MainCam != null) CollectTargets(CameraManager.MainCam.transform, "MainCam");
        }

        private void CollectTargets(Transform root, string label)
        {
            if (root == null) return;
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                if (_targets.Any(target => target.Transform == node)) continue;
                _targets.Add(new DebugTarget
                {
                    Transform = node,
                    Path = BuildPath(root, node, label),
                    Kind = DescribeKind(node),
                    RootLabel = label
                });
            }
            _targets.Sort(delegate(DebugTarget a, DebugTarget b) { return string.CompareOrdinal(a.Path, b.Path); });
        }
        private static string BuildPath(Transform root, Transform node, string label)
        {
            if (node == null) return label + ":null";
            if (node == root) return label + ":" + node.name;
            string path = node.name;
            while (node.parent != null && node.parent != root)
            {
                node = node.parent;
                path = node.name + "/" + path;
            }
            return label + ":" + root.name + "/" + path;
        }

        private string DescribeKind(Transform node)
        {
            if (node == null) return "null";
            if (node.GetComponent<Canvas>() != null) return "Canvas";
            if (node.GetComponent<TextMeshProUGUI>() != null) return "TMP";
            if (node.GetComponent<Image>() != null) return "Image";
            if (node.GetComponent<Graphic>() != null) return "Graphic";
            if (node.GetComponent<ReticleMesh>() != null) return "ReticleMesh";
            if (node.GetComponent<PostMeshComp>() != null) return "PostMesh";
            if (node.GetComponent<SpriteRenderer>() != null) return "SpriteRenderer";
            if (node.GetComponent<Renderer>() != null) return "Renderer";
            return "Transform";
        }

        private void SelectTarget(DebugTarget target)
        {
            if (target == null || target.Transform == null) return;
            _selectedElement = target.Transform;
            _selectedPath = target.Path;
            _selectedKind = target.Kind;
            PullSelectedTransform();
        }

        private void PullSelectedTransform()
        {
            if (_selectedElement == null) return;
            _positionOffset = _selectedElement.localPosition;
            _scaleOffset = _selectedElement.localScale;
            SyncInputsFromCurrentValues();
        }

        private void ResetSelectedPosition()
        {
            if (_selectedElement == null) return;
            _positionOffset = Vector3.zero;
            _selectedElement.localPosition = _positionOffset;
            SyncInputsFromCurrentValues();
        }

        private void ResetSelectedScale()
        {
            if (_selectedElement == null) return;
            _scaleOffset = Vector3.one;
            _selectedElement.localScale = _scaleOffset;
            SyncInputsFromCurrentValues();
        }

        private void ToggleSelectedActive()
        {
            if (_selectedElement == null) return;
            _selectedElement.gameObject.SetActive(!_selectedElement.gameObject.activeSelf);
            PullSelectedTransform();
        }

        private void ToggleSelectedCanvas()
        {
            var canvas = GetSelectedCanvas(true);
            if (canvas == null) return;
            canvas.enabled = !canvas.enabled;
            SyncCanvasInputs(canvas);
        }

        private void ToggleSelectedVisual()
        {
            if (_selectedElement == null) return;
            var sr = _selectedElement.GetComponent<SpriteRenderer>();
            if (sr != null) { sr.enabled = !sr.enabled; return; }
            var canvas = _selectedElement.GetComponent<Canvas>();
            if (canvas != null) { canvas.enabled = !canvas.enabled; return; }
            var renderer = _selectedElement.GetComponent<Renderer>();
            if (renderer != null) { renderer.enabled = !renderer.enabled; return; }
            var behaviour = _selectedElement.GetComponent<Behaviour>();
            if (behaviour != null) behaviour.enabled = !behaviour.enabled;
        }

        private void ToggleSelectedSpriteRenderer()
        {
            if (_selectedElement == null) return;
            var sr = _selectedElement.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = !sr.enabled;
        }

        private void ToggleSelectedPostMesh()
        {
            if (_selectedElement == null) return;
            var pm = _selectedElement.GetComponent<PostMeshComp>();
            if (pm != null) pm.enabled = !pm.enabled;
        }

        private Canvas GetSelectedCanvas(bool includeParent)
        {
            if (_selectedElement == null) return null;
            var canvas = _selectedElement.GetComponent<Canvas>();
            if (canvas != null) return canvas;
            return includeParent ? _selectedElement.GetComponentInParent<Canvas>(true) : null;
        }

        private void SyncCanvasInputs(Canvas canvas)
        {
            if (canvas == null) return;
            _canvasSortInput = canvas.sortingOrder.ToString(CultureInfo.InvariantCulture);
            _canvasPlaneInput = canvas.planeDistance.ToString("F2", CultureInfo.InvariantCulture);
        }

        private void DrawCanvasControls()
        {
            var canvas = GetSelectedCanvas(true);
            GUILayout.Space(6f);
            GUILayout.Label("Canvas");
            if (canvas == null)
            {
                GUILayout.Label("No Canvas on selected object or parents.");
                return;
            }
            GUILayout.Label($"CanvasTarget: {canvas.gameObject.name} | mode={canvas.renderMode} override={canvas.overrideSorting} sort={canvas.sortingOrder} plane={canvas.planeDistance:F2}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(canvas.enabled ? "Canvas Off" : "Canvas On", GUILayout.Width(90f))) { canvas.enabled = !canvas.enabled; SyncCanvasInputs(canvas); }
            if (GUILayout.Button(canvas.overrideSorting ? "Override Off" : "Override On", GUILayout.Width(95f))) { canvas.overrideSorting = !canvas.overrideSorting; SyncCanvasInputs(canvas); }
            if (GUILayout.Button("ScreenCam", GUILayout.Width(90f))) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = CameraManager.MainCam; SyncCanvasInputs(canvas); }
            if (GUILayout.Button("ScreenOverlay", GUILayout.Width(110f))) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; SyncCanvasInputs(canvas); }
            if (GUILayout.Button("WorldSpace", GUILayout.Width(90f))) { canvas.renderMode = RenderMode.WorldSpace; SyncCanvasInputs(canvas); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sort", GUILayout.Width(40f));
            _canvasSortInput = GUILayout.TextField(_canvasSortInput, GUILayout.Width(70f));
            GUILayout.Label("Plane", GUILayout.Width(44f));
            _canvasPlaneInput = GUILayout.TextField(_canvasPlaneInput, GUILayout.Width(70f));
            if (GUILayout.Button("Apply Canvas", GUILayout.Width(110f)))
            {
                int sortOrder;
                float planeDistance;
                if (int.TryParse(_canvasSortInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out sortOrder)) canvas.sortingOrder = sortOrder;
                if (float.TryParse(_canvasPlaneInput, NumberStyles.Float, CultureInfo.InvariantCulture, out planeDistance)) canvas.planeDistance = planeDistance;
                if (canvas.renderMode == RenderMode.ScreenSpaceCamera) canvas.worldCamera = CameraManager.MainCam;
            }
            GUILayout.EndHorizontal();
        }

        private void ApplyNumericInputs()
        {
            if (_selectedElement == null) return;
            float px, py, pz, sx, sy, sz;
            if (float.TryParse(_posXInput, NumberStyles.Float, CultureInfo.InvariantCulture, out px) &&
                float.TryParse(_posYInput, NumberStyles.Float, CultureInfo.InvariantCulture, out py) &&
                float.TryParse(_posZInput, NumberStyles.Float, CultureInfo.InvariantCulture, out pz))
            {
                _positionOffset = new Vector3(px, py, pz);
                _selectedElement.localPosition = _positionOffset;
            }
            if (float.TryParse(_scaleXInput, NumberStyles.Float, CultureInfo.InvariantCulture, out sx) &&
                float.TryParse(_scaleYInput, NumberStyles.Float, CultureInfo.InvariantCulture, out sy) &&
                float.TryParse(_scaleZInput, NumberStyles.Float, CultureInfo.InvariantCulture, out sz))
            {
                _scaleOffset = new Vector3(sx, sy, sz);
                _selectedElement.localScale = _scaleOffset;
            }
        }

        private void SyncInputsFromCurrentValues()
        {
            _posXInput = _positionOffset.x.ToString("F3", CultureInfo.InvariantCulture);
            _posYInput = _positionOffset.y.ToString("F3", CultureInfo.InvariantCulture);
            _posZInput = _positionOffset.z.ToString("F3", CultureInfo.InvariantCulture);
            _scaleXInput = _scaleOffset.x.ToString("F3", CultureInfo.InvariantCulture);
            _scaleYInput = _scaleOffset.y.ToString("F3", CultureInfo.InvariantCulture);
            _scaleZInput = _scaleOffset.z.ToString("F3", CultureInfo.InvariantCulture);
            SyncCanvasInputs(GetSelectedCanvas(true));
        }

        private float DrawSliderRow(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(40f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(280f));
            GUILayout.Label(value.ToString("F2", CultureInfo.InvariantCulture), GUILayout.Width(80f));
            GUILayout.EndHorizontal();
            return value;
        }

        private string GetSelectedStateSummary()
        {
            if (_selectedElement == null) return "null";
            string summary = $"name={_selectedElement.name}\nactive={_selectedElement.gameObject.activeSelf}\nlocalPosition={_selectedElement.localPosition}\nlocalScale={_selectedElement.localScale}";
            var rect = _selectedElement.GetComponent<RectTransform>();
            if (rect != null) summary += $"\nanchoredPosition={rect.anchoredPosition}\nsizeDelta={rect.sizeDelta}";
            var canvas = GetSelectedCanvas(true);
            if (canvas != null) summary += $"\ncanvas={canvas.name} mode={canvas.renderMode} sort={canvas.sortingOrder} plane={canvas.planeDistance:F2}";
            return summary;
        }

        private void PrintAllTargets()
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                var target = _targets[i];
                if (target == null || target.Transform == null) continue;
                MelonLogger.Msg($"[FCSDebugUI] [{target.RootLabel}|{target.Kind}] {target.Path}");
            }
        }

        private void PrintSelected()
        {
            if (_selectedElement == null) return;
            MelonLogger.Msg("[FCSDebugUI] Selected\n" + GetSelectedStateSummary());
        }

        private void DumpObject(object target, string tag)
        {
            if (target == null) { MelonLogger.Msg("[FCSDebugUI] " + tag + " = null"); return; }
            MelonLogger.Msg("[FCSDebugUI] Dump " + tag + " => " + DescribeObjectValue(target));
            var members = GetMembers(target.GetType());
            for (int i = 0; i < members.Count; i++)
            {
                object value;
                try { value = members[i].GetValue(target); }
                catch (Exception ex) { value = "<error: " + ex.Message + ">"; }
                MelonLogger.Msg("  " + members[i].Name + " = " + DescribeObjectValue(value));
            }
        }
        private void ExportCurrentReticle()
        {
            try
            {
                var context = ResolveCurrentReticleContext();
                if (context == null || context.Mesh == null || context.Reticle == null)
                {
                    MelonLogger.Warning("[FCSDebugUI] 导出失败：当前没有可用的 ReticleMesh/ReticleSO");
                    return;
                }

                string exportDir = Path.Combine(Directory.GetCurrentDirectory(), "ReticleExports");
                Directory.CreateDirectory(exportDir);

                string safeName = SanitizeFileName(context.Reticle.name);
                string filePath = Path.Combine(exportDir, DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + safeName + ".txt");

                var sb = new StringBuilder(32768);
                sb.AppendLine("# Reticle Export");
                sb.AppendLine("FormatVersion=3");
                sb.AppendLine("ExportTime=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine("Optic=" + DescribeUnityObject(_activeOptic));
                sb.AppendLine("ReticleMesh=" + DescribeUnityObject(context.Mesh));
                sb.AppendLine("ReticleSO=" + DescribeUnityObject(context.Reticle));
                sb.AppendLine("SelectedElement=" + (_selectedElement != null ? _selectedElement.name : "null"));
                sb.AppendLine();
                sb.AppendLine("## Transform Hierarchy");
                AppendTransformTree(sb, context.Root, 0);
                sb.AppendLine();
                sb.AppendLine("## Editable Transform Paths");
                AppendEditableTransformPaths(sb, context.Root);
                sb.AppendLine();
                sb.AppendLine("## ReticleSO Summary");
                AppendReticleSummary(sb, context.Reticle);
                sb.AppendLine();
                sb.AppendLine("## Parametric Graphics");
                AppendParametricReticle(sb, context.Reticle);
                sb.AppendLine();
                sb.AppendLine("## Resolved Preview");
                AppendResolvedPreview(sb, context.Mesh, context.Reticle);
                sb.AppendLine();
                sb.AppendLine("## Editable Paths");
                AppendEditablePaths(sb, context.Reticle, "reticleSO", 0, 5, new HashSet<object>(new ReferenceEqualityComparer()), true, false);
                sb.AppendLine();
                sb.AppendLine("## ReticleSO Reflection Dump");
                AppendObjectDump(sb, context.Reticle, 0, 4, new HashSet<object>(new ReferenceEqualityComparer()), true, false);

                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                _lastReticleExportPath = filePath;
                _reticleImportPath = filePath;
                _reticleImportStatus = "Exported";
                MelonLogger.Msg("[FCSDebugUI] Reticle 导出成功: " + filePath);
            }
            catch (Exception ex)
            {
                _reticleImportStatus = "Export failed: " + ex.Message;
                MelonLogger.Warning("[FCSDebugUI] Reticle 导出失败: " + ex.Message);
            }
        }

        private ReticleContext ResolveCurrentReticleContext()
        {
            ReticleMesh reticleMesh = null;
            if (_selectedElement != null)
                reticleMesh = _selectedElement.GetComponentInParent<ReticleMesh>(true);

            if (reticleMesh == null)
                reticleMesh = _activeOptic != null ? _activeOptic.reticleMesh : null;

            if (reticleMesh == null && _activeOptic != null)
                reticleMesh = FindFallbackReticleMesh(_activeOptic, null, true);

            if (reticleMesh == null || reticleMesh.reticleSO == null)
                return null;

            return new ReticleContext
            {
                Mesh = reticleMesh,
                Reticle = reticleMesh.reticleSO,
                Root = reticleMesh.transform,
                Optic = _activeOptic,
                IsOverride = IsImportedOverrideMesh(reticleMesh)
            };
        }

        private ReticleMesh FindFallbackReticleMesh(UsableOptic optic, ReticleMesh exclude, bool includeInactive)
        {
            if (optic == null) return null;

            var meshes = optic.GetComponentsInChildren<ReticleMesh>(includeInactive);
            ReticleMesh imported = null;
            ReticleMesh stock = null;
            for (int i = 0; i < meshes.Length; i++)
            {
                var mesh = meshes[i];
                if (mesh == null || mesh == exclude || mesh.reticleSO == null) continue;
                if (IsImportedOverrideMesh(mesh))
                {
                    if (imported == null) imported = mesh;
                    continue;
                }
                if (stock == null) stock = mesh;
            }
            return imported ?? stock;
        }

        private static bool IsImportedOverrideMesh(ReticleMesh reticleMesh)
        {
            return reticleMesh != null && (reticleMesh.GetComponent<ImportedReticleMarker>() != null || (!string.IsNullOrEmpty(reticleMesh.name) && reticleMesh.name.IndexOf(ImportedReticleSuffix, StringComparison.Ordinal) >= 0));
        }

        private static string RemoveImportedSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf(ImportedReticleSuffix, StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }

        private void ImportReticleFromFile(string filePath)
        {
            // 当前这条文本导入覆盖链只适合调试导出的直接 path 覆盖，
            // 不适合作为 EMES18 生产 reticle 工作流（无法可靠表达本次 donor/day tree 改装路径）。
            // 保留它仅用于后续调试；EMES18 正式实现改为代码内直接构建 ReticleTree。
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    _reticleImportStatus = "Import failed: file path is empty";
                    MelonLogger.Warning("[FCSDebugUI] Reticle 导入失败：文件路径为空");
                    return;
                }

                string normalizedPath = filePath.Trim().Trim('"');
                if (!File.Exists(normalizedPath))
                {
                    _reticleImportStatus = "Import failed: file not found";
                    MelonLogger.Warning("[FCSDebugUI] Reticle 导入失败：文件不存在 " + normalizedPath);
                    return;
                }

                var context = ResolveCurrentReticleContext();
                if (context == null || context.Mesh == null || context.Reticle == null || context.Root == null)
                {
                    _reticleImportStatus = "Import failed: no active reticle";
                    MelonLogger.Warning("[FCSDebugUI] Reticle 导入失败：当前没有可用的 ReticleMesh/ReticleSO");
                    return;
                }

                context = EnsureImportTargetContext(context);
                if (context == null || context.Mesh == null || context.Reticle == null || context.Root == null)
                {
                    _reticleImportStatus = "Import failed: cannot create override reticle";
                    MelonLogger.Warning("[FCSDebugUI] Reticle 导入失败：无法创建 override ReticleMesh");
                    return;
                }

                int applied = 0;
                int skipped = 0;
                int failed = 0;
                string[] lines = File.ReadAllLines(normalizedPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    if (trimmed.StartsWith("##", StringComparison.Ordinal))
                        continue;

                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string left = trimmed.Substring(0, equalsIndex).Trim();
                    string right = trimmed.Substring(equalsIndex + 1).Trim();

                    if (left.Length == 0 || left == "ExportTime" || left == "FormatVersion" || left == "Optic" || left == "ReticleMesh" || left == "ReticleSO" || left == "SelectedElement")
                        continue;
                    if (left.EndsWith(".$type", StringComparison.Ordinal) || left.EndsWith(".$value", StringComparison.Ordinal))
                    {
                        skipped++;
                        continue;
                    }
                    if (right.StartsWith("<", StringComparison.Ordinal))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        bool handled = false;
                        if (left.StartsWith("reticleSO", StringComparison.Ordinal))
                            handled = ApplyReticleImportPath(context.Reticle, left, right);
                        else if (left.StartsWith("transform[", StringComparison.Ordinal))
                        {
                            if (!ImportTransformPathsByDefault)
                            {
                                skipped++;
                                continue;
                            }
                            handled = ApplyTransformImportPath(context.Root, left, right);
                        }

                        if (handled) applied++;
                        else skipped++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        MelonLogger.Warning("[FCSDebugUI] Reticle 导入失败 | line " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + ex.Message + " | " + trimmed);
                    }
                }

                RefreshTargets();
                RefreshReticleVisuals(context.Mesh);
                PullSelectedTransform();
                _reticleImportPath = normalizedPath;
                _reticleImportStatus = "Applied=" + applied.ToString(CultureInfo.InvariantCulture) + ", Skipped=" + skipped.ToString(CultureInfo.InvariantCulture) + ", Failed=" + failed.ToString(CultureInfo.InvariantCulture);
                MelonLogger.Msg("[FCSDebugUI] Reticle 导入完成: " + normalizedPath + " | " + _reticleImportStatus);
            }
            catch (Exception ex)
            {
                _reticleImportStatus = "Import failed: " + ex.Message;
                MelonLogger.Warning("[FCSDebugUI] Reticle 导入失败: " + ex.Message);
            }
        }

        private void RefreshReticleVisuals(ReticleMesh reticleMesh)
        {
            if (reticleMesh == null) return;

            try
            {
                reticleMesh.gameObject.SetActive(true);
                reticleMesh.enabled = true;

                var skinnedMeshRenderer = reticleMesh.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMeshRenderer != null)
                    skinnedMeshRenderer.enabled = true;

                var childRenderers = reticleMesh.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < childRenderers.Length; i++)
                {
                    if (childRenderers[i] == null) continue;
                    childRenderers[i].enabled = true;
                    if (childRenderers[i].gameObject != null)
                        childRenderers[i].gameObject.SetActive(true);
                }

                var linesEnable = reticleMesh.GetComponentsInChildren<ReticleLine>(true);
                for (int i = 0; i < linesEnable.Length; i++)
                {
                    if (linesEnable[i] == null) continue;
                    linesEnable[i].enabled = true;
                    linesEnable[i].gameObject.SetActive(true);
                }

                var circlesEnable = reticleMesh.GetComponentsInChildren<ReticleCircle>(true);
                for (int i = 0; i < circlesEnable.Length; i++)
                {
                    if (circlesEnable[i] == null) continue;
                    circlesEnable[i].enabled = true;
                    circlesEnable[i].gameObject.SetActive(true);
                }

                var postMeshesEnable = reticleMesh.GetComponentsInChildren<PostMeshComp>(true);
                for (int i = 0; i < postMeshesEnable.Length; i++)
                {
                    if (postMeshesEnable[i] == null) continue;
                    postMeshesEnable[i].enabled = true;
                    postMeshesEnable[i].gameObject.SetActive(true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[FCSDebugUI] Reticle pre-enable failed: " + ex.Message);
            }

            try
            {
                InvokeHiddenMethod(reticleMesh, "Clear", false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[FCSDebugUI] Reticle Clear failed: " + ex.Message);
            }

            try
            {
                InvokeHiddenMethod(reticleMesh, "InitVars");
                InvokeHiddenMethod(reticleMesh, "Load");
                InvokeHiddenMethod(reticleMesh, "GenerateMesh");
                InvokeHiddenMethod(reticleMesh, "CacheLights");
                InvokeHiddenMethod(reticleMesh, "UpdateLights");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[FCSDebugUI] Reticle rebuild failed: " + ex.Message);
            }

            try
            {
                var lines = reticleMesh.GetComponentsInChildren<ReticleLine>(true);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] == null) continue;
                    InvokeHiddenMethod(lines[i], "Init");
                    InvokeHiddenMethod(lines[i], "UpdateMesh");
                }

                var circles = reticleMesh.GetComponentsInChildren<ReticleCircle>(true);
                for (int i = 0; i < circles.Length; i++)
                {
                    if (circles[i] == null) continue;
                    InvokeHiddenMethod(circles[i], "Init");
                    InvokeHiddenMethod(circles[i], "UpdateMesh");
                }

                var postMeshes = reticleMesh.GetComponentsInChildren<PostMeshComp>(true);
                for (int i = 0; i < postMeshes.Length; i++)
                {
                    if (postMeshes[i] == null) continue;
                    InvokeHiddenMethod(postMeshes[i], "ProcessSkinned");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[FCSDebugUI] Reticle child refresh failed: " + ex.Message);
            }

            try
            {
                reticleMesh.enabled = false;
                reticleMesh.enabled = true;
                reticleMesh.gameObject.SetActive(false);
                reticleMesh.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[FCSDebugUI] Reticle toggle refresh failed: " + ex.Message);
            }
        }

        private static object InvokeHiddenMethod(object target, string methodName, params object[] args)
        {
            if (target == null) throw new ArgumentNullException("target");

            Type type = target.GetType();
            var argTypes = args != null ? args.Select(arg => arg != null ? arg.GetType() : typeof(object)).ToArray() : Type.EmptyTypes;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            MethodInfo method = null;
            for (Type walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                var methods = walk.GetMethods(flags | BindingFlags.DeclaredOnly);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (!string.Equals(methods[i].Name, methodName, StringComparison.Ordinal)) continue;
                    var parameters = methods[i].GetParameters();
                    if (parameters.Length != argTypes.Length) continue;
                    method = methods[i];
                    break;
                }
                if (method != null) break;
            }

            if (method == null)
                throw new MissingMethodException(type.FullName, methodName);

            return method.Invoke(target, args);
        }

        private ReticleContext EnsureImportTargetContext(ReticleContext context)
        {
            if (context == null || context.Mesh == null || context.Reticle == null)
                return context;

            if (context.IsOverride)
                return context;

            if (context.Optic == null)
                return context;

            try
            {
                var importMesh = context.Mesh;

                if (importMesh.GetComponent<ImportedReticleMarker>() == null)
                    importMesh.gameObject.AddComponent<ImportedReticleMarker>();

                importMesh.gameObject.name = RemoveImportedSuffix(importMesh.gameObject.name);
                importMesh.gameObject.SetActive(true);
                importMesh.enabled = true;

                var renderer = importMesh.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null)
                    renderer.enabled = true;

                if (!IsImportedOverrideMesh(importMesh) || importMesh.reticleSO == null)
                {
                    importMesh.reticleSO = ScriptableObject.Instantiate(context.Reticle);
                    if (importMesh.reticleSO != null)
                        importMesh.reticleSO.name = context.Reticle.name + ImportedReticleSuffix;
                }

                context.Optic.reticleMesh = importMesh;
                RefreshReticleVisuals(importMesh);

                return new ReticleContext
                {
                    Mesh = importMesh,
                    Reticle = importMesh.reticleSO,
                    Root = importMesh.transform,
                    Optic = context.Optic,
                    IsOverride = true
                };
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[FCSDebugUI] Create override reticle failed: " + ex.Message);
                return context;
            }
        }

        private void HideSourceReticleMesh(ReticleMesh reticleMesh)
        {
            if (reticleMesh == null) return;
            try
            {
                reticleMesh.enabled = false;
                var smr = reticleMesh.GetComponent<SkinnedMeshRenderer>();
                if (smr != null) smr.enabled = false;
                foreach (var postMesh in reticleMesh.GetComponentsInChildren<PostMeshComp>(true))
                {
                    if (postMesh != null) postMesh.enabled = false;
                }
                reticleMesh.gameObject.SetActive(false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[FCSDebugUI] Hide source reticle failed: " + ex.Message);
            }
        }

        private void AppendEditableTransformPaths(StringBuilder sb, Transform root)
        {
            if (root == null) return;
            AppendEditableTransformPathRecursive(sb, root, root);
        }

        private void AppendEditableTransformPathRecursive(StringBuilder sb, Transform root, Transform node)
        {
            if (node == null) return;

            string path = "transform[" + FormatEditableValue(BuildStableTransformPath(root, node)) + "]";
            sb.AppendLine(path + ".activeSelf = " + (node.gameObject.activeSelf ? "true" : "false"));
            sb.AppendLine(path + ".localPosition = " + FormatEditableValue(node.localPosition));
            sb.AppendLine(path + ".localScale = " + FormatEditableValue(node.localScale));

            var rect = node.GetComponent<RectTransform>();
            if (rect != null)
            {
                sb.AppendLine(path + ".rect.anchoredPosition = " + FormatEditableValue(rect.anchoredPosition));
                sb.AppendLine(path + ".rect.sizeDelta = " + FormatEditableValue(rect.sizeDelta));
                sb.AppendLine(path + ".rect.pivot = " + FormatEditableValue(rect.pivot));
                sb.AppendLine(path + ".rect.anchorMin = " + FormatEditableValue(rect.anchorMin));
                sb.AppendLine(path + ".rect.anchorMax = " + FormatEditableValue(rect.anchorMax));
            }

            var canvas = node.GetComponent<Canvas>();
            if (canvas != null)
            {
                sb.AppendLine(path + ".canvas.enabled = " + (canvas.enabled ? "true" : "false"));
                sb.AppendLine(path + ".canvas.sortingOrder = " + canvas.sortingOrder.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(path + ".canvas.planeDistance = " + FormatEditableValue(canvas.planeDistance));
            }

            for (int i = 0; i < node.childCount; i++)
                AppendEditableTransformPathRecursive(sb, root, node.GetChild(i));
        }

        private bool ApplyTransformImportPath(Transform root, string path, string valueText)
        {
            string prefix = "transform[";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            int closing = FindTransformPathClosingBracket(path, prefix.Length);
            if (closing < prefix.Length)
                return false;

            string rawPathLiteral = path.Substring(prefix.Length, closing - prefix.Length).Trim();
            string propertyPath = path.Substring(closing + 1).TrimStart('.');
            string transformPath = Convert.ToString(ParseEditableValue(rawPathLiteral, typeof(string)), CultureInfo.InvariantCulture);
            Transform target = ResolveStableTransformPath(root, transformPath);
            if (target == null)
                throw new InvalidOperationException("Transform not found: " + transformPath);

            if (string.Equals(propertyPath, "activeSelf", StringComparison.Ordinal))
            {
                target.gameObject.SetActive((bool)ParseEditableValue(valueText, typeof(bool)));
                return true;
            }

            if (string.Equals(propertyPath, "localPosition", StringComparison.Ordinal))
            {
                target.localPosition = (Vector3)ParseEditableValue(valueText, typeof(Vector3));
                return true;
            }

            if (string.Equals(propertyPath, "localScale", StringComparison.Ordinal))
            {
                target.localScale = (Vector3)ParseEditableValue(valueText, typeof(Vector3));
                return true;
            }

            if (propertyPath.StartsWith("rect.", StringComparison.Ordinal))
            {
                var rect = target.GetComponent<RectTransform>();
                if (rect == null)
                    throw new InvalidOperationException("RectTransform missing on: " + transformPath);

                string rectProperty = propertyPath.Substring(5);
                if (string.Equals(rectProperty, "anchoredPosition", StringComparison.Ordinal)) rect.anchoredPosition = (Vector2)ParseEditableValue(valueText, typeof(Vector2));
                else if (string.Equals(rectProperty, "sizeDelta", StringComparison.Ordinal)) rect.sizeDelta = (Vector2)ParseEditableValue(valueText, typeof(Vector2));
                else if (string.Equals(rectProperty, "pivot", StringComparison.Ordinal)) rect.pivot = (Vector2)ParseEditableValue(valueText, typeof(Vector2));
                else if (string.Equals(rectProperty, "anchorMin", StringComparison.Ordinal)) rect.anchorMin = (Vector2)ParseEditableValue(valueText, typeof(Vector2));
                else if (string.Equals(rectProperty, "anchorMax", StringComparison.Ordinal)) rect.anchorMax = (Vector2)ParseEditableValue(valueText, typeof(Vector2));
                else return false;
                return true;
            }

            if (propertyPath.StartsWith("canvas.", StringComparison.Ordinal))
            {
                var canvas = target.GetComponent<Canvas>();
                if (canvas == null)
                    throw new InvalidOperationException("Canvas missing on: " + transformPath);

                string canvasProperty = propertyPath.Substring(7);
                if (string.Equals(canvasProperty, "enabled", StringComparison.Ordinal)) canvas.enabled = (bool)ParseEditableValue(valueText, typeof(bool));
                else if (string.Equals(canvasProperty, "sortingOrder", StringComparison.Ordinal)) canvas.sortingOrder = (int)ParseEditableValue(valueText, typeof(int));
                else if (string.Equals(canvasProperty, "planeDistance", StringComparison.Ordinal)) canvas.planeDistance = (float)ParseEditableValue(valueText, typeof(float));
                else return false;
                return true;
            }

            return false;
        }

        private static int FindTransformPathClosingBracket(string path, int startIndex)
        {
            bool inString = false;
            for (int i = startIndex; i < path.Length; i++)
            {
                char current = path[i];
                if (current == '"' && !IsEscapedStringChar(path, i))
                    inString = !inString;

                if (!inString && current == ']')
                    return i;
            }
            return -1;
        }

        private static bool IsEscapedStringChar(string text, int quoteIndex)
        {
            int slashCount = 0;
            for (int i = quoteIndex - 1; i >= 0 && text[i] == '\\'; i--)
                slashCount++;
            return (slashCount % 2) == 1;
        }

        private bool ApplyReticleImportPath(object root, string path, string valueText)
        {
            if (root == null || string.IsNullOrEmpty(path) || !path.StartsWith("reticleSO", StringComparison.Ordinal))
                return false;

            string relativePath = path.Length > "reticleSO".Length ? path.Substring("reticleSO".Length) : string.Empty;
            if (string.IsNullOrEmpty(relativePath))
                return false;
            if (relativePath[0] == '.')
                relativePath = relativePath.Substring(1);

            var segments = ParsePathSegments(relativePath);
            if (segments.Count == 0)
                return false;

            object current = root;
            for (int i = 0; i < segments.Count - 1; i++)
                current = ResolvePathSegmentValue(current, segments[i], false);

            var finalSegment = segments[segments.Count - 1];
            return SetPathSegmentValue(current, finalSegment, valueText);
        }

        private void AppendReticleSummary(StringBuilder sb, ReticleSO reticleSO)
        {
            if (reticleSO == null)
            {
                sb.AppendLine("reticleSO=null");
                return;
            }

            sb.AppendLine("name=" + reticleSO.name);
            sb.AppendLine("planes=" + (reticleSO.planes != null ? reticleSO.planes.Count.ToString(CultureInfo.InvariantCulture) : "0"));
            sb.AppendLine("lights=" + (reticleSO.lights != null ? reticleSO.lights.Count.ToString(CultureInfo.InvariantCulture) : "0"));

            if (reticleSO.planes != null)
            {
                for (int i = 0; i < reticleSO.planes.Count; i++)
                {
                    var plane = reticleSO.planes[i];
                    if (plane == null)
                    {
                        sb.AppendLine("plane[" + i.ToString(CultureInfo.InvariantCulture) + "]=null");
                        continue;
                    }

                    int elementCount = plane.elements != null ? plane.elements.Count : 0;
                    sb.AppendLine("plane[" + i.ToString(CultureInfo.InvariantCulture) + "].elements=" + elementCount.ToString(CultureInfo.InvariantCulture));
                    if (plane.elements != null)
                    {
                        for (int j = 0; j < plane.elements.Count; j++)
                        {
                            var element = plane.elements[j];
                            sb.AppendLine("  element[" + j.ToString(CultureInfo.InvariantCulture) + "]=" + (element != null ? element.GetType().FullName : "null"));
                        }
                    }
                }
            }
        }

        private void AppendTransformTree(StringBuilder sb, Transform node, int depth)
        {
            if (node == null) return;
            sb.Append(' ', depth * 2);
            sb.Append("- ");
            sb.Append(node.name);
            sb.Append(" | active=");
            sb.Append(node.gameObject.activeSelf ? "true" : "false");
            sb.Append(" | pos=");
            sb.Append(node.localPosition.ToString());
            sb.Append(" | scale=");
            sb.AppendLine(node.localScale.ToString());

            for (int i = 0; i < node.childCount; i++)
                AppendTransformTree(sb, node.GetChild(i), depth + 1);
        }

        private void AppendObjectDump(StringBuilder sb, object target, int depth, int maxDepth, HashSet<object> visited, bool expandTargetEvenIfUnityObject, bool includeUnityBaseMembers)
        {
            if (target == null)
            {
                sb.AppendLine(Indent(depth) + "null");
                return;
            }

            Type type = target.GetType();
            sb.AppendLine(Indent(depth) + type.FullName);

            bool isExpandedUnityRoot = expandTargetEvenIfUnityObject && typeof(UnityEngine.Object).IsAssignableFrom(type);
            if (depth >= maxDepth || (IsSimpleDumpType(type) && !isExpandedUnityRoot))
            {
                sb.AppendLine(Indent(depth + 1) + DescribeObjectValue(target));
                return;
            }

            if (!type.IsValueType)
            {
                if (visited.Contains(target))
                {
                    sb.AppendLine(Indent(depth + 1) + "<visited>");
                    return;
                }
                visited.Add(target);
            }

            if (target is System.Collections.IEnumerable enumerable && !(target is string))
            {
                int idx = 0;
                foreach (var item in enumerable)
                {
                    sb.AppendLine(Indent(depth + 1) + "[" + idx.ToString(CultureInfo.InvariantCulture) + "]");
                    AppendObjectDump(sb, item, depth + 2, maxDepth, visited, false, includeUnityBaseMembers);
                    idx++;
                }
                return;
            }

            var members = GetMembers(type, includeUnityBaseMembers);
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                object value;
                try
                {
                    value = member.GetValue(target);
                }
                catch (Exception ex)
                {
                    sb.AppendLine(Indent(depth + 1) + member.Name + " = <error: " + ex.Message + ">");
                    continue;
                }

                if (value == null || IsSimpleDumpType(member.ValueType))
                {
                    sb.AppendLine(Indent(depth + 1) + member.Name + " = " + DescribeObjectValue(value));
                }
                else
                {
                    sb.AppendLine(Indent(depth + 1) + member.Name + " =>");
                    AppendObjectDump(sb, value, depth + 2, maxDepth, visited, false, includeUnityBaseMembers);
                }
            }
        }

        private void AppendParametricReticle(StringBuilder sb, ReticleSO reticleSO)
        {
            if (reticleSO == null)
            {
                sb.AppendLine("reticleSO=null");
                return;
            }

            var visited = new HashSet<object>(new ReferenceEqualityComparer());
            sb.AppendLine("reticle=" + reticleSO.name);

            int planeCount = reticleSO.planes != null ? reticleSO.planes.Count : 0;
            sb.AppendLine("planeCount=" + planeCount.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < planeCount; i++)
            {
                AppendParametricNode(sb, reticleSO.planes[i], "plane[" + i.ToString(CultureInfo.InvariantCulture) + "]", 0, visited);
            }

            int lightCount = reticleSO.lights != null ? reticleSO.lights.Count : 0;
            sb.AppendLine("lightCount=" + lightCount.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < lightCount; i++)
            {
                AppendParametricNode(sb, reticleSO.lights[i], "light[" + i.ToString(CultureInfo.InvariantCulture) + "]", 0, visited);
            }
        }

        private void AppendParametricNode(StringBuilder sb, object node, string label, int depth, HashSet<object> visited)
        {
            string indent = Indent(depth);
            if (node == null)
            {
                sb.AppendLine(indent + "- " + label + " = null");
                return;
            }

            Type type = node.GetType();
            string kind = GetReticleSemanticKind(type);
            sb.AppendLine(indent + "- " + label + " | kind=" + kind + " | type=" + type.FullName);

            if (!type.IsValueType)
            {
                if (visited.Contains(node))
                {
                    sb.AppendLine(indent + "  <visited>");
                    return;
                }
                visited.Add(node);
            }

            var members = GetMembers(type, false);
            var entries = BuildParametricEntries(node, members);
            for (int i = 0; i < entries.Count; i++)
                sb.AppendLine(indent + "  " + entries[i]);

            IEnumerable childEnumerable;
            if (TryGetParametricChildren(node, members, out childEnumerable))
            {
                int childIndex = 0;
                foreach (var child in childEnumerable)
                {
                    AppendParametricNode(sb, child, "element[" + childIndex.ToString(CultureInfo.InvariantCulture) + "]", depth + 1, visited);
                    childIndex++;
                }
                if (childIndex == 0)
                    sb.AppendLine(indent + "  elements=0");
            }
        }

        private List<string> BuildParametricEntries(object node, List<MemberAdapter> members)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string[] orderedNames = new[]
            {
                "type", "distance", "position", "rotation", "align", "visualType", "illumination",
                "radius", "segments", "thickness", "length", "roundness", "targetSize",
                "text", "font", "fontSize", "alignment", "coef", "parent", "writingTransform"
            };

            for (int i = 0; i < orderedNames.Length; i++)
            {
                MemberAdapter member;
                if (!TryGetMember(node.GetType(), orderedNames[i], out member, false))
                    continue;
                if (!ShouldIncludeParametricMember(member))
                    continue;

                object value;
                try { value = member.GetValue(node); }
                catch (Exception ex) { value = "<error: " + ex.Message + ">"; }

                result.Add(member.Name + " = " + FormatParametricValue(value));
                seen.Add(member.Name);
            }

            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (seen.Contains(member.Name) || !ShouldIncludeParametricMember(member))
                    continue;

                object value;
                try { value = member.GetValue(node); }
                catch (Exception ex) { value = "<error: " + ex.Message + ">"; }

                result.Add(member.Name + " = " + FormatParametricValue(value));
            }

            return result;
        }

        private bool TryGetParametricChildren(object node, List<MemberAdapter> members, out IEnumerable children)
        {
            children = null;
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (!string.Equals(member.Name, "elements", StringComparison.Ordinal))
                    continue;

                object value = null;
                try { value = member.GetValue(node); }
                catch { value = null; }

                children = value as IEnumerable;
                return children != null;
            }

            return false;
        }

        private static bool ShouldIncludeParametricMember(MemberAdapter member)
        {
            if (member == null || string.IsNullOrEmpty(member.Name)) return false;
            if (string.Equals(member.Name, "elements", StringComparison.Ordinal)) return false;

            Type valueType = member.ValueType;
            if (valueType == null) return false;
            if (IsSimpleDumpType(valueType)) return true;

            string fullName = valueType.FullName ?? string.Empty;
            return fullName == "Reticle.AngularLength" ||
                   fullName == "Reticle.LinearLength" ||
                   fullName == "Reticle.AngularVector2" ||
                   fullName == "Reticle.ReticleTree+Position";
        }

        private static string GetReticleSemanticKind(Type type)
        {
            string name = type != null ? type.Name : string.Empty;
            switch (name)
            {
                case "FocalPlane": return "focal-plane";
                case "GroupBase": return "group";
                case "Angular": return "angular-scale";
                case "Circle": return "circle";
                case "Line": return "line";
                case "Stadia": return "stadia";
                case "Text": return "text";
                case "VerticalBallistic": return "vertical-ballistic";
                case "RotaryBallistic": return "rotary-ballistic";
                case "Light": return "light";
                default: return string.IsNullOrEmpty(name) ? "unknown" : name;
            }
        }

        private static string FormatParametricValue(object value)
        {
            if (value == null) return "null";

            Type type = value.GetType();
            string fullName = type.FullName ?? string.Empty;
            if (fullName == "Reticle.AngularLength")
            {
                object mrad = TryReadObjectMember(value, "mrad");
                object unit = TryReadObjectMember(value, "unit");
                return "AngularLength(mrad=" + FormatEditableValue(mrad) + ", unit=" + FormatEditableValue(unit) + ")";
            }
            if (fullName == "Reticle.LinearLength")
            {
                object meters = TryReadObjectMember(value, "meters");
                object unit = TryReadObjectMember(value, "unit");
                return "LinearLength(meters=" + FormatEditableValue(meters) + ", unit=" + FormatEditableValue(unit) + ")";
            }
            if (fullName == "Reticle.AngularVector2")
            {
                object mrads = TryReadObjectMember(value, "mrads");
                object unit = TryReadObjectMember(value, "unit");
                return "AngularVector2(mrads=" + FormatEditableValue(mrads) + ", unit=" + FormatEditableValue(unit) + ")";
            }
            if (fullName == "Reticle.ReticleTree+Position")
            {
                object x = TryReadObjectMember(value, "x");
                object y = TryReadObjectMember(value, "y");
                object angUnit = TryReadObjectMember(value, "angUnit");
                object linUnit = TryReadObjectMember(value, "linUnit");
                return "Position(x=" + FormatEditableValue(x) + ", y=" + FormatEditableValue(y) + ", angUnit=" + FormatEditableValue(angUnit) + ", linUnit=" + FormatEditableValue(linUnit) + ")";
            }

            return FormatEditableValue(value);
        }

        private static object TryReadObjectMember(object target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type type = target.GetType();
            for (Type walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                FieldInfo field = walk.GetField(memberName, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    try { return field.GetValue(target); }
                    catch { return null; }
                }

                PropertyInfo property = walk.GetProperty(memberName, flags | BindingFlags.DeclaredOnly);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    try { return property.GetValue(target, null); }
                    catch { return null; }
                }
            }

            return null;
        }

        private void AppendResolvedPreview(StringBuilder sb, ReticleMesh reticleMesh, ReticleSO reticleSO)
        {
            if (reticleSO == null)
            {
                sb.AppendLine("resolved=null");
                return;
            }

            var state = BuildPreviewRuntimeState(reticleMesh);
            sb.AppendLine("runtime.currentAmmo = " + DescribeObjectValue(state.CurrentAmmo));
            sb.AppendLine("runtime.currentRange = " + FormatParametricValue(new LinearLength(state.CurrentRangeMeters, LinearLength.LinearUnit.M)));
            sb.AppendLine("runtime.targetRange = " + FormatParametricValue(new LinearLength(state.TargetRangeMeters, LinearLength.LinearUnit.M)));
            sb.AppendLine("runtime.verticalRangeOffset = " + FormatParametricValue(new AngularLength(state.VerticalRangeOffsetMrads, AngularLength.AngularUnit.MIL_NATO)));
            sb.AppendLine("runtime.rotaryRangeRotation = " + FormatParametricValue(new AngularLength(state.RotaryRangeRotationMrads, AngularLength.AngularUnit.MIL_NATO)));

            int planeCount = reticleSO.planes != null ? reticleSO.planes.Count : 0;
            for (int i = 0; i < planeCount; i++)
            {
                var plane = reticleSO.planes[i];
                if (plane == null || plane.elements == null) continue;
                string planePath = "reticleSO.planes[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                Matrix4x4 planeMatrix = Matrix4x4.identity;
                for (int j = 0; j < plane.elements.Count; j++)
                {
                    AppendResolvedPreviewElement(sb, plane.elements[j], planePath + ".elements[" + j.ToString(CultureInfo.InvariantCulture) + "]", planeMatrix, null, state);
                }
            }
        }

        private PreviewRuntimeState BuildPreviewRuntimeState(ReticleMesh reticleMesh)
        {
            var state = new PreviewRuntimeState();
            if (reticleMesh == null)
                return state;

            state.CurrentAmmo = TryReadObjectMember(reticleMesh, "CurrentAmmo") as AmmoType;
            state.CurrentRangeMeters = ReadSingle(TryReadObjectMember(reticleMesh, "curReticleRange"));
            state.TargetRangeMeters = ReadSingle(TryReadObjectMember(reticleMesh, "targetReticleRange"));
            float rotaryCoef = ReadSingle(TryReadObjectMember(reticleMesh, "rotaryCoef"), 1f);
            state.RotaryRangeRotationMrads = rotaryCoef * 1000f * state.CurrentRangeMeters;
            if (state.CurrentAmmo != null)
            {
                float superelevationDeg = 0f - BallisticComputerRepository.Instance.GetSuperelevation(state.CurrentAmmo, Mathf.Abs(state.CurrentRangeMeters));
                if (state.CurrentRangeMeters < 0f) superelevationDeg = -superelevationDeg;
                state.VerticalRangeOffsetMrads = new AngularLength(superelevationDeg, AngularLength.AngularUnit.DEG).MRADS;
            }
            return state;
        }

        private void AppendResolvedPreviewElement(StringBuilder sb, ReticleTree.TransformElement element, string path, Matrix4x4 parentMatrix, ReticleTree.GroupBase parentGroup, PreviewRuntimeState state)
        {
            if (element == null) return;

            Vector2 rawPosition = element.position != null ? (Vector2)element.position : Vector2.zero;
            float rawRotationDeg = element.rotation.DEGS;

            Vector2 resolvedLocalPosition;
            float resolvedLocalRotationDeg;
            ResolveLocalPreviewTransform(parentGroup, rawPosition, rawRotationDeg, out resolvedLocalPosition, out resolvedLocalRotationDeg);

            Matrix4x4 localMatrix = Matrix4x4.TRS(new Vector3(resolvedLocalPosition.x, resolvedLocalPosition.y, 0f), Quaternion.Euler(0f, 0f, resolvedLocalRotationDeg), Vector3.one);
            Matrix4x4 worldMatrix = parentMatrix * localMatrix;
            Vector3 worldPos3 = worldMatrix.MultiplyPoint3x4(Vector3.zero);
            float worldRotationDeg = worldMatrix.rotation.eulerAngles.z;

            sb.AppendLine("resolved[\"" + path + "\"].kind = " + GetReticleSemanticKind(element.GetType()));
            sb.AppendLine("resolved[\"" + path + "\"].rawPosition = " + FormatParametricValue(new ReticleTree.Position(rawPosition.x, rawPosition.y)));
            sb.AppendLine("resolved[\"" + path + "\"].rawRotation = " + FormatParametricValue(new AngularLength(rawRotationDeg, AngularLength.AngularUnit.DEG)));
            sb.AppendLine("resolved[\"" + path + "\"].localPosition = " + FormatParametricValue(new ReticleTree.Position(resolvedLocalPosition.x, resolvedLocalPosition.y)));
            sb.AppendLine("resolved[\"" + path + "\"].localRotation = " + FormatParametricValue(new AngularLength(resolvedLocalRotationDeg, AngularLength.AngularUnit.DEG)));
            sb.AppendLine("resolved[\"" + path + "\"].worldPosition = " + FormatParametricValue(new ReticleTree.Position(worldPos3.x, worldPos3.y)));
            sb.AppendLine("resolved[\"" + path + "\"].worldRotation = " + FormatParametricValue(new AngularLength(worldRotationDeg, AngularLength.AngularUnit.DEG)));

            var group = element as ReticleTree.GroupBase;
            if (group == null || group.elements == null || group.elements.Count == 0)
                return;

            Matrix4x4 childMatrix = worldMatrix;
            if (group.align == ReticleTree.GroupBase.Alignment.VerticalRange)
            {
                childMatrix *= Matrix4x4.TRS(new Vector3(0f, state.VerticalRangeOffsetMrads, 0f), Quaternion.identity, Vector3.one);
            }
            else if (group.align == ReticleTree.GroupBase.Alignment.RotaryRange)
            {
                float rotationDeg = state.RotaryRangeRotationMrads * 0.05729578f;
                childMatrix *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, rotationDeg), Vector3.one);
            }

            for (int i = 0; i < group.elements.Count; i++)
            {
                AppendResolvedPreviewElement(sb, group.elements[i], path + ".elements[" + i.ToString(CultureInfo.InvariantCulture) + "]", childMatrix, group, state);
            }
        }

        private void ResolveLocalPreviewTransform(ReticleTree.GroupBase parentGroup, Vector2 rawPosition, float rawRotationDeg, out Vector2 resolvedPosition, out float resolvedRotationDeg)
        {
            resolvedPosition = rawPosition;
            resolvedRotationDeg = rawRotationDeg;
            if (parentGroup == null)
                return;

            if (parentGroup is ReticleTree.VerticalBallistic)
            {
                var vertical = (ReticleTree.VerticalBallistic)parentGroup;
                if (vertical.projectile != null && vertical.projectile.AmmoType != null)
                {
                    float distanceMeters = rawPosition.y;
                    float superelevationDeg = 0f - BallisticComputerRepository.Instance.GetSuperelevation(vertical.projectile.AmmoType, Mathf.Abs(distanceMeters));
                    if (distanceMeters < 0f) superelevationDeg = -superelevationDeg;
                    resolvedPosition.y = new AngularLength(superelevationDeg, AngularLength.AngularUnit.DEG).MRADS;
                }
                return;
            }

            if (parentGroup is ReticleTree.Stadia)
            {
                var stadia = (ReticleTree.Stadia)parentGroup;
                resolvedPosition.y = MathUtil.SizeAtDistanceToAngle(stadia.targetSize, rawPosition.y).MRADS;
                return;
            }

            if (parentGroup is ReticleTree.RotaryBallistic)
            {
                var rotary = (ReticleTree.RotaryBallistic)parentGroup;
                float radius = rawPosition.x;
                float angle = rawPosition.y * rotary.coef;
                if (radius < 0.01f)
                    radius = 0.01f;
                resolvedPosition.x = radius * Mathf.Sin(angle);
                resolvedPosition.y = radius * Mathf.Cos(angle);
                resolvedRotationDeg = rawRotationDeg - angle * 57.29578f;
            }
        }

        private static float ReadSingle(object value, float fallback = 0f)
        {
            if (value == null) return fallback;
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private void AppendEditablePaths(StringBuilder sb, object target, string path, int depth, int maxDepth, HashSet<object> visited, bool expandTargetEvenIfUnityObject, bool includeUnityBaseMembers)
        {
            if (target == null)
            {
                sb.AppendLine(path + " = null");
                return;
            }

            Type type = target.GetType();
            sb.AppendLine(path + ".$type = " + type.FullName);

            bool isExpandedUnityRoot = expandTargetEvenIfUnityObject && typeof(UnityEngine.Object).IsAssignableFrom(type);
            if (depth >= maxDepth || (IsSimpleDumpType(type) && !isExpandedUnityRoot))
            {
                sb.AppendLine(path + ".$value = " + FormatEditableValue(target));
                return;
            }

            if (!type.IsValueType)
            {
                if (visited.Contains(target))
                {
                    sb.AppendLine(path + " = <visited>");
                    return;
                }
                visited.Add(target);
            }

            if (target is System.Collections.IEnumerable enumerable && !(target is string))
            {
                int idx = 0;
                foreach (var item in enumerable)
                {
                    AppendEditablePaths(sb, item, path + "[" + idx.ToString(CultureInfo.InvariantCulture) + "]", depth + 1, maxDepth, visited, false, includeUnityBaseMembers);
                    idx++;
                }
                return;
            }

            var members = GetMembers(type, includeUnityBaseMembers);
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                object value;
                try
                {
                    value = member.GetValue(target);
                }
                catch (Exception ex)
                {
                    sb.AppendLine(path + "." + member.Name + " = <error: " + ex.Message + ">");
                    continue;
                }

                string memberPath = path + "." + member.Name;
                if (value == null || IsSimpleDumpType(member.ValueType))
                {
                    sb.AppendLine(memberPath + " = " + FormatEditableValue(value));
                }
                else
                {
                    AppendEditablePaths(sb, value, memberPath, depth + 1, maxDepth, visited, false, includeUnityBaseMembers);
                }
            }
        }

        private static string FormatEditableValue(object value)
        {
            if (value == null) return "null";
            if (value is string) return "\"" + value.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            if (value is bool) return ((bool)value) ? "true" : "false";
            if (value is float) return ((float)value).ToString("R", CultureInfo.InvariantCulture) + "f";
            if (value is double) return ((double)value).ToString("R", CultureInfo.InvariantCulture) + "d";
            if (value is int || value is long || value is short || value is byte) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is Enum) return value.GetType().FullName + "." + value;
            if (value is Vector2)
            {
                var vector2 = (Vector2)value;
                return "new Vector2(" + vector2.x.ToString("R", CultureInfo.InvariantCulture) + "f, " + vector2.y.ToString("R", CultureInfo.InvariantCulture) + "f)";
            }
            if (value is Vector3)
            {
                var vector3 = (Vector3)value;
                return "new Vector3(" + vector3.x.ToString("R", CultureInfo.InvariantCulture) + "f, " + vector3.y.ToString("R", CultureInfo.InvariantCulture) + "f, " + vector3.z.ToString("R", CultureInfo.InvariantCulture) + "f)";
            }
            if (value is Color)
            {
                var color = (Color)value;
                return "new Color(" + color.r.ToString("R", CultureInfo.InvariantCulture) + "f, " + color.g.ToString("R", CultureInfo.InvariantCulture) + "f, " + color.b.ToString("R", CultureInfo.InvariantCulture) + "f, " + color.a.ToString("R", CultureInfo.InvariantCulture) + "f)";
            }
            var unityObject = value as UnityEngine.Object;
            if (unityObject != null) return "<UnityObject:" + unityObject.name + ":" + unityObject.GetType().Name + ">";
            return value.ToString();
        }

        private static string BuildStableTransformPath(Transform root, Transform node)
        {
            if (root == null || node == null) return string.Empty;
            var segments = new List<string>();
            Transform current = node;
            while (current != null)
            {
                segments.Add(BuildStableTransformSegment(current));
                if (current == root) break;
                current = current.parent;
            }
            segments.Reverse();
            return string.Join("/", segments.ToArray());
        }

        private static string BuildStableTransformSegment(Transform node)
        {
            if (node == null) return string.Empty;
            int duplicateIndex = 0;
            Transform parent = node.parent;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform sibling = parent.GetChild(i);
                    if (!string.Equals(sibling.name, node.name, StringComparison.Ordinal)) continue;
                    if (sibling == node) break;
                    duplicateIndex++;
                }
            }
            return node.name + "[" + duplicateIndex.ToString(CultureInfo.InvariantCulture) + "]";
        }

        private static Transform ResolveStableTransformPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;

            string[] rawSegments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (rawSegments.Length == 0) return null;

            int startIndex = 0;
            string rootSegment = BuildStableTransformSegment(root);
            string rootNameNormalized = RemoveImportedSuffix(root.name);
            string rootSegmentNormalized = RemoveImportedSuffix(rootSegment);
            string firstSegmentNormalized = RemoveImportedSuffix(rawSegments[0]);
            string firstSegmentName;
            int firstSegmentIndex;
            TryParseStableTransformSegment(firstSegmentNormalized, out firstSegmentName, out firstSegmentIndex);

            string rootSegmentName;
            int rootSegmentIndex;
            TryParseStableTransformSegment(rootSegmentNormalized, out rootSegmentName, out rootSegmentIndex);

            if (string.Equals(firstSegmentNormalized, rootSegmentNormalized, StringComparison.Ordinal) ||
                string.Equals(firstSegmentNormalized, rootNameNormalized, StringComparison.Ordinal) ||
                string.Equals(firstSegmentName, rootNameNormalized, StringComparison.Ordinal) ||
                string.Equals(firstSegmentName, rootSegmentName, StringComparison.Ordinal))
                startIndex = 1;

            if (rawSegments.Length == 1 && startIndex == 1)
                return root;

            Transform current = root;
            for (int i = startIndex; i < rawSegments.Length; i++)
            {
                string childName;
                int duplicateIndex;
                TryParseStableTransformSegment(rawSegments[i], out childName, out duplicateIndex);
                current = ResolveStableTransformChild(current, childName, duplicateIndex);
                if (current == null)
                    return null;
            }

            return current;
        }

        private static Transform ResolveStableTransformChild(Transform parent, string childName, int duplicateIndex)
        {
            if (parent == null) return null;
            int matchIndex = 0;
            Transform firstMatch = null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (!string.Equals(child.name, childName, StringComparison.Ordinal)) continue;
                if (firstMatch == null) firstMatch = child;
                if (matchIndex == duplicateIndex) return child;
                matchIndex++;
            }
            return firstMatch;
        }

        private static bool TryParseStableTransformSegment(string segment, out string name, out int duplicateIndex)
        {
            name = segment ?? string.Empty;
            duplicateIndex = 0;
            if (string.IsNullOrEmpty(segment)) return false;

            int open = segment.LastIndexOf('[');
            int close = segment.LastIndexOf(']');
            if (open > 0 && close == segment.Length - 1)
            {
                int parsedIndex;
                if (int.TryParse(segment.Substring(open + 1, close - open - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedIndex))
                {
                    name = segment.Substring(0, open);
                    duplicateIndex = parsedIndex;
                    return true;
                }
            }

            return false;
        }

        private static List<PathSegment> ParsePathSegments(string path)
        {
            var result = new List<PathSegment>();
            if (string.IsNullOrEmpty(path)) return result;

            string[] rawSegments = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < rawSegments.Length; i++)
            {
                string raw = rawSegments[i].Trim();
                if (raw.Length == 0) continue;

                var segment = new PathSegment();
                int firstBracket = raw.IndexOf('[');
                segment.MemberName = firstBracket >= 0 ? raw.Substring(0, firstBracket) : raw;
                if (segment.MemberName != null)
                    segment.MemberName = segment.MemberName.Trim();

                int cursor = firstBracket;
                while (cursor >= 0 && cursor < raw.Length)
                {
                    int end = raw.IndexOf(']', cursor + 1);
                    if (end < 0)
                        throw new FormatException("Unclosed indexer: " + raw);

                    int parsedIndex;
                    if (!int.TryParse(raw.Substring(cursor + 1, end - cursor - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedIndex))
                        throw new FormatException("Invalid indexer: " + raw);

                    segment.Indices.Add(parsedIndex);
                    cursor = raw.IndexOf('[', end + 1);
                }

                result.Add(segment);
            }

            return result;
        }

        private object ResolvePathSegmentValue(object current, PathSegment segment, bool includeUnityBaseMembers)
        {
            if (current == null)
                throw new NullReferenceException("Path traversal hit null");

            object value = current;
            if (!string.IsNullOrEmpty(segment.MemberName))
            {
                MemberAdapter member;
                if (!TryGetMember(current.GetType(), segment.MemberName, out member, includeUnityBaseMembers))
                    throw new MissingMemberException(current.GetType().FullName, segment.MemberName);
                value = member.GetValue(current);
            }

            for (int i = 0; i < segment.Indices.Count; i++)
                value = GetIndexedValue(value, segment.Indices[i]);

            return value;
        }

        private bool SetPathSegmentValue(object current, PathSegment segment, string valueText)
        {
            if (current == null)
                throw new NullReferenceException("Target is null");

            MemberAdapter member = null;
            object container = current;
            if (!string.IsNullOrEmpty(segment.MemberName))
            {
                if (!TryGetMember(current.GetType(), segment.MemberName, out member, false))
                    throw new MissingMemberException(current.GetType().FullName, segment.MemberName);

                if (segment.Indices.Count == 0)
                {
                    if (!member.CanWrite)
                        return false;
                    object parsedValue = ParseEditableValue(valueText, member.ValueType);
                    member.SetValue(current, parsedValue);
                    return true;
                }

                container = member.GetValue(current);
            }

            if (segment.Indices.Count == 0)
                return false;

            for (int i = 0; i < segment.Indices.Count - 1; i++)
                container = GetIndexedValue(container, segment.Indices[i]);

            int lastIndex = segment.Indices[segment.Indices.Count - 1];
            Type elementType = GetIndexedValueType(container);
            object parsedElement = ParseEditableValue(valueText, elementType);
            return SetIndexedValue(container, lastIndex, parsedElement);
        }

        private static object GetIndexedValue(object target, int index)
        {
            if (target == null)
                throw new NullReferenceException("Indexed target is null");

            var list = target as IList;
            if (list != null)
            {
                if (index < 0 || index >= list.Count)
                    throw new IndexOutOfRangeException("Index out of range: " + index.ToString(CultureInfo.InvariantCulture));
                return list[index];
            }

            var enumerable = target as IEnumerable;
            if (enumerable != null)
            {
                int currentIndex = 0;
                foreach (object item in enumerable)
                {
                    if (currentIndex == index) return item;
                    currentIndex++;
                }
                throw new IndexOutOfRangeException("Index out of range: " + index.ToString(CultureInfo.InvariantCulture));
            }

            throw new InvalidOperationException("Target is not indexable: " + target.GetType().FullName);
        }

        private static bool SetIndexedValue(object target, int index, object value)
        {
            if (target == null)
                throw new NullReferenceException("Indexed target is null");

            var list = target as IList;
            if (list != null)
            {
                if (list.IsReadOnly)
                    return false;
                if (index < 0 || index >= list.Count)
                    throw new IndexOutOfRangeException("Index out of range: " + index.ToString(CultureInfo.InvariantCulture));
                list[index] = value;
                return true;
            }

            throw new InvalidOperationException("Target index is not writable: " + target.GetType().FullName);
        }

        private static Type GetIndexedValueType(object target)
        {
            if (target == null) return typeof(object);
            Type type = target.GetType();
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType) return type.GetGenericArguments()[0];
            return typeof(object);
        }

        private static object ParseEditableValue(string text, Type targetType)
        {
            if (targetType == null) targetType = typeof(object);
            string trimmed = text != null ? text.Trim() : string.Empty;

            Type nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                if (string.Equals(trimmed, "null", StringComparison.Ordinal)) return null;
                targetType = nullableType;
            }

            if (string.Equals(trimmed, "null", StringComparison.Ordinal))
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    return null;
                throw new FormatException("Cannot assign null to " + targetType.FullName);
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
                throw new InvalidOperationException("UnityObject references are not supported by reticle import");

            if (targetType == typeof(string))
                return UnescapeEditableString(trimmed);
            if (targetType == typeof(bool))
                return bool.Parse(trimmed);
            if (targetType == typeof(int))
                return int.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (targetType == typeof(long))
                return long.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (targetType == typeof(short))
                return short.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (targetType == typeof(byte))
                return byte.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (targetType == typeof(float))
                return float.Parse(trimmed.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (targetType == typeof(double))
                return double.Parse(trimmed.TrimEnd('d', 'D'), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (targetType == typeof(decimal))
                return decimal.Parse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture);

            if (targetType.IsEnum)
            {
                string enumName = trimmed;
                int lastDot = trimmed.LastIndexOf('.');
                if (lastDot >= 0) enumName = trimmed.Substring(lastDot + 1);
                return Enum.Parse(targetType, enumName, false);
            }

            if (targetType == typeof(Vector2))
            {
                var args = ParseConstructorArguments(trimmed, "Vector2", 2);
                return new Vector2(ParseFloatLiteral(args[0]), ParseFloatLiteral(args[1]));
            }

            if (targetType == typeof(Vector3))
            {
                var args = ParseConstructorArguments(trimmed, "Vector3", 3);
                return new Vector3(ParseFloatLiteral(args[0]), ParseFloatLiteral(args[1]), ParseFloatLiteral(args[2]));
            }

            if (targetType == typeof(Color))
            {
                var args = ParseConstructorArguments(trimmed, "Color", 4);
                return new Color(ParseFloatLiteral(args[0]), ParseFloatLiteral(args[1]), ParseFloatLiteral(args[2]), ParseFloatLiteral(args[3]));
            }

            return Convert.ChangeType(trimmed, targetType, CultureInfo.InvariantCulture);
        }

        private static string[] ParseConstructorArguments(string text, string typeName, int expectedCount)
        {
            string prefix = "new " + typeName + "(";
            if (!text.StartsWith(prefix, StringComparison.Ordinal) || !text.EndsWith(")", StringComparison.Ordinal))
                throw new FormatException("Expected " + prefix + "...)");

            string inner = text.Substring(prefix.Length, text.Length - prefix.Length - 1);
            string[] parts = inner.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != expectedCount)
                throw new FormatException("Expected " + expectedCount.ToString(CultureInfo.InvariantCulture) + " values for " + typeName);
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();
            return parts;
        }

        private static float ParseFloatLiteral(string text)
        {
            return float.Parse(text.Trim().TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string UnescapeEditableString(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                string inner = text.Substring(1, text.Length - 2);
                return inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return text;
        }

        private static string Indent(int depth)
        {
            return new string(' ', depth * 2);
        }

        private static bool IsSimpleDumpType(Type type)
        {
            return type == null || type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
                   type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Color) || typeof(UnityEngine.Object).IsAssignableFrom(type);
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "reticle";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(fileName.Length);
            for (int i = 0; i < fileName.Length; i++)
                sb.Append(invalid.Contains(fileName[i]) ? '_' : fileName[i]);
            return sb.ToString();
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        private void SyncSpecialAmmoBindings(AmmoType ammo)
        {
            if (ammo == null) return;
            // Leopard1Ammo 调试功能已移除（新架构不支持动态调试）
            if (ReferenceEquals(ammo, BMP1MCLOSAmmo.ammo_9m14_mclos) || BMP1MCLOSAmmo.IsOriginalMissileName(ammo.Name))
            {
                BMP1MCLOSAmmo.SetDebugParams(ammo.SpiralPower, ammo.SpiralAngularRate, ammo.MaximumRange,
                    ammo.NoisePowerX, ammo.NoisePowerY, ammo.NoiseTimeScale, ammo.TurnSpeed,
                    ammo.TntEquivalentKg, ammo.RhaPenetration, ammo.MaxSpallRha, ammo.MinSpallRha,
                    ammo.SpallMultiplier, ammo.RangedFuseTime, ammo.RhaToFuse, ammo.MuzzleVelocity, ammo.Mass);
            }
        }

        private static string DescribeAmmo(AmmoType ammo)
        {
            if (ammo == null) return "null";
            return $"{ammo.Name} | Guidance={ammo.Guidance} | Vel={ammo.MuzzleVelocity:F1} | Pen={ammo.RhaPenetration:F1}";
        }

        private static string DescribeWeaponInfo(WeaponSystemInfo info)
        {
            if (info == null) return "null";
            return $"{info.Name} | Weapon={(info.Weapon != null ? info.Weapon.name : "null")} | FCS={(info.FCS != null ? info.FCS.name : (info.Weapon != null && info.Weapon.FCS != null ? info.Weapon.FCS.name : "null"))}";
        }

        private static string DescribeUnityObject(UnityEngine.Object obj)
        {
            return obj != null ? (obj.name + " (" + obj.GetType().Name + ")") : "null";
        }

        private static string DescribeObjectValue(object value)
        {
            if (value == null) return "null";
            var unityObject = value as UnityEngine.Object;
            if (unityObject != null) return unityObject.name + " (" + unityObject.GetType().Name + ")";
            return value + " (" + value.GetType().Name + ")";
        }
    }
}







#endif
