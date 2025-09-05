using HarmonyLib;
using UnityModManagerNet;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using DV;
using DV.CabControls;
using DV.InventorySystem;

namespace ReRailRequirement
{
    public enum AllowSource { None = 0, DM1U = 1, Crane = 2 }

    static class Main
    {
        // HINWEIS: Feld muss "internal" oder "public" sein, damit andere Klassen zugreifen (z. B. Patches).
        internal static Settings settings = new Settings();
        public static Harmony? harmony;

        private static TrainCar? activeTargetCar;
        private static CommsRadioController? activeRadio;

        public static AllowSource CurrentAllowSource { get; internal set; } = AllowSource.None;

        // Vanilla-Klammern
        private const float VANILLA_MIN_DIST = 5f;
        private const float VANILLA_MAX_DIST = 50f;

        // BreakdownCrane-Klammern (nur fuer GUI)
        private const float BC_MIN_DIST   = 5f;
        private const float BC_MAX_DIST   = 25f;
        private const float BC_MIN_RANGE  = 10f;
        private const float BC_MAX_RANGE  = 50f;
        private const float BC_MIN_WEIGHT = 10f;
        private const float BC_MAX_WEIGHT = 50f;

        // Expansion-Klammern
        private const float BCX_MIN_PRICE_MUL = 1f;
        private const float BCX_MAX_PRICE_MUL = 2.5f;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            settings = Settings.Load<Settings>(modEntry);

            RRR_Config.Setup(
                () => BrakedownCraneDetector.IsDetected,
                () => CurrentAllowSource == AllowSource.DM1U ? settings.bc_rerailRange_m : 100f
            );

            settings.maxDistanceMeters        = Mathf.Clamp(Mathf.Round(settings.maxDistanceMeters),        VANILLA_MIN_DIST, VANILLA_MAX_DIST);
            settings.bc_distanceToDM1U_m      = Mathf.Clamp(Mathf.Round(settings.bc_distanceToDM1U_m),      BC_MIN_DIST,      BC_MAX_DIST);
            settings.bc_rerailRange_m         = Mathf.Clamp(Mathf.Round(settings.bc_rerailRange_m),         BC_MIN_RANGE,     BC_MAX_RANGE);
            settings.bc_maxWeight_t           = Mathf.Clamp(Mathf.Round(settings.bc_maxWeight_t),           BC_MIN_WEIGHT,    BC_MAX_WEIGHT);
            settings.bcx_basePriceMul         = Mathf.Clamp(settings.bcx_basePriceMul,                       BCX_MIN_PRICE_MUL, BCX_MAX_PRICE_MUL);
            settings.bcx_pricePerMeterMul     = Mathf.Clamp(settings.bcx_pricePerMeterMul,                   BCX_MIN_PRICE_MUL, BCX_MAX_PRICE_MUL);

            modEntry.OnGUI     = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            harmony = new Harmony(modEntry.Info.Id);

            // 1) Standard-Attribute patches
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // 2) BREITER Preis-Patch: Alle Rerail-bezogenen Methoden (inkl. Nested Types) in Assembly-CSharp
            RRR_MassPriceTranspiler.ApplyBroad(harmony);

            modEntry.OnUnload = Unload;

