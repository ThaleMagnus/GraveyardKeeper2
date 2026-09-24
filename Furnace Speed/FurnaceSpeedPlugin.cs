using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace FurnaceSpeed
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    public sealed class FurnaceSpeedPlugin : BaseUnityPlugin
    {
        public const string PluginId = "com.thalemagnus.gk2.furnacespeed";
        public const string PluginName = "Furnace Speed - Graveyard Keeper 2";
        public const string PluginVersion = "2.0.0";

        private static FurnaceSpeedPlugin instance;
        private ConfigEntry<float> multiplier;
        private Harmony harmony;

        private void Awake()
        {
            instance = this;
            multiplier = Config.Bind(
                "General",
                "Speed Multiplier",
                2f,
                new ConfigDescription(
                    "Automatic furnace crafting speed. Fuel and ingredients per recipe are unchanged.",
                    new AcceptableValueRange<float>(1f, 10f)));

            try
            {
                harmony = new Harmony(PluginId);
                PatchDeltaMethod("Update");
                PatchDeltaMethod("PreFinishUpdate");
                Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            }
            catch (Exception ex)
            {
                harmony?.UnpatchSelf();
                enabled = false;
                Logger.LogError("Furnace Speed could not initialize: " + ex);
            }
        }

        private void PatchDeltaMethod(string methodName)
        {
            var target = AccessTools.DeclaredMethod(typeof(CraftComponent), methodName, new[] { typeof(float) });
            if (target == null)
                throw new MissingMethodException("CraftComponent." + methodName + "(float)");

            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(FurnaceSpeedPlugin), nameof(ScaleFurnaceDelta)));
        }

        private static void ScaleFurnaceDelta(CraftComponent __instance, ref float __0)
        {
            FurnaceSpeedPlugin plugin = instance;
            if (plugin == null || !plugin.isActiveAndEnabled || !FurnaceRules.IsFurnace(__instance))
                return;

            __0 = FurnaceRules.ScaleDelta(__0, plugin.multiplier.Value);
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            if (ReferenceEquals(instance, this))
                instance = null;
        }
    }
}
