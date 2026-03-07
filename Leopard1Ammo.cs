using System.Reflection;
using GHPC.Weapons;
using GHPC.Weaponry;
using UnityEngine;

namespace UnderdogsEnhanced
{
    public static class Leopard1Ammo
    {
        public const float DEFAULT_RHA_PENETRATION = 447f;
        public const float DEFAULT_MUZZLE_VELOCITY = 1470f;
        public const float DEFAULT_MASS = 4.4f;

        public static float Debug_RhaPenetration = DEFAULT_RHA_PENETRATION;
        public static float Debug_MuzzleVelocity = DEFAULT_MUZZLE_VELOCITY;
        public static float Debug_Mass = DEFAULT_MASS;

        public static AmmoType ammo_dm63 = null;
        public static AmmoType ammo_original = null;
        public static AmmoType.AmmoClip clip_dm63 = null;
        public static AmmoCodexScriptable ammo_codex_dm63 = null;

        public static void Init(AmmoType dm23_or_dm13)
        {
            ammo_original = dm23_or_dm13;
            ammo_dm63 = new AmmoType();
            ShallowCopy(ammo_dm63, dm23_or_dm13);
            ammo_dm63.Name = "DM63 APFSDS-T";
            ammo_dm63.RhaPenetration = Debug_RhaPenetration;
            ammo_dm63.MuzzleVelocity = Debug_MuzzleVelocity;
            ammo_dm63.Mass = Debug_Mass;
            ammo_dm63.CachedIndex = -1;

            ammo_codex_dm63 = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            ammo_codex_dm63.AmmoType = ammo_dm63;
            ammo_codex_dm63.name = "ammo_dm63";

            clip_dm63 = new AmmoType.AmmoClip();
            clip_dm63.Capacity = 1;
            clip_dm63.Name = "DM63 APFSDS-T";
            clip_dm63.MinimalPattern = new AmmoCodexScriptable[] { ammo_codex_dm63 };

            if (ammo_dm63.VisualModel != null)
            {
                var vis = Object.Instantiate(ammo_dm63.VisualModel);
                vis.name = "DM63 visual";
                ammo_dm63.VisualModel = vis;
                if (vis.GetComponent<AmmoStoredVisual>() != null)
                {
                    vis.GetComponent<AmmoStoredVisual>().AmmoType = ammo_dm63;
                    vis.GetComponent<AmmoStoredVisual>().AmmoScriptable = ammo_codex_dm63;
                }
            }
        }

        public static void ApplyDebugParams()
        {
            if (ammo_dm63 == null) return;
            ammo_dm63.RhaPenetration = Debug_RhaPenetration;
            ammo_dm63.MuzzleVelocity = Debug_MuzzleVelocity;
            ammo_dm63.Mass = Debug_Mass;
        }

        static void ShallowCopy(AmmoType dst, AmmoType src)
        {
            foreach (var f in typeof(AmmoType).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                f.SetValue(dst, f.GetValue(src));
        }
    }
}
