using System.Reflection;
using GHPC;
using GHPC.Audio;
using GHPC.Camera;
using GHPC.Player;
using GHPC.UI.Hud;
using GHPC.Weapons;
using GHPC.Weaponry;
using HarmonyLib;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal static class Marder35mmWeapon
    {
        internal const string WeaponDisplayName = "Oerlikon 35mm Revolver Cannon";

        private static WeaponSystemCodexScriptable cachedCodex;

        internal static void ApplyDescriptor(WeaponSystemInfo weaponInfo)
        {
            if (weaponInfo?.Weapon == null)
                return;

            weaponInfo.Name = WeaponDisplayName;
            weaponInfo.Weapon.name = WeaponDisplayName;

            WeaponSystemCodexScriptable codex = GetOrCreateCodex(weaponInfo.Weapon.CodexEntry);
            if (codex != null)
                weaponInfo.Weapon.CodexEntry = codex;
        }

        private static WeaponSystemCodexScriptable GetOrCreateCodex(WeaponSystemCodexScriptable donorCodex)
        {
            if (cachedCodex != null)
                return cachedCodex;

            if (donorCodex == null)
                return null;

            cachedCodex = ScriptableObject.CreateInstance<WeaponSystemCodexScriptable>();
            UECommonUtil.ShallowCopy(cachedCodex, donorCodex);
            cachedCodex.name = "weapon_oerlikon_35mm_revolver";
            cachedCodex.FriendlyName = WeaponDisplayName;
            cachedCodex.CaliberMm = 35;
            return cachedCodex;
        }
    }

    internal sealed class Marder35mmFireRateToggle : MonoBehaviour
    {
        // 35mm射速参数
        private const float LowCycleTime = 60f / 200f;   // 200 RPM
        private const float HighCycleTime = 60f / 1000f; // 1000 RPM
        private const float HighRateLoopPitch = 1000f / 600f;  // ≈1.67倍速（使用600rpm音频加速到1000rpm）

        private static readonly string[] AlertMessages = { "Low fire rate selected", "High fire rate selected" };
        private static AlertHud cachedAlertHud;

        private WeaponSystem weapon;
        private AmmoFeed feed;
        private bool highRateSelected = true;

        internal bool IsHighRateSelected => highRateSelected;

        private void Awake()
        {
            weapon = GetComponent<WeaponSystem>();
            feed = weapon?.Feed;

            // 初始化音频设置
            if (weapon?.WeaponSound != null)
            {
                // 低射速单发模式使用华约30mm单发音频
                weapon.WeaponSound.SingleShotByDefault = true;
                weapon.WeaponSound.SingleShotEventPaths = new string[] { "event:/Weapons/autocannon_2a42_single" };
                // 高射速循环模式使用华约30mm 600rpm音频（再通过pitch加速到1000rpm）
                weapon.WeaponSound.LoopEventPath = "event:/Weapons/autocannon_2a42_600rpm";
            }

            ApplyRate(highRateSelected);
        }

        private void Update()
        {
            if (weapon == null)
                return;

            PlayerInput playerInput = PlayerInput.Instance;
            if (playerInput?.CurrentPlayerWeapon?.Weapon != weapon)
                return;

            if (!Input.GetKeyDown(KeyCode.B))
                return;

            highRateSelected = !highRateSelected;
            ApplyRate(highRateSelected);

            ResolveAlertHud()?.AddAlertMessage(AlertMessages[highRateSelected ? 1 : 0], 2f);
        }

        private void ApplyRate(bool useHighRate)
        {
            float cycleTime = useHighRate ? HighCycleTime : LowCycleTime;

            if (weapon != null)
                weapon._cycleTimeSeconds = cycleTime;

            if (feed != null)
                feed._totalCycleTime = cycleTime;

            ApplySoundMode(useHighRate);
        }

        private void ApplySoundMode(bool useHighRate)
        {
            var weaponSound = weapon?.WeaponSound;
            if (weaponSound == null)
                return;

            weaponSound.FinalStopLoop();

            // 低射速：单发模式（使用华约30mm单发音频）
            if (!useHighRate)
            {
                weaponSound.SingleShotMode = true;
            }
            else
            {
                // 高射速：循环模式，使用600rpm音频加速到1000rpm
                weaponSound.SingleShotMode = false;
                Marder35mmLoopPitch.Apply(weaponSound, HighRateLoopPitch);
            }
        }

        private static AlertHud ResolveAlertHud()
        {
            if (cachedAlertHud != null)
                return cachedAlertHud;

            GameObject app = GameObject.Find("_APP_GHPC_");
            Transform alertTransform = app != null ? app.transform.Find("UIHUDCanvas/system alert text") : null;
            cachedAlertHud = alertTransform != null ? alertTransform.GetComponent<AlertHud>() : null;
            return cachedAlertHud;
        }
    }

    [HarmonyPatch(typeof(WeaponAudio), "FinalStartLoop")]
    internal static class Marder35mmLowRateAudioPatch
    {
        private static bool Prefix(WeaponAudio __instance)
        {
            // 检查是否是35mm武器且在低射速模式
            PlayerInput playerInput = PlayerInput.Instance;
            WeaponSystem currentWeapon = playerInput?.CurrentPlayerWeapon?.Weapon;

            if (currentWeapon == null || currentWeapon.name != Marder35mmWeapon.WeaponDisplayName)
                return true;

            Marder35mmFireRateToggle toggle = currentWeapon.GetComponent<Marder35mmFireRateToggle>();
            if (toggle == null || toggle.IsHighRateSelected)
                return true;

            // 低射速模式：使用SingleShotMode，让游戏使用SingleShotEventPaths指定的华约30mm音频
            // 不需要额外处理，WeaponAudio会自动使用我们设置的音频路径
            return true;
        }

        private static void Postfix(WeaponAudio __instance)
        {
            Marder35mmLoopPitch.TryApplyForCurrentWeapon(__instance);
        }
    }

    internal static class Marder35mmLoopPitch
    {
        private static readonly FieldInfo LoopEventInstanceField = AccessTools.Field(typeof(WeaponAudio), "_loopEventInstance");

        internal static void TryApplyForCurrentWeapon(WeaponAudio weaponAudio)
        {
            if (weaponAudio == null || weaponAudio.SingleShotMode)
                return;

            PlayerInput playerInput = PlayerInput.Instance;
            WeaponSystem currentWeapon = playerInput?.CurrentPlayerWeapon?.Weapon;
            if (currentWeapon == null || currentWeapon.WeaponSound != weaponAudio || currentWeapon.name != Marder35mmWeapon.WeaponDisplayName)
                return;

            Marder35mmFireRateToggle toggle = currentWeapon.GetComponent<Marder35mmFireRateToggle>();
            if (toggle == null || !toggle.IsHighRateSelected)
                return;

            // 高射速：使用600rpm音频，pitch = 1000/600 ≈ 1.67
            Apply(weaponAudio, 1000f / 600f);
        }

        internal static void Apply(WeaponAudio weaponAudio, float pitch)
        {
            if (weaponAudio == null || LoopEventInstanceField == null)
                return;

            object loopEventInstanceObject = LoopEventInstanceField.GetValue(weaponAudio);
            if (!(loopEventInstanceObject is FMOD.Studio.EventInstance loopEventInstance))
                return;

            loopEventInstance.setPitch(pitch);
        }
    }
}