// File: RerailDistanceGuard.cs
// Purpose: Lightweight, adaptive distance check for rerail usage to avoid lag spikes.
// Usage: Call RerailDistanceGuard.StartGuard(...) when entering rerail mode, and
//        RerailDistanceGuard.StopGuard() when exiting. ASCII-only logs.
// Dependencies: UnityEngine

#nullable enable
using System;
using System.Collections;
using UnityEngine;

namespace ReRailRequirement
{
    /// <summary>
    /// Runs only while rerail mode is active. Checks distance adaptively and cancels when out of range.
    /// </summary>
    public sealed class RerailDistanceGuard : MonoBehaviour
    {
        // -------- Static API --------
        private static RerailDistanceGuard? _instance;
        public static bool IsRunning => _instance != null;

        /// <summary>
        /// Starts the guard. Creates a hidden GameObject with this component if needed.
        /// </summary>
        /// <param name="playerPosProvider">Function returning current player position.</param>
        /// <param name="targetPosProvider">Function returning current target vehicle position.</param>
        /// <param name="maxDistanceMeters">Max allowed distance for rerail to remain valid.</param>
        /// <param name="onOutOfRangeCancel">Callback invoked once when out of range is detected.</param>
        /// <param name="useLineOfSight">Optional: also require line of sight.</param>
        /// <param name="losMask">Layer mask for line of sight checks (only used if useLineOfSight = true).</param>
        public static void StartGuard(
            Func<Vector3> playerPosProvider,
            Func<Vector3> targetPosProvider,
            float maxDistanceMeters,
            Action onOutOfRangeCancel,
            bool useLineOfSight = false,
            LayerMask losMask = default(LayerMask))
        {
            if (_instance == null)
            {
                var go = new GameObject("RerailDistanceGuard_RUNTIME");
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<RerailDistanceGuard>();
            }
            _instance.Configure(playerPosProvider, targetPosProvider, maxDistanceMeters, onOutOfRangeCancel, useLineOfSight, losMask);
            _instance.EnableGuard();
        }

        /// <summary>
        /// Stops the guard if running and destroys the helper object.
        /// </summary>
        public static void StopGuard()
        {
            if (_instance == null) return;
            _instance.DisableGuard();
            var go = _instance.gameObject;
            _instance = null;
            if (go != null) Destroy(go);
        }

        // -------- Instance fields --------
        // Providers
        private Func<Vector3>? _playerPosProvider;
        private Func<Vector3>? _targetPosProvider;
        private Action? _onOutOfRangeCancel;

        // Config
        [SerializeField] private float _maxDistance = 35f;
        [SerializeField] private bool _useLos = false;
        [SerializeField] private LayerMask _losMask;

        // Performance tuning
        [SerializeField] private float _minCheckInterval = 0.15f;  // near limit
        [SerializeField] private float _maxCheckInterval = 0.75f;  // comfortably inside
        [SerializeField] private float _movementEps = 0.10f;       // meters required to re-check
        [SerializeField] private float _nearThreshold = 0.85f;     // when >85% of maxDistance, check faster

        // Runtime state
        private Coroutine? _loop;
        private Vector3 _lastPlayerPos;
        private Vector3 _lastTargetPos;
        private bool _firstSampleTaken;

        /// <summary>
        /// Optional: Adjust tuning parameters at runtime (e.g., from settings UI).
        /// </summary>
        public void SetTuning(float minInterval, float maxInterval, float movementEpsMeters, float nearThresholdFactor)
        {
            _minCheckInterval = Mathf.Max(0.02f, minInterval);
            _maxCheckInterval = Mathf.Max(_minCheckInterval, maxInterval);
            _movementEps = Mathf.Max(0.001f, movementEpsMeters);
            _nearThreshold = Mathf.Clamp01(nearThresholdFactor);
        }

