using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LethalLib.Modules;
using UnityEngine;

namespace Locker
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.zealsprince.locker";
        public const string ModName = "Locker";
        public const string ModVersion = "1.4.0";

        // These need to be lowercase because we're passing through the protected properties.
        public static ManualLogSource logger;
        public static ConfigFile config;

        private readonly Harmony harmony = new(ModGUID);

        public void Awake()
        {
            logger = Logger;
            config = Config;

            Locker.Config.Load();

            // Make sure asset loading is completed successfully and abort otherwise.
            if (Assets.Load() != Assets.LoadStatusCode.Success)
                return;

            EnemyType lockerEnemy = Assets.Bundle.LoadAsset<EnemyType>(
                "assets/exported/locker/enemies/lockerenemy.asset"
            );

            TerminalNode lockerTerminalNode = Assets.Bundle.LoadAsset<TerminalNode>(
                "assets/exported/locker/enemies/lockerterminalnode.asset"
            );

            TerminalKeyword lockerTerminalKeyword = Assets.Bundle.LoadAsset<TerminalKeyword>(
                "assets/exported/locker/enemies/lockerterminalkeyword.asset"
            );

            NetworkPrefabs.RegisterNetworkPrefab(lockerEnemy.enemyPrefab);

            // Custom config overrides.
            lockerEnemy.PowerLevel = Locker.Config.LockerSpawnPower.Value;
            lockerEnemy.MaxCount = Locker.Config.LockerSpawnMax.Value;

            // Construct options and a key match result to allow clean switching over formatted strings.
            string[] keyMatchOptions = ["all", "modded", "vanilla", "none"];
            string keyMatchResult = keyMatchOptions.FirstOrDefault(s =>
                new string( // Break up our lowercase string into characters and prune any that match whitespace.
                    Locker
                        .Config.LockerSpawnLevelsSet.Value.ToLower()
                        .ToCharArray()
                        .Where(c => !Char.IsWhiteSpace(c))
                        .ToArray()
                ).Contains(s) // Rejoin the character array and see if it contains our matched noun.
            );

            // Try matching our level set from the config. Default to no levels as we can always use the override.
            Levels.LevelTypes matchedLevelSet = Levels.LevelTypes.None;
            switch (keyMatchResult)
            {
                case "all":
                    matchedLevelSet = Levels.LevelTypes.All;
                    break;

                case "modded":
                    matchedLevelSet = Levels.LevelTypes.Modded;
                    break;

                case "vanilla":
                    matchedLevelSet = Levels.LevelTypes.Vanilla;
                    break;

                default:
                    break;
            }

            Dictionary<string, int> levelWeightOverrides = [];

            // Same as earlier. Break up our lowercase string into characters and prune any that match whitespace.
            string levelWeightOverridesCleaned =
                new(
                    Locker
                        .Config.LockerSpawnLevelsWithWeight.Value.ToLower()
                        .ToCharArray()
                        .Where(c => !Char.IsWhiteSpace(c))
                        .ToArray()
                );

            // Iterate over each level override pair.
            foreach (string levelWeightPair in levelWeightOverridesCleaned.Split(','))
            {
                string[] values = levelWeightPair.Split(':');
                if (values.Length == 1) // If we only have a level/moon name, add an override with default weight.
                {
                    levelWeightOverrides.Add(values[0], Locker.Config.LockerSpawnWeight.Value);
                }
                else if (values.Length == 2)
                {
                    int weight = 0;
                    try
                    {
                        weight = int.Parse(values[1]);
                    }
                    catch (Exception ex)
                    {
                        if (
                            ex is ArgumentException
                            || ex is FormatException
                            || ex is OverflowException
                        )
                        {
                            logger.LogError($"Failed to parse level/moon weight value: {ex}");
                        }

                        continue;
                    }

                    levelWeightOverrides.Add(values[0], weight);
                }
            }

            // Register our enemy with no levels so it shows up in the debug menu.
            // This is a bug/issue in LethalLib that doesn't register the enemy correctly outside of the base overload.
            // https://github.com/EvaisaDev/LethalLib/blob/main/LethalLib/Modules/Enemies.cs#L372-L405
            // Reference the fact that spawnableEnemies.Add(spawnableEnemy) is not called at the end of this method.
            Enemies.RegisterEnemy(
                lockerEnemy,
                Locker.Config.LockerSpawnWeight.Value,
                Levels.LevelTypes.None,
                Enemies.SpawnType.Default,
                lockerTerminalNode,
                lockerTerminalKeyword
            );

            // Need to do this to not keep the none level type registered.
            Enemies.RemoveEnemyFromLevels(lockerEnemy);

            // Register our enemy and set spawn options from the config.
            Enemies.RegisterEnemy(
                lockerEnemy,
                Enemies.SpawnType.Default,
                new Dictionary<Levels.LevelTypes, int>
                {
                    [matchedLevelSet] = Locker.Config.LockerSpawnWeight.Value
                },
                levelWeightOverrides,
                lockerTerminalNode,
                lockerTerminalKeyword
            );

            // Unity Netcode Patcher code - read more about it here and what to do to patch your mod:
            // https://github.com/EvaisaDev/UnityNetcodePatcher
            try
            {
                var types = Assembly.GetExecutingAssembly().GetTypes();
                foreach (var type in types)
                {
                    var methods = type.GetMethods(
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                    );
                    foreach (var method in methods)
                    {
                        var attributes = method.GetCustomAttributes(
                            typeof(RuntimeInitializeOnLoadMethodAttribute),
                            false
                        );
                        if (attributes.Length > 0)
                        {
                            method.Invoke(null, null);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e);
            }

            harmony.PatchAll();
        }
    }
}
