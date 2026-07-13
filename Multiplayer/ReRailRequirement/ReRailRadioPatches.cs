#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ReRailRequirement
{
    // ---- Spezifische Patches fuer Reichweite + Kontext ----
    [HarmonyPatch]
    internal static class Patch_RerailController_OnUpdate_Transpiler
    {
        static MethodBase TargetMethod()
            => RRR_Targets.Require(CachedTypes.RerailControllerType, "OnUpdate");

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var getRange = AccessTools.Method(typeof(RRR_Config), nameof(RRR_Config.GetSignalRange));
            for (int i = 0; i < code.Count; i++)
            {
                var ins = code[i];
                if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && Math.Abs(f - 100f) < 0.0001f)
                {
                    code[i] = new CodeInstruction(OpCodes.Call, getRange);
                }
            }
            return code;
        }
    }

    [HarmonyPatch]
    internal static class Patch_RerailController_OnUse_Transpiler
    {
        static MethodBase TargetMethod()
            => RRR_Targets.Require(CachedTypes.RerailControllerType, "OnUse");

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var getRange = AccessTools.Method(typeof(RRR_Config), nameof(RRR_Config.GetSignalRange));
            for (int i = 0; i < code.Count; i++)
            {
                var ins = code[i];
                if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && Math.Abs(f - 100f) < 0.0001f)
                {
                    code[i] = new CodeInstruction(OpCodes.Call, getRange);
                }
            }
            return code;
        }
    }

    [HarmonyPatch]
    internal static class Patch_RerailController_OnUpdate_Context
    {
        static MethodBase TargetMethod()
            => RRR_Targets.Require(CachedTypes.RerailControllerType, "OnUpdate");
        static void Prefix()    => RRR_Context.EnterRadioRerail();
        static void Finalizer() => RRR_Context.ExitRadioRerail();
    }

    [HarmonyPatch]
    internal static class Patch_RerailController_OnUse_Context
    {
        static MethodBase TargetMethod()
            => RRR_Targets.Require(CachedTypes.RerailControllerType, "OnUse");
        static void Prefix()    => RRR_Context.EnterRadioRerail();
        static void Finalizer() => RRR_Context.ExitRadioRerail();
    }
}
