using BepInEx.Configuration;

namespace Locker
{
    public class Config
    {
        public static ConfigEntry<int> LockerSpawnWeight;
        public static ConfigEntry<string> LockerSpawnLevels;

        public static void Load()
        {
            LockerSpawnWeight = Plugin.config.Bind(
                "Spawning",
                "LockerSpawnWeight",
                25,
                new ConfigDescription(
                    "What is the chance of the Locker spawning - higher values make it more common",
                    new AcceptableValueRange<int>(0, 300)
                )
            );
            /*
            LockerSpawnLevels = Plugin.config.Bind(
                "Spawning",
                "LockerSpawnLevels",
                "",
                new ConfigDescription(
                    "Set the chance of the teleporter malfunction happening - this will cause teleporters to disable themselves either at landing or after a random interval into the match",
                    new AcceptableValueRange<double>(0, 100)
                )
            );
            */
        }
    }
}
