using UnityModManagerNet;

namespace ReRailRequirement
{
    public class Settings : UnityModManager.ModSettings
    {
        // Standard: 50 m
        public float maxDistanceMeters = 25f;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
