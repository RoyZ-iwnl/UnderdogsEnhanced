using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using Reticle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderdogsEnhanced
{
    public class EMES18DebugUI : MonoBehaviour
    {
        public static EMES18DebugUI Instance { get; private set; }

        private sealed class DebugTarget
        {
            public Transform Transform;
            public string Path;
            public string Kind;
            public string RootLabel;
        }

        private bool show_ui = false;
        private Rect window_rect = new Rect(20f, 20f, 900f, 1200f);
        private Vector2 target_scroll;

        private readonly List<DebugTarget> targets = new List<DebugTarget>();
        private Transform selected_element;
        private string selected_path = string.Empty;
        private string selected_kind = string.Empty;

        private Vector3 position_offset = Vector3.zero;
        private Vector3 scale_offset = Vector3.one;
        private string pos_x_input = "0";
        private string pos_y_input = "0";
        private string pos_z_input = "0";
        private string scale_x_input = "1";
        private string scale_y_input = "1";
        private string scale_z_input = "1";
        private string canvas_sort_input = "0";
        private string canvas_plane_input = "1";

        private Transform emes18_root;

        void Start()
        {
            Instance = this;
            RefreshTargets();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
        }

        public void ToggleWindow()
        {
            show_ui = !show_ui;
            if (show_ui) RefreshTargets();
        }

        public void RefreshWindowTargets()
        {
            RefreshTargets();
        }

        void OnGUI()
        {
            if (!show_ui) return;
            window_rect = GUI.Window(12348, window_rect, DrawWindow, "EMES18 Display Debug UI");
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.Label("F8: toggle UI | F9: refresh target list");
            GUILayout.Label($"EMES root: {(emes18_root != null ? emes18_root.name : "null")}");
            GUILayout.Label($"Active cam parent: {(CameraManager.MainCam != null && CameraManager.MainCam.transform.parent != null ? CameraManager.MainCam.transform.parent.name : "null")}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Targets", GUILayout.Width(130))) RefreshTargets();
            if (GUILayout.Button("Print All", GUILayout.Width(100))) PrintAllTargets();
            if (GUILayout.Button("Print Selected", GUILayout.Width(110))) PrintSelected();
            if (GUILayout.Button("Toggle Active", GUILayout.Width(110))) ToggleSelectedActive();
            if (GUILayout.Button("Toggle Visual", GUILayout.Width(110))) ToggleSelectedVisual();
            if (GUILayout.Button("Pull Current", GUILayout.Width(110))) PullSelectedTransform();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"DefaultScopeCtl: {(EMES18Optic.DebugDisableDefaultScopeControl ? "OFF" : "ON")}", GUILayout.Width(170));
            if (GUILayout.Button(EMES18Optic.DebugDisableDefaultScopeControl ? "Enable DefaultScopeCtl" : "Disable DefaultScopeCtl", GUILayout.Width(180)))
            {
                EMES18Optic.DebugDisableDefaultScopeControl = !EMES18Optic.DebugDisableDefaultScopeControl;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] DefaultScopeCtl={(EMES18Optic.DebugDisableDefaultScopeControl ? "OFF" : "ON")}");
            }
            if (GUILayout.Button("Restore DefaultScope", GUILayout.Width(150)))
            {
                EMES18Optic.DebugDisableDefaultScopeControl = true;
                EMES18Optic.TickGlobalDefaultScopeState();
                MelonLoader.MelonLogger.Msg("[EMES18DBG] DefaultScope forced restore");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"NormalizeScope: {(EMES18DebugState.DisableNormalizeScopeSprite ? "OFF" : "ON")}", GUILayout.Width(170));
            if (GUILayout.Button(EMES18DebugState.DisableNormalizeScopeSprite ? "Enable NormalizeScope" : "Disable NormalizeScope", GUILayout.Width(180)))
            {
                EMES18DebugState.DisableNormalizeScopeSprite = !EMES18DebugState.DisableNormalizeScopeSprite;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] NormalizeScope={(EMES18DebugState.DisableNormalizeScopeSprite ? "OFF" : "ON")}");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"ThermalHideScope: {(EMES18DebugState.DisableThermalDefaultScopeHide ? "OFF" : "ON")}", GUILayout.Width(170));
            if (GUILayout.Button(EMES18DebugState.DisableThermalDefaultScopeHide ? "Enable ThermalHide" : "Disable ThermalHide", GUILayout.Width(180)))
            {
                EMES18DebugState.DisableThermalDefaultScopeHide = !EMES18DebugState.DisableThermalDefaultScopeHide;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] ThermalHideScope={(EMES18DebugState.DisableThermalDefaultScopeHide ? "OFF" : "ON")}");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"DayShowScope: {(EMES18DebugState.DisableDayDefaultScopeShow ? "OFF" : "ON")}", GUILayout.Width(170));
            if (GUILayout.Button(EMES18DebugState.DisableDayDefaultScopeShow ? "Enable DayShow" : "Disable DayShow", GUILayout.Width(180)))
            {
                EMES18DebugState.DisableDayDefaultScopeShow = !EMES18DebugState.DisableDayDefaultScopeShow;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] DayShowScope={(EMES18DebugState.DisableDayDefaultScopeShow ? "OFF" : "ON")}");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Toggle SpriteR", GUILayout.Width(120))) ToggleSelectedSpriteRenderer();
            if (GUILayout.Button("Toggle PostMesh", GUILayout.Width(120))) ToggleSelectedPostMesh();
            if (GUILayout.Button("Toggle Canvas", GUILayout.Width(110))) ToggleSelectedCanvas();
            if (GUILayout.Button("Reset Pos", GUILayout.Width(90))) ResetSelectedPosition();
            if (GUILayout.Button("Reset Scale", GUILayout.Width(90))) ResetSelectedScale();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"Targets: {targets.Count}");
            target_scroll = GUILayout.BeginScrollView(target_scroll, GUILayout.Height(320));
            foreach (var target in targets)
            {
                if (target == null || target.Transform == null) continue;

                GUILayout.BeginHorizontal();
                bool isSelected = selected_element == target.Transform;
                if (GUILayout.Button(isSelected ? ">" : "Sel", GUILayout.Width(40)))
                    SelectTarget(target);

                if (GUILayout.Button(target.Transform.gameObject.activeSelf ? "Hide" : "Show", GUILayout.Width(55)))
                {
                    bool newState = !target.Transform.gameObject.activeSelf;
                    target.Transform.gameObject.SetActive(newState);
                    MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleActive path={target.Path} active={newState}");
                    if (target.Transform == selected_element) PullSelectedTransform();
                }

                GUILayout.Label($"[{target.RootLabel}|{target.Kind}] {target.Path}");
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            if (selected_element != null)
            {
                GUILayout.Label($"Selected: {selected_kind} | {selected_path}");
                GUILayout.TextArea(GetSelectedStateSummary(), GUILayout.Height(110));

                GUILayout.Space(4f);
                GUILayout.Label("Position");
                position_offset.x = DrawSliderRow("X", position_offset.x, -2000f, 2000f);
                position_offset.y = DrawSliderRow("Y", position_offset.y, -2000f, 2000f);
                position_offset.z = DrawSliderRow("Z", position_offset.z, -200f, 200f);
                selected_element.localPosition = position_offset;

                GUILayout.Space(4f);
                GUILayout.Label("Scale");
                scale_offset.x = DrawSliderRow("SX", scale_offset.x, 0.01f, 100f);
                scale_offset.y = DrawSliderRow("SY", scale_offset.y, 0.01f, 100f);
                scale_offset.z = DrawSliderRow("SZ", scale_offset.z, 0.01f, 100f);
                selected_element.localScale = scale_offset;

                GUILayout.Space(6f);
                DrawCanvasControls();

                GUILayout.Space(6f);
                GUILayout.Label("Numeric Input");
                GUILayout.BeginHorizontal();
                GUILayout.Label("PX", GUILayout.Width(22));
                pos_x_input = GUILayout.TextField(pos_x_input, GUILayout.Width(70));
                GUILayout.Label("PY", GUILayout.Width(22));
                pos_y_input = GUILayout.TextField(pos_y_input, GUILayout.Width(70));
                GUILayout.Label("PZ", GUILayout.Width(22));
                pos_z_input = GUILayout.TextField(pos_z_input, GUILayout.Width(70));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("SX", GUILayout.Width(22));
                scale_x_input = GUILayout.TextField(scale_x_input, GUILayout.Width(70));
                GUILayout.Label("SY", GUILayout.Width(22));
                scale_y_input = GUILayout.TextField(scale_y_input, GUILayout.Width(70));
                GUILayout.Label("SZ", GUILayout.Width(22));
                scale_z_input = GUILayout.TextField(scale_z_input, GUILayout.Width(70));
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Apply Numeric Values"))
                    ApplyNumericInputs();
            }
            else
            {
                GUILayout.Label("No target selected.");
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private float DrawSliderRow(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(26));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString("F2"), GUILayout.Width(70));
            GUILayout.EndHorizontal();
            return value;
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

        private void RefreshTargets()
        {
            targets.Clear();

            var activeOptic = ResolveActiveOptic();
            if (activeOptic != null)
            {
                var monitors = activeOptic.GetComponentsInChildren<EMES18Optic.EMES18Monitor>(true);
                for (int i = 0; i < monitors.Length; i++)
                {
                    var monitor = monitors[i];
                    if (monitor == null) continue;
                    if (i == 0) emes18_root = monitor.transform;
                    CollectTargets(monitor.transform, $"EMES{i}");
                }

                CollectTargets(activeOptic.transform, "Optic");
            }
            else
            {
                emes18_root = null;
            }

            var scopeRoot = CameraManager.MainCam != null ? CameraManager.MainCam.transform.Find("Scope") : null;
            if (scopeRoot != null)
                CollectTargets(scopeRoot, "Scope");

            if (activeOptic == null)
            {
                int fallbackIndex = 0;
                foreach (var monitor in Resources.FindObjectsOfTypeAll<EMES18Optic.EMES18Monitor>())
                {
                    if (monitor == null) continue;
                    if (fallbackIndex == 0) emes18_root = monitor.transform;
                    CollectTargets(monitor.transform, $"EMES_FALLBACK{fallbackIndex}");
                    fallbackIndex++;
                }
            }

            targets.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

            if (selected_element != null)
            {
                var match = targets.FirstOrDefault(t => t.Transform == selected_element);
                if (match != null)
                {
                    selected_path = match.Path;
                    selected_kind = match.Kind;
                }
            }
        }

        private void CollectTargets(Transform root, string label)
        {
            if (root == null) return;

            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                if (targets.Any(t => t.Transform == node)) continue;

                targets.Add(new DebugTarget
                {
                    Transform = node,
                    Path = BuildPath(root, node, label),
                    Kind = DescribeKind(node),
                    RootLabel = label
                });
            }
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

        private string BuildPath(Transform root, Transform node, string label)
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

        private void SelectTarget(DebugTarget target)
        {
            if (target == null || target.Transform == null) return;
            selected_element = target.Transform;
            selected_path = target.Path;
            selected_kind = target.Kind;
            PullSelectedTransform();
            PrintSelected();
        }

        private void PullSelectedTransform()
        {
            if (selected_element == null) return;
            position_offset = selected_element.localPosition;
            scale_offset = selected_element.localScale;
            SyncInputsFromCurrentValues();
        }

        private void ResetSelectedPosition()
        {
            if (selected_element == null) return;
            position_offset = Vector3.zero;
            selected_element.localPosition = position_offset;
            SyncInputsFromCurrentValues();
            PrintSelected();
        }

        private void ResetSelectedScale()
        {
            if (selected_element == null) return;
            scale_offset = Vector3.one;
            selected_element.localScale = scale_offset;
            SyncInputsFromCurrentValues();
            PrintSelected();
        }

        private void ToggleSelectedActive()
        {
            if (selected_element == null) return;
            bool newState = !selected_element.gameObject.activeSelf;
            selected_element.gameObject.SetActive(newState);
            MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleActive path={selected_path} active={newState}");
            PullSelectedTransform();
        }

        private void ToggleSelectedVisual()
        {
            if (selected_element == null) return;

            var graphic = selected_element.GetComponent<Graphic>();
            if (graphic != null)
            {
                graphic.enabled = !graphic.enabled;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleGraphic path={selected_path} enabled={graphic.enabled}");
                return;
            }

            var spriteRenderer = selected_element.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = !spriteRenderer.enabled;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleSpriteRenderer path={selected_path} enabled={spriteRenderer.enabled}");
                return;
            }

            var canvas = selected_element.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = !canvas.enabled;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleCanvas path={selected_path} enabled={canvas.enabled}");
                return;
            }

            var renderer = selected_element.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = !renderer.enabled;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleRenderer path={selected_path} enabled={renderer.enabled}");
                return;
            }

            var behaviour = selected_element.GetComponent<Behaviour>();
            if (behaviour != null)
            {
                behaviour.enabled = !behaviour.enabled;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleBehaviour path={selected_path} type={behaviour.GetType().Name} enabled={behaviour.enabled}");
            }
        }

        private void ToggleSelectedSpriteRenderer()
        {
            if (selected_element == null) return;
            var sr = selected_element.GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleSpriteRenderer path={selected_path} missing");
                return;
            }

            sr.enabled = !sr.enabled;
            MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleSpriteRenderer path={selected_path} enabled={sr.enabled}");
        }

        private void ToggleSelectedPostMesh()
        {
            if (selected_element == null) return;
            var pm = selected_element.GetComponent<PostMeshComp>();
            if (pm == null)
            {
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] TogglePostMesh path={selected_path} missing");
                return;
            }

            pm.enabled = !pm.enabled;
            MelonLoader.MelonLogger.Msg($"[EMES18DBG] TogglePostMesh path={selected_path} enabled={pm.enabled}");
        }

        private Canvas GetSelectedCanvas(bool includeParent = true)
        {
            if (selected_element == null) return null;
            var canvas = selected_element.GetComponent<Canvas>();
            if (canvas != null) return canvas;
            return includeParent ? selected_element.GetComponentInParent<Canvas>(true) : null;
        }

        private void SyncCanvasInputs(Canvas canvas)
        {
            if (canvas == null) return;
            canvas_sort_input = canvas.sortingOrder.ToString(CultureInfo.InvariantCulture);
            canvas_plane_input = canvas.planeDistance.ToString("F2", CultureInfo.InvariantCulture);
        }

        private void ApplyCanvasPreset(Canvas canvas, RenderMode mode, bool overrideSorting, int sortOrder, float planeDistance)
        {
            if (canvas == null) return;

            canvas.renderMode = mode;
            canvas.overrideSorting = overrideSorting;
            canvas.sortingOrder = sortOrder;
            canvas.planeDistance = planeDistance;
            if (mode == RenderMode.ScreenSpaceCamera)
                canvas.worldCamera = CameraManager.MainCam;
            SyncCanvasInputs(canvas);
            MelonLoader.MelonLogger.Msg($"[EMES18DBG] CanvasPreset path={selected_path} mode={canvas.renderMode} override={canvas.overrideSorting} sort={canvas.sortingOrder} plane={canvas.planeDistance:F2} cam={(canvas.worldCamera != null ? canvas.worldCamera.name : "null")}");
        }

        private void DrawCanvasControls()
        {
            var canvas = GetSelectedCanvas(true);
            GUILayout.Label("Canvas");
            if (canvas == null)
            {
                GUILayout.Label("No Canvas on selected object or parents.");
                return;
            }

            GUILayout.Label($"CanvasTarget: {canvas.gameObject.name} | mode={canvas.renderMode} override={canvas.overrideSorting} sort={canvas.sortingOrder} plane={canvas.planeDistance:F2}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(canvas.enabled ? "Canvas Off" : "Canvas On", GUILayout.Width(90)))
            {
                canvas.enabled = !canvas.enabled;
                SyncCanvasInputs(canvas);
            }
            if (GUILayout.Button(canvas.overrideSorting ? "Override Off" : "Override On", GUILayout.Width(95)))
            {
                canvas.overrideSorting = !canvas.overrideSorting;
                SyncCanvasInputs(canvas);
            }
            if (GUILayout.Button("Use Parent Canvas", GUILayout.Width(120)))
            {
                var parentCanvas = selected_element != null ? selected_element.GetComponentInParent<Canvas>(true) : null;
                if (parentCanvas != null)
                {
                    selected_element = parentCanvas.transform;
                    selected_kind = "Canvas";
                    selected_path = parentCanvas.transform.name;
                    PullSelectedTransform();
                    SyncCanvasInputs(parentCanvas);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Overlay", GUILayout.Width(80)))
                ApplyCanvasPreset(canvas, RenderMode.ScreenSpaceOverlay, canvas.overrideSorting, canvas.sortingOrder, canvas.planeDistance);
            if (GUILayout.Button("Camera", GUILayout.Width(80)))
                ApplyCanvasPreset(canvas, RenderMode.ScreenSpaceCamera, canvas.overrideSorting, canvas.sortingOrder, Mathf.Max(0.1f, canvas.planeDistance));
            if (GUILayout.Button("World", GUILayout.Width(80)))
                ApplyCanvasPreset(canvas, RenderMode.WorldSpace, canvas.overrideSorting, canvas.sortingOrder, Mathf.Max(0.1f, canvas.planeDistance));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Sort", GUILayout.Width(32));
            canvas_sort_input = GUILayout.TextField(canvas_sort_input, GUILayout.Width(70));
            GUILayout.Label("Plane", GUILayout.Width(40));
            canvas_plane_input = GUILayout.TextField(canvas_plane_input, GUILayout.Width(70));
            if (GUILayout.Button("Apply Canvas", GUILayout.Width(110)))
            {
                int sortValue = canvas.sortingOrder;
                float planeValue = canvas.planeDistance;
                int.TryParse(canvas_sort_input, NumberStyles.Integer, CultureInfo.InvariantCulture, out sortValue);
                if (!TryParseFloat(canvas_plane_input, out planeValue)) planeValue = canvas.planeDistance;
                canvas.sortingOrder = sortValue;
                canvas.planeDistance = planeValue;
                if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
                    canvas.worldCamera = CameraManager.MainCam;
                SyncCanvasInputs(canvas);
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] CanvasApply path={selected_path} mode={canvas.renderMode} override={canvas.overrideSorting} sort={canvas.sortingOrder} plane={canvas.planeDistance:F2}");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("PIL Preset", GUILayout.Width(100)))
                ApplyCanvasPreset(canvas, RenderMode.ScreenSpaceOverlay, false, 0, 1f);
            if (GUILayout.Button("Low HUD", GUILayout.Width(90)))
                ApplyCanvasPreset(canvas, RenderMode.ScreenSpaceOverlay, true, -100, 1f);
            if (GUILayout.Button("UI Underlay", GUILayout.Width(100)))
                ApplyCanvasPreset(canvas, RenderMode.ScreenSpaceCamera, true, -100, 1f);
            if (GUILayout.Button("High HUD", GUILayout.Width(90)))
                ApplyCanvasPreset(canvas, RenderMode.ScreenSpaceOverlay, true, 200, 1f);
            GUILayout.EndHorizontal();
        }

        private void ToggleSelectedCanvas()
        {
            if (selected_element == null) return;
            var canvas = selected_element.GetComponent<Canvas>();
            if (canvas == null)
            {
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleCanvas path={selected_path} missing");
                return;
            }

            canvas.enabled = !canvas.enabled;
            MelonLoader.MelonLogger.Msg($"[EMES18DBG] ToggleCanvas path={selected_path} enabled={canvas.enabled}");
        }

        private string GetSelectedStateSummary()
        {
            if (selected_element == null) return "none";

            var go = selected_element.gameObject;
            var lines = new List<string>
            {
                $"activeSelf={go.activeSelf} activeInHierarchy={go.activeInHierarchy}",
                $"localPos={selected_element.localPosition} localScale={selected_element.localScale}"
            };

            var rect = selected_element as RectTransform;
            if (rect != null)
                lines.Add($"rectPos={rect.anchoredPosition} rectSize={rect.sizeDelta}");

            var graphic = selected_element.GetComponent<Graphic>();
            if (graphic != null)
            {
                var cr = selected_element.GetComponent<CanvasRenderer>();
                lines.Add($"graphic={graphic.enabled} alpha={graphic.color.a:F2} cull={(cr != null ? cr.cull.ToString() : "null")}");
            }

            var text = selected_element.GetComponent<TextMeshProUGUI>();
            if (text != null)
                lines.Add($"text={text.text}");

            var image = selected_element.GetComponent<Image>();
            if (image != null)
                lines.Add($"sprite={(image.sprite != null ? image.sprite.name : "null")}");

            var canvas = selected_element.GetComponent<Canvas>();
            if (canvas != null)
                lines.Add($"canvas={canvas.enabled} mode={canvas.renderMode} sort={canvas.sortingOrder} override={canvas.overrideSorting} plane={canvas.planeDistance:F2} cam={(canvas.worldCamera != null ? canvas.worldCamera.name : "null")}");

            var sr = selected_element.GetComponent<SpriteRenderer>();
            if (sr != null)
                lines.Add($"spriteRenderer={sr.enabled} sortingLayer={sr.sortingLayerName} order={sr.sortingOrder}");

            var pm = selected_element.GetComponent<PostMeshComp>();
            if (pm != null)
                lines.Add($"postMesh={pm.enabled}");

            var renderer = selected_element.GetComponent<Renderer>();
            if (renderer != null)
                lines.Add($"renderer={renderer.enabled}");

            return string.Join("\n", lines.ToArray());
        }

        private void PrintAllTargets()
        {
            foreach (var target in targets)
            {
                if (target == null || target.Transform == null) continue;
                MelonLoader.MelonLogger.Msg($"[EMES18DBG] target root={target.RootLabel} kind={target.Kind} path={target.Path} activeSelf={target.Transform.gameObject.activeSelf} activeInHierarchy={target.Transform.gameObject.activeInHierarchy}");
            }
        }

        private void PrintSelected()
        {
            if (selected_element == null)
            {
                MelonLoader.MelonLogger.Msg("[EMES18DBG] selected=null");
                return;
            }

            MelonLoader.MelonLogger.Msg($"[EMES18DBG] selected kind={selected_kind} path={selected_path} {GetSelectedStateSummary().Replace("\n", " | ")}");
        }

        private void SyncInputsFromCurrentValues()
        {
            pos_x_input = position_offset.x.ToString("F2", CultureInfo.InvariantCulture);
            pos_y_input = position_offset.y.ToString("F2", CultureInfo.InvariantCulture);
            pos_z_input = position_offset.z.ToString("F2", CultureInfo.InvariantCulture);
            scale_x_input = scale_offset.x.ToString("F2", CultureInfo.InvariantCulture);
            scale_y_input = scale_offset.y.ToString("F2", CultureInfo.InvariantCulture);
            scale_z_input = scale_offset.z.ToString("F2", CultureInfo.InvariantCulture);
            SyncCanvasInputs(GetSelectedCanvas(true));
        }

        private bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private void ApplyNumericInputs()
        {
            if (selected_element == null) return;

            float px = 0f, py = 0f, pz = 0f, sx = 1f, sy = 1f, sz = 1f;
            bool okPos = TryParseFloat(pos_x_input, out px) && TryParseFloat(pos_y_input, out py) && TryParseFloat(pos_z_input, out pz);
            bool okScale = TryParseFloat(scale_x_input, out sx) && TryParseFloat(scale_y_input, out sy) && TryParseFloat(scale_z_input, out sz);
            if (!okPos || !okScale) return;

            position_offset = new Vector3(px, py, pz);
            scale_offset = new Vector3(sx, sy, sz);
            selected_element.localPosition = position_offset;
            selected_element.localScale = scale_offset;
            SyncInputsFromCurrentValues();
            PrintSelected();
        }
    }
}
