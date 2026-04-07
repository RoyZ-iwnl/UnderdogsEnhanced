using GHPC.Vehicle;
using GHPC.Weapons;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal sealed class SpikeInfraredTracker : MonoBehaviour
    {
        private LiveRound missile;
        private Vehicle lockedTarget;
        private Transform lockedAnchor;
        private float minDistance = float.MaxValue;
        private bool passedClosestPoint = false;

        internal Vehicle LockedTarget => lockedTarget;
        internal bool MissedTarget => passedClosestPoint;

        internal void SetInitialLock(Vehicle target, Transform anchor)
        {
            lockedTarget = target;
            lockedAnchor = anchor;
        }

        private void Start()
        {
            missile = GetComponent<LiveRound>();
        }

        private void Update()
        {
            if (missile == null || missile.IsDestroyed)
                return;

            if (lockedTarget != null)
            {
                Vector3 targetPoint = GetTargetAimPoint();
                Vector3 goalVector = targetPoint - missile.transform.position;

                if (goalVector.sqrMagnitude > 0.001f)
                {
                    float currentDistance = goalVector.magnitude;

                    if (currentDistance < minDistance)
                        minDistance = currentDistance;
                    else if (currentDistance > minDistance + 10f)
                        passedClosestPoint = true;

                    missile.GoalVector = goalVector.normalized;
                    missile.TargetDistance = currentDistance;
                    missile.Guided = true;
                }
            }
        }

        private Vector3 GetTargetAimPoint()
        {
            if (lockedAnchor != null)
                return lockedAnchor.position;

            if (lockedTarget != null)
            {
                Transform tracking = lockedTarget.transform.Find("TRACKING OBJECT");
                if (tracking != null)
                    return tracking.position;

                return lockedTarget.transform.position;
            }

            return missile.transform.position + missile.transform.forward * 1000f;
        }
    }
}
