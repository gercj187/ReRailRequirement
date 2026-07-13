using UnityModManagerNet;

namespace ReRailRequirement
{
    public class Settings : UnityModManager.ModSettings
    {
        // ===== Vanilla-Modus =====
        // Standard: 25 m (harte Klammer 10..100 in main.cs)
        public float maxDistanceMeters = 50f;

        // ===== BreakdownCrane-Modus =====
        // Distanz Spieler <-> DM1U (harte Klammer 5..25 in main.cs)
        public float bc_distanceToDM1U_m = 10f;

        // Reichweite Funk/Raycast im RerailController (harte Klammer 10..50 in main.cs)
        public float bc_rerailRange_m = 25f;

        // Max. Gewicht pro Fahrzeug in Tonnen (harte Klammer 10..50 in main.cs)
        public float bc_maxWeight_t = 35f;

        // ===== BreakdownCrane-spezifische Optionen =====
        // Nur im Breakdown-Modus sichtbar/aktiv:
        // 1) DM1U darf den Kran (und Flat) auch entgegen des Gewichtslimits rerailen.
        public bool bc_allowDM1U_rerailCrane = false;

        // 2) "BrakedownCrane - Expansion" Preis-Multiplikatoren (nur im Kran-Flow; DM1U bleibt Vanilla)
        public float bcx_basePriceMul = 2.5f;         // 1..5
        public float bcx_pricePerMeterMul = 2.5f;     // 1..5

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
