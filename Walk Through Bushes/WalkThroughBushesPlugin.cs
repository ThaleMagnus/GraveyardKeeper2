using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace WalkThroughBushes
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    public sealed class WalkThroughBushesPlugin : BaseUnityPlugin
    {
        public const string PluginId = "com.thalemagnus.gk2.walkthroughbushes";
        public const string PluginName = "Walk Through Bushes - Graveyard Keeper 2";
        public const string PluginVersion = "2.0.0";

        private sealed class CollisionPair
        {
            internal Collider Player;
            internal Collider Bush;
            internal bool Seen;
        }

        private ConfigEntry<bool> enabledSetting;
        private ConfigEntry<float> scanRadius;
        private ConfigEntry<float> scanInterval;
        private readonly List<CollisionPair> ownedPairs = new List<CollisionPair>();
        private readonly HashSet<Collider> playerColliders = new HashSet<Collider>();
        private Collider[] nearby = new Collider[128];
        private float nextScanAt;
        private PlayerController trackedPlayer;

        private void Awake()
        {
            enabledSetting = Config.Bind(
                "General", "Enabled", true,
                "Allow the player to walk through ordinary and collectable bush colliders. Interaction triggers remain enabled.");
            scanRadius = Config.Bind(
                "General", "Scan Radius", 8f,
                new ConfigDescription("Radius around the player checked for bushes.",
                    new AcceptableValueRange<float>(4f, 20f)));
            scanInterval = Config.Bind(
                "General", "Scan Interval", 0.15f,
                new ConfigDescription("Seconds between collision scans.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            enabledSetting.SettingChanged += OnSettingChanged;
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void Update()
        {
            if (!enabledSetting.Value)
                return;
            if (Time.unscaledTime < nextScanAt)
                return;

            nextScanAt = Time.unscaledTime + scanInterval.Value;
            try
            {
                RefreshIgnoredCollisions();
            }
            catch (Exception ex)
            {
                RestoreAll();
                enabled = false;
                Logger.LogError("Walk Through Bushes stopped because of an error: " + ex);
            }
        }

        private void RefreshIgnoredCollisions()
        {
            MainGame game = MainGame.Instance;
            PlayerController player = game == null || game.gameState != MainGame.GameState.InGame
                ? null
                : MainGame.PlayerController;

            if (player == null || player.PhysicalBody == null)
            {
                trackedPlayer = null;
                RestoreAll();
                return;
            }

            if (trackedPlayer != player)
            {
                RestoreAll();
                trackedPlayer = player;
            }

            foreach (CollisionPair pair in ownedPairs)
                pair.Seen = false;

            playerColliders.Clear();
            Collider[] bodies = player.PhysicalBody.GetComponentsInChildren<Collider>(true);
            foreach (Collider body in bodies)
            {
                if (body != null && body.enabled && !body.isTrigger)
                    playerColliders.Add(body);
            }

            if (playerColliders.Count == 0)
            {
                RestoreAll();
                return;
            }

            Vector3 center = player.PhysicalBody.transform.position;
            int count;
            while (true)
            {
                count = Physics.OverlapSphereNonAlloc(
                    center, scanRadius.Value, nearby, ~0, QueryTriggerInteraction.Collide);
                if (count < nearby.Length)
                    break;
                Array.Resize(ref nearby, nearby.Length * 2);
            }

            for (int i = 0; i < count; i++)
            {
                Collider bush = nearby[i];
                if (bush == null || !bush.enabled || bush.isTrigger ||
                    playerColliders.Contains(bush) || !IsBushCollider(bush))
                    continue;

                foreach (Collider body in playerColliders)
                    OwnCollisionPair(body, bush);
            }

            Array.Clear(nearby, 0, count);
            for (int i = ownedPairs.Count - 1; i >= 0; i--)
            {
                if (!ownedPairs[i].Seen)
                {
                    RestorePair(ownedPairs[i]);
                    ownedPairs.RemoveAt(i);
                }
            }
        }

        private static bool IsBushCollider(Collider collider)
        {
            for (Transform current = collider.transform; current != null; current = current.parent)
            {
                Wgo wgo = current.GetComponent<Wgo>();
                if (wgo != null)
                {
                    if (wgo.Data != null && wgo.Data.Definition != null &&
                        BushRules.IsBushGroup(wgo.Data.Definition.wgoGroup))
                        return true;
                    if (BushRules.LooksLikeBush(wgo.Id))
                        return true;
                    return false;
                }

                if (BushRules.LooksLikeBush(current.name))
                    return true;
            }

            return false;
        }

        private void OwnCollisionPair(Collider player, Collider bush)
        {
            CollisionPair found = null;
            foreach (CollisionPair pair in ownedPairs)
            {
                if (pair.Player == player && pair.Bush == bush)
                {
                    found = pair;
                    break;
                }
            }

            if (found == null)
            {
                if (Physics.GetIgnoreCollision(player, bush))
                    return;
                found = new CollisionPair { Player = player, Bush = bush };
                ownedPairs.Add(found);
            }

            found.Seen = true;
            if (!Physics.GetIgnoreCollision(player, bush))
                Physics.IgnoreCollision(player, bush, true);
        }

        private static void RestorePair(CollisionPair pair)
        {
            if (pair.Player != null && pair.Bush != null)
                Physics.IgnoreCollision(pair.Player, pair.Bush, false);
        }

        private void RestoreAll()
        {
            foreach (CollisionPair pair in ownedPairs)
                RestorePair(pair);
            ownedPairs.Clear();
            playerColliders.Clear();
        }

        private void OnSettingChanged(object sender, EventArgs args)
        {
            if (!enabledSetting.Value)
                RestoreAll();
            else
                nextScanAt = 0f;
        }

        private void OnDisable()
        {
            RestoreAll();
        }

        private void OnDestroy()
        {
            RestoreAll();
            if (enabledSetting != null)
                enabledSetting.SettingChanged -= OnSettingChanged;
        }
    }
}
