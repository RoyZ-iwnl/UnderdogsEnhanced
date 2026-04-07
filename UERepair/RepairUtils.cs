using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using GHPC;
using GHPC.Crew;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Equipment;
using GHPC.Effects;
using GHPC.Camera;

namespace UnderdogsEnhanced
{
    /// <summary>
    /// Repair 功能辅助工具类
    /// </summary>
    public static class RepairUtils
    {
        #region Field/Property/Method Reflection Helpers

        public static void SetField(object obj, string fieldName, object value)
        {
            try
            {
                var field = FindField(obj.GetType(), fieldName);
                if (field != null)
                    field.SetValue(obj, value);
            }
            catch { }
        }

        public static FieldInfo FindField(Type type, string fieldName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            while (type != null)
            {
                var field = type.GetField(fieldName, flags);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        public static PropertyInfo FindProperty(Type type, string propertyName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            while (type != null)
            {
                var property = type.GetProperty(propertyName, flags);
                if (property != null) return property;
                type = type.BaseType;
            }
            return null;
        }

        public static MethodInfo FindMethod(Type type, string methodName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            while (type != null)
            {
                var method = type.GetMethod(methodName, flags, null, Type.EmptyTypes, null);
                if (method != null) return method;
                type = type.BaseType;
            }
            return null;
        }

        public static bool HasMember(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return false;
            var type = obj.GetType();
            return FindProperty(type, memberName) != null
                || FindField(type, memberName) != null
                || FindField(type, $"<{memberName}>k__BackingField") != null;
        }

        public static object GetMemberValue(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return null;

            try
            {
                var type = obj.GetType();
                var prop = FindProperty(type, memberName);
                if (prop != null)
                    return prop.GetValue(obj);

                var field = FindField(type, memberName);
                if (field != null)
                    return field.GetValue(obj);

                var backingField = FindField(type, $"<{memberName}>k__BackingField");
                if (backingField != null)
                    return backingField.GetValue(obj);
            }
            catch { }

            return null;
        }

        public static bool SetMemberValue(object obj, string memberName, object value)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return false;

            try
            {
                var type = obj.GetType();
                var prop = FindProperty(type, memberName);
                if (prop != null)
                {
                    if (prop.CanWrite)
                    {
                        prop.SetValue(obj, value);
                        return true;
                    }
                    var setter = prop.GetSetMethod(true);
                    if (setter != null)
                    {
                        setter.Invoke(obj, new object[] { value });
                        return true;
                    }
                }

                var backingField = FindField(type, $"<{memberName}>k__BackingField");
                if (backingField != null)
                {
                    backingField.SetValue(obj, value);
                    return true;
                }

                var field = FindField(type, memberName);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static bool InvokeParameterless(object obj, string methodName)
        {
            if (obj == null || string.IsNullOrEmpty(methodName)) return false;

            try
            {
                var method = FindMethod(obj.GetType(), methodName);
                if (method != null)
                {
                    method.Invoke(obj, null);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static bool InvokeMethod(object obj, string methodName, params object[] args)
        {
            if (obj == null || string.IsNullOrEmpty(methodName)) return false;

            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo method = null;

                if (args == null || args.Length == 0)
                {
                    method = obj.GetType().GetMethod(methodName, flags, null, Type.EmptyTypes, null);
                }
                else
                {
                    var argTypes = new Type[args.Length];
                    for (int i = 0; i < args.Length; i++)
                        argTypes[i] = args[i]?.GetType() ?? typeof(object);

                    method = obj.GetType().GetMethod(methodName, flags, null, argTypes, null);
                    if (method == null)
                    {
                        var methods = obj.GetType().GetMethods(flags);
                        foreach (var candidate in methods)
                        {
                            if (candidate.Name == methodName && candidate.GetParameters().Length == args.Length)
                            {
                                method = candidate;
                                break;
                            }
                        }
                    }
                }

                if (method != null)
                {
                    method.Invoke(obj, args);
                    return true;
                }
            }
            catch { }

            return false;
        }

        #endregion

        #region GameObject/Component Helpers

        public static GameObject GetGameObject(object obj)
        {
            if (obj == null) return null;

            var go = GetMemberValue(obj, "gameObject") as GameObject ?? GetMemberValue(obj, "GameObject") as GameObject;
            if (go != null) return go;

            var transform = GetMemberValue(obj, "transform") as Transform ?? GetMemberValue(obj, "Transform") as Transform;
            if (transform != null) return transform.gameObject;

            var baseUnit = GetMemberValue(obj, "BaseUnit");
            if (baseUnit != null && !ReferenceEquals(baseUnit, obj))
            {
                go = GetGameObject(baseUnit);
                if (go != null) return go;
            }

            var unitInfoBroker = GetMemberValue(obj, "UnitInfoBroker") ?? GetMemberValue(obj, "_unitInfoBroker");
            if (unitInfoBroker != null && !ReferenceEquals(unitInfoBroker, obj))
            {
                go = GetGameObject(unitInfoBroker);
                if (go != null) return go;
            }

            return null;
        }

        public static object GetComponentAnywhere(GameObject go, Type componentType)
        {
            if (go == null || componentType == null) return null;

            var component = go.GetComponent(componentType);
            if (component != null) return component;

            var parentComponents = go.GetComponentsInParent(componentType, true);
            if (parentComponents != null && parentComponents.Length > 0)
                return parentComponents[0];

            component = go.GetComponentInChildren(componentType, true);
            if (component != null) return component;

            var components = go.GetComponentsInChildren(componentType, true);
            if (components != null && components.Length > 0)
                return components[0];

            return null;
        }

        public static object GetDrivableChassis(object vehicle, GameObject go)
        {
            var directHull = GetMemberValue(vehicle, "Hull") ?? GetMemberValue(vehicle, "_hull");
            if (directHull is DrivableChassis dc) return dc;

            var vehicleInfo = GetVehicleInfo(vehicle);
            var vehicleInfoHull = vehicleInfo != null ? (GetMemberValue(vehicleInfo, "Hull") ?? GetMemberValue(vehicleInfo, "_hull")) : null;
            if (vehicleInfoHull is DrivableChassis dc2) return dc2;

            return go != null ? go.GetComponentInChildren<DrivableChassis>(true) : null;
        }

        #endregion

        #region VehicleInfo Helpers

        public static VehicleInfo GetPlayerVehicleInfo(object playerUnit)
        {
            var localVehicleInfo = GetVehicleInfo(playerUnit) as VehicleInfo;
            if (localVehicleInfo != null) return localVehicleInfo;

            try
            {
                if (WorldScript.PlayerVehicle != null)
                    return WorldScript.PlayerVehicle;
            }
            catch { }

            return null;
        }

        public static object GetVehicleInfo(object playerUnit)
        {
            try
            {
                var vehicleInfo = GetMemberValue(playerUnit, "VehicleInfo")
                    ?? GetMemberValue(playerUnit, "Info")
                    ?? GetMemberValue(playerUnit, "_info");
                if (vehicleInfo != null) return vehicleInfo;

                var broker = GetMemberValue(playerUnit, "UnitInfoBroker")
                    ?? GetMemberValue(playerUnit, "_unitInfoBroker");
                if (broker != null)
                {
                    vehicleInfo = GetMemberValue(broker, "VehicleInfo")
                        ?? GetMemberValue(broker, "_vehicleInfo");
                    if (vehicleInfo != null) return vehicleInfo;
                }

                var go = GetGameObject(playerUnit);
                var vehicleInfoType = Type.GetType("GHPC.Vehicle.VehicleInfo, Assembly-CSharp");
                if (vehicleInfoType != null && go != null)
                {
                    return GetComponentAnywhere(go, vehicleInfoType);
                }
            }
            catch { }

            return null;
        }

        #endregion

        #region Crew Helpers

        public static CrewManager GetCrewManager(Vehicle vehicle, GameObject go)
        {
            var crewManager = vehicle.CrewManager;
            if (crewManager != null) return crewManager;

            var broker = GetMemberValue(vehicle, "UnitInfoBroker") ?? GetMemberValue(vehicle, "_unitInfoBroker");
            if (broker != null)
            {
                crewManager = (GetMemberValue(broker, "CrewManager") ?? GetMemberValue(broker, "_crewManager")) as CrewManager;
                if (crewManager != null) return crewManager;
            }

            if (go != null)
            {
                var unitInfoBrokerType = Type.GetType("GHPC.UnitInfoBroker, Assembly-CSharp");
                if (unitInfoBrokerType != null)
                {
                    broker = GetComponentAnywhere(go, unitInfoBrokerType);
                    if (broker != null)
                        return (GetMemberValue(broker, "CrewManager") ?? GetMemberValue(broker, "_crewManager")) as CrewManager;
                }
            }

            return null;
        }

        #endregion

        #region Damage State Helpers

        public static bool IsObjectMarkedDestroyed(object obj)
        {
            if (obj == null) return true;

            if (obj is GHPC.Unit unit) return unit.Destroyed;
            if (obj is Vehicle vehicle) return vehicle.Destroyed;

            var t = obj.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var name in new[] { "IsDestroyed", "Destroyed", "IsDead", "Dead", "Killed" })
            {
                try
                {
                    var prop = t.GetProperty(name, flags);
                    if (prop != null && prop.PropertyType == typeof(bool) && (bool)prop.GetValue(obj, null))
                        return true;
                    var field = t.GetField(name, flags);
                    if (field != null && field.FieldType == typeof(bool) && (bool)field.GetValue(obj))
                        return true;
                }
                catch { }
            }

            return false;
        }

        public static bool HasActiveDamageState(object damageSource)
        {
            if (damageSource == null) return false;

            switch (damageSource)
            {
                case AmmoFeed af:
                    return af._loaderIncapacitated || af._autoloaderDestroyed || IsDestructibleDamaged(af.Autoloader);
                case CameraSlot cs:
                    return cs.Damaged || cs.IsDetached || IsDestructibleDamaged(cs.HitZone);
                case MissileGuidanceUnit mgu:
                    return mgu.Damaged || mgu.GuidanceComponent == null || IsDestructibleDamaged(mgu.GuidanceComponent);
                case LightBandExclusiveItem lbei:
                    return lbei._destroyed || IsDestructibleDamaged(lbei.Destructible);
                case WeaponSystem ws:
                    return ws.Damaged || ws._barrelDamaged || ws._breechDamaged || ws._guidanceDamaged
                        || IsDestructibleDamaged(ws.BarrelHitZone) || IsDestructibleDamaged(ws.BreechHitZone);
                case FireControlSystem fcs:
                    return fcs.Damaged || fcs._powerLost || fcs._laserDestroyed || fcs._computerDestroyed
                        || IsDestructibleDamaged(fcs.LaserComponent) || IsDestructibleDamaged(fcs.ComputerComponent);
                case FlammablesManager fm:
                    return fm.Damaged || fm.FirePresent || fm.UnsecuredFirePresent;
                case AimablePlatform ap:
                    return ap.Damaged || ap._destroyed;
                case ChassisDamageManager cdm:
                    return cdm.Damaged || cdm._engineDestroyed || cdm._engineDamaged || cdm._transmissionDestroyed
                        || cdm._transmissionDamaged || cdm._leftTrackDestroyed || cdm._rightTrackDestroyed || cdm._radiatorDestroyed;
                case CrewManager.CrewMember cm:
                    return cm.Evacuated || HasActiveDamageState(cm.BodyPartRegistry);
                case BodyPartComponentRegistry bpr:
                    return bpr._damaged || !string.IsNullOrWhiteSpace(bpr.DamageStatus);
            }

            if (GetMemberValue(damageSource, "Damaged") is bool damaged && damaged) return true;
            var damageStatus = GetMemberValue(damageSource, "DamageStatus") as string;
            if (!string.IsNullOrWhiteSpace(damageStatus)) return true;

            if (IsDestructibleDamaged(GetMemberValue(damageSource, "HitZone"))
                || IsDestructibleDamaged(GetMemberValue(damageSource, "Destructible"))
                || IsDestructibleDamaged(GetMemberValue(damageSource, "GuidanceComponent"))
                || IsDestructibleDamaged(GetMemberValue(damageSource, "Autoloader")))
                return true;

            if (GetMemberValue(damageSource, "_loaderIncapacitated") is bool loaderIncap && loaderIncap) return true;
            if (GetMemberValue(damageSource, "_autoloaderDestroyed") is bool autoDestroyed && autoDestroyed) return true;
            if (GetMemberValue(damageSource, "_guidanceDamaged") is bool guidanceDamaged && guidanceDamaged) return true;
            if (GetMemberValue(damageSource, "Evacuated") is bool evacuated && evacuated) return true;
            if (GetMemberValue(damageSource, "PowerEnabled") is bool powerEnabled && !powerEnabled) return true;

            return false;
        }

        public static bool IsDestructibleDamaged(object destructibleComponent)
        {
            if (destructibleComponent == null) return false;

            if (TryGetSingle(GetMemberValue(destructibleComponent, "HealthPercent"), out var healthPercent)
                && healthPercent >= 0f && healthPercent < 0.999f)
                return true;

            if (TryGetSingle(GetMemberValue(destructibleComponent, "Health"), out var health))
            {
                if (TryGetSingle(GetMemberValue(destructibleComponent, "_fullHealth"), out var fullHealth) && fullHealth > 0f)
                    return health + 0.001f < fullHealth;
                return health <= 0f;
            }

            return false;
        }

        public static void RepairDestructibleComponent(object destructibleComponent, float? health = null)
        {
            if (destructibleComponent == null) return;

            float targetHealth = health ?? 100.0f;
            if (!health.HasValue)
            {
                if (TryGetSingle(GetMemberValue(destructibleComponent, "_fullHealth"), out var fullHealth) && fullHealth > 0f)
                    targetHealth = fullHealth;
            }

            SetMemberValue(destructibleComponent, "Health", targetHealth);
        }

        public static bool TryGetSingle(object value, out float result)
        {
            switch (value)
            {
                case float single:
                    result = single;
                    return true;
                case double doubleValue:
                    result = (float)doubleValue;
                    return true;
                case int intValue:
                    result = intValue;
                    return true;
                case long longValue:
                    result = longValue;
                    return true;
                default:
                    result = 0f;
                    return false;
            }
        }

        public static void ClearDamageSourceState(object damageSource)
        {
            SetMemberValue(damageSource, "Damaged", false);
            SetField(damageSource, "<Damaged>k__BackingField", false);
            SetField(damageSource, "_damaged", false);
            SetField(damageSource, "_destroyed", false);
            SetMemberValue(damageSource, "DamageStatus", string.Empty);
            SetField(damageSource, "_damageStatus", string.Empty);
            SetField(damageSource, "<DamageStatus>k__BackingField", string.Empty);
            SetField(damageSource, "_statusMessage", string.Empty);
            SetField(damageSource, "_damageReport", string.Empty);
            InvokeParameterless(damageSource, "updateDamageText");
            InvokeParameterless(damageSource, "UpdateDamageText");
        }

        public static void RepairDamageSourcesByType<T>(GameObject go, HashSet<object> repairedInstances) where T : class
        {
            if (go == null) return;

            var components = go.GetComponentsInChildren<T>(true);
            if (components != null)
            {
                foreach (var component in components)
                {
                    if (component != null && HasActiveDamageState(component) && repairedInstances.Add(component))
                        ClearDamageSourceState(component);
                }
            }

            components = go.GetComponentsInParent<T>(true);
            if (components != null)
            {
                foreach (var component in components)
                {
                    if (component != null && HasActiveDamageState(component) && repairedInstances.Add(component))
                        ClearDamageSourceState(component);
                }
            }
        }

        #endregion
    }
}