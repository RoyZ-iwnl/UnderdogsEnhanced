using System;
using System.Collections.Generic;
using GHPC.Vehicle;
using UnityEngine;

namespace UnderdogsEnhanced
{
    internal static class MarderSpikeTrackingDimensions
    {
        private const string TrackingObjectName = "TRACKING OBJECT";

        // 轻量级目标尺寸备用表
        private static readonly Dictionary<string, Dim> Dimensions = new Dictionary<string, Dim>(StringComparer.Ordinal)
        {
            ["Marder Bradley"] = new Dim(new Vector3(0f, 1.38f, 0.45f), new Vector3(3f, 2.5f, 6f)),
            ["M60 LEO"] = new Dim(new Vector3(0f, 1.32f, 0f), new Vector3(3.6f, 3f, 7f)),
            ["_M1"] = new Dim(new Vector3(0f, 1.21f, -0.25f), new Vector3(3.5f, 2.4f, 7.8f)),
            ["M113 M901"] = new Dim(new Vector3(0f, 1.17f, 0.35f), new Vector3(2.5f, 2.4f, 4.7f)),
            ["M151"] = new Dim(new Vector3(0f, 0.77f, -0.25f), new Vector3(0.5f, 1.5f, 3f)),
            ["M923"] = new Dim(new Vector3(0f, 1.37f, -0.78f), new Vector3(2.3f, 2.8f, 7.5f)),
            ["T72 T80 T64"] = new Dim(new Vector3(0f, 1.096f, -0.523f), new Vector3(3.6f, 2.2f, 6.5f)),
            ["T55A T62A T54A"] = new Dim(new Vector3(0f, 1.124f, 0.087f), new Vector3(3.25f, 2.35f, 6f)),
            ["Ural"] = new Dim(new Vector3(0f, 1.471f, -1.215f), new Vector3(2.65f, 3f, 7.3f)),
            ["BMP"] = new Dim(new Vector3(0f, 1.002f, 0.12f), new Vector3(3f, 2f, 6.95f)),
            ["BTR"] = new Dim(new Vector3(0f, 1.35f, 0f), new Vector3(3f, 2.6f, 7.5f)),
            ["BRDM2"] = new Dim(new Vector3(0f, 1.228f, -0.221f), new Vector3(2.25f, 2.3f, 5.75f)),
            ["T-34-85"] = new Dim(new Vector3(0f, 1.34f, 0.36f), new Vector3(3f, 2.6f, 6f)),
            ["PT76B"] = new Dim(new Vector3(0f, 1.14f, 0.32f), new Vector3(3f, 2.25f, 7f)),
            ["Mi-8"] = new Dim(new Vector3(0f, 1.33f, -0.62f), new Vector3(3.7f, 3.5f, 11f)),
            ["Mi-2"] = new Dim(new Vector3(0f, 1.228f, -0.221f), new Vector3(2.25f, 2.3f, 5.75f)),
            ["Mi-24"] = new Dim(new Vector3(0f, 1.87f, -1.2f), new Vector3(2.7f, 3f, 10f)),
            ["AH-1"] = new Dim(new Vector3(0f, 1.83f, 0.73f), new Vector3(3.4f, 2.5f, 7f)),
            ["OH-58A"] = new Dim(new Vector3(0f, 2.01f, 0.22f), new Vector3(2f, 2f, 5f))
        };

        internal static bool TryEnsureFallbackAnchor(Vehicle vehicle, out Transform anchor)
        {
            anchor = null;
            if (vehicle == null)
                return false;

            Dim dim;
            if (!TryResolveDimension(vehicle, out dim))
                return false;

            GameObject trackingObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trackingObject.name = TrackingObjectName;
            trackingObject.layer = 8;
            trackingObject.hideFlags = HideFlags.DontSave;
            trackingObject.transform.SetParent(vehicle.transform, false);
            trackingObject.transform.localPosition = dim.LocalPosition;
            trackingObject.transform.localRotation = Quaternion.identity;
            trackingObject.transform.localScale = dim.LocalScale;

            Renderer renderer = trackingObject.GetComponent<Renderer>();
            if (renderer != null)
                renderer.enabled = false;

            Collider collider = trackingObject.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = false;

            trackingObject.AddComponent<MarderSpikeTrackingFallbackMarker>();
            anchor = trackingObject.transform;
            return true;
        }

        internal static bool IsFallbackAnchor(Transform anchor)
        {
            return anchor != null && anchor.GetComponent<MarderSpikeTrackingFallbackMarker>() != null;
        }

        internal static bool TryGetFallbackBounds(Transform anchor, out Bounds bounds)
        {
            bounds = default(Bounds);
            if (!IsFallbackAnchor(anchor))
                return false;

            Vector3 lossyScale = anchor.lossyScale;
            Vector3 localExtents = new Vector3(
                Mathf.Abs(lossyScale.x) * 0.5f,
                Mathf.Abs(lossyScale.y) * 0.5f,
                Mathf.Abs(lossyScale.z) * 0.5f);

            Vector3 extents = new Vector3(
                Mathf.Abs(anchor.right.x) * localExtents.x + Mathf.Abs(anchor.up.x) * localExtents.y + Mathf.Abs(anchor.forward.x) * localExtents.z,
                Mathf.Abs(anchor.right.y) * localExtents.x + Mathf.Abs(anchor.up.y) * localExtents.y + Mathf.Abs(anchor.forward.y) * localExtents.z,
                Mathf.Abs(anchor.right.z) * localExtents.x + Mathf.Abs(anchor.up.z) * localExtents.y + Mathf.Abs(anchor.forward.z) * localExtents.z);

            bounds = new Bounds(anchor.position, extents * 2f);
            return true;
        }

        private static bool TryResolveDimension(Vehicle vehicle, out Dim dim)
        {
            dim = default(Dim);
            if (vehicle == null)
                return false;

            foreach (string candidateName in EnumerateVehicleNames(vehicle))
            {
                if (string.IsNullOrEmpty(candidateName))
                    continue;

                foreach (KeyValuePair<string, Dim> entry in Dimensions)
                {
                    if (!MatchesDimKey(candidateName, entry.Key))
                        continue;

                    dim = entry.Value;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> EnumerateVehicleNames(Vehicle vehicle)
        {
            yield return vehicle.name;
            yield return vehicle.gameObject != null ? vehicle.gameObject.name : null;
            yield return vehicle.FriendlyName;
            yield return vehicle.transform != null && vehicle.transform.root != null ? vehicle.transform.root.name : null;
        }

        private static bool MatchesDimKey(string vehicleName, string key)
        {
            string[] subkeys = key.Split(' ');
            for (int i = 0; i < subkeys.Length; i++)
            {
                if (vehicleName.IndexOf(subkeys[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private readonly struct Dim
        {
            internal Dim(Vector3 localPosition, Vector3 localScale)
            {
                LocalPosition = localPosition;
                LocalScale = localScale;
            }

            internal Vector3 LocalPosition { get; }
            internal Vector3 LocalScale { get; }
        }
    }

    internal sealed class MarderSpikeTrackingFallbackMarker : MonoBehaviour
    {
    }
}