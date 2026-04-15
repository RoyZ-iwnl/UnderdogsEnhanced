using GHPC.Vehicle;
using GHPC.Weapons;
using UnityEngine;

namespace UnderdogsEnhanced
{
    /// <summary>
    /// Top-attack tracker for Spike missiles.
    /// Uses a simplified approach similar to 50mm-Bradley: guide missile to position above target then dive.
    /// </summary>
    internal sealed class SpikeTopAttackTracker : MonoBehaviour
    {
        internal const float MinTopAttackDistance = 650f;
        internal const float MaxTopAttackDistance = 5000f;
        internal const float MinTopAttackHeight = 90f;
        internal const float MaxTopAttackHeight = 180f;
        internal const float MinClimbDuration = 0.7f;
        internal const float DesiredDiveAngleDeg = 52.5f;
        internal const float MinDiveAngleDeg = 45f;
        internal const float DiveTriggerDistance = 90f;

        private LiveRound missile;
        private Vehicle lockedTarget;
        private Transform lockedAnchor;
        private Renderer lockedRenderer;
        private Vector3 explicitAimPoint;
        private bool hasExplicitAimPoint;
        private float minDistance = float.MaxValue;
        private bool passedClosestPoint = false;
        private float calculatedAttackHeight;
        private bool diveCommitted;
        private bool profileActive;
        private Vector3 currentAimPoint;
        private bool hasCurrentAimPoint;
        private float launchTime;

        internal Vehicle LockedTarget => lockedTarget;
        internal bool MissedTarget => passedClosestPoint;
        internal bool DiveCommitted => diveCommitted;
        internal bool ProfileActive => profileActive;

        internal bool TryGetCurrentAimPoint(out Vector3 aimPoint)
        {
            aimPoint = currentAimPoint;
            return hasCurrentAimPoint;
        }

        internal static bool SupportsTopAttack(float initialDistance)
        {
            return initialDistance >= MinTopAttackDistance;
        }

        internal static float CalculateAttackHeight(float initialDistance)
        {
            float t = Mathf.InverseLerp(MinTopAttackDistance, MaxTopAttackDistance, initialDistance);
            return Mathf.Lerp(MinTopAttackHeight, MaxTopAttackHeight, t);
        }

        internal static Vector3 BuildClimbAimPoint(Vector3 targetCenter, float attackHeight)
        {
            return new Vector3(targetCenter.x, targetCenter.y + attackHeight, targetCenter.z);
        }

        internal static bool ShouldStartDive(Vector3 missilePosition, Vector3 targetCenter, float attackHeight, float currentDistance, float timeSinceLaunch)
        {
            if (timeSinceLaunch < MinClimbDuration)
                return false;

            if (currentDistance <= DiveTriggerDistance)
                return true;

            float altitudeAboveTarget = missilePosition.y - targetCenter.y;
            if (altitudeAboveTarget <= attackHeight * 0.7f)
                return false;

            Vector2 missileHorizontal = new Vector2(missilePosition.x, missilePosition.z);
            Vector2 targetHorizontal = new Vector2(targetCenter.x, targetCenter.z);
            float horizontalDistance = Vector2.Distance(missileHorizontal, targetHorizontal);
            if (horizontalDistance <= DiveTriggerDistance)
                return true;

            float currentDiveAngleDeg = Mathf.Atan2(Mathf.Max(altitudeAboveTarget, 0.1f), Mathf.Max(horizontalDistance, 0.1f)) * Mathf.Rad2Deg;
            return currentDiveAngleDeg >= DesiredDiveAngleDeg;
        }

        internal static Vector3 GetGuidanceAimPoint(Vector3 missilePosition, Vector3 targetCenter, float attackHeight, float currentDistance)
        {
            return GetGuidanceAimPoint(missilePosition, targetCenter, attackHeight, currentDistance, MinClimbDuration);
        }

        internal static Vector3 GetGuidanceAimPoint(Vector3 missilePosition, Vector3 targetCenter, float attackHeight, float currentDistance, float timeSinceLaunch)
        {
            return ShouldStartDive(missilePosition, targetCenter, attackHeight, currentDistance, timeSinceLaunch)
                ? targetCenter
                : BuildClimbAimPoint(targetCenter, attackHeight);
        }

        internal void SetInitialLock(Vehicle target, Transform anchor)
        {
            lockedTarget = target;
            lockedAnchor = anchor;
            ResetAttackState();
            CalculateAttackParameters();
        }

