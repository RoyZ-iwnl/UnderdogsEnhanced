using GHPC.Utility;
using GHPC;
using TMPro;
using UnityEngine;
using HarmonyLib;
using System.Reflection;

namespace UnderdogsEnhanced
{
    public class LimitedLRF : MonoBehaviour
    {
        public Transform canvas;
        private const float STEP = 5f;

        void Update()
        {
            if (!UnderdogsEnhancedMod.DEBUG_MODE) return;
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
}
