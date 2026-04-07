using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using MelonLoader;
using GHPC;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Player;
using GHPC.Vehicle;
using GHPC.Equipment;
using GHPC.Weapons;
using GHPC.Effects;
using GHPC.Humans;
using NWH.VehiclePhysics;

namespace UnderdogsEnhanced
{
    /// <summary>
    /// Repair 功能主入口类
    /// </summary>
    public static class UERepairMain
    {
        ////////////////////////////////////////////////////////////////
        // Repair
        ////////////////////////////////////////////////////////////////
        public static MelonPreferences_Entry<bool> repair_enabled;

        public static void Config(MelonPreferences_Category cfg)
        {
            repair_enabled = cfg.CreateEntry("Repair Function", true);
            repair_enabled.Description = "Enable vehicle repair function (press J to repair; default: enabled)";
        }

        private const KeyCode RepairHotkey = KeyCode.J;

        /// <summary>
        /// 处理修复热键，由主程序 OnUpdate 调用
        /// </summary>
        public static void HandleRepairHotkey()
        {
            if (Input.GetKeyDown(RepairHotkey))
            {
                var playerUnit = PlayerInput.Instance?.CurrentPlayerUnit;
                string blockReason = null;
                if (playerUnit != null && !TryGetRepairBlockReason(playerUnit, out blockReason))
                {
                    PerformRepair(playerUnit as Vehicle);
                }
                else if (!string.IsNullOrEmpty(blockReason))
                {
                    UnderdogsDebug.LogRepair($"[Repair] Blocked: {blockReason}");
                }
            }
        }