            Debug.Log("[ReRailRequirement] Mod loaded. BreakdownCraneDetected=" + BrakedownCraneDetector.IsDetected);
            return true;
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (!BrakedownCraneDetector.IsDetected)
            {
                GUILayout.Label($"Distance to Maintenance-Vehicle (DM1U): {settings.maxDistanceMeters:0} meter");
				GUILayout.Label("(Distance between Player and DM1U to have Rerail-Option available)");
                float slider = GUILayout.HorizontalSlider(settings.maxDistanceMeters, VANILLA_MIN_DIST, VANILLA_MAX_DIST);
                settings.maxDistanceMeters = Mathf.Clamp(Mathf.Round(slider), VANILLA_MIN_DIST, VANILLA_MAX_DIST);
                GUILayout.Space(10f);
                GUILayout.Label(">>> INFO : If you want the total immersion, check out Cruzer's BreakdownCrane-Mod! <<<");
            }
            else
            {
                GUILayout.Label(">>> INFO : BreakdownCrane Mod detected! New exclusive DM1U parameters <<<");
                GUILayout.Space(10f);

                GUILayout.Label("New DM1U-Settings:");
                GUILayout.Space(5f);
                GUILayout.Label($"Maximum distance to DM1U : {settings.bc_distanceToDM1U_m:0} meter");
				GUILayout.Label("(Distance between Player and DM1U to have Rerail-Option available)");
                float s1 = GUILayout.HorizontalSlider(settings.bc_distanceToDM1U_m, BC_MIN_DIST, BC_MAX_DIST);
                settings.bc_distanceToDM1U_m = Mathf.Clamp(Mathf.Round(s1), BC_MIN_DIST, BC_MAX_DIST);

                GUILayout.Label($"Maximum rerail distance : {settings.bc_rerailRange_m:0} meter");
				GUILayout.Label("(Distance between Player and derailed Vehicle to allow rerailing)");
                float s2 = GUILayout.HorizontalSlider(settings.bc_rerailRange_m, BC_MIN_RANGE, BC_MAX_RANGE);
                settings.bc_rerailRange_m = Mathf.Clamp(Mathf.Round(s2), BC_MIN_RANGE, BC_MAX_RANGE);

                GUILayout.Label($"Maximum rerail weight : {settings.bc_maxWeight_t:0} tons");
				GUILayout.Label("(Maximum weight for derailed Vehicle to allow rerailing)");
                float s3 = GUILayout.HorizontalSlider(settings.bc_maxWeight_t, BC_MIN_WEIGHT, BC_MAX_WEIGHT);
                settings.bc_maxWeight_t = Mathf.Clamp(Mathf.Round(s3), BC_MIN_WEIGHT, BC_MAX_WEIGHT);
				
				GUILayout.Label("(If the derailed vehicle is to far away or to heavy, you need the Crane to do this job!)");

                GUILayout.Space(10f);
                GUILayout.Label("BrakedownCrane - Expansion");
                GUILayout.Space(5f);
                    
				bool newToggle = GUILayout.Toggle(settings.bc_allowDM1U_rerailCrane, " <- Allow DM1U to rerail Crane");
                settings.bc_allowDM1U_rerailCrane = newToggle;
				GUILayout.Label("(If Crane is derailed and not coupled to Crane-Flatcar)");
                GUILayout.Space(5f);

                GUILayout.Label($"Crane Rerail - Cost Multiplicator : {settings.bcx_basePriceMul:0.0}x");
				GUILayout.Label("(use this multiplicator to increase costs for rerailing with Crane (x1 = vanilla) )");
                float m1 = GUILayout.HorizontalSlider(settings.bcx_basePriceMul, BCX_MIN_PRICE_MUL, BCX_MAX_PRICE_MUL);
                settings.bcx_basePriceMul = Mathf.Clamp((float)System.Math.Round(m1, 2), BCX_MIN_PRICE_MUL, BCX_MAX_PRICE_MUL);

                GUILayout.Label($"Crane-Rerail - Cost per Meter Multiplicator : {settings.bcx_pricePerMeterMul:0.0}x");
                GUILayout.Label("(use this multiplicator to increase costs for each meter with Crane (x1 = vanilla) )");
                float m2 = GUILayout.HorizontalSlider(settings.bcx_pricePerMeterMul, BCX_MIN_PRICE_MUL, BCX_MAX_PRICE_MUL);
                settings.bcx_pricePerMeterMul = Mathf.Clamp((float)System.Math.Round(m2, 2), BCX_MIN_PRICE_MUL, BCX_MAX_PRICE_MUL);
            }
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.maxDistanceMeters    = Mathf.Clamp(Mathf.Round(settings.maxDistanceMeters),    VANILLA_MIN_DIST, VANILLA_MAX_DIST);
            settings.bc_distanceToDM1U_m  = Mathf.Clamp(Mathf.Round(settings.bc_distanceToDM1U_m),  BC_MIN_DIST,      BC_MAX_DIST);
            settings.bc_rerailRange_m     = Mathf.Clamp(Mathf.Round(settings.bc_rerailRange_m),     BC_MIN_RANGE,     BC_MAX_RANGE);
            settings.bc_maxWeight_t       = Mathf.Clamp(Mathf.Round(settings.bc_maxWeight_t),       BC_MIN_WEIGHT,    BC_MAX_WEIGHT);
            settings.bcx_basePriceMul     = Mathf.Clamp((float)System.Math.Round(settings.bcx_basePriceMul, 2),     BCX_MIN_PRICE_MUL, BCX_MAX_PRICE_MUL);
            settings.bcx_pricePerMeterMul = Mathf.Clamp((float)System.Math.Round(settings.bcx_pricePerMeterMul, 2), BCX_MIN_PRICE_MUL, BCX_MAX_PRICE_MUL);
            settings.Save(modEntry);
        }

