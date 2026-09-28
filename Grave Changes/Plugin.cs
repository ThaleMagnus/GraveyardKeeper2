using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace GraveChanges
{
    [BepInPlugin("com.thalemagnus.gravechanges", "Grave Changes ", "1.2.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static Plugin instance;
        private Harmony harmony;
        private ConfigEntry<bool> graves, decorations, ignoreSkulls;
        private readonly Dictionary<ItemDef, int> originalItems = new Dictionary<ItemDef, int>();
        private readonly Dictionary<WGODef, LazyExpression> originalObjects = new Dictionary<WGODef, LazyExpression>();
        private static readonly string[] skip = { "grave_empty", "_place", "place_", "grave_corp", "grave_exhume", "grave_ground" };
        private void Awake()
        {
            instance = this;
            graves = Config.Bind("Changes", "Modify Grave Items", true, "Set eligible grave decoration item quality to 30.");
            decorations = Config.Bind("Changes", "Modify Placed Decorations", false, "Set placed grave decoration quality to 30.");
            ignoreSkulls = Config.Bind("Changes", "Ignore Body Skull Limit", true, "Allow grave decoration quality past the body's white skull cap.");
            graves.SettingChanged += (s, e) => Apply();
            decorations.SettingChanged += (s, e) => Apply();
            harmony = new Harmony("com.thalemagnus.gravechanges");
            harmony.PatchAll(typeof(Plugin));
        }
        private void OnDestroy() { if (harmony != null) harmony.UnpatchSelf(); if (instance == this) instance = null; }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameBalance), "LoadGameBalance")]
        private static void BalanceLoaded() { if (instance != null) instance.Apply(); }
        private void Apply()
        {
            var balance = GameBalance.Me;
            if (balance == null) return;
            foreach (var item in balance.itemDefs.Where(x => x != null && x.id.StartsWith("grave", StringComparison.OrdinalIgnoreCase) && !skip.Any(y => x.id.Contains(y))))
            {
                if (!originalItems.ContainsKey(item)) originalItems[item] = item.quality;
                item.quality = graves.Value ? 30 : originalItems[item];
            }
            foreach (var wgo in balance.wgoDefs.Where(x => x != null && x.id.StartsWith("grave", StringComparison.OrdinalIgnoreCase) && !skip.Any(y => x.id.Contains(y))))
            {
                if (!originalObjects.ContainsKey(wgo)) originalObjects[wgo] = wgo.quality;
                wgo.quality = decorations.Value ? new LazyExpression("30") : originalObjects[wgo];
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Inventory), "GetTotalQualityGrave")]
        private static void GraveQuality(Inventory __instance, ref float __result)
        {
            if (instance == null || !instance.ignoreSkulls.Value || __instance == null || __instance.Data == null) return;
            float quality = 0f;
            int red = 0;
            foreach (var item in __instance.Data.Inventory)
            {
                if (item == null || item.Definition == null) continue;
                quality += item.Definition.quality;
                if (item.Definition.itemGroupIds == null || !item.Definition.itemGroupIds.Contains("body")) continue;
                foreach (var organ in item.Inventory)
                    if (organ != null && organ.Definition != null)
                        red += organ.Definition.redSkulls * organ.Count;
            }
            __result = Math.Max(-999f, quality - Math.Min(999, red));
        }
    }
}
