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
        private Renderer lockedRenderer;
        private Vector3 explicitAimPoint;
        private bool hasExplicitAimPoint;
        private float minDistance = float.MaxValue;
        private bool passedClosestPoint = false;

        internal Vehicle LockedTarget => lockedTarget;
        internal bool MissedTarget => passedClosestPoint;

        internal void SetInitialLock(Vehicle target, Transform anchor)
        {
            lockedTarget = target;
            lockedAnchor = anchor;
        }

        internal void UpdateLockSolution(Vehicle target, Transform anchor, Renderer renderer, Vector3 aimPoint)
        {
            lockedTarget = target;
            lockedAnchor = anchor;
            lockedRenderer = renderer;
            explicitAimPoint = aimPoint;
            hasExplicitAimPoint = true;
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
            if (hasExplicitAimPoint)
                return explicitAimPoint;

            if (lockedRenderer != null)
                return lockedRenderer.bounds.center;

            if (lockedAnchor != null)
            {
                Renderer anchorRenderer = lockedAnchor.GetComponent<Renderer>() ?? lockedAnchor.GetComponentInChildren<Renderer>(true);
                if (anchorRenderer != null)
                    return anchorRenderer.bounds.center;

                Collider anchorCollider = lockedAnchor.GetComponent<Collider>() ?? lockedAnchor.GetComponentInChildren<Collider>(true);
                if (anchorCollider != null)
                    return anchorCollider.bounds.center;

                return lockedAnchor.position;
            }

            if (lockedTarget != null)
            {
                Transform tracking = lockedTarget.transform.Find("TRACKING OBJECT");
                if (tracking != null)
                {
                    Renderer trackingRenderer = tracking.GetComponent<Renderer>() ?? tracking.GetComponentInChildren<Renderer>(true);
                    if (trackingRenderer != null)
                        return trackingRenderer.bounds.center;

                    Collider trackingCollider = tracking.GetComponent<Collider>() ?? tracking.GetComponentInChildren<Collider>(true);
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

            return missile.transform.position + missile.transform.forward * 1000f;
        }
    }
}