        private static void PerformRepair(Vehicle vehicle)
        {
            if (vehicle == null)
            {
                UnderdogsDebug.LogRepair("[Repair] Vehicle is null");
                return;
            }

            try
            {
                UnderdogsDebug.LogRepair("[Repair] ===== Starting Repair =====");
                LogVehicleState(vehicle, "Before");

                // Step 1: Vehicle status flags
                UnderdogsDebug.LogRepair($"[Repair] Step 1: UnitIncapacitated={vehicle.UnitIncapacitated}, CannotMove={vehicle.CannotMove}, CannotShoot={vehicle.CannotShoot}, Neutralized={vehicle.Neutralized}");
                vehicle.UnitIncapacitated = false;
                RepairUtils.SetField(vehicle, "<CannotMove>k__BackingField", false);
                RepairUtils.SetField(vehicle, "_cannotMove", false);
                RepairUtils.SetField(vehicle, "<CannotShoot>k__BackingField", false);
                RepairUtils.SetField(vehicle, "_cannotShoot", false);
                RepairUtils.SetField(vehicle, "<Neutralized>k__BackingField", false);
                RepairUtils.SetField(vehicle, "_neutralized", false);
                RepairUtils.SetField(vehicle, "<DamageStatus>k__BackingField", string.Empty);
                RepairUtils.SetField(vehicle, "_damageStatus", string.Empty);
                UnderdogsDebug.LogRepair($"[Repair] Step 1 Done: UnitIncapacitated={vehicle.UnitIncapacitated}, CannotMove={vehicle.CannotMove}");

                // Step 2: Chassis restoration
                var chassis = vehicle.Chassis as GHPC.NwhChassis;
                if (chassis != null)
                {
                    UnderdogsDebug.LogRepair($"[Repair] Step 2: EnginePower calling SetEnginePowerPercent(100)");
                    chassis.SetEnginePowerPercent(100.0f);
                    UnderdogsDebug.LogRepair($"[Repair] Step 2 Done: Engine power restored");

                    UnderdogsDebug.LogRepair($"[Repair] Step 3: Transmission calling SetTransmissionIntegrityPercent(100)");
                    chassis.SetTransmissionIntegrityPercent(100.0f);
                    UnderdogsDebug.LogRepair($"[Repair] Step 3 Done: Transmission restored");

                    UnderdogsDebug.LogRepair($"[Repair] Step 4: Unfreeze chassis");
                    chassis.Unfreeze(999f, false);
                    UnderdogsDebug.LogRepair($"[Repair] Step 4 Done: Chassis unfrozen");

                    UnderdogsDebug.LogRepair($"[Repair] Step 5: Reset flags _broken={chassis._broken}, _disabled={chassis._disabled}");
                    chassis._broken = false;
                    chassis._disabled = false;
                    chassis._origTrackSpeedCoefficient = 1.0f;
                    UnderdogsDebug.LogRepair($"[Repair] Step 5 Done: _broken={chassis._broken}, _disabled={chassis._disabled}");

                    UnderdogsDebug.LogRepair($"[Repair] Step 6: Reset track melted flags and times");
                    if (chassis._axleMeltedFlags != null)
                    {
                        for (int i = 0; i < chassis._axleMeltedFlags.Length; i++)
                            chassis._axleMeltedFlags[i] = false;
                    }
                    if (chassis._axleMeltTimes != null)
                    {
                        for (int i = 0; i < chassis._axleMeltTimes.Length; i++)
                            chassis._axleMeltTimes[i] = 0f;
                    }
                    UnderdogsDebug.LogRepair($"[Repair] Step 6 Done: Track flags cleared");

                    UnderdogsDebug.LogRepair($"[Repair] Step 7: Parking brake _parkingBrake={chassis._parkingBrake}, _braking={chassis._braking}");
                    chassis._parkingBrake = false;
                    chassis._braking = false;
                    chassis._constrained = false;
                    chassis.SetParkingConstraints(false, false, 0f);
                    UnderdogsDebug.LogRepair($"[Repair] Step 7 Done: _parkingBrake={chassis._parkingBrake}");
                }

                // Step 8: GameObject components
                var go = RepairUtils.GetGameObject(vehicle);
                if (go != null)
                {
                    // DrivableChassis
                    var drivableChassis = RepairUtils.GetDrivableChassis(vehicle, go) as DrivableChassis;
                    if (drivableChassis != null)
                    {
                        UnderdogsDebug.LogRepair($"[Repair] Step 8a: DrivableChassis _canMove={drivableChassis._canMove}, _disabledEngine={drivableChassis._disabledEngine}");
                        drivableChassis._canMove = true;
                        drivableChassis._disabledEngine = false;
                        drivableChassis._killedEngine = false;
                        drivableChassis._damagedDrivetrain = false;
                        drivableChassis._killedTracks = false;
                        drivableChassis._statusMessage = string.Empty;
                        drivableChassis.EngineOn = true;
                        drivableChassis.SetEngineOn(true);
                        drivableChassis.SetIsParked(false);
                        UnderdogsDebug.LogRepair($"[Repair] Step 8a Done: _canMove={drivableChassis._canMove}, EngineOn={drivableChassis.EngineOn}");
                    }

                    // Tracks
                    var tracks = go.GetComponent<NWH.VehiclePhysics.Tracks>();
                    if (tracks != null)
                    {
                        tracks.turnSpeedLimit = 999f;
                        tracks.leftTrackVel = 0f;
                        tracks.rightTrackVel = 0f;
                    }

                    // ChassisDamageManager
                    var chassisDmg = go.GetComponent<GHPC.Vehicle.ChassisDamageManager>();
                    if (chassisDmg != null)
                    {
                        UnderdogsDebug.LogRepair($"[Repair] Step 8b: ChassisDamageManager _engineDestroyed={chassisDmg._engineDestroyed}, _transmissionDestroyed={chassisDmg._transmissionDestroyed}");
                        chassisDmg._engineDestroyed = false;
                        chassisDmg._engineDamaged = false;
                        chassisDmg._transmissionDestroyed = false;
                        chassisDmg._transmissionDamaged = false;
                        chassisDmg._radiatorDestroyed = false;
                        chassisDmg._leftTrackDestroyed = false;
                        chassisDmg._rightTrackDestroyed = false;
                        RepairUtils.SetField(chassisDmg, "<DamageStatus>k__BackingField", string.Empty);
                        RepairUtils.SetField(chassisDmg, "_damageStatus", string.Empty);
                        if (chassisDmg.EngineHitZone != null) chassisDmg.engineRepaired(chassisDmg.EngineHitZone);
                        if (chassisDmg.TransmissionHitZone != null) chassisDmg.transmissionRepaired(chassisDmg.TransmissionHitZone);
                        if (chassisDmg.RadiatorHitZone != null) chassisDmg.radiatorRepaired(chassisDmg.RadiatorHitZone);
                        if (chassisDmg.LeftTrackHitZone != null) chassisDmg.leftTrackRepaired(chassisDmg.LeftTrackHitZone);
                        if (chassisDmg.RightTrackHitZone != null) chassisDmg.rightTrackRepaired(chassisDmg.RightTrackHitZone);
                        chassisDmg.updateDamageText();
                        UnderdogsDebug.LogRepair($"[Repair] Step 8b Done: _engineDestroyed={chassisDmg._engineDestroyed}");
                    }

                    // DamageHandler
                    var damageHandler = go.GetComponent<NWH.VehiclePhysics.DamageHandler>();
                    if (damageHandler != null)
                    {
                        UnderdogsDebug.LogRepair($"[Repair] Step 8c: DamageHandler damage={damageHandler.damage}");
                        damageHandler.damage = 0f;
                        damageHandler.performanceDegradation = false;
                        UnderdogsDebug.LogRepair($"[Repair] Step 8c Done: damage={damageHandler.damage}");
                    }

                    RepairAmmoFeeds(go);
                }

                // Step 9: AimablePlatforms (turrets)
                var platforms = vehicle.AimablePlatforms;
                if (platforms != null)
                {
                    int platformCount = 0;
                    foreach (var p in platforms) if (p != null) platformCount++;
                    UnderdogsDebug.LogRepair($"[Repair] Step 9: Repairing {platformCount} AimablePlatforms");
                    foreach (var platform in platforms)
                    {
                        if (platform == null) continue;
                        UnderdogsDebug.LogRepair($"[Repair] Step 9: Platform _destroyed={platform._destroyed}, IsDetached={platform.IsDetached}");
                        platform._destroyed = false;
                        platform.IsDetached = false;
                        platform.PowerEnabled = true;
                        RepairUtils.SetField(platform, "<Damaged>k__BackingField", false);
                        RepairUtils.SetField(platform, "_damaged", false);
                        UnderdogsDebug.LogRepair($"[Repair] Step 9 Done: Platform _destroyed={platform._destroyed}");
                    }
                }

                // Step 10: Engine and transmission mobility state
                RepairEngineMobilityState(vehicle);
                RepairTransmissionMobilityState(vehicle);

                // Step 11: Crew
                RepairCrewMembers(vehicle, go);

                // Step 12: Damage status sources
                RepairDamageStatusSources(vehicle, go);
                ClearDamageStatusState(vehicle, go);

                // Step 13: Refresh UI
                RefreshPlayerDamageUi();
                ClearCompassMarkers();

                UnderdogsDebug.LogRepair("[Repair] ===== Repair Complete =====");
                LogVehicleState(vehicle, "After");
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Repair failed: {e}");
            }
        }

