using System.IO;
using FMOD;
using FMODUnity;
using GHPC.Audio;
using GHPC.Camera;
using GHPC.Player;
using GHPC.UI.Hud;
using GHPC.Weapons;
using GHPC.Weaponry;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal static class Marder25mmWeapon
    {
        internal const string WeaponDisplayName = "Oerlikon KBA 25mm Cannon";

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
            cachedCodex.name = "weapon_oerlikon_kba_25mm";
            cachedCodex.FriendlyName = WeaponDisplayName;
            cachedCodex.CaliberMm = 25;
            return cachedCodex;
        }
    }

    internal sealed class Marder25mmFireRateToggle : MonoBehaviour
    {
        private const float LowCycleTime = 60f / 175f;
        private const float HighCycleTime = 60f / 600f;
        private const float HighRateLoopPitch = 0.6f;
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
            if (!weaponSound.SingleShotByDefault)
                weaponSound.SingleShotMode = !useHighRate && Marder25mmLowRateAudio.HasReplacementSounds;

            if (useHighRate)
                Marder25mmLoopPitch.Apply(weaponSound, HighRateLoopPitch);
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

    internal static class Marder25mmLowRateAudio
    {
        private const string PreferredAudioFolder = "UE/sounds/KBA";
        private static readonly string[] InteriorFiles =
        {
            "wpn_mk20_shot_int_1.wav",
            "wpn_mk20_shot_int_2.wav",
            "wpn_mk20_shot_int_3.wav",
            "wpn_mk20_shot_int_4.wav",
            "wpn_mk20_shot_int_5.wav",
            "wpn_mk20_shot_int_6.wav"
        };
        private static readonly string[] ExteriorFiles =
        {
            "wpn_mk20_shot_ext_1.wav",
            "wpn_mk20_shot_ext_2.wav",
            "wpn_mk20_shot_ext_3.wav",
            "wpn_mk20_shot_ext_4.wav",
            "wpn_mk20_shot_ext_5.wav",
            "wpn_mk20_shot_ext_6.wav"
        };

        private static readonly FMOD.Sound[] interiorSounds = new FMOD.Sound[InteriorFiles.Length];
        private static readonly FMOD.Sound[] exteriorSounds = new FMOD.Sound[ExteriorFiles.Length];
        private static bool initialized;

        internal static AudioSettingsManager AudioSettingsManager;

        internal static bool HasInteriorSounds { get; private set; }
        internal static bool HasExteriorSounds { get; private set; }
        internal static bool HasReplacementSounds => HasInteriorSounds || HasExteriorSounds;

        internal static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            FMOD.System coreSystem = RuntimeManager.CoreSystem;
            string modPath = ResolveAudioPath();
            if (string.IsNullOrEmpty(modPath))
            {
                MelonLogger.Warning("[Marder 25mm] Custom audio folder not found: Mods/UE/sounds/KBA");
                return;
            }

            HasInteriorSounds = LoadSoundSet(coreSystem, modPath, InteriorFiles, interiorSounds, MODE._2D);
            HasExteriorSounds = LoadSoundSet(coreSystem, modPath, ExteriorFiles, exteriorSounds, MODE._3D_INVERSEROLLOFF);

            if (HasExteriorSounds)
            {
                for (int i = 0; i < exteriorSounds.Length; i++)
                {
                    if (exteriorSounds[i].hasHandle())
                        exteriorSounds[i].set3DMinMaxDistance(35f, 2000f);
                }
            }
        }

        internal static void OnSceneLoaded(string sceneName)
        {
            if (UECommonUtil.IsMenuScene(sceneName))
            {
                AudioSettingsManager = null;
                return;
            }

            GameObject app = GameObject.Find("_APP_GHPC_");
            AudioSettingsManager = app != null ? app.GetComponent<AudioSettingsManager>() : null;
        }

        internal static bool TryPlayReplacement(WeaponAudio weaponAudio)
        {
            if (!HasReplacementSounds || weaponAudio == null || CameraManager.Instance == null)
                return false;

            PlayerInput playerInput = PlayerInput.Instance;
            WeaponSystem currentWeapon = playerInput?.CurrentPlayerWeapon?.Weapon;
            if (currentWeapon == null || currentWeapon.WeaponSound != weaponAudio || currentWeapon.name != Marder25mmWeapon.WeaponDisplayName)
                return false;

            Marder25mmFireRateToggle toggle = currentWeapon.GetComponent<Marder25mmFireRateToggle>();
            if (toggle == null || toggle.IsHighRateSelected)
                return false;

            bool exteriorMode = CameraManager.Instance.ExteriorMode;
            FMOD.Sound soundToPlay = exteriorMode ? GetRandomSound(exteriorSounds) : GetRandomSound(interiorSounds);
            if (!soundToPlay.hasHandle())
                soundToPlay = exteriorMode ? GetRandomSound(interiorSounds) : GetRandomSound(exteriorSounds);

            if (!soundToPlay.hasHandle())
                return false;

            FMOD.System coreSystem = RuntimeManager.CoreSystem;
            Vector3 vec = weaponAudio.transform.position;

            VECTOR pos = new VECTOR();
            pos.x = vec.x;
            pos.y = vec.y;
            pos.z = vec.z;

            VECTOR vel = new VECTOR();
            vel.x = 0f;
            vel.y = 0f;
            vel.z = 0f;

            ChannelGroup channelGroup;
            coreSystem.createChannelGroup("master", out channelGroup);
            channelGroup.setVolumeRamp(true);
            channelGroup.setMode(exteriorMode ? MODE._3D_WORLDRELATIVE : MODE._2D);

            FMOD.Channel channel;
            coreSystem.playSound(soundToPlay, channelGroup, true, out channel);

            float gameVolume = AudioSettingsManager != null ? AudioSettingsManager._previousVolume : 0.6f;
            float gunVolume = exteriorMode
                ? gameVolume + 0.07f * (gameVolume * 10f)
                : gameVolume + 0.10f * (gameVolume * 10f);

            channel.setVolume(gunVolume);
            channel.setVolumeRamp(true);
            if (exteriorMode)
            {
                channel.set3DAttributes(ref pos, ref vel);
                channelGroup.set3DAttributes(ref pos, ref vel);
            }
            channel.setPaused(false);
            return true;
        }

        private static string ResolveAudioPath()
        {
            string preferredPath = Path.Combine(MelonEnvironment.ModsDirectory, PreferredAudioFolder);
            return Directory.Exists(preferredPath) ? preferredPath : null;
        }

        private static bool LoadSoundSet(FMOD.System coreSystem, string modPath, string[] fileNames, FMOD.Sound[] target, MODE mode)
        {
            bool hasAny = false;

            for (int i = 0; i < fileNames.Length; i++)
            {
                string filePath = Path.Combine(modPath, fileNames[i]);
                if (!File.Exists(filePath))
                    continue;

                if (coreSystem.createSound(filePath, mode, out target[i]) == RESULT.OK)
                    hasAny = true;
            }

            return hasAny;
        }

        private static FMOD.Sound GetRandomSound(FMOD.Sound[] soundSet)
        {
            int[] validIndices = new int[soundSet.Length];
            int validCount = 0;

            for (int i = 0; i < soundSet.Length; i++)
            {
                if (soundSet[i].hasHandle())
                    validIndices[validCount++] = i;
            }

            if (validCount == 0)
                return default(FMOD.Sound);

            int chosen = validIndices[Random.Range(0, validCount)];
            return soundSet[chosen];
        }
    }

    [HarmonyPatch(typeof(WeaponAudio), "FinalStartLoop")]
    internal static class Marder25mmLowRateAudioPatch
    {
        private static bool Prefix(WeaponAudio __instance)
        {
            return !Marder25mmLowRateAudio.TryPlayReplacement(__instance);
        }

        private static void Postfix(WeaponAudio __instance)
        {
            Marder25mmLoopPitch.TryApplyForCurrentWeapon(__instance);
        }
    }

    internal static class Marder25mmLoopPitch
    {
        private static readonly System.Reflection.FieldInfo LoopEventInstanceField = AccessTools.Field(typeof(WeaponAudio), "_loopEventInstance");

        internal static void TryApplyForCurrentWeapon(WeaponAudio weaponAudio)
        {
            if (weaponAudio == null || weaponAudio.SingleShotMode)
                return;

            PlayerInput playerInput = PlayerInput.Instance;
            WeaponSystem currentWeapon = playerInput?.CurrentPlayerWeapon?.Weapon;
            if (currentWeapon == null || currentWeapon.WeaponSound != weaponAudio || currentWeapon.name != Marder25mmWeapon.WeaponDisplayName)
                return;

            Marder25mmFireRateToggle toggle = currentWeapon.GetComponent<Marder25mmFireRateToggle>();
            if (toggle == null || !toggle.IsHighRateSelected)
                return;

            Apply(weaponAudio, 0.6f);
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
