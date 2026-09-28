using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GetOuttaMaWay
{
    [BepInPlugin("com.thalemagnus.getouttamaway", "Get Outta Ma Way", "1.2.1")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static Plugin instance;
        private Harmony harmony;
        private ConfigEntry<bool> npcCollision, heavyCollision, allDrops, dropAway, grace;
        private ConfigEntry<float> graceSeconds;
        private readonly List<DropView> activeDrops = new List<DropView>();
        private readonly Dictionary<DropView, float> graceExpires = new Dictionary<DropView, float>();
        private float nextRefresh;
        private void Awake()
        {
            instance = this;
            npcCollision = Config.Bind("Collision", "NPC Collision", false, "Allow NPCs to block the player when enabled.");
            npcCollision.SettingChanged += (sender, args) =>
            {
                foreach (var part in UnityEngine.Object.FindObjectsOfType<WgoPart>(true))
                    if (part != null && part.Wgo != null && part.Wgo.Data != null &&
                        IsMobileCharacter(part.Wgo.Data))
                        IgnoreWithPlayer(part, !npcCollision.Value);
            };
            heavyCollision = Config.Bind("Collision", "Walk Through Heavy Drops", true, "Ignore physical collision with large dropped items.");
            allDrops = Config.Bind("Collision", "Include All Drops", false, "Also ignore physical collision with small item drops.");
            grace = Config.Bind("Collision", "Heavy Drop Grace Period", true, "Temporarily ignore a newly dropped heavy when permanent walk-through is disabled.");
            graceSeconds = Config.Bind("Collision", "Grace Period Seconds", 1.5f,
                new ConfigDescription("Seconds of collision grace.", new AcceptableValueRange<float>(.25f, 5f)));
            dropAway = Config.Bind("Drops", "Drop Heavies Away From Player", true, "Nudge big item spawn positions away from the player.");
            harmony = new Harmony("com.thalemagnus.getouttamaway");
            harmony.PatchAll(typeof(Plugin));
        }
        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            if (instance == this) instance = null;
        }
        private static Collider[] PlayerColliders()
        {
            var controller = MainGame.Instance != null ? MainGame.PlayerController : null;
            return controller != null && controller.PhysicalBody != null
                ? controller.PhysicalBody.GetComponentsInChildren<Collider>(true) : new Collider[0];
        }
        private static bool IsMobileCharacter(WgoData data)
        {
            if (data == null || data.id == null) return false;
            return data.id.StartsWith("npc_", StringComparison.OrdinalIgnoreCase) ||
                data.MovementComponent != null &&
                (data.id.StartsWith("zombie_", StringComparison.OrdinalIgnoreCase) ||
                 data.id.StartsWith("mob_", StringComparison.OrdinalIgnoreCase));
        }
        private static void IgnoreWithPlayer(Component component, bool ignore)
        {
            if (component == null) return;
            foreach (var part in component.GetComponentsInChildren<Collider>(true))
                foreach (var player in PlayerColliders())
                    if (part != null && player != null && part != player)
                        Physics.IgnoreCollision(part, player, ignore);
        }

        private static bool TryGetDropSize(DropView drop, out ItemSize size)
        {
            size = default(ItemSize);
            if (drop == null || !drop.isActiveAndEnabled || drop.IsDespawning) return false;
            var data = drop.Data;
            if (data == null || data.IsRemoving) return false;
            var item = data.Item;
            if (item == null || item.Definition == null) return false;
            size = item.Definition.itemSize;
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DropView), "SpawnDrop")]
        private static void DropSpawned(DropView __result)
        {
            ItemSize size;
            if (instance == null || !TryGetDropSize(__result, out size)) return;
            bool big = size == ItemSize.Big;
            bool eligible = (instance.heavyCollision.Value && big) || instance.allDrops.Value ||
                (instance.grace.Value && big);
            if (!eligible) return;
            IgnoreWithPlayer(__result, true);
            if (big) instance.graceExpires[__result] = Time.realtimeSinceStartup + instance.graceSeconds.Value;
            if (!instance.activeDrops.Contains(__result)) instance.activeDrops.Add(__result);
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DropSystem), "DropItem")]
        private static void RedirectDrop(Item __0, ref Vector3 __2)
        {
            if (instance == null || !instance.dropAway.Value || __0 == null || __0.Definition == null ||
                __0.Definition.itemSize != ItemSize.Big) return;
            var controller = MainGame.Instance != null ? MainGame.PlayerController : null;
            if (controller == null || controller.PhysicalBody == null) return;
            Vector3 fromPlayer = __2 - controller.PhysicalBody.transform.position;
            fromPlayer.y = 0;
            if (fromPlayer.sqrMagnitude < 0.01f) fromPlayer = controller.PhysicalBody.transform.forward;
            __2 += fromPlayer.normalized * 0.5f;
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(WgoPart), "InitVisuals")]
        private static void NpcInitialized(WgoPart __instance)
        {
            if (instance == null || instance.npcCollision.Value || __instance == null || __instance.Wgo == null ||
                __instance.Wgo.Data == null || __instance.Wgo.Data.id == null) return;
            if (IsMobileCharacter(__instance.Wgo.Data))
                IgnoreWithPlayer(__instance, true);
        }
        private void Update()
        {
            // Pooling or a changed setting restores collision flags. Reapply while active.
            if (Time.realtimeSinceStartup < nextRefresh) return;
            nextRefresh = Time.realtimeSinceStartup + .5f;
            if (heavyCollision.Value || allDrops.Value)
                foreach (var existing in UnityEngine.Object.FindObjectsOfType<DropView>(true))
                {
                    ItemSize size;
                    if (TryGetDropSize(existing, out size) &&
                        (allDrops.Value || size == ItemSize.Big) && !activeDrops.Contains(existing))
                        activeDrops.Add(existing);
                }
            for (int i = activeDrops.Count - 1; i >= 0; i--)
            {
                var drop = activeDrops[i];
                ItemSize size;
                if (!TryGetDropSize(drop, out size))
                {
                    activeDrops.RemoveAt(i);
                    if (drop != null) graceExpires.Remove(drop);
                    continue;
                }
                float until;
                bool temporary = grace.Value && graceExpires.TryGetValue(drop, out until) &&
                    Time.realtimeSinceStartup < until;
                bool ignore = allDrops.Value || (heavyCollision.Value && size == ItemSize.Big) || temporary;
                IgnoreWithPlayer(drop, ignore);
                if (!ignore) { activeDrops.RemoveAt(i); graceExpires.Remove(drop); }
            }
        }
    }
}
