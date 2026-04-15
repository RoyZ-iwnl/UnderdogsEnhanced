/*
双履带传动增强实验记录

当前文件故意不启用任何代码，只保留详细注释，便于以后继续调试。
现在 Marder Engine Upgrade 已恢复为原先那版“只增强动力和基础转向响应”的实现。

1. 游戏原版履带车转向逻辑
   在 GHPC 当前版本里，NWH.VehiclePhysics.Steering::Steer() 对履带车的处理是：
   - 内侧履带调用 SetBrakeIntensity() 加刹车
   - 内侧履带的 motorTorque 被清零
   - 外侧履带拿走两侧合并后的全部 motorTorque
   也就是说，原版不是“双履带对转”，而是“单侧制动 + 外侧单边驱动”。

2. 这份实验代码原本想做什么
   核心思路是给 Steering::Steer() 打一个 Harmony Postfix：
   - 先让原版逻辑照常跑完
   - 再把内侧履带的刹车取消
   - 从外侧拿出一部分扭矩，改成给内侧施加反向扭矩
   这样低速转向会更像双履带差速/反向驱动，理论上能减轻“内侧刹停”的顿感。

3. 当时使用过的关键参数
   MaxCounterRotationRatio
   - 最大反向扭矩比例
   - 例如 0.42f 代表把总驱动扭矩的 42% 分给内侧反转
   - 这个值越大，低速掉头越灵活，也越容易感觉过头

   FullAssistBelowSpeedKph
   - 在这个速度以下，双履带辅助按满比例生效
   - 例如 0.5f 代表 0.5km/h 以下基本吃满反向扭矩

   MaxAssistSpeedKph
   - 超过这个速度后，不再额外施加双履带辅助
   - 值越大，中低速转向都会更活

4. 如果以后重新启用，建议先看哪组参数
   如果感觉“整体转向都太快”
   - 先调 MarderMain.cs 里的基础参数
   - trackedSteerIntensity
   - degreesPerSecondLimit
   - setHeadingSmoothTime
   - setHeadingMaxVel

   如果感觉“只有低速太会扭头 / 太会原地转”
   - 再调这份双履带补丁里的参数
   - MaxCounterRotationRatio
   - FullAssistBelowSpeedKph
   - MaxAssistSpeedKph

5. 重新启用时可以从下面这份代码草稿开始

using HarmonyLib;
using NWH.VehiclePhysics;
using NWH.WheelController3D;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal sealed class MarderDualTrackSteeringMarker : MonoBehaviour
    {
        // 最大反向扭矩比例。值越大，内侧履带越像“倒着帮你拐”。
        public float MaxCounterRotationRatio = 0.08f;

        // 低于该速度时，双履带辅助按满额生效。
        public float FullAssistBelowSpeedKph = 0.5f;

        // 高于该速度后，双履带辅助逐步衰减到 0。
        public float MaxAssistSpeedKph = 6f;
    }

    [HarmonyPatch(typeof(Steering), "Steer")]
    internal static class MarderDualTrackSteeringPatch
    {
        private const float MinSteerAngle = 0.01f;
        private const float MinTorqueMagnitude = 1f;
        private const float MinReverseRatio = 0.02f;

        private static void Postfix(Steering __instance)
        {
            VehicleController vc = __instance?.vc;
            if (vc == null || !MarderMain.marder_enabled.Value || !MarderMain.marder_engine_upgrade.Value)
                return;

            MarderDualTrackSteeringMarker marker = vc.GetComponent<MarderDualTrackSteeringMarker>();
            if (marker == null || vc.tracks == null || !vc.tracks.trackedVehicle)
                return;

            float steerAngle = __instance.Angle;
            if (Mathf.Abs(steerAngle) < MinSteerAngle)
                return;

            float absSpeedKph = Mathf.Abs(vc.SpeedKPH);
            if (absSpeedKph >= marker.MaxAssistSpeedKph)
                return;

            // 速度越低，辅助越强；越接近上限速度，辅助越弱。
            float speedFactor = absSpeedKph <= marker.FullAssistBelowSpeedKph
                ? 1f
                : 1f - Mathf.InverseLerp(marker.FullAssistBelowSpeedKph, marker.MaxAssistSpeedKph, absSpeedKph);

            float maxSteerAngle = Mathf.Max(Mathf.Abs(__instance.lowSpeedAngle), Mathf.Abs(__instance.highSpeedAngle), 1f);
            float steerFactor = Mathf.Clamp01(Mathf.Abs(steerAngle) / maxSteerAngle);
            float reverseRatio = Mathf.Clamp01(marker.MaxCounterRotationRatio * speedFactor * steerFactor);
            if (reverseRatio < MinReverseRatio)
                return;

            for (int i = 0; i < vc.axles.Count; i++)
            {
                Axle axle = vc.axles[i];
                if (axle == null || axle.leftWheel == null || axle.rightWheel == null)
                    continue;

                // 右转时左侧为外侧，左转时右侧为外侧。
                Wheel outerWheel = steerAngle > 0f ? axle.leftWheel : axle.rightWheel;
                Wheel innerWheel = steerAngle > 0f ? axle.rightWheel : axle.leftWheel;
                WheelController.Wheel outerWheelData = outerWheel.wheelController?.wheel;
                WheelController.Wheel innerWheelData = innerWheel.wheelController?.wheel;

                if (outerWheelData == null || innerWheelData == null)
                    continue;

                // 原版 Steering::Steer() 已经把两侧扭矩合并到外侧，这里把它重新拆回去。
                float totalDriveTorque = outerWheelData.motorTorque + innerWheelData.motorTorque;
                float driveTorqueMagnitude = Mathf.Abs(totalDriveTorque);
                if (driveTorqueMagnitude < MinTorqueMagnitude)
                    continue;

                // 取消原版给内侧履带打的刹车。
                innerWheel.SetBrakeIntensity(0f);

                float driveDirection = Mathf.Sign(totalDriveTorque);
                float reverseTorque = driveTorqueMagnitude * reverseRatio;
                float forwardTorque = driveTorqueMagnitude - reverseTorque;

                // 外侧继续正向驱动，内侧吃一部分反向驱动。
                outerWheelData.motorTorque = driveDirection * forwardTorque;
                innerWheelData.motorTorque = -driveDirection * reverseTorque;
            }
        }
    }
}

6. 对应的挂载方式
   如果要重新开实验，需要在 MarderMain.Apply() 的 Engine Upgrade 段里重新加回：

   MarderDualTrackSteeringMarker steeringMarker =
       UECommonUtil.GetOrAddComponent<MarderDualTrackSteeringMarker>(vc.gameObject);

   steeringMarker.MaxCounterRotationRatio = 0.08f;
   steeringMarker.FullAssistBelowSpeedKph = 0.5f;
   steeringMarker.MaxAssistSpeedKph = 6f;

7. 试车顺序建议
   - 原地小角度修正
   - 5 到 10 km/h 连续左右修方向
   - 15 到 20 km/h 轻打方向
   - 松开方向后观察是否摆头过度

保守的参数起步：
MaxCounterRotationRatio = 0.04f
FullAssistBelowSpeedKph = 0.5f
MaxAssistSpeedKph = 5f
*/
