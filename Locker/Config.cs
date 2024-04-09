using BepInEx.Configuration;

namespace Locker
{
    public class Config
    {
        public static ConfigEntry<int> LockerSpawnWeight;
        public static ConfigEntry<float> LockerSpawnPower;
        public static ConfigEntry<int> LockerSpawnMax;
        public static ConfigEntry<string> LockerSpawnLevelsSet;
        public static ConfigEntry<string> LockerSpawnLevelsWithWeight;
        public static ConfigEntry<float> LockerVolumeAdjustment;
        public static ConfigEntry<float> LockerMechanicsReactivationChance;

        public static void Load()
        {
            LockerMechanicsReactivationChance = Plugin.config.Bind(
                "Mechanics",
                "LockerMechanicsReactivationChance",
                50f,
                new ConfigDescription(
                    "Chance for the Locker to reactivate after a chase and begin another lunge at the closest player (rolls a value 0-100 and if below the given value will reactivate)"
                )
            );

            LockerSpawnWeight = Plugin.config.Bind(
                "Spawn",
                "LockerSpawnWeight",
                50,
                new ConfigDescription(
                    "What is the chance of the Locker spawning - higher values make it more common (this is like adding tickets to a lottery - it doesn't guarantee getting picked but it vastly increases the chances)",
                    new AcceptableValueRange<int>(0, 99999)
                )
            );
            LockerSpawnPower = Plugin.config.Bind(
                "Spawn",
                "LockerSpawnPower",
                1f,
                new ConfigDescription(
                    "What's the spawn power of a Locker? How much does it subtract from the moon power pool on spawn?"
                )
            );
            LockerSpawnMax = Plugin.config.Bind(
                "Spawn",
                "LockerSpawnMax",
                3,
                new ConfigDescription(
                    "What's the maximum amount of Lockers that can spawn on a given moon?"
                )
            );
            LockerSpawnLevelsSet = Plugin.config.Bind(
                "Spawn",
                "LockerSpawnLevelsSet",
                "all",
                new ConfigDescription(
                    "Which set of levels should by default let the Locker spawn on them? (Options are: all/none/modded/vanilla)"
                )
            );
            LockerSpawnLevelsWithWeight = Plugin.config.Bind(
                "Spawn",
                "LockerSpawnLevels",
                "experimentation:25, assurance:75, vow:0, march:0, offense:100",
                new ConfigDescription(
                    "Which specific moons/levels can the Locker spawn on and with what weight? (This takes priority over the level set config option - names are matched leniently and case insensitive)"
                )
            );

            LockerVolumeAdjustment = Plugin.config.Bind(
                "Volume",
                "LockerVolumeAdjustment",
                1.0f,
                new ConfigDescription(
                    "Client side volume adjustment - values are a percentage i.e. 50% volume is 0.5"
                )
            );
        }
    }
}
