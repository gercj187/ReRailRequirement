using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using DV;

namespace ReRailRequirement
{
    internal static class BrakedownCraneDetector
    {
        private static bool _checked;
        private static bool _isDetected;

        public static bool IsDetected
        {
            get
            {
                if (!_checked)
                {
                    _isDetected = Probe();
                    _checked = true;
#if DEBUG
                    Debug.Log("[ReRailRequirement] BreakdownCrane detected: " + _isDetected);
#endif
                }
                return _isDetected;
            }
        }

        // Cache fuer letztes gefundenes Paar
        private static WeakReference<TrainCar>? _cachedCrane;
        private static WeakReference<TrainCar>? _cachedFlat;
        private static float _cacheTime;
        private const float CACHE_TTL = 1.0f;

        public static bool IsCraneRerailAllowed(Vector3 origin, float craneDistance, bool requireFlatBehindCrane = false)
        {
            TrainCar crane, flat;
            if (!TryFindCranePair(out crane, out flat))
                return false;

            if (!AreDirectlyCoupled(crane, flat))
                return false;

            if (requireFlatBehindCrane && !IsFlatBehindCrane(crane, flat))
                return false;

            // Reichweite: Spieler muss zum Kran ODER Flat innerhalb sein
            float d1 = Vector3.Distance(origin, crane.transform.position);
            if (d1 <= craneDistance) return true;

            float d2 = Vector3.Distance(origin, flat.transform.position);
            if (d2 <= craneDistance) return true;

            return false;
        }

        public static bool TryFindCranePair(out TrainCar crane, out TrainCar flat)
        {
            crane = null!;
            flat  = null!;

            // Cache gueltig?
            if (Time.realtimeSinceStartup - _cacheTime <= CACHE_TTL
                && TryGet(_cachedCrane, out var c1)
                && TryGet(_cachedFlat, out var c2)
                && c1 != null && c2 != null)
            {
                crane = c1!;  // null-forgiving: vorherige Checks stellen Nicht-Null sicher
                flat  = c2!;  // null-forgiving
                return true;
            }

            if (CarSpawner.Instance == null) return false;

            var cars = CarSpawner.Instance.AllCars;
            if (cars == null) return false;

            var craneCar = cars.FirstOrDefault(tc => tc != null && tc.carLivery != null && tc.carLivery.id == "Crane");
            var flatCar  = cars.FirstOrDefault(tc => tc != null && tc.carLivery != null && tc.carLivery.id == "CraneFlat");

            if (craneCar == null || flatCar == null)
                return false;

            crane = craneCar;
            flat  = flatCar;

            _cachedCrane = new WeakReference<TrainCar>(craneCar);
            _cachedFlat  = new WeakReference<TrainCar>(flatCar);
            _cacheTime   = Time.realtimeSinceStartup;
            return true;
        }

        private static bool TryGet<T>(WeakReference<T>? wr, out T? obj) where T : class
        {
            obj = null;
            if (wr == null) return false;
            return wr.TryGetTarget(out obj) && obj != null;
        }

        private static bool AreDirectlyCoupled(TrainCar a, TrainCar b)
        {
            if (CoupledTo(a, b)) return true;
            if (CoupledTo(b, a)) return true;
            float dist = Vector3.Distance(a.transform.position, b.transform.position);
            return dist < 2.5f;
        }

        private static bool CoupledTo(TrainCar from, TrainCar to)
        {
            var t = from.GetType();
            object? front = GetMemberValue(from, t, "frontCoupler") ?? GetMemberValue(from, t, "FrontCoupler");
            object? rear  = GetMemberValue(from, t, "rearCoupler")  ?? GetMemberValue(from, t, "RearCoupler");

            if (front != null && CouplerTargets(front, to)) return true;
            if (rear  != null && CouplerTargets(rear,  to)) return true;
            return false;
        }

        private static bool CouplerTargets(object coupler, TrainCar target)
        {
            try
            {
                var ct = coupler.GetType();
                var other = GetMemberValue(coupler, ct, "coupledTo")
                         ?? GetMemberValue(coupler, ct, "otherCoupler")
                         ?? GetMemberValue(coupler, ct, "coupledCoupler");
                if (other == null) return false;

                var ot = other.GetType();
                var carRef = GetMemberValue(other, ot, "train")
                          ?? GetMemberValue(other, ot, "trainCar")
                          ?? GetMemberValue(other, ot, "car")
                          ?? GetMemberValue(other, ot, "Car");

                if (carRef is TrainCar tc)
                    return tc == target || tc.transform.root == target.transform.root;
            }
            catch { }
            return false;
        }

        private static bool IsFlatBehindCrane(TrainCar crane, TrainCar flat)
        {
            try
            {
                Vector3 toFlat = flat.transform.position - crane.transform.position;
                float dot = Vector3.Dot(crane.transform.forward.normalized, toFlat.normalized);
                return dot < 0f; // hinter dem Kran
            }
            catch { return true; }
        }

        private static object? GetMemberValue(object instance, Type type, string name)
        {
            try
            {
                var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (f != null) return f.GetValue(instance);
                var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (p != null) return p.GetValue(instance, null);
            }
            catch { }
            return null;
        }

        private static bool Probe()
        {
            try
            {
                var nsHit = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Any(t => t?.Namespace != null && t.Namespace.IndexOf("BreakdownCrane", StringComparison.OrdinalIgnoreCase) >= 0);
                if (nsHit) return true;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var name = asm.GetName().Name ?? string.Empty;
                        if (name.IndexOf("BreakdownCrane", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }
    }
}
