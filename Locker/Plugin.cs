using System;
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
        public const string ModVersion = "0.11.0";

        // These need to be lowercase because we're passing through the protected properties.
        public static ManualLogSource logger;
        public static ConfigFile config;

        private readonly Harmony harmony = new Harmony(ModGUID);

        private void Awake()
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

            Enemies.RegisterEnemy(
                lockerEnemy,
                Locker.Config.LockerSpawnWeight.Value,
                Levels.LevelTypes.All,
                Enemies.SpawnType.Default,
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
            catch (Exception e) { }

            harmony.PatchAll();
        }
    }
}
