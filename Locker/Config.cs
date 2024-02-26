using BepInEx.Configuration;

namespace Locker
{
    public class Config
    {
        public static ConfigEntry<int> LockerSpawnPower;
        public static ConfigEntry<string> LockerSpawnLevels;

        public static void Load()
        {
            LockerSpawnPower = Plugin.config.Bind(
                "Spawning",
                "LockerSpawnPower",
                25,
                new ConfigDescription(
                    "Set the chance of the navigation malfunction happening - this will force the ship to route to a random moon with no regard to cost",
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
