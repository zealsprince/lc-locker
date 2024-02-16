﻿using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Locker
{
    internal class Assets
    {
        public static AssetBundle Bundle;
        public static Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

        // Store all the assets here so we don't have to grep the code for it.
        public static readonly Dictionary<string, string> Manifest = new Dictionary<
            string,
            string
        >()
        {
            // {},
        };

        public enum LoadStatusCode : ushort
        {
            Success = 0,
            Failed = 1,
            Exists = 2,
        }

        public static LoadStatusCode Load()
        {
            // Make sure we don't double load.
            if (Bundle != null)
                return LoadStatusCode.Exists;

            string path = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Plugin.ModName
            );

            Bundle = AssetBundle.LoadFromFile(path);
            if (Bundle == null)
            {
                Plugin.logger.LogInfo($"Failed to load asset bundle from path: {path}");

                return LoadStatusCode.Failed;
            }

            Plugin.logger.LogInfo($"Loaded asset bundle from path: {path}");

            foreach (string asset in Bundle.GetAllAssetNames())
            {
                Plugin.logger.LogDebug($"Loaded asset: {asset}");
            }

            // Iterate over our manifest and load the assets.
            foreach (KeyValuePair<string, string> entry in Manifest)
            {
                // Add prefab definitions.
                Prefabs.Add(entry.Key, Bundle.LoadAsset<GameObject>(entry.Value));
            }

            return LoadStatusCode.Success;
        }

        public static GameObject SpawnPrefab(string name, Vector3 position)
        {
            // Make sure we don't brick anything if we incorrectly load a prefab.
            try
            {
                if (Prefabs.ContainsKey(name))
                {
                    Plugin.logger.LogDebug($"Loading prefab '{name}'");

                    var gameObject = UnityEngine.Object.Instantiate(
                        Prefabs[name],
                        position,
                        Quaternion.identity
                    );

                    return gameObject;

                    // item.GetComponent<NetworkObject>().Spawn();
                }
                else
                {
                    Plugin.logger.LogWarning($"Prefab {name} not found!");
                }
            }
            catch (System.Exception e)
                when (e is System.ArgumentException || e is System.NullReferenceException)
            {
                Plugin.logger.LogError(e.Message);
            }

            return null;
        }
    }
}
