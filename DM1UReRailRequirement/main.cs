using HarmonyLib;
using UnityModManagerNet;
using UnityEngine;
using System.Collections;
using DV;

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

            CoroutineRunner.Start(PeriodicCheck());
            return true;
        }

        static bool Unload(UnityModManager.ModEntry modEntry)
        {
            harmony?.UnpatchAll(modEntry.Info.Id);
            CoroutineRunner.Stop();
            return true;
        }

        private static IEnumerator PeriodicCheck()
        {
            while (true)
            {
                if (CarSpawner.Instance == null || PlayerManager.PlayerTransform == null)
                {
                    yield return new WaitForSeconds(10f);
                    continue;
                }

                var radio = GameObject.FindObjectOfType<CommsRadioController>();
                if (radio != null)
                {
                    Vector3 playerPos = PlayerManager.PlayerTransform.position;
                    bool dm1uNearby = false;

                    foreach (TrainCar car in CarSpawner.Instance.AllCars)
                    {
                        if (car != null && car.name != null && car.name.Contains("LocoDM1U"))
                        {
                            float dist = Vector3.Distance(car.transform.position, playerPos);
                            if (dist <= 250f)
                            {
                                dm1uNearby = true;
                                break;
                            }
                        }
                    }

                    if (dm1uNearby)
                    {
                        radio.ActivateMode<RerailController>();
                        Debug.Log("[DM1UReRailRequirement] DM1U in range – Rerail enabled");
                    }
                    else
                    {
                        radio.DeactivateMode<RerailController>();
                        Debug.Log("[DM1UReRailRequirement] No DM1U within 250m – Rerail disabled");
                    }
                }

                yield return new WaitForSeconds(10f);
            }
        }
    }

    public class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner? instance;

        public static void Start(IEnumerator routine)
        {
            if (instance == null)
            {
                GameObject go = new GameObject("DM1UReRailRequirement_CoroutineRunner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                instance = go.AddComponent<CoroutineRunner>();
            }

            instance.StartCoroutine(routine);
        }

        public static void Stop()
        {
            if (instance != null)
            {
                UnityEngine.Object.Destroy(instance.gameObject);
                instance = null;
            }
        }
    }
}
