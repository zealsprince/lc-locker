using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LethalLib.Modules;
using UnityEngine;

namespace Locker
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.zealsprince.locker";
        public const string ModName = "Locker";
        public const string ModVersion = "0.2.0";

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

            NetworkPrefabs.RegisterNetworkPrefab(lockerEnemy.enemyPrefab);

            Enemies.RegisterEnemy(
                lockerEnemy,
                25,
                Levels.LevelTypes.All,
                Enemies.SpawnType.Default,
                new TerminalNode(),
                new TerminalKeyword()
            );

            harmony.PatchAll();
        }
    }
}
