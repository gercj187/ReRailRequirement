#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using DV;

namespace ReRailRequirement
{
    /// <summary>
    /// Zentraler Type-Cache, damit teure Reflection-Scans nur einmal stattfinden.
    /// </summary>
    internal static class CachedTypes
    {
        internal static readonly Type RerailControllerType;

        static CachedTypes()
        {
            // Einmalige Typ-Resolution. Danach nur noch Verwendung aus dem Cache.
            RerailControllerType = RRR_Targets.ResolveRerailControllerType();
            Debug.Log("[ReRailRequirement] Cached RerailControllerType = " + RerailControllerType.FullName);
        }
    }

    internal static class RRR_Config
    {
        private static Func<bool>? _isBCActive;
        private static Func<float>? _rangeProvider;

        public static void Setup(Func<bool> isBCActive, Func<float> rangeProvider)
        {
            _isBCActive = isBCActive;
            _rangeProvider = rangeProvider;
        }

        public static float GetSignalRange()
        {
            // Nur im Radio-Rerail + DM1U-Pfad darf die Mod die Reichweite anpassen. Im Crane-Pfad IMMER 100f.
            if (RRR_Context.InRadioRerail && Main.CurrentAllowSource == AllowSource.DM1U)
            {
                float v = _rangeProvider != null ? _rangeProvider() : 100f;
                return Mathf.Clamp(v, 1f, 1000f);
            }
            return 100f;
        }
    }

    internal static class RRR_Context
    {
        [ThreadStatic] private static bool _inRadioRerail;
        public static bool InRadioRerail => _inRadioRerail;

        public static void EnterRadioRerail() { _inRadioRerail = true; }
        public static void ExitRadioRerail()  { _inRadioRerail = false; }
    }

