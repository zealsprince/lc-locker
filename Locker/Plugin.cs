﻿using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Locker
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.zealsprince.locker";
        public const string ModName = "Locker";
        public const string ModVersion = "0.1.0";

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

            harmony.PatchAll();
        }
    }
}
