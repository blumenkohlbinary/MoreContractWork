using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using URandom = UnityEngine.Random;

namespace MoreContractWork
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID    = "tobi.Mad_Games_Tycoon_2.plugins.MoreContractWork";
        public const string PLUGIN_NAME    = "More Contract Work";
        public const string PLUGIN_VERSION = "1.1.0";

        internal static new ManualLogSource Logger;

        // ---- Config --------------------------------------------------------

        public static ConfigEntry<int>   MaxContracts;
        public static ConfigEntry<float> SpawnThreshold;
        public static ConfigEntry<int>   OfferLifetimeWeeks;
        public static ConfigEntry<int>   ContractsPerWeek;
        public static ConfigEntry<float> RewardMultiplier;
        public static ConfigEntry<float> PenaltyMultiplier;

        // ---- Awake ---------------------------------------------------------

        private Harmony harmony;

        private void Awake()
        {
            Logger = base.Logger;

            // --- Auftragsvolumen ------------------------------------------
            MaxContracts = Config.Bind(
                "Auftragsarbeit", "Max gleichzeitige Auftraege", 40,
                new ConfigDescription(
                    "Maximale Anzahl gleichzeitiger Auftraege (Vanilla: 20)",
                    new AcceptableValueRange<int>(5, 200)));

            ContractsPerWeek = Config.Bind(
                "Auftragsarbeit", "Neue Auftraege pro Woche", 5,
                new ConfigDescription(
                    "Wie viele neue Auftraege pro Woche versucht werden zu spawnen.\n" +
                    "Deckt alle Studio-Typen ab: Entwicklung, QA, Grafik, Sound, Produktion, Werkstatt, Konsolenentwicklung.\n" +
                    "Vanilla: 1 (mit ~20%% Chance)",
                    new AcceptableValueRange<int>(1, 30)));

            SpawnThreshold = Config.Bind(
                "Auftragsarbeit", "Spawn-Schwellenwert", 40f,
                new ConfigDescription(
                    "Steuert den 1. Vanilla-Spawn: niedriger = hoehere Chance (Vanilla: 80).\n" +
                    "Die zusaetzlichen Auftraege (Neue Auftraege pro Woche) ignorieren diesen Wert.",
                    new AcceptableValueRange<float>(0f, 100f)));

            OfferLifetimeWeeks = Config.Bind(
                "Auftragsarbeit", "Angebots-Lebensdauer Wochen", 32,
                new ConfigDescription(
                    "Wochen bevor nicht-angenommene Auftraege verschwinden koennen (Vanilla: 16)",
                    new AcceptableValueRange<int>(4, 200)));

            // --- Verguetung -----------------------------------------------
            RewardMultiplier = Config.Bind(
                "Verguetung", "Verguetungs-Multiplikator", 1.0f,
                new ConfigDescription(
                    "Multiplikator fuer Auftragsverguetung (1.0 = Vanilla)",
                    new AcceptableValueRange<float>(0.1f, 10.0f)));

            PenaltyMultiplier = Config.Bind(
                "Verguetung", "Strafen-Multiplikator", 1.0f,
                new ConfigDescription(
                    "Multiplikator fuer Strafzahlung bei Vertragsbruch (1.0 = Vanilla)",
                    new AcceptableValueRange<float>(0.0f, 5.0f)));

            // ---- Harmony patchen -----------------------------------------
            harmony = new Harmony(PLUGIN_GUID);
            try
            {
                harmony.PatchAll();
                Logger.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} geladen!");
                Logger.LogInfo($"  Max Auftraege : {MaxContracts.Value} (Vanilla: 20)");
                Logger.LogInfo($"  Pro Woche     : {ContractsPerWeek.Value} (Vanilla: ~1)");
                Logger.LogInfo($"  Spawn-Schwelle: {SpawnThreshold.Value} (Vanilla: 80)");
                Logger.LogInfo($"  Lebensdauer   : {OfferLifetimeWeeks.Value} Wochen (Vanilla: 16)");
                Logger.LogInfo($"  Verguetung    : x{RewardMultiplier.Value} | Strafe: x{PenaltyMultiplier.Value}");
            }
            catch (Exception ex)
            {
                Logger.LogError("Fehler beim Patchen: " + ex.Message);
                Logger.LogError(ex.StackTrace);
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }

    // ========================================================================
    // TRANSPILER: contractWorkMain.UpdateContractWork
    //
    // Aendert 3 Konstanten im IL-Code des Vanilla-Spawners:
    //   (1) ldc.i4.s 20 vor bge  -> MaxContracts
    //   (2) ldc.r4 80f            -> SpawnThreshold
    //   (3) ldc.i4.s 16 vor ble  -> OfferLifetimeWeeks
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
                // PATCH 1: Max Auftraege  (conv.i4 -> ldc.i4.s 20 -> bge/bge.s)
                if (i >= 1 && i < codes.Count - 1
                    && codes[i - 1].opcode == OpCodes.Conv_I4
                    && codes[i].opcode     == OpCodes.Ldc_I4_S
                    && Convert.ToInt32(codes[i].operand) == 20
                    && (codes[i + 1].opcode == OpCodes.Bge_S || codes[i + 1].opcode == OpCodes.Bge))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call,
                        typeof(Patch_UpdateContractWork).GetMethod("GetMaxContracts"));
                    patched++;
                }

                // PATCH 2: Spawn-Schwellenwert  (einzige ldc.r4 80f in der Methode)
                if (codes[i].opcode == OpCodes.Ldc_R4
                    && codes[i].operand is float fVal
                    && Math.Abs(fVal - 80f) < 0.01f)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call,
                        typeof(Patch_UpdateContractWork).GetMethod("GetSpawnThreshold"));
                    patched++;
                }

                // PATCH 3: Angebots-Lebensdauer  (ldc.i4.s 16 -> ble/ble.s)
                if (i < codes.Count - 1
                    && codes[i].opcode == OpCodes.Ldc_I4_S
                    && Convert.ToInt32(codes[i].operand) == 16
                    && (codes[i + 1].opcode == OpCodes.Ble_S || codes[i + 1].opcode == OpCodes.Ble))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call,
                        typeof(Patch_UpdateContractWork).GetMethod("GetOfferLifetime"));
                    patched++;
                }
            }

            if (patched >= 3)
                Plugin.Logger.LogInfo($"Transpiler: Alle {patched} Patches gesetzt.");
            else
                Plugin.Logger.LogWarning($"Transpiler: Nur {patched}/3 Patches! Game-Version inkompatibel?");

            return codes;
        }

        public static int   GetMaxContracts()    => Plugin.MaxContracts.Value;
        public static float GetSpawnThreshold()  => Plugin.SpawnThreshold.Value;
        public static int   GetOfferLifetime()   => Plugin.OfferLifetimeWeeks.Value;
    }

    // ========================================================================
    // POSTFIX: Zusaetzliche Auftraege pro Woche spawnen
    //
    // Vanilla erstellt maximal 1 Auftrag pro UpdateContractWork-Aufruf.
    // Dieser Postfix erstellt bis zu (ContractsPerWeek - 1) weitere,
    // indem er die exakt gleiche Initialisierungslogik aus dem Vanilla-IL
    // nachbaut. Alle contractWork-Felder sind public; nur die privaten
    // contractWorkMain-Felder werden via Harmony Traverse zugegriffen.
    //
    // Unterstuetzte Auftragstypen (art):
    //   0 = Entwicklung (immer verfuegbar)
    //   1 = QA          (Forschung 28)
    //   2 = Grafikstudio(Forschung 31)
    //   3 = Soundstudio (Forschung 32)
    //   4 = Hardware    (Unlock 8)
    //   5 = Produktion  (Forschung 33)
    //   6 = Werkstatt   (Forschung 38)
    //   7 = Motion/etc. (Forschung 39)
    // ========================================================================
    [HarmonyPatch(typeof(contractWorkMain), "UpdateContractWork")]
    public class Patch_SpawnExtra
    {
        [HarmonyPostfix]
        public static void Postfix(contractWorkMain __instance)
        {
            int extra = Plugin.ContractsPerWeek.Value - 1;
            if (extra <= 0) return;

            // Private Felder per Traverse holen (einmalig, gecacht fuer diese Methode)
            var tv           = Traverse.Create(__instance);
            var forschung    = tv.Field<forschungSonstiges>("forschungSonstiges_").Value;
            var unlock       = tv.Field<unlockScript>("unlock_").Value;
            var tS           = tv.Field<textScript>("tS_").Value;
            var mS           = tv.Field<mainScript>("mS_").Value;

            if (forschung == null || unlock == null || tS == null || mS == null)
            {
                Plugin.Logger.LogWarning("SpawnExtra: Konnte private Felder nicht lesen – ueberspringe.");
                return;
            }

            int spawned = 0;
            for (int i = 0; i < extra; i++)
            {
                // Maximumscheck (erneut, da gerade gespawnte ebenfalls zaehlen)
                var allContracts = GameObject.FindGameObjectsWithTag("ContractWork");
                if (allContracts.Length >= Plugin.MaxContracts.Value) break;

                // ---- Neues contractWork-Objekt erstellen -------------------
                contractWork cw = __instance.CreateContractWork();
                if (cw == null) continue;

                // ---- myID (zufaellig eindeutig) ----------------------------
                cw.myID = URandom.Range(1, 999999999);

                // ---- art (Auftragstyp) bestimmen ---------------------------
                // Vanilla: URandom.Range(0, 8) -> Typ pruefen ob freigeschaltet
                // Falls nicht freigeschaltet: art bleibt 0 (Entwicklung)
                int art = 0;
                switch (URandom.Range(0, 8))
                {
                    case 0: art = 0; break;
                    case 1: art = forschung.IsErforscht(28) ? 1 : 0; break; // QA
                    case 2: art = forschung.IsErforscht(31) ? 2 : 0; break; // Grafik
                    case 3: art = forschung.IsErforscht(32) ? 3 : 0; break; // Sound
                    case 4: art = unlock.Get(8)             ? 4 : 0; break; // Hardware/Konsole
                    case 5: art = forschung.IsErforscht(33) ? 5 : 0; break; // Produktion
                    case 6: art = forschung.IsErforscht(38) ? 6 : 0; break; // Werkstatt
                    case 7: art = forschung.IsErforscht(39) ? 7 : 0; break; // Motion
                }
                cw.art             = art;
                cw.angenommen      = false;
                cw.wochenAlsAngebot = 0;

                // ---- typ (Auftragsuntertyp) --------------------------------
                // art 5 (Produktion) und art 6 (Werkstatt) haben keinen randomisierten typ
                if (art != 5 && art != 6)
                    cw.typ = tS.GetRandomContractNumber(art);

                // ---- Punkte (Aufwand / Groesse) ----------------------------
                // Vanilla: 20 * Random.Range(10, 30 + RoundToInt(auftragsAnsehen * 5))
                int repBonus = Mathf.RoundToInt(mS.auftragsAnsehen * 5f);
                cw.points = 20 * URandom.Range(10, 30 + repBonus);

                // ---- Gehaltsberechnung -------------------------------------
                cw.gehalt = Mathf.RoundToInt(cw.points * URandom.Range(10f, 40f));

                // ---- Strafzahlung ------------------------------------------
                cw.strafe = Mathf.RoundToInt(URandom.Range(cw.gehalt * 0.7f, cw.gehalt * 1.8f));

                // ---- Auftraggeber ------------------------------------------
                cw.auftraggeberID = tv.Method("GetRandomPublisherID").GetValue<int>();

                // ---- Spezial: typ 25 erfordert Unlock 31 (sonst typ 6) -----
                if (cw.typ == 25 && !unlock.Get(31))
                    cw.typ = 6;

                // ---- Typ-spezifische Punktekorrektur (switch art-1 in IL) --
                switch (art)
                {
                    case 1: // QA
                        cw.points *= 0.8f;
                        break;
                    case 2: // Grafik
                        cw.points *= 0.6f;
                        break;
                    case 3: // Sound
                        cw.points *= 0.4f;
                        break;
                    case 4: // Hardware
                        cw.points *= 0.3f;
                        break;
                    case 5: // Produktion (Sonderfall: grosse Punkte, auf 100 gerundet)
                        cw.points *= 1000f;
                        cw.points  = (Mathf.RoundToInt(cw.points) / 100) * 100;
                        break;
                    case 6: // Werkstatt
                        cw.points *= 0.8f;
                        break;
                    case 7: // Motion / Sonstiges
                        cw.points *= 0.8f;
                        cw.points *= __instance.pointsRegulator;
                        break;
                    // art=0 (Entwicklung): keine Korrektur
                }

                // ---- Zeitlimit (Wochen bis Frist) --------------------------
                if (art != 5)
                    cw.zeitInWochen = Mathf.RoundToInt(cw.points / 50f    + URandom.Range(5, 10));
                else
                    cw.zeitInWochen = Mathf.RoundToInt(cw.points / 20000f + URandom.Range(5, 10));

                // ---- Kein Auftraggeber -> Objekt wegwerfen, sonst init -----
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
                Plugin.Logger.LogDebug($"SpawnExtra: {spawned} zusaetzliche Auftraege erstellt.");
        }
    }

    // ========================================================================
    // POSTFIX: Verguetung skalieren
    // contractWork.GetGehalt() -> Rueckgabewert * Multiplikator
    // ========================================================================
    [HarmonyPatch(typeof(contractWork), "GetGehalt")]
    public class Patch_GetGehalt
    {
        [HarmonyPostfix]
        public static void Postfix(ref int __result)
        {
            if (Math.Abs(Plugin.RewardMultiplier.Value - 1.0f) > 0.01f)
                __result = (int)(__result * Plugin.RewardMultiplier.Value);
        }
    }

    // ========================================================================
    // POSTFIX: Strafzahlung skalieren
    // contractWork.GetStrafe() -> Rueckgabewert * Multiplikator
    // ========================================================================
    [HarmonyPatch(typeof(contractWork), "GetStrafe")]
    public class Patch_GetStrafe
    {
        [HarmonyPostfix]
        public static void Postfix(ref int __result)
        {
            if (Math.Abs(Plugin.PenaltyMultiplier.Value - 1.0f) > 0.01f)
                __result = (int)(__result * Plugin.PenaltyMultiplier.Value);
        }
    }
}
