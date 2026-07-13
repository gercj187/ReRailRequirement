#nullable enable
using System.Reflection;
using HarmonyLib;

namespace ReRailRequirement
{
    // ---- Failsafe: Postfix auf CalculatePrice, nur fuer Crane-Pfad ----
    [HarmonyPatch]
    internal static class Patch_RerailController_CalculatePrice_Postfix
    {
        static MethodBase TargetMethod()
            => RRR_Targets.Require(CachedTypes.RerailControllerType, "CalculatePrice");

        // Annahme: Rueckgabewert ist float. Falls die Spielversion hier int nutzt, bitte melden.
        static void Postfix(ref float __result)
        {
            if (Main.CurrentAllowSource != AllowSource.Crane) return;

            float vanillaTotal = __result;
            float newTotal = RRR_Pricing.ApplyCraneMultipliersToVanillaTotal(vanillaTotal);
            __result = newTotal;
        }
    }
}
