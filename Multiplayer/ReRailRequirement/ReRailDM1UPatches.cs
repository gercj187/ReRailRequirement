#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using DV;

namespace ReRailRequirement
{
    // ---- DM1U-Enforcer (Range/Weight) ----
    internal static class RRR_RangeEnforcer
    {
        // Entfernt: teure ResolveRerailControllerType(). Stattdessen Cache nutzen.

        private static bool IsDerailedCarScanState(object rc)
        {
            var t = rc.GetType();
            var prop = AccessTools.Property(t, "CurrentState");
            if (prop == null) return false;
            var val = prop.GetValue(rc);
            return Convert.ToInt32(val) == 0; // enum State.DerailedCarScan
        }

        private static TrainCar? GetPointedCar(object rc)
        {
            var t = rc.GetType();
            var fi = AccessTools.Field(t, "pointedDerailedCar");
            return fi?.GetValue(rc) as TrainCar;
        }

        private static void ClearPointedCar(object rc)
        {
            var t = rc.GetType();
            var mPointToCar = AccessTools.Method(t, "PointToCar", new[] { typeof(TrainCar) });
            if (mPointToCar != null) { mPointToCar.Invoke(rc, new object?[] { null }); return; }

            var fi = AccessTools.Field(t, "pointedDerailedCar");
            fi?.SetValue(rc, null);
            AccessTools.Method(t, "ClearHighlightCar")?.Invoke(rc, null);
        }

        private static Transform? GetSignalOrigin(object rc)
        {
            var t = rc.GetType();
            var fi = AccessTools.Field(t, "signalOrigin");
            return fi?.GetValue(rc) as Transform;
        }

        private static float GetWeightLimitTons()
        {
            try
            {
                var mainType = typeof(Settings).Assembly.GetType("ReRailRequirement.Main");
                var settingsField = AccessTools.Field(mainType, "settings");
                if (settingsField?.GetValue(null) is Settings s)
                    return Mathf.Max(1f, s.bc_maxWeight_t);
            }
            catch { }
            return 30f;
        }

        private static bool IsCraneOrFlat(TrainCar car)
        {
            try
            {
                var id = car?.carLivery?.id;
                return id == "Crane" || id == "CraneFlat";
            }
            catch { return false; }
        }

        private static bool BypassWeightForCrane(TrainCar car)
        {
            if (!BrakedownCraneDetector.IsDetected) return false;

            try
            {
                var mainType = typeof(Settings).Assembly.GetType("ReRailRequirement.Main");
                var settingsField = AccessTools.Field(mainType, "settings");
                if (settingsField?.GetValue(null) is Settings s)
                {
                    if (s.bc_allowDM1U_rerailCrane && IsCraneOrFlat(car))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsTooFar(Transform origin, TrainCar car, float rangeMeters)
        {
            float d = Vector3.Distance(origin.position, car.transform.position);
            return d > rangeMeters + 0.01f;
        }

        private static float GetRadioRangeMeters()
        {
            // Bereits kontextsensitiv (DM1U != 100f, sonst 100f).
            return Mathf.Max(1f, RRR_Config.GetSignalRange());
        }

        private static bool IsTooHeavy(TrainCar car, float limitTons)
        {
            // Wenn Option aktiv und Ziel Kran/Flat ist, Gewichtslimit ignorieren (nur DM1U-Pfad greift diesen Enforcer).
            if (BypassWeightForCrane(car)) return false;

            try
            {
                var rb = car.GetComponent<Rigidbody>();
                if (rb == null) return false;
                float tons = Mathf.Max(0f, rb.mass) / 1000f;
                return tons > limitTons + 0.01f;
            }
            catch { return false; }
        }

        [HarmonyPatch]
        internal static class Patch_OnUpdate_Enforce
        {
            static MethodBase TargetMethod()
            {
                var t = CachedTypes.RerailControllerType;
                return AccessTools.Method(t, "OnUpdate") ?? throw new InvalidOperationException("OnUpdate not found");
            }

            static void Postfix(object __instance)
            {
                // Enforcer greift NUR im DM1U-Pfad.
                if (!RRR_Context.InRadioRerail) return;
                if (Main.CurrentAllowSource != AllowSource.DM1U) return;

                if (!IsDerailedCarScanState(__instance)) return;

                var car = GetPointedCar(__instance);
                if (car == null) return;

                var so = GetSignalOrigin(__instance);
                if (so == null) return;

                float range = GetRadioRangeMeters();
                float weightLimit = GetWeightLimitTons();

                if (IsTooFar(so, car, range) || IsTooHeavy(car, weightLimit))
                {
                    ClearPointedCar(__instance);
                }
            }
        }

        [HarmonyPatch]
        internal static class Patch_OnUse_Enforce
        {
            static MethodBase TargetMethod()
            {
                var t = CachedTypes.RerailControllerType;
                return AccessTools.Method(t, "OnUse") ?? throw new InvalidOperationException("OnUse not found");
            }

            static void Prefix(object __instance)
            {
                // Enforcer greift NUR im DM1U-Pfad.
                if (!RRR_Context.InRadioRerail) return;
                if (Main.CurrentAllowSource != AllowSource.DM1U) return;

                if (!IsDerailedCarScanState(__instance)) return;

                var car = GetPointedCar(__instance);
                if (car == null) return;

                var so = GetSignalOrigin(__instance);
                if (so == null) return;

                float range = GetRadioRangeMeters();
                float weightLimit = GetWeightLimitTons();

                if (IsTooFar(so, car, range) || IsTooHeavy(car, weightLimit))
                {
                    ClearPointedCar(__instance);
                }
            }
        }
    }
}