        private static void LogVehicleState(Vehicle vehicle, string label)
        {
            if (vehicle == null) return;
            var go = RepairUtils.GetGameObject(vehicle);
            var chassis = vehicle.Chassis as GHPC.NwhChassis;
            var chassisDmg = go?.GetComponent<GHPC.Vehicle.ChassisDamageManager>();

            UnderdogsDebug.LogRepair($"[Repair] {label}: CannotMove={vehicle.CannotMove}, CannotShoot={vehicle.CannotShoot}, Neutralized={vehicle.Neutralized}, UnitIncapacitated={vehicle.UnitIncapacitated}");
            UnderdogsDebug.LogRepair($"[Repair] {label}: DamageStatus={vehicle.DamageStatus ?? "empty"}");

            if (chassis != null)
            {
                UnderdogsDebug.LogRepair($"[Repair] {label}: Chassis _broken={chassis._broken}, _disabled={chassis._disabled}, _parkingBrake={chassis._parkingBrake}");
            }

            if (chassisDmg != null)
            {
                UnderdogsDebug.LogRepair($"[Repair] {label}: ChassisDmg _engineDestroyed={chassisDmg._engineDestroyed}, _transmissionDestroyed={chassisDmg._transmissionDestroyed}");
            }
        }

        private static bool TryGetRepairBlockReason(object playerUnit, out string blockReason)
        {
            blockReason = null;
            if (playerUnit == null)
            {
                blockReason = "No vehicle detected";
                return true;
            }

            var vehicleDamageStatus = RepairUtils.GetMemberValue(playerUnit, "DamageStatus") as string;
            if (ContainsEvacuationMarker(vehicleDamageStatus))
            {
                blockReason = "Crew bailed out";
                return true;
            }

            var vehicleInfo = RepairUtils.GetPlayerVehicleInfo(playerUnit);
            if (vehicleInfo != null)
            {
                if (vehicleInfo.IsDead || vehicleInfo.HardKilled || vehicleInfo.AllCrewDead)
                {
                    blockReason = "Vehicle destroyed";
                    return true;
                }
            }

            if (RepairUtils.GetMemberValue(playerUnit, "Neutralized") is bool neutralized && neutralized)
            {
                blockReason = "Vehicle neutralized";
                return true;
            }

            if (RepairUtils.IsObjectMarkedDestroyed(playerUnit))
            {
                blockReason = "Vehicle destroyed";
                return true;
            }

            return false;
        }