        internal void UpdateLockSolution(Vehicle target, Transform anchor, Renderer renderer, Vector3 aimPoint)
        {
            if (target != lockedTarget)
                ResetAttackState();

            lockedTarget = target;
            lockedAnchor = anchor;
            lockedRenderer = renderer;
            explicitAimPoint = aimPoint;
            hasExplicitAimPoint = true;

            CalculateAttackParameters();
        }

        private void ResetAttackState()
        {
            minDistance = float.MaxValue;
            passedClosestPoint = false;
            diveCommitted = false;
            profileActive = false;
            currentAimPoint = Vector3.zero;
            hasCurrentAimPoint = false;
        }

        private void Start()
        {
            missile = GetComponent<LiveRound>();
            launchTime = Time.time;
            CalculateAttackParameters();
        }

        private void CalculateAttackParameters()
        {
            if (lockedTarget == null || missile == null)
                return;

            float initialDistance = Vector3.Distance(missile.transform.position, lockedTarget.transform.position);
            profileActive = SupportsTopAttack(initialDistance);
            calculatedAttackHeight = CalculateAttackHeight(initialDistance);
        }

        private Vector3 GetTargetCenter()
        {
            if (hasExplicitAimPoint)
                return explicitAimPoint;

            if (lockedRenderer != null)
                return lockedRenderer.bounds.center;

            if (lockedAnchor != null)
            {
                Renderer anchorRenderer = lockedAnchor.GetComponent<Renderer>()
                    ?? lockedAnchor.GetComponentInChildren<Renderer>(true);
                if (anchorRenderer != null)
                    return anchorRenderer.bounds.center;

                Collider anchorCollider = lockedAnchor.GetComponent<Collider>()
                    ?? lockedAnchor.GetComponentInChildren<Collider>(true);
                if (anchorCollider != null)
                    return anchorCollider.bounds.center;

                return lockedAnchor.position;
            }

            if (lockedTarget != null)
            {
                Transform tracking = lockedTarget.transform.Find("TRACKING OBJECT");
                if (tracking != null)
                {
                    Renderer trackingRenderer = tracking.GetComponent<Renderer>()
                        ?? tracking.GetComponentInChildren<Renderer>(true);
                    if (trackingRenderer != null)
                        return trackingRenderer.bounds.center;

                    Collider trackingCollider = tracking.GetComponent<Collider>()
                        ?? tracking.GetComponentInChildren<Collider>(true);
                    if (trackingCollider != null)
                        return trackingCollider.bounds.center;

                    return tracking.position;
                }

                Collider[] colliders = lockedTarget.GetComponentsInChildren<Collider>(true);
                if (colliders != null && colliders.Length > 0)
                {
                    bool initialized = false;
                    Bounds bounds = default(Bounds);
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        Collider collider = colliders[i];
                        if (collider == null || collider.isTrigger)
                            continue;

                        if (!initialized)
                        {
                            bounds = collider.bounds;
                            initialized = true;
                        }
                        else
                        {
                            bounds.Encapsulate(collider.bounds);
                        }
                    }

                    if (initialized)
                        return bounds.center;
                }

                return lockedTarget.transform.position;
            }

            return missile != null ? missile.transform.position + missile.transform.forward * 1000f : Vector3.zero;
        }

        private void Update()
        {
            if (missile == null || missile.IsDestroyed)
                return;

            if (lockedTarget == null)
                return;

            Vector3 targetCenter = GetTargetCenter();
            float currentDistance = Vector3.Distance(missile.transform.position, targetCenter);
            if (!profileActive)
            {
                currentAimPoint = targetCenter;
                hasCurrentAimPoint = true;
            }
            else if (!diveCommitted && ShouldStartDive(
                missile.transform.position,
                targetCenter,
                calculatedAttackHeight,
                currentDistance,
                Time.time - launchTime))
                diveCommitted = true;

            Vector3 aimPoint = !profileActive
                ? targetCenter
                : diveCommitted
                    ? targetCenter
                    : BuildClimbAimPoint(targetCenter, calculatedAttackHeight);
            currentAimPoint = aimPoint;
            hasCurrentAimPoint = true;

            Vector3 goalVector = aimPoint - missile.transform.position;

            if (goalVector.sqrMagnitude > 0.001f)
            {
                float distance = goalVector.magnitude;

                if (distance < minDistance)
                    minDistance = distance;
                else if (distance > minDistance + 15f)
                    passedClosestPoint = true;

                missile.GoalVector = goalVector.normalized;
                missile.TargetDistance = distance;
                missile.Guided = true;
            }
        }
    }
}
