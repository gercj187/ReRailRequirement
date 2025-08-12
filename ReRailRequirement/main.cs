using HarmonyLib;
using UnityModManagerNet;
using UnityEngine;
using System.Linq;
using DV;
using DV.CabControls;
using DV.InventorySystem;

namespace ReRailRequirement
{
    static class Main
    {
        static Settings settings = new Settings();
        public static Harmony? harmony;
        private static TrainCar? activeTargetCar;
        private static CommsRadioController? activeRadio;

        private const float MIN_DIST = 10f;
        private const float MAX_DIST = 50f;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            settings = Settings.Load<Settings>(modEntry);
            // Hard clamp beim Laden, falls alte Settings außerhalb liegen
            settings.maxDistanceMeters = Mathf.Clamp(Mathf.Round(settings.maxDistanceMeters), MIN_DIST, MAX_DIST);

            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            modEntry.OnUnload = Unload;
            Debug.Log("[ReRailRequirement] Mod loaded (using ItemBase.Grabbed event + OnDisable)");
            return true;
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label($"Distance to Maintenance-Vehicle (DM1U): {settings.maxDistanceMeters:0} meter");
            float slider = GUILayout.HorizontalSlider(settings.maxDistanceMeters, MIN_DIST, MAX_DIST);
            settings.maxDistanceMeters = Mathf.Clamp(Mathf.Round(slider), MIN_DIST, MAX_DIST);
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.maxDistanceMeters = Mathf.Clamp(Mathf.Round(settings.maxDistanceMeters), MIN_DIST, MAX_DIST);
            settings.Save(modEntry);
        }

        static bool Unload(UnityModManager.ModEntry modEntry)
        {
            RerailDistanceGuard.StopGuard();
            activeTargetCar = null;
            activeRadio = null;

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

            // Reichweite für die Vorwahl der DM1U auf Settings-Wert umstellen
            float r = Mathf.Max(MIN_DIST, settings.maxDistanceMeters);
            float rSqr = r * r;

            TrainCar? targetCar = CarSpawner.Instance.AllCars
                .FirstOrDefault(car => car != null &&
                                       car.name != null &&
                                       car.name.Contains("LocoDM1U") &&
                                       (car.transform.position - playerPos).sqrMagnitude <= rSqr);

            // Falls vorheriger Guard lief, stoppen bevor neu gestartet wird
            RerailDistanceGuard.StopGuard();
            activeTargetCar = null;

            if (targetCar != null)
            {
                radio.ActivateMode<RerailController>();
                activeTargetCar = targetCar;
                Debug.Log("[ReRailRequirement] DM1U in range - Rerail enabled");

                // Start Guard: prüft Distanz adaptiv, beendet Modus bei Out-of-Range
                RerailDistanceGuard.StartGuard(
                    () => PlayerManager.PlayerTransform.position,
                    () => activeTargetCar != null ? activeTargetCar.transform.position : targetCar.transform.position,
                    settings.maxDistanceMeters, // aus den Settings
                    () => DeactivateRerailDueToRange(radio)
                );
            }
            else
            {
                radio.DeactivateMode<RerailController>();
                Debug.Log("[ReRailRequirement] No DM1U nearby - Rerail disabled");
            }
        }

        private static void DeactivateRerailDueToRange(CommsRadioController radio)
        {
            try
            {
                radio.DeactivateMode<RerailController>();
                Debug.Log("[ReRailRequirement] Out of range - Rerail disabled");
            }
            catch { /* ignore */ }
            finally
            {
                RerailDistanceGuard.StopGuard();
                activeTargetCar = null;
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

            // Nur Grabbed registrieren (existiert sicher)
            itemBase.Grabbed += _ => Main.OnRadioGrabbed(__instance);

            Debug.Log("[ReRailRequirement] Grab event registered via ItemBase");
        }
    }

    // Guard defensiv stoppen, wenn das Funkgerät deaktiviert wird (Ablegen, Holstern, Szenenwechsel etc.)
    [HarmonyPatch(typeof(CommsRadioController), "OnDisable")]
    static class Patch_CommsRadio_OnDisable
    {
        static void Postfix(CommsRadioController __instance)
        {
            RerailDistanceGuard.StopGuard();
        }
    }
}
