using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using URandom = UnityEngine.Random;

namespace MoreContractWork
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInProcess("Mad Games Tycoon 2.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID    = "tobi.Mad_Games_Tycoon_2.plugins.MoreContractWork";
        public const string PLUGIN_NAME    = "More Contract Work";
        public const string PLUGIN_VERSION = "1.3.0";

        internal static new ManualLogSource Logger;

        // ---- Config --------------------------------------------------------

        public static ConfigEntry<bool>  Enabled;
        public static ConfigEntry<int>   MaxContracts;
        public static ConfigEntry<float> SpawnThreshold;
        public static ConfigEntry<int>   OfferLifetimeWeeks;
        public static ConfigEntry<int>   ContractsPerWeek;
        public static ConfigEntry<float> RewardMultiplier;
        public static ConfigEntry<float> PenaltyMultiplier;

        private Harmony harmony;

        private void Awake()
        {
            Logger = base.Logger;

            Enabled = Config.Bind(
                "General", "Enabled", true,
                "Enable or disable the mod entirely.");

            // --- Contract Volume ------------------------------------------
            MaxContracts = Config.Bind(
                "Contract Work", "Max Active Contracts", 40,
                new ConfigDescription(
                    "Maximum number of simultaneous contract offers on the board (Vanilla: 20)",
                    new AcceptableValueRange<int>(5, 200)));

            ContractsPerWeek = Config.Bind(
                "Contract Work", "Contracts Per Week", 5,
                new ConfigDescription(
                    "How many new contract offers attempt to spawn per in-game week.\n" +
                    "Covers all studio types: Development, QA, Graphics, Sound, Production, Workshop, Console Dev.\n" +
                    "Vanilla: 1 (with ~20% chance)",
                    new AcceptableValueRange<int>(1, 30)));

            SpawnThreshold = Config.Bind(
                "Contract Work", "Spawn Threshold", 40f,
                new ConfigDescription(
                    "Controls the vanilla spawn chance: lower = higher chance (Vanilla: 80).\n" +
                    "Additional contracts (Contracts Per Week) ignore this value.",
                    new AcceptableValueRange<float>(0f, 100f)));

            OfferLifetimeWeeks = Config.Bind(
                "Contract Work", "Offer Lifetime Weeks", 32,
                new ConfigDescription(
                    "Weeks before unaccepted contract offers can expire (Vanilla: 16)",
                    new AcceptableValueRange<int>(4, 200)));

            // --- Payment --------------------------------------------------
            RewardMultiplier = Config.Bind(
                "Payment", "Reward Multiplier", 1.0f,
                new ConfigDescription(
                    "Multiplier applied to contract payment (1.0 = vanilla)",
                    new AcceptableValueRange<float>(0.1f, 10.0f)));

            PenaltyMultiplier = Config.Bind(
                "Payment", "Penalty Multiplier", 1.0f,
                new ConfigDescription(
                    "Multiplier applied to penalty on contract breach (1.0 = vanilla)",
                    new AcceptableValueRange<float>(0.0f, 5.0f)));

            // ---- Harmony patching ----------------------------------------
            harmony = new Harmony(PLUGIN_GUID);
            try
            {
                harmony.PatchAll();
                Logger.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} loaded!");
                Logger.LogInfo($"  Max contracts : {MaxContracts.Value} (Vanilla: 20)");
                Logger.LogInfo($"  Per week      : {ContractsPerWeek.Value} (Vanilla: ~1)");
                Logger.LogInfo($"  Spawn thresh. : {SpawnThreshold.Value} (Vanilla: 80)");
                Logger.LogInfo($"  Offer lifetime: {OfferLifetimeWeeks.Value} weeks (Vanilla: 16)");
                Logger.LogInfo($"  Reward: x{RewardMultiplier.Value} | Penalty: x{PenaltyMultiplier.Value}");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error during patching: " + ex.Message);
                Logger.LogError(ex.StackTrace);
            }
        }

        private void OnDestroy() => harmony?.UnpatchSelf();
    }

    // ========================================================================
    // TRANSPILER: contractWorkMain.UpdateContractWork
    //
    // Replaces 3 constants in the vanilla spawner IL code:
    //   (1) ldc.i4.s 20 before bge  -> MaxContracts
    //   (2) ldc.r4 80f (threshold)  -> SpawnThreshold
    //   (3) ldc.i4.s 16 before ble  -> OfferLifetimeWeeks
    //
    // Helper methods return vanilla defaults when Enabled = false.
    // ========================================================================
    [HarmonyPatch(typeof(contractWorkMain), "UpdateContractWork")]
    public class Patch_UpdateContractWork
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes   = new List<CodeInstruction>(instructions);
            int patched = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                // PATCH 1: Max contracts  (conv.i4 -> ldc.i4.s 20 -> bge/bge.s)
                if (i >= 1 && i < codes.Count - 1
                    && codes[i - 1].opcode == OpCodes.Conv_I4
                    && codes[i].opcode     == OpCodes.Ldc_I4_S
                    && Convert.ToInt32(codes[i].operand) == 20
                    && (codes[i + 1].opcode == OpCodes.Bge_S || codes[i + 1].opcode == OpCodes.Bge))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call,
                        typeof(Patch_UpdateContractWork).GetMethod(nameof(GetMaxContracts)));
                    patched++;
                }

                // PATCH 2: Spawn threshold  (ldc.r4 80f followed by comparison, not arithmetic)
                if (codes[i].opcode == OpCodes.Ldc_R4
                    && codes[i].operand is float fVal
                    && Math.Abs(fVal - 80f) < 0.01f)
                {
                    // Context check: skip if next opcode is arithmetic (80f used in calculation)
                    if (i + 1 < codes.Count && IsArithmeticOp(codes[i + 1].opcode))
                        continue;

                    codes[i] = new CodeInstruction(OpCodes.Call,
                        typeof(Patch_UpdateContractWork).GetMethod(nameof(GetSpawnThreshold)));
                    patched++;
                }

                // PATCH 3: Offer lifetime  (ldc.i4.s 16 -> ble/ble.s)
                if (i < codes.Count - 1
                    && codes[i].opcode == OpCodes.Ldc_I4_S
                    && Convert.ToInt32(codes[i].operand) == 16
                    && (codes[i + 1].opcode == OpCodes.Ble_S || codes[i + 1].opcode == OpCodes.Ble))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call,
                        typeof(Patch_UpdateContractWork).GetMethod(nameof(GetOfferLifetime)));
                    patched++;
                }
            }

            if (patched >= 3)
                Plugin.Logger.LogInfo($"Transpiler: All {patched} patches applied.");
            else
                Plugin.Logger.LogWarning($"Transpiler: Only {patched}/3 patches! Game version incompatible?");

            return codes;
        }

        private static bool IsArithmeticOp(OpCode op)
        {
            return op == OpCodes.Mul || op == OpCodes.Div
                || op == OpCodes.Add || op == OpCodes.Sub
                || op == OpCodes.Rem;
        }

        // Return vanilla defaults when mod is disabled
        public static int   GetMaxContracts()   => Plugin.Enabled.Value ? Plugin.MaxContracts.Value : 20;
        public static float GetSpawnThreshold() => Plugin.Enabled.Value ? Plugin.SpawnThreshold.Value : 80f;
        public static int   GetOfferLifetime()  => Plugin.Enabled.Value ? Plugin.OfferLifetimeWeeks.Value : 16;
    }

    // ========================================================================
    // POSTFIX: Spawn additional contracts per week
    //
    // Vanilla creates at most 1 contract per UpdateContractWork call.
    // This postfix creates up to (ContractsPerWeek - 1) additional ones
    // by replicating the exact initialization logic from the vanilla IL.
    //
    // All contractWork fields are public; the private contractWorkMain
    // fields are accessed via cached FieldInfo/MethodInfo (no Traverse).
    //
    // Supported contract types (art):
    //   0 = Development   (always available)
    //   1 = QA            (Research 28)
    //   2 = Graphics      (Research 31)
    //   3 = Sound         (Research 32)
    //   4 = Hardware      (Unlock 8)
    //   5 = Production    (Research 33)
    //   6 = Workshop      (Research 38)
    //   7 = Motion/etc.   (Research 39)
    // ========================================================================
    [HarmonyPatch(typeof(contractWorkMain), "UpdateContractWork")]
    public class Patch_SpawnExtra
    {
        // Cached reflection metadata (populated once in static ctor)
        private static readonly FieldInfo  _fForschung;
        private static readonly FieldInfo  _fUnlock;
        private static readonly FieldInfo  _fTS;
        private static readonly FieldInfo  _fMS;
        private static readonly MethodInfo _mGetPublisher;
        private static readonly object[]   _noArgs = new object[0];

        static Patch_SpawnExtra()
        {
            const BindingFlags priv = BindingFlags.NonPublic | BindingFlags.Instance;
            var t = typeof(contractWorkMain);
            _fForschung    = t.GetField("forschungSonstiges_", priv);
            _fUnlock       = t.GetField("unlock_", priv);
            _fTS           = t.GetField("tS_", priv);
            _fMS           = t.GetField("mS_", priv);
            _mGetPublisher = t.GetMethod("GetRandomPublisherID", priv);
        }

        [HarmonyPostfix]
        public static void Postfix(contractWorkMain __instance, bool forceNewContract)
        {
            try
            {
                if (!Plugin.Enabled.Value) return;

                // forceNewContract = special event / tutorial -> don't pile on extras
                if (forceNewContract) return;

                int extra = Plugin.ContractsPerWeek.Value - 1;
                if (extra <= 0) return;

                // Verify cached reflection is valid
                if (_fForschung == null || _fUnlock == null
                    || _fTS == null || _fMS == null || _mGetPublisher == null)
                {
                    Plugin.Logger.LogWarning("SpawnExtra: Reflection cache incomplete - game API changed?");
                    return;
                }

                var forschung = (forschungSonstiges)_fForschung.GetValue(__instance);
                var unlock    = (unlockScript)_fUnlock.GetValue(__instance);
                var tS        = (textScript)_fTS.GetValue(__instance);
                var mS        = (mainScript)_fMS.GetValue(__instance);

                if (forschung == null || unlock == null || tS == null || mS == null)
                {
                    Plugin.Logger.LogWarning("SpawnExtra: Private field values are null - skipping.");
                    return;
                }

                int currentCount = GameObject.FindGameObjectsWithTag("ContractWork").Length;
                int maxContracts = Plugin.MaxContracts.Value;

                int spawned = 0;
                for (int i = 0; i < extra; i++)
                {
                    if (currentCount + spawned >= maxContracts) break;

                    // ---- Create new contractWork object -----------------------
                    contractWork cw = __instance.CreateContractWork();
                    if (cw == null) continue;

                    // ---- Unique random ID -------------------------------------
                    cw.myID = URandom.Range(1, 999999999);

                    // ---- Determine contract type (art) ------------------------
                    int art = 0;
                    switch (URandom.Range(0, 8))
                    {
                        case 0: art = 0; break;
                        case 1: art = forschung.IsErforscht(28) ? 1 : 0; break; // QA
                        case 2: art = forschung.IsErforscht(31) ? 2 : 0; break; // Graphics
                        case 3: art = forschung.IsErforscht(32) ? 3 : 0; break; // Sound
                        case 4: art = unlock.Get(8)             ? 4 : 0; break; // Hardware
                        case 5: art = forschung.IsErforscht(33) ? 5 : 0; break; // Production
                        case 6: art = forschung.IsErforscht(38) ? 6 : 0; break; // Workshop
                        case 7: art = forschung.IsErforscht(39) ? 7 : 0; break; // Motion
                    }
                    cw.art              = art;
                    cw.angenommen       = false;
                    cw.wochenAlsAngebot = 0;

                    // ---- Contract subtype (typ) -------------------------------
                    if (art != 5 && art != 6)
                        cw.typ = tS.GetRandomContractNumber(art);

                    // ---- Points (effort / size) -------------------------------
                    int repBonus = Mathf.RoundToInt(mS.auftragsAnsehen * 5f);
                    cw.points = 20 * URandom.Range(10, 30 + repBonus);

                    // ---- Payment / Penalty ------------------------------------
                    cw.gehalt = Mathf.RoundToInt(cw.points * URandom.Range(10f, 40f));
                    cw.strafe = Mathf.RoundToInt(URandom.Range(cw.gehalt * 0.7f, cw.gehalt * 1.8f));

                    // ---- Publisher assignment (via cached MethodInfo) ----------
                    cw.auftraggeberID = (int)_mGetPublisher.Invoke(__instance, _noArgs);

                    // ---- Special: subtype 25 requires Unlock 31 ---------------
                    if (cw.typ == 25 && !unlock.Get(31))
                        cw.typ = 6;

                    // ---- Type-specific point adjustment -----------------------
                    switch (art)
                    {
                        case 1: cw.points *= 0.8f; break;                          // QA
                        case 2: cw.points *= 0.6f; break;                          // Graphics
                        case 3: cw.points *= 0.4f; break;                          // Sound
                        case 4: cw.points *= 0.3f; break;                          // Hardware
                        case 5:                                                     // Production
                            cw.points *= 1000f;
                            cw.points  = (Mathf.RoundToInt(cw.points) / 100) * 100;
                            break;
                        case 6: cw.points *= 0.8f; break;                          // Workshop
                        case 7:                                                     // Motion
                            cw.points *= 0.8f;
                            cw.points *= __instance.pointsRegulator;
                            break;
                    }

                    // ---- Deadline (weeks) -------------------------------------
                    if (art != 5)
                        cw.zeitInWochen = Mathf.RoundToInt(cw.points / 50f    + URandom.Range(5, 10));
                    else
                        cw.zeitInWochen = Mathf.RoundToInt(cw.points / 20000f + URandom.Range(5, 10));

                    // ---- No publisher -> discard, otherwise init --------------
                    if (cw.auftraggeberID == -1)
                    {
                        cw.gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(cw.gameObject);
                    }
                    else
                    {
                        cw.Init();
                        spawned++;
                    }
                }

                if (spawned > 0)
                    Plugin.Logger.LogDebug($"SpawnExtra: {spawned} additional contracts created.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"SpawnExtra: {ex.Message}");
            }
        }
    }

    // ========================================================================
    // POSTFIX: Scale contract reward
    // contractWork.GetGehalt() -> return value * multiplier
    // ========================================================================
    [HarmonyPatch(typeof(contractWork), "GetGehalt")]
    public class Patch_GetGehalt
    {
        [HarmonyPostfix]
        public static void Postfix(ref int __result)
        {
            try
            {
                if (!Plugin.Enabled.Value) return;
                if (Math.Abs(Plugin.RewardMultiplier.Value - 1.0f) > 0.01f)
                    __result = (int)(__result * Plugin.RewardMultiplier.Value);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"GetGehalt Postfix: {ex.Message}");
            }
        }
    }

    // ========================================================================
    // POSTFIX: Scale contract penalty
    // contractWork.GetStrafe() -> return value * multiplier
    // ========================================================================
    [HarmonyPatch(typeof(contractWork), "GetStrafe")]
    public class Patch_GetStrafe
    {
        [HarmonyPostfix]
        public static void Postfix(ref int __result)
        {
            try
            {
                if (!Plugin.Enabled.Value) return;
                if (Math.Abs(Plugin.PenaltyMultiplier.Value - 1.0f) > 0.01f)
                    __result = (int)(__result * Plugin.PenaltyMultiplier.Value);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"GetStrafe Postfix: {ex.Message}");
            }
        }
    }
}