        private static bool ContainsEvacuationMarker(string statusText)
        {
            if (string.IsNullOrWhiteSpace(statusText)) return false;
            return statusText.IndexOf("evac", StringComparison.OrdinalIgnoreCase) >= 0
                || statusText.IndexOf("bail", StringComparison.OrdinalIgnoreCase) >= 0
                || statusText.IndexOf("abandon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void RepairEngineMobilityState(Vehicle vehicle)
        {
            RepairUtils.SetField(vehicle, "<CannotMove>k__BackingField", false);
            RepairUtils.SetField(vehicle, "_cannotMove", false);
            RepairUtils.SetField(vehicle, "<DamageStatus>k__BackingField", string.Empty);
            RepairUtils.SetField(vehicle, "_damageStatus", string.Empty);

            var go = RepairUtils.GetGameObject(vehicle);
            if (go == null) return;

            var drivableChassis = RepairUtils.GetDrivableChassis(vehicle, go) as DrivableChassis;
            if (drivableChassis != null)
            {
                drivableChassis._canMove = true;
                drivableChassis._disabledEngine = false;
                drivableChassis._killedEngine = false;
                drivableChassis._statusMessage = string.Empty;
                drivableChassis.EngineOn = true;
                drivableChassis.SetEngineOn(true);
            }

            var chassisDmg = go.GetComponent<ChassisDamageManager>();
            if (chassisDmg != null)
            {
                chassisDmg._engineDestroyed = false;
                chassisDmg._engineDamaged = false;
                if (chassisDmg.EngineHitZone != null)
                {
                    RepairUtils.RepairDestructibleComponent(chassisDmg.EngineHitZone);
                    chassisDmg.engineRepaired(chassisDmg.EngineHitZone);
                }
                RepairUtils.SetField(chassisDmg, "<DamageStatus>k__BackingField", string.Empty);
                RepairUtils.SetField(chassisDmg, "_damageStatus", string.Empty);
                chassisDmg.updateDamageText();
            }

            RepairDrivePhysics(vehicle, go);
        }

        private static void RepairTransmissionMobilityState(Vehicle vehicle)
        {
            RepairUtils.SetField(vehicle, "<CannotMove>k__BackingField", false);
            RepairUtils.SetField(vehicle, "_cannotMove", false);
            RepairUtils.SetField(vehicle, "<DamageStatus>k__BackingField", string.Empty);
            RepairUtils.SetField(vehicle, "_damageStatus", string.Empty);

            var go = RepairUtils.GetGameObject(vehicle);
            if (go == null) return;

            var drivableChassis = RepairUtils.GetDrivableChassis(vehicle, go) as DrivableChassis;
            if (drivableChassis != null)
            {
                drivableChassis._canMove = true;
                drivableChassis._damagedDrivetrain = false;
                drivableChassis._killedTracks = false;
                drivableChassis._statusMessage = string.Empty;
            }

            var chassisDmg = go.GetComponent<ChassisDamageManager>();
            if (chassisDmg != null)
            {
                chassisDmg._transmissionDestroyed = false;
                chassisDmg._transmissionDamaged = false;
                chassisDmg._leftTrackDestroyed = false;
                chassisDmg._rightTrackDestroyed = false;
                if (chassisDmg.TransmissionHitZone != null)
                {
                    RepairUtils.RepairDestructibleComponent(chassisDmg.TransmissionHitZone);
                    chassisDmg.transmissionRepaired(chassisDmg.TransmissionHitZone);
                }
                if (chassisDmg.LeftTrackHitZone != null)
                {
                    RepairUtils.RepairDestructibleComponent(chassisDmg.LeftTrackHitZone);
                    chassisDmg.leftTrackRepaired(chassisDmg.LeftTrackHitZone);
                }
                if (chassisDmg.RightTrackHitZone != null)
                {
                    RepairUtils.RepairDestructibleComponent(chassisDmg.RightTrackHitZone);
                    chassisDmg.rightTrackRepaired(chassisDmg.RightTrackHitZone);
                }
                RepairUtils.SetField(chassisDmg, "<DamageStatus>k__BackingField", string.Empty);
                RepairUtils.SetField(chassisDmg, "_damageStatus", string.Empty);
                chassisDmg.updateDamageText();
            }

            RepairDrivePhysics(vehicle, go);
        }

        private static void RepairDrivePhysics(Vehicle vehicle, GameObject go)
        {
            try
            {
                RepairUtils.SetField(vehicle, "<CannotMove>k__BackingField", false);
                RepairUtils.SetField(vehicle, "_cannotMove", false);
                RepairUtils.SetField(vehicle, "<DamageStatus>k__BackingField", string.Empty);
                RepairUtils.SetField(vehicle, "_damageStatus", string.Empty);

                var chassis = vehicle.Chassis;
                VehicleController vc = null;

                if (chassis is GHPC.NwhChassis nwhChassis)
                    vc = nwhChassis.VehicleController;

                if (vc == null && chassis != null)
                    vc = RepairUtils.GetMemberValue(chassis, "VehicleController") as VehicleController;

                if (vc == null && go != null)
                    vc = go.GetComponent<VehicleController>();

                if (vc != null)
                {
                    vc.Active = true;
                    vc.active = true;
                    vc.frozen = false;
                    vc.wasFrozen = false;
                    vc.speedLimiter = 9999f;
                    vc.Activate();
                    vc.ResetInactivityTimer();

                    if (vc.damage != null)
                    {
                        vc.damage.damage = 0f;
                        vc.damage.performanceDegradation = false;
                    }

                    if (vc.engine != null)
                    {
                        vc.engine.isRunning = true;
                        vc.engine.starting = false;
                        vc.engine.stopping = false;
                        vc.engine.wasRunning = true;
                        vc.engine.fuelCutoffStart = 0f;
                        vc.engine.fuelCutoffDuration = 0f;
                        vc.engine.power = vc.engine.maxPower;
                        vc.engine.rpm = vc.engine.minRPM;
                        vc.engine.prevRpm = vc.engine.minRPM;
                        vc.engine.Start();
                    }

                    if (vc.transmission != null)
                    {
                        vc.transmission.postShiftBan = 0f;
                        vc.transmission.addedClutchRPM = 0f;
                        vc.transmission.lastShiftTime = 0f;
                        vc.transmission.clutchPedalPressedPercent = 0f;
                        vc.transmission.ShiftInto(1, false);
                    }

                    if (vc.brakes != null)
                    {
                        vc.brakes.intensity = 0f;
                        vc.brakes.airBrakePressure = 0f;
                        vc.brakes.active = true;
                    }

                    if (vc.input != null)
                    {
                        vc.input.Handbrake = 0f;
                        vc.input.Clutch = 0f;
                        vc.input.Horizontal = 0f;
                    }

                    if (vc.tracks != null)
                    {
                        vc.tracks.turnSpeedLimit = 999f;
                        vc.tracks.leftTrackVel = 0f;
                        vc.tracks.rightTrackVel = 0f;
                    }
                }

                if (go != null)
                {
                    var pidHull = go.GetComponent<PidHull>();
                    if (pidHull != null)
                    {
                        pidHull._allowInput = true;
                        pidHull._allowPid = true;
                        pidHull._braking = false;
                        pidHull._holdHeading = false;
                        pidHull._holdSpeed = false;
                        pidHull._lockedLeftTurn = false;
                        pidHull._lockedRightTurn = false;
                        pidHull._torqueMultiplier = 1f;
                        pidHull._driveForceMultiplier = 1f;
                        pidHull._slideForceMultiplier = 1f;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"RepairDrivePhysics failed: {e}");
            }
        }

        private static void RepairAmmoFeeds(GameObject go, HashSet<object> repairedInstances = null)
        {
            if (go == null) return;

            var ammoFeeds = go.GetComponentsInChildren<AmmoFeed>(true);
            if (ammoFeeds == null || ammoFeeds.Length == 0) return;

            foreach (var ammoFeed in ammoFeeds)
            {
                if (ammoFeed == null) continue;
                if (repairedInstances != null && !repairedInstances.Add(ammoFeed)) continue;
                RepairAmmoFeed(ammoFeed);
            }
        }

        private static void RepairCrewServedWeapons(GameObject go, HashSet<object> repairedInstances = null)
        {
            if (go == null) return;

            var mainGuns = go.GetComponentsInChildren<MainGun>(true);
            if (mainGuns != null)
            {
                foreach (var mainGun in mainGuns)
                {
                    if (mainGun == null) continue;
                    if (repairedInstances != null && !repairedInstances.Add(mainGun)) continue;
                    RepairMainGun(mainGun);
                }
            }

            var missileLaunchers = go.GetComponentsInChildren<MissileLauncher>(true);
            if (missileLaunchers == null) return;

            foreach (var missileLauncher in missileLaunchers)
            {
                if (missileLauncher == null) continue;
                if (repairedInstances != null && !repairedInstances.Add(missileLauncher)) continue;
                RepairMissileLauncher(missileLauncher);
            }
        }

        private static void RepairCrewMembers(Vehicle vehicle, GameObject go)
        {
            var crewManager = RepairUtils.GetCrewManager(vehicle, go);
            if (crewManager == null) return;

            string[] positions = { "Driver", "Gunner", "Loader", "Commander" };
            bool repairedAny = false;

            foreach (var posName in positions)
            {
                try
                {
                    var posValue = (CrewPosition)Enum.Parse(typeof(CrewPosition), posName);
                    IHuman human = crewManager.GetHuman(posValue);
                    ICrewBrain brain = crewManager.GetCrewBrain(posValue);
                    var crewMember = crewManager.GetCrewMember(posValue);

                    if (!ShouldRepairCrewPosition(crewMember, human, brain)) continue;

                    repairedAny = true;

                    if (human != null)
                        RepairHuman(human);

                    if (crewMember != null)
                    {
                        RepairCrewMemberRegistry(crewMember, posName);
                        crewMember.Evacuated = false;
                        RepairUtils.SetField(crewMember, "<NoBailOut>k__BackingField", false);
                    }

                    if (brain != null)
                    {
                        brain.Incapacitated = false;
                        brain.Suspended = false;
                        var weaponsModule = brain.WeaponsModule;
                        if (weaponsModule != null)
                            weaponsModule.AimLockedOutManually = false;
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning($"Failed to heal {posName}: {e}");
                }
            }

            if (!repairedAny && !crewManager._didEvac && !crewManager._someoneEvacuated && string.IsNullOrWhiteSpace(crewManager.DamageStatus))
                return;

            vehicle.UnitIncapacitated = false;
            RepairUtils.SetField(vehicle, "<UnitIncapacitated>k__BackingField", false);
            RepairUtils.SetField(vehicle, "_unitIncapacitated", false);
            crewManager.CrewArePanicked = false;
            RepairUtils.SetField(crewManager, "<DamageStatus>k__BackingField", string.Empty);
            RepairUtils.SetField(crewManager, "_damageStatus", string.Empty);
            crewManager._crewPanickedCountdown = 0f;
            crewManager._penetratedByLargeCaliberCount = 0;
            crewManager._penetratedBySmallCaliberCount = 0;
            crewManager._didEvac = false;
            crewManager._someoneEvacuated = false;

            if (go != null)
            {
                RepairAmmoFeeds(go);
                RepairCrewServedWeapons(go);
            }

            RefreshPlayerDamageUi();
            UnderdogsDebug.LogRepair("[Repair] Crew healed");
        }

        private static bool ShouldRepairCrewPosition(CrewManager.CrewMember crewMember, IHuman human, ICrewBrain brain)
        {
            if (crewMember != null)
            {
                if (crewMember.Evacuated) return true;
                if (RepairUtils.GetMemberValue(crewMember, "NoBailOut") is bool noBailOut && noBailOut) return true;
                if (RepairUtils.HasActiveDamageState(crewMember.BodyPartRegistry)) return true;
            }

            if (human != null)
            {
                if (human.IsDead) return true;
                if (human.IsStunned) return true;
                if (human.Fatigue > 0f) return true;
                var overallStatus = human.GetType().GetMethod("GetOverallStatus", BindingFlags.Instance | BindingFlags.Public)?.Invoke(human, null);
                if (overallStatus != null && !string.Equals(overallStatus.ToString(), "Fine", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (brain != null)
            {
                if (brain.Incapacitated) return true;
                if (brain.Suspended) return true;
            }

            return false;
        }

        private static void RepairHuman(IHuman human)
        {
            human.Fatigue = 0f;
            RepairUtils.SetField(human, "_stunExpireTime", 0f);

            var setPartHealthMethod = human.GetType().GetMethod("SetPartHealth", BindingFlags.Instance | BindingFlags.Public);
            var stabilizePartMethod = human.GetType().GetMethod("StabilizePart", BindingFlags.Instance | BindingFlags.Public);
            var updateStunStatusMethod = human.GetType().GetMethod("UpdateStunStatus", BindingFlags.Instance | BindingFlags.Public);
            var partNames = Enum.GetNames(typeof(BodyPart));

            foreach (var partName in partNames)
            {
                try
                {
                    var partValue = Enum.Parse(typeof(BodyPart), partName);
                    if (setPartHealthMethod != null)
                        setPartHealthMethod.Invoke(human, new object[] { partValue, 100.0f });
                    if (stabilizePartMethod != null)
                        stabilizePartMethod.Invoke(human, new object[] { partValue });
                }
                catch { }
            }

            if (updateStunStatusMethod != null)
                updateStunStatusMethod.Invoke(human, null);
        }

        private static void RepairCrewMemberRegistry(CrewManager.CrewMember crewMember, string posName)
        {
            var bodyPartRegistry = crewMember.BodyPartRegistry;
            if (bodyPartRegistry == null) return;

            if (string.IsNullOrWhiteSpace(bodyPartRegistry.DamageReportName))
                bodyPartRegistry.DamageReportName = posName;

            bodyPartRegistry._damaged = false;
            bodyPartRegistry._damageReport = string.Empty;
            RepairUtils.SetMemberValue(bodyPartRegistry, "DamageStatus", string.Empty);
        }

        private static void RepairDamageStatusSources(Vehicle vehicle, GameObject go)
        {
            try
            {
                var repairedInstances = new HashSet<object>();
                var damageStatuses = RepairUtils.GetMemberValue(vehicle, "DamageStatuses") as Array;
                if (damageStatuses != null)
                {
                    foreach (var damageSource in damageStatuses)
                    {
                        if (damageSource != null && repairedInstances.Add(damageSource))
                            RepairDamageStatusSource(damageSource);
                    }
                }

                RepairUtils.RepairDamageSourcesByType<WeaponSystem>(go, repairedInstances);
                RepairUtils.RepairDamageSourcesByType<FireControlSystem>(go, repairedInstances);
                RepairUtils.RepairDamageSourcesByType<FlammablesManager>(go, repairedInstances);
                RepairUtils.RepairDamageSourcesByType<AimablePlatform>(go, repairedInstances);
                RepairAmmoFeeds(go, repairedInstances);
                RepairCrewServedWeapons(go, repairedInstances);
                RepairUtils.RepairDamageSourcesByType<CameraSlot>(go, repairedInstances);
                RepairUtils.RepairDamageSourcesByType<MissileGuidanceUnit>(go, repairedInstances);
                RepairUtils.RepairDamageSourcesByType<LightBandExclusiveItem>(go, repairedInstances);
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"RepairDamageStatusSources failed: {e}");
            }
        }

        private static void RepairDamageStatusSource(object damageSource)
        {
            if (damageSource == null || !RepairUtils.HasActiveDamageState(damageSource)) return;

            switch (damageSource)
            {
                case WeaponSystem ws:
                    RepairWeaponSystem(ws);
                    break;
                case FireControlSystem fcs:
                    RepairFireControlSystem(fcs);
                    break;
                case FlammablesManager fm:
                    RepairFlammablesManager(fm);
                    break;
                case AimablePlatform ap:
                    RepairAimablePlatform(ap);
                    break;
                case AmmoFeed af:
                    RepairAmmoFeed(af);
                    break;
                case CameraSlot cs:
                    RepairCameraSlot(cs);
                    break;
                case MissileGuidanceUnit mgu:
                    RepairMissileGuidanceUnit(mgu);
                    break;
                case LightBandExclusiveItem lbei:
                    RepairLightBandExclusiveItem(lbei);
                    break;
                default:
                    RepairUtils.ClearDamageSourceState(damageSource);
                    break;
            }
        }

        private static void RepairWeaponSystem(WeaponSystem ws)
        {
            if (ws == null) return;
            RepairUtils.ClearDamageSourceState(ws);
            ws._barrelDamaged = false;
            ws._breechDamaged = false;
            ws._guidanceDamaged = false;
            RepairUtils.SetMemberValue(ws, "_singleBarrelDamaged", false);
            RepairUtils.RepairDestructibleComponent(ws.BarrelHitZone);
            RepairUtils.RepairDestructibleComponent(ws.BreechHitZone);
            if (ws.Feed != null && RepairUtils.HasActiveDamageState(ws.Feed))
                RepairAmmoFeed(ws.Feed);
        }

        private static void RepairFireControlSystem(FireControlSystem fcs)
        {
            if (fcs == null) return;
            fcs._laserDestroyed = false;
            fcs._computerDestroyed = false;
            fcs._powerLost = false;
            RepairUtils.SetMemberValue(fcs, "_computerInop", false);
            fcs._laseQueued = false;
            fcs.RangeInvalid = false;
            fcs.ReportedRange = 0f;
            RepairUtils.RepairDestructibleComponent(fcs.LaserComponent);
            RepairUtils.RepairDestructibleComponent(fcs.ComputerComponent);
            RepairUtils.ClearDamageSourceState(fcs);
        }

        private static void RepairAmmoFeed(AmmoFeed af)
        {
            if (af == null) return;
            RepairUtils.ClearDamageSourceState(af);
            af._loaderIncapacitated = false;
            af._autoloaderDestroyed = false;
            RepairUtils.RepairDestructibleComponent(af.Autoloader);
            af.ForcePauseReload = false;
            af.WaitingOnMissile = false;
            af.Reloading = false;
            af.Cycling = false;

            RepairUtils.SetField(af, "_clipFeedTime", 0f);
            RepairUtils.SetField(af, "_roundFeedTime", 0f);
            RepairUtils.SetField(af, "_clipFeedStage", 0);
            RepairUtils.SetField(af, "_roundFeedStage", 0);

            var roundCycleStages = RepairUtils.GetMemberValue(af, "RoundCycleStages");
            if (roundCycleStages != null)
                RepairUtils.InvokeMethod(af, "CalculateTotalTime", roundCycleStages, true);
            else
                RepairUtils.SetField(af, "_totalCycleTime", 0f);

            var clipReloadStages = RepairUtils.GetMemberValue(af, "ClipReloadStages");
            if (clipReloadStages != null)
                RepairUtils.InvokeMethod(af, "CalculateTotalTime", clipReloadStages, false);
            else
                RepairUtils.SetField(af, "_totalReloadTime", 0f);
        }

        private static void RepairMainGun(MainGun gun)
        {
            if (gun == null) return;

            var loaderDamaged = gun._loaderDamaged;
            if (loaderDamaged)
            {
                gun.MinReloadTime = Mathf.Max(0f, gun.MinReloadTime - 10f);
                gun.MaxReloadTime = Mathf.Max(0f, gun.MaxReloadTime - 10f);
                RepairUtils.SetField(gun, "_reloadTimeRemaining", Mathf.Max(0f, gun.ReloadTimeRemaining - 10f));
                if (RepairUtils.GetMemberValue(gun, "_currentReloadTime") is float currentReloadTime)
                    RepairUtils.SetField(gun, "_currentReloadTime", Mathf.Max(0f, currentReloadTime - 10f));
            }

            gun._loaderDamaged = false;
        }

        private static void RepairMissileLauncher(MissileLauncher launcher)
        {
            if (launcher == null) return;

            var loaderDamaged = launcher._loaderDamaged;
            if (loaderDamaged)
            {
                launcher.MinReloadTime = Mathf.Max(0f, launcher.MinReloadTime - 10f);
                launcher.MaxReloadTime = Mathf.Max(0f, launcher.MaxReloadTime - 10f);
                RepairUtils.SetField(launcher, "_reloadTimeRemaining", Mathf.Max(0f, launcher.ReloadTimeRemaining - 10f));
                if (RepairUtils.GetMemberValue(launcher, "_currentReloadTime") is float currentReloadTime)
                    RepairUtils.SetField(launcher, "_currentReloadTime", Mathf.Max(0f, currentReloadTime - 10f));
            }

            launcher._loaderDamaged = false;
        }

        private static void RepairCameraSlot(CameraSlot cs)
        {
            if (cs == null) return;
            RepairUtils.ClearDamageSourceState(cs);
            cs.IsDetached = false;
            cs._isUsableByDamage = true;
            cs._isUsableByWeapon = true;
            cs._wasAvailable = true;
            RepairUtils.RepairDestructibleComponent(cs.HitZone);
            RepairUtils.InvokeMethod(cs, "SetUsableByWeapon", true);
            var activeNow = RepairUtils.GetMemberValue(cs, "IsActive") is bool isActive && isActive;
            RepairUtils.InvokeMethod(cs, "UpdateCameraVisuals", activeNow);
        }

        private static void RepairMissileGuidanceUnit(MissileGuidanceUnit mgu)
        {
            if (mgu == null) return;
            RepairUtils.ClearDamageSourceState(mgu);
            RepairUtils.RepairDestructibleComponent(mgu.GuidanceComponent);
        }

        private static void RepairLightBandExclusiveItem(LightBandExclusiveItem lbei)
        {
            if (lbei == null) return;
            var destructible = lbei.Destructible;
            if (destructible == null && !lbei._destroyed) return;
            RepairUtils.ClearDamageSourceState(lbei);
            RepairUtils.RepairDestructibleComponent(destructible);
            if (destructible != null)
                RepairUtils.InvokeMethod(lbei, "Destructible_Repaired", destructible);
        }

        private static void RepairAimablePlatform(AimablePlatform ap)
        {
            if (ap == null) return;
            RepairUtils.ClearDamageSourceState(ap);
            ap._destroyed = false;
            ap.IsDetached = false;
            ap.PowerEnabled = true;
            ap._aimingEnabled = true;
        }

        private static void RepairFlammablesManager(FlammablesManager fm)
        {
            if (fm == null) return;
            if (fm.Compartments != null)
            {
                foreach (var compartment in fm.Compartments)
                {
                    if (compartment == null || compartment.Clusters == null) continue;
                    foreach (var cluster in compartment.Clusters)
                    {
                        if (cluster == null || cluster.Items == null) continue;
                        foreach (var item in cluster.Items)
                        {
                            if (item == null) continue;
                            item.Extinguish(true);
                            item.SilentExtinguish(true);
                        }
                    }
                }
            }
            RepairUtils.ClearDamageSourceState(fm);
            fm._unsecuredFirePresent = false;
            fm._totalUnsecuredBurnTime = 0f;
            fm._killedPower = false;
            fm._smokeColumnPlaying = false;
            fm._shutDown = false;
            fm.CurrentScorchRatio = 0f;
        }

        private static void ClearDamageStatusState(Vehicle vehicle, GameObject go)
        {
            if (vehicle == null) return;

            RepairUtils.SetMemberValue(vehicle, "DamageStatus", string.Empty);
            RepairUtils.SetField(vehicle, "_damageStatus", string.Empty);
            RepairUtils.SetField(vehicle, "<DamageStatus>k__BackingField", string.Empty);
            RepairUtils.SetMemberValue(vehicle, "StatusMessages", string.Empty);
            RepairUtils.SetField(vehicle, "_statusMessage", string.Empty);
            RepairUtils.SetField(vehicle, "_statusMessages", string.Empty);
            RepairUtils.SetField(vehicle, "<Neutralized>k__BackingField", false);
            RepairUtils.SetField(vehicle, "_neutralized", false);
            RepairUtils.SetField(vehicle, "<UnitIncapacitated>k__BackingField", false);
            RepairUtils.SetField(vehicle, "_unitIncapacitated", false);
            RepairUtils.SetField(vehicle, "_immobilized", false);
            RepairUtils.SetMemberValue(vehicle, "IsImmobilized", false);
            RepairUtils.SetField(vehicle, "_isParked", false);
            RepairUtils.SetMemberValue(vehicle, "Parked", false);
            RepairUtils.SetField(vehicle, "_driverInjured", false);
            RepairUtils.SetField(vehicle, "_gunnerInjured", false);
            RepairUtils.SetField(vehicle, "_loaderInjured", false);
            RepairUtils.SetField(vehicle, "_commanderInjured", false);

            var vehicleInfo = RepairUtils.GetVehicleInfo(vehicle);
            if (vehicleInfo != null)
            {
                RepairUtils.SetMemberValue(vehicleInfo, "DamageStatus", string.Empty);
                RepairUtils.SetField(vehicleInfo, "_damageStatus", string.Empty);
                RepairUtils.SetMemberValue(vehicleInfo, "StatusMessages", string.Empty);
                RepairUtils.SetField(vehicleInfo, "_statusMessage", string.Empty);
                RepairUtils.SetField(vehicleInfo, "_statusMessages", string.Empty);
                RepairUtils.SetField(vehicleInfo, "<Neutralized>k__BackingField", false);
                RepairUtils.SetField(vehicleInfo, "_neutralized", false);
                RepairUtils.SetField(vehicleInfo, "<UnitIncapacitated>k__BackingField", false);
                RepairUtils.SetField(vehicleInfo, "_unitIncapacitated", false);
                RepairUtils.SetField(vehicleInfo, "_immobilized", false);
                RepairUtils.SetMemberValue(vehicleInfo, "IsImmobilized", false);
                RepairUtils.SetField(vehicleInfo, "_isParked", false);
                RepairUtils.SetMemberValue(vehicleInfo, "Parked", false);
                RepairUtils.SetField(vehicleInfo, "_driverInjured", false);
                RepairUtils.SetField(vehicleInfo, "_gunnerInjured", false);
                RepairUtils.SetField(vehicleInfo, "_loaderInjured", false);
                RepairUtils.SetField(vehicleInfo, "_commanderInjured", false);
            }

            var drivableChassis = RepairUtils.GetDrivableChassis(vehicle, go) as DrivableChassis;
            if (drivableChassis != null)
            {
                drivableChassis._canMove = true;
                drivableChassis._disabledEngine = false;
                drivableChassis._killedEngine = false;
                drivableChassis._damagedDrivetrain = false;
                drivableChassis._killedTracks = false;
                drivableChassis._statusMessage = string.Empty;
                drivableChassis._isParked = false;
                drivableChassis.EngineOn = true;
                drivableChassis.SetEngineOn(true);
                drivableChassis.SetIsParked(false);
            }

            var crewManager = RepairUtils.GetCrewManager(vehicle, go) as CrewManager;
            if (crewManager != null)
            {
                crewManager._didEvac = false;
                crewManager._someoneEvacuated = false;
            }
        }

        private static void RefreshPlayerDamageUi()
        {
            try
            {
                var playerInput = PlayerInput.Instance;
                if (playerInput != null)
                {
                    var currentDamageStatus = RepairUtils.GetMemberValue(playerInput, "CurrentPlayerDamageStatus");
                    if (currentDamageStatus != null)
                    {
                        RepairUtils.SetMemberValue(currentDamageStatus, "DamageStatus", string.Empty);
                        RepairUtils.SetField(currentDamageStatus, "_damageStatus", string.Empty);
                    }

                    RepairUtils.SetMemberValue(playerInput, "CurrentPlayerDamageStatus", null);
                    RepairUtils.InvokeParameterless(playerInput, "SetupUnitReferences");
                    RepairUtils.InvokeParameterless(playerInput, "DoPlayerUnitSetup");

                    var refreshedDamageStatus = RepairUtils.GetMemberValue(playerInput, "CurrentPlayerDamageStatus");
                    if (refreshedDamageStatus != null)
                    {
                        RepairUtils.SetMemberValue(refreshedDamageStatus, "DamageStatus", string.Empty);
                        RepairUtils.SetField(refreshedDamageStatus, "_damageStatus", string.Empty);
                    }
                }

                var damageStatusHudType = Type.GetType("GHPC.UI.Hud.DamageStatusHud, Assembly-CSharp");
                if (damageStatusHudType != null)
                {
                    var damageStatusHud = UnityEngine.Object.FindObjectOfType(damageStatusHudType);
                    if (damageStatusHud != null)
                    {
                        RepairUtils.SetField(damageStatusHud, "timeLeft", 0f);
                        var hudText = RepairUtils.GetMemberValue(damageStatusHud, "hudText");
                        var textProp = hudText?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                        if (textProp != null && textProp.CanWrite)
                            textProp.SetValue(hudText, string.Empty);
                        RepairUtils.InvokeParameterless(damageStatusHud, "Update");
                    }
                }
            }
            catch { }
        }

        private static void ClearCompassMarkers()
        {
            try
            {
                var compassType = Type.GetType("GHPC.UI.Hud.Compass, Assembly-CSharp");
                if (compassType != null)
                {
                    var compass = UnityEngine.Object.FindObjectOfType(compassType);
                    if (compass != null)
                    {
                        var pointMapField = compassType.GetField("_compassPointMap", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (pointMapField != null)
                        {
                            var pointMap = pointMapField.GetValue(compass);
                            if (pointMap != null)
                            {
                                var valuesProperty = pointMap.GetType().GetProperty("Values");
                                if (valuesProperty != null)
                                {
                                    var values = valuesProperty.GetValue(pointMap);
                                    if (values != null)
                                    {
                                        foreach (var image in (System.Collections.IEnumerable)values)
                                        {
                                            if (image != null)
                                            {
                                                var imageGo = RepairUtils.GetMemberValue(image, "gameObject") as GameObject;
                                                if (imageGo != null)
                                                {
                                                    UnityEngine.Object.Destroy(imageGo);
                                                }
                                            }
                                        }
                                    }
                                }

                                var clearMethod = pointMap.GetType().GetMethod("Clear");
                                clearMethod?.Invoke(pointMap, null);
                            }
                        }

                        var pairsField = compassType.GetField("_compassPairs", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (pairsField != null)
                        {
                            var pairs = pairsField.GetValue(compass);
                            if (pairs != null)
                            {
                                var clearMethod = pairs.GetType().GetMethod("Clear");
                                clearMethod?.Invoke(pairs, null);
                            }
                        }

                        RepairUtils.InvokeParameterless(compass, "ClearCompassMarkers");
                    }
                }

                var compassControllerType = Type.GetType("GHPC.UI.Hud.CompassController, Assembly-CSharp");
                if (compassControllerType != null)
                {
                    var compassController = UnityEngine.Object.FindObjectOfType(compassControllerType);
                    if (compassController != null)
                    {
                        var targetsField = compassControllerType.GetField("_targets", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (targetsField != null)
                        {
                            var targets = targetsField.GetValue(compassController);
                            if (targets != null)
                            {
                                var clearMethod = targets.GetType().GetMethod("Clear");
                                clearMethod?.Invoke(targets, null);
                            }
                        }

                        var targetMarkerField = compassControllerType.GetField("_targetCompassMarker", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (targetMarkerField != null)
                        {
                            var targetMarker = targetMarkerField.GetValue(compassController);
                            if (targetMarker != null)
                            {
                                var markerGo = RepairUtils.GetMemberValue(targetMarker, "gameObject") as GameObject;
                                if (markerGo != null)
                                {
                                    markerGo.SetActive(false);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"ClearCompassMarkers failed: {e}");
            }
        }
    }
}