        // -------- Lifecycle --------
        public void Configure(
            Func<Vector3> playerPosProvider,
            Func<Vector3> targetPosProvider,
            float maxDistanceMeters,
            Action onOutOfRangeCancel,
            bool useLineOfSight,
            LayerMask losMask)
        {
            _playerPosProvider = playerPosProvider ?? throw new ArgumentNullException(nameof(playerPosProvider));
            _targetPosProvider = targetPosProvider ?? throw new ArgumentNullException(nameof(targetPosProvider));
            _onOutOfRangeCancel = onOutOfRangeCancel ?? throw new ArgumentNullException(nameof(onOutOfRangeCancel));
            _maxDistance = Mathf.Max(0.1f, maxDistanceMeters);
            _useLos = useLineOfSight;
            _losMask = losMask;
        }

        public void EnableGuard()
        {
            if (_loop != null) return;
            _firstSampleTaken = false;
            _loop = StartCoroutine(GuardLoop());
            Debug.Log("[ReRailRequirement_Guard] Started.");
        }

        public void DisableGuard()
        {
            if (_loop == null) return;
            StopCoroutine(_loop);
            _loop = null;
            Debug.Log("[ReRailRequirement_Guard] Stopped.");
        }

        private IEnumerator GuardLoop()
        {
            float maxDist = _maxDistance;
            float maxDistSqr = maxDist * maxDist;
            float nearDist = _nearThreshold * maxDist;

            while (true)
            {
                // 1) Sample positions
                Vector3 p = SafeGet(_playerPosProvider, _lastPlayerPos);
                Vector3 t = SafeGet(_targetPosProvider, _lastTargetPos);

                if (!_firstSampleTaken)
                {
                    _lastPlayerPos = p;
                    _lastTargetPos = t;
                    _firstSampleTaken = true;
                }

                // 2) Movement gating: skip heavy work if nothing moved noticeably
                bool playerMoved = (p - _lastPlayerPos).sqrMagnitude > (_movementEps * _movementEps);
                bool targetMoved = (t - _lastTargetPos).sqrMagnitude > (_movementEps * _movementEps);

                if (playerMoved || targetMoved)
                {
                    _lastPlayerPos = p;
                    _lastTargetPos = t;

                    // 3) Distance check (squared)
                    float distSqr = (p - t).sqrMagnitude;
                    if (distSqr > maxDistSqr)
                    {
                        Debug.Log("[ReRailRequirement_Guard] Out of range. Cancelling rerail.");
                        SafeCall(_onOutOfRangeCancel);
                        yield break; // guard ends
                    }

                    // 4) Optional Line-of-Sight
                    if (_useLos)
                    {
                        Vector3 pEye = p + Vector3.up * 1.5f;
                        Vector3 tEye = t + Vector3.up * 1.5f;
                        if (Physics.Linecast(pEye, tEye, out var hit, _losMask, QueryTriggerInteraction.Ignore))
                        {
                            Debug.Log("[ReRailRequirement_Guard] Line of sight blocked. Cancelling rerail.");
                            SafeCall(_onOutOfRangeCancel);
                            yield break;
                        }
                    }
                }

                // 5) Adaptive sleep:
                //    - Fast when near the limit
                //    - Slow when comfortably inside
                float currentDist = Mathf.Sqrt((p - t).sqrMagnitude);
                float sleep = _maxCheckInterval;

                if (currentDist >= nearDist)
                {
                    // near the threshold: tighten checking
                    sleep = _minCheckInterval;
                }

                // Add tiny jitter to avoid phase alignment if multiple guards run
                sleep += UnityEngine.Random.Range(0f, 0.05f);

                yield return new WaitForSeconds(sleep);
            }
        }

        private static Vector3 SafeGet(Func<Vector3>? f, Vector3 fallback)
        {
            try { return f != null ? f() : fallback; }
            catch { return fallback; }
        }

        private static void SafeCall(Action? a)
        {
            try { a?.Invoke(); }
            catch (Exception e) { Debug.LogError("[ReRailRequirement_Guard] Cancel callback threw: " + e); }
        }
    }
}