    internal static class RRR_Targets
    {
        public static Assembly? GetAssemblyCS()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal));
        }

        public static Type ResolveRerailControllerType()
        {
            // Bevorzugt Assembly-CSharp (schneller, gezielter).
            var asmCS = GetAssemblyCS();
            if (asmCS != null)
            {
                var t = asmCS.GetType("RerailController") ?? asmCS.GetType("DV.RerailController");
                if (t != null) return t;
            }

            // Fallback: breite Suche ueber alle Assemblies (passiert nun nur noch einmal zur Cache-Initialisierung).
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var tt in asm.GetTypes())
                    {
                        if (tt.Name == "RerailController" && typeof(ICommsRadioMode).IsAssignableFrom(tt))
                            return tt;
                    }
                }
                catch { }
            }
            throw new InvalidOperationException("RerailController type not found.");
        }

        public static MethodBase Require(Type t, string methodName)
        {
            var m = AccessTools.Method(t, methodName);
            if (m == null)
                throw new InvalidOperationException($"{t.FullName}.{methodName} not found");
            return m;
        }
    }

    // ---- Pricing helper (only in CRANE flow) ----
    internal static class RRR_Pricing
    {
        internal const float VANILLA_BASE = 500f;
        internal const float VANILLA_PPM  = 150f;

        private static Settings? GetSettings()
        {
            try
            {
                var mainType = typeof(Settings).Assembly.GetType("ReRailRequirement.Main");
                var f = AccessTools.Field(mainType, "settings");
                return f?.GetValue(null) as Settings;
            }
            catch { return null; }
        }

        public static float GetBaseRerailPrice()
        {
            // Vanilla base 500f; multiply only in Crane mode
            if (Main.CurrentAllowSource != AllowSource.Crane) return VANILLA_BASE;

            var s = GetSettings();
            if (s == null) return VANILLA_BASE;

            float mul = Mathf.Clamp(s.bcx_basePriceMul, 1f, 5f);
            return VANILLA_BASE * mul;
        }

        public static float GetPricePerMeter()
        {
            // Vanilla per-meter 150f; multiply only in Crane mode
            if (Main.CurrentAllowSource != AllowSource.Crane) return VANILLA_PPM;

            var s = GetSettings();
            if (s == null) return VANILLA_PPM;

            float mul = Mathf.Clamp(s.bcx_pricePerMeterMul, 1f, 5f);
            return VANILLA_PPM * mul;
        }

        public static float ApplyCraneMultipliersToVanillaTotal(float vanillaTotal)
        {
            // Rekonstruiere Distanz D aus Vanilla: total = 500 + 150 * D
            float D = Mathf.Max(0f, (vanillaTotal - VANILLA_BASE) / VANILLA_PPM);

            var s = GetSettings();
            if (s == null) return vanillaTotal;

            float baseMul = Mathf.Clamp(s.bcx_basePriceMul, 1f, 5f);
            float ppmMul  = Mathf.Clamp(s.bcx_pricePerMeterMul, 1f, 5f);

            float newTotal = VANILLA_BASE * baseMul + VANILLA_PPM * ppmMul * D;
            return Mathf.Max(0f, newTotal);
        }
    }

    // ---- Utility: Method-Suche & Transpiler-Anwendung ----
    internal static class RRR_TranspileUtils
    {
        private const bool PRINT_PRICE_PATCH_LOGS = true;

        public static IEnumerable<MethodBase> EnumerateTypeAndNestedMethods(Type t)
        {
            var stack = new Stack<Type>();
            stack.Push(t);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();

                var methods = cur.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                                 .Where(m => m.GetMethodBody() != null && !m.IsAbstract && !m.IsGenericMethodDefinition);
                foreach (var m in methods)
                    yield return m;

                foreach (var nt in cur.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    stack.Push(nt);
            }
        }

        public static void PatchMethods(Harmony harmony, IEnumerable<MethodBase> methods, MethodInfo transpilerMI, string tag)
        {
            int count = 0;
            foreach (var m in methods)
            {
                try
                {
                    // WICHTIG: CalculatePrice ueberspringen – wird per Postfix angepasst
                    if (string.Equals(m.Name, "CalculatePrice", StringComparison.Ordinal))
                        continue;

                    harmony.Patch(m, transpiler: new HarmonyMethod(transpilerMI));
                    count++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RRR] {tag}: Patch failed for {m.DeclaringType?.FullName}.{m.Name}: {e.Message}");
                }
            }
            if (PRINT_PRICE_PATCH_LOGS)
                Debug.Log($"[RRR] {tag}: Patched methods = {count}");
        }
    }

    // ---- Transpiler fuer 500/150 (breit), aber ohne CalculatePrice ----
    internal static class RRR_MassPriceTranspiler
    {
        private static readonly MethodInfo MI_GetBase = AccessTools.Method(typeof(RRR_Pricing), nameof(RRR_Pricing.GetBaseRerailPrice));
        private static readonly MethodInfo MI_GetPPM  = AccessTools.Method(typeof(RRR_Pricing), nameof(RRR_Pricing.GetPricePerMeter));
        private static readonly MethodInfo MI_ThisTranspiler = AccessTools.Method(typeof(RRR_MassPriceTranspiler), nameof(Transpiler));

        public static void ApplyBroad(Harmony harmony)
        {
            // Verwende den gecachten Typ.
            var rc = CachedTypes.RerailControllerType;
            var rcMethods = RRR_TranspileUtils.EnumerateTypeAndNestedMethods(rc);
            RRR_TranspileUtils.PatchMethods(harmony, rcMethods, MI_ThisTranspiler, "MassPrice RC");

            var asm = RRR_Targets.GetAssemblyCS();
            if (asm != null)
            {
                var allRerailTypes = asm.GetTypes().Where(t =>
                    t != rc && t.Name.IndexOf("Rerail", StringComparison.OrdinalIgnoreCase) >= 0);

                foreach (var t in allRerailTypes)
                {
                    var methods = RRR_TranspileUtils.EnumerateTypeAndNestedMethods(t);
                    RRR_TranspileUtils.PatchMethods(harmony, methods, MI_ThisTranspiler, $"MassPrice {t.FullName}");
                }
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            for (int i = 0; i < code.Count; i++)
            {
                var ins = code[i];

                // direkte float-Konstanten
                if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f)
                {
                    if (Math.Abs(f - RRR_Pricing.VANILLA_BASE) < 0.0001f)
                    {
                        yield return new CodeInstruction(OpCodes.Call, MI_GetBase);
                        continue;
                    }
                    if (Math.Abs(f - RRR_Pricing.VANILLA_PPM) < 0.0001f)
                    {
                        yield return new CodeInstruction(OpCodes.Call, MI_GetPPM);
                        continue;
                    }
                }

                // Muster: ldc.i4(500|150) (+ evtl. ldc.i4.s 150) gefolgt von conv.r4
                bool isIntBase =
                    (ins.opcode == OpCodes.Ldc_I4 && ins.operand is int i500 && i500 == (int)RRR_Pricing.VANILLA_BASE);
                bool isIntPPM =
                    (ins.opcode == OpCodes.Ldc_I4 && ins.operand is int i150 && i150 == (int)RRR_Pricing.VANILLA_PPM) ||
                    (ins.opcode == OpCodes.Ldc_I4_S && ins.operand is sbyte s150 && (int)s150 == (int)RRR_Pricing.VANILLA_PPM);

                if ((isIntBase || isIntPPM) && (i + 1) < code.Count && code[i + 1].opcode == OpCodes.Conv_R4)
                {
                    if (isIntBase)
                    {
                        yield return new CodeInstruction(OpCodes.Call, MI_GetBase);
                        continue;
                    }
                    if (isIntPPM)
                    {
                        yield return new CodeInstruction(OpCodes.Call, MI_GetPPM);
                        continue;
                    }
                }

                yield return ins;
            }
        }
    }
}
