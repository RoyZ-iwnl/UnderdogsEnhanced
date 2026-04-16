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

        // 缓存的 colliders 数组，避免每帧调用 GetComponentsInChildren
        private Collider[] cachedColliders;
        private bool collidersCached = false;

        internal Vehicle LockedTarget => lockedTarget;
        internal bool MissedTarget => passedClosestPoint;

        internal void SetInitialLock(Vehicle target, Transform anchor)
        {
            lockedTarget = target;
            lockedAnchor = anchor;
            CacheCollidersIfNeeded();
        }

        internal void UpdateLockSolution(Vehicle target, Transform anchor, Renderer renderer, Vector3 aimPoint)
        {
            // 目标变化时重新缓存 colliders
            if (target != lockedTarget)
            {
                lockedTarget = target;
                collidersCached = false;
                CacheCollidersIfNeeded();
            }

            lockedAnchor = anchor;
            lockedRenderer = renderer;
            explicitAimPoint = aimPoint;
            hasExplicitAimPoint = true;
        }

        /// <summary>
        /// 设置缓存的 colliders 数组（从外部传入，避免重复获取）
        /// </summary>
        internal void SetCachedColliders(Collider[] colliders)
        {
            cachedColliders = colliders;
            collidersCached = colliders != null && colliders.Length > 0;
        }

        private void CacheCollidersIfNeeded()
        {
            if (collidersCached || lockedTarget == null)
                return;

            cachedColliders = lockedTarget.GetComponentsInChildren<Collider>(true);
            collidersCached = cachedColliders != null && cachedColliders.Length > 0;
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
                // 优先使用 TRACKING OBJECT 的 renderer（PIL 的做法）
                Renderer anchorRenderer = lockedAnchor.GetComponent<Renderer>();
                if (anchorRenderer != null)
                    return anchorRenderer.bounds.center;

                // 其次检查子物体
                anchorRenderer = lockedAnchor.GetComponentInChildren<Renderer>(true);
                if (anchorRenderer != null)
                    return anchorRenderer.bounds.center;

                Collider anchorCollider = lockedAnchor.GetComponent<Collider>();
                if (anchorCollider != null)
                    return anchorCollider.bounds.center;

                anchorCollider = lockedAnchor.GetComponentInChildren<Collider>(true);
                if (anchorCollider != null)
                    return anchorCollider.bounds.center;

                return lockedAnchor.position;
            }

            // 使用缓存的 colliders 数组计算 bounds（避免每帧 GetComponentsInChildren）
            if (lockedTarget != null)
            {
                Transform tracking = lockedTarget.transform.Find("TRACKING OBJECT");
                if (tracking != null)
                {
                    Renderer trackingRenderer = tracking.GetComponent<Renderer>();
                    if (trackingRenderer != null)
                        return trackingRenderer.bounds.center;

                    Collider trackingCollider = tracking.GetComponent<Collider>();
                    if (trackingCollider != null)
                        return trackingCollider.bounds.center;

                    return tracking.position;
                }

                // 使用缓存计算 bounds
                if (!collidersCached)
                    CacheCollidersIfNeeded();

                if (cachedColliders != null && cachedColliders.Length > 0)
                {
                    bool initialized = false;
                    Bounds bounds = default(Bounds);
                    for (int i = 0; i < cachedColliders.Length; i++)
                    {
                        Collider collider = cachedColliders[i];
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
