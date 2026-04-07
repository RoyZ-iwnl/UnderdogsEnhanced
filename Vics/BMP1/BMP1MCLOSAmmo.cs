using System.Collections.Generic;
using GHPC.Weapons;
using GHPC.Weaponry;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public static class BMP1MCLOSAmmo
    {
        public struct MissileParamSnapshot
        {
            public float SpiralPower;
            public float SpiralAngularRate;
            public float MaximumRange;
            public float NoisePowerX;
            public float NoisePowerY;
            public float NoiseTimeScale;
            public float TurnSpeed;
            public float TntEquivalentKg;
            public float RhaPenetration;
            public float MaxSpallRha;
            public float MinSpallRha;
            public float SpallMultiplier;
            public float RangedFuseTime;
            public float RhaToFuse;
            public float MuzzleVelocity;
            public float Mass;
        }

        // 公共导弹名称 - 统一管理
        public const string MISSILE_NAME = "9M14TV Malyutka-TV";
        public static readonly string[] ORIGINAL_MISSILE_NAMES = new string[]
        {
            "9M14 Malyutka",
            "9M14M Malyutka-M",
            MISSILE_NAME
        };

        public static AmmoType ammo_9m14_mclos = null;
        public static AmmoType ammo_9m14_original = null;
        static AmmoType.AmmoClip clip_9m14_mclos = null;
        static AmmoCodexScriptable ammo_codex_9m14_mclos = null;
        static AmmoClipCodexScriptable clip_codex_9m14_mclos = null;
        static MissileParamSnapshot originalParams;
        static bool hasOriginalParams = false;
        static float turnSpeedBaseline = 0.5f;

        public static float GetTurnSpeedBaseline()
        {
            return turnSpeedBaseline;
        }

        public static bool TryGetOriginalParams(out MissileParamSnapshot snapshot)
        {
            snapshot = originalParams;
            return hasOriginalParams;
        }

        public static bool IsOriginalMissileName(string ammoName)
        {
            if (string.IsNullOrEmpty(ammoName)) return false;
            for (int i = 0; i < ORIGINAL_MISSILE_NAMES.Length; i++)
                if (ORIGINAL_MISSILE_NAMES[i] == ammoName) return true;
            return false;
        }

        static MissileParamSnapshot BuildSnapshot(AmmoType ammo)
        {
            return new MissileParamSnapshot
            {
                SpiralPower = ammo.SpiralPower,
                SpiralAngularRate = ammo.SpiralAngularRate,
                MaximumRange = ammo.MaximumRange,
                NoisePowerX = ammo.NoisePowerX,
                NoisePowerY = ammo.NoisePowerY,
                NoiseTimeScale = ammo.NoiseTimeScale,
                TurnSpeed = ammo.TurnSpeed,
                TntEquivalentKg = ammo.TntEquivalentKg,
                RhaPenetration = ammo.RhaPenetration,
                MaxSpallRha = ammo.MaxSpallRha,
                MinSpallRha = ammo.MinSpallRha,
                SpallMultiplier = ammo.SpallMultiplier,
                RangedFuseTime = ammo.RangedFuseTime,
                RhaToFuse = ammo.RhaToFuse,
                MuzzleVelocity = ammo.MuzzleVelocity,
                Mass = ammo.Mass
            };
        }

        // 运行时参数（可通过调试UI修改）
        // === 飞行参数 ===
        public const float DEFAULT_SPIRAL_POWER = 2.5f;           // 螺旋飞行强度 - 导弹飞行时的螺旋摆动幅度
        public const float DEFAULT_SPIRAL_ANGULAR_RATE = 1500f;   // 螺旋角速度 - 螺旋摆动的频率(度/秒)
        public const float DEFAULT_MAXIMUM_RANGE = 20000f;       // 最大射程(m)
        public const float DEFAULT_NOISE_POWER_X = 2f;            // 飞行噪声X轴 - 横向随机抖动幅度
        public const float DEFAULT_NOISE_POWER_Y = 2f;            // 飞行噪声Y轴 - 纵向随机抖动幅度
        public const float DEFAULT_NOISE_TIME_SCALE = 2f;         // 飞行噪声时间缩放 - 噪声变化速度
        public const float DEFAULT_TURN_SPEED = 0.2f;           // 转向速度 - 导弹姿态修正响应速度
        public static float Debug_SpiralPower = DEFAULT_SPIRAL_POWER;
        public static float Debug_SpiralAngularRate = DEFAULT_SPIRAL_ANGULAR_RATE;
        public static float Debug_MaximumRange = DEFAULT_MAXIMUM_RANGE;
        public static float Debug_NoisePowerX = DEFAULT_NOISE_POWER_X;
        public static float Debug_NoisePowerY = DEFAULT_NOISE_POWER_Y;
        public static float Debug_NoiseTimeScale = DEFAULT_NOISE_TIME_SCALE;
        public static float Debug_TurnSpeed = DEFAULT_TURN_SPEED;

        // === 威力参数 ===
        public const float DEFAULT_TNT_EQUIVALENT = 3.25f;        // 装药当量(kg) - 影响爆炸威力
        public const float DEFAULT_RHA_PENETRATION = 675f;       // 穿深(mm RHA) - 对匀质装甲的穿透力
        public const float DEFAULT_MAX_SPALL_RHA = 35.6f;           // 最大破片穿透(mm) - 破片能穿透的最大装甲厚度
        public const float DEFAULT_MIN_SPALL_RHA = 17.8f;           // 最小破片穿透(mm) - 破片能穿透的最小装甲厚度
        public const float DEFAULT_SPALL_MULTIPLIER = 2.5f;      // 破片倍数 - 破片数量倍率
        public const float DEFAULT_RANGED_FUSE_TIME = 500f;         // 定距引信触发时间(s)
        public const float DEFAULT_RHA_TO_FUSE = 0f;              // 引信触发所需等效RHA(mm)
        public const float DEFAULT_MUZZLE_VELOCITY = 140f;      // 初速(m/s)
        public const float DEFAULT_MASS = 10.9f;                 // 弹体质量(kg)
        public static float Debug_TntEquivalent = DEFAULT_TNT_EQUIVALENT;
        public static float Debug_RhaPenetration = DEFAULT_RHA_PENETRATION;
        public static float Debug_MaxSpallRha = DEFAULT_MAX_SPALL_RHA;
        public static float Debug_MinSpallRha = DEFAULT_MIN_SPALL_RHA;
        public static float Debug_SpallMultiplier = DEFAULT_SPALL_MULTIPLIER;
        public static float Debug_RangedFuseTime = DEFAULT_RANGED_FUSE_TIME;
        public static float Debug_RhaToFuse = DEFAULT_RHA_TO_FUSE;
        public static float Debug_MuzzleVelocity = DEFAULT_MUZZLE_VELOCITY;
        public static float Debug_Mass = DEFAULT_MASS;

        // === 音效参数 ===
        public const float DEFAULT_MISSILE_AUDIO_VOLUME = 0.2f;  // 导弹飞行音量倍率
        
        public static class MclosInputTuning
        {
            // 启用输入曲线修正：true 时会对 MCLOS 输入做倍率/曲线处理。
            public const bool DEFAULT_ENABLED = true;
            // 仅在导弹镜头内生效：true 可避免影响普通火炮/机枪瞄准输入。
            public const bool DEFAULT_ONLY_WHEN_MISSILE_CAMERA = true;
            // 输入总倍率（0.1~5）：整体放大/缩小横纵输入。
            public const float DEFAULT_INPUT_SCALE = 1f;
            // 输入曲线指数（0.2~2）：1=线性，<1 提升中心灵敏度，>1 降低中心灵敏度。
            public const float DEFAULT_CURVE_EXPONENT = 0.45f;
            // 启用动态 TurnSpeed：按输入幅值实时缩放导弹转向速度。
            public const bool DEFAULT_DYNAMIC_TURNSPEED_ENABLED = true;
            // 动态 TurnSpeed 最小倍率：输入接近 0 时使用。
            public const float DEFAULT_DYNAMIC_TURNSPEED_MIN_MULTIPLIER = 0.25f;
            // 动态 TurnSpeed 最大倍率：输入接近 1 时使用。
            public const float DEFAULT_DYNAMIC_TURNSPEED_MAX_MULTIPLIER = 2f;
            // 动态 TurnSpeed 映射指数：控制从最小到最大倍率的增长曲线。
            public const float DEFAULT_DYNAMIC_TURNSPEED_EXPONENT = 0.7f;

            // 运行时开关：是否启用输入修正。
            public static bool Enabled = DEFAULT_ENABLED;
            // 运行时开关：是否仅在导弹镜头生效。
            public static bool OnlyWhenMissileCamera = DEFAULT_ONLY_WHEN_MISSILE_CAMERA;
            // 运行时参数：输入总倍率。
            public static float InputScale = DEFAULT_INPUT_SCALE;
            // 运行时参数：输入曲线指数。
            public static float CurveExponent = DEFAULT_CURVE_EXPONENT;
            // 运行时开关：是否启用动态 TurnSpeed。
            public static bool DynamicTurnSpeedEnabled = DEFAULT_DYNAMIC_TURNSPEED_ENABLED;
            // 运行时参数：动态 TurnSpeed 最小倍率。
            public static float DynamicTurnSpeedMinMultiplier = DEFAULT_DYNAMIC_TURNSPEED_MIN_MULTIPLIER;
            // 运行时参数：动态 TurnSpeed 最大倍率。
            public static float DynamicTurnSpeedMaxMultiplier = DEFAULT_DYNAMIC_TURNSPEED_MAX_MULTIPLIER;
            // 运行时参数：动态 TurnSpeed 映射指数。
            public static float DynamicTurnSpeedExponent = DEFAULT_DYNAMIC_TURNSPEED_EXPONENT;
            // 仅用于 UI 显示：本帧是否正在应用输入修正。
            public static bool RuntimeApplying { get; private set; } = false;
            // 仅用于 UI 显示：当前帧 TurnSpeed 实际倍率。
            public static float LastTurnSpeedMultiplier { get; private set; } = 1f;

            public static void ResetToDefaults()
            {
                Enabled = DEFAULT_ENABLED;
                OnlyWhenMissileCamera = DEFAULT_ONLY_WHEN_MISSILE_CAMERA;
                InputScale = DEFAULT_INPUT_SCALE;
                CurveExponent = DEFAULT_CURVE_EXPONENT;
                DynamicTurnSpeedEnabled = DEFAULT_DYNAMIC_TURNSPEED_ENABLED;
                DynamicTurnSpeedMinMultiplier = DEFAULT_DYNAMIC_TURNSPEED_MIN_MULTIPLIER;
                DynamicTurnSpeedMaxMultiplier = DEFAULT_DYNAMIC_TURNSPEED_MAX_MULTIPLIER;
                DynamicTurnSpeedExponent = DEFAULT_DYNAMIC_TURNSPEED_EXPONENT;
                RuntimeApplying = false;
                LastTurnSpeedMultiplier = 1f;
            }

            public static bool ShouldApplyNow(bool missileCameraActive)
            {
                if (!Enabled) return false;
                if (OnlyWhenMissileCamera && !missileCameraActive) return false;
                return true;
            }

            // 对输入轴做曲线与倍率处理，输出仍限制在 [-1,1]。
            public static float ProcessAxis(float axis, bool applyNow)
            {
                RuntimeApplying = applyNow;
                if (!applyNow) return axis;

                float sign = Mathf.Sign(axis);
                float abs = Mathf.Abs(axis);
                float exp = Mathf.Clamp(CurveExponent, 0.2f, 2f);
                float curved = Mathf.Pow(abs, exp);
                float scaled = curved * Mathf.Clamp(InputScale, 0.1f, 5f);
                return sign * Mathf.Clamp(scaled, 0f, 1f);
            }

            // 按当前输入幅值动态调整 ammo.TurnSpeed。
            public static void ApplyDynamicTurnSpeed(AmmoType ammo, float horizontal, float vertical, bool applyNow)
            {
                if (ammo == null) return;

                float baseTurnSpeed = GetTurnSpeedBaseline();
                if (!applyNow || !DynamicTurnSpeedEnabled)
                {
                    LastTurnSpeedMultiplier = 1f;
                    ammo.TurnSpeed = baseTurnSpeed;
                    return;
                }

                float inputMagnitude = Mathf.Clamp01(new Vector2(horizontal, vertical).magnitude);
                float exp = Mathf.Clamp(DynamicTurnSpeedExponent, 0.2f, 3f);
                float t = Mathf.Pow(inputMagnitude, exp);
                float minMul = Mathf.Max(0.01f, DynamicTurnSpeedMinMultiplier);
                float maxMul = Mathf.Max(minMul, DynamicTurnSpeedMaxMultiplier);
                float mul = Mathf.Lerp(minMul, maxMul, t);
                LastTurnSpeedMultiplier = mul;
                ammo.TurnSpeed = baseTurnSpeed * mul;
            }
        }

        public static MissileParamSnapshot GetCurrentDebugParams()
        {
            return new MissileParamSnapshot
            {
                SpiralPower = Debug_SpiralPower,
                SpiralAngularRate = Debug_SpiralAngularRate,
                MaximumRange = Debug_MaximumRange,
                NoisePowerX = Debug_NoisePowerX,
                NoisePowerY = Debug_NoisePowerY,
                NoiseTimeScale = Debug_NoiseTimeScale,
                TurnSpeed = Debug_TurnSpeed,
                TntEquivalentKg = Debug_TntEquivalent,
                RhaPenetration = Debug_RhaPenetration,
                MaxSpallRha = Debug_MaxSpallRha,
                MinSpallRha = Debug_MinSpallRha,
                SpallMultiplier = Debug_SpallMultiplier,
                RangedFuseTime = Debug_RangedFuseTime,
                RhaToFuse = Debug_RhaToFuse,
                MuzzleVelocity = Debug_MuzzleVelocity,
                Mass = Debug_Mass
            };
        }

        public static void SetDebugParams(
            float spiralPower, float spiralAngularRate, float maximumRange,
            float noiseX, float noiseY, float noiseTime, float turnSpeed,
            float tnt, float penetration, float maxSpall, float minSpall, float spallMult,
            float rangedFuseTime, float rhaToFuse, float muzzleVelocity, float mass)
        {
            Debug_SpiralPower = spiralPower;
            Debug_SpiralAngularRate = spiralAngularRate;
            Debug_MaximumRange = maximumRange;
            Debug_NoisePowerX = noiseX;
            Debug_NoisePowerY = noiseY;
            Debug_NoiseTimeScale = noiseTime;
            Debug_TurnSpeed = turnSpeed;
            turnSpeedBaseline = turnSpeed;
            Debug_TntEquivalent = tnt;
            Debug_RhaPenetration = penetration;
            Debug_MaxSpallRha = maxSpall;
            Debug_MinSpallRha = minSpall;
            Debug_SpallMultiplier = spallMult;
            Debug_RangedFuseTime = rangedFuseTime;
            Debug_RhaToFuse = rhaToFuse;
            Debug_MuzzleVelocity = muzzleVelocity;
            Debug_Mass = mass;

            // 如果弹药已初始化，直接更新
            if (ammo_9m14_mclos != null)
            {
                ammo_9m14_mclos.SpiralPower = spiralPower;
                ammo_9m14_mclos.SpiralAngularRate = spiralAngularRate;
                ammo_9m14_mclos.MaximumRange = maximumRange;
                ammo_9m14_mclos.NoisePowerX = noiseX;
                ammo_9m14_mclos.NoisePowerY = noiseY;
                ammo_9m14_mclos.NoiseTimeScale = noiseTime;
                ammo_9m14_mclos.TurnSpeed = turnSpeed;
                ammo_9m14_mclos.TntEquivalentKg = tnt;
                ammo_9m14_mclos.RhaPenetration = penetration;
                ammo_9m14_mclos.MaxSpallRha = maxSpall;
                ammo_9m14_mclos.MinSpallRha = minSpall;
                ammo_9m14_mclos.SpallMultiplier = spallMult;
                ammo_9m14_mclos.RangedFuseTime = rangedFuseTime;
                ammo_9m14_mclos.RhaToFuse = rhaToFuse;
                ammo_9m14_mclos.MuzzleVelocity = muzzleVelocity;
                ammo_9m14_mclos.Mass = mass;

                MelonLogger.Msg($"[BMP-1 MCLOS] 调试参数已应用到现有弹药");
            }
        }

        public static void ResetDebugParamsToDefaults()
        {
            SetDebugParams(
                DEFAULT_SPIRAL_POWER,
                DEFAULT_SPIRAL_ANGULAR_RATE,
                DEFAULT_MAXIMUM_RANGE,
                DEFAULT_NOISE_POWER_X,
                DEFAULT_NOISE_POWER_Y,
                DEFAULT_NOISE_TIME_SCALE,
                DEFAULT_TURN_SPEED,
                DEFAULT_TNT_EQUIVALENT,
                DEFAULT_RHA_PENETRATION,
                DEFAULT_MAX_SPALL_RHA,
                DEFAULT_MIN_SPALL_RHA,
                DEFAULT_SPALL_MULTIPLIER,
                DEFAULT_RANGED_FUSE_TIME,
                DEFAULT_RHA_TO_FUSE,
                DEFAULT_MUZZLE_VELOCITY,
                DEFAULT_MASS
            );
        }

        public static void Apply(WeaponSystem atgm_ws, Vehicle vic)
        {
            if (ammo_9m14_mclos == null)
                Init(atgm_ws);

            if (ammo_9m14_mclos == null)
            {
#if DEBUG
                MelonLogger.Warning("[BMP-1 MCLOS] 弹药初始化失败，跳过参数调整");
#endif
                return;
            }

            try
            {
                var rack = atgm_ws.Feed.ReadyRack;

                int oldCount = rack.StoredClips != null ? System.Math.Max(rack.StoredClips.Count, 1) : 1;
                int count = oldCount;
                if (Bmp1Main.bmp1_mclos_ready_count != null && Bmp1Main.bmp1_mclos_ready_count.Value > 0)
                    count = Mathf.Clamp(Bmp1Main.bmp1_mclos_ready_count.Value, 1, 64);

                LoadoutManager loadoutManager = vic != null ? vic.GetComponent<LoadoutManager>() : null;
                if (count <= 1 && loadoutManager?.TotalAmmoCounts != null)
                {
                    int oldIndex = UECommonUtil.FindLoadedAmmoClipIndex(loadoutManager, clipCodex =>
                    {
                        AmmoType ammo = clipCodex?.ClipType?.MinimalPattern != null && clipCodex.ClipType.MinimalPattern.Length > 0
                            ? clipCodex.ClipType.MinimalPattern[0]?.AmmoType
                            : null;
                        return ammo != null && IsOriginalMissileName(ammo.Name);
                    });

                    if (oldIndex >= 0 && oldIndex < loadoutManager.TotalAmmoCounts.Length)
                        count = System.Math.Max(count, loadoutManager.TotalAmmoCounts[oldIndex]);
                }

                int preloadClipCount = count + 1;

                UECommonUtil.ReplaceReadyRack(rack, clip_9m14_mclos, preloadClipCount);
                UECommonUtil.RefreshLauncherFeed(atgm_ws.Feed);
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"[BMP-1 MCLOS] Apply 异常: {e.Message}\n{e.StackTrace}");
            }
        }

        static void Init(WeaponSystem atgm_ws)
        {
            AmmoType orig = atgm_ws.Feed.ReadyRack.ClipTypes[0]?.MinimalPattern?[0]?.AmmoType;

            if (orig == null)
            {
                foreach (AmmoCodexScriptable s in Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>())
                {
                    if (s.AmmoType.Name == "9M14 Malyutka") { orig = s.AmmoType; break; }
                }
            }

            if (orig == null) return;
            ammo_9m14_original = orig;
            originalParams = BuildSnapshot(orig);
            hasOriginalParams = true;

            // 打印原始参数值
#if DEBUG
            MelonLogger.Msg($"[BMP-1 MCLOS] 原始参数:");
            MelonLogger.Msg($"  SpiralPower={orig.SpiralPower}, SpiralAngularRate={orig.SpiralAngularRate}");
            MelonLogger.Msg($"  MaximumRange={orig.MaximumRange}");
            MelonLogger.Msg($"  NoisePowerX={orig.NoisePowerX}, NoisePowerY={orig.NoisePowerY}, NoiseTimeScale={orig.NoiseTimeScale}");
            MelonLogger.Msg($"  TurnSpeed={orig.TurnSpeed}");
            MelonLogger.Msg($"  TntEquivalentKg={orig.TntEquivalentKg}, RhaPenetration={orig.RhaPenetration}");
            MelonLogger.Msg($"  RangedFuseTime={orig.RangedFuseTime}, RhaToFuse={orig.RhaToFuse}, MuzzleVelocity={orig.MuzzleVelocity}, Mass={orig.Mass}");
#endif

            // 创建改进型导弹
            ammo_9m14_mclos = new AmmoType();
            UECommonUtil.ShallowCopy(ammo_9m14_mclos, orig);

            // === 导弹命名 ===
            ammo_9m14_mclos.Name = MISSILE_NAME;

            // === 飞行参数 ===
            ammo_9m14_mclos.SpiralPower = Debug_SpiralPower;           // 螺旋飞行强度
            ammo_9m14_mclos.SpiralAngularRate = Debug_SpiralAngularRate; // 螺旋角速度
            ammo_9m14_mclos.MaximumRange = Debug_MaximumRange;         // 最大射程(m)
            ammo_9m14_mclos.NoisePowerX = Debug_NoisePowerX;           // 飞行噪声X轴
            ammo_9m14_mclos.NoisePowerY = Debug_NoisePowerY;           // 飞行噪声Y轴
            ammo_9m14_mclos.NoiseTimeScale = Debug_NoiseTimeScale;     // 飞行噪声时间缩放
            ammo_9m14_mclos.TurnSpeed = Debug_TurnSpeed;               // 转向速度
            turnSpeedBaseline = ammo_9m14_mclos.TurnSpeed;

            // === 威力增强 ===
            ammo_9m14_mclos.TntEquivalentKg = Debug_TntEquivalent;    // 装药当量(kg)
            ammo_9m14_mclos.RhaPenetration = Debug_RhaPenetration;    // 穿深(mm RHA)
            ammo_9m14_mclos.MaxSpallRha = Debug_MaxSpallRha;          // 最大破片穿透(mm)
            ammo_9m14_mclos.MinSpallRha = Debug_MinSpallRha;          // 最小破片穿透(mm)
            ammo_9m14_mclos.SpallMultiplier = Debug_SpallMultiplier;  // 破片倍数
            ammo_9m14_mclos.RangedFuseTime = Debug_RangedFuseTime;    // 定距引信触发时间(s)
            ammo_9m14_mclos.RhaToFuse = Debug_RhaToFuse;              // 引信触发所需等效RHA(mm)
            ammo_9m14_mclos.MuzzleVelocity = Debug_MuzzleVelocity;    // 初速(m/s)
            ammo_9m14_mclos.Mass = Debug_Mass;                        // 弹体质量(kg)


            // === 弹药属性 ===
            ammo_9m14_mclos.CachedIndex = -1;

            // 创建弹药图鉴条目
            ammo_codex_9m14_mclos = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            ammo_codex_9m14_mclos.AmmoType = ammo_9m14_mclos;
            ammo_codex_9m14_mclos.name = "ammo_9m14_mclos";

            // 创建弹夹 - 保持原有容量
            var origClip = atgm_ws.Feed.ReadyRack.ClipTypes[0];
            clip_9m14_mclos = new AmmoType.AmmoClip();
            UECommonUtil.ShallowCopy(clip_9m14_mclos, origClip);
            clip_9m14_mclos.Name = MISSILE_NAME;
            clip_9m14_mclos.MinimalPattern = new AmmoCodexScriptable[] { ammo_codex_9m14_mclos };

            clip_codex_9m14_mclos = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            clip_codex_9m14_mclos.ClipType = clip_9m14_mclos;
            clip_codex_9m14_mclos.name = "clip_9m14_mclos";

#if DEBUG
            MelonLogger.Msg($"[BMP-1 MCLOS] 导弹初始化完成: {MISSILE_NAME}");
#endif
        }
    }
}