        static bool Unload(UnityModManager.ModEntry modEntry)
        {
            RerailDistanceGuard.StopGuard();
            activeTargetCar = null;
            activeRadio = null;
            CurrentAllowSource = AllowSource.None;

            harmony?.UnpatchAll(modEntry.Info.Id);
            Debug.Log("[ReRailRequirement] Mod unloaded");
            return true;
        }

        internal static void OnRadioGrabbed(CommsRadioController radio)
        {
            if (PlayerManager.PlayerTransform == null || CarSpawner.Instance == null)
            {
                Debug.LogWarning("[ReRailRequirement] Player or CarSpawner not ready.");
                return;
            }

            activeRadio = radio;
            Vector3 playerPos = PlayerManager.PlayerTransform.position;

            // DM1U-Distanzlimit (nur fuer DM1U-Pfad relevant)
            float distLimitDM1U = !BrakedownCraneDetector.IsDetected
                ? Mathf.Max(VANILLA_MIN_DIST, settings.maxDistanceMeters)
                : Mathf.Max(5f, settings.bc_distanceToDM1U_m);

            float rSqr = distLimitDM1U * distLimitDM1U;

            // 1) DM1U in Reichweite?
            TrainCar? dm1u = CarSpawner.Instance.AllCars
                .FirstOrDefault(car => car != null &&
                                       car.name != null &&
                                       car.name.Contains("LocoDM1U") &&
                                       (car.transform.position - playerPos).sqrMagnitude <= rSqr);

            // 2) BreakdownCrane-Konstellation erlaubt?
            bool allowByCrane = false;
            if (BrakedownCraneDetector.IsDetected)
            {
                float craneDetectRange = Mathf.Max(5f, settings.bc_rerailRange_m); // nur Erkennungsradius
                allowByCrane = BrakedownCraneDetector.IsCraneRerailAllowed(
                    origin: playerPos,
                    craneDistance: craneDetectRange,
                    requireFlatBehindCrane: false
                );
            }

            bool allow = (dm1u != null) || allowByCrane;

            // Guard/Quelle resetten
            RerailDistanceGuard.StopGuard();
            activeTargetCar = null;
            CurrentAllowSource = AllowSource.None;

            if (allow)
            {
                radio.ActivateMode<RerailController>();

                if (dm1u != null)
                {
                    CurrentAllowSource = AllowSource.DM1U;

                    activeTargetCar = dm1u;
                    Debug.Log("[ReRailRequirement] DM1U in range - Rerail enabled (DM1U mode)");

                    // Guard ueberwacht nur im DM1U-Modus die Distanz
                    RerailDistanceGuard.StartGuard(
                        () => PlayerManager.PlayerTransform.position,
                        () => activeTargetCar != null ? activeTargetCar.transform.position : dm1u.transform.position,
                        distLimitDM1U,
                        () => DeactivateRerailDueToRange(radio)
                    );
                }
                else
                {
                    CurrentAllowSource = AllowSource.Crane;

                    Debug.Log("[ReRailRequirement] BreakdownCrane in range - Rerail enabled (Crane mode)");
                    // KEIN Guard, KEINE bc_* Limits; Kran-Flow bleibt frei.
                }
            }
            else
            {
                radio.DeactivateMode<RerailController>();
                Debug.Log("[ReRailRequirement] No DM1U/Crane nearby - Rerail disabled");
            }
        }

        private static void DeactivateRerailDueToRange(CommsRadioController radio)
        {
            try
            {
                radio.DeactivateMode<RerailController>();
                Debug.Log("[ReRailRequirement] Out of range - Rerail disabled");
            }
            catch { }
            finally
            {
                RerailDistanceGuard.StopGuard();
                activeTargetCar = null;
                CurrentAllowSource = AllowSource.None;
            }
        }
    }

    [HarmonyPatch(typeof(CommsRadioController), "Start")]
    static class Patch_CommsRadio_Start
    {
        static void Postfix(CommsRadioController __instance)
        {
            var itemBase = __instance.GetComponent<ItemBase>();
            if (itemBase == null)
            {
                Debug.LogWarning("[ReRailRequirement] ItemBase missing on CommsRadio!");
                return;
            }

            itemBase.Grabbed += _ => Main.OnRadioGrabbed(__instance);
            Debug.Log("[ReRailRequirement] Grab event registered via ItemBase");
        }
    }

    [HarmonyPatch(typeof(CommsRadioController), "OnDisable")]
    static class Patch_CommsRadio_OnDisable
    {
        static void Postfix(CommsRadioController __instance)
        {
            RerailDistanceGuard.StopGuard();
            Main.CurrentAllowSource = AllowSource.None;
        }
    }
}
