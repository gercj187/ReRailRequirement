using HarmonyLib;
using UnityModManagerNet;
using UnityEngine;
using System.Linq;
using DV;
using DV.CabControls;
using DV.InventorySystem;

namespace DM1UReRailRequirement
{
    static class Main
    {
        public static Harmony? harmony;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            modEntry.OnUnload = Unload;
            Debug.Log("[DM1UReRailRequirement] Mod loaded (using ItemBase.Grabbed event)");
            return true;
        }

        static bool Unload(UnityModManager.ModEntry modEntry)
        {
            harmony?.UnpatchAll(modEntry.Info.Id);
            Debug.Log("[DM1UReRailRequirement] Mod unloaded");
            return true;
        }

        internal static void OnRadioGrabbed(CommsRadioController radio)
        {
            if (PlayerManager.PlayerTransform == null || CarSpawner.Instance == null)
            {
                Debug.LogWarning("[DM1UReRailRequirement] Player or CarSpawner not ready.");
                return;
            }

            Vector3 playerPos = PlayerManager.PlayerTransform.position;

            bool dm1uNearby = CarSpawner.Instance.AllCars
                .Any(car => car != null &&
                            car.name != null &&
                            car.name.Contains("LocoDM1U") &&
                            (car.transform.position - playerPos).sqrMagnitude <= 2500f);

            if (dm1uNearby)
            {
                radio.ActivateMode<RerailController>();
                Debug.Log("[DM1UReRailRequirement] DM1U in range – Rerail enabled");
            }
            else
            {
                radio.DeactivateMode<RerailController>();
                Debug.Log("[DM1UReRailRequirement] No DM1U nearby – Rerail disabled");
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
                Debug.LogWarning("[DM1UReRailRequirement] ItemBase missing on CommsRadio!");
                return;
            }

            // Einmal registrieren, ohne Mehrfachbindung
            itemBase.Grabbed += _ => Main.OnRadioGrabbed(__instance);
            Debug.Log("[DM1UReRailRequirement] Grab-Event registered via ItemBase");
        }
    }
}
